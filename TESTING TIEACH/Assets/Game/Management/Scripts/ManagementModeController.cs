using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management mode: click a station in the world; a UI panel on the right
/// lets you assign workers and outputs.
/// </summary>
public class ManagementModeController : MonoBehaviour
{
    public static ManagementModeController Instance { get; private set; }

    public GameModeManager modeManager;
    public LayerMask clickLayer = -1;

    [Header("UI")]
    [Tooltip("Canvas that shows the station panel. Defaults to PlayerUI.")]
    public Canvas playerUICanvas;

    [Header("Optional UI roots (created at runtime if null)")]
    public GameObject stationPopup;
    public TextMeshProUGUI stationTitleText;
    public TextMeshProUGUI workerInfoText;
    public TextMeshProUGUI outputInfoText;
    public TextMeshProUGUI statusText;
    public Button assignWorkerButton;
    public Button clearWorkerButton;
    public Button assignOutputButton;
    public Button clearOutputButton;
    public Transform workerListContainer;
    public TextMeshProUGUI productInfoText;
    public Transform productListContainer;
    public TextMeshProUGUI inventoryInfoText;

    enum PendingAction { None, PickOutput, PickWorker }
    PendingAction pending;
    StationNode selectedStation;

    void Awake()
    {
        Instance = this;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsurePopup();
        HidePopup();
        EnsureLinkVisuals();
    }

    void EnsureLinkVisuals()
    {
        if (StationOutputLinkVisuals.Instance != null) return;
        var go = new GameObject("StationOutputLinkVisuals");
        var visuals = go.AddComponent<StationOutputLinkVisuals>();
        visuals.modeManager = modeManager;
    }

    public bool IsManageMode =>
        modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;

