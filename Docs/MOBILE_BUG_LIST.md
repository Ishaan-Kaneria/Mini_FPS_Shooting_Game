# Mobile bug list (web build on an Android phone, landscape)

Found from phone screenshots of minifpsshootinggame.netlify.app plus a read of the code.
Items marked **FIXED** were fixed on branch `claude/clever-noether-zhthto` (cloud, no Unity),
then merged to `main` and finished locally on 2026-09-29: compiled (one CS0136 in
`DossierPanel` fixed), `ResetCampaign`, `ResetLevelSets`, `ImportUiKit` and Build Dashboard run,
and the remaining OPEN items fixed. See "Second pass" at the bottom.

## A. Reported by Ishaan

1. **Can walk/run or fire, never both.** **FIXED** (`GameInput.cs`, UI map).
   The UI `Point`/`Click` actions were bound to `<Pointer>/position` and `<Pointer>/press`.
   On a touchscreen that is the *primary touch only*, so the EventSystem saw one finger at a
   time: a thumb on the stick locked out FIRE, and a thumb on FIRE locked out the stick and the
   look area. They are now bound per finger (`<Touchscreen>/touch*/position|press`, plus Mouse
   and Pen), the same bindings as Unity's default UI actions.
   Verify: `FPSKitBatch.VerifyTouch` + `VerifyControls`, then on a phone hold the stick and FIRE together.

2. **Can't throw the bomb in any direction.** **FIXED** (both parts).
   - The bomb aims along the camera and throws on release. Turning while holding BOMB needed
     a second finger on the look area, which bug 1 blocked.
   - BOMB now drag-steers like FIRE (`dragFire` set in `FPSKitMobileControls`). Press BOMB,
     slide the same thumb to swing the ring, release to throw. This only applies after the
     touch scenes are re-staged (next WebGL/Android build). Check `VerifyBomb` and `VerifyTouch`.
   - Note: the BOMB button is `situational`, so it is hidden when the belt has no bombs. With
     0 bombs there is no button at all. Consider showing it greyed out so players know it exists.

3. **Touch button placement makes it hard to play.** OPEN, needs a design pass on a device.
   From the screenshots: the cluster covers the centre-right of the view, PUNCH sits beside the
   weapon model near screen centre, and CROUCH/RELOAD/JUMP are stacked away from the thumb's arc.
   Suggested: put the secondary buttons in an arc around FIRE, within reach of the right thumb.
   Keep the upper-middle of the screen clear, move PUNCH below/right of RELOAD, and check that
   no button overlaps the weapon card or the belt. Use `TouchCluster` slots/mm sizes in
   `TouchProfile.asset`, not pixel offsets.
   Verify with `VerifyHudLayout`, `VerifyDevices` and `CaptureViews` on a handset profile.

4. **Text and content too small on a phone.** OPEN.
   - `UITextFloor` floors text at 12dp (~1.9mm). That is below the usual 14–16sp minimum for
     body text on Android. Raise the floor to ~14dp for body and ~12dp for captions.
   - `PhoneUI.ReferenceScaleFor`: Handset Menu is 0.56 and Hud is 0.80. HUD labels in the
     screenshots (SCORE/RANK/KILLS, objective strip) are about 1.5mm tall. Try Hud 0.70.
   - Store card descriptions truncate on a phone ("useless pa..."). Let them wrap to two lines
     on `DeviceProfile.Form.Handset`.
   - Verify with `VerifyHudLayout`, `VerifyDevices`, and the UI gallery.

5. **No "enter name". The dashboard says "WELCOME BACK, OPERATIVE".** **FIXED**.
   `PlayerProfile.Name` was stored but never asked for. New `NameDialog.cs` is runtime-built,
   like `ConfirmDialog`, so it needs no dashboard rebuild. It opens once, after the opening story
   closes, on a profile with no name. SKIP/Escape stores "Operative" so it isn't asked again.
   `PlayerProfile.HasName` was added. Check the on-screen keyboard opens on Android Chrome.
   Consider also a "Name" row in Settings so the name can be changed later.

## B. Found in this pass

