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
    [System.NonSerialized] public GameObject taskSourceStation;
    public KitchenEmployee assignedTo;
    public bool hasPatty;
    /// <summary>Batch size carried between stations (upgraded workers hold more).</summary>
    public int heldUnits;
    public readonly List<ItemDefinition> ingredientsHeld = new List<ItemDefinition>();
    /// <summary>Parallel Pantry task that stocks an Assembly Station with its second input.</summary>
    public bool isAssemblySupply;
    public AssemblyStation assemblySupplyTarget;
    /// <summary>The pantry item collected for this exact assembly input.</summary>
    public ItemDefinition assemblySupplySourceItem;
    /// <summary>The item delivered to the assembly station after optional processing.</summary>
    public ItemDefinition assemblySupplyOutputItem;
    public ItemDefinition[] assemblySupplyStageItems;
    public int requestedSupplyUnits;
    /// <summary>Heat lamp this job must deliver to (from the last station's Assign Output link).</summary>
    public HeatLampStation deliveryHeatLamp;
    /// <summary>Runtime-only claims that prevent two workers targeting the same station inventory.</summary>
    [System.NonSerialized] public GameObject reservedWorkStation;
    [System.NonSerialized] public ItemDefinition reservedPantryIngredient;
    [System.NonSerialized] public GameObject reservedSourceStation;
    [System.NonSerialized] public GameObject reservedDestinationStation;
    [System.NonSerialized] public ItemDefinition reservedItem;
    [System.NonSerialized] public int reservedOutputUnits;
    [System.NonSerialized] public int reservedInputUnits;
    [System.NonSerialized] public ProductionTaskState taskState;
    [System.NonSerialized] public float createdAt;
    [System.NonSerialized] public float stateChangedAt;
    [System.NonSerialized] public float claimHeartbeatAt;
    [System.NonSerialized] public int claimVersion;
    [System.NonSerialized] public string blockedReason;
    [System.NonSerialized] public float retryAfter;
    [System.NonSerialized] public GameObject blockedStation;
    public string TaskKey => taskId + ":" + currentStepIndex + ":" + taskPhase;

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
/// Fast-food production: one job per menu item (burger / fries / drink).
/// Work order: Burger = Freezer → Grill → Assembly; Fries = Pantry → Cutting → Fryer → Assembly.
/// After each station, the worker delivers only to that station's Assign Output
/// (e.g. Assembly/Fryer → Heat Lamp). Drinks are cashier-served.
/// </summary>
public class ProductionManager : MonoBehaviour
{
    [System.Serializable]
    public class ProductionTarget
    {
        public ItemDefinition item;
        [Min(0)] public int quantity = 2;
    }

    public static ProductionManager Instance { get; private set; }

    [Header("Order config")]
    public CustomerOrderConfig orderConfig;
    [Header("Ready-stock targets")]
    public List<ProductionTarget> productionTargets = new List<ProductionTarget>();

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
    readonly Dictionary<GameObject, ProductionJob> stationWorkReservations =
        new Dictionary<GameObject, ProductionJob>();
    readonly Dictionary<(GameObject station, ItemDefinition ingredient), ProductionJob> pantryWorkReservations =
        new Dictionary<(GameObject station, ItemDefinition ingredient), ProductionJob>();
    readonly HashSet<ProductionJob> jobsWithReservations = new HashSet<ProductionJob>();
    readonly Dictionary<(KitchenEmployee worker, GameObject station), (int frame, float distance)>
        routeDistanceCache = new Dictionary<(KitchenEmployee, GameObject), (int, float)>();
    readonly HashSet<GameObject> activeDemandStations = new HashSet<GameObject>();
    float activeDemandStationsBuiltAt = -999f;
    sealed class TimedItemCount
    {
        public ItemDefinition item;
        public int count;
        public float time;
    }
    readonly List<TimedItemCount> orderedHistory = new List<TimedItemCount>();
    readonly List<TimedItemCount> completedHistory = new List<TimedItemCount>();
    sealed class TimedFlowOutput
    {
        public ProductionFlowPlan flow;
        public ItemDefinition item;
        public int count;
        public float time;
    }
    readonly List<TimedFlowOutput> completedFlowHistory = new List<TimedFlowOutput>();
    const float ThroughputWindowSeconds = 60f;
    MoneyManager moneyManager;
    [Header("Production scheduling")]
    [Tooltip("How often the manager rescans stations and plans new work. Active workers still update every frame.")]
    [Min(0.05f)] public float planningInterval = 0.15f;
    [Tooltip("How long an abandoned claim may survive without an active worker.")]
    [Min(3f)] public float abandonedClaimTimeout = 30f;
    float nextPlanningTime;

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
    CustomerAI[] cachedCustomers = System.Array.Empty<CustomerAI>();
    bool stationCacheInitialized;

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

    public int GetProductionTarget(ItemDefinition item)
    {
        if (item == null) return 0;
        foreach (ProductionTarget entry in productionTargets)
            if (entry != null && entry.item == item)
                return Mathf.Max(0, entry.quantity);
        return 2;
    }
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

    public void SetProductionTarget(ItemDefinition item, int quantity)
    {
        if (item == null) return;
        quantity = Mathf.Clamp(quantity, 0, 20);
        foreach (ProductionTarget entry in productionTargets)
            if (entry != null && entry.item == item)
            {
                entry.quantity = quantity;
                RequestImmediateProduction();
                return;
            }
        productionTargets.Add(new ProductionTarget { item = item, quantity = quantity });
        RequestImmediateProduction();
    }

