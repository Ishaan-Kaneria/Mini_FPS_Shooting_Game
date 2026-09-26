using UnityEngine;

/// <summary>
/// An enemy's throat: what it shouts when it finds you, grunts when a round lands,
/// screams when a leg goes, growls while it hunts and makes as it dies.
///
/// Separate from <see cref="EnemyAI"/>, which keeps its old single-clip fields as the
/// fallback for a prefab without one of these. With one present the AI stays quiet and
/// this does the talking, picking from a <see cref="VoiceBank"/> by
/// <see cref="EnemyArchetype.voice"/>.
///
/// Two things keep a crowd from turning into a wall of noise. Each enemy has its own
/// cooldowns, and the hunting sounds share one budget across the whole level: a single
/// growl every so often from somewhere in the dark is menace, twenty at once is static.
/// A death is never limited -- it is the one sound that says stop shooting this.
///
/// Each enemy speaks at its own pitch, set once at spawn and lowered for a bigger body,
/// so two of the same archetype are two voices and a boss sounds like the size it is.
/// </summary>
[DisallowMultipleComponent]
public class EnemyVoice : MonoBehaviour
{
    public enum Kind { Human, Creature }

    public Kind kind = Kind.Human;
    public VoiceBank bank;

    [Tooltip("Its own source, apart from the one the rifle fires through: a voice is " +
             "pitched per enemy, and a pitch set on a shared source bends the gunshot " +
             "playing through it too. Falls back to the body's source when empty.")]
    public AudioSource source;

    [Header("Volume")]
    [Range(0f, 1f)] public float painVolume = 0.75f;
    [Range(0f, 1f)] public float hurtVolume = 0.95f;
    [Range(0f, 1f)] public float deathVolume = 1f;
    [Range(0f, 1f)] public float huntVolume = 0.6f;
    [Range(0f, 1f)] public float alertVolume = 0.9f;

    [Header("Timing")]
    [Tooltip("Minimum seconds between grunts, so sustained fire does not machine-gun the voice.")]
    [Min(0f)] public float painCooldown = 0.32f;

    [Tooltip("Seconds between one enemy's hunting sounds, picked between these two.")]
    public Vector2 huntInterval = new Vector2(5f, 10f);

    [Tooltip("Seconds between any two hunting sounds in the whole level. The shared " +
             "budget that keeps a crowd from drowning out the fight.")]
    [Min(0f)] public float huntGap = 1.1f;

    [Tooltip("A hit worth this share of its health is a scream, not a grunt.")]
    [Range(0.05f, 1f)] public float hurtShare = 0.3f;

    Health _health;
    EnemyAI _ai;
    EnemyWounds _wounds;
    AudioSource _source;

    float _pitch = 1f;
    float _nextPain;
    float _nextHunt;
    bool _alerted;

    /// <summary>When the level may next hear a hunting sound, from any enemy.</summary>
    static float s_nextHuntAnywhere;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => s_nextHuntAnywhere = 0f;

    VoiceBank.Set Voice => bank != null ? bank.For(kind) : null;

    void Awake()
    {
        _health = GetComponent<Health>();
        _ai = GetComponent<EnemyAI>();
        _wounds = GetComponent<EnemyWounds>();
        _source = source != null ? source : GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        if (_health != null)
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }

        if (_wounds != null) _wounds.Crippled += OnCrippled;
    }

    void OnDisable()
    {
        if (_health != null)
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        if (_wounds != null) _wounds.Crippled -= OnCrippled;
    }

    void Start()
    {
        // After the archetype has scaled the body. A body twice the size speaks about a
        // fifth lower -- the square root, because a full octave for a big enemy turns a
        // voice into a sound effect.
        float size = Mathf.Max(0.3f, transform.lossyScale.y);
        _pitch = Random.Range(0.92f, 1.08f) / Mathf.Sqrt(size) * (Voice != null ? Voice.pitch : 1f);

        _nextHunt = Time.time + Random.Range(huntInterval.x, huntInterval.y);
    }

    void Update()
    {
        if (_ai == null || _health == null || _health.IsDead) return;

        if (!_alerted && _ai.HasSpotted)
        {
            _alerted = true;
            Say(VoiceBank.Pick(Voice?.alert), alertVolume);
            _nextHunt = Time.time + Random.Range(huntInterval.x, huntInterval.y);
        }

        if (!_alerted || Time.time < _nextHunt) return;

        _nextHunt = Time.time + Random.Range(huntInterval.x, huntInterval.y);

        if (Time.time < s_nextHuntAnywhere) return;
        s_nextHuntAnywhere = Time.time + huntGap;

        var clip = _ai.Crawling
            ? VoiceBank.Pick(Voice?.crawl, Voice?.hunt)
            : VoiceBank.Pick(Voice?.hunt);

        Say(clip, huntVolume);
    }

    /// <summary>The effort of an attack. Only on a swing: a rifle does the talking for a shot.</summary>
    public void OnAttack(bool shooting)
    {
        if (shooting) return;
        Say(VoiceBank.Pick(Voice?.attack), 0.85f);
    }

    void OnDamaged(Health health, DamageInfo info)
    {
        // The killing blow is the death cry's; both at once reads as two enemies.
        if (health.Current <= 0f || info.amount <= 0f) return;

        _alerted = true;

        bool heavy = info.amount >= health.maxHealth * hurtShare;

        if (!heavy && Time.time < _nextPain) return;
        _nextPain = Time.time + painCooldown;

        if (heavy) Say(VoiceBank.Pick(Voice?.hurt, Voice?.pain), hurtVolume, interrupt: true);
        else Say(VoiceBank.Pick(Voice?.pain), painVolume);
    }

    /// <summary>A leg went, or the gun arm did. Always a scream, whatever the cooldown says.</summary>
    void OnCrippled(EnemyWounds wounds, BodyPart part)
    {
        if (_health != null && _health.IsDead) return;

        _nextPain = Time.time + painCooldown * 2f;
        Say(VoiceBank.Pick(Voice?.hurt, Voice?.pain), hurtVolume, interrupt: true);
    }

    void OnDied(Health health)
    {
        // Whatever it was saying stops: a scream carrying on over the death cry is two
        // voices, and the one that matters is the one that says it is over.
        if (_source != null) _source.Stop();

        var info = health.LastDamage;
        var voice = Voice;

        // A round through the head does not leave time for a scream. A blast does.
        AudioClip clip = info.isHeadshot
            ? VoiceBank.Pick(voice?.headshotDeath, voice?.death)
            : VoiceBank.Pick(voice?.death);

        float volume = info.isHeadshot ? deathVolume * 0.7f : deathVolume;

        // Through the pool rather than this body's own source: the corpse is destroyed
        // in a few seconds, and a cry longer than that would be cut off mid-breath.
        OneShotAudio.Play(clip, transform.position + Vector3.up, volume,
                          _pitch * Random.Range(0.96f, 1.04f));
    }

    void Say(AudioClip clip, float volume, bool interrupt = false)
    {
        if (clip == null || _source == null) return;

        if (interrupt) _source.Stop();

        _source.pitch = _pitch * Random.Range(0.97f, 1.03f);
        _source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
}
