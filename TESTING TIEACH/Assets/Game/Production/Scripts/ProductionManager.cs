using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Station types workers can be assigned to.
/// Production pipelines use Freezer/Grill/Assembly (burger), Fryer (fries), Drink (drink).
/// Pantry is legacy / unused by the current menu.
/// </summary>
public enum StationType
{
    Freezer = 0,
    Grill = 1,
    Pantry = 2,
    Assembly = 3,
    Register = 4,
    Fryer = 5,
    Drink = 6
}

public class ProductionJob
{
    public CustomerOrder order;
    public ItemDefinition product;
    public StationType[] pipeline;
    /// <summary>Index into pipeline, or pipeline.Length for heat-lamp delivery.</summary>
    public int currentStepIndex;
    public KitchenEmployee assignedTo;
    public bool hasPatty;
    /// <summary>Batch size carried between stations (upgraded workers hold more).</summary>
    public int heldUnits;
    public readonly List<ItemDefinition> ingredientsHeld = new List<ItemDefinition>();
    /// <summary>Heat lamp this job must deliver to (from the last station's Assign Output link).</summary>
    public HeatLampStation deliveryHeatLamp;

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

    public ProductionJob(CustomerOrder o, StationType[] steps)
    {
        order = o;
        product = o != null ? o.PrimaryItem : null;
        pipeline = steps ?? System.Array.Empty<StationType>();
        currentStepIndex = 0;
        assignedTo = null;
        hasPatty = false;
        heldUnits = 0;
        deliveryHeatLamp = null;
    }
}

