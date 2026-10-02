# Abandoned Fairground: the Flooded Grounds look recipe

Measured from `Assets/Flooded_Grounds/Scenes/Scene_A.unity` with
`Tools/MiniFPS/Fairground/Dump Flooded Grounds Look` (raw output: `Logs/flooded-grounds-dump.txt`,
not committed). Every number below is read from the scene, not estimated. The demo is a **daytime
overcast** scene; the "Night" column is how we adapt it in Step 4.

## The short version

It looks damp and realistic because of **low contrast + cool grey light + fog tinted to the sky +
very glossy wet surfaces + a lot of repeated, mossy, dirty modular props**, not because of any
expensive technique. There is no baked GI, no probes, no reflection probes, no bloom, no AA, no
depth of field. It is cheap, and that is good news for mobile/WebGL.

## Lighting

| Item | Flooded Grounds (day) | Fairground (night) |
|---|---|---|
| Sun | 1 directional, colour (0.965, 0.980, 1.0) cold white, intensity 1.0, **soft shadows**, euler (25, 85, 0): a **low** sun, long shadows | Moon: same direction idea, colour ~(0.45, 0.55, 0.75), intensity ~0.15 to 0.25 |
| Shadow range | 40 m, 2 cascades (QualitySettings) | Keep 40 to 60 m, 2 cascades |
| Ambient | Mode Skybox, intensity 0.6. Gradient values: sky (0.368, 0.383, 0.400), equator (0.236, 0.263, 0.282), ground (0.094, 0.083, 0.068). Flat grey-blue, ground nearly black-brown | Same hue, intensity ~0.15 to 0.25 |
| Fill ("bounce") | **15 shadowless point lights** named `Pointlight_Bounce1`: range 10 to 20 m, intensity 0.5 to 0.8, colours cool greys (0.667, 0.718, 0.753), a few tinted (warm grey, lilac, pink-grey, one dark red-brown (0.263, 0.149, 0.176)). Hand-placed fake GI | Replace with practical lights (work lights, bulbs, barrels) |
| Accent | **One** orange point light (1.0, 0.6, 0.0), intensity 1.5, range 7, hard shadows | A handful, each a visible source |
| GI | None baked, none realtime, 0 lightmaps, 0 light probes, **0 reflection probes** | We bake (APV) because we have dark interiors |

## Sky, fog, reflections

- **Sky:** rotating cubemap, exposure **0.5** (deliberately dim), tint white, rotation 10 deg/s.
  Cloud-grey, no visible sun disc. We do not rebuild this shader; we use the Poly Haven night sky.
- **Fog:** **Linear, 0 to 400 m**, colour **(0.492, 0.567, 0.612)** = cool blue-grey teal, close to the
  sky/horizon colour so the distance dissolves into it. Camera far plane 1000. (The density value 0.003
  is ignored in Linear mode.) Post-process fog is on with `excludeSkybox`.
- **Reflections:** Custom cubemap `BGR_RefCube1`, intensity 0.6, 1 bounce. Everything glossy reflects the
  same grey sky, which is what makes wet surfaces read as wet.

## Colour grade (Post Processing v1 profile `Postprocess_FloodedGrounds`)

| Effect | State | Values |
|---|---|---|
| Tonemapping | on | **Neutral** (black in 0.02, white in 10, white level 5.3, white clip 10) |
| Post exposure | on | +1.0 EV |
| Temperature / tint | on | **-3 / 0** (slightly cool) |
| Saturation | on | **0.8** (desaturated) |
| Contrast | on | **0.9** (lowered: the milky, damp look) |
| Curves, channel mixer, colour wheels | identity | none |
| Ambient occlusion | on | intensity 1.0, radius 0.3, 3 samples, downsampled |
| Vignette | on | intensity 0.45, smoothness 0.2, roundness 0.888, black |
| Film grain | on | coloured, intensity 0.2, size 1.0, luminance contribution 0.8 |
| Bloom, AA, SSR, DoF, motion blur, eye adaptation, chromatic aberration | **off** | none |

