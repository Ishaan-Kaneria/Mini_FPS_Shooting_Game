#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the built-in EnemyArchetype assets -- the roster the LevelManager
    /// draws from.
    ///
    /// This mirrors FPSKitThemes on purpose: variety lives in assets, not in code.
    /// A seventh enemy is a duplicated asset with different numbers dropped into the
    /// LevelManager's roster, and nothing here has to change for it to work.
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
            // The shared ladder. Every arena draws from these, which is what keeps a
            // level's difficulty step meaning the same thing wherever it is played.
            "Grunt",
            "Runner",
            "Marksman",
            "Brute",
            "Sentinel",
            "Screamer",

            // One per zone, and the reason an arena fights differently from the one
            // before it. Each is built around the ground it stands on: a plant has
            // close corners, a quarry has none, a rooftop is all sightline.
            "Foreman",
            "Blaster",
            "Outrider",
            "Spotter",
            "Stalker",
            "Hardsuit",

            // The two generic bosses, met at the third and sixth rung of every ladder.
            "Warden",
            "Harbinger",

            // And the six who hold a zone. Named rather than numbered because the last
            // level of a zone is a person -- see CampaignData.
            "Tove Auger",
            "Kestrel Auger",
            "Aurel Auger",
            "Ilsa Auger",
            "Dev Auger",
            "Roan Auger",

            // The last fight in the campaign.
            "Marit Auger"
        };

        /// <summary>
        /// The bosses that are not anybody in particular, met partway up every ladder.
        ///
        /// Kept apart from the named six because <see cref="FPSKitLevels"/> has to be
        /// able to pick a boss for rung three without reaching for the person standing
        /// at rung eight -- which would kill Tove twice in one zone, the second time as
        /// a warm-up for herself.
        /// </summary>
        public static readonly string[] GenericBossNames = { "Warden", "Harbinger" };

        /// <summary>
        /// The local threat each zone introduces, in <see cref="FPSKitCampaign.ZoneOrder"/>
        /// order. What makes an arena fight like itself.
        /// </summary>
        public static readonly string[] ZoneDebut =
        {
            "Foreman",    // the plant: close corners, so something that wants to be close
            "Blaster",    // the quarry: open ground, so something that punishes standing in it
            "Outrider",   // the road: distance, so something that keeps it
            "Spotter",    // the rooftops: sightlines, so something that owns one
            "Stalker",    // the works: dark and broken, so something that uses both
            "Hardsuit"    // the colony: nowhere to run, so something you cannot outlast
        };

        [MenuItem("FPSKit/Create Enemy Archetypes", false, 42)]
        public static void CreateAllMenu()
        {
            var roster = GetOrCreateAll();

            EditorUtility.DisplayDialog("FPSKit Enemies",
                $"{roster.Count} enemy archetypes are ready in:\n{RosterFolder}\n\n" +
                "Select any of them to retune stats, colours, difficulty step and drops. " +
                "Duplicate one to add a brand new enemy -- then drop it into the " +
                "LevelManager's roster. No prefab, no code.", "OK");

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
        // 12 damage, 3.2 m/s. The level's own difficulty multiplies these again on top.
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
                // The level-one enemy, and the yardstick everything else is read
                // against. Its weight falls away so later levels are not still
                // mostly grunts.
                case "Grunt":
                    a.description = "Armed rifleman. Numerous early, thins out later.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.1f;
                    a.bodyColor = new Color(0.55f, 0.18f, 0.18f);
                    a.headColor = new Color(0.75f, 0.30f, 0.25f);
                    a.unlockWave = 1;
                    a.baseWeight = 7f;
                    a.weightGrowthPerWave = -0.28f;
                    a.healthMultiplier = 1f;
                    a.damageMultiplier = 1f;

                    // Well under the player's 5.6 m/s walk. A level-one enemy that matches
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

                    // Deliberately loose. This is the level-one gun, and it is meant to
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
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.0f;
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
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.05f;
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
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 0.6f;
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
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.45f;
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
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 0.0f;
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

                // ==========================================================
                // The six local threats.
                //
                // Each is the answer to a question its arena asks and the others do not,
                // so the same difficulty step fights differently in each of them. They
                // are deliberately stronger than the shared ladder at the same step --
                // an arena's own enemy has to be the thing the player remembers about
                // it -- and every one of them is paid for somewhere: a Spotter that
                // hits from forty metres has the health of a Runner, and a Hardsuit that
                // cannot be outlasted can be walked away from.
                // ==========================================================

                // ----------------------------------------------------------
                // The plant. Short, ugly corners and a roof to be fought over, so
                // something that wants the range the player least wants it at.
                case "Foreman":
                    a.description = "Comes in close and fires as it walks. No good answer at ten metres.";
                    a.role = EnemyArchetype.Role.Elite;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.4f;
                    a.bodyColor = new Color(0.34f, 0.26f, 0.14f);
                    a.headColor = new Color(0.58f, 0.46f, 0.24f);
                    a.glowColor = new Color(1.6f, 0.8f, 0.2f);
                    a.scaleMultiplier = 1.2f;
                    a.unlockWave = 3;
                    a.baseWeight = 0.7f;
                    a.weightGrowthPerWave = 0.16f;
                    a.maxWeight = 3.5f;
                    a.healthMultiplier = 2.4f;
                    a.shieldFraction = 0.4f;
                    a.damageMultiplier = 1.3f;
                    a.speedMultiplier = 0.9f;
                    a.ranged = true;
                    a.attackRange = 14f;

                    // Close, and it closes further. The whole threat is that backing off
                    // to a comfortable range is exactly what it wants.
                    a.preferredRangedDistance = 7f;
                    a.shotsPerAttack = 3;
                    a.burstInterval = 0.07f;
                    a.attackCooldown = 1.5f;
                    a.attackWindup = 0.35f;
                    a.rangedSpread = 6f;
                    a.rangedDamageMultiplier = 0.45f;
                    a.meleeRange = 3f;
                    a.strafeAmount = 0.3f;
                    a.staggerThreshold = 45f;
                    a.scoreValue = 340;
                    a.healthDropChance = 0.2f;
                    a.ammoDropChance = 0.12f;
                    break;

                // ----------------------------------------------------------
                // The quarry. Wide open snow, so something that makes standing
                // still in it expensive.
                case "Blaster":
                    a.description = "One heavy shell at a time, with a wind-up you can see and beat.";
                    a.role = EnemyArchetype.Role.Elite;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.45f;
                    a.bodyColor = new Color(0.22f, 0.28f, 0.34f);
                    a.headColor = new Color(0.52f, 0.62f, 0.70f);
                    a.glowColor = new Color(0.9f, 1.4f, 2.0f);
                    a.scaleMultiplier = 1.3f;
                    a.unlockWave = 4;
                    a.baseWeight = 0.6f;
                    a.weightGrowthPerWave = 0.14f;
                    a.maxWeight = 3f;
                    a.healthMultiplier = 2.6f;
                    a.shieldFraction = 0.25f;
                    a.damageMultiplier = 1f;
                    a.speedMultiplier = 0.55f;
                    a.ranged = true;
                    a.attackRange = 26f;
                    a.preferredRangedDistance = 18f;
                    a.shotsPerAttack = 1;

                    // One shot, and it is the hardest single hit in the roster -- which
                    // is only fair because the wind-up is nearly a second and it cannot
                    // follow you out of the way.
                    a.rangedDamageMultiplier = 1.5f;
                    a.attackCooldown = 3.2f;
                    a.attackWindup = 0.95f;
                    a.rangedSpread = 1.4f;
                    a.strafeAmount = 0.1f;
                    a.staggerThreshold = 40f;
                    a.scoreValue = 380;
                    a.healthDropChance = 0.18f;
                    break;

                // ----------------------------------------------------------
                // The road. Long sightlines and nothing to hide behind, so
                // something that keeps the distance the arena hands it.
                case "Outrider":
                    a.description = "Fast, keeps its distance, and will not stand and trade.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.2f;
                    a.bodyColor = new Color(0.46f, 0.34f, 0.16f);
                    a.headColor = new Color(0.72f, 0.58f, 0.30f);
                    a.glowColor = new Color(1.4f, 1.0f, 0.3f);
                    a.scaleMultiplier = 0.95f;
                    a.unlockWave = 3;
                    a.baseWeight = 0.9f;
                    a.weightGrowthPerWave = 0.18f;
                    a.maxWeight = 4f;
                    a.healthMultiplier = 1.1f;
                    a.damageMultiplier = 0.9f;

                    // Under the player's walk, like everything that is not a rusher.
                    // Disengaging has to stay possible or positioning stops being a
                    // thing the player can do.
                    a.speedMultiplier = 1.45f;
                    a.ranged = true;
                    a.attackRange = 24f;
                    a.preferredRangedDistance = 17f;
                    a.shotsPerAttack = 2;
                    a.burstInterval = 0.1f;
                    a.attackCooldown = 1.7f;
                    a.attackWindup = 0.3f;
                    a.rangedSpread = 4f;
                    a.rangedDamageMultiplier = 0.5f;
                    a.strafeAmount = 0.85f;
                    a.canRetreat = true;
                    a.suppressionDamage = 28f;        // breaks off early and often
                    a.staggerThreshold = 16f;
                    a.scoreValue = 240;
                    a.ammoDropChance = 0.15f;
                    break;

                // ----------------------------------------------------------
                // The rooftops. One long sightline is the whole arena, so
                // something that owns one and has to be gone to.
                case "Spotter":
                    a.description = "Hits from across the map. Fragile, and it knows it.";
                    a.role = EnemyArchetype.Role.Elite;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 0.05f;
                    a.bodyColor = new Color(0.16f, 0.18f, 0.26f);
                    a.headColor = new Color(0.40f, 0.44f, 0.58f);
                    a.glowColor = new Color(2.0f, 0.4f, 0.4f);
                    a.scaleMultiplier = 1f;
                    a.unlockWave = 5;
                    a.baseWeight = 0.5f;
                    a.weightGrowthPerWave = 0.12f;
                    a.maxWeight = 2.5f;

                    // The health of a Runner. Everything this thing is, is bought with
                    // the fact that one clean burst ends it.
                    a.healthMultiplier = 0.7f;
                    a.damageMultiplier = 1f;
                    a.speedMultiplier = 0.6f;
                    a.ranged = true;
                    a.attackRange = 46f;
                    a.preferredRangedDistance = 34f;
                    a.shotsPerAttack = 1;
                    a.rangedDamageMultiplier = 1.2f;
                    a.attackCooldown = 2.6f;
                    a.attackWindup = 0.85f;
                    a.rangedSpread = 0.8f;            // the most accurate thing in the game
                    a.strafeAmount = 0.15f;
                    a.canRetreat = true;
                    a.staggerThreshold = 12f;         // and the easiest to put off its shot
                    a.scoreValue = 420;
                    a.ammoDropChance = 0.1f;
                    break;

                // ----------------------------------------------------------
                // The works. Dark, broken ground with holes in it, so something
                // that uses the dark and arrives before it is seen.
                case "Stalker":
                    a.description = "Closes fast in the dark and hits far harder than it looks.";
                    a.role = EnemyArchetype.Role.Standard;
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 0.25f;
                    a.bodyColor = new Color(0.12f, 0.14f, 0.13f);
                    a.headColor = new Color(0.30f, 0.38f, 0.32f);
                    a.glowColor = new Color(0.4f, 1.8f, 0.7f);
                    a.scaleMultiplier = 0.9f;
                    a.unlockWave = 4;
                    a.baseWeight = 0.9f;
                    a.weightGrowthPerWave = 0.2f;
                    a.maxWeight = 4.5f;
                    a.healthMultiplier = 0.85f;
                    a.damageMultiplier = 1.8f;
                    a.speedMultiplier = 1.5f;
                    a.attackRange = 2.4f;
                    a.meleeRange = 2.6f;
                    a.attackCooldown = 1.1f;
                    a.attackWindup = 0.25f;
                    a.chargeSpeedMultiplier = 2.1f;
                    a.strafeAmount = 0.45f;
                    a.staggerThreshold = 14f;
                    a.scoreValue = 260;
                    a.healthDropChance = 0.14f;
                    break;

                // ----------------------------------------------------------
                // The colony. Sealed, thin air, nowhere to be, so something that
                // cannot be outlasted -- only avoided.
                case "Hardsuit":
                    a.description = "Sealed armour. Outlasting it is not the plan; leaving is.";
                    a.role = EnemyArchetype.Role.Elite;
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 0.8f;
                    a.bodyColor = new Color(0.38f, 0.22f, 0.16f);
                    a.headColor = new Color(0.62f, 0.38f, 0.26f);
                    a.glowColor = new Color(1.8f, 0.6f, 0.2f);
                    a.scaleMultiplier = 1.35f;
                    a.unlockWave = 6;
                    a.baseWeight = 0.55f;
                    a.weightGrowthPerWave = 0.16f;
                    a.maxWeight = 3.5f;
                    a.healthMultiplier = 3.6f;

                    // More armour than body. It is the longest thing in the game to
                    // kill, which is only survivable because it is also the slowest.
                    a.shieldFraction = 1.3f;
                    a.damageMultiplier = 1.5f;
                    a.speedMultiplier = 0.5f;
                    a.ranged = true;
                    a.attackRange = 18f;
                    a.preferredRangedDistance = 11f;
                    a.shotsPerAttack = 2;
                    a.burstInterval = 0.12f;
                    a.attackCooldown = 2.1f;
                    a.attackWindup = 0.5f;
                    a.rangedSpread = 3.5f;
                    a.rangedDamageMultiplier = 0.6f;
                    a.meleeRange = 3.4f;
                    a.strafeAmount = 0.1f;
                    a.staggerThreshold = 85f;
                    a.canRetreat = false;
                    a.scoreValue = 460;
                    a.healthDropChance = 0.3f;
                    a.shieldDropChance = 0.3f;
                    break;

                // ----------------------------------------------------------
                // First boss. A single enormous melee threat with an escort, so
                // the fight is about managing space rather than raw damage.
                case "Warden":
                    a.description = "Boss. An enormous armoured melee threat with a long reach.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 1.0f;
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
                    a.voice = EnemyVoice.Kind.Creature;
                    a.woundResistance = 1.0f;
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

                // ==========================================================
                // The six who hold a zone.
                //
                // <b>Each one fights the way their arena does.</b> They are the last
                // level of their ladder, so they are the last thing the player learns
                // about that place -- Kestrel is the quarry's Blaster taken to its
                // conclusion, Ilsa is the rooftop's sightline with a person on the end
                // of it. A boss that fought the same everywhere would say that the six
                // zones were a skybox change, which is exactly what this game used to be.
                //
                // They are all above the generic bosses and none of them is merely a
                // bigger number: every one has something the other five do not do.
                // ==========================================================

                // The plant. Never gives ground, and the pressure is constant rather
                // than spiky -- the fight is about the room, not about a wind-up.
                case "Tove Auger":
                    a.description = "Boss. Holds the hall roof and does not stop firing.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.34f, 0.24f, 0.10f);
                    a.headColor = new Color(0.66f, 0.50f, 0.22f);
                    a.glowColor = new Color(2.4f, 1.2f, 0.25f);
                    a.scaleMultiplier = 1.9f;
                    a.unlockWave = 5;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 11f;
                    a.shieldFraction = 0.7f;
                    a.damageMultiplier = 1.6f;
                    a.speedMultiplier = 0.85f;
                    a.ranged = true;
                    a.attackRange = 22f;
                    a.preferredRangedDistance = 12f;
                    a.shotsPerAttack = 4;
                    a.burstInterval = 0.08f;
                    a.attackCooldown = 1.6f;
                    a.attackWindup = 0.45f;
                    a.rangedSpread = 4.5f;
                    a.rangedDamageMultiplier = 0.45f;
                    a.meleeRange = 3.6f;
                    a.strafeAmount = 0.35f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 4000;
                    a.healthDropChance = 1f;
                    break;

                // The quarry. One shell at a time, and each one is the hardest hit in
                // the campaign -- the whole fight is the second between the wind-up and
                // being somewhere else.
                case "Kestrel Auger":
                    a.description = "Boss. Fires one shell at a time. Do not be where it lands.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.18f, 0.26f, 0.34f);
                    a.headColor = new Color(0.56f, 0.70f, 0.82f);
                    a.glowColor = new Color(0.8f, 1.8f, 2.6f);
                    a.scaleMultiplier = 2.0f;
                    a.unlockWave = 6;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 12f;
                    a.shieldFraction = 0.5f;
                    a.damageMultiplier = 1f;
                    a.speedMultiplier = 0.6f;
                    a.ranged = true;
                    a.attackRange = 38f;
                    a.preferredRangedDistance = 24f;
                    a.shotsPerAttack = 1;
                    a.rangedDamageMultiplier = 2.2f;
                    a.attackCooldown = 2.8f;
                    a.attackWindup = 1f;
                    a.rangedSpread = 1.2f;
                    a.strafeAmount = 0.1f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 4400;
                    a.healthDropChance = 1f;
                    break;

                // The road. The only boss that closes, and it closes at a speed the
                // player can still walk away from -- barely, and only in a straight line.
                case "Aurel Auger":
                    a.description = "Boss. Comes at you, and keeps coming.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.44f, 0.30f, 0.12f);
                    a.headColor = new Color(0.78f, 0.60f, 0.26f);
                    a.glowColor = new Color(2.6f, 1.4f, 0.3f);
                    a.scaleMultiplier = 1.8f;
                    a.unlockWave = 7;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 11f;
                    a.shieldFraction = 0.4f;
                    a.damageMultiplier = 2f;

                    // 4.3 m/s against a 5.6 walk. It closes on a player who stops and
                    // never on one who keeps moving, which is the lesson the road teaches.
                    a.speedMultiplier = 1.35f;
                    a.attackRange = 3.6f;
                    a.meleeRange = 3.8f;
                    a.attackCooldown = 1.4f;
                    a.attackWindup = 0.5f;
                    a.chargeSpeedMultiplier = 2.3f;
                    a.strafeAmount = 0.2f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 4800;
                    a.healthDropChance = 1f;
                    break;

                // The rooftops. The only boss that gives ground: it reads as her
                // repositioning rather than fleeing, and it is the one fight the player
                // has to cross the arena to finish.
                case "Ilsa Auger":
                    a.description = "Boss. Takes the long shot, then moves before you answer it.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.14f, 0.16f, 0.26f);
                    a.headColor = new Color(0.46f, 0.52f, 0.72f);
                    a.glowColor = new Color(2.4f, 0.5f, 0.6f);
                    a.scaleMultiplier = 1.7f;
                    a.unlockWave = 8;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 9f;
                    a.shieldFraction = 0.6f;
                    a.damageMultiplier = 1.2f;
                    a.speedMultiplier = 1f;
                    a.ranged = true;
                    a.attackRange = 50f;
                    a.preferredRangedDistance = 32f;
                    a.shotsPerAttack = 2;
                    a.burstInterval = 0.5f;
                    a.rangedDamageMultiplier = 1.1f;
                    a.attackCooldown = 2.4f;
                    a.attackWindup = 0.7f;
                    a.rangedSpread = 1f;
                    a.strafeAmount = 0.5f;

                    // The exception to "nothing makes a boss give ground", and the whole
                    // character of the fight. Suppression sends her to a new roof; the
                    // player has to go and find her rather than out-trade her.
                    a.canRetreat = true;
                    a.suppressionDamage = 90f;
                    a.staggerThreshold = 0f;
                    a.scoreValue = 5200;
                    a.healthDropChance = 1f;
                    break;

                // The works. The longest reach in the game on the arena with the least
                // room, which is the joke the fairground is built on.
                case "Dev Auger":
                    a.description = "Boss. Enormous reach in a place with nowhere to stand.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.16f, 0.20f, 0.14f);
                    a.headColor = new Color(0.38f, 0.50f, 0.36f);
                    a.glowColor = new Color(0.5f, 2.2f, 0.8f);
                    a.scaleMultiplier = 2.4f;
                    a.unlockWave = 9;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 13f;
                    a.shieldFraction = 0.6f;
                    a.damageMultiplier = 2.3f;
                    a.speedMultiplier = 0.75f;
                    a.attackRange = 5f;
                    a.meleeRange = 5.2f;
                    a.attackCooldown = 1.8f;
                    a.attackWindup = 0.75f;
                    a.chargeSpeedMultiplier = 2f;
                    a.strafeAmount = 0.15f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 5600;
                    a.healthDropChance = 1f;
                    break;

                // The colony. More armour than anything else in the campaign, and the
                // last zone, so it is the one fight that is genuinely a war of attrition.
                case "Roan Auger":
                    a.description = "Boss. Sealed, patient, and very hard to finish.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.40f, 0.20f, 0.14f);
                    a.headColor = new Color(0.72f, 0.42f, 0.26f);
                    a.glowColor = new Color(2.6f, 0.8f, 0.3f);
                    a.scaleMultiplier = 2.1f;
                    a.unlockWave = 10;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 14f;
                    a.shieldFraction = 1.4f;
                    a.damageMultiplier = 1.5f;
                    a.speedMultiplier = 0.7f;
                    a.ranged = true;
                    a.attackRange = 30f;
                    a.preferredRangedDistance = 16f;
                    a.shotsPerAttack = 3;
                    a.burstInterval = 0.1f;
                    a.attackCooldown = 2f;
                    a.attackWindup = 0.6f;
                    a.rangedSpread = 3f;
                    a.rangedDamageMultiplier = 0.55f;
                    a.meleeRange = 4f;
                    a.strafeAmount = 0.25f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 6000;
                    a.healthDropChance = 1f;
                    break;

                // ----------------------------------------------------------
                // The last one, and the only one who never held a site.
                //
                // <b>She does everything the other six do, one at a time.</b> That is the
                // design rather than a bigger number: she has the Warden's reach, the
                // Harbinger's burst, Aurel's charge and Ilsa's refusal to stand still, so
                // the fight is every lesson the campaign taught being asked for again in
                // one room. The room is eighty metres of house, which is the other half
                // of it -- six arenas taught the player that backing off works, and this
                // is the level with nowhere to back off to.
                //
                // Half again the health of anything else and the deepest armour in the
                // game, because she arrives with the largest escort in the game as well
                // and the player has to get through it repeatedly to reach her.
                case "Marit Auger":
                    a.description = "Boss. Everything the other seven did, in a house with no room in it.";
                    a.role = EnemyArchetype.Role.Boss;
                    a.voice = EnemyVoice.Kind.Human;
                    a.woundResistance = 1.0f;
                    a.bodyColor = new Color(0.10f, 0.09f, 0.13f);
                    a.headColor = new Color(0.78f, 0.66f, 0.38f);
                    a.glowColor = new Color(3.0f, 2.2f, 0.6f);
                    a.scaleMultiplier = 2.3f;
                    a.unlockWave = 12;
                    a.baseWeight = 1f;
                    a.healthMultiplier = 20f;
                    a.shieldFraction = 1.5f;
                    a.damageMultiplier = 1.9f;

                    // 3.4 m/s, under the player's walk. Even here -- the whole roster is
                    // written so that disengaging stays possible, and the thing that
                    // makes this fight hard is the house, not an enemy that cannot be
                    // walked away from.
                    a.speedMultiplier = 1.05f;
                    a.ranged = true;
                    a.attackRange = 32f;
                    a.preferredRangedDistance = 14f;
                    a.shotsPerAttack = 4;
                    a.burstInterval = 0.09f;
                    a.attackCooldown = 1.7f;
                    a.attackWindup = 0.5f;
                    a.rangedSpread = 3.2f;
                    a.rangedDamageMultiplier = 0.6f;

                    // And she swings if you close, so standing inside her muzzle is not
                    // the safe place it is against a pure shooter.
                    a.meleeRange = 4.4f;
                    a.chargeSpeedMultiplier = 2.2f;
                    a.strafeAmount = 0.45f;
                    a.staggerThreshold = 0f;
                    a.canRetreat = false;
                    a.scoreValue = 10000;
                    a.healthDropChance = 1f;
                    a.shieldDropChance = 1f;
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
