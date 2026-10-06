using UnityEngine;

/// <summary>What made a sound, which decides how a listener takes it.</summary>
public enum NoiseKind
{
    /// <summary>A footstep, a landing: someone is moving about. Worth a look.</summary>
    Movement,
    /// <summary>A shot. Nobody mistakes it: the hearer knows where the fight is.</summary>
    Gunfire,
    /// <summary>A blow landing, a body falling, a scream: something happened over there.</summary>
    Violence,
    /// <summary>An explosion.</summary>
    Blast,
    /// <summary>An enemy calling out where the player is. Only enemies make it.</summary>
    Shout,
}

/// <summary>
/// Every sound an enemy could hear, kept for a few seconds in a small ring. Senses read it
/// on their own schedule; nothing is pushed at anyone, so a level of forty listeners costs
/// a loop over the last few dozen sounds each time one of them thinks.
/// </summary>
public static class NoiseSystem
{
    public struct Noise
    {
        public Vector3 position;
        public float radius;
        public NoiseKind kind;
        public float time;
        public GameObject source;
        public int id;
    }

    const int Size = 48;
    static Noise[] _ring;
    static int _next;
    static int _id;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _ring = null;
        _next = 0;
        _id = 0;
    }

    static Noise[] Ring => _ring ??= new Noise[Size];

    /// <summary>A sound at <paramref name="position"/>, heard by anyone within <paramref name="radius"/> metres.</summary>
    public static void Emit(Vector3 position, float radius, NoiseKind kind, GameObject source)
    {
        if (radius <= 0f) return;
        Ring[_next] = new Noise
        {
            position = position,
            radius = radius,
            kind = kind,
            time = Time.time,
            source = source,
            id = ++_id,
        };
        _next = (_next + 1) % Size;
    }

    /// <summary>
    /// The sounds newer than <paramref name="afterId"/> that reach <paramref name="ear"/>,
    /// passed one at a time to <paramref name="heard"/>. Returns the newest id seen, to
    /// pass back next time.
    /// </summary>
    public static int Listen(Vector3 ear, float hearing, int afterId, System.Action<Noise, float> heard)
    {
        int newest = afterId;
        var ring = Ring;
        for (int i = 0; i < ring.Length; i++)
        {
            var n = ring[i];
            if (n.id <= afterId || n.radius <= 0f) continue;
            if (n.id > newest) newest = n.id;
            if (Time.time - n.time > 3f) continue;

            float distance = Vector3.Distance(ear, n.position);
            if (distance > n.radius * hearing) continue;
            heard(n, distance);
        }
        return newest;
    }

    /// <summary>The id of the newest sound, so a new listener does not hear the past.</summary>
    public static int Latest => _id;
}
