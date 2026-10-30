using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// The colour grade done once against the grade as it was, done twice (2026-10-30: post-processing ran for both the
/// world's camera and the arms', and the look he approved came from that). Does nothing unless the player is started with
/// <c>-fpskitgrade &lt;dir&gt;</c>. At three street views on WalkRoute and two old houses' rooms it draws the old double
/// grade, then searches the single grade's contrast, saturation and exposure for the nearest match (mean squared error
/// over the five views), and writes each view as it is now, halfway, matched and as it was, without the HUD, plus a
/// report of the values. Holds no static state.
/// </summary>
public class GradeProbe : MonoBehaviour
{
    string _dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitgrade");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("GradeProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<GradeProbe>()._dir = args[i + 1];
    }

    struct Grade { public float exposure, contrast, saturation, vignette; public override string ToString() => $"exposure {exposure:+0.00;-0.00}, contrast {contrast:0}, saturation {saturation:0}, vignette {vignette:0.00}"; }

    IEnumerator Start()
    {
        Directory.CreateDirectory(_dir);
        var log = new StringBuilder();
        if (SceneManager.GetActiveScene().name != "DesertOutpost") { SceneManager.LoadScene("DesertOutpost"); yield return new WaitForSecondsRealtime(1f); }
        float until = Time.realtimeSinceStartup + 60f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < until && (level == null || level.IsBriefing || !level.IsRunning)) { level = FindAnyObjectByType<LevelManager>(); yield return null; }
        yield return new WaitForSecondsRealtime(3f);

        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        var cc = player != null ? player.GetComponent<CharacterController>() : null;
        var cam = Camera.main;
        if (player == null || motor == null || cam == null) { File.WriteAllText(Path.Combine(_dir, "report.txt"), "no player or camera"); Application.Quit(1); yield break; }
        var camData = cam.GetUniversalAdditionalCameraData();
        var eye = FindAnyObjectByType<EyeAdaptation>();
        // The scene's own grade (not the eye's or the hurt effect's volume), and the hurt effect's, to put back as it was.
        ColorAdjustments ca = null; Vignette vg = null;
        foreach (var v in FindObjectsByType<Volume>().Where(v => v.isGlobal && v.sharedProfile != null).OrderByDescending(v => v.priority))
        {
            if (v.gameObject.name == "EyeAdaptationVolume" || v.gameObject.name == "PlayerCondition") continue;
            if (ca == null) v.sharedProfile.TryGet(out ca);
            if (vg == null) v.sharedProfile.TryGet(out vg);
        }
        var hurtVolume = FindObjectsByType<Volume>().FirstOrDefault(v => v.gameObject.name == "PlayerCondition");
        ColorAdjustments hurtColour = null; Vignette hurtEdge = null;
        if (hurtVolume != null) { hurtVolume.sharedProfile.TryGet(out hurtColour); hurtVolume.sharedProfile.TryGet(out hurtEdge); }
        if (ca == null) { File.WriteAllText(Path.Combine(_dir, "report.txt"), "no colour adjustments in the scene's grade"); Application.Quit(1); yield break; }
        ca.postExposure.overrideState = ca.contrast.overrideState = ca.saturation.overrideState = true;
        if (vg != null) vg.intensity.overrideState = true;
        var now = new Grade { exposure = ca.postExposure.value, contrast = ca.contrast.value, saturation = ca.saturation.value, vignette = vg != null ? vg.intensity.value : 0f };
        // As it was (before 2026-10-30): the hurt effect's volume overrode the whole colour adjustment and the vignette
        // with zeros at full weight, the eye took exposure 0 for the scene's, and both cameras graded the frame.
        void OldOverrides(bool on)
        {
            if (hurtColour != null)
            {
                hurtColour.postExposure.overrideState = hurtColour.contrast.overrideState = hurtColour.saturation.overrideState = hurtColour.hueShift.overrideState = hurtColour.colorFilter.overrideState = on;
                hurtColour.postExposure.value = 0f; hurtColour.contrast.value = 0f; hurtColour.saturation.value = 0f; hurtColour.hueShift.value = 0f; hurtColour.colorFilter.value = Color.white;
            }
            if (hurtEdge != null) { hurtEdge.intensity.overrideState = on; hurtEdge.intensity.value = 0f; }
        }
        foreach (var c in FindObjectsByType<Canvas>()) c.enabled = false;   // the HUD is the same in every version

        // The views: three on the walk, two rooms of the old houses nearest its start (their marks stand at the way in,
        // their Look inside).
        var line = WalkRoute.Line(player.transform.position, WalkRoute.Desert, new List<string>());
        var views = new List<(string name, Vector3 pos, Vector3 target)>();
        foreach (float at in new[] { 40f, 200f, 420f })
        {
            float along = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float len = Vector3.Distance(line[i - 1], line[i]);
                if (along + len >= at) { var p = Vector3.Lerp(line[i - 1], line[i], (at - along) / len); views.Add(($"street_{at:000}m", p, p + (line[i] - line[i - 1]).normalized * 20f)); break; }
                along += len;
            }
        }
        var rooms = GameObject.Find("HouseRooms");
        if (rooms != null)
        {
            var start = player.transform.position;
            foreach (var house in rooms.transform.Cast<Transform>().Where(h => h.name == "OldHouse").OrderBy(h => (h.position - start).sqrMagnitude).Take(6))
            {
                var room = house.Cast<Transform>().FirstOrDefault(r => r.name.StartsWith("Room ") && r.Find("Look") != null);
                if (room == null) continue;
                views.Add(($"room_{views.Count - 2}", room.position, room.Find("Look").position));
                if (views.Count >= 5) break;
            }
        }

        const int W = 192, H = 108;
        var candidates = new List<Grade>();
        foreach (float e in new[] { -0.45f, -0.3f, -0.15f, 0f, 0.15f })
            foreach (float c in new[] { 0f, 10f, 20f, 30f, 45f })
                foreach (float s in new[] { -10f, 0f, 10f, 20f, 35f })
                    foreach (float vgn in new[] { 0f, now.vignette })
                        candidates.Add(new Grade { exposure = now.exposure + e, contrast = c, saturation = s, vignette = vgn });
        var error = new double[candidates.Count];

        IEnumerator Place((string name, Vector3 pos, Vector3 target) v)
        {
            if (cc) cc.enabled = false;
            player.transform.position = v.pos + Vector3.up * 0.05f;
            if (cc) cc.enabled = true;
            for (int k = 0; k < 150; k++)   // three seconds and more: the eye adapts indoors over 1.4 s
            {
                motor.TurnTowards(v.target + Vector3.up * 1.5f, 0.02f);
                if (health) health.Heal(1000f);
                if (level) level.HoldClockForBenchmark(120f);
                yield return null;
            }
        }
        void Apply(Grade g)
        {
            ca.postExposure.value = g.exposure; ca.contrast.value = g.contrast; ca.saturation.value = g.saturation;
            if (vg != null) vg.intensity.value = g.vignette;
            if (eye) { eye.enabled = false; eye.enabled = true; }   // it reads the grade's exposure as its base when enabled
        }
        Color32[] grabbed = null;
        IEnumerator Grab(bool full, string file)
        {
            for (int k = 0; k < 3; k++) { if (health) health.Heal(1000f); yield return null; }
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (full) File.WriteAllBytes(file, tex.EncodeToPNG());
            var small = new RenderTexture(W, H, 0);
            Graphics.Blit(tex, small);
            var prev = RenderTexture.active; RenderTexture.active = small;
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
            RenderTexture.active = prev;
            grabbed = read.GetPixels32();
            Destroy(tex); Destroy(read); small.Release(); Destroy(small);
        }

        for (int v = 0; v < views.Count; v++)
        {
            yield return Place(views[v]);
            // As it was: the hurt effect's zeros over the grade, the eye's base 0, graded by the world's camera too.
            OldOverrides(true);
            camData.renderPostProcessing = true;
            Apply(new Grade { exposure = 0f, contrast = now.contrast, saturation = now.saturation, vignette = now.vignette });
            yield return Grab(true, Path.Combine(_dir, $"{views[v].name}_was_double.png"));
            var refPix = grabbed;
            OldOverrides(false);
            camData.renderPostProcessing = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Apply(candidates[i]);
                yield return Grab(false, null);
                double sum = 0;
                for (int p = 0; p < refPix.Length; p++)
                {
                    int dr = refPix[p].r - grabbed[p].r, dg = refPix[p].g - grabbed[p].g, db = refPix[p].b - grabbed[p].b;
                    sum += dr * dr + dg * dg + db * db;
                }
                error[i] += sum / refPix.Length;
            }
        }
        int best = Enumerable.Range(0, candidates.Count).OrderBy(i => error[i]).First();
        var matched = candidates[best];
        var half = new Grade { exposure = (now.exposure + matched.exposure) * 0.5f, contrast = (now.contrast + matched.contrast) * 0.5f, saturation = (now.saturation + matched.saturation) * 0.5f, vignette = (now.vignette + matched.vignette) * 0.5f };
        int nowIndex = Enumerable.Range(0, candidates.Count).OrderBy(i => Mathf.Abs(candidates[i].contrast - now.contrast) + Mathf.Abs(candidates[i].saturation - now.saturation) + 10f * Mathf.Abs(candidates[i].exposure - now.exposure) + 10f * Mathf.Abs(candidates[i].vignette - now.vignette)).First();
        log.AppendLine($"grade now (single): {now}");
        log.AppendLine($"matched to the old double grade: {matched} (error {error[best] / views.Count:0} against {error[nowIndex] / views.Count:0} for the nearest candidate to now)");
        log.AppendLine($"halfway: {half}");
        log.AppendLine($"views: {string.Join(", ", views.Select(x => x.name))}");

        for (int v = 0; v < views.Count; v++)
        {
            yield return Place(views[v]);
            foreach (var (label, g) in new[] { ("a_now", now), ("b_half", half), ("c_matched", matched) })
            {
                Apply(g);
                yield return Grab(true, Path.Combine(_dir, $"{views[v].name}_{label}.png"));
            }
        }
        Apply(now);
        File.WriteAllText(Path.Combine(_dir, "report.txt"), log.ToString());
        Application.Quit();
    }
}
