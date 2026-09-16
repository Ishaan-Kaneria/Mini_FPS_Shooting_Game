using UnityEngine;

/// <summary>
/// The dashboard's voice: a bed of music under it and a small set of interface
/// sounds over the top.
///
/// One component owns every menu sound so there is a single place to set levels,
/// and so nothing else has to hold an AudioSource. <see cref="UIButtonSound"/> finds
/// this by walking up its parents, which is what keeps the builder from having to
/// wire a reference into every button it makes.
///
/// Everything is optional. A missing clip plays nothing rather than throwing, the
/// way every other audio field in the kit behaves.
/// </summary>
[DisallowMultipleComponent]
public class UISounds : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("One-shots. 2D, because a menu has no geometry to be positioned in.")]
    public AudioSource effects;

    [Tooltip("The looping bed. Separate from the one-shots so its level can be set " +
             "independently and so a click never cuts the music off.")]
    public AudioSource music;

    [Header("Clips")]
    public AudioClip click;
    public AudioClip hover;
    public AudioClip back;

    [Tooltip("Played when a run is starting, rather than the ordinary click.")]
    public AudioClip launch;

    [Tooltip("Played when coins actually change hands. A purchase that sounds like every " +
             "other click is a purchase the player is not sure went through.")]
    public AudioClip purchase;

    [Header("Levels")]
    [Range(0f, 1f)] public float effectVolume = 0.55f;

    [Tooltip("Hover fires constantly as the pointer crosses the grid, so it sits well " +
             "under the click. A menu that chirps at full volume every time the mouse " +
             "moves is one people turn the sound off for.")]
    [Range(0f, 1f)] public float hoverVolume = 0.22f;

    [Range(0f, 1f)] public float musicVolume = 0.32f;

    [Tooltip("Seconds the music takes to come up. Starting at full level the instant " +
             "the scene loads reads as a mistake rather than as a soundtrack.")]
    [Min(0f)] public float musicFadeIn = 1.6f;

    [Tooltip("Minimum seconds between hover sounds. Without it, dragging the pointer " +
             "across six cards fires six overlapping blips.")]
    [Min(0f)] public float hoverInterval = 0.06f;

    float _nextHoverTime;
    float _musicTarget;

    void Awake()
    {
        _musicTarget = musicVolume;

        if (music == null) return;

        music.loop = true;
        music.spatialBlend = 0f;
        music.volume = musicFadeIn > 0f ? 0f : _musicTarget;

        if (music.clip != null && !music.isPlaying) music.Play();
    }

    void Update()
    {
        if (music == null || musicFadeIn <= 0f) return;
        if (music.volume >= _musicTarget) return;

        // Unscaled, because the dashboard is sometimes reached from a frozen game over.
        music.volume = Mathf.MoveTowards(music.volume, _musicTarget,
                                         _musicTarget / musicFadeIn * Time.unscaledDeltaTime);
    }

    public void PlayClick() => Play(click, effectVolume);
    public void PlayBack() => Play(back, effectVolume);
    public void PlayLaunch() => Play(launch != null ? launch : click, effectVolume);
    public void PlayPurchase() => Play(purchase != null ? purchase : click, effectVolume);

    public void PlayHover()
    {
        if (Time.unscaledTime < _nextHoverTime) return;

        _nextHoverTime = Time.unscaledTime + hoverInterval;
        Play(hover, hoverVolume);
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || effects == null) return;

        effects.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
}
