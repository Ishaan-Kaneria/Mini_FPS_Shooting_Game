using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A one-question card: a title, a line, and two buttons -- the safe one focused, the other
/// doing the thing. What stands between the player and anything that throws work away,
/// such as leaving a level mid-fight.
///
/// An <see cref="OverlayPanel"/>, so Escape and B are "no", and the same press does not also
/// reach the screen underneath.
/// </summary>
public class ConfirmDialog : OverlayPanel
{
    TMP_Text _title, _body;
    FlatButton _yes, _no;
    System.Action _onYes;

    /// <summary>Asks, on a canvas. Built the first time and reused.</summary>
    public static ConfirmDialog Show(Canvas canvas, string title, string body, string yes, System.Action onYes, bool destructive = true)
    {
        if (canvas == null) { onYes?.Invoke(); return null; }
        var d = canvas.GetComponentInChildren<ConfirmDialog>(true);
        if (d == null)
        {
            var host = new GameObject("ConfirmDialog", typeof(RectTransform));
            host.transform.SetParent(canvas.transform, false);
            UIKit.Fill((RectTransform)host.transform);
            var own = host.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = 800;
            host.AddComponent<GraphicRaycaster>();
            d = host.AddComponent<ConfirmDialog>();
            d.Build();
        }
        d._title.text = title;
        d._body.text = body;
        d._yes.label.text = (yes ?? "Yes").ToUpperInvariant();
        d._yes.variant = destructive ? FlatButton.Variant.Secondary : FlatButton.Variant.Primary;
        d._onYes = onYes;
        d.transform.SetAsLastSibling();
        d.Open();
        return d;
    }

    void Build()
    {
        var t = UITheme.Active;
        var shade = UIKit.Panel(transform, "Shade", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.85f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        panel = shade.gameObject;

        var card = UIKit.Panel(shade.transform, "Card", UIKit.PanelTone.Panel, t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripeColor = t.danger;
        card.stripePixels = t.stripePixels;
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(540f, 0f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 14f, new RectOffset(30, 30, 24, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        _title = UIKit.Text(card.transform, "Title", "", UIKit.TextRole.Title, t);
        _body = UIKit.Text(card.transform, "Body", "", UIKit.TextRole.Body, t);
        _body.color = t.textSecondary;
        _body.textWrappingMode = TextWrappingModes.Normal;
        var row = UIKit.Rect(card.transform, "Buttons");
        var r = UIKit.Row(row, 10f, null, TextAnchor.MiddleRight);
        r.childForceExpandWidth = false;
        UIKit.Size(row).minHeight = UIKit.ControlHeight;
        _yes = UIKit.Button(row, "Yes", "Yes", FlatButton.Variant.Secondary, null, t);
        _yes.onClick.AddListener(() => { var a = _onYes; _onYes = null; Close(); a?.Invoke(); });
        // The safe answer is the focused one: the default of a question that throws work
        // away is never the one that throws it away.
        _no = UIKit.Button(row, "No", "Stay", FlatButton.Variant.Primary, null, t);
        _no.onClick.AddListener(Close);
        _no.gameObject.AddComponent<UIDefaultSelection>().priority = 900;
        backButton = _no;
        panel.SetActive(false);
    }

    protected override void OnOpened()
    {
        if (_no != null) _no.Select();
    }
}
