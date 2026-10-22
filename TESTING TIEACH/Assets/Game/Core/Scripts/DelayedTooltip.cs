using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows a small, shared tooltip after the pointer rests over a control.
/// The tooltip is deliberately delayed so icon-only controls stay quiet while
/// the player moves across a screen.
/// </summary>
public sealed class DelayedTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    const float DefaultDelay = 0.7f;
    const float MaxWidth = 260f;
    const float EdgePadding = 10f;

    static DelayedTooltip hovered;
    static GameObject tooltipCanvasObject;
    static Canvas tooltipCanvas;
    static GameObject tooltipObject;
    static RectTransform tooltipRect;
    static RectTransform tooltipCanvasRect;
    static TextMeshProUGUI tooltipText;
    static Image tooltipBackground;
    static CanvasGroup tooltipGroup;

    string message;
    float delay = DefaultDelay;
    Coroutine showRoutine;
    bool pointerInside;

    public string HelpText => message;

    void Update()
    {
        // Runtime-built management controls can sit under nested canvases or
        // have child graphics that change which object receives the pointer
        // event. Keep a geometry-based fallback so the tooltip still works.
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform rect = transform as RectTransform;
        if (canvas == null || rect == null || !isActiveAndEnabled) return;

        Camera camera = canvas.rootCanvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera : null;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        if (inside == pointerInside) return;
        if (inside) BeginHover();
        else EndHover();
    }

    public static void Attach(Button button, string tooltip)
    {
        if (button == null) return;

        DelayedTooltip component = button.GetComponent<DelayedTooltip>();
        if (component == null) component = button.gameObject.AddComponent<DelayedTooltip>();
        component.message = tooltip ?? string.Empty;
        component.delay = DefaultDelay;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        BeginHover();
    }

    void BeginHover()
    {
        CancelPending();
        HideTooltip();
        pointerInside = true;
        hovered = this;
        if (!string.IsNullOrWhiteSpace(message))
            showRoutine = StartCoroutine(ShowAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        EndHover();
    }

    void EndHover()
    {
        CancelPending();
        pointerInside = false;
        if (hovered == this) hovered = null;
        HideTooltip();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        CancelPending();
        HideTooltip();
    }

    void OnDisable()
    {
        CancelPending();
        pointerInside = false;
        if (hovered == this) hovered = null;
        HideTooltip();
    }

    void OnDestroy()
    {
        if (hovered == this) hovered = null;
        HideTooltip();
    }

    IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(delay);
        showRoutine = null;
        if (hovered == this) ShowTooltip();
    }

    void CancelPending()
    {
        if (showRoutine == null) return;
        StopCoroutine(showRoutine);
        showRoutine = null;
    }

    void ShowTooltip()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        EnsureTooltip(canvas.rootCanvas != null ? canvas.rootCanvas : canvas);
        if (tooltipObject == null || tooltipText == null) return;

        tooltipText.text = message;
        tooltipText.textWrappingMode = TextWrappingModes.Normal;
        tooltipText.overflowMode = TextOverflowModes.Overflow;
        Vector2 preferred = tooltipText.GetPreferredValues(message, MaxWidth, 0f);
        float width = Mathf.Clamp(preferred.x + 20f, 100f, MaxWidth + 20f);
        float height = Mathf.Max(30f, preferred.y + 14f);
        tooltipRect.sizeDelta = new Vector2(width, height);
        tooltipObject.transform.SetAsLastSibling();
        tooltipObject.SetActive(true);
        PositionTooltip(canvas.rootCanvas != null ? canvas.rootCanvas : canvas);
    }

    static void EnsureTooltip(Canvas canvas)
    {
        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;
        if (tooltipObject != null && tooltipCanvasRect == canvasRect)
        {
            UpdateSortingOrder(canvas);
            return;
        }

        if (tooltipCanvasObject != null) Destroy(tooltipCanvasObject);
        tooltipCanvasObject = new GameObject("DelayedTooltipCanvas", typeof(RectTransform), typeof(Canvas));
        tooltipCanvasObject.transform.SetParent(canvas.transform, false);
        tooltipCanvasObject.transform.SetAsLastSibling();
        RectTransform overlayRect = (RectTransform)tooltipCanvasObject.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        tooltipCanvas = tooltipCanvasObject.GetComponent<Canvas>();
        tooltipCanvas.renderMode = canvas.renderMode;
        tooltipCanvas.worldCamera = canvas.worldCamera;
        tooltipCanvas.planeDistance = canvas.planeDistance;
        tooltipCanvas.overrideSorting = true;
        tooltipCanvas.sortingLayerID = canvas.sortingLayerID;
        UpdateSortingOrder(canvas);

        tooltipObject = new GameObject("DelayedTooltip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        tooltipObject.transform.SetParent(tooltipCanvasObject.transform, false);
        tooltipObject.transform.SetAsLastSibling();
        tooltipRect = (RectTransform)tooltipObject.transform;
        tooltipRect.anchorMin = new Vector2(0f, 0f);
        tooltipRect.anchorMax = new Vector2(0f, 0f);
        tooltipRect.pivot = new Vector2(0f, 0f);
        tooltipRect.anchoredPosition = Vector2.zero;

        tooltipBackground = tooltipObject.GetComponent<Image>();
        tooltipBackground.color = new Color(GameUITheme.Charcoal.r, GameUITheme.Charcoal.g,
            GameUITheme.Charcoal.b, 0.98f);
        tooltipBackground.raycastTarget = false;

        tooltipGroup = tooltipObject.GetComponent<CanvasGroup>();
        tooltipGroup.interactable = false;
        tooltipGroup.blocksRaycasts = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(tooltipObject.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);
        tooltipText = textObject.GetComponent<TextMeshProUGUI>();
        tooltipText.fontSize = 14f;
        tooltipText.color = GameUITheme.TextPrimary;
        tooltipText.alignment = TextAlignmentOptions.Center;
        tooltipText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tooltipText.font = TMP_Settings.defaultFontAsset;
        tooltipCanvasRect = canvasRect;
        tooltipObject.SetActive(false);
        GameUITheme.ApplyTo(tooltipObject.transform);
    }

    static void UpdateSortingOrder(Canvas sourceCanvas)
    {
        if (tooltipCanvas == null) return;
        int highest = sourceCanvas != null ? sourceCanvas.sortingOrder : 0;
        foreach (Canvas candidate in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate == null || candidate == tooltipCanvas || !candidate.isRootCanvas) continue;
            highest = Mathf.Max(highest, candidate.sortingOrder);
        }
        tooltipCanvas.sortingOrder = Mathf.Clamp(highest + 100, short.MinValue, short.MaxValue);
    }

    static void PositionTooltip(Canvas canvas)
    {
        if (tooltipRect == null || tooltipCanvasRect == null) return;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(tooltipCanvasRect,
            Input.mousePosition, camera, out local)) return;

        Vector2 size = tooltipRect.rect.size;
        Rect bounds = tooltipCanvasRect.rect;
        local += new Vector2(14f, -size.y - 14f);
        local.x = Mathf.Clamp(local.x, bounds.xMin + EdgePadding, bounds.xMax - size.x - EdgePadding);
        local.y = Mathf.Clamp(local.y, bounds.yMin + EdgePadding, bounds.yMax - size.y - EdgePadding);
        tooltipRect.anchoredPosition = local;
    }

    static void HideTooltip()
    {
        if (tooltipObject != null) tooltipObject.SetActive(false);
    }
}
