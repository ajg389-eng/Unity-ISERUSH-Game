using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One row in the assignment UI (legacy). Prefer operatedStations + StationNode output links.</summary>
[Serializable]
public class AssignmentRow
{
    public GameObject workAt;
    public GameObject deliverTo;
}

/// <summary>
/// Hired employee. Idle until assigned to stations in Manage mode (max 3).
/// Pipelines define work order (burger: Freezer→Grill→Assembly). Delivery is always via Assign Output.
/// Cashiers fetch drinks + heat-lamp food for customers.
/// </summary>
public class KitchenEmployee : MonoBehaviour
{
    public const int MaxStations = 3;

    public float moveSpeed = 3f;
    [Tooltip("Unused for grid travel (workers move cell-to-cell). Kept for inspector compatibility.")]
    public float moveSmoothTime = 0.12f;
    public float groundHeight;
    public GridManager grid;
    [Tooltip("How close to a cell center counts as arrived (then snaps exactly to center).")]
    public float cellArrivalDistance = 0.04f;

    [Header("Identity")]
    public string employeeName = "Worker";

    static readonly string[] FirstNames =
    {
        "Alex", "Jordan", "Sam", "Casey", "Riley", "Taylor", "Morgan", "Avery",
        "Quinn", "Jamie", "Reese", "Drew", "Skyler", "Cameron", "Parker", "Blake",
        "Harper", "Rowan", "Finley", "Hayden", "Logan", "Charlie", "Emery", "Kai",
        "Noah", "Mia", "Leo", "Zoe", "Owen", "Lily", "Ethan", "Nora",
        "Luis", "Sofia", "Diego", "Ava", "Marcus", "Elena", "Priya", "Kenji"
    };

    static readonly string[] LastNames =
    {
        "Lee", "Nguyen", "Patel", "Garcia", "Kim", "Brown", "Martinez", "Chen",
        "Wilson", "Lopez", "Singh", "Davis", "Torres", "Clark", "Rivera", "Young",
        "Scott", "Green", "Baker", "Adams", "Nelson", "Carter", "Mitchell", "Perez",
        "Roberts", "Turner", "Phillips", "Campbell", "Parker", "Evans", "Edwards", "Collins"
    };

    /// <summary>Assign a random first + last name (used when hiring).</summary>
    public void AssignRandomName()
    {
        string first = FirstNames[UnityEngine.Random.Range(0, FirstNames.Length)];
        string last = LastNames[UnityEngine.Random.Range(0, LastNames.Length)];
        employeeName = first + " " + last;
    }

    [Header("Assigned stations (max 3) — set via Management → click station → Assign Worker")]
    public List<GameObject> operatedStations = new List<GameObject>();

    [Header("Legacy")]
    public AssignmentRow[] assignmentRows = new AssignmentRow[4];
    public List<StationType> assignedStations = new List<StationType>();
    public GrillStation targetGrill;

    ProductionJob currentJob;

    public bool IsIdle => currentJob == null;
    public bool HasJob => currentJob != null;
    public int OperatedStationCount => operatedStations != null ? operatedStations.Count : 0;
    public int AssignedStationCount => OperatedStationCount;
    public bool CanTakeJobs => OperatedStationCount > 0;

    public bool IsAssignedTo(GameObject station)
    {
        return station != null && operatedStations != null && operatedStations.Contains(station);
    }

    public bool AddOperatedStation(GameObject station)
    {
        if (station == null) return false;
        if (operatedStations == null) operatedStations = new List<GameObject>();
        if (operatedStations.Contains(station)) return true;
        if (operatedStations.Count >= MaxStations) return false;
        operatedStations.Add(station);
        SyncFromOperatedStations();
        return true;
    }

    public void RemoveOperatedStation(GameObject station)
    {
        if (operatedStations == null || station == null) return;
        operatedStations.Remove(station);
        SyncFromOperatedStations();
    }

    public string GetAssignedStationsSummary()
    {
        if (operatedStations == null || operatedStations.Count == 0) return "Unassigned (idle)";
        var parts = new List<string>();
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var t = GetStationTypeFrom(go);
            parts.Add(t.HasValue ? t.Value.ToString() : go.name);
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "Unassigned (idle)";
    }