6. **Raw `<SPRITE="GLYPHS" NAME="FIST" TINT=1> PUNCH` under the crosshair** (screenshot).
   `fist.png` was added to `Assets/UI/Icons` after the Glyphs TMP sprite asset was last built,
   so TMP printed the unresolved tag. It is the only icon missing from the atlas.
   - **FIXED (guard):** `InputPrompts.Glyph` now returns "" for an id the sprite asset
     doesn't hold, so a stale atlas can never show raw markup again.
   - **TODO:** run `FPSKitBatch.ImportUiKit` so the fist glyph really exists. Then re-check the
     punch prompt with `VerifyCombat` / `CaptureEnemies`.

7. **Typing "q" in any text field closed the screen.** **FIXED** (`GameInput.BackPressed`).
   Q is a Back key. It closed the HUD editor while naming a layout, and would have closed the
   new name dialog. Q now counts as Back only while no `TMP_InputField` has focus
   (`GameInput.Typing`). Escape/B are unchanged.

8. **Settings on a phone showed "Mouse sensitivity" and the keyboard key list** (screenshot).
   **FIXED** (`SettingsPanel.BuildControls`). The key list was hidden only when
   `MobileInput.Active`, which only an arena's touch layer sets, so the dashboard's settings
   always showed them. It now hides the mouse slider and keys on any touch-only device.
   Follow-up: open the TOUCH tab first on a touch device.

9. **Opening story card runs off the bottom of a phone** (screenshot: answer buttons clipped).
   **FIXED** (`StoryPanel.OnOpened`). The card is a fixed 1180×620, but a handset menu canvas is
   about 540 units tall. It now gets `FitInsideParent` (as `ConfirmDialog` has), added at
   runtime so no rebuild is needed.

10. **Story card, dossier rows and instruction rows use TMP's default font (LiberationSans)**
    instead of the theme's Barlow. This is the Arial look in the story screenshot.
    **FIXED in the generator** (`FPSKitMenuBuilder.Label` sets `UITheme.Active.bodyFont`).
    **TODO:** Build Dashboard to regenerate `Menu.unity`.

11. **Starter gun shows as "TEST RIFLE" on the HUD** (screenshots). The store calls it
    "Service Rifle", but `FPSKitSceneBuilder.CreateWeaponData` named the asset "Test Rifle".
    That builder ran first and never re-stamps the name. **FIXED** in the builder and in
    `TestRifle.asset`'s `weaponName`. The file name `TestRifle.asset` is kept on purpose,
    because `FPSKitStore.StarterRiflePath` points at it.

12. **Damage-direction marker draws over the top objective strip** (screenshot 3: red diamond
    over "11/24"). OPEN. Keep the indicator ring inside a radius that clears the top HUD
    strip on a handset, or draw it under the HUD panels.

13. **"--" in player-facing story text** ("Before we start -- were you a boy..."). OPEN, cosmetic.
    `FPSKitCampaign.Configure` (+ `ResetCampaign`). Use an en/em dash if the Barlow TMP font
    asset includes the glyph; check the font's character table first.

14. **Store: price turns red and BUY greys out with no reason given.** OPEN, minor. Say
    "Need N more coins" on the card or as a tooltip/toast on tap, since touch has no hover.

15. **Coins shown twice on the store screen** (top bar "118" and header "118 COINS"). OPEN,
    minor. On a handset, drop the header copy to free a row.

## C. Level start and dashboard (second batch of screenshots)

16. **Level-start briefing was a pile of text over the fight.** **FIXED**.
    The builder's `briefingText` was a bare 1180-wide, 34pt label in TMP's default font.
    On a phone it ran under the run panel and the touch buttons, with the countdown and the
    stakes in faint type over the scenery.
    - `HudView.BuildBriefingCard` re-parents the same label onto one centred HUD card with
      an accent stripe and the theme font. The card is 560 wide on touch (760 on desktop),
      which clears the left panels and the right button cluster. It shows only while there is
      a briefing (`UpdateBriefingCard`). The middle of the screen is free then, because
      nothing has spawned.
    - `HUDController.UpdateBriefingText`: "GET READY · 3" now leads as an accent header. The
      stakes line goes up from 62% to 78% and the equipment hint from 58% to 72%. On touch the
      bomb tip is one line: "HOLD [bomb] AND SLIDE TO PLACE A BOMB, RELEASE TO THROW".
    - The same `briefingText` label is kept, so `VerifyFlow`'s stakes check still reads it.
      Run `VerifyFlow` and `VerifyHudLayout`, and check a handset capture.
    - Still worth considering: the brief and the stakes are about 40 words to read in a
      3-second countdown. Either shorten them in `FPSKitLevels`/`FPSKitThemes` or lengthen
      `briefingTime` on the first levels. Both need a reset.

