using UnityEngine;

/// <summary>
/// Applies the player's touch settings to the on-screen controls: left-handed mirroring,
/// tap or hold to aim, and auto-fire. Added to the touch layer by <see cref="TouchControls"/>
/// at runtime, so a scene built before these settings existed gets them without a rebuild.
///
/// <b>Mirroring swaps the two halves of the screen</b>: the stick region and the look area
/// trade sides, and the button cluster is laid from the left edge instead of the right. The
/// pause button stays top-right, because it is found by where it always is rather than by
/// which hand is free.
///
/// <b>Auto-fire is a trigger, not an aimbot.</b> It fires while the crosshair is already on an
/// enemy -- it never moves the view -- so the player still does all of the aiming and gives
/// up only the second thumb. It needs a clear line: a body behind a crate is not a target.
/// It is off by default and on a gamepad or a mouse it does nothing.
/// </summary>
[DisallowMultipleComponent]
public class TouchLayout : MonoBehaviour
{
    [Tooltip("How far auto-fire looks for an enemy under the crosshair, in metres.")]
    public float autoFireRange = 60f;

    VirtualJoystick _stick;
    TouchLookArea _look;
    TouchCluster _cluster;
    TouchButton _aim;
    bool _mirrored;
    bool _applied;
    bool _pulse;
    int _mask;

    void Awake()
    {
        _stick = GetComponentInChildren<VirtualJoystick>(true);
        _look = GetComponentInChildren<TouchLookArea>(true);
        _cluster = GetComponentInChildren<TouchCluster>(true);
        foreach (var b in GetComponentsInChildren<TouchButton>(true))
            if (b.action == TouchButton.ActionKind.Aim) _aim = b;
        int player = LayerMask.NameToLayer("Player");
        _mask = player >= 0 ? ~(1 << player) : ~0;
    }

    void OnEnable()
    {
        GameSettings.Changed += OnSettingChanged;
        HudLayout.Changed += OnLayoutChanged;
        Apply();
    }

    void OnDisable()
    {
        GameSettings.Changed -= OnSettingChanged;
        HudLayout.Changed -= OnLayoutChanged;
        MobileInput.SetAutoFire(false);
    }

    void OnSettingChanged(string key) => Apply();

    /// <summary>Re-reads the settings and lays the controls out for them.</summary>
    public void Apply()
    {
        if (_aim != null) _aim.toggle = GameSettings.TouchAimMode == GameSettings.AimMode.Tap;

        bool mirror = GameSettings.LeftHanded;
        if (_applied && mirror == _mirrored) return;
        _applied = true;
        if (mirror != _mirrored)
        {
            Flip(_stick != null ? (RectTransform)_stick.transform : null);
            Flip(_look != null ? (RectTransform)_look.transform : null);
        }
        if (mirror != _mirrored) MirrorHudFooter();
        _mirrored = mirror;
        if (_cluster != null)
        {
            _cluster.mirrored = mirror;
            _cluster.Rebuild();
        }

        // The joystick zone and the buttons outside the cluster (pause) are movable too.
        if (_stick != null)
        {
            var zone = HudLayoutTarget.Mark(_stick, "touch.move", "Joystick zone");
            zone.zone = true;
            zone.Settle();
        }
        foreach (var b in GetComponentsInChildren<TouchButton>(true))
        {
            if (b == _secondFire || b.GetComponent<HudLayoutTarget>() != null) continue;
            HudLayoutTarget.Mark(b, TargetId(b.action), TargetLabel(b.action)).Settle();
        }
        if (_secondFire != null) { Destroy(_secondFire.gameObject); _secondFire = null; }
        SyncSecondFire();
    }

    /// <summary>
    /// The HUD's bottom corners swap with the controls. Health sits bottom-left and ammo
    /// bottom-right because that is where the thumbs are not; move the buttons to the left
    /// and the health readout is under the fire button unless it moves too.
    /// </summary>
    /// <summary>The key a touch button is saved under in a HUD layout.</summary>
    public static string TargetId(TouchButton.ActionKind a) => "touch." + a.ToString().ToLowerInvariant();

    /// <summary>What the HUD editor calls a touch button.</summary>
    public static string TargetLabel(TouchButton.ActionKind a) => a switch
    {
        TouchButton.ActionKind.Bomb => "Grenade button",
        TouchButton.ActionKind.UseItem => "Medkit button",
        _ => a + " button",
    };

    const string SecondFireId = "touch.fire2";
    TouchButton _secondFire;

