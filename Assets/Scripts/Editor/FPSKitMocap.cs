#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Captured animation, the Unity half (Docs/DESIGN_NOTES.md "Captured animation"). Tools/mocap tracks a phone video
    /// of Ishaan acting a move and cleans it into joint positions in metres, standing on the ground (motion.json);
    /// this puts that motion on our Humanoid soldier and saves it as a Humanoid clip, which plays on the enemies and on
    /// the player's body alike (both are the Survivalist rig).
    ///
    /// How a frame is put on the rig (no bone's own axes are assumed; everything is measured on the rig first):
    /// - The pelvis, chest and head are turned to the performer's (from the hips, shoulders and ears), the bend between
    ///   pelvis and chest shared out over the spine bones, the head's measured from the take's own neutral pose.
    /// - Each arm and leg takes the performer's directions at the rig's own lengths: a two-bone reach to where the
    ///   wrist or ankle falls, then the upper bone turned about itself until the rig's elbow or knee hinge (measured
    ///   from its own muscles) lies across the bend, so the joint only ever bends the way it can.
    /// - Planted feet (from the solver's contacts) are locked where they stand, eased in and out, the hips lowered
    ///   where a locked foot would otherwise be out of reach; a foot is never let through the ground.
    /// - The pose is read back as Humanoid muscles; any outside the rig's limits is counted and clamped (that is the
    ///   "natural joint limits": elbows and knees not bending backwards, the neck not twisting further than a neck).
    /// - Fingers from the hand model where it saw the hand, else the settings' hand shape.
    /// Then trimmed, eased in and out, sped up or calmed down, looped or mirrored, and saved at 60 frames a second with
    /// a key on every frame (no keyframe reduction). The numbers the result is judged by are measured on the saved clip
    /// as it plays on the rig.
    ///
    ///   Tools/mocap/mocap.sh process player_punch_01        (runs this as FPSKitMocap.Bake -mocapTake player_punch_01)
    /// </summary>
    public static partial class FPSKitMocap
    {
        public const string Root = "Assets/Animations/Captured";
        const string RigPrefab = "Assets/Survivalist/Prefab/Survivalist (1).prefab";
        const string ModelFile = "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx";
        const string RiflePrefab = "Assets/FPSKit_Generated/ViewModels/VM_AK74.prefab";
        const float Rate = 60f;

        static string WorkDir => Path.GetFullPath("Tools/mocap/work");

        // MediaPipe Pose's points (Tools/mocap/mocap/common.py POSE_NAMES).
        const int Nose = 0, LEye = 2, REye = 5, LEar = 7, REar = 8, LShoulder = 11, RShoulder = 12, LElbow = 13, RElbow = 14,
                  LWrist = 15, RWrist = 16, LHip = 23, RHip = 24, LKnee = 25, RKnee = 26, LAnkle = 27, RAnkle = 28,
                  LHeel = 29, RHeel = 30, LFoot = 31, RFoot = 32;

        /// <summary>Tools/mocap's motion.json (solve.py), read with JsonUtility: flat arrays only.</summary>
        [Serializable]
        class Motion
        {
            public string take, calibrationName;
            public float fps, height, scale, autoStart, autoEnd, focalPixels, jerkRaw, jerkClean;
            public int frames, filledFrames, filledPoints, lostFrames, videoWidth, videoHeight;
            public bool calibrated, groundFromFeet;
            public float[] positions;
            public int[] contactLeft, contactRight, handLeftSeen, handRightSeen;
            public float[] handLeftBends, handRightBends, handLeftSpread, handRightSpread, handLeftFrame, handRightFrame;
            public float[] cameraPosition, cameraForward, cameraUp;
            public float cameraFov;

            public Vector3 P(int f, int j)
            {
                int i = (f * 33 + j) * 3;
                return new Vector3(positions[i], positions[i + 1], positions[i + 2]);
            }

            public Vector3 Mid(int f, int a, int b) => 0.5f * (P(f, a) + P(f, b));

            public Vector3 V(float[] a, int f, int k) => a == null || a.Length < (f + 1) * 6 ? Vector3.zero
                : new Vector3(a[f * 6 + k * 3], a[f * 6 + k * 3 + 1], a[f * 6 + k * 3 + 2]);
        }

        // ==================================================================================================================
        // The rig
        // ==================================================================================================================

        /// <summary>The Survivalist soldier in its T-pose with everything the retarget needs measured on it.</summary>
        class Rig : IDisposable
        {
            public GameObject go;
            public Animator anim;
            public HumanPoseHandler handler;
            public Transform hips, spine, chest, upperChest, neck, head;
            public Limb lArm, rArm, lLeg, rLeg;
            public Transform lFootToes, rFootToes;
            public LimbIK.HandFrame lHand, rHand;
            public Transform[] fingers;     // per side, per finger, the proximal bone (for the hand frame)

            Transform[] _all;
            Vector3[] _pos;
            Quaternion[] _rot;

            public Quaternion hipsRef, pelvisRef, chestFrameRef, topRef, headRef;
            public Vector3 hipsOffsetRef;
            public float legLength, height, torsoLength;
            public Vector3 lHeel, lToe, rHeel, rToe;            // sole points, in each foot's own space
            public Quaternion lFootRef, rFootRef, lFootFlat, rFootFlat;

            public class Limb
            {
                public Transform upper, lower, end;
                public float a, b;
                public Vector3 hinge;       // the joint's bending axis in the upper bone's space
                public string stretch;      // its Humanoid muscle
            }

            public Transform Top => upperChest != null ? upperChest : chest != null ? chest : spine;

            public Rig()
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
                if (prefab == null) throw new Exception(RigPrefab + " is missing");
                go = (GameObject)Object.Instantiate(prefab);
                go.name = "MocapRig";
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                anim = go.GetComponentInChildren<Animator>(true);
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman) throw new Exception("the soldier has no Humanoid avatar");
                anim.runtimeAnimatorController = null;
                anim.applyRootMotion = false;
                // Nothing sees it: the default culling would skip every clip played on it (the checks read zero).
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // The T-pose the avatar was set up in: the reference every turn is measured from.
                var importer = (ModelImporter)AssetImporter.GetAtPath(ModelFile);
                var byName = new Dictionary<string, Transform>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                foreach (var b in importer.humanDescription.skeleton)
                    if (byName.TryGetValue(b.name, out var t) && t != go.transform && t != anim.transform)
                    {
                        t.localPosition = b.position;
                        t.localRotation = b.rotation;
                    }
                _all = go.GetComponentsInChildren<Transform>(true);
                _pos = _all.Select(t => t.localPosition).ToArray();
                _rot = _all.Select(t => t.localRotation).ToArray();

                Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                hips = B(HumanBodyBones.Hips); spine = B(HumanBodyBones.Spine); chest = B(HumanBodyBones.Chest);
                upperChest = B(HumanBodyBones.UpperChest); neck = B(HumanBodyBones.Neck); head = B(HumanBodyBones.Head);
                lArm = MakeLimb(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), "Left Forearm Stretch");
                rArm = MakeLimb(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), "Right Forearm Stretch");
                lLeg = MakeLimb(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), "Left Lower Leg Stretch");
                rLeg = MakeLimb(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), "Right Lower Leg Stretch");
                lFootToes = B(HumanBodyBones.LeftToes); rFootToes = B(HumanBodyBones.RightToes);
                lHand = LimbIK.HandFrame.Measure(lArm.end, B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftIndexProximal), B(HumanBodyBones.LeftLittleProximal), false);
                rHand = LimbIK.HandFrame.Measure(rArm.end, B(HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.RightIndexProximal), B(HumanBodyBones.RightLittleProximal), true);

                hipsRef = hips.rotation;
                pelvisRef = Frame(rLeg.upper.position - lLeg.upper.position, Vector3.up);
                chestFrameRef = Frame(rArm.upper.position - lArm.upper.position, Vector3.up);
                topRef = Top.rotation;
                headRef = head.rotation;
                hipsOffsetRef = hips.position - 0.5f * (lLeg.upper.position + rLeg.upper.position);
                legLength = lLeg.a + lLeg.b;
                torsoLength = Vector3.Distance(0.5f * (lArm.upper.position + rArm.upper.position), 0.5f * (lLeg.upper.position + rLeg.upper.position));
                height = head.position.y + 0.1f;

                // The soles: under the ankle and under the toes, on the ground the T-pose stands on.
                // The T-pose stands on the root's ground (y 0), unless a foot bone sits below it.
                float ground = Mathf.Min(0f, Mathf.Min(lLeg.end.position.y, rLeg.end.position.y, lFootToes != null ? lFootToes.position.y : 99f) - 0.01f);
                (lHeel, lToe, lFootRef, lFootFlat) = Sole(lLeg.end, lFootToes, ground);
                (rHeel, rToe, rFootRef, rFootFlat) = Sole(rLeg.end, rFootToes, ground);

                handler = new HumanPoseHandler(anim.avatar, go.transform);
                foreach (var limb in new[] { lArm, rArm, lLeg, rLeg }) limb.hinge = MeasureHinge(limb);
                Reset();
            }

            (Vector3 heel, Vector3 toe, Quaternion footRef, Quaternion flat) Sole(Transform foot, Transform toes, float ground)
            {
                Vector3 ahead = toes != null ? toes.position - foot.position : Vector3.forward * 0.12f;
                ahead.y = 0f;
                Vector3 flatAhead = ahead.normalized;
                Vector3 heel = new Vector3(foot.position.x, ground, foot.position.z) - flatAhead * 0.04f;
                Vector3 toe = (toes != null ? new Vector3(toes.position.x, ground, toes.position.z) : foot.position + ahead) + flatAhead * 0.06f;
                return (foot.InverseTransformPoint(heel), foot.InverseTransformPoint(toe), foot.rotation,
                        Quaternion.LookRotation(flatAhead, Vector3.up));
            }

            Limb MakeLimb(Transform upper, Transform lower, Transform end, string muscle)
                => new Limb { upper = upper, lower = lower, end = end, a = Vector3.Distance(upper.position, lower.position),
                              b = Vector3.Distance(lower.position, end.position), stretch = muscle };

            /// <summary>
            /// The axis the elbow or knee bends about, in the upper bone's space: the joint's own muscle run from one
            /// end of its range to the other, and the bent end taken as the flexed side.
            /// </summary>
            Vector3 MeasureHinge(Limb limb)
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                int m = Array.IndexOf(HumanTrait.MuscleName, limb.stretch);
                Vector3 axis = limb.upper.InverseTransformDirection(limb.lower.position - limb.upper.position).normalized;
                Vector3 At(float v)
                {
                    var p = pose;
                    p.muscles = (float[])pose.muscles.Clone();
                    p.muscles[m] = v;
                    handler.SetHumanPose(ref p);
                    return limb.upper.InverseTransformDirection(limb.end.position - limb.lower.position).normalized;
                }
                Vector3 lo = At(-1f), hi = At(1f);
                Reset();
                Vector3 flexed = Vector3.Angle(axis, lo) > Vector3.Angle(axis, hi) ? lo : hi;
                return Vector3.Cross(axis, flexed).normalized;
            }

            public void Reset()
            {
                for (int i = 0; i < _all.Length; i++) { _all[i].localPosition = _pos[i]; _all[i].localRotation = _rot[i]; }
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            /// <summary>The chest's frame now (as the top spine bone has turned it from the T-pose).</summary>
            public Quaternion ChestNow => Top.rotation * Quaternion.Inverse(topRef) * chestFrameRef;
            public Quaternion PelvisNow => hips.rotation * Quaternion.Inverse(hipsRef) * pelvisRef;
            public Quaternion HeadNow => head.rotation * Quaternion.Inverse(headRef);

            public Vector3 Eye
            {
                get
                {
                    var l = anim.GetBoneTransform(HumanBodyBones.LeftEye);
                    var r = anim.GetBoneTransform(HumanBodyBones.RightEye);
                    if (l != null && r != null) return 0.5f * (l.position + r.position);
                    return head.position + HeadNow * new Vector3(0f, 0.09f, 0.09f);
                }
            }

            public void Dispose()
            {
                handler?.Dispose();
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        /// <summary>A rotation facing along right x up (Unity's axes), its up as near <paramref name="up"/> as it can be.</summary>
        static Quaternion Frame(Vector3 right, Vector3 up)
        {
            Vector3 fwd = Vector3.Cross(right, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            return Quaternion.LookRotation(fwd.normalized, up);
        }

        // ==================================================================================================================
        // One frame on the rig
        // ==================================================================================================================

        /// <summary>One frame of the performer, said for the rig: a place for the hips and directions for everything else.</summary>
        struct Target
        {
            public Vector3 hip;                 // between the hip joints, rig metres
            public Vector3 torso;               // the hip-to-shoulder line's direction
            public Quaternion pelvis, chest, headRel;
            public Vector3 lElbow, lFore, rElbow, rFore, lKnee, lShin, rKnee, rShin;   // unit directions
            public Vector3 lFingers, lAcross, rFingers, rAcross;
            public Quaternion lFoot, rFoot;     // the performer's foot frames (heel to toe, shin up)
            public bool lockL, lockR;
            public Vector3 lAnkle, rAnkle;      // where a locked (or lifted) ankle goes
        }

        static Target TargetFor(Motion m, int f, float k, Quaternion headNeutral)
        {
            var t = new Target();
            Vector3 midHip = m.Mid(f, LHip, RHip), midSh = m.Mid(f, LShoulder, RShoulder);
            Vector3 torsoUp = (midSh - midHip).normalized;
            t.hip = midHip * k;
            t.torso = torsoUp;
            // The lean is shared between the hip joints and the spine: half of it in the pelvis.
            t.pelvis = Frame(m.P(f, RHip) - m.P(f, LHip), Vector3.Slerp(Vector3.up, torsoUp, 0.5f));
            t.chest = Frame(m.P(f, RShoulder) - m.P(f, LShoulder), torsoUp);
            t.headRel = Quaternion.Inverse(t.chest) * HeadFrame(m, f) * Quaternion.Inverse(headNeutral);
            t.lElbow = (m.P(f, LElbow) - m.P(f, LShoulder)).normalized; t.lFore = (m.P(f, LWrist) - m.P(f, LElbow)).normalized;
            t.rElbow = (m.P(f, RElbow) - m.P(f, RShoulder)).normalized; t.rFore = (m.P(f, RWrist) - m.P(f, RElbow)).normalized;
            t.lKnee = (m.P(f, LKnee) - m.P(f, LHip)).normalized; t.lShin = (m.P(f, LAnkle) - m.P(f, LKnee)).normalized;
            t.rKnee = (m.P(f, RKnee) - m.P(f, RHip)).normalized; t.rShin = (m.P(f, RAnkle) - m.P(f, RKnee)).normalized;
            t.lFingers = m.V(m.handLeftFrame, f, 0); t.lAcross = m.V(m.handLeftFrame, f, 1);
            t.rFingers = m.V(m.handRightFrame, f, 0); t.rAcross = m.V(m.handRightFrame, f, 1);
            t.lFoot = FootFrame(m, f, LHeel, LFoot, LKnee, LAnkle);
            t.rFoot = FootFrame(m, f, RHeel, RFoot, RKnee, RAnkle);
            return t;
        }

        static Quaternion HeadFrame(Motion m, int f)
        {
            Vector3 right = m.P(f, REar) - m.P(f, LEar);
            Vector3 ahead = m.P(f, Nose) - m.Mid(f, LEar, REar);
            Vector3 up = Vector3.Cross(ahead, right);
            return Frame(right, up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up);
        }

        static Quaternion FootFrame(Motion m, int f, int heel, int toe, int knee, int ankle)
        {
            Vector3 ahead = m.P(f, toe) - m.P(f, heel);
            Vector3 up = m.P(f, knee) - m.P(f, ankle);
            if (ahead.sqrMagnitude < 1e-8f) ahead = Vector3.forward;
            return Quaternion.LookRotation(ahead.normalized, up);
        }

        /// <summary>Counts what the retarget had to correct on the way (reported with the clip).</summary>
        class Tally { public int hingeFlips; }

        /// <summary>Puts one frame on the rig. Returns nothing; the rig's bones hold the pose.</summary>
        static void Pose(Rig rig, in Target t, Tally tally)
        {
            rig.Reset();
            Quaternion pelvisTurn = t.pelvis * Quaternion.Inverse(rig.pelvisRef);
            rig.hips.rotation = pelvisTurn * rig.hipsRef;
            rig.hips.position = t.hip + pelvisTurn * rig.hipsOffsetRef;

            // The spine: its first bone leans the shoulders out along the performer's hip-to-shoulder line, the bones above
            // turn the chest to the performer's chest (the top one taking whatever is left); twice, as each moves the other.
            Vector3 hipMid = 0.5f * (rig.lLeg.upper.position + rig.rLeg.upper.position);
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 shoulders = 0.5f * (rig.lArm.upper.position + rig.rArm.upper.position);
                Vector3 want = hipMid + t.torso * rig.torsoLength;
                rig.spine.rotation = Quaternion.FromToRotation(shoulders - rig.spine.position, want - rig.spine.position) * rig.spine.rotation;
                Drive(new[] { rig.chest, rig.upperChest }, new[] { 0.5f, 1f }, t.chest, () => rig.ChestNow);
            }
            // The head, from the chest as it now stands.
            Quaternion headTarget = rig.ChestNow * t.headRel;
            Drive(new[] { rig.neck, rig.head }, new[] { 0.4f, 1f }, headTarget, () => rig.HeadNow);

            Reach(rig.lArm, t.lElbow, t.lFore, null, tally);
            Reach(rig.rArm, t.rElbow, t.rFore, null, tally);
            Hand(rig.lArm.end, rig.lHand, t.lFingers, t.lAcross, false);
            Hand(rig.rArm.end, rig.rHand, t.rFingers, t.rAcross, true);

            Reach(rig.lLeg, t.lKnee, t.lShin, t.lockL ? t.lAnkle : (Vector3?)null, tally);
            Reach(rig.rLeg, t.rKnee, t.rShin, t.lockR ? t.rAnkle : (Vector3?)null, tally);
            rig.lLeg.end.rotation = t.lFoot * Quaternion.Inverse(rig.lFootFlat) * rig.lFootRef;
            rig.rLeg.end.rotation = t.rFoot * Quaternion.Inverse(rig.rFootFlat) * rig.rFootRef;
        }

        /// <summary>Turns a chain of bones, each taking its share of what is left, until <paramref name="now"/> reads the target.</summary>
        static void Drive(Transform[] bones, float[] share, Quaternion target, Func<Quaternion> now)
        {
            int last = -1;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) last = i;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                Quaternion delta = target * Quaternion.Inverse(now());
                bones[i].rotation = Quaternion.Slerp(Quaternion.identity, delta, i == last ? 1f : share[i]) * bones[i].rotation;
            }
        }

        /// <summary>
        /// An arm or a leg along the performer's directions at the rig's own lengths (or onto a given end point), its
        /// hinge then laid across the bend so the elbow or knee bends only the way it can.
        /// </summary>
        static void Reach(Rig.Limb limb, Vector3 upperDir, Vector3 lowerDir, Vector3? endAt, Tally tally)
        {
            Vector3 s = limb.upper.position;
            Vector3 mid = s + upperDir * limb.a;
            Vector3 end = endAt ?? mid + lowerDir * limb.b;
            LimbIK.Solve(limb.upper, limb.lower, limb.end, end, mid);

            Vector3 a = (limb.lower.position - s).normalized;
            Vector3 f = end - limb.lower.position;
            if (f.sqrMagnitude < 1e-8f) return;
            f.Normalize();
            Vector3 n = Vector3.Cross(a, f);
            float bend = Vector3.Angle(a, f);
            if (n.sqrMagnitude < 1e-8f) return;
            Vector3 h = Vector3.ProjectOnPlane(limb.upper.rotation * limb.hinge, a);
            float turn = Vector3.SignedAngle(h, n.normalized, a);
            // Nearly straight, the bend's plane is noise: the hinge is only laid across a clear bend.
            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 25f, bend));
            if (Mathf.Abs(turn) > 120f && bend > 25f) tally.hingeFlips++;
            limb.upper.rotation = Quaternion.AngleAxis(turn * w, a) * limb.upper.rotation;
            limb.lower.rotation = Quaternion.FromToRotation(limb.end.position - limb.lower.position, end - limb.lower.position) * limb.lower.rotation;
        }

        static void Hand(Transform hand, LimbIK.HandFrame frame, Vector3 fingers, Vector3 across, bool right)
        {
            if (!frame.valid || fingers.sqrMagnitude < 1e-6f || across.sqrMagnitude < 1e-6f) return;
            Vector3 back = Vector3.Cross(fingers, across);
            if (right) back = -back;
            if (back.sqrMagnitude < 1e-8f) return;
            hand.rotation = frame.World(fingers.normalized, back.normalized);
        }

        // ==================================================================================================================
        // Bake
        // ==================================================================================================================

        static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        /// <summary>Batch entry: -mocapTake player_punch_01. Reads Tools/mocap/work/&lt;take&gt;/motion.json.</summary>
        public static void BakeBatch()
        {
            string take = Arg("-mocapTake");
            int code = 1;
            try
            {
                if (string.IsNullOrEmpty(take)) throw new Exception("no -mocapTake");
                Bake(take);
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap bake failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        static (string who, string move, int number) ParseTake(string take)
        {
            var parts = take.Split('_');
            if (parts.Length < 3 || !int.TryParse(parts[parts.Length - 1], out int n)) throw new Exception($"take '{take}' is not who_move_NN");
            string who = parts[0].ToLowerInvariant() == "player" ? "Player" : parts[0].ToLowerInvariant() == "enemy" ? "Enemy" : Cap(parts[0]);
            string move = string.Concat(parts.Skip(1).Take(parts.Length - 2).Select(Cap));
            return (who, move, n);
        }

        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        public static string FolderFor(string take)
        {
            var (who, move, _) = ParseTake(take);
            return $"{Root}/{who}/{move}";
        }

        /// <summary>The clip of ours that does the same move now, for the side-by-side preview.</summary>
        static AnimationClip CurrentFor(string move)
        {
            string m = move.ToLowerInvariant();
            string file = m.Contains("double") ? "Player/Punch_JabCross" : m.Contains("punch") || m.Contains("jab") ? "Player/Punch_Right"
                : m.Contains("kick") ? "Player/Kick_Front" : m.Contains("butt") ? "Player/Melee_RifleButt"
                : m.Contains("reload") ? "Actions/Reload_Stand" : m.Contains("stumble") ? "Injured/Stumble_Trip"
                : m.Contains("getup") ? "GetUp/GetUp_Back" : m.Contains("fall") ? "Injured/Fall_Backwards"
                : m.Contains("limp") ? "Injured/Limp_Walk" : m.Contains("crawl") ? "Crawl/Crawl_Low"
                : m.Contains("death") || m.Contains("die") ? "Deaths/Death_Front" : m.Contains("hit") ? "Hits/Hit_Rifle"
                : m.Contains("throw") ? "Actions/Throw_Stand" : m.Contains("idle") ? "Idle/Rifle_Idle" : null;
            return file == null ? null : LoadClip($"Assets/Animations/Mixamo/{file}.fbx");
        }

        public static AnimationClip LoadClip(string fbx)
            => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        static CapturedClipSettings SettingsFor(string take, string folder)
        {
            string path = $"{folder}/{take}_settings.asset";
            var s = AssetDatabase.LoadAssetAtPath<CapturedClipSettings>(path);
            if (s != null) return s;
            var (who, move, n) = ParseTake(take);
            s = ScriptableObject.CreateInstance<CapturedClipSettings>();
            s.who = who; s.move = move; s.take = n;
            string m = move.ToLowerInvariant();
            s.weapon = m.Contains("reload") || m.Contains("butt") || m.Contains("rifle") || m.Contains("aim");
            s.handPose = s.weapon ? 2 : m.Contains("punch") || m.Contains("jab") || m.Contains("cross") ? 1 : 0;
            s.loop = m.Contains("walk") || m.Contains("limp") || m.Contains("crawl") || m.Contains("run") || m.Contains("idle");
            s.mirror = m.Contains("limp") || m.Contains("hit") || m.Contains("stumble");
            s.firstPerson = who == "Player";
            s.currentClip = CurrentFor(move);
            AssetDatabase.CreateAsset(s, path);
            return s;
        }

        static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Everything the bake measured, for the take's info file and the report.</summary>
        [Serializable]
        public class Quality
        {
            public string take, clip, firstPerson, mirrored, settings, video, calibration, bakedOn, currentClip;
            public int frames, sourceFrames, filledFrames, filledPoints, lostFrames;
            public float seconds, trimStart, trimEnd;
            public float jerkTrackedWorst, jerkCleanWorst, jerkClipWorst, jerkCurrentClipWorst;
            public float footSlideWorstCm, footSlideMeanCm, groundPenetrationWorstCm;
            public int plantedFramesLeft, plantedFramesRight;
            public int jointLimitFrames, jointLimitValues, hingeFlips;
            public string worstMuscle;
            public float handsSeenLeft, handsSeenRight;
            public bool calibrated, groundFromFeet;
            public string striker;
            public float impact;
        }

        public static Quality Bake(string take)
        {
            string motionPath = Path.Combine(WorkDir, take, "motion.json");
            if (!File.Exists(motionPath)) throw new Exception("no " + motionPath + " (run Tools/mocap/mocap.sh process " + take + ")");
            var m = JsonUtility.FromJson<Motion>(File.ReadAllText(motionPath));
            if (m.positions == null || m.positions.Length < m.frames * 99) throw new Exception("motion.json is incomplete");

            string folder = FolderFor(take);
            EnsureFolder(folder);
            var settings = SettingsFor(take, folder);
            settings.video = m.take;
            settings.calibration = m.calibrationName ?? "";
            var q = new Quality { take = take, sourceFrames = m.frames, filledFrames = m.filledFrames, filledPoints = m.filledPoints,
                                  lostFrames = m.lostFrames, jerkTrackedWorst = m.jerkRaw, jerkCleanWorst = m.jerkClean,
                                  calibrated = m.calibrated, groundFromFeet = m.groundFromFeet, video = m.take,
                                  calibration = settings.calibration, bakedOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };

            using var rig = new Rig();
            int n = m.frames;
            float fps = m.fps > 0f ? m.fps : Rate;
            var tally = new Tally();

            // The performer's legs against the rig's: everything the hips travel is scaled by it.
            var legs = new List<float>();
            for (int f = 0; f < n; f++)
                legs.Add(Vector3.Distance(m.P(f, LHip), m.P(f, LKnee)) + Vector3.Distance(m.P(f, LKnee), m.P(f, LAnkle)));
            legs.Sort();
            float k = rig.legLength / Mathf.Max(0.3f, legs[legs.Count / 2]);

            // The head's neutral: its turn from the chest over the take's first third of a second.
            Quaternion headNeutral = Quaternion.identity;
            {
                int nf = Mathf.Clamp(Mathf.RoundToInt(0.3f * fps), 1, n);
                Vector4 sum = Vector4.zero;
                Quaternion first = Quaternion.identity;
                for (int f = 0; f < nf; f++)
                {
                    Quaternion chestF = Frame(m.P(f, RShoulder) - m.P(f, LShoulder), (m.Mid(f, LShoulder, RShoulder) - m.Mid(f, LHip, RHip)).normalized);
                    Quaternion rel = Quaternion.Inverse(chestF) * HeadFrame(m, f);
                    if (f == 0) first = rel;
                    if (Quaternion.Dot(first, rel) < 0f) rel = new Quaternion(-rel.x, -rel.y, -rel.z, -rel.w);
                    sum += new Vector4(rel.x, rel.y, rel.z, rel.w);
                }
                sum.Normalize();
                headNeutral = new Quaternion(sum.x, sum.y, sum.z, sum.w);
            }

            // Pass 1: every frame as tracked, to find where the planted feet stand.
            var targets = new Target[n];
            var ankleL = new Vector3[n]; var ankleR = new Vector3[n];
            var hipL = new Vector3[n]; var hipR = new Vector3[n];
            for (int f = 0; f < n; f++)
            {
                targets[f] = TargetFor(m, f, k, headNeutral);
                Pose(rig, targets[f], tally);
                ankleL[f] = rig.lLeg.end.position; ankleR[f] = rig.rLeg.end.position;
                hipL[f] = rig.lLeg.upper.position; hipR[f] = rig.rLeg.upper.position;
            }
            tally.hingeFlips = 0;

            bool[] plantL = Plants(m.contactLeft, n), plantR = Plants(m.contactRight, n);
            q.plantedFramesLeft = plantL.Count(b => b);
            q.plantedFramesRight = plantR.Count(b => b);
            var lockL = settings.lockFeet ? Locks(ankleL, plantL, rig.lLeg.end, rig.lHeel, rig.lToe, targets, true, rig) : null;
            var lockR = settings.lockFeet ? Locks(ankleR, plantR, rig.rLeg.end, rig.rHeel, rig.rToe, targets, false, rig) : null;

            // Where a locked foot would be out of the leg's reach, the hips come down (eased over a few frames).
            var drop = new float[n];
            for (int f = 0; f < n; f++)
            {
                float d = 0f;
                if (lockL != null) d = Mathf.Max(d, Drop(hipL[f], lockL[f].pos, rig.legLength));
                if (lockR != null) d = Mathf.Max(d, Drop(hipR[f], lockR[f].pos, rig.legLength));
                drop[f] = d;
            }
            drop = SmoothMax(drop, 4);

            // Pass 2: the final pose of every frame, read back as muscles.
            int muscleCount = HumanTrait.MuscleCount;
            var muscles = new float[n][];
            var bodyPos = new Vector3[n];
            var bodyRot = new Quaternion[n];
            var over = new int[muscleCount];
            var hp = new HumanPose();
            for (int f = 0; f < n; f++)
            {
                var t = targets[f];
                t.hip.y -= drop[f];
                if (lockL != null) { t.lockL = true; t.lAnkle = lockL[f].pos; }
                if (lockR != null) { t.lockR = true; t.rAnkle = lockR[f].pos; }
                Pose(rig, t, tally);
                rig.handler.GetHumanPose(ref hp);
                var row = (float[])hp.muscles.Clone();
                bool frameOver = false;
                for (int i = 0; i < muscleCount; i++)
                {
                    if (row[i] > 1.001f || row[i] < -1.001f) { over[i]++; q.jointLimitValues++; frameOver = true; }
                    row[i] = Mathf.Clamp(row[i], -1f, 1f);
                }
                if (frameOver) q.jointLimitFrames++;
                Fingers(row, m, f, settings.handPose);
                muscles[f] = row;
                bodyPos[f] = hp.bodyPosition;
                bodyRot[f] = f > 0 && Quaternion.Dot(bodyRot[f - 1], hp.bodyRotation) < 0f ? Neg(hp.bodyRotation) : hp.bodyRotation;
            }
            q.hingeFlips = tally.hingeFlips;
            int worst = Array.IndexOf(over, over.Max());
            q.worstMuscle = over.Max() > 0 ? $"{HumanTrait.MuscleName[worst]} ({over[worst]} frames)" : "none";

            // Trim, strength, speed, ease in and out, loop.
            float start = settings.trimStart, end = settings.trimEnd;
            if (start <= 0f && end <= 0f) { start = m.autoStart; end = m.autoEnd; }
            int a = Mathf.Clamp(Mathf.RoundToInt(start * fps), 0, n - 2);
            int b = Mathf.Clamp(end > start ? Mathf.RoundToInt(end * fps) : n - 1, a + 1, n - 1);
            q.trimStart = a / fps; q.trimEnd = b / fps;
            var clip = Resample(muscles, bodyPos, bodyRot, a, b, fps, settings, out int[] source);
            q.frames = clip.muscles.Length;
            q.seconds = (q.frames - 1) / Rate;

            string clipPath = $"{folder}/{take}.anim";
            var anim = WriteClip(clipPath, clip, settings.loop, false);
            q.clip = clipPath;
            if (settings.mirror)
            {
                string mirrorPath = $"{folder}/{take}_mirror.anim";
                WriteClip(mirrorPath, clip, settings.loop, true);
                q.mirrored = mirrorPath;
            }

            // Pass 3: the saved clip as it plays on the rig -- the numbers it is judged by, and the first-person track.
            Measure(rig, clip, source, plantL, plantR, q, settings, take, folder, anim);
            if (settings.currentClip != null) { q.jerkCurrentClipWorst = ClipJerk(rig, settings.currentClip); q.currentClip = settings.currentClip.name; }

            q.settings = AssetDatabase.GetAssetPath(settings);
            q.handsSeenLeft = m.handLeftSeen != null ? m.handLeftSeen.Count(v => v > 0) / (float)n : 0f;
            q.handsSeenRight = m.handRightSeen != null ? m.handRightSeen.Count(v => v > 0) / (float)n : 0f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            string info = JsonUtility.ToJson(new Info { quality = q, settings = JsonUtility.ToJson(settings) }, true);
            File.WriteAllText($"{folder}/{take}_info.json", info);
            File.WriteAllText(Path.Combine(WorkDir, take, "quality.json"), JsonUtility.ToJson(q, true));
            AssetDatabase.ImportAsset($"{folder}/{take}_info.json");
            Debug.Log($"[FPSKitBatch] Mocap {take}: {q.frames} frames ({q.seconds:0.00} s) -> {clipPath} | jerk worst {q.jerkClipWorst:0} m/s3 " +
                      $"(current clip {q.jerkCurrentClipWorst:0}) | foot slide {q.footSlideWorstCm:0.0} cm | ground {q.groundPenetrationWorstCm:0.0} cm | " +
                      $"joint limits {q.jointLimitFrames} frames | filled {q.filledFrames} frames");
            return q;
        }

        [Serializable] class Info { public Quality quality; public string settings; }

        static Quaternion Neg(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, -q.w);

        static bool[] Plants(int[] contact, int n)
        {
            var p = new bool[n];
            if (contact != null) for (int i = 0; i < n && i < contact.Length; i++) p[i] = contact[i] > 0;
            return p;
        }

        struct Lock { public Vector3 pos; }

        /// <summary>
        /// Where each ankle goes: in a plant, held at the plant's middle place, its sole on the ground, eased in and
        /// out over four frames; elsewhere where it was, lifted out of the ground if it went in.
        /// </summary>
        static Lock[] Locks(Vector3[] ankle, bool[] plant, Transform foot, Vector3 heel, Vector3 toe, Target[] targets, bool left, Rig rig)
        {
            int n = ankle.Length;
            var result = new Lock[n];
            Quaternion flat = left ? rig.lFootFlat : rig.rFootFlat, footRef = left ? rig.lFootRef : rig.rFootRef;
            float SoleLow(int f, Vector3 at)
            {
                Quaternion turn = (left ? targets[f].lFoot : targets[f].rFoot) * Quaternion.Inverse(flat) * footRef;
                return Mathf.Min((at + turn * heel).y, (at + turn * toe).y);
            }
            var held = new Vector3[n];
            var weight = new float[n];
            int i = 0;
            while (i < n)
            {
                if (!plant[i]) { i++; continue; }
                int j = i;
                while (j < n && plant[j]) j++;
                var xs = new List<float>(); var zs = new List<float>();
                for (int f = i; f < j; f++) { xs.Add(ankle[f].x); zs.Add(ankle[f].z); }
                xs.Sort(); zs.Sort();
                int middle = (i + j) / 2;
                Vector3 at = new Vector3(xs[xs.Count / 2], ankle[middle].y, zs[zs.Count / 2]);
                for (int f = i; f < j; f++)
                {
                    Vector3 p = at;
                    p.y -= SoleLow(f, p);                    // the sole on the ground, whatever the foot's tilt
                    held[f] = p;
                    float w = Mathf.Min(1f, Mathf.Min(f - i + 1, j - f) / 4f);
                    weight[f] = Mathf.SmoothStep(0f, 1f, w);
                }
                i = j;
            }
            for (int f = 0; f < n; f++)
            {
                Vector3 p = Vector3.Lerp(ankle[f], held[f], weight[f]);
                float low = SoleLow(f, p);
                if (low < 0f) p.y -= low;                    // never through the ground
                result[f].pos = p;
            }
            return result;
        }

        static float Drop(Vector3 hip, Vector3 ankle, float leg)
        {
            float reach = leg * 0.995f;
            Vector3 d = ankle - hip;
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat >= reach) return 0f;
            float down = Mathf.Sqrt(reach * reach - flat * flat);
            return Mathf.Max(0f, (hip.y - ankle.y) - down);
        }

        static float[] SmoothMax(float[] v, int r)
        {
            var mx = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
                for (int j = Mathf.Max(0, i - r); j <= Mathf.Min(v.Length - 1, i + r); j++) mx[i] = Mathf.Max(mx[i], v[j]);
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float s = 0f; int c = 0;
                for (int j = Mathf.Max(0, i - r); j <= Mathf.Min(v.Length - 1, i + r); j++) { s += mx[j]; c++; }
                o[i] = s / c;
            }
            return o;
        }

        // ---- fingers --------------------------------------------------------------------------------------------------

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        /// <summary>
        /// The fingers: bent as the hand model saw them where it saw the hand (eased over a few frames at the edges of
        /// what it saw), else held in the settings' shape: relaxed, a fist, or round a rifle grip.
        /// </summary>
        static void Fingers(float[] row, Motion m, int f, int shape)
        {
            for (int side = 0; side < 2; side++)
            {
                string s = side == 0 ? "Left" : "Right";
                float[] bends = side == 0 ? m.handLeftBends : m.handRightBends;
                for (int fi = 0; fi < 5; fi++)
                {
                    for (int j = 1; j <= 3; j++)
                    {
                        int mi = Array.IndexOf(HumanTrait.MuscleName, $"{s} {FingerNames[fi]} {j} Stretched");
                        if (mi < 0) continue;
                        float shapeValue = ShapeValue(shape, fi, side == 1);
                        float tracked = bends != null && bends.Length > (f * 15 + fi * 3 + j - 1) ? bends[f * 15 + fi * 3 + j - 1] : -1f;
                        if (tracked < 0f && fi == 0 && j == 3 && bends != null && bends.Length > f * 15 + 1) tracked = bends[f * 15 + 1];
                        // 0 degrees straight (+0.6), 90 curled shut (-1).
                        row[mi] = tracked >= 0f ? Mathf.Clamp(0.6f - 1.6f * tracked / 90f, -1f, 1f) : shapeValue;
                    }
                    int sp = Array.IndexOf(HumanTrait.MuscleName, $"{s} {FingerNames[fi]} Spread");
                    if (sp >= 0) row[sp] = shape == 1 ? -0.4f : shape == 2 ? -0.3f : 0f;
                }
            }
        }

        static float ShapeValue(int shape, int finger, bool right)
        {
            switch (shape)
            {
                case 1: return finger == 0 ? -0.7f : -1f;                                  // a fist
                case 2: return finger == 0 ? -0.65f : finger == 1 ? (right ? -0.35f : -0.35f) : (right ? -0.95f : -0.45f);   // round a grip
                default: return finger == 0 ? 0f : -0.25f;                                 // relaxed
            }
        }

        // ---- trim, strength, speed, ease, loop ---------------------------------------------------------------------------

        class Baked
        {
            public float[][] muscles;
            public Vector3[] pos;
            public Quaternion[] rot;
        }

        static Baked Resample(float[][] muscles, Vector3[] pos, Quaternion[] rot, int a, int b, float fps, CapturedClipSettings s, out int[] source)
        {
            int mc = muscles[0].Length;
            float speed = Mathf.Max(0.05f, s.speed);
            // Source frames per output frame: the take's own rate against 60, times the speed.
            float step = fps / Rate * speed;
            int count = Mathf.Max(2, Mathf.FloorToInt((b - a) / step) + 1);
            var o = new Baked { muscles = new float[count][], pos = new Vector3[count], rot = new Quaternion[count] };
            source = new int[count];
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Min(a + i * step, b);
                int i0 = Mathf.FloorToInt(x), i1 = Mathf.Min(i0 + 1, b);
                float t = x - i0;
                var row = new float[mc];
                for (int j = 0; j < mc; j++) row[j] = Mathf.Lerp(muscles[i0][j], muscles[i1][j], t);
                o.muscles[i] = row;
                o.pos[i] = Vector3.Lerp(pos[i0], pos[i1], t);
                o.rot[i] = Quaternion.Slerp(rot[i0], rot[i1], t);
                source[i] = Mathf.RoundToInt(x);
            }

            // Strength: each value's change from the clip's first pose scaled.
            if (Mathf.Abs(s.strength - 1f) > 1e-4f)
            {
                var m0 = (float[])o.muscles[0].Clone();
                Vector3 p0 = o.pos[0];
                Quaternion r0 = o.rot[0];
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < mc; j++) o.muscles[i][j] = Mathf.Clamp(m0[j] + (o.muscles[i][j] - m0[j]) * s.strength, -1f, 1f);
                    o.pos[i] = p0 + (o.pos[i] - p0) * s.strength;
                    o.rot[i] = r0 * Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.Inverse(r0) * o.rot[i], s.strength);
                }
            }

            // Eased out of the first pose and into the last, so the clip starts and ends still.
            int nin = Mathf.Min(count / 3, Mathf.RoundToInt(s.blendIn * Rate));
            int nout = Mathf.Min(count / 3, Mathf.RoundToInt(s.blendOut * Rate));
            if (!s.loop)
            {
                var first = (float[])o.muscles[0].Clone();
                var lastRow = (float[])o.muscles[count - 1].Clone();
                for (int i = 0; i < nin; i++)
                {
                    float w = Mathf.SmoothStep(0f, 1f, i / (float)nin);
                    for (int j = 0; j < mc; j++) o.muscles[i][j] = Mathf.Lerp(first[j], o.muscles[i][j], w);
                }
                for (int i = 0; i < nout; i++)
                {
                    int f = count - 1 - i;
                    float w = Mathf.SmoothStep(0f, 1f, i / (float)nout);
                    for (int j = 0; j < mc; j++) o.muscles[f][j] = Mathf.Lerp(lastRow[j], o.muscles[f][j], w);
                }
            }
            else
            {
                // A cycle: the gap between the last frame and the first shared out over the whole clip, so it joins.
                var gap = new float[mc];
                for (int j = 0; j < mc; j++) gap[j] = o.muscles[0][j] - o.muscles[count - 1][j];
                float gapY = o.pos[0].y - o.pos[count - 1].y;
                for (int i = 0; i < count; i++)
                {
                    float w = i / (float)(count - 1);
                    for (int j = 0; j < mc; j++) o.muscles[i][j] += gap[j] * w;
                    o.pos[i].y += gapY * w;
                }
            }
            return o;
        }

        // ---- the clip file ------------------------------------------------------------------------------------------------

        /// <summary>A Humanoid clip attribute for a muscle: the fingers are named differently in clips ("LeftHand.Index.1 Stretched").</summary>
        static string Attribute(string muscle)
        {
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in FingerNames)
                    if (muscle.StartsWith($"{side} {finger} "))
                        return $"{side}Hand.{finger}.{muscle.Substring(side.Length + finger.Length + 2)}";
            return muscle;
        }

        static AnimationClip WriteClip(string path, Baked b, bool loop, bool mirror)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool fresh = clip == null;
            if (fresh) clip = new AnimationClip();
            clip.ClearCurves();
            clip.frameRate = Rate;
            int n = b.muscles.Length;

            AnimationCurve Curve(Func<int, float> value)
            {
                var keys = new Keyframe[n];
                for (int i = 0; i < n; i++)
                {
                    float v = value(i);
                    float prev = value(Mathf.Max(0, i - 1)), next = value(Mathf.Min(n - 1, i + 1));
                    float span = (Mathf.Min(n - 1, i + 1) - Mathf.Max(0, i - 1)) / Rate;
                    float slope = span > 0f ? (next - prev) / span : 0f;
                    keys[i] = new Keyframe(i / Rate, v, slope, slope);
                }
                return new AnimationCurve(keys);
            }

            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            void Add(string attribute, Func<int, float> value)
            {
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), attribute));
                curves.Add(Curve(value));
            }
            Add("RootT.x", i => b.pos[i].x); Add("RootT.y", i => b.pos[i].y); Add("RootT.z", i => b.pos[i].z);
            Add("RootQ.x", i => b.rot[i].x); Add("RootQ.y", i => b.rot[i].y); Add("RootQ.z", i => b.rot[i].z); Add("RootQ.w", i => b.rot[i].w);
            for (int mi = 0; mi < HumanTrait.MuscleCount; mi++)
            {
                int j = mi;
                Add(Attribute(HumanTrait.MuscleName[mi]), i => b.muscles[i][j]);
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

            var cs = AnimationUtility.GetAnimationClipSettings(clip);
            cs.loopTime = loop;
            cs.mirror = mirror;
            cs.keepOriginalOrientation = true;
            cs.keepOriginalPositionY = true;
            cs.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, cs);

            if (fresh) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        // ---- measuring the result -------------------------------------------------------------------------------------------

        static void SetPose(Rig rig, Baked b, int i)
        {
            rig.Reset();
            var p = new HumanPose { bodyPosition = b.pos[i], bodyRotation = b.rot[i], muscles = b.muscles[i] };
            rig.handler.SetHumanPose(ref p);
        }

        static Vector3 Sole(Transform foot, Vector3 local) => foot.TransformPoint(local);

        static void Measure(Rig rig, Baked b, int[] source, bool[] plantL, bool[] plantR, Quality q, CapturedClipSettings s,
                            string take, string folder, AnimationClip anim)
        {
            int n = b.muscles.Length;
            var ends = new Vector3[n][];
            var soleL = new Vector3[n]; var soleR = new Vector3[n];
            float worstGround = 0f;
            var fp = ScriptableObject.CreateInstance<CapturedFirstPerson>();
            fp.leftHand = new Vector3[n]; fp.rightHand = new Vector3[n];
            fp.leftHandTurn = new Quaternion[n]; fp.rightHandTurn = new Quaternion[n];
            fp.leftFoot = new Vector3[n]; fp.rightFoot = new Vector3[n];
            fp.leftFootTurn = new Quaternion[n]; fp.rightFootTurn = new Quaternion[n];

            for (int i = 0; i < n; i++)
            {
                SetPose(rig, b, i);
                ends[i] = new[] { rig.lArm.end.position, rig.rArm.end.position, rig.lLeg.end.position, rig.rLeg.end.position, rig.head.position };
                Vector3 lh = Sole(rig.lLeg.end, rig.lHeel), lt = Sole(rig.lLeg.end, rig.lToe);
                Vector3 rh = Sole(rig.rLeg.end, rig.rHeel), rt = Sole(rig.rLeg.end, rig.rToe);
                soleL[i] = 0.5f * (lh + lt); soleR[i] = 0.5f * (rh + rt);
                worstGround = Mathf.Max(worstGround, -Mathf.Min(Mathf.Min(lh.y, lt.y), Mathf.Min(rh.y, rt.y)));

                // First person: the hands from the eye along the chest's axes; the feet about the pelvis.
                Quaternion chest = rig.ChestNow, toChest = Quaternion.Inverse(chest);
                Vector3 eye = rig.Eye;
                fp.leftHand[i] = toChest * (rig.lArm.end.position - eye);
                fp.rightHand[i] = toChest * (rig.rArm.end.position - eye);
                fp.leftHandTurn[i] = toChest * rig.lArm.end.rotation * Quaternion.Inverse(rig.lHand.toLocal);
                fp.rightHandTurn[i] = toChest * rig.rArm.end.rotation * Quaternion.Inverse(rig.rHand.toLocal);
                Quaternion pelvis = rig.PelvisNow, toPelvis = Quaternion.Inverse(pelvis);
                Vector3 hipMid = 0.5f * (rig.lLeg.upper.position + rig.rLeg.upper.position);
                fp.leftFoot[i] = toPelvis * (rig.lLeg.end.position - hipMid);
                fp.rightFoot[i] = toPelvis * (rig.rLeg.end.position - hipMid);
                fp.leftFootTurn[i] = toPelvis * rig.lLeg.end.rotation;
                fp.rightFootTurn[i] = toPelvis * rig.rLeg.end.rotation;
            }
            rig.Reset();

            q.jerkClipWorst = Jerk(ends);
            q.groundPenetrationWorstCm = worstGround * 100f;

            // Foot slide: in each plant, how far the sole wanders from where it stands.
            float worstSlide = 0f, sumSlide = 0f; int plants = 0;
            foreach (var (plant, sole) in new[] { (plantL, soleL), (plantR, soleR) })
            {
                int i = 0;
                while (i < n)
                {
                    if (!plant[Mathf.Clamp(source[i], 0, plant.Length - 1)]) { i++; continue; }
                    int j = i;
                    while (j < n && plant[Mathf.Clamp(source[j], 0, plant.Length - 1)]) j++;
                    if (j - i >= 3)
                    {
                        // Only the plant's held middle counts: its first and last four frames are where the foot lands and lifts.
                        int i0 = Mathf.Min(i + 4, j - 1), i1 = Mathf.Max(j - 4, i0 + 1);
                        Vector3 c = Vector3.zero;
                        for (int f = i0; f < i1; f++) c += sole[f];
                        c /= (i1 - i0);
                        float worst = 0f;
                        for (int f = i0; f < i1; f++) worst = Mathf.Max(worst, new Vector2(sole[f].x - c.x, sole[f].z - c.z).magnitude);
                        worstSlide = Mathf.Max(worstSlide, worst);
                        sumSlide += worst; plants++;
                    }
                    i = j;
                }
            }
            q.footSlideWorstCm = worstSlide * 100f;
            q.footSlideMeanCm = plants > 0 ? sumSlide / plants * 100f : 0f;

            // The blow. Which side strikes comes from the move's name where it says (a punch is the right hand, as in the
            // game; a jab the left; a kick the right foot unless it says left), else the side that reaches furthest. The
            // tracker's guesses at a hidden arm move it about as fast as a real punch, so speed alone cannot tell.
            string mv = s.move.ToLowerInvariant();
            bool kick = mv.Contains("kick");
            int striker = mv.Contains("jab") ? 0 : mv.Contains("punch") || mv.Contains("cross") || mv.Contains("butt") ? 1
                        : kick ? (mv.Contains("left") ? 2 : 3) : -2;
            var hands = new[] { fp.leftHand, fp.rightHand, fp.leftFoot, fp.rightFoot };
            if (striker == -2)
            {
                float best = 0.12f;
                striker = -1;
                for (int side = kick ? 2 : 0; side < (kick ? 4 : 2); side++)
                    for (int i = 0; i < n; i++)
                    {
                        float d = Vector3.Distance(hands[side][i], hands[side][0]);
                        if (d > best) { best = d; striker = side; }
                    }
            }
            int impact = 0;
            if (striker >= 0)
            {
                // The blow lands where the striking hand or foot is furthest out ahead.
                var track = hands[striker];
                for (int i = 1; i < n; i++) if (track[i].z > track[impact].z) impact = i;

                // The first-person track is the strike alone, guard to blow and back to guard: from where the hand last
                // stood still before the blow to where it next stands still after it (the take's neutral pose either side
                // is not the game's guard).
                float Speed(int i) => i <= 0 ? 0f : (track[i] - track[i - 1]).magnitude * Rate;
                int a0 = impact, b0 = impact;
                while (a0 > 0 && Speed(a0) > 0.35f) a0--;
                while (b0 < n - 1 && (Speed(b0 + 1) > 0.35f || b0 < impact + 4)) b0++;
                a0 = Mathf.Max(0, a0 - 4);
                b0 = Mathf.Min(n - 1, b0 + 4);
                T[] Cut<T>(T[] arr) { var o = new T[b0 - a0 + 1]; Array.Copy(arr, a0, o, 0, o.Length); return o; }
                fp.leftHand = Cut(fp.leftHand); fp.rightHand = Cut(fp.rightHand);
                fp.leftHandTurn = Cut(fp.leftHandTurn); fp.rightHandTurn = Cut(fp.rightHandTurn);
                fp.leftFoot = Cut(fp.leftFoot); fp.rightFoot = Cut(fp.rightFoot);
                fp.leftFootTurn = Cut(fp.leftFootTurn); fp.rightFootTurn = Cut(fp.rightFootTurn);
                fp.fromClip = a0 / Rate;
                impact -= a0;
            }
            fp.striker = striker;
            fp.impact = impact / Rate;
            fp.source = anim;
            q.striker = striker switch { 0 => "left hand", 1 => "right hand", 2 => "left foot", 3 => "right foot", _ => "none" };
            q.impact = fp.impact;

            if (s.firstPerson)
            {
                string fpPath = $"{folder}/{take}_fp.asset";
                var existing = AssetDatabase.LoadAssetAtPath<CapturedFirstPerson>(fpPath);
                if (existing != null) { EditorUtility.CopySerialized(fp, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(fp); }
                else AssetDatabase.CreateAsset(fp, fpPath);
                q.firstPerson = fpPath;
            }
            else Object.DestroyImmediate(fp);
        }

        /// <summary>The worst frame-to-frame jerk (third difference at 60 a second, m/s^3) of the given points.</summary>
        static float Jerk(Vector3[][] ends)
        {
            float worst = 0f;
            for (int i = 3; i < ends.Length; i++)
                for (int j = 0; j < ends[i].Length; j++)
                {
                    Vector3 d3 = ends[i][j] - 3f * ends[i - 1][j] + 3f * ends[i - 2][j] - ends[i - 3][j];
                    worst = Mathf.Max(worst, d3.magnitude * Rate * Rate * Rate);
                }
            return worst;
        }

        /// <summary>The same measure on one of our current clips, sampled at 60 a second on the same rig, to compare against.</summary>
        static float ClipJerk(Rig rig, AnimationClip clip)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(clip.length * Rate) + 1);
            var ends = new Vector3[n][];
            var graph = UnityEngine.Playables.PlayableGraph.Create("MocapJerk");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", rig.anim);
                var play = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                play.SetApplyFootIK(false);
                output.SetSourcePlayable(play);
                for (int i = 0; i < n; i++)
                {
                    rig.Reset();
                    play.SetTime(Mathf.Min(i / Rate, clip.length));
                    graph.Evaluate();
                    ends[i] = new[] { rig.lArm.end.position, rig.rArm.end.position, rig.lLeg.end.position, rig.rLeg.end.position, rig.head.position };
                }
            }
            finally { graph.Destroy(); rig.Reset(); }
            return Jerk(ends);
        }
    }
}
#endif