17. **"OUT OF TIME" last-run toast covered the mission card's heading on the dashboard.**
    **FIXED** (`MainMenuController.ShowLastRun`): skipped on touch devices. The results
    card has just shown the same information.

18. **Arena row on a phone: the second card slides under the mission panel.** OPEN.
    Its lock reason, title and description are all cut at the panel edge ("FINISH
    INDUST..."). Either fit whole cards (one full card plus a visible scroll hint) or give
    the row the full width and put the mission panel below it on `Form.Handset`.

19. **HUD shows the kill count twice** (mission strip "0 / 9" and the run panel's KILLS).
    OPEN, minor. On a handset, drop KILLS from the run panel to shrink it.

20. **Results card: "TIME LIMIT" sits alone under "43s / 43s"** as if it were a fourth
    stat row. OPEN, cosmetic (`ResultsView`). Put it inline, e.g. "43s / 43s LIMIT".

## D. Requested change: pinch to aim instead of an ADS button

21. **Touch ADS button removed; spread two fingers to aim, pinch to stop.** **DONE**.
    - `TouchLookArea` tracks every finger on the look surface (the right 62% of the screen,
      mirrored for left-handed players). With two fingers down it is a pinch, which never
      turns the view. Spreading past `TouchProfile.pinchAimMm` (new knob, 10 mm) sets
      `MobileInput.Aim`; pinching in by the same amount clears it. It stays on after the
      fingers lift, like the old tap mode, so the right thumb is free for FIRE.
    - `FPSKitMobileControls` no longer builds the ADS button. PUNCH moves into its cell
      beside FIRE, so it's no longer by the gun near screen centre. This only applies after
      the touch scenes are re-staged (next WebGL/Android build).
    - Settings: the touch "Aim button: Hold/Tap" choice is removed. `GameSettings.TouchAimMode`
      is left in place, unused on touch.
    - How to Play (touch): the aim line now says to spread two fingers.
    - Bomb HUD on touch: shows just the range. "[aim] TO LOCK" named a button that no longer
      exists. A pinch while aiming a bomb still toggles the range lock through
      `MobileInput.Aim`, which is harmless.
    - Depends on fix 1 (multi-touch UI bindings). Without it the second finger never reaches
      the look area. Check `VerifyTouch`, then try it on a phone.
    - Not done: zooming toward the point between the fingers. The sights zoom on the
      crosshair, and aim assist pulls onto a nearby enemy.

## E. Requested feature: view and rename the player

22. **Settings > Profile tab.** **DONE** (`SettingsPanel.BuildProfile`). It is the last tab,
    so Settings still opens on Controls, including from the pause menu.
    - **Name:** an editable field (the same one `NameDialog` uses). It saves on end-edit,
      and an empty entry puts the old name back. `shouldActivateOnSelect` is off, so a
      gamepad landing on the page doesn't start typing.
    - **In the story:** A boy / A girl / Rather not say, over `Campaign.Who`, so the
      opening's answer can be changed later.
    - **Career, read-only:** rank title, LV and XP; coins; coins earned; best score; levels
      played; enemies down. All are read from the PlayerPrefs accessors each time the panel
      opens.
    - The dashboard re-reads itself when Settings closes (`MainMenuController.OpenSettings`),
      so "WELCOME BACK, <name>" updates straight away.
    - "Reset to defaults" doesn't touch any of this (`GameSettings.ResetAll` lists its keys).
    - Not done: tapping the name/rank on the dashboard to open this tab. A small
      `UIKit.IconButton` ("user" icon) beside the welcome line in `FPSKitMenuBuilder`
      calling `SettingsPanel.Show(canvas).ShowTab("Profile")` would do it. Needs Build Dashboard.
    - Verify: `VerifyHudLayout` (clicks every Settings tab) and `VerifyControls`.

## F. Enemies too small at range; dashboard space and text size

23. **Everything was drawn about a fifth smaller on a phone.** **FIXED**
    (`GameSettings.VerticalFovFor`, used in `Weapon`). The FOV setting (default 75) went
    straight into `Camera.fieldOfView`, which is *vertical*. So a 20:9 phone saw about 120°
    side to side where a 16:9 monitor saw about 107°. The setting now means "as on 16:9",
    and anything wider keeps the same horizontal view. At 20:9, 75 becomes a vertical of
    about 63°, so enemies are about 25% larger. The ADS angle gets the same correction.
    Screens 16:9 or narrower are unchanged.

24. **Distant enemies hard to find on touch.** **FIXED** (`EnemyHealthBar`). On touch, every
    living enemy within `touchMarkerRange` (70 m) carries the red diamond, not only ones that
    have spotted the player. The diamond grows with distance so it's never under
    `touchMarkerMm` (2.5 mm) on the glass; up close it's the old 0.2 m. It's still the opaque
    HealthBar material, so walls hide it and it doesn't show anyone through cover. Desktop is
    unchanged. Check with `CaptureViews` on a handset profile.
    - Not done: spawning nearer on phones (`LevelManager.min/maxSpawnDistanceFromPlayer`,
      14–34 m). That changes balance and the leash check, so it's left for a design call.

25. **The Dossier ("The list") had no title, and its rows used half the width** (screenshot).
    **FIXED**.
    - The overlay shell puts its title at 88–97% of the screen, under the top bar, which stays
      up. `MainMenuController.InsetForTopBar` moves the Instructions and Dossier screens'
      top edge below the bar.
    - On a phone the Dossier switches its second column off, but the row's layout never
      recalculated, so the remaining column kept half the width. It now rebuilds after the
      toggle (`DossierPanel.OnOpened`).
    - The font is fixed by item 10 (Build Dashboard).

26. **Arena row cut the second card through its text.** **FIXED** (`MainMenuController.FitGrid`,
    handset branch). Cards were sized to the row's height, so one card plus about 70% of the
    next showed. When more than a third of a card would be left over, the cards shrink so one
    more fits whole. The row still scrolls. This supersedes item 18.

27. **Text floor raised from 12dp to 14dp** (`UITextFloor.GlobalMinimumDp`), applied on top of
    whatever each text serialized. This touches every menu and the HUD, so check for
    overflow/ellipsis with `VerifyHudLayout`, `VerifyDevices` and the UI gallery.

28. **Still open on the dashboard:** the mission card has empty space under ALL LEVELS on a
    phone. On `Form.Handset`, either centre its contents vertically or move the arena
    description/rank line into it. Arena card descriptions are also one line with an
    ellipsis; allow two lines on a handset.

## How the fixes were checked

No Unity in the session, so nothing was compiled. Each change was read against the APIs it
uses (UIKit, OverlayPanel, TMP_SpriteAsset, InputSystem bindings) and follows existing
patterns (`ConfirmDialog`, `HudEditor.NameField`). Run the compile check and the verifies
above before building for WebGL.

## Second pass (local, 2026-09-29)

- **3. Button placement: already addressed by item 21; arc tried and rejected.** The
  screenshots predate the cloud pass, which moved PUNCH from beside the gun into ADS's cell
  next to FIRE. An arc round FIRE was built and measured: touch hit areas are squares, and
  keeping 5mm between squares on diagonals pushed the cluster to 62x56mm, against 54x54mm
  for the current grid. Its inner ring landed on the grid's own cells (RELOAD at 31.9/31.9mm
  vs 31.75/31.75mm). The grid is the tighter thumb-safe packing, so it stays. If the phone
  still feels wrong, the next lever is `TouchCluster.secondaryMm`/`gapMm`, tried on the device.
- **VerifyTouch** required an Aim button the cloud pass had removed; it now accepts pinch-to-aim
  instead and fails if the pinch threshold is zero.
- **4. Text size: FIXED.** HUD reference scale on a handset 0.80 → 0.70. Store and arena card
  descriptions wrap to two lines on `Form.Handset` (runtime, in `StoreItemCard`/`ArenaCard`).
- **6. Fist glyph: FIXED.** `ImportUiKit` run; `fist` is in the Glyphs sprite asset.
- **10. Theme font on story/dossier/instructions: FIXED.** Build Dashboard run.
- **12. Red diamond over the objective strip: FIXED.** It was the enemy's world-space touch
  marker showing through the translucent strip, not the damage arc. On touch the marker is
  dropped while it sits under a HUD panel (`HudView.Covers`).
- **13. "--" in story text: FIXED.** Em dashes in `FPSKitCampaign` (identity question, the
  opening, two stakes lines); Barlow is a dynamic TMP font, so the glyph renders.
- **14. Store shortfall: FIXED.** A red price now reads "250 NEED 132".
- **15. Coins twice: FIXED.** The store header drops its balance on a handset.
- **19. KILLS twice: FIXED.** KILLS row and bar hidden on a handset.
- **20. "TIME LIMIT" as a fourth row: FIXED.** Now inline: "43s / 43s LIMIT".
- **22. Profile from the dashboard: FIXED.** A user icon button beside WELCOME BACK opens
  Settings on the Profile tab.
- **28. Mission card empty space: FIXED.** Compact card keeps the objective line and centres
  its contents vertically.

Left on purpose:
- **2 (note).** BOMB stays hidden with no bombs: CLAUDE.md says to show controls only for
  equipment the player has.
- **16 (note).** Briefing length vs the 3 s countdown: a design call (shorter text or a longer
  `briefingTime`), not changed.
- **24 (note).** Spawning nearer on phones: changes balance and the leash; not changed.
- Everything here still needs a play on the phone after the next deploy.

## Third pass (local, 2026-09-29): briefing, How to Play, pause

- **Level-start card trimmed.** The bomb and drink instructions are gone from it
  (`HUDController.UpdateBriefingText`); it is the countdown, the mission line and the arena's
  one-line stakes. The controls live on How to Play.
- **Level-start card lasts 7.5 s (8 s on each arena's first level)** (`FPSKitLevels`, applied
  with `ResetLevelSets`). The level clock does not run during it.
- **Level-start card is animated** (`BriefingReveal`): pops in, the words rise into place
  letter by letter, it pulses on each countdown tick, a bar drains with the countdown, and
  it lifts away when the fight starts.
- **How to Play rebuilt** (`InstructionsPanel`, now runtime-built): a "beat the clock" goal
  banner, one card per section with its icon, keys drawn as keycaps (mouse glyphs no longer
  vanish), an icon per action, cards pop in one after another. Same screen on the dashboard
  and in the pause menu. `InstructionRow` and the builder's page are gone.
- **Pause menu:** ACHIEVEMENTS replaced by HOW TO PLAY.
- **HUD panels overlapping on phones** (run panel over the mission strip, weapon card over the
  health bars): caused by the 14dp text floor (#27). The in-level HUD keeps a 12dp floor;
  menus keep 14dp. Handset HUD scale reverted to 0.80.
- **Still open:** on an iPhone-15-sized screen the DRINK button (third column, middle row) sits
  near the crosshair (`FPSKitDeviceShots` flags it). Needs a layout call for the cluster.

## Fourth pass (local, 2026-09-29): long levels, big arenas

- **32 levels in every arena, the finale included** (`FPSKitLevels.LevelsPerArena`).
- **Level length:** every arena opens at 2:30 and climbs to its longest level: 6:00 in the
  Warehouse, rising along the campaign to 15:00 in Mars Colony and The Auger House.
  About one enemy per 5 s of clock (25 at 2:30, ~200 at 15:00).
- **Waves** (`LevelSet.Level.waveSize/waveRest`): past ~3:20 enemies come in waves from one
  side of the map at a time, the next wave from elsewhere; the rest ends early once the arena
  is empty. **Spawn reach** (`spawnReach`, 1 → 2.2 up the ladder) moves the spawn ring out so
  late levels are fought across the arena; the leash grows with it.
- **Map objectives scale with length** (`objectiveStages`): several runners in turn, several
  hold sites across the map, a charge every ~40 s, and the new **RECON** objective (a chain of
  points across the arena, one after another).
- Toughness, aggression and roster mix follow each level's place on its ladder, so the curve
  has the same shape at 32 levels as at 8. Spare magazines grow with the clock.
- Level select scrolls six across on a desktop when a ladder is long.
- Level-start card: 9 s, no per-second pulse, FIRE skips it after 1.2 s ("[fire] TO START").
- DRINK moved to the top row beside CROUCH; the cluster also tightens itself on any phone
  where a button would enter an 8 mm square round the crosshair.
- About and How to Play are larger on laptops (How to Play is two columns of bigger cards).
- Profile button beside WELCOME BACK removed.
- Note: the next arena still unlocks when the previous arena's holder (now level 32) is down.
