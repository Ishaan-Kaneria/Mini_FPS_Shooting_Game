# Desert town: people, shops, roofs, cars, objects, houses

2026-11-04, from his play-test notes ("there are no people; in some shops we can clearly see everything; there is no
way up to the top of the buildings, no staircase; some shops are so open it looks bad; cars, objects, houses need
changes"). Each item gives what the code does now, the cause, and the fix. Written from the code, not from play.

## 1. No way up to the roofs (bug)

* **Cause, confirmed in code:** an outside stair to the roof is planned only for **enterable villas**
  (`FPSKitTownBuildings.BuildVilla` calls `PlanStair`; nothing else does). Apartments, commercial blocks, shop rows,
  row houses, old houses, courtyard houses and compounds never get one, and their roofs are kept off the navmesh
  (`roofWalk` false: a Not Walkable volume over the roof, `FPSKitTownBuildings.cs:784`). The main avenue is almost all
  apartments and commercial blocks, so along it there is no way up at all.
* **Fix:**
  1. Call `PlanStair` (or a variant) for every building kind with a flat roof, before `BuildMass` (the parapet gap is cut
     from the plan): apartments and commercial blocks get an **inside stair core** to a roof door (a stair-head box on
     the roof with a door), row houses and shop rows an outside stair at the end of the row or in a back yard, old
     houses a timber ladder or a mud stair in the courtyard (as real ones have).
  2. Not every roof: about one building in three in each block, and every roof that has a way up keeps two ways off
     (the rule the Rooftop arena already follows: `CLAUDE.md`, "every walkable roof keeps two escapes"): a second stair,
     a ladder, or a jump to a neighbour's roof (a gap under 1 m and a step under 0.4 m).
  3. Roofs that become walkable need their clutter checked (water tanks, aircon units, satellite dishes: on
     `NoStanding`, out of the stair's landing, `FPSKitTownBuildings.cs:995`), parapets 1 m (cover), and the town's
     `SealTownIslands` will then keep them (they are joined to the street).
  4. The player cannot climb ladders yet: either stairs only, or add a ladder volume to `PlayerMotor` (enter, move up and
     down with the move keys, step off at the top). Enemies need an off-mesh link for a ladder.
* **Check:** `VerifyTown` "no roof islands" plus a new count: walkable roofs per block, each with two ways off;
  `VerifyReach`; play: walk up from the avenue.

## 2. No people

**Update 2026-11-04:** households (residents living in the houses, shopping, shopkeepers) are now in code; the full
examination and the design are in `Docs/DESERT_PEOPLE.md`. What follows is the street walkers' side.

* **What the code does:** `CivilianDirector` keeps up to `townCivilians` (60) on High, 60 % (36) on Medium, 35 % (21)
  on Low and phones, spread over a town of about 250,000 m². They are put down 30-150 m from the player (never closer),
  never within 25 m of an enemy, and later ones only out of view (`CivilianDirector.cs`). At the first shot every one
  who hears it runs for a door; once inside and out of view they are removed, and new ones arrive 30-150 m away, out of
  view, and run again at the next shot. There are four civilian models, all men (`Civilian_Man_01-04`).
* **Why it looks empty:** in a fight (always, in an FPS level) the town clears itself, and nobody is ever put down
  near the player. 36 people over a town this size is one person per 7,000 m² even before they run.
* **Fix:**
  1. **Ambient people who stay:** shopkeepers behind counters, men at café tables, people at windows and on roofs,
     a mechanic at the garage, an attendant at the gas station. Placed by the builder at the shops' and buildings'
     marks; they duck down or lie flat when shooting starts and stay there (they are not removed). Cheap: mostly sitting
     or standing animations, no navigation; LOD and culled by distance with `RoomContents`.
  2. **Walkers nearer:** spawn range 12-80 m (out of view still), more on the avenue and the souk; when a fight starts,
     they run, but those out of the fight's hearing carry on (the player's shots carry `PlayerNoise.gunshot` metres; keep that well short of the whole town).
  3. **More people:** budget by area near the player rather than for the whole town (e.g. 25 within 80 m on Medium),
     with an animation LOD (`Animator.cullingMode` already culls updates off screen).
  4. **Variety:** women, older men, children (not as targets: civilians stay a penalty), in local dress; Mixamo
     characters or a commercial-use pack, logged in `LICENSES.md`; the MPFB body generator (`e9688f3`) for more.
  5. With review C1: a civilian killed by an enemy must not count against the player.
* **Check:** a count of civilians within 50 m of the player over the release walk (`WalkProbe` can log it), before
  and during a fight.

## 3. Shops: "we can see everything", "so open it looks bad"

* **What the code does** (`FPSKitTownShopfront.cs`): each shop is open behind glass, open to the street (a stall),
  half-shuttered or shut; its inside is a box behind the front, lit evenly (the tubes, or lightmapped), with few goods.
  The handoff already listed "shop interior: evenly lit, nearly empty". An open-front shop shows its whole empty box to
  the street: bare walls, a floor, the back wall, nothing to stop the eye.
* **Fix:**
  1. **Fill them.** Every shop gets a counter and a till at the front or side, shelving on the walls stocked by trade
     (groceries, phones, hardware, fabric, a pharmacy, a barber with a chair and a mirror, a butcher, a café with tables),
     goods on the floor and hanging from the ceiling, signs and price cards, a back-room door with a curtain. Props from
     Poly Haven where they exist, Blender kitbashes for the rest (shelving units and boxes instanced, cheap). The shop's
     trade already exists as data (`ShopLook`, the signs): drive the dressing from it.
  2. **Real depth:** 4-8 m deep, the back wall broken by shelving and the back-room door; nothing over 10 m.
  3. **Fewer wide-open fronts:** stalls (fully open) only in the souk; elsewhere glazed fronts with a door, half-down
     shutters, awnings, goods stacked outside on the pavement (they break the view in and make the street busy).
  4. **Light that falls off:** tubes over the counter and the shelves, darker at the back; daylight from the front
     (the lightmaps already do this; check the dressing is in the bake).
  5. **A shopkeeper** in most open shops (section 2).
* **Check:** the realism audit for `Town_ShopInterior`, `Town_ShopInsideLit` (both FAKE now); the street views in the
  lighting tour; `VerifyReach` (shops you can enter stay reachable past the counter).

## 4. Cars

From `Docs/DESERT_REVIEW.md` B1-B14 and the realism audit:
* Driving: the camera bug (B1), no camera collision (B2), steering, braking, the exit (B3-B8): fix before anything else.
* Parked cars: FAKE surfaces (an intact sedan carries the burnt-wreck material on 28 % of it, `Parked_Car_Sedan_B`;
  `bottom_interior` flat on 47 % of the sport car); the technical's tyres and rims are Unity cylinders.
* Traffic floats or sinks between route points (B14); the technical drives through everything (B13).
* Damage (the queued feature): bullet holes, glass by kind (`Docs/DESERT_GLASS.md`), tyres, then fire and the burst
  (`Docs/DESERT_EXPLOSIVES.md` section 4).

## 5. Objects

The realism audit's table (`Docs/DESERT_REVIEW.md` section 13) lists every object with its faults and where it is made.
The ones that matter most by how much of the view they take: edges with no bevel (222 objects), tiling (66), flat
colours (95), too-dark pack materials (64). Steps 1-3 of `Docs/DESERT_REVIEW.md` G1.

## 6. Houses

* Windows: real glass with real space behind (`Docs/DESERT_GLASS.md`).
* Roofs: section 1.
* Walls: bevels on every corner, sill and frame; the shared town shader (grime at the foot, macro variation, no
  visible tiling); real door panels.
* Insides: real reflections per room and the probe-lit band (`Docs/DESERT_LIGHTING.md` part 1); the old houses' dark
  rooms (review E5).
* Life in and on them: washing lines, water tanks and dishes on roofs, plants on balconies, open windows with curtains
  moving, people at windows (section 2).

## 7. House and shop numbers, and a map you can read

**Now:** no building has a number or an address. Streets have only builder names (`East130N`, `SouthStreet`,
`AvenueSouth`: `FPSKitTownPlan.cs:132-151`). The minimap (`Minimap`) is a 62 m disc of walls drawn in a pale grey-blue
(`wallColor` 0.62/0.66/0.74, `coverColor` 0.38/0.42/0.49): no roads, no building kinds, no names, no numbers, and there
is no full map at all. Finding "the courier at the bus stop" means knowing the town already.

**Plan:**
1. **Street names.** Every road gets a real name in Arabic and English (a name table in `FPSKitTownPlan`: e.g. the avenue
   "Shari' al-Jumhuriya / Republic Avenue", the boulevard "Corniche Road", the streets after people, trees and trades);
   the builder name stays as the id. Name plates at every junction corner, from `Tools/town/make_signs.py` (real Arabic,
   as the shop signs already are).
2. **Numbers.** Each building gets a number on its street: odd on the left and even on the right going away from the
   clock square, as towns number them; a shop is "No. 14, Republic Avenue", a house "No. 7, Pomegranate Street". A
   blue enamel plate beside each door with the number in Arabic-Indic and Western digits (a digit atlas made by
   `make_signs.py`, quads in the facade's detail mesh, so no new objects). The builder writes the town's **address
   book**: number, street, kind (house, shop and its trade, landmark), door position and footprint (a `TownAddresses`
   component, data only, beside `TownHouseholds`).
3. **A map that is not pale.** The minimap redrawn with a real palette: the ground a warm dark sand, roads in asphalt grey
   with their kerbs, pavements lighter, buildings filled by kind (houses ochre, shops amber, landmarks white, ruins
   broken outline), yards and compounds in a low tone, the player's view cone, north up or rotating (a setting), and
   building numbers drawn when zoomed in.
4. **A full map** (the M key, Back on a pad, a map button on touch): the whole town, district names, street names, every
   landmark with an icon, house and shop numbers when zoomed, the target and the boss places marked, your position and
   heading, drag and pinch to pan and zoom. Built in C# with `UIKit` like the rest of the UI (CLAUDE.md "UI").
5. **Objectives say the address:** "TARGET: THE COURIER, BUS STOP, No. 3 STATION ROAD - 150 M", and the full map opens on
   him.
6. **Check:** every enterable house and shop has a unique address; every address's door is on the navmesh; the map's
   numbers match the plates.

## 8. What looks generated or unreal (his list, and the audit's)

From the realism audit (`Docs/REALISM_AUDIT_DesertOutpost.csv`, 100 objects rated FAKE) and from reading the
builders. Each line: what it is, why it reads as generated, the fix (details in `DESERT_REVIEW.md` sections 8 and 13).

| # | What | Why it reads as generated | Fix |
|---|---|---|---|
| U1 | **Every edge in the town** (222 objects, ~65 % of the view) | Not one bevel: knife-sharp corners on walls, sills, steps, kerbs, frames (`Town_TrimStone_Plain` 136 km of hard edge, 0 % bevelled) | Chamfers in `TownMesh`; shader edge-rounding far off |
| U2 | **The street grid** | Every road straight and parallel to x or z (`FPSKitTownPlan`: "every road is parallel to x or to z"), one roundabout; no bend, no lane, no alley that wanders | Old town lanes that curve and narrow; a bend in the streets that follow the ground; cul-de-sacs |
| U3 | **Tiling** (66 objects) | The same stone repeating 57 times across a face (`Town_StoneWarm`, the most seen surface) | The shared anti-tiling shader with macro variation and grime |
| U4 | **Flat colours** (95 objects) | Single-colour materials with no texture or normal map: shutter boxes, shop insides, ceilings, curtains, road paint, galvanised steel, wrought iron | Real CC0 texture sets |
| U5 | **Windows** | Opaque dark mirrors with nothing behind them | Real glass (`DESERT_GLASS.md`) |
| U6 | **Shop insides** | Evenly lit, nearly empty boxes (`Town_ShopInsideLit`, `Town_ShopInterior` FAKE) | Section 3 |
| U7 | **Furniture made of boxes** | Beds, kitchen counters, cabinets (`Town_CabinetWhite` 100 % plain boxes), majlis mattresses (97 %), wardrobes | Real models (Poly Haven has many), or Blender kitbashes |
| U8 | **The technical** | Boxes and Unity cylinders | `DESERT_VEHICLES.md` V3 |
| U9 | **The same car everywhere** | One sedan model for all traffic, nobody in it | `DESERT_VEHICLES.md` V1, V2 |
| U10 | **The same four men** | Four male models, the only people in the town | `DESERT_PEOPLE.md` section 3 |
| U11 | **Too clean** | No litter, no stains on the pavements, no oil under parked cars, no sand drifted against walls, no water streaks under sills, no faded paint; `Town_WeatherDark` strips are a uniform band | Decals and the grime shader (realism step 5) |
| U12 | **The same building heights and boxes** | Masses are rectangles with flat roofs, stacked floors of one height | Setbacks, roof structures (stair heads, water tanks, rebar of unfinished floors), different storey heights, balconies |
| U13 | **Parapets and roofs** | Every roof flat and empty, a parapet of one height | Clutter on roofs (tanks, dishes, washing), broken parapets, roof access (section 1) |
| U14 | **The sky** | A clear, soft 4K sky (11 px/degree), a 43-degree sun | `DESERT_LIGHTING.md` part 2 |
| U15 | **Distant mountains and the apron** | One material each, no detail at the town's edge | Layered ridges, haze by height |
| U16 | **Shutters** | Flat boxes (`Town_ShutterBox` FAKE, 72 km of sharp edge) | Corrugated profile, dents, graffiti, rust at the foot |
| U17 | **Lamps and signals** | Box lamp heads, signal bodies 44 % plain boxes | Real models |
| U18 | **Placeholders that show** | The grenade pickup (a capsule), the health and shield pickups (a sphere, a cube), objective beacons (a glowing column) | Real pickups (a grenade, a med kit); beacons only on the HUD and map, or a smoke flare |
| U19 | **A blank white sign on a post** (the 200 m street view) | A sign with nothing on it | Give it a sign from the atlas |
| U20 | **The white fallback material** | 98 building pieces drawn in pure white (`Buildings: Universal Render Pipeline/Lit`) | Review E1 |
| U21 | **Interiors all lit the same** | Reflections from one painted cubemap | `DESERT_LIGHTING.md` part 1 |
| U22 | **Nobody reacts to anything but gunfire** | People, cars and animals ignore the player otherwise | `DESERT_PEOPLE.md` section 3 |
| U23 | **No animals** | No dogs, cats, goats, birds in a desert town | A few dogs and cats in the old town, pigeons on roofs, goats at the farms |
| U24 | **No sound of a town** | No traffic hum, voices, call to prayer, radios, generators | An ambience by district, sounds on things (generators, aircon units, TVs) |

## Order

1. Roof access (section 1): a bug, and it changes how every level plays.
1a. Numbers, street names and the map (section 7): they make every mission findable.
2. People who stay (section 2.1) and spawning nearer (2.2).
3. Shops filled and closed up (section 3).
4. Cars' driving fixes (review B1-B8), then the rest with the driving feature.
5. Realism steps for objects and houses, with the glass.
