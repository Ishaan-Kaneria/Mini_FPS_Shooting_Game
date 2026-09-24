using UnityEngine;

/// <summary>
/// INFO, behind the top bar's info icon: HOW TO PLAY, THE LIST, and what the game is -- its
/// version and the licences of what it is built with. Built at runtime from the kit.
///
/// It is a small card rather than a menu of its own, because it is a signpost: two of the
/// three things on it open full screens.
/// </summary>
public class InfoPanel : OverlayPanel
{
    public event System.Action HowToPlayRequested;
    public event System.Action ListRequested;

    public static InfoPanel Create(Canvas canvas, float topInset)
    {
        var host = new GameObject("InfoHost", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        var p = host.AddComponent<InfoPanel>();
        p.Build(canvas, topInset);
        return p;
    }

    void Build(Canvas canvas, float topInset)
    {
        var t = UITheme.Active;
        var shade = UIKit.Panel(canvas.transform, "Info", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.9f);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform, 0, topInset, 0, 0);
        panel = shade.gameObject;

        var card = UIKit.Panel(shade.transform, "Card", UIKit.PanelTone.Panel, t);
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(640f, 440f);
        var col = UIKit.Column(card, 14f, new RectOffset(32, 32, 28, 28));
        col.childForceExpandHeight = false;

        var header = UIKit.Rect(card.transform, "Header");
        UIKit.Row(header, 12f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header, height: 48f);
        var title = UIKit.Text(header, "Title", "Info", UIKit.TextRole.Title, t);
        UIKit.Size(title, flexWidth: 1f);
        var close = UIKit.IconButton(header, "Close", "x", "Close", UIKit.ControlHeight, t);
        close.onClick.AddListener(Close);

        var buttons = UIKit.Rect(card.transform, "Buttons");
        var row = UIKit.Row(buttons, 12f);
        row.childForceExpandWidth = true;
        UIKit.Size(buttons, height: UIKit.ControlHeight);
        var how = UIKit.Button(buttons, "HowToPlay", "How to play", FlatButton.Variant.Primary, "book", t);
        how.onClick.AddListener(() => HowToPlayRequested?.Invoke());
        how.gameObject.AddComponent<UIDefaultSelection>().priority = 50;
        var list = UIKit.Button(buttons, "TheList", "The list", FlatButton.Variant.Secondary, "list-details", t);
        list.onClick.AddListener(() => ListRequested?.Invoke());

        UIKit.Text(card.transform, "AboutHeading", "About", UIKit.TextRole.Label, t);
        UIKit.Text(card.transform, "About",
            $"MINI FPS{UIText.Separator}Single player campaign{UIText.Separator}Version {Application.version}",
            UIKit.TextRole.Body, t);
        UIKit.Text(card.transform, "Credits",
            "Type: Barlow, by Jeremy Tribby, under the SIL Open Font License.\nIcons: Tabler Icons, under the MIT License.",
            UIKit.TextRole.Caption, t);
        panel.SetActive(false);
    }

    protected override void OnOpened() { }
}
