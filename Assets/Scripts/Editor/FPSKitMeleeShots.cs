#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// A walkthrough of the player's hands, played for real: the arena is entered in play
    /// mode, one enemy is stood in front of the player and frozen, and every move is thrown
    /// at it -- rifle butt, the gun onto the back, jab, cross, kick, then a reload, a look at
    /// the body and a few steps -- photographing the frames that matter. Works in an editor
    /// someone has open: it never quits, and puts their scene back when it is done.
    ///
    ///   FPSKitMeleeShots.Run("Build/MeleeShots")   (through Unity MCP, or a menu)
    /// </summary>
    public static class FPSKitMeleeShots
    {
        const string DefaultScene = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";

        static string _folder;
        static string _returnTo;
        static int _width = 480, _height = 270;
        static double _started;
        static double _realMark;
        static int _step;
        static float _mark;
        static int _shot;
        static Action _onDone;

        static GameObject _player;
        static PlayerMotor _motor;
        static Weapon _weapon;
        static MeleeStrike _melee;
        static EnemyAI _dummy;

        /// <summary>What happened, for whoever started it.</summary>
        public static string Report { get; private set; } = "";

        /// <summary>Forgets a walkthrough that never finished (an editor recompile mid-run, say).</summary>
        public static void ResetState()
        {
            EditorApplication.update -= Tick;
            Running = false;
        }
        public static bool Running { get; private set; }

        [MenuItem("FPSKit/View Models/Melee Walkthrough", false, 66)]
        static void Menu() => Run("Build/MeleeShots");

        static bool _enemyScript;
        static bool _goreScript;
        static readonly List<EnemyAI> _row = new List<EnemyAI>();

        /// <summary>Four soldiers in a row, each killed a different way: caved, beheaded, a leg off, a leg broken.</summary>
        public static void RunGore(string folder, string scene = null, int width = 480, int height = 270, Action onDone = null)
        {
            _goreScript = true;
            _enemyScript = false;
            Run(folder, scene, width, height, onDone, keepMode: true);
        }

        /// <summary>The enemy's side: hit reactions by part, a kick that throws it, the get-up.</summary>
        public static void RunEnemy(string folder, string scene = null, int width = 480, int height = 270, Action onDone = null)
        {
            _enemyScript = true;
            Run(folder, scene, width, height, onDone, keepMode: true);
        }

        public static void Run(string folder, string scene = null, int width = 480, int height = 270, Action onDone = null, bool keepMode = false)
        {
            if (!keepMode) { _enemyScript = false; _goreScript = false; }
            if (Running) return;
            _folder = string.IsNullOrEmpty(folder) ? "Build/MeleeShots" : folder;
            _width = width;
            _height = height;
            _onDone = onDone;
            Directory.CreateDirectory(_folder);
            Report = "";

            _returnTo = EditorSceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? DefaultScene : scene, OpenSceneMode.Single);

            FPSKitPlayMode.SuspendStartScene();
            _step = 0;
            _started = EditorApplication.timeSinceStartup;
            Running = true;
            EditorApplication.update += Tick;
        }

        static void Note(string line) => Report += line + "\n";

        static void Shoot(string name)
        {
            var cam = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            if (cam == null) { Note("no camera for " + name); return; }
            FairgroundShots.Capture(cam, $"{_folder}/{_shot++:00}_{name}.png", _width, _height);
        }

        /// <summary>
        /// The script, as (when, what) pairs: each runs once game time passes its mark. Time
        /// is pinned at a sixtieth a frame so a strike lasting a third of a second is photographed
        /// where it should be whatever the editor's frame rate.
        /// </summary>
        static List<(float at, Action act)> _script;
        static int _next;

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 900) throw new Exception("melee walkthrough timed out");

                switch (_step)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _realMark = EditorApplication.timeSinceStartup + 2.0;
                        _step = 1;
                        return;

                    case 1:
                        // Waited on the clock on the wall, not game time: a slow editor frame
                        // rate must not turn a few seconds' wait into minutes.
                        if (EditorApplication.timeSinceStartup < _realMark) return;
                        // Someone to hit: whoever the level has sent by now, or one made for it.
                        if (Object.FindAnyObjectByType<EnemyAI>() == null)
                        {
                            if (EditorApplication.timeSinceStartup < _realMark + 6.0) return;
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab");
                            var p = GameObject.FindGameObjectWithTag("Player");
                            if (prefab != null && p != null)
                                Object.Instantiate(prefab, p.transform.position + p.transform.forward * 2.2f, Quaternion.identity);
                        }
                        Setup();
                        // A thirtieth a frame: fine enough to catch a strike at its blow, coarse
                        // enough that an editor throttled in the background still gets through.
                        Time.captureDeltaTime = 1f / 30f;
                        _script = _goreScript ? GoreScript() : _enemyScript ? EnemyScript() : Script();
                        _next = 0;
                        _mark = Time.time;
                        _step = 2;
                        return;

                    case 2:
                        // On the enemy's walkthrough the view follows the body, wherever it is thrown.
                        if (_enemyScript) Face();
                        while (_next < _script.Count && Time.time >= _mark + _script[_next].at)
                        {
                            _script[_next].act();
                            _next++;
                        }
                        if (_next >= _script.Count) { _step = 3; }
                        return;

                    case 3:
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Note("FAILED: " + e.Message);
                Debug.LogError("[FPSKit] melee walkthrough: " + e);
                Finish();
            }
        }

        static void Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) throw new Exception("no player");
            _motor = _player.GetComponent<PlayerMotor>();
            _weapon = _player.GetComponentInChildren<Weapon>();
            _melee = _player.GetComponent<MeleeStrike>();

            // One enemy, stood two metres ahead and switched off thinking: something to hit.
            EnemyAI keep = null;
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                if (keep == null) { keep = ai; continue; }
                ai.gameObject.SetActive(false);
            }

            _dummy = keep;
            if (_dummy != null)
            {
                var agent = _dummy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                Vector3 spot = _player.transform.position + _player.transform.forward * 2.2f;
                if (agent != null && agent.enabled) agent.Warp(spot);
                else _dummy.transform.position = spot;
                _dummy.transform.rotation = Quaternion.LookRotation(-_player.transform.forward);

                // It stands and takes it: no attacks back, no walking off. Its body still
                // reacts, which is the point.
                _dummy.attackDamage = 0f;
                _dummy.rangedDamageMultiplier = 0f;
                if (agent != null) agent.speed = 0f;
            }

            Note($"player {(_player != null)} weapon {(_weapon != null)} arms {(_weapon != null && _weapon.HasArms)} melee {(_melee != null)} enemy {(_dummy != null)}");
        }

        static void Face()
        {
            if (_dummy == null || _motor == null) return;
            var animator = _dummy.GetComponentInChildren<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            _motor.TurnTowards(hips != null ? hips.position + Vector3.up * 0.55f : _dummy.transform.position + Vector3.up * 1.3f, 1f);
        }

        static List<(float, Action)> Script()
        {
            var s = MeleeSettings.Active;
            var list = new List<(float, Action)>();
            float t = 0f;

            void At(float dt, Action a) { t += dt; list.Add((t, a)); }

            At(0.1f, () => { Face(); });
            At(0.4f, () => Shoot("idle"));

            // The rifle butt: the first press.
            At(0.1f, () => { Face(); Note("butt hit " + Name(_melee.TryPunch())); });
            At(s.buttSeconds * 0.2f, () => Shoot("butt_cock"));
            At(s.buttSeconds * 0.18f, () => Shoot("butt_blow"));

            // The second press: the gun goes onto the back and the left fist lands.
            At(s.buttSeconds * 0.7f + s.buttRecovery, () => { Note("jab hit " + Name(_melee.TryPunch()) + " fists " + _melee.FistsOut); });
            At(s.holsterSeconds * 0.4f, () => Shoot("gun_to_back"));
            At(s.holsterSeconds * 0.5f + s.jabSeconds * 0.3f, () => Shoot("jab"));
            At(s.jabSeconds * 0.7f + s.jabRecovery, () => { Note("cross hit " + Name(_melee.TryPunch())); });
            At(s.crossSeconds * 0.38f, () => Shoot("cross"));
            At(s.crossSeconds + s.crossRecovery, () => Shoot("guard"));

            // The kick, from the fists, then the gun comes back.
            At(0.1f, () => { Face(); Note("kick hit " + Name(_melee.TryKick())); });
            At(s.kickSeconds * 0.25f, () => Shoot("kick_chamber"));
            At(s.kickSeconds * 0.22f, () => Shoot("kick"));
            At(s.kickSeconds * 0.6f + s.fistIdleSeconds + s.drawSeconds + 0.3f, () => Shoot("gun_back"));

            // The kick with the gun in the hands: slid aside first.
            At(s.kickRecovery, () => { Face(); Note("gun kick hit " + Name(_melee.TryKick())); });
            At(s.kickSeconds * 0.2f, () => Shoot("kick_gun_aside"));
            At(s.kickSeconds * 0.25f, () => Shoot("kick_gun"));

            // A reload from empty, and a look down at the body.
            At(s.kickSeconds + 0.3f, () =>
            {
                typeof(Weapon).GetProperty("CurrentAmmo").SetValue(_weapon, 0);
                _weapon.TryReload();
            });
            At(_weapon != null ? _weapon.ReloadTime * 0.3f : 0.6f, () => Shoot("reload"));
            At(_weapon != null ? _weapon.ReloadTime * 0.8f : 1.5f, () => { if (_motor != null) _motor.TurnBy(new Vector2(0f, -70f)); });
            At(0.2f, () => Shoot("look_down"));

            return list;
        }

        static string Name(Health h) => h != null ? h.name : "nothing";

        /// <summary>A round into one part of the stand-in, as the gun would land it.</summary>
        static void ShootPart(BodyPart part, float amount)
        {
            if (_dummy == null) return;
            foreach (var hb in _dummy.GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != part) continue;
                Vector3 from = _weapon.fpsCamera.transform.position;
                Vector3 at = hb.GetComponent<Collider>().bounds.center;
                var info = new DamageInfo(amount, at, (from - at).normalized, (at - from).normalized, _player);
                hb.Receive(info);
                return;
            }
            Note("no hitbox for " + part);
        }

        static void HitRow(int index, BodyPart part, float amount)
        {
            if (index >= _row.Count || _row[index] == null) return;
            foreach (var hb in _row[index].GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != part) continue;
                Vector3 from = _weapon.fpsCamera.transform.position;
                Vector3 at = hb.GetComponent<Collider>().bounds.center;
                hb.Receive(new DamageInfo(amount, at, (from - at).normalized, (at - from).normalized, _player));
                return;
            }
        }

        static List<(float, Action)> GoreScript()
        {
            var list = new List<(float, Action)>();
            float t = 0f;
            void At(float dt, Action a) { t += dt; list.Add((t, a)); }

            At(0.1f, () =>
            {
                // Four in a row, five metres off, side on to the player so the damage shows.
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.gameObject.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab");
                _row.Clear();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 at = _player.transform.position + _player.transform.forward * 5f + _player.transform.right * (i - 1.5f) * 1.5f;
                    var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(-_player.transform.forward));
                    var ai = go.GetComponent<EnemyAI>();
                    ai.attackDamage = 0f;
                    ai.rangedDamageMultiplier = 0f;
                    var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent != null) agent.speed = 0f;
                    _row.Add(ai);
                }
            });
            At(1.5f, () => Shoot("row_standing"));

            // 0: killed through the head, an ordinary round: caved in.
            At(0.2f, () => HitRow(0, BodyPart.Head, 40f));
            // 1: a huge round through the head: taken off.
            At(0.1f, () => HitRow(1, BodyPart.Head, 400f));
            // 2: a huge round through the shin: the leg off below the knee.
            At(0.1f, () => HitRow(2, BodyPart.RightShin, 400f));
            // 3: the leg shot to pieces while alive (it buckles and stays bent), then killed in the chest.
            At(0.1f, () => { HitRow(3, BodyPart.LeftShin, 30f); HitRow(3, BodyPart.LeftShin, 30f); });
            At(0.2f, () => Shoot("row_hits"));
            At(0.6f, () => Shoot("row_falling"));
            At(0.5f, () => HitRow(3, BodyPart.Torso, 400f));
            At(1.6f, () => Shoot("row_dead"));
            At(1.5f, () =>
            {
                // Closer, looking down at them.
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.transform.position += _player.transform.forward * 3f;
                if (cc != null) cc.enabled = true;
                _motor.TurnBy(new Vector2(0f, -45f));
            });
            At(0.4f, () => Shoot("row_close"));
            return list;
        }

        static List<(float, Action)> EnemyScript()
        {
            var s = MeleeSettings.Active;
            var list = new List<(float, Action)>();
            float t = 0f;
            void At(float dt, Action a) { t += dt; list.Add((t, a)); }

            At(0.1f, () => Face());
            // Stood off a few metres first, to see the whole soldier and how it holds the rifle.
            At(0.1f, () =>
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.transform.position -= _player.transform.forward * 2.5f;
                if (cc != null) cc.enabled = true;
            });
            At(0.5f, () => Shoot("enemy_far"));
            At(0.1f, () =>
            {
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.transform.position += _player.transform.forward * 2.5f;
                if (cc != null) cc.enabled = true;
            });
            At(0.6f, () => { Face(); Shoot("enemy_standing"); });

            // Where it is hit is where it moves.
            At(0.2f, () => ShootPart(BodyPart.LeftArm, 4f));
            At(0.07f, () => Shoot("hit_left_arm"));
            At(0.7f, () => ShootPart(BodyPart.Neck, 1f));
            At(0.07f, () => Shoot("hit_neck"));
            At(0.7f, () => ShootPart(BodyPart.LowerTorso, 4f));
            At(0.08f, () => Shoot("hit_belly"));
            At(0.7f, () => ShootPart(BodyPart.RightShin, 4f));
            At(0.08f, () => Shoot("hit_shin"));

            // The kick: thrown, down, and back up.
            At(0.8f, () => { Face(); Note("kick hit " + Name(_melee.TryKick()) + " health " + (_dummy != null ? _dummy.GetComponent<Health>().Current.ToString("0") : "-")); });
            At(s.kickSeconds * MeleeStrike.ImpactAt(MeleeStrike.Strike.Kick) + 0.05f, () => Shoot("kick_land"));
            At(0.2f, () => { Face(); Shoot("thrown_0.2s"); });
            At(0.35f, () => { Face(); Shoot("thrown_0.55s"); });
            At(0.6f, () => { Face(); Shoot("down"); Note("downed " + (_dummy != null && _dummy.Downed)); });
            At(1.2f, () => { Face(); Shoot("down_later"); });
            At(0.8f, () => { Face(); Shoot("getting_up"); });
            At(0.6f, () => { Face(); Shoot("getting_up_2"); });
            At(1.0f, () => { Face(); Shoot("back_up"); Note("downed after " + (_dummy != null && _dummy.Downed)); });

            return list;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();

            EditorApplication.delayCall += Restore;
        }

        static void Restore()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Restore; return; }
            Running = false;
            try
            {
                if (!string.IsNullOrEmpty(_returnTo)) EditorSceneManager.OpenScene(_returnTo, OpenSceneMode.Single);
            }
            catch (Exception e)
            {
                Note("could not reopen " + _returnTo + ": " + e.Message);
            }
            Debug.Log("[FPSKit] melee walkthrough done:\n" + Report);
            _onDone?.Invoke();
        }
    }
}
#endif
