using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A prop that can be kicked, shot, rammed or blown about (Docs/DESERT_BUGS_AND_PERFORMANCE.md W1). Kinematic until something
/// hits it and again once it has come to rest, so a town full of them costs the physics nothing: only a few are ever awake,
/// and past <see cref="MaxAwake"/> the longest awake is put to rest. The builder gives each its mass by what it is
/// (FPSKitTownAssets.LooseMassOf). A fuel drum is one too: moving it does not set it off, its ExplosiveProp answers damage alone.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class LooseProp : MonoBehaviour, IWorldHit
{
    /// <summary>The most moving at once.</summary>
    const int MaxAwake = 60;

    public enum Stuff { Metal, Wood, Plastic }
    [Tooltip("What it sounds like when it lands.")]
    public Stuff stuff = Stuff.Metal;
    [Tooltip("Slowest landing, metres a second, that is heard.")]
    [Min(0f)] public float heardFrom = 1.2f;

    static readonly List<LooseProp> _awake = new List<LooseProp>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _awake.Clear();

    Rigidbody _body;
    float _restFrom, _soundAt;
    bool _moving;

    Rigidbody Body => _body != null ? _body : _body = GetComponent<Rigidbody>();

    void Awake()
    {
        Body.isKinematic = true;
        Body.interpolation = RigidbodyInterpolation.None;
        enabled = false;
    }

    void OnDisable() { _awake.Remove(this); _moving = false; }

    /// <summary>Free to move; it stays awake until it has settled.</summary>
    public void Wake()
    {
        if (_moving) return;
        if (_awake.Count >= MaxAwake) { var oldest = _awake[0]; oldest.Rest(); }
        _moving = true;
        _awake.Add(this);
        Body.isKinematic = false;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Body.WakeUp();
        _restFrom = Time.time + 1f;
        enabled = true;
    }

    void Rest()
    {
        _moving = false;
        _awake.Remove(this);
        Body.interpolation = RigidbodyInterpolation.None;
        Body.isKinematic = true;
        enabled = false;
    }

    void FixedUpdate()
    {
        if (Time.time < _restFrom) return;
        if (Body.linearVelocity.sqrMagnitude < 0.01f && Body.angularVelocity.sqrMagnitude < 0.04f) Rest();
    }

    public void OnWorldHit(in WorldHitInfo hit)
    {
        Wake();
        Body.AddForceAtPosition(hit.direction.normalized * hit.impulse, hit.point, ForceMode.Impulse);
        Sound(hit.impulse / Mathf.Max(1f, Body.mass));
    }

    void OnCollisionEnter(Collision c)
    {
        // A car meeting it at rest: it gives way instead of stopping the car like a wall.
        if (!_moving && c.rigidbody != null && !c.rigidbody.isKinematic)
        {
            Wake();
            Body.AddForce(c.impulse, ForceMode.Impulse);
        }
        if (_moving) Sound(c.relativeVelocity.magnitude);
    }

    void Sound(float speed)
    {
        if (speed < heardFrom || Time.time < _soundAt) return;
        _soundAt = Time.time + 0.12f;
        var clip = WorldSounds.Prop(stuff);
        if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, Mathf.Clamp01(speed / 6f));
    }
}
