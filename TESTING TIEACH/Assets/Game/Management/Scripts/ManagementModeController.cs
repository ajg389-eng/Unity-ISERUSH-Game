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
    public Texture2D timeToCompleteIcon;
    public Texture2D efficiencyIcon;

    GameObject productionDiagramRoot;
    GameObject productionInputCard;
    GameObject productionSecondInputCard;
    GameObject productionConversionRoot;
    GameObject productionOutputCard;
    GameObject productionSecondOutputCard;
    RawImage productionInputPreview;
    RawImage productionSecondInputPreview;
    RawImage productionOutputPreview;
    RawImage productionSecondOutputPreview;
    TextMeshProUGUI productionInputName;
    TextMeshProUGUI productionInputRate;
    TextMeshProUGUI productionSecondInputName;
    TextMeshProUGUI productionSecondInputRate;
    TextMeshProUGUI productionOutputName;
    TextMeshProUGUI productionOutputRate;
    TextMeshProUGUI productionSecondOutputName;
    TextMeshProUGUI productionSecondOutputRate;
    TextMeshProUGUI productionCycleText;
    TextMeshProUGUI productionEfficiencyText;
    TextMeshProUGUI productionArrowText;
    GameObject stationDiagnosticsRoot;
    TextMeshProUGUI stationDiagnosticsText;
    GameObject pickupInventoryRoot;
    TextMeshProUGUI pickupStockText;
    readonly RawImage[] pickupSlotPreviews = new RawImage[4];
    readonly TextMeshProUGUI[] pickupSlotLabels = new TextMeshProUGUI[4];
    readonly TextMeshProUGUI[] pickupSlotFreshnessLabels = new TextMeshProUGUI[4];

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
    readonly List<ProductionFlowConnection> editBackupConnections = new List<ProductionFlowConnection>();
    readonly Dictionary<GameObject, GameObject> editBackupOutputs = new Dictionary<GameObject, GameObject>();
    readonly List<StationSelectionHighlight> flowCaptureHighlights = new List<StationSelectionHighlight>();
    StationNode activeFlowNode;

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

    /// <summary>Select a flow from the Staff UI and focus its complete worker team.</summary>
    public void SelectFlow(ProductionFlowPlan flow)
    {
        if (!IsManageMode || flow == null) return;

        if (selectedWorkerHighlight != null)
        {
            selectedWorkerHighlight.SetHovered(false);
            selectedWorkerHighlight = null;
        }
        selectedEmployee = null;
        SetWorkflowDecisionHudVisible(false);
        WorkerAssignmentLinkVisuals.SetFocusedFlow(flow);
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
            RefreshStationDiagnostics();
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
        if (IsCapturingFlow)
        {
            CustomerWallDoor.HideActivePopup();
            StationNode flowStation = FindFlowStationUnderRay(ray);
            if (flowStation != null)
                BeginOutputDrag(flowStation);
            return;
        }
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

        // Station output assignment is intentionally disabled. Routing belongs to flows.
        if (pending == PendingAction.PickOutput)
        {
            pending = PendingAction.None;
            SetStatus("Station outputs are controlled by the assigned flow.");
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
            if (IsCapturingFlow)
                target = FindFlowStationUnderRay(ray);
            else if (Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
                target = StationNode.FindFromCollider(hit.collider);
        }

        CancelOutputDrag();

        if (IsCapturingFlow)
        {
            if (!wasDrag)
            {
                AddCapturedFlowStation(source);
                return;
            }
            if (source == null || target == null)
            {
                SetStatus("Flow connection cancelled. Release over another station.");
                return;
            }
            TryConnectCapturedFlowStations(source, target);
            return;
        }

        if (!wasDrag || source == null)
            return;

        if (target == null)
        {
            SetStatus("Output link cancelled. Release over another station.");
            return;
        }

        ApplyOutputLink(source, target);
    }

    void BeginOutputDrag(StationNode source)
    {
        if (!IsCapturingFlow && source != null && !CanHaveOutput(source))
        {
            CancelOutputDrag();
            SetStatus(source.DisplayName + " is an endpoint and cannot send output onward.");
            return;
        }

        outputDragSource = source;
        outputDragStartScreen = Input.mousePosition;
        outputDragActive = source != null;
        outputDragMoved = false;
        EnsureOutputDragPreview();
        SetStatus(IsCapturingFlow
            ? "Drag from " + source.DisplayName + " and release on the next station in this flow."
            : "Drag from this station to a compatible station, then release to set its output.");
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
        StationNode hoveredTarget = null;
        if (IsCapturingFlow)
        {
            hoveredTarget = FindFlowStationUnderRay(ray);
            if (hoveredTarget != null)
                to = KitchenEmployee.GetInteractionPosition(hoveredTarget.gameObject);
            else
            {
                GridManager grid = GridManager.Instance;
                float floorY = grid != null ? grid.Origin.y : from.y;
                var floorPlane = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
                if (floorPlane.Raycast(ray, out float enter))
                    to = ray.GetPoint(enter);
            }
        }
        else if (Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            hoveredTarget = StationNode.FindFromCollider(hit.collider);
            to = hoveredTarget != null ? GetStationAnchor(hoveredTarget.gameObject) : hit.point;
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

        outputDragPreview.enabled = true;
        if (IsCapturingFlow)
        {
            GridManager grid = GridManager.Instance;
            if (grid != null)
            {
                from = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(outputDragSource.gameObject));
                to = grid.GetCellCenter(to);
                float y = grid.Origin.y + 0.08f;
                from.y = y;
                to.y = y;
                List<Vector3> previewPath = grid.GetPath(from, to);
                if (previewPath == null) previewPath = new List<Vector3>();
                if (previewPath.Count == 0) previewPath.Add(from);
                if (Vector3.SqrMagnitude(previewPath[0] - from) > 0.001f) previewPath.Insert(0, from);
                if (Vector3.SqrMagnitude(previewPath[previewPath.Count - 1] - to) > 0.001f) previewPath.Add(to);
                for (int i = 0; i < previewPath.Count; i++)
                {
                    Vector3 point = previewPath[i];
                    point.y = y;
                    previewPath[i] = point;
                }
                outputDragPreview.positionCount = previewPath.Count;
                outputDragPreview.SetPositions(previewPath.ToArray());
            }
        }
        else
        {
            float bridgeY = Mathf.Max(from.y, to.y) + 1.5f;
            outputDragPreview.positionCount = 4;
            outputDragPreview.SetPosition(0, from);
            outputDragPreview.SetPosition(1, new Vector3(from.x, bridgeY, from.z));
            outputDragPreview.SetPosition(2, new Vector3(to.x, bridgeY, to.z));
            outputDragPreview.SetPosition(3, to);
        }

        Color previewColor = new Color(1f, 0.7f, 0.2f, 0.9f);
        if (hoveredTarget != null)
            previewColor = (IsCapturingFlow
                ? CanConnectCapturedFlowStations(outputDragSource, hoveredTarget, out _)
                : CanLinkOutput(outputDragSource, hoveredTarget, out _))
                ? new Color(0.35f, 0.9f, 0.58f, 0.95f)
                : new Color(0.95f, 0.28f, 0.25f, 0.95f);
        outputDragPreview.startColor = previewColor;
        outputDragPreview.endColor = previewColor;
        if (outputDragPreview.material != null)
            outputDragPreview.material.color = previewColor;
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
        if (!CanLinkOutput(from, to, out string reason))
        {
            SetStatus(reason);
            return;
        }

        from.SetOutput(to.gameObject);
        Sfx.Play(SfxId.AssignOutput);
        if (selectedStation == from)
            RefreshPopup();
        StationOutputLinkVisuals.NotifyLinksChanged();
        SetStatus($"Output set: {from.DisplayName} → {to.DisplayName}");
    }

    bool CanLinkOutput(StationNode from, StationNode to, out string reason, bool checkLegacyCycle = true)
    {
        reason = "That output link is not valid.";
        if (from == null || to == null)
        {
            reason = "Release over another station to set the output.";
            return false;
        }
        if (from == to)
        {
            reason = "A station cannot send output to itself.";
            return false;
        }
        if (!CanHaveOutput(from))
        {
            reason = from.DisplayName + " is an endpoint and cannot have an output link.";
            return false;
        }
        if (checkLegacyCycle && WouldCreateOutputCycle(from, to))
        {
            reason = "That link would create a production loop.";
            return false;
        }

        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config == null)
        {
            reason = "Production recipes are not available.";
            return false;
        }

        GrillStation sourceGrill = from.GetComponent<GrillStation>();
        AssemblyStation sourceAssembly = from.GetComponent<AssemblyStation>();
        FreezerStation sourceFreezer = from.GetComponent<FreezerStation>();
        PantryStation sourcePantry = from.GetComponent<PantryStation>();
        if (sourceGrill != null && !sourceGrill.HasProductSelected)
        {
            reason = "Choose the Grill recipe before assigning its output.";
            return false;
        }
        if (sourceAssembly != null && sourceAssembly.GetSelectedRecipe() == null)
        {
            reason = "Choose the Assembly recipe before assigning its output.";
            return false;
        }
        if (sourceFreezer != null && !sourceFreezer.HasItemSelected)
        {
            reason = "Choose the Freezer ingredient before assigning its output.";
            return false;
        }
        if (sourcePantry != null && !sourcePantry.HasItemSelected)
        {
            reason = "Choose the Pantry ingredient before assigning its output.";
            return false;
        }

        GrillStation targetGrill = to.GetComponent<GrillStation>();
        AssemblyStation targetAssembly = to.GetComponent<AssemblyStation>();
        if (targetGrill != null && !targetGrill.HasProductSelected)
        {
            reason = "Choose the destination Grill recipe first.";
            return false;
        }
        if (targetAssembly != null && targetAssembly.GetSelectedRecipe() == null)
        {
            reason = "Choose the destination Assembly recipe first.";
            return false;
        }

        if (sourcePantry != null)
            return CanPantryFeed(sourcePantry, to, config, out reason);

        ItemDefinition produced = GetStationOutputItem(from, config);
        if (produced == null)
        {
            reason = from.DisplayName + " has no recipe output to route.";
            return false;
        }

        if (!CanStationAccept(to, produced, config))
        {
            reason = to.DisplayName + " cannot accept " + DisplayItemName(produced) + ".";
            return false;
        }

        reason = null;
        return true;
    }

    static bool CanHaveOutput(StationNode node)
    {
        return node != null
            && node.GetComponent<HeatLampStation>() == null
            && node.GetComponent<Register>() == null;
    }

    static bool WouldCreateOutputCycle(StationNode source, StationNode target)
    {
        var visited = new HashSet<StationNode>();
        StationNode current = target;
        while (current != null && visited.Add(current))
        {
            if (current == source) return true;
            GameObject next = current.outputTarget;
            current = next != null ? next.GetComponent<StationNode>() : null;
        }
        return false;
    }

    static ItemDefinition GetStationOutputItem(StationNode station, CustomerOrderConfig config)
    {
        if (station == null || config == null) return null;
        FreezerStation freezer = station.GetComponent<FreezerStation>();
        if (freezer != null) return freezer.selectedItem;
        PantryStation pantry = station.GetComponent<PantryStation>();
        if (pantry != null) return pantry.selectedItem;
        GrillStation grill = station.GetComponent<GrillStation>();
        if (grill != null) return grill.GetSelectedOutput();
        CuttingStation cutting = station.GetComponent<CuttingStation>();
        if (cutting != null) return cutting.GetSelectedRecipe()?.output;
        FryerStation fryer = station.GetComponent<FryerStation>();
        if (fryer != null) return fryer.GetSelectedOutput();
        if (station.GetComponent<DrinkStation>() != null) return config.drinkItem;
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        return assembly != null ? assembly.GetSelectedRecipe()?.output : null;
    }

    static bool CanPantryFeed(PantryStation pantry, StationNode target,
        CustomerOrderConfig config, out string reason)
    {
        reason = null;
        if (pantry != null && (CanPantryIngredientFeed(pantry.selectedItem, target, config)
            || CanPantryIngredientFeed(pantry.secondItem, target, config))) return true;
        reason = pantry != null && pantry.HasItemSelected
            ? "Neither Pantry ingredient matches this station's selected recipe."
            : "Choose what this Pantry stores first.";
        return false;
    }

    static bool CanPantryIngredientFeed(ItemDefinition stored, StationNode target, CustomerOrderConfig config)
    {
        if (stored == null)
        {
            return false;
        }
        if (target.GetComponent<CuttingStation>() != null)
        {
            CuttingStation cutting = target.GetComponent<CuttingStation>();
            if (cutting != null && cutting.CanProcess(stored)) return true;
            return false;
        }
        if (target.GetComponent<FryerStation>() != null)
        {
            FryerStation fryer = target.GetComponent<FryerStation>();
            StationProcessingRecipeDefinition fryerRecipe = fryer != null
                ? config.GetFryerRecipe(fryer.GetSelectedOutput()) : null;
            ItemDefinition required = fryerRecipe != null ? fryerRecipe.input : config.slicedPotatoIngredient;
            if (stored == required) return true;
            return false;
        }

        AssemblyStation assembly = target.GetComponent<AssemblyStation>();
        AssemblyRecipeDefinition recipe = assembly != null ? assembly.GetSelectedRecipe() : null;
        if (recipe != null && ((recipe.pantryInput == stored && recipe.rawPantryInput == null)
            || (recipe.processedInputFromPantry && recipe.processedInput == stored)
            || (recipe.thirdInput == stored && recipe.rawThirdInput == null
                && (recipe.thirdSupplyPipeline == null || recipe.thirdSupplyPipeline.Length == 0))))
            return true;

        return false;
    }

    static bool CanStationAccept(StationNode target, ItemDefinition item, CustomerOrderConfig config)
    {
        if (target == null || item == null || config == null) return false;
        if (target.GetComponent<GrillStation>() != null)
        {
            GrillStation grill = target.GetComponent<GrillStation>();
            return grill != null && item == grill.GetSelectedInput();
        }
        CuttingStation cutting = target.GetComponent<CuttingStation>();
        if (cutting != null)
            return cutting.CanProcess(item);
        if (target.GetComponent<FryerStation>() != null)
        {
            FryerStation fryer = target.GetComponent<FryerStation>();
            StationProcessingRecipeDefinition fryerRecipe = fryer != null
                ? config.GetFryerRecipe(fryer.GetSelectedOutput()) : null;
            return fryerRecipe != null ? item == fryerRecipe.input : item == config.slicedPotatoIngredient;
        }

        AssemblyStation assembly = target.GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            if (recipe == null) return false;
            ItemDefinition processedInput = recipe.processedInput != null
                ? recipe.processedInput : config.cookedPattyIngredient;
            return item == processedInput || item == recipe.pantryInput
                || (assembly.IsMk2 && item == recipe.thirdInput);
        }

        if (target.GetComponent<HeatLampStation>() != null)
            return config.IsBurger(item) || config.IsFries(item) || config.IsDrink(item);

        return false;
    }

    void RevalidateSelectedStationOutput()
    {
        if (selectedStation == null || selectedStation.outputTarget == null) return;
        StationNode target = selectedStation.outputTarget.GetComponent<StationNode>();
        if (CanLinkOutput(selectedStation, target, out _)) return;
        selectedStation.ClearOutput();
        StationOutputLinkVisuals.NotifyLinksChanged();
    }

    void SelectEmployeeForAssignment(KitchenEmployee emp)
    {
        if (emp == null) return;

        // Cancel output picking if we switch to worker assignment.
        if (pending == PendingAction.PickOutput)
            pending = PendingAction.None;

        SetSelectedEmployee(emp);
        ClearSelection();
        SetStatus(emp.employeeName + " selected — click any station to assign them.");
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

        FocusEmployeeFlow(selectedEmployee);
        RefreshWorkflowDecisionHud();
    }

    void FocusEmployeeFlow(KitchenEmployee employee)
    {
        ProductionManager production = ProductionManager.Instance != null
            ? ProductionManager.Instance
            : FindObjectOfType<ProductionManager>();
        ProductionFlowPlan flow = production != null ? production.GetFlowForWorker(employee) : null;
        if (production != null && flow != null)
        {
            int index = production.productionFlows.IndexOf(flow);
            if (index >= 0)
                production.SelectProductionFlow(index);
            WorkerAssignmentLinkVisuals.SetFocusedFlow(flow);
        }
        else
        {
            WorkerAssignmentLinkVisuals.SetFocusedWorker(employee);
        }

        WorkersUI workersUi = FindObjectOfType<WorkersUI>();
        if (workersUi != null && workersUi.isActiveAndEnabled)
            workersUi.Refresh();
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

        node.SetWorker(emp);
        Sfx.Play(SfxId.AssignWorker);
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.WorkerAssigned);
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();

        SetStatus(emp.employeeName + " assigned to " + node.DisplayName +
                  ". Click another station or Esc.");
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
        RefreshStationDiagnostics();
        ApplyPanelLayout(isHeatLamp);
    }

    void EnsureStationDiagnosticsUI()
    {
        if (stationPopup == null || stationDiagnosticsRoot != null) return;

        stationDiagnosticsRoot = new GameObject("StationDiagnostics", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement));
        stationDiagnosticsRoot.transform.SetParent(stationPopup.transform, false);
        stationDiagnosticsRoot.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.115f, 0.94f);
        var size = stationDiagnosticsRoot.GetComponent<LayoutElement>();
        size.minHeight = 62f;
        size.preferredHeight = 62f;
        size.flexibleHeight = 0f;

        var labelObject = new GameObject("DiagnosticsText", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(stationDiagnosticsRoot.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 5f);
        labelRect.offsetMax = new Vector2(-8f, -5f);
        stationDiagnosticsText = labelObject.GetComponent<TextMeshProUGUI>();
        stationDiagnosticsText.fontSize = 10.5f;
        stationDiagnosticsText.alignment = TextAlignmentOptions.TopLeft;
        stationDiagnosticsText.color = new Color(0.91f, 0.93f, 0.94f, 1f);
        stationDiagnosticsText.richText = true;
        stationDiagnosticsText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) stationDiagnosticsText.font = TMP_Settings.defaultFontAsset;
    }

    void RefreshStationDiagnostics()
    {
        EnsureStationDiagnosticsUI();
        if (stationDiagnosticsRoot == null || stationDiagnosticsText == null) return;
        bool show = selectedStation != null;
        stationDiagnosticsRoot.SetActive(show);
        if (!show) return;

        StationRuntimeMetrics metrics = StationRuntimeMetrics.EnsureOn(selectedStation.gameObject);
        string state = metrics != null ? metrics.CurrentState.ToString() : "Idle";
        if (productionEfficiencyText != null)
            productionEfficiencyText.text = metrics != null && metrics.TotalSeconds >= 1f
                ? metrics.WorkingPercent.ToString("0") + "%" : "0%";
        string utilization = metrics != null && metrics.TotalSeconds >= 1f
            ? "Working " + metrics.WorkingPercent.ToString("0") + "%  |  Blocked "
                + metrics.BlockedPercent.ToString("0") + "%  |  Starved "
                + metrics.StarvedPercent.ToString("0") + "%  |  Idle "
                + metrics.IdlePercent.ToString("0") + "%"
            : "Collecting utilization data...";

        ProductionManager manager = ProductionManager.Instance;
        int incoming = manager != null ? manager.GetReservedInputCount(selectedStation.gameObject) : 0;
        int outgoing = manager != null ? manager.GetReservedOutputCount(selectedStation.gameObject) : 0;
        string buffer = BuildBufferSummary(selectedStation, incoming, outgoing);
        stationDiagnosticsText.text = "<b>STATE: " + state.ToUpperInvariant() + "</b>\n"
            + utilization + "\n" + buffer;
    }

    static string BuildBufferSummary(StationNode node, int incoming, int outgoing)
    {
        if (node == null) return "Buffer unavailable";
        AssemblyStation assembly = node.GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            return "Buffers: input A " + assembly.BufferedProcessedInputCount + "/" + assembly.IngredientCapacity
                + "  |  input B " + assembly.BufferedPantryInputCount + "/" + assembly.IngredientCapacity
                + "  |  output " + assembly.BufferedOutputCount + "/" + assembly.OutputSlotCapacity
                + "  |  reserved in " + incoming + ", out " + outgoing;
        }

        GrillStation grill = node.GetComponent<GrillStation>();
        if (grill != null)
            return "Buffer: " + grill.BufferedPattyCount + "/" + grill.InputSlotCapacity
                + (grill.IsCooked() ? " ready" : (grill.IsCookingPatty ? " cooking" : " empty"))
                + "  |  reserved in " + incoming + ", out " + outgoing;

        HeatLampStation pickup = node.GetComponent<HeatLampStation>();
        if (pickup != null)
            return "Buffer: " + pickup.Count + "/" + pickup.maxCapacity
                + "  |  reserved in " + incoming + ", out " + outgoing;

        FreezerStation freezer = node.GetComponent<FreezerStation>();
        if (freezer != null)
        {
            int stock = freezer.selectedItem != null
                ? freezer.GetOutputCount(freezer.selectedItem) : 0;
            return "Output stock: " + stock + "  |  reserved out " + outgoing;
        }

        IStationBuffer stationBuffer = node.GetComponent<IStationBuffer>();
        if (stationBuffer != null)
            return "Capacity: input " + stationBuffer.InputSlotCapacity + "  |  output "
                + stationBuffer.OutputSlotCapacity + "  |  reserved in " + incoming + ", out " + outgoing;
        return "No local buffer  |  reserved in " + incoming + ", out " + outgoing;
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
        productionSecondInputCard = CreateProductionCard(productionDiagramRoot.transform, "Input 2",
            out productionSecondInputPreview, out productionSecondInputName, out productionSecondInputRate);
        productionSecondInputCard.SetActive(false);

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

        productionArrowText = CreateDiagramText(productionConversionRoot.transform, "Arrow", ">", 30f, 40f);
        productionArrowText.color = new Color(0.3f, 0.9f, 1f, 1f);
        productionCycleText = CreateIconMetric(productionConversionRoot.transform, "CycleTime",
            timeToCompleteIcon, "0s", 44f, vertical: true);
        productionCycleText.color = new Color(1f, 0.78f, 0.32f, 1f);
        productionEfficiencyText = CreateIconMetric(productionConversionRoot.transform, "Efficiency",
            efficiencyIcon, "0%", 44f, vertical: true);
        productionEfficiencyText.color = new Color(1f, 0.78f, 0.32f, 1f);

        productionOutputCard = CreateProductionCard(productionDiagramRoot.transform, "Output", out productionOutputPreview,
            out productionOutputName, out productionOutputRate);
        productionSecondOutputCard = CreateProductionCard(productionDiagramRoot.transform, "Output 2", out productionSecondOutputPreview,
            out productionSecondOutputName, out productionSecondOutputRate);
        productionSecondOutputCard.SetActive(false);
        productionDiagramRoot.SetActive(false);
    }

    GameObject CreateProductionCard(Transform parent, string heading, out RawImage preview,
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
        previewFrame.GetComponent<Image>().color = ItemPreviewThumbnails.BackgroundColor;
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
        rate = CreateDiagramText(card.transform, "Rate", "0/min", 12f, 20f);
        rate.color = new Color(1f, 0.78f, 0.32f, 1f);
        return card;
    }

    static TextMeshProUGUI CreateIconMetric(Transform parent, string objectName, Texture icon,
        string value, float height, bool vertical)
    {
        var root = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        var rootSize = root.GetComponent<LayoutElement>();
        rootSize.minHeight = height;
        rootSize.preferredHeight = height;
        rootSize.flexibleHeight = 0f;

        if (vertical)
        {
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 1f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }
        else
        {
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        iconObject.transform.SetParent(root.transform, false);
        var image = iconObject.GetComponent<RawImage>();
        image.texture = icon;
        image.uvRect = new Rect(0.18f, 0.18f, 0.64f, 0.64f);
        image.color = Color.white;
        image.raycastTarget = false;
        var iconSize = iconObject.GetComponent<LayoutElement>();
        iconSize.minWidth = vertical ? 24f : 18f;
        iconSize.preferredWidth = vertical ? 24f : 18f;
        iconSize.minHeight = vertical ? 24f : 18f;
        iconSize.preferredHeight = vertical ? 24f : 18f;

        TextMeshProUGUI text = CreateDiagramText(root.transform, "Value", value, 11f,
            vertical ? 18f : height);
        return text;
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
        GameObject secondInputPrefab = null;
        GameObject outputPrefab = null;
        string inputName = string.IsNullOrEmpty(node.inputUnit) || node.inputUnit == "-"
            ? "Kitchen stock" : ToTitleCase(node.inputUnit);
        string secondInputName = string.Empty;
        string outputName = string.IsNullOrEmpty(node.outputUnit)
            ? "Items" : ToTitleCase(node.outputUnit);
        float inputRate = node.HasInputAmount ? node.inputAmountPerMinute : node.outputAmountPerMinute;
        float secondInputRate = inputRate;
        float cycleSeconds = 0f;

        var freezer = node.GetComponent<FreezerStation>();
        var grill = node.GetComponent<GrillStation>();
        var assembly = node.GetComponent<AssemblyStation>();
        var cutting = node.GetComponent<CuttingStation>();
        var fryer = node.GetComponent<FryerStation>();
        var drink = node.GetComponent<DrinkStation>();
        var pantry = node.GetComponent<PantryStation>();

        bool outputOnly = false;
        CustomerOrderConfig modelConfig = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        GameObject rawPattyModel = modelConfig != null && modelConfig.rawPattyIngredient != null
            && modelConfig.rawPattyIngredient.prefab != null
            ? modelConfig.rawPattyIngredient.prefab : rawPattyPreviewPrefab;
        GameObject cookedPattyModel = modelConfig != null && modelConfig.cookedPattyIngredient != null
            && modelConfig.cookedPattyIngredient.prefab != null
            ? modelConfig.cookedPattyIngredient.prefab : cookedPattyPreviewPrefab;
        if (freezer != null)
        {
            ItemDefinition stored = freezer.selectedItem;
            outputPrefab = stored != null && stored.prefab != null ? stored.prefab : rawPattyModel;
            outputName = stored != null ? DisplayItemName(stored) : "Select ingredient";
            cycleSeconds = freezer.processTimeSeconds;
            outputOnly = true;
        }
        else if (grill != null)
        {
            ItemDefinition grillInput = grill.GetSelectedInput();
            ItemDefinition grillOutput = grill.GetSelectedOutput();
            inputPrefab = grillInput != null && grillInput.prefab != null ? grillInput.prefab : rawPattyModel;
            outputPrefab = grillOutput != null && grillOutput.prefab != null ? grillOutput.prefab : cookedPattyModel;
            inputName = grillInput != null ? DisplayItemName(grillInput) : "Select recipe";
            outputName = grillOutput != null ? DisplayItemName(grillOutput) : "Select recipe";
            cycleSeconds = grill.processTimeSeconds;
        }
        else if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            inputPrefab = recipe != null && recipe.processedInput != null
                ? recipe.processedInput.prefab : cookedPattyModel;
            outputPrefab = recipe != null && recipe.output != null
                ? (recipe.output.prefab ?? GetPickupPreviewPrefab(recipe.output))
                : burgerPreviewPrefab;
            inputName = recipe != null ? recipe.processedInputName : "Cooked patty";
            secondInputPrefab = recipe != null && recipe.pantryInput != null ? recipe.pantryInput.prefab : null;
            secondInputName = recipe != null && recipe.pantryInput != null
                ? DisplayItemName(recipe.pantryInput) : "Bun";
            inputRate = node.outputAmountPerMinute * (recipe != null ? Mathf.Max(1, recipe.processedInputAmount) : 1);
            secondInputRate = node.outputAmountPerMinute * (recipe != null ? Mathf.Max(1, recipe.pantryInputAmount) : 1);
            outputName = recipe != null ? recipe.DisplayName
                : (assembly.selectedProduct != null ? DisplayItemName(assembly.selectedProduct) : "Burger");
            cycleSeconds = assembly.processTimeSeconds;
        }
        else if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            inputPrefab = recipe != null && recipe.input != null ? recipe.input.prefab : null;
            outputPrefab = recipe != null && recipe.output != null ? recipe.output.prefab : null;
            inputName = recipe != null ? DisplayItemName(recipe.input) : "Select ingredient";
            outputName = recipe != null ? recipe.DisplayName : "Select recipe";
            cycleSeconds = cutting.processTimeSeconds;
        }
        else if (fryer != null)
        {
            ItemDefinition fryerOutput = fryer.GetSelectedOutput();
            StationProcessingRecipeDefinition fryerRecipe = modelConfig != null
                ? modelConfig.GetFryerRecipe(fryerOutput) : null;
            ItemDefinition fryerInput = fryerRecipe != null ? fryerRecipe.input : null;
            inputPrefab = fryerInput != null && fryerInput.prefab != null
                ? fryerInput.prefab : rawFriesPreviewPrefab;
            outputPrefab = fryerOutput != null && fryerOutput.prefab != null
                ? fryerOutput.prefab : cookedFriesPreviewPrefab;
            inputName = fryerInput != null ? DisplayItemName(fryerInput) : "Potato slices";
            outputName = fryerOutput != null ? DisplayItemName(fryerOutput) : "Cooked Potato Slices";
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
            ItemDefinition stored = pantry.selectedItem;
            outputPrefab = stored != null ? stored.prefab : null;
            outputName = stored != null ? DisplayItemName(stored) : "Not selected";
            cycleSeconds = pantry.processTimeSeconds;
            outputOnly = true;
        }
        else
        {
            productionDiagramRoot.SetActive(false);
            return;
        }

        productionDiagramRoot.SetActive(true);
        bool hasTwoInputs = assembly != null;
        SetProductionDiagramMode(outputOnly, hasTwoInputs, pantry != null);
        if (!outputOnly)
            SetDiagramPreview(productionInputPreview, inputPrefab, inputName);
        if (hasTwoInputs)
            SetDiagramPreview(productionSecondInputPreview, secondInputPrefab, secondInputName);
        SetDiagramPreview(productionOutputPreview, outputPrefab, outputName);
        if (!outputOnly)
        {
            productionInputName.text = inputName;
            productionInputRate.text = FormatPerMinute(inputRate) + "/min";
        }
        if (hasTwoInputs)
        {
            productionSecondInputName.text = secondInputName;
            productionSecondInputRate.text = FormatPerMinute(secondInputRate) + "/min";
        }
        productionOutputName.text = outputName;
        productionOutputRate.text = FormatPerMinute(node.outputAmountPerMinute) + "/min";
        if (pantry != null)
        {
            ItemDefinition second = pantry.secondItem;
            string secondName = second != null ? DisplayItemName(second) : "Not selected";
            SetDiagramPreview(productionSecondOutputPreview, second != null ? second.prefab : null, secondName);
            productionSecondOutputName.text = secondName;
            productionSecondOutputRate.text = second != null ? FormatPerMinute(node.outputAmountPerMinute) + "/min" : "—";
            if (pantry.selectedItem == null) productionOutputRate.text = "—";
        }
        productionCycleText.text = FormatSeconds(cycleSeconds) + "s";
    }

    void SetProductionDiagramMode(bool outputOnly, bool twoInputs = false, bool twoOutputs = false)
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
        if (productionSecondInputCard != null) productionSecondInputCard.SetActive(!outputOnly && twoInputs);
        if (productionSecondOutputCard != null) productionSecondOutputCard.SetActive(twoOutputs);
        if (productionConversionRoot != null) productionConversionRoot.SetActive(true);
        if (productionArrowText != null) productionArrowText.gameObject.SetActive(!outputOnly);
        if (productionOutputCard == null) return;

        var outputSize = productionOutputCard.GetComponent<LayoutElement>();
        if (outputSize == null) return;
        outputSize.minWidth = outputOnly ? 150f : 98f;
        outputSize.preferredWidth = outputOnly ? 180f : 104f;
        outputSize.flexibleWidth = outputOnly ? 0f : 1f;

        SetProductionCardWidth(productionInputCard, twoInputs ? 66f : 98f, twoInputs ? 76f : 104f);
        SetProductionCardWidth(productionSecondInputCard, 66f, 76f);
        SetProductionCardWidth(productionOutputCard, twoOutputs ? 98f : (outputOnly ? 150f : (twoInputs ? 76f : 98f)),
            twoOutputs ? 104f : (outputOnly ? 180f : (twoInputs ? 88f : 104f)));
        SetProductionCardWidth(productionSecondOutputCard, 98f, 104f);
        TextMeshProUGUI outputHeading = productionOutputCard.transform.Find("Heading")?.GetComponent<TextMeshProUGUI>();
        if (outputHeading != null) outputHeading.text = twoOutputs ? "OUTPUT 1" : "OUTPUT";
        if (productionInputCard != null)
        {
            TextMeshProUGUI heading = productionInputCard.transform.Find("Heading")?.GetComponent<TextMeshProUGUI>();
            if (heading != null) heading.text = twoInputs ? "INPUT 1" : "INPUT";
        }
    }

    static void SetProductionCardWidth(GameObject card, float min, float preferred)
    {
        if (card == null) return;
        LayoutElement size = card.GetComponent<LayoutElement>();
        if (size == null) return;
        size.minWidth = min;
        size.preferredWidth = preferred;
        size.flexibleWidth = 1f;
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

    void ApplyPopupLayout(RectTransform rt, float width, float height)
    {
        if (rt == null) return;
        rt.anchorMin = popupAnchor;
        rt.anchorMax = popupAnchor;
        rt.pivot = popupPivot;
        rt.anchoredPosition = popupAnchoredPosition;
        rt.sizeDelta = new Vector2(width, height);
    }

    void ApplyPanelLayout(bool heatLampCompact)
    {
        if (stationPopup == null) return;

        bool hasRecipeControls = productListContainer != null
            && productListContainer.gameObject.activeSelf;

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
        CollapseInactiveLayout(stationDiagnosticsRoot);

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
            // Recipe cards are 106 px tall. Do not collapse their container after
            // RefreshProductSection has sized it, or later siblings overlap them.
            listLe.minHeight = Mathf.Max(122f, listLe.minHeight);
            listLe.preferredHeight = Mathf.Max(122f, listLe.preferredHeight);
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
            if (stationDiagnosticsRoot != null) stationDiagnosticsRoot.transform.SetSiblingIndex(2);
            if (statusText != null) statusText.transform.SetSiblingIndex(3);
        }
        else
        {
            if (productionDiagramRoot != null) productionDiagramRoot.transform.SetSiblingIndex(1);
            if (productInfoText != null) productInfoText.transform.SetSiblingIndex(2);
            if (productListContainer != null) productListContainer.SetSiblingIndex(3);
            if (stationDiagnosticsRoot != null) stationDiagnosticsRoot.transform.SetSiblingIndex(4);
            if (statusText != null) statusText.transform.SetSiblingIndex(5);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        float minimumHeight = heatLampCompact ? 246f : 286f;
        float preferredHeight = LayoutUtility.GetPreferredHeight(rt);
        float height = Mathf.Max(minimumHeight, preferredHeight);
        float width = hasRecipeControls ? 340f : 300f;

        if (!usingScenePopup)
            ApplyPopupLayout(rt, width, height);
        else
            rt.sizeDelta = new Vector2(Mathf.Max(width, rt.sizeDelta.x), height);

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
            inventorySize.minHeight = 128f;
            inventorySize.preferredHeight = 128f;
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

            TextMeshProUGUI freshnessLabel = pickupSlotFreshnessLabels[i];
            if (freshnessLabel != null)
            {
                bool occupied = item != null && meal != null;
                int percentage = occupied
                    ? Mathf.RoundToInt(lamp.GetFreshnessModifier(meal) * 100f)
                    : 0;
                freshnessLabel.gameObject.SetActive(occupied);
                if (occupied)
                {
                    freshnessLabel.text = percentage > 0 ? $"+{percentage}%" : $"{percentage}%";
                    freshnessLabel.color = percentage > 0
                        ? new Color(0.45f, 0.86f, 0.62f, 1f)
                        : percentage < 0
                            ? new Color(1f, 0.48f, 0.36f, 1f)
                            : new Color(1f, 0.78f, 0.32f, 1f);
                }
            }
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
        rootSize.minHeight = 128f;
        rootSize.preferredHeight = 128f;
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
        rowSize.minHeight = 98f;
        rowSize.preferredHeight = 98f;
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
        previewFrame.GetComponent<Image>().color = ItemPreviewThumbnails.BackgroundColor;
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

        var freshness = CreateDiagramText(slot.transform, "Freshness", "+50%", 9f, 14f);
        freshness.fontStyle = FontStyles.Bold;
        freshness.color = new Color(0.45f, 0.86f, 0.62f, 1f);
        freshness.gameObject.SetActive(false);
        pickupSlotFreshnessLabels[index] = freshness;
    }

    GameObject GetPickupPreviewPrefab(ItemDefinition item)
    {
        if (item == null) return null;
        if (item.prefab != null) return item.prefab;
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

    static IEnumerable<ItemDefinition> GetCuttingOutputs(CustomerOrderConfig config)
    {
        if (config == null) yield break;
        foreach (CuttingRecipeDefinition recipe in config.GetCuttingRecipes())
            if (recipe != null && recipe.output != null)
                yield return recipe.output;
    }

    void RefreshProductSection()
    {
        EnsureProductUI();
        if (productInfoText == null || productListContainer == null) return;

        var grill = selectedStation != null ? selectedStation.GetComponent<GrillStation>() : null;
        var assembly = selectedStation != null ? selectedStation.GetComponent<AssemblyStation>() : null;
        var freezer = selectedStation != null ? selectedStation.GetComponent<FreezerStation>() : null;
        var pantry = selectedStation != null ? selectedStation.GetComponent<PantryStation>() : null;
        var cutting = selectedStation != null ? selectedStation.GetComponent<CuttingStation>() : null;
        var fryer = selectedStation != null ? selectedStation.GetComponent<FryerStation>() : null;
        bool show = grill != null || assembly != null || freezer != null || pantry != null
            || cutting != null || fryer != null;

        productInfoText.gameObject.SetActive(show);
        productListContainer.gameObject.SetActive(show);
        if (!show) return;

        ConfigureProductListLayout();

        ItemDefinition current = grill != null ? grill.GetSelectedOutput()
            : assembly != null ? assembly.selectedProduct
            : freezer != null ? freezer.selectedItem
            : pantry != null ? pantry.selectedItem
            : cutting != null ? cutting.selectedProduct
            : fryer != null ? fryer.GetSelectedOutput() : null;

        for (int i = productListContainer.childCount - 1; i >= 0; i--)
            Destroy(productListContainer.GetChild(i).gameObject);

        var pm = ProductionManager.Instance;
        var config = pm != null ? pm.orderConfig : null;
        if (config == null)
        {
            productListContainer.gameObject.SetActive(false);
            return;
        }

        productInfoText.text = freezer != null || pantry != null
            ? "SELECT STORED INGREDIENT" : "SELECT RECIPE";
        if (pantry != null)
            productInfoText.text = "SELECT UP TO 2 INGREDIENTS · CLICK SELECTED TO REMOVE";
        productInfoText.fontSize = 12f;
        productInfoText.fontStyle = FontStyles.Bold;
        productInfoText.alignment = TextAlignmentOptions.Center;
        productInfoText.color = new Color(0.78f, 0.82f, 0.88f, 1f);

        if (assembly != null)
        {
            Transform recipeRow = null;
            int recipeCount = 0;
            int recipeRows = 0;
            AssemblyRecipeDefinition currentRecipe = assembly.GetSelectedRecipe();
            bool shakeStation = assembly.GetComponent<ShakeStation>() != null;
            productInfoText.text = assembly.IsMk2
                ? "SELECT 3-INPUT MK2 RECIPE"
                : "SELECT 2-INPUT MK1 RECIPE";
            foreach (AssemblyRecipeDefinition recipe in config.GetAssemblyRecipes())
            {
                if (recipe == null) continue;
                if (recipe.RequiresMk2 != assembly.IsMk2) continue;
                bool shakeRecipe = config.IsDrink(recipe.output);
                if (shakeStation != shakeRecipe) continue;
                if (recipeCount % 3 == 0)
                {
                    recipeRow = CreateRecipeRow();
                    recipeRows++;
                }
                recipeCount++;
                bool selectedRecipe = currentRecipe == recipe;
                Button recipeButton = CreateRecipeCard(recipeRow, recipe, selectedRecipe);
                AssemblyRecipeDefinition capturedRecipe = recipe;
                recipeButton.onClick.AddListener(() => SetSelectedAssemblyRecipe(capturedRecipe));
            }

            LayoutElement listSize = productListContainer.GetComponent<LayoutElement>();
            if (listSize != null)
            {
                float height = Mathf.Max(122f,
                    recipeRows * 117f + Mathf.Max(0, recipeRows - 1) * 5f);
                listSize.minHeight = height;
                listSize.preferredHeight = height;
            }
            if (recipeCount == 0)
            {
                productInfoText.text = assembly.IsMk2
                    ? "MK2: no 3-input recipes unlocked yet"
                    : "MK1: no 2-input recipes available";
            }
            productListContainer.gameObject.SetActive(recipeCount > 0);
            return;
        }

        IEnumerable<ItemDefinition> options = freezer != null
            ? config.GetFreezerIngredients()
            : pantry != null ? config.GetPantryIngredients()
            : cutting != null ? GetCuttingOutputs(config)
            : fryer != null ? config.GetFryerProducts()
            : config.GetGrillProducts();
        Transform optionRow = null;
        int optionCount = 0;
        int rowCount = 0;
        foreach (var item in options)
        {
            if (item == null) continue;
            if (optionCount % 3 == 0)
            {
                optionRow = CreateRecipeRow();
                rowCount++;
            }
            optionCount++;
            bool selected = pantry != null ? pantry.CanDispense(item) : current == item;
            Button btn = CreateRecipeCard(optionRow, item, selected);
            var captured = item;
            btn.onClick.AddListener(() => SetSelectedProduct(captured));
        }

        LayoutElement grillListSize = productListContainer.GetComponent<LayoutElement>();
        if (grillListSize != null)
        {
            float height = Mathf.Max(122f, rowCount * 117f + Mathf.Max(0, rowCount - 1) * 5f);
            grillListSize.minHeight = height;
            grillListSize.preferredHeight = height;
        }
        productListContainer.gameObject.SetActive(optionCount > 0);
    }

    void ConfigureProductListLayout()
    {
        if (productListContainer == null) return;

        var vertical = productListContainer.GetComponent<VerticalLayoutGroup>();
        if (vertical == null)
            vertical = productListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.enabled = true;
        vertical.spacing = 5f;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
    }

    Transform CreateRecipeRow()
    {
        var row = new GameObject("RecipeCards", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.transform.SetParent(productListContainer, false);

        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var size = row.GetComponent<LayoutElement>();
        size.minHeight = 112f;
        size.preferredHeight = 112f;
        size.flexibleHeight = 0f;
        return row.transform;
    }

    Button CreateRecipeCard(Transform parent, AssemblyRecipeDefinition recipe, bool selected)
    {
        GameObject previewPrefab = recipe.output != null ? recipe.output.prefab : null;
        if (previewPrefab == null)
            previewPrefab = GetPickupPreviewPrefab(recipe.output);
        return CreateRecipeCardVisual(parent, recipe.DisplayName, previewPrefab, selected);
    }

    Button CreateRecipeCard(Transform parent, ItemDefinition product, bool selected)
    {
        string recipeName = DisplayItemName(product);
        GameObject previewPrefab = product != null ? product.prefab : null;
        if (previewPrefab == null)
            previewPrefab = GetPickupPreviewPrefab(product);
        return CreateRecipeCardVisual(parent, recipeName, previewPrefab, selected);
    }

    Button CreateRecipeCardVisual(Transform parent, string recipeDisplayName, GameObject previewPrefab, bool selected)
    {
        var card = new GameObject("Recipe_" + recipeDisplayName, typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(Outline));
        card.transform.SetParent(parent, false);

        var background = card.GetComponent<Image>();
        background.color = selected
            ? new Color(0.24f, 0.43f, 0.34f, 1f)
            : new Color(0.075f, 0.085f, 0.115f, 0.98f);

        var outline = card.GetComponent<Outline>();
        outline.effectColor = selected
            ? new Color(0.50f, 0.72f, 0.62f, 1f)
            : new Color(0.18f, 0.21f, 0.25f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        var button = card.GetComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.86f, 0.9f, 0.88f, 1f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        var cardLayout = card.GetComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(6, 6, 6, 5);
        cardLayout.spacing = 4f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        var cardSize = card.GetComponent<LayoutElement>();
        cardSize.minWidth = 96f;
        cardSize.preferredWidth = 108f;
        cardSize.flexibleWidth = 0f;
        cardSize.minHeight = 106f;
        cardSize.preferredHeight = 106f;

        var previewFrame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement));
        previewFrame.transform.SetParent(card.transform, false);
        previewFrame.GetComponent<Image>().color = ItemPreviewThumbnails.BackgroundColor;
        var previewSize = previewFrame.GetComponent<LayoutElement>();
        previewSize.minHeight = 70f;
        previewSize.preferredHeight = 70f;
        previewSize.flexibleHeight = 0f;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage),
            typeof(AspectRatioFitter));
        previewObject.transform.SetParent(previewFrame.transform, false);
        var preview = previewObject.GetComponent<RawImage>();
        preview.raycastTarget = false;
        var previewFitter = previewObject.GetComponent<AspectRatioFitter>();
        previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        previewFitter.aspectRatio = 1f;

        SetDiagramPreview(preview, previewPrefab, recipeDisplayName);

        TextMeshProUGUI recipeName = CreateDiagramText(card.transform, "RecipeName", recipeDisplayName, 12f, 20f);
        recipeName.fontStyle = FontStyles.Bold;
        recipeName.enableAutoSizing = true;
        recipeName.fontSizeMin = 9f;
        recipeName.fontSizeMax = 12f;
        return button;
    }

    void SetSelectedProduct(ItemDefinition item)
    {
        if (selectedStation == null || item == null) return;
        var grill = selectedStation.GetComponent<GrillStation>();
        if (grill != null)
        {
            grill.SetRecipeOutput(item);
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Grill recipe set to " + DisplayItemName(item));
            return;
        }
        var freezer = selectedStation.GetComponent<FreezerStation>();
        if (freezer != null)
        {
            freezer.SetStoredItem(item);
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Freezer set to store " + DisplayItemName(item));
            return;
        }
        var pantry = selectedStation.GetComponent<PantryStation>();
        if (pantry != null)
        {
            if (!pantry.ToggleStoredItem(item))
            {
                SetStatus("Pantry holds two ingredients. Deselect one before choosing another.");
                return;
            }
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Pantry: " + pantry.StoredItemsLabel);
            return;
        }
        var cutting = selectedStation.GetComponent<CuttingStation>();
        if (cutting != null)
        {
            var manager = ProductionManager.Instance;
            var config = manager != null ? manager.orderConfig : null;
            cutting.SetRecipe(config != null ? config.GetCuttingRecipe(item) : null);
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Cutting recipe set to " + DisplayItemName(item));
            return;
        }
        var fryer = selectedStation.GetComponent<FryerStation>();
        if (fryer != null)
        {
            fryer.SetRecipeOutput(item);
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Fryer recipe set to " + DisplayItemName(item));
            return;
        }
        var assembly = selectedStation.GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            var manager = ProductionManager.Instance;
            var config = manager != null ? manager.orderConfig : null;
            assembly.SetRecipe(config != null ? config.GetAssemblyRecipe(item) : null);
            RevalidateSelectedStationOutput();
            WorkerAssignmentLinkVisuals.NotifyLinksChanged();
            RefreshPopup();
            SetStatus("Assembly set to produce " + (item.itemName ?? item.name));
        }
    }

    void SetSelectedAssemblyRecipe(AssemblyRecipeDefinition recipe)
    {
        if (selectedStation == null || recipe == null) return;
        AssemblyStation assembly = selectedStation.GetComponent<AssemblyStation>();
        if (assembly == null) return;
        assembly.SetRecipe(recipe);
        RevalidateSelectedStationOutput();
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
        RefreshPopup();
        SetStatus("Assembly recipe set to " + recipe.DisplayName);
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
            var btn = CreateListButton(
                emp.employeeName + " (" + emp.OperatedStationCount + " stations)", true);
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
        pending = PendingAction.None;
        CancelOutputDrag();
        SetStatus("Station outputs are controlled by flows.");
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
        editBackupConnections.Clear();
        editBackupOutputs.Clear();
        activeFlowNode = null;

        if (isNew)
        {
            capturedFlow.stepIds.Clear();
            capturedFlow.stations.Clear();
            capturedFlow.connections.Clear();
            capturedFlow.graphInitialized = true;
        }
        else
        {
            // Rebuild GameObject route from saved steps if stations were lost (null refs, etc.).
            WorkerFlowAssigner.SynchronizeFlowRoute(capturedFlow);
            capturedFlow.EnsureLegacyConnections();

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
            foreach (ProductionFlowConnection connection in capturedFlow.connections)
                if (connection != null)
                    editBackupConnections.Add(new ProductionFlowConnection(connection.from, connection.to));
            if (capturedFlow.stations.Count > 0)
                activeFlowNode = StationNode.EnsureOn(capturedFlow.stations[capturedFlow.stations.Count - 1]);

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
            SetStatus("Drag from one station to the next to create the flow. Drag from the same station again to split it.");
        }
        else if (capturedFlow.stations.Count == 0)
        {
            SetStatus("Editing " + capturedFlow.flowName + ": drag between two stations to create the first path.");
        }
        else
        {
            SetStatus("Editing " + capturedFlow.flowName + ": drag station to station. Reuse a source node to create branches.");
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
        string routeProblem = ValidateFlowGraph(finished);
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
        editBackupConnections.Clear();
        editBackupOutputs.Clear();
        activeFlowNode = null;
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
            cancelled.connections = new List<ProductionFlowConnection>();
            foreach (ProductionFlowConnection connection in editBackupConnections)
                cancelled.connections.Add(new ProductionFlowConnection(connection.from, connection.to));
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
        editBackupConnections.Clear();
        editBackupOutputs.Clear();
        activeFlowNode = null;
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
        if (capturedFlow.stations.Contains(node.gameObject))
        {
            bool removeNode = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (removeNode)
            {
                capturedFlow.connections.RemoveAll(connection => connection == null
                    || connection.from == node.gameObject || connection.to == node.gameObject);
                capturedFlow.stations.Remove(node.gameObject);
                WorkerFlowAssigner.RebuildStepIdsFromStations(capturedFlow);
                activeFlowNode = capturedFlow.stations.Count > 0
                    ? StationNode.EnsureOn(capturedFlow.stations[capturedFlow.stations.Count - 1]) : null;
                SetStatus("Removed node " + node.DisplayName + " and its connected branches.");
                RefreshFlowCaptureHud();
                RefreshWorkersUi();
                return;
            }
            activeFlowNode = node;
            SetStatus("Selected " + node.DisplayName + ". Drag from it to another station to add a path.");
            RefreshFlowCaptureHud();
            RefreshWorkersUi();
            return;
        }
        if (capturedFlow.stations.Count >= WorkerFlowAssigner.MaxSteps)
        {
            SetStatus("This flow has reached the " + WorkerFlowAssigner.MaxSteps + " station limit.");
            return;
        }

        capturedFlow.stations.Add(node.gameObject);
        capturedFlow.stepIds.Add(stationId);
        activeFlowNode = node;
        WorkerFlowAssigner.RebuildStepIdsFromStations(capturedFlow);
        SetStatus("Added " + node.DisplayName + " as a flow node. Drag from it to create a path.");
        RefreshFlowCaptureHud();
        RefreshWorkersUi();
    }

    bool CanConnectCapturedFlowStations(StationNode from, StationNode to, out string reason)
    {
        reason = "That flow connection is not valid.";
        if (capturedFlow == null || from == null || to == null)
        {
            reason = "Release over another station to create a flow path.";
            return false;
        }
        if (from == to)
        {
            reason = "A flow node cannot connect to itself.";
            return false;
        }
        if (string.IsNullOrEmpty(WorkerFlowAssigner.GetStationId(from.gameObject))
            || string.IsNullOrEmpty(WorkerFlowAssigner.GetStationId(to.gameObject)))
        {
            reason = "Both ends of a flow path must be production stations.";
            return false;
        }
        int newNodes = (capturedFlow.stations.Contains(from.gameObject) ? 0 : 1)
            + (capturedFlow.stations.Contains(to.gameObject) ? 0 : 1);
        if (capturedFlow.stations.Count + newNodes > WorkerFlowAssigner.MaxSteps)
        {
            reason = "This flow has reached the " + WorkerFlowAssigner.MaxSteps + " station limit.";
            return false;
        }
        if (capturedFlow.HasConnection(from.gameObject, to.gameObject))
        {
            reason = "Those flow nodes are already connected.";
            return false;
        }
        if (WouldCreateFlowCycle(capturedFlow, from.gameObject, to.gameObject))
        {
            reason = "That path would create a loop. Production flows must remain acyclic.";
            return false;
        }
        if (!CanLinkOutput(from, to, out reason, false))
            return false;
        return true;
    }

    void TryConnectCapturedFlowStations(StationNode from, StationNode to)
    {
        if (!CanConnectCapturedFlowStations(from, to, out string reason))
        {
            SetStatus(reason);
            return;
        }
        AddCapturedFlowNodeIfMissing(from);
        AddCapturedFlowNodeIfMissing(to);
        if (!capturedFlow.AddConnection(from.gameObject, to.gameObject))
        {
            SetStatus("Those flow nodes are already connected.");
            return;
        }

        activeFlowNode = to;
        WorkerFlowAssigner.RebuildStepIdsFromStations(capturedFlow);
        SetStatus("Flow path added: " + from.DisplayName + " to " + to.DisplayName
            + ". Drag from " + from.DisplayName + " again to create a split.");
        RefreshFlowCaptureHud();
        RefreshWorkersUi();
    }

    void AddCapturedFlowNodeIfMissing(StationNode node)
    {
        if (capturedFlow == null || node == null || capturedFlow.stations.Contains(node.gameObject)) return;
        capturedFlow.stations.Add(node.gameObject);
        string stationId = WorkerFlowAssigner.GetStationId(node.gameObject);
        if (!string.IsNullOrEmpty(stationId)) capturedFlow.stepIds.Add(stationId);
    }

    static bool WouldCreateFlowCycle(ProductionFlowPlan flow, GameObject from, GameObject to)
    {
        if (flow == null || from == null || to == null || from == to) return true;
        var open = new Stack<GameObject>();
        var visited = new HashSet<GameObject>();
        open.Push(to);
        while (open.Count > 0)
        {
            GameObject current = open.Pop();
            if (current == from) return true;
            if (current == null || !visited.Add(current)) continue;
            foreach (GameObject next in flow.GetOutgoing(current))
                if (next != null) open.Push(next);
        }
        return false;
    }

    static string ValidateFlowGraph(ProductionFlowPlan flow)
    {
        if (flow == null || flow.stations == null || flow.stations.Count == 0)
            return "A flow needs at least one station.";
        flow.Clean();
        if (flow.stations.Count > 1 && flow.connections.Count == 0)
            return "Connect the flow nodes before finishing.";
        foreach (ProductionFlowConnection connection in flow.connections)
            if (connection != null && WouldCreateFlowCycleIgnoringEdge(flow, connection.from, connection.to, connection))
                return "This flow contains a loop. Remove the looping branch.";
        return "";
    }

    static bool WouldCreateFlowCycleIgnoringEdge(ProductionFlowPlan flow, GameObject from, GameObject to,
        ProductionFlowConnection ignored)
    {
        var open = new Stack<GameObject>();
        var visited = new HashSet<GameObject>();
        open.Push(to);
        while (open.Count > 0)
        {
            GameObject current = open.Pop();
            if (current == from) return true;
            if (current == null || !visited.Add(current)) continue;
            foreach (ProductionFlowConnection connection in flow.connections)
                if (connection != null && connection != ignored && connection.from == current && connection.to != null)
                    open.Push(connection.to);
        }
        return false;
    }

    static void RefreshWorkersUi()
    {
        WorkersUI workersUi = FindObjectOfType<WorkersUI>();
        if (workersUi != null) workersUi.RefreshFlowOnly();
    }

    void EnsureFlowCaptureHud()
    {
        if (flowCaptureHud != null && flowCapturePath != null && flowCaptureFinishButton != null)
        {
            LayoutFlowCaptureHud();
            return;
        }
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
            RectTransform existingRect = flowCaptureHud.GetComponent<RectTransform>();
            flowCaptureTitle = existing.Find("Title")?.GetComponent<TextMeshProUGUI>();
            flowCapturePath = existing.Find("Path")?.GetComponent<TextMeshProUGUI>();
            flowCaptureFinishButton = existing.Find("Buttons/Finish")?.GetComponent<Button>();
            flowCaptureCancelButton = existing.Find("Buttons/Cancel")?.GetComponent<Button>();
            LayoutFlowCaptureHud();
            WireFlowCaptureButtons();
            return;
        }

        flowCaptureHud = new GameObject("FlowCaptureHUD", typeof(RectTransform), typeof(Image));
        flowCaptureHud.transform.SetParent(playerUICanvas.transform, false);
        var rect = (RectTransform)flowCaptureHud.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -70f);
        rect.sizeDelta = new Vector2(760f, 140f);
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

        flowCaptureTitle = MakeCaptureLabel(flowCaptureHud.transform, "Title", "Create Flow Graph", 15f, FontStyles.Bold);
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

        flowCaptureFinishButton = MakeCaptureButton(buttons.transform, "Finish", new Color(0.22f, 0.38f, 0.48f, 1f));
        flowCaptureCancelButton = MakeCaptureButton(buttons.transform, "Cancel", new Color(0.20f, 0.23f, 0.29f, 1f));
        LayoutFlowCaptureHud();
        WireFlowCaptureButtons();
        flowCaptureHud.transform.SetAsLastSibling();
        flowCaptureHud.SetActive(false);
    }

    void LayoutFlowCaptureHud()
    {
        if (playerUICanvas == null) playerUICanvas = flowCaptureHud.GetComponentInParent<Canvas>();
        if (playerUICanvas == null) return;
        var rect = flowCaptureHud.GetComponent<RectTransform>();
        var panelSize = flowCaptureHud.GetComponent<LayoutElement>();
        if (panelSize == null) panelSize = flowCaptureHud.AddComponent<LayoutElement>();
        panelSize.ignoreLayout = true;
        var fitter = flowCaptureHud.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
        rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -70f);
        float canvasWidth = ((RectTransform)playerUICanvas.transform).rect.width;
        rect.sizeDelta = new Vector2(Mathf.Min(500f, Mathf.Max(0f, canvasWidth - 32f)), 150f);
        flowCaptureHud.GetComponent<Image>().color = new Color(0.075f, 0.09f, 0.12f, 0.98f);
        var layout = flowCaptureHud.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 6f;
        layout.childForceExpandHeight = false;
        var instructions = flowCaptureHud.transform.Find("Instructions")?.GetComponent<TextMeshProUGUI>();
        if (instructions == null)
            instructions = MakeCaptureLabel(flowCaptureHud.transform, "Instructions", "", 11f, FontStyles.Normal);
        instructions.transform.SetSiblingIndex(2);
        instructions.text = "Drag to connect  •  Reuse a source to split  •  Click to select  •  Ctrl-click to remove";
        instructions.color = new Color(0.65f, 0.72f, 0.80f, 1f);
        instructions.overflowMode = TextOverflowModes.Ellipsis;
        instructions.GetComponent<LayoutElement>().minHeight = 32f;
        instructions.GetComponent<LayoutElement>().preferredHeight = 32f;
        foreach (TextMeshProUGUI label in new[] { flowCaptureTitle, flowCapturePath })
        {
            if (label == null) continue;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.GetComponent<LayoutElement>().minHeight = 22f;
            label.GetComponent<LayoutElement>().preferredHeight = 22f;
        }
        if (flowCapturePath != null) flowCapturePath.color = new Color(0.78f, 0.83f, 0.89f, 1f);
        Transform buttons = flowCaptureHud.transform.Find("Buttons");
        if (buttons != null)
        {
            var size = buttons.GetComponent<LayoutElement>();
            size.minHeight = 32f;
            size.preferredHeight = 32f;
            size.flexibleHeight = 0f;
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
        }
        foreach (Button button in new[] { flowCaptureFinishButton, flowCaptureCancelButton })
        {
            if (button == null) continue;
            var size = button.GetComponent<LayoutElement>();
            size.minWidth = 88f;
            size.preferredWidth = 88f;
            size.minHeight = 30f;
            size.preferredHeight = 30f;
            size.flexibleHeight = 0f;
            button.GetComponent<Image>().color = button == flowCaptureFinishButton
                ? new Color(0.22f, 0.38f, 0.48f, 1f) : new Color(0.20f, 0.23f, 0.29f, 1f);
            button.GetComponentInChildren<TextMeshProUGUI>().fontSize = 12f;
        }
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
            flowCaptureTitle.text = verb + " \"" + capturedFlow.flowName + "\"";
        }
        if (flowCapturePath != null)
        {
            if (capturedFlow.stations.Count == 0)
            {
                flowCapturePath.text = capturingNewFlow
                    ? "No paths yet | Drag from one station and release on the next"
                    : "Current path empty | Drag station to station to rebuild, then Finish";
            }
            else
            {
                string active = activeFlowNode != null ? activeFlowNode.DisplayName : "none";
                flowCapturePath.text = "Active: " + active + "  •  " + capturedFlow.stations.Count
                    + " stations  •  " + capturedFlow.connections.Count + " connections";
            }
        }
        if (flowCaptureFinishButton != null)
            flowCaptureFinishButton.interactable = capturedFlow.stations.Count > 0;
    }

    static string FormatFlowGraph(ProductionFlowPlan flow)
    {
        if (flow == null || flow.stations == null || flow.stations.Count == 0) return "No nodes";
        flow.EnsureLegacyConnections();
        if (flow.connections.Count == 0)
            return "Node: " + StationNode.EnsureOn(flow.stations[0]).DisplayName;

        var parts = new List<string>();
        foreach (ProductionFlowConnection connection in flow.connections)
        {
            if (connection == null || connection.from == null || connection.to == null) continue;
            string from = StationNode.EnsureOn(connection.from).DisplayName;
            string to = StationNode.EnsureOn(connection.to).DisplayName;
            parts.Add(from + " → " + to);
        }
        return string.Join("  |  ", parts);
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

                highlight.highlightColor = activeFlowNode != null && activeFlowNode.gameObject == station
                    ? new Color(1f, 0.72f, 0.12f, 1f)
                    : new Color(0.35f, 1.1f, 0.45f, 1f);
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
        summary += "\n\n<b>Current task:</b> " + selectedEmployee.GetCurrentTaskDescription();
        ProductionJob activeJob = selectedEmployee.ActiveJob;
        if (activeJob == null && ProductionManager.Instance != null)
            summary += "\n\n" + ProductionManager.Instance.GetFlowQueueStatus(selectedEmployee);
        if (activeJob != null)
        {
            summary += "\n<b>Step:</b> " + activeJob.CurrentStationType
                + "  |  <b>Carrying:</b> " + activeJob.heldUnits;
            if (activeJob.reservedDestinationStation != null)
                summary += "\n<b>Reserved destination:</b> "
                    + StationNode.EnsureOn(activeJob.reservedDestinationStation).DisplayName;
        }
        StationNode hoveredStation = GetStationUnderPointer();
        string preview = WorkflowAnalysis.GetAssignmentPreview(selectedEmployee, hoveredStation);
        workflowDecisionText.text = string.IsNullOrEmpty(preview) ? summary : summary + "\n" + preview;
        SetWorkflowDecisionHudVisible(true);
    }

    StationNode GetStationUnderPointer()
    {
        if (Camera.main == null || IsPointerOverUI()) return null;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (IsCapturingFlow) return FindFlowStationUnderRay(ray);
        return Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer)
            ? StationNode.FindFromCollider(hit.collider)
            : null;
    }

    StationNode FindFlowStationUnderRay(Ray ray)
    {
        // Mounted stations can overlap the counter collider. Look through scenery
        // while capturing a flow, choosing the nearest actual station on the ray.
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, clickLayer);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            StationNode node = StationNode.FindFromCollider(hit.collider);
            if (node == null) continue;
            Register register = node.GetComponent<Register>();
            if (register != null && !register.IsPlacedRegister) continue;
            return node;
        }
        return null;
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
