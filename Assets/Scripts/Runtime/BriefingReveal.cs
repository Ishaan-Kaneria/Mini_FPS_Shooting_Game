using TMPro;
using UnityEngine;

/// <summary>
/// Plays the level-start card: it pops in, its words rise into place one letter after
/// another, a bar under the words drains with the countdown, and when the fight starts it
/// lifts away rather than blinking out. No per-second pulse: Ishaan found a card pumping
/// every second hard to focus past.
///
/// <b>Driven by the level, not by the label.</b> HUDController empties the label the frame
/// the briefing ends, so a card shown "while the label has text" can only vanish. This
/// watches <see cref="LevelManager.IsBriefing"/>, keeps the last words for the exit, and
/// clears the label once the card has gone.
///
/// Everything runs on unscaled time: a pause during the countdown freezes the level, and
/// the card should finish arriving rather than hang half-drawn under the pause menu.
/// Lives on the HUD's host, which is always active, never on the card it hides.
/// </summary>
public class BriefingReveal : MonoBehaviour
{
    [Header("Wiring")]
    public RectTransform card;
    public CanvasGroup group;
    public TMP_Text text;
    public UIProgressBar bar;
    public LevelManager level;

    [Header("Timing, in seconds")]
    [Tooltip("How long the card takes to pop in.")]
    public float enterTime = 0.45f;

    [Tooltip("How long one letter takes to rise into place.")]
    public float letterTime = 0.35f;

    [Tooltip("The gap between one letter starting and the next. Shortened for a long text, " +
             "so every word has landed within Reveal Budget.")]
    public float letterGap = 0.02f;

    [Tooltip("The whole text is in place by this long after the card appears.")]
    public float revealBudget = 1.8f;

    [Tooltip("How long the card takes to lift away when the fight starts.")]
    public float exitTime = 0.4f;

    [Tooltip("Set while the HUD editor is open: the card stays hidden.")]
    public bool suppressed;

    enum State { Hidden, Shown, Leaving }

    State _state;
    float _since;
    float _total;
    string _lastText = "";
    Vector2 _home;
    bool _homeKnown;

    void Start()
    {
        if (card != null) card.gameObject.SetActive(false);
    }

    void Update()
    {
        if (card == null || text == null) return;
        if (!_homeKnown) { _home = card.anchoredPosition; _homeKnown = true; }

        bool briefing = level != null && level.IsBriefing && !level.IsFinished && !suppressed &&
                        !string.IsNullOrEmpty(text.text);

        switch (_state)
        {
            case State.Hidden:
                if (briefing) Enter();
                break;

            case State.Shown:
                if (!briefing)
                {
                    if (suppressed) { Hide(); break; }
                    Leave();
                    break;
                }
                // Kept for the exit, which plays after HUDController empties the label.
                _lastText = text.text;
                if (bar != null && _total > 0f) bar.Value = Mathf.Clamp01(level.BriefingRemaining / _total);
                break;

            case State.Leaving:
                if (briefing) { Enter(); break; }
                if (Time.unscaledTime - _since >= exitTime) Hide();
                break;
        }

        Pose();
    }

    void LateUpdate()
    {
        if (_state == State.Shown) RevealLetters();
    }

    void Enter()
    {
        _state = State.Shown;
        _since = Time.unscaledTime;
        _lastText = text.text;
        _total = Mathf.Max(0.01f, level.BriefingRemaining);
        card.gameObject.SetActive(true);
        if (bar != null) { bar.Value = 1f; bar.Snap(); }
    }

    void Leave()
    {
        _state = State.Leaving;
        _since = Time.unscaledTime;
        // Keep the words on the card while it goes; the label is HUDController's and is
        // empty now. Put back to empty in Hide, which it expects.
        text.text = _lastText;
        text.ForceMeshUpdate();
    }

    void Hide()
    {
        _state = State.Hidden;
        card.gameObject.SetActive(false);
        card.anchoredPosition = _home;
        if (level == null || !level.IsBriefing) text.text = "";
    }

    /// <summary>The card's scale, fade and height for where it is in its life.</summary>
    void Pose()
    {
        if (_state == State.Hidden) return;
        float t = Time.unscaledTime - _since;

        float alpha, scale, lift;
        if (_state == State.Shown)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, enterTime));
            alpha = Mathf.Clamp01(k * 1.6f);
            scale = Mathf.LerpUnclamped(0.86f, 1f, BackOut(k));
            lift = Mathf.Lerp(-24f, 0f, CubicOut(k));

        }
        else
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, exitTime));
            alpha = 1f - k;
            scale = Mathf.Lerp(1f, 1.06f, CubicOut(k));
            lift = Mathf.Lerp(0f, 60f, CubicOut(k));
        }

        if (group != null) group.alpha = alpha;
        card.localScale = new Vector3(scale, scale, 1f);
        card.anchoredPosition = _home + new Vector2(0f, lift);
    }

    /// <summary>
    /// Each letter fades up from a little below and a little small, in reading order. The
    /// mesh is regenerated first because the countdown rewrites the label every second, and
    /// the offsets are applied to a fresh mesh rather than piled onto the last frame's.
    /// </summary>
    void RevealLetters()
    {
        float t = Time.unscaledTime - _since - enterTime * 0.4f;

        text.ForceMeshUpdate();
        var info = text.textInfo;
        int visible = 0;
        for (int i = 0; i < info.characterCount; i++) if (info.characterInfo[i].isVisible) visible++;
        if (visible == 0) return;

        float gap = Mathf.Min(letterGap, Mathf.Max(0f, revealBudget - letterTime) / visible);
        if (t > gap * visible + letterTime) return; // all landed: leave TMP's own mesh alone

        int n = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible) continue;

            float k = Mathf.Clamp01((t - gap * n) / Mathf.Max(0.01f, letterTime));
            n++;
            float e = CubicOut(k);

            var mesh = info.meshInfo[c.materialReferenceIndex];
            int v = c.vertexIndex;
            Vector3 mid = (mesh.vertices[v] + mesh.vertices[v + 2]) * 0.5f;
            Vector3 drop = new Vector3(0f, (1f - e) * -18f, 0f);
            float size = Mathf.Lerp(0.55f, 1f, BackOut(k));

            for (int j = 0; j < 4; j++)
            {
                mesh.vertices[v + j] = mid + (mesh.vertices[v + j] - mid) * size + drop;
                var col = mesh.colors32[v + j];
                col.a = (byte)(col.a * e);
                mesh.colors32[v + j] = col;
            }
        }

        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }

    static float CubicOut(float k) => 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);

    static float BackOut(float k)
    {
        const float s = 1.70158f;
        k = Mathf.Clamp01(k) - 1f;
        return k * k * ((s + 1f) * k + s) + 1f;
    }
}
