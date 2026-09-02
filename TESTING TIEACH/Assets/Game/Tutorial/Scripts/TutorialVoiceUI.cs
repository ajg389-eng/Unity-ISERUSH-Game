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

    [Header("Position")]
    public BubbleAnchor anchor = BubbleAnchor.BottomCenter;
    [Tooltip("Distance from the anchored screen edge (X = horizontal, Y = vertical).")]
    public Vector2 screenOffset = new Vector2(0f, 110f);
    public Vector2 bubbleSize = new Vector2(720f, 120f);

    [Header("Appearance")]
    public Color backgroundColor = new Color(0.08f, 0.1f, 0.14f, 0.92f);
    public Color textColor = new Color(0.95f, 0.97f, 1f, 1f);
    public int fontSize = 22;
    public Vector2 textPadding = new Vector2(20f, 16f);
    public TextAlignmentOptions textAlignment = TextAlignmentOptions.Center;

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
            scaler.matchWidthOrHeight = 0.5f;
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

        built = true;
        ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (bubbleRoot == null) return;

        var rt = (RectTransform)bubbleRoot.transform;
        ApplyAnchor(rt);
        rt.sizeDelta = bubbleSize;
        rt.anchoredPosition = screenOffset;

        if (bubbleBackground != null)
            bubbleBackground.color = backgroundColor;

        if (subtitleText != null)
        {
            subtitleText.fontSize = fontSize;
            subtitleText.color = textColor;
            subtitleText.alignment = textAlignment;

            var textRt = subtitleText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(textPadding.x, textPadding.y);
            textRt.offsetMax = new Vector2(-textPadding.x, -textPadding.y);
        }
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
        ApplyLayout();
        if (bubbleRoot == null || subtitleText == null) return;

        subtitleText.text = string.IsNullOrEmpty(text) ? "" : text;
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
