using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's own arms and gun, from rigged models, posed in code every frame.
///
/// <see cref="Weapon"/> still owns everything the gun does -- the hip and sights blend,
/// the kick, the reload tilt, the strikes' turns -- by moving its model transform. This
/// takes that motion and puts a real gun on it (the gun's <see cref="WeaponData.viewModel"/>),
/// then reaches a pair of rigged arms onto the gun's grip and handguard with two-bone IK,
/// curls the fingers round them, and lets go of the gun for <see cref="MeleeStrike"/>'s
/// punches. A kick is drawn with a pair of legs that exist only for it.
///
/// There are no animation clips. Every pose is a place for a hand, said in the view's
/// metres, which is what can be judged in a screenshot; and nothing here depends on how a
/// particular rig's bones happen to be oriented (see <see cref="LimbIK"/>), so a different
/// arms model is a different prefab and no code.
///
/// Added by Weapon at runtime and built from the gun's data, so no arena scene has to be
/// rebuilt for it. With no view model on the data it does nothing, and the scene's own
/// gun is drawn as before.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class ViewModelRig : MonoBehaviour
{
    /// <summary>Everything a frame of the arms is decided from. Read from the components in play; set by hand by the preview tool.</summary>
    public struct State
    {
        public float aim;
        public float fistsUp;
        public float gunAway;
        public bool takedownWithFists;
        public Vector3 velocity;        // the body's, in its own space
        public bool grounded;
        public float crouch;            // metres the eye has dropped
        public float lookDown;          // 0 looking level, 1 straight down
        public MeleeStrike.Strike strike;
        public float strikeProgress;
        public float reloadProgress;
        public bool reloadEmpty;
        public float sinceShot;
    }

    class Arm
    {
        public bool right;
        public Transform upper, lower, hand;
        public LimbIK.HandFrame frame;
        public Transform[] fingers;
        public Quaternion[] hold, fist;
        public Quaternion upperBind, lowerBind, handBind;
    }

    Weapon _weapon;
    MeleeStrike _melee;
    PlayerMotor _motor;
    Transform _holder;
    WeaponData _builtFor;

    ViewModelSpec _spec;
    Vector3 _boltRest;

    Transform _arms;
    Vector3 _headLocal;
    Arm _left, _right;

    class Leg
    {
        public Transform thigh, calf, foot;
        public Quaternion thighBind, calfBind, footBind, footToBody;
        public Vector3 rest;            // where the foot stands, in the body's space
    }

    Transform _legs;
    Leg _leftLeg, _rightLeg;
    Transform _hips;
    Vector3 _hipsRest;
    Renderer[] _gunRenderers;
    float _gait;
    float _feetZ;
    float _standingEye = -1f;

    readonly List<Renderer> _hidden = new List<Renderer>();

    /// <summary>The body the kick legs hang from when there is no PlayerMotor: the preview tool's stand-in.</summary>
    [System.NonSerialized] public Transform PreviewBody;

    /// <summary>Takes everything this built down again.</summary>
    public void Clear() => Teardown(keepBodies: false);

    /// <summary>The arms are built and holding a gun.</summary>
    public bool Built => _spec != null && _arms != null;

    // ======================================================================

    void Start()
    {
        _weapon = GetComponent<Weapon>();
        if (_weapon == null) { enabled = false; return; }

        _melee = GetComponentInParent<MeleeStrike>();
        _motor = GetComponentInParent<PlayerMotor>();
        _weapon.Equipped += OnEquipped;

        Build(_weapon.data);
    }

    void OnDestroy()
    {
        if (_weapon != null) _weapon.Equipped -= OnEquipped;
    }

    void OnEquipped(Weapon w) => Build(w.data);

    /// <summary>
    /// Puts the data's gun in the hands, building the arms the first time. Data with no
    /// view model takes the rig down and shows the scene's own gun again.
    /// </summary>
    public void Build(WeaponData data)
    {
        if (data == _builtFor && Built) return;
        _builtFor = data;

        Teardown(keepBodies: data != null && data.viewModel != null);

        if (data == null || data.viewModel == null || _weapon == null) return;

        _holder = _weapon.transform.parent;
        if (_holder == null) return;

        Build(data.viewModel, _holder, gameObject.layer);
        if (_spec == null) return;

        // The scene's own gun and hands are hidden, never destroyed: the rig can come down
        // again (a gun with no model) and they are what is left.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r.transform.IsChildOf(_spec.transform)) continue;
            r.enabled = false;
            _hidden.Add(r);
        }

        if (_weapon.muzzlePoint != null && _spec.muzzle != null)
        {
            _weapon.muzzlePoint.SetParent(_spec.muzzle, false);
            _weapon.muzzlePoint.localPosition = Vector3.zero;
            _weapon.muzzlePoint.localRotation = Quaternion.identity;
        }

        _weapon.BindReloadParts(_spec.magazine, _spec.leftHand);
        _weapon.ViewHipPosition = _spec.hipPosition;
        _weapon.ViewHipRotation = Quaternion.Euler(_spec.hipTurn);
        _weapon.ViewButtLength = _spec.buttLength;
        _weapon.HasArms = _arms != null;
    }

    /// <summary>
    /// The part of the build that needs no running game: the gun and the bodies under
    /// <paramref name="holder"/>. Public for the preview tool, which poses a rig in a
    /// scene that is not playing.
    /// </summary>
    public void Build(GameObject viewModel, Transform holder, int layer)
    {
        _holder = holder;

        var gun = Instantiate(viewModel, holder, false);
        gun.name = viewModel.name;
        _spec = gun.GetComponent<ViewModelSpec>();
        if (_spec == null) { Destroy(gun); return; }

        if (_spec.bolt != null) _boltRest = _spec.bolt.localPosition;
        Prepare(gun, layer);
        _gunRenderers = gun.GetComponentsInChildren<Renderer>(true);

        if (_arms == null && _spec.armsPrefab != null) BuildArms(_spec.armsPrefab, holder, layer);
        if (_legs == null && _spec.legsPrefab != null) BuildLegs(_spec.legsPrefab, layer);
    }

    void Teardown(bool keepBodies)
    {
        if (_weapon != null)
        {
            _weapon.HasArms = false;
            if (_weapon.muzzlePoint != null && _weapon.muzzlePoint.parent != _weapon.transform && _spec != null)
                _weapon.muzzlePoint.SetParent(_weapon.transform, true);
            _weapon.BindReloadParts(_weapon.transform.Find("Magazine"), _weapon.transform.Find("SupportHand"));
        }

        foreach (var r in _hidden) if (r != null) r.enabled = true;
        _hidden.Clear();

        if (_spec != null) Destroy(_spec.gameObject);
        _spec = null;

        if (keepBodies) return;

        if (_arms != null) Destroy(_arms.gameObject);
        if (_legs != null) Destroy(_legs.gameObject);
        _arms = _legs = null;
        _left = _right = null;
    }

    /// <summary>A view model is drawn, never hit and never casts a shadow onto the world it is not really in.</summary>
    static void Prepare(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
        }
    }

    static new void Destroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    // ======================================================================
    // Arms
    // ======================================================================

    void BuildArms(GameObject prefab, Transform holder, int layer)
    {
        var go = Instantiate(prefab, holder, false);
        go.name = "Arms";
        Prepare(go, layer);
        _arms = go.transform;
        _arms.localPosition = Vector3.zero;
        _arms.localRotation = Quaternion.identity;

        var animator = go.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) { Destroy(go); _arms = null; return; }
        animator.enabled = false;

        Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

        var head = Bone(HumanBodyBones.Head);
        _headLocal = head != null ? _arms.InverseTransformPoint(head.position) : new Vector3(0f, 1.65f, 0f);

        _left = MakeArm(Bone, right: false);
        _right = MakeArm(Bone, right: true);

        BakeFingers(animator);
    }

    static Arm MakeArm(System.Func<HumanBodyBones, Transform> bone, bool right)
    {
        var arm = new Arm
        {
            right = right,
            upper = bone(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm),
            lower = bone(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm),
            hand = bone(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand),
        };

        if (arm.upper == null || arm.lower == null || arm.hand == null) return null;

        arm.upperBind = arm.upper.localRotation;
        arm.lowerBind = arm.lower.localRotation;
        arm.handBind = arm.hand.localRotation;

        arm.frame = LimbIK.HandFrame.Measure(arm.hand,
            bone(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal),
            bone(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal),
            bone(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal),
            right);

        var fingers = new List<Transform>();
        int first = right ? (int)HumanBodyBones.RightThumbProximal : (int)HumanBodyBones.LeftThumbProximal;
        for (int i = 0; i < 15; i++)
        {
            var t = bone((HumanBodyBones)(first + i));
            if (t != null) fingers.Add(t);
        }
        arm.fingers = fingers.ToArray();
        return arm;
    }

    /// <summary>
    /// The finger curls, worked out once through the Humanoid muscles so they mean the
    /// same thing on any rig: a hold (round a grip or a handguard, trigger finger along
    /// the guard on the right) and a fist. Everything the muscles move besides the fingers
    /// is put back afterwards.
    /// </summary>
    void BakeFingers(Animator animator)
    {
        if (_left == null || _right == null) return;

        var all = _arms.GetComponentsInChildren<Transform>(true);
        var pos = new Vector3[all.Length];
        var rot = new Quaternion[all.Length];
        for (int i = 0; i < all.Length; i++) { pos[i] = all[i].localPosition; rot[i] = all[i].localRotation; }

        var handler = new HumanPoseHandler(animator.avatar, _arms);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);

        _right.hold = Curl(handler, ref pose, _right, thumb: -0.3f, index: 0.1f, others: -0.6f, spread: 0f);
        _right.fist = Curl(handler, ref pose, _right, thumb: -0.7f, index: -1f, others: -1f, spread: -0.4f);
        _left.hold = Curl(handler, ref pose, _left, thumb: -0.25f, index: -0.15f, others: -0.25f, spread: -0.6f);
        _left.fist = Curl(handler, ref pose, _left, thumb: -0.7f, index: -1f, others: -1f, spread: -0.4f);

        handler.Dispose();
        for (int i = 0; i < all.Length; i++) { all[i].localPosition = pos[i]; all[i].localRotation = rot[i]; }
    }

    /// <summary>One hand's fingers set through their muscles, read back as local rotations.</summary>
    static Quaternion[] Curl(HumanPoseHandler handler, ref HumanPose pose, Arm arm, float thumb, float index, float others, float spread)
    {
        string side = arm.right ? "Right" : "Left";

        for (int m = 0; m < HumanTrait.MuscleCount; m++)
        {
            string name = HumanTrait.MuscleName[m];
            if (!name.StartsWith(side + " ")) continue;

            if (name.Contains("Thumb") && name.Contains("Stretched")) pose.muscles[m] = thumb;
            else if (name.Contains("Index") && name.Contains("Stretched")) pose.muscles[m] = index;
            else if ((name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little")) && name.Contains("Stretched")) pose.muscles[m] = others;
            else if (name.Contains("Spread") && !name.Contains("Thumb")) pose.muscles[m] = spread;
        }

        handler.SetHumanPose(ref pose);

        var result = new Quaternion[arm.fingers.Length];
        for (int i = 0; i < result.Length; i++) result[i] = arm.fingers[i].localRotation;
        return result;
    }

    // ======================================================================
    // The body
    // ======================================================================

    /// <summary>
    /// The player's own body below the arms: torso, legs and boots, on the player root so
    /// it turns with the player and not with the look. Its own arms are collapsed into the
    /// shoulders (the first-person arms are the ones that hold the gun), and it stands a
    /// little behind the eye, so looking down shows the chest and the feet the way it does
    /// in life, rather than the inside of a collar.
    /// </summary>
    void BuildLegs(GameObject prefab, int layer)
    {
        var body = _motor != null ? _motor.transform : PreviewBody;
        if (body == null) return;

        var go = Instantiate(prefab, body, false);
        go.name = "Body";
        Prepare(go, layer);
        _legs = go.transform;
        _legs.localPosition = BodyOffset;
        _legs.localRotation = Quaternion.identity;

        var animator = go.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) { Destroy(go); _legs = null; return; }
        animator.enabled = false;

        foreach (var arm in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.Head })
        {
            var bone = animator.GetBoneTransform(arm);
            if (bone != null) bone.localScale = Vector3.one * 0.001f;
        }

        _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        _hipsRest = _hips != null ? _hips.localPosition : Vector3.zero;

        _leftLeg = MakeLeg(animator, right: false);
        _rightLeg = MakeLeg(animator, right: true);
        if (_leftLeg == null || _rightLeg == null) { Destroy(go); _legs = null; }
    }

    /// <summary>Behind the eye and the player's centre, so the camera is never inside the chest.</summary>
    static readonly Vector3 BodyOffset = new Vector3(0f, -0.04f, -0.24f);
    static readonly Vector3 LookDownOffset = new Vector3(0f, -0.12f, -0.32f);

    Leg MakeLeg(Animator animator, bool right)
    {
        var leg = new Leg
        {
            thigh = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg),
            calf = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg),
            foot = animator.GetBoneTransform(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot),
        };
        if (leg.thigh == null || leg.calf == null || leg.foot == null) return null;

        leg.thighBind = leg.thigh.localRotation;
        leg.calfBind = leg.calf.localRotation;
        leg.footBind = leg.foot.localRotation;
        leg.footToBody = Quaternion.Inverse(_legs.rotation) * leg.foot.rotation;
        leg.rest = _legs.InverseTransformPoint(leg.foot.position);
        return leg;
    }

    // ======================================================================
    // Posing
    // ======================================================================

    void LateUpdate()
    {
        if (!Built || _weapon == null) return;
        Pose(Read());
    }

    State Read()
    {
        var s = new State
        {
            aim = _weapon.AimProgress,
            reloadProgress = _weapon.ReloadProgress,
            reloadEmpty = _weapon.ReloadIsEmpty,
            sinceShot = Time.time - _weapon.LastShotTime,
            strike = MeleeStrike.Strike.None,
            strikeProgress = -1f,
        };

        if (_melee != null)
        {
            s.fistsUp = _melee.FistsUp;
            s.gunAway = _melee.GunAway;
            s.takedownWithFists = _melee.TakedownWithFists;
            s.strike = _melee.Current;
            s.strikeProgress = _melee.StrikeProgress;
        }

        if (_motor != null)
        {
            s.velocity = _motor.transform.InverseTransformDirection(_motor.Velocity);
            if (_weapon.fpsCamera != null) s.lookDown = Mathf.Clamp01(-_weapon.fpsCamera.transform.forward.y);
            s.grounded = _motor.IsGrounded;

            float eye = _motor.cameraHolder != null ? _motor.cameraHolder.localPosition.y : 1.65f;
            if (_standingEye < 0f || eye > _standingEye) _standingEye = eye;
            s.crouch = Mathf.Max(0f, _standingEye - eye);
        }
        else
        {
            s.grounded = true;
        }

        return s;
    }

    /// <summary>Poses the gun, the arms and the legs for one frame.</summary>
    public void Pose(State s)
    {
        PlaceGun(s);
        PlaceArms(s);
        PlaceLegs(s);
    }

    /// <summary>
    /// The gun goes where the spec says for the hip-to-sights blend, carrying every
    /// offset and turn the Weapon has put on its own model this frame on top: kick,
    /// reload, strikes, the run and the draw all come through unchanged.
    /// </summary>
    void PlaceGun(State s)
    {
        var model = _weapon != null ? _weapon.transform : null;

        Vector3 delta = Vector3.zero;
        Quaternion turn = Quaternion.identity;

        if (model != null && _weapon.data != null && Application.isPlaying)
        {
            Vector3 weaponBase = Vector3.Lerp(_weapon.HipPosition, _weapon.data.adsPosition, s.aim);
            delta = model.localPosition - weaponBase;
            turn = Quaternion.Inverse(_weapon.HipRotation) * model.localRotation;
        }

        Vector3 basePos = Vector3.Lerp(_spec.hipPosition, _spec.aimPosition, s.aim);
        Quaternion baseTurn = Quaternion.Slerp(Quaternion.Euler(_spec.hipTurn), Quaternion.identity, s.aim);

        var gun = _spec.transform;
        gun.localPosition = basePos + delta;
        gun.localRotation = baseTurn * turn;

        // On the back it is out of sight: hidden rather than left hanging behind the eye.
        bool shown = s.gunAway < 0.97f;
        if (_gunRenderers != null && _gunRenderers.Length > 0 && _gunRenderers[0] != null && _gunRenderers[0].enabled != shown)
            foreach (var r in _gunRenderers) if (r != null) r.enabled = shown;

        // The bolt: back and home on every round, and run on an empty reload.
        if (_spec.bolt != null)
        {
            float cycle = s.sinceShot >= 0f && s.sinceShot < 0.08f ? Mathf.Sin(s.sinceShot / 0.08f * Mathf.PI) : 0f;
            float run = s.reloadEmpty && s.reloadProgress >= 0.74f && s.reloadProgress < 0.9f
                ? Mathf.Sin((s.reloadProgress - 0.74f) / 0.16f * Mathf.PI) : 0f;
            _spec.bolt.localPosition = _boltRest + _spec.boltTravel * Mathf.Max(cycle, run);
        }
    }

    // ---- fists, in the view's space --------------------------------------

    static readonly Vector3 LeftGuard = new Vector3(-0.13f, -0.15f, 0.3f);
    static readonly Vector3 RightGuard = new Vector3(0.12f, -0.17f, 0.24f);
    static readonly Vector3 JabReach = new Vector3(-0.05f, -0.05f, 0.64f);
    static readonly Vector3 CrossReach = new Vector3(0.0f, -0.04f, 0.66f);

    /// <summary>
    /// Out fast, a beat at full stretch, back slower: the shape of a punch. Before the
    /// blow it draws back a little first, the load that makes it read as thrown, not slid.
    /// </summary>
    static float Extension(float k, float impact)
    {
        if (k < 0f) return 0f;
        float load = impact * 0.3f;
        if (k < load) return -0.18f * Mathf.Sin(k / load * Mathf.PI * 0.5f);
        if (k < impact)
        {
            float t = (k - load) / (impact - load);
            return Mathf.Lerp(-0.18f, 1f, t * t);   // accelerating into the target
        }
        if (k < impact + 0.1f) return 1f;
        float back = (k - impact - 0.1f) / Mathf.Max(0.01f, 1f - impact - 0.1f);
        return 1f - back * back * (3f - 2f * back);
    }

    void PlaceArms(State s)
    {
        if (_arms == null || _left == null || _right == null) return;

        // Head on the eye, body facing where the view faces.
        _arms.localRotation = Quaternion.identity;
        _arms.localPosition = _spec.armsOffset - _headLocal;

        Reset(_left);
        Reset(_right);

        Transform view = _holder;

        // On the gun.
        Vector3 rPos = _spec.rightHand.position;
        Quaternion rRot = _spec.rightHand.rotation;
        Vector3 lPos = _spec.leftHand.position;
        Quaternion lRot = _spec.leftHand.rotation;

        // Off it, for the punches: both fists up in a guard, one thrown at a time. The left
        // jabs; the right throws the cross, and the finishing blow, harder and further.
        float f = Mathf.Clamp01(s.fistsUp);
        if (f > 0f)
        {
            bool finisher = s.strike == MeleeStrike.Strike.Takedown && s.takedownWithFists;
            float jab = s.strike == MeleeStrike.Strike.Jab ? Extension(s.strikeProgress, MeleeStrike.ImpactAt(MeleeStrike.Strike.Jab)) : 0f;
            float cross = s.strike == MeleeStrike.Strike.Cross || finisher
                ? Extension(s.strikeProgress, MeleeStrike.ImpactAt(MeleeStrike.Strike.Cross)) * (finisher ? 1.12f : 1f) : 0f;

            // The off hand pulls back to the chin as the other is thrown.
            Vector3 lFist = Vector3.LerpUnclamped(LeftGuard, JabReach, jab) + new Vector3(0.02f, 0.02f, -0.06f) * Mathf.Clamp01(cross);
            Vector3 rFist = Vector3.LerpUnclamped(RightGuard, CrossReach, cross) + new Vector3(-0.01f, 0.02f, -0.05f) * Mathf.Clamp01(jab);

            // A guard holds the fists knuckles-up, facing in; the punch turns them over
            // flat, knuckles to the target.
            float jt = Mathf.Clamp01(jab), ct = Mathf.Clamp01(cross);
            Quaternion lFistRot = Quaternion.Slerp(Look(view, new Vector3(0.25f, 0.55f, 0.8f), new Vector3(-0.8f, 0.1f, 0.2f)),
                                                   Look(view, new Vector3(0.08f, 0.05f, 1f), new Vector3(0f, 1f, 0f)), jt);
            Quaternion rFistRot = Quaternion.Slerp(Look(view, new Vector3(-0.25f, 0.55f, 0.8f), new Vector3(0.8f, 0.1f, 0.2f)),
                                                   Look(view, new Vector3(-0.08f, 0.05f, 1f), new Vector3(0f, 1f, 0f)), ct);

            lPos = Vector3.Lerp(lPos, view.TransformPoint(lFist), f);
            rPos = Vector3.Lerp(rPos, view.TransformPoint(rFist), f);
            lRot = Quaternion.Slerp(lRot, lFistRot, f);
            rRot = Quaternion.Slerp(rRot, rFistRot, f);
        }

        Reach(_right, rPos, rRot, view.TransformDirection(new Vector3(0.55f, -0.7f, -0.2f)), f);
        Reach(_left, lPos, lRot, view.TransformDirection(new Vector3(-0.35f, -0.8f, 0.05f)), f);
    }

    /// <summary>A rotation whose +Z points along <paramref name="fingers"/> and +Y along <paramref name="back"/>, in the view's space.</summary>
    static Quaternion Look(Transform view, Vector3 fingers, Vector3 back)
        => view.rotation * Quaternion.LookRotation(fingers, back);

    static void Reset(Arm arm)
    {
        arm.upper.localRotation = arm.upperBind;
        arm.lower.localRotation = arm.lowerBind;
        arm.hand.localRotation = arm.handBind;
    }

    /// <summary>
    /// One arm onto a wrist position and a hand frame (+Z the fingers, +Y the back of the
    /// hand), the elbow out towards <paramref name="elbowDirection"/>, the fingers between
    /// their hold and a fist.
    /// </summary>
    static void Reach(Arm arm, Vector3 wrist, Quaternion frame, Vector3 elbowDirection, float fist)
    {
        LimbIK.Solve(arm.upper, arm.lower, arm.hand, wrist, arm.upper.position + elbowDirection);

        if (arm.frame.valid)
            arm.hand.rotation = arm.frame.World(frame * Vector3.forward, frame * Vector3.up);

        if (arm.hold == null || arm.fist == null) return;
        for (int i = 0; i < arm.fingers.Length; i++)
            arm.fingers[i].localRotation = Quaternion.Slerp(arm.hold[i], arm.fist[i], fist);
    }

    // ---- the legs ------------------------------------------------------------

    /// <summary>
    /// Walking, running, crouching and the kick, all from where each foot should be. A
    /// stride is a stance (the foot planted, sliding back under the body as it moves) and
    /// a swing (lifted and carried forward); the stride lengthens with speed. Crouching
    /// lowers the hips and the knees bend to keep the feet on the ground. In the air the
    /// feet tuck up.
    /// </summary>
    void PlaceLegs(State s)
    {
        if (_legs == null || _leftLeg == null || _rightLeg == null) return;

        _legs.localPosition = BodyOffset;
        _legs.localRotation = Quaternion.identity;

        foreach (var leg in new[] { _leftLeg, _rightLeg })
        {
            leg.thigh.localRotation = leg.thighBind;
            leg.calf.localRotation = leg.calfBind;
            leg.foot.localRotation = leg.footBind;
        }

        Vector3 v = new Vector3(s.velocity.x, 0f, s.velocity.z);
        float speed = v.magnitude;
        float moving = Mathf.Clamp01(speed / 1.2f);
        float stride = Mathf.Lerp(0.55f, 1.25f, Mathf.Clamp01(speed / 8f));
        float dt = Application.isPlaying ? Time.deltaTime : 0f;
        _gait = Mathf.Repeat(_gait + speed * dt / Mathf.Max(0.1f, stride * 2f), 1f);

        Vector3 dir = speed > 0.05f ? v / speed : Vector3.forward;
        float crouch = Mathf.Clamp(s.crouch, 0f, 0.6f);

        // Hips down for the crouch and with each step's weight, back a touch while kicking.
        bool kicking = s.strike == MeleeStrike.Strike.Kick && s.strikeProgress >= 0f;
        float bob = Mathf.Abs(Mathf.Sin(_gait * Mathf.PI * 2f)) * 0.035f * moving;
        if (_hips != null) _hips.localPosition = _hipsRest;

        // Into the kick the hips drive forward and the body leans back off the leg, which
        // is what puts the foot out at chest height.
        float drive = 0f;
        if (kicking)
        {
            float k = s.strikeProgress;
            drive = k < 0.45f ? UITheme.EaseOut(k / 0.45f) : k < 0.7f ? 1f : 1f - UITheme.EaseOut((k - 0.7f) / 0.3f);
        }

        // Looking down, the body comes forward under the eye and drops a little, so the chest,
        // the legs and the boots are what is seen, not the inside of the collar.
        float down = Mathf.Clamp01((s.lookDown - 0.25f) / 0.6f);
        Vector3 offset = Vector3.Lerp(BodyOffset, LookDownOffset, down);
        _feetZ = offset.z;

        _legs.localPosition = offset + Vector3.down * (crouch * 0.85f + bob) + Vector3.forward * (0.3f * drive);
        _legs.localRotation = Quaternion.Euler(-12f * drive, 0f, 0f);

        PoseLeg(_leftLeg, s, 0f, dir, stride, moving, kicking: false);
        PoseLeg(_rightLeg, s, 0.5f, dir, stride, moving, kicking);
    }

    void PoseLeg(Leg leg, State s, float offset, Vector3 dir, float stride, float moving, bool kicking)
    {
        Transform player = _legs.parent;
        Vector3 rest = leg.rest + new Vector3(0f, 0f, _feetZ);   // in the player's space, on the ground; the hips' drive leaves it planted
        Vector3 target;
        float toeUp = 0f;

        if (kicking)
        {
            // A front kick: knee up (chamber), the foot driven out to chest height sole
            // first, held a beat, pulled back and set down.
            float k = s.strikeProgress;
            float impact = MeleeStrike.ImpactAt(MeleeStrike.Strike.Kick);
            float chamberEnd = impact * 0.55f;

            Vector3 chambered = new Vector3(rest.x - 0.02f, 0.85f, 0.3f);
            Vector3 struck = new Vector3(0.05f, 1.42f, 0.66f);

            if (k < chamberEnd) target = Vector3.Lerp(rest, chambered, UITheme.EaseOut(k / chamberEnd));
            else if (k < impact) { float t = (k - chamberEnd) / (impact - chamberEnd); target = Vector3.Lerp(chambered, struck, t * t); }
            else if (k < impact + 0.14f) target = struck;
            else if (k < 0.8f) target = Vector3.Lerp(struck, chambered, UITheme.EaseOut((k - impact - 0.14f) / (0.8f - impact - 0.14f)));
            else target = Vector3.Lerp(chambered, rest, UITheme.EaseOut((k - 0.8f) / 0.2f));

            toeUp = k < impact ? Mathf.Clamp01(k / impact) : k < 0.8f ? 1f : 1f - (k - 0.8f) / 0.2f;
        }
        else if (!s.grounded)
        {
            target = rest + new Vector3(0f, 0.28f, 0.12f);
        }
        else
        {
            float p = Mathf.Repeat(_gait + offset, 1f);
            const float stance = 0.6f;
            float along, lift = 0f;
            if (p < stance) along = Mathf.Lerp(0.5f, -0.5f, p / stance);
            else
            {
                float t = (p - stance) / (1f - stance);
                along = Mathf.Lerp(-0.5f, 0.5f, t);
                lift = Mathf.Sin(t * Mathf.PI) * 0.16f;
            }

            target = rest + dir * (along * stride * moving) + Vector3.up * (lift * moving);
        }

        Vector3 world = player.TransformPoint(target);
        Vector3 knee = leg.thigh.position + player.TransformDirection(new Vector3(leg.rest.x * 0.6f, 0.2f, 1f));
        LimbIK.Solve(leg.thigh, leg.calf, leg.foot, world, knee);

        leg.foot.rotation = Quaternion.AngleAxis(-70f * toeUp, player.right) * (player.rotation * leg.footToBody);
    }
}
