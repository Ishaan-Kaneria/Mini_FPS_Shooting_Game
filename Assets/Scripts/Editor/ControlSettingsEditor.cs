#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Adds one-click scheme presets above the raw binding fields, so you can swap
    /// the whole control layout without checking twenty dropdowns by hand.
    /// </summary>
    [CustomEditor(typeof(ControlSettings))]
    public class ControlSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (ControlSettings)target;

            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Applying a preset overwrites every binding below. Edit them afterwards " +
                "to make your own.", MessageType.None);

            DrawPreset(settings, ControlSettings.Preset.StandardFPS,
                "Standard FPS",
                "WASD and arrows both move. Left click fires, right click aims, " +
                "Space jumps, Shift sprints. What every shooter uses.");

            DrawPreset(settings, ControlSettings.Preset.ArrowsAndMouse,
                "Arrows + Mouse",
                "Arrows only for movement, mouse for shooting and aiming. " +
                "Space jumps, Shift sprints. No key does two jobs.");

            DrawPreset(settings, ControlSettings.Preset.ArrowsAndSpace,
                "Arrows + Space",
                "Arrows move, Space fires, double-tap Space sprints, left click jumps. " +
                "Note: the first tap of a sprint also fires a shot.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bindings", EditorStyles.boldLabel);

            DrawDefaultInspector();
        }

        private void DrawPreset(ControlSettings settings, ControlSettings.Preset preset,
                                string label, string description)
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(label, GUILayout.Width(130f), GUILayout.Height(34f)))
                {
                    Undo.RecordObject(settings, "Apply control preset");

                    settings.ApplyPreset(preset);

                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }

                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
#endif
