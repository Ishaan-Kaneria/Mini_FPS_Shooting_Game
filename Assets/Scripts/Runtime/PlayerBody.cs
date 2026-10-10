using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The player as a real body: a full soldier (FPSKit > Player > Build Body), animated by the
/// same Mixamo rifle clips the enemies walk in, standing under the camera.
///
/// - **Drawn three ways.** Each garment's mesh is split by bone weight at start: the arms
///   (sleeves, gloves) go on the FirstPerson layer and are drawn by their own camera, stacked
///   on the main one, at their own field of view (Settings > Video), so they never stretch at a
///   wide FOV and never go into walls; the rest of the body without the head is drawn by the
///   main camera, so the legs and chest are there looking down; and arms and head together
///   are drawn into the shadows only, so the shadow on the ground is a whole person.
/// - **The camera never follows the head.** The body follows the camera: each frame it is
///   slid (smoothly) so its eyes are where the camera is, and a little back when looking down.
///   A walk cycle's head motion therefore moves the arms and rifle, not the view.
/// - **Clips for everything the body does**: walk, run, sprint and crouch in eight directions,
///   idle, reload, the strikes and the kick. A strike's clip is driven by MeleeStrike's own
///   progress so its blow lands on the frame the damage does; the reload's by Weapon's.
/// - **On top of the clips**: the spine turns the rifle onto the crosshair; aiming down sights
///   slides the arms until the rear sight sits on the eye; the rifle hangs on a spring under
///   the hand (it lags and settles), kicks on each shot and settles, and the left hand is
///   reached onto the handguard. A hit makes the arms and rifle flinch, not the view.
///
/// Added by PlayerMotor when the settings name a body; the old arms-and-legs view model
/// (<see cref="ViewModelRig"/>) is not built then. Values: <see cref="PlayerBodySettings"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]   // after the motor has placed the camera and the weapon and melee have updated
public class PlayerBody : MonoBehaviour
{
    /// <summary>The settings name a body: the player is drawn as one.</summary>
    public static bool Wanted => PlayerBodySettings.Active.bodyPrefab != null;

    static PlayerBodySettings S => PlayerBodySettings.Active;

    PlayerMotor _motor;
    Weapon _weapon;
    MeleeStrike _melee;
    Health _health;
    CharacterController _cc;
    Camera _cam, _armsCam;
    int _firstPerson = -1, _playerLayer;

    Transform _body;
    PlayerBodyRig _rig;
    Animator _anim;
    Transform _hips, _spine, _chestLow, _chest, _neck, _head;
    Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
    LimbIK.HandFrame _lFrame, _rFrame;

    // The gun in the hands.
    Transform _gun;
    ViewModelSpec _spec;
    WeaponData _builtFor;
    List<Renderer> _hiddenSceneList;
    List<Renderer> _hiddenScene => _hiddenSceneList ??= new List<Renderer>();
    Transform _dummyHand;

    // Placement and aim.
    Vector3 _offset, _armsAt;
    bool _armsPlaced;
    float _lift;
    float _aimYaw, _aimPitch;
    bool _placed;

    // The rifle's spring (weight) and kick (recoil).
    Vector3 _swayPos, _swayVel, _swayTurn, _swayTurnVel;
    Vector3 _kickPos, _kickVel, _kickTurn, _kickTurnVel;
    Vector3 _lastGripPos, _lastGripVel;
    Quaternion _lastGripRot;
    bool _hasLast;
    float _leftHand = 1f;
    Vector3 _flinch, _flinchVel;

    // Where the rifle is: 0 right hand, 1 left hand (low, by the handguard), 2 sling. Eased.
    float _toLeft, _toSling, _sprint, _turn, _hipsFacing;
    Transform _lThigh, _rThigh;
    bool _clipStrike, _gunLedOn, _gunInWorld, _hasFists, _hasCrouchReload;
    Quaternion _gunLed = Quaternion.identity, _gunLead = Quaternion.identity;
    float _aimW, _rightLead;

    int _actions = -1, _leftCarry = -1, _kickLayer = -1;
    float _actionsW, _leftCarryW, _kickW;
    string _actionState = "", _kickState = "";
    string _baseState = "Idle";

    static readonly int MoveX = Animator.StringToHash("MoveX");
    static readonly int MoveZ = Animator.StringToHash("MoveZ");
    static readonly int CrouchId = Animator.StringToHash("Crouch");
    static readonly int AimId = Animator.StringToHash("Aim");
    static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
    static readonly int MoveRate = Animator.StringToHash("MoveRate");
    static readonly int ActionTime = Animator.StringToHash("ActionTime");
    static readonly int KickTime = Animator.StringToHash("KickTime");

    /// <summary>The body is built and holding a gun.</summary>
    public bool Built => _anim != null && _gun != null;

    /// <summary>
    /// What the body acts out this frame. Read from the player's own parts (motor, weapon,
    /// melee); the preview tool (FPSKit > Player > Preview Body) sets it instead, so every
    /// pose can be seen and tuned without playing.
    /// </summary>
    public struct Drive
    {
        public Vector3 velocity;
        public bool crouching, lowered, reloading;
        public float reload, aim;
        public MeleeStrike.Strike strike;
        public float strikeProgress, gunAway;
        public Vector3 handsOffset, handsRotation;
    }

    /// <summary>The preview's drive, used instead of the player's parts while <see cref="previewing"/>.</summary>
    [System.NonSerialized] public Drive preview;
    [System.NonSerialized] public bool previewing;
    Drive _d;

