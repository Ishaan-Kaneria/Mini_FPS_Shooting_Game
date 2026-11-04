# Desert Outpost: status, verification, and what is left

2026-11-04. **The one file to read first.** It says what has been checked and how, what is in code and what is only a
plan, how the desert now plays as missions, and what the vehicles need to look real. The detail lives in the other
documents: `DESERT_REVIEW.md` (every bug, every object), `DESERT_LIGHTING.md`, `DESERT_GLASS.md`,
`DESERT_EXPLOSIVES.md`, `DESERT_TOWN_FIXES.md`, `DESERT_PEOPLE.md`.

## 1. How the code was verified (and what could not be)

No Unity in the cloud session. What was done instead, on all 28 C# files changed on branch `claude/gifted-lamport-ho6dv7`:

| Check | Result |
|---|---|
| Syntax: every file parsed with a full C# grammar (tree-sitter) | 28 files, **0 syntax errors** |
| Every engine and project API called by the new code looked up in the repo or URP 17 (`Health`, `DamageInfo`, `BlastSpec`, `Explosion.Blast/Play`, `OneShotAudio.Play`, `NoiseSystem.Emit/Listen/Latest`, `BombData` fields, `BombThrower.data/charges/ChargesChanged`, `Civilian.walkSpeed/runSpeed/animator/health/Dead`, `QualityTiers`, `LevelObjective.BuildBeacon`, `UITheme.Alert/Hazard`, `CreatePickupPrefab`, `MixamoClip`, `_roomMarks`, `_shopMarks`) | All exist with the signatures used |
| Name clashes across the 40+ `FPSKitSceneBuilder` partial files | One overload clash (`Hash01`, legal); renamed to `LifeHash` anyway |
| Logic traced by hand (households, explosives, grenades, target objective) | **Bugs found and fixed:** a resident leaving for a shop kept their seat and would have been teleported back onto it on reaching the shop; a lingering activity number could pull a dead body back out of its death animation; a fire component created a set in a field initialiser (against the project's reload rule); a `|=` that read its flag before the call that set it |
| HUD and touch controls pick up a grenade lent mid-level | Yes: `HudView` listens to `ChargesChanged`, `HUDController` and `TouchCluster` re-check `data` |

**Not verified, and most likely to need a fix on the laptop:**
1. Compile errors the parser cannot see (types, overloads). Expect a few; they are small.
2. URP's light-cookie API names (`lightCookieSize`, `lightCookieOffset`) and Forward+ on Medium: compile and measure.
3. Pose heights for sitting and lying (depend on the clips chosen).
4. Residents in houses whose front door is bolted: they live inside and never go shopping (by design, until doors open for residents).
5. Explosive props now have a `Health`. Checked: aim assist (`TouchAimAssist`) and the melee target pick only `EnemyAI`; the
   minimap leaves anything with a `Health` off its walls. Damage numbers will show on a barrel that is shot (harmless).
6. The town's first rebuild after all this: run the checks in section 6.

## 2. The desert as places and people to find

**Before:** 32 levels; enemies streamed toward the player from the town's 17 zones in order; objectives placed on random
navmesh points; bosses 1-10 unnamed ("BOSS 1"), arriving like everyone else and alert to the player from the start.
Nothing said "go there, kill him".

**Now (in code; needs `FPSKitBatch.ResetLevelSets` and a rebuild):**

| Level | Who | Where (distance from the start) | How you find him |
|---|---|---|---|
| 3 | The Quartermaster (boss) | Gas Station (320 m) | Brief names the place; map pip and a red column there |
| 6 | The Customs Man (boss) | Checkpoint (315 m) | same |
| 7 | The Paymaster + 3-4 guards (target) | Clock Square (75 m) | HUD: "TARGET: THE PAYMASTER AT THE CLOCK SQUARE - 70 M"; map pip; marker over his head |
| 9 | The Mechanic (boss) | Garage (310 m) | |
| 12 | The Scrap King (boss) | Junkyard (495 m) | |
| 15 | The Watchman (boss) | Water Tower (345 m) | |
| 17 | The Smuggler + guards (target) | Farms (540 m) | |
| 18 | The Courier (boss) | Bus Stop (155 m) | |
| 21 | The Fixer (boss) | Souk (160 m) | |
| 24 | The Well Keeper (boss) | Oasis (515 m) | |
| 27 | The Steward (boss) | Palace (395 m) | |
| 28 | The Radio Man + guards (target) | Old Town West (420 m) | |
| 30 | The Gatekeeper (boss) | Citadel (495 m) | |
| 32 | Aurel Auger | as before | |

* **They wait there.** A stationed target, his guards and a placed boss hold their ground on watch (EnemyBrain: they
  stand, look about, walk a short beat round their post) until they see or hear the player. The rest of the roster
  still streams in from the zones as before.
* **Fair to the clock.** Every place is reachable at a jog well inside its level's clock (the farthest boss, 495 m, is on
  a 265 s level); the drivable cars make it quicker. The clock still ends every level (the project's rule).
* **Scoring.** The target is the objective's weight (about a fifth of the level), the boss its boss weight, as before.
* **Never the mosque.** Nobody is hunted there.
* **Files:** `LevelSet` (objective `Target`, `targetName`, `targetPlace`, `targetGuards`, `bossPlace`), `LevelManager`
  (`FindPlace`, `TryPlacePoint`, `Station`, the boss at his place with a beacon), `LevelObjective` (the target), 
  `EnemyBrain` (a boss holding a place is not told where the player is), `FPSKitLevels` (`PlaceDesertLevel`),
  `FPSKitCampaign` (the ten names), `Missions` (card titles), `FPSKitObjectiveTest` (the new kind is tested).
  A side effect worth knowing: the Fairground's boss at its `bossSpawnPoint` now also waits until it notices the player,
  which is what `LevelManager`'s comment always said it did.

**Next for the missions** (plan): a compass strip at the top of the HUD with the target's bearing; a photo of the target
on the briefing card; intel pickups (a phone, a map) that reveal where the next target is; his guards' posts on roofs and
at doorways (needs the roof access of `DESERT_TOWN_FIXES.md` section 1); a target who flees in a car when the shooting
starts (the technical's driving code, with a driver).

## 3. The vehicles: how real they are, and what to do

| Vehicle | What it is | Audit (shape / surface / light) | What is wrong |
|---|---|---|---|
| Sedans A-D (traffic, parked, drivable) | One pack model, `RMCar26`, in four colours | REAL / NEEDS WORK-FAKE / NEEDS WORK | Every moving car in town is the same model; the lamps' material is pure black (albedo 0.000); parked "intact" sedans carry the burnt-wreck material on 28 % (`Parked_Car_Sedan_B`) |
| Sport car, touring car (parked) | Two other packs | REAL / FAKE / NEEDS WORK | Flat black parts (`Blacks1`, 23 %), no normal maps |
| Covered car | Poly Haven | REAL / REAL / NEEDS WORK | Fine |
| **Technical (armed pickup)** | **Built from boxes in the builder** (`MakeTechnical`) | NEEDS WORK / FAKE / NEEDS WORK | 98 % plain boxes, flat colours, **Unity cylinders for tyres and rims**, box headlamps, the M2 gun a palette texture. The least real thing in the town. |
| Traffic, all | `TrafficCar` | | **Nobody is driving** (no driver model in any moving car); moved along route points with no suspension, no body roll, wheels that may float or sink between points; no brake lights or indicators; one speed of engine sound or none |
| Drivable cars | `DrivableCar` | | The camera, steering, braking and exit faults (`DESERT_REVIEW.md` B1-B8) |

**What a real desert town's traffic is:** mostly pickups (the region's most common vehicle) and older sedans, white and
pale colours under dust, minibuses and taxis at the bus stop, motorbikes, a water tanker, a fuel tanker at the gas
station, lorries, burnt-out wrecks.

**Plan, in order:**
1. **A driver in every moving car**, and a driver for the technical: a civilian model seated (the `Life_SitChair` clip,
   or a Mixamo "driving" clip), hands laid on the wheel by IK, as the player's own hands are laid on the gun. This is
   the single biggest fix: an empty car moving reads as a toy.
2. **The technical rebuilt** on a real pickup model (commercial-use or CC0, 30-80k triangles, PBR, separate wheels and
   doors), with a real heavy machine gun model on a pintle and the gunner behind it; the box builder deleted.
3. **Variety:** 3-4 pickup models, a minibus, a taxi livery for the sedans, a lorry, a water tanker, motorbikes;
   colours weighted toward white, silver, beige; a dust layer on every body (the shared town shader's grime, by the
   upward-facing normal).
4. **Movement that has weight** (`TrafficCar`): ground-following at each axle (raycasts) for pitch and roll, a little
   body roll in turns and dive under braking, wheels that steer and spin, a dust trail behind on unpaved roads, engine
   sound pitched by speed, a horn when blocked, brake lights on braking, indicators at junctions, headlights at night.
5. **Materials:** the pack's lamp material out of black, the parked sedan's burnt-wreck share fixed (check
   `ParkedCarPrefab`), the sport and touring cars' flat parts given real maps (`FPSKitTownAssets` remap table).
6. **Damage** (with the explosives work): bullet holes, glass by kind (`DESERT_GLASS.md`), flat tyres, then fire and the
   burst; a car blown up becomes a wreck that stays.
7. **Driving** (`DESERT_REVIEW.md` B1-B12): fix before anyone drives a mission car.

## 4. Everything, in code or plan

| Area | In code on the branch | Plan only (documented with file, cause, fix, check) |
|---|---|---|
| Lighting | Medium Forward+ renderer; a reflection probe per room; grade controls; cloud shadows | Probe layers and indoor probes (the band on people indoors); per-room reflection captures; removing the arms/eye workarounds; Low and web probes; soft shadows on Medium; the late-afternoon sky; haze; sun shafts; windows |
| Glass | | Every stage (`DESERT_GLASS.md`) |
| Explosives | Drums, cans, cylinders, propane tanks, pumps; fire; chains | Gas station rebuilt in Blender; real drums and cans; scorch marks and wrecks |
| Grenades | Enemy drops; frags lent to a player with none | Enemies throwing grenades |
| People | Households (residents, shopping, shopkeepers, cowering); clip download and sort tools | Women, children, older people; props in hand; families; doors; reactions to the player; sound; time of day; pooling |
| Missions | Named bosses at named places; target levels; markers; the brain fix | Compass; intel; roof posts; a target who flees by car |
| Combat bugs | | Neck shot at low health; fire rate at low fps; blasts through walls; civilian blame; reported damage; negative score; double payout |
| Vehicles | | Section 3; driving fixes B1-B12 |
| Town | | Roof access for every building type; shops filled; realism steps (bevels, the shared shader, materials) |

## 5. Order for the laptop

1. Pull the branch, compile, fix what the compiler finds.
2. `FPSKitBatch.ResetThemes`, `FPSKitBatch.ResetLevelSets` (the new theme and level values), rebuild the desert.
3. Play levels 3 and 7 (a boss at the gas station, a target at the clock square), shoot a red drum, walk into a house.
4. The quick combat and driving fixes from `DESERT_REVIEW.md` section 11 step 1 (they are small and each has its code).
5. The vehicles' driver (section 3 step 1), then the technical.
6. Then lighting part 1, glass, roofs, shops, as the other documents order them.

## 6. Checks to run after the rebuild

```
Tools/unity-batch.sh FPSKitBatch.VerifyLevels
Tools/unity-batch.sh FPSKitBatch.VerifyObjectives
Tools/unity-batch.sh FPSKitBatch.VerifyCombat
Tools/unity-batch.sh FPSKitBatch.VerifyBomb
Tools/unity-batch.sh FPSKitBatch.VerifyStatics
Tools/unity-batch.sh FPSKitBatch.VerifyTown
```
Read the build log for `[FPSKit] explosives:`, `[FPSKit] households:` and the civilians' clip warning. In play, watch for
a target never appearing (the HUD keeps "TARGET: ... AT ..." with no distance): that means no point of its place has a
route to the player.
