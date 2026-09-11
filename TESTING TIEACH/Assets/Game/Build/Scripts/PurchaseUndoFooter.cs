using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Undo control placed at the bottom of station, worker, and ingredient purchase panels.
/// </summary>
public class PurchaseUndoFooter : MonoBehaviour
{
    public const string ObjectName = "PurchaseUndoFooter";

    public Button button;
    public TextMeshProUGUI label;
    [Tooltip("When true, only undoes kitchen floor expansions.")]
    public bool floorOnly;

    public static PurchaseUndoFooter EnsureOnPanel(Transform panel)
    {
        if (panel == null) return null;

        var footer = Ensure(panel);
        if (footer == null) return null;

        var footerRt = (RectTransform)footer.transform;
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0.5f, 0f);
        footerRt.anchoredPosition = new Vector2(0f, 8f);
        footerRt.sizeDelta = new Vector2(-24f, 40f);
        footer.transform.SetAsLastSibling();
        return footer;
    }

    public static PurchaseUndoFooter EnsureMatching(RectTransform source)
    {
        if (source == null || source.parent == null) return null;

        var footer = Ensure(source.parent);
        if (footer == null) return null;

        var footerRt = (RectTransform)footer.transform;
        footerRt.anchorMin = new Vector2(source.anchorMin.x, 0f);
        footerRt.anchorMax = new Vector2(source.anchorMax.x, 0f);
        footerRt.pivot = new Vector2(0.5f, 0f);
        footerRt.anchoredPosition = new Vector2(source.anchoredPosition.x, 8f);
        footerRt.sizeDelta = new Vector2(source.sizeDelta.x, 40f);
        footer.transform.SetAsLastSibling();
        return footer;
    }

    static PurchaseUndoFooter Ensure(Transform parent)
    {
        var existing = parent.Find(ObjectName);
        if (existing != null)
        {
            var footer = existing.GetComponent<PurchaseUndoFooter>();
            if (footer == null)
                footer = existing.gameObject.AddComponent<PurchaseUndoFooter>();
            footer.Bind();
            return footer;
        }

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(PurchaseUndoFooter));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.28f, 0.32f, 0.42f, 1f);

        var created = go.GetComponent<PurchaseUndoFooter>();
        created.button = go.GetComponent<Button>();

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        created.label = labelGo.AddComponent<TextMeshProUGUI>();
        created.label.text = "Undo";
        created.label.fontSize = 16;
        created.label.fontStyle = FontStyles.Bold;
        created.label.alignment = TextAlignmentOptions.Center;
        created.label.color = Color.white;
        created.label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            created.label.font = TMP_Settings.defaultFontAsset;

        created.Bind();
        return created;
    }

    void Awake()
    {
        Bind();
    }

    void Update()
    {
        Refresh();
    }

    void Bind()
    {
        if (button == null)
            button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);

        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
            button.onClick.AddListener(OnClicked);
        }

        Refresh();
    }

    void Refresh()
    {
        var undo = PurchaseUndoManager.Instance != null ? PurchaseUndoManager.Instance : PurchaseUndoManager.Ensure();
        bool can = undo != null && (floorOnly ? undo.CanUndoFloor : undo.CanUndo);
        if (button != null)
            button.interactable = can;
        if (label != null)
        {
            if (!can)
                label.text = floorOnly ? "Undo Floor" : "Undo";
            else
                label.text = floorOnly ? undo.PeekFloorLabel : undo.PeekLabel;
        }
    }

    void OnClicked()
    {
        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
        {
            if (floorOnly)
                undo.TryUndoLastFloor();
            else
                undo.TryUndo();
        }
        Refresh();
    }
}