    Drive Read()
    {
        if (previewing) return preview;
        var d = new Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
        d.velocity = _cc != null ? _cc.velocity : (_motor != null ? _motor.Velocity : Vector3.zero);
        d.crouching = _motor != null && _motor.IsCrouching;
        if (_weapon != null)
        {
            d.lowered = _weapon.IsLowered;
            d.reloading = _weapon.IsReloading;
            d.reload = _weapon.ReloadProgress;
            d.aim = _weapon.AimProgress;
            d.handsOffset = _weapon.HandsOffset;
            d.handsRotation = _weapon.HandsRotation;
        }
        if (_melee != null)
        {
            d.strike = _melee.Current;
            d.strikeProgress = _melee.StrikeProgress;
            d.gunAway = _melee.GunAway;
        }
        return d;
    }

    // ======================================================================

    void Start()
    {
        _motor = GetComponent<PlayerMotor>();
        _melee = GetComponent<MeleeStrike>();
        _health = GetComponent<Health>();
        _cc = GetComponent<CharacterController>();
        _weapon = GetComponentInChildren<Weapon>();
        _cam = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
        _firstPerson = LayerMask.NameToLayer("FirstPerson");
        _playerLayer = LayerMask.NameToLayer("Player");
        if (_playerLayer < 0) _playerLayer = 0;
        if (S.bodyPrefab == null || _cam == null || _firstPerson < 0) { enabled = false; return; }

        BuildBody();
        BuildArmsCamera();
        if (_weapon != null)
        {
            _weapon.Equipped += OnEquipped;
            _weapon.Fired += OnFired;
            BuildGun(_weapon.data);
        }
        if (_health != null) _health.Damaged += OnDamaged;
    }

    void OnDestroy()
    {
        if (_weapon != null) { _weapon.Equipped -= OnEquipped; _weapon.Fired -= OnFired; }
        if (_health != null) _health.Damaged -= OnDamaged;
    }

    void BuildBody()
    {
        var go = Instantiate(S.bodyPrefab, transform.position, transform.rotation);
        go.name = "PlayerBody";
        _body = go.transform;
        _body.SetParent(transform, true);
        _rig = go.GetComponent<PlayerBodyRig>();
        _anim = _rig != null && _rig.animator != null ? _rig.animator : go.GetComponentInChildren<Animator>();
        _anim.applyRootMotion = false;
        _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
        _hips = B(HumanBodyBones.Hips);
        _spine = B(HumanBodyBones.Spine);
        _chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
        _chestLow = B(HumanBodyBones.Chest);
        if (_chestLow == _chest) _chestLow = null;
        _neck = B(HumanBodyBones.Neck);
        _lThigh = B(HumanBodyBones.LeftUpperLeg);
        _rThigh = B(HumanBodyBones.RightUpperLeg);
        _head = B(HumanBodyBones.Head);
        _lUpper = B(HumanBodyBones.LeftUpperArm); _lLower = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
        _rUpper = B(HumanBodyBones.RightUpperArm); _rLower = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
        _lFrame = LimbIK.HandFrame.Measure(_lHand, B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftIndexProximal),
                                           B(HumanBodyBones.LeftLittleProximal), right: false);
        _rFrame = LimbIK.HandFrame.Measure(_rHand, B(HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.RightIndexProximal),
                                           B(HumanBodyBones.RightLittleProximal), right: true);

        _actions = _anim.GetLayerIndex("Actions");
        _leftCarry = _anim.GetLayerIndex("LeftCarry");
        _kickLayer = _anim.GetLayerIndex("Kick");
        _hasFists = Has(_actions, "Actions", "Fists");
        _hasCrouchReload = Has(_actions, "Actions", "ReloadCrouch");

