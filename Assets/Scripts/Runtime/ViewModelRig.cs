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
/// Every pose is a place for a hand, said in the view's metres, which is what can be judged in
/// a screenshot; and nothing here depends on how a particular rig's bones happen to be oriented
/// (see <see cref="LimbIK"/>), so a different arms model is a different prefab and no code. The
/// gun's motion through the stride, the aim, the shots, the reload and the draw comes from the
/// Low Poly Shooter Pack's clips (<see cref="FirstPersonClips"/>, their motion only, measured on
/// their rig and played about our own grip) when they are baked; the hands stay on the gun by IK.
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
        public Quaternion[] hold, fist, squeeze;
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

    List<Renderer> _hiddenList;
    List<Renderer> _hidden => _hiddenList ??= new List<Renderer>();

    // The arms and gun's own camera (the FirstPerson layer), and the gun's muzzle as it is seen.
    Camera _armsCam;
    int _firstPerson = -1;

    // A single punch: the rifle carried in the left hand while the right is thrown.
    float _carry;
    MeleeStrike.Strike _lastFists = MeleeStrike.Strike.None;

    static PlayerBodySettings Body => PlayerBodySettings.Active;

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

        // On the FirstPerson layer, drawn by the arms camera at its own field of view, when the
        // project has the layer (every arena built since the full-body work).
        _firstPerson = LayerMask.NameToLayer("FirstPerson");
        bool ownCamera = _firstPerson >= 0 && Application.isPlaying && _weapon.fpsCamera != null;
        Build(data.viewModel, _holder, ownCamera ? _firstPerson : gameObject.layer);
        if (_spec == null) return;
        if (ownCamera) BuildArmsCamera(_weapon.fpsCamera);

        // The scene's own gun and hands are hidden, never destroyed: the rig can come down
        // again (a gun with no model) and they are what is left.
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r.transform.IsChildOf(_spec.transform)) continue;
            r.enabled = false;
            _hidden.Add(r);
        }

        // Drawn by its own camera, the gun is seen somewhere other than where it is: the muzzle is
        // placed each frame where it is seen (PlaceMuzzle) rather than parented to the gun.
        if (_weapon.muzzlePoint != null && _spec.muzzle != null && _armsCam == null)
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
        gun.transform.localScale *= Body.gunScale;
        _spec = gun.GetComponent<ViewModelSpec>();
        if (_spec == null) { Destroy(gun); return; }

        if (_spec.bolt != null) _boltRest = _spec.bolt.localPosition;
        Prepare(gun, layer);
        _gunRenderers = gun.GetComponentsInChildren<Renderer>(true);

        if (_arms == null && _spec.armsPrefab != null) BuildArms(_spec.armsPrefab, holder, layer);
        // The full body (PlayerBody) has the legs, the kick and the shadow when there is one.
        bool bodied = PlayerBody.Wanted && _motor != null;
        if (_legs == null && _spec.legsPrefab != null && !bodied) BuildLegs(_spec.legsPrefab, layer);
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

    /// <summary>
    /// The arms and gun's own camera: stacked on the main one, depth cleared, at its own field of
    /// view (Settings > Video), set back from the eye so the arms are seen from a little behind
    /// them. The main camera stops drawing the FirstPerson layer.
    /// </summary>
    void BuildArmsCamera(Camera main)
    {
        if (_armsCam != null) return;
        var existing = main.transform.Find("ArmsCamera");
        var go = existing != null ? existing.gameObject : new GameObject("ArmsCamera");
        go.transform.SetParent(main.transform, false);
        _armsCam = go.GetComponent<Camera>();
        if (_armsCam == null) _armsCam = go.AddComponent<Camera>();
        _armsCam.cullingMask = 1 << _firstPerson;
        _armsCam.nearClipPlane = Body.armsNearClip;
        _armsCam.farClipPlane = 10f;
        var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_armsCam);
        data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
        data.renderShadows = false;
        // Post-processing on: the stack's tonemapping and grading are applied after its last camera,
        // so without it the arms and gun were drawn untonemapped over a graded world (white gloves,
        // a flat grey rifle).
        data.renderPostProcessing = true;
        var stack = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main).cameraStack;
        if (!stack.Contains(_armsCam)) stack.Add(_armsCam);
        main.cullingMask &= ~(1 << _firstPerson);
        PlaceArmsCamera();
    }

    void PlaceArmsCamera()
    {
        if (_armsCam == null) return;
        // Back from the eye at the hip; at the eye aiming, or the stock (which ends at the cheek)
        // is in front of the camera.
        float aim = _weapon != null ? Smooth01(Mathf.Clamp01(_weapon.AimProgress)) : 0f;
        _armsCam.transform.localPosition = new Vector3(0f, 0f, -Body.eyeBack * (1f - aim));
        _armsCam.transform.localRotation = Quaternion.identity;
        _armsCam.fieldOfView = GameSettings.ArmsFieldOfView;
    }

    /// <summary>
    /// The weapon's muzzle (where tracers start and the flash is drawn, by the main camera) put
    /// where the drawn muzzle is seen: found on the arms camera's screen, and the same screen
    /// point on the main camera's, at the same depth.
    /// </summary>
    void PlaceMuzzle()
    {
        if (_armsCam == null || _weapon == null || _weapon.muzzlePoint == null || _spec == null || _spec.muzzle == null) return;
        Vector3 at = _spec.muzzle.position;
        Vector3 vp = _armsCam.WorldToViewportPoint(at);
        if (vp.z > 0.01f) at = _weapon.fpsCamera.ViewportToWorldPoint(vp);
        _weapon.muzzlePoint.SetPositionAndRotation(at, _spec.muzzle.rotation);
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
        _arms.localScale = Vector3.one * Body.armsScale;

        var animator = go.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) { Destroy(go); _arms = null; return; }
        animator.enabled = false;

        Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

        var head = Bone(HumanBodyBones.Head);
        // In the holder's space, scale included: it is what is subtracted to put the head on the eye.
        _headLocal = head != null ? holder.InverseTransformPoint(head.position) : new Vector3(0f, 1.65f, 0f) * Body.armsScale;

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

        // Round the pistol grip: the three fingers below the trigger closed on it, the thumb round the
        // far side, the index finger curled onto the trigger (Fix 2: half-open fingers left a gap and
        // read as a hand resting under the gun). The squeeze when it fires is _right.squeeze.
        _right.hold = Curl(handler, ref pose, _right, thumb: -0.65f, index: -0.35f, others: -0.95f, spread: -0.3f);
        _right.squeeze = Curl(handler, ref pose, _right, thumb: -0.65f, index: -0.75f, others: -0.95f, spread: -0.3f);
        _right.fist = Curl(handler, ref pose, _right, thumb: -0.7f, index: -1f, others: -1f, spread: -0.4f);
        _left.hold = Curl(handler, ref pose, _left, thumb: -0.1f, index: -0.35f, others: -0.45f, spread: -0.7f);
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

    // The arms' and gun's sizes are PlayerBodySettings.armsScale and gunScale: true size, now that
    // they have their own camera and field of view (they were drawn 14% and 10% over life).
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
        PlaceArmsCamera();
        Pose(Read());
        PlaceMuzzle();
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

        Vector3 hip = _spec.hipPosition + Body.hipNudge + (DeviceProfile.Handheld ? Body.handsetHipNudge : Vector3.zero);
        // Eased in and out: the aim's own progress is a straight ramp, which started and stopped the gun at full
        // speed in a frame (FPSKitEyeCamTest, the worst jolt of the gun at any frame rate).
        float aimed = Smooth01(Mathf.Clamp01(s.aim));
        Vector3 basePos = Vector3.Lerp(hip, _spec.aimPosition, aimed);
        Quaternion baseTurn = Quaternion.Slerp(Quaternion.Euler(_spec.hipTurn + Body.hipTurnNudge), Quaternion.identity, aimed);

        var gun = _spec.transform;
        gun.localPosition = basePos + delta;
        gun.localRotation = baseTurn * turn;

        // Firing from the hip and the rifle butt: the rifle comes up and in so it is seen, eased in and out
        // over PlayerBodySettings.fightBlend; never down the sights.
        float blend = (Application.isPlaying ? Time.deltaTime : 1f) / Mathf.Max(0.01f, Body.fightBlend);
        bool hipFiring = s.sinceShot >= 0f && s.sinceShot < Body.fireHold && s.aim < 0.5f && s.fistsUp < 0.01f;
        bool butting = s.strike == MeleeStrike.Strike.Butt || (s.strike == MeleeStrike.Strike.Takedown && !s.takedownWithFists);
        _fireFrame = Mathf.MoveTowards(_fireFrame, hipFiring ? 1f : 0f, blend);
        _buttFrame = Mathf.MoveTowards(_buttFrame, butting ? 1f : 0f, blend);
        _punchFrame = Mathf.MoveTowards(_punchFrame, s.fistsUp > 0.01f ? 1f : 0f, blend);
        float atHip = 1f - aimed;
        float fire = Smooth01(_fireFrame) * atHip, butt = Smooth01(_buttFrame) * atHip;
        // On a phone the rifle goes left and down through a kick, out from under the fire button.
        _kickFrame = Mathf.MoveTowards(_kickFrame, s.strike == MeleeStrike.Strike.Kick && DeviceProfile.Handheld ? 1f : 0f, blend);
        gun.localPosition += Body.hipFireLift * fire + Body.buttLift * butt + Body.handsetKickGun * Smooth01(_kickFrame);
        gun.localRotation = gun.localRotation * Quaternion.Euler(Body.hipFireTurn * fire);

        // The movement clips: the rifle (and both hands with it) through the stride.
        PlayMoves(out Vector3 moveOffset, out Vector3 moveTurn);
        gun.localPosition += moveOffset;
        gun.localRotation = gun.localRotation * Quaternion.Euler(moveTurn);

        // The first-person clips (the Low Poly Shooter Pack's motion) on top, when there are any.
        _leftMoved = false;
        var clips = Clips;
        if (clips != null) PlayClips(s, gun, clips);

        // A single punch: the rifle is carried low at the side in the left hand while the right
        // is thrown, rather than going on the back.
        UpdateCarry(s);
        if (_carry > 0.001f)
        {
            float w = UITheme.EaseOut(_carry);
            gun.localPosition = Vector3.Lerp(gun.localPosition, Body.leftCarryPosition, w);
            gun.localRotation = Quaternion.Slerp(gun.localRotation, Quaternion.Euler(Body.leftCarryTurn), w);
        }

        // On the back it is out of sight: hidden rather than left hanging behind the eye.
        // Carried in the left hand it is seen; once the cross starts (both fists) it is on the back at
        // once, rather than drawn sliding from the hand to the sling across the view.
        bool shown = (s.gunAway < 0.97f || (_carry > 0.05f && _lastFists == MeleeStrike.Strike.Jab))
                     && !(_weapon != null && _weapon.Disarmed);   // out of ammo it is on the ground, not in a hand
        if (_gunRenderers != null && _gunRenderers.Length > 0 && _gunRenderers[0] != null && _gunRenderers[0].enabled != shown)
            foreach (var r in _gunRenderers) if (r != null) r.enabled = shown;

        // The bolt: back and home on every round, and run on an empty reload (on the clip's beat when there is one).
        if (_spec.bolt != null)
        {
            float cycle = s.sinceShot >= 0f && s.sinceShot < 0.08f ? Mathf.Sin(s.sinceShot / 0.08f * Mathf.PI) : 0f;
            float boltFrom = clips != null && _weapon != null ? _weapon.BoltBeat - 0.03f : 0.74f;
            float run = s.reloadEmpty && s.reloadProgress >= boltFrom && s.reloadProgress < boltFrom + 0.16f
                ? Mathf.Sin((s.reloadProgress - boltFrom) / 0.16f * Mathf.PI) : 0f;
            _spec.bolt.localPosition = _boltRest + _spec.boltTravel * Mathf.Max(cycle, run);
        }
    }

    /// <summary>
    /// The left hand carries the rifle from a single punch (the jab) until the fight moves on: to
    /// the double punch, which puts it on the back, or back to the gun when the fists come down.
    /// </summary>
    void UpdateCarry(State s)
    {
        if (s.strike == MeleeStrike.Strike.Jab || s.strike == MeleeStrike.Strike.Cross) _lastFists = s.strike;
        if (s.gunAway <= 0.01f && s.strike == MeleeStrike.Strike.None) _lastFists = MeleeStrike.Strike.None;
        bool carrying = _arms != null && _lastFists == MeleeStrike.Strike.Jab && (s.gunAway > 0.01f || s.strike == MeleeStrike.Strike.Jab);
        float dt = Application.isPlaying ? Time.deltaTime : 1f;
        // Into the cross the left hand lets go at once: its jab opens the double punch.
        float rate = !carrying && _lastFists == MeleeStrike.Strike.Cross ? 25f : 8f;
        _carry = Mathf.MoveTowards(_carry, carrying ? 1f : 0f, dt * rate);
    }

    // ---- the movement clips ---------------------------------------------

    // Each clip's weight, eased so walk, run, sprint and firing hand over with no pop.
    float _wWalk, _wRun, _wSprint, _wRunFire, _wAim;

    /// <summary>
    /// The movement clips (FirstPersonMoves) played on the player's footsteps: the stride's phase
    /// is the motor's distance-counted step phase, so a clip never drifts from the feet. Which clip
    /// by gait, how much of it by speed against the speed it was made for.
    /// </summary>
    void PlayMoves(out Vector3 offset, out Vector3 turn)
    {
        offset = turn = Vector3.zero;
        var moves = FirstPersonMoves.Active;
        if (moves == null || _motor == null || !Application.isPlaying) return;

        Vector3 v = _motor.Velocity; v.y = 0f;
        float speed = _motor.IsGrounded ? v.magnitude : 0f;
        bool aiming = _weapon != null && _weapon.AimProgress > 0.5f;
        bool firing = _weapon != null && Time.time - _weapon.LastShotTime < moves.firingHold;
        bool sprinting = _motor.IsSprinting && !firing;
        bool runFire = !aiming && (_motor.IsCombatJogging || firing && speed > moves.walk.speed * 1.2f);

        float walkT = Mathf.InverseLerp(moves.walk.speed, moves.run.speed, speed);
        float tw = 0f, tr = 0f, ts = 0f, tf = 0f, ta = 0f;
        if (aiming) ta = 1f;
        else if (sprinting) ts = 1f;
        else if (runFire) tf = 1f;
        else { tw = 1f - walkT; tr = walkT; }

        float step = Time.deltaTime * moves.blendRate;
        _wWalk = Mathf.MoveTowards(_wWalk, tw, step);
        _wRun = Mathf.MoveTowards(_wRun, tr, step);
        _wSprint = Mathf.MoveTowards(_wSprint, ts, step);
        _wRunFire = Mathf.MoveTowards(_wRunFire, tf, step);
        _wAim = Mathf.MoveTowards(_wAim, ta, step);

        // The first-person clips play the stride instead (PlayClips), on these same weights.
        if (Clips != null) return;

        // One stride is two steps: the footfalls at 0 and 0.5.
        float phase = Mathf.Repeat(_motor.StepPhase * 0.5f, 1f);
        void Add(FirstPersonMoves.Move m, float w, ref Vector3 o, ref Vector3 t)
        {
            if (w <= 0.001f || m == null) return;
            float size = Mathf.Clamp01(speed / Mathf.Max(0.1f, m.speed));
            m.Sample(phase, out Vector3 mo, out Vector3 mt);
            o += mo * (w * size);
            t += mt * (w * size);
        }
        Add(moves.walk, _wWalk, ref offset, ref turn);
        Add(moves.run, _wRun, ref offset, ref turn);
        Add(moves.sprint, _wSprint, ref offset, ref turn);
        Add(moves.runFire, _wRunFire, ref offset, ref turn);
        Add(moves.aimWalk, _wAim, ref offset, ref turn);

        // Not while the strikes or the left-hand carry move the rifle.
        float free = 1f - Mathf.Max(_carry, 0f);
        offset *= free;
        turn *= free;
    }

    // ---- the first-person clips -------------------------------------------

    FirstPersonClips Clips => _weapon != null && _weapon.UsesClips && Application.isPlaying ? FirstPersonClips.Active : null;

    /// <summary>Metres a second the pack's walk is made for: faster, its swing grows (up to <see cref="MaxSwing"/>).</summary>
    const float WalkClipSpeed = 3.2f, SprintClipSpeed = 9.5f, MaxSwing = 1.5f;

    /// <summary>The gun lowest a moment after the head is (EyeCameraSettings.dipAfterFootfall): the arms lag the body a little.</summary>
    const float ArmsLag = 0.17f;

    float _aimWas = -1f, _aimDir = 1f;

    // The fighting framing's weights (PlayerBodySettings "Fighting"), each eased over fightBlend.
    float _fireFrame, _buttFrame, _punchFrame, _kickFrame;
    readonly float[] _shots = new float[6];
    float _lastShotSeen = -99f;
    int _shotNext;

    // The left hand's place and turn when a clip takes it off its grip (the reload), world space.
    bool _leftMoved;
    Vector3 _leftPos;
    Quaternion _leftRot = Quaternion.identity;

    // The magazine: carried in the left hand from the moment it leaves the well until it is seated again.
    bool _magHeld, _magRestKnown;
    Vector3 _magInHand, _magRest;
    Quaternion _magInHandRot, _magRestRot;

    /// <summary>
    /// The pack's motion on our gun (FirstPersonClips): the stride on the footsteps and the idle's breathing,
    /// the arc between the hip and the sights, every shot's kick (each one played to its end, so a burst
    /// adds up and never restarts), the reload with the left hand and the magazine in it, and the draw.
    /// All of it turns the gun about the right hand's grip, which is where the pack's motion was measured
    /// from; the IK keeps both hands on our own grip and handguard.
    /// </summary>
    void PlayClips(State s, Transform gun, FirstPersonClips c)
    {
        Vector3 dp = Vector3.zero, hp = Vector3.zero;
        Quaternion dr = Quaternion.identity, hr = Quaternion.identity;
        bool hand = false;

        void Add(FirstPersonClips.Track t, float time, float w, bool swing = false, bool withHand = false, float gunShare = 1f)
        {
            if (w <= 0.0001f || t == null || !t.Valid) return;
            t.Sample(time, out Vector3 p, out Quaternion r, out Vector3 h, out Quaternion hq);
            if (swing) { p -= t.meanPos; r = r * Quaternion.Inverse(t.meanRot); }
            dp += p * (w * gunShare);
            dr = Quaternion.SlerpUnclamped(Quaternion.identity, r, w * gunShare) * dr;
            if (withHand) { hand = true; hp += h * w; hr = Quaternion.SlerpUnclamped(Quaternion.identity, hq, w) * hr; }
        }

        float dt = Time.deltaTime;
        Vector3 v = _motor != null ? _motor.Velocity : Vector3.zero;
        v.y = 0f;
        float speed = _motor != null && _motor.IsGrounded ? v.magnitude : 0f;
        float phase = _motor != null ? _motor.StepPhase - ArmsLag : 0f;
        float StrideTime(FirstPersonClips.Track t) => t.footfallAt + phase * 0.5f * t.strideSeconds;

        // The stride: the walk's swing for walking, jogging and firing on the move, bigger with speed; the
        // aimed walk's down the sights; the sprint's carry and swing as the gun goes into the sprint.
        float free = 1f - Mathf.Max(_carry, 0f);
        float sprint = _weapon.SprintPose;
        float walkSize = Mathf.Min(MaxSwing, speed / WalkClipSpeed) * c.strideWeight * free;
        Add(c.walk, StrideTime(c.walk), (_wWalk + _wRun + _wRunFire) * walkSize * (1f - sprint), swing: true);
        Add(c.aimWalk, StrideTime(c.aimWalk), _wAim * Mathf.Min(MaxSwing, speed / WalkClipSpeed) * c.strideWeight * free, swing: true);
        // The sprint: the pack's swing through the stride on the gun's own carry (Weapon's sprint pose), by the pace.
        if (sprint > 0.0001f)
            Add(c.sprint, StrideTime(c.sprint), sprint * free * Mathf.Min(1f, speed / SprintClipSpeed) * c.strideWeight, swing: true);
        float still = 1f - Mathf.Clamp01(speed / 1.2f);
        Add(c.idle, Time.time, still * c.idleWeight * (1f - 0.7f * s.aim) * free);

        // The arc between the hip and the sights: the way in on the way in, the way out on the way out.
        if (_aimWas < 0f) _aimWas = s.aim;
        if (s.aim > _aimWas + 1e-5f) _aimDir = Mathf.MoveTowards(_aimDir, 1f, dt * 8f);
        else if (s.aim < _aimWas - 1e-5f) _aimDir = Mathf.MoveTowards(_aimDir, -1f, dt * 8f);
        _aimWas = s.aim;
        if (s.aim > 0.001f && s.aim < 0.999f)
        {
            // The pack's arcs take half a second; this gun may come up in a tenth. Squeezed into that, the full arc
            // is a jolt, so it is played by how long the gun's own transition really is (a third of a second for all of it).
            float arc = _weapon.data != null ? Mathf.Clamp01(1f / (Mathf.Max(0.1f, _weapon.data.adsSpeed) * 0.35f)) : 1f;
            float into = 0.5f * (1f + _aimDir);
            Add(c.aimIn, s.aim * c.aimIn.length, into * arc);
            Add(c.aimOut, (1f - s.aim) * c.aimOut.length, (1f - into) * arc);
        }

        // Every shot's kick, each to its own end: a burst adds up and never jumps back to the start.
        if (_weapon.LastShotTime > _lastShotSeen + 1e-4f)
        {
            _lastShotSeen = _weapon.LastShotTime;
            _shots[_shotNext] = _lastShotSeen;
            _shotNext = (_shotNext + 1) % _shots.Length;
        }
        foreach (float shot in _shots)
        {
            float age = Time.time - shot;
            if (age < 0f || age > c.fire.length) continue;
            Add(c.fire, age, c.fireWeight * (1f - s.aim) * free);
            Add(c.aimFire, age, c.fireWeight * s.aim * free);
        }

        // The reload, stretched over the gun's own reload time, the left hand and the magazine with it.
        var reload = s.reloadEmpty ? c.reloadEmpty : c.reload;
        bool reloading = s.reloadProgress >= 0f && s.reloadProgress <= 1f && reload.Valid;
        if (reloading) Add(reload, s.reloadProgress * reload.length, 1f, withHand: true, gunShare: c.reloadTurn);

        // The draw: up from below the frame.
        float drawn = _weapon.DrawProgress;
        if (drawn < 1f) Add(c.unholster, drawn * c.unholster.length, 1f);

        // About the right hand's grip.
        Vector3 pivot = _holder.InverseTransformPoint(_spec.rightHand.position);
        gun.localRotation = dr * gun.localRotation;
        gun.localPosition = pivot + dr * (gun.localPosition - pivot) + dp;

        if (hand)
        {
            Quaternion q = _holder.rotation * dr;
            _leftPos = _spec.leftHand.position + q * hp;
            _leftRot = q * hr * Quaternion.Inverse(q) * _spec.leftHand.rotation;
            _leftMoved = true;
        }
        Magazine(s, reloading ? reload : null);
    }

    /// <summary>The magazine in the left hand from when it leaves the well (the clip's beat) until it is seated.</summary>
    void Magazine(State s, FirstPersonClips.Track reload)
    {
        var mag = _spec.magazine;
        if (mag == null) return;
        if (!_magRestKnown) { _magRest = mag.localPosition; _magRestRot = mag.localRotation; _magRestKnown = true; }

        float p = s.reloadProgress;
        float outAt = _weapon.MagOutBeat, inAt = _weapon.MagInBeat;
        if (reload == null || !_leftMoved || p < outAt || p >= inAt)
        {
            _magHeld = false;
            mag.localPosition = _magRest;
            mag.localRotation = _magRestRot;
            return;
        }
        if (!_magHeld)
        {
            // Its turn in the hand as the hand closes on it; its place is the palm (below the knuckles, toward the fingers).
            _magInHandRot = Quaternion.Inverse(_leftRot) * (mag.parent.rotation * _magRestRot);
            _magHeld = true;
        }
        mag.localPosition = _magRest;
        mag.localRotation = _magRestRot;
        Vector3 restPos = mag.position;
        Quaternion restRot = mag.rotation;
        Vector3 held = _leftPos + _leftRot * PalmHold;
        Quaternion heldRot = _leftRot * _magInHandRot;
        // Out of the well into the hand over a moment, and back into the well over the last stretch, so it
        // seats exactly however the hand arrives.
        float take = Mathf.Clamp01((p - outAt) / 0.05f);
        float seat = Mathf.Clamp01((p - (inAt - 0.08f)) / 0.08f);
        float w = Smooth01(take) * (1f - Smooth01(seat));
        mag.SetPositionAndRotation(Vector3.Lerp(restPos, held, w), Quaternion.Slerp(restRot, heldRot, w));
    }

    /// <summary>Where a magazine sits in the left hand, in its frame (+Z the fingers, +Y the back of the hand), metres.</summary>
    static readonly Vector3 PalmHold = new Vector3(0f, -0.05f, 0.05f);

    static float Smooth01(float x) => x * x * (3f - 2f * x);

    // ---- fists, in the view's space --------------------------------------

    // Close to the eye, so the fists are big in the view the way they are in a real fight:
    // a guard just under the chin at each side, a punch landing a forearm's length out.
    static readonly Vector3 LeftGuard = new Vector3(-0.14f, -0.15f, 0.27f);
    static readonly Vector3 RightGuard = new Vector3(0.13f, -0.17f, 0.24f);
    static readonly Vector3 JabReach = new Vector3(-0.05f, -0.06f, 0.55f);
    static readonly Vector3 CrossReach = new Vector3(0.02f, -0.05f, 0.57f);

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

        // Head on the eye, body facing where the view faces -- and the shoulders driving into
        // a punch, which is what carries a fist the last ten centimetres to a real target.
        _arms.localRotation = Quaternion.identity;
        float drive = 0f;
        if (s.strike == MeleeStrike.Strike.Jab || s.strike == MeleeStrike.Strike.Cross || (s.strike == MeleeStrike.Strike.Takedown && s.takedownWithFists))
            drive = Mathf.Clamp01(Extension(s.strikeProgress, MeleeStrike.ImpactAt(s.strike)));
        float pf = Smooth01(_punchFrame);
        _arms.localPosition = _spec.armsOffset - _headLocal + new Vector3(0f, -0.02f, 0.14f) * drive * Mathf.Clamp01(s.fistsUp)
                            + Body.punchArms * pf;
        // Each fist thrown a little wider to its own side and lower while fighting, so the whole forearm is seen at the
        // blow; the guard only half as wide and no lower (lowered, the fist held back at the chin left the frame).
        Vector3 Wide(Vector3 v, float side) => v + new Vector3(side * Body.punchWiden, -Body.punchLower, 0f) * pf;
        Vector3 Guard(Vector3 v, float side) => v + new Vector3(side * Body.punchWiden * 0.5f, 0f, 0f) * pf;

        Reset(_left);
        Reset(_right);

        Transform view = _holder;

        // On the gun.
        Vector3 rPos = _spec.rightHand.position;
        Quaternion rRot = _spec.rightHand.rotation;
        Vector3 lPos = _leftMoved ? _leftPos : _spec.leftHand.position;
        Quaternion lRot = _leftMoved ? _leftRot : _spec.leftHand.rotation;

        // Where the hands are on the gun (the left hand stays there while it carries the rifle).
        Vector3 gunL = lPos;
        Quaternion gunLRot = lRot;

        // Off it, for the punches: both fists up in a guard, one thrown at a time. The left
        // jabs; the right throws the cross, and the finishing blow, harder and further.
        float f = Mathf.Clamp01(s.fistsUp);
        float carry = UITheme.EaseOut(_carry);
        float lFistW = f, rFistW = f;
        if (f > 0f)
        {
            bool finisher = s.strike == MeleeStrike.Strike.Takedown && s.takedownWithFists;
            float jab = s.strike == MeleeStrike.Strike.Jab ? Extension(s.strikeProgress, MeleeStrike.ImpactAt(MeleeStrike.Strike.Jab)) : 0f;
            float cross = s.strike == MeleeStrike.Strike.Cross || finisher
                ? Extension(s.strikeProgress, MeleeStrike.ImpactAt(MeleeStrike.Strike.Cross)) * (finisher ? 1.12f : 1f) : 0f;

            // The double punch: a quick left jab out and back in the first part of the cross's
            // wind-up, then the right cross lands on the strike's own moment.
            if (s.strike == MeleeStrike.Strike.Cross)
            {
                float early = MeleeStrike.ImpactAt(MeleeStrike.Strike.Cross) * 0.85f;
                if (s.strikeProgress >= 0f && s.strikeProgress < early)
                    jab = Mathf.Max(jab, Extension(s.strikeProgress / early, 0.5f) * Body.doubleJabReach);
            }

            // The off hand pulls back to the chin as the other is thrown.
            Vector3 lFist = Vector3.LerpUnclamped(Guard(LeftGuard, -1f), Wide(JabReach, -1f), jab) + new Vector3(0.02f, 0.02f, -0.06f) * Mathf.Clamp01(cross);
            Vector3 rFist = Vector3.LerpUnclamped(Guard(RightGuard, 1f), Wide(CrossReach, 1f), cross) + new Vector3(-0.01f, 0.02f, -0.05f) * Mathf.Clamp01(jab);

            // A guard holds the fists knuckles-up, facing in; the punch turns them over
            // flat, knuckles to the target.
            float jt = Mathf.Clamp01(jab), ct = Mathf.Clamp01(cross);
            Quaternion lFistRot = Quaternion.Slerp(Look(view, new Vector3(0.25f, 0.55f, 0.8f), new Vector3(-0.8f, 0.1f, 0.2f)),
                                                   Look(view, new Vector3(0.08f, 0.05f, 1f), new Vector3(0f, 1f, 0f)), jt);
            Quaternion rFistRot = Quaternion.Slerp(Look(view, new Vector3(-0.25f, 0.55f, 0.8f), new Vector3(0.8f, 0.1f, 0.2f)),
                                                   Look(view, new Vector3(-0.08f, 0.05f, 1f), new Vector3(0f, 1f, 0f)), ct);

            // A single punch (the rifle carried in the left hand): the right fist is thrown from
            // its own guard at the crosshair, and the left stays on the rifle.
            if (carry > 0f)
            {
                Vector3 single = Vector3.LerpUnclamped(Guard(Body.rightGuard, 1f), Wide(Body.rightReach, 1f), jab);
                rFist = Vector3.Lerp(rFist, single, carry);
                rFistRot = Quaternion.Slerp(rFistRot, Quaternion.Slerp(
                    Look(view, new Vector3(-0.25f, 0.55f, 0.8f), new Vector3(0.8f, 0.1f, 0.2f)),
                    Look(view, new Vector3(-0.08f, 0.05f, 1f), new Vector3(0f, 1f, 0f)), jt), carry);
                lFist = view.InverseTransformPoint(gunL);
                lFistRot = gunLRot;
                lFistW = f * (1f - carry);
            }

            // A captured move (FPSKitMocap's first-person track) in place of the drawn one, fitted to this framing.
            if (Captured != null && Captured.Frames > 1)
                CapturedFists(view, Guard(LeftGuard, -1f), Guard(RightGuard, 1f), Wide(JabReach, -1f), Wide(CrossReach, 1f),
                              ref lFist, ref lFistRot, ref rFist, ref rFistRot);

            lPos = Vector3.Lerp(lPos, view.TransformPoint(lFist), f);
            rPos = Vector3.Lerp(rPos, view.TransformPoint(rFist), f);
            lRot = Quaternion.Slerp(lRot, lFistRot, f);
            rRot = Quaternion.Slerp(rRot, rFistRot, f);
        }

        Reach(_right, rPos, rRot, view.TransformDirection(new Vector3(0.55f, -0.7f, -0.2f)), rFistW);
        Squeeze(s, rFistW);
        Reach(_left, lPos, lRot, view.TransformDirection(new Vector3(-0.35f, -0.8f, 0.05f)), lFistW);
    }

    /// <summary>
    /// A captured first-person move playing on the fists (CapturedFirstPerson, made by FPSKitMocap from a phone video).
    /// Set by the capture preview; nothing in the game sets it until a captured move is approved and assigned.
    /// </summary>
    [System.NonSerialized] public CapturedFirstPerson Captured;
    [System.NonSerialized] public float CapturedTime;

    /// <summary>
    /// The captured hands fitted to this view's framing: the striking hand's path from its guard to the blow is turned
    /// and scaled onto ours (guard to reach, the fighting framing included), and the other hand moves with the same turn
    /// and scale from its own guard, only PlayerBodySettings.capturedOffHand of it (the hand the body hid from the phone).
    /// The performer's path, timing and hand turn are kept; where they land is ours.
    /// </summary>
    void CapturedFists(Transform view, Vector3 lGuard, Vector3 rGuard, Vector3 lReach, Vector3 rReach,
                       ref Vector3 lFist, ref Quaternion lRot, ref Vector3 rFist, ref Quaternion rRot)
    {
        var c = Captured;
        bool left = c.striker == 0;
        Vector3[] strike = left ? c.leftHand : c.rightHand;
        Vector3 captured = strike[c.ImpactFrame] - strike[0];
        Vector3 ours = left ? lReach - lGuard : rReach - rGuard;
        bool fits = c.striker >= 0 && c.striker <= 1 && captured.magnitude > 0.05f;
        float scale = fits ? ours.magnitude / captured.magnitude : 1f;
        Quaternion turn = fits ? Quaternion.FromToRotation(captured, ours) : Quaternion.identity;
        float t = CapturedTime;
        float lKeep = left || !fits ? 1f : Body.capturedOffHand, rKeep = !left || !fits ? 1f : Body.capturedOffHand;
        lFist = lGuard + turn * (CapturedFirstPerson.Sample(c.leftHand, t) - c.leftHand[0]) * (scale * lKeep);
        rFist = rGuard + turn * (CapturedFirstPerson.Sample(c.rightHand, t) - c.rightHand[0]) * (scale * rKeep);
        Quaternion lTurn = CapturedFirstPerson.Sample(c.leftHandTurn, t), rTurn = CapturedFirstPerson.Sample(c.rightHandTurn, t);
        lRot = view.rotation * turn * Quaternion.Slerp(c.leftHandTurn[0], lTurn, lKeep);
        rRot = view.rotation * turn * Quaternion.Slerp(c.rightHandTurn[0], rTurn, rKeep);
    }

    /// <summary>The trigger finger pulls on each shot and lets go: index only, the grip's other fingers stay closed.</summary>
    void Squeeze(State s, float fist)
    {
        if (_right == null || _right.squeeze == null || fist > 0.01f) return;
        float pull = s.sinceShot >= 0f && s.sinceShot < 0.09f ? Mathf.Sin(s.sinceShot / 0.09f * Mathf.PI) : 0f;
        if (pull <= 0f) return;
        // The index finger's three joints follow the thumb's three in the list (HumanBodyBones order).
        for (int i = 3; i < 6 && i < _right.fingers.Length; i++)
            _right.fingers[i].localRotation = Quaternion.Slerp(_right.hold[i], _right.squeeze[i], pull);
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
        _legs.localRotation = Quaternion.Euler(-16f * drive, 0f, 0f);

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

            // High and close: the knee comes up into the bottom of the view and the boot is
            // driven out in front of the chest, sole first, big in the frame.
            Vector3 chambered = new Vector3(rest.x - 0.02f, 1.05f, 0.28f);
            Vector3 struck = new Vector3(0.04f, 1.5f, 0.5f);

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

        leg.foot.rotation = Quaternion.AngleAxis(-85f * toeUp, player.right) * (player.rotation * leg.footToBody);
    }
}