    /// <summary>Multi-line detail for worker inspect UI: station and output route per assignment.</summary>
    public string GetAssignmentDetailText()
    {
        if (operatedStations == null || operatedStations.Count == 0)
            return "No stations assigned";

        var lines = new List<string>();
        foreach (var go in operatedStations)
        {
            if (go == null) continue;

            if (go.GetComponent<Register>() != null)
            {
                lines.Add("• Register (cashier)");
                continue;
            }

            var node = go.GetComponent<StationNode>();
            string stationName = node != null ? node.DisplayName : go.name;
            if (node != null && node.outputTarget != null)
            {
                var outNode = StationNode.EnsureOn(node.outputTarget);
                string outName = outNode != null ? outNode.DisplayName : node.outputTarget.name;
                lines.Add("• " + stationName + " → " + outName);
            }
            else
            {
                lines.Add("• " + stationName + " (no output set)");
            }
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "No stations assigned";
    }

    /// <summary>Human-readable description of what this worker is doing right now.</summary>
    public string GetCurrentTaskDescription()
    {
        string product = GetActiveProductLabel();

        switch (step)
        {
            case Step.GoToFreezer: return FormatTask("Walking to Freezer", product);
            case Step.AtFreezer: return FormatTask("Taking patty from Freezer", product);
            case Step.GoToGrill: return FormatTask("Walking to Grill", product);
            case Step.AtGrill: return FormatTask("Cooking at Grill", product);
            case Step.GoToAssembly: return FormatTask("Walking to Assembly", product);
            case Step.AtAssembly: return FormatTask("Assembling order", product);
            case Step.GoToFryer: return FormatTask("Walking to Fryer", product);
            case Step.AtFryer: return FormatTask("Frying at Fryer", product);
            case Step.GoToOutput:
            case Step.GoToHeatLamp:
                return FormatTask("Walking to deliver at " + GetDeliverTargetLabel(), product);
            case Step.AtOutput:
            case Step.AtHeatLamp:
                return FormatTask("Delivering to " + GetDeliverTargetLabel(), product);
            case Step.CashierGoDrink: return "Cashier — walking to drink station";
            case Step.CashierAtDrink: return "Cashier — pouring drink";
            case Step.CashierGoFood: return "Cashier — walking to heat lamp";
            case Step.CashierAtFood: return "Cashier — picking up food";
            case Step.CashierReturnServe: return "Cashier — serving customer";
        }

        var reg = GetRegisterStation();
        if (reg != null)
        {
            if (cashierTray != null && cashierTray.Count > 0)
                return "Cashier — ready to serve customer";

            var front = reg.GetFrontCustomer();
            if (front != null)
            {
                var order = front.GetOrder();
                if (order != null)
                {
                    var next = GetNextCashierFetch(order);
                    if (next == null)
                        return "Cashier — waiting for kitchen items";
                    return "Cashier — starting customer order";
                }
            }

            return "Cashier — waiting at register";
        }

        if (currentJob != null)
            return FormatTask("Working", product);

        if (!CanTakeJobs)
            return "Idle — no stations assigned";

        return "Idle — waiting for work";
    }

    string GetActiveProductLabel()
    {
        if (currentJob?.product != null)
            return FormatItemName(currentJob.product);

        if (heldDeliveryItem != null)
            return FormatItemName(heldDeliveryItem);

        if (cashierFetchItem != null)
            return FormatItemName(cashierFetchItem);

        return null;
    }

    static string FormatItemName(ItemDefinition item)
    {
        if (item == null) return null;
        return !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
    }

    static string FormatTask(string action, string product)
    {
        if (string.IsNullOrEmpty(product))
            return action;
        return action + " (" + product + ")";
    }

    string GetDeliverTargetLabel()
    {
        if (deliverTarget == null) return "output station";
        var node = StationNode.EnsureOn(deliverTarget);
        return node != null ? node.DisplayName : deliverTarget.name;
    }

    public void SyncFromOperatedStations()
    {
        assignedStations = new List<StationType>();
        targetGrill = null;
        if (operatedStations == null) return;
        operatedStations.RemoveAll(g => g == null);
        foreach (var go in operatedStations)
        {
            var t = GetStationTypeFrom(go);
            if (t.HasValue && !assignedStations.Contains(t.Value))
                assignedStations.Add(t.Value);
            if (t == StationType.Grill)
                targetGrill = go.GetComponent<GrillStation>();
        }
    }

    /// <summary>True if this worker can run the job's current pipeline step (and product filters).</summary>
    public bool CanTakeJobStep(ProductionJob job)
    {
        if (job == null) return false;
        SyncFromOperatedStations();

        if (job.IsHeatLampStep)
        {
            // Heat lamp delivery is done by the worker who finished the last production step
            // (same worker continues — jobs shouldn't be reassigned at heat lamp).
            return false;
        }

        var stationType = job.CurrentStationType;
        if (!stationType.HasValue) return false;
        if (assignedStations == null || !assignedStations.Contains(stationType.Value))
            return false;

        // Grill / Assembly require matching selected product
        if (stationType.Value == StationType.Grill)
        {
            var g = GetGrillStation();
            if (g == null || !g.CanProcess(job.product)) return false;
        }
        else if (stationType.Value == StationType.Assembly)
        {
            var a = GetAssemblyStation();
            if (a == null || !a.CanProcess(job.product)) return false;
        }

        return true;
    }

    [System.Obsolete("Use CanTakeJobStep")]
    public bool HasStationForStep(int stepIndex) => false;

    public bool HasAllStationsAssigned() => CanTakeJobs;

    public void SetAssignedStations(List<StationType> stations)
    {
        assignedStations = new List<StationType>();
        if (stations == null) return;
        for (int i = 0; i < stations.Count && assignedStations.Count < MaxStations; i++)
        {
            if (!assignedStations.Contains(stations[i]))
                assignedStations.Add(stations[i]);
        }
    }

    public void EnsureAssignmentRows()
    {
        if (assignmentRows == null) assignmentRows = new AssignmentRow[4];
        for (int i = 0; i < 4; i++)
        {
            if (assignmentRows[i] == null)
                assignmentRows[i] = new AssignmentRow();
        }
    }

    public void SyncFromAssignmentRows()
    {
        if (operatedStations != null && operatedStations.Count > 0)
        {
            SyncFromOperatedStations();
            return;
        }
        EnsureAssignmentRows();
        assignedStations = new List<StationType>();
        targetGrill = null;
        for (int i = 0; i < 4 && i < assignmentRows.Length; i++)
        {
            var row = assignmentRows[i];
            if (row == null) continue;
            StationType? t = GetStationTypeFrom(row.workAt);
            if (t.HasValue && !assignedStations.Contains(t.Value))
                assignedStations.Add(t.Value);
            if (i == 0 && row.workAt != null && row.deliverTo != null)
            {
                var fs = row.workAt.GetComponent<FreezerStation>();
                var gs = row.deliverTo.GetComponent<GrillStation>();
                if (fs != null && gs != null)
                    targetGrill = gs;
            }
        }
    }

    public static StationType? GetStationTypeFrom(GameObject go)
    {
        if (go == null) return null;
        if (go.GetComponent<FreezerStation>() != null) return StationType.Freezer;
        if (go.GetComponent<GrillStation>() != null) return StationType.Grill;
        if (go.GetComponent<PantryStation>() != null) return StationType.Pantry;
        if (go.GetComponent<AssemblyStation>() != null) return StationType.Assembly;
        if (go.GetComponent<FryerStation>() != null) return StationType.Fryer;
        if (go.GetComponent<DrinkStation>() != null) return StationType.Drink;
        if (go.GetComponent<Register>() != null) return StationType.Register;
        return null;
    }

    public static bool IsRegister(GameObject go) => go != null && go.GetComponent<Register>() != null;

    public static Vector3 GetInteractionPosition(GameObject go)
    {
        if (go == null) return Vector3.zero;
        var f = go.GetComponent<FreezerStation>();
        if (f != null) return f.GetInteractionPosition();
        var g = go.GetComponent<GrillStation>();
        if (g != null) return g.GetInteractionPosition();
        var p = go.GetComponent<PantryStation>();
        if (p != null) return p.GetInteractionPosition();
        var a = go.GetComponent<AssemblyStation>();
        if (a != null) return a.GetInteractionPosition();
        var fry = go.GetComponent<FryerStation>();
        if (fry != null) return fry.GetInteractionPosition();
        var d = go.GetComponent<DrinkStation>();
        if (d != null) return d.GetInteractionPosition();
        var r = go.GetComponent<Register>();
        if (r != null) return r.GetInteractionPosition();
        var h = go.GetComponent<HeatLampStation>();
        if (h != null) return h.GetInteractionPosition();
        return go.transform.position;
    }

    public Register GetRegisterStation()
    {
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var reg = go.GetComponent<Register>();
            if (reg != null) return reg;
        }
        return null;
    }