    void Update()
    {
        if (!IsManageMode)
        {
            if (selectedStation != null || pending != PendingAction.None)
                CancelAndHide();
            return;
        }

        // Keep heat lamp inventory readout live while selected
        if (selectedStation != null
            && selectedStation.GetComponent<HeatLampStation>() != null
            && stationPopup != null
            && stationPopup.activeSelf)
        {
            RefreshHeatLampInventory();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelAndHide();
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;
        if (Camera.main == null) return;
        if (IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            if (pending == PendingAction.None)
                HidePopup();
            return;
        }

        var node = StationNode.FindFromCollider(hit.collider);
        if (node == null)
        {
            if (pending == PendingAction.None)
                HidePopup();
            return;
        }

        if (pending == PendingAction.PickOutput)
        {
            if (selectedStation != null && node.gameObject != selectedStation.gameObject)
            {
                selectedStation.SetOutput(node.gameObject);
                Sfx.Play(SfxId.AssignOutput);
                pending = PendingAction.None;
                RefreshPopup();
                StationOutputLinkVisuals.NotifyLinksChanged();
                SetStatus($"Output set: {selectedStation.DisplayName} → {node.DisplayName}");
            }
            return;
        }

        SelectStation(node);
    }

    void SelectStation(StationNode node)
    {
        Sfx.Play(SfxId.StationSelect);
        selectedStation = node;
        pending = PendingAction.None;
        EnsurePopup();
        if (stationPopup != null)
            stationPopup.SetActive(true);
        RefreshPopup();

        var t = node != null ? node.StationType : null;
        if (node != null && node.GetComponent<HeatLampStation>() != null)
            SetStatus("Holding finished food for the cashier to serve.");
        else if (t == StationType.Register)
            SetStatus("Assign a cashier to serve customers.");
        else if (t.HasValue)
        {
            bool hasOut = node.HasOutput;
            SetStatus(hasOut
                ? $"Output → {StationNode.EnsureOn(node.outputTarget)?.DisplayName}. Worker will deliver here after using this station."
                : "Required: Assign Output — worker will only deliver to the station you set.");
        }
        else
            SetStatus("Assign a worker or set this station's output.");
    }

    void RefreshPopup()
    {
        if (selectedStation == null) return;

        if (stationTitleText != null)
            stationTitleText.text = selectedStation.DisplayName;

        bool isHeatLamp = selectedStation.GetComponent<HeatLampStation>() != null;

        if (workerInfoText != null)
        {
            workerInfoText.gameObject.SetActive(!isHeatLamp);
            if (!isHeatLamp)
            {
                if (selectedStation.assignedWorker != null)
                    workerInfoText.text = "Worker: " + (selectedStation.assignedWorker.employeeName ?? "Worker");
                else
                    workerInfoText.text = "Worker: Unassigned";
            }
        }

        if (outputInfoText != null)
        {
            outputInfoText.gameObject.SetActive(!isHeatLamp);
            if (!isHeatLamp)
            {
                if (selectedStation.outputTarget != null)
                {
                    var outNode = StationNode.EnsureOn(selectedStation.outputTarget);
                    outputInfoText.text = "Output → " + (outNode != null ? outNode.DisplayName : selectedStation.outputTarget.name);
                }
                else
                    outputInfoText.text = "Output → (none)";
            }
        }

        if (assignWorkerButton != null) assignWorkerButton.gameObject.SetActive(!isHeatLamp);
        if (clearWorkerButton != null) clearWorkerButton.gameObject.SetActive(!isHeatLamp);
        if (assignOutputButton != null) assignOutputButton.gameObject.SetActive(!isHeatLamp);
        if (clearOutputButton != null) clearOutputButton.gameObject.SetActive(!isHeatLamp);

        if (workerListContainer != null)
            workerListContainer.gameObject.SetActive(!isHeatLamp);

        ApplyPanelLayout(isHeatLamp);
        RefreshProductSection();
        RefreshHeatLampInventory();
        if (!isHeatLamp)
            RebuildWorkerList(false);
    }

    void ApplyPanelLayout(bool heatLampCompact)
    {
        if (stationPopup == null) return;

        var rt = (RectTransform)stationPopup.transform;
        var vlg = stationPopup.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.padding = heatLampCompact
                ? new RectOffset(14, 14, 8, 12)
                : new RectOffset(14, 14, 14, 14);
            vlg.spacing = heatLampCompact ? 6 : 8;
            vlg.childAlignment = TextAnchor.UpperCenter;
        }

        rt.sizeDelta = heatLampCompact
            ? new Vector2(300f, 280f)
            : new Vector2(300f, 520f);

        if (stationTitleText != null)
        {
            stationTitleText.fontSize = heatLampCompact ? 24 : 22;
            var titleLe = stationTitleText.GetComponent<LayoutElement>();
            if (titleLe != null)
            {
                titleLe.minHeight = heatLampCompact ? 26 : 30;
                titleLe.preferredHeight = titleLe.minHeight;
                titleLe.flexibleHeight = 0;
            }
        }

        if (statusText != null)
        {
            statusText.fontSize = heatLampCompact ? 15 : 13;
            statusText.alignment = heatLampCompact ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            var statusLe = statusText.GetComponent<LayoutElement>();
            if (statusLe != null)
            {
                statusLe.minHeight = heatLampCompact ? 36 : 21;
                statusLe.flexibleHeight = 0;
            }
        }

        if (workerListContainer != null)
        {
            var listLe = workerListContainer.GetComponent<LayoutElement>();
            if (listLe != null)
            {
                listLe.flexibleHeight = heatLampCompact ? 0 : 1;
                listLe.minHeight = heatLampCompact ? 0 : 80;
            }
        }

        if (inventoryInfoText != null && !heatLampCompact)
        {
            var invLe = inventoryInfoText.GetComponent<LayoutElement>();
            if (invLe != null)
            {
                invLe.flexibleHeight = 0;
                invLe.minHeight = 0;
            }
        }
    }

