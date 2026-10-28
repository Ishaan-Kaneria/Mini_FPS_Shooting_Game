#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>The realism audit's measurements: geometry per mesh, maps and texture statistics per material, wall
    /// thickness by casts, and the rating rules (FPSKitRealismAudit).</summary>
    public static partial class FPSKitRealismAudit
    {
        // ------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------
        /// <summary>One submesh's measurements, in the mesh's own units (scaled per instance by the caller).</summary>
        sealed class GeoSub
        {
            public long tris;
            public double area, axisArea, bevelLen, sharpLen, openLen, boxArea, uvArea;
            public float uvSpanP95;
            public bool colourVariation;
            /// <summary>Some of its triangles (centre, normal), for the thickness casts and the close-up's choice of spot.</summary>
            public readonly List<(Vector3 c, Vector3 n, float area)> samples = new List<(Vector3, Vector3, float)>();
        }

        static readonly Dictionary<Mesh, GeoSub[]> _geo = new Dictionary<Mesh, GeoSub[]>();

        /// <summary>
        /// A mesh's geometry by submesh. Vertices are welded by position (1 mm) so the edges between separately built quads
        /// count; each edge's dihedral angle sorts it: 60 degrees and over is a hard edge, 10-60 beside a strip under 6 cm wide
        /// is a bevel (a 16-sided pipe's facets are not), one triangle only is an open edge (a shell). A connected piece of 8
        /// corners and 12 triangles is a plain box.
        /// </summary>
        static GeoSub[] Geometry(Mesh mesh)
        {
            if (_geo.TryGetValue(mesh, out var cached)) return cached;
            string key = GeoKey(mesh);
            if (_geoDisk.TryGetValue(key, out cached)) { _geo[mesh] = cached; return cached; }
            var measured = MeasureMesh(mesh);
            _geoDisk[key] = measured;
            _geoDirty = true;
            return measured;
        }

        // ---- measured meshes kept between runs (Library/FPSKitRealismGeometry.bin): seven minutes of the first audit ----
        const string GeoCachePath = "Library/FPSKitRealismGeometry.bin";
        const int GeoCacheVersion = 2;
        static readonly Dictionary<string, GeoSub[]> _geoDisk = new Dictionary<string, GeoSub[]>();
        static bool _geoDirty;

        static string GeoKey(Mesh m)
        {
            var b = m.bounds;
            return FormattableString.Invariant($"{m.name}|{m.vertexCount}|{m.subMeshCount}|{b.center.x:F3},{b.center.y:F3},{b.center.z:F3}|{b.size.x:F3},{b.size.y:F3},{b.size.z:F3}");
        }

        static void LoadGeoCache()
        {
            _geoDisk.Clear(); _geoDirty = false;
            if (!System.IO.File.Exists(GeoCachePath)) return;
            try
            {
                using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(GeoCachePath));
                if (r.ReadInt32() != GeoCacheVersion) return;
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string key = r.ReadString();
                    var subs = new GeoSub[r.ReadInt32()];
                    for (int s = 0; s < subs.Length; s++)
                    {
                        var g = subs[s] = new GeoSub
                        {
                            tris = r.ReadInt64(), area = r.ReadDouble(), axisArea = r.ReadDouble(), bevelLen = r.ReadDouble(), sharpLen = r.ReadDouble(),
                            openLen = r.ReadDouble(), boxArea = r.ReadDouble(), uvArea = r.ReadDouble(), uvSpanP95 = r.ReadSingle(), colourVariation = r.ReadBoolean(),
                        };
                        int k = r.ReadInt32();
                        for (int j = 0; j < k; j++)
                            g.samples.Add((new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadSingle()));
                    }
                    _geoDisk[key] = subs;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[FPSKit] realism: geometry cache unreadable, measuring afresh ({e.Message})"); _geoDisk.Clear(); }
        }

        static void SaveGeoCache()
        {
            if (!_geoDirty) return;
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(GeoCachePath));
            w.Write(GeoCacheVersion);
            w.Write(_geoDisk.Count);
            foreach (var kv in _geoDisk)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Length);
                foreach (var g in kv.Value)
                {
                    w.Write(g.tris); w.Write(g.area); w.Write(g.axisArea); w.Write(g.bevelLen); w.Write(g.sharpLen);
                    w.Write(g.openLen); w.Write(g.boxArea); w.Write(g.uvArea); w.Write(g.uvSpanP95); w.Write(g.colourVariation);
                    w.Write(g.samples.Count);
                    foreach (var (c, n, a) in g.samples) { w.Write(c.x); w.Write(c.y); w.Write(c.z); w.Write(n.x); w.Write(n.y); w.Write(n.z); w.Write(a); }
                }
            }
            _geoDirty = false;
        }

        static GeoSub[] MeasureMesh(Mesh mesh)
        {
            using var arr = Mesh.AcquireReadOnlyMeshData(mesh);
            var md = arr[0];
            int vc = md.vertexCount, subs = md.subMeshCount;
            var res = new GeoSub[subs];
            for (int s = 0; s < subs; s++) res[s] = new GeoSub();
            _geo[mesh] = res;
            if (vc == 0) return res;

            var pos = new NativeArray<Vector3>(vc, Allocator.Persistent);
            md.GetVertices(pos);
            bool hasUv = md.HasVertexAttribute(VertexAttribute.TexCoord0), hasCol = md.HasVertexAttribute(VertexAttribute.Color);
            var uv = hasUv ? new NativeArray<Vector2>(vc, Allocator.Persistent) : default;
            if (hasUv) md.GetUVs(0, uv);
            var col = hasCol ? new NativeArray<Color>(vc, Allocator.Persistent) : default;
            if (hasCol) md.GetColors(col);
            try
            {
                var weld = new int[vc];
                var weldPos = new List<Vector3>(vc);
                // Positions to the millimetre, packed 21 bits an axis (+-1048 m: the arenas are smaller).
                var map = new Dictionary<long, int>(vc);
                for (int i = 0; i < vc; i++)
                {
                    var p = pos[i];
                    long q = ((long)(Mathf.RoundToInt(p.x * 1000f) & 0x1FFFFF) << 42) | ((long)(Mathf.RoundToInt(p.y * 1000f) & 0x1FFFFF) << 21) | (long)(Mathf.RoundToInt(p.z * 1000f) & 0x1FFFFF);
                    if (!map.TryGetValue(q, out int w)) { w = weldPos.Count; map[q] = w; weldPos.Add(p); }
                    weld[i] = w;
                }

                var triV = new List<int>();          // welded corners, three per triangle
                var triSub = new List<int>();
                var triN = new List<Vector3>();
                var triArea = new List<float>();
                var triWidth = new List<float>();
                var rng = new System.Random(mesh.name.GetHashCode() ^ vc);
                for (int s = 0; s < subs; s++)
                {
                    var desc = md.GetSubMesh(s);
                    if (desc.topology != MeshTopology.Triangles || desc.indexCount < 3) continue;
                    var idx = new NativeArray<int>(desc.indexCount, Allocator.Persistent);
                    md.GetIndices(idx, s, true);
                    var g = res[s];
                    var spans = new List<(float span, float area)>();
                    double cSum = 0, cSq = 0; int cN = 0;
                    int triCount = desc.indexCount / 3;
                    // Keep about 64 sample triangles per submesh, spread through it.
                    int every = Mathf.Max(1, triCount / 64);
                    for (int t = 0; t < triCount; t++)
                    {
                        int a = idx[t * 3], b = idx[t * 3 + 1], c = idx[t * 3 + 2];
                        g.tris++;
                        Vector3 pa = pos[a], pb = pos[b], pc = pos[c];
                        var cross = Vector3.Cross(pb - pa, pc - pa);
                        float area = 0.5f * cross.magnitude;
                        if (area < 1e-9f) continue;
                        var n = cross / (2f * area);
                        g.area += area;
                        if (Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z))) > 0.99939f) g.axisArea += area;   // within 2 degrees of an axis
                        float longest = Mathf.Max((pb - pa).magnitude, Mathf.Max((pc - pb).magnitude, (pa - pc).magnitude));
                        triV.Add(weld[a]); triV.Add(weld[b]); triV.Add(weld[c]);
                        triSub.Add(s); triN.Add(n); triArea.Add(area); triWidth.Add(2f * area / Mathf.Max(longest, 1e-6f));
                        if (t % every == 0 || (g.samples.Count < 64 && rng.NextDouble() < 0.01)) g.samples.Add(((pa + pb + pc) / 3f, n, area));
                        if (hasUv)
                        {
                            Vector2 ua = uv[a], ub = uv[b], uc = uv[c];
                            g.uvArea += 0.5 * Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (ub.y - ua.y) * (uc.x - ua.x));
                            float span = Mathf.Max(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x)) - Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)),
                                                   Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y)) - Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y)));
                            spans.Add((span, area));
                        }
                        if (hasCol)
                        {
                            float la = Lum(col[a]), lb = Lum(col[b]), lc = Lum(col[c]);
                            cSum += la + lb + lc; cSq += la * la + lb * lb + lc * lc; cN += 3;
                        }
                    }
                    idx.Dispose();
                    if (spans.Count > 0)
                    {
                        spans.Sort((x, y) => x.span.CompareTo(y.span));
                        double total = spans.Sum(x => (double)x.area), run = 0;
                        foreach (var sp in spans) { run += sp.area; if (run >= total * 0.95) { g.uvSpanP95 = sp.span; break; } }
                    }
                    if (cN > 0)
                    {
                        double mean = cSum / cN, sd = Math.Sqrt(Math.Max(0, cSq / cN - mean * mean));
                        g.colourVariation = sd > 0.02;
                    }
                }

                // Edges.
                int tc = triSub.Count;
                var edges = new Dictionary<long, (int t0, int t1, int n)>(tc * 2);
                for (int t = 0; t < tc; t++)
                    for (int e = 0; e < 3; e++)
                    {
                        int u = triV[t * 3 + e], v = triV[t * 3 + (e + 1) % 3];
                        if (u == v) continue;
                        long key = ((long)Math.Min(u, v) << 32) | (uint)Math.Max(u, v);
                        if (!edges.TryGetValue(key, out var rec)) edges[key] = (t, -1, 1);
                        else edges[key] = rec.n == 1 ? (rec.t0, t, 2) : (rec.t0, rec.t1, rec.n + 1);
                    }
                foreach (var kv in edges)
                {
                    int u = (int)(kv.Key >> 32), v = (int)(kv.Key & 0xffffffff);
                    float len = (weldPos[u] - weldPos[v]).magnitude;
                    var (t0, t1, n) = kv.Value;
                    if (n == 1) { res[triSub[t0]].openLen += len; continue; }
                    if (n > 2) { res[triSub[t0]].sharpLen += len; continue; }
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(triN[t0], triN[t1]), -1f, 1f)) * Mathf.Rad2Deg;
                    if (angle >= 60f) { res[triSub[t0]].sharpLen += len * 0.5; res[triSub[t1]].sharpLen += len * 0.5; }
                    else if (angle >= 10f && (triWidth[t0] < 0.06f || triWidth[t1] < 0.06f))
                    { res[triSub[t0]].bevelLen += len * 0.5; res[triSub[t1]].bevelLen += len * 0.5; }
                }

                // Plain boxes: connected pieces of 8 corners and 12 triangles.
                var parent = new int[weldPos.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                for (int t = 0; t < tc; t++)
                {
                    int r0 = Find(triV[t * 3]);
                    for (int e = 1; e < 3; e++) { int r1 = Find(triV[t * 3 + e]); if (r1 != r0) parent[r1] = r0; }
                }
                var compTris = new Dictionary<int, int>();
                var compVerts = new Dictionary<int, int>();
                for (int i = 0; i < parent.Length; i++) { int r = Find(i); compVerts[r] = compVerts.TryGetValue(r, out var c) ? c + 1 : 1; }
                for (int t = 0; t < tc; t++) { int r = Find(triV[t * 3]); compTris[r] = compTris.TryGetValue(r, out var c) ? c + 1 : 1; }
                for (int t = 0; t < tc; t++)
                {
                    int r = Find(triV[t * 3]);
                    if (compTris[r] == 12 && compVerts.TryGetValue(r, out var cv) && cv == 8) res[triSub[t]].boxArea += triArea[t];
                }
            }
            finally
            {
                pos.Dispose();
                if (hasUv) uv.Dispose();
                if (hasCol) col.Dispose();
            }
            return res;
        }

        static float UniformScale(Matrix4x4 m) => Mathf.Pow(Mathf.Abs(m.determinant), 1f / 3f);

        static void MeasureGeometry(List<Item> items)
        {
            LoadGeoCache();
            int had = _geoDisk.Count;
            try { MeasureItems(items); }
            finally { SaveGeoCache(); }
            Log($"geometry: {had} meshes known from earlier runs, {_geoDisk.Count - had} measured now");
        }

        static void MeasureItems(List<Item> items)
        {
            foreach (var it in items)
            {
                if (it.terrainLayer != null || it.skyMaterial != null) continue;
                foreach (var m in it.members)
                {
                    if (m.lod != 0) continue;   // LOD0 is what is seen up close
                    var subs = Geometry(m.mesh);
                    if (m.sub >= subs.Length) continue;
                    var g = subs[m.sub];
                    float k = UniformScale(m.toWorld), k2 = k * k;
                    it.tris += g.tris;
                    it.area += g.area * k2;
                    it.axisArea += g.axisArea * k2;
                    it.bevelLen += g.bevelLen * k;
                    it.sharpLen += g.sharpLen * k;
                    it.openLen += g.openLen * k;
                    it.boxArea += g.boxArea * k2;
                    it.vertexColourVariation |= g.colourVariation;
                    it.matArea[m.mat] = (it.matArea.TryGetValue(m.mat, out var a) ? a : 0) + g.area * k2;
                }
                // A plain box: every piece of it is one.
                it.plainBox = it.area > 0 && it.BoxShare > 0.98;
            }
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------
        static readonly Dictionary<(Texture, int, Color), TexStats> _texCache = new Dictionary<(Texture, int, Color), TexStats>();
        static readonly Dictionary<Material, MatInfo> _matCache = new Dictionary<Material, MatInfo>();
        static Material _statsMat;

        /// <summary>
        /// A texture's statistics as the shader sees it: 256 x 256 samples of its full-size level (a lower level would average
        /// the relief out of a normal map), sRGB decoded to linear, normal maps unpacked. Mode 0 colour (tinted by
        /// <paramref name="tint"/>), 1 normal map, 2 channel means.
        /// </summary>
        static TexStats Stats(Texture t, int mode, Color tint)
        {
            if (t == null || t.dimension != TextureDimension.Tex2D) return null;
            if (_texCache.TryGetValue((t, mode, tint), out var s)) return s;
            if (_statsMat == null) _statsMat = new Material(Shader.Find("Hidden/FPSKit/RealismTexStats"));
            const int N = 256;
            _statsMat.SetFloat("_Mode", mode == 1 ? 1f : 0f);
            var rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt, _statsMat);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);

            s = new TexStats { w = t.width, h = t.height, format = t is Texture2D t2 ? t2.format.ToString() : t.graphicsFormat.ToString() };
            if (mode == 1)
            {
                var tilt = new float[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var n = new Vector3(px[i].r * 2f - 1f, px[i].g * 2f - 1f, px[i].b * 2f - 1f).normalized;
                    tilt[i] = Mathf.Acos(Mathf.Clamp01(n.z)) * Mathf.Rad2Deg;
                }
                Array.Sort(tilt);
                s.tiltMean = tilt.Average();
                s.tiltP90 = tilt[(int)(tilt.Length * 0.9f)];
            }
            else
            {
                var lum = new float[px.Length];
                double r = 0, g = 0, b = 0, a = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = new Color(px[i].r * tint.r, px[i].g * tint.g, px[i].b * tint.b, px[i].a);
                    lum[i] = Lum(c);
                    r += c.r; g += c.g; b += c.b; a += px[i].a;
                }
                int n = px.Length;
                s.meanColour = new Color((float)(r / n), (float)(g / n), (float)(b / n));
                s.r = (float)(r / n); s.g = (float)(g / n); s.a = (float)(a / n);
                s.mean = lum.Average();
                s.std = Mathf.Sqrt(lum.Select(l => (l - s.mean) * (l - s.mean)).Average());
                // Inside 4 x 4 blocks of samples: a photograph varies at every scale, a palette of flat swatches does not.
                double fineSum = 0; int blocks = 0;
                for (int by = 0; by < N; by += 4)
                    for (int bx = 0; bx < N; bx += 4)
                    {
                        double bs = 0, bq = 0;
                        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) { double l = lum[(by + y) * N + bx + x]; bs += l; bq += l * l; }
                        double bm = bs / 16;
                        fineSum += Math.Sqrt(Math.Max(0, bq / 16 - bm * bm));
                        blocks++;
                    }
                s.fine = s.mean > 1e-4f ? (float)(fineSum / blocks / s.mean) : 0f;
                Array.Sort(lum);
                s.p2 = lum[(int)(n * 0.02f)]; s.p50 = lum[n / 2]; s.p98 = lum[(int)(n * 0.98f)];
            }
            _texCache[(t, mode, tint)] = s;
            return s;
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static float Num(Material m, string p, float fallback) => m.HasProperty(p) ? m.GetFloat(p) : fallback;

        /// <summary>The first kind with a word that starts one of the name's words ("wood" in "wooden", but not "arm" in
        /// "farm": the first audit called the farm soil skin). Names split at punctuation, digits and lowerUpper.</summary>
        static string ClassOf(params string[] names)
        {
            foreach (var name in names)
            {
                var tokens = Tokens(name ?? "");
                foreach (bool weak in new[] { false, true })
                    foreach (var t in tokens)
                        foreach (var c in Classes)
                            if (c.words.Any(w => (w[0] == '~') == weak && Matches(t, weak ? w.Substring(1) : w))) return c.cls;
            }
            return "unknown";
        }

        /// <summary>A name's word matches a kind's word if it starts with it ("wooden" for "wood"), or, for a word marked =,
        /// only if it is it ("rust", not "rustic").</summary>
        static bool Matches(string token, string word) => word[0] == '=' ? token == word.Substring(1) : token.StartsWith(word);

        static List<string> Tokens(string s)
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            return System.Text.RegularExpressions.Regex.Split(spaced.ToLowerInvariant(), "[^a-z]+").Where(t => t.Length > 0).ToList();
        }

        static (string cls, string[] words, float aMin, float aMax, float sMin, float sMax, bool metal) ClassData(string cls) =>
            Classes.FirstOrDefault(c => c.cls == cls);

        static readonly string[] LampWords = { "lamp", "light", "bulb", "glow", "neon", "screen", "led", "lantern", "emissive", "fire", "flame", "lava" };

        static MatInfo Measure(Material m, string itemName)
        {
            if (_matCache.TryGetValue(m, out var mi)) return mi;
            mi = new MatInfo { mat = m, shader = m.shader != null ? m.shader.name : "(none)" };
            string sh = mi.shader.ToLowerInvariant();
            mi.lit = !(sh.Contains("unlit") || sh.StartsWith("sprites/") || sh.StartsWith("ui/") || sh.Contains("particles/") && !sh.Contains("lit"));
            mi.transparent = Num(m, "_Surface", 0f) > 0.5f || m.renderQueue >= 2900;
            mi.alphaClip = Num(m, "_AlphaClip", 0f) > 0.5f;
            mi.cull = Num(m, "_Cull", 2f);
            mi.baseMap = Tex(m, "_BaseMap") ?? Tex(m, "_MainTex");
            mi.baseColour = (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white).linear;
            string stProp = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            mi.st = m.HasProperty(stProp) ? new Vector4(m.GetTextureScale(stProp).x, m.GetTextureScale(stProp).y, m.GetTextureOffset(stProp).x, m.GetTextureOffset(stProp).y) : new Vector4(1, 1, 0, 0);
            mi.normalMap = Tex(m, "_BumpMap") ?? Tex(m, "_NormalMap");
            mi.bumpScale = Num(m, "_BumpScale", 1f);
            mi.maskMap = Tex(m, "_MetallicGlossMap") ?? Tex(m, "_SpecGlossMap");
            mi.aoMap = Tex(m, "_OcclusionMap");
            mi.emissionMap = Tex(m, "_EmissionMap");
            mi.detailAlbedo = Tex(m, "_DetailAlbedoMap");
            mi.detailNormal = Tex(m, "_DetailNormalMap");
            mi.smoothness = Num(m, "_Smoothness", Num(m, "_Glossiness", 0.5f));
            mi.metallic = Num(m, "_Metallic", 0f);
            mi.emissive = m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor");
            if (mi.emissive)
            {
                var e = m.GetColor("_EmissionColor").linear;
                mi.emissionLum = Lum(e);
                if (mi.emissionLum < 1e-3f) mi.emissive = false;
            }
            mi.albedo = Stats(mi.baseMap, 0, mi.baseColour);
            mi.normal = Stats(mi.normalMap, 1, Color.white);
            mi.mask = Stats(mi.maskMap, 2, Color.white);
            mi.ao = Stats(mi.aoMap, 2, Color.white);
            if (sh.Contains("autodesk interactive"))
            {
                // Roughness, not smoothness: _Glossiness is the roughness, _SpecGlossMap its map (red), each behind a toggle.
                bool roughMap = Num(m, "_UseRoughnessMap", 0f) > 0.5f && Tex(m, "_SpecGlossMap") != null;
                bool metalMap = Num(m, "_UseMetallicMap", 0f) > 0.5f && Tex(m, "_MetallicGlossMap") != null;
                var rough = roughMap ? Stats(Tex(m, "_SpecGlossMap"), 2, Color.white) : null;
                mi.smoothness = 1f - (rough != null ? rough.r : Num(m, "_Glossiness", 0.5f));
                mi.mask = metalMap ? Stats(Tex(m, "_MetallicGlossMap"), 2, Color.white) : rough;
                mi.maskMap = metalMap ? Tex(m, "_MetallicGlossMap") : roughMap ? Tex(m, "_SpecGlossMap") : null;
                mi.metallic = metalMap ? mi.mask.r : Num(m, "_Metallic", 0f);
                if (Num(m, "_UseNormalMap", 1f) < 0.5f) { mi.normalMap = null; mi.normal = null; }
                if (Num(m, "_UseAoMap", 1f) < 0.5f) { mi.aoMap = null; mi.ao = null; }
            }
            else if (mi.mask != null)
            {
                if (Num(m, "_WorkflowMode", 1f) < 0.5f && Tex(m, "_SpecGlossMap") != null)
                    // URP Lit, specular: the map is the specular colour and its alpha the smoothness; no metallic.
                    { mi.metallic = 0f; mi.smoothness = (Num(m, "_SmoothnessTextureChannel", 0f) > 0.5f ? mi.albedo?.a ?? 1f : mi.mask.a) * Num(m, "_Smoothness", 1f); }
                else if (Tex(m, "_MetallicGlossMap") != null)
                    // URP Lit, metallic: metallic from the map's red, smoothness from its alpha (or the albedo's) times the slider.
                    { mi.metallic = mi.mask.r; mi.smoothness = (Num(m, "_SmoothnessTextureChannel", 0f) > 0.5f ? mi.albedo?.a ?? 1f : mi.mask.a) * Num(m, "_Smoothness", 1f); }
            }
            string words = $"{m.name} {(mi.baseMap != null ? mi.baseMap.name : "")} {itemName}";
            mi.cls = ClassOf(m.name, mi.baseMap != null ? mi.baseMap.name : null, itemName);
            var tokens = Tokens(words);
            mi.lampLike = LampWords.Any(w => tokens.Any(t => t.StartsWith(w)));
            _matCache[m] = mi;
            return mi;
        }

        static void MeasureMaterials(List<Item> items)
        {
            _matCache.Clear();
            foreach (var it in items)
            {
                if (it.terrainLayer != null) { it.mats.Add(MeasureLayer(it)); it.cls = it.mats[0].cls; continue; }
                if (it.skyMaterial != null) { it.mats.Add(Measure(it.skyMaterial, it.name)); it.mats[0].cls = it.cls = "sky"; continue; }
                foreach (var mat in it.members.Select(m => m.mat).Distinct()) it.mats.Add(Measure(mat, it.name));
                // The item's class: its biggest material's.
                var main = it.mats.OrderByDescending(mm => it.matArea.TryGetValue(mm.mat, out var a) ? a : 0).FirstOrDefault();
                if (main != null) it.cls = main.cls;

                // Texels per metre and repeats, per member, area-weighted.
                double repW = 0, repA = 0;
                foreach (var m in it.members)
                {
                    if (m.lod != 0) continue;
                    var mi = Measure(m.mat, it.name);
                    var subs = Geometry(m.mesh);
                    if (m.sub >= subs.Length || mi.baseMap == null) continue;
                    var g = subs[m.sub];
                    float k = UniformScale(m.toWorld);
                    double world = g.area * k * k;
                    if (world < 1e-6 || g.uvArea < 1e-9) continue;
                    double texels = g.uvArea * Mathf.Abs(mi.st.x * mi.st.y) * mi.baseMap.width * mi.baseMap.height;
                    double density = Math.Sqrt(texels / world);
                    it.texelWeighted += density * world;
                    it.texelArea += world;
                    repW += g.uvSpanP95 * Mathf.Max(Mathf.Abs(mi.st.x), Mathf.Abs(mi.st.y)) * world;
                    repA += world;
                }
                it.repeatsP95 = repA > 0 ? repW / repA : 0;
            }
        }

        /// <summary>A terrain layer as a material: its textures, its tile size, URP terrain's smoothness and metallic.</summary>
        static MatInfo MeasureLayer(Item it)
        {
            var l = it.terrainLayer;
            var mi = new MatInfo
            {
                shader = it.terrain != null && it.terrain.materialTemplate != null ? it.terrain.materialTemplate.shader.name : "terrain (built-in)",
                lit = true, baseMap = l.diffuseTexture, normalMap = l.normalMapTexture, maskMap = l.maskMapTexture,
                bumpScale = l.normalScale, smoothness = l.smoothness, metallic = l.metallic,
                baseColour = Color.white, st = new Vector4(1f / Mathf.Max(0.01f, l.tileSize.x), 1f / Mathf.Max(0.01f, l.tileSize.y), 0, 0),
            };
            mi.albedo = Stats(mi.baseMap, 0, Color.white);
            mi.normal = Stats(mi.normalMap, 1, Color.white);
            mi.mask = Stats(mi.maskMap, 2, Color.white);
            if (mi.mask != null) mi.smoothness = mi.mask.a;   // URP terrain: the mask map's alpha is the smoothness
            mi.cls = ClassOf(l.name, l.diffuseTexture != null ? l.diffuseTexture.name : null);
            if (mi.cls == "unknown") mi.cls = "sand";
            if (mi.baseMap != null)
            {
                it.texelWeighted = mi.baseMap.width / Mathf.Max(0.01f, l.tileSize.x);
                it.texelArea = 1;
                // Across one view of open ground (60 m) the layer's tile repeats this often.
                it.repeatsP95 = 60f / Mathf.Max(0.01f, l.tileSize.x);
            }
            return mi;
        }

        // ------------------------------------------------------------------
        // Wall thickness
        // ------------------------------------------------------------------
        /// <summary>
        /// Up to 24 spots on each object with a collider: a cast from just behind the surface, straight in, either meets the
        /// surface's back within a metre (the wall has that thickness) or meets nothing (a shell, a face with nothing behind).
        /// </summary>
        static void MeasureThickness(List<Item> items)
        {
            bool was = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            var rng = new System.Random(11);
            try
            {
                foreach (var it in items)
                {
                    var ms = it.members.Where(m => m.renderer != null && m.hasCollider && m.lod == 0).ToList();
                    if (ms.Count == 0) continue;
                    for (int k = 0; k < 24; k++)
                    {
                        var m = ms[rng.Next(ms.Count)];
                        var subs = Geometry(m.mesh);
                        if (m.sub >= subs.Length || subs[m.sub].samples.Count == 0) continue;
                        var (c, n, area) = subs[m.sub].samples[rng.Next(subs[m.sub].samples.Count)];
                        Vector3 p = m.toWorld.MultiplyPoint3x4(c), nw = m.toWorld.MultiplyVector(n).normalized;
                        if (Mathf.Abs(nw.y) > 0.7f) continue;   // walls only: a floor's "thickness" is the ground under it
                        it.thickSamples++;
                        if (Physics.Raycast(p - nw * 0.01f, -nw, out var hit, 1f, ~0, QueryTriggerInteraction.Ignore)) it.thickness.Add(hit.distance + 0.01f);
                        else it.shellSamples++;
                    }
                }
            }
            finally { Physics.queriesHitBackfaces = was; }
        }

        // ------------------------------------------------------------------
        // Rating
        // ------------------------------------------------------------------
        static void Rate(Item it)
        {
            it.reasons.Clear();
            it.shape = it.surface = it.light = Verdict.Real;
            bool near = it.minDist < NearMetres;
            bool building = it.source == "generated mesh";
            bool terrain = it.terrainLayer != null, sky = it.skyMaterial != null, weapon = it.source == "player weapon";
            if (weapon) near = true;   // a hand's length from the eye, always on screen

            void Mark(ref Verdict v, Verdict to, string tag, string why)
            {
                if (to > v) v = to;
                it.reasons.Add($"[{tag}] {(to == Verdict.Fake ? "FAKE" : to == Verdict.NeedsWork ? "needs work" : "uncertain")}: {why}");
            }

            // ---- the sky: its photograph's sharpness against the screen's ----
            if (sky && it.mats.Count > 0 && it.mats[0].baseMap != null)
            {
                float perDegree = it.mats[0].baseMap.width / 360f, screen = 1920f / (2f * Mathf.Atan(Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad) * 16f / 9f) * Mathf.Rad2Deg);
                if (perDegree < screen)
                    Mark(ref it.surface, Verdict.Uncertain, "surface", $"the sky's panorama has {perDegree:0} pixels per degree where a 1080p screen shows {screen:0}: soft if it has clouds or detail, unseen if it is clear");
            }
            // An overlay (weathering, grime) is as real as the wall it lies on: judged in the walls' close-ups.
            if (it.cls == "decal") Mark(ref it.light, Verdict.Uncertain, "light", "an overlay over walls: judged with the walls under it (their close-ups show it)");

            // ---- shape ----
            if (sky) { }
            else if (it.primitive) Mark(ref it.shape, near ? Verdict.Fake : Verdict.NeedsWork, "shape", "a Unity primitive (cube, cylinder, plane...)");
            else if (!terrain)
            {
                if (it.plainBox && near && !building) Mark(ref it.shape, Verdict.Fake, "shape", "a single plain box (8 corners, 12 triangles) seen up close");
                else if (it.BoxShare >= BoxShareFake && near && !building) Mark(ref it.shape, Verdict.Fake, "shape", $"{it.BoxShare:P0} of its surface is plain boxes");
                else if (it.BoxShare >= 0.25) Mark(ref it.shape, Verdict.NeedsWork, "shape", $"{it.BoxShare:P0} of its surface is plain boxes");
                // Panes, overlays and ground surfaces (a road's edge is the kerb's) have no corners of their own to round.
                bool cornerless = it.cls is "glass" or "water" or "decal" or "asphalt" or "sand";
                if (!cornerless && it.bevelLen + it.sharpLen > 1 && it.BevelShare < BevelShareMin)
                    Mark(ref it.shape, Verdict.NeedsWork, "shape", $"edges perfectly sharp: {it.BevelShare:P1} of {it.bevelLen + it.sharpLen:0} m of hard edge is bevelled");
                if (weapon && it.assetPath != ArmsPrefab && it.tris < HeldTrisMin)
                    Mark(ref it.shape, Verdict.NeedsWork, "shape", $"low-poly for a gun held half a metre from the eye: {it.tris:N0} triangles (first-person guns run 20-60k)");
                if (!building && near && it.area > 0.5 && it.TrisPerM2 < PropTrisPerM2Min)
                    Mark(ref it.shape, Verdict.NeedsWork, "shape", $"low-poly: {it.TrisPerM2:0} triangles per m2");
                // Thickness is reported, not rated: a building with no inside is a shell everywhere but at its openings,
                // whose reveals are what is seen (the close-up's wireframe shows them).
            }

            // ---- surface and light, per material, weighted by its share ----
            double total = it.matArea.Values.Sum();
            foreach (var mi in it.mats)
            {
                double share = total > 0 && mi.mat != null && it.matArea.TryGetValue(mi.mat, out var a) ? a / total : 1.0;
                Verdict Cap(Verdict v) => share < MinorShare && v == Verdict.Fake ? Verdict.NeedsWork : v;
                string who = it.mats.Count > 1 ? $"{(mi.mat != null ? mi.mat.name : "layer")} ({share:P0}): " : "";
                bool clear = mi.cls is "glass" or "water" or "sky" or "decal";
                var cd = ClassData(mi.cls);

                if (!sky)
                {
                    if (!clear && mi.normalMap == null && (mi.baseMap == null || mi.Variation < FlatVariation))
                        Mark(ref it.surface, Cap(Verdict.Fake), "surface", $"{who}flat single colour (albedo variation {mi.Variation:0.000}, no normal map)");
                    else if (!clear && mi.normalMap == null && mi.baseMap != null && mi.Fine < FineVariationMin)
                        Mark(ref it.surface, Cap(Verdict.Fake), "surface", $"{who}flat colour blocks: a palette texture ({mi.Variation:0.00} across it, {mi.Fine:0.000} inside any small patch), no normal map: every face one colour");
                    else
                    {
                        if (!clear && mi.normalMap == null && near) Mark(ref it.surface, Cap(Verdict.NeedsWork), "surface", $"{who}no normal map on a near surface");
                        if (mi.normalMap != null && mi.Tilt < NormalTiltMin) Mark(ref it.surface, Cap(Verdict.NeedsWork), "surface", $"{who}normal map too weak to show relief (mean tilt {mi.Tilt:0.0} deg)");
                        if (!clear && mi.baseMap == null) Mark(ref it.surface, Cap(Verdict.NeedsWork), "surface", $"{who}no albedo texture (colour only)");
                    }
                }

                // light
                if (sky) continue;
                if (!mi.lit) { Mark(ref it.light, Cap(Verdict.Fake), "light", $"{who}unlit shader ({mi.shader}): the same brightness in sun, shade and indoors"); continue; }
                if (mi.emissive && !mi.lampLike) Mark(ref it.light, Cap(Verdict.Fake), "light", $"{who}emits light ({mi.emissionLum:0.00}): glows in shade and indoors");
                else if (mi.emissive && (mi.emissionLum < 0.3f || mi.emissionLum > 30f)) Mark(ref it.light, Verdict.NeedsWork, "light", $"{who}lamp emission {mi.emissionLum:0.00} outside 0.3-30");
                if (clear) continue;
                if (mi.AlbedoP98 > AlbedoValidMax) Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}albedo up to {mi.AlbedoP98:0.00}: whiter than any real surface (URP validation max {AlbedoValidMax})");
                if (mi.AlbedoP2 < AlbedoValidMin) Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}albedo down to {mi.AlbedoP2:0.000}: blacker than any real surface (URP validation min {AlbedoValidMin})");
                if (cd.cls == null || mi.cls == "unknown") { Mark(ref it.light, Verdict.Uncertain, "light", $"{who}material kind not recognised from its name: albedo and smoothness not checked against a real range"); continue; }
                if (mi.AlbedoMean < cd.aMin || mi.AlbedoMean > cd.aMax)
                    Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}albedo {mi.AlbedoMean:0.00} outside real {mi.cls} ({cd.aMin:0.00}-{cd.aMax:0.00})");
                if (mi.smoothness < cd.sMin - 0.02f || mi.smoothness > cd.sMax + 0.02f)
                    Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}smoothness {mi.smoothness:0.00} outside real {mi.cls} ({cd.sMin:0.00}-{cd.sMax:0.00})");
                if (!cd.metal && mi.cls != "gun finish" && mi.metallic > 0.1f) Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}metallic {mi.metallic:0.00} on {mi.cls}");
                if (cd.metal && mi.metallic < 0.5f) Mark(ref it.light, Verdict.Uncertain, "light", $"{who}named as bare metal but metallic {mi.metallic:0.00} (painted?)");
                if (mi.aoMap == null && mi.smoothness > 0.5f) Mark(ref it.light, Cap(Verdict.NeedsWork), "light", $"{who}smooth ({mi.smoothness:0.00}) with no occlusion map: crevices and corners shine");
            }
            if (!sky && !terrain && !weapon)
            {
                var rs = it.members.Where(m => m.renderer != null).Select(m => m.renderer).Distinct().ToList();
                int noShadow = rs.Count(r => !r.receiveShadows);
                if (noShadow > 0) Mark(ref it.light, near ? Verdict.Fake : Verdict.NeedsWork, "light", $"{noShadow} of {rs.Count} renderers do not receive shadows: they stay lit in shade");
                int noProbes = rs.Count(r => !r.gameObject.isStatic && r.lightProbeUsage == LightProbeUsage.Off);
                if (noProbes > 0) Mark(ref it.light, Verdict.NeedsWork, "light", $"{noProbes} of {rs.Count} moving renderers use no light probes: lit by the open sky indoors");
            }
            if (!sky && it.texelArea > 0)
            {
                float wanted = weapon ? TexelHeld : TexelNear;
                if (near && it.Texel < wanted) Mark(ref it.surface, Verdict.NeedsWork, "surface", $"{it.Texel:0} texels per metre, seen within {it.minDist:0.0} m (wanted {wanted:0})");
                else if (it.Texel < TexelFar) Mark(ref it.surface, Verdict.NeedsWork, "surface", $"{it.Texel:0} texels per metre (a blur at any distance; wanted {TexelFar:0} or more)");
                bool varied = it.vertexColourVariation || it.mats.Any(m => m.detailAlbedo != null || m.detailNormal != null);
                if (it.repeatsP95 >= RepeatsMax && !varied)
                    Mark(ref it.surface, Verdict.NeedsWork, "surface", $"one texture repeats {it.repeatsP95:0} times across a face with nothing to vary it (no detail map, no vertex colour)");
            }
            foreach (var line in it.measured) if (line.StartsWith("!")) Mark(ref it.light, line.StartsWith("!!") ? Verdict.Fake : Verdict.NeedsWork, "light test", line.TrimStart('!'));

            it.overall = (Verdict)Math.Max((int)it.shape, Math.Max((int)it.surface, (int)it.light));
        }
    }
}
#endif
