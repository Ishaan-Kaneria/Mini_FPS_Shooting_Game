using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A stretch of shin-to-knee-deep floodwater. Anything standing in it moves slower.
///
/// A trigger volume that carries the numbers, rather than a check inside the player: like
/// <see cref="KillVolume"/>, the hazard belongs to the place, so enemies and the player share one
/// definition of "wading", and a second flooded arena adds a volume and no code.
///
/// <see cref="PlayerMotor"/> and <see cref="EnemyAI"/> ask <see cref="SpeedFactorAt"/> and multiply
/// their own speed by the answer. The zone only reports a multiplier; it never writes into a shared
/// asset (see CLAUDE.md: upgrades are multipliers on components). The NavMesh side of the same water
/// is an area with a higher cost, so enemies prefer the boardwalks and dry ground; this is the
/// physical slow-down once they are in it anyway.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WadingZone : MonoBehaviour
{
    [Header("Speed penalty")]
    [Tooltip("Player speed while inside, as a share of normal. 1 = no penalty, 0.65 = 35% slower.")]
    [Range(0.2f, 1f)] public float playerSpeedFactor = 0.65f;

    [Tooltip("Enemy speed while inside. Kept a little kinder than the player's so a flooded lane " +
             "slows a fight down without making the water a safe place to stand.")]
    [Range(0.2f, 1f)] public float enemySpeedFactor = 0.75f;

    [Tooltip("How much worse the penalty is while sprinting. 1 = the same, 1.5 = half as bad again. " +
             "Running through water should cost more than walking through it.")]
    [Range(0.5f, 2f)] public float sprintPenaltyScale = 1.3f;

    [Header("Water")]
    [Tooltip("World height of the surface. For the audio and splash code, to know when feet are wet.")]
    public float surfaceY;

    static readonly List<WadingZone> Zones = new List<WadingZone>();

    Collider _volume;

    // Domain reload is off: a static list would otherwise keep zones from the previous play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Zones.Clear();

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
        surfaceY = transform.position.y + transform.lossyScale.y * 0.5f;
    }

    void OnEnable()
    {
        _volume = GetComponent<Collider>();
        if (!Zones.Contains(this)) Zones.Add(this);
    }

    void OnDisable() { Zones.Remove(this); }

    /// <summary>
    /// Speed multiplier at a world position: 1 outside every zone, the lowest factor inside overlapping ones.
    /// Sprinting makes the penalty bigger by <see cref="sprintPenaltyScale"/>.
    /// </summary>
    public static float SpeedFactorAt(Vector3 position, bool isPlayer, bool sprinting = false)
    {
        float factor = 1f;
        for (int i = 0; i < Zones.Count; i++)
        {
            var z = Zones[i];
            if (z == null || !z.isActiveAndEnabled || z._volume == null) continue;
            if (!z._volume.bounds.Contains(position)) continue;

            float f = isPlayer ? z.playerSpeedFactor : z.enemySpeedFactor;
            if (sprinting) f = 1f - (1f - f) * z.sprintPenaltyScale;
            if (f < factor) factor = f;
        }
        return Mathf.Max(0.2f, factor);
    }
}
