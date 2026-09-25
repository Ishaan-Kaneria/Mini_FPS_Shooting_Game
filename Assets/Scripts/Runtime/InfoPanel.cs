using UnityEngine;
using UnityEngine.UI;

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
        rt.sizeDelta = new Vector2(680f, 0f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 12f, new RectOffset(32, 32, 26, 28));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        var header = UIKit.Rect(card.transform, "Header");
        UIKit.Row(header, 12f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header, height: 48f);
        var title = UIKit.Text(header, "Title", "About", UIKit.TextRole.Title, t);
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

        var name = UIKit.Text(card.transform, "Game", "Mini FPS", UIKit.TextRole.Display, t);
        name.color = t.textPrimary;
        UIKit.Text(card.transform, "Version",
            UIText.Row("SINGLE PLAYER CAMPAIGN", $"VERSION {Application.version}"), UIKit.TextRole.Label, t).color = t.textSecondary;

        Section(card.transform, "Credits",
            "Made by Ishaan Kaneria.\n" +
            "Engine: Unity 6 with the Universal Render Pipeline.\n" +
            "Industrial art: the RPG FPS Game Assets industrial pack.\n" +
            "Sound: placeholder effects synthesised for the game.", t);
        Section(card.transform, "Licences",
            "Barlow and Barlow Condensed, by Jeremy Tribby, under the SIL Open Font License 1.1.\n" +
            "Tabler Icons, under the MIT License; the game-specific icons are drawn to match.", t);

        panel.SetActive(false);
    }

    static void Section(Transform parent, string heading, string body, UITheme t)
    {
        UIKit.Text(parent, heading + "Heading", heading, UIKit.TextRole.Label, t).color = t.textSecondary;
        var text = UIKit.Text(parent, heading, body, UIKit.TextRole.Caption, t);
        text.textWrappingMode = TMPro.TextWrappingModes.Normal;
        text.lineSpacing = 8f;
    }

    protected override void OnOpened() { }
}
