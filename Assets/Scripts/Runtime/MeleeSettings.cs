using UnityEngine;

/// <summary>
/// Every number the player's close-quarters fighting runs on: reach, the combo window,
/// the kick, the takedown, stamina and the feel of a hit. One asset, so tuning a punch is
/// opening a file in the Inspector rather than finding a field on a component in seven
/// arena scenes.
///
/// Loaded from <c>Resources/MeleeSettings</c>; when there is none, the field initialisers
/// below are the defaults, so the game plays the same with or without the asset.
///
/// Damage is a share of the target's own full health and armour, by role, for the reason
/// the punch always was: a fixed number that drops a grunt would tickle a boss, and one
/// that hurts a boss would make a grunt vanish.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Melee Settings", fileName = "MeleeSettings")]
public class MeleeSettings : ScriptableObject
{
    [System.Serializable]
    public struct Shares
    {
        [Tooltip("Share of an ordinary enemy's health and armour.")]
        [Range(0f, 1.5f)] public float standard;
        [Tooltip("Share of an elite's.")]
        [Range(0f, 1f)] public float elite;
        [Tooltip("Share of a boss's. Small on purpose: a boss that could be punched to " +
                 "death would make its fight a matter of walking up to it.")]
        [Range(0f, 0.3f)] public float boss;

        public Shares(float standard, float elite, float boss)
        {
            this.standard = standard;
            this.elite = elite;
            this.boss = boss;
        }

        public float For(EnemyArchetype.Role role) => role switch
        {
            EnemyArchetype.Role.Boss => boss,
            EnemyArchetype.Role.Elite => elite,
            _ => standard,
        };
    }

    [Header("Reach")]
    [Tooltip("Metres from the eye to the nearest point of the target for a punch.")]
    [Range(1f, 4f)] public float punchRange = 2.0f;
    [Tooltip("Metres for a kick. A leg is longer than an arm.")]
    [Range(1f, 4f)] public float kickRange = 2.5f;
    [Tooltip("Degrees either side of the crosshair a target can be and still be hit.")]
    [Range(10f, 80f)] public float coneDegrees = 45f;
    [Tooltip("Metres past the punch or kick range a target may be when the blow lands and still be " +
             "hit: it may have stepped back a little during the swing. Small on purpose: the reach is " +
             "the arm's or the leg's, the body takes no step.")]
    [Range(0f, 0.6f)] public float impactTolerance = 0.25f;

    [Header("Pad and touch help (never a mouse)")]
    [Tooltip("How hard the view turns onto the target as the strike is thrown, on a pad or a " +
             "touch screen, as a share of the angle between them. 0 = no help, 1 = snaps onto it. " +
             "A turn only: no strike ever moves the player (the lunge that used to step the " +
             "player up to 2.2 m in is gone).")]
    [Range(0f, 1f)] public float magnetism = 0.5f;

    [Header("The chain: rifle butt, then the gun goes on the back and the fists come up")]
    [Tooltip("Seconds the rifle-butt strike takes: the first press, with the gun still in the hands.")]
    [Range(0.2f, 1f)] public float buttSeconds = 0.32f;
    [Tooltip("Seconds after the butt strike before the next press can start.")]
    [Range(0.05f, 1f)] public float buttRecovery = 0.12f;
    [Tooltip("Share of the target's pool the rifle butt takes.")]
    public Shares buttDamage = new Shares(0.45f, 0.18f, 0.03f);
    [Tooltip("Another melee press within this many seconds of the last strike carries the " +
             "fight on: after the butt it puts the gun on the back and throws fists.")]
    [Range(0.2f, 3f)] public float chainWindow = 1.2f;
    [Tooltip("Seconds to swing the gun onto the back. The first punch lands as it goes.")]
    [Range(0.1f, 0.8f)] public float holsterSeconds = 0.16f;
    [Tooltip("Seconds to bring the gun back off the back into the hands.")]
    [Range(0.1f, 0.8f)] public float drawSeconds = 0.34f;
    [Tooltip("With the fists up and no strike for this long, the gun comes back. Firing, " +
             "aiming or reloading brings it back at once.")]
    [Range(0.3f, 5f)] public float fistIdleSeconds = 1.8f;

