#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What keyframe reduction costs the enemy's Mixamo clips (smooth-animation brief, 2026-10-18: "no or
    /// minimal keyframe reduction, so fine motion isn't lost"). Reimports every clip under
    /// Assets/Animations/Mixamo three ways -- no compression, Unity's default reduction (0.5 degrees) and
    /// the importer's own minimal setting (<see cref="FPSKitMixamoImport.RotationError"/>) -- and for a set
    /// of clips with fine motion in them (a breathing idle, an aimed idle, a walk, a reload, a get-up)
    /// samples every humanoid bone at 60 Hz on the Survivalist (AnimationMode) and measures how
    /// far each reduced clip strays from the uncompressed one. Memory is the editor's runtime size of the
    /// clips. Leaves the clips imported with the importer's own setting.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyClipCompression      (Build/ClipCompression/report.txt)
    /// </summary>
    public static class FPSKitClipCompression
    {
        const string Folder = "Build/ClipCompression";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        static readonly string[] Fine = { "Idle/Idle_Breathing", "Rifle/Rifle_Idle_Aim", "Rifle/Move_Walk_F", "Actions/Reload_Stand", "GetUp/GetUp_Back" };

        /// <summary>The largest stray allowed with the importer's own setting, degrees, on any bone, fingers included.</summary>
        const float MaxStray = 0.6f;

        public static void Run()
        {
            var lines = new List<string>();
            bool ok = true;
            GameObject body = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab));
                var animator = body.GetComponentInChildren<Animator>();
                var bones = new List<(HumanBodyBones id, Transform t)>();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var t = animator.GetBoneTransform((HumanBodyBones)i);
                    if (t != null) bones.Add(((HumanBodyBones)i, t));
                }

                var settings = new (string name, ModelImporterAnimationCompression c, float error)[]
                {
                    ("none", ModelImporterAnimationCompression.Off, 0f),
                    ("Unity default, 0.5 deg", ModelImporterAnimationCompression.KeyframeReduction, 0.5f),
                    ($"minimal, {FPSKitMixamoImport.RotationError} deg (the importer's own)", ModelImporterAnimationCompression.KeyframeReduction, FPSKitMixamoImport.RotationError),
                };
                var samples = new Dictionary<(int, string), List<Quaternion[]>>();
                var memory = new long[settings.Length];
                var paths = Directory.GetFiles(FPSKitMixamoImport.Root, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).ToArray();

                for (int s = 0; s < settings.Length; s++)
                {
                    FPSKitMixamoImport.MeasureWith = settings[s].c;
                    FPSKitMixamoImport.MeasureError = settings[s].error;
                    AssetDatabase.StartAssetEditing();
                    try { foreach (var p in paths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
                    finally { AssetDatabase.StopAssetEditing(); }

                    foreach (var p in paths)
                        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                            if (!clip.name.StartsWith("__preview")) memory[s] += Profiler.GetRuntimeMemorySizeLong(clip);

                    foreach (var name in Fine)
                    {
                        var clip = AssetDatabase.LoadAllAssetsAtPath($"{FPSKitMixamoImport.Root}{name}.fbx").OfType<AnimationClip>()
                                                .First(c => !c.name.StartsWith("__preview") && !c.name.EndsWith("_Mirror"));
                        samples[(s, name)] = Sample(animator, clip, bones.Select(b => b.t).ToArray());
                    }
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }

                lines.Add($"all {paths.Length} files under {FPSKitMixamoImport.Root}, clip memory (editor runtime size):");
                for (int s = 0; s < settings.Length; s++)
                    lines.Add($"  {settings[s].name,-44} {memory[s] / 1048576.0,7:0.00} MB");

                // Sanity: the sampling moves the bones (a stray of zero means nothing if nothing moved).
                foreach (var name in Fine)
                {
                    var a = samples[(0, name)];
                    float moved = 0f;
                    for (int f = 1; f < a.Count; f++) for (int i = 0; i < bones.Count; i++) moved = Mathf.Max(moved, Quaternion.Angle(a[0][i], a[f][i]));
                    if (moved < 1f) { ok = false; lines.Add($"  {name}: sampling moved no bone more than {moved:0.00} deg -- the measure is broken"); }
                    else lines.Add($"  {name}: {a.Count} frames, the most a bone turns through the clip {moved:0.0} deg");
                }
                lines.Add("stray from the uncompressed clip, every humanoid bone at 60 Hz (degrees: mean / 99th percentile / worst, and the worst finger):");
                for (int s = 1; s < settings.Length; s++)
                {
                    lines.Add($"  {settings[s].name}:");
                    foreach (var name in Fine)
                    {
                        var a = samples[(0, name)]; var b = samples[(s, name)];
                        var all = new List<float>();
                        float fingers = 0f;
                        for (int f = 0; f < Mathf.Min(a.Count, b.Count); f++)
                            for (int i = 0; i < bones.Count; i++)
                            {
                                float d = Quaternion.Angle(a[f][i], b[f][i]);
                                all.Add(d);
                                if (bones[i].id >= HumanBodyBones.LeftThumbProximal && bones[i].id <= HumanBodyBones.RightLittleDistal) fingers = Mathf.Max(fingers, d);
                            }
                        all.Sort();
                        float worst = all.Count > 0 ? all[all.Count - 1] : 0f;
                        if (s == settings.Length - 1 && worst > MaxStray) ok = false;
                        lines.Add($"    {name,-24} {all.Average(),6:0.000} / {all[(int)(0.99f * (all.Count - 1))],6:0.000} / {worst,6:0.000}   fingers {fingers,6:0.000}");
                    }
                }
            }
            catch (Exception e) { ok = false; lines.Add("  error: " + e.Message); Debug.LogException(e); }
            finally
            {
                // Back to the importer's own setting, whatever happened.
                FPSKitMixamoImport.MeasureWith = null;
                AssetDatabase.StartAssetEditing();
                try { foreach (var p in Directory.GetFiles(FPSKitMixamoImport.Root, "*.fbx", SearchOption.AllDirectories)) AssetDatabase.ImportAsset(p.Replace('\\', '/'), ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
                if (body != null) Object.DestroyImmediate(body);
            }

            Directory.CreateDirectory(Folder);
            string report = (ok ? "clip compression check passed" : "clip compression check FAILED")
                            + $" (the importer's own setting may stray at most {MaxStray} deg on any bone)\n" + string.Join("\n", lines);
            File.WriteAllText($"{Folder}/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);
            FPSKitBatch.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Every bone's local rotation at 60 Hz through the clip, played alone on the body: through the
        /// editor's AnimationMode, which samples a Humanoid clip onto a body that is not playing (a manual
        /// PlayableGraph evaluated outside play mode wrote nothing to the bones).
        /// </summary>
        static List<Quaternion[]> Sample(Animator animator, AnimationClip clip, Transform[] bones)
        {
            var frames = new List<Quaternion[]>();
            AnimationMode.StartAnimationMode();
            try
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(clip.length * 60f));
                for (int f = 0; f <= n; f++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, f / 60f);
                    AnimationMode.EndSampling();
                    frames.Add(bones.Select(b => b.localRotation).ToArray());
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
            return frames;
        }
    }
}
#endif
