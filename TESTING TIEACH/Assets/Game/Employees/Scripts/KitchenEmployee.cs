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
/// Hired employee. Idle until assigned to a production flow.
/// Pipelines define work order (burger: Freezer→Grill→Assembly). Delivery is always via Assign Output.
/// Cashiers fetch drinks + heat-lamp food for customers.
/// </summary>
public class KitchenEmployee : MonoBehaviour
{
    public enum WorkerActivityState
    {
        Unassigned,
        Idle,
        Returning,
        Traveling,
        Working,
        Delivering,
        Blocked,
        Cashier
    }

    public enum HeldPreviewKind
    {
        None,
        RawPatty,
        CookedPatty,
        RawFries,
        CookedFries,
        Burger,
        Drink,
        ItemPrefab
    }

    // A worker assigned to a flow must be able to operate its complete graph,
    // including ingredient branches. Keep this equal to the flow node limit so
    // one worker can run an entire flow without requiring artificial staffing.
    public const int MaxStations = 16;
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
    bool returningToFlowStart;
    GameObject returnFlowTarget;

    public bool IsIdle => currentJob == null && !returningToFlowStart;
    public bool HasJob => currentJob != null;
    public ProductionJob ActiveJob => currentJob;
    public ItemDefinition CurrentWorkProduct => currentJob != null ? currentJob.CurrentWorkProduct : null;
    public bool IsWorkingOn(ProductionJob job) => job != null && currentJob == job;
    public int OperatedStationCount => operatedStations != null ? operatedStations.Count : 0;
    public int AssignedStationCount => OperatedStationCount;
    public bool CanTakeJobs => OperatedStationCount > 0;
    public WorkerActivityState CurrentActivity
    {
        get
        {
            if (!CanTakeJobs) return WorkerActivityState.Unassigned;
            if (GetRegisterStation() != null) return WorkerActivityState.Cashier;
            if (IsCurrentlyBlocked()) return WorkerActivityState.Blocked;
            if (returningToFlowStart) return WorkerActivityState.Returning;
            if (step == Step.GoToOutput || step == Step.AtOutput
                || step == Step.GoToHeatLamp || step == Step.AtHeatLamp)
                return WorkerActivityState.Delivering;
            if (step.ToString().StartsWith("GoTo", StringComparison.Ordinal))
                return WorkerActivityState.Traveling;
            if (currentJob != null) return WorkerActivityState.Working;
            return WorkerActivityState.Idle;
        }
    }
    public int UpgradeLevel => Mathf.Clamp(upgradeLevel, 0, MaxUpgradeLevel);
    public void RestoreUpgradeLevel(int level) { upgradeLevel = Mathf.Clamp(level, 0, MaxUpgradeLevel); }
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

    bool IsInAssignedFlow(GameObject station)
    {
        if (station == null) return false;
        ProductionManager production = manager != null ? manager : ProductionManager.Instance;
        return production != null && production.IsStationInWorkerFlow(this, station);
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
            if (node != null && node.IsWorkerAssigned(this))
                node.RemoveWorker(this);
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
        returningToFlowStart = false;
        returnFlowTarget = null;
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
        foreach (GameObject station in WorkflowAnalysis.GetOrderedRoute(this))
        {
            if (station == null) continue;
            var node = StationNode.EnsureOn(station);
            parts.Add(node != null ? node.DisplayName : station.name);
        }
        return string.Join(" → ", parts);
    }

    public string GetAssignmentDetailText() => GetCompactRouteText();

    /// <summary>Human-readable description of what this worker is doing right now.</summary>
    public string GetCurrentTaskDescription()
    {
        string product = GetActiveProductLabel();
        string blockedReason = GetBlockedReason();
        if (!string.IsNullOrEmpty(blockedReason))
            return "Blocked: " + blockedReason;

        switch (step)
        {
            case Step.GoToFreezer: return FormatTask("Walking to Freezer", product);
            case Step.AtFreezer: return FormatTask("Taking patty from Freezer", product);
            case Step.GoToGrill: return FormatTask("Walking to Grill", product);
            case Step.AtGrill: return FormatTask("Cooking at Grill", product);
            case Step.GoToAssembly: return FormatTask("Walking to Assembly", product);
            case Step.AtAssembly: return FormatTask("Assembling order", product);
            case Step.GoToCutting: return FormatTask("Walking to Cutting Station", product);
            case Step.AtCutting:
                return FormatTask(manager != null && manager.orderConfig != null
                    && manager.orderConfig.IsFries(currentJob != null ? currentJob.product : null)
                        ? "Slicing potatoes" : "Slicing recipe ingredients", product);
            case Step.GoToPantry: return FormatTask("Walking to Pantry for ingredients", product);
            case Step.AtPantry: return FormatTask("Collecting recipe ingredients", product);
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

        if (returningToFlowStart)
            return "Returning to flow start";

        return "Idle — waiting for work";
    }

    public string GetBlockedReason()
    {
        if (!CanTakeJobs) return "No flow or stations assigned";
        if (manager == null || currentJob == null) return string.Empty;

        if ((awaitingOutputDelivery || step == Step.GoToOutput || step == Step.AtOutput)
            && deliverTarget == null)
            return "No downstream station is connected in this flow";

        if (deliverTarget != null)
        {
            HeatLampStation lamp = deliverTarget.GetComponent<HeatLampStation>();
            if (lamp != null && manager.GetReservedInputUnits(currentJob) <= 0 && !lamp.HasSpace)
                return "Pickup Station is full";
        }

        if (step == Step.AtFreezer)
        {
            if (heldUnits <= 0 && !manager.HasPattyInStock(this)) return "Raw patties are out of stock";
            if (heldUnits > 0 && manager.GetReservedInputUnits(currentJob) <= 0)
                return "Grill cannot accept the carried batch";
        }
        if (step == Step.AtGrill && manager.GetReservedInputUnits(currentJob) <= 0)
        {
            GrillStation grill = manager.GetGrillFor(this);
            if (grill != null && grill.IsCooked()) return "Next station has no free input space";
        }
        if (step == Step.AtAssembly)
        {
            AssemblyStation station = GetAssemblyStation(currentJob.CurrentWorkProduct);
            if (station != null)
            {
                if (!station.CanStoreOutput(1)) return "Assembly output buffer is full";
                if (!station.HasRequiredInputs(currentJob.CurrentWorkProduct, ingredientsHeld,
                        Mathf.Max(1, heldUnits)))
                    return "Assembly is missing a required ingredient";
            }
        }
        if (step == Step.AtPantry)
        {
            PantryStation pantry = GetPantryStation();
            ItemDefinition required = currentJob.isAssemblySupply
                ? manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget)
                : manager.GetPantryItemForProduct(currentJob.product);
            if (pantry != null && required != null && !pantry.HasItem(required))
                return FormatItemName(required) + " is out of stock";
        }
        if (step == Step.AtDrink && heldUnits <= 0 && !manager.HasDrinkInStock())
            return "Drink stock is empty";
        if ((step == Step.GoToCutting || step == Step.AtCutting) && currentJob.isAssemblySupply)
        {
            CuttingStation cutting = GetCuttingStation();
            if (cutting != null && !cutting.HasCarriedSupply(manager.orderConfig,
                    ingredientsHeld, Mathf.Max(1, heldUnits)))
            {
                CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
                return FormatItemName(recipe != null ? recipe.input : null)
                    + " has not reached the Cutting Station";
            }
        }
        return string.Empty;
    }

    public GameObject GetCurrentStationObject()
    {
        if (currentJob == null || !currentJob.CurrentStationType.HasValue) return null;
        return GetOperatedStationObject(currentJob.CurrentStationType.Value);
    }

    public bool IsActivelyWorkingAt(GameObject station)
    {
        if (station == null || GetCurrentStationObject() != station || !string.IsNullOrEmpty(GetBlockedReason()))
            return false;
        return step == Step.AtFreezer || step == Step.AtGrill || step == Step.AtAssembly
            || step == Step.AtCutting || step == Step.AtPantry || step == Step.AtFryer
            || step == Step.AtDrink;
    }

