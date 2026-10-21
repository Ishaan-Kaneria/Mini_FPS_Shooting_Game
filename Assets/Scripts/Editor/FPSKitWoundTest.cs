#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Proves that where a round lands changes what it does, by the rules in CLAUDE.md
    /// ("Gameplay invariants") and <see cref="EnemyWounds"/>: real enemy prefabs, real archetypes,
    /// in the AI test scene (a navmesh, so they stand and fall as in a level), shot through their
    /// real hitboxes. Every amount is worked out from the body's own profile
    /// (<see cref="EnemyWounds.CrippleDamage"/>), so retuning the numbers does not break it; only
    /// breaking a rule does.
    ///
    /// - a grunt's leg: Wounded (a little slower), then Crippled (limps, slower), the other leg
    ///   Crippled too (crawls, slowest); seconds later it is still crawling;
    /// - a grunt with one crippled leg still limps seconds later (states never improve);
    /// - a grunt's arms: one crippled holds the rifle one-handed, both drop it (out of its hands)
    ///   and it closes to fight with its hands;
    /// - a limb hit never kills: twenty huge rounds into the limbs leave it alive, at or above the
    ///   floor; one round to the chest then finishes it;
    /// - a grunt's head, for one point: dead, limp and on the floor; a shielded elite survives a
    ///   headshot until its shield is down; a boss survives one, never crawls, never drops its
    ///   rifle and never loses a limb;
    /// - a blast kill: limp, and thrown;
    /// - dress by role (cap on every soldier, backpack on elites and bosses, the outfit on all);
    /// - blood: wounds on bodies, splats in the world, and none with the setting off;
    /// - every enemy a human voice; the prefab 10% over a person's size.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyWounds
    /// </summary>
    public static class FPSKitWoundTest
    {
        const string PrefabPath = "Assets/FPSKit_Generated/Enemy.prefab";
        const double HardTimeout = 600.0;

        static readonly List<string> Problems = new List<string>();
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();
        static bool _bloodBackup;
        static int _step, _lastFrame = -1;
        static float _t0;
        static double _realStart;

        static GameObject _player, _leg, _limp, _arms, _never, _head, _elite, _boss, _blasted;
        static float _speedSound, _speedWounded, _speedLimp, _speedCrawl;
        static Vector3 _blastedFrom;
        static bool _bossRanged;

        public static void VerifyWounds()
        {
            try
            {
                Problems.Clear(); Errors.Clear(); Notes.Clear();
                _step = 0; _lastFrame = -1;
                FPSKitAITestScene.Build();
                _bloodBackup = GameSettings.Blood;
                GameSettings.Blood = true;
                _realStart = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnGameLog;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
                Debug.Log("[FPSKitBatch] wound test: a body must answer to where it is shot");
            }
            catch (Exception e)
            {
                Errors.Add(e.ToString());
                Finish();
            }
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _realStart > HardTimeout)
                    throw new Exception($"wound test exceeded {HardTimeout}s at step {_step}");
                if (!EditorApplication.isPlaying)
                {
                    if (_step == 0) { EditorApplication.EnterPlaymode(); _step = 1; }
                    return;
                }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 30f;
                // Nobody here is testing the player: keep whatever stands for it alive.
                if (_player != null && _player.GetComponent<Health>() is Health ph && ph.Normalized < 0.9f) ph.ResetHealth();

                float since = Time.time - _t0;
                switch (_step)
                {
                    case 1: SaveMigration.Apply(); Spawn(); Next(2); return;
                    // Each blow a moment apart, so no two add up to one heavy hit (a knockdown).
                    case 2: if (since > 0.5f) { LegWounded(); Next(3); } return;
                    case 3: if (since > 0.4f) { LegCrippled(); Next(4); } return;
                    case 4: if (since > 0.4f) { LegsBoth(); Next(5); } return;
                    case 5: if (since > 0.4f) { ArmOne(); Next(6); } return;
                    case 6: if (since > 0.4f) { ArmBoth(); Next(7); } return;
                    case 7: if (NeverKills(since)) Next(8); return;
                    case 8: if (since > 0.4f) { Heads(); Next(9); } return;
                    case 9: if (Boss(since)) Next(10); return;
                    case 10: Blast(); Next(11); return;
                    case 11: if (since > 3f) { Settled(); Next(12); } return;
                    case 12: Finish(); return;
                }
            }
            catch (Exception e)
            {
                Errors.Add(e.ToString());
                Finish();
            }
        }

        static void Next(int step) { _step = step; _t0 = Time.time; }

        static void Spawn()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            _leg = Make("Grunt", "Leg", 0);
            _limp = Make("Grunt", "Limp", 1);
            _arms = Make("Grunt", "Arms", 2);
            _never = Make("Grunt", "NeverKills", 3);
            _head = Make("Grunt", "Head", 4);
            _elite = Make("Sentinel", "Elite", 5);
            _boss = Make("Warden", "Boss", 6);
            _blasted = Make("Grunt", "Blasted", 7);

            var w = Wounds(_leg);
            Require(w != null, "the enemy prefab has no EnemyWounds");
            _speedSound = w != null ? w.MoveSpeedMultiplier : 0f;
            // The Warden fights hand to hand by design: what matters is that its arms change nothing.
            _bossRanged = _boss.GetComponent<EnemyAI>().ranged;
        }

        static GameObject Make(string archetype, string what, int slot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new Exception($"{PrefabPath} does not exist. Run FPSKit > Enemy > Build Rigged Soldier.");
            var at = new Vector3(slot * 3f - 10.5f, 0f, 12f);
            var enemy = Object.Instantiate(prefab, at, Quaternion.LookRotation(-at.normalized));
            enemy.name = $"{archetype}_{what}";
            FPSKitEnemyRoster.GetOrCreate(archetype).ApplyTo(enemy);
            return enemy;
        }

        // ==================================================================
        // The steps.
        // ==================================================================

        static void LegWounded()
        {
            var w = Wounds(_leg);
            // Just past the line that wounds the leg, well short of the one that cripples it.
            Book(_leg, BodyPart.LeftLeg, PartHealth(w, BodyPart.LeftLeg) * w.Body.woundedAt * 1.2f);
            _speedWounded = w.MoveSpeedMultiplier;
            Require(w.State(BodyPart.LeftLeg) == EnemyWounds.LimbState.Wounded,
                    $"a round past the wound line left a grunt's leg {w.State(BodyPart.LeftLeg)}, not Wounded");
            Require(w.Legs == EnemyWounds.LegState.Sound, $"a wounded (not crippled) leg already has it {w.Legs}");
        }

        static void LegCrippled()
        {
            var w = Wounds(_leg);
            Book(_leg, BodyPart.LeftLeg, w.CrippleDamage(BodyPart.LeftLeg));
            _speedLimp = w.MoveSpeedMultiplier;
            Require(w.State(BodyPart.LeftLeg) == EnemyWounds.LimbState.Crippled,
                    $"a leg hit past its cripple line is {w.State(BodyPart.LeftLeg)}, not Crippled");
            Require(w.Legs == EnemyWounds.LegState.Limping, $"one crippled leg left a grunt {w.Legs}, not limping");

            // The one that will be checked, much later, for still limping.
            var lw = Wounds(_limp);
            Book(_limp, BodyPart.RightLeg, lw.CrippleDamage(BodyPart.RightLeg) * 1.1f);
            Require(lw.Legs == EnemyWounds.LegState.Limping, $"one crippled leg left the second grunt {lw.Legs}, not limping");
        }

        static void LegsBoth()
        {
            var w = Wounds(_leg);
            Book(_leg, BodyPart.RightLeg, w.CrippleDamage(BodyPart.RightLeg) * 1.1f);
            _speedCrawl = w.MoveSpeedMultiplier;
            Require(!HealthOf(_leg).IsDead, "crippling both legs killed a grunt");
            Require(w.Legs == EnemyWounds.LegState.Crawling, $"both legs crippled left a grunt {w.Legs}, not crawling");
            Require(_speedSound > _speedWounded && _speedWounded > _speedLimp && _speedLimp > _speedCrawl,
                    $"speed did not fall with each wound (sound {_speedSound:0.00}, wounded {_speedWounded:0.00}, " +
                    $"limping {_speedLimp:0.00}, crawling {_speedCrawl:0.00})");
            var ai = _leg.GetComponent<EnemyAI>();
            Require(ai != null && ai.Crawling, "EnemyAI does not know its enemy is crawling");
        }

        static void ArmOne()
        {
            var w = Wounds(_arms);
            var ai = _arms.GetComponent<EnemyAI>();
            Require(ai != null && ai.ranged, "a grunt spawned unarmed; the arm test means nothing");
            Book(_arms, BodyPart.RightArm, w.CrippleDamage(BodyPart.RightArm) * 1.1f);
            Require(w.State(BodyPart.RightArm) == EnemyWounds.LimbState.Crippled, $"the gun arm is {w.State(BodyPart.RightArm)}, not Crippled");
            Require(w.OneHanded, "one crippled arm did not leave it holding the rifle one-handed");
            Require(!w.Disarmed && ai.ranged, "one crippled arm took the rifle off it (both are needed)");
        }

        static void ArmBoth()
        {
            var w = Wounds(_arms);
            var ai = _arms.GetComponent<EnemyAI>();
            Book(_arms, BodyPart.LeftArm, w.CrippleDamage(BodyPart.LeftArm) * 1.1f);
            Require(w.Disarmed, "both arms crippled and it still holds the rifle (Disarmed is false)");
            Require(!ai.ranged, "a disarmed grunt is still a ranged enemy");
            Require(ai.attackRange <= ai.meleeRange, $"a disarmed grunt still wants to fight from {ai.attackRange:0.0}m");
        }

        static int _neverShots;

        /// <summary>Twenty huge rounds into the limbs, one every few frames: alive at the floor; then the chest finishes it.</summary>
        static bool NeverKills(float since)
        {
            var h = HealthOf(_never);
            var w = Wounds(_never);
            if (_neverShots < 20)
            {
                if (since < _neverShots * 0.1f) return false;
                var limb = new[] { BodyPart.LeftLeg, BodyPart.RightShin, BodyPart.LeftForearm, BodyPart.RightArm }[_neverShots % 4];
                Book(_never, limb, h.maxHealth * 3f);
                _neverShots++;
                return false;
            }
            float floor = w.Body.limbCoreFloor * h.maxHealth;
            Notes.Append($"\n  twenty limb rounds of 3x its health left it at {h.Current + h.Shield:0.0} of {h.maxHealth:0} (floor {floor:0.0})");
            Require(!h.IsDead, "limb rounds killed a grunt (a limb hit never kills)");
            Require(h.Current + h.Shield >= floor - 0.01f, $"limb rounds took it to {h.Current + h.Shield:0.0}, under the floor {floor:0.0}");
            Book(_never, BodyPart.Torso, h.maxHealth * 0.5f);
            Require(h.IsDead, "a round to the chest did not finish a body held at the floor by limb rounds");
            return true;
        }

        static void Heads()
        {
            Book(_head, BodyPart.Head, 1f);
            Require(HealthOf(_head).IsDead, "a one-point headshot did not kill a grunt");

            var eh = HealthOf(_elite);
            Require(eh.Shield > 0f, "the Sentinel spawned without its shield; the elite test means nothing");
            Book(_elite, BodyPart.Head, 1f);
            Require(!eh.IsDead, "a headshot killed a shielded elite through its armour");
            Book(_elite, BodyPart.Torso, eh.Shield + 1f);
            Book(_elite, BodyPart.Head, 1f);
            Require(eh.IsDead, "a headshot did not kill an elite with its shield down");

            var bh = HealthOf(_boss);
            Book(_boss, BodyPart.Head, 1f);
            Require(!bh.IsDead, "one headshot killed a boss");
        }

        static int _bossStep;

        static bool Boss(float since)
        {
            var w = Wounds(_boss);
            var ai = _boss.GetComponent<EnemyAI>();
            var limbs = new[] { BodyPart.LeftLeg, BodyPart.RightLeg, BodyPart.LeftArm, BodyPart.RightArm };
            if (_bossStep < limbs.Length)
            {
                if (since < _bossStep * 0.4f) return false;
                Book(_boss, limbs[_bossStep], w.CrippleDamage(limbs[_bossStep]) * 1.2f);
                _bossStep++;
                return false;
            }
            Require(!HealthOf(_boss).IsDead, "crippling a boss's four limbs killed it");
            Require(w.Legs == EnemyWounds.LegState.Limping, $"a boss with both legs crippled is {w.Legs}, not limping (it must never crawl)");
            Require(!w.Disarmed && ai.ranged == _bossRanged, "a boss with both arms crippled let go of its weapon");
            w.Sever(BodyPart.LeftLeg);
            Require(w.State(BodyPart.LeftLeg) != EnemyWounds.LimbState.Severed, "a boss lost a leg (a boss never loses a limb)");
            return true;
        }

        static void Blast()
        {
            _blastedFrom = Hips(_blasted);
            var h = HealthOf(_blasted);
            // From behind it, towards the player: across the open ground, where nothing stops the throw.
            Vector3 toward = Flat(-_blasted.transform.position).normalized;
            h.ApplyDamage(new DamageInfo(h.maxHealth * 10f, _blasted.transform.position + Vector3.up - toward,
                                         -toward, (toward + Vector3.up * 0.2f).normalized, null)
            {
                fromBlast = true,
                heavy = true,
            });
            Require(h.IsDead, "a blast of ten times its health did not kill a grunt");
        }

        static void Settled()
        {
            // ---- states never improve, seconds and a fall later -------------------------
            var lw = Wounds(_leg);
            Require(lw.Legs == EnemyWounds.LegState.Crawling && _leg.GetComponent<EnemyAI>().Crawling,
                    $"the crawler is {lw.Legs} three seconds on (nothing stands a crawler up)");
            var limp = Wounds(_limp);
            Require(limp.Legs == EnemyWounds.LegState.Limping && limp.State(BodyPart.RightLeg) == EnemyWounds.LimbState.Crippled,
                    $"the limper is {limp.Legs} three seconds on (a crippled leg limps for life)");

            // The rifle out of its hands: on the floor or gone.
            var rifle = _arms.GetComponentInChildren<EnemyRifle>(true);
            Require(rifle != null && !rifle.InHands && !rifle.OnBody,
                    $"a grunt with both arms crippled still has its rifle ({(rifle != null ? rifle.State.ToString() : "none")})");

            // ---- the dead go limp, and fall the way they were hit ------------------------
            foreach (var dead in new[] { _head, _never, _blasted })
            {
                var rag = dead.GetComponent<RagdollController>();
                Require(rag != null && rag.IsRagdolled, $"{dead.name} died and did not go limp");
            }
            float headHips = Hips(_head).y - _head.transform.position.y;
            Notes.Append($"\n  headshot victim's hips {headHips:0.00} m off the ground after 3 s");
            Require(headHips < 0.55f, $"a headshot victim's hips are still {headHips:0.00} m up: it did not fall");
            float thrown = Vector3.Distance(Flat(Hips(_blasted)), Flat(_blastedFrom));
            Notes.Append($"\n  blast victim's hips ended {thrown:0.00} m from where it stood");
            Require(thrown > 0.8f, $"a blast moved the body only {thrown:0.00} m");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var prefabHealth = prefab != null ? prefab.GetComponent<Health>() : null;
            Require(prefabHealth != null && !prefabHealth.destroyOnDeath,
                    "the enemy prefab still destroys its body: corpses must stay until the level ends");

            // ---- dress by role ----------------------------------------------------------
            foreach (var body in new[] { _arms, _elite, _boss })
                foreach (var piece in new[] { "Outfit_Head", "Outfit_Hands", "Outfit_Shirt", "Outfit_Pants", "Outfit_Boots", "Outfit_Vest" })
                    Require(Worn(body, piece), $"{body.name} is not wearing {piece}");
            Require(Worn(_arms, "Kit_Human_Cap") && !Worn(_arms, "Kit_Elite_Backpack"),
                    "a grunt is not dressed as an ordinary soldier (cap on, no backpack)");
            Require(Worn(_elite, "Kit_Human_Cap") && Worn(_elite, "Kit_Elite_Backpack"),
                    "the Sentinel is not dressed as an elite (cap and backpack on)");
            Require(Worn(_boss, "Kit_Human_Cap") && Worn(_boss, "Kit_Elite_Backpack"),
                    "the Warden is not dressed as a boss soldier (cap and backpack on)");

            // ---- blood -------------------------------------------------------------------
            int wounds = Count(_leg, "Wound") + Count(_arms, "Wound");
            int splats = Splats();
            Notes.Append($"\n  {wounds} wounds on two bodies, {splats} splats in the world");
            Require(wounds >= 2, $"six rounds left only {wounds} wounds on the bodies they hit");
            Require(splats >= 3, $"a dozen hits left only {splats} splats of blood in the world");

            // Off means off: in one frame (no bleeding drips in between), three hits add nothing.
            GameSettings.Blood = false;
            var clean = Make("Grunt", "Clean", 8);
            int before = Splats();
            for (int i = 0; i < 3; i++) Book(clean, BodyPart.Torso, 10f);
            Require(Splats() == before, $"with Blood off, three hits still left {Splats() - before} splats");
            Require(Count(clean, "Wound") == 0, $"with Blood off, a body still took {Count(clean, "Wound")} wounds");
            GameSettings.Blood = true;

            // ---- voice and size -----------------------------------------------------------
            var voice = _leg.GetComponent<EnemyVoice>();
            Require(voice != null && voice.bank != null, "the enemy prefab has no voice bank");
            if (voice != null && voice.bank != null)
            {
                var set = voice.bank.For(EnemyVoice.Kind.Human);
                Require(set.pain.Length > 0 && set.death.Length > 0 && set.hurt.Length > 0 &&
                        set.headshotDeath.Length > 0 && set.hunt.Length > 0, "the human voice is missing clips");
            }
            // Every enemy is a person: Ishaan heard the creature growls as evil noise.
            foreach (var name in FPSKitEnemyRoster.Names)
                Require(FPSKitEnemyRoster.GetOrCreate(name).voice == EnemyVoice.Kind.Human, $"{name} does not have a human voice");
            Require(prefab != null && Mathf.Abs(prefab.transform.localScale.y - 1.1f) < 0.01f,
                    $"the enemy prefab is scaled {(prefab != null ? prefab.transform.localScale.y : 0f):0.00}, not 1.10");
        }

        // ==================================================================
        // Helpers.
        // ==================================================================

        static EnemyWounds Wounds(GameObject g) => g.GetComponent<EnemyWounds>();
        static Health HealthOf(GameObject g) => g.GetComponent<Health>();

        /// <summary>A limb's own health in points: what its wound and cripple lines are fractions of.</summary>
        static float PartHealth(EnemyWounds w, BodyPart limb) => w.CrippleDamage(limb) / Mathf.Max(0.01f, w.Body.crippledAt);

        /// <summary>
        /// A round through the hitbox for <paramref name="part"/>, from the front, sized so that
        /// <paramref name="booked"/> points land on the part once its own multiplier is applied.
        /// </summary>
        static void Book(GameObject enemy, BodyPart part, float booked)
        {
            foreach (var hb in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != part) continue;
                float mult = hb.profile != null ? hb.profile.For(part) : hb.damageMultiplier;
                Vector3 point = hb.transform.position;
                Vector3 dir = -enemy.transform.forward;
                hb.Receive(new DamageInfo(booked / Mathf.Max(0.01f, mult), point, -dir, dir, _player));
                return;
            }
            throw new Exception($"{enemy.name} has no {part} hitbox");
        }

        static Vector3 Hips(GameObject g)
        {
            var anim = g.GetComponentInChildren<Animator>();
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            return hips != null ? hips.position : g.transform.position;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static bool Worn(GameObject enemy, string part)
        {
            foreach (var t in enemy.GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t.gameObject.activeInHierarchy;
            return false;
        }

        static int Count(GameObject enemy, string name)
        {
            int n = 0;
            foreach (var t in enemy.GetComponentsInChildren<Transform>(true)) if (t.name == name) n++;
            return n;
        }

        static int Splats()
        {
            var root = GameObject.Find("BloodFX");
            int n = 0;
            if (root != null) foreach (Transform t in root.transform) if (t.name == "Blood" && t.gameObject.activeSelf) n++;
            return n;
        }

        static void Require(bool condition, string problem)
        {
            if (!condition) Problems.Add(problem);
        }

        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            // The editor's own log lines (the Android device probe, store packs' windows in batch mode). Not the game.
            if (FPSKitPlayMode.IsEditorNoise(message)) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"[{type}] {message.Trim()}");
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
            Time.captureDeltaTime = 0f;
            GameSettings.Blood = _bloodBackup;
            _neverShots = 0; _bossStep = 0;

            var report = new StringBuilder();
            foreach (var e in Errors) report.Append($"\n  - {e}");
            foreach (var p in Problems) report.Append($"\n  - {p}");
            bool ok = report.Length == 0;
            if (ok)
                Debug.Log($"[FPSKitBatch] verify wounds passed: speed {_speedSound:0.00} -> wounded {_speedWounded:0.00} -> " +
                          $"limp {_speedLimp:0.00} -> crawl {_speedCrawl:0.00}; states held; limb rounds never killed; " +
                          $"headshots kill a grunt and an unshielded elite but not a boss; the dead went limp.{Notes}");
            else
                Debug.LogError($"[FPSKitBatch] FAILED: wounds do not do what they say:{report}\n{Notes}");

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () =>
            {
                FPSKitPlayMode.RestoreStartScene();
                FPSKitBatch.Exit(ok ? 0 : 1);
            };
        }
    }
}
#endif
