using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Station management panel shown in Manage mode. Create under PlayerUI via
/// Game / Setup Station Manage Popup, or let ManagementModeController build one at runtime.
/// </summary>
public class StationManagePopup : MonoBehaviour
{
    public const string PopupObjectName = "StationManagePopup";

    public const string StationTitleName = "StationTitle";
    public const string WorkerInfoName = "WorkerInfo";
    public const string OutputInfoName = "OutputInfo";
    public const string StatusName = "Status";
    public const string ProductInfoName = "ProductInfo";
    public const string ProductListName = "ProductList";
    public const string InventoryInfoName = "InventoryInfo";
    public const string WorkerListName = "WorkerList";

    [Header("Layout")]
    public Vector2 anchor = new Vector2(1f, 0.5f);
    public Vector2 pivot = new Vector2(1f, 0.5f);
    public Vector2 anchoredPosition = new Vector2(-24f, 0f);
    public Vector2 size = new Vector2(300f, 520f);

    [Header("References")]
    public TextMeshProUGUI stationTitleText;
    public TextMeshProUGUI workerInfoText;
    public TextMeshProUGUI outputInfoText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI productInfoText;
    public TextMeshProUGUI inventoryInfoText;
    public Button assignWorkerButton;
    public Button clearWorkerButton;
    public Button assignOutputButton;
    public Button clearOutputButton;
    public Transform workerListContainer;
    public Transform productListContainer;

    void Awake()
    {
        BindReferences();
        gameObject.SetActive(false);
    }

    public void BindReferences()
    {
        stationTitleText = FindLabel(StationTitleName, stationTitleText);
        workerInfoText = FindLabel(WorkerInfoName, workerInfoText);
        outputInfoText = FindLabel(OutputInfoName, outputInfoText);
        statusText = FindLabel(StatusName, statusText);
        productInfoText = FindLabel(ProductInfoName, productInfoText);
        inventoryInfoText = FindLabel(InventoryInfoName, inventoryInfoText);

        assignWorkerButton = FindButton("Assign Worker", assignWorkerButton);
        clearWorkerButton = FindButton("Clear Worker", clearWorkerButton);
        assignOutputButton = FindButton("Assign Output", assignOutputButton);
        clearOutputButton = FindButton("Clear Output", clearOutputButton);

        workerListContainer = FindChild(WorkerListName, workerListContainer);
        productListContainer = FindChild(ProductListName, productListContainer);
    }

    public void ApplyLayout()
    {
        var rt = transform as RectTransform;
        if (rt == null) return;

        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
    }

    public void CopyReferencesTo(ManagementModeController controller)
    {
        if (controller == null) return;

        controller.stationPopup = gameObject;
        controller.stationTitleText = stationTitleText;
        controller.workerInfoText = workerInfoText;
        controller.outputInfoText = outputInfoText;
        controller.statusText = statusText;
        controller.productInfoText = productInfoText;
        controller.inventoryInfoText = inventoryInfoText;
        controller.assignWorkerButton = assignWorkerButton;
        controller.clearWorkerButton = clearWorkerButton;
        controller.assignOutputButton = assignOutputButton;
        controller.clearOutputButton = clearOutputButton;
        controller.workerListContainer = workerListContainer;
        controller.productListContainer = productListContainer;
        controller.popupAnchor = anchor;
        controller.popupPivot = pivot;
        controller.popupAnchoredPosition = anchoredPosition;
    }

    public static StationManagePopup CreateInCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        var existing = canvasTransform.Find(PopupObjectName);
        if (existing != null)
            return existing.GetComponent<StationManagePopup>() ?? existing.gameObject.AddComponent<StationManagePopup>();

        var popupGo = new GameObject(PopupObjectName, typeof(RectTransform));
        popupGo.transform.SetParent(canvasTransform, false);

        var popup = popupGo.AddComponent<StationManagePopup>();
        popup.BuildHierarchy();
        popup.BindReferences();
        popup.ApplyLayout();
        popupGo.SetActive(false);
        popupGo.transform.SetAsLastSibling();
        return popup;
    }

    void BuildHierarchy()
    {
        ApplyLayout();

        var bg = gameObject.GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.16f, 0.96f);
        bg.raycastTarget = true;

        var vlg = gameObject.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 14, 14);
        vlg.spacing = 8;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        stationTitleText = CreateLabel(transform, StationTitleName, "Station", 22);
        inventoryInfoText = CreateLabel(transform, InventoryInfoName, "Inventory\n—", 18);
        inventoryInfoText.alignment = TextAlignmentOptions.Center;
        inventoryInfoText.color = new Color(1f, 0.88f, 0.55f, 1f);
        inventoryInfoText.gameObject.SetActive(false);
        var inventoryLe = inventoryInfoText.GetComponent<LayoutElement>();
        inventoryLe.minHeight = 90;
        inventoryLe.flexibleHeight = 1;

        workerInfoText = CreateLabel(transform, WorkerInfoName, "Worker: —", 15);
        outputInfoText = CreateLabel(transform, OutputInfoName, "Output → (none)", 15);
        productInfoText = CreateLabel(transform, ProductInfoName, "Produces: —", 14);
        productInfoText.gameObject.SetActive(false);

        productListContainer = CreateListContainer(transform, ProductListName, minHeight: 40, flexibleHeight: 0);
        productListContainer.gameObject.SetActive(false);

        statusText = CreateLabel(transform, StatusName, "", 13);
        statusText.color = new Color(0.85f, 0.85f, 0.9f, 1f);
        statusText.alignment = TextAlignmentOptions.Left;

        assignWorkerButton = CreateButton(transform, "Assign Worker");
        clearWorkerButton = CreateButton(transform, "Clear Worker");
        assignOutputButton = CreateButton(transform, "Assign Output");
        clearOutputButton = CreateButton(transform, "Clear Output");

        workerListContainer = CreateListContainer(transform, WorkerListName, minHeight: 80, flexibleHeight: 1);
    }

    static Transform CreateListContainer(Transform parent, string objectName, float minHeight, float flexibleHeight)
    {
        var listGo = new GameObject(objectName, typeof(RectTransform));
        listGo.transform.SetParent(parent, false);

        var listVlg = listGo.AddComponent<VerticalLayoutGroup>();
        listVlg.spacing = 4;
        listVlg.childForceExpandWidth = true;

        var le = listGo.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleHeight = flexibleHeight;
        return listGo.transform;
    }

    TextMeshProUGUI FindLabel(string objectName, TextMeshProUGUI current)
    {
        if (current != null) return current;
        var t = transform.Find(objectName);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    Button FindButton(string objectName, Button current)
    {
        if (current != null) return current;
        var t = transform.Find(objectName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    Transform FindChild(string objectName, Transform current)
    {
        if (current != null) return current;
        var t = transform.Find(objectName);
        return t;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string objectName, string text, float size)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        go.AddComponent<LayoutElement>().minHeight = size + 8;
        return tmp;
    }

    static Button CreateButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.35f, 0.45f, 1f);
        var btn = go.AddComponent<Button>();
        go.AddComponent<LayoutElement>().minHeight = 36;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        return btn;
    }
}
