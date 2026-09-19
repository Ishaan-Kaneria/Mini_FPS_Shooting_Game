using UnityEngine;

/// <summary>
/// Moves the texture across a run of water surfaces so the river looks like it is going
/// somewhere.
///
/// The river is the one thing in an arena that is meant to be moving and the only one
/// that costs nothing to move: it is flat, it is always the same distance from the
/// player, and the whole effect is two numbers added to a UV offset. Left still it is a
/// sheet of blue-green lying in a canyon, and a still river is a thing players read as
/// "the level did not finish loading" rather than as a lake.
///
/// Two details are load-bearing:
///
/// <list type="bullet">
/// <item>It writes through a <see cref="MaterialPropertyBlock"/> rather than touching
/// <c>Renderer.material</c>. That property clones the shared material on first access
/// and the clone is never freed, so a component doing the obvious thing leaks one
/// material per water slice per play session -- and in the editor, with domain reload
/// off, those sessions accumulate.</item>
/// <item>The block is created by a property with a null check rather than assigned in
/// <c>Awake</c>. <c>MaterialPropertyBlock</c> is not a serializable type, so Unity's
/// mid-play domain reload cannot carry it across: it comes back null while the
/// <c>Renderer[]</c> beside it survives intact, and a method guarded on the renderers
/// then hands the null one to Unity. That is the exact shape of the bug
/// <c>EnemyAI.SetFlash</c> had.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class ScrollingWater : MonoBehaviour
{
    [Tooltip("Tiles of texture per second, across and along the flow. Small: water this " +
             "wide moving visibly fast reads as a flash flood, and the point is only to " +
             "stop it reading as glass.")]
    public Vector2 scrollSpeed = new Vector2(0.012f, 0.05f);

    [Tooltip("The base map whose tiling and offset are driven. URP's Lit shader packs " +
             "both into a companion vector named after it with _ST on the end.")]
    public string mapProperty = "_BaseMap";

    /// <summary>
    /// Survives a mid-play domain reload, because an array of Object references is a
    /// serializable type. Collected on demand anyway, so a fresh instance works too.
    /// </summary>
    [SerializeField, HideInInspector] Renderer[] _surfaces;

    MaterialPropertyBlock _block;
    MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

    int _map = -1;
    int Map => _map > 0 ? _map : _map = Shader.PropertyToID(mapProperty);

    int _transform = -1;
    int Transform => _transform > 0 ? _transform : _transform = Shader.PropertyToID(mapProperty + "_ST");

    void OnEnable() => Collect();

    void Collect()
    {
        if (_surfaces != null && _surfaces.Length > 0) return;

        _surfaces = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    void LateUpdate()
    {
        if (_surfaces == null || _surfaces.Length == 0) Collect();
        if (_surfaces == null) return;

        var offset = scrollSpeed * Time.time;

        for (int i = 0; i < _surfaces.Length; i++)
        {
            var surface = _surfaces[i];
            if (surface == null) continue;

            var material = surface.sharedMaterial;

            // Asked about the map, not about the _ST vector beside it.
            //
            // HasProperty answers from the shader's declared property block, and a
            // texture's tiling/offset vector is not declared there -- it is generated
            // into the constant buffer. So HasProperty("_BaseMap_ST") is false on a
            // material that plainly has one, and a guard written that way skips every
            // renderer, every frame, with the river sitting perfectly still and nothing
            // logged. Setting it through a property block works regardless.
            if (material == null || !material.HasProperty(Map)) continue;

            // The tiling half has to be read back rather than assumed: the slices are
            // scaled to different lengths, so each one carries its own tiling, and
            // writing a flat (1,1) here would resize the texture on every one of them.
            var tiling = material.GetTextureScale(mapProperty);

            surface.GetPropertyBlock(Block);
            Block.SetVector(Transform, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            surface.SetPropertyBlock(Block);
        }
    }
}
