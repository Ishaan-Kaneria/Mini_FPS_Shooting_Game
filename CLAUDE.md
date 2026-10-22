# CLAUDE.md

Level-based FPS, **Unity 6000.6.0f1**, **URP 17.6.0**. Seven arenas played as a campaign
(Warehouse, Snowbound, Desert, Rooftop, Fairground, Mars, The Auger House), with 32 levels in
each arena. A level = fixed enemy roster + strict clock + objective + 1-3 stars.

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
Renaming an arena renames its save key: add the pair to `SaveMigration.CarryRenamedArenas`
(`VerifySaveRename`), or keep the key and change `displayName` as the Unknown Planet does.
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
- Reserve ammo is per level (`LevelSet.Level.reserveMagazines`), a runtime flag on `Weapon`. Out of rounds altogether,
  the gun drops once (`Weapon.DropForFists`, a `GroundGun`) and the fists stay up until a gun is picked up (`GunPickup`:
  `GameAction.Interact`, E / D-pad up / RELOAD reads PICK UP on touch). One gun is carried. Check: `VerifyFists`.
- Wounds (`EnemyWounds`) only slow or loosen an enemy; thresholds stretch by archetype
  `woundResistance`; bosses never crawl, disarm or die to one headshot. Lethal headshots are
  raised damage in `Hitbox.Receive`, never a side-channel kill. **A limb hit never kills**:
  `Hitbox.Receive` caps its core share (`DamageInfo.coreCapped`, floor
  `BodyDamageProfile.limbCoreFloor`); only head/neck/torso, blasts and melee finish a body.
  Limb states (Healthy, Wounded, Crippled, Severed) never improve: one crippled leg limps for
  life, two (or a severed one) crawl for life, and nothing stands a crawler up. Tuning is one
  `BodyDamageProfile` per type (`EnemyArchetype.bodyProfile`, `FPSKitBodyProfiles`). A hurt
  body moves by its clip's root motion with the agent following (`EnemyMotor`; every gait since
  Stage 2). Never reparent anything into the enemy's Animator hierarchy in play (it rebinds and
  freezes values): weapons are root children laid on sockets each frame. Write Defaults stays off.
  Standing still is its own state: no long idle at a movement blend tree's centre. Enemy body changes:
  FPSKit > Enemy > Build Rigged Soldier, then FPSKit > Enemy AI > Run Limb Test (open editor).
  Blood materials: `_BlendModePreserveSpecular` 0, low smoothness. Blood obeys `GameSettings.Blood`.
  A limb comes off a living body only to a heavy blow (`WeaponData.damageClass` Heavy within
  `heavyRange`, or a blast) on a limb already crippled, with Blood on, never a boss (`EnemyBrutality`);
  pieces are capped (`SeveredPiece`). A melee blow's reaction starts on its contact frame: never slow
  `Time.timeScale` for hit-stop (VerifyCombat measures it). Stage 3 check: `FPSKitStage3Test.Batch`.
- Bullet impacts: `FPSImpactManager.HandleImpact(RaycastHit)` from every gun (Weapon, EnemyAI, TechnicalTruck); surface = struck face's
  material on a multi-material mesh, else the collider's `Surface_*` physics material, else the tag. Pooled only (one marks mesh, three
  particle systems, three lights): never instantiate per shot. Physics materials keep default friction. Check: `FPSKitBatch.VerifyImpacts`.
- Grab and punch (`MeleeGrapple`, `Docs/DESIGN_NOTES.md` "The grab and punch"): face to face (head within `grabRange`, crosshair on it,
  facing) the press grabs the head; presses punch the face (never finishing: `heldFloor`), then the overhand (kills a standard enemy,
  floors an elite); a hook at the belly doubles him over, the next press takes him bent. Never a boss. Held: pinned by `EnemyAI.Grapple`,
  holder collisions off (on, the motor's enemy slide moved the player), keyed hold/impact clips on the Action layer (Blender:
  `Tools/blender/enemy_grabbed_clips.py`; set fps after Blender's FBX import). Face damage: Blender shape keys
  (`Tools/blender/face_damage_keys.py`) baked onto the game's head by position (`FPSKitBatch.BakeFaceDamage`), driven by
  `FacialDamageHandler`. A staged test enemy has its `EnemyBrain` removed, not disabled. Check: `FPSKitBatch.VerifyGrapple`.
- Punch (`MeleeStrike`): damage is a share of the target's pool by role (boss only chips);
  the touch button is always shown; the gun is lowered via `Weapon.Lower`, hits reported via
  `Weapon.ReportHit`. Drawn as a rifle-butt strike (`MeleeStrike.AnimateButt` via
  `Weapon.HandsRotation`/`HandsOffset`); no fist is built. Hands are lofted (`BuildGunHands`).
- The player's view is a hybrid (`Docs/DESIGN_NOTES.md` "The player is a body"): arms and gun are the
  first-person view model (`ViewModelRig`: FP_Arms, IK on the gun's grip points; the gun's motion from the
  Low Poly Shooter Pack's clips, baked as deltas into `FirstPersonClips` by `FPSKitBatch.BakeFirstPersonClips`
  from Assets/AnimationStaging, played about our own grip; no clip drives a bone of our arms) on
  the FirstPerson layer under a stacked arms camera (own FOV, post-processing ON, set back `eyeBack`
  except when aiming); `PlayerBody` is legs, kick and shadow only (upper body shadow-only). Mixamo
  clips never drive the first-person arms. Values in `PlayerBodySettings`. The camera never moves for a
  strike: only a landed blow shakes it, through `AddShake` (Camera Shake setting). The arms camera renders its own shadows (gun
  self-shadowing); the world's shade reaches the gun only through `FirstPersonShade`. Muzzle light: `FPSMuzzleFlashLight` (player's casts
  shadows, High tier only). See it in a real
  arena: `UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.Batch` (Build/PlayerShots).
