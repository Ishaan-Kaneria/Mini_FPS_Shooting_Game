#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Lamp posts, signal poles and sign poles that a car knocks down (his note 2026-11-16: driving into a pole or a bin, it "acts as a
    /// fixed object"). The town builds every pole into its one shared "Signs" mesh, one draw for the lot, and a mesh collider on it: a
    /// wall. <see cref="Split"/> takes each standing pole back out of that mesh into an object of its own: its own triangles, a box per
    /// post (flat-footed, so a woken pole stands until struck: a capsule end would topple it), a box round its arm, head or panel, a
    /// kinematic rigidbody of its weight and an anchored LooseProp (breakSpeed), so it stands like a wall to people and to a car
    /// creeping into it, and goes over to a car at speed. A signal's head goes with its pole. What is left in the shared mesh (tree
    /// pits, wall-mounted signs) stays as it was.
    ///
    /// The town builder runs it on the mesh it has just built; the saved desert takes it with no rebuild or bake:
    /// <c>Tools/unity-batch.sh FPSKitBatch.PatchDesertPoles</c> (which also makes the bins and plaza lamps loose, FPSKitTownAssets).
    /// Idempotent: a second run finds no pole left in the shared mesh.
    /// </summary>
    public static class FPSKitPoles
    {
        const string DesertScene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        /// <summary>Metres a second (about 14 km/h) a car needs to knock down a lamp or signal pole.</summary>
        public const float LampBreak = 4f;
        /// <summary>A thin sign pole goes over to a car at about 9 km/h.</summary>
        public const float SignBreak = 2.5f;

        /// <summary>A vertical post found in a set of points: centre on the ground plane, half its width, its top.</summary>
        public struct Post { public Vector2 centre; public float half; public float top; }

        /// <summary>
        /// The posts a shape stands on: where its edges (<paramref name="edges"/>, world) cross the level 0.75 m over its lowest point, grouped
        /// where they lie within 0.35 m of each other. Edges, not vertices: a post's cylinder has vertices only at its two ends. Null when
        /// nothing crosses, a group is wider than a post (0.4 m), or there are more than two: not a pole.
        /// </summary>
        public static List<Post> PostsOf(IReadOnlyList<(Vector3 a, Vector3 b)> edges, float minY)
        {
            float level = minY + 0.75f;
            var slice = new List<Vector2>();
            foreach (var (a, b) in edges)
            {
                if ((a.y - level) * (b.y - level) > 0f || Mathf.Approximately(a.y, b.y)) continue;
                var p = Vector3.Lerp(a, b, (level - a.y) / (b.y - a.y));
                slice.Add(new Vector2(p.x, p.z));
            }
            if (slice.Count == 0) return null;
            var groups = new List<List<Vector2>>();
            foreach (var q in slice)
            {
                List<Vector2> into = null;
                for (int g = groups.Count - 1; g >= 0; g--)
                {
                    if (!groups[g].Any(o => (o - q).sqrMagnitude < 0.35f * 0.35f)) continue;
                    if (into == null) { into = groups[g]; into.Add(q); }
                    else { into.AddRange(groups[g]); groups.RemoveAt(g); }
                }
                if (into == null) groups.Add(new List<Vector2> { q });
            }
            if (groups.Count > 2) return null;
            var posts = new List<Post>();
            foreach (var g in groups)
            {
                Vector2 lo = g[0], hi = g[0];
                foreach (var q in g) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
                var size = hi - lo;
                if (Mathf.Max(size.x, size.y) > 0.4f) return null;
                var post = new Post { centre = (lo + hi) * 0.5f, half = Mathf.Max(0.04f, Mathf.Max(size.x, size.y) * 0.5f), top = minY };
                foreach (var (a, b) in edges)
                    foreach (var p in new[] { a, b })
                        if ((new Vector2(p.x, p.z) - post.centre).sqrMagnitude <= (post.half + 0.02f) * (post.half + 0.02f)) post.top = Mathf.Max(post.top, p.y);
                posts.Add(post);
            }
            return posts;
        }

        /// <summary>
        /// Box colliders on <paramref name="host"/> for a pole whose points (world) are <paramref name="pts"/>: one per post, from just over
        /// <paramref name="ground"/> to its top, and one round everything off the posts (arm, head, panel), so a fallen pole lies on it.
        /// </summary>
        public static void AddBoxes(GameObject host, IReadOnlyList<Vector3> pts, List<Post> posts, float ground)
        {
            var t = host.transform;
            foreach (var post in posts)
            {
                float h = Mathf.Max(0.3f, post.top - ground - 0.01f);
                var box = host.AddComponent<BoxCollider>();
                var c = new Vector3(post.centre.x, ground + 0.01f + h * 0.5f, post.centre.y);
                box.center = t.InverseTransformPoint(c);
                box.size = Abs(t.InverseTransformVector(new Vector3(post.half * 2f, h, post.half * 2f)));
            }
            bool any = false; Bounds off = default;
            foreach (var p in pts)
            {
                var q = new Vector2(p.x, p.z);
                if (posts.Any(o => (q - o.centre).sqrMagnitude <= (o.half + 0.06f) * (o.half + 0.06f))) continue;
                if (p.y < ground + 1.2f) continue;          // a base plate is not a head
                var local = t.InverseTransformPoint(p);
                if (!any) { off = new Bounds(local, Vector3.zero); any = true; } else off.Encapsulate(local);
            }
            if (!any) return;
            var head = host.AddComponent<BoxCollider>();
            head.center = off.center;
            head.size = Vector3.Max(off.size, Abs(t.InverseTransformVector(Vector3.one * 0.05f)));
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>A kinematic rigidbody of <paramref name="kg"/> and an anchored LooseProp that gives way to a car at <paramref name="breakSpeed"/>.</summary>
        public static void MakeKnockable(GameObject go, float kg, float breakSpeed)
        {
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.mass = kg;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.4f;
            rb.isKinematic = true;
            if (!go.TryGetComponent<LooseProp>(out var loose)) loose = go.AddComponent<LooseProp>();
            loose.stuff = LooseProp.Stuff.Metal;
            loose.breakSpeed = breakSpeed;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(tr.gameObject, 0);
        }

        // ------------------------------------------------------------------ taking the poles out of the shared mesh
        class Piece { public Bounds b; public bool ground; public int parent; }

        /// <summary>Takes every standing pole out of <paramref name="signs"/> (a mesh with a MeshFilter) into its own knockable object. Returns how many.</summary>
        public static int Split(GameObject signs)
        {
            if (signs == null || !signs.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) return 0;
            var mesh = mf.sharedMesh;
            var tf = signs.transform;
            var mr = signs.GetComponent<MeshRenderer>();
            var mats = mr != null ? mr.sharedMaterials : new Material[0];
            var local = mesh.vertices;
            var world = local.Select(v => tf.TransformPoint(v)).ToArray();
            var subs = Enumerable.Range(0, mesh.subMeshCount).Select(s => mesh.GetTriangles(s)).ToArray();

            // Pieces: vertices joined by triangles.
            var up = Enumerable.Range(0, local.Length).ToArray();
            int Find(int[] a, int i) { while (a[i] != i) { a[i] = a[a[i]]; i = a[i]; } return i; }
            foreach (var tris in subs)
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int r0 = Find(up, tris[i]);
                    int r1 = Find(up, tris[i + 1]); if (r1 != r0) up[r1] = r0;
                    int r2 = Find(up, tris[i + 2]); if (r2 != r0) up[r2] = r0;
                }
            var pieceOf = new int[local.Length];
            var pieces = new List<Piece>();
            var index = new Dictionary<int, int>();
            for (int v = 0; v < local.Length; v++)
            {
                int r = Find(up, v);
                if (!index.TryGetValue(r, out int p)) { p = pieces.Count; index[r] = p; pieces.Add(new Piece { b = new Bounds(world[v], Vector3.zero), parent = p }); }
                else pieces[p].b.Encapsulate(world[v]);
                pieceOf[v] = p;
            }
            // Flat and wide (a tree pit, a kerb slab) joins nothing: it lies on the ground, and a pole stood by it is not part of it.
            foreach (var p in pieces) p.ground = p.b.size.y < 0.2f && Mathf.Max(p.b.size.x, p.b.size.z) > 1f;

            // Pieces that touch are one thing: a pole, its base plate, arm, head and lens; a sign and its post.
            int Root(int i) { while (pieces[i].parent != i) { pieces[i].parent = pieces[pieces[i].parent].parent; i = pieces[i].parent; } return i; }
            var cells = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].ground) continue;
                var b = pieces[i].b; b.Expand(0.04f);
                var lo = Vector3Int.FloorToInt(b.min); var hi = Vector3Int.FloorToInt(b.max);
                if ((hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1) > 4000) continue;     // nothing that size is a pole
                for (int x = lo.x; x <= hi.x; x++) for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++)
                {
                    var key = new Vector3Int(x, y, z);
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                    foreach (int j in list)
                    {
                        var bj = pieces[j].b; bj.Expand(0.04f);
                        if (!bj.Intersects(pieces[i].b)) continue;
                        int ri = Root(i), rj = Root(j);
                        if (ri != rj) pieces[ri].parent = rj;
                    }
                    list.Add(i);
                }
            }

            // Each joined thing: its vertices, and whether it is a pole.
            var verts = new Dictionary<int, List<int>>();
            for (int v = 0; v < local.Length; v++)
            {
                int r = Root(pieceOf[v]);
                if (!verts.TryGetValue(r, out var l)) verts[r] = l = new List<int>();
                l.Add(v);
            }
            var holder = tf.parent != null ? tf.parent.Find("Poles") : null;
            if (holder == null)
            {
                holder = new GameObject("Poles").transform;
                holder.SetParent(tf.parent, false);
            }
            var heads = (tf.parent != null ? tf.parent.GetComponentsInChildren<Transform>(true) : new Transform[0]).Where(t => t.name == "SignalHead").ToList();
            var others = signs.GetComponents<Component>().Where(c => !(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider)).ToArray();
            var ownCollider = signs.GetComponent<Collider>();
            Physics.SyncTransforms();

            var taken = new HashSet<int>();      // roots taken out
            int made = 0, tall = 0, notPosts = 0;
            var why = new System.Text.StringBuilder();
            foreach (var kv in verts)
            {
                var pts = kv.Value.Select(v => world[v]).ToList();
                float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
                if (maxY - minY < 1.8f) continue;
                tall++;
                var edges = new List<(Vector3, Vector3)>();
                foreach (var tris in subs)
                    for (int i = 0; i < tris.Length; i += 3)
                        if (Root(pieceOf[tris[i]]) == kv.Key)
                        { var a = world[tris[i]]; var b = world[tris[i + 1]]; var c = world[tris[i + 2]]; edges.Add((a, b)); edges.Add((b, c)); edges.Add((c, a)); }
                var posts = PostsOf(edges, minY);
                if (posts == null)
                {
                    if (notPosts++ < 12)
                    {
                        var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p);
                        why.Append($" [{pts.Count} verts, box {b.min:F1}..{b.max:F1}]");
                    }
                    continue;
                }

                // Where the ground is under it (a sign pole is sunk 30 cm into the pavement): the first thing under its foot that is not the shared mesh.
                Vector2 foot2 = posts.Aggregate(Vector2.zero, (s, p) => s + p.centre) / posts.Count;
                float ground = minY;
                var hits = Physics.RaycastAll(new Vector3(foot2.x, minY + 1.2f, foot2.y), Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits.OrderBy(h => h.distance))
                {
                    if (h.collider == ownCollider || h.collider.GetComponentInParent<LooseProp>() != null) continue;
                    ground = h.point.y; break;
                }
                // Nothing found under it (or something far off): a thin post is sunk 30 cm (RoadSign, SignPanel), a lamp stands on its plate.
                bool thin = posts.Max(p => p.half) < 0.08f;
                if (ground < minY - 0.5f || ground > minY + 0.6f || hits.Length == 0) ground = thin ? minY + 0.3f : minY;

                float half = posts.Max(p => p.half), height = maxY - ground;
                bool stout = half >= 0.08f;
                string kind = stout ? (height > 6f ? "LampPost" : "SignalPole") : "SignPole";
                var go = new GameObject(kind);
                go.transform.SetParent(holder, false);
                go.transform.position = new Vector3(foot2.x, ground, foot2.y);
                go.layer = signs.layer;
                go.tag = signs.tag;

                // Its own mesh: its triangles, by material.
                var map = new Dictionary<int, int>();
                foreach (int v in kv.Value) map[v] = map.Count;
                var own = new Mesh { name = kind };
                var order = kv.Value.ToArray();
                own.SetVertices(order.Select(v => go.transform.InverseTransformPoint(world[v])).ToList());
                if (mesh.normals.Length == local.Length) { var n = mesh.normals; own.SetNormals(order.Select(v => tf.TransformDirection(n[v])).ToList()); }
                if (mesh.uv.Length == local.Length) { var uv = mesh.uv; own.SetUVs(0, order.Select(v => uv[v]).ToList()); }
                var ownMats = new List<Material>(); var ownSubs = new List<int[]>();
                var mine = new HashSet<int>(kv.Value);
                for (int s = 0; s < subs.Length; s++)
                {
                    var t = new List<int>();
                    for (int i = 0; i < subs[s].Length; i += 3)
                        if (mine.Contains(subs[s][i])) { t.Add(map[subs[s][i]]); t.Add(map[subs[s][i + 1]]); t.Add(map[subs[s][i + 2]]); }
                    if (t.Count == 0) continue;
                    ownSubs.Add(t.ToArray()); ownMats.Add(s < mats.Length ? mats[s] : null);
                }
                own.subMeshCount = ownSubs.Count;
                for (int s = 0; s < ownSubs.Count; s++) own.SetTriangles(ownSubs[s], s);
                own.RecalculateBounds();
                if (mesh.tangents.Length == local.Length) own.RecalculateTangents();
                go.AddComponent<MeshFilter>().sharedMesh = own;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = ownMats.ToArray();
                if (mr != null)
                {
                    r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                    r.lightProbeUsage = mr.lightProbeUsage; r.reflectionProbeUsage = mr.reflectionProbeUsage; r.renderingLayerMask = mr.renderingLayerMask;
                }
                foreach (var c in others) { UnityEditorInternal.ComponentUtility.CopyComponent(c); UnityEditorInternal.ComponentUtility.PasteComponentAsNew(go); }

                AddBoxes(go, pts, posts, ground);

                // A signal's head goes down with its pole.
                foreach (var h in heads.ToArray())
                {
                    var hp = h.position;
                    if (hp.y < ground || hp.y > maxY + 0.8f) continue;
                    if (!posts.Any(p => (new Vector2(hp.x, hp.z) - p.centre).sqrMagnitude < 0.45f * 0.45f)) continue;
                    h.SetParent(go.transform, true);
                    heads.Remove(h);
                }
                // A galvanised lamp column about 22 kg a metre, a signal pole the same; a thin sign post 8 kg a metre a post. After the
                // head is on it: MakeKnockable takes everything under it out of static batching (a batched head would stay in the air).
                MakeKnockable(go, stout ? 22f * height : 8f * height * posts.Count, stout ? LampBreak : SignBreak);
                taken.Add(kv.Key);
                made++;
            }
            Debug.Log($"[FPSKit] {signs.name}: {local.Length} vertices, {pieces.Count} pieces, {verts.Count} joined, {tall} tall, {made} poles; not poles: {notPosts}{why}");
            if (made == 0) return 0;

            // What is left stays in the shared mesh.
            var keep = new List<int>();
            for (int v = 0; v < local.Length; v++) if (!taken.Contains(Root(pieceOf[v]))) keep.Add(v);
            var remap = new Dictionary<int, int>();
            foreach (int v in keep) remap[v] = remap.Count;
            var rest = new Mesh { name = mesh.name };
            if (keep.Count > 65000) rest.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            rest.SetVertices(keep.Select(v => local[v]).ToList());
            if (mesh.normals.Length == local.Length) { var n = mesh.normals; rest.SetNormals(keep.Select(v => n[v]).ToList()); }
            if (mesh.uv.Length == local.Length) { var uv = mesh.uv; rest.SetUVs(0, keep.Select(v => uv[v]).ToList()); }
            rest.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++)
            {
                var t = new List<int>();
                for (int i = 0; i < subs[s].Length; i += 3)
                    if (remap.ContainsKey(subs[s][i])) { t.Add(remap[subs[s][i]]); t.Add(remap[subs[s][i + 1]]); t.Add(remap[subs[s][i + 2]]); }
                rest.SetTriangles(t.ToArray(), s);
            }
            rest.RecalculateBounds();
            if (mesh.tangents.Length == local.Length) rest.RecalculateTangents();
            var compression = MeshUtility.GetMeshCompression(mesh);
            if (compression != ModelImporterMeshCompression.Off) MeshUtility.SetMeshCompression(rest, compression);
            mf.sharedMesh = rest;
            if (ownCollider is MeshCollider mc) mc.sharedMesh = rest;
            return made;
        }

        // ------------------------------------------------------------------ into the saved scene, without a rebuild
        /// <summary>
        /// The saved desert's poles taken out of its sign meshes and made knockable, the bins and the plaza lamps made loose
        /// (FPSKitTownAssets.MakeLooseProps), every loose prop's instance kept out of static batching. Lighting and navmesh untouched.
        /// </summary>
        public static void PatchDesert()
        {
            int prefabs = FPSKitTownAssets.MakeLooseProps();
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene(DesertScene, OpenSceneMode.Single);
            int poles = 0, signMeshes = 0;
            var signs = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                             .Where(t => t.name == "Signs" && t.GetComponent<MeshFilter>() != null).ToArray();
            foreach (var t in signs) { poles += Split(t.gameObject); signMeshes++; }
            int unbatched = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var lp in root.GetComponentsInChildren<LooseProp>(true))
                    foreach (var tr in lp.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetStaticEditorFlags(tr.gameObject) != 0) { GameObjectUtility.SetStaticEditorFlags(tr.gameObject, 0); unbatched++; }
            var kinds = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LooseProp>(true))
                             .GroupBy(l => l.name.Split(' ')[0]).Select(g => $"{g.Key} {g.Count()}").ToArray();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FPSKit] poles: {poles} taken out of {signMeshes} sign mesh(es); {prefabs} loose prefabs; {unbatched} static flags cleared; loose props by name: {string.Join(", ", kinds)}");
        }
    }
}
#endif
