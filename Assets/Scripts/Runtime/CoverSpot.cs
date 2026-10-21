using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A piece of cover set down in an arena -- crates stacked against a shop, a generator on its crate, a street cabinet --
/// that enemies know for cover: <see cref="EnemyBrain"/>'s cover search tries the spots beside and in front of each one
/// near it, as well as its own random tries. Low cover (under <see cref="LowBelow"/>) hides a crouched body only, so the
/// brain judges it at crouched height: it keeps its head down there when the fire is on it, and stands up to shoot.
///
/// On the group's root, facing out from the wall it stands against (right = along the wall); placed with its measured
/// size by the town builder (FPSKitSceneBuilder.SpinShopCover). The Stage 4 cover bake (Docs/ENEMY_AI_PLAN.md) can
/// take these over.
/// </summary>
[DisallowMultipleComponent]
public class CoverSpot : MonoBehaviour
{
    /// <summary>Cover lower than this hides a crouched body, not a standing one.</summary>
    public const float LowBelow = 1.3f;

    [Tooltip("How high the top of the cover stands above the ground it is on, in metres.")]
    public float height = 1f;
    [Tooltip("Half its length along the wall it stands against, in metres.")]
    public float halfLength = 0.6f;
    [Tooltip("Half its depth out from the wall, in metres.")]
    public float halfDepth = 0.3f;

    static readonly List<CoverSpot> _all = new List<CoverSpot>();

    /// <summary>Every cover spot in the loaded scenes.</summary>
    public static IReadOnlyList<CoverSpot> All => _all;

    /// <summary>Whether it hides only a crouched body.</summary>
    public bool Low => height < LowBelow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _all.Clear();

    void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }

    void OnDisable() => _all.Remove(this);
}
