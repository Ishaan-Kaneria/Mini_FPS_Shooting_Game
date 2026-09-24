#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The PLAY screen and the top bar every menu screen shares, built from the UI kit.
    ///
    ///   top bar      logo, PLAY / LOADOUT / ACHIEVEMENTS / STORE, rank and XP, coins,
    ///                Settings, Info, Quit
    ///   left         WELCOME BACK, the arena grid, YOUR LOADOUT
    ///   right        CURRENT MISSION with PLAY MISSION, NEXT REWARD
    ///   footer       "Single Player Campaign | Version x"
    ///
    /// Everything is placed by anchors and layout groups, never pixels from an edge, so it
    /// holds from 4:3 to 21:9; <see cref="MainMenuController.ApplyFormLayout"/> rearranges it
    /// for a handset. What is shown is bound at runtime from the save, so this builds shapes
    /// with nothing typed into them.
    /// </summary>
    public static partial class FPSKitMenuBuilder
    {
        const float TopBarHeight = 88f;

        static void BuildDashboard(RectTransform root, MainMenuController menu)
        {
            var t = UITheme.Active;
            menu.topBarHeight = TopBarHeight;

            var backdrop = UIKit.Panel(root, "Backdrop", UIKit.PanelTone.Background, t);
            UIKit.Fill(backdrop.rectTransform);

            var main = UIKit.Rect(root, "Play");
            UIKit.Fill(main, 40f, TopBarHeight + 24f, 40f, 44f);

            // Left: welcome, arenas, loadout.
            var left = UIKit.Rect(main, "Left");
            UIKit.Anchor(left, 0f, 0f, 0.685f, 1f);
            var lcol = UIKit.Column(left, 14f);
            lcol.childForceExpandHeight = false;
            menu.leftColumn = left;

            menu.welcomeText = UIKit.Text(left, "Welcome", "WELCOME BACK, OPERATIVE", UIKit.TextRole.Title, t);
            menu.rankLine = UIKit.Text(left, "RankLine", "", UIKit.TextRole.Label, t);
            var heading = UIKit.Text(left, "SelectArena", "Select arena", UIKit.TextRole.Heading, t);
            heading.color = t.textSecondary;

            BuildArenaScroller(left, menu, t);
            var strip = BuildLoadoutStrip(left, menu, t);

            // Right: the mission, and what is next.
            var right = UIKit.Rect(main, "Right");
            UIKit.Anchor(right, 0.7f, 0f, 1f, 1f);
            var rcol = UIKit.Column(right, 16f);
            rcol.childForceExpandHeight = false;
            menu.rightColumn = right;
            BuildMission(right, menu, t);
            var reward = BuildNextReward(right, menu, t);

            var footer = UIKit.Text(root, "Footer", "", UIKit.TextRole.Caption, t);
            var frt = footer.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0f); frt.pivot = new Vector2(0f, 0f);
            frt.offsetMin = new Vector2(40f, 10f); frt.offsetMax = new Vector2(-40f, 34f);
            footer.textWrappingMode = TextWrappingModes.NoWrap;
            footer.characterSpacing = t.labelSpacing * 0.5f;
            menu.footerText = footer;

            BuildTopBar(root, menu, t);
            BuildToasts(root, menu, t);
            BuildQuitConfirm(root, menu, t);

            menu.dashboardOnly = new[] { main.gameObject, footer.gameObject };
            menu.hideOnHandset = new[] { strip, reward };
        }

        // ==================================================================
        // Top bar.
        // ==================================================================

        static void BuildTopBar(RectTransform root, MainMenuController menu, UITheme t)
        {
            var bar = UIKit.Panel(root, "TopBar", UIKit.PanelTone.Panel, t);
            bar.borderColor = new Color(0, 0, 0, 0);
            bar.stripeSide = FlatRect.Side.Bottom;
            bar.stripeColor = t.border;
            bar.stripePixels = t.borderPixels;
            bar.raycastTarget = true;
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, TopBarHeight);
            rt.anchoredPosition = Vector2.zero;
            var row = UIKit.Row(bar, 18f, new RectOffset(32, 24, 0, 0), TextAnchor.MiddleLeft);
            row.childForceExpandHeight = false;

            var logo = UIKit.Rect(bar.transform, "Logo");
            UIKit.Row(logo, 12f, null, TextAnchor.MiddleLeft);
            var mark = UIKit.Rect(logo, "Mark").gameObject.AddComponent<FlatRect>();
            mark.raycastTarget = false;
            mark.color = t.accent;
            UIKit.Size(mark, 6f, 32f);
            UIKit.Text(logo, "Name", "Mini FPS", UIKit.TextRole.Title, t);

            var gap = UIKit.Rect(bar.transform, "Gap");
            UIKit.Size(gap, 24f);

            var tabs = UIKit.TabBar(bar.transform, "Tabs", new[] { "Play", "Loadout", "Achievements", "Store" }, t);
            // Full height, so the selected tab's amber line lands on the bar's own rule.
            UIKit.Size(tabs, height: TopBarHeight);
            var tabsRule = tabs.GetComponent<FlatRect>();
            if (tabsRule != null) tabsRule.stripeColor = new Color(0, 0, 0, 0);
            foreach (var tab in tabs.tabs) UIKit.Size(tab, height: TopBarHeight);

            var flex = UIKit.Rect(bar.transform, "Flex");
            UIKit.Size(flex, flexWidth: 1f);

            // Rank and XP.
            var rank = UIKit.Rect(bar.transform, "Rank");
            UIKit.Column(rank, 4f, null, TextAnchor.MiddleLeft);
            UIKit.Size(rank, 240f);
            var rankText = UIKit.Text(rank, "Title", "RECRUIT  LV 1", UIKit.TextRole.Heading, t);
            rankText.fontSize = t.sizeLabel + 3f;
            var xpBar = UIKit.ProgressBar(rank, "Xp", t.accent, 4f, false, t);
            var xpText = UIKit.Text(rank, "XpText", "0 / 500 XP", UIKit.TextRole.Caption, t);
            xpText.textWrappingMode = TextWrappingModes.NoWrap;

            var divider = UIKit.Rect(bar.transform, "Divider").gameObject.AddComponent<FlatRect>();
            divider.raycastTarget = false;
            divider.color = t.border;
            UIKit.Size(divider, 1f, 40f);

            var coins = UIKit.Rect(bar.transform, "Coins");
            UIKit.Row(coins, 8f, null, TextAnchor.MiddleLeft);
            UIKit.Icon(coins, "Icon", "coin", 24f, t.accent, t);
            var coinText = UIKit.Text(coins, "Amount", "0", UIKit.TextRole.Number, t);
            coinText.color = t.accent;
            coinText.fontSize = t.sizeHeading + 2f;

            var icons = UIKit.Rect(bar.transform, "Icons");
            UIKit.Row(icons, 8f, null, TextAnchor.MiddleRight);
            var settings = UIKit.IconButton(icons, "Settings", "settings", "Settings", UIKit.ControlHeight, t);
            var info = UIKit.IconButton(icons, "Info", "info-circle", "How to play, the list, about", UIKit.ControlHeight, t);
            var quit = UIKit.IconButton(icons, "Quit", "power", "Quit", UIKit.ControlHeight, t);

            var top = bar.gameObject.AddComponent<MenuTopBar>();
            top.tabs = tabs;
            top.rankText = rankText;
            top.xpBar = xpBar;
            top.xpText = xpText;
            top.coinText = coinText;
            top.settingsButton = settings;
            top.infoButton = info;
            top.quitButton = quit;
            top.rankBlock = rank.gameObject;
            menu.topBar = top;
        }

        // ==================================================================
        // Arenas.
        // ==================================================================

        static void BuildArenaScroller(RectTransform parent, MainMenuController menu, UITheme t)
        {
            var viewport = UIKit.Rect(parent, "Arenas");
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

            var content = UIKit.Rect(viewport, "Grid");
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(16f, 16f);
            grid.cellSize = new Vector2(380f, 340f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            menu.cardScroll = scroll;
            menu.cardParent = content;
            menu.cardTemplate = BuildArenaCardTemplate(content, t);
        }

        /// <summary>One arena card, cloned per arena at runtime. Parts anchored in fractions of the card.</summary>
        static ArenaCard BuildArenaCardTemplate(RectTransform parent, UITheme t)
        {
            var frame = UIKit.Panel(parent, "ArenaCardTemplate", UIKit.PanelTone.Panel, t);
            frame.raycastTarget = true;
            var root = frame.rectTransform;
            const float split = 0.40f;

            var thumbRect = UIKit.Rect(root, "Thumbnail");
            UIKit.Anchor(thumbRect, 0f, split, 1f, 1f);
            thumbRect.offsetMin = new Vector2(1f, 0f); thumbRect.offsetMax = new Vector2(-1f, -1f);
            var thumb = thumbRect.gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;

            var stripeRect = UIKit.Rect(root, "Stripe");
            stripeRect.anchorMin = new Vector2(0f, split); stripeRect.anchorMax = new Vector2(1f, split);
            stripeRect.pivot = new Vector2(0.5f, 1f);
            stripeRect.offsetMin = new Vector2(1f, -3f); stripeRect.offsetMax = new Vector2(-1f, 0f);
            var stripe = stripeRect.gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;

            var shade = UIKit.Panel(root, "Locked", UIKit.PanelTone.Background, t);
            shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.55f);
            UIKit.Anchor(shade.rectTransform, 0f, split, 1f, 1f);
            var lockCol = UIKit.Column(shade, 6f, new RectOffset(16, 16, 12, 12), TextAnchor.MiddleCenter);
            lockCol.childForceExpandWidth = true;
            var lockIcon = UIKit.Icon(shade.transform, "Icon", "lock", 30f, t.textPrimary, t);
            lockIcon.GetComponent<LayoutElement>().flexibleWidth = 0f;
            var lockText = UIKit.Text(shade.transform, "Requirement", "LOCKED", UIKit.TextRole.Label, t);
            lockText.color = t.textPrimary;
            lockText.alignment = TextAlignmentOptions.Center;
            lockText.textWrappingMode = TextWrappingModes.Normal;

            var info = UIKit.Rect(root, "Info");
            UIKit.Anchor(info, 0f, 0f, 1f, split);
            info.offsetMin = new Vector2(16f, 12f); info.offsetMax = new Vector2(-16f, -14f);
            var col = UIKit.Column(info, 4f);
            col.childForceExpandHeight = false;
            var name = UIKit.Text(info, "Name", "ARENA", UIKit.TextRole.Heading, t);
            name.fontSize = t.sizeHeading - 2f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var desc = UIKit.Text(info, "Description", "", UIKit.TextRole.Caption, t);
            desc.textWrappingMode = TextWrappingModes.NoWrap;
            desc.overflowMode = TextOverflowModes.Ellipsis;
            var flex = UIKit.Rect(info, "Flex");
            UIKit.Size(flex, flexHeight: 1f);

            var bottom = UIKit.Rect(info, "Bottom");
            UIKit.Row(bottom, 8f, null, TextAnchor.MiddleLeft);
            UIKit.Size(bottom, height: 22f);
            var progress = UIKit.Text(bottom, "Progress", "", UIKit.TextRole.Label, t);
            progress.fontSize = t.sizeLabel - 1f;
            progress.characterSpacing = t.labelSpacing * 0.5f;
            UIKit.Size(progress, flexWidth: 1f);
            var bars = DifficultyBars(bottom, t);
            var diff = UIKit.Text(bottom, "Difficulty", "EASY", UIKit.TextRole.Label, t);
            diff.fontSize = t.sizeLabel - 1f;

            var button = frame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = frame;

            var card = frame.gameObject.AddComponent<ArenaCard>();
            card.button = button;
            card.frame = frame;
            card.thumbnail = thumb;
            card.stripe = stripe;
            card.lockShade = shade.gameObject;
            card.lockText = lockText;
            card.nameText = name;
            card.descriptionText = desc;
            card.progressText = progress;
            card.difficultyText = diff;
            card.difficultyBars = bars;
            frame.gameObject.SetActive(false);
            return card;
        }

        /// <summary>Three bars of rising height: how many are lit is how hard it is.</summary>
        static FlatRect[] DifficultyBars(RectTransform parent, UITheme t)
        {
            var holder = UIKit.Rect(parent, "Bars");
            UIKit.Row(holder, 2f, null, TextAnchor.LowerLeft);
            UIKit.Size(holder, 20f, 16f);
            var bars = new FlatRect[3];
            for (int i = 0; i < 3; i++)
            {
                var b = UIKit.Rect(holder, "Bar" + i).gameObject.AddComponent<FlatRect>();
                b.raycastTarget = false;
                b.color = t.border;
                UIKit.Size(b, 5f, 6f + i * 5f);
                bars[i] = b;
            }
            return bars;
        }

        // ==================================================================
        // Loadout strip.
        // ==================================================================

        static GameObject BuildLoadoutStrip(RectTransform parent, MainMenuController menu, UITheme t)
        {
            var panel = UIKit.Panel(parent, "YourLoadout", UIKit.PanelTone.Panel, t);
            var row = UIKit.Row(panel, 0f, new RectOffset(24, 20, 14, 14), TextAnchor.MiddleLeft);
            row.childForceExpandHeight = true;
            // No flexible height: a row that force-expands reports some, and the column would
            // hand this strip half of the arena grid's space.
            UIKit.Size(panel, height: 112f, flexHeight: 0f);

            var head = UIKit.Rect(panel.transform, "Heading");
            UIKit.Column(head, 10f, null, TextAnchor.MiddleLeft);
            UIKit.Size(head, 190f);
            UIKit.Text(head, "Title", "Your loadout", UIKit.TextRole.Heading, t).fontSize = t.sizeLabel + 3f;
            var customize = UIKit.Button(head, "Customize", "Customize", FlatButton.Variant.Secondary, null, t);

            var strip = panel.gameObject.AddComponent<LoadoutStrip>();
            strip.primary = Slot(panel.transform, "Primary", t);
            strip.secondary = Slot(panel.transform, "Secondary", t);
            strip.grenades = Slot(panel.transform, "Grenades", t);
            strip.medkits = Slot(panel.transform, "Medkits", t);
            strip.customizeButton = customize;
            menu.loadoutStrip = strip;
            return panel.gameObject;
        }

        static LoadoutStrip.Slot Slot(Transform parent, string name, UITheme t)
        {
            var cell = UIKit.Panel(parent, name, UIKit.PanelTone.Panel, t);
            cell.borderColor = new Color(0, 0, 0, 0);
            cell.stripeSide = FlatRect.Side.Left;
            cell.stripeColor = t.border;
            cell.stripePixels = t.borderPixels;
            UIKit.Row(cell, 12f, new RectOffset(18, 10, 0, 0), TextAnchor.MiddleLeft);
            UIKit.Size(cell, flexWidth: 1f);
            var icon = UIKit.Icon(cell.transform, "Icon", "rifle", 28f, t.textPrimary, t);
            var text = UIKit.Rect(cell.transform, "Text");
            UIKit.Column(text, 2f, null, TextAnchor.MiddleLeft);
            UIKit.Size(text, flexWidth: 1f);
            var kind = UIKit.Text(text, "Kind", name, UIKit.TextRole.Label, t);
            kind.fontSize = t.sizeCaption;
            var item = UIKit.Text(text, "Item", "", UIKit.TextRole.Heading, t);
            item.fontSize = t.sizeLabel + 2f;
            item.overflowMode = TextOverflowModes.Ellipsis;
            var count = UIKit.Text(cell.transform, "Count", "", UIKit.TextRole.Number, t);
            count.fontSize = t.sizeHeading;
            return new LoadoutStrip.Slot { icon = icon, kind = kind, item = item, count = count };
        }

        // ==================================================================
        // Right column.
        // ==================================================================

        static void BuildMission(RectTransform parent, MainMenuController menu, UITheme t)
        {
            var card = UIKit.Panel(parent, "CurrentMission", UIKit.PanelTone.Panel, t);
            UIKit.Size(card, flexHeight: 1f);
            var col = UIKit.Column(card, 10f, new RectOffset(24, 24, 20, 24));
            col.childForceExpandHeight = false;

            UIKit.Text(card.transform, "Heading", "Current mission", UIKit.TextRole.Heading, t).color = t.textSecondary;

            var thumbRect = UIKit.Rect(card.transform, "Thumbnail");
            UIKit.Size(thumbRect, height: 120f, flexHeight: 1f);
            var thumb = thumbRect.gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;
            var stripeRect = UIKit.Rect(card.transform, "Stripe");
            var stripe = stripeRect.gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;
            UIKit.Size(stripe, height: 3f);

            var arena = UIKit.Text(card.transform, "Arena", "", UIKit.TextRole.Title, t);
            arena.fontSize = t.sizeTitle - 4f;
            var level = UIKit.Text(card.transform, "Level", "", UIKit.TextRole.Heading, t);
            level.fontSize = t.sizeHeading - 2f;

            var objRow = UIKit.Rect(card.transform, "Objective");
            UIKit.Row(objRow, 10f, null, TextAnchor.UpperLeft);
            UIKit.Icon(objRow, "Icon", "target", 20f, t.accent, t);
            var objective = UIKit.Text(objRow, "Text", "", UIKit.TextRole.Body, t);
            objective.fontSize = t.sizeBody - 1f;
            objective.color = t.textSecondary;
            objective.maxVisibleLines = 2;
            objective.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(objective, flexWidth: 1f);

            var stats = UIKit.Panel(card.transform, "Stats", UIKit.PanelTone.Raised, t);
            UIKit.Row(stats, 18f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft);
            UIKit.Size(stats, height: 44f);
            UIKit.Icon(stats.transform, "EnemiesIcon", "skull", 18f, t.textSecondary, t);
            var enemies = StatText(stats.transform, "Enemies", t);
            UIKit.Icon(stats.transform, "TimeIcon", "clock", 18f, t.textSecondary, t);
            var time = StatText(stats.transform, "Time", t);
            var flex = UIKit.Rect(stats.transform, "Flex");
            UIKit.Size(flex, flexWidth: 1f);
            var bars = DifficultyBars((RectTransform)stats.transform, t);
            var diff = StatText(stats.transform, "Difficulty", t);

            var rewardsHead = UIKit.Text(card.transform, "Rewards", "Rewards", UIKit.TextRole.Label, t);
            rewardsHead.fontSize = t.sizeCaption;
            var rewards = UIKit.Rect(card.transform, "RewardRow");
            UIKit.Row(rewards, 10f, null, TextAnchor.MiddleLeft);
            UIKit.Size(rewards, height: 28f);
            UIKit.Icon(rewards, "CoinIcon", "coin", 22f, t.accent, t);
            var coins = UIKit.Text(rewards, "Coins", "", UIKit.TextRole.Heading, t);
            coins.color = t.accent;
            coins.fontSize = t.sizeLabel + 2f;
            var sp = UIKit.Rect(rewards, "Gap");
            UIKit.Size(sp, 16f);
            UIKit.Icon(rewards, "StarIcon", "star-filled", 22f, t.accent, t);
            var stars = UIKit.Text(rewards, "Stars", "", UIKit.TextRole.Heading, t);
            stars.color = t.accent;
            stars.fontSize = t.sizeLabel + 2f;

            var play = UIKit.Button(card.transform, "PlayMission", "Play mission", FlatButton.Variant.Primary, "player-play", t);
            UIKit.Size(play, height: 64f);
            play.label.fontSize = t.sizeHeading;
            if (play.icon != null) UIKit.Size(play.icon, 26f, 26f);
            play.gameObject.AddComponent<UIDefaultSelection>().priority = 1;
            var levels = UIKit.Button(card.transform, "AllLevels", "All levels", FlatButton.Variant.Quiet, "list-details", t);

            var m = card.gameObject.AddComponent<MissionCard>();
            m.thumbnail = thumb;
            m.stripe = stripe;
            m.arenaText = arena;
            m.levelText = level;
            m.objectiveText = objective;
            m.enemiesText = enemies;
            m.timeText = time;
            m.difficultyText = diff;
            m.difficultyBars = bars;
            m.coinsText = coins;
            m.starsText = stars;
            m.playButton = play;
            m.levelsButton = levels;
            m.compactHidden = new[] { thumbRect.gameObject, stripeRect.gameObject, objRow.gameObject, rewardsHead.gameObject };
            menu.mission = m;
        }

        static TMP_Text StatText(Transform parent, string name, UITheme t)
        {
            var s = UIKit.Text(parent, name, "", UIKit.TextRole.Label, t);
            s.fontSize = t.sizeLabel;
            s.color = t.textPrimary;
            return s;
        }

        static GameObject BuildNextReward(RectTransform parent, MainMenuController menu, UITheme t)
        {
            var card = UIKit.Panel(parent, "NextReward", UIKit.PanelTone.Panel, t);
            UIKit.Column(card, 10f, new RectOffset(24, 24, 18, 20)).childForceExpandHeight = false;
            UIKit.Size(card, height: 196f);

            var heading = UIKit.Text(card.transform, "Heading", "Next reward", UIKit.TextRole.Heading, t);
            heading.color = t.textSecondary;
            var row = UIKit.Rect(card.transform, "Item");
            UIKit.Row(row, 14f, null, TextAnchor.MiddleLeft);
            var icon = UIKit.Icon(row, "Icon", "shopping-cart", 30f, t.textPrimary, t);
            var text = UIKit.Rect(row, "Text");
            UIKit.Column(text, 2f);
            UIKit.Size(text, flexWidth: 1f);
            var name = UIKit.Text(text, "Name", "", UIKit.TextRole.Heading, t);
            name.fontSize = t.sizeLabel + 3f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var detail = UIKit.Text(text, "Detail", "", UIKit.TextRole.Caption, t);
            var action = UIKit.Button(row, "OpenStore", "Store", FlatButton.Variant.Secondary, "shopping-cart", t);
            var bar = UIKit.ProgressBar(card.transform, "Progress", t.accent, 6f, false, t);
            var barText = UIKit.Text(card.transform, "ProgressText", "", UIKit.TextRole.Label, t);
            barText.fontSize = t.sizeCaption;

            var reward = card.gameObject.AddComponent<NextRewardCard>();
            reward.headingText = heading;
            reward.icon = icon;
            reward.nameText = name;
            reward.detailText = detail;
            reward.bar = bar;
            reward.barText = barText;
            reward.actionButton = action;
            menu.nextReward = reward;
            return card.gameObject;
        }

        // ==================================================================
        // Toasts, quit, and the screens under the bar.
        // ==================================================================

        static void BuildToasts(RectTransform root, MainMenuController menu, UITheme t)
        {
            var area = UIKit.Rect(root, "ToastArea");
            area.anchorMin = area.anchorMax = area.pivot = new Vector2(1f, 1f);
            area.sizeDelta = new Vector2(420f, 400f);
            area.anchoredPosition = new Vector2(-24f, -(TopBarHeight + 16f));
            var stack = UIKit.ToastStack(area, "Toasts", t);
            var rt = (RectTransform)stack.transform;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(420f, 0f);
            menu.toasts = stack;
        }

        static void BuildQuitConfirm(RectTransform root, MainMenuController menu, UITheme t)
        {
            var shade = UIKit.Panel(root, "QuitConfirm", UIKit.PanelTone.Background, t);
            shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.88f);
            shade.raycastTarget = true;
            UIKit.Fill(shade.rectTransform);

            var card = UIKit.Panel(shade.transform, "Dialog", UIKit.PanelTone.Panel, t);
            var rt = card.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(560f, 260f);
            card.stripeSide = FlatRect.Side.Top;
            card.stripeColor = t.danger;
            card.stripePixels = t.stripePixels;
            UIKit.Column(card, 14f, new RectOffset(32, 32, 28, 28)).childForceExpandHeight = false;
            UIKit.Text(card.transform, "Title", "Quit Mini FPS?", UIKit.TextRole.Title, t);
            var body = UIKit.Text(card.transform, "Body", "Exiting closes the game. Your progress is saved.", UIKit.TextRole.Body, t);
            body.color = t.textSecondary;
            var flex = UIKit.Rect(card.transform, "Flex");
            UIKit.Size(flex, flexHeight: 1f);
            var buttons = UIKit.Rect(card.transform, "Buttons");
            UIKit.Row(buttons, 12f, null, TextAnchor.MiddleRight);
            UIKit.Size(buttons, height: UIKit.ControlHeight);
            var quit = UIKit.Button(buttons, "Quit", "Quit", FlatButton.Variant.Secondary, "power", t);
            var stay = UIKit.Button(buttons, "Stay", "Stay", FlatButton.Variant.Primary, null, t);
            stay.gameObject.AddComponent<UIDefaultSelection>().priority = 200;

            menu.exitConfirmPanel = shade.gameObject;
            menu.confirmExitButton = quit;
            menu.cancelExitButton = stay;
            shade.gameObject.SetActive(false);
        }

        /// <summary>
        /// The screens a tab opens start below the top bar. Their layouts are fractions of the
        /// box they are given, so each simply lays itself out in a slightly shorter one.
        /// </summary>
        static void InsetBelowTopBar(MainMenuController menu)
        {
            var panels = new List<GameObject>();
            if (menu.store != null) panels.Add(menu.store.panel);
            if (menu.achievements != null) panels.Add(menu.achievements.panel);
            if (menu.instructions != null) panels.Add(menu.instructions.panel);
            if (menu.dossier != null) panels.Add(menu.dossier.panel);
            if (menu.levelSelect != null) panels.Add(menu.levelSelect.panel);
            foreach (var p in panels)
            {
                if (p == null || !(p.transform is RectTransform rt)) continue;
                rt.offsetMax = new Vector2(rt.offsetMax.x, -TopBarHeight);
            }
        }
    }
}
#endif
