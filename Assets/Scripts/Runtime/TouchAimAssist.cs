using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helps a thumb hold a target, without ever aiming for the player.
///
/// <b>Why this is not a concession.</b> A mouse resolves something like a hundredth of a
/// degree per count. A thumb on glass is moving a contact patch roughly the size of the
/// thing it is trying to point at, and resolves perhaps a degree on a good day. Ship a
/// shooter to a phone with no assist and the player is not being tested on aim, they are
/// being tested on a motor task that nobody can perform -- and what that feels like from
/// the inside is not "hard", it is "this game does not register my input properly".
/// Every console and mobile shooter has some version of this. The ones that feel best
/// are the ones where you cannot tell.
///
/// Two halves, and the quiet one does most of the work:
///
/// - <b>Slowdown.</b> While the crosshair is within the cone of a target, look input is
///   scaled down. Nothing moves that the player did not move; they simply get more
///   thumb travel per degree exactly where precision matters. This is the half nobody
///   notices and the half that makes the game playable.
/// - <b>Adhesion.</b> The view is drawn toward the target, strongest dead centre and
///   falling off to nothing at the edge of the cone. This is deliberately weak, and it
///   is gated: it only applies while the player is <i>already</i> turning or firing.
///   Without that gate it becomes magnetism -- the camera creeping toward enemies while
///   the player stands still, which feels like the game wrestling them for the mouse and
///   is the single fastest way to make assist obvious and hated.
///
/// <b>Thumbs and sticks only.</b> A player on a mouse gets none of it. Assist compensates
/// for an input device, and handing it to a device that does not need it is both unfair
/// and worse to play. The player can turn it down or off (<see cref="GameSettings"/>).
/// </summary>
[DisallowMultipleComponent]
public class TouchAimAssist : MonoBehaviour
{
    [Tooltip("The camera the crosshair belongs to. Found from the player rig when empty.")]
    public Camera eye;

    [Tooltip("Where the numbers come from. Without one the assist stays off entirely -- " +
             "it must never be on with tuning nobody chose.")]
    public TouchProfile profile;

    [Tooltip("What blocks the line to a target. An enemy behind a wall must not be " +
             "assisted, or the view is pulled toward something the player cannot see " +
             "and cannot shoot.")]
    public LayerMask sightBlockers;

    [Tooltip("Seconds between rebuilds of the candidate list. Angles are evaluated every " +
             "frame; only the search for who exists is rationed.")]
    [Range(0.05f, 1f)] public float rescanInterval = 0.25f;

    readonly List<EnemyAI> _candidates = new List<EnemyAI>();
    float _nextScan;

    void Awake()
    {
        if (eye == null) eye = GetComponentInChildren<Camera>();
        if (eye == null) eye = Camera.main;
    }

    /// <summary>
    /// Adjusts a frame's look, in degrees, and returns what should actually be applied.
    ///
    /// Called by <see cref="PlayerMotor"/> rather than acting on the transform itself, so
    /// there is exactly one thing in the project that turns the view and the assist
    /// cannot fight it. Returning the value also means a caller that does not want
    /// assist simply does not call -- there is no state to switch off.
    ///
    /// <paramref name="strength"/> is the player's setting, 0 to 1, scaling both halves;
    /// 1 is the tuned assist. The caller decides whether the input is one that gets any --
    /// a thumb or a stick, never a mouse.
    /// </summary>
    public Vector2 Adjust(Vector2 lookDegrees, bool firing, float deltaTime, float strength = 1f)
    {
        if (profile == null || !profile.aimAssist || strength <= 0f) return lookDegrees;
        if (eye == null) return lookDegrees;

        var target = FindTarget(out float offAxis);
        if (target == null) return lookDegrees;

        // 1 dead centre, 0 at the edge of the cone.
        float closeness = 1f - Mathf.Clamp01(offAxis / Mathf.Max(0.01f, profile.assistAngle));

        Vector2 adjusted = lookDegrees * Mathf.Lerp(1f, profile.assistSlowdown, closeness * strength);

        // Adhesion only while the player is doing something. Standing still with a full
        // magazine and watching the camera drift toward a doorway is the moment assist
        // stops being invisible.
        bool turning = lookDegrees.sqrMagnitude > 0.0001f;
        if (!turning && !firing) return adjusted;

        return adjusted + Pull(target, closeness * strength, deltaTime);
    }

