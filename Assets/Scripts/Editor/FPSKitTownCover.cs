#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Cover outside the shops (2026-10-21): along every street face of shops, here and there against the wall, the
    /// things that stand outside shops here -- wooden crates stacked three high, a diesel generator on the crate it came
    /// on with a jerrycan by it, a street electrical cabinet, a commercial bin -- each a metre or so high: something a
    /// body can crouch behind in a fight on the pavement.
    ///
    /// Never across an open shop or its door (only in front of the piers, a face's ends and shut shutters), never at a
    /// building's door or an outside stair, never on anything already standing there, never leaving less than 1.6 m to
    /// the kerb, never within 6 m of another. Every group a metre high or more carries a <see cref="CoverSpot"/> with its
    /// measured size, so enemies know it for cover (EnemyBrain.FindCover). Placed before the navmesh bake, so the bake
    /// goes round them, and with a sequence of their own so nothing else in the town moves.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float CoverSpacing = 6f, CoverWalkway = 1.6f;

        private static void SpinShopCover(Transform root, System.Random rng)
        {
            var group = new GameObject("ShopCover").transform;
            group.SetParent(root, false);
            Physics.SyncTransforms();
            var placed = new List<Vector3>();
            int crates = 0, generators = 0, cabinets = 0, bins = 0, spots = 0;
            float lowest = float.MaxValue, highest = 0f;
            int mask = ~LayerMask.GetMask("Ignore Raycast");

            foreach (var face in _shopFaces)
            {
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
                        if (rng.NextDouble() > 0.42) { x += 2.5f; continue; }
                        // What it is, and how much wall it takes.
                        double what = rng.NextDouble();
                        int kind = what < 0.40 ? 0 : what < 0.62 ? 1 : what < 0.80 ? 2 : 3;   // crates, generator, cabinet, bin
                        float w = kind == 0 ? 1.25f : kind == 1 ? 1.35f : kind == 2 ? 0.95f : 1.9f;
                        float d = kind == 2 ? 0.45f : 0.58f;
                        if (run.y - x < w) { x += 1f; continue; }
                        float xc = x + w * 0.5f + (float)rng.NextDouble() * Mathf.Min(1.2f, run.y - x - w);
                        x = xc + w * 0.5f + 0.5f;

                        // On the pavement, its back 5 cm off the foundation course (the face's corners are at y 0: the floor).
                        Vector3 wall = face.a0 + face.u * xc;
                        Vector3 at = wall + face.nrm * (0.09f + 0.05f + d * 0.5f);
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
                        if (placed.Any(p => (p - at).sqrMagnitude < CoverSpacing * CoverSpacing)) continue;
                        // Standing on the pavement's surface (taken under its front edge).
                        if (!Physics.Raycast(front - face.nrm * 0.05f + Vector3.up * 1.2f, Vector3.down, out var ground, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                        at.y = ground.point.y;
                        // Nothing standing there already: benches, bins, lamps, trees, a stair, goods.
                        var rot = Quaternion.LookRotation(face.nrm);
                        // (Its back stays 3 cm clear of the foundation course's face, its foot 10 cm over the pavement.)
                        if (Physics.CheckBox(at + Vector3.up * 0.65f, new Vector3(w * 0.5f + 0.1f, 0.55f, d * 0.5f + 0.02f), rot, mask, QueryTriggerInteraction.Ignore)) continue;

                        var cover = new GameObject(kind == 0 ? "CoverCrates" : kind == 1 ? "CoverGenerator" : kind == 2 ? "CoverCabinet" : "CoverBin").transform;
                        cover.SetParent(group, false);
                        cover.SetPositionAndRotation(at, rot);
                        cover.gameObject.layer = LayerMask.NameToLayer("Environment");
                        float top = BuildCoverGroup(cover, kind, at, yaw, face.u, rng);
                        if (top <= at.y + 0.1f) { Object.DestroyImmediate(cover.gameObject); continue; }
                        Physics.SyncTransforms();
                        placed.Add(at);
                        float height = top - at.y;
                        lowest = Mathf.Min(lowest, height); highest = Mathf.Max(highest, height);
                        if (kind == 0) crates++; else if (kind == 1) generators++; else if (kind == 2) cabinets++; else bins++;
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
            Debug.Log($"[FPSKit] town cover: {placed.Count} groups outside the shops ({crates} crate stacks, {generators} generators, {cabinets} cabinets, " +
                      $"{bins} bins), {lowest:0.00}-{highest:0.00} m high, {spots} marked as cover for enemies, along {_shopFaces.Count} shop faces");
        }

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
                default:  // a commercial bin, now and then a bag beside it
                {
                    var b = PlaceTown(cover, Prop("metal_trash_can"), at, Jitter(2f));
                    if (rng.NextDouble() < 0.5) PlaceTown(cover, Prop("trashbag"), at + along * (rng.NextDouble() < 0.5 ? -1.15f : 1.15f), Rand(rng, 0f, 360f));
                    return Top(b);
                }
            }
        }
    }
}
#endif
