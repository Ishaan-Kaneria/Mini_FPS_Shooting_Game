using UnityEngine;

/// <summary>
/// Plays a random clip from a list at random intervals, from a random point near this object: a crow in the
/// trees, a drip in the pavilion, a creak from the coaster. Gives a place a pulse without a looping bed.
///
/// Needs an AudioSource (3D) on the same object; the clip list and the timing are the tuning.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AmbientOneShot : MonoBehaviour
{
    [Header("What and when")]
    public AudioClip[] clips;

    [Tooltip("Seconds between sounds (min, max).")]
    public Vector2 interval = new Vector2(2f, 8f);

    [Tooltip("The sound comes from a random point this far from the object, in metres.")]
    [Min(0f)] public float scatter = 1.5f;

    [Range(0f, 1f)] public float volume = 0.6f;
    public Vector2 pitch = new Vector2(0.92f, 1.08f);

    AudioSource _source;
    float _next;

    void OnEnable()
    {
        _source = GetComponent<AudioSource>();
        _next = Time.time + Random.Range(0f, interval.y);
    }

    void Update()
    {
        if (clips == null || clips.Length == 0 || Time.time < _next) return;
        _next = Time.time + Random.Range(interval.x, interval.y);
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        Vector3 at = transform.position + Random.insideUnitSphere * scatter;
        _source.pitch = Random.Range(pitch.x, pitch.y);
        AudioSource.PlayClipAtPoint(clip, at, volume);
    }
}
