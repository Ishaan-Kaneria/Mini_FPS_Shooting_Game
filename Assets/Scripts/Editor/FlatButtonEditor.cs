#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Unity's Button inspector draws only the Button's own fields, so without this every
    /// field FlatButton adds -- its variant, its parts, the held look the gallery uses --
    /// would be invisible in the Inspector.
    /// </summary>
    [CustomEditor(typeof(FlatButton), true)]
    [CanEditMultipleObjects]
    public class FlatButtonEditor : ButtonEditor
    {
        static readonly string[] Fields = { "_variant", "face", "label", "icon", "theme", "_selected", "holdLook", "heldLook" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            foreach (var name in Fields)
            {
                var p = serializedObject.FindProperty(name);
                if (p != null) EditorGUILayout.PropertyField(p);
            }
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }
    }
}
#endif
