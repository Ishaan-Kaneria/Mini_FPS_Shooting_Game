using UnityEngine;

/// <summary>
/// How much a round does where it lands: one multiplier per body part. Every hitbox on a
/// body that points here reads it at the moment of the hit, so tuning it in the Inspector
/// changes the next shot, in play, with nothing rebuilt.
///
/// What the parts then do -- a limp, a crawl, a dropped rifle, a headshot that kills --
/// is <see cref="EnemyWounds"/>'s; this is only the size of the number. A creature with a
/// different skeleton uses the same parts (see <see cref="BodyParts"/>) and its own profile.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Body Damage Profile", fileName = "BodyDamage")]
public class BodyDamageProfile : ScriptableObject
{
    [Header("Multipliers on the weapon's damage")]
    [Tooltip("Through the head. An ordinary enemy dies to one whatever this says (EnemyWounds).")]
    [Min(0f)] public float head = 3f;
    [Min(0f)] public float neck = 2f;
    [Tooltip("The chest: the reference everything else is measured against.")]
    [Min(0f)] public float upperTorso = 1f;
    [Tooltip("The belly and hips.")]
    [Min(0f)] public float lowerTorso = 0.9f;
    [Min(0f)] public float upperArm = 0.7f;
    [Min(0f)] public float forearm = 0.6f;
    [Min(0f)] public float thigh = 0.8f;
    [Min(0f)] public float shin = 0.7f;

    public float For(BodyPart part) => part switch
    {
        BodyPart.Head => head,
        BodyPart.Neck => neck,
        BodyPart.Torso => upperTorso,
        BodyPart.LowerTorso => lowerTorso,
        BodyPart.LeftArm or BodyPart.RightArm => upperArm,
        BodyPart.LeftForearm or BodyPart.RightForearm => forearm,
        BodyPart.LeftLeg or BodyPart.RightLeg => thigh,
        BodyPart.LeftShin or BodyPart.RightShin => shin,
        _ => 1f,
    };
}
