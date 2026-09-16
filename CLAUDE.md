# CLAUDE.md

Wave-survival FPS built on **Unity 6000.6.0f1** with the **Universal Render Pipeline** (com.unity.render-pipelines.universal 17.6.0). The player holds a rebindable arena position against endless, escalating waves of NavMesh-driven enemies.

## Layout

Scripts live in `Assets/Scripts`, split into exactly two folders:

- `Assets/Scripts/Runtime/` — all gameplay MonoBehaviours and ScriptableObjects, flat (no subfolders), **global namespace**.
- `Assets/Scripts/Editor/` — editor-only tooling under `namespace FPSKit.EditorTools`, every file wrapped in `#if UNITY_EDITOR`. The folder name `Editor` is what keeps this code out of player builds; there are no `.asmdef` files anywhere, so moving these files breaks the build.

There is no `Core/`, `Player/`, `Weapons/`, `Enemies/` or `UI/` folder — those concerns are separated by file, not by directory:

| Concern  | Files |
|---|---|
| Player   | `PlayerMotor.cs` (CharacterController move/look/recoil/shake), `WeaponSway.cs` |
| Weapons  | `Weapon.cs`, `WeaponData.cs` (ScriptableObject, `FireMode` Single/Burst/Auto), `ImpactLibrary.cs`, `TracerProjectile.cs` (flies its own tracer, so the shot outlives whoever fired it) |
| Enemies  | `EnemyAI.cs` (NavMeshAgent, `State` Idle/Chase/Attack/Stagger/Dead), `EnemyArchetype.cs`, `Health.cs`, `Hitbox.cs`, `RagdollController.cs`, `EnemyLimbAnimator.cs` (swings the limbs off agent velocity -- there is no AnimatorController anywhere in the project) |
| Waves    | `WaveManager.cs` — endless spawner, per-wave growth, boss waves, `WaveModifier`, golden-angle ring spawns that avoid the player's view, an enemy leash and a wave clock so no wave can stall |
| Run state | `GameDirector.cs` — score, combo, pause, game over, restart, PlayerPrefs records |
| Feedback | `HUDController.cs`, `EnemyHealthBar.cs`, `DamageNumber.cs`, `Pickup.cs`, `TransientFlash.cs` (shrinks a spawned flash out of sight), `OneShotAudio.cs` (pooled positional one-shots) |
| UI       | the touch stack: `TouchControls.cs`, `TouchButton.cs`, `TouchLookArea.cs`, `VirtualJoystick.cs`, `MobileInput.cs` |
| Config   | `ControlSettings.cs`, `LevelTheme.cs` |

`Assets/FPSKit_Generated/` holds tool output: generated scenes, themes, `Enemies/` (EnemyArchetype assets), materials, `Controls.asset`, `TestRifle.asset`, `ImpactLibrary.asset`, `Enemy.prefab`, `Pickup_*.prefab`, post-FX volume profiles. Treat everything in it as regenerable. Art comes from `Assets/RPG_FPS_game_assets_industrial/`.

## Input: legacy only

Gameplay reads the **legacy `UnityEngine.Input` API** (`Input.GetKey`, `Input.GetAxisRaw("Mouse X")`). Do not port code to the new Input System.

Caveats worth knowing before you touch input:

- The project's `activeInputHandler` is `2` (**Both**), and `com.unity.inputsystem` is installed as a package dependency. That is deliberate — it keeps the legacy API alive while allowing one optional path.
- The single exception is `PlayerMotor.ReadMouseCounts()`, which has a `#if ENABLE_INPUT_SYSTEM` branch using `Mouse.current.delta` for raw, unsmoothed mouse look when `rawMouseInput` is on. It falls back to `Input.GetAxisRaw` otherwise. Keep both branches working.
- `Assets/InputSystem_Actions.inputactions` is leftover from the Unity template and is not referenced by any gameplay code.

Bindings are not hard-coded: they live in the `ControlSettings` ScriptableObject (`FPSKit_Generated/Controls.asset`). What ships there is the `StandardFPS` preset — **arrows and W/A/S/D both move, left click fires, right click aims, Space jumps, Shift sprints, Ctrl/C crouch, R reloads** — and `ControlSettings.Preset` also carries `ArrowsAndMouse` and `ArrowsAndSpace` (arrows move, Space fires, double-tap Space sprints, click jumps), applied from the custom inspector. Read new keys through `ControlSettings` helpers rather than calling `Input.GetKey` with a literal `KeyCode`.

## The scene builder regenerates everything

`FPSKitSceneBuilder.cs` (~1830 lines) drives the **FPSKit** menu:

