using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Station types workers can be assigned to.
/// Production pipelines use Freezer/Grill/Pantry/Assembly (burger),
/// Pantry/Cutting/Fryer (fries), and Drink (drink).
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

public class ProductionJob
{
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
    public int requestedSupplyUnits;
    /// <summary>Heat lamp this job must deliver to (from the last station's Assign Output link).</summary>
    public HeatLampStation deliveryHeatLamp;
    /// <summary>Runtime-only claims that prevent two workers targeting the same station inventory.</summary>
    [System.NonSerialized] public GameObject reservedWorkStation;
    [System.NonSerialized] public GameObject reservedSourceStation;
    [System.NonSerialized] public GameObject reservedDestinationStation;
    [System.NonSerialized] public ItemDefinition reservedItem;
    [System.NonSerialized] public int reservedOutputUnits;
    [System.NonSerialized] public int reservedInputUnits;

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

    public ProductionJob(CustomerOrder o, StationType[] steps, IList<ItemDefinition> assemblyStages = null)
    {
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
    }
}

/// <summary>
/// Fast-food production: one job per menu item (burger / fries / drink).
/// Work order: Burger = Freezer → Grill → Assembly; Fries = Pantry → Cutting → Fryer.
/// After each station, the worker delivers only to that station's Assign Output
/// (e.g. Assembly/Fryer → Heat Lamp). Drinks are cashier-served.
/// </summary>
public class ProductionManager : MonoBehaviour
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
    readonly Dictionary<GameObject, ProductionJob> stationWorkReservations =
        new Dictionary<GameObject, ProductionJob>();
    readonly HashSet<ProductionJob> jobsWithReservations = new HashSet<ProductionJob>();
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
    float nextPlanningTime;

    FreezerStation freezer;
    GrillStation grill;
    AssemblyStation assembly;
    FryerStation fryer;
    DrinkStation drinkStation;

    public ItemDefinition PattyItem => orderConfig != null ? orderConfig.burgerBase : null;
    public ItemDefinition FriesItem => orderConfig != null ? orderConfig.friesItem : null;
    public ItemDefinition PotatoItem => orderConfig != null
        ? (orderConfig.friesIngredient != null ? orderConfig.friesIngredient : orderConfig.friesItem)
        : null;
    public ItemDefinition SlicedPotatoItem => orderConfig != null
        ? (orderConfig.slicedPotatoIngredient != null
            ? orderConfig.slicedPotatoIngredient : PotatoItem)
        : null;
    public AssemblyRecipeDefinition GetAssemblyRecipe(ItemDefinition product) =>
        orderConfig != null ? orderConfig.GetAssemblyRecipe(product) : null;
    public ItemDefinition GetPantryItemForProduct(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetAssemblyRecipe(product);
        if (recipe != null && recipe.pantryInput != null) return recipe.pantryInput;
        return orderConfig != null && orderConfig.IsFries(product) ? PotatoItem : null;
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
        foreach (HeatLampStation lamp in FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
        bool anyBurgerEnabled = (orderConfig.burgerBase != null && orderConfig.IsItemEnabled(orderConfig.burgerBase))
            || (orderConfig.cheeseburgerItem != null && orderConfig.IsItemEnabled(orderConfig.cheeseburgerItem));
        float b = anyBurgerEnabled ? Mathf.Clamp01(orderConfig.burgerChance) : 0f;
        float f = orderConfig.friesItem != null && orderConfig.IsItemEnabled(orderConfig.friesItem) ? Mathf.Clamp01(orderConfig.friesChance) : 0f;
        float d = orderConfig.drinkItem != null && orderConfig.IsItemEnabled(orderConfig.drinkItem) ? Mathf.Clamp01(orderConfig.drinkChance) : 0f;
        float none = (1f - b) * (1f - f) * (1f - d);

        float burgerP = b;
        float friesP = f;
        float drinkP = d;
        if (none > 0f)
        {
            if (anyBurgerEnabled) burgerP += none;
            else if (orderConfig.friesItem != null && orderConfig.IsItemEnabled(orderConfig.friesItem)) friesP += none;
            else if (orderConfig.drinkItem != null && orderConfig.IsItemEnabled(orderConfig.drinkItem)) drinkP += none;
        }

        var enabledBurgers = new List<ItemDefinition>();
        if (orderConfig.burgerBase != null && orderConfig.IsItemEnabled(orderConfig.burgerBase))
            enabledBurgers.Add(orderConfig.burgerBase);
        if (orderConfig.cheeseburgerItem != null && orderConfig.IsItemEnabled(orderConfig.cheeseburgerItem))
            enabledBurgers.Add(orderConfig.cheeseburgerItem);
        if (enabledBurgers.Count > 0)
        {
            float perBurger = burgerP / enabledBurgers.Count;
            foreach (ItemDefinition burger in enabledBurgers)
                chances[burger] = perBurger;
        }
        if (orderConfig.friesItem != null && orderConfig.IsItemEnabled(orderConfig.friesItem))
            chances[orderConfig.friesItem] = friesP;
        if (includeDrinks && orderConfig.drinkItem != null && orderConfig.IsItemEnabled(orderConfig.drinkItem))
            chances[orderConfig.drinkItem] = drinkP;
        return chances;
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
        if (flow == null || productionFlows == null || productionFlows.Count <= 1) return;
        foreach (KitchenEmployee worker in new List<KitchenEmployee>(flow.workers))
            if (worker != null) worker.ClearAllOperatedStations();
        productionFlows.Remove(flow);
        selectedFlowIndex = Mathf.Clamp(selectedFlowIndex, 0, productionFlows.Count - 1);
        lastFlowBalance = null;
        SyncLegacyFlowSelection();
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
        if (outgoing.Count == 0) return null;

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
        if (requiredTarget != null && outgoing.Contains(requiredTarget))
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

        return best;
    }

    GameObject GetRequiredBranchTarget(ProductionJob job, GameObject source)
    {
        if (job == null || !job.isAssemblySupply || job.assemblySupplyTarget == null) return null;
        if (source != null && source.GetComponent<CuttingStation>() != null)
            return job.assemblySupplyTarget.gameObject;
        bool needsCutting = orderConfig != null
            && orderConfig.AssemblySupplyRequiresCutting(job.assemblySupplyTarget.GetSelectedRecipe());
        return needsCutting ? null : job.assemblySupplyTarget.gameObject;
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
            return job.CurrentStationType == StationType.Cutting
                ? GetAssemblySupplyOutput(job.assemblySupplyTarget)
                : GetAssemblySupplySource(job.assemblySupplyTarget);
        if (job.CurrentStationType == StationType.Pantry && orderConfig != null && orderConfig.IsFries(job.product))
            return PotatoItem;
        if (job.CurrentStationType == StationType.Cutting && orderConfig != null && orderConfig.IsFries(job.product))
            return SlicedPotatoItem;
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
        if (target.GetComponent<GrillStation>() != null) return item == orderConfig.rawPattyIngredient;
        CuttingStation cutting = target.GetComponent<CuttingStation>();
        if (cutting != null) return cutting.CanProcess(item);
        if (target.GetComponent<FryerStation>() != null) return item == SlicedPotatoItem;
        AssemblyStation assembly = target.GetComponent<AssemblyStation>();
        if (assembly == null) return false;
        AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
        if (recipe == null) return false;
        ItemDefinition processed = recipe.processedInput != null
            ? recipe.processedInput : orderConfig.cookedPattyIngredient;
        return item == processed || item == recipe.pantryInput;
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
        nextPlanningTime = Time.unscaledTime + Mathf.Max(0.05f, planningInterval);
        RefreshStations();
        RecoverOrphanedJobAssignments();
        CollectProductionJobs();
        EnsureAssemblySupplyJobs();
        AssignJobsToEmployees();
    }

    void EnsureAssemblySupplyJobs()
    {
        AssemblyStation[] stations = FindObjectsByType<AssemblyStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (AssemblyStation station in stations)
        {
            if (station == null) continue;
            AssemblyRecipeDefinition recipe = station.GetSelectedRecipe();
            if (recipe == null || recipe.output == null || recipe.pantryInput == null) continue;

            int target = AssemblyStation.IngredientCapacity;
            int reserved = station.BufferedPantryInputCount;
            bool alreadyQueued = false;
            foreach (ProductionJob queued in pendingJobs)
            {
                if (queued == null || !queued.isAssemblySupply || queued.assemblySupplyTarget != station) continue;
                reserved += Mathf.Max(1, queued.requestedSupplyUnits);
                alreadyQueued = true;
            }
            if (reserved >= target || alreadyQueued) continue;

            bool requiresCutting = orderConfig != null && orderConfig.AssemblySupplyRequiresCutting(recipe);
            var supply = new ProductionJob(CustomerOrder.FromItem(recipe.output, 1),
                requiresCutting
                    ? new[] { StationType.Pantry, StationType.Cutting }
                    : new[] { StationType.Pantry })
            {
                isAssemblySupply = true,
                assemblySupplyTarget = station,
                requestedSupplyUnits = target - reserved
            };
            if (CanAnyWorkerContinue(supply))
                pendingJobs.Add(supply);
        }
    }

    int MenuJobCount()
    {
        int count = 0;
        foreach (ProductionJob job in pendingJobs)
            if (job != null && !job.isAssemblySupply)
                count++;
        return count;
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
            if (owner != null && !owner.IsWorkingOn(job))
                job.assignedTo = null;

            // Reservations are meaningful only while a worker owns the task.
            // A task released by a recovery path must not leave a station or
            // destination permanently unavailable to the rest of the flow.
            if (job.assignedTo == null && (job.reservedWorkStation != null
                || job.reservedSourceStation != null || job.reservedDestinationStation != null))
                ReleaseReservations(job);

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
        registers.RemoveAll(r => r == null || !r.gameObject.activeInHierarchy);
        foreach (Register placed in FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (placed != null && !registers.Contains(placed))
                registers.Add(placed);
        if (freezer == null) freezer = FindObjectOfType<FreezerStation>();
        if (grill == null) grill = FindObjectOfType<GrillStation>();
        if (assembly == null) assembly = FindObjectOfType<AssemblyStation>();
        if (fryer == null) fryer = FindObjectOfType<FryerStation>();
        if (drinkStation == null) drinkStation = FindObjectOfType<DrinkStation>();
        if (heatLamp == null) heatLamp = HeatLampStation.Instance != null ? HeatLampStation.Instance : FindObjectOfType<HeatLampStation>();
    }

    void CollectProductionJobs()
    {
        if (orderConfig == null) return;

        HeatLampStation[] lamps = FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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
        int target = totalCapacity;

        int inFlight = heldCount + MenuJobCount();
        int openSlots = totalCapacity - inFlight;

        // Always keep stock up — kitchen produces without waiting for customers.
        target = Mathf.Clamp(target, 1, totalCapacity);
        var cookable = GetCookableMenuItems();
        if (cookable.Count == 0) return;

        var stockCounts = CountInFlightByItem();

        // Keep assigned cooks cycling: if someone is idle and can cook, raise the
        // production target so they immediately start another flow loop.
        int idleCookSlots = CountIdleCookSlots(cookable);
        if (idleCookSlots > 0)
        {
            target = Mathf.Min(totalCapacity, Mathf.Max(target, heldCount + MenuJobCount() + idleCookSlots));
            // Drink (and other specialist) cooks still need a job when the pass is full
            // of other items — they will wait at delivery if there is no space yet.
            openSlots = Mathf.Max(openSlots, idleCookSlots);
        }

        if (openSlots <= 0) return;

        // Prefer matching pending jobs to the cooks who can run them.
        EnsurePendingJobsForIdleCooks(cookable, stockCounts, ref openSlots);

        while (openSlots > 0 && heldCount + MenuJobCount() < target)
        {
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
        var list = new List<ItemDefinition>();
        if (orderConfig == null) return list;

        bool canBurger = false;
        bool canFries = false;
        bool canDrink = false;
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
                        if (id == "Drink") canDrink = true;
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
                        if (station.GetComponent<DrinkStation>() != null) canDrink = true;
                    }
                }
                if (flowHasFreezer && flowHasGrill && flowHasAssembly)
                    canBurger = true;
                if (flowHasPantry && flowHasPotatoCutting && flowHasFryer)
                    canFries = true;
            }
        }

        // Fallback: if no flows yet, allow any menu item the kitchen has stations for.
        if (!hasConfiguredFlow)
        {
            canBurger = HasConfiguredFreezer()
                && (grill != null || FindObjectOfType<GrillStation>() != null)
                && (assembly != null || FindObjectOfType<AssemblyStation>() != null);
            canFries = (fryer != null || FindObjectOfType<FryerStation>() != null)
                && HasPantrySelection(PotatoItem, false)
                && HasCuttingSupplyPath(PotatoItem, SlicedPotatoItem, false);
            canDrink = drinkStation != null || FindObjectOfType<DrinkStation>() != null;
        }

        if (canBurger && HasConfiguredFreezer())
        {
            ItemDefinition[] burgers =
            {
                orderConfig.burgerBase, orderConfig.cheeseburgerItem,
                orderConfig.clBurgerItem, orderConfig.cltBurgerItem
            };
            foreach (ItemDefinition burger in burgers)
            {
                if (burger != null && orderConfig.IsItemEnabled(burger)
                    && HasAssemblyChainAvailable(burger, hasConfiguredFlow)
                    && HasAssemblyPantrySupplies(burger, hasConfiguredFlow)
                    && HasRequiredCuttingSupplyAvailable(burger, hasConfiguredFlow))
                    list.Add(burger);
            }
        }
        if (canFries && HasPantrySelection(PotatoItem, hasConfiguredFlow)
            && HasCuttingSupplyPath(PotatoItem, SlicedPotatoItem, hasConfiguredFlow)
            && orderConfig.friesItem != null && orderConfig.IsItemEnabled(orderConfig.friesItem))
            list.Add(orderConfig.friesItem);
        if (canDrink && orderConfig.drinkItem != null && orderConfig.IsItemEnabled(orderConfig.drinkItem))
            list.Add(orderConfig.drinkItem);
        return list;
    }

    bool HasConfiguredFreezer()
    {
        if (orderConfig == null || orderConfig.burgerBase == null) return false;
        foreach (FreezerStation candidate in FindObjectsByType<FreezerStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (candidate != null && candidate.CanSupply(orderConfig.burgerBase))
                return true;
        return false;
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
            if (source != null && !HasPantrySelection(source, useFlows)) return false;
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

        foreach (PantryStation pantry in FindObjectsByType<PantryStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
        foreach (AssemblyStation station in FindObjectsByType<AssemblyStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
            if (!orderConfig.AssemblySupplyRequiresCutting(recipe)) continue;
            ItemDefinition requiredRaw = orderConfig.GetAssemblySupplySource(recipe);
            ItemDefinition requiredOutput = recipe.pantryInput;
            if (!HasCuttingSupplyPath(requiredRaw, requiredOutput, useFlows)) return false;
        }
        return true;
    }

    bool HasCuttingSupplyPath(ItemDefinition requiredRaw, ItemDefinition requiredOutput, bool useFlows)
    {
        if (requiredRaw == null || requiredOutput == null) return false;
        if (!useFlows)
        {
            if (!HasPantrySelection(requiredRaw, false)) return false;
            foreach (CuttingStation cutting in FindObjectsByType<CuttingStation>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (cutting != null && cutting.CanProcess(requiredRaw, requiredOutput)) return true;
            return false;
        }

        foreach (ProductionFlowPlan flow in productionFlows)
        {
            if (flow?.stations == null) continue;
            bool foundPantry = false;
            foreach (GameObject station in flow.stations)
            {
                if (station == null) continue;
                PantryStation pantry = station.GetComponent<PantryStation>();
                if (pantry != null && pantry.CanDispense(requiredRaw)) foundPantry = true;
                else if (foundPantry)
                {
                    CuttingStation cutting = station.GetComponent<CuttingStation>();
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
        foreach (HeatLampStation lamp in FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
        CustomerAI[] customers = FindObjectsByType<CustomerAI>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (CustomerAI customer in customers)
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
            TryAssignHighestPriorityTask(e);
        }
    }

    /// <summary>
    /// Gives the worker the first runnable step queued for their assigned flow.
    /// Blocked steps are skipped and retried on the next planning pass. This is
    /// deliberately FIFO: workers follow flow work instead of scoring, jumping,
    /// or inventing priorities that can strand an intermediate item.
    /// </summary>
    public bool TryAssignHighestPriorityTask(KitchenEmployee employee)
    {
        if (employee == null || employee.HasJob || !employee.CanTakeJobs) return false;
        if (employee.IsWaitingToReevaluateTasks || employee.ShouldDeliverInsteadOfCook()) return false;

        ProductionFlowPlan flow = GetFlowForWorker(employee);
        if (flow?.stations == null || flow.stations.Count == 0) return false;

        if (TryAssignFirstRunnableFlowStep(employee, flow))
            return true;

        // If this flow has no queued work, create one compatible production
        // cycle and immediately try it. Nothing else is inferred or reordered.
        if (TryQueueCompatibleStockJob(employee, true) != null)
            return TryAssignFirstRunnableFlowStep(employee, flow);

        return false;
    }

    bool TryAssignFirstRunnableFlowStep(KitchenEmployee employee, ProductionFlowPlan flow)
    {
        foreach (ProductionJob job in pendingJobs)
        {
            if (job == null || job.assignedTo != null) continue;
            GameObject station = employee.FindStationForJob(job, true, true);
            if (station == null || !flow.stations.Contains(station)) continue;
            if (!TryReserveCurrentStation(job, employee, true)) continue;

            if (job.assignedTo != null)
            {
                ReleaseReservations(job);
                continue;
            }

            job.assignedTo = employee;
            employee.AssignJob(job);
            return employee.ActiveJob == job;
        }
        return false;
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
        int target = 0;
        HeatLampStation[] lamps = FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (lamps == null || lamps.Length == 0) return null;
        foreach (HeatLampStation lamp in lamps)
        {
            if (lamp == null) continue;
            heldCount += lamp.Count;
            target += Mathf.Max(0, lamp.maxCapacity);
        }

        // Do not overproduce. This fallback only repairs a missing compatible job while the
        // configured ready-stock target still has an open slot.
        if (heldCount + MenuJobCount() >= target) return null;

        List<ItemDefinition> cookable = GetCookableMenuItems();
        ItemDefinition item = PickItemWorkerCanCook(employee, cookable, searchEntireFlow);
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
            pendingJobs.Add(job);
    }

    /// <summary>Discard runtime-only production work before rebuilding a saved kitchen.</summary>
    public void ResetTransientProductionState()
    {
        foreach (ProductionJob job in new List<ProductionJob>(jobsWithReservations))
            ReleaseReservations(job);
        stationWorkReservations.Clear();
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
        pendingJobs.Remove(job);
    }

    public bool TryReserveCurrentStation(ProductionJob job, KitchenEmployee employee,
        bool searchEntireFlow = false)
    {
        if (job == null || employee == null || !job.CurrentStationType.HasValue) return false;
        GameObject station = employee.FindStationForJob(job, searchEntireFlow, true);
        if (station == null || !IsStationInWorkerFlow(employee, station)) return false;
        if (stationWorkReservations.TryGetValue(station, out ProductionJob owner)
            && owner != null && owner != job)
            return false;

        stationWorkReservations[station] = job;
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
            if (job.pipeline != null && System.Array.IndexOf(job.pipeline, StationType.Assembly) >= 0)
                desiredBatch = Mathf.Min(desiredBatch, AssemblyStation.IngredientCapacity);
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
            // buffer first so a worker never ends up idling with an undeliverable bun.
            GameObject destination = GetFlowOutput(employee, station, job);
            ItemDefinition pantryOutput = GetBranchTransferItem(job);
            int desiredBatch = Mathf.Clamp(
                job.requestedSupplyUnits > 0 ? job.requestedSupplyUnits : employee.CarryCapacity,
                1, employee.CarryCapacity);
            if (destination == null || !IsStationInWorkerFlow(employee, destination)
                || pantryOutput == null
                || !TryReserveDestination(job, destination, pantryOutput, desiredBatch))
            {
                ReleaseWorkReservation(job);
                return false;
            }
        }
        return true;
    }

    public void ReleaseWorkReservation(ProductionJob job)
    {
        if (job == null || job.reservedWorkStation == null) return;
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
        else if (station.GetComponent<GrillStation>() != null)
        {
            if (ReservedInputCount(station, item, except) > 0) return 0;
            occupied = buffer.GetInputCount(item) + buffer.GetOutputCount(item);
        }
        else if (station.GetComponent<AssemblyStation>() != null)
            return Mathf.Max(0, AssemblyStation.IngredientCapacity - buffer.GetInputCount(item)
                - ReservedInputCount(station, item, except));
        else
            occupied = buffer.GetInputCount(item);
        return Mathf.Max(0, buffer.InputSlotCapacity - occupied
            - ReservedInputCount(station, item, except));
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
        if (emp != null && emp.targetGrill != null) return emp.targetGrill;
        if (emp != null)
        {
            var gs = emp.GetGrillStation();
            if (gs != null) return gs;
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
        IStationBuffer buffer = GetGrillFor(forEmployee);
        ItemDefinition input = orderConfig != null && orderConfig.rawPattyIngredient != null
            ? orderConfig.rawPattyIngredient : item;
        return buffer != null ? buffer.StoreInput(input, amount) : 0;
    }

    public bool TakePattyFromGrill(KitchenEmployee forEmployee = null)
    {
        return TakePattiesFromGrill(forEmployee, PattyItem, 1) == 1;
    }

    public int TakePattiesFromGrill(KitchenEmployee forEmployee, ItemDefinition item, int amount)
    {
        IStationBuffer buffer = GetGrillFor(forEmployee);
        ItemDefinition output = orderConfig != null && orderConfig.cookedPattyIngredient != null
            ? orderConfig.cookedPattyIngredient : item;
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
        if (f == null) f = freezer != null ? freezer : FindObjectOfType<FreezerStation>();
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
        var f = GetFryerFor(forEmployee);
        if (f == null || SlicedPotatoItem == null) return false;
        return f.TryLoad(SlicedPotatoItem);
    }

    public bool TakeFromFryer(KitchenEmployee forEmployee = null)
    {
        var f = GetFryerFor(forEmployee);
        return f != null && f.TakeCooked();
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
