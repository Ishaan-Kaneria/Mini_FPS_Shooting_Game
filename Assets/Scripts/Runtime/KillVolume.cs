using UnityEngine;

/// <summary>
/// The bottom of the river. Anything that falls in dies.
///
/// A trigger rather than a check somewhere in the player: the hazard is a property of
/// the place, so an enemy backed off a ledge drowns on the same terms the player does,
/// and a level that adds a second gorge adds no code. It is also why this damages
/// through <see cref="Health"/> instead of teleporting or despawning -- the level has
/// exactly one idea of what being dead means, and everything downstream of it (the
/// ragdoll, the score, the combo, the end of the level) is already wired to that one.
///
/// <see cref="LevelManager"/>'s leash would eventually discard a body that fell in and
/// kept falling, which is the safety net and not the intent: a discard scores nothing
/// and queues a replacement, so a player who shoulder-barged an enemy into the water
/// would watch it pay out nothing at all. Killing it here means the push was a kill.
///
/// Instant by default because that is what the drop reads as from the rim. The
/// alternative -- <see cref="damagePerSecond"/> with <see cref="instantKill"/> off -- is
/// for shallow water and for the electrified track, where the fair version is a second
/// of grace to scramble out.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KillVolume : MonoBehaviour
{
    [Header("What it does")]
    [Tooltip("Kill outright on contact. Off, it burns health down at the rate below.")]
    public bool instantKill = true;

    [Tooltip("Health per second while something is still inside. Ignored when it kills " +
             "outright.")]
    [Min(0f)] public float damagePerSecond = 60f;

    [Tooltip("Ignore anything already this far into dying, so a corpse sinking into the " +
             "water does not re-trigger everything downstream of a kill.")]
    public bool skipDead = true;

    [Header("Feedback")]
    [Tooltip("Played at the point of entry. A splash, a hiss, an arc.")]
    public AudioClip enterClip;
    [Range(0f, 1f)] public float enterVolume = 0.8f;

    [Tooltip("Spawned at the point of entry and left to clean itself up.")]
    public GameObject enterEffect;
    [Min(0f)] public float effectLifetime = 3f;

    void Reset()
    {
        var collider = GetComponent<Collider>();
        if (collider != null) collider.isTrigger = true;
    }

    void OnTriggerEnter(Collider other) => Consume(other, Time.deltaTime, entering: true);
    void OnTriggerStay(Collider other) => Consume(other, Time.deltaTime, entering: false);

    void Consume(Collider other, float dt, bool entering)
    {
        // GetComponentInParent, because what enters the water is a shin or a head: the
        // colliders on a body are its hitboxes, and the Health is on the root above them.
        var health = other.GetComponentInParent<Health>();
        if (health == null) return;
        if (skipDead && health.IsDead) return;

        if (entering) Announce(other.bounds.center);

        var info = new DamageInfo(
            instantKill ? health.maxHealth + health.maxShield : damagePerSecond * dt,
            other.bounds.center, Vector3.up, Vector3.down, gameObject);

        if (instantKill) health.Kill(info);
        else health.ApplyDamage(info);
    }

    void Announce(Vector3 point)
    {
        if (enterClip != null) OneShotAudio.Play(enterClip, point, enterVolume);

        if (enterEffect == null) return;

        var effect = Instantiate(enterEffect, point, Quaternion.identity);
        if (effectLifetime > 0f) Destroy(effect, effectLifetime);
    }
}
