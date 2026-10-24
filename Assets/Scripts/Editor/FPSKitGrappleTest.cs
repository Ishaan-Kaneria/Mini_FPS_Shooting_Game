#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The grab and punch (MeleeGrapple, after the fight in the film Master), proved on the real player against real enemies:
    ///
    /// - G1 face to face with the rifle in the hands, the press takes him by the head: the rifle goes onto the back, he
    ///   is held, nothing is dealt by the grip.
    /// - G2 held, he is kept in front of the player and follows the player's turn.
    /// - G3 each press is a punch to the face: it lands, takes some of him, never finishes him, and his head answers it
    ///   within two frames. Nothing slows the world (Time.timeScale stays 1).
    /// - G4 the overhand ends an ordinary enemy and lets go of him.
    /// - G5 held and left alone, he wrenches himself free after releaseAfter and runs (Ishaan, 2026-10-22: "the enemy also
    ///   don't try to run or punch when I hold it").
    /// - G10 held and not punched, he winds up and his punch lands on the player (the view shakes, the hand is knocked).
    /// - G11 a punch to his face thrown into his wind-up stops it: nothing reaches the player.
    /// - G6 an elite at full health is not grabbed (the rifle butt instead); badly hurt, it is; the overhand leaves it on
    ///   the floor, not dead.
    /// - G7 a boss is never grabbed.
    /// - G8 with the fists up, a blow at the belly is the hook: he doubles over, and the next press takes him bent.
    /// - G3 also: the face-damage shapes are on his head, two punches cave the struck cheek in, and he is left dazed; G8:
    ///   the hook leaves him winded.
    /// - G9 holding him never moves the player: standing, the player stays put; walking, the player's root moves exactly as
    ///   the motor's own velocity says (the rule VerifyMeleeMotion proves for the other strikes), and he is carried ahead.
    ///
    /// Real player from level one of the Warehouse, on a flat floor of the test's own high above it.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyGrapple        (or FPSKit > Player > Run Grapple Test)
    ///
    /// Report: Build/Grapple/report.txt.
    /// </summary>
    public static class FPSKitGrappleTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string ReportPath = "Build/Grapple/report.txt";
        const float FloorHeight = 400f, FloorSize = 80f;

        public static bool Running { get; private set; }

        static readonly List<string> Lines = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly Stack<IEnumerator> Script = new Stack<IEnumerator>();
        static readonly List<GameObject> Spawned = new List<GameObject>();
        static int _lastFrame, _warm;
        static double _startedAt;
        static GameObject _player, _floor;
        static PlayerMotor _motor;
        static MeleeStrike _melee;
        static Vector3 _start;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;
        static float _minTimeScale = 1f;

        static int _landed, _missed;
        static GameObject _hurtBy;
        static int _hurt;
        static MeleeStrike.Strike _lastLanded;
        static int _landedFrame;

        [MenuItem("FPSKit/Player/Run Grapple Test")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            try
            {
                if (EditorSceneManager.GetActiveScene().isDirty) throw new Exception("save the open scene first");
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Lines.Clear(); Fails.Clear(); Script.Clear(); Spawned.Clear();
                _lastFrame = -1; _warm = 0; _player = null; _minTimeScale = 1f;
                _startedAt = EditorApplication.timeSinceStartup;
                Running = true;
                SaveMigration.Apply();
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
                EditorApplication.isPlaying = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[FPSKitBatch] FAILED: grapple test: " + e.Message);
                FPSKitBatch.Exit(1);
            }
        }

        public static void Batch() => Run();

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("grapple test timed out");
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
                if (Time.timeScale > 0f) Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null) { Setup(); Script.Push(Scenarios()); }
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                    if (!Spawned.Contains(ai.gameObject)) ai.gameObject.SetActive(false);
                var h = _player.GetComponent<Health>();
                if (h != null && h.Normalized < 0.9f) h.ResetHealth();
                _minTimeScale = Mathf.Min(_minTimeScale, Time.timeScale);

                while (Script.Count > 0)
                {
                    var top = Script.Peek();
                    if (!top.MoveNext()) { Script.Pop(); continue; }
                    if (top.Current is IEnumerator sub) { Script.Push(sub); continue; }
                    return;   // wait a frame
                }
                Finish();
            }
            catch (Exception e)
            {
                Fails.Add("error: " + e.Message);
                Debug.LogException(e);
                Finish();
            }
        }

        static void Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) throw new Exception("no player");
            _motor = _player.GetComponent<PlayerMotor>();
            _melee = _player.GetComponent<MeleeStrike>();
            _melee.Landed += (m, s, t) => { _landed++; _lastLanded = s; _landedFrame = Time.frameCount; };
            _melee.Missed += (m, s) => _missed++;
            var self = _player.GetComponent<Health>();
            if (self != null) self.Damaged += (h, info) => { if (_hurtBy != null && info.source == _hurtBy && info.fromMelee) _hurt++; };

            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "Grapple_Floor";
            _floor.transform.position = new Vector3(0f, FloorHeight - 0.5f, 0f);
            _floor.transform.localScale = new Vector3(FloorSize, 1f, FloorSize);
            int env = LayerMask.NameToLayer("Environment");
            if (env >= 0) _floor.layer = env;
            var surface = _floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.BuildNavMesh();
            _start = new Vector3(0f, FloorHeight, 0f);
        }

        static void Require(bool ok, string what)
        {
            Lines.Add((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) Fails.Add(what);
        }

        // ------------------------------------------------------------------

        static IEnumerator Scenarios()
        {
            var s = MeleeSettings.Active;

            // ---- G1-G4: an ordinary enemy, face to face, rifle in the hands ----
            yield return Fresh();
            var t = Enemy(Ahead(0.85f), "Grunt");
            var ai = t.GetComponent<EnemyAI>();
            var hp = t.GetComponent<Health>();
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            float full = hp.maxHealth + hp.maxShield, pool0 = Pool(hp);
            Count();
            var aimed = _melee.TryPunch();
            Require(aimed == hp && _melee.Thrown == MeleeStrike.Strike.Grab, $"G1 face to face, the press is the grab at him ({_melee.Thrown}, {(aimed != null ? aimed.name : "nobody")})");
            yield return Until(() => _melee.Holding, 1.5f);
            ai.target = _player.transform;
            ai.enabled = true;   // held: it only keeps itself in front (EnemyAI.Update's stagger branch)
            yield return Frames(2);
            Require(_melee.Holding && ai.Grappled, $"G1 he is held by the head (holding {_melee.Holding}, grappled {ai.Grappled})");
            Require(_melee.GunAway > 0.9f, $"G1 the rifle went onto the back for it ({_melee.GunAway:0.00})");
            Require(Mathf.Approximately(pool0, Pool(hp)), $"G1 the grip deals nothing ({pool0:0} -> {Pool(hp):0})");

            // G2: kept in front, and following a turn.
            yield return Seconds(0.4f);
            float apart = Flat(t.transform.position - _player.transform.position).magnitude;
            Require(Mathf.Abs(apart - s.holdDistance) < 0.2f && ai.HoldSlip < 0.2f, $"G2 held {apart:0.00} m in front (hold distance {s.holdDistance:0.00}, slip {ai.HoldSlip:0.00})");
            Look(_player.transform.position + Vector3.up * 1.4f + Quaternion.AngleAxis(25f, Vector3.up) * Forward() * 3f);
            yield return Seconds(0.6f);
            Vector3 pin = _player.transform.position + Flat(_player.transform.forward).normalized * s.holdDistance;
            float off = Flat(t.transform.position - pin).magnitude;
            Require(off < 0.3f && _melee.Holding, $"G2 he follows the player's 25-degree turn ({off:0.00} m off where he is held, still holding {_melee.Holding})");
            Look(Head(t));
            yield return Frames(3);

            // G3: the punches to the face.
            var headBone = HeadBone(t);
            for (int i = 0; i < s.facePunchesBeforeFinisher; i++)
            {
                yield return Until(() => Time.time >= _melee.ReadyAt, 1f);
                Count();
                float before = Pool(hp);
                Quaternion headBefore = headBone != null ? headBone.rotation : Quaternion.identity;
                Quaternion headAtLanding = headBefore;
                _melee.TryPunch();
                int waited = 0;
                while (_landed == 0 && _missed == 0 && waited++ < 90)
                {
                    if (headBone != null) headAtLanding = headBone.rotation;
                    yield return null;
                }
                yield return Frames(2);
                float turned = headBone != null ? Quaternion.Angle(headAtLanding, headBone.rotation) : 0f;
                Require(_landed == 1 && _lastLanded == MeleeStrike.Strike.FacePunch, $"G3 punch {i + 1} to the face lands ({_lastLanded}, landed {_landed}, missed {_missed})");
                Require(Pool(hp) < before && !hp.IsDead && Pool(hp) >= full * s.heldFloor - 1f,
                        $"G3 punch {i + 1} takes some of him and never finishes him ({before:0} -> {Pool(hp):0} of {full:0}; floor {full * s.heldFloor:0})");
                Require(headBone == null || turned > 4f, $"G3 punch {i + 1}: his head answers within two frames of the blow ({turned:0.0} degrees)");
                Require(headBone == null || turned < 70f, $"G3 punch {i + 1}: and is not wrenched round past 70 degrees ({turned:0.0})");
            }

            // G3: the face takes it (Blender's shapes on the head, FacialDamageHandler), and he is left dazed.
            var face = t.GetComponent<FacialDamageHandler>();
            float caved = face != null ? Mathf.Max(face.Weight(0), face.Weight(1)) : 0f;
            Require(face != null && face.Ready, $"G3 the face-damage shapes are on his head (handler {(face != null)}, ready {(face != null && face.Ready)})");
            Require(caved >= 50f, $"G3 two punches to the face cave his cheek in (impact weight {caved:0} of 100)");
            var body = t.GetComponent<EnemyBody>();
            Require(body != null && body.Dazed, $"G3 and leave him dazed ({(body != null && body.Dazed)})");

            // G4: the overhand.
            yield return Until(() => Time.time >= _melee.ReadyAt, 1f);
            Count();
            _melee.TryPunch();
            yield return Until(() => _landed > 0 || _missed > 0, 1.5f);
            yield return Frames(2);
            Require(_lastLanded == MeleeStrike.Strike.Overhand && hp.IsDead, $"G4 the overhand ends an ordinary enemy ({_lastLanded}, dead {hp.IsDead})");
            Require(!_melee.Holding, "G4 and lets go of him");
            Require(_minTimeScale >= 0.999f, $"G3/G4 nothing slowed the world (lowest timeScale {_minTimeScale:0.000})");

            // ---- G10, G5: held and left alone, he punches back, then wrenches free and runs ----
            yield return Fresh();
            t = Enemy(Ahead(0.85f), "Grunt");
            ai = t.GetComponent<EnemyAI>();
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            _melee.TryPunch();
            yield return Until(() => _melee.Holding, 1.5f);
            ai.target = _player.transform;
            ai.enabled = true;
            _hurtBy = t; _hurt = 0;
            float grabbedAt = Time.time;
            yield return Until(() => _hurt > 0 || !ai.Grappled, s.counterFirstAfter + s.counterWindup + s.counterSwing + 0.5f);
            float landedAfter = Time.time - grabbedAt;
            Require(_hurt > 0 && ai.CountersLanded == 1, $"G10 held and not punched, his punch lands on the player ({_hurt} blows taken, {landedAfter:0.00} s after the grab; counters {ai.CountersLanded})");
            Require(landedAfter >= s.counterFirstAfter + s.counterWindup - 0.05f, $"G10 after a wind-up the player could see ({landedAfter:0.00} s; first try at {s.counterFirstAfter:0.00} + wind-up {s.counterWindup:0.00})");
            yield return null;
            Require(_melee.GripKnock > 0.5f && _melee.Holding, $"G10 the holding hand is knocked and holds on (knock {_melee.GripKnock:0.00}, holding {_melee.Holding})");
            yield return Until(() => !ai.Grappled, s.releaseAfter + s.wrenchSeconds + 0.5f);
            float freedAt = Time.time - grabbedAt;
            yield return null;   // the hand lets go on the player's next frame
            Require(!_melee.Holding && !ai.Grappled && ai.Bolting, $"G5 left alone, he wrenches himself free ({freedAt:0.00} s after the grab; releaseAfter {s.releaseAfter:0.0} + wrench {s.wrenchSeconds:0.0}) and runs (bolting {ai.Bolting})");
            float near = Flat(t.transform.position - _player.transform.position).magnitude;
            yield return Seconds(1.2f);
            float far = Flat(t.transform.position - _player.transform.position).magnitude;
            Require(far > near + 2f, $"G5 running from the player ({near:0.0} m -> {far:0.0} m in 1.2 s)");
            ai.enabled = false;
            _hurtBy = null;

            // ---- G11: a punch into his wind-up stops it ----
            yield return Fresh();
            t = Enemy(Ahead(0.85f), "Grunt");
            ai = t.GetComponent<EnemyAI>();
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            _melee.TryPunch();
            yield return Until(() => _melee.Holding, 1.5f);
            ai.target = _player.transform;
            ai.enabled = true;
            _hurtBy = t; _hurt = 0;
            yield return Until(() => ai.Struggle == EnemyAI.HeldMove.Wind, s.counterFirstAfter + 0.5f);
            bool winding = ai.Struggle == EnemyAI.HeldMove.Wind;
            yield return Until(() => Time.time >= _melee.ReadyAt, 0.5f);
            Count();
            _melee.TryPunch();
            yield return Until(() => _landed > 0 || _missed > 0, 1f);
            var after = ai.Struggle;
            yield return Seconds(s.counterWindup + s.counterSwing);
            Require(winding && _landed == 1 && after == EnemyAI.HeldMove.Pull, $"G11 a punch thrown into his wind-up lands and stops it (winding {winding}, landed {_landed}, then {after})");
            Require(_hurt == 0 && ai.CountersLanded == 0, $"G11 and nothing reaches the player ({_hurt} blows taken, counters {ai.CountersLanded})");
            ai.enabled = false;
            _hurtBy = null;

            // ---- G6: an elite ----
            yield return Fresh();
            t = Enemy(Ahead(0.85f), "Sentinel");
            ai = t.GetComponent<EnemyAI>();
            hp = t.GetComponent<Health>();
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            Count();
            _melee.TryPunch();
            Require(_melee.Thrown != MeleeStrike.Strike.Grab, $"G6 an elite at full health is not grabbed ({_melee.Thrown})");
            yield return Seconds(0.8f);
            Require(!_melee.Holding, "G6 and is not held");
            yield return Fresh(keepEnemies: true);
            hp.ApplyDamage(new DamageInfo(Pool(hp) - (hp.maxHealth + hp.maxShield) * 0.25f, t.transform.position + Vector3.up, Vector3.back, Vector3.forward, null));
            Look(Head(t));
            yield return Frames(3);
            _melee.TryPunch();
            yield return Until(() => _melee.Holding, 1.5f);
            Require(_melee.Holding, $"G6 badly hurt ({Pool(hp) / (hp.maxHealth + hp.maxShield):0.00} of its pool), the elite is grabbed ({_melee.Thrown})");
            if (_melee.Holding)
            {
                ai.target = _player.transform;
                ai.enabled = true;
                for (int i = 0; i <= s.facePunchesBeforeFinisher; i++)
                {
                    yield return Until(() => Time.time >= _melee.ReadyAt, 1f);
                    Count();
                    _melee.TryPunch();
                    yield return Until(() => _landed > 0 || _missed > 0, 1.5f);
                }
                yield return Seconds(0.4f);
                Require(!hp.IsDead && !_melee.Holding, $"G6 the overhand leaves the elite alive and let go (dead {hp.IsDead}, holding {_melee.Holding})");
                Require(ai.Downed || ai.IsStaggered, $"G6 and on the floor or reeling (downed {ai.Downed}, staggered {ai.IsStaggered})");
                ai.enabled = false;
            }

            // ---- G7: a boss ----
            yield return Fresh();
            t = Enemy(Ahead(1.0f), "Warden");
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            _melee.TryPunch();
            yield return Seconds(0.8f);
            Require(_melee.Thrown != MeleeStrike.Strike.Grab && !_melee.Holding, $"G7 a boss is never grabbed ({_melee.Thrown}, holding {_melee.Holding})");

            // ---- G8: the hook to the belly, then taken bent ----
            yield return Fresh();
            t = Enemy(Ahead(1.3f), "Grunt");
            ai = t.GetComponent<EnemyAI>();
            hp = t.GetComponent<Health>();
            yield return Frames(3);
            // The fists up first, swung at nobody (a butt, then the chain's jab), so nothing has folded him or moved
            // his head into the crosshair; then the hook at the belly.
            Look(_player.transform.position + Vector3.up * 1.6f + Quaternion.AngleAxis(100f, Vector3.up) * Forward() * 5f);
            yield return Frames(3);
            _melee.TryPunch();
            yield return Until(() => Time.time >= _melee.ReadyAt, 1.5f);
            _melee.TryPunch();
            yield return Until(() => Time.time >= _melee.ReadyAt && _melee.GunAway > 0.9f, 1.5f);
            Look(Belly(t));
            yield return Frames(4);
            yield return Frames(2);
            Count();
            _melee.TryPunch();
            var hook = _melee.Thrown;
            yield return Until(() => _landed > 0 || _missed > 0, 1.5f);
            yield return Frames(2);
            Require(hook == MeleeStrike.Strike.BodyBlow && _lastLanded == MeleeStrike.Strike.BodyBlow, $"G8 with the fists up, the blow at the belly is the hook ({hook}, landed {_lastLanded})");
            Require(ai.DoubledOver, "G8 he doubles over");
            var gut = t.GetComponent<EnemyBody>();
            Require(gut != null && gut.Winded, "G8 and is winded by it");
            yield return Until(() => Time.time >= _melee.ReadyAt, 1f);
            Look(Head(t));
            yield return Frames(2);
            _melee.TryPunch();
            var bent = _melee.Thrown;
            yield return Until(() => _melee.Holding, 1.5f);
            Require(bent == MeleeStrike.Strike.Grab && _melee.Holding && _melee.HoldingBent, $"G8 the next press takes him bent, by the back of the head ({bent}, holding {_melee.Holding}, bent {_melee.HoldingBent})");
            ai.target = _player.transform;
            ai.enabled = true;
            yield return Seconds(0.3f);
            ai.enabled = false;

            // ---- G9: holding never moves the player ----
            yield return Fresh();
            t = Enemy(Ahead(0.85f), "Grunt");
            ai = t.GetComponent<EnemyAI>();
            yield return Frames(3);
            Look(Head(t));
            yield return Frames(3);
            _melee.TryPunch();
            yield return Until(() => _melee.Holding, 1.5f);
            ai.target = _player.transform;
            ai.enabled = true;
            yield return Frames(5);
            Vector3 stood = _player.transform.position;
            yield return Seconds(0.35f);   // all of G9 inside releaseAfter: left alone he breaks free at 2.2 s
            float drift = Flat(_player.transform.position - stood).magnitude;
            Require(_melee.Holding && drift < 0.005f, $"G9 standing and holding him, the player stays put ({drift * 1000f:0.0} mm)");
            float surge = 0f, walked = 0f;
            Vector3 last = _player.transform.position;
            for (int i = 0; i < 45; i++)
            {
                MobileInput.Move = new Vector2(0f, 0.6f);
                yield return null;
                Vector3 step = Flat(_player.transform.position - last);
                Vector3 allowed = Flat(_motor.Velocity) * Time.deltaTime;
                surge += Mathf.Max(0f, step.magnitude - allowed.magnitude - 0.0005f);
                walked += step.magnitude;
                last = _player.transform.position;
            }
            MobileInput.Move = Vector2.zero;
            float ahead = Vector3.Dot(Flat(t.transform.position - _player.transform.position), Forward());
            Require(surge < 0.01f && walked > 0.3f, $"G9 walking while holding him, the player moves only as the motor says ({walked:0.00} m walked, {surge * 1000f:0.0} mm beyond it)");
            Require(_melee.Holding && ahead > 0.4f, $"G9 and he is carried ahead, still held ({ahead:0.00} m in front, holding {_melee.Holding})");
            ai.enabled = false;
        }

        // ------------------------------------------------------------------

        static IEnumerator Fresh(bool keepEnemies = false)
        {
            if (!keepEnemies)
            {
                foreach (var go in Spawned) if (go != null) Object.Destroy(go);
                Spawned.Clear();
            }
            MobileInput.Reset();
            MobileInput.Active = true;
            GameInput.SetScheme(InputScheme.Touch);
            // Cold: no strike in flight, nobody held, the chain over, the gun back in the hands.
            float waited = 0f;
            while ((_melee.Holding || _melee.Current != MeleeStrike.Strike.None || _melee.GunAway > 0f || _melee.FistsOut || Time.time < _melee.ReadyAt + 1.6f) && waited < 8f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = _start + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            _motor.TurnTowards(_start + Vector3.up * 1.6f + Vector3.forward * 20f, 1f);
            yield return Frames(3);
        }

        static void Count()
        {
            _landed = _missed = 0;
            _lastLanded = MeleeStrike.Strike.None;
        }

        static float Pool(Health h) => h.Current + h.Shield;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static Vector3 Forward() => Flat(_player.transform.forward).normalized;
        static Vector3 Ahead(float metres) => _start + Vector3.forward * metres;
        static void Look(Vector3 at) => _motor.TurnTowards(at, 1f);

        static Transform HeadBone(GameObject enemy)
        {
            var anim = enemy.GetComponentInChildren<Animator>();
            return anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        }

        static Vector3 Head(GameObject enemy)
        {
            var body = enemy.GetComponent<EnemyBody>();
            return body != null ? body.EyePosition : enemy.transform.position + Vector3.up * 1.6f;
        }

        static Vector3 Belly(GameObject enemy)
        {
            foreach (var hb in enemy.GetComponentsInChildren<Hitbox>())
                if (hb.Part == BodyPart.LowerTorso) { var c = hb.GetComponent<Collider>(); if (c != null) return c.bounds.center; }
            return enemy.transform.position + Vector3.up * 1.0f;
        }

        static GameObject Enemy(Vector3 at, string archetype)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (NavMesh.SamplePosition(at, out var hit, 1f, NavMesh.AllAreas)) at = hit.position;
            Vector3 face = _start - at; face.y = 0f;
            var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.back));
            go.name = "Grapple_" + archetype + Spawned.Count;
            Spawned.Add(go);
            FPSKitEnemyRoster.GetOrCreate(archetype).ApplyTo(go);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.enabled = false;
            Physics.SyncTransforms();
            return go;
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }
        static IEnumerator Until(Func<bool> done, float timeout) { float end = Time.time + timeout; while (!done() && Time.time < end) yield return null; }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            foreach (var go in Spawned) if (go != null) Object.Destroy(go);
            if (_floor != null) Object.Destroy(_floor);
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null; _clockBackup = -1f;
            MobileInput.Reset();
            MobileInput.Active = false;
            GameInput.SetScheme(InputScheme.KeyboardMouse);
            PlayerMotor.InputEnabled = true;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            Running = false;

            string report = (Fails.Count == 0 ? "grapple test passed\n" : $"grapple test FAILED ({Fails.Count}):\n") + string.Join("\n", Lines);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report);
            if (Fails.Count == 0) Debug.Log("[FPSKitBatch] " + report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + report);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
