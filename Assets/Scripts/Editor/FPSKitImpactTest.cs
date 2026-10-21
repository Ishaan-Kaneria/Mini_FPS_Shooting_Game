#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// VerifyImpacts: a round's strike in the desert town is drawn for what it struck, and firing for ever costs nothing.
    ///
    ///   * Surfaces, by real raycasts into the built town: a plaster or brick wall is concrete, a shop's shutter in that
    ///     same wall mesh is metal (the struck face's material, not the chunk's physics material), shop glass is glass, a
    ///     parked car metal, a crate stack wood, the open desert sand.
    ///   * Pools: thousands of strikes at ten rounds a second leave the marks, flashes and particles within their pools,
    ///     make no objects and allocate nothing (counted on the strikes' own thread: GC.GetTotalMemory counts the whole
    ///     editor's threads and once reported 1.5 MB that no strike had made); thirty seconds on, every mark is gone
    ///     and every flash out.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyImpacts      (after building Desert Outpost)
    /// </summary>
    public static class FPSKitImpactTest
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        public static void VerifyImpacts()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
                Physics.SyncTransforms();
                CheckSurfaces(problems, notes);
                CheckPools(problems, notes);
            }
            catch (Exception e) { problems.Add(e.ToString()); }

            if (problems.Count > 0)
            {
                Debug.LogError("[FPSKitBatch] FAILED: impacts:\n  - " + string.Join("\n  - ", problems) + "\n" + notes);
                FPSKitBatch.Exit(1);
            }
            else
            {
                Debug.Log("[FPSKitBatch] verify impacts passed.\n" + notes);
                FPSKitBatch.Exit(0);
            }
        }

        // ------------------------------------------------------------------
        static void CheckSurfaces(List<string> problems, StringBuilder notes)
        {
            void Expect(string what, RaycastHit? hit, ImpactSurface wanted)
            {
                if (hit == null) { problems.Add($"{what}: nothing found to strike"); return; }
                var got = FPSImpactManager.SurfaceOf(hit.Value);
                string physics = hit.Value.collider.sharedMaterial != null ? hit.Value.collider.sharedMaterial.name : "none";
                if (got != wanted) problems.Add($"{what}: struck {hit.Value.collider.name} ({physics}) and got {got}, not {wanted}");
                else notes.AppendLine($"  {what}: {got} ({hit.Value.collider.name}, physics {physics})");
            }

            Expect("plaster or brick wall", FaceHit(n => n.Contains("plaster") || n.Contains("brick") || n.Contains("ashlar")), ImpactSurface.Concrete);
            var shutter = FaceHit(n => n.Contains("shopshutter"));
            Expect("shop shutter (in the wall's mesh)", shutter, ImpactSurface.Metal);
            if (shutter != null && (shutter.Value.collider.sharedMaterial == null || !shutter.Value.collider.sharedMaterial.name.EndsWith("Concrete")))
                problems.Add("the shutter's chunk is not concrete by its physics material: the face check is not what was tested");
            Expect("shop glass", FaceHit(n => n.Contains("shopglass")), ImpactSurface.Glass);

            RaycastHit? DownOnto(Collider c)
            {
                var b = c.bounds;
                return Physics.Raycast(b.center + Vector3.up * (b.extents.y + 2f), Vector3.down, out var h, b.size.y + 4f, ~0, QueryTriggerInteraction.Ignore) &&
                       h.collider == c ? h : (RaycastHit?)null;
            }
            var car = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.transform.root.name == "Arena" && c.GetComponentInParent<LODGroup>() != null && c.CompareTag("Metal") &&
                                     c.sharedMaterial != null && c.sharedMaterial.name == "Surface_Metal" && DownOnto(c) != null);
            Expect("parked car", car != null ? DownOnto(car) : null, ImpactSurface.Metal);

            var crate = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Cover_crates")
                .SelectMany(t => t.GetComponentsInChildren<Collider>()).FirstOrDefault(c => DownOnto(c) != null);
            Expect("crate stack", crate != null ? DownOnto(crate) : null, ImpactSurface.Wood);

            RaycastHit? sand = null;
            foreach (var p in new[] { new Vector2(330f, -470f), new Vector2(-450f, 420f), new Vector2(460f, -300f), new Vector2(-470f, -200f) })
                if (Physics.Raycast(new Vector3(p.x, 400f, p.y), Vector3.down, out var h, 800f, ~0, QueryTriggerInteraction.Ignore) && h.collider is TerrainCollider)
                { sand = h; break; }
            Expect("open desert", sand, ImpactSurface.Sand);
        }

        /// <summary>A real strike on a face of a building chunk whose material's name passes <paramref name="named"/>: a ray
        /// down the face's normal onto its middle, kept if it lands on that chunk and that face's material.</summary>
        static RaycastHit? FaceHit(Func<string, bool> named)
        {
            var arena = GameObject.Find("Arena");
            if (arena == null) return null;
            foreach (var mc in arena.GetComponentsInChildren<MeshCollider>())
            {
                var mesh = mc.sharedMesh;
                if (mesh == null || mesh.subMeshCount < 2 || !mesh.isReadable || !mc.TryGetComponent(out MeshRenderer mr)) continue;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null || !named(mats[s].name.ToLowerInvariant())) continue;
                    var tris = mesh.GetTriangles(s);
                    var verts = mesh.vertices;
                    for (int t = 0; t + 2 < tris.Length && t < 3 * 60; t += 3)
                    {
                        Vector3 a = mc.transform.TransformPoint(verts[tris[t]]), b = mc.transform.TransformPoint(verts[tris[t + 1]]), c = mc.transform.TransformPoint(verts[tris[t + 2]]);
                        var n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-6f) continue;
                        n.Normalize();
                        Vector3 mid = (a + b + c) / 3f;
                        foreach (var dir in new[] { n, -n })
                            if (Physics.Raycast(mid + dir * 0.3f, -dir, out var hit, 0.6f, ~0, QueryTriggerInteraction.Ignore) && hit.collider == mc)
                            {
                                var face = FaceMaterialOf(mesh, mats, hit.triangleIndex);
                                if (face == mats[s]) return hit;
                            }
                    }
                }
            }
            return null;
        }

        static Material FaceMaterialOf(Mesh mesh, Material[] mats, int triangle)
        {
            int index = triangle * 3;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var sm = mesh.GetSubMesh(i);
                if (index >= sm.indexStart && index < sm.indexStart + sm.indexCount) return i < mats.Length ? mats[i] : null;
            }
            return null;
        }

        // ------------------------------------------------------------------
        static void CheckPools(List<string> problems, StringBuilder notes)
        {
            // Real strikes: rays from the avenue's pavements and the side streets out at the walls, the shops and the ground.
            var hits = new List<RaycastHit>();
            var rand = new System.Random(5);
            for (int i = 0; i < 2000 && hits.Count < 300; i++)
            {
                var from = new Vector3((float)(rand.NextDouble() * 30 - 15), 0f, (float)(rand.NextDouble() * 500 - 350));
                from.y = (Physics.Raycast(from + Vector3.up * 300f, Vector3.down, out var g, 600f) ? g.point.y : 0f) + 1.5f;
                var dir = new Vector3((float)(rand.NextDouble() * 2 - 1), (float)(rand.NextDouble() * 0.8 - 0.5), (float)(rand.NextDouble() * 2 - 1)).normalized;
                if (Physics.Raycast(from, dir, out var h, 120f, ~0, QueryTriggerInteraction.Ignore) && FPSImpactManager.SurfaceOf(h) != ImpactSurface.Flesh) hits.Add(h);
            }
            if (hits.Count < 50) { problems.Add($"only {hits.Count} strikes found to fire with"); return; }
            var kinds = hits.GroupBy(FPSImpactManager.SurfaceOf).ToDictionary(g => g.Key, g => g.Count());

            var manager = FPSImpactManager.Instance;
            try
            {
                for (int i = 0; i < 400; i++) { manager.HandleImpact(hits[i % hits.Count]); if (i % 10 == 9) manager.Advance(0.1f); }   // warm
                int objects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                long before = GC.GetAllocatedBytesForCurrentThread();
                int maxMarks = 0, maxFlashes = 0, maxParticles = 0;
                const int rounds = 6000;                                     // ten minutes of fire at ten rounds a second
                for (int i = 0; i < rounds; i++)
                {
                    manager.HandleImpact(hits[i % hits.Count]);
                    if (i % 10 == 9) manager.Advance(0.1f);
                    maxMarks = Mathf.Max(maxMarks, manager.LiveMarks);
                    maxFlashes = Mathf.Max(maxFlashes, manager.LitFlashes);
                    if (i % 50 == 0) maxParticles = Mathf.Max(maxParticles, manager.LiveParticles);
                }
                long grew = GC.GetAllocatedBytesForCurrentThread() - before;
                int madeObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length - objects;
                if (maxMarks == 0) problems.Add("no marks were left");
                if (maxMarks > manager.MarkCapacity) problems.Add($"{maxMarks} marks at once, over the pool of {manager.MarkCapacity}");
                if (maxFlashes > 3) problems.Add($"{maxFlashes} flashes lit at once (3 pooled)");
                if (maxParticles > 600 + 240 + 360) problems.Add($"{maxParticles} particles at once, over the pools");
                if (madeObjects != 0) problems.Add($"{madeObjects} objects made while firing (none wanted)");
                if (grew > 0) problems.Add($"{grew} bytes allocated over {rounds} strikes (none wanted)");

                manager.Advance(31f);
                if (manager.LiveMarks != 0) problems.Add($"{manager.LiveMarks} marks still there 31 s after the last strike");
                if (manager.LitFlashes != 0) problems.Add($"{manager.LitFlashes} flashes still lit after the firing");
                notes.AppendLine($"  {rounds} strikes ({string.Join(", ", kinds.Select(kv => $"{kv.Value} {kv.Key}"))} among {hits.Count} spots): marks {maxMarks}/{manager.MarkCapacity}, " +
                                 $"flashes {maxFlashes}/3, particles {maxParticles}, objects made {madeObjects}, {grew} bytes allocated; all marks gone at 30 s");
            }
            finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
        }
    }
}
#endif
