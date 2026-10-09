using UnityEngine;

/// <summary>
/// Sits beside the enemy's Animator (on the body, a child of the root) and passes its
/// root motion up to <see cref="EnemyMotor"/>. Unity only calls OnAnimatorMove on the
/// Animator's own GameObject. With this here, the Animator never moves the body by itself:
/// the agent does, or the motor does with the clip's motion.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyRootMotion : MonoBehaviour
{
    EnemyMotor _motor;

    void Awake() => _motor = GetComponentInParent<EnemyMotor>();

    void OnAnimatorMove()
    {
        if (_motor != null) _motor.AnimatorMoved();
    }
}
