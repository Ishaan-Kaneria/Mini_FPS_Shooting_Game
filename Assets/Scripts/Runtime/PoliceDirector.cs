using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// The town's police, who come when the player is wanted (<see cref="WantedLevel"/>). A police car is one of the town's own cars in
/// white with a light bar and a siren, put on a traffic route that passes where he is, far enough up it to arrive from out of
/// sight; it runs the lights, drives round whatever stands in its lane, stops at the point nearest him and two officers get out and
/// fight. More stars, more cars. Out of their sight for long enough with no new crime, the level clears and they go.
///
/// Added by <see cref="TrafficDirector"/> to itself, so it exists only where there is a town with roads. It makes its cars from the
/// traffic's prefabs and its officers through <see cref="LevelManager.SpawnOutsider"/> (the arena's ordinary soldier: there is no
/// police model in the project yet), who are not part of the level's roster, score or stars.
/// </summary>
[RequireComponent(typeof(TrafficDirector))]
public class PoliceDirector : MonoBehaviour
{
    [Header("Response")]
    [Tooltip("Seconds between one police car being sent and the next.")]
    [Min(1f)] public float dispatchEvery = 7f;
    [Tooltip("Officers in each car.")]
    [Range(1, 4)] public int officersPerCar = 2;
    [Tooltip("How far up its road a police car starts, metres: far enough to arrive, not to appear.")]
    [Min(30f)] public float arriveFrom = 110f;
    [Tooltip("An officer this close to the player has him in sight: the wanted level does not cool down.")]
    [Min(5f)] public float sightRange = 45f;
    [Tooltip("Police this far from the player and out of view are taken away.")]
    [Min(60f)] public float leaveBeyond = 170f;

    class Unit
    {
        public TrafficCar car;
        public bool emptied;
        public PoliceLights lights;
    }

    readonly List<Unit> _units = new List<Unit>();
    readonly List<Health> _officers = new List<Health>();
    TrafficDirector _traffic;
    LevelManager _level;
    Transform _player;
    Camera _cam;
    float _next, _nextDispatch;
    GameObject _hud;
    Image[] _pips;

    void Start()
    {
        _traffic = GetComponent<TrafficDirector>();
        _level = FindAnyObjectByType<LevelManager>();
        var p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
        WantedLevel.Clear();
        WantedLevel.Changed += OnWanted;
    }

    void OnDestroy()
    {
        WantedLevel.Changed -= OnWanted;
        if (_hud != null) Destroy(_hud);
    }

    /// <summary>How many cars are out for this many stars.</summary>
    static int CarsFor(int stars) => stars <= 0 ? 0 : stars <= 1 ? 1 : stars <= 3 ? 2 : 3;

