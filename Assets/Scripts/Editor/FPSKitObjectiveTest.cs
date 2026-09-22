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
    /// Plays every objective in a real arena and asserts the three things each one has to
    /// do: attach, say what it wants, and put something in the world when it claims to
    /// have marked somewhere.
    ///
    /// <b>The invariant it exists for is the last assertion, not the first.</b> An
    /// objective may change what scores and may hold a cleared roster open; it may never
    /// change what *ends* a level, because in a real arena an enemy that falls through a
    /// gap stays alive forever and any completion requirement is a softlock. So the last
    /// pass here sets an objective that can never be satisfied -- an extraction on a
    /// level nobody is going to clear -- and fails unless the clock still ends it.
    ///
    /// Without something like this the objectives are exercised by nobody until a player
    /// has the finished game, and an objective that fails to wire does not throw or log:
    /// it produces a level that looks ordinary and quietly cannot be three-starred.
    /// </summary>
    public static class FPSKitObjectiveTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 420.0;

        enum Phase { Enter, Arm, Watch, Probe, Next, Clock, AwaitClock, Judge }

        static Phase _phase;
        static double _startedAt;
        static double _until;

        static readonly StringBuilder Notes = new StringBuilder();
        static readonly List<string> Errors = new List<string>();

        /// <summary>Every objective that is not a plain clear, in enum order.</summary>
        static readonly LevelSet.Objective[] Tried =
        {
            LevelSet.Objective.OneMagazine,
            LevelSet.Objective.Hunt,
            LevelSet.Objective.Hold,
            LevelSet.Objective.Blackout,
            LevelSet.Objective.Extraction,
            LevelSet.Objective.Disposal
        };

        static int _at;
        static bool _clockEndedIt;
        static float _huntPaid = -1f;
        static float _sunBefore = -1f;

        // ---- the level this test borrows, and everything it changed on it ----------
        static LevelSet _set;
        static LevelSet.Level _level;
        static LevelSet.Objective _objectiveBackup;
        static float _timeBackup, _weightBackup, _briefingBackup;
        static int _countBackup, _aliveBackup;
        static bool _bossBackup;
        static bool _borrowed;

        // ==================================================================
        public static void VerifyObjectives()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();

                _at = 0;
                _clockEndedIt = false;
                _huntPaid = -1f;
                _sunBefore = -1f;
                _phase = Phase.Enter;
                _startedAt = EditorApplication.timeSinceStartup;

                SaveMigration.Apply();

                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] objective test: six objectives must arm, say what they " +
                          "want, and never take the ending away from the clock");
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"objective test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        Wait(2.0, Phase.Arm);
                        return;

                    case Phase.Arm:
                    {
                        if (Waiting()) return;

                        var manager = Manager();
                        Borrow(manager);

                        _level.objective = Tried[_at];
                        _level.objectiveWeight = 6f;

                        // Long enough that nothing finishes while it is being looked at,
                        // and short enough that six of them fit in one run.
                        _level.timeLimit = 90f;
                        _level.briefingTime = 0.2f;
                        _level.enemyCount = 6;
                        _level.maxAliveAtOnce = 3;
                        _level.hasBoss = false;

                        if (_level.objective == LevelSet.Objective.Blackout && _sunBefore < 0f)
                        {
                            var sun = Sun();
                            _sunBefore = sun != null ? sun.intensity : -1f;
                        }

                        manager.RestartLevel();

                        // Long enough for the briefing to run out, the first enemies to
                        // arrive and a site to be sampled off the navmesh. Disposal waits
                        // longer because its first charge is on a fuse before it is even
                        // placed -- checked at three seconds it would report zero charges
                        // and pass, which is a test that cannot tell the objective from a
                        // stub that does nothing.
                        Wait(_level.objective == LevelSet.Objective.Disposal ? 8.0 : 3.5,
                             Phase.Watch);
                        return;
                    }

                    case Phase.Watch:
                    {
                        if (Waiting()) return;

                        var manager = Manager();
                        Inspect(manager, Tried[_at]);

                        // The hunt has to actually pay. Everything else here proves the
                        // objective armed; this is the only assertion that the weight it
                        // is worth ever reaches the score, which is the whole point of
                        // marking a runner in the first place.
                        if (Tried[_at] == LevelSet.Objective.Hunt)
                        {
                            manager.KillAllEnemies();
                            Wait(1.0, Phase.Probe);
                            return;
                        }

                        _phase = Phase.Next;
                        return;
                    }

                    case Phase.Probe:
                    {
                        if (Waiting()) return;

                        var manager = Manager();
                        _huntPaid = manager.Objective != null ? manager.Objective.EarnedWeight : 0f;

                        Notes.Append($"\n  the runner paid {_huntPaid:0.#} of {_level.objectiveWeight:0.#}");

                        _phase = Phase.Next;
                        return;
                    }

                    case Phase.Next:
                        _at++;
                        _phase = _at < Tried.Length ? Phase.Arm : Phase.Clock;
                        return;

                    case Phase.Clock:
                    {
                        var manager = Manager();

                        // An objective that cannot be satisfied, on a level whose roster
                        // is killed for it. If anything about the objective has crept
                        // into the ending, this run never finishes and the timeout is
                        // what reports it.
                        _level.objective = LevelSet.Objective.Extraction;
                        _level.timeLimit = 6f;
                        _level.briefingTime = 0.2f;
                        _level.enemyCount = 1;
                        _level.maxAliveAtOnce = 1;
                        _level.hasBoss = false;

                        manager.RestartLevel();
                        Wait(1.5, Phase.AwaitClock);
                        return;
                    }

                    case Phase.AwaitClock:
                    {
                        if (Waiting()) return;

                        var manager = Manager();

                        // Everything dead, objective unreachable on purpose: the only
                        // thing left that can end this level is the clock.
                        manager.KillAllEnemies();

                        if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout - 30.0)
                            throw new Exception("the clock never ended a level whose objective " +
                                                "could not be satisfied");

                        if (!manager.IsFinished) return;

                        _clockEndedIt = true;
                        Notes.Append($"\n  unsatisfiable objective: the clock still ended it " +
                                     $"({manager.Result.ending})");

                        _phase = Phase.Judge;
                        return;
                    }

                    case Phase.Judge:
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// What each objective has to have done three and a half seconds in.
        ///
        /// Deliberately not "is it complete": holding ground takes twenty seconds and an
        /// extraction cannot even begin until the roster is down, so a test that waited
        /// for completion would be a test of the clock. What is checked is that it exists,
        /// that it is asking for something in words, and that anything it says it marked
        /// is actually in the world -- which is the failure mode that produces a level
        /// telling the player to reach a place that was never created.
        /// </summary>
        static void Inspect(LevelManager manager, LevelSet.Objective kind)
        {
            var objective = manager.Objective;

            if (objective == null)
            {
                Errors.Add($"{kind}: no objective was attached to the level");
                return;
            }

            if (string.IsNullOrWhiteSpace(manager.ObjectiveLine))
                Errors.Add($"{kind}: the HUD is told nothing, so the level asks for something " +
                           "without saying what");

            int beacons = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<MinimapMarker>(
                         FindObjectsSortMode.None))
                if (go != null && go.name.StartsWith("Objective_")) beacons++;

            switch (kind)
            {
                case LevelSet.Objective.OneMagazine:
                {
                    var weapon = manager.player != null
                        ? manager.player.GetComponentInChildren<Weapon>()
                        : null;

                    if (weapon == null) Errors.Add("OneMagazine: the player has no weapon to lock");
                    else if (!weapon.reloadLocked)
                        Errors.Add("OneMagazine: the gun can still reload, so the level asks for " +
                                   "nothing at all");

                    if (objective.AmmoDropBonus <= 0f)
                        Errors.Add("OneMagazine: kills do not resupply, so a player who misses " +
                                   "enough is holding an empty gun with no way to refill it");
                    break;
                }

                case LevelSet.Objective.Hunt:
                {
                    bool running = false;

                    foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(
                                 FindObjectsSortMode.None))
                        if (ai != null && ai.forcedFlight) running = true;

                    if (!running)
                        Errors.Add("Hunt: nothing in the level is running, so there is no quarry");

                    if (beacons < 1)
                        Errors.Add("Hunt: the runner is not marked, so the objective is to find " +
                                   "one enemy among six that look identical");
                    break;
                }

                case LevelSet.Objective.Hold:
                case LevelSet.Objective.Extraction:
                    if (beacons < 1)
                        Errors.Add($"{kind}: nothing was placed in the world, so the level marks " +
                                   "ground that does not exist");
                    break;

                case LevelSet.Objective.Blackout:
                {
                    var sun = Sun();

                    if (sun != null && _sunBefore > 0f && sun.intensity >= _sunBefore * 0.9f)
                        Errors.Add("Blackout: the lights are still on");

                    var map = UnityEngine.Object.FindAnyObjectByType<Minimap>();

                    if (map != null && map.view != null && map.view.gameObject.activeInHierarchy)
                        Errors.Add("Blackout: the minimap is still up, which is most of what a " +
                                   "player navigates by -- the blackout is then cosmetic");
                    break;
                }

                case LevelSet.Objective.Disposal:
                {
                    if (beacons < 1)
                        Errors.Add("Disposal: no charge was ever placed, so the level asks the " +
                                   "player to defuse nothing");

                    if (objective.Satisfied)
                        Errors.Add("Disposal: the objective reports itself done before a single " +
                                   "charge has been resolved");

                    // What a charge going off actually costs. Driven directly rather than
                    // waited out: a fuse is twenty-six seconds and the thing being checked
                    // is one subtraction, not the timer.
                    float before = manager.TimeRemaining;
                    manager.PenaliseClock(5f);

                    if (manager.TimeRemaining > before - 4f)
                        Errors.Add($"Disposal: a charge costs no clock ({before:0.0}s -> " +
                                   $"{manager.TimeRemaining:0.0}s), so its fuse is a decoration");
                    break;
                }
            }

            Notes.Append($"\n  {kind,-12} \"{manager.ObjectiveLine}\"  ({beacons} marked)");
        }

        // ==================================================================
        static LevelManager Manager()
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
            if (manager == null) throw new Exception("the arena has no LevelManager");

            return manager;
        }

        static Light Sun()
        {
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != null && light.type == LightType.Directional) return light;

            return null;
        }

        /// <summary>
        /// Takes a copy of the level this test is about to rewrite.
        ///
        /// The LevelSet is a shared asset on disk, so everything changed here is put back
        /// in Detach -- a check that left level one of the first arena as a ninety second
        /// extraction would be a check that broke the game in order to pass.
        /// </summary>
        static void Borrow(LevelManager manager)
        {
            if (_borrowed) return;

            _set = manager.levels;
            _level = manager.Level;

            if (_set == null || _level == null)
                throw new Exception("the arena has no level set to borrow");

            _objectiveBackup = _level.objective;
            _weightBackup = _level.objectiveWeight;
            _timeBackup = _level.timeLimit;
            _briefingBackup = _level.briefingTime;
            _countBackup = _level.enemyCount;
            _aliveBackup = _level.maxAliveAtOnce;
            _bossBackup = _level.hasBoss;

            _borrowed = true;
        }

        static void GiveBack()
        {
            if (!_borrowed || _level == null) return;

            _level.objective = _objectiveBackup;
            _level.objectiveWeight = _weightBackup;
            _level.timeLimit = _timeBackup;
            _level.briefingTime = _briefingBackup;
            _level.enemyCount = _countBackup;
            _level.maxAliveAtOnce = _aliveBackup;
            _level.hasBoss = _bossBackup;

            EditorUtility.SetDirty(_set);
            AssetDatabase.SaveAssets();

            _borrowed = false;
        }

        static void Wait(double seconds, Phase next)
        {
            _until = EditorApplication.timeSinceStartup + seconds;
            _phase = next;
        }

        static bool Waiting() => EditorApplication.timeSinceStartup < _until;

        static void Detach()
        {
            GiveBack();

            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;
        }

        static void Finish()
        {
            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            if (_huntPaid < 0f)
                problems.Append("\n  - the hunt was never probed, so nothing proved its weight is " +
                                "reachable");
            else if (_huntPaid <= 0f)
                problems.Append("\n  - killing the marked runner paid nothing: the hunt is worth " +
                                "weight the player can never earn, so the level caps below three " +
                                "stars however well it is played");

            if (!_clockEndedIt)
                problems.Append("\n  - a level whose objective could not be satisfied did not end " +
                                "on the clock: an objective has taken the ending away, and in a " +
                                "real arena that is a softlock");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] verify objectives FAILED:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify objectives passed: {Tried.Length} objectives armed, " +
                      $"marked their ground and left the ending to the clock.{Notes}");

            EditorApplication.Exit(0);
        }
    }
}
#endif
