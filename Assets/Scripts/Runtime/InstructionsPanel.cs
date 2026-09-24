using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// How to play, written from the live bindings.
///
/// <b>Nothing here is hard-coded, and that is not tidiness.</b> The bindings live in a
/// <see cref="ControlSettings"/> asset with three presets, so a panel that spelled out
/// "WASD to move" would be wrong for two of them -- and wrong in the worst way, because a
/// player who follows written instructions and gets nothing concludes the game is broken
/// rather than that the page is stale.
///
/// <b>Touch and pointer get different pages, not one page with a line crossed out.</b> On a
/// handset none of these keys exist and the answer to every question is a different gesture,
/// so the pointer page would be a page of things that are not true. Which page is shown is
/// <see cref="DeviceProfile.Touched"/>, so a tablet with a keyboard gets the keys and a
/// laptop with a touchscreen does not.
///
/// The two least guessable controls lead, because they are the two nobody finds by
/// experiment: the bomb is <i>held</i> and releasing is the throw, and the drink is a key
/// with no on-screen presence at all.
/// </summary>
public class InstructionsPanel : OverlayPanel
{
    [Header("Content")]
    [Tooltip("Hidden row cloned once per instruction.")]
    public InstructionRow rowTemplate;

    [Tooltip("Where rows are parented, one per column.")]
    public RectTransform[] columns;

    [Tooltip("Heading above each column.")]
    public TMP_Text[] columnHeadings;

    [Tooltip("One line under the title saying which controls these are.")]
    public TMP_Text subtitleText;

    [Tooltip("Bindings to read. Left empty, the panel finds the one the player rig uses.")]
    public ControlSettings controls;

    readonly List<InstructionRow> _rows = new();

    /// <summary>Switches a whole column off. The rows hang off the scroll view's content,
    /// so the object to switch is its grandparent -- the viewport is in between.</summary>
    void SetColumnShown(int index, bool shown)
    {
        var column = columns[index];
        var viewport = column.parent as RectTransform;
        var root = viewport != null ? viewport.parent as RectTransform : null;

        (root != null ? root.gameObject : column.gameObject).SetActive(shown);
    }

