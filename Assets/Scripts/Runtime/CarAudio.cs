using UnityEngine;

/// <summary>
/// A car's sound (clips from Tools/generate-car-audio.py). The engine is three recordings' worth of synthesis (idle, low, high) crossfaded and pitched by revs, louder with the throttle; over it
/// the tyres' roll and squeal, and wind with speed. In the seat it all sits close and the wind and road are muffled by the car; heard from outside it is a thing in the world. The sources are made
/// the first time it starts and turned off when it stops, so a parked car costs nothing.
/// </summary>
[DisallowMultipleComponent]
public class CarAudio : MonoBehaviour
{
    AudioSource _idle, _low, _high, _road, _skid, _wind, _oneShot;
    bool _on;
    float _flare;

    const float IdleRpm = 900f, LowRpm = 2500f, HighRpm = 5000f;

    AudioSource Source(string clip, float minDistance, float maxDistance)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = WorldSounds.Car(clip);
        s.loop = true; s.playOnAwake = false; s.dopplerLevel = 0f;
        s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = minDistance; s.maxDistance = maxDistance;
        s.spatialBlend = 1f; s.volume = 0f;
        if (s.clip != null) { s.time = Random.value * s.clip.length * 0.9f; s.Play(); }
        return s;
    }

    public void Switch(bool on)
    {
        if (on == _on) return;
        _on = on;
        if (on && _idle == null)
        {
            _idle = Source("CarEngine_Idle", 6f, 60f); _low = Source("CarEngine_Low", 7f, 80f); _high = Source("CarEngine_High", 8f, 110f);
            _road = Source("CarRoad", 3f, 40f); _skid = Source("CarSkid", 8f, 90f); _wind = Source("CarWind", 2f, 25f);
        }
        foreach (var s in new[] { _idle, _low, _high, _road, _skid, _wind })
        {
            if (s == null) continue;
            if (on) { if (!s.isPlaying) s.Play(); }
            else { s.volume = 0f; s.Stop(); }
        }
    }

    /// <summary>One frame of the car: revs, throttle 0 to 1, speed in metres a second, tyre scrub 0 to 1, and whether the listener is inside it.</summary>
    public void Drive(float rpm, float throttle, float speed, float scrub, bool inside, float dt)
    {
        if (!_on || _idle == null) return;
        _flare = Mathf.MoveTowards(_flare, throttle, dt * 3.5f);
        float loud = Mathf.Lerp(0.42f, 1f, _flare);
        float wI = 1f - Mathf.InverseLerp(IdleRpm * 1.1f, LowRpm * 0.85f, rpm);
        float wH = Mathf.InverseLerp(LowRpm * 1.3f, HighRpm * 0.95f, rpm);
        float wL = Mathf.Clamp01(1f - wI - wH);
        float master = inside ? 0.72f : 1f;
        _idle.volume = Mathf.Sqrt(wI) * 0.5f * master;
        _low.volume = Mathf.Sqrt(wL) * 0.62f * loud * master;
        _high.volume = Mathf.Sqrt(wH) * 0.72f * loud * master;
        _idle.pitch = rpm / IdleRpm; _low.pitch = rpm / LowRpm; _high.pitch = rpm / HighRpm;
        float blend = inside ? 0.25f : 1f;
        _idle.spatialBlend = _low.spatialBlend = _high.spatialBlend = _road.spatialBlend = _skid.spatialBlend = _wind.spatialBlend = blend;
        float s01 = Mathf.Clamp01(speed / 34f);
        _road.volume = (inside ? 0.28f : 0.5f) * Mathf.Sqrt(s01);
        _road.pitch = Mathf.Lerp(0.75f, 1.35f, s01);
        _skid.volume = Mathf.MoveTowards(_skid.volume, scrub * (inside ? 0.55f : 0.85f), dt * 5f);
        _wind.volume = (inside ? 0.28f : 0.14f) * s01 * s01;
        _wind.pitch = Mathf.Lerp(0.8f, 1.25f, s01);
    }

    /// <summary>The town's cars: one voice, a low hum that follows the speed, heard only close by. No revs or gears to work out, and fifteen of them.</summary>
    public void Traffic(float speed)
    {
        if (!_on || _low == null) return;
        float s01 = Mathf.Clamp01(speed / 15f);
        _idle.volume = (1f - s01) * 0.35f;
        _low.volume = s01 * 0.55f;
        _low.pitch = Mathf.Lerp(0.7f, 1.35f, s01);
        _road.volume = s01 * 0.3f;
    }

    public void Once(string clip, float volume, bool inside)
    {
        var c = WorldSounds.Car(clip);
        if (c == null) return;
        if (_oneShot == null)
        {
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false; _oneShot.dopplerLevel = 0f; _oneShot.rolloffMode = AudioRolloffMode.Linear; _oneShot.minDistance = 6f; _oneShot.maxDistance = 80f;
        }
        _oneShot.spatialBlend = inside ? 0.2f : 1f;
        _oneShot.PlayOneShot(c, volume);
    }
}
