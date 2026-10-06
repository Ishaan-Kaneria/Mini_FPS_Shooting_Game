using UnityEngine;

/// <summary>
/// What a first-person gun model is, for <see cref="ViewModelRig"/>: where the hands
/// go on it, which parts move, and where it sits in the view. Sits on the root of each
/// generated view-model prefab (FPSKit > View Models builds them; edit the builder's
/// table, not the prefab).
///
/// The root's frame is the line of sight: +Z down the barrel, the rear sight on the
/// origin, so putting the root on the eye's axis is what aiming down the sights means.
/// </summary>
public class ViewModelSpec : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Where the right wrist goes: +Z is the way the fingers point, +Y the back " +
             "of the hand.")]
    public Transform rightHand;
    [Tooltip("Where the left wrist goes on the handguard, same convention. The reload " +
             "moves this, and the hand follows it.")]
    public Transform leftHand;
    [Tooltip("The end of the barrel. The muzzle flash and tracers start here.")]
    public Transform muzzle;
    [Tooltip("Dropped and replaced by the reload. Optional.")]
    public Transform magazine;
    [Tooltip("Slides back on each shot and on an empty reload. Optional.")]
    public Transform bolt;
    [Tooltip("Metres from the root (the rear sight) back to the butt plate. The rifle-butt strike lands this.")]
    public float buttLength = 0.28f;
    [Tooltip("Which way the bolt travels when it runs, in the root's space, metres.")]
    public Vector3 boltTravel = new Vector3(0f, 0f, -0.05f);

    [Header("Placement, in the view's space (x right, y up, z ahead)")]
    [Tooltip("Where the rear sight sits held at the hip.")]
    public Vector3 hipPosition = new Vector3(0.13f, -0.15f, 0.12f);
    [Tooltip("The gun's turn at the hip, in degrees: a little inward and canted.")]
    public Vector3 hipTurn = new Vector3(6f, -2f, -10f);
    [Tooltip("Where the rear sight sits aimed: on the eye's axis, this far ahead.")]
    public Vector3 aimPosition = new Vector3(0f, 0f, 0.11f);

    [Header("Bodies")]
    [Tooltip("The arms, a Humanoid rig. Its head bone is put on the eye.")]
    public GameObject armsPrefab;
    [Tooltip("The player's body below the arms, a Humanoid rig: seen looking down, walks with " +
             "the player and throws the kick.")]
    public GameObject legsPrefab;
    [Tooltip("Moves the arms from head-on-the-eye, in the view's space: shoulders a " +
             "little forward and down, as they are when a rifle is shouldered.")]
    public Vector3 armsOffset = new Vector3(0f, -0.04f, 0.07f);
}
