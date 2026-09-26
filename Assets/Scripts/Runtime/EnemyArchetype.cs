using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One enemy variant, described as data rather than as a prefab.
///
/// The LevelManager spawns a single base enemy prefab and then stamps an archetype
/// onto the instance: stats, size, colour, behaviour and what it is worth. That is
/// deliberate -- a new enemy is a new asset created from the Create menu, with no
/// prefab to rig, no scene to rebuild and no code to touch. Duplicate an existing
/// one, change the numbers, drop it in the LevelManager roster, done.
///
/// The three fields still named "wave" are the difficulty step a level names in its
/// LevelSet, not a wave number -- the names are kept because renaming a serialized
/// field on a ScriptableObject silently drops the value out of every asset already
/// written with the old one.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Enemy Archetype", fileName = "Archetype")]
public class EnemyArchetype : ScriptableObject
{
    public enum Role
    {
        /// <summary>Ordinary filler. The bulk of a level.</summary>
        Standard,

        /// <summary>Rarer and meaner. Still drawn from the normal roster.</summary>
        Elite,

        /// <summary>Only ever spawned as a level's boss, one at a time, with its own HUD bar.</summary>
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
    [Tooltip("Lowest difficulty step a level can name and still draw this variant.")]
    [Min(1)] public int unlockWave = 1;

    [Tooltip("Relative chance of being picked once unlocked, before growth is applied.")]
    [Min(0f)] public float baseWeight = 1f;

    [Tooltip("Added to the weight for every step past the unlock. Above zero the variant " +
             "starts rare and takes over; at zero it stays a constant share of the mix.")]
    public float weightGrowthPerWave;

    [Min(0f)] public float maxWeight = 12f;

