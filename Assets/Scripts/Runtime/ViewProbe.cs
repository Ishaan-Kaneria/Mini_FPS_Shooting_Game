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
/// writes each screen without the HUD (view_*.png), then quits. The rooms and the shop are the interior lighting pilot's
/// (2026-10-30). Holds no static state.
/// </summary>
public class ViewProbe : MonoBehaviour
{
    string _dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitviews");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("ViewProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<ViewProbe>()._dir = args[i + 1];
    }

    /// <summary>The pilot's rooms and shop: the two old houses' first rooms and the lit shop nearest <paramref name="start"/>
    /// (the player's start). The lightmap bake (FPSKitTownLightmaps) chooses them with this, from the scene's player, and
    /// leaves what it chose as marks ("LightmapPilot": room_1, room_2, shop, each with its "Look"), which are used when
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
        if (rooms != null)
            foreach (var house in rooms.transform.Cast<Transform>().Where(h => h.name == "OldHouse").OrderBy(h => (h.position - start).sqrMagnitude))
            {
                var room = house.Cast<Transform>().FirstOrDefault(r => r.name.StartsWith("Room ") && r.Find("Look") != null);
                if (room == null) continue;
                views.Add(($"room_{views.Count + 1}", room.position, room.Find("Look").position));
                if (views.Count >= 2) break;
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
        if (SceneManager.GetActiveScene().name != "DesertOutpost") { SceneManager.LoadScene("DesertOutpost"); yield return new WaitForSecondsRealtime(1f); }
        float until = Time.realtimeSinceStartup + 60f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < until && (level == null || level.IsBriefing || !level.IsRunning)) { level = FindAnyObjectByType<LevelManager>(); yield return null; }
        yield return new WaitForSecondsRealtime(3f);
        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        var cc = player != null ? player.GetComponent<CharacterController>() : null;
        if (player == null || motor == null) { File.WriteAllText(Path.Combine(_dir, "views.txt"), "no player"); Application.Quit(1); yield break; }
        foreach (var c in FindObjectsByType<Canvas>()) c.enabled = false;

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
        File.WriteAllLines(Path.Combine(_dir, "views.txt"), log);
        Application.Quit();
    }
}
