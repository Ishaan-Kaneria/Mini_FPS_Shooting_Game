# Desert Outpost: full review (bugs, realism, fixes)

Written 2026-11-04 by Claude Code (cloud session) for the developer and for Claude Code on the laptop. Lighting fixes
and the look: `Docs/DESERT_LIGHTING.md` (A2 and A4 are now in code). Glass: `Docs/DESERT_GLASS.md`. Explosives and
grenades: `Docs/DESERT_EXPLOSIVES.md`. Roofs, people, shops: `Docs/DESERT_TOWN_FIXES.md`. Repo state reviewed:
`261c706` (same as GitHub `main`).

## 0. Read this first (for the Claude Code session that carries it out)

* **How this was made.** No Unity in the cloud session: every finding comes from reading the source, the commit
  history and the realism audit's data (`Docs/REALISM_AUDIT_DesertOutpost.csv`). Nothing was compiled, baked or played.
  Each issue says how sure it is:
  * **Confirmed (code)**: the code does this; the effect follows directly.
  * **Likely**: the code points to it strongly; confirm in play before fixing.
  * **Check**: a question to settle in the editor.
* **The realism data is from 2026-10-29** (before the gloss fix, Phase A, the room-scene split and the district bakes).
  **Re-run the audit first** (`UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.AuditRealism -fpskitTheme "Desert Outpost"`,
  about 3 min). Then re-run `python3 Tools/desert_review_objects.py` and replace sections 12-13 with its output, so the per-object table matches.
* **Project rules still apply** (`CLAUDE.md`): fix generators, never generated scenes; one change at a time; commit after
  each; the developer play-tests after each stage; batch jobs only with Unity closed; memory watchdog on; max two
  attempts per problem, then stop and give options.
* **IDs.** `A` lighting, `B` vehicles, `C` people and traffic, `D` combat, `E` builders and content, `F` performance,
  `G` realism. Severity: **S1** breaks play or a core system, **S2** clearly visible or wrong, **S3** polish or cost.

## 1. All issues at a glance

| ID | Sev | Issue | Sure | Effort |
|---|---|---|---|---|
| A1 | S1 | Moving things and probe-lit details still show the "bright upper wall" band indoors | Confirmed (code) | L |
| A2 | S1 | Medium gets no indoor reflections at all: rooms mirror the street and sky | Confirmed (code) | M |
| A3 | S2 | 160 houses and 752 shops reflect one shared, made-up cubemap at a fixed strength | Confirmed (code) | M |
| A4 | S2 | One reflection box per house covers every room, both floors and the yard between | Confirmed (code) | M |
| A5 | S2 | Shop reflection strength chosen by a " dark" name suffix | Confirmed (code) | S |
| A6 | S2 | Arms and gun lit by moving the sun for the arms camera (a stand-in, not a light system) | Confirmed (code) | L |
| A7 | S2 | The eye's probe dimming is global: probe-lit things anywhere in view dim with the room | Confirmed (code) | M |
| A8 | S2 | Door frames, skirting, front doors and small props glow in dark rooms | Confirmed (commit 0c296f6) | M |
| A9 | S2 | Window glass renders as black holes (opaque, nothing behind it) | Confirmed (code) | L |
| A10 | S2 | Low, phones and web: no eye adaptation, outdoor probes inside rooms | Confirmed (code) | M |
| A11 | S3 | APV sky occlusion off | Check | S |
| A12 | S3 | Medium shares its renderer asset with Low and phones | Confirmed (assets) | S |
| B1 | S1 | Driving: the eye camera drags the chase camera to the world origin | Confirmed (code) | S |
| B2 | S1 | Chase camera goes through walls | Confirmed (code) | S |
| B3 | S2 | Steering snaps from 0 to full lock | Confirmed (code) | S |
| B4 | S2 | No engine braking, no idle brake: the car rolls on and slides down slopes | Confirmed (code) | S |
| B5 | S2 | Flat torque, no gears, hard cut at top speed | Confirmed (code) | M |
| B6 | S2 | Wheel physics at default substeps: jitter at low speed | Confirmed (code) | S |
| B7 | S2 | Getting out turns on every player renderer, including ones that were off | Confirmed (code) | S |
| B8 | S2 | Getting out can put the player inside a wall or on the roof | Confirmed (code) | S |
| B9 | S2 | Hitting an enemy is hitting a wall; no knock-down | Likely | M |
| B10 | S2 | The player hidden in the car: enemies' shots and sight hit the car's box | Likely | M |
| B11 | S3 | One E press can pick up a gun and get in; interact works while paused | Confirmed (code) | S |
| B12 | S3 | Per-frame allocations and scene searches in the car | Confirmed (code) | S |
| B13 | S2 | Technical truck drives through cars, people and the player; pitch half the slope | Confirmed (code) | M |
| B14 | S3 | Traffic cars float or sink between route points; no suspension | Likely | M |
| C1 | S1 | Civilians killed by enemies or the truck cost the player stars and score | Confirmed (code) | S |
| C2 | S2 | Civilians and traffic cars are created and destroyed (hitches) instead of pooled | Confirmed (code) | M |
| C3 | S3 | Bodies over 20 disappear even in view | Confirmed (code) | S |
| D1 | S1 | A neck shot does nothing to an enemy at or below 15 % health | Confirmed (code) | S |
| D2 | S2 | Automatic guns fire up to 25 % slower at low frame rates | Confirmed (simulated) | S |
| D3 | S2 | Blasts damage through walls (houses, shops) | Confirmed (code) | M |
| D4 | S3 | Damage reported larger than what landed (numbers, hit reports) | Confirmed (code) | S |
| D5 | S3 | Score can go negative after the civilian penalty | Confirmed (code) | S |
| D6 | S3 | Quitting twice in one frame banks the coins twice | Confirmed (code) | S |
| D7 | S3 | Recoil and kick recovery depend on frame rate | Confirmed (code) | S |
| E1 | S2 | A silent white fallback material on 98 building pieces | Confirmed (code + audit) | S |
| E2 | S3 | VerifyStatics fails on `WalkRoute.Desert` and `DrawCounters.Names` | Confirmed (commit 261c706) | S |
| E3 | S2 | Kicked-in doors: whether agents use the opened house is unmeasured | Check | S |
| E4 | S3 | `ShadowLeak`/`RoomAudit` found no ceiling gap: the "sky in the room" report is still open | Check | M |
| E5 | S3 | Old houses' rooms very dark | Confirmed (commit 0c296f6) | M |
| F1 | S2 | Medium 1 % low ~20 fps (stutter) | Measured (notes) | L |
| F2 | S3 | Traffic noise listening allocates every frame per car | Confirmed (code) | S |
| G | S2 | Realism: 222 objects with knife-sharp edges, 95 flat colours, 66 visible tiling, 64 too dark... | Audit | L |
| E6 | S1 | No way up to most roofs: only enterable villas get a stair (`PlanStair` in `BuildVilla` alone) | Confirmed (code) | L |
| C4 | S2 | "No people": 36 on Medium over the whole town, none within 30 m, and every one hides and is removed at the first shot | Confirmed (code) | M |
| E7 | S2 | Shops too open and nearly empty: whole empty boxes seen from the street | Confirmed (code + audit) | L |
| D8 | S2 | No grenades unless the Warehouse boss is beaten; no explosive props | Confirmed (code); explosives and grenade drops now in code | M |

Effort: S under an hour, M a few hours, L a day or more.

---

## 2. Lighting (A)

How the desert is lit today, from the code:

| Thing | High | Medium | Low / phones / web |
|---|---|---|---|
| Renderer | `PC_Renderer` (Forward+) | `Mobile_Renderer` (**Forward**) | `Mobile_Renderer` (Forward) |
| Probes for moving things | APV (`m_LightProbeSystem 1`), 3 m town grid; 1 m volumes only in the 4 pilot insides | APV, same | classic light probes (~2,200, none indoors) |
| Room walls | district lightmaps, 20 cm (6 district scenes, `8ab44d3`) | same | `FPSKitReducedCopy` (check what it keeps) |
| Indoor reflections | 160 house + 752 shop box probes, shared cubemap; 4 pilot rooms captured | **all off** (A2) | removed |
| Eye adaptation | `EyeAdaptation` (metered, before post) | same | **off** (A10) |
| Arms and gun | `FirstPersonShade` moves and dims the sun for the arms camera | same | same |

### A1 (S1) Moving things and probe-lit details still show the band indoors
* **What you see:** upper walls bright, lower half dark, a hard line between (his Oct 31 play-test). Since `8ab44d3` the
  **walls** are lightmapped and the band is gone from them (BatchBand room 1: 128/55 -> 48/50). But everything still lit
  by probes keeps it: enemies, civilians, the player's body, doors, frames, skirting and small props.
* **Cause:** `Editor/FPSKitTownGI.cs:411` `const bool ProbeLayers = false`. The APV grid's layers sit at 1.5 m and 4.5 m.
  In a one-storey house the 4.5 m probes are above the roof, in the sun, so anything probe-lit blends toward the
  street's light going up the room. Probe layers fix exactly this, but they were switched off on Nov 3 (`d553f45`):
  with a 3 m grid the indoor probes held almost no light and the rooms went black. That was before every room had
  lightmaps.
* **Fix (in order; stop after each and measure):**
  1. Turn `ProbeLayers` back on (`ApplyIndoorLayers`): inside surfaces read only indoor probes. The walls no longer
     depend on probes (they are lightmapped), so the black-room result of `d553f45` does not apply to them.
  2. Give rooms enough indoor probes. The 1 m volumes for all 319 insides went over the 4 M probe limit (11.1 M,
     `8e3054e`). Use **1.5 m in rooms** instead: one `ProbeVolume` per district's interiors with
     `overridesSubdivLevels`, lowest level 1 (the town's 3 m) and an extra level in rooms only, or per-room volumes
     sized to the room's box (not the inside's whole renderer bounds) with `fillEmptySpaces = false`. Budget it with
     `FPSKitTownGI`'s probe count log before baking.
  3. Moving things have no in/out layer. Give characters and the player's body a runtime layer switch: under a roof
     (the same 13-ray test `EyeAdaptation` uses, rationed to 0.25 s) set `renderingLayerMask` to `IndoorLayer`, else
     `OutdoorLayer` (bits from `FPSKitTownGI.IndoorLayer/OutdoorLayer`). Without this an enemy in a room still reads
     the probes over the roof.
* **Check:** `FPSKitShadowLeak.BatchBand` (rooms upper/middle within 15 %), `FPSKIT_TOUR_SOLDIER=1` lighting tour
  (the soldier in each room within 0.7-1.3x the wall level), `VerifyTown` probe ratio. Memory: the bake peaked at
  10-12 GB; run it with the watchdog, one district at a time.

### A2 (S1) Medium has no indoor reflections
* **What you see on Medium:** walls, floors, shelves and fans inside mirror the bright street probe or the sky: the
  "shiny everything" of his Nov 3 play-test. This is the default tier on his laptop.
* **Cause:** `Runtime/IndoorReflections.cs:40-46` turns every indoor probe off unless the renderer is Forward+ or
  Deferred+. Medium uses `Mobile_Renderer.asset` (`m_RenderingMode: 0`, Forward). Under Forward each object takes the
  top two probes for the *whole object*; the town's facades are merged per 64 m chunk, so a chunk near a house took
  the house's room probe on its outside. That was the reason. Since the split (`b9d0ef3`, `3bebaa0`), insides are
  their own objects (per room in district scenes), so per-object selection is mostly right for them.
* **Fix (pick one, measure GPU cost on Medium with `FrameCostProbe`):**
  1. **Preferred:** make a `Medium_Renderer.asset` that is Forward+ (copy `PC_Renderer` settings that matter to Medium)
     and point `Medium_RPAsset` at it in `FPSKitQualityTiers.Ensure` (never in `FPSKitGraphics.Apply`). Forward+ cost
     on the Arc at this light count is usually under 1 ms; measure.
  2. Keep Forward on Medium, but let `IndoorReflections` turn probes on, and make sure each interior renderer's
     `reflectionProbeUsage = BlendProbes` while every facade chunk's is `BlendProbesAndSkybox` with the indoor probes'
     `importance` lower than the street's *for the facades*. Simplest form: put the indoor probes on their own
     rendering layer; URP 17 does not filter probes by layer, so in practice option 1 is cleaner.
