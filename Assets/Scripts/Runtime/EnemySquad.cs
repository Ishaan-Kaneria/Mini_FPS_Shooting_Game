using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the enemies share: whose turn it is to shoot, word passed between them, and the
/// radio.
///
/// - **Turns to shoot.** Only a few may fire at the player at once (more on a harder
///   level). The rest hold cover, move up or go round, and take a turn when one comes
///   free. A fight is fair because it is paced, not because each enemy is weak.
/// - **Word passed.** An enemy that finds the player calls it out; anyone in earshot
///   knows, a beat later, roughly where.
/// - **The radio.** Calm enemies are not left standing about forever: every so often, while
///   nobody is fighting, a couple of them are told the player was seen somewhere near
///   where they really are, and go to look. Without it a quiet player could see out a
///   level without meeting anyone, and the clock, not the fight, would end it.
/// </summary>
public static class EnemySquad
{
    static readonly List<EnemyBrain> _all = new List<EnemyBrain>();
    static readonly Dictionary<EnemyBrain, float> _turns = new Dictionary<EnemyBrain, float>();
    static int _tickedFrame = -1;
    static float _nextRadio;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _all.Clear();
        _turns.Clear();
        _tickedFrame = -1;
        _nextRadio = 0f;
    }

    public static IReadOnlyList<EnemyBrain> All => _all;

    public static void Join(EnemyBrain brain)
    {
        if (!_all.Contains(brain)) _all.Add(brain);
    }

    public static void Leave(EnemyBrain brain)
    {
        _all.Remove(brain);
        _turns.Remove(brain);
    }

    // ======================================================================
    // Turns
    // ======================================================================

    /// <summary>How many may shoot at once, by how aggressive the level has made them.</summary>
    public static int Shooters(float aggression) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 4f, aggression)), 1, 6);

    /// <summary>
    /// Asks for a turn to shoot. A turn lasts a few seconds, then must be asked for again,
    /// so the ones waiting get theirs. Returns whether this one may fire now.
    /// </summary>
    public static bool RequestTurn(EnemyBrain brain, float aggression)
    {
        Tidy();
        if (_turns.TryGetValue(brain, out float until))
        {
            if (Time.time < until) return true;
            _turns.Remove(brain);
            return false;        // a breather before asking again
        }

        if (_turns.Count >= Shooters(aggression)) return false;
        _turns[brain] = Time.time + Random.Range(2.5f, 4.5f);
        return true;
    }

    public static void ReleaseTurn(EnemyBrain brain) => _turns.Remove(brain);

    static readonly List<EnemyBrain> _stale = new List<EnemyBrain>();

    static void Tidy()
    {
        _stale.Clear();
        foreach (var pair in _turns)
            if (pair.Key == null || !pair.Key.isActiveAndEnabled || Time.time > pair.Value + 1.5f) _stale.Add(pair.Key);
        foreach (var b in _stale) _turns.Remove(b);
    }

    // ======================================================================
    // Word and radio
    // ======================================================================

    /// <summary>An enemy calling out where the player is. Heard through the noise system.</summary>
    public static void Shout(EnemyBrain from, Vector3 playerAt, float radius)
    {
        foreach (var other in _all)
        {
            if (other == null || other == from) continue;
            if (Vector3.Distance(other.transform.position, from.transform.position) > radius) continue;
            other.HearCall(playerAt, Random.Range(0.3f, 0.9f));
        }
    }

    /// <summary>Once a frame, by whichever brain thinks first: the radio.</summary>
    public static void Tick(Transform player)
    {
        if (_tickedFrame == Time.frameCount) return;
        _tickedFrame = Time.frameCount;
        if (player == null || _all.Count == 0) return;

        if (_nextRadio <= 0f) _nextRadio = Time.time + Random.Range(10f, 16f);
        if (Time.time < _nextRadio) return;
        _nextRadio = Time.time + Random.Range(14f, 24f);

        // Somebody is already on it: the radio stays quiet.
        foreach (var b in _all)
            if (b != null && b.Mind >= EnemyBrain.Awareness.Alert) return;

        // A couple of the nearest calm ones are sent to look at a spot near the player --
        // near, not on: a report, not a gun sight.
        _sorted.Clear();
        foreach (var b in _all) if (b != null && b.isActiveAndEnabled) _sorted.Add(b);
        _sorted.Sort((a, c) => (a.transform.position - player.position).sqrMagnitude
                               .CompareTo((c.transform.position - player.position).sqrMagnitude));

        int sent = 0;
        foreach (var b in _sorted)
        {
            Vector2 off = Random.insideUnitCircle * Random.Range(10f, 22f);
            b.HearReport(player.position + new Vector3(off.x, 0f, off.y));
            if (++sent >= 2) break;
        }
    }

    static readonly List<EnemyBrain> _sorted = new List<EnemyBrain>();
}
