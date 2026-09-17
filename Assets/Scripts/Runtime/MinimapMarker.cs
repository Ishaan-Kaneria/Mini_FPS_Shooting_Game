using UnityEngine;

/// <summary>
/// Puts one object on the minimap on its own terms, overriding everything
/// <see cref="Minimap"/> would otherwise work out for itself.
///
/// The map reads the world rather than a hand-drawn plan -- it takes the footprint of
/// every solid thing on the environment layer and tones it by height. That is what
/// makes it work in a level the kit never built. But it also means the map only knows
/// what the geometry tells it, and geometry cannot say "this one is water and it will
/// kill you" or "that is the only way across".
///
/// So this is the seam. Drop it on an object and the map draws that object the way the
/// component says, skipping every filter: a river is water-coloured however flat it is,
/// a bridge reads as the crossing it is, a landmark shows as an icon rather than as the
/// shape of its collider. Content, not code -- the same reason an arena is a
/// LevelTheme asset and an enemy is an EnemyArchetype.
/// </summary>
[DisallowMultipleComponent]
public class MinimapMarker : MonoBehaviour
{
    public enum Style
    {
        /// <summary>Draw the object's own footprint, oriented the way it is placed.</summary>
        Footprint,

        /// <summary>Draw a fixed-size pip at its centre, whatever size the object is.</summary>
        Dot,

        /// <summary>Draw nothing. For scenery big enough to be picked up as a structure
        /// and unimportant enough that showing it is noise.</summary>
        Hidden
    }

    [Header("How it reads")]
    public Style style = Style.Footprint;

    [Tooltip("The colour on the map. This is the whole point of the component -- a river " +
             "and the bridge over it are the same grey box to a height test.")]
    public Color color = new Color(0.35f, 0.6f, 0.95f, 0.85f);

    [Tooltip("Size of a Dot, in metres of world, so it stays the same size on the map " +
             "however far the map is zoomed out.")]
    [Min(0.2f)] public float dotRadius = 2f;

    [Tooltip("Higher draws later, so it lands on top. A bridge wants to be above the " +
             "water it crosses or the crossing is invisible, which is the one thing " +
             "about it worth knowing.")]
    public int order;

    [Tooltip("Follow this object every frame instead of reading its position once at " +
             "the start of the level. Off for scenery, which is the usual case and " +
             "costs nothing; on for anything that moves.")]
    public bool moves;
}
