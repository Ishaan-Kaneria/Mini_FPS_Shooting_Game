# CLAUDE.md

Level-based FPS, **Unity 6000.6.0f1**, **URP 17.6.0**. Seven arenas played as a campaign
(Warehouse, Snowbound, Desert, Rooftop, Subway, Mars, The Auger House), eight levels each
except the finale (three). A level = fixed enemy roster + strict clock + objective + 1-3 stars.

The long-form history behind every rule below (the bugs, the measurements, the reports) is in
`Docs/DESIGN_NOTES.md`, with the same section names. Read the relevant section before
changing a system it covers. Where anything disagrees with a generic Unity skill, this file wins.

## Layout

- `Assets/Scripts/Runtime/`: all gameplay code, flat, global namespace.
- `Assets/Scripts/Editor/`: editor tooling, `namespace FPSKit.EditorTools`, every file wrapped
  in `#if UNITY_EDITOR`. No `.asmdef` anywhere; the folder name is what excludes it from builds.
- Concerns are split by file, not folder. Key files: `PlayerMotor`, `Weapon`/`WeaponData`,
  `BombThrower`/`BombProjectile`/`Explosion`, `EnemyAI`/`EnemyArchetype`, `LevelManager`/`LevelSet`/
  `LevelProgress`, `GameDirector`/`GameSession`, `Wallet`/`Loadout`/`StoreCatalog`, `Campaign`/
  `CampaignData`, `LevelObjective`, `GameInput`/`ControlSettings`, `HUDController`/`HudView`,
  `UITheme`/`UIKit`, `DeviceProfile`, `TouchControls`/`MobileInput`.
- `Assets/FPSKit_Generated/`: tool output (scenes, themes, levels, enemies, store, materials,
  prefabs). All regenerable. Art from `Assets/RPG_FPS_game_assets_industrial/`.

## Generated content: edit the generator, never the output

- `FPSKitSceneBuilder` (partial across `FPSKitOpenZone/Desert/Terrain/Industrial*/Factory/
  MeshKit/Park/Volcanic/DesertLife/SnowLife/LavaField/Textures.cs`) **destroys and rebuilds** each
  arena scene. `FPSKitMenuBuilder` (+ `FPSKitDashboard.cs`) does the same for `Menu.unity`.
  Fix scenes in the builders. Same for `Build/WebGL/`: edit `Assets/WebGLTemplates/FPSKit/`.
- Scenes are **binary**: grepping them for GUIDs finds nothing. Use `AssetDatabase.GetDependencies`.
- **Five generators need a reset to take effect**: changing `Configure` in `FPSKitEnemyRoster`,
  `FPSKitLevels`, `FPSKitStore`, `FPSKitThemes`, `FPSKitCampaign` does nothing until
  `FPSKitBatch.ResetEnemyArchetypes / ResetLevelSets / ResetStore / ResetThemes / ResetCampaign`
  runs. Then **verify by reading the `.asset`** (`grep -a`), not the log. Duplicate assignments
  in one `Configure` case silently keep the later one.
- Clear builder statics at the top of the per-arena build (`BuildFromTheme`), not in the branch
  that writes them; `BuildAllThemes` runs all arenas in one process.
- Shared builder helpers must gate on the layout mode (e.g. river hole only for `openZone`).
- `NoEntry` volumes must not inherit scale/shear; put them on an unscaled object.
- Retuned colours leave orphan materials; `FPSKitPrune` / `FPSKitBatch.PruneMaterials -fpskitApply`.
- Previews and store renders need a graphics device: `UNITY_GRAPHICS=1 Tools/unity-batch.sh
  FPSKitBatch.BuildDashboard`, else headless writes blank fallbacks. Render through
  `RenderPipeline.SubmitRenderRequest`, submit twice, `DynamicGI.UpdateEnvironment()` first.
- To see a built arena: `UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViews
  -fpskitTheme "<Theme>" -fpskitOut Build/Views`.

## Content is data

New arena = `LevelTheme` asset (+ scene, `LevelSet`, `ArenaCatalog` entry, rebuild dashboard).
New enemy = `EnemyArchetype`. New item = `StoreCatalog` entry with a **new permanent `id`**.
New level = `LevelSet` entry. Add missing knobs to the ScriptableObject, not the builder.
Never rename serialized fields (`unlockWave` etc. keep old names on purpose).
Campaign order lives only in `FPSKitCampaign.ZoneOrder`; difficulty keys off that, not
`FPSKitThemes.Names`. Each arena's `enemyRoster` differs; Grunt/Runner/Marksman are in all.