/// <summary>
/// Fast-food production: one job per menu item (burger / fries / drink).
/// Work order: Burger = Freezer → Grill → Assembly; Fries = Fryer.
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
    MoneyManager moneyManager;

    FreezerStation freezer;
    GrillStation grill;
    AssemblyStation assembly;
    FryerStation fryer;
    DrinkStation drinkStation;

    public ItemDefinition PattyItem => orderConfig != null ? orderConfig.burgerBase : null;
    public ItemDefinition FriesItem => orderConfig != null ? orderConfig.friesItem : null;
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
        /// <summary>Kitchen units still short of live orders (absolute).</summary>
        public int requiredOutput;
        /// <summary>Target production rate (items per real minute) to meet demand.</summary>
        public float requiredPerMinute;
    }

    public List<ItemOutputNeed> GetRequiredOutputByItem()
    {
        var requested = new Dictionary<ItemDefinition, int>();
        foreach (var order in GetAllQueuedOrders())
        {
            if (order?.lines == null) continue;
            foreach (var line in order.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (orderConfig != null && orderConfig.IsDrink(line.item)) continue;
                requested[line.item] = requested.TryGetValue(line.item, out int c)
                    ? c + line.quantity
                    : line.quantity;
            }
        }

        var ready = new Dictionary<ItemDefinition, int>();
        var cooking = new Dictionary<ItemDefinition, int>();
        if (heatLamp != null)
        {
            foreach (var meal in heatLamp.Meals)
            {
                ItemDefinition item = meal?.order != null ? meal.order.PrimaryItem : null;
                if (item == null) continue;
                ready[item] = ready.TryGetValue(item, out int c) ? c + 1 : 1;
            }
        }
        foreach (var job in pendingJobs)
        {
            ItemDefinition item = job?.product;
            if (item == null) continue;
            cooking[item] = cooking.TryGetValue(item, out int c) ? c + 1 : 1;
        }

        float customersPerMinute = EstimateCustomersPerMinute();
        var chanceByItem = EstimateOrderChanceByItem();

        var items = new HashSet<ItemDefinition>();
        foreach (var kv in requested) items.Add(kv.Key);
        foreach (var kv in ready) items.Add(kv.Key);
        foreach (var kv in cooking) items.Add(kv.Key);
        foreach (var kv in chanceByItem) items.Add(kv.Key);
        foreach (var item in GetCookableMenuItems())
            if (item != null) items.Add(item);

        // Clear live shortfall within a few minutes so backlog also drives rate.
        const float backlogClearMinutes = 3f;

        var list = new List<ItemOutputNeed>(items.Count);
        foreach (var item in items)
        {
            if (item == null) continue;
            requested.TryGetValue(item, out int req);
            ready.TryGetValue(item, out int readyCount);
            cooking.TryGetValue(item, out int cookingCount);
            int shortfall = Mathf.Max(0, req - readyCount - cookingCount);

            chanceByItem.TryGetValue(item, out float chance);
            float arrivalRate = customersPerMinute * Mathf.Clamp01(chance);
            float backlogRate = shortfall / backlogClearMinutes;
            float requiredPerMinute = Mathf.Max(arrivalRate, backlogRate);

            list.Add(new ItemOutputNeed
            {
                item = item,
                requested = req,
                ready = readyCount,
                cooking = cookingCount,
                requiredOutput = shortfall,
                requiredPerMinute = requiredPerMinute
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

    float EstimateCustomersPerMinute()
    {
        var spawner = FindObjectOfType<CustomerSpawner>();
        if (spawner != null)
            return spawner.CustomersPerMinute;
        return 10f;
    }

    Dictionary<ItemDefinition, float> EstimateOrderChanceByItem()
    {
        var chances = new Dictionary<ItemDefinition, float>();
        if (orderConfig == null) return chances;

        // Matches GenerateRandomOrder: independent rolls, with fallback to at least one item.
        float b = orderConfig.burgerBase != null ? Mathf.Clamp01(orderConfig.burgerChance) : 0f;
        float f = orderConfig.friesItem != null ? Mathf.Clamp01(orderConfig.friesChance) : 0f;
        float d = orderConfig.drinkItem != null ? Mathf.Clamp01(orderConfig.drinkChance) : 0f;
        float none = (1f - b) * (1f - f) * (1f - d);

        float burgerP = b;
        float friesP = f;
        if (none > 0f)
        {
            if (orderConfig.burgerBase != null) burgerP += none;
            else if (orderConfig.friesItem != null) friesP += none;
        }

        if (orderConfig.burgerBase != null)
            chances[orderConfig.burgerBase] = burgerP;
        if (orderConfig.friesItem != null)
            chances[orderConfig.friesItem] = friesP;
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

    /// <summary>
    /// True if this station is locked to a different flow.
    /// Heat lamps are shared pass-throughs and may appear on multiple flows.
    /// </summary>
    public bool IsStationOnOtherFlow(GameObject station, ProductionFlowPlan except)
    {
        if (station == null || productionFlows == null) return false;
        if (station.GetComponent<HeatLampStation>() != null)
            return false;

        foreach (ProductionFlowPlan flow in productionFlows)
            if (flow != null && flow != except && flow.stations != null && flow.stations.Contains(station))
                return true;
        return false;
    }

    public void AddWorkerToSelectedFlow(KitchenEmployee employee)
    {
        if (employee == null) return;
        EnsureProductionFlows();
        ProductionFlowPlan targetFlow = SelectedFlow;
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
            AddWorkerToSelectedFlow(emp);
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
            AddWorkerToSelectedFlow(emp);
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
        RefreshStations();
        CollectProductionJobs();
        AssignJobsToEmployees();
    }

    /// <summary>Run job collection now (e.g. right after a cook finishes a delivery).</summary>
    public void RequestImmediateProduction()
    {
        RefreshStations();
        CollectProductionJobs();
    }

    void RefreshStations()
    {
        if (freezer == null) freezer = FindObjectOfType<FreezerStation>();
        if (grill == null) grill = FindObjectOfType<GrillStation>();
        if (assembly == null) assembly = FindObjectOfType<AssemblyStation>();
        if (fryer == null) fryer = FindObjectOfType<FryerStation>();
        if (drinkStation == null) drinkStation = FindObjectOfType<DrinkStation>();
        if (heatLamp == null) heatLamp = HeatLampStation.Instance != null ? HeatLampStation.Instance : FindObjectOfType<HeatLampStation>();
    }

    void CollectProductionJobs()
    {
        if (heatLamp == null || orderConfig == null) return;

        int inFlight = heatLamp.Count + pendingJobs.Count;
        int openSlots = heatLamp.maxCapacity - inFlight;
        if (openSlots <= 0) return;

        // Always keep stock up — kitchen produces without waiting for customers.
        int target = Mathf.Clamp(heatLamp.targetStock, 1, heatLamp.maxCapacity);
        var cookable = GetCookableMenuItems();
        if (cookable.Count == 0) return;

        var stockCounts = CountInFlightByItem();

        // Keep assigned cooks cycling: if someone is idle and can cook, raise the
        // production target so they immediately start another flow loop.
        int idleCookSlots = CountIdleCookSlots(cookable);
        if (idleCookSlots > 0)
            target = Mathf.Min(heatLamp.maxCapacity, Mathf.Max(target, heatLamp.Count + pendingJobs.Count + idleCookSlots));

        while (openSlots > 0 && heatLamp.Count + pendingJobs.Count < target)
        {
            ItemDefinition item = PickLeastStockedItem(cookable, stockCounts);
            if (item == null) break;
            var job = CreateJob(CustomerOrder.FromItem(item, 1));
            if (job == null) break;
            pendingJobs.Add(job);
            stockCounts[item] = stockCounts.TryGetValue(item, out int c) ? c + 1 : 1;
            openSlots--;
        }

        // Prefer matching pending jobs to the cooks who can run them.
        EnsurePendingJobsForIdleCooks(cookable, stockCounts, ref openSlots);

        // Extra demand from live customers can push production up to max capacity.
        if (openSlots <= 0) return;
        var available = heatLamp.GetHeldOrderClones();
        foreach (var job in pendingJobs)
        {
            if (job?.order != null)
                available.Add(job.order.Clone());
        }

        foreach (var customerOrder in GetAllQueuedOrders())
        {
            if (customerOrder?.lines == null) continue;
            foreach (var line in customerOrder.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                if (orderConfig.IsDrink(line.item)) continue;
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
        if (productionFlows != null)
        {
            foreach (ProductionFlowPlan flow in productionFlows)
            {
                if (flow?.stepIds == null) continue;
                for (int i = 0; i < flow.stepIds.Count; i++)
                {
                    string id = flow.stepIds[i];
                    if (id == "Freezer" || id == "Grill" || id == "Assembly")
                        canBurger = true;
                    if (id == "Fryer")
                        canFries = true;
                }
                if (flow.stations == null) continue;
                foreach (GameObject station in flow.stations)
                {
                    if (station == null) continue;
                    if (station.GetComponent<FreezerStation>() != null
                        || station.GetComponent<GrillStation>() != null
                        || station.GetComponent<AssemblyStation>() != null)
                        canBurger = true;
                    if (station.GetComponent<FryerStation>() != null)
                        canFries = true;
                }
            }
        }

        // Fallback: if no flows yet, allow any menu item the kitchen has stations for.
        if (!canBurger && !canFries)
        {
            canBurger = freezer != null || grill != null || assembly != null
                || FindObjectOfType<FreezerStation>() != null
                || FindObjectOfType<GrillStation>() != null;
            canFries = fryer != null || FindObjectOfType<FryerStation>() != null;
        }

        if (canBurger && orderConfig.burgerBase != null)
            list.Add(orderConfig.burgerBase);
        if (canFries && orderConfig.friesItem != null)
            list.Add(orderConfig.friesItem);
        return list;
    }

    int CountIdleCookSlots(List<ItemDefinition> cookable)
    {
        if (cookable == null || cookable.Count == 0) return 0;
        int n = 0;
        foreach (var e in employees)
        {
            if (e == null || !e.IsIdle || !e.CanTakeJobs) continue;
            if (e.ShouldDeliverInsteadOfCook()) continue;
            if (PickItemWorkerCanCook(e, cookable) != null)
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
                if (!e.CanTakeJobStep(job)) continue;
                alreadyQueued = true;
                break;
            }
            if (alreadyQueued) continue;

            ItemDefinition item = PickItemWorkerCanCook(e, cookable);
            if (item == null) continue;
            var jobNew = CreateJob(CustomerOrder.FromItem(item, 1));
            if (jobNew == null) continue;
            pendingJobs.Add(jobNew);
            stockCounts[item] = stockCounts.TryGetValue(item, out int c) ? c + 1 : 1;
            openSlots--;
        }
    }

    ItemDefinition PickItemWorkerCanCook(KitchenEmployee employee, List<ItemDefinition> cookable)
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
            if (!employee.CanTakeJobStep(probe)) continue;
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
        if (heatLamp != null)
        {
            foreach (var meal in heatLamp.Meals)
            {
                ItemDefinition item = meal?.order != null ? meal.order.PrimaryItem : null;
                if (item == null) continue;
                counts[item] = counts.TryGetValue(item, out int c) ? c + 1 : 1;
            }
        }
        foreach (var job in pendingJobs)
        {
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
        return new ProductionJob(singleItemOrder, pipeline);
    }

    List<CustomerOrder> GetAllQueuedOrders()
    {
        var list = new List<CustomerOrder>();
        foreach (var reg in registers)
        {
            if (reg == null || !reg.isEnabled) continue;
            foreach (var order in reg.GetQueuedOrders())
            {
                if (order != null)
                    list.Add(order);
            }
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
            TryAssignJobTo(e);
    }

    /// <summary>Assign the next suitable pending job to an idle cook, if any.</summary>
    public bool TryAssignJobTo(KitchenEmployee employee)
    {
        if (employee == null || !employee.IsIdle || !employee.CanTakeJobs) return false;
        if (employee.ShouldDeliverInsteadOfCook()) return false;

        ProductionJob bestJob = null;
        foreach (var job in pendingJobs)
        {
            if (job == null || job.assignedTo != null) continue;
            if (!employee.CanTakeJobStep(job)) continue;
            bestJob = job;
            break;
        }

        if (bestJob == null) return false;
        bestJob.assignedTo = employee;
        employee.AssignJob(bestJob);
        return true;
    }

    public void ReleaseJob(ProductionJob job)
    {
        if (job != null)
            job.assignedTo = null;
    }

    public void CompleteJob(ProductionJob job)
    {
        if (job == null) return;
        job.assignedTo = null;
        pendingJobs.Remove(job);
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
        if (l == null) return false;
        return l.DeliverMeal(order);
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
            var ast = forEmployee.GetAssemblyStation();
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
        var a = forEmployee != null ? forEmployee.GetAssemblyStation() : null;
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
        var g = GetGrillFor(forEmployee);
        if (g == null || !g.CanPlacePatty()) return false;
        g.PlacePatty();
        return true;
    }

    public bool TakePattyFromGrill(KitchenEmployee forEmployee = null)
    {
        var g = GetGrillFor(forEmployee);
        return g != null && g.TakeCookedPatty();
    }

    public float GetFreezerInteractionTime(KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null) f = freezer;
        return f != null ? f.interactionTimeSeconds : 1f;
    }

    public bool TryTakePattyFromFreezer(KitchenEmployee forEmployee = null)
    {
        var f = forEmployee != null ? forEmployee.GetFreezerStation() : null;
        if (f == null) f = freezer;
        if (f == null) return false;
        return f.TryTakePatty(PattyItem);
    }

    public bool HasPattyInStock()
    {
        if (PattyItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(PattyItem);
    }

    public float GetGrillPlaceTime(KitchenEmployee forEmployee = null) => GetGrillFor(forEmployee) != null ? GetGrillFor(forEmployee).placeTimeSeconds : 0.5f;
    public float GetGrillCookTime(KitchenEmployee forEmployee = null) => GetGrillFor(forEmployee) != null ? GetGrillFor(forEmployee).cookTimeSeconds : 4f;
    public float GetGrillWaitAfterCookedTime(KitchenEmployee forEmployee = null) => GetGrillFor(forEmployee) != null ? GetGrillFor(forEmployee).waitAfterCookedSeconds : 0.5f;
    public float GetGrillTakeTime(KitchenEmployee forEmployee = null) => GetGrillFor(forEmployee) != null ? GetGrillFor(forEmployee).takeTimeSeconds : 0.5f;

    public float GetAssemblyInteractionTime(KitchenEmployee forEmployee = null)
    {
        var a = forEmployee != null ? forEmployee.GetAssemblyStation() : null;
        if (a == null) a = assembly;
        return a != null ? a.interactionTimeSeconds : 1f;
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

    public float GetFryerTotalTime(KitchenEmployee forEmployee = null)
    {
        var f = GetFryerFor(forEmployee);
        if (f == null) return 6f;
        return f.loadTimeSeconds + f.cookTimeSeconds + f.waitAfterCookedSeconds + f.takeTimeSeconds;
    }

    public bool TryLoadFryer(KitchenEmployee forEmployee = null)
    {
        var f = GetFryerFor(forEmployee);
        if (f == null || FriesItem == null) return false;
        return f.TryLoad(FriesItem);
    }

    public bool TakeFromFryer(KitchenEmployee forEmployee = null)
    {
        var f = GetFryerFor(forEmployee);
        return f != null && f.TakeCooked();
    }

    public float GetDrinkInteractionTime(KitchenEmployee forEmployee = null)
    {
        var d = GetDrinkFor(forEmployee);
        return d != null ? d.interactionTimeSeconds : 1.5f;
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
        if (FriesItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(FriesItem);
    }
}
