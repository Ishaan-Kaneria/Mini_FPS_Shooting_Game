using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A prop that can be kicked, shot, rammed or blown about (Docs/DESERT_BUGS_AND_PERFORMANCE.md W1). Kinematic until something
/// hits it and again once it has come to rest, so a town full of them costs the physics nothing: only a few are ever awake,
/// and past <see cref="MaxAwake"/> the longest awake is put to rest. The builder gives each its mass by what it is
/// (FPSKitTownAssets.LooseMassOf). A fuel drum is one too: moving it does not set it off, its ExplosiveProp answers damage alone.
///
/// An anchored one (<see cref="breakSpeed"/> over 0: a lamp post, a signal or sign pole, FPSKitPoles) is bolted down: it stands
/// like a wall to people, kicks, rounds, blasts and a car creeping into it, and goes over only to a car at
/// <see cref="breakSpeed"/> or more (his note 2026-11-16: poles and bins "act as a fixed object" when driven into). Once down it
/// is loose like any other; one that was woken and never moved stands anchored again.
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
    [Tooltip("Speed, metres a second, a car must be doing to knock it down (a lamp post, a sign pole). Below it, and to kicks, rounds and blasts, it stands as if fixed. 0 = loose: anything moves it.")]
    [Min(0f)] public float breakSpeed = 0f;

    static readonly List<LooseProp> _awake = new List<LooseProp>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _awake.Clear();

    Rigidbody _body;
    float _restFrom, _soundAt;
    bool _moving, _down;
    Vector3 _stoodAt;

    /// <summary>Bolted down and still standing: only a fast enough car moves it.</summary>
    public bool Anchored => breakSpeed > 0f && !_down;

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
        if (_moving || Anchored) return;
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

    /// <summary>
    /// A car coming at it at <paramref name="speed"/> metres a second (DrivableCar.WakeAhead, a collision): an anchored prop gives way
    /// only at its <see cref="breakSpeed"/>. True when it is now free to move.
    /// </summary>
    public bool StruckBy(float speed)
    {
        if (Anchored)
        {
            if (speed < breakSpeed) return false;
            _down = true;
            _stoodAt = transform.position;
        }
        Wake();
        return _moving;
    }

    void Rest()
    {
        // Woken for a car that then missed it: still upright where it stood, so still bolted down.
        if (_down && breakSpeed > 0f && Vector3.Dot(transform.up, Vector3.up) > 0.995f && (transform.position - _stoodAt).sqrMagnitude < 0.0025f) _down = false;
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
        if (Anchored) { Sound(hit.impulse / Mathf.Max(1f, Body.mass) * 4f); return; }      // a kick or a round rings it, no more
        Wake();
        Body.AddForceAtPosition(hit.direction.normalized * hit.impulse, hit.point, ForceMode.Impulse);
        Sound(hit.impulse / Mathf.Max(1f, Body.mass));
    }

    void OnCollisionEnter(Collision c)
    {
        // A car meeting it at rest: it gives way instead of stopping the car like a wall.
        if (!_moving && c.rigidbody != null && !c.rigidbody.isKinematic && StruckBy(c.relativeVelocity.magnitude))
            Body.AddForce(c.impulse, ForceMode.Impulse);
        if (_moving) Sound(c.relativeVelocity.magnitude);
    }

    void Sound(float speed)
    {
        if (speed < heardFrom || Time.time < _soundAt) return;
        _soundAt = Time.time + 0.12f;
        var clip = WorldSounds.Prop(stuff);
        if (clip != null) OneShotAudio.Play(clip, transform.position, Mathf.Clamp01(speed / 6f));
    }
}
