using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Asks what to call the player, once, on a profile that has never been asked.
///
/// <see cref="PlayerProfile.Name"/> was stored but never asked for, so every dashboard said
/// "WELCOME BACK, OPERATIVE". Built at runtime like <see cref="ConfirmDialog"/>, so no
/// dashboard rebuild is needed for it to appear.
///
/// <b>Leaving is an answer.</b> Escape, B or SKIP stores the default name, so a player who
/// does not want to give one is not asked again on every visit to the dashboard.
/// </summary>
public class NameDialog : OverlayPanel
{
    const string DefaultName = "Operative";

    TMP_InputField _field;
    FlatButton _ok;
    System.Action _onDone;

    /// <summary>Asks, on a canvas. Built the first time and reused.</summary>
    public static NameDialog Show(Canvas canvas, System.Action onDone)
    {
        if (canvas == null) { onDone?.Invoke(); return null; }
        var d = canvas.GetComponentInChildren<NameDialog>(true);
        if (d == null)
        {
            var host = new GameObject("NameDialog", typeof(RectTransform));
            host.transform.SetParent(canvas.transform, false);
            UIKit.Fill((RectTransform)host.transform);
            var own = host.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = 790;
            host.AddComponent<GraphicRaycaster>();
            d = host.AddComponent<NameDialog>();
            d.Build();
        }
        d._onDone = onDone;
        d.transform.SetAsLastSibling();
        d.Open();
        return d;
    }

    void Build()
    {
        var t = UITheme.Active;
        var shade = UIKit.Panel(transform, "Shade", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.92f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        panel = shade.gameObject;

        var card = UIKit.Panel(shade.transform, "Card", UIKit.PanelTone.Panel, t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripeColor = t.accent;
        card.stripePixels = t.stripePixels;
        var rt = card.rectTransform;
        // Held in the upper half: on a phone the on-screen keyboard takes the lower one,
        // and a field hidden under the keys it is being typed with cannot be checked.
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.72f);
        rt.sizeDelta = new Vector2(560f, 0f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 14f, new RectOffset(30, 30, 24, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        UIKit.Text(card.transform, "Title", "WHAT DO WE CALL YOU?", UIKit.TextRole.Title, t);
        var body = UIKit.Text(card.transform, "Body", "The name the dashboard greets you by. You can skip this.",
                              UIKit.TextRole.Body, t);
        body.color = t.textSecondary;
        body.textWrappingMode = TextWrappingModes.Normal;

        _field = Field(card.rectTransform, "Your name", t);
        _field.onSubmit.AddListener(_ => Confirm());

        var row = UIKit.Rect(card.transform, "Buttons");
        var r = UIKit.Row(row, 10f, null, TextAnchor.MiddleRight);
        r.childForceExpandWidth = false;
        UIKit.Size(row).minHeight = UIKit.ControlHeight;
        var skip = UIKit.Button(row, "Skip", "Skip", FlatButton.Variant.Secondary, null, t);
        skip.onClick.AddListener(Close);
        _ok = UIKit.Button(row, "Ok", "Continue", FlatButton.Variant.Primary, null, t);
        _ok.onClick.AddListener(Confirm);
        _ok.gameObject.AddComponent<UIDefaultSelection>().priority = 900;
        backButton = skip;
        panel.SetActive(false);
    }

    /// <summary>A flat text field, the same one the HUD editor names layouts with. Settings' Profile page uses it too.</summary>
    public static TMP_InputField Field(RectTransform parent, string placeholder, UITheme t)
    {
        var face = UIKit.Panel(parent, "Name", UIKit.PanelTone.Raised, t);
        face.raycastTarget = true;
        UIKit.Size(face, height: UIKit.ControlHeight);
        var area = UIKit.Rect(face.transform, "Text Area");
        UIKit.Fill(area, 12f, 6f, 12f, 6f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = UIKit.Text(area, "Placeholder", placeholder, UIKit.TextRole.Body, t);
        ph.color = t.textDisabled;
        UIKit.Fill(ph.rectTransform);
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        var text = UIKit.Text(area, "Text", "", UIKit.TextRole.Body, t);
        UIKit.Fill(text.rectTransform);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var field = face.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = ph;
        field.characterLimit = 16;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.targetGraphic = face;
        field.caretColor = t.accent;
        field.selectionColor = new Color(t.accent.r, t.accent.g, t.accent.b, 0.35f);
        // On a touch screen, the game's own keyboard rather than the phone's.
        OnScreenKeyboard.Attach(field, placeholder);
        return field;
    }

    void Confirm()
    {
        string typed = _field != null ? _field.text.Trim() : "";
        if (typed.Length > 0) PlayerProfile.Name = typed;
        Close();
    }

    protected override void OnOpened()
    {
        if (_field == null) return;
        _field.text = "";
        // A touch screen goes straight to the game's keyboard, whose DONE confirms; the
        // phone's own keyboard never opens. A desk types into the field as it always has.
        if (OnScreenKeyboard.Wanted)
        {
            OnScreenKeyboard.Open(_field, "What do we call you?");
            return;
        }
        _field.Select();
        _field.ActivateInputField();
    }

    protected override void OnClosed()
    {
        if (!PlayerProfile.HasName) PlayerProfile.Name = DefaultName;
        var done = _onDone;
        _onDone = null;
        done?.Invoke();
    }
}
