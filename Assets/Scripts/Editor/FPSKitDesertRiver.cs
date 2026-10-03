#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What lives along the desert river: reeds in the shallows, boulders and pebbles on the
    /// waterline, a broken line of foam where the water meets the bank, and driftwood.
    ///
    /// Ishaan, 2026-10-03: "focus on the river (improve the quality)". What was there was a
    /// flat sheet of green between two bare canyon walls. A river reads as one at the edge:
    /// the waterline is where the eye goes, and it was the one place with nothing on it.
    ///
    /// <b>The waterline is measured, not computed.</b> The canyon is carved by several
    /// passes, so there is no formula for where its wall crosses the water surface. After
    /// the gorge exists, a ray is cast sideways from the centre line at the water's height
    /// and the first thing it hits is the shore. Everything here is placed from that hit,
    /// and a row that finds no shore (a bridge pier in the way, a gap) is skipped.
    ///
    /// No colliders and the backdrop layer: it is looked at and walked past, never in the
    /// way of the bake, a bullet or the kill volume.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static bool IsDesertArena()
            => _theme.openZone && _theme.floorDetail == "Sand" && !_theme.volcanicZone
               && !_theme.snowZone && !_theme.parkZone && _theme.hazard == LevelTheme.Hazard.River;

        private static void BuildRiverLife(Transform root, int layer, System.Random rng, float half)
        {
            Physics.SyncTransforms();

            var group = new GameObject("RiverLife").transform;
            group.SetParent(root, false);

            var reedMat = MakeMaterial("Reed", new Color(0.36f, 0.42f, 0.20f), 0.1f, 0f);
            var reedDry = MakeMaterial("ReedDry", new Color(0.58f, 0.50f, 0.28f), 0.08f, 0f);
            var foamMat = MakeMaterial("Foam", new Color(0.86f, 0.86f, 0.80f), 0.2f, 0f);
            var driftMat = MakeMaterial("Driftwood", new Color(0.46f, 0.40f, 0.32f), 0.08f, 0f);

            var reeds = new MeshBuild { UVScale = 1f };
            var dry = new MeshBuild { UVScale = 1f };
            var foam = new MeshBuild { UVScale = 1f };
            var drift = new MeshBuild { UVScale = 0.8f };

            float y = WaterSurfaceY;
            float reach = _theme.hazardWidth * 0.5f + 60f;
            int rocks = 0;

            for (int side = -1; side <= 1; side += 2)
            {
                bool havePrev = false;
                Vector3 prev = default;

                for (float z = -half - 40f; z < half + 40f; z += 3.5f)
                {
                    // Piers and legs are not the shore; leave the crossings alone.
                    if (NearCrossing(z, _theme.bridgeWidth * 0.5f + 7f)) { havePrev = false; continue; }

                    float centre = GorgeCentreAt(z);
                    var origin = new Vector3(centre, y + 0.35f, z);

                    // Sideways from the middle of the water until the wall.
                    if (!Physics.Raycast(origin, new Vector3(side, 0f, 0f), out var hit, reach, ~0,
                                         QueryTriggerInteraction.Ignore))
                    {
                        havePrev = false;
                        continue;
                    }

                    // A pier or a leg is not a shore; the real one is the wall of the canyon.
                    if (Mathf.Abs(hit.normal.y) > 0.92f && hit.distance < 4f) { havePrev = false; continue; }

                    var shore = new Vector3(hit.point.x, y, hit.point.z);
                    float inward = -side;    // towards the water

                    // ---- foam: a broken ribbon, skipped at random and wider where it is rough ----
                    if (havePrev && rng.NextDouble() < 0.72)
                    {
                        float w0 = Rand(rng, 0.25f, 0.8f);
                        var a0 = prev + new Vector3(inward * 0.05f, 0.04f, 0f);
                        var b0 = shore + new Vector3(inward * 0.05f, 0.04f, 0f);
                        var a1 = prev + new Vector3(inward * w0, 0.04f, 0f);
                        var b1 = shore + new Vector3(inward * w0, 0.04f, 0f);
                        // Wound so it faces up: along +z, then across towards the water.
                        if (side < 0) foam.Quad(a0, a1, b1, b0); else foam.Quad(a0, b0, b1, a1);
                    }

                    prev = shore;
                    havePrev = true;

                    // ---- reeds: clumps in the shallows, a few at a time, more on the quiet bends ----
                    if (rng.NextDouble() < 0.34)
                    {
                        var at = shore + new Vector3(inward * Rand(rng, 0.2f, 1.6f), -0.1f, Rand(rng, -1f, 1f));
                        var target = rng.NextDouble() < 0.78 ? reeds : dry;
                        int blades = rng.Next(7, 15);

                        for (int b = 0; b < blades; b++)
                        {
                            var foot = at + new Vector3(Rand(rng, -0.45f, 0.45f), 0f, Rand(rng, -0.45f, 0.45f));
                            float len = Rand(rng, 1.1f, 2.5f);
                            var tip = foot + new Vector3(Rand(rng, -0.4f, 0.4f), len, Rand(rng, -0.4f, 0.4f));
                            target.Tube(foot, tip, 0.028f, 0.004f, 4);
                        }
                    }

                    // ---- boulders and pebbles on the waterline ----
                    if (rocks < 140 && rng.NextDouble() < 0.16)
                    {
                        float size = rng.NextDouble() < 0.2 ? Rand(rng, 1.4f, 3.2f) : Rand(rng, 0.35f, 1.1f);
                        var at = shore + new Vector3(inward * Rand(rng, -0.3f, 1.4f), -size * 0.18f, Rand(rng, -1.2f, 1.2f));

                        MeshObject(group, "ShoreRock", ButteMesh(rng.Next(1, 40), sides: 7, levels: 3), _rockWetMat,
                                   at, Quaternion.Euler(Rand(rng, -8f, 8f), Rand(rng, 0f, 360f), Rand(rng, -8f, 8f)),
                                   new Vector3(size, size * Rand(rng, 0.45f, 0.8f), size * Rand(rng, 0.7f, 1.2f)),
                                   layer, "Untagged", collider: false);
                        rocks++;
                    }

                    // ---- driftwood, caught on the edge now and then ----
                    if (rng.NextDouble() < 0.025)
                    {
                        var at = shore + new Vector3(inward * Rand(rng, 0.4f, 1.8f), 0.12f, 0f);
                        float yaw = Rand(rng, 0f, Mathf.PI);
                        float len = Rand(rng, 2.2f, 5.5f);
                        var d = new Vector3(Mathf.Sin(yaw), Rand(rng, -0.06f, 0.1f), Mathf.Cos(yaw)) * len * 0.5f;
                        drift.Tube(at - d, at + d, 0.16f, 0.11f, 6);
                        if (rng.NextDouble() < 0.6)
                            drift.Tube(at, at + new Vector3(Rand(rng, -0.8f, 0.8f), Rand(rng, 0.5f, 1.1f), Rand(rng, -0.8f, 0.8f)),
                                       0.06f, 0.02f, 4);
                    }
                }
            }

            void Flush(MeshBuild b, string name, Material m)
            {
                if (b.Triangles.Count == 0) return;
                var go = MeshObject(group, name, b.ToMesh(name), m, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Untagged", collider: false);
                NoStanding(go);
                Hide(go);
            }

            Flush(reeds, "Reeds", reedMat);
            Flush(dry, "ReedsDry", reedDry);
            Flush(foam, "Foam", foamMat);
            Flush(drift, "Driftwood", driftMat);

            Debug.Log($"[FPSKit] river life: {rocks} shore rock(s), {reeds.Triangles.Count / 3} reed tri(s), {foam.Triangles.Count / 3} foam tri(s).");
        }
    }
}
#endif
