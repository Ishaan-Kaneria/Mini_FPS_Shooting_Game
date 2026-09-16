using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A small pool of 3D AudioSources for fire-and-forget sounds that have a position in
/// the world but no object to belong to -- bullet impacts and pickups.
///
/// Both of those used <c>AudioSource.PlayClipAtPoint</c>, which creates a GameObject,
/// adds an AudioSource to it, plays one clip and destroys the lot. At the rifle's 650
/// rounds a minute that is around eleven object creations and destructions every second
/// the trigger is held, for sounds a few frames long -- and impacts are the one sound in
/// this kit that fires on almost every shot.
///
/// Pooling also gives the kit a single place to route positional one-shots through an
/// AudioMixer if it ever grows one. PlayClipAtPoint cannot be routed at all, because the
/// source it builds is private to the call.
/// </summary>
public static class OneShotAudio
{
    /// <summary>
    /// Ceiling on simultaneous one-shots. Past this, the oldest is cut short to make
    /// room: a few dozen overlapping impacts are already indistinguishable, and an
    /// unbounded pool under sustained fire is the churn this exists to avoid.
    /// </summary>
    const int MaxSources = 24;

    /// <summary>
    /// Matches what PlayClipAtPoint does closely enough to be a drop-in -- 3D,
    /// logarithmic falloff -- but with a range that suits an arena rather than Unity's
    /// 500 metre default, which is well past the point of being audible anyway.
    /// </summary>
    const float MinDistance = 1.5f;
    const float MaxDistance = 120f;

    static readonly List<AudioSource> Sources = new List<AudioSource>();
    static Transform _root;

    /// <summary>Round-robin cursor, used only once every source is busy.</summary>
    static int _next;

    /// <summary>
    /// Domain reload is disabled in this project, so without this the pool would carry
    /// destroyed AudioSources from the previous play session into the next one -- and a
    /// pool full of destroyed sources plays nothing at all, for the rest of the session.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        Sources.Clear();
        _root = null;
        _next = 0;
    }

    /// <summary>
    /// Plays a clip at a world position. Silently does nothing without a clip, which is
    /// what lets every audio field in the kit stay optional.
    /// </summary>
    /// <param name="pitch">
    /// Playback rate. Variance here is what stops a sound that fires several times a
    /// second -- an impact, a death in a crowd -- reading as one sample on a loop.
    /// </param>
    /// <param name="minDistance">
    /// Metres the sound stays at full volume for before it starts rolling off. 0 keeps
    /// the pool's own, which suits a bullet hitting a wall. A blast wants a much larger
    /// one: rolling a detonation off from a metre and a half away makes something that
    /// covers a quarter of the arena sound like it happened somewhere else.
    /// </param>
    public static void Play(AudioClip clip, Vector3 position, float volume = 1f,
                            float pitch = 1f, float minDistance = 0f)
    {
        if (clip == null) return;

        var source = Take();
        if (source == null) return;

        source.transform.position = position;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        // Put back every time rather than only when overridden. A pooled source keeps
        // whatever the last caller set, so a bullet impact borrowing the source a blast
        // used would otherwise inherit its reach and be audible across the level.
        source.minDistance = minDistance > 0f ? minDistance : MinDistance;
        source.maxDistance = Mathf.Max(MaxDistance, source.minDistance * 8f);

        source.Play();
    }

    /// <summary>
    /// A source that is free, or a new one, or -- once the pool is full -- the next one
    /// in rotation, cut short.
    /// </summary>
    static AudioSource Take()
    {
        var root = Root();
        if (root == null) return null;

        // Destroyed entries are dropped rather than skipped. A scene reload takes the
        // whole pool with it, and leaving the corpses in the list would shrink the pool
        // a little more on every restart until nothing was left to play with.
        for (int i = Sources.Count - 1; i >= 0; i--)
            if (Sources[i] == null) Sources.RemoveAt(i);

        foreach (var source in Sources)
            if (!source.isPlaying) return source;

        if (Sources.Count < MaxSources) return Create(root);

        _next = (_next + 1) % Sources.Count;
        return Sources[_next];
    }

    static AudioSource Create(Transform root)
    {
        var go = new GameObject($"OneShot_{Sources.Count:00}");
        go.transform.SetParent(root, false);

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = MinDistance;
        source.maxDistance = MaxDistance;

        Sources.Add(source);
        return source;
    }

    /// <summary>
    /// The object the pool hangs off. Rebuilt when missing rather than created once,
    /// because a scene reload destroys it while the static reference survives -- and
    /// comparing a destroyed Unity object against null is exactly what catches that.
    /// </summary>
    static Transform Root()
    {
        if (_root != null) return _root;

        // Nothing is left behind between scenes: the pool is cheap to rebuild, and an
        // object that outlived its scene would have to be reasoned about every time
        // something else went looking for stray AudioSources.
        var go = new GameObject("OneShotAudio");
        _root = go.transform;

        // The pool is rebuilt from scratch whenever it goes missing, so the old entries
        // belong to a scene that no longer exists.
        Sources.Clear();
        _next = 0;

        return _root;
    }
}
