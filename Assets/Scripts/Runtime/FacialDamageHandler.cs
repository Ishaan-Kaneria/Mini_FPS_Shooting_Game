using UnityEngine;

/// <summary>
/// What blows and heavy rounds to the head do to the face: the five shapes authored in Blender on the soldier's head
/// (Tools/blender/face_damage_keys.py; baked onto the game's head mesh by FPSKitFaceDamage), driven by the force of each
/// hit (Docs/DESIGN_NOTES.md "Pain, face damage and the head grab"):
///
/// - <b>Face_Deform_Impact_L/R</b>: the cheekbone on the side struck caved in, the jaw shifted. Driven straight to its new
///   weight on the frame of the blow (a punch caves it in at once).
/// - <b>Eye_Swollen_L/R</b>: the eye on that side puffing up and closing -- over a second and a half, as swelling does.
/// - <b>Head_Bruise_Distortion</b>: lumps coming up with every blow, over a second.
///
/// The weight a hit adds is its share of the enemy's pool times <see cref="impactPerShare"/> (more for a fist or a heavy
/// round, less for an ordinary one), up to 100; it never goes down -- a face stays beaten, and on the corpse. Bruises and
/// blood on the skin are <see cref="EnemyGore"/>'s.
///
/// Added at runtime by <see cref="EnemyBody"/> to a soldier whose head is the Survivalist head the shapes were made for;
/// it swaps that renderer's mesh for the baked copy (same vertices, bones and bind poses, plus the shapes), so no scene or
/// prefab has to be rebuilt for it.
/// </summary>
[DisallowMultipleComponent]
public class FacialDamageHandler : MonoBehaviour
{
    /// <summary>The baked head (FPSKitFaceDamage.Bake), under a Resources folder.</summary>
    public const string ResourcePath = "FaceDamage/SK_Military_Head1";
    public const string HeadMeshName = "SK_Military_Head1";

    [Header("How much a hit does")]
    [Tooltip("Weight (of 100) the cave-in on the struck side gains per share of the enemy's whole pool a hit takes: a punch " +
             "that takes a fifth of him adds about half of it.")]
    [Min(0f)] public float impactPerShare = 260f;
    [Tooltip("Weight the eye on the struck side swells by, per share.")]
    [Min(0f)] public float swellingPerShare = 200f;
    [Tooltip("Weight the lumps come up by, per share.")]
    [Min(0f)] public float bruisingPerShare = 140f;
    [Tooltip("How much harder a fist or a boot marks a face than a round of the same damage (a round mostly goes through).")]
    [Min(0f)] public float bluntScale = 1.4f;
    [Tooltip("How much a heavy round (a shotgun, a rifle's heavy class) marks it against an ordinary one.")]
    [Min(0f)] public float heavyRoundScale = 1.2f;
    [Tooltip("An ordinary round's share of that: a 9 mm graze does not cave a cheekbone in.")]
    [Range(0f, 1f)] public float roundScale = 0.35f;

    [Header("How fast")]
    [Tooltip("Seconds the swelling takes to come up to its weight.")]
    [Min(0.05f)] public float swellSeconds = 1.5f;
    [Tooltip("Seconds the lumps take.")]
    [Min(0.05f)] public float bruiseSeconds = 1.0f;

    enum Shape { ImpactL, ImpactR, EyeL, EyeR, Bruise }
    static readonly string[] Names = { "Face_Deform_Impact_L", "Face_Deform_Impact_R", "Eye_Swollen_L", "Eye_Swollen_R", "Head_Bruise_Distortion" };

    SkinnedMeshRenderer _head;
    Transform _headBone;
    Health _health;
    readonly int[] _index = { -1, -1, -1, -1, -1 };
    readonly float[] _target = new float[5];
    readonly float[] _weight = new float[5];

