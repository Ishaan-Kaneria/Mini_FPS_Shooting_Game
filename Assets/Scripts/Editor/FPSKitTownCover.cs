#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Cover outside the shops (2026-10-21): along every street face of shops, here and there against the wall, the
    /// things that stand outside shops here -- wooden crates stacked three high, a stack of pallets, a diesel generator on
    /// the crate it came on with a jerrycan by it, a street electrical cabinet, a commercial bin, a pair of oil drums --
    /// each a metre or so high: something a body can crouch behind in a fight on the pavement.
    ///
    /// Each face is its own (a sequence from the layout's seed and where the face is): how much stands along it, its mix
    /// (a market row runs to crates and pallets, a quiet one to bins and cabinets), how far apart (4.5-7.5 m), how far off
    /// the wall (3-20 cm) and how squarely each group stands to it.
    ///
    /// Never across an open shop or its door (only in front of the piers, a face's ends and shut shutters), never at a
    /// building's door or an outside stair, never on anything already standing there, never leaving less than 1.6 m to
    /// the kerb, never within 6 m of another. Every group a metre high or more carries a <see cref="CoverSpot"/> with its
    /// measured size, so enemies know it for cover (EnemyBrain.FindCover). Placed before the navmesh bake, so the bake
    /// goes round them, and with a sequence of their own so nothing else in the town moves.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float CoverWalkway = 1.6f;
        private static readonly string[] CoverKinds = { "crates", "generator", "cabinet", "bin", "pallets", "drums" };

        private static void SpinShopCover(Transform root, System.Random seed)
        {
            var group = new GameObject("ShopCover").transform;
            group.SetParent(root, false);
            Physics.SyncTransforms();
            var placed = new List<(Vector3 at, float apart)>();
            var counts = new int[CoverKinds.Length];
            int spots = 0;
            float lowest = float.MaxValue, highest = 0f;
            int mask = ~LayerMask.GetMask("Ignore Raycast");
            int salt = seed.Next();

            foreach (var face in _shopFaces)
            {
                var rng = new System.Random(LayoutSeed(face.a0 + face.u * (face.len * 0.5f)) ^ salt);
                // This face's character: how much stands along it, and what (weights for the six kinds).
                float density = Mathf.Lerp(0.35f, 0.75f, (float)rng.NextDouble());
                bool market = rng.NextDouble() < 0.5;
                float[] weights = market ? new[] { 0.32f, 0.12f, 0.08f, 0.12f, 0.26f, 0.10f } : new[] { 0.16f, 0.16f, 0.22f, 0.24f, 0.10f, 0.12f };
                // Where something may stand along the face: the piers and its ends, and in front of a shut shutter --
                // merged into free runs. A group may lap 15 cm over an open shop's edge (its jamb), no more.
                var free = new List<Vector2>();
                float prev = 0f;
                foreach (var bay in face.bays.OrderBy(q => q.x0))
                {
                    if (bay.state == ShopState.Shut) continue;
                    if (bay.x0 - prev > 0.05f) free.Add(new Vector2(prev, bay.x0 + 0.15f));
                    prev = bay.x1 - 0.15f;
                }
                if (face.len - prev > 0.05f) free.Add(new Vector2(prev, face.len));
                float yaw = Mathf.Atan2(face.nrm.x, face.nrm.z) * Mathf.Rad2Deg;

                foreach (var run in free)
                {
                    for (float x = run.x; x < run.y - 0.6f;)
                    {
                        if (rng.NextDouble() > density) { x += 2.5f; continue; }
                        // What it is, and how much wall it takes.
                        int kind = Pick(rng, weights);
                        float w = kind == 0 ? 1.25f : kind == 1 ? 1.35f : kind == 2 ? 0.95f : kind == 3 ? 1.9f : kind == 4 ? 1.25f : 1.3f;
                        float d = kind == 2 ? 0.45f : kind == 4 ? 0.85f : 0.62f;
                        if (run.y - x < w) { x += 1f; continue; }
                        float xc = x + w * 0.5f + (float)rng.NextDouble() * Mathf.Min(1.2f, run.y - x - w);
                        x = xc + w * 0.5f + 0.5f;
                        float apart = Mathf.Lerp(4.5f, 7.5f, (float)rng.NextDouble());
                        float gap = Mathf.Lerp(0.03f, 0.2f, (float)rng.NextDouble());   // off the foundation course
                        float skew = Rand(rng, -7f, 7f);                                   // how squarely it stands

                        // On the pavement, its back off the foundation course (the face's corners are at y 0: the floor).
                        Vector3 wall = face.a0 + face.u * xc;
                        Vector3 at = wall + face.nrm * (0.09f + 0.02f + gap + d * 0.5f);
                        at.y = face.floorY;
                        var xz = new Vector2(at.x, at.z);
                        // The pavement is judged at its front edge: lots begin 0.4 m beyond the road's corridor (Edge),
                        // so a shallow group's middle is still over that strip.
                        var front = at + face.nrm * (d * 0.5f);
                        var fxz = new Vector2(front.x, front.z);
                        var road = RoadAt(fxz);
                        if (road == null || road.sidewalk <= 0f || OnCarriage(fxz, 0.3f) || InJunction(fxz)) continue;
                        var ahead = front + face.nrm * CoverWalkway;
                        if (OnCarriage(new Vector2(ahead.x, ahead.z), 0f)) continue;                 // leave a way along the pavement
                        if (_townDoors.Any(dr => (new Vector2(dr.x, dr.z) - xz).sqrMagnitude < 1.8f * 1.8f)) continue;
                        if (placed.Any(p => (p.at - at).sqrMagnitude < Mathf.Max(p.apart, apart) * Mathf.Max(p.apart, apart))) continue;
                        // Standing on the pavement's surface (taken under its front edge).
                        if (!Physics.Raycast(front - face.nrm * 0.05f + Vector3.up * 1.2f, Vector3.down, out var ground, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                        at.y = ground.point.y;
                        // Nothing standing there already: benches, bins, lamps, trees, a stair, goods.
                        var rot = Quaternion.LookRotation(face.nrm) * Quaternion.Euler(0f, skew, 0f);
                        // (Its back stays 3 cm clear of the foundation course's face, its foot 10 cm over the pavement.)
                        if (Physics.CheckBox(at + Vector3.up * 0.65f, new Vector3(w * 0.5f + 0.1f, 0.55f, d * 0.5f + 0.02f), rot, mask, QueryTriggerInteraction.Ignore)) continue;

                        var cover = new GameObject("Cover_" + CoverKinds[kind]).transform;
                        cover.SetParent(group, false);
                        cover.SetPositionAndRotation(at, rot);
                        cover.gameObject.layer = LayerMask.NameToLayer("Environment");
                        float top = BuildCoverGroup(cover, kind, at, yaw + skew, rot * Vector3.right, rng);
                        if (top <= at.y + 0.1f) { Object.DestroyImmediate(cover.gameObject); continue; }
                        Physics.SyncTransforms();
                        placed.Add((at, apart));
                        float height = top - at.y;
                        lowest = Mathf.Min(lowest, height); highest = Mathf.Max(highest, height);
                        counts[kind]++;
                        if (height >= 1f)
                        {
                            var spot = cover.gameObject.AddComponent<CoverSpot>();
                            spot.height = height;
                            spot.halfLength = w * 0.5f;
                            spot.halfDepth = d * 0.5f;
                            spots++;
                        }
                    }
                }
            }
            Debug.Log($"[FPSKit] town cover: {placed.Count} groups outside the shops ({string.Join(", ", CoverKinds.Select((k, i) => $"{counts[i]} {k}"))}), " +
                      $"{lowest:0.00}-{highest:0.00} m high, {spots} marked as cover for enemies, along {_shopFaces.Count} shop faces");
        }

        private static int Pick(System.Random rng, float[] weights)
        {
            float total = 0f; foreach (var w in weights) total += w;
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r <= 0f) return i; }
            return weights.Length - 1;
        }

        /// <summary>
        /// A euro pallet (1.2 x 0.8 m, 14.4 cm): three bottom boards, nine blocks, three stringer boards, five deck boards,
        /// in weathered planks; one box collider over it, tagged and surfaced as wood. Made once into FPSKit_Generated/Town.
        /// </summary>
        private static GameObject PalletPrefab()
        {
            string path = $"{FPSKitTownAssets.Out}/Pallet.prefab";
            if (_palletPrefab != null) return _palletPrefab;
            var wood = TownMat("PalletWood", "weathered_planks", new Color(0.92f, 0.82f, 0.66f), 1.2f, 0.2f);
            var m = new TownMesh();
            foreach (float x in new[] { -0.525f, 0f, 0.525f }) m.Box(wood, new Vector3(x, 0.011f, 0f), new Vector3(0.145f, 0.022f, 0.8f), Quaternion.identity);
            foreach (float x in new[] { -0.525f, 0f, 0.525f })
                foreach (float z in new[] { -0.33f, 0f, 0.33f }) m.Box(wood, new Vector3(x, 0.061f, z), new Vector3(0.145f, 0.078f, 0.1f), Quaternion.identity);
            foreach (float z in new[] { -0.35f, 0f, 0.35f }) m.Box(wood, new Vector3(0f, 0.111f, z), new Vector3(1.2f, 0.022f, 0.1f), Quaternion.identity);
            foreach (float x in new[] { -0.5275f, -0.265f, 0f, 0.265f, 0.5275f })
                m.Box(wood, new Vector3(x, 0.133f, 0f), new Vector3(x == 0f || Mathf.Abs(x) > 0.5f ? 0.145f : 0.1f, 0.022f, 0.8f), Quaternion.identity);
            var holder = new GameObject("Pallet").transform;
            var go = m.Build(holder, "Pallet", LayerMask.NameToLayer("Environment"), "Wood", collider: false);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            string meshPath = $"{FPSKitTownAssets.Out}/Pallet_mesh.asset";
            var old = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old != null) { old.Clear(); UnityEditor.EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else UnityEditor.AssetDatabase.CreateAsset(mesh, meshPath);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, 0);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.072f, 0f);
            box.size = new Vector3(1.2f, 0.144f, 0.8f);
            box.sharedMaterial = FPSKitImpactAssets.Surface("Wood");
            go.transform.SetParent(null, false);
            Object.DestroyImmediate(holder.gameObject);
            _palletPrefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return _palletPrefab;
        }
        private static GameObject _palletPrefab;

        /// <summary>Sets one cover group down under <paramref name="cover"/> at <paramref name="at"/>, its long side along the
        /// wall; returns the height of its top.</summary>
        private static float BuildCoverGroup(Transform cover, int kind, Vector3 at, float yaw, Vector3 along, System.Random rng)
        {
            float Jitter(float deg) => yaw + Rand(rng, -deg, deg);
            float Top(GameObject go) => go != null ? WorldBounds(go).max.y : at.y;
            switch (kind)
            {
                case 0:   // crates, three high, each a little askew
                {
                    var a = PlaceTown(cover, Prop("wooden_military_crate") ?? Prop("wooden_crate_02"), at, Jitter(3f));
                    var b = PlaceTown(cover, Prop("wooden_crate_02"), new Vector3(at.x, Top(a), at.z) + along * Rand(rng, -0.08f, 0.08f), Jitter(6f));
                    string[] tops = { "wooden_crate_01", "plastic_crate_02", "cardboard_box_01" };
                    var c = PlaceTown(cover, Prop(tops[rng.Next(tops.Length)]), new Vector3(at.x, Top(b), at.z) + along * Rand(rng, -0.25f, 0.25f), Jitter(20f));
                    return Mathf.Max(Top(a), Top(b), Top(c));
                }
                case 1:   // a generator on the crate it came on, a jerrycan beside it
                {
                    var a = PlaceTown(cover, Prop("wooden_military_crate") ?? Prop("wooden_crate_02"), at, Jitter(2f));
                    var g = PlaceTown(cover, Prop("portable_generator"), new Vector3(at.x, Top(a), at.z), Jitter(4f));
                    PlaceTown(cover, Prop("plastic_jerrycan"), at + along * (rng.NextDouble() < 0.5 ? -0.85f : 0.85f), Jitter(25f));
                    return Mathf.Max(Top(a), Top(g));
                }
                case 2:   // a street electrical cabinet
                {
                    var c = PlaceTown(cover, Prop("utility_box_02") ?? Prop("utility_box_01"), at, yaw);
                    return Top(c);
                }
                case 3:   // a commercial bin, now and then a bag beside it
                {
                    var b = PlaceTown(cover, Prop("metal_trash_can"), at, Jitter(2f));
                    if (rng.NextDouble() < 0.5) PlaceTown(cover, Prop("trashbag"), at + along * (rng.NextDouble() < 0.5 ? -1.15f : 1.15f), Rand(rng, 0f, 360f));
                    return Top(b);
                }
                case 4:   // pallets, six or seven high and not quite square, now and then a crate left on top
                {
                    var pallet = PalletPrefab();
                    float y = at.y;
                    int n = rng.Next(6, 8);
                    GameObject last = null;
                    for (int i = 0; i < n && pallet != null; i++)
                    {
                        last = PlaceTown(cover, pallet, new Vector3(at.x, y, at.z) + along * Rand(rng, -0.04f, 0.04f), Jitter(4f));
                        y = Top(last);
                    }
                    if (rng.NextDouble() < 0.35) y = Top(PlaceTown(cover, Prop("wooden_crate_01"), new Vector3(at.x, y, at.z), Jitter(15f)));
                    return y;
                }
                default:  // two oil drums used as bins, the odd one rusted
                {
                    var a = PlaceTown(cover, Prop(rng.NextDouble() < 0.5 ? "Barrel_01" : "barrel_03"), at - along * 0.33f, Rand(rng, 0f, 360f));
                    var b = PlaceTown(cover, Prop(rng.NextDouble() < 0.5 ? "Barrel_01" : "barrel_03"), at + along * 0.33f, Rand(rng, 0f, 360f));
                    return Mathf.Max(Top(a), Top(b));
                }
            }
        }
    }
}
#endif
