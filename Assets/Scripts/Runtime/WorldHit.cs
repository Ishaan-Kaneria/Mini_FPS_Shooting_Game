using UnityEngine;

/// <summary>What hit a thing in the world.</summary>
public enum WorldHitKind { Bullet, Kick, Punch, Body, Blast }

/// <summary>
/// A blow to something in the world that is not a body: where, which way, how hard (an impulse, N s), and from what.
/// </summary>
public struct WorldHitInfo
{
    public WorldHitKind kind;
    public Vector3 point, normal, direction;
    public float impulse;
    public GameObject source;
}

/// <summary>Anything in the world that answers a blow: a door, a bin, a pane of glass, a car, a shutter.</summary>
public interface IWorldHit
{
    void OnWorldHit(in WorldHitInfo hit);
}

/// <summary>
/// One way for every blow to reach the world (his brief, 2026-11-03: kicked doors burst open, shot bins jump, glass
/// breaks). Every gun's impact (FPSImpactManager.HandleImpact) and the player's kick and punch (MeleeStrike, when no man is
/// in reach) send it here: the struck collider's own <see cref="IWorldHit"/> answers, or, when there is none, a loose
/// rigidbody is pushed. Holds no state.
/// </summary>
public static class WorldHit
{
    /// <summary>A rifle round's push on what it strikes (5.56 mm, ~0.004 kg at ~900 m/s, most of it kept: ~3 N s).</summary>
    public const float BulletImpulse = 3f;
    /// <summary>A front kick's push (about 1.5-3 kN over 30-40 ms).</summary>
    public const float KickImpulse = 70f;
    /// <summary>A punch's push.</summary>
    public const float PunchImpulse = 20f;

    /// <summary>Sends a blow to what <paramref name="collider"/> belongs to. True when something answered or moved.</summary>
    public static bool Send(Collider collider, in WorldHitInfo info)
    {
        if (collider == null) return false;
        var target = collider.GetComponentInParent<IWorldHit>();
        if (target != null)
        {
            target.OnWorldHit(info);
            return true;
        }
        var body = collider.attachedRigidbody;
        if (body != null && !body.isKinematic)
        {
            body.AddForceAtPosition(info.direction.normalized * info.impulse, info.point, ForceMode.Impulse);
            return true;
        }
        return false;
    }
}
