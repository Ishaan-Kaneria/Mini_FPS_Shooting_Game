#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The face damage's last step: the five shapes Blender put on the soldier's head (Tools/blender/face_damage_keys.py
    /// writes Assets/Models/Enemy/SK_Military_Head1_FaceDamage.fbx) copied onto the game's own import of that head, saved as
    /// a mesh FacialDamageHandler swaps in at runtime (Docs/DESIGN_NOTES.md "Pain, face damage and the head grab").
    ///
    /// Copied, not swapped for Blender's mesh: the game's head keeps its own vertices, skin weights, bones and bind poses,
    /// so the round trip through Blender cannot touch the rig. The two imports are the same head in two files written by
    /// two programs, so their axes and scale may differ: every signed axis order is tried, scaled and centred on the
    /// game's head, and the one whose points land nearest wins; each game vertex then takes the shape offsets of its
    /// nearest Blender vertex, turned and scaled the same way. The match is reported (it should be a fraction of a mm).
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.BakeFaceDamage
    /// </summary>
    public static class FPSKitFaceDamage
    {
        const string SourceFbx = "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx";
        const string ShapesFbx = "Assets/Models/Enemy/SK_Military_Head1_FaceDamage.fbx";
        const string OutFolder = "Assets/FPSKit_Generated/Resources/FaceDamage";
        const string OutPath = OutFolder + "/SK_Military_Head1.asset";

        public static string Bake()
        {
            var importer = AssetImporter.GetAtPath(ShapesFbx) as ModelImporter;
            if (importer == null) throw new Exception($"{ShapesFbx} is missing: run Tools/blender/face_damage_keys.py");
            if (!importer.importBlendShapes || importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.animationType != ModelImporterAnimationType.None)
            {
                importer.importBlendShapes = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }

            Mesh original = Load(SourceFbx), shaped = Load(ShapesFbx);
            if (original == null) throw new Exception($"no {FacialDamageHandler.HeadMeshName} in {SourceFbx}");
            if (shaped == null) throw new Exception($"no {FacialDamageHandler.HeadMeshName} in {ShapesFbx}");
            if (shaped.blendShapeCount < 5) throw new Exception($"{ShapesFbx} has {shaped.blendShapeCount} shapes, not 5");

            Vector3[] a = original.vertices, b = shaped.vertices;
            var fit = Fit(a, b);

            // Each game vertex's nearest Blender vertex, in the fitted space.
            var fitted = b.Select(p => fit.Apply(p)).ToArray();
            var nearest = new int[a.Length];
            float worst = 0f, sum = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float best = float.MaxValue; int bi = 0;
                for (int j = 0; j < fitted.Length; j++)
                {
                    float d = (fitted[j] - a[i]).sqrMagnitude;
                    if (d < best) { best = d; bi = j; }
                }
                nearest[i] = bi;
                float m = Mathf.Sqrt(best);
                worst = Mathf.Max(worst, m); sum += m;
            }

            var mesh = Object.Instantiate(original);
            mesh.name = FacialDamageHandler.HeadMeshName + "_FaceDamage";
            mesh.ClearBlendShapes();
            var dv = new Vector3[b.Length]; var dn = new Vector3[b.Length]; var dt = new Vector3[b.Length];
            var report = new List<string>();
            for (int s = 0; s < shaped.blendShapeCount; s++)
            {
                string name = shaped.GetBlendShapeName(s);
                int frame = shaped.GetBlendShapeFrameCount(s) - 1;
                shaped.GetBlendShapeFrameVertices(s, frame, dv, dn, dt);
                var va = new Vector3[a.Length]; var na = new Vector3[a.Length];
                float most = 0f;
                for (int i = 0; i < a.Length; i++)
                {
                    va[i] = fit.Turn(dv[nearest[i]]) * fit.scale;
                    na[i] = fit.Turn(dn[nearest[i]]);
                    most = Mathf.Max(most, va[i].magnitude);
                }
                mesh.AddBlendShapeFrame(name, 100f, va, na, null);
                report.Add($"{name} {most * 1000f:0.0} mm");
            }

            Directory.CreateDirectory(OutFolder);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, OutPath);
            EditorUtility.SetDirty(mesh);

            // The bruise material is the blood library's: written there too.
            FPSKitGore.GetOrCreateLibrary();
            AssetDatabase.SaveAssets();

            string line = $"face damage baked: {a.Length} game vertices onto {b.Length} Blender ones (axes {fit.name}, scale {fit.scale:0.0000}), " +
                          $"match mean {sum / a.Length * 1000f:0.00} mm, worst {worst * 1000f:0.00} mm; shapes {string.Join(", ", report)} -> {OutPath}";
            if (worst > 0.002f) throw new Exception("the two heads do not line up: " + line);
            return line;
        }

        static Mesh Load(string path)
            => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name.StartsWith(FacialDamageHandler.HeadMeshName));

        struct Fitted
        {
            public int[] order; public float[] sign; public float scale; public Vector3 offset; public string name;
            public Vector3 Turn(Vector3 v) => new Vector3(v[order[0]] * sign[0], v[order[1]] * sign[1], v[order[2]] * sign[2]);
            public Vector3 Apply(Vector3 p) => Turn(p) * scale + offset;
        }

        /// <summary>The signed axis order, scale and offset that lay <paramref name="b"/> on <paramref name="a"/>.</summary>
        static Fitted Fit(Vector3[] a, Vector3[] b)
        {
            var ba = Box(a);
            var sample = Enumerable.Range(0, 160).Select(i => a[i * a.Length / 160]).ToArray();
            var orders = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            Fitted best = default; float bestError = float.MaxValue;
            foreach (var order in orders)
                for (int signs = 0; signs < 8; signs++)
                {
                    var f = new Fitted { order = order, sign = new[] { (signs & 1) != 0 ? -1f : 1f, (signs & 2) != 0 ? -1f : 1f, (signs & 4) != 0 ? -1f : 1f } };
                    var turned = b.Select(f.Turn).ToArray();
                    var bb = Box(turned);
                    f.scale = ba.size.magnitude / Mathf.Max(1e-6f, bb.size.magnitude);
                    f.offset = ba.center - bb.center * f.scale;
                    float error = 0f;
                    foreach (var p in sample)
                    {
                        float near = float.MaxValue;
                        foreach (var q in turned) near = Mathf.Min(near, (q * f.scale + f.offset - p).sqrMagnitude);
                        error += near;
                        if (error >= bestError) break;
                    }
                    if (error < bestError)
                    {
                        bestError = error;
                        f.name = $"{"+-"[f.sign[0] < 0 ? 1 : 0]}{"xyz"[order[0]]}{"+-"[f.sign[1] < 0 ? 1 : 0]}{"xyz"[order[1]]}{"+-"[f.sign[2] < 0 ? 1 : 0]}{"xyz"[order[2]]}";
                        best = f;
                    }
                }
            return best;
        }

        static Bounds Box(Vector3[] p)
        {
            var b = new Bounds(p[0], Vector3.zero);
            foreach (var v in p) b.Encapsulate(v);
            return b;
        }
    }
}
#endif
