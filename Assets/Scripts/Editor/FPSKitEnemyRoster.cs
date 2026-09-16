#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the built-in EnemyArchetype assets -- the roster the WaveManager
    /// draws from.
    ///
    /// This mirrors FPSKitThemes on purpose: variety lives in assets, not in code.
    /// A seventh enemy is a duplicated asset with different numbers dropped into the
    /// WaveManager's roster, and nothing here has to change for it to work.
    /// </summary>
    public static class FPSKitEnemyRoster
    {
        public const string RosterFolder = "Assets/FPSKit_Generated/Enemies";

        /// <summary>
        /// Ordered so the roster reads as a difficulty curve: what you meet first,
        /// then what replaces it, then what shows up to ruin a good run.
        /// </summary>
        public static readonly string[] Names =
        {
            "Grunt",
            "Runner",
            "Marksman",
            "Brute",
            "Sentinel",
            "Screamer",
            "Warden",
            "Harbinger"
        };

        [MenuItem("FPSKit/Create Enemy Archetypes", false, 42)]
        public static void CreateAllMenu()
        {
            var roster = GetOrCreateAll();

            EditorUtility.DisplayDialog("FPSKit Enemies",
                $"{roster.Count} enemy archetypes are ready in:\n{RosterFolder}\n\n" +
                "Select any of them to retune stats, colours, unlock wave and drops. " +
                "Duplicate one to add a brand new enemy -- then drop it into the " +
                "WaveManager's roster. No prefab, no code.", "OK");

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = roster[0];
        }

        [MenuItem("FPSKit/Reset Enemy Archetypes to Defaults", false, 43)]
        public static void ResetAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset enemies",
                "Re-applies the built-in values to every enemy archetype asset.\n\n" +
                "Any tuning you did in the Inspector will be lost.", "Reset them", "Cancel")) return;

            ResetAll();
        }

        /// <summary>
        /// Re-stamps the built-in values onto every archetype asset, with no dialog.
        ///
        /// Split out from the menu item because GetOrCreate only configures an asset it
        /// had to create -- an existing one keeps whatever is on disk, which is the right
        /// behaviour for tuning done in the Inspector and the wrong one entirely for a
        /// retune done here in code. Without a headless way in, changing a number in
        /// Configure would compile, build and ship without ever reaching the asset the
        /// game actually reads. FPSKitBatch.ResetEnemyArchetypes is what CI calls.
        /// </summary>
        public static void ResetAll()
        {
            foreach (var name in Names)
            {
                var archetype = GetOrCreate(name);
                Configure(archetype, name);
                EditorUtility.SetDirty(archetype);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<color=lime>[FPSKit]</color> {Names.Length} enemy archetypes reset to defaults.");
        }

        public static List<EnemyArchetype> GetOrCreateAll()
        {
            EnsureFolders();

            var result = new List<EnemyArchetype>();
            foreach (var name in Names) result.Add(GetOrCreate(name));

            AssetDatabase.SaveAssets();
            return result;
        }

        public static EnemyArchetype GetOrCreate(string displayName)
        {
            EnsureFolders();

            string path = $"{RosterFolder}/{displayName.Replace(" ", "")}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyArchetype>(path);
            if (existing != null) return existing;

            var archetype = ScriptableObject.CreateInstance<EnemyArchetype>();
            Configure(archetype, displayName);

            AssetDatabase.CreateAsset(archetype, path);
            return archetype;
        }

        // ==================================================================
        // The numbers below are multipliers on the base enemy prefab: 100 health,
        // 12 damage, 3.2 m/s. The wave curve multiplies these again on top.
        //
        // Speed is read against the player, who walks at 5.6 and sprints at 8.2. Every
        // multiplier here is set so that nothing outruns a sprint and only the two
        // rushers beat a walk -- disengaging has to stay possible, or positioning stops
        // being a thing the player can do.
        //
        // rangedDamageMultiplier is the counterweight to a roster where most variants
        // shoot: it is what a shot is worth against the same enemy's swing, and it is
        // well under 1 everywhere.
        // ==================================================================
        static void Configure(EnemyArchetype a, string displayName)
        {
            a.displayName = displayName;

            switch (displayName)
            {
                // ----------------------------------------------------------
                // The wave-one enemy, and the yardstick everything else is read
                // against. Its weight falls away so later waves are not still
                // mostly grunts.
                case "Grunt":
                    a.description = "Armed rifleman. Numerous early, thins out later.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.bodyColor = new Color(0.55f, 0.18f, 0.18f);
                    a.headColor = new Color(0.75f, 0.30f, 0.25f);
                    a.unlockWave = 1;
                    a.baseWeight = 7f;
                    a.weightGrowthPerWave = -0.28f;
                    a.healthMultiplier = 1f;
                    a.damageMultiplier = 1f;

                    // Well under the player's 5.6 m/s walk. A wave-one enemy that matches
                    // your own speed cannot be disengaged from, which turns the opening
                    // fight into a shoving match rather than something you can back out
                    // of, reposition against and shoot.
                    a.speedMultiplier = 0.85f;

                    a.ranged = true;
                    a.attackRange = 22f;
                    a.preferredRangedDistance = 11f;
                    a.meleeRange = 2.4f;
                    a.shotsPerAttack = 2;
                    a.burstInterval = 0.15f;
                    a.attackCooldown = 1.9f;

                    // A long tell on the first enemy the player ever meets: the flash is
                    // how you learn that the flash means something.
                    a.attackWindup = 0.55f;

                    // Deliberately loose. This is the wave-one gun, and it is meant to
                    // pressure the player from across the arena, not to hit every shot.
                    a.rangedSpread = 5f;
                    a.rangedDamageMultiplier = 0.38f;

                    a.strafeAmount = 0.25f;
                    a.staggerThreshold = 18f;
                    a.suppressionDamage = 40f;
                    a.scoreValue = 100;
                    a.healthDropChance = 0.06f;
                    a.shieldDropChance = 0.05f;
                    break;

                // ----------------------------------------------------------
                // Fast and paper-thin. Exists to punish standing still, and to
                // make the crowd arrive unevenly instead of as one front.
                case "Runner":
                    a.description = "Fast, fragile, charges the last stretch. Punishes camping.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.bodyColor = new Color(0.85f, 0.45f, 0.10f);
                    a.headColor = new Color(1f, 0.65f, 0.25f);
                    a.scaleMultiplier = 0.85f;
                    a.unlockWave = 2;
                    a.baseWeight = 1.5f;
                    a.weightGrowthPerWave = 0.3f;
                    a.maxWeight = 8f;
                    a.healthMultiplier = 0.6f;
                    a.damageMultiplier = 0.8f;

                    // Still the fastest thing on the field, and still slower than a
                    // sprinting player -- it closes on you when you stand still, which is
                    // the whole job, without being able to run you down when you do not.
                    a.speedMultiplier = 1.3f;

                    a.attackRange = 2.4f;
                    a.attackCooldown = 0.9f;
                    a.attackWindup = 0.28f;
                    a.strafeAmount = 0.55f;
                    a.chargeSpeedMultiplier = 1.9f;
                    a.staggerThreshold = 8f;          // flinches at almost anything
                    a.suppressionDamage = 22f;        // and breaks off almost as easily
                    a.scoreValue = 140;
                    a.healthDropChance = 0.05f;
                    a.shieldDropChance = 0.07f;
                    break;

                // ----------------------------------------------------------
                // The first enemy that makes cover matter. Holds a firing line
                // and backs off when crowded.
                case "Marksman":
                    a.description = "Keeps its distance and shoots. Makes cover matter.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.bodyColor = new Color(0.20f, 0.45f, 0.42f);
                    a.headColor = new Color(0.35f, 0.65f, 0.60f);
                    a.unlockWave = 3;
                    a.baseWeight = 1.2f;
                    a.weightGrowthPerWave = 0.18f;
                    a.maxWeight = 5f;
                    a.healthMultiplier = 0.8f;
                    a.damageMultiplier = 0.9f;
                    a.speedMultiplier = 0.75f;
                    a.ranged = true;
                    a.attackRange = 26f;
                    a.preferredRangedDistance = 16f;
                    a.attackCooldown = 2.2f;
                    a.attackWindup = 0.65f;           // long tell: you can break line of sight
                    a.rangedSpread = 3.2f;

                    // Hits harder per shot than a grunt does, and fires a third as often.
                    // This is the variant that is supposed to punish standing in the open.
                    a.rangedDamageMultiplier = 0.55f;

                    a.strafeAmount = 0.4f;
                    a.staggerThreshold = 14f;
                    a.suppressionDamage = 30f;
                    a.scoreValue = 180;
                    a.healthDropChance = 0.07f;
                    a.shieldDropChance = 0.12f;
                    break;

                // ----------------------------------------------------------
                // A wall with a long, obvious wind-up. Slow enough to walk away
                // from, punishing if you do not.
                case "Brute":
                    a.description = "Huge, armoured, hits like a truck. Telegraphs hard.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.bodyColor = new Color(0.28f, 0.20f, 0.30f);
                    a.headColor = new Color(0.45f, 0.30f, 0.42f);
                    a.glowColor = new Color(0.6f, 0.1f, 0.25f);
                    a.scaleMultiplier = 1.45f;
                    a.unlockWave = 4;
                    a.baseWeight = 0.8f;
                    a.weightGrowthPerWave = 0.2f;
                    a.maxWeight = 4f;
                    a.healthMultiplier = 3.2f;
                    a.shieldFraction = 0.3f;
                    a.damageMultiplier = 1.9f;
                    a.speedMultiplier = 0.6f;
                    a.attackRange = 3.2f;
                    a.meleeRange = 3.2f;
                    a.attackCooldown = 2f;
                    a.attackWindup = 0.7f;
                    a.strafeAmount = 0.1f;
                    a.staggerThreshold = 70f;         // only a headshot moves it
                    a.canRetreat = false;             // and nothing makes it back off
                    a.scoreValue = 320;
                    a.healthDropChance = 0.25f;
                    a.shieldDropChance = 0.2f;
                    break;

                // ----------------------------------------------------------
                // Elite: burst-firing and shielded. The shield is the point --
                // it forces sustained accurate fire instead of one lucky shot.
                case "Sentinel":
                    a.description = "Shielded burst-fire elite. Break the armour first.";
                    a.role = EnemyArchetype.Role.Elite;
                    a.bodyColor = new Color(0.20f, 0.30f, 0.55f);
                    a.headColor = new Color(0.45f, 0.60f, 0.90f);
                    a.glowColor = new Color(0.2f, 0.7f, 1.6f);
                    a.scaleMultiplier = 1.1f;
                    a.unlockWave = 7;
                    a.baseWeight = 0.6f;
                    a.weightGrowthPerWave = 0.14f;
                    a.maxWeight = 3.5f;
                    a.healthMultiplier = 1.8f;
                    a.shieldFraction = 1f;            // half its effective pool is armour
                    a.damageMultiplier = 0.7f;
                    a.speedMultiplier = 0.7f;
                    a.ranged = true;
                    a.attackRange = 24f;
                    a.preferredRangedDistance = 14f;
                    a.shotsPerAttack = 3;
                    a.burstInterval = 0.1f;
                    a.attackCooldown = 2.6f;
                    a.attackWindup = 0.55f;
                    a.rangedSpread = 2.4f;
                    a.rangedDamageMultiplier = 0.45f;
                    a.strafeAmount = 0.5f;
                    a.staggerThreshold = 40f;

                    // Armour is its answer to being shot, so it has no need of cover.
                    a.canRetreat = false;
                    a.scoreValue = 450;
                    a.healthDropChance = 0.3f;
                    a.shieldDropChance = 0.35f;
                    break;

                // ----------------------------------------------------------
                // Late-game pressure: small, quick, constantly circling. Meant to
                // be the reason you cannot hold one corner forever.
                case "Screamer":
                    a.description = "Small, quick and always circling. Flanks relentlessly.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.bodyColor = new Color(0.62f, 0.15f, 0.45f);
                    a.headColor = new Color(0.90f, 0.35f, 0.65f);
                    a.glowColor = new Color(0.8f, 0.1f, 0.5f);
                    a.scaleMultiplier = 0.72f;
                    a.unlockWave = 9;
                    a.baseWeight = 0.8f;
                    a.weightGrowthPerWave = 0.32f;
                    a.maxWeight = 9f;
                    a.healthMultiplier = 0.75f;
                    a.damageMultiplier = 0.85f;
                    a.speedMultiplier = 1.35f;
                    a.attackRange = 2.3f;
                    a.attackCooldown = 0.75f;
                    a.attackWindup = 0.25f;
                    a.strafeAmount = 0.9f;
                    a.chargeSpeedMultiplier = 1.6f;
                    a.staggerThreshold = 10f;
                    a.suppressionDamage = 26f;
                    a.scoreValue = 220;
                    a.healthDropChance = 0.05f;
                    a.shieldDropChance = 0.08f;
                    break;

                // ----------------------------------------------------------
                // First boss. A single enormous melee threat with an escort, so
                // the fight is about managing space rather than raw damage.
                case "Warden":
                    a.description = "Boss. An enormous armoured melee threat with a long reach.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.bodyColor = new Color(0.32f, 0.06f, 0.08f);
                    a.headColor = new Color(0.60f, 0.12f, 0.12f);
                    a.glowColor = new Color(2.2f, 0.25f, 0.15f);
                    a.scaleMultiplier = 2.2f;
                    a.unlockWave = 5;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 10f;
                    a.shieldFraction = 0.5f;
                    a.damageMultiplier = 2.2f;
                    a.speedMultiplier = 0.7f;
                    a.attackRange = 4.2f;
                    a.meleeRange = 4.2f;
                    a.attackCooldown = 1.9f;
                    a.attackWindup = 0.8f;
                    a.strafeAmount = 0.15f;
                    a.chargeSpeedMultiplier = 2.2f;
                    a.staggerThreshold = 0f;          // nothing interrupts a boss
                    a.canRetreat = false;             // and nothing makes one give ground
                    a.scoreValue = 2500;
                    a.healthDropChance = 1f;
                    break;

                // ----------------------------------------------------------
                // Second boss, so boss waves do not become one repeated fight.
                // Ranged, so it demands the opposite approach to the Warden.
                case "Harbinger":
                    a.description = "Boss. Hangs back and shells the arena in bursts.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.bodyColor = new Color(0.12f, 0.24f, 0.30f);
                    a.headColor = new Color(0.30f, 0.60f, 0.70f);
                    a.glowColor = new Color(0.3f, 1.8f, 2.2f);
                    a.scaleMultiplier = 1.9f;
                    a.unlockWave = 10;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 8f;
                    a.shieldFraction = 0.8f;
                    a.damageMultiplier = 1.3f;
                    a.speedMultiplier = 0.95f;
                    a.ranged = true;
                    a.attackRange = 34f;
                    a.preferredRangedDistance = 20f;
                    a.shotsPerAttack = 5;
                    a.burstInterval = 0.09f;
                    a.attackCooldown = 2.4f;
                    a.attackWindup = 0.75f;
                    a.rangedSpread = 3.5f;

                    // Five shots a burst is the pressure; the per-shot number has to come
                    // down to match, or one volley deletes a full-health player outright.
                    a.rangedDamageMultiplier = 0.5f;

                    a.strafeAmount = 0.6f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 3200;
                    a.healthDropChance = 1f;
                    break;
            }
        }

        // ==================================================================
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(RosterFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Enemies");
        }
    }
}
#endif
