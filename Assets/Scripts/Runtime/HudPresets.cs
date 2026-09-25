using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The built-in HUD layouts. Each is made from the live elements rather than typed in as
/// coordinates, because where "the corner" is depends on the screen and on how big each panel
/// came out -- a preset written as numbers would be right on one phone.
///
///   Default       nothing overridden: every element where its owner put it
///   Minimal       health, ammo, the objective and the crosshair; the rest hidden
///   Competitive   everything a fifth smaller and pushed into its corner, a tighter crosshair
///   Two-Thumb     the default touch layout, right-handed
///   Claw          fire and aim raised to the top of the right side, for an index finger
///   Left-Handed   the touch layout mirrored
/// </summary>
public static class HudPresets
{
    /// <summary>A preset, keeping the crosshair and second fire button from what is being edited unless the preset says otherwise.</summary>
    public static HudLayout.Data Build(string name, HudLayout.Data from)
    {
        var d = new HudLayout.Data { crosshair = from != null ? from.crosshair.Clone() : new HudLayout.Crosshair() };
        switch (name)
        {
            case "Minimal":
                foreach (var id in new[] { "hud.minimap", "hud.mission", "hud.feed", "hud.abilities", "hud.run" })
                    d.Ensure(id).hidden = true;
                break;

            case "Competitive":
                // Measured from the owners' own placement, which is what the preview shows
                // once the working layout is cleared.
                HudLayout.Preview(new HudLayout.Data());
                Canvas.ForceUpdateCanvases();
                foreach (var t in HudLayout.Targets)
                {
                    if (t == null || !t.id.StartsWith("hud.") || !t.gameObject.activeInHierarchy) continue;
                    var e = t.Measure(d.Ensure(t.id));
                    e.scale = 0.8f;
                    // Hard against the edges it is anchored to; the middle axis stays put.
                    if (e.ax != 0.5f) e.x = Mathf.Sign(e.x == 0f ? (e.ax == 0f ? 1f : -1f) : e.x) * 6f;
                    if (e.ay != 0.5f) e.y = Mathf.Sign(e.y == 0f ? (e.ay == 0f ? 1f : -1f) : e.y) * 6f;
                }
                d.crosshair.size = 0.8f;
                d.crosshair.gap = 0.6f;
                break;

            case "Two-Thumb":
                GameSettings.LeftHanded = false;
                break;

            case "Left-Handed":
                GameSettings.LeftHanded = true;
                break;

            case "Claw":
                GameSettings.LeftHanded = false;
                HudLayout.Preview(new HudLayout.Data());
                Canvas.ForceUpdateCanvases();
                Raise(d, "touch.fire", 0.62f);
                Raise(d, "touch.aim", 0.62f);
                break;
        }
        return d;
    }

    /// <summary>Moves a touch button up to a fraction of its parent's height, keeping its distance from the side.</summary>
    static void Raise(HudLayout.Data d, string id, float height)
    {
        var t = HudLayout.TargetById(id);
        if (t == null || !(t.Rect.parent is RectTransform parent)) return;
        var e = t.Measure(d.Ensure(id));
        e.ay = 0f;
        e.y = parent.rect.height * height;
    }
}