- Reload is animated by the `FirstPersonClips` reload track (`Weapon.ReloadPose` only without them) with
  three `WeaponData` clips on its beats (`Weapon.MagOutBeat`/`MagInBeat`/`BoltBeat`, from the pack's rifle rig); keep them in step.
- The view's own motion is `EyeCamera` (`EyeCameraSettings`): on the camera under the motor's holder, never the
  holder. Steps count off the legs' clips (`PlayerBody.StrideCycles`), so the footsteps, bob and arms are the feet seen.
  Check with `FPSKitBatch.VerifyEyeCam` (bob by gait, 30/60/144 fps, hands on the gun, foot slip).
- Captured animation (`Docs/DESIGN_NOTES.md` "Captured animation"): Ishaan's phone videos (~/MocapVideos, never in git)
  -> `Tools/mocap/mocap.sh process who_move_NN` (MediaPipe in Tools/mocap/.venv, heat-guarded) -> Humanoid clip, first-person
  track (`CapturedFirstPerson`) and settings asset in `Assets/Animations/Captured/<Who>/<Move>/`. Tune in the settings asset and
  re-run; never edit the .anim. A captured move goes into the game only after he approves its preview. Licences:
  `Docs/MOCAP_LICENSES.md` (no SMPL-based or non-commercial tools). Pipeline check: `Tools/mocap/mocap.sh test`.

## Terrain and arenas

- Ground heights: always ask `GroundHeightAt`. Slopes capped at 26 degrees (a vehicle is planned).
  No sub-grid ripples in heightfields (normal maps only). Flat pads are planned before terrain;
  nearest pad wins; claim `Radius + Blend`. `PlanX`/`BuildX` stay in the same order.
- Closed rock meshes are sunk to `LowestGroundIn` their footprint; check mesh winding (a face-out
  closed shape has 100% of normals pointing away from its centroid).
- Kill volumes follow the hazard exactly; nothing standable may sit inside one.
- Industrial: nothing glossy (`Matte`), every walkway has stairs at both ends, walled yards
  have two gates on two sides. Park: everything walkable is the terrain.
- Fairground (`parkZone`) is a **fixed plan** (`PlanFairground`, `FPSKitFairgroundPlan.cs`): sites, walkways
  and plazas have set coordinates; nothing is random-placed. Walkways are paving paint (clockwise-from-above
  winding; the first version was upside-down and invisible). Ground is gently rolling (`duneHeight` 2.6).
  Raised walkable decks are real: hall gallery + stage, coaster station, carousel/bandstand floors, haunted
  balcony, big-top seating, bridge, container lookouts. Each is solid with **flush** stairs (`FgStair`, rise
  0.22) and must not share a plane with a `NoStanding` mesh or sit behind a lip. `VerifyReach`
  (`CheckFairgroundDecks`) proves each is baked and connected; add a new deck's collider name to its list.
  Every attraction/shop door is an `Entrance(...)` in the plan; `CheckWalkwayGraph` and
  `VerifyWalkways` fail if the paving is not one network or a door has none. Anything placed late (clutter, trees) must clear `Keep()` circles, `_fgRoutes` and `SpotEmpty`. Every
  building/tent needs 2+ openings of 2.6m+. Trunks 2.8m apart, fixed tree budget (300). Glow is emissive.
- Rooftop (`rooftopZone`) is a city district (`FPSKitCity*.cs`, `Docs/DESIGN_NOTES.md` "Night Rooftop is a city district"): every building shell needs a
  `NoEnter` volume (a closed box bakes an inside), fire-escape lanes stay 2.2 m wide, every walkable roof keeps two escapes, emissive materials need
  `RealtimeEmissive`. Check with `Tools > MiniFPS > Rooftop > Check Reach` after any change.
- Desert Outpost is a **town** (`desertTownZone`, `FPSKitTown*.cs`, `Docs/DESIGN_NOTES.md` "The desert is a town"): a fixed plan
  (axis-aligned roads, one roundabout), a real `Terrain` whose heights are `_ground`, roads draped on `_townBase` over terrain sunk 18 cm,
  every mesh through `TownMesh` (world-metre UVs, winding from a stated normal). Interiors cut the outer wall's recorded openings; solid masses
  and hollow walls get Not Walkable volumes; outside stairs are planned before walls (parapet gap). Poly Haven props (CC0, `Assets/ThirdParty/PolyHaven`,
  `Tools/polyhaven`) are stood upright and LOD-culled by `FPSKitTownAssets`; never place the 200k+ triangle plant scans. Signs come from
  `Tools/town/make_signs.py` (real Arabic via raqm). The bake ends with `SealTownIslands` (every navmesh piece not joined to the start sealed, baked
  again). `NavMeshModifierVolume.center` defaults to (0,1,0): set it. `CalculatePath` gives up on long routes: prove reachability in short legs.
  Facades (`FPSKitTownFacade`): brick / limestone / plaster with real dressings; fine work in per-chunk `Detail_` meshes (no collider,
  out of the bake, LOD-culled); scene meshes compressed. Tile textures by the courses in them, tint = wanted colour / image mean.
  Memory: the editor was killed (earlyoom) at 6.6 GB playing it. Props collide on their coarsest LOD (convex if dense), parked cars are
  merged meshes (`ParkedCarPrefab`); measure with `FPSKitTownTest.Memory` and `/usr/bin/time -v` round VerifyDesertSpawns (now 4.0 GB peak).
  Check: `FPSKitBatch.VerifyTown` (famous locations reachable, no roof islands, life wired); `FPSKitTownTest.Probe` maps what fails.
  Light probes (`FPSKitTownProbes`) need the probes-only bake at save (`BakeTownProbes`: no lightmaps, sun Mixed/Baked Indirect);
  realtime reflection probes stay out of it (captured headless they drew everything flat white).
  Shops (`FPSKitTownShopfront`): storefront 0.35 m behind the piers, frames 5 cm out of glass/shutter, sign plates 10 cm off the
  wall under a hood, retail cornice, foundation course; `ShopFront` keeps the old draws from the building's random sequence (new
  choices from its own) so the town does not reshuffle. Cover outside shops (`SpinShopCover`) carries `CoverSpot` for EnemyBrain.
  Probe bake indirect is scaled (`TownProbeIndirect` 2.75) so people/cars get ~0.8 of the walls' sky ambient (VerifyTown checks the
  ratio); never judge a dark civilian by its material first. A building's shops share a `ShopLook` (layout-seeded).
  Walls vary per building (`FPSKitTownWalls`, layout-seeded); house interiors are `FPSKitTownInterior` + `FPSKitTownFurniture`
  (`Docs/DESIGN_NOTES.md` "Walls varied and weathered"): new randomness never draws from `b.rng` (replay the old draws when
  replacing code that did, or the town reshuffles). For the navmesh: doorways 1.8 m (1.5-1.6 m joins only sometimes), door
  leaves folded flat against a wall, nothing (plinth, kerb) across a threshold, stair foot 1.5 m in, stairwell cut from the
  first tread with < 2.45 m over its top, no floor or rug over the stairwell, furniture off the 1.4 m lanes to every doorway.
  Room marks sit at each room's way in; VerifyTown `CheckInteriors` wants 95% of rooms / 90% upstairs reached.
