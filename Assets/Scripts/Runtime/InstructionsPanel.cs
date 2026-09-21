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

    protected override void OnOpened()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);
        _rows.Clear();

        bool touch = DeviceProfile.Touched;

        if (subtitleText != null)
            subtitleText.text = touch
                ? "On-screen controls. Drag the fire button to keep shooting while you aim."
                : "Keyboard and mouse. Rebind these in the control settings asset.";

        var groups = touch ? TouchPages() : KeyPages();

        for (int i = 0; i < groups.Count; i++)
        {
            if (columnHeadings != null && i < columnHeadings.Length && columnHeadings[i] != null)
                columnHeadings[i].text = groups[i].Heading.ToUpperInvariant();

            if (columns == null || i >= columns.Length || columns[i] == null) continue;

            foreach (var line in groups[i].Lines)
            {
                var row = Instantiate(rowTemplate, columns[i]);
                row.gameObject.SetActive(true);
                row.Bind(line.Control, line.Says);
                _rows.Add(row);
            }
        }
    }

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

    string K(KeyCode key) => UIText.KeyLabel(key);

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
            new Page("Equipment", new List<Line>
            {
                new Line(K(c.bomb), "Hold to aim the bomb. Let go to throw it."),
                new Line("", "Tap it instead and the ring stays up. Tap again to throw."),
                new Line("", "The mouse moves the marker, not your head."),
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
                new Line("FIRE", "Press and keep dragging -- it keeps firing while you aim."),
                new Line("AIM", "Down sights."),
                new Line("RELOAD", "Reload."),
            }),

            new Page("Equipment", new List<Line>
            {
                new Line("BOMB", "Hold it. Look further down to throw shorter, up to throw further."),
                new Line("", "Let go to throw."),
                new Line("DRINK", "Restores health."),
            }),

            new Page("Level", new List<Line>
            {
                new Line("Top right", "Pause. It is the only way out of a level."),
            }),
        };
}
