using UnityEngine;

/// <summary>
/// One blast, with the upgrades already folded in.
///
/// It exists so that <see cref="BombData"/> can stay the pristine baseline the way
/// <see cref="WeaponData"/> does. A bought upgrade must never be written into the asset:
/// the asset is one shared instance, so a bonus stored there would survive quitting the
/// game and would compound on every restart. The thrower resolves the asset plus the
/// upgrades into one of these on the way out, and everything downstream reads only this.
/// </summary>
public struct BlastSpec
{
    public float damage;
    public float radius;
    public float edgeDamageFraction;
    public float falloffPower;
    public float selfDamageFraction;
    public float explosionForce;

    /// <summary>
    /// Damage at a distance from the centre, before any per-target fraction.
    ///
    /// Curved rather than linear so accuracy is worth something: at falloffPower 1.6 a
    /// target halfway out still takes about two thirds, and one on the rim takes the
    /// edge fraction. Outside the radius it is exactly zero, which is the contract the
    /// aiming ring draws on the ground.
    /// </summary>
    public float DamageAtDistance(float distance)
    {
        if (distance >= radius) return 0f;
        if (distance <= 0f) return damage;

        float t = Mathf.Clamp01(distance / Mathf.Max(0.01f, radius));
        float falloff = Mathf.Lerp(1f, edgeDamageFraction, Mathf.Pow(t, Mathf.Max(0.2f, falloffPower)));

        return damage * falloff;
    }
}

/// <summary>
/// One throwable explosive, as data rather than as a prefab.
///
/// The same extension point as <see cref="WeaponData"/> and
/// <see cref="EnemyArchetype"/>: a second bomb is a duplicated asset with different
/// numbers, dropped into the <see cref="StoreCatalog"/>. Nothing about
/// <see cref="BombThrower"/> or <see cref="BombProjectile"/> has to change for it.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Bomb Data", fileName = "NewBomb")]
public class BombData : ScriptableObject
{
    [Header("Identity")]
    public string bombName = "Frag Bomb";

    [TextArea(2, 3)]
    public string description;

    [Header("Blast")]
    [Tooltip("Damage at the centre of the blast. Everything inside the radius takes " +
             "some fraction of this, falling off with distance.")]
    [Min(1f)] public float damage = 120f;

    [Tooltip("Metres the blast reaches. Nothing outside this takes anything at all, " +
             "which is what makes the ring on the ground an honest promise.")]
    [Min(0.5f)] public float radius = 7f;

    [Tooltip("What a target standing right on the edge of the radius takes, as a " +
             "fraction of the full damage. Above zero on purpose: a blast that does " +
             "nothing at all one metre from its edge reads as a bug rather than as " +
             "falloff.")]
    [Range(0f, 1f)] public float edgeDamageFraction = 0.25f;

    [Tooltip("How sharply the damage falls off. 1 is linear; above 1 keeps more of the " +
             "damage near the centre, which is what rewards landing it accurately.")]
    [Min(0.2f)] public float falloffPower = 1.6f;

    [Tooltip("What the player takes from their own bomb, as a fraction of what an enemy " +
             "at the same distance would. Not zero: a bomb you can drop on your own feet " +
             "for free is a bomb with no aiming in it.")]
    [Range(0f, 1f)] public float selfDamageFraction = 0.35f;

    [Tooltip("Impulse applied to anything with a Rigidbody in the blast. Ragdolls are " +
             "most of what a player sees of a bomb landing well.")]
    [Min(0f)] public float explosionForce = 900f;

    [Header("Flight")]
    [Tooltip("Seconds a throw at maximum range takes to arrive. One second is " +
             "deliberate: long enough to watch it arc, short enough that the ring you " +
             "aimed at is still the fight you aimed it at.")]
    [Range(0.2f, 4f)] public float fallTime = 1f;

    [Tooltip("Seconds a throw at minimum range takes. Shorter, because the flight time " +
             "scales with the distance -- see FlightTimeFor. A bomb lobbed six metres " +
             "and a bomb lobbed thirty-four both hanging in the air for a full second " +
             "is what made the short throw, which is the panic throw, feel broken.")]
    [Range(0.15f, 4f)] public float minFallTime = 0.42f;

