using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The map in the corner of the HUD: the level seen from above, turning under a player
/// arrow that stays put, with every live enemy on it in its own colour.
///
/// It is drawn rather than rendered. A second camera pointed at the floor would be the
/// obvious build, and it is the wrong one here: it costs a full extra pass over the
/// arena every frame on a platform where the whole game has to fit in a browser tab,
/// and what it produces is a top-down photograph of grey boxes on a grey floor -- an
/// honest picture that answers nothing. This walks the geometry once at the start of
/// the level, keeps each solid thing as a footprint, and redraws those footprints as
/// flat shapes. Cover and walls separate by tone, enemies keep the colour they are
/// wearing, and the whole thing is a few dozen UI quads.
///
/// Reading the world is also what makes it work in a level the kit never built. There
/// is no map to author and nothing to keep in sync: <see cref="AddGameplayToCurrentScene"/>
/// drops this into somebody else's level and the map is correct on the first frame.
/// Where the geometry cannot say what something is -- water, a bridge, a way out --
/// <see cref="MinimapMarker"/> says it instead.
///
/// Nothing here is clickable. Every graphic it makes has its raycast target off, so the
/// map cannot swallow a click meant for the pause menu underneath it.
///
/// Every reference is optional. With none of them wired it finds the player by tag and
/// builds its own layers; with <see cref="view"/> missing it switches itself off rather
/// than throwing once per frame.
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("Centre of the map. Found by the Player tag when empty.")]
    public Transform player;

    [Tooltip("What decides which way is up while the map turns. The camera, normally, " +
             "so the map agrees with what is on screen. Falls back to the player.")]
    public Transform facing;

    [Tooltip("Only used to stop drawing once the level is over. Optional.")]
    public LevelManager levelManager;

    [Header("Frame")]
    [Tooltip("The square the map is drawn inside. Everything is clipped to it, and its " +
             "shorter side is what the range below is measured against.")]
    public RectTransform view;

    [Tooltip("The arrow at the centre. Left alone while the map turns under it -- that " +
             "is the whole reason a rotating map is easier to read than a fixed one.")]
    public RectTransform playerMarker;

    [Tooltip("A pip that rides the rim pointing at world north, so a turning map still " +
             "has one fixed thing in it. Optional.")]
    public RectTransform northPip;

    [Header("Range")]
    [Tooltip("Metres of world between the player and the edge of the map. Bigger sees " +
             "more of the level and less of the fight.")]
    [Min(5f)] public float worldRadius = 62f;

    [Tooltip("Turn the map so the player's forward is always up. Off pins north up and " +
             "turns the arrow instead.")]
    public bool rotateWithPlayer = true;

    [Tooltip("Hold an enemy that is off the map at the rim, dimmed, instead of dropping " +
             "it. The direction a threat is coming from is worth more than its distance.")]
    public bool clampOffMapEnemies = true;

    [Header("What Counts As A Structure")]
    [Tooltip("Layers the map takes its footprints from. An object carrying a " +
             "MinimapMarker is drawn whatever layer it is on.")]
    public LayerMask structureLayers = ~0;


    [Tooltip("Ignore anything smaller than this along its longest side, in metres. A " +
             "crate is three pixels on a map this wide -- too small to be read as cover " +
             "and numerous enough to bury the walls that can be. Measured on the long " +
             "side, so a cover wall survives however thin it is.")]
    [Min(0f)] public float minStructureSize = 2f;

    [Tooltip("A structure at least this tall reads as a wall rather than as cover. This " +
             "is the whole difference between 'you cannot go there' and 'you can hide " +
             "behind that', which is most of what a map is for.")]
    [Min(0.5f)] public float wallHeight = 2.6f;

    [Tooltip("Anything flatter than this and wider than the area below is the ground, " +
             "not a structure. Drawn, it would be one shape covering the whole map.")]
    [Min(0f)] public float groundThickness = 1.6f;

    [Tooltip("Square metres of footprint above which a flat object counts as ground. " +
             "A perimeter wall is long but thin, so area is what tells them apart.")]
    [Min(1f)] public float groundArea = 900f;

    [Tooltip("Ceiling on how many footprints are kept. Past it the biggest are kept and " +
             "the rest dropped: on a map this size the small ones were illegible anyway.")]
    [Min(8)] public int maxStructures = 260;

    [Header("Colours")]
    public Color wallColor = new Color(0.62f, 0.66f, 0.74f, 0.9f);

    [Tooltip("Dimmer than a wall on purpose. Cover is worth knowing about and worth " +
             "less than the shape of the building, and a map where everything is equally " +
             "bright is a map with nothing to look at first.")]
    public Color coverColor = new Color(0.38f, 0.42f, 0.49f, 0.7f);

    [Tooltip("Used for an enemy whose archetype has no colour of its own.")]
    public Color enemyColor = new Color(0.95f, 0.33f, 0.28f);

    [Tooltip("Ignores the archetype, because the boss is the level and has to be the " +
             "loudest thing on the map.")]
    public Color bossColor = new Color(1f, 0.79f, 0.2f);

    public Color healthPickupColor = new Color(0.45f, 0.95f, 0.5f);
    public Color shieldPickupColor = new Color(0.4f, 0.7f, 1f);
    public Color ammoPickupColor = new Color(1f, 0.82f, 0.3f);

    [Header("Blips")]
    [Tooltip("Sprite for the round pips -- enemies, pickups, anything alive. Structures " +
             "are drawn as bare rectangles, so a circle is what tells a body apart from " +
             "a building at a glance rather than by colour alone.")]
    public Sprite blipSprite;


    [Min(0.5f)] public float enemyRadius = 2.1f;
    [Min(0.5f)] public float eliteRadius = 2.8f;
    [Min(0.5f)] public float bossRadius = 4f;
    [Min(0.3f)] public float pickupRadius = 1.6f;

    [Tooltip("How far the boss pip swells and shrinks, as a fraction of its size. Zero " +
             "holds it still.")]
    [Range(0f, 1f)] public float bossPulse = 0.22f;

    [Header("Budget")]
    [Tooltip("Seconds between sweeps for enemies and pickups. Their positions are read " +
             "every frame -- this is only how often the map notices a new one, and a " +
             "third of a second of lag on something that just spawned is invisible.")]
    [Min(0.05f)] public float actorScanInterval = 0.33f;

    // ======================================================================
    // Runtime state.
    //
    // Every one of these is a plain C# object, and Unity's mid-play reload backup
    // carries a private field across only if its type is serializable -- so all of
    // them come back null after a recompile during play while the serialized fields
    // above survive. Nothing may assume Awake ran; EnsureRuntimeState is what puts
    // them back, and it runs before the draw every frame. See the class docs on
    // EnemyAI.Block for the same rule applied to a property block.
    // ======================================================================
    struct Piece
    {
        public Transform follow;   // only set when the marker says it moves
        public Vector2 centre;
        public Vector2 half;
        public float yaw;
        public Color tone;
        public int order;
        public bool dot;
    }

    List<Piece> _pieces;
    List<Image> _terrainPool;
    List<Image> _blipPool;
    List<EnemyAI> _enemies;
    List<Pickup> _pickups;

    RectTransform _terrainRoot;
    RectTransform _blipRoot;

    float _nextActorScan;
    bool _scanned;

    Transform Player
    {
        get
        {
            if (player != null) return player;

            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) player = tagged.transform;

            return player;
        }
    }

    // ======================================================================
    void Awake()
    {
        if (view == null)
        {
            // Nothing to draw into. Said once, here, rather than skipped silently every
            // frame: a map that quietly does nothing looks exactly like a map that is
            // broken, and this is the one moment there is somewhere to say so.
            Debug.LogWarning("[Minimap] No view rect assigned -- the map is switched off.", this);
            enabled = false;
            return;
        }

        if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();
    }

    void OnEnable()
    {
        // Cheap, and it is what makes the map correct after a level restart: the old
        // level's footprints belong to objects that no longer exist.
        _scanned = false;
        _nextActorScan = 0f;
    }

    // ======================================================================
    void LateUpdate()
    {
        if (view == null) return;

        EnsureRuntimeState();

        var origin = Player;
        if (origin == null) return;

        // Scanned here rather than in Start so the level's own geometry -- anything a
        // builder or a spawner made on the first frame -- is already in the scene.
        if (!_scanned) ScanStructures();

        if (Time.unscaledTime >= _nextActorScan)
        {
            _nextActorScan = Time.unscaledTime + actorScanInterval;
            ScanActors();
        }

        Vector3 p = origin.position;
        var centre = new Vector2(p.x, p.z);

        float yaw = rotateWithPlayer ? YawOf(facing != null ? facing : origin) : 0f;
        float rad = yaw * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

        Rect r = view.rect;
        float halfPx = Mathf.Min(r.width, r.height) * 0.5f;
        float scale = halfPx / Mathf.Max(worldRadius, 0.01f);

        int terrain = DrawStructures(centre, cos, sin, yaw, scale, halfPx);
        Hide(_terrainPool, terrain);

        int blips = DrawActors(centre, cos, sin, scale, halfPx);
        Hide(_blipPool, blips);

        if (playerMarker != null)
            playerMarker.localRotation = Quaternion.Euler(0f, 0f, rotateWithPlayer ? 0f : -yaw);

        if (northPip != null)
        {
            // North is world +Z put through the same rotation as everything else, so the
            // pip and the map can never disagree about which way the level is facing.
            var dir = new Vector2(-sin, cos);
            northPip.anchoredPosition = dir * (halfPx - 10f);
        }
    }

    // ======================================================================
    // Drawing
    // ======================================================================
    int DrawStructures(Vector2 centre, float cos, float sin, float yaw, float scale, float halfPx)
    {
        int used = 0;

        // The diagonal, not the radius: a footprint whose centre is off the map can
        // still have a corner on it, and dropping it makes a wall end in mid-air.
        float reach = worldRadius * 1.45f;

        for (int i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];

            if (piece.follow != null)
            {
                Vector3 wp = piece.follow.position;
                piece.centre = new Vector2(wp.x, wp.z);
                piece.yaw = piece.follow.eulerAngles.y;
            }

            Vector2 d = piece.centre - centre;
            float radius = piece.half.magnitude;
            if (d.sqrMagnitude > (reach + radius) * (reach + radius)) continue;

            var image = Take(_terrainPool, _terrainRoot, used);
            if (image == null) break;

            var rect = image.rectTransform;
            rect.anchoredPosition = Project(d, cos, sin) * scale;
            rect.sizeDelta = piece.half * 2f * scale;
            rect.localRotation = Quaternion.Euler(0f, 0f, yaw - piece.yaw);

            image.color = piece.tone;
            image.enabled = true;
            used++;

            if (used >= maxStructures) break;
        }

        return used;
    }

    int DrawActors(Vector2 centre, float cos, float sin, float scale, float halfPx)
    {
        int used = 0;
        bool finished = levelManager != null && levelManager.IsFinished;
        if (finished) return 0;

        for (int i = 0; i < _pickups.Count; i++)
        {
            var pickup = _pickups[i];
            if (pickup == null || !pickup.isActiveAndEnabled) continue;

            Vector3 wp = pickup.transform.position;
            Color tint = pickup.kind == Pickup.Kind.Health ? healthPickupColor
                       : pickup.kind == Pickup.Kind.Shield ? shieldPickupColor
                       : ammoPickupColor;

            Blip(new Vector2(wp.x, wp.z) - centre, cos, sin, scale, halfPx,
                 pickupRadius, tint, ref used, clamp: false);
        }

        for (int i = 0; i < _enemies.Count; i++)
        {
            var ai = _enemies[i];
            if (ai == null || !ai.isActiveAndEnabled) continue;
            if (ai.CurrentState == EnemyAI.State.Dead) continue;

            var type = ai.archetype;
            bool boss = type != null && type.role == EnemyArchetype.Role.Boss;
            bool elite = type != null && type.role == EnemyArchetype.Role.Elite;

            Color tint = boss ? bossColor
                       : type != null ? Legible(type.bodyColor)
                       : enemyColor;

            float size = boss ? bossRadius : elite ? eliteRadius : enemyRadius;

            // The boss breathes. It is the one thing on the map worth finding, and a
            // pip that moves is found without being looked for.
            if (boss && bossPulse > 0f)
                size *= 1f + Mathf.Sin(Time.unscaledTime * 3.4f) * bossPulse;

            Vector3 wp = ai.transform.position;
            Blip(new Vector2(wp.x, wp.z) - centre, cos, sin, scale, halfPx,
                 size, tint, ref used, clamp: clampOffMapEnemies);
        }

        return used;
    }

    /// <summary>
    /// Places one round pip. Returns false when the thing was off the map and was not
    /// worth holding at the rim.
    /// </summary>
    bool Blip(Vector2 delta, float cos, float sin, float scale, float halfPx,
              float metres, Color tint, ref int used, bool clamp)
    {
        Vector2 uv = Project(delta, cos, sin) * scale;
        float limit = halfPx - metres * scale;

        if (uv.magnitude > limit)
        {
            if (!clamp) return false;

            uv = uv.normalized * Mathf.Max(limit, 0f);

            // Dimmed, because a pip on the rim is a bearing and not a position, and one
            // drawn as brightly as the real thing is a lie about where that enemy is.
            tint.a *= 0.55f;
        }

        var image = Take(_blipPool, _blipRoot, used);
        if (image == null) return false;

        var rect = image.rectTransform;
        rect.anchoredPosition = uv;
        rect.sizeDelta = Vector2.one * (metres * 2f * scale);
        rect.localRotation = Quaternion.identity;

        image.color = tint;
        image.enabled = true;
        used++;

        return true;
    }

    /// <summary>
    /// World XZ, relative to the player, turned so the player's forward points up.
    ///
    /// Unity turns +Z into (sin y, cos y), so the rotation that puts forward at the top
    /// of the map is by the yaw itself rather than by its negative. Getting that sign
    /// wrong gives a map that is right whenever the player faces a cardinal direction
    /// and mirrored the rest of the time, which is a very easy thing not to notice.
    /// </summary>
    static Vector2 Project(Vector2 delta, float cos, float sin)
        => new Vector2(delta.x * cos - delta.y * sin,
                       delta.x * sin + delta.y * cos);

    static float YawOf(Transform t)
    {
        Vector3 f = t.forward;
        f.y = 0f;

        // Straight up or straight down: the forward vector says nothing about yaw, so
        // take it from the transform instead of normalising a zero vector into NaN.
        return f.sqrMagnitude < 0.0001f ? t.eulerAngles.y
                                        : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Lifts a colour to something that reads on a dark panel while keeping its hue.
    /// The archetype's body colour is the point -- the player learns that the pale one
    /// is the fast one -- but a deep maroon at map size is a black dot.
    /// </summary>
    static Color Legible(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        var lifted = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f + 0.15f), Mathf.Max(v, 0.78f));
        lifted.a = c.a <= 0f ? 1f : c.a;
        return lifted;
    }

    // ======================================================================
    // Scanning
    // ======================================================================
    /// <summary>
    /// Walks the scene once and keeps the footprint of everything solid.
    ///
    /// Footprints come from the renderer's local bounds put through its transform, not
    /// from world bounds. A world bounding box is axis aligned, so a wall laid at
    /// forty-five degrees comes back as a square twice its size -- and half of what a
    /// generated arena builds is turned.
    /// </summary>
    public void ScanStructures()
    {
        EnsureRuntimeState();

        _pieces.Clear();
        _scanned = true;

        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude,
                                                        FindObjectsSortMode.None);

        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;

            var marker = renderer.GetComponentInParent<MinimapMarker>();
            if (marker != null && marker.style == MinimapMarker.Style.Hidden) continue;

            // A marker is an instruction, so it overrides the layer filter as well as
            // everything below. Water is not on the environment layer and should be.
            if (marker == null && (structureLayers.value & (1 << renderer.gameObject.layer)) == 0)
                continue;

            // Anything that can be hurt or picked up is an actor, and actors are drawn
            // live further down. Caught here as well as by the layer mask because this
            // scan happens once: an enemy standing in it at the time would be baked into
            // the map as a wall and stay there for the rest of the level.
            if (marker == null && (renderer.GetComponentInParent<Health>() != null ||
                                   renderer.GetComponentInParent<Pickup>() != null))
                continue;

            var t = renderer.transform;
            Bounds local = renderer.localBounds;
            Vector3 scale = t.lossyScale;

            float hx = Mathf.Abs(local.extents.x * scale.x);
            float hz = Mathf.Abs(local.extents.z * scale.z);
            float height = Mathf.Abs(local.size.y * scale.y);

            if (marker == null)
            {
                if (Mathf.Max(hx, hz) * 2f < minStructureSize) continue;

                // The floor. Flat and enormous, and drawn it would be one shape covering
                // the entire map. A perimeter wall is just as long but far thinner, which
                // is why the test is on area and not on either side.
                if (height <= groundThickness && hx * hz * 4f >= groundArea) continue;
            }

            Vector3 world = t.TransformPoint(local.center);
            bool dot = marker != null && marker.style == MinimapMarker.Style.Dot;
            var footprint = new Vector2(world.x, world.z);

            // A stack of crates is three cubes standing on the same square metre. From
            // above they are one shape, so keeping all three means drawing the same
            // square three times, and the arena is built out of stacks -- it was most of
            // what the map was spending itself on.
            if (marker == null && Covered(footprint, hx, hz)) continue;

            _pieces.Add(new Piece
            {
                follow = marker != null && marker.moves ? t : null,
                centre = footprint,
                half = dot ? Vector2.one * marker.dotRadius : new Vector2(hx, hz),
                yaw = t.eulerAngles.y,
                tone = marker != null ? marker.color
                     : height >= wallHeight ? wallColor : coverColor,
                order = marker != null ? marker.order : (height >= wallHeight ? 1 : 0),
                dot = dot
            });
        }

        // Over budget, the biggest survive: the ones worth navigating by are the ones
        // big enough to see, and a hundred crates are one texture of noise either way.
        if (_pieces.Count > maxStructures)
        {
            _pieces.Sort((a, b) => (b.half.x * b.half.y).CompareTo(a.half.x * a.half.y));
            _pieces.RemoveRange(maxStructures, _pieces.Count - maxStructures);
        }

        // Low things first so a wall lands on top of the cover in front of it, and a
        // marker's own order on top of that -- a bridge has to draw over its river.
        _pieces.Sort((a, b) => a.order != b.order
            ? a.order.CompareTo(b.order)
            : (b.half.x * b.half.y).CompareTo(a.half.x * a.half.y));
    }

    /// <summary>
    /// True when something already kept sits on this spot and is at least as big, so
    /// drawing this one would add a shape nobody can see.
    /// </summary>
    bool Covered(Vector2 centre, float hx, float hz)
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            if (piece.dot || piece.follow != null) continue;

            if ((piece.centre - centre).sqrMagnitude > 0.36f) continue;
            if (piece.half.x >= hx - 0.05f && piece.half.y >= hz - 0.05f) return true;
        }

        return false;
    }

    void ScanActors()
    {
        _enemies.Clear();
        _enemies.AddRange(FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude,
                                                     FindObjectsSortMode.None));

        _pickups.Clear();
        _pickups.AddRange(FindObjectsByType<Pickup>(FindObjectsInactive.Exclude,
                                                    FindObjectsSortMode.None));
    }

    // ======================================================================
    // Pools
    // ======================================================================
    Image Take(List<Image> pool, RectTransform parent, int index)
    {
        if (pool == null || parent == null) return null;

        while (pool.Count <= index)
        {
            var go = new GameObject("Blip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.GetComponent<Image>();
            if (parent == _blipRoot) image.sprite = blipSprite;

            // Nothing the map draws may be clickable. The pause menu and the results
            // screen sit on this same canvas, and a corner full of invisible raycast
            // targets is a corner where the wrong thing gets clicked.
            image.raycastTarget = false;
            image.enabled = false;

            pool.Add(image);
        }

        return pool[index];
    }

    static void Hide(List<Image> pool, int from)
    {
        if (pool == null) return;

        for (int i = from; i < pool.Count; i++)
            if (pool[i] != null) pool[i].enabled = false;
    }

    /// <summary>
    /// Rebuilds anything Unity could not carry across a mid-play recompile.
    ///
    /// The lists here are plain C# objects, so the reload backup drops them while the
    /// serialized fields beside them survive -- the asymmetry that makes this bug class
    /// look like a null reference out of nowhere in code that has been running for a
    /// minute. The two layer objects are real GameObjects and do survive, so the pools
    /// are refilled from the children that are already there rather than by making a
    /// second set on top of the first.
    /// </summary>
    void EnsureRuntimeState()
    {
        if (view == null) return;

        _pieces ??= new List<Piece>();
        _enemies ??= new List<EnemyAI>();
        _pickups ??= new List<Pickup>();

        if (_terrainRoot == null) _terrainRoot = Layer("Terrain", 0);
        if (_blipRoot == null) _blipRoot = Layer("Blips", 1);

        if (_terrainPool == null) _terrainPool = Adopt(_terrainRoot);
        if (_blipPool == null) _blipPool = Adopt(_blipRoot);

        // The footprints went with the list, so they have to be taken again.
        if (_pieces.Count == 0) _scanned = false;
    }

    RectTransform Layer(string name, int siblingIndex)
    {
        var existing = view.Find(name) as RectTransform;
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(view, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.SetSiblingIndex(siblingIndex);

        return rect;
    }

    static List<Image> Adopt(RectTransform root)
    {
        var pool = new List<Image>();
        if (root == null) return pool;

        for (int i = 0; i < root.childCount; i++)
        {
            var image = root.GetChild(i).GetComponent<Image>();
            if (image == null) continue;

            image.enabled = false;
            pool.Add(image);
        }

        return pool;
    }
}
