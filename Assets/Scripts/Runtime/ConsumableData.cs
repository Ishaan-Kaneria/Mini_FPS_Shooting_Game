using UnityEngine;

/// <summary>
/// One thing you can carry into a level and use: an energy drink, a stim, a shield
/// cell. Data rather than a prefab, so a second one is a duplicated asset.
///
/// A consumable is the only purchase in the store that is spent rather than owned.
/// That is what it is for: a gun changes every level you play from now on, and a drink
/// changes the next thirty seconds of the one you are in.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Consumable Data", fileName = "NewConsumable")]
public class ConsumableData : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Energy Drink";

    [TextArea(2, 3)]
    public string description;

    [Tooltip("Tint for the HUD counter and the store card, so a belt of three different " +
             "things can be read at a glance.")]
    public Color tint = new Color(0.4f, 0.9f, 1f);

    [Header("Instant")]
    [Tooltip("Health restored the moment it is used. This is the half a player reaches " +
             "for it at three hit points for.")]
    [Min(0f)] public float healthRestore = 55f;

    [Tooltip("Shield topped up straight away, bypassing the regeneration delay.")]
    [Min(0f)] public float shieldRestore = 50f;

    [Header("The rush")]
    [Tooltip("Seconds the boost lasts. Zero makes this a pure heal.")]
    [Min(0f)] public float boostDuration = 12f;

    [Tooltip("Multiplier on movement speed while it lasts. This is most of what makes " +
             "it read as energy rather than as a bandage.")]
    [Range(1f, 2.5f)] public float moveSpeedMultiplier = 1.3f;

    [Tooltip("Multiplier on rounds per minute while it lasts.")]
    [Range(1f, 3f)] public float fireRateMultiplier = 1.35f;

    [Tooltip("Multiplier on reload time while it lasts. Below 1 is faster.")]
    [Range(0.2f, 1f)] public float reloadTimeMultiplier = 0.7f;

    [Header("Audio (optional)")]
    public AudioClip useClip;

    /// <summary>True when this does nothing but heal, so the HUD can skip the timer.</summary>
    public bool HasBoost => boostDuration > 0f &&
                            (moveSpeedMultiplier > 1.001f ||
                             fireRateMultiplier > 1.001f ||
                             reloadTimeMultiplier < 0.999f);

    /// <summary>
    /// The one stats line a store card gets, built from the numbers rather than typed
    /// out a second time somewhere they can drift apart. Same three-value shape and the
    /// same separator every other card uses, so a shelf reads as a list.
    /// </summary>
    public string Effects
    {
        get
        {
            return UIText.Row(
                healthRestore > 0f ? $"+{healthRestore:0} HP" : "",
                shieldRestore > 0f ? $"+{shieldRestore:0} SHIELD" : "",
                HasBoost ? $"{UIText.Seconds(boostDuration)} RUSH" : "");
        }
    }
}
