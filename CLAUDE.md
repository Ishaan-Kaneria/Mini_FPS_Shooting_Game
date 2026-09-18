# CLAUDE.md

Level-based FPS built on **Unity 6000.6.0f1** with the **Universal Render Pipeline** (com.unity.render-pipelines.universal 17.6.0). Six arenas, a ladder of eight levels in each: a fixed crowd of NavMesh-driven enemies, a strict clock, and one to three stars. Clearing a level unlocks the next.

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
| Feedback | `HUDController.cs`, `LevelResultsUI.cs` (the stars screen), `Minimap.cs` (the map in the corner), `MinimapMarker.cs`, `EnemyHealthBar.cs`, `DamageNumber.cs`, `Pickup.cs`, `TransientFlash.cs` (shrinks a spawned flash out of sight), `OneShotAudio.cs` (pooled positional one-shots) |
| UI       | the touch stack: `TouchControls.cs`, `TouchButton.cs`, `TouchLookArea.cs`, `VirtualJoystick.cs`, `MobileInput.cs` |
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

- **FPSKit > Build Scene > [Industrial Warehouse | Desert Outpost | Snowbound Station | Night Rooftop | Abandoned Subway | Mars Colony]** — each calls `BuildScene(themeName)`.
- **FPSKit > Build Scene > From Selected Theme Asset** — builds from whatever `LevelTheme` is selected in the Project window, built-in or not. This is how a new arena gets made without touching code.
- **FPSKit > Build Scene > Build ALL Themes** — builds and saves one scene per theme, registering them in Build Settings so the in-game restart works.
- **FPSKit > Add Gameplay To Current Scene** — the non-destructive path: injects player, enemies, level manager, HUD, post-FX and a baked NavMesh into an existing level, leaving geometry and baked lighting alone. Refuses to run twice (bails if a `Player`-tagged object exists).

**`BuildScene` is destructive.** It calls `EditorSceneManager.NewScene(..., NewSceneMode.Single)` and constructs the entire scene from scratch — lighting, skybox, arena geometry, player rig, weapon data, enemy prefab, spawn points, baked NavMesh, level manager, HUD, results screen, post-processing — then saves over `Assets/FPSKit_Generated/Scenes/<Theme>.unity`. Any hand-tuning done in the Unity editor to a generated scene is lost on the next build.

So: **fix scene content by editing the builder, not the `.unity` file.** Hand-editing a generated scene is only appropriate for a throwaway experiment. The same applies to the generated assets it writes (`Controls.asset`, `Enemy.prefab`, themes, materials) — the builder and `FPSKitThemes` recreate or re-dirty them.

Other editor tools: `FPSKitThemes.cs` (creates/resets `LevelTheme` assets), `FPSKitLevels.cs` (creates/resets `LevelSet` assets), `FPSKitEnemyRoster.cs` (creates/resets `EnemyArchetype` assets), `FPSKitStore.cs` (creates/resets the `StoreCatalog` and its stock), `FPSKitArtTools.cs` (**FPSKit > Art Pack Setup**), `FPSKitEnemySetup.cs` (**FPSKit > Enemy Setup**), `FPSKitMobileControls.cs` (**FPSKit > Add Mobile Touch Controls**), `ControlSettingsEditor.cs` (custom inspector with control presets), `FPSKitGraphics.cs` (the render settings that live on the pipeline asset rather than in any scene, applied alongside `EnsureProjectTagsAndLayers`), `FPSKitAudioImportPolicy.cs` (stamps import settings on a clip the moment it lands under `Assets/Audio/`).

The headless side is `FPSKitBatch.cs`, which exposes the builder and the tests as public `-executeMethod` entry points because the menu items are private. It is what `Tools/unity-batch.sh` calls, and the eight checks behind it are `FPSKitControlsTest.cs` (`VerifyControls`, which audits every binding in every preset for collisions and then plays a level driving each action in turn), `FPSKitPlayTest.cs` (`VerifyReplay`), `FPSKitLevelTest.cs` (`VerifyLevels`, which plays one level to a failure and then to a three-star clear), `FPSKitStoreTest.cs` (`VerifyStore`, which buys a gun and two upgrades and then checks both reached the player), `FPSKitCombatTest.cs` (`VerifyCombat`), `FPSKitBombTest.cs` (`VerifyBomb`, which sweeps the aim a degree at a time, holds the key for real and turns the view under it, throws at both ends of the range and listens), `FPSKitFlowTest.cs` (`VerifyFlow`) and `FPSKitStaticProbe.cs` (`VerifyStatics`, which finds statics by reflection, so a new class with one is audited without being registered anywhere).

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

**Three generators, three reset entry points, one trap.** `FPSKitBatch.ResetEnemyArchetypes`, `ResetLevelSets` and `ResetStore` exist because the roster, the level ladders and the store are all generated once and then left alone. Retuning a number in `FPSKitEnemyRoster.Configure`, `FPSKitLevels.Configure` or `FPSKitStore.Configure` does **not** reach the assets the game reads until the matching reset is run. A price or a level clock changed in code and never reset is a change that compiles, builds and ships without doing anything.

`FPSKitCombatTest` and `FPSKitLevelTest` both retune the live `LevelSet` while they run -- one lengthening the clock so the fight outlives it, the other shortening it so the test does not sit through a full level -- and both put it back in `Detach`. The set is a shared asset: a test that left level one with a four-minute clock would be a test that broke the game to pass.

## The web build has its own page