    [Tooltip("Stop spawning this variant once the step passes here, so early trash " +
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

    [Tooltip("Seconds between attacks before the level's aggression shortens it.")]
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

    [Header("Body")]
    [Tooltip("What it sounds like when it is hurt, when it hunts and when it dies. " +
             "Human soldiers grunt, shout and scream; creatures growl, snarl and roar.")]
    public EnemyVoice.Kind voice = EnemyVoice.Kind.Human;

    [Tooltip("How much punishment its limbs take before a wound changes how it fights, " +
             "0 to 1. At 0 a couple of rounds in the leg put it on the floor and one " +
             "through the gun arm makes it drop the rifle; at 1 it limps at worst and " +
             "never lets go of the weapon. Bosses ignore headshot kills whatever this says.")]
    [Range(0f, 1f)] public float woundResistance = 0.2f;

    [Header("Reward")]
    [Min(0)] public int scoreValue = 100;

    [Range(0f, 1f)] public float healthDropChance = 0.08f;

    [Tooltip("Chance of dropping a shield cell, rolled only when the health roll fails.")]
    [Range(0f, 1f)] public float shieldDropChance = 0.06f;

    [Tooltip("Chance of dropping ammo. Worth nothing while the weapon has an infinite " +
             "reserve, which is the default -- turn that off and this starts mattering.")]
    [Range(0f, 1f)] public float ammoDropChance;

    /// <summary>True if this variant is eligible at the given difficulty step, ignoring role.</summary>
    public bool IsAvailable(int step)
        => step >= unlockWave && (retireWave <= 0 || step <= retireWave);

    /// <summary>
    /// Selection weight at a difficulty step. Growth is what makes the mix drift: grunts
    /// thin out, and the things that were rare at step 5 are the bulk of step 20.
    /// </summary>
    public float WeightAtStep(int step)
    {
        if (!IsAvailable(step)) return 0f;

        float grown = baseWeight + weightGrowthPerWave * (step - unlockWave);
        return Mathf.Clamp(grown, 0f, Mathf.Max(0.01f, maxWeight));
    }

    /// <summary>
    /// Stamps this variant onto a freshly spawned enemy. The scale arguments are the
    /// level's own difficulty, applied on top of the archetype's own multipliers so the
    /// two compose instead of fighting.
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

        var wounds = enemy.GetComponent<EnemyWounds>();
        if (wounds != null) wounds.Configure(this);

        var voiceBox = enemy.GetComponent<EnemyVoice>();
        if (voiceBox != null) voiceBox.kind = voice;

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

        Dress(enemy);
        Tint(enemy);
    }

    /// <summary>
    /// Switches the kit the prefab carries for everybody to what this one wears, by the
    /// Kit_&lt;Who&gt;_ naming the builder uses: Human and Creature by voice, Elite for elites
    /// and bosses, Boss for bosses alone. A prefab with no such children is left as it is.
    /// </summary>
    void Dress(GameObject enemy)
    {
        bool creature = voice == EnemyVoice.Kind.Creature;
        bool elite = role != Role.Standard;
        bool boss = role == Role.Boss;

        foreach (var part in enemy.GetComponentsInChildren<Transform>(true))
        {
            string name = part.name;
            if (!name.StartsWith("Kit_", System.StringComparison.Ordinal)) continue;

            bool wear = name.StartsWith("Kit_Human_", System.StringComparison.Ordinal) ? !creature && !(boss && name.Contains("Goggles"))
                      : name.StartsWith("Kit_Creature_", System.StringComparison.Ordinal) ? creature
                      : name.StartsWith("Kit_Elite_", System.StringComparison.Ordinal) ? elite
                      : name.StartsWith("Kit_Boss_", System.StringComparison.Ordinal) ? boss
                      : part.gameObject.activeSelf;

            if (part.gameObject.activeSelf != wear) part.gameObject.SetActive(wear);
        }
    }

    /// <summary>
    /// Recolours the instance through a property block, which costs no material
    /// instance and therefore no extra draw call per enemy.
    ///
    /// Emission is written unconditionally rather than keyword-toggled here: turning
    /// on _EMISSION per renderer would need renderer.material, and that instantiates
    /// the material -- one copy per enemy, every level. The builder enables the keyword
    /// on the shared enemy material once instead, with the colour left black, so this
    /// block can drive the glow for free.
    /// </summary>
    static bool IsSkin(string name)
        => name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Neck", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Human skin, from pale to dark. One is picked per soldier at spawn, so a squad is a
    /// group of people rather than a row of the same face.
    /// </summary>
    static Color SkinTone(int index)
    {
        // A switch rather than a static array: constant data, and no static for the
        // domain-reload audit to ask a reset hook of.
        switch (index)
        {
            case 0: return new Color(0.87f, 0.68f, 0.56f);
            case 1: return new Color(0.78f, 0.57f, 0.44f);
            case 2: return new Color(0.66f, 0.46f, 0.33f);
            case 3: return new Color(0.5f, 0.34f, 0.24f);
            case 4: return new Color(0.36f, 0.24f, 0.17f);
            default: return new Color(0.82f, 0.62f, 0.48f);
        }
    }

    /// <summary>
    /// The uniform: the archetype's colour, taken most of the way to a field drab. The raw
    /// colours are signal colours -- the minimap draws them -- and a soldier dressed in
    /// saturated orange reads as a toy. Keeping the hue and dropping the saturation leaves
    /// each type recognisable by its shade and makes all of them look like cloth.
    /// </summary>
    public Color UniformColor
    {
        get
        {
            float grey = bodyColor.r * 0.3f + bodyColor.g * 0.59f + bodyColor.b * 0.11f;
            var drab = new Color(0.33f, 0.32f, 0.25f);
            var muted = Color.Lerp(bodyColor, new Color(grey, grey, grey), 0.55f);
            return Color.Lerp(muted, drab, 0.35f);
        }
    }

    /// <summary>A person's skin, or for a creature a sick, bloodless pallor tinged by its own colour.</summary>
    Color SkinColor()
    {
        if (voice == EnemyVoice.Kind.Creature)
        {
            float grey = headColor.r * 0.3f + headColor.g * 0.59f + headColor.b * 0.11f;
            return Color.Lerp(new Color(0.55f, 0.56f, 0.5f), Color.Lerp(headColor, new Color(grey, grey, grey), 0.6f), 0.35f);
        }

        return SkinTone(Random.Range(0, 6));
    }

    void Tint(GameObject enemy)
    {
        var block = new MaterialPropertyBlock();
        Color skin = SkinColor();
        Color uniform = UniformColor;

        foreach (var renderer in enemy.GetComponentsInChildren<Renderer>())
        {
            // Body only: a carried weapon is not flesh and must not be painted the
            // archetype's colour. See EnemyAI.IsBodyRenderer for the rule.
            if (!EnemyAI.IsBodyRenderer(renderer)) continue;

            // Skin -- the head, which is the headshot box, and the hands and neck -- is a
            // person's; the clothes take the archetype's colour, muted to cloth.
            bool isSkin = IsSkin(renderer.name);
            Color color = isSkin ? skin : uniform;

            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);

            // The body itself never glows: a whole soldier lit up in the variant's colour
            // read as a neon mannequin. The glow goes on the optics instead, below.
            block.SetColor("_EmissionColor", Color.black);
            renderer.SetPropertyBlock(block);
        }

        // The variant's glow, where a real threat would carry a light: goggles, a visor,
        // a creature's eyes. Still the at-a-glance tell in a dark arena, now a pair of
        // lit lenses rather than a lit body.
        foreach (var renderer in enemy.GetComponentsInChildren<Renderer>(true))
        {
            string name = renderer.name;
            if (name.IndexOf("Goggles", System.StringComparison.Ordinal) < 0
                && name.IndexOf("Visor", System.StringComparison.Ordinal) < 0
                && name.IndexOf("Eyes", System.StringComparison.Ordinal) < 0) continue;

            renderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", glowColor * 1.6f);
            renderer.SetPropertyBlock(block);
        }
    }
}
