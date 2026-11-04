# Desert Outpost: lighting, fixed and made beautiful

2026-11-04. Companion to `Docs/DESERT_REVIEW.md` (issue IDs A1-A12 are from there). Part 1 is what is wrong and
how to fix it; part 2 is what makes the light look good, as settings with numbers. Written from the code without
Unity: everything here needs building and looking at on the laptop. One change at a time, look, keep or revert.

## What changed in code with this document (not yet built or baked)

| Change | File | Takes effect | Turn it off |
|---|---|---|---|
| Medium gets its own Forward+ renderer (`Medium_Renderer.asset`, a copy of Low's), so its indoor reflection probes work (A2) | `Editor/FPSKitQualityTiers.cs` (`MediumForwardPlus`) | next `FPSKitQualityTiers.Ensure()` (runs in the desert build and bake); commit the new asset and its `.meta` | `MediumForwardPlus = false` |
| One box-projected reflection probe per room instead of one per house (A4); shops box projected | `Editor/FPSKitTownGI.cs` (`PerRoomProbes`) | next desert build (`PlaceIndoorReflections`) | `PerRoomProbes = false` |
| Grade controls: white balance, split toning (neutral by default: nothing changes until set) | `Runtime/LevelTheme.cs`, `Editor/FPSKitSceneBuilder.cs` (`CreateVolumeProfile`) | set values in `FPSKitThemes` (Desert case), `FPSKitBatch.ResetThemes`, rebuild | leave the values neutral |
| Cloud shadows: a drifting cookie on the sun, made in play only (bakes never see it), High and Medium only | `Runtime/CloudShadows.cs`, attached by `BuildLighting` when `LevelTheme.cloudShadows` | `cloudShadows = true` in the theme, reset themes, rebuild | `cloudShadows = false` (default) |

Checks after building: `VerifyTown`, `VerifyStatics`, the lighting tour, and `FrameCostProbe` on Medium (Forward+
costs a little; it should be under 1 ms on the Arc at this light count. If it is more, say so before keeping it).

**A correction to the review:** A9 said the window glass is wrong because it is metallic 0.85. The code's comment
(`FPSKitTownBuildings.cs:117-120`) shows that was a deliberate choice: as a plain dark dielectric the windows read as
flat navy cards. The black-hole look is more likely what the glass reflects (a dim or sky-blind probe) and that
nothing is behind it. The fix is in part 2, "Windows".

---

## Part 1: the faults, in the order to fix them

Each one removes a reason for a patch, so do them in order and remove the patch when its reason is gone.

1. **Reflections (A2, A4 done in code; A3 next).** Capture each room's own reflection after its district's lightmap
   bake, with the pilot's code (`FPSKitTownLightmaps.CaptureReflections`: `FindRoom` floods the room, the probe is put
   at its centroid + 1.5 m, captured box-projected at full strength, sun off). Run it over every `RoomProbe_*` and
   `ShopProbe_*` per district, 64 px, BC6H. Then delete `RoomCube()` and the constant strengths (`HouseReflection`,
   `ShopLitReflection`, `ShopDarkReflection`) and the " dark" suffix rule (A5). **Done when:** no milky sheen on the
   walls in any room view (wall brightness with reflections on minus off, under 3 of 255) on High *and* Medium.
