using UnityEngine;

/// <summary>
/// How many civilians died in the current level (the desert town's people, see <see cref="Civilian"/>).
///
/// Reset when a level starts (LevelManager.RunLevel) and read when it ends (LevelManager.Finish): any civilian
/// killed caps the level at two stars, three or more at one, and each costs <see cref="ScorePenalty"/> points.
/// That is the user's rule for the town (2026-10-20: "hitting a civilian costs score or stars").
/// </summary>
public static class CivilianCasualties
{
    /// <summary>Points taken off the score for each civilian killed.</summary>
    public const int ScorePenalty = 300;

    public static int Count { get; private set; }

    /// <summary>Raised when a civilian dies, with the new count (the HUD can warn).</summary>
    public static event System.Action<int> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Count = 0;
        Changed = null;
    }

    public static void Reset() => Count = 0;

    public static void Report()
    {
        Count++;
        Changed?.Invoke(Count);
    }

    /// <summary>The star cap for this many civilian deaths: 3 for none, 2 for one or two, 1 for three or more.</summary>
    public static int StarCap(int count) => count <= 0 ? 3 : count < 3 ? 2 : 1;
}
