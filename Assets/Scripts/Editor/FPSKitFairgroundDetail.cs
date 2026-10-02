#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The last small things: paper and confetti caught on the paving, cracks across it, and
    /// crows on the high places. None of it is cover and none of it is on the bake; each is a
    /// reason to look twice at something that would otherwise be clean.
    ///
    /// <b>Scraps sit on the paving, not the lawn.</b> They are laid above the stone (and above a
    /// plaza's higher stone) so they never z-fight, and they cluster against the kerb side of a walkway,
    /// where wind and feet leave them. <b>Crows perch where a crow would</b>: a gantry, a ridge, a jib,
    /// the top of a tower -- the highest dry places in a park nobody visits.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private static void ScrapQuad(MeshBuild b, Vector3 c, float w, float d, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float z) => c + rot * new Vector3(x, 0f, z);
            b.Quad(P(-w, -d), P(-w, d), P(w, d), P(w, -d));
        }

        private static void Crow(MeshBuild b, Vector3 perch, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => perch + rot * new Vector3(x, y, z);

            b.Tube(P(0f, 0.16f, -0.12f), P(0f, 0.2f, 0.1f), 0.085f, 0.07f, 6);       // body
            b.Tube(P(0f, 0.2f, 0.08f), P(0f, 0.3f, 0.16f), 0.045f, 0.04f, 5);        // neck and head
            b.Tube(P(0f, 0.3f, 0.16f), P(0f, 0.285f, 0.27f), 0.022f, 0.004f, 4);     // beak
            b.Tube(P(0f, 0.17f, -0.1f), P(0f, 0.12f, -0.36f), 0.04f, 0.02f, 4);      // tail
            b.Tube(P(0f, 0.08f, 0f), P(0f, 0f, 0f), 0.012f, 0.012f, 3);              // legs
            b.Tube(P(-0.07f, 0.2f, -0.05f), P(-0.09f, 0.14f, -0.28f), 0.05f, 0.02f, 3);   // folded wings
            b.Tube(P(0.07f, 0.2f, -0.05f), P(0.09f, 0.14f, -0.28f), 0.05f, 0.02f, 3);
        }

        private static void BuildFinalDetails(Transform root, int backdrop, float half)
        {
            var rng = new System.Random(_theme.randomSeed * 5393 + 101);
            var group = new GameObject("Details").transform;
            group.SetParent(root, false);

            var paper = new MeshBuild { UVScale = 0.5f };
            var red = new MeshBuild { UVScale = 0.5f };
            var blue = new MeshBuild { UVScale = 0.5f };
            var cracks = new MeshBuild { UVScale = 0.5f };
            var crows = new MeshBuild { UVScale = 0.5f };
            int scraps = 0, crackCount = 0;

            foreach (var route in _fgRoutes)
            {
                if (route.Width < 3.5f) continue;

                foreach (var c in ResamplePath(route.Points, 2.6f))
                {
                    bool plaza = InsidePlaza(c, 0f);
                    float lift = plaza ? 0.115f : 0.082f;
                    var next = c + Vector2.right * 0.01f;

                    // A drift of paper and confetti, off-centre where the wind left it.
                    if (rng.NextDouble() < 0.28)
                    {
                        var p = c + new Vector2(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)) * route.Width * 0.38f;
                        var set = rng.NextDouble() < 0.5 ? paper : (rng.NextDouble() < 0.5 ? red : blue);
                        int n = 2 + rng.Next(4);
                        for (int i = 0; i < n; i++)
                        {
                            var q = p + new Vector2(Rand(rng, -0.5f, 0.5f), Rand(rng, -0.5f, 0.5f));
                            ScrapQuad(set, OnGround(q, lift), Rand(rng, 0.05f, 0.14f), Rand(rng, 0.04f, 0.1f), Rand(rng, 0f, 360f));
                            scraps++;
                        }
                    }

                    // A crack across the stone: long, thin and not quite straight.
                    if (rng.NextDouble() < 0.16)
                    {
                        var p = c + new Vector2(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)) * route.Width * 0.35f;
                        float yaw = Rand(rng, 0f, 180f);
                        var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        var a = OnGround(p, lift - 0.006f);
                        for (int seg = 0; seg < 3; seg++)
                        {
                            var b = a + Quaternion.Euler(0f, Rand(rng, -30f, 30f), 0f) * dir * Rand(rng, 0.4f, 0.8f);
                            b.y = GroundHeightAt(b.x, b.z) + lift - 0.006f;
                            var mid = (a + b) * 0.5f;
                            ScrapQuad(cracks, mid, 0.014f, Vector3.Distance(a, b) * 0.5f, Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg);
                            a = b;
                        }

                        crackCount++;
                    }
                }
            }

            // ---- crows, on the high dry places ----
            var perches = new List<Vector3>
            {
                new Vector3(-8.6f, 13.0f, _entrancePlan.Centre.y), new Vector3(8.6f, 13.0f, _entrancePlan.Centre.y),
                new Vector3(-5f, 20.7f, _hallPlan.Centre.y), new Vector3(14f, 20.7f, _hallPlan.Centre.y), new Vector3(-22f, 20.7f, _hallPlan.Centre.y),
                new Vector3(_clockPlan.Centre.x, 27f, _clockPlan.Centre.y),
                new Vector3(_maintenancePlan.Centre.x, 22.2f, _maintenancePlan.Centre.y),
            };

            foreach (var ride in _ridePlans)
                if (ride.Kind == 1) perches.Add(new Vector3(ride.Centre.x, 22.2f, ride.Centre.y));

            foreach (var p in perches)
            {
                float g = GroundHeightAt(p.x, p.z);
                int count = 1 + rng.Next(3);
                for (int i = 0; i < count; i++)
                    Crow(crows, new Vector3(p.x + i * 0.45f, g + p.y, p.z + Rand(rng, -0.2f, 0.2f)), Rand(rng, 0f, 360f));
            }

            RideDecor(group, "ScrapPaper", paper, _hhBone, false);
            RideDecor(group, "ScrapRed", red, _parkPaint, false);
            RideDecor(group, "ScrapBlue", blue, _midwayPaintB, false);
            RideDecor(group, "PavingCracks", cracks, _parkDark, false);
            RideDecor(group, "Crows", crows, _parkDark, false);

            Debug.Log($"[FPSKit] fairground details: {scraps} scrap(s), {crackCount} crack(s), {perches.Count} perch(es).");
        }
    }
}
#endif
