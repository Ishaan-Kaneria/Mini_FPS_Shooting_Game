using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player sees while a bomb is being aimed: the ring the blast will cover and
/// the dotted arc it will travel to get there.
///
/// The ring is the honest part. It is scaled to the bomb's actual damage radius, so a
/// player who puts two enemies inside it is being told the truth about what is going to
/// happen -- and the dots are sampled from the same equation
/// <see cref="BombProjectile"/> flies, so the line cannot say one thing while the bomb
/// does another.
///
/// It is a display and nothing else: it never decides where the bomb goes.
/// <see cref="BombThrower"/> works that out and hands it here.
/// </summary>
[DisallowMultipleComponent]
public class BombAimIndicator : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Scaled to the blast diameter and laid on the ground at the landing point.")]
    public Transform ring;

    [Tooltip("Cloned along the arc. Left switched off -- it is a template, not a dot.")]
    public Transform dotTemplate;

    [Tooltip("Marks the exact landing point inside the ring, so a wide blast still has " +
             "a precise centre to aim at.")]
    public Transform pip;

    [Header("Arc")]
    [Tooltip("Dots along the flight path. Enough to read as a line, few enough to read " +
             "as dots.")]
    [Range(4, 40)] public int dotCount = 16;

    [Tooltip("The first fraction of the arc is skipped, because the dots there come out " +
             "of the player's own face.")]
    [Range(0f, 0.5f)] public float arcStart = 0.12f;

    [Tooltip("Dots shrink toward the landing point, which is what gives a flat line a " +
             "direction.")]
    public float dotStartScale = 0.16f;

    public float dotEndScale = 0.07f;

    [Header("Colour")]
    [Tooltip("A throw that can be made.")]
    public Color validColor = new Color(1f, 0.72f, 0.2f);

    [Tooltip("A throw that would land somewhere the bomb cannot reach -- no ground under " +
             "the aim, or a wall in the way.")]
    public Color blockedColor = new Color(1f, 0.3f, 0.25f);

    [Tooltip("The range has been locked, so turning sweeps the ring around you at a " +
             "fixed distance instead of sliding it along the floor.")]
    public Color lockedColor = new Color(0.4f, 0.95f, 1f);

    [Tooltip("Cycles per second the ring pulses at. Movement is what stops a ring on a " +
             "busy floor being mistaken for a decal.")]
    [Min(0f)] public float pulseRate = 2.4f;

    // ======================================================================
    readonly List<Transform> _dots = new List<Transform>();
    readonly List<Renderer> _dotRenderers = new List<Renderer>();

    Renderer _ringRenderer;
    Renderer _pipRenderer;
    MaterialPropertyBlock _block;
    bool _built;

    /// <summary>
    /// Made on demand, never in Awake. A MaterialPropertyBlock is not serializable, so
    /// Unity's mid-play reload backup drops it while the Renderers beside it survive --
    /// and a guard on the renderers then passes a null block to GetPropertyBlock, which
    /// throws once per renderer per frame for the rest of the session. See the sibling
    /// trap in CLAUDE.md.
    /// </summary>
    MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

    void Awake()
    {
        Build();
        Hide();
    }

    /// <summary>
    /// Clones the dot pool once. Rebuilt rather than assumed after a scene reload,
    /// because the clones are children of this object and go with it.
    /// </summary>
    void Build()
    {
        if (_built || dotTemplate == null) return;

        _built = true;
        dotTemplate.gameObject.SetActive(false);

        for (int i = 0; i < dotCount; i++)
        {
            var dot = Instantiate(dotTemplate, transform);
            dot.name = $"ArcDot_{i}";
            dot.gameObject.SetActive(false);

            _dots.Add(dot);
            _dotRenderers.Add(dot.GetComponentInChildren<Renderer>());
        }

        _ringRenderer = ring != null ? ring.GetComponentInChildren<Renderer>() : null;
        _pipRenderer = pip != null ? pip.GetComponentInChildren<Renderer>() : null;
    }

    /// <summary>Switches the whole thing off. What not aiming looks like.</summary>
    public void Hide()
    {
        if (ring != null) ring.gameObject.SetActive(false);
        if (pip != null) pip.gameObject.SetActive(false);

        foreach (var dot in _dots)
            if (dot != null) dot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Draws one frame of the aim.
    /// </summary>
    /// <param name="from">Where the bomb will leave from.</param>
    /// <param name="landing">Where it will land.</param>
    /// <param name="velocity">The launch velocity, so the dots trace the real arc.</param>
    /// <param name="flightTime">Seconds the arc covers.</param>
    /// <param name="gravityScale">The bomb's own gravity, so the dots trace its real lob.</param>
    /// <param name="radius">Blast radius, which is what the ring is scaled to.</param>
    /// <param name="normal">Ground normal at the landing point, so the ring lies flat on a slope.</param>
    /// <param name="valid">False when the throw cannot be made.</param>
    /// <param name="rangeLocked">True when the player has pinned the distance.</param>
    public void Show(Vector3 from, Vector3 landing, Vector3 velocity, float flightTime,
                     float gravityScale, float radius, Vector3 normal, bool valid,
                     bool rangeLocked)
    {
        Build();

        Color tint = !valid ? blockedColor : rangeLocked ? lockedColor : validColor;

        // Pulsed on unscaled time, so the ring keeps breathing if the game is paused
        // with a bomb half-aimed.
        float pulse = pulseRate <= 0f
            ? 1f
            : 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * pulseRate * Mathf.PI * 2f);

        if (ring != null)
        {
            ring.gameObject.SetActive(true);

            // Lifted a hand's width so it does not z-fight with the floor it is drawn on.
            ring.position = landing + normal * 0.06f;
            ring.rotation = Quaternion.FromToRotation(Vector3.up, normal);

            // Diameter, not radius: what the player is being shown is the area damaged.
            float diameter = Mathf.Max(0.2f, radius * 2f);
            ring.localScale = new Vector3(diameter, ring.localScale.y, diameter);

            Tint(_ringRenderer, tint, pulse);
        }

        if (pip != null)
        {
            pip.gameObject.SetActive(true);
            pip.position = landing + normal * 0.1f;
            Tint(_pipRenderer, tint, 1f);
        }

        if (_dots.Count == 0) return;

        for (int i = 0; i < _dots.Count; i++)
        {
            var dot = _dots[i];
            if (dot == null) continue;

            float along = Mathf.Lerp(arcStart, 1f, (i + 1f) / (_dots.Count + 1f));

            dot.gameObject.SetActive(true);
            dot.position = BombProjectile.PointOnArc(from, velocity, along * flightTime, gravityScale);
            dot.localScale = Vector3.one * Mathf.Lerp(dotStartScale, dotEndScale, along);

            Tint(_dotRenderers[i], tint, Mathf.Lerp(0.45f, 1f, along));
        }
    }

    void Tint(Renderer target, Color tint, float strength)
    {
        if (target == null) return;

        var block = Block;
        target.GetPropertyBlock(block);

        block.SetColor("_BaseColor", tint);
        block.SetColor("_Color", tint);
        block.SetColor("_EmissionColor", tint * Mathf.Max(0f, strength) * 2.4f);

        target.SetPropertyBlock(block);
    }
}