    [Header("Punch: jab and cross, alternating")]
    [Tooltip("Seconds the jab takes, out and back.")]
    [Range(0.15f, 0.8f)] public float jabSeconds = 0.22f;
    [Tooltip("Seconds the cross takes. A little longer: the whole body turns into it.")]
    [Range(0.15f, 0.8f)] public float crossSeconds = 0.26f;
    [Tooltip("Seconds after a jab before another strike can start. Short, so the cross " +
             "can follow it at once.")]
    [Range(0.05f, 1f)] public float jabRecovery = 0.12f;
    [Tooltip("Seconds after the cross before the next strike: the combo's end.")]
    [Range(0.1f, 1.5f)] public float crossRecovery = 0.15f;
    [Tooltip("Share of the target's pool the jab takes.")]
    public Shares jabDamage = new Shares(0.6f, 0.2f, 0.03f);
    [Tooltip("Share the cross takes. The butt and one punch put an ordinary enemy down.")]
    public Shares crossDamage = new Shares(0.75f, 0.3f, 0.05f);
    [Tooltip("Seconds a landed jab staggers the target.")]
    [Range(0f, 1.5f)] public float jabStagger = 0.35f;
    [Tooltip("Seconds a landed cross staggers the target.")]
    [Range(0f, 2f)] public float crossStagger = 0.6f;

    [Header("Kick")]
    [Tooltip("Seconds the kick takes, chamber to foot down.")]
    [Range(0.3f, 1.4f)] public float kickSeconds = 0.42f;
    [Tooltip("Seconds after a kick before any strike can start. Longer than a punch's.")]
    [Range(0.2f, 3f)] public float kickRecovery = 0.3f;
    [Tooltip("Share of the target's pool the kick takes.")]
    public Shares kickDamage = new Shares(0.35f, 0.15f, 0.03f);
    [Tooltip("Speed, in metres a second, an ordinary enemy is thrown back at. It flies, lands " +
             "and gets up; a strike while it is down is a takedown.")]
    [Range(0f, 15f)] public float kickLaunchSpeed = 7.5f;
    [Tooltip("Metres an enemy that cannot be thrown is driven back along the ground instead: one " +
             "with no ragdoll, or one already on its way up from the last fall.")]
    [Range(0f, 6f)] public float kickKnockback = 3.5f;
    [Tooltip("Seconds it is off balance afterwards. Long enough to follow up.")]
    [Range(0f, 4f)] public float kickStagger = 1.5f;
    [Tooltip("Share of the knockback an elite takes. A boss only rocks.")]
    [Range(0f, 1f)] public float eliteKnockbackShare = 0.5f;

    [Header("Takedown")]
    [Tooltip("A target at or below this share of its health can be finished by any melee press.")]
    [Range(0f, 1f)] public float takedownHealthShare = 0.35f;
    [Tooltip("A staggered target can be finished too.")]
    public bool takedownWhenStaggered = true;
    [Tooltip("A target crawling on the floor can be finished too.")]
    public bool takedownWhenCrawling = true;
    [Tooltip("Share of the target's pool the takedown takes. Over 1 for anything it should end.")]
    public Shares takedownDamage = new Shares(1.2f, 1.2f, 0.12f);
    [Tooltip("Seconds the finishing blow takes.")]
    [Range(0.3f, 1.2f)] public float takedownSeconds = 0.4f;
    [Tooltip("Seconds after a takedown before the next strike.")]
    [Range(0.1f, 2f)] public float takedownRecovery = 0.35f;