- **FPSKit > Build Scene > [Industrial Warehouse | Desert Outpost | Snowbound Station | Night Rooftop | Abandoned Subway | Mars Colony]** — each calls `BuildScene(themeName)`.
- **FPSKit > Build Scene > From Selected Theme Asset** — builds from whatever `LevelTheme` is selected in the Project window, built-in or not. This is how a new arena gets made without touching code.
- **FPSKit > Build Scene > Build ALL Themes** — builds and saves one scene per theme, registering them in Build Settings so the in-game restart works.
- **FPSKit > Add Gameplay To Current Scene** — the non-destructive path: injects player, enemies, wave manager, HUD, post-FX and a baked NavMesh into an existing level, leaving geometry and baked lighting alone. Refuses to run twice (bails if a `Player`-tagged object exists).

**`BuildScene` is destructive.** It calls `EditorSceneManager.NewScene(..., NewSceneMode.Single)` and constructs the entire scene from scratch — lighting, skybox, arena geometry, player rig, weapon data, enemy prefab, spawn points, baked NavMesh, wave manager, HUD, post-processing — then saves over `Assets/FPSKit_Generated/Scenes/<Theme>.unity`. Any hand-tuning done in the Unity editor to a generated scene is lost on the next build.

So: **fix scene content by editing the builder, not the `.unity` file.** Hand-editing a generated scene is only appropriate for a throwaway experiment. The same applies to the generated assets it writes (`Controls.asset`, `Enemy.prefab`, themes, materials) — the builder and `FPSKitThemes` recreate or re-dirty them.

Other editor tools: `FPSKitThemes.cs` (creates/resets `LevelTheme` assets), `FPSKitEnemyRoster.cs` (creates/resets `EnemyArchetype` assets), `FPSKitArtTools.cs` (**FPSKit > Art Pack Setup**), `FPSKitEnemySetup.cs` (**FPSKit > Enemy Setup**), `FPSKitMobileControls.cs` (**FPSKit > Add Mobile Touch Controls**), `ControlSettingsEditor.cs` (custom inspector with control presets), `FPSKitGraphics.cs` (the render settings that live on the pipeline asset rather than in any scene, applied alongside `EnsureProjectTagsAndLayers`), `FPSKitAudioImportPolicy.cs` (stamps import settings on a clip the moment it lands under `Assets/Audio/`).

The headless side is `FPSKitBatch.cs`, which exposes the builder and the tests as public `-executeMethod` entry points because the menu items are private. It is what `Tools/unity-batch.sh` calls, and the three checks behind it are `FPSKitPlayTest.cs` (`VerifyReplay`), `FPSKitWaveTest.cs` (`VerifyWaves`) and `FPSKitStaticProbe.cs` (`VerifyStatics`, which finds statics by reflection, so a new class with one is audited without being registered anywhere).

## The web build has its own page

`Assets/WebGLTemplates/FPSKit/index.html` is the page a browser player actually sees, and `FPSKitBatch.ConfigureWebGL` selects it with `PlayerSettings.WebGL.template = "PROJECT:FPSKit"`. `BuildWebGL` throws if the folder is missing rather than let the build fall back to Unity's stock page, which is a 960×600 canvas in the corner of a white document titled "Unity Web Player", with no statement of the controls and an `alert()` for a loading failure.

`Build/WebGL/` is output. Editing the `index.html` in there fixes nothing past the next build — **change the template**, the same way scene fixes go in the builder rather than the `.unity` file.

The page owns what only the browser can answer: pointer/keyboard focus, suppressing the context menu over the arena, capping `devicePixelRatio` on phones, and the `(any-pointer: coarse)` test behind `Assets/Plugins/WebGL/FPSKitWebDevice.jslib`, which `WebDevice.IsTouchOnly` reads. That test exists because `Application.isMobilePlatform` on WebGL is a user-agent match an iPad fails — it has called itself a Macintosh since iPadOS 13 — so `TouchControls` would hide the on-screen controls on the one device with no other way to play.

## Content is data, not code

Two ScriptableObject types are the extension points, and both exist so that adding content never means editing the builder:

- **A new arena is a `LevelTheme` asset.** Duplicate one in `FPSKit_Generated/Themes/`, retune it, select it, then **FPSKit > Build Scene > From Selected Theme Asset**. The six named menu entries are just shortcuts to the built-in assets.
- **A new enemy is an `EnemyArchetype` asset.** Duplicate one in `FPSKit_Generated/Enemies/`, change the numbers, add it to the WaveManager's roster. There is one base `Enemy.prefab`; an archetype is *stamped onto* an instance at spawn (stats, scale, colour via `MaterialPropertyBlock`, behaviour, score, drops). `EnemyArchetype.Role` decides whether it joins the normal mix, counts as an elite, or is drawn only for boss waves.

