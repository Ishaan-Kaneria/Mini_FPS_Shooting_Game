using TMPro;
using UnityEngine;

/// <summary>
/// One line of the instructions: what to press, and what it does.
///
/// A row with no control is a continuation of the one above it -- the bomb needs three
/// sentences and only the first of them has a key.
/// </summary>
public class InstructionRow : MonoBehaviour
{
    public TMP_Text controlText;
    public TMP_Text saysText;

    public void Bind(string control, string says)
    {
        name = string.IsNullOrEmpty(control) ? "Instruction_cont" : $"Instruction_{control}";

        if (controlText != null)
        {
            controlText.text = control ?? "";
            controlText.color = UITheme.Hazard;
        }

        if (saysText != null)
        {
            saysText.text = says ?? "";

            // A continuation is dimmed, so the eye can still find where each control's
            // explanation starts.
            saysText.color = string.IsNullOrEmpty(control) ? UITheme.InkDim : UITheme.Ink;
        }
    }
}
