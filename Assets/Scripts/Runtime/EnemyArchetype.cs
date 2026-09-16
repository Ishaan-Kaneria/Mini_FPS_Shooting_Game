using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One enemy variant, described as data rather than as a prefab.
///
/// The WaveManager spawns a single base enemy prefab and then stamps an archetype
/// onto the instance: stats, size, colour, behaviour and what it is worth. That is
/// deliberate -- a new enemy is a new asset created from the Create menu, with no
/// prefab to rig, no scene to rebuild and no code to touch. Duplicate an existing
/// one, change the numbers, drop it in the WaveManager roster, done.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Enemy Archetype", fileName = "Archetype")]
public class EnemyArchetype : ScriptableObject
{
    public enum Role
    {
        /// <summary>Ordinary wave filler.</summary>
        Standard,

        /// <summary>Rarer and meaner. Still drawn from the normal roster.</summary>
        Elite,

        /// <summary>Only ever spawned as a wave's boss, one at a time, with its own HUD bar.</summary>
        Boss
    }

    [Header("Identity")]
    public string displayName = "Grunt";
    [TextArea(2, 4)] public string description;
    public Role role = Role.Standard;

    [Header("Appearance")]
    public Color bodyColor = new Color(0.55f, 0.18f, 0.18f);
    public Color headColor = new Color(0.75f, 0.30f, 0.25f);

    [Tooltip("Emissive rim so dangerous variants read at a glance in a dark theme. " +
             "Black means no glow.")]
    [ColorUsage(false, true)] public Color glowColor = Color.black;

    [Tooltip("Uniform size. Big means slow and obvious; small means fast and hard to hit.")]
    [Range(0.4f, 3f)] public float scaleMultiplier = 1f;

    [Header("When It Shows Up")]
    [Tooltip("First wave this variant can appear on.")]
    [Min(1)] public int unlockWave = 1;

    [Tooltip("Relative chance of being picked once unlocked, before growth is applied.")]
    [Min(0f)] public float baseWeight = 1f;

    [Tooltip("Added to the weight for every wave past the unlock. Above zero the variant " +
             "starts rare and takes over; at zero it stays a constant share of the mix.")]
    public float weightGrowthPerWave;

    [Min(0f)] public float maxWeight = 12f;

    [Tooltip("Stop spawning this variant once the wave passes here, so early trash " +
             "makes way for the real threats. 0 means it never retires.")]
    [Min(0)] public int retireWave;

    [Header("Stats (multipliers on the base enemy)")]
    [Min(0.05f)] public float healthMultiplier = 1f;

    [Tooltip("Shield granted as a fraction of this variant's max health. 0.5 means an " +
             "armour layer worth half its health that you have to break first.")]
    [Range(0f, 3f)] public float shieldFraction;

    [Min(0f)] public float damageMultiplier = 1f;
    [Min(0.1f)] public float speedMultiplier = 1f;

    [Header("Behaviour")]
    public bool ranged;

    [Tooltip("Metres at which it commits to an attack. Melee wants ~2, a shooter wants 20+.")]
    public float attackRange = 2.2f;

    [Tooltip("Reach of a swing. A shooter inside this range hits you with the rifle " +
             "instead of firing it, so closing on one is not a free pass.")]
    public float meleeRange = 2.4f;

    [Tooltip("Seconds between attacks before wave aggression shortens it.")]
    public float attackCooldown = 1.3f;

    [Tooltip("Telegraph time before the damage lands. This is the player's reaction window, " +
             "so a heavy hitter should wind up longer, not shorter.")]
    public float attackWindup = 0.35f;

    [Tooltip("Shots per ranged attack. Above 1 turns a plinker into a suppressor.")]
    [Min(1)] public int shotsPerAttack = 1;

    [Tooltip("Seconds between the shots of one burst.")]
    [Min(0f)] public float burstInterval = 0.12f;

    [Tooltip("What one of its shots is worth against one of its swings. Well under 1 " +
             "throughout the roster: a variant that shoots is already trading the need " +
             "to close for the ability to hurt you from anywhere, and letting it keep " +
             "full damage on top of that is what turns a crowd of riflemen into an " +
             "unsurvivable one.")]
    [Range(0f, 2f)] public float rangedDamageMultiplier = 0.4f;

    [Tooltip("Cone half-angle of ranged fire, in degrees. Larger is more forgiving.")]
    public float rangedSpread = 2.5f;

    [Tooltip("Metres a shooter tries to hold. It backs off when you close, which is what " +
             "stops a ranged enemy from wandering into melee and dying pointlessly.")]
    public float preferredRangedDistance = 14f;

    [Tooltip("Sidestep while closing instead of walking a straight line at you. " +
             "A crowd that all beelines reads as a queue; a crowd that circles reads as a pack.")]
    [Range(0f, 1f)] public float strafeAmount = 0.35f;

    [Tooltip("Speed multiplier for a short sprint once it is in mid-range. " +
             "1 disables the charge.")]
    [Min(1f)] public float chargeSpeedMultiplier = 1f;

