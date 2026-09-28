# Mobile bug list (web build on an Android phone, landscape)

Found from phone screenshots of minifpsshootinggame.netlify.app plus a read of the code.
Items marked **FIXED** are fixed in code on branch `claude/clever-noether-zhthto`.
That session had no Unity, so nothing below is compiled or run yet.
**First step:** `Tools/unity-batch.sh` (compile check). Then `FPSKitBatch.ImportUiKit`,
Build Dashboard, and the rebuilds/verifies named per item.

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

## How the fixes were checked

No Unity in the session, so nothing was compiled. Each change was read against the APIs it
uses (UIKit, OverlayPanel, TMP_SpriteAsset, InputSystem bindings) and follows existing
patterns (`ConfirmDialog`, `HudEditor.NameField`). Run the compile check and the verifies
above before building for WebGL.
