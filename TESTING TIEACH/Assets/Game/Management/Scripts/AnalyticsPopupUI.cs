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
    GameObject liveAnalyticsContent;
    GameObject playerStatisticsContent;
    Button liveAnalyticsTab;
    Button playerStatisticsTab;
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
        instance.ShowPage(false);
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
        overlay.color = new Color(0.015f, 0.02f, 0.03f, 0.90f);
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
        panelImage.color = new Color(GameUITheme.Panel.r, GameUITheme.Panel.g,
            GameUITheme.Panel.b, 0.995f);
        panelImage.raycastTarget = true;

        var title = CreateText(panel.transform, "Title", "ANALYTICS", 24f, FontStyles.Bold);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.offsetMin = new Vector2(28f, -56f);
        title.rectTransform.offsetMax = new Vector2(-100f, -12f);
        title.alignment = TextAlignmentOptions.Left;

        var hint = CreateText(panel.transform, "Hint",
            "Review live production or lifetime restaurant performance",
            12f, FontStyles.Normal);
        hint.rectTransform.anchorMin = new Vector2(0f, 1f);
        hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.pivot = new Vector2(0.5f, 1f);
        hint.rectTransform.offsetMin = new Vector2(30f, -78f);
        hint.rectTransform.offsetMax = new Vector2(-100f, -58f);
        hint.alignment = TextAlignmentOptions.Left;
        hint.color = GameUITheme.TextSecondary;

        var close = CreateButton(panel.transform, "Close", "X", new Vector2(-68f, -58f), new Vector2(-18f, -14f), GameUITheme.Danger);
        close.onClick.AddListener(Close);

        BuildPageTabs();

        liveAnalyticsContent = CreatePageContent("AnalyticsContent");
        liveAnalyticsContent.AddComponent<CustomersUI>();

        playerStatisticsContent = CreatePageContent("PlayerStatisticsContent");
        playerStatisticsContent.AddComponent<Image>().color = Color.clear;
        playerStatisticsContent.AddComponent<PlayerStatisticsUI>();
        playerStatisticsContent.SetActive(false);
    }

    GameObject CreatePageContent(string name)
    {
        GameObject content = new GameObject(name, typeof(RectTransform));
        content.transform.SetParent(panel.transform, false);
        RectTransform rect = (RectTransform)content.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(18f, 18f);
        rect.offsetMax = new Vector2(-18f, -126f);
        return content;
    }

    void BuildPageTabs()
    {
        GameObject tabs = new GameObject("AnalyticsTabs", typeof(RectTransform),
            typeof(HorizontalLayoutGroup));
        tabs.transform.SetParent(panel.transform, false);
        RectTransform rect = (RectTransform)tabs.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(28f, -120f);
        rect.offsetMax = new Vector2(-100f, -84f);
        HorizontalLayoutGroup layout = tabs.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        liveAnalyticsTab = CreatePageTab(tabs.transform, "LiveAnalyticsTab", "LIVE ANALYTICS");
        playerStatisticsTab = CreatePageTab(tabs.transform, "PlayerStatisticsTab", "PLAYER STATS");
        liveAnalyticsTab.onClick.AddListener(() => ShowPage(false));
        playerStatisticsTab.onClick.AddListener(() => ShowPage(true));
    }

    Button CreatePageTab(Transform parent, string name, string label)
    {
        GameObject tabObject = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        tabObject.transform.SetParent(parent, false);
        Image image = tabObject.GetComponent<Image>();
        image.color = GameUITheme.Charcoal;
        Button button = tabObject.GetComponent<Button>();
        button.targetGraphic = image;
        GameUITheme.ApplyCompactControlEffects(button);
        LayoutElement size = tabObject.GetComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = 166f;
        size.minHeight = size.preferredHeight = 34f;

        TextMeshProUGUI text = CreateText(tabObject.transform, "Label", label, 12f,
            FontStyles.Bold);
        text.color = GameUITheme.TextPrimary;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        GameUITheme.ApplyCompactTabButton(button, label == "LIVE ANALYTICS");
        return button;
    }

    void ShowPage(bool showPlayerStatistics)
    {
        if (liveAnalyticsContent != null)
            liveAnalyticsContent.SetActive(!showPlayerStatistics);
        if (playerStatisticsContent != null)
            playerStatisticsContent.SetActive(showPlayerStatistics);
        if (liveAnalyticsTab != null)
            GameUITheme.ApplyCompactTabButton(liveAnalyticsTab, !showPlayerStatistics);
        if (playerStatisticsTab != null)
            GameUITheme.ApplyCompactTabButton(playerStatisticsTab, showPlayerStatistics);
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
