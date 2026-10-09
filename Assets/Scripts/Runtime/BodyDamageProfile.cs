using UnityEngine;

/// <summary>
/// How a body takes a hit, part by part: the multiplier on the weapon's damage, how much
/// each part can take, when a limb becomes wounded or crippled, how much of a limb hit
/// reaches the core, and what pain and wounds do to the way it fights. One per enemy type
/// (<see cref="EnemyArchetype.bodyProfile"/>, made by FPSKit > Enemy AI > Make Body
/// Profiles); the soldier's own BodyDamage asset is the fallback. Every hitbox and the
/// wounds read it at the moment of the hit, so a change in the Inspector lands on the next
/// shot, in play.
///
/// Health figures are shares of the enemy's max health, so one profile fits a level that
/// scales health up. What the states then do -- the limp, the crawl, the one-handed rifle
/// -- is <see cref="EnemyWounds"/>'s.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Body Damage Profile", fileName = "BodyDamage")]
public class BodyDamageProfile : ScriptableObject
{
    [Header("Multipliers on the weapon's damage")]
    [Tooltip("Through the head. An ordinary enemy dies to one whatever this says (EnemyWounds).")]
    [Min(0f)] public float head = 3f;
    [Min(0f)] public float neck = 2f;
    [Tooltip("The chest: the reference everything else is measured against.")]
    [Min(0f)] public float upperTorso = 1f;
    [Tooltip("The belly and hips.")]
    [Min(0f)] public float lowerTorso = 0.9f;
    [Min(0f)] public float upperArm = 0.7f;
    [Min(0f)] public float forearm = 0.6f;
    [Min(0f)] public float thigh = 0.8f;
    [Min(0f)] public float shin = 0.7f;

    [Header("Part health, as a share of the enemy's max health")]
    [Tooltip("What the head can take before it counts as wrecked. Only the pain and the blood read it: " +
             "a headshot's lethality is the headshot rule.")]
    [Range(0.05f, 2f)] public float headHealth = 0.35f;
    [Range(0.05f, 2f)] public float neckHealth = 0.3f;
    [Tooltip("The chest. Hits here come off the core in full; this only scales pain and bleeding.")]
    [Range(0.05f, 2f)] public float upperTorsoHealth = 0.8f;
    [Range(0.05f, 2f)] public float lowerTorsoHealth = 0.6f;
    [Tooltip("Upper arm plus forearm is the arm's health.")]
    [Range(0.05f, 2f)] public float upperArmHealth = 0.3f;
    [Range(0.05f, 2f)] public float forearmHealth = 0.25f;
    [Tooltip("Thigh plus lower leg is the leg's health. At 0.4 + 0.3 a 100-health grunt's leg takes 70: " +
             "four rifle rounds through the thigh cripple it.")]
    [Range(0.05f, 2f)] public float thighHealth = 0.4f;
    [Range(0.05f, 2f)] public float lowerLegHealth = 0.3f;
    [Tooltip("How far a wound resistance of 1 (the archetype's) stretches every part's health. At 2.5 a " +
             "fully resistant enemy's limbs take three and a half times the punishment.")]
    [Min(0f)] public float resistanceStretch = 2.5f;

    [Header("Limb states")]
    [Tooltip("Share of a limb's health lost before it is Wounded: a flinch, a little slower, a little worse aim.")]
    [Range(0.01f, 1f)] public float woundedAt = 0.2f;
    [Tooltip("Share of a limb's health lost before it is Crippled, for good: a leg limps for the rest of its " +
             "life, an arm can no longer hold the rifle properly.")]
    [Range(0.1f, 2f)] public float crippledAt = 1f;

    [Header("The core: a limb shot never kills")]
    [Tooltip("Share of a limb hit's damage that also comes off health.")]
    [Range(0f, 1f)] public float limbCoreShare = 0.3f;
    [Tooltip("Health a limb hit can never take it below, as a share of max health. Only the head, neck, " +
             "torso, a blast or a melee takedown can finish it from here.")]
    [Range(0f, 0.9f)] public float limbCoreFloor = 0.15f;

    [Header("Pain")]
    [Tooltip("Pain a hit adds when its damage is a fifth of max health, by where it lands. Bigger hits add " +
             "more, up to twice this. Pain runs 0 to 1.")]
    [Range(0f, 1f)] public float headPain = 0.6f;
    [Range(0f, 1f)] public float torsoPain = 0.35f;
    [Range(0f, 1f)] public float armPain = 0.3f;
    [Range(0f, 1f)] public float legPain = 0.4f;
    [Tooltip("Pain lost per second.")]
    [Range(0f, 1f)] public float painFade = 0.12f;
    [Tooltip("Pain a crippled limb keeps up for good: it hurts while it lives.")]
    [Range(0f, 0.5f)] public float crippledPainFloor = 0.15f;
    [Tooltip("Above this it groans, stumbles when hit on the move, and aims worst.")]
    [Range(0f, 1f)] public float highPain = 0.55f;
    [Tooltip("How much wider its shots spread at full pain, as a multiple.")]
    [Range(1f, 4f)] public float painSpread = 1.8f;

    [Header("What wounds do")]
    [Tooltip("Share of its speed left with a wounded (not crippled) leg.")]
    [Range(0.3f, 1f)] public float woundedLegSpeed = 0.9f;
    [Tooltip("Share of its speed left limping, for a body with no limp animation. A rigged body's limp " +
             "speed is the animation's.")]
    [Range(0.1f, 1f)] public float limpSpeed = 0.55f;
    [Tooltip("Share of its speed left crawling, for a body with no crawl animation.")]
    [Range(0.05f, 1f)] public float crawlSpeed = 0.25f;
    [Tooltip("Spread multiple for a wounded arm.")]
    [Range(1f, 5f)] public float woundedArmSpread = 1.5f;
    [Tooltip("Spread multiple for a crippled arm: the rifle held low in one hand.")]
    [Range(1f, 8f)] public float crippledArmSpread = 3.5f;
    [Tooltip("Time between bursts with a wounded arm, as a multiple: slower to reload and recover.")]
    [Range(1f, 3f)] public float woundedArmFireInterval = 1.25f;
    [Tooltip("Time between bursts with a crippled arm, as a multiple.")]
    [Range(1f, 4f)] public float crippledArmFireInterval = 1.8f;

