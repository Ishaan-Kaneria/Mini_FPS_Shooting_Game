using System;
using UnityEngine;

/// <summary>
/// The punch: a close-range strike that costs no ammunition. Drawn as a rifle-butt
/// strike (Ishaan's choice over a fist): the rifle is cocked back, pitched over so the
/// stock leads, and driven up into the target, both hands on it, out and back in under
/// half a second (see AnimateButt). A fist
/// is still supported if one is wired, but the builder no longer makes one.
///
/// It exists for the level where every round counts. Up against an enemy, the player
/// gets a choice -- spend bullets, or put them down with a fist -- and on One Magazine
/// that choice is the difference between finishing with rounds in hand and running dry.
///
/// A punch drops an ordinary enemy outright, takes a large bite out of an elite, and
/// only chips a boss. The damage is a fraction of the target's own pool rather than a
/// number, so it means the same thing on level one and level eight, where the level
/// curve has scaled every enemy's health up.
///
/// Reach is found, not aimed: whoever is closest to the crosshair within arm's length
/// and a wide cone in front gets hit, as long as nothing solid is in between. A punch is
/// thrown at someone standing in your face, and asking for pixel aim at that range would
/// make it the least reliable thing in the game.
/// </summary>
[DisallowMultipleComponent]
public class MeleeStrike : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Found below this object when empty. Lowered while the fist is out, and " +
             "where the hitmarker is raised.")]
    public Weapon weapon;

    [Tooltip("The camera the reach is measured from. The weapon's when empty.")]
    public Camera fpsCamera;

    [Tooltip("Bindings. Taken from the motor when empty, so both stay in sync.")]
    public ControlSettings controls;

    public AudioSource audioSource;
    public AudioClip swingClip;
    public AudioClip hitClip;

    [Header("Hands")]
    [Tooltip("The left fist and forearm, parented to the weapon holder. Hidden at rest; " +
             "thrown forward on a punch.")]
    public Transform fist;

    [Tooltip("The left hand on the gun's handguard. Hidden while the fist is out, so " +
             "the player never has two left hands.")]
    public GameObject supportHand;

    [Tooltip("Where the fist waits, out of frame below and to the left, in the weapon " +
             "holder's space.")]
    public Vector3 fistRest = new Vector3(-0.30f, -0.42f, 0.18f);

    [Tooltip("Where the fist lands: a little left of and below the crosshair.")]
    public Vector3 fistStrike = new Vector3(-0.05f, -0.09f, 0.58f);

    [Tooltip("How far the gun drops out of the fist's way, in the model's local space. " +
             "Only with a fist; without one the rifle itself is the strike.")]
    public Vector3 gunDip = new Vector3(0.07f, -0.16f, -0.06f);

    [Header("Rifle Butt")]
    [Tooltip("The cock before the blow, in degrees (pitch, yaw, then lean across the " +
             "view): muzzle tipped up and leaning out to the right, the way the rifle is " +
             "drawn back to load a strike.")]
    public Vector3 buttCockTurn = new Vector3(-18f, 10f, -22f);

    [Tooltip("Where the rifle is drawn back to for the cock: down, right and in.")]
    public Vector3 buttCockOffset = new Vector3(0.06f, -0.05f, -0.1f);

    [Tooltip("Where the butt plate lands, in the view's space (x right, y up, z ahead, " +
             "metres): just under the crosshair, at arm's length.")]
    public Vector3 buttStrikePoint = new Vector3(0.05f, -0.06f, 0.62f);

    [Tooltip("Which way the stock points at the blow, in the view's space: up and forward, " +
             "a little to the right, so the rifle lies across the view from the muzzle, " +
             "low and near on the left, to the butt at the centre.")]
    public Vector3 buttStrikeAim = new Vector3(0.22f, 0.75f, 0.62f);

    [Tooltip("Which way the sights face at the blow, in the view's space: out to the left, " +
             "so the rifle shows its side and the grip, with the right hand round it, is " +
             "on the right rather than between the rifle and the eye.")]
    public Vector3 buttStrikeSightsFace = new Vector3(-1f, 0.1f, 0f);

    [Tooltip("Metres from the model's pivot, at the receiver, back to the butt plate.")]
    [Range(0.1f, 0.5f)] public float buttLength = 0.25f;

    [Tooltip("Degrees the view dips on the thrust: the body leaning into the blow.")]
    [Range(0f, 5f)] public float buttLunge = 1.6f;

    [Header("Strike")]
    [Tooltip("Metres from the eye to the nearest point of the target. About an arm and " +
             "a half-step: close enough that it is a choice, far enough that it lands.")]
    [Range(1f, 4f)] public float reach = 2.3f;

    [Tooltip("Degrees either side of the crosshair a target can be and still be hit.")]
    [Range(10f, 80f)] public float coneDegrees = 50f;

    [Tooltip("Seconds between punches.")]
    [Range(0.2f, 2f)] public float cooldown = 0.55f;

    [Tooltip("Seconds the whole throw takes, out and back. The gun is down for this long.")]
    [Range(0.15f, 1f)] public float swingSeconds = 0.42f;

    [Header("Damage, as a share of the target's full health and armour")]
    [Tooltip("An ordinary enemy is put down by one punch whatever this says. Kept as a " +
             "number for the ones that are not ordinary.")]
    [Range(0f, 1f)] public float eliteShare = 0.45f;

    [Tooltip("Small on purpose: a boss that could be punched to death would make the " +
             "fight a matter of walking up to it.")]
    [Range(0f, 1f)] public float bossShare = 0.08f;

    [Tooltip("Camera shake on a landed punch.")]
    [Range(0f, 1f)] public float hitShake = 0.35f;

    // ======================================================================

    /// <summary>Who a punch would hit right now, or null. Refreshed ten times a second.</summary>
    public Health Target { get; private set; }

    /// <summary>True while someone is in reach. The HUD's prompt follows it.</summary>
    public bool InReach => Target != null;

    /// <summary>Raised when <see cref="InReach"/> flips.</summary>
    public event Action<MeleeStrike, bool> ReachChanged;

    /// <summary>Raised on every punch thrown, with who it hit (null for a miss).</summary>
    public event Action<MeleeStrike, Health> Punched;

    PlayerMotor _motor;
    BombThrower _bombs;
    Health _self;
    ControlSettings _fallbackControls;

    float _nextPunch;
    float _swingStart = -99f;
    float _nextScan;
    bool _lunged;
    bool _impactPending;

    Collider[] _overlap;
    Collider[] Overlap => _overlap ??= new Collider[32];

    int _enemyMask = -1;
    int _blockMask;

    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;
            if (_motor != null && _motor.controls != null) return _motor.controls;
            return _fallbackControls ??= ControlSettings.CreateDefault();
        }
    }

    void Awake()
    {
        _motor = GetComponentInParent<PlayerMotor>();
        _bombs = GetComponentInParent<BombThrower>();
        _self = GetComponent<Health>();
        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (fpsCamera == null && weapon != null) fpsCamera = weapon.fpsCamera;
        if (fpsCamera == null) fpsCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (fist != null)
        {
            fist.localPosition = fistRest;
            fist.gameObject.SetActive(false);
        }
    }

    void OnDisable()
    {
        if (weapon != null)
        {
            weapon.HandsOffset = Vector3.zero;
            weapon.HandsRotation = Vector3.zero;
        }

        if (fist != null) fist.gameObject.SetActive(false);
        if (supportHand != null) supportHand.SetActive(true);
        _impactPending = false;
        SetTarget(null);
    }

    void Update()
    {
        bool able = PlayerMotor.InputEnabled && (_self == null || !_self.IsDead)
                    && !(_bombs != null && _bombs.IsAiming);

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 0.1f;
            SetTarget(able ? FindTarget() : null);
        }

        // Read every frame whether or not a punch is possible, so a tap queued while it
        // was not cannot fire late, at an enemy the player has since turned away from.
        bool pressed = ControlSettings.Pressed(Bindings.melee)
                       | MobileInput.ConsumeMelee()
                       | GameInput.PadPressed(GameAction.Melee);

        if (pressed && able) TryPunch();

        Animate();
    }

    // ======================================================================

    /// <summary>
    /// Throws a punch at whoever is in reach, or at the air. Returns who it hit.
    /// Public so a check can throw one without a keyboard.
    /// </summary>
    public Health TryPunch()
    {
        if (Time.time < _nextPunch) return null;
        _nextPunch = Time.time + cooldown;
        _swingStart = Time.time;

        if (weapon != null) weapon.Lower(swingSeconds);
        PlayClip(swingClip, 0.8f);

        // Asked again now rather than trusted from the last scan: a tenth of a second is
        // long enough for the enemy in the prompt to have died to a teammate's bomb.
        var target = FindTarget();
        SetTarget(target);

        if (target != null) Strike(target);

        Punched?.Invoke(this, target);
        return target;
    }

    void Strike(Health target)
    {
        Transform cam = fpsCamera.transform;

        float pool = target.Current + target.Shield;
        float full = target.maxHealth + target.maxShield;

        float amount = RoleOf(target) switch
        {
            EnemyArchetype.Role.Boss => Mathf.Max(1f, full * bossShare),
            EnemyArchetype.Role.Elite => Mathf.Max(1f, full * eliteShare),
            // A hair over what it has left, so the shield-then-health split cannot leave
            // it standing on a rounding error.
            _ => pool + 1f,
        };

        var point = ClosestPoint(target, cam.position);
        var info = new DamageInfo(amount, point, -cam.forward, cam.forward, gameObject)
        {
            fromMelee = true,
        };
        info.amount = target.ApplyDamage(info);

        if (info.amount <= 0f) return;

        // The sound and the jolt wait for the frame the stock lands on (see Animate);
        // the damage does not, so the strike is as quick to count as it is to press.
        _impactPending = true;
        if (weapon != null) weapon.ReportHit(info);
    }

    static EnemyArchetype.Role RoleOf(Health target)
    {
        var ai = target.GetComponent<EnemyAI>();
        return ai != null && ai.archetype != null ? ai.archetype.role : EnemyArchetype.Role.Standard;
    }

    // ======================================================================

    /// <summary>
    /// The enemy nearest the crosshair within reach and the cone, with nothing solid
    /// between the eye and it. Null when there is none.
    /// </summary>
    Health FindTarget()
    {
        if (fpsCamera == null) return null;

        if (_enemyMask < 0)
        {
            int enemy = LayerMask.NameToLayer("Enemy");
            int player = LayerMask.NameToLayer("Player");
            _enemyMask = enemy >= 0 ? 1 << enemy : ~0;
            _blockMask = ~((enemy >= 0 ? 1 << enemy : 0) | (player >= 0 ? 1 << player : 0));
        }

        Transform cam = fpsCamera.transform;
        Vector3 eye = cam.position;

        int count = Physics.OverlapSphereNonAlloc(eye, reach, Overlap, _enemyMask,
                                                  QueryTriggerInteraction.Ignore);

        Health best = null;
        float bestAngle = coneDegrees;

        for (int i = 0; i < count; i++)
        {
            var col = Overlap[i];
            var health = col.GetComponentInParent<Health>();
            if (health == null || health.IsDead || health == _self) continue;
            if (health.GetComponent<EnemyAI>() == null) continue;

            Vector3 point = col.ClosestPoint(eye);
            Vector3 to = point - eye;
            float distance = to.magnitude;
            if (distance > reach) continue;

            // Inside the collider (the enemy is overlapping the camera) counts as dead ahead.
            float angle = distance < 0.05f ? 0f : Vector3.Angle(cam.forward, to);
            if (angle > bestAngle) continue;

            if (distance > 0.05f &&
                Physics.Raycast(eye, to / distance, distance - 0.02f, _blockMask, QueryTriggerInteraction.Ignore))
                continue;

            best = health;
            bestAngle = angle;
        }

        return best;
    }

    Vector3 ClosestPoint(Health target, Vector3 from)
    {
        Vector3 best = target.transform.position + Vector3.up;
        float bestDistance = float.MaxValue;

        foreach (var col in target.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled) continue;
            Vector3 p = col.ClosestPoint(from);
            float d = (p - from).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = p; }
        }

        return best;
    }

    void SetTarget(Health target)
    {
        bool was = Target != null;
        Target = target;
        if (was != (target != null)) ReachChanged?.Invoke(this, target != null);
    }

    // ======================================================================

    /// <summary>Share of the swing at which the blow lands, for each way of drawing it.</summary>
    const float ButtImpactAt = 0.38f, FistImpactAt = 0.28f;

    /// <summary>
    /// Out fast, a beat at full stretch, back slower -- the shape of a jab, and the
    /// reason it reads as a punch rather than as the arm sliding across the screen.
    /// Driven from a timestamp rather than a coroutine, so a domain reload mid-swing
    /// cannot leave the gun dipped.
    /// </summary>
    void Animate()
    {
        float k = swingSeconds <= 0f ? 1f : (Time.time - _swingStart) / swingSeconds;
        bool swinging = k >= 0f && k < 1f;

        if (_impactPending && (!swinging || k >= (fist == null ? ButtImpactAt : FistImpactAt)))
        {
            _impactPending = false;
            PlayClip(hitClip, 1f);
            if (_motor != null) _motor.AddShake(hitShake);
        }

        if (fist == null)
        {
            AnimateButt(k, swinging);
            return;
        }

        // The gun: down over the first fifth, held, back up over the last half.
        float dip = !swinging ? 0f
                  : k < 0.2f ? UITheme.EaseOut(k / 0.2f)
                  : k < 0.5f ? 1f
                  : 1f - UITheme.EaseOut((k - 0.5f) / 0.5f);

        if (weapon != null) weapon.HandsOffset = gunDip * dip;
        if (supportHand != null) supportHand.SetActive(dip < 0.3f);

        if (fist == null) return;

        fist.gameObject.SetActive(swinging);
        if (!swinging) return;

        float reachOut = k < 0.28f ? UITheme.EaseOut(k / 0.28f)
                       : k < 0.45f ? 1f
                       : 1f - UITheme.EaseOut((k - 0.45f) / 0.55f);

        fist.localPosition = Vector3.LerpUnclamped(fistRest, fistStrike, reachOut);
        // Knuckles turn over as the arm extends, which is what makes it a punch and not a push.
        fist.localRotation = Quaternion.Euler(Mathf.Lerp(20f, -2f, reachOut),
                                              Mathf.Lerp(22f, 4f, reachOut),
                                              Mathf.Lerp(-30f, -8f, reachOut));
    }

    /// <summary>
    /// The rifle-butt strike, drawn the way a soldier throws one. The rifle is cocked
    /// back and rolled out first, a beat of load-up; then pitched over hard so the
    /// muzzle drops away and the stock drives up and forward to the crosshair, the view
    /// dipping into it; it stops dead on the blow and rebounds a little, the way wood
    /// does off a body; and comes back to the shoulder in its own time. Both hands stay
    /// on it throughout. It was once a flat quarter-turn with the gun sliding across the
    /// screen, which read as holding the rifle out sideways rather than hitting with it.
    ///
    /// Key times, as a share of the swing: cocked by 0.2, blow at 0.38, rebound by 0.5,
    /// home at 1.
    /// </summary>
    void AnimateButt(float k, bool swinging)
    {
        if (weapon == null) return;

        if (!swinging)
        {
            weapon.HandsOffset = Vector3.zero;
            weapon.HandsRotation = Vector3.zero;
            _lunged = false;
            return;
        }

        // Blended as rotations, not as three angles: the lean is across the view, after
        // the pitch, which Euler angles cannot say and a per-axis blend would wobble.
        Quaternion cock = Turn(buttCockTurn);
        BlowPose(out Quaternion blow, out Vector3 blowOffset);
        Quaternion turn;
        Vector3 offset;

        if (k < 0.2f)
        {
            float t = UITheme.EaseOut(k / 0.2f);
            turn = Quaternion.Slerp(Quaternion.identity, cock, t);
            offset = buttCockOffset * t;
        }
        else if (k < ButtImpactAt)
        {
            // Accelerating into the blow: slow off the cock, fastest at the moment it lands.
            float t = (k - 0.2f) / (ButtImpactAt - 0.2f);
            t *= t;
            turn = Quaternion.Slerp(cock, blow, t);
            offset = Vector3.Lerp(buttCockOffset, blowOffset, t);
        }
        else if (k < 0.5f)
        {
            // Stopped dead, then a small rebound off whatever it hit.
            float t = Mathf.Sin((k - ButtImpactAt) / (0.5f - ButtImpactAt) * Mathf.PI);
            turn = Quaternion.Slerp(blow, cock, 0.07f * t);
            offset = blowOffset + new Vector3(0f, -0.01f, -0.035f) * t;
        }
        else
        {
            float t = (k - 0.5f) / 0.5f;
            t = t * t * (3f - 2f * t);
            turn = Quaternion.Slerp(blow, Quaternion.identity, t);
            offset = Vector3.Lerp(blowOffset, Vector3.zero, t);
        }

        weapon.HandsRotation = turn.eulerAngles;
        weapon.HandsOffset = offset;

        if (!_lunged && k >= 0.3f)
        {
            _lunged = true;
            if (_motor != null) _motor.AddRecoil(-buttLunge, 0f, 7f);
        }
    }

    /// <summary>
    /// The rifle at the blow, as the turn and offset on top of its resting pose, worked
    /// out from where the butt should land and which way the stock should point. Said
    /// that way because that is what can be seen and judged; the angles that produce it
    /// are three numbers nobody could tune by eye.
    /// </summary>
    void BlowPose(out Quaternion turn, out Vector3 offset)
    {
        Vector3 stock = buttStrikeAim.sqrMagnitude > 1e-6f ? buttStrikeAim.normalized : Vector3.forward;
        Vector3 sights = Vector3.ProjectOnPlane(buttStrikeSightsFace, stock);
        if (sights.sqrMagnitude < 1e-6f) sights = Vector3.ProjectOnPlane(Vector3.up, stock);

        // The model looks down its barrel, the opposite way to the stock.
        var look = Quaternion.LookRotation(-stock, sights);
        turn = Quaternion.Inverse(weapon.HipRotation) * look;
        offset = buttStrikePoint - stock * buttLength - weapon.HipPosition;
    }

    /// <summary>Pitch, then yaw, then a lean about the line of sight.</summary>
    static Quaternion Turn(Vector3 degrees)
        => Quaternion.AngleAxis(degrees.z, Vector3.forward)
           * Quaternion.AngleAxis(degrees.y, Vector3.up)
           * Quaternion.AngleAxis(degrees.x, Vector3.right);

    void PlayClip(AudioClip clip, float volume)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume);
    }
}
