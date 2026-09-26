using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What being shot does to how an enemy looks: the spray from each hit, a wound left on
/// the body where the round went in, the limb darkening as it is shot up, a trail of
/// drops behind something badly hurt, a smear behind something crawling, and the pool
/// spreading under the body once it is down.
///
/// Every piece reads the hit's <see cref="BodyPart"/> and its share of the enemy's
/// health, so a graze and a slug do not look the same and a leg that has taken four
/// rounds looks like it. The level-wide pieces (spray, splats, pools) are
/// <see cref="BloodFX"/>'s; this owns only what is on the body.
///
/// The Blood setting switches the blood off, not the wounds: with it off a hit still
/// throws a dull puff, and a shot leg still limps.
/// </summary>
[DisallowMultipleComponent]
public class EnemyGore : MonoBehaviour
{
    public BloodLibrary library;

    [Header("Wounds")]
    [Tooltip("Wounds kept on one body. Past this the oldest one moves to the newest hit.")]
    [Min(0)] public int maxWounds = 14;

    [Tooltip("Size of a wound on the body, in metres, from a graze to a heavy hit.")]
    public Vector2 woundSize = new Vector2(0.07f, 0.15f);

    [Header("Stains")]
    [Tooltip("How far a shot-up limb darkens toward the library's stain colour, at worst.")]
    [Range(0f, 1f)] public float maxStain = 0.6f;

    [Header("Trail")]
    [Tooltip("How hurt a limb, or the whole body, has to be before it leaves a trail.")]
    [Range(0f, 1f)] public float bleedingAt = 0.45f;

    [Tooltip("Metres walked between drops on the floor behind it.")]
    [Min(0.2f)] public float dripSpacing = 1.3f;

    [Header("Pool")]
    [Tooltip("Seconds after it falls before the pool starts, so it spreads from where the " +
             "body came to rest rather than from where it was standing.")]
    [Min(0f)] public float poolDelay = 1.1f;

    [Tooltip("Width of the pool under a body of ordinary size, in metres.")]
    [Min(0.1f)] public float poolSize = 1.3f;

    [Tooltip("Seconds the pool takes to spread to its full size.")]
    [Min(0.1f)] public float poolSpread = 6f;

    Health _health;
    EnemyAI _ai;
    EnemyWounds _wounds;
    Hitbox[] _hitboxes;
    Collider[] _hitColliders;
    List<Transform> _woundMarks = new List<Transform>();
    int _nextWound;
    Vector3 _lastDrip;

    void Awake()
    {
        _health = GetComponent<Health>();
        _ai = GetComponent<EnemyAI>();
        _wounds = GetComponent<EnemyWounds>();

        _hitboxes = GetComponentsInChildren<Hitbox>(true);
        _hitColliders = new Collider[_hitboxes.Length];
        for (int i = 0; i < _hitboxes.Length; i++) _hitColliders[i] = _hitboxes[i].GetComponent<Collider>();

        _lastDrip = transform.position;
    }

    void OnEnable()
    {
        if (_health == null) return;

        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
    }

    void OnDisable()
    {
        if (_health == null) return;

        _health.Damaged -= OnDamaged;
        _health.Died -= OnDied;
    }

    void Update()
    {
        if (_health == null || _health.IsDead || !BloodFX.Enabled) return;

        // The trail. Measured by distance, not time, so a limping enemy that stops to
        // shoot does not grow a puddle, and a crawler leaves a line rather than dots.
        Vector3 here = transform.position;
        Vector3 moved = here - _lastDrip;
        moved.y = 0f;

        bool crawling = _ai != null && _ai.Crawling;
        float spacing = crawling ? dripSpacing * 0.45f : dripSpacing;

        if (moved.sqrMagnitude < spacing * spacing) return;

        _lastDrip = here;

        if (!Bleeding) return;

        if (crawling) BloodFX.Smear(here, moved, Random.Range(0.35f, 0.55f) * Size);
        else BloodFX.Drip(here + Vector3.up * 0.9f * Size + Random.insideUnitSphere * 0.2f,
                          Random.Range(0.12f, 0.22f) * Size);
    }

    /// <summary>Hurt enough to leave a trail: one limb badly, or the body as a whole.</summary>
    bool Bleeding
    {
        get
        {
            if (_health.Normalized < 1f - bleedingAt) return true;
            if (_wounds == null) return false;

            return _wounds.Severity(BodyPart.LeftLeg) >= bleedingAt
                || _wounds.Severity(BodyPart.RightLeg) >= bleedingAt
                || _wounds.Severity(BodyPart.LeftArm) >= bleedingAt
                || _wounds.Severity(BodyPart.RightArm) >= bleedingAt;
        }
    }

    /// <summary>The archetype's scale, so a boss bleeds like the size it is.</summary>
    float Size => Mathf.Max(0.4f, transform.lossyScale.y);

