using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The same views every time, for a before and after in the game itself: started with <c>-fpskitviews &lt;dir&gt;</c>, it
/// stands the player at three street views on WalkRoute (40, 200 and 420 m), in the two old houses' rooms nearest the
/// player's start (GradeProbe's room views), and in front of the lit shop nearest the start, lets the eye adapt, and
/// writes each screen without the HUD (view_*.png), then quits. <c>-fpskitquality Low|Medium|High</c> picks the tier for the
/// run (else the player's own). The rooms and the shop are the interior lighting pilot's
/// (2026-10-30). Holds no static state.
/// </summary>
public class ViewProbe : MonoBehaviour
{
    string _dir, _tier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitviews");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("ViewProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<ViewProbe>();
        probe._dir = args[i + 1];
        int q = Array.IndexOf(args, "-fpskitquality");      // as WalkProbe: set as the player's own, put back after
        if (q >= 0 && q + 1 < args.Length) probe._tier = args[q + 1];
    }

    /// <summary>The pilot's rooms and shop: the two old houses' first rooms and the lit shop nearest <paramref name="start"/>
    /// (the player's start). The lightmap bake (FPSKitTownLightmaps) chooses them with this, from the scene's player, and
    /// leaves what it chose as marks ("LightmapPilot": room_1, room_2, room_3, shop, each with its "Look", and a second
    /// view of each room facing its openings, "*_openings"), which are used when
    /// they are there, so the views are of what was baked wherever the level starts the player.</summary>
    public static List<(string name, Vector3 pos, Vector3 target)> PilotViews(Vector3 start)
    {
        var views = new List<(string, Vector3, Vector3)>();
        var baked = GameObject.Find("LightmapPilot");
        if (baked != null)
        {
            foreach (Transform v in baked.transform)
                if (v.Find("Look") is Transform look) views.Add((v.name, v.position, look.position));
            if (views.Count > 0) return views;
        }
        var rooms = GameObject.Find("HouseRooms");
        var used = new List<Transform>();
        if (rooms != null)
            foreach (var house in rooms.transform.Cast<Transform>().Where(h => h.name == "OldHouse").OrderBy(h => (h.position - start).sqrMagnitude))
            {
                var room = house.Cast<Transform>().FirstOrDefault(r => r.name.StartsWith("Room ") && r.Find("Look") != null);
                if (room == null) continue;
                views.Add(($"room_{views.Count + 1}", room.position, room.Find("Look").position));
                used.Add(house);
                if (views.Count >= 2) break;
            }
        // A third: the front room of the house nearest the start whose doorway the sun comes in at (his ask, 2026-10-31),
        // seen from its way in.
        var sun = FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
        if (rooms != null && sun != null)
        {
            Vector3 toSun = -sun.transform.forward;
            bool Sunlit(Vector3 p) => !Physics.Raycast(p, toSun, 300f, ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast"), QueryTriggerInteraction.Ignore);
            foreach (var house in rooms.transform.Cast<Transform>().Where(h => h.Find("Door") != null && !used.Contains(h))
                                       .OrderBy(h => (h.Find("Door").position - start).sqrMagnitude))
            {
                var door = house.Find("Door").position;
                var room = house.Cast<Transform>().Where(r => r.name.StartsWith("Room ") && r.name.EndsWith(" f0") && r.Find("Look") != null)
                                .OrderBy(r => (r.position - door).sqrMagnitude).FirstOrDefault();
                if (room == null || !(Sunlit(room.position) || Sunlit(Vector3.Lerp(door, room.position, 0.75f) + Vector3.up * 0.1f))) continue;
                views.Add(("room_3", room.position, room.Find("Look").position));
                break;
            }
        }
        var shops = GameObject.Find("ShopRooms");
        if (shops != null)
        {
            // A lit shop's mark is its inside as a unit box facing the street (FPSKitTownShopfront): stand 2.5 m out
            // from its front, at eye height, and look in.
            var shop = shops.transform.Cast<Transform>().Where(s => !s.name.EndsWith(" dark") && s.name.StartsWith("Shop "))
                            .OrderBy(s => (s.position - start).sqrMagnitude).FirstOrDefault();
            if (shop != null)
            {
                var front = shop.position + shop.forward * (shop.localScale.z * 0.5f + 2.5f);
                front.y = shop.position.y - shop.localScale.y * 0.5f;
                views.Add(("shop", front, shop.position));
            }
        }
        return views;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(_dir);
        int savedTier = GameSettings.QualityTier;
        if (_tier != null && Enum.TryParse(_tier, true, out GameSettings.Quality wanted)) GameSettings.QualityTier = (int)wanted;
        if (SceneManager.GetActiveScene().name != "DesertOutpost") { SceneManager.LoadScene("DesertOutpost"); yield return new WaitForSecondsRealtime(1f); }
        float until = Time.realtimeSinceStartup + 60f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < until && (level == null || level.IsBriefing || !level.IsRunning)) { level = FindAnyObjectByType<LevelManager>(); yield return null; }
        yield return new WaitForSecondsRealtime(3f);
        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        var cc = player != null ? player.GetComponent<CharacterController>() : null;
        if (player == null || motor == null) { File.WriteAllText(Path.Combine(_dir, "views.txt"), "no player"); GameSettings.QualityTier = savedTier; Application.Quit(1); yield break; }
        foreach (var c in FindObjectsByType<Canvas>()) c.enabled = false;
        // Diagnosis: -fpskitviewsoff indoor (the rooms' and shops' reflection probes), refl (every reflection), and the views
        // name the folder they go in, so a set can be taken with a part of the light taken away.
        var args = Environment.GetCommandLineArgs();
        int off = Array.IndexOf(args, "-fpskitviewsoff");
        var without = off >= 0 && off + 1 < args.Length ? args[off + 1].Split(',') : new string[0];
        if (without.Contains("indoor") || without.Contains("refl"))
            foreach (var indoor in FindObjectsByType<IndoorReflections>(FindObjectsInactive.Include))
            {
                indoor.enabled = false;
                foreach (var rp in indoor.GetComponentsInChildren<ReflectionProbe>(true)) rp.enabled = false;
            }
        if (without.Contains("refl"))
        {
            RenderSettings.reflectionIntensity = 0f;
            foreach (var rp in FindObjectsByType<ReflectionProbe>()) rp.enabled = false;
        }

        var start = player.transform.position;
        var line = WalkRoute.Line(start, WalkRoute.Desert, new List<string>());
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
        views.AddRange(PilotViews(start));
        var log = new List<string>();
        foreach (var v in views)
        {
            if (cc) cc.enabled = false;
            player.transform.position = v.pos + Vector3.up * 0.05f;
            if (cc) cc.enabled = true;
            for (int k = 0; k < 180; k++)   // the eye adapts indoors over 1.4 s
            {
                motor.TurnTowards(v.target + Vector3.up * 1.5f, 0.02f);
                if (health) health.Heal(1000f);
                if (level) level.HoldClockForBenchmark(120f);
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_dir, $"view_{v.name}.png"), tex.EncodeToPNG());
            Destroy(tex);
            log.Add($"{v.name}: at {v.pos} looking at {v.target}");
        }
        log.Add($"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        File.WriteAllLines(Path.Combine(_dir, "views.txt"), log);
        GameSettings.QualityTier = savedTier;
        Application.Quit();
    }
}
