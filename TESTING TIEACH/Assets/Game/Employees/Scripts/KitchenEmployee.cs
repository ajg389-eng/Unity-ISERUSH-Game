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
    public const int MaxUpgradeLevel = 3;

    static readonly float[] TransportRatesByLevel = { 5f, 10f, 15f, 20f };
    /// <summary>Cost to go from level 0→1, 1→2, 2→3.</summary>
    static readonly int[] UpgradeCosts = { 50, 100, 200 };

    public float moveSpeed = 3f;
    [Tooltip("Unused for grid travel (workers move cell-to-cell). Kept for inspector compatibility.")]
    public float moveSmoothTime = 0.12f;
    [Tooltip("Upgrade stars (0–3). Carry capacity 1–4 and transport 5/10/15/20 items per min.")]
    [Range(0, MaxUpgradeLevel)]
    [SerializeField] int upgradeLevel;
    public float groundHeight;
    public GridManager grid;
    [Tooltip("How close to a cell center counts as arrived (then snaps exactly to center).")]
    public float cellArrivalDistance = 0.04f;

    [Header("Identity")]
    public string employeeName = "Worker";
    [Tooltip("Wave played while this worker needs player attention.")]
    public AnimationClip attentionWaveClip;

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
    [HideInInspector] public KitchenFlowKind assignedFlow = KitchenFlowKind.None;
    [HideInInspector] public string assignedFlowName;

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
    public int UpgradeLevel => Mathf.Clamp(upgradeLevel, 0, MaxUpgradeLevel);
    public bool IsMaxUpgraded => UpgradeLevel >= MaxUpgradeLevel;

    /// <summary>How many items this worker can carry per trip (1–4 by stars).</summary>
    public int CarryCapacity => UpgradeLevel + 1;

    /// <summary>Effective throughput from carry capacity (5 / 10 / 15 / 20 per min).</summary>
    public float transportItemsPerMinute => TransportRatesByLevel[UpgradeLevel];

    /// <summary>Cost of the next upgrade, or -1 if maxed.</summary>
    public int GetNextUpgradeCost()
    {
        int level = UpgradeLevel;
        if (level >= MaxUpgradeLevel) return -1;
        return UpgradeCosts[level];
    }

    public bool TryUpgrade(MoneyManager money = null)
    {
        int cost = GetNextUpgradeCost();
        if (cost < 0) return false;
        if (money == null)
            money = FindObjectOfType<MoneyManager>();
        if (cost > 0)
        {
            if (money == null || !money.TrySpend(cost))
                return false;
            Sfx.Play(SfxId.SpendMoney);
        }
        upgradeLevel = Mathf.Min(MaxUpgradeLevel, UpgradeLevel + 1);
        return true;
    }

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

    /// <summary>Legacy reorder helper — kept for compatibility; priority UI was removed.</summary>
    public bool MoveStationPriority(int fromIndex, int toIndex)
    {
        if (operatedStations == null || fromIndex < 0 || fromIndex >= operatedStations.Count)
            return false;
        toIndex = Mathf.Clamp(toIndex, 0, operatedStations.Count - 1);
        if (fromIndex == toIndex) return false;

        GameObject station = operatedStations[fromIndex];
        operatedStations.RemoveAt(fromIndex);
        operatedStations.Insert(toIndex, station);
        SyncFromOperatedStations();
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
        return true;
    }

    /// <summary>Returns list index of a station type on this worker, or int.MaxValue if missing.</summary>
    public int GetStationPriority(StationType stationType)
    {
        if (operatedStations == null) return int.MaxValue;
        for (int i = 0; i < operatedStations.Count; i++)
        {
            StationType? type = GetStationTypeFrom(operatedStations[i]);
            if (type.HasValue && type.Value == stationType)
                return i;
        }
        return int.MaxValue;
    }

    public void RemoveOperatedStation(GameObject station)
    {
        if (operatedStations == null || station == null) return;
        operatedStations.Remove(station);
        SyncFromOperatedStations();
        if (operatedStations.Count == 0)
            AbortCurrentWork();
    }

    public void ClearAllOperatedStations()
    {
        AbortCurrentWork();
        if (operatedStations == null) return;

        var copy = new List<GameObject>(operatedStations);
        foreach (var go in copy)
        {
            if (go == null) continue;
            var node = go.GetComponent<StationNode>();
            if (node != null && node.assignedWorker == this)
                node.ClearWorker();
            else
                RemoveOperatedStation(go);
        }

        assignedFlow = KitchenFlowKind.None;
        assignedFlowName = "";
        SyncFromOperatedStations();
    }

    /// <summary>Drop the active job so the worker becomes idle and reassignable.</summary>
    public void AbortCurrentWork()
    {
        if (currentJob != null && manager != null)
            manager.ReleaseJob(currentJob);
        currentJob = null;
        ClearHeldInventory();
        deliverTarget = null;
        step = Step.None;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        ShowTaskBar = false;
        TaskProgress = 0f;
        cashierTray.Clear();
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
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

    public string GetCompactRouteText()
    {
        if (operatedStations == null || operatedStations.Count == 0)
            return "Unassigned";

        var parts = new List<string>();
        GameObject cur = operatedStations[0];
        var seen = new HashSet<GameObject>();
        while (cur != null && seen.Add(cur))
        {
            var node = StationNode.EnsureOn(cur);
            parts.Add(node != null ? node.DisplayName : cur.name);
            cur = node != null ? node.outputTarget : null;
        }
        return string.Join(" → ", parts);
    }

    public string GetAssignmentDetailText() => GetCompactRouteText();

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
            case Step.GoToDrink: return FormatTask("Walking to Drink Fountain", product);
            case Step.AtDrink: return FormatTask("Pouring at Drink Fountain", product);
            case Step.GoToOutput:
            case Step.GoToHeatLamp:
                return FormatTask("Walking to deliver at " + GetDeliverTargetLabel(), product);
            case Step.AtOutput:
            case Step.AtHeatLamp:
                return FormatTask("Delivering to " + GetDeliverTargetLabel(), product);
            case Step.CashierGoDrink: return "Cashier — walking to drink station";
            case Step.CashierAtDrink: return "Cashier — pouring drink";
            case Step.CashierGoFood: return "Cashier — walking to Pickup Station";
            case Step.CashierAtFood: return "Cashier — picking up food";
            case Step.CashierReturnServe: return "Cashier — serving full order";
        }

        var reg = GetRegisterStation();
        if (reg != null)
        {
            var front = reg.GetFrontCustomer();
            var order = front != null ? front.GetOrder() : null;

            if (cashierTray != null && cashierTray.Count > 0)
            {
                if (TrayFulfillsOrder(order))
                    return "Cashier — serving full order";
                return "Cashier — collecting order items";
            }

            if (front != null && order != null)
            {
                var next = GetNextCashierFetch(order);
                if (next == null)
                    return "Cashier — waiting for kitchen items";
                return "Cashier — collecting customer order";
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
        GoToDrink, AtDrink,
        GoToHeatLamp, AtHeatLamp, // legacy aliases unused — delivery uses GoToOutput
        GoToOutput, AtOutput,
        // Cashier fetch / serve
        CashierGoDrink, CashierAtDrink,
        CashierGoFood, CashierAtFood,
        CashierReturnServe
    }

    Step step;
    bool hasPatty;
    /// <summary>Items in this worker's hands for the current job batch (1..CarryCapacity).</summary>
    int heldUnits;
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
    bool cashierDrinksOnly;
    bool cashierFoodOnly;
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

        if (heldDeliveryItem == null && heldUnits > 0)
        {
            string unitName = "Item";
            if (manager != null && manager.PattyItem != null)
                unitName = FormatItemName(manager.PattyItem);
            else if (manager != null && manager.FriesItem != null && currentJob != null
                     && currentJob.product == manager.FriesItem)
                unitName = FormatItemName(manager.FriesItem);
            parts.Add(heldUnits > 1 ? unitName + " x" + heldUnits : unitName);
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
        heldUnits = 0;
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
        if (grid != null)
            grid.GridChanged += OnGridChanged;
        if (groundHeight == 0f && grid != null)
            groundHeight = grid.Origin.y;
        if (GetComponent<EmployeeInventoryLabel>() == null)
            gameObject.AddComponent<EmployeeInventoryLabel>();
        PartyCharacterAnimator.EnsureOn(gameObject);
        var look = PartyCharacterRandomizer.EnsureOn(gameObject);
        if (look != null)
            look.hatChance = 0.85f;
    }

    void OnGridChanged()
    {
        path.Clear();
    }

    void OnDestroy()
    {
        if (grid != null)
            grid.GridChanged -= OnGridChanged;
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
        heldUnits = 0;
        if (job != null && job.heldUnits > 0)
            heldUnits = job.heldUnits;
        else if (job != null && job.hasPatty)
            heldUnits = 1;
        SyncHasPattyFlag();
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

    int BatchSize => Mathf.Clamp(heldUnits > 0 ? heldUnits : CarryCapacity, 1, CarryCapacity);

    void SyncHasPattyFlag()
    {
        hasPatty = heldUnits > 0;
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
            StationType.Drink => Step.GoToDrink,
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
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
        if (currentJob == null || manager == null)
        {
            currentJob = null;
            step = Step.None;
            return;
        }

        currentJob.hasPatty = heldUnits > 0;
        currentJob.heldUnits = heldUnits;
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
        if (heldUnits <= 0)
            heldUnits = 1;
        SyncHasPattyFlag();
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
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            currentJob = null;
            step = Step.None;
            deliverTarget = null;
            return;
        }

        var lamp = deliverTarget.GetComponent<HeatLampStation>();
        if (lamp != null)
        {
            int toDeliver = Mathf.Max(1, heldUnits);
            int delivered = 0;
            while (delivered < toDeliver && lamp.HasSpace)
            {
                if (!manager.DeliverToHeatLamp(currentJob.order, lamp))
                    break;
                delivered++;
            }
            if (delivered == 0)
            {
                if (!lamp.HasSpace)
                    return; // wait for space
                Debug.LogWarning("KitchenEmployee: could not deliver to heat lamp.", this);
                return;
            }

            heldUnits = Mathf.Max(0, heldUnits - delivered);
            if (heldUnits > 0)
            {
                // Still carrying more — wait for lamp space, then continue.
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }

            ClearHeldInventory();
            awaitingOutputDelivery = false;
            heldDeliveryItem = null;
            ProductionJob finished = currentJob;
            currentJob = null;
            deliverTarget = null;
            step = Step.None;
            stateTimer = 0f;
            path.Clear();
            pathDestination = Vector3.zero;
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            manager.CompleteJob(finished);
            // Immediately queue/start the next cycle so cooks don't stand idle after a delivery.
            manager.RequestImmediateProduction();
            manager.TryAssignJobTo(this);
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

        currentJob.hasPatty = heldUnits > 0;
        currentJob.heldUnits = heldUnits;
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
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
    }

    void Update()
    {
        SnapToGround();
        if (manager == null)
            manager = ProductionManager.Instance;

        bool needsAttention = NeedsPlayerAttention();
        SetAttentionWave(needsAttention);
        if (needsAttention)
        {
            ShowTaskBar = false;
            TaskProgress = 0f;
            path.Clear();
            pathDestination = Vector3.zero;
            return;
        }

        if (currentJob != null)
        {
            RunWorkflow();
            return;
        }

        RunRegisterDuty();
    }

    bool NeedsPlayerAttention()
    {
        if (!CanTakeJobs)
            return true;
        if (manager == null || currentJob == null)
            return false;

        if (step == Step.AtFreezer && heldUnits <= 0 && !manager.HasPattyInStock())
            return true;

        if (step == Step.GoToFryer
            && manager.IsEmployeeOnFryerTile(transform.position, this)
            && !manager.HasFriesInStock())
            return true;

        if (step == Step.AtDrink && heldUnits <= 0 && !manager.HasDrinkInStock())
            return true;

        if (awaitingOutputDelivery && deliverTarget == null)
            return true;

        if (step == Step.GoToOutput && deliverTarget == null)
            return true;

        if (step == Step.AtOutput && deliverTarget != null)
        {
            var lamp = deliverTarget.GetComponent<HeatLampStation>();
            if (lamp != null && !lamp.HasSpace)
                return true;
        }

        return false;
    }

    void SetAttentionWave(bool on)
    {
        var character = PartyCharacterAnimator.EnsureOn(gameObject);
        if (character != null)
            character.SetAttentionWave(on, attentionWaveClip);
    }

    Register GetServiceRegister()
    {
        var own = GetRegisterStation();
        Register best = null;
        int bestScore = -1;
        var found = UnityEngine.Object.FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            var r = found[i];
            if (r == null || !r.isEnabled) continue;
            int score = r.PickupCount * 10 + r.QueueCount;
            if (own != null && r == own)
                score += 5;
            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }
        if (best != null) return best;
        return own != null && own.isEnabled ? own : null;
    }

    /// <summary>
    /// Register-assigned workers stay at the counter (order taking). Kitchen cooks never do this —
    /// guests grab finished food from the heat lamp pass themselves.
    /// </summary>
    public bool ShouldDeliverInsteadOfCook()
    {
        return GetRegisterStation() != null;
    }

    Vector3 GetIdleStandPosition(Register reg)
    {
        if (operatedStations != null)
        {
            foreach (var go in operatedStations)
            {
                if (go == null) continue;
                return GetInteractionPosition(go);
            }
        }
        return reg != null ? reg.GetInteractionPosition() : transform.position;
    }

    void RunRegisterDuty()
    {
        ShowTaskBar = false;
        TaskProgress = 0f;

        // Kitchen cooks never run the register — only register-assigned workers.
        if (GetRegisterStation() == null)
        {
            ClearCashierTray();
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            return;
        }

        var reg = GetServiceRegister();
        if (reg == null || !reg.isEnabled)
        {
            ClearCashierTray();
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            return;
        }

        // Abort legacy fetch/serve paths — guests grab food from the heat lamp themselves.
        if (step == Step.CashierGoDrink || step == Step.CashierAtDrink
            || step == Step.CashierGoFood || step == Step.CashierAtFood
            || step == Step.CashierReturnServe)
        {
            ClearCashierTray();
        }

        // Cashier presence is required to take orders (Register.TryTakeFrontOrder).
        // Stay on the stand so guests can finish ordering and move to the heat lamp.
        Vector3 idle = GetIdleStandPosition(reg);
        if (CloseEnough(idle, 0.35f))
        {
            FaceStationObject(reg.gameObject);
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Register);
        }
        else
        {
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            MoveToward(idle);
        }
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

    Dictionary<ItemDefinition, int> BuildTrayCounts()
    {
        var counts = new Dictionary<ItemDefinition, int>();
        for (int i = 0; i < cashierTray.Count; i++)
        {
            var item = cashierTray[i];
            if (item == null) continue;
            counts[item] = counts.TryGetValue(item, out int c) ? c + 1 : 1;
        }
        return counts;
    }

    bool TrayFulfillsOrder(CustomerOrder order) => TrayCoversLines(order, drinksOnly: false, foodOnly: false);

    bool TrayReadyToServe(CustomerOrder order) => TrayFulfillsOrder(order);

    bool TrayCoversLines(CustomerOrder order, bool drinksOnly, bool foodOnly)
    {
        if (order?.lines == null || order.GetTotalQuantity() <= 0) return false;
        var config = manager != null ? manager.orderConfig : null;
        var trayCounts = BuildTrayCounts();
        bool any = false;
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            bool drink = config != null && config.IsDrink(line.item);
            if (drinksOnly && !drink) continue;
            if (foodOnly && drink) continue;
            any = true;
            trayCounts.TryGetValue(line.item, out int held);
            if (held < line.quantity) return false;
        }
        return any;
    }

    ItemDefinition GetNextCashierFetch(CustomerOrder order, bool drinksOnly = false, bool foodOnly = false)
    {
        if (order?.lines == null || manager == null) return null;
        var config = manager.orderConfig;
        var trayCounts = BuildTrayCounts();

        if (!drinksOnly)
        {
            var lamp = manager.HeatLamp;
            foreach (var line in order.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (config != null && config.IsDrink(line.item)) continue;
                trayCounts.TryGetValue(line.item, out int held);
                if (held >= line.quantity) continue;
                if (lamp != null && lamp.HasSingleItem(line.item))
                    return line.item;
            }
        }

        if (!foodOnly)
        {
            foreach (var line in order.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (config == null || !config.IsDrink(line.item)) continue;
                trayCounts.TryGetValue(line.item, out int held);
                if (held >= line.quantity) continue;
                if (manager.HasDrinkInStock())
                    return line.item;
            }
        }

        return null;
    }

    /// <summary>
    /// After picking something up: keep collecting until the tray matches the full order,
    /// otherwise wait (do not serve a partial order).
    /// </summary>
    void ContinueCashierAfterPickup(Register reg)
    {
        cashierFetchItem = null;
        stateTimer = 0f;
        path.Clear();

        var order = cashierCustomer != null ? cashierCustomer.GetOrder() : null;
        if (TrayFulfillsOrder(order))
        {
            step = Step.CashierReturnServe;
            return;
        }

        var next = GetNextCashierFetch(order);
        if (next != null)
        {
            cashierFetchItem = next;
            bool isDrink = manager != null && manager.orderConfig != null && manager.orderConfig.IsDrink(next);
            step = isDrink ? Step.CashierGoDrink : Step.CashierGoFood;
            return;
        }

        // Still missing items that are not ready yet — wait near the register with the partial tray.
        step = Step.None;
        if (reg != null)
            MoveToward(GetIdleStandPosition(reg));
    }

    bool CloseEnough(Vector3 world, float radius = 1f)
    {
        return HorizontalDistSq(transform.position, world) <= radius * radius;
    }

    void WalkTo(Vector3 world)
    {
        if (CloseEnough(world, 0.2f)) return;
        if (!MoveToward(world))
            MoveTowardStraight(world);
    }

    void RunCashierStep(Register reg)
    {
        switch (step)
        {
            case Step.CashierGoDrink:
            {
                Vector3 drinkPos = manager.GetDrinkStationPosition(this);
                if (CloseEnough(drinkPos, 0.95f) || manager.IsEmployeeOnDrinkTile(transform.position, this))
                {
                    step = Step.CashierAtDrink;
                    stateTimer = 0f;
                    break;
                }
                WalkTo(drinkPos);
                break;
            }

            case Step.CashierAtDrink:
            {
                FaceStationObject(GetOperatedStationObject(StationType.Drink) ?? GetDrinkStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Drink);
                Vector3 drinkPos = manager.GetDrinkStationPosition(this);
                if (!CloseEnough(drinkPos, 1.1f) && !manager.IsEmployeeOnDrinkTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
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
                    ContinueCashierAfterPickup(reg);
                }
                break;
            }

            case Step.CashierGoFood:
            {
                Vector3 lampPos = manager.GetHeatLampPosition();
                if (CloseEnough(lampPos, 0.95f) || manager.IsEmployeeOnHeatLampTile(transform.position))
                {
                    step = Step.CashierAtFood;
                    stateTimer = 0f;
                    break;
                }
                WalkTo(lampPos);
                break;
            }

            case Step.CashierAtFood:
            {
                FaceStationObject(manager != null && manager.HeatLamp != null ? manager.HeatLamp.gameObject : null);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Plating);
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
                ContinueCashierAfterPickup(reg);
                break;
            }

            case Step.CashierReturnServe:
            {
                if (cashierCustomer == null || cashierTray.Count == 0)
                {
                    step = Step.None;
                    break;
                }

                var serveOrder = cashierCustomer.GetOrder();
                if (!TrayFulfillsOrder(serveOrder))
                {
                    // Never hand over a partial order — resume collecting.
                    ContinueCashierAfterPickup(reg);
                    break;
                }

                // Serve from the register (employee side) — never walk through the counter to the customer.
                Vector3 servePos = reg.GetInteractionPosition();
                if (!MoveToward(servePos))
                    break;

                if (!reg.HasWorkerOnDuty())
                {
                    // Arrived near register but not on the stand tile yet — keep approaching.
                    MoveTowardStraight(servePos);
                    break;
                }

                ShowTaskBar = true;
                TaskProgress = 1f;

                // Hand over the entire tray in one serve action.
                while (cashierTray.Count > 0)
                {
                    var item = cashierTray[0];
                    if (!reg.TryDeliverItem(cashierCustomer, item))
                        break;
                    cashierTray.RemoveAt(0);
                }

                if (cashierTray.Count == 0)
                {
                    cashierFetchItem = null;
                    cashierCustomer = null;
                    step = Step.None;
                }
                break;
            }
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
                FaceStationObject(GetOperatedStationObject(StationType.Freezer) ?? GetFreezerStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Freezer);
                if (!manager.IsEmployeeOnFreezerTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
                    step = Step.GoToFreezer;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                // Already took the item — keep retrying handoff (e.g. output assigned late)
                if (heldUnits > 0 || awaitingOutputDelivery)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    FinishStepAndHandoff();
                    break;
                }
                stateTimer += Time.deltaTime;
                float freezerTime = manager.GetFreezerInteractionTime(this) * CarryCapacity;
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, freezerTime));
                if (stateTimer >= freezerTime)
                {
                    int taken = 0;
                    while (taken < CarryCapacity && manager.HasPattyInStock())
                    {
                        if (!manager.TryTakePattyFromFreezer(this))
                            break;
                        taken++;
                    }
                    if (taken <= 0)
                    {
                        TaskProgress = 1f;
                        stateTimer = freezerTime;
                        break;
                    }
                    heldUnits = taken;
                    SyncHasPattyFlag();
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
                            FaceStationObject(grill.gameObject);
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }

                        // Salvage a leftover cooked patty so a previous stuck cycle can't block forever.
                        if (grill != null && grill.IsCooked())
                        {
                            if (manager.TakePattyFromGrill(this))
                            {
                                if (heldUnits <= 0)
                                    heldUnits = 1;
                                SyncHasPattyFlag();
                                FinishStepAndHandoff();
                                break;
                            }
                        }

                        if (heldUnits > 0 && manager.PlacePattyOnGrill(this))
                        {
                            step = Step.AtGrill;
                            stateTimer = 0f;
                        }
                        else if (grill != null && grill.HasPattyOnGrill)
                        {
                            // Already cooking — watch this cycle instead of path-thrashing.
                            FaceStationObject(grill.gameObject);
                            step = Step.AtGrill;
                            stateTimer = 0f;
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                        }
                        else
                        {
                            path.Clear();
                            pathDestination = Vector3.zero;
                        }
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtGrill:
                FaceStationObject(GetOperatedStationObject(StationType.Grill) ?? GetGrillStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Grill);
                if (!manager.IsEmployeeOnGrillTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
                    step = Step.GoToGrill;
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
                float oneGrillCycle = manager.GetGrillPlaceTime(this) + manager.GetGrillCookTime(this)
                    + manager.GetGrillWaitAfterCookedTime(this) + manager.GetGrillTakeTime(this);
                // Grill cooks one patty at a time; batch size only scales total wait for multi-carry.
                float grillTotal = oneGrillCycle * Mathf.Max(1, BatchSize);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, grillTotal));

                var grillNow = manager.GetGrillFor(this);
                bool readyToTake = grillNow != null && grillNow.IsCooked()
                    && stateTimer >= oneGrillCycle;

                if (readyToTake || stateTimer >= grillTotal)
                {
                    // Always take the cooked patty off the grill before handoff.
                    // (heldUnits stays > 0 during cooking for batch carry — do not treat that as "already done".)
                    if (manager.TakePattyFromGrill(this))
                    {
                        if (heldUnits <= 0)
                            heldUnits = 1;
                        SyncHasPattyFlag();
                        FinishStepAndHandoff();
                    }
                    else if (grillNow != null && !grillNow.HasPattyOnGrill && heldUnits > 0)
                    {
                        // Nothing on grill but we're holding food — continue the route.
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
                FaceStationObject(GetOperatedStationObject(StationType.Assembly) ?? GetAssemblyStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Assembly);
                if (!manager.IsEmployeeOnAssemblyTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
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
                float assemblyTime = manager.GetAssemblyInteractionTime(this) * BatchSize;
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
                            FaceStationObject(GetOperatedStationObject(StationType.Fryer) ?? GetFryerStation()?.gameObject);
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        if (manager.TryLoadFryer(this))
                        {
                            // Consume extra fries for the upgraded carry batch (first unit already consumed by load).
                            int batch = CarryCapacity;
                            int loaded = 1;
                            var inv = KitchenInventory.Instance;
                            while (loaded < batch && manager.HasFriesInStock())
                            {
                                if (inv != null && manager.FriesItem != null
                                    && !inv.TryConsume(manager.FriesItem, 1))
                                    break;
                                loaded++;
                            }
                            heldUnits = loaded;
                            SyncHasPattyFlag();
                            step = Step.AtFryer;
                            stateTimer = 0f;
                        }
                        else { path.Clear(); pathDestination = Vector3.zero; }
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtFryer:
                FaceStationObject(GetOperatedStationObject(StationType.Fryer) ?? GetFryerStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Fryer);
                if (!manager.IsEmployeeOnFryerTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
                    step = Step.GoToFryer;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                stateTimer += Time.deltaTime;
                float fryerTotal = manager.GetFryerTotalTime(this) * BatchSize;
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
                    {
                        if (heldUnits <= 0)
                            heldUnits = 1;
                        SyncHasPattyFlag();
                        FinishStepAndHandoff();
                    }
                }
                break;

            case Step.GoToDrink:
                if (MoveToward(manager.GetDrinkStationPosition(this)))
                {
                    if (manager.IsEmployeeOnDrinkTile(transform.position, this))
                    {
                        step = Step.AtDrink;
                        stateTimer = 0f;
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtDrink:
                FaceStationObject(GetOperatedStationObject(StationType.Drink) ?? GetDrinkStation()?.gameObject);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Drink);
                if (!manager.IsEmployeeOnDrinkTile(transform.position, this))
                {
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
                    step = Step.GoToDrink;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
                if (heldUnits > 0 || awaitingOutputDelivery)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    FinishStepAndHandoff();
                    break;
                }
                stateTimer += Time.deltaTime;
                float drinkProductionTime = manager.GetDrinkInteractionTime(this) * CarryCapacity;
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, drinkProductionTime));
                if (stateTimer >= drinkProductionTime)
                {
                    int poured = 0;
                    while (poured < CarryCapacity && manager.HasDrinkInStock())
                    {
                        if (!manager.TryDispenseDrink(this))
                            break;
                        poured++;
                    }
                    if (poured <= 0)
                    {
                        TaskProgress = 1f;
                        stateTimer = drinkProductionTime;
                        break;
                    }
                    heldUnits = poured;
                    SyncHasPattyFlag();
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
                FaceStationObject(deliverTarget);
                SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Plating);
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
            // Pathfind via nearest walkable cell, then finish on the exact stand point when close.
            Vector3 pathQuery = grid.GetCellCenter(exactTarget);
            path = grid.GetPath(transform.position, pathQuery);
            bool hadRoute = path.Count > 0;
            float maxApproach = grid.cellSize * 1.25f;

            while (path.Count > 0 && HorizontalDistSq(transform.position, path[0]) <= ArrivalRadius * ArrivalRadius)
            {
                SnapToWorldXZ(path[0]);
                path.RemoveAt(0);
            }

            if (path.Count > 0)
            {
                Vector3 last = path[path.Count - 1];
                if (HorizontalDistSq(last, exactTarget) <= maxApproach * maxApproach)
                    path[path.Count - 1] = exactTarget;
            }
            else if (hadRoute || HorizontalDistSq(transform.position, exactTarget) <= maxApproach * maxApproach)
            {
                // Already on/near the goal cell — walk the last step onto the interaction point
                // (do not treat stripped waypoints as "no route").
                path.Add(exactTarget);
            }
            else
            {
                // Truly unreachable without cutting through walls/stations.
                return false;
            }
        }

        if (path.Count == 0)
            return false;

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
        FaceMoveTarget(waypoint);
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
        FaceMoveTarget(target);
        return false;
    }

    void FaceMoveTarget(Vector3 worldPoint)
    {
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.FaceTowardAdjacent(worldPoint, smooth: true);
    }

    void FaceStationObject(GameObject station)
    {
        if (station == null) return;
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.FaceTowardAdjacentObject(station, smooth: true);
    }

    void SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind work)
    {
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.SetStationWork(work);
    }
}
