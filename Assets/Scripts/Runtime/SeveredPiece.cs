using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A severed limb or head (EnemyBrutality.Sever) lying in the world: it lies for its life, then
/// sinks into the ground over a second or two and is gone. There are only ever so many: past the
/// cap the oldest goes at once, so a long fight with a shotgun cannot fill an arena with pieces
/// (each is a cut mesh and a physics body).
/// </summary>
public class SeveredPiece : MonoBehaviour
{
    const float SinkSeconds = 1.5f;

    float _until;
    bool _sinking;
    float _sinkStart;
    Vector3 _sinkFrom;

    static readonly List<SeveredPiece> Live = new List<SeveredPiece>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Live.Clear();

    /// <summary>Starts a piece's life; the oldest goes if there are already <paramref name="cap"/>.</summary>
    public static void Register(GameObject piece, float life, int cap)
    {
        var p = piece.AddComponent<SeveredPiece>();
        p._until = Time.time + life;
        Live.RemoveAll(x => x == null);
        while (Live.Count >= Mathf.Max(1, cap))
        {
            var oldest = Live[0];
            Live.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }
        Live.Add(p);
    }

    /// <summary>How many lie in the world now.</summary>
    public static int Count { get { Live.RemoveAll(x => x == null); return Live.Count; } }

    void Update()
    {
        if (!_sinking)
        {
            if (Time.time < _until) return;
            _sinking = true;
            _sinkStart = Time.time;
            _sinkFrom = transform.position;
            var body = GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; }
            return;
        }

        float t = (Time.time - _sinkStart) / SinkSeconds;
        transform.position = _sinkFrom + Vector3.down * (0.35f * Mathf.Clamp01(t));
        if (t >= 1f) Destroy(gameObject);
    }

    void OnDestroy() => Live.Remove(this);
}