        SplitMeshes();
    }

    /// <summary>
    /// Each garment three ways, by which bone a vertex mostly follows: the arms to the arms
    /// camera, the body without the head to the main camera, the arms and head to the shadows
    /// only. The new renderers hang off this object, not the skeleton (a child added to the
    /// skeleton in play would make the Animator rebind).
    /// </summary>
    void SplitMeshes()
    {
        // The arms from the elbow down, and the upper arm's lower part: the shoulder stays with the
        // body (seen from the arms camera's place, it showed as a spike at the screen's edge).
        var arms = new HashSet<Transform>();
        foreach (var t in new[] { _lLower, _rLower }) if (t != null) foreach (var c in t.GetComponentsInChildren<Transform>(true)) arms.Add(c);
        var head = new HashSet<Transform>();
        if (_neck != null) foreach (var c in _neck.GetComponentsInChildren<Transform>(true)) head.Add(c);

        foreach (var smr in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
            var mesh = smr.sharedMesh;
            if (!mesh.isReadable) { smr.gameObject.layer = _playerLayer; continue; }

            var bones = smr.bones;
            var weights = mesh.boneWeights;
            if (weights.Length != mesh.vertexCount) continue;
            var binds = mesh.bindposes;
            var verts = mesh.vertices;
            // Where a bone is in the mesh's own (bind) space.
            Vector3 BindAt(Transform bone)
            {
                int i = System.Array.IndexOf(bones, bone);
                return i >= 0 && i < binds.Length ? binds[i].inverse.MultiplyPoint3x4(Vector3.zero) : Vector3.zero;
            }
            float Along(Vector3 v, Transform from, Transform to)
            {
                Vector3 a = BindAt(from), d = BindAt(to) - a;
                return d.sqrMagnitude > 1e-6f ? Vector3.Dot(v - a, d) / d.sqrMagnitude : 0f;
            }
            var kind = new byte[weights.Length];   // 0 body, 1 arm, 2 head
            for (int v = 0; v < weights.Length; v++)
            {
                int bi = weights[v].boneIndex0;
                var bone = bi >= 0 && bi < bones.Length ? bones[bi] : null;
                bool arm = bone != null && (arms.Contains(bone)
                    || bone == _lUpper && Along(verts[v], _lUpper, _lLower) > S.armsFromUpperArm
                    || bone == _rUpper && Along(verts[v], _rUpper, _rLower) > S.armsFromUpperArm);
                kind[v] = (byte)(arm ? 1 : bone != null && head.Contains(bone) ? 2 : 0);
            }

            Mesh Subset(string suffix, System.Func<byte, bool> keep, int need)
            {
                var m = Instantiate(mesh);
                m.name = mesh.name + suffix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        int votes = (keep(kind[tris[i]]) ? 1 : 0) + (keep(kind[tris[i + 1]]) ? 1 : 0) + (keep(kind[tris[i + 2]]) ? 1 : 0);
                        if (votes >= need) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); }
                    }
                    m.SetTriangles(kept, sub);
                }
                return m;
            }

            SkinnedMeshRenderer Copy(string name, Mesh m, int layer, UnityEngine.Rendering.ShadowCastingMode shadows, bool twoSided = false)
            {
                var go = new GameObject(smr.name + name);
                go.transform.SetParent(transform, false);
                go.layer = layer;
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = m;
                r.bones = bones;
                r.rootBone = smr.rootBone;
                r.sharedMaterials = twoSided ? TwoSided(smr.sharedMaterials) : smr.sharedMaterials;
                r.shadowCastingMode = shadows;
                r.receiveShadows = true;
                r.updateWhenOffscreen = true;
                return r;
            }

            // A triangle across a seam goes to the arms only if all of it is arm, to the body if any of it
            // is body; the shadow copy is what the body leaves out, so the shadow is the whole person.
            // Two-sided: where the sleeve is cut from the body its open end shows cloth, not a see-through flap.
            Copy("_Arms", Subset("_Arms", k => k == 1, 3), _firstPerson, UnityEngine.Rendering.ShadowCastingMode.Off, twoSided: true);
            Copy("_Shadow", Subset("_ArmsHead", k => k != 0, 3), _playerLayer, UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);
            smr.sharedMesh = Subset("_Body", k => k == 0, 1);
            smr.gameObject.layer = _playerLayer;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            smr.updateWhenOffscreen = true;
        }
    }

    /// <summary>Copies of the materials drawn on both faces (URP Lit's Render Face: Both), made once per material.</summary>
    Material[] TwoSided(Material[] mats)
    {
        var result = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null || !mats[i].HasProperty("_Cull")) { result[i] = mats[i]; continue; }
            if (!_twoSided.TryGetValue(mats[i], out var copy))
            {
                copy = new Material(mats[i]) { name = mats[i].name + " (two-sided)" };
                copy.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                _twoSided[mats[i]] = copy;
            }
            result[i] = copy;
        }
        return result;
    }

    Dictionary<Material, Material> _twoSidedCache;
    Dictionary<Material, Material> _twoSided => _twoSidedCache ??= new Dictionary<Material, Material>();

    /// <summary>The arms and gun's own camera: stacked on the main one, its own field of view, depth cleared.</summary>
    void BuildArmsCamera()
    {
        var go = new GameObject("ArmsCamera");
        go.transform.SetParent(_cam.transform, false);
        go.transform.localPosition = S.armsViewOffset;
        _armsCam = go.AddComponent<Camera>();
        _armsCam.cullingMask = 1 << _firstPerson;
        _armsCam.nearClipPlane = S.armsNearClip;
        _armsCam.farClipPlane = 10f;
        _armsCam.fieldOfView = GameSettings.ArmsFieldOfView;
        var data = _armsCam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderShadows = false;
        data.renderPostProcessing = false;
        var main = _cam.GetUniversalAdditionalCameraData();
        if (!main.cameraStack.Contains(_armsCam)) main.cameraStack.Add(_armsCam);
        _cam.cullingMask &= ~(1 << _firstPerson);
    }

    void OnEquipped(Weapon w) => BuildGun(w.data);

    /// <summary>The data's gun, in the body's right hand; the scene's own gun hidden; the muzzle moved onto it.</summary>
    void BuildGun(WeaponData data)
    {
        if (data == _builtFor && _gun != null) return;
        _builtFor = data;
        if (_gun != null) Gone(_gun.gameObject);
        _gun = null; _spec = null;
        foreach (var r in _hiddenScene) if (r != null) r.enabled = true;
        _hiddenScene.Clear();
        if (data == null || data.viewModel == null) return;

        var gun = Instantiate(data.viewModel, transform, false);
        gun.name = data.viewModel.name;
        _spec = gun.GetComponent<ViewModelSpec>();
        if (_spec == null) { Gone(gun); return; }
        _gun = gun.transform;
        _gunInWorld = false;
        foreach (var t in gun.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _firstPerson;
        foreach (var c in gun.GetComponentsInChildren<Collider>(true)) Gone(c);
        foreach (var r in gun.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Its shadow, from a copy drawn into the shadows only, so the body's shadow holds a rifle.
        foreach (var mr in gun.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var copy = new GameObject(mr.name + "_Shadow");
            copy.layer = _playerLayer;
            copy.transform.SetParent(mr.transform, false);
            copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var cr = copy.AddComponent<MeshRenderer>();
            cr.sharedMaterials = mr.sharedMaterials;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        _hasLast = false;
        if (_weapon == null) return;
        foreach (var r in _weapon.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            r.enabled = false;
            _hiddenScene.Add(r);
        }
        // The weapon's reload moves this gun's own magazine (out and back in on its beats); its
        // left hand is a stand-in, since the body's reload clip moves the real one.
        if (_dummyHand == null)
        {
            _dummyHand = new GameObject("ReloadHandStandIn").transform;
            _dummyHand.SetParent(_weapon.transform, false);
        }
        _weapon.BindReloadParts(_spec.magazine, _dummyHand);
        _weapon.HasArms = true;
        _hasLast = false;
    }

    // ======================================================================
    // The clips
    // ======================================================================

    void Update()
    {
        if (_anim == null || previewing) return;
        Tick(Time.deltaTime);
    }

    void Tick(float dt)
    {
        _d = Read();
        if (_armsCam != null)
        {
            _armsCam.fieldOfView = GameSettings.ArmsFieldOfView;
        }

        // The body's velocity in its own space, for the eight-way blend.
        Vector3 v = _d.velocity;
        v.y = 0f;
        // In metres a second at the clips' own size: the body may be a little smaller or larger.
        Vector3 local = transform.InverseTransformDirection(v) / Mathf.Max(0.1f, _body.lossyScale.x);
        _anim.SetFloat(MoveX, local.x, 0.12f, dt);
        _anim.SetFloat(MoveZ, local.z, 0.12f, dt);
        float speed = local.magnitude;
        float share = speed * _body.lossyScale.x / S.sprintClipSpeed;
        _anim.SetFloat(MoveRate, Mathf.Max(1f, share), 0.2f, dt);
        // Sprinting: the rifle is carried, not aimed.
        float sprint = Mathf.InverseLerp(S.sprintBetween.x, S.sprintBetween.y, share);
        _sprint = Mathf.MoveTowards(_sprint, sprint, dt * 4f);
        _anim.SetFloat(CrouchId, _d.crouching ? 1f : 0f, 0.2f, dt);
        bool lowered = _d.lowered;
        _anim.SetFloat(AimId, lowered ? 0f : 1f, 0.2f, dt);
        _anim.SetFloat(IdleVariant, lowered ? 1f : 0f, 0.2f, dt);

        string want = speed > 0.2f ? "Move" : "Idle";
        if (want != _baseState && (want == "Move" || speed < 0.1f))
        {
            _anim.CrossFadeInFixedTime(want, 0.25f, 0);
            _baseState = want;
        }

        UpdateActions(dt);
    }

    /// <summary>The upper body's work (reload, punches, the butt) and the kick, each clip driven by its own progress.</summary>
    void UpdateActions(float dt)
    {
        string action = "", kick = "";
        float turn = 0f;
        float actionTime = 0f, kickTime = 0f;
        bool leftCarry = false;
        float toSling = 0f, toLeft = 0f;

        var strike = _d.strike;
        float k = _d.strikeProgress;
        _clipStrike = false;
        if (strike != MeleeStrike.Strike.None && k >= 0f && S.TryStrike(strike, out var clip))
        {
            _clipStrike = true;
            // A kick's clip brings its own hips (and turn); a punch is laid on the stance's hips, so the
            // way those face is added (the rifle stance is bladed).
            turn = strike == MeleeStrike.Strike.Kick ? clip.turn : clip.turn + _hipsFacing;
            float t = ClipTime(k, MeleeStrike.ImpactAt(strike), clip);
            if (strike == MeleeStrike.Strike.Kick) { kick = clip.state; kickTime = t; }
            else { action = clip.state; actionTime = t; }
            if (clip.rifle == PlayerBodySettings.RifleDuring.LeftHand) { leftCarry = true; toLeft = 1f; }
            if (clip.rifle == PlayerBodySettings.RifleDuring.Sling) toSling = 1f;
        }
        else if (_d.reloading)
        {
            action = _d.crouching && _hasCrouchReload ? "ReloadCrouch" : "Reload";
            actionTime = _d.reload;
        }
        else if (_d.gunAway > 0.5f && _hasFists)
        {
            action = "Fists";   // fists up between punches: loops on its own clock
            actionTime = -1f;
        }

        // Fists up between punches: the rifle stays on the sling (unless this punch's clip
        // keeps it in the left hand).
        if (_d.gunAway > 0.5f && toLeft <= 0f) toSling = 1f;

        SetLayer(_actions, ref _actionsW, ref _actionState, action, ActionTime, actionTime, dt);
        SetLayer(_kickLayer, ref _kickW, ref _kickState, kick, KickTime, kickTime, dt);
        if (_leftCarry >= 0)
        {
            _leftCarryW = Mathf.MoveTowards(_leftCarryW, leftCarry ? 1f : 0f, dt * 6f);
            _anim.SetLayerWeight(_leftCarry, Smooth(_leftCarryW));
        }
        _toLeft = Mathf.MoveTowards(_toLeft, toLeft, dt * 6f);
        _turn = Mathf.Lerp(_turn, turn, 1f - Mathf.Exp(-30f * dt));
        _toSling = Mathf.MoveTowards(_toSling, toSling, dt * 5f);
    }

    void SetLayer(int layer, ref float weight, ref string current, string state, int timeParam, float time, float dt)
    {
        if (layer < 0) return;
        bool on = !string.IsNullOrEmpty(state);
        if (on && state != current)
        {
            _anim.CrossFadeInFixedTime(state, 0.08f, layer);
            current = state;
        }
        if (on && time >= 0f) _anim.SetFloat(timeParam, Mathf.Clamp01(time));
        weight = Mathf.MoveTowards(weight, on ? 1f : 0f, dt * (on ? 10f : 5f));
        _anim.SetLayerWeight(layer, Smooth(weight));
        if (!on && weight <= 0f) current = "";
    }

    /// <summary>
    /// The clip's time for a share of the strike: the clip's own moment of contact falls on the
    /// strike's (so the blow lands where the damage does), the rest of the clip shared out evenly either side.
    /// </summary>
    static float ClipTime(float k, float strikeImpact, PlayerBodySettings.StrikeClip clip)
    {
        strikeImpact = Mathf.Clamp(strikeImpact, 0.01f, 0.99f);
        float a = clip.range.y > clip.range.x ? clip.range.x : 0f;
        float b = clip.range.y > clip.range.x ? clip.range.y : 1f;
        float i = Mathf.Clamp(clip.impact, a, b);
        return k <= strikeImpact
            ? a + k / strikeImpact * (i - a)
            : i + (k - strikeImpact) / (1f - strikeImpact) * (b - i);
    }

    // ======================================================================
    // On top of the clips
    // ======================================================================

    void LateUpdate()
    {
        if (_anim == null || _cam == null || previewing) return;
        LateTick(Mathf.Max(1e-4f, Time.deltaTime));
    }

    void LateTick(float dt)
    {
        PlaceBody(dt);
        PlaceGun(dt, measureOnly: true);
        AimSpine(dt);
        Flinch(dt);
        PlaceGun(dt, measureOnly: false);
        Sights(dt);
        RightHandOnRifle();
        LeftHandOnRifle(dt);
        MagazineInHand();
        PlaceMuzzle();
    }

    /// <summary>The body slid under the camera so its eyes are where the camera is; back a little when looking down.</summary>
    void PlaceBody(float dt)
    {
        if (_head == null) return;
        // Which way the stance's hips face, across the thighs, from the body's forward (before the turn below).
        if (_lThigh != null && _rThigh != null)
        {
            Vector3 across = _body.InverseTransformDirection(_rThigh.position - _lThigh.position);
            Vector3 facing = Vector3.Cross(across, Vector3.up);
            if (_actionsW < 0.01f && _kickW < 0.01f) _hipsFacing = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        }
        // Turned against the way a strike's clip faces, so its blow lands ahead; leaned back for a kick.
        _body.rotation = transform.rotation * Quaternion.AngleAxis(-S.kickLean * Smooth(_kickW), Vector3.right)
                                            * Quaternion.AngleAxis(-_turn, Vector3.up);
        Vector3 eye = _head.position + _body.forward * S.eyeAhead + Vector3.up * S.eyeUp;
        Vector3 toCam = _cam.transform.position - eye;
        toCam.y = 0f;
        float down = Mathf.Clamp01(Vector3.Dot(_cam.transform.forward, Vector3.down));
        Vector3 back = -Flat(_cam.transform.forward).normalized * S.lookDownBack * down;
        Vector3 want = (_body.position - transform.position) + toCam + back;
        want.y = 0f;
        _offset = _placed ? Vector3.Lerp(_offset, want, 1f - Mathf.Exp(-S.followRate * dt)) : want;
        // Up or down, slowly and only a little, so the eyes meet the camera on average (crouched
        // too) while each stride's rise and fall still moves the arms and rifle, not the view.
        float lift = Mathf.Clamp(_lift + _cam.transform.position.y - eye.y, S.liftLimits.x, S.liftLimits.y);
        _lift = _placed ? Mathf.Lerp(_lift, lift, 1f - Mathf.Exp(-S.liftRate * dt)) : lift;
        _offset.y = _lift;
        _placed = true;
        _body.position = transform.position + _offset;
    }

    /// <summary>The spine turns the rifle onto the crosshair: chest-led, shared down the spine, on top of the clip.</summary>
    void AimSpine(float dt)
    {
        if (_gun == null || _spec == null) return;
        Vector3 bore = _gun.forward;
        Vector3 want = _cam.transform.forward;
        // Less while a clip moves the rifle itself (a strike, the reload) or it is out of the right hand.
        // Through a kick the rifle stays pointed ahead (the body turns under it), only its pitch eased.
        float on = 1f - Mathf.Max(Mathf.Max(Mathf.Max(_toSling, _toLeft), Smooth(_sprint)), Mathf.Max(_kickW * 0.6f, _actionsW * 0.8f));
        float onYaw = 1f - Mathf.Max(Mathf.Max(Mathf.Max(_toSling, _toLeft), Smooth(_sprint)), _actionsW * 0.8f);
        float yaw = Mathf.Clamp(Vector3.SignedAngle(Flat(bore), Flat(want), Vector3.up), -S.aimLimit, S.aimLimit) * onYaw;
        float pitch = Mathf.Clamp(Elevation(bore) - Elevation(want), -80f, 80f) * on;
        _aimYaw = Mathf.Lerp(_aimYaw, yaw, 1f - Mathf.Exp(-S.aimRate * dt));
        _aimPitch = Mathf.Lerp(_aimPitch, pitch, 1f - Mathf.Exp(-S.aimRate * dt));

        Turn(_spine, Vector3.up, _aimYaw * 0.3f);
        Turn(_chestLow, Vector3.up, _aimYaw * 0.3f);
        Turn(_chest, Vector3.up, _aimYaw * 0.4f);
        // Up and down: the spine a share, the arms the rest at the shoulders. Bent down the whole
        // way at the spine, looking at the floor put the chest under the eyes.
        Vector3 side = Quaternion.AngleAxis(_aimYaw, Vector3.up) * _body.right;
        float spine = _aimPitch * S.spinePitchShare;
        Turn(_spine, side, spine * 0.2f);
        Turn(_chestLow, side, spine * 0.3f);
        Turn(_chest, side, spine * 0.5f);
        Turn(_rUpper, side, _aimPitch - spine);
        Turn(_lUpper, side, _aimPitch - spine);
    }

    /// <summary>
    /// The rifle onto where it is carried: the right hand's grip (with the spring and the kick
    /// on it), the left hand by the handguard for a single punch, or the sling across the back.
    /// </summary>
    void PlaceGun(float dt, bool measureOnly)
    {
        if (_gun == null || _spec == null || _rig == null || _rig.rifleGrip == null) return;
        Transform grip = _rig.rifleGrip;

        if (!measureOnly) Weight(dt, grip);

        // In the right hand: the gun's own right-hand grip on the socket.
        Quaternion rot = grip.rotation * Quaternion.Euler(_swayTurn + _kickTurn);
        Vector3 gripOffset = grip.TransformDirection(_swayPos + _kickPos);
        Vector3 gripLocal = Vector3.Scale(_gun.InverseTransformPoint(_spec.rightHand.position), _gun.lossyScale);
        // (The grip's local position in the gun, scaled: the gun's pivot is its rear sight.)
        Vector3 pos = grip.position + gripOffset - rot * gripLocal;
        Quaternion inHand = rot;

        // A strike with no clip of its own (the rifle butt, the rifle slid aside for a kick) is
        // drawn the way it always was: MeleeStrike's turn and offset, in the view's frame, on the
        // rifle; the right hand is then reached onto the grip (RightHandOnRifle).
        _gunLed = Quaternion.identity;
        _gunLedOn = false;
        if (!_clipStrike && (_d.handsOffset.sqrMagnitude > 1e-8f || _d.handsRotation.sqrMagnitude > 1e-6f) && _d.gunAway < 0.01f)
        {
            Quaternion view = _cam.transform.rotation;
            _gunLed = view * Quaternion.Euler(_d.handsRotation) * Quaternion.Inverse(view);
            rot = _gunLed * rot;
            pos = grip.position + _gunLed * (pos - grip.position) + _cam.transform.TransformVector(_d.handsOffset);
            _gunLedOn = true;
        }

        // Aiming down sights: raised until the rear sight is on the eye line at the gun's own eye
        // relief, the bore along the view, the stock back by the cheek. The sway is lessened and
        // the kick kept, so the shot still climbs through the sights. Both hands follow it (IK).
        _aimW = Smooth(_d.aim) * (1f - Mathf.Max(Mathf.Max(_toSling, _toLeft), Mathf.Max(_actionsW, _kickW)));
        if (_aimW > 0.001f)
        {
            Transform view = _cam.transform;
            Quaternion adsRot = view.rotation * Quaternion.Euler(_swayTurn * S.aimSwayShare + _kickTurn);
            Vector3 adsPos = view.TransformPoint(_spec.aimPosition) + view.TransformDirection(_swayPos * S.aimSwayShare + _kickPos);
            pos = Vector3.Lerp(pos, adsPos, _aimW);
            rot = Quaternion.Slerp(rot, adsRot, _aimW);
        }
        _gunLead = rot * Quaternion.Inverse(inHand);
        _rightLead = Mathf.Max(_gunLedOn ? 1f : 0f, _aimW);

        // In the left hand, held by the handguard, hanging low: the hand's frame says how.
        if (_toLeft > 0.001f && _lHand != null && _lFrame.valid)
        {
            Quaternion handFrame = _lHand.rotation * Quaternion.Inverse(_lFrame.toLocal);   // fingers ahead, back of hand up
            Quaternion leftGripRot = Quaternion.Inverse(_gun.rotation) * _spec.leftHand.rotation;
            Quaternion lrot = handFrame * Quaternion.Inverse(leftGripRot);
            Vector3 lpos = _lHand.position - lrot * Vector3.Scale(_gun.InverseTransformPoint(_spec.leftHand.position), _gun.lossyScale);
            float w = Smooth(_toLeft);
            pos = Vector3.Lerp(pos, lpos, w);
            rot = Quaternion.Slerp(rot, lrot, w);
        }

        // On the sling.
        if (_toSling > 0.001f && _rig.slingPoint != null)
        {
            float w = Smooth(_toSling);
            pos = Vector3.Lerp(pos, _rig.slingPoint.position, w);
            rot = Quaternion.Slerp(rot, _rig.slingPoint.rotation, w);
        }

        _gun.SetPositionAndRotation(pos, rot);
        SlungLayer();
    }

    /// <summary>The spring the rifle hangs on under the hand (weight), and its kick (recoil), stepped.</summary>
    void Weight(float dt, Transform grip)
    {
        Vector3 target = Vector3.zero, turnTarget = Vector3.zero;
        if (_hasLast)
        {
            Vector3 vel = (grip.position - _lastGripPos) / dt;
            Vector3 acc = (vel - _lastGripVel) / dt;
            _lastGripVel = vel;
            target = Vector3.ClampMagnitude(grip.InverseTransformDirection(-acc) * S.swayInertia, S.swayLimit.x);
            (grip.rotation * Quaternion.Inverse(_lastGripRot)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude > 0.5f)
                turnTarget = Vector3.ClampMagnitude(grip.InverseTransformDirection(axis) * (-angle / dt * S.swayTurnInertia), S.swayLimit.y);
        }
        _lastGripPos = grip.position;
        _lastGripRot = grip.rotation;
        _hasLast = true;

        Spring(ref _swayPos, ref _swayVel, target, S.swayStiffness, S.swayDamping, dt);
        Spring(ref _swayTurn, ref _swayTurnVel, turnTarget, S.swayStiffness, S.swayDamping, dt);

        Spring(ref _kickPos, ref _kickVel, Vector3.zero, S.recoilStiffness, S.recoilDamping, dt);
        Spring(ref _kickTurn, ref _kickTurnVel, Vector3.zero, S.recoilStiffness, S.recoilDamping, dt);
    }

    void OnFired(Weapon w)
    {
        float share = w.IsAiming ? S.recoilAimShare : 1f;
        // Back into the shoulder and the muzzle up; a touch to one side.
        _kickVel += new Vector3(0f, 0f, -S.recoilBack) * share * S.recoilStiffness * 0.12f;
        _kickTurnVel += new Vector3(-S.recoilPitch, Random.Range(-S.recoilYaw, S.recoilYaw), 0f) * share * S.recoilStiffness * 0.12f;
    }

    void OnDamaged(Health h, DamageInfo info)
    {
        if (info.amount <= 0f) return;
        Vector3 dir = info.direction.sqrMagnitude > 0.001f ? info.direction.normalized : -_body.forward;
        // A push on the upper body away from the hit: the arms and rifle jolt, the view does not.
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        _flinchVel += axis * S.flinchKick * Mathf.Clamp(info.amount / 25f, 0.3f, 1.5f);
    }

    void Flinch(float dt)
    {
        Spring(ref _flinch, ref _flinchVel, Vector3.zero, S.flinchStiffness, S.flinchDamping, dt);
        _flinch = Vector3.ClampMagnitude(_flinch, 20f);
        float a = _flinch.magnitude;
        if (a > 0.05f && _chest != null) _chest.rotation = Quaternion.AngleAxis(a, _flinch / a) * _chest.rotation;
    }

    /// <summary>
    /// The arms camera: a little behind and below the eye at the hip (so the arms are framed),
    /// at the eye when aiming down sights (the rifle is raised to it), tipped down at a sprint.
    /// </summary>
    void Sights(float dt)
    {
        if (_armsCam == null) return;
        // Looking down the offset goes: behind the eye in view space is above the shoulders then.
        float down = Mathf.Clamp01(Vector3.Dot(_cam.transform.forward, Vector3.down) / Mathf.Max(0.05f, S.armsOffsetGoneAt));
        // At the eye too for the fists and the kick: from behind it, an arm thrown forward showed the
        // back of its shoulder, and the open end of the sleeve where the arms are cut from the body.
        float fighting = Mathf.Max(_kickW, _clipStrike ? _actionsW : 0f);
        Vector3 want = Vector3.Lerp(S.armsViewOffset * (1f - Smooth(down)), S.aimArmsOffset, Mathf.Max(_aimW, Smooth(fighting)));
        _armsAt = _armsPlaced ? Vector3.Lerp(_armsAt, want, 1f - Mathf.Exp(-S.sightsRate * dt)) : want;
        _armsPlaced = true;
        _armsCam.transform.localPosition = _armsAt + Vector3.down * (S.sprintArmsDrop * Smooth(_sprint) * (1f - _aimW));
        _armsCam.transform.localRotation = Quaternion.Euler(S.sprintArmsTilt * Smooth(_sprint) * (1f - _aimW), 0f, 0f);
    }

    /// <summary>The right hand kept on the grip while the rifle is moved by something other than the hand: raised to the eye, or a strike with no clip.</summary>
    void RightHandOnRifle()
    {
        if (_rightLead <= 0.001f || _spec == null || _spec.rightHand == null || _rUpper == null) return;
        Quaternion u = _rUpper.rotation, l = _rLower.rotation, hand = _rHand.rotation;
        Vector3 pole = _rUpper.position + _body.TransformDirection(new Vector3(0.5f, -0.8f, -0.2f));
        LimbIK.Solve(_rUpper, _rLower, _rHand, _spec.rightHand.position, pole);
        // Turned as the gun's grip says (aiming), or with the gun (a strike moves it).
        _rHand.rotation = _aimW > 0.001f && _rFrame.valid
            ? Quaternion.Slerp(_gunLead * hand, _rFrame.World(_spec.rightHand.forward, _spec.rightHand.up), _aimW / _rightLead)
            : _gunLead * hand;
        if (_rightLead < 0.999f)
        {
            _rUpper.rotation = Quaternion.Slerp(u, _rUpper.rotation, _rightLead);
            _rLower.rotation = Quaternion.Slerp(l, _rLower.rotation, _rightLead);
            _rHand.rotation = Quaternion.Slerp(hand, _rHand.rotation, _rightLead);
        }
    }

    /// <summary>The left hand onto the handguard while the rifle is in the right hand; off for the punches, the kick and the reload's middle.</summary>
    void LeftHandOnRifle(float dt)
    {
        float want = 1f - Mathf.Max(_toLeft, _toSling, _kickW);
        if (_d.reloading)
        {
            float r = _d.reload;
            Vector2 beats = S.reloadMagazineBeats;
            if (r > beats.x - 0.08f && r < beats.y + 0.12f) want = 0f;
        }
        if (_actionsW > 0.5f && !_d.reloading) want = 0f;
        _leftHand = Mathf.MoveTowards(_leftHand, want, dt * 5f);
        if (_leftHand <= 0.001f || _spec == null || _spec.leftHand == null || _lUpper == null) return;

        Quaternion u = _lUpper.rotation, l = _lLower.rotation, h = _lHand.rotation;
        Vector3 pole = _lUpper.position + _body.TransformDirection(new Vector3(-0.5f, -0.8f, 0f));
        LimbIK.Solve(_lUpper, _lLower, _lHand, _spec.leftHand.position, pole);
        if (_lFrame.valid) _lHand.rotation = _lFrame.World(_spec.leftHand.forward, _spec.leftHand.up);
        if (_leftHand < 0.999f)
        {
            _lUpper.rotation = Quaternion.Slerp(u, _lUpper.rotation, _leftHand);
            _lLower.rotation = Quaternion.Slerp(l, _lLower.rotation, _leftHand);
            _lHand.rotation = Quaternion.Slerp(h, _lHand.rotation, _leftHand);
        }
    }

    /// <summary>
    /// Between the reload's two beats the magazine is in the left hand: it leaves the rifle with
    /// the hand, goes down with it and comes back up to be seated, wherever the clip takes the hand.
    /// </summary>
    void MagazineInHand()
    {
        if (_spec == null || _spec.magazine == null || _lHand == null || !_d.reloading) return;
        float r = _d.reload;
        Vector2 beats = S.reloadMagazineBeats;
        if (r <= beats.x || r >= beats.y) return;
        // Ease out of the well and back into it over the first and last tenth of the middle.
        float span = beats.y - beats.x;
        float w = Smooth(Mathf.Min((r - beats.x) / (span * 0.15f), (beats.y - r) / (span * 0.15f)));
        // In the palm: along the fingers and a little below the back of the hand.
        Vector3 palm = _lHand.position + (_lFrame.valid
            ? _lHand.rotation * Quaternion.Inverse(_lFrame.toLocal) * new Vector3(0f, -0.03f, 0.06f)
            : Vector3.zero);
        _spec.magazine.position = Vector3.Lerp(_spec.magazine.position, palm, w);
    }

    /// <summary>
    /// The weapon's muzzle (where tracers start and the flash is drawn, by the main camera) put
    /// where the drawn rifle's muzzle is seen: the arms camera sits elsewhere and sees wider, so
    /// the real muzzle is found on its screen and the same point on the main camera's screen, at
    /// the same depth, is used.
    /// </summary>
    void PlaceMuzzle()
    {
        if (_weapon == null || _weapon.muzzlePoint == null || _spec == null || _spec.muzzle == null) return;
        Vector3 at = _spec.muzzle.position;
        if (_armsCam != null)
        {
            Vector3 vp = _armsCam.WorldToViewportPoint(at);
            if (vp.z > 0.01f) at = _cam.ViewportToWorldPoint(vp);
        }
        _weapon.muzzlePoint.SetPositionAndRotation(at, _spec.muzzle.rotation);
    }

    /// <summary>On the sling the rifle is on the back, never seen from the eyes (its stock over the shoulder was): its shadow only.</summary>
    void SlungLayer()
    {
        bool slung = _toSling > 0.5f;
        if (_gun == null || slung == _gunInWorld) return;
        _gunInWorld = slung;
        foreach (var r in _gun.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.layer == _firstPerson) r.enabled = !slung;
    }

    // ======================================================================

    bool Has(int layer, string layerName, string state) =>
        layer >= 0 && (_anim.HasState(layer, Animator.StringToHash(state)) || _anim.HasState(layer, Animator.StringToHash(layerName + "." + state)));

    static void Gone(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ======================================================================
    // The preview (FPSKit > Player > Preview Body): built and stepped by hand, in edit mode
    // ======================================================================

    /// <summary>Builds the body, its camera and the gun under this object, looking through <paramref name="cam"/>, for the preview tool.</summary>
    public void PreviewBuild(Camera cam, WeaponData data)
    {
        previewing = true;
        preview = new Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
        _cam = cam;
        _firstPerson = LayerMask.NameToLayer("FirstPerson");
        _playerLayer = Mathf.Max(0, LayerMask.NameToLayer("Player"));
        BuildBody();
        BuildArmsCamera();
        BuildGun(data);
        _anim.Rebind();
    }

    /// <summary>One frame of the preview: the clips' parameters, the Animator, then everything on top.</summary>
    public void PreviewStep(float dt)
    {
        if (_anim == null) return;
        Tick(dt);
        _anim.Update(dt);
        LateTick(dt);
    }

    /// <summary>
    /// A damped spring stepped in pieces of at most 1/120 s, so it holds at any frame rate: in one
    /// long step (a hitch, or the editor running slowly) a stiff spring overshoots further each time.
    /// </summary>
    static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float stiffness, float damping, float dt)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 60);
        float h = Mathf.Min(dt, 0.5f) / steps;
        for (int i = 0; i < steps; i++)
        {
            v += (stiffness * (target - x) - damping * v) * h;
            x += v * h;
        }
    }

    static void Turn(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float Elevation(Vector3 v) => Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