    void RefreshHeatLampInventory()
    {
        EnsureInventoryUI();
        if (inventoryInfoText == null) return;

        var lamp = selectedStation != null ? selectedStation.GetComponent<HeatLampStation>() : null;
        bool show = lamp != null;
        inventoryInfoText.gameObject.SetActive(show);
        if (!show) return;

        inventoryInfoText.fontSize = 16;
        inventoryInfoText.alignment = TextAlignmentOptions.Left;
        inventoryInfoText.text = "Inventory\n" + lamp.GetManagePanelText();

        var le = inventoryInfoText.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = 140;
            le.flexibleHeight = 1;
        }

        // Keep inventory under the title for heat lamp
        inventoryInfoText.transform.SetSiblingIndex(1);
        if (statusText != null)
            statusText.transform.SetSiblingIndex(2);
    }

    void EnsureInventoryUI()
    {
        if (stationPopup == null) return;
        if (inventoryInfoText != null) return;

        inventoryInfoText = CreateLabel(stationPopup.transform, "Inventory\n—", 18);
        inventoryInfoText.gameObject.name = "InventoryInfo";
        inventoryInfoText.alignment = TextAlignmentOptions.Center;
        inventoryInfoText.color = new Color(1f, 0.88f, 0.55f, 1f);
        var le = inventoryInfoText.GetComponent<LayoutElement>();
        le.minHeight = 90;
        le.flexibleHeight = 1;
        inventoryInfoText.gameObject.SetActive(false);
    }

    void RefreshProductSection()
    {
        EnsureProductUI();
        if (productInfoText == null || productListContainer == null) return;

        var grill = selectedStation != null ? selectedStation.GetComponent<GrillStation>() : null;
        var assembly = selectedStation != null ? selectedStation.GetComponent<AssemblyStation>() : null;
        bool show = grill != null || assembly != null;

        productInfoText.gameObject.SetActive(show);
        productListContainer.gameObject.SetActive(show);
        if (!show) return;

        ItemDefinition current = grill != null ? grill.selectedProduct : assembly.selectedProduct;
        string currentName = current != null
            ? (!string.IsNullOrEmpty(current.itemName) ? current.itemName : current.name)
            : "(none — pick below)";
        productInfoText.text = "Produces: " + currentName;

        for (int i = productListContainer.childCount - 1; i >= 0; i--)
            Destroy(productListContainer.GetChild(i).gameObject);

        var pm = ProductionManager.Instance;
        var config = pm != null ? pm.orderConfig : null;
        if (config == null) return;

        IEnumerable<ItemDefinition> options = grill != null
            ? config.GetGrillProducts()
            : config.GetAssemblyProducts();

        foreach (var item in options)
        {
            if (item == null) continue;
            string label = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            bool selected = current == item;
            var btn = CreateProductButton(label + (selected ? " ✓" : ""), true);
            var captured = item;
            btn.onClick.AddListener(() => SetSelectedProduct(captured));
        }
    }

    void SetSelectedProduct(ItemDefinition item)
    {
        if (selectedStation == null || item == null) return;
        var grill = selectedStation.GetComponent<GrillStation>();
        if (grill != null)
        {
            grill.selectedProduct = item;
            RefreshPopup();
            SetStatus("Grill set to produce " + (item.itemName ?? item.name));
            return;
        }
        var assembly = selectedStation.GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            assembly.selectedProduct = item;
            RefreshPopup();
            SetStatus("Assembly set to produce " + (item.itemName ?? item.name));
        }
    }

    void EnsureProductUI()
    {
        if (stationPopup == null) return;
        if (productInfoText != null && productListContainer != null) return;

        productInfoText = CreateLabel(stationPopup.transform, "Produces: —", 14);
        productInfoText.gameObject.name = "ProductInfo";
        productInfoText.transform.SetSiblingIndex(3);

        var listGo = new GameObject("ProductList", typeof(RectTransform));
        listGo.transform.SetParent(stationPopup.transform, false);
        listGo.transform.SetSiblingIndex(4);
        productListContainer = listGo.transform;
        var listVlg = listGo.AddComponent<VerticalLayoutGroup>();
        listVlg.spacing = 4;
        listVlg.childForceExpandWidth = true;
        var le = listGo.AddComponent<LayoutElement>();
        le.minHeight = 40;
        le.flexibleHeight = 0;
    }

    Button CreateProductButton(string label, bool interactable)
    {
        var go = new GameObject("ProductPick", typeof(RectTransform));
        go.transform.SetParent(productListContainer, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.28f, 0.4f, 0.32f, 1f);
        var btn = go.AddComponent<Button>();
        btn.interactable = interactable;
        go.AddComponent<LayoutElement>().minHeight = 28;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13;
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

    void RebuildWorkerList(bool showPicker)
    {
        if (workerListContainer == null) return;

        for (int i = workerListContainer.childCount - 1; i >= 0; i--)
            Destroy(workerListContainer.GetChild(i).gameObject);

        if (!showPicker) return;

        var pm = ProductionManager.Instance;
        if (pm == null || pm.employees == null) return;

        foreach (var emp in pm.employees)
        {
            if (emp == null) continue;
            bool full = emp.OperatedStationCount >= KitchenEmployee.MaxStations
                        && (selectedStation == null || !emp.IsAssignedTo(selectedStation.gameObject));
            var btn = CreateListButton(
                emp.employeeName + " (" + emp.OperatedStationCount + "/" + KitchenEmployee.MaxStations + ")",
                !full || emp.IsAssignedTo(selectedStation.gameObject));
            var captured = emp;
            btn.onClick.AddListener(() => AssignWorkerToSelected(captured));
        }
    }

    void AssignWorkerToSelected(KitchenEmployee emp)
    {
        if (selectedStation == null || emp == null) return;
        if (!selectedStation.IsWorkStation)
        {
            SetStatus("Only kitchen/register stations can have workers.");
            return;
        }

        if (!emp.IsAssignedTo(selectedStation.gameObject) && emp.OperatedStationCount >= KitchenEmployee.MaxStations)
        {
            SetStatus("That worker already has " + KitchenEmployee.MaxStations + " stations.");
            return;
        }

        selectedStation.SetWorker(emp);
        Sfx.Play(SfxId.AssignWorker);
        pending = PendingAction.None;
        RefreshPopup();
        SetStatus(emp.employeeName + " assigned to " + selectedStation.DisplayName);
    }

    public void OnAssignWorkerClicked()
    {
        if (selectedStation == null) return;
        if (!selectedStation.IsWorkStation)
        {
            SetStatus("Only kitchen/register stations can be assigned a worker.");
            return;
        }
        pending = PendingAction.PickWorker;
        RebuildWorkerList(true);
        SetStatus("Pick a hired worker from the list.");
    }

    public void OnClearWorkerClicked()
    {
        if (selectedStation == null) return;
        selectedStation.ClearWorker();
        Sfx.Play(SfxId.ClearWorker);
        pending = PendingAction.None;
        RefreshPopup();
        SetStatus("Worker cleared.");
    }

    public void OnAssignOutputClicked()
    {
        if (selectedStation == null) return;
        pending = PendingAction.PickOutput;
        RebuildWorkerList(false);
        SetStatus("Click another station to set as output.");
    }

    public void OnClearOutputClicked()
    {
        if (selectedStation == null) return;
        selectedStation.ClearOutput();
        Sfx.Play(SfxId.ClearOutput);
        pending = PendingAction.None;
        RefreshPopup();
        SetStatus("Output cleared.");
    }

    void CancelAndHide()
    {
        pending = PendingAction.None;
        selectedStation = null;
        HidePopup();
    }

    void HidePopup()
    {
        if (stationPopup != null) stationPopup.SetActive(false);
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    static bool IsPointerOverUI()
    {
        return UnityEngine.EventSystems.EventSystem.current != null &&
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    void EnsurePopup()
    {
        if (stationPopup != null)
        {
            // If an old world-space popup exists from a previous version, rebuild as screen UI
            var ownCanvas = stationPopup.GetComponent<Canvas>();
            if (ownCanvas != null && ownCanvas.renderMode == RenderMode.WorldSpace)
            {
                Destroy(stationPopup);
                stationPopup = null;
            }
            else
            {
                // Keep it under PlayerUI if it somehow isn't
                var parentCanvas = ResolvePlayerUICanvas();
                if (parentCanvas != null && stationPopup.transform.parent != parentCanvas.transform)
                    stationPopup.transform.SetParent(parentCanvas.transform, false);
                return;
            }
        }

        var parent = ResolvePlayerUICanvas();
        if (parent == null)
        {
            Debug.LogWarning("ManagementModeController: could not find PlayerUI canvas.", this);
            return;
        }

        stationPopup = new GameObject("StationManagePopup", typeof(RectTransform));
        stationPopup.transform.SetParent(parent.transform, false);

        var rt = (RectTransform)stationPopup.transform;
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-24f, 0f);
        rt.sizeDelta = new Vector2(300f, 520f);

        var bg = stationPopup.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.16f, 0.96f);
        bg.raycastTarget = true;

        var vlg = stationPopup.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 14, 14);
        vlg.spacing = 8;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        stationTitleText = CreateLabel(stationPopup.transform, "Station", 22);
        workerInfoText = CreateLabel(stationPopup.transform, "Worker: —", 15);
        outputInfoText = CreateLabel(stationPopup.transform, "Output → (none)", 15);
        statusText = CreateLabel(stationPopup.transform, "", 13);
        statusText.color = new Color(0.85f, 0.85f, 0.9f, 1f);
        statusText.alignment = TextAlignmentOptions.Left;

        assignWorkerButton = CreateButton(stationPopup.transform, "Assign Worker", OnAssignWorkerClicked);
        clearWorkerButton = CreateButton(stationPopup.transform, "Clear Worker", OnClearWorkerClicked);
        assignOutputButton = CreateButton(stationPopup.transform, "Assign Output", OnAssignOutputClicked);
        clearOutputButton = CreateButton(stationPopup.transform, "Clear Output", OnClearOutputClicked);

        var listGo = new GameObject("WorkerList", typeof(RectTransform));
        listGo.transform.SetParent(stationPopup.transform, false);
        workerListContainer = listGo.transform;
        var listVlg = listGo.AddComponent<VerticalLayoutGroup>();
        listVlg.spacing = 4;
        listVlg.childForceExpandWidth = true;
        var le = listGo.AddComponent<LayoutElement>();
        le.minHeight = 80;
        le.flexibleHeight = 1;

        stationPopup.transform.SetAsLastSibling();
    }

    Canvas ResolvePlayerUICanvas()
    {
        if (playerUICanvas != null) return playerUICanvas;

        // Prefer the scene object named PlayerUI
        var named = GameObject.Find("PlayerUI");
        if (named != null)
        {
            playerUICanvas = named.GetComponent<Canvas>();
            if (playerUICanvas != null) return playerUICanvas;
        }

        // Fallback: TitleScreenController's in-game UI root
        var title = FindObjectOfType<TitleScreenController>();
        if (title != null && title.inGameUIRoot != null)
        {
            playerUICanvas = title.inGameUIRoot.GetComponent<Canvas>();
            if (playerUICanvas != null) return playerUICanvas;
        }

        // Last resort: any root canvas that isn't the title screen
        var canvases = FindObjectsOfType<Canvas>(true);
        foreach (var c in canvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            if (c.gameObject.name.Contains("Title")) continue;
            playerUICanvas = c;
            return c;
        }

        return FindObjectOfType<Canvas>();
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string text, float size)
    {
        var go = new GameObject("Label", typeof(RectTransform));
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

    static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.35f, 0.45f, 1f);
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
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

    Button CreateListButton(string label, bool interactable)
    {
        var go = new GameObject("WorkerPick", typeof(RectTransform));
        go.transform.SetParent(workerListContainer, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.25f, 0.32f, 1f);
        var btn = go.AddComponent<Button>();
        btn.interactable = interactable;
        go.AddComponent<LayoutElement>().minHeight = 30;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13;
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
