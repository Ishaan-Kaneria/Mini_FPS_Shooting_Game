#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Fists when out of ammo (Ishaan's Part B). On a floor of the test's own, in a real level, through the touch inputs
    /// (the path a phone takes; the key and the pad are checked as bindings):
    ///
    /// - the magazine empty with rounds in reserve reloads and drops nothing;
    /// - single shot, the last round fired: the gun drops once, however long the trigger is held after;
    /// - automatic, the trigger held through the last round: it drops once;
    /// - disarmed, the trigger, the sights and the reload do nothing; the fists come up; the punch is a jab, not the butt;
    ///   the HUD says FISTS with the fist icon;
    /// - near a gun, the prompt and the PICK UP button; taking it arms with the rounds it had;
    /// - an enemy's rifle, from the enemy that dropped it dying, with the rounds it had left; armed, the gun in the
    ///   hands goes down in its place with its own;
    /// - an ammo pickup with the player's own gun near re-arms with it; far from it, the rounds are kept and go into
    ///   it when it is taken up;
    /// - the interact key and the pad's D-pad up are bound and prompted; the saved loadout is not touched.
    ///
    /// The player carries one gun, so "switch to another weapon with ammo" has nothing to switch to (reported).
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyFists      (Build/Fists/report.txt)
    /// </summary>
    public static class FPSKitFistsTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Store = "Assets/FPSKit_Generated/Store/";
        const float Floor = 400f;

        public static bool Running { get; private set; }

        static readonly Stack<IEnumerator> Script = new Stack<IEnumerator>();
        static readonly List<string> Lines = new List<string>();
        static int _lastFrame, _warm, _fails;
        static double _startedAt;
        static GameObject _player, _floor, _enemy;
        static PlayerMotor _motor;
        static Weapon _weapon;
        static MeleeStrike _melee;
        static GunPickup _pickup;
        static int _fired;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;
        static string _error;

        public static void Batch() => Run();

        [MenuItem("FPSKit/Player/Check Fists When Out Of Ammo")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Script.Clear(); Lines.Clear();
            _lastFrame = -1; _warm = 0; _fails = 0; _player = null; _error = null;
            _startedAt = EditorApplication.timeSinceStartup;
            Running = true;
            SaveMigration.Apply();
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("fists test timed out");
                if (!EditorApplication.isPlaying || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tunedLevel == null)
                {
                    _tunedLevel = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }
                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null) Script.Push(All());
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                    if (_enemy == null || ai.gameObject != _enemy) ai.gameObject.SetActive(false);
                var health = _player != null ? _player.GetComponent<Health>() : null;
                if (health != null && health.Normalized < 0.9f) health.ResetHealth();

                while (Script.Count > 0)
                {
                    var top = Script.Peek();
                    if (!top.MoveNext()) { Script.Pop(); continue; }
                    if (top.Current is IEnumerator sub) { Script.Push(sub); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception e)
            {
                _error = e.Message + "\n" + e.StackTrace;
                Debug.LogException(e);
                Finish();
            }
        }

        static void Check(bool ok, string what)
        {
            Lines.Add($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) _fails++;
        }

        static IEnumerator Frames(float seconds) { for (int i = 0; i < Mathf.Max(1, Mathf.RoundToInt(seconds * 60f)); i++) yield return null; }

        static void SetAmmo(int magazine)
            => typeof(Weapon).GetProperty("CurrentAmmo").GetSetMethod(true).Invoke(_weapon, new object[] { magazine });

        static int OwnOnGround() => GroundGun.All.Count(g => g != null && g.playersOwn && g.Available);

        static void Place(Vector3 at, Vector3 lookAt)
        {
            var cc = _player.GetComponent<CharacterController>();
            cc.enabled = false;
            _player.transform.position = at;
            cc.enabled = true;
            Physics.SyncTransforms();
            _motor.TurnTowards(lookAt, 1f);
        }

        /// <summary>A gun of the player's own kind laid at their feet and taken up: armed with exactly these rounds.</summary>
        static IEnumerator Arm(WeaponData data, int magazine, int reserve, bool own)
        {
            var g = GroundGun.SpawnDropped(data, magazine, reserve, _player.transform.position + _player.transform.forward * 0.6f + Vector3.up * 0.3f,
                                           _player.transform.rotation, Vector3.zero, own);
            yield return null;
            _weapon.TakeUp(g);
            yield return Frames(0.6f);   // drawn
        }

        static IEnumerator All()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            _motor = _player.GetComponent<PlayerMotor>();
            _weapon = _player.GetComponentInChildren<Weapon>();
            _melee = _player.GetComponent<MeleeStrike>();
            _pickup = _player.GetComponent<GunPickup>();
            _weapon.Fired += w => _fired++;
            MobileInput.Reset();
            MobileInput.Active = true;
            string loadoutBefore = PlayerPrefs.GetString("FPSKit.Loadout.Gun", "");

            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "Fists_Floor";
            _floor.transform.position = new Vector3(0f, Floor - 0.5f, 0f);
            _floor.transform.localScale = new Vector3(80f, 1f, 80f);
            var surface = _floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.BuildNavMesh();
            Place(new Vector3(0f, Floor + 0.05f, 0f), new Vector3(0f, Floor + 1.6f, 10f));
            yield return Frames(0.5f);

            var dmr = AssetDatabase.LoadAssetAtPath<WeaponData>(Store + "Gun_DMR.asset");
            var smg = AssetDatabase.LoadAssetAtPath<WeaponData>(Store + "Gun_SMG.asset");
            var burst = AssetDatabase.LoadAssetAtPath<WeaponData>(Store + "Gun_Burst.asset");
            Lines.Add($"guns: single {dmr?.weaponName}, automatic {smg?.weaponName}, the enemies' rifle {burst?.weaponName}");

            // ---- the magazine empty, rounds in reserve: a reload, nothing dropped ----
            _weapon.Equip(dmr);
            _weapon.SetReserve(12);
            SetAmmo(1);
            yield return Frames(0.6f);
            _fired = 0;
            MobileInput.SetFireButton(true); yield return null; MobileInput.SetFireButton(false);
            yield return Frames(0.5f);
            Check(_weapon.IsReloading && !_weapon.Disarmed && OwnOnGround() == 0, $"magazine empty with 12 in reserve: reloading {_weapon.IsReloading}, disarmed {_weapon.Disarmed}, guns dropped {OwnOnGround()}");
            yield return Frames(3f);
            Check(_weapon.CurrentAmmo == 12 && _weapon.ReserveAmmo == 0, $"  and loaded: {_weapon.CurrentAmmo} / {_weapon.ReserveAmmo}");

            // ---- single shot, the last two rounds: one drop, and the trigger held after does nothing ----
            _weapon.SetReserve(0);
            SetAmmo(2);
            _fired = 0;
            for (int i = 0; i < 2; i++) { MobileInput.SetFireButton(true); yield return null; MobileInput.SetFireButton(false); yield return Frames(0.6f); }
            for (int i = 0; i < 8; i++) { MobileInput.SetFireButton(true); yield return Frames(0.1f); MobileInput.SetFireButton(false); yield return Frames(0.1f); }
            MobileInput.SetFireButton(true); yield return Frames(1f); MobileInput.SetFireButton(false);
            Check(_fired == 2 && _weapon.Disarmed && OwnOnGround() == 1,
                  $"single shot, the last 2 rounds then the trigger pulled 9 more times: {_fired} fired, disarmed {_weapon.Disarmed}, guns on the ground {OwnOnGround()} (1 wanted)");

            // ---- disarmed: nothing answers the trigger, the sights or the reload; the fists are up ----
            float shotBefore = _weapon.LastShotTime;
            MobileInput.Aim = true; MobileInput.QueueReload(); MobileInput.SetFireButton(true);
            yield return Frames(0.8f);
            Check(!_weapon.IsAiming && !_weapon.IsReloading && Mathf.Approximately(_weapon.LastShotTime, shotBefore),
                  $"disarmed, fire + aim + reload held: aiming {_weapon.IsAiming}, reloading {_weapon.IsReloading}, a shot {!Mathf.Approximately(_weapon.LastShotTime, shotBefore)}");
            MobileInput.Aim = false; MobileInput.SetFireButton(false);
            yield return Frames(0.4f);
            Check(_melee.FistsUp > 0.99f && _melee.FistsOut, $"the fists are up: FistsUp {_melee.FistsUp:0.00}");
            var hudName = Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Name" && t.transform.parent != null && t.transform.parent.name == "Kind");
            var hudIcon = hudName != null ? hudName.transform.parent.GetComponentInChildren<Image>() : null;
            Check(hudName != null && hudName.text == "FISTS" && hudIcon != null && hudIcon.sprite != null && hudIcon.sprite.name.Contains("fist"),
                  $"the HUD's weapon panel: \"{hudName?.text}\" with the {hudIcon?.sprite?.name} icon");
            _melee.TryPunch();
            yield return null;
            Check(_melee.Current == MeleeStrike.Strike.Jab, $"the punch key throws a {_melee.Current} (a jab, never the rifle butt)");
            yield return Frames(1.2f);

            // ---- automatic, the trigger held through the last rounds: one drop ----
            yield return Arm(smg, 5, 0, own: false);
            Check(!_weapon.Disarmed && _weapon.Data == smg && _weapon.CurrentAmmo == 5, $"armed again with the {_weapon.Data?.weaponName}: {_weapon.CurrentAmmo} rounds");
            int groundBefore = GroundGun.All.Count(g => g != null && g.Available);
            _fired = 0;
            MobileInput.SetFireButton(true); yield return Frames(3f); MobileInput.SetFireButton(false);
            int dropped = GroundGun.All.Count(g => g != null && g.Available) - groundBefore;
            Check(_fired == 5 && _weapon.Disarmed && dropped == 1, $"automatic, the trigger held 3 s through the last 5: {_fired} fired, disarmed {_weapon.Disarmed}, guns dropped {dropped} (1 wanted)");

            // ---- near a gun: the prompt and PICK UP; taking it arms with its rounds ----
            var lying = GroundGun.SpawnDropped(smg, 10, 20, new Vector3(3f, Floor + 0.3f, 3f), Quaternion.identity, Vector3.zero, false);
            yield return Frames(1f);
            Place(new Vector3(3f, Floor + 0.05f, 1.6f), lying.Centre);
            yield return Frames(0.4f);
            var prompt = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "PickUpPrompt");
            Check(_pickup.Candidate == lying && MobileInput.PickUpReady && prompt != null && prompt.gameObject.activeInHierarchy,
                  $"a gun in reach: it is the one offered {_pickup.Candidate == lying}, PICK UP on the touch button {MobileInput.PickUpReady}, the prompt up {prompt != null && prompt.gameObject.activeInHierarchy} (\"{prompt?.GetComponentInChildren<TMP_Text>()?.text}\")");
            MobileInput.QueueInteract();
            yield return Frames(0.6f);
            Check(!_weapon.Disarmed && _weapon.Data == smg && _weapon.CurrentAmmo == 10 && _weapon.ReserveAmmo == 20 && lying == null,
                  $"picked up through the touch button: {_weapon.Data?.weaponName} {_weapon.CurrentAmmo} / {_weapon.ReserveAmmo} (10 / 20 wanted), gone from the ground {lying == null}");

            // ---- an enemy's rifle, dropped as it died, with what it had left; ours goes down in its place ----
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            Vector3 enemyAt = new Vector3(-4f, Floor, 6f);
            if (NavMesh.SamplePosition(enemyAt, out var hit, 2f, NavMesh.AllAreas)) enemyAt = hit.position;
            _enemy = Object.Instantiate(prefab, enemyAt, Quaternion.LookRotation(Vector3.back));
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_enemy);
            _enemy.GetComponent<EnemyAI>().attackDamage = 0f;
            yield return Frames(1.5f);
            _enemy.GetComponent<Health>().ApplyDamage(new DamageInfo(5000f, _enemy.transform.position + Vector3.up, Vector3.up, Vector3.forward, null));
            yield return Frames(3f);
            var rifle = GroundGun.All.FirstOrDefault(g => g != null && !g.playersOwn && g.Available && g.transform.IsChildOf(_enemy.transform) == false && g.data == burst);
            Check(rifle != null, $"the dead enemy's rifle is a gun on the ground: {(rifle != null ? rifle.data.weaponName + ", " + rifle.magazine + " + " + rifle.reserve + " rounds" : "none")}");
            if (rifle != null)
            {
                int load = rifle.magazine + rifle.reserve;
                Vector3 r = rifle.Centre;
                Place(new Vector3(r.x, Floor + 0.05f, r.z) - new Vector3(0f, 0f, 1.4f), r);
                yield return Frames(0.4f);
                var before = new HashSet<GroundGun>(GroundGun.All.Where(g => g != null));
                MobileInput.QueueInteract();  // armed with the SMG: a swap, only through the key or the pad in play, the same call
                if (!MobileInput.PickUpReady) _weapon.TakeUp(_pickup.Candidate);
                yield return Frames(0.6f);
                var put = GroundGun.All.FirstOrDefault(g => g != null && g.Available && g.data == smg && !before.Contains(g));
                Check(_weapon.Data == burst && _weapon.CurrentAmmo + _weapon.ReserveAmmo == load && put != null && put.magazine + put.reserve == 30,
                      $"taken: {_weapon.Data?.weaponName} with {_weapon.CurrentAmmo} + {_weapon.ReserveAmmo} ({load} it had); the {smg.weaponName} put down with {(put != null ? put.magazine + put.reserve : -1)} (30 wanted)");
            }

            // ---- ammo with our own gun near: taken back up with it ----
            yield return Arm(dmr, 1, 0, own: true);
            _fired = 0;
            MobileInput.SetFireButton(true); yield return null; MobileInput.SetFireButton(false);
            yield return Frames(0.8f);
            Check(_weapon.Disarmed && OwnOnGround() >= 1, $"our own {dmr.weaponName} run dry and dropped: disarmed {_weapon.Disarmed}");
            var box = new GameObject("Fists_AmmoBox");
            box.transform.position = _player.transform.position + Vector3.up;
            var ammo = box.AddComponent<Pickup>();
            ammo.kind = Pickup.Kind.Ammo; ammo.ammoAmount = 60; ammo.magnetRadius = 0f;
            yield return Frames(4f);
            Check(!_weapon.Disarmed && _weapon.Data == dmr && _weapon.CurrentAmmo + _weapon.ReserveAmmo == 60 && _weapon.CurrentAmmo == _weapon.MagazineSize,
                  $"an ammo box with our gun near: re-armed {!_weapon.Disarmed} with the {_weapon.Data?.weaponName}, {_weapon.CurrentAmmo} + {_weapon.ReserveAmmo} (60, and loaded)");

            // ---- ammo far from it: kept, and loaded when it is taken up ----
            _weapon.SetReserve(0);
            SetAmmo(1);
            MobileInput.SetFireButton(true); yield return null; MobileInput.SetFireButton(false);
            yield return Frames(0.8f);
            var ours = GroundGun.All.LastOrDefault(g => g != null && g.playersOwn && g.Available);
            Place(new Vector3(30f, Floor + 0.05f, -30f), new Vector3(30f, Floor + 1.6f, 0f));
            yield return Frames(0.3f);
            box = new GameObject("Fists_AmmoBox2");
            box.transform.position = _player.transform.position + Vector3.up;
            ammo = box.AddComponent<Pickup>();
            ammo.kind = Pickup.Kind.Ammo; ammo.ammoAmount = 60; ammo.magnetRadius = 0f;
            yield return Frames(1f);
            Check(_weapon.Disarmed && _weapon.StoredRounds == 60, $"an ammo box 40 m from our gun: still disarmed {_weapon.Disarmed}, rounds kept {_weapon.StoredRounds} (60 wanted)");
            if (ours != null)
            {
                Vector3 o = ours.Centre;
                Place(new Vector3(o.x, Floor + 0.05f, o.z) - new Vector3(0f, 0f, 1.4f), o);
                yield return Frames(0.4f);
                MobileInput.QueueInteract();
                yield return Frames(4f);
                Check(!_weapon.Disarmed && _weapon.Data == dmr && _weapon.CurrentAmmo + _weapon.ReserveAmmo == 60 && _weapon.StoredRounds == 0 && _weapon.CurrentAmmo == _weapon.MagazineSize,
                      $"  and our gun picked up: {_weapon.CurrentAmmo} + {_weapon.ReserveAmmo} (the 60 kept), kept now {_weapon.StoredRounds}");
            }

            // ---- the key, the pad, the prompts; the saved loadout ----
            var action = GameInput.ActionFor(GameAction.Interact);
            var paths = action.bindings.Select(b => b.effectivePath).ToList();
            Check(paths.Contains("<Keyboard>/e") && paths.Contains("<Gamepad>/dpad/up"), $"bound: {string.Join(", ", paths)}");
            Check(InputPrompts.PadGlyph(GameAction.Interact, PadFamily.Xbox) == "dpad" && KeyBindings.All().Any(b => b.Id == "interact"),
                  $"the pad prompt is the D-pad and the key can be rebound in Settings");
            Check(PlayerPrefs.GetString("FPSKit.Loadout.Gun", "") == loadoutBefore, $"the saved loadout is untouched (\"{loadoutBefore}\")");
            Lines.Add("  --   switch to another weapon with ammo: the player carries one gun (the loadout's), so there is never another to switch to; the gun drops");
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Build/Fists");
            bool ok = _error == null && _fails == 0 && Lines.Count > 3;
            string report = (ok ? "fists test passed" : "fists test FAILED") + (_error != null ? "\n  error: " + _error : "") + "\n" + string.Join("\n", Lines);
            File.WriteAllText("Build/Fists/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);

            MobileInput.Reset();
            MobileInput.Active = false;
            if (_floor != null) Object.Destroy(_floor);
            if (_enemy != null) Object.Destroy(_enemy);
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null; _clockBackup = -1f;
            Time.captureDeltaTime = 0f;
            Running = false;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(ok ? 0 : 1);
        }
    }
}
#endif
