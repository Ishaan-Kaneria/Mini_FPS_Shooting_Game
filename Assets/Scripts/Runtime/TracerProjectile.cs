using UnityEngine;

/// <summary>
/// Flies a tracer from the muzzle to wherever the shot landed, then removes itself.
///
/// The flight used to be a coroutine on <see cref="Weapon"/>, which worked for the
/// player and could not work for anyone else: a coroutine belongs to the behaviour
/// that started it, so an enemy's tracer would vanish mid-air the moment the enemy
/// died -- which, since the thing usually killing it is the shot being traced, is
/// most of them. Owning its own flight makes the tracer independent of whoever fired
/// it, and lets <see cref="EnemyAI"/> use the same prefab and the same look.
///
/// The round has already hit by the time this exists. The raycast resolved on the
/// frame the trigger was pulled, so this is the visual record of a shot that has
/// already happened, not a projectile that can miss.
/// </summary>
[DisallowMultipleComponent]
public class TracerProjectile : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Metres per second. Overwritten by whoever launches it, so this is only " +
             "the speed a tracer dropped into a scene by hand travels at.")]
    public float speed = 240f;

    [Tooltip("Hard ceiling on how long a tracer may live, in seconds. A safety net for " +
             "the case where the end point is unreachable -- an enormous max range, or a " +
             "speed of nearly zero -- so a stray tracer can never accumulate forever.")]
    public float maxLifetime = 3f;

    Vector3 _endPoint;
    bool _flying;
    float _age;

    /// <summary>Sends the tracer to <paramref name="endPoint"/> at the given speed.</summary>
    public void Launch(Vector3 endPoint, float metresPerSecond)
    {
        _endPoint = endPoint;
        speed = Mathf.Max(1f, metresPerSecond);
        _flying = true;
        _age = 0f;

        // Pointed along the shot immediately rather than on the first Update, so a tracer
        // that arrives within one frame -- a point-blank shot -- is still oriented.
        Vector3 travel = endPoint - transform.position;
        if (travel.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(travel);
    }

    void Update()
    {
        if (!_flying) return;

        _age += Time.deltaTime;

        transform.position = Vector3.MoveTowards(transform.position, _endPoint, speed * Time.deltaTime);

        // The squared distance is deliberately loose: MoveTowards lands exactly on the
        // target, but comparing floats for equality to decide when to despawn is the kind
        // of thing that works until someone changes the speed.
        if ((transform.position - _endPoint).sqrMagnitude <= 0.04f || _age >= maxLifetime)
            Destroy(gameObject);
    }
}
