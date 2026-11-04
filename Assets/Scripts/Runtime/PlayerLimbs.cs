using UnityEngine;

/// <summary>
/// The player's fists and kicking foot as things that can be hurt (Docs/DESERT_BODY.md). A bare fist on a wall hurts;
/// a boot on concrete hurts less, the rifle's stock hardly at all; a skull is hard too. Every blow adds strain to the
/// limb that threw it, by what it struck (concrete, stone and metal most, wood half, sand and cloth little) and by how
/// strained the limb already is, and costs a little health. Strain only builds, past three steps that never go back
/// within a level, as the enemies' limbs do: Bruised (it eases off again if left alone), Swollen, Fractured.
///
/// What an injury costs: a sore hand punches softer and reloads slower; a broken one punches feebly, hurts the player
/// every time it is used, and makes the reload slow and one-handed. A sore foot kicks softer and the player limps a
/// little; a broken one kicks feebly, takes the sprint away and slows every step.
///
/// Added by <see cref="MeleeStrike"/> on first use. Holds no static state; a new level is a new body.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLimbs : MonoBehaviour
{
    public enum Limb { LeftHand = 0, RightHand = 1, RightFoot = 2 }
    public enum Injury { Healthy, Bruised, Swollen, Fractured }

    [Header("Strain (0 sound, 1 broken)")]
    [Tooltip("Strain one bare-fisted punch on concrete adds. Swollen by the third, broken by the fifth.")]
    [Range(0f, 0.6f)] public float strainPerBlow = 0.22f;
    [Tooltip("Where each step starts.")]
    public float bruisedAt = 0.25f, swollenAt = 0.55f, fracturedAt = 0.9f;
    [Tooltip("Strain a bruise loses per second while the limb is left alone (only below Swollen).")]
    [Min(0f)] public float bruiseHealPerSecond = 0.004f;
    [Tooltip("Seconds of rest before a bruise starts to ease.")]
    [Min(0f)] public float restBeforeHealing = 20f;

    [Header("Pain")]
    [Tooltip("Health one bare-fisted punch on concrete costs, at no strain; it grows with the strain.")]
    [Min(0f)] public float healthPerBlow = 6f;
    [Tooltip("Extra health a blow with a broken limb costs.")]
    [Min(0f)] public float brokenBlowHealth = 10f;
    [Tooltip("View shake on a painful blow, at full strain.")]
    [Range(0f, 1f)] public float painShake = 0.35f;

    float[] _strain, _lastUse;
    float[] Strain => _strain ??= new float[3];
    float[] LastUse => _lastUse ??= new float[3];

    Health _health;
    PlayerMotor _motor;
    Weapon _weapon;

    /// <summary>Raised when a limb gets worse (for the HUD, a sound).</summary>
    public event System.Action<PlayerLimbs, Limb, Injury> Worsened;

    public Injury StateOf(Limb limb)
    {
        float s = Strain[(int)limb];
        return s >= fracturedAt ? Injury.Fractured : s >= swollenAt ? Injury.Swollen : s >= bruisedAt ? Injury.Bruised : Injury.Healthy;
    }

    /// <summary>What a blow from this limb is worth against a sound one.</summary>
    public float StrikeMultiplier(Limb limb) => StateOf(limb) switch
    {
        Injury.Bruised => 0.85f,
        Injury.Swollen => 0.6f,
        Injury.Fractured => 0.3f,
        _ => 1f,
    };

    /// <summary>How hard a surface is on a bare limb, 0 (cloth, sand) to about 1.1 (steel).</summary>
    public static float Hardness(ImpactSurface surface) => surface switch
    {
        ImpactSurface.Metal => 1.1f,
        ImpactSurface.Concrete => 1f,
        ImpactSurface.Ice => 0.8f,
        ImpactSurface.Glass => 0.7f,
        ImpactSurface.Wood => 0.5f,
        ImpactSurface.Flesh => 0.3f,       // a skull or a jaw: boxers break their hands on them
        ImpactSurface.Sand => 0.15f,
        ImpactSurface.Snow => 0.1f,
        _ => 0.8f,
    };

    void Awake()
    {
        _health = GetComponent<Health>();
        _motor = GetComponent<PlayerMotor>();
        _weapon = GetComponentInChildren<Weapon>();
    }

    /// <summary>
    /// A blow from <paramref name="limb"/> met something of <paramref name="hardness"/>; <paramref name="padding"/> is how
    /// much of it the limb is spared (1 bare fist, about 0.45 a boot, about 0.12 the rifle's stock).
    /// </summary>
    public void Impact(Limb limb, float hardness, float padding, Vector3 at)
    {
        int i = (int)limb;
        var before = StateOf(limb);
        float load = Mathf.Max(0f, hardness) * Mathf.Max(0f, padding);
        if (load <= 0.001f) return;
        float s = Strain[i];
        Strain[i] = Mathf.Min(1.2f, s + strainPerBlow * load * (1f + 0.5f * s));
        LastUse[i] = Time.time;

        float hurt = healthPerBlow * load * (1f + s) + (before == Injury.Fractured ? brokenBlowHealth : 0f);
        if (_health != null && hurt > 0.05f)
            _health.ApplyDamage(new DamageInfo(hurt, at, Vector3.up, Vector3.down, gameObject) { part = BodyPart.Torso });
        if (_motor != null) _motor.AddShake(painShake * Mathf.Clamp01(load * (0.4f + s)));

        var after = StateOf(limb);
        if (after != before) { Apply(); Worsened?.Invoke(this, limb, after); }
    }

    void Update()
    {
        // A bruise left alone eases; swelling and a break stay for the level.
        bool changed = false;
        for (int i = 0; i < 3; i++)
        {
            float s = Strain[i];
            if (s <= 0f || s >= swollenAt || Time.time - LastUse[i] < restBeforeHealing) continue;
            var before = StateOf((Limb)i);
            Strain[i] = Mathf.Max(0f, s - bruiseHealPerSecond * Time.deltaTime);
            if (StateOf((Limb)i) != before) changed = true;
        }
        if (changed) Apply();
    }

    /// <summary>What the injuries do to the rest of the body: the reload, the walk, the sprint.</summary>
    void Apply()
    {
        var worstHand = (Injury)Mathf.Max((int)StateOf(Limb.LeftHand), (int)StateOf(Limb.RightHand));
        if (_weapon == null) _weapon = GetComponentInChildren<Weapon>();
        if (_weapon != null)
            _weapon.InjuryReloadMultiplier = worstHand == Injury.Fractured ? 1.6f : worstHand == Injury.Swollen ? 1.25f : 1f;
        var foot = StateOf(Limb.RightFoot);
        if (_motor != null)
        {
            _motor.InjuryMultiplier = foot == Injury.Fractured ? 0.75f : foot == Injury.Swollen ? 0.92f : 1f;
            _motor.SprintBlocked = foot == Injury.Fractured;
        }
    }
}