    static Mesh s_mesh;
    static bool s_looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_mesh = null; s_looked = false; }

    /// <summary>The baked head, or null when it has not been baked.</summary>
    static Mesh Baked
    {
        get
        {
            if (!s_looked) { s_looked = true; s_mesh = Resources.Load<Mesh>(ResourcePath); }
            return s_mesh;
        }
    }

    /// <summary>Adds the handler to a soldier whose head the shapes were made for. Null when there is none, or no bake.</summary>
    public static FacialDamageHandler Attach(GameObject enemy)
    {
        if (enemy == null || Baked == null) return null;
        var existing = enemy.GetComponent<FacialDamageHandler>();
        if (existing != null) return existing;
        foreach (var r in enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.sharedMesh != null && r.sharedMesh.name.StartsWith(HeadMeshName) && r.sharedMesh.vertexCount == Baked.vertexCount)
                return enemy.AddComponent<FacialDamageHandler>();
        return null;
    }

    /// <summary>The shapes' weights now, 0-100 (for the checks): impact L/R, eye L/R, bruise.</summary>
    public float Weight(int shape) => shape >= 0 && shape < 5 ? _weight[shape] : 0f;

    /// <summary>The head renderer is on the baked mesh and has its shapes.</summary>
    public bool Ready => _head != null && _index[0] >= 0;

    void Awake()
    {
        _health = GetComponent<Health>();
        var mesh = Baked;
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.sharedMesh == null || !r.sharedMesh.name.StartsWith(HeadMeshName)) continue;
            if (mesh == null || r.sharedMesh.vertexCount != mesh.vertexCount) continue;
            r.sharedMesh = mesh;
            _head = r;
            break;
        }
        if (_head != null)
            for (int i = 0; i < Names.Length; i++) _index[i] = _head.sharedMesh.GetBlendShapeIndex(Names[i]);
        var animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman) _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void OnEnable() { if (_health != null) _health.Damaged += OnDamaged; }
    void OnDisable() { if (_health != null) _health.Damaged -= OnDamaged; }

    void OnDamaged(Health health, DamageInfo info)
    {
        if (!Ready || info.amount <= 0f || (info.part != BodyPart.Head && info.part != BodyPart.Neck)) return;
        float share = info.amount / Mathf.Max(1f, health.maxHealth + health.maxShield);
        float force = share * (info.fromMelee ? bluntScale : info.heavy ? heavyRoundScale : roundScale);
        if (info.part == BodyPart.Neck) force *= 0.4f;
        if (force <= 0f) return;

        // The side struck: the character's left is against its right (the hit point against the head, or else where the
        // blow came from).
        Transform head = _headBone != null ? _headBone : transform;
        Vector3 at = info.point.sqrMagnitude > 0.0001f ? info.point - head.position : -info.direction;
        bool left = Vector3.Dot(at, transform.right) < 0f;

        int impact = (int)(left ? Shape.ImpactL : Shape.ImpactR), eye = (int)(left ? Shape.EyeL : Shape.EyeR);
        _target[impact] = Mathf.Min(100f, _target[impact] + force * impactPerShare);
        _weight[impact] = _target[impact];   // caved in on the frame of the blow
        _target[eye] = Mathf.Min(100f, _target[eye] + force * swellingPerShare);
        int bruise = (int)Shape.Bruise;
        _target[bruise] = Mathf.Min(100f, _target[bruise] + force * bruisingPerShare);
        Apply();
    }

    void LateUpdate()
    {
        if (!Ready) return;
        bool moving = false;
        for (int i = 0; i < 5; i++)
        {
            if (_weight[i] >= _target[i]) continue;
            float seconds = i == (int)Shape.Bruise ? bruiseSeconds : i >= (int)Shape.EyeL ? swellSeconds : 0.05f;
            _weight[i] = Mathf.MoveTowards(_weight[i], _target[i], 100f / seconds * Time.deltaTime);
            moving = true;
        }
        if (moving) Apply();
    }

    void Apply()
    {
        for (int i = 0; i < 5; i++)
            if (_index[i] >= 0) _head.SetBlendShapeWeight(_index[i], _weight[i]);
    }
}
