using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A real front door (his brief, 2026-11-03: "after repeated kicking the door has to slam and the house has to open"). It
/// hangs on its hinge (this transform, the hinge line its up axis) and starts shut and bolted. A kick shakes it in its frame
/// with a thud; after <see cref="kicksToBreak"/> the bolt gives: it bursts in on its hinge, slams against the wall, bounces
/// back off it and settles there, and dust shakes out of the frame. A round only knocks it (a shudder and a splinter of
/// sound). While it is bolted a NavMeshObstacle carves the doorway, so nothing walks through a shut door and nothing is
/// spawned behind one; burst open, the way through is the navmesh's again. The swing is a spring on the hinge angle, run
/// here (the leaf's collider is moved by a kinematic body), so it is the same on every machine. Holds no static state.
/// </summary>
[DisallowMultipleComponent]
public class Door : MonoBehaviour, IWorldHit
{
    [Header("Swing")]
    [Tooltip("Degrees the door swings to when open (positive opens into the house). The wall behind it stops it there.")]
    public float openAngle = 95f;
    [Tooltip("The swing's speed as the bolt gives, degrees a second.")]
    public float burstSpeed = 720f;
    [Tooltip("How much of its speed the door keeps bouncing off the wall it slams into (0 dead, 1 a perfect bounce).")]
    [Range(0f, 0.8f)] public float bounce = 0.35f;
    [Tooltip("The hinge's friction: how fast a swing dies, per second.")]
    public float damping = 2.5f;

    [Header("Bolt")]
    [Tooltip("Kicks the bolt takes before it gives.")]
    [Min(1)] public int kicksToBreak = 3;
    [Tooltip("Degrees a kick knocks a bolted door in its frame (it springs straight back).")]
    public float rattle = 2.5f;

    [Header("Sound")]
    public AudioClip kickClip;
    public AudioClip burstClip;

    [Header("Doorway")]
    [Tooltip("What keeps the navmesh's way through shut while the door is bolted (shared by both leaves of a double door).")]
    public NavMeshObstacle obstacle;
    [Tooltip("The other leaf of a double door: it gives with this one.")]
    public Door partner;

    int _kicks;
    bool _bolted = true;
    float _angle, _speed, _rattle, _rattleSpeed;
    Quaternion _shut;
    bool _ready;
    AudioSource _audio;

    /// <summary>Shut and bolted (kicks to go), or burst open.</summary>
    public bool Bolted => _bolted;
    public float Angle => _angle;

    void Awake()
    {
        // Saved off, on from the start of play: on in the editor, the block stayed cut into the navmesh for every bake and check.
        if (obstacle != null) obstacle.enabled = _bolted;
        _shut = transform.localRotation;
        _ready = true;
    }

    AudioSource Audio
    {
        get
        {
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f; _audio.minDistance = 2f; _audio.maxDistance = 40f; _audio.playOnAwake = false;
            }
            return _audio;
        }
    }

    public void OnWorldHit(in WorldHitInfo hit)
    {
        // Which way it was struck, against the way it opens: a kick from inside only shakes it.
        Vector3 inward = transform.rotation * Vector3.forward;   // the leaf's face normal, pointing into the house when shut
        bool fromOutside = Vector3.Dot(hit.direction, inward) > 0f;
        // Pushed the way the blow goes: into the house (the opening way, openAngle's sign) from outside. Unsigned, a leaf
        // hinged on its left swung out at the kicker first and then in (his play-test, 2026-11-03).
        float push = (fromOutside ? 1f : -1f) * Mathf.Sign(openAngle);
        switch (hit.kind)
        {
            case WorldHitKind.Kick:
            case WorldHitKind.Blast:
                Play(kickClip, 0.85f, Random.Range(0.75f, 0.9f));
                if (_bolted)
                {
                    _rattleSpeed += push * rattle * 18f;
                    if (fromOutside || hit.kind == WorldHitKind.Blast) _kicks++;
                    if (_kicks >= kicksToBreak || hit.kind == WorldHitKind.Blast) Burst(fromOutside ? 1f : 1f);
                }
                else _speed += push * burstSpeed * 0.6f;   // open: a kick swings it
                break;
            case WorldHitKind.Bullet:
                Play(kickClip, 0.35f, Random.Range(1.2f, 1.45f));
                if (_bolted) _rattleSpeed += push * rattle * 4f;
                else _speed += push * 25f;
                break;
            default:
                Play(kickClip, 0.5f, 1f);
                if (!_bolted) _speed += push * burstSpeed * 0.25f;
                else _rattleSpeed += push * rattle * 8f;
                break;
        }
    }

    /// <summary>The bolt gives: the door flies in on its hinge.</summary>
    public void Burst(float strength = 1f)
    {
        if (!_bolted) return;
        _bolted = false;
        _speed = burstSpeed * strength * Mathf.Sign(openAngle);
        _rattle = _rattleSpeed = 0f;
        Play(burstClip != null ? burstClip : kickClip, 1f, 0.7f);
        if (obstacle != null) obstacle.enabled = false;
        if (partner != null) partner.Burst(strength * 0.8f);
        var dust = GetComponentInChildren<ParticleSystem>();
        if (dust != null) dust.Play();
    }

    void Update()
    {
        if (!_ready) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // A bolted door only shakes in its frame and springs back.
        if (_bolted)
        {
            _rattleSpeed += -_rattle * 900f * dt;
            _rattleSpeed *= Mathf.Exp(-18f * dt);
            _rattle += _rattleSpeed * dt;
            if (Mathf.Abs(_rattle) < 0.01f && Mathf.Abs(_rattleSpeed) < 0.1f) { _rattle = _rattleSpeed = 0f; }
            Apply(_rattle);
            return;
        }

        if (Mathf.Abs(_speed) < 0.5f) return;
        _speed *= Mathf.Exp(-damping * dt);
        _angle += _speed * dt;
        // The wall behind the open door, and the frame it shut against: it slams and bounces off both.
        float lo = Mathf.Min(0f, openAngle), hi = Mathf.Max(0f, openAngle);
        if (_angle > hi || _angle < lo)
        {
            float hard = Mathf.Abs(_speed);
            _angle = Mathf.Clamp(_angle, lo, hi);
            _speed = -_speed * bounce;
            if (hard > 120f) Play(kickClip, Mathf.Clamp01(hard / burstSpeed), 0.65f);   // the slam
        }
        Apply(_angle);
    }

    void Apply(float degrees) => transform.localRotation = _shut * Quaternion.AngleAxis(degrees, Vector3.up);

    void Play(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        Audio.pitch = pitch;
        Audio.PlayOneShot(clip, volume);
    }
}
