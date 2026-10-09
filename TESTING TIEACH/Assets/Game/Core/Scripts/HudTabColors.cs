using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared active/idle colors for MainHudTabs and panel sub-tabs
/// (Management + Inventory).
/// </summary>
public static class HudTabColors
{
    /// <summary>Selected tab — shared coral accent from the game UI palette.</summary>
    public static readonly Color Active = GameUITheme.Coral;

    /// <summary>Unselected tab.</summary>
    public static readonly Color Idle = GameUITheme.Surface;

    /// <summary>Outer strip / tab bar chrome.</summary>
    public static readonly Color Strip = GameUITheme.Backdrop;

    /// <summary>Panel content background (Management ContentBox).</summary>
    public static readonly Color Panel = GameUITheme.Panel;

    public static void Apply(Button button, bool active)
    {
        GameUITheme.ApplyTabButton(button, active);
    }
}