    /// <summary>
    /// The yaw and pitch correction toward the target for this frame, in degrees.
    ///
    /// Clamped to what is left of the angle, so it can settle onto a target and stop
    /// rather than crossing it and oscillating -- an assist that overshoots reads as the
    /// view shaking, which is worse than no assist at all.
    /// </summary>
    Vector2 Pull(EnemyAI target, float closeness, float deltaTime)
    {
        Vector3 toTarget = AimPoint(target) - eye.transform.position;
        if (toTarget.sqrMagnitude < 0.0001f) return Vector2.zero;

        Vector3 wanted = Quaternion.LookRotation(toTarget).eulerAngles;
        Vector3 current = eye.transform.eulerAngles;

        float yaw = Mathf.DeltaAngle(current.y, wanted.y);
        float pitch = Mathf.DeltaAngle(current.x, wanted.x);

        float step = profile.assistPull * closeness * deltaTime;

        // The motor's pitch convention is inverted relative to Euler x, which is why the
        // y term is negated: it is fed back through the same path a thumb drag takes.
        return new Vector2(
            Mathf.Clamp(yaw, -step, step),
            -Mathf.Clamp(pitch, -step, step));
    }

    /// <summary>
    /// The living enemy closest to the centre of the view, inside the cone, in range and
    /// actually visible.
    ///
    /// Closest to the crosshair rather than nearest in space: the player is pointing at
    /// something, and assist that picks a different target because it happens to be
    /// nearer is assist that drags the shot off the one they chose.
    /// </summary>
    EnemyAI FindTarget(out float offAxis)
    {
        offAxis = 0f;

        Rescan();

        Vector3 origin = eye.transform.position;
        Vector3 forward = eye.transform.forward;

        float range = profile.assistRange;
        float best = profile.assistAngle;
        EnemyAI chosen = null;

        for (int i = 0; i < _candidates.Count; i++)
        {
            var enemy = _candidates[i];
            if (enemy == null || !enemy.isActiveAndEnabled) continue;
            if (enemy.CurrentState == EnemyAI.State.Dead) continue;

            Vector3 point = AimPoint(enemy);
            Vector3 toTarget = point - origin;

            float distance = toTarget.magnitude;
            if (distance > range || distance < 0.01f) continue;

            float angle = Vector3.Angle(forward, toTarget);
            if (angle >= best) continue;

            // Checked last, because it is the only expensive test here and most
            // candidates have already failed the cheap ones.
            if (Physics.Linecast(origin, point, sightBlockers, QueryTriggerInteraction.Ignore))
                continue;

            best = angle;
            chosen = enemy;
        }

        if (chosen != null) offAxis = best;
        return chosen;
    }

    /// <summary>
    /// Centre mass, never the head.
    ///
    /// Assist that converges on a head is not assist, it is an aimbot -- and it would
    /// quietly hand the player the headshot bonus for pointing in the general direction
    /// of somebody, which makes the one skilled shot in the game free.
    /// </summary>
    static Vector3 AimPoint(EnemyAI enemy)
    {
        var body = enemy.GetComponent<Collider>();
        return body != null ? body.bounds.center : enemy.transform.position + Vector3.up;
    }

    void Rescan()
    {
        if (Time.time < _nextScan) return;

        _nextScan = Time.time + rescanInterval;

        _candidates.Clear();
        _candidates.AddRange(FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude,
                                                        FindObjectsSortMode.None));
    }
}
