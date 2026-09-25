#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The level select, built from the UI kit: the screen an arena card's ALL LEVELS opens.
    ///
    ///   header   BACK, the arena's stripe and name, "14/24 ★", "5/8 CLEARED"
    ///   story    whose zone this is and the zone's opening, at reading size
    ///   grid     one card per level (see <see cref="LevelButton"/>)
    ///
    /// Built into the dashboard scene rather than a scene of its own, because it is the same
    /// screen with the arenas swapped for that arena's ladder. Everything shown is bound at
    /// runtime by <see cref="LevelSelectPanel"/>, so this builds shapes with nothing typed in.
    /// </summary>
    public static partial class FPSKitMenuBuilder
    {
        static void BuildLevelSelect(RectTransform root, MainMenuController menu)
        {
            var t = UITheme.Active;

            var shade = UIKit.Panel(root, "LevelSelect", UIKit.PanelTone.Background, t);
            shade.borderColor = new Color(0, 0, 0, 0);
            // A raycast target, so a click that misses a card does not fall through to an
            // arena card underneath and select a different arena entirely.
            shade.raycastTarget = true;
            UIKit.Fill(shade.rectTransform);

            // The component goes on the canvas, not on the panel it switches on and off:
            // one that hides its own object in Awake never gets to show it again.
            var select = root.gameObject.AddComponent<LevelSelectPanel>();
            select.panel = shade.gameObject;

            var content = UIKit.Rect(shade.transform, "Content");
            UIKit.Fill(content, 40f, 24f, 40f, 24f);
            var col = UIKit.Column(content, 16f);
            col.childForceExpandHeight = false;
            col.childControlHeight = true;

            // ---- header ------------------------------------------------------------
            var header = UIKit.Rect(content, "Header");
            var hrow = UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft);
            hrow.childForceExpandHeight = false;
            UIKit.Size(header, height: 52f);

            var back = UIKit.Button(header, "Back", "Back", FlatButton.Variant.Secondary, "arrow-left", t);
            back.gameObject.AddComponent<UIButtonSound>().voice = UIButtonSound.Voice.Back;

            var stripe = UIKit.Rect(header, "Stripe").gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;
            stripe.color = t.accent;
            UIKit.Size(stripe, 3f, 36f);

            var arenaName = UIKit.Text(header, "ArenaName", "ARENA", UIKit.TextRole.Title, t);
            arenaName.textWrappingMode = TextWrappingModes.NoWrap;
            arenaName.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(arenaName, flexWidth: 1f);

            var stars = UIKit.Text(header, "Stars", "0/24", UIKit.TextRole.Heading, t);
            stars.color = t.accent;
            stars.textWrappingMode = TextWrappingModes.NoWrap;

            var divider = UIKit.Rect(header, "Divider").gameObject.AddComponent<FlatRect>();
            divider.raycastTarget = false;
            divider.color = t.border;
            UIKit.Size(divider, 1f, 28f);

            var cleared = UIKit.Text(header, "Cleared", "0/8 CLEARED", UIKit.TextRole.Heading, t);
            cleared.textWrappingMode = TextWrappingModes.NoWrap;

            // ---- story ---------------------------------------------------------------
            var story = UIKit.Panel(content, "Story", UIKit.PanelTone.Panel, t);
            story.stripeSide = FlatRect.Side.Left;
            story.stripeColor = t.accent;
            story.stripePixels = t.stripePixels;
            var scol = UIKit.Column(story, 6f, new RectOffset(24, 24, 14, 16));
            scol.childForceExpandHeight = false;
            scol.childControlHeight = true;
            var storyTitle = UIKit.Text(story.transform, "Holder", "THE STORY", UIKit.TextRole.Label, t);
            storyTitle.color = t.textSecondary;
            var storyText = UIKit.Text(story.transform, "Opening", "", UIKit.TextRole.Body, t);
            storyText.color = t.textPrimary;
            storyText.textWrappingMode = TextWrappingModes.Normal;

            // ---- grid ----------------------------------------------------------------
            var viewport = UIKit.Rect(content, "Levels");
            UIKit.Size(viewport, flexHeight: 1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<FlatRect>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.horizontal = false;
            scroll.vertical = false;

            var grid = UIKit.Rect(viewport, "Grid");
            grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0.5f, 1f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            // A starting size only: LevelSelectPanel works the cell out from the real box.
            layout.cellSize = new Vector2(440f, 340f);
            layout.spacing = new Vector2(16f, 16f);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            var fit = grid.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = grid;

            // ---- status: only ever says something when the ladder itself is broken ----
            var status = UIKit.Text(shade.transform, "Status", "", UIKit.TextRole.Caption, t);
            status.color = t.danger;
            var srt = status.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f); srt.pivot = new Vector2(0f, 0f);
            srt.offsetMin = new Vector2(40f, 4f); srt.offsetMax = new Vector2(-40f, 22f);

            select.tileParent = grid;
            select.scroll = scroll;
            select.tileTemplate = BuildLevelCardTemplate(shade.rectTransform, t);
            select.backButton = back;
            select.arenaNameText = arenaName;
            select.arenaStripe = stripe;
            select.starsText = stars;
            select.clearedText = cleared;
            select.storyBlock = story.gameObject;
            select.storyTitleText = storyTitle;
            select.hintText = storyText;
            select.statusText = status;
            select.campaign = FPSKitCampaign.GetOrCreate();
            select.gridColumns = 4;
            select.tileAspect = 0.78f;

            menu.levelSelect = select;
            shade.gameObject.SetActive(false);
        }

        /// <summary>
        /// One level card, built once and left switched off. Parented to the panel rather than
        /// to the grid, for the reason the arena card template is: a template inside the grid
        /// would be counted as a cell. The strip takes whatever height the text leaves, so a
        /// card of any size keeps every line of its text.
        /// </summary>
        static LevelButton BuildLevelCardTemplate(RectTransform parent, UITheme t)
        {
            var frame = UIKit.Panel(parent, "LevelCardTemplate", UIKit.PanelTone.Panel, t);
            frame.raycastTarget = true;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(440f, 340f);
            var col = UIKit.Column(frame, 0f, new RectOffset(1, 1, 1, 1));
            col.childForceExpandHeight = false;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;

            // ---- strip -------------------------------------------------------------
            var stripArea = UIKit.Rect(frame.transform, "Strip");
            var sle = UIKit.Size(stripArea, flexHeight: 1f);
            sle.minHeight = 48f;
            var strip = UIKit.Rect(stripArea, "Image").gameObject.AddComponent<RawImage>();
            strip.raycastTarget = false;
            UIKit.Fill(strip.rectTransform);

            var shade = UIKit.Panel(stripArea, "Locked", UIKit.PanelTone.Background, t);
            shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.6f);
            shade.borderColor = new Color(0, 0, 0, 0);
            UIKit.Fill(shade.rectTransform);
            var lockRow = UIKit.Row(shade, 10f, null, TextAnchor.MiddleCenter);
            lockRow.childForceExpandWidth = false;
            UIKit.Icon(shade.transform, "Icon", "lock", 22f, t.textPrimary, t);
            var lockText = UIKit.Text(shade.transform, "Requirement", "LOCKED", UIKit.TextRole.Label, t);
            lockText.color = t.textPrimary;
            lockText.textWrappingMode = TextWrappingModes.NoWrap;

            var next = Tag(stripArea, "Next", "NEXT", t.accent, t.textOnAccent, left: true, t);
            var boss = Tag(stripArea, "Boss", "BOSS", t.danger, t.textPrimary, left: false, t);

            var stripeRect = UIKit.Rect(frame.transform, "Stripe");
            UIKit.Size(stripeRect, height: 3f);
            var stripe = stripeRect.gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;

            // ---- body ----------------------------------------------------------------
            var body = UIKit.Rect(frame.transform, "Body");
            var bcol = UIKit.Column(body, 5f, new RectOffset(14, 14, 10, 12));
            bcol.childForceExpandHeight = false;
            bcol.childControlHeight = true;

            var head = UIKit.Rect(body, "Head");
            UIKit.Row(head, 10f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(head).minHeight = 30f;
            var number = UIKit.Text(head, "Number", "01", UIKit.TextRole.Number, t);
            number.fontSize = t.sizeHeading;
            number.color = t.accent;
            var name = UIKit.Text(head, "Name", "LEVEL", UIKit.TextRole.Heading, t);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(name, flexWidth: 1f);

            var facts = UIKit.Rect(body, "Facts");
            UIKit.Row(facts, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(facts).minHeight = 22f;
            UIKit.Icon(facts, "EnemiesIcon", "skull", 16f, t.textSecondary, t);
            var enemies = FactText(facts, "Enemies", t);
            var gap = UIKit.Rect(facts, "Gap");
            UIKit.Size(gap, 10f);
            UIKit.Icon(facts, "TimeIcon", "clock", 16f, t.textSecondary, t);
            var time = FactText(facts, "Time", t);

            var rule = UIKit.Rect(body, "Rule").gameObject.AddComponent<FlatRect>();
            rule.raycastTarget = false;
            rule.color = t.border;
            UIKit.Size(rule, height: 1f);

            var icons = new Image[3];
            var texts = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                var row = UIKit.Rect(body, $"Star{i + 1}");
                var rowLayout = UIKit.Row(row, 8f, null, TextAnchor.MiddleLeft);
                rowLayout.childForceExpandWidth = false;
                rowLayout.childControlHeight = true;
                // A minimum, not a height: TMP with Ellipsis draws nothing at all when one line
                // does not fit, and a handset's text floor makes the line taller than 20.
                UIKit.Size(row).minHeight = 20f;
                icons[i] = UIKit.Icon(row, "Icon", "star", 16f, t.textSecondary, t);
                var text = UIKit.Text(row, "Condition", "", UIKit.TextRole.Caption, t);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                UIKit.Size(text, flexWidth: 1f);
                texts[i] = text;
            }

            var weight = UIKit.Text(body, "Weight", "", UIKit.TextRole.Caption, t);
            weight.textWrappingMode = TextWrappingModes.NoWrap;
            weight.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(weight).minHeight = 18f;

            var best = UIKit.Text(body, "Best", "", UIKit.TextRole.Label, t);
            best.textWrappingMode = TextWrappingModes.NoWrap;
            best.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(best).minHeight = 20f;

            var button = frame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = frame;
            // A card starts a level, so it gets the two-note launch rather than a click.
            frame.gameObject.AddComponent<UIButtonSound>().voice = UIButtonSound.Voice.Launch;

            var card = frame.gameObject.AddComponent<LevelButton>();
            card.button = button;
            card.frame = frame;
            card.strip = strip;
            card.stripe = stripe;
            card.lockShade = shade.gameObject;
            card.lockText = lockText;
            card.nextTag = next;
            card.bossTag = boss;
            card.numberText = number;
            card.nameText = name;
            card.enemiesText = enemies;
            card.timeText = time;
            card.starIcons = icons;
            card.conditionTexts = texts;
            card.weightText = weight;
            card.bestText = best;
            frame.gameObject.SetActive(false);
            return card;
        }

        static TMP_Text FactText(RectTransform parent, string name, UITheme t)
        {
            var text = UIKit.Text(parent, name, "", UIKit.TextRole.Label, t);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>A solid tag in a corner of the strip: NEXT in amber, BOSS in red.</summary>
        static GameObject Tag(RectTransform parent, string name, string caption, Color fill, Color ink, bool left, UITheme t)
        {
            var tag = UIKit.Panel(parent, name, UIKit.PanelTone.Raised, t);
            tag.color = fill;
            tag.borderColor = new Color(0, 0, 0, 0);
            var rt = tag.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(left ? 10f : -10f, -10f);
            UIKit.Row(tag, 0f, new RectOffset(10, 10, 3, 3), TextAnchor.MiddleCenter);
            var fit = tag.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = UIKit.Text(tag.transform, "Label", caption, UIKit.TextRole.Label, t);
            text.color = ink;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return tag.gameObject;
        }
    }
}
#endif
