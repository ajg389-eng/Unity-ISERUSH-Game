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

    [Header("Station popup layout")]
    [Tooltip("Screen anchor for the popup (0=left/bottom, 1=right/top). Default pins to right-center.")]
    public Vector2 popupAnchor = new Vector2(1f, 0.5f);
    [Tooltip("Pivot on the popup panel itself. Match horizontal anchor for edge-aligned panels.")]
    public Vector2 popupPivot = new Vector2(1f, 0.5f);
    [Tooltip("Pixel offset from the anchor. Negative X pulls inward from the right edge.")]
    public Vector2 popupAnchoredPosition = new Vector2(-24f, 0f);

    [Header("Optional UI roots")]
    [Tooltip("Assign a scene copy of StationManagePopup to use your layout. If empty, looks for StationManagePopup under PlayerUI before creating one.")]
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

    [Header("Station production previews")]
    public GameObject rawPattyPreviewPrefab;
    public GameObject cookedPattyPreviewPrefab;
    public GameObject rawFriesPreviewPrefab;
    public GameObject cookedFriesPreviewPrefab;
    public GameObject burgerPreviewPrefab;
    public GameObject drinkPreviewPrefab;

    GameObject productionDiagramRoot;
    GameObject productionInputCard;
    GameObject productionConversionRoot;
    GameObject productionOutputCard;
    RawImage productionInputPreview;
    RawImage productionOutputPreview;
    TextMeshProUGUI productionInputName;
    TextMeshProUGUI productionInputRate;
    TextMeshProUGUI productionOutputName;
    TextMeshProUGUI productionOutputRate;
    TextMeshProUGUI productionCycleText;
    GameObject pickupInventoryRoot;
    TextMeshProUGUI pickupStockText;
    readonly RawImage[] pickupSlotPreviews = new RawImage[4];
    readonly TextMeshProUGUI[] pickupSlotLabels = new TextMeshProUGUI[4];

    [Header("Workflow decision support")]
    public GameObject workflowDecisionHud;
    public TextMeshProUGUI workflowDecisionText;

    [Header("Flow capture")]
    public GameObject flowCaptureHud;
    public TextMeshProUGUI flowCaptureTitle;
    public TextMeshProUGUI flowCapturePath;
    public Button flowCaptureFinishButton;
    public Button flowCaptureCancelButton;

    /// <summary>True when using a designer-placed popup (inspector or scene). Keeps your RectTransform.</summary>
    bool usingScenePopup;

    enum PendingAction { None, PickOutput, PickWorker }
    PendingAction pending;
    StationNode selectedStation;
    StationSelectionHighlight selectedHighlight;
    WorkerHoverHighlight hoveredWorkerHighlight;
    KitchenEmployee selectedEmployee;
    WorkerHoverHighlight selectedWorkerHighlight;

    StationNode outputDragSource;
    Vector2 outputDragStartScreen;
    bool outputDragActive;
    bool outputDragMoved;
    LineRenderer outputDragPreview;
    const float OutputDragThresholdPixels = 14f;
    float nextWorkflowHudRefresh;
    ProductionFlowPlan capturedFlow;
    readonly Dictionary<GameObject, GameObject> capturedOriginalOutputs = new Dictionary<GameObject, GameObject>();
    bool capturingNewFlow;
    readonly List<GameObject> editBackupStations = new List<GameObject>();
    readonly List<string> editBackupStepIds = new List<string>();
    readonly Dictionary<GameObject, GameObject> editBackupOutputs = new Dictionary<GameObject, GameObject>();
    readonly List<StationSelectionHighlight> flowCaptureHighlights = new List<StationSelectionHighlight>();

    public bool IsCapturingFlow => capturedFlow != null;
    public ProductionFlowPlan CapturedFlow => capturedFlow;
    public KitchenEmployee SelectedEmployee => selectedEmployee;

    void Awake()
    {
        Instance = this;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (stationPopup != null)
            usingScenePopup = true;
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
        EnsureWorkflowDecisionHud();
    }

    void EnsureLinkVisuals()
    {
        if (StationOutputLinkVisuals.Instance == null)
        {
            var go = new GameObject("StationOutputLinkVisuals");
            var visuals = go.AddComponent<StationOutputLinkVisuals>();
            visuals.modeManager = modeManager;
        }

        if (WorkerAssignmentLinkVisuals.Instance == null)
        {
            var go = new GameObject("WorkerAssignmentLinkVisuals");
            var visuals = go.AddComponent<WorkerAssignmentLinkVisuals>();
            visuals.modeManager = modeManager;
        }
    }

    public bool IsManageMode =>
        modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;

    /// <summary>Select a worker from the Management UI and show their grid workflow.</summary>
    public void SelectWorker(KitchenEmployee employee)
    {
        if (!IsManageMode || employee == null) return;
        SelectEmployeeForAssignment(employee);
    }

    void Update()
    {
        if (!IsManageMode)
        {
            CustomerWallDoor.HideActivePopup();
            if (IsCapturingFlow)
                CancelFlowCapture();
            if (selectedStation != null || pending != PendingAction.None || selectedEmployee != null)
                CancelAndHide();
            ClearWorkerHover();
            CancelOutputDrag();
            SetWorkflowDecisionHudVisible(false);
            return;
        }

        UpdateWorkerHover();
        UpdateOutputDragPreview();
        RefreshWorkflowDecisionHud();

        if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.Escape) && IsCapturingFlow)
        {
            CancelFlowCapture();
            PauseMenuUI.MarkEscapeHandled();
            return;
        }

        // Keep heat lamp inventory / recipe-station rates live while selected
        if (selectedStation != null
            && stationPopup != null
            && stationPopup.activeSelf)
        {
            if (selectedStation.GetComponent<HeatLampStation>() != null)
                RefreshHeatLampInventory();
            else
                RefreshRecipeStationRatesLive();
        }

        if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.Escape) && HasCancellableManageAction())
        {
            if (PauseMenuUI.IsOpen)
                return;

            CancelOutputDrag();
            CancelAndHide();
            PauseMenuUI.MarkEscapeHandled();
            return;
        }

        if (Input.GetMouseButtonUp(0))
        {
            HandleMouseUp();
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;
        if (Camera.main == null) return;
        if (IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            CustomerWallDoor.HideActivePopup();
            if (pending == PendingAction.None && !IsCapturingFlow)
            {
                ClearEmployeeSelection();
                ClearSelection();
            }
            CancelOutputDrag();
            return;
        }

        // Click worker first: select them for station assignment.
        var clickedEmployee = hit.collider.GetComponentInParent<KitchenEmployee>();
        if (clickedEmployee != null && !IsCapturingFlow)
        {
            CustomerWallDoor.HideActivePopup();
            CancelOutputDrag();
            SelectEmployeeForAssignment(clickedEmployee);
            return;
        }

        CustomerWallDoor clickedDoor = hit.collider.GetComponentInParent<CustomerWallDoor>();
        if (clickedDoor != null && !IsCapturingFlow)
        {
            CancelOutputDrag();
            ClearEmployeeSelection();
            ClearSelection();
            clickedDoor.ShowRolePopup();
            return;
        }

        CustomerWallDoor.HideActivePopup();

        var node = StationNode.FindFromCollider(hit.collider);
        if (node == null)
        {
            if (pending == PendingAction.None && !IsCapturingFlow)
            {
                ClearEmployeeSelection();
                ClearSelection();
            }
            CancelOutputDrag();
            return;
        }

        if (IsCapturingFlow)
        {
            AddCapturedFlowStation(node);
            return;
        }

        // Legacy button flow: next station click sets output.
        if (pending == PendingAction.PickOutput)
        {
            if (selectedStation != null && node.gameObject != selectedStation.gameObject)
            {
                ApplyOutputLink(selectedStation, node);
                pending = PendingAction.None;
            }
            return;
        }

        // Worker selected → assign to clicked station.
        if (selectedEmployee != null)
        {
            CancelOutputDrag();
            TryAssignEmployeeToStation(selectedEmployee, node);
            return;
        }

        // Start potential drag-to-assign-output from this station.
        BeginOutputDrag(node);
        SelectStation(node);
    }

    void HandleMouseUp()
    {
        if (!outputDragActive)
            return;

        StationNode source = outputDragSource;
        bool wasDrag = outputDragMoved
            || (Vector2.Distance(outputDragStartScreen, Input.mousePosition) >= OutputDragThresholdPixels);

        StationNode target = null;
        if (wasDrag && Camera.main != null && !IsPointerOverUI())
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
                target = StationNode.FindFromCollider(hit.collider);
        }

        CancelOutputDrag();

        if (source != null && target != null && target != source && wasDrag)
            ApplyOutputLink(source, target);
    }

    void BeginOutputDrag(StationNode source)
    {
        outputDragSource = source;
        outputDragStartScreen = Input.mousePosition;
        outputDragActive = source != null;
        outputDragMoved = false;
        EnsureOutputDragPreview();
        SetStatus("Drag to another station to set this station's Output.");
    }

    void CancelOutputDrag()
    {
        outputDragActive = false;
        outputDragMoved = false;
        outputDragSource = null;
        if (outputDragPreview != null)
            outputDragPreview.enabled = false;
    }

    void UpdateOutputDragPreview()
    {
        if (!outputDragActive || outputDragSource == null || Camera.main == null)
        {
            if (outputDragPreview != null)
                outputDragPreview.enabled = false;
            return;
        }

        float moved = Vector2.Distance(outputDragStartScreen, Input.mousePosition);
        if (moved >= OutputDragThresholdPixels)
            outputDragMoved = true;

        if (!outputDragMoved)
        {
            if (outputDragPreview != null)
                outputDragPreview.enabled = false;
            return;
        }

        EnsureOutputDragPreview();
        Vector3 from = GetStationAnchor(outputDragSource.gameObject);
        Vector3 to = from;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            var target = StationNode.FindFromCollider(hit.collider);
            to = target != null ? GetStationAnchor(target.gameObject) : hit.point;
        }
        else if (Physics.Raycast(ray, out hit, 500f))
        {
            to = hit.point;
        }
        else
        {
            // Project onto a horizontal plane near the source.
            var plane = new Plane(Vector3.up, from);
            if (plane.Raycast(ray, out float enter))
                to = ray.GetPoint(enter);
        }

        float bridgeY = Mathf.Max(from.y, to.y) + 1.5f;
        outputDragPreview.enabled = true;
        outputDragPreview.SetPosition(0, from);
        outputDragPreview.SetPosition(1, new Vector3(from.x, bridgeY, from.z));
        outputDragPreview.SetPosition(2, new Vector3(to.x, bridgeY, to.z));
        outputDragPreview.SetPosition(3, to);
    }

    void EnsureOutputDragPreview()
    {
        if (outputDragPreview != null) return;

        var go = new GameObject("OutputDragPreview");
        go.transform.SetParent(transform, false);
        outputDragPreview = go.AddComponent<LineRenderer>();
        outputDragPreview.useWorldSpace = true;
        outputDragPreview.positionCount = 4;
        outputDragPreview.startWidth = 0.08f;
        outputDragPreview.endWidth = 0.05f;
        outputDragPreview.numCapVertices = 4;
        outputDragPreview.numCornerVertices = 4;
        outputDragPreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outputDragPreview.receiveShadows = false;

        var shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        var color = new Color(1f, 0.7f, 0.2f, 0.9f);
        mat.color = color;
        outputDragPreview.material = mat;
        outputDragPreview.startColor = color;
        outputDragPreview.endColor = color;
        outputDragPreview.enabled = false;
    }

    static Vector3 GetStationAnchor(GameObject go)
    {
        if (go == null) return Vector3.zero;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null || renderers[i] is LineRenderer) continue;
                if (renderers[i].GetComponentInParent<Canvas>() != null) continue;
                b.Encapsulate(renderers[i].bounds);
            }
            return new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        }

        var col = go.GetComponentInChildren<Collider>();
        if (col != null)
        {
            var b = col.bounds;
            return new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        }

        return go.transform.position + Vector3.up;
    }

    void ApplyOutputLink(StationNode from, StationNode to)
    {
        if (from == null || to == null || from == to) return;

        from.SetOutput(to.gameObject);
        Sfx.Play(SfxId.AssignOutput);
        if (selectedStation == from)
            RefreshPopup();
        StationOutputLinkVisuals.NotifyLinksChanged();
        SetStatus($"Output set: {from.DisplayName} → {to.DisplayName}");
    }

    void SelectEmployeeForAssignment(KitchenEmployee emp)
    {
        if (emp == null) return;

        // Cancel output picking if we switch to worker assignment.
        if (pending == PendingAction.PickOutput)
            pending = PendingAction.None;

        SetSelectedEmployee(emp);
        ClearSelection();
        SetStatus(emp.employeeName + " selected — click a station to assign them (" +
                  emp.OperatedStationCount + "/" + KitchenEmployee.MaxStations + ").");
    }

    void SetSelectedEmployee(KitchenEmployee emp)
    {
        if (selectedEmployee == emp) return;

        if (selectedWorkerHighlight != null)
        {
            selectedWorkerHighlight.SetHovered(false);
            selectedWorkerHighlight = null;
        }

        selectedEmployee = emp;
        if (selectedEmployee != null)
        {
            selectedWorkerHighlight = WorkerHoverHighlight.EnsureOn(selectedEmployee);
            if (selectedWorkerHighlight != null)
                selectedWorkerHighlight.SetHovered(true);
        }

        WorkerAssignmentLinkVisuals.SetFocusedWorker(selectedEmployee);
        RefreshWorkflowDecisionHud();
    }

    void ClearEmployeeSelection()
    {
        if (selectedWorkerHighlight != null)
        {
            // Keep hover glow if the cursor is still over this worker.
            bool keepHover = hoveredWorkerHighlight == selectedWorkerHighlight;
            if (!keepHover)
                selectedWorkerHighlight.SetHovered(false);
            selectedWorkerHighlight = null;
        }
        selectedEmployee = null;
        SetWorkflowDecisionHudVisible(false);
        // If a station is still selected, show its worker link instead of clearing.
        if (selectedStation != null)
            WorkerAssignmentLinkVisuals.SetFocusedStation(selectedStation);
        else
            WorkerAssignmentLinkVisuals.ClearFocus();
    }

    void TryAssignEmployeeToStation(KitchenEmployee emp, StationNode node)
    {
        if (emp == null || node == null) return;

        if (!node.IsWorkStation)
        {
            SetStatus("Only kitchen/register stations can have workers.");
            return;
        }

        if (emp.IsAssignedTo(node.gameObject))
        {
            SetStatus(emp.employeeName + " is already assigned to " + node.DisplayName + ".");
            SelectStation(node);
            return;
        }

        if (emp.OperatedStationCount >= KitchenEmployee.MaxStations)
        {
            SetStatus(emp.employeeName + " already has " + KitchenEmployee.MaxStations + " stations.");
            return;
        }

        node.SetWorker(emp);
        Sfx.Play(SfxId.AssignWorker);
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.WorkerAssigned);
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();

        SetStatus(emp.employeeName + " assigned to " + node.DisplayName +
                  " (" + emp.OperatedStationCount + "/" + KitchenEmployee.MaxStations + "). Click another station or Esc.");
        SelectStation(node);
        // Keep employee selected so you can assign them to more stations.
        SetSelectedEmployee(emp);
    }

    void UpdateWorkerHover()
    {
        if (Camera.main == null || IsPointerOverUI())
        {
            ClearWorkerHover();
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            ClearWorkerHover();
            return;
        }

        var employee = hit.collider.GetComponentInParent<KitchenEmployee>();
        if (employee == null)
        {
            ClearWorkerHover();
            return;
        }

        var highlight = WorkerHoverHighlight.EnsureOn(employee);
        if (hoveredWorkerHighlight == highlight) return;

        ClearWorkerHover();
        hoveredWorkerHighlight = highlight;
        if (hoveredWorkerHighlight != null)
            hoveredWorkerHighlight.SetHovered(true);
    }

    void ClearWorkerHover()
    {
        if (hoveredWorkerHighlight == null) return;
        // Don't clear glow if this worker is the selected assignment source.
        if (hoveredWorkerHighlight == selectedWorkerHighlight)
        {
            hoveredWorkerHighlight = null;
            return;
        }
        hoveredWorkerHighlight.SetHovered(false);
        hoveredWorkerHighlight = null;
    }

    void SelectStation(StationNode node)
    {
        Sfx.Play(SfxId.StationSelect);
        SetSelectedStation(node);
        pending = PendingAction.None;
        EnsurePopup();
        if (stationPopup != null)
            stationPopup.SetActive(true);
        RefreshPopup();
        WorkerAssignmentLinkVisuals.SetFocusedStation(node);

        var t = node != null ? node.StationType : null;
        if (node != null && node.GetComponent<HeatLampStation>() != null)
            SetStatus("Holding finished items for customers to pick up.");
        else if (node != null && (node.GetComponent<GrillStation>() != null || node.GetComponent<AssemblyStation>() != null))
            SetStatus("Choose what this station should produce.");
        else if (t == StationType.Register)
            SetStatus("Takes orders. Customers collect them at the Pickup Station.");
        else
            SetStatus("Production rate at the current cycle time.");
    }

    void RefreshPopup()
    {
        if (selectedStation == null) return;

        if (stationTitleText != null)
            stationTitleText.text = selectedStation.DisplayName;

        bool isHeatLamp = selectedStation.GetComponent<HeatLampStation>() != null;

        // Flows own worker/output assignment — hide these controls on every station.
        if (assignWorkerButton != null) assignWorkerButton.gameObject.SetActive(false);
        if (clearWorkerButton != null) clearWorkerButton.gameObject.SetActive(false);
        if (assignOutputButton != null) assignOutputButton.gameObject.SetActive(false);
        if (clearOutputButton != null) clearOutputButton.gameObject.SetActive(false);
        if (workerListContainer != null) workerListContainer.gameObject.SetActive(false);

        if (isHeatLamp)
        {
            if (workerInfoText != null) workerInfoText.gameObject.SetActive(false);
            if (outputInfoText != null) outputInfoText.gameObject.SetActive(false);
            if (productionDiagramRoot != null) productionDiagramRoot.SetActive(false);
        }
        else
        {
            ApplyStationRateLabels(selectedStation);
        }

        RefreshProductSection();
        RefreshHeatLampInventory();
        ApplyPanelLayout(isHeatLamp);
    }

    void RefreshRecipeStationRatesLive()
    {
        if (selectedStation == null) return;
        if (selectedStation.GetComponent<HeatLampStation>() != null) return;
        ApplyStationRateLabels(selectedStation);
    }

    void ApplyStationRateLabels(StationNode node)
    {
        if (node == null) return;
        node.EnsureIoDefaults(force: true);
        if (workerInfoText != null) workerInfoText.gameObject.SetActive(false);
        if (outputInfoText != null) outputInfoText.gameObject.SetActive(false);
        RefreshProductionDiagram(node);
    }

    static void TightenLabel(TextMeshProUGUI label, float height)
    {
        if (label == null) return;
        var le = label.GetComponent<LayoutElement>();
        if (le == null) le = label.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleHeight = 0;
    }

    static string FormatPerMinute(float rate)
    {
        if (Mathf.Approximately(rate, Mathf.Round(rate))) return rate.ToString("0");
        if (rate >= 10f) return rate.ToString("0");
        return rate.ToString("0.0");
    }

    void EnsureProductionDiagramUI()
    {
        if (stationPopup == null || productionDiagramRoot != null) return;

        Transform existing = stationPopup.transform.Find("ProductionDiagram");
        if (existing != null)
            Destroy(existing.gameObject);

        productionDiagramRoot = new GameObject("ProductionDiagram", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        productionDiagramRoot.transform.SetParent(stationPopup.transform, false);
        productionDiagramRoot.transform.SetSiblingIndex(1);
        var diagramBackground = productionDiagramRoot.GetComponent<Image>();
        diagramBackground.color = Color.clear;
        diagramBackground.raycastTarget = false;

        var layout = productionDiagramRoot.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 7, 7);
        layout.spacing = 7f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var rootLayout = productionDiagramRoot.GetComponent<LayoutElement>();
        rootLayout.minHeight = 154f;
        rootLayout.preferredHeight = 154f;
        rootLayout.flexibleHeight = 0f;

        productionInputCard = CreateProductionCard(productionDiagramRoot.transform, "Input", out productionInputPreview,
            out productionInputName, out productionInputRate);

        productionConversionRoot = new GameObject("Conversion", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        productionConversionRoot.transform.SetParent(productionDiagramRoot.transform, false);
        var centerLayout = productionConversionRoot.GetComponent<VerticalLayoutGroup>();
        centerLayout.childAlignment = TextAnchor.MiddleCenter;
        centerLayout.childControlWidth = true;
        centerLayout.childControlHeight = true;
        centerLayout.childForceExpandWidth = true;
        centerLayout.childForceExpandHeight = false;
        centerLayout.spacing = 5f;
        var centerSize = productionConversionRoot.GetComponent<LayoutElement>();
        centerSize.minWidth = 42f;
        centerSize.preferredWidth = 42f;
        centerSize.flexibleWidth = 0f;

        TextMeshProUGUI arrow = CreateDiagramText(productionConversionRoot.transform, "Arrow", ">", 30f, 40f);
        arrow.color = new Color(0.3f, 0.9f, 1f, 1f);
        productionCycleText = CreateDiagramText(productionConversionRoot.transform, "CycleTime", "0s\ncycle", 11f, 38f);
        productionCycleText.color = new Color(1f, 0.78f, 0.32f, 1f);

        productionOutputCard = CreateProductionCard(productionDiagramRoot.transform, "Output", out productionOutputPreview,
            out productionOutputName, out productionOutputRate);
        productionDiagramRoot.SetActive(false);
    }

    static GameObject CreateProductionCard(Transform parent, string heading, out RawImage preview,
        out TextMeshProUGUI itemName, out TextMeshProUGUI rate)
    {
        var card = new GameObject(heading + "Card", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.115f, 0.98f);
        var size = card.GetComponent<LayoutElement>();
        size.minWidth = 98f;
        size.preferredWidth = 104f;
        size.flexibleWidth = 1f;

        var layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(5, 5, 4, 5);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateDiagramText(card.transform, "Heading", heading.ToUpperInvariant(), 10f, 15f);
        title.color = new Color(0.65f, 0.72f, 0.84f, 1f);

        var previewFrame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        previewFrame.transform.SetParent(card.transform, false);
        // Match ItemPreviewThumbnails' camera background so the frame and texture read as one surface.
        previewFrame.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.2f, 1f);
        var previewSize = previewFrame.GetComponent<LayoutElement>();
        previewSize.minHeight = 68f;
        previewSize.preferredHeight = 68f;
        previewSize.flexibleHeight = 0f;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage),
            typeof(AspectRatioFitter));
        previewObject.transform.SetParent(previewFrame.transform, false);
        preview = previewObject.GetComponent<RawImage>();
        preview.color = Color.white;
        preview.raycastTarget = false;
        var previewFitter = previewObject.GetComponent<AspectRatioFitter>();
        previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        previewFitter.aspectRatio = 1f;

        itemName = CreateDiagramText(card.transform, "ItemName", "Item", 11f, 22f);
        itemName.enableAutoSizing = true;
        itemName.fontSizeMin = 8f;
        itemName.fontSizeMax = 11f;
        rate = CreateDiagramText(card.transform, "Rate", "0 / min", 12f, 20f);
        rate.color = new Color(1f, 0.78f, 0.32f, 1f);
        return card;
    }

    static TextMeshProUGUI CreateDiagramText(Transform parent, string objectName, string value,
        float fontSize, float height)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        var size = go.GetComponent<LayoutElement>();
        size.minHeight = height;
        size.preferredHeight = height;
        size.flexibleHeight = 0f;
        return text;
    }

    void RefreshProductionDiagram(StationNode node)
    {
        EnsureProductionDiagramUI();
        if (productionDiagramRoot == null || node == null) return;

        GameObject inputPrefab = null;
        GameObject outputPrefab = null;
        string inputName = string.IsNullOrEmpty(node.inputUnit) || node.inputUnit == "-"
            ? "Kitchen stock" : ToTitleCase(node.inputUnit);
        string outputName = string.IsNullOrEmpty(node.outputUnit)
            ? "Items" : ToTitleCase(node.outputUnit);
        float inputRate = node.HasInputAmount ? node.inputAmountPerMinute : node.outputAmountPerMinute;
        float cycleSeconds = 0f;

        var freezer = node.GetComponent<FreezerStation>();
        var grill = node.GetComponent<GrillStation>();
        var assembly = node.GetComponent<AssemblyStation>();
        var fryer = node.GetComponent<FryerStation>();
        var drink = node.GetComponent<DrinkStation>();
        var pantry = node.GetComponent<PantryStation>();

        bool outputOnly = false;
        if (freezer != null)
        {
            outputPrefab = rawPattyPreviewPrefab;
            outputName = "Raw patties";
            cycleSeconds = freezer.processTimeSeconds;
            outputOnly = true;
        }
        else if (grill != null)
        {
            inputPrefab = rawPattyPreviewPrefab;
            outputPrefab = cookedPattyPreviewPrefab;
            inputName = "Raw patties";
            outputName = "Cooked patties";
            cycleSeconds = grill.processTimeSeconds;
        }
        else if (assembly != null)
        {
            inputPrefab = cookedPattyPreviewPrefab;
            outputPrefab = burgerPreviewPrefab;
            inputName = "Cooked patties";
            outputName = assembly.selectedProduct != null
                ? DisplayItemName(assembly.selectedProduct) : "Burger";
            cycleSeconds = assembly.processTimeSeconds;
        }
        else if (fryer != null)
        {
            inputPrefab = rawFriesPreviewPrefab;
            outputPrefab = cookedFriesPreviewPrefab;
            inputName = "Raw fries";
            outputName = "Cooked fries";
            cycleSeconds = fryer.processTimeSeconds;
        }
        else if (drink != null)
        {
            outputPrefab = drinkPreviewPrefab;
            outputName = "Drinks";
            cycleSeconds = drink.processTimeSeconds;
            outputOnly = true;
        }
        else if (pantry != null)
        {
            outputPrefab = burgerPreviewPrefab;
            outputName = "Ingredients";
            cycleSeconds = pantry.processTimeSeconds;
            outputOnly = true;
        }
        else
        {
            productionDiagramRoot.SetActive(false);
            return;
        }

        productionDiagramRoot.SetActive(true);
        SetProductionDiagramMode(outputOnly);
        if (!outputOnly)
            SetDiagramPreview(productionInputPreview, inputPrefab, inputName);
        SetDiagramPreview(productionOutputPreview, outputPrefab, outputName);
        if (!outputOnly)
        {
            productionInputName.text = inputName;
            productionInputRate.text = FormatPerMinute(inputRate) + " / min";
        }
        productionOutputName.text = outputName;
        productionOutputRate.text = FormatPerMinute(node.outputAmountPerMinute) + " / min";
        if (!outputOnly)
            productionCycleText.text = FormatSeconds(cycleSeconds) + "s\ncycle";
    }

    void SetProductionDiagramMode(bool outputOnly)
    {
        var diagramSize = productionDiagramRoot != null
            ? productionDiagramRoot.GetComponent<LayoutElement>() : null;
        if (diagramSize != null)
        {
            diagramSize.minHeight = 154f;
            diagramSize.preferredHeight = 154f;
            diagramSize.flexibleHeight = 0f;
        }

        if (productionInputCard != null) productionInputCard.SetActive(!outputOnly);
        if (productionConversionRoot != null) productionConversionRoot.SetActive(!outputOnly);
        if (productionOutputCard == null) return;

        var outputSize = productionOutputCard.GetComponent<LayoutElement>();
        if (outputSize == null) return;
        outputSize.minWidth = outputOnly ? 150f : 98f;
        outputSize.preferredWidth = outputOnly ? 180f : 104f;
        outputSize.flexibleWidth = outputOnly ? 0f : 1f;
    }

    static void SetDiagramPreview(RawImage image, GameObject prefab, string label)
    {
        if (image == null) return;
        image.texture = prefab != null ? ItemPreviewThumbnails.GetPrefab(prefab, label) : null;
        image.color = image.texture != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);
        var fitter = image.GetComponent<AspectRatioFitter>();
        if (fitter != null && image.texture != null && image.texture.height > 0)
            fitter.aspectRatio = (float)image.texture.width / image.texture.height;
    }

    static string DisplayItemName(ItemDefinition item)
    {
        if (item == null) return "Item";
        return !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
    }

    static string ToTitleCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Items";
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    static string FormatSeconds(float seconds)
    {
        return Mathf.Approximately(seconds, Mathf.Round(seconds))
            ? seconds.ToString("0") : seconds.ToString("0.0");
    }

    void ApplyPopupLayout(RectTransform rt, float height)
    {
        if (rt == null) return;
        rt.anchorMin = popupAnchor;
        rt.anchorMax = popupAnchor;
        rt.pivot = popupPivot;
        rt.anchoredPosition = popupAnchoredPosition;
        rt.sizeDelta = new Vector2(300f, height);
    }

    void ApplyPanelLayout(bool heatLampCompact)
    {
        if (stationPopup == null) return;

        bool hasRecipeControls = productListContainer != null
            && productListContainer.gameObject.activeSelf;
        bool hasInput = selectedStation != null
            && !heatLampCompact
            && selectedStation.HasInputAmount;

        var rt = (RectTransform)stationPopup.transform;
        var vlg = stationPopup.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 4;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // Collapse leftover assign-button space and worker list.
        CollapseInactiveLayout(assignWorkerButton != null ? assignWorkerButton.gameObject : null);
        CollapseInactiveLayout(clearWorkerButton != null ? clearWorkerButton.gameObject : null);
        CollapseInactiveLayout(assignOutputButton != null ? assignOutputButton.gameObject : null);
        CollapseInactiveLayout(clearOutputButton != null ? clearOutputButton.gameObject : null);
        CollapseInactiveLayout(workerListContainer != null ? workerListContainer.gameObject : null);
        CollapseInactiveLayout(workerInfoText != null ? workerInfoText.gameObject : null);
        CollapseInactiveLayout(productInfoText != null ? productInfoText.gameObject : null);
        CollapseInactiveLayout(productListContainer != null ? productListContainer.gameObject : null);
        CollapseInactiveLayout(inventoryInfoText != null ? inventoryInfoText.gameObject : null);
        CollapseInactiveLayout(productionDiagramRoot);
        CollapseInactiveLayout(pickupInventoryRoot);

        float height;
        if (heatLampCompact)
            height = 180f;
        else if (hasRecipeControls)
            height = 260f;
        else if (hasInput)
            height = 220f;
        else
            height = 220f;

        if (!usingScenePopup)
            ApplyPopupLayout(rt, height);
        else
            rt.sizeDelta = new Vector2(rt.sizeDelta.x > 10f ? rt.sizeDelta.x : 300f, height);

        if (stationTitleText != null)
        {
            stationTitleText.fontSize = 22;
            TightenLabel(stationTitleText, 28f);
        }

        if (statusText != null)
        {
            statusText.text = string.Empty;
            statusText.gameObject.SetActive(false);
        }

        if (productInfoText != null && productInfoText.gameObject.activeSelf)
            TightenLabel(productInfoText, 22f);

        if (productListContainer != null && productListContainer.gameObject.activeSelf)
        {
            var listLe = productListContainer.GetComponent<LayoutElement>();
            if (listLe == null) listLe = productListContainer.gameObject.AddComponent<LayoutElement>();
            listLe.minHeight = 32;
            listLe.preferredHeight = 36;
            listLe.flexibleHeight = 0;
        }

        if (inventoryInfoText != null && inventoryInfoText.gameObject.activeSelf)
        {
            var invLe = inventoryInfoText.GetComponent<LayoutElement>();
            if (invLe != null)
            {
                invLe.minHeight = 140;
                invLe.preferredHeight = 140;
                invLe.flexibleHeight = 0;
            }
        }

        // Keep a predictable vertical order regardless of the serialized scene hierarchy.
        if (stationTitleText != null) stationTitleText.transform.SetSiblingIndex(0);
        if (heatLampCompact)
        {
            if (pickupInventoryRoot != null) pickupInventoryRoot.transform.SetSiblingIndex(1);
            if (statusText != null) statusText.transform.SetSiblingIndex(2);
        }
        else
        {
            if (productionDiagramRoot != null) productionDiagramRoot.transform.SetSiblingIndex(1);
            if (productInfoText != null) productInfoText.transform.SetSiblingIndex(2);
            if (productListContainer != null) productListContainer.SetSiblingIndex(3);
            if (statusText != null) statusText.transform.SetSiblingIndex(4);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    static void CollapseInactiveLayout(GameObject go)
    {
        if (go == null || go.activeSelf) return;
        var le = go.GetComponent<LayoutElement>();
        if (le == null) return;
        le.minHeight = 0;
        le.preferredHeight = 0;
        le.flexibleHeight = 0;
    }

    void RefreshHeatLampInventory()
    {
        EnsureInventoryUI();
        if (pickupInventoryRoot == null) return;

        var lamp = selectedStation != null ? selectedStation.GetComponent<HeatLampStation>() : null;
        bool show = lamp != null;
        pickupInventoryRoot.SetActive(show);
        if (inventoryInfoText != null) inventoryInfoText.gameObject.SetActive(false);
        if (!show) return;

        var inventorySize = pickupInventoryRoot.GetComponent<LayoutElement>();
        if (inventorySize != null)
        {
            inventorySize.minHeight = 112f;
            inventorySize.preferredHeight = 112f;
            inventorySize.flexibleHeight = 0f;
        }

        pickupStockText.text = "STOCK  " + lamp.Count + " / 4";
        for (int i = 0; i < pickupSlotPreviews.Length; i++)
        {
            HeldMeal meal = i < lamp.Meals.Count ? lamp.Meals[i] : null;
            ItemDefinition item = meal != null && meal.order != null ? meal.order.PrimaryItem : null;
            GameObject prefab = GetPickupPreviewPrefab(item);
            string label = item != null ? DisplayItemName(item) : "Empty";
            SetDiagramPreview(pickupSlotPreviews[i], prefab, label);
            pickupSlotLabels[i].text = label;
            pickupSlotLabels[i].color = item != null
                ? Color.white : new Color(0.55f, 0.58f, 0.65f, 1f);
        }
    }

    void EnsureInventoryUI()
    {
        if (stationPopup == null) return;
        if (inventoryInfoText == null)
        {

        inventoryInfoText = CreateLabel(stationPopup.transform, "Inventory\n—", 18);
        inventoryInfoText.gameObject.name = "InventoryInfo";
        inventoryInfoText.alignment = TextAlignmentOptions.Center;
        inventoryInfoText.color = new Color(1f, 0.88f, 0.55f, 1f);
        var le = inventoryInfoText.GetComponent<LayoutElement>();
        le.minHeight = 90;
        le.flexibleHeight = 1;
        inventoryInfoText.gameObject.SetActive(false);
        }

        if (pickupInventoryRoot != null) return;
        pickupInventoryRoot = new GameObject("PickupInventory", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        pickupInventoryRoot.transform.SetParent(stationPopup.transform, false);
        var rootLayout = pickupInventoryRoot.GetComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 4f;
        rootLayout.childAlignment = TextAnchor.UpperCenter;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;
        var rootSize = pickupInventoryRoot.GetComponent<LayoutElement>();
        rootSize.minHeight = 112f;
        rootSize.preferredHeight = 112f;
        rootSize.flexibleHeight = 0f;

        pickupStockText = CreateDiagramText(pickupInventoryRoot.transform, "Stock", "STOCK  0 / 4", 14f, 22f);
        pickupStockText.fontStyle = FontStyles.Bold;
        pickupStockText.color = new Color(1f, 0.78f, 0.32f, 1f);

        var row = new GameObject("Slots", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.transform.SetParent(pickupInventoryRoot.transform, false);
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 5f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;
        var rowSize = row.GetComponent<LayoutElement>();
        rowSize.minHeight = 82f;
        rowSize.preferredHeight = 82f;
        rowSize.flexibleHeight = 0f;

        for (int i = 0; i < pickupSlotPreviews.Length; i++)
            CreatePickupInventorySlot(row.transform, i);

        pickupInventoryRoot.SetActive(false);
    }

    void CreatePickupInventorySlot(Transform parent, int index)
    {
        var slot = new GameObject("Slot" + (index + 1), typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        slot.transform.SetParent(parent, false);
        slot.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.115f, 0.98f);
        var slotLayout = slot.GetComponent<VerticalLayoutGroup>();
        slotLayout.padding = new RectOffset(3, 3, 3, 3);
        slotLayout.spacing = 2f;
        slotLayout.childAlignment = TextAnchor.UpperCenter;
        slotLayout.childControlWidth = true;
        slotLayout.childControlHeight = true;
        slotLayout.childForceExpandWidth = true;
        slotLayout.childForceExpandHeight = false;
        var slotSize = slot.GetComponent<LayoutElement>();
        slotSize.minWidth = 54f;
        slotSize.preferredWidth = 60f;
        slotSize.flexibleWidth = 1f;

        var previewFrame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement));
        previewFrame.transform.SetParent(slot.transform, false);
        previewFrame.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.2f, 1f);
        var frameSize = previewFrame.GetComponent<LayoutElement>();
        frameSize.minHeight = 52f;
        frameSize.preferredHeight = 52f;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage),
            typeof(AspectRatioFitter));
        previewObject.transform.SetParent(previewFrame.transform, false);
        var preview = previewObject.GetComponent<RawImage>();
        preview.raycastTarget = false;
        var fitter = previewObject.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f;
        pickupSlotPreviews[index] = preview;

        var label = CreateDiagramText(slot.transform, "Item", "Empty", 9f, 18f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 7f;
        label.fontSizeMax = 9f;
        pickupSlotLabels[index] = label;
    }

    GameObject GetPickupPreviewPrefab(ItemDefinition item)
    {
        if (item == null) return null;
        CustomerOrderConfig menu = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (menu != null)
        {
            if (menu.IsBurger(item)) return burgerPreviewPrefab;
            if (menu.IsFries(item)) return cookedFriesPreviewPrefab;
            if (menu.IsDrink(item)) return drinkPreviewPrefab;
        }
        return null;
    }

    void RefreshProductSection()
    {
        EnsureProductUI();
        if (productInfoText == null || productListContainer == null) return;

        var grill = selectedStation != null ? selectedStation.GetComponent<GrillStation>() : null;
        var assembly = selectedStation != null ? selectedStation.GetComponent<AssemblyStation>() : null;
        bool show = grill != null || assembly != null;

        productInfoText.gameObject.SetActive(false);
        productListContainer.gameObject.SetActive(show);
        if (!show) return;

        ConfigureProductListLayout();

        ItemDefinition current = grill != null ? grill.selectedProduct : assembly.selectedProduct;

        for (int i = productListContainer.childCount - 1; i >= 0; i--)
            Destroy(productListContainer.GetChild(i).gameObject);

        var pm = ProductionManager.Instance;
        var config = pm != null ? pm.orderConfig : null;
        if (config == null)
        {
            productListContainer.gameObject.SetActive(false);
            return;
        }

        IEnumerable<ItemDefinition> options = grill != null
            ? config.GetGrillProducts()
            : config.GetAssemblyProducts();

        int optionCount = 0;
        foreach (var item in options)
        {
            if (item == null) continue;
            optionCount++;
            string label = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            bool selected = current == item;
            var btn = CreateProductButton(label + (selected ? " ✓" : ""), true);
            var captured = item;
            btn.onClick.AddListener(() => SetSelectedProduct(captured));
        }
        productListContainer.gameObject.SetActive(optionCount > 0);
    }

    void ConfigureProductListLayout()
    {
        if (productListContainer == null) return;

        var vertical = productListContainer.GetComponent<VerticalLayoutGroup>();
        if (vertical != null) vertical.enabled = false;

        var horizontal = productListContainer.GetComponent<HorizontalLayoutGroup>();
        if (horizontal == null)
            horizontal = productListContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
        horizontal.enabled = true;
        horizontal.spacing = 5f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = true;
        horizontal.childForceExpandHeight = true;
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
        var btnLe = go.AddComponent<LayoutElement>();
        btnLe.minHeight = 32;
        btnLe.preferredHeight = 32;
        btnLe.flexibleHeight = 0;

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
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.WorkerAssigned);
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
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
        CustomerWallDoor.HideActivePopup();
        pending = PendingAction.None;
        CancelOutputDrag();
        ClearEmployeeSelection();
        ClearSelection();
    }

    bool HasCancellableManageAction()
    {
        return outputDragActive
            || pending != PendingAction.None
            || selectedStation != null
            || selectedEmployee != null
            || CustomerWallDoor.HasActivePopup
            || (stationPopup != null && stationPopup.activeSelf);
    }

    void SetSelectedStation(StationNode node)
    {
        if (selectedStation == node) return;

        SetStationHighlighted(selectedStation, false);
        selectedStation = node;
        SetStationHighlighted(selectedStation, true);
    }

    void ClearSelection()
    {
        ClearStationSelection();
    }

    void ClearStationSelection()
    {
        SetStationHighlighted(selectedStation, false);
        selectedStation = null;
        selectedHighlight = null;
        HidePopup();
        if (IsCapturingFlow && capturedFlow != null)
            WorkerAssignmentLinkVisuals.SetFocusedFlow(capturedFlow);
        else if (selectedEmployee != null)
            WorkerAssignmentLinkVisuals.SetFocusedWorker(selectedEmployee);
        else
            WorkerAssignmentLinkVisuals.ClearFocus();
    }

    void SetStationHighlighted(StationNode node, bool on)
    {
        if (node == null) return;

        var highlight = StationSelectionHighlight.EnsureOn(node.gameObject);
        if (highlight == null) return;

        highlight.SetSelected(on);
        selectedHighlight = on ? highlight : null;
    }

    void HidePopup()
    {
        if (stationPopup != null) stationPopup.SetActive(false);
    }

    void EnsureWorkflowDecisionHud()
    {
        if (workflowDecisionHud != null && workflowDecisionText != null) return;
        if (playerUICanvas == null)
        {
            var named = GameObject.Find("PlayerUI");
            if (named != null) playerUICanvas = named.GetComponent<Canvas>();
            if (playerUICanvas == null) playerUICanvas = FindObjectOfType<Canvas>();
        }
        if (playerUICanvas == null) return;

        Transform existing = playerUICanvas.transform.Find("WorkflowDecisionHUD");
        if (existing != null)
        {
            workflowDecisionHud = existing.gameObject;
            workflowDecisionText = existing.Find("Text")?.GetComponent<TextMeshProUGUI>();
            return;
        }

        workflowDecisionHud = new GameObject("WorkflowDecisionHUD", typeof(RectTransform), typeof(Image));
        workflowDecisionHud.transform.SetParent(playerUICanvas.transform, false);
        var rect = (RectTransform)workflowDecisionHud.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(380f, 154f);
        var image = workflowDecisionHud.GetComponent<Image>();
        image.color = new Color(0.08f, 0.1f, 0.14f, 0.88f);
        image.raycastTarget = false;

        var titleObject = new GameObject("Title", typeof(RectTransform));
        titleObject.transform.SetParent(workflowDecisionHud.transform, false);
        var titleRect = (RectTransform)titleObject.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(-24f, 32f);
        var title = titleObject.AddComponent<TextMeshProUGUI>();
        title.text = "Workflow";
        title.fontSize = 18f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.raycastTarget = false;

        var headerObject = new GameObject("AnalysisHeader", typeof(RectTransform), typeof(Image));
        headerObject.transform.SetParent(workflowDecisionHud.transform, false);
        var headerRect = (RectTransform)headerObject.transform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = new Vector2(0f, -42f);
        headerRect.sizeDelta = new Vector2(-24f, 30f);
        var headerImage = headerObject.GetComponent<Image>();
        headerImage.color = new Color(0.28f, 0.36f, 0.48f, 1f);
        headerImage.raycastTarget = false;

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(headerObject.transform, false);
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = "Assignment Analysis";
        label.fontSize = 14f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(workflowDecisionHud.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 10f);
        textRect.offsetMax = new Vector2(-12f, -78f);
        workflowDecisionText = textObject.AddComponent<TextMeshProUGUI>();
        workflowDecisionText.fontSize = 14f;
        workflowDecisionText.color = new Color(0.78f, 0.82f, 0.88f, 1f);
        workflowDecisionText.alignment = TextAlignmentOptions.TopLeft;
        workflowDecisionText.textWrappingMode = TextWrappingModes.Normal;
        workflowDecisionText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) workflowDecisionText.font = TMP_Settings.defaultFontAsset;
        workflowDecisionHud.transform.SetAsLastSibling();
        workflowDecisionHud.SetActive(false);
    }

    public void BeginFlowCapture(ProductionFlowPlan flow, bool isNew = true)
    {
        if (flow == null) return;
        if (modeManager != null)
            modeManager.SetMode(GameModeManager.Mode.Manage);

        CancelOutputDrag();
        ClearEmployeeSelection();
        ClearSelection();
        HidePopup();

        capturingNewFlow = isNew;
        capturedFlow = flow;
        capturedFlow.kind = KitchenFlowKind.Custom;
        capturedOriginalOutputs.Clear();
        editBackupStations.Clear();
        editBackupStepIds.Clear();
        editBackupOutputs.Clear();

        if (isNew)
        {
            capturedFlow.stepIds.Clear();
            capturedFlow.stations.Clear();
        }
        else
        {
            // Rebuild GameObject route from saved steps if stations were lost (null refs, etc.).
            WorkerFlowAssigner.SynchronizeFlowRoute(capturedFlow);

            // Snapshot so Esc can restore the previous route.
            if (capturedFlow.stations != null)
            {
                foreach (GameObject station in capturedFlow.stations)
                {
                    if (station == null) continue;
                    editBackupStations.Add(station);
                    StationNode node = StationNode.EnsureOn(station);
                    if (node != null)
                    {
                        editBackupOutputs[station] = node.outputTarget;
                        capturedOriginalOutputs[station] = node.outputTarget;
                    }
                }
            }
            if (capturedFlow.stepIds != null)
                editBackupStepIds.AddRange(capturedFlow.stepIds);

            WorkerAssignmentLinkVisuals.SetFocusedFlow(capturedFlow);
        }

        var screen = FindObjectOfType<ManagementScreenController>();
        if (screen != null)
            screen.SuspendForWorldCapture();

        EnsureFlowCaptureHud();
        SetFlowCaptureHudVisible(true);
        RefreshFlowCaptureHud();
        WorkerAssignmentLinkVisuals.SetFocusedFlow(capturedFlow);
        if (isNew)
        {
            SetStatus("Click stations in order for " + capturedFlow.flowName + ". Esc cancels.");
        }
        else if (capturedFlow.stations.Count == 0)
        {
            SetStatus("Editing " + capturedFlow.flowName + ": no saved stations — click the route from the start.");
        }
        else
        {
            SetStatus("Editing " + capturedFlow.flowName + ": click to extend, click a station on the path to trim, Esc restores.");
        }
    }

    public void BeginFlowEdit(ProductionFlowPlan flow)
    {
        BeginFlowCapture(flow, isNew: false);
    }

    public bool FinishFlowCapture()
    {
        if (capturedFlow == null) return false;
        ProductionFlowPlan finished = capturedFlow;
        bool wasEdit = !capturingNewFlow;
        if (finished.stations.Count == 0)
        {
            SetStatus("A flow needs at least one station.");
            RefreshFlowCaptureHud();
            return false;
        }
        string routeProblem = WorkerFlowAssigner.ValidateStepOrder(finished.stepIds);
        if (!string.IsNullOrEmpty(routeProblem))
        {
            SetStatus(routeProblem);
            RefreshFlowCaptureHud();
            return false;
        }

        capturedFlow = null;
        capturingNewFlow = false;
        capturedOriginalOutputs.Clear();
        editBackupStations.Clear();
        editBackupStepIds.Clear();
        editBackupOutputs.Clear();
        ClearFlowCaptureHighlights();
        SetFlowCaptureHudVisible(false);

        WorkerFlowAssigner.SynchronizeFlowRoute(finished);
        OnboardingTutorial.NotifyFlowSaved(finished, wasEdit);

        ProductionManager production = ProductionManager.Instance;
        if (production != null)
        {
            production.SyncLegacyFlowSelection();
            production.lastFlowBalance = finished.workers.Count > 0
                ? WorkerFlowAssigner.ApplyBalancedTeam(finished)
                : new TeamBalanceResult { message = "Flow ready — assign workers from the list." };
        }
        SetStatus("Saved: " + WorkerFlowAssigner.FormatFlow(finished));
        WorkerAssignmentLinkVisuals.SetFocusedFlow(finished);

        var screen = FindObjectOfType<ManagementScreenController>();
        if (screen != null)
            screen.ResumeAfterWorldCapture();
        RefreshWorkersUi();
        return true;
    }

    public void CancelFlowCapture()
    {
        if (capturedFlow == null) return;
        ProductionFlowPlan cancelled = capturedFlow;
        bool wasNew = capturingNewFlow;
        capturedFlow = null;
        capturingNewFlow = false;

        foreach (var original in capturedOriginalOutputs)
        {
            StationNode node = StationNode.EnsureOn(original.Key);
            if (node != null) node.SetOutput(original.Value);
        }
        capturedOriginalOutputs.Clear();

        ProductionManager production = ProductionManager.Instance;
        if (wasNew)
        {
            if (production != null)
                production.RemoveProductionFlow(cancelled);
            SetStatus("Flow creation cancelled.");
        }
        else
        {
            cancelled.stations = new List<GameObject>(editBackupStations);
            cancelled.stepIds = new List<string>(editBackupStepIds);
            foreach (var pair in editBackupOutputs)
            {
                StationNode node = StationNode.EnsureOn(pair.Key);
                if (node != null) node.SetOutput(pair.Value);
            }
            if (production != null && cancelled.workers.Count > 0)
                production.lastFlowBalance = WorkerFlowAssigner.ApplyBalancedTeam(cancelled);
            SetStatus("Flow edit cancelled — previous route restored.");
        }

        editBackupStations.Clear();
        editBackupStepIds.Clear();
        editBackupOutputs.Clear();
        ClearFlowCaptureHighlights();
        SetFlowCaptureHudVisible(false);

        var screen = FindObjectOfType<ManagementScreenController>();
        if (screen != null && modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage)
            screen.ResumeAfterWorldCapture();
        RefreshWorkersUi();
    }

    void AddCapturedFlowStation(StationNode node)
    {
        if (capturedFlow == null || node == null) return;
        string stationId = WorkerFlowAssigner.GetStationId(node.gameObject);
        if (string.IsNullOrEmpty(stationId))
        {
            SetStatus("That object cannot be used in a production flow.");
            return;
        }
        ProductionManager production = ProductionManager.Instance;
        if (production != null && production.IsStationOnOtherFlow(node.gameObject, capturedFlow))
        {
            SetStatus(node.DisplayName + " already belongs to another flow.");
            return;
        }
        if (node.assignedWorker != null && !capturedFlow.workers.Contains(node.assignedWorker))
        {
            SetStatus(node.DisplayName + " is assigned to " + node.assignedWorker.employeeName + ". Remove that worker first.");
            return;
        }
        if (capturedFlow.stations.Contains(node.gameObject))
        {
            int index = capturedFlow.stations.IndexOf(node.gameObject);
            if (index < 0) return;

            // Already on the path: trim after this station. Clicking the current end undoes it.
            int removeDownTo = index == capturedFlow.stations.Count - 1 ? index : index + 1;
            while (capturedFlow.stations.Count > removeDownTo)
            {
                GameObject removed = capturedFlow.stations[capturedFlow.stations.Count - 1];
                capturedFlow.stations.RemoveAt(capturedFlow.stations.Count - 1);
                if (capturedFlow.stepIds.Count > 0)
                    capturedFlow.stepIds.RemoveAt(capturedFlow.stepIds.Count - 1);

                if (capturedOriginalOutputs.TryGetValue(removed, out GameObject removedOriginal))
                {
                    StationNode.EnsureOn(removed).SetOutput(removedOriginal);
                    capturedOriginalOutputs.Remove(removed);
                }
                else
                    StationNode.EnsureOn(removed).ClearOutput();
            }

            if (capturedFlow.stations.Count > 0)
            {
                GameObject newLast = capturedFlow.stations[capturedFlow.stations.Count - 1];
                if (capturedOriginalOutputs.TryGetValue(newLast, out GameObject originalOutput))
                    StationNode.EnsureOn(newLast).SetOutput(originalOutput);
                else
                    StationNode.EnsureOn(newLast).ClearOutput();
            }

            WorkerFlowAssigner.RebuildStepIdsFromStations(capturedFlow);
            StationOutputLinkVisuals.NotifyLinksChanged();
            SetStatus(capturedFlow.stations.Count == 0
                ? "Path cleared. Click the first station."
                : "Path now ends at " + StationNode.EnsureOn(capturedFlow.stations[capturedFlow.stations.Count - 1]).DisplayName + ".");
            RefreshFlowCaptureHud();
            RefreshWorkersUi();
            return;
        }
        if (capturedFlow.stations.Count >= WorkerFlowAssigner.MaxSteps)
        {
            SetStatus("This flow has reached the " + WorkerFlowAssigner.MaxSteps + " station limit.");
            return;
        }

        if (capturedFlow.stations.Count > 0)
        {
            GameObject previous = capturedFlow.stations[capturedFlow.stations.Count - 1];
            StationNode previousNode = StationNode.EnsureOn(previous);
            if (!capturedOriginalOutputs.ContainsKey(previous))
                capturedOriginalOutputs.Add(previous, previousNode.outputTarget);
            previousNode.SetOutput(node.gameObject);
        }
        capturedFlow.stations.Add(node.gameObject);
        capturedFlow.stepIds.Add(stationId);
        WorkerFlowAssigner.RebuildStepIdsFromStations(capturedFlow);
        StationOutputLinkVisuals.NotifyLinksChanged();
        SetStatus("Added " + node.DisplayName + ". Keep clicking, then Finish.");
        RefreshFlowCaptureHud();
        RefreshWorkersUi();
    }

    static void RefreshWorkersUi()
    {
        WorkersUI workersUi = FindObjectOfType<WorkersUI>();
        if (workersUi != null) workersUi.RefreshFlowOnly();
    }

    void EnsureFlowCaptureHud()
    {
        if (flowCaptureHud != null && flowCapturePath != null && flowCaptureFinishButton != null) return;
        if (playerUICanvas == null)
        {
            var playerUi = GameObject.Find("PlayerUI");
            if (playerUi != null) playerUICanvas = playerUi.GetComponent<Canvas>();
            if (playerUICanvas == null) playerUICanvas = FindObjectOfType<Canvas>();
        }
        if (playerUICanvas == null) return;

        Transform existing = playerUICanvas.transform.Find("FlowCaptureHUD");
        if (existing != null)
        {
            flowCaptureHud = existing.gameObject;
            flowCaptureTitle = existing.Find("Title")?.GetComponent<TextMeshProUGUI>();
            flowCapturePath = existing.Find("Path")?.GetComponent<TextMeshProUGUI>();
            flowCaptureFinishButton = existing.Find("Buttons/Finish")?.GetComponent<Button>();
            flowCaptureCancelButton = existing.Find("Buttons/Cancel")?.GetComponent<Button>();
            WireFlowCaptureButtons();
            return;
        }

        flowCaptureHud = new GameObject("FlowCaptureHUD", typeof(RectTransform), typeof(Image));
        flowCaptureHud.transform.SetParent(playerUICanvas.transform, false);
        var rect = (RectTransform)flowCaptureHud.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -18f);
        rect.sizeDelta = new Vector2(520f, 110f);
        var bg = flowCaptureHud.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.12f, 0.18f, 0.94f);

        var vlg = flowCaptureHud.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.spacing = 6;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        flowCaptureTitle = MakeCaptureLabel(flowCaptureHud.transform, "Title", "Create Flow — click stations in order", 15f, FontStyles.Bold);
        flowCapturePath = MakeCaptureLabel(flowCaptureHud.transform, "Path", "No stations yet", 13f, FontStyles.Normal);

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(flowCaptureHud.transform, false);
        var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        var buttonsLe = buttons.AddComponent<LayoutElement>();
        buttonsLe.minHeight = 32f;
        buttonsLe.preferredHeight = 32f;

        flowCaptureFinishButton = MakeCaptureButton(buttons.transform, "Finish", new Color(0.28f, 0.5f, 0.34f, 1f));
        flowCaptureCancelButton = MakeCaptureButton(buttons.transform, "Cancel", new Color(0.45f, 0.28f, 0.28f, 1f));
        WireFlowCaptureButtons();
        flowCaptureHud.transform.SetAsLastSibling();
        flowCaptureHud.SetActive(false);
    }

    void WireFlowCaptureButtons()
    {
        if (flowCaptureFinishButton != null)
        {
            flowCaptureFinishButton.onClick.RemoveAllListeners();
            flowCaptureFinishButton.onClick.AddListener(() => FinishFlowCapture());
        }
        if (flowCaptureCancelButton != null)
        {
            flowCaptureCancelButton.onClick.RemoveAllListeners();
            flowCaptureCancelButton.onClick.AddListener(CancelFlowCapture);
        }
    }

    static TextMeshProUGUI MakeCaptureLabel(Transform parent, string name, string text, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        le.preferredHeight = size + 6f;
        return tmp;
    }

    static Button MakeCaptureButton(Transform parent, string label, Color color)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 110f;
        le.preferredWidth = 110f;
        le.minHeight = 30f;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = label;
        tmp.fontSize = 14f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return go.GetComponent<Button>();
    }

    void RefreshFlowCaptureHud()
    {
        EnsureFlowCaptureHud();
        RefreshFlowCaptureHighlights();
        if (flowCaptureHud == null || capturedFlow == null) return;
        if (flowCaptureTitle != null)
        {
            string verb = capturingNewFlow ? "Creating" : "Editing";
            flowCaptureTitle.text = verb + " \"" + capturedFlow.flowName + "\" — click stations in order";
        }
        if (flowCapturePath != null)
        {
            if (capturedFlow.stations.Count == 0)
            {
                flowCapturePath.text = capturingNewFlow
                    ? "No stations yet — click the first station"
                    : "Current path empty — click stations to rebuild, then Finish";
            }
            else
            {
                flowCapturePath.text = WorkerFlowAssigner.FormatFlow(capturedFlow)
                    + (capturingNewFlow ? "" : "  (click a station on the path to trim)");
            }
        }
        if (flowCaptureFinishButton != null)
            flowCaptureFinishButton.interactable = capturedFlow.stations.Count > 0;
    }

    void RefreshFlowCaptureHighlights()
    {
        ClearFlowCaptureHighlights();
        if (capturedFlow == null) return;

        if (capturedFlow.stations != null)
        {
            for (int i = 0; i < capturedFlow.stations.Count; i++)
            {
                GameObject station = capturedFlow.stations[i];
                if (station == null) continue;

                var highlight = StationSelectionHighlight.EnsureOn(station);
                if (highlight == null) continue;

                highlight.SetSelected(true);
                flowCaptureHighlights.Add(highlight);
            }
        }

        // Live draft path on the kitchen floor as stations are clicked.
        WorkerAssignmentLinkVisuals.SetFocusedFlow(capturedFlow);
    }

    void ClearFlowCaptureHighlights()
    {
        for (int i = 0; i < flowCaptureHighlights.Count; i++)
        {
            if (flowCaptureHighlights[i] != null)
                flowCaptureHighlights[i].SetSelected(false);
        }
        flowCaptureHighlights.Clear();
    }

    void SetFlowCaptureHudVisible(bool show)
    {
        EnsureFlowCaptureHud();
        if (flowCaptureHud != null && flowCaptureHud.activeSelf != show)
            flowCaptureHud.SetActive(show);
    }

    void RefreshWorkflowDecisionHud()
    {
        if (selectedEmployee == null)
        {
            SetWorkflowDecisionHudVisible(false);
            return;
        }

        if (Time.unscaledTime < nextWorkflowHudRefresh) return;
        nextWorkflowHudRefresh = Time.unscaledTime + 0.12f;

        EnsureWorkflowDecisionHud();
        if (workflowDecisionText == null) return;
        string summary = WorkflowAnalysis.GetWorkerDecisionSummary(selectedEmployee);
        StationNode hoveredStation = GetStationUnderPointer();
        string preview = WorkflowAnalysis.GetAssignmentPreview(selectedEmployee, hoveredStation);
        workflowDecisionText.text = string.IsNullOrEmpty(preview) ? summary : summary + "\n" + preview;
        SetWorkflowDecisionHudVisible(true);
    }

    StationNode GetStationUnderPointer()
    {
        if (Camera.main == null || IsPointerOverUI()) return null;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer)
            ? StationNode.FindFromCollider(hit.collider)
            : null;
    }

    void SetWorkflowDecisionHudVisible(bool show)
    {
        if (workflowDecisionHud != null && workflowDecisionHud.activeSelf != show)
            workflowDecisionHud.SetActive(show);
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
        if (stationPopup == null)
        {
            stationPopup = FindExistingStationPopup();
            if (stationPopup != null)
                usingScenePopup = true;
        }

        if (stationPopup != null)
        {
            // If an old world-space popup exists from a previous version, rebuild as screen UI
            var ownCanvas = stationPopup.GetComponent<Canvas>();
            if (ownCanvas != null && ownCanvas.renderMode == RenderMode.WorldSpace)
            {
                Destroy(stationPopup);
                stationPopup = null;
                usingScenePopup = false;
            }
            else
            {
                var parentCanvas = ResolvePlayerUICanvas();
                if (parentCanvas != null && stationPopup.transform.parent != parentCanvas.transform)
                    stationPopup.transform.SetParent(parentCanvas.transform, false);

                TryImportScenePopup(stationPopup);
                BindPopupReferences();
                EnsureProductionDiagramUI();
                WirePopupButtons();
                return;
            }
        }

        var parent = ResolvePlayerUICanvas();
        if (parent == null)
        {
            Debug.LogWarning("ManagementModeController: could not find PlayerUI canvas.", this);
            return;
        }

        var popup = StationManagePopup.CreateInCanvas(parent.transform);
        if (popup == null) return;

        popup.CopyReferencesTo(this);
        usingScenePopup = true;
        BindPopupReferences();
        EnsureProductionDiagramUI();
        WirePopupButtons();
    }

    void TryImportScenePopup(GameObject popupGo)
    {
        if (popupGo == null) return;

        var popup = popupGo.GetComponent<StationManagePopup>();
        if (popup == null) return;

        popup.BindReferences();
        popup.CopyReferencesTo(this);
        usingScenePopup = true;
    }

    GameObject FindExistingStationPopup()
    {
        var popup = FindFirstObjectByType<StationManagePopup>(FindObjectsInactive.Include);
        if (popup != null)
            return popup.gameObject;

        var canvas = ResolvePlayerUICanvas();
        if (canvas == null) return null;

        foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == StationManagePopup.PopupObjectName)
                return t.gameObject;
        }

        return null;
    }

    void BindPopupReferences()
    {
        if (stationPopup == null) return;

        TryImportScenePopup(stationPopup);

        var workerList = stationPopup.transform.Find(StationManagePopup.WorkerListName);
        if (workerList != null)
            workerListContainer = workerList;

        var productInfo = stationPopup.transform.Find(StationManagePopup.ProductInfoName);
        if (productInfo != null)
            productInfoText = productInfo.GetComponent<TextMeshProUGUI>();

        var productList = stationPopup.transform.Find(StationManagePopup.ProductListName);
        if (productList != null)
            productListContainer = productList;

        var inventoryInfo = stationPopup.transform.Find(StationManagePopup.InventoryInfoName);
        if (inventoryInfo != null)
            inventoryInfoText = inventoryInfo.GetComponent<TextMeshProUGUI>();

        var stationTitle = stationPopup.transform.Find(StationManagePopup.StationTitleName);
        if (stationTitle != null)
            stationTitleText = stationTitle.GetComponent<TextMeshProUGUI>();

        var workerInfo = stationPopup.transform.Find(StationManagePopup.WorkerInfoName);
        if (workerInfo != null)
            workerInfoText = workerInfo.GetComponent<TextMeshProUGUI>();

        var outputInfo = stationPopup.transform.Find(StationManagePopup.OutputInfoName);
        if (outputInfo != null)
            outputInfoText = outputInfo.GetComponent<TextMeshProUGUI>();

        var status = stationPopup.transform.Find(StationManagePopup.StatusName);
        if (status != null)
            statusText = status.GetComponent<TextMeshProUGUI>();

        foreach (var btn in stationPopup.GetComponentsInChildren<Button>(true))
        {
            switch (btn.gameObject.name)
            {
                case "Assign Worker": assignWorkerButton = btn; break;
                case "Clear Worker": clearWorkerButton = btn; break;
                case "Assign Output": assignOutputButton = btn; break;
                case "Clear Output": clearOutputButton = btn; break;
            }
        }

        var directLabels = new List<TextMeshProUGUI>();
        for (int i = 0; i < stationPopup.transform.childCount; i++)
        {
            var child = stationPopup.transform.GetChild(i);
            var label = child.GetComponent<TextMeshProUGUI>();
            if (label != null)
                directLabels.Add(label);
        }

        if (stationTitleText == null && directLabels.Count > 0) stationTitleText = directLabels[0];
        if (workerInfoText == null && directLabels.Count > 1) workerInfoText = directLabels[1];
        if (outputInfoText == null && directLabels.Count > 2) outputInfoText = directLabels[2];
        if (statusText == null && directLabels.Count > 3) statusText = directLabels[3];
    }

    void WirePopupButtons()
    {
        WireButton(assignWorkerButton, OnAssignWorkerClicked);
        WireButton(clearWorkerButton, OnClearWorkerClicked);
        WireButton(assignOutputButton, OnAssignOutputClicked);
        WireButton(clearOutputButton, OnClearOutputClicked);
    }

    static void WireButton(Button btn, UnityEngine.Events.UnityAction onClick)
    {
        if (btn == null || onClick == null) return;
        btn.onClick.RemoveListener(onClick);
        btn.onClick.AddListener(onClick);
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
