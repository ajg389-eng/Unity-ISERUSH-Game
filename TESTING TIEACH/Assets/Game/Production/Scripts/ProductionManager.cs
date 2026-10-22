using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Station types workers can be assigned to.
/// Production pipelines use Freezer/Grill/Pantry/Assembly (burger),
/// Pantry/Cutting/Fryer/Assembly (fries), and Drink (drink).
/// </summary>
public enum StationType
{
    Freezer = 0,
    Grill = 1,
    Pantry = 2,
    Assembly = 3,
    Register = 4,
    Fryer = 5,
    Drink = 6,
    Cutting = 7
}

public enum ProductionTaskPhase
{
    Work,
    CollectOutput
}

/// <summary>Observable lifecycle for the currently claimable action of a production job.</summary>
public enum ProductionTaskState
{
    WaitingForDependencies,
    Runnable,
    Claimed,
    Traveling,
    Working,
    Delivering,
    Complete,
    Blocked,
    Cancelled
}

public class ProductionJob
{
    static long nextTaskId;

    /// <summary>Stable runtime identity used for claims and scheduler diagnostics.</summary>
    public readonly long taskId;
    /// <summary>Identity shared by every step belonging to the same requested end product.</summary>
    public readonly long rootDemandId;
    /// <summary>Supply tasks that feed this demand. Physical buffers remain authoritative.</summary>
    public readonly List<long> prerequisiteTaskIds = new List<long>();
    public CustomerOrder order;
    public ItemDefinition product;
    public StationType[] pipeline;
    /// <summary>Outputs of repeated Assembly steps, in pipeline order.</summary>
    public ItemDefinition[] assemblyStageProducts;
    /// <summary>Index into pipeline, or pipeline.Length for heat-lamp delivery.</summary>
    public int currentStepIndex;
    /// <summary>The small, claimable action currently available on the shared flow.</summary>
    public ProductionTaskPhase taskPhase;
    // Exact physical flow node for collection or a station-specific task.
    // StationType and recipe alone are not unique when a flow has duplicates.
    [System.NonSerialized] public GameObject taskSourceStation;
    public KitchenEmployee assignedTo;
    public bool hasPatty;
    /// <summary>Item count currently carried between stations.</summary>
    public int heldUnits;
    /// <summary>One-item assembly cycles completed for the current job step.</summary>
    [System.NonSerialized] public int completedUnitsAtCurrentStep;
    public readonly List<ItemDefinition> ingredientsHeld = new List<ItemDefinition>();
    /// <summary>Parallel supply task that stocks a station with a recipe input.</summary>
    public bool isAssemblySupply;
    public AssemblyStation assemblySupplyTarget;
    public GrillStation grillSupplyTarget;
    /// <summary>The pantry item collected for this exact assembly input.</summary>
    public ItemDefinition assemblySupplySourceItem;
    /// <summary>The item delivered to the assembly station after optional processing.</summary>
    public ItemDefinition assemblySupplyOutputItem;
    public ItemDefinition[] assemblySupplyStageItems;
    public int requestedSupplyUnits;
    /// <summary>Heat lamp this job must deliver to (from the last station's Assign Output link).</summary>
    public HeatLampStation deliveryHeatLamp;
    /// <summary>Runtime-only claims that prevent two workers targeting the same station inventory.</summary>
    // Exact physical instance claimed by this task, not just its station type.
    [System.NonSerialized] public GameObject reservedWorkStation;
    [System.NonSerialized] public ItemDefinition reservedPantryIngredient;
    /// <summary>The current exact source-to-destination handoff reserved by the planner.</summary>
    [System.NonSerialized] public FlowTransferOrder activeTransfer;
    public GameObject reservedSourceStation => activeTransfer != null ? activeTransfer.source : null;
    public GameObject reservedDestinationStation => activeTransfer != null ? activeTransfer.destination : null;
    public ItemDefinition reservedItem => activeTransfer != null ? activeTransfer.item : null;
    public int reservedOutputUnits => activeTransfer != null ? activeTransfer.reservedOutputUnits : 0;
    public int reservedInputUnits => activeTransfer != null ? activeTransfer.reservedInputUnits : 0;
    [System.NonSerialized] public ProductionTaskState taskState;
    [System.NonSerialized] public float createdAt;
    [System.NonSerialized] public float stateChangedAt;
    [System.NonSerialized] public float claimHeartbeatAt;
    [System.NonSerialized] public int claimVersion;
    [System.NonSerialized] public string blockedReason;
    [System.NonSerialized] public ProductionFlowPlan blockedFlow;
    [System.NonSerialized] public float retryAfter;
    [System.NonSerialized] public GameObject blockedStation;
    public string TaskKey => taskId + ":" + currentStepIndex + ":" + taskPhase;

    public void SetTransferReservation(GameObject source, GameObject destination,
        ItemDefinition item, int outputUnits, int inputUnits)
    {
        FlowTransferPurpose purpose = isAssemblySupply
            ? FlowTransferPurpose.SupplyIngredient
            : destination != null && destination.GetComponent<HeatLampStation>() != null
                ? FlowTransferPurpose.DeliverFinishedProduct
                : FlowTransferPurpose.FeedStation;
        activeTransfer = new FlowTransferOrder(taskId, purpose, source, destination,
            item, outputUnits, inputUnits);
    }

    public void SetDestinationReservation(GameObject destination, ItemDefinition item, int inputUnits)
    {
        bool sameItem = reservedItem == item;
        GameObject source = sameItem && reservedSourceStation != null
            ? reservedSourceStation
            : reservedWorkStation != null ? reservedWorkStation : taskSourceStation;
        int outputUnits = sameItem ? reservedOutputUnits : 0;
        SetTransferReservation(source, destination, item, outputUnits, inputUnits);
    }

    public void ClearTransferReservation() => activeTransfer = null;

    public bool IsHeatLampStep =>
        pipeline != null && currentStepIndex >= pipeline.Length;

    public StationType? CurrentStationType
    {
        get
        {
            if (pipeline == null || currentStepIndex < 0 || currentStepIndex >= pipeline.Length)
                return null;
            return pipeline[currentStepIndex];
        }
    }

    public StationType? LastProductionStationType
    {
        get
        {
            if (pipeline == null || pipeline.Length == 0) return null;
            return pipeline[pipeline.Length - 1];
        }
    }

    public int FindPipelineIndex(StationType type)
    {
        if (pipeline == null) return -1;
        for (int i = 0; i < pipeline.Length; i++)
        {
            if (pipeline[i] == type)
                return i;
        }
        return -1;
    }

    public int FindNextPipelineIndex(StationType type)
    {
        if (pipeline == null) return -1;
        for (int i = Mathf.Max(0, currentStepIndex + 1); i < pipeline.Length; i++)
            if (pipeline[i] == type) return i;
        return -1;
    }

    public ItemDefinition CurrentWorkProduct
    {
        get
        {
            if (CurrentStationType != StationType.Assembly || assemblyStageProducts == null
                || assemblyStageProducts.Length == 0)
                return product;

            int assemblyIndex = -1;
            for (int i = 0; i <= currentStepIndex && i < pipeline.Length; i++)
                if (pipeline[i] == StationType.Assembly) assemblyIndex++;
            return assemblyStageProducts[Mathf.Clamp(assemblyIndex, 0, assemblyStageProducts.Length - 1)];
        }
    }

    public ProductionJob(CustomerOrder o, StationType[] steps, IList<ItemDefinition> assemblyStages = null,
        long parentDemandId = 0)
    {
        taskId = ++nextTaskId;
        rootDemandId = parentDemandId != 0 ? parentDemandId : taskId;
        order = o;
        product = o != null ? o.PrimaryItem : null;
        pipeline = steps ?? System.Array.Empty<StationType>();
        assemblyStageProducts = assemblyStages != null
            ? new List<ItemDefinition>(assemblyStages).ToArray()
            : System.Array.Empty<ItemDefinition>();
        currentStepIndex = 0;
        taskPhase = ProductionTaskPhase.Work;
        taskSourceStation = null;
        assignedTo = null;
        hasPatty = false;
        heldUnits = 0;
        deliveryHeatLamp = null;
        createdAt = Time.time;
        stateChangedAt = createdAt;
        claimHeartbeatAt = createdAt;
        taskState = ProductionTaskState.WaitingForDependencies;
    }
}

/// <summary>
/// Fast-food production: one job may batch up to four identical menu items.
/// Work order: Burger = Freezer → Grill → Assembly; Fries = Pantry → Cutting → Fryer → Assembly.
/// After each station, the worker delivers only to that station's Assign Output
/// (e.g. Assembly/Fryer → Heat Lamp). Drinks are cashier-served.
/// </summary>
[DefaultExecutionOrder(-200)]
public partial class ProductionManager : MonoBehaviour
{
    public static ProductionManager Instance { get; private set; }

    [Header("Order config")]
    public CustomerOrderConfig orderConfig;
    [Header("Registers")]
    public List<Register> registers = new List<Register>();

    [Header("Heat lamp")]
    public HeatLampStation heatLamp;

    [Header("Employees")]
    public List<KitchenEmployee> employees = new List<KitchenEmployee>();

    [Header("Hiring")]
    public GameObject employeePrefab;
    public Transform spawnPoint;
    public int hireCost = 100;

    [Header("Player-designed production flows")]
    public List<ProductionFlowPlan> productionFlows = new List<ProductionFlowPlan>();
    [Min(0)] public int selectedFlowIndex;
    [System.NonSerialized] public TeamBalanceResult lastFlowBalance;

    // Legacy serialized fields kept so old scenes still load; ignored at runtime.
    [HideInInspector] public KitchenFlowKind hireFlow = KitchenFlowKind.Custom;
    [HideInInspector] public List<string> hireFlowSteps = new List<string>();

    readonly List<ProductionJob> pendingJobs = new List<ProductionJob>();
    readonly List<GameObject> outgoingCandidateBuffer = new List<GameObject>();
    readonly Dictionary<GameObject, ProductionJob> stationWorkReservations =
        new Dictionary<GameObject, ProductionJob>();
    readonly Dictionary<(GameObject station, ItemDefinition ingredient), ProductionJob> pantryWorkReservations =
        new Dictionary<(GameObject station, ItemDefinition ingredient), ProductionJob>();
    readonly HashSet<ProductionJob> jobsWithReservations = new HashSet<ProductionJob>();
    readonly HashSet<GameObject> activeDemandStations = new HashSet<GameObject>();
    float activeDemandStationsBuiltAt = -999f;
    sealed class TimedItemCount
    {
        public ItemDefinition item;
        public int count;
        public float time;
    }
    readonly List<TimedItemCount> orderedHistory = new List<TimedItemCount>();
    readonly List<TimedItemCount> pickupDeliveryHistory = new List<TimedItemCount>();
    sealed class TimedFlowOutput
    {
        public ProductionFlowPlan flow;
        public ItemDefinition item;
        public int count;
        public float time;
    }
    readonly List<TimedFlowOutput> completedFlowHistory = new List<TimedFlowOutput>();
    sealed class TimedWorkerTrip
    {
        public ProductionFlowPlan flow;
        public int deliveredUnits;
        public float travelSeconds;
        public float travelTiles;
        public float time;
        public bool empty;
    }
    sealed class TimedWorkerWait
    {
        public ProductionFlowPlan flow;
        public string reason;
        public float time;
    }
    readonly List<TimedWorkerTrip> workerTripHistory = new List<TimedWorkerTrip>();
    readonly List<TimedWorkerWait> workerWaitHistory = new List<TimedWorkerWait>();
    const float ThroughputWindowSeconds = 60f;
    float throughputTrackingStartedAt;
    MoneyManager moneyManager;
    [Header("Production scheduling")]
    [Tooltip("Safety interval for a full planning scan. Production changes also wake the planner immediately.")]
    [Min(0.25f)] public float planningInterval = 1f;
    [Tooltip("How long an abandoned claim may survive without an active worker.")]
    [Min(3f)] public float abandonedClaimTimeout = 30f;
    float nextPlanningTime;
    float nextDirtyPlanningTime;
    bool planningDirty = true;

    public struct WorkerEfficiencySnapshot
    {
        public float actualOutputPerMinute;
        public float averageBatchSize;
        public float averageTravelSeconds;
        public int deliveryTrips;
        public int emptyTrips;
        public string mostCommonWaitReason;
    }

    FreezerStation freezer;
    GrillStation grill;
    AssemblyStation assembly;
    FryerStation fryer;
    DrinkStation drinkStation;
    HeatLampStation[] cachedHeatLamps = System.Array.Empty<HeatLampStation>();
    AssemblyStation[] cachedAssemblyStations = System.Array.Empty<AssemblyStation>();
    FreezerStation[] cachedFreezerStations = System.Array.Empty<FreezerStation>();
    PantryStation[] cachedPantryStations = System.Array.Empty<PantryStation>();
    CuttingStation[] cachedCuttingStations = System.Array.Empty<CuttingStation>();
    GrillStation[] cachedGrillStations = System.Array.Empty<GrillStation>();
    FryerStation[] cachedFryerStations = System.Array.Empty<FryerStation>();
    DrinkStation[] cachedDrinkStations = System.Array.Empty<DrinkStation>();
    ShakeStation[] cachedShakeStations = System.Array.Empty<ShakeStation>();
    bool stationCacheInitialized;
    bool stationCacheDirty = true;

    public ItemDefinition PattyItem => orderConfig != null ? orderConfig.burgerBase : null;
    public ItemDefinition FriesItem => orderConfig != null ? orderConfig.friesItem : null;
    public ItemDefinition PotatoItem => orderConfig != null
        ? (orderConfig.friesIngredient != null ? orderConfig.friesIngredient : orderConfig.friesItem)
        : null;
    public ItemDefinition SlicedPotatoItem => orderConfig != null
        ? (orderConfig.slicedPotatoIngredient != null
            ? orderConfig.slicedPotatoIngredient : PotatoItem)
        : null;
    public ItemDefinition CookedPotatoItem => orderConfig != null
        ? (orderConfig.cookedPotatoIngredient != null
            ? orderConfig.cookedPotatoIngredient : FriesItem)
        : null;
    public AssemblyRecipeDefinition GetAssemblyRecipe(ItemDefinition product) =>
        orderConfig != null ? orderConfig.GetAssemblyRecipe(product) : null;
    public ItemDefinition GetPantryItemForProduct(ItemDefinition product)
    {
        if (orderConfig != null && orderConfig.IsFries(product)) return PotatoItem;
        AssemblyRecipeDefinition recipe = GetAssemblyRecipe(product);
        if (recipe != null && recipe.pantryInput != null) return recipe.pantryInput;
        return null;
    }
    public ItemDefinition GetAssemblySupplySource(AssemblyStation target)
    {
        AssemblyRecipeDefinition recipe = target != null ? target.GetSelectedRecipe() : null;
        return orderConfig != null ? orderConfig.GetAssemblySupplySource(recipe) : null;
    }
    public ItemDefinition GetAssemblySupplyOutput(AssemblyStation target)
    {
        AssemblyRecipeDefinition recipe = target != null ? target.GetSelectedRecipe() : null;
        return recipe != null ? recipe.pantryInput : null;
    }
    public ItemDefinition DrinkItem => orderConfig != null ? orderConfig.drinkItem : null;
    public HeatLampStation HeatLamp => heatLamp;
    public int PendingJobCount => pendingJobs.Count;

    public ItemDefinition GetAssemblySupplySource(ProductionJob job) =>
        job != null && job.assemblySupplySourceItem != null
            ? job.assemblySupplySourceItem
            : GetAssemblySupplySource(job != null ? job.assemblySupplyTarget : null);
    public ItemDefinition GetAssemblySupplyOutput(ProductionJob job) =>
        job != null && job.assemblySupplyOutputItem != null
            ? job.assemblySupplyOutputItem
            : GetAssemblySupplyOutput(job != null ? job.assemblySupplyTarget : null);
    public ItemDefinition GetAssemblySupplyStepInput(ProductionJob job)
    {
        if (job == null || job.currentStepIndex <= 0) return GetAssemblySupplySource(job);
        return job.assemblySupplyStageItems != null
            && job.currentStepIndex - 1 < job.assemblySupplyStageItems.Length
            ? job.assemblySupplyStageItems[job.currentStepIndex - 1]
            : GetAssemblySupplySource(job);
    }
    public ItemDefinition GetAssemblySupplyStepOutput(ProductionJob job) => GetBranchTransferItem(job);

