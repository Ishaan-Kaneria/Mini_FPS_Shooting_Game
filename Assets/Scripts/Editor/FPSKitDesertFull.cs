#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert made full (2026-09-26). Ishaan: Industrial "is good and real looking, but
    /// not others ... take desert, and make it full. By full, I mean, fill the space with
    /// appropriate things, and make it real." Asked what should fill it, he chose a
    /// military base, a real village in place of the box town and wild desert detail;
    /// dense like Industrial; about a third of the houses enterable with a stair to the
    /// roof; the river, the bridges, the plane, the wrecks and the dune terrain kept, and
    /// the oasis dropped.
    ///
    /// This file is the shared kit and the base (FOB). FPSKitDesertVillage.cs is the
    /// village, FPSKitDesertWild.cs the farm compounds, camps, checkpoints, power line and
    /// the scrub, wadis and junk on the open sand.
    ///
    /// <b>Everything here is laid out in a <see cref="Frame"/> turned by a multiple of
    /// ninety degrees.</b> That keeps every wall on a world axis, which is what SealBox and
    /// AdobeStair can describe; a base turned by thirty-one degrees would need both
    /// rewritten for no gain a player could see.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Plans
        // ==================================================================
        /// <summary>A place laid out in its own frame: centre, extent and a quarter-turn.</summary>
        private struct SitePlan
        {
            public Vector2 Centre;
            public float Width, Depth, Yaw;

            /// <summary>Half the footprint along world x and z, after the turn.</summary>
            public Vector2 HalfWorld => Mathf.Abs(Mathf.Sin(Yaw * Mathf.Deg2Rad)) > 0.5f
                ? new Vector2(Depth * 0.5f, Width * 0.5f)
                : new Vector2(Width * 0.5f, Depth * 0.5f);

            public bool Contains(Vector2 p, float margin)
            {
                var h = HalfWorld;
                return Mathf.Abs(p.x - Centre.x) < h.x + margin && Mathf.Abs(p.y - Centre.y) < h.y + margin;
            }
        }

        private static bool _hasFob;
        private static SitePlan _fob;
        private static readonly List<SitePlan> _farmPlans = new List<SitePlan>();
        private static readonly List<Vector2> _campSites = new List<Vector2>();
        private static readonly List<Vector3> _checkpoints = new List<Vector3>();   // x, z, yaw
        private static readonly List<Vector2> _poleLine = new List<Vector2>();

        /// <summary>Cleared with the rest of the desert's plans; see ResetDesertLife.</summary>
        private static void ResetDesertFull()
        {
            _hasFob = false;
            _farmPlans.Clear();
            _hamletPlans.Clear();
            _campSites.Clear();
            _checkpoints.Clear();
            _poleLine.Clear();
        }

        // ==================================================================
        // A turned frame
        // ==================================================================
        /// <summary>
        /// A site's own axes: u across, v deep, y up from its floor. P gives the world point,
        /// Rot the turn to hand a box, and Axis the world direction of a local one.
        /// </summary>
        private readonly struct Frame
        {
            public readonly Vector3 Origin;
            public readonly Quaternion Rot;
            public readonly float Yaw;

            public Frame(Vector3 origin, float yaw)
            {
                Origin = origin;
                Yaw = yaw;
                Rot = Quaternion.Euler(0f, yaw, 0f);
            }

            public Vector3 P(float u, float y, float v) => Origin + Rot * new Vector3(u, y, v);
            public Vector2 P2(float u, float v) { var p = P(u, 0f, v); return new Vector2(p.x, p.z); }
            public Vector3 Axis(Vector3 local) => Rot * local;

            /// <summary>A local size as the world-axis size it covers (quarter-turns only).</summary>
            public Vector3 WorldSize(Vector3 local)
            {
                var w = Rot * local;
                return new Vector3(Mathf.Abs(w.x), Mathf.Abs(w.y), Mathf.Abs(w.z));
            }
        }

        /// <summary>A gap in a wall: where along it, how wide, and from what height to what.</summary>
        private readonly struct Opening
        {
            public readonly float At, Width, Bottom, Top;
            public Opening(float at, float width, float bottom, float top) { At = at; Width = width; Bottom = bottom; Top = top; }
        }

        /// <summary>
        /// A straight wall from a to b (local u, v), in panels round its openings: full height
        /// between them, and a sill below and a lintel above each one. A door is an opening
        /// whose bottom is zero.
        /// </summary>
        private static void WallRun(MeshBuild build, Frame f, Vector2 a, Vector2 b, float baseY, float height,
                                    float thick, List<Opening> openings = null)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.05f) return;
            var dir = d / length;
            var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg, 0f);

            void Panel(float s0, float s1, float y0, float y1)
            {
                if (s1 - s0 < 0.04f || y1 - y0 < 0.04f) return;
                var mid = a + dir * ((s0 + s1) * 0.5f);
                build.Box(f.P(mid.x, baseY + (y0 + y1) * 0.5f, mid.y), new Vector3(s1 - s0, y1 - y0, thick), rot);
            }

            float s = 0f;
            if (openings != null)
            {
                openings.Sort((x, y) => x.At.CompareTo(y.At));
                foreach (var o in openings)
                {
                    float o0 = Mathf.Clamp(o.At - o.Width * 0.5f, s, length);
                    float o1 = Mathf.Clamp(o.At + o.Width * 0.5f, o0, length);
                    Panel(s, o0, 0f, height);
                    Panel(o0, o1, 0f, o.Bottom);
                    Panel(o0, o1, Mathf.Min(o.Top, height), height);
                    s = o1;
                }
            }

            Panel(s, length, 0f, height);
        }

        /// <summary>A navigation seal over a local box, for a frame turned by a quarter.</summary>
        private static void SealLocal(Transform parent, Frame f, float u, float y, float v, Vector3 size)
            => SealBox(parent, f.P(u, y, v), f.WorldSize(size));

        /// <summary>
        /// A solid mud-brick stair in a frame: the flight climbs along <paramref name="upLocal"/>
        /// (a local axis) and lands with its landing centred at (u, v). See AdobeStair.
        /// </summary>
        private static void StairLocal(Transform parent, int layer, Material mat, Frame f, float u, float v,
                                       Vector3 upLocal, float height)
        {
            var up = f.Axis(upLocal);
            up = new Vector3(Mathf.Round(up.x), 0f, Mathf.Round(up.z));
            AdobeStair(parent, layer, mat, f.P(u, 0f, v), up, height);
        }

        // ==================================================================
        // Materials
        // ==================================================================
        private static Material _hescoMat, _wireMat, _twallMat, _milTanMat, _milGreenMat, _tentCanvasMat, _plywoodMat,
                                _helipadMat, _paintWhiteMat, _paintRedMat, _johnBlueMat, _tankBlackMat, _dishMat, _acMat,
                                _plinthMat, _glassDarkMat, _scrubMat, _saltbushMat, _scrubDryMat, _grassDryMat, _gravelMat,
                                _rutMat, _boneMat, _corrugatedMat, _drumMat, _tyreMat, _stoneMat;
        private static Material[] _doorPaints, _containerPaints, _carPaints, _clothPaints;

        private static void ResolveDesertFullMaterials()
        {
            // Every one a detail material: MakeMaterial never updates a colour once it has
            // been saved, and a palette nobody can retune is a palette that stays wrong.
            _hescoMat = MakeDetailMaterial("Hesco", new Color(0.64f, 0.57f, 0.42f), "Timber", 1.4f, 0.05f, 0f, 1.2f);
            _wireMat = MakeDetailMaterial("HescoWire", new Color(0.30f, 0.29f, 0.27f), "Metal", 1f, 0.2f, 0.3f, 0.5f);
            _twallMat = MakeDetailMaterial("TWall", new Color(0.70f, 0.68f, 0.63f), "Concrete", 0.35f, 0.08f, 0f, 0.8f);
            _milTanMat = MakeDetailMaterial("MilTan", new Color(0.62f, 0.54f, 0.39f), "Metal", 0.6f, 0.14f, 0.1f, 0.35f);
            _milGreenMat = MakeDetailMaterial("MilGreen", new Color(0.33f, 0.36f, 0.24f), "Metal", 0.6f, 0.14f, 0.1f, 0.35f);
            _tentCanvasMat = MakeDetailMaterial("TentCanvas", new Color(0.60f, 0.54f, 0.41f), "Timber", 0.7f, 0.05f, 0f, 0.5f);
            _plywoodMat = MakeDetailMaterial("Plywood", new Color(0.70f, 0.58f, 0.40f), "Timber", 0.45f, 0.1f, 0f, 0.8f);
            _helipadMat = MakeDetailMaterial("Helipad", new Color(0.60f, 0.59f, 0.56f), "Concrete", 0.25f, 0.08f, 0f, 0.7f);
            _paintWhiteMat = MakeDetailMaterial("PaintWhite", new Color(0.88f, 0.87f, 0.83f), "Concrete", 0.5f, 0.1f, 0f, 0.2f);
            _paintRedMat = MakeDetailMaterial("PaintRed", new Color(0.72f, 0.14f, 0.10f), "Concrete", 0.5f, 0.1f, 0f, 0.2f);
            _johnBlueMat = MakeDetailMaterial("JohnBlue", new Color(0.16f, 0.32f, 0.58f), "Concrete", 0.5f, 0.15f, 0f, 0.2f);
            _tankBlackMat = MakeDetailMaterial("TankBlack", new Color(0.10f, 0.10f, 0.11f), "Concrete", 0.6f, 0.15f, 0f, 0.3f);
            _dishMat = MakeDetailMaterial("Dish", new Color(0.84f, 0.84f, 0.82f), "Concrete", 0.6f, 0.15f, 0f, 0.2f);
            _acMat = MakeDetailMaterial("AirCon", new Color(0.66f, 0.66f, 0.63f), "Metal", 0.8f, 0.15f, 0.2f, 0.4f);
            _plinthMat = MakeDetailMaterial("Plinth", Shade(_theme.wallColor, 0.74f), "Adobe", 0.3f, 0.06f, 0f, 0.9f);
            _glassDarkMat = MakeDetailMaterial("GlassDark", new Color(0.09f, 0.10f, 0.11f), "Concrete", 0.6f, 0.3f, 0f, 0.1f);
            _scrubMat = MakeDetailMaterial("Scrub", new Color(0.34f, 0.36f, 0.20f), "Timber", 1.2f, 0.06f, 0f, 0.6f);
            _saltbushMat = MakeDetailMaterial("Saltbush", new Color(0.50f, 0.51f, 0.39f), "Timber", 1.2f, 0.06f, 0f, 0.6f);
            _scrubDryMat = MakeDetailMaterial("ScrubDry", new Color(0.56f, 0.49f, 0.31f), "Timber", 1.2f, 0.06f, 0f, 0.6f);
            _grassDryMat = MakeDetailMaterial("GrassDry", new Color(0.72f, 0.62f, 0.40f), "Timber", 1.2f, 0.05f, 0f, 0.4f);
            _gravelMat = MakeDetailMaterial("WadiGravel", new Color(0.54f, 0.47f, 0.36f), "Rock", 0.45f, 0.08f, 0f, 1.3f);
            _rutMat = MakeDetailMaterial("Rut", new Color(0.50f, 0.42f, 0.30f), "Sand", 0.15f, 0.05f, 0f, 0.7f);
            _boneMat = MakeDetailMaterial("Bone", new Color(0.86f, 0.82f, 0.72f), "Concrete", 0.8f, 0.12f, 0f, 0.4f);
            _corrugatedMat = MakeDetailMaterial("Corrugated", new Color(0.56f, 0.52f, 0.47f), "Metal", 0.5f, 0.18f, 0.25f, 1f);
            _drumMat = MakeDetailMaterial("Drum", new Color(0.46f, 0.26f, 0.15f), "Metal", 0.7f, 0.15f, 0.2f, 0.6f);
            _tyreMat = MakeDetailMaterial("Tyre", new Color(0.09f, 0.09f, 0.09f), "Rock", 1f, 0.1f, 0f, 0.5f);
            _stoneMat = MakeDetailMaterial("FieldStone", Shade(_theme.bankColor, 1.3f), "Rock", 0.4f, 0.08f, 0f, 1.2f);

            _doorPaints = new[]
            {
                MakeDetailMaterial("DoorTeal", new Color(0.15f, 0.43f, 0.46f), "Timber", 0.8f, 0.1f, 0f, 0.8f),
                MakeDetailMaterial("DoorGreen", new Color(0.22f, 0.38f, 0.22f), "Timber", 0.8f, 0.1f, 0f, 0.8f),
                MakeDetailMaterial("DoorBlue", new Color(0.19f, 0.30f, 0.54f), "Timber", 0.8f, 0.1f, 0f, 0.8f),
                MakeDetailMaterial("DoorRed", new Color(0.50f, 0.20f, 0.14f), "Timber", 0.8f, 0.1f, 0f, 0.8f),
                _timberMat
            };

            _containerPaints = new[]
            {
                MakeDetailMaterial("ChuTan", new Color(0.70f, 0.62f, 0.47f), "Metal", 0.5f, 0.15f, 0.1f, 0.8f),
                MakeDetailMaterial("ChuSand", new Color(0.78f, 0.72f, 0.58f), "Metal", 0.5f, 0.15f, 0.1f, 0.8f),
                MakeDetailMaterial("ChuGreen", new Color(0.40f, 0.43f, 0.32f), "Metal", 0.5f, 0.15f, 0.1f, 0.8f)
            };

            _carPaints = new[]
            {
                MakeDetailMaterial("CarWhite", new Color(0.82f, 0.81f, 0.77f), "Metal", 0.6f, 0.2f, 0.1f, 0.3f),
                MakeDetailMaterial("CarRed", new Color(0.56f, 0.16f, 0.12f), "Metal", 0.6f, 0.2f, 0.1f, 0.3f),
                MakeDetailMaterial("CarBlue", new Color(0.22f, 0.32f, 0.50f), "Metal", 0.6f, 0.2f, 0.1f, 0.3f),
                MakeDetailMaterial("CarSand", new Color(0.72f, 0.64f, 0.48f), "Metal", 0.6f, 0.2f, 0.1f, 0.3f)
            };

            _clothPaints = new[] { _clothRed, _clothBlue, _clothSaffron, _clothCream };
            _sandbagMat ??= MakeDetailMaterial("Sandbag", new Color(0.62f, 0.54f, 0.38f), "Timber", 0.8f, 0.05f, 0f, 1f);
        }

        // ==================================================================
        // Shared small things
        // ==================================================================
        /// <summary>A crate, a drum or a sack: things lying about that a player can crouch behind.</summary>
        private static void Crate(MeshBuild build, Vector3 at, float size, float yaw)
            => build.Box(at + Vector3.up * size * 0.5f, new Vector3(size, size, size * 0.8f), Quaternion.Euler(0f, yaw, 0f));

        private static void Drum(MeshBuild build, Vector3 at)
        {
            build.Tube(at, at + Vector3.up * 0.9f, 0.3f, 0.3f, 10);
            build.Tube(at + Vector3.up * 0.3f, at + Vector3.up * 0.33f, 0.315f, 0.315f, 10);
            build.Tube(at + Vector3.up * 0.6f, at + Vector3.up * 0.63f, 0.315f, 0.315f, 10);
        }

        /// <summary>A wheel on its side axis: a tyre and a hub.</summary>
        private static void Wheel(MeshBuild tyres, Vector3 centre, Vector3 axis, float radius, float width)
            => tyres.Tube(centre - axis * width * 0.5f, centre + axis * width * 0.5f, radius, radius, 12);

        /// <summary>
        /// A vehicle, built round its own origin and dropped in place: a pickup, a military
        /// truck or an armoured patrol car. Solid and off the bake; none is ever driven.
        /// </summary>
        private enum VehicleKind { Pickup, Truck, Armoured }

        private static void BuildVehicle(Transform parent, int layer, System.Random rng, Vector2 at, float yaw,
                                         VehicleKind kind, Material paint)
        {
            var body = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };
            var trim = new MeshBuild { UVScale = 0.5f };

            float length, width;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var position = new Vector3(at.x, LowestGroundIn(at.x, at.y, 2.5f), at.y);

            switch (kind)
            {
                case VehicleKind.Pickup:
                    length = 5.2f; width = 1.9f;
                    body.Box(new Vector3(0f, 0.75f, 0f), new Vector3(width, 0.55f, length), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.35f, 0.9f), new Vector3(width - 0.1f, 0.75f, 1.9f), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.1f, 2.15f), new Vector3(width, 0.2f, 0.9f), Quaternion.Euler(-8f, 0f, 0f));
                    foreach (float s in new[] { -1f, 1f })
                        body.Box(new Vector3(s * (width * 0.5f - 0.05f), 1.2f, -1.4f), new Vector3(0.1f, 0.45f, 2.3f), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.2f, -2.55f), new Vector3(width, 0.45f, 0.1f), Quaternion.identity);
                    glass.Box(new Vector3(0f, 1.42f, 1.86f), new Vector3(width - 0.2f, 0.55f, 0.06f), Quaternion.Euler(-24f, 0f, 0f));
                    foreach (float s in new[] { -1f, 1f })
                        glass.Box(new Vector3(s * (width * 0.5f - 0.03f), 1.45f, 0.9f), new Vector3(0.05f, 0.45f, 1.5f), Quaternion.identity);
                    trim.Box(new Vector3(0f, 0.55f, 2.62f), new Vector3(width + 0.05f, 0.22f, 0.12f), Quaternion.identity);
                    trim.Box(new Vector3(0f, 0.55f, -2.62f), new Vector3(width + 0.05f, 0.22f, 0.12f), Quaternion.identity);
                    // Something in the back: sacks, a drum, a spare.
                    if (rng.NextDouble() < 0.6)
                    {
                        body.Box(new Vector3(0.3f, 1.2f, -1.2f), new Vector3(0.7f, 0.4f, 0.9f), Quaternion.Euler(0f, 12f, 0f));
                        Drum(trim, new Vector3(-0.45f, 1.0f, -1.9f));
                    }
                    foreach (float z in new[] { 1.6f, -1.6f })
                        foreach (float s in new[] { -1f, 1f })
                            Wheel(tyres, new Vector3(s * (width * 0.5f - 0.05f), 0.38f, z), Vector3.right, 0.38f, 0.28f);
                    break;

                case VehicleKind.Truck:
                    length = 7.6f; width = 2.4f;
                    body.Box(new Vector3(0f, 1.0f, 0f), new Vector3(width - 0.3f, 0.35f, length), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.85f, 2.9f), new Vector3(width, 1.5f, 1.8f), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.4f, 3.85f), new Vector3(width - 0.1f, 0.8f, 0.3f), Quaternion.identity);
                    // The bed, a canvas tilt over it on hoops.
                    body.Box(new Vector3(0f, 1.3f, -1.0f), new Vector3(width, 0.25f, 5f), Quaternion.identity);
                    var tilt = new MeshBuild { UVScale = 0.5f };
                    ArchShell(tilt, new Vector3(0f, 1.42f, -1.0f), width * 0.5f, 1.2f, 5f, 8, Quaternion.identity);
                    var tiltGo = MeshObject(parent, "TruckTilt", ToMesh(tilt, DenseKey("tilt")), _tentCanvasMat,
                                            position, rot, Vector3.one, layer, "Wood");
                    NoStanding(tiltGo);
                    Hide(tiltGo);
                    glass.Box(new Vector3(0f, 2.15f, 3.82f), new Vector3(width - 0.3f, 0.6f, 0.06f), Quaternion.identity);
                    foreach (float z in new[] { 2.9f, -1.4f, -2.8f })
                        foreach (float s in new[] { -1f, 1f })
                            Wheel(tyres, new Vector3(s * (width * 0.5f - 0.2f), 0.52f, z), Vector3.right, 0.52f, 0.4f);
                    break;

                default:
                    length = 6.2f; width = 2.5f;
                    // A v-hulled patrol car: sloped lower flanks, a boxy cabin, a turret ring.
                    body.Box(new Vector3(0f, 1.05f, 0f), new Vector3(width - 0.5f, 0.7f, length), Quaternion.identity);
                    foreach (float s in new[] { -1f, 1f })
                        body.Box(new Vector3(s * (width * 0.5f - 0.35f), 1.0f, 0f), new Vector3(0.3f, 0.75f, length - 0.4f), Quaternion.Euler(0f, 0f, s * 28f));
                    body.Box(new Vector3(0f, 2.0f, -0.3f), new Vector3(width, 1.3f, 4.6f), Quaternion.identity);
                    body.Box(new Vector3(0f, 1.45f, 2.55f), new Vector3(width - 0.1f, 0.5f, 1.1f), Quaternion.Euler(10f, 0f, 0f));
                    trim.Tube(new Vector3(0f, 2.65f, -0.6f), new Vector3(0f, 3.0f, -0.6f), 0.7f, 0.7f, 12);
                    trim.Box(new Vector3(0f, 3.1f, -0.1f), new Vector3(0.12f, 0.12f, 1.1f), Quaternion.identity);
                    glass.Box(new Vector3(0f, 2.25f, 1.93f), new Vector3(width - 0.4f, 0.55f, 0.06f), Quaternion.identity);
                    foreach (float s in new[] { -1f, 1f })
                        glass.Box(new Vector3(s * (width * 0.5f + 0.01f), 2.25f, 0.9f), new Vector3(0.05f, 0.45f, 0.7f), Quaternion.identity);
                    foreach (float z in new[] { 1.9f, -1.9f })
                        foreach (float s in new[] { -1f, 1f })
                            Wheel(tyres, new Vector3(s * (width * 0.5f - 0.1f), 0.58f, z), Vector3.right, 0.58f, 0.42f);
                    break;
            }

            var go = MeshObject(parent, "Vehicle", ToMesh(body, DenseKey("vehicle")), paint, position, rot, Vector3.one, layer, "Metal");
            NoStanding(go);
            Mark(go, new Color(0.45f, 0.42f, 0.36f), 3);
            Hide(MeshObject(parent, "VehicleGlass", ToMesh(glass, DenseKey("vglass")), _glassDarkMat, position, rot, Vector3.one, layer, null, collider: false));
            Hide(MeshObject(parent, "VehicleTyres", ToMesh(tyres, DenseKey("vtyres")), _tyreMat, position, rot, Vector3.one, layer, "Metal"));
            if (trim.Triangles.Count > 0)
                Hide(MeshObject(parent, "VehicleTrim", ToMesh(trim, DenseKey("vtrim")), _steelMat, position, rot, Vector3.one, layer, "Metal", collider: false));
        }

        /// <summary>
        /// A half-cylinder shell along z, closed at both ends: a tent, a truck's tilt, a
        /// hangar. Built wound outward, so it is lit from outside and solid from outside.
        /// </summary>
        private static void ArchShell(MeshBuild build, Vector3 baseCentre, float radius, float height, float length,
                                      int segments, Quaternion rot)
        {
            Vector3 Ring(int i, float z)
            {
                float a = Mathf.PI * i / segments;
                return baseCentre + rot * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * height, z);
            }

            float h = length * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                // Outward: from the far end to the near end, round the arch.
                build.Quad(Ring(i, -h), Ring(i + 1, -h), Ring(i + 1, h), Ring(i, h));
            }

            // The ends, fanned from the middle of the base.
            var nearMid = baseCentre + rot * new Vector3(0f, 0f, h);
            var farMid = baseCentre + rot * new Vector3(0f, 0f, -h);
            for (int i = 0; i < segments; i++)
            {
                build.Tri(nearMid, Ring(i, h), Ring(i + 1, h));
                build.Tri(farMid, Ring(i + 1, -h), Ring(i, -h));
            }
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// The base, after the village and before the track -- the track runs to its gate.
        /// A hundred by seventy-six metres on a pad of its own, clear of the river, the
        /// village and the road from the village to the bridge, with its main gate turned to
        /// face the middle of the map.
        /// </summary>
        private static void PlanFob(System.Random rng, float half, float rim)
        {
            const float W = 100f, D = 76f;
            float circum = Mathf.Sqrt(W * W + D * D) * 0.5f;

            // The road from the village to the nearer bridge, which the base must not sit on.
            Vector2 bridge = _townCentre;
            float best = float.MaxValue;
            foreach (var c in _crossings)
            {
                float d = Mathf.Abs(c.z - _townCentre.y);
                if (d < best) { best = d; bridge = new Vector2(c.x, c.z); }
            }

            for (int i = 0; i < 400 && !_hasFob; i++)
            {
                var p = new Vector2(Rand(rng, -half + 60f, half - 60f), Rand(rng, -half + 60f, half - 60f));

                // Gate towards the middle of the map: local -z faces the origin.
                var toMiddle = -p;
                float yaw = Mathf.Round(Mathf.Atan2(-toMiddle.x, -toMiddle.y) * Mathf.Rad2Deg / 90f) * 90f;

                var plan = new SitePlan { Centre = p, Width = W, Depth = D, Yaw = yaw };
                var h = plan.HalfWorld;

                if (Mathf.Abs(p.x) + h.x > half - 26f || Mathf.Abs(p.y) + h.y > half - 26f) continue;
                if ((p - _townCentre).magnitude < _townRadius + circum + 12f) continue;
                if (p.magnitude < circum + 40f) continue;

                bool clear = true;
                for (int s = 0; s <= 8 && clear; s++)
                    for (int t = 0; t <= 8 && clear; t++)
                    {
                        var q = new Vector2(p.x - h.x + h.x * 2f * s / 8f, p.y - h.y + h.y * 2f * t / 8f);
                        if (Mathf.Abs(q.x - GorgeCentreAt(q.y)) < rim + 16f) clear = false;
                        if (DistanceToSegment(q, _townCentre, bridge) < 14f) clear = false;
                    }
                if (!clear || !Free(p, Mathf.Min(h.x, h.y))) continue;

                _fob = plan;
                _hasFob = true;
                Claim(p.x, p.y, circum + 4f);
                FlattenPad(p.x, p.y, circum + 2f, 30f);
                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }

            if (!_hasFob) Debug.LogWarning("[FPSKit] desert: no room for the base.");
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>The main gate of the base, and a point out in front of it where a road arrives.</summary>
        private static Vector2 FobGate(float outFront)
        {
            var f = new Frame(Vector3.zero, _fob.Yaw);
            var local = f.P(0f, 0f, -_fob.Depth * 0.5f - outFront);
            return _fob.Centre + new Vector2(local.x, local.z);
        }

        // ==================================================================
        // The base
        // ==================================================================
        /// <summary>
        /// A forward operating base: a HESCO perimeter with a guard tower in every corner, a
        /// main gate with a chicane and a barrier facing the middle of the map, a back gate
        /// on a side wall -- two gates on two sides, like every walled yard in the kit, so it
        /// is never a pen. Inside, a ring road round the wall and a main street through the
        /// middle, with four quarters off them: the tents, the motor pool, the headquarters
        /// and the landing zone.
        /// </summary>
        private static void BuildFob(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var fob = new GameObject("FOB").transform;
            fob.SetParent(parent, false);

            float W = _fob.Width, D = _fob.Depth, hw = W * 0.5f, hd = D * 0.5f;
            float floor = GroundHeightAt(_fob.Centre.x, _fob.Centre.y);
            var f = new Frame(new Vector3(_fob.Centre.x, floor, _fob.Centre.y), _fob.Yaw);

            const float unit = 1.1f, hescoH = 2.2f;
            const float mainGate = 9f, backGate = 7f, backGateV = 10f;

            // Gravel over the whole yard, so the inside reads as a base and not as more desert.
            var yard = new MeshBuild { UVScale = 0.2f };
            AddUp(yard, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(fob, backdrop, "FobYard", yard, DenseKey("fobyard"), _gravelMat);

            // ---- the perimeter ----
            var hesco = new MeshBuild { UVScale = 0.45f };
            var wire = new MeshBuild { UVScale = 0.5f };

            void HescoRun(Vector2 a, Vector2 b, float gapAt, float gapWidth)
            {
                var d = b - a;
                float len = d.magnitude;
                var dir = d / len;
                var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg, 0f);
                int count = Mathf.RoundToInt(len / unit);
                float step = len / count;

                for (int i = 0; i < count; i++)
                {
                    float s = (i + 0.5f) * step;
                    if (gapWidth > 0f && Mathf.Abs(s - gapAt) < gapWidth * 0.5f + step * 0.5f) continue;

                    var m = a + dir * s;
                    // A sack of sand in a wire cage: the fill slumps a little proud of the top.
                    hesco.Box(f.P(m.x, hescoH * 0.5f, m.y), new Vector3(step - 0.03f, hescoH, unit), rot);
                    hesco.Box(f.P(m.x, hescoH + 0.06f, m.y), new Vector3(step - 0.2f, 0.12f, unit - 0.2f), rot);

                    // The cage: corner posts and two bands.
                    var along = rot * Vector3.right * (step * 0.5f);
                    var across = rot * Vector3.forward * (unit * 0.5f + 0.015f);
                    var centre = f.P(m.x, 0f, m.y);
                    foreach (var c in new[] { along + across, along - across, -along + across, -along - across })
                        wire.Box(centre + c + Vector3.up * hescoH * 0.5f, new Vector3(0.035f, hescoH, 0.035f), rot);
                    foreach (float y in new[] { 0.73f, 1.47f, hescoH - 0.02f })
                    {
                        wire.Box(centre + across + Vector3.up * y, new Vector3(step, 0.03f, 0.03f), rot);
                        wire.Box(centre - across + Vector3.up * y, new Vector3(step, 0.03f, 0.03f), rot);
                    }
                }
            }

            float ih = hw - unit * 0.5f, id = hd - unit * 0.5f;   // the wall's centre line
            HescoRun(new Vector2(-ih, -id), new Vector2(ih, -id), ih, mainGate);            // front, main gate in the middle
            HescoRun(new Vector2(-ih, id), new Vector2(ih, id), -1f, 0f);                    // back
            HescoRun(new Vector2(-ih, -id), new Vector2(-ih, id), -1f, 0f);                  // left
            HescoRun(new Vector2(ih, -id), new Vector2(ih, id), id + backGateV, backGate);   // right, the back gate

            var wall = MeshObject(fob, "Hesco", ToMesh(hesco, DenseKey("hesco")), _hescoMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete");
            NoStanding(wall);
            Hide(MeshObject(fob, "HescoWire", ToMesh(wire, DenseKey("hescowire")), _wireMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));

            // Concertina along the outside of the wall, clear of both gates.
            var coil = new MeshBuild { UVScale = 0.5f };
            void Concertina(Vector2 a, Vector2 b, float gapAt, float gapWidth)
            {
                var d = b - a;
                float len = d.magnitude;
                var dir = d / len;
                for (float s = 0.4f; s < len; s += 0.55f)
                {
                    if (gapWidth > 0f && Mathf.Abs(s - gapAt) < gapWidth * 0.5f + 2.5f) continue;
                    var m = a + dir * s;
                    var c = f.P(m.x, 0.45f, m.y);
                    var ax = f.Axis(new Vector3(dir.x, 0f, dir.y));
                    coil.Tube(c - ax * 0.02f, c + ax * 0.02f, 0.45f, 0.45f, 9);
                }
            }
            float oh = hw + 1.4f, od = hd + 1.4f;
            Concertina(new Vector2(-oh, -od), new Vector2(oh, -od), oh, mainGate + 16f);
            Concertina(new Vector2(-oh, od), new Vector2(oh, od), -1f, 0f);
            Concertina(new Vector2(-oh, -od), new Vector2(-oh, od), -1f, 0f);
            Concertina(new Vector2(oh, -od), new Vector2(oh, od), od + backGateV, backGate + 4f);
            // Wire, not a wall: seen, walked round, and off the bake so it is never a barrier to it.
            Hide(MeshObject(fob, "Concertina", ToMesh(coil, DenseKey("concertina")), _wireMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, backdrop, null, collider: false));

            // ---- the towers, one in each corner ----
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    FobTower(fob, layer, f, su * (hw - unit - 1.75f), sv * (hd - unit - 1.75f), su, sv);

            // ---- the gates ----
            FobMainGate(fob, layer, f, -hd, mainGate);
            FobBarrier(fob, layer, f, new Vector2(hw - unit, backGateV), Vector3.forward, backGate);

            // ---- the quarters ----
            // Interior: a six-metre ring road inside the wall, the main street |u| < 5 from
            // the main gate to the back wall, and a cross street 6 < v < 14 out to the back gate.
            float innerU = hw - unit - 6.5f, innerV = hd - unit - 6.5f;

            FobTents(fob, layer, f, rng, -innerU, -5f, -innerV, 6f);
            FobMotorPool(fob, layer, f, rng, 5f, innerU, -innerV, 6f);
            FobHeadquarters(fob, layer, f, rng, -innerU, -5f, 14f, innerV);
            FobLandingZone(fob, layer, backdrop, f, rng, 5f, innerU, 14f, innerV);
            FobFill(fob, layer, f, rng, innerU, innerV);

            float lo = float.MaxValue, hi = float.MinValue;
            for (float u = -hw; u <= hw; u += 5f)
                for (float v = -hd; v <= hd; v += 5f)
                {
                    var q = f.P(u, 0f, v);
                    float g = GroundHeightAt(q.x, q.z);
                    lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                }
            Debug.Log($"[FPSKit] desert base at {_fob.Centre}, turned {_fob.Yaw}, floor {floor:0.00}, ground inside {lo:0.00}..{hi:0.00}.");
        }

        /// <summary>
        /// A guard tower: a block of HESCO two cages high, sandbags round its top, a plywood
        /// roof on posts, and a sandbag stair up its inner face. The top is ground -- that is
        /// what it is for -- and the landing overlaps it, so the bake joins the two.
        /// </summary>
        private static void FobTower(Transform parent, int layer, Frame f, float u, float v, float su, float sv)
        {
            const float size = 3.5f;
            float height = 20 * StairRise;   // 4.4 m, a whole number of risers

            var block = new MeshBuild { UVScale = 0.45f };
            block.Box(f.P(u, height * 0.5f, v), new Vector3(size, height, size), f.Rot);
            MeshObject(parent, "TowerBase", ToMesh(block, DenseKey("towerbase")), _hescoMat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Concrete");

            // Sandbags round the top, open on the inner face where the stair lands.
            var bags = new MeshBuild { UVScale = 0.5f };
            float e = size * 0.5f - 0.3f;
            void Bags(Vector2 a, Vector2 b, float gapAt, float gap)
            {
                var d = b - a;
                float len = d.magnitude;
                var dir = d / len;
                var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg, 0f);
                for (float s = 0.35f; s < len; s += 0.7f)
                {
                    if (gap > 0f && Mathf.Abs(s - gapAt) < gap * 0.5f) continue;
                    var m = a + dir * s;
                    for (int c = 0; c < 3; c++)
                        bags.Box(f.P(u + m.x, height + 0.17f + c * 0.32f, v + m.y), new Vector3(0.68f, 0.32f, 0.55f), rot);
                }
            }
            // The stair lands on the -sv face, at the -su half of it.
            Bags(new Vector2(-e, -e * sv), new Vector2(e, -e * sv), e - su * 0.9f, 2.2f);
            Bags(new Vector2(-e, e * sv), new Vector2(e, e * sv), -1f, 0f);
            Bags(new Vector2(-e * su, -e), new Vector2(-e * su, e), -1f, 0f);
            Bags(new Vector2(e * su, -e), new Vector2(e * su, e), -1f, 0f);
            NoStanding(MeshObject(parent, "TowerSandbags", ToMesh(bags, DenseKey("towerbags")), _sandbagMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete"));

            // Posts and a sloped plywood roof, high enough to stand under.
            var roof = new MeshBuild { UVScale = 0.5f };
            foreach (float a in new[] { -1f, 1f })
                foreach (float b in new[] { -1f, 1f })
                    roof.Box(f.P(u + a * (size * 0.5f - 0.15f), height + 1.25f, v + b * (size * 0.5f - 0.15f)), new Vector3(0.14f, 2.5f, 0.14f), f.Rot);
            roof.Box(f.P(u, height + 2.55f, v), new Vector3(size + 0.6f, 0.12f, size + 0.6f), f.Rot * Quaternion.Euler(4f * sv, 0f, 0f));
            var roofGo = MeshObject(parent, "TowerRoof", ToMesh(roof, DenseKey("towerroof")), _plywoodMat, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Wood");
            NoStanding(roofGo);
            Hide(roofGo);

            // The stair: along u towards the corner's own side, landing against the inner face.
            float landU = u - su * 0.9f;
            float landV = v - sv * (size * 0.5f + 0.9f - 0.08f);
            StairLocal(parent, layer, _sandbagMat, f, landU, landV, new Vector3(su, 0f, 0f), height);
        }

        /// <summary>
        /// The main gate: a guard post of sandbags inside it, a barrier arm across it, and
        /// outside a chicane of concrete barriers that slows anything driving in -- laid so
        /// that on foot there is always a lane through.
        /// </summary>
        private static void FobMainGate(Transform parent, int layer, Frame f, float wallV, float gate)
        {
            FobBarrier(parent, layer, f, new Vector2(0f, wallV + 0.55f), Vector3.right, gate);

            var jersey = new MeshBuild { UVScale = 0.4f };
            void Jersey(float u, float v, float yaw, float length)
            {
                var rot = f.Rot * Quaternion.Euler(0f, yaw, 0f);
                jersey.Box(f.P(u, 0.25f, v), new Vector3(length, 0.5f, 0.8f), rot);
                jersey.Box(f.P(u, 0.62f, v), new Vector3(length, 0.3f, 0.45f), rot);
                jersey.Box(f.P(u, 0.87f, v), new Vector3(length, 0.2f, 0.25f), rot);
            }

            // Staggered: left, right, left -- a lane six metres wide snakes between them.
            Jersey(-4.5f, wallV - 7f, 0f, 6f);
            Jersey(4.5f, wallV - 13f, 0f, 6f);
            Jersey(-4.5f, wallV - 19f, 0f, 6f);
            // And the gate's own shoulders.
            Jersey(-6.5f, wallV - 2.5f, 90f, 3f);
            Jersey(6.5f, wallV - 2.5f, 90f, 3f);
            NoStanding(MeshObject(parent, "JerseyBarriers", ToMesh(jersey, DenseKey("jersey")), _twallMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // A sandbagged guard post just inside, to one side of the gate.
            var post = new MeshBuild { UVScale = 0.5f };
            float pu = gate * 0.5f + 3.2f, pv = wallV + 4.5f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 40f) continue;   // open to the inside
                var rot = f.Rot * Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                for (int c = 0; c < 4; c++)
                    post.Box(f.P(pu + Mathf.Cos(a) * 2f, 0.17f + c * 0.32f, pv + Mathf.Sin(a) * 2f), new Vector3(0.6f, 0.32f, 1.15f), rot);
            }
            NoStanding(MeshObject(parent, "GuardPost", ToMesh(post, DenseKey("guardpost")), _sandbagMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete"));

            // Signs on the wall either side of the gate.
            var sign = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -1f, 1f })
                sign.Box(f.P(s * (gate * 0.5f + 2.2f), 1.5f, wallV - 0.62f), new Vector3(1.6f, 1.0f, 0.05f), f.Rot);
            Hide(MeshObject(parent, "GateSigns", ToMesh(sign, DenseKey("gatesigns")), _signMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));
        }

        /// <summary>A striped barrier arm on a post, raised part-way. No collider: it stops cars, not people.</summary>
        private static void FobBarrier(Transform parent, int layer, Frame f, Vector2 at, Vector3 acrossLocal, float span)
        {
            var post = new MeshBuild { UVScale = 0.5f };
            var red = new MeshBuild { UVScale = 0.5f };
            var white = new MeshBuild { UVScale = 0.5f };

            var across = f.Axis(acrossLocal);
            var basePoint = f.P(at.x, 0f, at.y) - across * (span * 0.5f - 0.2f);
            post.Box(basePoint + Vector3.up * 0.6f, new Vector3(0.35f, 1.2f, 0.35f), f.Rot);

            // Raised a few degrees, as a guard would leave it.
            var arm = Quaternion.AngleAxis(-12f, Vector3.Cross(across, Vector3.up)) * across;
            int stripes = 8;
            float len = span - 0.6f;
            for (int i = 0; i < stripes; i++)
            {
                var a = basePoint + Vector3.up * 1.1f + arm * (len * i / stripes);
                var b = basePoint + Vector3.up * 1.1f + arm * (len * (i + 1) / stripes);
                (i % 2 == 0 ? red : white).Tube(a, b, 0.06f, 0.06f, 6);
            }

            MeshObject(parent, "BarrierPost", ToMesh(post, DenseKey("barrierpost")), _steelMat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Metal");
            Hide(MeshObject(parent, "BarrierRed", ToMesh(red, DenseKey("barrierred")), _paintRedMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));
            Hide(MeshObject(parent, "BarrierWhite", ToMesh(white, DenseKey("barrierwhite")), _paintWhiteMat, Vector3.zero,
                            Quaternion.identity, Vector3.one, layer, null, collider: false));
        }

        /// <summary>Rows of arched canvas tents with alleys between, and blast walls of sandbags at the row ends.</summary>
        private static void FobTents(Transform parent, int layer, Frame f, System.Random rng, float u0, float u1, float v0, float v1)
        {
            const float tentW = 5.2f, tentL = 9f, alley = 2.6f;
            var tents = new MeshBuild { UVScale = 0.4f };
            var frames = new MeshBuild { UVScale = 0.5f };
            var clutter = new MeshBuild { UVScale = 0.5f };

            for (float u = u0 + tentW * 0.5f + 1f; u + tentW * 0.5f <= u1 - 1f; u += tentW + alley)
                for (float v = v0 + tentL * 0.5f + 1f; v + tentL * 0.5f <= v1 - 1f; v += tentL + 3.5f)
                {
                    ArchShell(tents, f.P(u, 0f, v), tentW * 0.5f, 2.9f, tentL, 10, f.Rot);
                    SealLocal(parent, f, u, 1.5f, v, new Vector3(tentW, 3f, tentL));

                    // A doorway flap and a vestibule frame at the end facing the ring road.
                    frames.Box(f.P(u, 1.05f, v - tentL * 0.5f - 0.02f), new Vector3(1.4f, 2.1f, 0.05f), f.Rot);
                    // Guy ropes' stakes, and water and kit piled at the door.
                    if (rng.NextDouble() < 0.6)
                        Crate(clutter, f.P(u + 1.6f, 0f, v - tentL * 0.5f - 0.6f), 0.55f, Rand(rng, 0f, 90f));
                    if (rng.NextDouble() < 0.4)
                        Drum(clutter, f.P(u - 1.8f, 0f, v - tentL * 0.5f - 0.7f));
                }

            var tentGo = MeshObject(parent, "Tents", ToMesh(tents, DenseKey("tents")), _tentCanvasMat, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Wood");
            NoStanding(tentGo);
            Hide(MeshObject(parent, "TentDoors", ToMesh(frames, DenseKey("tentdoors")), _glassDarkMat, Vector3.zero,
                            Quaternion.identity, Vector3.one, layer, null, collider: false));
            NoStanding(MeshObject(parent, "TentClutter", ToMesh(clutter, DenseKey("tentclutter")), _milGreenMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
        }

        /// <summary>
        /// The motor pool: vehicles parked in a row under camouflage nets, fuel bladders in a
        /// sandbag berm, a generator and a stack of drums.
        /// </summary>
        private static void FobMotorPool(Transform parent, int layer, Frame f, System.Random rng, float u0, float u1, float v0, float v1)
        {
            // Two rows of vehicles, noses to the main street.
            int n = 0;
            for (float u = u0 + 3.5f; u <= u1 - 3f && n < 8; u += 5.2f)
            {
                var kind = n % 3 == 0 ? VehicleKind.Truck : VehicleKind.Armoured;
                var at = f.P2(u, v0 + 6f);
                BuildVehicle(parent, layer, rng, at, _fob.Yaw + 180f + Rand(rng, -3f, 3f), kind,
                             rng.NextDouble() < 0.7 ? _milTanMat : _milGreenMat);
                n++;
            }

            // A second row further in: trucks, nose to tail.
            for (float u = u0 + 5f; u <= u1 - 5f; u += 9f)
                BuildVehicle(parent, layer, rng, f.P2(u, v0 + 17f), _fob.Yaw + 90f + Rand(rng, -3f, 3f), VehicleKind.Truck,
                             rng.NextDouble() < 0.6 ? _milTanMat : _milGreenMat);

            // Nets over the vehicle row.
            BuildNet(parent, layer, f, (u0 + u1) * 0.5f, v0 + 6f, (u1 - u0) - 2f, 10f, 3.6f);

            // The fuel point: two bladders in a berm, further in.
            float fv = v1 - 4f, fu = u0 + (u1 - u0) * 0.35f;
            var bladders = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -3.2f, 3.2f })
            {
                bladders.Box(f.P(fu + s, 0.45f, fv), new Vector3(5f, 0.9f, 3.2f), f.Rot);
                bladders.Box(f.P(fu + s, 0.95f, fv), new Vector3(4.2f, 0.25f, 2.4f), f.Rot);
            }
            NoStanding(MeshObject(parent, "FuelBladders", ToMesh(bladders, DenseKey("bladders")), _tankBlackMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));

            var berm = new MeshBuild { UVScale = 0.5f };
            float bw = 15f, bd = 6.5f;
            foreach (float side in new[] { -1f, 1f })
                for (float s = -bw * 0.5f; s < bw * 0.5f; s += 0.7f)
                    for (int c = 0; c < 2; c++)
                        berm.Box(f.P(fu + s + 0.35f, 0.17f + c * 0.32f, fv + side * bd * 0.5f), new Vector3(0.68f, 0.32f, 0.55f), f.Rot);
            NoStanding(MeshObject(parent, "FuelBerm", ToMesh(berm, DenseKey("fuelberm")), _sandbagMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // A generator on skids, and drums.
            var gen = new MeshBuild { UVScale = 0.5f };
            gen.Box(f.P(u1 - 5f, 0.1f, fv), new Vector3(2.6f, 0.2f, 1.4f), f.Rot);
            gen.Box(f.P(u1 - 5f, 0.95f, fv), new Vector3(2.4f, 1.5f, 1.2f), f.Rot);
            gen.Tube(f.P(u1 - 5.8f, 1.7f, fv), f.P(u1 - 5.8f, 2.4f, fv), 0.08f, 0.08f, 6);
            for (int i = 0; i < 7; i++)
                Drum(gen, f.P(u1 - 2f + (i % 3) * 0.66f, 0f, fv - 1f + (i / 3) * 0.66f));
            NoStanding(MeshObject(parent, "Generator", ToMesh(gen, DenseKey("generator")), _milGreenMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));
        }

        /// <summary>
        /// What a base accumulates in every gap: stacked shipping containers, pallets under
        /// tarps, a sandbag bunker with a roof, crates, drums, a generator, water. Placed
        /// wherever the physics scene says there is room, off the ring road and the main
        /// and cross streets, so it fills the base without closing any route through it.
        /// </summary>
        private static void FobFill(Transform parent, int layer, Frame f, System.Random rng, float innerU, float innerV)
        {
            Physics.SyncTransforms();
            var iso = new MeshBuild { UVScale = 0.4f };
            var stuff = new MeshBuild { UVScale = 0.5f };
            var bags = new MeshBuild { UVScale = 0.5f };
            var tarp = new MeshBuild { UVScale = 0.5f };
            int placed = 0;

            // The landing pad, as FobLandingZone lays it: painted, not solid, so ask here.
            float padU = (5f + innerU) * 0.5f + 3f, padV = (14f + innerV) * 0.5f + 1f;

            bool Open(float u, float v, float halfU, float halfV)
            {
                if (Mathf.Abs(u - padU) < 9.5f + halfU && Mathf.Abs(v - padV) < 9.5f + halfV) return false;
                if (Mathf.Abs(u) < 5f + halfU) return false;                  // the main street
                if (v + halfV > 6f && v - halfV < 14f) return false;          // the cross street
                if (Mathf.Abs(u) + halfU > innerU || Mathf.Abs(v) + halfV > innerV) return false;
                var c = f.P(u, 1.5f, v);
                var half = f.WorldSize(new Vector3(halfU + 1.2f, 1.4f, halfV + 1.2f));
                return !Physics.CheckBox(c, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            }

            for (int i = 0; i < 1500 && placed < 34; i++)
            {
                float u = Rand(rng, -innerU, innerU), v = Rand(rng, -innerV, innerV);
                int kind = rng.Next(6);

                switch (kind)
                {
                    case 0:
                    {
                        // Shipping containers, two high sometimes, long side along u.
                        if (!Open(u, v, 3.1f, 1.3f)) continue;
                        int high = rng.NextDouble() < 0.5 ? 2 : 1;
                        for (int h = 0; h < high; h++)
                        {
                            iso.Box(f.P(u + h * 0.15f, 1.3f + h * 2.6f, v), new Vector3(6.06f, 2.6f, 2.44f), f.Rot);
                            for (float r = -2.8f; r <= 2.8f; r += 0.4f)
                                foreach (float sd in new[] { -1f, 1f })
                                    iso.Box(f.P(u + h * 0.15f + r, 1.3f + h * 2.6f, v + sd * 1.23f), new Vector3(0.12f, 2.4f, 0.04f), f.Rot);
                        }
                        break;
                    }
                    case 1:
                    {
                        // A sandbag bunker: a U of bags with a tin roof on it.
                        if (!Open(u, v, 2.6f, 2.6f)) continue;
                        for (int k = 0; k < 16; k++)
                        {
                            float a = k * Mathf.PI * 2f / 16f;
                            if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 270f)) < 30f) continue;
                            var rot = f.Rot * Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                            for (int c = 0; c < 5; c++)
                                bags.Box(f.P(u + Mathf.Cos(a) * 2.2f, 0.17f + c * 0.32f, v + Mathf.Sin(a) * 2.2f), new Vector3(0.6f, 0.32f, 0.95f), rot);
                        }
                        tarp.Box(f.P(u, 1.75f, v), new Vector3(5.2f, 0.1f, 5.2f), f.Rot);
                        break;
                    }
                    case 2:
                    {
                        // Pallets of stores under a tarp.
                        if (!Open(u, v, 2.2f, 1.6f)) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            var at = f.P(u - 1.1f + (k % 2) * 2.2f, 0f, v - 0.7f + (k / 2) * 1.4f);
                            stuff.Box(at + Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1.0f), f.Rot);
                            stuff.Box(at + Vector3.up * 0.7f, new Vector3(1.1f, 1.1f, 0.95f), f.Rot);
                        }
                        tarp.Box(f.P(u, 1.3f, v), new Vector3(4.4f, 0.06f, 2.9f), f.Rot * Quaternion.Euler(0f, 0f, 4f));
                        break;
                    }
                    case 3:
                    {
                        // Drums, a rack of them.
                        if (!Open(u, v, 1.6f, 1.2f)) continue;
                        for (int k = 0; k < 8; k++)
                            Drum(stuff, f.P(u - 1.2f + (k % 4) * 0.68f, 0f, v - 0.4f + (k / 4) * 0.7f));
                        break;
                    }
                    case 4:
                    {
                        // Ammunition crates in a stack.
                        if (!Open(u, v, 1.4f, 1f)) continue;
                        for (int k = 0; k < 6; k++)
                            stuff.Box(f.P(u - 0.7f + (k % 2) * 1.4f, 0.3f + (k / 2) * 0.6f, v), new Vector3(1.3f, 0.58f, 0.7f), f.Rot);
                        break;
                    }
                    default:
                    {
                        // A generator on skids.
                        if (!Open(u, v, 1.4f, 0.8f)) continue;
                        stuff.Box(f.P(u, 0.1f, v), new Vector3(2.6f, 0.2f, 1.4f), f.Rot);
                        stuff.Box(f.P(u, 0.95f, v), new Vector3(2.4f, 1.5f, 1.2f), f.Rot);
                        break;
                    }
                }

                // Commit this one to the physics scene now, so the next one sees it.
                FlushFill(parent, layer, ref iso, ref stuff, ref bags, ref tarp);
                placed++;
            }

            Debug.Log($"[FPSKit] desert base fill: {placed} piece(s).");
        }

        private static void FlushFill(Transform parent, int layer, ref MeshBuild iso, ref MeshBuild stuff, ref MeshBuild bags, ref MeshBuild tarp)
        {
            if (iso.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "IsoContainers", ToMesh(iso, DenseKey("iso")), _containerPaints[iso.Triangles.Count % _containerPaints.Length],
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));
            if (stuff.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Stores", ToMesh(stuff, DenseKey("stores")), _milGreenMat, Vector3.zero, Quaternion.identity,
                                      Vector3.one, layer, "Wood"));
            if (bags.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Bunker", ToMesh(bags, DenseKey("bunker")), _sandbagMat, Vector3.zero, Quaternion.identity,
                                      Vector3.one, layer, "Concrete"));
            if (tarp.Triangles.Count > 0)
            {
                var go = MeshObject(parent, "Tarp", ToMesh(tarp, DenseKey("tarp")), _corrugatedMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Metal");
                NoStanding(go);
                Hide(go);
            }

            iso = new MeshBuild { UVScale = 0.4f };
            stuff = new MeshBuild { UVScale = 0.5f };
            bags = new MeshBuild { UVScale = 0.5f };
            tarp = new MeshBuild { UVScale = 0.5f };
        }

        /// <summary>A camouflage net on poles over (u, v), sagging between them. No collider.</summary>
        private static void BuildNet(Transform parent, int layer, Frame f, float u, float v, float width, float depth, float height)
        {
            _netMat ??= MakeMaterial("CamoNet", new Color(0.36f, 0.34f, 0.22f), 0.04f, 0f);

            var poles = new MeshBuild { UVScale = 0.5f };
            int nu = Mathf.Max(2, Mathf.RoundToInt(width / 8f) + 1);
            for (int i = 0; i < nu; i++)
                foreach (float s in new[] { -0.5f, 0.5f })
                {
                    float pu = u - width * 0.5f + width * i / (nu - 1);
                    poles.Box(f.P(pu, height * 0.5f, v + s * depth), new Vector3(0.12f, height, 0.12f), f.Rot);
                }
            MeshObject(parent, "NetPoles", ToMesh(poles, DenseKey("netpoles")), _timberMat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Wood");

            const int N = 12;
            var net = new MeshBuild { UVScale = 0.6f };
            Vector3 At(int i, int j)
            {
                float a = i / (float)(N - 1), b = j / (float)(N - 1);
                float sag = Mathf.Sin(b * Mathf.PI) * 0.5f + Mathf.Abs(Mathf.Sin(a * Mathf.PI * (nu - 1))) * 0.35f;
                float y = height - sag + Fbm2(i * 0.7f, j * 0.7f, 7611, 2) * 0.2f;
                return f.P(u - width * 0.5f + width * a, y, v - depth * 0.5f + depth * b);
            }
            for (int i = 0; i < N - 1; i++)
                for (int j = 0; j < N - 1; j++)
                {
                    AddUp(net, At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1));
                    AddDown(net, At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1));
                }
            var netGo = MeshObject(parent, "CamoNet", ToMesh(net, DenseKey("camonet")), _netMat, Vector3.zero, Quaternion.identity,
                                   Vector3.one, layer, null, collider: false);
            NoStanding(netGo);
            Hide(netGo);
        }

        /// <summary>
        /// Headquarters: a plywood B-hut with a pitched roof behind a line of concrete T-walls,
        /// a radio mast and a flag, and a row of accommodation containers.
        /// </summary>
        private static void FobHeadquarters(Transform parent, int layer, Frame f, System.Random rng, float u0, float u1, float v0, float v1)
        {
            // The hut, eight by sixteen, along u, towards the back wall.
            float hu = (u0 + u1) * 0.5f + 4f, hv = v1 - 5.5f;
            const float hutW = 16f, hutD = 8f, eave = 3f;
            var hut = new MeshBuild { UVScale = 0.4f };
            hut.Box(f.P(hu, 0.35f, hv), new Vector3(hutW + 0.4f, 0.7f, hutD + 0.4f), f.Rot);   // on a raised floor
            hut.Box(f.P(hu, 0.7f + eave * 0.5f, hv), new Vector3(hutW, eave, hutD), f.Rot);
            // Gable roof, two slopes.
            foreach (float s in new[] { -1f, 1f })
                hut.Box(f.P(hu, 0.7f + eave + 0.75f, hv + s * hutD * 0.25f), new Vector3(hutW + 0.6f, 0.12f, hutD * 0.56f),
                        f.Rot * Quaternion.Euler(-s * 20f, 0f, 0f));
            var hutGo = MeshObject(parent, "HQHut", ToMesh(hut, DenseKey("hqhut")), _plywoodMat, Vector3.zero, Quaternion.identity,
                                   Vector3.one, layer, "Wood");
            NoStanding(hutGo);
            Mark(hutGo, new Color(0.60f, 0.52f, 0.38f), 4);
            SealLocal(parent, f, hu, 2f, hv, new Vector3(hutW, 4f, hutD));

            var hutDark = new MeshBuild { UVScale = 0.5f };
            hutDark.Box(f.P(hu - 3f, 1.8f, hv - hutD * 0.5f - 0.03f), new Vector3(1.1f, 2.1f, 0.06f), f.Rot);
            for (float w = -6f; w <= 6f; w += 3f)
                if (Mathf.Abs(w + 3f) > 1f)
                    hutDark.Box(f.P(hu + w, 2.4f, hv - hutD * 0.5f - 0.03f), new Vector3(0.9f, 0.7f, 0.06f), f.Rot);
            Hide(MeshObject(parent, "HQOpenings", ToMesh(hutDark, DenseKey("hqopen")), _glassDarkMat, Vector3.zero,
                            Quaternion.identity, Vector3.one, layer, null, collider: false));

            // Steps up to the door.
            var steps = new MeshBuild { UVScale = 0.5f };
            steps.Box(f.P(hu - 3f, 0.18f, hv - hutD * 0.5f - 0.6f), new Vector3(1.6f, 0.36f, 0.8f), f.Rot);
            NoStanding(MeshObject(parent, "HQSteps", ToMesh(steps, DenseKey("hqsteps")), _plywoodMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));

            // T-walls in front of it, between it and the cross street, with gaps to walk through.
            var twalls = new MeshBuild { UVScale = 0.35f };
            float tv = hv - hutD * 0.5f - 3.2f;
            for (float u = hu - hutW * 0.5f - 1f; u <= u1 - 1f; u += 1.3f)
            {
                if (Mathf.Abs(u - (hu - 3f)) < 2.2f || Mathf.Abs(u - (hu + 5f)) < 1.6f) continue;
                twalls.Box(f.P(u, 1.8f, tv), new Vector3(1.2f, 3.6f, 0.3f), f.Rot);
                twalls.Box(f.P(u, 0.3f, tv), new Vector3(1.2f, 0.6f, 1.4f), f.Rot);
            }
            NoStanding(MeshObject(parent, "TWalls", ToMesh(twalls, DenseKey("twalls")), _twallMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // Containers: two columns, long side along v, on the far side of the hut.
            var chu = new MeshBuild[_containerPaints.Length];
            for (int i = 0; i < chu.Length; i++) chu[i] = new MeshBuild { UVScale = 0.4f };
            var chuDetail = new MeshBuild { UVScale = 0.5f };
            foreach (float cu in new[] { u0 + 1.6f, u0 + 5.4f })
                for (float cv = v0 + 4f; cv + 3f <= v1; cv += 7f)
                {
                    var build = chu[rng.Next(chu.Length)];
                    build.Box(f.P(cu, 1.3f, cv), new Vector3(2.44f, 2.6f, 6.06f), f.Rot);
                    // Corrugations: ribs down the long faces.
                    for (float r = -2.8f; r <= 2.8f; r += 0.35f)
                        foreach (float s in new[] { -1f, 1f })
                            build.Box(f.P(cu + s * 1.23f, 1.3f, cv + r), new Vector3(0.04f, 2.4f, 0.1f), f.Rot);
                    chuDetail.Box(f.P(cu + 1.25f, 1.05f, cv - 1.5f), new Vector3(0.05f, 2.0f, 0.9f), f.Rot);
                    chuDetail.Box(f.P(cu + 1.25f, 1.6f, cv + 1.4f), new Vector3(0.05f, 0.6f, 0.8f), f.Rot);
                }
            for (int i = 0; i < chu.Length; i++)
                if (chu[i].Triangles.Count > 0)
                    NoStanding(MeshObject(parent, "Container", ToMesh(chu[i], DenseKey("chu")), _containerPaints[i], Vector3.zero,
                                          Quaternion.identity, Vector3.one, layer, "Metal"));
            Hide(MeshObject(parent, "ContainerDoors", ToMesh(chuDetail, DenseKey("chudoors")), _glassDarkMat, Vector3.zero,
                            Quaternion.identity, Vector3.one, layer, null, collider: false));

            // A radio mast with guy wires, and a flag.
            var mast = new MeshBuild { UVScale = 0.5f };
            var mastAt = f.P(u0 + 9f, 0f, v1 - 2f);
            mast.Tube(mastAt, mastAt + Vector3.up * 18f, 0.18f, 0.08f, 6);
            for (float y = 3f; y < 18f; y += 3f)
                mast.Box(mastAt + Vector3.up * y, new Vector3(0.6f, 0.05f, 0.05f), f.Rot);
            mast.Box(mastAt + Vector3.up * 16.5f, new Vector3(0.05f, 0.05f, 1.4f), f.Rot);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.4f;
                var foot = mastAt + new Vector3(Mathf.Cos(a) * 5f, 0f, Mathf.Sin(a) * 5f);
                mast.Tube(foot, mastAt + Vector3.up * 12f, 0.015f, 0.015f, 4);
            }
            var flagAt = f.P(hu - 3f, 0f, tv - 3f);
            mast.Tube(flagAt, flagAt + Vector3.up * 8f, 0.06f, 0.04f, 6);
            Hide(MeshObject(parent, "Mast", ToMesh(mast, DenseKey("mast")), _steelMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, "Metal"));

            var flag = new MeshBuild { UVScale = 0.5f };
            var fx = f.Axis(Vector3.right);
            Vector3 F(float a, float b) => flagAt + Vector3.up * (7.9f - b) + fx * a + Vector3.forward * Mathf.Sin(a * 2.2f) * 0.12f;
            AddUp(flag, F(0f, 0f), F(1.8f, 0f), F(1.8f, 1.1f), F(0f, 1.1f));
            AddDown(flag, F(0f, 0f), F(1.8f, 0f), F(1.8f, 1.1f), F(0f, 1.1f));
            Hide(MeshObject(parent, "Flag", ToMesh(flag, DenseKey("flag")), _milGreenMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));
        }

        /// <summary>
        /// The landing zone: a painted pad with an H in a circle and a windsock, a water tower,
        /// a line of portable toilets and pallets of bottled water.
        /// </summary>
        private static void FobLandingZone(Transform parent, int layer, int backdrop, Frame f, System.Random rng,
                                           float u0, float u1, float v0, float v1)
        {
            float pu = (u0 + u1) * 0.5f + 3f, pv = (v0 + v1) * 0.5f + 1f;
            float size = Mathf.Min(u1 - u0 - 6f, v1 - v0 - 2f, 16f);

            var pad = new MeshBuild { UVScale = 0.25f };
            float hs = size * 0.5f;
            AddUp(pad, f.P(pu - hs, 0.06f, pv - hs), f.P(pu - hs, 0.06f, pv + hs), f.P(pu + hs, 0.06f, pv + hs), f.P(pu + hs, 0.06f, pv - hs));
            Flat(parent, backdrop, "Helipad", pad, DenseKey("helipad"), _helipadMat);

            // The H and the circle, painted.
            var paint = new MeshBuild { UVScale = 0.5f };
            void Strip(float cu, float cv, float w, float d)
                => AddUp(paint, f.P(cu - w * 0.5f, 0.08f, cv - d * 0.5f), f.P(cu - w * 0.5f, 0.08f, cv + d * 0.5f),
                         f.P(cu + w * 0.5f, 0.08f, cv + d * 0.5f), f.P(cu + w * 0.5f, 0.08f, cv - d * 0.5f));
            float h = size * 0.28f;
            Strip(pu - h * 0.5f, pv, 0.9f, h * 1.6f);
            Strip(pu + h * 0.5f, pv, 0.9f, h * 1.6f);
            Strip(pu, pv, h, 0.9f);
            const int seg = 40;
            float r0 = size * 0.4f, r1 = size * 0.4f + 0.6f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                AddUp(paint, f.P(pu + Mathf.Cos(a0) * r0, 0.08f, pv + Mathf.Sin(a0) * r0), f.P(pu + Mathf.Cos(a0) * r1, 0.08f, pv + Mathf.Sin(a0) * r1),
                      f.P(pu + Mathf.Cos(a1) * r1, 0.08f, pv + Mathf.Sin(a1) * r1), f.P(pu + Mathf.Cos(a1) * r0, 0.08f, pv + Mathf.Sin(a1) * r0));
            }
            Flat(parent, backdrop, "HelipadPaint", paint, DenseKey("helipaint"), _paintWhiteMat);

            // Windsock.
            var sock = new MeshBuild { UVScale = 0.5f };
            var sockAt = f.P(u1 - 1f, 0f, v1 - 1f);
            sock.Tube(sockAt, sockAt + Vector3.up * 6f, 0.05f, 0.04f, 6);
            Hide(MeshObject(parent, "WindsockPole", ToMesh(sock, DenseKey("sockpole")), _steelMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, "Metal"));
            var cone = new MeshBuild { UVScale = 0.5f };
            var wind = new Vector3(0.95f, -0.1f, 0.3f).normalized;
            cone.Tube(sockAt + Vector3.up * 5.9f, sockAt + Vector3.up * 5.9f + wind * 2.2f, 0.32f, 0.16f, 8);
            Hide(MeshObject(parent, "Windsock", ToMesh(cone, DenseKey("sock")), _signMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));

            // Water tower: a tank on four legs.
            var tower = new MeshBuild { UVScale = 0.5f };
            var wt = f.P(u0 + 3f, 0f, v1 - 3f);
            foreach (float a in new[] { -1f, 1f })
                foreach (float b in new[] { -1f, 1f })
                    tower.Tube(wt + new Vector3(a * 1.3f, 0f, b * 1.3f), wt + new Vector3(a * 1.1f, 5f, b * 1.1f), 0.1f, 0.1f, 6);
            tower.Tube(wt + Vector3.up * 5f, wt + Vector3.up * 7.6f, 1.7f, 1.7f, 12);
            NoStanding(MeshObject(parent, "WaterTower", ToMesh(tower, DenseKey("watertower")), _dishMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));

            // A row of portable toilets along the ring road side.
            var johns = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 5; i++)
            {
                var at = f.P(u1 - 1f - i * 1.6f, 0f, v0 + 1.2f);
                johns.Box(at + Vector3.up * 1.15f, new Vector3(1.2f, 2.3f, 1.2f), f.Rot);
                johns.Box(at + Vector3.up * 2.36f, new Vector3(1.1f, 0.12f, 1.1f), f.Rot);
            }
            NoStanding(MeshObject(parent, "PortableToilets", ToMesh(johns, DenseKey("johns")), _johnBlueMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));

            // Pallets of bottled water, shrink-wrapped.
            var pallets = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 6; i++)
            {
                var at = f.P(u0 + 2f + (i % 3) * 1.5f, 0f, v0 + 2f + (i / 3) * 1.5f);
                pallets.Box(at + Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1.0f), f.Rot);
                pallets.Box(at + Vector3.up * 0.64f, new Vector3(1.1f, 1.0f, 0.95f), f.Rot);
            }
            NoStanding(MeshObject(parent, "WaterPallets", ToMesh(pallets, DenseKey("pallets")), _clothCream, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
        }
    }
}
#endif