    void Update()
    {
        if (_player == null || Time.time < _next) return;
        float step = 0.5f;
        _next = Time.time + step;
        if (_cam == null) _cam = Camera.main;
        int stars = WantedLevel.Stars;

        // Officers: the dead forgotten, the far taken away, and whether any has him in sight.
        bool seen = false;
        for (int i = _officers.Count - 1; i >= 0; i--)
        {
            var o = _officers[i];
            if (o == null || o.IsDead) { _officers.RemoveAt(i); continue; }
            float d = Vector3.Distance(o.transform.position, _player.position);
            if (d < sightRange) seen = true;
            bool gone = (d > leaveBeyond || (stars == 0 && d > 40f)) && !InView(o.transform.position);
            if (gone) { o.Died -= OnOfficerDied; Destroy(o.gameObject); _officers.RemoveAt(i); }
        }
        WantedLevel.Tick(step, seen);

        int out_ = 0;
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            var u = _units[i];
            if (u.car == null) { _units.RemoveAt(i); continue; }
            float d = Vector3.Distance(u.car.transform.position, _player.position);
            if (u.car.Arrived && !u.emptied) { u.emptied = true; DropOfficers(u); }
            if (u.lights != null) u.lights.On = stars > 0 && !(u.car.TryGetComponent<CarWreck>(out var w) && w.Burnt);
            bool gone = (d > leaveBeyond || (stars == 0 && d > 50f)) && !InView(u.car.transform.position);
            if (gone) { Destroy(u.car.gameObject); _units.RemoveAt(i); continue; }
            // A car that has stopped far from where he now is does not count: another is sent to where he went.
            if (!u.car.Arrived || d < 90f) out_++;
        }
        if (stars > 0 && out_ < CarsFor(stars) && Time.time >= _nextDispatch)
        {
            _nextDispatch = Time.time + dispatchEvery;
            Dispatch();
        }
    }

    bool InView(Vector3 p)
    {
        if (_cam == null) return false;
        var v = _cam.WorldToViewportPoint(p + Vector3.up);
        return v.z > 0f && v.z < 300f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    void Dispatch()
    {
        var car = _traffic.SpawnToward(_player.position, 32f, arriveFrom, out int stop);
        if (car == null) return;
        car.police = true;
        car.policeStop = stop;
        var unit = new Unit { car = car, lights = Dress(car) };
        _units.Add(unit);
    }

    /// <summary>White paint, a light bar on the roof and a siren.</summary>
    PoliceLights Dress(TrafficCar car)
    {
        if (car.TryGetComponent<CarLook>(out var look) && _traffic.paints != null)
        {
            Material white = null;
            foreach (var m in _traffic.paints) if (m != null && m.name.Contains("White")) { white = m; break; }
            if (white != null) look.SetPaint(white);
        }
        float roof = 1.4f;
        foreach (var box in car.GetComponents<BoxCollider>()) if (!box.isTrigger) roof = Mathf.Max(roof, box.center.y + box.size.y * 0.5f);
        var bar = new GameObject("LightBar");
        bar.transform.SetParent(car.transform, false);
        bar.transform.localPosition = new Vector3(0f, roof + 0.06f, 0f);
        var lights = bar.AddComponent<PoliceLights>();
        lights.Build(car.halfWidth);
        return lights;
    }

    void DropOfficers(Unit u)
    {
        if (_level == null) return;
        var car = u.car;
        for (int i = 0; i < officersPerCar; i++)
        {
            float side = (i & 1) == 0 ? 1f : -1f;
            float along = i < 2 ? 0.4f : -0.9f;
            Vector3 door = car.transform.TransformPoint(new Vector3(side * (car.halfWidth + 0.9f), 0f, along));
            if (!NavMesh.SamplePosition(door, out var at, 2.5f, NavMesh.AllAreas)) continue;
            var officer = _level.SpawnOutsider(at.position);
            if (officer == null || !officer.TryGetComponent<Health>(out var health)) continue;
            officer.name = "PoliceOfficer";
            health.Died += OnOfficerDied;
            _officers.Add(health);
        }
    }

    void OnOfficerDied(Health h)
    {
        h.Died -= OnOfficerDied;
        if (CivilianCasualties.ByPlayer(h.LastDamage.source)) WantedLevel.Report(WantedLevel.Crime.KilledOfficer, h.transform.position);
    }

    // ---------------------------------------------------------------- the stars on the screen
    static readonly Color Lit = new Color(1f, 0.72f, 0.2f, 1f), Dim = new Color(1f, 1f, 1f, 0.16f);

    void OnWanted(int stars)
    {
        if (_hud == null)
        {
            if (stars <= 0) return;
            BuildHud();
        }
        _hud.SetActive(stars > 0);
        for (int i = 0; i < _pips.Length; i++) _pips[i].color = i < stars ? Lit : Dim;
    }

    /// <summary>Five pips under the top of the screen, amber for each star. Sized as a share of the screen's height, on its own canvas.</summary>
    void BuildHud()
    {
        _hud = new GameObject("WantedHud", typeof(Canvas));
        var canvas = _hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var row = new GameObject("Pips", typeof(RectTransform), typeof(AspectRatioFitter)).GetComponent<RectTransform>();
        row.SetParent(_hud.transform, false);
        row.anchorMin = new Vector2(0.5f, 0.842f); row.anchorMax = new Vector2(0.5f, 0.864f);
        row.offsetMin = row.offsetMax = Vector2.zero;
        const float gap = 0.6f, span = WantedLevel.Max + (WantedLevel.Max - 1) * gap;
        var fit = row.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        fit.aspectRatio = span;
        _pips = new Image[WantedLevel.Max];
        for (int i = 0; i < _pips.Length; i++)
        {
            var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            pip.SetParent(row, false);
            float from = i * (1f + gap) / span;
            pip.anchorMin = new Vector2(from, 0f); pip.anchorMax = new Vector2(from + 1f / span, 1f);
            pip.offsetMin = pip.offsetMax = Vector2.zero;
            _pips[i] = pip.GetComponent<Image>();
            _pips[i].raycastTarget = false;
        }
    }
}

