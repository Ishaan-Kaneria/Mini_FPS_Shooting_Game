#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the store: the gun, bomb and consumable assets it sells, and the
    /// <see cref="StoreCatalog"/> that prices them.
    ///
    /// The same shape as <see cref="FPSKitEnemyRoster"/>, <see cref="FPSKitThemes"/> and
    /// <see cref="FPSKitLevels"/>: content lives in assets, not in code. A seventh gun is
    /// a duplicated <see cref="WeaponData"/> and an entry in the catalog, and nothing in
    /// <see cref="StorePanel"/> has to change for it -- the store clones a card per entry
    /// at runtime the way the dashboard clones a card per arena.
    ///
    /// The prices here are written against the coin rates on <see cref="GameDirector"/>.
    /// They are one half of a pair, so moving either without the other is what turns a
    /// shop into either a grind or a free lunch: at three coins a kill, sixty a star and
    /// a point-based bonus, a well-played level pays roughly three hundred, and the
    /// whole catalogue is somewhere near fifty of them.
    ///
    /// The generated assets are created once and then left alone, exactly like the enemy
    /// roster -- so retuning a price here does **not** reach the asset the game reads
    /// until <see cref="ResetAll"/> runs. FPSKitBatch.ResetStore is the way in.
    /// </summary>
    public static class FPSKitStore
    {
        public const string StoreFolder = "Assets/FPSKit_Generated/Store";
        public const string CatalogPath = StoreFolder + "/Store.asset";

        /// <summary>
        /// The starter rifle lives where it always has, so a scene built before the store
        /// existed keeps pointing at the same asset.
        /// </summary>
        public const string StarterRiflePath = "Assets/FPSKit_Generated/TestRifle.asset";

        // ==================================================================
        [MenuItem("FPSKit/Create Store", false, 46)]
        public static void CreateMenu()
        {
            var catalog = GetOrCreate();

            EditorUtility.DisplayDialog("FPSKit Store",
                $"The store is ready in:\n{StoreFolder}\n\n" +
                $"{catalog.guns.Count} guns, {catalog.bombs.Count} explosives and " +
                $"{catalog.consumables.Count} supplies.\n\n" +
                "Select the catalog to retune prices, upgrade curves and what one " +
                "upgrade buys. Duplicate a WeaponData or BombData to add stock.", "OK");

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = catalog;
        }

        [MenuItem("FPSKit/Reset Store to Defaults", false, 47)]
        public static void ResetMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset the store",
                "Re-applies the built-in stock, prices and upgrade curves.\n\n" +
                "Any tuning you did in the Inspector will be lost. Coins the player has " +
                "earned and items they have bought are not touched.", "Reset it", "Cancel")) return;

            ResetAll();
        }

        /// <summary>Re-stamps the built-in stock and prices, with no dialog.</summary>
        public static void ResetAll()
        {
            var catalog = GetOrCreate();

            Configure(catalog, resetAssets: true);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // Configure writes the numbers; it does not know about prefabs, audio clips
            // or the impact library, which the scene builder owns and stamps on. Without
            // this the reset hands back a catalogue whose bombs spawn nothing and whose
            // every sound is missing, and the game stays that way until somebody happens
            // to rebuild an arena.
            FPSKitSceneBuilder.StampStore();

            Debug.Log($"<color=lime>[FPSKit]</color> Store reset: {catalog.guns.Count} guns, " +
                      $"{catalog.bombs.Count} explosives, {catalog.consumables.Count} supplies.");
        }

        /// <summary>
        /// The catalog, created on first use and then left alone. This is what the scene
        /// builder and the dashboard builder both call.
        /// </summary>
        public static StoreCatalog GetOrCreate()
        {
            EnsureFolders();

            var existing = AssetDatabase.LoadAssetAtPath<StoreCatalog>(CatalogPath);
            if (existing != null) return existing;

            var catalog = ScriptableObject.CreateInstance<StoreCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);

            Configure(catalog, resetAssets: true);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<StoreCatalog>(CatalogPath);
        }

        // ==================================================================
        // The stock
        //
        // Six guns spanning the shapes a shooter has -- one of everything rather than
        // six of the same gun at rising damage, because a store whose upgrades are the
        // only real difference between items is a store with one item in it.
        //
        // The starter rifle is free and owned from the start. That is not generosity:
        // a store with nothing owned would leave a new player unable to take anything
        // into a level, and that failure looks like a broken arena rather than like an
        // empty wallet.
        // ==================================================================
        static void Configure(StoreCatalog catalog, bool resetAssets)
        {
            catalog.guns.Clear();
            catalog.bombs.Clear();
            catalog.consumables.Clear();

            // ---- guns ----------------------------------------------------
            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "rifle",
                displayName = "Service Rifle",
                description = "Steady, forgiving, free.",
                data = StarterRifle(resetAssets),
                price = 0,
                ownedFromStart = true,
                upgrades = Curve(300, 1.6f),
                damagePerLevel = 0.12f,
                fireRatePerLevel = 0.09f,
                magazinePerLevel = 3,
                reloadPerLevel = 0.05f
            });

            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "smg",
                displayName = "Vector SMG",
                description = "Very fast, very close. The crowd gun.",
                data = Gun(resetAssets, "SMG", g =>
                {
                    g.weaponName = "Vector SMG";
                    g.fireMode = FireMode.Auto;
                    g.roundsPerMinute = 900f;
                    g.damage = 15f;
                    g.maxRange = 90f;
                    g.magazineSize = 40;
                    g.reloadTime = 1.7f;
                    g.falloffStart = 18f;
                    g.falloffEnd = 60f;
                    g.minDamageFactor = 0.4f;
                    g.baseSpread = 1.1f;
                    g.spreadPerShot = 0.38f;
                    g.maxSpread = 6.5f;
                    g.recoilVertical = 0.7f;
                    g.recoilHorizontal = 0.5f;
                    g.cameraShake = 0.1f;
                }),
                price = 1400,
                upgrades = Curve(320, 1.6f),
                damagePerLevel = 0.13f,
                fireRatePerLevel = 0.06f,
                magazinePerLevel = 5,
                reloadPerLevel = 0.06f
            });

            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "shotgun",
                displayName = "Breacher",
                description = "Nine pellets. Devastating up close, useless past twenty metres.",
                data = Gun(resetAssets, "Shotgun", g =>
                {
                    g.weaponName = "Breacher";
                    g.fireMode = FireMode.Single;
                    g.roundsPerMinute = 85f;
                    g.damage = 13f;
                    g.pelletsPerShot = 9;
                    g.maxRange = 60f;
                    g.magazineSize = 7;
                    g.reloadTime = 2.9f;
                    g.falloffStart = 8f;
                    g.falloffEnd = 26f;
                    g.minDamageFactor = 0.2f;
                    g.baseSpread = 3.4f;
                    g.spreadPerShot = 0.6f;
                    g.maxSpread = 7f;
                    g.recoilVertical = 3.4f;
                    g.recoilHorizontal = 0.9f;
                    g.cameraShake = 0.5f;
                    g.impactForce = 90f;
                }),
                price = 2600,
                upgrades = Curve(360, 1.62f),
                damagePerLevel = 0.14f,
                fireRatePerLevel = 0.1f,
                magazinePerLevel = 1,
                reloadPerLevel = 0.07f
            });

            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "burst",
                displayName = "Tactical Burst",
                description = "Three tight rounds a pull. Rewards picking your shots.",
                data = Gun(resetAssets, "Burst", g =>
                {
                    g.weaponName = "Tactical Burst";
                    g.fireMode = FireMode.Burst;
                    g.burstCount = 3;
                    g.burstInterval = 0.06f;
                    g.roundsPerMinute = 340f;
                    g.damage = 26f;
                    g.maxRange = 150f;
                    g.magazineSize = 30;
                    g.reloadTime = 2f;
                    g.baseSpread = 0.35f;
                    g.spreadPerShot = 0.3f;
                    g.maxSpread = 3.2f;
                    g.recoilVertical = 1f;
                    g.recoilHorizontal = 0.25f;
                    g.cameraShake = 0.16f;
                }),
                price = 3400,
                upgrades = Curve(380, 1.62f),
                damagePerLevel = 0.13f,
                fireRatePerLevel = 0.08f,
                magazinePerLevel = 3,
                reloadPerLevel = 0.05f
            });

            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "dmr",
                displayName = "Marksman DMR",
                description = "One heavy round, the length of the arena.",
                data = Gun(resetAssets, "DMR", g =>
                {
                    g.weaponName = "Marksman DMR";
                    g.fireMode = FireMode.Single;
                    g.roundsPerMinute = 200f;
                    g.damage = 62f;
                    g.maxRange = 260f;
                    g.magazineSize = 12;
                    g.reloadTime = 2.4f;
                    g.falloffStart = 90f;
                    g.falloffEnd = 220f;
                    g.minDamageFactor = 0.8f;
                    g.baseSpread = 0.12f;
                    g.spreadPerShot = 0.8f;
                    g.maxSpread = 4.5f;
                    g.spreadRecovery = 7f;
                    g.recoilVertical = 2.2f;
                    g.recoilHorizontal = 0.3f;
                    g.adsFieldOfView = 26f;
                    g.cameraShake = 0.3f;
                }),
                price = 4200,
                upgrades = Curve(420, 1.65f),
                damagePerLevel = 0.12f,
                fireRatePerLevel = 0.11f,
                magazinePerLevel = 2,
                reloadPerLevel = 0.05f
            });

            catalog.guns.Add(new StoreCatalog.GunEntry
            {
                id = "lmg",
                displayName = "Suppressor LMG",
                description = "A hundred rounds, and a reload you will regret.",
                data = Gun(resetAssets, "LMG", g =>
                {
                    g.weaponName = "Suppressor LMG";
                    g.fireMode = FireMode.Auto;
                    g.roundsPerMinute = 760f;
                    g.damage = 21f;
                    g.maxRange = 170f;
                    g.magazineSize = 100;
                    g.reloadTime = 4.2f;
                    g.falloffStart = 45f;
                    g.falloffEnd = 130f;
                    g.minDamageFactor = 0.6f;
                    g.baseSpread = 1.5f;
                    g.spreadPerShot = 0.32f;
                    g.maxSpread = 7.5f;
                    g.spreadRecovery = 4.5f;
                    g.recoilVertical = 1f;
                    g.recoilHorizontal = 0.6f;
                    g.movementSpreadPenalty = 2.4f;
                    g.cameraShake = 0.2f;
                }),
                price = 5600,
                upgrades = Curve(460, 1.65f),
                damagePerLevel = 0.12f,
                fireRatePerLevel = 0.07f,
                magazinePerLevel = 10,
                reloadPerLevel = 0.08f
            });

            // ---- explosives ----------------------------------------------
            //
            // The frag is not owned from the start and is not for sale either: it is the
            // campaign's first reward, handed over by whichever zone of `CampaignData`
            // names it in `grantsPower`. That is a
            // change of mind and the reason is worth keeping. It used to be owned from
            // the start, for the same reason the rifle is -- the aiming ring is the most
            // interesting control in the game, and a player who has to save four hundred
            // coins before they ever see one is a player who does not know it exists.
            // Both of those stay true; what the story adds is a better answer than the
            // shop to "how did I get this", and a first power whose arrival is an event.
            //
            // `grantedByStory` rather than a hard-coded id, so which explosive the story
            // gives is a decision made here, in the content, and not in `Campaign`.
            catalog.bombs.Add(new StoreCatalog.BombEntry
            {
                id = "frag",
                displayName = "Frag Bomb",
                description = "The standard charge. The ring is what it hurts.",
                data = Bomb(resetAssets, "Frag", b =>
                {
                    b.bombName = "Frag Bomb";
                    b.damage = 120f;
                    b.radius = 7f;
                    b.edgeDamageFraction = 0.25f;
                    b.falloffPower = 1.6f;
                    b.chargesPerLevel = 2;
                    b.blastColor = new Color(1f, 0.55f, 0.15f);
                }),
                price = 0,
                ownedFromStart = false,
                grantedByStory = true,
                upgrades = Curve(350, 1.6f),
                damagePerLevel = 0.15f,
                radiusPerLevel = 0.7f,
                upgradesPerExtraCharge = 2
            });

            catalog.bombs.Add(new StoreCatalog.BombEntry
            {
                id = "cluster",
                displayName = "Cluster Charge",
                description = "Half the punch over twice the ground.",
                data = Bomb(resetAssets, "Cluster", b =>
                {
                    b.bombName = "Cluster Charge";
                    b.damage = 85f;
                    b.radius = 12f;
                    b.edgeDamageFraction = 0.55f;
                    b.falloffPower = 1f;
                    b.chargesPerLevel = 3;
                    b.maxRange = 40f;
                    b.explosionForce = 700f;
                    b.blastColor = new Color(0.55f, 0.85f, 1f);
                }),
                price = 1800,
                upgrades = Curve(380, 1.6f),
                damagePerLevel = 0.14f,
                radiusPerLevel = 0.9f,
                upgradesPerExtraCharge = 3
            });

            catalog.bombs.Add(new StoreCatalog.BombEntry
            {
                id = "thermite",
                displayName = "Thermite Shell",
                description = "Everything it has, in four metres. Land it on the boss.",
                data = Bomb(resetAssets, "Thermite", b =>
                {
                    b.bombName = "Thermite Shell";
                    b.damage = 340f;
                    b.radius = 4.5f;
                    b.edgeDamageFraction = 0.1f;
                    b.falloffPower = 2.6f;
                    b.chargesPerLevel = 1;
                    b.selfDamageFraction = 0.6f;
                    b.minRange = 8f;
                    b.explosionForce = 1400f;
                    b.cameraShake = 1f;
                    b.blastColor = new Color(1f, 0.92f, 0.5f);
                }),
                price = 3200,
                upgrades = Curve(500, 1.7f),
                damagePerLevel = 0.16f,
                radiusPerLevel = 0.4f,
                upgradesPerExtraCharge = 3
            });

            // ---- supplies ------------------------------------------------
            catalog.consumables.Add(new StoreCatalog.ConsumableEntry
            {
                id = "energy",
                data = Consumable(resetAssets, "EnergyDrink", c =>
                {
                    c.displayName = "Energy Drink";
                    c.description = "Topped up, then twelve seconds of everything faster.";
                    c.healthRestore = 55f;
                    c.shieldRestore = 50f;
                    c.boostDuration = 12f;
                    c.moveSpeedMultiplier = 1.3f;
                    c.fireRateMultiplier = 1.35f;
                    c.reloadTimeMultiplier = 0.7f;
                    c.tint = new Color(0.4f, 0.9f, 1f);
                }),
                price = 220,
                packSize = 3,
                maxCarried = 9
            });

            catalog.consumables.Add(new StoreCatalog.ConsumableEntry
            {
                id = "adrenaline",
                data = Consumable(resetAssets, "Adrenaline", c =>
                {
                    c.displayName = "Adrenaline Shot";
                    c.description = "Almost all your health, and a short violent rush.";
                    c.healthRestore = 95f;
                    c.shieldRestore = 0f;
                    c.boostDuration = 7f;
                    c.moveSpeedMultiplier = 1.55f;
                    c.fireRateMultiplier = 1.6f;
                    c.reloadTimeMultiplier = 0.55f;
                    c.tint = new Color(1f, 0.55f, 0.6f);
                }),
                price = 420,
                packSize = 2,
                maxCarried = 6
            });

            // ---- health --------------------------------------------------
            //
            // Uncapped, and the only thing in the shop that is. It is what a player who
            // is stuck on a level can always put coins into, so being stuck is never a
            // wall with nothing to do about it -- and the 1.45 growth is what stops that
            // being a free pass.
            catalog.healthUpgrades = Curve(250, 1.45f, maxLevel: 0);
            catalog.healthPerLevel = 12f;
            catalog.shieldPerLevel = 5f;
        }

        static StoreCatalog.UpgradeCurve Curve(int basePrice, float growth, int maxLevel = 5)
            => new StoreCatalog.UpgradeCurve
            {
                basePrice = basePrice,
                growth = growth,
                maxLevel = maxLevel
            };

        // ==================================================================
        // Assets
        // ==================================================================

        /// <summary>
        /// The rifle the scene builder already makes. Loaded rather than recreated, so
        /// the store and the builder cannot end up pointing at two different rifles --
        /// and so a project that tuned TestRifle.asset keeps that tuning.
        /// </summary>
        static WeaponData StarterRifle(bool resetAssets)
        {
            var existing = AssetDatabase.LoadAssetAtPath<WeaponData>(StarterRiflePath);
            if (existing != null) return existing;

            // Built here when the scene builder has not run yet, with the same numbers
            // it would have used. The builder stamps the audio and VFX on afterwards.
            var data = ScriptableObject.CreateInstance<WeaponData>();
            data.weaponName = "Service Rifle";
            data.fireMode = FireMode.Auto;
            data.roundsPerMinute = 650f;
            data.damage = 24f;
            data.maxRange = 200f;
            data.magazineSize = 30;
            data.reserveAmmo = 150;
            data.reloadTime = 2.1f;
            data.infiniteReserve = true;

            AssetDatabase.CreateAsset(data, StarterRiflePath);
            return AssetDatabase.LoadAssetAtPath<WeaponData>(StarterRiflePath);
        }

        static WeaponData Gun(bool resetAssets, string fileName, System.Action<WeaponData> configure)
        {
            string path = $"{StoreFolder}/Gun_{fileName}.asset";

            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            bool created = data == null;

            if (created)
            {
                data = ScriptableObject.CreateInstance<WeaponData>();
                AssetDatabase.CreateAsset(data, path);
            }

            // Re-stamped only on a reset, so Inspector tuning survives an ordinary build.
            if (created || resetAssets)
            {
                // Started from a fresh instance's defaults rather than from whatever is
                // on disk: a reset that only wrote the fields Configure mentions would
                // leave a field somebody changed in the Inspector standing, which is not
                // what "reset to defaults" means.
                //
                // CopySerialized copies m_Name with everything else, and the name of a
                // throwaway instance is empty -- so the asset comes back nameless, and
                // anything that identifies a WeaponData by name is quietly comparing two
                // empty strings. Put back explicitly.
                EditorUtility.CopySerialized(ScriptableObject.CreateInstance<WeaponData>(), data);
                data.name = $"Gun_{fileName}";

                data.infiniteReserve = true;
                configure(data);

                EditorUtility.SetDirty(data);
            }

            return AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        }

        static BombData Bomb(bool resetAssets, string fileName, System.Action<BombData> configure)
        {
            string path = $"{StoreFolder}/Bomb_{fileName}.asset";

            var data = AssetDatabase.LoadAssetAtPath<BombData>(path);
            bool created = data == null;

            if (created)
            {
                data = ScriptableObject.CreateInstance<BombData>();
                AssetDatabase.CreateAsset(data, path);
            }

            if (created || resetAssets)
            {
                // The prefabs are the scene builder's and are not part of the tuning, so
                // they are carried across a reset rather than blanked -- otherwise every
                // reset would leave every bomb unable to spawn until the next full build.
                var keptBomb = data.bombPrefab;
                var keptBlast = data.explosionPrefab;

                // See the gun above: CopySerialized takes the name with it.
                EditorUtility.CopySerialized(ScriptableObject.CreateInstance<BombData>(), data);
                data.name = $"Bomb_{fileName}";

                configure(data);

                data.bombPrefab = keptBomb;
                data.explosionPrefab = keptBlast;

                EditorUtility.SetDirty(data);
            }

            return AssetDatabase.LoadAssetAtPath<BombData>(path);
        }

        static ConsumableData Consumable(bool resetAssets, string fileName,
                                         System.Action<ConsumableData> configure)
        {
            string path = $"{StoreFolder}/Item_{fileName}.asset";

            var data = AssetDatabase.LoadAssetAtPath<ConsumableData>(path);
            bool created = data == null;

            if (created)
            {
                data = ScriptableObject.CreateInstance<ConsumableData>();
                AssetDatabase.CreateAsset(data, path);
            }

            if (created || resetAssets)
            {
                var keptClip = data.useClip;

                // See the gun above: CopySerialized takes the name with it.
                EditorUtility.CopySerialized(ScriptableObject.CreateInstance<ConsumableData>(), data);
                data.name = $"Item_{fileName}";

                configure(data);

                data.useClip = keptClip;

                EditorUtility.SetDirty(data);
            }

            return AssetDatabase.LoadAssetAtPath<ConsumableData>(path);
        }

        // ==================================================================
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(StoreFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Store");
        }
    }
}
#endif
