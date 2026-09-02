using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keeps mouse-wheel scrolling on this ScrollRect instead of passing it to camera zoom
/// or other scroll views behind the pointer.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class ScrollRectWheelBlocker : MonoBehaviour, IScrollHandler
{
    public ScrollRect scrollRect;

    void Awake()
    {
        if (scrollRect == null)
            scrollRect = GetComponent<ScrollRect>();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (scrollRect == null) return;

        scrollRect.OnScroll(eventData);
        eventData.Use();
    }
}
