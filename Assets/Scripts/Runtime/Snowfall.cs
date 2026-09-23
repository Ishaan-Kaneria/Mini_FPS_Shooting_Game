using UnityEngine;

/// <summary>
/// Snow falling everywhere, which is to say snow falling wherever the player is.
///
/// <b>It follows the player rather than covering the arena.</b> A four-hundred-metre
/// field of particles is hundreds of thousands of them to put a few hundred anywhere the
/// eye can resolve one, and every one outside the view costs the same as one inside it.
/// A box a few tens of metres across, kept above the player and simulated in world space,
/// is indistinguishable from weather and costs what it looks like. The world-space
/// simulation is the part that sells it: parented motion would drag the whole snowfall
/// sideways every time the player walked, which reads as the sky moving.
///
/// <b>The system is configured here rather than authored as a prefab</b>, for the reason
/// the objective markers are built from primitives: a generated scene cannot carry an
/// asset the builder does not make, and a prefab would have to exist in every arena that
/// ever wanted weather. The one thing it cannot make for itself is the material, because
/// a shader found by name at runtime is a shader that may not be in the build -- so the
/// builder makes that and hands it over.
/// </summary>
[DisallowMultipleComponent]
public class Snowfall : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("What the weather follows. Left empty it finds the Player tag, which is " +
             "what lets the builder add this before the player rig exists.")]
    public Transform follow;

    [Tooltip("The flake. Made by the builder, because a shader found by name at runtime " +
             "is one that may have been stripped from the build.")]
    public Material flakeMaterial;

    [Header("Weather")]
    [Tooltip("How far across the box of falling snow is. Wider costs more and shows less " +
             "-- past about forty metres the flakes are under a pixel.")]
    [Min(4f)] public float spread = 42f;

    [Tooltip("How high above the player it starts. This is also how long a flake has to " +
             "fall before the player can see it, so too low reads as snow appearing out " +
             "of nothing a few metres up.")]
    [Min(2f)] public float ceiling = 22f;

    [Min(1)] public int flakes = 1400;

    [Tooltip("Metres per second downward. Real snow falls at about 1, which looks like " +
             "the game has stopped; this is the honest lie every game tells.")]
    public float fallSpeed = 3.2f;

    [Tooltip("The wind, in metres per second. A vertical fall reads as rain.")]
    public Vector3 wind = new Vector3(1.6f, 0f, 0.7f);

    [Header("Look")]
    [Tooltip("The colour of a flake. With a second colour set, each one is picked between " +
             "the two -- which is how the same weather is ash in one arena and snow in " +
             "another.")]
    public Color tint = new Color(1f, 1f, 1f, 0.85f);

    [Tooltip("The other end of the range. Fully transparent means every flake is tint.")]
    public Color tintAlt = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Smallest and largest flake, in metres.")]
    public Vector2 size = new Vector2(0.06f, 0.16f);

    ParticleSystem _system;

    void Start()
    {
        if (follow == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) follow = player.transform;
        }

        Build();
    }

    void LateUpdate()
    {
        if (follow == null) return;

        // Kept over the player's head, and deliberately not rotated with them: the box is
        // symmetrical, so turning it would do nothing except make every flake in world
        // space jump.
        transform.position = follow.position + Vector3.up * ceiling;
    }

    void Build()
    {
        _system = GetComponent<ParticleSystem>();
        if (_system == null) _system = gameObject.AddComponent<ParticleSystem>();

        var main = _system.main;
        main.loop = true;
        main.playOnAwake = true;

        // Long enough to fall the whole way down and a little past, so flakes are not
        // seen vanishing at knee height.
        // Measured on the speed rather than the direction: a negative fall is a rise --
        // embers going up -- and dividing by it would give every one a negative life.
        main.startLifetime = (ceiling + 6f) / Mathf.Max(0.1f, Mathf.Abs(fallSpeed));
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = tintAlt.a > 0f
            ? new ParticleSystem.MinMaxGradient(tint, tintAlt)
            : new ParticleSystem.MinMaxGradient(tint);
        main.maxParticles = Mathf.Max(64, flakes);

        // The whole point. In Local, walking drags the weather along with the player.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = _system.emission;
        emission.rateOverTime = flakes / Mathf.Max(0.1f, main.startLifetime.constant);

        var shape = _system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spread, 0.5f, spread);

        var velocity = _system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(wind.x * 0.6f, wind.x * 1.4f);
        velocity.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.25f, -fallSpeed * 0.75f);
        velocity.z = new ParticleSystem.MinMaxCurve(wind.z * 0.6f, wind.z * 1.4f);

        // A flake does not fall straight. Without this the field reads as a grid of dots
        // sliding down the screen, which is the single thing that gives away particles.
        var noise = _system.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.3f;
        noise.damping = true;

        var renderer = GetComponent<ParticleSystemRenderer>();

        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;

            if (flakeMaterial != null) renderer.sharedMaterial = flakeMaterial;

            // Snow in front of the player is not the thing being aimed at, and a flake
            // that writes shadows is a flake that costs a shadow map entry each.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        _system.Play();
    }
}
