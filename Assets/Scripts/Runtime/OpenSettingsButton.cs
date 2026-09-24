using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opens <see cref="SettingsPanel"/> over this button's canvas. Put on a button by the
/// builders -- the dashboard's SETTINGS and the pause menu's -- so neither builder has to
/// know how the settings are made.
/// </summary>
[RequireComponent(typeof(Button))]
public class OpenSettingsButton : MonoBehaviour
{
    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => SettingsPanel.Show(GetComponentInParent<Canvas>()));
    }
}
