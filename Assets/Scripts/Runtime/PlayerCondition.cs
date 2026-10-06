using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The player's health, told without a bar (Ishaan's call: no health bars anywhere, only
/// bodies). The player reads their own state off the world the way they would in it:
///
/// - Hurt, the edges of the view close in, dark red; badly hurt, the colour drains out of
///   everything and the picture dims.
/// - Every hit kicks the edges and splits the colour for a moment.
/// - Breathing comes up when hurt or blown from running; a heartbeat underneath it near
///   the end; the rest of the world goes muffled, as if heard through a wall.
/// - The legs go: badly hurt, the player moves slower.
///
/// Health only: the shield is armour, and armour going down is felt in the hits, not in
/// the body. As the health regenerates every one of these eases off again.
///
/// Added by the PlayerMotor at runtime, so every arena already built has it. Numbers in
/// <see cref="ConditionSettings"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCondition : MonoBehaviour
{
    Health _health;
    PlayerMotor _motor;
    Transform _listener;

    GameObject _volumeObject;
    VolumeProfile _profile;
    Vignette _vignette;
    ColorAdjustments _colour;
    ChromaticAberration _chroma;

    AudioSource _breath, _heart;
    AudioLowPassFilter _muffle;

    float _hurt;
    float _hit;

    ConditionSettings Settings => ConditionSettings.Active;

    /// <summary>0 when whole, 1 at death's door. Smoothed, so it is what the screen shows.</summary>
    public float Hurt => _hurt;

    void Awake()
    {
        _health = GetComponent<Health>();
        _motor = GetComponent<PlayerMotor>();
    }

    void OnEnable()
    {
        if (_health != null) _health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_health != null) _health.Damaged -= OnDamaged;

        if (_volumeObject != null) Destroy(_volumeObject);
        if (_profile != null) Destroy(_profile);
        _volumeObject = null;
        _profile = null;

        if (_muffle != null) Destroy(_muffle);
        if (_motor != null) _motor.ConditionMultiplier = 1f;
    }

    void Start()
    {
        BuildVolume();
        BuildSound();
    }

    void OnDamaged(Health h, DamageInfo info)
    {
        if (info.amount <= 0f) return;
        float share = info.amount / Mathf.Max(1f, h.maxHealth);
        _hit = Mathf.Clamp01(Mathf.Max(_hit, 0.35f + share * 3f));
    }

    void BuildVolume()
    {
        _volumeObject = new GameObject("PlayerCondition");
        _volumeObject.transform.SetParent(transform, false);
        var volume = _volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 90f;

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "PlayerCondition";
        volume.sharedProfile = _profile;

        _vignette = _profile.Add<Vignette>(true);
        _vignette.color.Override(Settings.edgeColour);
        _vignette.smoothness.Override(0.85f);
        _vignette.intensity.Override(0f);

        _colour = _profile.Add<ColorAdjustments>(true);
        _colour.saturation.Override(0f);
        _colour.postExposure.Override(0f);

        _chroma = _profile.Add<ChromaticAberration>(true);
        _chroma.intensity.Override(0f);
    }

    void BuildSound()
    {
        var s = Settings;
        _breath = Loop(s.breathing, "Breath");
        _heart = Loop(s.heartbeat, "Heartbeat");

        var listener = FindAnyObjectByType<AudioListener>();
        if (listener != null)
        {
            _listener = listener.transform;
            _muffle = listener.GetComponent<AudioLowPassFilter>();
            if (_muffle == null) _muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
            _muffle.cutoffFrequency = 22000f;
        }
    }

    AudioSource Loop(AudioClip clip, string name)
    {
        if (clip == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.6f;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.playOnAwake = false;
        // Bypasses the muffle: it is inside the player's own head.
        source.bypassListenerEffects = true;
        source.Play();
        return source;
    }

    void Update()
    {
        if (_health == null) return;
        var s = Settings;

        bool dead = _health.IsDead;
        float raw = dead ? 1f : 1f - Mathf.Clamp01(_health.Current / Mathf.Max(1f, _health.maxHealth));
        _hurt = Mathf.MoveTowards(_hurt, raw, Time.deltaTime * (raw > _hurt ? 3f : 0.6f));
        _hit = Mathf.MoveTowards(_hit, 0f, Time.deltaTime * 2.5f);

        // Nothing shows until the player is actually hurt; then it builds.
        float hurt = Mathf.Clamp01((_hurt - s.showFrom) / (1f - s.showFrom));
        float critical = Mathf.Clamp01((_hurt - s.criticalFrom) / (1f - s.criticalFrom));

        if (_vignette != null)
        {
            _vignette.intensity.value = Mathf.Clamp01(s.edgeAtWorst * hurt + s.edgeOnHit * _hit);
            _colour.saturation.value = -s.drainAtWorst * critical;
            _colour.postExposure.value = -s.dimAtWorst * critical;
            _chroma.intensity.value = Mathf.Clamp01(s.splitOnHit * _hit + 0.15f * critical);
        }

        // Blown from sprinting counts as well as hurt, for the breathing.
        float winded = _motor != null ? 1f - _motor.Stamina01 : 0f;
        float breathe = dead ? 0f : Mathf.Max(hurt * 0.9f, winded * 0.7f);
        if (_breath != null)
        {
            _breath.volume = Mathf.MoveTowards(_breath.volume, s.breathVolume * breathe, Time.deltaTime);
            _breath.pitch = 1f + 0.25f * Mathf.Max(critical, winded);
        }

        if (_heart != null)
        {
            _heart.volume = Mathf.MoveTowards(_heart.volume, dead ? 0f : s.heartVolume * critical, Time.deltaTime);
            _heart.pitch = 1f + 0.35f * critical;
        }

        if (_muffle != null)
            _muffle.cutoffFrequency = Mathf.Lerp(22000f, s.muffleAtWorst, Mathf.Pow(critical, 1.5f));

        if (_motor != null) _motor.ConditionMultiplier = Mathf.Lerp(1f, s.speedAtWorst, critical);
    }
}
