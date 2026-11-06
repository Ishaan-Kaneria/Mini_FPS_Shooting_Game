# Desert: vehicles, drivers, running people over, and the bosses

**2026-11-06:** V2, V7 (brake lamps), V8 and V11 are done, see `DESIGN_NOTES.md` "Cars of our own" and "Driving from the seat". The technical (V3) and wrecks are still the old models.

2026-11-04. His notes: "no riders in a moving car, and the cars look very similar"; "custom cars, trucks and bikes in
Blender with a driver moving in them"; "shoot through the windscreen and kill the driver"; "a fast car runs over a
person walking in front of it"; "the bosses are very large and not special: make them mafia bosses, black shirts, dark
glasses, advanced weapons, very tight security." Section 1 is the bug list; the rest says what is now in code (not
compiled, built or played: no Unity in the session that wrote it) and what is the plan.

## 1. The vehicle bugs

| # | Bug | Where | Status |
|---|---|---|---|
| V1 | **Nobody drives the moving cars.** No driver model in any traffic car or in the technical | `FPSKitTownLife.TrafficPrefab`, `MakeTechnical` | **Traffic: fixed in code** (section 2). Technical: plan (it needs its rebuild, V3) |
| V2 | **Every moving car is the same car.** All traffic is one model (`RMCar26`) in four colours; there are only three car models in the project (that sedan, a sports car, a touring car) | `FPSKitTownAssets` pack list, `TrafficDirector.carPrefabs` | Plan (section 4) |
| V3 | **The technical is built from boxes**: 98 % plain boxes, flat colours, Unity cylinders for tyres and rims, box headlamps; its M2 a palette texture | `FPSKitTownLife.MakeTechnical` | Plan (section 4) |
| V4 | **No weight in the movement.** Moved along route points: no suspension, no pitch over a rise or roll in a turn, wheels that can hover or sink between points | `TrafficCar.Update` | Plan (review B14) |
| V5 | **Cars always stopped for people at once**, so nobody was ever hit; and a car could not hurt anyone | `TrafficCar` | **Fixed in code** (section 3) |
| V6 | **A round could never reach a driver**: the car's collider was a box over the whole car, windows included | `TrafficPrefab` | **Fixed in code** (section 2) |
| V7 | No lights that mean anything: brake lights, indicators, headlights at night; the pack's lamp material is pure black | `TrafficCar`, `FPSKitTownAssets` | Plan |
| V8 | No engine sound pitched by speed, no horn, no tyre noise | | Plan |
| V9 | No damage: holes, glass, tyres, fire | | Plan (`DESERT_GLASS.md`, `DESERT_EXPLOSIVES.md`) |
| V10 | Parked "intact" sedans carry the burnt-wreck material (28 % of `Parked_Car_Sedan_B`); sport and touring cars have flat black parts | `ParkedCarPrefab`, remap table | Plan |
| V11 | The drivable car's camera, steering, braking, exit (review B1-B8); its own crashes into an enemy stop it dead | `DrivableCar` | Plan (B1-B8); running people over: **in code** |
| V12 | No dust behind cars on the sand roads, no dust on the cars | | Plan |

## 2. Drivers, and shooting them through the glass (in code)

