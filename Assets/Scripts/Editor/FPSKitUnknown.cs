#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The Unknown Planet, made unknown (2026-10-04). Ishaan: "rebuilt from scratch and also make it pure
    /// unknown (why is there a bridge in unknown planet, use the lava shader asset for best lava, improve
    /// skybox, add red hot sun and red environment, improve triangles)."
    ///
    /// The first version was the desert's layout with the paint changed, and it still carried everything a
    /// person puts down: trestle bridges, a railed fence along the river, survey-crew compounds, timber decks
    /// with ramps, sandbag lines. Nothing on a world nobody has been to should look like it was built by
    /// anyone. So, on a volcanic arena, those passes are replaced:
    ///
    /// <b>The crossings are a natural causeway</b> of hexagonal basalt columns, the way a lava flow cools --
    /// the tops a step apart at most, ragged at the edges, the sides standing down into the melt. <b>The river
    /// has no fence</b>, only rim rocks with gaps. <b>The compounds, decks and sandbag lines are gone</b>:
    /// rings of standing monoliths with a lit glyph circle, stepped hills of cooled columns to fight from,
    /// groves of glowing crystal, and lines of broken obsidian for cover. <b>The lava is the project's Lava
    /// shader</b> (Assets/Shaders/Lava), not a texture. <b>The sky is painted</b>: a red giant filling a tenth
    /// of the horizon, ash banks lit from underneath, a ringed planet, stars in the dark overhead.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static bool IsAlien() => _theme != null && _theme.volcanicZone;
        private static bool IsFineArena() => _theme != null && (_theme.snowZone || _theme.volcanicZone);

        // ==================================================================
        // The lava shader
        // ==================================================================
        private const string LavaShaderSource = "Assets/Shaders/Lava/Lava.mat";
        private static readonly Dictionary<string, Material> _shaderLavas = new Dictionary<string, Material>();

        /// <summary>
        /// The project's Lava shader graph on a material of its own, its tiling, voronoi and noise written for a
        /// mesh whose UVs run <paramref name="uvPerMetre"/> units to the metre, so a crust cell is the same size
        /// on the river as on a crater pool whatever the mesh's own mapping. Cached by role and mapping.
        /// </summary>
        private static Material ShaderLava(string role, float uvPerMetre)
        {
            uvPerMetre = Mathf.Max(0.0005f, uvPerMetre);
            string key = $"{role}_{uvPerMetre:0.0000}";
            if (_shaderLavas.TryGetValue(key, out var cached) && cached != null) return cached;

            var source = AssetDatabase.LoadAssetAtPath<Material>(LavaShaderSource);
            if (source == null) return _lavaMat;

            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_LavaShader_{role}_{Mathf.RoundToInt(uvPerMetre * 1000f)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }

            float u = uvPerMetre;
            void V2(string n, float x, float y) { if (mat.HasProperty(n)) mat.SetVector(n, new Vector4(x, y, 0f, 0f)); }
            void F(string n, float v) { if (mat.HasProperty(n)) mat.SetFloat(n, v); }

            // Metres per tile of the crust, and finer for the normal map; slow, because lava is thick.
            float crust = u < 0.05f ? 0.05f : 0.085f, fine = u < 0.05f ? 0.022f : 0.17f;
            V2("_LavaScale", crust / u, crust / u);
            V2("_LavaScaleNormal", fine / u, fine / u);
            F("_Normal_Strength", 5f);
            // Volatile: a boiling crust that churns, with deeper dark seams and a hotter core.
            F("_NoiseAmount", 1f);
            F("_VoronoiAmount", -0.05f);
            F("_Frenselpower", 1.1f);
            // Speeds are in tiles per second, whatever the mesh's UV scale: they were 0.0004 for the river
            // (u * 1.2 * 0.03), which is a standing pool. Flow runs down the long axis; the fine normal
            // layer runs faster and the voronoi/noise layers churn, so the surface reads as liquid.
            V2("_LavaSpeed", 0.02f, 0.14f);
            V2("_LavaNormalSpeed", 0.04f, 0.32f);
            V2("_VoronoiSpeed", 0.01f, 0.06f);
            V2("_NoiseSpeed", 0.08f, 0.12f);
            F("_Smoke_Int", 0f);
            F("_BackGround_Strenght", 0f);
            F("_Smoothness", 0.3f);
            // No white: the Fresnel and background terms are HDR near-white in the stock asset, and at a grazing
            // angle under bloom they filled half the screen with cream.
            mat.SetColor("_FresnelColor", new Color(2.4f, 0.45f, 0.08f));
            mat.SetColor("_LavaBackground", new Color(2.2f, 0.25f, 0.03f));
            mat.SetColor("_LavaColor", new Color(4.4f, 1.0f, 0f));
            F("_VoronoiScale", 0.22f / u);
            F("_NoiseScale", 0.55f / u);

            EditorUtility.SetDirty(mat);
            _shaderLavas[key] = mat;
            return mat;
        }

        /// <summary>
        /// Moves a lava mesh's UVs so the smallest is (1, 1). The graph returns NaN (black) on negative UVs and
        /// loses precision on large ones (a flat white blob across the screen), and pools, tongues and fissures
        /// had both: unit-space UVs centred on zero, and world-metre UVs out to 150. Only the origin moves, so
        /// the crust keeps its size.
        /// </summary>
        private static Mesh LavaUv(Mesh mesh)
        {
            var uvs = mesh.uv;
            if (uvs.Length == 0) return mesh;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            foreach (var uv in uvs) min = Vector2.Min(min, uv);
            for (int i = 0; i < uvs.Length; i++) uvs[i] = uvs[i] - min + Vector2.one;
            mesh.uv = uvs;
            return mesh;
        }

        // ==================================================================
        // Parts
        // ==================================================================
        private static void TriUp(MeshBuild b, Vector3 a, Vector3 c, Vector3 d)
        {
            if (Vector3.Cross(c - a, d - a).y >= 0f) b.Tri(a, c, d); else b.Tri(a, d, c);
        }

        /// <summary>
        /// One hexagonal basalt column: a chamfered top (eighteen triangles, tilted a little) and six sides down
        /// to <paramref name="yBottom"/>. Pointy-topped, so a lattice of them tiles with a spacing of
        /// sqrt(3) r by 1.5 r.
        /// </summary>
        private static void HexColumn(MeshBuild top, MeshBuild side, Vector3 c, float r, float yTop, float yBottom, Vector2 tilt)
        {
            var v = new Vector3[6];
            var vi = new Vector3[6];
            for (int k = 0; k < 6; k++)
            {
                float a = (30f + 60f * k) * Mathf.Deg2Rad;
                float ox = Mathf.Cos(a) * r, oz = Mathf.Sin(a) * r;
                v[k] = new Vector3(c.x + ox, yTop + tilt.x * ox + tilt.y * oz, c.z + oz);
                vi[k] = new Vector3(c.x + ox * 0.84f, yTop + 0.04f + tilt.x * ox * 0.84f + tilt.y * oz * 0.84f, c.z + oz * 0.84f);
            }
            var ctr = new Vector3(c.x, yTop + 0.04f, c.z);
            var inside = new Vector3(c.x, (yTop + yBottom) * 0.5f, c.z);
            for (int k = 0; k < 6; k++)
            {
                int j = (k + 1) % 6;
                TriUp(top, ctr, vi[k], vi[j]);
                TriUp(top, v[k], v[j], vi[j]);
                TriUp(top, v[k], vi[j], vi[k]);
                var bk = new Vector3(v[k].x, yBottom, v[k].z);
                var bj = new Vector3(v[j].x, yBottom, v[j].z);
                AddOutward(side, inside, v[k], bk, bj);
                AddOutward(side, inside, v[k], bj, v[j]);
            }
        }

        private static Material AlienGlowMaterial()
        {
            if (_alienGlowMat != null) return _alienGlowMat;
            _alienGlowMat = MakeMaterial("AlienGlow", new Color(0.30f, 0.05f, 0.02f), 0.4f, 0f);
            SetEmission(_alienGlowMat, new Color(3.0f, 0.5f, 0.08f));
            return _alienGlowMat;
        }

        private static Material CrystalMaterial()
        {
            if (_crystalMat != null) return _crystalMat;
            _crystalMat = MakeMaterial("AlienCrystal", new Color(0.55f, 0.07f, 0.04f), 0.92f, 0.1f);
            SetEmission(_crystalMat, new Color(0.7f, 0.10f, 0.03f));
            return _crystalMat;
        }

        private static Material _alienGlowMat, _crystalMat;

        // ==================================================================
        // The causeways
        // ==================================================================
        /// <summary>
        /// The ways across the river, as the lava made them: a field of basalt columns from rim to rim,
        /// meandering a little, ragged at the edges, tops within a step of each other (the bake needs them
        /// flat enough to join, and a player needs to run). Their sides go down to the bed through the melt.
        /// </summary>
        private static void BuildCauseways(Transform root, int layer, float half)
        {
            var group = new GameObject("Crossings").transform;
            group.SetParent(root, false);

            float depth = _theme.hazardDepth;
            const float r = 0.92f;
            float dx = Mathf.Sqrt(3f) * r, dz = 1.5f * r;

            for (int i = 0; i < _crossings.Count; i++)
            {
                var crossing = _crossings[i];
                float length = _theme.hazardWidth + BridgeOverhang * 2f;
                float width = _theme.bridgeWidth * 1.15f;
                float x0 = crossing.x - length * 0.5f, x1 = crossing.x + length * 0.5f;

                var tops = new MeshBuild { UVScale = 0.35f };
                var sides = new MeshBuild { UVScale = 0.18f };

                float Meander(float x) => Fbm2(x * 0.045f, i * 5.3f, _theme.randomSeed + 211, 3) * 6f;
                float HalfWidth(float x) => width * 0.5f * (0.78f + 0.5f * (Fbm2(x * 0.07f, i * 9f + 4f, _theme.randomSeed + 223, 2) + 0.5f));

                int rowsEach = Mathf.CeilToInt((width * 0.5f + 8f) / dz);
                int columns = 0;
                for (int j = -rowsEach; j <= rowsEach; j++)
                {
                    float rowZ = crossing.z + j * dz;
                    for (float x = x0 + (j & 1) * dx * 0.5f; x < x1; x += dx)
                    {
                        float across = rowZ - (crossing.z + Meander(x));
                        float hw = HalfWidth(x);
                        float h01 = Hash01(i * 7919 + j * 31, Mathf.RoundToInt(x * 3f));
                        // Ragged edge: some columns past the line, some short of it.
                        if (Mathf.Abs(across) > hw + (h01 - 0.5f) * 2.2f) continue;

                        float edge = Mathf.InverseLerp(hw * 0.62f, hw, Mathf.Abs(across));
                        // Within a step of the deck: 0, +-0.1 in tenths, dropping away only at the very edge.
                        float level = Mathf.Round((h01 - 0.5f) * 2.4f) * 0.06f - edge * 0.28f * h01;
                        // Down to the rim at the ends so the approach is a step, not a lip.
                        float toEnd = Mathf.Min(x - x0, x1 - x);
                        level -= Mathf.Clamp01(1f - toEnd / 5f) * 0.18f;

                        var tilt = new Vector2((h01 - 0.5f) * 0.05f, (Hash01(j, i + 99) - 0.5f) * 0.05f);
                        HexColumn(tops, sides, new Vector3(x, 0f, rowZ), r * 0.985f, DeckLift + level, -depth - 1.5f, tilt);
                        columns++;
                    }
                }

                var topGo = MeshObject(group, $"CausewayTop_{i}", ToMesh(tops, DenseKey("causewaytop")), _crustMat,
                                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
                var sideGo = MeshObject(group, $"CausewaySides_{i}", ToMesh(sides, DenseKey("causewaysides")), _rockWetMat,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, _theme.wallTag);
                // No NoStanding on the sides: the volume reaches the top plane and cut the walkable tops out of the bake.
                Mark(topGo, new Color(0.45f, 0.30f, 0.22f), 4);

                Debug.Log($"[FPSKit] causeway {i}: {columns} column(s).");
            }
        }

        /// <summary>
        /// Rocks along the river's rim where the fence was: boulders with gaps between them, the nearest thing the
        /// level has to a warning that the ground ends. No posts, no rails, nothing anyone put there.
        /// </summary>
        private static void BuildRimRocks(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("RimRocks").transform;
            group.SetParent(root, false);

            float edge = _theme.hazardWidth * 0.5f;
            float standOff = edge + FenceStandOff;
            float clear = _theme.bridgeWidth * 0.5f + 8f;

            for (int side = -1; side <= 1; side += 2)
                for (float z = -half; z < half; z += Rand(rng, 4.5f, 9f))
                {
                    if (NearCrossing(z, clear)) continue;
                    float x = GorgeCentreAt(z) + side * (standOff + Rand(rng, -1.2f, 1.2f));
                    Boulder(group, layer, rng, new Vector3(x, 0f, z), Rand(rng, 1.5f, 3.2f));
                }
        }

        // ==================================================================
        // Alien sites
        // ==================================================================
        private struct AlienSite { public Vector2 Centre; public float Radius, Height; public int Seed; }
        private static readonly List<AlienSite> _alienRings = new List<AlienSite>();
        private static readonly List<AlienSite> _alienHills = new List<AlienSite>();
        private static readonly List<AlienSite> _alienGroves = new List<AlienSite>();

        private static void ResetAlien()
        {
            _alienRings.Clear();
            _alienHills.Clear();
            _alienGroves.Clear();
        }

        /// <summary>Plans the ground the alien sites need, before the terrain exists: replaces the outposts and decks.</summary>
        private static void PlanAlienSites(System.Random rng, float half)
        {
            ResetAlien();

            for (int i = 0; i < Mathf.Max(6, _theme.outpostCount); i++)
            {
                float radius = Rand(rng, 12f, 17f);
                if (!TryClaim(rng, half * 0.9f, radius + 5f, out var p, 60)) continue;
                _alienRings.Add(new AlienSite { Centre = p, Radius = radius, Seed = rng.Next(1, 99999) });
                FlattenPad(p.x, p.y, radius * 0.85f, 24f);
                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }

            for (int i = 0; i < Mathf.Max(7, _theme.vantageCount); i++)
            {
                float radius = Rand(rng, 10f, 14f) * 1.45f;
                if (!TryClaim(rng, half * 0.9f, radius + 3f, out var p, 60)) continue;
                // Slope kept under about seventeen degrees so a lattice of columns, one step to the next, stays a
                // climb a player and an enemy can make.
                float h = radius * Rand(rng, 0.2f, 0.29f);
                _alienHills.Add(new AlienSite { Centre = p, Radius = radius, Height = h, Seed = rng.Next(1, 99999) });
                FlattenPad(p.x, p.y, radius * 0.95f, 22f, NaturalHeightAt(p.x, p.y));
            }

            for (int i = 0; i < 14; i++)
            {
                float radius = Rand(rng, 7f, 11f);
                if (!TryClaim(rng, half * 0.92f, radius, out var p, 60)) continue;
                _alienGroves.Add(new AlienSite { Centre = p, Radius = radius, Seed = rng.Next(1, 99999) });
            }
        }

        private static void BuildAlienSites(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("AlienSites").transform;
            group.SetParent(root, false);

            foreach (var s in _alienRings) BuildMonolithRing(group, layer, rng, s);
            foreach (var s in _alienHills) BuildColumnHill(group, layer, rng, s);
            foreach (var s in _alienGroves) BuildCrystalGrove(group, layer, rng, s);

            Debug.Log($"[FPSKit] alien sites: {_alienRings.Count} monolith ring(s), {_alienHills.Count} column hill(s), {_alienGroves.Count} crystal grove(s).");
        }

        /// <summary>
        /// A ring of tall obsidian monoliths round a circle of lit glyphs. Each is four broken blocks stacked
        /// and leaned, with two seams of light down its face and a column of marks across it: nobody knows
        /// what they say, and that is the point. Solid, and off the bake at the top.
        /// </summary>
        private static void BuildMonolithRing(Transform parent, int layer, System.Random rng, AlienSite s)
        {
            var site = new GameObject("MonolithRing").transform;
            site.SetParent(parent, false);

            float ground = GroundHeightAt(s.Centre.x, s.Centre.y);
            var rock = new MeshBuild { UVScale = 0.25f };
            var glow = new MeshBuild { UVScale = 0.5f };

            int count = 5 + rng.Next(3);
            for (int k = 0; k < count; k++)
            {
                float a = k * Mathf.PI * 2f / count + Rand(rng, -0.12f, 0.12f);
                float r = s.Radius * Rand(rng, 0.78f, 0.9f);
                var at = new Vector3(s.Centre.x + Mathf.Cos(a) * r, 0f, s.Centre.y + Mathf.Sin(a) * r);
                float g = GroundHeightAt(at.x, at.z);
                float h = Rand(rng, 6.5f, 11f) * 1.7f;
                float w = Rand(rng, 2.0f, 3.0f) * 1.6f, d = Rand(rng, 1.1f, 1.6f) * 1.6f;
                // Facing the middle of the ring.
                float yaw = (-a * Mathf.Rad2Deg) + 90f + Rand(rng, -10f, 10f);
                var facing = Quaternion.Euler(0f, yaw, 0f);

                float y = g - 1.0f;
                int blocks = 4;
                float bh = (h + 1f) / blocks;
                for (int b = 0; b < blocks; b++)
                {
                    float taper = 1f - b * 0.14f;
                    var lean = Quaternion.Euler(Rand(rng, -2.5f, 2.5f), Rand(rng, -8f, 8f), Rand(rng, -2.5f, 2.5f));
                    JaggedBlock(rock, new Vector3(at.x, y + bh * 0.5f, at.z) + facing * new Vector3(b * 0.06f, 0f, 0f),
                                new Vector3(w * taper, bh * 1.08f, d * taper), facing * lean, rng.Next(1, 99999));
                    y += bh * 0.94f;
                }

                // Seams of light and a column of marks, set on the face that looks inward.
                var face = at + facing * new Vector3(0f, 0f, -d * 0.52f);
                foreach (float off in new[] { -w * 0.22f, w * 0.22f })
                    glow.Box(new Vector3(face.x, g + h * 0.5f, face.z) + facing * new Vector3(off, 0f, 0f), new Vector3(0.09f, h * 0.78f, 0.05f), facing);
                for (int m = 0; m < 9; m++)
                {
                    float my = g + 1.4f + m * (h - 2.6f) / 8.5f;
                    float mw = Rand(rng, 0.2f, 0.7f);
                    glow.Box(new Vector3(face.x, my, face.z) + facing * new Vector3(Rand(rng, -0.35f, 0.35f), 0f, -0.01f), new Vector3(mw, 0.07f, 0.05f), facing);
                }
            }

            var rockGo = Solid(site, "Monoliths", rock, _obsidianMat, layer, "Concrete");
            _ = rockGo;
            SnowCapLike(site, "MonolithGlyphs", glow, AlienGlowMaterial(), layer);

            // The glyph circle on the ground: a ring, an inner ring and radial ticks, just proud of it.
            var ring = new MeshBuild { UVScale = 0.5f };
            float gy = ground + 0.06f;
            void Band(float r0, float r1, int seg, float tick)
            {
                for (int k = 0; k < seg; k++)
                {
                    if (tick > 0f && k % 2 == 1) continue;
                    float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                    Vector3 P(float rr, float aa) { float x = s.Centre.x + Mathf.Cos(aa) * rr, z = s.Centre.y + Mathf.Sin(aa) * rr; return new Vector3(x, GroundHeightAt(x, z) + 0.06f, z); }
                    AddUp(ring, P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1));
                }
            }
            Band(s.Radius * 0.52f, s.Radius * 0.56f, 72, 0f);
            Band(s.Radius * 0.26f, s.Radius * 0.29f, 48, 0f);
            Band(s.Radius * 0.30f, s.Radius * 0.51f, 28, 1f);
            _ = gy;
            SnowCapLike(site, "GlyphCircle", ring, AlienGlowMaterial(), layer);

            var light = new GameObject("RingLight");
            light.transform.SetParent(site, false);
            light.transform.position = new Vector3(s.Centre.x, ground + 3f, s.Centre.y);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point; l.range = s.Radius * 1.8f; l.intensity = 1.4f; l.color = new Color(1f, 0.38f, 0.12f);
            l.shadows = LightShadows.None;
        }

        /// <summary>A visual-only mesh with no collider, off the bake and the minimap (see SnowCap, which this is, for any arena).</summary>
        private static void SnowCapLike(Transform parent, string name, MeshBuild build, Material mat, int layer)
        {
            if (build.Triangles.Count == 0) return;
            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name)), mat, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(go);
            Hide(go);
        }

        /// <summary>
        /// A hill of cooled columns: a hex lattice over a disc, each column's top a step above the one outside it,
        /// so the whole stands up in terraces to a flat crown. Walkable the whole way (the steps are under the
        /// agent's climb), and the place to fight from now that there are no decks.
        /// </summary>
        private static void BuildColumnHill(Transform parent, int layer, System.Random rng, AlienSite s)
        {
            var site = new GameObject("ColumnHill").transform;
            site.SetParent(parent, false);

            float ground = GroundHeightAt(s.Centre.x, s.Centre.y);
            var tops = new MeshBuild { UVScale = 0.35f };
            var sides = new MeshBuild { UVScale = 0.2f };

            const float r = 0.95f;
            float dx = Mathf.Sqrt(3f) * r, dz = 1.5f * r;
            int rows = Mathf.CeilToInt(s.Radius / dz) + 1;
            for (int j = -rows; j <= rows; j++)
                for (float x = -s.Radius - dx + (j & 1) * dx * 0.5f; x <= s.Radius + dx; x += dx)
                {
                    float z = j * dz;
                    float rho = Mathf.Sqrt(x * x + z * z) / s.Radius;
                    float h01 = Hash01(s.Seed + j * 17, Mathf.RoundToInt(x * 5f));
                    if (rho > 1f + (h01 - 0.5f) * 0.14f) continue;

                    // Terraces: a straight slope, quantised to a tenth of a metre, with a touch of noise.
                    float rise = Mathf.Max(0f, 1f - rho) * s.Height;
                    float top = Mathf.Round(rise / 0.1f) * 0.1f + 0.12f + (h01 - 0.5f) * 0.12f;
                    var c = new Vector3(s.Centre.x + x, 0f, s.Centre.y + z);
                    float g = GroundHeightAt(c.x, c.z);
                    HexColumn(tops, sides, c, r * 0.985f, ground + top, g - 1.8f,
                              new Vector2((h01 - 0.5f) * 0.06f, (Hash01(s.Seed, j) - 0.5f) * 0.06f));
                }

            Solid(site, "HillTops", tops, _crustMat, layer, "Concrete", standing: true);
            var sideGo = Solid(site, "HillSides", sides, _rockWetMat, layer, _theme.wallTag);
            _ = sideGo;
        }

        /// <summary>
        /// A grove of crystal: a dozen hexagonal spires leaning out from a middle, the tallest at the heart, each
        /// a shaft and a point, lit from inside. Cover that glows, and a light for each grove.
        /// </summary>
        private static void BuildCrystalGrove(Transform parent, int layer, System.Random rng, AlienSite s)
        {
            var site = new GameObject("CrystalGrove").transform;
            site.SetParent(parent, false);

            var crystal = new MeshBuild { UVScale = 0.4f };
            int count = 9 + rng.Next(8);
            float ground = LowestGroundIn(s.Centre.x, s.Centre.y, s.Radius * 0.6f);

            for (int k = 0; k < count; k++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                float away = k == 0 ? 0f : s.Radius * Mathf.Sqrt(Rand(rng, 0.03f, 1f)) * 0.85f;
                float x = s.Centre.x + Mathf.Cos(a) * away, z = s.Centre.y + Mathf.Sin(a) * away;
                float len = Rand(rng, 2.2f, 8.5f) * Mathf.Lerp(1.5f, 0.6f, away / s.Radius) * 1.8f;
                if (k == 0) len = Rand(rng, 8f, 11f) * 1.8f;
                float rad = Mathf.Clamp(len * Rand(rng, 0.06f, 0.1f), 0.28f, 1.7f);
                // Leaning outward from the middle.
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Tan(Rand(rng, 4f, 28f) * Mathf.Deg2Rad);
                var foot = new Vector3(x, ground - 0.8f, z);
                var dir = (Vector3.up + lean * (k == 0 ? 0.2f : 1f)).normalized;
                var shaftEnd = foot + dir * len * 0.78f;
                crystal.Tube(foot, shaftEnd, rad, rad * 0.92f, 6);
                crystal.Tube(shaftEnd, foot + dir * len, rad * 0.92f, 0.02f, 6);
                // A smaller spire on its flank.
                if (rng.NextDouble() < 0.6)
                {
                    var from = foot + dir * len * Rand(rng, 0.2f, 0.45f);
                    var d2 = (dir + new Vector3(Rand(rng, -1f, 1f), 0.2f, Rand(rng, -1f, 1f)).normalized * 0.9f).normalized;
                    crystal.Tube(from, from + d2 * len * 0.4f, rad * 0.55f, rad * 0.5f, 6);
                    crystal.Tube(from + d2 * len * 0.4f, from + d2 * len * 0.55f, rad * 0.5f, 0.015f, 6);
                }
            }

            var go = Solid(site, "Crystals", crystal, CrystalMaterial(), layer, "Metal");
            _ = go;

            var light = new GameObject("GroveLight");
            light.transform.SetParent(site, false);
            light.transform.position = new Vector3(s.Centre.x, ground + 4f, s.Centre.y);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point; l.range = s.Radius * 2.6f; l.intensity = 1.8f; l.color = new Color(1f, 0.30f, 0.10f);
            l.shadows = LightShadows.None;
        }

        // ==================================================================
        // Cover lines
        // ==================================================================
        /// <summary>
        /// Lines of broken obsidian across the approaches, laid across the line between two places that matter with a
        /// gap in them -- the sandbag and timber lines this replaces, made of what the ground would throw up.
        /// </summary>
        /// <summary>
        /// Impaler fields: clusters of black glass spikes thrust out of the ground at random angles, the tallest
        /// leaning over the cluster, tips a dull red. Scattered with no plan at all, which is the point: the
        /// ground looks hostile and unplanned, and none of it is a place anyone could stand.
        /// </summary>
        private static void BuildSpikeFields(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("SpikeFields").transform;
            group.SetParent(root, false);
            var spikes = new MeshBuild { UVScale = 0.3f };
            var tips = new MeshBuild { UVScale = 0.5f };
            int placed = 0;

            for (int i = 0, tries = 0; i < 46 && tries < 400; tries++)
            {
                var at = new Vector2(Rand(rng, -half * 0.92f, half * 0.92f), Rand(rng, -half * 0.92f, half * 0.92f));
                float spread = Rand(rng, 2.2f, 4.5f);
                if (at.magnitude < 28f || !Free(at, spread + 1f)) continue;
                if (Mathf.Abs(at.x - GorgeCentreAt(at.y)) < _theme.hazardWidth * 0.5f + RimBandWidth + 10f) continue;
                Claim(at.x, at.y, spread + 1f);
                i++;

                int count = 5 + rng.Next(7);
                for (int k = 0; k < count; k++)
                {
                    float a = Rand(rng, 0f, Mathf.PI * 2f), d = spread * Mathf.Sqrt(Rand(rng, 0f, 1f));
                    float x = at.x + Mathf.Cos(a) * d, z = at.y + Mathf.Sin(a) * d;
                    float len = Rand(rng, 3f, 13f) * (k == 0 ? 1.5f : 1f);
                    float rad = Mathf.Clamp(len * Rand(rng, 0.04f, 0.08f), 0.25f, 0.8f);
                    float tilt = Rand(rng, 4f, 34f) * Mathf.Deg2Rad, dir = Rand(rng, 0f, Mathf.PI * 2f);
                    var foot = new Vector3(x, LowestGroundIn(x, z, rad) - 0.7f, z);
                    var axis = (Vector3.up + new Vector3(Mathf.Cos(dir), 0f, Mathf.Sin(dir)) * Mathf.Tan(tilt)).normalized;
                    var mid = foot + axis * len * 0.8f;
                    spikes.Tube(foot, mid, rad, rad * 0.55f, 5);
                    spikes.Tube(mid, foot + axis * len, rad * 0.55f, 0.02f, 5);
                    tips.Tube(foot + axis * len * 0.9f, foot + axis * len, rad * 0.30f, 0.015f, 5);
                    placed++;
                }
            }

            var mat = _obsidianMat;
            Solid(group, "Spikes", spikes, mat, layer, "Concrete");
            SnowCapLike(group, "SpikeTips", tips, AlienGlowMaterial(), layer);
            Debug.Log($"[FPSKit] spike fields: {placed} spike(s).");
        }

        private static void BuildShardLines(Transform root, int layer, System.Random rng, float half)
        {
            if (_anchors.Count < 2) return;
            var group = new GameObject("ShardLines").transform;
            group.SetParent(root, false);

            for (int i = 0; i < Mathf.Max(18, _theme.coverLineCount); i++)
            {
                var a = _anchors[rng.Next(_anchors.Count)];
                var b = _anchors[rng.Next(_anchors.Count)];
                var along = b - a; along.y = 0f;
                if (along.sqrMagnitude < 900f) continue;

                var centre = a + along * Rand(rng, 0.25f, 0.75f);
                if (Mathf.Abs(centre.x) > half * 0.92f || Mathf.Abs(centre.z) > half * 0.92f) continue;

                float facing = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
                int pieces = rng.Next(3, 6);
                int gap = rng.Next(pieces);
                var run = new MeshBuild { UVScale = 0.3f };
                bool any = false;

                for (int k = 0; k < pieces; k++)
                {
                    if (k == gap) continue;
                    float offset = (k - (pieces - 1) * 0.5f) * Rand(rng, 4.2f, 6f);
                    var right = Quaternion.Euler(0f, facing + 90f, 0f) * Vector3.forward;
                    var pos = centre + right * offset;
                    if (!Free(new Vector2(pos.x, pos.z), 3f)) continue;
                    Claim(pos.x, pos.z, 3f);
                    float g = GroundHeightAt(pos.x, pos.z);
                    float yaw = facing + 90f + Rand(rng, -14f, 14f);
                    // Two or three leaning slabs side by side.
                    int slabs = 2 + rng.Next(2);
                    for (int sIdx = 0; sIdx < slabs; sIdx++)
                    {
                        float h = Rand(rng, 1.8f, 3.1f);
                        var rot = Quaternion.Euler(Rand(rng, -9f, 9f), yaw + Rand(rng, -16f, 16f), Rand(rng, -11f, 11f));
                        var at = new Vector3(pos.x, g + h * 0.4f, pos.z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3((sIdx - (slabs - 1) * 0.5f) * 1.35f, 0f, Rand(rng, -0.35f, 0.35f));
                        JaggedBlock(run, at, new Vector3(Rand(rng, 1.2f, 1.9f), h, Rand(rng, 0.7f, 1.2f)), rot, rng.Next(1, 99999));
                    }
                    any = true;
                }

                if (any) Solid(group, "Shards", run, _obsidianMat, layer, "Concrete");
            }
        }

        // ==================================================================
        // The horizon
        // ==================================================================
        /// <summary>
        /// Massifs behind the cones: ridged-noise mountains of dark rock under the red sun, with veins of molten rock
        /// running down their flanks (the quads where a second ridged noise peaks are written as glowing material).
        /// </summary>
        private static void BuildAlienMountains(Transform group, int layer, System.Random rng)
        {
            var rockMats = new Material[3];
            for (int b = 0; b < 3; b++)
            {
                float haze = b / 2f;
                var col = Color.Lerp(new Color(0.11f, 0.055f, 0.05f), _theme.fogColor, 0.10f + haze * 0.55f);
                rockMats[b] = MakeDetailMaterial($"AlienPeak{b}", col, "Basalt", 0.012f, 0.2f, 0f, 1.2f);
            }
            var veinMat = MakeMaterial("AlienVein", new Color(0.30f, 0.06f, 0.02f), 0.3f, 0f);
            SetEmission(veinMat, new Color(2.2f, 0.35f, 0.06f));

            const int N = 64;
            int massifs = 20;
            for (int m = 0; m < massifs; m++)
            {
                float angle = m * 2.39996f + Rand(rng, -0.1f, 0.1f);
                float distance = Rand(rng, 900f, 1180f);
                float W = Rand(rng, 420f, 700f);
                float H = Rand(rng, 170f, 340f);
                int seed = rng.Next(1, 99999);
                int bucket = Mathf.Min(2, Mathf.FloorToInt(Mathf.InverseLerp(900f, 1180f, distance) * 3f));

                var rock = new MeshBuild { UVScale = 0.012f };
                var vein = new MeshBuild { UVScale = 0.012f };
                var grid = new Vector3[N + 1, N + 1];
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        float u = i / (float)N - 0.5f, v = j / (float)N - 0.5f;
                        float x = u * W, z = v * W;
                        float env = Mathf.Clamp01(1f - (u * u + v * v) * 4f);
                        env = env * env * (3f - 2f * env);
                        float rr = 0f, amp = 1f, f = 0.006f, norm = 0f;
                        for (int o = 0; o < 4; o++)
                        {
                            float n = 1f - Mathf.Abs(Fbm2(x * f + seed, z * f, seed + o, 1) * 2f);
                            rr += n * n * amp; norm += amp; amp *= 0.5f; f *= 2.1f;
                        }
                        rr /= norm;
                        float y = H * Mathf.Pow(env, 0.85f) * (0.3f + 0.7f * rr) + Fbm2(x * 0.05f, z * 0.05f, seed + 9, 2) * 7f * env;
                        grid[i, j] = new Vector3(x, y, z);
                    }
                for (int i = 0; i < N; i++)
                    for (int j = 0; j < N; j++)
                    {
                        var a = grid[i, j]; var b = grid[i, j + 1]; var c = grid[i + 1, j + 1]; var d = grid[i + 1, j];
                        float avg = (a.y + b.y + c.y + d.y) * 0.25f;
                        float veinNoise = 1f - Mathf.Abs(Fbm2(a.x * 0.011f + seed, a.z * 0.011f, seed + 31, 3) * 2f);
                        bool isVein = veinNoise > 0.935f && avg > H * 0.12f && avg < H * 0.82f;
                        AddUp(isVein ? vein : rock, a, b, c, d);
                    }

                var pos = new Vector3(Mathf.Cos(angle) * distance, -H * 0.04f, Mathf.Sin(angle) * distance);
                var rot = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                MeshObject(group, "AlienPeak", ToMesh(rock, DenseKey("alienpeak")), rockMats[bucket], pos, rot, Vector3.one, layer, "Untagged", collider: false);
                if (vein.Triangles.Count > 0)
                    MeshObject(group, "AlienPeakVeins", ToMesh(vein, DenseKey("alienveins")), veinMat, pos, rot, Vector3.one, layer, "Untagged", collider: false);
            }
        }

        // ==================================================================
        // The sky
        // ==================================================================
        private const string AlienSkyRecipe = "alien-sky-v2";

        /// <summary>
        /// The Unknown Planet's sky, painted: a red giant filling a tenth of the horizon (limb-darkened, grained,
        /// spotted, with prominences and a corona), a horizon that glows, banks of ash lit from underneath and
        /// from the sun's side, no planet, nothing but the sun, and stars where the air thins.
        /// Same equirectangular layout and the same shader as Snowbound's (Skybox/Panoramic), so the sun in the
        /// picture is where the scene's directional light says it is.
        /// </summary>
        private static Material UnknownSky()
        {
            string texPath = $"{TextureFolder}/Sky_{SafeName(_theme.themeName)}.png";
            string matPath = $"{MaterialFolder}/Sky_{SafeName(_theme.themeName)}.mat";
            const int W = 4096, H = 2048;

            float el = _theme.sunAngles.x * Mathf.Deg2Rad, az = _theme.sunAngles.y * Mathf.Deg2Rad;
            var sun = new Vector3(-Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el)).normalized;
            string stamp = $"{AlienSkyRecipe}|{sun.x:0.000},{sun.y:0.000},{sun.z:0.000}|{ColorKey(_theme.fogColor)}";

            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            bool fresh = System.IO.File.Exists(texPath) && importer != null && importer.userData == stamp;
            Color32[] panorama = null;

            if (!fresh)
            {
                panorama = new Color32[W * H];
                var fog = _theme.fogColor;
                var horizon = Color.Lerp(fog, new Color(0.85f, 0.26f, 0.08f), 0.55f);
                var low = new Color(0.55f, 0.10f, 0.05f);
                var mid = new Color(0.24f, 0.03f, 0.03f);
                var zenith = new Color(0.035f, 0.004f, 0.012f);
                int seed = _theme.randomSeed * 17 + 3;
                const float SunR = 0.19f;                               // about 11 degrees: a looming, dying giant

                // The sun's tangent frame, for the disc, the prominences and the spots.
                var e1 = Vector3.Cross(Vector3.up, sun).normalized;
                var e2 = Vector3.Cross(sun, e1);

                Parallel.For(0, H, j =>
                {
                    float v = (j + 0.5f) / H;
                    float lat = (1f - v) * Mathf.PI;
                    float y = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                    for (int i = 0; i < W; i++)
                    {
                        float u = (i + 0.5f) / W;
                        float lon = (0.5f - u) * 2f * Mathf.PI;
                        var d = new Vector3(sl * Mathf.Cos(lon), y, sl * Mathf.Sin(lon));
                        float cosT = Mathf.Clamp(Vector3.Dot(d, sun), -1f, 1f);
                        float theta = Mathf.Acos(cosT);

                        // ---- the dark overhead to the burning horizon ----
                        float elev = Mathf.Asin(Mathf.Clamp(y, -1f, 1f)) * Mathf.Rad2Deg;
                        Color col;
                        if (elev < 0f) col = Color.Lerp(horizon, horizon * 0.55f, Sstep(0f, 20f, -elev));
                        else if (elev < 12f) col = Color.Lerp(horizon, low, Sstep(0f, 12f, elev));
                        else if (elev < 42f) col = Color.Lerp(low, mid, Sstep(12f, 42f, elev));
                        else col = Color.Lerp(mid, zenith, Sstep(42f, 85f, elev));

                        // ---- stars, where the air is thin and nothing is glaring ----
                        if (y > 0.18f)
                        {
                            float fade = Sstep(0.18f, 0.5f, y) * (1f - Sstep(0.15f, 1.1f, 1f - theta * 0.5f));
                            int cx = Mathf.FloorToInt(d.x * 210f), cy = Mathf.FloorToInt(d.y * 210f), cz = Mathf.FloorToInt(d.z * 210f);
                            float h = SkyHash(cx, cy, cz, seed + 5);
                            if (h > 0.9972f)
                            {
                                float b = Mathf.Pow((h - 0.9972f) / 0.0028f, 3f) * 0.9f + 0.1f;
                                col += new Color(1f, 0.82f, 0.78f) * (b * fade * 0.8f);
                            }
                        }

                        // ---- the glare round the sun, and the corona ----
                        float over = Mathf.Max(0f, theta - SunR);
                        col += new Color(1.0f, 0.30f, 0.07f) * (1.1f * Mathf.Exp(-over / 0.10f));
                        col += new Color(0.9f, 0.18f, 0.04f) * (0.8f * Mathf.Exp(-over / 0.5f));
                        col += new Color(0.6f, 0.10f, 0.03f) * (0.5f * Mathf.Exp(-over / 1.4f));

                        // ---- ash banks and clouds on a plane overhead, lit from beneath ----
                        if (y > 0.004f)
                        {
                            float k = 1f / (y + 0.12f);
                            float qx = d.x * k, qz = d.z * k;
                            float cov = Sstep(0.35f, 0.62f, SkyFbm(qx * 0.15f + 17f, qz * 0.15f, 2.7f, seed + 9, 3));
                            float c1 = SkyFbm(qx * 0.55f, qz * 0.55f + 3f, 5.1f, seed + 21, 5);
                            float streak = SkyFbm(qx * 0.18f, qz * 1.1f, 8.4f, seed + 33, 4);
                            float dens = Sstep(0.44f, 0.70f, c1 * 0.7f + streak * 0.3f) * cov;
                            // A low bank of smoke along the whole horizon, darker than the glow behind it.
                            float bank = Sstep(0.0f, 0.04f, y) * (1f - Sstep(0.07f, 0.26f, y)) * Sstep(0.38f, 0.72f, SkyFbm(qx * 0.07f + 5f, qz * 0.9f, 1.3f, seed + 41, 4));
                            dens = Mathf.Max(dens * Sstep(0.01f, 0.2f, y), bank * 0.9f);

                            if (dens > 0.002f)
                            {
                                float towardSun = Mathf.Clamp01(cosT);
                                var shadow = new Color(0.035f, 0.008f, 0.01f);
                                var lit = new Color(1.0f, 0.34f, 0.08f);
                                float light = Mathf.Pow(towardSun, 2.4f) * 0.9f + (1f - Mathf.Clamp01(y * 3.5f)) * 0.45f;
                                float edge = 1f - Sstep(0f, 0.55f, dens);
                                var cloud = Color.Lerp(shadow, lit, Mathf.Clamp01(light + edge * 0.35f * towardSun));
                                col = Color.Lerp(col, cloud, Mathf.Clamp01(dens * 0.93f));
                            }
                        }

                        // ---- the sun: a disc of limb-darkened, grained, spotted fire, with prominences ----
                        {
                            float da = Vector3.Dot(d, e1), db = Vector3.Dot(d, e2);
                            float ang = Mathf.Atan2(db, da);
                            if (theta < SunR)
                            {
                                float rr = theta / SunR;
                                float mu = Mathf.Sqrt(Mathf.Max(0f, 1f - rr * rr));
                                var core = new Color(1.0f, 0.95f, 0.70f);
                                var limb = new Color(0.62f, 0.04f, 0.01f);
                                var disc = Color.Lerp(limb, core, Mathf.Pow(mu, 0.75f));
                                float grain = SkyFbm(d.x * 46f, d.y * 46f, d.z * 46f, seed + 51, 4);
                                disc *= 0.70f + grain * 0.75f;
                                float spot = Sstep(0.52f, 0.64f, SkyFbm(d.x * 13f + 3f, d.y * 13f, d.z * 13f, seed + 61, 3));
                                disc = Color.Lerp(disc, disc * new Color(0.55f, 0.2f, 0.1f), spot * 0.8f * Sstep(0.15f, 0.55f, rr));
                                col = Color.Lerp(disc, col, Sstep(0.96f, 1.0f, rr) * 0.6f);
                            }
                            else
                            {
                                // Prominences: loops of fire standing off the limb where a slow noise around it peaks.
                                float around = SkyFbm(Mathf.Cos(ang) * 2.4f + 9f, Mathf.Sin(ang) * 2.4f, 4.4f, seed + 71, 3);
                                float height = SunR * (0.18f + 1.1f * Sstep(0.42f, 0.74f, around));
                                float off = theta - SunR;
                                if (off < height)
                                {
                                    float f = 1f - off / height;
                                    col += new Color(1.0f, 0.42f, 0.10f) * (f * f * 1.15f);
                                }
                            }
                        }

                        float dither = (SkyHash(i, j, 7, 91) - 0.5f) * (1.5f / 255f);
                        panorama[j * W + i] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.r + dither) * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.g + dither) * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.b + dither) * 255f), 0, 255), 255);
                    }
                });

                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.SetPixels32(panorama);
                tex.Apply(false);
                System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = true;
                    importer.wrapModeU = TextureWrapMode.Repeat;
                    importer.wrapModeV = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.maxTextureSize = 4096;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.userData = stamp;
                    importer.SaveAndReimport();
                }
            }

            // The reflection cubemap is made from a half-size copy of the same picture.
            if (panorama == null)
            {
                var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                loaded.LoadImage(System.IO.File.ReadAllBytes(texPath));
                panorama = loaded.GetPixels32();
                Object.DestroyImmediate(loaded);
            }
            const int RW = 2048, RH = 1024;
            var small = new Color32[RW * RH];
            for (int yy = 0; yy < RH; yy++)
                for (int xx = 0; xx < RW; xx++)
                    small[yy * RW + xx] = panorama[yy * 2 * W + xx * 2];
            ReflectSky(small, RW, RH, $"{TextureFolder}/Reflection_{SafeName(_theme.themeName)}.cubemap");

            var shader = Shader.Find("Skybox/Panoramic");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (shader == null) return existing;
            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_Exposure", 1.12f);
            mat.SetFloat("_Rotation", 0f);
            mat.SetFloat("_Mapping", 1f);
            mat.SetFloat("_ImageType", 0f);
            mat.SetFloat("_MirrorOnBack", 0f);
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            if (existing == null) AssetDatabase.CreateAsset(mat, matPath);
            else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
