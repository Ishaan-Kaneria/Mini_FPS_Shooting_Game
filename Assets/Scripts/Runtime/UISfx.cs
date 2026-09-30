using UnityEngine;

/// <summary>
/// The interface's sounds anywhere: menu, pause card, results, on-screen keyboard.
///
/// <see cref="UISounds"/> lives on the dashboard canvas and carries the music, so a button in
/// an arena scene had nobody to ask and made no sound at all. This is the fallback every
/// <see cref="UIButtonSound"/> and kit control uses when there is no <see cref="UISounds"/>
/// above it: one hidden 2D source, created on first use, with the clips read off
/// <see cref="UITheme"/>.
///
/// <b>It ignores the listener's pause</b>, because the pause card and the results screen are
/// exactly the screens shown with <c>AudioListener.pause</c> set. It still goes through the
/// listener's volume, so the player's master and effects sliders apply.
/// </summary>
public static class UISfx
{
    public enum Sound { Click, Hover, Back, Launch, Purchase, Key, Toggle, Open, Star, Error }

    static AudioSource _source, _pitched;
    static float _nextHover;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _source = _pitched = null;
        _nextHover = 0f;
    }

    /// <summary>The clip the theme gives a sound, or null.</summary>
    public static AudioClip ClipFor(Sound s, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        if (t == null) return null;
        return s switch
        {
            Sound.Hover => t.soundHover,
            Sound.Back => t.soundBack,
            Sound.Launch => t.soundLaunch != null ? t.soundLaunch : t.soundClick,
            Sound.Purchase => t.soundPurchase != null ? t.soundPurchase : t.soundClick,
            Sound.Key => t.soundKey != null ? t.soundKey : t.soundClick,
            Sound.Toggle => t.soundToggle != null ? t.soundToggle : t.soundClick,
            Sound.Open => t.soundOpen,
            Sound.Star => t.soundStar,
            Sound.Error => t.soundError,
            _ => t.soundClick,
        };
    }

    /// <summary>Plays a sound. Pitch is for a run of stars; everything else leaves it at 1.</summary>
    public static void Play(Sound s, float pitch = 1f, float volumeScale = 1f)
    {
        if (!Application.isPlaying) return;
        var t = UITheme.Active;
        var clip = ClipFor(s, t);
        if (clip == null) return;

        // Hover fires as the pointer crosses a grid; without a gap, six cards are six blips.
        if (s == Sound.Hover)
        {
            if (Time.unscaledTime < _nextHover) return;
            _nextHover = Time.unscaledTime + 0.06f;
            volumeScale *= 0.4f;
        }

        // A pitched sound gets its own source: changing the pitch of the shared one would
        // bend whatever click is still ringing on it.
        var source = Source(!Mathf.Approximately(pitch, 1f));
        source.pitch = pitch;
        source.PlayOneShot(clip, Mathf.Clamp01(t.soundVolume * volumeScale));
    }

    static AudioSource Source(bool pitched)
    {
        if (pitched ? _pitched != null : _source != null) return pitched ? _pitched : _source;
        var go = new GameObject(pitched ? "UISfx (pitched)" : "UISfx") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true;
        if (pitched) _pitched = s; else _source = s;
        return s;
    }
}