URP translation (Step 2c): ACES vs Neutral is your call. Neutral exists in URP (Tonemapping: Neutral);
Step 4 asks for ACES, so the Volume will carry both and we compare.

## Terrain (1024 x 1024 m, height 32 m)

- **Only 3 ground layers**: Moss and Dirt (10 m tile), Asphalt (20 m tile). The realism is in the
  *blend*, not the layer count. No smoothness or metallic on any layer; normal scale 1.
- Terrain material is already URP Terrain/Lit (Unity auto-upgraded it; see `_TerrainAutoUpgrade`).
- Base map distance 200 m, **detail distance 60 m, detail density 0.4**, tree distance 500 m,
  **billboards from 130 m**, 30 m cross-fade, up to 1500 full-mesh trees.
- **Trees: 1276 instances, 9 prototypes.** Heavy on small ones: 484 Small_B + 353 Bush_A + 142 Tall_A +
  122 Tall_C, plus 47 dead trees. Tree Creator, so they do **not** render in URP (see below).
- **Grass: 9 mesh prototypes** (not billboards), scale 0.1 to 1.5. One prototype, `Grass_Small_C`,
  carries ~1.25 M of the ~1.4 M detail cells, with a **dark grey dry colour (0.196)** and a wide noise
  spread (0.5); the other 8 are accents (tall tufts, medium clumps). So the ground cover is a dense, dark,
  uneven low layer with a few tall clumps, not a lawn.

## Water

- **One opaque plane**, 1280 x 1280 m at y = 16 (terrain peaks at 32), no transparency, no refraction,
  no depth fade. It is cheap.
- Shader `PBR_Water`: tint **(0.786, 0.791, 0.626)** over an ocean diffuse, **smoothness 0.95**,
  metallic 0, emission 0.05. Two scrolling normal maps (speed 0.4, the second runs the other way) plus a
  static third, a parallax height map (0.031), and a sine vertex wobble (**height 0.25, frequency 20**).
- It reads as water through gloss + reflection of the grey sky + moving normals. For the fairground we
  go darker and murkier and add depth darkening and soft shoreline, which the original lacks.

## Props and decay

- **8288 renderers, 2025 LOD groups, 77 audio sources, 45 particle systems.** Dense and modular.
- Most-repeated: flood walls (546 + 93), fences (459 + 252 + 234 + 57), rocks (276 + 255 + 6 cobble types),
  chairs (232), poles (212), park benches (192), wood paths (159), bridges, docks, lamps (~190).
  **Variety comes from many prop types and rocks, not from unique art.**
- Materials: 6760 `Standard` + **1496 `PBR_TopBlend`** renderers. TopBlend lays moss/dirt on every
  **up-facing surface** (world-normal blend with a breakup mask and a detail normal), so cars, ships,
  rocks, bridges and cabins all look weathered and mossy on top. This is the single biggest
  "decay" trick, and we reproduce it for rust/mud/leaves.
- Particles: falling leaves (rate 3 to 20, 80 to 500 max, 5 s life) and interior dust (rate 20,
  size 0.1) are the only atmosphere. No rain, no mist cards.
- Audio: 77 sources of 8 clips (wind, wind howl, leaf rustle, water, taps, deep rattle, horn, background).

## What breaks in URP (verified from the dump)

| Item | Count | Fix |
|---|---|---|
| `Standard` materials | 32 | Render Pipeline Converter / copy to URP Lit |
| `Flooded_Grounds/PBR_TopBlend` | 8 materials, 1496 renderers | `FG_TopBlend_URP` |
| `Flooded_Grounds/PBR_Water` | 1 | `FG_Water_URP` |
| `Flooded_Grounds/Triplanar_BumpSpec` | 1 (the bush) | `FG_Triplanar_URP` |
| `Flooded_Grounds/Skybox_Rotating` | 1 | not rebuilt; Poly Haven HDRI |
| Tree Creator Bark / Leaves | 9 + 5 materials | URP Lit copies, leaves alpha-clipped, two-sided |
| Legacy particle shaders | 3 | URP Particles Unlit |
| Post Processing v1 | whole stack | URP Volume |