    [Tooltip("Closest the landing ring can be placed, in metres. Inside this you are " +
             "inside your own blast.")]
    [Min(1f)] public float minRange = 6f;

    [Tooltip("Furthest the landing ring can be placed. The arc is solved to reach " +
             "exactly the locked point in exactly the fall time, so a long throw is a " +
             "high one rather than a fast one.")]
    [Min(2f)] public float maxRange = 34f;

    [Tooltip("How much harder gravity pulls on a bomb than on everything else, which is " +
             "what decides the shape of the throw.\n\n" +
             "The flight time is fixed and so is the distance, so the *horizontal* speed " +
             "is not a choice -- thirty metres in one second is thirty metres a second " +
             "whatever this is set to. What it buys is height. Under real gravity a " +
             "one-second throw peaks about a metre up, which is a flat rocket that meets " +
             "the first crate between here and the target; at 5.5 it peaks near seven " +
             "metres and lobs over cover the way a thrown explosive should.")]
    [Range(1f, 14f)] public float gravityScale = 5.5f;

    [Header("Supply")]
    [Tooltip("Bombs you start each level with. They come back every level rather than " +
             "being consumed forever -- a bomb you are afraid to spend is a bomb that " +
             "never gets used.")]
    [Min(1)] public int chargesPerLevel = 2;

    [Tooltip("Seconds between throws, so two charges cannot be spent on one frame.")]
    [Min(0f)] public float throwCooldown = 0.8f;

    [Header("Feel")]
    [Range(0f, 1f)] public float cameraShake = 0.75f;

    [Tooltip("Seconds the blast sphere takes to expand and fade. Not the damage window " +
             "-- damage is applied once, on the frame it goes off.")]
    [Min(0.05f)] public float blastVisualDuration = 0.45f;

    public Color blastColor = new Color(1f, 0.55f, 0.15f);

    [Header("Prefabs (written by the scene builder)")]
    public GameObject bombPrefab;
    public GameObject explosionPrefab;

    [Header("Audio (optional)")]
    [Tooltip("The pin, played the moment the ring comes up. Holding the bomb key was " +
             "the one control in the game that made no sound at all, and a key that " +
             "makes no sound is a key the player decides is broken -- the ring is on " +
             "the floor, and somebody looking down the sights never sees it.")]
    public AudioClip armClip;

    public AudioClip throwClip;
    public AudioClip explodeClip;

    /// <summary>
    /// Seconds a throw of this length spends in the air.
    ///
    /// Scaled rather than fixed. The flight is solved to arrive in exactly this time,
    /// so a constant made every throw take the same second whether it was six metres or
    /// thirty-four -- and six metres in one second is not a throw, it is a bomb placed
    /// gently on the floor while the enemy who prompted it walks away. The far end is
    /// unchanged, because that is the one the arc height was tuned against.
    /// </summary>
    public float FlightTimeFor(float distance)
    {
        float far = Mathf.Max(0.2f, fallTime);
        float near = Mathf.Clamp(minFallTime, 0.15f, far);

        float t = Mathf.InverseLerp(minRange, Mathf.Max(minRange + 0.01f, maxRange), distance);

        return Mathf.Lerp(near, far, t);
    }

    /// <summary>
    /// The asset's numbers with a set of bought upgrades folded in. The only way
    /// anything downstream should ever learn how hard this bomb hits.
    /// </summary>
    public BlastSpec Resolve(float damageMultiplier = 1f, float radiusBonus = 0f)
        => new BlastSpec
        {
            damage = damage * Mathf.Max(0.01f, damageMultiplier),
            radius = Mathf.Max(0.5f, radius + radiusBonus),
            edgeDamageFraction = edgeDamageFraction,
            falloffPower = falloffPower,
            selfDamageFraction = selfDamageFraction,
            explosionForce = explosionForce
        };

}
