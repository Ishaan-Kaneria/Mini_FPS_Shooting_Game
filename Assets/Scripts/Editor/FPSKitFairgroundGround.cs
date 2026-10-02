#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What the ground is wearing. The first two fairgrounds had one dark green for 450 metres, and
    /// that is the largest single reason they did not read as real: ground that is the same everywhere
    /// is a texture, not a place.
    ///
    /// <b>Every kind of mark has a cause, and goes where the cause is.</b> Mud and puddles collect at
    /// the edge of the paving and at the foot of buildings, where feet and water went; leaf litter lies
    /// under the trees that dropped it; moss follows the damp along the walkways; gravel spills over
    /// from the plazas and the service road; and the broad pale and lush patches are what a lawn does
    /// when nobody has watered or mown it for a decade. Each is a flat, irregular polygon laid a few
    /// centimetres over the heightfield, on the backdrop layer, with no collider and nothing the
    /// navigation bake looks at.
    ///
    /// <b>Wound clockwise from above.</b> A mesh wound the other way is invisible from the side you
    /// look at it from; the first fairground's paving was exactly that.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private sealed class DecalCell
        {
            public readonly MeshBuild Mud = new MeshBuild { UVScale = 0.3f };
            public readonly MeshBuild Puddle = new MeshBuild { UVScale = 0.1f };
            public readonly MeshBuild Leaf = new MeshBuild { UVScale = 0.3f };
            public readonly MeshBuild Moss = new MeshBuild { UVScale = 0.3f };
            public readonly MeshBuild Gravel = new MeshBuild { UVScale = 0.3f };
            public readonly MeshBuild Lush = new MeshBuild { UVScale = 0.1f };
            public readonly MeshBuild Dry = new MeshBuild { UVScale = 0.1f };
        }

        private static Material _decalMud, _decalLeaf, _decalMoss, _decalLush, _decalDry;

        private static void ResolveDecalMaterials()
        {
            _decalMud = MakeDetailMaterial("DecalMud", new Color(0.26f, 0.19f, 0.12f), "Adobe", Metres(3f), 0.12f, 0f, 0.5f);
            _decalLeaf = MakeDetailMaterial("DecalLeaf", new Color(0.36f, 0.25f, 0.12f), "Adobe", Metres(2f), 0.05f, 0f, 0.6f);
            _decalMoss = MakeDetailMaterial("DecalMoss", new Color(0.24f, 0.36f, 0.14f), "Adobe", Metres(2f), 0.05f, 0f, 0.6f);
            _decalLush = MakeDetailMaterial("DecalLush", new Color(0.34f, 0.47f, 0.19f), "Adobe", Metres(2.4f), 0.06f, 0f, 0.8f);
            _decalDry = MakeDetailMaterial("DecalDry", new Color(0.43f, 0.40f, 0.22f), "Adobe", Metres(2.4f), 0.05f, 0f, 0.8f);
        }

        /// <summary>An irregular flat polygon on the ground: a fan of triangles with a ragged rim.</summary>
        private static void DecalBlob(MeshBuild b, Vector2 c, float radius, int seed, float lift, int sides = 9, float stretch = 1f)
        {
            float turn = Hash01(seed, 1) * Mathf.PI * 2f;
            var centre = new Vector3(c.x, GroundHeightAt(c.x, c.y) + lift, c.y);

            Vector3 Rim(int i)
            {
                float a = turn + i * Mathf.PI * 2f / sides;
                float r = radius * (0.62f + 0.55f * Hash01(seed, 10 + i));
                float x = Mathf.Cos(a) * r * stretch, z = Mathf.Sin(a) * r / stretch;
                return new Vector3(c.x + x, GroundHeightAt(c.x + x, c.y + z) + lift, c.y + z);
            }

            for (int i = 0; i < sides; i++)
                b.Tri(centre, Rim((i + 1) % sides), Rim(i));
        }

        private static void BuildGroundDecals(Transform root, int backdrop, float half)
        {
            ResolveDecalMaterials();

            var group = new GameObject("GroundDecals").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 4271 + 13);
            var cells = new Dictionary<Vector2Int, DecalCell>();

            DecalCell CellAt(Vector2 p)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / 90f), Mathf.FloorToInt(p.y / 90f));
                if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new DecalCell();
                return cell;
            }

            float limit = half - BermToe - 10f;
            int counts = 0;
            int seed = 1;

            bool Ok(Vector2 p, float r) => Mathf.Abs(p.x) < limit && Mathf.Abs(p.y) < limit && !InsidePlaza(p, -r) && OffSinks(p, r + 1f);

            // ---- along the walkways: mud at the foot-worn edge, moss in the damp, gravel spilled ----
            foreach (var route in _fgRoutes)
            {
                var pts = ResamplePath(route.Points, route.Width >= 8f ? 7f : 5f);
                foreach (var centre in pts)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var i = pts.IndexOf(centre);
                        var next = pts[Mathf.Min(pts.Count - 1, i + 1)]; var prev = pts[Mathf.Max(0, i - 1)];
                        var dir = (next - prev).normalized;
                        if (dir.sqrMagnitude < 0.1f) continue;
                        var across = new Vector2(-dir.y, dir.x) * side;

                        double roll = rng.NextDouble();
                        if (roll < 0.26)
                        {
                            var p = centre + across * (route.Width * 0.5f + Rand(rng, 0.5f, 2.6f));
                            if (!Ok(p, 1f)) continue;
                            float r = Rand(rng, 0.9f, 2.4f);
                            var cell = CellAt(p);
                            DecalBlob(cell.Mud, p, r, seed++, 0.03f, 9, Rand(rng, 0.8f, 1.5f));
                            if (rng.NextDouble() < 0.4) DecalBlob(cell.Puddle, p, r * 0.55f, seed++, 0.045f, 8);
                            counts++;
                        }
                        else if (roll < 0.5)
                        {
                            var p = centre + across * (route.Width * 0.5f + Rand(rng, 2f, 6f));
                            if (!Ok(p, 1.5f)) continue;
                            DecalBlob(CellAt(p).Moss, p, Rand(rng, 1.4f, 3.4f), seed++, 0.025f, 10);
                            counts++;
                        }
                        else if (roll < 0.58 && route.Width >= 5f)
                        {
                            var p = centre + across * (route.Width * 0.5f + Rand(rng, 0.4f, 1.8f));
                            if (!Ok(p, 1f)) continue;
                            DecalBlob(CellAt(p).Gravel, p, Rand(rng, 0.8f, 1.7f), seed++, 0.04f, 8);
                            counts++;
                        }
                    }
                }
            }

            // ---- round the foot of every building and ride: where people stood and the roof ran off ----
            foreach (var k in _fgKeep)
            {
                if (k.z < 5f) continue;
                int n = Mathf.RoundToInt(k.z * 0.9f);
                for (int i = 0; i < n; i++)
                {
                    float a = Rand(rng, 0f, 6.283f);
                    var p = new Vector2(k.x, k.y) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (k.z + Rand(rng, 0.2f, 2.4f));
                    if (!Ok(p, 1f)) continue;

                    var cell = CellAt(p);
                    float r = Rand(rng, 0.9f, 2.2f);
                    if (rng.NextDouble() < 0.6) { DecalBlob(cell.Mud, p, r, seed++, 0.03f, 9, Rand(rng, 0.8f, 1.4f)); if (rng.NextDouble() < 0.3) DecalBlob(cell.Puddle, p, r * 0.5f, seed++, 0.045f, 8); }
                    else DecalBlob(cell.Leaf, p, r, seed++, 0.035f, 8);
                    counts++;
                }
            }

            // ---- under every tree, its own litter and the moss that follows it ----
            foreach (var t in _fgTrunks)
            {
                if (!Ok(t, 1f)) continue;
                var cell = CellAt(t);
                DecalBlob(cell.Leaf, t + new Vector2(Rand(rng, -0.8f, 0.8f), Rand(rng, -0.8f, 0.8f)), Rand(rng, 2.2f, 4.2f), seed++, 0.035f, 10);
                if (rng.NextDouble() < 0.5) DecalBlob(cell.Moss, t + new Vector2(Rand(rng, -2f, 2f), Rand(rng, -2f, 2f)), Rand(rng, 1.2f, 2.6f), seed++, 0.028f, 9);
                counts++;
            }

            // ---- the lawn: broad lush and parched patches, close in tone so they read as a lawn ----
            for (int i = 0; i < 90; i++)
            {
                var p = new Vector2(Rand(rng, -limit, limit), Rand(rng, -limit, limit));
                if (!Ok(p, 6f) || !KeepClear(p, 3f)) continue;

                float r = Rand(rng, 3f, 7f);
                var cell = CellAt(p);
                if (rng.NextDouble() < 0.55) DecalBlob(cell.Lush, p, r, seed++, 0.02f, 13, Rand(rng, 0.7f, 1.6f));
                else DecalBlob(cell.Dry, p, r, seed++, 0.021f, 13, Rand(rng, 0.7f, 1.6f));
                counts++;
            }

            foreach (var cell in cells.Values)
            {
                RideDecor(group, "DecalMud", cell.Mud, _decalMud, false);
                RideDecor(group, "DecalPuddle", cell.Puddle, _fgWater, false);
                RideDecor(group, "DecalLeaf", cell.Leaf, _decalLeaf, false);
                RideDecor(group, "DecalMoss", cell.Moss, _decalMoss, false);
                RideDecor(group, "DecalGravel", cell.Gravel, _fgGravel, false);
                RideDecor(group, "DecalLush", cell.Lush, _decalLush, false);
                RideDecor(group, "DecalDry", cell.Dry, _decalDry, false);
            }

            Debug.Log($"[FPSKit] fairground ground: {counts} patch(es) of mud, leaf, moss, gravel and lawn in {cells.Count} cell(s).");
        }
    }
}
#endif
