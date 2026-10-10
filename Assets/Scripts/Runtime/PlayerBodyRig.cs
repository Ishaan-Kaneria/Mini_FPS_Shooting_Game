using UnityEngine;

/// <summary>
/// The player body prefab's own references, set by FPSKit > Player > Build Body: its Animator
/// and the points the rifle is carried at. All of them are in the skeleton from the start
/// (nothing is ever parented into it in play: see CLAUDE.md, the Animator rebinds).
/// </summary>
public class PlayerBodyRig : MonoBehaviour
{
    public Animator animator;
    [Tooltip("Under the right hand: where a rifle's right-hand grip sits, bore straight ahead, in the aiming clip.")]
    public Transform rifleGrip;
    [Tooltip("Under the upper chest: where the rifle rides on the sling, across the back.")]
    public Transform slingPoint;
}
