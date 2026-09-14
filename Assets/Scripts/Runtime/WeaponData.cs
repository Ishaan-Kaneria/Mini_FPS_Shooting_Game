using UnityEngine;

public enum FireMode { Single, Burst, Auto }

/// <summary>
/// All tuning for one weapon. Create via Assets > Create > FPSKit > Weapon Data,
/// or let FPSKitSceneBuilder generate one.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Weapon Data", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName = "Rifle";
    public FireMode fireMode = FireMode.Auto;

    [Header("Ballistics")]
    public float roundsPerMinute = 600f;
    public float damage = 24f;
    public float maxRange = 200f;
    [Min(1)] public int pelletsPerShot = 1;          // >1 turns it into a shotgun
    public int burstCount = 3;
    public float burstInterval = 0.07f;

    [Header("Damage Falloff")]
    public float falloffStart = 40f;                 // full damage up to here
    public float falloffEnd = 120f;                  // minimum damage from here
    [Range(0f, 1f)] public float minDamageFactor = 0.55f;

    [Header("Ammo")]
    public int magazineSize = 30;
    public int reserveAmmo = 150;

    [Tooltip("Ceiling the reserve can be topped up to by pickups. 0 means no ceiling. " +
             "Without one, a long run turns ammo drops into litter.")]
    [Min(0)] public int maxReserveAmmo = 360;
    public float reloadTime = 2.1f;
    public bool autoReloadWhenEmpty = true;

    [Tooltip("Reserve never runs out. You still reload every magazine, which is what " +
             "keeps the gun feeling like a gun. This is what most games call unlimited ammo.")]
    public bool infiniteReserve = true;

    [Tooltip("The magazine never empties either, so there is no reloading at all. " +
             "Truly unlimited -- turn this on for pure blasting.")]
    public bool infiniteAmmo;

    [Header("Accuracy (cone half-angle, degrees)")]
    public float baseSpread = 0.6f;
    public float spreadPerShot = 0.45f;
    public float maxSpread = 5f;
    public float spreadRecovery = 6f;                // degrees per second
    [Range(0f, 1f)] public float adsSpreadMultiplier = 0.15f;
    public float movementSpreadPenalty = 1.6f;       // multiplier at full sprint
    public float airborneSpreadPenalty = 2.5f;

    [Header("Recoil")]
    public float recoilVertical = 1.15f;             // degrees of pitch per shot
    public float recoilHorizontal = 0.4f;            // +/- degrees of yaw per shot
    public float recoilRecovery = 9f;
    [Range(0f, 1f)] public float adsRecoilMultiplier = 0.7f;
    public float visualKickback = 0.045f;            // metres the model punches back

    [Header("Aim Down Sights")]
    public float adsFieldOfView = 40f;
    public Vector3 adsPosition = new Vector3(0f, -0.02f, 0.12f);
    public float adsSpeed = 12f;

    [Header("Feel")]
    public float muzzleFlashDuration = 0.045f;
    public float impactForce = 35f;

    [Tooltip("Camera shake per shot, 0 to 1. Kept low on an automatic -- shake stacked " +
             "over a held trigger is the fastest way to make a gun unaimable.")]
    [Range(0f, 1f)] public float cameraShake = 0.18f;

    [Header("Audio (optional)")]
    public AudioClip fireClip;
    public AudioClip reloadClip;
    public AudioClip emptyClip;
    [Range(0f, 0.3f)] public float pitchVariance = 0.06f;

    [Header("VFX (optional)")]
    public GameObject muzzleFlashPrefab;
    public GameObject tracerPrefab;
    public float tracerSpeed = 240f;
    public ImpactLibrary impacts;

    public float SecondsBetweenShots => roundsPerMinute <= 0f ? 0.1f : 60f / roundsPerMinute;

    public float DamageAtDistance(float distance)
    {
        if (distance <= falloffStart || falloffEnd <= falloffStart) return damage;
        float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
        return damage * Mathf.Lerp(1f, minDamageFactor, t);
    }
}
