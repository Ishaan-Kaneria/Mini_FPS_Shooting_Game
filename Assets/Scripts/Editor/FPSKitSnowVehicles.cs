#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Snowbound's vehicles, built for looking at (2026-10-04: "houses, cars, realistic ... use more and
    /// more triangles"): a piste groomer with real tracks (every link a pad with cleats), road wheels,
    /// sprocket and idler, a glazed cab with a light bar and beacon, a hydraulic front blade and a rear
    /// tiller; a crew-cab 4x4 pickup with arches, glazing in frames, grille, bumpers, lamps, mirrors and
    /// door seams; a snowmobile; and a 6x6 utility truck with a tilt. None is driven. Each is built round
    /// its own origin (ground at y = 0, front at +z), solid, off the navigation bake, and sealed.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _tyreSnow, _chromeSnow, _headLampSnow, _tailLampSnow, _rubberSnow;
        private static Material[] _vehiclePaints;

        private static void EnsureVehicleMats()
        {
            if (_tyreSnow != null && _vehiclePaints != null && _vehiclePaints.Length > 0 && _vehiclePaints[0] != null) return;
            _tyreSnow = MakeDetailMaterial("VehTyre", new Color(0.07f, 0.07f, 0.075f), "Rock", 1.2f, 0.12f, 0f, 0.6f);
            _rubberSnow = MakeMaterial("VehRubber", new Color(0.05f, 0.05f, 0.055f), 0.15f, 0f);
            _chromeSnow = MakeMaterial("VehChrome", new Color(0.62f, 0.64f, 0.66f), 0.85f, 0.85f);
            _headLampSnow = MakeMaterial("VehHeadLamp", new Color(0.92f, 0.90f, 0.74f), 0.95f, 0f);
            _tailLampSnow = MakeMaterial("VehTailLamp", new Color(0.62f, 0.05f, 0.04f), 0.9f, 0f);
            _vehiclePaints = new[]
            {
                MakeDetailMaterial("VehWhite", new Color(0.84f, 0.85f, 0.84f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f),
                MakeDetailMaterial("VehRed", new Color(0.55f, 0.10f, 0.08f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f),
                MakeDetailMaterial("VehBlue", new Color(0.14f, 0.26f, 0.46f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f),
                MakeDetailMaterial("VehGreen", new Color(0.14f, 0.28f, 0.20f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f),
                MakeDetailMaterial("VehGrey", new Color(0.36f, 0.38f, 0.40f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f),
                MakeDetailMaterial("VehOrange", new Color(0.80f, 0.38f, 0.08f), "Metal", 0.7f, 0.62f, 0.3f, 0.3f)
            };
        }

        private static Material VehiclePaint(System.Random rng) { EnsureVehicleMats(); return _vehiclePaints[rng.Next(_vehiclePaints.Length)]; }

        /// <summary>Puts a vehicle's meshes in the scene at a point and heading; collision on the body only.</summary>
        private static void PlaceVehicle(Transform parent, int layer, string name, Vector3 at, float yaw, Material paint,
                                         MeshBuild body, MeshBuild glass, MeshBuild tyres, MeshBuild dark, MeshBuild chrome,
                                         MeshBuild lamps, MeshBuild tails, MeshBuild snowLoad, Vector3 sealSize)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);

            var go = MeshObject(root, "Body", ToMesh(body, DenseKey("vbody")), paint, at, rot, Vector3.one, layer, "Metal");
            NoStanding(go);
            Mark(go, new Color(0.45f, 0.42f, 0.36f), 3);
            void Vis(string n, MeshBuild b, Material m, bool collide = false)
            {
                if (b == null || b.Triangles.Count == 0) return;
                var o = MeshObject(root, n, ToMesh(b, DenseKey(n)), m, at, rot, Vector3.one, layer, collide ? "Metal" : null, collider: collide);
                NoStanding(o); Hide(o);
            }
            Vis("Glass", glass, _dullGlassMat);
            Vis("Tyres", tyres, _tyreSnow, collide: true);
            Vis("Dark", dark, _rubberSnow);
            Vis("Chrome", chrome, _chromeSnow);
            Vis("Lamps", lamps, _headLampSnow);
            Vis("TailLamps", tails, _tailLampSnow);
            Vis("SnowLoad", snowLoad, _pineSnowMat);
            SealBox(root, at + Vector3.up * sealSize.y * 0.5f, sealSize);
        }

        // ==================================================================
        // The groomer
        // ==================================================================
        private static void Snowcat(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            EnsureVehicleMats();
            var body = new MeshBuild { UVScale = 0.45f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };   // tracks
            var dark = new MeshBuild { UVScale = 0.5f };
            var chrome = new MeshBuild { UVScale = 0.5f };
            var lamps = new MeshBuild { UVScale = 0.5f };
            var snow = new MeshBuild { UVScale = 0.5f };

            // ---- the tracks: a stadium loop of pads round the wheels, each with a cleat ----
            const float trackX = 1.05f, trackW = 0.95f, wheelY = 0.62f, half = 1.75f, rr = 0.62f;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * trackX;
                float perimeter = 4f * half + 2f * Mathf.PI * rr;
                int pads = Mathf.RoundToInt(perimeter / 0.3f);
                for (int i = 0; i < pads; i++)
                {
                    float s = (i + 0.5f) / pads * perimeter;
                    Vector3 p, tangent;
                    float straight = 2f * half;
                    float arc = Mathf.PI * rr;
                    if (s < straight)                           // bottom run, front to back
                    { p = new Vector3(x, wheelY - rr, half - s); tangent = Vector3.back; }
                    else if (s < straight + arc)                // rear arc
                    { float a = (s - straight) / rr; p = new Vector3(x, wheelY - Mathf.Cos(a) * rr, -half - Mathf.Sin(a) * rr); tangent = new Vector3(0f, Mathf.Sin(a), -Mathf.Cos(a)); }
                    else if (s < 2f * straight + arc)           // top run, back to front
                    { p = new Vector3(x, wheelY + rr, -half + (s - straight - arc)); tangent = Vector3.forward; }
                    else                                        // front arc
                    { float a = (s - 2f * straight - arc) / rr; p = new Vector3(x, wheelY + Mathf.Cos(a) * rr, half + Mathf.Sin(a) * rr); tangent = new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a)); }
                    var pr = Quaternion.LookRotation(tangent, Vector3.up);
                    tyres.Box(p, new Vector3(trackW, 0.1f, 0.3f), pr);
                    tyres.Box(p + pr * new Vector3(0f, -0.07f, 0f), new Vector3(trackW * 0.96f, 0.05f, 0.07f), pr);   // the cleat
                }
                // Road wheels, sprocket, idler, and the guide-horn rim they ride on.
                for (int k = 0; k < 6; k++)
                    dark.Tube(new Vector3(x - 0.3f, wheelY - 0.38f, -half + 0.35f + k * 0.62f), new Vector3(x + 0.3f, wheelY - 0.38f, -half + 0.35f + k * 0.62f), 0.26f, 0.26f, 14);
                dark.Tube(new Vector3(x - 0.34f, wheelY, half), new Vector3(x + 0.34f, wheelY, half), 0.5f, 0.5f, 18);
                dark.Tube(new Vector3(x - 0.34f, wheelY, -half), new Vector3(x + 0.34f, wheelY, -half), 0.5f, 0.5f, 18);
                chrome.Tube(new Vector3(x - 0.36f, wheelY, half), new Vector3(x - 0.37f, wheelY, half), 0.2f, 0.2f, 10);
                // Track guard above each run.
                body.SoftBox(new Vector3(x, 1.38f, 0.1f), new Vector3(trackW + 0.15f, 0.1f, 3.6f), Quaternion.identity, rng.Next(99), 0.04f, 3, 0.004f);
            }

            // ---- chassis, engine deck, cab ----
            body.SoftBox(new Vector3(0f, 1.12f, -0.1f), new Vector3(1.55f, 0.5f, 3.9f), Quaternion.identity, 3, 0.12f, 5, 0.006f);
            body.SoftBox(new Vector3(0f, 1.7f, -1.45f), new Vector3(1.9f, 0.7f, 1.6f), Quaternion.identity, 4, 0.14f, 5, 0.006f);          // engine deck
            body.SoftBox(new Vector3(0f, 1.62f, 0.55f), new Vector3(2.0f, 0.5f, 1.2f), Quaternion.Euler(-4f, 0f, 0f), 5, 0.14f, 5, 0.006f); // bonnet
            body.SoftBox(new Vector3(0f, 2.45f, 0.7f), new Vector3(1.95f, 1.1f, 1.8f), Quaternion.identity, 6, 0.16f, 5, 0.005f);          // cab
            body.SoftBox(new Vector3(0f, 3.04f, 0.65f), new Vector3(1.85f, 0.1f, 1.7f), Quaternion.identity, 7, 0.06f, 3, 0.002f);          // roof
            // Cab glass: a raked windscreen, side glazing, rear glass.
            glass.Box(new Vector3(0f, 2.5f, 1.58f), new Vector3(1.7f, 0.85f, 0.05f), Quaternion.Euler(-16f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                glass.Box(new Vector3(s * 0.99f, 2.5f, 0.95f), new Vector3(0.05f, 0.78f, 1.0f), Quaternion.identity);
                glass.Box(new Vector3(s * 0.99f, 2.5f, 0.1f), new Vector3(0.05f, 0.78f, 0.45f), Quaternion.identity);
            }
            glass.Box(new Vector3(0f, 2.5f, -0.22f), new Vector3(1.6f, 0.75f, 0.05f), Quaternion.identity);
            // Pillars, wipers, mirrors, handrails, the step up.
            foreach (float s in new[] { -1f, 1f })
            {
                dark.Box(new Vector3(s * 0.96f, 2.45f, 1.5f), new Vector3(0.09f, 1.0f, 0.09f), Quaternion.Euler(-16f, 0f, 0f));
                dark.Box(new Vector3(s * 1.15f, 2.5f, 1.35f), new Vector3(0.28f, 0.34f, 0.05f), Quaternion.identity);       // mirror
                dark.Tube(new Vector3(s * 1.0f, 2.35f, 1.35f), new Vector3(s * 1.13f, 2.5f, 1.35f), 0.015f, 0.015f, 4);
                chrome.Tube(new Vector3(s * 1.04f, 1.6f, 0.2f), new Vector3(s * 1.04f, 2.3f, 0.2f), 0.025f, 0.025f, 5);     // grab rail
                dark.Box(new Vector3(s * 1.0f, 1.45f, 0.5f), new Vector3(0.5f, 0.05f, 0.4f), Quaternion.identity);          // step
            }
            for (float x = -0.5f; x <= 0.5f; x += 0.5f)
                dark.Tube(new Vector3(x, 2.15f, 1.57f), new Vector3(x + 0.35f, 2.55f, 1.55f), 0.012f, 0.012f, 4);          // wipers
            // Light bar on the roof, an amber beacon, work lamps.
            dark.Box(new Vector3(0f, 3.16f, 1.2f), new Vector3(1.6f, 0.1f, 0.14f), Quaternion.identity);
            for (int i = 0; i < 6; i++)
                lamps.Box(new Vector3(-0.65f + i * 0.26f, 3.16f, 1.28f), new Vector3(0.2f, 0.08f, 0.04f), Quaternion.identity);
            lamps.Tube(new Vector3(0.5f, 3.1f, -0.2f), new Vector3(0.5f, 3.4f, -0.2f), 0.1f, 0.08f, 8);
            // Engine deck: louvres, exhaust stack, rear lamps.
            for (int i = 0; i < 6; i++) dark.Box(new Vector3(0f, 2.02f, -2.05f + i * 0.2f), new Vector3(1.5f, 0.02f, 0.05f), Quaternion.identity);
            chrome.Tube(new Vector3(0.6f, 2.0f, -1.0f), new Vector3(0.6f, 2.9f, -1.0f), 0.08f, 0.07f, 8);
            lamps.Box(new Vector3(-0.7f, 2.3f, -2.27f), new Vector3(0.3f, 0.14f, 0.04f), Quaternion.identity);
            lamps.Box(new Vector3(0.7f, 2.3f, -2.27f), new Vector3(0.3f, 0.14f, 0.04f), Quaternion.identity);
            // Headlamps on the bonnet.
            foreach (float s in new[] { -0.8f, 0.8f }) lamps.Box(new Vector3(s, 1.78f, 1.17f), new Vector3(0.3f, 0.2f, 0.05f), Quaternion.identity);

            // ---- the blade: five angled slats on two hydraulic arms, an edge strip, a rubber skirt ----
            for (int i = 0; i < 5; i++)
                body.SoftBox(new Vector3(0f, 0.55f + i * 0.17f, 2.95f + i * 0.06f), new Vector3(3.3f, 0.19f, 0.12f), Quaternion.Euler(-14f, 0f, 0f), 20 + i, 0.03f, 3, 0.003f);
            foreach (float s in new[] { -1.65f, 1.65f }) body.Box(new Vector3(s, 0.85f, 2.95f), new Vector3(0.1f, 0.95f, 0.5f), Quaternion.identity);
            dark.Box(new Vector3(0f, 0.28f, 2.85f), new Vector3(3.3f, 0.1f, 0.06f), Quaternion.identity);
            foreach (float s in new[] { -0.7f, 0.7f })
            {
                chrome.Tube(new Vector3(s, 1.0f, 1.4f), new Vector3(s * 1.3f, 0.9f, 2.8f), 0.05f, 0.05f, 6);
                dark.Tube(new Vector3(s, 1.35f, 1.5f), new Vector3(s * 1.2f, 1.0f, 2.8f), 0.03f, 0.03f, 5);
            }

            // ---- the rear tiller: a hooded drum with a comb of teeth ----
            body.SoftBox(new Vector3(0f, 0.9f, -2.55f), new Vector3(2.8f, 0.5f, 0.7f), Quaternion.identity, 41, 0.12f, 4, 0.006f);
            for (float x = -1.3f; x <= 1.3f; x += 0.1f) dark.Box(new Vector3(x, 0.4f, -2.85f), new Vector3(0.04f, 0.45f, 0.04f), Quaternion.identity);
            dark.Tube(new Vector3(-1.35f, 0.7f, -2.6f), new Vector3(1.35f, 0.7f, -2.6f), 0.2f, 0.2f, 12);

            // ---- snow lying on the roof, the bonnet and the deck ----
            snow.SoftBox(new Vector3(0f, 3.11f, 0.6f), new Vector3(1.7f, 0.07f, 1.5f), Quaternion.identity, 61, 0.05f, 3, 0.02f);
            snow.SoftBox(new Vector3(0f, 2.08f, -1.45f), new Vector3(1.7f, 0.05f, 1.4f), Quaternion.identity, 62, 0.05f, 3, 0.02f);

            PlaceVehicle(parent, layer, "Snowcat", at, yaw, rng.Next(2) == 0 ? _moduleOrange : _moduleRed,
                         body, glass, tyres, dark, chrome, lamps, null, snow, new Vector3(5.6f, 3.4f, 6.2f));
        }

        // ==================================================================
        // The pickup
        // ==================================================================
        private static void BuildPickup(Transform parent, int layer, System.Random rng, Vector3 at, float yaw, Material paint = null)
        {
            EnsureVehicleMats();
            paint = paint != null ? paint : VehiclePaint(rng);
            var body = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var chrome = new MeshBuild { UVScale = 0.5f };
            var lamps = new MeshBuild { UVScale = 0.5f };
            var tails = new MeshBuild { UVScale = 0.5f };
            var snow = new MeshBuild { UVScale = 0.5f };
            const float W = 1.92f;

            // Lower body, wings, bonnet, cab, roof panel.
            body.SoftBox(new Vector3(0f, 0.72f, 0f), new Vector3(W, 0.5f, 5.1f), Quaternion.identity, 11, 0.1f, 5, 0.006f);
            body.SoftBox(new Vector3(0f, 1.12f, 1.9f), new Vector3(W - 0.06f, 0.36f, 1.5f), Quaternion.Euler(-3f, 0f, 0f), 12, 0.13f, 5, 0.006f);
            foreach (float s in new[] { -1f, 1f })
                body.SoftBox(new Vector3(s * (W * 0.5f - 0.06f), 1.0f, 1.8f), new Vector3(0.14f, 0.5f, 1.7f), Quaternion.identity, 13, 0.06f, 3, 0.004f);
            body.SoftBox(new Vector3(0f, 1.52f, 0.3f), new Vector3(W - 0.1f, 0.85f, 1.9f), Quaternion.identity, 14, 0.14f, 5, 0.005f);
            body.SoftBox(new Vector3(0f, 2.0f, 0.22f), new Vector3(W - 0.24f, 0.09f, 1.55f), Quaternion.identity, 15, 0.05f, 3, 0.003f);
            // Bed: floor, sides with a rolled top edge, bulkhead, tailgate.
            body.Box(new Vector3(0f, 1.0f, -1.85f), new Vector3(W - 0.2f, 0.07f, 1.8f), Quaternion.identity);
            foreach (float s in new[] { -1f, 1f })
            {
                body.SoftBox(new Vector3(s * (W * 0.5f - 0.07f), 1.32f, -1.85f), new Vector3(0.12f, 0.6f, 1.95f), Quaternion.identity, 16, 0.05f, 3, 0.004f);
                body.SoftBox(new Vector3(s * (W * 0.5f - 0.07f), 1.64f, -1.85f), new Vector3(0.18f, 0.08f, 1.98f), Quaternion.identity, 17, 0.04f, 3, 0.003f);
            }
            body.SoftBox(new Vector3(0f, 1.3f, -0.85f), new Vector3(W - 0.14f, 0.55f, 0.1f), Quaternion.identity, 18, 0.04f, 3, 0.003f);
            body.SoftBox(new Vector3(0f, 1.3f, -2.78f), new Vector3(W - 0.1f, 0.55f, 0.1f), Quaternion.identity, 19, 0.04f, 3, 0.003f);
            // Wheel arches (dark flares) and the tyres.
            foreach (float z in new[] { 1.65f, -1.6f })
                foreach (float s in new[] { -1f, 1f })
                {
                    dark.Tube(new Vector3(s * (W * 0.5f - 0.12f), 0.52f, z), new Vector3(s * (W * 0.5f + 0.06f), 0.52f, z), 0.54f, 0.54f, 18);
                    TyreWheel(tyres, new Vector3(s * (W * 0.5f - 0.08f), 0.42f, z), Vector3.right, 0.42f, 0.32f);
                }
            // Glass in frames: windscreen, rear window, four side lights.
            glass.Box(new Vector3(0f, 1.6f, 1.2f), new Vector3(W - 0.28f, 0.66f, 0.04f), Quaternion.Euler(-33f, 0f, 0f));
            glass.Box(new Vector3(0f, 1.6f, -0.72f), new Vector3(W - 0.4f, 0.55f, 0.04f), Quaternion.Euler(14f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (float z in new[] { 0.7f, -0.12f })
                {
                    glass.Box(new Vector3(s * (W * 0.5f - 0.03f), 1.65f, z), new Vector3(0.04f, 0.5f, 0.7f), Quaternion.identity);
                    dark.Box(new Vector3(s * (W * 0.5f - 0.02f), 1.65f, z), new Vector3(0.03f, 0.58f, 0.78f), Quaternion.identity);
                }
                dark.Box(new Vector3(s * (W * 0.5f - 0.005f), 1.35f, 0.28f), new Vector3(0.02f, 0.9f, 0.025f), Quaternion.identity);  // door seams
                dark.Box(new Vector3(s * (W * 0.5f - 0.005f), 1.35f, -0.42f), new Vector3(0.02f, 0.9f, 0.025f), Quaternion.identity);
                chrome.Box(new Vector3(s * (W * 0.5f + 0.01f), 1.45f, 0.5f), new Vector3(0.03f, 0.04f, 0.18f), Quaternion.identity); // handles
                chrome.Box(new Vector3(s * (W * 0.5f + 0.01f), 1.45f, -0.25f), new Vector3(0.03f, 0.04f, 0.18f), Quaternion.identity);
                dark.Box(new Vector3(s * (W * 0.5f + 0.17f), 1.55f, 1.1f), new Vector3(0.22f, 0.16f, 0.09f), Quaternion.identity);     // mirrors
                dark.Tube(new Vector3(s * (W * 0.5f - 0.02f), 1.5f, 1.1f), new Vector3(s * (W * 0.5f + 0.08f), 1.55f, 1.1f), 0.015f, 0.015f, 4);
                lamps.Box(new Vector3(s * 0.7f, 1.0f, 2.68f), new Vector3(0.38f, 0.2f, 0.05f), Quaternion.identity);
                tails.Box(new Vector3(s * 0.82f, 1.25f, -2.83f), new Vector3(0.18f, 0.28f, 0.04f), Quaternion.identity);
            }
            // Grille, bumpers, plate, tow hooks.
            dark.Box(new Vector3(0f, 0.97f, 2.68f), new Vector3(0.86f, 0.24f, 0.05f), Quaternion.identity);
            for (int i = 0; i < 5; i++) chrome.Box(new Vector3(0f, 0.88f + i * 0.04f, 2.71f), new Vector3(0.82f, 0.012f, 0.02f), Quaternion.identity);
            chrome.SoftBox(new Vector3(0f, 0.55f, 2.72f), new Vector3(W + 0.06f, 0.22f, 0.2f), Quaternion.identity, 21, 0.06f, 3, 0.003f);
            chrome.SoftBox(new Vector3(0f, 0.6f, -2.8f), new Vector3(W + 0.04f, 0.2f, 0.18f), Quaternion.identity, 22, 0.06f, 3, 0.003f);
            dark.Box(new Vector3(0f, 0.55f, 2.84f), new Vector3(0.5f, 0.12f, 0.02f), Quaternion.identity);
            // A light bar, a roof rack, a snow shovel standing in the bed.
            if (rng.NextDouble() < 0.5)
                for (float x = -0.7f; x <= 0.7f; x += 0.35f) dark.Tube(new Vector3(x, 2.07f, -0.5f), new Vector3(x, 2.07f, 0.9f), 0.02f, 0.02f, 5);
            if (rng.NextDouble() < 0.6)
            {
                dark.Tube(new Vector3(0.4f, 1.0f, -1.5f), new Vector3(0.6f, 2.0f, -1.9f), 0.02f, 0.02f, 4);
                dark.Box(new Vector3(0.38f, 1.12f, -1.46f), new Vector3(0.32f, 0.04f, 0.28f), Quaternion.identity);
            }
            // The snow on it: a skin on the roof, the bonnet and across the bed.
            snow.SoftBox(new Vector3(0f, 2.07f, 0.22f), new Vector3(W - 0.3f, 0.07f, 1.4f), Quaternion.identity, 31, 0.04f, 3, 0.02f);
            snow.SoftBox(new Vector3(0f, 1.34f, 1.9f), new Vector3(W - 0.3f, 0.05f, 1.2f), Quaternion.identity, 32, 0.04f, 3, 0.02f);
            if (rng.NextDouble() < 0.7) snow.SoftBox(new Vector3(0f, 1.08f, -1.85f), new Vector3(W - 0.3f, 0.12f, 1.6f), Quaternion.identity, 33, 0.05f, 3, 0.04f);

            PlaceVehicle(parent, layer, "Pickup", at, yaw, paint, body, glass, tyres, dark, chrome, lamps, tails, snow, new Vector3(5.6f, 2.2f, 5.6f));
        }

        // ==================================================================
        // The snowmobile
        // ==================================================================
        private static void BuildSnowmobile(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            EnsureVehicleMats();
            var paint = VehiclePaint(rng);
            var body = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var chrome = new MeshBuild { UVScale = 0.5f };
            var lamps = new MeshBuild { UVScale = 0.5f };

            body.SoftBox(new Vector3(0f, 0.7f, 0.5f), new Vector3(0.78f, 0.42f, 1.3f), Quaternion.Euler(-9f, 0f, 0f), 51, 0.14f, 5, 0.006f);   // hood
            body.SoftBox(new Vector3(0f, 0.92f, -0.1f), new Vector3(0.6f, 0.4f, 0.6f), Quaternion.identity, 52, 0.12f, 4, 0.005f);               // tank
            body.SoftBox(new Vector3(0f, 0.72f, -0.95f), new Vector3(0.64f, 0.5f, 1.6f), Quaternion.identity, 53, 0.1f, 4, 0.005f);               // tunnel
            dark.SoftBox(new Vector3(0f, 1.06f, -0.85f), new Vector3(0.46f, 0.14f, 1.2f), Quaternion.identity, 54, 0.06f, 3, 0.004f);            // seat
            glass.Box(new Vector3(0f, 1.2f, 0.6f), new Vector3(0.55f, 0.4f, 0.03f), Quaternion.Euler(-48f, 0f, 0f));                             // screen
            dark.Tube(new Vector3(-0.42f, 1.15f, 0.12f), new Vector3(0.42f, 1.15f, 0.12f), 0.018f, 0.018f, 6);                                    // bars
            foreach (float s in new[] { -1f, 1f })
            {
                dark.Tube(new Vector3(s * 0.4f, 1.15f, 0.12f), new Vector3(s * 0.4f, 1.08f, 0.0f), 0.03f, 0.03f, 6);
                lamps.Box(new Vector3(s * 0.2f, 0.78f, 1.17f), new Vector3(0.2f, 0.12f, 0.04f), Quaternion.identity);
                // Skis: a flat run, a turned-up tip.
                dark.Box(new Vector3(s * 0.52f, 0.18f, 0.2f), new Vector3(0.13f, 0.04f, 1.7f), Quaternion.identity);
                dark.Box(new Vector3(s * 0.52f, 0.32f, 1.17f), new Vector3(0.13f, 0.04f, 0.35f), Quaternion.Euler(-35f, 0f, 0f));
                chrome.Tube(new Vector3(s * 0.52f, 0.18f, 0.6f), new Vector3(s * 0.3f, 0.6f, 0.55f), 0.02f, 0.02f, 4);
            }
            // The track under the tunnel.
            for (int i = 0; i < 16; i++)
            {
                float z = -1.7f + i * 0.1f;
                tyres.Box(new Vector3(0f, 0.12f, z), new Vector3(0.5f, 0.03f, 0.08f), Quaternion.identity);
            }
            tyres.Box(new Vector3(0f, 0.38f, -1.0f), new Vector3(0.5f, 0.03f, 1.6f), Quaternion.identity);
            dark.Tube(new Vector3(-0.28f, 0.28f, -1.65f), new Vector3(0.28f, 0.28f, -1.65f), 0.14f, 0.14f, 12);
            lamps.Box(new Vector3(0f, 0.9f, -1.76f), new Vector3(0.3f, 0.08f, 0.03f), Quaternion.identity);
            PlaceVehicle(parent, layer, "Snowmobile", at, yaw, paint, body, glass, tyres, dark, chrome, lamps, null, null, new Vector3(2.6f, 1.4f, 3.2f));
        }

        // ==================================================================
        // The utility truck
        // ==================================================================
        private static void BuildUtilityTruck(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            EnsureVehicleMats();
            var paint = _vehiclePaints[rng.Next(2) == 0 ? 3 : 4];
            var body = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var chrome = new MeshBuild { UVScale = 0.5f };
            var lamps = new MeshBuild { UVScale = 0.5f };
            var tails = new MeshBuild { UVScale = 0.5f };
            var snow = new MeshBuild { UVScale = 0.5f };
            const float W = 2.45f;

            // Frame rails, cab, bonnet, wings, bumper.
            foreach (float s in new[] { -0.8f, 0.8f }) dark.Box(new Vector3(s, 0.95f, -0.3f), new Vector3(0.18f, 0.28f, 7.2f), Quaternion.identity);
            body.SoftBox(new Vector3(0f, 2.05f, 2.0f), new Vector3(W - 0.15f, 1.6f, 1.85f), Quaternion.identity, 71, 0.16f, 5, 0.006f);
            body.SoftBox(new Vector3(0f, 1.35f, 3.35f), new Vector3(W - 0.35f, 1.1f, 1.5f), Quaternion.identity, 72, 0.16f, 5, 0.006f);
            foreach (float s in new[] { -1f, 1f })
                body.SoftBox(new Vector3(s * (W * 0.5f - 0.05f), 1.05f, 3.3f), new Vector3(0.26f, 0.22f, 1.9f), Quaternion.identity, 73, 0.08f, 3, 0.004f);
            chrome.SoftBox(new Vector3(0f, 0.85f, 4.18f), new Vector3(W, 0.3f, 0.25f), Quaternion.identity, 74, 0.08f, 3, 0.004f);
            glass.Box(new Vector3(0f, 2.3f, 2.95f), new Vector3(W - 0.5f, 0.8f, 0.05f), Quaternion.Euler(-12f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                glass.Box(new Vector3(s * (W * 0.5f - 0.05f), 2.3f, 2.2f), new Vector3(0.05f, 0.72f, 1.0f), Quaternion.identity);
                lamps.Box(new Vector3(s * 0.8f, 1.35f, 4.12f), new Vector3(0.3f, 0.2f, 0.05f), Quaternion.identity);
                dark.Box(new Vector3(s * (W * 0.5f + 0.2f), 2.3f, 3.0f), new Vector3(0.14f, 0.5f, 0.1f), Quaternion.identity);
                tails.Box(new Vector3(s * 0.9f, 1.15f, -4.1f), new Vector3(0.2f, 0.26f, 0.04f), Quaternion.identity);
            }
            dark.Box(new Vector3(0f, 1.3f, 4.12f), new Vector3(0.9f, 0.5f, 0.06f), Quaternion.identity);
            chrome.Tube(new Vector3(0.95f, 1.6f, 1.2f), new Vector3(0.95f, 3.2f, 1.2f), 0.07f, 0.06f, 8);   // snorkel
            // The bed: boards on a deck, drop sides, hoops and a canvas tilt.
            body.Box(new Vector3(0f, 1.2f, -1.7f), new Vector3(W, 0.16f, 4.4f), Quaternion.identity);
            foreach (float s in new[] { -1f, 1f })
            {
                for (int i = 0; i < 3; i++)
                    body.Box(new Vector3(s * (W * 0.5f - 0.03f), 1.38f + i * 0.2f, -1.7f), new Vector3(0.06f, 0.18f, 4.4f), Quaternion.identity);
            }
            body.Box(new Vector3(0f, 1.6f, -3.88f), new Vector3(W, 0.7f, 0.08f), Quaternion.identity);
            var tilt = new MeshBuild { UVScale = 0.5f };
            ArchShell(tilt, new Vector3(0f, 1.78f, -1.7f), W * 0.5f - 0.05f, 1.15f, 4.2f, 10, Quaternion.identity);
            for (float z = -3.6f; z <= 0.2f; z += 0.95f)
                for (int k = 0; k < 9; k++)
                {
                    float a0 = Mathf.PI * k / 9f, a1 = Mathf.PI * (k + 1) / 9f;
                    float r = W * 0.5f - 0.02f;
                    dark.Tube(new Vector3(Mathf.Cos(a0) * r, 1.78f + Mathf.Sin(a0) * 1.17f, z), new Vector3(Mathf.Cos(a1) * r, 1.78f + Mathf.Sin(a1) * 1.17f, z), 0.025f, 0.025f, 4);
                }
            // Three axles, six big tyres, mudguards, a spare on a bracket.
            foreach (float z in new[] { 3.0f, -1.2f, -2.6f })
                foreach (float s in new[] { -1f, 1f })
                {
                    TyreWheel(tyres, new Vector3(s * (W * 0.5f - 0.18f), 0.62f, z), Vector3.right, 0.62f, 0.44f);
                    dark.Tube(new Vector3(s * (W * 0.5f - 0.3f), 0.62f, z), new Vector3(s * (W * 0.5f + 0.02f), 0.62f, z), 0.68f, 0.68f, 18);
                }
            TyreWheel(tyres, new Vector3(0f, 1.9f, -4.0f), Vector3.forward, 0.55f, 0.38f);
            // A step, a toolbox, a fuel can.
            dark.Box(new Vector3(-W * 0.5f + 0.1f, 0.7f, 2.0f), new Vector3(0.3f, 0.06f, 0.8f), Quaternion.identity);
            body.SoftBox(new Vector3(-W * 0.5f + 0.18f, 0.75f, -0.1f), new Vector3(0.36f, 0.4f, 1.1f), Quaternion.identity, 75, 0.04f, 3, 0.003f);
            snow.SoftBox(new Vector3(0f, 2.9f, 2.0f), new Vector3(W - 0.4f, 0.07f, 1.6f), Quaternion.identity, 76, 0.04f, 3, 0.02f);
            snow.SoftBox(new Vector3(0f, 1.65f, 3.35f), new Vector3(W - 0.5f, 0.05f, 1.2f), Quaternion.identity, 77, 0.04f, 3, 0.02f);

            var rot = Quaternion.Euler(0f, yaw, 0f);
            var tgo = MeshObject(parent, "TruckTilt", ToMesh(tilt, DenseKey("tilt")), _alpCanvas, at, rot, Vector3.one, layer, "Wood");
            NoStanding(tgo);
            PlaceVehicle(parent, layer, "UtilityTruck", at, yaw, paint, body, glass, tyres, dark, chrome, lamps, tails, snow, new Vector3(8.6f, 3.6f, 8.6f));
        }
    }
}
#endif
