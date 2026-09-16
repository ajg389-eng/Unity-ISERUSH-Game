using UnityEngine;

/// <summary>
/// Selection/hover highlight for workers in Manage mode.
/// Uses the same rim silhouette as <see cref="StationSelectionHighlight"/>.
/// </summary>
public class WorkerHoverHighlight : MonoBehaviour
{
    [Header("Look")]
    public Color highlightColor = new Color(0.35f, 1.1f, 0.45f, 1f);
    [Range(0.005f, 0.08f)]
    public float outlineWidth = 0.032f;

    SelectionOutlineEffect outline;
    bool hovering;

    public static WorkerHoverHighlight EnsureOn(KitchenEmployee employee)
    {
        if (employee == null) return null;
        var highlight = employee.GetComponent<WorkerHoverHighlight>();
        if (highlight == null)
            highlight = employee.gameObject.AddComponent<WorkerHoverHighlight>();
        return highlight;
    }

    public void SetHovered(bool on)
    {
        if (hovering == on && outline != null)
        {
            if (on)
                ApplyLook();
            return;
        }

        hovering = on;
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
        if (hovering && outline != null)
            outline.SetActive(false);
    }
}