    /// <summary>Per-item demand vs ready stock / cooking — used by the Customers management tab.</summary>
    public struct ItemOutputNeed
    {
        public ItemDefinition item;
        public int requested;
        public int ready;
        public int cooking;
        /// <summary>Exact number of this item ordered during the trailing simulation minute.</summary>
        public int orderedLastMinute;
        /// <summary>Exact number delivered to pickup stations during the trailing simulation minute.</summary>
        public int completedLastMinute;
        /// <summary>Kitchen units still short of live orders (absolute).</summary>
        public int requiredOutput;
        /// <summary>Target production rate (items per real minute) to meet demand.</summary>
        public float requiredPerMinute;
    }

    public List<ItemOutputNeed> GetRequiredOutputByItem(bool includeDrinks = true)
    {
        EnsureStationCache();
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
            int units = Mathf.Max(1, job.heldUnits);
            cooking[item] = cooking.TryGetValue(item, out int c) ? c + units : units;
        }

        PruneThroughputHistory();
        var orderedLastMinute = SumHistory(orderedHistory);
        var completedLastMinute = SumHistory(completedHistory);

        var items = new HashSet<ItemDefinition>();
        foreach (var kv in requested) items.Add(kv.Key);
        foreach (var kv in ready) items.Add(kv.Key);
        foreach (var kv in cooking) items.Add(kv.Key);
        foreach (var kv in orderedLastMinute) items.Add(kv.Key);
        foreach (var kv in completedLastMinute) items.Add(kv.Key);
        foreach (var item in GetCookableMenuItems())
            if (item != null) items.Add(item);

