using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The story, shown over the dashboard: the opening on a fresh profile, and one card
/// for each of the villain's children as they go down.
///
/// <b>One card, whole beat, dismissed once.</b> A paced, line-by-line telling reads
/// better and loses everything to a player who taps through it, and the dashboard is
/// somewhere people arrive in a hurry to play the next level. So a beat is a single
/// screen with a face, a name and a paragraph, and the text stays re-readable
/// afterwards on the level select for the zone it belongs to -- which is where somebody
/// who skipped it will next be standing.
///
/// <b>It is an <see cref="OverlayPanel"/> like every other screen here</b>, so it opens,
/// closes and restores the dashboard the same way the store and the ladder do, and it
/// inherits the trap that component must not live on the panel it hides.
/// </summary>
public class StoryPanel : OverlayPanel
{
    [Header("Card")]
    [Tooltip("The face. Hidden on the opening, which is about the player rather than " +
             "about one of the eight.")]
    public Image portrait;

    [Tooltip("The border round the face. Hidden on a card with no portrait.")]
    public GameObject portraitFrame;

    [Tooltip("The block the words sit in. It is inset to clear the portrait, and that " +
             "inset is taken back when there is no portrait to clear.\n\n" +
             "Hiding the face does not, on its own, make anything else wider -- the text " +
             "stays where it was authored and the opening is then a paragraph in the " +
             "right two thirds of a card with a third of it empty. Same trap as the " +
             "arena card's description on a phone: the space is only worth anything if " +
             "something is given it.")]
    public RectTransform textArea;

    [Tooltip("How far the words are held off the left edge with a face beside them, and " +
             "without one.")]
    public float textInsetWithFace = 360f;

    public float textInsetAlone = 56f;

    public TMP_Text titleText;

    [Tooltip("What they held: \"the plant\", \"the convoy road\".")]
    public TMP_Text subtitleText;

    public TMP_Text bodyText;

    [Tooltip("Advances to the next card, or closes on the last one.")]
    public Button continueButton;

    public TMP_Text continueLabel;

    [Header("The one question")]
    [Tooltip("The row of identity buttons. Shown only on the opening card, and it is " +
             "what advances that card -- there is no CONTINUE past a question.")]
    public GameObject identityRow;

    public Button manButton;
    public Button womanButton;
    public Button declineButton;

    /// <summary>
    /// One screen of story. A plain struct rather than an asset: these are assembled
    /// from <see cref="CampaignData"/> every time the panel opens, because which of them
    /// are owed depends on what the player has just done.
    /// </summary>
    struct Card
    {
        public string Title;
        public string Subtitle;
        public string Body;
        public Texture2D Portrait;

        /// <summary>The opening, which ends on a question instead of a button.</summary>
        public bool AsksIdentity;

        /// <summary>The zone whose beat this is, so it can be marked read. -1 otherwise.</summary>
        public int ZoneIndex;
    }

    readonly List<Card> _cards = new List<Card>();
    int _at = -1;

    /// <summary>
    /// A zone whose opening is to be shown instead of the usual sweep, or -1.
    ///
    /// Cleared as soon as the panel opens, so a queued opening is used exactly once and
    /// the next open goes back to reporting whatever the campaign owes.
    /// </summary>
    int _queuedZone = -1;

    CampaignData _data;

    /// <summary>The campaign this panel is telling. Set by the dashboard before opening.</summary>
    public void Bind(CampaignData data) => _data = data;

    /// <summary>
    /// Whether there is anything to say right now: the opening on a fresh profile, or a
    /// child who is down and whose beat has not been read.
    ///
    /// Asked by the dashboard before opening, because an overlay that opens with nothing
    /// in it is a screen the player has to dismiss for no reason -- and this one opens
    /// on the way in to the dashboard, which is the worst place to put one.
    /// </summary>
    public bool HasAnythingToSay(CampaignData data)
    {
        if (data == null) return false;
        if (!Campaign.HasIdentity && !string.IsNullOrWhiteSpace(data.prologue)) return true;

        for (int i = 0; i < data.ZoneCount; i++)
            if (OwesBeat(data, i)) return true;

        return false;
    }

    static bool OwesBeat(CampaignData data, int zoneIndex)
    {
        if (Campaign.BeatSeen(zoneIndex)) return false;
        if (!Campaign.ChildDefeated(data, zoneIndex)) return false;

        var holder = data.HolderOf(data.ZoneAt(zoneIndex));
        return holder != null && !string.IsNullOrWhiteSpace(holder.beat);
    }

    /// <summary>
    /// Asks the next open to be this zone's opening rather than the usual sweep.
    ///
    /// The openings are shown on the way *into* a zone rather than queued up with the
    /// beats, because that is when they mean anything: "Kestrel lit the yard a week ago"
    /// is a thing to read while choosing a level in Kestrel's yard, and the same sentence
    /// on the dashboard two zones later is somebody else's news.
    /// </summary>
    public void QueueZoneOpening(int zoneIndex) => _queuedZone = zoneIndex;

