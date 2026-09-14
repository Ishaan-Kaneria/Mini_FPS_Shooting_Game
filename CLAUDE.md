# CLAUDE.md

Wave-survival FPS built on **Unity 6000.6.0f1** with the **Universal Render Pipeline** (com.unity.render-pipelines.universal 17.6.0). The player holds a rebindable arena position against endless, escalating waves of NavMesh-driven enemies.

## Layout

Scripts live in `Assets/Scripts`, split into exactly two folders:

- `Assets/Scripts/Runtime/` — all gameplay MonoBehaviours and ScriptableObjects, flat (no subfolders), **global namespace**.
- `Assets/Scripts/Editor/` — editor-only tooling under `namespace FPSKit.EditorTools`, every file wrapped in `#if UNITY_EDITOR`. The folder name `Editor` is what keeps this code out of player builds; there are no `.asmdef` files anywhere, so moving these files breaks the build.

There is no `Core/`, `Player/`, `Weapons/`, `Enemies/` or `UI/` folder — those concerns are separated by file, not by directory:

| Concern  | Files |
|---|---|
| Player   | `PlayerMotor.cs` (CharacterController move/look/recoil), `WeaponSway.cs` |
| Weapons  | `Weapon.cs`, `WeaponData.cs` (ScriptableObject, `FireMode` Single/Burst/Auto), `ImpactLibrary.cs` |
| Enemies  | `EnemyAI.cs` (NavMeshAgent, `State` Idle/Chase/Attack/Dead), `Health.cs`, `Hitbox.cs`, `RagdollController.cs` |
| Waves    | `WaveManager.cs` — endless spawner, per-wave growth, dynamic NavMesh ring spawns that avoid the player's view |
| UI       | `HUDController.cs`, plus the touch stack: `TouchControls.cs`, `TouchButton.cs`, `TouchLookArea.cs`, `VirtualJoystick.cs`, `MobileInput.cs` |
| Config   | `ControlSettings.cs`, `LevelTheme.cs` |

`Assets/FPSKit_Generated/` holds tool output: generated scenes, themes, materials, `Controls.asset`, `TestRifle.asset`, `ImpactLibrary.asset`, `Enemy.prefab`, post-FX volume profiles. Treat everything in it as regenerable. Art comes from `Assets/RPG_FPS_game_assets_industrial/`.

## Input: legacy only

Gameplay reads the **legacy `UnityEngine.Input` API** (`Input.GetKey`, `Input.GetAxisRaw("Mouse X")`). Do not port code to the new Input System.

Caveats worth knowing before you touch input:

- The project's `activeInputHandler` is `2` (**Both**), and `com.unity.inputsystem` is installed as a package dependency. That is deliberate — it keeps the legacy API alive while allowing one optional path.
- The single exception is `PlayerMotor.ReadMouseCounts()`, which has a `#if ENABLE_INPUT_SYSTEM` branch using `Mouse.current.delta` for raw, unsmoothed mouse look when `rawMouseInput` is on. It falls back to `Input.GetAxisRaw` otherwise. Keep both branches working.
- `Assets/InputSystem_Actions.inputactions` is leftover from the Unity template and is not referenced by any gameplay code.

Bindings are not hard-coded: they live in the `ControlSettings` ScriptableObject (`FPSKit_Generated/Controls.asset`). Defaults are unconventional — **arrows move, Space fires, double-tap Space sprints, left click jumps** — with W/A/S/D wired as always-live alternates. Read new keys through `ControlSettings` helpers rather than calling `Input.GetKey` with a literal `KeyCode`.

## The scene builder regenerates everything

`FPSKitSceneBuilder.cs` (~1350 lines) drives the **FPSKit** menu:

- **FPSKit > Build Scene > [Industrial Warehouse | Desert Outpost | Snowbound Station | Night Rooftop | Abandoned Subway | Mars Colony]** — each calls `BuildScene(themeName)`.
- **FPSKit > Build Scene > Build ALL Themes** — builds and saves one scene per theme, registering them in Build Settings so the in-game restart works.
- **FPSKit > Add Gameplay To Current Scene** — the non-destructive path: injects player, enemies, wave manager, HUD, post-FX and a baked NavMesh into an existing level, leaving geometry and baked lighting alone. Refuses to run twice (bails if a `Player`-tagged object exists).

**`BuildScene` is destructive.** It calls `EditorSceneManager.NewScene(..., NewSceneMode.Single)` and constructs the entire scene from scratch — lighting, skybox, arena geometry, player rig, weapon data, enemy prefab, spawn points, baked NavMesh, wave manager, HUD, post-processing — then saves over `Assets/FPSKit_Generated/Scenes/<Theme>.unity`. Any hand-tuning done in the Unity editor to a generated scene is lost on the next build.

So: **fix scene content by editing the builder, not the `.unity` file.** Hand-editing a generated scene is only appropriate for a throwaway experiment. The same applies to the generated assets it writes (`Controls.asset`, `Enemy.prefab`, themes, materials) — the builder and `FPSKitThemes` recreate or re-dirty them.

Other editor tools: `FPSKitThemes.cs` (creates/resets `LevelTheme` assets), `FPSKitArtTools.cs` (**FPSKit > Art Pack Setup**), `FPSKitEnemySetup.cs` (**FPSKit > Enemy Setup**), `FPSKitMobileControls.cs` (**FPSKit > Add Mobile Touch Controls**), `ControlSettingsEditor.cs` (custom inspector with control presets).

## Project conventions

- The builder provisions tags `Player`, `Enemy`, `Concrete`, `Metal`, `Wood`, `Flesh` and layers `Player`, `Enemy`, `Environment` via `EnsureProjectTagsAndLayers()`. New surface types or layers must be added there, not just in the Tag Manager UI.
- Surface tags drive `ImpactLibrary` decal/FX lookup on bullet hits.
- Enemy navigation uses `com.unity.ai.navigation` (`NavMeshSurface`), baked at build time by the builder.
- Tunables are `[Header]`-grouped public fields with `[Tooltip]`s written as plain prose. Match that style — the existing XML doc comments explain *why* a knob exists, not just what it is.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are gitignored; `.meta` files are committed and must stay in sync with their assets.
