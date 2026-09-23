# CLAUDE.md

Level-based FPS built on **Unity 6000.6.0f1** with the **Universal Render Pipeline** (com.unity.render-pipelines.universal 17.6.0). **Seven arenas played as a campaign**, each with a ladder of eight levels -- except the finale, which has three. A level is a fixed crowd of NavMesh-driven enemies, a strict clock, an objective, and one to three stars. Clearing a level unlocks the next; clearing a zone's last level puts down the child who holds it and opens the next zone.

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
| Dashboard | `MainMenuController.cs`, `ArenaCard.cs`, `ArenaCatalog.cs` (ScriptableObject), `LevelSelectPanel.cs`, `LevelButton.cs` |
| Machinery | `MachineTravel.cs` (crane trolleys), `MachineSpin.cs` (roof fans) -- position from the clock, no state, no collider |
| Feedback | `HUDController.cs`, `LevelResultsUI.cs` (the stars screen), `Minimap.cs` (the map in the corner), `MinimapMarker.cs`, `EnemyHealthBar.cs`, `DamageNumber.cs`, `Pickup.cs`, `TransientFlash.cs` (shrinks a spawned flash out of sight), `OneShotAudio.cs` (pooled positional one-shots), `ScrollingWater.cs` (drags the river's texture along it) |
| UI       | the touch stack: `TouchControls.cs`, `TouchButton.cs`, `TouchLookArea.cs`, `VirtualJoystick.cs`, `MobileInput.cs` |
| Achievements | `Achievements.cs` (twenty thresholds, all derived), `PlayerStats.cs` (the lifetime counters they read), `AchievementsPanel.cs`, `AchievementRow.cs` |
| Instructions | `InstructionsPanel.cs` (written from the live bindings), `InstructionRow.cs` |
| Screens  | `OverlayPanel.cs` (the base every overlay shares), `UITheme.cs` (colour, type and motion tokens), `DeviceProfile.cs` (reach, form, orientation), `PhoneUI.cs` (the one-line question, delegating to it) |
| Campaign | `Campaign.cs` (the player's side: identity, powers, beats read, the one gate), `CampaignData.cs` (ScriptableObject: the zones in order, the eight names, the beats), `SaveMigration.cs` (the one-time wipe), `StoryPanel.cs` (the opening and the beat cards), `DossierPanel.cs` / `DossierRow.cs` (THE LIST) |
| Objectives | `LevelObjective.cs` (the six, and the invariant that none of them may end a level) |
| Config   | `ControlSettings.cs`, `LevelTheme.cs` |

`Assets/FPSKit_Generated/` holds tool output: generated scenes, themes, `Levels/` (LevelSet assets), `Enemies/` (EnemyArchetype assets), `Store/` (the catalog plus its WeaponData, BombData and ConsumableData), materials, `Controls.asset`, `TestRifle.asset`, `ImpactLibrary.asset`, `MinimapBlip.png`, `Enemy.prefab`, `Bomb.prefab`, `Explosion.prefab`, `Pickup_*.prefab`, post-FX volume profiles. Treat everything in it as regenerable. Art comes from `Assets/RPG_FPS_game_assets_industrial/`.

## Input: legacy only

Gameplay reads the **legacy `UnityEngine.Input` API** (`Input.GetKey`, `Input.GetAxisRaw("Mouse X")`). Do not port code to the new Input System.

Caveats worth knowing before you touch input:

- The project's `activeInputHandler` is `2` (**Both**), and `com.unity.inputsystem` is installed as a package dependency. That is deliberate — it keeps the legacy API alive while allowing one optional path.
- The single exception is `PlayerMotor.ReadMouseCounts()`, which has a `#if ENABLE_INPUT_SYSTEM` branch using `Mouse.current.delta` for raw, unsmoothed mouse look when `rawMouseInput` is on, and falls back to `Input.GetAxisRaw`. Keep both branches working — and note **the fallback is reached on an empty reading, not only on a missing package.** On some platforms, Linux/X11 most reliably, `Mouse.current.delta` reports zero on every frame the cursor is locked while `Mouse.current` itself is present and every other control on it works. Written as an unconditional `return`, that is a game with no mouse look at all: live device, locked cursor, focused window, sixty frames a second, nothing logged. It took a diagnostic reading 296 consecutive frames of `LookDeltaDegrees` at exactly zero to see it, because from the outside it is indistinguishable from a player who is not touching the mouse. Consulting the legacy axis on a frame the Input System says the mouse did not move cannot double-count, and it must stay `GetAxisRaw` — the smoothed axis keeps reporting after the mouse stops, which would turn the view on frames that really were still.
- `Assets/InputSystem_Actions.inputactions` is leftover from the Unity template and is not referenced by any gameplay code.

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

- **FPSKit > Build Scene > [Industrial Warehouse | Desert Outpost | Snowbound Station | Night Rooftop | Abandoned Subway | Mars Colony | The Auger House]** — each calls `BuildScene(themeName)`.
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

`FPSKitSceneBuilder` is partial across several files, all of them the same class: `FPSKitOpenZone.cs` (where the open-zone arena's pieces go), `FPSKitDesert.cs` (what they are made of), `FPSKitTerrain.cs` (the heightfield), `FPSKitIndustrial.cs` and `FPSKitIndustrialParts.cs` (the plant and its fittings), `FPSKitFactory.cs` (the big industrial structures -- halls, chimneys, silos, cooling towers, cranes, and what the ground is wearing), `FPSKitIndustrialDense.cs` (narrow roads, walled compounds and gates, street tunnels, and the buildings packed against the walls), `FPSKitIndustrialWorks.cs` (roof routes, walk-in warehouses, the railway siding, silo conveyors, roof fans), `FPSKitMeshKit.cs` (procedural meshes, noise, and the `Hide`/`NoStanding`/`NoEntry`/`SealNavMeshOutside`/`Mark` bookkeeping every generated object needs) and `FPSKitPark.cs` (the abandoned fairground: hollows, shafts, rides and what stands between them), `FPSKitVolcanic.cs` (the Unknown Planet's dressing: sky, cones, vents, lava), `FPSKitLavaField.cs` (its ground: flows, pits, and everything draped on the plain) and `FPSKitTextures.cs` (the detail maps). Splitting is not tidiness -- they share the whole rest of the builder, so a split anywhere else would mean passing half of it around.

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

- **Esc or P** pause. Both, because a browser takes Escape to release pointer lock, so a
  web player pressing it gets their cursor back and no menu.
- **R** resume. **Q** leaves the level.
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
cranes and pipe bridges, and the pack's sheds are demoted to the outbuildings they are
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
spills and markings. All of it is faded and close in value to what it sits on -- the
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
six arenas, and `maxSpawnDistanceFromPlayer` already clamps to 80m on a wide arena, so
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

**WebGL's quality tier is chosen at runtime, because it is the one platform that cannot be
told in advance.** `QualitySettings` has one entry per platform and WebGL's was 0 -- the
Mobile tier, written to be cheap -- so every desktop browser player was getting reduced
render scale and no MSAA as well. `RenderPolicy` picks the PC tier for a pointer and the
Mobile tier for a finger, before the first scene loads, compiled into the player only: calling
`SetQualityLevel` in the editor writes the project's current tier to disk as a side effect of
pressing Play. The Mobile tier's own render scale is back at 1.0; the pixel budget now lives
in the page, where the screen is known.

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

## The dashboard has five destinations

`HOW TO PLAY`, `THE LIST`, `ACHIEVEMENTS`, `STORE` and `EXIT GAME`, built as a `HorizontalLayoutGroup`
rather than four anchored offsets -- the old pair sat at fixed pixels from the right edge
and a third and fourth would have reached 1,120px in, fine at one window width and off the
screen at a narrower one. Every one of them is in `dashboardOnly`, because an overlay is a
full-screen raycast target and a button left switched on behind one is drawn and
unreachable.

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

`LevelTheme.parkZone` is the fourth layout mode, and Abandoned Subway is built with it
at 450x450 -- an abandoned fairground at night, with hollows in the ground and shafts in
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
  floor and of a heightfield, so true of all six arenas -- and false for any layout whose
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

The order is Warehouse, Snowbound, Desert, Rooftop, Subway, Mars, The Auger House, and
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
  `LevelProgress.IsUnlocked` and for the same reason. A zone the campaign does not list,
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
