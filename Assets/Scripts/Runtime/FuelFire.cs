using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Burning fuel: a drum alight, a pool left by a burst, a pump spraying (ExplosiveProp). Flames and smoke as particles, a
/// flickering warm light, and damage to whatever stands in it; another explosive in the fire is lit by it (the chain by
/// heat, after the chain by blast). Made in play, nothing of it saved.
/// </summary>
public class FuelFire : MonoBehaviour
{
    float _radius, _until, _dps, _nextTick, _seed;
    GameObject _source;
    ParticleSystem _flames, _smoke;
    Light _light;
    bool _stopping;
    Collider[] _hits;
    HashSet<Health> _doneSet;
    // Made on demand, never in a field initialiser (domain reload is off: CLAUDE.md, "Statics and reloads").
    HashSet<Health> Done => _doneSet ??= new HashSet<Health>();

    /// <summary>Starts a fire at <paramref name="at"/>; following <paramref name="follow"/> if given (a burning drum).</summary>
    public static FuelFire Spawn(Vector3 at, float radius, float seconds, float damagePerSecond, Material fire, Material smoke,
                                 GameObject source, Transform follow)
    {
        var go = new GameObject("FuelFire");
        if (follow != null) go.transform.SetParent(follow, true);
        go.transform.position = at;
        var f = go.AddComponent<FuelFire>();
        f._radius = Mathf.Max(0.2f, radius);
        f._until = Time.time + Mathf.Max(0.5f, seconds);
        f._dps = damagePerSecond;
        f._source = source;
        f._seed = Random.value * 100f;
        f._flames = MakeParticles(go.transform, "Flames", fire, f._radius, true);
        f._smoke = MakeParticles(go.transform, "Smoke", smoke, f._radius, false);
        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = Vector3.up * (0.6f + f._radius * 0.4f);
        f._light = lightGo.AddComponent<Light>();
        f._light.type = LightType.Point;
        f._light.color = new Color(1f, 0.55f, 0.22f);
        f._light.range = 4f + f._radius * 4f;
        f._light.intensity = 3f;
        f._light.shadows = LightShadows.None;
        return f;
    }

    static ParticleSystem MakeParticles(Transform parent, string name, Material material, float radius, bool flames)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);    // emit upward
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = flames ? new ParticleSystem.MinMaxCurve(0.45f, 0.9f) : new ParticleSystem.MinMaxCurve(3f, 5.5f);
        main.startSpeed = flames ? new ParticleSystem.MinMaxCurve(1.2f, 2.6f) : new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = flames ? new ParticleSystem.MinMaxCurve(0.5f * radius + 0.3f, 1.1f * radius + 0.4f)
                                : new ParticleSystem.MinMaxCurve(1.2f * radius + 0.8f, 2.2f * radius + 1.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = flames ? new Color(1f, 0.75f, 0.45f, 1f) : new Color(0.12f, 0.11f, 0.10f, 0.55f);
        main.gravityModifier = flames ? -0.15f : -0.05f;
        main.maxParticles = flames ? 80 : 60;
        var emission = ps.emission;
        emission.rateOverTime = flames ? 26f + radius * 18f : 6f + radius * 4f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * (flames ? 0.8f : 0.6f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var g = new Gradient();
        if (flames)
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.5f), new GradientColorKey(new Color(0.4f, 0.12f, 0.04f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        else
            g.SetKeys(new[] { new GradientColorKey(new Color(0.18f, 0.16f, 0.14f), 0f), new GradientColorKey(new Color(0.35f, 0.33f, 0.31f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colour.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, flames ? AnimationCurve.Linear(0f, 1f, 1f, 0.35f) : AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = flames ? 0.6f : 0.4f;
        noise.frequency = 0.8f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (material != null) r.sharedMaterial = material;
        r.sortingFudge = flames ? -1f : 0f;
        ps.Play();
        return ps;
    }

    /// <summary>Stops feeding the fire: the last flames and smoke drift off, then it is gone.</summary>
    public void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        if (_flames != null) _flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        transform.SetParent(null, true);
        Destroy(gameObject, 6f);
    }

    void Update()
    {
        if (_light != null)
        {
            float fade = _stopping ? Mathf.MoveTowards(_light.intensity, 0f, Time.deltaTime * 4f) : 2.4f + 1.4f * Mathf.PerlinNoise(_seed, Time.time * 7f);
            _light.intensity = fade;
        }
        if (_stopping) return;
        if (Time.time >= _until) { Stop(); return; }
        if (Time.time < _nextTick) return;
        _nextTick = Time.time + 0.5f;
        Burn(0.5f);
    }

    void Burn(float seconds)
    {
        _hits ??= new Collider[48];
        int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.5f, _radius + 0.3f, _hits, ~0, QueryTriggerInteraction.Ignore);
        var done = Done;
        done.Clear();
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i] != null ? _hits[i].GetComponentInParent<Health>() : null;
            if (h == null || h.IsDead || !done.Add(h)) continue;
            if (transform.parent != null && h.transform == transform.parent) continue;   // the drum that is burning
            // Another explosive in the fire is lit by it.
            if (h.TryGetComponent<ExplosiveProp>(out var other)) { other.Ignite(); continue; }
            if (_dps <= 0f) continue;
            Vector3 up = Vector3.up;
            var info = new DamageInfo(_dps * seconds, h.transform.position, up, up, _source);
            h.ApplyDamage(info);
        }
    }
}
