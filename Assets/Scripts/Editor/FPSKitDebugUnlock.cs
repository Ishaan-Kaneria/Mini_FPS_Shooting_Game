#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The menu toggle for <see cref="DebugUnlock"/>, and a warning each time play mode
    /// starts with it on.
    ///
    /// The warning is the point of this file as much as the toggle: an unlock left on is a
    /// campaign whose gates silently do nothing, and a play-through made like that says
    /// nothing about whether the real ladder works.
    /// </summary>
    [InitializeOnLoad]
    public static class FPSKitDebugUnlock
    {
        const string MenuPath = "FPSKit/Debug/Unlock All Zones";

        static FPSKitDebugUnlock()
        {
            // The old switch lived in EditorPrefs and outlived every restart; it is gone for good.
            if (EditorPrefs.HasKey(DebugUnlock.PrefKey)) EditorPrefs.DeleteKey(DebugUnlock.PrefKey);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static bool Enabled
        {
            get => SessionState.GetBool(DebugUnlock.PrefKey, false);
            set => SessionState.SetBool(DebugUnlock.PrefKey, value);
        }

        [MenuItem(MenuPath, false, 40)]
        static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log(Enabled
                ? "[FPSKit] Debug unlock ON for this editor session: every zone and level is open. Off again when the editor restarts; never in a build."
                : "[FPSKit] Debug unlock OFF: the campaign gates are back to what the save says.");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && DebugUnlock.Active)
                Debug.LogWarning("[FPSKit] Debug unlock is ON: every zone and level is open. " +
                                 "Switch it off under FPSKit > Debug > Unlock All Zones.");
        }
    }
}
#endif
