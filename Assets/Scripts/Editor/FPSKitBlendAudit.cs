#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Every transition blend in the built controllers (the soldier's and the player's body), in seconds
    /// (smooth-animation brief, 2026-10-18: "no instant cuts, no long mushy blends"). A blend under
    /// <see cref="Cut"/> seconds between two different poses is a cut; over <see cref="Mush"/> seconds is
    /// mush, unless the transition is listed in <see cref="LongOnPurpose"/> with its reason. A normalized
    /// duration is turned into seconds on the source state's clip (a blend tree's longest clip).
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyBlends      (Build/BlendAudit/report.txt)
    /// </summary>
    public static class FPSKitBlendAudit
    {
        static readonly string[] Controllers =
        {
            "Assets/FPSKit_Generated/Soldier/Rig/Soldier.controller",
            "Assets/FPSKit_Generated/Player/PlayerBody.controller",
        };

        const float Cut = 0.07f, Mush = 0.45f;

        /// <summary>Long blends that are long on purpose ("source -> destination" as listed in the report).</summary>
        static readonly Dictionary<string, string> LongOnPurpose = new Dictionary<string, string>();

        public static void Run()
        {
            var lines = new List<string>();
            int cuts = 0, mush = 0, total = 0;
            foreach (var path in Controllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) { lines.Add($"{path}: missing"); cuts++; continue; }
                lines.Add($"{path}:");
                foreach (var layer in controller.layers)
                {
                    var rows = new List<string>();
                    void Check(string from, Motion fromMotion, AnimatorStateTransition t)
                    {
                        if (t == null) return;
                        total++;
                        string to = t.destinationState != null ? t.destinationState.name : t.isExit ? "(exit)" : t.destinationStateMachine != null ? t.destinationStateMachine.name : "?";
                        float seconds = t.hasFixedDuration ? t.duration : t.duration * Length(fromMotion);
                        bool same = t.destinationState != null && fromMotion != null && t.destinationState.motion == fromMotion;
                        string key = $"{from} -> {to}";
                        string verdict = "";
                        if (seconds < Cut && !same) { verdict = "  <-- CUT"; cuts++; }
                        else if (seconds > Mush) { if (LongOnPurpose.TryGetValue(key, out var why)) verdict = "  (long on purpose: " + why + ")"; else { verdict = "  <-- MUSH"; mush++; } }
                        rows.Add($"    {key,-58} {seconds,5:0.00} s{(t.hasFixedDuration ? "" : $" ({t.duration:0.00} of the clip)")}{verdict}");
                    }
                    foreach (var child in layer.stateMachine.states)
                        foreach (var t in child.state.transitions) Check(child.state.name, child.state.motion, t);
                    foreach (var t in layer.stateMachine.anyStateTransitions) Check("Any", null, t);
                    if (rows.Count == 0) continue;
                    lines.Add($"  layer {layer.name}:");
                    lines.AddRange(rows);
                }
            }

            // The blends made in code: every CrossFade with a literal duration in the game's scripts.
            lines.Add("Crossfades in code (Assets/Scripts/Runtime):");
            var call = new System.Text.RegularExpressions.Regex(@"CrossFade(InFixedTime)?\(([^;]*?),\s*([A-Za-z_][A-Za-z0-9_.]*|[0-9.]+f?)\s*,");
            foreach (var file in Directory.GetFiles("Assets/Scripts/Runtime", "*.cs"))
            {
                var text = File.ReadAllLines(file);
                for (int i = 0; i < text.Length; i++)
                {
                    var m = call.Match(text[i]);
                    if (!m.Success) continue;
                    total++;
                    string arg = m.Groups[3].Value;
                    float seconds = -1f;
                    if (!float.TryParse(arg.TrimEnd('f'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
                    {
                        // A named constant: read its value from the same file.
                        var def = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(arg)}\s*=\s*([0-9.]+)f");
                        foreach (var l in text) { var d = def.Match(l); if (d.Success) { float.TryParse(d.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds); break; } }
                    }
                    bool normalized = m.Groups[1].Value.Length == 0;
                    string verdict = seconds < 0f ? "  (not a literal: not judged)" : normalized ? "  (normalized: not judged)" : "";
                    if (seconds >= 0f && !normalized && seconds < Cut) { verdict = "  <-- CUT"; cuts++; }
                    else if (seconds > Mush && !normalized) { verdict = "  <-- MUSH"; mush++; }
                    lines.Add($"    {Path.GetFileName(file)}:{i + 1,-5} {arg,-14} {(seconds >= 0f ? seconds.ToString("0.00") + " s" : "")}{verdict}");
                }
            }

            bool ok = cuts == 0 && mush == 0;
            Directory.CreateDirectory("Build/BlendAudit");
            string report = (ok ? "blend audit passed" : "blend audit FAILED")
                            + $": {total} transitions; {cuts} cut(s) under {Cut} s, {mush} mushy over {Mush} s\n" + string.Join("\n", lines);
            File.WriteAllText("Build/BlendAudit/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report.Split('\n')[0]); else Debug.LogError("[FPSKitBatch] FAILED: " + report.Split('\n')[0]);
            FPSKitBatch.Exit(ok ? 0 : 1);
        }

        /// <summary>Seconds of a state's motion: a clip's length, a blend tree's longest clip.</summary>
        static float Length(Motion m)
        {
            if (m is AnimationClip c) return c.length;
            if (m is BlendTree tree) return tree.children.Select(ch => Length(ch.motion)).DefaultIfEmpty(1f).Max();
            return 1f;
        }
    }
}
#endif