- `FPSKitGraphics.Apply` must never write the three tier assets; only `FPSKitQualityTiers.Ensure` does. The dynamic UI fonts are never saved
  (`FPSKitFontHygiene`), except by `Rebuild Font Assets`.
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
- Text fields on touch use the game's own keyboard (`OnScreenKeyboard.Attach`), never the
  OS one; desktop types into the field. Keys are not Selectables (they would steal focus).
- UI sound goes through `UISfx` (clips on `UITheme`, filled by `ImportUiKit`); kit buttons get
  a `UIButtonSound` automatically, so set a voice with `UIButtonSound.On`, never `AddComponent`.
- Only the top `OverlayPanel` answers Back. One-line kit texts hold their line height
  (`UITextFloor`), so size card cells with `UIGrid.ContentHeight`, not an aspect alone.
- Level select is choose-then-play: tiles choose, the `LevelDetail` pane has PLAY.
  Hover never changes the pane, and the grid/pane split is fixed flex (content-sized widths
  made the screen shake).
- Corners are slightly rounded (`UITheme.cornerRadius`, `FlatRect.cornerRadius`; 0 = square for
  full-screen sheets, bars across the screen and tabs; 999 = pill). Short choices (2-4) in
  Settings are `UISegmented`; longer lists keep `UIChoice` arrows.

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
Objectives, Rosters, Touch, HudLayout, SaveRename; UI kit gallery via `FPSKitUIKit.VerifyGallery`.
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
