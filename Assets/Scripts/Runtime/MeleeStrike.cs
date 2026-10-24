using System;
using UnityEngine;

/// <summary>
/// The player's close-quarters fighting. None of it costs ammunition.
///
/// It exists for the level where every round counts. Up against an enemy, the player
/// gets a choice -- spend bullets, or put them down by hand -- and on One Magazine that
/// choice is the difference between finishing with rounds in hand and running dry.
///
/// One key carries a whole fight, the way a soldier's hands would (Ishaan's design):
///
/// - **First press: the rifle butt.** The gun is in the hands, so it is the gun that hits.
/// - **Press again while the fight is on** (inside the chain window): the gun swings
///   onto the back and the fists come up. From then on each press is a punch, jab and
///   cross in turn, thrown hard. The fists stay up until the player stops for a moment,
///   or fires, aims or reloads -- then the gun comes back off the back.
/// - **Kick** (its own key): with the gun in the hands it is slid aside out of the leg's
///   way first; then a front kick that throws an ordinary enemy off its feet.
/// - **Takedown**: any strike at a target that is badly hurt, reeling, or on the floor
///   becomes the finishing blow -- the butt with the gun in hand, a heavy cross with the
///   fists up.
///
/// Damage is a share of the target's own pool by role (see <see cref="MeleeSettings"/>),
/// dealt on the frame the fist or the boot actually arrives -- with the sound, the shake
/// and the hit-stop -- so what is seen and what is felt are one moment, and booked to the
/// body part the blow lands on. A target that has got away by then is missed.
///
/// Reach is found, not aimed: whoever is closest to the crosshair within reach and a cone
/// in front gets hit, as long as nothing solid is in between. **A strike never moves the
/// player**: the arm or the leg reaches, the body stays where the player's own movement
/// puts it (it used to step up to 2.2 m in, which read as a surge; see DESIGN_NOTES, "A strike
/// never moves the player"). On a pad or a touch screen the view is pulled onto the target;
/// never with a mouse, which is the aim-assist rule.
///
/// Stamina stops the keys being held down into a blender: every strike draws on it, and
/// a winded player cannot throw until it comes back. The takedown is free.
///
/// Drawn by <see cref="ViewModelRig"/> when the gun has rigged arms; without them every
/// strike is the rifle butt (<see cref="AnimateButt"/>).
///
/// The grab and punch (the fight in the film <i>Master</i>; MeleeGrapple.cs): face to face, the press takes the enemy
/// by the head, and each press after it is a punch to the face, then the overhand that ends it; a hook to the belly
/// folds him over, and the next press takes him by the back of the head.
/// </summary>
[DisallowMultipleComponent]
public partial class MeleeStrike : MonoBehaviour
{
    // Appended to, never reordered.
    public enum Strike { None, Jab, Cross, Kick, Takedown, Butt, BodyBlow, Grab, FacePunch, Overhand }

    [Header("Wiring")]
    [Tooltip("Found below this object when empty. Lowered while the hands are busy, and " +
             "where the hitmarker is raised.")]
    public Weapon weapon;
    [Tooltip("The camera the reach is measured from. The weapon's when empty.")]
    public Camera fpsCamera;
    [Tooltip("Bindings. Taken from the motor when empty, so both stay in sync.")]
    public ControlSettings controls;
    [Tooltip("The numbers. MeleeSettings.Active (Resources/MeleeSettings) when empty.")]
    public MeleeSettings settings;
    public AudioSource audioSource;
    public AudioClip swingClip;
    public AudioClip hitClip;

    [Header("Hands")]
    [Tooltip("Unused since the arms are rigged; kept so older scenes still load it.")]
    public Transform fist;
    [Tooltip("The left hand on the gun's handguard in a scene-built gun.")]
    public GameObject supportHand;

    [Header("The gun's moves, on top of its pose (model space, metres and degrees)")]
    [Tooltip("Where the gun goes on its way onto the back: up, right and back over the shoulder.")]
    public Vector3 holsterOffset = new Vector3(0.22f, 0.28f, -0.55f);
    [Tooltip("How it turns as it goes: muzzle up and over, rolled onto its side.")]
    public Vector3 holsterTurn = new Vector3(-70f, 35f, 55f);
    [Tooltip("Where the gun is slid to for the kick: across to the left and down, out of the leg's way.")]
    public Vector3 kickAsideOffset = new Vector3(-0.11f, -0.05f, -0.03f);
    [Tooltip("How it turns aside for the kick: muzzle down and across.")]
    public Vector3 kickAsideTurn = new Vector3(22f, -30f, 24f);

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

