using UnityEngine;

/// <summary>
/// Glowing silhouette outline around a station while selected in Manage mode.
/// </summary>
public class StationSelectionHighlight : MonoBehaviour
{
    [Header("Look")]
    public Color highlightColor = new Color(0.35f, 1.1f, 0.45f, 1f);
    [Range(0.005f, 0.08f)]
    public float outlineWidth = 0.032f;

    SelectionOutlineEffect outline;
    bool selected;

    public static StationSelectionHighlight EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var highlight = go.GetComponent<StationSelectionHighlight>();
        if (highlight == null)
            highlight = go.AddComponent<StationSelectionHighlight>();
        return highlight;
    }

    public void SetSelected(bool on)
    {
        if (selected == on && outline != null)
        {
            if (on)
                ApplyLook();
            return;
        }

        selected = on;
        outline = SelectionOutlineEffect.EnsureOn(gameObject);
        if (outline == null) return;

        ApplyLook();
        outline.SetActive(on);
    }

    void ApplyLook()
    {
        if (outline == null) return;
        outline.outlineColor = highlightColor;
        outline.outlineWidth = outlineWidth;
        outline.SetColor(highlightColor);
    }

    void OnDisable()
    {
        if (selected && outline != null)
            outline.SetActive(false);
    }
}