    public FreezerStation GetFreezerStation()
    {
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var fs = go.GetComponent<FreezerStation>();
            if (fs != null) return fs;
        }
        return null;
    }

    public GrillStation GetGrillStation()
    {
        if (targetGrill != null) return targetGrill;
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var gs = go.GetComponent<GrillStation>();
            if (gs != null) return gs;
        }
        return null;
    }

    public AssemblyStation GetAssemblyStation()
    {
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var ast = go.GetComponent<AssemblyStation>();
            if (ast != null) return ast;
        }
        return null;
    }

    public FryerStation GetFryerStation()
    {
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var f = go.GetComponent<FryerStation>();
            if (f != null) return f;
        }
        return null;
    }

    public DrinkStation GetDrinkStation()
    {
        foreach (var go in operatedStations)
        {
            if (go == null) continue;
            var d = go.GetComponent<DrinkStation>();
            if (d != null) return d;
        }
        return null;
    }

    float stateTimer;
    List<Vector3> path = new List<Vector3>();
    Vector3 pathDestination;
    Vector3 moveVelocity;
    float ArrivalRadius => Mathf.Max(0.02f, cellArrivalDistance);

    enum Step
    {
        None,
        GoToFreezer, AtFreezer,
        GoToGrill, AtGrill,
        GoToAssembly, AtAssembly,
        GoToFryer, AtFryer,
        GoToHeatLamp, AtHeatLamp, // legacy aliases unused — delivery uses GoToOutput
        GoToOutput, AtOutput,
        // Cashier fetch / serve
        CashierGoDrink, CashierAtDrink,
        CashierGoFood, CashierAtFood,
        CashierReturnServe
    }

    Step step;
    bool hasPatty;
    readonly List<ItemDefinition> ingredientsHeld = new List<ItemDefinition>();
    /// <summary>Finished product being carried to the heat lamp (burger/fries).</summary>
    ItemDefinition heldDeliveryItem;
    /// <summary>Station this worker is delivering to after finishing work.</summary>
    GameObject deliverTarget;
    /// <summary>True after station work is done and we are waiting on / moving to Assign Output.</summary>
    bool awaitingOutputDelivery;
    ProductionManager manager;
    float instantStepBarTimer;

    // Cashier tray — items gathered for the current front customer
    CustomerAI cashierCustomer;
    readonly List<ItemDefinition> cashierTray = new List<ItemDefinition>();
    ItemDefinition cashierFetchItem;

    public bool ShowTaskBar { get; private set; }
    public float TaskProgress { get; private set; }

    /// <summary>True if the worker is carrying anything (kitchen handoff or cashier tray).</summary>
    public bool IsHoldingAnything =>
        heldDeliveryItem != null
        || hasPatty
        || (ingredientsHeld != null && ingredientsHeld.Count > 0)
        || (cashierTray != null && cashierTray.Count > 0);

    /// <summary>Readable list of what this worker is currently holding.</summary>
    public string GetHeldInventoryDisplay()
    {
        var parts = new List<string>();

        void AddItem(ItemDefinition item)
        {
            if (item == null) return;
            string n = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            if (string.IsNullOrEmpty(n)) return;
            if (!parts.Contains(n)) parts.Add(n);
        }

        AddItem(heldDeliveryItem);

        if (heldDeliveryItem == null && hasPatty)
        {
            if (manager != null && manager.PattyItem != null)
                AddItem(manager.PattyItem);
            else
                parts.Add("Patty");
        }

        if (ingredientsHeld != null)
        {
            foreach (var item in ingredientsHeld)
                AddItem(item);
        }

        if (cashierTray != null)
        {
            foreach (var item in cashierTray)
                AddItem(item);
        }

        if (parts.Count == 0) return "";
        return string.Join(", ", parts);
    }

    void ClearHeldInventory()
    {
        hasPatty = false;
        heldDeliveryItem = null;
        ingredientsHeld.Clear();
        awaitingOutputDelivery = false;
    }

    void Start()
    {
        if (operatedStations == null) operatedStations = new List<GameObject>();
        SyncFromOperatedStations();
        manager = ProductionManager.Instance;
        if (manager != null)
            manager.RegisterEmployee(this);
        if (grid == null)
            grid = FindObjectOfType<GridManager>();
        if (groundHeight == 0f && grid != null)
            groundHeight = grid.Origin.y;
        if (GetComponent<EmployeeInventoryLabel>() == null)
            gameObject.AddComponent<EmployeeInventoryLabel>();
    }

    void OnDestroy()
    {
        if (operatedStations != null)
        {
            foreach (var go in operatedStations)
            {
                if (go == null) continue;
                var node = go.GetComponent<StationNode>();
                if (node != null && node.assignedWorker == this)
                    node.assignedWorker = null;
            }
        }
        if (manager != null)
            manager.UnregisterEmployee(this);
    }

    public void AssignJob(ProductionJob job)
    {
        if (currentJob != null) return;
        currentJob = job;
        hasPatty = job != null && job.hasPatty;
        ingredientsHeld.Clear();
        if (job?.ingredientsHeld != null)
            ingredientsHeld.AddRange(job.ingredientsHeld);

        heldDeliveryItem = null;
        deliverTarget = null;
        awaitingOutputDelivery = false;
        step = StepFromJob(job);
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
    }

    static Step StepFromJob(ProductionJob job)
    {
        if (job == null) return Step.None;
        if (job.IsHeatLampStep) return Step.GoToHeatLamp;
        var t = job.CurrentStationType;
        if (!t.HasValue) return Step.GoToHeatLamp;
        return t.Value switch
        {
            StationType.Freezer => Step.GoToFreezer,
            StationType.Grill => Step.GoToGrill,
            StationType.Assembly => Step.GoToAssembly,
            StationType.Fryer => Step.GoToFryer,
            _ => Step.GoToHeatLamp
        };
    }

    /// <summary>World station this worker operates for the given type.</summary>
    GameObject GetOperatedStationObject(StationType stationType)
    {
        if (operatedStations != null)
        {
            foreach (var go in operatedStations)
            {
                if (go == null) continue;
                if (GetStationTypeFrom(go) == stationType)
                    return go;
            }
        }

        // Fallback via typed getters (same list, but keeps handoff working if list was stale)
        switch (stationType)
        {
            case StationType.Freezer: return GetFreezerStation()?.gameObject;
            case StationType.Grill: return GetGrillStation()?.gameObject;
            case StationType.Assembly: return GetAssemblyStation()?.gameObject;
            case StationType.Fryer: return GetFryerStation()?.gameObject;
            case StationType.Drink: return GetDrinkStation()?.gameObject;
            case StationType.Register: return GetRegisterStation()?.gameObject;
        }
        return null;
    }

    /// <summary>
    /// StationNode for the step just finished — prefers operated station, then any node
    /// of that type that has this worker assigned.
    /// </summary>
    StationNode ResolveStationNodeForStep(StationType stationType)
    {
        var stationGo = GetOperatedStationObject(stationType);
        if (stationGo != null)
        {
            var node = StationNode.EnsureOn(stationGo);
            if (node != null) return node;
        }

        var nodes = FindObjectsOfType<StationNode>();
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            if (n.assignedWorker != this) continue;
            if (n.StationType == stationType)
                return n;
        }
        return null;
    }

    /// <summary>
    /// After finishing work at the current station, deliver to that station's Assign Output.
    /// Workers never auto-route to the next pipeline step — output must be set by the player.
    /// Safe to call repeatedly while waiting for Assign Output.
    /// </summary>
    void FinishStepAndHandoff()
    {
        if (currentJob == null || manager == null)
        {
            currentJob = null;
            step = Step.None;
            return;
        }

        currentJob.hasPatty = hasPatty;
        currentJob.ingredientsHeld.Clear();
        currentJob.ingredientsHeld.AddRange(ingredientsHeld);

        if (!currentJob.CurrentStationType.HasValue)
        {
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        var node = ResolveStationNodeForStep(currentJob.CurrentStationType.Value);
        if (node == null || !node.HasOutput)
        {
            // Finished work but no output — wait until player assigns one
            awaitingOutputDelivery = true;
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        awaitingOutputDelivery = true;
        deliverTarget = node.outputTarget;
        heldDeliveryItem = currentJob.product;
        step = Step.GoToOutput;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        Sfx.Play(SfxId.StationWorkComplete);
    }

    bool IsAtDeliverTarget()
    {
        if (deliverTarget == null) return false;
        var tiles = deliverTarget.GetComponent<StationInteractionTiles>()
            ?? deliverTarget.GetComponentInChildren<StationInteractionTiles>(true);
        if (tiles != null)
            return tiles.IsEmployeeOnInteractionTile(transform.position);
        // Heat lamp / stations without tiles: arrived when MoveToward completed
        return true;
    }

    void CompleteOutputDelivery()
    {
        if (currentJob == null || manager == null || deliverTarget == null)
        {
            currentJob = null;
            step = Step.None;
            deliverTarget = null;
            return;
        }

        var lamp = deliverTarget.GetComponent<HeatLampStation>();
        if (lamp != null)
        {
            bool delivered = manager.DeliverToHeatLamp(currentJob.order, lamp);
            if (!delivered && !lamp.HasSpace)
                return; // wait for space
            if (!delivered)
            {
                Debug.LogWarning("KitchenEmployee: could not deliver to heat lamp.", this);
                return;
            }
            ClearHeldInventory();
            manager.CompleteJob(currentJob);
            currentJob = null;
            deliverTarget = null;
            step = Step.None;
            return;
        }

        var outType = GetStationTypeFrom(deliverTarget);
        if (!outType.HasValue)
        {
            Debug.LogWarning("KitchenEmployee: output target is not a known station.", deliverTarget);
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        int idx = currentJob.FindPipelineIndex(outType.Value);
        if (idx < 0)
        {
            Debug.LogWarning(
                $"KitchenEmployee: output {outType.Value} is not on this product's pipeline. " +
                "Route Freezer→Grill→Assembly→HeatLamp (burger) or Fryer→HeatLamp (fries).",
                this);
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        currentJob.hasPatty = hasPatty;
        currentJob.ingredientsHeld.Clear();
        currentJob.ingredientsHeld.AddRange(ingredientsHeld);
        currentJob.currentStepIndex = idx;
        manager.ReleaseJob(currentJob);
        currentJob = null;
        ClearHeldInventory();
        deliverTarget = null;
        step = Step.None;
        stateTimer = 0f;
        path.Clear();
    }

    void Update()
    {
        SnapToGround();
        if (manager == null)
            manager = ProductionManager.Instance;

        if (currentJob != null)
        {
            RunWorkflow();
            return;
        }

        RunRegisterDuty();
    }

    void RunRegisterDuty()
    {
        ShowTaskBar = false;
        TaskProgress = 0f;

        var reg = GetRegisterStation();
        if (reg == null || !reg.isEnabled)
        {
            ClearCashierTray();
            return;
        }

        // Continue in-progress cashier fetch/serve steps
        if (step == Step.CashierGoDrink || step == Step.CashierAtDrink
            || step == Step.CashierGoFood || step == Step.CashierAtFood
            || step == Step.CashierReturnServe)
        {
            RunCashierStep(reg);
            return;
        }

        var front = reg.GetFrontCustomer();
        if (front == null || !reg.IsFrontCustomerReady())
        {
            ClearCashierTray();
            MoveToward(reg.GetInteractionPosition());
            return;
        }

        EnsureCashierCustomer(front);

        // Always deliver whatever we're holding before fetching more
        if (cashierTray.Count > 0)
        {
            step = Step.CashierReturnServe;
            stateTimer = 0f;
            path.Clear();
            RunCashierStep(reg);
            return;
        }

        var order = front.GetOrder();
        if (order == null || order.GetTotalQuantity() <= 0)
        {
            // Order already fully delivered — finish the sale if still in queue
            if (front.IsOrderFullyDelivered)
                reg.CompleteServe(front, order);
            MoveToward(reg.GetInteractionPosition());
            return;
        }

        var next = GetNextCashierFetch(order);
        if (next == null)
        {
            // Waiting on kitchen / stock — stand at register
            MoveToward(reg.GetInteractionPosition());
            ShowTaskBar = true;
            TaskProgress = 0.25f;
            return;
        }

        cashierFetchItem = next;
        bool isDrink = manager != null && manager.orderConfig != null && manager.orderConfig.IsDrink(next);
        step = isDrink ? Step.CashierGoDrink : Step.CashierGoFood;
        stateTimer = 0f;
        path.Clear();
        RunCashierStep(reg);
    }

    void EnsureCashierCustomer(CustomerAI front)
    {
        if (cashierCustomer == front) return;
        cashierCustomer = front;
        cashierTray.Clear();
        cashierFetchItem = null;
    }

    void ClearCashierTray()
    {
        cashierCustomer = null;
        cashierTray.Clear();
        cashierFetchItem = null;
        if (step == Step.CashierGoDrink || step == Step.CashierAtDrink
            || step == Step.CashierGoFood || step == Step.CashierAtFood
            || step == Step.CashierReturnServe)
            step = Step.None;
    }

    /// <summary>Next remaining order item the cashier can fetch right now.</summary>
    ItemDefinition GetNextCashierFetch(CustomerOrder order)
    {
        if (order?.lines == null || manager == null) return null;
        var config = manager.orderConfig;

        // Prefer drinks first (quick)
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            if (config != null && config.IsDrink(line.item) && manager.HasDrinkInStock())
                return line.item;
        }

        var lamp = manager.HeatLamp;
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            if (config != null && config.IsDrink(line.item)) continue;
            if (lamp != null && lamp.HasSingleItem(line.item))
                return line.item;
        }

        return null;
    }

    void RunCashierStep(Register reg)
    {
        switch (step)
        {
            case Step.CashierGoDrink:
                if (MoveToward(manager.GetDrinkStationPosition(this)))
                {
                    if (manager.IsEmployeeOnDrinkTile(transform.position, this))
                    {
                        step = Step.CashierAtDrink;
                        stateTimer = 0f;
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.CashierAtDrink:
                if (!manager.IsEmployeeOnDrinkTile(transform.position, this))
                {
                    step = Step.CashierGoDrink;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                stateTimer += Time.deltaTime;
                float drinkTime = manager.GetDrinkInteractionTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / drinkTime);
                if (stateTimer >= drinkTime)
                {
                    if (!manager.HasDrinkInStock() || !manager.TryDispenseDrink(this))
                    {
                        step = Step.None;
                        cashierFetchItem = null;
                        break;
                    }
                    if (cashierFetchItem != null)
                        cashierTray.Add(cashierFetchItem);
                    cashierFetchItem = null;
                    // Deliver this item immediately
                    step = Step.CashierReturnServe;
                    stateTimer = 0f;
                    path.Clear();
                }
                break;

            case Step.CashierGoFood:
                if (MoveToward(manager.GetHeatLampPosition()))
                {
                    if (manager.IsEmployeeOnHeatLampTile(transform.position))
                    {
                        step = Step.CashierAtFood;
                        stateTimer = 0f;
                    }
                    else
                    {
                        if (manager.HeatLamp == null || manager.HeatLamp.GetComponent<StationInteractionTiles>() == null)
                        {
                            step = Step.CashierAtFood;
                            stateTimer = 0f;
                        }
                        else { path.Clear(); pathDestination = Vector3.zero; }
                    }
                }
                break;

            case Step.CashierAtFood:
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                var lamp = manager.HeatLamp;
                if (lamp == null || cashierFetchItem == null)
                {
                    step = Step.None;
                    cashierFetchItem = null;
                    break;
                }
                var taken = lamp.TryTakeSingleItem(cashierFetchItem);
                if (taken != null)
                    cashierTray.Add(cashierFetchItem);
                cashierFetchItem = null;
                // Deliver this item immediately
                step = Step.CashierReturnServe;
                stateTimer = 0f;
                path.Clear();
                break;
            }

            case Step.CashierReturnServe:
                if (!MoveToward(reg.GetInteractionPosition()))
                    break;
                ShowTaskBar = true;
                TaskProgress = 1f;

                if (cashierCustomer == null || cashierTray.Count == 0)
                {
                    step = Step.None;
                    break;
                }

                // Hand over one item at a time
                var item = cashierTray[0];
                if (reg.TryDeliverItem(cashierCustomer, item))
                {
                    cashierTray.RemoveAt(0);
                    // If more held (shouldn't usually), keep delivering; else fetch next
                    if (cashierTray.Count > 0)
                    {
                        // stay in ReturnServe for next item
                        break;
                    }
                    step = Step.None;
                }
                // If deliver failed (not on duty yet), keep trying next frame
                break;
        }
    }

    void RunWorkflow()
    {
        ShowTaskBar = false;
        TaskProgress = 0f;

        switch (step)
        {
            case Step.GoToFreezer:
                if (MoveToward(manager.GetFreezerPosition(this)))
                {
                    if (manager.IsEmployeeOnFreezerTile(transform.position, this))
                    {
                        step = Step.AtFreezer;
                        stateTimer = 0f;
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtFreezer:
                if (!manager.IsEmployeeOnFreezerTile(transform.position, this))
                {
                    step = Step.GoToFreezer;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                // Already took the item — keep retrying handoff (e.g. output assigned late)
                if (hasPatty || awaitingOutputDelivery)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    FinishStepAndHandoff();
                    break;
                }
                stateTimer += Time.deltaTime;
                float freezerTime = manager.GetFreezerInteractionTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / freezerTime);
                if (stateTimer >= freezerTime)
                {
                    if (!manager.HasPattyInStock())
                    {
                        TaskProgress = 1f;
                        break;
                    }
                    if (!manager.TryTakePattyFromFreezer(this))
                    {
                        stateTimer = freezerTime;
                        break;
                    }
                    hasPatty = true;
                    FinishStepAndHandoff();
                }
                break;

            case Step.GoToGrill:
                if (MoveToward(manager.GetGrillPosition(this)))
                {
                    if (manager.IsEmployeeOnGrillTile(transform.position, this))
                    {
                        var grill = manager.GetGrillFor(this);
                        if (grill != null && !grill.CanProcess(currentJob != null ? currentJob.product : null))
                        {
                            // Wrong product selected — wait
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        if (hasPatty && manager.PlacePattyOnGrill(this))
                        {
                            hasPatty = false;
                            step = Step.AtGrill;
                            stateTimer = 0f;
                        }
                        else { path.Clear(); pathDestination = Vector3.zero; }
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtGrill:
                if (!manager.IsEmployeeOnGrillTile(transform.position, this))
                {
                    step = Step.GoToGrill;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                stateTimer += Time.deltaTime;
                float grillTotal = manager.GetGrillPlaceTime(this) + manager.GetGrillCookTime(this)
                    + manager.GetGrillWaitAfterCookedTime(this) + manager.GetGrillTakeTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, grillTotal));
                if (stateTimer >= grillTotal)
                {
                    // Already took cooked item — retry handoff without taking again
                    if (hasPatty || awaitingOutputDelivery)
                    {
                        TaskProgress = 1f;
                        FinishStepAndHandoff();
                        break;
                    }
                    if (manager.TakePattyFromGrill(this))
                    {
                        hasPatty = true;
                        FinishStepAndHandoff();
                    }
                }
                break;

            case Step.GoToAssembly:
                if (MoveToward(manager.GetAssemblyPosition(this)))
                {
                    if (manager.IsEmployeeOnAssemblyTile(transform.position, this))
                    {
                        var asm = GetAssemblyStation();
                        if (asm != null && !asm.CanProcess(currentJob != null ? currentJob.product : null))
                        {
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        step = Step.AtAssembly;
                        stateTimer = 0f;
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtAssembly:
                if (!manager.IsEmployeeOnAssemblyTile(transform.position, this))
                {
                    step = Step.GoToAssembly;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                if (awaitingOutputDelivery)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    FinishStepAndHandoff();
                    break;
                }
                stateTimer += Time.deltaTime;
                float assemblyTime = manager.GetAssemblyInteractionTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, assemblyTime));
                if (stateTimer >= assemblyTime)
                    FinishStepAndHandoff();
                break;

            case Step.GoToFryer:
                if (MoveToward(manager.GetFryerPosition(this)))
                {
                    if (manager.IsEmployeeOnFryerTile(transform.position, this))
                    {
                        if (!manager.HasFriesInStock())
                        {
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        if (manager.TryLoadFryer(this))
                        {
                            step = Step.AtFryer;
                            stateTimer = 0f;
                        }
                        else { path.Clear(); pathDestination = Vector3.zero; }
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtFryer:
                if (!manager.IsEmployeeOnFryerTile(transform.position, this))
                {
                    step = Step.GoToFryer;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                stateTimer += Time.deltaTime;
                float fryerTotal = manager.GetFryerTotalTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, fryerTotal));
                if (stateTimer >= fryerTotal)
                {
                    if (awaitingOutputDelivery)
                    {
                        TaskProgress = 1f;
                        FinishStepAndHandoff();
                        break;
                    }
                    if (manager.TakeFromFryer(this))
                        FinishStepAndHandoff();
                }
                break;

            case Step.GoToOutput:
            {
                if (deliverTarget == null)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    break;
                }
                Vector3 dest = GetInteractionPosition(deliverTarget);
                if (MoveToward(dest))
                {
                    if (IsAtDeliverTarget())
                        step = Step.AtOutput;
                    else
                    {
                        // No interaction tiles — arrival is enough
                        if (deliverTarget.GetComponent<StationInteractionTiles>() == null)
                            step = Step.AtOutput;
                        else
                        {
                            path.Clear();
                            pathDestination = Vector3.zero;
                        }
                    }
                }
                break;
            }

            case Step.AtOutput:
                ShowTaskBar = true;
                TaskProgress = 1f;
                CompleteOutputDelivery();
                break;

            case Step.GoToHeatLamp:
            case Step.AtHeatLamp:
                // Legacy — redirect through output delivery
                step = deliverTarget != null ? Step.GoToOutput : Step.None;
                break;
        }

        if (instantStepBarTimer > 0f)
        {
            instantStepBarTimer -= Time.deltaTime;
            if (instantStepBarTimer > 0f) { ShowTaskBar = true; TaskProgress = 1f; }
        }
    }

    float GroundY => groundHeight != 0f ? groundHeight : (grid != null ? grid.Origin.y : transform.position.y);

    void SnapToGround()
    {
        Vector3 p = transform.position;
        p.y = GroundY;
        transform.position = p;
    }

    void SnapToWorldXZ(Vector3 world)
    {
        transform.position = new Vector3(world.x, GroundY, world.z);
        moveVelocity = Vector3.zero;
    }

    /// <summary>Snap a destination to its grid cell center when a grid is available.</summary>
    Vector3 SnapTarget(Vector3 target)
    {
        if (grid == null) return target;
        Vector3 c = grid.GetCellCenter(target);
        c.y = GroundY;
        return c;
    }

    /// <summary>
    /// Move along grid cells toward target, ending exactly on the requested XZ
    /// (so workers finish on green interaction quad centers).
    /// </summary>
    bool MoveToward(Vector3 target)
    {
        Vector3 exactTarget = target;
        exactTarget.y = GroundY;

        if (grid == null)
            return MoveTowardStraight(exactTarget);

        // Already at the exact stand point
        if (HorizontalDistSq(transform.position, exactTarget) <= ArrivalRadius * ArrivalRadius)
        {
            SnapToWorldXZ(exactTarget);
            path.Clear();
            return true;
        }

        if (path.Count == 0 || Vector3.SqrMagnitude(pathDestination - exactTarget) > 0.0001f)
        {
            pathDestination = exactTarget;
            // Pathfind via nearest walkable cell, but always finish on the exact target (quad center)
            Vector3 pathQuery = grid.GetCellCenter(exactTarget);
            path = grid.GetPath(transform.position, pathQuery);

            while (path.Count > 0 && HorizontalDistSq(transform.position, path[0]) <= ArrivalRadius * ArrivalRadius)
            {
                SnapToWorldXZ(path[0]);
                path.RemoveAt(0);
            }

            if (path.Count > 0)
                path[path.Count - 1] = exactTarget;
            else
                path.Add(exactTarget);
        }

        if (path.Count == 0)
            return MoveTowardStraight(exactTarget);

        Vector3 waypoint = path[0];
        waypoint.y = GroundY;

        float step = moveSpeed * Time.deltaTime;
        Vector3 pos = transform.position;
        pos.y = GroundY;
        float dist = Vector3.Distance(pos, waypoint);

        if (dist <= step || dist <= ArrivalRadius)
        {
            SnapToWorldXZ(waypoint);
            path.RemoveAt(0);
            if (path.Count == 0)
                return true;
            return false;
        }

        Vector3 nextPos = Vector3.MoveTowards(pos, waypoint, step);
        nextPos.y = GroundY;
        transform.position = nextPos;
        moveVelocity = Vector3.zero;
        return false;
    }

    static float HorizontalDistSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    bool MoveTowardStraight(Vector3 target)
    {
        target.y = GroundY;
        Vector3 pos = transform.position;
        pos.y = GroundY;
        float dist = Vector3.Distance(pos, target);
        float step = moveSpeed * Time.deltaTime;
        if (dist <= step || dist <= ArrivalRadius)
        {
            SnapToWorldXZ(target);
            return true;
        }
        Vector3 nextPos = Vector3.MoveTowards(pos, target, step);
        nextPos.y = GroundY;
        transform.position = nextPos;
        return false;
    }
}