    // ======================================================================

    /// <summary>Who a strike would hit right now, or null. Refreshed ten times a second.</summary>
    public Health Target { get; private set; }

    /// <summary>True while someone is in reach. The HUD's prompt follows it.</summary>
    public bool InReach => Target != null;

    /// <summary>The target in reach can be finished: any strike now is the takedown.</summary>
    public bool TakedownReady => Target != null && CanTakedown(Target);

    /// <summary>Too tired to throw a punch.</summary>
    public bool Winded => Stamina < Settings.jabCost;

    /// <summary>Stamina left, 0 to <see cref="MeleeSettings.maxStamina"/>.</summary>
    public float Stamina { get; private set; } = -1f;

    public float Stamina01 => Mathf.Clamp01(Stamina / Mathf.Max(1f, Settings.maxStamina));

    /// <summary>The strike being thrown now, or None.</summary>
    public Strike Current => StrikeProgress >= 0f ? _strike : Strike.None;

    /// <summary>Share of the way through the current strike, 0 to 1, or -1 between strikes.</summary>
    public float StrikeProgress
    {
        get
        {
            if (_strike == Strike.None || _strikeLength <= 0f) return -1f;
            if (Time.time < _holdUntil) return _heldAt;   // held on the blow (StartHitStop)
            float k = (Time.time - _strikeStart) / _strikeLength;
            return k >= 0f && k < 1f ? k : -1f;
        }
    }

    /// <summary>The takedown being thrown is a fist (the gun is on the back), not the rifle butt.</summary>
    public bool TakedownWithFists { get; private set; }

    /// <summary>How far the gun is onto the back, 0 (in the hands) to 1 (gone).</summary>
    public float GunAway { get; private set; }

    /// <summary>How far up the fists are, 0 to 1: they come up as the gun goes.</summary>
    public float FistsUp => Mathf.Clamp01((GunAway - 0.35f) / 0.65f);

    /// <summary>The fists are the weapon now; the gun is on the back or going there.</summary>
    public bool FistsOut => _fists;

    /// <summary>When the next strike may start. For checks that throw strikes on a timer.</summary>
    public float ReadyAt => _nextStrike;

    /// <summary>The strike thrown last, including one still waiting on its start (the first punch waits for the gun to go).</summary>
    public Strike Thrown => _strike;

    /// <summary>Raised when <see cref="InReach"/> flips.</summary>
    public event Action<MeleeStrike, bool> ReachChanged;

    /// <summary>
    /// Raised when a strike starts, with who it is aimed at (null when nobody is in reach). Not a
    /// blow: whether it lands is decided when the fist or the boot gets there (<see cref="Landed"/>,
    /// <see cref="Missed"/>). A swing is quiet; only a blow that lands is heard as violence.
    /// </summary>
    public event Action<MeleeStrike, Strike, Health> AttackStarted;

    /// <summary>Raised on the frame a blow lands, with who it hit.</summary>
    public event Action<MeleeStrike, Strike, Health> Landed;

    /// <summary>
    /// Raised at the moment a blow would have landed when it met nobody: nobody in reach, out of
    /// reach by then, or behind something solid by then.
    /// </summary>
    public event Action<MeleeStrike, Strike> Missed;

    /// <summary>Share of each strike at which the blow lands.</summary>
    public static float ImpactAt(Strike s) => s switch
    {
        Strike.Jab => 0.3f,
        Strike.Cross => 0.32f,
        Strike.Kick => 0.36f,
        Strike.Takedown => 0.34f,
        Strike.Butt => 0.32f,
        Strike.BodyBlow => 0.38f,
        Strike.Grab => 0.7f,          // the hand closes on the head
        Strike.FacePunch => 0.45f,    // a wind-up the player sees, then the blow
        Strike.Overhand => 0.56f,     // wound up high first, then brought down
        _ => 0.4f,
    };

    PlayerMotor _motor;
    BombThrower _bombs;
    Health _self;
    ControlSettings _fallbackControls;

    Strike _strike;
    float _strikeStart = -99f;
    float _strikeLength;
    float _nextStrike;
    Strike _queued;
    float _queuedAt;
    float _chainUntil = -99f;
    bool _fists;
    float _fistsUntil;
    bool _crossNext;
    float _lastStrikeTime = -99f;
    float _nextScan;

