using UnityEngine;

/// <summary>
/// The bomb in flight, from leaving your hand to going off.
///
/// It flies its own arc rather than being a Rigidbody, and that is the whole design:
/// the player was shown a ring on the ground and told the bomb would land in it, so the
/// bomb has to land in it. A rigidbody clipping the corner of a crate on the way over
/// would break that promise silently and blame the physics engine. The path here is a
/// plain parabola solved for the exact landing point and the exact flight time, so
/// "one second, there" is arithmetic rather than a hope.
///
/// It still collides: the position is swept between frames, and hitting something early
/// detonates it early. What it cannot do is *drift*.
///
/// Like <see cref="TracerProjectile"/>, it carries everything it needs and outlives
/// whoever threw it -- a player who dies half a second after throwing still gets the
/// blast.
/// </summary>
[DisallowMultipleComponent]
public class BombProjectile : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Degrees per second the body tumbles on its way over. Cosmetic.")]
    public Vector3 spin = new Vector3(520f, 180f, 260f);

    [Tooltip("Optional light that pulses faster as the fuse runs down, so a bomb in the " +
             "air reads as about to go off rather than as debris.")]
    public Light fuseLight;

    [Min(0f)] public float fuseLightIntensity = 6f;

    [Tooltip("Radius the sweep uses to notice geometry. Roughly the bomb's own size.")]
    [Min(0.01f)] public float sweepRadius = 0.18f;

    // ---- flight, all plain values so a mid-play domain reload cannot half-survive it
    BombData _data;
    BlastSpec _spec;
    GameObject _attacker;
    GameObject _selfRoot;
    LayerMask _damageMask;
    LayerMask _collisionMask;

    Vector3 _origin;
    Vector3 _velocity;
    Vector3 _target;
    float _flightTime = 1f;
    float _gravityScale = 1f;
    float _age;
    bool _armed;
    bool _detonated;

    /// <summary>
    /// Throws the bomb at a point, to arrive there in exactly the data's fall time.
    ///
    /// The velocity is the closed-form solution for a projectile that must be at
    /// <paramref name="target"/> after <c>t</c> seconds under constant gravity:
    ///
    ///   p(t) = p0 + v*t + 0.5*g*t^2   =>   v = (target - p0)/t - 0.5*g*t
    ///
    /// which is why the arc gets higher rather than faster as the throw gets longer.
    /// The aiming indicator draws the same equation, so the dotted line the player sees
    /// is the path, not an approximation of it.
    ///
    /// Gravity is the bomb's own, several times the world's. That is what makes this a
    /// lob rather than a rocket: the horizontal speed is fixed by the distance and the
    /// time, so the only thing left to choose is how high it goes, and under real
    /// gravity a one-second throw peaks about a metre up and flies flat into the first
    /// crate in the way. See BombData.gravityScale.
    /// </summary>
    public void Launch(Vector3 target, BombData data, BlastSpec spec, GameObject attacker,
                       LayerMask damageMask, LayerMask collisionMask, GameObject selfRoot = null)
    {
        _data = data;
        _spec = spec;
        _attacker = attacker;
        _selfRoot = selfRoot;
        _damageMask = damageMask;
        _collisionMask = collisionMask;

        _target = target;
        _flightTime = data != null ? Mathf.Max(0.05f, data.fallTime) : 1f;
        _gravityScale = data != null ? Mathf.Max(0.1f, data.gravityScale) : 1f;
        _origin = transform.position;
        _velocity = SolveVelocity(_origin, target, _flightTime, _gravityScale);

        _age = 0f;
        _armed = true;
        _detonated = false;

        if (fuseLight != null)
        {
            fuseLight.enabled = true;
            fuseLight.color = data != null ? data.blastColor : Color.red;
        }
    }

    /// <summary>The acceleration a bomb falls under. Several times the world's.</summary>
    public static Vector3 GravityFor(float scale) => Physics.gravity * Mathf.Max(0.1f, scale);

    /// <summary>The launch velocity that puts a projectile at <c>target</c> after <c>t</c>.</summary>
    public static Vector3 SolveVelocity(Vector3 from, Vector3 target, float t, float gravityScale)
    {
        t = Mathf.Max(0.05f, t);
        return (target - from) / t - 0.5f * GravityFor(gravityScale) * t;
    }

    /// <summary>
    /// The arc, sampled for drawing. Shared with the aiming indicator so there is one
    /// equation and the dotted line cannot disagree with where the bomb actually goes.
    /// </summary>
    public static Vector3 PointOnArc(Vector3 from, Vector3 velocity, float t, float gravityScale)
        => from + velocity * t + 0.5f * GravityFor(gravityScale) * t * t;

    void Update()
    {
        if (!_armed || _detonated) return;

        float step = Time.deltaTime;
        _age += step;

        Vector3 previous = transform.position;
        Vector3 next = PointOnArc(_origin, _velocity, _age, _gravityScale);

        // Swept, not teleported. At a long throw the bomb covers several metres a frame,
        // and a position check alone would pass straight through a wall between frames.
        Vector3 delta = next - previous;
        float distance = delta.magnitude;

        if (distance > 0.0001f &&
            Physics.SphereCast(previous, sweepRadius, delta / distance, out RaycastHit hit,
                               distance, _collisionMask, QueryTriggerInteraction.Ignore))
        {
            // Backed off the surface so the blast sphere is not born inside the wall.
            Detonate(hit.point + hit.normal * sweepRadius);
            return;
        }

        transform.position = next;
        transform.Rotate(spin * step, Space.Self);

        if (fuseLight != null && fuseLightIntensity > 0f)
        {
            // Blinks faster as the fuse runs down: 4 Hz at the start, 20 by the end.
            float urgency = Mathf.Clamp01(_age / _flightTime);
            float rate = Mathf.Lerp(4f, 20f, urgency);
            fuseLight.intensity = fuseLightIntensity * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_age * rate)));
        }

        if (_age >= _flightTime) Detonate(_target);
    }

    /// <summary>
    /// Goes off. Idempotent, because the sweep and the clock can both come due on the
    /// same frame and a bomb that detonated twice would do double damage for free.
    /// </summary>
    public void Detonate(Vector3 at)
    {
        if (_detonated) return;
        _detonated = true;
        _armed = false;

        transform.position = at;

        if (_data != null)
        {
            Explosion.Blast(at, _spec, _attacker, _damageMask, _selfRoot);

            if (_data.explosionPrefab != null)
            {
                var effect = Instantiate(_data.explosionPrefab, at, Quaternion.identity);

                var blast = effect.GetComponent<Explosion>();

                // Sized from the resolved radius, not the asset's: an upgraded bomb has
                // to look as big as it hits, or the ring the player aimed with is the
                // only honest thing on screen.
                if (blast != null) blast.Play(_spec.radius, _data.blastColor, _data.blastVisualDuration);
                else Destroy(effect, 3f);
            }

            // Full volume, and held at full volume out to the blast radius before it
            // starts rolling off. The pool's default is a metre and a half, which is
            // right for a bullet hitting a wall and makes a detonation covering seven
            // metres of arena sound like it happened next door.
            OneShotAudio.Play(_data.explodeClip, at, 1f,
                              UnityEngine.Random.Range(0.94f, 1.06f),
                              minDistance: Mathf.Max(8f, _spec.radius * 1.5f));

            ShakeNearbyCamera(at);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Shakes the player's view, harder the closer they are.
    ///
    /// Found rather than passed in, because the bomb outlives whoever threw it: a player
    /// who died in the last second of the fuse has had their motor destroyed, and a
    /// cached reference to it would be a MissingReferenceException on the frame the blast
    /// is supposed to be the most satisfying thing on screen.
    /// </summary>
    void ShakeNearbyCamera(Vector3 at)
    {
        if (_data == null || _data.cameraShake <= 0f) return;

        var motor = FindAnyObjectByType<PlayerMotor>();
        if (motor == null) return;

        float distance = Vector3.Distance(motor.transform.position, at);
        float reach = _spec.radius * 3f;
        if (distance > reach) return;

        motor.AddShake(_data.cameraShake * (1f - distance / reach));
    }
}
