using System;
using UnityEngine;

/// <summary>
/// The energy drinks the player bought, and what happens when one goes down.
///
/// A drink is the one purchase that is spent rather than owned, and it is the only one
/// that can change the level you are already losing: health back, shield back, and
/// thirty seconds of moving and shooting faster. That last part is why it is a drink
/// and not a medkit -- a heal buys you the same fight again, and a rush lets you go and
/// win it.
///
/// The count is <see cref="Loadout"/>'s, not this component's, and it is decremented
/// through PlayerPrefs the moment one is used. A drink taken and then a browser tab
/// closed must not come back.
/// </summary>
[DisallowMultipleComponent]
public class ConsumableBelt : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Found on this object when empty.")]
    public Health health;

    [Tooltip("Found on this object or below it when empty.")]
    public PlayerMotor motor;

    [Tooltip("Found below this object when empty. The rush speeds the gun up too.")]
    public Weapon weapon;

    [Tooltip("Bindings. Taken from the motor when empty, so both stay in sync.")]
    public ControlSettings controls;

    public AudioSource audioSource;

    [Header("Belt")]
    [Tooltip("What is on the belt. PlayerLoadout writes this from the store; set it by " +
             "hand for a scene that never goes through the dashboard.")]
    public ConsumableData data;

    [Tooltip("The store id the count is kept under. Written by PlayerLoadout.")]
    public string itemId = "";

    // ======================================================================

    /// <summary>How many are left. Read straight through, so the HUD cannot go stale.</summary>
    public int Count => string.IsNullOrEmpty(itemId) ? 0 : Loadout.Stock(itemId);

    /// <summary>True while a rush is running.</summary>
    public bool BoostActive => Time.time < _boostUntil;

    /// <summary>Seconds of rush left, for the HUD bar. 0 when there is none.</summary>
    public float BoostRemaining => Mathf.Max(0f, _boostUntil - Time.time);

    /// <summary>0 to 1 across the rush, so a bar can drain rather than jump.</summary>
    public float BoostNormalized
        => _boostDuration <= 0f ? 0f : Mathf.Clamp01(BoostRemaining / _boostDuration);

    /// <summary>Raised after one is used, with how many are left.</summary>
    public event Action<ConsumableBelt, int> Used;

    /// <summary>Raised when the key is pressed with an empty belt. The HUD flashes on it.</summary>
    public event Action<ConsumableBelt> Denied;

    float _boostUntil;
    float _boostDuration;
    bool _boostApplied;

    ControlSettings _fallbackControls;

    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;
            if (motor != null && motor.controls != null) return motor.controls;

            return _fallbackControls ??= ControlSettings.CreateDefault();
        }
    }

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (motor == null) motor = GetComponentInParent<PlayerMotor>();
        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void OnDisable() => ClearBoost();

    void Update()
    {
        if (PlayerMotor.InputEnabled &&
            (ControlSettings.Pressed(Bindings.useItem) || MobileInput.ConsumeUseItem() || GameInput.PadPressed(GameAction.UseItem)))
            TryUse();

        // Checked every frame rather than scheduled with a coroutine, because a coroutine
        // does not survive a mid-play domain reload and the multipliers it was going to
        // put back would be left on the player forever. A float expiry does survive it.
        if (_boostApplied && !BoostActive) ClearBoost();
    }

    // ======================================================================

    /// <summary>
    /// Drinks one. Returns false when there is nothing to drink, which is what the HUD
    /// flashes on -- an item key that silently does nothing reads as a broken key.
    /// </summary>
    public bool TryUse()
    {
        if (data == null || string.IsNullOrEmpty(itemId) || Count <= 0)
        {
            Denied?.Invoke(this);
            return false;
        }

        if (health != null && health.IsDead) return false;

        // Taken before it is applied. The other order would hand out the heal and then
        // discover the belt was empty, which is a free drink for anyone who can make two
        // calls land on the same frame.
        if (!Loadout.TryUseStock(itemId))
        {
            Denied?.Invoke(this);
            return false;
        }

        if (health != null)
        {
            if (data.healthRestore > 0f) health.Heal(data.healthRestore);
            if (data.shieldRestore > 0f) health.RestoreShield(data.shieldRestore);
        }

        if (data.HasBoost) ApplyBoost();

        if (data.useClip != null && audioSource != null)
            audioSource.PlayOneShot(data.useClip, 0.9f);

        Used?.Invoke(this, Count);
        return true;
    }

    /// <summary>
    /// Starts the rush, or extends one that is already running.
    ///
    /// Absolute rather than stacking: a second drink resets the clock instead of
    /// doubling the speed. Stacking multipliers is how a consumable turns into the only
    /// thing worth buying, and a player at three times walking speed is a player whose
    /// level has stopped being a fight.
    /// </summary>
    void ApplyBoost()
    {
        _boostDuration = data.boostDuration;
        _boostUntil = Time.time + _boostDuration;
        _boostApplied = true;

        if (motor != null) motor.SpeedMultiplier = data.moveSpeedMultiplier;

        if (weapon != null)
        {
            weapon.BoostFireRateMultiplier = data.fireRateMultiplier;
            weapon.BoostReloadTimeMultiplier = data.reloadTimeMultiplier;
        }
    }

    /// <summary>
    /// Puts the multipliers back. Called when the rush ends, when the belt is disabled,
    /// and by PlayerLoadout at the start of a level -- because this is the one piece of
    /// state here that can outlive the thing that set it.
    /// </summary>
    public void ClearBoost()
    {
        _boostApplied = false;
        _boostUntil = 0f;
        _boostDuration = 0f;

        if (motor != null) motor.SpeedMultiplier = 1f;

        if (weapon != null)
        {
            weapon.BoostFireRateMultiplier = 1f;
            weapon.BoostReloadTimeMultiplier = 1f;
        }
    }

    /// <summary>Arms the belt for a level. Called by PlayerLoadout once the store selection is known.</summary>
    public void Equip(ConsumableData item, string id)
    {
        ClearBoost();

        data = item;
        itemId = id ?? "";
    }
}
