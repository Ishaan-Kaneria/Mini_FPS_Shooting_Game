using UnityEngine;

/// <summary>
/// How badly the town's police want the player: 0 to <see cref="Max"/> (2026-11-11: killing civilians only capped a level's stars,
/// and nobody came). Crimes raise it; with no new crime and no officer near him it clears after a while, all at once, as in GTA.
/// <see cref="PoliceDirector"/> answers it with cars and officers and keeps its clock. It is not saved and does not touch the
/// level's score or stars (CivilianCasualties still does that).
/// </summary>
public static class WantedLevel
{
    public enum Crime { CarTheft, HurtCivilian, KilledCivilian, KilledOfficer }

    public const int Max = 5;

    public static int Stars { get; private set; }
    /// <summary>Where the last crime was: where the police are sent.</summary>
    public static Vector3 LastCrimeAt { get; private set; }
    /// <summary>Raised when <see cref="Stars"/> changes.</summary>
    public static event System.Action<int> Changed;

    static float _quietFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Stars = 0;
        _quietFor = 0f;
        Changed = null;
    }

    /// <summary>A new level starts clean.</summary>
    public static void Clear() => Set(0);

    /// <summary>Seconds of quiet, out of the police's sight, before the level clears: longer the more they want him.</summary>
    public static float CoolDown(int stars) => 25f + 10f * stars;

    public static void Report(Crime crime, Vector3 at)
    {
        int want = crime switch
        {
            Crime.KilledCivilian => Mathf.Max(Stars + 1, 2),
            Crime.KilledOfficer => Mathf.Max(Stars + 1, 3),
            _ => Mathf.Max(Stars, 1),
        };
        LastCrimeAt = at;
        _quietFor = 0f;
        Set(Mathf.Min(Max, want));
    }

    /// <summary>The clock, run by PoliceDirector: it only counts while no officer is near enough to see him.</summary>
    public static void Tick(float seconds, bool seen)
    {
        if (Stars <= 0) return;
        if (seen) { _quietFor = 0f; return; }
        _quietFor += seconds;
        if (_quietFor >= CoolDown(Stars)) Set(0);
    }

    static void Set(int stars)
    {
        if (stars == Stars) return;
        Stars = stars;
        if (stars == 0) _quietFor = 0f;
        Changed?.Invoke(stars);
    }
}
