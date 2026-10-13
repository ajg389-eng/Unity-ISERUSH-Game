using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shared active/idle colors for MainHudTabs and panel sub-tabs
/// (Management + Inventory).
/// </summary>
public static class HudTabColors
{
    // All management/build navigation tabs use the same footprint. Keeping these
    // values here prevents scene-authored and runtime-created tabs from drifting.
    public const float SharedTabWidth = 260f;
    // All navigation rows use the same visible button height as MainHudTabs.
    // Keep this independent from each row's outer strip height.
    public const float SharedTabHeight = 40f;
    public const float SharedTabBarHeight = 60f;
    public const float SharedTabSpacing = 8f;

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

    /// <summary>
    /// Applies the shared modal panel footprint used by Recipes. Keeping this
    /// in one place prevents Research and Analytics from drifting to different
    /// fixed sizes or screen percentages.
    /// </summary>
    public static void SizeOverlayPanel(RectTransform panel, RectTransform canvasRect)
    {
        if (panel == null) return;

        float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : 682f;
        float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : 752f;
        float panelWidth = Mathf.Clamp(canvasWidth - 32f, 720f, 1040f);
        float panelHeight = Mathf.Clamp(canvasHeight - 32f, 260f, 720f);

        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(panelWidth, panelHeight);
    }

    public static void ConfigureTabGroup(Transform bar, params Button[] buttons)
    {
        if (bar == null) return;

        var layout = bar.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        var background = bar.GetComponent<Image>();
        if (background != null)
        {
            background.color = Color.clear;
            background.raycastTarget = false;
        }
        layout.padding = new RectOffset(4, 4, 6, 6);
        layout.spacing = SharedTabSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (buttons == null) return;
        foreach (var button in buttons)
        {
            if (button == null) continue;
            var size = button.GetComponent<LayoutElement>();
            if (size == null) size = button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = SharedTabWidth;
            size.preferredWidth = SharedTabWidth;
            size.flexibleWidth = 0f;
            size.minHeight = SharedTabHeight;
            size.preferredHeight = SharedTabHeight;
            size.flexibleHeight = 0f;

            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                var labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(8f, 2f);
                labelRect.offsetMax = new Vector2(-8f, -2f);
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 20f;
                label.fontSizeMin = 20f;
                label.fontSizeMax = 20f;
                label.enableAutoSizing = false;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 0.8f;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.raycastTarget = false;
            }

            Apply(button, false);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(bar as RectTransform);
    }
}
