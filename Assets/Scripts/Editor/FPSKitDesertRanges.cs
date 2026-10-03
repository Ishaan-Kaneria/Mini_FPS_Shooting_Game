#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The far horizon of the desert: continuous jagged mountain ranges in three hazy layers
    /// instead of a ring of smooth single-colour buttes.
    ///
    /// Ishaan, 2026-10-03: "as we see far we see yellow low poly mountain and not far real
    /// desert thing". Two things made them read as props. They were a handful of separate
    /// smooth solids, so the horizon was a row of lumps with sky between them; and each was
    /// one flat colour, so nothing said "far away" except the fog. A real range is one
    /// continuous broken skyline, bedded in strata, and each layer behind another is paler
    /// and bluer than the one in front -- aerial perspective is most of what distance looks
    /// like.
    ///
    /// Each layer is a single ring mesh: a ridged-noise skyline, a few hundred columns round,
    /// sunk below the ground at its foot. No collider, backdrop layer, desert theme only.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private struct RangeLayer
        {
            public float Radius, MinHeight, MaxHeight, Tiling;
            public Color Colour;
            public int Seed;
        }

        private static void BuildDesertRanges(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("DistantRanges").transform;
            group.SetParent(root, false);

            // Near to far: warmer and darker in front, paler and bluer behind.
            var layers = new[]
            {
                new RangeLayer { Radius = 640f,  MinHeight = 38f,  MaxHeight = 120f, Tiling = 0.010f, Seed = 11,
                                 Colour = new Color(0.40f, 0.31f, 0.26f) },
                new RangeLayer { Radius = 900f,  MinHeight = 70f,  MaxHeight = 190f, Tiling = 0.008f, Seed = 23,
                                 Colour = new Color(0.50f, 0.42f, 0.38f) },
                new RangeLayer { Radius = 1180f, MinHeight = 100f, MaxHeight = 260f, Tiling = 0.006f, Seed = 37,
                                 Colour = new Color(0.60f, 0.55f, 0.55f) }
            };

            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var mat = MakeDetailMaterial($"Range{i}", l.Colour, "Rock", 1f, 0.04f, 0f, 1.2f);
                MeshObject(group, $"Range{i}", RangeMesh(l), mat, Vector3.zero, Quaternion.identity,
                           Vector3.one, layer, "Untagged", collider: false);
            }
        }

        /// <summary>
        /// A ring of columns round the origin. Height is a ridged fbm (sharp crests, broad
        /// saddles) times a slow envelope so the range swells and dips instead of running
        /// at one height, with a little jitter per column so the skyline is broken. The
        /// rows run from well under the apron up to the crest; the lower rows are steep and
        /// the upper rows rougher, so the strata texture bands across the face.
        /// </summary>
        private static Mesh RangeMesh(RangeLayer l)
        {
            const int columns = 420;
            const int rows = 7;

            var verts = new Vector3[(columns + 1) * (rows + 1)];
            var uvs = new Vector2[verts.Length];

            for (int c = 0; c <= columns; c++)
            {
                float t = (c % columns) / (float)columns;
                float a = t * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), sz = Mathf.Sin(a);

                // Sampled on the circle, so the ring closes without a seam.
                float ridged = 1f - Mathf.Abs(Fbm2(cx * 3.1f + 10f, sz * 3.1f + 10f, l.Seed, 5)) * 2.6f;
                ridged = Mathf.Pow(Mathf.Clamp01(ridged), 0.9f);
                float envelope = 0.45f + 0.55f * (Fbm2(cx * 1.2f + 40f, sz * 1.2f + 40f, l.Seed + 7, 3) + 0.5f);
                float jitter = 1f + Fbm2(cx * 26f, sz * 26f, l.Seed + 3, 2) * 0.28f;

                float height = Mathf.Lerp(l.MinHeight, l.MaxHeight, Mathf.Clamp01(ridged * envelope)) * jitter;

                // <b>A gap where the river runs out.</b> The river carries on across the apron, so a range
                // standing across its line reads as a mountain passing over the water (Ishaan, 2026-10-03).
                // Along the river's axis -- the ring points far north and far south of the arena, within a
                // couple of hundred metres of its x -- the range is brought down to low foothills, and
                // rises back to full height further out either side.
                if (Mathf.Abs(sz) > 0.5f)
                {
                    float away = Mathf.Abs(cx * l.Radius - _theme.hazardOffset);
                    height *= Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, (away - 90f) / 220f));
                }

                for (int r = 0; r <= rows; r++)
                {
                    float f = r / (float)rows;

                    // The face leans back as it rises: the foot is at the ring radius, the
                    // crest a little further out, which is what a slope does.
                    float radius = l.Radius + f * height * 0.55f;
                    float y = Mathf.Lerp(-30f, height, f);

                    // Rougher higher up: the crest is where the skyline breaks.
                    float rough = Fbm2(cx * 40f + r * 3.1f, sz * 40f - r * 2.3f, l.Seed + 19, 2) * 14f * f;

                    verts[c * (rows + 1) + r] = new Vector3(cx * (radius + rough), y, sz * (radius + rough));
                    uvs[c * (rows + 1) + r] = new Vector2(t * 2f * Mathf.PI * l.Radius * l.Tiling * 6f,
                                                          y * l.Tiling * 6f);
                }
            }

            var tris = new List<int>(columns * rows * 6);
            for (int c = 0; c < columns; c++)
                for (int r = 0; r < rows; r++)
                {
                    int a0 = c * (rows + 1) + r, a1 = a0 + 1;
                    int b0 = (c + 1) * (rows + 1) + r, b1 = b0 + 1;
                    tris.Add(a0); tris.Add(a1); tris.Add(b1);
                    tris.Add(a0); tris.Add(b1); tris.Add(b0);
                }

            // Faces towards the middle, where the player is. Decided from the first
            // triangle rather than hoped for -- the butte code records what happens when
            // a closed shape is wound inside out.
            Vector3 n0 = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
            Vector3 toCentre = -new Vector3(verts[tris[0]].x, 0f, verts[tris[0]].z);
            if (Vector3.Dot(n0, toCentre) < 0f)
                for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }

            var mesh = new Mesh { name = $"Range_{l.Seed}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
