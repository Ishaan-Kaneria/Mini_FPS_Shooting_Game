using UnityEngine;

/// <summary>
/// A car can be destroyed (2026-11-11: cars had no health, fire or explosion, and a blast moved only the one being driven). Every
/// car in the town carries one: those made to drive, the traffic, and the parked ones. Rounds, blasts and hard crashes wear it down;
/// half gone, the engine smokes; all gone, it catches fire, and a few seconds later (time to get out and run) the tank goes up: a
/// blast like a fuel drum's that kills whoever is still in it, throws the car if it can move, and leaves it black and burning. A
/// burnt car cannot be driven or taken. One car's blast can set off the next.
///
/// It borrows its fireball, its sound and its fire from the town's fuel drums (<see cref="ExplosiveProp"/>), so it needs no
/// assets of its own; in an arena with none it still smokes and still does the blast's damage. Asleep (disabled) except while burning.
/// </summary>
[DisallowMultipleComponent]
public class CarWreck : MonoBehaviour, IWorldHit
{
    [Header("How much it takes")]
    [Tooltip("What the car can take before it catches fire. A rifle round takes perRound of it, so 100 and 4 is 25 rounds.")]
    [Min(1f)] public float strength = 100f;
    [Tooltip("Taken off by each round that hits the car anywhere.")]
    [Min(0f)] public float perRound = 4f;
    [Tooltip("Taken off for each metre a second of a crash over crashFrom: at 1.5 a wall met at 63 km/h takes 12, at 100 km/h 28. At 4, three hard knocks set it alight.")]
    [Min(0f)] public float perCrashSpeed = 1.5f;
    [Tooltip("Slowest crash, metres a second, that hurts the car (9 is about 32 km/h).")]
    [Min(0f)] public float crashFrom = 9f;
    [Tooltip("Taken off by a blast right against the car; it falls away to nothing at the blast's edge.")]
    [Min(0f)] public float blastHurt = 170f;
    [Tooltip("Share of its strength left at which the engine starts to smoke.")]
    [Range(0f, 1f)] public float smokeFrom = 0.5f;

    [Header("Going up")]
    [Tooltip("Seconds it burns before the tank goes: the time to get out and away. A range, picked each time.")]
    public Vector2 burnBeforeBurst = new Vector2(3.5f, 5.5f);
    [Tooltip("The blast: damage at the car, and how far it reaches, metres.")]
    [Min(0f)] public float blastDamage = 180f;
    [Min(0f)] public float blastRadius = 7f;
    [Tooltip("Seconds the wreck goes on burning afterwards.")]
    [Min(0f)] public float burnsFor = 14f;

    [Tooltip("Takes no damage at all: for a test that needs the car whole.")]
    public bool indestructible;

    /// <summary>On fire, the tank not yet gone.</summary>
    public bool Burning { get; private set; }
    /// <summary>Burnt out: black, dead, not to be driven.</summary>
    public bool Burnt { get; private set; }
    /// <summary>What is left of its strength, 0 to 1.</summary>
    public float Left01 => Mathf.Clamp01(_left / Mathf.Max(1f, strength));

    float _left, _burstAt;
    int _blastFrame = -1;
    GameObject _source;
    ParticleSystem _smoke;

