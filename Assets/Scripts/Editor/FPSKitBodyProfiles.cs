#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// One <see cref="BodyDamageProfile"/> per enemy type, so each type's limb health, wound
    /// thresholds, core floor and pain can be tuned on its own (Docs/ENEMY_AI_PLAN.md, Stage 1).
    /// A new profile starts as a copy of the soldier's own (Soldier/BodyDamage.asset). An
    /// existing one is never overwritten: it is the tuning. Toughness is not baked in -- the
    /// archetype's woundResistance still stretches whatever the profile says.
    ///
    /// FPSKitEnemyRoster.Configure assigns these, so a reset keeps them; the menu item below
    /// assigns them to archetypes made before profiles existed.
    /// </summary>
    public static class FPSKitBodyProfiles
    {
        public const string Folder = "Assets/FPSKit_Generated/Soldier/BodyProfiles";

        public static BodyDamageProfile GetOrCreate(string displayName)
        {
            string path = $"{Folder}/{displayName.Replace(" ", "")}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<BodyDamageProfile>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated/Soldier", "BodyProfiles");

            var profile = ScriptableObject.CreateInstance<BodyDamageProfile>();
            var template = AssetDatabase.LoadAssetAtPath<BodyDamageProfile>(FPSKitRiggedSoldier.ProfilePath);
            if (template != null) EditorUtility.CopySerialized(template, profile);
            profile.name = displayName.Replace(" ", "");
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        [MenuItem("FPSKit/Enemy AI/Make Body Profiles")]
        public static void EnsureAll()
        {
            int made = 0;
            foreach (var name in FPSKitEnemyRoster.Names)
            {
                var archetype = FPSKitEnemyRoster.GetOrCreate(name);
                if (archetype.bodyProfile != null) continue;
                archetype.bodyProfile = GetOrCreate(name);
                EditorUtility.SetDirty(archetype);
                made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] Body profiles: {made} enemy types given their own, in {Folder}.");
        }
    }
}
#endif