* **Check:** Medium release build, the pilot views and 3 non-pilot rooms: wall sheen (on minus off) under 3 levels of
  255. `FrameCostProbe` Medium median within 1 ms of today.

### A3 (S2) One shared made-up cubemap for 156 houses and 752 shops
* **Cause:** `Editor/FPSKitTownGI.cs:114-195` (`PlaceIndoorReflections`) gives every probe `customBakedTexture =
  RoomCube()` (`IndoorRoom.cubemap`, a 32 px painted gradient) and a fixed strength: `HouseReflection 0.04`,
  `ShopLitReflection 0.7`, `ShopDarkReflection 0.25` (line 274). Only the 4 pilot rooms get real captures
  (`FPSKitTownLightmaps.ApplyReflections`, line 673). A room lit through one window and a room with three doors reflect
  the same thing at the same level; a red-walled room reflects beige.
* **Fix:** capture each room after its district's lightmap bake, box-projected, from the room's middle at 1.6 m, at
  64 or 128 px (`Lightmapping.BakeReflectionProbe`, the pilot's own code path, `CaptureReflections`). Then normalise:
  probe intensity = (room's mean baked wall brightness, read from the lightmap as `BatchLightReport` does) / (the
  capture's mean). Remove `RoomCube()` and the three constants. Memory: 900 rooms x 128 px HDR cubes is about 300 MB
  uncompressed; use 64 px (75 MB) and BC6H. Capture per district (the bake already runs per district).
* **Check:** wall sheen in the golden room views; `FrameCostProbe`; resident memory (`FPSKitTownTest.Memory`).

### A4 (S2) One reflection box per house
* **Cause:** `FPSKitTownGI.cs:146-163` encapsulates all of a house's rooms into one axis-aligned box, upstairs and
  downstairs, with `boxProjection = false`. For an L-shaped house the box also covers the yard and part of the street
  in front. Everything inside the box (including the outside wall's face, the yard floor and passers-by) reflects the
  room cubemap.
* **Fix:** one probe per **room** (the room boxes the code already computes, `rb`), `boxProjection = true`,
  `blendDistance` 0.05 as now. That is also what A3's captures need.
* **Check:** yard and street surfaces next to houses no longer change reflection when walking past.

### A5 (S2) " dark" name suffix decides a shop's reflection
* **Cause:** `FPSKitTownGI.cs:182` and `FPSKitTownLightmaps.cs:143` read `shop.name.EndsWith(" dark")`. Renaming a shop
  mark silently changes its lighting.
* **Fix:** make it data: a `bool lit` (or `ShopLook.lit`) on the shop mark component, written where the shop is built
  (`FPSKitTownShopfront`). With A3's captures the strength comes from the capture, so the flag only decides tubes on/off.

### A6 (S2) Arms and gun lit by moving the sun
* **Cause:** `Runtime/FirstPersonShade.cs:150-170`: before the arms camera draws, the sun's intensity is lowered and
  its rotation turned to over the shoulder (shadows off past half), then put back after. The arms' probes are scaled
  by a volume on the FirstPerson layer (`ArmsProbeLight`, priority 1001). It works as a patch, but it is a second
  lighting model only the hands obey, so they will never match an enemy standing beside the player.
* **Risks:** any exception or skipped `endCameraRendering` leaves the world lit from the shoulder for a frame; the
  rotation change dirties the sun every frame (shadow cascades, GPU Resident Drawer bookkeeping).
* **Fix:** after A1 (layers plus indoor probes), light the arms like every other moving thing: APV at the eye
  position, `IndoorLayer` under a roof, the real sun with its real shadow. Keep `FirstPersonShade`'s ray test only to
  fade the sun when the gun is in shade (that part is physically right). Delete the rotation trick and the room-light
  stand-in. Do it on a branch and compare the glove/gun close-ups indoors and outdoors.

### A7 (S2) The eye dims probe light for the whole view
* **Cause:** `Runtime/EyeAdaptation.cs:206` sets `ProbeVolumesOptions.intensityMultiplier` (global volume, priority
  1000) to `2^-(offset - probeStops)`. With the eye open 5 stops, every probe-lit thing in the frame is dimmed 16x,
  including people and cars in the sunlit street seen through the doorway. Their direct sun stays, so they read as
  hard-lit cut-outs with black shadows.
* **Fix:** it exists because probe-lit things were brighter than the lightmapped walls. After A1 and A3 they come from
  the same bake and should agree, so set `probeStops` high (no dimming) and remove the multiplier if the soldier test
  passes. If a mismatch remains, fix it at bake time (APV `indirectMultiplier` vs lightmap) rather than per frame.

### A8 (S2) Door frames, skirting, front doors and small props glow in dark rooms
* **Cause (from `0c296f6`):** they are probe-lit beside lightmapped walls; A1's band and A7's mismatch.
  `16dbaf4` set house detail to take lightmaps at the next district bake (`FPSKitTownRoomBake` takes `Detail_Interior_`);
  doors and small props are still on probes.
* **Fix:** static door frames, skirting and fixed props over ~0.5 m: lightmapped in the district bake (they already get
  mesh copies with UVs for furniture, `1cfad04`). Moving door leaves stay on probes: after A1 they read indoor probes.

### A9 (S2) Window glass renders as black holes
* **Corrected 2026-11-04** (the first version blamed metallic 0.85; the comment at `FPSKitTownBuildings.cs:117-120`
  shows it was chosen on purpose: as a plain dark dielectric the windows read as flat navy cards).
* **What the audit saw:** `Town_GlassDark`, `Town_GlassBlue` FAKE, "renders as a black hole: 4 % of the brightness round
  it: almost no reflection and nothing seen behind it".
* **Likely cause:** the glass is opaque, so nothing is behind it, and what it reflects is a street probe captured near the
  ground (a dim, sky-blind view). A metallic surface shows only its reflection, so a dim probe gives a black window.
* **Fix:** real glass (his rule: no painted fake rooms): a transparent two-sided glass shader with Fresnel reflection
  and dust, no shadow and no GI blocking, real space behind every window (the real room, or a shallow "window room"
  built behind each window of a solid building), street reflection probes at window height, and breakable panes. Full
  plan: `Docs/DESERT_GLASS.md`.
* **Check:** audit light test for both materials passes; street views show reflections at grazing angles and a dim
  room face-on.

### A10 (S2) Low, phones and web: rooms on outdoor probes, no adaptation
* **Cause:** `EyeAdaptation.cs:179` sets the eye to 0 when probe volumes are off; the classic probes are deliberately
  never placed indoors (`FPSKitTownProbes.cs:24`), so anything moving in a room on Low is lit like the street outside.
* **Fix:** on the reduced copy, add a small set of classic probes per room (4-8, at 0.5 m and 1.8 m, from the room boxes),
  baked in the same bake, and let `EyeAdaptation` run on its ray test alone (no meter) with a capped boost (2 stops).
  Check what `FPSKitReducedCopy` keeps of the district lightmaps; if they are dropped, say so in its doc comment.
* **Check:** a Low build: soldier in a dark corner visible from the doorway; no glowing gun indoors.

### A11 (S3) Sky occlusion off
* `FPSKitTownGI.cs:603` `set.skyOcclusion = false`. APV sky occlusion makes the sky's share follow what each probe can
  see (narrow streets darker, courtyards lit from above). **Check** its cost (memory and bake time) on one district;
  turn it on if within budget.

### A12 (S3) Medium shares `Mobile_Renderer` with Low and phones
* Both `Medium_RPAsset` and `Mobile_RPAsset` list `Mobile_Renderer` (guid `65bc7d...`). Any change for Medium (A2)
  changes phones too. Create `Medium_Renderer.asset` in `FPSKitQualityTiers.Ensure`.

---

## 3. Vehicles (B)

Files: `Runtime/DrivableCar.cs` (248 lines), built by `Editor/FPSKitTownLife.cs:334-420` (`PlaceDrivableCars`,
`MakeDrivable`); `Runtime/TrafficCar.cs`, `Runtime/TrafficDirector.cs`, `Runtime/TechnicalTruck.cs`.

The queued "driving" feature (handoff 5.2, Stage 1) should start from these fixes. Recommended shape for the rewrite:
split `DrivableCar` into `VehicleInput` (reads `GameInput`/`MobileInput`, smooths steering), `VehicleDrive`
(engine curve, gears, brakes, WheelColliders, anti-roll, downforce), `VehicleCamera` (its own camera, collision) and
`VehicleSeat` (enter/exit, hiding the player, interact routing). Tunables into a `VehicleSettings` ScriptableObject
per car (CLAUDE.md: knobs on assets).

### B1 (S1) The chase camera is pulled to the world origin
* **Cause:** `DrivableCar.GetIn` (line 145) unparents the player's main camera and drives it in `LateUpdate`. But
  `EyeCamera` (on the player, `[DefaultExecutionOrder(150)]`, runs after the car) caches that same camera and writes
  `_eye.localPosition = offset; _eye.localRotation = ...` every frame (`EyeCamera.cs:164-165`). Unparented, local is
  world: the camera is put at about (0, 0, 0) each frame. `WeaponSway` and `ViewModelRig` may also write to it.
* **Fix:** give the car its own camera (a child of the car, disabled), enable it and disable the player's camera stack
  (main + arms) while driving; restore on exit. Or minimum: `EyeCamera`, `WeaponSway`, `ViewModelRig` return early
  while `DrivableCar.Driving != null`. With its own camera: copy FOV, far clip (3400 in the desert), post-processing
  and the `EyeAdaptation` meter (it looks for `Camera.main`: tag the car camera `MainCamera` while driving, untag the
  player's).
* **Check:** drive 30 s on keyboard, pad and touch; the view stays behind the car; get out: the view is the player's eye.

### B2 (S1) The chase camera passes through walls
* **Cause:** `DrivableCar.cs:242-245` lerps to a fixed offset with no collision test.
* **Fix:** `Physics.SphereCast` (radius 0.25) from a pivot 1.4 m above the car's centre toward the wanted position,
  against world layers (not Vehicle, Player, Enemy, Civilian); place the camera at `hit.distance - 0.2`. Pull in fast
  (no smoothing toward the car), ease out slowly (0.3 s).

### B3 (S2) Steering snaps to full lock
* `DrivableCar.cs:194-195` writes the target angle straight to `steerAngle`. A key press is 0 to 32 degrees in one
  physics step: twitchy, and it unsettles the car at speed.
* **Fix:** `_steer = Mathf.MoveTowards(_steer, target, steerRate * Time.fixedDeltaTime)` with `steerRate` about 120
  deg/s, return faster (200 deg/s) when the input is released. Keep the speed-narrowed lock.

### B4 (S2) No engine braking or idle brake
* `DrivableCar.cs:189-196`: with no input, throttle 0 and brake 0. The car coasts forever and rolls down any slope
  while the player sits still.
* **Fix:** no throttle: brake torque `0.05 * brakeTorque` on the driven wheels (engine braking); under 1 m/s and no
  input: full brake (hold). Getting out already brakes (line 157); keep that.

### B5 (S2) Flat torque and a hard cut at top speed
* **Fix:** an `AnimationCurve` of torque over rpm and 4-5 gear ratios, or at least torque falling with speed:
  `motor = motorTorque * Mathf.Clamp01(1 - |speed| / topSpeed)^0.5`. Reverse limited to 6 m/s.

### B6 (S2) Wheel physics jitter at low speed
* **Fix:** in `Awake`, `frontLeft.ConfigureVehicleSubsteps(5f, 12, 15);` (it applies to the whole vehicle).
  Also set `Rigidbody.maxAngularVelocity` and keep `interpolation = Interpolate` (already set).

### B7 (S2) Getting out turns on every renderer
* `DrivableCar.cs:143-144, 166` disables every `Renderer` under the player and re-enables them all on exit, including
  ones that were off before (muzzle flash, hidden magazines, shadow-only parts that are toggled). Anything that turns a
  renderer on while driving (weapon draw, ViewModelRig) leaves part of the gun floating.
* **Fix:** store the renderers that were enabled and restore only those; better: while driving, cull the player's
  layers from the car camera (`cullingMask`), touching no renderer.

### B8 (S2) Exit position can be inside a wall or on the roof
* `DrivableCar.cs:160-163` casts down at the door offset and puts the player there. Nothing checks that the capsule
  fits; the cast also hits the car itself if it is on its side.
* **Fix:** try in order: driver side, passenger side, behind, in front, on top; for each, `Physics.CheckCapsule` with
  the controller's radius and height against world + Vehicle layers; first that fits wins; if none, refuse to get out
  and show a hint.

### B9 (S2) Hitting an enemy is hitting a wall (Likely)
* Enemies are NavMeshAgents with colliders; the car's box hits them as fixed obstacles. Nothing knocks a man down.
* **Fix (Stage 1 of the driving feature):** `OnCollisionEnter` on the car: if the other collider's root has
  `EnemyAI`/`Civilian` and relative speed > 4 m/s, apply damage scaled by speed and mass (blunt, `fromMelee`-like so a
  hit can kill), switch it to ragdoll along the car's velocity (`EnemyBody` has the ragdoll path; `Spun` for melee),
  and ignore the collision for 0.5 s so the car rolls on.

### B10 (S2) In the car the player is hidden in a box (Likely)
* While driving, the player's `Health` and hitboxes sit at the car position + 0.3 m (`DrivableCar.cs:246`). Enemy
  sight rays and shots hit the car's `BoxCollider` first, so the player is untouchable and often unseen.
* **Fix:** decide the rule (handoff Stage 2: the body stops rounds, windows do not). Until then: enemies target the
  car (a `Health` on the car, or route damage through to the player with a share), and their sight test treats the car
  as the player.

### B11 (S3) Interact conflicts
* `DrivableCar.cs:101-110`: the same frame's E can be read by the car and by `GunPickup`; the comment says "one press
  does one thing" but only guards one direction (`PickUpReady && Driving == null`). It also works while paused.
* **Fix:** one `Interact` router (e.g. `InteractFocus`): every interactable registers a priority and distance; one
  owner per press. Skip when `Time.timeScale == 0` or `OverlayPanel.AnyOpenThisFrame`.

### B12 (S3) Per-frame costs
* `DrivableCar.cs:233` `new[] { frontLeft, ... }` every frame for every car (parked ones too); line 105
  `FindAnyObjectByType<PlayerMotor>()` each frame while a car is offered; parked cars update wheel visuals every frame.
* **Fix:** cache the array and the motor; skip wheel visuals while `_rb.isKinematic`.

### B13 (S2) Technical truck ignores the world
* `TechnicalTruck.cs:110-119` moves the transform along points: through traffic cars, civilians and the player (a
  kinematic body does not push a CharacterController; the player ends up inside the truck), and it pitches by half the
  slope (`flat.y *= 0.5f`), so the wheels sink on rises.
* Its rounds use `~LayerMask.GetMask("PlayerBounds","FirstPerson")` (line 144): they hit civilians and enemies (see
  C1) and can hit its own colliders.
* **Fix:** sphere-cast ahead and stop for anything in the way (as `TrafficCar` does); ground-follow with four short
  rays (wheel contact points) for pitch and roll; give the gunner a mask that excludes its own truck and civilians
  unless they are in the line of fire to the player (then it is the truck's kill, see C1).

### B14 (S3) Traffic cars float or sink (Likely)
* `TrafficCar.cs:118-131` moves straight between route points; heights between points are interpolated, and roads are
  draped on terrain (`_townBase`). On rolling ground the wheels can hover or sink between sparse points.
* **Fix:** after moving, raycast down at front and rear axle to the road layer; set height and pitch from the hits;
  a small body roll from lateral acceleration. Check the route point spacing in `FPSKitTownRoads` (2-4 m is enough).

---

## 4. People and traffic (C)

### C1 (S1) The player pays for civilians killed by enemies
* **Cause:** `Runtime/Civilian.cs:151` calls `CivilianCasualties.Report()` for every death. Enemy rifles use
  `rangedHitMask = ~(1 << enemyLayer)` (`Editor/FPSKitEnemySetup.cs:185`, `FPSKitSceneBuilder.cs:2479`,
  `FPSKitRiggedSoldier.cs:141`), which includes the Civilian layer; the truck's gun hits them too; objective charges
  (`LevelObjective.cs:642`, attacker null) blast them. Every such death caps the player's stars (2 at one, 1 at three)
  and takes 300 points (`LevelManager.cs:839-844`).
* **Fix:** in `Civilian.OnDied`, report only when the killer is the player:
  ```csharp
  var src = health.LastDamage.source;
  bool byPlayer = src != null && (src.CompareTag("Player") || src.GetComponentInParent<PlayerMotor>() != null);
  if (byPlayer) CivilianCasualties.Report();
  ```
  Check that the player's bomb (`BombProjectile`, `_attacker`) and melee pass the player as `source`. Optionally, make
  enemies hold fire when a civilian is in the line (realistic and fairer): a `SphereCast` on the Civilian layer before
  `EnemyAI` fires.
* **Check:** a play-mode test: an enemy kills a civilian, the result has 3 stars possible and no penalty; the player
  kills one, the cap applies.

### C2 (S2) Spawning by Instantiate/Destroy
* `CivilianDirector.TrySpawn` and `TrafficDirector.TrySpawn` instantiate prefabs while playing and destroy them when
  far. Each instantiate of a skinned civilian or a car is a frame spike (part of F1's 1 % lows).
* **Fix:** pool: create the budget at level start (during the briefing), deactivate instead of destroy, reset state on
  reuse (`Civilian.OnEnable` already resets most). Same for the truck tracer and impact effects (already pooled).

### C3 (S3) Bodies vanish in view
* `CivilianDirector.cs` destroys the oldest body past 20 even if the player is looking at it. Only remove bodies out of
  view (reuse `InView`).

---

## 5. Combat bugs that show in the desert (D)

### D1 (S1) Neck shot does nothing to a weakened enemy
* **Cause:** `Runtime/Hitbox.cs:80` caps a neck round at `Current - max(1, maxHealth * 0.15)`. At or below 15 % health
  that is 0: `Health.ApplyDamage` returns early, no `Damaged` event, the neck wound never opens, and every later light
  neck round also does 0. `BodyDamageProfile.limbCoreFloor` defaults to 0.15, so shooting the legs first leaves him
  exactly there.
* **Fix:**
  ```csharp
  float floor = Mathf.Max(1f, owner.maxHealth * 0.15f);
  if (owner.Current > floor) info.amount = Mathf.Min(info.amount, owner.Current - floor);
  ```
* **Check:** `FPSKitNeckWoundTest.Batch` plus a new case: legs to the floor, then the neck: he dies (or opens and
  bleeds); `VerifyWounds`.

### D2 (S2) Automatic fire rate depends on frame rate
* **Cause:** `Runtime/Weapon.cs:537, 556` `_nextFireTime = Time.time + SecondsBetweenShots`: each shot waits for the
  next frame, and the waiting time is lost. Simulated (10 s held): SMG 900 RPM fires 708 at 30 fps, 816 at 60; LMG 760
  fires 606 at 30, 720 at 60.
* **Fix:** carry the remainder:
  ```csharp
  float interval = SecondsBetweenShots;
  _nextFireTime = (Time.time - _nextFireTime < interval) ? _nextFireTime + interval : Time.time + interval;
  ```
  (simulated: 906 / 762 at every frame rate). The same pattern in `EnemyAI.cs:1093` (cooldowns of a second, less
  affected) and `TechnicalTruck.cs:140` (6 rps, affected at 30 fps).
* **Check:** a play-mode test at `Time.captureDeltaTime = 1/30` and `1/144` counting rounds in 5 s.

### D3 (S2) Blasts through walls
* **Cause:** `Runtime/Explosion.cs:192-245` damages every `Health` in the sphere, no occlusion. A bomb against a house's
  outer wall kills everyone inside; a charge in a shop kills people in the next one.
* **Fix:** per target, `Physics.Linecast(centre + up*0.2, bodyCentre, worldMask)`; if blocked, damage x 0.15 (or 0, his
  call). Measure to the body's centre (the collider bounds of the hips/chest), not `health.transform.position` (the feet).
  Use `OverlapSphereNonAlloc` with a reused buffer and reused sets.

### D4 (S3) Reported damage larger than what landed
* `Runtime/Health.cs:262` returns `info.amount`, the full value before the limb cap, shield and overkill. Damage numbers
  and `DealtDamage` listeners get the full number. Return what was actually removed (`absorbed + remaining`), keep
  `info.amount` for the wound system.

### D5 (S3) Negative score
* `LevelManager.cs:843` can push the score below zero; clamp at 0 in `GameDirector.AddScore`.

### D6 (S3) Double banking on quit
* `GameDirector.ReturnToMenu/RestartRun` record an abandoned result without setting `IsGameOver`; two calls in one
  frame pay twice. Add a `_leaving` flag.

### D7 (S3) Frame-rate dependent decay
* `Mathf.Lerp(x, 0, k * Time.deltaTime)` in `PlayerMotor.cs:544` (recoil) and `Weapon.cs:881` (kickback): use
  `1f - Mathf.Exp(-k * Time.deltaTime)`.

---

## 6. Builders and content (E)

### E1 (S2) Silent white fallback material
* **Cause:** `Editor/FPSKitTownMesh.cs:58`: when a builder passes a null material, the mesh part gets
  `new Material(Shader.Find("Universal Render Pipeline/Lit"))`: unnamed, white (albedo 1.0), not saved as an asset,
  kept in a static (`_fallback`) across `BuildAllThemes`. The audit lists it as `Buildings: Universal Render
  Pipeline/Lit`, 98 instances, flat white, albedo 1.00.
* **Fix:** `Debug.LogError` with the builder's name and the part when this happens, and fix the caller(s) that pass
  null (find them by logging the stack once). Clear `_fallback` at the top of the per-arena build (CLAUDE.md rule).

### E2 (S3) VerifyStatics fails
* `261c706`: fails on `WalkRoute.Desert` (`Runtime/WalkRoute.cs:15`, a `static readonly Vector2[]`, contents mutable)
  and `DrawCounters.Names`. Make them `IReadOnlyList<>` properties returning a copy or wrap in `Array.AsReadOnly`, or
  add them to the check's allow-list with the reason (they are constant data).

### E3 (S2) Kicked-in houses and the navmesh (Check)
* `0c296f6`: house doors are out of the bake and seal; a `Door` carves while bolted. "Kicking a door open in play is
  not yet measured." Add a `VerifyTown` step: open one front door in play mode, ask a path from the street to the room
  behind it; must be complete.

### E4 (S3) The "sky in a room" report (Check)
* `FPSKitTownRoomAudit` (`1cfad04`): 3 of 184,001 floor points meet nothing; `FPSKitShadowLeak`: every roof casts.
  With A1's band explained by probes, re-test the original screenshot spot after A1. If sky still shows, check
  reflection probes there (A2/A4): a room reflecting the sky looks like an open ceiling.

### E5 (S3) Old houses' rooms very dark
* `0c296f6`. After A3 (real captures) and A1, measure them in the room tour; if still under 60/255 adapted, raise
  their lightmap bounce for those rooms only (their walls are mud, albedo ~0.25) or open a window. Do not raise the
  eye's maximum.

---

## 7. Performance (F)

### F1 (S2) Stutter: Medium 1 % low ~20 fps
Known spikes from the code, to check with the Profiler (Timeline, "Deep profile" off) on the release walk:
1. `CivilianDirector`/`TrafficDirector` instantiate and destroy (C2).
2. `RoomContents` toggles hundreds of renderers in one frame when a room comes in range (`RoomContents.cs:178-185`);
   spread the toggles over frames (a queue, 50 renderers a frame).
3. `IndoorReflections` sorts and toggles up to 40 probes every 0.5 s; turning a probe on uploads its cubemap.
4. Shader compilation on first sight: build a `ShaderVariantCollection` from a play-through and warm it during the briefing.
5. GC: B12, F2, `Explosion.Blast` allocations, the burst coroutine's `new WaitForSeconds` per round.
Rule from the handoff: no frame over 33 ms on Medium.

### F2 (S3) Traffic listening allocates every frame
* `TrafficCar.cs:57` passes a lambda capturing `this` to `NoiseSystem.Listen` every frame per car (a new delegate each
  call). Cache it: `Action<NoiseSystem.Noise,float> _onNoise; ... _onNoise ??= OnNoise;`. Same in `Civilian.cs:81`
  (method group converted each call; every 0.25 s, minor).

---

## 8. Realism (G)

From the audit (2026-10-29, 356 objects): **REAL 21, UNCERTAIN 16, NEEDS WORK 213, FAKE 106**. REAL objects cover
about 14 % of what the player sees. The fault codes, how many objects have each and the fix are in section 12;
every object is in section 13.

### G1. What to do, by impact (the order of the handoff's steps 2-5)
1. **Edges (222 objects, ~65 % of the view).** Nothing in the town has a bevel. Add a chamfer to `TownMesh`'s box and
   extrude helpers (2 cm on frames, sills, steps, kerbs, slab edges, parapets; 1 cm on furniture), with a split
   normal on the chamfer so it catches light. Far geometry (over ~30 m): a shader edge-rounding (bent normals at
   mesh edges from a baked edge mask) is cheaper than polygons. Imported props: Blender bevel + weighted normals.
2. **Tiling (66) and flat colours (95).** One shared town surface shader (Shader Graph or HLSL, URP Lit compatible):
   stochastic or hex tiling for the base map, a macro variation map at 8-20 m, a grime gradient toward the ground and
   under sills (by world height and vertex colour), a detail normal. Every flat `TownFlat` material moves to a real CC0
   texture set: `Town_CeilingPaint`, `Town_ShopInsideLit`, `Town_ShopInterior`, `Town_ShutterBox`, `Town_TrimStone_Plain`
   and the rest marked FLAT. Remove the `Town_WeatherDark` strips once grime is in the shader.
3. **Albedo and smoothness (DARK 64, ALB 75, SMOOTH 46, METAL 12).** Mostly imported pack materials (cars' `RMCar*`,
   Poly Haven props): fix in `FPSKitTownAssets`'s remap table (tint = wanted / image mean; metallic 0 on paint).
   Re-audit first: the Oct 31 gloss fix changed many of these.
4. **Glass (A9): real glass, see `Docs/DESERT_GLASS.md`.**
5. **Unrecognised names (59).** Rename materials so the audit can judge them (e.g. `Town_TownSigns` -> `_Paint`).
   Until then they are UNCERTAIN, never fine.
6. **Guns (6, FAKE).** The weapon-pack purchase (handoff 5.5).
7. **Sky (7.9 % of the view, UNCERTAIN).** 11 px/degree; an 8K HDRI or a cloud layer.

### G2. Specific objects worth naming
* **Parked cars** (`Parked_Car_*`): FAKE surfaces, `Town_BurntWreck` on 28 % of an intact parked sedan
  (`Parked_Car_Sedan_B`): check the parked-car merge (`ParkedCarPrefab`) is not picking the wreck material for
  intact cars; `bottom_interior` flat on 47 % of `Parked_Car_Sport`.
* **Tyres and rims of the technical** (`Cylinder (Town_Tyre)`, `Cylinder (Town_Rim)`): Unity primitives. Use the car
  pack's wheels or a modelled tyre.
* **Town_StoneWarm** (most seen world object, 8.3 %): repeats 57x across a face; anti-tiling first.
* **Town_PaversGrey, Town_PlazaStone**: no edge on the pavers' kerbs; plaza stone tiles 57x.

---

## 9. Copilot's review: verdicts

| Claim | Verdict | Evidence |
|---|---|---|
| Only 4 interiors are validated | **Partly out of date** | Every room is lightmapped since `8ab44d3` (6 districts, 319 insides at 20 cm). Only the 4 pilot rooms have their own reflection captures and pilot views; `BatchBand` measures a handful of rooms. |
| 160 house + 752 shop probes share one `IndoorRoom.cubemap` | **True** | `FPSKitTownGI.cs:114-195`; counts from `4981b10` (160 house, 752 shop, 332 dark). See A3. |
| Shop strength by a " dark" suffix (0.7 / 0.25) | **True** | `FPSKitTownGI.cs:182, 274`. See A5. |
| One probe per house spans several rooms and floors | **True** | `FPSKitTownGI.cs:146-163` (union box). See A4. |
| `IndoorReflections` disables indoor probes on Forward (Medium) | **True** | `IndoorReflections.cs:40-46`; Medium uses `Mobile_Renderer` (Forward). See A2. |
| ~2,200 classic probes avoid interiors | **True by design** (count not checked: it is in the binary scene) | `FPSKitTownProbes.cs:24`. On High/Medium (APV) they are unused; they matter on Low/web. See A10. |

## 10. The handoff file vs the repo

| Handoff | Repo |
|---|---|
| 8 arenas incl. Abandoned Subway | 7 (`FPSKitCampaign.ZoneOrder`); Subway was renamed to Fairground (`SaveMigration`), Unknown Planet is Mars Colony's display name |
| 8 levels per arena | 32 (`Levels_DesertOutpost.asset`) |
| `.github/copilot-instructions.md`, `Docs/HANDOFF_*.md` | No copilot file; only `HANDOFF_PLAYER_MELEE.md` |
| `LICENSES.txt` | `LICENSES.md` |
| GitHub stuck at `59fb207` | GitHub `main` = `261c706` (Nov 4) |
| Interior lightmaps only in the pilot | All rooms since `8ab44d3` (Nov 3) |
| Oct 31 bugs open: white blood, debug unlock, invalid worldAABB | Fix commits: `fd22ef0` (blood, debug unlock compiled out of builds), `a7a1acb` (impact-mark bounds, NaN tracer). Confirm in a fight. |

## 11. Suggested order of work

Each step: one change, rebuild what it needs, run its check, commit, then the developer looks.

1. **Quick code fixes, no rebuild** (one commit each): D1, D2, C1, B1, B2, B3, B4, B6, B7, B8, B11, B12, D4-D7, E2, F2.
   Checks: `VerifyCombat`, `VerifyWounds`, `FPSKitNeckWoundTest.Batch`, `VerifyStatics`, `VerifyControls`, a drive test.
2. **Re-run the realism audit** and regenerate sections 12-13 with `Tools/desert_review_objects.py`.
3. **E1 fallback material**: generator fix, desert rebuild. (A9 glass: `Docs/DESERT_GLASS.md`, its own stages G0-G8.)
4. **A2 + A12** (Medium Forward+ renderer): measure Medium GPU.
5. **A4 then A3** (per-room probes, then per-room captures in the district bakes), A5.
6. **A1** (probe layers + indoor probe density + runtime layer for characters), then **A7, A6, A8** (in that order: each
   removes a patch the previous fix makes unnecessary).
7. **A10** (Low/web).
8. **C2, F1** (pooling, toggle spreading, shader warm-up), D3 (blast occlusion), B5, B9, B10, B13, B14.
9. **Realism G1** steps 1-7.

Batch list for the laptop (Unity closed, one at a time, watchdog on):
```
Tools/unity-batch.sh FPSKitBatch.VerifyCombat
Tools/unity-batch.sh FPSKitBatch.VerifyWounds
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitNeckWoundTest.Batch
Tools/unity-batch.sh FPSKitBatch.VerifyStatics
Tools/unity-batch.sh FPSKitBatch.VerifyTown
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.AuditRealism -fpskitTheme "Desert Outpost"
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitShadowLeak.BatchBand -force-vulkan
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchLightingTour -fpskitTag review
```
(Method names checked against the repo on 2026-11-04.)

---

## 12. Realism: fault codes

The screen share column adds up each object's share of the view; the six guns count while held (about 7-12 % each),
which is why PALETTE, DARK, TEXEL and POLY look large.

| Code | Objects | Combined screen share | Fix |
|---|---|---|---|
| **EDGE** | 222 | 64.5 % | Bevel the hard edges: 1-3 cm chamfer on near geometry (add a chamfered box/extrude helper to FPSKitTownMesh and use it for frames, sills, kerbs, steps, slabs); far geometry: rounded-edge normals in the shader (bevel normal trick) instead of real polygons; imported models: Blender Bevel modifier (2 segments, 1-2 cm) + Weighted Normals, then re-export. |
| **FLAT** | 95 | 14.5 % | Give it a real surface: a CC0 PBR set (ambientCG / Poly Haven) with albedo + normal + roughness/AO at world-metre UVs, tinted so the mean albedo matches the real material; no flat Color-only materials. |
| **PALETTE** | 8 | 54.5 % | Replace the palette (colour-swatch) texture with a real PBR texture set; for guns this is the planned weapon-pack purchase. |
| **BOX** | 70 | 4.5 % | Replace plain boxes with profiled shapes: panelled doors, moulded sills and cornices, beam ceilings, rounded slab edges, real prop meshes. Keep plain boxes only as shadow/collision stand-ins. |
| **TILE** | 66 | 32.0 % | Break the tiling: shared anti-tiling shader (stochastic/hex tiling or two-scale blend), macro variation noise at 8-20 m, grime/dirt toward the ground by vertex colour or height, a detail normal map. |
| **DARK** | 64 | 57.9 % | Albedo too dark (below URP 0.01): raise the darkest texels / tint so p2 >= 0.02 and the mean sits inside the real range (tint = wanted colour / image mean). |
| **BRIGHT** | 5 | 0.1 % | Albedo too bright (above URP 0.90): cap the texture/tint (albedoCap) so p98 <= 0.85. |
| **ALB** | 75 | 5.3 % | Albedo outside the real range for its kind: retint to the middle of the range given in the reason. |
| **SMOOTH** | 46 | 8.1 % | Smoothness outside the real range: set it inside the range given (matte plaster/stone/sand 0.05-0.2; painted metal 0.3-0.5). |
| **METAL** | 12 | 0.9 % | Painted metal must be metallic 0 (the paint is a dielectric). |
| **METALQ** | 9 | 0.4 % | Decide: bare metal -> metallic 1, albedo 0.5-0.9; painted -> rename the material to say painted and set metallic 0. |
| **AO** | 16 | 2.1 % | Smooth without an occlusion map: add an AO map (bake in Blender or use the pack AO) or lower smoothness; otherwise crevices glow. |
| **NRM** | 48 | 6.0 % | Normal map too weak: raise normal strength (bumpScale 1-2) or use the texture set's real normal map; generated grayscale normals need _NORMALMAP on. |
| **NONRM** | 12 | 2.7 % | Add a normal map (it is seen up close). |
| **TEXEL** | 12 | 55.5 % | Too few texels per metre for how close it is seen: use a 2K map or tile finer (keep >= 256 px/m near, 512 for things within 1 m). |
| **GUNPOLY** | 6 | 54.2 % | Held gun too low-poly: replace with the planned realistic weapon pack (20-60k tris, 2-4K PBR, separate mag/bolt/trigger), keeping ViewModelRig grip points and reload beats. |
| **POLY** | 8 | 54.9 % | Too few triangles for how close it is seen: higher-detail model or LOD0 from the source pack. |
| **PRIM** | 2 | 0.0 % | Unity primitive: replace with a modelled mesh (tyre with tread and sidewall, rim with spokes) or the source pack's own part. |
| **NAME** | 59 | 3.2 % | The audit could not tell what it is: rename the material with its kind (e.g. _Plaster, _Fabric, _PaintedMetal) or add it to the audit name map, then re-run the audit so its albedo/smoothness are checked. |
| **GLASS** | 2 | 1.2 % | Glass draws as a black hole: real glass (Docs/DESERT_GLASS.md): transparent, Fresnel reflection, dust, no shadow, and real space behind it (the real room, or a window room built behind it). |
| **RAKE** | 1 | 2.0 % | Flat under raking light: add a subtle normal/height detail so low sun shows relief. |
| **SAT** | 3 | 0.2 % | Colour too saturated/deep for the material: desaturate so the weakest channel is >= 5 % of the strongest. |
| **OVER** | 2 | 3.3 % | An overlay (dark weathering strips): planned for removal; replace with the anti-tiling shader's grime instead. |
| **SKY** | 1 | 7.9 % | Sky panorama is soft (11 px/deg vs 18 on 1080p): use an 8K HDRI or add a cloud layer/procedural sky detail. |
| **VALID** | 1 | 3.5 % | URP albedo validation flags it: bring texels into 0.01-0.90. |

## 13. Realism: every object

Sorted by the audit rank (most seen first). Verdicts are Shape / Surface / Light / Overall. "Where" is the generator file and line that defines the material (generated surfaces), else the builder or asset step. Sub-materials of imported models are listed with their own codes.

| # | Object | Group | Kind | Shape | Surface | Light | Overall | Screen % | Nearest m | Faults | Sub-materials | Where to fix |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Gun: Gun_LMG (VM_M249) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 12.35 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 2 | Gun: Gun_SMG (VM_Uzi) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 11.96 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 3 | First-person arms (FP_Arms) | Player | fabric | REAL | REAL | REAL | **REAL** | 9.04 | 0.5 | - | - | weapon pack / ViewModelRig |
| 4 | Landmarks: Town_StoneWarm | Landmarks | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 8.31 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:26` |
| 5 | Gun: Gun_Burst (VM_AK74) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 8.06 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 6 | Sky: TownSky | Sky | sky | REAL | UNCERTAIN | REAL | **UNCERTAIN** | 7.90 |  | SKY | - | LevelTheme sky (FPSKitThemes) |
| 7 | Roads: Town_AsphaltDusty | Roads | asphalt | REAL | REAL | REAL | **REAL** | 7.81 | 1.5 | - | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:34` |
| 8 | Gun: Gun_Shotgun (VM_Bennelli_M4) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 7.59 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 9 | Gun: Gun_DMR (VM_M107) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 7.22 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 10 | Gun: TestRifle (VM_M4_8) | Player | gun finish | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 7.03 | 0.5 | PALETTE,DARK,TEXEL,GUNPOLY,POLY | - | weapon pack / ViewModelRig |
| 11 | Roads: Town_Asphalt | Roads | asphalt | REAL | REAL | REAL | **REAL** | 6.99 | 1.5 | - | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:32` |
| 12 | Roads: Town_PaversGrey | Roads | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 6.70 | 1.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:37` |
| 13 | Landmarks: Town_PlazaStone | Landmarks | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 4.84 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:38` |
| 14 | Buildings: Town_ShopGlass | Buildings | glass | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 3.51 | 0.9 | VALID | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:119` |
| 15 | Buildings: Town_PaintWhite | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 3.47 | 1.0 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:94` |
| 16 | Buildings: Town_WeatherDark | Buildings | decal | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 3.23 | 1.5 | OVER | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:81` |
| 17 | Buildings: Town_CladStoneSlip | Buildings | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 2.80 | 1.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:68` |
| 18 | Buildings: Town_ShutterBox | Buildings | painted metal | NEEDS WORK | FAKE | REAL | **FAKE** | 2.17 | 1.5 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:51` |
| 19 | Buildings: Town_ShopInsideLit | Buildings | plaster | NEEDS WORK | FAKE | REAL | **FAKE** | 2.07 | 1.5 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:130` |
| 20 | Buildings: Town_FoundationConcrete | Buildings | concrete | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 2.06 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:113` |
| 21 | Buildings: Town_CeilingPaint | Buildings | plaster | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 2.01 | 1.4 | EDGE,FLAT,RAKE | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:30` |
| 22 | Buildings: Town_CladGranite | Buildings | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 1.95 | 1.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:67` |
| 23 | Buildings: Town_TrimStone | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.67 | 1.5 | EDGE,BOX,TILE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:45` |
| 24 | Buildings: Town_BrickSand | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.38 | 1.3 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:38` |
| 25 | Ground: Layer_gravelly_sand | Terrain | sand | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 1.34 | 1.6 | TILE | - | FPSKitTownGround (terrain layers) |
| 26 | Landmarks: Town_ShopInterior | Landmarks | plaster | REAL | FAKE | REAL | **FAKE** | 1.27 | 1.5 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:152` |
| 27 | Buildings: Town_DoorWood | Buildings | wood | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.24 | 1.2 | EDGE,BOX,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:137` |
| 28 | Buildings: Town_PaintBlue | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 1.20 | 1.0 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:98` |
| 29 | Buildings: Town_PlasterBeige | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.18 | 1.5 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:90` |
| 30 | Buildings: Town_BrickBuff | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.08 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:37` |
| 31 | Buildings: Town_RusticStone | Buildings | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 1.07 | 1.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:42` |
| 32 | Buildings: Town_BrickYellow | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 1.02 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:43` |
| 33 | Buildings: Town_TrimStone_Plain | Buildings | stone | NEEDS WORK | FAKE | REAL | **FAKE** | 0.99 | 1.5 | EDGE,FLAT | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 34 | Roads: Town_Kerb | Roads | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.96 | 1.9 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:39` |
| 35 | Buildings: Town_Terrazzo | Buildings | polished stone | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.96 | 1.5 | EDGE,TILE,SMOOTH,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:113` |
| 36 | Buildings: Town_ShopInterior | Buildings | plaster | NEEDS WORK | FAKE | REAL | **FAKE** | 0.84 | 1.5 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:152` |
| 37 | Buildings: Town_RusticDressed | Buildings | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.83 | 1.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:43` |
| 38 | Buildings: Town_TownSigns | Buildings | unknown | REAL | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.78 | 1.8 | NONRM,TEXEL,NAME | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:154` |
| 39 | Buildings: Town_GlassBlue | Buildings | glass | REAL | REAL | FAKE | **FAKE** | 0.73 | 1.4 | GLASS | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:122` |
| 40 | Buildings: Town_AshlarHoney | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.71 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:41` |
| 41 | Buildings: Town_CladFacadeTiles | Buildings | polished stone | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.69 | 1.5 | EDGE,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:69` |
| 42 | Buildings: Town_PlasterStucco | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.67 | 1.5 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:89` |
| 43 | Buildings: Town_Coping | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.65 | 1.7 | EDGE,BOX,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:112` |
| 44 | Buildings: Town_BrickYellowHoney | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.63 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:44` |
| 45 | Buildings: Town_ShopShutterGrey | Buildings | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.63 | 1.5 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:140` |
| 46 | Buildings: Town_FrameCharcoal | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.60 | 1.2 | EDGE,FLAT,DARK,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:47` |
| 47 | Palm_SingleStraight | Buildings | vegetation | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.56 | 2.1 | POLY | - | FPSKitTownAssets (material remap) / source asset |
| 48 | Buildings: Town_FloorChecker | Buildings | polished stone | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.55 | 1.5 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:106` |
| 49 | Parked_Car_Sedan_B | Life | skin | REAL | FAKE | NEEDS WORK | **FAKE** | 0.51 | 0.8 | FLAT,DARK,ALB,SMOOTH,AO,NONRM,NAME | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO,NONRM; Town_BurntWreck: FLAT,DARK,NAME; Town_WreckHole: FLAT,DARK,NAME; Town_RustBody: ALB,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 50 | Ground: Layer_dry_ground_rocks | Terrain | stone | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.51 | 1.6 | TILE | - | FPSKitTownGround (terrain layers) |
| 51 | Buildings: Town_FloorBeigeTiles | Buildings | polished stone | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.50 | 1.5 | TILE,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:107` |
| 52 | Buildings: Town_PlasterWhite | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.50 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:88` |
| 53 | Buildings: Town_FloorDiagonal | Buildings | polished stone | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.49 | 1.5 | TILE,SMOOTH,NRM | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:109` |
| 54 | Buildings: Town_GlassDark | Buildings | glass | REAL | REAL | FAKE | **FAKE** | 0.45 | 1.7 | GLASS | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:121` |
| 55 | Buildings: Town_FloorParquet | Buildings | unknown | REAL | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.45 | 1.5 | NRM,NAME | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:110` |
| 56 | Buildings: Town_ShopShutterBrown | Buildings | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.40 | 1.5 | ALB,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:144` |
| 57 | Landmarks: Town_Limestone | Landmarks | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.37 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:104` |
| 58 | Buildings: Town_FrameBronze | Buildings | bare metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.36 | 1.2 | EDGE,FLAT,DARK,ALB,METALQ | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:46` |
| 59 | Buildings: Town_AshlarLimestone | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.36 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:40` |
| 60 | Buildings: Town_StoneWarm | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.35 | 1.1 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:26` |
| 61 | Buildings: Town_FrameAluminium | Buildings | bare metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.34 | 1.4 | EDGE,FLAT,AO | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:128` |
| 62 | Buildings: Town_ConcreteBand | Buildings | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.33 | 2.5 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:109` |
| 63 | Buildings: Town_ShopShutterSand | Buildings | sand | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.33 | 1.5 | ALB | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:143` |
| 64 | Parked_Car_Sedan_A | Life | painted metal | REAL | FAKE | NEEDS WORK | **FAKE** | 0.33 | 0.8 | FLAT,DARK,ALB,SMOOTH,AO,NONRM,NAME | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO,NONRM; Town_RustBody: ALB,SMOOTH; Town_BurntWreck: FLAT,DARK,NAME; Town_WreckHole: FLAT,DARK,NAME | FPSKitTownAssets (material remap) / source asset |
| 65 | Buildings: Town_StoneWhiteBlocks | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.33 | 1.8 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:51` |
| 66 | Buildings: Town_ShopShutterBlue | Buildings | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.33 | 1.5 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:142` |
| 67 | Buildings: Town_BrickPale | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.29 | 1.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:45` |
| 68 | Buildings: Town_TownAwnings | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.27 | 1.2 | EDGE,PALETTE,TEXEL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:156` |
| 69 | Buildings: Town_OldSandstone | Buildings | sand | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.27 | 15.3 | TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:75` |
| 70 | Buildings: Town_ShopShutterGreen | Buildings | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.26 | 1.5 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:141` |
| 71 | Buildings: Town_PlasterGrey | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.25 | 14.3 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:94` |
| 72 | Roads: Town_RoadPaint | Roads | plaster | REAL | FAKE | REAL | **FAKE** | 0.25 | 1.5 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:40` |
| 73 | Parked_Car_Sedan_C | Life | skin | REAL | FAKE | NEEDS WORK | **FAKE** | 0.24 | 1.0 | FLAT,DARK,ALB,SMOOTH,AO,NONRM,NAME | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO,NONRM; Town_BurntWreck: FLAT,DARK,NAME; Town_WreckHole: FLAT,DARK,NAME; Town_RustBody: ALB,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 74 | Buildings: Town_Coping_Plain | Buildings | stone | NEEDS WORK | FAKE | REAL | **FAKE** | 0.22 | 1.1 | EDGE,FLAT | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 75 | Buildings: Town_FloorGreyTiles | Buildings | polished stone | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.21 | 1.5 | SMOOTH,NRM | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:108` |
| 76 | Buildings: Town_StallGranite | Buildings | stone | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.20 | 2.0 | EDGE,FLAT,BOX,ALB,SMOOTH,AO | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:115` |
| 77 | Buildings: Town_PlasterRoughWhite | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.20 | 1.3 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:57` |
| 78 | Buildings: Town_FrameGreen | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.20 | 1.5 | EDGE,FLAT,ALB | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:48` |
| 79 | Buildings: Town_BrickArch_Plain | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.19 | 2.8 | EDGE,NONRM | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 80 | Buildings: Town_PlasterSand | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.19 | 19.0 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:95` |
| 81 | Parked_Car_Sedan_D | Life | skin | REAL | FAKE | NEEDS WORK | **FAKE** | 0.19 | 1.2 | FLAT,DARK,ALB,SMOOTH,AO,NONRM,NAME | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO,NONRM; Town_BurntWreck: FLAT,DARK,NAME; Town_WreckHole: FLAT,DARK,NAME; Town_RustBody: ALB,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 82 | Buildings: Town_BrickPlastered | Buildings | brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.19 | 18.7 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:46` |
| 83 | wicker_basket_01 | Buildings | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.19 | 1.5 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 84 | wooden_crate_02 | Landmarks/LandmarkProps | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.18 | 1.4 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 85 | Buildings: Town_StallMarble | Buildings | stone | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.18 | 1.8 | EDGE,FLAT,SMOOTH,AO | - | `Assets/Scripts/Editor/FPSKitTownShopfront.cs:147` |
| 86 | vintage_cabinet_01 | Buildings | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.17 | 1.5 | EDGE,DARK,ALB,SMOOTH,NRM,TEXEL | PH_vintage_cabinet_01_b: DARK,ALB,SMOOTH,NRM; PH_vintage_cabinet_01_a: DARK,ALB,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 87 | Buildings: Town_FrameWood_Plain | Buildings | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.17 | 1.0 | EDGE,DARK,ALB,SMOOTH,NONRM,SAT | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 88 | Car_Sedan_B | Life | skin | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.16 | 1.5 | DARK,ALB,SMOOTH,AO,NONRM | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO,NONRM | FPSKitTownAssets (material remap) / source asset |
| 89 | plastic_crate_02 | Buildings | plastic | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.16 | 1.8 | EDGE,DARK | - | FPSKitTownAssets (material remap) / source asset |
| 90 | Street: Town_Galvanised | Street | bare metal | REAL | FAKE | REAL | **FAKE** | 0.15 | 1.8 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:134` |
| 91 | metal_trash_can | Edge | painted metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.15 | 1.4 | EDGE,DARK,SMOOTH | PH_metal_trash_can_rust: DARK,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 92 | Buildings: Town_CurtainWhite | Buildings | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.14 | 1.5 | EDGE,FLAT,ALB | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:39` |
| 93 | Buildings: Town_CabinetWhite | Buildings | wood | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.14 | 1.5 | EDGE,FLAT,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:117` |
| 94 | plastic_monobloc_chair_01 | Landmarks/LandmarkProps | plastic | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.14 | 1.1 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 95 | Buildings: Town_DoorMetal | Buildings | painted metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.14 | 1.3 | EDGE,BOX,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:138` |
| 96 | Buildings: Town_MudBrick | Buildings | mud brick | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.13 | 15.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:73` |
| 97 | Buildings: Town_FrameDark | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.13 | 0.9 | EDGE,FLAT,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:130` |
| 98 | Buildings: Universal Render Pipeline/Lit | Buildings | plaster | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.13 | 1.5 | EDGE,FLAT,BRIGHT,ALB,SMOOTH | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 99 | steel_frame_shelves_01 | Buildings | painted metal | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.12 | 3.3 | NRM | - | FPSKitTownAssets (material remap) / source asset |
| 100 | Roads: Town_MedianGravel | Roads | sand | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.12 | 5.9 | ALB | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:42` |
| 101 | Roads: Town_PlazaStone | Roads | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.11 | 17.0 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:38` |
| 102 | Buildings: Town_BareBlock | Buildings | concrete | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.11 | 1.8 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:79` |
| 103 | Buildings: Town_CurtainSand | Buildings | sand | REAL | FAKE | REAL | **FAKE** | 0.11 | 0.9 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:37` |
| 104 | cardboard_box_01 | Buildings | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.11 | 1.7 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 105 | outdoor_table_chair_set_01 | Buildings | wood | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.10 | 1.1 | EDGE,NRM | PH_outdoor_table_chair_set_01_chair: NRM; PH_outdoor_table_chair_set_01_table: NRM | FPSKitTownAssets (material remap) / source asset |
| 106 | painted_wooden_table | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.10 | 2.4 | EDGE,ALB,SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 107 | Street: Town_PoleWood | Street | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.09 | 9.1 | EDGE,TILE,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:473` |
| 108 | utility_box_02 | Street | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.09 | 1.2 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 109 | Buildings: Town_ShopShutter | Buildings | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.09 | 2.1 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:141` |
| 110 | Buildings: Town_WroughtIron | Buildings | painted metal | REAL | FAKE | NEEDS WORK | **FAKE** | 0.09 | 3.2 | FLAT,DARK,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:50` |
| 111 | Buildings: Town_CurtainTeal | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.08 | 0.7 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:39` |
| 112 | Palm_SingleBended | Buildings | vegetation | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.08 | 3.3 | POLY | - | FPSKitTownAssets (material remap) / source asset |
| 113 | Street: Town_Wire | Street | unknown | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.08 | 6.1 | EDGE,FLAT,DARK,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:474` |
| 114 | Buildings: Town_GlassBlinds | Buildings | glass | REAL | REAL | REAL | **REAL** | 0.08 | 3.3 | - | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:126` |
| 115 | Buildings: Town_Carpets | Buildings | fabric | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.08 | 0.9 | NONRM | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 116 | Buildings: Town_GlassCurtain | Buildings | glass | REAL | REAL | REAL | **REAL** | 0.08 | 0.9 | - | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:125` |
| 117 | Buildings: Town_CurtainWine | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.08 | 1.5 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:38` |
| 118 | exterior_aircon_unit | Buildings | unknown | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.07 | 2.9 | EDGE,DARK,SMOOTH,NAME | PH_exterior_aircon_unit_02: DARK,NAME; PH_exterior_aircon_unit_01: DARK,NAME; PH_exterior_aircon_unit_rusted_02: DARK; PH_exterior_aircon_unit_rusted_01: DARK,SMOOTH | FPSKitTownAssets (material remap) / source asset |
| 119 | wooden_crate_01 | Buildings | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.07 | 1.6 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 120 | Buildings: Town_PlasterPitted | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.07 | 3.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:62` |
| 121 | street_lamp_01 | Street | painted metal | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.06 | 1.5 | EDGE,DARK,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 122 | Buildings: Town_WallTilesWhite | Buildings | polished stone | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.06 | 2.9 | EDGE,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:113` |
| 123 | painted_wooden_chair_01 | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.06 | 1.5 | EDGE,SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 124 | modular_street_seating | Street | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.06 | 1.0 | EDGE,DARK,NRM,NAME | PH_modular_street_seating_connectors: DARK,NRM,NAME; PH_modular_street_seating_supports: NAME; PH_modular_street_seating_timber: NRM | FPSKitTownAssets (material remap) / source asset |
| 125 | Buildings: Town_Limestone | Buildings | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.06 | 21.8 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:104` |
| 126 | wooden_display_shelves_01 | Buildings | wood | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.05 | 3.9 | NRM | - | FPSKitTownAssets (material remap) / source asset |
| 127 | portable_generator | Edge | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.05 | 1.4 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 128 | Buildings: Town_FrameWhite | Buildings | painted metal | NEEDS WORK | FAKE | REAL | **FAKE** | 0.05 | 2.0 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:129` |
| 129 | Buildings: Town_FrameWood | Buildings | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.05 | 1.7 | EDGE,BOX,DARK,ALB,SMOOTH,NRM,SAT | - | `Assets/Scripts/Editor/FPSKitTownFacade.cs:49` |
| 130 | wooden_military_crate | Landmarks/LandmarkProps | wood | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.04 | 1.7 | EDGE,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 131 | Buildings: Town_ConcreteBand_Plain | Buildings | concrete | REAL | FAKE | REAL | **FAKE** | 0.04 | 2.1 | FLAT | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 132 | Pallet | ShopCover | wood | FAKE | REAL | NEEDS WORK | **FAKE** | 0.04 | 2.3 | EDGE,BOX,DARK,ALB,SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 133 | Buildings: Town_PlasterCream | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.04 | 1.1 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:91` |
| 134 | Buildings: Town_CurtainOlive | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.04 | 2.6 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:38` |
| 135 | Street: Town_SignalBody | Street | skin | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.04 | 1.9 | FLAT,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:32` |
| 136 | Mountains: Town_Mountain | Mountains | stone | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.04 | 571.7 | EDGE,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownGround.cs:626` |
| 137 | painted_wooden_cabinet | Buildings | plaster | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.04 | 2.3 | EDGE,DARK,ALB,TEXEL | - | FPSKitTownAssets (material remap) / source asset |
| 138 | Buildings: Town_WeatherLight | Buildings | decal | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.04 | 2.4 | OVER | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:82` |
| 139 | plastic_crate_01 | Landmarks/LandmarkProps | plastic | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.04 | 1.7 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 140 | utility_box_01 | Street | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.03 | 1.1 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 141 | Street: Town_Kerb | Street | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.03 | 2.6 | EDGE,BOX | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:39` |
| 142 | Buildings: Town_Mattress | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.03 | 2.0 | FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:123` |
| 143 | folding_wooden_stool | Landmarks/LandmarkProps | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.03 | 3.0 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 144 | Buildings: Town_PlasterWornOld | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.03 | 15.2 | EDGE,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:106` |
| 145 | Buildings: Town_PlasterWhiteFlat | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.03 | 3.5 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:61` |
| 146 | Buildings: Town_PlasterGreyRender | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.03 | 14.7 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:58` |
| 147 | Buildings: Town_PlasterWorn | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.03 | 23.7 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:93` |
| 148 | Buildings: Town_MudDark | Buildings | mud brick | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.03 | 16.3 | EDGE,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:105` |
| 149 | Buildings: Town_ClothOlive | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.03 | 1.9 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:127` |
| 150 | Street: Town_SteelDark | Street | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.02 | 3.4 | FLAT,BOX,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:131` |
| 151 | Buildings: Town_ClothNavy | Buildings | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.02 | 1.9 | EDGE,FLAT,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:126` |
| 152 | Ground: Layer_farm_soil | Terrain | sand | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.02 | 1.6 | TILE,ALB | - | FPSKitTownGround (terrain layers) |
| 153 | Buildings: Town_OldCracked | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.02 | 30.0 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:76` |
| 154 | Buildings: Town_ClothRed | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.02 | 2.4 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:126` |
| 155 | Landmarks: Town_Coping | Landmarks | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.02 | 19.1 | EDGE,BOX,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:112` |
| 156 | Street: Town_TownRoadSigns | Street | asphalt | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.02 | 1.0 | ALB,NONRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:155` |
| 157 | trashbag | Buildings | unknown | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.02 | 1.7 | DARK,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 158 | wooden_bookshelf_worn | Buildings | wood | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.02 | 3.1 | EDGE,TEXEL | - | FPSKitTownAssets (material remap) / source asset |
| 159 | Buildings: Town_OldPitted | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.02 | 14.7 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:74` |
| 160 | Buildings: Town_GateBlue | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.0 | EDGE,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:140` |
| 161 | Buildings: Town_TankWhite | Buildings | painted metal | NEEDS WORK | FAKE | REAL | **FAKE** | 0.01 | 2.4 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:146` |
| 162 | Buildings: Town_WoodShutter | Buildings | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.01 | 14.9 | EDGE,BOX,SMOOTH,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:143` |
| 163 | Ground: Layer_rock_ground | Terrain | stone | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.01 | 1.6 | TILE | - | FPSKitTownGround (terrain layers) |
| 164 | Buildings: Town_GateGreen | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.6 | EDGE,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:139` |
| 165 | Buildings: Town_ClothMaroon | Buildings | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.01 | 2.7 | EDGE,FLAT,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:127` |
| 166 | ceramic_pot | Buildings | polished stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.01 | 1.9 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 167 | wooden_stool_01 | Buildings | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.01 | 3.0 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 168 | Buildings: Town_PlasterCracked | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.01 | 37.1 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:60` |
| 169 | Street: Town_TownSigns | Street | unknown | REAL | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.01 | 3.6 | NONRM,TEXEL,NAME | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:154` |
| 170 | Buildings: Town_MudPattern | Buildings | mud brick | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.01 | 16.6 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:103` |
| 171 | Buildings: Town_ClothSand | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.01 | 2.1 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:128` |
| 172 | Buildings: Town_MudPlaster | Buildings | mud brick | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 22.9 | EDGE,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:102` |
| 173 | Television_01 | Buildings | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.01 | 2.5 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 174 | ceiling_fan | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.2 | EDGE,DARK,ALB,SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 175 | Buildings: Town_SteelDark | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.01 | 1.4 | EDGE,FLAT,BOX,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:131` |
| 176 | water_manhole_cover | Street | water | REAL | REAL | REAL | **REAL** | 0.01 | 4.1 | - | - | FPSKitTownAssets (material remap) / source asset |
| 177 | ceramic_vase_02 | Buildings | polished stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.01 | 1.9 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 178 | sofa_02 | Buildings | fabric | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.0 | DARK,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 179 | WoodenTable_01 | Buildings | wood | REAL | REAL | REAL | **REAL** | 0.01 | 3.4 | - | - | FPSKitTownAssets (material remap) / source asset |
| 180 | Buildings: Town_PalmBeam | Buildings | vegetation | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 15.5 | EDGE,BOX,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:144` |
| 181 | Lantern_01 | Buildings | bare metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.4 | EDGE,ALB,SMOOTH,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 182 | brass_vase_01 | Buildings | bare metal | NEEDS WORK | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.01 | 1.8 | EDGE,METALQ,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 183 | Landmarks: Town_TownSigns | Landmarks | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.01 | 10.2 | NAME | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:154` |
| 184 | ceramic_vase_03 | Buildings | polished stone | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 2.3 | SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 185 | antique_ceramic_vase_01 | Buildings | polished stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.01 | 1.7 | EDGE,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 186 | ceramic_vase_04 | Buildings | polished stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.01 | 1.9 | EDGE,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 187 | Street: Town_LampHead | Street | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.01 | 7.0 | EDGE,FLAT,BOX,METAL | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:135` |
| 188 | brass_pot_01 | Buildings | bare metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.01 | 3.1 | EDGE,DARK,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 189 | Landmarks: Town_StuccoWhite | Landmarks | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.01 | 164.7 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:27` |
| 190 | brass_goblets | Buildings | bare metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.9 | DARK,ALB,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 191 | brass_diya_lantern | Buildings | bare metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 1.8 | EDGE,DARK,ALB,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 192 | Buildings: Town_PlasterPeach | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 126.5 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:96` |
| 193 | Landmarks: Town_TownClock | Landmarks | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 26.4 | NAME | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:35` |
| 194 | Landmarks: Town_DomeTile | Landmarks | polished stone | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 15.2 | SMOOTH,NRM | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:28` |
| 195 | Buildings: Town_FloorBathMosaic | Buildings | stone | REAL | REAL | REAL | **REAL** | 0.00 | 5.4 | - | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:114` |
| 196 | Buildings: Town_PlasterOchre | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 42.4 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:92` |
| 197 | plastic_bottle_gallon | Landmarks/LandmarkProps | plastic | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 2.6 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 198 | painted_wooden_shelves | Buildings | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 6.1 | EDGE,DARK,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 199 | Buildings: Town_CinderBlock | Buildings | concrete | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 6.6 | EDGE,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:110` |
| 200 | plastic_jerrycan | Buildings | plastic | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 2.6 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 201 | Street: Town_LampLens | Street | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 6.9 | EDGE,FLAT,BOX,SMOOTH,AO | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:136` |
| 202 | Buildings: Town_TankBlack | Buildings | painted metal | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 2.9 | FLAT,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:145` |
| 203 | brass_vase_03 | Buildings | bare metal | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 2.9 | EDGE,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 204 | Barrel_01 | Buildings | unknown | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 14.0 | DARK,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 205 | Buildings: Town_LampGlow | Buildings | painted metal | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 2.0 | FLAT,AO | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:121` |
| 206 | ceramic_vase_01 | Buildings | polished stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 2.3 | EDGE,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 207 | vintage_suitcase | Buildings | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 2.0 | EDGE,NAME | PH_vintage_suitcase_01: NAME; PH_vintage_suitcase_02: NAME | FPSKitTownAssets (material remap) / source asset |
| 208 | wooden_bowl_01 | Buildings | wood | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.2 | EDGE,DARK | - | FPSKitTownAssets (material remap) / source asset |
| 209 | jug_01 | Buildings | unknown | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.4 | EDGE,BRIGHT,NRM,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 210 | Edge: Town_DryScrub | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 8.9 | EDGE,FLAT,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:522` |
| 211 | Landmarks: Town_Kerb | Landmarks | concrete | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 131.7 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:39` |
| 212 | Edge: Town_ForecourtConcrete | Edge | concrete | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 19.9 | EDGE,TILE,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:23` |
| 213 | tea_set_01 | Buildings | unknown | NEEDS WORK | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.00 | 2.2 | EDGE,NRM,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 214 | covered_car | Life | unknown | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 30.1 | DARK,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 215 | brass_pot_02 | Buildings | bare metal | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 2.9 | EDGE,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 216 | Buildings: Town_PaintWhitewash | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 15.1 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:102` |
| 217 | pot_enamel_01 | Buildings | ceramic ware | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.5 | EDGE,DARK,NRM | - | FPSKitTownAssets (material remap) / source asset |
| 218 | brass_vase_02 | Landmarks/LandmarkProps | bare metal | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 3.2 | EDGE,METALQ | - | FPSKitTownAssets (material remap) / source asset |
| 219 | Buildings: Town_PaintMudInside | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 15.4 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:103` |
| 220 | barrel_03 | Buildings | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 23.0 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 221 | Street: Town_SignalOff | Street | unknown | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 2.1 | FLAT,DARK,NAME | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:33` |
| 222 | Buildings: Town_Rebar | Buildings | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 4.4 | EDGE,FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:132` |
| 223 | Edge: Town_FrameAluminium | Edge | bare metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 41.8 | EDGE,FLAT,BOX,AO | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:128` |
| 224 | Street: Town_SignalGreen | Street | unknown | REAL | FAKE | UNCERTAIN | **FAKE** | 0.00 | 2.0 | FLAT,NAME | - | `Assets/Scripts/Editor/FPSKitTownStreet.cs:36` |
| 225 | Edge: Town_DeadScrub | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 42.4 | EDGE,FLAT,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:523` |
| 226 | Buildings: Town_Rubble | Buildings | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 53.7 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:1530` |
| 227 | concrete_road_barrier | Buildings | concrete | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 24.0 | ALB | - | FPSKitTownAssets (material remap) / source asset |
| 228 | Buildings: Town_TankBlue | Buildings | painted metal | REAL | FAKE | REAL | **FAKE** | 0.00 | 4.0 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:147` |
| 229 | Buildings: Town_FloorOldMosaic | Buildings | unknown | REAL | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.00 | 17.2 | NRM,NAME | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:116` |
| 230 | wild_rooibos_bush | Landmarks/LandmarkProps | vegetation | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 22.3 | EDGE,DARK,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 231 | Edge: Town_CanopyWhite | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 45.3 | EDGE,FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:24` |
| 232 | Car_Sedan_C | Life | skin | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 44.4 | DARK,ALB,SMOOTH,AO | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO | FPSKitTownAssets (material remap) / source asset |
| 233 | Buildings: Town_Cloth3 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 234 | Buildings: Town_Cloth4 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 235 | Buildings: Town_PlasterBeigeSmooth | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 98.4 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownWalls.cs:59` |
| 236 | Edge: Town_StuccoWhite | Edge | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 107.6 | EDGE,BOX,NRM | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:27` |
| 237 | throw_pillows_01 | Buildings | fabric | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.5 | DARK | - | FPSKitTownAssets (material remap) / source asset |
| 238 | food_apple_01 | Buildings | unknown | REAL | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.7 | DARK,NRM,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 239 | Buildings: Town_Cloth6 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 240 | Edge: Town_PoleWood | Edge | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 37.3 | EDGE,TILE,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:473` |
| 241 | vintage_oil_lamp | Buildings | painted metal | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.4 | EDGE,DARK,NRM,NONRM | PH_vintage_oil_lamp: DARK; PH_vintage_oil_lamp_flame: DARK,NONRM; PH_vintage_oil_lamp_glass: NRM | FPSKitTownAssets (material remap) / source asset |
| 242 | Buildings: Town_Cloth0 | Buildings | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 4.0 | EDGE,FLAT,BOX,ALB | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 243 | rusted_wheel_rim_01 | Buildings | painted metal | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 7.1 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 244 | plastic_crate_03 | Buildings | plastic | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 2.7 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 245 | dead_quiver_trunk | Edge | vegetation | REAL | REAL | REAL | **REAL** | 0.00 | 100.3 | - | - | FPSKitTownAssets (material remap) / source asset |
| 246 | Edge: Town_FuelGreen | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 44.8 | EDGE,FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:68` |
| 247 | Edge: DesertOutpost_Sandbag | Edge | sand | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 118.5 | ALB | - | FPSKitTownEdge |
| 248 | Buildings: Town_Cloth7 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 249 | Buildings: Town_Cloth2 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 250 | Landmarks: Town_Gold | Landmarks | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 35.2 | FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:29` |
| 251 | Buildings: Town_Cloth1 | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 252 | dry_branches_medium_01 | Landmarks/LandmarkProps | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 14.5 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 253 | Buildings: Town_DishWhite | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 2.4 | EDGE,FLAT,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:148` |
| 254 | old_tyre | Buildings | rubber | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 32.9 | DARK,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 255 | Landmarks: Town_GlassDark | Landmarks | glass | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 184.1 | BOX | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:121` |
| 256 | Edge: Town_DesertRock | Edge | stone | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 87.3 | EDGE,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:521` |
| 257 | Buildings: Town_SolarPanel | Buildings | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 2.2 | EDGE,FLAT,BOX,DARK,ALB,SMOOTH,METAL,AO | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:149` |
| 258 | Buildings: Town_Cloth5 | Buildings | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 1.8 | EDGE,FLAT,BOX,ALB | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 259 | Edge: Town_Wire | Edge | unknown | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 40.6 | EDGE,FLAT,DARK,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:474` |
| 260 | carved_wooden_plate | Buildings | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 1.8 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 261 | Landmarks: Town_MosqueFloor | Landmarks | unknown | NEEDS WORK | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.00 | 182.5 | EDGE,TILE,NRM,NAME | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:32` |
| 262 | security_light | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 121.2 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 263 | Landmarks: Town_SteelDark | Landmarks | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 25.9 | FLAT,BOX,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:131` |
| 264 | Palm_DualBended | Edge | vegetation | REAL | REAL | REAL | **REAL** | 0.00 | 159.6 | - | - | FPSKitTownAssets (material remap) / source asset |
| 265 | Buildings: Town_PalmBeam_Plain | Buildings | vegetation | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 15.7 | EDGE,TILE,DARK,ALB | - | FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront |
| 266 | Edge: Town_RoofConcrete | Edge | concrete | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 107.6 | EDGE,BOX | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:111` |
| 267 | Edge: Town_GlassDark | Edge | glass | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 45.2 | BOX | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:121` |
| 268 | Edge: Town_SteelDark | Edge | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 61.1 | EDGE,FLAT,BOX,ALB,METAL | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:131` |
| 269 | Buildings: Town_FloorTerracotta | Buildings | unknown | REAL | NEEDS WORK | UNCERTAIN | **NEEDS WORK** | 0.00 | 16.6 | NRM,NAME | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:115` |
| 270 | food_lime_01 | Buildings | unknown | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 2.9 | DARK,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 271 | Edge: Town_FlagCloth | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 121.7 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:709` |
| 272 | Edge: Town_GlassClear | Edge | glass | REAL | REAL | REAL | **REAL** | 0.00 | 74.0 | - | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:127` |
| 273 | Roads: Town_RoadPaintYellow | Roads | plaster | REAL | FAKE | REAL | **FAKE** | 0.00 | 50.4 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:41` |
| 274 | Landmarks: Town_RoofConcrete | Landmarks | concrete | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 222.1 | EDGE,BOX,TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:111` |
| 275 | Edge: Town_Cloth1 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 220.2 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 276 | Edge: Town_Cloth6 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 219.4 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 277 | Edge: Town_Cloth5 | Edge | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 218.4 | EDGE,FLAT,BOX,ALB | - | FPSKitTownEdge |
| 278 | dead_tree_trunk_02 | Edge | vegetation | REAL | REAL | REAL | **REAL** | 0.00 | 139.7 | - | - | FPSKitTownAssets (material remap) / source asset |
| 279 | Edge: Town_StripeRed | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 107.6 | EDGE,FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:25` |
| 280 | Edge: Town_CorrugatedRust | Edge | painted metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 334.4 | EDGE,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:22` |
| 281 | Edge: Town_Cloth7 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 219.1 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 282 | Edge: Town_Kerb | Edge | concrete | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 60.8 | EDGE,BOX,TILE | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:39` |
| 283 | Edge: Town_StripeWhite | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 61.2 | EDGE,FLAT,BOX,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:26` |
| 284 | Landmarks: Town_Carpets | Landmarks | fabric | REAL | REAL | REAL | **REAL** | 0.00 | 233.4 | - | - | FPSKitTownLandmarks |
| 285 | Landmarks: Town_DoorWood | Landmarks | wood | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 272.6 | EDGE,BOX,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:137` |
| 286 | Edge: Town_Cloth3 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 221.7 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 287 | Edge: Town_Cloth2 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 219.8 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 288 | Edge: Town_Cloth4 | Edge | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 218.1 | EDGE,FLAT,BOX | - | FPSKitTownEdge |
| 289 | Edge: Town_Cloth0 | Edge | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 221.3 | EDGE,FLAT,BOX,ALB | - | FPSKitTownEdge |
| 290 | Edge: Town_FenceSteel | Edge | painted metal | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 82.4 | EDGE,FLAT,BOX,METAL | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:28` |
| 291 | Apron: Town_ApronSandPale | Apron | sand | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 1557.3 | TILE | - | `Assets/Scripts/Editor/FPSKitTownGround.cs:593` |
| 292 | Apron: Town_ApronAsphalt | Apron | asphalt | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 1545.6 | TILE | - | `Assets/Scripts/Editor/FPSKitTownGround.cs:594` |
| 293 | cement_bag | Buildings | concrete | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 61.2 | SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 294 | food_pomegranate_01 | Buildings | unknown | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 80.8 | EDGE,DARK,NRM,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 295 | wicker_basket_02 | Buildings | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 18.5 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 296 | Palm_DualStraight | Buildings | vegetation | REAL | REAL | REAL | **REAL** | 0.00 | 209.7 | - | - | FPSKitTownAssets (material remap) / source asset |
| 297 | propane_tank | Buildings | painted metal | REAL | REAL | REAL | **REAL** | 0.00 | 191.3 | - | - | FPSKitTownAssets (material remap) / source asset |
| 298 | wooden_ladder | Buildings | wood | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 215.7 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 299 | wooden_barrels_01 | Buildings | wood | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 188.3 | EDGE,ALB,NRM | PH_wooden_barrels_01_barrel01: ALB,NRM; PH_wooden_barrels_01_barrel02: ALB,NRM; PH_wooden_barrels_01_pieces: NRM | FPSKitTownAssets (material remap) / source asset |
| 300 | Buildings: Town_RoofConcrete | Buildings | concrete | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 2.0 | TILE | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:111` |
| 301 | Buildings: Town_PlasterMint | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 31.6 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:97` |
| 302 | Buildings: Town_PaintGrey | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 66.2 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:100` |
| 303 | Buildings: Town_CabinetGreen | Buildings | wood | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 39.4 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:118` |
| 304 | Buildings: Town_PaintYellow | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 66.1 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:96` |
| 305 | Buildings: Town_PaintCream | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 80.2 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:95` |
| 306 | Buildings: Town_PaintGreen | Buildings | plaster | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 82.0 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:97` |
| 307 | Buildings: Town_PaintPeach | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 71.0 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownInterior.cs:99` |
| 308 | Buildings: Town_PlasterSky | Buildings | plaster | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 90.9 | EDGE,TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:98` |
| 309 | Buildings: Town_PlazaStone | Buildings | stone | REAL | REAL | REAL | **REAL** | 0.00 | 85.2 | - | - | `Assets/Scripts/Editor/FPSKitTownRoads.cs:38` |
| 310 | Buildings: Town_CurtainLinen | Buildings | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 66.3 | EDGE,FLAT | - | `Assets/Scripts/Editor/FPSKitTownFurniture.cs:37` |
| 311 | ammo_box | Landmarks/LandmarkProps | unknown | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 119.9 | EDGE,DARK,NRM,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 312 | old_military_crate | Landmarks/LandmarkProps | plastic | REAL | REAL | REAL | **REAL** | 0.00 | 385.6 | - | - | FPSKitTownAssets (material remap) / source asset |
| 313 | portable_searchlight | Landmarks/LandmarkProps | unknown | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 412.8 | DARK,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 314 | Landmarks: DesertOutpost_TownWater | Landmarks | water | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 204.7 | NRM | - | FPSKitTownLandmarks |
| 315 | Landmarks: Town_CastleStone | Landmarks | stone | NEEDS WORK | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 308.1 | EDGE,TILE | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:30` |
| 316 | Landmarks: Town_FlagCloth | Landmarks | fabric | NEEDS WORK | FAKE | REAL | **FAKE** | 0.00 | 400.6 | EDGE,FLAT,BOX | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:709` |
| 317 | Landmarks: Town_GardenPath | Landmarks | concrete | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 246.3 | TILE,NRM | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:762` |
| 318 | small_lpg_tank | Edge | painted metal | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 82.8 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 319 | hand_truck | Edge | skin | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 240.3 | EDGE | - | FPSKitTownAssets (material remap) / source asset |
| 320 | namaqualand_boulder_05 | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 104.9 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 321 | namaqualand_boulder_02 | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 240.3 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 322 | namaqualand_boulder_03 | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 134.6 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 323 | namaqualand_boulder_04 | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 210.8 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 324 | concrete_road_barrier_02 | Edge | concrete | REAL | REAL | REAL | **REAL** | 0.00 | 375.8 | - | - | FPSKitTownAssets (material remap) / source asset |
| 325 | Edge: Town_TownSigns | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 45.6 | NAME | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:154` |
| 326 | Edge: Town_Corrugated | Edge | painted metal | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 380.9 | EDGE,BOX,METAL | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:21` |
| 327 | Edge: Town_DoorMetal | Edge | painted metal | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 384.1 | SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:138` |
| 328 | Edge: Town_TowerWhite | Edge | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 289.5 | EDGE,FLAT,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:286` |
| 329 | Edge: Town_TowerBlue | Edge | unknown | REAL | FAKE | UNCERTAIN | **FAKE** | 0.00 | 289.6 | FLAT,NAME | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:287` |
| 330 | Palm_Trio | Edge | vegetation | REAL | REAL | REAL | **REAL** | 0.00 | 208.9 | - | - | FPSKitTownAssets (material remap) / source asset |
| 331 | Edge: Town_FarmWall | Edge | plaster | NEEDS WORK | NEEDS WORK | NEEDS WORK | **NEEDS WORK** | 0.00 | 210.0 | EDGE,BOX,TILE,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:328` |
| 332 | Edge: DesertOutpost_TownWater | Edge | water | REAL | NEEDS WORK | REAL | **NEEDS WORK** | 0.00 | 209.6 | TILE,NRM | - | FPSKitTownEdge |
| 333 | Edge: Town_Limestone | Edge | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 338.6 | EDGE,BOX | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:104` |
| 334 | dead_quiver_branch_01 | Edge | unknown | NEEDS WORK | REAL | UNCERTAIN | **NEEDS WORK** | 0.00 | 360.4 | EDGE,NAME | - | FPSKitTownAssets (material remap) / source asset |
| 335 | namaqualand_boulder_06 | Edge | unknown | REAL | REAL | UNCERTAIN | **UNCERTAIN** | 0.00 | 221.9 | NAME | - | FPSKitTownAssets (material remap) / source asset |
| 336 | Edge: Town_PalmBeam | Edge | vegetation | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 234.4 | DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:144` |
| 337 | Edge: Town_TentCloth | Edge | fabric | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 234.4 | EDGE,FLAT,ALB | - | `Assets/Scripts/Editor/FPSKitTownEdge.cs:27` |
| 338 | Edge: Town_Carpets | Edge | fabric | REAL | REAL | REAL | **REAL** | 0.00 | 234.4 | - | - | FPSKitTownEdge |
| 339 | painted_wooden_bench | Street | plaster | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 174.1 | EDGE,ALB | - | FPSKitTownAssets (material remap) / source asset |
| 340 | Street: Town_StoneWarm | Street | stone | NEEDS WORK | REAL | REAL | **NEEDS WORK** | 0.00 | 159.2 | EDGE | - | `Assets/Scripts/Editor/FPSKitTownLandmarks.cs:26` |
| 341 | Street: Town_ShopInterior | Street | plaster | REAL | FAKE | REAL | **FAKE** | 0.00 | 159.2 | FLAT | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:152` |
| 342 | Street: Town_PalmBeam | Street | vegetation | NEEDS WORK | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 159.2 | EDGE,BOX,DARK,ALB | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:144` |
| 343 | Parked_Car_Sport | Life | plaster | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 276.5 | FLAT,DARK,ALB,SMOOTH,AO,NAME,SAT | Blacks1: FLAT,DARK,NAME; frontlight1: FLAT,NAME; Body1: FLAT,SMOOTH,AO,SAT; rearlight1: FLAT,NAME; Back_Light: FLAT,NAME; bottom_interior: FLAT,DARK,ALB; mirror1: FLAT,NAME | FPSKitTownAssets (material remap) / source asset |
| 344 | Parked_Car_Touring | Life | unknown | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 239.3 | FLAT,DARK,BRIGHT,ALB,SMOOTH,NRM,NAME | M_Caje: FLAT,NAME; M_Antenas: FLAT,NAME; M_Opticas_Tras: DARK,NAME; M_Black: FLAT,DARK,NAME; M_Engine: DARK,NAME; M_Wood: DARK,ALB,SMOOTH; M_Interior: DARK,ALB; M_Seat: DARK,NAME; M_Carbody666: DARK,NAME; M_Dash: DARK,NAME; M_Wheel: BRIGHT,ALB,SMOOTH; M_caliper: DARK,NAME; M_sidewall: DARK,NAME; M_Rim2: DARK,NAME; M_tread: DARK,NRM,NAME; M_Rim: DARK,NAME; M_brackedisk: DARK,NAME | FPSKitTownAssets (material remap) / source asset |
| 345 | Car_Sedan_D | Life | skin | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 238.7 | DARK,ALB,SMOOTH,AO | RMCar26_Light: DARK,ALB,AO; RMCar26_Body: DARK,ALB,AO; RMCar26_Paint: ALB,SMOOTH,AO | FPSKitTownAssets (material remap) / source asset |
| 346 | Life: Town_TechnicalTrim | Life | stone | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 558.6 | FLAT,BOX,ALB | - | `Assets/Scripts/Editor/FPSKitTownLife.cs:460` |
| 347 | Life: Town_TechnicalPaint | Life | plaster | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 558.6 | EDGE,FLAT,SMOOTH | - | `Assets/Scripts/Editor/FPSKitTownLife.cs:459` |
| 348 | Life: Town_GlassDark | Life | glass | REAL | REAL | REAL | **REAL** | 0.00 | 558.6 | - | - | `Assets/Scripts/Editor/FPSKitTownBuildings.cs:121` |
| 349 | Life: Town_Headlamp | Life | skin | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 558.6 | EDGE,FLAT,BOX,ALB,SMOOTH,AO | - | `Assets/Scripts/Editor/FPSKitTownLife.cs:463` |
| 350 | Cylinder (Town_Tyre) | Life | rubber | NEEDS WORK | FAKE | NEEDS WORK | **FAKE** | 0.00 | 559.3 | FLAT,DARK,ALB,PRIM | - | FPSKitTownLife |
| 351 | Cylinder (Town_Rim) | Life | unknown | NEEDS WORK | FAKE | UNCERTAIN | **FAKE** | 0.00 | 559.5 | FLAT,PRIM,NAME | - | FPSKitTownLife |
| 352 | Gun_M2 | Life | gun finish | REAL | FAKE | NEEDS WORK | **FAKE** | 0.00 | 561.2 | PALETTE,DARK,SMOOTH | - | FPSKitTownAssets (material remap) / source asset |
| 353 | Man_Mesh/Man_Eyes_Mesh | Life | skin | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 563.1 | BRIGHT | - | FPSKitTownAssets (material remap) / source asset |
| 354 | Man_Mesh/Man_Half_ Body_Mesh | Life | skin | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 562.9 | BRIGHT | - | FPSKitTownAssets (material remap) / source asset |
| 355 | Man_Mesh/Man_Pants_Mesh | Life | fabric | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 563.0 | DARK | - | FPSKitTownAssets (material remap) / source asset |
| 356 | Man_Mesh/Man_Shoes_Mesh | Life | fabric | REAL | REAL | NEEDS WORK | **NEEDS WORK** | 0.00 | 562.9 | DARK | - | FPSKitTownAssets (material remap) / source asset |
