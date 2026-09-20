#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Plays the economy end to end: coins into the store, a gun and an upgrade out of
    /// it, and both of them in the player's hands in the next level.
    ///
    /// This is the regression test for a class of failure that compiles perfectly and is
    /// invisible until somebody complains. A purchase is four things in four different
    /// files -- coins taken from the <see cref="Wallet"/>, ownership written to
    /// <see cref="Loadout"/>, the selection read back by <see cref="PlayerLoadout"/>, and
    /// the multipliers composed with <see cref="PlayerProgression"/>'s. Break any one
    /// join and the store still looks like it worked: the coins go, the card says OWNED,
    /// and the player walks into the level holding the gun they started with.
    ///
    /// It also throws a real bomb at a real enemy, because the aiming ring is a promise
    /// about damage and the only honest way to check a promise is to collect on it.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyStore
    /// </summary>
    public static class FPSKitStoreTest
    {
        const double HardTimeout = 420.0;

        /// <summary>Coins the test gives itself. Every price in the catalogue fits inside it.</summary>
        const int TestFunds = 60000;

        /// <summary>The gun bought. Anything not owned from the start would do.</summary>
        const string GunId = "smg";

        const string ItemId = "energy";

        enum Phase
        {
            Enter, OpenStore, InspectStore, Buy, Upgrade, BuyHealth, BuyItem,
            CloseStore, Launch, AwaitArena, InspectLoadout,
            AwaitEnemies, ThrowBomb, AwaitBlast, KillOne, Judge
        }

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        // ---- what the store did ----
        static int _cardsShown;
        static string _unclickable = "";
        static int _balanceBeforeGun, _balanceAfterGun;
        static bool _gunOwned;
        static int _firstUpgradePrice, _secondUpgradePrice;
        static int _gunLevel;
        static int _healthLevel;
        static int _stockBought;

        // ---- what reached the player ----
        static string _equippedWeapon = "";
        static string _expectedWeapon = "";
        static float _fireRateMultiplier = -1f;
        static float _maxHealth = -1f;
        static float _baseMaxHealth = -1f;
        static int _bombCharges = -1;
        static int _beltCount = -1;

        // ---- what the bomb did ----
        //
        // Measured on one named enemy rather than on the arena's total, which was the
        // first thing tried and was simply wrong: a level is still filling while the
        // bomb is in the air, so the total health standing goes *up* across a throw that
        // worked perfectly. Tracking one body cannot be fooled by arrivals.
        static Health _tracked;
        static float _trackedPoolBefore = -1f;
        static float _trackedPoolAfter = -1f;
        static bool _trackedDied;
        static int _chargesBeforeThrow = -1;
        static int _chargesAfterThrow = -1;

        static int _coinsBeforeKill = -1;
        static int _coinsAfterKill = -1;

        static string _arenaScene = "";

        /// <summary>Every PlayerPrefs key the test writes, put back in Detach.</summary>
        static readonly string[] IntKeys =
        {
            "FPSKit.Coins", "FPSKit.CoinsEarned",
            "FPSKit.Own.Gun." + GunId, "FPSKit.Upgrade.Gun." + GunId,
            "FPSKit.Upgrade.Health", "FPSKit.Stock." + ItemId,
            "FPSKit.Runs", "FPSKit.TotalKills", "FPSKit.BestScore",
            "FPSKit.Level.IndustrialWarehouse.0.Stars",
            "FPSKit.Level.IndustrialWarehouse.0.Score",

            // The campaign's bomb unlock. This test throws a real bomb, and the player no
            // longer starts with one -- it is the story's first reward now -- so the test
            // has to grant it the way the campaign would. Backed up with everything else,
            // because a check that leaves a power switched on in the developer's own
            // profile has changed the game to pass.
            "FPSKit.Campaign.Power." + Campaign.BombPower,
            "FPSKit.Own.Bomb.frag"
        };

        static readonly string[] StringKeys =
        {
            "FPSKit.Loadout.Gun", "FPSKit.Loadout.Bomb", "FPSKit.Loadout.Item"
        };

        static readonly Dictionary<string, int> IntBackup = new Dictionary<string, int>();
        static readonly Dictionary<string, string> StringBackup = new Dictionary<string, string>();

        // ==================================================================
        public static void VerifyStore()
        {
            try
            {
                if (!System.IO.File.Exists(FPSKitMenuBuilder.MenuScenePath))
                    throw new Exception($"{FPSKitMenuBuilder.MenuScenePath} does not exist. " +
                                        "Run FPSKitBatch.BuildDashboard first.");

                EditorSceneManager.OpenScene(FPSKitMenuBuilder.MenuScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();

                _cardsShown = 0;
                _unclickable = _equippedWeapon = _expectedWeapon = _arenaScene = "";
                _balanceBeforeGun = _balanceAfterGun = -1;
                _gunOwned = false;
                _firstUpgradePrice = _secondUpgradePrice = _gunLevel = _healthLevel = _stockBought = -1;
                _fireRateMultiplier = _maxHealth = _baseMaxHealth = -1f;
                _bombCharges = _beltCount = -1;
                _tracked = null;
                _trackedPoolBefore = _trackedPoolAfter = -1f;
                _trackedDied = false;
                _chargesBeforeThrow = _chargesAfterThrow = -1;
                _coinsBeforeKill = _coinsAfterKill = -1;

                BackUp();
                StartFromNothing();

                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] store test: coins in, a gun and an upgrade out, " +
                          "and both of them in hand in the next level");
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Puts the profile in a known state: funded, owning nothing it is about to buy.
        /// Without this, a machine where somebody already owns the SMG would pass the
        /// "it is owned afterwards" assertion without buying anything.
        /// </summary>
        static void StartFromNothing()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StoreCatalog>(FPSKitStore.CatalogPath);
            if (catalog != null) Loadout.Clear(catalog);

            // "From nothing" means owning nothing that was bought -- not being earlier in
            // the story than this test is about. The player no longer starts with a bomb,
            // it is the campaign's first reward, and this test throws a real one; so the
            // power is granted back here, after the wipe, where nothing clears it again.
            // Granting it directly rather than awarding eighteen stars keeps this a store
            // test rather than a progression one.
            Campaign.GrantPower(Campaign.BombPower);

            var storyBomb = catalog != null ? catalog.StoryBomb : null;
            if (storyBomb != null)
            {
                Loadout.GrantBomb(storyBomb.id);
                Loadout.SelectBomb(storyBomb.id);
            }

            Wallet.SetBalance(TestFunds);
            PlayerPrefs.Save();
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"store test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        Wait(1.5, Phase.OpenStore);
                        return;

                    case Phase.OpenStore:
                    {
                        if (Waiting()) return;

                        var menu = Menu();
                        if (menu == null) throw new Exception("the dashboard has no MainMenuController");
                        if (menu.store == null) throw new Exception("the dashboard has no store");

                        // Through the same call the Store button is wired to.
                        menu.OpenStore();

                        // A frame before anything is raycast at it: a graphic enabled this
                        // frame is not in the canvas yet.
                        Wait(0.5, Phase.InspectStore);
                        return;
                    }

                    case Phase.InspectStore:
                    {
                        if (Waiting()) return;

                        var store = Menu().store;
                        if (!store.IsOpen) throw new Exception("the Store button did not open the store");

                        _cardsShown = store.CardCount;
                        if (_cardsShown == 0) throw new Exception("the store has nothing for sale");

                        _unclickable = FPSKitFlowTest.UnclickableButtonIn(Menu());

                        Notes.Append($"\n  store: {_cardsShown} cards on the weapons shelf, " +
                                     $"{Wallet.Format(Wallet.Balance)} coins");

                        _phase = Phase.Buy;
                        return;
                    }

                    case Phase.Buy:
                    {
                        var gun = Catalog().FindGun(GunId);
                        if (gun == null) throw new Exception($"the store has no gun called \"{GunId}\"");

                        _expectedWeapon = gun.data != null ? gun.data.name : "";
                        _balanceBeforeGun = Wallet.Balance;

                        // Clicked, not called. The purchase path is the button's listener,
                        // and a test that called the method behind it would pass on a card
                        // whose button was never wired to anything.
                        Click(FindCard(gun.Label), primary: true);

                        _gunOwned = Loadout.OwnsGun(gun);
                        _balanceAfterGun = Wallet.Balance;

                        Notes.Append($"\n  bought {gun.Label} for {gun.price}: owned={_gunOwned}, " +
                                     $"balance {_balanceBeforeGun} -> {_balanceAfterGun}");

                        _phase = Phase.Upgrade;
                        return;
                    }

                    case Phase.Upgrade:
                    {
                        var gun = Catalog().FindGun(GunId);

                        _firstUpgradePrice = gun.upgrades.PriceAt(0);
                        Click(FindCard(gun.Label), primary: false);

                        _secondUpgradePrice = gun.upgrades.PriceAt(1);
                        Click(FindCard(gun.Label), primary: false);

                        _gunLevel = Loadout.GunUpgradeLevel(GunId);

                        Notes.Append($"\n  upgraded twice: level {_gunLevel}, prices " +
                                     $"{_firstUpgradePrice} then {_secondUpgradePrice}");

                        _phase = Phase.BuyHealth;
                        return;
                    }

                    case Phase.BuyHealth:
                    {
                        var store = Menu().store;
                        store.ShowTab(StorePanel.Tab.Health);

                        Click(FindCard("Vitality"), primary: true);
                        _healthLevel = Loadout.HealthUpgradeLevel;

                        Notes.Append($"\n  health upgraded to level {_healthLevel}");

                        _phase = Phase.BuyItem;
                        return;
                    }

                    case Phase.BuyItem:
                    {
                        var store = Menu().store;
                        store.ShowTab(StorePanel.Tab.Items);

                        var item = Catalog().FindConsumable(ItemId);
                        if (item == null) throw new Exception($"the store has no item called \"{ItemId}\"");

                        Click(FindCard(item.Label), primary: true);
                        _stockBought = Loadout.Stock(ItemId);

                        Notes.Append($"\n  bought a pack: {_stockBought} on the belt");

                        _phase = Phase.CloseStore;
                        return;
                    }

                    case Phase.CloseStore:
                    {
                        Menu().store.Close();
                        Wait(0.5, Phase.Launch);
                        return;
                    }

                    case Phase.Launch:
                    {
                        if (Waiting()) return;

                        var menu = Menu();
                        var entry = FirstPlayable(menu);
                        if (entry == null) throw new Exception("no arena in the catalog can be loaded");

                        _arenaScene = entry.sceneName;
                        menu.Launch(entry, 0);

                        Wait(14.0, Phase.AwaitArena);
                        return;
                    }

                    case Phase.AwaitArena:
                    {
                        var active = SceneManager.GetActiveScene();
                        if (active.name != _arenaScene)
                        {
                            if (Waiting()) return;
                            throw new Exception($"the dashboard never loaded \"{_arenaScene}\"");
                        }

                        // Let the loadout, the progression and the level manager settle.
                        Wait(3.0, Phase.InspectLoadout);
                        return;
                    }

                    case Phase.InspectLoadout:
                    {
                        if (Waiting()) return;

                        var weapon = UnityEngine.Object.FindAnyObjectByType<Weapon>();
                        var loadout = UnityEngine.Object.FindAnyObjectByType<PlayerLoadout>();
                        var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
                        var belt = UnityEngine.Object.FindAnyObjectByType<ConsumableBelt>();

                        if (weapon == null) throw new Exception("the arena has no Weapon");
                        if (loadout == null) throw new Exception("the player has no PlayerLoadout");

                        _equippedWeapon = weapon.data != null ? weapon.data.name : "";
                        _fireRateMultiplier = weapon.FireRateMultiplier;

                        var health = loadout.health;
                        _maxHealth = health != null ? health.maxHealth : -1f;
                        _baseMaxHealth = loadout.baseHealth;

                        _bombCharges = bombs != null ? bombs.charges : -1;
                        _beltCount = belt != null ? belt.Count : -1;

                        Notes.Append($"\n  in the arena: holding \"{_equippedWeapon}\" at fire rate " +
                                     $"x{_fireRateMultiplier:0.00}, {_maxHealth:0} health " +
                                     $"(base {_baseMaxHealth:0}), {_bombCharges} bombs, " +
                                     $"{_beltCount} drinks");

                        Wait(25.0, Phase.AwaitEnemies);
                        return;
                    }

                    case Phase.AwaitEnemies:
                    {
                        var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
                        if (bombs == null || bombs.data == null)
                            throw new Exception("the player has no bomb to throw");

                        // Waited for one that is actually inside the bomb's range. A
                        // target further away than the arc can reach is clamped back to
                        // maximum range and the blast lands somewhere else entirely,
                        // which is correct behaviour and a useless thing to measure.
                        if (InRangeEnemy(bombs) == null)
                        {
                            if (Waiting()) return;
                            throw new Exception("no enemy came inside the bomb's range, so the " +
                                                "bomb had nothing to prove");
                        }

                        _phase = Phase.ThrowBomb;
                        return;
                    }

                    case Phase.ThrowBomb:
                    {
                        var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
                        if (bombs == null || bombs.data == null)
                            throw new Exception("the player has no bomb to throw");

                        _tracked = InRangeEnemy(bombs);
                        if (_tracked == null) throw new Exception("no enemy to aim at");

                        // Aimed at one enemy's feet. It walks a few metres during the
                        // one-second fall and the blast radius is several times that, so
                        // it is still inside the ring when the bomb arrives.
                        Vector3 target = _tracked.transform.position;

                        _trackedPoolBefore = _tracked.Current + _tracked.Shield;
                        _chargesBeforeThrow = bombs.charges;

                        // Thrown through the public call the release path uses, so the
                        // flight, the sweep and the blast are all the real ones.
                        bombs.Throw(bombs.transform.position + Vector3.up * 1.5f, target);

                        _chargesAfterThrow = bombs.charges;

                        Notes.Append($"\n  threw a bomb at an enemy on {_trackedPoolBefore:0} " +
                                     $"health, {Vector3.Distance(bombs.transform.position, target):0}m away");

                        // The fall is one second; this is that plus the blast and slack.
                        Wait(bombs.data.fallTime + 2.0, Phase.AwaitBlast);
                        return;
                    }

                    case Phase.AwaitBlast:
                    {
                        if (Waiting()) return;

                        // A destroyed target is the strongest possible pass: the blast
                        // took everything it had.
                        _trackedDied = _tracked == null || _tracked.IsDead;
                        _trackedPoolAfter = _trackedDied ? 0f : _tracked.Current + _tracked.Shield;

                        Notes.Append($"\n  after the blast: the target is on " +
                                     $"{_trackedPoolAfter:0} health" +
                                     (_trackedDied ? " (dead)" : ""));

                        _phase = Phase.KillOne;
                        return;
                    }

                    case Phase.KillOne:
                    {
                        // Coins are the whole point of the feature, so the kill that pays
                        // them is made deliberately rather than hoped for from the blast.
                        var director = GameDirector.Instance;
                        if (director == null) throw new Exception("the arena has no GameDirector");

                        _coinsBeforeKill = director.CoinsEarned;

                        var victim = NearestEnemy(Vector3.zero);
                        if (victim == null) throw new Exception("nothing left alive to kill for coins");

                        victim.Kill();
                        _coinsAfterKill = director.CoinsEarned;

                        Notes.Append($"\n  one kill paid {_coinsAfterKill - _coinsBeforeKill} coins " +
                                     $"({_coinsBeforeKill} -> {_coinsAfterKill})");

                        _phase = Phase.Judge;
                        return;
                    }

                    case Phase.Judge:
                        if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{Notes}");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        static MainMenuController Menu() => UnityEngine.Object.FindAnyObjectByType<MainMenuController>();

        static StoreCatalog Catalog()
        {
            var store = Menu() != null ? Menu().store : null;
            if (store == null || store.catalog == null)
                throw new Exception("the store has no catalog");

            return store.catalog;
        }

        /// <summary>
        /// The card for an item, found by the title it draws. By title rather than by
        /// index, because the shelf is rebuilt after every purchase and an index would
        /// quietly start pointing at a different item.
        /// </summary>
        static StoreItemCard FindCard(string title)
        {
            string wanted = (title ?? "").ToUpperInvariant();

            foreach (var card in UnityEngine.Object.FindObjectsByType<StoreItemCard>(
                         FindObjectsSortMode.None))
            {
                if (!card.gameObject.activeInHierarchy) continue;
                if (card.titleText == null) continue;

                if (card.titleText.text == wanted) return card;
            }

            throw new Exception($"no store card for \"{title}\" is on screen");
        }

        static void Click(StoreItemCard card, bool primary)
        {
            var button = primary ? card.primaryButton : card.upgradeButton;

            if (button == null || !button.gameObject.activeInHierarchy)
                throw new Exception($"the {(primary ? "buy" : "upgrade")} button on " +
                                    $"\"{card.titleText.text}\" is not on screen");

            if (!button.interactable)
                throw new Exception($"the {(primary ? "buy" : "upgrade")} button on " +
                                    $"\"{card.titleText.text}\" is not interactable, with " +
                                    $"{Wallet.Format(Wallet.Balance)} coins in hand");

            button.onClick.Invoke();
        }

        static ArenaCatalog.Entry FirstPlayable(MainMenuController menu)
        {
            if (menu == null || menu.catalog == null) return null;

            foreach (var entry in menu.catalog.arenas)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) continue;
                if (Application.CanStreamedLevelBeLoaded(entry.sceneName)) return entry;
            }

            return null;
        }

        /// <summary>
        /// The closest living enemy the bomb can actually reach: inside its maximum
        /// range, and outside its minimum so the throw is not clamped outward either.
        /// </summary>
        static Health InRangeEnemy(BombThrower bombs)
        {
            Vector3 from = bombs.transform.position;
            Health best = null;
            float bestDistance = float.MaxValue;

            foreach (var enemy in GameObject.FindGameObjectsWithTag("Enemy"))
            {
                if (enemy == null) continue;

                var health = enemy.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                float distance = Vector3.Distance(
                    new Vector3(from.x, 0f, from.z),
                    new Vector3(enemy.transform.position.x, 0f, enemy.transform.position.z));

                // Backed off both ends, so a target drifting during the one-second fall
                // cannot walk out of the range the throw was solved for.
                if (distance < bombs.data.minRange + 2f) continue;
                if (distance > bombs.data.maxRange - 4f) continue;

                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = health;
            }

            return best;
        }

        /// <summary>The closest living enemy to a point, or null when none is left.</summary>
        static Health NearestEnemy(Vector3 from)
        {
            Health nearest = null;
            float best = float.MaxValue;

            foreach (var enemy in GameObject.FindGameObjectsWithTag("Enemy"))
            {
                if (enemy == null) continue;

                var health = enemy.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                float distance = Vector3.Distance(enemy.transform.position, from);
                if (distance >= best) continue;

                best = distance;
                nearest = health;
            }

            return nearest;
        }

        static void Wait(double seconds, Phase next)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds;
            _phase = next;
        }

        static bool Waiting() => EditorApplication.timeSinceStartup < _deadline;

        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"[{type}] {message.Trim()}");
        }

        // ==================================================================
        static void BackUp()
        {
            IntBackup.Clear();
            StringBackup.Clear();

            foreach (string key in IntKeys) IntBackup[key] = PlayerPrefs.GetInt(key, 0);
            foreach (string key in StringKeys) StringBackup[key] = PlayerPrefs.GetString(key, "");
        }

        static void Restore()
        {
            foreach (var pair in IntBackup) PlayerPrefs.SetInt(pair.Key, pair.Value);
            foreach (var pair in StringBackup) PlayerPrefs.SetString(pair.Key, pair.Value);

            PlayerPrefs.Save();
        }

        static void Detach()
        {
            Restore();
            FPSKitPlayMode.RestoreStartScene();

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
        }

        static void Finish()
        {
            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            // ---- the store ----
            if (!string.IsNullOrEmpty(_unclickable))
                problems.Append($"\n  - the store's \"{_unclickable}\" button cannot be clicked: " +
                                "a raycast at it hits nothing, so no listener on it will ever run");

            if (!_gunOwned)
                problems.Append("\n  - buying a gun did not grant it");

            if (_balanceAfterGun >= _balanceBeforeGun)
                problems.Append($"\n  - buying a gun cost nothing (balance {_balanceBeforeGun} -> " +
                                $"{_balanceAfterGun}): the store is giving stock away");

            if (_gunLevel != 2)
                problems.Append($"\n  - two upgrades left the gun at level {_gunLevel}");

            if (_secondUpgradePrice <= _firstUpgradePrice)
                problems.Append($"\n  - the second upgrade costs {_secondUpgradePrice} against the " +
                                $"first's {_firstUpgradePrice}: the price does not escalate, so " +
                                "upgrades are a free lunch");

            if (_healthLevel < 1)
                problems.Append("\n  - the health upgrade did not take");

            if (_stockBought < 1)
                problems.Append("\n  - buying a pack put nothing on the belt");

            // ---- what reached the player ----
            if (_equippedWeapon != _expectedWeapon)
                problems.Append($"\n  - the player went in holding \"{_equippedWeapon}\" rather than " +
                                $"the \"{_expectedWeapon}\" they bought");

            if (_fireRateMultiplier <= 1.001f)
                problems.Append($"\n  - the bought upgrades never reached the gun (fire rate " +
                                $"x{_fireRateMultiplier:0.00}); bullets per second is what an " +
                                "upgrade is meant to buy");

            if (_maxHealth <= _baseMaxHealth)
                problems.Append($"\n  - the health upgrade never reached the player " +
                                $"({_maxHealth:0} against a base of {_baseMaxHealth:0})");

            if (_bombCharges < 1)
                problems.Append($"\n  - the player went in with {_bombCharges} bombs");

            if (_beltCount < 1)
                problems.Append($"\n  - the drinks bought never reached the belt ({_beltCount})");

            // ---- the bomb ----
            if (_chargesAfterThrow >= _chargesBeforeThrow)
                problems.Append($"\n  - throwing a bomb did not spend a charge " +
                                $"({_chargesBeforeThrow} -> {_chargesAfterThrow})");

            if (_trackedPoolBefore > 0f && _trackedPoolAfter >= _trackedPoolBefore)
                problems.Append($"\n  - the bomb hurt the enemy it was aimed at for nothing " +
                                $"({_trackedPoolBefore:0} -> {_trackedPoolAfter:0}): the ring " +
                                "promises damage it did not deliver");

            if (_coinsAfterKill <= _coinsBeforeKill)
                problems.Append($"\n  - a kill paid no coins ({_coinsBeforeKill} -> " +
                                $"{_coinsAfterKill}): there is nothing to spend in the store");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: the economy is broken:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify store passed: bought a gun and two upgrades at an " +
                      $"escalating price, carried them into \"{_arenaScene}\" as " +
                      $"\"{_equippedWeapon}\" at fire rate x{_fireRateMultiplier:0.00}, a bomb took " +
                      $"{_trackedPoolBefore - _trackedPoolAfter:0} off the enemy it was aimed at, " +
                      $"and a kill paid {_coinsAfterKill - _coinsBeforeKill} coins.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
