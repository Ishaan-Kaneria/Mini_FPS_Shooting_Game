# Real glass: design and work plan (work in progress)

2026-11-04. **Status: not started.** The developer's rule: every window is **real glass**. You see through it, it
reflects, the sun comes through it, and it breaks. No painted fake rooms. This document is the plan, written from the
code (no Unity in the session that wrote it). Each stage ends with his play-test. Mark each line's status as the work goes.
Related: `Docs/DESERT_LIGHTING.md` (light and reflections), `Docs/DESERT_REVIEW.md` A9, and the handoff's queued
feature "Glass, shutters, cars, driving" (its Stage 3 is this document).

## 1. What there is today

| Where | How it is built | What it does |
|---|---|---|
| House windows, outside (`FPSKitTownBuildings.cs:812-835`, `FPSKitTownFacade.cs:247-260`) | One quad per window in the building's merged chunk mesh. about 80 % `Town_GlassDark`/`Town_GlassBlue` (**opaque**, metallic 0.85, smoothness 0.96), 12-14 % `Town_GlassCurtain`, 8 % `Town_GlassBlinds` (curtains and blinds drawn *on* the pane) | A dark mirror. Nothing behind it is visible; the audit rates both glass materials FAKE ("renders as a black hole") |
| Old houses (`b.oldStyle`) | Timber shutters, 70 % shut; the rest open on `Town_GlassDark` | Shutters fine; the open ones are the same dark mirror |
| Enterable houses, ground floor | Open and barred, no glass | Correct as a choice |
| Enterable houses, upper floors (`FPSKitTownInterior.cs:525-533`) | A second pane on the inside, `Town_ShopGlass` (transparent), 4 cm behind the opaque outer quad | From inside you see out (the outer quad is one-sided); **from outside you cannot see in**, though the room is really there |
| Shops (`FPSKitTownShopfront.cs:116-127, 328-333`) | `Town_ShopGlass`: alpha-blended, alpha 0.34, smoothness 0.94, metallic 0 | Nearly real: you see the lit shop through it |
| Cars | The car pack's own glass materials | Not reviewed |
| Breaking | Nothing. `IWorldHit` (`Runtime/WorldHit.cs`) exists for "glass breaks" and `ImpactSurface.Glass` exists in `FPSImpactManager`; no pane answers either | Bullets stop at windows as if they were walls |

## 2. What "real glass" means here (the acceptance list)

1. **You see through it.** What is behind it is real geometry: a real room where there is one, and where the building
   is a solid mass, a real shallow room built behind the window (section 4).
2. **It reflects as glass does.** Weak face-on (about 4 %), strong at grazing angles (Fresnel), the street and the sky
   in it, a hard sun glint.
3. **Light passes.** The sun and the bake's light go through it into the room. Glass casts no shadow and blocks no
   bounce.
4. **It is dirty, as desert glass is.** Dust thicker at the bottom and in the corners, streaks, smudges. This is what
   sells it more than anything else.
5. **It breaks** by its kind of glass (section 6): a bullet makes a hole and goes through (slowed), a punch cracks it, a
   kick or more damage shatters it, a blast blows it in. Enemies hear it.
