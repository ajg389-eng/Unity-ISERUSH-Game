using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResearchDisabledNoticeUI : MonoBehaviour
{
    static ResearchDisabledNoticeUI instance;

    public static void Show(Transform canvasRoot)
    {
        if (canvasRoot == null) return;
        if (instance == null)
            instance = FindFirstObjectByType<ResearchDisabledNoticeUI>(FindObjectsInactive.Include);
        if (instance == null)
        {
            var root = new GameObject("ResearchPausedNotice", typeof(RectTransform),
                typeof(Image), typeof(ResearchDisabledNoticeUI));
            instance = root.GetComponent<ResearchDisabledNoticeUI>();
        }

        if (instance.transform.parent != canvasRoot)
            instance.transform.SetParent(canvasRoot, false);
        instance.Build();
        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
    }

    void Build()
    {
        RectTransform root = transform as RectTransform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        Image overlay = GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.02f, 0.03f, 0.78f);
        overlay.raycastTarget = true;

        Transform existing = transform.Find("Panel");
        if (existing != null) return;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = panel.transform as RectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 320f);
        panel.GetComponent<Image>().color = GameUITheme.PanelSlate;

        CreateText(panel.transform, "Title", "RESEARCH PAUSED", 26f, FontStyles.Bold,
            new Vector2(32f, -70f), new Vector2(-32f, -28f), GameUITheme.Accent,
            TextAlignmentOptions.Center);
        CreateText(panel.transform, "Message",
            "Research projects are temporarily disabled. Recipes, stations, and upgrades now unlock automatically as you reach milestones.",
            18f, FontStyles.Normal, new Vector2(46f, -205f), new Vector2(-46f, -100f),
            GameUITheme.TextPrimary, TextAlignmentOptions.Center);

        var buttonObject = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(panel.transform, false);
        RectTransform buttonRect = buttonObject.transform as RectTransform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 28f);
        buttonRect.sizeDelta = new Vector2(190f, 48f);
        buttonObject.GetComponent<Image>().color = GameUITheme.Positive;
        buttonObject.GetComponent<Button>().onClick.AddListener(Close);
        TextMeshProUGUI buttonLabel = CreateText(buttonObject.transform, "Label", "CLOSE", 17f,
            FontStyles.Bold, Vector2.zero, Vector2.zero, GameUITheme.TextPrimary,
            TextAlignmentOptions.Center);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = Vector2.zero;
        buttonLabel.rectTransform.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, Vector2 min, Vector2 max, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.transform as RectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = min;
        rect.offsetMax = max;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Close()
    {
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
