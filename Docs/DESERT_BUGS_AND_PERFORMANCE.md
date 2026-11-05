# Desert Outpost: every bug, the files that bloat it, and how to run it on a laptop with no graphics card

2026-11-04. Part 1 is the master bug list: every bug found so far, by area, each one line with where its fix is
written. Part 2 lists the files that make the project and the game heavy. Part 3 is the plan for running the desert well
on a 16 GB laptop with integrated graphics (the Intel Arc in the developer's laptop) without losing the look.
Measured numbers come from `Docs/DESIGN_NOTES.md` ("Part A on mains"); sizes from Git LFS. Nothing here was run in Unity.

**Status key:** **Fixed** = fixed in code on branch `claude/gifted-lamport-ho6dv7` (not yet compiled or played).
**Plan** = written up with cause and fix, not yet coded. **New** = found in this round.

---

## Part 1: every bug

### Combat
| ID | Bug | Status | Where |
|---|---|---|---|
| D1 | A neck shot does nothing to an enemy at or below 15 % health | Fixed, tested | `DESERT_REVIEW.md` D1 |
| D2 | Automatic guns fire up to 25 % slower at 30 fps | Fixed, tested (player, truck; enemies' 1 s cooldowns left) | D2 |
| D3 | Blasts hurt people through walls | Fixed, tested (15 % through a wall) | D3 |
| D4 | Damage reported larger than what landed | Not a bug: `Health.ApplyDamage` reports a capped limb hit in full on purpose | D4 |
| D5 | Score can go negative | Fixed | D5 |
| D6 | Quitting twice in a frame banks coins twice | Fixed | D6 |
| D7 | Recoil recovery depends on frame rate | Fixed | D7 |
| D8 | No grenades before the Warehouse boss; no explosive props | Fixed | `DESERT_EXPLOSIVES.md` |
| C1 | Civilians killed by enemies cost the player stars | Fixed, tested | `DESERT_REVIEW.md` C1 |

### Lighting
| ID | Bug | Status | Where |
|---|---|---|---|
| A1 | A bright band on people, doors and props in one-storey rooms | Plan | `DESERT_LIGHTING.md` part 1 |
| A2 | Medium had no indoor reflections (rooms mirrored the sky) | Fixed (measure the cost) | |
| A3 | One painted cubemap for every room's reflection | Plan | |
| A4 | One reflection box over a whole house and its yard | Fixed | |
| A5 | Shop light chosen by a " dark" name suffix | Plan | |
| A6 | Arms and gun lit by turning the sun for their camera | Plan | |
| A7 | The eye dims every probe-lit thing in view, outdoors too | Plan | |
| A8 | Door frames, skirting and small props glow in dark rooms | Plan | |
| A9 | Windows are black holes (opaque, nothing behind) | Plan | `DESERT_GLASS.md` |
| A10 | Low, phones, web: outdoor probes inside rooms, no eye adaptation | Plan | |
| A11 | Probe sky occlusion off | Plan (check) | |
| A12 | Medium shared Low's renderer | Fixed | |

### Vehicles
| ID | Bug | Status | Where |
|---|---|---|---|
| V1 | Nobody drives the moving cars | Fixed (traffic); the technical: Plan | `DESERT_VEHICLES.md` |
| V2 | Every moving car is the same sedan | Plan | |
| V3 | The technical is built from boxes and cylinders | Plan | |
| V4 | Cars glide with no weight; float or sink | Plan | |
| V5 | Cars never hit anyone | Fixed | |
| V6 | A round could never reach a driver | Fixed | |
| V7 | No brake lights, indicators or headlights; lamp material black | Plan | |
| V8 | No engine sound by speed, no horn | Plan | |
| V9 | No damage | Plan | |
| V10 | Parked intact sedans wear the burnt-wreck material | Plan | |
| V11 / B1 | The drivable car's camera is pulled to the world origin | Plan (code given) | `DESERT_REVIEW.md` B1 |
| B2-B8 | Chase camera through walls, snapping steering, no engine brake, flat torque, wheel jitter, renderers on exit, exit into walls | Plan | B2-B8 |
| B9 | The drivable car stops dead on an enemy | Fixed, tested at town speed; under 2.5 m/s it still stops at him | B9 |
| B10 | In the car the player cannot be hit or seen | Fixed for rounds, tested (glass passes, body stops); blasts and melee still do not reach him | B10 |
| B11-B12 | Interact conflicts; per-frame allocations | Plan | B11-B12 |
| B13 | The technical drives through everything | Stops for people and vehicles (not play-tested); ground-following still Plan | B13 |
| V12 | No dust behind or on cars | Plan | |

### People
| ID | Bug | Status | Where |
|---|---|---|---|
| C4 | "No people": 36 on Medium, none within 30 m, removed at the first shot | Households Fixed; walkers Plan | `DESERT_PEOPLE.md` |
| P-1 | Only four models, all men | Plan | |
| P-2 | No everyday animations (the clips are combat only) | Plan (download script written) | |
| C2 | People and cars created and destroyed in play (hitches) | Plan | `DESERT_REVIEW.md` C2 |
| C3 | Bodies over 20 vanish in view | Fixed | C3 |

### Missions and bosses
| ID | Bug | Status | Where |
|---|---|---|---|
| M1 | Nothing said "go there, kill him"; objectives on random points | Fixed (named bosses at places; target levels) | `DESERT_STATUS.md` 2 |
| M2 | Bosses unnamed and alert from the start | Fixed | |
| M3 | Bosses are giants (1.9-2.2x) in soldiers' camouflage | Size and security Fixed; the mafia look Plan | `DESERT_VEHICLES.md` 5 |

### The town
| ID | Bug | Status | Where |
|---|---|---|---|
| E6 | No way up to most roofs (only enterable villas get a stair) | Plan | `DESERT_TOWN_FIXES.md` 1 |
| E7 | Shops too open and empty | Plan | 3 |
| E1 | 98 building pieces in a silent white fallback material | Plan | `DESERT_REVIEW.md` E1 |
| E3 | Kicked-in houses: whether agents use them is unmeasured | Plan (check) | E3 |
| E4 | "Sky seen in a room": still open | Plan (check) | E4 |
| E5 | The old houses' rooms are very dark | Plan | E5 |
| N1 | No house or shop numbers, no street names | Plan | `DESERT_TOWN_FIXES.md` 7 |
| N2 | The minimap is pale and has no roads or names; there is no full map | Plan | 7 |
| U1-U24 | 24 things that look generated (no bevels, straight grid, tiling, flat colours, box furniture, clean streets, placeholders, no animals, no town sound, ...) | Plan | 8 |

### The player's body
| ID | Bug | Status | Where |
|---|---|---|---|
| H1 | Punching or kicking a wall did nothing to the player | Fixed (pain, injury that builds) | `DESERT_BODY.md` |
| H2 | Fist, stock and kicking leg went into walls | Fixed (strikes stop at the wall) | |
| H3 | Legs went into walls when standing at one | Fixed | |
| H4 | The rifle's muzzle went into walls | Fixed (drawn back, muzzle lifted) | |

### Checks and code health
| ID | Bug | Status | Where |
|---|---|---|---|
| E2 | `VerifyStatics` fails on `WalkRoute.Desert`, `DrawCounters.Names` | Plan | `DESERT_REVIEW.md` E2 |
| F2 | Traffic noise listener allocated a delegate every frame | Fixed | |

### New this round: performance and size
| ID | Bug | Where it is | Status |
|---|---|---|---|
| **P1** | **Roslyn compiler DLLs ship in every build.** `Assets/Plugins/Roslyn` (12.8 MB: `Microsoft.CodeAnalysis.CSharp.dll`, `Microsoft.CodeAnalysis.dll`, three System DLLs). Their `.meta` files carry no platform settings, which Unity treats as "all platforms"; no code, tool or document refers to them. | `Assets/Plugins/Roslyn` | New: remove, or set Editor only |
| **P2** | **Mipmap streaming is off on every tier** (`QualitySettings`: `streamingMipmapsActive: 0`). Every texture's full chain stays in memory. On integrated graphics that is system RAM. | `ProjectSettings/QualitySettings.asset` | New |
| **P3** | **604 Poly Haven textures import at 2048 px, none crunched.** Small props (cups, plates, bottles) get the same 2K maps as walls. | `Assets/ThirdParty/PolyHaven` import settings | New |
| **P4** | **Dynamic resolution only on phones** (`QualityTiers`: `if (Handheld)`). The laptop drops frames in a heavy moment instead of dropping a little sharpness. | `Runtime/QualityTiers.cs:225` | New |
| **P5** | **No frame cap on the laptop tiers** (vSync off, no target frame rate except in the reduced scenes). The laptop runs flat out, reaches its 100-103 C limit within 20 s (`DESIGN_NOTES.md`), throttles, and stutters. | Quality tiers | New |
| **P6** | **The scenes are enormous**: Snowbound 366 MB, Desert 188 MB, Mars 165 MB, the six desert room scenes 91 MB. The town's merged meshes are stored inside the scene. All seven arenas are in Build Settings. | `Assets/FPSKit_Generated/Scenes` | New |
| **P7** | **All six room districts load at once** (`FPSKitTownRoomScenes`: "the town loads its district rooms wherever it is opened"). Every house interior in the town is in memory, wherever the player is. | `Editor/FPSKitTownRoomScenes.cs` | New |
| **P8** | **GPU occlusion culling is off** in the cameras (`m_GPUResidentDrawerEnableOcclusionCullingInCameras: 0`), though the GPU Resident Drawer is on. | `Medium_RPAsset`, `PC_RPAsset` | New |
| **P9** | **Probe volume data for the desert is 151 MB** (`CellSupportData` 75 MB, `CellData` 38 MB, `CellOptionalData` 38 MB), loaded whole. | `DesertOutpost_APV*` | New: check which parts the player needs; turn on probe disk streaming |
| **P10** | **Old versions in Git LFS**: the desert scene was committed 17 times (about 190 MB each), its probe support data 11 times (75 MB). About 4 GB of history on GitHub and on the laptop. | `.git/lfs` | New: `git lfs prune` locally |

### New this round: bugs in this session's own code
Honest list of what the code written in this session does not do well yet.

| ID | Bug | File |
|---|---|---|
| O1 | A dead driver "slumps" by freezing the sitting pose and tilting the body: no real dead-at-the-wheel pose | `VehicleDriver` |
| O2 | Fixed, tested: A car whose driver is dead still follows its route round corners while it rolls to a stop, and is removed at its route's end even in view | `TrafficCar` |
| O3 | Sides Fixed; the drivable car still stops at an enemy under 2.5 m/s | `VehicleStrike` |
| O4 | **New load:** a skinned driver in each of up to 15 traffic cars, plus up to 54 residents and shopkeepers on High; each body is 40-120k triangles with no lower LOD for people | `VehicleDriver`, `TownHouseholds` |
| O5 | Residents of a house with a bolted front door never go out (errands need an open path) | `CivilianLife` |
| O6 | Until the everyday clips are downloaded, every resident and driver stands or sits in the idle pose: they look like statues | `FPSKitTownHouseholds` |
| O7 | Mixamo's "Praying" is not the Islamic prayer: it needs a captured clip or must be left out | `Tools/mixamo-download-life.js` |
| O8 | Fixed (not play-tested): A stationed target, his guards or a boss's detail can appear in view if the player is already at the place when the level starts (`Station` does not test visibility) | `LevelManager.Station` |
| O9 | Fixed (24f08f2): Residents are kept from appearing in view by a frustum test that ignores walls: a house looked at through a wall from under 30 m gets nobody until the player turns away | `TownHouseholds` |
| O10 | The grenade pickup is a plain capsule; a burst leaves no scorch and no wreck | `FPSKitSceneBuilder`, `ExplosiveProp` |
| O11 | The petrol drums' red is a material copy per source material (a few small assets) | `FPSKitTownExplosives` |
| O12 | Medium's new Forward+ renderer: its frame cost is unmeasured | `FPSKitQualityTiers` |
| O13 | Cloud shadows are off by default: the sky is clear, and they would contradict it | `CloudShadows` |

---

## Part 1b: playing in the world (round 5, 2026-11-05)

His example: "dustbins are not clickable". Analysed what the player can touch, push, search, break, shoot through, climb
and hear. Each row: the bug, the cause in the code, the solution.

### Touching the world
| ID | Bug | Cause | Solution |
|---|---|---|---|
| W1 | **Props are frozen.** Bins, crates, chairs, buckets, tyres, cones, bottles, pots do not move when kicked, shot or blown up | None of the town's 114 prop prefabs has a `Rigidbody`, and only `Door` answers a blow (`IWorldHit`); `WorldHit` and `Explosion.Blast` push only loose rigidbodies | A `LooseProp` pass in `FPSKitTownAssets`: every prop under about 60 kg gets a sleeping `Rigidbody` with a mass by kind (a plastic chair 3 kg, a bin 8, a full crate 25, a drum 30), interpolation, a physics material by surface. At most about 60 awake at once (the rest wake when hit). Moved props stay off the navmesh bake (they already are) |
| W2 | **Nothing can be searched or picked up**: bins, crates, cabinets, cars, shops hold nothing; no ammo box or med kit lies anywhere in the town (supplies only drop from enemies) | No searchable object, no world-placed pickup | A `Searchable` interactable: E (pad D-pad up, a touch button) with a short search (1-1.5 s; tap starts it and it finishes on its own, for his touchpad rule), loot by kind (bins little, crates ammo, bathroom cabinets and the pharmacy med kits, cars a pistol in a glovebox now and then), once per object. Ammo crates at enemy posts and the boss's place |
| W3 | **The interact key reaches only cars and guns.** No door can be opened or shut with it (they only break after 3 kicks), no knocking, no counters, switches or taps | `Door` has no interact path; the interact press is read separately by `DrivableCar` and `GunPickup` | One `Interactable` component with a priority, a prompt (`InputPrompts`) and a reach, and one router that gives each press to one owner (also fixes review B11). Doors: unbolted ones open and shut with E, bolted ones still break to kicks, a knock with a hold |
| W4 | Kicking a light bin will hurt like kicking a wall once props move | `PlayerLimbs` hardness is by surface only (a metal bin is metal) | In `MeleeStrike.HitWorld`: when what was struck moves (a loose rigidbody under 80 kg), a third of the strain; the bin flies instead |
| W5 | **Nothing breaks** but doors and fuel: street lamps, windows, signs, plastic chairs, wooden crates, aircon units, water tanks, dishes | No breakable component | A `Breakable` component: health by kind, on break a swap to a broken mesh or pooled debris, a sound, lamps go dark (emissive off), a water tank leaks. Glass: `DESERT_GLASS.md` |
| W6 | **Bullets never go through anything**: not a wooden door, a plaster partition, a car door, a cardboard box, a plastic chair | `Weapon.FireRay` and `EnemyAI` stop at the first collider | Penetration by material: each round carries power, each surface a resistance per cm (cardboard, plastic, wood, plaster, sheet metal, glass), a loop of casts that loses damage through each, at most 2-3 surfaces; never through concrete, stone or brick walls |
| W7 | **Wrong collision shapes.** Small props are solid boxes of their full bounds (bullets stop in the air between a chair's or table's legs, you cannot shoot under a table, invisible corners on carts and bicycles); big dense props are convex hulls; palms carry their pack's full mesh collider (the fronds overhead probably stop bullets: check) | `FPSKitTownAssets` collider rules (box by bounds under 2.2 m, convex hull over 3,000 triangles) | Compound colliders by kind (a table's top and four legs, a chair's seat and back, a cart's bed and wheels), capsules on trunks, no collider on fronds and leaves; a coarse mesh collider on a "bullet only" layer for detailed props, simple shapes for movement |
| W8 | **Cars drive off the edge of the map** | The edge (`PlayerBounds`) collided only with the player's layer; driving, his controller is off | **Fixed in code**: the edge now stops the `Vehicle` layer too (`FPSKitSceneBuilder.EnsureProjectTagsAndLayers`) |
| W9 | An invisible wall at the town's edge, with open desert visible beyond | `BuildTownBounds`: four boxes on a sand ridge | A real edge: a fence with a gate and a guard post on the roads out, a dry river bed, berms, wrecks; a "leaving the area" warning that turns the player round before the wall |

### Moving
| ID | Bug | Cause | Solution |
|---|---|---|---|
| MV1 | **No vaulting, mantling or climbing**: a 1 m compound wall, a crate, a car's bonnet are obstacles that need a lucky jump; nothing climbable | Not in `PlayerMotor` (no such action) | Vault (an obstacle 0.4-1.3 m high with room beyond, on Jump while moving at it: the camera rolls over in 0.4 s), mantle up to 1.9 m ledges (onto a container, a low roof), ladders (with the roof access of `DESERT_TOWN_FIXES.md` 1) |
| MV2 | **No leaning, no prone** | No actions for them | Lean (keys from `ControlSettings`, never typed into `.inputactions`; hold or tap-to-latch for his touchpad), prone (a hold of crouch); enemies' sight already scores a crouched player smaller |
| MV3 | Swimming: the oasis is wading only | `WadingZone` only | Fine for a desert; keep deep water out of reach |

### Sound
| ID | Bug | Cause | Solution |
|---|---|---|---|
| S1 | **The town is silent between shots** | The Desert theme's `ambienceLoop` is empty | Ambience by district (the souk's voices, traffic, wind, dogs far off, the call to prayer at its times from the mosque), point sounds on things (generators, aircon units, a radio in a shop, a TV through a wall), reverb zones indoors |
| S2 | **Footsteps sound the same on sand, asphalt, tiles, wood and metal** | Four clips in `PlayerMotor.footstepClips`, no surface check | A ray down at each step to `FPSImpactManager.SurfaceOf`, a clip set per surface (sand, gravel, asphalt, tile, wood, metal, carpet, water); the same for enemies' steps (they are how the player hears them coming) |
| S3 | All sounds are synthetic placeholders; six guns share three firing sounds; no far shots or indoor tails | `Tools/generate-placeholder-audio.py` | Real recordings with a commercial licence (CC0 libraries, the Sonniss GDC bundles), logged in `LICENSES.md`: per gun a close shot, a distant shot and an indoor tail, chosen by distance and by whether the shooter is under a roof |
| S4 | Nobody speaks: civilians are silent, enemies have pain and alert sounds only | No barks | Short barks in Arabic (warnings, callouts, a shopkeeper calling out), from voice actors or a licensed pack |