* **`VehicleDriver`** (new): every traffic car is given a driver when it appears, one of the town's civilian models with
  everything that walks taken off, seated at the left front (`TrafficCar.seatLocal`, measured from the car by the
  builder), in the driving pose (the animator's `Life_Drive` state, fetched by `Tools/mixamo-download-life.js`). His body
  is a short capsule from the seat to the top of the head.
* **The windows are open to rounds.** The traffic car's collider is now the body up to the waistline and the roof, with
  nothing between them (`TrafficPrefab`): a round through the windscreen or a side window reaches the driver, and kills
  him as it would anyone. The navmesh obstacle still covers the whole car.
* **When the driver is killed** he slumps in his seat; the car rolls on, slowing, to a stop, and stays where it stopped
  while the player is near (`TrafficCar.Abandoned`). A driver the player kills is a civilian casualty; one killed by
  anyone else is not.
* **Next:** the glass itself (a hole and cracks where the round went through, the side windows falling out in cubes,
  the windscreen holding together in a web: `DESERT_GLASS.md` stage G7); a proper dead-at-the-wheel pose instead of the
  frozen slump; hands on the wheel by IK, turning with the steering; a passenger now and then; the driver getting out
  and running when the shooting starts.

## 3. Running people over (in code)

* **`VehicleStrike`** (new) on every traffic car and every drivable car: anyone in front of the bumper (behind it,
  reversing) while the vehicle moves faster than 2.5 m/s takes a blow growing with the square of the speed: about 30 at
  5 m/s (knocked about), 160 at 10 m/s (dead), 400 at 15 m/s. It is a heavy melee blow, so an enemy goes down in a
  ragdoll along it. Credited to the player while he drives, else to the car (a civilian killed by traffic is not his).
  The player can be hit too.
* **Drivers react like people.** A traffic car brakes for another car at once, but for a person stepping out only after
  the driver's reaction time (0.7-1.2 s, per driver). At 15 m/s that is 10-18 m before the brakes even start, so a man
  who steps out close in front of a fast car is hit, and one who steps out far ahead is not. After a hit the driver
  stops and sits in shock (25 s).
* **Next:** civilians ragdoll when hit (they have only a death clip), a bonnet thud and a body thrown over it at speed;
  people looking before they cross; the horn when someone is in the road; enemies jumping clear of a car driven at them;
  the drivable car passing through a body it has killed instead of stopping dead on it.

## 4. Real vehicles of our own, in Blender

**What is honest to say:** a car that looks real is modelled by hand (or bought). A Python script in Blender can block a
car out, but it will look generated, which is the thing to get away from. What scripts do well is everything around the
model: splitting, naming, rigging, LODs, export and the import into Unity. So:

**The set the town needs** (a Middle Eastern or North African town, not any one brand):

| Vehicle | Why | Notes |
|---|---|---|
| Pickup, single and double cab (2-3 models) | The region's most common vehicle | Also the technical's base |
| Old sedan (have it) + a taxi livery | Traffic | Taxi: two-tone, roof sign |
| Minibus | Bus stop, traffic | Sliding door, roof rack with luggage |
| Water tanker, fuel tanker | Gas station, houses | The fuel tanker is an explosive (`DESERT_EXPLOSIVES.md`) |
| Lorry | The highway | Tarpaulin load |
| Motorbike, scooter | Traffic in the old town | Rider leans into turns |
| Bicycle | Old town | Pedalling rider |
| Armoured SUV | The bosses' convoy | Black, tinted glass |
| Technical (pickup with a heavy machine gun) | Enemy | Replaces the box build (V3) |
| Wrecks (burnt, stripped) | Junkyard, roadsides | From the same models |

**How each is built** (one `.blend` per vehicle, in `Art/Vehicles/<name>/`, outside `Assets/`; the FBX goes in
`Assets/Vehicles/<name>/`):
1. **Reference:** side, front, top and back photos of a real vehicle of that type (no brand marks on the model).
2. **Modelling:** a clean quad body, real panel gaps (a 3-5 mm groove), bevels on every edge (Bevel modifier with angle
   limit, 2-3 segments), weighted normals; 30-80k triangles for LOD0. **Separate objects** for: body, each door, bonnet,
   boot, each wheel (tyre and rim separately), windscreen, each side window, rear window, headlamps and tail lamps
   (lens and reflector), mirrors, number plates, seats, steering wheel, dashboard, and for a pickup the bed.
3. **Empties the game reads** (named exactly): `Seat_Driver`, `Seat_Passenger`, `Wheel_FL/FR/RL/RR` at each wheel's
   centre, `Hinge_Door_FL` (and the rest) on each hinge axis, `Steer_Pivot`, `Light_Head_L/R`, `Light_Tail_L/R`,
   `Light_Brake`, `Exhaust`, `Gun_Mount` (technical), `Rider_Seat` (bikes).
4. **Materials by kind** so the realism audit can judge them: `_CarPaint` (URP Complex Lit with clear coat), `_Glass_Laminated`
   (windscreen), `_Glass_Tempered` (side and rear), `_Rubber`, `_Chrome`, `_Plastic`, `_Fabric`, `_LampLens`, `_PlateMetal`;
   textures from CC0 sets (ambientCG, Poly Haven) plus a painted dirt and dust mask; colours weighted to white, silver
   and beige, faded by the sun.
5. **Damage:** shape keys for dents on each panel, a second "burnt" material set, a broken-glass mesh for each window.
6. **LODs:** LOD1 at 40 %, LOD2 at 10 % (Decimate), a cull past about 250 m.
7. **Export:** FBX, Apply Transform, Y up, metres, embedded nothing (textures beside it).

**What can be written for it next** (on request): `Tools/blender/vehicle_prepare.py` (headless: checks the names and
empties above, applies modifiers, makes the LODs, renames materials by kind, exports), and `FPSKitVehicleImport` (Unity:
reads the empties into WheelColliders, seats, door hinges, light objects and `TrafficCar`/`DrivableCar` fields, so a new
vehicle is a drop-in). Then the drivers: `VehicleDriver` in `Seat_Driver`, hands on the wheel by IK; a bike rider
leaning by the bike's turn rate.

**The other route:** commercial-use or CC0 vehicle models (Sketchfab with a commercial licence, the Asset Store), checked
against the realism bar, then put through the same Blender preparation. Logged in `LICENSES.md`.

## 5. The bosses

**What they are now:** the desert's bosses 1-10 are the two generic boss bodies, "Warden" (2.2x a man's size) and
"Harbinger" (1.9x), in the soldiers' printed camouflage with the soldiers' rifle. Giants in fatigues: that is why they
are "very large and not special".

