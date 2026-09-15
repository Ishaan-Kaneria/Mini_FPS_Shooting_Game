using UnityEngine;

/// <summary>
/// A dropped reward that bobs, spins and is collected by walking near it.
///
/// Proximity is checked against the player transform every frame rather than
/// through a trigger volume. A CharacterController's trigger events depend on the
/// layer collision matrix and on which side owns a Rigidbody, and a pickup that
/// silently fails to be collectable is a worse bug than the handful of distance
/// checks this costs.
/// </summary>
public class Pickup : MonoBehaviour
{
    public enum Kind
    {
        Health,
        Shield,
        Ammo
    }

    [Header("Payload")]
    public Kind kind = Kind.Health;

    [Tooltip("Health or shield points restored. Ignored for ammo.")]
    public float amount = 30f;

    [Tooltip("Rounds added to the reserve. Ignored unless this is an ammo pickup.")]
    public int ammoAmount = 60;

    [Header("Collection")]
    public float collectRadius = 1.6f;

    [Tooltip("Inside this distance the pickup drifts toward the player. Generous " +
             "collection is the right call in a game that wants you moving under fire.")]
    public float magnetRadius = 5f;

    public float magnetSpeed = 7f;

    [Tooltip("Refuse to be picked up when it would be wasted, so a full-health player " +
             "leaves the medkit on the floor for later instead of burning it.")]
    public bool requireEffect = true;

    [Header("Lifetime")]
    [Tooltip("Seconds before it despawns. 0 means it waits forever.")]
    public float lifetime = 30f;

    [Tooltip("Seconds of blinking before it goes, as a warning.")]
    public float blinkWarning = 5f;

    [Header("Motion")]
    public float bobHeight = 0.22f;
    public float bobSpeed = 2.2f;
    public float spinSpeed = 90f;

    [Header("Feedback (optional)")]
    public AudioClip collectClip;
    public GameObject collectEffect;
    [Range(0f, 1f)] public float collectVolume = 0.7f;

    Transform _player;
    Health _playerHealth;
    Weapon _playerWeapon;
    Renderer[] _renderers;
    Vector3 _anchor;
    float _expiry;
    float _phase;

    void Start()
    {
        _anchor = transform.position;
        _phase = Random.Range(0f, Mathf.PI * 2f);
        _renderers = GetComponentsInChildren<Renderer>();
        _expiry = lifetime > 0f ? Time.time + lifetime : float.MaxValue;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        _player = player.transform;
        _playerHealth = player.GetComponent<Health>();
        _playerWeapon = player.GetComponentInChildren<Weapon>();
    }

    void Update()
    {
        if (Time.time >= _expiry)
        {
            Destroy(gameObject);
            return;
        }

        UpdateBlink();

        if (_player != null)
        {
            float distance = Vector3.Distance(transform.position, _player.position);

            if (distance <= collectRadius)
            {
                TryCollect();
                return;
            }

            if (distance <= magnetRadius)
            {
                // Drawn in, and the anchor follows so the bob does not fight the pull.
                _anchor = Vector3.MoveTowards(_anchor, _player.position + Vector3.up,
                                              magnetSpeed * Time.deltaTime);
            }
        }

        transform.position = _anchor + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + _phase) * bobHeight);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    void UpdateBlink()
    {
        if (_renderers == null || blinkWarning <= 0f || lifetime <= 0f) return;

        float remaining = _expiry - Time.time;
        if (remaining > blinkWarning) return;

        bool on = Mathf.Repeat(remaining, 0.28f) > 0.14f;
        foreach (var renderer in _renderers)
            if (renderer != null) renderer.enabled = on;
    }

    void TryCollect()
    {
        if (!Apply() && requireEffect) return;

        if (collectClip != null)
            OneShotAudio.Play(collectClip, transform.position, collectVolume);

        if (collectEffect != null)
            Destroy(Instantiate(collectEffect, transform.position, Quaternion.identity), 3f);

        Destroy(gameObject);
    }

    /// <summary>Returns true only if the pickup actually changed something.</summary>
    bool Apply()
    {
        switch (kind)
        {
            case Kind.Health:
                return _playerHealth != null && _playerHealth.Heal(amount) > 0f;

            case Kind.Shield:
                return _playerHealth != null && _playerHealth.RestoreShield(amount) > 0f;

            case Kind.Ammo:
                if (_playerWeapon == null) return false;
                return _playerWeapon.AddReserveAmmo(ammoAmount) > 0;

            default:
                return false;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, collectRadius);

        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, magnetRadius);
    }
}
