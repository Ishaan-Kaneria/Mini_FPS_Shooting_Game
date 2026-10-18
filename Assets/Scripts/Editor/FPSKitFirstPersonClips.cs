#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Bakes the Low Poly Shooter Pack's first-person clips into <see cref="FirstPersonClips"/> (Resources):
    /// only their motion, never their low-poly arms or rifle (Ishaan, 2026-10-18).
    ///
    /// Each clip is sampled on the pack's own arms rig (Assets/AnimationStaging/LowPolyShooterPack), whose
    /// root sits at the camera with its axes the view's. Per frame: the gun bone's turn and shift from the
    /// reference pose, re-expressed about the right hand's grip (where our right hand holds our gun), and the
    /// left hand's shift and turn on the gun from its reference grip, in the gun's frame as it lies in the
    /// reference. The aim transitions keep only their arc: the part that is not a straight line between their
    /// ends, which are our own hip and sight positions. Loops get their stride (the gun's side-to-side
    /// period) and the moment of a footfall (the gun lowest); the reloads get the moments the magazine
    /// leaves and is seated and the bolt is run, from the pack's rifle rig and its own clip of the reload.
    ///
    ///   FPSKit > Player > Bake First-Person Clips
    ///   Tools/unity-batch.sh FPSKitBatch.BakeFirstPersonClips      (Build/FirstPersonClips/report.txt)
    /// </summary>
    public static class FPSKitFirstPersonClips
    {
        const string Dir = "Assets/AnimationStaging/LowPolyShooterPack/";
        const string Rig = Dir + "SK_FP_CH_Default_Cubic.fbx";
        const string GunRig = Dir + "Weapons/SK_AR_01.fbx";
        const string AssetPath = "Assets/FPSKit_Generated/Resources/FirstPersonClips.asset";

        struct Pose { public Vector3 gunP, handP, rightP; public Quaternion gunR, handR; }

        [MenuItem("FPSKit/Player/Bake First-Person Clips")]
        public static void Bake()
        {
            var report = new StringBuilder();
            GameObject rig = null, gunRig = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Rig));
                gunRig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GunRig));
                var bones = rig.GetComponentsInChildren<Transform>(true);
                Transform B(string n) => bones.First(t => t.name == n);
                var gun = B("ik_hand_gun"); var handL = B("hand_l"); var handR = B("hand_r");

                Pose Read()
                {
                    var root = rig.transform;
                    var inv = Quaternion.Inverse(root.rotation);
                    return new Pose
                    {
                        gunP = root.InverseTransformPoint(gun.position), gunR = inv * gun.rotation,
                        handP = root.InverseTransformPoint(handL.position), handR = inv * handL.rotation,
                        rightP = root.InverseTransformPoint(handR.position),
                    };
                }
                AnimationClip Clip(string file) => AssetDatabase.LoadAllAssetsAtPath(Dir + "Animations/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
                Pose At(AnimationClip clip, float t) { clip.SampleAnimation(rig, t); return Read(); }

                var hip = At(Clip("ARs/A_FP_PCH_AR_01_Idle_Pose"), 0f);
                var aim = At(Clip("ARs/A_FP_PCH_AR_01_Aim_Pose"), 0f);

                var asset = AssetDatabase.LoadAssetAtPath<FirstPersonClips>(AssetPath);
                bool fresh = asset == null;
                if (fresh) asset = ScriptableObject.CreateInstance<FirstPersonClips>();

                FirstPersonClips.Track Track(string file, Pose reference, bool loop, bool arc = false)
                {
                    var clip = Clip(file);
                    var track = new FirstPersonClips.Track { source = file, length = clip.length, loop = loop, rate = 30f };
                    int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * 30f) + 1);
                    var poses = new Pose[n];
                    for (int f = 0; f < n; f++) poses[f] = At(clip, Mathf.Min(clip.length, f / 30f));
                    track.gunPos = new Vector3[n]; track.gunRot = new Quaternion[n];
                    track.handPos = new Vector3[n]; track.handRot = new Quaternion[n];
                    for (int f = 0; f < n; f++)
                    {
                        // The reference this frame: a pose, or for an arc the straight line between the clip's ends.
                        Pose r = reference;
                        if (arc)
                        {
                            float k = f / (float)(n - 1);
                            r.gunP = Vector3.Lerp(poses[0].gunP, poses[n - 1].gunP, k);
                            r.gunR = Quaternion.Slerp(poses[0].gunR, poses[n - 1].gunR, k);
                            r.rightP = Vector3.Lerp(poses[0].rightP, poses[n - 1].rightP, k);
                            r.handP = Vector3.Lerp(poses[0].handP, poses[n - 1].handP, k);
                            r.handR = Quaternion.Slerp(poses[0].handR, poses[n - 1].handR, k);
                        }
                        var p = poses[f];
                        // The gun: turned about the right hand's grip in the reference, then shifted.
                        Quaternion dR = p.gunR * Quaternion.Inverse(r.gunR);
                        Vector3 c = r.rightP;
                        track.gunRot[f] = dR;
                        track.gunPos[f] = p.gunP - c - dR * (r.gunP - c);
                        // The left hand on the gun, from its reference grip, in the gun's frame as it lies in the reference.
                        Vector3 onGun = Quaternion.Inverse(p.gunR) * (p.handP - p.gunP);
                        Vector3 onGunRef = Quaternion.Inverse(r.gunR) * (r.handP - r.gunP);
                        Quaternion turnOnGun = (Quaternion.Inverse(p.gunR) * p.handR) * Quaternion.Inverse(Quaternion.Inverse(r.gunR) * r.handR);
                        track.handPos[f] = r.gunR * (onGun - onGunRef);
                        track.handRot[f] = r.gunR * turnOnGun * Quaternion.Inverse(r.gunR);
                    }
                    if (loop) Stride(track);
                    report.AppendLine($"  {file,-40} {clip.length,5:0.00} s {n,4} frames  gun moves up to {track.gunPos.Max(v => v.magnitude) * 100f,4:0.0} cm, " +
                                      $"{track.gunRot.Max(q => Quaternion.Angle(q, Quaternion.identity)),4:0.0} deg; left hand up to {track.handPos.Max(v => v.magnitude) * 100f,4:0.0} cm" +
                                      (loop ? $"; stride {track.strideSeconds:0.00} s, footfall at {track.footfallAt:0.00} s, carry {track.meanPos * 100f} cm {Quaternion.Angle(track.meanRot, Quaternion.identity):0.0} deg" : ""));
                    return track;
                }

                asset.idle = Track("_Common/A_FP_PCH_Idle", hip, true);
                asset.walk = Track("_Common/A_FP_PCH_Walk_F", hip, true);
                asset.aimWalk = Track("_Common/A_FP_PCH_Aim_Walk_F", aim, true);
                asset.run = Track("_Common/A_FP_PCH_Run_01", hip, true);
                asset.sprint = Track("_Common/A_FP_PCH_Run_02", hip, true);
                asset.aimIn = Track("_Common/A_FP_PCH_Aim_In_Transition", hip, false, arc: true);
                asset.aimOut = Track("_Common/A_FP_PCH_Aim_Out_Transition", aim, false, arc: true);
                asset.fire = Track("ARs/A_FP_PCH_AR_Fire", hip, false);
                asset.aimFire = Track("ARs/A_FP_PCH_AR_Aim_Fire", aim, false);
                asset.reload = Track("ARs/A_FP_PCH_AR_01_Reload", hip, false);
                asset.reloadEmpty = Track("ARs/A_FP_PCH_AR_01_Reload_Empty", hip, false);
                asset.holster = Track("ARs/A_FP_PCH_AR_01_Holster", hip, false);
                asset.unholster = Track("ARs/A_FP_PCH_AR_01_Unholster", hip, false);
                asset.inspect = Track("ARs/A_FP_PCH_AR_01_Inspect", hip, false);

                Beats(gunRig, "A_FP_WEP_AR_01_Reload", asset.reload, report);
                Beats(gunRig, "A_FP_WEP_AR_01_Reload_Empty", asset.reloadEmpty, report);

                if (fresh) AssetDatabase.CreateAsset(asset, AssetPath);
                else EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                Debug.Log("[FPSKitBatch] first-person clips baked to " + AssetPath + "\n" + report);
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig);
                if (gunRig != null) Object.DestroyImmediate(gunRig);
            }
            Directory.CreateDirectory("Build/FirstPersonClips");
            File.WriteAllText("Build/FirstPersonClips/report.txt", report.ToString());
        }

        /// <summary>
        /// A loop's stride: the period of the gun's side-to-side swing (once per two steps), and a footfall:
        /// where the gun is lowest, which is a moment after the foot lands. The carry is its mean pose.
        /// </summary>
        static void Stride(FirstPersonClips.Track t)
        {
            int n = t.gunPos.Length;
            var x = t.gunPos.Select(v => v.x).ToArray();
            float mean = x.Average();
            int best = 0; float bestC = 0f;
            for (int lag = 8; lag < n / 2; lag++)
            {
                float c = 0f;
                for (int i = 0; i + lag < n; i++) c += (x[i] - mean) * (x[i + lag] - mean);
                c /= n - lag;
                if (c > bestC) { bestC = c; best = lag; }
            }
            t.strideSeconds = best > 0 ? best / t.rate : 0f;
            int half = Mathf.Max(2, best / 2);
            int low = 0;
            for (int i = 1; i < Mathf.Min(n, half + 1); i++) if (t.gunPos[i].y < t.gunPos[low].y) low = i;
            t.footfallAt = low / t.rate;

            Vector3 p = Vector3.zero; Vector4 q = Vector4.zero;
            foreach (var v in t.gunPos) p += v;
            foreach (var r in t.gunRot) { var v = new Vector4(r.x, r.y, r.z, r.w); if (Vector4.Dot(v, q) < 0f) v = -v; q += v; }
            q.Normalize();
            t.meanPos = p / n;
            t.meanRot = new Quaternion(q.x, q.y, q.z, q.w);
        }

        /// <summary>The magazine out, the magazine seated and the bolt, from the pack's rifle and its own clip of the reload.</summary>
        static void Beats(GameObject gunRig, string clipName, FirstPersonClips.Track t, StringBuilder report)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(GunRig).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
            var bones = gunRig.GetComponentsInChildren<Transform>(true);
            var mag = bones.FirstOrDefault(b => b.name == "magazine");
            var bolt = bones.FirstOrDefault(b => b.name == "bolt");
            if (clip == null || mag == null) { report.AppendLine($"  {clipName}: no rifle clip, beats left as they were"); return; }
            var root = gunRig.transform;
            clip.SampleAnimation(gunRig, 0f);
            Vector3 magRest = root.InverseTransformPoint(mag.position);
            Vector3 boltRest = bolt != null ? root.InverseTransformPoint(bolt.position) : Vector3.zero;
            float outAt = -1f, inAt = -1f, boltAt = -1f, far = 0f, boltFar = 0f;
            for (float time = 0f; time <= clip.length + 1e-4f; time += 1f / 60f)
            {
                clip.SampleAnimation(gunRig, time);
                float d = (root.InverseTransformPoint(mag.position) - magRest).magnitude;
                if (outAt < 0f && d > 0.05f) outAt = time;
                if (d > far) far = d;
                if (outAt >= 0f && inAt < 0f && far > 0.1f && d < 0.01f) inAt = time;
                if (bolt != null)
                {
                    float b = (root.InverseTransformPoint(bolt.position) - boltRest).magnitude;
                    if (b > boltFar) { boltFar = b; boltAt = time; }
                }
            }
            t.magOut = outAt >= 0f ? outAt / clip.length : -1f;
            t.magIn = inAt >= 0f ? inAt / clip.length : -1f;
            t.bolt = boltFar > 0.01f ? boltAt / clip.length : -1f;
            report.AppendLine($"  {clipName}: magazine out at {t.magOut:0.00}, seated at {t.magIn:0.00}, bolt {(t.bolt >= 0f ? t.bolt.ToString("0.00") : "none")} (of {clip.length:0.00} s; bolt travel {boltFar * 100f:0.0} cm)");
        }
    }
}
#endif
