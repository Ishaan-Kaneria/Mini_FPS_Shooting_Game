using UnityEngine;

/// <summary>
/// The grab and punch, after the fight in the film <i>Master</i> (Ishaan's brief, 2026-10-22; Docs/DESIGN_NOTES.md
/// "The grab and punch"). What the hands do is picked from the part of the enemy that is nearest them:
///
/// - **Face to face** (his head within <see cref="MeleeSettings.grabRange"/> of the eye, the crosshair on it, and him
///   facing the player): the press takes him by the head. The rifle swings onto the back first, as for the fists.
/// - **Held**: the left hand has him by the side of the head, behind the ear, so his face is clear in the middle of the
///   view. Each press is a punch to the face -- the straight right, the hook, the uppercut, in turn
///   (<see cref="MeleeSettings.facePunchesBeforeFinisher"/> of them, which never finish him:
///   <see cref="MeleeSettings.heldFloor"/>) -- then the overhand brought down from above the head, which ends an
///   ordinary enemy and leaves an elite on the floor. He fights it (Ishaan, 2026-10-22: "the enemy also don't try to run
///   or punch when I hold it"; <see cref="EnemyAI.UpdateStruggle"/>): he pulls back against the hand, winds his free fist
///   up and punches the player unless a punch to his face gets there first, and left alone for
///   <see cref="MeleeSettings.releaseAfter"/>, or after landing <see cref="MeleeSettings.countersToBreak"/> punches,
///   wrenches himself free and runs. He cannot shoot; nobody else stops shooting. The player reaching for the gun, or
///   the body slipping from the grip (a wall), shoves him off, dazed.
/// - **A hook at the belly** (fists up, the blow aimed low): he folds over it for
///   <see cref="MeleeSettings.doubledSeconds"/>, and the next press takes him by the back of the head, bent, as the
///   film's opponent is taken.
///
/// Ordinary enemies can always be grabbed; an elite only when badly hurt, reeling or doubled up; a boss never (it only
/// takes chip damage). Every blow is dealt on the frame the fist arrives, with the existing feel: only a landed blow
/// shakes the view, no slow motion, and the enemy answers on that frame (EnemyBody). Nothing is parented into the enemy:
/// it is pinned in front of the player by its AI (<see cref="EnemyAI.Grapple"/>) and its body leans into the hand that
/// holds it (<see cref="EnemyBody"/>), and the player's left hand is laid on its head each frame (<see cref="ViewModelRig"/>).
/// </summary>
public partial class MeleeStrike
{
    EnemyAI _held;
    Health _heldHealth;
    int _heldBlows;
    float _holdBreakAt;
    bool _heldBent;
    float _counterSeen, _tornUntil;

    /// <summary>Someone is held by the head.</summary>
    public bool Holding => _held != null;

    /// <summary>The enemy held by the head, or null.</summary>
    public EnemyAI Held => _held;

    /// <summary>He was doubled over when he was taken: held bent, by the back of the head.</summary>
    public bool HoldingBent => _held != null && _heldBent;

    /// <summary>How far the left hand is onto the head: out to it through the grab, 1 while held, back off it on release.</summary>
    public float GrabHold { get; private set; }

    /// <summary>Where the left hand holds the head, in the world.</summary>
    public Vector3 GrabPoint { get; private set; }

    /// <summary>Where the blow in flight lands, in the world: the face, or the belly for the hook.</summary>
    public Vector3 StrikePoint { get; private set; }

    /// <summary>The held head's own up (crown) and face directions, for laying the hand on it (ViewModelRig).</summary>
    public Vector3 GripUp { get; private set; } = Vector3.up;
    public Vector3 GripFace { get; private set; } = Vector3.forward;

    /// <summary>Which punch to the face is in flight: 0 the straight right, 1 the hook, 2 the uppercut.</summary>
    public int PunchKind { get; private set; }

    /// <summary>His punch has just knocked the holding hand: 1 on the frame it lands, back to 0 over a third of a second.</summary>
    public float GripKnock { get; private set; }

