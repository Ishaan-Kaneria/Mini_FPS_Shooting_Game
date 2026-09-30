#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The level select, built from the UI kit: the screen an arena card's ALL LEVELS opens.
    ///
    ///   header   BACK, the arena's stripe and name, STORY (phone), "14/24 ★", "5/8 CLEARED"
    ///   story    whose zone this is and the zone's opening (desk and tablet)
    ///   grid     one compact tile per level (see <see cref="LevelButton"/>), scrolling down
    ///   detail   the chosen level in full, and PLAY (see <see cref="LevelDetail"/>)
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
            UIButtonSound.On(back.gameObject, UIButtonSound.Voice.Back);

            var stripe = UIKit.Rect(header, "Stripe").gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;
            stripe.color = t.accent;
            UIKit.Size(stripe, 3f, 36f);

            var arenaName = UIKit.Text(header, "ArenaName", "ARENA", UIKit.TextRole.Title, t);
            arenaName.textWrappingMode = TextWrappingModes.NoWrap;
            arenaName.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(arenaName, flexWidth: 1f);

            var storyButton = UIKit.Button(header, "Story", "Story", FlatButton.Variant.Secondary, "book", t);
            storyButton.gameObject.SetActive(false);

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
            storyText.overflowMode = TextOverflowModes.Ellipsis;
            storyText.maxVisibleLines = 2;

            // ---- body: the grid, and the pane beside it ---------------------------------
            var bodyRow = UIKit.Rect(content, "Body");
            UIKit.Size(bodyRow, flexHeight: 1f);
            var brow = UIKit.Row(bodyRow, 20f, null, TextAnchor.UpperLeft);
            brow.childForceExpandHeight = true;
            brow.childControlHeight = true;

            var viewport = UIKit.Rect(bodyRow, "Levels");
            // Fixed shares of the width, never content: the pane's width followed its longest
            // line, so a different level resized the grid under the pointer, which chose a
            // different tile, which resized it again -- the screen shook.
            var vle = UIKit.Size(viewport, flexWidth: 1.9f, flexHeight: 1f);
            vle.minWidth = 0f; vle.preferredWidth = 0f;
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<FlatRect>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.horizontal = false;
            scroll.vertical = true;

            var grid = UIKit.Rect(viewport, "Grid");
            grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0f, 1f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            // A starting size only: LevelSelectPanel works the cell out from the real box.
            layout.cellSize = new Vector2(140f, 110f);
            layout.spacing = new Vector2(10f, 10f);
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 8;
            var fit = grid.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = grid;

            var detail = BuildLevelDetail(bodyRow, t);

            // ---- status: only ever says something when the ladder itself is broken ----
            var status = UIKit.Text(shade.transform, "Status", "", UIKit.TextRole.Caption, t);
            status.color = t.danger;
            var srt = status.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f); srt.pivot = new Vector2(0f, 0f);
            srt.offsetMin = new Vector2(40f, 4f); srt.offsetMax = new Vector2(-40f, 22f);

            select.tileParent = grid;
            select.scroll = scroll;
            select.tileTemplate = BuildLevelTileTemplate(shade.rectTransform, t);
            select.detail = detail;
            select.storyButton = storyButton;
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
            select.gridColumns = 8;
            select.tileAspect = 0.8f;
            select.minTileWidth = 132f;

            menu.levelSelect = select;
            shade.gameObject.SetActive(false);
        }

        /// <summary>
        /// One level tile, built once and left switched off. Parented to the panel rather than
        /// to the grid, for the reason the arena card template is: a template inside the grid
        /// would be counted as a cell.
        ///
        ///   01            NEXT  skull
        ///   ONE MAGAZINE
        ///   ★ ★ ☆    (a lock while locked)
        /// </summary>
        static LevelButton BuildLevelTileTemplate(RectTransform parent, UITheme t)
        {
            var frame = UIKit.Panel(parent, "LevelTileTemplate", UIKit.PanelTone.Panel, t);
            frame.raycastTarget = true;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(140f, 110f);
            var col = UIKit.Column(frame, 4f, new RectOffset(12, 10, 8, 10));
            col.childForceExpandHeight = false;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;

            var head = UIKit.Rect(frame.transform, "Head");
            UIKit.Row(head, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            var number = UIKit.Text(head, "Number", "01", UIKit.TextRole.Number, t);
            number.fontSize = t.sizeTitle - 2f;
            UIKit.Size(number, flexWidth: 1f);
            var next = Tag(head, "Next", "NEXT", t.accent, t.textOnAccent, t);
            var boss = UIKit.Icon(head, "Boss", "skull", 18f, t.danger, t).gameObject;

            var name = UIKit.Text(frame.transform, "Name", "LEVEL", UIKit.TextRole.Label, t);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.characterSpacing = t.labelSpacing * 0.4f;

            var flex = UIKit.Rect(frame.transform, "Flex");
            UIKit.Size(flex, flexHeight: 1f);

            var foot = UIKit.Rect(frame.transform, "Foot");
            UIKit.Row(foot, 0f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(foot).minHeight = 18f;
            var stars = UIKit.Rect(foot, "Stars");
            UIKit.Row(stars, 3f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            var icons = new Image[3];
            for (int i = 0; i < 3; i++) icons[i] = UIKit.Icon(stars, $"Star{i + 1}", "star", 17f, t.textDisabled, t);
            var lockIcon = UIKit.Icon(foot, "Lock", "lock", 17f, t.textDisabled, t);

            var button = frame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = frame;
            UIButtonSound.On(frame.gameObject, UIButtonSound.Voice.Silent);

            var tile = frame.gameObject.AddComponent<LevelButton>();
            tile.button = button;
            tile.frame = frame;
            tile.numberText = number;
            tile.nameText = name;
            tile.starIcons = icons;
            tile.starRow = stars.gameObject;
            tile.lockIcon = lockIcon;
            tile.bossTag = boss;
            tile.nextTag = next;
            frame.gameObject.SetActive(false);
            return tile;
        }

        /// <summary>
        /// The pane beside the grid. The picture takes whatever height the text leaves, and
        /// PLAY sits at the bottom where a thumb or the eye ends up.
        /// </summary>
        static LevelDetail BuildLevelDetail(RectTransform parent, UITheme t)
        {
            var pane = UIKit.Panel(parent, "Detail", UIKit.PanelTone.Panel, t);
            var ple0 = UIKit.Size(pane, flexWidth: 1f, flexHeight: 1f);
            ple0.minWidth = 0f; ple0.preferredWidth = 0f;
            var pcol = UIKit.Column(pane, 0f, new RectOffset(1, 1, 1, 1));
            pcol.childForceExpandHeight = false;
            pcol.childControlHeight = true;
            var detail = pane.gameObject.AddComponent<LevelDetail>();

            // ---- level view ------------------------------------------------------------
            var level = UIKit.Rect(pane.transform, "Level");
            UIKit.Size(level, flexHeight: 1f);
            var lcol = UIKit.Column(level, 0f);
            lcol.childForceExpandHeight = false;
            lcol.childControlHeight = true;

            var pictureArea = UIKit.Rect(level, "Picture");
            var ple = UIKit.Size(pictureArea, flexHeight: 1f);
            ple.minHeight = 0f;
            ple.preferredHeight = 240f;
            pictureArea.gameObject.AddComponent<RectMask2D>();
            var picture = UIKit.Rect(pictureArea, "Image").gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            UIKit.Fill(picture.rectTransform);
            var nextTag = CornerTag(pictureArea, "Next", "NEXT", t.accent, t.textOnAccent, left: true, t);
            var bossTag = CornerTag(pictureArea, "Boss", "BOSS", t.danger, t.textPrimary, left: false, t);

            var stripeRect = UIKit.Rect(level, "Stripe");
            UIKit.Size(stripeRect, height: 3f);
            var stripe = stripeRect.gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;

            var body = UIKit.Rect(level, "Body");
            var bcol = UIKit.Column(body, 6f, new RectOffset(22, 22, 16, 18));
            bcol.childForceExpandHeight = false;
            bcol.childControlHeight = true;

            var number = UIKit.Text(body, "Number", "LEVEL 01", UIKit.TextRole.Label, t);
            number.color = t.accent;
            var name = UIKit.Text(body, "Name", "LEVEL", UIKit.TextRole.Title, t);
            name.fontSize = t.sizeTitle - 2f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var facts = UIKit.Rect(body, "Facts");
            UIKit.Row(facts, 6f, new RectOffset(0, 0, 2, 4), TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Icon(facts, "EnemiesIcon", "skull", 18f, t.textSecondary, t);
            var enemies = FactText(facts, "Enemies", t);
            var gap = UIKit.Rect(facts, "Gap");
            UIKit.Size(gap, 14f);
            UIKit.Icon(facts, "TimeIcon", "clock", 18f, t.textSecondary, t);
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
                var rowLayout = UIKit.Row(row, 10f, new RectOffset(0, 0, 2, 2), TextAnchor.MiddleLeft);
                rowLayout.childForceExpandWidth = false;
                icons[i] = UIKit.Icon(row, "Icon", "star", 20f, t.textSecondary, t);
                var text = UIKit.Text(row, "Condition", "", UIKit.TextRole.Body, t);
                text.fontSize = t.sizeBody - 1f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                UIKit.Size(text, flexWidth: 1f);
                texts[i] = text;
            }

            var weight = UIKit.Text(body, "Weight", "", UIKit.TextRole.Caption, t);
            weight.textWrappingMode = TextWrappingModes.NoWrap;
            weight.overflowMode = TextOverflowModes.Ellipsis;
            weight.color = t.textDisabled;

            var best = UIKit.Text(body, "Best", "", UIKit.TextRole.Label, t);
            best.textWrappingMode = TextWrappingModes.NoWrap;
            best.overflowMode = TextOverflowModes.Ellipsis;

            var play = UIKit.Button(body, "Play", "Play level", FlatButton.Variant.Primary, "player-play", t);
            UIKit.Size(play, height: 56f);
            play.label.fontSize = t.sizeHeading;
            UIButtonSound.On(play.gameObject, UIButtonSound.Voice.Launch);

            // ---- story view (handset) ----------------------------------------------------
            var story = UIKit.Rect(pane.transform, "Story");
            UIKit.Size(story, flexHeight: 1f);
            var scol = UIKit.Column(story, 10f, new RectOffset(24, 24, 20, 20));
            scol.childForceExpandHeight = false;
            scol.childControlHeight = true;
            var storyTitle = UIKit.Text(story, "Holder", "THE STORY", UIKit.TextRole.Label, t);
            storyTitle.color = t.accent;
            var storyText = UIKit.Text(story, "Opening", "", UIKit.TextRole.Body, t);
            storyText.textWrappingMode = TextWrappingModes.Normal;
            story.gameObject.SetActive(false);

            detail.levelView = level.gameObject;
            detail.picture = picture;
            detail.stripe = stripe;
            detail.nextTag = nextTag;
            detail.bossTag = bossTag;
            detail.numberText = number;
            detail.nameText = name;
            detail.enemiesText = enemies;
            detail.timeText = time;
            detail.starIcons = icons;
            detail.conditionTexts = texts;
            detail.weightText = weight;
            detail.bestText = best;
            detail.playButton = play;
            detail.storyView = story.gameObject;
            detail.storyTitleText = storyTitle;
            detail.storyText = storyText;
            return detail;
        }

        static TMP_Text FactText(RectTransform parent, string name, UITheme t)
        {
            var text = UIKit.Text(parent, name, "", UIKit.TextRole.Label, t);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>A small solid tag in a row: NEXT on a tile.</summary>
        static GameObject Tag(RectTransform parent, string name, string caption, Color fill, Color ink, UITheme t)
        {
            var tag = UIKit.Panel(parent, name, UIKit.PanelTone.Raised, t);
            tag.color = fill;
            tag.borderColor = new Color(0, 0, 0, 0);
            UIKit.Row(tag, 0f, new RectOffset(6, 6, 1, 1), TextAnchor.MiddleCenter);
            var text = UIKit.Text(tag.transform, "Label", caption, UIKit.TextRole.Label, t);
            text.color = ink;
            text.fontSize = t.sizeCaption;
            text.characterSpacing = t.labelSpacing * 0.5f;
            return tag.gameObject;
        }

        /// <summary>A solid tag in a corner of the picture: NEXT in amber, BOSS in red.</summary>
        static GameObject CornerTag(RectTransform parent, string name, string caption, Color fill, Color ink, bool left, UITheme t)
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
