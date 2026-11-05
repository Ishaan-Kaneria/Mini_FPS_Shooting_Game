# Handoff: what the cloud session wrote (2026-11-04), for Claude Code on the laptop

**Read this before anything else on branch `claude/gifted-lamport-ho6dv7`.** Everything below was written by Claude Code
in a cloud session **with no Unity**: none of it has been compiled, built, baked or played. All 28 changed C# files
parse (a C# grammar check), and every API they call was looked up in the repo, but expect compile errors, wrong numbers
and wrong poses. **Treat every item as unverified until you have compiled it, run its check and the developer has
played it.** Where a fix here is wrong, fix it here (in the generators and runtime code, never in generated scenes),
and say what you changed. Two attempts per problem, then stop and give options (the project's rule).

Base: `261c706` (GitHub `main`). 9 commits, 54 files, about 3,000 lines of code.

## 1. Code written (features)

| # | Feature | Files | Needs before it works |
|---|---|---|---|
| F1 | **Medium on its own Forward+ renderer**, so indoor reflections are not switched off there | `Editor/FPSKitQualityTiers.cs` (`MediumForwardPlus`) | `FPSKitQualityTiers.Ensure` runs (desert build/bake); commit the new `Assets/Settings/Medium_Renderer.asset` + `.meta`; measure Medium's frame cost |
| F2 | **A reflection probe per room** (box projected) instead of one per house | `Editor/FPSKitTownGI.cs` (`PerRoomProbes`) | Desert rebuild |
| F3 | **Grade controls** (white balance, split toning), neutral by default | `Runtime/LevelTheme.cs`, `Editor/FPSKitSceneBuilder.cs` | Set values, `ResetThemes`, rebuild |
| F4 | **Cloud shadows** (a drifting cookie on the sun), off by default | `Runtime/CloudShadows.cs`, `FPSKitSceneBuilder.BuildLighting` | A cloudy sky; check URP's `lightCookieSize/Offset` names compile |
| F5 | **Explosive props**: petrol/diesel drums, jerry cans, gas cylinders, propane tanks, fuel pumps; fire; chains | `Runtime/ExplosiveProp.cs`, `Runtime/FuelFire.cs`, `Editor/FPSKitTownExplosives.cs`, `FPSKitTown.cs`, `FPSKitTownEdge.cs`, `FPSKitSceneBuilder.cs` | Desert rebuild; log line `[FPSKit] explosives:` |
| F6 | **Grenade drops**; frags lent to a player with no bomb | `Runtime/Pickup.cs` (`Kind.Grenades`, appended), `Runtime/BombThrower.cs` (`AddCharges`), `Runtime/LevelManager.cs`, `Runtime/LevelTheme.cs`, `Editor/FPSKitThemes.cs`, `FPSKitSceneBuilder.cs` | `ResetThemes`, rebuild |
| F7 | **Households**: residents in the enterable houses doing everyday things at the furniture, shopping, shopkeepers, cowering at gunfire | `Runtime/LifeActivity.cs`, `Runtime/TownHouseholds.cs`, `Runtime/CivilianLife.cs`, `Editor/FPSKitTownHouseholds.cs`, `FPSKitTownInterior.cs`, `FPSKitTownFurniture.cs`, `FPSKitTownLife.cs`, `FPSKitMixamoImport.cs` | The everyday clips: `Tools/mixamo-download-life.js` then `Tools/sort-life-clips.py`; rebuild; log line `[FPSKit] households:` |
| F8 | **Missions**: named bosses waiting at named places with a marker; "Target" levels (a named man and guards at a place, HUD distance) | `Runtime/LevelSet.cs` (`Objective.Target`, appended; `targetName/Place/Guards`, `bossPlace`), `Runtime/LevelManager.cs` (`FindPlace`, `TryPlacePoint`, `Station`, `SpawnTypeAt`), `Runtime/LevelObjective.cs`, `Runtime/EnemyBrain.cs`, `Runtime/Missions.cs`, `Editor/FPSKitLevels.cs`, `Editor/FPSKitCampaign.cs`, `Editor/FPSKitObjectiveTest.cs` | `ResetLevelSets`, rebuild |
| F9 | **Bosses a man's size with an elite security detail** | `LevelSet` (`bossScale`, `bossGuards`), `LevelManager.SpawnBoss`/`SpawnLevel` order, `FPSKitLevels.PlaceDesertLevel` | `ResetLevelSets` |
| F10 | **Drivers in traffic cars**, shot through open windows | `Runtime/VehicleDriver.cs`, `Runtime/TrafficCar.cs`, `Runtime/TrafficDirector.cs`, `FPSKitTownLife.TrafficPrefab` (collider split) | Rebuild; `Life_Drive` clip |
| F11 | **Cars run people over**; drivers react to people after a human delay | `Runtime/VehicleStrike.cs`, `TrafficCar.cs`, `DrivableCar.cs`, `FPSKitTownLife.cs` | Rebuild |
| F12 | **Player limbs**: wall punches and kicks hurt and injure (bruised, swollen, fractured); strikes stop at walls; legs and rifle kept out of walls | `Runtime/PlayerLimbs.cs`, `Runtime/MeleeStrike.cs`, `Runtime/PlayerBody.cs`, `Runtime/Weapon.cs`, `Runtime/PlayerMotor.cs` | Compile only |
| F13 | **The town's edge stops cars too** (they drove off the map) | `Editor/FPSKitSceneBuilder.cs` (`EnsureProjectTagsAndLayers`) | `EnsureProjectTagsAndLayers` runs (the build does); check a driven car stops at the edge |
| T1 | Tools | `Tools/desert_review_objects.py`, `Tools/mixamo-download-life.js`, `Tools/sort-life-clips.py`; `.gitignore` (`__pycache__`) | |

## 2. Known bugs and risks in that code (fix or confirm each)

| # | Risk | Where | What to check |
|---|---|---|---|
| R1 | **Compile errors** the parser cannot see (types, overloads, missing `using`) | all of the above | Compile first; fix one at a time |
| R2 | `AnimatorConditionMode.Equals` and the any-state transitions in `AddLifeStates` | `FPSKitTownHouseholds.cs` | The civilians' animator builds; a dead civilian is not pulled back into an activity state |
| R3 | URP light cookie properties `lightCookieSize` / `lightCookieOffset` | `CloudShadows.cs` | Compiles in URP 17.6 |
| R4 | The `|=` order fix in `Ensure` (renderer made before the `|=`) | `FPSKitQualityTiers.cs` | `Medium_RPAsset` points at `Medium_Renderer`; Low still at `Mobile_Renderer` |
| R5 | **Sitting and lying poses** float or sink; the bed pose may need a quarter turn | `LifeSpot` calls in `FPSKitTownInterior.cs`, `FPSKitTownFurniture.cs` | Tune the spot heights/offsets there, never the clips |
| R6 | Residents behind a bolted front door never go out | `CivilianLife` | Accept or open doors for residents |
| R7 | Mixamo "Praying" is not salah | `mixamo-download-life.js` | Leave `Life_Pray` out, or capture it with the mocap pipeline after the developer's approval |
| R8 | A stationed target, guards or boss can appear in view if the player is already there | `LevelManager.Station` | Add a visibility test |
| R9 | Residents' "not in view" test ignores walls | `TownHouseholds.Spawn` | Acceptable; note it |
| R10 | A dead driver "slumps" by freezing the pose; his car still turns corners while rolling to a stop | `VehicleDriver`, `TrafficCar` | Look at it in play |
| R11 | Driver seat position is a guess from the car's bounds | `FPSKitTownLife.TrafficPrefab` (`seatLocal`) | The driver sits in the seat, head below the roof |
| R12 | The car's collider split (body to the waistline + roof) | `TrafficPrefab` | Rounds through the windscreen hit the driver; the car still blocks the player and traffic |
| R13 | Run-over damage numbers (30 at 5 m/s, 160 at 10, 400 at 15) and the 0.7-1.2 s reaction | `VehicleStrike`, `TrafficCar` | Feel; side hits are not checked |
| R14 | **Performance**: up to 15 drivers and 54 residents/keepers, each 40-120k triangles, no people LODs | `VehicleDriver`, `TownHouseholds` | `FrameCostProbe` on Medium; lower the budgets if needed |
| R15 | Explosive props carry a `Health`: damage numbers appear on barrels | `ExplosiveProp` | Aim assist and melee were checked (they take only `EnemyAI`) |
| R16 | Petrol drums: one red material copy per source material | `FPSKitTownExplosives.PaintFuelDrum` | Assets made under `Town/Materials` |
| R17 | The grenade pickup is a placeholder capsule; bursts leave no scorch | `FPSKitSceneBuilder`, `ExplosiveProp` | Replace later |
| R18 | `EnemyBrain` change: a boss with `relentless` off is no longer told where the player is (also the Fairground's boss at its spawn point) | `EnemyBrain.Start` | `VerifyCombat`; a placed boss waits until he notices |
| R19 | `SpawnLevel` now sets the roster before the boss (so `Station` has enemies to take) | `LevelManager.SpawnLevel` | `VerifyLevels`; enemy counts unchanged |
| R20 | Limb strain numbers (fist on concrete: swollen by the 3rd punch, broken by the 5th) | `PlayerLimbs` | Feel; `VerifyCombat` (landed punches now strain the fist a little and cost a little health) |
| R21 | A strike blocked by a wall plays back from the contact (`StrikeProgress` mirrored) | `MeleeStrike` | `VerifyMeleeMotion`, `VerifyKickView`, `VerifyGrapple` still pass |
| R22 | The rifle's wall tuck uses a 0.85 m cast | `Weapon.UpdateWallTuck` | No tuck in open ground; no jitter along a wall |
| R23 | Medium's Forward+ renderer costs frame time | `FPSKitQualityTiers` | Under 1 ms extra on Medium, or revert with `MediumForwardPlus = false` |

## 3. Order to verify (Unity closed for the batch jobs, the memory watchdog on)

1. Pull the branch. Open Unity and compile. Fix R1 (and R2-R4 as they show).
2. `FPSKitBatch.ResetThemes`, `FPSKitBatch.ResetLevelSets`. Then rebuild the desert (`Tools/build-desert.sh` or the
   usual chain); commit `Medium_Renderer.asset`.
3. Checks:
   ```
   Tools/unity-batch.sh FPSKitBatch.VerifyStatics
   Tools/unity-batch.sh FPSKitBatch.VerifyCombat
   Tools/unity-batch.sh FPSKitBatch.VerifyWounds
   Tools/unity-batch.sh FPSKitBatch.VerifyBomb
   Tools/unity-batch.sh FPSKitBatch.VerifyLevels
   Tools/unity-batch.sh FPSKitBatch.VerifyObjectives
   Tools/unity-batch.sh FPSKitBatch.VerifyTown
   Tools/unity-batch.sh FPSKitBatch.VerifyGrapple
   ```
   plus `VerifyMeleeMotion` and `VerifyKickView` if they are batch methods.
4. Read the build log for `[FPSKit] explosives:`, `[FPSKit] households:`, and the civilians' "no everyday clips" warning.
5. Play with the developer, one feature at a time: level 3 (boss at the gas station), level 7 (the Paymaster), a red drum,
   a house with people in it, a driver shot through the windscreen, standing in front of a fast car, five punches on a
   wall. Fix what he finds; commit after each.
6. `FrameCostProbe` and `Tools/walk-bench.sh` on Medium before and after (R14, R23).

## 4. What is only a plan (not code)

Written up with file, cause, fix and check, in: `Docs/DESERT_BUGS_AND_PERFORMANCE.md` (the master list),
`DESERT_REVIEW.md`, `DESERT_LIGHTING.md`, `DESERT_GLASS.md`, `DESERT_EXPLOSIVES.md`, `DESERT_TOWN_FIXES.md`,
`DESERT_PEOPLE.md`, `DESERT_VEHICLES.md`, `DESERT_BODY.md`, `DESERT_STATUS.md`. Start them only after section 3 passes.