    [Header("Grab and punch (the fight in the film Master, 2026-10-22)")]
    [Tooltip("Metres from the eye to the nearest point of an enemy's head inside which a punch at the head takes him by it " +
             "instead (the rifle swings onto the back first). Face to face: arm's length.")]
    [Range(0.6f, 1.6f)] public float grabRange = 1.15f;
    [Tooltip("Degrees off facing the player inside which a standing enemy can be taken by the face. One doubled up over " +
             "a body blow is taken by the back of the head whichever way he faces.")]
    [Range(20f, 120f)] public float grabFacingDegrees = 75f;
    [Tooltip("Metres from the player to the body of the enemy held by the head: he is pulled in to this and kept there " +
             "(he pulls against it). Far enough that his whole face is in the middle of the view, near enough to hit.")]
    [Range(0.4f, 1.2f)] public float holdDistance = 0.68f;
    [Tooltip("Seconds the hand takes to reach the head and close on it.")]
    [Range(0.15f, 0.8f)] public float grabSeconds = 0.3f;
    [Tooltip("Seconds each punch to the face of a held enemy takes: a wind-up the player sees (drawn back, wound out or " +
             "dropped low), the blow, a beat on the face and back. At 0.32 the fist went from the guard to the face in seven " +
             "frames and nobody saw it (Ishaan, 2026-10-22).")]
    [Range(0.15f, 0.8f)] public float facePunchSeconds = 0.44f;
    [Tooltip("Seconds the overhand (the finishing blow brought down from above the head) takes.")]
    [Range(0.25f, 1.2f)] public float overhandSeconds = 0.62f;
    [Tooltip("Seconds the hook to the belly takes.")]
    [Range(0.15f, 0.8f)] public float bodyBlowSeconds = 0.3f;
    [Tooltip("Punches to the face before the next press is the overhand: the straight right, the hook, the uppercut, in turn.")]
    [Range(1, 4)] public int facePunchesBeforeFinisher = 3;
    [Tooltip("Share of the pool each punch to a held face takes.")]
    public Shares facePunchDamage = new Shares(0.22f, 0.12f, 0f);
    [Tooltip("Share the overhand takes: over 1 ends an ordinary enemy (Ishaan's call: the final blow kills); an elite is " +
             "left on the floor with at least 5% of its pool, and gets up hurt.")]
    public Shares overhandDamage = new Shares(1.2f, 0.45f, 0f);
    [Tooltip("Share the hook to the belly takes.")]
    public Shares bodyBlowDamage = new Shares(0.2f, 0.12f, 0.03f);
    [Tooltip("A held enemy is never finished by the punches to the face: they leave him at least this share of his pool, " +
             "so the overhand is always the blow that ends it.")]
    [Range(0f, 0.5f)] public float heldFloor = 0.12f;
    [Tooltip("Seconds without a punch landing before the held enemy wrenches himself free and runs.")]
    [Range(0.4f, 4f)] public float releaseAfter = 2.2f;
    [Tooltip("Seconds a hook to the belly folds an ordinary enemy over it (an elite 60%, a boss not at all): the window " +
             "in which the next press takes him by the back of the head.")]
    [Range(0.3f, 3f)] public float doubledSeconds = 1.4f;
    [Tooltip("Metres a second the overhand drives a body that survives it (an elite) down onto the floor.")]
    [Range(0f, 6f)] public float collapseSpeed = 2.2f;
    [Tooltip("Degrees the view narrows for a moment when a punch to the face or the hook lands (scaled by Camera Shake).")]
    [Range(0f, 6f)] public float punchFovPulse = 1.6f;
    [Tooltip("Degrees it narrows when the overhand lands.")]
    [Range(0f, 8f)] public float finisherFovPulse = 2.8f;
    [Range(0f, 100f)] public float grabCost = 8f;