`Assets/WebGLTemplates/FPSKit/index.html` is the page a browser player actually sees, and `FPSKitBatch.ConfigureWebGL` selects it with `PlayerSettings.WebGL.template = "PROJECT:FPSKit"`. `BuildWebGL` throws if the folder is missing rather than let the build fall back to Unity's stock page, which is a 960×600 canvas in the corner of a white document titled "Unity Web Player", with no statement of the controls and an `alert()` for a loading failure.

`Build/WebGL/` is output. Editing the `index.html` in there fixes nothing past the next build — **change the template**, the same way scene fixes go in the builder rather than the `.unity` file.

The page owns what only the browser can answer: pointer/keyboard focus, suppressing the context menu over the arena, capping `devicePixelRatio` on phones, and the `(any-pointer: coarse)` test behind `Assets/Plugins/WebGL/FPSKitWebDevice.jslib`, which `WebDevice.IsTouchOnly` reads. That test exists because `Application.isMobilePlatform` on WebGL is a user-agent match an iPad fails — it has called itself a Macintosh since iPadOS 13 — so `TouchControls` would hide the on-screen controls on the one device with no other way to play.

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
  than admit there was nowhere to go. `GameDirector.CanQuit` now only governs the
  dashboard's **Exit Game** button, which on the web hands over to `WebDevice.Exit` and
  the `FPSKitExit` jslib export — it closes the tab where the browser allows it and
  otherwise replaces the page with a sign-off, because `Application.Quit` there just
  leaves a dead canvas.
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
  while every structural check still counted six. `ArenaCard` animates `localScale`
  instead, and `VerifyFlow` fails if two cards share a position.
- **Cell sizes are computed, not fixed.** A fixed cell is only right at one aspect ratio:
  three 404px cards fit 16:9 and slide under the record panel at 4:3, and a browser window
  is whatever shape the player left it. Card internals are anchored as fractions for the
  same reason, and the text auto-sizes.

TMP has no closing `</alpha>` tag — `<alpha=#99>` applies from where it appears. Writing
one prints those eight characters on screen, which is what the instruction strip and the
dashboard hint both did. Use `<color=…></color>`; `VerifyFlow` checks for it.

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

## Enemies fight back, and the fight is legible

Three rules hold the combat model together. They are cheap to break by retuning one
number in isolation, so they are written down.

**Reach has to be shared with the brake.** A `NavMeshAgent` stops a full
`stoppingDistance` short of its destination. Sending a melee enemy to a point "just
inside its attack range" therefore parks it that much *outside* the range, the attack
check never passes, and the whole level gathers around the player and does nothing --
silently, with no error and every build check green. `EnemyAI.DesiredStandOff` now
subtracts the brake before choosing a destination, and the prefab's `stoppingDistance`
is 0.8 rather than 1.5 so there is less of it to pay for. `VerifyCombat` is the
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
  fallen too far below the player, or whose agent has left the NavMesh. Discarding
  deliberately awards no score, no combo and no drop — it is not a kill. It *does* put
  a fresh enemy in the queue, bounded at one replacement per enemy in the level, which
  is new and is the point: a level asks for a fixed number of kills and scores the
  player against it, so a body that fell down a hole is not theirs to pay for.
- **The clock** (`RunLevel`) ends the level regardless of survivors and scores it on
  what was actually killed.

`despawnDistance` is force-raised at `Start` if it is not comfortably clear of
`maxSpawnDistanceFromPlayer`, because a leash inside the spawn ring deletes enemies on
arrival and presents as "nothing spawns".

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

## Project conventions

- The builder provisions tags `Player`, `Enemy`, `Concrete`, `Metal`, `Wood`, `Flesh` and layers `Player`, `Enemy`, `Environment` via `EnsureProjectTagsAndLayers()`. New surface types or layers must be added there, not just in the Tag Manager UI.
- Surface tags drive `ImpactLibrary` decal/FX lookup on bullet hits.
- Enemy navigation uses `com.unity.ai.navigation` (`NavMeshSurface`), baked at build time by the builder.
- Tunables are `[Header]`-grouped public fields with `[Tooltip]`s written as plain prose. Match that style — the existing XML doc comments explain *why* a knob exists, not just what it is.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are gitignored; `.meta` files are committed and must stay in sync with their assets.
- Placeholder audio is synthesised by `Tools/generate-placeholder-audio.py` (stdlib only). It draws from one seeded random stream, so a new clip that uses noise must save and restore `random.getstate()` around itself or every sound authored below it is re-rolled into an identical-sounding but byte-different file.
- Arena previews (`FPSKit_Generated/Previews/`) are rendered from the built scenes by the dashboard builder. Render them through `RenderPipeline.SubmitRenderRequest`, not `Camera.Render()` — the latter predates scriptable pipelines and under URP returns a frame with the skybox and essentially no lighting, so every arena comes back as black silhouettes. Call `DynamicGI.UpdateEnvironment()` after opening a scene and submit twice, or the first arena captured is lit by nothing while the rest look right.
- **If a build suddenly has no sound, suspect `Library/` before the files.** A corrupted asset database makes Unity import every `.wav` as a `DefaultAsset` rather than an `AudioClip`, with no error logged anywhere; the builder then writes null into every audio slot and saves a mute scene over a working one. Closing Unity and deleting `Library/` fixes it. `FPSKitSceneBuilder.Clip` now warns per clip and `ReportMissingClips` sums it up at the end of a build, so this is loud rather than silent.
