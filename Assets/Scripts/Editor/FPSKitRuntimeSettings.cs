#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The tuning assets the game loads from Resources at runtime -- melee, movement and the
    /// player's condition -- made if missing and never overwritten, so Inspector tuning
    /// survives. Clips are filled only where empty.
    ///
    ///   FPSKit > Settings > Ensure Runtime Settings
    /// </summary>
    public static class FPSKitRuntimeSettings
    {
        const string Folder = "Assets/FPSKit_Generated/Resources";

        [MenuItem("FPSKit/Settings/Ensure Runtime Settings", false, 70)]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Resources");

            Make<MeleeSettings>(MeleeSettings.ResourcePath);
            Make<MovementSettings>(MovementSettings.ResourcePath);
            Make<PlayerBodySettings>(PlayerBodySettings.ResourcePath);
            Make<EyeCameraSettings>(EyeCameraSettings.ResourcePath);
            FPSKitFirstPersonMoves.Ensure();
            var condition = Make<ConditionSettings>(ConditionSettings.ResourcePath);

            bool dirty = false;
            if (condition.breathing == null)
            {
                condition.breathing = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/player_breath.wav");
                dirty = true;
            }
            if (condition.heartbeat == null)
            {
                condition.heartbeat = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/player_heartbeat.wav");
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(condition);

            AssetDatabase.SaveAssets();
        }

        static T Make<T>(string name) where T : ScriptableObject
        {
            string path = $"{Folder}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
#endif