2. **The band on probe-lit things (A1).** Turn `ProbeLayers` back on (`FPSKitTownGI.cs`), add indoor probes at 1.5 m
   per room (not per inside's bounds: that blew the 4 M limit), and give moving things a runtime in/out rendering
   layer (under a roof: `IndoorLayer`). **Done when:** a soldier, a door leaf and the player's legs are evenly lit from
   floor to head in a one-storey room (BatchBand, upper/middle within 15 %).
3. **Remove the patches.** With 1 and 2 in, the probe-lit things and the lightmapped walls come from the same bake:
   * set `EyeAdaptation.probeStops` to the eye's maximum (no dimming) and, if the soldier test passes, delete the
     `ProbeVolumesOptions` multiplier (A7);
   * light the arms like everyone else: delete `FirstPersonShade`'s sun rotation and room-light stand-in, keep its
     shade test only (A6);
   * lightmap static door frames, skirting and fixed props (A8).
   **Done when:** gloves and gun indoors are dark and matte, the same brightness as a soldier's sleeve next to them.
4. **Low, phones, web (A10).** A few classic probes inside each room for the reduced copy, and the eye's roof test
   with a 2-stop cap.

---

## Part 2: what makes it look good

What separates a convincing desert town from a flat one is mostly six things, in order of how much they give for
what they cost: **the time of day, warm light against cool shade, soft shadows with contact darkening, atmosphere,
light you can see (shafts, dust, glints), and a restrained grade.** Everything below is per tier and costed for the
Intel Arc laptop.

### 1. Time of day and the sky: the biggest single change

* **Now:** `kloofendal_43d_clear_puresky` (Poly Haven), the sun taken from it (`AimTownSun`) at 43 degrees: a
  late-morning sun in a cloudless sky. High sun means short shadows, flat walls, little colour difference
  between sun and shade. It is the least flattering light there is.
* **Better: late afternoon, sun at 20-30 degrees, a few clouds.** Long shadows across the streets, walls facing the
  sun glowing warm, the shaded side of every street cool blue, rim light on people walking toward the sun.
* **How:** pick another Poly Haven sky. In the "kloofendal" series the number in the name is the sun's elevation; look
  for a `*_puresky` or partly cloudy one at 20-30 degrees, CC0 like the current one. Put its id in
  `FPSKitTownAssets.TownSkyId`; `AimTownSun` aims the sun from it automatically. `AimTownSun` holds the sun at 24
  degrees or higher (`FPSKitTownGround.cs:843`); lower that to 18 if the chosen sky's sun is lower. Then re-bake
  (`BakeDesertLight`: the bounce changes with the sun).
* **If the sky has clouds, turn on cloud shadows** (`cloudShadows = true`, strength 0.35, cover = the sky's
  cloud share, scale 500-800 m, wind 2-5 m/s). They are the cheapest "this is a real place" effect there is.
* **Gameplay check:** low sun puts alleys in deep shade. The eye's adaptation and the sky ambient keep it readable;
  check the soldier-in-shade views in the lighting tour.

### 2. Warm light, cool shade

* The sun: about 5,000 K at 25 degrees (`sunColor` now (1, 0.91, 0.80), about 4,800 K: right for late afternoon;
  warmer, (1, 0.85, 0.70), only below 15 degrees).
* The shade: lit by the blue sky. That comes from the bake if the sky is right; do not tint ambient by hand.
* The grade can push it a little further, never instead of it. A starting point for the desert (in `FPSKitThemes`,
  Desert case):
  ```csharp
  t.whiteBalanceTemperature = 3f;                       // a breath warmer overall
  t.splitShadows    = new Color(0.47f, 0.50f, 0.55f);   // shade toward the sky's blue
  t.splitHighlights = new Color(0.55f, 0.50f, 0.45f);   // sunlit toward amber
  t.splitBalance    = -10f;
  ```
  He signed off grade C (2026-10-30): show this beside it, and keep it only if he picks it.

### 3. Shadows: soft, deep enough, darker where things touch

* **Soft shadows on Medium** (`FPSKitQualityTiers`: Medium `soft: false` now). Hard-edged 2048 shadows over 80 m are
  the most "game-looking" thing on Medium. Soft shadows at Low quality cost about 0.3-0.6 ms; measure.
* **Contact darkening.** SSAO is High-only and set to contact reach (right: long reach made black smudges). Medium
  gets none: the 0.2 m lightmap AO covers rooms; outdoors the corner where a wall meets the street has nothing. Options:
  SSAO on Medium at quarter resolution, 4 samples (about 1 ms), or a baked "dirt and AO" band at the foot of every wall
  in the shared town shader (free at run time; realism step 2 wants that shader anyway).
* **Shadow strength** 0.8 in the theme: a real desert sun's shade is about 1/8 of the sun (a 3-stop difference). Keep
  0.8; the bake's sky light fills the rest. Do not lift shadows globally (it reads as fog).

### 4. Atmosphere

* **Now:** Unity exponential fog, density 0.00065, one colour (0.82, 0.79, 0.73). The far mountains and the near street
  get the same colour of haze from every direction.
* **Better: height and sun-aware haze.** A full-screen pass (URP renderer feature, after opaques, before
  transparents) that adds fog from depth: denser near the ground (height falloff about 60 m), thinner up the
  mountains, its colour taken from the sky near the horizon and warmer toward the sun (a Henyey-Greenstein lobe,
  g 0.6-0.7). About 0.3-0.5 ms at 1080p. This is what makes the town's depth read: near walls crisp, the far end of
  the avenue soft and warm, the mountains pale.
* **Dust in the air.** A few hundred large, slow, very faint particles near the player outdoors (the theme's weather
  slot), lit by the sun. Fade them with distance and by tier.

### 5. Light you can see

* **Sun shafts indoors.** The single best-looking thing in a desert interior: a bright patch on the floor and a faint
  beam from the door or window down to it, dust turning in it. URP has no volumetric lighting; the AA trick is cheap:
  * the builder already knows each room's openings and which the sun enters (`FPSKitTownLightmaps.BatchLightReport`
    flood fill, "which the sun enters");
  * for each such opening, a soft additive card (or a box with a shader that fades by depth and view angle) along the
    sun's direction from the opening to the floor, very faint (2-5 % added), plus a small dust particle system inside
    it (20-40 particles, lit by the sun);
  * on only within 25 m of the eye (as `RoomContents` does), High and Medium.
