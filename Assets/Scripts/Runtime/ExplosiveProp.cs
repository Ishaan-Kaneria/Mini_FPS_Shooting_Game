using UnityEngine;

/// <summary>
/// Something in the world that burns or blows up when it is shot, struck by a blast or set alight: a petrol drum, a
/// diesel drum, a jerry can, a gas cylinder, a propane tank, a fuel pump (Docs/DESERT_EXPLOSIVES.md).
///
/// Shot through the prop's own <see cref="Health"/> (a round finds it as it finds any body: Weapon.FireRay, EnemyAI, the
/// technical's gun), so enemies' rounds set it off too. What follows is by kind, as the real thing does it:
/// * **petrol drum**: a hole leaks; a spark lights it (a chance on each round, certain once its health is gone); it burns
///   for a few seconds and then bursts, a fireball and a blast, and leaves a pool of burning fuel;
/// * **diesel drum**: hard to light (diesel does not take a spark); once lit it burns, and only bursts after long heating;
/// * **jerry can**: lights at once and goes up in a small fireball;
/// * **gas cylinder / propane tank**: a round vents it (a hiss, then a jet of flame), and it bursts after a few seconds of
///   heating; a blast close by bursts it at once. The biggest blast;
/// * **fuel pump**: never explodes (a pump's valves shut); shot through, it sprays burning fuel for a while.
/// A blast that reaches another explosive sets it off after a short delay: the chain.
///
/// The blast is <see cref="Explosion.Blast"/>, the bomb's own, credited to whoever damaged it last (a civilian killed by a
/// barrel the player shot is the player's: CivilianCasualties). States only get worse: Intact, Leaking, Burning, Spent.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class ExplosiveProp : MonoBehaviour
{
    public enum Kind { PetrolDrum, DieselDrum, JerryCan, GasCylinder, PropaneTank, FuelPump }
    public enum State { Intact, Leaking, Burning, Spent }

    [Header("What it is (written by the builder)")]
    public Kind kind = Kind.PetrolDrum;

    [Header("Blast")]
    [Tooltip("Damage at the centre of the blast.")]
    [Min(0f)] public float blastDamage = 160f;
    [Tooltip("Blast radius, metres.")]
    [Min(0f)] public float blastRadius = 6f;
    [Tooltip("Share of the damage left at the edge of the radius.")]
    [Range(0f, 1f)] public float edgeDamageFraction = 0.2f;
    [Tooltip("Push given to loose rigidbodies (and ragdolls).")]
    [Min(0f)] public float explosionForce = 1100f;

    [Header("Fire")]
    [Tooltip("Seconds it burns before it bursts: a range, picked when it lights. 0 for things that go up at once.")]
    public Vector2 burnBeforeBurst = new Vector2(2.5f, 5f);
    [Tooltip("Chance a single round lights it (a spark). 1 for things that light at once, near 0 for diesel.")]
    [Range(0f, 1f)] public float igniteChancePerHit = 0.3f;
    [Tooltip("Seconds the spilt fuel burns after the burst (or, for a pump, how long it sprays). 0: none.")]
    [Min(0f)] public float poolFireSeconds = 9f;
    [Tooltip("Radius of the burning pool, metres.")]
    [Min(0f)] public float poolFireRadius = 1.6f;
    [Tooltip("Damage per second to anything standing in the fire.")]
    [Min(0f)] public float fireDamagePerSecond = 14f;

    [Header("Look and sound (written by the builder)")]
    public GameObject explosionPrefab;
    public Color blastColor = new Color(1f, 0.5f, 0.12f);
    [Min(0.05f)] public float blastVisualSeconds = 0.6f;
    public Material fireMaterial;
    public Material smokeMaterial;
    public AudioClip burstClip;
    public AudioClip igniteClip;
    [Range(0f, 1f)] public float cameraShake = 0.8f;

    public State Current { get; private set; } = State.Intact;

    Health _health;
    GameObject _source;
    float _burstAt = -1f, _nextBurn;
    FuelFire _fire;

    void Awake()
    {
        _health = GetComponent<Health>();
        _health.destroyOnDeath = false;
    }

    void OnEnable()
    {
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
    }

    void OnDisable()
    {
        _health.Damaged -= OnDamaged;
        _health.Died -= OnDied;
    }

    void OnDamaged(Health h, DamageInfo info)
    {
        if (Current == State.Spent) return;
        if (info.source != null) _source = info.source;

        if (info.fromBlast)
        {
            // The chain: another blast. Cylinders burst at once; the rest catch and go after a breath.
            if (kind == Kind.GasCylinder || kind == Kind.PropaneTank || kind == Kind.JerryCan) Burst(Random.Range(0.08f, 0.35f));
            else if (kind == Kind.FuelPump) Ignite();
            else if (kind == Kind.DieselDrum) Ignite(Random.Range(6f, 12f));
            else Burst(Random.Range(0.15f, 0.6f));
            return;
        }

        if (Current == State.Intact) Current = State.Leaking;
        if (Current == State.Leaking && Random.value < igniteChancePerHit) Ignite();
    }

    void OnDied(Health h)
    {
        if (Current == State.Spent) return;
        if (h.LastDamage.source != null) _source = h.LastDamage.source;
        if (kind == Kind.DieselDrum && Current != State.Burning) { Ignite(Random.Range(6f, 12f)); return; }
        Ignite();
    }

    /// <summary>Sets it alight. It bursts when the fire has heated it long enough (never, for a pump).</summary>
    public void Ignite(float burstAfter = -1f)
    {
        if (Current == State.Burning || Current == State.Spent) return;
        Current = State.Burning;
        if (igniteClip != null) OneShotAudio.Play(igniteClip, transform.position, 0.9f, Random.Range(0.92f, 1.08f), minDistance: 8f);
        NoiseSystem.Emit(transform.position, 40f, NoiseKind.Blast, _source);

        float radius = kind == Kind.FuelPump ? poolFireRadius : Mathf.Max(0.4f, poolFireRadius * 0.45f);
        float seconds = kind == Kind.FuelPump ? poolFireSeconds : 999f;
        _fire = FuelFire.Spawn(Centre, radius, seconds, fireDamagePerSecond, fireMaterial, smokeMaterial, _source, transform);

        if (kind == Kind.FuelPump) { Current = State.Spent; return; }
        float wait = burstAfter >= 0f ? burstAfter : Random.Range(burnBeforeBurst.x, burnBeforeBurst.y);
        _burstAt = Time.time + Mathf.Max(0f, wait);
    }

    void Burst(float delay)
    {
        if (Current == State.Spent) return;
        if (Current != State.Burning) Ignite(delay);
        else _burstAt = Mathf.Min(_burstAt < 0f ? float.MaxValue : _burstAt, Time.time + delay);
    }

    void Update()
    {
        if (Current != State.Burning || _burstAt < 0f) return;
        if (Time.time >= _burstAt) Explode();
    }

    Vector3 Centre
    {
        get
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return transform.position + Vector3.up * 0.5f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }
    }

    /// <summary>Goes up: the blast, the fireball, the sound, the shake; the burning pool left behind. Once.</summary>
    public void Explode()
    {
        if (Current == State.Spent) return;
        Current = State.Spent;
        Vector3 at = Centre;
        if (_fire != null) _fire.Stop();

        // Gone before the blast is measured, so the blast does not find its own Health and the chain does not loop.
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

        var spec = new BlastSpec
        {
            damage = blastDamage,
            radius = blastRadius,
            edgeDamageFraction = edgeDamageFraction,
            falloffPower = 1.5f,
            selfDamageFraction = 1f,
            explosionForce = explosionForce,
        };
        int mask = ~LayerMask.GetMask("PlayerBounds", "FirstPerson", "Ignore Raycast", "UI");
        Explosion.Blast(at, spec, _source, mask, gameObject);

        if (explosionPrefab != null)
        {
            var effect = Instantiate(explosionPrefab, at, Quaternion.identity);
            var blast = effect.GetComponent<Explosion>();
            if (blast != null) blast.Play(blastRadius, blastColor, blastVisualSeconds);
            else Destroy(effect, 3f);
        }
        if (burstClip != null) OneShotAudio.Play(burstClip, at, 1f, Random.Range(0.9f, 1.05f), minDistance: Mathf.Max(20f, blastRadius * 5f));
        Shake(at);

        if (poolFireSeconds > 0f && kind != Kind.GasCylinder && kind != Kind.PropaneTank)
        {
            float ground = Physics.Raycast(at, Vector3.down, out var hit, 4f, ~LayerMask.GetMask("Player", "Enemy", "Civilian", "FirstPerson"), QueryTriggerInteraction.Ignore)
                ? hit.point.y : transform.position.y;
            FuelFire.Spawn(new Vector3(at.x, ground + 0.05f, at.z), poolFireRadius, poolFireSeconds, fireDamagePerSecond, fireMaterial, smokeMaterial, _source, null);
        }
        Destroy(gameObject, 0.1f);
    }

    void Shake(Vector3 at)
    {
        if (cameraShake <= 0f) return;
        var motor = FindAnyObjectByType<PlayerMotor>();
        if (motor == null) return;
        float d = Vector3.Distance(motor.transform.position, at);
        float reach = blastRadius * 4f;
        if (d < reach) motor.AddShake(cameraShake * (1f - d / reach));
    }
}