When adding either, prefer a new asset over a new branch in the builder. If a knob genuinely does not exist yet, add it to the ScriptableObject — not to `FPSKitSceneBuilder`.

## Domain reload is OFF — every static needs a reset hook

`ProjectSettings/EditorSettings.asset` has `m_EnterPlayModeOptionsEnabled: 1` and `m_EnterPlayModeOptions: 3`, which is `DisableDomainReload | DisableSceneReload`. Entering play mode is fast, and **no C# static is ever reset between play sessions.**

That makes a specific bug class very easy to write and very hard to spot: the game works the first time you press Play and is dead the second, while every compile and build check still passes. A gate left false, a cached singleton pointing at a destroyed object, a shutdown flag set by the `OnApplicationQuit` that fires when play mode exits.

So: **any static field that carries state must be cleared in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hook on its own class.** That hook runs before the first scene loads on every play session, with or without a domain reload. The classes that currently own one are `PlayerMotor`, `GameDirector`, `DamageNumber`, `EnemyHealthBar` and `MobileInput`. `Time.timeScale` is not a static but persists the same way, and `GameDirector` resets it in the same hook.

`Tools/unity-batch.sh FPSKitBatch.VerifyReplay` is the regression test: it plays two real sessions back to back, ends the first on the game over screen (the messiest state a player can leave), and fails if the second does not start clean.

### The sibling trap: non-serializable fields after a mid-play reload

Statics are one half. The other is that **`ScriptChangesDuringPlayOptions` is unset**, so it sits at its default *Recompile And Continue Playing* — edit any script while play mode is running and Unity reloads the domain underneath the running game **without re-running `Awake`**.

Unity's reload backup carries a field across only if its *type* is serializable, and it does this for private fields too. So a partial survival is the normal outcome:

| Field | Survives a mid-play reload |
|---|---|
| `Renderer[]`, `Color[]`, `float`, any `UnityEngine.Object` reference | yes |
| `MaterialPropertyBlock`, `Coroutine`, `Action`, any plain C# class | **no — comes back `null`** |

That asymmetry is what makes it dangerous: a method guarded on the fields that survive runs anyway and hands the null one to Unity. `EnemyAI.SetFlash` did exactly this and threw `ArgumentNullException: dest` out of `Renderer.GetPropertyBlock`, once per renderer per frame, for the rest of the session.

So: **anything whose type Unity cannot serialize must be created on demand, not assigned once in `Awake`.** `EnemyAI.Block` and `EnemyHealthBar.Block` are the pattern — a private property with a null check, and `Awake` left out of it entirely so the property is the only thing that can create it.

## Waves never require a total wipe

A wave ends on **clear or clock**, whichever comes first. This is deliberate and must not be "simplified" back to waiting for `EnemiesRemaining == 0`: the kit is meant to run in imported levels, and in a real level an enemy that falls through a gap stays alive forever, so a wipe requirement is a guaranteed softlock.

Two independent safety nets, both in `WaveManager`:

- **The leash** (`SweepEnemies` / `IsLost`) discards an enemy that is too far, has fallen too far below the player, or whose agent has left the NavMesh. Discarding deliberately awards no score, no combo and no drop — it is not a kill.
- **The wave clock** (`RunUntilWaveEnds`) ends the wave regardless of survivors, and clears them so the intermission is a real break.

`despawnDistance` is force-raised at `Start` if it is not comfortably clear of `maxSpawnDistanceFromPlayer`, because a leash inside the spawn ring deletes enemies on arrival and presents as "nothing spawns".

## Project conventions

- The builder provisions tags `Player`, `Enemy`, `Concrete`, `Metal`, `Wood`, `Flesh` and layers `Player`, `Enemy`, `Environment` via `EnsureProjectTagsAndLayers()`. New surface types or layers must be added there, not just in the Tag Manager UI.
- Surface tags drive `ImpactLibrary` decal/FX lookup on bullet hits.
- Enemy navigation uses `com.unity.ai.navigation` (`NavMeshSurface`), baked at build time by the builder.
- Tunables are `[Header]`-grouped public fields with `[Tooltip]`s written as plain prose. Match that style — the existing XML doc comments explain *why* a knob exists, not just what it is.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are gitignored; `.meta` files are committed and must stay in sync with their assets.
