using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A blast: the sphere that flashes out, the light that goes with it, and the static
/// method that works out who it hurt.
///
/// The damage and the picture are deliberately separate. <see cref="Blast"/> is a static
/// that needs no prefab and applies its damage on the single frame it is called -- so a
/// bomb going off is one event, not a trigger volume that keeps hurting whatever walks
/// into it. This component is only the thing you see, and a scene with no explosion
/// prefab still does the damage.
///
/// Everything runs on unscaled time so a blast that lands on the frame a level is
/// scored still plays out rather than freezing mid-expansion.
///
/// It ends by shrinking rather than fading, for the same reason
/// <see cref="TransientFlash"/> does: fading needs a transparent material, the kit has
/// none, and the unlit one these spheres use ignores the alpha channel entirely. Written
/// as a fade it *looked* written -- the colour was set every frame and the alpha went to
/// zero -- and what actually happened on screen was a black sphere sitting at full size
/// until the object was destroyed underneath it.
/// </summary>
[DisallowMultipleComponent]
public class Explosion : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("The sphere that expands. Scaled to twice the blast radius, so what you " +
             "see is exactly what was damaged.")]
    public Transform ball;

    [Tooltip("Optional second sphere, expanding slower and further, that reads as the " +
             "smoke left behind.")]
    public Transform smoke;

    public Light flash;

    [Header("Timing")]
    [Min(0.05f)] public float duration = 0.45f;

    [Tooltip("Fraction of the duration the light lasts. Short: a flash that fades over " +
             "half a second reads as a lamp rather than as a detonation.")]
    [Range(0.05f, 1f)] public float flashFraction = 0.28f;

    [Tooltip("How far past the damage radius the smoke puffs out.")]
    [Min(1f)] public float smokeScale = 1.35f;

    [Header("Look")]
    public Color color = new Color(1f, 0.55f, 0.15f);

    [Min(0f)] public float lightIntensity = 40f;
    [Min(0f)] public float lightRange = 18f;

    float _age;
    float _radius = 3f;

    MaterialPropertyBlock _block;
    Renderer _ballRenderer;
    Renderer _smokeRenderer;

    /// <summary>
    /// The property block, made on demand.
    ///
    /// A MaterialPropertyBlock is not serializable, so Unity's mid-play reload backup
    /// drops it while the Renderer references beside it survive -- and a method guarded
    /// on the renderers then hands a null block to GetPropertyBlock, which throws
    /// ArgumentNullException once per frame for the rest of the session. See the sibling
    /// trap in CLAUDE.md; EnemyAI.Block and EnemyHealthBar.Block are the same pattern.
    /// </summary>
    MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

    /// <summary>
    /// Sizes and starts the blast. Called instead of just enabling the object, because
    /// the radius is the bomb's and not the prefab's.
    /// </summary>
    public void Play(float radius, Color tint, float seconds)
    {
        _radius = Mathf.Max(0.1f, radius);
        color = tint;
        duration = Mathf.Max(0.05f, seconds);
        _age = 0f;

        if (flash != null)
        {
            flash.color = color;
            flash.range = Mathf.Max(lightRange, _radius * 2.2f);
            flash.intensity = lightIntensity;
            flash.enabled = true;
        }

        Apply(0f);
    }

    void OnEnable()
    {
        _ballRenderer = ball != null ? ball.GetComponent<Renderer>() : null;
        _smokeRenderer = smoke != null ? smoke.GetComponent<Renderer>() : null;

        _age = 0f;
    }

    void Update()
    {
        // Unscaled: a bomb that lands on the frame the level is scored freezes the game,
        // and a blast frozen a third of the way through its expansion looks like a bug.
        _age += Time.unscaledDeltaTime;

        float t = Mathf.Clamp01(_age / duration);
        Apply(t);

        if (t >= 1f) Destroy(gameObject);
    }

    /// <summary>
    /// Draws the blast at a point in its life. The ball snaps out and holds; the smoke
    /// follows it out more slowly and keeps going, which is the shape of a real one.
    /// </summary>
    void Apply(float t)
    {
        float diameter = _radius * 2f;

        if (ball != null)
        {
            // Out hard and back in. The expansion is almost over in the first fifth,
            // which is what an explosion looks like; the collapse over the back half is
            // what a fade would have done if a fade were available.
            float grow = 1f - Mathf.Pow(1f - t, 4f);
            float collapse = 1f - Mathf.Clamp01((t - 0.35f) / 0.65f);

            ball.localScale = Vector3.one * (diameter * grow * collapse);

            Tint(_ballRenderer, Color.Lerp(Color.white, color, Mathf.Clamp01(t * 2.2f)),
                 Mathf.Clamp01(1f - t * 1.5f));
        }

        if (smoke != null)
        {
            // Slower out, and it only starts pulling in at the very end -- so the smoke
            // is still standing when the fireball inside it has gone, which is the order
            // a real one happens in.
            float grow = Mathf.Sqrt(t);
            float collapse = 1f - Mathf.Clamp01((t - 0.75f) / 0.25f);

            smoke.localScale = Vector3.one * (diameter * smokeScale * grow * collapse);

            Tint(_smokeRenderer, Color.Lerp(color * 0.35f, new Color(0.12f, 0.12f, 0.13f), t),
                 Mathf.Clamp01(0.85f - t));
        }

        if (flash == null) return;

        float flashLife = Mathf.Clamp01(t / Mathf.Max(0.01f, flashFraction));
        flash.intensity = lightIntensity * (1f - flashLife) * (1f - flashLife);
        if (flashLife >= 1f) flash.enabled = false;
    }

    /// <summary>
    /// Colours one sphere through a property block rather than through its material.
    /// Touching renderer.material would instantiate a copy of the shared material per
    /// blast, and a bomb that leaks a material per throw is a bomb that leaks.
    /// </summary>
    void Tint(Renderer target, Color tint, float strength)
    {
        if (target == null) return;

        var block = Block;
        target.GetPropertyBlock(block);

        block.SetColor("_BaseColor", tint);
        block.SetColor("_Color", tint);
        block.SetColor("_EmissionColor", tint * Mathf.Max(0f, strength) * 6f);

        target.SetPropertyBlock(block);
    }

    // ======================================================================

    /// <summary>
    /// Hurts everything inside a radius, falling off with distance, and returns how many
    /// things it hit.
    ///
    /// Static and prefab-free so the damage is not a property of the effect: a bomb with
    /// no explosion prefab wired still goes off, which is the same rule every audio and
    /// VFX field in the kit follows.
    ///
    /// One <see cref="Health"/> is damaged at most once however many colliders it has.
    /// An enemy is a body, a head and a pair of limbs, and an OverlapSphere returns all
    /// of them -- damaging per collider would make a bomb four times stronger against a
    /// target with hitboxes than against one without, which is not a rule anybody could
    /// guess from looking at it.
    /// </summary>
    public static int Blast(Vector3 centre, BlastSpec spec, GameObject attacker,
                            LayerMask mask, GameObject selfRoot = null)
    {
        if (spec.radius <= 0f || spec.damage <= 0f) return 0;

        var hits = Physics.OverlapSphere(centre, spec.radius, mask, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) return 0;

        var damaged = new HashSet<Health>();
        var pushed = new HashSet<Rigidbody>();
        int count = 0;

        foreach (var collider in hits)
        {
            if (collider == null) continue;

            var health = collider.GetComponentInParent<Health>();

            if (health != null && !health.IsDead && damaged.Add(health))
            {
                // Measured to the body, not to whichever collider the sphere happened to
                // return first: a target's head and its feet are a metre and a half apart,
                // and a blast should not be worth more against a tall enemy.
                float distance = Vector3.Distance(centre, health.transform.position);
                float amount = spec.DamageAtDistance(distance);

                bool isSelf = selfRoot != null && health.gameObject == selfRoot;
                if (isSelf) amount *= spec.selfDamageFraction;

                if (amount > 0f)
                {
                    Vector3 away = health.transform.position - centre;
                    if (away.sqrMagnitude < 0.0001f) away = Vector3.up;

                    var info = new DamageInfo(amount, centre + away.normalized * distance,
                                              away.normalized, away.normalized, attacker);

                    if (health.ApplyDamage(info) > 0f) count++;
                }
            }

            var body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic && pushed.Add(body))
                body.AddExplosionForce(spec.explosionForce, centre, spec.radius, 0.7f, ForceMode.Impulse);
        }

        return count;
    }
}