## Statics and reloads (domain reload is OFF)

- Every static carrying state is cleared in a
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` hook on its
  class. `Wallet`, `Loadout`, `LevelProgress`, `PlayerProfile` are pure PlayerPrefs accessors
  and cache nothing: keep them that way.
- Non-serializable fields (`MaterialPropertyBlock`, `NavMeshPath`, delegates, plain classes) are
  created on demand via a null-checked property, never in `Awake` or a field initialiser.
- Play-mode tests: call `FPSKitPlayMode.SuspendStartScene()` and `SaveMigration.Apply()` in setup;
  restore every borrowed global (`Time.captureDeltaTime`, live `LevelSet` clocks) in `Detach`.

## Input

- All input via `GameInput` (Input System). Keyboard bindings come from `ControlSettings`
  (`Controls.asset`), never typed into `.inputactions`. Pad layout in `AddPadBindings` must match
  `InputPrompts.PadGlyph`. Read keys via `ControlSettings`/`GameInput`, not `Input.GetKey`.
- **Keep** `PlayerMotor.ReadMouseCounts()`'s fallback to `Input.GetAxisRaw` on an *empty* reading
  (Linux/X11 reports zero delta while locked). Hence `activeInputHandler: 2` (Both).
- Mouse controls are ignored while `MobileInput.Active` (touch 0 is also a mouse click).
- Mouse look needs `Cursor.lockState == Locked`; `HandleCursor` re-locks on play keys (not Escape).
- Escape/B = back, one press closes one thing (`OverlayPanel.AnyOpenThisFrame`).
- Aim assist: touch and stick only, never mouse; centre mass; gated on player turning/firing.
- Ishaan plays on a touchpad: hold-key-and-move-pointer is unperformable (disable-while-typing).
  Any hold-to-X control also accepts tap-to-latch.
- Sprint works in any direction and from standstill; the key alone runs forward
  (`SprintDrivesForward`, based on the key being *held*, applied after `EvaluateSprint`).

## Gameplay invariants

- **The clock always ends a level.** Never require an empty arena; no objective may change what
  ends a level (only what scores). The leash (`IsLost`) discards lost/unreachable enemies with no
  score and queues one replacement each.
- Spawns require a complete `NavMesh.CalculatePath` to the (snapped) player. Builders keep
  unreachable navmesh out: `NoStanding` (flat tops), `NoEntry` (anything with an inside),
  `SealNavMeshOutside`. Kerbs/detail go on the backdrop layer, never a NavMeshModifier.
- Stars: weighted fraction (boss weight 5+); three stars only for a full clear. `LevelProgress`
  keeps the best; unlocking is only `LevelProgress.IsUnlocked`; zones only `Campaign.ArenaUnlocked`
  (previous holder down **and** star threshold). A level/zone with stars is always open.
  "Everything unlocked" reports: check the **FPSKit > Debug > Unlock All** EditorPref first.
- Child defeated and achievements are *derived*, never stored. Rank/XP derived.
- Coins: only `Wallet` moves them, `TrySpend` refuses negatives; buy = refuse, charge, then grant.
  Banked once in `GameSession.RecordResult` (every ending passes there); not combo-multiplied.
  `LevelResult` is a struct: fill it completely before passing it anywhere.
- Upgrades are multipliers on components (`Weapon`, `BlastSpec`), **never written into
  `WeaponData`/`BombData`/`EnemyArchetype`** (shared assets). `PlayerLoadout` and
  `PlayerProgression.Apply` are absolute and idempotent.
- Enemy `stoppingDistance` is a small fixed number (0.8) in both `FPSKitSceneBuilder` and
  `FPSKitEnemySetup`; `DesiredStandOff` subtracts it. Sight casts rationed at 0.1s, staggered.
  Nothing outruns a player sprint (8.2); speed cap 1.35x.
- Bomb: flies its own solved parabola with its own gravity; indicator draws the same equation;
  clamp range from the player; flight time scales with distance; touch uses `RangeFromPitch`.
  Blast damages each `Health` once. Audible reach is `maxRange`.
- Reserve ammo is per level (`LevelSet.Level.reserveMagazines`), a runtime flag on `Weapon`.
- Wounds (`EnemyWounds`) only slow or loosen an enemy; thresholds stretch by archetype
  `woundResistance`; bosses never crawl, disarm or die to one headshot. Lethal headshots are
  raised damage in `Hitbox.Receive`, never a side-channel kill. Enemy body changes:
  `FPSKitBatch.RebuildEnemy`, then `VerifyWounds` and `CaptureEnemies` (UNITY_GRAPHICS=1).
  Blood materials: `_BlendModePreserveSpecular` 0, low smoothness. Blood obeys `GameSettings.Blood`.
- Punch (`MeleeStrike`): damage is a share of the target's pool by role (boss only chips);
  the touch button is always shown; the gun is lowered via `Weapon.Lower`, hits reported via
  `Weapon.ReportHit`. Hands are builder primitives (`BuildGunHands`, `BuildFist`).

## Terrain and arenas

- Ground heights: always ask `GroundHeightAt`. Slopes capped at 26 degrees (a vehicle is planned).
  No sub-grid ripples in heightfields (normal maps only). Flat pads are planned before terrain;
  nearest pad wins; claim `Radius + Blend`. `PlanX`/`BuildX` stay in the same order.
- Closed rock meshes are sunk to `LowestGroundIn` their footprint; check mesh winding (a face-out
  closed shape has 100% of normals pointing away from its centroid).
- Kill volumes follow the hazard exactly; nothing standable may sit inside one.
- Industrial: nothing glossy (`Matte`), every walkway has stairs at both ends, walled yards
  have two gates on two sides. Park: everything walkable is the terrain.
- Art-pack lookups (`Pack`) may return null; always fall back.
- Generated textures are grayscale; colour stays on the material. `_NORMALMAP` keyword required.

## UI

- UI is built in C# (`UIKit`, builders), never prefabs/UXML. Build buttons with
  `FPSKitMenuBuilder.MakeButton` / `UIKit`; everything else `raycastTarget = false`.
- A panel's component lives on its parent and toggles a child (else `Awake` hides it forever).
- Runtime-built views: `Awake`/`OnEnable` run inside `AddComponent`, before parts are assigned.
  Subscribe at the end of the build; kit controls that listen to children expose an idempotent
  `Wire()`.
- Never write `anchoredPosition` on a grid child (animate `localScale`). Grids go through `UIGrid`.
  Lay out in fractions of measured boxes, not pixels. TMP has no `</alpha>`.
- Text joins go through `UIText` (`Separator`, `Row`, `KeyLabel`); coins through `Wallet.Format`.
- Devices: `DeviceProfile` = reach + form + orientation. The only screen read is
  `TouchMetrics.ScreenDpi` (WebGL uses `WebDevice.FramebufferDpi`). A handset gets its own
  layout (`ApplyFormLayout`), not a scaled desktop. Landscape-only everywhere. Touch sizes are in
  millimetres (`TouchProfile.asset`). Measure canvas scale on the first frame, not in `Start`.
- Show controls/instructions only for equipment the player actually has.
- `UITheme.asset` field initialisers are the spec; flat, matte, one amber accent plus six arena
  signal colours. New icons need `FPSKitBatch.ImportUiKit`.
- No developer logs on the player's screen (WebGL page only with `?debug`).

## Platforms

- WebGL uses the `PROJECT:FPSKit` template. Touch layer is added to staged scene copies
  (`StageTouchScenes`) for WebGL and Android, never to the real scenes.
- **Never change `companyName` or `productName`** (orphans every web save). Android app id is
  validated by `ResolveApplicationId`. No INTERNET permission. Signing only from `FPSKIT_*` env
  vars; never commit keystores. `targetSdkVersion` is explicit.
- Quality: three tiers, chosen once then the player's. Low render scale stays 1.0.

## Verifying

Use the `verify` skill. Checks run via `Tools/unity-batch.sh FPSKitBatch.<Verify*>`:
Controls, Replay, Levels, Store, Combat, Wounds, Bomb, Flow, Terrain, Reach, Zone, Devices, Statics,
Objectives, Rosters, Touch, HudLayout; UI kit gallery via `FPSKitUIKit.VerifyGallery`.
A play-mode test measuring speed must set `Time.captureDeltaTime = 1f/60f`. Tests that exist
to prove a rule must fail with the rule deleted (hence public `ControlSettings.CanRead`,
`DeviceProfile.FormFor`). If `VerifyReach` fails by more than a few percent, read the reported
extents before changing anything.

## Conventions

- Tags/layers are provisioned in `EnsureProjectTagsAndLayers()` only.
- Tunables: `[Header]`-grouped public fields with plain-prose `[Tooltip]`s; doc comments say why.
- `.meta` files committed and kept in sync.
- `Tools/generate-placeholder-audio.py`: save/restore `random.getstate()` around new noise clips.
- Build with no sound: suspect a corrupted `Library/` first (delete it with Unity closed).
