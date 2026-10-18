using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's full body, for what the first-person view model cannot be: the legs seen
/// looking down, the kick, and the shadow. A soldier (FPSKit > Player > Build Body) moved by the
/// same Mixamo rifle clips the enemies walk in, standing under the camera.
///
/// - **Drawn two ways.** Each garment's mesh is split by bone weight at start: the hips and legs
///   are drawn to the player's eye (two-sided, so the cut at the waist shows cloth looking down);
///   the chest, arms and head are drawn into the shadows only. Mixamo's clips are third-person
///   animation, and their arms and hands do not line up from a camera inside the head, so what
///   the player sees holding the gun is the first-person view model (<see cref="ViewModelRig"/>).
///   A shadow-only copy of the gun rides in the body's right hand, so the shadow carries a rifle.
/// - **The camera never follows the head; the body follows the camera.** Each frame it is slid
///   (smoothly) so its eyes are a little ahead of the camera (the eye is set back,
///   <see cref="PlayerBodySettings.eyeBack"/>), and back again looking down so the eye clears
///   the vest and the forward boot is seen.
/// - **The kick** plays its clip on the legs (and the body's turn), timed by MeleeStrike so the
///   foot lands on the frame the damage does; the body is turned for it so the foot goes ahead,
///   and leans back, which lifts the leg into view.
///
/// Added by PlayerMotor when the settings name a body. Values: <see cref="PlayerBodySettings"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]   // after the motor has placed the camera
public class PlayerBody : MonoBehaviour
{
    /// <summary>The settings name a body: the player has legs and a shadow.</summary>
    public static bool Wanted => PlayerBodySettings.Active.bodyPrefab != null;

    static PlayerBodySettings S => PlayerBodySettings.Active;

    PlayerMotor _motor;
    Weapon _weapon;
    MeleeStrike _melee;
    CharacterController _cc;
    Camera _cam;
    int _playerLayer;

    Transform _body;
    PlayerBodyRig _rig;
    Animator _anim;
    Transform _head, _lThigh, _rThigh;

    // The shadow's rifle.
    Transform _shadowGun;
    Vector3 _shadowGrip;
    WeaponData _builtFor;

    Vector3 _offset;
    float _lift, _turn, _kickW;
    bool _placed;
    int _kickLayer = -1;
    string _kickState = "";
    string _baseState = "Idle";

    static readonly int MoveX = Animator.StringToHash("MoveX");
    static readonly int MoveZ = Animator.StringToHash("MoveZ");
    static readonly int CrouchId = Animator.StringToHash("Crouch");
    static readonly int AimId = Animator.StringToHash("Aim");
    static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
    static readonly int MoveRate = Animator.StringToHash("MoveRate");
    static readonly int KickTime = Animator.StringToHash("KickTime");

    /// <summary>The body is built.</summary>
    public bool Built => _anim != null;

    static readonly int MoveState = Animator.StringToHash("Move");
    float _lastNorm;
    bool _wasMoving;

    /// <summary>
    /// Gait cycles the legs' movement clips have played (two steps each), counted after the Animator: the
    /// footsteps, the head's bob and the arms' swing are taken from it (<see cref="PlayerMotor.StepPhase"/>),
    /// so what is heard and seen is the feet that are seen.
    /// </summary>
    public float StrideCycles { get; private set; }

    /// <summary>The legs are in their movement clips (not standing, not in the air).</summary>
    public bool Striding { get; private set; }

    /// <summary>Counts the movement clips' cycles: the state the legs are in, or the one they are blending into.</summary>
    void CountStride()
    {
        var now = _anim.GetCurrentAnimatorStateInfo(0);
        if (_anim.IsInTransition(0))
        {
            var next = _anim.GetNextAnimatorStateInfo(0);
            if (next.shortNameHash == MoveState) now = next;
        }
        bool moving = now.shortNameHash == MoveState;
        if (moving && _wasMoving) StrideCycles += Mathf.Repeat(now.normalizedTime - _lastNorm, 1f);
        _lastNorm = now.normalizedTime;
        _wasMoving = moving;
        Striding = moving;
    }

