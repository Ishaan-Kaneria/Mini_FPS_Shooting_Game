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

    [Header("Lunge")]
    [Tooltip("A target this close draws the player in a step as the strike is thrown, so " +
             "a punch from just outside reach still lands. 0 turns the lunge off.")]
    [Range(0f, 5f)] public float lungeRange = 3.6f;
    [Tooltip("How close the lunge brings the player's eye to the target's body, in metres.")]
    [Range(0.4f, 2f)] public float lungeStopDistance = 0.75f;
    [Tooltip("The longest step the lunge will take, in metres.")]
    [Range(0f, 2.5f)] public float lungeMaxStep = 2.2f;
    [Tooltip("Seconds the step takes.")]
    [Range(0.05f, 0.4f)] public float lungeSeconds = 0.11f;
    [Tooltip("How hard the view turns onto the target as the strike is thrown, as a " +
             "share of the angle between them. 0 = no help, 1 = snaps onto it.")]
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
    [Tooltip("Metres an enemy with no ragdoll is driven back instead.")]
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
    [Tooltip("Seconds the world freezes when a punch lands. Real time, so it is the same at any frame rate.")]
    [Range(0f, 0.2f)] public float punchHitStop = 0.045f;
    [Range(0f, 0.25f)] public float kickHitStop = 0.08f;
    [Range(0f, 0.3f)] public float takedownHitStop = 0.12f;
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