**In code now** (`LevelSet.bossScale`, `bossGuards`; `LevelManager.SpawnBoss`; `FPSKitLevels.PlaceDesertLevel`):
* each desert lieutenant is a man's size (1.06);
* he waits at his place (as before this round) with **an elite security detail** round him, 6 to 15 guards up the
  ladder, taken from the level's roster, holding the place with him; the brief says how many;
* the brief and the map say where (the previous round).

**Plan: the look** (needs models; the camouflage outfit is a printed texture, so no tint can make a black shirt):
1. **An outfit for each boss:** black or dark shirt open at the collar, dark trousers, leather shoes, a watch, a gold
   chain for some, **dark glasses** (a prop laid on the head's socket each frame, as the guns are: never parented into the
   Animator), a head of hair and a beard of his own. Made with MPFB2 (the CC0 base already tried, `e9688f3`) and its
   clothing tools in Blender, or a commercial-use character, rigged Humanoid so the soldiers' clips play on him.
2. **Each of the ten different**: the Quartermaster in a short-sleeved black shirt, the Customs Man in a dark uniform jacket,
   the Fixer in a suit, the Scrap King in a leather jacket, and so on. A short entrance (he turns, takes off the
   glasses) when the player first sees him.
3. **Bodyguards who look the part:** dark clothes, plate carriers, earpieces; a tighter formation round him (front,
   both sides, a pair at the door), two on a roof once roofs can be reached (`DESERT_TOWN_FIXES.md` section 1).
4. **Weapons:** a compact rifle with an optic and a suppressor for the boss, a short rifle or a submachine gun for the
   guards, pistols in hand when close (the enemies' weapon is `EnemyRifle`; a new `WeaponData`-like asset per enemy gun).
5. **How he fights:** stays behind the guards, falls back to a second position when they go down, calls reinforcements,
   and at half health tries to leave in the armoured SUV (section 4), which the player can chase.

## 6. Order

1. Compile; the life clips including `Life_Drive`; rebuild; check drivers sit right and die to a round through the
   windscreen; stand in the road in front of a car on the highway.
2. Replace the technical (V3) and add pickups (V2), through the Blender preparation of section 4.
3. Vehicles' movement, lights and sound (V4, V7, V8), then damage with the glass (V9).
4. The bosses' look, one boss first, approved before the other nine.