    /// <summary>
    /// What the body acts out this frame. Read from the player's own parts; the kick's
    /// calibration (FPSKit > Player > Build Body) sets it instead.
    /// </summary>
    public struct Drive
    {
        public Vector3 velocity;
        public bool crouching, lowered;
        public MeleeStrike.Strike strike;
        public float strikeProgress;
    }

    [System.NonSerialized] public Drive preview;
    [System.NonSerialized] public bool previewing;
    Drive _d;

    Drive Read()
    {
        if (previewing) return preview;
        var d = new Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
        d.velocity = _cc != null ? _cc.velocity : (_motor != null ? _motor.Velocity : Vector3.zero);
        d.crouching = _motor != null && _motor.IsCrouching;
        d.lowered = _weapon != null && _weapon.IsLowered;
        if (_melee != null)
        {
            d.strike = _melee.Current;
            d.strikeProgress = _melee.StrikeProgress;
        }
        return d;
    }

    // ======================================================================

    void Start()
    {
        _motor = GetComponent<PlayerMotor>();
        _melee = GetComponent<MeleeStrike>();
        _cc = GetComponent<CharacterController>();
        _weapon = GetComponentInChildren<Weapon>();
        _cam = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
        _playerLayer = Mathf.Max(0, LayerMask.NameToLayer("Player"));
        if (S.bodyPrefab == null || _cam == null) { enabled = false; return; }

        BuildBody();
        if (_weapon != null)
        {
            _weapon.Equipped += OnEquipped;
            BuildShadowGun(_weapon.data);
        }
    }

    void OnDestroy()
    {
        if (_weapon != null) _weapon.Equipped -= OnEquipped;
    }

    void OnEquipped(Weapon w) => BuildShadowGun(w.data);

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

        _head = _anim.GetBoneTransform(HumanBodyBones.Head);
        _lThigh = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _rThigh = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _kickLayer = _anim.GetLayerIndex("Kick");

