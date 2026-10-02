#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The Grand Hall: 80 x 50m, the one big interior in the Abandoned Fairground, and
    /// <b>half built</b>.
    ///
    /// <pre>
    ///   west (x -40)                                  east (x +40)
    ///   +--------------------------------+-------------------------+
    ///   | stage | grand stair  nave      |  raw slab, steel frame, |
    ///   |       | seating, fountain,     |  scaffold, excavator,   |
    ///   | gallery all round (U)          |  gallery slabs, no rail |
    ///   +--------------------------------+-------------------------+
    ///        finished, then ruined             never finished
    /// </pre>
    ///
    /// The west two thirds are the hall as it was opened: plastered walls, a tiled floor, a
    /// stage, chandeliers and a gallery with a balustrade, now with the roof coming in and a tree
    /// through it. The east third was never completed: a bare steel frame, brick walls that
    /// stop at head height, a raw slab, scaffolding and the plant that was left when the
    /// money ran out.
    ///
    /// <b>The gallery is real.</b> It is a first floor of solid deck, six metres up, running
    /// round three sides, and it is reached by three flights of stairs -- a grand one in the
    /// nave and a plain one at the end of each side -- each of 28 solid treads with its landing
    /// flush with the deck. The previous hall had a collapsed stair that went nowhere. This one is
    /// walkable, and the break in the south run (where the deck came down) is the fight, not a
    /// reason it cannot be used.
    ///
    /// <b>Why it can be real now.</b> The industrial walkways proved the join: solid treads of a
    /// fixed rise, a landing exactly at deck height, and the deck a collider the bake can see.
    /// What fails is a stair that stops short, so every flight here is built from the deck
    /// outwards, with the deck's height a whole number of risers.
    ///
    /// <b>Everything else is still ground.</b> The floor is paint over the terrain; the roof,
    /// the trusses, the columns, the rails and the scaffolding are held off the bake.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private const float HallPad = 49f;
        private const float HallHalfLength = 40f;
        private const float HallHalfWidth = 25f;
        private const float HallWall = 13f;
        private const float HallRidge = 20.5f;
        private const float HallDoorHeight = 5.4f;
        private const float HallThick = 0.7f;

        /// <summary>28 risers of 0.22: the deck is exactly this high, so its stairs land flush.</summary>
        private const int GallerySteps = 28;
        private const float GalleryY = GallerySteps * StairRise;
        private const float GalleryDepth = 7.6f;
        private const float GalleryWest = -39.6f;
        private const float GalleryEast = 26.4f;

        /// <summary>West of this the hall was finished; east of it, never.</summary>
        private const float HallSplit = 10f;

        private static Material _hallWallMat, _hallTrimMat, _hallGlassMat, _hallGlassLitMat, _hallRoofMat,
                                _hallSkyMat, _hallTileA, _hallTileB, _hallRugMat, _hallClothA, _hallClothB,
                                _hallBrick, _hallRaw, _hallRust, _hallTarp;

        private struct HallDoor
        {
            public float Centre, Width;
            public HallDoor(float centre, float width) { Centre = centre; Width = width; }
        }

        private struct HallRect
        {
            public float X0, X1, Z0, Z1;
        }

        private static readonly List<HallRect> _hallBlock = new List<HallRect>();

        private static void HallReserve(float x0, float x1, float z0, float z1, float margin = 1.2f)
            => _hallBlock.Add(new HallRect { X0 = Mathf.Min(x0, x1) - margin, X1 = Mathf.Max(x0, x1) + margin,
                                             Z0 = Mathf.Min(z0, z1) - margin, Z1 = Mathf.Max(z0, z1) + margin });

        private static bool HallFree(float x, float z, float radius)
        {
            foreach (var r in _hallBlock)
                if (x > r.X0 - radius && x < r.X1 + radius && z > r.Z0 - radius && z < r.Z1 + radius) return false;

            return true;
        }

        private static float TrussX(int k) => -39.6f + 6.6f * k;

        private static void ResolveHallMaterials()
        {
            _hallWallMat = MakeDetailMaterial("HallPlaster", new Color(0.56f, 0.50f, 0.41f), "Adobe",
                                              Metres(5f), 0.06f, 0f, 0.7f);
            _hallTrimMat = MakeDetailMaterial("HallTrim", new Color(0.36f, 0.30f, 0.25f), "Concrete",
                                              Metres(2.4f), 0.08f, 0f, 0.8f);
            _hallGlassMat = MakeDetailMaterial("HallGlass", new Color(0.05f, 0.08f, 0.10f), "Concrete",
                                               Metres(6f), 0.88f, 0.1f, 0.1f);
            _hallGlassLitMat = MakeDetailMaterial("HallGlassLit", new Color(0.30f, 0.20f, 0.09f), "Concrete",
                                                  Metres(6f), 0.5f, 0f, 0.1f);
            SetEmission(_hallGlassLitMat, new Color(1f, 0.62f, 0.28f) * 1.15f);
            _hallRoofMat = MakeDetailMaterial("HallRoof", new Color(0.34f, 0.26f, 0.21f), "Metal",
                                              Metres(2.6f), 0.3f, 0.45f, 0.8f);
            _hallSkyMat = MakeDetailMaterial("HallSkylight", new Color(0.16f, 0.21f, 0.24f), "Concrete",
                                             Metres(6f), 0.85f, 0.05f, 0.1f);
            SetEmission(_hallSkyMat, new Color(0.18f, 0.25f, 0.32f));
            _hallTileA = MakeDetailMaterial("HallTileA", new Color(0.38f, 0.34f, 0.30f), "Concrete",
                                            Metres(2.6f), 0.06f, 0f, 0.7f);
            _hallTileB = MakeDetailMaterial("HallTileB", new Color(0.20f, 0.19f, 0.17f), "Concrete",
                                            Metres(2.6f), 0.06f, 0f, 0.7f);
            _hallRugMat = MakeDetailMaterial("HallRug", new Color(0.36f, 0.12f, 0.12f), "Timber",
                                             Metres(1.6f), 0.04f, 0f, 0.5f);
            _hallClothA = MakeDetailMaterial("HallClothA", new Color(0.46f, 0.15f, 0.13f), "Timber",
                                             Metres(2f), 0.06f, 0f, 0.4f);
            _hallClothB = MakeDetailMaterial("HallClothB", new Color(0.12f, 0.29f, 0.29f), "Timber",
                                             Metres(2f), 0.06f, 0f, 0.4f);
            _hallBrick = MakeDetailMaterial("HallBrick", new Color(0.46f, 0.24f, 0.17f), "Adobe",
                                            Metres(3f), 0.05f, 0f, 1f);
            _hallRaw = MakeDetailMaterial("HallRawConcrete", new Color(0.40f, 0.39f, 0.36f), "Concrete",
                                          Metres(3f), 0.04f, 0f, 1f);
            _hallRust = MakeDetailMaterial("HallRust", new Color(0.33f, 0.20f, 0.13f), "Metal",
                                           Metres(1.5f), 0.12f, 0.4f);
            _hallTarp = MakeDetailMaterial("HallTarp", new Color(0.16f, 0.30f, 0.44f), "Timber",
                                           Metres(2.5f), 0.12f, 0f, 0.3f);
        }

        // ==================================================================
        // The hall
        // ==================================================================
        private static void BuildGrandHall(Transform root, int layer, int backdrop)
        {
            if (!_hasHall) return;

            var site = _hallPlan;
            var hall = new GameObject("GrandHall").transform;
            hall.SetParent(root, false);
            hall.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            hall.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            var rng = new System.Random(_theme.randomSeed * 9433 + 17);

            _hallBlock.Clear();
            // The three stairs, the stage steps: nothing is placed on or at the foot of one.
            HallReserve(-32f, -22.5f, -3.4f, 3.4f);
            HallReserve(GalleryEast, GalleryEast + FgStairRun(GallerySteps) + 1.5f, 18.6f, 23f);
            HallReserve(GalleryEast, GalleryEast + FgStairRun(GallerySteps) + 1.5f, -23f, -18.6f);
            HallReserve(-34f, -30.4f, 3.5f, 8.5f, 0.6f);
            HallReserve(-34f, -30.4f, -8.5f, -3.5f, 0.6f);

            var trim = new MeshBuild { UVScale = 0.4f };
            var glass = new MeshBuild { UVScale = 0.4f };
            var glassLit = new MeshBuild { UVScale = 0.4f };
            var steel = new MeshBuild { UVScale = 0.5f };

            BuildHallWalls(hall, layer, trim, glass, glassLit, rng);
            BuildHallFrame(hall, layer, steel, rng);
            BuildHallRoof(hall, backdrop, rng);
            BuildHallGallery(hall, layer, backdrop, trim, rng);
            BuildHallFloor(hall, backdrop, rng);
            BuildHallStage(hall, layer, backdrop, trim, rng);
            BuildHallFurnishings(hall, layer, backdrop, rng);
            BuildHallSite(hall, layer, backdrop, rng);
            BuildHallGreenery(hall, layer, backdrop, rng);
            BuildHallForecourt(hall, layer, backdrop, trim, rng);
            BuildHallCrane(hall, layer, backdrop, rng);
            BuildHallLights(hall, rng);

            EmitHall(hall, backdrop, "HallTrim", trim, _hallTrimMat);
            EmitHall(hall, backdrop, "HallGlass", glass, _hallGlassMat);
            EmitHall(hall, backdrop, "HallGlassLit", glassLit, _hallGlassLitMat);
            EmitHall(hall, backdrop, "HallSteel", steel, _parkSteel);
        }

        /// <summary>A hall detail: drawn, never collided with, off the map, off the bake.</summary>
        private static void EmitHall(Transform hall, int backdrop, string name, MeshBuild build, Material material)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(hall, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(go);
            Hide(go);
        }

        /// <summary>A hall mesh that is solid: cover, and a collider, but never stood on.</summary>
        private static void EmitHallSolid(Transform hall, int layer, string name, MeshBuild build, Material material,
                                          string tag = "Metal")
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(hall, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, tag);
            NoStanding(go);
            Hide(go);
        }

        /// <summary>A solid block in the hall's own frame: cover, and a collider. Not walkable.</summary>
        private static GameObject HallBlock(Transform hall, int layer, string name, Vector3 pos, Vector3 size,
                                            Material material, string tag = "Concrete", float yaw = 0f)
        {
            var go = CreateBlock(hall, pos, size, yaw, layer, tag, Color.grey, 0.05f, 0f, name, material);
            NoStanding(go);
            return go;
        }

        /// <summary>A solid block that <i>is</i> walkable: a deck, a stage. Left in the bake on purpose.</summary>
        private static GameObject HallDeck(Transform hall, int layer, string name, Vector3 pos, Vector3 size,
                                           Material material, string tag = "Wood")
            => CreateBlock(hall, pos, size, 0f, layer, tag, Color.grey, 0.05f, 0f, name, material);

        // ------------------------------------------------------------------
        // Walls
        // ------------------------------------------------------------------
        private static void BuildHallWalls(Transform hall, int layer, MeshBuild trim, MeshBuild glass,
                                           MeshBuild glassLit, System.Random rng)
        {
            // The finished walls end where the building stopped.
            var front = new[] { new HallDoor(-30f, 5f), new HallDoor(-16f, 5f), new HallDoor(0f, 9f) };
            var back = new[] { new HallDoor(-28f, 4.5f), new HallDoor(-6f, 4.5f), new HallDoor(5f, 4.5f) };
            var west = new[] { new HallDoor(-14f, 4.5f), new HallDoor(14f, 4.5f) };

            WallRun(hall, layer, "WallFront", true, HallHalfWidth, -HallHalfLength, HallSplit, front);
            WallRun(hall, layer, "WallBack", true, -HallHalfWidth, -HallHalfLength, HallSplit, back);
            WallRun(hall, layer, "WallWest", false, -HallHalfLength, -HallHalfWidth, HallHalfWidth, west);

            Dress(trim, glass, glassLit, true, HallHalfWidth, -HallHalfLength, HallSplit, front, 1f, rng);
            Dress(trim, glass, glassLit, true, -HallHalfWidth, -HallHalfLength, HallSplit, back, -1f, rng);
            Dress(trim, glass, glassLit, false, -HallHalfLength, -HallHalfWidth, HallHalfWidth, west, -1f, rng);

            // The unfinished walls: brick that stops where the bricklayers did, and nothing beyond.
            RaggedWall(hall, layer, true, HallHalfWidth, HallSplit, HallHalfLength,
                       new[] { new HallDoor(19f, 6f), new HallDoor(33f, 8f) }, rng, 9.5f);
            RaggedWall(hall, layer, true, -HallHalfWidth, HallSplit, HallHalfLength,
                       new[] { new HallDoor(17f, 6f), new HallDoor(30f, 7f) }, rng, 7.5f);
            RaggedWall(hall, layer, false, HallHalfLength, -HallHalfWidth, HallHalfWidth,
                       new[] { new HallDoor(-12f, 9f), new HallDoor(6f, 10f) }, rng, 4.5f);
        }

        private static void WallRun(Transform hall, int layer, string name, bool alongX, float fixedCoord,
                                    float from, float to, HallDoor[] doors)
        {
            var sorted = new List<HallDoor>(doors);
            sorted.Sort((a, b) => a.Centre.CompareTo(b.Centre));

            float cursor = from;

            void Solid(float a, float b, float y0, float y1, string suffix)
            {
                if (b - a < 0.05f) return;

                float mid = (a + b) * 0.5f;
                var pos = alongX
                    ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord)
                    : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                var size = alongX
                    ? new Vector3(b - a, y1 - y0, HallThick)
                    : new Vector3(HallThick, y1 - y0, b - a);

                HallBlock(hall, layer, $"{name}{suffix}", pos, size, _hallWallMat);
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                float a = sorted[i].Centre - sorted[i].Width * 0.5f;
                float b = sorted[i].Centre + sorted[i].Width * 0.5f;

                Solid(cursor, a, 0f, HallWall, $"_{i}");
                Solid(a, b, HallDoorHeight, HallWall, $"_Lintel{i}");
                cursor = b;
            }

            Solid(cursor, to, 0f, HallWall, "_End");
        }

        /// <summary>
        /// Brick walls left half built. Each two-metre bay was raised to its own height, so the
        /// top is a staircase of courses; between the bays a steel column stands to the full
        /// height of the building. Openings are left wide: this is a site, not a sealed room.
        /// </summary>
        private static void RaggedWall(Transform hall, int layer, bool alongX, float fixedCoord, float from, float to,
                                       HallDoor[] openings, System.Random rng, float tallest)
        {
            const float Bay = 2.2f;
            int bays = Mathf.CeilToInt((to - from) / Bay);

            for (int i = 0; i < bays; i++)
            {
                float a = from + i * Bay, b = Mathf.Min(to, a + Bay);
                float mid = (a + b) * 0.5f;

                bool open = false;
                foreach (var d in openings)
                    if (Mathf.Abs(mid - d.Centre) < d.Width * 0.5f + 0.2f) open = true;
                if (open) continue;

                // Stepped, rising and falling: bricklayers work in runs, not evenly.
                float wave = Mathf.PerlinNoise(mid * 0.23f + fixedCoord, 3.3f);
                float h = Mathf.Lerp(1.2f, tallest, wave) + (float)rng.NextDouble() * 0.6f;
                if (rng.NextDouble() < 0.07) h = 0.5f;
                h = Mathf.Round(h / 0.25f) * 0.25f;

                var pos = alongX ? new Vector3(mid, h * 0.5f, fixedCoord) : new Vector3(fixedCoord, h * 0.5f, mid);
                var size = alongX ? new Vector3(b - a, h, 0.5f) : new Vector3(0.5f, h, b - a);
                HallBlock(hall, layer, "BrickBay", pos, size, _hallBrick);
            }
        }

        // ------------------------------------------------------------------
        // Steel frame of the unfinished wing, and the columns of the finished one
        // ------------------------------------------------------------------
        private static void BuildHallFrame(Transform hall, int layer, MeshBuild steel, System.Random rng)
        {
            var plaster = new MeshBuild { UVScale = 0.5f };
            var iron = new MeshBuild { UVScale = 0.5f };

            // Interior columns carrying the gallery: plaster in the finished wing, bare steel beyond it.
            for (int k = 1; k <= 11; k++)
            {
                float x = TrussX(k);
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = side * 16.7f;
                    bool finished = x < HallSplit;
                    float h = HallWall - 0.4f;

                    if (finished)
                    {
                        plaster.Box(new Vector3(x, h * 0.5f, z), new Vector3(0.9f, h, 0.9f), Quaternion.identity);
                        plaster.Box(new Vector3(x, 0.4f, z), new Vector3(1.3f, 0.8f, 1.3f), Quaternion.identity);
                        plaster.Box(new Vector3(x, h - 0.3f, z), new Vector3(1.4f, 0.6f, 1.4f), Quaternion.identity);
                    }
                    else
                    {
                        // An H-section, bolted to a plate, with the bolts showing.
                        iron.Box(new Vector3(x, h * 0.5f, z), new Vector3(0.14f, h, 0.5f), Quaternion.identity);
                        iron.Box(new Vector3(x, h * 0.5f, z - 0.18f), new Vector3(0.5f, h, 0.1f), Quaternion.identity);
                        iron.Box(new Vector3(x, h * 0.5f, z + 0.18f), new Vector3(0.5f, h, 0.1f), Quaternion.identity);
                        iron.Box(new Vector3(x, 0.08f, z), new Vector3(0.9f, 0.16f, 0.9f), Quaternion.identity);
                    }
                }
            }

            // The bare outer frame of the east wing: a column every bay, with two girders between.
            for (int k = 7; k <= 12; k++)
            {
                float x = TrussX(k);
                if (x < HallSplit + 2f) continue;

                foreach (float z in new[] { -HallHalfWidth, HallHalfWidth })
                {
                    iron.Box(new Vector3(x, HallWall * 0.5f, z), new Vector3(0.5f, HallWall, 0.5f), Quaternion.identity);
                    if (k > 7)
                    {
                        float x0 = TrussX(k - 1);
                        iron.Box(new Vector3((x + x0) * 0.5f, HallWall - 0.3f, z), new Vector3(x - x0, 0.45f, 0.3f), Quaternion.identity);
                        if (rng.NextDouble() < 0.7)
                            iron.Box(new Vector3((x + x0) * 0.5f, 6.4f, z), new Vector3(x - x0, 0.4f, 0.3f), Quaternion.identity);
                    }
                }
            }

            // The east end: columns, and one girder across.
            for (int i = -3; i <= 3; i++)
            {
                float z = i * 7.1f;
                iron.Box(new Vector3(HallHalfLength, HallWall * 0.5f, z), new Vector3(0.5f, HallWall, 0.5f), Quaternion.identity);
                if (i < 3)
                    iron.Box(new Vector3(HallHalfLength, HallWall - 0.3f, z + 3.55f), new Vector3(0.3f, 0.45f, 7.1f), Quaternion.identity);
            }

            EmitHallSolid(hall, layer, "HallColumns", plaster, _hallWallMat, "Concrete");
            EmitHallSolid(hall, layer, "HallSteelColumns", iron, _hallRust, "Metal");
        }

        /// <summary>
        /// Everything a wall has that a slab does not. Built into the shared trim and glass
        /// meshes, so a dressed face of 50 metres costs one draw call and no colliders.
        /// <paramref name="outward"/> is the side the face looks out to.
        /// </summary>
        private static void Dress(MeshBuild trim, MeshBuild glass, MeshBuild glassLit, bool alongX, float fixedCoord,
                                  float from, float to, HallDoor[] doors, float outward, System.Random rng)
        {
            Vector3 At(float along, float y, float off)
            {
                float across = fixedCoord + outward * (HallThick * 0.5f + off);
                return alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
            }

            Vector3 Size(float length, float height, float depth)
                => alongX ? new Vector3(length, height, depth) : new Vector3(depth, height, length);

            bool InDoor(float along, float margin)
            {
                foreach (var d in doors)
                    if (Mathf.Abs(along - d.Centre) < d.Width * 0.5f + margin) return true;
                return false;
            }

            float length = to - from;

            trim.Box(At((from + to) * 0.5f, 0.45f, 0.2f), Size(length + 0.8f, 0.9f, 0.4f), Quaternion.identity);
            trim.Box(At((from + to) * 0.5f, HallWall + 0.2f, 0.45f), Size(length + 1.4f, 0.55f, 1.0f), Quaternion.identity);
            trim.Box(At((from + to) * 0.5f, HallWall - 0.6f, 0.2f), Size(length + 0.8f, 0.25f, 0.4f), Quaternion.identity);

            int bays = Mathf.Max(1, Mathf.RoundToInt(length / 6.6f));
            float step = length / bays;

            for (int i = 0; i <= bays; i++)
            {
                float along = from + i * step;
                if (!InDoor(along, 0.6f))
                    trim.Box(At(along, HallWall * 0.5f, 0.2f), Size(0.9f, HallWall, 0.4f), Quaternion.identity);
            }

            for (int i = 0; i < bays; i++)
            {
                float along = from + (i + 0.5f) * step;
                if (InDoor(along, 1.9f)) continue;

                // Tall windows with a frame and a mullion; one in five lit from inside.
                var pane = rng.NextDouble() < 0.2 ? glassLit : glass;
                float wy = 8.1f, wh = 6.2f, ww = 3.4f;

                pane.Box(At(along, wy, 0.06f), Size(ww, wh, 0.1f), Quaternion.identity);
                trim.Box(At(along, wy + wh * 0.5f + 0.15f, 0.12f), Size(ww + 0.5f, 0.3f, 0.22f), Quaternion.identity);
                trim.Box(At(along, wy - wh * 0.5f - 0.15f, 0.14f), Size(ww + 0.7f, 0.3f, 0.3f), Quaternion.identity);
                trim.Box(At(along - ww * 0.5f - 0.1f, wy, 0.12f), Size(0.2f, wh + 0.6f, 0.22f), Quaternion.identity);
                trim.Box(At(along + ww * 0.5f + 0.1f, wy, 0.12f), Size(0.2f, wh + 0.6f, 0.22f), Quaternion.identity);
                trim.Box(At(along, wy, 0.14f), Size(0.12f, wh, 0.2f), Quaternion.identity);
                trim.Box(At(along, wy + 0.5f, 0.14f), Size(ww, 0.12f, 0.2f), Quaternion.identity);

                // A low window under the gallery, so the ground floor is not a dark corridor.
                if (rng.NextDouble() < 0.6)
                    glass.Box(At(along, 2.6f, 0.06f), Size(2.2f, 1.7f, 0.1f), Quaternion.identity);
            }

            foreach (var d in doors)
            {
                trim.Box(At(d.Centre - d.Width * 0.5f - 0.3f, HallDoorHeight * 0.5f, 0.2f),
                         Size(0.5f, HallDoorHeight, 0.4f), Quaternion.identity);
                trim.Box(At(d.Centre + d.Width * 0.5f + 0.3f, HallDoorHeight * 0.5f, 0.2f),
                         Size(0.5f, HallDoorHeight, 0.4f), Quaternion.identity);
                trim.Box(At(d.Centre, HallDoorHeight + 0.3f, 0.2f),
                         Size(d.Width + 1.1f, 0.6f, 0.4f), Quaternion.identity);
            }
        }

        // ------------------------------------------------------------------
        // Roof
        // ------------------------------------------------------------------
        /// <summary>Where the roof has come in: over the nave, and a long strip over the east wing.</summary>
        private static bool HallRoofHole(float x, float z)
            => (x > -3f && x < 9f && z > -13f && z < 2f) || (x > 20f && x < 31f && z > 4f && z < 17f);

        private static void BuildHallRoof(Transform hall, int backdrop, System.Random rng)
        {
            var roof = new MeshBuild { UVScale = 0.35f };
            var sky = new MeshBuild { UVScale = 0.3f };
            var steel = new MeshBuild { UVScale = 0.5f };

            const float Eave = HallHalfWidth + 1.6f;
            const float EaveY = HallWall - 0.3f;
            const float Slot = 1.8f;

            const int panelsAlong = 20;
            const int bands = 4;

            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < panelsAlong; i++)
                    for (int b = 0; b < bands; b++)
                    {
                        float x0 = Mathf.Lerp(-HallHalfLength - 1f, HallHalfLength + 1f, i / (float)panelsAlong);
                        float x1 = Mathf.Lerp(-HallHalfLength - 1f, HallHalfLength + 1f, (i + 1f) / panelsAlong);
                        float t0 = b / (float)bands, t1 = (b + 1f) / bands;

                        float z0 = side * Mathf.Lerp(Eave, Slot, t0), z1 = side * Mathf.Lerp(Eave, Slot, t1);
                        float y0 = Mathf.Lerp(EaveY, HallRidge, t0), y1 = Mathf.Lerp(EaveY, HallRidge, t1);

                        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                        if (HallRoofHole(cx, cz)) continue;

                        // The finished wing is mostly roofed; the east wing was only begun.
                        double gone = cx < HallSplit ? 0.07 + 0.14 * t1 : 0.62 + 0.2 * t1;
                        if (rng.NextDouble() < gone) continue;

                        var a = new Vector3(x0, y0, z0); var d = new Vector3(x1, y0, z0);
                        var c = new Vector3(x1, y1, z1); var e = new Vector3(x0, y1, z1);

                        roof.Quad(a, e, c, d);
                        roof.Quad(d, c, e, a);
                    }

            // The skylight along the ridge of the finished wing, some panes out.
            for (int i = 0; i < panelsAlong; i++)
            {
                float x0 = Mathf.Lerp(-HallHalfLength - 1f, HallHalfLength + 1f, i / (float)panelsAlong);
                float x1 = Mathf.Lerp(-HallHalfLength - 1f, HallHalfLength + 1f, (i + 1f) / panelsAlong);
                if (x1 > HallSplit || HallRoofHole((x0 + x1) * 0.5f, 0f) || rng.NextDouble() < 0.24) continue;

                var a = new Vector3(x0, HallRidge + 0.1f, -Slot); var b = new Vector3(x0, HallRidge + 0.1f, Slot);
                var c = new Vector3(x1, HallRidge + 0.1f, Slot); var d = new Vector3(x1, HallRidge + 0.1f, -Slot);
                sky.Quad(a, b, c, d);
                sky.Quad(d, c, b, a);
            }

            // Trusses: a tie, two rafters, a king post, struts and a web, one at every bay. Over the
            // roof hole the far rafter has snapped and hangs.
            for (int k = 0; k <= 12; k++)
            {
                float x = TrussX(k);
                var left = new Vector3(x, EaveY, -HallHalfWidth);
                var right = new Vector3(x, EaveY, HallHalfWidth);
                var apex = new Vector3(x, HallRidge - 0.4f, 0f);
                var centre = new Vector3(x, EaveY, 0f);
                var midL = new Vector3(x, EaveY + (HallRidge - EaveY) * 0.5f - 0.2f, -HallHalfWidth * 0.5f);
                var midR = new Vector3(x, midL.y, HallHalfWidth * 0.5f);

                bool broken = HallRoofHole(x, -4f);

                steel.Tube(left, right, 0.17f, 0.17f, 4);
                steel.Tube(left, apex, 0.16f, 0.14f, 4);
                if (!broken)
                {
                    steel.Tube(right, apex, 0.16f, 0.14f, 4);
                    steel.Tube(centre, apex, 0.11f, 0.11f, 4);
                    steel.Tube(centre, midR, 0.09f, 0.09f, 3);
                    steel.Tube(midR, new Vector3(x, EaveY, HallHalfWidth * 0.78f), 0.08f, 0.08f, 3);
                }
                else
                {
                    steel.Tube(right, Vector3.Lerp(right, apex, 0.4f), 0.16f, 0.13f, 4);
                    steel.Tube(Vector3.Lerp(right, apex, 0.4f), Vector3.Lerp(right, apex, 0.4f) + new Vector3(0f, -4f, -3f), 0.13f, 0.1f, 4);
                }

                steel.Tube(centre, midL, 0.09f, 0.09f, 3);
                steel.Tube(midL, new Vector3(x, EaveY, -HallHalfWidth * 0.78f), 0.08f, 0.08f, 3);
            }

            // Purlins along the ridge, the quarters and the eaves.
            steel.Tube(new Vector3(-HallHalfLength, HallRidge - 0.4f, 0f), new Vector3(HallHalfLength, HallRidge - 0.4f, 0f), 0.14f, 0.14f, 4);
            for (int side = -1; side <= 1; side += 2)
            {
                steel.Tube(new Vector3(-HallHalfLength, EaveY + 3.4f, side * HallHalfWidth * 0.5f),
                           new Vector3(HallHalfLength, EaveY + 3.4f, side * HallHalfWidth * 0.5f), 0.1f, 0.1f, 4);
                steel.Tube(new Vector3(-HallHalfLength, EaveY, side * Eave),
                           new Vector3(HallHalfLength, EaveY, side * Eave), 0.14f, 0.14f, 4);
            }

            var roofGo = MeshObject(hall, "HallRoof", ToMesh(roof, DenseKey("hallroof")), _hallRoofMat, Vector3.zero,
                                    Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(roofGo);
            Hide(roofGo);

            EmitHall(hall, backdrop, "HallSkylight", sky, _hallSkyMat);
            EmitHall(hall, backdrop, "HallTrusses", steel, _parkSteel);
        }

        // ------------------------------------------------------------------
        // The gallery, and the three flights that reach it
        // ------------------------------------------------------------------
        /// <summary>Whether the south run of the gallery is down at this x.</summary>
        private static bool SouthGalleryGone(float x) => x > -22f && x < -12f;

        private static void BuildHallGallery(Transform hall, int layer, int backdrop, MeshBuild trim, System.Random rng)
        {
            var rail = new MeshBuild { UVScale = 0.5f };
            var rawRail = new MeshBuild { UVScale = 0.5f };
            var plank = new MeshBuild { UVScale = 0.5f };

            // ---- decks ----
            // Each run is one solid deck, so each is one collider and nothing joins to anything
            // else but its stairs.
            void Deck(string name, float x0, float x1, float z0, float z1, Material material)
            {
                float depth = Mathf.Abs(z1 - z0);
                HallDeck(hall, layer, name, new Vector3((x0 + x1) * 0.5f, GalleryY - 0.2f, (z0 + z1) * 0.5f),
                         new Vector3(x1 - x0, 0.4f, depth), material, material == _hallRaw ? "Concrete" : "Wood");

                // A fascia on the nave side, where the finished deck's edge is dressed.
                if (material != _hallRaw)
                    HallBlock(hall, layer, name + "Fascia", new Vector3((x0 + x1) * 0.5f, GalleryY - 0.75f, z0),
                              new Vector3(x1 - x0, 0.7f, 0.3f), _hallWallMat);
            }

            float zs0 = 17f, zs1 = 17f + GalleryDepth;       // south (front) run
            float zn0 = -17f, zn1 = -17f - GalleryDepth;     // north (back) run

            // South run: west half, a break where it fell, and the east half, which is raw.
            Deck("GalleryS_West", GalleryWest, -22f, zs0, zs1, _hallTrimMat);
            Deck("GalleryS_Mid", -12f, HallSplit, zs0, zs1, _hallTrimMat);
            Deck("GalleryS_Raw", HallSplit, GalleryEast, zs0, zs1, _hallRaw);

            // North run is whole.
            Deck("GalleryN", GalleryWest, HallSplit, zn0, zn1, _hallTrimMat);
            Deck("GalleryN_Raw", HallSplit, GalleryEast, zn0, zn1, _hallRaw);

            // West cross gallery above the stage, joining the two.
            Deck("GalleryW", GalleryWest, GalleryWest + GalleryDepth, zn0, zs0, _hallTrimMat);

            // ---- balustrades, finished wing only ----
            void Balustrade(float x0, float x1, float z, bool endCaps)
            {
                for (float x = x0 + 0.2f; x <= x1 - 0.1f; x += 1.7f)
                {
                    if (rng.NextDouble() < 0.12) continue;
                    rail.Box(new Vector3(x, GalleryY + 0.5f, z), new Vector3(0.1f, 1f, 0.1f), Quaternion.identity);
                }

                rail.Box(new Vector3((x0 + x1) * 0.5f, GalleryY + 1.0f, z), new Vector3(x1 - x0, 0.1f, 0.12f), Quaternion.identity);
                if (rng.NextDouble() < 0.75)
                    rail.Box(new Vector3((x0 + x1) * 0.5f, GalleryY + 0.5f, z), new Vector3(x1 - x0, 0.06f, 0.08f), Quaternion.identity);
            }

            Balustrade(GalleryWest + GalleryDepth, -22f, zs0, false);
            Balustrade(-12f, HallSplit, zs0, false);
            Balustrade(GalleryWest + GalleryDepth, HallSplit, zn0, false);

            // The cross gallery's rail faces east, over the stage... and the nave.
            for (float z = zn0 + 0.2f; z <= zs0 - 0.1f; z += 1.7f)
            {
                if (rng.NextDouble() < 0.12) continue;
                rail.Box(new Vector3(GalleryWest + GalleryDepth, GalleryY + 0.5f, z), new Vector3(0.1f, 1f, 0.1f), Quaternion.identity);
            }

            rail.Box(new Vector3(GalleryWest + GalleryDepth, GalleryY + 1.0f, 0f), new Vector3(0.12f, 0.1f, zs0 - zn0), Quaternion.identity);

            // The raw wing's edge: a few uprights with rebar bent over, and one plank guard.
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side > 0 ? zs0 : zn0;
                for (float x = HallSplit + 2f; x < GalleryEast - 1f; x += 5.4f)
                {
                    rawRail.Tube(new Vector3(x, GalleryY, z), new Vector3(x, GalleryY + 1.5f, z), 0.04f, 0.04f, 3);
                    rawRail.Tube(new Vector3(x, GalleryY + 1.5f, z), new Vector3(x + 0.4f, GalleryY + 1.3f, z + side * 0.4f), 0.035f, 0.035f, 3);
                }
            }

            // The break: planks and joists hanging from where the deck stops.
            for (int i = 0; i < 6; i++)
            {
                float x = -12f - 0.3f - i * 0.45f;
                float drop = Rand(rng, 1.2f, 4.2f);
                plank.Box(new Vector3(x, GalleryY - 0.2f - drop * 0.5f, zs0 + GalleryDepth * 0.5f + Rand(rng, -1.5f, 1.5f)),
                          new Vector3(0.34f, drop, 0.14f), Quaternion.Euler(Rand(rng, -10f, 10f), 0f, Rand(rng, -25f, 25f)));
            }

            // A section of deck that came down and lies on the floor below the break.
            HallBlock(hall, layer, "FallenDeck", new Vector3(-17f, 0.55f, zs0 + 3.8f), new Vector3(6.4f, 0.3f, 3.6f),
                      _hallTrimMat, "Wood", 12f).transform.localRotation = Quaternion.Euler(8f, 12f, 6f);

            EmitHall(hall, backdrop, "GalleryRail", rail, _parkSteel);
            EmitHall(hall, backdrop, "GalleryRawRail", rawRail, _hallRust);
            EmitHall(hall, backdrop, "GalleryBreak", plank, _parkTimber);

            // ---- the stairs ----
            // Grand stair: five metres wide, in the nave, up to the middle of the west cross gallery.
            float grandX = GalleryWest + GalleryDepth + FgStairRun(GallerySteps);
            FgStair(hall, layer, _hallTrimMat, _parkSteel, new Vector3(grandX, 0f, 0f), 270f, GallerySteps, 5f);

            // Plain flights at the east end of each run, in the unfinished wing.
            float plainX = GalleryEast + FgStairRun(GallerySteps);
            FgStair(hall, layer, _hallRaw, _hallRust, new Vector3(plainX, 0f, (zs0 + zs1) * 0.5f), 270f, GallerySteps, 3.2f);
            FgStair(hall, layer, _hallRaw, _hallRust, new Vector3(plainX, 0f, (zn0 + zn1) * 0.5f), 270f, GallerySteps, 3.2f);

            // The raw slab's own steel: columns under it and a beam, so it reads as carried.
            HallBlock(hall, layer, "SlabBeamS", new Vector3((HallSplit + GalleryEast) * 0.5f, GalleryY - 0.55f, zs0 + 0.2f),
                      new Vector3(GalleryEast - HallSplit, 0.5f, 0.3f), _hallRust, "Metal");
            HallBlock(hall, layer, "SlabBeamN", new Vector3((HallSplit + GalleryEast) * 0.5f, GalleryY - 0.55f, zn0 - 0.2f),
                      new Vector3(GalleryEast - HallSplit, 0.5f, 0.3f), _hallRust, "Metal");
        }

        // ------------------------------------------------------------------
        // The floor
        // ------------------------------------------------------------------
        private static void BuildHallFloor(Transform hall, int backdrop, System.Random rng)
        {
            var a = new MeshBuild { UVScale = 0.4f };
            var b = new MeshBuild { UVScale = 0.4f };
            var rug = new MeshBuild { UVScale = 0.4f };
            var litter = new MeshBuild { UVScale = 0.4f };
            var raw = new MeshBuild { UVScale = 0.3f };
            var mud = new MeshBuild { UVScale = 0.3f };

            const float y = 0.05f;

            // Tiles in the finished wing.
            const float cell = 2.5f;
            int nx = Mathf.RoundToInt((HallSplit + HallHalfLength) / cell);
            int nz = Mathf.RoundToInt(HallHalfWidth * 2f / cell);

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x0 = -HallHalfLength + i * cell, z0 = -HallHalfWidth + j * cell;
                    var build = (i + j) % 2 == 0 ? a : b;

                    // A few tiles are up, showing the slab underneath.
                    if (rng.NextDouble() < 0.04) continue;

                    build.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z0 + cell), new Vector3(x0 + cell, y, z0 + cell),
                               new Vector3(x0 + cell, y, z0));
                }

            // The raw slab to the east: one sheet, with the ground showing through where it was not poured.
            raw.Quad(new Vector3(HallSplit, y, -HallHalfWidth), new Vector3(HallSplit, y, HallHalfWidth),
                     new Vector3(HallHalfLength - 3f, y, HallHalfWidth), new Vector3(HallHalfLength - 3f, y, -HallHalfWidth));

            for (int i = 0; i < 9; i++)
            {
                float cx = Rand(rng, HallSplit + 3f, HallHalfLength - 4f), cz = Rand(rng, -HallHalfWidth + 3f, HallHalfWidth - 3f);
                float r = Rand(rng, 1.5f, 3.6f);
                for (int s = 0; s < 8; s++)
                {
                    float a0 = s * Mathf.PI * 2f / 8f, a1 = (s + 1) * Mathf.PI * 2f / 8f;
                    mud.Tri(new Vector3(cx, y + 0.03f, cz),
                            new Vector3(cx + Mathf.Cos(a1) * r * Rand(rng, 0.7f, 1f), y + 0.03f, cz + Mathf.Sin(a1) * r * Rand(rng, 0.7f, 1f)),
                            new Vector3(cx + Mathf.Cos(a0) * r * Rand(rng, 0.7f, 1f), y + 0.03f, cz + Mathf.Sin(a0) * r * Rand(rng, 0.7f, 1f)));
                }
            }

            // A runner down the nave, worn through in places.
            for (int i = 0; i < 20; i++)
            {
                if (rng.NextDouble() < 0.16) continue;
                float x0 = -31f + i * 2.4f, x1 = x0 + 2.4f;
                if (x1 > HallSplit) break;
                rug.Quad(new Vector3(x0, y + 0.015f, -2.4f), new Vector3(x0, y + 0.015f, 2.4f),
                         new Vector3(x1, y + 0.015f, 2.4f), new Vector3(x1, y + 0.015f, -2.4f));
            }

            // Leaf litter blown in at the doors and under the roof holes.
            for (int i = 0; i < 120; i++)
            {
                bool nearHole = rng.NextDouble() < 0.45;
                float x = nearHole ? Rand(rng, -3f, 9f) : Rand(rng, -HallHalfLength + 2f, HallSplit - 2f);
                float z = nearHole ? Rand(rng, -13f, 2f) : (rng.NextDouble() < 0.5 ? Rand(rng, -HallHalfWidth + 1f, -HallHalfWidth + 6f) : Rand(rng, HallHalfWidth - 6f, HallHalfWidth - 1f));
                float r = Rand(rng, 0.25f, 0.8f), turn = Rand(rng, 0f, 6.283f);

                var p0 = new Vector3(x + Mathf.Cos(turn) * r, y + 0.03f, z + Mathf.Sin(turn) * r);
                var p1 = new Vector3(x + Mathf.Cos(turn + 2.2f) * r * 0.8f, y + 0.03f, z + Mathf.Sin(turn + 2.2f) * r * 0.8f);
                var p2 = new Vector3(x + Mathf.Cos(turn + 4.1f) * r, y + 0.03f, z + Mathf.Sin(turn + 4.1f) * r);
                litter.Tri(p0, p2, p1);
            }

            Floor(hall, backdrop, "HallTileA", a, _hallTileA);
            Floor(hall, backdrop, "HallTileB", b, _hallTileB);
            Floor(hall, backdrop, "HallRug", rug, _hallRugMat);
            Floor(hall, backdrop, "HallRawSlab", raw, _hallRaw);
            Floor(hall, backdrop, "HallMud", mud, _parkRubble);
            Floor(hall, backdrop, "HallLitter", litter, _fgLeafA);
        }

        private static void Floor(Transform hall, int backdrop, string name, MeshBuild build, Material material)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(hall, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material, Vector3.zero,
                                Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Hide(go);
        }

        // ------------------------------------------------------------------
        // The stage, under the west gallery
        // ------------------------------------------------------------------
        private static void BuildHallStage(Transform hall, int layer, int backdrop, MeshBuild trim, System.Random rng)
        {
            const float X0 = -39.2f, X1 = -33.4f;
            float xc = (X0 + X1) * 0.5f;
            const float Height = 5 * StairRise;

            // A raised, walkable platform; two stairs of five risers lead up to it from the nave.
            HallDeck(hall, layer, "Stage", new Vector3(xc, Height * 0.5f, 0f), new Vector3(X1 - X0, Height, 17.4f), _parkTimber);
            // The lip is in three lengths with a gap at each stair: a wall across the head of a flight
            // is a wall the flight cannot be climbed over.
            foreach (var (z0, z1) in new[] { (-8.9f, -7.4f), (-4.6f, 4.6f), (7.4f, 8.9f) })
                HallBlock(hall, layer, "StageLip", new Vector3(X1 + 0.1f, Height * 0.5f + 0.05f, (z0 + z1) * 0.5f),
                          new Vector3(0.25f, Height + 0.1f, z1 - z0), _hallTrimMat);
            foreach (float z in new[] { -6f, 6f })
                FgStair(hall, layer, _parkTimber, _parkSteel, new Vector3(X1 + 1.5f, 0f, z), 270f, 5, 2.6f, rails: false);

            // The painted backdrop and its sun.
            HallBlock(hall, layer, "StageFlat", new Vector3(X0 + 0.2f, 3.6f, 0f), new Vector3(0.3f, 5.2f, 15.4f), _hallClothB, "Wood");
            trim.Tube(new Vector3(X0 + 0.5f, 4.6f, 0f), new Vector3(X0 + 0.62f, 4.6f, 0f), 2.8f, 2.8f, 24);

            for (int side = -1; side <= 1; side += 2)
                HallBlock(hall, layer, "Curtain", new Vector3(X1 - 0.6f, 3.4f + Height, side * 7.8f),
                          new Vector3(0.25f, 5.4f, 1.8f), _hallClothA, "Wood", side * 6f);

            HallBlock(hall, layer, "StageRig", new Vector3(X1 - 2.4f, GalleryY - 1.6f, 0f), new Vector3(0.3f, 0.3f, 15.4f), _parkSteel, "Metal");
            for (int i = -3; i <= 3; i++)
                trim.Box(new Vector3(X1 - 2.4f, GalleryY - 2.1f, i * 2.1f), new Vector3(0.5f, 0.55f, 0.5f), Quaternion.Euler(0f, 0f, 20f));

            HallBlock(hall, layer, "Lectern", new Vector3(X1 - 1.4f, Height + 0.55f, 2.2f), new Vector3(0.9f, 1.1f, 0.7f),
                      _parkTimber, "Wood", 12f);
        }

        // ------------------------------------------------------------------
        // Furnishings of the finished wing
        // ------------------------------------------------------------------
        private static void BuildHallFurnishings(Transform hall, int layer, int backdrop, System.Random rng)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var cloth = new MeshBuild { UVScale = 0.4f };
            var clothB = new MeshBuild { UVScale = 0.4f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var bulbs = new MeshBuild { UVScale = 0.5f };

            // ---- rows of seating facing the stage ----
            for (int row = 0; row < 5; row++)
            {
                float x = -20.5f + row * 2.5f;

                for (int side = -1; side <= 1; side += 2)
                    for (int seg = 0; seg < 2; seg++)
                    {
                        float z = side * (4.6f + seg * 4.2f);
                        if (rng.NextDouble() < 0.12) continue;
                        HallBench(wood, new Vector3(x, 0f, z), 90f + Rand(rng, -5f, 5f), rng.NextDouble() < 0.14, rng);
                    }
            }

            // ---- the food court: a counter under the north gallery, tables out from it ----
            for (int seg = 0; seg < 3; seg++)
            {
                float x = -36f + seg * 7.6f;
                HallBlock(hall, layer, "FoodCounter", new Vector3(x, 0.55f, -23.4f), new Vector3(6.4f, 1.1f, 1.0f), _parkTimber, "Wood");
                HallBlock(hall, layer, "FoodCounterTop", new Vector3(x, 1.15f, -23.4f), new Vector3(6.7f, 0.12f, 1.3f), _hallTrimMat);
                HallBlock(hall, layer, "MenuBoard", new Vector3(x, 4.2f, -24.5f), new Vector3(5.2f, 1.4f, 0.2f), seg % 2 == 0 ? _hallClothA : _hallClothB, "Wood");
            }

            for (int col = 0; col < 5; col++)
                for (int row = 0; row < 2; row++)
                {
                    if (rng.NextDouble() < 0.18) continue;
                    float x = -36f + col * 4.4f, z = -20.6f + row * 2.6f;
                    if (HallFree(x, z, 1.2f)) HallTable(wood, steel, new Vector3(x, 0f, z), rng);
                }

            // ---- display cases under the galleries, clear of the doors ----
            float[] south = { -36.5f, -26.5f, -22.5f, -9f, 4.5f };
            float[] north = { -16f, -11f, -1.5f, 12f, 16f };

            foreach (var x in south) DisplayCase(hall, layer, new Vector3(x, 0f, HallHalfWidth - 1.6f), 180f, rng, cloth, clothB);
            foreach (var x in north) DisplayCase(hall, layer, new Vector3(x, 0f, -HallHalfWidth + 1.6f), 0f, rng, cloth, clothB);

            // ---- the dry fountain in the nave ----
            HallFountain(hall, layer, new Vector3(-4f, 0f, 9f), rng);

            // ---- banners hung from the trusses ----
            for (int k = 1; k < 11; k++)
            {
                float x = TrussX(k) + 3.3f;
                if (x > HallSplit || HallRoofHole(x, -3f)) continue;

                float z = (k % 2 == 0 ? 1f : -1f) * Rand(rng, 3f, 7f);
                float length = Rand(rng, 6f, 8.4f);
                var build = k % 2 == 0 ? cloth : clothB;
                var at = new Vector3(x, HallWall - 0.4f - length * 0.5f, z);

                build.Box(at, new Vector3(0.06f, length, 2.4f), Quaternion.Euler(Rand(rng, -4f, 4f), Rand(rng, -8f, 8f), Rand(rng, -3f, 3f)));
                steel.Tube(new Vector3(x, HallWall - 0.4f, z - 1.4f), new Vector3(x, HallWall - 0.4f, z + 1.4f), 0.05f, 0.05f, 4);
            }

            // ---- ring chandeliers on chains ----
            foreach (float x in new[] { -22f, -12f })
                Chandelier(steel, bulbs, new Vector3(x, 9.4f, 0f));

            // ---- plaster and ceiling that have come down ----
            for (int i = 0; i < 26; i++)
            {
                float x = Rand(rng, -HallHalfLength + 3f, HallSplit - 2f);
                float z = Rand(rng, -HallHalfWidth + 3f, HallHalfWidth - 3f);
                if (!HallFree(x, z, 1.5f)) continue;

                float w = Rand(rng, 0.7f, 2.4f);
                HallBlock(hall, layer, "Fallen", new Vector3(x, 0.18f, z), new Vector3(w, 0.36f, w * Rand(rng, 0.5f, 1f)),
                          _parkRubble, "Concrete", Rand(rng, 0f, 360f));
            }

            if (wood.Triangles.Count > 0)
            {
                var go = MeshObject(hall, "HallFurniture", ToMesh(wood, DenseKey("hallfurniture")), _parkTimber, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Wood");
                NoStanding(go);
                Hide(go);
            }

            EmitHall(hall, backdrop, "HallBanners", cloth, _hallClothA);
            EmitHall(hall, backdrop, "HallBannersB", clothB, _hallClothB);
            EmitHall(hall, backdrop, "HallIron", steel, _parkSteel);
            EmitHall(hall, backdrop, "HallBulbs", bulbs, _fgBulb);
        }

        private static void HallBench(MeshBuild build, Vector3 foot, float yaw, bool knockedOver, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (knockedOver) rot *= Quaternion.Euler(0f, 0f, rng.NextDouble() < 0.5 ? 80f : -80f);

            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            build.Box(P(0f, 0.46f, 0f), new Vector3(3.4f, 0.1f, 0.5f), rot);
            build.Box(P(0f, 0.86f, -0.23f), new Vector3(3.4f, 0.5f, 0.07f), rot * Quaternion.Euler(-8f, 0f, 0f));
            for (int e = -1; e <= 1; e += 1)
                build.Box(P(e * 1.6f, 0.23f, 0f), new Vector3(0.08f, 0.46f, 0.46f), rot);
        }

        private static void HallTable(MeshBuild wood, MeshBuild steel, Vector3 foot, System.Random rng)
        {
            wood.Tube(foot + Vector3.up * 0.86f, foot + Vector3.up * 0.94f, 0.8f, 0.8f, 12);
            steel.Tube(foot, foot + Vector3.up * 0.88f, 0.1f, 0.07f, 5);
            steel.Tube(foot, foot + Vector3.up * 0.06f, 0.35f, 0.3f, 8);

            int chairs = 3 + rng.Next(2);
            float turn = Rand(rng, 0f, 6.283f);
            for (int i = 0; i < chairs; i++)
            {
                float a = turn + i * Mathf.PI * 2f / chairs + Rand(rng, -0.2f, 0.2f);
                var at = foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(rng, 1.15f, 1.5f);
                bool over = rng.NextDouble() < 0.18;

                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg - 90f, 0f);
                if (over) rot *= Quaternion.Euler(Rand(rng, 70f, 90f), 0f, 0f);

                Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

                wood.Box(P(0f, 0.45f, 0f), new Vector3(0.45f, 0.06f, 0.45f), rot);
                wood.Box(P(0f, 0.75f, -0.21f), new Vector3(0.45f, 0.5f, 0.05f), rot);
                for (int lx = -1; lx <= 1; lx += 2)
                    for (int lz = -1; lz <= 1; lz += 2)
                        wood.Box(P(lx * 0.19f, 0.22f, lz * 0.19f), new Vector3(0.05f, 0.44f, 0.05f), rot);
            }
        }

        private static void DisplayCase(Transform hall, int layer, Vector3 at, float yaw, System.Random rng,
                                        MeshBuild clothA, MeshBuild clothB)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            HallBlock(hall, layer, "Plinth", P(0f, 0.45f, 0f), new Vector3(2.4f, 0.9f, 1.4f), _hallTrimMat, "Wood", yaw);
            HallBlock(hall, layer, "Case", P(0f, 1.5f, 0f), new Vector3(2.2f, 1.2f, 1.2f), _hallGlassMat, "Concrete", yaw);

            var paint = _theme.RandomCoverColor(rng);
            var exhibit = CreateBlock(hall, P(0f, 1.3f, 0f), new Vector3(0.9f, 0.7f, 0.4f), yaw, layer, "Wood", paint,
                                      0.15f, 0f, "Exhibit", _parkPaint);
            NoStanding(exhibit);

            var back = rot * new Vector3(0f, 0f, -0.9f);
            var build = rng.NextDouble() < 0.5 ? clothA : clothB;
            build.Box(at + back + Vector3.up * 3.2f, new Vector3(1.6f, 3.2f, 0.06f), rot * Quaternion.Euler(0f, 0f, Rand(rng, -4f, 4f)));
        }

        private static void HallFountain(Transform hall, int layer, Vector3 at, System.Random rng)
        {
            var basin = MeshObject(hall, "FountainBasin",
                                   ToMesh(HallDisc(at, 3.3f, 0f, 0.8f, 18), DenseKey("fountainbasin")), _parkRubble,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(basin);

            var water = MeshObject(hall, "FountainWater",
                                   ToMesh(HallDisc(at, 3.05f, 0.78f, 0.84f, 18), DenseKey("fountainwater")), _fgWater,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete", collider: false);
            Hide(water);

            var tiers = new MeshBuild { UVScale = 0.5f };
            tiers.Tube(at + Vector3.up * 0.8f, at + Vector3.up * 3.4f, 0.55f, 0.32f, 10);
            tiers.Tube(at + Vector3.up * 2.0f, at + Vector3.up * 2.35f, 1.5f, 1.5f, 14);
            tiers.Tube(at + Vector3.up * 3.3f, at + Vector3.up * 3.55f, 0.85f, 0.85f, 12);

            var go = MeshObject(hall, "FountainTiers", ToMesh(tiers, DenseKey("fountaintiers")), _parkRubble,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(go);
        }

        private static MeshBuild HallDisc(Vector3 at, float radius, float y0, float y1, int sides)
        {
            var build = new MeshBuild { UVScale = 0.4f };
            build.Tube(new Vector3(at.x, y0, at.z), new Vector3(at.x, y1, at.z), radius, radius, sides);
            return build;
        }

        private static void Chandelier(MeshBuild steel, MeshBuild bulbs, Vector3 at)
        {
            steel.Tube(at + Vector3.up * 2.6f, at + Vector3.up * 0.1f, 0.03f, 0.03f, 3);

            const int arms = 12;
            for (int i = 0; i < arms; i++)
            {
                float a0 = i * Mathf.PI * 2f / arms, a1 = (i + 1) * Mathf.PI * 2f / arms;
                var p0 = at + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 1.4f;
                var p1 = at + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 1.4f;
                steel.Tube(p0, p1, 0.05f, 0.05f, 4);

                if (i % 3 == 1) continue;
                bulbs.Box(p0 + Vector3.up * 0.2f, new Vector3(0.14f, 0.28f, 0.14f), Quaternion.identity);
            }

            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                steel.Tube(at + Vector3.up * 0.1f, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.4f, 0.025f, 0.025f, 3);
            }
        }

        // ------------------------------------------------------------------
        // The unfinished wing: the site the builders left
        // ------------------------------------------------------------------
        private static void BuildHallSite(Transform hall, int layer, int backdrop, System.Random rng)
        {
            var steel = new MeshBuild { UVScale = 0.5f };
            var rust = new MeshBuild { UVScale = 0.5f };
            var brick = new MeshBuild { UVScale = 0.5f };
            var wood = new MeshBuild { UVScale = 0.5f };
            var sand = new MeshBuild { UVScale = 0.4f };
            var tarp = new MeshBuild { UVScale = 0.4f };
            var paint = new MeshBuild { UVScale = 0.5f };

            // ---- scaffold towers: lattice masts with plank decks, tied to the frame ----
            var towers = new[]
            {
                new Vector3(14f, 0f, -12f), new Vector3(22f, 0f, 13f), new Vector3(35f, 0f, -8f),
                new Vector3(30f, 0f, 11f), new Vector3(16f, 0f, 9f)
            };

            foreach (var at in towers)
            {
                float h = Rand(rng, 8f, 13f);
                Lattice(steel, at, at + Vector3.up * h, 2.2f, 0.06f, 0.035f, Mathf.RoundToInt(h / 1.6f));

                for (float level = 3f; level < h; level += 3.4f)
                    wood.Box(at + Vector3.up * level, new Vector3(2.4f, 0.1f, 2.4f), Quaternion.identity);

                // A tarp lashed to one face, half torn away.
                Cloth(tarp, at + new Vector3(-1.2f, h - 0.6f, 1.25f), Vector3.right * 2.4f, Vector3.down * Mathf.Min(6f, h - 1.5f),
                      3, 5, 0.25f, rng.Next(1, 99999), 0.25f);
            }

            // ---- a stack of building materials, laid out where a crew would leave them ----
            int piles = 0;
            for (int attempt = 0; attempt < 60 && piles < 9; attempt++)
            {
                float x = Rand(rng, HallSplit + 3f, HallHalfLength - 4f), z = Rand(rng, -13f, 13f);
                if (!HallFree(x, z, 2f) || HallRoofHole(x, z)) continue;

                BrickPile(brick, new Vector3(x, 0.06f, z), Rand(rng, 0f, 180f), 4, 1 + rng.Next(5), rng);
                HallReserve(x, x, z, z, 1.6f);
                piles++;
            }

            // ---- sand and gravel heaps ----
            foreach (var at in new[] { new Vector3(18f, 0f, -3f), new Vector3(33f, 0f, 4f) })
            {
                float r = Rand(rng, 2.2f, 3.2f);
                sand.Tube(at, at + Vector3.up * 1.4f, r, r * 0.25f, 9);
                HallReserve(at.x, at.x, at.z, at.z, r);
            }

            // ---- rebar bundles, cut lengths lying where they were dropped ----
            for (int i = 0; i < 7; i++)
            {
                float x = Rand(rng, HallSplit + 3f, HallHalfLength - 4f), z = Rand(rng, -14f, 14f);
                if (!HallFree(x, z, 1.5f)) continue;

                float yaw = Rand(rng, 0f, 180f);
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                for (int bar = 0; bar < 7; bar++)
                {
                    var off = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward * ((bar % 4 - 1.5f) * 0.07f) + Vector3.up * (0.12f + (bar / 4) * 0.07f);
                    rust.Tube(new Vector3(x, 0f, z) + off - dir * 3.2f, new Vector3(x, 0f, z) + off + dir * 3.2f, 0.025f, 0.025f, 3);
                }

                HallBlock(hall, layer, "RebarBundle", new Vector3(x, 0.2f, z), new Vector3(0.45f, 0.4f, 6.4f), _hallRust, "Metal", yaw);
            }

            // ---- two cement mixers, a wheelbarrow or two ----
            for (int i = 0; i < 3; i++)
            {
                float x = 24f + i * 5.5f, z = Rand(rng, -9f, 7f);
                if (!HallFree(x, z, 1.5f) || HallRoofHole(x, z)) continue;

                var drum = new MeshBuild { UVScale = 0.5f };
                drum.Tube(new Vector3(x, 0.9f, z), new Vector3(x + 0.5f, 1.8f, z), 0.7f, 0.5f, 10);
                drum.Tube(new Vector3(x - 0.4f, 0f, z - 0.5f), new Vector3(x - 0.4f, 0.9f, z), 0.06f, 0.06f, 3);
                drum.Tube(new Vector3(x - 0.4f, 0f, z + 0.5f), new Vector3(x - 0.4f, 0.9f, z), 0.06f, 0.06f, 3);
                var mixer = MeshObject(hall, "Mixer", ToMesh(drum, DenseKey("mixer")), _hallRust, Vector3.zero,
                                       Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(mixer);
                Hide(mixer);
                HallReserve(x, x, z, z, 1.4f);
            }

            // ---- the excavator the contractor never came back for ----
            BuildExcavator(hall, layer, new Vector3(29f, 0f, -4f), 215f);
            HallReserve(24f, 34f, -9f, 1f);

            // ---- portable toilets and a site hut ----
            HallBlock(hall, layer, "SiteHut", new Vector3(37f, 1.3f, -19f), new Vector3(5f, 2.6f, 2.4f), _hallTarp, "Metal");
            HallBlock(hall, layer, "SiteHutRoof", new Vector3(37f, 2.7f, -19f), new Vector3(5.4f, 0.15f, 2.8f), _hallRust, "Metal");
            HallBlock(hall, layer, "PortaLoo1", new Vector3(13f, 1.15f, 19.8f), new Vector3(1.2f, 2.3f, 1.2f), _hallClothB, "Wood");
            HallBlock(hall, layer, "PortaLoo2", new Vector3(14.5f, 1.15f, 19.8f), new Vector3(1.2f, 2.3f, 1.2f), _hallClothA, "Wood");
            HallReserve(35f, 39f, -20f, -18f);

            // ---- barrier tape and hazard posts round the slab's edge ----
            for (float x = HallSplit + 3f; x < GalleryEast; x += 4.2f)
                foreach (float z in new[] { -16f, 16f })
                {
                    paint.Tube(new Vector3(x, 0f, z), new Vector3(x, 1.1f, z), 0.04f, 0.04f, 3);
                    if (x + 4.2f < GalleryEast && rng.NextDouble() < 0.7)
                        paint.Tube(new Vector3(x, 1.0f, z), new Vector3(x + 4.2f, 0.95f, z), 0.012f, 0.012f, 3);
                }

            // ---- steel left lying: a girder, and I-beam offcuts ----
            for (int i = 0; i < 4; i++)
            {
                float x = Rand(rng, HallSplit + 4f, HallHalfLength - 4f), z = Rand(rng, -14f, 14f);
                if (!HallFree(x, z, 2f)) continue;
                float yaw = Rand(rng, 0f, 180f);
                HallBlock(hall, layer, "Girder", new Vector3(x, 0.25f, z), new Vector3(0.4f, 0.5f, 6f), _hallRust, "Metal", yaw);
            }

            // ---- a fallen roof truss, still lying across the slab ----
            var fallen = new MeshBuild { UVScale = 0.5f };
            FlatTruss(fallen, new Vector3(12.5f, 0.6f, -4f), new Vector3(20f, 3.8f, 12f), Vector3.up * 1.4f, 1.4f, 0.12f, 0.07f, 9);
            EmitHallSolid(hall, layer, "FallenTruss", fallen, _hallRust);

            EmitHall(hall, backdrop, "SiteScaffold", steel, _parkSteel);
            EmitHall(hall, backdrop, "SiteRebar", rust, _hallRust);
            EmitHall(hall, backdrop, "SitePlanks", wood, _parkTimber);
            EmitHall(hall, backdrop, "SiteTape", paint, _parkPaint);
            EmitHall(hall, backdrop, "SiteTarps", tarp, _hallTarp);

            if (brick.Triangles.Count > 0)
            {
                var go = MeshObject(hall, "SiteBricks", ToMesh(brick, DenseKey("sitebricks")), _hallBrick, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Concrete");
                NoStanding(go);
                Hide(go);
            }

            if (sand.Triangles.Count > 0)
            {
                var go = MeshObject(hall, "SiteSand", ToMesh(sand, DenseKey("sitesand")), _parkRubble, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Concrete");
                NoStanding(go);
                Hide(go);
            }
        }

        private static void BuildExcavator(Transform parent, int layer, Vector3 at, float yaw)
        {
            var body = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            // Tracks, undercarriage, cab, counterweight, boom, stick and bucket.
            for (int s = -1; s <= 1; s += 2)
            {
                dark.Box(P(s * 1.2f, 0.45f, 0f), new Vector3(0.7f, 0.9f, 3.6f), rot);
                dark.Tube(P(s * 1.2f, 0.45f, -1.8f), P(s * 1.2f, 0.45f, -1.8f) + rot * Vector3.right * 0.01f, 0.45f, 0.45f, 8);
            }

            body.Box(P(0f, 1.1f, 0f), new Vector3(2.4f, 0.5f, 2.8f), rot);
            body.Box(P(-0.5f, 2.1f, -0.3f), new Vector3(1.4f, 1.7f, 1.7f), rot);
            body.Box(P(0f, 1.6f, -1.6f), new Vector3(2.2f, 1.0f, 0.9f), rot);
            body.Tube(P(0.3f, 1.7f, 0.9f), P(0.3f, 3.6f, 3.4f), 0.22f, 0.17f, 5);
            body.Tube(P(0.3f, 3.6f, 3.4f), P(0.3f, 1.2f, 5.6f), 0.17f, 0.13f, 5);
            body.Box(P(0.3f, 0.9f, 5.9f), new Vector3(1.1f, 0.9f, 0.9f), rot * Quaternion.Euler(-30f, 0f, 0f));

            var a = MeshObject(parent, "ExcavatorBody", ToMesh(body, DenseKey("excavator")), _parkPaint,
                               Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(a);
            var b = MeshObject(parent, "ExcavatorTracks", ToMesh(dark, DenseKey("excavatortracks")), _parkDark,
                               Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(b);
        }

        // ------------------------------------------------------------------
        // The tower crane, outside the east wing
        // ------------------------------------------------------------------
        private static void BuildHallCrane(Transform hall, int layer, int backdrop, System.Random rng)
        {
            var steel = new MeshBuild { UVScale = 0.5f };
            var cab = new MeshBuild { UVScale = 0.5f };

            var foot = new Vector3(33f, 0f, -33f);
            const float Mast = 46f;

            Lattice(steel, foot, foot + Vector3.up * Mast, 2.6f, 0.11f, 0.06f, 23);

            // The jib reaches over the hall and swings round, with the counter-jib behind it.
            var top = foot + Vector3.up * Mast;
            var jibDir = new Vector3(-0.8f, 0f, 0.6f).normalized;
            FlatTruss(steel, top + Vector3.up * 0.6f - jibDir * 12f, top + Vector3.up * 0.6f + jibDir * 40f, Vector3.up * 2.4f, 2.4f, 0.09f, 0.05f, 22);
            steel.Tube(top + Vector3.up * 7.5f, top + Vector3.up * 3f + jibDir * 36f, 0.04f, 0.04f, 3);
            steel.Tube(top + Vector3.up * 7.5f, top + Vector3.up * 3f - jibDir * 10f, 0.04f, 0.04f, 3);
            steel.Tube(top + Vector3.up * 3f, top + Vector3.up * 7.5f, 0.12f, 0.12f, 4);

            cab.Box(top + Vector3.up * 1.2f + jibDir * 2.5f, new Vector3(1.8f, 2.2f, 2.4f), Quaternion.LookRotation(jibDir));
            cab.Box(top + Vector3.up * 1.4f - jibDir * 11f, new Vector3(3.2f, 2.6f, 4.2f), Quaternion.LookRotation(jibDir));

            // The hook block and its cable, at the end of the trolley's run.
            Cable(steel, top + jibDir * 30f + Vector3.up * 0.4f, top + jibDir * 30f + Vector3.down * 24f, 0.5f, 0.05f, 5);
            cab.Box(top + jibDir * 30f + Vector3.down * 24.5f, new Vector3(0.8f, 1.1f, 0.5f), Quaternion.LookRotation(jibDir));

            var go = MeshObject(hall, "CraneSteel", ToMesh(steel, DenseKey("cranesteel")), _hallRust, Vector3.zero,
                                Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(go);
            Hide(go);

            var c = MeshObject(hall, "CraneCab", ToMesh(cab, DenseKey("cranecab")), _parkPaint, Vector3.zero,
                               Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(c);
            Hide(c);

            // Concrete footings.
            HallBlock(hall, layer, "CraneFooting", foot + new Vector3(0f, 0.5f, 0f), new Vector3(5f, 1f, 5f), _hallRaw);

            // A red aircraft-warning lamp at the very top.
            var lamp = new GameObject("CraneLamp");
            lamp.transform.SetParent(hall, false);
            lamp.transform.localPosition = top + Vector3.up * 8f;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.1f);
            light.intensity = 6f;
            light.range = 22f;
            light.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------
        // Greenery
        // ------------------------------------------------------------------
        private static void BuildHallGreenery(Transform hall, int layer, int backdrop, System.Random rng)
        {
            var grove = new Grove();

            // The tree that came up through the hole in the roof.
            var treeAt = new Vector3(3.0f, -0.3f, -6.0f);
            JungleTree(grove, rng, treeAt, 1.1f);
            Bush(grove.Under, treeAt + new Vector3(1.8f, 0.3f, 0.6f), 1.3f, 17);
            Bush(grove.Under, treeAt + new Vector3(-1.6f, 0.3f, -1.2f), 1.1f, 31);
            Fern(grove.Under, treeAt + new Vector3(0.4f, 0.3f, 2.0f), 1.5f, 5);

            // A second one in the east roof gap, which has been open longer.
            var treeB = new Vector3(25.5f, -0.3f, 10.5f);
            JungleTree(grove, rng, treeB, 0.95f);
            Fern(grove.Under, treeB + new Vector3(1.4f, 0.3f, 1f), 1.6f, 9);

            // Planters between the columns, each with a palm or a clump of fern.
            float[] xs = { -26f, -17f, -8f, 4f };
            int n = 0;
            foreach (var x in xs)
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = side * 13.0f;
                    if (!HallFree(x, z, 1.2f)) continue;

                    HallBlock(hall, layer, "Planter", new Vector3(x, 0.35f, z), new Vector3(1.5f, 0.7f, 1.5f), _parkRubble);
                    var foot = new Vector3(x, 0.55f, z);

                    if (n++ % 2 == 0) JunglePalm(grove, rng, foot, 0.62f);
                    else
                    {
                        Fern(grove.Under, foot, 1.6f, n * 7);
                        Bush(grove.Under, foot + new Vector3(0.2f, 0f, 0.1f), 0.9f, n * 11);
                    }
                }

            // Vines coming down through the roof openings and off the gallery.
            for (int i = 0; i < 16; i++)
            {
                var from = new Vector3(Rand(rng, -2f, 9f), HallWall + Rand(rng, 0.5f, 3f), Rand(rng, -12f, 1.5f));
                Vine(grove.Under, from, Rand(rng, 4f, 9f), rng.Next(1, 99999));
            }

            for (int i = 0; i < 14; i++)
            {
                float side = rng.NextDouble() < 0.5 ? 1f : -1f;
                var from = new Vector3(Rand(rng, -32f, 8f), GalleryY + 0.9f, side * 17f);
                Vine(grove.Under, from, Rand(rng, 2.5f, 4.6f), rng.Next(1, 99999));
            }

            if (grove.Trunks.Triangles.Count > 0)
            {
                var trunks = MeshObject(hall, "HallTreeTrunk", ToMesh(grove.Trunks, DenseKey("halltrunk")), _fgBark,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
                Hide(trunks);
            }

            EmitFoliage(hall, backdrop, "HallCrownA", grove.LeafA, _fgLeafA);
            EmitFoliage(hall, backdrop, "HallCrownB", grove.LeafB, _fgLeafB);
            EmitFoliage(hall, backdrop, "HallUndergrowth", grove.Under, _fgFern);
        }

        // ------------------------------------------------------------------
        // The forecourt: portico, plaza, fountain
        // ------------------------------------------------------------------
        private static void BuildHallForecourt(Transform hall, int layer, int backdrop, MeshBuild trim, System.Random rng)
        {
            const float Z = HallHalfWidth + 2.4f;

            foreach (float x in new[] { -8.6f, -5.2f, 5.2f, 8.6f })
            {
                HallBlock(hall, layer, "PorticoColumn", new Vector3(x, 4.1f, Z), new Vector3(0.95f, 8.2f, 0.95f), _hallWallMat);
                trim.Box(new Vector3(x, 0.4f, Z), new Vector3(1.4f, 0.8f, 1.4f), Quaternion.identity);
                trim.Box(new Vector3(x, 8.4f, Z), new Vector3(1.5f, 0.5f, 1.5f), Quaternion.identity);
            }

            HallBlock(hall, layer, "Entablature", new Vector3(0f, 9.1f, Z), new Vector3(19.8f, 1.3f, 1.7f), _hallTrimMat);

            var p = new MeshBuild { UVScale = 0.4f };
            float zf = Z + 0.85f, zb = Z - 0.85f, y0 = 9.75f, y1 = 12.4f, hw = 9.9f;
            p.Tri(new Vector3(-hw, y0, zf), new Vector3(hw, y0, zf), new Vector3(0f, y1, zf));
            p.Tri(new Vector3(hw, y0, zb), new Vector3(-hw, y0, zb), new Vector3(0f, y1, zb));
            p.Quad(new Vector3(-hw, y0, zb), new Vector3(-hw, y0, zf), new Vector3(0f, y1, zf), new Vector3(0f, y1, zb));
            p.Quad(new Vector3(hw, y0, zf), new Vector3(hw, y0, zb), new Vector3(0f, y1, zb), new Vector3(0f, y1, zf));

            var ped = MeshObject(hall, "Pediment", ToMesh(p, DenseKey("pediment")), _hallWallMat, Vector3.zero,
                                 Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(ped);
            Hide(ped);

            trim.Box(new Vector3(0f, 9.1f, zf), new Vector3(9.4f, 0.8f, 0.12f), Quaternion.identity);
            var letters = new MeshBuild { UVScale = 0.4f };
            for (int i = 0; i < 11; i++)
            {
                float x = -4.2f + i * 0.84f;
                if (i == 5) continue;
                float h = i % 3 == 0 ? 0.6f : 0.5f;
                letters.Box(new Vector3(x, 9.1f, zf + 0.12f), new Vector3(0.26f, h, 0.1f), Quaternion.identity);
            }

            EmitHall(hall, backdrop, "HallLettering", letters, _fgBrass);

            // Wide steps from the forecourt up to the doors? No -- level, because the ground is.
            // The plaza is paving over it; the walkway disc outside does the rest.
            foreach (float x in new[] { -13.5f, 13.5f })
            {
                HallBlock(hall, layer, "PlazaPlanter", new Vector3(x, 0.45f, HallHalfWidth + 5f),
                          new Vector3(2.6f, 0.9f, 2.6f), _parkRubble);

                var grove = new Grove();
                JunglePalm(grove, rng, new Vector3(x, 0.8f, HallHalfWidth + 5f), 0.8f);
                EmitFoliage(hall, backdrop, "PlanterFronds", grove.LeafB, _fgLeafB);
                var trunk = MeshObject(hall, "PlanterTrunk", ToMesh(grove.Trunks, DenseKey("planttrunk")), _fgBark,
                                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
                Hide(trunk);
            }

            HallFountain(hall, layer, new Vector3(-22f, 0f, HallHalfWidth + 12f), rng);

            // The hoarding on the unfinished corner: COMING SOON, in a colour that has faded to nothing.
            HallBlock(hall, layer, "Hoarding", new Vector3(30f, 2.2f, HallHalfWidth + 3.4f), new Vector3(16f, 4.4f, 0.25f),
                      _hallClothB, "Wood");
            HallBlock(hall, layer, "HoardingBanner", new Vector3(30f, 3.2f, HallHalfWidth + 3.52f), new Vector3(11f, 1.6f, 0.1f),
                      _hallClothA, "Wood");
        }

        // ------------------------------------------------------------------
        // Light
        // ------------------------------------------------------------------
        private static void BuildHallLights(Transform hall, System.Random rng)
        {
            void Point(string name, Vector3 at, float range, float intensity, Color color)
            {
                var go = new GameObject(name);
                go.transform.SetParent(hall, false);
                go.transform.localPosition = at;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }

            void Spot(string name, Vector3 at, Vector3 target, float angle, float range, float intensity, Color color)
            {
                var go = new GameObject(name);
                go.transform.SetParent(hall, false);
                go.transform.localPosition = at;
                go.transform.localRotation = Quaternion.LookRotation((target - at).normalized);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = angle;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }

            var warm = new Color(1f, 0.70f, 0.40f);

            foreach (float x in new[] { -22f, -12f })
                Point($"Chandelier{x}", new Vector3(x, 9.2f, 0f), 24f, 4.6f, warm);

            Point("FoodCourtLight", new Vector3(-30f, 4.5f, -21f), 17f, 3.4f, new Color(1f, 0.62f, 0.34f));
            Point("PorticoLightL", new Vector3(-6.8f, 6.2f, HallHalfWidth + 3f), 14f, 3.6f, warm);
            Point("PorticoLightR", new Vector3(6.8f, 6.2f, HallHalfWidth + 3f), 14f, 3.6f, warm);
            Point("GalleryLight", new Vector3(-30f, GalleryY + 2.6f, 0f), 18f, 3.2f, warm);

            var cold = new Color(0.62f, 0.66f, 0.95f);
            Spot("SkylightShaft", new Vector3(-14f, HallRidge + 1f, 0f), new Vector3(-14f, 0f, 1f), 50f, 30f, 11f, cold);
            Spot("RoofHoleShaft", new Vector3(3f, HallRidge + 3f, -6f), new Vector3(3f, 0f, -6f), 46f, 32f, 14f, cold);
            Spot("EastGapShaft", new Vector3(25.5f, HallRidge + 3f, 10.5f), new Vector3(25.5f, 0f, 10.5f), 46f, 32f, 12f, cold);
            Spot("StageSpot", new Vector3(-28f, GalleryY - 2f, 0f), new Vector3(-37f, 1.2f, 0f), 62f, 22f, 15f, new Color(1f, 0.82f, 0.58f));

            // The one work light somebody left on.
            Spot("SiteFloodlight", new Vector3(34f, 6f, 14f), new Vector3(26f, 0f, 2f), 70f, 30f, 12f, new Color(0.9f, 0.95f, 1f));
        }
    }
}
#endif