    protected override void OnOpened()
    {
        _cards.Clear();
        _at = -1;

        int queued = _queuedZone;
        _queuedZone = -1;

        if (_data != null && queued >= 0)
        {
            var zone = _data.ZoneAt(queued);
            var holder = _data.HolderOf(zone);

            if (zone != null && !string.IsNullOrWhiteSpace(zone.opening))
            {
                Campaign.MarkOpeningSeen(queued);

                _cards.Add(new Card
                {
                    Title = zone.displayName != null
                        ? zone.displayName.ToUpperInvariant()
                        : "THE NEXT ONE",

                    Subtitle = holder != null && !string.IsNullOrWhiteSpace(holder.displayName)
                        ? $"HELD BY {holder.displayName.ToUpperInvariant()}"
                        : "",

                    Body = zone.opening,
                    Portrait = holder != null ? holder.portrait : null,
                    ZoneIndex = -1
                });
            }

            Wire();
            Advance();
            return;
        }

        if (_data != null)
        {
            if (!Campaign.HasIdentity && !string.IsNullOrWhiteSpace(_data.prologue))
            {
                _cards.Add(new Card
                {
                    Title = "TWENTY YEARS AGO",
                    Body = _data.prologue,
                    AsksIdentity = true,
                    ZoneIndex = -1
                });
            }

            // In zone order rather than in the order they were beaten, which is the same
            // thing while the campaign is a sequence and stays right if it ever is not.
            for (int i = 0; i < _data.ZoneCount; i++)
            {
                if (!OwesBeat(_data, i)) continue;

                var holder = _data.HolderOf(_data.ZoneAt(i));

                _cards.Add(new Card
                {
                    Title = holder.displayName.ToUpperInvariant(),
                    Subtitle = string.IsNullOrWhiteSpace(holder.holding)
                        ? ""
                        : holder.holding.ToUpperInvariant(),
                    Body = holder.beat,
                    Portrait = holder.portrait,
                    ZoneIndex = i
                });
            }
        }

        Wire();
        Advance();
    }

    /// <summary>
    /// Everything still queued is marked read on the way out.
    ///
    /// <b>Dismissing counts as reading.</b> Re-showing a beat the player walked away from
    /// would mean this screen interrupts the dashboard again on the next visit, and again
    /// after that -- a story that will not take no for an answer. The text is not lost:
    /// every zone's opening is on its level select, which is where somebody who skipped
    /// it is standing the moment they go back in.
    /// </summary>
    protected override void OnClosed()
    {
        for (int i = Mathf.Max(0, _at); i < _cards.Count; i++)
            if (_cards[i].ZoneIndex >= 0) Campaign.MarkBeatSeen(_cards[i].ZoneIndex);

        _cards.Clear();
        _at = -1;
    }

    /// <summary>
    /// <b>The opening cannot be escaped out of</b>, because it is a question and
    /// declining is one of its answers. Escape on the other cards closes the screen, as
    /// it does on every other overlay here.
    /// </summary>
    protected override void Update()
    {
        if (!IsOpen) return;

        if (Showing.AsksIdentity) return;

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Advance();
            return;
        }

        base.Update();
    }

    Card Showing => _at >= 0 && _at < _cards.Count ? _cards[_at] : default;

    void Wire()
    {
        Hook(continueButton, Advance);
        Hook(manButton, () => Answer(Campaign.Identity.Man));
        Hook(womanButton, () => Answer(Campaign.Identity.Woman));
        Hook(declineButton, () => Answer(Campaign.Identity.Unstated));
    }

    static void Hook(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    void Answer(Campaign.Identity identity)
    {
        Campaign.Who = identity;
        Advance();
    }

    /// <summary>Marks the card just read and shows the next, or closes on the last.</summary>
    void Advance()
    {
        if (_at >= 0 && _at < _cards.Count && _cards[_at].ZoneIndex >= 0)
            Campaign.MarkBeatSeen(_cards[_at].ZoneIndex);

        _at++;

        if (_at >= _cards.Count)
        {
            Close();
            return;
        }

        Show(_cards[_at]);
    }

    void Show(Card card)
    {
        if (titleText != null) titleText.text = card.Title ?? "";

        if (subtitleText != null)
        {
            subtitleText.text = card.Subtitle ?? "";
            subtitleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(card.Subtitle));
        }

        if (bodyText != null)
        {
            // The question is part of the opening's body rather than a label of its own,
            // so it sits against the paragraph it follows and the buttons sit under both.
            bodyText.text = card.AsksIdentity && _data != null
                                              && !string.IsNullOrWhiteSpace(_data.identityQuestion)
                ? $"{card.Body}\n\n<color=#FFC51F>{_data.identityQuestion}</color>"
                : card.Body ?? "";
        }

        bool hasFace = card.Portrait != null;

        if (portrait != null)
        {
            if (hasFace)
                portrait.sprite = Sprite.Create(
                    card.Portrait,
                    new Rect(0f, 0f, card.Portrait.width, card.Portrait.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);

            portrait.color = Color.white;
        }

        if (portraitFrame != null) portraitFrame.SetActive(hasFace);

        if (textArea != null)
            textArea.offsetMin = new Vector2(hasFace ? textInsetWithFace : textInsetAlone,
                                             textArea.offsetMin.y);

        if (identityRow != null) identityRow.SetActive(card.AsksIdentity);

        // Never both. A question with a CONTINUE beside it is a question with a fourth
        // answer, and the fourth answer is the one that leaves the game not knowing.
        if (continueButton != null) continueButton.gameObject.SetActive(!card.AsksIdentity);

        if (continueLabel != null)
            continueLabel.text = _at >= _cards.Count - 1 ? "CONTINUE" : "NEXT";
    }
}