    /// <summary>The blows of the grab and punch, which have their own targets and damage.</summary>
    public static bool IsGrappleStrike(Strike s) => s == Strike.Grab || s == Strike.FacePunch || s == Strike.Overhand || s == Strike.BodyBlow;

    /// <summary>
    /// Before the old rules: whether this press belongs to the grab and punch, and if so the strike it throws.
    /// </summary>
    bool TryGrapplePress(out Health hit)
    {
        hit = null;
        var s = Settings;

        if (_held != null)
        {
            var next = _heldBlows < s.facePunchesBeforeFinisher ? Strike.FacePunch : Strike.Overhand;
            if (next == Strike.FacePunch) PunchKind = _heldBlows % 3;
            hit = Throw(next, 0f, _heldHealth);
            return true;
        }

        // No arms that can let go of the gun: the rifle butt, as before.
        if (!HasArms && !Disarmed) return false;

        var target = FindTarget(s.punchRange);
        if (target == null) return false;
        var ai = target.GetComponent<EnemyAI>();
        if (ai == null) return false;

        if (CanGrab(target, ai) && (ai.DoubledOver ? HeadDistance(target) <= s.grabRange + 0.25f : FaceToFace(target, ai)))
        {
            // Into the fists first (the rifle onto the back), as the punch chain does.
            float delay = s.holsterSeconds * (1f - GunAway) * 0.6f;
            _fists = true;
            _heldBent = ai.DoubledOver;
            hit = Throw(Strike.Grab, delay, target);
            return true;
        }

        // With the fists up, a blow aimed low is the hook to the belly.
        if ((_fists || Disarmed) && GunAway > 0.5f)
        {
            AimPoint(target, fpsCamera.transform, out BodyPart part);
            if (part == BodyPart.LowerTorso)
            {
                hit = Throw(Strike.BodyBlow, 0f, target);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Who can be taken by the head: an ordinary enemy on its feet; an elite only badly hurt, reeling or doubled up; never
    /// a boss, nor one on the floor or crawling, nor one already held.
    /// </summary>
    public bool CanGrab(Health target, EnemyAI ai)
    {
        if (target == null || target.IsDead || ai == null) return false;
        if (ai.Downed || ai.Crawling || ai.Grappled) return false;
        var role = RoleOf(target);
        if (role == EnemyArchetype.Role.Boss) return false;
        if (role == EnemyArchetype.Role.Elite)
        {
            float pool = target.Current + target.Shield, full = Mathf.Max(1f, target.maxHealth + target.maxShield);
            return pool / full <= Settings.takedownHealthShare || ai.IsStaggered || ai.DoubledOver;
        }
        return true;
    }

    /// <summary>His head within grab reach of the eye, the crosshair on it, and him facing the player.</summary>
    bool FaceToFace(Health target, EnemyAI ai)
    {
        var s = Settings;
        Transform cam = fpsCamera.transform;
        Vector3 head = HeadOf(target);
        if (HeadDistance(target) > s.grabRange) return false;

        AimPoint(target, cam, out BodyPart part);
        bool onHead = part == BodyPart.Head || part == BodyPart.Neck || Vector3.Angle(cam.forward, head - cam.position) < 22f;
        if (!onHead) return false;

        Vector3 toPlayer = cam.position - target.transform.position; toPlayer.y = 0f;
        Vector3 facing = target.transform.forward; facing.y = 0f;
        return toPlayer.sqrMagnitude < 0.0001f || Vector3.Angle(facing, toPlayer) <= s.grabFacingDegrees;
    }

    float HeadDistance(Health target) => Vector3.Distance(HeadOf(target), fpsCamera.transform.position);

    static Vector3 HeadOf(Health target)
    {
        var body = target.GetComponent<EnemyBody>();
        return body != null ? body.EyePosition : target.transform.position + Vector3.up * 1.6f;
    }

    /// <summary>The nearest point of one of the target's parts to <paramref name="from"/> (the belly for the hook).</summary>
    static Vector3 PartPoint(Health target, BodyPart part, Vector3 from, Vector3 fallback)
    {
        float best = float.MaxValue;
        Vector3 point = fallback;
        foreach (var hitbox in target.GetComponentsInChildren<Hitbox>())
        {
            if (hitbox.Part != part) continue;
            var col = hitbox.GetComponent<Collider>();
            if (col == null || !col.enabled) continue;
            Vector3 p = col.ClosestPoint(from);
            float d = (p - from).sqrMagnitude;
            if (d < best) { best = d; point = p; }
        }
        return point;
    }

    /// <summary>
    /// Where the hand holds his head: by the back of the neck from the player's left side, under the ear, so the hand is
    /// below his face and the forearm stays in the corner of the view. Held over the crown, and then beside the head, the
    /// first-person forearm (an arm reaching up to eye height passes close to the eye) covered the face and every punch
    /// landed out of sight (filmed, Build/PlayerShots/desert_video).
    /// </summary>
    Vector3 GripOn(Health target)
    {
        Vector3 eye = HeadOf(target);
        Frame(eye, target, out Vector3 toPlayer, out Vector3 left);
        if (Settings.grip == MeleeSettings.Grip.BackOfHead)
        {
            // On the top and back of the skull, by the head's own axes (it is bowed into the clinch and turns as he
            // struggles): the palm over the crown towards the player's left, the fingers spread down the back of it.
            var body = target.GetComponent<EnemyBody>();
            Vector3 up = body != null ? body.HeadUp : Vector3.up, face = body != null ? body.HeadFace : -toPlayer;
            float size = target.transform.lossyScale.y;
            // Round the back of his neck under the skull from outside it on the player's left: bowed into the clinch his
            // head is below the player's eye, so the forearm runs below his face, beside his jaw, and the face is clear
            // over it (over the crown, or level with the eye, the forearm crossed the face). The hand wraps behind (drawn
            // in the world's depth, it goes behind his neck).
            return eye + (-face * 0.08f - up * 0.12f + left * 0.12f) * size;
        }
        return Settings.grip switch
        {
            MeleeSettings.Grip.SideOfHead => eye - toPlayer * 0.06f + left * 0.09f + Vector3.up * 0.01f,
            _ => eye - toPlayer * 0.065f + left * 0.08f - Vector3.up * 0.135f,
        };
    }

    /// <summary>
    /// Where the punch in flight lands on the face: the nose for the straight right, his left cheek (the player's right)
    /// for the hook, under the chin for the uppercut.
    /// </summary>
    Vector3 FaceOf(Health target)
    {
        Vector3 eye = HeadOf(target);
        Frame(eye, target, out Vector3 toPlayer, out Vector3 left);
        Vector3 nose = eye + toPlayer * 0.03f - Vector3.up * 0.035f;
        if (Current != Strike.FacePunch) return nose;
        return PunchKind switch
        {
            1 => nose - left * 0.055f - toPlayer * 0.02f,
            2 => nose - Vector3.up * 0.075f - toPlayer * 0.01f,
            _ => nose,
        };
    }

    /// <summary>
    /// Where his right hand takes the holding arm: on the forearm a hand's length and more from the grip towards the player's
    /// shoulder, a little under it (the clip's holder_forearm(0.24), Tools/blender/enemy_grabbed_clips.py).
    /// </summary>
    Vector3 WristOf(Health target)
    {
        // Beside his jaw on the player's side and low, where the forearm comes past his head: along the line from the grip
        // to the player's shoulder it passed over his own face, and his hand went to his forehead.
        Vector3 eye = HeadOf(target);
        Frame(eye, target, out Vector3 toPlayer, out Vector3 left);
        float size = target.transform.lossyScale.y;
        return eye + (left * 0.12f + toPlayer * 0.10f - Vector3.up * 0.20f) * size;
    }

    /// <summary>The flat way from his head to the player, and the player's left.</summary>
    void Frame(Vector3 head, Health target, out Vector3 toPlayer, out Vector3 left)
    {
        toPlayer = fpsCamera.transform.position - head; toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : -target.transform.forward;
        left = Vector3.Cross(toPlayer, Vector3.up).normalized;
    }

    // ======================================================================

    /// <summary>The blows of the grab and punch landing. Returns whether one landed.</summary>
    bool HitGrapple(Health target, Strike strike)
    {
        var s = Settings;
        var ai = target.GetComponent<EnemyAI>();
        var body = target.GetComponent<EnemyBody>();
        var role = RoleOf(target);
        Transform cam = fpsCamera.transform;

        if (strike == Strike.Grab)
        {
            if (ai == null || ai.CurrentState == EnemyAI.State.Dead || !CanGrab(target, ai)) return false;
            _held = ai;
            _heldHealth = target;
            _heldBlows = 0;
            _counterSeen = Time.time;
            _holdBreakAt = Time.time + s.releaseAfter;
            GrabPoint = GripOn(target);
            ai.Grapple(transform, s.holdDistance);
            ai.GrabPoint = GrabPoint;
            return true;   // a grip: nothing is dealt
        }

        float pool = target.Current + target.Shield;
        float full = Mathf.Max(1f, target.maxHealth + target.maxShield);
        float share = strike switch
        {
            Strike.FacePunch => s.facePunchDamage.For(role),
            Strike.Overhand => s.overhandDamage.For(role),
            _ => s.bodyBlowDamage.For(role),
        };
        float amount = Mathf.Max(1f, full * share * (_tired ? 0.7f : 1f));
        if (strike == Strike.FacePunch) amount = Mathf.Max(1f, Mathf.Min(amount, pool - full * s.heldFloor));
        // An elite is put on the floor by the overhand, not killed by it (Ishaan's call): it gets up hurt. It can only be
        // grabbed badly hurt, so any real share of its pool would have ended it every time.
        else if (strike == Strike.Overhand && role == EnemyArchetype.Role.Elite) amount = Mathf.Max(1f, Mathf.Min(amount, pool - full * 0.05f));
        else if (amount >= pool) amount = pool + 1f;

        Vector3 point, dir;
        BodyPart part;
        if (strike == Strike.BodyBlow)
        {
            point = PartPoint(target, BodyPart.LowerTorso, cam.position, target.transform.position + Vector3.up * 1.0f);
            dir = Vector3.ProjectOnPlane(point - cam.position, Vector3.up).normalized;
            part = BodyPart.LowerTorso;
        }
        else
        {
            point = FaceOf(target);
            dir = (point - cam.position).normalized;
            // The overhand comes down from above the head.
            if (strike == Strike.Overhand) dir = (dir + Vector3.down * 0.9f).normalized;
            part = BodyPart.Head;
        }
        if (dir.sqrMagnitude < 0.0001f) dir = cam.forward;

        var info = new DamageInfo(amount, point, -dir, dir, gameObject) { fromMelee = true, part = part };
        info.amount = target.ApplyDamage(info);
        if (info.amount <= 0f) return false;
        if (weapon != null) weapon.ReportHit(info);

        switch (strike)
        {
            case Strike.FacePunch:
                _heldBlows++;
                _holdBreakAt = Time.time + s.releaseAfter;
                if (ai != null) ai.TakeHeldBlow();
                if (body != null) body.Punched(dir, 1f, PunchKind);
                break;

            case Strike.Overhand:
                if (body != null) body.Punched(dir, 1.3f, 2);
                LetGo(shove: false);
                // Dead, the body drops where it stands (a blow to the head: EnemyDeath's Drop). Alive (an elite), it is
                // driven down onto the floor and gets up hurt.
                if (!target.IsDead && ai != null)
                {
                    Vector3 away = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                    ai.Knock((away * 0.5f + Vector3.down) * s.collapseSpeed, s.kickStagger, 0.4f);
                }
                break;

            case Strike.BodyBlow:
                if (!target.IsDead && ai != null && role != EnemyArchetype.Role.Boss)
                    ai.DoubleOver(s.doubledSeconds * (role == EnemyArchetype.Role.Elite ? 0.6f : 1f));
                break;
        }
        return true;
    }

    /// <summary>
    /// Every frame: the left hand onto the head, the grip followed as he moves, and the hold broken when it should be.
    /// </summary>
    void UpdateHold(ControlSettings keys)
    {
        var s = Settings;

        // The hands' aim: where the blow in flight lands, and where the grip is.
        var aimAt = _held != null ? _heldHealth : _strikeTarget;
        if (aimAt != null && !aimAt.IsDead)
        {
            StrikePoint = Current == Strike.BodyBlow
                ? PartPoint(aimAt, BodyPart.LowerTorso, fpsCamera.transform.position, aimAt.transform.position + Vector3.up)
                : FaceOf(aimAt);
            if (_held != null || Current == Strike.Grab)
            {
                GrabPoint = GripOn(aimAt);
                var body = aimAt.GetComponent<EnemyBody>();
                if (body != null) { GripUp = body.HeadUp; GripFace = body.HeadFace; }
            }
        }

        if (Current == Strike.Grab)
        {
            float k = StrikeProgress, at = ImpactAt(Strike.Grab);
            GrabHold = k < at ? UITheme.EaseOut(Mathf.Clamp01(k / at)) : 1f;
        }
        else if (_held != null) GrabHold = 1f;
        else GrabHold = Mathf.MoveTowards(GrabHold, 0f, Time.deltaTime / (Time.time < _tornUntil ? 0.08f : 0.22f));
        GripKnock = Mathf.MoveTowards(GripKnock, 0f, Time.deltaTime / 0.33f);

        if (_held == null) return;
        _held.GrabPoint = GrabPoint;
        _held.GrabWrist = WristOf(_heldHealth);
        _fistsUntil = Mathf.Max(_fistsUntil, Time.time + s.fistIdleSeconds);

        // His punch landed on the player: a landed blow, so the view shakes (the only thing that moves it), and the
        // holding hand is knocked.
        if (_held.CounterLandedAt > _counterSeen)
        {
            _counterSeen = _held.CounterLandedAt;
            GripKnock = 1f;
            if (_motor != null) _motor.AddShake(s.counterShake);
            PlayClip(hitClip, 0.7f);
        }

        // He wrenched himself free (EnemyAI.BreakFree), died, or was finished: let go, the hand torn off him.
        bool gone = _heldHealth == null || _heldHealth.IsDead || _held.CurrentState == EnemyAI.State.Dead || !_held.Grappled;
        bool wantsGun = ControlSettings.Pressed(keys.fire) || ControlSettings.Pressed(keys.aim) || ControlSettings.Pressed(keys.reload)
                        || MobileInput.Fire || MobileInput.Aim
                        || GameInput.PadPressed(GameAction.Fire) || GameInput.PadPressed(GameAction.Aim) || GameInput.PadPressed(GameAction.Reload);
        bool slipped = _held.HoldSlip > 0.9f;
        // A body that does not fight the hold (its AI off) is shoved off after a while; one that does breaks free itself.
        bool idle = Time.time > _holdBreakAt + 1.5f && Current == Strike.None;
        bool dead = _self != null && _self.IsDead;
        if (gone && _held.Bolting) { GripKnock = 1f; _tornUntil = Time.time + 0.3f; }   // torn off him: the hand thrown off
        if (gone || wantsGun || slipped || idle || dead) LetGo(shove: !gone && !dead);
    }

    /// <summary>Lets go of the head: shoved off and dazed, or just let go (he died, or was finished).</summary>
    void LetGo(bool shove)
    {
        var ai = _held;
        _held = null;
        _heldHealth = null;
        _heldBlows = 0;
        if (ai == null || !ai.Grappled) return;
        ai.ReleaseGrapple(shove ? 0.9f : 0.1f);
        if (shove && fpsCamera != null)
        {
            Vector3 away = Vector3.ProjectOnPlane(fpsCamera.transform.forward, Vector3.up).normalized;
            ai.Shove(away * 0.8f, 0.25f, 0.9f);
        }
    }
}
