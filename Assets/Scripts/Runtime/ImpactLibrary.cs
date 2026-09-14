using UnityEngine;

/// <summary>
/// Maps a surface tag (Concrete / Metal / Wood / Flesh) to an impact effect and
/// sound. Everything is optional -- an empty library is silently ignored, so the
/// grey-box scene runs fine before you have any art.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Impact Library", fileName = "ImpactLibrary")]
public class ImpactLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string surfaceTag = "Concrete";
        public GameObject effectPrefab;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.8f;
        public float effectLifetime = 4f;
    }

    public Entry[] entries;
    public Entry fallback;

    public Entry Find(string surfaceTag)
    {
        if (entries != null)
            foreach (var e in entries)
                if (e != null && e.surfaceTag == surfaceTag) return e;
        return fallback;
    }

    /// <summary>Spawns the effect oriented along the surface normal and plays a sound.</summary>
    public void Spawn(string surfaceTag, Vector3 point, Vector3 normal)
    {
        var entry = Find(surfaceTag);
        if (entry == null) return;

        if (entry.effectPrefab != null)
        {
            var fx = Instantiate(entry.effectPrefab, point + normal * 0.01f,
                                 Quaternion.LookRotation(normal));
            if (entry.effectLifetime > 0f) Destroy(fx, entry.effectLifetime);
        }

        if (entry.clips != null && entry.clips.Length > 0)
        {
            var clip = entry.clips[Random.Range(0, entry.clips.Length)];
            if (clip != null) AudioSource.PlayClipAtPoint(clip, point, entry.volume);
        }
    }
}
