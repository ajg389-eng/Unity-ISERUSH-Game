using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Moves the tutorial card while preserving its automatic placement until dragged.</summary>
public sealed class TutorialPanelDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler,
    IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    const float ScreenInset = 12f;

    RectTransform panel;
    RectTransform canvasRect;
    Image handleImage;
    Vector2 pointerOffset;
    bool dragging;
    bool userPositioned;
    Color idleColor;

    public void Initialize(RectTransform panelRect, RectTransform rootRect)
    {
        panel = panelRect;
        canvasRect = rootRect;
        handleImage = GetComponent<Image>();
        if (handleImage != null)
            idleColor = handleImage.color;
    }

    public void SetAutomaticPosition(Vector2 position)
    {
        if (panel == null || userPositioned || dragging) return;
        panel.anchoredPosition = ClampToCanvas(position);
    }

    public void ResetUserPosition()
    {
        userPositioned = false;
        dragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (panel == null || canvasRect == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 pointerLocal))
            return;

        pointerOffset = panel.anchoredPosition - pointerLocal;
        dragging = true;
        userPositioned = true;
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || panel == null || canvasRect == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 pointerLocal))
            return;

        panel.anchoredPosition = ClampToCanvas(pointerLocal + pointerOffset);
        eventData.Use();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;
        eventData.Use();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (handleImage != null)
            handleImage.color = Color.Lerp(idleColor, Color.white, 0.16f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (handleImage != null)
            handleImage.color = idleColor;
    }

    Vector2 ClampToCanvas(Vector2 position)
    {
        if (panel == null || canvasRect == null) return position;

        Rect bounds = canvasRect.rect;
        Vector2 size = panel.rect.size;
        float minX = bounds.xMin + size.x * panel.pivot.x + ScreenInset;
        float maxX = bounds.xMax - size.x * (1f - panel.pivot.x) - ScreenInset;
        float minY = bounds.yMin + size.y * panel.pivot.y + ScreenInset;
        float maxY = bounds.yMax - size.y * (1f - panel.pivot.y) - ScreenInset;

        position.x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : 0f;
        position.y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : 0f;
        return position;
    }
}
