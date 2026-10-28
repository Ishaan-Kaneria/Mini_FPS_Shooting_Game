#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>The realism audit's five-light test (FPSKitRealismAudit, FPSKitRealismEvidence).</summary>
    public static partial class FPSKitRealismAudit
    {
        sealed class Part
        {
            public Mesh mesh; public Material[] mats; public Matrix4x4 m; public bool cut;
            public readonly HashSet<int> own = new HashSet<int>();
        }

        /// <summary>What goes on the stage for an object, in stage space (the spot at the origin, its face towards -Z).</summary>
        sealed class Patch : IDisposable
        {
            public readonly List<Part> parts = new List<Part>();
            public TerrainLayer layer; public Material terrainMaterial;
            public bool floor, ceiling, model, held;
            public void Dispose() { foreach (var p in parts) if (p.cut && p.mesh != null) Object.DestroyImmediate(p.mesh); }
        }

        const float PatchRadius = 1.8f;

        static Patch CutPatch(Item it, Pose pose)
        {
            if (it.skyMaterial != null) return null;
            if (it.source == "player weapon")
            {
                // A first-person model as a model on the stage: side-on to the camera, its middle at the origin.
                if (it.prefabAsset == null) return null;
                var go = SideOn(it.prefabAsset, Vector3.zero, Quaternion.identity);
                var weapon = new Patch { model = true, held = true };
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var part = new Part { mesh = mesh, mats = r.sharedMaterials, m = r.localToWorldMatrix };
                    for (int s = 0; s < mesh.subMeshCount; s++) part.own.Add(s);
                    weapon.parts.Add(part);
                }
                Object.DestroyImmediate(go);
                return weapon.parts.Count > 0 ? weapon : null;
            }
            if (it.terrainLayer != null) return new Patch { layer = it.terrainLayer, terrainMaterial = it.terrain != null ? it.terrain.materialTemplate : null, floor = true };

            // Stage space: the spot at the origin; turned about the vertical so the camera's side faces -Z.
            var face = Vector3.ProjectOnPlane(pose.floor || pose.ceiling ? pose.cam - pose.target : pose.normal, Vector3.up);
            if (face.sqrMagnitude < 1e-4f) face = Vector3.ProjectOnPlane(pose.cam - pose.target, Vector3.up);
            var turn = face.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(face.normalized, Vector3.back) : Quaternion.identity;
            var toStage = Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-pose.target);
            var patch = new Patch { floor = pose.floor, ceiling = pose.ceiling };

            if (pose.instance != null)
            {
                // A model: all of its instance, as it stands.
                patch.model = true;
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(pose.instance.gameObject);
                var renderers = root != null ? root.GetComponentsInChildren<Renderer>() : new[] { pose.instance };
                foreach (var r in renderers)
                {
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var part = new Part { mesh = mesh, mats = r.sharedMaterials, m = toStage * r.localToWorldMatrix };
                    if (it.renderers.Contains(r)) for (int s = 0; s < mesh.subMeshCount; s++) part.own.Add(s);
                    patch.parts.Add(part);
                }
                return patch.parts.Count > 0 ? patch : null;
            }

            // A surface: every triangle within 1.8 m of the spot, from every mesh there (its frames, sills and neighbours too).
            var sphere = new Bounds(pose.target, Vector3.one * PatchRadius * 2f);
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.bounds.Intersects(sphere)) continue;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var own = new HashSet<int>(it.members.Where(m => m.renderer == r).Select(m => m.sub));
                var cut = Cut(mesh, r.localToWorldMatrix, toStage, pose.target, own, out var ownSubs);
                if (cut == null) continue;
                var part = new Part { mesh = cut, mats = r.sharedMaterials, m = Matrix4x4.identity, cut = true };
                foreach (var s in ownSubs) part.own.Add(s);
                patch.parts.Add(part);
            }
            return patch.parts.Any(p => p.own.Count > 0) ? patch : null;
        }

        /// <summary>The triangles of <paramref name="mesh"/> passing within the patch radius of the spot, as a new mesh in
        /// stage space with the same submeshes (empty ones kept, so the materials line up).</summary>
        static Mesh Cut(Mesh mesh, Matrix4x4 toWorld, Matrix4x4 toStage, Vector3 spot, HashSet<int> own, out List<int> ownSubs)
        {
            ownSubs = new List<int>();
            using var arr = Mesh.AcquireReadOnlyMeshData(mesh);
            var md = arr[0];
            int vc = md.vertexCount;
            var pos = new NativeArray<Vector3>(vc, Allocator.Persistent);
            var nrm = new NativeArray<Vector3>(vc, Allocator.Persistent);
            var tan = new NativeArray<Vector4>(vc, Allocator.Persistent);
            var uv = new NativeArray<Vector2>(vc, Allocator.Persistent);
            md.GetVertices(pos);
            bool hasN = md.HasVertexAttribute(VertexAttribute.Normal), hasT = md.HasVertexAttribute(VertexAttribute.Tangent), hasUv = md.HasVertexAttribute(VertexAttribute.TexCoord0);
            if (hasN) md.GetNormals(nrm);
            if (hasT) md.GetTangents(tan);
            if (hasUv) md.GetUVs(0, uv);
            var toStageW = toStage * toWorld;
            var remap = new Dictionary<int, int>();
            var vP = new List<Vector3>(); var vN = new List<Vector3>(); var vT = new List<Vector4>(); var vU = new List<Vector2>();
            var subs = new List<int>[md.subMeshCount];
            float r2 = PatchRadius * PatchRadius;
            bool any = false;
            try
            {
                for (int s = 0; s < md.subMeshCount; s++)
                {
                    subs[s] = new List<int>();
                    var desc = md.GetSubMesh(s);
                    if (desc.topology != MeshTopology.Triangles) continue;
                    var idx = new NativeArray<int>(desc.indexCount, Allocator.Persistent);
                    md.GetIndices(idx, s, true);
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        // Any triangle that passes within the radius (a plaza's slab is a few huge triangles with no corner near).
                        Vector3 a = toWorld.MultiplyPoint3x4(pos[idx[t]]), b = toWorld.MultiplyPoint3x4(pos[idx[t + 1]]), c = toWorld.MultiplyPoint3x4(pos[idx[t + 2]]);
                        if ((ClosestOnTriangle(spot, a, b, c) - spot).sqrMagnitude >= r2) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int v = idx[t + k];
                            if (!remap.TryGetValue(v, out int nv))
                            {
                                nv = vP.Count; remap[v] = nv;
                                vP.Add(toStageW.MultiplyPoint3x4(pos[v]));
                                vN.Add(hasN ? toStageW.MultiplyVector(nrm[v]).normalized : Vector3.back);
                                var tv = hasT ? tan[v] : new Vector4(1, 0, 0, 1);
                                var td = toStageW.MultiplyVector(new Vector3(tv.x, tv.y, tv.z)).normalized;
                                vT.Add(new Vector4(td.x, td.y, td.z, tv.w));
                                vU.Add(hasUv ? uv[v] : Vector2.zero);
                            }
                            subs[s].Add(nv);
                        }
                        any = true;
                        if (own.Contains(s) && !ownSubs.Contains(s)) ownSubs.Add(s);
                    }
                    idx.Dispose();
                }
            }
            finally { pos.Dispose(); nrm.Dispose(); tan.Dispose(); uv.Dispose(); }
            if (!any) return null;
            var cut = new Mesh { name = mesh.name + "_patch", indexFormat = vP.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.DontSave };
            cut.SetVertices(vP); cut.SetNormals(vN); cut.SetTangents(vT); cut.SetUVs(0, vU);
            cut.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) cut.SetTriangles(subs[s], s, false);
            cut.RecalculateBounds();
            return cut;
        }

        // ------------------------------------------------------------------
        // The stage
        // ------------------------------------------------------------------
        static readonly string[] LightNames = { "midday", "evening", "shade", "indoor", "night" };
        /// <summary>Exposure added for the pictures only, standing in for the eye's adaptation (the measurements are of
        /// the unprocessed render against the grey card, which no exposure changes).</summary>
        static readonly float[] DisplayExposure = { 0f, 0.6f, 1.4f, 2.6f, 3.2f };

        static Material Plain(Color linear, float smooth)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", linear.gamma);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        static GameObject Box(string name, Vector3 centre, Vector3 size, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void Stage(Dictionary<Item, Patch> patches, (float intensity, Color colour, float kelvin) sunLook, VolumeProfile desertProfile)
        {
            if (patches.Count == 0) return;
            float ambient = RenderSettings.ambientIntensity, reflections = RenderSettings.reflectionIntensity;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetTier();

            var sky = new Material(Shader.Find("Skybox/Procedural")) { hideFlags = HideFlags.DontSave };
            sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 1.0f); sky.SetFloat("_Exposure", 1.25f);
            sky.SetColor("_GroundColor", new Color(0.55f, 0.48f, 0.40f));
            RenderSettings.fog = false;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
            sun.color = sunLook.colour; sun.intensity = sunLook.intensity;
            if (sunLook.kelvin > 0) { sun.useColorTemperature = true; sun.colorTemperature = sunLook.kelvin; }
            RenderSettings.sun = sun;
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 8f; lamp.intensity = 3f; lamp.shadows = LightShadows.Soft;
            lamp.color = Mathf.CorrelatedColorTemperatureToRGB(2700f);
            var door = new GameObject("DoorSky").AddComponent<Light>();
            door.type = LightType.Spot; door.spotAngle = 150f; door.innerSpotAngle = 60f; door.range = 9f; door.intensity = 1.6f;
            door.shadows = LightShadows.Soft; door.color = new Color(0.75f, 0.85f, 1f);

            var volumeGo = new GameObject("Look");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = desertProfile != null ? Object.Instantiate(desertProfile) : ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            if (!profile.TryGet<ColorAdjustments>(out var grade)) grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.overrideState = true;
            volume.sharedProfile = profile;

            var cards = new[] { Plain(new Color(0.18f, 0.18f, 0.18f), 0.1f), Plain(new Color(0.8f, 0.8f, 0.8f), 0.1f), Plain(new Color(0.04f, 0.04f, 0.04f), 0.1f) };
            var groundMat = Plain(new Color(0.33f, 0.29f, 0.24f), 0.1f);
            var roomMat = Plain(new Color(0.55f, 0.52f, 0.47f), 0.15f);

            const int MW = 480, MH = 270, DW = 960, DH = 540;
            var display = new RenderTexture(DW, DH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var measure = new RenderTexture(MW, MH, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var ids = new RenderTexture(MW, MH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var camGo = new GameObject("StageCamera") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 400f; cam.fieldOfView = 60f; cam.allowMSAA = false;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderShadows = true; camData.dithering = false;
            try
            {
                foreach (var kv in patches)
                {
                    var (it, patch) = (kv.Key, kv.Value);
                    var set = new GameObject("Set").transform;
                    var subject = BuildSubject(patch, set, out var own);
                    var b = new Bounds(Vector3.zero, Vector3.one * 0.2f);
                    bool first = true;
                    foreach (var r in subject.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                    if (patch.parts.Count > 0 && !patch.model) b = new Bounds(Vector3.zero, new Vector3(PatchRadius * 2f, PatchRadius * 2f, 0.4f));

                    // Camera, ground, cards: the cards in the subject's own plane (a wall's, the floor's), or facing the camera.
                    Vector3 eye, look, cardNormal;
                    float floorY;
                    if (patch.floor) { eye = new Vector3(0f, 1.3f, -0.95f); look = Vector3.zero; floorY = -0.01f; cardNormal = Vector3.up; }
                    else if (patch.ceiling) { eye = new Vector3(0f, -1.3f, -0.95f); look = Vector3.zero; floorY = -2.7f; cardNormal = Vector3.down; }
                    else if (patch.held) { eye = b.center + new Vector3(0f, 0.05f, -0.8f); look = b.center; floorY = b.min.y - 0.6f; cardNormal = Vector3.back; }
                    else if (patch.model) { float d = Mathf.Clamp(b.extents.magnitude * 1.6f, 1f, 2f) + Vector3.ProjectOnPlane(b.extents, Vector3.up).magnitude * 0.5f; eye = b.center + new Vector3(0f, Mathf.Min(0.4f, b.extents.y), -d); look = b.center; floorY = b.min.y - 0.005f; cardNormal = Vector3.back; }
                    else { eye = new Vector3(0f, 0f, -1.5f); look = Vector3.zero; floorY = Mathf.Min(-0.3f, b.min.y) - 0.005f; cardNormal = Vector3.back; }
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    var ground = Box("Ground", new Vector3(0f, floorY - 0.05f, 0f), new Vector3(60f, 0.1f, 60f), groundMat, set);
                    own[ground.GetComponent<Renderer>()] = -1;
                    var cardRenderers = new Renderer[3];
                    for (int c = 0; c < 3; c++)
                    {
                        // Lower left of the frame, a little in front of the subject.
                        var vp = cam.ViewportToWorldPoint(new Vector3(0.07f + c * 0.075f, 0.12f, Vector3.Distance(eye, look) * 0.9f));
                        var card = Box($"Card{c}", vp, Vector3.one * 0.0001f, cards[c], set);
                        float size = Vector3.Distance(eye, look) * 0.07f;
                        card.transform.localScale = new Vector3(size, size, 0.005f);
                        card.transform.rotation = Quaternion.LookRotation(-cardNormal, patch.floor || patch.ceiling ? Vector3.forward : Vector3.up);
                        if (patch.floor) card.transform.position = new Vector3(vp.x, 0.004f, vp.z);
                        if (patch.ceiling) card.transform.position = new Vector3(vp.x, -0.004f, vp.z);
                        cardRenderers[c] = card.GetComponent<Renderer>();
                    }
                    var room = BuildRoom(set, floorY, roomMat);
                    var blocker = Box("Blocker", Vector3.zero, new Vector3(14f, 14f, 0.05f), groundMat, set);
                    blocker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;

                    // ---- the id mask: the object 1, the cards 2-4 ----
                    room.SetActive(false); blocker.SetActive(false);
                    var saved = new Dictionary<Renderer, Material[]>();
                    foreach (var r in set.GetComponentsInChildren<Renderer>())
                    {
                        saved[r] = r.sharedMaterials;
                        int id = Array.IndexOf(cardRenderers, r) is int ci && ci >= 0 ? 2 + ci : 0;
                        r.sharedMaterials = r.sharedMaterials.Select((m, s) => IdMaterial(id != 0 ? id : own.TryGetValue(r, out var o) && (o == int.MaxValue || o == s) ? 1 : 0, m)).ToArray();
                    }
                    var t = subject.GetComponentInChildren<Terrain>();
                    Material tWas = null;
                    if (t != null) { tWas = t.materialTemplate; t.materialTemplate = IdMaterial(1, null); }
                    camData.renderPostProcessing = false; camData.antialiasing = AntialiasingMode.None;
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false;
                    RenderSettings.skybox = null;
                    Render(cam, ids);
                    var idPx = Read32(ids);
                    foreach (var kvr in saved) kvr.Key.sharedMaterials = kvr.Value;
                    if (t != null) t.materialTemplate = tWas;

                    // ---- five lights ----
                    var strip = new Texture2D(DW * 5, DH, TextureFormat.RGB24, false);
                    var stats = new (float obj, float grey, float cv)[6];
                    for (int k = 0; k < 5; k++)
                    {
                        SetLights(k, sun, lamp, door, sky, room, blocker, look, floorY, ambient, reflections, sunLook);
                        DynamicGI.UpdateEnvironment();
                        grade.postExposure.value = DisplayExposure[k];
                        camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                        cam.clearFlags = k == 4 ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; cam.backgroundColor = new Color(0.005f, 0.006f, 0.01f); cam.allowHDR = true;
                        Render(cam, display, twice: true);
                        var prev = RenderTexture.active; RenderTexture.active = display;
                        strip.ReadPixels(new Rect(0, 0, DW, DH), DW * k, 0);
                        RenderTexture.active = prev;
                        camData.renderPostProcessing = false; camData.antialiasing = AntialiasingMode.None;
                        Render(cam, measure);
                        var hdr = ReadFloat(measure);
                        stats[k] = MeasureStage(hdr, idPx);
                    }
                    // Sixth, measured only: every light off, no sky, no reflections. Whatever is still seen is emitting.
                    SetLights(4, sun, lamp, door, sky, room, blocker, look, floorY, ambient, reflections, sunLook);
                    lamp.enabled = false;
                    RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0f;
                    DynamicGI.UpdateEnvironment();
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                    camData.renderPostProcessing = false;
                    Render(cam, measure);
                    stats[5] = MeasureStage(ReadFloat(measure), idPx);
                    strip.Apply(false);
                    File.WriteAllBytes($"{it.evidence}/light_test.jpg", strip.EncodeToJPG(88));
                    Object.DestroyImmediate(strip);
                    ReportStage(it, stats);
                    Object.DestroyImmediate(set.gameObject);
                }
            }
            finally
            {
                display.Release(); measure.Release(); ids.Release();
                Object.DestroyImmediate(display); Object.DestroyImmediate(measure); Object.DestroyImmediate(ids);
                Object.DestroyImmediate(camGo);
            }
        }

        /// <summary>The subject on the stage; <paramref name="own"/> marks the object's own renderers (int.MaxValue all
        /// submeshes, else the one submesh; -1 none).</summary>
        static Transform BuildSubject(Patch patch, Transform set, out Dictionary<Renderer, int> own)
        {
            own = new Dictionary<Renderer, int>();
            var root = new GameObject("Subject").transform;
            root.SetParent(set, false);
            if (patch.layer != null)
            {
                var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16, hideFlags = HideFlags.DontSave };
                data.size = new Vector3(8f, 1f, 8f);
                data.terrainLayers = new[] { patch.layer };
                var alpha = new float[16, 16, 1];
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) alpha[y, x, 0] = 1f;
                data.SetAlphamaps(0, 0, alpha);
                var go = Terrain.CreateTerrainGameObject(data);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(-4f, 0f, -4f);
                var t = go.GetComponent<Terrain>();
                if (patch.terrainMaterial != null) t.materialTemplate = patch.terrainMaterial;
                return root;
            }
            foreach (var p in patch.parts)
            {
                var go = new GameObject(p.mesh.name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(p.m.GetPosition(), p.m.rotation);
                go.transform.localScale = p.m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = p.mats;
                // An object is one material, so one submesh of a cut is its own (a model owns all of its renderers).
                own[r] = p.own.Count == 0 ? -1 : p.own.Count == p.mesh.subMeshCount || patch.model ? int.MaxValue : p.own.First();
            }
            return root;
        }

        /// <summary>A plastered room round the subject and the camera, its only opening a doorway in the left wall.</summary>
        static GameObject BuildRoom(Transform set, float floorY, Material mat)
        {
            var room = new GameObject("Room");
            room.transform.SetParent(set, false);
            float x0 = -2.7f, x1 = 2.7f, z0 = -3.8f, z1 = 0.35f, h = 3.0f, t = 0.2f;
            float y0 = floorY, y1 = floorY + h;
            Box("Floor", new Vector3(0f, y0 - t * 0.5f - 0.02f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 2 * t, t, z1 - z0 + 2 * t), mat, room.transform);
            Box("Ceiling", new Vector3(0f, y1 + t * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 2 * t, t, z1 - z0 + 2 * t), mat, room.transform);
            Box("Back", new Vector3(0f, (y0 + y1) * 0.5f, z1 + t * 0.5f), new Vector3(x1 - x0 + 2 * t, h, t), mat, room.transform);
            Box("Front", new Vector3(0f, (y0 + y1) * 0.5f, z0 - t * 0.5f), new Vector3(x1 - x0 + 2 * t, h, t), mat, room.transform);
            Box("Right", new Vector3(x1 + t * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0), mat, room.transform);
            // Left wall with a doorway 1 m wide, 2.1 m high, from z -3.1 to -2.1.
            float d0 = -3.1f, d1 = -2.1f, dh = 2.1f;
            Box("LeftA", new Vector3(x0 - t * 0.5f, (y0 + y1) * 0.5f, (z0 + d0) * 0.5f), new Vector3(t, h, d0 - z0), mat, room.transform);
            Box("LeftB", new Vector3(x0 - t * 0.5f, (y0 + y1) * 0.5f, (d1 + z1) * 0.5f), new Vector3(t, h, z1 - d1), mat, room.transform);
            Box("Lintel", new Vector3(x0 - t * 0.5f, y0 + dh + (h - dh) * 0.5f, (d0 + d1) * 0.5f), new Vector3(t, h - dh, d1 - d0), mat, room.transform);
            return room;
        }

        /// <summary>
        /// The five lights. The sun keeps the arena's colour and strength at midday; the sky is a procedural one that follows
        /// it. In the room the open sky is shut out (ambient at 7%, reflections at 10%: no bounce is computed on a stage) and the
        /// light comes only through the doorway: the sun's shaft on the floor and a broad soft spot standing in for the sky
        /// seen through it. At night: no sun, a moonless sky, one 2700 K lamp 1.5 m off.
        /// </summary>
        static void SetLights(int k, Light sun, Light lamp, Light door, Material sky, GameObject room, GameObject blocker, Vector3 look, float floorY,
                          float ambient, float reflections, (float intensity, Color colour, float kelvin) sunLook)
        {
            room.SetActive(k == 3);
            blocker.SetActive(k == 2);
            lamp.enabled = k == 4;
            door.enabled = k == 3;
            sun.enabled = k != 4;
            RenderSettings.skybox = k == 4 ? null : sky;
            RenderSettings.ambientMode = k == 4 ? AmbientMode.Flat : AmbientMode.Skybox;
            RenderSettings.ambientLight = new Color(0.004f, 0.005f, 0.008f);
            RenderSettings.ambientIntensity = k == 3 ? 0.07f : ambient;
            RenderSettings.reflectionIntensity = k == 3 ? 0.1f : k == 4 ? 0.05f : reflections;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            sun.intensity = sunLook.intensity;
            if (sunLook.kelvin > 0) sun.colorTemperature = sunLook.kelvin;
            switch (k)
            {
                case 0: sun.transform.rotation = Quaternion.Euler(70f, 30f, 0f); break;
                case 1:
                    sun.transform.rotation = Quaternion.Euler(8f, 70f, 0f);
                    sun.intensity = sunLook.intensity * 0.55f;
                    if (sunLook.kelvin > 0) sun.colorTemperature = 2900f; else sun.color = new Color(1f, 0.62f, 0.35f);
                    break;
                case 2:
                    sun.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
                    blocker.transform.position = look - sun.transform.forward * 6f;
                    blocker.transform.rotation = Quaternion.LookRotation(sun.transform.forward);
                    break;
                case 3:
                    sun.transform.rotation = Quaternion.Euler(28f, 85f, 0f);
                    door.transform.position = new Vector3(-2.55f, floorY + 1.3f, -2.6f);
                    door.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.25f, 0.55f));
                    break;
                case 4:
                    lamp.transform.position = look + new Vector3(-0.8f, 0.6f, -1.4f);
                    break;
            }
        }

        /// <summary>The object's and the grey card's mean luminance in an unprocessed HDR render, and how much the object
        /// varies across itself (std / mean).</summary>
        static (float obj, float grey, float cv) MeasureStage(Color[] hdr, Color32[] ids)
        {
            double o = 0, oo = 0, g = 0; int on = 0, gn = 0;
            for (int i = 0; i < hdr.Length && i < ids.Length; i++)
            {
                int id = IdOf(ids[i]);
                float l = Lum(hdr[i]);
                if (id == 1) { o += l; oo += l * l; on++; }
                else if (id == 2) { g += l; gn++; }
            }
            if (on == 0) return (0, gn > 0 ? (float)(g / gn) : 0, 0);
            double mean = o / on, sd = Math.Sqrt(Math.Max(0, oo / on - mean * mean));
            return ((float)mean, gn > 0 ? (float)(g / gn) : 0, mean > 1e-6 ? (float)(sd / mean) : 0);
        }

        static void ReportStage(Item it, (float obj, float grey, float cv)[] s)
        {
            if (s[0].obj <= 0) { it.measured.Add("light test: the object was not in its own stage view"); return; }
            float Ratio(int k) => s[k].grey > 1e-5f ? s[k].obj / s[k].grey : 0f;
            var mi = it.mats.FirstOrDefault();
            float expect = mi != null ? mi.AlbedoMean / 0.18f : 0f;
            it.measured.Add("light test (object / 18% grey card): " + string.Join(", ", Enumerable.Range(0, 5).Select(k => $"{LightNames[k]} {Ratio(k):0.00}")) +
                            (expect > 0 ? $"; its albedo alone would give {expect:0.00}" : ""));
            it.measured.Add($"light test levels against midday: evening {s[1].obj / s[0].obj:0.00}, shade {s[2].obj / s[0].obj:0.00}, indoor {s[3].obj / s[0].obj:0.000}, night {s[4].obj / s[0].obj:0.0000}");
            it.measured.Add($"light test relief: brightness varies {s[0].cv:P0} across it at midday, {s[1].cv:P0} in the low evening sun");
            bool clear = it.cls is "glass" or "water" or "decal";
            // With no light at all, a real surface is black; what is left is its own emission.
            float dark = s[0].obj > 1e-6f ? s[5].obj / s[0].obj : 0f;
            it.measured.Add($"light test with every light off: {dark:P2} of its midday brightness");
            if (dark > 0.02f) it.measured.Add($"!!glows: with every light off it keeps {dark:P0} of its midday brightness (it emits light)");
            // In shade it should lose what the grey card in the same plane loses (a wall facing away from a high sun loses little).
            float objShade = s[2].obj / s[0].obj, cardShade = s[0].grey > 1e-6f ? s[2].grey / s[0].grey : objShade;
            // Reported, not rated: a surface already shaded at midday (a frame under its hood) keeps all of its brightness in shade.
            it.measured.Add($"light test shade: keeps {objShade:P0} of its midday brightness; the grey card beside it {cardShade:P0}");
            if (!clear && it.skyMaterial == null && s[1].cv < 0.06f) it.measured.Add($"!flat under raking light: its brightness varies only {s[1].cv:P0} across it in the low evening sun");
        }
    }
}
#endif