6. **It costs little.** The town has thousands of windows: one draw for all intact panes, nothing per pane until one is hit.
   No frame over 33 ms when glass breaks (the handoff's rule).

## 3. The glass itself (look)

**Status: not started.**

* **One glass shader for the town**, `FPSKit/Glass` (Shader Graph, URP, transparent, premultiplied so the reflection
  keeps full strength while the body fades), used by windows, shop glass and doors:
  * base: very dark tint (linear 0.01-0.03, slightly green for float glass), alpha 0.08-0.15 face-on;
  * reflection: the reflection probe, Fresnel (Schlick, F0 0.04), smoothness 0.95-0.98; the sun's specular glint from
    the main light;
  * **dust**: a tiling dust mask (CC0, ambientCG "smudges/dirt" sets) plus a gradient up from the sill, lowering the
    alpha's transparency (dust scatters light: more opaque, lighter), raising roughness where it lies; per-window
    random offset and amount from a hash of the pane's position;
  * streaks: a vertical streak mask under the top rail;
  * High only: a small refraction offset through `_CameraOpaqueTexture` (1-2 px), which makes it read as a thickness.
* **Two-sided:** one pane, drawn from both sides with the normal flipped on the back (no more outer and inner quads).
* **No shadows, no GI blocking:** glass renderers `shadowCastingMode = Off`, not Contribute GI, so the sun's real-time
  light and the district bakes go through it. Today the opaque outer quads cast shadow and block the bake: check what
  the rooms behind windows look like after this (they will get more light).
* **Reflections to reflect something:** street reflection probes at window height (about 4 m), box projected along
  each street segment, so a pane shows the facade opposite, not the ground. URP has no screen-space reflections; if
  High wants more, a planar reflection only for the one large shop window nearest the player is affordable, nothing more.
* **Its own layer, `Glass`**, added in `FPSKitSceneBuilder.EnsureProjectTagsAndLayers` (the only place layers are made).
  Every ray that means "can light or sight get through" leaves `Glass` out: enemy sight (`EnemyAI`, `EnemyBrain`),
  `EyeAdaptation`'s roof rays, `FirstPersonShade`'s sun rays, `IndoorReflections`' room casts in `FPSKitTownGI`,
  `FPSKitShadowLeak`. Every ray that means "a round hits it" keeps it (section 6).
* **Sorting:** transparent panes in one merged mesh cannot sort among themselves; with ZWrite off and a dark, thin
  base the error is invisible on facades. Smoke, dust and blood particles behind glass: check them.

**Check:** the audit's light test for the glass passes; `ViewProbe` street views show the facade opposite in the glass
at a grazing angle and the room through it face-on; a room's brightness behind a window rises (no more blocked bake),
re-measure it in the lighting tour.

## 4. What is behind the glass

**Status: not started.**

* **Enterable rooms:** already real. Remove the opaque outer quad; the one two-sided pane sits at the glass plane.
* **Shops:** already real (lit or dark insides behind `Town_ShopGlass`). Move them to `FPSKit/Glass`.
* **Solid buildings** (the masses you cannot enter): today a window has nothing behind it but the mass. Real glass needs
  real space behind it. Build a **window room** behind each window, in the builder:
  * a box 1.5-3 m deep (the wall's own thickness plus the room), as wide as the window plus 0.6 m each side, floor to
    ceiling of that storey; inside faces only (seen through the glass), with the house's interior paint, a floor, a
    ceiling, skirting;
  * dressing by a draw from the building's **own** random stream (never `b.rng`: the town must not reshuffle; see
    CLAUDE.md "Walls varied"): curtains (real cloth geometry, half drawn, a few folds), venetian blinds (real slats at an
    angle), a net curtain (a second, translucent layer), or an open room with one or two pieces of furniture from
    `FPSKitTownFurniture` (a wardrobe, a chair, a lamp) against the back wall;
  * lighting: in the district bake (they are inside faces of a house, 20 cm texels, like the rest) so the window rooms
    are lit by the light through their own window; out of navmesh (inside the mass's Not Walkable volume, which must
    now stop at the window room's walls, not cover it);
  * cost: these are `Detail_` meshes per chunk (no collider, out of the navmesh bake, culled past about 180 m like the
    facade detail). Past the cull distance the glass shows only the reflection and its dark base, which at that distance
    is what real glass shows.
* **Old houses' open shutters:** the same, a window room behind.
* **Upper storeys over shops:** the same.

**Check:** `VerifyTown` (no navmesh in the window rooms, room counts unchanged); memory with `FPSKitTownTest.Memory`;
`FrameCostProbe` on Medium (transparent cost was 2.1 ms: measure what it becomes).

## 5. Rendering many panes cheaply

**Status: not started.**

* Intact panes are drawn together: either kept in the chunk meshes (glass submesh per chunk, as today, cheapest to
  build) or, better for breaking, as **instances** of one quad mesh drawn by a `GlassField` component with
  `Graphics.RenderMeshInstanced` (per-instance matrix, size, dust seed, state), one draw call per few thousand panes,
  culled per 64 m chunk.
* The builder writes the pane list (centre, size, normal, kind, which building, which room behind) into the scene as
  data (a `GlassField` asset or serialized arrays, like `RoomContents`).
* Each pane also gets a thin `BoxCollider` (2 cm) on the `Glass` layer, so rounds and blows can find it. Static, so it
  costs nothing until hit. Grouped under one object per chunk.

## 6. Breaking

**Status: not started.** The handoff's Stage 3 asked for exactly this; the hooks exist.

### Kinds of glass

| Kind | Where | Bullet | Punch | Kick / heavy blow | Blast | Shatter pattern |
|---|---|---|---|---|---|---|
| Annealed (ordinary window) | houses, upper storeys | hole + radial cracks, round passes slowed | small crack star | shatters (second kick, or once cracked) | shatters, blown in | 10-30 long sharp shards, denser at the hit; some stay in the frame |
| Tempered (safety) | shop doors, car side and rear windows | the first hit shatters it whole | crack, then shatters | shatters | shatters | thousands of small cubes (particles), crumbles out of the frame |
| Laminated | car windscreens | hole with a spider web, stays in the frame | web | web, sags | web, sags, may fall out whole | does not fall apart |
| Display (thick annealed) | shopfronts | hole + long cracks; three hits or a heavy round shatter | crack | crack, then shatters | shatters | large heavy shards, fall straight down |

### How each hit reaches the glass

* **Rounds.** Today `Weapon.FireRay` stops at the first hit (`Weapon.cs:593`). Make it go on through glass: raycast; if
  the hit is on `Glass`, report the hit to the pane, spawn the hole and the glass impact, and cast again from just past
  it with the damage times 0.8 and a 1-2 degree deflection, at most two panes; else as now. Same in `EnemyAI` (enemies
  shoot the player through windows, `EnemyAI.cs:1197`) and `TechnicalTruck` (`TechnicalTruck.cs:144`). The impact uses
  `FPSImpactManager`'s `ImpactSurface.Glass` (pooled marks, never one object per shot).
* **Blows.** `MeleeStrike.HitWorld` already sends kicks and punches to `IWorldHit`: the pane component answers.
* **Blasts.** `Explosion.Blast`: an `OverlapSphere` on `Glass` within 1.5x the blast radius: shatter, shards thrown away
  from the centre.
* **Cars.** A car hitting a pane (B9 in the review): shatter.

### The pane's state

`GlassPane` (runtime, created only when a pane is first hit: the instance is removed from `GlassField` and a live pane
takes its place with its own small mesh):
* damage accumulates by kind of blow; states only get worse: Intact, Cracked, Holed, Shattered (the same never-improve
  rule as limbs);
* cracks and holes: decals on the pane (a small atlas of crack stars, webs and holes, both faces), at most about 8 a pane,
  then it shatters;
* **shatter:** a 2D Voronoi of the pane's rectangle, seeded densest at the hit, **pre-computed at build time** for a few
  patterns per pane shape (so breaking costs no fracturing at run time), extruded to 6 mm; shards are pooled rigidbodies
  (at most about 150 live in the level, the oldest sleeping shards removed out of view; no collision with the player's
  controller; small ones as particles). Cells touching the frame stay in it with a chance, leaving the jagged edge real
  broken windows have;
* tempered: no shards; a burst of glass-cube particles and a fall of cubes to the sill and the ground;
* laminated: a spider-web decal that grows, the pane sags a few centimetres; it never comes apart;
* sound: a crack, a shatter and a tinkle of falling pieces, by kind (the placeholder generator or CC0 recordings,
  logged in `LICENSES.md`); `NoiseSystem.Emit` with a new `NoiseKind.Glass` (enemies come to look, civilians are
  alarmed, as for gunfire);
* gameplay: an opened window is a hole to shoot through (sight and rounds pass with no glass). It is not a way in: the
  navmesh does not change.

**Check (a new `VerifyGlass`):** a round through a pane hits the man behind it with 0.8 of its damage; a punch cracks, a
kick after it shatters; a blast clears every pane in range; 200 shards and the frame stays under 33 ms (`Time.captureDeltaTime
= 1/60`, frame time measured); a broken pane stays broken; enemy sight through intact glass is unchanged; the navmesh is
unchanged. It must fail with the glass rule deleted (CLAUDE.md "Verifying").

## 7. Order of work

| Stage | What | Status |
|---|---|---|
| G0 | Count windows by type (enterable, solid, shop, old shutter) and what is behind each; measure transparent cost today | not started |
| G1 | `FPSKit/Glass` shader and material, `Glass` layer, shadows and GI off on glass, the rays that must ignore it; one two-sided pane | not started |
| G2 | Glass on enterable rooms and shops (no new geometry): see in from the street | not started |
| G3 | Window rooms behind the solid buildings' windows, dressed, in the district bake | not started |
| G4 | Street reflection probes at window height | not started |
| G5 | `GlassField` instanced drawing, pane colliders | not started |
| G6 | Breaking: rounds through glass, blows, blasts, states, decals, shards, sounds, noise | not started |
| G7 | Cars' glass by kind (tempered side/rear, laminated windscreen), with the driving feature | not started |
| G8 | `VerifyGlass`, the frame-time test, his play-test | not started |

After each stage: rebuild the desert, the lighting tour, `FrameCostProbe` on Medium, his look.

## 8. Open questions for the developer

1. Window rooms behind solid buildings: one or two pieces of furniture, or curtains and blinds only (cheaper)?
2. May the player climb in through a window once it is broken, or does it stay a hole to shoot through (simpler: no
   navmesh change)?
3. Bullets through glass: 0.8 of the damage and a slight deflection, or no loss?
4. Shards: may they cut (small damage walking through a broken shopfront), or are they only seen?