/// <summary>A police car's light bar: a red and a blue lamp flashing in turn, each a small glowing block and a light, and the siren.</summary>
public class PoliceLights : MonoBehaviour
{
    Renderer _red, _blue;
    Light _redLight, _blueLight;
    AudioSource _siren;
    MaterialPropertyBlock _block;
    MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
    static AudioClip _wail;
    static Material _glow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _wail = null; _glow = null; }

    /// <summary>Flashing and wailing, or dark and silent.</summary>
    public bool On { get; set; } = true;

    public void Build(float halfWidth)
    {
        if (_glow == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _glow = new Material(shader) { name = "PoliceLamp" };
        }
        _red = Lamp("Red", -halfWidth * 0.34f, out _redLight, new Color(1f, 0.08f, 0.05f));
        _blue = Lamp("Blue", halfWidth * 0.34f, out _blueLight, new Color(0.1f, 0.3f, 1f));
        _siren = gameObject.AddComponent<AudioSource>();
        _siren.clip = Wail();
        _siren.loop = true;
        _siren.spatialBlend = 1f;
        _siren.rolloffMode = AudioRolloffMode.Linear;
        _siren.minDistance = 12f; _siren.maxDistance = 160f;
        _siren.volume = 0.5f;
        _siren.dopplerLevel = 0.6f;
    }

    Renderer Lamp(string name, float x, out Light light, Color colour)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        go.transform.localScale = new Vector3(0.42f, 0.11f, 0.16f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = _glow;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lamp = new GameObject(name + "Light");
        lamp.transform.SetParent(transform, false);
        lamp.transform.localPosition = new Vector3(x, 0.25f, 0f);
        light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = colour;
        light.range = 14f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        Tint(r, colour * 0.25f);
        return r;
    }

    void Tint(Renderer r, Color c)
    {
        c.a = 1f;
        r.GetPropertyBlock(Block);
        Block.SetColor(BaseColorId, c);
        Block.SetColor(ColorId, c);
        r.SetPropertyBlock(Block);
    }

    void Update()
    {
        if (_red == null) return;
        // Three flashes a second, red then blue.
        bool red = On && Mathf.Repeat(Time.time * 3f, 1f) < 0.5f, blue = On && !red;
        Tint(_red, new Color(1f, 0.08f, 0.05f) * (red ? 3f : 0.25f));
        Tint(_blue, new Color(0.1f, 0.3f, 1f) * (blue ? 3f : 0.25f));
        _redLight.intensity = red ? 6f : 0f;
        _blueLight.intensity = blue ? 6f : 0f;
        if (_siren != null)
        {
            if (On && !_siren.isPlaying) _siren.Play();
            else if (!On && _siren.isPlaying) _siren.Stop();
        }
    }

    /// <summary>The siren, worked out once: a tone rising and falling between 650 and 1300 Hz every second and a half.</summary>
    static AudioClip Wail()
    {
        if (_wail != null) return _wail;
        const int rate = 22050;
        const float seconds = 1.5f;
        int n = Mathf.RoundToInt(rate * seconds);
        var data = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float sweep = 0.5f - 0.5f * Mathf.Cos(t * 2f * Mathf.PI);
            float hz = Mathf.Lerp(650f, 1300f, sweep);
            phase += 2.0 * System.Math.PI * hz / rate;
            float s = (float)System.Math.Sin(phase);
            // A little of the next harmonic: a horn, not a whistle.
            data[i] = (s * 0.7f + (float)System.Math.Sin(phase * 2.0) * 0.2f) * 0.6f;
        }
        _wail = AudioClip.Create("PoliceSiren", n, 1, rate, false);
        _wail.SetData(data, 0);
        return _wail;
    }
}
