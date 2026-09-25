using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's HUD layout: where each movable element sits, how big and how opaque it is,
/// whether it shows, how the crosshair looks, and whether a phone has a second fire button.
///
/// <b>Kept per device form</b> -- handset, tablet, desktop -- so arranging the buttons on a
/// phone never moves a panel on the PC, and stored with the rest of the settings (PlayerPrefs,
/// under the settings prefix), as JSON. Up to three custom layouts per form can be saved by
/// name beside the one in use.
///
/// <b>Positions are anchored to the nearest screen edge</b> (see <see cref="HudLayoutTarget"/>):
/// an element in the bottom-right third is stored as an offset from the bottom-right corner,
/// one in the middle third as an offset from the middle. So a layout saved on one resolution
/// or aspect lands in the same place relative to the edges it was near on another.
///
/// An element with no entry is wherever its owner put it -- the builder, <see cref="HudView"/>
/// or <see cref="TouchCluster"/> -- which is what "Default" and "Reset" mean.
/// </summary>
public static class HudLayout
{
    [Serializable]
    public class Entry
    {
        public string id;
        public float ax, ay;        // the anchor, and the pivot: 0, 0.5 or 1 on each axis
        public float x, y;          // from that anchor, in the parent's units
        public float scale = 1f;    // 0.6 to 1.5, on top of what the owner chose
        public float opacity = 1f;  // 0.2 to 1
        public bool hidden;
        public bool placed;         // false: only size/opacity/visibility are overridden
    }

    public enum CrosshairStyle { Cross, Dot, Circle }

    [Serializable]
    public class Crosshair
    {
        public CrosshairStyle style = CrosshairStyle.Cross;
        public int color;           // an index into Colors
        public float size = 1f;     // 0.5 to 2
        public float thickness = 1f;// 0.5 to 3
        public float gap = 1f;      // 0 to 2
        public bool outline = true;

        public Crosshair Clone() => (Crosshair)MemberwiseClone();
    }

    [Serializable]
    public class Data
    {
        public string name = "";
        public List<Entry> entries = new List<Entry>();
        public Crosshair crosshair = new Crosshair();
        public bool secondFire;

        public Entry Find(string id)
        {
            foreach (var e in entries) if (e != null && e.id == id) return e;
            return null;
        }

        public Entry Ensure(string id)
        {
            var e = Find(id);
            if (e == null) { e = new Entry { id = id }; entries.Add(e); }
            return e;
        }

        public Data Clone() => JsonUtility.FromJson<Data>(JsonUtility.ToJson(this));
    }

    /// <summary>The crosshair colours on offer. White first: it is the default and the one that reads everywhere.</summary>
    public static readonly (string name, Color color)[] Colors =
    {
        ("White", Color.white),
        ("Green", new Color(0.45f, 1f, 0.45f)),
        ("Yellow", new Color(1f, 0.92f, 0.3f)),
        ("Cyan", new Color(0.35f, 0.95f, 1f)),
        ("Pink", new Color(1f, 0.45f, 0.85f)),
        ("Red", new Color(1f, 0.3f, 0.28f)),
    };

    public const int CustomSlots = 3;

    const string Prefix = "settings.hud.";

    static Data _current;
    static Data _shown;

    /// <summary>What is on screen: the kept layout, or the editor's preview of one.</summary>
    public static Data Shown => _shown ?? Current;
    static readonly List<HudLayoutTarget> _targets = new List<HudLayoutTarget>();

    /// <summary>Raised after the layout in use changes: saved, loaded, or previewed by the editor.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _current = null;
        _shown = null;
        _targets.Clear();
        Changed = null;
    }

    /// <summary>Handset, tablet or desktop -- a laptop lays out like a desktop.</summary>
    public static string Form => DeviceProfile.CurrentForm switch
    {
        DeviceProfile.Form.Handset => "handset",
        DeviceProfile.Form.Tablet => "tablet",
        _ => "desktop",
    };

    /// <summary>The layout in use on this device form. Never null.</summary>
    public static Data Current
    {
        get
        {
            if (_current == null) _current = Load(Prefix + Form) ?? new Data();
            return _current;
        }
    }

    /// <summary>Makes a layout the one in use and keeps it.</summary>
    public static void Save(Data data)
    {
        _current = data != null ? data.Clone() : new Data();
        PlayerPrefs.SetString(Prefix + Form, JsonUtility.ToJson(_current));
        PlayerPrefs.Save();
        ApplyAll(_current);
        Changed?.Invoke();
    }

    /// <summary>Shows a layout without keeping it -- what the editor does while you drag.</summary>
    public static void Preview(Data data)
    {
        ApplyAll(data ?? Current);
        Changed?.Invoke();
    }

    /// <summary>Puts the kept layout back on screen, after a preview the player cancelled.</summary>
    public static void Revert() => Preview(Current);

    /// <summary>Forgets the kept layout on this form. The owners' own placement comes back.</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Prefix + Form);
        _current = null;
        ApplyAll(Current);
        Changed?.Invoke();
    }

    // ---- custom slots ------------------------------------------------------------

    public static Data Custom(int slot) => slot < 0 || slot >= CustomSlots ? null : Load(Prefix + Form + ".custom" + slot);

    public static void SaveCustom(int slot, Data data, string name)
    {
        if (slot < 0 || slot >= CustomSlots || data == null) return;
        var copy = data.Clone();
        copy.name = string.IsNullOrWhiteSpace(name) ? $"Layout {slot + 1}" : name.Trim();
        PlayerPrefs.SetString(Prefix + Form + ".custom" + slot, JsonUtility.ToJson(copy));
        PlayerPrefs.Save();
    }

    static Data Load(string key)
    {
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<Data>(json); }
        catch (Exception) { return null; }
    }

    /// <summary>Every form's kept layout, for Settings' reset. The named custom layouts are the player's and stay.</summary>
    public static void ForgetAllForms()
    {
        foreach (var form in new[] { "handset", "tablet", "desktop" }) PlayerPrefs.DeleteKey(Prefix + form);
        _current = null;
        ApplyAll(Current);
        Changed?.Invoke();
    }

    // ---- targets -------------------------------------------------------------------

    public static IReadOnlyList<HudLayoutTarget> Targets => _targets;

    internal static void Register(HudLayoutTarget t)
    {
        if (t != null && !_targets.Contains(t)) _targets.Add(t);
    }

    internal static void Unregister(HudLayoutTarget t) => _targets.Remove(t);

    public static HudLayoutTarget TargetById(string id)
    {
        foreach (var t in _targets) if (t != null && t.id == id) return t;
        return null;
    }

    static void ApplyAll(Data data)
    {
        _shown = data;
        foreach (var t in _targets) if (t != null) t.Apply(data.Find(t.id));
    }
}
