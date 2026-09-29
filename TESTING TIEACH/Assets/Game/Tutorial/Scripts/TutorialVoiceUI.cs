using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// On-screen subtitle bubble shown while tutorial voice is active.
/// Edit look and position on this component in the Inspector.
/// </summary>
public class TutorialVoiceUI : MonoBehaviour
{
    public enum BubbleAnchor
    {
        BottomCenter,
        TopCenter,
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }

    [Header("Canvas")]
    [Tooltip("Parent canvas. Auto-found if empty.")]
    public Canvas targetCanvas;

    [Header("References (auto-created if empty)")]
    public GameObject bubbleRoot;
    public TextMeshProUGUI subtitleText;
    public Image bubbleBackground;
    TextMeshProUGUI speakerLabel;
    Image accentStrip;

    [Header("Position")]
    public BubbleAnchor anchor = BubbleAnchor.BottomCenter;
    [Tooltip("Distance from the anchored screen edge (X = horizontal, Y = vertical).")]
    public Vector2 screenOffset = new Vector2(0f, -112f);
    public Vector2 bubbleSize = new Vector2(720f, 104f);

    [Header("Appearance")]
    public Color backgroundColor = new Color(0.137f, 0.157f, 0.188f, 0.97f);
    public Color textColor = new Color(0.949f, 0.933f, 0.906f, 1f);
    public int fontSize = 19;
    public Vector2 textPadding = new Vector2(24f, 14f);
    public TextAlignmentOptions textAlignment = TextAlignmentOptions.Left;

    bool built;

    public bool IsVisible => bubbleRoot != null && bubbleRoot.activeSelf;

    void Awake()
    {
        EnsureUI();
        ApplyLayout();
    }

    void OnValidate()
    {
        if (!Application.isPlaying && built)
            ApplyLayout();
    }