### Fighting
| ID | Bug | Cause | Solution |
|---|---|---|---|
| G1 | Hitscan bullets over the town's 300 m sightlines: no travel time, no drop | `Weapon.FireRay` is one instant ray | Past about 80 m, a projectile with drop and time of flight (the DMR and the LMG feel it); hitscan close in |
| G2 | **Every fight is at street level**: no enemy on a roof, at a window or inside a building | Spawns are open ground only ("never a roof"); roofs cannot be reached | After roof access: posts on roofs and at windows for the stationed targets and boss guards, a building to clear room by room on target levels |
| G3 | Enemies only ever arrive on foot | No vehicle drop-offs | A truck that brings a squad and drops them at the edge of the fight (with the technical's driving code) |

### The world
| ID | Bug | Cause | Solution |
|---|---|---|---|
| L1 | **Always the same light and weather**: a clear late-morning sky, no wind (palms and cloth never move), no dust | One sky, no `WindZone` | A `WindZone` and foliage/cloth wind in the shaders, dust devils, blowing sand at ground level, a dusk and a sandstorm variant of some levels (`DESERT_LIGHTING.md` part 2) |
| L2 | No animals | | Dogs and cats in the old town, pigeons on roofs, goats at the farms (U23) |

---

## Part 2: files that make it heavy

**First, how size works in Unity.** A file nobody uses does **not** go into the built game: Unity builds only the scenes
in Build Settings, what they reference, and everything in a `Resources` folder. Unused files make the **project** heavy
(the clone, Git LFS, the editor's import time and memory), not the game. What makes the **game** heavy is what is used:
scenes, lightmaps, probe data, textures, meshes, audio.

### 2a. Unused or never-needed (safe to remove from the project after a check in Unity)
Found by tracing every asset's ID through every text asset (1,577 prefabs, materials, controllers and assets) and its
name through the code. Scenes are binary and could not be searched, so **check each in Unity before deleting**
(right-click > Find References in Scene, or `AssetDatabase.GetDependencies` over the scenes in Build Settings).

| What | Size | Why it looks unused | Action |
|---|---|---|---|
| `Assets/Plugins/Roslyn/` (5 DLLs) | 12.8 MB, **in every build** | No code, tool or doc refers to it | Remove (P1) |
| `Assets/Fairground/ThirdParty/Fence001/` (7 textures) | 14.2 MB | No material uses it | Remove if the Fairground check agrees |
| `Assets/Animations/Mixamo/Idle/Rifle_Idle_TwoHand.fbx` | 0.7 MB | No controller uses it | Check |
| `Assets/Animations/Mixamo/Hits/*` | 2.6 MB | No controller uses them; the mocap tool maps names to them | **Keep** (used by `FPSKitMocap`) |
| The sky HDRI (`kloofendal_43d_clear_puresky_4k.hdr`, 18 MB) | | Loaded by code by folder | **Keep** (in use) |
| Civilian prefabs and other `Town/` outputs | | Referenced only by the binary scene | **Keep** |

### 2b. Heavy and used: where the game's size really is

| What | Size | What to do |
|---|---|---|
| Scenes in Build Settings: Snowbound 366 MB, Desert 188 MB, Mars 165 MB, room districts 91 MB, the rest | ~850 MB of scene data | A **desert-only build profile** (Unity 6 Build Profiles) for playing the desert: drops the other arenas' scenes entirely. Meshes saved as assets with mesh compression instead of inside the scene |
| Desert probe volumes | 151 MB | Probe disk streaming; check whether `CellSupportData` (75 MB, used for debugging and re-baking) is needed in the player |
| Room lightmaps (EXR, ~12 MB each, 12 files) | ~110 MB on disk | Compressed at import (check: high-quality compression, not "None"); smaller texel size for "seen-only" rooms |
| Desert occlusion data | 22 MB | Keep: it saves frame time |
| Poly Haven models and textures | 917 MB on disk | Import at 1K for small props, 2K for large surfaces; crunch the albedo maps; BC5 normals (P3) |
| Fairground third-party textures | 124 MB | The same import rules |
| `Build/` on the laptop (screenshots, Realism evidence, Pilot views, player shots, perf builds) | Not in git; often several GB | Delete old runs: `Build/Realism`, `Build/Pilot`, `Build/PlayerShots`, `Build/Views`, `Build/Perf`, old player builds |
| `Library/` on the laptop | Usually 10-30 GB | Regenerable; delete only with Unity closed and if a rebuild is acceptable |
| `~/MocapVideos` | Outside the project | Keep the approved takes only |
| Old LFS versions (P10) | ~4 GB | `git lfs prune` (keeps the current versions and recent ones) |

Commands for the laptop (read-only first: they only report):
```bash
du -sh Build/* Library Logs Temp 2>/dev/null | sort -rh | head -20
```
```bash
git lfs prune --dry-run --verbose
```

---

## Part 3: running it well on 16 GB with integrated graphics, without losing the look

**Where it stands** (release build, 1080p, on mains): Medium 47 fps on the walk with a 1 % low of 20 (stutter), High 19.8
fps. Medium is GPU-bound at ~20 ms a frame: sun shadows 6.5-7.5 ms, SMAA 1.7-3.0, the arms camera 0.8-2.8, transparent
2.1, post 0.4. The laptop hits its thermal limit in 20 s.

**Honest target:** integrated graphics cannot run today's High at 60. The plan is **Medium at a steady 60 (no frame over
33 ms) that looks like today's High**, and High kept for screenshots and future hardware. The look comes mostly from
baked light, good materials and the grade, which cost almost nothing per frame; the cost is in what is computed every
frame. So: bake more, compute less, and spend the frame where the eye is.

### 3a. Light: the clever part
| Technique | What it gives | Cost saved | Where |
|---|---|---|---|
| **Distance shadowmask** (Mixed lighting): far static shadows baked into a shadowmask, real-time sun shadows only near | Shadows all the way to the horizon with real-time ones only to 40 m | The largest single item: sun shadows 6.5-7.5 ms to about 2.5-3 | Lighting settings, `FPSKitTownGI` bake |
| **Small props cast no sun shadow past 25 m**, buildings cast from plain-box shadow stand-ins | Same silhouettes | 0.3-3.4 ms of the shadow cost | Builders (planned in the handoff) |
| **All indirect light baked** (lightmaps in rooms, probe volumes outside) and **per-room reflection captures** | The bounce, the soft shade, the room-coloured reflections | Already per-pixel lookups, no real-time GI | `DESERT_LIGHTING.md` part 1 |
| **Sun shafts as cards** in doorways and windows, dust particles in them | The "light you can see" look | No volumetrics | `DESERT_LIGHTING.md` 2.5 |
| **Height haze in one full-screen pass** | Depth and atmosphere | ~0.3 ms | 2.4 |
| **Contact darkening baked** (lightmap AO, a grime band at the foot of walls in the shared shader) instead of SSAO on Medium | Grounded corners | No SSAO pass | 2.3 |
| **Eye adaptation** (already metered cheaply, before post) | Real interior exposure | 0.4 ms | `EyeAdaptation` |
| **Fires and muzzle flashes**: no shadows on point lights (High's muzzle flash excepted), at most 8 at once | Warm flicker | Light count bounded | `FuelFire`, `FPSMuzzleFlashLight` |
| **Lamps at night baked** with an emissive surface, only the nearest few real-time | Lit streets | | Later night variant |

### 3b. The frame
| Technique | Gain | Where |
|---|---|---|
| **A frame cap** (60 by default; 45 and 30 "cool and quiet" in Settings) with vSync off and steady pacing | Less heat, no throttling: the 1 % low rises more than the average | P5: `QualityTiers`, `GameSettings` |
| **Dynamic resolution on the laptop too**, with STP (Unity 6) or FSR upscaling: 0.7-1.0 render scale on Medium | Heavy moments cost sharpness, not frames | P4 |
| **GPU occlusion culling** on (GPU Resident Drawer is already on) | Fewer hidden buildings drawn | P8 |
| **SMAA High to Medium**, then STP's own anti-aliasing instead | 1-2 ms | Handoff |
| **Far town as merged low meshes** (one HLOD per 64 m block past 150 m) and impostors for palms past 120 m | Triangles and draw calls in long views | Builders |
| **People LODs**: three levels per body (full, ~15k, ~4k triangles), an impostor past 60 m, animators culled completely past 40 m and updated at half rate past 20 m | The cost of O4 (drivers and residents) under control | `FPSKitTownLife`, characters |
| **Pooling** people, cars, shards and effects; spreading `RoomContents` toggles over frames; a shader warm-up during the briefing | The 1 % low (the stutter) | `DESERT_REVIEW.md` C2, F1 |

### 3c. Memory: 16 GB shared with the GPU
Budget for the game on Medium (the OS and other programs keep 4-5 GB):

| Item | Today (estimate) | Target | How |
|---|---|---|---|
| Textures | 1.5-3 GB (604 Poly Haven 2K maps plus the packs, full mip chains) | ≤ 1.2 GB | Mipmap streaming on (P2) with a 1 GB budget on Medium, 1.5 GB on High; 1K maps for small props; crunch (P3) |
| Meshes | Large (the merged town, every room district, people at full detail) | ≤ 1.5 GB | Stream room districts by distance (P7): only the 2-3 near the player; HLOD far off; people LODs |
| Light data (probe volumes, lightmaps, shadowmask) | ~0.4 GB | ≤ 0.4 GB | Probe disk streaming (P9); "seen-only" rooms at 20 cm (done) |
| Audio | small | ≤ 0.2 GB | Vorbis compressed in memory, music and ambience streamed |
| Scripts, physics, everything else | ~1 GB | ≤ 1 GB | Pooling; no Roslyn (P1) |
| **Total** | not measured | **≤ 4.5 GB** | Measure with the Memory Profiler in the release build after each step |

### 3d. Order (one change at a time, measured with `Tools/walk-bench.sh`, median of 3 runs on mains)
1. Frame cap and dynamic resolution on the laptop (P4, P5): an evening's work, and it removes the heat stutter.
2. Mipmap streaming and texture import sizes (P2, P3): memory.
3. Distance shadowmask and small-prop shadows: the biggest frame-time win.
4. GPU occlusion culling (P8), then SMAA to STP.
5. Room districts streamed by distance (P7); a desert-only build profile (P6); Roslyn out (P1).
6. People LODs and pooling (O4, C2), then the stutter hunt (no frame over 33 ms).
7. Re-check the look against the lighting tour after each light change, and keep only what passes.

**Done means:** Medium at 60 fps on the walk with a 1 % low of at least 50 and no frame over 33 ms, game memory under
4.5 GB, the lighting tour's rooms and streets within the targets of `DESERT_LIGHTING.md`, and High no worse than today.
