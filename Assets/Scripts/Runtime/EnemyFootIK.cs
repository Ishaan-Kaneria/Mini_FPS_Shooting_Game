using UnityEngine;

/// <summary>
/// Puts the enemy's feet on the ground they are over: each foot is moved by the height of the
/// ground under it against the ground under the body, and turned to its slope, and the pelvis
/// comes down by the lower foot's drop so a leg is never stretched to reach a step. The clips
/// were made on flat ground; this is what makes them stand on stairs, slopes and debris.
///
/// Humanoid IK (OnAnimatorIK on the base layer's IK pass), on the body beside the Animator.
/// Off while it lies, crawls or gets up (the clips own the ground then), and beyond
/// <see cref="maxDistance"/> from the camera, where nobody can see a foot.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyFootIK : MonoBehaviour
{
    [Tooltip("Highest step, metres, a foot is raised or lowered to meet.")]
    [Range(0.05f, 1f)] public float maxStep = 0.45f;
    [Tooltip("How fast the pelvis follows the feet down, per second.")]
    [Range(1f, 20f)] public float pelvisSpeed = 8f;
    [Tooltip("Metres from the camera beyond which the feet are left as animated.")]
    [Range(5f, 100f)] public float maxDistance = 35f;

    Animator _animator;
    EnemyBody _body;
    EnemyAI _ai;
    float _pelvis, _weight;
    int _mask;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _body = GetComponentInParent<EnemyBody>();
        _ai = GetComponentInParent<EnemyAI>();
        _mask = LayerMask.GetMask("Environment", "Default");
    }

    void OnAnimatorIK(int layer)
    {
        if (layer != 0 || _animator == null) return;

        bool want = _body == null || (!_body.IsDown && !(_ai != null && _ai.Crawling));
        var cam = Camera.main;
        if (want && cam != null && (cam.transform.position - transform.position).sqrMagnitude > maxDistance * maxDistance) want = false;
        _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, Time.deltaTime * 4f);
        if (_weight <= 0.001f) { _pelvis = 0f; return; }

        Transform root = _body != null ? _body.transform : transform;
        float baseY = root.position.y;
        float dl = Foot(AvatarIKGoal.LeftFoot, baseY);
        float dr = Foot(AvatarIKGoal.RightFoot, baseY);

        // The pelvis drops by the lower foot's drop, so the lower leg is not stretched to reach.
        float down = Mathf.Min(0f, Mathf.Min(dl, dr));
        _pelvis = Mathf.MoveTowards(_pelvis, down, Time.deltaTime * pelvisSpeed * Mathf.Max(0.2f, Mathf.Abs(down - _pelvis)));
        _animator.bodyPosition += Vector3.up * _pelvis * _weight;
    }

    /// <summary>Lays one foot on the ground under it; returns how far it moved up (negative: down).</summary>
    float Foot(AvatarIKGoal goal, float baseY)
    {
        Vector3 p = _animator.GetIKPosition(goal);
        float above = p.y - baseY;
        if (!Physics.Raycast(p + Vector3.up * maxStep, Vector3.down, out RaycastHit hit, maxStep * 2f + above, _mask, QueryTriggerInteraction.Ignore))
        {
            _animator.SetIKPositionWeight(goal, 0f);
            _animator.SetIKRotationWeight(goal, 0f);
            return 0f;
        }

        float delta = Mathf.Clamp(hit.point.y - baseY, -maxStep, maxStep);
        // A foot in the air (mid-stride) is left to the clip; a planted one is laid on the ground.
        float planted = 1f - Mathf.Clamp01((above - 0.12f) / 0.25f);
        float w = planted * _weight;
        _animator.SetIKPositionWeight(goal, w);
        _animator.SetIKRotationWeight(goal, w);
        _animator.SetIKPosition(goal, p + Vector3.up * delta);
        _animator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, hit.normal) * _animator.GetIKRotation(goal));
        return delta;
    }
}