        var list = new List<ItemOutputNeed>(items.Count);
        foreach (var item in items)
        {
            if (item == null) continue;
            requested.TryGetValue(item, out int req);
            ready.TryGetValue(item, out int readyCount);
            cooking.TryGetValue(item, out int cookingCount);
            int shortfall = Mathf.Max(0, req - readyCount - cookingCount);

            orderedLastMinute.TryGetValue(item, out int orderedRate);
            completedLastMinute.TryGetValue(item, out int completedRate);

            list.Add(new ItemOutputNeed
            {
                item = item,
                requested = req,
                ready = readyCount,
                cooking = cookingCount,
                orderedLastMinute = orderedRate,
                completedLastMinute = completedRate,
                requiredOutput = shortfall,
                requiredPerMinute = orderedRate
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

    public void RecordCustomerOrder(CustomerOrder order)
    {
        RecordHistory(orderedHistory, order);
    }

    public void RecordCompletedOutput(CustomerOrder order)
    {
        RecordHistory(completedHistory, order);
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
    }

    public float GetFlowCompletedOutputPerMinute(ProductionFlowPlan flow)
    {
        if (flow == null) return 0f;
        PruneThroughputHistory();
        int total = 0;
        foreach (TimedFlowOutput entry in completedFlowHistory)
            if (entry != null && entry.flow == flow)
                total += entry.count;
        return total;
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
        completedHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
        completedFlowHistory.RemoveAll(entry => entry == null || entry.time < cutoff);
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
        List<GameObject> outgoing = flow.GetOutgoing(station);
        outgoing.RemoveAll(candidate => candidate == null || !flow.stations.Contains(candidate));
        if (outgoing.Count == 0)
        {
            // A Pickup Station is the terminal sink of a production flow. Players
            // commonly add it as a separate start node because it has no recipe
            // input selector. Final products must still be able to leave the last
            // work station, while intermediate items continue to require explicit
            // directed links.
            bool finalProduct = job != null
                ? !GetRequiredNextStationType(job).HasValue
                : producedItem != null && orderConfig != null
                    && orderConfig.GetProductKind(producedItem) != CustomerOrderConfig.ProductKind.None;
            if (finalProduct)
            {
                GameObject nearestPickup = null;
                float nearestDistance = float.MaxValue;
                foreach (GameObject candidate in flow.stations)
                {
                    HeatLampStation pickup = candidate != null
                        ? candidate.GetComponent<HeatLampStation>() : null;
                    if (pickup == null || !pickup.isActiveAndEnabled) continue;
                    float distance = Vector3.SqrMagnitude(
                        candidate.transform.position - station.transform.position);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearestPickup = candidate;
                }
                return nearestPickup;
            }
            return null;
        }

        job ??= worker != null ? worker.ActiveJob : null;
        if (job == null)
        {
            if (producedItem == null) return outgoing[0];
            GameObject itemTarget = null;
            float itemTargetDistance = float.MaxValue;
            foreach (GameObject candidate in outgoing)
            {
                if (!CanBranchAcceptItem(candidate, producedItem)) continue;
                float distance = Vector3.SqrMagnitude(candidate.transform.position - station.transform.position);
                if (distance < itemTargetDistance)
                {
                    itemTargetDistance = distance;
                    itemTarget = candidate;
                }
            }
            return itemTarget;
        }
        GameObject requiredTarget = GetRequiredBranchTarget(job, station);
        if (requiredTarget != null && flow.stations.Contains(requiredTarget)
            && CanBranchAcceptItem(requiredTarget, GetBranchTransferItem(job)))
            return requiredTarget;

        StationType? requiredType = GetRequiredNextStationType(job);
        ItemDefinition requiredAssemblyProduct = GetRequiredNextAssemblyProduct(job);
        GameObject best = null;
        float bestScore = float.MaxValue;
        foreach (GameObject candidate in outgoing)
        {
            if (candidate == null) continue;
            StationType? candidateType = KitchenEmployee.GetStationTypeFrom(candidate);
            bool pickup = candidate.GetComponent<HeatLampStation>() != null;
            if (requiredType.HasValue)
            {
                if (candidateType != requiredType) continue;
            }
            else if (!pickup)
            {
                continue;
            }

            if (requiredType == StationType.Assembly && requiredAssemblyProduct != null)
            {
                AssemblyStation assembly = candidate.GetComponent<AssemblyStation>();
                if (assembly == null || !assembly.CanProcess(requiredAssemblyProduct)) continue;
            }

            float score = Vector3.SqrMagnitude(candidate.transform.position - station.transform.position);
            IStationBuffer buffer = candidate.GetComponent<IStationBuffer>();
            ItemDefinition transferItem = GetBranchTransferItem(job);
            if (buffer != null && transferItem != null && !buffer.CanAcceptInput(transferItem, 1))
                score += 100000f;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // A flow can contain converging branches whose visual edge does not directly
        // connect every intermediate processing station. Keep the recipe sequence
        // authoritative and use any compatible station in this flow as a fallback.
        if (best == null && requiredType.HasValue)
        {
            ItemDefinition transferItem = GetBranchTransferItem(job);
            foreach (GameObject candidate in flow.stations)
            {
                if (candidate == null || candidate == station
                    || KitchenEmployee.GetStationTypeFrom(candidate) != requiredType)
                    continue;
                if (requiredType == StationType.Assembly && requiredAssemblyProduct != null)
                {
                    AssemblyStation assembly = candidate.GetComponent<AssemblyStation>();
                    if (assembly == null || !assembly.CanProcess(requiredAssemblyProduct)) continue;
                }
                if (!CanBranchAcceptItem(candidate, transferItem)) continue;

                float score = Vector3.SqrMagnitude(
                    candidate.transform.position - station.transform.position);
                IStationBuffer buffer = candidate.GetComponent<IStationBuffer>();
                if (buffer != null && transferItem != null
                    && !buffer.CanAcceptInput(transferItem, 1))
                    score += 100000f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    GameObject GetRequiredBranchTarget(ProductionJob job, GameObject source)
    {
        if (job == null || !job.isAssemblySupply || job.assemblySupplyTarget == null) return null;
        // Only force the configured Assembly Station on the final supply step.
        // Multi-stage ingredients can continue after Cutting, for example
        // Bacon Slab -> Cutting -> Grill -> Assembly.
        if (job.currentStepIndex >= job.pipeline.Length - 1)
            return job.assemblySupplyTarget.gameObject;
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
        if (job.isAssemblySupply && job.assemblySupplyTarget != null)
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
            return orderConfig.cookedPattyIngredient;
        return job.CurrentWorkProduct ?? job.product;
    }

    bool CanBranchAcceptItem(GameObject target, ItemDefinition item)
    {
        if (target == null || item == null || orderConfig == null) return false;
        if (target.GetComponent<HeatLampStation>() != null)
            return orderConfig.IsBurger(item) || orderConfig.IsFries(item) || orderConfig.IsDrink(item);
        GrillStation grill = target.GetComponent<GrillStation>();
        if (grill != null) return item == grill.GetSelectedInput();
        CuttingStation cutting = target.GetComponent<CuttingStation>();
        if (cutting != null) return cutting.CanProcess(item);
        FryerStation fryer = target.GetComponent<FryerStation>();
        if (fryer != null)
        {
            StationProcessingRecipeDefinition fryerRecipe = orderConfig.GetFryerRecipe(fryer.GetSelectedOutput());
            return fryerRecipe != null && item == fryerRecipe.input;
        }
        AssemblyStation assembly = target.GetComponent<AssemblyStation>();
        if (assembly == null) return false;
        AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
        if (recipe == null) return false;
        ItemDefinition processed = recipe.processedInput != null
            ? recipe.processedInput : orderConfig.cookedPattyIngredient;
        return item == processed || item == recipe.pantryInput
            || (assembly.IsMk2 && item == recipe.thirdInput);
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
        if (!CanAddWorkerToFlow(targetFlow))
            return;
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
    public const int ExtraHiresAtMilestone2 = 1;

    /// <summary>Up to 3 workers before Milestone 2; one extra hire when Milestone 2 is reached.</summary>
    public int MaxHiredWorkers => StartingHireCap
        + (MilestoneFeatures.ExtraStaffingUnlocked ? ExtraHiresAtMilestone2 : 0);

    public bool CanHireWorker()
    {
        if (employeePrefab == null) return false;
        return HiredWorkerCount < MaxHiredWorkers;
    }

    public bool ExtraHireLocked => HiredWorkerCount >= StartingHireCap && !MilestoneFeatures.ExtraStaffingUnlocked;

    public bool CanAddWorkerToFlow(ProductionFlowPlan targetFlow)
    {
        if (targetFlow == null) return false;
        targetFlow.Clean();
        if (targetFlow.workers.Count < 1) return true;
        return MilestoneFeatures.ExtraFlowStaffingUnlocked;
    }

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
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (Time.timeScale <= 0f)
            return;
        if (Time.unscaledTime < nextPlanningTime)
            return;
        nextPlanningTime = Time.unscaledTime + Mathf.Max(0.25f, planningInterval);
        RefreshStations();
        RecoverOrphanedJobAssignments();
        CollectProductionJobs();
        EnsureAssemblySupplyJobs();
        AssignJobsToEmployees();
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
        int requestBatch = Mathf.Min(target - reserved, GetLargestCarryCapacityForStation(station));
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
                count++;
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
    }

    void RefreshStations()
    {
        RefreshStationCache();
        registers.RemoveAll(r => r == null || !r.gameObject.activeInHierarchy);
        foreach (Register placed in FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (placed != null && !registers.Contains(placed))
                registers.Add(placed);
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
        if (!stationCacheInitialized)
            RefreshStationCache();
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
        cachedCustomers = FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        stationCacheInitialized = true;
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

        // Keep assigned cooks cycling: if someone is idle and can cook, raise the
        // production target so they immediately start another flow loop.
        if (openSlots <= 0) return;

        // Prefer matching pending jobs to the cooks who can run them.
        while (openSlots > 0)
        {
            ItemDefinition item = null;
            int greatestDeficit = 0;
            foreach (ItemDefinition candidate in cookable)
            {
                if (candidate == null) continue;
                int current = stockCounts.TryGetValue(candidate, out int count) ? count : 0;
                int deficit = GetProductionTarget(candidate) - current;
                if (deficit <= greatestDeficit) continue;
                item = candidate;
                greatestDeficit = deficit;
            }
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
                    var single = CustomerOrder.FromItem(line.item, 1);
                    int matchIdx = FindMatchingIndex(available, single);
                    if (matchIdx >= 0)
                    {
                        available.RemoveAt(matchIdx);
                        continue;
                    }
                    var job = CreateJob(single);
                    if (job == null) continue;
                    pendingJobs.Add(job);
                    available.Add(single.Clone());
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
                if (burger != null && orderConfig.IsBurger(burger) && orderConfig.IsItemEnabled(burger)
                    && HasAssemblyChainAvailable(burger, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(burger, hasConfiguredFlow)
                    && HasRequiredCuttingSupplyAvailable(burger, hasConfiguredFlow))
                    list.Add(burger);
            }
        }
        if (canFries && HasPantrySelection(PotatoItem, hasConfiguredFlow)
            && HasCuttingSupplyPath(PotatoItem, SlicedPotatoItem, hasConfiguredFlow))
        {
            foreach (ItemDefinition item in orderConfig.GetEnabledMenuItems())
                if (orderConfig.IsFries(item) && HasAssemblyChainAvailable(item, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(item, hasConfiguredFlow)
                    && HasRequiredCuttingSupplyAvailable(item, hasConfiguredFlow))
                    list.Add(item);
        }
        if (canShake)
        {
            foreach (ItemDefinition item in orderConfig.GetEnabledMenuItems())
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
        lines.Add("Fries ready + in progress: " + friesCount + "/" + GetProductionTarget(FriesItem));
        if (readyMeals + queuedMeals >= pickupCapacity)
            lines.Add("New meals paused: all pickup slots are filled or claimed by queued meals");
        else if (FriesItem != null && !GetCookableMenuItems().Contains(FriesItem))
            lines.Add("Fries recipe unavailable: check source selections and directed ingredient connections");
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
        if (flow?.stations == null || orderConfig == null || FriesItem == null) return string.Empty;
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
        return string.Empty;
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
            counts[item] = counts.TryGetValue(item, out int c) ? c + 1 : 1;
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
        foreach (CustomerAI customer in cachedCustomers)
        {
            if (customer == null || customer.IsLeaving) continue;
            CustomerOrder order = customer.GetOrder();
            if (order != null && order.GetTotalQuantity() > 0)
                list.Add(order);
        }
        return list;
    }

    static int FindMatchingIndex(List<CustomerOrder> pool, CustomerOrder needed)
    {
        if (pool == null || needed == null) return -1;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && needed.Matches(pool[i]))
                return i;
        }
        return -1;
    }

    void AssignJobsToEmployees()
    {
        foreach (var e in employees)
        {
            if (e == null) continue;
            e.QueueRecoverableFlowTasks();
        }

        // Match the best worker/task pair globally. Repeating after each atomic
        // claim lets reservations immediately influence the next assignment.
        while (TryAssignBestGlobalPair()) { }
    }

    bool TryAssignBestGlobalPair()
    {
        KitchenEmployee bestEmployee = null;
        ProductionJob bestJob = null;
        GameObject bestStation = null;
        float bestScore = float.NegativeInfinity;

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
                GameObject station = employee.FindStationForJob(job, true, true);
                if (station == null || !flow.stations.Contains(station)) continue;
                float score = ScoreTaskForWorker(employee, job, station);
                if (score <= bestScore) continue;
                bestScore = score;
                bestEmployee = employee;
                bestJob = job;
                bestStation = station;
            }
        }

        if (bestEmployee == null || bestJob == null || bestStation == null) return false;
        return TryClaimAndAssign(bestEmployee, bestJob);
    }

    /// <summary>
    /// Scores every runnable task in the worker's flow. Preference is a bonus, not
    /// a hard filter, so a worker never idles while useful non-preferred work exists.
    /// </summary>
    public bool TryAssignHighestPriorityTask(KitchenEmployee employee)
    {
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

    sealed class ScheduledCandidate
    {
        public ProductionJob job;
        public GameObject station;
        public float score;
    }

    bool TryAssignBestRunnableFlowStep(KitchenEmployee employee, ProductionFlowPlan flow)
    {
        var candidates = new List<ScheduledCandidate>();
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.assignedTo != null || Time.time < job.retryAfter) continue;
            GameObject station = employee.FindStationForJob(job, true, true);
            if (station == null || !flow.stations.Contains(station))
            {
                SetTaskState(job, ProductionTaskState.WaitingForDependencies,
                    "Required input, station, or output capacity is unavailable");
                continue;
            }

            SetTaskState(job, ProductionTaskState.Runnable);
            candidates.Add(new ScheduledCandidate
            {
                job = job,
                station = station,
                score = ScoreTaskForWorker(employee, job, station)
            });
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (ScheduledCandidate candidate in candidates)
        {
            if (TryClaimAndAssign(employee, candidate.job)) return true;
        }
        return false;
    }

    bool TryClaimAndAssign(KitchenEmployee employee, ProductionJob job)
    {
        if (employee == null || job == null || employee.HasJob || job.assignedTo != null
            || !TryReserveCurrentStation(job, employee, true)) return false;
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

    float ScoreTaskForWorker(KitchenEmployee employee, ProductionJob job, GameObject station)
    {
        int stepCount = job.pipeline != null ? Mathf.Max(1, job.pipeline.Length) : 1;
        float progress = Mathf.Clamp01((float)job.currentStepIndex / stepCount);
        float score = progress * 120f;

        // Completing downstream work frees upstream buffers and moves the final
        // product closer to pickup, so it outranks speculative ingredient work.
        if (job.taskPhase == ProductionTaskPhase.CollectOutput) score += 130f;
        if (job.currentStepIndex >= stepCount - 1) score += 90f;
        if (!job.isAssemblySupply) score += 35f;
        if (job.CurrentStationType == StationType.Assembly) score += 45f;
        if (employee.PrefersJob(job)) score += 160f;

        // A burger base is a hard dependency for every later assembly stage.
        // Once the first assembly has its side ingredient but no processed patty,
        // prioritize the main Freezer -> Grill branch over further buffer filling.
        if (IsBurgerBaseBlockingAssembly(employee, job)) score += 400f;

        // Starved assembly inputs are the useful exception to downstream-first.
        // Supplying an empty input unblocks an already selected final recipe.
        if (job.isAssemblySupply && job.assemblySupplyTarget != null)
        {
            ItemDefinition supplied = GetAssemblySupplyOutput(job);
            IStationBuffer buffer = job.assemblySupplyTarget.GetComponent<IStationBuffer>();
            if (buffer != null && supplied != null && buffer.GetInputCount(supplied) == 0)
                score += 110f;
        }

        score += Mathf.Min(45f, Mathf.Max(0f, Time.time - job.createdAt) * 2f);
        score += CountTasksBlockedBy(station) * 55f;

        // Compare the worker's predicted arrival with the time at which this
        // station will starve or block. Lateness dominates proximity and task
        // preference, while an on-time worker still benefits from a short route.
        GameObject serviceStation = ResolveTaskServiceStation(employee, job, station);
        float completionSeconds = EstimateTaskCompletionSeconds(employee, job, station, serviceStation);
        float deadlineSeconds = GetStationServiceDeadlineSeconds(job, serviceStation);
        float slackSeconds = deadlineSeconds - completionSeconds;
        score += GetStationContinuityBonus(job, serviceStation);
        if (deadlineSeconds < 999f)
        {
            // Missing a service deadline makes the task more urgent, not less.
            // The completion-time penalty below still selects the worker who can
            // reach the same urgent task soonest.
            if (slackSeconds >= 0f)
                score += 260f + Mathf.Clamp(120f - slackSeconds * 18f, 0f, 120f);
            else
                score += 520f;
        }
        score -= completionSeconds * 8f;

        int nearbyWorkers = 0;
        foreach (KitchenEmployee other in employees)
            if (other != null && other != employee && other.HasJob
                && Vector3.SqrMagnitude(other.transform.position - station.transform.position) < 9f)
                nearbyWorkers++;
        score -= nearbyWorkers * 18f;
        return score;
    }

    /// <summary>
    /// Prioritizes the atomic task that keeps a demanded flow station cycling.
    /// This remains a score rather than a hard lock, so blocked outputs and final
    /// product delivery can still outrank unnecessary input filling.
    /// </summary>
    float GetStationContinuityBonus(ProductionJob job, GameObject serviceStation)
    {
        if (job == null || serviceStation == null || !HasActiveDemandForStation(serviceStation))
            return 0f;

        StationRuntimeMetrics metrics = serviceStation.GetComponent<StationRuntimeMetrics>();
        if (metrics == null) return 0f;

        switch (metrics.CurrentState)
        {
            case StationRuntimeState.Starved:
                return job.taskPhase == ProductionTaskPhase.Work ? 480f : 0f;
            case StationRuntimeState.Blocked:
                return job.taskPhase == ProductionTaskPhase.CollectOutput ? 480f : 0f;
            case StationRuntimeState.Working:
                return 180f;
            case StationRuntimeState.Idle:
                return job.taskPhase == ProductionTaskPhase.Work ? 300f : 0f;
            default:
                return 0f;
        }
    }

    GameObject ResolveTaskServiceStation(KitchenEmployee employee, ProductionJob job,
        GameObject workStation)
    {
        if (job == null || workStation == null) return workStation;
        if (job.taskPhase == ProductionTaskPhase.CollectOutput) return workStation;
        StationType? type = KitchenEmployee.GetStationTypeFrom(workStation);
        if (type == StationType.Freezer || type == StationType.Pantry)
            return GetFlowOutput(employee, workStation, job) ?? workStation;
        return workStation;
    }

    float EstimateTaskCompletionSeconds(KitchenEmployee employee, ProductionJob job,
        GameObject workStation, GameObject serviceStation)
    {
        if (employee == null || workStation == null) return 999f;
        float speed = Mathf.Max(0.1f, employee.moveSpeed);
        float seconds = GetRouteDistance(employee, workStation) / speed + 0.45f;
        if (serviceStation != null && serviceStation != workStation)
        {
            Vector3 delta = serviceStation.transform.position - workStation.transform.position;
            float gridDistance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
            seconds += gridDistance / speed + 0.45f;
        }

        // Include station cycle time when this atomic task must transform an
        // ingredient before the threatened destination receives it.
        if (job != null && job.taskPhase == ProductionTaskPhase.Work)
        {
            GrillStation grill = workStation.GetComponent<GrillStation>();
            FryerStation fryer = workStation.GetComponent<FryerStation>();
            CuttingStation cutting = workStation.GetComponent<CuttingStation>();
            AssemblyStation assembly = workStation.GetComponent<AssemblyStation>();
            if (grill != null) seconds += grill.processTimeSeconds;
            else if (fryer != null) seconds += fryer.processTimeSeconds;
            else if (cutting != null) seconds += cutting.processTimeSeconds;
            else if (assembly != null) seconds += assembly.processTimeSeconds;
        }
        return seconds;
    }

    float GetStationServiceDeadlineSeconds(ProductionJob job, GameObject serviceStation)
    {
        if (job == null || serviceStation == null) return 999f;

        StationRuntimeMetrics metrics = serviceStation.GetComponent<StationRuntimeMetrics>();
        if (metrics != null && (metrics.CurrentState == StationRuntimeState.Blocked
            || metrics.CurrentState == StationRuntimeState.Starved))
            return 0f;

        GrillStation grill = serviceStation.GetComponent<GrillStation>();
        if (grill != null)
            return grill.IsCookingPatty ? grill.ProcessRemainingSeconds : 0f;
        FryerStation fryer = serviceStation.GetComponent<FryerStation>();
        if (fryer != null)
            return fryer.IsCooking ? fryer.ProcessRemainingSeconds : 0f;
        CuttingStation cutting = serviceStation.GetComponent<CuttingStation>();
        if (cutting != null)
            return cutting.IsProcessing ? cutting.ProcessRemainingSeconds : 0f;
        AssemblyStation assembly = serviceStation.GetComponent<AssemblyStation>();
        if (assembly != null)
            return assembly.IsProcessing ? assembly.ProcessRemainingSeconds : 0f;

        return job.taskPhase == ProductionTaskPhase.CollectOutput ? 0f : 999f;
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

    int GetLargestCarryCapacityForStation(AssemblyStation station)
    {
        int capacity = 1;
        if (station == null) return capacity;
        foreach (KitchenEmployee employee in employees)
        {
            if (employee == null) continue;
            ProductionFlowPlan flow = GetFlowForWorker(employee);
            if (flow?.stations == null || !flow.stations.Contains(station.gameObject)) continue;
            capacity = Mathf.Max(capacity, employee.CarryCapacity);
        }
        return capacity;
    }

    bool IsBurgerBaseBlockingAssembly(KitchenEmployee employee, ProductionJob job)
    {
        if (employee == null || job == null || job.isAssemblySupply || orderConfig == null
            || !orderConfig.IsBurger(job.product)
            || (job.CurrentStationType != StationType.Freezer
                && job.CurrentStationType != StationType.Grill))
            return false;

        ProductionFlowPlan flow = GetFlowForWorker(employee);
        if (flow?.stations == null || job.assemblyStageProducts == null
            || job.assemblyStageProducts.Length == 0)
            return false;

        ItemDefinition firstAssemblyProduct = job.assemblyStageProducts[0];
        foreach (GameObject stationObject in flow.stations)
        {
            AssemblyStation station = stationObject != null
                ? stationObject.GetComponent<AssemblyStation>() : null;
            if (station == null || !station.CanProcess(firstAssemblyProduct)) continue;
            AssemblyRecipeDefinition recipe = station.GetSelectedRecipe();
            if (recipe == null || recipe.processedInput == null) continue;
            bool missingPatty = station.GetInputCount(recipe.processedInput) <= 0;
            bool otherInputWaiting = recipe.pantryInput == null
                || station.GetInputCount(recipe.pantryInput) > 0;
            if (missingPatty && otherInputWaiting) return true;
        }
        return false;
    }

    float GetRouteDistance(KitchenEmployee employee, GameObject station)
    {
        var key = (employee, station);
        if (routeDistanceCache.TryGetValue(key, out var cached) && cached.frame == Time.frameCount)
            return cached.distance;
        float distance = employee.EstimateRouteDistance(station);
        routeDistanceCache[key] = (Time.frameCount, distance);
        if (routeDistanceCache.Count > 256) routeDistanceCache.Clear();
        return distance;
    }

    int CountTasksBlockedBy(GameObject station)
    {
        if (station == null) return 0;
        int count = 0;
        foreach (ProductionJob job in pendingJobs)
            if (job != null && job.assignedTo == null && job.blockedStation == station
                && Time.time < job.retryAfter)
                count++;
        return count;
    }

    static void SetTaskState(ProductionJob job, ProductionTaskState state, string reason = null)
    {
        if (job == null) return;
        if (job.taskState != state) job.stateChangedAt = Time.time;
        job.taskState = state;
        job.blockedReason = reason;
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

        // Do not overproduce. This fallback only repairs a missing compatible job while the
        // configured ready-stock target still has an open slot.
        if (heldCount + MenuJobCount() >= capacity) return null;

        List<ItemDefinition> cookable = GetCookableMenuItems();
        Dictionary<ItemDefinition, int> stockCounts = CountInFlightByItem();
        List<ItemDefinition> needed = new List<ItemDefinition>();
        foreach (ItemDefinition candidate in cookable)
        {
            if (candidate == null) continue;
            int current = stockCounts.TryGetValue(candidate, out int count) ? count : 0;
            if (current < GetProductionTarget(candidate)) needed.Add(candidate);
        }
        ItemDefinition item = PickItemWorkerCanCook(employee, needed, searchEntireFlow);
        if (item == null) return null;

        ProductionJob job = CreateJob(CustomerOrder.FromItem(item, 1));
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
    }

    /// <summary>
    /// Atomically moves an owned job from its completed station step to its next
    /// station. The job must never become publicly claimable between releasing
    /// the old station and reserving the new one, otherwise another worker can
    /// claim it while the original worker still holds it locally.
    /// </summary>
    public bool TryContinueJobAtNextStation(ProductionJob job, KitchenEmployee employee)
    {
        if (job == null || employee == null || job.assignedTo != employee
            || !employee.IsWorkingOn(job))
            return false;

        ReleaseReservations(job);
        if (!TryReserveCurrentStation(job, employee, true))
        {
            job.assignedTo = null;
            SetTaskState(job, ProductionTaskState.WaitingForDependencies);
            return false;
        }

        job.claimHeartbeatAt = Time.time;
        job.retryAfter = 0f;
        job.blockedStation = null;
        SetTaskState(job, ProductionTaskState.Claimed);
        return true;
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
        if (job != null && !pendingJobs.Contains(job))
        {
            SetTaskState(job, ProductionTaskState.WaitingForDependencies);
            pendingJobs.Add(job);
        }
    }

    /// <summary>Discard runtime-only production work before rebuilding a saved kitchen.</summary>
    public void ResetTransientProductionState()
    {
        foreach (ProductionJob job in new List<ProductionJob>(jobsWithReservations))
            ReleaseReservations(job);
        stationWorkReservations.Clear();
        pantryWorkReservations.Clear();
        jobsWithReservations.Clear();
        pendingJobs.Clear();
        orderedHistory.Clear();
        completedHistory.Clear();
        completedFlowHistory.Clear();
    }

    public void CompleteJob(ProductionJob job)
    {
        if (job == null) return;
        ReleaseReservations(job);
        job.assignedTo = null;
        SetTaskState(job, ProductionTaskState.Complete);
        pendingJobs.Remove(job);
    }

    public bool TryReserveCurrentStation(ProductionJob job, KitchenEmployee employee,
        bool searchEntireFlow = false)
    {
        if (job == null || employee == null || !job.CurrentStationType.HasValue) return false;
        GameObject station = employee.FindStationForJob(job, searchEntireFlow, true);
        if (station == null || !IsStationInWorkerFlow(employee, station)) return false;
        if (!TryClaimWorkStation(job, station)) return false;
        job.reservedWorkStation = station;
        jobsWithReservations.Add(job);

        // A source worker must claim the next buffer before taking physical stock.
        // Reserving one slot is sufficient to make the single-buffer Grill exclusive;
        // the claim is expanded to the worker's actual batch during handoff.
        if (job.CurrentStationType.Value == StationType.Freezer)
        {
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition freezerOutput = GetBranchTransferItem(job);
            int desiredBatch = Mathf.Clamp(employee.CarryCapacity, 1, 4);
            AssemblyStation destinationAssembly = destination != null
                ? destination.GetComponent<AssemblyStation>() : null;
            if (destinationAssembly != null)
                desiredBatch = Mathf.Min(desiredBatch, destinationAssembly.IngredientCapacity);
            if (destination == null || !IsStationInWorkerFlow(employee, destination) || freezerOutput == null
                || !TryReserveDestination(job, destination, freezerOutput, desiredBatch))
            {
                ReleaseWorkReservation(job);
                return false;
            }
        }
        else if (job.CurrentStationType.Value == StationType.Pantry)
        {
            // Pantry output is picked up immediately. Claim its actual downstream
            // buffer first so a worker never ends up idling with undeliverable stock.
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition pantryOutput = GetBranchTransferItem(job);
            int desiredBatch = Mathf.Clamp(
                job.requestedSupplyUnits > 0 ? job.requestedSupplyUnits : employee.CarryCapacity,
                1, employee.CarryCapacity);
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && pantryOutput != null;
            bool destinationReady = validDestination
                && TryReserveDestination(job, destination, pantryOutput, desiredBatch);
            if (!validDestination || !destinationReady)
            {
                ReleaseWorkReservation(job);
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
            int desiredBatch = Mathf.Clamp(
                job.heldUnits > 0 ? job.heldUnits : employee.CarryCapacity,
                1, employee.CarryCapacity);
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && cuttingOutput != null;
            bool destinationReady = validDestination
                && TryReserveDestination(job, destination, cuttingOutput, desiredBatch);
            if (!validDestination || !destinationReady)
            {
                ReleaseWorkReservation(job);
                return false;
            }
        }
        else if (job.CurrentStationType.Value == StationType.Grill
            || job.CurrentStationType.Value == StationType.Fryer
            || job.CurrentStationType.Value == StationType.Assembly
            || job.CurrentStationType.Value == StationType.Drink)
        {
            // Every processing station must prove that at least one complete
            // output can leave the station before work begins. The reservation
            // includes current station stock and claims made by other workers.
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition output = GetBranchTransferItem(job);
            int desiredBatch = GetProcessOutputBatch(job, employee, station);
            bool validDestination = destination != null
                && IsStationInWorkerFlow(employee, destination)
                && output != null;
            if (!validDestination
                || !TryReserveDestination(job, destination, output, desiredBatch))
            {
                ReleaseWorkReservation(job);
                return false;
            }
        }
        return true;
    }

    int GetProcessOutputBatch(ProductionJob job, KitchenEmployee employee, GameObject station)
    {
        int capacity = Mathf.Max(1, employee != null ? employee.CarryCapacity : 1);
        if (station == null || job == null) return 1;
        GrillStation grillStation = station.GetComponent<GrillStation>();
        if (grillStation != null)
            return Mathf.Clamp(grillStation.GetOutputCount(grillStation.GetSelectedOutput()), 1, capacity);
        FryerStation fryerStation = station.GetComponent<FryerStation>();
        if (fryerStation != null)
            return Mathf.Clamp(fryerStation.GetOutputCount(fryerStation.GetSelectedOutput()), 1, capacity);
        AssemblyStation assemblyStation = station.GetComponent<AssemblyStation>();
        if (assemblyStation != null)
        {
            int requested = job.heldUnits > 0 ? job.heldUnits : 1;
            return Mathf.Clamp(requested, 1,
                Mathf.Min(capacity, assemblyStation.MaxProcessBatch));
        }
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
        ItemDefinition item, int requestedUnits)
    {
        if (job == null || source == null || destination == null || item == null || requestedUnits <= 0)
            return false;

        IStationBuffer sourceBuffer = source.GetComponent<IStationBuffer>();
        IStationBuffer destinationBuffer = destination.GetComponent<IStationBuffer>();
        if (sourceBuffer == null || destinationBuffer == null) return false;

        int outputAvailable = sourceBuffer.GetOutputCount(item) - ReservedOutputCount(source, item, job);
        int destinationAvailable = GetAvailableInputCapacity(destinationBuffer, destination, item, job);
        int units = Mathf.Min(requestedUnits, outputAvailable, destinationAvailable);
        if (units <= 0 || !destinationBuffer.CanAcceptInput(item, units)) return false;

        ReleaseTransferReservations(job);
        job.reservedSourceStation = source;
        job.reservedDestinationStation = destination;
        job.reservedItem = item;
        job.reservedOutputUnits = units;
        job.reservedInputUnits = units;
        jobsWithReservations.Add(job);
        return true;
    }

    public bool CanTransferAvailable(ProductionJob job, GameObject source, GameObject destination,
        ItemDefinition item, int requestedUnits = 1)
    {
        if (source == null || destination == null || item == null || requestedUnits <= 0)
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
        ItemDefinition item, int requestedUnits)
    {
        if (job == null || destination == null || item == null || requestedUnits <= 0)
            return false;
        IStationBuffer destinationBuffer = destination.GetComponent<IStationBuffer>();
        if (destinationBuffer == null) return false;

        int available = GetAvailableInputCapacity(destinationBuffer, destination, item, job);
        int units = Mathf.Min(requestedUnits, available);
        if (units <= 0 || !destinationBuffer.CanAcceptInput(item, units)) return false;

        job.reservedDestinationStation = destination;
        job.reservedItem = item;
        job.reservedInputUnits = units;
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
        if (job == null || amount <= 0) return;
        job.reservedOutputUnits = Mathf.Max(0, job.reservedOutputUnits - amount);
        if (job.reservedOutputUnits == 0) job.reservedSourceStation = null;
        RemoveReservationTrackingIfEmpty(job);
    }

    public void ConsumeInputReservation(ProductionJob job, int amount)
    {
        if (job == null || amount <= 0) return;
        job.reservedInputUnits = Mathf.Max(0, job.reservedInputUnits - amount);
        if (job.reservedInputUnits == 0) job.reservedDestinationStation = null;
        RemoveReservationTrackingIfEmpty(job);
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
            occupied = lamp.Count;
        else if (station.GetComponent<GrillStation>() is GrillStation grillStation)
        {
            if (ReservedInputCount(station, item, except) > 0) return 0;
            occupied = grillStation.BufferedPattyCount;
        }
        else if (station.GetComponent<FryerStation>() is FryerStation fryerStation)
        {
            if (ReservedInputCount(station, item, except) > 0) return 0;
            occupied = fryerStation.GetInputCount(item);
        }
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
        job.reservedSourceStation = null;
        job.reservedDestinationStation = null;
        job.reservedItem = null;
        job.reservedOutputUnits = 0;
        job.reservedInputUnits = 0;
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
        if (job != null && job.reservedWorkStation == null && job.reservedSourceStation == null
            && job.reservedDestinationStation == null)
            jobsWithReservations.Remove(job);
    }

    public void RegisterEmployee(KitchenEmployee emp)
    {
        if (emp != null && !employees.Contains(emp))
            employees.Add(emp);
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
        IStationBuffer buffer = l;
        return buffer != null && buffer.StoreInput(item, 1, order) == 1;
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
            && buffer.GetOutputCount(freezerOutput) > 0;
    }

    public float GetGrillProcessTime(KitchenEmployee forEmployee = null)
    {
        var g = GetGrillFor(forEmployee);
        return g != null ? g.processTimeSeconds : 4f;
    }

    public float GetAssemblyProcessTime(KitchenEmployee forEmployee = null)
    {
        var a = forEmployee != null ? forEmployee.GetAssemblyStation(forEmployee.CurrentWorkProduct) : null;
        if (a == null) a = assembly;
        return a != null ? a.processTimeSeconds : 1f;
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
        return f != null ? f.processTimeSeconds : 7.5f;
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
        return d != null ? d.processTimeSeconds : 7.5f;
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
