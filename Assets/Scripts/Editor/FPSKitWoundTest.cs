#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Proves that where a round lands changes what it does.
    ///
    /// Everything here is behaviour a player sees and no build check would notice going:
    /// a shot leg that does not limp, a wrecked gun arm that keeps shooting, a boss that
    /// dies to one headshot, a corpse that stays standing. It stands a row of real enemy
    /// prefabs on a floor in an empty scene, stamps real archetypes on them, and shoots
    /// them through their real hitboxes:
    ///
    /// - a grunt's leg: limps after one round, is on the floor after three, and is slower each time;
    /// - a grunt's gun arm: drops the rifle (a real object on the floor) and closes to melee;
    /// - a grunt's head, for one point of damage: dead, and dropped where it stood;
    /// - a shielded elite's head: survives while the shield is up, dies once it is down;
    /// - a boss's head and legs: survives the headshot, limps, and never goes down to a crawl;
    /// - a blast: the body is thrown, moving, off the floor;
    /// - blood: wounds on the body, splats on the floor, and nothing with the setting off.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyWounds
    /// </summary>
    public static class FPSKitWoundTest
    {
        const string PrefabPath = "Assets/FPSKit_Generated/Enemy.prefab";
        const double HardTimeout = 90.0;

        enum Phase { Enter, Spawn, Shoot, Settle, Judge }

        static Phase _phase;
        static double _startedAt, _deadline;
        static readonly List<string> Errors = new List<string>();
        static readonly List<string> Problems = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();
        static bool _bloodBackup;

        static GameObject _legGrunt, _armGrunt, _headGrunt, _elite, _boss, _blasted, _legKilled;
        static float _speedSound, _speedLimp, _speedCrawl;

        public static void VerifyWounds()
        {
            try
            {
                Errors.Clear();
                Problems.Clear();
                Notes.Clear();

                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                // Something for bodies to fall on and blood to land on.
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.localScale = new Vector3(8f, 1f, 8f);
                floor.layer = LayerMask.NameToLayer("Environment");

                _bloodBackup = GameSettings.Blood;
                GameSettings.Blood = true;

                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] wound test: a body must answer to where it is shot");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                Detach();
                EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"wound test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        SaveMigration.Apply();
                        _phase = Phase.Spawn;
                        return;

                    case Phase.Spawn:
                        _legGrunt = Spawn("Grunt", 0);
                        _armGrunt = Spawn("Grunt", 1);
                        _headGrunt = Spawn("Grunt", 2);
                        _elite = Spawn("Sentinel", 3);
                        _boss = Spawn("Warden", 4);
                        _blasted = Spawn("Grunt", 5);
                        _legKilled = Spawn("Grunt", 6);

                        // Worn down now, well outside the window that sums a burst, so the
                        // round that finishes it later is a light leg shot and not a heavy hit.
                        var worn = _legKilled.GetComponent<Health>();
                        Hit(_legKilled, BodyPart.Torso, worn.maxHealth * 0.9f);

                        Wait(0.5, Phase.Shoot);
                        return;

                    case Phase.Shoot:
                        if (Waiting()) return;
                        Shoot();
                        Wait(1.6, Phase.Settle);
                        return;

                    case Phase.Settle:
                        if (Waiting()) return;
                        Settle();
                        _phase = Phase.Judge;
                        return;

                    case Phase.Judge:
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Errors.Add(e.ToString());
                Finish();
            }
        }

        static GameObject Spawn(string archetype, int slot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new Exception($"{PrefabPath} does not exist. Run FPSKitBatch.RebuildEnemy.");

            var enemy = UnityEngine.Object.Instantiate(prefab, new Vector3(slot * 4f - 12f, 0f, 0f), Quaternion.identity);
            enemy.name = $"{archetype}_{slot}";

            var type = FPSKitEnemyRoster.GetOrCreate(archetype);
            type.ApplyTo(enemy);

            return enemy;
        }

        // ==================================================================

        static void Shoot()
        {
            // ---- the leg ------------------------------------------------------------
            var wounds = _legGrunt.GetComponent<EnemyWounds>();
            Require(wounds != null, "the enemy prefab has no EnemyWounds");
            if (wounds == null) return;

            _speedSound = wounds.MoveSpeedMultiplier;

            Hit(_legGrunt, BodyPart.LeftLeg, 25f);
            _speedLimp = wounds.MoveSpeedMultiplier;
            Require(wounds.Legs == EnemyWounds.LegState.Limping,
                    $"one round in a grunt's leg left it {wounds.Legs}, not limping");

            Hit(_legGrunt, BodyPart.LeftLeg, 25f);
            Hit(_legGrunt, BodyPart.LeftLeg, 25f);
            _speedCrawl = wounds.MoveSpeedMultiplier;

            Require(!_legGrunt.GetComponent<Health>().IsDead, "three leg rounds killed a grunt outright");
            Require(wounds.Legs == EnemyWounds.LegState.Crawling,
                    $"three rounds in one leg left a grunt {wounds.Legs}, not on the floor");
            Require(_speedSound > _speedLimp && _speedLimp > _speedCrawl,
                    $"speed did not fall with the wound ({_speedSound:0.00} -> {_speedLimp:0.00} -> {_speedCrawl:0.00})");

            var ai = _legGrunt.GetComponent<EnemyAI>();
            Require(ai != null && ai.Crawling, "EnemyAI does not know its enemy is crawling");

            // ---- the gun arm ---------------------------------------------------------
            var armAi = _armGrunt.GetComponent<EnemyAI>();
            Require(armAi.ranged, "a grunt spawned unarmed; the arm test means nothing");

            for (int i = 0; i < 3; i++) Hit(_armGrunt, BodyPart.RightArm, 25f);

            var armWounds = _armGrunt.GetComponent<EnemyWounds>();
            Require(armWounds.Disarmed, "three rounds in a grunt's gun arm did not make it drop the rifle");
            Require(!armAi.ranged, "a disarmed grunt is still a ranged enemy");
            Require(armAi.attackRange <= armAi.meleeRange,
                    $"a disarmed grunt still wants to fight from {armAi.attackRange:0.0}m");
            Require(GameObject.Find("DroppedRifle") != null, "no rifle fell to the floor");

            // ---- the head ------------------------------------------------------------
            Hit(_headGrunt, BodyPart.Head, 1f);
            Require(_headGrunt.GetComponent<Health>().IsDead, "a one-point headshot did not kill a grunt");

            var eliteHealth = _elite.GetComponent<Health>();
            Require(eliteHealth.Shield > 0f, "the Sentinel spawned without its shield; the elite test means nothing");
            Hit(_elite, BodyPart.Head, 1f);
            Require(!eliteHealth.IsDead, "a headshot killed a shielded elite through its armour");

            Hit(_elite, BodyPart.Torso, eliteHealth.Shield + 1f);
            Hit(_elite, BodyPart.Head, 1f);
            Require(eliteHealth.IsDead, "a headshot did not kill an elite with its shield down");

            var bossHealth = _boss.GetComponent<Health>();
            Hit(_boss, BodyPart.Head, 1f);
            Require(!bossHealth.IsDead, "one headshot killed a boss");

            // Both legs past a limp is the rule that floors anything else. Just under half
            // the pool into each, so the boss is alive and past the threshold on both.
            var bossWounds = _boss.GetComponent<EnemyWounds>();
            Hit(_boss, BodyPart.LeftLeg, bossHealth.maxHealth * 0.495f);
            Hit(_boss, BodyPart.RightLeg, bossHealth.maxHealth * 0.495f);
            Require(!bossHealth.IsDead, "the boss leg test killed the boss");
            Require(bossWounds.Legs == EnemyWounds.LegState.Limping,
                    $"a boss shot through both legs is {bossWounds.Legs}, not limping (it must never crawl)");

            // ---- the blast, and a leg kill ------------------------------------------
            var blasted = _blasted.GetComponent<Health>();
            blasted.ApplyDamage(new DamageInfo(1000f, _blasted.transform.position + Vector3.up,
                                               Vector3.forward, (Vector3.forward + Vector3.up * 0.2f).normalized, null)
            {
                fromBlast = true,
            });

            var legKilled = _legKilled.GetComponent<Health>();
            Hit(_legKilled, BodyPart.RightLeg, legKilled.maxHealth * 0.2f);
            Require(legKilled.IsDead, "the finishing leg shot did not kill");
        }

        static void Settle()
        {
            Style(_headGrunt, EnemyDeath.Style.Drop, "a headshot");
            Style(_blasted, EnemyDeath.Style.Thrown, "a blast");
            Style(_legKilled, EnemyDeath.Style.Buckle, "a leg shot");

            // Ragdolled: bodies on every segment, and the blast victim thrown clear.
            foreach (var dead in new[] { _headGrunt, _blasted, _legKilled })
            {
                var death = dead != null ? dead.GetComponent<EnemyDeath>() : null;
                Require(death != null && death.IsRagdolled, $"{Name(dead)} died and did not go limp");

                int bodies = dead != null ? dead.GetComponentsInChildren<Rigidbody>().Length : 0;
                Require(bodies >= 8, $"{Name(dead)} went limp with only {bodies} rigidbodies; the joints are missing");
            }

            if (_blasted != null && _blasted.GetComponent<EnemyDeath>() is EnemyDeath thrown && thrown.torso != null)
            {
                float travelled = Vector3.Distance(thrown.torso.position, _blasted.transform.position + Vector3.up);
                Notes.Append($"\n  blast victim's torso ended {travelled:0.00}m from where it stood");
                Require(travelled > 0.8f, $"a blast moved the body only {travelled:0.00}m");
            }

            // A standing body's torso is a metre up; a fallen one is on the floor.
            if (_headGrunt != null && _headGrunt.GetComponent<EnemyDeath>() is EnemyDeath dropped && dropped.torso != null)
            {
                float height = dropped.torso.position.y;
                Notes.Append($"\n  headshot victim's hips at {height:0.00}m after 1.6s");
                Require(height < 0.55f, $"a headshot victim's hips are still {height:0.00}m up: it did not fall");
            }

            // ---- blood --------------------------------------------------------------
            int wounds = 0;
            foreach (var enemy in new[] { _legGrunt, _armGrunt })
                if (enemy != null)
                    foreach (Transform t in enemy.GetComponentsInChildren<Transform>())
                        if (t.name == "Wound") wounds++;

            var root = GameObject.Find("BloodFX");
            int splats = 0;
            if (root != null)
                foreach (Transform t in root.transform)
                    if (t.name == "Blood") splats++;

            Notes.Append($"\n  {wounds} wounds on two bodies, {splats} splats in the world");
            Require(wounds >= 4, $"six rounds left only {wounds} wounds on the bodies they hit");
            Require(splats >= 3, $"a dozen hits left only {splats} splats of blood in the world");

            // Off means off: a hit with the setting off adds nothing to the world.
            GameSettings.Blood = false;
            int before = splats;
            var clean = Spawn("Grunt", 7);
            for (int i = 0; i < 3; i++) Hit(clean, BodyPart.Torso, 10f);

            int after = 0;
            if (root != null)
                foreach (Transform t in root.transform)
                    if (t.name == "Blood") after++;

            Require(after == before, $"with Blood off, three hits still left {after - before} splats");

            int cleanWounds = 0;
            foreach (Transform t in clean.GetComponentsInChildren<Transform>())
                if (t.name == "Wound") cleanWounds++;
            Require(cleanWounds == 0, $"with Blood off, a body still took {cleanWounds} wounds");

            // ---- voice --------------------------------------------------------------
            var voice = _legGrunt.GetComponent<EnemyVoice>();
            Require(voice != null && voice.bank != null, "the enemy prefab has no voice bank");
            if (voice != null && voice.bank != null)
            {
                foreach (var kind in new[] { EnemyVoice.Kind.Human, EnemyVoice.Kind.Creature })
                {
                    var set = voice.bank.For(kind);
                    Require(set.pain.Length > 0 && set.death.Length > 0 && set.hurt.Length > 0 &&
                            set.headshotDeath.Length > 0 && set.hunt.Length > 0,
                            $"the {kind} voice is missing clips");
                }
            }

            Require(_boss.GetComponent<EnemyVoice>().kind == EnemyVoice.Kind.Creature,
                    "the Warden speaks with a human voice; the archetype's voice did not stamp");
        }

        // ==================================================================

        /// <summary>A round through the named part's hitbox, from in front, the way Weapon sends one.</summary>
        static void Hit(GameObject enemy, BodyPart part, float amount)
        {
            if (enemy == null) return;

            foreach (var hitbox in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (hitbox.Part != part) continue;

                var collider = hitbox.GetComponent<Collider>();
                Vector3 point = collider.bounds.center + enemy.transform.forward * collider.bounds.extents.z;
                var info = new DamageInfo(amount / Mathf.Max(0.01f, hitbox.damageMultiplier), point,
                                          enemy.transform.forward, -enemy.transform.forward, null);
                hitbox.Receive(info);
                return;
            }

            throw new Exception($"{Name(enemy)} has no {part} hitbox");
        }

        static void Style(GameObject enemy, EnemyDeath.Style expected, string how)
        {
            var death = enemy != null ? enemy.GetComponent<EnemyDeath>() : null;
            if (death == null) return;

            Require(death.LastStyle == expected, $"killed by {how}, {Name(enemy)} died {death.LastStyle}, not {expected}");
        }

        static string Name(GameObject go) => go != null ? go.name : "(destroyed)";

        static void Require(bool condition, string problem)
        {
            if (!condition) Problems.Add(problem);
        }

        static void Wait(double seconds, Phase next)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds;
            _phase = next;
        }

        static bool Waiting() => EditorApplication.timeSinceStartup < _deadline;

        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            // The editor probes for an Android device on entering play mode with the
            // Android module installed, and logs the failure as an exception. Nothing to
            // do with the game.
            if (message.Contains("Unity Remote requirements check failed")) return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"[{type}] {message.Trim()}");
        }

        static void Detach()
        {
            GameSettings.Blood = _bloodBackup;
            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
        }

        static void Finish()
        {
            Detach();

            var report = new StringBuilder();
            foreach (var e in Errors) report.Append($"\n  - {e}");
            foreach (var p in Problems) report.Append($"\n  - {p}");

            if (report.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: wounds do not do what they say:{report}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify wounds passed: speed {_speedSound:0.00} -> limp {_speedLimp:0.00} " +
                      $"-> crawl {_speedCrawl:0.00}, rifle dropped, headshots kill a grunt and an unshielded " +
                      $"elite but not a boss, and bodies fall the way they were shot.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