    [Header("Held, he fights back (Ishaan, 2026-10-22: \"the enemy also don't try to run or punch when I hold it\")")]
    [Tooltip("Seconds after the grab before a held enemy first winds up a punch at the player.")]
    [Range(0.2f, 3f)] public float counterFirstAfter = 0.7f;
    [Tooltip("Seconds between his tries after that (picked between the two each time).")]
    public Vector2 counterEvery = new Vector2(1.0f, 1.5f);
    [Tooltip("Seconds after a punch to his face before he can wind up again: a player who keeps hitting always gets in " +
             "first, one who stops is hit.")]
    [Range(0f, 2f)] public float counterAfterBlow = 0.35f;
    [Tooltip("Seconds his wind-up takes (his free fist drawn back by his shoulder): the window in which a punch to his face " +
             "stops it. Matches the keyed clip (Tools/blender/enemy_grabbed_clips.py, the hook lands at 0.55 s).")]
    [Range(0.1f, 1f)] public float counterWindup = 0.45f;
    [Tooltip("Seconds from the end of the wind-up to his fist arriving.")]
    [Range(0.03f, 0.4f)] public float counterSwing = 0.1f;
    [Tooltip("Share of his own attack damage his punch does to the player.")]
    [Range(0f, 2f)] public float counterDamage = 0.6f;
    [Tooltip("Camera shake when his punch lands on the player (a landed blow; scaled by Camera Shake).")]
    [Range(0f, 1f)] public float counterShake = 0.45f;
    [Tooltip("His punches landed before he breaks the hold.")]
    [Range(1, 4)] public int countersToBreak = 2;
    [Tooltip("Seconds he takes to wrench himself free (both hands thrown at the player's arm, the head ripped back).")]
    [Range(0.1f, 1.2f)] public float wrenchSeconds = 0.6f;
    [Tooltip("Seconds he runs from the player once free, before he turns to fight again.")]
    [Range(0f, 6f)] public float boltSeconds = 2.4f;
    [Tooltip("Metres he tugs back against the hold while he struggles (and the player's arm with him).")]
    [Range(0f, 0.3f)] public float struggleTug = 0.07f;
    [Range(0f, 100f)] public float facePunchCost = 12f;
    [Range(0f, 100f)] public float bodyBlowCost = 18f;

    [Header("Stamina")]
    [Tooltip("The pool every strike draws on. Full, it is five punches or two kicks and a " +
             "punch before the player is winded.")]
    [Min(1f)] public float maxStamina = 100f;
    [Range(0f, 100f)] public float jabCost = 16f;
    [Range(0f, 100f)] public float crossCost = 20f;
    [Range(0f, 100f)] public float kickCost = 34f;
    [Tooltip("Free: a takedown ends a fight, it should never be refused for being tired.")]
    [Range(0f, 100f)] public float takedownCost = 0f;
    [Tooltip("Stamina back per second once the player stops striking.")]
    [Min(0f)] public float staminaRegen = 32f;
    [Tooltip("Seconds after a strike before stamina starts coming back.")]
    [Range(0f, 3f)] public float staminaRegenDelay = 0.7f;

    [Header("Hit feel")]
    [Tooltip("Seconds the player's own strike holds where it met the body when a punch lands (the hit-stop). " +
             "Only the strike holds: the enemy answers the blow on the frame it lands. Kept to 40-60 ms.")]
    [Range(0f, 0.06f)] public float punchHitStop = 0.045f;
    [Range(0f, 0.06f)] public float kickHitStop = 0.055f;
    [Range(0f, 0.06f)] public float takedownHitStop = 0.06f;
    [Tooltip("Metres an ordinary enemy steps back off a punch, starting on the frame it lands (an elite 60%, a boss 30%).")]
    [Range(0f, 1f)] public float jabStepBack = 0.25f;
    [Tooltip("Metres it steps back off the cross.")]
    [Range(0f, 1f)] public float crossStepBack = 0.35f;
    [Tooltip("Metres it steps back off the rifle butt.")]
    [Range(0f, 1f)] public float buttStepBack = 0.3f;
    [Tooltip("Seconds the step back takes. Most of it in the first frames: the blow moves the body at once.")]
    [Range(0.05f, 0.5f)] public float stepBackSeconds = 0.18f;
    [Tooltip("Camera shake on a landed punch, 0 to 1.")]
    [Range(0f, 1f)] public float punchShake = 0.25f;
    [Tooltip("Camera shake when a kick lands: a blip at the impact only, never in the wind-up. " +
             "Shake goes with the square of this: 0.35 is about a fifth of a degree for a few frames.")]
    [Range(0f, 1f)] public float kickShake = 0.35f;
    [Range(0f, 1f)] public float takedownShake = 0.4f;

    // ======================================================================

    public const string ResourcePath = "MeleeSettings";

    static MeleeSettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    /// <summary>The asset in Resources, or the defaults when there is none.</summary>
    public static MeleeSettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<MeleeSettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<MeleeSettings>();
                _active.name = "Default Melee";
            }
            return _active;
        }
    }
}
