using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The list of arenas the dashboard offers, as data rather than as a hard-coded menu.
///
/// This is the same extension point the rest of the kit uses: a new arena is a new
/// <see cref="LevelTheme"/> asset and a scene built from it, and it appears on the
/// dashboard by being added here -- not by editing the menu. Duplicating a theme,
/// building a scene from it and dropping an entry in this list is the whole workflow.
/// See the "Content is data, not code" section of CLAUDE.md.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Arena Catalog", fileName = "Arenas")]
public class ArenaCatalog : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Shown on the card. Falls back to the scene name when empty.")]
        public string displayName;

        [Tooltip("Scene name exactly as it appears in Build Settings. This is what is " +
                 "loaded, so an arena missing from Build Settings is the one way a card " +
                 "can be shown and not work -- MainMenuController checks and says so.")]
        public string sceneName;

        [TextArea(2, 3)]
        [Tooltip("One line under the name. The theme's own description is a sensible source.")]
        public string description;

        [Tooltip("Preview image. Rendered from the built scene by FPSKit > Build Dashboard; " +
                 "a card with none falls back to a flat colour drawn from the theme.")]
        public Texture2D preview;

        [Tooltip("The theme this arena was built from. Only used for colour, so a card " +
                 "still works with this empty.")]
        public LevelTheme theme;

        public string Label => string.IsNullOrWhiteSpace(displayName) ? sceneName : displayName;

        /// <summary>The card's accent, taken from the theme so the grid reads as a set.</summary>
        public Color Accent => theme != null ? theme.accentLightColor : new Color(0.9f, 0.55f, 0.2f);
    }

    public List<Entry> arenas = new List<Entry>();

    /// <summary>The entry for a scene name, or null. Used to label a run after the fact.</summary>
    public Entry Find(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;

        foreach (var entry in arenas)
            if (entry != null && entry.sceneName == sceneName) return entry;

        return null;
    }
}