* **Sun shafts outdoors** (through palms, between buildings): a screen-space radial blur from the sun's screen position
  masked by the sky in the depth buffer. High only, about 0.5 ms; skip if the haze pass is in.
* **Glints.** Specular highlights on metal, glass and water are what the eye reads as "real". They need the
  reflections right (part 1) and a sun bright enough against them. Car paint and chrome in the sun, the dome's gold, the
  fountain's water.

### 6. Windows

Windows are a large share of every street view, and today they are dark mirrors with nothing behind them (audit:
`Town_GlassDark`, `Town_GlassBlue` FAKE, "renders as a black hole").
* **Interior mapping** for every window of a house you cannot enter: a shader that draws a fake room behind the glass
  (a small cubemap atlas of 6-8 room interiors, chosen per window by a hash of its position, with parallax), dimmed by
  how dark rooms are by day, plus curtains or blinds as a layer. Cost: one pass per window pixel, no geometry.
* **The glass itself:** a dielectric (metallic 0), smoothness 0.95, its reflection from the street's probe with Fresnel
  (strong at grazing angles, a few percent face-on), over the interior map. Put the street's reflection probes at
  window height (about 4 m) so the glass reflects buildings opposite, not the ground.
* **Enterable houses** keep real interiors behind real transparent glass.

### 7. Practical lights

* **Indoors by day:** lamps off (as now). **Shops:** tubes on, 4,000 K, slight green; baked (as the pilot did).
* **Dusk (a later level variant):** warm interior lamps (2,700 K), street lamps (sodium, 2,000 K, or LED 4,000 K),
  shop signs emissive with bloom, windows lit through their interior maps. Bloom threshold 1, intensity 0.45 (now) is
  right for that.

### 8. Exposure and tone

* **ACES** (now) gives the filmic shoulder that makes sun on white plaster look bright without clipping, but it shifts
  saturated colours toward yellow. If paint and signs look off, try URP's **Neutral** tonemapper plus the grade above.
* **Eye adaptation** (`EyeAdaptation`): opening 1.4 s, closing 0.5 s are real values; keep them. Rooms metered to 94
  of 255: right for play; a little lower (80-85) reads moodier.
* **Vignette** 0.2, **grain** 0.08: keep. No chromatic aberration, no lens dirt, no motion blur by default (motion blur
  as an option, off).

### 9. Materials decide how light looks

Light only looks as good as the surfaces it falls on (`Docs/DESERT_REVIEW.md` G1): bevelled edges catch a line of sun
along every corner (222 objects have none), real normal maps make low sun rake across plaster and brick, varied albedo
stops walls reading as cardboard. Do realism steps 1-3 alongside the lighting, not after it.

---

## The look, per tier

| | High | Medium | Low / phones / web |
|---|---|---|---|
| Sun, sky | late-afternoon HDRI, cloud shadows | same | same sky, no cloud shadows |
| Shadows | 4096, 150 m, soft, 4 cascades | 2048, 80 m, **soft** (new) | none (as now) |
| Indirect | lightmaps + APV with layers | same | lightmaps + classic probes per room |
| Reflections | per-room captures, box projected | same (Forward+) | street probe only |
| AO | SSAO contact | baked wall-foot band (or SSAO 1/4 res) | baked band |
| Atmosphere | height + sun haze pass, dust | haze pass | Unity fog |
| Shafts | indoor cards + dust, outdoor radial | indoor cards + dust | none |
| Windows | interior mapping | interior mapping | interior mapping (cheap) |
| Grade | ACES + split toning | same | same |

## Judging it

* Always in the release build, after the eye has adapted, at Medium and High (`ViewProbe`, the lighting tour).
* Per view: mean brightness, crushed (<= 5/255) and clipped (>= 250) shares, wall sheen (reflections on minus off), and
  a soldier's brightness against the wall behind him. Targets: rooms 90-120 adapted, crushed under 5 % (8-10 % looking
  into a bright doorway), no sheen over 3, soldier 0.7-1.3x the wall.
* Frame time: `Tools/walk-bench.sh`, median of three runs on mains. Medium aim 60 fps, 1 % low 50.
* Show him two pictures side by side for every look change (before, after); keep only what he picks.