    protected override void OnOpened()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);
        _rows.Clear();

        // By what the player is holding now, not by what the device is: a phone with a
        // pad paired is read the pad page, and picking the pad up mid-read rewrites it.
        var scheme = GameInput.Scheme;
        if (scheme == InputScheme.KeyboardMouse && DeviceProfile.Touched) scheme = InputScheme.Touch;

        if (subtitleText != null)
            subtitleText.text = scheme switch
            {
                InputScheme.Touch => "On-screen controls. Drag the fire button to keep shooting while you aim.",
                InputScheme.Gamepad => "Gamepad. Stick sensitivity, deadzone and aim assist are in Settings.",
                _ => "Keyboard and mouse.",
            };

        var groups = scheme switch
        {
            InputScheme.Touch => TouchPages(),
            InputScheme.Gamepad => PadPages(),
            _ => KeyPages(),
        };

        // Same rule as the achievements screen: a handset gets one column and a tablet two,
        // because four columns on a 147mm screen are four strips too narrow to hold a
        // sentence. The pages keep their order, so the screen is the same screen.
        int wide = DeviceProfile.CurrentForm switch
        {
            DeviceProfile.Form.Handset => 1,
            DeviceProfile.Form.Tablet => 2,
            _ => groups.Count,
        };

        bool headingsAbove = wide >= groups.Count;

        if (columns != null)
            for (int c = 0; c < columns.Length; c++)
                if (columns[c] != null)
                    SetColumnShown(c, c < wide);

        if (columnHeadings != null)
            for (int h = 0; h < columnHeadings.Length; h++)
                if (columnHeadings[h] != null)
                    columnHeadings[h].gameObject.SetActive(headingsAbove && h < wide);

        for (int i = 0; i < groups.Count; i++)
        {
            int target = wide <= 0 ? 0 : i % wide;

            if (columns == null || target >= columns.Length || columns[target] == null) continue;

            if (headingsAbove)
            {
                if (columnHeadings != null && i < columnHeadings.Length && columnHeadings[i] != null)
                    columnHeadings[i].text = groups[i].Heading.ToUpperInvariant();
            }
            else if (rowTemplate != null)
            {
                // The heading travels with its rows when a column holds more than one page,
                // or it would label only the first of them.
                var heading = Instantiate(rowTemplate, columns[target]);
                heading.gameObject.SetActive(true);
                heading.Bind("", groups[i].Heading.ToUpperInvariant());
                if (heading.saysText != null) heading.saysText.color = UITheme.Hazard;
                _rows.Add(heading);
            }

            foreach (var line in groups[i].Lines)
            {
                var row = Instantiate(rowTemplate, columns[target]);
                row.gameObject.SetActive(true);
                row.Bind(line.Control, line.Says);
                _rows.Add(row);
            }
        }
    }


    /// <summary>
    /// Whether the story has handed the bomb over, and what to say when it has not.
    ///
    /// <b>A page that explains a control the player does not have is worse than a page
    /// that omits it.</b> The bomb is the campaign's first reward, taken off the first
    /// of the Augers to fall, so before that there is no bomb key, no bomb button in the
    /// thumb cluster and nothing in the HUD -- and this panel was still teaching all
    /// three. What that produces is a player following written instructions, finding no
    /// button where the game says there is one, and concluding the controls are broken.
    /// Ishaan reported exactly that: "there is no bomb symbol, so why".
    ///
    /// So the row stays, because the answer to "why" has to be somewhere, and it says
    /// what it is and where it comes from.
    ///
    /// <b>The line names no number.</b> It used to quote the star gate, which was true
    /// of exactly one version of the campaign: which zone hands the bomb over is written
    /// on <see cref="CampaignData"/> now and is meant to be moved, so a page repeating a
    /// figure from the asset is a page that goes quietly wrong the first time somebody
    /// drags a zone.
    /// </summary>
    bool BombEarned => Campaign.HasPower(Campaign.BombPower);

    string BombLockLine()
        => "Locked. It belonged to one of the Augers -- the story hands it over when " +
           "the first of them goes down.";

    readonly struct Line
    {
        public readonly string Control;
        public readonly string Says;
        public Line(string control, string says) { Control = control; Says = says; }
    }

    readonly struct Page
    {
        public readonly string Heading;
        public readonly List<Line> Lines;
        public Page(string heading, List<Line> lines) { Heading = heading; Lines = lines; }
    }

    /// <summary>
    /// The bindings to describe.
    ///
    /// The dashboard has no player rig, so there is nothing in this scene to read them
    /// off. The builder wires the same asset the arenas use; <see cref="ControlSettings.CreateDefault"/>
    /// is the fallback rather than a hard-coded list, so even an unwired panel describes
    /// the shipped preset instead of describing nothing.
    /// </summary>
    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;

            _fallback ??= ControlSettings.CreateDefault();
            return _fallback;
        }
    }

    ControlSettings _fallback;

    string K(KeyCode key) => InputPrompts.KeyText(key);

    static string P(GameAction a) => InputPrompts.For(a, InputScheme.Gamepad, GameInput.Pad);
    static string T(GameAction a) => InputPrompts.For(a, InputScheme.Touch, GameInput.Pad);

    void OnEnable() => GameInput.SchemeChanged += OnSchemeChanged;
    void OnDisable() => GameInput.SchemeChanged -= OnSchemeChanged;

    void OnSchemeChanged()
    {
        if (IsOpen) OnOpened();
    }

    List<Page> PadPages()
        => new List<Page>
        {
            new Page("Move", new List<Line>
            {
                new Line(P(GameAction.Move), "Walk"),
                new Line(P(GameAction.Look), "Look"),
                new Line(P(GameAction.Sprint), "Sprint. Click once; it holds until you stop."),
                new Line(P(GameAction.Jump), "Jump"),
                new Line(P(GameAction.Crouch), "Crouch. Press again to stand."),
            }),

            new Page("Fight", new List<Line>
            {
                new Line(P(GameAction.Fire), "Fire"),
                new Line(P(GameAction.Aim), "Aim down sights"),
                new Line(P(GameAction.Reload), "Reload"),
            }),

            new Page("Equipment", BombEarned
                ? new List<Line>
                {
                    new Line(P(GameAction.Bomb), "Tap to aim the bomb, tap again to throw."),
                    new Line(P(GameAction.Look), "Moves the marker while you aim."),
                    new Line(P(GameAction.UseItem), "Drink. Restores health."),
                }
                : new List<Line>
                {
                    new Line("BOMB", BombLockLine()),
                    new Line(P(GameAction.UseItem), "Drink. Restores health."),
                }),

            new Page("Level", new List<Line>
            {
                new Line(P(GameAction.Pause), "Pause"),
                new Line(InputPrompts.Back, "Back, and resume from pause"),
            }),
        };

    List<Page> KeyPages()
    {
        var c = Bindings;
        if (c == null) return new List<Page>();

        return new List<Page>
        {
            new Page("Move", new List<Line>
            {
                new Line($"{K(c.altForward)} {K(c.altLeft)} {K(c.altBack)} {K(c.altRight)}", "Walk"),
                new Line($"{K(c.moveForward)} {K(c.moveLeft)} {K(c.moveBack)} {K(c.moveRight)}", "Walk, the other way"),
                new Line(K(c.sprintKey), "Sprint. Held on its own it runs forward."),
                new Line(K(c.jump), "Jump"),
                new Line(K(c.crouch), "Crouch"),
            }),

            new Page("Fight", new List<Line>
            {
                new Line(K(c.fire), "Fire"),
                new Line(K(c.aim), "Aim down sights"),
                new Line(K(c.reload), "Reload"),
            }),

            // The two nobody finds by experiment, and the reason this panel exists.
            new Page("Equipment", BombEarned
                ? new List<Line>
                {
                    new Line(K(c.bomb), "Hold to aim the bomb. Let go to throw it."),
                    new Line("", "Tap it instead and the ring stays up. Tap again to throw."),
                    new Line("", "The mouse moves the marker, not your head."),
                    new Line(K(c.useItem), "Drink. Restores health."),
                }
                : new List<Line>
                {
                    new Line("BOMB", BombLockLine()),
                    new Line(K(c.useItem), "Drink. Restores health."),
                }),

            new Page("Level", new List<Line>
            {
                new Line("ESC  P", "Pause"),
                new Line("R", "Resume"),
                new Line("Q", "Leave the level"),
            }),
        };
    }

    List<Page> TouchPages()
        => new List<Page>
        {
            new Page("Move", new List<Line>
            {
                new Line("Left stick", "Walk. Push it to the edge to sprint."),
                new Line("Right side", "Drag anywhere to look."),
            }),

            new Page("Fight", new List<Line>
            {
                new Line(T(GameAction.Fire), "Press and keep dragging -- it keeps firing while you aim."),
                new Line(T(GameAction.Aim), "Down sights. Tap or hold, in Settings."),
                new Line(T(GameAction.Reload), "Reload."),
                new Line(T(GameAction.Jump), "Jump."),
                new Line(T(GameAction.Crouch), "Crouch."),
            }),

            new Page("Equipment", BombEarned
                ? new List<Line>
                {
                    new Line(T(GameAction.Bomb), "Hold it. Look further down to throw shorter, up to throw further."),
                    new Line("", "Let go to throw."),
                    new Line(T(GameAction.UseItem), "Restores health."),
                }
                : new List<Line>
                {
                    new Line("BOMB", BombLockLine()),
                    new Line(T(GameAction.UseItem), "Restores health."),
                }),

            new Page("Level", new List<Line>
            {
                new Line(T(GameAction.Pause), "Top right. Pause -- it is the only way out of a level."),
            }),
        };
}
