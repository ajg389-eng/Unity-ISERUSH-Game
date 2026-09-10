using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared active/idle colors for MainHudTabs and panel sub-tabs
/// (Management + Inventory).
/// </summary>
public static class HudTabColors
{
    /// <summary>Selected tab — muted purple-gray (Management Store Stats style).</summary>
    public static readonly Color Active = new Color(0.45f, 0.45f, 0.55f, 1f);

    /// <summary>Unselected tab.</summary>
    public static readonly Color Idle = new Color(0.27f, 0.29f, 0.35f, 1f);

    /// <summary>Outer strip / tab bar chrome.</summary>
    public static readonly Color Strip = new Color(0.08f, 0.09f, 0.12f, 0.98f);

    /// <summary>Panel content background (Management ContentBox).</summary>
    public static readonly Color Panel = new Color(0.2f, 0.2f, 0.25f, 0.98f);

    public static void Apply(Button button, bool active)
    {
        if (button == null) return;
        if (button.targetGraphic is Image img)
            img.color = active ? Active : Idle;
        else
        {
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = active ? Active : Idle;
        }
    }
}
