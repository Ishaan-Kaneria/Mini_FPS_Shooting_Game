using TMPro;
using UnityEngine;

/// <summary>
/// A small card over the paused arena: a title, a few lines of text, and a close button.
/// What the paused HUD's Info and Stats icons open -- this level's objective and star
/// conditions, and this run's numbers so far.
///
/// An <see cref="OverlayPanel"/>, so Escape and B close it and the same press does not also
/// resume the game underneath (<see cref="OverlayPanel.AnyOpenThisFrame"/>).
/// </summary>
public class HudCard : OverlayPanel
{
    TMP_Text _title;
    TMP_Text _body;

    public static HudCard Create(Transform canvas, string name)
    {
        var host = new GameObject(name, typeof(RectTransform));
        host.transform.SetParent(canvas, false);
        var card = host.AddComponent<HudCard>();
        card.Build(canvas);
        return card;
    }

    void Build(Transform canvas)
    {
        var t = UITheme.Active;
        var shade = UIKit.Panel(transform, "Shade", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.75f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        UIKit.Fill((RectTransform)transform);
        panel = shade.gameObject;

        var card = UIKit.Panel(shade.transform, "Card", UIKit.PanelTone.Panel, t);
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(620f, 0f);
        card.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
            UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 12f, new RectOffset(28, 28, 22, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        var header = UIKit.Rect(card.transform, "Header");
        UIKit.Row(header, 12f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header).minHeight = 44f;
        _title = UIKit.Text(header, "Title", "", UIKit.TextRole.Title, t);
        UIKit.Size(_title, flexWidth: 1f);
        var close = UIKit.IconButton(header, "Close", "x", "Close", UIKit.ControlHeight, t);
        close.onClick.AddListener(Close);
        close.gameObject.AddComponent<UIDefaultSelection>().priority = 60;

        _body = UIKit.Text(card.transform, "Body", "", UIKit.TextRole.Body, t);
        _body.textWrappingMode = TextWrappingModes.Normal;
        _body.lineSpacing = 12f;

        panel.SetActive(false);
    }

    public void Show(string title, string body)
    {
        _title.text = title;
        _body.text = body;
        transform.SetAsLastSibling();
        Open();
    }

    protected override void OnOpened() { }
}