    [Tooltip("Damage in one hit needed to interrupt it. Lower means easier to stagger, " +
             "so a light enemy flinches under fire and a boss shrugs it off. 0 never staggers.")]
    [Min(0f)] public float staggerThreshold = 18f;

    [Tooltip("Whether sustained fire sends it looking for cover. Off for a boss, which " +
             "is supposed to be the thing that keeps coming.")]
    public bool canRetreat = true;

    [Tooltip("Damage taken in quick succession before it breaks off. Higher on anything " +
             "meant to hold its ground.")]
    [Min(1f)] public float suppressionDamage = 45f;

    [Header("Reward")]
    [Min(0)] public int scoreValue = 100;

    [Range(0f, 1f)] public float healthDropChance = 0.08f;

    [Tooltip("Chance of dropping a shield cell, rolled only when the health roll fails.")]
    [Range(0f, 1f)] public float shieldDropChance = 0.06f;

    [Tooltip("Chance of dropping ammo. Worth nothing while the weapon has an infinite " +
             "reserve, which is the default -- turn that off and this starts mattering.")]
    [Range(0f, 1f)] public float ammoDropChance;

    /// <summary>True if this variant is eligible on the given wave, ignoring role.</summary>
    public bool IsAvailable(int wave)
        => wave >= unlockWave && (retireWave <= 0 || wave <= retireWave);

    /// <summary>
    /// Selection weight on a given wave. Growth is what makes the mix drift: grunts
    /// thin out, the things that were rare on wave 5 are the bulk of wave 20.
    /// </summary>
    public float WeightAtWave(int wave)
    {
        if (!IsAvailable(wave)) return 0f;

        float grown = baseWeight + weightGrowthPerWave * (wave - unlockWave);
        return Mathf.Clamp(grown, 0f, Mathf.Max(0.01f, maxWeight));
    }

    /// <summary>
    /// Stamps this variant onto a freshly spawned enemy. The scale arguments are the
    /// WaveManager's per-wave growth, applied on top of the archetype's own multipliers
    /// so the two curves compose instead of fighting.
    /// </summary>
    public void ApplyTo(GameObject enemy, float healthScale = 1f, float damageScale = 1f,
                        float speedScale = 1f)
    {
        if (enemy == null) return;

        if (!Mathf.Approximately(scaleMultiplier, 1f))
            enemy.transform.localScale *= scaleMultiplier;

        var health = enemy.GetComponent<Health>();
        if (health != null)
        {
            health.SetMaxHealth(health.maxHealth * healthMultiplier * healthScale);
            health.SetMaxShield(health.maxHealth * shieldFraction);
        }

        var ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.archetype = this;
            ai.ranged = ranged;
            ai.attackRange = attackRange;
            ai.attackCooldown = attackCooldown;
            ai.attackWindup = attackWindup;
            ai.attackDamage *= damageMultiplier * damageScale;
            ai.meleeRange = meleeRange;
            ai.shotsPerAttack = shotsPerAttack;
            ai.burstInterval = burstInterval;
            ai.rangedDamageMultiplier = rangedDamageMultiplier;
            ai.rangedSpread = rangedSpread;
            ai.canRetreat = canRetreat;
            ai.suppressionDamage = suppressionDamage;
            ai.preferredRangedDistance = preferredRangedDistance;
            ai.strafeAmount = strafeAmount;
            ai.chargeSpeedMultiplier = chargeSpeedMultiplier;
            ai.staggerThreshold = staggerThreshold;
        }

        var agent = enemy.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed *= speedMultiplier * speedScale;

            // A bigger body needs a bigger footprint or it clips through crowds, but
            // an over-wide agent stops fitting through the arena's doorways.
            if (!Mathf.Approximately(scaleMultiplier, 1f))
            {
                agent.radius = Mathf.Clamp(agent.radius * scaleMultiplier, 0.2f, 1.1f);
                agent.height = Mathf.Max(0.5f, agent.height * scaleMultiplier);
            }
        }

        Tint(enemy);
    }

    /// <summary>
    /// Recolours the instance through a property block, which costs no material
    /// instance and therefore no extra draw call per enemy.
    ///
    /// Emission is written unconditionally rather than keyword-toggled here: turning
    /// on _EMISSION per renderer would need renderer.material, and that instantiates
    /// the material -- one copy per enemy, every wave. The builder enables the keyword
    /// on the shared enemy material once instead, with the colour left black, so this
    /// block can drive the glow for free.
    /// </summary>
    void Tint(GameObject enemy)
    {
        var block = new MaterialPropertyBlock();

        foreach (var renderer in enemy.GetComponentsInChildren<Renderer>())
        {
            // Body only: a carried weapon is not flesh and must not be painted the
            // archetype's colour. See EnemyAI.IsBodyRenderer for the rule.
            if (!EnemyAI.IsBodyRenderer(renderer)) continue;

            // The head is the headshot box, so it keeps its own lighter shade.
            bool isHead = renderer.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0;
            Color color = isHead ? headColor : bodyColor;

            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            block.SetColor("_EmissionColor", glowColor);
            renderer.SetPropertyBlock(block);
        }
    }
}
