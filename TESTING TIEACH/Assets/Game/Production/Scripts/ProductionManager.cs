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

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        moneyManager = FindObjectOfType<MoneyManager>();
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
        if (moneyManager != null && !moneyManager.TrySpend(hireCost))
            return null;
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
        }
        return emp;
    }

    static bool raisedFirstWorkerEvent;

    static void RaiseFirstWorkerHiredEvent()
    {
        if (raisedFirstWorkerEvent) return;
        raisedFirstWorkerEvent = true;
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstWorkerHired);
    }

    public void FireWorker(KitchenEmployee emp)
    {
        if (emp == null) return;
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

        int openSlots = heatLamp.maxCapacity - heatLamp.Count - pendingJobs.Count;
        if (openSlots <= 0) return;

        var available = heatLamp.GetHeldOrderClones();
        foreach (var job in pendingJobs)
        {
            if (job?.order != null)
                available.Add(job.order.Clone());
        }

        // Demand: one job per missing menu line
        foreach (var customerOrder in GetAllQueuedOrders())
        {
            if (customerOrder?.lines == null) continue;
            foreach (var line in customerOrder.lines)
            {
                if (line.item == null || line.quantity <= 0) continue;
                // Drinks are served by the cashier, not cooked into the heat lamp
                if (orderConfig.IsDrink(line.item)) continue;
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

        // Speculative single-item buffer
        int target = Mathf.Clamp(heatLamp.targetStock, 0, heatLamp.maxCapacity);
        while (openSlots > 0 && heatLamp.Count + pendingJobs.Count < target)
        {
            var speculative = orderConfig.GenerateRandomSingleItemOrder();
            if (speculative == null || speculative.PrimaryItem == null)
                break;
            var job = CreateJob(speculative);
            if (job == null) break;
            pendingJobs.Add(job);
            openSlots--;
        }
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
        {
            if (e == null || !e.IsIdle || !e.CanTakeJobs) continue;
            foreach (var job in pendingJobs)
            {
                if (job.assignedTo != null) continue;
                if (!e.CanTakeJobStep(job)) continue;
                job.assignedTo = e;
                e.AssignJob(job);
                break;
            }
        }
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
