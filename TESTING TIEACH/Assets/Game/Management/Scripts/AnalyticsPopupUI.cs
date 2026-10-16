using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen analytics reference panel opened from the HUD utility actions.
/// Analytics is intentionally independent from the Business workflow.
/// </summary>
public sealed class AnalyticsPopupUI : MonoBehaviour
{
    public const string PopupObjectName = "AnalyticsPopup";

    static AnalyticsPopupUI instance;
    GameObject panel;
    public static bool IsOpen => instance != null && instance.gameObject.activeInHierarchy;

    public static void Open(Transform canvas)
    {
        if (canvas == null) return;

        if (instance == null)
        {
            Transform existing = canvas.Find(PopupObjectName);
            if (existing != null)
                instance = existing.GetComponent<AnalyticsPopupUI>();

            if (instance == null)
            {
                var root = new GameObject(PopupObjectName, typeof(RectTransform), typeof(Image), typeof(AnalyticsPopupUI));
                root.transform.SetParent(canvas, false);
                root.SetActive(false);
                instance = root.GetComponent<AnalyticsPopupUI>();
                instance.Build();
            }
        }

        instance.gameObject.SetActive(false);
        instance.transform.SetAsLastSibling();
        instance.gameObject.SetActive(true);
        OnboardingTutorial.NotifyAnalyticsScreenOpened();
        Sfx.Play(SfxId.UiOpen);
    }

    public static void CloseIfOpen()
    {
        if (instance != null && instance.gameObject.activeSelf)
            instance.Close();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    void Build()
    {
        var overlay = GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.02f, 0.03f, 0.78f);
        overlay.raycastTarget = true;

        var overlayRect = (RectTransform)transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        panel = new GameObject("AnalyticsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var panelRect = (RectTransform)panel.transform;
        HudTabColors.SizeOverlayPanel(panelRect, transform as RectTransform);
        var panelImage = panel.GetComponent<Image>();
        panelImage.color = GameUITheme.Panel;
        panelImage.raycastTarget = true;

        var title = CreateText(panel.transform, "Title", "ANALYTICS", 24f, FontStyles.Bold);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.offsetMin = new Vector2(28f, -56f);
        title.rectTransform.offsetMax = new Vector2(-100f, -12f);
        title.alignment = TextAlignmentOptions.Left;

        var hint = CreateText(panel.transform, "Hint", "Live demand, arrivals, and service performance", 12f, FontStyles.Normal);
        hint.rectTransform.anchorMin = new Vector2(0f, 1f);
        hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.pivot = new Vector2(0.5f, 1f);
        hint.rectTransform.offsetMin = new Vector2(30f, -78f);
        hint.rectTransform.offsetMax = new Vector2(-100f, -58f);
        hint.alignment = TextAlignmentOptions.Left;
        hint.color = GameUITheme.TextSecondary;

        var close = CreateButton(panel.transform, "Close", "X", new Vector2(-68f, -58f), new Vector2(-18f, -14f), GameUITheme.Danger);
        close.onClick.AddListener(Close);

        var content = new GameObject("AnalyticsContent", typeof(RectTransform));
        content.transform.SetParent(panel.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(18f, 18f);
        contentRect.offsetMax = new Vector2(-18f, -88f);
        content.AddComponent<CustomersUI>();
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = GameUITheme.TextPrimary;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    static Button CreateButton(Transform parent, string name, string label, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var image = go.GetComponent<Image>();
        image.color = color;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        GameUITheme.ApplyCompactControlEffects(button);

        var text = CreateText(go.transform, "Label", label, 18f, FontStyles.Bold);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    void Close()
    {
        gameObject.SetActive(false);
        Sfx.Play(SfxId.UiClose);
    }
}