    string GetActiveProductLabel()
    {
        if (currentJob?.CurrentWorkProduct != null)
            return FormatItemName(currentJob.CurrentWorkProduct);

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
        if (assignedStations == null)
            assignedStations = new List<StationType>();
        else
            assignedStations.Clear();
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
    public bool CanTakeJobStep(ProductionJob job) => CanTakeJobStep(job, false);

    public bool CanTakeJobStep(ProductionJob job, bool searchEntireFlow)
    {
        return FindStationForJob(job, searchEntireFlow, true) != null;
    }

    /// <summary>
    /// Resolves the exact station a job would use. Idle workers may search all
    /// branches of their assigned flow, while normal balancing remains local.
    /// </summary>
    public GameObject FindStationForJob(ProductionJob job, bool searchEntireFlow,
        bool requireReady)
    {
        if (job == null || job.IsHeatLampStep || !job.CurrentStationType.HasValue)
            return null;

        ProductionManager production = manager != null ? manager : ProductionManager.Instance;
        ProductionFlowPlan flow = production != null ? production.GetFlowForWorker(this) : null;
        if (flow?.stations == null) return null;
        if (job.isAssemblySupply && (job.assemblySupplyTarget == null
            || !flow.stations.Contains(job.assemblySupplyTarget.gameObject)))
            return null;

        // operatedStations can briefly retain entries from a previous assignment.
        // Never let those stale entries escape the worker's current flow graph.
        List<GameObject> candidates = new List<GameObject>();
        if (searchEntireFlow)
        {
            foreach (GameObject station in flow.stations)
                if (station != null)
                    candidates.Add(station);
        }
        else if (operatedStations != null)
        {
            foreach (GameObject station in operatedStations)
                if (station != null && flow.stations.Contains(station))
                    candidates.Add(station);
        }
        if (candidates.Count == 0) return null;

        StationType stationType = job.CurrentStationType.Value;
        if (job.taskPhase == ProductionTaskPhase.CollectOutput)
        {
            GameObject source = job.taskSourceStation;
            if (source == null || !candidates.Contains(source)) return null;
            if (!requireReady) return source;
            GameObject destination = production != null
                ? production.GetFlowOutput(this, source, job)
                : null;
            ItemDefinition item = production != null
                ? production.GetBranchTransferItem(job)
                : null;
            return production != null && production.CanTransferAvailable(
                job, source, destination, item, 1) ? source : null;
        }

        foreach (GameObject candidate in candidates)
        {
            if (candidate == null || !candidate.activeInHierarchy
                || GetStationTypeFrom(candidate) != stationType)
                continue;
            if (job.taskSourceStation != null && candidate != job.taskSourceStation)
                continue;

            if (stationType == StationType.Grill)
            {
                GrillStation grill = candidate.GetComponent<GrillStation>();
                if (grill == null || !grill.CanProcess(job.product)) continue;
                // Station contents are the source of truth. Jobs are rebuilt
                // after loading and recovery, so object-reference order checks
                // can reject a perfectly valid patty and leave every worker idle.
                if (requireReady && !grill.HasPattyOnGrill) continue;
                if (requireReady && grill.IsCooked())
                {
                    GameObject destination = production.GetFlowOutput(this, candidate, job);
                    ItemDefinition item = production.GetBranchTransferItem(job);
                    if (!production.CanTransferAvailable(job, candidate, destination, item, 1))
                        continue;
                }
            }
            else if (stationType == StationType.Freezer)
            {
                FreezerStation freezer = candidate.GetComponent<FreezerStation>();
                ItemDefinition freezerItem = production?.GetBranchTransferItem(job);
                if (freezer == null || freezerItem == null || !freezer.CanSupply(freezerItem)) continue;
                if (requireReady && freezer.GetOutputCount(freezerItem) <= 0) continue;
            }
            else if (stationType == StationType.Pantry)
            {
                PantryStation pantry = candidate.GetComponent<PantryStation>();
                ItemDefinition required = job.isAssemblySupply
                    ? production?.GetAssemblySupplySource(job.assemblySupplyTarget)
                    : production?.GetPantryItemForProduct(job.product);
                if (pantry == null || required == null || !pantry.CanDispense(required)) continue;
                if (requireReady && !pantry.HasItem(required)) continue;
            }
            else if (stationType == StationType.Assembly)
            {
                AssemblyStation assembly = candidate.GetComponent<AssemblyStation>();
                if (assembly == null || !assembly.CanProcess(job.CurrentWorkProduct)) continue;
                if (requireReady)
                {
                    int units = Mathf.Clamp(job.heldUnits > 0 ? job.heldUnits : 1, 1,
                        Mathf.Min(CarryCapacity, assembly.MaxProcessBatch));
                    bool canDeliverStoredOutput = HasRoutableStoredAssemblyOutput(
                        assembly, job.CurrentWorkProduct, job);
                    if (!canDeliverStoredOutput && !assembly.HasRequiredInputs(
                            job.CurrentWorkProduct, job.ingredientsHeld, units))
                        continue;
                }
            }
            else if (stationType == StationType.Cutting)
            {
                CuttingStation cutting = candidate.GetComponent<CuttingStation>();
                bool friesJob = production?.orderConfig != null
                    && production.orderConfig.IsFries(job.product);
                ItemDefinition requiredInput = job.isAssemblySupply
                    ? production?.GetAssemblySupplySource(job.assemblySupplyTarget)
                    : (friesJob ? production?.PotatoItem : null);
                ItemDefinition requiredOutput = job.isAssemblySupply
                    ? production?.GetAssemblySupplyOutput(job.assemblySupplyTarget)
                    : (friesJob ? production?.SlicedPotatoItem : null);
                if (cutting == null || !cutting.CanProcess(requiredInput, requiredOutput)) continue;
                if (requireReady)
                {
                    int requiredUnits = Mathf.Max(1, job.heldUnits);
                    bool carriedInput = ContainsItemCount(job.ingredientsHeld, requiredInput, requiredUnits);
                    bool bufferedInput = cutting.GetInputCount(requiredInput) > 0;
                    bool bufferedOutput = cutting.GetOutputCount(requiredOutput) > 0;
                    if (!carriedInput && !bufferedInput && !bufferedOutput) continue;
                }
            }
            else if (stationType == StationType.Fryer && requireReady)
            {
                FryerStation fryer = candidate.GetComponent<FryerStation>();
                if (fryer == null || (!fryer.IsCooking && !fryer.IsCooked())) continue;
                if (fryer.IsCooked())
                {
                    GameObject destination = production.GetFlowOutput(this, candidate, job);
                    ItemDefinition item = production.GetBranchTransferItem(job);
                    if (!production.CanTransferAvailable(job, candidate, destination, item, 1))
                        continue;
                }
            }
            else if (stationType == StationType.Drink)
            {
                DrinkStation drink = candidate.GetComponent<DrinkStation>();
                if (drink == null || (requireReady && !drink.HasStock(job.product))) continue;
            }

            return candidate;
        }

        return null;
    }

    static bool ContainsItemCount(IReadOnlyList<ItemDefinition> items,
        ItemDefinition required, int amount)
    {
        if (items == null || required == null || amount <= 0) return false;
        int found = 0;
        for (int i = 0; i < items.Count; i++)
            if (items[i] == required && ++found >= amount)
                return true;
        return false;
    }

    /// <summary>
    /// True when this worker has a compatible station for the job, regardless of
    /// temporary input, output, or destination capacity. Used when deciding if a
    /// queued job is obsolete, so ordinary backpressure cannot delete live work.
    /// </summary>
    public bool CanOperateJobStep(ProductionJob job)
    {
        // A branched flow can contain more stations than the worker's local
        // operated-station cache. Structural capability must consider every
        // station in the assigned flow, otherwise valid work on another branch
        // is treated as orphaned and removed from the queue.
        return FindStationForJob(job, true, false) != null;
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
        if (go.GetComponent<CuttingStation>() != null) return StationType.Cutting;
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
        var cut = go.GetComponent<CuttingStation>();
        if (cut != null) return cut.GetInteractionPosition();
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
            if (go == null || !IsInAssignedFlow(go)) continue;
            var reg = go.GetComponent<Register>();
            if (reg != null) return reg;
        }
        return null;
    }