    float _holdUntil = -1f, _heldAt;
    Health _strikeTarget;   // who the strike in flight was thrown at (for the hands' aim; MeleeGrapple)

    Collider[] _overlap;
    Collider[] Overlap => _overlap ??= new Collider[64];
    int _enemyMask = -1;
    int _blockMask;

    MeleeSettings Settings => settings != null ? settings : MeleeSettings.Active;

    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;
            if (_motor != null && _motor.controls != null) return _motor.controls;
            return _fallbackControls ??= ControlSettings.CreateDefault();
        }
    }

    /// <summary>The gun has rigged arms that can let go of it; without them every strike is the rifle butt.</summary>
    bool HasArms => weapon != null && weapon.HasArms;

    /// <summary>The gun went to the ground out of ammo (Weapon.Disarmed): the fists stay up until one is picked up.</summary>
    bool Disarmed => weapon != null && weapon.Disarmed;

    void Awake()
    {
        _motor = GetComponentInParent<PlayerMotor>();
        _bombs = GetComponentInParent<BombThrower>();
        _self = GetComponent<Health>();

        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (fpsCamera == null && weapon != null) fpsCamera = weapon.fpsCamera;
        if (fpsCamera == null) fpsCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (fist != null) fist.gameObject.SetActive(false);

        Stamina = Settings.maxStamina;
    }

    void EnsureMasks()
    {
        if (_enemyMask >= 0) return;
        int enemy = LayerMask.NameToLayer("Enemy");
        int player = LayerMask.NameToLayer("Player");
        _enemyMask = enemy >= 0 ? 1 << enemy : ~0;
        _blockMask = ~((enemy >= 0 ? 1 << enemy : 0) | (player >= 0 ? 1 << player : 0));
    }

    void OnDisable()
    {
        if (weapon != null)
        {
            weapon.HandsOffset = Vector3.zero;
            weapon.HandsRotation = Vector3.zero;
        }

        if (supportHand != null) supportHand.SetActive(true);
        LetGo(shove: false);
        _pendingTarget = null;
        _pending = false;
        _queued = Strike.None;
        _strike = Strike.None;
        _fists = false;
        GunAway = 0f;
        SetTarget(null);
    }

    void Update()
    {

        bool able = PlayerMotor.InputEnabled && (_self == null || !_self.IsDead)
                    && !(_bombs != null && _bombs.IsAiming);

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 0.1f;
            SetTarget(able ? FindTarget(Mathf.Max(Settings.punchRange, Settings.kickRange)) : null);
        }

        MobileInput.MeleeReady = able && Target != null;

        // Read every frame whether or not a strike is possible, so a tap queued while it
        // was not cannot fire late, at an enemy the player has since turned away from.
        var keys = Bindings;
        bool punch = ControlSettings.Pressed(keys.melee) | ControlSettings.Pressed(keys.altMelee)
                     | MobileInput.ConsumeMelee() | GameInput.PadPressed(GameAction.Melee);
        bool kick = ControlSettings.Pressed(keys.kick) | ControlSettings.Pressed(keys.altKick)
                    | MobileInput.ConsumeKick() | GameInput.PadPressed(GameAction.Kick);

        // Nothing waits through a pause, a menu or a death: a press queued before them must not
        // be thrown the moment play comes back (game time stands still in a pause, so the queue's
        // 0.4 s would never run out).
        if (!able) _queued = Strike.None;

        // A press made while the last strike is still recovering is kept for a moment and
        // thrown the instant it can be, not dropped: mashing must never feel ignored.
        if (able && (punch || kick) && Time.time < _nextStrike)
        {
            _queued = kick && !punch ? Strike.Kick : Strike.Jab;
            _queuedAt = Time.time;
        }
        else if (able && punch) TryPunch();
        else if (able && kick) TryKick();
        else if (able && _queued != Strike.None && Time.time >= _nextStrike)
        {
            var queued = _queued;
            _queued = Strike.None;
            if (Time.time - _queuedAt < 0.4f)
            {
                if (queued == Strike.Kick) TryKick();
                else TryPunch();
            }
        }

        UpdateHold(keys);
        UpdateFists(keys);
        RegenStamina();
        Animate();
    }

    /// <summary>
    /// The gun on the back: going there while the fists are wanted, coming back after a
    /// pause or the moment the player reaches for it (fire, aim, reload). Nothing fires
    /// while it is anywhere but the hands.
    /// </summary>
    void UpdateFists(ControlSettings keys)
    {
        var s = Settings;

        // Disarmed, the fists are the weapon: up, and staying up, whatever the trigger does.
        if (Disarmed || Holding) _fists = true;
        else if (_fists && Current == Strike.None)
        {
            bool wantsGun = ControlSettings.Pressed(keys.fire) || ControlSettings.Pressed(keys.aim)
                            || ControlSettings.Pressed(keys.reload) || MobileInput.Fire || MobileInput.Aim
                            || GameInput.PadPressed(GameAction.Fire) || GameInput.PadPressed(GameAction.Aim)
                            || GameInput.PadPressed(GameAction.Reload);

            if (wantsGun || Time.time > _fistsUntil) _fists = false;
        }

        float speed = 1f / Mathf.Max(0.05f, _fists ? s.holsterSeconds : s.drawSeconds);
        GunAway = Mathf.MoveTowards(GunAway, _fists ? 1f : 0f, Time.deltaTime * speed);

        if ((_fists || GunAway > 0f) && weapon != null)
        {
            weapon.CancelReload();
            weapon.Lower(0.15f);
        }
    }

    // ======================================================================
    // Throwing
    // ======================================================================

    /// <summary>
    /// The punch key: the rifle butt with the gun in the hands; with the fight already on,
    /// the gun goes on the back and the fists take over, jab and cross in turn; the takedown
    /// whenever the target is ready for one. Returns who it hit. Public so a check can throw
    /// one without a keyboard.
    /// </summary>
    public Health TryPunch()
    {
        if (Time.time < _nextStrike) return null;

        // Holding someone by the head, face to face with one, over one doubled up, or a hook at the belly.
        if (TryGrapplePress(out Health grappled)) return grappled;

        // No rifle to swing with the gun on the ground: always the fists.
        if (!HasArms && !Disarmed) return Throw(Strike.Butt, 0f);

        if (!_fists && Time.time > _chainUntil && !Disarmed) return Throw(Strike.Butt, 0f);

        // Into the fists, or already in them. The first punch waits for the hand that
        // swings the gun away; the ones after it go at once.
        bool entering = !_fists;
        float delay = Settings.holsterSeconds * (1f - GunAway) * 0.6f;
        _fists = true;

        var strike = entering || !_crossNext ? Strike.Jab : Strike.Cross;
        _crossNext = strike == Strike.Jab;
        return Throw(strike, delay);
    }

    /// <summary>The kick key, or the takedown if the target is ready for one. Returns who it hit.</summary>
    public Health TryKick() => Time.time < _nextStrike ? null : Throw(Strike.Kick, 0f);

    Health Throw(Strike strike, float delay, Health forced = null)
    {
        var s = Settings;
        float range = strike == Strike.Kick ? s.kickRange : s.punchRange;

        // Asked again now rather than trusted from the last scan: a tenth of a second is
        // long enough for the enemy in the prompt to have died to a teammate's bomb. The man held
        // by the head is the target of every blow while he is held, wherever the crosshair is.
        var target = forced != null && !forced.IsDead ? forced : FindTarget(range);
        SetTarget(target);
        _strikeTarget = target;

        if (target != null && !IsGrappleStrike(strike) && CanTakedown(target))
        {
            strike = Strike.Takedown;
            TakedownWithFists = _fists;
        }

        float cost = strike switch
        {
            Strike.Jab => s.jabCost,
            Strike.Cross => s.crossCost,
            Strike.Kick => s.kickCost,
            Strike.Butt => s.jabCost,
            Strike.BodyBlow => s.bodyBlowCost,
            Strike.Grab => s.grabCost,
            Strike.FacePunch => s.facePunchCost,
            _ => s.takedownCost,          // the takedown and the overhand: a finisher is never refused
        };

        // Winded is slower and weaker, never refused: a key that does nothing reads as a bug.
        _tired = Stamina < cost;
        Stamina = Mathf.Max(0f, Stamina - cost);
        _lastStrikeTime = Time.time;

        float length = strike switch
        {
            Strike.Jab => s.jabSeconds,
            Strike.Cross => s.crossSeconds,
            Strike.Kick => s.kickSeconds,
            Strike.Butt => s.buttSeconds,
            Strike.BodyBlow => s.bodyBlowSeconds,
            Strike.Grab => s.grabSeconds,
            Strike.FacePunch => s.facePunchSeconds,
            Strike.Overhand => s.overhandSeconds,
            _ => s.takedownSeconds,
        };

        if (_tired) length *= 1.2f;

        float recovery = strike switch
        {
            Strike.Jab => s.jabRecovery,
            Strike.Cross => s.crossRecovery,
            Strike.Kick => s.kickRecovery,
            Strike.Butt => s.buttRecovery,
            Strike.BodyBlow => s.crossRecovery,
            Strike.Grab => 0.04f,         // the next press is the first punch, as soon as the hand has hold
            // The next punch only once this fist is most of the way home: thrown from mid-return it began from the guard
            // and the arm jumped there in a frame.
            Strike.FacePunch => Mathf.Max(s.jabRecovery, length * (1f - ImpactAt(Strike.FacePunch)) * 0.8f),
            _ => s.takedownRecovery,
        };

        _strike = strike;
        _strikeStart = Time.time + delay;
        _strikeLength = length;

        float impact = _strikeStart + length * ImpactAt(strike);
        _nextStrike = strike == Strike.Kick || strike == Strike.Takedown || strike == Strike.Overhand
            ? _strikeStart + length + recovery * 0.5f
            : impact + recovery;
        _chainUntil = _strikeStart + length + s.chainWindow;
        if (_fists) _fistsUntil = _strikeStart + length + s.fistIdleSeconds;

        // Hands off the trigger: the reload goes, and nothing fires until the strike is done.
        if (weapon != null)
        {
            weapon.CancelReload();
            weapon.Lower(delay + length + 0.1f);
        }

        PlayClip(swingClip, strike == Strike.Kick ? 0.95f : 0.8f);

        // The pull of the view happens now; the blow lands or misses when the fist or the boot
        // gets there (Animate), so what the eye sees and what the body feels are the same moment.
        // A target that has got away by then, or gone behind something, is missed.
        if (_pending) Resolve();
        if (target != null) Pull(target);
        _pending = true;
        _pendingTarget = target;
        _pendingStrike = strike;
        _pendingRange = range;

        AttackStarted?.Invoke(this, strike, target);
        return target;
    }

    /// <summary>
    /// A target that can be finished: hurt past the takedown share, reeling from a heavy
    /// blow, or down and crawling. A boss qualifies too, for the chunk the takedown takes
    /// out of it; it is never one blow from dead.
    /// </summary>
    public bool CanTakedown(Health target)
    {
        if (target == null || target.IsDead) return false;
        var s = Settings;

        float pool = target.Current + target.Shield;
        float full = Mathf.Max(1f, target.maxHealth + target.maxShield);
        if (pool / full <= s.takedownHealthShare) return true;

        var ai = target.GetComponent<EnemyAI>();
        if (ai == null) return false;

        // Reeling from something heavier than the player's own last punch: a stagger the
        // chain itself causes would otherwise turn every second punch into a finisher.
        bool ownStagger = Time.time - _lastStrikeTime < 1.2f && _strike != Strike.Kick;
        if (s.takedownWhenStaggered && ai.IsStaggered && !ownStagger) return true;
        if (s.takedownWhenCrawling && (ai.Crawling || ai.Downed)) return true;
        return false;
    }

    /// <summary>
    /// On a pad or a touch screen, the pull of the view onto the target: a turn, never a step.
    /// Nothing here moves the player (VerifyMeleeMotion measures it).
    /// </summary>
    void Pull(Health target)
    {
        var s = Settings;
        if (_motor == null || s.magnetism <= 0f || GameInput.Scheme == InputScheme.KeyboardMouse) return;
        _motor.TurnTowards(ClosestPoint(target, fpsCamera.transform.position, out _), s.magnetism);
    }

    bool _tired;
    bool _pending;
    Health _pendingTarget;
    Strike _pendingStrike;
    float _pendingRange;

    /// <summary>Deals the blow. Returns whether it landed.</summary>
    bool Hit(Health target, Strike strike)
    {
        if (IsGrappleStrike(strike)) return HitGrapple(target, strike);

        var s = Settings;
        Transform cam = fpsCamera.transform;

        var role = RoleOf(target);
        float share = strike switch
        {
            Strike.Jab => s.jabDamage.For(role),
            Strike.Cross => s.crossDamage.For(role),
            Strike.Kick => s.kickDamage.For(role),
            Strike.Butt => s.buttDamage.For(role),
            _ => s.takedownDamage.For(role),
        };

        float pool = target.Current + target.Shield;
        float full = target.maxHealth + target.maxShield;

        // A hair over what is left when the share would finish it, so the
        // shield-then-health split cannot leave it standing on a rounding error.
        float amount = Mathf.Max(1f, full * share * (_tired ? 0.7f : 1f));
        if (amount >= pool) amount = pool + 1f;

        // Where the blow lands decides where the body answers it: the head snaps back from
        // a punch to the face, the gut folds round one to the stomach.
        var point = AimPoint(target, cam, out BodyPart part);
        var info = new DamageInfo(amount, point, -cam.forward, cam.forward, gameObject)
        {
            fromMelee = true,
            part = part,
        };

        info.amount = target.ApplyDamage(info);
        if (info.amount <= 0f) return false;

        if (weapon != null) weapon.ReportHit(info);

        if (target.IsDead) return true;

        var ai = target.GetComponent<EnemyAI>();
        if (ai == null) return true;

        // The stagger starts on this frame: a step back off the blow (through the motor, so a body
        // moved by its clips moves for it too), its attack taken away.
        Vector3 away = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        float give = role == EnemyArchetype.Role.Boss ? 0.3f : role == EnemyArchetype.Role.Elite ? 0.6f : 1f;
        switch (strike)
        {
            case Strike.Jab: ai.Shove(away * s.jabStepBack * give, s.stepBackSeconds, s.jabStagger); break;
            case Strike.Butt: ai.Shove(away * s.buttStepBack * give, s.stepBackSeconds, s.crossStagger); break;
            case Strike.Cross: ai.Shove(away * s.crossStepBack * give, s.stepBackSeconds, s.crossStagger); break;
            case Strike.Kick:
            {
                if (role == EnemyArchetype.Role.Boss)
                {
                    ai.Shove(away * 0.4f, 0.3f, 0.3f);
                    break;
                }

                float push = role == EnemyArchetype.Role.Elite ? s.eliteKnockbackShare : 1f;
                // Thrown as a ragdoll; a body that cannot be (none, or already on its way up) is
                // driven back kickKnockback metres instead.
                ai.Knock((away + Vector3.up * 0.25f) * s.kickLaunchSpeed * push, s.kickStagger, s.kickKnockback * push);
                break;
            }
        }
        return true;
    }

    static EnemyArchetype.Role RoleOf(Health target)
    {
        var ai = target.GetComponent<EnemyAI>();
        return ai != null && ai.archetype != null ? ai.archetype.role : EnemyArchetype.Role.Standard;
    }

    void RegenStamina()
    {
        var s = Settings;
        if (Stamina < 0f) Stamina = s.maxStamina;
        if (Time.time - _lastStrikeTime < s.staminaRegenDelay) return;
        Stamina = Mathf.Min(s.maxStamina, Stamina + s.staminaRegen * Time.deltaTime);
    }

    // ======================================================================
    // Finding a target
    // ======================================================================

    /// <summary>
    /// The enemy nearest the crosshair within reach and the cone, with nothing solid
    /// between the eye and it. Null when there is none.
    /// </summary>
    Health FindTarget(float range)
    {
        if (fpsCamera == null) return null;
        EnsureMasks();

        var s = Settings;
        float reach = range;

        Transform cam = fpsCamera.transform;
        Vector3 eye = cam.position;

        // Every enemy is a dozen or more hitboxes, so two or three close by fill a small buffer, and
        // a full buffer leaves out whatever did not fit -- maybe the one under the crosshair. Asked
        // again with room to spare until it is not full.
        int count = Physics.OverlapSphereNonAlloc(eye, reach, Overlap, _enemyMask, QueryTriggerInteraction.Ignore);
        while (count == _overlap.Length && _overlap.Length < MaxOverlap)
        {
            _overlap = new Collider[_overlap.Length * 2];
            count = Physics.OverlapSphereNonAlloc(eye, reach, _overlap, _enemyMask, QueryTriggerInteraction.Ignore);
        }

        Health best = null;
        float bestAngle = s.coneDegrees;

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

    /// <summary>The point of the target nearest the eye, and the body part it belongs to.</summary>
    Vector3 ClosestPoint(Health target, Vector3 from, out BodyPart part)
    {
        Vector3 best = target.transform.position + Vector3.up;
        float bestDistance = float.MaxValue;
        part = BodyPart.Torso;

        foreach (var col in target.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled) continue;
            Vector3 p = col.ClosestPoint(from);
            float d = (p - from).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
                var hitbox = col.GetComponent<Hitbox>();
                part = hitbox != null ? hitbox.Part : BodyPart.Torso;
            }
        }

        return best;
    }

    /// <summary>
    /// Where on the target the strike lands: the hitbox under the crosshair if the line of
    /// the blow meets one (a punch thrown at the head hits the head), otherwise the part
    /// of the body nearest the line.
    /// </summary>
    Vector3 AimPoint(Health target, Transform cam, out BodyPart part)
    {
        Vector3 bestPoint = target.transform.position + Vector3.up;
        part = BodyPart.Torso;
        float best = float.MaxValue;

        var ray = new Ray(cam.position, cam.forward);
        foreach (var hitbox in target.GetComponentsInChildren<Hitbox>())
        {
            var col = hitbox.GetComponent<Collider>();
            if (col == null || !col.enabled) continue;

            if (col.Raycast(ray, out RaycastHit hit, 6f))
            {
                part = hitbox.Part;
                return hit.point;
            }

            // Not under the crosshair: score it by how far it sits off the line.
            Vector3 c = col.bounds.center;
            float along = Vector3.Dot(c - ray.origin, ray.direction);
            float off = (c - (ray.origin + ray.direction * Mathf.Max(0f, along))).sqrMagnitude;
            if (off < best)
            {
                best = off;
                part = hitbox.Part;
                bestPoint = col.ClosestPoint(ray.origin + ray.direction * Mathf.Max(0.3f, along));
            }
        }

        if (best == float.MaxValue) bestPoint = ClosestPoint(target, cam.position, out part);
        return bestPoint;
    }

    void SetTarget(Health target)
    {
        bool was = Target != null;
        Target = target;
        if (was != (target != null)) ReachChanged?.Invoke(this, target != null);
    }

    // ======================================================================
    // Hit-stop
    // ======================================================================

    /// <summary>
    /// The hit-stop: on a landed blow the player's own strike holds where it met the body for a
    /// few hundredths of a second (the single thing that most makes a hit feel heavy), and
    /// everything after it in the strike moves on by the same amount, so the strike's timings
    /// do not change. Only the player's strike: it used to slow the whole world to 4%, which
    /// held the enemy's answer back by the length of the stop -- 50 to 120 ms of a body that
    /// had been hit and did not move.
    /// </summary>
    void StartHitStop(float seconds)
    {
        if (seconds <= 0f || _strike == Strike.None) return;
        float k = StrikeProgress;
        if (k < 0f) return;
        _heldAt = k;
        _holdUntil = Time.time + seconds;
        _strikeStart += seconds;
        _nextStrike += seconds;
        _chainUntil += seconds;
        if (_fists) _fistsUntil += seconds;
    }

    // ======================================================================
    // Drawing
    // ======================================================================

    /// <summary>The landing: the thud, the jolt and the freeze -- longer on a blow that kills.</summary>
    void Feel(Strike strike, bool killed)
    {
        var s = Settings;
        float extra = killed ? 0.01f : 0f;
        switch (strike)
        {
            case Strike.Kick:
                PlayClip(hitClip, 1f);
                if (_motor != null) _motor.AddShake(s.kickShake);
                StartHitStop(s.kickHitStop + extra);
                break;
            case Strike.Takedown:
            case Strike.Overhand:
                PlayClip(hitClip, 1f);
                if (_motor != null) _motor.AddShake(s.takedownShake);
                if (strike == Strike.Overhand && weapon != null) weapon.FovPulse(s.finisherFovPulse);
                StartHitStop(s.takedownHitStop + extra);
                break;
            case Strike.Grab:
                // A grip, not a blow: no shake and no hold, but the slap of the hand on his head is heard.
                PlayClip(hitClip, 0.35f);
                break;
            default:
                PlayClip(hitClip, 0.9f);
                if (_motor != null) _motor.AddShake(s.punchShake);
                if ((strike == Strike.FacePunch || strike == Strike.BodyBlow) && weapon != null) weapon.FovPulse(s.punchFovPulse);
                StartHitStop(s.punchHitStop + extra);
                break;
        }
    }

    const int MaxOverlap = 1024;

    /// <summary>
    /// Whether the target can still be hit as the blow lands: within reach of the eye, and with
    /// nothing solid between (asked again now, not trusted from the throw: an enemy that stepped
    /// behind a wall during the swing is not hit through it).
    /// </summary>
    bool Reaches(Health target, float reach)
    {
        EnsureMasks();
        Vector3 eye = fpsCamera.transform.position;
        Vector3 to = ClosestPoint(target, eye, out _) - eye;
        float distance = to.magnitude;
        if (distance > reach) return false;
        return distance <= 0.05f
               || !Physics.Raycast(eye, to / distance, distance - 0.02f, _blockMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>The blow arrives: it lands on the target if the target can still be hit, else it is a miss.</summary>
    void Resolve()
    {
        _pending = false;
        var target = _pendingTarget;
        var strike = _pendingStrike;
        _pendingTarget = null;

        // The strike's own reach and a hand's breadth more, for a target stepping back during the
        // swing. It used to be 1.2 m more, room for the lunge the strike no longer takes.
        bool landed = target != null && !target.IsDead
                      && Reaches(target, _pendingRange + Settings.impactTolerance) && Hit(target, strike);
        if (landed)
        {
            Feel(strike, target.IsDead);
            Landed?.Invoke(this, strike, target);
        }
        else Missed?.Invoke(this, strike);
    }

    /// <summary>
    /// The impact beat (sound, shake, hit-stop) and the gun's part in each strike. Driven
    /// from timestamps rather than coroutines, so a domain reload mid-swing cannot leave
    /// the gun dipped.
    /// </summary>
    void Animate()
    {
        float k = StrikeProgress;
        bool swinging = k >= 0f;

        if (_pending && (!swinging && Time.time >= _strikeStart || k >= ImpactAt(_pendingStrike))) Resolve();

        if (weapon == null) return;

        // The rifle butt: every strike without arms, and the first of a fight with them.
        bool butt = swinging && (!HasArms || _strike == Strike.Butt || (_strike == Strike.Takedown && !TakedownWithFists));
        if (butt || !HasArms)
        {
            AnimateButt(k, butt);
            return;
        }

        // Onto the back and off it again.
        float away = UITheme.EaseOut(GunAway);
        Vector3 offset = holsterOffset * away;
        Vector3 turn = holsterTurn * away;

        // The kick with the gun in the hands: slid aside fast, held there through the
        // kick, and brought back as the foot comes down.
        if (swinging && _strike == Strike.Kick && GunAway < 0.5f)
        {
            float aside = k < 0.2f ? UITheme.EaseOut(k / 0.2f) : k < 0.72f ? 1f : 1f - UITheme.EaseOut((k - 0.72f) / 0.28f);
            offset += kickAsideOffset * aside;
            turn += kickAsideTurn * aside;
        }

        weapon.HandsOffset = offset;
        weapon.HandsRotation = turn;

        // The view never moves for a strike: it stays where the player is aiming, so the
        // enemy stays in sight. (It used to dip 9 degrees for a kick and lean into a punch;
        // the kicking leg now comes into view on its own.) Only the impact shakes, a blip.
    }

    /// <summary>
    /// The rifle-butt strike, drawn the way a soldier throws one. The rifle is cocked
    /// back and rolled out first, a beat of load-up; then pitched over hard so the
    /// muzzle drops away and the stock drives up and forward to the crosshair, the view
    /// dipping into it; it stops dead on the blow and rebounds a little, the way wood
    /// does off a body; and comes back to the shoulder in its own time. Both hands stay
    /// on it throughout.
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
            return;
        }

        // The stock arrives on the frame the blow is dealt.
        float blowAt = ImpactAt(Strike.Butt);

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
        else if (k < blowAt)
        {
            // Accelerating into the blow: slow off the cock, fastest at the moment it lands.
            float t = (k - 0.2f) / (blowAt - 0.2f);
            t *= t;
            turn = Quaternion.Slerp(cock, blow, t);
            offset = Vector3.Lerp(buttCockOffset, blowOffset, t);
        }
        else if (k < 0.5f)
        {
            // Stopped dead, then a small rebound off whatever it hit.
            float t = Mathf.Sin((k - blowAt) / (0.5f - blowAt) * Mathf.PI);
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

        float length = weapon.HasArms && weapon.ViewButtLength > 0f ? weapon.ViewButtLength : buttLength;
        turn = Quaternion.Inverse(weapon.PoseHipRotation) * look;
        offset = buttStrikePoint - stock * length - weapon.PoseHipPosition;
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