    static ExplosiveProp _donor;
    static bool _donorSought;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _donor = null; _donorSought = false; }

    /// <summary>The town's fuel drum, for its fireball, bang and flames.</summary>
    static ExplosiveProp Donor
    {
        get
        {
            if (_donor == null && !_donorSought) { _donorSought = true; _donor = FindAnyObjectByType<ExplosiveProp>(FindObjectsInactive.Include); }
            return _donor;
        }
    }

    void Awake()
    {
        _left = strength;
        enabled = false;
    }

    public void OnWorldHit(in WorldHitInfo hit)
    {
        if (hit.kind == WorldHitKind.Bullet) Hurt(perRound, hit.source);
        // What a bare rigidbody would have had from the blow (WorldHit pushes those itself).
        if (TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
            body.AddForceAtPosition(hit.direction.normalized * hit.impulse, hit.point, ForceMode.Impulse);
    }

    /// <summary>A blast that reached the car (Explosion.Blast, once a blast however many of its colliders were found).</summary>
    public void Blasted(Vector3 centre, BlastSpec spec, GameObject attacker)
    {
        if (Burnt || Time.frameCount == _blastFrame) return;
        _blastFrame = Time.frameCount;
        float near = Mathf.Clamp01(1f - Vector3.Distance(centre, Middle) / Mathf.Max(0.1f, spec.radius + 1.5f));
        Hurt(blastHurt * near, attacker);
    }

    void OnCollisionEnter(Collision c)
    {
        float hard = c.relativeVelocity.magnitude;
        if (hard > crashFrom) Hurt((hard - crashFrom) * perCrashSpeed, DrivableCar.Driving != null ? GameObject.FindWithTag("Player") : null);
    }

    /// <summary>Takes <paramref name="amount"/> off the car; <paramref name="source"/> is who answers for what it does when it goes up.</summary>
    public void Hurt(float amount, GameObject source)
    {
        if (Burnt || indestructible || amount <= 0f) return;
        if (source != null) _source = source;
        _left -= amount;
        if (_left <= strength * smokeFrom) Smoke(Burning ? 34f : 9f);
        if (_left <= 0f && !Burning) Ignite();
    }

    Vector3 Middle => transform.TransformPoint(new Vector3(0f, 0.7f, 0f));

    /// <summary>Where the engine is: the front of the car's box, bonnet high.</summary>
    Vector3 EngineLocal
    {
        get
        {
            float front = 1.6f, top = 1.4f;
            foreach (var box in GetComponents<BoxCollider>())
            {
                if (box.isTrigger) continue;
                front = Mathf.Max(front, box.center.z + box.size.z * 0.5f);
                top = Mathf.Max(top, box.center.y + box.size.y * 0.5f);
            }
            return new Vector3(0f, top * 0.62f, front * 0.72f);
        }
    }

    void Ignite()
    {
        Burning = true;
        enabled = true;
        _burstAt = Time.time + Random.Range(burnBeforeBurst.x, burnBeforeBurst.y);
        Smoke(34f);
        var donor = Donor;
        if (donor != null && donor.fireMaterial != null)
            FuelFire.Spawn(transform.TransformPoint(EngineLocal), 0.7f, _burstAt - Time.time + 0.3f, 8f, donor.fireMaterial, donor.smokeMaterial, _source, transform);
        if (donor != null && donor.igniteClip != null) OneShotAudio.Play(donor.igniteClip, Middle, 0.9f);
        if (TryGetComponent<TrafficCar>(out var traffic)) traffic.Bail(Middle);
        // People run from a burning car; a driver gets out of it.
        NoiseSystem.Emit(Middle, 35f, NoiseKind.Blast, _source != null ? _source : gameObject);
    }

    void Update()
    {
        if (Burning && Time.time >= _burstAt) Explode();
        else if (Burnt && Time.time >= _burstAt) { if (_smoke != null) { var e = _smoke.emission; e.rateOverTime = 0f; } enabled = false; }
    }

    /// <summary>The tank goes: the blast, the fireball, whoever is inside, the car thrown and left black and burning. Once.</summary>
    public void Explode()
    {
        if (Burnt) return;
        Burnt = true;
        Burning = false;
        _burstAt = Time.time + burnsFor + 10f;       // the smoke outlasts the fire a little, then Update lets it stop
        enabled = true;
        Vector3 at = Middle;
        GameObject answers = _source != null ? _source : gameObject;

        var drivable = GetComponent<DrivableCar>();
        var traffic = GetComponent<TrafficCar>();
        if (drivable != null)
        {
            // The player still in it: nobody walks away from that. (His own collider is off while he drives, so the blast would miss him.)
            bool inside = DrivableCar.Driving == drivable;
            drivable.Wrecked = true;
            if (inside)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null && player.TryGetComponent<Health>(out var his))
                    his.ApplyDamage(new DamageInfo(his.maxHealth * 3f + his.maxShield, at, Vector3.up, Vector3.up, answers) { fromBlast = true, heavy = true });
            }
        }
        else
        {
            // Whoever is at the wheel. (A car made to drive seats only the player's stand-in, which is nobody.)
            if (traffic != null) traffic.Crash();
            foreach (var h in GetComponentsInChildren<Health>())
                if (!h.IsDead) h.ApplyDamage(new DamageInfo(h.maxHealth * 3f + h.maxShield, at, Vector3.up, Vector3.up, answers) { fromBlast = true, heavy = true });
        }

        var spec = new BlastSpec
        {
            damage = blastDamage,
            radius = blastRadius,
            edgeDamageFraction = 0.2f,
            falloffPower = 1.5f,
            selfDamageFraction = 1f,
            explosionForce = 1100f,
        };
        int mask = ~LayerMask.GetMask("PlayerBounds", "FirstPerson", "Ignore Raycast", "UI");
        Explosion.Blast(at, spec, answers, mask, gameObject);

        var donor = Donor;
        if (donor != null && donor.explosionPrefab != null)
        {
            var effect = Instantiate(donor.explosionPrefab, at, Quaternion.identity);
            var blast = effect.GetComponent<Explosion>();
            if (blast != null) blast.Play(blastRadius, donor.blastColor, 0.7f);
            else Destroy(effect, 3f);
        }
        if (donor != null && donor.burstClip != null) OneShotAudio.Play(donor.burstClip, at, 1f, Random.Range(0.85f, 1f), minDistance: blastRadius * 5f);

        // Thrown, if it is a body that can be: up off its wheels and turning.
        if (TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
        {
            body.AddForce(Vector3.up * 4.5f + Random.insideUnitSphere * 1.2f, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 2.5f, ForceMode.VelocityChange);
        }
        if (TryGetComponent<CarGlass>(out var glass)) glass.Crash(at, 30f);
        Blacken();
        Smoke(26f);
        if (donor != null && donor.fireMaterial != null && burnsFor > 0f)
            FuelFire.Spawn(transform.TransformPoint(new Vector3(0f, 0.5f, 0f)), 1.5f, burnsFor, 12f, donor.fireMaterial, donor.smokeMaterial, answers, transform);
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"), EmissionId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock _block;
    MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

    /// <summary>Paint, trim and lamps all burnt black. Through a property block: the materials are shared with every other car.</summary>
    void Blacken()
    {
        var soot = new Color(0.035f, 0.032f, 0.03f, 1f);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.GetComponentInParent<Health>() != null) continue;      // not the man in it
            r.GetPropertyBlock(Block);
            Block.SetColor(BaseColorId, soot);
            Block.SetColor(ColorId, soot);
            Block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(Block);
        }
    }

    /// <summary>Smoke from under the bonnet, thicker as it gets worse. One particle system, made when first needed.</summary>
    void Smoke(float rate)
    {
        if (_smoke == null)
        {
            var mat = Resources.Load<Material>("Car/Dust");
            if (mat == null) return;
            var go = new GameObject("EngineSmoke");
            go.transform.SetParent(transform, false);
            go.transform.SetLocalPositionAndRotation(EngineLocal, Quaternion.LookRotation(Vector3.up));
            _smoke = go.AddComponent<ParticleSystem>();
            _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _smoke.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90; main.startLifetime = 2.6f; main.startSpeed = 1.6f; main.startSize = 0.5f;
            main.startColor = new Color(0.07f, 0.07f, 0.07f, 0.5f);
            main.gravityModifier = -0.05f;
            main.loop = true;
            var shape = _smoke.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12f; shape.radius = 0.25f;
            var size = _smoke.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(4.5f, AnimationCurve.Linear(0f, 0.25f, 1f, 1f));
            var colour = _smoke.colorOverLifetime; colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            _smoke.Play();
        }
        var emission = _smoke.emission;
        emission.rateOverTime = rate;
    }
}