    public FreezerStation GetFreezerStation()
    {
        FreezerStation reserved = GetReservedWorkStation<FreezerStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var fs = go.GetComponent<FreezerStation>();
            if (fs != null) return fs;
        }
        return null;
    }

    public GrillStation GetGrillStation()
    {
        GrillStation reserved = GetReservedWorkStation<GrillStation>();
        if (reserved != null) return reserved;
        if (targetGrill != null && IsInAssignedFlow(targetGrill.gameObject)) return targetGrill;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var gs = go.GetComponent<GrillStation>();
            if (gs != null) return gs;
        }
        return null;
    }

    public AssemblyStation GetAssemblyStation()
    {
        AssemblyStation reserved = GetReservedWorkStation<AssemblyStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var ast = go.GetComponent<AssemblyStation>();
            if (ast != null) return ast;
        }
        return null;
    }

    public AssemblyStation GetAssemblyStation(ItemDefinition stageProduct)
    {
        AssemblyStation reserved = GetReservedWorkStation<AssemblyStation>();
        if (reserved != null && (stageProduct == null || reserved.CanProcess(stageProduct)))
            return reserved;
        if (stageProduct == null) return GetAssemblyStation();
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var station = go.GetComponent<AssemblyStation>();
            if (station != null && station.CanProcess(stageProduct)) return station;
        }
        return null;
    }

    public FryerStation GetFryerStation()
    {
        FryerStation reserved = GetReservedWorkStation<FryerStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var f = go.GetComponent<FryerStation>();
            if (f != null) return f;
        }
        return null;
    }

    public PantryStation GetPantryStation()
    {
        PantryStation reserved = GetReservedWorkStation<PantryStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var pantry = go.GetComponent<PantryStation>();
            if (pantry != null) return pantry;
        }
        return null;
    }

    public CuttingStation GetCuttingStation()
    {
        CuttingStation reserved = GetReservedWorkStation<CuttingStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var station = go.GetComponent<CuttingStation>();
            if (station != null) return station;
        }
        return null;
    }

    public DrinkStation GetDrinkStation()
    {
        DrinkStation reserved = GetReservedWorkStation<DrinkStation>();
        if (reserved != null) return reserved;
        foreach (var go in operatedStations)
        {
            if (go == null || !IsInAssignedFlow(go)) continue;
            var d = go.GetComponent<DrinkStation>();
            if (d != null) return d;
        }
        return null;
    }

    T GetReservedWorkStation<T>() where T : Component
    {
        return currentJob?.reservedWorkStation != null
            && IsInAssignedFlow(currentJob.reservedWorkStation)
            ? currentJob.reservedWorkStation.GetComponent<T>()
            : null;
    }

    float stateTimer;
    List<Vector3> path = new List<Vector3>();
    Vector3 pathDestination;
    Vector3 moveVelocity;
    [Header("Navigation recovery")]
    [Tooltip("Minimum delay before retrying a route that could not be found.")]
    [Min(0.05f)] public float pathRetryDelay = 0.2f;
    [Tooltip("Seconds without movement before a traveling worker refreshes its route.")]
    [Min(0.5f)] public float movementStallTimeout = 2.5f;
    float nextPathAttemptTime;
    int consecutivePathFailures;
    Vector3 lastProgressPosition;
    float lastProgressTime;
    Step lastProgressStep;
    ProductionJob lastProgressJob;
    bool lastProgressReturning;
    int stallRecoveries;
    float ArrivalRadius => Mathf.Max(0.02f, cellArrivalDistance);

    enum Step
    {
        None,
        GoToFreezer, AtFreezer,
        GoToGrill, AtGrill,
        GoToAssembly, AtAssembly,
        GoToCutting, AtCutting,
        GoToPantry, AtPantry,
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
    PartyCharacterAnimator characterAnimator;
    float instantStepBarTimer;
    // Flow workers should select their next step almost immediately. A long
    // cooldown made a healthy handoff look like an idle worker and allowed the
    // production planner to miss short-lived station availability windows.
    const float TaskReevaluationDelay = 0.05f;
    float nextTaskEvaluationTime;

    public bool IsWaitingToReevaluateTasks =>
        currentJob == null && Time.time < nextTaskEvaluationTime;

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

    public int HeldPreviewCount => Mathf.Clamp(
        heldUnits > 0 ? heldUnits : (cashierTray != null ? cashierTray.Count : 1), 1, CarryCapacity);

    public HeldPreviewKind GetHeldPreviewKind(out ItemDefinition item)
    {
        item = ingredientsHeld != null && ingredientsHeld.Count > 0
            ? ingredientsHeld[0]
            : (heldDeliveryItem ?? currentJob?.product);
        if (item == null && ingredientsHeld != null && ingredientsHeld.Count > 0)
            item = ingredientsHeld[0];
        if (item == null && cashierTray != null && cashierTray.Count > 0)
            item = cashierTray[0];
        if (!IsHoldingAnything || item == null)
            return HeldPreviewKind.None;

        CustomerOrderConfig config = manager != null ? manager.orderConfig
            : (ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null);
        if (config != null && config.IsDrink(item))
            return HeldPreviewKind.Drink;

        bool finishedCurrentStep = awaitingOutputDelivery
            || step == Step.GoToOutput || step == Step.AtOutput;
        if (config != null && config.IsFries(item))
            return finishedCurrentStep ? HeldPreviewKind.CookedFries : HeldPreviewKind.RawFries;

        if (config != null && config.IsBurger(item))
        {
            if (deliverTarget != null && deliverTarget.GetComponent<HeatLampStation>() != null)
                return HeldPreviewKind.Burger;

            StationType? currentType = currentJob?.CurrentStationType;
            if (currentType == StationType.Assembly)
            {
                AssemblyRecipeDefinition recipe = config.GetAssemblyRecipe(currentJob.CurrentWorkProduct);
                if (!finishedCurrentStep && recipe != null && recipe.processedInput != null)
                {
                    item = recipe.processedInput;
                    return item.prefab != null ? HeldPreviewKind.ItemPrefab : HeldPreviewKind.CookedPatty;
                }
                return finishedCurrentStep ? HeldPreviewKind.Burger : HeldPreviewKind.CookedPatty;
            }
            if (currentType == StationType.Grill)
                return finishedCurrentStep ? HeldPreviewKind.CookedPatty : HeldPreviewKind.RawPatty;
            return HeldPreviewKind.RawPatty;
        }

        return item.prefab != null ? HeldPreviewKind.ItemPrefab : HeldPreviewKind.None;
    }

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

    void ReturnExcessToKitchenStock(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || KitchenInventory.Instance == null) return;
        KitchenInventory.Instance.AddStock(item, amount);
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
        characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        var look = PartyCharacterRandomizer.EnsureOn(gameObject);
        if (look != null)
            look.hatChance = 0.85f;
        ResetProgressTracking();
    }

    void OnGridChanged()
    {
        ResetNavigationPath(true);
    }

    void ResetNavigationPath(bool retryImmediately = true)
    {
        path.Clear();
        pathDestination = Vector3.zero;
        consecutivePathFailures = 0;
        if (retryImmediately)
            nextPathAttemptTime = 0f;
    }

    void ResetProgressTracking()
    {
        lastProgressPosition = transform.position;
        lastProgressTime = Time.time;
        lastProgressStep = step;
        lastProgressJob = currentJob;
        lastProgressReturning = returningToFlowStart;
        stallRecoveries = 0;
    }

    bool IsTraveling()
    {
        return returningToFlowStart
            || step == Step.GoToFreezer || step == Step.GoToGrill
            || step == Step.GoToAssembly || step == Step.GoToCutting
            || step == Step.GoToPantry || step == Step.GoToFryer
            || step == Step.GoToDrink || step == Step.GoToOutput
            || step == Step.GoToHeatLamp || step == Step.CashierGoDrink
            || step == Step.CashierGoFood || step == Step.CashierReturnServe;
    }

    void RecoverStalledMovement()
    {
        bool contextChanged = lastProgressJob != currentJob
            || lastProgressStep != step
            || lastProgressReturning != returningToFlowStart;
        bool moved = HorizontalDistSq(lastProgressPosition, transform.position) > 0.0025f;
        if (contextChanged || moved || !IsTraveling())
        {
            lastProgressPosition = transform.position;
            lastProgressTime = Time.time;
            lastProgressStep = step;
            lastProgressJob = currentJob;
            lastProgressReturning = returningToFlowStart;
            if (contextChanged || moved)
                stallRecoveries = 0;
            return;
        }

        if (Time.time - lastProgressTime < movementStallTimeout)
            return;

        // Rebuild the route first. If an empty-handed worker remains unable to
        // move after several attempts, release the job claim. Never discard a
        // worker's physical inventory just because navigation is temporarily bad.
        ResetNavigationPath(true);
        lastProgressTime = Time.time;
        stallRecoveries++;
        if (stallRecoveries < 3 || currentJob == null)
            return;

        if (!IsHoldingAnything && !awaitingOutputDelivery)
        {
            ReleaseBlockedAssemblyJobAndReturn();
            ResetProgressTracking();
        }
        else
        {
            // Keep the owned item and reservation intact, but continue periodic
            // route rebuilding until the layout becomes reachable.
            stallRecoveries = 2;
        }
    }

    void OnDestroy()
    {
        if (grid != null)
            grid.GridChanged -= OnGridChanged;
        if (operatedStations != null)
        {
            foreach (var go in new List<GameObject>(operatedStations))
            {
                if (go == null) continue;
                var node = go.GetComponent<StationNode>();
                if (node != null && node.IsWorkerAssigned(this))
                    node.RemoveWorker(this);
            }
        }
        if (currentJob != null && manager != null)
            manager.ReleaseJob(currentJob);
        if (manager != null)
            manager.UnregisterEmployee(this);
    }

    public void AssignJob(ProductionJob job)
    {
        if (currentJob != null) return;
        currentJob = job;
        heldUnits = 0;
        // heldUnits on the job also records the batch size that should be worked
        // at the next station. Only restore it as physical inventory when the job
        // explicitly says the item is still being carried.
        if (job != null && job.hasPatty && job.heldUnits > 0)
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
        returningToFlowStart = false;
        returnFlowTarget = null;
        ResetProgressTracking();
    }

    int BatchSize => Mathf.Clamp(heldUnits > 0 ? heldUnits : CarryCapacity, 1, CarryCapacity);

    int CurrentAssemblyBatchSize
    {
        get
        {
            int requested = currentJob != null && currentJob.heldUnits > 0
                ? currentJob.heldUnits
                : (heldUnits > 0 ? heldUnits : 1);
            AssemblyStation assembly = GetAssemblyStation(
                currentJob != null ? currentJob.CurrentWorkProduct : null);
            int stationBatch = assembly != null ? assembly.MaxProcessBatch : 1;
            return Mathf.Clamp(requested, 1, Mathf.Min(CarryCapacity, stationBatch));
        }
    }

    int ProductionBatchCapacity
    {
        get
        {
            if (currentJob?.pipeline != null)
            {
                for (int i = 0; i < currentJob.pipeline.Length; i++)
                    if (currentJob.pipeline[i] == StationType.Assembly)
                    {
                        AssemblyStation assembly = GetAssemblyStation(currentJob.CurrentWorkProduct);
                        return Mathf.Min(CarryCapacity, assembly != null ? assembly.MaxProcessBatch : 1);
                    }
            }
            return CarryCapacity;
        }
    }

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
            StationType.Cutting => Step.GoToCutting,
            StationType.Pantry => Step.GoToPantry,
            StationType.Fryer => Step.GoToFryer,
            StationType.Drink => Step.GoToDrink,
            _ => Step.GoToHeatLamp
        };
    }

    /// <summary>World station this worker operates for the given type.</summary>
    public GameObject GetOperatedStationObject(StationType stationType)
    {
        if (currentJob?.reservedWorkStation != null
            && IsInAssignedFlow(currentJob.reservedWorkStation)
            && GetStationTypeFrom(currentJob.reservedWorkStation) == stationType)
            return currentJob.reservedWorkStation;

        if (stationType == StationType.Assembly)
        {
            GameObject assembly = GetAssemblyStation(CurrentWorkProduct)?.gameObject;
            return IsInAssignedFlow(assembly) ? assembly : null;
        }

        if (operatedStations != null)
        {
            foreach (var go in operatedStations)
            {
                if (go == null || !IsInAssignedFlow(go)) continue;
                if (GetStationTypeFrom(go) == stationType)
                    return go;
            }
        }

        // Fallback via typed getters (same list, but keeps handoff working if list was stale)
        switch (stationType)
        {
            case StationType.Freezer: return GetFreezerStation()?.gameObject;
            case StationType.Grill: return GetGrillStation()?.gameObject;
            case StationType.Assembly: return GetAssemblyStation(CurrentWorkProduct)?.gameObject;
            case StationType.Cutting: return GetCuttingStation()?.gameObject;
            case StationType.Pantry: return GetPantryStation()?.gameObject;
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

        ProductionFlowPlan flow = manager != null ? manager.GetFlowForWorker(this) : null;
        if (flow?.stations == null) return null;
        for (int i = 0; i < flow.stations.Count; i++)
        {
            GameObject candidate = flow.stations[i];
            if (candidate == null) continue;
            StationNode n = candidate.GetComponent<StationNode>();
            if (n == null) continue;
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
    int EnsureNextDestinationReservation(int desiredUnits)
    {
        if (manager == null || currentJob == null || !currentJob.CurrentStationType.HasValue)
            return 0;
        int existing = manager.GetReservedInputUnits(currentJob);
        if (existing > 0) return Mathf.Min(desiredUnits, existing);

        GameObject source = GetOperatedStationObject(currentJob.CurrentStationType.Value);
        GameObject destination = source != null ? manager.GetFlowOutput(this, source) : null;
        ItemDefinition item = GetCurrentTransferItem();
        if (destination == null || item == null
            || !manager.TryReserveDestination(currentJob, destination, item, desiredUnits))
            return 0;
        return Mathf.Min(desiredUnits, manager.GetReservedInputUnits(currentJob));
    }

    ItemDefinition GetCurrentTransferItem()
    {
        if (currentJob == null) return null;
        if (currentJob.isAssemblySupply && currentJob.assemblySupplyTarget != null)
        {
            if (currentJob.CurrentStationType == StationType.Cutting)
                return manager.GetAssemblySupplyOutput(currentJob.assemblySupplyTarget);
            return manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget);
        }
        if (currentJob.CurrentStationType == StationType.Pantry
            && manager.orderConfig != null && manager.orderConfig.IsFries(currentJob.product))
            return manager.PotatoItem;
        if (currentJob.CurrentStationType == StationType.Cutting
            && manager.orderConfig != null && manager.orderConfig.IsFries(currentJob.product))
            return manager.SlicedPotatoItem;
        if (currentJob.CurrentStationType == StationType.Fryer
            && manager.orderConfig != null && manager.orderConfig.IsFries(currentJob.product))
            return manager.CookedPotatoItem;
        if (currentJob.CurrentStationType == StationType.Freezer && manager.orderConfig != null)
            return manager.orderConfig.rawPattyIngredient;
        if (currentJob.CurrentStationType == StationType.Grill && manager.orderConfig != null)
            return manager.orderConfig.cookedPattyIngredient;
        return currentJob.CurrentWorkProduct ?? currentJob.product;
    }

    void BeginTaskReevaluationDelay()
    {
        nextTaskEvaluationTime = Time.time + TaskReevaluationDelay;
        returningToFlowStart = false;
        returnFlowTarget = null;
        ShowTaskBar = false;
        TaskProgress = 0f;
        ResetNavigationPath(true);
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
    }

    void FinishStepAndHandoff()
    {
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
        if (currentJob == null)
        {
            step = Step.None;
            return;
        }
        if (manager == null)
        {
            manager = ProductionManager.Instance;
            if (manager == null) return;
        }

        currentJob.hasPatty = heldUnits > 0;
        currentJob.heldUnits = heldUnits;
        currentJob.ingredientsHeld.Clear();
        currentJob.ingredientsHeld.AddRange(ingredientsHeld);

        if (currentJob.isAssemblySupply && currentJob.assemblySupplyTarget != null
            && (currentJob.CurrentStationType == StationType.Pantry
                || currentJob.CurrentStationType == StationType.Cutting))
        {
            bool cuttingStepExists = currentJob.FindNextPipelineIndex(StationType.Cutting) >= 0;
            bool finalSupplyStep = currentJob.CurrentStationType == StationType.Cutting || !cuttingStepExists;
            awaitingOutputDelivery = true;
            if (finalSupplyStep)
            {
                GameObject source = GetOperatedStationObject(currentJob.CurrentStationType.Value);
                deliverTarget = source != null ? manager.GetFlowOutput(this, source, currentJob) : null;
                heldDeliveryItem = manager.GetAssemblySupplyOutput(currentJob.assemblySupplyTarget);
                if (deliverTarget != currentJob.assemblySupplyTarget.gameObject
                    || !manager.IsStationInWorkerFlow(this, deliverTarget)
                    || (manager.GetReservedInputUnits(currentJob) <= 0
                    && !manager.TryReserveDestination(currentJob, deliverTarget,
                        heldDeliveryItem, Mathf.Max(1, heldUnits))))
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    return;
                }
            }
            else
            {
                GameObject pantryObject = GetOperatedStationObject(StationType.Pantry);
                deliverTarget = pantryObject != null
                    ? manager.GetFlowOutput(this, pantryObject, currentJob)
                    : null;
                heldDeliveryItem = manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget);
                if (deliverTarget == null || deliverTarget.GetComponent<CuttingStation>() == null)
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    return;
                }
            }
            manager.ReleaseWorkReservation(currentJob);
            step = Step.GoToOutput;
            stateTimer = 0f;
            path.Clear();
            pathDestination = Vector3.zero;
            Sfx.Play(SfxId.StationWorkComplete);
            return;
        }

        bool friesCuttingHandoff = manager.orderConfig != null
            && manager.orderConfig.IsFries(currentJob.product)
            && (currentJob.CurrentStationType == StationType.Pantry
                || currentJob.CurrentStationType == StationType.Cutting);
        if (friesCuttingHandoff)
        {
            GameObject source = GetOperatedStationObject(currentJob.CurrentStationType.Value);
            deliverTarget = source != null ? manager.GetFlowOutput(this, source) : null;
            heldDeliveryItem = currentJob.CurrentStationType == StationType.Cutting
                ? manager.SlicedPotatoItem : manager.PotatoItem;
            bool leavingCutting = currentJob.CurrentStationType == StationType.Cutting;
            bool validTarget = leavingCutting
                ? deliverTarget != null && deliverTarget.GetComponent<FryerStation>() != null
                : deliverTarget != null && deliverTarget.GetComponent<CuttingStation>() != null;
            if (!validTarget)
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }
            if (leavingCutting && !manager.TryReserveDestination(currentJob, deliverTarget,
                    heldDeliveryItem, Mathf.Max(1, heldUnits)))
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }

            awaitingOutputDelivery = true;
            manager.ReleaseWorkReservation(currentJob);
            step = Step.GoToOutput;
            stateTimer = 0f;
            path.Clear();
            pathDestination = Vector3.zero;
            Sfx.Play(SfxId.StationWorkComplete);
            return;
        }

        if (!currentJob.CurrentStationType.HasValue)
        {
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        var node = ResolveStationNodeForStep(currentJob.CurrentStationType.Value);
        GameObject flowOutput = node != null ? manager.GetFlowOutput(this, node.gameObject) : null;
        if (node == null || flowOutput == null)
        {
            // This station is the final stop in the worker's assigned flow. Leave
            // completed assembly output buffered until another flow can collect it.
            if (currentJob.CurrentStationType == StationType.Assembly)
            {
                // The completed product already lives in the Assembly Station's
                // output buffer. End this production job without taking the item,
                // then let the worker retrace their assigned flow to its start.
                ProductionJob completedAtAssembly = currentJob;
                currentJob = null;
                ClearHeldInventory();
                deliverTarget = null;
                step = Step.None;
                stateTimer = 0f;
                path.Clear();
                pathDestination = Vector3.zero;
                returningToFlowStart = false;
                manager.CompleteJob(completedAtAssembly);
                Sfx.Play(SfxId.StationWorkComplete);
                BeginTaskReevaluationDelay();
                return;
            }
            awaitingOutputDelivery = true;
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        awaitingOutputDelivery = true;
        deliverTarget = flowOutput;
        heldDeliveryItem = GetCurrentTransferItem();
        if (heldUnits <= 0)
            heldUnits = 1;
        if (currentJob.CurrentStationType == StationType.Assembly)
        {
            if (!manager.TryReserveTransfer(currentJob, node.gameObject, flowOutput,
                    heldDeliveryItem, heldUnits))
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }
        }
        else if (manager.GetReservedInputUnits(currentJob) <= 0
            && !manager.TryReserveDestination(currentJob, flowOutput, heldDeliveryItem, heldUnits))
        {
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }
        ReleaseCurrentAssemblyOutput(heldUnits);
        if (currentJob.CurrentStationType == StationType.Assembly)
            manager.ConsumeOutputReservation(currentJob, heldUnits);
        manager.ReleaseWorkReservation(currentJob);
        SyncHasPattyFlag();
        step = Step.GoToOutput;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        Sfx.Play(SfxId.StationWorkComplete);
    }

    void ReleaseCurrentAssemblyOutput(int amount)
    {
        if (amount <= 0 || currentJob?.CurrentStationType != StationType.Assembly) return;
        ItemDefinition output = currentJob.CurrentWorkProduct;
        AssemblyStation source = GetAssemblyStation(output);
        IStationBuffer buffer = source;
        if (buffer != null)
            buffer.TakeOutput(output, amount);
    }

    bool HasRoutableStoredAssemblyOutput(AssemblyStation station, ItemDefinition product,
        ProductionJob job = null)
    {
        IStationBuffer buffer = station;
        if (station == null || buffer == null || product == null || buffer.GetOutputCount(product) <= 0
            || !station.CanProcess(product))
            return false;

        ProductionManager production = manager != null ? manager : ProductionManager.Instance;
        GameObject destination = production != null
            ? production.GetFlowOutput(this, station.gameObject, job, product)
            : null;
        return production != null && production.CanTransferAvailable(
            job, station.gameObject, destination, product, 1);
    }

    bool TryTakeStoredAssemblyOutputForDelivery(AssemblyStation station, ItemDefinition product)
    {
        if (currentJob == null || !HasRoutableStoredAssemblyOutput(station, product, currentJob))
            return false;

        GameObject flowOutput = manager.GetFlowOutput(this, station.gameObject);
        IStationBuffer buffer = station;
        int requested = manager.GetReservedTransferUnits(currentJob);
        if (requested <= 0)
        {
            if (!manager.TryReserveTransfer(currentJob, station.gameObject, flowOutput,
                    product, CarryCapacity))
                return false;
            requested = manager.GetReservedTransferUnits(currentJob);
        }
        int taken = buffer.TakeOutput(product, requested);
        if (taken <= 0) return false;
        manager.ConsumeOutputReservation(currentJob, taken);
        manager.ReleaseWorkReservation(currentJob);

        heldUnits = taken;
        heldDeliveryItem = product;
        ingredientsHeld.Clear();
        currentJob.hasPatty = true;
        currentJob.heldUnits = taken;
        currentJob.ingredientsHeld.Clear();
        SyncHasPattyFlag();
        awaitingOutputDelivery = true;
        deliverTarget = flowOutput;
        step = Step.GoToOutput;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        Sfx.Play(SfxId.StationWorkComplete);
        return true;
    }

    void ReleaseBlockedAssemblyJobAndReturn()
    {
        if (currentJob == null) return;

        // Do not erase the requested batch after an intermediate product was
        // deposited into this station. At that point heldUnits is intentionally
        // zero because the item is in the station buffer, not in the worker's hands.
        if (heldUnits > 0 || ingredientsHeld.Count > 0)
        {
            currentJob.hasPatty = heldUnits > 0;
            currentJob.heldUnits = heldUnits;
            currentJob.ingredientsHeld.Clear();
            currentJob.ingredientsHeld.AddRange(ingredientsHeld);
        }
        else
        {
            currentJob.hasPatty = false;
            currentJob.ingredientsHeld.Clear();
        }
        manager?.ReleaseJob(currentJob);
        currentJob = null;
        ClearHeldInventory();
        deliverTarget = null;
        step = Step.None;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        returningToFlowStart = false;
        ReturnToFlowStart();
    }

    /// <summary>
    /// Leaves finished food on its station when the next buffer is full. The
    /// worker can then perform another runnable step in the same flow instead of
    /// standing at the blocked station. The queued job remains available and is
    /// retried as soon as its destination has room.
    /// </summary>
    void YieldBlockedOutputStep()
    {
        if (currentJob == null || IsHoldingAnything) return;
        manager?.ReleaseJob(currentJob);
        currentJob = null;
        deliverTarget = null;
        awaitingOutputDelivery = false;
        step = Step.None;
        stateTimer = 0f;
        ShowTaskBar = false;
        TaskProgress = 0f;
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
        ResetNavigationPath(true);
        nextTaskEvaluationTime = Time.time;
        ResetProgressTracking();
    }

    void RewindInvalidCuttingTask()
    {
        if (currentJob == null) return;

        ProductionJob job = currentJob;
        int pantryStep = -1;
        if (job.pipeline != null)
            for (int i = Mathf.Min(job.currentStepIndex - 1, job.pipeline.Length - 1); i >= 0; i--)
                if (job.pipeline[i] == StationType.Pantry)
                {
                    pantryStep = i;
                    break;
                }

        job.hasPatty = false;
        job.heldUnits = 0;
        job.ingredientsHeld.Clear();
        job.taskPhase = ProductionTaskPhase.Work;
        job.taskSourceStation = null;
        if (pantryStep >= 0)
            job.currentStepIndex = pantryStep;

        if (pantryStep >= 0)
            manager?.ReleaseJob(job);
        else
            manager?.CompleteJob(job);

        currentJob = null;
        ClearHeldInventory();
        deliverTarget = null;
        awaitingOutputDelivery = false;
        step = Step.None;
        stateTimer = 0f;
        BeginTaskReevaluationDelay();
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
        if (currentJob == null)
        {
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            step = Step.None;
            deliverTarget = null;
            return;
        }
        if (manager == null)
        {
            manager = ProductionManager.Instance;
            if (manager == null) return;
        }
        if (deliverTarget == null)
        {
            awaitingOutputDelivery = true;
            step = Step.None;
            return;
        }

        var lamp = deliverTarget.GetComponent<HeatLampStation>();
        if (lamp != null)
        {
            if (currentJob.pipeline != null && currentJob.currentStepIndex < currentJob.pipeline.Length - 1)
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }
            int toDeliver = Mathf.Max(1, heldUnits);
            if (manager.GetReservedInputUnits(currentJob) <= 0
                && !manager.TryReserveDestination(currentJob, lamp.gameObject,
                    heldDeliveryItem ?? currentJob.product, toDeliver))
                return;
            toDeliver = Mathf.Min(toDeliver, Mathf.Max(1,
                manager.GetReservedInputUnits(currentJob)));
            int delivered = 0;
            while (delivered < toDeliver && lamp.HasSpace)
            {
                if (!manager.DeliverToHeatLamp(currentJob.order, lamp))
                    break;
                manager.RecordFlowCompletedOutput(this, currentJob.order.PrimaryItem, 1);
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
            manager.ConsumeInputReservation(currentJob, delivered);
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
            BeginTaskReevaluationDelay();
            return;
        }

        if (currentJob.isAssemblySupply && deliverTarget.GetComponent<CuttingStation>() == null)
        {
            AssemblyStation targetAssembly = currentJob.assemblySupplyTarget != null
                ? currentJob.assemblySupplyTarget
                : deliverTarget.GetComponent<AssemblyStation>();
            if (targetAssembly == null)
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }

            ItemDefinition suppliedItem = manager.GetAssemblySupplyOutput(targetAssembly);
            int carried = Mathf.Max(0, heldUnits);
            int accepted = targetAssembly.ReceivePantryInput(suppliedItem, carried);
            manager.ConsumeInputReservation(currentJob, accepted);
            if (accepted < carried && KitchenInventory.Instance != null && suppliedItem != null)
                KitchenInventory.Instance.AddStock(suppliedItem, carried - accepted);

            ClearHeldInventory();
            awaitingOutputDelivery = false;
            heldDeliveryItem = null;
            ProductionJob finishedSupply = currentJob;
            currentJob = null;
            deliverTarget = null;
            step = Step.None;
            stateTimer = 0f;
            path.Clear();
            pathDestination = Vector3.zero;
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            manager.CompleteJob(finishedSupply);
            manager.RequestImmediateProduction();
            BeginTaskReevaluationDelay();
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

        int idx = currentJob.FindNextPipelineIndex(outType.Value);
        if (idx < 0)
        {
            Debug.LogWarning(
                $"KitchenEmployee: output {outType.Value} is not on this product's pipeline. " +
                "Route Freezer→Grill→Assembly→Pickup Station, with Pantry supplying buns to Assembly.",
                this);
            ShowTaskBar = true;
            TaskProgress = 1f;
            return;
        }

        IStationBuffer destinationBuffer = deliverTarget.GetComponent<IStationBuffer>();
        int depositedUnits = 0;
        if (destinationBuffer != null)
        {
            int carried = Mathf.Max(1, heldUnits);
            int reserved = manager.GetReservedInputUnits(currentJob);
            if (reserved <= 0)
            {
                manager.TryReserveDestination(currentJob, deliverTarget,
                    heldDeliveryItem, carried);
                reserved = manager.GetReservedInputUnits(currentJob);
            }
            int transferUnits = reserved > 0 ? Mathf.Min(carried, reserved) : carried;
            if (!destinationBuffer.CanAcceptInput(heldDeliveryItem, transferUnits))
            {
                ShowTaskBar = true;
                TaskProgress = 1f;
                return;
            }
            if (destinationBuffer.StoreInput(heldDeliveryItem, transferUnits,
                    currentJob.order) != transferUnits)
                return;
            manager.ConsumeInputReservation(currentJob, transferUnits);
            depositedUnits = transferUnits;

            // Jobs created before destination-aware batching may already carry
            // more than an MK1 station can hold. Preserve that excess in stock
            // instead of deadlocking the worker or deleting it.
            ReturnExcessToKitchenStock(heldDeliveryItem, carried - transferUnits);

            // The destination owns the WIP now. The next compatible worker claims
            // the next station task from the shared flow queue.
            heldUnits = 0;
            ingredientsHeld.Clear();
        }

        currentJob.hasPatty = depositedUnits == 0 && heldUnits > 0;
        currentJob.heldUnits = depositedUnits > 0 ? 0 : heldUnits;
        currentJob.ingredientsHeld.Clear();
        if (depositedUnits == 0)
            currentJob.ingredientsHeld.AddRange(ingredientsHeld);
        currentJob.currentStepIndex = idx;
        currentJob.taskPhase = ProductionTaskPhase.Work;
        currentJob.taskSourceStation = null;
        manager.ReleaseJob(currentJob);

        // Keep one worker responsible for the job from source to pickup. The
        // previous implementation released every intermediate step into a
        // shared queue, which created orphaned ingredients and reservation races.
        // If the next station is temporarily missing another input, release the
        // intact job so another useful task can run and retry it later.
        bool continueImmediately = manager.TryReserveCurrentStation(currentJob, this, true);
        ClearHeldInventory();
        deliverTarget = null;
        awaitingOutputDelivery = false;
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
        if (continueImmediately)
        {
            currentJob.assignedTo = this;
            // Non-buffer stations such as Cutting receive ingredients in the
            // worker's hands. ClearHeldInventory above resets the local view,
            // so restore the inventory that was persisted on the job before
            // continuing directly to the next step.
            heldUnits = Mathf.Max(0, currentJob.heldUnits);
            ingredientsHeld.Clear();
            ingredientsHeld.AddRange(currentJob.ingredientsHeld);
            SyncHasPattyFlag();
            step = StepFromJob(currentJob);
            ResetProgressTracking();
            return;
        }

        currentJob = null;
        step = Step.None;
        BeginTaskReevaluationDelay();
    }

    void Update()
    {
        SnapToGround();
        if (manager == null)
            manager = ProductionManager.Instance;

        RecoverStalledMovement();

        bool needsAttention = NeedsPlayerAttention();
        SetAttentionWave(needsAttention);
        if (needsAttention)
        {
            ShowTaskBar = false;
            TaskProgress = 0f;
        }

        if (currentJob != null)
        {
            RecoverWorkflowStep();
            RunWorkflow();
            return;
        }

        // After completing station work or a delivery, briefly hold position and
        // then reassess the entire flow. This prevents workers from committing to
        // low-priority ingredient work before a finished output becomes claimable.
        if (IsWaitingToReevaluateTasks)
        {
            ShowTaskBar = false;
            TaskProgress = 0f;
            SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
            return;
        }

        if (GetRegisterStation() != null)
        {
            returningToFlowStart = false;
            RunRegisterDuty();
            return;
        }

        // All kitchen work is published to one flow-wide queue, ranked by how
        // close its station is to the end of the graph, and claimed atomically.
        QueueRecoverableFlowTasks();
        if (manager != null && manager.TryAssignHighestPriorityTask(this))
        {
            RecoverWorkflowStep();
            RunWorkflow();
            return;
        }

        ReturnToFlowStart();
    }

    void QueueReadyAssemblyProductionTasks()
    {
        if (manager == null) return;

        foreach (GameObject stationObject in GetTaskStations(true))
        {
            AssemblyStation station = stationObject != null
                ? stationObject.GetComponent<AssemblyStation>()
                : null;
            ItemDefinition stageProduct = station != null ? station.selectedProduct : null;
            if (station == null || stageProduct == null
                || !station.HasRequiredInputs(stageProduct,
                    System.Array.Empty<ItemDefinition>(), 1))
                continue;
            if (manager.HasPendingAssemblyWorkTask(station.gameObject, stageProduct))
                continue;

            GameObject output = manager.GetFlowOutput(this, station.gameObject, null, stageProduct);
            AssemblyStation nextAssembly = output != null
                ? output.GetComponent<AssemblyStation>()
                : null;
            ItemDefinition finalProduct = stageProduct;
            StationType[] pipeline = new[] { StationType.Assembly };
            ItemDefinition[] stages = new[] { stageProduct };

            if (nextAssembly != null)
            {
                AssemblyRecipeDefinition nextRecipe = nextAssembly.GetSelectedRecipe();
                if (nextRecipe != null && nextRecipe.output != null
                    && nextRecipe.processedInput == stageProduct)
                {
                    finalProduct = nextRecipe.output;
                    pipeline = new[] { StationType.Assembly, StationType.Assembly };
                    stages = new[] { stageProduct, finalProduct };
                }
            }

            ProductionJob recovery = new ProductionJob(
                CustomerOrder.FromItem(finalProduct, 1), pipeline, stages);
            recovery.taskSourceStation = station.gameObject;
            manager.QueueRecoveryJob(recovery);
        }
    }

    void RecoverWorkflowStep()
    {
        if (currentJob == null || step != Step.None) return;
        if (currentJob.IsHeatLampStep)
        {
            TryResolvePendingOutput();
            awaitingOutputDelivery = true;
            step = deliverTarget != null ? Step.GoToOutput : Step.GoToHeatLamp;
        }
        else
        {
            step = StepFromJob(currentJob);
        }
        stateTimer = 0f;
        path.Clear();
        pathDestination = Vector3.zero;
    }

    bool TryResolvePendingOutput()
    {
        if (deliverTarget != null)
        {
            if (manager != null && manager.IsStationInWorkerFlow(this, deliverTarget))
                return true;
            deliverTarget = null;
            if (currentJob != null)
            {
                currentJob.deliveryHeatLamp = null;
                manager?.ReleaseTransferReservations(currentJob);
            }
        }
        if (currentJob == null) return false;
        if (currentJob.deliveryHeatLamp != null
            && manager.IsStationInWorkerFlow(this, currentJob.deliveryHeatLamp.gameObject))
            deliverTarget = currentJob.deliveryHeatLamp.gameObject;
        else
            currentJob.deliveryHeatLamp = null;

        StationType? sourceType = currentJob.CurrentStationType
            ?? currentJob.LastProductionStationType;
        if (deliverTarget == null && sourceType.HasValue)
        {
            StationNode source = ResolveStationNodeForStep(sourceType.Value);
            if (source != null)
                deliverTarget = manager.GetFlowOutput(this, source.gameObject);
        }
        return deliverTarget != null;
    }

    public void QueueRecoverableFlowTasks()
    {
        if (currentJob != null || manager == null) return;

        foreach (GameObject stationObject in GetTaskStations(true))
        {
            if (stationObject == null) continue;
            FryerStation fryer = stationObject.GetComponent<FryerStation>();
            QueueCookedFryerOutputTask(fryer);
            AssemblyStation station = stationObject.GetComponent<AssemblyStation>();
            QueueStoredAssemblyOutputTask(station);
        }
        QueueReadyAssemblyProductionTasks();
    }

    void QueueCookedFryerOutputTask(FryerStation fryer)
    {
        if (manager == null || fryer == null || !fryer.IsCooked()
            || fryer.GetOutputCount(manager.CookedPotatoItem) <= 0
            || manager.FriesItem == null || manager.HasPendingFryerTask(fryer.gameObject))
            return;

        GameObject output = manager.GetFlowOutput(this, fryer.gameObject, null,
            manager.FriesItem);
        AssemblyStation assembly = output != null ? output.GetComponent<AssemblyStation>() : null;
        if (assembly == null || !assembly.CanProcess(manager.FriesItem)
            || !assembly.CanAcceptInput(manager.CookedPotatoItem, 1))
            return;

        var recovery = new ProductionJob(CustomerOrder.FromItem(manager.FriesItem, 1),
            new[] { StationType.Fryer, StationType.Assembly },
            new[] { manager.FriesItem })
        {
            taskPhase = ProductionTaskPhase.CollectOutput,
            taskSourceStation = fryer.gameObject
        };
        manager.QueueRecoveryJob(recovery);
    }

    List<GameObject> GetTaskStations(bool searchEntireFlow)
    {
        ProductionFlowPlan flow = manager != null ? manager.GetFlowForWorker(this) : null;
        if (flow?.stations == null)
            return new List<GameObject>();
        if (searchEntireFlow)
            return flow.stations;

        var localStations = new List<GameObject>();
        if (operatedStations == null) return localStations;
        foreach (GameObject station in operatedStations)
            if (station != null && flow.stations.Contains(station))
                localStations.Add(station);
        return localStations;
    }

    void QueueStoredAssemblyOutputTask(AssemblyStation station)
    {
        if (manager == null || station == null
            || station.BufferedOutputCount <= 0 || station.selectedProduct == null)
            return;
        if (manager.HasPendingCollectionTask(station.gameObject, station.selectedProduct)
            || manager.HasPendingAssemblyWorkTask(station.gameObject, station.selectedProduct))
            return;

        GameObject output = manager.GetFlowOutput(this, station.gameObject, null, station.selectedProduct);
        if (output == null) return;

        HeatLampStation pickup = output.GetComponent<HeatLampStation>();
        AssemblyStation nextAssembly = output.GetComponent<AssemblyStation>();
        ItemDefinition finalProduct = station.selectedProduct;
        StationType[] pipeline;
        ItemDefinition[] assemblyStages;
        if (pickup != null)
        {
            if (!pickup.isActiveAndEnabled || !pickup.HasSpace) return;
            pipeline = new[] { StationType.Assembly };
            assemblyStages = new[] { station.selectedProduct };
        }
        else if (nextAssembly != null)
        {
            AssemblyRecipeDefinition nextRecipe = nextAssembly.GetSelectedRecipe();
            if (nextRecipe == null || nextRecipe.output == null
                || nextRecipe.processedInput != station.selectedProduct
                || !manager.CanTransferAvailable(null, station.gameObject, output,
                    station.selectedProduct, 1))
                return;

            finalProduct = nextRecipe.output;
            pipeline = new[] { StationType.Assembly, StationType.Assembly };
            assemblyStages = new[] { station.selectedProduct, finalProduct };
        }
        else
        {
            return;
        }

        ProductionJob deliveryJob = new ProductionJob(
            CustomerOrder.FromItem(finalProduct, 1), pipeline, assemblyStages);
        deliveryJob.taskPhase = ProductionTaskPhase.CollectOutput;
        deliveryJob.taskSourceStation = station.gameObject;
        manager.QueueRecoveryJob(deliveryJob);
    }

    void ReturnToFlowStart()
    {
        ShowTaskBar = false;
        TaskProgress = 0f;
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);

        if (!returningToFlowStart || returnFlowTarget == null)
            returnFlowTarget = FindNearestFlowRoot();

        if (returnFlowTarget == null)
        {
            returningToFlowStart = false;
            return;
        }

        returningToFlowStart = true;
        Vector3 position = GetInteractionPosition(returnFlowTarget);
        if (!CloseEnough(position, Mathf.Max(0.2f, ArrivalRadius * 2f)))
        {
            MoveToward(position);
            return;
        }

        FaceStationObject(returnFlowTarget);
        returningToFlowStart = false;
        returnFlowTarget = null;
        path.Clear();
        pathDestination = Vector3.zero;
    }

    GameObject FindNearestFlowRoot()
    {
        ProductionFlowPlan flow = manager != null ? manager.GetFlowForWorker(this) : null;
        if (flow == null || flow.stations == null || flow.stations.Count == 0)
            return null;

        flow.EnsureLegacyConnections();
        var assigned = new HashSet<GameObject>();
        if (operatedStations != null)
            foreach (GameObject station in operatedStations)
                if (station != null) assigned.Add(station);

        var hasIncoming = new HashSet<GameObject>();
        if (flow.connections != null)
        {
            foreach (ProductionFlowConnection connection in flow.connections)
                if (connection != null && connection.from != null && connection.to != null
                    && assigned.Contains(connection.from) && assigned.Contains(connection.to))
                    hasIncoming.Add(connection.to);
        }

        GameObject nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (GameObject station in flow.stations)
        {
            if (station == null || !assigned.Contains(station) || hasIncoming.Contains(station))
                continue;
            float distance = (GetInteractionPosition(station) - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = station;
                nearestDistance = distance;
            }
        }

        // A malformed graph may have no detectable root. Keep the worker parked
        // at an assigned station rather than sending them to an arbitrary endpoint.
        if (nearest == null)
            foreach (GameObject station in flow.stations)
                if (station != null && assigned.Contains(station))
                    return station;
        return nearest;
    }

    bool NeedsPlayerAttention()
    {
        if (!CanTakeJobs)
            return true;
        if (manager == null || currentJob == null)
            return false;

        if ((step == Step.AtOutput || step == Step.GoToOutput) && deliverTarget != null)
        {
            var currentLamp = deliverTarget.GetComponent<HeatLampStation>();
            if (currentLamp != null && !currentLamp.HasSpace)
            {
                HeatLampStation available = null;
                float nearest = float.PositiveInfinity;
                foreach (var candidate in FindObjectsByType<HeatLampStation>(FindObjectsSortMode.None))
                {
                    if (!candidate.isActiveAndEnabled || !candidate.HasSpace || candidate.name.Contains("Ghost")) continue;
                    if (!manager.IsStationInWorkerFlow(this, candidate.gameObject)) continue;
                    var mounted = candidate.GetComponent<CounterMountedItem>();
                    if (mounted != null && mounted.surface == null) continue;
                    float distance = (candidate.GetInteractionPosition() - transform.position).sqrMagnitude;
                    if (distance < nearest) { available = candidate; nearest = distance; }
                }
                if (available != null)
                {
                    deliverTarget = available.gameObject;
                    currentJob.deliveryHeatLamp = available;
                    step = Step.GoToOutput;
                    path.Clear();
                    pathDestination = Vector3.zero;
                }
            }
        }

        if (step == Step.AtFreezer && heldUnits <= 0 && !manager.HasPattyInStock(this))
            return true;

        if (step == Step.AtPantry)
        {
            PantryStation pantry = GetPantryStation();
            ItemDefinition required = currentJob.isAssemblySupply
                ? manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget)
                : manager.GetPantryItemForProduct(currentJob.product);
            if (pantry != null && required != null && !pantry.HasItem(required))
                return true;
        }

        if (step == Step.AtDrink && heldUnits <= 0 && !manager.HasDrinkInStock())
            return true;

        if (awaitingOutputDelivery && deliverTarget == null)
        {
            if (!TryResolvePendingOutput()) return true;
            step = Step.GoToOutput;
        }

        if (step == Step.GoToOutput && deliverTarget == null)
        {
            if (!TryResolvePendingOutput()) return true;
        }

        if (step == Step.AtOutput && deliverTarget != null)
        {
            var lamp = deliverTarget.GetComponent<HeatLampStation>();
            if (lamp != null && !lamp.HasSpace)
                return true;
        }

        return false;
    }

    bool IsCurrentlyBlocked()
    {
        return !string.IsNullOrEmpty(GetBlockedReason());
    }

    void SetAttentionWave(bool on)
    {
        if (characterAnimator == null)
            characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        if (characterAnimator != null)
            characterAnimator.SetAttentionWave(on, attentionWaveClip);
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

    void ReturnToKitchenStand()
    {
        Vector3 stand = GetIdleStandPosition(null);
        if (CloseEnough(stand, 0.4f))
        {
            if (operatedStations != null)
            {
                for (int i = 0; i < operatedStations.Count; i++)
                {
                    if (operatedStations[i] == null) continue;
                    FaceStationObject(operatedStations[i]);
                    break;
                }
            }
            return;
        }
        MoveToward(stand);
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
            ReturnToKitchenStand();
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
                float drinkTime = manager.GetDrinkProcessTime(this);
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
                var taken = lamp.TryTakeSingleItem(cashierFetchItem, out int adjustedSaleValue);
                if (taken != null)
                {
                    cashierTray.Add(cashierFetchItem);
                    cashierCustomer?.ApplyPickupSaleValue(cashierFetchItem, adjustedSaleValue);
                }
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
                float freezerTime = manager.GetFreezerProcessTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, freezerTime));
                if (stateTimer >= freezerTime)
                {
                    int reservedCapacity = manager.GetReservedInputUnits(currentJob);
                    if (reservedCapacity <= 0)
                    {
                        TaskProgress = 1f;
                        stateTimer = freezerTime;
                        break;
                    }
                    int taken = manager.TryTakePattiesFromFreezer(this,
                        GetCurrentTransferItem(), Mathf.Min(ProductionBatchCapacity, reservedCapacity));
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
                            int transferCapacity = EnsureNextDestinationReservation(CarryCapacity);
                            if (transferCapacity <= 0)
                            {
                                YieldBlockedOutputStep();
                                break;
                            }
                            int cooked = manager.TakePattiesFromGrill(this,
                                currentJob != null ? currentJob.product : manager.PattyItem,
                                transferCapacity);
                            if (cooked > 0)
                            {
                                heldUnits = cooked;
                                SyncHasPattyFlag();
                                FinishStepAndHandoff();
                                break;
                            }
                        }

                        int grillLoad = grill != null
                            ? Mathf.Min(heldUnits, grill.InputSlotCapacity) : heldUnits;
                        int placed = heldUnits > 0
                            ? manager.PlacePattiesOnGrill(this,
                                currentJob != null ? currentJob.product : manager.PattyItem,
                                grillLoad)
                            : 0;
                        if (placed > 0)
                        {
                            manager.ConsumeInputReservation(currentJob, placed);
                            heldUnits = Mathf.Max(0, heldUnits - placed);
                            ReturnExcessToKitchenStock(manager.orderConfig != null
                                ? manager.orderConfig.rawPattyIngredient : manager.PattyItem,
                                heldUnits);
                            heldUnits = 0;
                            SyncHasPattyFlag();
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
                float grillTotal = manager.GetGrillProcessTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, grillTotal));

                var grillNow = manager.GetGrillFor(this);
                bool collectingGrillOutput = currentJob != null
                    && currentJob.taskPhase == ProductionTaskPhase.CollectOutput;
                bool readyToTake = grillNow != null && grillNow.IsCooked()
                    && (collectingGrillOutput || stateTimer >= grillTotal);

                if (readyToTake || stateTimer >= grillTotal)
                {
                    // Always take the cooked patty off the grill before handoff.
                    // (heldUnits stays > 0 during cooking for batch carry — do not treat that as "already done".)
                    int transferCapacity = EnsureNextDestinationReservation(CarryCapacity);
                    if (transferCapacity <= 0)
                    {
                        YieldBlockedOutputStep();
                        break;
                    }
                    int cooked = manager.TakePattiesFromGrill(this,
                        currentJob != null ? currentJob.product : manager.PattyItem,
                        transferCapacity);
                    if (cooked > 0)
                    {
                        heldUnits = cooked;
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

            case Step.GoToCutting:
                {
                    var cutting = GetCuttingStation();
                    if (cutting == null) break;
                    if (MoveToward(cutting.GetInteractionPosition()))
                    {
                        CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
                        int carriedAmount = Mathf.Max(0, heldUnits);
                        if (recipe != null && carriedAmount > 0
                            && cutting.HasCarriedSupply(manager.orderConfig, ingredientsHeld, carriedAmount))
                        {
                            int openSlots = Mathf.Max(0, cutting.InputSlotCapacity
                                - cutting.GetInputCount(recipe.input));
                            int storeAmount = Mathf.Min(carriedAmount, openSlots);
                            int reserved = manager.GetReservedInputUnits(currentJob);
                            if (reserved > 0) storeAmount = Mathf.Min(storeAmount, reserved);
                            int stored = cutting.StoreInput(recipe.input, storeAmount,
                                currentJob != null ? currentJob.order : null);
                            if (stored > 0)
                            {
                                int remaining = stored;
                                for (int i = ingredientsHeld.Count - 1; i >= 0 && remaining > 0; i--)
                                {
                                    if (ingredientsHeld[i] != recipe.input) continue;
                                    ingredientsHeld.RemoveAt(i);
                                    remaining--;
                                }
                                int excess = Mathf.Max(0, carriedAmount - stored);
                                ReturnExcessToKitchenStock(recipe.input, excess);
                                ingredientsHeld.Clear();
                                heldUnits = 0;
                                SyncHasPattyFlag();
                            }
                        }

                        bool hasBufferedWork = recipe != null
                            && (cutting.GetInputCount(recipe.input) > 0
                                || cutting.GetOutputCount(recipe.output) > 0);
                        if (manager.orderConfig == null || !hasBufferedWork)
                        {
                            RewindInvalidCuttingTask();
                            break;
                        }
                        step = Step.AtCutting;
                        stateTimer = cutting.GetOutputCount(recipe.output) > 0
                            ? cutting.processTimeSeconds : 0f;
                    }
                    break;
                }

            case Step.AtCutting:
                {
                    var cutting = GetCuttingStation();
                    if (cutting == null)
                    {
                        step = Step.GoToCutting;
                        break;
                    }
                    FaceStationObject(cutting.gameObject);
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Assembly);
                    stateTimer += Time.deltaTime;
                    ShowTaskBar = true;
                    TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, cutting.processTimeSeconds));
                    if (stateTimer >= cutting.processTimeSeconds)
                    {
                        CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
                        if (recipe == null)
                        {
                            RewindInvalidCuttingTask();
                            break;
                        }

                        int availableOutput = cutting.GetOutputCount(recipe.output);
                        if (availableOutput <= 0)
                            availableOutput = cutting.ProcessBuffered(CarryCapacity);
                        if (availableOutput <= 0)
                        {
                            RewindInvalidCuttingTask();
                            break;
                        }

                        int transferCapacity = EnsureNextDestinationReservation(
                            Mathf.Min(CarryCapacity, availableOutput));
                        if (transferCapacity <= 0)
                        {
                            YieldBlockedOutputStep();
                            break;
                        }

                        int taken = cutting.TakeOutput(recipe.output, transferCapacity);
                        if (taken <= 0) break;
                        manager.ConsumeOutputReservation(currentJob, taken);
                        heldUnits = taken;
                        ingredientsHeld.Clear();
                        for (int i = 0; i < taken; i++) ingredientsHeld.Add(recipe.output);
                        SyncHasPattyFlag();
                        FinishStepAndHandoff();
                    }
                    break;
                }

            case Step.GoToAssembly:
                if (MoveToward(manager.GetAssemblyPosition(this)))
                {
                    if (manager.IsEmployeeOnAssemblyTile(transform.position, this))
                    {
                        ItemDefinition assemblyProduct = currentJob != null ? currentJob.CurrentWorkProduct : null;
                        var asm = GetAssemblyStation(assemblyProduct);
                        if (TryTakeStoredAssemblyOutputForDelivery(asm, assemblyProduct))
                            break;
                        if (asm != null && !asm.CanProcess(assemblyProduct))
                        {
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        int assemblyUnits = CurrentAssemblyBatchSize;
                        if (asm == null || !asm.HasRequiredInputs(
                            assemblyProduct, ingredientsHeld, assemblyUnits))
                        {
                            ReleaseBlockedAssemblyJobAndReturn();
                            break;
                        }
                        step = Step.AtAssembly;
                        stateTimer = 0f;
                    }
                    else { path.Clear(); pathDestination = Vector3.zero; }
                }
                break;

            case Step.AtAssembly:
                ItemDefinition activeAssemblyProduct = currentJob != null ? currentJob.CurrentWorkProduct : null;
                var activeAssembly = GetAssemblyStation(activeAssemblyProduct);
                FaceStationObject(GetOperatedStationObject(StationType.Assembly) ?? activeAssembly?.gameObject);
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
                if (TryTakeStoredAssemblyOutputForDelivery(activeAssembly, activeAssemblyProduct))
                    break;
                int activeAssemblyUnits = CurrentAssemblyBatchSize;
                if (activeAssembly == null || !activeAssembly.HasRequiredInputs(
                    activeAssemblyProduct, ingredientsHeld, activeAssemblyUnits))
                {
                    // Do not park the worker at Assembly while an ingredient is
                    // missing. Preserve the unfinished job for later assignment
                    // and return this worker to the beginning of their flow.
                    ReleaseBlockedAssemblyJobAndReturn();
                    break;
                }
                stateTimer += Time.deltaTime;
                float assemblyTime = manager.GetAssemblyProcessTime(this);
                ShowTaskBar = true;
                TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, assemblyTime));
                if (stateTimer >= assemblyTime)
                {
                    if (!activeAssembly.TryAssemble(activeAssemblyProduct, ingredientsHeld,
                            activeAssemblyUnits))
                    {
                        stateTimer = 0f;
                        TaskProgress = 0f;
                        break;
                    }
                    heldUnits = activeAssemblyUnits;
                    currentJob.hasPatty = true;
                    currentJob.heldUnits = activeAssemblyUnits;
                    SyncHasPattyFlag();
                    heldDeliveryItem = activeAssemblyProduct;
                    awaitingOutputDelivery = true;
                    FinishStepAndHandoff();
                }
                break;

            case Step.GoToPantry:
                {
                    var pantry = GetPantryStation();
                    if (pantry == null) break;
                    ItemDefinition pantryItem = currentJob != null && currentJob.isAssemblySupply
                        ? manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget)
                        : manager.GetPantryItemForProduct(currentJob != null ? currentJob.product : null);
                    if (MoveToward(pantry.GetInteractionPosition()))
                    {
                        FaceStationObject(GetOperatedStationObject(StationType.Pantry) ?? pantry.gameObject);
                        if (pantryItem == null || !pantry.HasItem(pantryItem))
                        {
                            ShowTaskBar = true;
                            TaskProgress = 0f;
                            break;
                        }
                        step = Step.AtPantry;
                        stateTimer = 0f;
                    }
                    break;
                }

            case Step.AtPantry:
                {
                    var pantry = GetPantryStation();
                    if (pantry == null)
                    {
                        step = Step.GoToPantry;
                        break;
                    }
                    ItemDefinition pantryItem = currentJob != null && currentJob.isAssemblySupply
                        ? manager.GetAssemblySupplySource(currentJob.assemblySupplyTarget)
                        : manager.GetPantryItemForProduct(currentJob != null ? currentJob.product : null);
                    if (pantryItem == null)
                    {
                        TaskProgress = 0f;
                        break;
                    }
                    FaceStationObject(GetOperatedStationObject(StationType.Pantry) ?? pantry.gameObject);
                    SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.Assembly);
                    stateTimer += Time.deltaTime;
                    ShowTaskBar = true;
                    TaskProgress = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, pantry.processTimeSeconds));
                    if (stateTimer >= pantry.processTimeSeconds)
                    {
                        if (currentJob != null && currentJob.isAssemblySupply)
                        {
                            int wanted = Mathf.Clamp(currentJob.requestedSupplyUnits, 1, CarryCapacity);
                            int reservedCapacity = manager.GetReservedInputUnits(currentJob);
                            if (reservedCapacity > 0)
                                wanted = Mathf.Min(wanted, reservedCapacity);
                            int supplyCollected = 0;
                            while (supplyCollected < wanted && pantry.TakeItem(pantryItem))
                            {
                                ingredientsHeld.Add(pantryItem);
                                supplyCollected++;
                            }
                            if (supplyCollected <= 0)
                            {
                                TaskProgress = 0f;
                                break;
                            }
                            heldUnits = supplyCollected;
                            SyncHasPattyFlag();
                            FinishStepAndHandoff();
                            break;
                        }

                        int collected = 0;
                        int collectionLimit = EnsureNextDestinationReservation(CarryCapacity);
                        if (collectionLimit <= 0)
                        {
                            TaskProgress = 1f;
                            break;
                        }
                        while (collected < collectionLimit && pantry.TakeItem(pantryItem))
                        {
                            ingredientsHeld.Add(pantryItem);
                            collected++;
                        }
                        if (collected <= 0)
                        {
                            TaskProgress = 0f;
                            break;
                        }
                        heldUnits = collected;
                        SyncHasPattyFlag();
                        FinishStepAndHandoff();
                    }
                    break;
                }

            case Step.GoToFryer:
                if (MoveToward(manager.GetFryerPosition(this)))
                {
                    if (manager.IsEmployeeOnFryerTile(transform.position, this))
                    {
                        FryerStation fryer = manager.GetFryerFor(this);
                        if (fryer != null && (fryer.IsCooking || fryer.IsCooked()))
                        {
                            step = Step.AtFryer;
                            stateTimer = 0f;
                        }
                        else
                        {
                            int fryerLoad = fryer != null
                                ? Mathf.Min(heldUnits, fryer.InputSlotCapacity) : heldUnits;
                            int placed = heldUnits > 0
                                ? manager.TryLoadFryer(this, fryerLoad,
                                    currentJob != null ? currentJob.order : null)
                                : 0;
                            if (placed > 0)
                            {
                                manager.ConsumeInputReservation(currentJob, placed);
                                heldUnits = Mathf.Max(0, heldUnits - placed);
                                ReturnExcessToKitchenStock(manager.SlicedPotatoItem, heldUnits);
                                heldUnits = 0;
                                SyncHasPattyFlag();
                                step = Step.AtFryer;
                                stateTimer = 0f;
                            }
                            else { path.Clear(); pathDestination = Vector3.zero; }
                        }
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
                FryerStation fryerAtTask = manager.GetFryerFor(this);
                if (currentJob != null && currentJob.taskPhase == ProductionTaskPhase.CollectOutput
                    && fryerAtTask != null && fryerAtTask.IsCooked())
                {
                    int available = fryerAtTask.GetOutputCount(manager.CookedPotatoItem);
                    int transferCapacity = EnsureNextDestinationReservation(
                        Mathf.Min(CarryCapacity, available));
                    if (transferCapacity <= 0)
                    {
                        YieldBlockedOutputStep();
                        break;
                    }
                    int taken = manager.TakeFromFryer(this, transferCapacity);
                    if (taken > 0)
                    {
                        manager.ConsumeOutputReservation(currentJob, taken);
                        heldUnits = taken;
                        SyncHasPattyFlag();
                        FinishStepAndHandoff();
                    }
                    break;
                }
                stateTimer += Time.deltaTime;
                float fryerTotal = manager.GetFryerProcessTime(this);
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
                    FryerStation activeFryer = manager.GetFryerFor(this);
                    int available = activeFryer != null
                        ? activeFryer.GetOutputCount(manager.CookedPotatoItem) : 0;
                    int transferCapacity = EnsureNextDestinationReservation(
                        Mathf.Min(CarryCapacity, available));
                    if (activeFryer != null && activeFryer.IsCooked()
                        && transferCapacity <= 0)
                    {
                        YieldBlockedOutputStep();
                        break;
                    }
                    int taken = manager.TakeFromFryer(this, transferCapacity);
                    if (taken > 0)
                    {
                        manager.ConsumeOutputReservation(currentJob, taken);
                        heldUnits = taken;
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
                float drinkProductionTime = manager.GetDrinkProcessTime(this);
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
                if (!TryResolvePendingOutput())
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    break;
                }
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
                GameObject arrivedTarget = deliverTarget;
                if (!TryResolvePendingOutput())
                {
                    ShowTaskBar = true;
                    TaskProgress = 1f;
                    break;
                }
                if (deliverTarget != arrivedTarget)
                {
                    step = Step.GoToOutput;
                    path.Clear();
                    pathDestination = Vector3.zero;
                    break;
                }
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

        bool destinationChanged = Vector3.SqrMagnitude(pathDestination - exactTarget) > 0.0001f;
        if (path.Count == 0 || destinationChanged)
        {
            if (!destinationChanged && Time.time < nextPathAttemptTime)
                return false;

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
                consecutivePathFailures++;
                float retryDelay = Mathf.Min(1f,
                    Mathf.Max(0.05f, pathRetryDelay) * Mathf.Pow(1.6f, consecutivePathFailures - 1));
                nextPathAttemptTime = Time.time + retryDelay;
                return false;
            }

            consecutivePathFailures = 0;
            nextPathAttemptTime = 0f;
        }

        if (path.Count == 0)
            return false;

        Vector3 waypoint = path[0];
        waypoint.y = GroundY;

        float step = moveSpeed * Time.deltaTime;
        Vector3 pos = transform.position;
        pos.y = GroundY;
        float dist = Vector3.Distance(pos, waypoint);

        if (dist <= step)
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
        if (characterAnimator == null)
            characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        if (characterAnimator != null)
            characterAnimator.FaceMovementToward(worldPoint, smooth: true);
    }

    void FaceStationObject(GameObject station)
    {
        if (station == null) return;
        if (characterAnimator == null)
            characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        if (characterAnimator != null)
            characterAnimator.FaceTowardAdjacentObject(station, smooth: true);
    }

    void SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind work)
    {
        if (characterAnimator == null)
            characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        if (characterAnimator != null)
            characterAnimator.SetStationWork(work);
    }
}