    /// <summary>
    /// The optional fire button on the other thumb's side, cloned from the real one so it is
    /// the same size, look and behaviour. Not a cluster slot: the cluster lays out one hand,
    /// and this belongs to the other.
    /// </summary>
    void SyncSecondFire()
    {
        bool want = HudLayout.Current.secondFire && _cluster != null;
        if (!want)
        {
            if (_secondFire != null) Destroy(_secondFire.gameObject);
            _secondFire = null;
            return;
        }
        if (_secondFire != null) return;

        TouchButton fire = null;
        foreach (var b in GetComponentsInChildren<TouchButton>(true))
            if (b.action == TouchButton.ActionKind.Fire) { fire = b; break; }
        if (fire == null) return;

        _secondFire = Instantiate(fire, fire.transform.parent);
        _secondFire.name = "FireButton2";
        var existing = _secondFire.GetComponent<HudLayoutTarget>();
        if (existing != null) DestroyImmediate(existing);
        var rt = (RectTransform)_secondFire.transform;
        var src = (RectTransform)fire.transform;
        // Opposite the cluster, a third of the way up: above where the moving thumb rests.
        bool left = !GameSettings.LeftHanded;
        rt.anchorMin = rt.anchorMax = new Vector2(left ? 0f : 1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = src.sizeDelta;
        var parent = (RectTransform)rt.parent;
        float x = src.sizeDelta.x * 0.9f;
        rt.anchoredPosition = new Vector2(left ? x : -x, parent.rect.height * 0.55f);
        HudLayoutTarget.Mark(_secondFire, SecondFireId, "Second fire button").Settle();
    }

    void OnLayoutChanged() => SyncSecondFire();

    static void MirrorHudFooter()
    {
        var hud = FindAnyObjectByType<HUDController>();
        if (hud == null) return;
        var canvas = hud.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
        {
            var parent = rt.parent as RectTransform;
            // Only direct children of the HUD's own layer (the canvas or its safe area),
            // anchored to a bottom corner -- not everything anchored low somewhere inside.
            if (parent == null || (parent != canvas.transform && parent.parent != canvas.transform)) continue;
            if (rt.GetComponent<SafeAreaFitter>() != null) continue;

            // A movable element is judged, and mirrored, from where its owner put it -- the
            // player's layout comes off first and goes back on afterwards (Settle), so it is
            // applied to the mirrored placement rather than undone by it.
            var target = rt.GetComponent<HudLayoutTarget>();
            if (target != null) target.Apply(null);
            bool corner = rt.anchorMax.y <= 0.35f && (rt.anchorMax.x <= 0.4f || rt.anchorMin.x >= 0.6f);
            if (!corner)
            {
                if (target != null) target.Settle();
                continue;
            }
            var min = rt.anchorMin; var max = rt.anchorMax;
            rt.anchorMin = new Vector2(1f - max.x, min.y);
            rt.anchorMax = new Vector2(1f - min.x, max.y);
            rt.pivot = new Vector2(1f - rt.pivot.x, rt.pivot.y);
            rt.anchoredPosition = new Vector2(-rt.anchoredPosition.x, rt.anchoredPosition.y);
            foreach (var text in rt.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (text.alignment == TMPro.TextAlignmentOptions.Left) text.alignment = TMPro.TextAlignmentOptions.Right;
                else if (text.alignment == TMPro.TextAlignmentOptions.Right) text.alignment = TMPro.TextAlignmentOptions.Left;
                else if (text.alignment == TMPro.TextAlignmentOptions.BottomLeft) text.alignment = TMPro.TextAlignmentOptions.BottomRight;
                else if (text.alignment == TMPro.TextAlignmentOptions.BottomRight) text.alignment = TMPro.TextAlignmentOptions.BottomLeft;
            }
            if (target != null) target.Settle();
        }
    }

    /// <summary>Reflects a rect's anchors across the vertical centre line.</summary>
    static void Flip(RectTransform rt)
    {
        if (rt == null) return;
        var target = rt.GetComponent<HudLayoutTarget>();
        if (target != null) target.Apply(null);
        var min = rt.anchorMin;
        var max = rt.anchorMax;
        rt.anchorMin = new Vector2(1f - max.x, min.y);
        rt.anchorMax = new Vector2(1f - min.x, max.y);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (target != null) target.Settle();
    }

    void Update()
    {
        bool on = GameSettings.AutoFire && MobileInput.Active && PlayerMotor.InputEnabled
                  && GameInput.Scheme == InputScheme.Touch && EnemyUnderCrosshair();
        // Pulsed rather than held, so a semi-automatic weapon -- which fires on the edge
        // of a press -- fires at its own rate instead of once.
        _pulse = on && !_pulse;
        MobileInput.SetAutoFire(on && _pulse);
    }

    bool EnemyUnderCrosshair()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out var hit, autoFireRange, _mask, QueryTriggerInteraction.Ignore))
            return false;
        var enemy = hit.collider.GetComponentInParent<EnemyAI>();
        if (enemy == null) return false;
        var health = enemy.GetComponent<Health>();
        return health == null || !health.IsDead;
    }
}