    public void EnsureUI()
    {
        if (built && bubbleRoot != null) return;

        if (targetCanvas == null)
            targetCanvas = ResolveCanvas();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject("TutorialVoiceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;
        }

        if (bubbleRoot == null)
        {
            bubbleRoot = new GameObject("TutorialVoiceBubble", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            bubbleRoot.transform.SetParent(targetCanvas.transform, false);
            bubbleBackground = bubbleRoot.GetComponent<Image>();
            bubbleBackground.raycastTarget = false;

            var textGo = new GameObject("Subtitle", typeof(RectTransform));
            textGo.transform.SetParent(bubbleRoot.transform, false);
            subtitleText = textGo.AddComponent<TextMeshProUGUI>();
            subtitleText.textWrappingMode = TextWrappingModes.Normal;
            subtitleText.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                subtitleText.font = TMP_Settings.defaultFontAsset;

            bubbleRoot.SetActive(false);
        }

        EnsureThemeChrome();

        built = true;
        ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (bubbleRoot == null) return;

        var rt = (RectTransform)bubbleRoot.transform;
        // Subtitles always live below the two-row top HUD. Their width is based
        // on the actual canvas width so narrow and fullscreen displays retain margins.
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        float canvasWidth = GetCanvasWidth();
        float maximumWidth = Mathf.Max(240f, Mathf.Min(bubbleSize.x, canvasWidth - 40f));
        float minimumWidth = Mathf.Min(420f, maximumWidth);
        float responsiveWidth = Mathf.Clamp(canvasWidth * 0.82f, minimumWidth, maximumWidth);
        rt.sizeDelta = new Vector2(responsiveWidth, Mathf.Clamp(bubbleSize.y, 96f, 112f));
        rt.anchoredPosition = new Vector2(0f, -112f);

        if (bubbleBackground != null)
            bubbleBackground.color = backgroundColor;

        if (subtitleText != null)
        {
            subtitleText.fontSize = fontSize;
            subtitleText.enableAutoSizing = true;
            subtitleText.fontSizeMin = 14f;
            subtitleText.fontSizeMax = Mathf.Max(16f, fontSize);
            subtitleText.color = textColor;
            subtitleText.alignment = TextAlignmentOptions.TopLeft;
            subtitleText.overflowMode = TextOverflowModes.Ellipsis;
            GameUITheme.ApplyTitleScreenFont(subtitleText);

            var textRt = subtitleText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(textPadding.x, textPadding.y);
            textRt.offsetMax = new Vector2(-textPadding.x, -34f);
        }


        if (speakerLabel != null)
        {
            speakerLabel.text = "GUS";
            speakerLabel.color = GameUITheme.Accent;
            GameUITheme.ApplyTitleScreenFont(speakerLabel);
        }
        if (accentStrip != null)
            accentStrip.color = GameUITheme.Accent;
    }

    void EnsureThemeChrome()
    {
        if (bubbleRoot == null) return;
        if (bubbleBackground == null) bubbleBackground = bubbleRoot.GetComponent<Image>();

        var shadow = bubbleRoot.GetComponent<Shadow>();
        if (shadow == null) shadow = bubbleRoot.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        shadow.effectDistance = new Vector2(3f, -3f);
        shadow.useGraphicAlpha = true;

        Transform accent = bubbleRoot.transform.Find("AccentStrip");
        if (accent == null)
        {
            var go = new GameObject("AccentStrip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bubbleRoot.transform, false);
            accent = go.transform;
        }
        accentStrip = accent.GetComponent<Image>();
        accentStrip.raycastTarget = false;
        RectTransform accentRect = (RectTransform)accent;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(6f, 0f);

        Transform speaker = bubbleRoot.transform.Find("Speaker");
        if (speaker == null)
        {
            var go = new GameObject("Speaker", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(bubbleRoot.transform, false);
            speaker = go.transform;
        }
        speakerLabel = speaker.GetComponent<TextMeshProUGUI>();
        speakerLabel.fontSize = 13f;
        speakerLabel.fontStyle = FontStyles.Bold;
        speakerLabel.alignment = TextAlignmentOptions.MidlineLeft;
        speakerLabel.raycastTarget = false;
        RectTransform speakerRect = (RectTransform)speaker;
        speakerRect.anchorMin = new Vector2(0f, 1f);
        speakerRect.anchorMax = new Vector2(1f, 1f);
        speakerRect.pivot = new Vector2(0.5f, 1f);
        speakerRect.anchoredPosition = new Vector2(0f, -9f);
        speakerRect.sizeDelta = new Vector2(-48f, 22f);

        accent.SetAsFirstSibling();
        speaker.SetAsLastSibling();
    }

    float GetCanvasWidth()
    {
        if (targetCanvas == null) return Mathf.Max(420f, Screen.width);
        RectTransform canvasRect = targetCanvas.transform as RectTransform;
        if (canvasRect != null && canvasRect.rect.width > 1f)
            return canvasRect.rect.width;
        return Mathf.Max(420f, Screen.width / Mathf.Max(0.01f, targetCanvas.scaleFactor));
    }

    void ApplyAnchor(RectTransform rt)
    {
        switch (anchor)
        {
            case BubbleAnchor.TopCenter:
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                break;
            case BubbleAnchor.TopLeft:
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                break;
            case BubbleAnchor.TopRight:
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                break;
            case BubbleAnchor.BottomLeft:
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                break;
            case BubbleAnchor.BottomRight:
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                break;
            default: // BottomCenter
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                break;
        }
    }

    Canvas ResolveCanvas()
    {
        var title = FindObjectOfType<TitleScreenController>();
        if (title != null && title.inGameUIRoot != null)
        {
            var c = title.inGameUIRoot.GetComponent<Canvas>();
            if (c != null) return c;
            c = title.inGameUIRoot.GetComponentInChildren<Canvas>(true);
            if (c != null) return c;
        }

        var canvases = FindObjectsOfType<Canvas>(true);
        foreach (var canvas in canvases)
        {
            if (canvas != null && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return canvas;
        }

        return FindObjectOfType<Canvas>();
    }

    public void Show(string text)
    {
        EnsureUI();
        if (bubbleRoot == null || subtitleText == null) return;

        subtitleText.text = string.IsNullOrEmpty(text) ? "" : text;
        ApplyLayout();
        bubbleRoot.SetActive(true);
    }

    public void Hide()
    {
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);
        if (subtitleText != null)
            subtitleText.text = "";
    }

    [ContextMenu("Preview Subtitle Bubble")]
    void PreviewInEditor()
    {
        EnsureUI();
        Show("Preview: tutorial subtitles appear here.");
    }

    [ContextMenu("Hide Preview")]
    void HidePreviewInEditor()
    {
        Hide();
    }
}