        SplitMeshes();
    }

    /// <summary>
    /// Each garment two ways, by which bone a vertex mostly follows: the hips and legs drawn, the
    /// rest (chest, arms, head) into the shadows only. A triangle on the seam is drawn if any of it
    /// is leg. The shadow copy hangs off this object, not the skeleton (a child added to the
    /// skeleton in play would make the Animator rebind).
    /// </summary>
    void SplitMeshes()
    {
        var legs = new HashSet<Transform>();
        foreach (var t in new[] { _lThigh, _rThigh }) if (t != null) foreach (var c in t.GetComponentsInChildren<Transform>(true)) legs.Add(c);
        var hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) legs.Add(hips);

        foreach (var smr in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
            var mesh = smr.sharedMesh;
            smr.gameObject.layer = _playerLayer;
            smr.updateWhenOffscreen = true;
            if (!mesh.isReadable) { smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; continue; }

            var bones = smr.bones;
            var weights = mesh.boneWeights;
            if (weights.Length != mesh.vertexCount) continue;
            var leg = new bool[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                int bi = weights[v].boneIndex0;
                leg[v] = bi >= 0 && bi < bones.Length && bones[bi] != null && legs.Contains(bones[bi]);
            }

            Mesh Subset(string suffix, bool keepLegs)
            {
                var m = Instantiate(mesh);
                m.name = mesh.name + suffix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var tris = mesh.GetTriangles(sub);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                    {
                        bool any = leg[tris[i]] || leg[tris[i + 1]] || leg[tris[i + 2]];
                        if (any == keepLegs) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); }
                    }
                    m.SetTriangles(kept, sub);
                }
                return m;
            }

            var shadow = new GameObject(smr.name + "_Shadow");
            shadow.transform.SetParent(transform, false);
            shadow.layer = _playerLayer;
            var r = shadow.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = Subset("_Upper", keepLegs: false);
            r.bones = bones;
            r.rootBone = smr.rootBone;
            r.sharedMaterials = smr.sharedMaterials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            r.updateWhenOffscreen = true;

            // Two-sided: looking down, the waist where the body is cut shows cloth, not a hollow.
            smr.sharedMesh = Subset("_Legs", keepLegs: true);
            smr.sharedMaterials = TwoSided(smr.sharedMaterials);
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    /// <summary>Copies of the materials drawn on both faces (URP Lit's Render Face: Both), made once per material.</summary>
    Material[] TwoSided(Material[] mats)
    {
        var result = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null || !mats[i].HasProperty("_Cull")) { result[i] = mats[i]; continue; }
            if (!TwoSidedCache.TryGetValue(mats[i], out var copy))
            {
                copy = new Material(mats[i]) { name = mats[i].name + " (two-sided)" };
                copy.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                TwoSidedCache[mats[i]] = copy;
            }
            result[i] = copy;
        }
        return result;
    }

    Dictionary<Material, Material> _twoSided;
    Dictionary<Material, Material> TwoSidedCache => _twoSided ??= new Dictionary<Material, Material>();

    /// <summary>The gun's meshes, drawn into the shadows only, carried in the body's right hand.</summary>
    void BuildShadowGun(WeaponData data)
    {
        if (data == _builtFor && _shadowGun != null) return;
        _builtFor = data;
        if (_shadowGun != null) Gone(_shadowGun.gameObject);
        _shadowGun = null;
        if (data == null || data.viewModel == null) return;

        var gun = Instantiate(data.viewModel, transform, false);
        gun.name = data.viewModel.name + "_Shadow";
        var spec = gun.GetComponent<ViewModelSpec>();
        _shadowGrip = spec != null && spec.rightHand != null ? gun.transform.InverseTransformPoint(spec.rightHand.position) : Vector3.zero;
        foreach (var c in gun.GetComponentsInChildren<Collider>(true)) Gone(c);
        foreach (var b in gun.GetComponentsInChildren<MonoBehaviour>(true)) Gone(b);
        foreach (var t in gun.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _playerLayer;
        foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        _shadowGun = gun.transform;
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

        // The body's velocity in the player's space, for the eight-way blend, in metres a second
        // at the clips' own size.
        Vector3 v = _d.velocity;
        v.y = 0f;
        Vector3 local = transform.InverseTransformDirection(v) / Mathf.Max(0.1f, _body.lossyScale.x) * S.LegPaceAt(v.magnitude);
        _anim.SetFloat(MoveX, local.x, 0.12f, dt);
        _anim.SetFloat(MoveZ, local.z, 0.12f, dt);
        float speed = local.magnitude;
        _anim.SetFloat(MoveRate, Mathf.Max(1f, speed * _body.lossyScale.x / S.sprintClipSpeed), 0.2f, dt);
        _anim.SetFloat(CrouchId, _d.crouching ? 1f : 0f, 0.2f, dt);
        _anim.SetFloat(AimId, _d.lowered ? 0f : 1f, 0.2f, dt);
        _anim.SetFloat(IdleVariant, _d.lowered ? 1f : 0f, 0.2f, dt);

        string want = speed > 0.2f ? "Move" : "Idle";
        if (want != _baseState && (want == "Move" || speed < 0.1f))
        {
            _anim.CrossFadeInFixedTime(want, 0.25f, 0);
            _baseState = want;
        }

        // The kick, its clip driven by the strike's own progress.
        bool kicking = _d.strike == MeleeStrike.Strike.Kick && _d.strikeProgress >= 0f && !string.IsNullOrEmpty(S.kick.state);
        if (_kickLayer >= 0)
        {
            if (kicking && _kickState != S.kick.state)
            {
                _anim.CrossFadeInFixedTime(S.kick.state, 0.08f, _kickLayer);
                _kickState = S.kick.state;
            }
            if (kicking) _anim.SetFloat(KickTime, ClipTime(_d.strikeProgress, MeleeStrike.ImpactAt(MeleeStrike.Strike.Kick), S.kick));
            _kickW = Mathf.MoveTowards(_kickW, kicking ? 1f : 0f, dt * (kicking ? 10f : 5f));
            _anim.SetLayerWeight(_kickLayer, Smooth(_kickW));
            if (!kicking && _kickW <= 0f) _kickState = "";
        }
        // Turned so the boot lands just right of the crosshair (kickAside), beside the enemy's head
        // and chest rather than over them.
        _turn = Mathf.Lerp(_turn, kicking ? S.kick.turn - S.kickAside : 0f, 1f - Mathf.Exp(-30f * dt));
    }

    /// <summary>
    /// The clip's time for a share of the strike: the clip's own moment of contact falls on the
    /// strike's (so the foot lands where the damage does), the rest of its range shared out either side.
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
    // Under the camera
    // ======================================================================

    void LateUpdate()
    {
        if (_anim != null && !previewing) CountStride();
        if (_anim == null || _cam == null || previewing) return;
        LateTick(Mathf.Max(1e-4f, Time.deltaTime));
    }

    void LateTick(float dt)
    {
        PlaceBody(dt);
        PlaceShadowGun();
    }

    /// <summary>
    /// The body slid under the camera so its eyes are a little ahead of it (the eye is set back);
    /// back looking down; turned and leaned for a kick.
    /// </summary>
    void PlaceBody(float dt)
    {
        if (_head == null) return;
        _body.rotation = transform.rotation * Quaternion.AngleAxis(-S.kickLean * Smooth(_kickW), Vector3.right)
                                            * Quaternion.AngleAxis(-_turn, Vector3.up);

        Vector3 flat = Flat(_cam.transform.forward);
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Flat(transform.forward).normalized;
        Vector3 eye = _head.position + _body.forward * S.eyeAhead + Vector3.up * S.eyeUp;
        Vector3 wantEye = _cam.transform.position + flat * S.eyeBack;
        Vector3 toEye = wantEye - eye;
        toEye.y = 0f;
        float down = Mathf.Clamp01(Vector3.Dot(_cam.transform.forward, Vector3.down));
        Vector3 back = -flat * S.lookDownBack * down;
        Vector3 want = (_body.position - transform.position) + toEye + back;
        want.y = 0f;
        _offset = _placed ? Vector3.Lerp(_offset, want, 1f - Mathf.Exp(-S.followRate * dt)) : want;
        float lift = Mathf.Clamp(_lift + _cam.transform.position.y - eye.y, S.liftLimits.x, S.liftLimits.y);
        _lift = _placed ? Mathf.Lerp(_lift, lift, 1f - Mathf.Exp(-S.liftRate * dt)) : lift;
        _offset.y = _lift;
        _placed = true;
        _body.position = transform.position + _offset;
    }

    void PlaceShadowGun()
    {
        if (_shadowGun == null || _rig == null || _rig.rifleGrip == null) return;
        Transform grip = _rig.rifleGrip;
        _shadowGun.rotation = grip.rotation;
        _shadowGun.position = grip.position - grip.rotation * Vector3.Scale(_shadowGrip, _shadowGun.lossyScale);
    }

    // ======================================================================
    // The kick's calibration (FPSKit > Player > Build Body): built and stepped by hand, in edit mode
    // ======================================================================

    /// <summary>Builds the body under this object, under <paramref name="cam"/>, for the builder's calibration.</summary>
    public void PreviewBuild(Camera cam)
    {
        previewing = true;
        preview = new Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
        _cam = cam;
        _playerLayer = Mathf.Max(0, LayerMask.NameToLayer("Player"));
        BuildBody();
        _anim.Rebind();
    }

    /// <summary>One frame: the clips' parameters, the Animator, then the placing.</summary>
    public void PreviewStep(float dt)
    {
        if (_anim == null) return;
        Tick(dt);
        _anim.Update(dt);
        LateTick(dt);
    }

    // ======================================================================

    static void Gone(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