    [Header("Knockdown and getting up")]
    [Tooltip("Seconds it lies after a kick or a fall, once it has come to rest, for a healthy body: " +
             "picked between these each time.")]
    public Vector2 groundSeconds = new Vector2(1f, 2.5f);
    [Tooltip("Extra seconds on the ground at full pain.")]
    [Range(0f, 5f)] public float groundPainExtra = 1f;
    [Tooltip("Extra seconds on the ground with any limb wounded or worse.")]
    [Range(0f, 5f)] public float groundWoundedExtra = 0.5f;
    [Tooltip("Seconds from the first push off the ground to standing: picked between these each time. " +
             "Each get-up clip is played at whatever speed fits, within the limits below.")]
    public Vector2 riseSeconds = new Vector2(1.5f, 2.5f);
    [Tooltip("Extra rise seconds at full pain: it gets up heavier.")]
    [Range(0f, 3f)] public float painRiseExtra = 0.5f;
    [Tooltip("Pain at or above which it uses the slow, hurt get-ups.")]
    [Range(0f, 1f)] public float slowRisePain = 0.5f;
    [Tooltip("Slowest and fastest a get-up clip may be played to fit the rise time.")]
    public Vector2 riseSpeedLimits = new Vector2(0.85f, 1.8f);
    [Tooltip("Seconds the ragdoll's pose is blended into the get-up clip: picked between these.")]
    public Vector2 riseBlendSeconds = new Vector2(0.3f, 0.5f);
    [Tooltip("Seconds of recovery once standing -- a brief stagger, a hand to the knee, a look at the " +
             "player -- before the weapon comes back up.")]
    public Vector2 recoverSeconds = new Vector2(0.2f, 0.4f);
    [Tooltip("Seconds to bring the rifle from the sling (or from the hand after a pick-up) back to the ready.")]
    [Range(0.2f, 1.5f)] public float weaponRecoverSeconds = 0.5f;
    [Tooltip("Seconds after it is fully up before it may shoot, at middling aggression: picked between " +
             "these. A more aggressive (harder) enemy shortens it, a calmer one lengthens it.")]
    public Vector2 holdFireAfterUp = new Vector2(0.4f, 0.8f);

    [Header("The rifle in a fall")]
    [Tooltip("Mass of the rifle, kilograms, when it is on the sling or on the ground.")]
    [Range(1f, 10f)] public float rifleMass = 3.5f;
    [Tooltip("Speed (m/s) of the fall below which the rifle always stays on the sling.")]
    [Range(0f, 15f)] public float slingBelowSpeed = 4f;
    [Tooltip("Speed (m/s) of the fall above which the rifle is always knocked out of the hands. In " +
             "between, the chance of dropping it rises with the speed (a full kick is about 7.7).")]
    [Range(0f, 20f)] public float dropAboveSpeed = 10f;
    [Tooltip("Metres within which a dropped rifle is worth going back for.")]
    [Range(0f, 15f)] public float pickUpReach = 5f;
    [Tooltip("It will not go for a dropped rifle with the player nearer than this, metres.")]
    [Range(0f, 30f)] public float pickUpPlayerDistance = 8f;

    public float For(BodyPart part) => part switch
    {
        BodyPart.Head => head,
        BodyPart.Neck => neck,
        BodyPart.Torso => upperTorso,
        BodyPart.LowerTorso => lowerTorso,
        BodyPart.LeftArm or BodyPart.RightArm => upperArm,
        BodyPart.LeftForearm or BodyPart.RightForearm => forearm,
        BodyPart.LeftLeg or BodyPart.RightLeg => thigh,
        BodyPart.LeftShin or BodyPart.RightShin => shin,
        _ => 1f,
    };

    /// <summary>A fine part's health as a share of max health, before toughness.</summary>
    public float HealthOf(BodyPart part) => part switch
    {
        BodyPart.Head => headHealth,
        BodyPart.Neck => neckHealth,
        BodyPart.Torso => upperTorsoHealth,
        BodyPart.LowerTorso => lowerTorsoHealth,
        BodyPart.LeftArm or BodyPart.RightArm => upperArmHealth,
        BodyPart.LeftForearm or BodyPart.RightForearm => forearmHealth,
        BodyPart.LeftLeg or BodyPart.RightLeg => thighHealth,
        BodyPart.LeftShin or BodyPart.RightShin => lowerLegHealth,
        _ => upperTorsoHealth,
    };

    /// <summary>A whole limb's health as a share of max health (both its parts), before toughness.</summary>
    public float LimbHealth(BodyPart limb) => BodyParts.Limb(limb) switch
    {
        BodyPart.LeftArm or BodyPart.RightArm => upperArmHealth + forearmHealth,
        BodyPart.LeftLeg or BodyPart.RightLeg => thighHealth + lowerLegHealth,
        var other => HealthOf(other),
    };

    /// <summary>The pain a hit to this part adds at the reference size.</summary>
    public float PainOf(BodyPart part)
    {
        if (BodyParts.IsLeg(part)) return legPain;
        if (BodyParts.IsArm(part)) return armPain;
        return part == BodyPart.Head || part == BodyPart.Neck ? headPain : torsoPain;
    }
}