    /// <summary>
    /// Returns true only when a customer has already reached the pickup queue
    /// and still has this item on their order.
    /// </summary>
    public bool HasWaitingCustomerForItem(ItemDefinition item)
    {
        if (item == null) return false;
        EnsureStationCache();
        IReadOnlyList<CustomerAI> customers = CustomerAI.OrderBoardCustomers;
        for (int i = 0; i < customers.Count; i++)
        {
            CustomerAI customer = customers[i];
            if (customer == null || !customer.IsWaitingForPickup) continue;
            CustomerOrder order = customer.GetOrder();
            if (order?.lines == null) continue;
            foreach (CustomerOrder.OrderLine line in order.lines)
            {
                if (line.quantity <= 0 || line.item == null) continue;
                if (line.item == item || (!string.IsNullOrEmpty(line.item.itemName)
                    && line.item.itemName == item.itemName))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Per-item demand vs ready stock / cooking — used by the Customers management tab.</summary>
    public struct ItemOutputNeed
    {
        public ItemDefinition item;
        public int requested;
        /// <summary>Units in active customer orders accepted at a register.</summary>
        public int openOrdersAtRegister;
        public int ready;
        public int cooking;
        /// <summary>Number of this item in customer orders accepted at registers during the trailing simulation minute.</summary>
        public int orderedLastMinute;
        /// <summary>Register accepted item rate, normalized to a minute during the first minute of tracking.</summary>
        public float orderedPerMinute;
        /// <summary>Item units successfully delivered to Pickup Stations during the trailing minute.</summary>
        public int producedLastMinute;
        /// <summary>Kitchen units still short of live orders (absolute).</summary>
        public int requiredOutput;
        /// <summary>Exact scheduled customer demand target (items per minute).</summary>
        public float requiredPerMinute;
    }

    public List<ItemOutputNeed> GetRequiredOutputByItem(bool includeDrinks = true)
    {
        EnsureStationCache();
        CustomerSpawner demandSpawner = CustomerSpawner.Instance != null
            ? CustomerSpawner.Instance
            : FindObjectOfType<CustomerSpawner>();
        CustomerOrderConfig demandConfig = demandSpawner != null && demandSpawner.orderConfig != null
            ? demandSpawner.orderConfig
            : orderConfig;
        var acceptedOpenOrders = GetAcceptedOpenOrdersByItem(includeDrinks, demandConfig);
        var requested = new Dictionary<ItemDefinition, int>();
        foreach (var order in GetAllQueuedOrders())
        {
            if (order?.lines == null) continue;
            foreach (var line in order.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (!includeDrinks && orderConfig != null && orderConfig.IsDrink(line.item)) continue;
                requested[line.item] = requested.TryGetValue(line.item, out int c)
                    ? c + line.quantity
                    : line.quantity;
            }
        }

        var ready = new Dictionary<ItemDefinition, int>();
        var cooking = new Dictionary<ItemDefinition, int>();
        foreach (HeatLampStation lamp in cachedHeatLamps)
        {
            if (lamp == null) continue;
            foreach (var meal in lamp.Meals)
            {
                if (meal?.order?.lines == null) continue;
                foreach (var line in meal.order.lines)
                {
                    if (line.item == null || line.quantity <= 0) continue;
                    ready[line.item] = ready.TryGetValue(line.item, out int c) ? c + line.quantity : line.quantity;
                }
            }
        }
        foreach (var job in pendingJobs)
        {
            if (job == null || job.isAssemblySupply) continue;
            ItemDefinition item = job?.product;
            if (item == null) continue;
            int units = Mathf.Max(1, job.order != null
                ? job.order.CountQuantityOf(item) : job.heldUnits);
            cooking[item] = cooking.TryGetValue(item, out int c) ? c + units : units;
        }

        PruneThroughputHistory();
        var orderedLastMinute = SumHistory(orderedHistory);
        var producedLastMinute = SumHistory(pickupDeliveryHistory);
        float observedDemandWindow = Mathf.Clamp(Time.time - throughputTrackingStartedAt,
            1f, ThroughputWindowSeconds);

        var items = new HashSet<ItemDefinition>();
        foreach (var kv in requested) items.Add(kv.Key);
        foreach (var kv in acceptedOpenOrders) items.Add(kv.Key);
        foreach (var kv in ready) items.Add(kv.Key);
        foreach (var kv in cooking) items.Add(kv.Key);
        foreach (var kv in orderedLastMinute) items.Add(kv.Key);
        foreach (var kv in producedLastMinute) items.Add(kv.Key);
        foreach (var item in GetCookableMenuItems())
            if (item != null) items.Add(item);
        if (demandConfig != null)
        {
            foreach (ItemDefinition item in demandConfig.GetEnabledMenuItems())
            {
                if (item == null || (!includeDrinks && demandConfig.IsDrink(item))) continue;
                items.Add(item);
            }
        }

        var list = new List<ItemOutputNeed>(items.Count);
        foreach (var item in items)
        {
            if (item == null) continue;
            requested.TryGetValue(item, out int req);
            ready.TryGetValue(item, out int readyCount);
            cooking.TryGetValue(item, out int cookingCount);
            int shortfall = Mathf.Max(0, req - readyCount - cookingCount);

            orderedLastMinute.TryGetValue(item, out int orderedRate);
            producedLastMinute.TryGetValue(item, out int producedRate);

            list.Add(new ItemOutputNeed
            {
                item = item,
                requested = req,
                openOrdersAtRegister = acceptedOpenOrders.TryGetValue(item, out int openCount) ? openCount : 0,
                ready = readyCount,
                cooking = cookingCount,
                orderedLastMinute = orderedRate,
                orderedPerMinute = orderedRate * 60f / observedDemandWindow,
                producedLastMinute = producedRate,
                requiredOutput = shortfall,
                requiredPerMinute = demandSpawner != null
                    ? demandSpawner.GetCustomerDemandPerMinute(item)
                    : (demandConfig != null ? demandConfig.GetCustomerDemandPerMinute(item) : 0f)
            });
        }

        list.Sort((a, b) =>
        {
            int cmp = b.requiredPerMinute.CompareTo(a.requiredPerMinute);
            if (cmp != 0) return cmp;
            cmp = b.requiredOutput.CompareTo(a.requiredOutput);
            if (cmp != 0) return cmp;
            string an = a.item != null ? a.item.itemName : "";
            string bn = b.item != null ? b.item.itemName : "";
            return string.CompareOrdinal(an, bn);
        });
        return list;
    }

    Dictionary<ItemDefinition, int> GetAcceptedOpenOrdersByItem(bool includeDrinks,
        CustomerOrderConfig demandConfig)
    {
        var counts = new Dictionary<ItemDefinition, int>();
        IReadOnlyList<CustomerAI> customers = CustomerAI.OrderBoardCustomers;
        for (int i = 0; i < customers.Count; i++)
        {
            CustomerAI customer = customers[i];
            if (customer == null || customer.IsLeaving || !customer.HasOrderedAtRegister) continue;

            CustomerOrder order = customer.GetOrder();
            if (order?.lines == null) continue;
            foreach (CustomerOrder.OrderLine line in order.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (!includeDrinks && demandConfig != null && demandConfig.IsDrink(line.item)) continue;
                counts[line.item] = counts.TryGetValue(line.item, out int current)
                    ? current + line.quantity
                    : line.quantity;
            }
        }

        return counts;
    }

    /// <summary>Records item quantities only after the register accepts the customer's order.</summary>
    public void RecordCustomerOrder(CustomerOrder order)
    {
        RecordHistory(orderedHistory, order);
    }

    /// <summary>Records item units only after a Pickup Station accepts the delivery.</summary>
    public void RecordPickupDelivery(CustomerOrder order)
    {
        RecordHistory(pickupDeliveryHistory, order);
    }

    /// <summary>Delivered item rate over the current observed shift window.</summary>
    public float GetPickupDeliveryRatePerMinute(ItemDefinition item)
    {
        if (item == null) return 0f;
        PruneThroughputHistory();
        int total = 0;
        foreach (TimedItemCount entry in pickupDeliveryHistory)
        {
            if (entry == null || entry.item == null || entry.count <= 0) continue;
            if (entry.item == item || entry.item.itemName == item.itemName)
                total += entry.count;
        }

        float observedWindow = Mathf.Clamp(Time.time - throughputTrackingStartedAt,
            1f, ThroughputWindowSeconds);
        return total * 60f / observedWindow;
    }

    /// <summary>Starts a clean rolling output window for the next player-run shift.</summary>
    public void BeginShiftThroughputTracking()
    {
        orderedHistory.Clear();
        pickupDeliveryHistory.Clear();
        completedFlowHistory.Clear();
        workerTripHistory.Clear();
        workerWaitHistory.Clear();
        throughputTrackingStartedAt = Time.time;
    }

    public void RecordFlowCompletedOutput(KitchenEmployee worker, ItemDefinition item, int count)
    {
        if (worker == null || item == null || count <= 0) return;
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        if (flow == null) return;
        completedFlowHistory.Add(new TimedFlowOutput
        {
            flow = flow,
            item = item,
            count = count,
            time = Time.time
        });
        PruneThroughputHistory();
        NotifyProductionStateChanged();
    }

    public void RecordWorkerDeliveryTrip(KitchenEmployee worker, int deliveredUnits,
        float travelSeconds, float travelTiles)
    {
        if (worker == null || deliveredUnits <= 0) return;
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        if (flow == null) return;
        workerTripHistory.Add(new TimedWorkerTrip
        {
            flow = flow,
            deliveredUnits = deliveredUnits,
            travelSeconds = Mathf.Max(0f, travelSeconds),
            travelTiles = Mathf.Max(0f, travelTiles),
            time = Time.time
        });
        PruneWorkerEfficiencyHistory();
    }

    public void RecordWorkerEmptyTrip(KitchenEmployee worker, float travelSeconds,
        float travelTiles)
    {
        if (worker == null || travelTiles < 0.5f) return;
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        if (flow == null) return;
        workerTripHistory.Add(new TimedWorkerTrip
        {
            flow = flow,
            travelSeconds = Mathf.Max(0f, travelSeconds),
            travelTiles = Mathf.Max(0f, travelTiles),
            time = Time.time,
            empty = true
        });
        PruneWorkerEfficiencyHistory();
    }

    void RecordWorkerWait(KitchenEmployee worker, string reason)
    {
        if (worker == null || string.IsNullOrWhiteSpace(reason)) return;
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        RecordWorkerWait(flow, reason);
    }

    void RecordWorkerWait(ProductionFlowPlan flow, string reason)
    {
        if (flow == null) return;
        if (string.IsNullOrWhiteSpace(reason)) return;
        workerWaitHistory.Add(new TimedWorkerWait
        {
            flow = flow,
            reason = reason.Trim(),
            time = Time.time
        });
        PruneWorkerEfficiencyHistory();
    }

    public WorkerEfficiencySnapshot GetWorkerEfficiencySnapshot(ProductionFlowPlan flow)
    {
        var snapshot = new WorkerEfficiencySnapshot
        {
            actualOutputPerMinute = GetFlowCompletedOutputPerMinute(flow),
            mostCommonWaitReason = string.Empty
        };
        if (flow == null) return snapshot;

        int deliveredUnits = 0;
        float travelSeconds = 0f;
        int deliveryTrips = 0;
        int emptyTrips = 0;
        var waitCounts = new Dictionary<string, int>();
        foreach (TimedWorkerTrip trip in workerTripHistory)
        {
            if (trip == null || trip.flow != flow) continue;
            if (trip.empty)
            {
                emptyTrips++;
                continue;
            }
            deliveryTrips++;
            deliveredUnits += trip.deliveredUnits;
            travelSeconds += trip.travelSeconds;
        }
        snapshot.deliveryTrips = deliveryTrips;
        snapshot.emptyTrips = emptyTrips;
        snapshot.averageBatchSize = deliveryTrips > 0
            ? (float)deliveredUnits / deliveryTrips : 0f;
        snapshot.averageTravelSeconds = deliveryTrips > 0
            ? travelSeconds / deliveryTrips : 0f;

        for (int i = 0; i < workerWaitHistory.Count; i++)
        {
            TimedWorkerWait wait = workerWaitHistory[i];
            if (wait == null || wait.flow != flow || string.IsNullOrEmpty(wait.reason)) continue;
            waitCounts[wait.reason] = waitCounts.TryGetValue(wait.reason, out int count)
                ? count + 1 : 1;
        }
        int mostFrequent = 0;
        foreach (KeyValuePair<string, int> pair in waitCounts)
        {
            if (pair.Value <= mostFrequent) continue;
            mostFrequent = pair.Value;
            snapshot.mostCommonWaitReason = pair.Key;
        }
        return snapshot;
    }

    public float GetFlowCompletedOutputPerMinute(ProductionFlowPlan flow)
    {
        if (flow == null) return 0f;
        PruneThroughputHistory();
        int total = 0;
        foreach (TimedFlowOutput entry in completedFlowHistory)
            if (entry != null && entry.flow == flow)
                total += entry.count;
        float observedWindow = Mathf.Clamp(Time.time - throughputTrackingStartedAt,
            1f, ThroughputWindowSeconds);
        return total * 60f / observedWindow;
    }

    void RecordHistory(List<TimedItemCount> history, CustomerOrder order)
    {
        if (history == null || order?.lines == null) return;
        float now = Time.time;
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            history.Add(new TimedItemCount { item = line.item, count = line.quantity, time = now });
        }
        PruneThroughputHistory();
    }

    void PruneThroughputHistory()
    {
        float cutoff = Time.time - ThroughputWindowSeconds;
        orderedHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
        pickupDeliveryHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
        completedFlowHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
        PruneWorkerEfficiencyHistory(cutoff);
    }

    void PruneWorkerEfficiencyHistory()
    {
        PruneWorkerEfficiencyHistory(Time.time - ThroughputWindowSeconds);
    }

    void PruneWorkerEfficiencyHistory(float cutoff)
    {
        workerTripHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
        workerWaitHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
    }

    static Dictionary<ItemDefinition, int> SumHistory(List<TimedItemCount> history)
    {
        var totals = new Dictionary<ItemDefinition, int>();
        if (history == null) return totals;
        foreach (TimedItemCount entry in history)
        {
            if (entry?.item == null || entry.count <= 0) continue;
            totals[entry.item] = totals.TryGetValue(entry.item, out int count)
                ? count + entry.count
                : entry.count;
        }
        return totals;
    }

    float EstimateCustomersPerMinute()
    {
        var spawner = FindObjectOfType<CustomerSpawner>();
        if (spawner != null)
            return spawner.CustomersPerMinute;
        return 10f;
    }

    Dictionary<ItemDefinition, float> EstimateOrderChanceByItem(bool includeDrinks = false)
    {
        var chances = new Dictionary<ItemDefinition, float>();
        if (orderConfig == null) return chances;

        // Matches GenerateRandomOrder: independent rolls, with fallback to at least one item.
        var enabledBurgers = new List<ItemDefinition>();
        var enabledFries = new List<ItemDefinition>();
        var enabledDrinks = new List<ItemDefinition>();
        foreach (ItemDefinition item in orderConfig.GetEnabledMenuItems())
        {
            if (orderConfig.IsBurger(item)) enabledBurgers.Add(item);
            else if (orderConfig.IsFries(item)) enabledFries.Add(item);
            else if (orderConfig.IsDrink(item)) enabledDrinks.Add(item);
        }
        bool anyBurgerEnabled = enabledBurgers.Count > 0;
        float b = anyBurgerEnabled ? Mathf.Clamp01(orderConfig.burgerChance) : 0f;
        float f = enabledFries.Count > 0 ? Mathf.Clamp01(orderConfig.friesChance) : 0f;
        float d = enabledDrinks.Count > 0 ? Mathf.Clamp01(orderConfig.drinkChance) : 0f;
        float none = (1f - b) * (1f - f) * (1f - d);

        float burgerP = b;
        float friesP = f;
        float drinkP = d;
        if (none > 0f)
        {
            if (anyBurgerEnabled) burgerP += none;
            else if (enabledFries.Count > 0) friesP += none;
            else if (enabledDrinks.Count > 0) drinkP += none;
        }

        AddCategoryChances(chances, enabledBurgers, burgerP);
        AddCategoryChances(chances, enabledFries, friesP);
        if (includeDrinks) AddCategoryChances(chances, enabledDrinks, drinkP);
        return chances;
    }

    static void AddCategoryChances(Dictionary<ItemDefinition, float> chances,
        List<ItemDefinition> items, float categoryChance)
    {
        if (items == null || items.Count == 0) return;
        float perItem = categoryChance / items.Count;
        foreach (ItemDefinition item in items)
            if (item != null) chances[item] = perItem;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        moneyManager = FindObjectOfType<MoneyManager>();
        throughputTrackingStartedAt = Time.time;
        EnsureProductionFlows();
    }

    public ProductionFlowPlan SelectedFlow
    {
        get
        {
            EnsureProductionFlows();
            selectedFlowIndex = Mathf.Clamp(selectedFlowIndex, 0, productionFlows.Count - 1);
            return productionFlows[selectedFlowIndex];
        }
    }

    public void EnsureProductionFlows()
    {
        if (productionFlows == null) productionFlows = new List<ProductionFlowPlan>();
        productionFlows.RemoveAll(flow => flow == null);
        if (productionFlows.Count == 0)
        {
            productionFlows.Add(new ProductionFlowPlan
            {
                flowName = "Flow 1",
                kind = KitchenFlowKind.Custom,
                stepIds = new List<string>(),
                stations = new List<GameObject>()
            });
        }

        foreach (ProductionFlowPlan flow in productionFlows)
        {
            flow.Clean();
            flow.kind = KitchenFlowKind.Custom;
        }
        selectedFlowIndex = Mathf.Clamp(selectedFlowIndex, 0, productionFlows.Count - 1);
        SyncLegacyFlowSelection();
    }

    public ProductionFlowPlan CreateProductionFlow()
    {
        EnsureProductionFlows();
        int number = productionFlows.Count + 1;
        var flow = new ProductionFlowPlan
        {
            flowName = "Flow " + number,
            kind = KitchenFlowKind.Custom,
            stepIds = new List<string>(),
            stations = new List<GameObject>()
        };
        productionFlows.Add(flow);
        selectedFlowIndex = productionFlows.Count - 1;
        lastFlowBalance = null;
        SyncLegacyFlowSelection();
        NotifyProductionStateChanged(0f);
        return flow;
    }

    public void SelectProductionFlow(int index)
    {
        EnsureProductionFlows();
        selectedFlowIndex = Mathf.Clamp(index, 0, productionFlows.Count - 1);
        lastFlowBalance = null;
        SyncLegacyFlowSelection();
    }

    public void SyncLegacyFlowSelection()
    {
        if (productionFlows == null || productionFlows.Count == 0) return;
        selectedFlowIndex = Mathf.Clamp(selectedFlowIndex, 0, productionFlows.Count - 1);
        ProductionFlowPlan flow = productionFlows[selectedFlowIndex];
        hireFlow = flow.kind;
        hireFlowSteps = flow.stepIds;
    }

    public KitchenEmployee AddAvailableWorkerToSelectedFlow()
    {
        ProductionFlowPlan flow = SelectedFlow;
        if (employees == null) return null;
        foreach (KitchenEmployee employee in employees)
        {
            if (employee == null || IsWorkerOnAnyFlow(employee)) continue;
            flow.workers.Add(employee);
            lastFlowBalance = WorkerFlowAssigner.ApplyBalancedTeam(flow);
            NotifyProductionStateChanged(0f);
            return employee;
        }
        return null;
    }

    public KitchenEmployee RemoveLastWorkerFromSelectedFlow()
    {
        ProductionFlowPlan flow = SelectedFlow;
        if (flow.workers.Count == 0) return null;
        KitchenEmployee employee = flow.workers[flow.workers.Count - 1];
        flow.workers.RemoveAt(flow.workers.Count - 1);
        if (employee != null) employee.ClearAllOperatedStations();
        lastFlowBalance = WorkerFlowAssigner.ApplyBalancedTeam(flow);
        NotifyProductionStateChanged(0f);
        return employee;
    }

    public void RemoveWorkerFromFlow(ProductionFlowPlan flow, KitchenEmployee employee)
    {
        if (flow == null || employee == null || flow.workers == null) return;
        if (!flow.workers.Remove(employee)) return;
        employee.ClearAllOperatedStations();
        lastFlowBalance = flow.workers.Count > 0
            ? WorkerFlowAssigner.ApplyBalancedTeam(flow)
            : new TeamBalanceResult { message = "No workers assigned." };
        NotifyProductionStateChanged(0f);
    }

    public void RemoveProductionFlow(ProductionFlowPlan flow)
    {
        if (flow == null || productionFlows == null || !productionFlows.Contains(flow)) return;
        ProductionFlowPlan previousSelection = SelectedFlow;
        int removedIndex = productionFlows.IndexOf(flow);
        foreach (KitchenEmployee worker in new List<KitchenEmployee>(flow.workers))
            if (worker != null) worker.ClearAllOperatedStations();
        flow.workers.Clear();
        productionFlows.Remove(flow);
        selectedFlowIndex = previousSelection != flow ? productionFlows.IndexOf(previousSelection)
            : Mathf.Min(removedIndex, productionFlows.Count - 1);
        lastFlowBalance = null;
        // Keep an empty flow available when the player deletes their last route.
        EnsureProductionFlows();
        NotifyProductionStateChanged(0f);
    }

    public bool IsWorkerOnAnyFlow(KitchenEmployee employee)
    {
        if (employee == null || productionFlows == null) return false;
        foreach (ProductionFlowPlan flow in productionFlows)
            if (flow != null && flow.workers != null && flow.workers.Contains(employee))
                return true;
        return false;
    }

    /// <summary>Stations are shared resources and may appear on multiple flows.</summary>
    public bool IsStationOnOtherFlow(GameObject station, ProductionFlowPlan except)
    {
        return false;
    }

    public ProductionFlowPlan GetFlowForWorker(KitchenEmployee worker)
    {
        if (worker == null || productionFlows == null) return null;
        foreach (ProductionFlowPlan flow in productionFlows)
            if (flow != null && flow.workers != null && flow.workers.Contains(worker))
                return flow;
        return null;
    }

    public bool IsStationInWorkerFlow(KitchenEmployee worker, GameObject station)
    {
        if (station == null) return false;
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        return flow?.stations != null && flow.stations.Contains(station);
    }

    /// <summary>Returns the best outgoing branch in the worker's currently assigned flow.</summary>
    public GameObject GetFlowOutput(KitchenEmployee worker, GameObject station, ProductionJob job = null,
        ItemDefinition producedItem = null)
    {
        ProductionFlowPlan flow = GetFlowForWorker(worker);
        if (flow?.stations == null || station == null || !flow.stations.Contains(station)) return null;
        if (!flow.graphInitialized) flow.EnsureLegacyConnections();
        job ??= worker != null ? worker.ActiveJob : null;
        ItemDefinition plannedTransferItem = producedItem != null
            ? producedItem : job != null ? GetBranchTransferItem(job) : null;
        GameObject plannedSource = job != null
            ? job.reservedWorkStation ?? job.taskSourceStation ?? job.reservedSourceStation
            : null;
        if (job != null && plannedSource == station && plannedTransferItem != null
            && job.reservedItem == plannedTransferItem && job.reservedDestinationStation != null
            && flow.stations.Contains(job.reservedDestinationStation))
            return job.reservedDestinationStation;

        List<GameObject> outgoing = outgoingCandidateBuffer;
        flow.GetOutgoing(station, outgoing);
        outgoing.RemoveAll(candidate => candidate == null || !flow.stations.Contains(candidate));
        // A supply job has one exact receiver. It still needs a visible edge,
        // just like every other transfer, so the graph and worker stay in sync.
        GameObject requiredTarget = GetRequiredBranchTarget(job, station);
        if (requiredTarget != null)
            return flow.stations.Contains(requiredTarget)
                && flow.HasConnection(station, requiredTarget)
                && CanBranchAcceptItem(requiredTarget, GetBranchTransferItem(job))
                ? requiredTarget : null;
        bool finalProduct = job != null
            ? !GetRequiredNextStationType(job).HasValue
            : producedItem != null && orderConfig != null
                && orderConfig.GetProductKind(producedItem) != CustomerOrderConfig.ProductKind.None;
        bool finalAssemblyProduct = finalProduct
            && station.GetComponent<AssemblyStation>() != null;
        ItemDefinition finalItem = producedItem;
        if (finalItem == null && job != null)
            finalItem = job.CurrentWorkProduct ?? job.product;
        // Finished assembly products stock the pickup station ahead of demand.
        bool canRouteFinalItem = !finalProduct || finalAssemblyProduct
            || HasWaitingCustomerForItem(finalItem);
        if (outgoing.Count == 0)
        {
            return null;
        }

        if (job == null)
        {
            if (producedItem == null) return outgoing[0];
            GameObject itemTarget = null;
            float bestItemTargetScore = float.MaxValue;
            foreach (GameObject candidate in outgoing)
            {
                if (!CanBranchAcceptItem(candidate, producedItem)) continue;
                if (candidate.GetComponent<HeatLampStation>() != null && !canRouteFinalItem)
                    continue;
                float score = GetReceiverRouteScore(candidate, producedItem, null)
                    + Vector3.SqrMagnitude(candidate.transform.position - station.transform.position);
                if (score < bestItemTargetScore)
                {
                    bestItemTargetScore = score;
                    itemTarget = candidate;
                }
            }
            return itemTarget;
        }
        StationType? requiredType = GetRequiredNextStationType(job);
        ItemDefinition requiredAssemblyProduct = GetRequiredNextAssemblyProduct(job);
        ItemDefinition transferItem = GetBranchTransferItem(job);
        GameObject best = null;
        float bestScore = float.MaxValue;
        foreach (GameObject candidate in outgoing)
        {
            if (candidate == null) continue;
            StationType? candidateType = KitchenEmployee.GetStationTypeFrom(candidate);
            bool pickup = candidate.GetComponent<HeatLampStation>() != null;
            if (pickup && !canRouteFinalItem) continue;
            if (requiredType.HasValue)
            {
                if (candidateType != requiredType) continue;
            }
            else if (!pickup)
            {
                continue;
            }

            // A flow can contain more than one station of the required type.
            // Only route an ingredient to one that is configured to accept it;
            // otherwise a cheese-supply job can be sent to a potato cutter and
            // remain blocked even when a compatible cutter is connected.
            if (!CanBranchAcceptItem(candidate, transferItem)) continue;

            if (requiredType == StationType.Assembly && requiredAssemblyProduct != null)
            {
                AssemblyStation assembly = candidate.GetComponent<AssemblyStation>();
                if (assembly == null || !assembly.CanProcess(requiredAssemblyProduct)) continue;
            }

            float score = GetReceiverRouteScore(candidate, transferItem, job)
                + Vector3.SqrMagnitude(candidate.transform.position - station.transform.position);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    bool IsValidFlowHandoff(KitchenEmployee worker, ProductionFlowPlan flow,
        GameObject source, GameObject destination, ItemDefinition item)
    {
        if (worker == null || flow?.stations == null || source == null || destination == null
            || item == null)
            return false;
        if (!flow.graphInitialized) flow.EnsureLegacyConnections();
        return flow.stations.Contains(source) && flow.stations.Contains(destination)
            && flow.HasConnection(source, destination)
            && CanBranchAcceptItem(destination, item);
    }

    float GetReceiverRouteScore(GameObject receiverObject, ItemDefinition item,
        ProductionJob job)
    {
        IStationBuffer receiver = receiverObject != null
            ? receiverObject.GetComponent<IStationBuffer>() : null;
        if (receiver == null || item == null) return 0f;

        int freeSlots = GetAvailableInputCapacity(receiver, receiverObject, item, job);
        if (freeSlots <= 0) return 100000000f;

        int occupiedForItem = receiver.GetInputCount(item)
            + ReservedInputCount(receiverObject, item, job);
        // Compare buffered seconds at each receiver's configured target rate.
        // This keeps parallel branches fed in proportion to their recipe demand
        // instead of splitting items by raw buffer count.
        float requiredInputRate = GetReceiverRequiredInputRatePerMinute(receiverObject, item);
        float bufferedMinutes = requiredInputRate > 0.01f
            ? occupiedForItem / requiredInputRate
            : occupiedForItem;
        return occupiedForItem <= 0 ? 0f : 100000f + bufferedMinutes * 60000f;
    }

    float GetReceiverRequiredInputRatePerMinute(GameObject receiverObject, ItemDefinition item)
    {
        if (receiverObject == null || item == null) return 0f;
        StationNode node = receiverObject.GetComponent<StationNode>();
        float outputRate = node != null ? Mathf.Max(0f, node.outputAmountPerMinute) : 0f;
        if (outputRate <= 0.01f) return 0f;

        GrillStation grill = receiverObject.GetComponent<GrillStation>();
        if (grill != null)
            return item == grill.GetSelectedInput() || item == grill.GetSelectedSecondaryInput()
                ? outputRate : 0f;

        FryerStation fryer = receiverObject.GetComponent<FryerStation>();
        if (fryer != null)
        {
            StationProcessingRecipeDefinition recipe = orderConfig != null
                ? orderConfig.GetFryerRecipe(fryer.GetSelectedOutput()) : null;
            return recipe != null && recipe.input == item ? outputRate : 0f;
        }

        CuttingStation cutting = receiverObject.GetComponent<CuttingStation>();
        if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            return recipe != null && recipe.input == item ? outputRate : 0f;
        }

        AssemblyStation assembly = receiverObject.GetComponent<AssemblyStation>();
        AssemblyRecipeDefinition assemblyRecipe = assembly != null
            ? assembly.GetSelectedRecipe() : null;
        if (assemblyRecipe == null) return 0f;
        if (assemblyRecipe.RequiresProcessedInput && item == assemblyRecipe.processedInput)
            return outputRate * Mathf.Max(1, assemblyRecipe.processedInputAmount);
        if (item == assemblyRecipe.pantryInput)
            return outputRate * Mathf.Max(1, assemblyRecipe.pantryInputAmount);
        if (item == assemblyRecipe.thirdInput && assembly.IsMk2)
            return outputRate * Mathf.Max(1, assemblyRecipe.thirdInputAmount);
        return 0f;
    }

    GameObject GetRequiredBranchTarget(ProductionJob job, GameObject source)
    {
        if (job == null || !job.isAssemblySupply) return null;
        // Only force the configured destination on the final supply step.
        // Multi-stage ingredients can continue after Cutting, for example
        // Bacon Slab -> Cutting -> Grill -> Assembly.
        if (job.currentStepIndex >= job.pipeline.Length - 1)
            return job.assemblySupplyTarget != null ? job.assemblySupplyTarget.gameObject
                : job.grillSupplyTarget != null ? job.grillSupplyTarget.gameObject : null;
        return null;
    }

    static StationType? GetRequiredNextStationType(ProductionJob job)
    {
        if (job == null || job.pipeline == null) return null;
        int next = job.currentStepIndex + 1;
        return next >= 0 && next < job.pipeline.Length ? job.pipeline[next] : (StationType?)null;
    }

    static ItemDefinition GetRequiredNextAssemblyProduct(ProductionJob job)
    {
        if (job == null || job.pipeline == null || job.assemblyStageProducts == null) return null;
        int assemblyIndex = -1;
        for (int i = 0; i <= job.currentStepIndex + 1 && i < job.pipeline.Length; i++)
            if (job.pipeline[i] == StationType.Assembly) assemblyIndex++;
        return assemblyIndex >= 0 && assemblyIndex < job.assemblyStageProducts.Length
            ? job.assemblyStageProducts[assemblyIndex] : null;
    }

    public ItemDefinition GetBranchTransferItem(ProductionJob job)
    {
        if (job == null) return null;
        if (job.isAssemblySupply && (job.assemblySupplyTarget != null || job.grillSupplyTarget != null))
        {
            if (job.assemblySupplyStageItems != null && job.currentStepIndex >= 0
                && job.currentStepIndex < job.assemblySupplyStageItems.Length
                && job.assemblySupplyStageItems[job.currentStepIndex] != null)
                return job.assemblySupplyStageItems[job.currentStepIndex];
            return job.currentStepIndex >= job.pipeline.Length - 1
                ? GetAssemblySupplyOutput(job) : GetAssemblySupplySource(job);
        }
        if (job.CurrentStationType == StationType.Pantry && orderConfig != null && orderConfig.IsFries(job.product))
            return PotatoItem;
        if (job.CurrentStationType == StationType.Cutting && orderConfig != null && orderConfig.IsFries(job.product))
            return SlicedPotatoItem;
        if (job.CurrentStationType == StationType.Fryer && orderConfig != null && orderConfig.IsFries(job.product))
            return CookedPotatoItem;
        if (job.CurrentStationType == StationType.Freezer && orderConfig != null)
            return orderConfig.rawPattyIngredient;
        if (job.CurrentStationType == StationType.Grill && orderConfig != null)
        {
            List<ItemDefinition> stages = orderConfig.GetAssemblyChain(job.product);
            AssemblyRecipeDefinition first = stages.Count > 0 ? orderConfig.GetAssemblyRecipe(stages[0]) : null;
            return first != null && first.processedInput != null
                ? first.processedInput : orderConfig.cookedPattyIngredient;
        }
        return job.CurrentWorkProduct ?? job.product;
    }

    bool CanBranchAcceptItem(GameObject target, ItemDefinition item)
    {
        return CanFlowStationReceiveItem(target, item);
    }

    public bool CanFlowStationReceiveItem(GameObject destination, ItemDefinition item)
    {
        if (destination == null || item == null || orderConfig == null) return false;
        if (destination.GetComponent<HeatLampStation>() != null)
            return orderConfig.IsBurger(item) || orderConfig.IsFries(item) || orderConfig.IsDrink(item);

        GrillStation grill = destination.GetComponent<GrillStation>();
        if (grill != null)
            return item == grill.GetSelectedInput() || item == grill.GetSelectedSecondaryInput();

        CuttingStation cutting = destination.GetComponent<CuttingStation>();
        if (cutting != null) return cutting.CanProcess(item);

        FryerStation fryer = destination.GetComponent<FryerStation>();
        if (fryer != null)
        {
            StationProcessingRecipeDefinition recipe = orderConfig.GetFryerRecipe(fryer.GetSelectedOutput());
            return recipe != null && recipe.input == item;
        }

        AssemblyStation assembly = destination.GetComponent<AssemblyStation>();
        AssemblyRecipeDefinition assemblyRecipe = assembly != null ? assembly.GetSelectedRecipe() : null;
        if (assemblyRecipe == null) return false;
        return (assemblyRecipe.RequiresProcessedInput && item == assemblyRecipe.processedInput)
            || item == assemblyRecipe.pantryInput
            || (assembly.IsMk2 && item == assemblyRecipe.thirdInput);
    }

    public void AddWorkerToSelectedFlow(KitchenEmployee employee)
    {
        EnsureProductionFlows();
        AddWorkerToFlow(SelectedFlow, employee);
    }

    public void AddWorkerToFlow(ProductionFlowPlan targetFlow, KitchenEmployee employee)
    {
        if (targetFlow == null || employee == null) return;
        EnsureProductionFlows();
        if (!productionFlows.Contains(targetFlow)) return;
        if (targetFlow.workers.Contains(employee))
        {
            lastFlowBalance = WorkerFlowAssigner.ApplyBalancedTeam(targetFlow);
            return;
        }
        var affectedFlows = new List<ProductionFlowPlan>();
        foreach (ProductionFlowPlan other in productionFlows)
            if (other != null && other.workers != null)
            {
                if (other != targetFlow && other.workers.Remove(employee))
                    affectedFlows.Add(other);
            }
        employee.ClearAllOperatedStations();
        foreach (ProductionFlowPlan affected in affectedFlows)
            if (affected.workers.Count > 0)
                WorkerFlowAssigner.ApplyBalancedTeam(affected);
        targetFlow.workers.Add(employee);
        lastFlowBalance = WorkerFlowAssigner.ApplyBalancedTeam(targetFlow);
        NotifyProductionStateChanged(0f);
    }

    /// <summary>Hire without spending money (debug).</summary>
    public KitchenEmployee HireWorkerFree()
    {
        if (employeePrefab == null)
        {
            Debug.LogWarning("ProductionManager: no employeePrefab assigned.");
            return null;
        }

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        GameObject go = Instantiate(employeePrefab, pos, Quaternion.identity);
        var emp = go.GetComponent<KitchenEmployee>();
        if (emp != null)
        {
            emp.AssignRandomName();
            go.name = emp.employeeName;
            RegisterEmployee(emp);
            RaiseFirstWorkerHiredEvent();
        }
        return emp;
    }

    public KitchenEmployee HireWorker()
    {
        if (employeePrefab == null)
        {
            Debug.LogWarning("ProductionManager: no employeePrefab assigned. Use Production > Create Default Employee Prefab and assign it.");
            return null;
        }

        if (!CanHireWorker())
            return null;

        int cost = GetHireCost();
        if (cost > 0 && moneyManager != null && !moneyManager.TrySpend(cost))
            return null;
        if (cost > 0)
            Sfx.Play(SfxId.SpendMoney);

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        GameObject go = Instantiate(employeePrefab, pos, Quaternion.identity);
        var emp = go.GetComponent<KitchenEmployee>();
        if (emp != null)
        {
            emp.AssignRandomName();
            go.name = emp.employeeName;
            RegisterEmployee(emp);
            Sfx.Play(SfxId.HireWorker);
            RaiseFirstWorkerHiredEvent();
            int paid = cost;
            var undo = PurchaseUndoManager.Ensure();
            if (undo != null)
                undo.RecordWorkerHire(emp, paid);
        }
        else if (cost > 0 && moneyManager != null)
            moneyManager.AddMoney(cost);
        return emp;
    }

    /// <summary>First worker is free; later hires use hireCost.</summary>
    public int GetHireCost()
    {
        int count = employees != null ? employees.Count : 0;
        return count <= 0 ? 0 : Mathf.Max(0, hireCost);
    }

    public int HiredWorkerCount
    {
        get
        {
            if (employees == null) return 0;
            int count = 0;
            foreach (var emp in employees)
                if (emp != null) count++;
            return count;
        }
    }

    public const int StartingHireCap = 3;
    public const int ExtraHiresFromFourthWorkerResearch = 1;
    public const int ExtraHiresFromFifthWorkerResearch = 1;
    public const int ExtraHiresFromSixthWorkerResearch = 1;

    /// <summary>Hire limits grow through Fourth Worker, Fifth Worker, and Sixth Worker research.</summary>
    public int MaxHiredWorkers => StartingHireCap
        + (MilestoneFeatures.ExtraStaffingUnlocked ? ExtraHiresFromFourthWorkerResearch : 0)
        + (MilestoneFeatures.IsResearchComplete("fifth_worker") ? ExtraHiresFromFifthWorkerResearch : 0)
        + (MilestoneFeatures.IsResearchComplete("sixth_worker") ? ExtraHiresFromSixthWorkerResearch : 0);

    public bool CanHireWorker()
    {
        if (employeePrefab == null) return false;
        return HiredWorkerCount < MaxHiredWorkers;
    }

    public bool ExtraHireLocked => HiredWorkerCount >= MaxHiredWorkers
        && !MilestoneFeatures.ExpertStaffingUnlocked;

    static bool raisedFirstWorkerEvent;

    static void RaiseFirstWorkerHiredEvent()
    {
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.WorkerHired);
        if (raisedFirstWorkerEvent) return;
        raisedFirstWorkerEvent = true;
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstWorkerHired);
    }

    public void FireWorker(KitchenEmployee emp)
    {
        if (emp == null) return;
        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
            undo.NotifyWorkerFired(emp);
        UnregisterEmployee(emp);
        Destroy(emp.gameObject);
    }

    void OnDestroy()
    {
        scheduledProduction?.Dispose();
        if (Instance == this)
            Instance = null;
    }

    float nextPlanningDiagnostic;

    void Update()
    {
        if (useScheduledProduction)
        {
            ScheduledProduction.Tick();
            return;
        }
        if (Time.timeScale <= 0f)
            return;
        float now = Time.unscaledTime;
        bool fallbackDue = now >= nextPlanningTime;
        bool changeDue = planningDirty && now >= nextDirtyPlanningTime;
        if (!fallbackDue && !changeDue)
            return;
        planningDirty = false;
        nextPlanningTime = now + Mathf.Max(1f, planningInterval);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        RefreshStations();
        RecoverOrphanedJobAssignments();
        CollectProductionJobs();
        EnsureAssemblySupplyJobs();
        EnsureGrillBufferSupplyJobs();
        AssignJobsToEmployees();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
        if (elapsed >= 12 && Time.realtimeSinceStartup >= nextPlanningDiagnostic)
        {
            nextPlanningDiagnostic = Time.realtimeSinceStartup + 5f;
            Debug.Log($"[Performance] Production planning: {elapsed:F1} ms; workers={employees.Count}, pending jobs={pendingJobs.Count}.");
        }
#endif
    }

    /// <summary>
    /// Wakes the planner after inventory, station, worker, or flow changes.
    /// Several same-frame changes collapse into one scan; a slower periodic scan
    /// remains as a recovery path for state changes without an explicit signal.
    /// </summary>
    public void NotifyProductionStateChanged(float debounceSeconds = 0.05f)
    {
        scheduledProduction?.RequestConfigurationCheck();
        planningDirty = true;
        float due = Time.unscaledTime + Mathf.Max(0f, debounceSeconds);
        if (nextDirtyPlanningTime <= Time.unscaledTime || due < nextDirtyPlanningTime)
            nextDirtyPlanningTime = due;
    }

    void EnsureAssemblySupplyJobs()
    {
        foreach (AssemblyStation station in cachedAssemblyStations)
        {
            if (station == null) continue;
            AssemblyRecipeDefinition recipe = station.GetSelectedRecipe();
            if (recipe == null || recipe.output == null) continue;
            // Assembly-produced ingredients are transferred by the producer's
            // output task, not fetched from a Pantry as raw stock.
            if (recipe.pantryInput != null && orderConfig?.GetAssemblyRecipe(recipe.pantryInput) == null)
                EnsureAssemblyIngredientSupplyJob(station, recipe,
                    orderConfig != null ? orderConfig.GetAssemblySupplySource(recipe) : recipe.pantryInput,
                    recipe.pantryInput, station.BufferedPantryInputCount,
                    orderConfig != null && orderConfig.AssemblySupplyRequiresCutting(recipe),
                    recipe.supplyPipeline, recipe.supplyStageOutputs);
            if ((recipe.processedInputFromPantry || recipe.processedInputFromFreezer)
                && recipe.processedInput != null)
                EnsureAssemblyIngredientSupplyJob(station, recipe, recipe.processedInput,
                    recipe.processedInput, station.BufferedProcessedInputCount, false, null, null);
            else if (recipe.processedInput != null && orderConfig != null)
            {
                // Input 1 normally arrives from the main production chain. Some
                // branch assemblies, such as Veggie Mix, instead use a processed
                // ingredient there. Infer its raw-to-cutting supply route from the
                // configured cutting recipe so it cannot be silently ignored.
                CuttingRecipeDefinition processedRecipe = orderConfig.GetCuttingRecipe(recipe.processedInput);
                if (processedRecipe != null && processedRecipe.input != null)
                {
                    StationType sourceType = orderConfig.IsFreezerIngredient(processedRecipe.input)
                        ? StationType.Freezer : StationType.Pantry;
                    EnsureAssemblyIngredientSupplyJob(station, recipe, processedRecipe.input,
                        recipe.processedInput, station.BufferedProcessedInputCount, true,
                        new[] { sourceType, StationType.Cutting },
                        new[] { processedRecipe.input, recipe.processedInput });
                }
            }
            if (recipe.thirdInput != null && orderConfig?.GetAssemblyRecipe(recipe.thirdInput) == null)
                EnsureAssemblyIngredientSupplyJob(station, recipe,
                    orderConfig != null ? orderConfig.GetAssemblyThirdSupplySource(recipe) : recipe.thirdInput,
                    recipe.thirdInput, station.BufferedThirdInputCount,
                    orderConfig != null && orderConfig.AssemblyThirdSupplyRequiresProcessing(recipe),
                    recipe.thirdSupplyPipeline, recipe.thirdSupplyStageOutputs);
        }
    }

    void EnsureGrillBufferSupplyJobs()
    {
        if (orderConfig == null) return;
        foreach (GrillStation grill in cachedGrillStations)
        {
            if (grill == null || grill.GetSelectedOutput() == null) continue;
            QueueGrillInputSupplyJob(grill, grill.GetSelectedInput());
            ItemDefinition secondary = grill.GetSelectedSecondaryInput();
            if (secondary != null && secondary != grill.GetSelectedInput())
                QueueGrillInputSupplyJob(grill, secondary);
        }
    }

    void QueueGrillInputSupplyJob(GrillStation grill, ItemDefinition inputItem)
    {
        if (grill == null || inputItem == null) return;
        int target = grill.InputSlotCapacity;
        int reserved = grill.GetInputCount(inputItem);
        foreach (ProductionJob queued in pendingJobs)
        {
            if (queued == null || !queued.isAssemblySupply || queued.grillSupplyTarget != grill
                || queued.assemblySupplyOutputItem != inputItem)
                continue;
            reserved += Mathf.Max(1, queued.requestedSupplyUnits);
        }
        if (reserved >= target) return;

        CuttingRecipeDefinition cuttingRecipe = orderConfig.GetCuttingRecipe(inputItem);
        ItemDefinition sourceItem = cuttingRecipe != null && cuttingRecipe.input != null
            ? cuttingRecipe.input : inputItem;
        StationType sourceType = orderConfig.IsFreezerIngredient(sourceItem)
            ? StationType.Freezer : StationType.Pantry;
        StationType[] pipeline = cuttingRecipe != null && cuttingRecipe.input != null
            ? new[] { sourceType, StationType.Cutting }
            : new[] { sourceType };
        ItemDefinition[] stageItems = pipeline.Length > 1
            ? new[] { sourceItem, inputItem }
            : new[] { inputItem };
        int requestBatch = Mathf.Min(target - reserved,
            GetLargestCarryCapacityForFlowStation(grill.gameObject));
        if (requestBatch <= 0) return;

        var supply = new ProductionJob(CustomerOrder.FromItem(grill.GetSelectedOutput(), 1), pipeline)
        {
            isAssemblySupply = true,
            grillSupplyTarget = grill,
            assemblySupplySourceItem = sourceItem,
            assemblySupplyOutputItem = inputItem,
            assemblySupplyStageItems = stageItems,
            requestedSupplyUnits = requestBatch
        };
        if (CanAnyWorkerContinue(supply)) pendingJobs.Add(supply);
    }

    void EnsureAssemblyIngredientSupplyJob(AssemblyStation station, AssemblyRecipeDefinition recipe,
        ItemDefinition sourceItem, ItemDefinition outputItem, int buffered, bool requiresCutting,
        StationType[] pipelineOverride, ItemDefinition[] stageItems)
    {
        if (station == null || recipe == null || sourceItem == null || outputItem == null) return;
        ProductionJob parentDemand = FindParentDemand(recipe.output);
        // Assembly supply is buffer-driven. Active demand increases urgency in
        // worker scoring, but it must not collapse the requested stock to one.
        int target = GetAssemblyInputCapacity(station, outputItem);
        if (target <= 0) return;
        int reserved = buffered;
        foreach (ProductionJob queued in pendingJobs)
        {
            if (queued == null || !queued.isAssemblySupply || queued.assemblySupplyTarget != station
                || queued.assemblySupplyOutputItem != outputItem) continue;
            reserved += Mathf.Max(1, queued.requestedSupplyUnits);
        }
        if (reserved >= target) return;
        StationType sourceType = orderConfig != null && orderConfig.IsFreezerIngredient(sourceItem)
            ? StationType.Freezer : StationType.Pantry;
        StationType[] pipeline = pipelineOverride != null && pipelineOverride.Length > 0
            ? pipelineOverride : (requiresCutting ? new[] { sourceType, StationType.Cutting }
                : new[] { sourceType });
        int requestBatch = Mathf.Min(target - reserved,
            GetLargestCarryCapacityForFlowStation(station.gameObject));
        var supply = new ProductionJob(CustomerOrder.FromItem(recipe.output, 1), pipeline, null,
            parentDemand != null ? parentDemand.rootDemandId : 0)
        {
            isAssemblySupply = true,
            assemblySupplyTarget = station,
            assemblySupplySourceItem = sourceItem,
            assemblySupplyOutputItem = outputItem,
            assemblySupplyStageItems = stageItems,
            requestedSupplyUnits = requestBatch
        };
        if (CanAnyWorkerContinue(supply))
        {
            pendingJobs.Add(supply);
            if (parentDemand != null && !parentDemand.prerequisiteTaskIds.Contains(supply.taskId))
                parentDemand.prerequisiteTaskIds.Add(supply.taskId);
        }
    }

    ProductionJob FindParentDemand(ItemDefinition assemblyOutput)
    {
        if (assemblyOutput == null) return null;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.isAssemblySupply) continue;
            if (job.CurrentWorkProduct == assemblyOutput) return job;
            if (job.assemblyStageProducts == null) continue;
            foreach (ItemDefinition stage in job.assemblyStageProducts)
                if (stage == assemblyOutput) return job;
        }
        return null;
    }

    int MenuJobCount()
    {
        int count = 0;
        foreach (ProductionJob job in pendingJobs)
            if (job != null && !job.isAssemblySupply && ClaimsPickupCapacity(job))
                count += Mathf.Max(1, job.order != null ? job.order.GetTotalQuantity() : job.heldUnits);
        return count;
    }

    bool ClaimsPickupCapacity(ProductionJob job)
    {
        // Queued orders at an empty source have no physical WIP. They must not
        // prevent another flow with available ingredients from using the pass.
        if (job.assignedTo != null || job.currentStepIndex > 0 || job.heldUnits > 0
            || job.ingredientsHeld.Count > 0 || job.taskSourceStation != null
            || job.taskPhase == ProductionTaskPhase.CollectOutput) return true;
        foreach (KitchenEmployee employee in employees)
            if (employee != null && employee.CanTakeJobs && employee.CanTakeJobStep(job, true))
                return true;
        return false;
    }

    void RecoverOrphanedJobAssignments()
    {
        for (int i = pendingJobs.Count - 1; i >= 0; i--)
        {
            ProductionJob job = pendingJobs[i];
            if (job == null)
            {
                pendingJobs.RemoveAt(i);
                continue;
            }

            KitchenEmployee owner = job.assignedTo;
            bool claimExpired = owner != null
                && (!owner.isActiveAndEnabled
                    || Time.time - job.claimHeartbeatAt >= abandonedClaimTimeout);
            if (owner != null && (!owner.IsWorkingOn(job) || claimExpired))
                job.assignedTo = null;

            // Reservations are meaningful only while a worker owns the task.
            // A task released by a recovery path must not leave a station or
            // destination permanently unavailable to the rest of the flow.
            if (job.assignedTo == null && (job.reservedWorkStation != null
                || job.reservedSourceStation != null || job.reservedDestinationStation != null))
                ReleaseReservations(job);

            if (job.assignedTo == null && job.taskState != ProductionTaskState.Cancelled
                && Time.time >= job.retryAfter)
                SetTaskState(job, ProductionTaskState.WaitingForDependencies);

            // Only remove jobs that no worker is structurally capable of continuing.
            // Missing ingredients and full downstream buffers are temporary
            // backpressure, not evidence that the job is obsolete.
            if (job.assignedTo == null && !CanAnyWorkerContinue(job))
                pendingJobs.RemoveAt(i);
        }
    }

    bool CanAnyWorkerContinue(ProductionJob job)
    {
        if (job == null || job.IsHeatLampStep) return false;

        foreach (KitchenEmployee employee in employees)
        {
            if (employee != null && employee.CanTakeJobs && employee.CanOperateJobStep(job))
                return true;
        }

        return false;
    }

    /// <summary>Run job collection now (e.g. right after a cook finishes a delivery).</summary>
    public void RequestImmediateProduction()
    {
        RefreshStations();
        CollectProductionJobs();
        NotifyProductionStateChanged(0f);
    }

    void RefreshStations()
    {
        EnsureStationCache();
        registers.RemoveAll(r => r == null || !r.gameObject.activeInHierarchy);
        IReadOnlyList<Register> liveRegisters = Register.ActiveRegisters;
        for (int i = 0; i < liveRegisters.Count; i++)
        {
            Register placed = liveRegisters[i];
            if (placed != null && !registers.Contains(placed))
                registers.Add(placed);
        }
        if (freezer == null && cachedFreezerStations.Length > 0) freezer = cachedFreezerStations[0];
        if (grill == null && cachedGrillStations.Length > 0) grill = cachedGrillStations[0];
        if (assembly == null && cachedAssemblyStations.Length > 0) assembly = cachedAssemblyStations[0];
        if (fryer == null && cachedFryerStations.Length > 0) fryer = cachedFryerStations[0];
        if (drinkStation == null && cachedDrinkStations.Length > 0) drinkStation = cachedDrinkStations[0];
        if (heatLamp == null)
            heatLamp = HeatLampStation.Instance != null ? HeatLampStation.Instance
                : (cachedHeatLamps.Length > 0 ? cachedHeatLamps[0] : null);
    }

    void EnsureStationCache()
    {
        if (!stationCacheInitialized || stationCacheDirty)
            RefreshStationCache();
    }

    public void InvalidateStationCache()
    {
        stationCacheDirty = true;
        NotifyProductionStateChanged(0f);
    }

    void RefreshStationCache()
    {
        cachedHeatLamps = FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedAssemblyStations = FindObjectsByType<AssemblyStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedFreezerStations = FindObjectsByType<FreezerStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedPantryStations = FindObjectsByType<PantryStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedCuttingStations = FindObjectsByType<CuttingStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedGrillStations = FindObjectsByType<GrillStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedFryerStations = FindObjectsByType<FryerStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedDrinkStations = FindObjectsByType<DrinkStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        cachedShakeStations = FindObjectsByType<ShakeStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        stationCacheInitialized = true;
        stationCacheDirty = false;
    }

    void CollectProductionJobs()
    {
        if (orderConfig == null) return;

        HeatLampStation[] lamps = cachedHeatLamps;
        if (lamps == null || lamps.Length == 0) return;

        int heldCount = 0;
        int totalCapacity = 0;
        foreach (HeatLampStation lamp in lamps)
        {
            if (lamp == null) continue;
            heldCount += lamp.Count;
            totalCapacity += Mathf.Max(0, lamp.maxCapacity);
        }

        // Pickup stations are four-unit WIP buffers. Keep every slot filled or
        // reserved so a carry upgrade cannot stop after one partial batch.
        int inFlight = heldCount + MenuJobCount();
        int openSlots = totalCapacity - inFlight;

        // Always keep stock up — kitchen produces without waiting for customers.
        var cookable = GetCookableMenuItems();
        if (cookable.Count == 0) return;

        var stockCounts = CountInFlightByItem();

        // Keep assigned cooks cycling. There are no per-item stock targets now;
        // workers balance the available menu products while capacity remains.
        if (openSlots <= 0) return;

        // Prefer the least represented product so every unlocked menu route can
        // use the available system capacity instead of being controlled by a
        // player-entered item target.
        while (openSlots > 0)
        {
            // Dependency-blocked jobs do not reserve pickup slots, but still
            // count as queued demand. Do not recreate them every planning tick.
            cookable.RemoveAll(candidate => stockCounts.TryGetValue(candidate, out int queued)
                && queued >= totalCapacity);
            ItemDefinition item = PickLeastStockedItem(cookable, stockCounts);
            if (item == null) break;
            var job = CreateJob(CustomerOrder.FromItem(item, 1));
            if (job == null) break;
            pendingJobs.Add(job);
            stockCounts[item] = stockCounts.TryGetValue(item, out int c) ? c + 1 : 1;
            openSlots--;
        }

        // Extra demand from live customers can push production up to max capacity.
        if (openSlots <= 0) return;
        var available = new List<CustomerOrder>();
        foreach (HeatLampStation lamp in lamps)
            if (lamp != null) available.AddRange(lamp.GetHeldOrderClones());
        foreach (var job in pendingJobs)
        {
            if (job == null || job.isAssemblySupply) continue;
            if (job?.order != null)
                available.Add(job.order.Clone());
        }

        foreach (var customerOrder in GetAllQueuedOrders())
        {
            if (customerOrder?.lines == null) continue;
            foreach (var line in customerOrder.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (!cookable.Contains(line.item)) continue;
                for (int q = 0; q < line.quantity; q++)
                {
                    if (openSlots <= 0) return;
                    int matchIdx = FindMatchingUnitIndex(available, line.item);
                    if (matchIdx >= 0)
                    {
                        if (available[matchIdx].GetTotalQuantity() <= 0)
                            available.RemoveAt(matchIdx);
                        continue;
                    }
                    var job = CreateJob(CustomerOrder.FromItem(line.item, 1));
                    if (job == null) continue;
                    pendingJobs.Add(job);
                    available.Add(CustomerOrder.FromItem(line.item, 1));
                    openSlots--;
                }
            }
        }
    }

    List<ItemDefinition> GetCookableMenuItems()
    {
        EnsureStationCache();
        var list = new List<ItemDefinition>();
        if (orderConfig == null) return list;

        bool canBurger = false;
        bool canFries = false;
        bool canShake = false;
        bool hasConfiguredFlow = false;
        if (productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow == null) continue;
                flow.Clean();
                if ((flow.stepIds != null && flow.stepIds.Count > 0)
                    || (flow.stations != null && flow.stations.Count > 0))
                    hasConfiguredFlow = true;

                bool flowHasFreezer = false;
                bool flowHasGrill = false;
                bool flowHasPantry = false;
                bool flowHasPotatoCutting = false;
                bool flowHasAssembly = false;
                bool flowHasFryer = false;
                if (flow.stepIds != null)
                {
                    for (int i = 0; i < flow.stepIds.Count; i++)
                    {
                        string id = flow.stepIds[i];
                        if (id == "Freezer") flowHasFreezer = true;
                        if (id == "Grill") flowHasGrill = true;
                        if (id == "Pantry") flowHasPantry = true;
                        if (id == "Cutting") flowHasPotatoCutting = true;
                        if (id == "Assembly") flowHasAssembly = true;
                        if (id == "Fryer") flowHasFryer = true;
                        if (id == "Shake") canShake = true;
                    }
                }
                if (flow.stations != null)
                {
                    foreach (GameObject station in flow.stations)
                    {
                        if (station == null) continue;
                        FreezerStation flowFreezer = station.GetComponent<FreezerStation>();
                        if (flowFreezer != null && orderConfig.burgerBase != null
                            && flowFreezer.CanSupply(orderConfig.burgerBase)) flowHasFreezer = true;
                        if (station.GetComponent<GrillStation>() != null) flowHasGrill = true;
                        PantryStation flowPantry = station.GetComponent<PantryStation>();
                        if (flowPantry != null && flowPantry.CanDispense(PotatoItem)) flowHasPantry = true;
                        CuttingStation flowCutting = station.GetComponent<CuttingStation>();
                        if (flowCutting != null
                            && flowCutting.CanProcess(PotatoItem, SlicedPotatoItem))
                            flowHasPotatoCutting = true;
                        if (station.GetComponent<AssemblyStation>() != null) flowHasAssembly = true;
                        if (station.GetComponent<FryerStation>() != null) flowHasFryer = true;
                        if (station.GetComponent<ShakeStation>() != null) canShake = true;
                    }
                }
                if (flowHasFreezer && flowHasGrill && flowHasAssembly)
                    canBurger = true;
                if (flowHasPantry && flowHasPotatoCutting && flowHasFryer && flowHasAssembly)
                    canFries = true;
            }
        }

        // Fallback: if no flows yet, allow any menu item the kitchen has stations for.
        if (!hasConfiguredFlow)
        {
            canBurger = HasConfiguredFreezer()
                && (grill != null || cachedGrillStations.Length > 0)
                && (assembly != null || cachedAssemblyStations.Length > 0);
            canFries = (fryer != null || cachedFryerStations.Length > 0)
                && HasPantrySelection(PotatoItem, false)
                && HasCuttingSupplyPath(PotatoItem, SlicedPotatoItem, false)
                && HasAssemblyChainAvailable(FriesItem, false)
                && HasAssemblyPantrySupplies(FriesItem, false);
            canShake = cachedShakeStations.Length > 0;
        }

        if (canBurger && HasConfiguredFreezer())
        {
            foreach (ItemDefinition burger in orderConfig.GetMenuItems())
            {
                if (burger != null && orderConfig.IsBurger(burger)
                    && HasAssemblyChainAvailable(burger, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(burger, hasConfiguredFlow)
                    && HasRequiredCuttingSupplyAvailable(burger, hasConfiguredFlow))
                    list.Add(burger);
            }
        }
        if (canFries && HasPantrySelection(PotatoItem, hasConfiguredFlow)
            && HasCuttingSupplyPath(PotatoItem, SlicedPotatoItem, hasConfiguredFlow))
        {
            foreach (ItemDefinition item in orderConfig.GetMenuItems())
                if (orderConfig.IsFries(item) && HasAssemblyChainAvailable(item, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(item, hasConfiguredFlow)
                    && HasRequiredCuttingSupplyAvailable(item, hasConfiguredFlow))
                    list.Add(item);
        }
        if (canShake)
        {
            foreach (ItemDefinition item in orderConfig.GetMenuItems())
                if (orderConfig.IsDrink(item) && HasAssemblyChainAvailable(item, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(item, hasConfiguredFlow))
                    list.Add(item);
        }
        return list;
    }

    bool HasConfiguredFreezer()
    {
        if (orderConfig == null || orderConfig.burgerBase == null) return false;
        EnsureStationCache();
        foreach (FreezerStation candidate in cachedFreezerStations)
            if (candidate != null && candidate.CanSupply(orderConfig.burgerBase))
                return true;
        return false;
    }

    public string GetFlowQueueStatus(KitchenEmployee employee)
    {
        if (useScheduledProduction)
            return employee != null && employee.IsHoldingAnything
                ? "Carrying " + employee.HeldPreviewCount + ": " + employee.GetHeldInventoryDisplay() : "Carrying: none";
        ProductionFlowPlan flow = GetFlowForWorker(employee);
        if (flow?.stations == null) return "No assigned flow";
        var lines = new List<string>();
        int matchingJobs = 0;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || !employee.CanOperateJobStep(job)) continue;
            matchingJobs++;
            if (lines.Count >= 4) continue;
            string product = job.product != null ? job.product.itemName : "Unknown";
            GameObject readyStation = employee.FindStationForJob(job, true, true);
            lines.Add(product + " / " + job.CurrentStationType + (job.isAssemblySupply ? " supply" : "")
                + (job.assignedTo != null ? " — assigned" : readyStation != null ? " — ready" : " — waiting for inputs or space"));
        }
        lines.Insert(0, "Queued tasks for this worker: " + matchingJobs);
        EnsureStationCache();
        int readyMeals = 0, pickupCapacity = 0;
        foreach (HeatLampStation pickup in cachedHeatLamps)
        {
            if (pickup == null) continue;
            readyMeals += pickup.Count;
            pickupCapacity += Mathf.Max(0, pickup.maxCapacity);
        }
        int queuedMeals = MenuJobCount();
        lines.Add("Pickup: " + readyMeals + "/" + pickupCapacity + " ready; " + queuedMeals + " meals in progress");
        var stockCounts = CountInFlightByItem();
        int friesCount = FriesItem != null && stockCounts.TryGetValue(FriesItem, out int countedFries) ? countedFries : 0;
        lines.Add("Fries ready + in progress: " + friesCount);
        if (readyMeals + queuedMeals >= pickupCapacity)
            lines.Add("New meals paused: all pickup slots are filled or claimed by queued meals");
        else if (FriesItem != null && !GetCookableMenuItems().Contains(FriesItem))
            lines.Add("Fries recipe unavailable: check source selections and directed ingredient connections");
        else if (FriesItem != null && orderConfig != null && !orderConfig.IsItemEnabled(FriesItem))
            lines.Add("Fries is disabled for customer orders; its configured flow can still produce to pickup");
        foreach (GameObject station in flow.stations)
        {
            if (station == null) continue;
            PantryStation pantryStation = station.GetComponent<PantryStation>();
            if (pantryStation != null && pantryStation.CanDispense(PotatoItem))
                lines.Add("Potato stock: " + (KitchenInventory.Instance != null ? KitchenInventory.Instance.GetCount(PotatoItem).ToString() : "unlimited"));
            CuttingStation cuttingStation = station.GetComponent<CuttingStation>();
            if (cuttingStation != null && cuttingStation.CanProcess(PotatoItem, SlicedPotatoItem))
                lines.Add("Cutting: " + cuttingStation.GetInputCount(PotatoItem) + " raw / " + cuttingStation.GetOutputCount(SlicedPotatoItem) + " sliced");
            FryerStation fryerStation = station.GetComponent<FryerStation>();
            if (fryerStation != null)
                lines.Add("Fryer: " + fryerStation.BufferedUnitCount + (fryerStation.IsCooked() ? " cooked" : fryerStation.IsCooking ? " cooking" : " empty"));
        }
        return string.Join("\n", lines);
    }

    public string GetIdleFlowSupplyWarning(KitchenEmployee employee)
    {
        ProductionFlowPlan flow = GetFlowForWorker(employee);
        if (flow?.stations == null || orderConfig == null) return string.Empty;
        if (!flow.graphInitialized) flow.EnsureLegacyConnections();
        foreach (ProductionJob queuedJob in pendingJobs)
            if (queuedJob != null && queuedJob.assignedTo == null
                && queuedJob.blockedFlow == flow
                && !string.IsNullOrEmpty(queuedJob.blockedReason))
                return queuedJob.blockedReason;

        if (FriesItem != null)
        {
            foreach (GameObject station in flow.stations)
            {
                AssemblyStation assemblyStation = station != null ? station.GetComponent<AssemblyStation>() : null;
                AssemblyRecipeDefinition recipe = assemblyStation != null ? assemblyStation.GetSelectedRecipe() : null;
                if (recipe == null || !recipe.Produces(FriesItem) || recipe.pantryInput == null) continue;
                bool hasSupply = false;
                bool hasSupplyStock = false;
                foreach (GameObject source in flow.stations)
                {
                    PantryStation pantryStation = source != null ? source.GetComponent<PantryStation>() : null;
                    if (pantryStation != null && pantryStation.CanDispense(recipe.pantryInput)
                        && flow.GetOutgoing(source).Contains(station))
                    {
                        hasSupply = true;
                        if (pantryStation.HasItem(recipe.pantryInput)) hasSupplyStock = true;
                    }
                }
                if (!hasSupply)
                {
                    string ingredient = string.IsNullOrEmpty(recipe.pantryInput.itemName)
                        ? recipe.pantryInput.name : recipe.pantryInput.itemName;
                    return "Fries need " + ingredient + ": select it at a Pantry and connect that Pantry to Assembly";
                }
                if (!hasSupplyStock && assemblyStation.GetInputCount(recipe.pantryInput) == 0)
                {
                    string ingredient = string.IsNullOrEmpty(recipe.pantryInput.itemName)
                        ? recipe.pantryInput.name : recipe.pantryInput.itemName;
                    return ingredient + " is out of stock — order more ingredients";
                }
            }
        }

        return string.Empty;
    }

    string GetMissingFlowConnectionWarning(ProductionFlowPlan flow, GameObject source,
        ProductionJob job, ItemDefinition item)
    {
        string sourceName = GetFlowStationDisplayName(source);
        string itemName = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
        GameObject requiredTarget = GetRequiredBranchTarget(job, source);
        if (requiredTarget != null)
        {
            if (!flow.stations.Contains(requiredTarget))
                return "Add " + GetFlowStationDisplayName(requiredTarget)
                    + " to this flow to receive " + itemName;
            return "Connect " + sourceName + " to " + GetFlowStationDisplayName(requiredTarget)
                + " to deliver " + itemName;
        }

        StationType? requiredType = GetRequiredNextStationType(job);
        if (requiredType.HasValue)
        {
            GameObject compatibleTarget = null;
            foreach (GameObject candidate in flow.stations)
            {
                if (candidate == null || candidate == source
                    || KitchenEmployee.GetStationTypeFrom(candidate) != requiredType
                    || !CanBranchAcceptItem(candidate, item))
                    continue;
                if (requiredType == StationType.Assembly)
                {
                    ItemDefinition requiredProduct = GetRequiredNextAssemblyProduct(job);
                    AssemblyStation assembly = candidate.GetComponent<AssemblyStation>();
                    if (requiredProduct != null && (assembly == null || !assembly.CanProcess(requiredProduct)))
                        continue;
                }
                compatibleTarget = candidate;
                break;
            }

            if (compatibleTarget != null)
                return "Connect " + sourceName + " to " + GetFlowStationDisplayName(compatibleTarget)
                    + " to move " + itemName;
            return "Add a compatible " + requiredType.Value + " station after " + sourceName
                + " for " + itemName;
        }

        GameObject pickupTarget = null;
        foreach (GameObject candidate in flow.stations)
        {
            if (candidate != null && candidate.GetComponent<HeatLampStation>() != null
                && CanBranchAcceptItem(candidate, item))
            {
                pickupTarget = candidate;
                break;
            }
        }
        return pickupTarget != null
            ? "Connect " + sourceName + " to " + GetFlowStationDisplayName(pickupTarget)
                + " to send " + itemName + " to pickup"
            : "Add a Pickup Station after " + sourceName + " for " + itemName;
    }

    string GetMissingRequiredFlowHandoffReason(KitchenEmployee worker,
        ProductionFlowPlan flow, ProductionJob job, GameObject source)
    {
        if (worker == null || flow == null || job == null || source == null)
            return string.Empty;
        ItemDefinition item = GetBranchTransferItem(job);
        if (item == null || GetFlowOutput(worker, source, job) != null)
            return string.Empty;

        bool hasNextStation = GetRequiredNextStationType(job).HasValue;
        bool finalAssembly = source.GetComponent<AssemblyStation>() != null && !hasNextStation;
        bool finalProductHasDemand = !hasNextStation && HasWaitingCustomerForItem(item);
        if (GetRequiredBranchTarget(job, source) == null && !hasNextStation
            && !finalAssembly && !finalProductHasDemand)
            return string.Empty;

        AssemblyStation assembly = finalAssembly ? source.GetComponent<AssemblyStation>() : null;
        if (assembly != null && job.taskPhase == ProductionTaskPhase.Work
            && assembly.CanStoreOutput(GetProcessOutputBatch(job, worker, source)))
            return string.Empty;

        return GetMissingFlowConnectionWarning(flow, source, job, item);
    }

    static string GetFlowStationDisplayName(GameObject station)
    {
        if (station == null) return "station";
        StationNode node = station.GetComponent<StationNode>();
        return node != null && !string.IsNullOrEmpty(node.DisplayName)
            ? node.DisplayName : station.name;
    }

    bool HasAssemblyPantrySupplies(ItemDefinition product, bool useFlows)
    {
        if (orderConfig == null) return false;
        List<ItemDefinition> chain = orderConfig.GetAssemblyChain(product);
        if (chain == null || chain.Count == 0) return false;
        foreach (ItemDefinition stage in chain)
        {
            AssemblyRecipeDefinition recipe = orderConfig.GetAssemblyRecipe(stage);
            ItemDefinition source = orderConfig.GetAssemblySupplySource(recipe);
            if (recipe != null && recipe.pantryInput != null)
            {
                if (orderConfig.GetAssemblyRecipe(recipe.pantryInput) != null)
                {
                    if (!HasAssemblyProducer(recipe.pantryInput, useFlows)) return false;
                }
                else if (source != null && !HasIngredientSourceSelection(source, useFlows)) return false;
            }
            if (recipe != null && recipe.thirdInput != null)
            {
                ItemDefinition thirdSource = orderConfig.GetAssemblyThirdSupplySource(recipe);
                if (orderConfig.GetAssemblyRecipe(recipe.thirdInput) != null)
                {
                    if (!HasAssemblyProducer(recipe.thirdInput, useFlows)) return false;
                }
                else if (thirdSource != null && !HasIngredientSourceSelection(thirdSource, useFlows)) return false;
            }
            if (recipe != null && recipe.processedInputFromPantry
                && recipe.processedInput != null
                && !HasPantrySelection(recipe.processedInput, useFlows)) return false;
            if (recipe != null && recipe.processedInputFromFreezer
                && recipe.processedInput != null
                && !HasFreezerSelection(recipe.processedInput, useFlows)) return false;
            if (recipe != null && !recipe.processedInputFromPantry
                && !recipe.processedInputFromFreezer && recipe.processedInput != null)
            {
                CuttingRecipeDefinition processedRecipe = orderConfig.GetCuttingRecipe(recipe.processedInput);
                if (processedRecipe != null
                    && !HasCuttingSupplyPath(processedRecipe.input, recipe.processedInput, useFlows))
                    return false;
            }
        }
        return true;
    }

    bool HasPantrySelection(ItemDefinition ingredient, bool useFlows)
    {
        if (ingredient == null) return false;
        if (useFlows && productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stations == null) continue;
                foreach (GameObject station in flow.stations)
                {
                    PantryStation pantry = station != null ? station.GetComponent<PantryStation>() : null;
                    if (pantry != null && pantry.CanDispense(ingredient)) return true;
                }
            }
            return false;
        }

        EnsureStationCache();
        foreach (PantryStation pantry in cachedPantryStations)
            if (pantry != null && pantry.CanDispense(ingredient)) return true;
        return false;
    }

    bool HasAssemblyChainAvailable(ItemDefinition product, bool useFlows)
    {
        List<ItemDefinition> chain = orderConfig != null ? orderConfig.GetAssemblyChain(product) : null;
        if (chain == null || chain.Count == 0) return false;

        if (useFlows && productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stations == null) continue;
                int stage = 0;
                foreach (GameObject stationObject in flow.stations)
                {
                    AssemblyStation station = stationObject != null
                        ? stationObject.GetComponent<AssemblyStation>() : null;
                    if (station != null && station.CanProcess(chain[stage]))
                    {
                        stage++;
                        if (stage >= chain.Count) return true;
                    }
                }
            }
            return false;
        }

        var remaining = new HashSet<ItemDefinition>(chain);
        EnsureStationCache();
        foreach (AssemblyStation station in cachedAssemblyStations)
        {
            if (station == null) continue;
            foreach (ItemDefinition stageProduct in chain)
                if (station.CanProcess(stageProduct)) remaining.Remove(stageProduct);
        }
        return remaining.Count == 0;
    }

    bool HasRequiredCuttingSupplyAvailable(ItemDefinition product, bool useFlows)
    {
        List<ItemDefinition> chain = orderConfig != null ? orderConfig.GetAssemblyChain(product) : null;
        if (chain == null) return false;
        foreach (ItemDefinition stage in chain)
        {
            AssemblyRecipeDefinition recipe = orderConfig.GetAssemblyRecipe(stage);
            if (recipe == null) continue;
            if (orderConfig.AssemblySupplyRequiresCutting(recipe)
                && (recipe.supplyPipeline == null || recipe.supplyPipeline.Length == 0))
            {
                ItemDefinition requiredRaw = orderConfig.GetAssemblySupplySource(recipe);
                if (!HasCuttingSupplyPath(requiredRaw, recipe.pantryInput, useFlows)) return false;
            }
            if (orderConfig.AssemblyThirdSupplyRequiresProcessing(recipe)
                && (recipe.thirdSupplyPipeline == null || recipe.thirdSupplyPipeline.Length == 0))
            {
                ItemDefinition requiredRaw = orderConfig.GetAssemblyThirdSupplySource(recipe);
                if (!HasCuttingSupplyPath(requiredRaw, recipe.thirdInput, useFlows)) return false;
            }
        }
        return true;
    }

    bool HasCuttingSupplyPath(ItemDefinition requiredRaw, ItemDefinition requiredOutput, bool useFlows)
    {
        if (requiredRaw == null || requiredOutput == null) return false;
        if (!useFlows)
        {
            if (!HasIngredientSourceSelection(requiredRaw, false)) return false;
            EnsureStationCache();
            foreach (CuttingStation cutting in cachedCuttingStations)
                if (cutting != null && cutting.CanProcess(requiredRaw, requiredOutput)) return true;
            return false;
        }

        foreach (ProductionFlowPlan flow in productionFlows)
        {
            if (flow?.stations == null) continue;
            foreach (GameObject station in flow.stations)
            {
                if (station == null) continue;
                PantryStation pantry = station.GetComponent<PantryStation>();
                FreezerStation freezerStation = station.GetComponent<FreezerStation>();
                bool suppliesRaw = (pantry != null && pantry.CanDispense(requiredRaw))
                    || (freezerStation != null && freezerStation.CanSupply(requiredRaw));
                if (!suppliesRaw) continue;
                // Station insertion order is unrelated to the directed production route.
                foreach (GameObject destination in flow.GetOutgoing(station))
                {
                    if (destination == null || !flow.stations.Contains(destination)) continue;
                    CuttingStation cutting = destination.GetComponent<CuttingStation>();
                    if (cutting != null && cutting.CanProcess(requiredRaw, requiredOutput)) return true;
                }
            }
        }
        return false;
    }

    int CountIdleCookSlots(List<ItemDefinition> cookable)
    {
        if (cookable == null || cookable.Count == 0) return 0;
        int n = 0;
        foreach (var e in employees)
        {
            if (e == null || !e.IsIdle || !e.CanTakeJobs) continue;
            if (e.ShouldDeliverInsteadOfCook()) continue;
            if (PickItemWorkerCanCook(e, cookable, true) != null)
                n++;
        }
        return n;
    }

    void EnsurePendingJobsForIdleCooks(
        List<ItemDefinition> cookable,
        Dictionary<ItemDefinition, int> stockCounts,
        ref int openSlots)
    {
        if (openSlots <= 0 || cookable == null || cookable.Count == 0) return;

        foreach (var e in employees)
        {
            if (openSlots <= 0) return;
            if (e == null || !e.IsIdle || !e.CanTakeJobs) continue;
            if (e.ShouldDeliverInsteadOfCook()) continue;

            bool alreadyQueued = false;
            foreach (var job in pendingJobs)
            {
                if (job == null || job.assignedTo != null) continue;
                if (!e.CanTakeJobStep(job, true)) continue;
                alreadyQueued = true;
                break;
            }
            if (alreadyQueued) continue;

            ItemDefinition item = PickItemWorkerCanCook(e, cookable, true);
            if (item == null) continue;
            var jobNew = CreateJob(CustomerOrder.FromItem(item, 1));
            if (jobNew == null) continue;
            pendingJobs.Add(jobNew);
            stockCounts[item] = stockCounts.TryGetValue(item, out int c) ? c + 1 : 1;
            openSlots--;
        }
    }

    ItemDefinition PickItemWorkerCanCook(KitchenEmployee employee,
        List<ItemDefinition> cookable, bool searchEntireFlow = false)
    {
        if (employee == null || cookable == null) return null;
        ItemDefinition best = null;
        int bestCount = int.MaxValue;
        var stockCounts = CountInFlightByItem();
        for (int i = 0; i < cookable.Count; i++)
        {
            ItemDefinition item = cookable[i];
            if (item == null) continue;
            var probe = CreateJob(CustomerOrder.FromItem(item, 1));
            if (probe == null) continue;
            if (!employee.CanTakeJobStep(probe, searchEntireFlow)) continue;
            stockCounts.TryGetValue(item, out int count);
            if (best == null || count < bestCount)
            {
                best = item;
                bestCount = count;
            }
        }
        return best;
    }

    Dictionary<ItemDefinition, int> CountInFlightByItem()
    {
        var counts = new Dictionary<ItemDefinition, int>();
        EnsureStationCache();
        foreach (HeatLampStation lamp in cachedHeatLamps)
        {
            if (lamp == null) continue;
            foreach (var meal in lamp.Meals)
            {
                ItemDefinition item = meal?.order != null ? meal.order.PrimaryItem : null;
                if (item == null) continue;
                counts[item] = counts.TryGetValue(item, out int c) ? c + 1 : 1;
            }
        }
        foreach (var job in pendingJobs)
        {
            if (job == null || job.isAssemblySupply) continue;
            ItemDefinition item = job?.product;
            if (item == null) continue;
            int units = Mathf.Max(1, job.order != null
                ? job.order.CountQuantityOf(item) : job.heldUnits);
            counts[item] = counts.TryGetValue(item, out int c) ? c + units : units;
        }
        return counts;
    }

    static ItemDefinition PickLeastStockedItem(List<ItemDefinition> cookable, Dictionary<ItemDefinition, int> stockCounts)
    {
        if (cookable == null || cookable.Count == 0) return null;
        ItemDefinition best = null;
        int bestCount = int.MaxValue;
        for (int i = 0; i < cookable.Count; i++)
        {
            ItemDefinition item = cookable[i];
            if (item == null) continue;
            stockCounts.TryGetValue(item, out int count);
            if (best == null || count < bestCount)
            {
                best = item;
                bestCount = count;
            }
        }
        return best;
    }

    ProductionJob CreateJob(CustomerOrder singleItemOrder)
    {
        if (singleItemOrder == null || orderConfig == null) return null;
        var product = singleItemOrder.PrimaryItem;
        if (product == null) return null;
        var pipeline = orderConfig.GetPipeline(product);
        if (pipeline == null || pipeline.Length == 0) return null;
        return new ProductionJob(singleItemOrder, pipeline, orderConfig.GetAssemblyChain(product));
    }

    List<CustomerOrder> GetAllQueuedOrders()
    {
        var list = new List<CustomerOrder>();
        EnsureStationCache();
        IReadOnlyList<CustomerAI> customers = CustomerAI.OrderBoardCustomers;
        for (int i = 0; i < customers.Count; i++)
        {
            CustomerAI customer = customers[i];
            if (customer == null || customer.IsLeaving) continue;
            CustomerOrder order = customer.GetOrder();
            if (order != null && order.GetTotalQuantity() > 0)
                list.Add(order);
        }

        CustomerSpawner spawner = CustomerSpawner.Instance != null
            ? CustomerSpawner.Instance
            : FindObjectOfType<CustomerSpawner>();
        CustomerOrder scheduledDemand = spawner != null
            ? spawner.GetPendingScheduledDemand()
            : null;
        if (scheduledDemand != null && scheduledDemand.GetTotalQuantity() > 0)
            list.Add(scheduledDemand);

        return list;
    }

    static int FindMatchingUnitIndex(List<CustomerOrder> pool, ItemDefinition item)
    {
        if (pool == null || item == null) return -1;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && pool[i].CountQuantityOf(item) > 0
                && pool[i].TryRemoveOne(item))
                return i;
        }
        return -1;
    }

    void AssignJobsToEmployees()
    {
        var scannedFlows = new HashSet<ProductionFlowPlan>();
        foreach (var e in employees)
        {
            if (e == null) continue;
            ProductionFlowPlan flow = GetFlowForWorker(e);
            if (flow != null && scannedFlows.Add(flow))
                e.QueueRecoverableFlowTasks();
        }

        // Build and sort the global candidate set once per planning pass. Each
        // claim still rechecks station and buffer reservations, so stale
        // candidates are skipped without rescanning every worker/job/station
        // combination after each successful assignment.
        TryAssignBestGlobalCandidates();

        // A worker can still be idle when its flow has no queued runnable job,
        // even though a source station already has stock ready to process.
        // Give each remaining idle worker one normal scheduling pass. This may
        // create a stock job only when the flow is ready and pickup has space.
        foreach (KitchenEmployee employee in employees)
        {
            if (employee == null || employee.HasJob || !employee.CanTakeJobs
                || employee.IsWaitingToReevaluateTasks || employee.ShouldDeliverInsteadOfCook())
                continue;
            TryAssignHighestPriorityTask(employee);
        }
    }

    void TryAssignBestGlobalCandidates()
    {
        var candidates = new List<ScheduledCandidate>();

        foreach (KitchenEmployee employee in employees)
        {
            if (employee == null || employee.HasJob || !employee.CanTakeJobs
                || employee.IsWaitingToReevaluateTasks || employee.ShouldDeliverInsteadOfCook())
                continue;
            ProductionFlowPlan flow = GetFlowForWorker(employee);
            if (flow?.stations == null || flow.stations.Count == 0) continue;

            foreach (ProductionJob job in pendingJobs)
            {
                if (job == null || job.assignedTo != null || Time.time < job.retryAfter) continue;
                foreach (GameObject flowStation in flow.stations)
                {
                    if (flowStation == null) continue;
                    GameObject station = employee.FindStationForJob(
                        job, true, true, flowStation);
                    if (station == null || !flow.stations.Contains(station)) continue;
                    if (!CanStartTaskNow(job, station))
                    {
                        MarkTaskWaitingForDependencies(job, flow,
                            GetTaskWaitReason(job, station));
                        continue;
                    }
                    float score = ScoreTaskForWorker(employee, job, station);
                    candidates.Add(new ScheduledCandidate
                    {
                        job = job,
                        station = station,
                        score = score,
                        employee = employee
                    });
                }
            }
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (ScheduledCandidate candidate in candidates)
        {
            KitchenEmployee employee = candidate.employee;
            ProductionJob job = candidate.job;
            if (employee == null || employee.HasJob || !employee.CanTakeJobs
                || employee.IsWaitingToReevaluateTasks || employee.ShouldDeliverInsteadOfCook()
                || job == null || !pendingJobs.Contains(job) || job.assignedTo != null
                || Time.time < job.retryAfter || !CanStartTaskNow(job, candidate.station))
                continue;

            TryClaimAndAssign(employee, job, candidate.station);
        }
    }

    /// <summary>Assigns ready final output first, then fills the emptiest receiver.</summary>
    public bool TryAssignHighestPriorityTask(KitchenEmployee employee)
    {
        if (useScheduledProduction) return false;
        if (employee == null || employee.HasJob || !employee.CanTakeJobs) return false;
        if (employee.IsWaitingToReevaluateTasks || employee.ShouldDeliverInsteadOfCook()) return false;

        ProductionFlowPlan flow = GetFlowForWorker(employee);
        if (flow?.stations == null || flow.stations.Count == 0) return false;

        if (TryAssignBestRunnableFlowStep(employee, flow))
            return true;

        // If this flow has no queued work, create one compatible production
        // cycle and immediately try it. Nothing else is inferred or reordered.
        if (TryQueueCompatibleStockJob(employee, true) != null)
        {
            return TryAssignBestRunnableFlowStep(employee, flow);
        }

        return false;
    }

    /// <summary>
    /// Immediately assigns an already queued runnable task from this worker's
    /// flow. Unlike the normal idle scheduler, this does not create speculative
    /// stock jobs while the worker is yielding a blocked task.
    /// </summary>
    public bool TryAssignNextRunnableFlowTask(KitchenEmployee employee)
    {
        if (employee == null || employee.HasJob || !employee.CanTakeJobs
            || employee.ShouldDeliverInsteadOfCook())
            return false;

        ProductionFlowPlan flow = GetFlowForWorker(employee);
        return flow?.stations != null && flow.stations.Count > 0
            && TryAssignBestRunnableFlowStep(employee, flow);
    }

    sealed class ScheduledCandidate
    {
        public ProductionJob job;
        public GameObject station;
        public KitchenEmployee employee;
        public float score;
    }

    bool TryAssignBestRunnableFlowStep(KitchenEmployee employee, ProductionFlowPlan flow)
    {
        var candidates = new List<ScheduledCandidate>();
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.assignedTo != null || Time.time < job.retryAfter) continue;
            bool foundStation = false;
            bool foundRunnableStation = false;
            string waitReason = "Required input, station, or output capacity is unavailable";
            foreach (GameObject flowStation in flow.stations)
            {
                if (flowStation == null) continue;
                GameObject station = employee.FindStationForJob(
                    job, true, true, flowStation);
                if (station == null || !flow.stations.Contains(station)) continue;
                foundStation = true;

                if (!CanStartTaskNow(job, station))
                {
                    waitReason = GetTaskWaitReason(job, station);
                    continue;
                }

                foundRunnableStation = true;
                SetTaskState(job, ProductionTaskState.Runnable);
                candidates.Add(new ScheduledCandidate
                {
                    job = job,
                    station = station,
                    score = ScoreTaskForWorker(employee, job, station)
                });
            }

            if (!foundRunnableStation)
                MarkTaskWaitingForDependencies(job, flow,
                    foundStation ? waitReason : "Required input, station, or output capacity is unavailable");
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (ScheduledCandidate candidate in candidates)
        {
            if (TryClaimAndAssign(employee, candidate.job, candidate.station)) return true;
        }
        return false;
    }

    void MarkTaskWaitingForDependencies(ProductionJob job, ProductionFlowPlan flow, string reason)
    {
        if (job == null) return;
        bool flowRouteBlocked = false;
        if (flow?.workers != null)
        {
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || !worker.CanTakeJobs) continue;
                GameObject source = worker.FindStationForJob(job, true, false);
                string routeReason = source != null
                    ? GetMissingRequiredFlowHandoffReason(worker, flow, job, source)
                    : string.Empty;
                if (string.IsNullOrEmpty(routeReason)) continue;
                reason = routeReason;
                flowRouteBlocked = true;
                break;
            }
        }
        if (job.taskState != ProductionTaskState.WaitingForDependencies
            || job.blockedReason != reason)
            RecordWorkerWait(flow, reason);
        SetTaskState(job, ProductionTaskState.WaitingForDependencies, reason);
        if (flowRouteBlocked) job.blockedFlow = flow;
    }

    bool CanStartTaskNow(ProductionJob job, GameObject station)
    {
        if (job == null || station == null) return false;
        ItemDefinition item = GetBranchTransferItem(job);
        StationType? type = job.CurrentStationType;

        if (job.taskPhase == ProductionTaskPhase.CollectOutput)
        {
            GameObject source = job.taskSourceStation != null
                ? job.taskSourceStation : station;
            IStationBuffer sourceBuffer = source.GetComponent<IStationBuffer>();
            int available = sourceBuffer != null && item != null
                ? sourceBuffer.GetOutputCount(item) - GetReservedOutputCount(source, item)
                : 0;
            return available > 0;
        }

        if (type == StationType.Freezer)
        {
            FreezerStation freezerSource = station.GetComponent<FreezerStation>();
            return freezerSource != null && item != null
                && freezerSource.GetReadyOutputCount(item) > 0;
        }
        if (type == StationType.Pantry)
        {
            PantryStation pantrySource = station.GetComponent<PantryStation>();
            return pantrySource != null && item != null
                && pantrySource.GetReadyOutputCount(item) > 0;
        }
        return true;
    }

    static string GetTaskWaitReason(ProductionJob job, GameObject station)
    {
        if (job != null && job.taskPhase == ProductionTaskPhase.CollectOutput)
            return "Waiting for station output";
        StationType? type = job != null ? job.CurrentStationType : null;
        if (type == StationType.Freezer || type == StationType.Pantry)
            return "Waiting for stock or output rate";
        return "Waiting for station input or capacity";
    }

    bool TryClaimAndAssign(KitchenEmployee employee, ProductionJob job, GameObject station)
    {
        if (employee == null || job == null || employee.HasJob || job.assignedTo != null)
            return false;
        MergeQueuedSameItemJobs(job, employee, station);
        if (job.assignedTo != null
            || !TryReserveCurrentStation(job, employee, true, station)) return false;
        if (job.assignedTo != null)
        {
            ReleaseReservations(job);
            return false;
        }

        job.assignedTo = employee;
        job.claimVersion++;
        job.claimHeartbeatAt = Time.time;
        job.retryAfter = 0f;
        job.blockedStation = null;
        SetTaskState(job, ProductionTaskState.Claimed);
        employee.AssignJob(job);
        if (employee.ActiveJob == job) return true;

        job.assignedTo = null;
        ReleaseReservations(job);
        SetTaskState(job, ProductionTaskState.Runnable);
        return false;
    }

    void MergeQueuedSameItemJobs(ProductionJob primary, KitchenEmployee employee,
        GameObject preferredSource)
    {
        if (primary == null || employee == null || primary.isAssemblySupply || primary.assignedTo != null
            || primary.taskPhase != ProductionTaskPhase.Work || primary.currentStepIndex != 0
            || primary.heldUnits > 0 || primary.ingredientsHeld.Count > 0
            || primary.reservedWorkStation != null || primary.reservedDestinationStation != null
            || primary.order == null || primary.order.lines == null || primary.order.lines.Count != 1)
            return;

        ItemDefinition item = primary.product;
        int units = primary.order.CountQuantityOf(item);
        int limit = GetJobBatchLimit(primary, employee);
        if (item == null || units <= 0 || units >= limit) return;
        GameObject primarySource = preferredSource != null
            ? preferredSource : employee.FindStationForJob(primary, true, true);
        if (primarySource == null) return;
        limit = GetAvailableTaskBatchLimit(primary, employee, primarySource, limit);
        if (units >= limit) return;

        for (int i = pendingJobs.Count - 1; i >= 0 && units < limit; i--)
        {
            ProductionJob candidate = pendingJobs[i];
            if (candidate == null || candidate == primary || candidate.assignedTo != null
                || Time.time < candidate.retryAfter
                || candidate.isAssemblySupply || candidate.taskPhase != ProductionTaskPhase.Work
                || candidate.currentStepIndex != 0 || candidate.heldUnits > 0
                || candidate.ingredientsHeld.Count > 0 || candidate.reservedWorkStation != null
                || candidate.reservedDestinationStation != null || candidate.reservedSourceStation != null
                || candidate.product != item || candidate.order == null
                || candidate.order.lines == null || candidate.order.lines.Count != 1
                || !SamePipeline(primary, candidate))
                continue;
            if (employee.FindStationForJob(candidate, true, true, primarySource) != primarySource)
                continue;

            int candidateUnits = candidate.order.CountQuantityOf(item);
            if (candidateUnits <= 0 || units + candidateUnits > limit) continue;

            CustomerOrder.OrderLine line = primary.order.lines[0];
            line.quantity += candidateUnits;
            primary.order.lines[0] = line;
            units += candidateUnits;
            pendingJobs.RemoveAt(i);
            SetTaskState(candidate, ProductionTaskState.Complete);
        }
    }

    static bool SamePipeline(ProductionJob a, ProductionJob b)
    {
        if (a == null || b == null || a.pipeline == null || b.pipeline == null
            || a.pipeline.Length != b.pipeline.Length
            || a.assemblyStageProducts == null || b.assemblyStageProducts == null
            || a.assemblyStageProducts.Length != b.assemblyStageProducts.Length)
            return false;

        for (int i = 0; i < a.pipeline.Length; i++)
            if (a.pipeline[i] != b.pipeline[i]) return false;
        for (int i = 0; i < a.assemblyStageProducts.Length; i++)
            if (a.assemblyStageProducts[i] != b.assemblyStageProducts[i]) return false;
        return true;
    }

    int GetJobBatchLimit(ProductionJob job, KitchenEmployee employee)
    {
        int limit = Mathf.Clamp(employee != null ? employee.CarryCapacity : 1, 1, 4);
        ProductionFlowPlan flow = employee != null ? GetFlowForWorker(employee) : null;
        if (job?.pipeline == null || flow?.stations == null) return limit;

        for (int i = 0; i < flow.stations.Count; i++)
        {
            GameObject stationObject = flow.stations[i];
            if (stationObject == null) continue;
            StationType? type = KitchenEmployee.GetStationTypeFrom(stationObject);
            if (!type.HasValue || System.Array.IndexOf(job.pipeline, type.Value) < 0) continue;

            GrillStation grill = stationObject.GetComponent<GrillStation>();
            if (grill != null)
            {
                limit = Mathf.Min(limit, Mathf.Min(grill.InputSlotCapacity, grill.OutputSlotCapacity));
                continue;
            }
            FryerStation fryer = stationObject.GetComponent<FryerStation>();
            if (fryer != null)
            {
                limit = Mathf.Min(limit, Mathf.Min(fryer.InputSlotCapacity, fryer.OutputSlotCapacity));
                continue;
            }
            CuttingStation cutting = stationObject.GetComponent<CuttingStation>();
            if (cutting != null)
            {
                limit = Mathf.Min(limit, Mathf.Min(cutting.InputSlotCapacity, cutting.OutputSlotCapacity));
                continue;
            }
            AssemblyStation assemblyStation = stationObject.GetComponent<AssemblyStation>();
            if (assemblyStation != null && job.assemblyStageProducts != null)
            {
                for (int stage = 0; stage < job.assemblyStageProducts.Length; stage++)
                {
                    if (!assemblyStation.CanProcess(job.assemblyStageProducts[stage])) continue;
                    int assemblyLimit = assemblyStation.MaxProcessBatch;
                    if (assemblyLimit > 0) limit = Mathf.Min(limit, assemblyLimit);
                    break;
                }
            }
        }

        return Mathf.Clamp(limit, 1, 4);
    }

    int GetAvailableTaskBatchLimit(ProductionJob job, KitchenEmployee employee,
        GameObject source, int requestedUnits)
    {
        if (job == null || employee == null || source == null) return 1;
        int limit = Mathf.Clamp(Mathf.Min(requestedUnits,
            GetJobBatchLimit(job, employee)), 1, 4);
        ItemDefinition item = GetBranchTransferItem(job);

        if (item != null && job.taskPhase == ProductionTaskPhase.CollectOutput)
        {
            IStationBuffer sourceBuffer = source.GetComponent<IStationBuffer>();
            int available = sourceBuffer != null
                ? sourceBuffer.GetOutputCount(item) - GetReservedOutputCount(source, item)
                : 0;
            limit = Mathf.Min(limit, Mathf.Max(1, available));
        }
        else if (item != null && job.CurrentStationType == StationType.Freezer)
        {
            FreezerStation freezerSource = source.GetComponent<FreezerStation>();
            int available = freezerSource != null
                ? freezerSource.GetReadyOutputCount(item) - GetReservedOutputCount(source, item)
                : 0;
            limit = Mathf.Min(limit, Mathf.Max(1, available));
        }
        else if (item != null && job.CurrentStationType == StationType.Pantry)
        {
            PantryStation pantrySource = source.GetComponent<PantryStation>();
            int available = pantrySource != null
                ? pantrySource.GetReadyOutputCount(item) - GetReservedOutputCount(source, item)
                : 0;
            limit = Mathf.Min(limit, Mathf.Max(1, available));
        }

        GameObject destination = GetFlowOutput(employee, source, job);
        IStationBuffer destinationBuffer = destination != null
            ? destination.GetComponent<IStationBuffer>() : null;
        if (destinationBuffer != null && item != null)
        {
            int availableSpace = GetAvailableInputCapacity(
                destinationBuffer, destination, item, job);
            limit = Mathf.Min(limit, Mathf.Max(1, availableSpace));
        }
        return Mathf.Clamp(limit, 1, 4);
    }

    float ScoreTaskForWorker(KitchenEmployee employee, ProductionJob job, GameObject station)
    {
        if (employee == null || job == null || station == null) return float.MinValue;

        GameObject source = job.taskPhase == ProductionTaskPhase.CollectOutput
            ? (job.taskSourceStation != null ? job.taskSourceStation : station)
            : station;
        ItemDefinition item = GetBranchTransferItem(job);
        GameObject destination = GetFlowOutput(employee, source, job);
        HeatLampStation pickup = destination != null
            ? destination.GetComponent<HeatLampStation>() : null;

        // Finished products always leave for pickup before workers service any
        // other part of the flow.
        if (pickup != null)
            return 1000000f - Vector3.Distance(employee.transform.position, source.transform.position);

        // Otherwise fill the emptiest compatible receiver first. For recipes
        // with multiple inputs, emptiness is measured for the exact item this
        // transfer supplies, so a missing side ingredient can still be filled.
        float score = 100000f;
        IStationBuffer receiver = destination != null
            ? destination.GetComponent<IStationBuffer>() : null;
        if (receiver != null && item != null)
        {
            int freeSlots = GetAvailableInputCapacity(receiver, destination, item, job);
            AssemblyStation assembly = destination.GetComponent<AssemblyStation>();
            int itemCapacity = assembly != null
                ? GetAssemblyInputCapacity(assembly, item)
                : receiver.InputSlotCapacity;
            int occupiedSlots = receiver.GetInputCount(item)
                + GetReservedInputCount(destination, item);

            if (freeSlots > 0)
            {
                if (occupiedSlots == 0) score += 10000f;
                score += itemCapacity > 0
                    ? 1000f * freeSlots / itemCapacity
                    : freeSlots * 100f;
            }
        }

        // Use distance only to break ties between equivalent delivery choices.
        score -= Vector3.Distance(employee.transform.position, source.transform.position);
        if (destination != null)
            score -= Vector3.Distance(source.transform.position, destination.transform.position) * 0.1f;
        return score;
    }

    /// <summary>True when this physical station belongs to an unfinished demanded route.</summary>
    public bool HasActiveDemandForStation(GameObject station)
    {
        if (station == null || pendingJobs == null || productionFlows == null) return false;
        if (Time.unscaledTime - activeDemandStationsBuiltAt > Mathf.Max(0.05f, planningInterval))
            RebuildActiveDemandStations();
        return activeDemandStations.Contains(station);
    }

    void RebuildActiveDemandStations()
    {
        activeDemandStations.Clear();
        activeDemandStationsBuiltAt = Time.unscaledTime;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.taskState == ProductionTaskState.Complete
                || job.taskState == ProductionTaskState.Cancelled) continue;
            if (job.taskSourceStation != null) activeDemandStations.Add(job.taskSourceStation);
            if (job.reservedWorkStation != null) activeDemandStations.Add(job.reservedWorkStation);
            if (job.reservedDestinationStation != null)
                activeDemandStations.Add(job.reservedDestinationStation);
            if (job.assemblySupplyTarget != null)
                activeDemandStations.Add(job.assemblySupplyTarget.gameObject);
            if (job.grillSupplyTarget != null)
                activeDemandStations.Add(job.grillSupplyTarget.gameObject);
            if (job.pipeline == null) continue;

            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stations == null) continue;
                foreach (GameObject candidate in flow.stations)
                {
                    if (candidate == null) continue;
                    StationType? candidateType = KitchenEmployee.GetStationTypeFrom(candidate);
                    if (!candidateType.HasValue) continue;
                    for (int i = Mathf.Max(0, job.currentStepIndex); i < job.pipeline.Length; i++)
                    {
                        if (job.pipeline[i] != candidateType.Value) continue;
                        activeDemandStations.Add(candidate);
                        break;
                    }
                }
            }
        }
    }

    int GetLargestCarryCapacityForFlowStation(GameObject station)
    {
        int capacity = 1;
        if (station == null) return capacity;
        foreach (KitchenEmployee employee in employees)
        {
            if (employee == null) continue;
            ProductionFlowPlan flow = GetFlowForWorker(employee);
            if (flow?.stations == null || !flow.stations.Contains(station)) continue;
            capacity = Mathf.Max(capacity, employee.CarryCapacity);
        }
        return capacity;
    }

    static void SetTaskState(ProductionJob job, ProductionTaskState state, string reason = null)
    {
        if (job == null) return;
        if (job.taskState != state) job.stateChangedAt = Time.time;
        job.taskState = state;
        job.blockedReason = reason;
        job.blockedFlow = null;
    }

    public void TouchTask(ProductionJob job, ProductionTaskState state)
    {
        if (job == null) return;
        SetTaskState(job, state);
        // TouchTask is called by the owning worker every update while travelling
        // or operating a station. Keep the claim alive for the whole operation,
        // not only when the worker changes step or inventory. Long recipes could
        // otherwise be reclaimed mid-animation and send their worker back to the
        // flow root even though useful work was still in progress.
        if (job.assignedTo != null)
            job.claimHeartbeatAt = Time.time;
    }

    public void ReportTaskProgress(ProductionJob job)
    {
        if (job != null) job.claimHeartbeatAt = Time.time;
    }

    public bool ValidateActiveClaim(ProductionJob job, KitchenEmployee employee)
    {
        if (job == null || employee == null || job.assignedTo != employee) return false;
        if (job.IsHeatLampStep || job.taskPhase == ProductionTaskPhase.CollectOutput) return true;
        GameObject station = job.reservedWorkStation;
        if (station == null) return job.reservedDestinationStation != null;
        if (!station.activeInHierarchy || !IsStationInWorkerFlow(employee, station)) return false;
        if (KitchenEmployee.GetStationTypeFrom(station) != job.CurrentStationType) return false;
        if (job.CurrentStationType == StationType.Assembly)
        {
            AssemblyStation assemblyStation = station.GetComponent<AssemblyStation>();
            if (assemblyStation == null || !assemblyStation.CanProcess(job.CurrentWorkProduct))
                return false;
        }
        if (job.CurrentStationType == StationType.Pantry && job.reservedPantryIngredient != null)
        {
            PantryStation pantryStation = station.GetComponent<PantryStation>();
            if (pantryStation == null || !pantryStation.CanDispense(job.reservedPantryIngredient))
                return false;
        }
        return true;
    }

    public void DeferTask(ProductionJob job, string reason, GameObject station,
        float delaySeconds = 0.35f)
    {
        if (job == null) return;
        job.retryAfter = Mathf.Max(job.retryAfter, Time.time + Mathf.Max(0.05f, delaySeconds));
        job.blockedStation = station;
        SetTaskState(job, ProductionTaskState.Blocked, reason);
        RecordWorkerWait(job.assignedTo, reason);
        NotifyProductionStateChanged(Mathf.Max(0.05f, delaySeconds));
    }

    /// <summary>Compatibility wrapper for older callers.</summary>
    public bool TryAssignJobTo(KitchenEmployee employee, int minimumPriority = 1,
        bool searchEntireFlow = false)
    {
        return TryAssignHighestPriorityTask(employee);
    }

    ProductionJob TryQueueCompatibleStockJob(KitchenEmployee employee,
        bool searchEntireFlow)
    {
        if (employee == null || orderConfig == null) return null;

        int heldCount = 0;
        int capacity = 0;
        EnsureStationCache();
        HeatLampStation[] lamps = cachedHeatLamps;
        if (lamps == null || lamps.Length == 0) return null;
        foreach (HeatLampStation lamp in lamps)
        {
            if (lamp == null) continue;
            heldCount += lamp.Count;
            capacity += Mathf.Max(0, lamp.maxCapacity);
        }

        // Do not exceed the physical pickup buffer. This fallback only repairs
        // a missing compatible job while the system has an open completion slot.
        if (heldCount + MenuJobCount() >= capacity) return null;

        List<ItemDefinition> cookable = GetCookableMenuItems();
        Dictionary<ItemDefinition, int> stockCounts = CountInFlightByItem();
        ItemDefinition item = PickItemWorkerCanCook(employee, cookable, searchEntireFlow);
        if (item == null) return null;
        if (stockCounts.TryGetValue(item, out int queued) && queued >= capacity) return null;

        int freeSlots = Mathf.Max(1, capacity - heldCount - MenuJobCount());
        ProductionJob probe = CreateJob(CustomerOrder.FromItem(item, 1));
        GameObject source = probe != null
            ? employee.FindStationForJob(probe, searchEntireFlow, true) : null;
        int batch = GetAvailableTaskBatchLimit(probe, employee, source,
            Mathf.Clamp(Mathf.Min(4, freeSlots), 1, 4));
        ProductionJob job = CreateJob(CustomerOrder.FromItem(item, batch));
        if (job == null || !employee.CanTakeJobStep(job, searchEntireFlow)) return null;
        pendingJobs.Add(job);
        return job;
    }

    public void ReleaseJob(ProductionJob job)
    {
        if (job == null) return;
        ReleaseReservations(job);
        job.assignedTo = null;
        if (Time.time >= job.retryAfter)
            SetTaskState(job, ProductionTaskState.WaitingForDependencies);
        NotifyProductionStateChanged();
    }

    /// <summary>
    /// Releases a completed handoff so any available worker can claim the next
    /// station step once its input or output is ready.
    /// </summary>
    public void ReleaseJobAfterHandoff(ProductionJob job, KitchenEmployee employee)
    {
        if (job == null || employee == null || job.assignedTo != employee
            || !employee.IsWorkingOn(job))
            return;

        ReleaseReservations(job);
        job.assignedTo = null;
        job.claimVersion++;
        job.retryAfter = 0f;
        job.blockedStation = null;
        SetTaskState(job, ProductionTaskState.WaitingForDependencies);
        NotifyProductionStateChanged(0f);
    }

    bool HasAssemblyProducer(ItemDefinition product, bool useFlows)
    {
        if (product == null) return false;
        if (useFlows && productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stations == null) continue;
                foreach (GameObject stationObject in flow.stations)
                {
                    AssemblyStation station = stationObject != null
                        ? stationObject.GetComponent<AssemblyStation>() : null;
                    if (station != null && station.CanProcess(product)) return true;
                }
            }
            return false;
        }

        EnsureStationCache();
        foreach (AssemblyStation station in cachedAssemblyStations)
            if (station != null && station.CanProcess(product)) return true;
        return false;
    }

    public bool HasPendingCollectionTask(GameObject source, ItemDefinition item)
    {
        if (source == null || item == null) return false;
        foreach (ProductionJob job in pendingJobs)
            if (job != null && job.taskPhase == ProductionTaskPhase.CollectOutput
                && job.taskSourceStation == source && job.CurrentWorkProduct == item)
                return true;
        return false;
    }

    bool HasFreezerSelection(ItemDefinition ingredient, bool useFlows)
    {
        if (ingredient == null) return false;
        if (useFlows && productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stations == null) continue;
                foreach (GameObject station in flow.stations)
                {
                    FreezerStation freezerStation = station != null ? station.GetComponent<FreezerStation>() : null;
                    if (freezerStation != null && freezerStation.CanSupply(ingredient)) return true;
                }
            }
            return false;
        }
        EnsureStationCache();
        foreach (FreezerStation freezerStation in cachedFreezerStations)
            if (freezerStation != null && freezerStation.CanSupply(ingredient)) return true;
        return false;
    }

    bool HasIngredientSourceSelection(ItemDefinition ingredient, bool useFlows)
    {
        return orderConfig != null && orderConfig.IsFreezerIngredient(ingredient)
            ? HasFreezerSelection(ingredient, useFlows)
            : HasPantrySelection(ingredient, useFlows);
    }

    public bool HasPendingFryerTask(GameObject fryerStation)
    {
        if (fryerStation == null) return false;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.isAssemblySupply || orderConfig == null
                || !orderConfig.IsFries(job.product)
                || job.CurrentStationType != StationType.Fryer)
                continue;
            if (job.taskSourceStation == null || job.taskSourceStation == fryerStation
                || job.reservedWorkStation == fryerStation)
                return true;
        }
        return false;
    }

    public bool HasPendingCuttingTask(GameObject cuttingStation)
    {
        if (cuttingStation == null) return false;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.CurrentStationType != StationType.Cutting)
                continue;
            if (job.taskSourceStation == cuttingStation
                || job.reservedWorkStation == cuttingStation
                || job.assignedTo != null && job.assignedTo.IsWorkingOn(job))
                return true;

            // A queued recipe step without a physical source can collect from
            // any compatible cutter, so let that job own the next output.
            if (job.taskSourceStation == null) return true;
        }
        return false;
    }

    public bool HasPendingGrillTask(GameObject grillStation)
    {
        if (grillStation == null) return false;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.CurrentStationType != StationType.Grill)
                continue;
            if (job.taskSourceStation == grillStation
                || job.reservedWorkStation == grillStation)
                return true;
        }
        return false;
    }

    public bool HasPendingAssemblyWorkTask(GameObject station, ItemDefinition item)
    {
        if (station == null || item == null) return false;
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.taskPhase != ProductionTaskPhase.Work
                || job.CurrentStationType != StationType.Assembly
                || job.CurrentWorkProduct != item)
                continue;
            if (job.taskSourceStation == null || job.taskSourceStation == station
                || job.reservedWorkStation == station)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Registers a recovery job created from real station inventory. This lets an
    /// orphaned intermediate continue through later recipe stages instead of
    /// leaving workers to repeatedly inspect the same full station.
    /// </summary>
    public void QueueRecoveryJob(ProductionJob job)
    {
        if (job == null || pendingJobs.Contains(job)) return;

        // Recovery jobs are rebuilt from station contents during planning. Keep
        // only one queued recovery for the same source, phase, station and item.
        // This also protects against different workers discovering the same WIP.
        if (job.taskSourceStation != null)
        {
            foreach (ProductionJob queued in pendingJobs)
            {
                if (queued == null || queued.taskSourceStation != job.taskSourceStation
                    || queued.taskPhase != job.taskPhase
                    || queued.CurrentStationType != job.CurrentStationType
                    || queued.CurrentWorkProduct != job.CurrentWorkProduct)
                    continue;
                return;
            }
        }

        SetTaskState(job, ProductionTaskState.WaitingForDependencies);
        pendingJobs.Add(job);
        NotifyProductionStateChanged();
    }

    /// <summary>Discard runtime-only production work before rebuilding a saved kitchen.</summary>
    public void ResetTransientProductionState()
    {
        scheduledProduction?.Dispose();
        scheduledProduction = null;
        foreach (ProductionJob job in new List<ProductionJob>(jobsWithReservations))
            ReleaseReservations(job);
        stationWorkReservations.Clear();
        pantryWorkReservations.Clear();
        jobsWithReservations.Clear();
        pendingJobs.Clear();
        orderedHistory.Clear();
        pickupDeliveryHistory.Clear();
        completedFlowHistory.Clear();
        workerTripHistory.Clear();
        workerWaitHistory.Clear();
        throughputTrackingStartedAt = Time.time;
    }

    public void CompleteJob(ProductionJob job)
    {
        if (job == null) return;
        ReleaseReservations(job);
        job.assignedTo = null;
        SetTaskState(job, ProductionTaskState.Complete);
        pendingJobs.Remove(job);
        NotifyProductionStateChanged();
    }

    public bool TryReserveCurrentStation(ProductionJob job, KitchenEmployee employee,
        bool searchEntireFlow = false, GameObject preferredStation = null)
    {
        if (job == null || employee == null || !job.CurrentStationType.HasValue) return false;
        GameObject station = employee.FindStationForJob(
            job, searchEntireFlow, true, preferredStation);
        if (station == null || !IsStationInWorkerFlow(employee, station)) return false;

        string missingHandoffReason = GetMissingRequiredFlowHandoffReason(
            employee, GetFlowForWorker(employee), job, station);
        if (!string.IsNullOrEmpty(missingHandoffReason))
        {
            ProductionFlowPlan flow = GetFlowForWorker(employee);
            SetTaskState(job, ProductionTaskState.WaitingForDependencies, missingHandoffReason);
            job.blockedFlow = flow;
            return false;
        }

        if (job.CurrentStationType.Value == StationType.Freezer && !job.isAssemblySupply)
        {
            GameObject destination = GetFlowOutput(employee, station, job);
            GrillStation grillTarget = destination != null
                ? destination.GetComponent<GrillStation>() : null;
            ItemDefinition secondary = grillTarget != null
                ? grillTarget.GetSelectedSecondaryInput() : null;
            if (secondary != null && grillTarget.GetInputCount(secondary) <= 0)
                return false;
        }
        if (!TryClaimWorkStation(job, station)) return false;
        job.reservedWorkStation = station;
        jobsWithReservations.Add(job);

        // A source worker reserves as many currently open downstream input
        // slots as it can carry before taking physical stock.
        if (job.CurrentStationType.Value == StationType.Freezer)
        {
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition freezerOutput = GetBranchTransferItem(job);
            int requested = job.isAssemblySupply
                ? job.requestedSupplyUnits : employee.CarryCapacity;
            int desiredBatch = Mathf.Clamp(requested, 1, employee.CarryCapacity);
            if (!job.isAssemblySupply)
                desiredBatch = Mathf.Min(desiredBatch, GetJobBatchLimit(job, employee));
            AssemblyStation destinationAssembly = destination != null
                ? destination.GetComponent<AssemblyStation>() : null;
            if (destinationAssembly != null)
                desiredBatch = Mathf.Min(desiredBatch, destinationAssembly.IngredientCapacity);
            bool reserved = destination != null && IsStationInWorkerFlow(employee, destination)
                && freezerOutput != null
                && TryReserveDestination(job, destination, freezerOutput, desiredBatch, employee);
            if (!reserved)
            {
                ReleaseReservations(job);
                return false;
            }
        }
        else if (job.CurrentStationType.Value == StationType.Pantry)
        {
            // Pantry output is picked up immediately. Claim its actual downstream
            // buffer first so a worker never ends up idling with undeliverable stock.
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition pantryOutput = GetBranchTransferItem(job);
            int requested = job.isAssemblySupply
                ? job.requestedSupplyUnits : employee.CarryCapacity;
            int desiredBatch = Mathf.Clamp(requested, 1, employee.CarryCapacity);
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && pantryOutput != null;
            bool destinationReady = validDestination
                && TryReserveDestination(job, destination, pantryOutput, desiredBatch, employee);
            if (!validDestination || !destinationReady)
            {
                ReleaseReservations(job);
                return false;
            }
        }
        else if (job.CurrentStationType.Value == StationType.Cutting)
        {
            // Cutting creates physical output immediately after processing. Reserve
            // its downstream buffer before allowing the task to start so another
            // worker cannot slice stock that has nowhere to go.
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition cuttingOutput = GetBranchTransferItem(job);
            int requested = job.isAssemblySupply
                ? (job.heldUnits > 0 ? job.heldUnits : job.requestedSupplyUnits)
                : employee.CarryCapacity;
            int desiredBatch = Mathf.Clamp(requested, 1, employee.CarryCapacity);
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && cuttingOutput != null;
            bool destinationReady = validDestination
                && TryReserveDestination(job, destination, cuttingOutput, desiredBatch, employee);
            if (!validDestination || !destinationReady)
            {
                ReleaseReservations(job);
                return false;
            }
        }
        else if (job.CurrentStationType.Value == StationType.Grill
            || job.CurrentStationType.Value == StationType.Fryer
            || job.CurrentStationType.Value == StationType.Assembly
            || job.CurrentStationType.Value == StationType.Drink)
        {
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition output = GetBranchTransferItem(job);
            int desiredBatch = GetProcessOutputBatch(job, employee, station);
            bool finalProduct = !GetRequiredNextStationType(job).HasValue;
            AssemblyStation finalAssembly = finalProduct
                ? station.GetComponent<AssemblyStation>() : null;
            bool canBufferFinalAssemblyOutput = finalAssembly != null
                && finalAssembly.CanStoreOutput(desiredBatch);
            bool canHoldFinalDrinkOutput = finalProduct
                && !HasWaitingCustomerForItem(output)
                && station.GetComponent<DrinkStation>() != null;
            if (canHoldFinalDrinkOutput)
                return true;

            // Every processing station must prove that at least one complete
            // output can leave the station before work begins. The reservation
            // includes current station stock and claims made by other workers.
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && output != null;
            bool destinationReserved = validDestination
                && TryReserveDestination(job, destination, output, desiredBatch, employee);
            if (!destinationReserved && canBufferFinalAssemblyOutput
                && job.taskPhase == ProductionTaskPhase.Work)
                return true;
            if (!destinationReserved)
            {
                ReleaseReservations(job);
                return false;
            }
        }
        return true;
    }

    int GetProcessOutputBatch(ProductionJob job, KitchenEmployee employee, GameObject station)
    {
        int capacity = Mathf.Max(1, employee != null ? employee.CarryCapacity : 1);
        if (station == null || job == null) return 1;
        int requestedUnits;
        if (job.isAssemblySupply)
            requestedUnits = Mathf.Max(1, job.requestedSupplyUnits);
        else if (GetRequiredNextStationType(job).HasValue)
            // Intermediate stock should fill the receiver's currently open
            // input slots. TryReserveDestination clamps this to the exact free
            // room, including inputs already buffered at that station.
            requestedUnits = capacity;
        else
            requestedUnits = Mathf.Max(1, job.order != null
                ? job.order.CountQuantityOf(job.product) : job.heldUnits);
        GrillStation grillStation = station.GetComponent<GrillStation>();
        if (grillStation != null)
            return Mathf.Min(requestedUnits,
                Mathf.Min(capacity, grillStation.OutputSlotCapacity));
        FryerStation fryerStation = station.GetComponent<FryerStation>();
        if (fryerStation != null)
            return Mathf.Min(requestedUnits,
                Mathf.Min(capacity, fryerStation.OutputSlotCapacity));
        AssemblyStation assemblyStation = station.GetComponent<AssemblyStation>();
        if (assemblyStation != null)
        {
            return Mathf.Clamp(requestedUnits, 1,
                Mathf.Min(capacity, assemblyStation.MaxProcessBatch));
        }
        if (station.GetComponent<DrinkStation>() != null)
            return Mathf.Min(capacity, requestedUnits);
        return 1;
    }

    public bool CanClaimWorkStation(ProductionJob job, GameObject station)
    {
        if (job == null || station == null) return false;
        if (job.CurrentStationType == StationType.Pantry)
        {
            ItemDefinition ingredient = job.isAssemblySupply
                ? GetAssemblySupplySource(job) : GetPantryItemForProduct(job.product);
            PantryStation pantry = station.GetComponent<PantryStation>();
            if (ingredient == null || pantry == null || !pantry.CanDispense(ingredient)) return false;
            var key = (station, ingredient);
            if (pantryWorkReservations.TryGetValue(key, out ProductionJob pantryOwner)
                && pantryOwner != null && pantryOwner != job)
            {
                if (pantryOwner.reservedWorkStation != station
                    || pantryOwner.reservedPantryIngredient != ingredient
                    || pantryOwner.assignedTo == null)
                    pantryWorkReservations.Remove(key);
                else
                    return false;
            }
            return true;
        }

        if (stationWorkReservations.TryGetValue(station, out ProductionJob owner)
            && owner != null && owner != job)
        {
            // A reservation dictionary entry is only authoritative while its job
            // still points at this station and has an owner. Self-heal leaked
            // entries so one released worker cannot permanently block every
            // other worker assigned to the same flow.
            if (owner.reservedWorkStation != station || owner.assignedTo == null)
                stationWorkReservations.Remove(station);
            else
                return false;
        }
        return true;
    }

    bool TryClaimWorkStation(ProductionJob job, GameObject station)
    {
        if (!CanClaimWorkStation(job, station)) return false;
        if (job.CurrentStationType == StationType.Pantry)
        {
            ItemDefinition ingredient = job.isAssemblySupply
                ? GetAssemblySupplySource(job) : GetPantryItemForProduct(job.product);
            pantryWorkReservations[(station, ingredient)] = job;
            // Remember the claimed item even if the Pantry selection changes during work.
            job.reservedPantryIngredient = ingredient;
            return true;
        }

        stationWorkReservations[station] = job;
        return true;
    }

    public void ReleaseWorkReservation(ProductionJob job)
    {
        if (job == null || job.reservedWorkStation == null) return;
        if (job.reservedPantryIngredient != null)
        {
            var key = (job.reservedWorkStation, job.reservedPantryIngredient);
            if (pantryWorkReservations.TryGetValue(key, out ProductionJob pantryOwner) && pantryOwner == job)
                pantryWorkReservations.Remove(key);
            job.reservedPantryIngredient = null;
        }
        if (stationWorkReservations.TryGetValue(job.reservedWorkStation, out ProductionJob owner)
            && owner == job)
            stationWorkReservations.Remove(job.reservedWorkStation);
        job.reservedWorkStation = null;
        RemoveReservationTrackingIfEmpty(job);
    }

    public bool TryReserveTransfer(ProductionJob job, GameObject source, GameObject destination,
        ItemDefinition item, int requestedUnits, KitchenEmployee worker = null)
    {
        if (job == null || source == null || destination == null || item == null || requestedUnits <= 0)
            return false;
        worker ??= job.assignedTo;
        if (!IsValidFlowHandoff(worker, GetFlowForWorker(worker), source, destination, item))
            return false;

        IStationBuffer sourceBuffer = source.GetComponent<IStationBuffer>();
        IStationBuffer destinationBuffer = destination.GetComponent<IStationBuffer>();
        if (sourceBuffer == null || destinationBuffer == null) return false;

        int outputAvailable = sourceBuffer.GetOutputCount(item) - ReservedOutputCount(source, item, job);
        int destinationAvailable = GetAvailableInputCapacity(destinationBuffer, destination, item, job);
        int units = Mathf.Min(requestedUnits, outputAvailable, destinationAvailable);
        if (units <= 0 || !destinationBuffer.CanAcceptInput(item, units)) return false;

        ReleaseTransferReservations(job);
        job.SetTransferReservation(source, destination, item, units, units);
        jobsWithReservations.Add(job);
        return true;
    }

    public bool CanTransferAvailable(ProductionJob job, GameObject source, GameObject destination,
        ItemDefinition item, int requestedUnits = 1, KitchenEmployee worker = null)
    {
        if (source == null || destination == null || item == null || requestedUnits <= 0)
            return false;
        worker ??= job != null ? job.assignedTo : null;
        if (worker != null
            && !IsValidFlowHandoff(worker, GetFlowForWorker(worker), source, destination, item))
            return false;

        IStationBuffer sourceBuffer = source.GetComponent<IStationBuffer>();
        IStationBuffer destinationBuffer = destination.GetComponent<IStationBuffer>();
        if (sourceBuffer == null || destinationBuffer == null) return false;

        int outputAvailable = sourceBuffer.GetOutputCount(item)
            - ReservedOutputCount(source, item, job);
        int destinationAvailable = GetAvailableInputCapacity(
            destinationBuffer, destination, item, job);
        int units = Mathf.Min(requestedUnits, outputAvailable, destinationAvailable);
        return units > 0 && destinationBuffer.CanAcceptInput(item, units);
    }

    public bool TryReserveDestination(ProductionJob job, GameObject destination,
        ItemDefinition item, int requestedUnits, KitchenEmployee worker = null)
    {
        if (job == null || destination == null || item == null || requestedUnits <= 0)
            return false;
        worker ??= job.assignedTo;
        GameObject source = job.reservedWorkStation ?? job.taskSourceStation ?? job.reservedSourceStation;
        if (!IsValidFlowHandoff(worker, GetFlowForWorker(worker), source, destination, item))
            return false;

        IStationBuffer destinationBuffer = destination.GetComponent<IStationBuffer>();
        if (destinationBuffer == null) return false;

        int available = GetAvailableInputCapacity(destinationBuffer, destination, item, job);
        int units = Mathf.Min(requestedUnits, available);
        if (units <= 0 || !destinationBuffer.CanAcceptInput(item, units)) return false;

        bool sameSource = job.reservedSourceStation == source && job.reservedItem == item;
        int outputUnits = sameSource ? job.reservedOutputUnits : 0;
        job.SetTransferReservation(source, destination, item, outputUnits, units);
        jobsWithReservations.Add(job);
        return true;
    }

    public int GetReservedTransferUnits(ProductionJob job) =>
        job != null ? Mathf.Min(job.reservedOutputUnits, job.reservedInputUnits) : 0;

    public int GetReservedInputUnits(ProductionJob job) =>
        job != null ? job.reservedInputUnits : 0;

    public int GetReservedInputCount(GameObject station, ItemDefinition item = null)
    {
        if (station == null) return 0;
        int total = 0;
        foreach (ProductionJob job in jobsWithReservations)
            if (job != null && job.reservedDestinationStation == station
                && (item == null || job.reservedItem == item))
                total += job.reservedInputUnits;
        return total;
    }

    public int GetReservedOutputCount(GameObject station, ItemDefinition item = null)
    {
        if (station == null) return 0;
        int total = 0;
        foreach (ProductionJob job in jobsWithReservations)
            if (job != null && job.reservedSourceStation == station
                && (item == null || job.reservedItem == item))
                total += job.reservedOutputUnits;
        return total;
    }

    public void ConsumeOutputReservation(ProductionJob job, int amount)
    {
        if (job == null || amount <= 0 || job.activeTransfer == null) return;
        job.activeTransfer.ConsumeOutput(amount);
        RemoveReservationTrackingIfEmpty(job);
        NotifyProductionStateChanged();
    }

    public void ConsumeInputReservation(ProductionJob job, int amount)
    {
        if (job == null || amount <= 0 || job.activeTransfer == null) return;
        job.activeTransfer.ConsumeInput(amount);
        RemoveReservationTrackingIfEmpty(job);
        NotifyProductionStateChanged();
    }

    int ReservedOutputCount(GameObject station, ItemDefinition item, ProductionJob except)
    {
        int total = 0;
        foreach (ProductionJob job in jobsWithReservations)
            if (job != null && job != except && job.reservedSourceStation == station
                && job.reservedItem == item)
                total += job.reservedOutputUnits;
        return total;
    }

    int ReservedInputCount(GameObject station, ItemDefinition item, ProductionJob except)
    {
        int total = 0;
        foreach (ProductionJob job in jobsWithReservations)
            if (job != null && job != except && job.reservedDestinationStation == station
                && job.reservedItem == item)
                total += job.reservedInputUnits;
        return total;
    }

    int GetAvailableInputCapacity(IStationBuffer buffer, GameObject station, ItemDefinition item,
        ProductionJob except)
    {
        int occupied;
        HeatLampStation lamp = station.GetComponent<HeatLampStation>();
        if (lamp != null)
            return Mathf.Max(0, buffer.InputSlotCapacity - lamp.Count
                - ReservedInputCount(station, null, except));
        else if (station.GetComponent<GrillStation>() is GrillStation grillStation)
        {
            if (ReservedInputCount(station, item, except) > 0) return 0;
            occupied = grillStation.GetInputCount(item);
        }
        else if (station.GetComponent<FryerStation>() is FryerStation fryerStation)
        {
            if (ReservedInputCount(station, item, except) > 0) return 0;
            occupied = fryerStation.GetInputCount(item);
        }
        else if (station.GetComponent<CuttingStation>() is CuttingStation cuttingStation)
            occupied = cuttingStation.GetInputCount(item);
        else if (station.GetComponent<AssemblyStation>() is AssemblyStation assembly)
            return Mathf.Max(0, GetAssemblyInputCapacity(assembly, item) - buffer.GetInputCount(item)
                - ReservedInputCount(station, item, except));
        else
            occupied = buffer.GetInputCount(item);
        return Mathf.Max(0, buffer.InputSlotCapacity - occupied
            - ReservedInputCount(station, item, except));
    }

    static int GetAssemblyInputCapacity(AssemblyStation assembly, ItemDefinition item)
    {
        if (assembly == null || item == null) return 0;
        AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
        if (recipe == null) return 0;
        if (item == recipe.pantryInput) return assembly.PantryInputCapacity;
        if (item == recipe.thirdInput) return assembly.ThirdInputCapacity;
        if (recipe.processedInput == null || item == recipe.processedInput)
            return assembly.ProcessedInputCapacity;
        return 0;
    }

    public void ReleaseTransferReservations(ProductionJob job)
    {
        if (job == null) return;
        job.ClearTransferReservation();
        RemoveReservationTrackingIfEmpty(job);
    }

    void ReleaseReservations(ProductionJob job)
    {
        if (job == null) return;
        ReleaseWorkReservation(job);
        ReleaseTransferReservations(job);
        jobsWithReservations.Remove(job);
    }

    void RemoveReservationTrackingIfEmpty(ProductionJob job)
    {
        if (job != null && job.reservedWorkStation == null
            && job.reservedOutputUnits <= 0 && job.reservedInputUnits <= 0)
            jobsWithReservations.Remove(job);
    }

    public void RegisterEmployee(KitchenEmployee emp)
    {
        if (emp != null && !employees.Contains(emp))
            employees.Add(emp);
        NotifyProductionStateChanged();
    }

    public void UnregisterEmployee(KitchenEmployee emp)
    {
        employees.Remove(emp);
        if (productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
                if (flow != null && flow.workers != null)
                    flow.workers.Remove(emp);
        }
        NotifyProductionStateChanged();
    }

    public Vector3 GetHeatLampPosition(HeatLampStation lamp = null)
    {
        var l = lamp != null ? lamp : heatLamp;
        return l != null ? l.GetInteractionPosition() : transform.position;
    }

    public bool IsEmployeeOnHeatLampTile(Vector3 worldPosition, HeatLampStation lamp = null)
    {
        var l = lamp != null ? lamp : heatLamp;
        if (l == null) return true;
        var tiles = l.GetComponent<StationInteractionTiles>();
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool DeliverToHeatLamp(CustomerOrder order, HeatLampStation lamp = null)
    {
        var l = lamp != null ? lamp : heatLamp;
        ItemDefinition item = order != null ? order.PrimaryItem : null;
        if (item == null) return false;
        IStationBuffer buffer = l;
        return buffer != null
            && buffer.StoreInput(item, 1, CustomerOrder.FromItem(item, 1)) == 1;
    }

    public Vector3 GetFreezerPosition(KitchenEmployee forEmployee = null)
    {
        if (forEmployee != null)
        {
            var fs = forEmployee.GetFreezerStation();
            if (fs != null) return fs.GetInteractionPosition();
        }
        return freezer != null ? freezer.GetInteractionPosition() : transform.position;
    }

    public GrillStation GetGrillFor(KitchenEmployee emp)
    {
        if (emp != null)
        {
            // GetGrillStation resolves the active job's reserved physical station
            // first. The legacy targetGrill field can point at a different grill
            // in a shared flow, which is fatal for recipe-specific work such as
            // Cut Bacon -> Cooked Bacon.
            var gs = emp.GetGrillStation();
            if (gs != null) return gs;
            if (emp.targetGrill != null && emp.IsAssignedTo(emp.targetGrill.gameObject))
                return emp.targetGrill;
        }
        return grill;
    }

    public Vector3 GetGrillPosition(KitchenEmployee forEmployee = null)
    {
        var g = GetGrillFor(forEmployee);
        return g != null ? g.GetInteractionPosition() : transform.position;
    }

    public Vector3 GetAssemblyPosition(KitchenEmployee forEmployee = null)
    {
        if (forEmployee != null)
        {
            var ast = forEmployee.GetAssemblyStation(forEmployee.CurrentWorkProduct);
            if (ast != null) return ast.GetInteractionPosition();
        }
        return assembly != null ? assembly.GetInteractionPosition() : transform.position;
    }

    public Vector3 GetFryerPosition(KitchenEmployee forEmployee = null)
    {
        if (forEmployee != null)
        {
            var f = forEmployee.GetFryerStation();
            if (f != null) return f.GetInteractionPosition();
        }
        return fryer != null ? fryer.GetInteractionPosition() : transform.position;
    }

    public Vector3 GetDrinkStationPosition(KitchenEmployee forEmployee = null)
    {
        if (forEmployee != null)
        {
            var d = forEmployee.GetDrinkStation();
            if (d != null) return d.GetInteractionPosition();
        }
        return drinkStation != null ? drinkStation.GetInteractionPosition() : transform.position;
    }

    public bool IsEmployeeOnFreezerTile(Vector3 worldPosition, KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null) f = freezer;
        var tiles = f != null ? f.GetComponent<StationInteractionTiles>() : null;
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool IsEmployeeOnGrillTile(Vector3 worldPosition, KitchenEmployee forEmployee = null)
    {
        var g = GetGrillFor(forEmployee);
        var tiles = g != null ? g.GetComponent<StationInteractionTiles>() : null;
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool IsEmployeeOnAssemblyTile(Vector3 worldPosition, KitchenEmployee forEmployee = null)
    {
        var a = forEmployee != null ? forEmployee.GetAssemblyStation(forEmployee.CurrentWorkProduct) : null;
        if (a == null) a = assembly;
        var tiles = a != null ? a.GetComponent<StationInteractionTiles>() : null;
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool IsEmployeeOnFryerTile(Vector3 worldPosition, KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFryerStation() : null;
        if (f == null) f = fryer;
        var tiles = f != null ? f.GetComponent<StationInteractionTiles>() : null;
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool IsEmployeeOnDrinkTile(Vector3 worldPosition, KitchenEmployee forEmployee = null)
    {
        var d = forEmployee != null ? forEmployee.GetDrinkStation() : null;
        if (d == null) d = drinkStation;
        var tiles = d != null ? d.GetComponent<StationInteractionTiles>() : null;
        return tiles == null || tiles.IsEmployeeOnInteractionTile(worldPosition);
    }

    public bool PlacePattyOnGrill(KitchenEmployee forEmployee = null)
    {
        return PlacePattiesOnGrill(forEmployee, PattyItem, 1) == 1;
    }

    public int PlacePattiesOnGrill(KitchenEmployee forEmployee, ItemDefinition item, int amount)
    {
        GrillStation grillStation = GetGrillFor(forEmployee);
        if (grillStation == null || !grillStation.CanProcess(item)) return 0;
        IStationBuffer buffer = grillStation;
        ItemDefinition input = grillStation != null ? grillStation.GetSelectedInput() : item;
        return buffer != null ? buffer.StoreInput(input, amount) : 0;
    }

    public bool TakePattyFromGrill(KitchenEmployee forEmployee = null)
    {
        return TakePattiesFromGrill(forEmployee, PattyItem, 1) == 1;
    }

    public int TakePattiesFromGrill(KitchenEmployee forEmployee, ItemDefinition item, int amount)
    {
        GrillStation grillStation = GetGrillFor(forEmployee);
        if (grillStation == null || !grillStation.CanProcess(item)) return 0;
        IStationBuffer buffer = grillStation;
        ItemDefinition output = grillStation.GetSelectedOutput();
        return buffer != null ? buffer.TakeOutput(output, amount) : 0;
    }

    public float GetFreezerProcessTime(KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null) f = freezer;
        return f != null ? f.processTimeSeconds : 1f;
    }

    public bool TryTakePattyFromFreezer(KitchenEmployee forEmployee = null)
    {
        return TryTakePattiesFromFreezer(forEmployee, PattyItem, 1) == 1;
    }

    public int TryTakePattiesFromFreezer(KitchenEmployee forEmployee, ItemDefinition item, int amount)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null) f = freezer;
        IStationBuffer buffer = f;
        ItemDefinition freezerOutput = f != null && f.selectedItem != null
            ? f.selectedItem
            : (orderConfig != null ? orderConfig.rawPattyIngredient : item);
        return buffer != null && freezerOutput != null
            ? buffer.TakeOutput(freezerOutput, amount)
            : 0;
    }

    public bool HasPattyInStock(KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null)
        {
            EnsureStationCache();
            f = freezer != null ? freezer
                : (cachedFreezerStations.Length > 0 ? cachedFreezerStations[0] : null);
        }
        IStationBuffer buffer = f;
        ItemDefinition freezerOutput = f != null && f.selectedItem != null
            ? f.selectedItem
            : (orderConfig != null ? orderConfig.rawPattyIngredient : null);
        return freezerOutput != null && buffer != null
            && f.GetReadyOutputCount(freezerOutput) > 0;
    }

    public float GetGrillProcessTime(KitchenEmployee forEmployee = null)
    {
        var g = GetGrillFor(forEmployee);
        return g != null ? g.EffectiveProcessTimeSeconds : 4f;
    }

    public float GetAssemblyProcessTime(KitchenEmployee forEmployee = null)
    {
        var a = forEmployee != null ? forEmployee.GetAssemblyStation(forEmployee.CurrentWorkProduct) : null;
        if (a == null) a = assembly;
        return a != null ? a.EffectiveProcessTimeSeconds : 1f;
    }

    public FryerStation GetFryerFor(KitchenEmployee emp)
    {
        if (emp != null)
        {
            var f = emp.GetFryerStation();
            if (f != null) return f;
        }
        return fryer;
    }

    public DrinkStation GetDrinkFor(KitchenEmployee emp)
    {
        if (emp != null)
        {
            var d = emp.GetDrinkStation();
            if (d != null) return d;
        }
        return drinkStation;
    }

    public float GetFryerProcessTime(KitchenEmployee forEmployee = null)
    {
        var f = GetFryerFor(forEmployee);
        return f != null ? f.EffectiveProcessTimeSeconds : 7.5f;
    }

    public bool TryLoadFryer(KitchenEmployee forEmployee = null)
    {
        return TryLoadFryer(forEmployee, 1) == 1;
    }

    public int TryLoadFryer(KitchenEmployee forEmployee, int amount, CustomerOrder sourceOrder = null)
    {
        var f = GetFryerFor(forEmployee);
        if (f == null || SlicedPotatoItem == null) return 0;
        return f.TryLoad(SlicedPotatoItem, amount, sourceOrder);
    }
    public int TryLoadFryer(KitchenEmployee forEmployee, ItemDefinition input, int amount,
        CustomerOrder sourceOrder = null)
    {
        var f = GetFryerFor(forEmployee);
        return f != null && input != null ? f.TryLoad(input, amount, sourceOrder) : 0;
    }

    public bool TakeFromFryer(KitchenEmployee forEmployee = null)
    {
        return TakeFromFryer(forEmployee, 1) == 1;
    }

    public int TakeFromFryer(KitchenEmployee forEmployee, int amount)
    {
        var f = GetFryerFor(forEmployee);
        return f != null ? f.TakeCooked(amount) : 0;
    }
    public int TakeFromFryer(KitchenEmployee forEmployee, ItemDefinition output, int amount)
    {
        var f = GetFryerFor(forEmployee);
        return f != null && output != null ? f.TakeOutput(output, amount) : 0;
    }

    public float GetDrinkProcessTime(KitchenEmployee forEmployee = null)
    {
        var d = GetDrinkFor(forEmployee);
        return d != null ? StationNode.GetEffectiveCycleSeconds(d, d.processTimeSeconds) : 7.5f;
    }

    public bool TryDispenseDrink(KitchenEmployee forEmployee = null)
    {
        var d = GetDrinkFor(forEmployee);
        if (d == null || DrinkItem == null) return false;
        return d.TryDispense(DrinkItem);
    }

    public bool HasDrinkInStock()
    {
        if (DrinkItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(DrinkItem);
    }

    public bool HasFriesInStock()
    {
        if (PotatoItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(PotatoItem);
    }
}
