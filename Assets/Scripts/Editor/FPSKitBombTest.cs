#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Proves that the bomb can be aimed with the mouse, that it goes off promptly, and
    /// that all three of its sounds are wired and reach the player.
    ///
    /// Each of those is a regression for something that shipped:
    ///
    /// - **The aim.** The ring's distance came from wherever the camera ray met the
    ///   floor. The eye is a metre and a half up, so that distance goes as
    ///   <c>height / tan(pitch)</c> -- a curve that is nearly vertical at the horizon.
    ///   The whole six-to-thirty-four metre range lived inside about twelve degrees of
    ///   pitch, there was no pitch at all that gave fifteen metres, and every degree
    ///   above the horizon gave the same maximum. Aiming felt like the mouse had been
    ///   taken away. So the test sweeps the pitch a degree at a time and requires the
    ///   range to move smoothly, to cover both ends, and to stop somewhere in the
    ///   middle -- which is the part a threshold on smoothness alone would miss, since
    ///   a ring frozen at maximum range is perfectly smooth.
    ///
    /// - **The clock.** Every throw took the same second in the air whether it was six
    ///   metres or thirty-four. So a short throw must now land in appreciably less time
    ///   than a long one.
    ///
    /// - **The sound.** Holding the key was silent, and the blast's audible reach was
    ///   derived from its damage radius rather than from how far away the person who
    ///   threw it is standing, so an ordinary long throw arrived at about a third of
    ///   its volume.
    ///
    /// It also checks the promise the whole feature rests on: the bomb goes off where
    /// the ring said it would.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyBomb
    /// </summary>
    public static class FPSKitBombTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 240.0;

        /// <summary>
        /// The level's own clock is around thirty seconds and a scored level freezes
        /// time and disables input, which would strand this mid-throw. Put back in
        /// Detach: the LevelSet is a shared asset.
        /// </summary>
        const float TestClock = 240f;

        /// <summary>
        /// Metres the ring may jump for one degree of pitch. The honest mapping moves
        /// about half a metre a degree; the ray-based one moved more than ten near the
        /// horizon.
        /// </summary>
        const float MaxMetresPerDegree = 2.0f;

        /// <summary>How far off the ring the bomb may go off, in metres.</summary>
        const float LandingTolerance = 0.75f;

        enum Phase { Enter, Tune, Await, Arm, Cursor, CursorEdge, HeldTurn, HeldDown, HeldUp,
                     ThrowNear, WatchNear, ThrowFar, WatchFar, Judge }

        static Phase _phase;
        static double _startedAt, _deadline;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;

        /// <summary>The bomb this test arms, and the prefs it borrows to do it.</summary>
        const string BombId = "frag";
        const string BombOwnedKey = "FPSKit.Own.Bomb." + BombId;

        static bool _bombPowerBackup;
        static int _bombOwnedBackup;

        // ---- what the aim did ----
        static float _sweptMin = float.MaxValue, _sweptMax = float.MinValue;
        static float _worstJump;
        static float _worstJumpAt;
        static bool _sweptMiddle;
        static float _lookMultiplier = -1f;
        static bool _armSoundPlayed;
        static bool _throwSoundPlayed;

        // ---- what the throws did ----
        static BombProjectile _live;
        static Vector3 _aimedAt;
        static float _threwAt;
        static float _nearFlight = -1f, _farFlight = -1f;
        static float _nearMiss = -1f, _farMiss = -1f;
        static float _blastMinDistance = -1f;
        static bool _sawBlastSound;

        static MethodInfo _beginAiming, _stopAiming, _updateAim, _resolveLanding;

        // ---- what the look did while the key was genuinely held ----
        static int _heldFrames;
        static float _heldYawTurned, _heldLastYaw;
        static float _heldMultiplierMin = float.MaxValue;
        static float _heldRingMin = float.MaxValue, _heldRingMax = float.MinValue;
        static bool _heldAimingThroughout = true;

        // ---- what the cursor did ----
        static bool _cursorBackup = true;
        static bool _usingCursor;
        static float _cursorLeftAngle, _cursorRightAngle;
        static float _cursorNearRange, _cursorFarRange;
        static float _viewDriftWhileCursorMoved = -1f;
        static float _edgePushTurn = -1f;
        static float _yawBeforeEdge;
        static string _tapState = "not checked";
        static float _reticleMiss = -1f;
        static string _reticleState = "not checked";

        public static void VerifyBomb()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();
                _sweptMin = float.MaxValue;
                _sweptMax = float.MinValue;
                _worstJump = _worstJumpAt = 0f;
                _sweptMiddle = _armSoundPlayed = _throwSoundPlayed = _sawBlastSound = false;
                _lookMultiplier = -1f;
                _nearFlight = _farFlight = _nearMiss = _farMiss = _blastMinDistance = -1f;
                _heldFrames = 0;
                _heldYawTurned = 0f;
                _heldMultiplierMin = float.MaxValue;
                _heldRingMin = float.MaxValue;
                _heldRingMax = float.MinValue;
                _heldAimingThroughout = true;
                _usingCursor = false;
                _cursorLeftAngle = _cursorRightAngle = 0f;
                _cursorNearRange = _cursorFarRange = -1f;
                _viewDriftWhileCursorMoved = _edgePushTurn = -1f;
                _reticleMiss = -1f;
                _reticleState = "not checked";
                _tapState = "not checked";
                _live = null;
                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                var t = typeof(BombThrower);
                const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
                _beginAiming = t.GetMethod("BeginAiming", Hidden);
                _stopAiming = t.GetMethod("StopAiming", Hidden);
                _updateAim = t.GetMethod("UpdateAim", Hidden);
                _resolveLanding = t.GetMethod("ResolveLanding", Hidden);

                if (_beginAiming == null || _stopAiming == null || _updateAim == null ||
                    _resolveLanding == null)
                    throw new Exception("BombThrower no longer has BeginAiming/UpdateAim/ResolveLanding; " +
                                        "this test drives them directly because batch mode cannot press a key");

                // The player no longer starts with a bomb -- it is the campaign's first
                // reward -- and an arena wires the store catalogue into PlayerLoadout, so
                // without this the thrower is equipped with nothing and every assertion
                // below fails on "the player has no bomb to throw". Granting the power
                // directly is what the campaign would have done by the time anyone has a
                // bomb to test; it is put back in Detach like every other borrowed piece
                // of state, because leaving it on would be a test that unlocked a power
                // in the developer's own profile.
                _bombPowerBackup = Campaign.HasPower(Campaign.BombPower);
                _bombOwnedBackup = PlayerPrefs.GetInt(BombOwnedKey, 0);

                Campaign.GrantPower(Campaign.BombPower);
                Loadout.GrantBomb(BombId);

                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] bomb test: the ring must follow the mouse, the bomb must land in it, " +
                          "and all three sounds must be wired");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ======================================================================
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"bomb test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _phase = Phase.Tune;
                        return;

                    case Phase.Tune:
                    {
                        var level = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
                        if (level == null || level.Level == null) return;

                        _clockBackup = level.Level.timeLimit;
                        _tunedLevel = level.Level;
                        level.Level.timeLimit = TestClock;
                        level.ResolveLevel();

                        _phase = Phase.Await;
                        return;
                    }

                    case Phase.Await:
                    {
                        var level = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
                        if (level == null || !level.IsRunning) return;

                        _phase = Phase.Arm;
                        return;
                    }

                    case Phase.Arm:
                        Arm();
                        _phase = Phase.Cursor;
                        return;

                    case Phase.Cursor:
                        TapToLatch();
                        CursorAim();
                        _phase = Phase.CursorEdge;
                        return;

                    // A frame later, deliberately. TurnBy writes PlayerMotor's own yaw
                    // field; the transform is not touched until HandleLook runs, so a
                    // reading taken in the same tick that asked for the turn sees the
                    // view exactly where it was and reports a dead edge push.
                    case Phase.CursorEdge:
                        CursorEdge();
                        BeginHold();
                        _phase = Phase.HeldTurn;
                        return;

                    // ---- the key genuinely held, the real Update loop running -------
                    //
                    // Everything above drives BombThrower's private methods directly,
                    // which proves the geometry and nothing about the game. These three
                    // phases set MobileInput.BombAim, which BombThrower.Update reads as
                    // an OR beside the G binding, so BeginAiming, UpdateAim and the lock
                    // toggle all run once a frame exactly as a held key makes them --
                    // with PlayerMotor turning the view in between. That is the only way
                    // to catch something *outside* this component eating the look while
                    // the ring is up, which is what "I hold the bomb key and the mouse
                    // stops" actually describes.
                    //
                    // The look is fed through MobileInput rather than the mouse because
                    // batch mode will not grant a cursor lock, and PlayerMotor reads no
                    // mouse at all without one. Both paths scale by the same
                    // LookSensitivityMultiplier, which is the thing that can be wrong.
                    case Phase.HeldTurn:
                        SampleHold();
                        MobileInput.AddLook(new Vector2(40f, 0f));

                        if (++_heldFrames < 25) return;

                        Notes.Append($"\n  holding the aim, the view turned {_heldYawTurned:0}deg " +
                                     $"in 25 frames at x{_heldMultiplierMin:0.00} sensitivity");

                        _heldFrames = 0;
                        _phase = Phase.HeldDown;
                        return;

                    case Phase.HeldDown:
                        SampleHold();
                        MobileInput.AddLook(new Vector2(0f, -30f));

                        if (++_heldFrames < 40) return;

                        _heldFrames = 0;
                        _phase = Phase.HeldUp;
                        return;

                    case Phase.HeldUp:
                        SampleHold();
                        MobileInput.AddLook(new Vector2(0f, 30f));

                        if (++_heldFrames < 40) return;

                        Notes.Append($"\n  looking up and down with the key held moved the ring " +
                                     $"{_heldRingMin:0.0}m..{_heldRingMax:0.0}m");

                        EndHold();
                        _phase = Phase.ThrowNear;
                        return;

                    case Phase.ThrowNear:
                        // Steeply down: the ring comes all the way in, and a throw that
                        // short is a low arc a metre from the player's boots, so nothing
                        // can be between them and it.
                        Launch(-70f);
                        _phase = Phase.WatchNear;
                        return;

                    case Phase.WatchNear:
                        if (!Settled(out float nearFlight, out float nearMiss)) return;
                        _nearFlight = nearFlight;
                        _nearMiss = nearMiss;

                        Notes.Append($"\n  short throw: {Vector3.Distance(Player(), _aimedAt):0.0}m out, " +
                                     $"in the air {_nearFlight:0.00}s, went off {_nearMiss:0.00}m from the ring");

                        _phase = Phase.ThrowFar;
                        return;

                    case Phase.ThrowFar:
                        Launch(0f);
                        _phase = Phase.WatchFar;
                        return;

                    case Phase.WatchFar:
                        if (!Settled(out float farFlight, out float farMiss)) return;
                        _farFlight = farFlight;
                        _farMiss = farMiss;

                        Notes.Append($"\n  long throw:  {Vector3.Distance(Player(), _aimedAt):0.0}m out, " +
                                     $"in the air {_farFlight:0.00}s, went off {_farMiss:0.00}m from the ring");

                        _phase = Phase.Judge;
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Errors.Add(e.Message);
                Finish();
            }
        }

        // ======================================================================
        /// <summary>
        /// Brings the ring up and sweeps the look through it a degree at a time.
        ///
        /// The sweep runs inside one editor update, before the player's own Update gets
        /// to write the camera back, which is why it can drive the transform directly.
        /// </summary>
        static void Arm()
        {
            var bombs = Thrower();
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            var cam = Camera.main;

            if (bombs.data == null) throw new Exception("the player went in with no bomb to throw");

            if (bombs.data.armClip == null)
                Errors.Add("BombData.armClip is empty, so holding the bomb key is silent -- " +
                           "rebuild a scene so FPSKitSceneBuilder stamps SFX/bomb_pin.wav onto the store");

            if (bombs.audioSource == null)
                Errors.Add("the thrower has no AudioSource, so neither the pin nor the throw can be heard");
            else
                bombs.audioSource.Stop();

            // Plenty, because the test spends them.
            bombs.charges = 8;

            // The pitch-to-range mapping is the touch path now that the mouse drives a
            // cursor instead, so the sweep below has to ask for it explicitly. Left on,
            // this measures the cursor sitting still in the middle of the screen while
            // the camera pitches under it -- which is the floor-ray curve the mapping
            // was written to replace, so the test would fail on the very geometry it is
            // meant to be proving.
            _cursorBackup = bombs.cursorAiming;
            bombs.cursorAiming = false;

            _beginAiming.Invoke(bombs, null);

            if (!bombs.IsAiming) throw new Exception("BeginAiming refused with charges in hand");

            _armSoundPlayed = bombs.audioSource != null && bombs.audioSource.isPlaying;
            _lookMultiplier = motor != null ? motor.LookSensitivityMultiplier : -1f;

            Transform holder = cam.transform.parent;

            // A bearing with the far wall actually far away: ShortenForWall correctly
            // pulls the ring in when the player is facing one, and a sweep taken into a
            // wall would fail for the right reason at the wrong time.
            float bestYaw = 0f, bestReach = -1f;

            for (int yaw = 0; yaw < 360; yaw += 15)
            {
                holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
                LookAt(0f);
                _updateAim.Invoke(bombs, null);

                if (bombs.AimRange <= bestReach) continue;

                bestReach = bombs.AimRange;
                bestYaw = yaw;
            }

            holder.localRotation = Quaternion.Euler(0f, bestYaw, 0f);

            Notes.Append($"\n  swept the pitch facing {bestYaw:0}deg, where the arena is " +
                         $"{bestReach:0.0}m deep");

            float previous = -1f;
            float middle = (bombs.data.minRange + bombs.data.maxRange) * 0.5f;

            for (int pitch = -80; pitch <= 20; pitch++)
            {
                LookAt(pitch);
                _updateAim.Invoke(bombs, null);

                float range = bombs.AimRange;

                _sweptMin = Mathf.Min(_sweptMin, range);
                _sweptMax = Mathf.Max(_sweptMax, range);
                if (Mathf.Abs(range - middle) <= 2f) _sweptMiddle = true;

                if (previous >= 0f && Mathf.Abs(range - previous) > _worstJump)
                {
                    _worstJump = Mathf.Abs(range - previous);
                    _worstJumpAt = pitch;
                }

                previous = range;
            }

            Notes.Append($"\n  one degree of pitch moves the ring at most {_worstJump:0.00}m " +
                         $"(worst at {_worstJumpAt:0}deg); the sweep covered " +
                         $"{_sweptMin:0.0}m..{_sweptMax:0.0}m of {bombs.data.minRange:0.0}" +
                         $"..{bombs.data.maxRange:0.0}");

            bombs.cursorAiming = _cursorBackup;

            // Put the ring down before handing the frame back. The thrower reads the
            // key itself, and a live aim with the key not held is a release -- which is
            // a throw, on the next frame, that this test did not ask for and would then
            // measure by mistake.
            _stopAiming.Invoke(bombs, null);
        }

        /// <summary>
        /// Points the view at a pitch, in degrees below the horizon.
        ///
        /// Both halves, because the rig splits them: PlayerMotor writes the pitch to
        /// the camera holder and the test writes it to the camera under it, so setting
        /// only the camera adds the test's angle to whatever the player's own look left
        /// on the holder. That is not hypothetical -- it is what made a throw aimed
        /// seventy degrees down come out at thirty metres, right after the phases that
        /// sweep the look for real.
        /// </summary>
        static void LookAt(float pitch)
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            if (motor != null && motor.cameraHolder != null)
                motor.cameraHolder.localRotation = Quaternion.identity;

            Camera.main.transform.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        }

        /// <summary>
        /// A tap must leave the ring up with no key held, and a second tap must throw.
        ///
        /// This is the control a laptop player is left with, because a touchpad stops
        /// reporting motion while a key is down -- libinput's disable-while-typing --
        /// so hold-to-aim-and-move-the-pointer cannot be performed there at all. It is
        /// driven through PumpAim rather than through Update because batch mode cannot
        /// synthesise a legacy key press, and a rule about *when* a key was released is
        /// exactly the kind that is written once and never exercised again.
        /// </summary>
        static void TapToLatch()
        {
            var bombs = Thrower();

            var pump = typeof(BombThrower).GetMethod("PumpAim",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (pump == null) { _tapState = "BombThrower.PumpAim is gone"; return; }

            bombs.charges = 8;
            MobileInput.Active = false;
            MobileInput.BombAim = false;
            _stopAiming.Invoke(bombs, null);

            object[] down = { true, true, false, bombs.controls };
            object[] up = { false, false, false, bombs.controls };
            object[] trigger = { false, false, true, bombs.controls };

            // Press and release in the same instant: a tap.
            pump.Invoke(bombs, down);
            pump.Invoke(bombs, up);

            if (!bombs.IsAiming) { _tapState = "a tap did not raise the ring"; return; }
            if (!bombs.AimLatched) { _tapState = "a tap raised the ring but did not latch it"; return; }

            // And it must survive frames with nothing held at all.
            pump.Invoke(bombs, up);
            pump.Invoke(bombs, up);

            if (!bombs.IsAiming) { _tapState = "the latched ring fell over with no key held"; return; }

            int before = bombs.charges;
            pump.Invoke(bombs, down);

            if (bombs.IsAiming) { _tapState = "a second tap did not put the ring down"; return; }
            if (bombs.charges != before - 1) { _tapState = "a second tap did not throw"; return; }

            // And the trigger has to be a way out, or an accidental tap leaves the
            // player unable to fire until they spend a bomb they did not want to spend.
            //
            // The throw cooldown is stepped over first. It is real and correct -- two
            // charges must not be spendable on one frame -- but every press in this
            // method lands in a single editor tick, so Time.time has not moved since
            // the throw above and CanThrow would refuse. Batch mode advances game time
            // by almost nothing per frame, so waiting it out is not an option either.
            ClearCooldown(bombs);

            pump.Invoke(bombs, down);
            pump.Invoke(bombs, up);

            if (!bombs.AimLatched) { _tapState = "the second tap did not re-latch for the trigger check"; return; }

            int held = bombs.charges;
            pump.Invoke(bombs, trigger);

            if (bombs.IsAiming) { _tapState = "pulling the trigger did not clear a latched aim"; return; }
            if (bombs.charges != held - 1) { _tapState = "pulling the trigger did not throw"; return; }

            _tapState = "ok";
            Notes.Append($"\n  a tap latched the ring open with no key held, a second tap " +
                         $"threw, and the trigger threw a latched one ({before} -> {bombs.charges} charges)");
        }

        /// <summary>
        /// The control the player asked for: a reticle the mouse moves, with the bomb
        /// landing where it points and the view staying where it was put.
        ///
        /// Both halves are checked, because they fail separately and for different
        /// reasons. The placement half -- cursor left puts the bomb left, cursor low
        /// brings it in -- is the promise. The stillness half is the thing that makes
        /// it a cursor rather than a slower way of turning your head, and it is the
        /// one that quietly comes back if PlayerMotor.LookCaptured is ever dropped.
        ///
        /// The cursor is placed directly rather than driven through the mouse, because
        /// batch mode grants no cursor lock and PlayerMotor reads no mouse without one.
        /// MoveCursor is then exercised on its own for the edge push, with the look
        /// delta written straight onto the motor.
        /// </summary>
        static void CursorAim()
        {
            var bombs = Thrower();
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();

            // The desktop path. MobileInput.Active would switch the cursor off, which
            // is correct on a phone and would make this measure the wrong thing.
            MobileInput.Active = false;

            // Held for real across the two ticks this measurement spans. Without it the
            // thrower's own Update sees a live aim with the key up on the next frame,
            // calls that a release, and throws a bomb in the middle of the test.
            MobileInput.BombAim = true;

            _beginAiming.Invoke(bombs, null);
            _usingCursor = bombs.UsingCursor;

            if (!_usingCursor)
            {
                Errors.Add("the thrower is not using the cursor on a desktop scene, so " +
                           "there is no reticle to move");
                MobileInput.BombAim = false;
                _stopAiming.Invoke(bombs, null);
                return;
            }

            LookAt(12f);
            SetLookDelta(motor, Vector2.zero);

            float yawBefore = motor.transform.eulerAngles.y;
            Vector3 player = bombs.transform.position;
            Vector3 forward = bombs.transform.forward;

            float w = Screen.width, h = Screen.height;

            _cursorLeftAngle = BearingAt(bombs, player, forward, w * 0.2f, h * 0.5f);
            _cursorRightAngle = BearingAt(bombs, player, forward, w * 0.8f, h * 0.5f);

            SetCursor(bombs, new Vector2(w * 0.5f, h * 0.3f));
            _updateAim.Invoke(bombs, null);
            _cursorNearRange = bombs.AimRange;

            SetCursor(bombs, new Vector2(w * 0.5f, h * 0.7f));
            _updateAim.Invoke(bombs, null);
            _cursorFarRange = bombs.AimRange;

            _viewDriftWhileCursorMoved =
                Mathf.Abs(Mathf.DeltaAngle(yawBefore, motor.transform.eulerAngles.y));

            Notes.Append($"\n  cursor left/right put the bomb {_cursorLeftAngle:0}deg / " +
                         $"{_cursorRightAngle:0}deg off the player's forward, " +
                         $"low/high at {_cursorNearRange:0.0}m / {_cursorFarRange:0.0}m, " +
                         $"with the view drifting {_viewDriftWhileCursorMoved:0.00}deg");

            // The edge push: already at the side, shoved a little further, so the
            // cursor stops at the margin and the leftover turns the head instead.
            //
            // Deliberately a small shove. A big one turns the view several hundred
            // degrees, and DeltaAngle wraps that back to almost nothing -- which read
            // as a dead edge push when the push was in fact working far too well.
            SetCursor(bombs, new Vector2(w, h * 0.5f));
            SetLookDelta(motor, new Vector2(20f, 0f));

            _yawBeforeEdge = motor.transform.eulerAngles.y;
            _updateAim.Invoke(bombs, null);
        }

        /// <summary>Reads the edge push back a frame later, once HandleLook has applied it.</summary>
        static void CursorEdge()
        {
            var bombs = Thrower();
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();

            _edgePushTurn = Mathf.Abs(Mathf.DeltaAngle(_yawBeforeEdge, motor.transform.eulerAngles.y));

            Notes.Append($"\n  shoving the cursor off the side of the screen turned the view " +
                         $"{_edgePushTurn:0.0}deg (screen {Screen.width}x{Screen.height})");

            CheckReticle(bombs);

            SetLookDelta(motor, Vector2.zero);

            MobileInput.BombAim = false;
            _stopAiming.Invoke(bombs, null);

            if (motor.LookCaptured)
                Errors.Add("PlayerMotor.LookCaptured was left on after the aim ended, which " +
                           "is a player who can never turn again");
        }

        /// <summary>
        /// Is the reticle the player actually sees where the aim says it is?
        ///
        /// Checked a frame after the cursor was placed, because the HUD moves it from
        /// its own Update. Everything else in this test would pass with the reticle
        /// drawn off the side of the screen -- the geometry is computed from
        /// AimScreenPoint, not from where the graphic ended up -- and a cursor you
        /// cannot see, on a view that deliberately no longer turns, is the whole
        /// feature failing in the way that looks most like a broken mouse.
        /// </summary>
        static void CheckReticle(BombThrower bombs)
        {
            var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();

            if (hud == null) { _reticleState = "no HUDController in the scene"; return; }
            if (hud.bombCursor == null) { _reticleState = "the HUD never built a reticle"; return; }
            if (!hud.bombCursor.gameObject.activeInHierarchy) { _reticleState = "the reticle is switched off"; return; }

            var canvas = hud.bombCursor.GetComponentInParent<Canvas>();
            var eye = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector2 drawn = RectTransformUtility.WorldToScreenPoint(eye, hud.bombCursor.position);

            _reticleMiss = Vector2.Distance(drawn, bombs.AimScreenPoint);
            _reticleState = "drawn";

            Notes.Append($"\n  the reticle is drawn at {drawn} against an aim at " +
                         $"{bombs.AimScreenPoint} -- {_reticleMiss:0.0}px out");
        }

        /// <summary>Where a cursor at this screen point puts the bomb, as a bearing.</summary>
        static float BearingAt(BombThrower bombs, Vector3 player, Vector3 forward, float x, float y)
        {
            SetCursor(bombs, new Vector2(x, y));
            _updateAim.Invoke(bombs, null);

            object[] args = { Vector3.up, true };
            var landing = (Vector3)_resolveLanding.Invoke(bombs, args);

            Vector3 offset = landing - player;
            offset.y = 0f;

            return Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), offset, Vector3.up);
        }

        /// <summary>Zeroes the throw cooldown, so two throws can be tested in one tick.</summary>
        static void ClearCooldown(BombThrower bombs)
        {
            var field = typeof(BombThrower).GetField("_nextThrowTime",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                throw new Exception("BombThrower._nextThrowTime is gone, so this test cannot " +
                                    "step over the throw cooldown");

            field.SetValue(bombs, 0f);
        }

        static void SetCursor(BombThrower bombs, Vector2 point) => Backing(bombs, "AimScreenPoint", point);
        static void SetLookDelta(PlayerMotor motor, Vector2 degrees) => Backing(motor, "LookDeltaDegrees", degrees);

        /// <summary>
        /// Writes an auto-property's backing field. Both of these are deliberately
        /// read-only to the game -- one is owned by the thrower and one by the motor --
        /// and a test that added public setters to reach them would be a test that
        /// widened the API it is checking.
        /// </summary>
        static void Backing(object target, string property, Vector2 value)
        {
            var field = target.GetType().GetField($"<{property}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                throw new Exception($"{target.GetType().Name}.{property} is no longer an " +
                                    "auto-property, so this test cannot drive it");

            field.SetValue(target, value);
        }

        /// <summary>Starts a genuinely held aim, with the touch look path live.</summary>
        static void BeginHold()
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();

            MobileInput.Active = true;
            MobileInput.BombAim = true;

            _heldFrames = 0;
            _heldLastYaw = motor.transform.eulerAngles.y;
        }

        static void SampleHold()
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            var bombs = Thrower();

            float yaw = motor.transform.eulerAngles.y;
            _heldYawTurned += Mathf.Abs(Mathf.DeltaAngle(_heldLastYaw, yaw));
            _heldLastYaw = yaw;

            _heldMultiplierMin = Mathf.Min(_heldMultiplierMin, motor.LookSensitivityMultiplier);

            if (!bombs.IsAiming) _heldAimingThroughout = false;
            else
            {
                _heldRingMin = Mathf.Min(_heldRingMin, bombs.AimRange);
                _heldRingMax = Mathf.Max(_heldRingMax, bombs.AimRange);
            }
        }

        /// <summary>
        /// Lets go. That is a throw, so the charges are topped up before the measured
        /// ones and the aim is put down afterwards.
        /// </summary>
        static void EndHold()
        {
            MobileInput.BombAim = false;
            MobileInput.Active = false;

            var bombs = Thrower();
            bombs.charges = 8;
            _stopAiming.Invoke(bombs, null);
        }

        /// <summary>Points the look at a pitch, reads the ring, and throws at it.</summary>
        static void Launch(float pitch)
        {
            var bombs = Thrower();
            var cam = Camera.main;

            LookAt(pitch);

            if (!bombs.IsAiming) _beginAiming.Invoke(bombs, null);

            // Dead centre, so the throw is aimed by the pitch above and nothing else.
            // Left where the cursor phase parked it, each throw would go wherever the
            // last assertion happened to leave the reticle.
            SetCursor(bombs, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            SetLookDelta(UnityEngine.Object.FindAnyObjectByType<PlayerMotor>(), Vector2.zero);

            _updateAim.Invoke(bombs, null);

            object[] args = { Vector3.up, true };
            _aimedAt = (Vector3)_resolveLanding.Invoke(bombs, args);

            if (!(bool)args[1])
                throw new Exception($"the ring says a throw at pitch {pitch:0} cannot be made, " +
                                    "so there is nothing to measure");

            if (bombs.audioSource != null) bombs.audioSource.Stop();

            // Clear the air first. Earlier phases throw real bombs -- the second tap in
            // TapToLatch is a throw, by definition -- and FindAnyObjectByType returns an
            // arbitrary one of several, so a leftover still in flight gets measured in
            // place of the throw under test. That reads as the bomb landing forty metres
            // from its own ring, which is a spectacular-looking failure of something
            // that is working perfectly.
            foreach (var stray in UnityEngine.Object.FindObjectsByType<BombProjectile>(
                         FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(stray.gameObject);

            _lastSeen = Vector3.zero;

            Vector3 origin = bombs.throwPoint != null ? bombs.throwPoint.position
                                                      : cam.transform.position;

            _threwAt = Time.time;
            bombs.Throw(origin, _aimedAt);

            _throwSoundPlayed |= bombs.audioSource != null && bombs.audioSource.isPlaying;
            _live = UnityEngine.Object.FindAnyObjectByType<BombProjectile>();

            // Same reason as the sweep: leave no aim behind for the thrower's own
            // Update to release into a second, unmeasured throw.
            _stopAiming.Invoke(bombs, null);

            if (_live == null) throw new Exception("throwing produced no bomb in the air");

            _deadline = EditorApplication.timeSinceStartup + 15.0;
        }

        /// <summary>
        /// True once the bomb has gone off, with how long it took and how far from the
        /// ring it landed. The last position the projectile held is where it went off:
        /// Detonate moves it there before destroying it.
        /// </summary>
        static bool Settled(out float flight, out float miss)
        {
            flight = miss = -1f;

            if (_live != null)
            {
                _lastSeen = _live.transform.position;

                if (EditorApplication.timeSinceStartup > _deadline)
                    throw new Exception("a thrown bomb was still in the air fifteen seconds later");

                return false;
            }

            flight = Time.time - _threwAt;
            miss = Vector3.Distance(new Vector3(_lastSeen.x, 0f, _lastSeen.z),
                                    new Vector3(_aimedAt.x, 0f, _aimedAt.z));

            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source.clip == null || !source.gameObject.name.StartsWith("OneShot")) continue;

                _sawBlastSound = true;
                _blastMinDistance = Mathf.Max(_blastMinDistance, source.minDistance);
            }

            return true;
        }

        static Vector3 _lastSeen;

        static BombThrower Thrower()
        {
            var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
            if (bombs == null) throw new Exception("the scene has no BombThrower");
            return bombs;
        }

        static Vector3 Player()
        {
            var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
            return bombs != null ? bombs.transform.position : Vector3.zero;
        }

        // ======================================================================
        static void Detach()
        {
            // Statics, and domain reload is off: a test that failed mid-hold would
            // otherwise leave the next play session convinced a bomb key is down and
            // on-screen controls exist.
            MobileInput.BombAim = false;
            MobileInput.Active = false;

            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null;
            _clockBackup = -1f;

            if (!_bombPowerBackup) PlayerPrefs.DeleteKey("FPSKit.Campaign.Power." + Campaign.BombPower);
            if (_bombOwnedBackup == 0) PlayerPrefs.DeleteKey(BombOwnedKey);
            else PlayerPrefs.SetInt(BombOwnedKey, _bombOwnedBackup);
            PlayerPrefs.Save();

            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;
        }

        static void Finish()
        {
            var bombs = UnityEngine.Object.FindAnyObjectByType<BombThrower>();
            var data = bombs != null ? bombs.data : null;

            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            // ---- the aim ----
            if (_worstJump > MaxMetresPerDegree)
                problems.Append($"\n  - one degree of pitch moves the ring {_worstJump:0.0}m " +
                                $"(at {_worstJumpAt:0}deg), over the {MaxMetresPerDegree:0.0}m limit: " +
                                "the mouse cannot place a bomb it throws that far per degree");

            if (data != null && _sweptMin > data.minRange + 1f)
                problems.Append($"\n  - looking down never brings the ring closer than {_sweptMin:0.0}m, " +
                                $"though the bomb can be thrown {data.minRange:0.0}m");

            if (data != null && _sweptMax < data.maxRange - 1f)
                problems.Append($"\n  - looking up never pushes the ring past {_sweptMax:0.0}m, " +
                                $"though the bomb reaches {data.maxRange:0.0}m");

            if (!_sweptMiddle)
                problems.Append("\n  - no pitch puts the ring at mid range: the whole middle of the " +
                                "throw is unreachable with the mouse");

            if (_tapState != "ok")
                problems.Append($"\n  - tapping the bomb key does not work: {_tapState}. A laptop " +
                                "touchpad reports no motion while a key is held, so a player " +
                                "without a mouse cannot aim at all without this");

            // ---- the cursor ----
            if (_usingCursor)
            {
                if (_cursorLeftAngle > -10f || _cursorRightAngle < 10f)
                    problems.Append($"\n  - the cursor does not steer the throw: left of screen " +
                                    $"put the bomb {_cursorLeftAngle:0}deg off forward and right of " +
                                    $"screen {_cursorRightAngle:0}deg, when they should straddle it");

                if (_cursorNearRange >= _cursorFarRange - 3f)
                    problems.Append($"\n  - the cursor's height does not change the distance: low on " +
                                    $"screen gave {_cursorNearRange:0.0}m and high gave " +
                                    $"{_cursorFarRange:0.0}m");

                if (_viewDriftWhileCursorMoved > 0.5f)
                    problems.Append($"\n  - the view turned {_viewDriftWhileCursorMoved:0.00}deg while " +
                                    "the cursor was moved inside the screen, so this is a slower way " +
                                    "of turning your head rather than a cursor");

                if (_reticleState != "drawn")
                    problems.Append($"\n  - the player can see no reticle: {_reticleState}");
                else if (_reticleMiss > 4f)
                    problems.Append($"\n  - the reticle is drawn {_reticleMiss:0}px from the point " +
                                    "the bomb is actually aimed at, so the player is moving one " +
                                    "thing and placing another");

                if (_edgePushTurn < 1f || _edgePushTurn > 170f)
                    problems.Append($"\n  - shoving the cursor past the edge turned the view " +
                                    $"{_edgePushTurn:0.00}deg, so the throw is stuck inside whatever " +
                                    "was on screen when the key went down");
            }

            if (!_heldAimingThroughout)
                problems.Append("\n  - the aim dropped partway through the hold, so nothing below " +
                                "was measured against a live ring");

            if (_heldYawTurned < 20f)
                problems.Append($"\n  - with the bomb key held the view turned only {_heldYawTurned:0.0}deg " +
                                "for a full sweep of look input: something is eating the look while " +
                                "the ring is up");

            if (_heldMultiplierMin <= 0.05f)
                problems.Append($"\n  - the look sensitivity fell to x{_heldMultiplierMin:0.00} while the " +
                                "bomb key was held, which is a camera the player cannot turn");

            if (data != null && _heldRingMax < data.maxRange - 1f)
                problems.Append($"\n  - looking up with the key held never pushed the ring past " +
                                $"{_heldRingMax:0.0}m of {data.maxRange:0.0}m");

            if (data != null && _heldRingMin > data.minRange + 1f)
                problems.Append($"\n  - looking down with the key held never brought the ring closer " +
                                $"than {_heldRingMin:0.0}m of {data.minRange:0.0}m");

            if (_lookMultiplier <= 0f)
                problems.Append($"\n  - the look is scaled by {_lookMultiplier:0.00} while aiming, " +
                                "which is a camera the player cannot turn");

            // ---- the clock ----
            if (_nearFlight > 0f && _farFlight > 0f && _farFlight < _nearFlight * 1.4f)
                problems.Append($"\n  - a six-metre throw takes {_nearFlight:0.00}s and a long one " +
                                $"{_farFlight:0.00}s: the flight time is not scaling with the distance, " +
                                "so every bomb hangs in the air for the same second");

            // ---- the promise ----
            if (_nearMiss > LandingTolerance)
                problems.Append($"\n  - the short throw went off {_nearMiss:0.00}m from the ring it was " +
                                $"aimed at, over the {LandingTolerance:0.00}m tolerance");

            // ---- the sound ----
            if (!_armSoundPlayed)
                problems.Append("\n  - bringing the ring up played nothing: holding the bomb key is silent");

            if (!_throwSoundPlayed)
                problems.Append("\n  - throwing played nothing");

            if (!_sawBlastSound)
                problems.Append("\n  - the blast played nothing through OneShotAudio");

            if (data != null && _blastMinDistance >= 0f && _blastMinDistance < data.maxRange - 0.01f)
                problems.Append($"\n  - the blast holds full volume for only {_blastMinDistance:0.0}m " +
                                $"though the bomb is thrown up to {data.maxRange:0.0}m, so an ordinary " +
                                "long throw is heard at a fraction of its level");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: the bomb{problems}\n\n  what happened:{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] OK: the bomb aims, lands and sounds like one.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
