# Explosives, grenades and the gas station

2026-11-04. Part 1 is in code now (not compiled, built or played: no Unity in the session that wrote it). Parts 2-4
are the plan. His brief: "add an explosive; rebuild the gas station with the real thing using Blender; add barrels;
petrol cans blast when I fire; there is no explosive and no grenades."

## 1. In code now

### Things that burn and blow up

| Kind | What the town already had | Health | What a round does | Burst | Fire left |
|---|---|---|---|---|---|
| Petrol drum (painted red) | 35 % of the steel drums (`Barrel_01`, `barrel_03`), chosen by a hash of each drum's place | 45 | leaks; each round has a 35 % chance to light it; at 0 health it lights | after burning 2-4.5 s: 170 damage, 6.5 m | burning pool, 10 s, 1.8 m |
| Diesel drum | 20 % of the steel drums | 70 | leaks; 3 % chance to light (diesel does not take a spark) | only after 8-14 s of burning: 140, 5.5 m | 14 s |
| Jerry can | every `plastic_jerrycan` | 12 | lights at once | at once: 80, 3.5 m | 6 s |
| Gas cylinder | every `small_lpg_tank` (the cage at the gas station) | 60 | vents and lights (60 %) | after 3-6 s: 200, 7 m | none |
| Propane tank | every `propane_tank` | 90 | vents and lights (60 %) | after 4-7 s: 260, 8.5 m | none |
| Fuel pump | the gas station's six pumps (a box round each) | 80 | 50 % chance to light | never bursts | sprays burning fuel 25 s, 1.4 m |
| Other steel drums | 45 % of them | | hold water or nothing: inert | | |

* **Chains:** a blast that reaches another explosive sets it off a moment later (cylinders at once); a fire lights any
  explosive standing in it. Shoot one drum by the cylinder cage and the cage goes.
* **Who it credits:** the blast and the fire carry whoever damaged the prop last. Enemies' rounds set barrels off too.
* **What it uses:** the frag's own blast (`Explosion.Blast`), its fireball prefab and its sounds; the Fairground's flame
  material (`FX_Flame`) and the Warehouse's smoke material for the fire; a flickering warm point light.
