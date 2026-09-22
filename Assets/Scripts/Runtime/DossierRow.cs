using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One of the eight on <see cref="DossierPanel"/>: their face, their name, what they
/// held, and what happened to them.
///
/// Cloned from a hidden template per sibling at runtime, the same way
/// <see cref="AchievementRow"/> and <see cref="LevelButton"/> are, so the number of
/// names is a property of <see cref="CampaignData"/> and never of the menu scene.
/// </summary>
public class DossierRow : MonoBehaviour
{
    [Header("Parts")]
    public Image portrait;

    [Tooltip("The border round the face, so a generated still reads as a photograph " +
             "rather than as a rendering fault.")]
    public Image frame;

    public TMP_Text nameText;
    public TMP_Text holdingText;
    public TMP_Text statusText;

    [Tooltip("What the player learned when they went down. Empty until then.")]
    public TMP_Text beatText;

    [Header("Feel")]
    public Color downColor = new Color(1f, 0.27f, 0.39f);
    public Color standingColor = new Color(0.62f, 0.67f, 0.74f);

    /// <summary>
    /// Fills the row in.
    ///
    /// <b>A name the player has not met is shown, and shown blank.</b> Hiding it would
    /// make the list grow as the story went on, and a list that grows cannot tell you
    /// how much is left -- which is most of what somebody opens this screen to find
    /// out. What is withheld is who they are, not that they exist.
    /// </summary>
    public void Bind(CampaignData.Sibling sibling, bool met, bool down)
    {
        if (sibling == null) return;

        name = $"Dossier_{sibling.displayName}";

        if (nameText != null)
            nameText.text = met ? sibling.displayName.ToUpperInvariant() : "NOT YET NAMED";

        if (holdingText != null)
            holdingText.text = met && !string.IsNullOrWhiteSpace(sibling.holding)
                ? sibling.holding.ToUpperInvariant()
                : "";

        if (statusText != null)
        {
            statusText.text = down ? "DOWN" : met ? "STANDING" : "";
            statusText.color = down ? downColor : standingColor;
        }

        // The beat only after they are down: it is what the player learned from them,
        // and printing it beforehand is the story telling itself out of order.
        if (beatText != null)
            beatText.text = down ? sibling.beat ?? "" : "";

        if (portrait != null)
        {
            bool hasFace = sibling.portrait != null && met;

            if (hasFace)
                portrait.sprite = Sprite.Create(
                    sibling.portrait,
                    new Rect(0f, 0f, sibling.portrait.width, sibling.portrait.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);

            // An unmet name keeps the frame and loses the face, which is the whole of
            // what this screen is saying about them.
            portrait.enabled = hasFace;
        }

        if (frame != null)
            frame.color = down
                ? new Color(downColor.r, downColor.g, downColor.b, 0.55f)
                : new Color(standingColor.r, standingColor.g, standingColor.b, 0.35f);
    }
}
