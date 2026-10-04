# CLAUDE.md

Level-based FPS built on **Unity 6000.6.0f1** with the **Universal Render Pipeline** (com.unity.render-pipelines.universal 17.6.0). **Seven arenas played as a campaign**, each with a ladder of 32 levels. A level is a fixed crowd of NavMesh-driven enemies, a strict clock, an objective, and one to three stars. Clearing a level unlocks the next; clearing a zone's last level puts down the child who holds it and opens the next zone.

## Layout

Scripts live in `Assets/Scripts`, split into exactly two folders:

- `Assets/Scripts/Runtime/` — all gameplay MonoBehaviours and ScriptableObjects, flat (no subfolders), **global namespace**.
- `Assets/Scripts/Editor/` — editor-only tooling under `namespace FPSKit.EditorTools`, every file wrapped in `#if UNITY_EDITOR`. The folder name `Editor` is what keeps this code out of player builds; there are no `.asmdef` files anywhere, so moving these files breaks the build.

There is no `Core/`, `Player/`, `Weapons/`, `Enemies/` or `UI/` folder — those concerns are separated by file, not by directory:

| Concern  | Files |
|---|---|
| Player   | `PlayerMotor.cs` (CharacterController move/look/recoil/shake), `WeaponSway.cs`, `PlayerProgression.cs` (per-level rifle upgrades), `PlayerLoadout.cs` (turns what was bought into what is carried) |
| Weapons  | `Weapon.cs`, `WeaponData.cs` (ScriptableObject, `FireMode` Single/Burst/Auto), `ImpactLibrary.cs`, `TracerProjectile.cs` (flies its own tracer, so the shot outlives whoever fired it) |
| Explosives | `BombThrower.cs` (the aiming ring and the throw), `BombProjectile.cs` (flies its own solved arc), `BombAimIndicator.cs`, `Explosion.cs` (the blast, and the static that works out who it hurt), `BombData.cs` (ScriptableObject, carries `BlastSpec`) |
| Economy  | `Wallet.cs` (coins in PlayerPrefs), `Loadout.cs` (what is owned, upgraded and equipped), `StoreCatalog.cs` (ScriptableObject: stock and prices), `StorePanel.cs`, `StoreItemCard.cs`, `ConsumableData.cs`, `ConsumableBelt.cs` |
| Enemies  | `EnemyAI.cs` (NavMeshAgent, `State` Idle/Chase/Attack/Stagger/Retreat/Dead), `EnemyArchetype.cs`, `Health.cs`, `Hitbox.cs`, `RagdollController.cs`, `EnemyLimbAnimator.cs` (swings the limbs off agent velocity -- there is no AnimatorController anywhere in the project) |
| Levels   | `LevelManager.cs` — runs one level: a fixed roster, a strict clock, golden-angle ring spawns that avoid the player's view, an enemy leash that replaces what it discards, and a weighted score cut into stars. `LevelSet.cs` (ScriptableObject, one ladder per arena), `LevelResult.cs` (how a level ended), `LevelProgress.cs` (stars and unlocks in PlayerPrefs) |
| Run state | `GameDirector.cs` — score, combo, pause, end of level, return-to-dashboard, PlayerPrefs records; `GameSession.cs` (what survives a scene change), `PlayerProfile.cs` (PlayerPrefs stats) |
| Dashboard | `ArenaCard.cs`, `ArenaCatalog.cs` (ScriptableObject), `LevelSelectPanel.cs`, `LevelButton.cs` -- the rest is under Menus |
| Machinery | `MachineTravel.cs` (crane trolleys, dust devils), `MachineSpin.cs` (roof fans, pumpjack cranks), `MachineRock.cs` (pumpjack beams) -- position from the clock, no state, no collider |
| Feedback | `HUDController.cs`, `LevelResultsUI.cs` (the stars screen), `Minimap.cs` (the map in the corner), `MinimapMarker.cs`, `EnemyHealthBar.cs`, `DamageNumber.cs`, `Pickup.cs`, `TransientFlash.cs` (shrinks a spawned flash out of sight), `OneShotAudio.cs` (pooled positional one-shots), `ScrollingWater.cs` (drags the river's texture along it) |
| UI       | the touch stack: `TouchControls.cs`, `TouchButton.cs`, `TouchLookArea.cs`, `VirtualJoystick.cs`, `MobileInput.cs`, `TouchLayout.cs` (mirror, tap/hold aim, auto-fire) |
| Input    | `GameInput.cs` (Input System maps, schemes, last-used device), `InputPrompts.cs` (glyphs for keys, pads, touch), `GameSettings.cs` (the player's settings, over PlayerPrefs) |
| Menus    | `MainMenuController.cs`, `MenuTopBar.cs`, `MissionCard.cs`, `NextRewardCard.cs`, `LoadoutStrip.cs`, `LoadoutPanel.cs`, `InfoPanel.cs`, `SettingsPanel.cs`, `PlayerRank.cs` (derived rank/XP), `Missions.cs` (next level, difficulty, rewards) |
| Devices  | `ScreenInfo.cs` (the screen, simulable), `SafeAreaCanvas.cs`, `UIScaleBinder.cs`, `UINavigator.cs` / `UIFocusRing.cs` / `UIDefaultSelection.cs` (pad navigation), `UITextFloor.cs`, `UIBootstrap.cs` (applies all of it per scene), `QualityTiers.cs`, `DynamicResolution.cs` |
| Achievements | `Achievements.cs` (twenty thresholds, all derived), `PlayerStats.cs` (the lifetime counters they read), `AchievementsPanel.cs`, `AchievementRow.cs` |
| Instructions | `InstructionsPanel.cs` (written from the live bindings), `InstructionRow.cs` |
| Screens  | `OverlayPanel.cs` (the base every overlay shares), `UITheme.cs` (the theme ScriptableObject: colour, type, icon and motion tokens), `UIKit.cs` (builds the components below), `FlatRect.cs`, `FlatButton.cs`, `UITooltip.cs`/`UITooltipView.cs`, `UITabBar.cs`, `UIProgressBar.cs`, `UIStatBar.cs`, `UIToast.cs`/`UIToastStack.cs`, `DeviceProfile.cs` (reach, form, orientation), `PhoneUI.cs` (the one-line question, delegating to it) |
| Campaign | `Campaign.cs` (the player's side: identity, powers, beats read, the one gate), `CampaignData.cs` (ScriptableObject: the zones in order, the eight names, the beats), `SaveMigration.cs` (the one-time wipe), `StoryPanel.cs` (the opening and the beat cards), `DossierPanel.cs` / `DossierRow.cs` (THE LIST) |
| Objectives | `LevelObjective.cs` (the six, and the invariant that none of them may end a level) |
| Config   | `ControlSettings.cs`, `LevelTheme.cs` |

`Assets/FPSKit_Generated/` holds tool output: generated scenes, themes, `Levels/` (LevelSet assets), `Enemies/` (EnemyArchetype assets), `Store/` (the catalog plus its WeaponData, BombData and ConsumableData), materials, `Controls.asset`, `TestRifle.asset`, `ImpactLibrary.asset`, `MinimapBlip.png`, `Enemy.prefab`, `Bomb.prefab`, `Explosion.prefab`, `Pickup_*.prefab`, post-FX volume profiles. Treat everything in it as regenerable. Art comes from `Assets/RPG_FPS_game_assets_industrial/`.

## Input: the Input System, with one legacy read left

All input goes through **`GameInput`** (the Input System): a `Gameplay` action map and a `UI`
map, control schemes `Keyboard&Mouse`, `Gamepad` and `Touch`, and `GameInput.Scheme` -- the
device the player actuated last, past a threshold so a resting pad cannot flip it. Prompts
(`InputPrompts`), the focus ring, the key strip, HOW TO PLAY and the touch layer all follow
`GameInput.SchemeChanged`. This replaced the legacy `UnityEngine.Input` API in September 2026,
at Ishaan's request; the earlier "legacy only" rule is gone.

Five things about it are worth not re-deriving:

- **The keyboard bindings are written from `ControlSettings`**, not typed into an .inputactions
  file. The asset's three presets are what the instruction strip and HOW TO PLAY describe, so
  a second list of keys would be a second answer. `GameInput.Bind(controls)` rewrites them;
  `ControlSettings.Revision` says when. `ControlSettings.Held/Pressed(KeyCode)` still exist
  and read the Input System. The pad layout is fixed in `AddPadBindings` and must match
  `InputPrompts.PadGlyph`.
- **The one legacy read is `PlayerMotor.ReadMouseCounts()`'s fallback**, and it must stay.
  It prefers `Mouse.current.delta` and falls back to `Input.GetAxisRaw` **on an empty reading,
  not only on a missing package.** On Linux/X11 `Mouse.current.delta` reports zero on every
  frame the cursor is locked while the device is present and every other control on it works.
  Written as an unconditional `return`, that is a game with no mouse look at all, with nothing
  logged. It took 296 consecutive frames of `LookDeltaDegrees` at exactly zero to see. It must
  stay `GetAxisRaw` -- the smoothed axis keeps reporting after the mouse stops. That read is
  why `activeInputHandler` stays `2` (**Both**).
- **The mouse rule moved with it.** Unity maps touch 0 onto the mouse in a browser, so
  `GameInput.Held/Pressed` ignore every mouse control while `MobileInput.Active`, and a mouse
  event never switches the scheme away from touch. `VerifyInput` presses a virtual mouse to
  prove it.
- **Aim assist is for thumbs and sticks.** `TouchAimAssist.Adjust` takes a strength (the
  player's setting) and the caller decides who gets it: the touch look and the right stick,
  never the mouse. The stick look has its own deadzone and response curve (`GameInput.Shape`,
  radial, curve on magnitude only). L3 latches sprint until the stick centres; B toggles crouch.
- **Escape and B mean "back", and one press closes one thing.** `GameInput.BackPressed` is
  what every overlay closes on; `OverlayPanel.AnyOpenThisFrame` stops the pause menu under the
  settings from also resuming on the same press.

`Assets/InputSystem_Actions.inputactions` is leftover from the Unity template and is not
referenced by anything. Tests can now press real buttons: `InputSystem.AddDevice` plus
`QueueStateEvent` is how `VerifyInput` drives a pad, a DualShock, a keyboard and a mouse.

**Mouse look is gated on the pointer lock, and losing it is silent.** `PlayerMotor.HandleLook`
reads no mouse at all unless `Cursor.lockState == Locked`, and the lock is dropped
constantly by things the game does not control: Escape releases it in every browser, the
editor drops it the moment the Game view loses focus, and alt-tab drops it anywhere. What
a player sees is the keyboard still working and the mouse dead -- they can walk, they can
hold the bomb key and watch the ring sit there, and turning does nothing, with nothing
logged because nothing went wrong. Recovery used to be a left click and nothing else,
which is undiscoverable, and worse while a bomb is up because that is the one button
`Weapon` suppresses -- so the click that would have fixed it also produced no shot and no
sign it had done anything. `HandleCursor` now re-locks on any key that means "I am
playing", held rather than pressed, rate limited because WebGL only grants a lock off a
real gesture. Escape is deliberately not on that list: it is the pause key, and re-locking
the pointer on the frame the pause menu opens takes the cursor away from the menu it just
opened.

Bindings are not hard-coded: they live in the `ControlSettings` ScriptableObject (`FPSKit_Generated/Controls.asset`). What ships there is the `StandardFPS` preset — **arrows and W/A/S/D both move, left click fires, right click aims, Space jumps, Shift sprints, Ctrl/C crouch, R reloads** — and `ControlSettings.Preset` also carries `ArrowsAndMouse` and `ArrowsAndSpace` (arrows move, Space fires, double-tap Space sprints, click jumps), applied from the custom inspector. Read new keys through `ControlSettings` helpers rather than calling `Input.GetKey` with a literal `KeyCode`.

## The scene builder regenerates everything

`FPSKitSceneBuilder.cs` (~3200 lines) drives the **FPSKit** menu:

- **FPSKit > Build Scene > [Industrial Warehouse | Desert Outpost | Snowbound Station | Night Rooftop | Abandoned Fairground | Mars Colony | The Auger House]** — each calls `BuildScene(themeName)`.
- **FPSKit > Build Scene > From Selected Theme Asset** — builds from whatever `LevelTheme` is selected in the Project window, built-in or not. This is how a new arena gets made without touching code.
- **FPSKit > Build Scene > Build ALL Themes** — builds and saves one scene per theme, registering them in Build Settings so the in-game restart works.
- **FPSKit > Add Gameplay To Current Scene** — the non-destructive path: injects player, enemies, level manager, HUD, post-FX and a baked NavMesh into an existing level, leaving geometry and baked lighting alone. Refuses to run twice (bails if a `Player`-tagged object exists).

**`BuildScene` is destructive.** It calls `EditorSceneManager.NewScene(..., NewSceneMode.Single)` and constructs the entire scene from scratch — lighting, skybox, arena geometry, player rig, weapon data, enemy prefab, spawn points, baked NavMesh, level manager, HUD, results screen, post-processing — then saves over `Assets/FPSKit_Generated/Scenes/<Theme>.unity`. Any hand-tuning done in the Unity editor to a generated scene is lost on the next build.

So: **fix scene content by editing the builder, not the `.unity` file.** Hand-editing a generated scene is only appropriate for a throwaway experiment. The same applies to the generated assets it writes (`Controls.asset`, `Enemy.prefab`, themes, materials) — the builder and `FPSKitThemes` recreate or re-dirty them.

**Generated scenes are saved in binary, so you cannot grep them.** A GUID lives in one
as sixteen raw bytes rather than as the hex string a text search looks for, so grepping
the scenes for a material or a prefab finds nothing and reports it as unreferenced —
which is a very convincing way to talk yourself into deleting a whole arena. Ask
`AssetDatabase.GetDependencies` instead; it is the only answer that is true, and it is
the same one the build uses.

**The material folder grows on its own.** `FPSKitSceneBuilder.MakeMaterialAt` names a
material after its theme, its role and its colour (`DesertOutpost_Crate_8A6A44`) and
reuses the asset already at that path, which is what stops a rebuild churning every
material in the project. The cost is that retuning one colour writes a *new* material
and leaves the old one on disk forever, referenced by nothing and named closely enough
to its replacement to look deliberate. `FPSKitPrune.cs` (**FPSKit > Prune Unused
Generated Materials**, or `FPSKitBatch.PruneMaterials`, which reports and only deletes
with `-fpskitApply`) is how that is cleared; anything the builder still wants, it
recreates on the next build.

`FPSKitSceneBuilder` is partial across several files, all of them the same class: `FPSKitOpenZone.cs` (where the open-zone arena's pieces go), `FPSKitDesert.cs` (what they are made of), `FPSKitTerrain.cs` (the heightfield), `FPSKitIndustrial.cs` and `FPSKitIndustrialParts.cs` (the plant and its fittings), `FPSKitFactory.cs` (the big industrial structures -- halls, chimneys, silos, cooling towers, cranes, and what the ground is wearing), `FPSKitIndustrialDense.cs` (narrow roads, walled compounds and gates, street tunnels, and the buildings packed against the walls), `FPSKitIndustrialWorks.cs` (roof routes, walk-in warehouses, the railway siding, silo conveyors, roof fans), `FPSKitMeshKit.cs` (procedural meshes, noise, and the `Hide`/`NoStanding`/`NoEntry`/`SealNavMeshOutside`/`Mark` bookkeeping every generated object needs) and `FPSKitPark.cs` (the abandoned fairground: hollows, shafts, rides and what stands between them), `FPSKitVolcanic.cs` (the Unknown Planet's dressing: sky, cones, vents, lava), `FPSKitDesertLife.cs` (the desert's town, oasis, fields, oil field, wrecks, track and dust -- planned before the ground, built after it), `FPSKitSnowLife.cs` (Snowbound's station, pine forest, ice caves, crevasses cut as real holes in the terrain, the frozen waterfall), `FPSKitLavaField.cs` (its ground: flows, pits, and everything draped on the plain) and `FPSKitTextures.cs` (the detail maps). Splitting is not tidiness -- they share the whole rest of the builder, so a split anywhere else would mean passing half of it around.

Other editor tools: `FPSKitThemes.cs` (creates/resets `LevelTheme` assets), `FPSKitLevels.cs` (creates/resets `LevelSet` assets), `FPSKitEnemyRoster.cs` (creates/resets `EnemyArchetype` assets), `FPSKitStore.cs` (creates/resets the `StoreCatalog` and its stock), `FPSKitArtTools.cs` (**FPSKit > Art Pack Setup**), `FPSKitEnemySetup.cs` (**FPSKit > Enemy Setup**), `FPSKitMobileControls.cs` (**FPSKit > Add Mobile Touch Controls**), `ControlSettingsEditor.cs` (custom inspector with control presets), `FPSKitGraphics.cs` (the render settings that live on the pipeline asset rather than in any scene, applied alongside `EnsureProjectTagsAndLayers`), `FPSKitAudioImportPolicy.cs` (stamps import settings on a clip the moment it lands under `Assets/Audio/`).

The headless side is `FPSKitBatch.cs`, which exposes the builder and the tests as public `-executeMethod` entry points because the menu items are private. It is what `Tools/unity-batch.sh` calls, and the checks behind it are `FPSKitControlsTest.cs` (`VerifyControls`, which audits every binding in every preset for collisions and then plays a level driving each action in turn), `FPSKitPlayTest.cs` (`VerifyReplay`), `FPSKitLevelTest.cs` (`VerifyLevels`, which plays one level to a failure and then to a three-star clear), `FPSKitStoreTest.cs` (`VerifyStore`, which buys a gun and two upgrades and then checks both reached the player), `FPSKitCombatTest.cs` (`VerifyCombat`), `FPSKitBombTest.cs` (`VerifyBomb`, which sweeps the aim a degree at a time, holds the key for real and turns the view under it, throws at both ends of the range and listens), `FPSKitFlowTest.cs` (`VerifyFlow`), `FPSKitTerrainTest.cs` (`VerifyTerrain`, below), `FPSKitReachTest.cs` (`VerifyReach`, below -- the one that asks whether the navmesh an arena bakes is navmesh anything can get to) `FPSKitDeviceTest.cs` (`VerifyDevices`, which asserts every class of screen is laid out for the hands that are on it, and builds nothing) and `FPSKitStaticProbe.cs` (`VerifyStatics`, which finds statics by reflection, so a new class with one is audited without being registered anywhere), `FPSKitObjectiveTest.cs` (`VerifyObjectives`, which plays all six objectives in one session and proves an unsatisfiable one still lets the clock end the level) and `FPSKitRosterTest.cs` (`VerifyRosters`, which opens every built arena and fails if two of them field the same enemies -- no play mode, no graphics).

`FPSKitViews.cs` (`FPSKitBatch.CaptureViews`) is not a test -- it renders a built arena from fixed viewpoints to PNGs, because a generated scene is otherwise write-only from the command line: it is saved in binary so it cannot be read, its geometry is procedural so the code is not a description of the result, and every check here answers whether a level *works* rather than what it looks like. `VerifyZone` would pass just as happily on an arena whose cliffs were inside out. It needs a real graphics device:

```
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViews \
    -fpskitTheme "Desert Outpost" -fpskitOut Build/Views
```

Render through `RenderPipeline.SubmitRenderRequest` and submit twice, for the same two reasons the dashboard's arena previews do.

**A play-mode test that measures speed must set `Time.captureDeltaTime`.** Under
`-batchmode -nographics` the game's delta time is very nearly zero, so anything that
accumulates per frame barely moves: movement ramps at 55 m/s^2, and fifty frames worth
half a millisecond each reached 1.2 m/s against a 5.6 m/s walk. Every speed in
`VerifyControls` was measuring the batch frame rate rather than the game, and it read as
four separate product bugs. `Time.captureDeltaTime = 1f/60f` pins the step; it is a
global, so it goes back to 0 in `Detach` like every other borrowed piece of state. The
same test throws away the first third of each measurement burst, because a burst starts
at whatever speed the previous one left behind -- walking measured straight after
sprinting is a sprint.

**Sprint is not forward-only and does not need movement.** `EvaluateSprint` used to test
`moveInput.y > 0.1f`, so the sprint key did nothing at all while strafing or backing up,
and nothing while standing still. Both are now gone: any direction sprints, and holding
the key while stationary engages it so the first step out of cover is already at full
speed (`ControlSettings.sprintNeedsMovement` restores the old requirement).

**And the sprint key on its own runs forward.** Engaging a sprint with no direction key
down used to cost nothing and do nothing visible: `IsSprinting` went true, there was no
speed to apply, `PlanarSpeed01` stayed at zero, and what the player saw was a key that
works with the movement keys and is dead without them -- which reads as sprint being
broken, because from the outside it is. `PlayerMotor.SprintDrivesForward` now supplies
`(0, 1)` when the key is held and nothing else is, so the key *is* a run
(`ControlSettings.sprintDrivesForward` turns it off). Three details hold it together: a
direction key always wins, since the substitution only ever fills in for no direction at
all; it happens *after* `EvaluateSprint`, so it cannot talk itself into a sprint the
controls did not grant; and it asks for the key to be **held** rather than for
`IsSprinting`, or a double-tap-latched sprint -- the `ArrowsAndSpace` preset, where the
sprint key is also the fire key -- would become a permanent auto-run with no visible way
to stop it. The same reason `EvaluateSprint` counts a held sprint key as "moving": the
key is about to produce the movement itself, and without that the latch drops on the
frame it was taken. `VerifyControls` step 4 is the regression test, and it is the one
burst measured at the *end* of its frames rather than at its peak, because it starts at
whatever the sideways sprint before it left behind and friction sheds that in ten
frames.

`Weapon` keys its sprint FOV widen off actual speed rather than off `IsSprinting`, which
is what kept the view from pulling out while the player had not moved -- still the right
way round now that the key does move them.

**Five generators, five reset entry points, one trap.** `FPSKitBatch.ResetEnemyArchetypes`, `ResetLevelSets`, `ResetStore`, `ResetThemes` and `ResetCampaign` exist because the roster, the level ladders, the store, the themes and the campaign are all generated once and then left alone. Retuning a number in `FPSKitEnemyRoster.Configure`, `FPSKitLevels.Configure`, `FPSKitStore.Configure`, `FPSKitThemes.Configure` or `FPSKitCampaign.Configure` does **not** reach the assets the game reads until the matching reset is run. `ResetCampaign` has the widest blast radius of the five, because the campaign decides what order the arenas are played in and which of them are locked. A price or a level clock changed in code and never reset is a change that compiles, builds and ships without doing anything.

`FPSKitCombatTest` and `FPSKitLevelTest` both retune the live `LevelSet` while they run -- one lengthening the clock so the fight outlives it, the other shortening it so the test does not sit through a full level -- and both put it back in `Detach`. The set is a shared asset: a test that left level one with a four-minute clock would be a test that broke the game to pass.

## The web build has its own page

`Assets/WebGLTemplates/FPSKit/index.html` is the page a browser player actually sees, and `FPSKitBatch.ConfigureWebGL` selects it with `PlayerSettings.WebGL.template = "PROJECT:FPSKit"`. `BuildWebGL` throws if the folder is missing rather than let the build fall back to Unity's stock page, which is a 960×600 canvas in the corner of a white document titled "Unity Web Player", with no statement of the controls and an `alert()` for a loading failure.

`Build/WebGL/` is output. Editing the `index.html` in there fixes nothing past the next build — **change the template**, the same way scene fixes go in the builder rather than the `.unity` file.

The page owns what only the browser can answer: pointer/keyboard focus, suppressing the context menu over the arena, capping `devicePixelRatio` on phones, and the `(any-pointer: coarse)` test behind `Assets/Plugins/WebGL/FPSKitWebDevice.jslib`, which `WebDevice.IsTouchOnly` reads. That test exists because `Application.isMobilePlatform` on WebGL is a user-agent match an iPad fails — it has called itself a Macintosh since iPadOS 13 — so `TouchControls` would hide the on-screen controls on the one device with no other way to play.

## A touch is also a mouse click, and that was the worst bug in the game

**Unity maps touch 0 onto `KeyCode.Mouse0`** on mobile and in a browser. The default
fire binding is `Mouse0`, and `ControlSettings.Held` went straight to
`Input.GetKey` -- so on a phone the gun fired continuously while the player dragged the
move stick, and once for every tap on any button or on empty space. The legacy read
bypasses the EventSystem entirely, so it made no difference that the joystick had
swallowed the touch as far as UI was concerned. The same mapping fires the jump in the
`ArrowsAndSpace` preset, where `Mouse0` is the jump key.

It is invisible on a desktop, invisible in the editor, and it is not in the touch layer,
which is where anybody would look. `ControlSettings.Readable` now refuses mouse bindings
whenever `MobileInput.Active`: on a touch device the on-screen controls are the only
thing that speaks for touch. Keyboard bindings stay readable, because a tablet with a
keyboard should work. Desktop is untouched -- `MobileInput.Active` is false there,
including on a touchscreen laptop, which `TouchControls` deliberately does not count as
a touch device.

`ControlSettings.CanRead` is public **only** so `VerifyTouch` can assert the rule. Batch
mode has no mouse to press, so a check written against `Held` would pass identically with
the rule deleted -- which is a worse test than none.

## The on-screen controls are tuned in millimetres, not pixels

`TouchProfile` (`FPSKit_Generated/TouchProfile.asset`) holds every number the touch stack
is tuned by, for the reason `ControlSettings` is an asset: touch feel is a dozen settings
that only make sense together. `TouchMetrics` converts pixels to physical distance, and
falls back to a typical phone when `Screen.dpi` reports something impossible -- which it
does, including 0, on plenty of Android devices.

Four things in that stack are worth not re-deriving:

- **The fire button is drag-fire.** Press it and keep dragging: the trigger stays down
  while the same gesture turns the view. A phone has two thumbs, the left one moves, so
  the right one must aim *and* shoot -- and if holding fire occupies it, every fight is a
  choice between firing where the enemy was and tracking them without firing. Players
  report that as the game being unresponsive, never as a layout problem.
- **Look is corrected for screen density before it becomes degrees.** Raw pixels mean the
  same physical swipe turns twice as far on a 1440p phone as on a 720p one.
  `TouchLookArea` normalises to a reference density, so `PlayerMotor.touchSensitivity`
  and `VerifyBomb` keep working in one unit and neither has to know what a screen is.
- **Sprint comes from pushing the stick**, not from a button -- the sprint button and the
  look surface want the same thumb, so a sprint costing a button press is a sprint taken
  while unable to aim. It latches, because easing off to steer is not stopping. Button
  and stick are two sources on `MobileInput` for the same reason the two fire sources
  are: sharing one bool means whichever wrote last wins.
- **Everything hangs off `SafeAreaFitter`.** A canvas fills the panel, cutout included,
  and the pause button is anchored top-right -- which on a landscape phone is where the
  front camera is. It is also the only way out of a level.

**Aim assist is touch-only and it is not a concession.** A mouse resolves a hundredth of
a degree; a thumb moving a contact patch the size of its target resolves about one. With
no assist the player is not being tested on aim but on a motor task nobody can perform,
and that reads as the game not registering input rather than as difficulty.
`TouchAimAssist` slows the look inside a 6-degree cone -- the half nobody notices and the
half that does the work -- and adds weak adhesion toward the target, **gated on the
player already turning or firing**. Without that gate it is magnetism: the camera
creeping toward enemies while the player stands still, which feels like the game
wrestling them for control. It aims at centre mass, never the head, or it would hand out
the headshot bonus for pointing vaguely at somebody; it needs line of sight; and it is
skipped while `LookCaptured`, because those degrees move the bomb reticle rather than the
head.

`VerifyTouch` is the regression test for all of it. It builds the layer into a real arena
and fails on a missing or unwired part, on two buttons overlapping (one thumb, two
actions -- invisible in an editor where a mouse presses one pixel), and on the mouse rule
above. Until it existed the touch layer was exercised by nothing until a player had the
finished app, and a control that fails to wire does not throw or log: it produces a game
where the thumb does nothing.

## The Android build exists, and one setting in it is permanent

`FPSKitBatch.BuildAndroid` (`-buildTarget Android`) produces the App Bundle Google Play
wants, or an APK with `-fpskitApk` for a real device. Four things about it are worth not
re-deriving:

- **The touch layer is not optional, and it is not in the scenes.** `StageTouchScenes`
  copies every arena, runs `FPSKitMobileControls.AddMobileControls` into the copy, and
  builds the copies — the same trick `BuildWebGL` uses, now shared rather than written
  twice. Skip it and the game is not merely awkward on a phone: `TouchControls` switches
  `MobileInput` on because `Application.isMobilePlatform` is true, finds no joystick, no
  look area and no buttons in the scene, and the player lands in an arena unable to move,
  look or fire, with nothing logged. The copies are throwaway because a generated scene
  carrying something the builder does not put there is a half-state the next `BuildScene`
  would silently wipe.
- **The application id is refused rather than warned about.** It is the app's identity on
  Play: permanent after the first upload, unchangeable by anyone, and changing your mind
  means a new listing with no installs and no reviews. The project shipped with the URP
  template's `com.UnityTechnologies.com.unity.template.urpblank`, which is a placeholder
  *and* a claim to be Unity Technologies. `ResolveApplicationId` throws on any id
  matching `ForbiddenIdFragments`, because a warning in a build log is read after the
  upload.
- **`companyName` and `productName` must not be touched to fix that.** They are what
  Unity derives the WebGL storage path from, so renaming either orphans every browser
  player's coins, stars and purchases — the data stays in their IndexedDB and the game
  looks somewhere else for it. Only `applicationIdentifier.Android` was changed, which is
  per-platform and touches nothing on the web.
- **No INTERNET permission.** The game has no network code at all, so `ConfigureAndroid`
  sets `forceInternetPermission = false`. Unity adds it by default; a permission in the
  manifest is something a player is shown and something the Data Safety form has to
  answer for.

Signing comes from `FPSKIT_KEYSTORE`, `FPSKIT_KEYSTORE_PASS`, `FPSKIT_KEY_ALIAS` and
`FPSKIT_KEY_PASS` in the environment and never from the repository — this repo is public,
and a committed keystore is a signing key published to the world. `.gitignore` refuses
`*.keystore`, `*.jks`, `*.p12`, `*.pepk` and `keystore.properties` by pattern rather than
by discipline, because `git add -A` does not ask. With no key set the build is
debug-signed and says so: that installs on a device and Play rejects it.

`targetSdkVersion` is set explicitly rather than left on Automatic, which means "the
highest SDK installed" — and a preview SDK is something an editor install quietly
acquires. An app targeting one is not publishable, and the setting that caused it reads
as the sensible choice. Play's floor rises every year, so `TargetSdk` is a number to
check against the Console before a release.

## The game boots into a dashboard

`Menu.unity` is scene 0, built by `FPSKitMenuBuilder.cs` (**FPSKit > Build Dashboard**,
or `FPSKitBatch.BuildDashboard`). It is destructive in the same way the scene builder is
and fixed the same way — edit the builder, not the scene.

The loop is: dashboard → level select → arena → results → back to the dashboard, or
straight into the next level.

**An arena card opens a ladder, it does not start a run.** `MainMenuController.Choose`
opens `LevelSelectPanel` over the dashboard; only `MainMenuController.Launch` loads a
scene, and it refuses a level `LevelProgress` says is locked. The level select and the
results screen both put their choice in `GameSession` and then load, so there is one
way a level ever begins.

- **Esc or P** pause, and the same key resumes. Both, because a browser takes Escape to
  release pointer lock, so a web player pressing it gets their cursor back and no menu.
- **R is RELOAD**, never resume, and **no key leaves a level**: that is the pause menu's
  QUIT TO MENU, behind a `ConfirmDialog`. A single Q that threw a level away was one key
  from reload. `GameDirector.resumeKey`/`quitKey` are kept only so scenes that serialized
  them still load; nothing reads them during play. RESTART is `RestartRun`, which records
  the attempt first, like leaving does.
- On the results screen the same keys mean three different things -- **R** replay,
  **Space** next level, **Q** dashboard -- and `LevelResultsUI` owns them.
  `GameDirector` used to take any of its keys as "I have read this" and go back to the
  menu, which was right when the menu was the only place to go; leaving that in would
  steal two of the three.
- Q is a *scene load*, not `Application.Quit`. That is the whole fix: quitting used to
  call `Application.Quit`, which does nothing in a browser, so the HUD hid the key rather
  than admit there was nowhere to go. The dashboard's **Exit Game** button is the only
  thing left that really means "leave", and it is shown everywhere: on the web
  `GameDirector.ExitApplication` hands over to `WebDevice.Exit` and the `FPSKitExit`
  jslib export, which closes the tab where the browser allows it and otherwise replaces
  the page with a sign-off, because `Application.Quit` there just leaves a dead canvas.
  There is no longer a `CanQuit` gate — it was a leftover that nothing consulted, and
  the tooltip claiming the row was hidden in a browser described a build that never
  shipped.
- `HUDController.instructionText` is the strip across the top of the arena. It is written
  from the live bindings, never hard-coded, and hidden on a touch-only device. It lists
  the bomb and drink keys **only when the player is carrying that equipment** -- naming a
  key somebody has nothing to use it with is worse than saying nothing, because they try
  it, nothing happens, and then they distrust the rest of the strip.
- **The briefing does the teaching.** `HUDController.EquipmentHint` puts one sentence
  under the countdown at the start of a level saying that the bomb is *held* and that
  releasing it is the throw, and another naming the drink key, what it gives back and
  how many are left. Those are the least guessable controls in the game, a key listed in
  a strip is something you notice on your third run, and the briefing is the one moment
  with nothing else happening. Same rule: only for equipment being carried, and the
  drink line is built from the `ConsumableData` so a drink retuned to restore no shield
  stops claiming that it does.

**Play in the editor starts at the dashboard**, whatever scene is open. Build Settings
order only decides what a *player* boots into; the editor plays what is in the hierarchy,
so pressing Play with an arena open dropped you into that arena. `FPSKitPlayMode` sets
`playModeStartScene` from `[InitializeOnLoad]` rather than once at build time, because
Unity keeps that in gitignored per-user settings and a fresh clone would lose it. Toggle
it with **FPSKit > Play Starts At Dashboard**.

Every play-mode test therefore calls `FPSKitPlayMode.SuspendStartScene()` before entering
play mode and restores it in `Detach` — each one opens the scene it means to exercise, and
would otherwise be handed the dashboard and fail on its first assertion.

**The store is `Store.asset`, not code.** The Store button on the dashboard opens
`StorePanel` over the arenas, exactly the way an arena card opens the level select. Both
are on the canvas, both hide `MainMenuController.dashboardOnly` while they are up, and
both put back what was showing rather than switching everything on -- the result strip
only appears after a level, and Exit Game is hidden in a browser.

**The arena list is `ArenaCatalog.asset`, not code.** Adding an arena means a theme, a
built scene, a `LevelSet` and an entry in the catalog — the dashboard clones its card
template per entry at runtime, and the level select clones a tile per level, so the menu
scene never needs rebuilding for new content.

**A panel's component does not live on the panel.** `LevelSelectPanel` and
`LevelResultsUI` are on the canvas and point at a child they switch on and off. Put the
component on the object it hides and it can never be shown: the builder leaves the panel
off, so `Awake` has not run, and the first `SetActive(true)` is what finally runs it --
which switches the object straight back off. The screen then simply never opens, with
nothing logged. `HideAtLoad` says so now instead.

Four things about that screen are worth not re-deriving:

- **Build every button with `FPSKitMenuBuilder.MakeButton`.** Everything else this file
  draws is `raycastTarget = false`, because most of it is decoration and a canvas full of
  click targets is a canvas where the wrong thing gets clicked. Miss the exception on a
  `Button`'s own graphic and it is inert *silently*: it highlights nothing, receives
  nothing, and looks exactly like a button whose handler is broken. That is what Exit Game
  did. One helper owns that detail now.
  `VerifyFlow` fires a real raycast at every button and fails if one is unreachable —
  checking that a listener is attached would not have caught it, because one always was.
  Two things that test gets wrong if you write it casually: raycast at
  `rect.TransformPoint(rect.rect.center)` rather than `rect.position`, which is the pivot
  and sits on the boundary for a corner-anchored button; and give a panel a frame after
  enabling it before raycasting, because a graphic enabled this frame is not in the canvas
  yet.
- **Panels lay out in fractions of a measured box, not pixels from an edge.** The record
  panel placed its rows a fixed distance down and pinned a hint block to the bottom, which
  is fine at one window height and collides at any shorter one — "TOTAL KILLS" ran into
  the text below it. Splitting a measured box `n` ways cannot collide at any size.
- **The grid must fit vertically as well as horizontally.** A `GridLayoutGroup` neither
  clips nor scrolls; content that does not fit is simply drawn past the edge, which cut
  the descriptions off the bottom row. `FitGrid` takes the smaller of what the width
  allows and what the height allows.

- **Nothing may write `anchoredPosition` on a card.** A `GridLayoutGroup` owns that
  property on every child it places, so the hover animation doing so dragged all six
  cards onto one spot — a grid that looked like a single card with five hidden under it,
  while every structural check still counted six. `HoverCard` animates `localScale`
  instead, and `VerifyFlow` fails if two cards share a position.
- **Every clickable tile is a `HoverCard`.** `ArenaCard`, `LevelButton` and
  `StoreItemCard` share one base rather than three copies of the same twenty lines —
  the copies had already drifted to three different growth amounts nobody chose, and
  the three screens sit on top of each other, so a card that grows differently from the
  one it replaced reads as the interface being slightly unreliable. Same reasoning as
  `UIText`. A subclass overrides `ApplyHover` and calls base to add what is particular
  to it (`ArenaCard` brightens its preview), and overrides `Hoverable` to say when the
  pointer should do nothing at all — `LevelButton` returns `Unlocked`, because a locked
  tile that lights up and then does nothing reads as broken rather than as locked.
- **Cell sizes are computed, not fixed.** A fixed cell is only right at one aspect ratio:
  three 404px cards fit 16:9 and slide under the record panel at 4:3, and a browser window
  is whatever shape the player left it. Card internals are anchored as fractions for the
  same reason, and the text auto-sizes.

TMP has no closing `</alpha>` tag — `<alpha=#99>` applies from where it appears. Writing
one prints those eight characters on screen, which is what the instruction strip and the
dashboard hint both did. Use `<color=…></color>`; `VerifyFlow` checks for it.

## The desert has ground, not a floor

The open zone's ground is a heightfield, not a slab. `LevelTheme.duneHeight` is the
switch: zero leaves the flat banks the walled arenas use, and anything above it builds
`FPSKitTerrain.BuildDuneField` -- a dune field of crossed asymmetric ridge trains over
broad basins, meshed as chunks with the river cut out of it as a true hole so the
navigation bake still gets no floor over the water.

Five things about it are worth not re-deriving:

- **The angle of repose is enforced, not hoped for.** Summed sine waves overshoot
  wherever two slipfaces land on top of each other. Three separate limits care: sand
  stands at about 33 degrees, the NavMesh will not bake past `agentSlope` (45), and the
  player's `CharacterController` stops at its `slopeLimit` (50) -- and **a wheeled
  vehicle gives up long before any of them**, which is why `ReposeDegrees` is 26 and not
  a compromise with the other three. `RelaxToRepose` is thermal erosion run as thirty
  Gauss-Seidel sweeps, and a sweep moves sand one cell, so the count is a *distance*: at
  ten, the steepest ground in the arena was still on the lip of a flattened pad, because
  the relaxation had not reached that far before it stopped. That it also rounds crests
  and piles a toe at the bottom of each slipface -- which is most of what makes the field
  read as sand rather than as mathematics -- is the bonus.
- **The dune wavelength is what sets the slope, and it is deliberately short enough to
  overshoot.** A dune's gradient is its height over its length, so `duneWavelength` is
  the knob that decides whether the field survives the relaxation intact. Set long enough
  not to overshoot, it comes back as smooth swells with no crest in it. Set short, the
  primary train is cut back to exactly the repose angle -- which is what a slipface *is*,
  sand piled until it slides.
- **There are no ripples in the heightfield.** A wind ripple is a couple of metres crest
  to crest and the grid is three, so putting one in the field samples it below its own
  Nyquist rate: what comes out is not ripples but a field of hard kinks at the grid
  spacing. Invisible on foot, and under a wheel it is a surface that chatters everywhere.
  The ripples live in the sand normal map instead. `SmoothField` then takes the creases
  out of what the relaxation leaves behind -- erosion stops the instant nothing is too
  steep, so it hands back planes meeting at the limiting angle, and smooth vertex normals
  hide those creases from the eye completely but not from a collider.
- **Everything asks the terrain where the ground is.** `GroundHeightAt` is the single
  answer and every pass that places anything calls it. A rock placed at y=0 on a dune
  field is buried or hovering, and which one changes per rebuild. `BuildPlayer` asks it
  too, rather than starting the player at a fixed `y = 1`.
- **Flat ground is reserved before the terrain exists, not carved after.** A compound is
  four straight walls at right angles and there is no version of that which follows a
  hill; a boulder needs no such thing and looks better half-buried. So `BuildOpenZone`
  runs in two halves -- `PlanCrossings`/`PlanLandmarks`/`PlanOutposts`/`PlanVantages`
  choose sites and call `FlattenPad`, then the terrain is generated with those sites
  already flat in it, and only then is anything built. `PlanX` and `BuildX` have to stay
  in the same order as each other, because the second reads the list the first wrote.

The village plot bound includes the full house footprint, so no outer wall crosses from
the level pad onto the sloping dune blend. Each hamlet's pad covers its complete rectangular
ground footprint. House doors and windows carry timber sunshades to break up the plain
facades and keep the architecture suited to the desert heat.

**Two pads that must agree have to be pinned to one height, and the nearest pad wins.**
Both of those are bugs that were written first and found by `VerifyTerrain`:

- A raised deck and the foot of its own ramp were each pinned to whatever the dunes were
  doing under them. The ramp is one rigid plank between the two, so they have to be
  level; pinned separately they were two flat discs metres apart in height with a couple
  of metres of sand between them, and that sand was the steepest ground in the arena and
  the one place relaxation could not touch, because both sides of the step were held.
- Overlapping pads were applied **in turn**, each lerping the height it was handed
  towards its own target -- so the *last* pad to mention a point decided it, however far
  away that pad was. A vantage sixty metres from a bridge, planned after it and therefore
  applied after it, reached the end of the deck through the tail of its own blend and
  pulled the sand there most of the way down to deck height. The bridge then ended at a
  step no agent could climb: the navmesh stopped at the water, the far half of the level
  became unreachable, and the bridge was still standing there looking exactly right. The
  pad with the smallest `t` now wins outright.

`Tools/unity-batch.sh FPSKitBatch.VerifyTerrain` is the regression test, and it makes
three assertions that pull against each other on purpose: **relief** (the ground has to
rise and fall by enough to hide a person, or the dunes are decoration), **gradient**
(and never by more than a vehicle can climb, measured at 1.5m -- about a wheelbase), and
**chatter** (and the gradient must not jump between adjacent steps, which is the thing
smooth normals hide completely and a collider does not). It raycasts the built scene
rather than re-running the generator, so what is checked is the collider the game
actually uses. **It filters on the `Sand` tag, not on the surface normal** -- the top of
a boulder is as near-level as a dune is, so a normal test kept half the rocks in the
arena and each one contributed two enormous steps.

## Surfaces are textured, and the textures are generated

`FPSKitTextures.cs` renders tiling albedo and normal maps for sand, rock, timber, adobe,
water, concrete, metal, snow and tile into `FPSKit_Generated/Textures/`. Every generated arena was flat-shaded before
this -- one solid colour per material -- which is readable and completely scaleless: a
dune the size of a house and one the size of a stadium are the same wash of orange, and a
player walking over the second cannot tell they are moving.

- **The maps are grayscale and the colour stays on the material.** Tinting a grey detail
  map with `_BaseColor` keeps `LevelTheme` in charge of what colour an arena is, which is
  the whole point of the theme being an asset. A coloured texture would quietly override
  it and a theme retune would then do nothing.
- **They are regenerated on every build rather than cached.** The generator is a pure
  function of constants, so the PNG bytes are identical every time and git sees no change
  -- which means there is no "reset the textures" step to forget, unlike the theme,
  roster, level and store generators.
- **They are written as PNGs and imported, not created as `Texture2D` assets.** A normal
  map has to go through the importer to be encoded the way the shader unpacks it.
  `MakeDetailMaterial` must also `EnableKeyword("_NORMALMAP")`: URP's Lit shader only
  samples the map when that keyword is on, and setting the texture does not set it -- the
  map is then assigned, visible in the inspector, and doing nothing.
- **Noise for a texture has to wrap.** `TileNoise` wraps its lattice at the period. A
  texture tiled across four hundred metres shows its seam as a hard line every few
  metres, and a hard line repeated in a grid is more visible than having no texture.
- **Each surface needs its own map.** Adobe borrowed sand's, and sand's detail is wind
  ripples -- parallel, evenly spaced, all the same size -- which on a vertical wall is
  not mud brick, it is corrugated iron.
- Sand's broad warp varies ripple spacing and breaks up crests, while remaining periodic
  at tile edges so the generated detail does not seam.

`Sand` is a surface tag, provisioned in `EnsureProjectTagsAndLayers` with the rest and
carrying its own `ImpactLibrary` entry. It is the tag a player sees and hears most: every
footstep across a dune field and most bullet impacts land on it, and a round cracking off
stone where it should throw up dust is wrong in a way nobody can name and everybody
notices.

## The river is a canyon, and the fence is a fence

`FPSKitDesert.cs` owns what the open zone is made of. Three pieces of it have traps in
them:

- **The canyon's top shelf must stay on the navmesh.** The wall is built as three
  stratified bands from one shared grid of points, and `CanyonBands` splits them at row
  two rather than anywhere prettier: the bridge decks are flush with that top shelf, and
  a lip laid unwalkable over the end of a bridge cuts the crossing in half -- silently,
  because the bridge is still visibly there and the path around it still completes.
  Everything below row two is held off the bake by `NoStanding`, because the bench and
  the talus are both shallower than the agent slope and would otherwise bake as islands
  twenty metres down, inside the kill volume, for the spawner to find.
- **The lip has to sit on top of the sand it overlaps.** A heightfield can only end on a
  cell boundary and the river does not, so the terrain's edge is a staircase; the first
  two rows of the canyon profile are nearly flat, six metres wide and *above* the rim to
  cover it. Get the sign wrong and the terrain pokes back through the rock.
- **The river is sliced, and the cliffs are hidden.** The minimap draws the footprint of
  what it is given, so one four-hundred-metre cliff mesh comes back as an axis-aligned
  rectangle over a third of the map with the meander nowhere in it. The cliffs, the bed,
  the fence runs and the bridge gates are all `Hide`-den; the water is cut into slices
  that follow the bend and each one is `Mark`-ed, which is what actually tells the player
  where the river is.

**The apron stops where the terrain stops.** It used to run under the whole arena, which
was invisible under a flat floor; under a heightfield it is a sheet at y=0 cutting up
through every hollow that dips below zero. It is also built as meshes with world-space
UVs rather than as scaled cubes -- a cube's UVs run nought to one across whichever face,
so a kilometre-wide slab stretches one tile of texture across the whole kilometre, which
reads as smeared streaks all along the horizon.

**A boulder needs `NoStanding` too.** A NavMeshSurface reads any surface shallower than
the agent slope as walkable, so the dome on top of every rock, the crown of every palm
and the sheet of every tarpaulin bakes as an island nothing can path to -- harmless until
`LevelManager` samples the mesh near the player to place a spawn, finds one, and puts an
enemy on a tree.

**The kill volume is the river, not the corridor the river wanders about in.** It is a
chain of boxes following `GorgeCentreAt`, each as wide as the bed plus the foot of the
talus, with its lid two and a half metres over the water. Written as one axis-aligned box
as wide as `hazardWidth + hazardMeander * 2.5` with its lid five metres under *zero*, it
described a hundred-and-thirty-metre band of the arena rather than the water -- which was
survivable on a flat floor and lethal the moment the floor became a dune field, because a
dune field has basins in it. The sand on the western approach bottoms out thirteen metres
down, eight metres inside that lid, so four thousand square metres of ordinary walkable
sand nowhere near the river killed the player outright the instant they stepped on it.
From inside the game that is "I walked towards the river and died", with no water, no
edge and no fall anywhere in sight. `VerifyZone` now raycasts the built arena and fails if
any ground the level says is standable -- tagged `Sand`, or covered by navmesh -- is
inside the trigger.

**A mesh wound inside out does not look like a hole, and that is what makes it
expensive.** `ButteMesh` took its wall quads *around* each ring rather than *up* the wall,
so every face of every butte and every backdrop mesa pointed at its own axis. A butte is a
big closed solid, so with the near wall culled away the eye is shown the **inside of the
far wall** -- a perfectly convincing silhouette, lit backwards. What that produces is a
hill with no lit face anywhere on it, in shadow from every direction and at every time of
day, which reads as a rock in the shade and not as a rendering fault. The collider is
unaffected, so the player walks at the hillside, passes through a surface that is not
drawn, and stops dead against nothing: from inside the game they are standing in the
middle of a hill, trapped by something invisible. Ishaan's report was "this hill is the
bug, I get inside and get trapped", with a screenshot of a uniformly dark brown mound,
and that screenshot is the whole diagnosis.

`VerifyZone` measures it now rather than looking at it: for a closed shape that contains
its own centroid every face normal points away from that centroid, so counting how many do
is a direct read of the winding that needs no camera, no lighting and no opinion. Only the
genuinely closed star-shaped pieces are checked -- a fence run, a cliff band, a welded run
of boulders or a flat sheet of water has a centroid the test means nothing about, and each
of those sits somewhere between 34% and 84% while a correct butte is at 100%.

**Every rock in the arena is hollow, so every rock has to be buried.** A boulder and a
butte are both closed shells cut off flat underneath, which is exactly right on a plane:
put the cut a little below the ground and it is buried all the way round. On a dune field
the ground under a forty-metre butte varies by ten metres, so a cut pinned to the height
at the rock's *centre* stands clear of the sand downhill of it -- and the gap that leaves
is a doorway into the inside of a closed mesh. The inside of a closed mesh is not drawn at
all, because every face of it is a backface, so the player walks in through a gap they can
see, the view fills with rock at angles that correspond to nothing, and they are wedged
inside geometry that is invisible and cannot be climbed. `LowestGroundIn` is the fix:
every rock is sunk to the lowest the heightfield gets anywhere under its own footprint,
scanned cell by cell rather than round a ring of spokes -- ten spokes at half a
twenty-metre radius are eight metres apart, and a dune drops several metres in eight.
`VerifyZone` measures `bounds.min.y` against the sand at sixteen points under every rock
and fails on a gap. **Not by raycasting upwards from the sand**, which was the first
attempt and measures the wrong thing: a ray up from inside a properly buried butte leaves
through the underside of a weathering ledge seven metres up and reports seven metres of
air under a rock that is buried six metres deep.

**`BoulderMesh` is a unit icosphere, so it comes out about 2.6x whatever scale it is
given.** `BoulderSpread` is that number, and `Boulder` takes a *width* in metres because
every call site was already writing one -- a "four metre boulder" was arriving eleven
metres across, which is not cover, it is a dome, and a field of them was most of what made
this arena's rock read as lumpy blobs. A butte is the landmark; a boulder is something to
crouch behind.

**A butte's lowest weathering ledge is suppressed.** `ButteMesh` steps its rings out every
third band, which is what tells you how tall it is from across the map -- but at the foot
of a forty-metre butte that is a three-metre overhang at head height with an alcove under
it, and the player walks into what reads as a cave and is not one. The bands above eye
level say everything the bottom one did.

**The raised decks are the positions the level is meant to be fought from, and there used
to be one of them.** `PlanVantages` claimed a single circle big enough for the deck *and*
the whole run of its ramp -- about twenty-three metres, when the ramp only ever leaves in
one direction -- and gave each vantage exactly one attempt, thrown thirty-six to sixty
metres from a landmark that had already claimed twenty-five of those metres. The
arithmetic almost never came out, so nine in ten were silently dropped. The deck claims
the deck, the ramp foot claims the ramp foot, and each vantage gets ten throws; seven to
ten now land.

**The map's edge is a sand hill, not a row of rocks.** `BuildBoundary` builds one
continuous dune ridge wrapped round the arena as a square annulus: a profile that rises
out of the sand six metres inside the boundary, crests twenty metres outside it and lies
back down, so the four sides are the same function of `max(|x|, |z|)` and meet exactly
along the diagonal with no corner to get wrong. What it replaces was a row of the same
stepped butte mesh the horizon is made of, each squashed onto a footprint half as wide as
it was tall -- and a butte steps *out* every third band, which at mesa proportions is a
weathered bench and at those proportions is a flange sticking out sideways. Forty of them
in a row came out as black spikes with a head-height pocket under every flange: a hill
you cannot see, that you walk into and cannot walk back out of. **The seal moved too, and
that is half the fix** -- it used to sit a metre inside the arena boundary while the rocks
were placed on or outside it, so the player was stopped by an invisible wall standing in
open sand with the thing meant to stop them either twenty metres behind them or seven
metres out of reach. It is now at the toe of the hill: the sand is what stops you, and it
carries on rising in front of you. The outcrops on it are boulders rather than buttes,
because a boulder is round and has nothing to get caught under.

**Two things flip in that ridge's winding, not one.** The grid's axes are "outward" and
"along"; which world axis each of those is depends on which side is being built, and which
way outward points depends on the side's sign -- so a face is up when
`cross(alongStep, outwardStep).y` is positive, which is `sign > 0` on the runs that go in
x and `sign < 0` on the runs that go in z. Keyed on the sign alone, the north and south
runs came out inside out: a twenty-metre hill drawn from no angle above it, that a raycast
passes straight through, with the rocks sunk into it left hanging in the air over the
dunes. It was found by the buried-rock check above reporting nineteen metres of air under
an outcrop, not by looking at the arena -- the one side that had been photographed was one
of the two that were right.

## The industrial zone is a plant, not a yard with crates in it

`LevelTheme.industrialZone` is the third layout mode beside the walled box and the open
zone, and Industrial Warehouse is built with it at 500x500m. `FPSKitIndustrial.cs` is
the plan; `FPSKitIndustrialParts.cs` is the small fittings the art pack does not contain
-- stairs, catwalks, railings, signage; `FPSKitFactory.cs` is the big structures it does
not contain either.

A ring road, two avenues and three cross streets cut the site into 16 blocks, and each
block gets a district **by its shape** rather than at random: *power house* (a cooling
tower, its boiler hall, two stacks and a transformer compound), *works* (a production
hall with a stack and a silo bank), *tank farm* (tanks inside a chest-high bund, with a
pipe run and catwalk leaving it), *container yard* (stacked rows making lanes, a gantry
crane across them, walk-through containers as the flank) and *depot* (the low-rise
filler). Pipe bridges cross the streets between neighbouring districts.

**Mass is what an industrial site is made of, and it cannot be added as scatter.** The
pack's biggest building is a hangar about twenty-five metres long and seven high; the
site is five hundred metres square. Built from those alone -- which is what the first
version of this was -- nothing on the map is taller than a lamp post: from the ground the
whole arena is one flat horizon with small sheds dotted over it, and from the air it is a
car park with a grid painted on it. No amount of barrels and pallets fixes that, because
the problem is the silhouette. So `FPSKitFactory.cs` builds a sixty-metre production hall
with a walkable roof, forty-metre brick stacks, banks of silos, a cooling tower, gantry
cranes and pipe bridges. Truck-door corners get concrete-footed safety bollards, and
stormwater grates sit in the road gutters. The pack's sheds are demoted to the outbuildings they are
the right size for. Three rules hold that file together:

- **a hall's roof is reached by its own stairs and is meant to be fought over**, which is
  why it is flat with a parapet rather than pitched -- a pitched roof has to be held off
  the bake and can never be stood on, and a deck with chest-high cover on four sides and
  two stairs to it is the best position in the district;
- **the stairs run *along* the wall, not out from it.** A twelve-metre climb at a
  walkable pitch is nineteen metres of flight; straight out from the wall that reaches
  past the edge of the block and through whatever the district put there;
- **anything a player can walk into has two ways out.** The hall has a roller door at
  each end and a personnel door in each side wall.

**The ground is wearing something.** A uniform surface is the loudest artificial thing in
an outdoor level and the hardest to name: a quarter of a million square metres of one
tint of one texture has no near detail at all, so the eye cannot judge distance and the
site reads as a car park however many sheds are on it. `BuildYardDetail` lays kerbs
(which draw the street plan at eye level rather than only from the air), worn patches,
spills, markings and grated storm drains along straight road gutters. All of it is faded
and close in value to what it sits on -- the
first cut was saturated yellow hatching at full opacity across every junction, which was
the loudest thing in the arena from every angle, and the job of ground detail is to be
noticed without being looked at.

**A street is a corridor, not a gap between car parks.** Ishaan's next verdict was "very
empty, the roads are quite big", and it was one fact: twenty-metre carriageways between
hundred-metre yards with buildings stood in the middle, so every eye-level view was forty
metres of open asphalt. `FPSKitIndustrialDense.cs` narrows every road to ten metres of
carriageway (`CarriageHalf`; still two lanes for the car) inside the same twenty-metre tile
grid, walls every block at `CompoundLine` with gates, spans three streets with buildings the
road runs through, and packs buildings against the compound walls. Four rules hold it up:

- **Two gates on two sides, or no wall.** A walled yard is a building for navigation
  purposes: one way in is a cul-de-sac, none is an island. Gates go where the ground just
  inside is clear, and everything built afterwards keeps off the gate approaches.
- **Free ground is asked of the physics scene**, not of the claim circles -- districts
  claim almost none of what they scatter. Loose pack clutter (pallets, barrels, crates)
  gives way to a building; anything structural vetoes it.
- **The districts are compact.** The container yard used to space its boxes evenly from
  edge to edge and the depot scattered its stock over the whole block, which left no
  footprint anywhere and no yard either: the block looked empty from the ground and was
  full to anything placed later. Stacks take a band along one side; stock one laydown area.
- **Closed buildings are sealed with `SealBox`**, a world-space volume -- not `NoEntry`.

**High ground is reached by stairs, and every walkway has one at each end.** There is no
climbing in the game. Roof routes (`RoofRow`) put a stair up the yard face of the first and
last building, landing flush where the parapet is open, and bridge the roofs between; their
shells stay on the bake and `SealBox` stops a metre under the roof. Every pipe-run catwalk
now has a stair at both ends (Ishaan: "the catwalk has only one direction stairs"); the far
one is checked against the ground *past the end of the pipes*, because the deck is exactly
the pipe run's length and counting its own trestle refused all four. `VerifyReach` samples
near eye level, so it does not measure the roofs.

**Nothing on this site may shine.** The pack's concrete comes at smoothness 0.5, and with no
baked reflection anywhere in the kit, gloss reflects Unity's default grey-blue: the compound
walls read as brushed silver ("the shiny silver wall doesn't fit"). `Matte` takes it off.
The same missing reflection is why the catwalks were invisible for a different reason: they
were the transparent lattice all over, tiled so finely it mipped to nothing, so the stair
stood in plain view and the bridge it led to did not. Walkways are a solid deck on a
safety-yellow frame now. `CaptureViews` runs every particle system forward before
rendering, because particles do not simulate in edit mode and no plume had ever been seen.

**None of it is a NavMeshModifier.** Flat ground detail is kept off the bake by *layer*
-- the backdrop layer, which the NavMeshSurface already excludes and which the apron
already uses. A modifier does not exclude geometry, it marks it *Not Walkable*, so a kerb
round every road tile is an unwalkable line drawn between every carriageway and every
block: two thirds of the site came back severed from the rest, and the only symptom was a
level that quietly never ended.

Two facts out of the pack decided the whole design, and **`Map_v1.unity` is binary, so
they came from `AssetDatabase` and not from grepping it**: `Hangar_v2` and `Hangar_v3`
carry non-convex mesh colliders, so they are hollow and the player can walk inside them;
and the road set is modelled on a **20m tile grid**, so a street network is a matter of
laying tiles. `Hangar_v4` is a solid box -- a silo, not a shed.

The large production halls now have dark clerestory glazing between their wall ribs and
steel loading canopies over both vehicle doors. These break up blank wall planes and
make the open bays read as working docks; both details are visual-only on the backdrop
layer, so they do not alter the proven navigation.

Four things about it each cost a rebuild, and all four are the same failure: the change
compiled, the build reported success, and nothing happened.

- **`FPSKitThemes.Configure` does not reach the asset without `ResetThemes`.** The first
  build of this rebuilt the old 110x150 box and said it had succeeded. Same trap as the
  roster, the ladders and the store; it was caught only because the zone's own log line
  was missing from the output.
- **Fog is exponential-squared, and 0.009 was calibrated for a 110m box.** By 250m it is
  all but opaque, so on a 500m site the far half of the map is simply not there -- and
  what that reads as is an *empty* arena rather than a foggy one. 0.0032 here; the
  desert is at 0.0011.
- **The yard and the roads must not be the same surface.** Both started as the pack's
  dark asphalt, and from the air the street grid the entire layout is organised around
  was invisible: one black field with sheds round the edge. The yard is the same asphalt
  tinted pale. Do **not** floor it in the pack's concrete instead -- that asset is a
  *wall* panel with strong horizontal banding, and tiled across 500m it reads as
  corrugated iron laid flat.
- **A base colour multiplies the albedo, so it cannot brighten a dark texture.** The
  first pale yard was tinted light grey, which on asphalt at 0.15 came out at 0.10 --
  darker than it started. Brightening needs a tint above 1.

**Anything with a flat or gently curved top and no way up is kept off the bake**
(`NoStanding`): bund caps, the boundary wall, pipe runs, roof plant, **every** container
rather than only the stacked ones, and the tanks and silos. A `NavMeshSurface` takes any
surface shallower than the agent slope, which includes the top third of a cylinder --
fifteen tanks is fifteen perches the spawner can choose and nothing can path to. The
catwalk decks and the hall roofs stay bakeable on purpose, because they are reached by
stairs and are meant to be stood on.

**`NoStanding` is not enough for anything with an inside; that needs `NoEntry`.** A
modifier marks the geometry it is on, and the floor inside one of the pack's sheds is not
the shed -- it is the site's own yard slab running underneath it. So `NoStanding` takes
the roof off the bake and leaves the room: a slab of navmesh the size of a shed, walled
in on four sides, joined to nothing. `NoEntry` puts a `NavMeshModifierVolume` over the
whole footprint instead, which asks the right question -- nothing in this box is
walkable, whatever it is made of. **Every pack shed gets one, including the hollow ones**,
and that is a measurement rather than a precaution: `Hangar_v2` and `Hangar_v3` carry
non-convex colliders so a *player* can walk inside them, but their door openings do not
admit a half-metre agent. Sixteen of those were sixteen rooms the spawner could stand an
enemy in for a whole level. The interior fight belongs to the hall, whose doorways are
eleven metres wide and are checked.

**Every art-pack lookup is allowed to fail.** `Pack` returns null and warns once, and
every caller falls back to a built shape or skips that dressing, because the pack is
third-party and a project without it must still build a playable arena.

The level ladder is deliberately not retuned for the larger site: it is shared by all
seven arenas, and `maxSpawnDistanceFromPlayer` already clamps to 80m on a wide arena, so
the fight stays local to the player however big the map is.

## Baked is not the same question as reachable

A `NavMeshSurface` bakes wherever an agent fits. It never asks whether one could arrive.
So every arena is full of walkable ground joined to nothing -- the floor inside a sealed
shed, the roof of a container, the cap of a bund, the four metres of yard outside the
fence, the deck of a walkway whose stair faces the wrong way, a hollow between three
boulders.

**What that costs is a level that quietly does not end.** `LevelManager` places a spawn
by sampling the navmesh near the player; `NavMesh.SamplePosition` answers "is there
walkable ground near here", and every one of those islands says yes. The enemy arrives,
stands in a room for the rest of the round, and nothing is logged, nothing errors and
nothing moves. The player hunts an arena that sounds occupied and is not, the clock runs
out, and they are scored against kills that were never available. When the check below
was first written, **two thirds of the industrial site could not reach the player**, and
every other build check in the repo was green.

Three things answer it, at three different levels:

- **The spawner refuses it.** `LevelManager.requireReachableSpawns` makes every spawn --
  the ring around the player and the authored points both -- demand a *complete*
  `NavMesh.CalculatePath` to the player, not merely a sample. The player is snapped onto
  the mesh first, because they spend a good deal of a level off it (mid-jump, on a crate,
  on a catwalk) and a destination off the mesh would make every route incomplete and
  refuse every spawn in the level.
- **The leash discards it.** `IsWalledIn` is a fourth condition on `IsLost`, beside the
  distance, the fall and the off-mesh tests, and it catches the one none of those can:
  an enemy sealed inside a shed forty metres away is inside every other limit and will
  stand there forever. Rationed at `reachCheckInterval` with the first check offset per
  enemy, because a *failing* path query is the expensive one -- it searches the whole
  island the agent is on before it can say no.
- **The builder does not make it.** `NoStanding` for anything with a flat top and no way
  up, `NoEntry` for anything with an inside, and `SealNavMeshOutside` for the ring of
  ground beyond whatever holds the player in -- the dune field runs forty metres past the
  boundary before it fades, and the plant's yard slab runs out to the boundary wall
  behind its fence.

`Tools/unity-batch.sh FPSKitBatch.VerifyReach` is the regression test. It samples the
navmesh across every built arena and fails if more than 2% of it cannot path to the
player. Not zero, deliberately: a dead end behind a chimney and the inside of a ring of
crates are real places a real arena has, they are a few square metres each, and the
spawner already refuses them. What the threshold is for is the other kind -- a building,
a district, or everything outside a fence, which is percent rather than fractions of one.

Two things it will not catch if written casually, both of which cost a run here:

- **Sample from just above the ground, not from the sky.** The search radius is measured
  from the point given, so a probe dropped from sixty metres up finds nothing anywhere
  and the whole test passes on an empty set.
- **`NavMeshPath` must be created on demand, never in a field initialiser.** A field
  initialiser runs inside the MonoBehaviour's constructor, and Unity refuses to build one
  there: it throws `InitializeNavMeshPath is not allowed to be called from a MonoBehaviour
  constructor`, leaves the field null, and the exception is filed against the construction
  of the object rather than against anything you wrote. Every route query then throws a
  `NullReferenceException` inside the spawn coroutine, every spawn fails, and the arena
  stays empty until the clock ends it. Same rule and same symptom as `EnemyAI.Block` --
  a private property with a null check, and nothing in `Awake`.

## The map reads the level, it does not have one

`Minimap` is the square in the top-left of the HUD: the level from above, turning under
a player arrow that stays put, every live enemy on it in the colour its archetype is
wearing. `FPSKitSceneBuilder.BuildMinimap` builds the frame; everything inside it is
worked out at runtime.

**It is drawn, not rendered.** A second camera pointed at the floor is the obvious
build and the wrong one: it costs a full extra pass over the arena every frame on a
platform where the whole game has to fit in a browser tab, and what it produces is a
top-down photograph of grey boxes on a grey floor. Instead `ScanStructures` walks the
scene once, keeps the footprint of everything solid, and redraws those footprints as
flat quads — a few dozen of them, tone-separated into walls and cover by height.

That is also what makes it work in a level the kit never built. There is no plan of the
arena anywhere, so **a rebuilt arena cannot disagree with its map**, and
**FPSKit > Add Gameplay To Current Scene** drops it into somebody else's level correct
on the first frame. Four things fall out of that and are worth not re-deriving:

- **Footprints come from local bounds through the transform, never from world bounds.**
  A world bounding box is axis aligned, so a cover wall laid at forty-five degrees comes
  back as a square twice its size — and half of what the builder places is turned.
- **A stack of crates is one shape from above.** Three cubes on the same square metre
  are three identical quads, and the arena is built out of stacks: collapsing them
  (`Covered`) is most of the difference between a map and a field of speckle. The other
  half is `minStructureSize`, measured on the *long* side so a thin cover wall survives
  and a crate does not.
- **Actors are never scenery.** The structure sweep happens once, so an enemy standing
  in it would be baked into the map as a wall for the rest of the level. Anything with a
  `Health` or a `Pickup` above it is skipped and drawn live instead.
- **Nothing it draws is clickable.** The pause menu and the results screen are on the
  same canvas; every quad it makes has `raycastTarget` off, the same rule as
  `FPSKitMenuBuilder`.

**`MinimapMarker` is the seam.** Geometry can say how tall something is and not what it
is, so a river is a grey box and so is the bridge over it. Drop the component on an
object and the map draws it the component's way — colour, footprint or pip, and a draw
order so a bridge lands on top of its water — skipping every filter above. A new kind of
terrain is a marker, not a branch in the minimap.

**The projection is the part to be careful with.** Unity turns `+Z` into
`(sin y, cos y)`, so the rotation that puts the player's forward at the top of the map
is by the yaw itself and not by its negative. Get that sign wrong and the map is correct
whenever the player faces a cardinal direction and mirrored the rest of the time, which
is very easy not to notice. `FPSKitLevelTest` is the regression test: with the level
full, it projects every nearby enemy from first principles — how far along the player's
right, how far along their forward — and fails if a pip is more than four pixels from
where that says it should be. It is written with dot products rather than with the sine
and cosine the component uses, because a test that repeated the formula would repeat the
mistake with it. It also fails a map drawing nothing, which otherwise looks exactly like
a map that works.

## Content is data, not code

Three ScriptableObject types are the extension points, and all three exist so that adding content never means editing the builder:

- **A new arena is a `LevelTheme` asset.** Duplicate one in `FPSKit_Generated/Themes/`, retune it, select it, then **FPSKit > Build Scene > From Selected Theme Asset**. The six named menu entries are just shortcuts to the built-in assets. Re-run **FPSKit > Build Dashboard** afterwards so it gets a card and a preview.
- **A new enemy is an `EnemyArchetype` asset.** Duplicate one in `FPSKit_Generated/Enemies/`, change the numbers, add it to the LevelManager's roster. There is one base `Enemy.prefab`; an archetype is *stamped onto* an instance at spawn (stats, scale, colour via `MaterialPropertyBlock`, behaviour, score, drops). `EnemyArchetype.Role` decides whether it joins the normal mix, counts as an elite, or is drawn only for boss levels. Its `unlockWave` / `weightGrowthPerWave` / `retireWave` fields are difficulty *steps* now, not wave numbers -- the names are kept because renaming a serialized field silently drops the value out of every asset already written with the old one.
- **A new gun, bomb or supply is an entry in the `StoreCatalog`.** The stock lives in `FPSKit_Generated/Store/`: a `WeaponData` per gun, a `BombData` per explosive, a `ConsumableData` per supply, and one catalog that prices them. Duplicate an asset, add an entry with a **new, permanent `id`** -- ownership and upgrade level are filed under it, so renaming one forgets every purchase of that item -- and the store clones a card for it at runtime.
- **A new level is an entry in a `LevelSet`.** The ladders live in `FPSKit_Generated/Levels/`, one asset per arena, each a plain list. Add an entry in the Inspector and re-run **FPSKit > Build Dashboard** so the arena card picks up the new count; the level select clones a tile per entry at runtime, so nothing needs rebuilding for the tiles themselves.

When adding either, prefer a new asset over a new branch in the builder. If a knob genuinely does not exist yet, add it to the ScriptableObject — not to `FPSKitSceneBuilder`.

## Domain reload is OFF — every static needs a reset hook

`ProjectSettings/EditorSettings.asset` has `m_EnterPlayModeOptionsEnabled: 1` and `m_EnterPlayModeOptions: 3`, which is `DisableDomainReload | DisableSceneReload`. Entering play mode is fast, and **no C# static is ever reset between play sessions.**

That makes a specific bug class very easy to write and very hard to spot: the game works the first time you press Play and is dead the second, while every compile and build check still passes. A gate left false, a cached singleton pointing at a destroyed object, a shutdown flag set by the `OnApplicationQuit` that fires when play mode exits.

So: **any static field that carries state must be cleared in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hook on its own class.** That hook runs before the first scene loads on every play session, with or without a domain reload. The classes that currently own one are `PlayerMotor`, `GameDirector`, `GameSession`, `DamageNumber`, `EnemyHealthBar`, `MobileInput` and `OneShotAudio`. `Time.timeScale` is not a static but persists the same way, and `GameDirector` resets it in the same hook.

`Wallet`, `Loadout`, `LevelProgress` and `PlayerProfile` are static and deliberately have **no** hook, because they hold no state: every one of them is a set of accessors straight over PlayerPrefs, so there is nothing cached to go stale between sessions. That is the reason they are written that way and not merely a convenience -- a cached balance would be one more thing to clear, and the one nobody would think to.

`Tools/unity-batch.sh FPSKitBatch.VerifyReplay` is the regression test: it plays two real sessions back to back, ends the first on a scored level (the messiest state a player can leave -- frozen clock, no input, free cursor), and fails if the second does not start clean.

### The sibling trap: non-serializable fields after a mid-play reload

Statics are one half. The other is that **`ScriptChangesDuringPlayOptions` is unset**, so it sits at its default *Recompile And Continue Playing* — edit any script while play mode is running and Unity reloads the domain underneath the running game **without re-running `Awake`**.

Unity's reload backup carries a field across only if its *type* is serializable, and it does this for private fields too. So a partial survival is the normal outcome:

| Field | Survives a mid-play reload |
|---|---|
| `Renderer[]`, `Color[]`, `float`, any `UnityEngine.Object` reference | yes |
| `MaterialPropertyBlock`, `Coroutine`, `Action`, any plain C# class | **no — comes back `null`** |

That asymmetry is what makes it dangerous: a method guarded on the fields that survive runs anyway and hands the null one to Unity. `EnemyAI.SetFlash` did exactly this and threw `ArgumentNullException: dest` out of `Renderer.GetPropertyBlock`, once per renderer per frame, for the rest of the session.

So: **anything whose type Unity cannot serialize must be created on demand, not assigned once in `Awake`.** `EnemyAI.Block` and `EnemyHealthBar.Block` are the pattern — a private property with a null check, and `Awake` left out of it entirely so the property is the only thing that can create it.

### The editor sibling: a builder static leaking from one arena into the next

The same rule applies one layer up, and it is easier to miss because no play session
is involved. `FPSKitBatch.BuildAllThemes` builds six arenas in a single editor process,
so every static in the builder carries whatever the *previous* arena left in it.

The heightfield is the one that bit. `_ground` is cleared by `ResetTerrain`, and the two
layout modes that build terrain — the open zone and the industrial zone — both call it on
their way in. A walled box calls neither, because it has no terrain to build. But it does
read one: `BuildPlayer` asks `GroundHeightAt(0, 0)` where to stand the player, and with
the desert's dunes still loaded that answer is the desert's spawn hollow, twelve metres
below a floor that is at zero. **Four of the six arenas shipped with the player buried
under them**, in exactly the order `BuildAllThemes` runs.

Every check in the repo passed. The scene builds, the navmesh bakes, `VerifyReach` found
navmesh within its twenty-metre tolerance and was satisfied. The only symptom anywhere
was that the dashboard's card for those four arenas came back as a band of bare sky —
which is what you see from under a floor, and reads as a preview camera that needs
tuning rather than as a level with its player inside the ground.

So `BuildFromTheme` calls `ResetTerrain` itself, where **every** arena passes, rather
than leaving it to the two that use it. That is the general shape: clear a builder static
at the top of the per-arena build, not in the branch that happens to write it.

`VerifyReach` now asks the question directly before it asks any navigation one — it
raycasts down from the spawn and fails if there is no collider within 3.5 m, and prints
what the player is standing on for each arena, so "Floor", "Dune" and
"Road_set_v1_b_floor" are visible in a passing run rather than only in a failing one.
A tolerance is the wrong instrument for this: thirteen metres of error sat comfortably
inside the old one.

## Enemies fight back, and the fight is legible

Three rules hold the combat model together. They are cheap to break by retuning one
number in isolation, so they are written down.

**Reach has to be shared with the brake.** A `NavMeshAgent` stops a full
`stoppingDistance` short of its destination. Sending a melee enemy to a point "just
inside its attack range" therefore parks it that much *outside* the range, the attack
check never passes, and the whole level gathers around the player and does nothing --
silently, with no error and every build check green. `EnemyAI.DesiredStandOff` now
subtracts the brake before choosing a destination, and the prefab's `stoppingDistance`
is 0.8 rather than 1.5 so there is less of it to pay for. **It is a small fixed number
in both places that set it** -- `FPSKitSceneBuilder` and `FPSKitEnemySetup` -- and never
derived from the attack range, which is the same bug wearing a formula: at `range * 0.7`
a 2.2m melee reach keeps 0.66m to stand in and a 25m rifle keeps none, so
`DesiredStandOff` returns zero, `preferredRangedDistance` stops meaning anything, and
the shooter settles wherever 17.5m of braking put it. `VerifyCombat` only fights the
builder's prefab, so the setup tool is the copy that can drift unnoticed. `VerifyCombat` is the
regression test: it stands the player inside an enemy's reach and fails if nothing hits.

**A shooter is not only a shooter.** `EnemyAI.meleeRange` makes an armed enemy swing
the rifle at anything in its face, because a shot traced from the eyes at a body
standing inside the muzzle misses forever -- so without it, closing the distance is the
safest place on the map. `rangedDamageMultiplier` is the counterweight: a shot is worth
well under a swing across the whole roster, since a crowd that can reach you from
anywhere is not the fight a crowd that must close is.

**Reacting to being shot happens on three scales.** A light hit is a flinch (movement
stops for `flinchDuration`, the next attack is pushed back by `flinchAttackDelay`), rate
limited by `flinchCooldown` -- without that floor a held trigger at 600rpm lands a
flinch every tenth of a second and the reaction becomes a stunlock. A hit at or above
`staggerThreshold` staggers and costs the attack outright. Sustained fire fills a
suppression bucket and sends the enemy looking for cover (`State.Retreat`), on a
cooldown, and switched off entirely for bosses and armoured variants.

**Seeing you is rationed; noticing you is not.** `CanSeeTarget` is the one thing every
enemy does on every frame, and its third test is a physics cast -- forty enemies at
sixty frames a second is thousands of casts a second on a platform that has to fit in a
browser tab, for an answer that changes when somebody walks behind a crate. So the cast
runs at `EnemyAI.sightCheckInterval` (0.1s) and the answer is held between casts, with
the first one seeded at a random offset in `Awake` so a level's worth of enemies does
not all cast on the same frame and turn the saving into a stutter. The range and
field-of-view tests are still made every frame, because they are arithmetic and because
a failure has to clear the cache rather than leave a stale `true` behind it -- what this
must never do is delay the cast that *first* sees the player. The field-of-view test is
also skipped outright once `_hasAlerted`, which is almost the whole of a fight: written
as `angle > fov && !_hasAlerted` it reads the same and computes an arc cosine per enemy
per frame to throw the answer away.

Speed is read against the player, who walks at 5.6 m/s and sprints at 8.2. Nothing in
the roster outruns a sprint and only the two rushers beat a walk: disengaging has to
stay possible or positioning stops being something the player can do. The base agent is
3.2 m/s and `FPSKitLevels` caps a level's `speedMultiplier` at 1.35.

`EnemyAI` publishes `LastReactionTime`, `LastReactionLocal` and `LastReactionStrength`,
and `EnemyLimbAnimator` reads them each frame to throw the body along the bullet. They
are plain values rather than an event on purpose -- an imported character with a real
Animator ignores them, and three floats survive a mid-play domain reload where a
subscription would not.

## Where a round lands changes what it does

Added 2026-09-26 at Ishaan's request: enemies that look, move, bleed, sound and die like
bodies rather than capsules. His calls: blood heavy but no dismemberment (with a Settings
switch), limb wounds that change the fight, scaled by how strong the enemy is, voices mixed
by type (soldiers human, heavies and the two generic bosses creature; the Augers are people
and stay human). He then said he has no Asset Store soldier and to keep and improve the
built one; bodies stay until the level ends; the synthesised voices were "very bad".

**The body.** `BuildEnemyPrefab` builds a segmented soldier: hips, knees, shoulders, elbows
and a neck are real pivots. What is drawn is a lofted mesh per segment (`FPSKitSoldier`:
tapered thighs, calves, deltoids, a chest broader than the waist, a jawed head); what is hit
is a plain primitive underneath with its renderer removed (`HideUnder`, `Hitbox.visual`
points the stain at the drawn mesh). Gear is tagged Metal so the archetype colour stays on
the clothes and skin. Kit named `Kit_<Human|Creature|Elite|Boss>_` is switched per archetype
in `EnemyArchetype.Dress`. The uniform is the archetype colour muted toward drab
(`UniformColor`) over a camo weave; skin is a real tone picked per soldier, a pallor for
creatures, which also walk hunched. Every segment has a `Hitbox` with a
`BodyPart`; the part rides on `DamageInfo.part`. The rifle hangs off the torso, is aimed by
`EnemyLimbAnimator.PoseWeapon`, and the hands reach its two grips by two-bone IK
(`ReachFor`). It used to ride the right arm, which pointed forward only with both arms held
out straight: a mannequin with a gun between its hands.

**Wounds are behaviour** (`EnemyWounds`). Per-part damage as a fraction of max health; past
`limpAt` a leg limps, past `crawlAt` (or two limping legs) it crawls, past `disarmAt` the gun
arm drops the rifle (a real object) and `EnemyAI.Disarm` turns it melee. Every threshold is
stretched by `EnemyArchetype.woundResistance` (0-1, set per archetype in `FPSKitEnemyRoster`).
A boss never crawls and never drops its weapon. A headshot is lethal to a Standard, to an
Elite only with its shield down, never to a Boss (which is staggered instead); the lethality
is done in `Hitbox.Receive` by raising the damage, so the kill passes through Damaged/Died
like any other. Wounds only ever slow or loosen an enemy -- the sprint rule is untouched.

**Blood** (`BloodFX`, `EnemyGore`, assets from `FPSKitGore`). One spray and one mist particle
system per level, emitted into by code; splats are a ring of quads capped per quality tier;
wounds are quads on the nearest evenly-scaled ancestor of the hit segment; limbs darken via
`EnemyAI.Stain` (written into the rest colours, or the wind-up flash washes it off). Blood
materials must have `_BlendModePreserveSpecular` off: URP's default keeps specular where
alpha is zero and every splat drew a pale square of sky. Keep their smoothness low (0.3) for
the same reason, or flat pools come out lilac. `GameSettings.Blood` off leaves a grey puff.

**Corpses stay.** `Health.destroyOnDeath` is off on the built enemy: bodies lie where they
fell until the scene changes, frozen and stripped of behaviours and (below High) shadows by
`EnemyDeath.Rest`. Pools are a separate ring from splats (`BloodFX.Pools`, never recycled
within a level's roster) and spread for about a minute, front-loaded.

**Death** (`EnemyDeath`). Built at the moment of death, not kept kinematic: a rigidbody per
segment and CharacterJoints with a body's limits, made in the *built* pose (limits are
measured from the pose a joint is created in) then put back. `Classify` picks the fall:
headshot drops, blast throws, a burst over `heavyShare` blows back, a leg shot buckles,
otherwise doubles over or staggers back. Corpses freeze after `settleTime`, sink before
`Health.destroyDelay` (12s), and only a tier's budget simulate at once. A rigged character's
`RagdollController` uses the same `Classify`/`Blow`.

**Voices** (`EnemyVoice`, `VoiceBank`). Recordings, all CC0 from OpenGameArt: "Male
Grunt/Yelling sounds" (HaelDB), "grunts of male death and pain" (thebardofblasphemy, split
into segments), "80 CC0 creature SFX" (rubberduck, played at `VoiceBank.Set.pitch` 0.72).
A formant synth was tried first and rejected. Files are `<human|creature>_<slot>_NN.wav|ogg`
in `Assets/Audio/SFX/Voice`; `FPSKitGore.GetOrCreateVoiceBank` finds them by name.
Gunfire is recorded too (`Assets/Audio/SFX/Recorded`, The Free Firearm Sound Library, CC0):
AR-15 for rifles, Mossberg for shotguns, PPSh for SMGs (picked by pellets / rpm in
`StampWeaponFeedback`), AK-47 for enemies. The voice has
its own AudioSource: a per-enemy pitch on the shared one bent the gunshots.

**The Asset Store model** goes through Enemy Setup as before. `BuildFromTheme` now keeps a
prefab with an Animator and only upgrades it (adding wounds, blood, voice); rebuilding the
built soldier over an imported character is what it used to do.

**Every enemy is a person (2026-09-26, later the same day).** Ishaan heard the creature
growls as "evil voices and disturbing noise" and asked for every enemy to be a human soldier
in voice and look: every archetype is `EnemyVoice.Kind.Human`, the creature clips and the
moaning hunt clips are gone (hunting is the alert shouts, rarely), and the Creature slot
of the bank plays the same people at 0.9 pitch. The Kit_Creature parts remain in the prefab
but nothing wears them. Enemies are built at 1.1 scale.

**The player's hands, reload and strike.** Hands are lofted (`BuildGunHands`, `HandPart`):
a glove closed round the grip, a cupped hand under the handguard, forearms as thick as a
sleeved arm and long enough that their ends never swing into frame. The reload is drawn
in `Weapon.ReloadPose` (turn to show the side, magazine out, left hand down for it, fresh
one up and seated, bolt kick), with three recorded clips on `WeaponData` placed on the same
beats (`MagOutAt`, `MagInAt`, `BoltAt`) by `ReloadRoutine`. The punch is a rifle-butt strike
(his choice): `MeleeStrike.AnimateButt` turns and drives the rifle through
`Weapon.HandsRotation`/`HandsOffset`; the forearms counter-rotate at the wrist
(`Weapon._forearms`) so they keep running back to the shoulders while the gun turns. See
it with `UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViewModel`, which pins
`Time.captureDeltaTime` -- a batch frame can be longer than the whole strike.

Checks: `VerifyWounds` (the rules above, in an empty scene). Pictures:
`UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureEnemies -fpskitOut Build/Enemies`.
Enemy-only rebuild: `FPSKitBatch.RebuildEnemy` (scenes hold the prefab by GUID).

## Coins are earned in a level and spent between them

`Wallet` is the only thing that moves coins, and `Wallet.TrySpend` is the only thing
that can lower the balance -- it refuses rather than going negative. Every purchase in
`StorePanel` is the same three steps in the same order: refuse if it is not for sale,
take the money, *then* record it in `Loadout`. Granting first and charging afterwards is
how a shop hands out an item to somebody who could not pay for it.

Coins accrue on `GameDirector` during a level and are banked once, in
`GameSession.RecordResult`. That is deliberate on both ends:

- **Not per kill.** Banking as you go would let a player farm a level to the last enemy,
  quit, and repeat. The clock exists so a level has an ending, and the payout is part of
  the ending.
- **Every ending pays.** Cleared, timed out, died and walked out all route through
  `RecordResult`, so there is one line that can pay and no way for a new ending to be
  added later that silently pays nothing. An abandoned level pays for the kills only --
  no star bonus, no score bonus.

A headshot is worth extra coins as well as extra score, and coins are **not** multiplied
by the combo. The combo is the scoreboard's reward for pushing; making it the wallet's
as well would mean a good chain is worth several minutes of ordinary play, and every
price in the store would have to be written for the chain rather than for the game.

The prices in `FPSKitStore.Configure` and the rates on `GameDirector` are one pair. At
three coins a kill, sixty a star and a point-based bonus, a well-played level pays
roughly three hundred and the whole catalogue is near fifty of them. Moving either half
without the other is what turns a shop into a grind or a free lunch.

**`LevelResult` is a struct, so everything downstream gets a copy.** The level-end coin
bonus is therefore awarded by `LevelManager` *before* it builds the result --
`GameDirector.AwardLevelCoins` exists only so that it can be. Written the obvious way,
with `ReportLevelFinished` filling the coins into the result it was handed, the wallet
and the dashboard were paid correctly while the results screen -- which is handed the
*manager's* copy through the `LevelFinished` event -- showed nothing at all. Anything
that has to appear in a result must be in it before it is passed anywhere.

## Upgrades escalate, and one of them never stops

`StoreCatalog.UpgradeCurve` is `base * growth^level`, held on the entry rather than in
code, so nothing upgradeable can quietly invent its own escalation. Guns and bombs cap
at five levels; **health is uncapped on purpose** and is the one thing a stuck player can
always put coins into, so being stuck on a level is never a wall with nothing to do about
it. The 1.45 growth is what stops that being a free pass.

Nothing bought is ever written into a `WeaponData` or a `BombData`. Those are single
shared ScriptableObjects, so a bonus stored there would survive quitting the game and
compound on every restart -- the same rule `PlayerProgression` has always followed. A
gun's upgrades become multipliers on the `Weapon` component; a bomb's become a
`BlastSpec` the thrower resolves on the way out.

`PlayerLoadout` is the seam. It reads `Loadout`, equips the gun, arms the bomb, fills the
belt, resizes the health pool, and then calls `PlayerProgression.Apply`, which multiplies
the *bought* upgrades by the *level* curve. Both calls are absolute and idempotent,
because which of the two `Start` methods Unity runs first is not defined.

## The bomb lands where the ring says, in the time the arc was drawn for

`BombThrower` puts a ring on the ground at the blast radius and a dotted arc to it, and
the throw is solved to arrive in exactly the time `BombData.FlightTimeFor` gives for that
distance. Five things make that a promise rather than a hope:

- **The bomb flies its own parabola.** `v = (target - p0)/t - ½gt²`, integrated by hand
  and swept against the world between frames. A Rigidbody clipping a crate would land
  somewhere else and blame the physics engine.
- **`BombAimIndicator` draws the same equation**, so the dotted line cannot disagree with
  where the bomb actually goes. It is handed the flight time rather than reading it, and
  so is `BombProjectile.Launch`, because the thrower is the one that chose it.
- **`BombThrower.Throw` clamps into range**, including for a caller that hands in a point
  of its own. Measured from the *player*, which is the frame the ring and the HUD readout
  use -- clamping against the throw origin instead is clamping against the muzzle, the
  better part of a metre further forward, which moved a landing point the ring had
  already placed exactly on the limit.
- **The flight time scales with the distance.** `fallTime` is the far end, `minFallTime`
  the near one. A flat second for every throw meant a six-metre lob -- the panic throw,
  the one you make with something already in your face -- spent as long in the air as one
  sent across the whole arena.
- **Gravity is the bomb's own**, several times the world's -- `BombData.gravityScale`.
  The horizontal speed is fixed by the distance and the time, so the only thing left to
  choose is height, and under real gravity a one-second throw peaks about a metre up and
  flies flat into the first crate in the way. This was not a theory: the store test found
  it, with a thirty-metre throw that hurt nobody.

**Tap the key, do not hold it -- and the reason is not preference.** A laptop touchpad
stops reporting motion while a key is held. That is libinput's *disable-while-typing*,
it is on by default on GNOME and most desktops, and it applies system wide. So
"hold this key and move the pointer" is a gesture the majority of laptop players cannot
physically perform: the key goes down, the touchpad goes dead, and the game looks
frozen. Nothing in the game can detect it -- Unity is simply handed no mouse motion, and
every reading inside the process stays perfectly healthy while the player sees a dead
mouse. This cost six rounds of debugging to find, in the game, where it was never going
to be.

So `BombThrower.PumpAim` accepts both. Tap the key and the ring latches open with
nothing held down; tap again to throw. Hold it and releasing throws, as before.
`tapToLatch` is the window that separates them. `VerifyBomb` drives `PumpAim` directly
rather than going through `Update`, because batch mode cannot synthesise a legacy key
press and a rule about *when* a key was released is exactly the kind written once and
never exercised again. Any new hold-to-do-something control needs the same treatment.

**The mouse moves a cursor, not the player's head.** Holding the key hands the look to
`BombThrower` through `PlayerMotor.LookCaptured`: the motor still measures the mouse and
publishes `LookDeltaDegrees`, but stops applying it to the view, and the thrower spends
those same degrees moving a reticle across the screen instead. The bomb goes where the
reticle points -- `ResolveLanding` traces `ScreenPointToRay(AimScreenPoint)` rather than
the camera's forward -- and pushing the reticle into the edge of the screen turns the
view by the leftover, so nothing is out of reach.

Borrowing the look rather than reading the mouse directly is what keeps this component
ignorant of which input backend is live, keeps one sensitivity setting governing both
the head and the cursor, and makes the cursor work for a player on the keyboard look
keys. Degrees become pixels through the camera's own field of view, read every frame
because aiming down sights changes it, so the cursor lands on whatever the crosshair
would have been pointing at had the view turned instead.

**Aiming it by turning your whole body reads as the mouse having stopped working.** That
was the original build and it is worth writing down, because every measurement of it
came back healthy: the view turned, the ring followed, the geometry was exact, and the
player's report was "I can't move my mouse to place the bomb". The control was doing
precisely what it was written to do and that was the defect. A thrown explosive wants
"put it there", not "face it".

**The pitch mapping is the touch path.** A phone has no pointer to move, so
`UsingCursor` is false whenever `MobileInput.Active` is, and the ring's distance comes
from how far below the horizon the player is looking. `RangeFromPitch` maps that evenly.
It must not go back to tracing the camera forward onto the floor, which is the obvious
build and is unusable: the eye is a metre and a half up, so the distance goes as
`height / tan(pitch)`, nearly vertical at the horizon. On a flat arena the entire 6--34m
range lived inside about twelve degrees of pitch, no pitch produced fifteen metres, and
every degree *above* the horizon gave the same maximum. `VerifyBomb` sweeps a degree at
a time with the cursor switched off and fails if one degree moves the ring more than two
metres, if either end is unreachable, or if nothing lands in the middle -- that last one
matters, because a ring frozen at maximum range is perfectly smooth.

Nothing shortens the throw for a wall in between, deliberately. The high gravity exists
so a throw lobs *over* cover, and the dotted arc already shows a player the line going
into anything it genuinely cannot clear. A wall test on the straight eye ray cut a
thirty-four metre throw to seventeen over a chest-high crate the bomb would have sailed
past -- and did it on the one degree of pitch where the ray stopped clearing the crate's
top edge, which is the same cliff in a new place.

`VerifyBomb` covers the cursor on both counts, because they break separately: that
cursor-left puts the bomb left and cursor-low brings it in, and that the view does **not**
drift while the reticle moves inside the screen. The second is what makes it a cursor
rather than a slower way of turning your head, and it is what silently comes back if
`LookCaptured` is ever dropped. Two traps in writing that test: `TurnBy` writes the
motor's own yaw field and the transform is not touched until `HandleLook` runs, so an
edge-push reading taken in the same tick that asked for the turn sees nothing; and a big
shove turns the view several hundred degrees, which `Mathf.DeltaAngle` wraps back to
almost zero and reports as a dead edge push.

**Half of `VerifyBomb` drives the private methods and half holds the key for real.** The
first half is precise and proves the geometry; it proves nothing about the game, because
it never runs `Update`. The second sets `MobileInput.BombAim`, which `BombThrower.Update`
reads as an OR beside the `G` binding, so `BeginAiming`, `UpdateAim` and the lock toggle
all run once a frame exactly as a held key makes them -- with `PlayerMotor` turning the
view in between. That is the only way to catch something *outside* the component eating
the look while the ring is up, which is what "I hold the bomb key and the mouse stops"
describes. The look is fed through `MobileInput.AddLook` rather than the mouse because
batch mode will not grant a cursor lock and `PlayerMotor` reads no mouse without one;
both paths scale by the same `LookSensitivityMultiplier`, which is the thing that can be
wrong. Note that the rig splits the two angles -- `PlayerMotor` writes pitch to the camera
*holder* -- so a test that sets the camera's own local rotation is adding its angle to
whatever the player's look left on the holder.

**All three of the bomb's sounds have to reach the player.** The pin (`BombData.armClip`)
plays when the ring comes up, because holding the key was otherwise the one control in
the game that made no sound, and what it draws is a ring on the floor that somebody
looking at an enemy never sees. The blast's audible reach is `BombData.maxRange`, not its
damage radius: the radius says how far the bomb *hurts*, and the player is never inside
their own blast -- they are out at the distance they threw it from. Seven metres of radius
bought ten metres of full volume on a bomb thrown up to thirty-four, so the ordinary
throw arrived quieter than a footstep.

`Explosion.Blast` is a static that needs no prefab, so a bomb with nothing wired still
does its damage. It damages one `Health` at most once however many colliders it has --
an enemy is a body, a head and a pair of limbs, and damaging per collider would make a
bomb four times stronger against a target with hitboxes.

Self-damage is a fraction on the bomb, not a layer excluded from the sphere. Excluding
the player would make the ring a lie about one of the things standing in it.

## The rifle has a curve too

`PlayerProgression` widens the magazine, hardens the round and shortens the reload for
every level below the one being played. It exists because the level curve does: the same
thirty rounds against twice the bodies turns a difficulty curve into a slope the player
slides down.

Every upgrade is a multiplier held on the `Weapon` **component** -- `MagazineBonus`,
`DamageMultiplier`, `ReloadTimeMultiplier`, read back through `MagazineSize`,
`ReloadTime` and `DamageAtDistance`. Nothing is ever written to `WeaponData`. That is
not tidiness: a `WeaponData` is one shared ScriptableObject, so a bonus written into it
would edit `TestRifle.asset` on disk, and the next run -- in a fresh session, after a
restart -- would start already upgraded and compound from there. If you add an upgrade,
add it the same way.

`PlayerProgression` reads `LevelManager.LevelNumber` rather than counting anything. A
level is one scene load, so there is no run-long tally to keep: entering level 6 hands
you the level-6 rifle whether you got there by clearing level 5 or by picking it off the
ladder, and `LevelManager` restarting its own loop after a mid-play recompile cannot pay
out twice.

## A level ends on the last kill or on the clock

Both, and the clock is checked without exception. This must not be "simplified" into
waiting for the arena to empty: the kit is meant to run in imported levels, and in a
real level an enemy that falls through a gap stays alive forever, so a completion
requirement is a guaranteed softlock.

Two independent safety nets, both in `LevelManager`:

- **The leash** (`SweepEnemies` / `IsLost`) discards an enemy that is too far, has
  fallen too far below the player, whose agent has left the NavMesh, or that has no
  route to the player at all -- see *Baked is not the same question as reachable*. Discarding
  deliberately awards no score, no combo and no drop — it is not a kill. It *does* put
  a fresh enemy in the queue, bounded at one replacement per enemy in the level, which
  is new and is the point: a level asks for a fixed number of kills and scores the
  player against it, so a body that fell down a hole is not theirs to pay for.
- **The clock** (`RunLevel`) ends the level regardless of survivors and scores it on
  what was actually killed.

`despawnDistance` is force-raised at `Start` if it is not comfortably clear of
`maxSpawnDistanceFromPlayer`, because a leash inside the spawn ring deletes enemies on
arrival and presents as "nothing spawns".

**Spawn points are placed geometrically and then snapped onto the navmesh.** Those are
two different questions, and the open zone is where they come apart:
`BuildOpenZoneSpawnPoints` shoves a point clear of the river and then clamps it back
inside the arena, and the clamp can put it straight back over the water, because the
river meanders and the shove was measured at one `z`. What that costs is invisible --
the level spawns from the ring it was given, an agent placed off the mesh is discarded
by the leash on arrival, and the level simply has one fewer direction to arrive from,
with nothing logged. `FPSKitSceneBuilder.SnapSpawnPointsToNavMesh` runs after
`BakeNavMesh`, which is the first moment the answer exists at all, widening its search
(6m, then 18m, then 45m) and warning about anything it still cannot place. `VerifyZone`
is the regression test.

## Stars are weighted, and the boss is most of the weight

`LevelResult.StarsFor` cuts a fraction into stars, and the fraction is
`killedWeight / totalWeight` where an ordinary enemy is worth 1 and the boss is worth
`LevelSet.Level.bossWeight` — 5 and up. Clearing the escort of a boss level and leaving
the boss standing is therefore about 0.7, which is one star. That is deliberate: the
boss *is* the level.

Three stars is the one result a fraction cannot buy. It is awarded only for `cleared`,
meaning `Killed >= TotalEnemies`, because "kill them all" has to mean all of them or the
top of the scoreboard is somewhere a player can stumble into.

Two other rules hold the ladder together:

- **`LevelProgress.Record` keeps the best, never the latest.** Replaying a level already
  three-starred must not be able to take stars away, or practising is a punishment. An
  abandoned level records a zero for exactly this reason: it is safe.
- **Unlocking is one line and lives in `LevelProgress.IsUnlocked`.** Level 0 is always
  open; every level after it needs a star on the one before. Nothing else may decide it —
  the menu, the results screen and `LevelManager` all ask that method.
- **A level with stars on it is open whatever is below it.** Saves played with the editor's
  **FPSKit > Debug > Unlock All** switched on -- Ishaan's was -- have stars scattered up a
  ladder with gaps under them, and locking a level somebody already beat takes back what
  they earned. The same rule holds for zones. And when somebody reports "everything is
  unlocked", check that switch before the code: it is an EditorPrefs toggle, it survives
  restarts, and it bypasses both gates.

**The level select states each star's rule, and the results screen marks it MET or MISSED.**
`Missions.StarConditions` writes the three from the level's own `oneStarScore`,
`twoStarScore` and objective, so the card that promises a star and the screen that awards it
read one method. One and two stars are a share of the level's *weight* (the boss and a task
objective count for more than one enemy, which `Missions.WeightNote` spells out); three is
every enemy, the boss and the task before the clock. There is **no par time**: the seconds on
a card are the clock that ends the level, labelled TIME LIMIT, because a bare "32s" was read
as a target. Best clear time is stored beside best score (`LevelProgress.BestTimeIn`), only
for a clear, so saves from before it have scores and no times -- the card shows what exists.

**The results screen is `ResultsView`, built at runtime from the kit.** `LevelResultsUI` still
owns the keys, the audio, the stars landing in turn and where each button goes, and points
its fields at the view in `Awake`; the builder-made panel is still in the seven arena scenes
and stays hidden. Built at runtime for the reason Loadout and Info are: the screen is the same
everywhere and a builder edit would mean rebuilding every arena. It has **its own canvas at
sort order 500**, because the touch layer is a separate canvas above the HUD and drew the move
stick and fire button over the card. XP for the level is counted from the result -- kills,
stars above the previous best, the boss, new achievements -- rather than as XP after minus XP
before, because `PlayerStats`' star total is only recomputed on the dashboard.

**TMP with `Ellipsis` draws nothing when one line does not fit the height.** A row authored at
20 units rendered its text on a monitor and was blank on a phone, where the text floor makes
the line taller -- the star conditions on the level cards simply vanished, with the star icons
beside them still drawn. Give such rows a `minHeight` and let the layout grow them.

Progress is keyed by `ArenaCatalog.Entry.ProgressKey`, which is the `LevelSet`'s
`arenaScene`, **not** the scene name. A WebGL build stages every arena as a renamed copy
so the touch layer can be baked in, so keying on the live scene would file a browser
player's stars under a different arena from everyone else's.

## Every screen spells a list the same way

`UIText` owns it: values joined by `UIText.Separator`, a value written as a number then a
label in capitals, and a unit lowercase and attached to its number (`38s`, `7m`) because
it is part of the number rather than a word of its own. `UIText.Row` skips empty values,
which is what lets a caller pass something it may not have -- a drink with no shield
component, a level with no boss -- without building the string conditionally and getting
the separators wrong at the seams.

It exists because six labels built the same kind of row and between them used two
spacings, two casings and three ways of writing a count, so an arena card, a level tile
and a store card read as three different games. None of that is a bug and all of it is
noticeable. Same reasoning as `Wallet.Format`, which is why every screen spells a coin
balance the same way.

A store card shows the value a purchase would give the player **now**, not the running
total of what they have already bought. The health card showed the total and so opened
on `+0 HP   ·   +0 SHIELD` for anybody who had never bought one -- true, and useless,
because the card is read to find out what pressing the button does.

## A device is three facts, not one bool

`DeviceProfile` answers what the game is being played on: a **reach** (`Pointer` or
`Touch`), a **form** (`Handset`, `Tablet`, `Laptop`, `Desktop`, measured in millimetres),
and an orientation. Three axes because real hardware varies them independently -- a
touchscreen laptop is a pointer device in practice, and a 12.9in iPad is a *laptop-sized
surface reached by a finger*, which is the true answer and the useful one: laptop density,
thumb-sized targets. No single axis can say that.

**What it replaced was one bool and a scale factor**, and that failed twice over.

`PhoneUI.Active` asked `TouchMetrics` for the screen's density -- but that property exists
to size a thumb target, so when the platform tells it nothing it substitutes *a typical
phone*, 400 dpi. The question answered itself. Every ordinary monitor reports 96 dpi and
every 1440p one 109, both under the 120 dpi floor the touch layer clamps to, so both got
the substitution and a 1920px monitor measured **122mm across**. Desktop players lost the
arena card descriptions, lost the career panel entirely, and saw the menus and HUD at
1.43x. The one machine it passed on was a high-dpi laptop, whose density is inside the
handheld range and so escapes the substitution -- which is why it survived being looked at.

And a phone was getting the desktop's layout magnified, with `HideOnPhone` deleting
whatever then overflowed. **That is subtraction, not design.** What breaks a six-card grid
on a handset is that it is a six-card grid, not that the cards are the wrong size --
scaling preserves arrangement and density, which is exactly what has to change. The
achievements and instructions screens take their column count from the form: four on a
monitor, two on a tablet, one on a handset, with the category headings moving into the row
stream when columns are shared, because a heading pinned above a column that now holds two
categories labels only the first.

**A handset gets its own arrangement, not this one rearranged.** `MainMenuController.ApplyFormLayout`
is where the dashboard becomes a phone screen: the five-row career panel goes (its one
figure a player checks before the store, the balance, is already in the header), the arena
grid takes the third of the width that frees, the title bar and the heading are compressed
from about three hundred reference units of chrome to a hundred and seventy, and the button
row grows until each button clears the nine millimetres below which a thumb starts missing.
Every one of those is an anchor, so a tablet and a desktop get exactly what the builder
authored and the method does nothing at all. `PhoneUI` still shrinks the canvas reference
on top -- 0.56 for a menu, 0.80 for the HUD -- but only as a **floor under the type**: it is
what lifts body text on a 155mm screen off 1.8mm, and on its own it is the magnified desktop
that was rejected.

**The HUD is scaled less than the menus, deliberately.** It is glanced at rather than read,
its largest elements (the minimap, the ammo count) are already big enough, and everything
else on that canvas is the touch layer, which sizes itself in millimetres and does not move
when the reference does. Growing it as far as the menus spends the middle of a phone screen
on furniture during a fight.

**`UIGrid` is the one grid fitter.** The arena grid, the level select and the store each
had the same twenty lines of measure-and-divide, and only one of the three had learned to
*choose* its column count -- so the other two stayed fixed at a number that is right on
16:9 and wasteful on a landscape phone, where the height sets the cell and the width
becomes margin. It measures every arrangement and takes the largest card, with a per-screen
ceiling for the cases where the arithmetic wins and the argument loses: a store card
carries three statistics, a sentence and a price, so past two columns on a handset it is a
card nobody can read whatever its area.

`FPSKitDeviceTest` (`FPSKitBatch.VerifyDevices`) pins fourteen screen classes. It drives
`DeviceProfile.FormFor` and `ReachFor` with the readings handed in, because batch mode has
one screen and a check written against the live properties would assert whatever the build
machine is and pass identically with every rule deleted -- the same reason
`ControlSettings.CanRead` is public. It is cheap: no scene, no play session.

**The game is landscape-only on every device.** `ProjectSettings` allows only
`LandscapeLeft`/`LandscapeRight` and the WebGL template calls `screen.orientation.lock`.
So a handset here is a wide, short surface with thumbs at the **left and right edges** --
a bottom bar spends the scarcest dimension and sits outside the thumb arc.

## In a browser, `Screen.dpi` is not a measurement of anything

Unity's WebGL runtime answers `Screen.dpi` with **96 times the pixel ratio the page
configured** -- not the panel's density, and not the backbuffer's either once the page and
the panel disagree. Everything physical in this game is converted through a density, so one
bad reading came out as three separate player-visible faults, each of which looked like a
different feature being broken:

- the on-screen controls were sized against the substituted 400 dpi and came out **three
  times too large**, with FIRE covering the middle of the screen;
- the look correction divided by that same 400 against a real 150, so the view turned **a
  third as far** as the thumb asked and the report was "I have to swipe and swipe to see
  behind me";
- and `DeviceProfile` took 96 at face value, measured a **152mm handset as 242mm**, called
  it a tablet, and handed a phone two columns of achievements and the desktop's card grid.

`WebDevice.FramebufferDpi` is the answer, and it asks the page. A browser does know one
thing exactly: how big a CSS pixel is meant to be. That is not a fixed length but it is a
fixed *intent* -- mobile browsers choose the ideal viewport so that text at 16px is readable
in the hand, which puts every phone and tablet near **150 CSS dpi** and a desktop at the
spec's **96**. Against real hardware that is within a few millimetres: a 914 CSS pixel
landscape phone measures 155mm and is truly 152mm. Multiplying by the ratio the page renders
at turns it into the framebuffer's own density, which is the number every millimetre and
every swipe is converted with.

**`TouchMetrics.ScreenDpi` is the only place that reads a screen.** `DeviceProfile` asks it
too. Two consumers measuring the same glass separately is how one platform quirk produced
two opposite wrong answers.

**The page's sharpness must change nothing the player can feel.** It renders a touch device
at `min(devicePixelRatio, 2, 1600 / cssLongEdge)` -- about 1.75 on a modern phone, three
times the pixels of the ratio 1 that shipped, which is what "the game looks very blurry"
was: roughly 900 pixels drawn and stretched across a 2400 pixel panel. Because the same
ratio feeds the density above, a control keeps its physical size and a swipe keeps its
degrees; it only gains resolution. `VerifyDevices` asserts exactly that invariant at ratios
1, 1.75, 2 and 3, and asserts the absolute figure as well -- a 40mm swipe is worth 630
reference pixels on every device there has ever been, and the shipped code was
self-consistently 2.6x short of it.

**Quality is three tiers, chosen once and then the player's.** `FPSKitQualityTiers` makes
the levels Low (the old Mobile asset: no real-time shadows, no MSAA, no HDR), Medium (made from
the PC asset: 60m shadows, 2x MSAA, the mobile renderer so no occlusion) and High (the PC asset
untouched), with no tier excluded from any platform -- the template excluded PC from Android
and iOS, which removes it from `QualitySettings.names` there. `QualityTiers` picks Low for a
phone, a tablet or a browser and High for a desktop on the first launch, writes it to
`GameSettings.QualityTier`, and never overrides the player afterwards. Post-processing and
particles are per scene and cut on Low at runtime; fog is deliberately left alone on every
tier, because it hides the far clip plane on the big arenas and costs almost nothing. A phone
runs at the player's 30 or 60 with `DynamicResolution` trading sharpness for frame time; a
desktop uses vsync; a browser is paced by the page. **Low's render scale is 1.0**: the pixel
budget lives in the page (see above), and a low tier at 0.8 is the "very blurry" build again.
None of it runs in the editor, where `SetQualityLevel` writes the project's level to disk.

## The HUD is `HudView`, built at runtime and driven by events

`HUDController` creates it in `Awake` (`useKitHud`) and hands over: the mission panel and
objective strip (top centre), the run panel (top right; beside the minimap on a touch screen),
the kill feed (under the minimap), the player card (bottom left), the ability slots (bottom
centre) and the weapon panel (bottom right; in the bottom-centre row on a touch screen), the
hit marker, the flat pause card and the paused icon row. `HUDController` keeps the crosshair,
briefing, banner, boss bar, bomb cursor, damage indicators, vignette, and the logic of pausing
-- its `pausePanel`, `resumeButton`, `quitButton` and `pauseHintText` are pointed at the kit
card. The builder-made readouts are still in the seven arena scenes, hidden, and
`legacyReadouts` stops them updating. Built at runtime for the reason the results screen is:
the same in every arena and in a level the kit did not build, with no rebuild of seven scenes.

**Nothing in it polls.** Each readout is written when its source raises: `Health.Changed` and
`Healed`, `Weapon.AmmoChanged` and the reload events, `GameDirector.ScoreChanged`,
`CoinsChanged` and `KillRegistered` (the kill feed's who, how and boss), `LevelManager.
ProgressChanged`, `ObjectiveChanged` and `ClockTicked` (compared once, in the manager, so the
line's moving distance is not re-laid out by every reader), `BombThrower.ChargesChanged` and
`ConsumableBelt.Changed`. Two traps it hit:

- **A view that subscribes in `OnEnable` and is configured after `AddComponent` subscribes to
  nothing.** `OnEnable` runs inside `AddComponent`. The panels still showed the right numbers
  once, from `Start`, and then never moved -- found only because the pause icons never came up.
  Subscribe at the end of the build, and from `OnEnable` only once built.
- **`UIProgressBar` never laid out a bar that started at zero.** It snapped in `OnEnable`, before
  `UIKit` had assigned its fill, so a bar at 0 matched its target and drew the fill as Unity's
  default 100x100 rectangle -- a solid block of amber over the run panel. It lays out on its
  first `Update` now.

Achievements only move in the save when a level is scored, so a toast mid-level is the
lifetime counter plus this run's live delta (kills, headshots, bomb kills, bosses, chain,
coins), at each quarter of a target and at the finish -- not per kill. The ability slots are the
bomb and the belt's one drink: the game has no flash, and a slot for an item that does not
exist would be a control that does nothing. On a touch screen a slot shows no key -- the prompt
there is the on-screen button's icon, which under the slot's own icon read as the icon twice.

`CaptureDevices` stages a real fight for `*_hud_combat` (two enemies killed through their own
`Health`, the player hurt, healed and re-armoured) and shoots it `After` 0.45s, while the
pop-ups and feed rows are still up; the results shots hand a made-up result straight to the
screen, never to `GameSession`, so a capture never writes the save of whoever ran it.

## The player can move the HUD, and a layout is anchored to edges

`HudLayout` is the player's layout: per element an anchor (0, 0.5 or 1 on each axis, chosen by
which third of the screen its centre is in), an offset from it, a scale (60-150%), an opacity
(20-100%) and hidden; plus the crosshair (cross/dot/circle, colour, size, thickness, gap,
outline) and a phone's second fire button. **Kept per device form** (handset, tablet, desktop)
as JSON under `settings.hud.<form>`, with three named custom layouts beside it; Settings'
reset clears the layouts and keeps the named ones.

Every movable element carries a `HudLayoutTarget`. **The owner places it and calls `Settle()`**,
which records that placement as the default and re-applies the entry on top -- `HudView` after
it builds, `TouchCluster` after every `Rebuild` (it re-lays whenever the canvas rescales or
equipment arrives), `TouchLayout` for the joystick zone and pause after mirroring. Applied once,
an entry would be undone by the next owner layout. Three traps it hit:

- **Mirroring for a left-handed player moves settled elements.** `MirrorHudFooter` and `Flip`
  drop an element back to its owner's placement, mirror that, and `Settle` again; judging
  "is this in a bottom corner" from a layout's point anchors mirrored the wrong things.
- **The player's scale is captured once**, because owners re-lay position and size but never
  scale -- re-capturing it compounded the player's scale on every rebuild.
- **The joystick zone is a zone, not a panel** (`HudLayoutTarget.zone`): it lies under
  everything by design and never counts as an overlap, or every panel on its half is red.

`HudEditor` (Settings > HUD > Customize layout, from the menu or the pause menu) is an
`OverlayPanel` over the frozen arena: a grid, the safe area, a red crosshair keep-out, an
outline per element (amber selected, red where two visible ones overlap), and a side panel
that sits on the side away from the selection and only as tall as its content, so the corners
stay grabbable. It edits a copy and previews live; Cancel (and Escape/B, which come through
the base `Close`) reverts in `OnClosed`, Save keeps. Pad: D-pad selects, left stick moves,
triggers size, right stick fades, Y hides, X resets, Start saves -- with the EventSystem's
navigation off while it is open, or the D-pad would also walk the side panel. From the menu it
loads the selected arena frozen (`GameSession.EditingHud`), and leaving records no run.

Two fire buttons share `MobileInput.PressFire`, a hold count: with one bool, lifting either
thumb stopped the gun while the other was still pressing. `VerifyHudLayout` is the regression
test (moved, sized, faded, hidden, crosshair restyled, preview cancelled, kept across a second
play session, two fire buttons).

## Store, achievements and loadout

**The store is five shelves on a left rail**: FEATURED (the cheapest things not yet owned, read
from the catalog), WEAPONS, CHARACTERS (empty, and says so: there are none), GEAR (the bombs and
Vitality -- `Tab.Bombs` and `Tab.Health` both open it) and CONSUMABLES. `StorePanel`'s purchase
logic is unchanged -- refuse, take the money, grant -- and **every spend now asks first**
(`Ask`, a confirm dialog built at runtime; `VerifyStore` confirms through its BUY button).
`Purchased` refreshes the top bar's coins at once. A price the player cannot pay is red with its
button off. `StoreAlerts` puts the amber dot on STORE for an affordable item not yet seen, kept
as a set of ids so it survives a restart and clears on opening.

**The card pictures are renders of per-item models built by `FPSKitItemRenders`** (FPSKit >
Render Store Items, `UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.RenderStoreItems`),
from primitives in the style of the first-person gun, onto a transparent background, into
`ItemRenders.asset`. **Every gun in the game uses one shared viewmodel**, so rendering "the"
model would have been six identical pictures; the renders are the only place the guns look
different. Framed by the model's projected width and height, not a bounding sphere, which left
a long gun a thin line in an empty card.

**Achievements pay coins on CLAIM** (`Achievements.CoinReward`, 150 for a first tier to 1,500
for every star); the XP is automatic because rank reads it off the earned count. Earned is
still derived; whether it was claimed is stored, like a beat read. `AchievementsPanel` builds
its kit content at runtime; the builder makes only the screen. **A `SafeAreaFitter` baked into
a menu screen by the builder takes the whole canvas out of the safe area**: `SafeAreaCanvas`
skips any canvas that already holds one when it wakes, so the top bar and rail went to the
physical edges -- add screen-level fitters at runtime.

**Loadout is four slots** -- PRIMARY, SECONDARY (empty: the game carries one gun; Ishaan chose
to show it), GRENADE, CONSUMABLE -- each with the item's render; a slot opens what the player
owns for it. Choices go straight to `Loadout`, which the dashboard strip and the next level read.

**Rasterised icons need the theme's list refreshed.** `Tools/rasterize-icons.py` writes PNGs;
`UITheme.IconSprite` finds an icon by id in the theme asset's own list, so a new icon draws
nothing until `FPSKitBatch.ImportUiKit` runs. The menu item behind it never exits the editor --
called directly with `-executeMethod` it idles forever.

## A kit control that wires itself in Awake does nothing when built at runtime

`UIKit` adds a component and then assigns its parts, and `Awake` runs inside `AddComponent` --
so `UITabBar` and `UIChoice`, which hooked their buttons in `Awake`, found empty lists on every
screen built at runtime and hooked nothing. Settings' Video, Audio and HUD tabs, the HUD
editor's tabs, the achievement filters and every Settings chooser's arrows did nothing when
clicked, while the scene-built top bar (whose `Awake` ran with its tabs already assigned) worked
-- and every check passed, because they all drove the panels through `ShowTab`. Both now have
an idempotent `Wire()`, called from `Awake`, `OnEnable`, `Start` and by `UIKit` straight after
assigning. **Any new kit control that listens to its own children follows the same rule.**

`VerifyHudLayout` is the regression test: it clicks every tab and a chooser's arrows through
their own `onClick`, and then opens each runtime-built screen for real -- every Settings tab,
the pause menu, the quit confirmation, the HUD editor, Achievements -- and fires a raycast at
every visible control through every raycaster, failing on anything a click would not reach.
It was checked against the old code and fails there, naming the dead tabs.

## Settings, ammunition and the pause menu

**Settings** (the gear in the menus and the pause menu) is Controls, Touch (touch devices),
Video, Audio and HUD, each a `GameSettings` accessor raising `Changed`, with Reset to defaults
clearing the lot -- the HUD layouts and the player's keys included, the named custom layouts
not. Where each lands: mouse sensitivity in `PlayerMotor`, FOV in `Weapon` (read live; the
sights still zoom from it), vsync in `QualityTiers.ApplyFrameRate`, HUD scale as a multiplier
in `HudLayoutTarget` for `hud.*` only (touch buttons size in millimetres), minimap rotation in
`HudView`, the crosshair in `HudLayout` (one source for Settings and the editor). Resolution,
fullscreen and vsync are only offered on a desktop -- a phone has one resolution and a browser's
is the page's.

**Audio has no mixer, and does not need one.** Unity cannot create an AudioMixer from code, so
`SettingsApplier` puts master times effects on `AudioListener.volume`, and the menu's music
source sets `ignoreListenerVolume` and applies master times music itself (`UISounds`). Exact,
and nothing per source to keep in step.

**Keys are rebound over the asset, not in it** (`KeyBindings`): overrides in PlayerPrefs are
written onto every live `ControlSettings` it has been handed, the authored keys are remembered
and put back when play stops, and `Revision` is bumped so `GameInput.Bind` rewrites the actions
-- which it only does at spawn, so a rebind rebinds the live motor itself. Rebinding a key
another action holds swaps them. While a row is listening it owns Escape, or Escape would both
cancel the listen and close Settings.

**The reserve is finite, per level.** `LevelSet.Level.reserveMagazines` (8; One Magazine 0; -1
unlimited) and `ammoDropChance` (0.12 on every kill, on top of `EnemyArchetype.ammoDropChance`
and the objective's bonus) are the knobs, written by `FPSKitLevels.Configure` and applied by
`LevelManager.ArmReserve` at the start of the fight -- before the objective begins, so One
Magazine's empty reserve is the last word. The limit is a runtime flag on `Weapon`
(`reserveLimited`, `InfiniteReserve`), never written into `WeaponData`. One Magazine now allows
reloading: it starts with nothing spare, and kills are the only resupply. Pickup size is
`Pickup.ammoAmount` (90, in the scene builder) and the carry cap `WeaponData.maxReserveAmmo`.

## The punch, and the hands that throw it

Added 2026-09-25 at Ishaan's request, for the level with no spare rounds: up close, the
player chooses between spending a bullet and a fist. `MeleeStrike` on the player root.

- **Damage is a share of the target's own pool**, not a number, so it means the same on
  level one and level eight after the level curve has scaled health. An ordinary enemy goes
  down in one; an elite loses `eliteShare` (0.45) of health plus armour; a boss only
  `bossShare` (0.08), or a boss fight becomes walking up to it. Role comes from
  `EnemyAI.archetype.role`. Nothing is written into `EnemyArchetype`.
- **Reach is found, not aimed**: nearest the crosshair within `reach` (2.3m, eye to the
  closest point of a collider) and a 50-degree cone, with no non-enemy collider between.
  Scanned ten times a second for the HUD prompt, and asked again at the moment of the punch.
- **Bindings**: `ControlSettings.melee` (V), rebindable as "Punch"; pad **R3**
  (`AddPadBindings` and `InputPrompts.PadGlyph` agree); touch `TouchButton.ActionKind.Melee`,
  appended last because the kind is serialized by number into staged scenes. The touch button
  is **always shown**, in column 3 row 0, beyond the bomb on the bottom row -- a free cell, so
  nothing else in the cluster moves. The top row is taken on a phone: above JUMP is under the
  pause button, above the bomb is over the objective strip (both seen in `CaptureDevices`). A situational button that appeared when an enemy got close would jump
  under a thumb in the middle of the fight it was for.
- **The HUD says when it would land**: "V PUNCH" under the crosshair only while `InReach`.
- **The gun is lowered, not the reload cancelled**: `Weapon.Lower(seconds)` blocks fire,
  sights and the reload key; a reload already running carries on. The hitmarker and damage
  number come through `Weapon.ReportHit`, so the HUD has one source of hits.

**The hands** are primitives like the gun, gloves and sleeves rather than skin (no one skin
tone is the player's). Right hand on the grip and left under the handguard are children of
`WeaponModel` (`BuildGunHands`), so they recoil, sway and come up to the sights with it. The
punching fist is a separate arm under `WeaponHolder` (`BuildFist`): the gun dips away while it
is out and the fist must not go with it. The support hand hides while the fist is out, so there
are never two left hands. The swing is driven from a timestamp, not a coroutine, so a domain
reload mid-swing cannot leave the gun dipped. `VerifyCombat` punches the nearest level-one
enemy from arm's length (must go down, gun lowered, fist shown) and from eight metres (must
hit nothing); `CaptureDevices -fpskitOnly punch` shows it.

## A crosshair authored in canvas units disappears on a phone

The HUD's crosshair is four 2x10 arms against a 1920-wide reference. On a monitor that is a
hairline and correct. On a phone the canvas scale is about 0.45 and the result is then
stretched by the browser, so the arms land at roughly **a third of a millimetre** of pale
line over bright sand -- and the report is not "the crosshair is small", it is that the
phone has no aim point at all. From the outside that is exactly what it is.

`HUDController.FitCrosshairToDevice` re-derives it in millimetres on a touch screen, adds a
centre dot the weapon's spread cannot open -- the arms say *how accurately*, the dot says
*where* -- and outlines it so it survives sand, snow and sky. A pointer keeps the hairline:
a mouse resolves a hundredth of a degree and it is a deliberate choice there.

Two things about where that runs:

- **Not in the builder**, for the reason `TouchCluster` gives at length: a scene is authored
  in reference units and a reference unit is not a distance until there is a screen.
- **Not in `Start` either.** A `CanvasScaler` publishes its factor in its own update, which
  is after every `Start` in the frame, so anything measured in `Start` is measured against
  the scale the canvas had *before* the HUD rescaled it for this form. The first frame knows.
  `TouchCluster` now re-lays itself when that factor moves for the same reason, which makes
  the ordering between it and the HUD irrelevant rather than lucky.

## A button for equipment you have not earned, and one you have

Two halves of the same rule, and the kit had one of each wrong.

**The bomb button is situational and the check was made once.** `TouchCluster` hid it unless
a `BombThrower` was carrying data, asked in its own `Start` -- and `PlayerLoadout` arms the
bomb in *its* `Start`, with no defined order between them. It now re-asks twice a second and
re-lays only when the answer changes, which also covers the drink button when the belt runs
out mid-fight.

**And the instructions were teaching a control the player does not have.** The bomb is the
campaign's first reward, handed over when the first of the Augers falls, so before that there
is no key, no button and nothing in the HUD -- while HOW TO PLAY still described all three.
A player who follows written instructions and finds no button concludes the controls are
broken, which is what was reported: "there is no bomb symbol, so why". The row stays, because
the answer to *why* has to be somewhere, and it says what it is and how far off it is.

## The UI kit

The interface is being rebuilt screen by screen onto one kit: flat, matte, tactical --
solid panels, one-pixel borders, square corners, one amber accent, no gradients, glows or
shadows (a thin dark outline on HUD text is the one exception). Step one is the kit and its
gallery; no existing screen has moved onto it yet.

- **`UITheme` is a ScriptableObject at `Assets/UI/Resources/UITheme.asset`**, read through
  `UITheme.Active`. Its field initialisers are the spec: **FPSKit > UI Kit > Import Fonts And
  Icons** creates the asset from them when missing and afterwards only rewrites the references
  it owns (fonts, outline materials, icons) -- never a colour. The static colours further down
  the same file are the previous theme, kept until the last screen has moved.
- **Components are built by `UIKit` calls, not prefabs**, for the reason scenes are built by
  the builders: a prefab is one more asset to keep in step, and a hand edit is erased by the
  next build. `FlatButton` is a `Button`, so `onClick` and `VerifyFlow`'s raycast still apply.
- **Lines are in screen pixels.** `FlatRect` divides its border and stripe widths by the
  canvas scale factor, so a one-pixel border is one pixel at 720p and at 1440p.
- **Barlow has proportional figures and TMP has no `tnum`**, so counters go through
  `UITheme.Tabular`, which wraps digit runs in `<mspace>`.
- Fonts are `Assets/UI/Fonts` (OFL); TMP font assets are generated into
  `FPSKit_Generated/UI/Fonts` once and reused, because recreating one changes its GUID.
  Icons are `Tools/icons` (Tabler, MIT, plus the game's own drawn on the same 24-unit grid at a
  2-unit stroke), rasterised by `Tools/rasterize-icons.py` into `Assets/UI/Icons`, white, so the
  colour is the Image's.
- `FPSKitUIKit.VerifyGallery` builds `UIKitGallery.unity`, plays it, fails on any console error,
  an unreachable button, or a hover/press/tab that does not land, and renders the canvas at
  1920x1080 and 1280x720 by re-pointing it at an offscreen camera -- batch mode's own screen is
  640x480, so anything placed while the game is running is placed against that:

  ```
  UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitUIKit.VerifyGallery -fpskitOut Build/UIKit
  ```

## Every screen works on every device

The rules every screen follows from the UI kit onward, and the pieces that enforce them.
All of it is applied at runtime by `UIBootstrap`, to every scene, so no screen has to opt in:

- **Safe area on every root canvas** (`SafeAreaCanvas`). Full-screen layers bleed to the
  physical edge; placed content is moved into a `SafeArea` container fitted to the platform's
  safe area, one container per run of siblings so draw order is kept. Mark something that must
  stay put with `SafeAreaBleed`. A canvas that already has a `SafeAreaFitter` is left alone.
- **Interface scale** (`GameSettings.UiScale`, 80-130%) through `UIScaleBinder`, which keeps
  the authored reference resolution and combines it with the device form (`PhoneUI`) --
  `PhoneUI` used to multiply the scaler in place, which compounded on a second call.
- **A pad can use every screen.** `UINavigator` keeps the selection on something visible and
  pressable (a raycast at its centre, the test `VerifyFlow` uses for clicks), choosing the
  highest-priority `UIDefaultSelection`, else a primary button, else the top-left control.
  `UIFocusRing` draws the amber outline round it while the pad is live. LB/RB step the visible
  tab bar. Tooltips show on selection as well as on hover.
- **Touch targets at least 48dp, nothing over the crosshair, nothing outside the safe area.**
  `FPSKitBatch.CaptureDevices` measures all three and fails on them.

`SettingsPanel` is built at runtime from the kit onto whichever canvas asks
(`OpenSettingsButton`), so the dashboard and all seven pause menus share one screen without a
copy in any scene. Each setting is one accessor on `GameSettings`, straight over PlayerPrefs,
raising `GameSettings.Changed`; whatever owns a setting applies it from that event.

**`ScreenInfo` is where the screen is read, so a phone can be checked without a phone.**
`Screen` in batch mode is 640x480 with no density, notch or touch. `CaptureDevices` puts the
iPhone 15, Pixel 7 and iPad in its place (`ScreenInfo.SimulateNextSession`) and renders at each
device's resolution. Two traps it hit:

- **Canvases must reach the simulated screen before any `Awake`.** The touch buttons size
  themselves against the canvas scale in `Awake`, and a scaler publishes its factor in its own
  update. Measured against batch mode's screen, the pause button came out 31mm across and the
  crosshair 40mm. `ScreenInfo` routes the canvases in `BeforeSceneLoad` -- with scene reload off
  the scene's objects already exist then -- and toggles each scaler so the factor is current.
- **The HUD is rendered without the arena's post-processing.** The arena renders through its
  own camera, and the canvases render on black and on white, which gives exact coverage for
  compositing without trusting the pipeline's alpha. Tonemapping the HUD would change the
  colours the shots are meant to check.

The bundled simulator devices predate the iPhone 15 and Pixel 7, so those two are typed from
the manufacturers' specs in `FPSKitDeviceShots`; the iPad is the simulator's own.

## The dashboard is the PLAY tab under a shared top bar

Rebuilt on the UI kit (`FPSKitDashboard.cs`, a partial of `FPSKitMenuBuilder`). The top bar
(`MenuTopBar`) carries the logo, the tabs PLAY / LOADOUT / ACHIEVEMENTS / STORE, the rank and
XP, the coins, and Settings, Info and Quit. It stays up while the tabs swap underneath it: the
store, achievements, instructions, THE LIST and the level select are built into the scene
**inset below the bar** (`InsetBelowTopBar`), and Loadout, Info and Settings are built at
runtime from the kit. Only the story covers the bar. HOW TO PLAY and THE LIST are under the
Info icon; the old bottom button row and its EXIT GAME are gone.

- **On a handset the tabs are a rail down the left edge, not a bar.** The game is
  landscape-only, so height is what a phone lacks and the left edge is under a thumb; a
  bottom bar spends the wrong dimension. The builder makes both (`TabRail` is off),
  `ApplyFormLayout` swaps them, and every screen under the bar is inset by
  `MenuTopBar.railWidth` -- the runtime Loadout and Info screens included, which are
  created after the swap and inset as they are made.
- **A card selects; PLAY MISSION plays.** Clicking an arena makes it the one CURRENT MISSION
  describes (`Missions.NextLevel`: the first unlocked level with no stars). PLAY MISSION is the
  only button that loads a level; ALL LEVELS opens the ladder. `MainMenuController.Choose`
  still opens the ladder, which `VerifyFlow` relies on. The selection is remembered.
- **Rank and XP are derived, never stored** (`PlayerRank`): kills, stars, bosses and
  achievements, weighted. Nothing unlocks at a rank yet, so NEXT REWARD falls back to the
  next thing in the store the player can afford, and says so.
- **Difficulty is read from the ladder** (`Missions.ForLevel`): the level's roster step,
  which `FPSKitLevels` already offsets by the arena's campaign position.
- **The loadout strip shows an empty secondary slot** because the game has no secondary
  weapon. Grenades are the bomb (locked until the story hands it over), medkits the drink.
- **The last run's result is a toast**, and it is `lastRunPanel` while it is up, which is
  what `VerifyFlow` checks after a run.

**The arena thumbnails had a white disc in them.** The bomb aiming ring and pip were saved
switched on; the indicator hides them in `Awake`, so no one saw them in play, but the
dashboard renders its cards from the scene in edit mode. The builder saves them off now.

**No developer message reaches a player's screen.** The WebGL page used to print every Unity
warning and error as a banner, and errors stayed there: a player saw a red "NoSubscription"
line from an editor package's service check. They go to the browser console now, and onto
the page only with `?debug` in the URL. Builds are made without `BuildOptions.Development`,
so native players show no on-screen log either.

**Achievements are derived, never stored.** `Achievements` holds twenty thresholds read
against `PlayerStats` the moment somebody looks, so nothing can disagree with the counters
and an achievement added later is retroactive for free -- a player with 564 kills already
has the hundred-kill one the first time the screen opens. Counting happens in
`GameSession.RecordResult` and `StorePanel.Confirm`, which are already the single points
every ending and every purchase pass through, so a new ending cannot silently fail to
count. Stars and finished arenas are *recomputed* from the catalogue instead, because
`LevelProgress.Record` keeps the best of each attempt -- stars are not additive and a
counter could only drift from the truth.

**The instructions are read from `ControlSettings`, never written down.** The asset ships
three presets, so a panel claiming "WASD to move" is wrong for two of them, and wrong in
the worst way: somebody who follows written instructions and gets nothing concludes the
game is broken rather than the page. Touch and pointer get *different pages* rather than
one page with lines crossed out, chosen by `DeviceProfile.Touched`.

**Three things were extracted rather than copied**, because copies drift -- the same
argument `HoverCard` settles for cards. `UIText.KeyLabel` owns what a key is called on
screen, so the instruction strip and the instructions panel cannot disagree about whether
the fire button is "LMB" or "Mouse0". `OverlayPanel` owns open, close, Escape and the trap
where a panel's own component hides the object that would have run it. `BuildOverlayShell`
owns the shade, title and back button that three screens share.

## One accent is why a dashboard looks dull

`UITheme` holds the tokens. The menu used to draw all six arenas, the store, the level
tiles and the results screen in a single amber over near-black, so nothing on any screen
distinguished itself from anything else -- and it is also the commonest look a dark game
menu can have.

Six **signal** colours now, held apart in hue so no two cards are confusable and matched in
luminance so no arena looks more important than another, and an arena wears its own on its
card, its level tiles and its results screen. The point is that the colour *identifies*:
a player learns "the cyan one" before they learn "Snowbound Station".

**Not `LevelTheme.accentLightColor`.** That is a lighting value -- what the practicals in
that arena glow -- so it is muted by the job it actually does, and three of the six themes
never set it and fell back to the same orange. A light and an identity have opposite
requirements and now come from different places.

The base is a colour rather than a tinted near-black. What made the old dashboard look
flat was not that it was dark but that its floor was `#04060A`, against which every bright
thing appears to float rather than to be lit.


## The park is one surface, and that is the whole design

`LevelTheme.parkZone` is the fourth layout mode, and Abandoned Fairground is built with it
at 450x450 -- an abandoned fairground (at night until the overgrown rework, now dusk), with hollows in the ground and shafts in
some of them that kill on contact. `FPSKitPark.cs` builds it.

**Everything walkable is the terrain.** That is not a simplification, it is the lesson
from what it replaced. The arena in this slot was a three-level underground station --
track beds, platforms, a concourse over both -- joined by stairs, bridges and ramps, and
every one of those joins was somewhere `NavMeshSurface` could fail to connect. It took
thirteen build-and-verify cycles to find six separate connectivity failures and it still
never passed. A park is one continuous surface with things standing on it, so there is
nothing to fail to connect; it reached a passing check in three.

Write the section down before writing the builder. Of the subway's six failures, five
were visible in a two-minute sketch of "what is at each x, from the centre outward" --
that a 4.4m trench can only be entered sideways, that anything crossing it must fly over
it, that a platform edge strip lands exactly where a bridge does. The sketch was drawn an
hour too late.

Four things about the park are worth not re-deriving:

- **The ground is the desert's, subsided.** Same `BuildDuneField`, same relaxation, same
  smoothing, so it rolls the way ground does rather than the way noise does. A hollow is
  a `FlattenPad` pinned *below* `NaturalHeightAt` rather than carved afterwards, so it
  goes through the same nearest-pad-wins arithmetic as every other flattened site.
- **The pitfalls are the shafts, never the hollows.** Same rule the river already taught:
  a hollow is a place -- you walk down into it and fight in it -- and only the opening in
  its floor is lethal, with its trigger lid 2.2m *below* the lip so that standing on the
  edge is safe. Each lethal shaft is lit from inside, which is what makes it readable
  from across the park: hidden at distance, plain up close.
- **Dark is a lighting design, not an absence of one.** The station this replaced ran a
  0.15 moon with near-zero ambient and 0.016 fog over 450m, and rendered as a black
  rectangle -- every bit of its geometry present and none of it visible. What makes a
  night arena work is contrast, not dimness: a moon low enough to model the ground and
  throw long shadows, ambient that is not zero, and *local* light. The park is genuinely
  black between its lamp posts and warm underneath them, which is both more frightening
  and more honest about the state of the electrics than a uniform grey.
- **A stall is not a box.** Props here are built from parts -- a stall is a back, two
  sides, a counter, an open front, an awning on posts, a sign and a roof -- because what
  makes a stall legible is the opening you buy across and not its dimensions. A correctly
  sized, correctly coloured single cube reads as a crate. The built stalls also stopped
  appearing in `VerifyReach`'s stranded list, which solid cubes had been pinching pockets
  behind: proper geometry turns out to be better for navigation as well as for looks.
- **The fairground has a public front and a working back.** The entrance plaza, ticket
  booths, queue rails and midway are planned before the terrain so their pads remain level.
  An open repair yard and service van make the attractions feel operated rather than
  randomly scattered. Worn paths link the gate, midway and rides, follow the actual ground,
  detour around sinkholes, and have no collider so they cannot alter the proven NavMesh.
  Focused sodium pools at the gate, midway and service yard keep those places legible
  without lifting the darkness across the whole park.
- `CaptureViews` has Fairground-specific entrance, midway, maintenance, hazard and overview
  shots; heightfield-grounded shots identify the `Dune` terrain chunks even though the
  park's ground uses the Concrete surface tag.

## The fairground is overgrown: a hall, a jungle and walkways

Ishaan's brief for the rework (Oct 2026): "realistic, filled, not blocks" -- trees (a jungle
park), walkways, a big hall with a real interior, and the arena renamed from Abandoned Subway
(a name left over from the station it replaced) to **Abandoned Fairground**. His picks: full
rename, overgrown dusk, medium fill, a grand exhibition hall, and every landmark offered
(ferris wheel + carousel, stalls/tents/bumper cars, clown signage and gate, pond/fountain/bridge).
The files are `FPSKitFairgroundHall.cs`, `FPSKitFairgroundGrounds.cs`, `FPSKitFairgroundJungle.cs`.

- **The rename moves saves.** Progress is filed under `LevelSet.arenaScene`, so changing it would
  have zeroed every player who had played the arena. `SaveMigration.CarryRenamedArenas` runs on
  every start (idempotent, a few `HasKey` calls) and moves stars, scores and best times;
  `VerifySaveRename` proves it. The cheaper pattern is the one the Unknown Planet uses: keep the
  key and change `displayName`. A rename like this one is for when the key itself is wrong.
  Dead station code (`FPSKitSubway.cs`, nothing called it) went with it.
- **Everything walkable is still the terrain.** The hall is a pad flattened before the heightfield
  exists and a shell stood on it; its tiles are paint on the backdrop layer. The gallery, its
  collapsed staircase, the stage top, the bridge deck and the roof are all `NoStanding`. A first
  floor is two joins the bake can fail at, and the station that was here died of exactly that. If a
  playable gallery is ever wanted it needs a navigation link and a `VerifyReach` case first.
- **Eight doors, none under 4.5m clear.** A hall with one door is a trap, and anything with an
  inside needs more than one way out. The bumper pavilion's rail is the same: runs with openings.
- **Where a thing may stand is answered by three things, not one.** `Keep()` circles (rides, sites,
  the spawn, scatter), `_fgRoutes` (every walkway, with its width) and `SpotEmpty`, which asks the
  physics scene. The `_claimed` circles are no use here: by the end of the build they cover
  the whole map. The jungle runs **last** so it fills what is left instead of pushing aside what the
  player navigates by. Trunks are 2.8m apart minimum so that every gap in the thickest grove is
  passable; without it dense bands were pockets walled in on all sides.
- **Counts come out of a budget, not a hope.** `_theme.ScaledCount` scales with arena area, so a
  budget of 560 trees came out as 2,145 the first time. The jungle sums its own candidate weights and
  scales to 560. (`ScaledCount` is for things that should grow with the arena; a fixed budget is not
  one.)
- **Glow is a material, light is a budget.** Lamps, festoons and ride rims are emissive; only one lamp
  in four carries a real light. The hall has nine lights (three chandeliers, two cold skylight shafts
  and a stage spot among them).
- **Dusk is not a coloured sun.** The first dusk had a sun of (1, 0.58, 0.34) at 9 degrees: the green
  floor multiplied by 0.58 went brown and the whole arena was dark. A low sun is mostly light that
  has left the surface; the fix was 17 degrees, a softer (1, 0.8, 0.6) and letting the violet sky
  and ambient carry the mood. Likewise the foliage uses the soft `Adobe` map, not the streaky
  `Timber` one, which read as orange stripes.
- **Walkways go round, not through.** The old detour measured the bend from the line and for a line that
  only grazed a hollow put the bend on the hollow. The new one stands off by a margin from the
  *centre*, and treats a circle with one of the route's ends inside it as the destination.
- **`CaptureViews` helper:** points taken from a transform that stands on the ground already include
  its height; `ParkShot` adds the ground again. Use `WorldShot` for those (hall, pond, pavilion).
  Interior shots from the first run were in the roof for this reason.
- The scene went from 8.5MB to 41MB (trees are welded per 60m grove, flat-shaded). In line with
  Snowbound (26MB) and Mars (46MB), but a WebGL concern: fewer or lower-poly crowns are the lever.

## The volcanic plain is accidents, not a pattern

Ishaan's verdict on the first Unknown Planet was "too predictable and very artificial", and
both came from the same place: it was the desert's dune generator with the sand swapped for
basalt. Dunes are regular by nature; a lava field is a pile of separate events. So
`FPSKitLavaField.VolcanicHeightAt` replaces the dune trains with meandering flow lobes,
tumuli and collapse pits, and `BuildLavaFieldSurface` drapes fresh flows, hot ground, ash,
sulphur, fissures and rubble over it -- still inside the 26-degree cap and the chatter limit.
Three things found on the way:

- **Anything in a tiled texture repeats, including the glow.** Lighting a third of the
  basalt's cracks turned the 32m tile into a lattice visible from any height. The ground's
  own glow is now nearly nothing, and heat lives in `BuildHotGround`: drapes that share the
  ground's maps and world UVs, so a patch's cracks are the plain's cracks, lit. The
  landscape-scale colour comes through the Lit shader's detail slot (`Basalt_Macro`, linear,
  0.5 = no change) at a period that does not divide the tile's.
- **Nothing bakes lighting, so nothing reflects the sky.** Every glossy surface reflected
  Unity's default grey-blue: obsidian came out silver and fresh lava like ice. `ReflectSky`
  builds a cubemap from the painted panorama and sets it as the custom reflection. A
  realtime probe was tried first, and rejected: it only renders once the level is running,
  so no check can see whether it works.
- **`NoEntry` does nothing under an object that is both rotated and non-uniformly
  scaled.** The volume inherits a sheared frame the navigation package cannot represent,
  and the bake drops it without a word -- the volcanic cones kept a hollow disc of navmesh
  inside every one of them. `SealCone` puts the volume on an unscaled object of its own.

`CaptureViews` shots marked `Grounded` now measure height from the ground under them: the
fixed heights were written for a floor at zero and put the camera under the plain, which
renders as a smooth brown gradient that looks exactly like ground in shadow.
`10_open_plain` is the one shot not standing on a flattened pad.

## Shared builder code reads fields its callers never meant to fill

Four latent bugs surfaced in one session, and all four are the same shape: a helper
reading a value that only one kind of caller was ever expected to populate. Every one of
them had been shipping silently because the existing arenas happened to avoid it.

- **`BuildDuneField` cut a river through arenas with no river.** It reads
  `_theme.hazardWidth` to size the hole the gorge needs, so that the bake gets no floor
  over the water -- unconditionally, and every theme carries that field. A layout that
  never calls `BuildGorge` still got a 55m channel carved the whole length of its map at
  `hazardOffset`, with nothing in it: a void that split the arena in two, stranded
  everything beyond it, and took the ground out from under whatever had been placed near
  it. Gated on `openZone` now.
- **`NoEntry` sized its volume in world units and parented it to the object.**
  `NavMeshModifierVolume.size` is local, so it is multiplied by the parent's scale.
  Invisible for every caller until then -- art-pack prefabs at scale one -- and
  catastrophic for a `CreateBlock` primitive, where the scale *is* the size: two 121x450
  blocks produced Not Walkable volumes that blanketed the arena and the bake came back
  with **no navmesh anywhere**. Nothing logged, and a level with nowhere to spawn.
- **`BuildPlayer` assumed the walkable surface starts at ground level.** True of a flat
  floor and of a heightfield, so true of all seven arenas -- and false for any layout whose
  ground plane is raised, which puts the player *inside* it, on no navmesh, unreachable
  by everything. `_playerStart` lets a layout say otherwise and is cleared beside
  `ResetTerrain` so one arena in a six-arena batch cannot inherit the last one's.
- **`BuildDuneField` hard-coded the Sand detail map.** A fairground stood on wind ripples
  and read as a desert with rides in it. It takes `LevelTheme.floorDetail` now -- and
  Desert Outpost had to be pinned to `"Sand"` explicitly in the same change, because it
  had only ever been getting it from that hard-coded string and the field's default is
  `Concrete`. The fix would otherwise have quietly turned the dunes to pavement.

**A pad reaches `Radius + Blend`, so that is what has to be claimed.** Claiming only the
flat part lets a later pad's falloff lap over an earlier one, and the nearer pad wins
outright -- which is how a sinkhole forty metres away pulled the ground out from under a
ferris wheel. The same bug the desert already records, where a vantage sixty metres from
a bridge dragged the sand at the end of the deck down with it.

## VerifyReach reports where, not just how much

One sample point cannot tell a thin ring round the boundary from a severed half of the
map, and those want completely different fixes. It now prints the extent and centre of
the stranded ground, the extent of the *reachable* ground beside it, and where the player
is.

That is the difference between guessing and eliminating. The park's 25% took three failed
attempts at moving the navmesh seal -- all of them treating a boundary ring that was never
there -- and then four runs that each killed a family outright: hollows off (unchanged, so
not hollows), relief flattened (unchanged, so not terrain), player moved east (the split
*inverted* rather than followed, so the barrier was fixed in world space), and the 13m
dead strip at a known x pinned it to the terrain chunk builder.

**If a reachability failure is more than a few percent, read the extents before changing
anything.** A quarter of the arena is never a boundary strip.

## Previews are only rendered with a graphics device

`FPSKit_Generated/Previews/` were **130-byte single-colour PNGs** -- every one of them,
for as long as they had existed. The largest element on every dashboard card was an empty
rectangle, which is most of why that screen read as dull whatever the palette did.

They are rendered from the built scenes and the builder falls back to a flat fill when
there is no graphics device, so **every headless `BuildDashboard` quietly writes the
fallback over them**. Rebuild with `UNITY_GRAPHICS=1` or the cards go blank again:

```
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.BuildDashboard
```

## A reset can run and still not take

`ResetThemes` reported success and wrote the wrong number, because the case it was
resetting had two assignments to the same field and the later one won -- a new value added
near the top of a `Configure` case, and the original still sitting further down.

The documented trap is that changing `Configure` does not reach the asset without a reset.
This is the layer below it: **the reset ran and the value still did not change.** Every
check passed, the scene built, and the arena came out at its old size.

So: **verify a reset by reading the `.asset`, not by trusting the log.**

```
grep -a -o "arenaSize: [0-9.]*" Assets/FPSKit_Generated/Themes/<Theme>.asset
```


## The campaign is a sequence, and it is an asset

`CampaignData` (`FPSKit_Generated/Campaign/Campaign.asset`, generated by
`FPSKitCampaign.cs`, reset with `FPSKitBatch.ResetCampaign`) holds the whole spine: the
seven zones in play order, who holds each one, the story beats, and what falling to the
player hands over. **Reordering the campaign is dragging an entry in a list**; moving
which zone gives the bomb is retyping one string. Neither is a code change.

The order is Warehouse, Snowbound, Desert, Rooftop, Fairground, Mars, The Auger House, and
`FPSKitCampaign.ZoneOrder` is the one place it is written down -- `FPSKitLevels` shifts
its difficulty curve by position in *that* list rather than by position in
`FPSKitThemes.Names`, which is the order the arenas were built in and means nothing to a
player. Keyed on the wrong one, the third zone a player reaches is tuned as though it
were the second.

Four rules hold it together:

- **A child being down is derived, never stored.** A sibling *is* the last level of their
  zone, so `Campaign.ChildDefeated` reads `LevelProgress`. Storing a second answer is how
  the two come to disagree -- silently. Same rule `Achievements` follows. What *is* stored
  is whether the player has **read** a beat, because that is a fact about the player.
- **The gate is one method.** `Campaign.ArenaUnlocked`, exactly like
  `LevelProgress.IsUnlocked` and for the same reason. It asks two things: the holder of
  the zone before is down (the story), **and** the player holds `Zone.starsToUnlock` stars
  across the campaign -- 10/24/38/52/66/84 for Snowbound onward, Ishaan's pick, written in
  `FPSKitCampaign.StarsToUnlock`. Both, because stars alone would let a player meet a later
  sibling before an earlier one and the beat cards cannot survive that. `LockNote` says only
  what is still missing. A zone the campaign does not list,
  or a missing campaign asset, is **open** -- the kit runs in arenas it did not build, and
  a gate that failed closed would be a game with one playable arena and no way to find out
  why. The dashboard says so on screen when the asset is missing.
- **`SaveMigration.Version` wipes a profile once.** A save from before the campaign has
  stars scattered in no order, which under a sequential rule means zones opened by fights
  that never happened. `DeleteAll` rather than a key list, because a list is a thing to
  keep in sync with six classes and the one key left off it is the one that makes the wipe
  look like it worked. **Every play-mode test that touches a saved value calls
  `SaveMigration.Apply()` in its setup**, or the wipe lands mid-test between the backup and
  the assertions.
- **The story is shown where the player already is.** The opening and the beats are
  `StoryPanel` cards over the dashboard; a zone's opening is read on the way *into* it,
  once, before its ladder; the stakes line is under every countdown; and `DossierPanel`
  (THE LIST) is where any of it can be read again. `Campaign.Expand` fills `{child}`,
  `{they}`, `{their}`, `{them}` **at display time**, because the answer can arrive between
  a card being queued and shown.

## A level asks for something besides a body count

`LevelSet.Objective` is per level: `Clear`, `OneMagazine`, `Hunt`, `Hold`, `Blackout`,
`Extraction`, `Disposal`. `LevelObjective.cs` runs whichever one a level names.

**The invariant, and it is not negotiable: an objective may change what *scores* and may
delay the early finish a cleared roster grants. It may never change what *ends* a level.**
The clock ends every level without exception -- in a real arena an enemy that falls
through a gap stays alive forever, so any completion requirement is a guaranteed softlock.
`VerifyObjectives` sets an extraction on a level it then clears and fails unless the clock
still ends it.

- **A modifier is worth zero weight.** `OneMagazine` and `Blackout` make the same roster
  harder; they add nothing to kill or reach. Weighting them would create weight that can
  never be *earned*, so a perfectly played blackout would cap at two stars.
- **One magazine has to keep feeding you**, or it is not a hard level, it is one that
  stopped being playable without saying so. The ammo bonus is held on the objective and
  read by the spawner -- **never written into an `EnemyArchetype`**, which is a shared
  asset a level would be editing on disk for every other level that uses it.
- **Everything an objective places is placed at runtime**, sampled off the navmesh and
  asked whether the player can actually *route* to it. No scene holds a marked zone or an
  extraction point, which is why adding these rebuilt nothing.

## Each arena fights differently

`LevelTheme.enemyRoster` is who fights there, stamped into the scene by
`FPSKitSceneBuilder.BuildRoster`. It used to hand **every archetype in the project to
every arena**, so the six were separated only by the difficulty step -- the same number at
the same rung everywhere. A zone that looks different and plays the same is a skybox.

Grunts, Runners and Marksmen are still in every roster on purpose: a difficulty step has
to mean the same thing wherever it is played, or the ladder is seven unrelated curves. What
varies is the top half of the mix -- one local threat per zone (`FPSKitEnemyRoster.ZoneDebut`)
and the person standing at the end of it.

**Every Auger carries the `Boss` role**, so anything sweeping for "a boss archetype" will
find them. `FPSKitLevels` names the generic two explicitly
(`FPSKitEnemyRoster.GenericBossNames`) and asks the campaign who holds the arena for the
last rung -- without that, rung three of the Warehouse is Tove, who is standing at rung
eight of the same ladder.

A theme with no roster gets the lot and warns. That fallback is what keeps **Build Scene >
From Selected Theme Asset** working for a theme somebody made without reading this file;
an empty roster is an arena that spawns nothing, with nothing logged.

`VerifyRosters` fails if an arena carries every archetype, is missing its own zone's
enemy, has no holder, or fields the same list as another arena.

## Project conventions

- The builder provisions tags `Player`, `Enemy`, `Concrete`, `Metal`, `Wood`, `Flesh` and layers `Player`, `Enemy`, `Environment` via `EnsureProjectTagsAndLayers()`. New surface types or layers must be added there, not just in the Tag Manager UI.
- Surface tags drive `ImpactLibrary` decal/FX lookup on bullet hits.
- Enemy navigation uses `com.unity.ai.navigation` (`NavMeshSurface`), baked at build time by the builder.
- Tunables are `[Header]`-grouped public fields with `[Tooltip]`s written as plain prose. Match that style — the existing XML doc comments explain *why* a knob exists, not just what it is.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are gitignored; `.meta` files are committed and must stay in sync with their assets.
- Placeholder audio is synthesised by `Tools/generate-placeholder-audio.py` (stdlib only). It draws from one seeded random stream, so a new clip that uses noise must save and restore `random.getstate()` around itself or every sound authored below it is re-rolled into an identical-sounding but byte-different file.
- Arena previews (`FPSKit_Generated/Previews/`) are rendered from the built scenes by the dashboard builder. Render them through `RenderPipeline.SubmitRenderRequest`, not `Camera.Render()` — the latter predates scriptable pipelines and under URP returns a frame with the skybox and essentially no lighting, so every arena comes back as black silhouettes. Call `DynamicGI.UpdateEnvironment()` after opening a scene and submit twice, or the first arena captured is lit by nothing while the rest look right.
- **If a build suddenly has no sound, suspect `Library/` before the files.** A corrupted asset database makes Unity import every `.wav` as a `DefaultAsset` rather than an `AudioClip`, with no error logged anywhere; the builder then writes null into every audio slot and saves a mute scene over a working one. Closing Unity and deleting `Library/` fixes it. `FPSKitSceneBuilder.Clip` now warns per clip and `ReportMissingClips` sums it up at the end of a build, so this is loud rather than silent.
- **A Unity skill's advice yields to this file.** The `unity` plugin's skills are written for a
  generic Unity 6 project and several of them are confidently wrong here: the package-selection
  skill recommends the new Input System for an FPS, and the UI skills route to prefab or UXML
  authoring. This project is legacy-`Input`-only on purpose, and its UI is *constructed in C#* by
  `FPSKitMenuBuilder` and `FPSKitSceneBuilder`, so a prefab edit is erased by the next build. The
  skills are still worth reading -- `optimize-web`, `initialize-ai-navigation`,
  `physics-3d-collision` and `urp-postprocessing` all apply -- but read them for the technique and
  keep the conventions above. Where the two disagree, this file wins.


## The fairground, replanned (Oct 2026)

Ishaan's verdict on the overgrown rework: no real walkways, a small hall, a staircase that was not real,
and blocks. The brief: an *abandoned* fairground, dense, many broken rides, a half-built/broken hall, many
walkways, no filler blocks. Everything was replaced (`FPSKitFairground{Plan,Parts,Hall,Rides,Attractions,
Street,Fill,Clutter}.cs`; `FPSKitPark.cs` keeps sinkholes and the small helpers).

- **The old paving was invisible.** Its quads were wound counter-clockwise from above, so Unity culled
  them. A strip must be clockwise from above: `PathQuad` uses (0,3,2),(0,2,1). Check winding first when a
  mesh "does not show".
- **A plan, not dice.** One table of sites (gate S, plaza at the spawn, hall N, ferris NE-ish, coaster E,
  carousel/big top W, haunted SE, maze/pond/drop tower/swings NW, yards at the corners, golf, karts, flume,
  shops on the avenues) and 20 walkways from door to door, plus a perimeter service road. Spurs leave an
  avenue at its edge so paving never overlaps; a plaza is the surface under its tiles.
- **Real elevation, proven.** The 80x50m hall's gallery is a U of solid decks 6.16m up (28 risers),
  reached by a grand 5m stair and two plain ones; the west two thirds are the finished ruin, the east third
  a never-finished site (steel frame, ragged brick, scaffold, excavator, crane). The previous hall's stair
  went nowhere on purpose (a first floor killed an older arena's navmesh); the industrial walkway recipe
  (solid treads, landing flush with the deck) is what made it safe. The reach probe samples at 1.5m so it
  could never see a deck: `CheckFairgroundDecks` raycasts each deck's top and paths it to the player. It
  caught four real faults on first run: a stage lip walling off its own stairs, a container-roof deck
  sharing a plane with the container's non-walkable mesh (the NoStanding area wins), a probe landing on a
  carousel drum, and a bridge probe reading the rail's height.
- **Closed shapes seal infields.** Two tyre rings with complementary gaps filled each other's, sealing the
  kart infield; a flume channel under 2m headroom walled off pockets. The stray list the reach test now
  prints (coordinates) is how both were found.
- **Rides are tubes and lattice,** not boxes (`Lattice`, `FlatTruss`, `Arc`, `Cable`, `Cloth`, `Horse`).
  The coaster is a Catmull-Rom spline with banked rails, a fallen stretch laid crumpled on the ground, and a
  climbable station. Stock counts: ~25 booths/tents, 13 shops, ~730 clutter pieces, 300 trees; scene 61MB.
- Capture list lives in `FramePark` (`FPSKitViews.cs`); the tool is in see-a-built-arena.


## The fairground, third pass: routes to things, not things on a lawn (Oct 2026)

His verdict on the replan: "I don't feel it real, the walkways are not good and not connecting
properly, I want meaningful things to fill the land." What was wrong, from my own renders: rides
stood on a 450m lawn with paths drawn *near* them; attractions added later (golf, flume) had no path at
all; every path was a bare strip with no edge; clutter was random crates in open ground.

- **Smaller, and brighter.** 340m (was 450), so the next scene is under ~40m away; sun 1.75, brighter
  ambient and a lighter lawn, because the detail did not read in the murk.
- **Walkways are a graph that is checked.** Every place a visitor goes in is an `Entrance(...)` in the
  plan (17 ride/site doors + one per shop). `CheckWalkwayGraph` unions paths and plazas where their
  paving meets and fails the build if the park is more than one network or an entrance is >2.5m from
  paving; it writes an `Entrances` marker group into the scene. `FPSKitBatch.VerifyWalkways`
  (`FPSKitWalkwayTest`) re-derives the same answer from the *saved* paving meshes, flood-filled across
  one missing stone. A plan that is right and a scene that is right are different claims.
- **Paths have edges.** A gravel verge a metre either side, a kerb, and on narrow ones a bollard every
  5m with chain between. Signposts at junctions point at real destinations (bearing from the plan);
  map boards are drawn from the plan itself at 1:103.
- **Every ride has a gate where its walkway arrives** (`RideGate`): posts, a sign with the ride's own
  pictogram, a hanging chain and sign across half the way in, a height gauge, a booth, a rope queue. +z is
  towards the visitor; the first version faced the ride and every sign was seen from behind.
- **Meaning is placed.** Gate house with notice board and wreath, plaza carts, a four-parterre rose
  garden, pedal swans at the pond dock, a camp with a live fire, a sandbag line across the hall's
  forecourt, coin horses at the carousel, a yard break room and service carts. Clutter is no longer
  random in the open: it is placed beside a walkway, against a building/ride, or in a sitting spot
  a few metres back from a path.
- **Ground wears things for reasons** (`FPSKitFairgroundGround`): mud and puddles at path edges and
  building feet, leaf litter under trees, moss in the damp, gravel spill, and lawn patches. All flat
  decals on the backdrop layer, wound clockwise from above.
- Dropped as weak fits without entrances: mini-golf and the log flume. Karts stayed (gate + path).
- **Last details** (`FPSKitFairgroundDetail.cs`): paper/confetti drifts and cracks laid above the paving
  (0.082 on paths, 0.115 on plazas, so nothing z-fights), and crows perched on the gate towers, hall
  ridge, clock tower, yard water tower and big-top pole. None is cover or on the bake.

## The fairground, realistic night rebuild (Oct 2026, in progress)

The cheerful low-poly park is being replaced by a dark, flooded, abandoned fairground built to the quality
and mood of the Flooded Grounds Asset Store pack. The old scene stays until the new one replaces it. Look
recipe (measured, not guessed): `Docs/FAIRGROUND_LOOK_RECIPE.md`. All the new tooling is `Fairground*.cs` in
`Assets/Scripts/Editor` (menu: **Tools > MiniFPS > Fairground**); generated output is in `Assets/Fairground`.

- **The pack is not committed** (`.gitignore`), and neither is `_TerrainAutoUpgrade`. Our converted copies
  (`Assets/Fairground/Materials`, `Prefabs`) still point at the pack's textures and meshes, so a fresh clone needs the
  pack re-imported first. `PostProcessing/Editor` inside the pack is renamed `Editor~`: its two scripts do not compile
  on Unity 6 and they take the whole editor assembly (every menu item) down with them.
- **Shaders are HLSL, not Shader Graph** (`Assets/Fairground/Shaders`): `FG_TopBlend_URP`, `FG_Triplanar_URP`,
  `FG_Water_URP`, sharing `FG_Common/ForwardPass/DepthPasses.hlsl`. Lightmap variants are dropped on purpose
  (realtime + adaptive probe volumes). `_FG_LOW` is the Low-tier switch. Water needs the depth texture for
  depth darkening and the soft shoreline; the Mobile URP asset has it off.
- **Terrain trees:** Tree Creator billboards render wrong in URP (white leaves, blue trunks). Set
  `treeBillboardDistance` past the view distance; the baked meshes are converted to URP Lit.
- **Terrain mesh grass does not render** with the converted prefabs (instanced or not, even a forced full-coverage
  patch). Unsolved; the fallback is script-scattered instanced grass, not terrain details.
- **Night balance:** exposure is fixed, so a dim moon needs `FG_Night` postExposure ~+1.4 to be playable. The night HDRI
  (`qwantani_night_puresky`) has horizon values far above 1: skybox exposure 0.12.
- **Blockout** (`FairgroundBlockout`): x east, z north, 180 m playable. Cover is placed along lanes and into any empty
  10 m cell; sightline blockers are placed by raycasts but never in lanes, the spawn, the Big Top or a camera spot
  (the first version put a 3 m wall in front of three of the eight views). `WadingZone` carries the configurable
  water speed penalty but is not wired into `PlayerMotor`/`EnemyAI` yet.
- Fixed camera spots live in `FairgroundSpots` (gate, midway, carousel, ferris, lowlands, bigtop, +coaster, backlot).

### Step 2 (ground, roads, water, props), Oct 2026
`FairgroundDress` (menu **Build Step 2 Dressed Scene**) runs the blockout generator, then dresses it and saves
`AbandonedFairground_Step2.unity`; the approved blockout scene is untouched.
- The blockout's rubble-wall sightline blockers and box cover were the "random big blocks". They are now the pack's
  fences, wrecked cars, a beached boat, benches, cabinets, sofas, flower boxes and cobble rocks. Blockers still never go
  in lanes, the spawn, the Big Top or a camera view.
- Every remaining primitive uses `FG_WorldPBR_URP` (world-space triplanar, whiteout normals) with the CC0 sets, so boxes
  of any size keep their texel density. Roads are generated mesh slabs with kerbs (not the Splines package): asphalt
  strips, concrete plazas, an asphalt lot; mud bleeds over their edges in the terrain paint near the flood.
- Night values that work (exposure is fixed): moon 1.3, trilight ambient 0.26/0.19/0.09, `FG_Night` post exposure +2.0,
  fog 20 to 220 m, leaf tint (0.24, 0.30, 0.26) so the treeline sits back.
- **Decals** (`FairgroundDecals`, 230 placed): procedural textures (cracks, potholes, oil, mud, leaves, worn queue arrows,
  flood lines). They need the `DecalRendererFeature` on the renderer in **Screen Space** mode (added to `PC_Renderer`;
  our own shaders do not write the DBuffer). `Mobile_Renderer` does not have it yet (Step 5). Flood-line decals are
  feathered at the ends; unfeathered they show a seam every 6 m.
- Not done yet: grass, drain grates, boardwalks round the rides outside the lowlands, practical lights.

### Step 3 (dressing and decay), Oct 2026
`FairgroundDressing` (run by the Step 2 build): fairground signs, caution/quarantine tape, plywood boarding, sandbag
emplacements, torn alpha-clipped tarps (they sway: `WindFlutter`), toppled bins, four squatter camps, burning-barrel and
camp-fire anchors (`FX_BarrelFire`, `FX_CampFire`) for the lighting pass, string lights (most bulbs dead, ~7% emissive
for flicker, some wires snapped to the ground), furniture clusters, litter and spray-painted warnings. Textures are
generated by a Python/PIL script (not in the repo): signs, tape, graffiti, litter sheet, tarps; no real brands.
- **Anything that moves at runtime is not static** (`Flutter()` clears `isStatic`), or static batching freezes it.
- Memory: the full rebuild runs near the machine's limit. It now releases unused assets between stages, all of our
  scenes use an explicit lighting-settings asset with Auto Generate OFF, and textures are capped (colour/normal 2048,
  roughness/AO/sky 1024, signs 1024, decals 512). Set Preferences > Asset Pipeline > Import Worker Count to 0 before it.
- Known weak spots: sandbags still read slightly brick-like, sleeping bags are capsules, signs are flat slabs.

### Step 4 (light and atmosphere), Oct 2026
`FairgroundLighting` (menu **Add Step 4 Lighting To Open Scene**) works on the OPEN Step 2/3 scene (no rebuild, so it is
cheap on memory) and saves `AbandonedFairground_Step4.unity`.
- **The moon is kept low (0.7)**; the scene is lit by practicals so the eye follows pools of light: 6 burning barrels and
  4 camp fires (anchors `FX_BarrelFire` / `FX_CampFire` from the dressing pass, flicker + flame particles), 5 generator
  work lights (cold spots with a hum; the gate and the Big Top cast shadows, the only two that do), 3 pulsing red
  beacons, one lit ticket booth, 29 live bulbs that drop out (every third carries a light). 27 lights in all.
- `FG_FxAdd_URP` is the one additive shader for flames, rain, shafts and mist. Mist cards must be FAINT: tint 0.06 turned
  the lowlands to ice; 0.011 is a haze. Shafts are additive cones along the moon direction through the Big Top's four
  missing roof slabs and a hole cut in the pavilion roof.
- Runtime: `FlickerLight` (flicker/pulse/dropout, emission via MaterialPropertyBlock), `RainFollow`, `AmbientOneShot`.
  Audio is generated, not downloaded (`Assets/Fairground/Audio`, Python/PIL/wave script not in the repo).
- **Not done / not verified:** reflection probes are placed but NOT baked and there is no APV bake (waits for approval).
  Particles do not simulate in edit-mode renders, so rain and flames were never seen moving. Audio was never listened to.

## Fairground 450 m arena (2026-10-02)

Ishaan asked for more breathing space: the arena is now **450 x 450 m** (`FairgroundBlockout.Half` 225; terrain 550 m,
heightmap 1025). The hand-planned fair (the old 180 m square, `Core` = 90) is unchanged and stays flat and dense; the
band outside it is abandoned open ground. Stretching every hard-coded site coordinate was rejected: ~130 coordinates
across the blockout, plan and dress files, and the verified reach/deck checks depend on them.

- **Height:** `H()` clamps to `Half + 1`; beyond `Core + 25` a gentle swell (about 2 m over 80 m, ~4 degrees) fades in, never
  over the flood (`LowMask`). The SE corner stays flooded lowland.
- **Cover:** `FillEmptyCells` covers the whole arena but the meadow gets ~1 piece per 25-30 m. Sightline fixing stays in the core.
- **Outskirts** (`Outskirts()`): gate road runs on to the wall, service roads, overflow car park, ranger lodge (two doors),
  campsite, water tower, watch post (raised deck, flush stairs, in `Elevated`), fallen fence lines, road wrecks.
- **Trees:** up to 1400 terrain trees: noise-clustered groves in the meadow (2.8 m trunk spacing, off the flood, clear of the
  core) plus the treeline beyond the walls.
- **Zones:** North Fields, West Meadow, East Meadow, South Meadow added to `ZoneDefs` and `FairgroundFightOrder`; spawn range
  is `Half - 4`, ring cap 170, zone distance 18-260, despawn 560, camera far clip 420.
- **Rain removed** (particles and `RainBed` sound); `Rain()` is no longer called.
- **Checks:** `VerifyReach` passed (Fairground 1.3% stranded, strays on props/track). All 88 spawn points on 11 zones are
  on the NavMesh and path to the player start. `VerifyWalkways` skips the realistic arena by design.
- **Checks no longer close the editor:** every Verify* exit goes through `FPSKitBatch.Exit`, which only quits in batch mode.
  (Calling `VerifyReach` over MCP once closed his editor.)
- Not yet done: baked lighting/probes, Mobile_Renderer decals, grass, `WadingZone` wiring; a 450 m arena needs a perf pass
  on WebGL/Android (tree count, draw distance).

### 450 m arena: the fair is stretched (2026-10-02, supersedes the "core stays at the planned spot" note above)

Ishaan: "you have not stretched the current properly". The planned fair is now spread over ~350 m, not left in the middle.
- **Planned space first.** `FairgroundBlockout.Generate()` builds the eight sites in their planned coordinates (`stretched` = false);
  Dress, Dressing, Decals and `FairgroundLighting.ApplyWorld` all run in planned space, so none of their ~50 hard-coded
  coordinates changed. `FairgroundDress.Finish()` then calls `FairgroundBlockout.FinalStretch()`.
- **`FinalStretch`:** each site group moves rigidly to `Warp(anchor)` (x2 from the centre, per axis; sites keep size and insides).
  Everything else (dressing, decals, lights, audio) moves with the site it belongs to (`SiteOf`: footprint rectangles, else nearest
  anchor). Marks on the old perimeter wall (|x| or |z| = 88.9) are re-seated on the new wall. Then `stretched` turns on:
  `H()` reads the planned ground through `Unwarp` (flood, gate plateau stretch continuously) and the terrain, water, walls,
  outskirts, zones and cover are built in final coordinates. `SwapProps`, `Retexture`, roads, paint and trees run after.
- **Roads** are re-laid between the moved sites (`AddRoadShapes`, final coordinates); `PlannedStrips` keeps the old lanes for the dressing.
- **Performance:** 420 trees (was 1400), tree distance 170, 120 full-LOD trees, heightmap 513, terrain pixel error 8, kerbs in
  4 m runs and none on the outer dirt roads, sightline blockers capped at 80, camera far clip 320.
- Checks: `VerifyReach` passed, Fairground 0.9% stranded. 87 of 88 spawns reachable (one Midway point is off the navmesh; spawns are snapped at run time).

### Old-growth wood replaces the flood (2026-10-02)

Ishaan did not like the water ("does not look any clearer"): the flood is gone. `LowMask` returns 0 (ground no longer sinks, no
pit beyond the walls), `FloodWater`, the water plane, flood decals, `FloodNavVolume`, `MapWater` and the lapping sounds are removed.
The south-east quarter (`FairgroundBlockout.InOldGrowth`) is an old-growth forest built by `FairgroundDress.BuildOldGrowth`:
huge, very thick trees on a jittered 20 m grid (prefab trees scaled 3.4-4.8 wide, 2.6-3.4 tall, each with a trunk capsule so the
NavMesh goes round), 16 fallen giant logs, 28 mossy boulders. The floor is painted moss/leaf, not mud. The zone is renamed
"Old Growth" (minimap "WOODS"). The lowlands site (pavilion, kiddie rides) now stands inside the wood.

### Dusk lighting (2026-10-02)

Ishaan found the night too dim: "brighter like dusk or dawn (not too much)". `FairgroundLighting.Atmosphere`: a low warm sun
(colour 1.0/0.74/0.58, intensity 0.95, elevation 22 degrees, yaw 208), ambient sky 0.30/0.34/0.48, fog 0.25/0.25/0.31 from 14 to 210 m,
reflection intensity 0.7, sky exposure 0.4 with a warm tint. `FairgroundVolume.Night`: post exposure 1.5 (was 2.0 with a dim moon),
saturation -14, warm colour filter, white balance +3. The Volume profile is now rebuilt on every scene build (it used to load a stale
asset, so edits to `Night()` did nothing). Old-growth fix: the fallen-log overlap box is long and thin (it was a square), 400 tries.

## Asset cleanup (2026-10-02)

- **Audit:** `Tools > MiniFPS > Assets > Audit Unused (read-only)` (`FPSKitAssetAudit`) lists what the build scenes, Resources folders,
  settings and generated content reach, what only the scene builders name (exact file names / paths in scripts), and what nothing uses.
  Its dependency analysis cannot see shader `#include` files or `Shader.Find` names: never archive `.hlsl`/`.cginc`/`.compute` on its say-so.
- **Archive:** `Tools/archive_assets.py --apply` moved 430 unused files (262 MB, including the whole Post Processing Stack v1) to
  `~/MiniFPS_AssetArchive/` (same folder structure, `.meta` files with them, `MOVED.txt`, `restore.sh`). Kept in the project: `Fairground/ThirdParty`
  (CC0 textures for the dressing step), shader includes, `InputSystem_Actions.inputactions`, and 1.5 GB of pack content that only the builders load.
  The pack's own profile `Postprocess_FloodedGrounds.asset` was archived too; its values are in `FAIRGROUND_LOOK_RECIPE.md`.
- **Packages removed:** `com.unity.ai.assistant` (its Accounts module polled `generators.ai.unity.com`), `visualscripting`, `timeline`,
  `collab-proxy`. Kept `probuilder`.
- **Old formats:** 97 old Flooded Grounds assets were re-saved in the Unity 6 format (originals in `~/MiniFPS_PackBackup_preResave/`);
  `FpsController.prefab` lost its dead post-processing component. `HudEditor.HandleDrag.handle` is `[NonSerialized]`.
- **Checked afterwards:** 382 prefabs and 8 build scenes: no missing scripts, no empty material slots; Console clean.

## Night Rooftop is a city district (2026-10-03)

Ishaan found no suitable Asset Store pack and asked for a real or semi-real 450 m arena, so the Night Rooftop was rebuilt as a night
city. `LevelTheme.rooftopZone` (precedence over `industrialZone`); `FPSKitCity.cs` (plan, textures, materials), `FPSKitCityBuildings.cs`
(shell, roof, fire escapes, bridges), `FPSKitCityStreet.cs` (crossings, lamps, cars, signs, lights, skyline), `FPSKitCityCheck.cs`
(`Tools > MiniFPS > Rooftop > Check Reach`, runs in the open scene). It reuses the industrial street grid (`PlanStreets`, `BuildStreets`,
the fence, `BuildZoneSpawnPoints`): 16 blocks of 80 m on a 20 m tile, cut into 40 lots (cross of four, two, or one big and two small).

- **Facade:** one texture pair per wall type (masonry, glass curtain wall): 16 bays of 3.3 m, a window in each, and an emission map of
  the same layout with 26-30 % of the bays lit (warm, cool white, screen blue, pink). Every wall is snapped to the 3.3 m bay so a window is
  never cut by a corner; every building is built in world coordinates so each reads a different part of the tile. Six tints, no lights.
  The skyline beyond the fence is the same material.
- **Roofs:** 32 of 40 lots are walkable (roof 3-9 floors of 3.3 m), 8 are towers (12-18 floors, setbacks, antenna, red beacon, no way up).
  Towers lean north-east. A walkable roof has two fire escapes on two faces (zig-zag, two 2.2 m lanes, 15 treads of 0.22 per floor,
  3 m landings; the last landing overlaps the deck 1.8 m) and a parapet open at each. A lot that cannot be given two escapes becomes a
  tower. Seven skybridges join roofs across a street or alley; each pair is raised to the lower height (groups unioned).
- **Navigation rules that cost a rebuild each:**
  1. **A closed box has an inside to the bake.** The voxelizer rasterizes triangles, so the slab under a building baked a room of navmesh
     (spawn points inside buildings, 4 of 16 unreachable). Every tier has a `NavMeshModifierVolume` (`NoEnter`) that stops a metre under the
     deck so the roof stays walkable; the roof stair bulkhead has one too.
  2. **Lanes of 1.6 m baked on some buildings and not others** (the bake erodes half a metre off each edge): 2.2 m lanes and 3 m landings.
  3. Everything flat with no way up (towers' roofs, parapets, cornices, rooftop plant, car roofs, awnings, signs) is `NoStanding`.
- **Emission:** `globalIlluminationFlags` must be `RealtimeEmissive`. With `None`, URP cleared the `_EMISSION` keyword on import and no
  window lit.
- **Lights:** one sodium point light at each junction (no shadows, a quarter flicker); windows, signs and lamp heads are emissive.
- **Verified:** reach check 32/32 roofs, 16/16 spawns, 800/800 sampled navmesh triangles reachable from the player (11 k triangles); a
  30 s play session on level 1 had 6 enemies on the navmesh within 120 m and no errors. Static report: 208 k triangles, 1444 renderers,
  19 lights, 11 MB of textures (the Fairground is 3.9 M, 4895 and 656 MB). **Not run:** the batch `Verify*` checks (editor was open).
- **Not done yet:** per-level card images, minimap check, a quality rig like `FairgroundQuality` (lights and particles per tier), street
  trees/rubble, interiors. Cars are boxes; the pack has none.

## FPSKitGraphics must not write the tier assets (2026-10-03)

`FPSKitGraphics.Apply()` runs on every scene build. It first calls `FPSKitQualityTiers.Ensure()` and then used to raise every active
pipeline asset to 95 m shadows, four cascades, 4x MSAA and put full SSAO back, which silently undid the tier settings (and is why Medium
had 95 m). It now skips `FPSKitQualityTiers.IsTierAsset`; only `Ensure()` writes Low/Medium/High. High is 50 m, 3 cascades, 2x MSAA, 85 %
render scale with FSR 1, half-resolution 4-sample SSAO.

## Night Rooftop streets: footways, crossings, signals, lamps (2026-10-03)

Ishaan asked for proper street lighting, good roads, traffic lights and a good pedestrian walkway. `FPSKitCityRoad.cs` (new) and
`FPSKitCityStreet.cs`; all of it is paint or hand-sized detail with **no collider and on the Backdrop layer** (the bake ignores it), so the
reach check is unchanged (32/32 roofs, 16/16 spawns, 800/800 samples, 11.2 k triangles).

- **Footways:** each road tile is 20 m with a 10 m carriageway, so the other 5 m each side is footway. Paving = the four corner squares plus
  the strip on any side that is not more road; grayscale 0.6 m slab texture (`City_Paving`, 2.4 m per tile) a few cm above the ground, and a
  `RoadKerbs` kerb on the carriageway edge. Kerbs and paving on `layer` (not Backdrop) bake as ledges: keep them on Backdrop.
- **Crossings:** a zebra on every arm of every junction (bars 0.5 m, parallel to traffic), a stop line for the lane that enters (traffic keeps
  right), yellow tactile patches where the footway meets the crossing, and a mid-block zebra with a beacon pole at each end on some straights
  (`MidBlockCrossing`: two plain tiles each side, 50 m from the spawn). `_cityCrossings` keeps parked cars off them (`NearCrossing`).
- **Signals:** at every junction of 3+ arms, a pinwheel of four footway-corner poles. Each carries the head for the arm whose driver keeps to
  that side and a pedestrian signal for each crossing it stands beside. Phase is fixed per junction (east-west green / north-south green / east-west
  amber); a crossing shows walk while its arm's traffic is red. Emissive only, no lights.
- **Lamps:** one per straight tile (20 m), alternating sides, 8.2 m with a 2.7 m arm. Every lamp has an additive pool (`City_LampPool`) on the road;
  every second lamp has a real unshadowed spot (80 lights; scene total 99). URP allows only 4 additional lights per object (`PC_RPAsset`), so
  real lamp lights are decoration for walls and cars; the pools carry the look. The 80 sit under `StreetFurniture/LampLights`: a `RooftopQuality`
  rig should thin them per tier (the next step).

## Night Rooftop: stairs, cars, night, roofs (2026-10-03, second pass)

Ishaan fell off the fire escapes whenever he missed the turn at a landing, found the night too dark, the cars cartoony and the roofs black.

- **Fire escapes:** every flight now has a handrail on both sides, a toe plate and a baluster every second tread; every landing a full perimeter rail
  with balusters and an amber lamp cage at the corner (a marker for the turn). Under the rails are **invisible `BoxCollider` guards**
  (children of the `FireEscape`, 1.2-1.3 m tall, tilted along each flight) so overshooting a landing is a bump, not a fall. Colliders without a
  renderer are ignored by the bake (`CollectObjects.All` collects render meshes), so the layout and the reach check are unchanged.
- **Cars:** `FPSKitCityCars.cs`. The pack's only cars are rusted wrecks of 8-21 k triangles each and no download source is configured
  (Sketchfab has no token), so cars are built in code: a ten-point ring lofted along the length (hood, deck, greenhouse, tail), glass,
  arches, tyres, rims, head and tail lamps, bumpers, mirrors, plates; three shapes (sedan, hatch, SUV), ~1 k triangles, six submeshes so
  one renderer per car, two `BoxCollider`s instead of a mesh collider. `Mathf.SmoothStep(a, b, t)` is an interpolation, not the shader
  smoothstep: use `Sm`. Parked nose to the flow of the lane (traffic keeps right).
- **Night:** moon 0.35 -> 1.05, ambient up ~2x, post exposure 0.35 -> 0.85, lighter fog. `FPSKitCitySky.cs` paints the sky panorama (gradient with a city
  glow at the horizon, ~6500 stars thinned toward the horizon and hidden by cloud, cloud banks lit from the streets, a moon placed opposite the
  directional light) and `ReflectSky` makes it the reflection. `CreateSkybox` returns it for `rooftopZone`.
  Theme numbers changed in `Configure`, so only that theme was reset (`Configure` called for "Night Rooftop" alone, not `ResetAll`).
- **Roofs:** the roof material was near black (0.13); now 0.30 (set explicitly, `MakeMaterial` leaves an existing asset alone). Each walkable roof
  gets two floodlight posts on corners clear of stairs and plant, an additive warm pool under each, a real point light, string lights between
  the posts and amber corner lamps on the parapet. Lights: 131 in the scene (99 -> 131), 539 k triangles. **A quality rig must thin the lamp
  and roof lights per tier before this ships on Medium/Low.**

**RooftopQuality (2026-10-03):** `Runtime/RooftopQuality.cs`, wired by `BuildCityZone` onto `Arena/RooftopQuality` (80 lamp lights, 32 roof lights). Low: no lamp lights,
every third roof light, shadows 45 m / 2 cascades; Medium: every other lamp light, every roof light, 60 m / 2; High: all, 55 m / 3. The pipeline asset's
shadow range is saved and restored on disable, like `FairgroundQuality`: never call `Apply` on it in edit mode (nothing restores it). Reach check unchanged.

## Flooded Grounds pack trimmed to what the Fairground uses (2026-10-03)

The Asset Store pack was 2.6 GB in `Assets/Flooded_Grounds` (gitignored). The Fairground scene and every asset under `Assets/Fairground` depend on 187 of its files (1.29 GB of
source textures, trees, meshes, props); the other 1.3 GB (demo scenes, unused textures) was deleted. The 187 were moved with `AssetDatabase.MoveAsset` (GUIDs kept, so no
reference broke) to `Assets/Fairground/ThirdParty/FloodedGrounds/` with the same internal layout, and the builders' path constants point there. That folder is **gitignored** (the pack's
licence is why the old one was): a fresh clone must re-import the pack (`Flooded Grounds.unitypackage`, ~/.local/share/unity3d/Asset Store-5.x) and move those files; the list of 187 is
whatever `AssetDatabase.GetDependencies` returns for `AbandonedFairground.unity` plus `Assets/Fairground`.
Two traps: `AssetDatabase.CreateFolder` inside `StartAssetEditing` is deferred, so every move failed ("parent directory is not in the database") and left empty junk folders; create folders first.
And deleting folders on disk leaves the database remembering them until `refresh_unity`. The MCP `execute_code` tool blocks `AssetDatabase.DeleteAsset`.

## Reduced scenes for a laptop that overheats (2026-10-03)

This laptop's cooling fails under the editor (fans read 0 RPM, package 90-110 C, forced shutdowns). `FPSKit > Performance > Make Reduced Copy of Open Scene` (`FPSKitReducedCopy.cs`) saves the open arena
as `Scenes/Reduced/<Name>_Reduced.unity` and reduces the copy only (the original is saved as a copy first, never edited; a dirty scene is refused). It switches off every shadow caster and the camera's
shadow pass, keeps the twelve strongest lights with no shadows, switches particles off, removes half the trees, tiny props and LOD-grouped clutter, makes LODs drop 1.8x sooner, shortens the far plane,
clones the post-processing profile and switches off DoF/motion blur/grain/aberration/lens in the clone, drops the tier rigs, and adds `ArenaPerformance` (30 fps cap, texture mip limit 2 while playing, restored on stop).
Fairground: 4,857 -> 3,567 mesh renderers, 4,807 -> 0 shadow casters. A Reduced scene is not in the build settings, so the in-game restart cannot reload it; it is for editing and play-testing in the editor.
Shared assets are never written (the terrain asset is shared, so only the Terrain component's own distances are changed). Not measured: frame time or temperature in play.

## The shipped Night Rooftop (Night Citylife) and Fairground are the reduced versions (2026-10-03)

At Ishaan's request (after I advised against it) the reduced copies replaced the game's own `NightRooftop.unity` and `AbandonedFairground.unity`: the reduced files were copied over the originals' paths, so the
`.meta` GUIDs, the build settings and the arena catalog are unchanged. What the player gets in those two arenas: no shadow casters or camera shadow pass, the twelve strongest lights, no particles (Fairground), thinned
clutter, LODs that drop 1.8x sooner, a 160 m far plane, a cloned post profile without DoF/motion blur/grain/aberration/lens (`FPSKit_Generated/Profiles`, keep it: both scenes reference it), no `RooftopQuality`/`FairgroundQuality` rig,
and `ArenaPerformance` forcing a 30 fps cap and quarter-size textures while the scene plays (it restores the player's settings on exit). **To get the full-quality arenas back:** `git checkout facee30 -- Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity`
(and `617830b^` for `NightRooftop.unity`), or rebuild with the scene builder (`BuildScene`), which regenerates the full scene and overwrites the reduced one. The `FPSKit > Reduced` tool refuses a scene named `*_Reduced`, but the shipped scenes are not named that, so running it again would reduce them twice.
Also deleted this session (regenerable, untracked): `Build/Android` IL2CPP symbols (554 MB, stale 18 Sept build), `Build/Views`, `Build/Perf`.

**Renames (2026-10-03):** the word "Lite" is gone: `LiteSceneSettings` -> `ArenaPerformance`, `FPSKitLite` -> `FPSKitReducedCopy` (menu `FPSKit > Performance`), copies are saved as `Scenes/Reduced/<Name>_Reduced.unity`, and the two post profiles live in
`Assets/FPSKit_Generated/Profiles/`. The Night Rooftop is **Night Citylife** to the player (`LevelTheme.displayName`, as the Unknown Planet is); its key `Night Rooftop`, scene `NightRooftop.unity`, save key and campaign slot are unchanged.
The menu card and the campaign read the label when they are generated, so changing `displayName` needs the theme re-applied, `ResetCampaign` and a menu rebuild.

**Night Citylife card image (2026-10-03):** a low street-level shot down an avenue at blue hour: camera `(-72, 1.25, -1.2)` looking at `(30, 19, 8)`, FOV 56, sun exposure 1.1, rendered with
`FPSKitThumbnails.Render` (1280x720 saved at 640x360) into `Assets/UI/Thumbnails/NightRooftop/Arena.png`. The shipped scene has its lights, moon shadows and shadow casters switched off, so the render
first switched everything on **in memory only** (all 131 lights, directional shadows Soft 0.6, every renderer casting) and discarded that by opening the Menu scene; the scene file was never saved.
`Shots.asset` in that folder has `m_Script: {fileID: 0}` (the `ThumbnailShots` class lives in `FPSKitThumbnails.cs`, whose file name does not match it), so it does not load as a typed asset and
`CaptureArena` would recreate it with defaults: a pre-existing fault, so the camera above is recorded here rather than there. The dashboard card picks the image up at the next menu rebuild.

**Night Citylife in the menu (2026-10-03):** `MainMenuController` builds the cards at run time from `Arenas.asset`, so no menu rebuild is needed for a rename: the Rooftop entry's `displayName`, `description` and `preview` were set directly
(`Build Dashboard` would also do it, but it captures a preview of all seven arenas first). `ProgressKey` stays `NightRooftop`, which is the save key. The card folder follows the display name
(`Assets/UI/Thumbnails/NightCitylife/`, as `FPSKitThumbnails.CardImage(displayName)` expects); the PNG imports at 512x256 (NPOT-to-nearest), which the 16:9 card frame stretches back. A later `Build Dashboard` finds the card by that folder.

**Fixed (2026-10-03): `Shots.asset` loads now.** `ThumbnailShots` moved into its own file (`ThumbnailShots.cs`, a ScriptableObject class must live in a file of the same name), and both `Shots.asset` files had their `m_Script` patched to its GUID
(text edit, so the assets keep their identity). The earlier note about the broken reference no longer applies. Night Citylife's card camera is stored there now (`(-72, 1.25, -1.2)` -> `(30, 19, 8)`, FOV 56, exposure 1.1).
Caveat: `CaptureArena` renders the shipped scene as saved (lights, moon shadows and shadow casters off), so it will not reproduce the card exactly; the card was rendered after switching those on in memory (see above).

**Fairground card image (2026-10-03):** the Ferris wheel in silhouette against the dusk glow, framed by a big tree on the right: camera `(35, 1.8, -72)` -> `(99, 24, -26)`, FOV 55, sun exposure 1.25 (stored in `Shots.asset`). The sun sits low in the north-east (rotation 22/208), so a camera looking north-east
puts the wheel against the glow. Rendered with the same in-memory trick as the Night Citylife card (all lights, Soft moon/sun shadows, every renderer casting, particles on and simulated 14 s), then discarded by opening the Menu scene; the scene file was not saved.
Landmarks (world): Ferris wheel `(99, 18, -28)` 18x37x35, Big Top `(-20, 8, -136)`, carousel `(0, 0, -24)`, coaster `(-124, 7, -20)`, main gate `(0, 8, 147)`. Tried and dropped: tighter wheel shots (string-light wires cut across the frame),
a trunk-blocked close view, and a Big Top path view (good, kept as the runner-up: `(-20, 2, -85)` -> `(-20, 9, -136)`, FOV 55). `Shots.asset` for each arena lives in `Assets/UI/Thumbnails/<DisplayName>/`.


## Snowbound made full (2026-10-04)

Ishaan: "rebuild it like what you did on Desert". Asked once, he chose all four -- alpine village, research outpost,
wild arctic detail, mountain pass with cabins -- dense like the desert, keeping the pines, crevasses and ice caves,
igloo camps and the frozen lake. Code: `FPSKitSnowFull.cs` (kit, planning, village), `FPSKitSnowBase.cs` (outpost,
lodge), `FPSKitSnowPass.cs` (ski lift, mine, pass, cabins, camps, the wild pass), wired from `PlanSnowLife` /
`BuildSnowLife`.

- **Planning order matters; the arena is full on paper.** Lake and spawn first, then the village, then the station,
  then outpost/lodge/lift/mine/pass (`PlanSnowRest`), then the igloo camps, crevasses, caves, and last the small
  cabins and camps (`PlanSnowSmall`). Planned any other way the station, the icefall or the crevasses lose their room
  (each was lost once). `PlanCamps` now runs inside `PlanSnowLife`.
- **Nothing borrows the desert's materials** (they are null outside the desert, and null is magenta).
- **A stair beside a wall is fragile for the navmesh.** Five roofs were unreachable until three things were found,
  one by one: (1) windows/sills on the wall the stair climbs close the steps (the bake reads render meshes, collider or
  not) -- keep that wall blank; (2) a `DriftAgainst` mound with `NoStanding` on the stair marks its steps unwalkable;
  (3) **`NoStanding` on the storehouse's walls closed the last steps** (measured by disabling the modifier and
  rebaking in the open editor: 0 of 5 roofs reachable with it, 5 of 5 without). Wall tops are 0.4 m wide, too thin to
  bake anyway. Stairs are 2.6 m (`AlpStair`), wider than the desert's 1.8. A fast way to bisect a navmesh problem:
  `NavMeshSurface.BuildNavMesh()` on the scene's `NavMesh` object takes under a second in the editor.
- Visual-only tops (stall canopies, snow caps over rock) use `SnowCap` (NoStanding) so they do not bake islands.
- Solid rock masses get `SealLocal` over their footprint (the terrain under a solid box otherwise bakes unreachable).
- VerifyReach Snowbound: 0.0 % stranded (was 1.9 %); VerifyTerrain clean. VerifyZone's two gorge messages do not
  apply to a river-less arena.

### Snowbound detail pass (2026-10-04, later the same day)

Ishaan: village denser; check the Game view; the crevasse area was broken; houses and cars realistic; "use more and
more triangles ... ground, snow, trees, houses, cars". What changed, and why:

- **Ground is 1.5 m (was 3 m) for snow only** (`_groundStep`), with wind-packed sastrugi ridges in `DuneHeightAt`. The
  relaxation sweeps and smoothing passes scale with the step so their reach in metres stays the same. 253k triangles.
- **Pines are built branch by branch** (`Pine`, ~1200 triangles each instead of ~140): flared leaning trunk, root
  buttresses, 6-7 whorls of drooping boughs, a snow load on most. **Chalets** (`FPSKitSnowLogs.cs`): round logs with
  notched, overhanging corners (`LogBox`), stone course (`StoneBase`), shingle rows + ridge cap + fascia + board gables
  + lumpy snow blanket (`GableRoof`), balcony with balusters on two-storey ones. Logs are render-only; each builder
  keeps a thin core wall for collision and the bake. **Vehicles** (`FPSKitSnowVehicles.cs`): groomer with real tracks,
  glazed cab, blade and tiller; crew-cab pickup; snowmobile; 6x6 truck. Detail materials (normal maps) throughout.
- **The "broken ground" was the crevasse.** The terrain mesher cuts every cell the crack touches *plus a margin*, so the
  hole runs a few metres past the crack's ends and sides; the 12 m walls did not cover it and the **sky showed through
  as yellow slabs**, with a 6 m blue ice blanket hiding the rest. Now there is a floor under the whole hole (5 m past
  each end), a thin snow cornice of 1.1 m strips sampled from the ground (and a grid over each end) in place of the
  blue skin, and the cornice carries the `Snow` tag.
- Reading a render mesh's bake effect: stranded samples after the finer ground are 0.3 % (tiny pockets), under the 2 %
  limit. The scene is now ~160 MB and **Git LFS** (see `.gitattributes`), like the desert's.

### Snowbound: bigger, denser, finer, filled, and a sky (2026-10-04, third pass)

Ishaan, still unhappy after the detail pass: the crevasse area ("slides"), the frozen lake, the glacier ice and the ski
lift ("rides") were poor; "make all objects' triangles at least double"; houses far too small ("I can run round them in
under a second"); "fill some spaces ... fencing or objects"; "make the skybox better and accurate".

- **Houses are about 1.7x the footprint** (chalets 11.5-16 m wide, 13-18 m deep, lots 18 m, lanes 4.2 m; church 12x21; clock
  tower 8 m; lodge hall 25x17; outpost laid out at `OutK` 1.15 with 11.5 x 26 m barracks; cabins 20 m lots). Space is the
  limit: the village picks the largest radius that fits from 92 m down, and the outpost/lodge/pass/cabin sites were
  trimmed until all fit (an outpost, lift or pass that "finds no room" is a planning fault, check the log).
- **Triangles: 1.83 M -> 3.9 M.** Ground 1 m cells (the slope relaxation needs the *square* of the cell ratio in sweeps,
  not the ratio); boulders and buttes at four times the facets (`IsSnowArena`); a lake of 5,700 triangles with fissures,
  heaved plates and wind-snow; crevasse walls in 0.8 m by 0.9 m cells narrowing with depth with ledges and icicles; ice
  caves from a grid of blocks (7,500 each, were 40); icefall curtain 22 by 36 cells; igloos in 24 courses; and everything
  built with `MeshBuild` and not individually rebuilt is split in two at `ToMesh` (`SnowBisect`, exactly 2x, **no new
  shape** -- say so when reporting). Butte ledges are spaced in proportion for snow (the desert is unchanged).
- **Mountains** are ridged-noise massifs (`BuildSnowMountains`, 64 x 64 cells, rock below the snowline, snow above, steep
  faces stay rock), not pale buttes.
- **Fill** (`FPSKitSnowFill.cs`): picket fences on three sides of village lots (front open), wire and snow fences,
  barrels, pallets, tarped crates, hay bales, benches, signposts, trail markers, snowmen, jersey barriers, floodlights,
  ice-fishing huts. Leave the storehouse lots clear: a prop or fence at a stair foot cut the navmesh again (6 roofs).
- **Sky** (`FPSKitSnowSky.cs`): a painted 4096 x 2048 equirectangular texture on Unity's Skybox/Panoramic, because the
  procedural sky at a low sun reddens the whole horizon like a desert sunset. Pale horizon equal to the fog colour, cold blue
  zenith, wide warm glow, cirrus streaked along the wind, broken altocumulus lit from the sun's side, a 22-degree halo and
  sundogs. The sun in the picture is placed from the theme's light angle. Regenerated only when the sun, fog or recipe change.
- Checks on the verified build: VerifyReach 0.1 % stranded, VerifyTerrain clean, all 8 storehouse roofs reachable.

### Desert houses made bigger (2026-10-04)

Ishaan, after Snowbound: "make houses bigger for desert also". `BuildSolidHouse` / `BuildEnterableHouse` now scale with their lot
(the old 11 m lot is the unit, `sc = max(1, lot / 11)`): village lots 16 m (lane 4.5, streets 11 and 8 m), hamlet lots 14 m,
farm house lots 15 m, with the hamlet, farm and village sites enlarged to match (village radius 96 down to 72, whichever fits).

The same three navmesh faults as Snowbound's storehouses were found, and fixed the same way, in `BuildEnterableHouse`: **every
roof was cut off from the ground** (the reach test only samples, so it had reported 0 stranded for the village): (1) NoStanding on the
walls beside the roof stair, (2) open-window frames on the +u wall the stair climbs, (3) a 1.8 m flight. Now: walls left standing,
windows on faces 0 and 2 only, and `AlpStair` at 2.6 m. Check roof reach directly (`NavMesh.CalculatePath` from the spawn to each
roof) and do not trust the sampled percentage. Result: 27 of 27 roofs reachable, VerifyReach 0.2 %, VerifyTerrain and VerifyZone clean.

## Unknown Planet rebuilt from scratch (2026-10-04)

Theme key stays "Mars Colony" (displayName Unknown Planet). `FPSKitUnknown.cs` holds all of it: basalt-column causeways instead of bridges (no human-made crossing), rim boulders instead of fences, monolith rings, column hills, crystal groves, ridged alien massifs, painted red-sun sky (`UnknownSky`, recipe `alien-sky-v1`), 1.5 m ground and fine meshes (`IsFineArena`).
- Lava is the shader-graph asset `Assets/Shaders/Lava/Lava.mat`, copied per role by `ShaderLava(role, uvPerMetre)`. The graph goes NaN on negative UVs and loses precision on big ones: the river maps at 0.01 UV/m with +4 added (`BuildRiver`), never plain world metres.
- Emissive HDR values over about (3, 0.5, 0.08) wash to pale yellow under the grade; crystals 1.4, glyphs 3.0, veins 2.2.
- Do not put `NoStanding` on the causeway sides: the volume reaches the top plane and cut the walkable tops out of the bake (far bank unreachable). Reach 0.4 %, Zone clean.

Unknown Planet, second pass (2026-10-04): the white and black blobs on screen were lava meshes with bad UVs (pools/tongues centred on zero = negative, fissures in world metres out to 150). `LavaUv` shifts every pool/tongue/fissure mesh so its smallest UV is (1,1). Do not raise `_NoiseAmount`/`_VoronoiAmount` on the river: 1.7/-0.12 turned it black. Volcanoes ×2.3 wide (landmarks), backdrop ×1.5, vents ×1.8, boulders ×1.8, monoliths/crystals/column hills larger; Saturn removed, sun is 11° across with longer prominences (`alien-sky-v2`); spike fields (`BuildSpikeFields`), ~2× fissures and pits, taller tumuli for a random, hostile surface. Reach 0.7 %.

Unknown Planet, third pass (2026-10-04): the big cream blobs while playing were the LavaGlow smoke puffs, not the muzzle flash: unlit white-orange (0.95,0.46,0.24) particles, 10-20 m across, under bloom 1.6. Now dark red ash, lower alpha, camera near fade (6-22 m), bloom 0.9, embers ×2.2 instead of ×4. Lava shader speeds had been ~0.0004 tiles/s (a standing pool); they are now absolute tiles/s (flow 0.14 down the river), `_Smoke_Int` and `_BackGround_Strenght` 0, Fresnel/background colours orange not white, smoothness 0.3 for wet glints (0.85 mirrored the sky and washed the lava pink).

Unknown Planet, fourth pass (2026-10-04): the cream blobs with a black streak were NaN pixels. `ApronWater`, the lava sheet beyond the arena (112 x 2650 m), had UVs from -79 to +79 and the lava graph returns NaN on negative UVs; Bloom spread each NaN across half the screen. Fixed by shifting the sheet's UVs positive (`LavaUv` in `Backdrop`) and giving it the river's 0.01 mapping. Belt and braces for every arena: `PlayerMotor.Start` sets the camera's `stopNaN`, the builder sets it too, and every Bloom now has `clamp` 8. When a screenshot shows a blob with a black streak, look for a mesh with negative UVs on a lava or shader-graph material before touching bloom.
