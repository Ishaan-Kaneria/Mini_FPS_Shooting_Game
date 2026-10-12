using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a killing blow does to a body (Ishaan's call: deform and dismember).
///
/// - **Caved.** A headshot that kills crushes the skull out of shape and bloodies it.
/// - **Broken.** A limb shot to pieces before death is left broken: the joint goes slack,
///   so the ragdoll lies with the leg or arm at an angle a living one cannot hold.
/// - **Severed.** A heavy enough killing blow takes the part off: a big round through the
///   head takes the head; one through a limb takes it at the joint above the wound; a
///   blast takes one or two. The piece is cut out of the skinned meshes as they stand at
///   that moment -- every garment, the cap with the head, the boot with the shin -- and
///   becomes its own body that tumbles away; the stump pumps blood and the piece bleeds.
///
/// Generic: a part is a bone and everything under it, so a creature with another skeleton
/// is cut the same way. Off when the Blood setting is off: a body with no blood keeps its
/// shape, for the lower age rating.
///
/// Reading the meshes' bone weights needs the model imported with Read/Write on; the
/// enemy builder (FPSKitRiggedSoldier) sets it.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBrutality : MonoBehaviour
{
    [Header("When")]
    [Tooltip("A killing blow worth this share of the body's whole health takes the part it hit off.")]
    [Range(0.1f, 2f)] public float severShare = 0.55f;
    [Tooltip("A killing headshot this heavy (share of health) takes the head off rather than caving it in.")]
    [Range(0.1f, 3f)] public float decapitateShare = 0.9f;
    [Tooltip("A limb this badly wounded before death is left broken.")]
    [Range(0f, 1f)] public float breakAt = 0.6f;
    [Tooltip("Limbs a blast can take off.")]
    [Range(0, 4)] public int blastSevers = 2;
    [Tooltip("A blast that leaves the body alive takes a limb off when it was worth this share of its health.")]
    [Range(0.05f, 1f)] public float livingBlastShare = 0.3f;

    [Header("Pieces")]
    [Tooltip("Seconds a severed piece lies before it sinks away.")]
    [Range(5f, 120f)] public float pieceLife = 40f;
    [Tooltip("The most severed pieces in the world at once; past it the oldest goes.")]
    [Range(4, 64)] public int maxPieces = 16;

    [Header("Look")]
    [Tooltip("The caved skull: how the head is squashed, on its own axes.")]
    public Vector3 cavedScale = new Vector3(0.9f, 0.76f, 0.86f);
    [Tooltip("What a bloodied face and scalp are tinted with.")]
    public Color bloodied = new Color(0.5f, 0.12f, 0.1f);
    [Tooltip("Metres a second a severed piece is thrown at, along the blow.")]
    [Range(0f, 12f)] public float throwSpeed = 4.5f;
    [Tooltip("Seconds a stump keeps pumping.")]
    [Range(0f, 15f)] public float gushSeconds = 5f;

    Health _health;
    Animator _animator;
    EnemyWounds _wounds;
    EnemyGore _gore;
    bool _done;

    void Awake()
    {
        _health = GetComponent<Health>();
        _animator = GetComponentInChildren<Animator>();
        _wounds = GetComponent<EnemyWounds>();
        _gore = GetComponent<EnemyGore>();
    }

    void OnEnable()
    {
        if (_health != null) { _health.Died += OnDied; _health.Damaged += OnDamaged; }
    }

    void OnDisable()
    {
        if (_health != null) { _health.Died -= OnDied; _health.Damaged -= OnDamaged; }
    }

    // ======================================================================
    // On a living body (Stage 3): a heavy round or a blast takes a crippled limb off
    // ======================================================================

    bool _pending;
    DamageInfo _pendingBlow;

    void OnDamaged(Health h, DamageInfo info)
    {
        if (!info.heavy || info.amount <= 0f || _health.IsDead || !BloodFX.Enabled) return;
        _pending = true;
        _pendingBlow = info;
    }

    /// <summary>
    /// After the wounds have booked the hit and the body is in this frame's pose: a heavy round on
    /// a limb it has crippled takes the limb off at the joint above the wound; a heavy enough blast
    /// takes a forearm or a shin. The body then lives on with it: crawling for a leg, one-armed (the
    /// pistol, or the rifle dropped with both) for an arm (EnemyWounds). Never a boss.
    /// </summary>
    void LateUpdate()
    {
        if (!_pending) return;
        _pending = false;
        var blow = _pendingBlow;
        if (_health.IsDead || _done || _wounds == null || _animator == null || !_animator.isHuman) return;
        var ai = GetComponent<EnemyAI>();
        if (ai != null && ai.archetype != null && ai.archetype.role == EnemyArchetype.Role.Boss) return;

        HumanBodyBones bone;
        BodyPart limb;
        if (blow.fromBlast)
        {
            float share = blow.amount / Mathf.Max(1f, _health.maxHealth + _health.maxShield);
            if (share < livingBlastShare) return;
            var options = new[] { (HumanBodyBones.LeftLowerLeg, BodyPart.LeftLeg), (HumanBodyBones.RightLowerLeg, BodyPart.RightLeg),
                                  (HumanBodyBones.LeftLowerArm, BodyPart.LeftArm), (HumanBodyBones.RightLowerArm, BodyPart.RightArm) };
            var pick = options[Random.Range(0, options.Length)];
            bone = pick.Item1; limb = pick.Item2;
        }
        else
        {
            limb = BodyParts.Limb(blow.part);
            bone = SeverBoneFor(blow.part);
            if (bone == HumanBodyBones.LastBone || bone == HumanBodyBones.Head) return;
            if (_wounds.State(limb) < EnemyWounds.LimbState.Crippled) return;
        }
        if (_wounds.State(limb) == EnemyWounds.LimbState.Severed) return;

        Sever(bone, blow);
        _wounds.Sever(limb);
    }

    void OnDied(Health h)
    {
        if (_done || !BloodFX.Enabled || _animator == null || !_animator.isHuman) return;
        _done = true;
        StartCoroutine(Brutalise(h.LastDamage));
    }

    /// <summary>One physics step later, so the ragdoll has taken the bones and the cut is made in the pose it fell in.</summary>
    IEnumerator Brutalise(DamageInfo blow)
    {
        // A body that dies by a clip first (RagdollController.Dying) is cut once the ragdoll has it.
        var ragdoll = GetComponent<RagdollController>();
        float waited = 0f;
        while (ragdoll != null && ragdoll.Dying && waited < 1.5f) { waited += Time.deltaTime; yield return null; }
        yield return new WaitForFixedUpdate();

        float share = blow.amount / Mathf.Max(1f, _health.maxHealth + _health.maxShield);
        var part = blow.part;

        // Taking a part off is a heavy blow's (a heavy round or a blast); anything else caves and breaks.
        if (blow.isHeadshot || part == BodyPart.Head)
        {
            if (blow.heavy && share >= decapitateShare) Sever(HumanBodyBones.Head, blow);
            else CaveHead();
        }
        else if (blow.fromBlast)
        {
            var limbs = new List<HumanBodyBones>
            {
                HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperLeg,
            };
            int count = Mathf.Min(blastSevers, Mathf.Clamp(Mathf.FloorToInt(share * 3f), 1, blastSevers));
            for (int i = 0; i < count && limbs.Count > 0; i++)
            {
                int pick = Random.Range(0, limbs.Count);
                Sever(limbs[pick], blow);
                limbs.RemoveAt(pick);
            }
        }
        else if (blow.heavy && share >= severShare && !blow.fromMelee)
        {
            var bone = SeverBoneFor(part);
            if (bone != HumanBodyBones.LastBone) Sever(bone, blow);
        }

        // Whatever was shot up before the end stays broken.
        BreakIfWrecked(BodyPart.LeftLeg, HumanBodyBones.LeftLowerLeg);
        BreakIfWrecked(BodyPart.RightLeg, HumanBodyBones.RightLowerLeg);
        BreakIfWrecked(BodyPart.LeftArm, HumanBodyBones.LeftLowerArm);
        BreakIfWrecked(BodyPart.RightArm, HumanBodyBones.RightLowerArm);
    }

    /// <summary>The joint a hit on a part takes the limb off at: the one above the wound.</summary>
    static HumanBodyBones SeverBoneFor(BodyPart part) => part switch
    {
        BodyPart.LeftForearm => HumanBodyBones.LeftLowerArm,
        BodyPart.RightForearm => HumanBodyBones.RightLowerArm,
        BodyPart.LeftArm => HumanBodyBones.LeftUpperArm,
        BodyPart.RightArm => HumanBodyBones.RightUpperArm,
        BodyPart.LeftShin => HumanBodyBones.LeftLowerLeg,
        BodyPart.RightShin => HumanBodyBones.RightLowerLeg,
        BodyPart.LeftLeg => HumanBodyBones.LeftUpperLeg,
        BodyPart.RightLeg => HumanBodyBones.RightUpperLeg,
        BodyPart.Neck => HumanBodyBones.Head,
        _ => HumanBodyBones.LastBone,
    };

    // ======================================================================
    // Caved and broken
    // ======================================================================

    void CaveHead()
    {
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;

        head.localScale = Vector3.Scale(head.localScale, cavedScale);
        head.localRotation *= Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(-8f, 8f), Random.Range(-15f, 15f));

        // Blood over the face and the scalp: the head's own renderers, darkened red.
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!r.name.Contains("Head") && !r.name.Contains("Cap")) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", bloodied);
            r.SetPropertyBlock(block);
        }

        if (_gore != null && _gore.library != null)
            BloodFX.Pool(_gore.library, head.position, 0.9f, 40f);
    }

    /// <summary>
    /// A wrecked limb's joint goes slack: the ragdoll lets it fold any way, and it is given
    /// a twist to start it off, so it lies the way a broken bone lies.
    /// </summary>
    void BreakIfWrecked(BodyPart limb, HumanBodyBones bone)
    {
        if (_wounds == null || _wounds.Severity(limb) < breakAt) return;

        var t = _animator.GetBoneTransform(bone);
        if (t == null || t.localScale.x < 0.01f) return;

        var joint = t.GetComponent<CharacterJoint>();
        if (joint != null)
        {
            // Loose, but inside what a CharacterJoint solves steadily: near 180 degrees it
            // flips between limits every step and the limb buzzes.
            joint.lowTwistLimit = new SoftJointLimit { limit = -100f };
            joint.highTwistLimit = new SoftJointLimit { limit = 100f };
            joint.swing1Limit = new SoftJointLimit { limit = 100f };
            joint.swing2Limit = new SoftJointLimit { limit = 90f };
        }

        var body = t.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
            body.angularVelocity += Random.onUnitSphere * 5f;
    }

    // ======================================================================
    // Severed
    // ======================================================================

    /// <summary>
    /// Cuts the bone and everything under it out of every skinned mesh as it stands, makes
    /// the cut-out its own falling body, and collapses the part on the corpse into the joint.
    /// </summary>
    public void Sever(HumanBodyBones boneId, DamageInfo blow)
    {
        var root = _animator.GetBoneTransform(boneId);
        if (root == null || root.localScale.x < 0.01f) return;

        var cut = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));

        var piece = new GameObject("Severed_" + boneId);
        piece.layer = gameObject.layer;
        piece.transform.SetPositionAndRotation(root.position, root.rotation);

        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;

            var part = CutOut(smr, cut, piece.transform);
            if (part == null) continue;

            var r = part.GetComponent<Renderer>();
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!any) { Destroy(piece); return; }

        // The piece: a solid body thrown along the blow, tagged as flesh for the impacts.
        var box = piece.AddComponent<BoxCollider>();
        box.center = piece.transform.InverseTransformPoint(bounds.center);
        box.size = piece.transform.InverseTransformVector(bounds.size);
        box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));

        var body = piece.AddComponent<Rigidbody>();
        body.mass = boneId == HumanBodyBones.Head ? 4.5f : 3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Vector3 along = blow.direction.sqrMagnitude > 0.001f ? blow.direction.normalized : -transform.forward;
        body.linearVelocity = (along + Vector3.up * 0.6f) * throwSpeed * Random.Range(0.7f, 1.1f);
        body.angularVelocity = Random.onUnitSphere * 8f;
        try { piece.tag = "Flesh"; } catch (UnityException) { }
        SeveredPiece.Register(piece, pieceLife, maxPieces);

        // The part on the corpse folds into the joint it came off at, and stops being a body.
        // The joint goes first: a frozen bone still jointed to a moving body tugs it back
        // every step, and the corpse shivers where the limb was.
        foreach (var t in cut)
        {
            foreach (var j in t.GetComponents<Joint>()) Destroy(j);
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
            foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
        }
        // The piece starts inside the body it came off: it must not fight its way out of it.
        foreach (var c in GetComponentsInChildren<Collider>())
            if (c != null && c.enabled) Physics.IgnoreCollision(box, c);
        root.localScale = Vector3.one * 0.001f;

        // A raw end over the cut, on the body and on the piece, so what shows is a wound and
        // not the skin stretched to a point.
        Cap(root.parent != null ? root.parent : root, root.position, along);
        Cap(piece.transform, root.position, -along);

        // Blood: a burst at the cut, then the stump pumps, and the piece bleeds too.
        if (_gore != null && _gore.library != null)
        {
            BloodFX.Hit(_gore.library, root.position, along, along, 1f);
            StartCoroutine(Gush(root.parent != null ? root.parent : root, root.position - (root.parent != null ? root.parent.position : root.position)));
            StartCoroutine(Gush(piece.transform, Vector3.zero));
        }
    }

    /// <summary>
    /// The triangles of one skinned mesh whose corners are carried mostly by the cut bones,
    /// as they stand this frame, as a plain mesh under <paramref name="parent"/>.
    /// </summary>
    static GameObject CutOut(SkinnedMeshRenderer smr, HashSet<Transform> cut, Transform parent)
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        var weights = mesh.boneWeights;
        if (weights == null || weights.Length != mesh.vertexCount) return null;

        var inCut = new bool[bones.Length];
        bool anyBone = false;
        for (int i = 0; i < bones.Length; i++)
        {
            inCut[i] = bones[i] != null && cut.Contains(bones[i]);
            anyBone |= inCut[i];
        }
        if (!anyBone) return null;

        var mine = new bool[weights.Length];
        int count = 0;
        for (int v = 0; v < weights.Length; v++)
        {
            var w = weights[v];
            float carried = (inCut[w.boneIndex0] ? w.weight0 : 0f) + (inCut[w.boneIndex1] ? w.weight1 : 0f)
                          + (inCut[w.boneIndex2] ? w.weight2 : 0f) + (inCut[w.boneIndex3] ? w.weight3 : 0f);
            mine[v] = carried > 0.5f;
            if (mine[v]) count++;
        }
        if (count < 3) return null;

        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var normals = baked.normals;
        var uvs = baked.uv;

        var map = new Dictionary<int, int>();
        var newVerts = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newUvs = new List<Vector2>();
        var subTris = new List<int[]>();

        // Into the piece's own space, so it can be moved as one object.
        var toPiece = parent.worldToLocalMatrix * smr.transform.localToWorldMatrix;

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var tris = mesh.GetTriangles(sub);
            var kept = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (!mine[a] || !mine[b] || !mine[c]) continue;
                kept.Add(Remap(a)); kept.Add(Remap(b)); kept.Add(Remap(c));
            }
            subTris.Add(kept.ToArray());
        }

        int Remap(int v)
        {
            if (map.TryGetValue(v, out int n)) return n;
            n = newVerts.Count;
            map[v] = n;
            // BakeMesh with scale gives vertices in the renderer's space with its scale undone; back to world, then in.
            newVerts.Add(toPiece.MultiplyPoint3x4(Vector3.Scale(verts[v], Inverse(smr.transform.lossyScale))));
            newNormals.Add(toPiece.MultiplyVector(v < normals.Length ? normals[v] : Vector3.up).normalized);
            newUvs.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
            return n;
        }

        Object.Destroy(baked);
        if (newVerts.Count < 3) return null;

        var result = new Mesh { name = smr.name + "_Severed" };
        if (newVerts.Count > 65000) result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        result.SetVertices(newVerts);
        result.SetNormals(newNormals);
        result.SetUVs(0, newUvs);
        result.subMeshCount = subTris.Count;
        for (int s = 0; s < subTris.Count; s++) result.SetTriangles(subTris[s], s);
        result.RecalculateBounds();

        var go = new GameObject(smr.name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = result;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = smr.sharedMaterials;

        // The same tint the archetype put on the body, so the piece matches what it came off.
        var block = new MaterialPropertyBlock();
        smr.GetPropertyBlock(block);
        renderer.SetPropertyBlock(block);
        return go;
    }

    void Cap(Transform parent, Vector3 at, Vector3 facing)
    {
        if (_gore == null || _gore.library == null || _gore.library.woundMaterial == null) return;
        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(cap.GetComponent<Collider>());
        cap.name = "Stump";
        cap.transform.position = at;
        cap.transform.localScale = new Vector3(0.1f, 0.06f, 0.1f) * transform.lossyScale.x;
        cap.transform.rotation = Quaternion.FromToRotation(Vector3.up, facing.sqrMagnitude > 0.001f ? facing : Vector3.up);
        cap.transform.SetParent(parent, true);
        var r = cap.GetComponent<Renderer>();
        r.sharedMaterial = _gore.library.woundMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Vector3 Inverse(Vector3 s) => new Vector3(1f / Mathf.Max(1e-5f, s.x), 1f / Mathf.Max(1e-5f, s.y), 1f / Mathf.Max(1e-5f, s.z));

    /// <summary>A stump or a cut piece pumping: spurts that weaken, then drips.</summary>
    IEnumerator Gush(Transform at, Vector3 localOffset)
    {
        if (_gore == null || _gore.library == null || gushSeconds <= 0f) yield break;

        float end = Time.time + gushSeconds;
        float strength = 1f;
        while (Time.time < end && at != null)
        {
            Vector3 point = at.position + localOffset;
            Vector3 dir = (localOffset.sqrMagnitude > 1e-6f ? localOffset.normalized : at.up) + Random.insideUnitSphere * 0.3f;
            BloodFX.Hit(_gore.library, point, dir, dir, 0.35f * strength);
            if (Random.value < 0.3f) BloodFX.Drip(point, 0.15f * strength);

            strength = Mathf.Max(0.2f, strength * 0.85f);
            yield return new WaitForSeconds(Random.Range(0.35f, 0.6f));
        }

        if (at != null) BloodFX.Pool(_gore.library, at.position + localOffset, 0.5f, 30f);
    }
}
