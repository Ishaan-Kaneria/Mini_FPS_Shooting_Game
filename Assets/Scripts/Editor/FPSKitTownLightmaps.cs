#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Baked light inside the town: the interior lighting pilot (Part B, 2026-10-31). The rooms were lit by probes 3 m
    /// apart (FPSKitTownGI), which cannot draw what his screenshots missed: a wall blown white beside a black room, a hard
    /// bright band across the majlis, nothing darker in a corner or under a shelf, a lamp glowing over a room it does not
    /// light. A lightmap can: the light through the door and the windows bounced round the room at 5 cm, ambient
    /// occlusion in every corner, and a shop's tubes lighting its shelves.
    ///
    /// The pilot is the two old houses' first rooms and the lit shop nearest the player's start (ViewProbe.PilotViews):
    ///   * their insides (the whole house's Interior_n, the shop block's ShopInside_x_y) get lightmap UVs and take their
    ///     indirect light from a lightmap; the sun stays real-time (Mixed, Baked Indirect), so their direct light and
    ///     shadows are as before and only what it bounces is baked;
    ///   * the furniture in them shades the bake (Contribute GI) but stays on the probes (no lightmap of its own);
    ///   * by day the houses' lamps are off (FPSKitTownInterior); a trading shop's tubes are on, as baked lights over the
    ///     fittings (FPSKitTownShopfront.ShopTubes), so they cost nothing to draw.
    /// Run inside the classic probe bake (FPSKitTownProbes.BakeTownProbes), which is one bake for both. The adaptive probe
    /// volume bake after it leaves anything with a lightmap on it (FPSKitTownGI).
    /// </summary>
    public static class FPSKitTownLightmaps
    {
        /// <summary>Lightmap texels a metre on the pilot's surfaces: 5 cm, a skirting's height; a corner's darkening reads.</summary>
        public const float TexelsPerMetre = 20f;

        /// <summary>A shop tube's brightness as a baked area light (a 6 cm by 1.2 m strip under the tube).</summary>
        const float TubeIntensity = 8f;

        /// <summary>A cool white, as the tubes in the reference shops (Build/Realism/reference_interiors/shop_01-03).</summary>
        static readonly Color TubeColour = new Color(0.94f, 0.97f, 1f);

        public sealed class Plan
        {
            public readonly List<MeshRenderer> lit = new List<MeshRenderer>();
            public int shading, tubes;
            public float area;
        }

        /// <summary>
        /// Chooses the pilot's spaces in the open (saved) desert scene and readies them for the bake: lightmap UVs, Receive GI
        /// Lightmaps, the furniture in them Contribute GI, the tubes as baked lights, and the marks the views use. Returns
        /// null (and bakes nothing new) when there is no player or nothing to light. Idempotent: a re-bake replaces its lights.
        /// </summary>
        public static Plan Prepare(LightingSettings settings)
        {
            var old = GameObject.Find("LightmapPilot");
            if (old != null) Object.DestroyImmediate(old);
            var player = GameObject.FindWithTag("Player");
            var arena = GameObject.Find("Arena");
            if (player == null || arena == null) return null;

            var views = ViewProbe.PilotViews(player.transform.position);
            var root = new GameObject("LightmapPilot").transform;
            root.SetParent(arena.transform, false);
            var plan = new Plan();
            var houses = GameObject.Find("Arena/Buildings/HouseInteriors");
            var shops = GameObject.Find("Arena/Buildings/ShopInteriors");
            foreach (var (name, pos, target) in views)
            {
                bool shop = name == "shop";
                var group = shop ? shops : houses;
                // The inside the view's room is in: the smallest whose bounds hold it (the shop's middle, the room's mark).
                var inside = group == null ? null : group.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => !r.name.StartsWith("Detail_") && r.bounds.Contains(shop ? target : pos))
                    .OrderBy(r => r.bounds.size.x * r.bounds.size.y * r.bounds.size.z).FirstOrDefault();
                if (inside == null) { Debug.LogWarning($"[FPSKit] lightmap pilot: no inside found for {name} at {pos}"); continue; }

                var mark = new GameObject(name).transform;
                mark.SetParent(root, false);
                mark.position = pos;
                var look = new GameObject("Look").transform;
                look.SetParent(mark, false);
                look.position = target;
                if (plan.lit.Contains(inside)) continue;
                plan.lit.Add(inside);
                plan.area += Lightmapped(inside);

                // The furniture in it shades the bake, lit itself by the probes as before.
                var space = inside.bounds;
                foreach (var r in arena.transform.Find("Buildings/BuildingProps")?.GetComponentsInChildren<MeshRenderer>(true) ?? new MeshRenderer[0])
                {
                    if (!r.enabled || !space.Contains(r.bounds.center)) continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
                    r.receiveGI = ReceiveGI.LightProbes;
                    plan.shading++;
                }
                // A shop block's trading shops: their tubes, lit.
                if (shop && GameObject.Find("ShopRooms") is GameObject marks)
                    foreach (Transform s in marks.transform)
                    {
                        if (s.name.EndsWith(" dark") || !s.name.StartsWith("Shop ") || !space.Contains(s.position)) continue;
                        foreach (var (at, along) in FPSKitSceneBuilder.ShopTubes(s.position, s.right, s.forward, s.localScale.x, s.localScale.y))
                            Tube(root, at, along);
                        plan.tubes++;
                    }
            }
            if (plan.lit.Count == 0) { Object.DestroyImmediate(root.gameObject); return null; }

            settings.lightmapResolution = TexelsPerMetre;
            settings.lightmapPadding = 4;
            settings.lightmapMaxSize = 1024;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;   // the walls' normal maps answer the bounce's direction
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.aoExponentIndirect = 1f;
            settings.aoExponentDirect = 0f;      // the sun is real-time: its shadows are its own
            return plan;
        }

        /// <summary>The inside takes a lightmap: lightmap UVs made for it (its surface area back, for the report).</summary>
        static float Lightmapped(MeshRenderer r)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            float area = 0f;
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            if (mesh.uv2 == null || mesh.uv2.Length == 0)
            {
                UnwrapParam.SetDefaults(out var p);
                // Two texels between islands at the size the inside will have in its lightmap.
                p.packMargin = Mathf.Clamp(2f / (Mathf.Sqrt(area) * TexelsPerMetre), 0.001f, 0.03f);
                Unwrapping.GenerateSecondaryUVSet(mesh, p);
            }
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
            r.receiveGI = ReceiveGI.Lightmaps;
            r.scaleInLightmap = 1f;
            return area;
        }

        /// <summary>A tube's light: a strip as long as the tube, just under it, facing down, baked only.</summary>
        static void Tube(Transform root, Vector3 at, Vector3 along)
        {
            var go = new GameObject("ShopTube");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at - Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.down, along));
            var l = go.AddComponent<Light>();
            l.type = LightType.Rectangle;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.areaSize = new Vector2(0.06f, 1.2f);     // across, along the tube
            l.color = TubeColour;
            l.intensity = TubeIntensity;
            l.shadows = LightShadows.Soft;
        }

        /// <summary>After the bake: the lightmaps made, their size and memory, and what they cover.</summary>
        public static string Report(Plan plan)
        {
            if (plan == null) return "no lightmap pilot";
            var maps = LightmapSettings.lightmaps;
            long bytes = 0;
            var sizes = new List<string>();
            foreach (var m in maps)
                foreach (var tex in new Texture2D[] { m.lightmapColor, m.lightmapDir, m.shadowMask })
                {
                    if (tex == null) continue;
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                    sizes.Add($"{tex.width}x{tex.height} {tex.format}");
                }
            int got = plan.lit.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
            return $"lightmap pilot: {got} of {plan.lit.Count} insides lightmapped ({string.Join(", ", plan.lit.Select(r => r.name))}), {plan.area:0} m² at {TexelsPerMetre:0} texels/m, " +
                   $"{plan.shading} pieces of furniture shading, {plan.tubes} lit shops' tubes; {maps.Length} lightmaps ({string.Join("; ", sizes)}), {bytes / 1048576f:0.0} MB";
        }
    }
}
#endif