* **Files:** `Runtime/ExplosiveProp.cs`, `Runtime/FuelFire.cs` (runtime); `Editor/FPSKitTownExplosives.cs` (arms the
  town's props at the end of the desert build, `FPSKitTown.cs`); `FPSKitTownEdge.BuildGasStation` records the pumps.
  Shares and numbers are constants at the top of `FPSKitTownExplosives.cs` and in its `Arm()` switch.

### Grenades

The game has three bombs (Frag, Cluster, Thermite), but none is owned at the start: the Frag is the story's reward for
the Warehouse boss (`FPSKitCampaign`, `grantsPower`), and the other two are bought in the store. Playing the desert
first, with zones unlocked, leaves the player with none. That is why there were no grenades.
* **Now:** enemies in the desert drop grenades (`LevelTheme.grenadeDropChance`, 0.12 in the Desert theme, 0 elsewhere,
  so the story's first power is untouched in the arenas before it). Picked up (`Pickup`, `Kind.Grenades`), it adds one
  charge; a player with no bomb is lent frags for that level only (`BombThrower.AddCharges`): nothing is bought or owned.
* **Files:** `Runtime/Pickup.cs`, `Runtime/BombThrower.cs`, `Runtime/LevelManager.cs` (drop roll after health and
  shield), `Runtime/LevelTheme.cs`, `Editor/FPSKitThemes.cs` (Desert), `Editor/FPSKitSceneBuilder.cs` (the
  `Pickup_Grenade` prefab: a placeholder olive capsule for now).

### To do on the laptop

1. Compile. Then `FPSKitBatch.ResetThemes` (the new theme values), rebuild the desert.
2. Read the build log's `[FPSKit] explosives:` line (how many of each).
3. Play: shoot a red drum, a jerry can, the gas cage, a pump; throw a picked-up grenade into a group of drums.
4. Checks: `VerifyCombat`, `VerifyBomb`, `VerifyStatics`, `VerifyTown`, frame time during a chain (no frame over 33 ms).
5. Write `VerifyExplosives` (the shape of `VerifyBomb`): a round lights a jerry can, a jerry can's blast sets off a petrol
   drum within 3 m, a diesel drum survives three rounds, a pump never bursts, the player is credited with a civilian
   killed by a drum they shot. It must fail with the rule deleted.

Known gaps in this first version: no scorch decal after a burst; no burnt wreck left; the pickup is a capsule (use the
frag's own model); no dents or bullet holes on drums (use `FPSImpactManager`'s metal marks, which already land); fire
damage ignores cover.

## 2. The gas station, rebuilt for real

**Now** (`FPSKitTownEdge.BuildGasStation`): a slab, a canopy on six square posts, three islands with two pumps each.
The pumps are white boxes with a green band, a dark "screen" box and a stick for a hose. There is a fascia, a price
pylon, a shop (a town mass), a cylinder cage, tyres and two drums. It reads as a blockout.

**What a real one has** (use reference photos of a Middle Eastern or North African roadside station, not a brand):
* canopy: steel deck with a fascia band, recessed lights underneath (emissive, 4,000 K), drainage downpipes in the
  columns, round or clad columns with yellow-black crash bollards;
* islands: raised concrete forms with rounded noses, steel kerb guards; on each, a **dispenser**: a cabinet about
  2.2 m tall, 1.2 m wide, 0.5 m deep, with two sides, a lit display and a price window on each side, nozzle boots,
  rubber hoses on retractors, a breakaway coupling, a column for the hose mast on high-flow pumps; a bin, a squeegee
  bucket, a fire extinguisher on a stand, an emergency stop button on a post;
* the forecourt: concrete slabs with sawn joints, oil and fuel stains, a perimeter drain with a grate, painted arrows,
  underground tank fill points (round covers with coloured caps in a row), vent pipes up the back wall;
* the shop: glazed front, air conditioners, an ice chest, a gas cylinder cage (already there), a water and air point;
* signs: the price pylon with real changeable digits, the canopy fascia, no-smoking and engine-off signs (real Arabic,
  as `Tools/town/make_signs.py` already makes).

**How to build it**

1. **Models in Blender** (headless, the same way the face damage keys are made: `Tools/blender/*.py`):
   `Tools/blender/gas_station.py` builds the parts procedurally with bmesh: the dispenser cabinet (bevelled edges 3-8 mm,
   panel seams as inset faces, the display window recessed, the nozzle boots), nozzles (a curved spout, a lever, a
   rubber grip), hoses (a curve with a bevel profile, sagging between retractor and nozzle), the column cladding, the
   bollards, the crash guard, the canopy deck's light fittings, the island form. One object per part; nozzles and hoses
   separate (they are thrown by a burst). Materials named by kind so the realism audit can judge them
   (`_PaintedMetal`, `_Plastic`, `_Rubber`, `_Glass`, `_BrushedSteel`, `_Concrete`). UVs at world-metre scale; CC0
   textures (ambientCG, Poly Haven) for paint, rubber, brushed steel, scuffs. Export FBX with LOD0 (about 6-10k
   triangles a dispenser), LOD1 and LOD2.
2. **Or a model** with a commercial-use licence (Sketchfab CC0/CC-BY, the Asset Store), checked against the realism
   bar (shape, surface and light all pass) and logged in `LICENSES.md`. Blender is still the place to split it into
   parts and rename materials.
3. **Into the builder:** `BuildGasStation` places the prefabs where the boxes are now (the island positions stay, so
   cars parked at the bays, `_gasPumps`, still fit), keeps `NoEntryBox` over the canopy and `_townSolids` round the
   islands, and gives each dispenser its `ExplosiveProp` (Fuel Pump) on the model's own collider instead of the
   stand-in box.
4. **What burns:** a dispenser shot through sprays burning fuel (as now); a burst nearby throws its nozzles and hoses.
   For a set piece: a **fuel tanker** parked at the fill points (a large `ExplosiveProp`, petrol, 400 damage, 14 m, a
   slow fire first) and the underground tanks' vent pipes jetting flame when the forecourt burns. The canopy lights go
   out when a blast reaches them.
5. **Check:** the realism audit's evidence for the station's parts (all three pass), `VerifyReach` (the islands and the
   shop door reachable), `VerifyTown`, frame time at the station.

## 3. Barrels and cans, for real

The Poly Haven drums (`Barrel_01`, `barrel_03`) and the plastic jerry can stand in now. The real set:
* **200 litre steel drum** (Blender): two rolling hoops, the chimes (rolled rims) top and bottom, two bung caps on the
  lid, 3-5 dent variants (shape keys, chosen per drum), paint masks for chips and rust at the chimes; colour variants
  red (petrol), blue and green (chemical, water: inert), black (oil), each with a printed label; open-topped variants
  as burn barrels.
* **Steel jerry can** (the 20 litre pattern with the three handles and the X pressing): petrol, red or olive.
* **Plastic jerry cans** in red (petrol), yellow (diesel), blue (water: inert).
* **Gas cylinders:** the cage's small LPG bottles (already there) and 45 kg cylinders by kitchens and shops, with
  regulators and hoses.
* After a burst: a torn, blackened drum (a separate model) left where it stood, and a scorch decal.
* Placement: by the garage, the junkyard, the gas station's back yard, behind shops, on building sites (half of the
  town's cover drums stay inert, so cover is still cover).

## 4. Later

* Enemies throw grenades (enemy AI Stage 5: squad tactics), and shoot at drums the player hides behind.
* Cooking a grenade (hold before the throw: a shorter fuse); a smoke grenade; a molotov (a bottle, a fire like a petrol pool).
* Cars as explosives (the queued "car damage" stage: fuel leak trail, engine fire, then the blast).
* Shutters and glass breaking from blasts (`Docs/DESERT_GLASS.md`).
