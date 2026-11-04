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

## Order

1. Roof access (section 1): a bug, and it changes how every level plays.
2. People who stay (section 2.1) and spawning nearer (2.2).
3. Shops filled and closed up (section 3).
4. Cars' driving fixes (review B1-B8), then the rest with the driving feature.
5. Realism steps for objects and houses, with the glass.
