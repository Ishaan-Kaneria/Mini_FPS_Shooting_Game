#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Renders a built arena from a handful of fixed viewpoints and writes them out as
    /// PNGs, so what the builder produced can be looked at without opening the editor.
    ///
    /// This exists because a generated scene is write-only from the command line. It is
    /// saved in binary, so it cannot be read; its geometry is procedural, so a
    /// description of the code is not a description of the result; and every structural
    /// check in this repo answers a question about whether the level *works* --
    /// VerifyZone proves the banks are joined and the water is lethal, and would pass
    /// just as happily on an arena where the cliffs were inside out and the sand was
    /// black. A change made for how a level looks has to be checked by looking at it.
    ///
    /// Two things about the render are worth not re-deriving, and both are the same ones
    /// the dashboard's arena previews hit:
    ///
    ///   * <b>Render through the pipeline, not with <c>Camera.Render()</c>.</b> That call
    ///     predates scriptable pipelines; under URP it returns a frame with the skybox
    ///     and essentially no lighting, which reads as "the arena is a black silhouette"
    ///     rather than as "the capture is wrong".
    ///   * <b>Submit twice and keep the second.</b> The first frame after a scene opens
    ///     is drawn with whatever the pipeline had warmed already, so shadows and the
    ///     environment probe land one frame late.
    ///
    /// It needs a real graphics device, so it is run with UNITY_GRAPHICS=1:
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViews \
    ///       -fpskitTheme "Desert Outpost" -fpskitOut /tmp/shots
    /// </summary>
    public static class FPSKitViews
    {
        const int Width = 1024;
        const int Height = 576;

        struct Shot
        {
            public string Name;
            public Vector3 From;
            public Vector3 Look;
            public float Fov;

            /// <summary>
            /// Heights are above the ground under the camera rather than above zero.
            ///
            /// The desert's shots were written for ground near zero, and on a field whose
            /// ground is metres higher they put the camera inside the terrain: what came
            /// back was the sky below the horizon, a smooth brown gradient across the
            /// bottom of the frame that looks entirely like dark ground in shadow.
            /// </summary>
            public bool Grounded;
        }

        /// <summary>
        /// The lowest solid surface under a point, which on a field is the ground itself --
        /// not a deck overhead. Not for shots over the canyon, where the lowest surface is
        /// the river bed; the rim shots are left absolute, because the rim is held at zero.
        /// </summary>
        static float GroundUnder(Vector3 p)
        {
            float best = float.NaN, ground = float.NaN;

            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 600f, p.z), Vector3.down, 1200f,
                                                   ~0, QueryTriggerInteraction.Ignore))
            {
                if (float.IsNaN(best) || hit.point.y < best) best = hit.point.y;

                // The terrain itself, when there is some: near the canyon the lowest thing
                // under a point is the canyon's rock running on under the sand, and a camera
                // put there looks up at the world from inside it.
                bool terrain = hit.collider.CompareTag("Sand") || hit.collider.CompareTag("Snow");
                if (terrain && (float.IsNaN(ground) || hit.point.y > ground)) ground = hit.point.y;
            }

            if (!float.IsNaN(ground)) return ground;
            return float.IsNaN(best) ? 0f : best;
        }

        public static void Capture()
        {
            string theme = Arg("-fpskitTheme") ?? "Desert Outpost";
            string outputFolder = Arg("-fpskitOut") ?? "Build/Views";

            string path = $"Assets/FPSKit_Generated/Scenes/{theme.Replace(" ", "")}.unity";

            if (!File.Exists(path))
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {path} does not exist, so there is nothing to look at.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(outputFolder);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            // Without this the first arena captured in a session is lit by no environment
            // at all, while everything after it looks right.
            DynamicGI.UpdateEnvironment();

            // Particles do not run in edit mode, so every plume on every map was invisible
            // here and the smoke had never once been looked at. Run each system forward to
            // where it would be a while into a level.
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                system.Simulate(14f, withChildren: true, restart: true);

            var settings = FPSKitThemes.GetOrCreate(theme);
            foreach (var shot in Frame(settings)) Render(shot, outputFolder);

            Debug.Log($"[FPSKitBatch] captured views of \"{theme}\" into {outputFolder}");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The viewpoints. Chosen to answer specific questions rather than to flatter:
        /// what the player sees on the first frame, whether the dunes read as dunes from
        /// eye height, whether the fence reads as a barrier from a distance, and what the
        /// canyon looks like from the one place the level never lets you stand.
        /// </summary>
        static IEnumerable<Shot> Frame(LevelTheme theme)
        {
            if (theme != null && theme.snowZone) return System.Linq.Enumerable.Concat(FrameSnow(), FrameSnowLife());
            if (theme != null && theme.industrialZone) return FrameIndustrial();
            if (theme != null && theme.openZone && !theme.volcanicZone)
                return System.Linq.Enumerable.Concat(FrameDesert(theme), FrameDesertLife());

            return FrameDesert(theme);
        }

        /// <summary>
        /// The frozen field's viewpoints, and they are <b>found rather than written
        /// down</b>.
        ///
        /// The desert's shots below are hard-coded coordinates aimed at a river, which is
        /// correct for the arena they were written for and useless anywhere else -- run
        /// against a snow field, four of the nine point at a canyon that does not exist
        /// and come back as photographs of empty ground. The lake and the camps here are
        /// placed from a seeded random, so there are no coordinates to write: the scene is
        /// already open by the time this runs, so it asks the scene where they are.
        ///
        /// The one that earns its place is the last: standing *inside* an igloo looking
        /// out of its door. That is the only shot that can say the shell is wound the
        /// right way round, that the doorway is actually a hole, and that the inside is
        /// lit -- three things invisible from every other angle, two of which have already
        /// been got wrong once in this project.
        /// </summary>
        static IEnumerable<Shot> FrameSnow()
        {
            const float Eye = 1.65f;

            var shots = new List<Shot>();

            shots.Add(new Shot
            {
                Name = "01_spawn", From = new Vector3(0f, Eye, 0f),
                Look = new Vector3(140f, 2f, 20f), Fov = 75f
            });

            var lake = GameObject.Find("Arena/FrozenLake/Ice");

            if (lake != null)
            {
                var at = lake.transform.position;
                float radius = lake.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x;

                // From the shore, across it. The question this answers is whether the lake
                // reads as a different surface from the snow around it at all -- if it
                // does not, the one open place in the arena is invisible.
                shots.Add(new Shot
                {
                    Name = "02_lake_shore",
                    From = at + new Vector3(-radius - 26f, Eye + 2.5f, -radius * 0.4f),
                    Look = at + new Vector3(radius * 0.6f, 0f, radius * 0.3f), Fov = 74f
                });

                shots.Add(new Shot
                {
                    Name = "03_on_the_lake",
                    From = at + new Vector3(-radius * 0.55f, Eye, 0f),
                    Look = at + new Vector3(radius, 1f, 0f), Fov = 78f
                });

                shots.Add(new Shot
                {
                    Name = "04_lake_aerial",
                    From = at + new Vector3(-radius * 1.6f, 95f, -radius * 1.6f),
                    Look = at, Fov = 60f
                });
            }

            var igloo = FindFirst("Igloo_");

            if (igloo != null)
            {
                var at = igloo.transform.position;

                // The door faces its own camp's middle and the tunnel points out along the
                // igloo's forward, so the view is placed off that axis: straight on, a
                // tunnel is a dark circle and could be anything.
                var ahead = igloo.transform.forward;

                shots.Add(new Shot
                {
                    Name = "05_camp",
                    From = at + ahead * 26f + igloo.transform.right * 9f + Vector3.up * (Eye + 1.2f),
                    Look = at + Vector3.up * 2f, Fov = 70f
                });

                shots.Add(new Shot
                {
                    Name = "06_igloo_door",
                    From = at + ahead * 21f + Vector3.up * Eye,
                    Look = at + Vector3.up * 1.4f, Fov = 68f
                });

                // Inside, looking out. The shot that proves the thing is a room.
                shots.Add(new Shot
                {
                    Name = "07_inside_igloo",
                    From = at + ahead * -1.6f + Vector3.up * 1.5f,
                    Look = at + ahead * 30f + Vector3.up * 1.2f, Fov = 80f
                });
            }

            var ridge = FindFirst("Ridge_");

            if (ridge != null)
                shots.Add(new Shot
                {
                    Name = "08_pressure_ridge",
                    From = ridge.transform.position + new Vector3(-18f, Eye + 1f, -14f),
                    Look = ridge.transform.position + Vector3.up * 1f, Fov = 70f
                });

            shots.Add(new Shot
            {
                Name = "09_aerial", From = new Vector3(-150f, 190f, -190f),
                Look = new Vector3(60f, 0f, 20f), Fov = 62f
            });

            return shots;
        }

        /// <summary>The first object in the scene whose name starts with this.</summary>
        static GameObject FindFirst(string prefix)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t != null && t.name.StartsWith(prefix)) return t.gameObject;

            return null;
        }

        /// <summary>
        /// The plant's viewpoints. It had none -- it fell through to the desert's, which
        /// aim at a river -- so nothing here had ever been looked at except from the air.
        ///
        /// The street plan is fixed (see PlanStreets): avenues and cross streets at 0 and
        /// ±100 m, the ring road at ±220 m, blocks of eighty to a hundred metres between.
        /// The ground is flat at zero, so the heights here are real heights.
        /// </summary>
        static IEnumerable<Shot> FrameIndustrial()
        {
            const float eye = 1.65f;

            yield return new Shot { Name = "01_spawn_north", From = new Vector3(0f, eye, 0f),
                                    Look = new Vector3(0f, 4f, 200f), Fov = 75f };
            yield return new Shot { Name = "02_spawn_east", From = new Vector3(0f, eye, 0f),
                                    Look = new Vector3(200f, 4f, 0f), Fov = 75f };
            yield return new Shot { Name = "03_down_a_street", From = new Vector3(100f, eye, -190f),
                                    Look = new Vector3(100f, 3f, 20f), Fov = 72f };
            yield return new Shot { Name = "04_in_a_block_ne", From = new Vector3(60f, eye, 60f),
                                    Look = new Vector3(160f, 4f, 150f), Fov = 75f };
            yield return new Shot { Name = "05_in_a_block_sw", From = new Vector3(-150f, eye, -60f),
                                    Look = new Vector3(-160f, 4f, -180f), Fov = 75f };
            yield return new Shot { Name = "06_ring_road", From = new Vector3(-220f, eye, -200f),
                                    Look = new Vector3(-220f, 3f, 100f), Fov = 72f };
            // Low, because the plant's fog is written for eye level: exponential-squared at
            // this density, anything much past two hundred metres of air is grey.
            yield return new Shot { Name = "07_aerial", From = new Vector3(-190f, 60f, -190f),
                                    Look = new Vector3(-60f, 0f, -60f), Fov = 70f };
            yield return new Shot { Name = "08_overhead", From = new Vector3(40f, 110f, -60f),
                                    Look = new Vector3(60f, 0f, 60f), Fov = 75f };
            // The spawn junction from a few metres up one arm: its road markings, the stop
            // lines and the clear box between them, as the player first meets them.
            yield return new Shot { Name = "08b_junction", From = new Vector3(-3f, 3.2f, -17f),
                                    Look = new Vector3(0f, 0f, 2f), Fov = 72f };

            // A walkway and its stair, found rather than written down: the catwalks go
            // wherever the tank farms and the power house put their pipe runs.
            // A pipe run's walkway rather than any catwalk: the roof routes' bridges are
            // catwalks too, and the first one found was behind a compound wall. Seen from
            // above one side, so both of its stairs are in frame.
            var pipes = FindFirst("PipeRun");
            if (pipes != null)
            {
                var r = pipes.GetComponent<Renderer>();
                var at = r != null ? r.bounds.center : pipes.transform.position;
                float half = r != null ? Mathf.Max(r.bounds.extents.x, r.bounds.extents.z) : 30f;
                var across = r != null && r.bounds.extents.x > r.bounds.extents.z ? Vector3.forward : Vector3.right;
                yield return new Shot { Name = "09_catwalk", From = at + across * (half * 1.1f) + Vector3.up * 16f,
                                        Look = at + Vector3.up * 2f, Fov = 80f };
            }

            // These are built in world space on an object at the origin, so where they are is
            // their renderer's bounds, not their transform.
            Bounds? BoundsOf(string name)
            {
                var go = FindFirst(name);
                var r = go != null ? go.GetComponent<Renderer>() : null;
                return r != null ? r.bounds : (Bounds?)null;
            }

            var row = BoundsOf("RoofRowBuilding");
            if (row.HasValue)
            {
                var b = row.Value;
                yield return new Shot { Name = "11_roof_route", From = b.center + new Vector3(-30f, b.extents.y + 10f, -30f),
                                        Look = b.center + Vector3.up * b.extents.y, Fov = 70f };
            }

            var shed = BoundsOf("WarehouseWalls");
            if (shed.HasValue)
            {
                var b = shed.Value;
                var from = new Vector3(b.min.x + 2.5f, 1.65f, b.min.z + 2.5f);
                yield return new Shot { Name = "12_warehouse_inside", From = from,
                                        Look = new Vector3(b.max.x, 3f, b.max.z), Fov = 80f };
            }

            var loco = FindFirst("Locomotive");
            if (loco != null)
            {
                var at = loco.transform.position;
                yield return new Shot { Name = "13_railway", From = at + new Vector3(14f, 3f, 14f),
                                        Look = at + new Vector3(-30f, 1f, -30f), Fov = 72f };
            }

            var belt = BoundsOf("ConveyorBelt");
            if (belt.HasValue)
            {
                var b = belt.Value;
                yield return new Shot { Name = "14_conveyor", From = b.center + new Vector3(18f, -b.extents.y + 2f, 18f),
                                        Look = b.center, Fov = 70f };
            }

            var cooler = FindFirst("CoolingTower");
            if (cooler != null)
            {
                var at = cooler.transform.position;
                yield return new Shot { Name = "10_steam", From = at + new Vector3(90f, 6f, -90f),
                                        Look = at + Vector3.up * 40f, Fov = 70f };
            }
        }

        static IEnumerable<Shot> FrameDesert(LevelTheme theme)
        {
            float centre = theme == null ? 92f
                : theme.hazardOffset + Mathf.Sin(1.7f) * theme.hazardMeander * 0.35f;

            float eye = 1.65f;

            yield return new Shot
            {
                Name = "01_spawn_east", Grounded = true, From = new Vector3(0f, eye, 0f),
                Look = new Vector3(140f, 2f, 20f), Fov = 75f
            };

            yield return new Shot
            {
                Name = "02_spawn_north", Grounded = true, From = new Vector3(0f, eye, 0f),
                Look = new Vector3(-30f, 4f, 160f), Fov = 75f
            };

            yield return new Shot
            {
                Name = "03_dune_crest", Grounded = true, From = new Vector3(-70f, eye + 6f, -60f),
                Look = new Vector3(40f, 0f, 30f), Fov = 70f
            };

            yield return new Shot
            {
                Name = "04_rim_fence", From = new Vector3(centre - 62f, eye, -30f),
                Look = new Vector3(centre - 40f, 0f, 40f), Fov = 72f
            };

            // Right on the lip. Anywhere further back and the far rim hides the canyon
            // completely, which is correct for the level and useless for checking it.
            yield return new Shot
            {
                Name = "05_rim_over_water", From = new Vector3(centre - 38f, eye, 10f),
                Look = new Vector3(centre + 30f, -12f, 22f), Fov = 78f
            };

            yield return new Shot
            {
                Name = "06_bridge", From = new Vector3(centre - 18f, eye + 0.3f, 139f),
                Look = new Vector3(centre + 55f, -3f, 139f), Fov = 78f
            };

            yield return new Shot
            {
                Name = "07_in_canyon", From = new Vector3(centre - 6f, -16.5f, 60f),
                Look = new Vector3(centre - 24f, -12f, 30f), Fov = 80f
            };

            yield return new Shot
            {
                Name = "08_aerial", From = new Vector3(-150f, 190f, -190f),
                Look = new Vector3(60f, 0f, 20f), Fov = 62f
            };

            // Across the open plain rather than from a pad. Every other shot here stands
            // on flattened ground -- the spawn, the rim, a deck -- which is exactly the
            // ground that has had its character taken out, so none of them could show
            // whether the plain itself reads as varied.
            yield return new Shot
            {
                Name = "10_open_plain", Grounded = true, From = new Vector3(-150f, eye + 1.5f, -150f),
                Look = new Vector3(-60f, 0f, -40f), Fov = 72f
            };

            yield return new Shot
            {
                Name = "09_ground_close", Grounded = true, From = new Vector3(-40f, 1.1f, 30f),
                Look = new Vector3(10f, 0.2f, 55f), Fov = 60f
            };
        }

        /// <summary>
        /// The desert's newer places, found in the scene: they are placed from the seed, so
        /// there are no coordinates to write down.
        /// </summary>
        static IEnumerable<Shot> FrameDesertLife()
        {
            Bounds? Of(string name)
            {
                var go = FindFirst(name);
                if (go == null) return null;
                var r = go.GetComponentInChildren<Renderer>();
                return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
            }

            var town = FindFirst("Town");
            var minaret = Of("Minaret");
            if (minaret.HasValue)
            {
                var m = minaret.Value.center;
                yield return new Shot { Name = "11_town_street", Grounded = true, From = new Vector3(m.x - 34f, 1.65f, m.z - 22f),
                                        Look = new Vector3(m.x, 6f, m.z), Fov = 75f };
                yield return new Shot { Name = "12_town_above", From = new Vector3(m.x - 70f, m.y + 45f, m.z - 70f),
                                        Look = new Vector3(m.x - 14f, m.y - 10f, m.z - 14f), Fov = 65f };
            }
            _ = town;

            // The base: from out in front of the main gate, and from inside on its main street.
            var fob = Of("Hesco");
            var chicane = Of("JerseyBarriers");
            if (fob.HasValue && chicane.HasValue)
            {
                var c = fob.Value.center;
                var gate = chicane.Value.center;
                var outward = new Vector3(gate.x - c.x, 0f, gate.z - c.z).normalized;
                var side = Vector3.Cross(Vector3.up, outward);
                yield return new Shot { Name = "13_base_gate", Grounded = true, From = gate + outward * 16f + side * 6f + Vector3.up * (1.65f - gate.y),
                                        Look = new Vector3(c.x, 2f, c.z), Fov = 72f };
                yield return new Shot { Name = "14_base_inside", Grounded = true, From = c + outward * 26f + side * 2f + Vector3.up * (1.65f - c.y),
                                        Look = new Vector3(c.x - side.x * 14f, 2f, c.z - side.z * 14f) - outward * 10f, Fov = 78f };
            }

            if (fob.HasValue)
            {
                var c = fob.Value.center;
                yield return new Shot { Name = "24_base_above", From = new Vector3(c.x - 40f, c.y + 60f, c.z - 60f),
                                        Look = c, Fov = 60f };
            }

            yield return new Shot { Name = "25_map_top", From = new Vector3(0f, 430f, -60f), Look = Vector3.zero, Fov = 64f };

            var farm = Of("FarmWall");
            if (farm.HasValue)
            {
                var p = farm.Value.center;
                yield return new Shot { Name = "18_farm", Grounded = true, From = new Vector3(p.x + 22f, 3.5f, p.z - 20f),
                                        Look = new Vector3(p.x, 1.5f, p.z), Fov = 70f };
            }

            var room = Of("HouseWalls");
            if (room.HasValue)
            {
                var p = room.Value.center;
                yield return new Shot { Name = "19_house_inside", Grounded = true, From = new Vector3(p.x - 0.8f, 1.65f, p.z - 0.4f),
                                        Look = new Vector3(p.x + 3f, 1.2f, p.z - 3f), Fov = 82f };
            }

            if (minaret.HasValue)
            {
                var m = minaret.Value.center;
                // Down a lane rather than the main street: the lanes are what the village is.
                yield return new Shot { Name = "20_village_lane", Grounded = true, From = new Vector3(m.x - 7.5f - 45f, 1.65f, m.z + 7.5f),
                                        Look = new Vector3(m.x - 7.5f, 2.5f, m.z + 7.5f), Fov = 72f };
            }

            var camp = Of("CampTent");
            if (camp.HasValue)
            {
                var p = camp.Value.center;
                yield return new Shot { Name = "21_camp", Grounded = true, From = new Vector3(p.x + 12f, 1.65f, p.z - 10f),
                                        Look = new Vector3(p.x, 0.5f, p.z), Fov = 70f };
            }

            var cp = Of("Booth");
            if (cp.HasValue)
            {
                var p = cp.Value.center;
                yield return new Shot { Name = "22_checkpoint", Grounded = true, From = new Vector3(p.x - 16f, 1.65f, p.z - 16f),
                                        Look = new Vector3(p.x, 1f, p.z), Fov = 70f };
            }

            var wadi = Of("Wadi");
            if (wadi.HasValue)
            {
                var p = wadi.Value.center;
                yield return new Shot { Name = "23_wadi", Grounded = true, From = new Vector3(p.x, 2.2f, p.z),
                                        Look = new Vector3(p.x + 30f, 0.5f, p.z + 30f), Fov = 72f };
            }

            var net = Of("CamoNet");
            if (net.HasValue)
            {
                var p = net.Value.center;
                yield return new Shot { Name = "16_net_position", Grounded = true, From = new Vector3(p.x + 16f, 1.65f, p.z + 12f),
                                        Look = new Vector3(p.x, p.y - 1.5f, p.z), Fov = 70f };
            }

            var grove = Of("AcaciaTrunks");
            if (grove.HasValue)
            {
                var p = grove.Value.center;
                yield return new Shot { Name = "17_acacia_grove", Grounded = true, From = new Vector3(p.x + 22f, 1.65f, p.z + 16f),
                                        Look = new Vector3(p.x, p.y, p.z), Fov = 70f };
            }

            var plane = Of("PlaneFuselage");
            if (plane.HasValue)
            {
                var p = plane.Value.center;
                yield return new Shot { Name = "15_plane", Grounded = true, From = new Vector3(p.x + 24f, 1.65f, p.z + 18f),
                                        Look = p, Fov = 70f };
            }
        }

        /// <summary>Snowbound's newer places, found in the scene like the camps are.</summary>
        static IEnumerable<Shot> FrameSnowLife()
        {
            Bounds? Of(string name)
            {
                var go = FindFirst(name);
                if (go == null) return null;
                var r = go.GetComponentInChildren<Renderer>();
                return r != null ? r.bounds : new Bounds(go.transform.position, Vector3.one);
            }

            var anchor = FindFirst("PreviewAnchor");
            if (anchor != null)
                yield return new Shot { Name = "10_station", From = anchor.transform.position,
                                        Look = anchor.transform.position + anchor.transform.forward * 50f, Fov = 62f };

            var bridge = Of("RopeBridge");
            if (bridge.HasValue)
            {
                var b = bridge.Value.center;
                yield return new Shot { Name = "11_crevasse", Grounded = true, From = new Vector3(b.x + 14f, 1.8f, b.z + 14f),
                                        Look = new Vector3(b.x, b.y - 2f, b.z), Fov = 72f };
            }

            var cave = Of("IceCave");
            if (cave.HasValue)
            {
                var c = cave.Value.center;
                yield return new Shot { Name = "12_ice_cave", Grounded = true, From = new Vector3(c.x + 18f, 1.65f, c.z + 18f),
                                        Look = new Vector3(c.x, 2f, c.z), Fov = 72f };
            }

            var fall = Of("FrozenFall");
            if (fall.HasValue)
            {
                var f = fall.Value.center;
                yield return new Shot { Name = "13_icefall", Grounded = true, From = new Vector3(f.x * 0.8f, 1.65f, f.z * 0.8f),
                                        Look = new Vector3(f.x, f.y, f.z), Fov = 72f };
            }

            var pines = Of("PineTrunks");
            if (pines.HasValue)
            {
                var p = pines.Value.center;
                yield return new Shot { Name = "14_forest", Grounded = true, From = new Vector3(p.x + 30f, 1.65f, p.z + 30f),
                                        Look = new Vector3(p.x, 4f, p.z), Fov = 72f };
            }
        }

        static void Render(Shot shot, string folder)
        {
            var rig = new GameObject("CaptureCamera");
            var camera = rig.AddComponent<Camera>();

            RenderTexture target = null;

            try
            {
                if (shot.Grounded)
                {
                    // The look point rises with the camera so each shot keeps its angle.
                    float lift = GroundUnder(shot.From);
                    shot.From.y += lift;
                    shot.Look.y += lift;

                    // Out of anything solid: the places are placed from the seed, and a shot
                    // framed off one of them can land in a parked car or a wall.
                    var start = shot.From;
                    for (int ring = 1; ring <= 6 && Physics.CheckSphere(shot.From, 0.45f, ~0, QueryTriggerInteraction.Ignore); ring++)
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI * 0.25f;
                            var p = start + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
                            p.y = GroundUnder(p) + (start.y - lift);
                            if (Physics.CheckSphere(p, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            shot.From = p;
                            break;
                        }
                }

                rig.transform.position = shot.From;
                rig.transform.rotation = Quaternion.LookRotation((shot.Look - shot.From).normalized,
                                                                 Vector3.up);

                camera.fieldOfView = shot.Fov;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 4000f;
                camera.clearFlags = CameraClearFlags.Skybox;

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 2
                };

                var request = new RenderPipeline.StandardRequest { destination = target };

                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;

                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                image.Apply();

                RenderTexture.active = previous;

                File.WriteAllBytes($"{folder}/{shot.Name}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Object.DestroyImmediate(rig);
            }
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];

            return null;
        }
    }
}
#endif