    void OnDamaged(Health health, DamageInfo info)
    {
        if (info.amount <= 0f || library == null) return;

        // Measured against a third of the pool: three hits that each take a third are
        // three hits worth the full spray.
        float severity = Mathf.Clamp01(info.amount / Mathf.Max(1f, health.maxHealth * 0.33f));
        if (info.isHeadshot) severity = Mathf.Max(severity, 0.85f);

        if (info.fromBlast)
        {
            // Everywhere at once: a spray from a few parts, thrown away from the blast.
            for (int i = 0; i < 3 && _hitColliders.Length > 0; i++)
            {
                var part = _hitColliders[Random.Range(0, _hitColliders.Length)];
                if (part == null) continue;

                BloodFX.Hit(library, part.bounds.center, info.direction, info.direction, severity);
            }

            return;
        }

        BloodFX.Hit(library, info.point, info.normal, info.direction, info.fromMelee ? severity * 0.4f : severity);

        if (!BloodFX.Enabled) return;

        // A fist bruises; it does not leave a hole.
        if (!info.fromMelee) AddWound(info, severity);

        Stain(info.part);
    }

    void OnDied(Health health)
    {
        if (library == null || !isActiveAndEnabled) return;
        StartCoroutine(PoolWhenSettled());
    }

    IEnumerator PoolWhenSettled()
    {
        yield return new WaitForSeconds(poolDelay);

        // From the torso, wherever the ragdoll has put it by now.
        Vector3 at = transform.position;

        for (int i = 0; i < _hitboxes.Length; i++)
        {
            if (_hitboxes[i] == null || _hitboxes[i].Part != BodyPart.Torso || _hitColliders[i] == null) continue;

            at = _hitColliders[i].bounds.center;
            break;
        }

        // Faster and wider from a body that took a lot at once.
        BloodFX.Pool(library, at, poolSize * Size * Random.Range(0.85f, 1.15f),
                     poolSpread * Random.Range(0.8f, 1.2f));
    }

    // ======================================================================

    /// <summary>
    /// A wound where the round went in: a dark hole with a torn red rim, stuck to the
    /// limb so it moves with it and falls with the body.
    /// </summary>
    void AddWound(DamageInfo info, float severity)
    {
        if (maxWounds <= 0 || library.woundMaterial == null) return;

        var collider = NearestPart(info.point);
        if (collider == null) return;

        // Stuck to the nearest ancestor with an even scale, not to the collider: the
        // limbs of the built enemy are scaled capsules, and a square parented to one
        // would be stretched into a stripe.
        var anchor = collider.transform;
        while (anchor.parent != null && !Even(anchor.localScale)) anchor = anchor.parent;

        Transform mark;

        if (_woundMarks.Count < maxWounds)
        {
            var go = new GameObject("Wound");
            go.layer = 2;
            go.AddComponent<MeshFilter>().sharedMesh = WoundMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = library.woundMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mark = go.transform;
            _woundMarks.Add(mark);
        }
        else
        {
            _nextWound %= _woundMarks.Count;
            mark = _woundMarks[_nextWound++];
            if (mark == null) return;
        }

        Vector3 normal = info.normal.sqrMagnitude > 0.0001f ? info.normal.normalized : -info.direction.normalized;
        Vector3 surface = collider.ClosestPoint(info.point + normal * 0.2f);

        mark.SetParent(anchor, true);
        mark.SetPositionAndRotation(surface + normal * 0.006f,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized + normal * 0.001f,
                                    normal));

        float size = Mathf.Lerp(woundSize.x, woundSize.y, severity);
        float parent = Mathf.Max(0.001f, anchor.lossyScale.x);
        mark.localScale = Vector3.one * (size / parent);
    }

    Collider NearestPart(Vector3 point)
    {
        Collider best = null;
        float bestDistance = float.MaxValue;

        foreach (var collider in _hitColliders)
        {
            if (collider == null || !collider.enabled) continue;

            float distance = (collider.ClosestPoint(point) - point).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = collider;
        }

        return best;
    }

    /// <summary>Darkens every renderer of the part that was hit, by how hurt that part is.</summary>
    void Stain(BodyPart part)
    {
        if (_ai == null || library == null || maxStain <= 0f) return;

        float severity = _wounds != null ? _wounds.Severity(part) : 0.3f;

        for (int i = 0; i < _hitboxes.Length; i++)
        {
            if (_hitboxes[i] == null || _hitboxes[i].Part != part) continue;

            var renderer = _hitboxes[i].GetComponent<Renderer>();

            // One skinned mesh is the whole body; staining it would paint all of it.
            if (renderer == null || renderer is SkinnedMeshRenderer) continue;

            _ai.Stain(renderer, library.stainColor, severity * maxStain);
        }
    }

    static bool Even(Vector3 scale)
        => Mathf.Abs(scale.x - scale.y) < 0.01f * Mathf.Abs(scale.x) + 0.0001f
        && Mathf.Abs(scale.x - scale.z) < 0.01f * Mathf.Abs(scale.x) + 0.0001f;

    static Mesh s_woundMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => s_woundMesh = null;

    /// <summary>One quad for every wound in the level, built the first time one is needed.</summary>
    static Mesh WoundMesh
    {
        get
        {
            if (s_woundMesh == null) s_woundMesh = BloodFX.Quad(Vector2.zero, 1f);
            return s_woundMesh;
        }
    }
}
