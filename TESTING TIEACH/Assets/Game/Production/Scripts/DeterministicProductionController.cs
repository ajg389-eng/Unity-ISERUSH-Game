using System;
using System.Collections.Generic;
using KitchenProduction;
using UnityEngine;
using Sim = KitchenProduction.ProductionSimulation;

/// <summary>
/// Unity adapter for the production clock. Unity objects supply configuration and
/// present state; the same pure simulation executes trips and predicts deliveries.
/// </summary>
public sealed class DeterministicProductionController : IDisposable
{
    public sealed class FlowForecast
    {
        public int Workers;
        public long Next60;
        public float TargetPerMinute;
        public float PlannedPerMinute;
        public long StartsAt;
        public string Warning;
    }

    sealed class Binding
    {
        public GameObject Object;
        public Sim.Node Node;
        public GrillStation Grill;
        public FryerStation Fryer;
        public CuttingStation Cutting;
        public AssemblyStation Assembly;
        public FreezerStation Freezer;
        public PantryStation Pantry;
        public HeatLampStation Pickup;
        public DrinkStation Drink;
        public StationRuntimeMetrics Metrics;
    }

    readonly ProductionManager manager;
    readonly List<Binding> bindings = new List<Binding>();
    readonly List<KitchenEmployee> workers = new List<KitchenEmployee>();
    readonly List<ItemDefinition> items = new List<ItemDefinition>();
    readonly Dictionary<ItemDefinition, int> itemIndex = new Dictionary<ItemDefinition, int>();
    readonly Dictionary<GameObject, int> stationIndex = new Dictionary<GameObject, int>();
    readonly Dictionary<KitchenEmployee, int> workerIndex = new Dictionary<KitchenEmployee, int>();
    readonly Dictionary<ProductionFlowPlan, int> flowIndex = new Dictionary<ProductionFlowPlan, int>();
    readonly Dictionary<(Vector3, Vector3), Vector3[]> routeCache = new Dictionary<(Vector3, Vector3), Vector3[]>();
    readonly Dictionary<(int, int), Vector3[]> routes = new Dictionary<(int, int), Vector3[]>();
    readonly Dictionary<GameObject, ProductionStationCheckpoint> restoredStations = new Dictionary<GameObject, ProductionStationCheckpoint>();
    readonly Dictionary<KitchenEmployee, ProductionWorkerCheckpoint> restoredWorkers = new Dictionary<KitchenEmployee, ProductionWorkerCheckpoint>();
    Vector3[] points;
    Sim simulation;
    FlowForecast[] forecasts;
    GridManager grid;
    int signature, gridRevision;
    double epoch, nextConfigurationCheck;
    bool configurationCheck = true, disposed, invalidEventLogged;
    long nextForecastAt = -1;
    long[] plannedTotals;
    float[] targetTotals;
    string[] workerDescriptions;
    int[] workerDescriptionKeys;

    public Sim Simulation => simulation;
    public bool OwnsStation(GameObject station) => station != null && stationIndex.ContainsKey(station);
    public void RequestConfigurationCheck() => configurationCheck = true;

    public DeterministicProductionController(ProductionManager manager)
    {
        this.manager = manager;
        grid = GridManager.Instance;
        if (grid != null) grid.GridChanged += GridChanged;
    }

    void GridChanged()
    {
        gridRevision++;
        routeCache.Clear();
        configurationCheck = true;
    }

    public void Dispose()
    {
        if (grid != null) grid.GridChanged -= GridChanged;
        foreach (KitchenEmployee worker in workers)
            if (worker != null) worker.ClearScheduledPresentation();
        disposed = true;
        stationIndex.Clear();
    }

    int Item(ItemDefinition item)
    {
        if (item == null) return -1;
        if (itemIndex.TryGetValue(item, out int index)) return index;
        index = items.Count;
        items.Add(item);
        itemIndex.Add(item, index);
        return index;
    }

    static long Cycle(float seconds) => seconds > 0f ? Sim.Seconds(seconds) : 1;
    static long Period(float rate) => rate > 0.001f ? Sim.Seconds(60.0 / rate) : Sim.Never;
    static bool Active(GameObject station) => station != null && station.activeInHierarchy;

    public void Tick()
    {
        if (disposed || !Application.isPlaying) return;
        if (grid != GridManager.Instance)
        {
            if (grid != null) grid.GridChanged -= GridChanged;
            grid = GridManager.Instance;
            if (grid != null) grid.GridChanged += GridChanged;
            GridChanged();
        }
        double now = Time.timeAsDouble;
        if (simulation == null || configurationCheck || now >= nextConfigurationCheck)
        {
            configurationCheck = false;
            nextConfigurationCheck = now + 0.25;
            int current = ConfigurationSignature();
            if (simulation == null || current != signature)
            {
                Rebuild();
                signature = ConfigurationSignature();
            }
        }
        if (simulation == null) return;
        SynchronizeExternalState();
        long target = Math.Max(simulation.Now, (long)Math.Floor((now - epoch) * Sim.TicksPerSecond + 1e-6));
        simulation.AdvanceTo(target);
        ApplyEvents();
        ProjectBuffers();
    }

    int ConfigurationSignature()
    {
        unchecked
        {
            int hash = 17 + gridRevision;
            if (manager.productionFlows == null) return hash;
            foreach (ProductionFlowPlan flow in manager.productionFlows)
            {
                if (flow == null) continue;
                hash = hash * 31 + flow.GetHashCode();
                if (flow.stations != null) foreach (GameObject station in flow.stations)
                {
                    if (!Active(station)) { hash *= 31; continue; }
                    hash = hash * 31 + station.GetInstanceID();
                    hash = hash * 31 + station.transform.position.GetHashCode();
                    hash = hash * 31 + station.transform.rotation.GetHashCode();
                    hash = hash * 31 + KitchenEmployee.GetInteractionPosition(station).GetHashCode();
                    StationNode node = station.GetComponent<StationNode>();
                    hash = hash * 31 + (node != null ? node.ProductionRatePercent.GetHashCode() : 0);
                    GrillStation grill = station.GetComponent<GrillStation>();
                    FryerStation fryer = station.GetComponent<FryerStation>();
                    CuttingStation cutting = station.GetComponent<CuttingStation>();
                    AssemblyStation assembly = station.GetComponent<AssemblyStation>();
                    FreezerStation freezer = station.GetComponent<FreezerStation>();
                    PantryStation pantry = station.GetComponent<PantryStation>();
                    DrinkStation drink = station.GetComponent<DrinkStation>();
                    HeatLampStation pickup = station.GetComponent<HeatLampStation>();
                    ItemDefinition selected = grill != null ? grill.GetSelectedOutput() : fryer != null ? fryer.GetSelectedOutput()
                        : cutting != null ? cutting.selectedProduct : assembly != null ? assembly.selectedProduct
                        : freezer != null ? freezer.selectedItem : pantry != null ? pantry.selectedItem : null;
                    hash = hash * 31 + (selected != null ? selected.GetInstanceID() : 0);
                    if (assembly != null) hash = hash * 31 + (assembly.GetSelectedRecipe() != null ? 1 : 0);
                    if (cutting != null) hash = hash * 31 + (cutting.GetSelectedRecipe() != null ? 1 : 0);
                    float seconds = grill != null ? grill.EffectiveProcessTimeSeconds : fryer != null ? fryer.EffectiveProcessTimeSeconds
                        : cutting != null ? cutting.EffectiveProcessTimeSeconds : assembly != null ? assembly.EffectiveProcessTimeSeconds
                        : freezer != null ? freezer.EffectiveMaxOutputPerMinute : pantry != null ? pantry.EffectiveMaxOutputPerMinute
                        : drink != null ? drink.processTimeSeconds : pickup != null ? pickup.maxCapacity : 0;
                    hash = hash * 31 + seconds.GetHashCode();
                    MonoBehaviour component = (MonoBehaviour)grill ?? (MonoBehaviour)fryer ?? (MonoBehaviour)cutting
                        ?? (MonoBehaviour)assembly ?? (MonoBehaviour)freezer ?? (MonoBehaviour)pantry ?? drink;
                    hash = hash * 31 + (component != null && component.enabled ? 1 : 0);
                }
                if (flow.connections != null) foreach (ProductionFlowConnection edge in flow.connections)
                {
                    hash = hash * 31 + (edge?.from != null ? edge.from.GetInstanceID() : 0);
                    hash = hash * 31 + (edge?.to != null ? edge.to.GetInstanceID() : 0);
                }
                if (flow.workers != null) foreach (KitchenEmployee worker in flow.workers)
                {
                    if (worker == null) continue;
                    hash = hash * 31 + worker.GetInstanceID();
                    hash = hash * 31 + worker.moveSpeed.GetHashCode();
                    hash = hash * 31 + worker.UpgradeLevel;
                    hash = hash * 31 + worker.pickupTimeSeconds.GetHashCode();
                    hash = hash * 31 + worker.placementTimeSeconds.GetHashCode();
                    hash = hash * 31 + (worker.isActiveAndEnabled ? 1 : 0);
                }
            }
            return hash;
        }
    }

    void Rebuild()
    {
        invalidEventLogged = false;
        // Keep physical cargo across edits. Unpicked reservations can safely be
        // discarded; already picked items remain on their individual worker.
        var cargo = new Dictionary<KitchenEmployee, (ItemDefinition item, int units, GameObject source)>();
        if (simulation != null)
            for (int i = 0; i < workers.Count; i++)
            {
                Sim.Trip trip = simulation.Trips[i];
                if (trip.CargoUnits <= 0) continue;
                ItemDefinition item = items[trip.CargoItem];
                GameObject source = trip.CargoSource >= 0 ? bindings[trip.CargoSource].Object : null;
                if (workers[i] != null) cargo[workers[i]] = (item, trip.CargoUnits, source);
                else KitchenInventory.Instance?.AddStock(item, trip.CargoUnits);
            }
        foreach (KitchenEmployee worker in workers) if (worker != null) worker.ClearScheduledPresentation();
        bindings.Clear(); workers.Clear(); items.Clear(); itemIndex.Clear(); stationIndex.Clear();
        workerIndex.Clear(); flowIndex.Clear(); routes.Clear();
        manager.EnsureProductionFlows();
        var stationObjects = new List<GameObject>();
        foreach (ProductionFlowPlan flow in manager.productionFlows)
        {
            flow.EnsureLegacyConnections();
            flowIndex[flow] = flowIndex.Count;
            foreach (GameObject station in flow.stations)
                if (Active(station) && !stationObjects.Contains(station) && Supported(station)) stationObjects.Add(station);
        }
        stationObjects.Sort((a, b) => string.CompareOrdinal(StationNode.EnsureOn(a).StableStationId,
            StationNode.EnsureOn(b).StableStationId));
        foreach (GameObject station in stationObjects)
        {
            stationIndex.Add(station, bindings.Count);
            bindings.Add(Configure(station));
        }
        if (KitchenInventory.Instance != null)
            foreach (IngredientStockEntry entry in KitchenInventory.Instance.stock) if (entry != null) Item(entry.item);
        foreach (var entry in cargo) Item(entry.Value.item);
        if (restoredStations.Count > 0 || restoredWorkers.Count > 0)
        {
            var referenced = new HashSet<string>();
            foreach (ProductionStationCheckpoint saved in restoredStations.Values)
            {
                foreach (ProductionStationCheckpoint.Count count in saved.inputs) referenced.Add(count.item);
                foreach (ProductionStationCheckpoint.Food food in saved.pickupFood) referenced.Add(food.item);
            }
            foreach (ProductionWorkerCheckpoint saved in restoredWorkers.Values) referenced.Add(saved.item);
            foreach (ItemDefinition definition in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                if (definition != null && referenced.Contains(definition.name)) Item(definition);
        }
        var edges = new List<Sim.Edge>();
        var workerDefs = new List<Sim.Worker>();
        foreach (ProductionFlowPlan flow in manager.productionFlows)
        {
            int fi = flowIndex[flow];
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection?.from == null || connection.to == null
                    || !stationIndex.TryGetValue(connection.from, out int from)
                    || !stationIndex.TryGetValue(connection.to, out int to) || from == to) continue;
                Sim.Node source = bindings[from].Node;
                var outputs = new List<int>(source.Kind == Sim.NodeKind.Source ? source.SourceItems : new[] { source.OutputItem });
                // A recipe change cannot change food already in a worker's hands.
                // Preserve compatible, explicitly connected cargo handoffs. Idle
                // workers still cannot take that old item from a changed source.
                foreach (var held in cargo.Values)
                    if (held.source == connection.from && itemIndex.TryGetValue(held.item, out int carried)
                        && !outputs.Contains(carried)) outputs.Add(carried);
                foreach (ProductionWorkerCheckpoint saved in restoredWorkers.Values)
                    if (saved.units > 0 && saved.sourceGuid == source.Key)
                    {
                        int carried = items.FindIndex(item => item != null && item.name == saved.item);
                        if (carried >= 0 && !outputs.Contains(carried)) outputs.Add(carried);
                    }
                foreach (int item in outputs)
                    if (item >= 0 && Accepts(bindings[to].Node, item))
                        edges.Add(new Sim.Edge { From = from, To = to, Item = item, Flow = fi });
            }
            bool productionFlow = flow.stations.Exists(s => Active(s) && stationIndex.ContainsKey(s)
                && bindings[stationIndex[s]].Node.Kind != Sim.NodeKind.Pickup);
            if (!productionFlow) continue;
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || !worker.isActiveAndEnabled || workerIndex.ContainsKey(worker)
                    || worker.GetRegisterStation() != null) continue;
                worker.AbortCurrentWork();
                workerIndex.Add(worker, workers.Count);
                workers.Add(worker);
                workerDefs.Add(new Sim.Worker { Flow = fi, Capacity = worker.CarryCapacity,
                    Origin = bindings.Count + workers.Count - 1,
                    PickupTicks = Cycle(worker.pickupTimeSeconds), PlacementTicks = Cycle(worker.placementTimeSeconds),
                    TransportTicksPerItem = Period(worker.transportItemsPerMinute) });
            }
        }
        var plan = new Sim.Definition { ItemCount = items.Count, FlowCount = flowIndex.Count,
            Nodes = bindings.ConvertAll(b => b.Node).ToArray(), Edges = edges.ToArray(), Workers = workerDefs.ToArray() };
        points = new Vector3[bindings.Count + workers.Count];
        for (int i = 0; i < bindings.Count; i++)
        {
            Vector3 p = KitchenEmployee.GetInteractionPosition(bindings[i].Object);
            if (grid != null)
            {
                // A shelf against the kitchen boundary can have its authored
                // interaction marker inside an occupied cell. Resolve the same
                // walkable fallback as GridManager before compiling routes, so
                // approaching and leaving the station share one real endpoint.
                grid.WorldToCell(p, out int x, out int y);
                p = grid.FindNearestWalkable(x, y, out int standX, out int standY)
                    ? grid.CellToWorld(standX, standY) : grid.GetCellCenter(p);
            }
            points[i] = p;
        }
        for (int i = 0; i < workers.Count; i++) points[bindings.Count + i] = workers[i].transform.position;
        BuildTravel(plan);
        simulation = new Sim(plan);
        workerDescriptions = new string[workers.Count];
        workerDescriptionKeys = new int[workers.Count];
        epoch = Time.timeAsDouble;
        for (int i = 0; i < bindings.Count; i++) ImportBuffers(i);
        for (int i = 0; i < workers.Count; i++)
        {
            KitchenEmployee worker = workers[i];
            Sim.Trip trip = simulation.Trips[i];
            if (cargo.TryGetValue(worker, out var held))
            {
                if (held.source != null && stationIndex.TryGetValue(held.source, out int source))
                {
                    trip.CargoItem = Item(held.item); trip.CargoUnits = held.units; trip.CargoSource = source;
                }
                else KitchenInventory.Instance?.AddStock(held.item, held.units);
            }
            if (restoredWorkers.TryGetValue(worker, out ProductionWorkerCheckpoint saved))
            {
                trip.ReadyAt = Math.Max(0, saved.readyDelayTicks);
                int item = items.FindIndex(x => x != null && x.name == saved.item);
                int source = bindings.FindIndex(b => b.Node.Key == saved.sourceGuid);
                if (item >= 0 && saved.units > 0)
                {
                    if (source >= 0) { trip.CargoItem = item; trip.CargoUnits = saved.units; trip.CargoSource = source; }
                    else KitchenInventory.Instance?.AddStock(items[item], saved.units);
                }
            }
        }
        // Workers moved to service/unassigned roles still own their picked cargo.
        foreach (var entry in cargo)
            if (!workerIndex.ContainsKey(entry.Key)) KitchenInventory.Instance?.AddStock(entry.Value.item, entry.Value.units);
        restoredStations.Clear(); restoredWorkers.Clear();
        forecasts = new FlowForecast[flowIndex.Count];
        for (int i = 0; i < forecasts.Length; i++) forecasts[i] = new FlowForecast();
        foreach (Sim.Worker worker in plan.Workers) forecasts[worker.Flow].Workers++;
        targetTotals = new float[flowIndex.Count];
        var countedTargets = new HashSet<(int flow, int station, int item)>();
        foreach (Sim.Edge edge in plan.Edges)
        {
            if (edge.To < 0 || edge.To >= plan.Nodes.Length
                || plan.Nodes[edge.To].Kind != Sim.NodeKind.Pickup
                || edge.From < 0 || edge.From >= plan.Nodes.Length
                || edge.Flow < 0 || edge.Flow >= targetTotals.Length) continue;
            Sim.Node source = plan.Nodes[edge.From];
            if (source.Kind != Sim.NodeKind.Processor || source.OutputItem != edge.Item
                || source.CycleTicks <= 0 || source.CycleTicks == Sim.Never
                || !countedTargets.Add((edge.Flow, edge.From, edge.Item))) continue;
            targetTotals[edge.Flow] += 60f * Sim.TicksPerSecond / source.CycleTicks;
        }
        plannedTotals = null;
        nextForecastAt = -1;
        SynchronizeExternalState();
        simulation.AdvanceTo(0);
        ApplyEvents();
        ProjectBuffers();
    }

    static bool Supported(GameObject station) => station.GetComponent<FreezerStation>() != null
        || station.GetComponent<PantryStation>() != null || station.GetComponent<GrillStation>() != null
        || station.GetComponent<FryerStation>() != null || station.GetComponent<CuttingStation>() != null
        || station.GetComponent<AssemblyStation>() != null || station.GetComponent<HeatLampStation>() != null
        || station.GetComponent<DrinkStation>() != null;

    Binding Configure(GameObject station)
    {
        var b = new Binding { Object = station, Grill = station.GetComponent<GrillStation>(),
            Fryer = station.GetComponent<FryerStation>(), Cutting = station.GetComponent<CuttingStation>(),
            Assembly = station.GetComponent<AssemblyStation>(), Freezer = station.GetComponent<FreezerStation>(),
            Pantry = station.GetComponent<PantryStation>(), Pickup = station.GetComponent<HeatLampStation>(),
            Drink = station.GetComponent<DrinkStation>(), Metrics = StationRuntimeMetrics.EnsureOn(station) };
        var n = b.Node = new Sim.Node { Key = StationNode.EnsureOn(station).StableStationId };
        var inputs = new List<Sim.Input>();
        Action<ItemDefinition, int, int> input = (item, amount, capacity) =>
        {
            int id = Item(item);
            if (id < 0) return;
            Sim.Input existing = inputs.Find(x => x.Item == id);
            if (existing != null)
            {
                existing.Amount += Mathf.Max(1, amount);
                existing.Capacity += Mathf.Max(1, capacity);
            }
            else inputs.Add(new Sim.Input { Item = id, Amount = Mathf.Max(1, amount), Capacity = capacity });
        };
        System.Action<RecipeGraphNodeDefinition, System.Func<int, int>> graphInputs = (recipe, capacityForPort) =>
        {
            if (recipe?.inputs == null) return;
            foreach (RecipeGraphInputDefinition recipeInput in recipe.inputs)
                if (recipeInput != null && recipeInput.item != null)
                    input(recipeInput.item, recipeInput.amount,
                        capacityForPort != null ? capacityForPort(recipeInput.port) : 1);
        };
        if (b.Freezer != null || b.Pantry != null)
        {
            n.Kind = Sim.NodeKind.Source;
            n.SourcePeriodTicks = Period(b.Freezer != null ? b.Freezer.EffectiveMaxOutputPerMinute : b.Pantry.EffectiveMaxOutputPerMinute);
            var outputs = new List<int>();
            int first = Item(b.Freezer != null ? b.Freezer.selectedItem : b.Pantry.selectedItem);
            if (first >= 0) outputs.Add(first);
            n.SourceItems = outputs.ToArray();
        }
        else if (b.Pickup != null)
        {
            n.Kind = Sim.NodeKind.Pickup;
            n.OutputCapacity = b.Pickup.maxCapacity;
            foreach (HeldMeal meal in b.Pickup.Meals) Item(meal.order.PrimaryItem);
        }
        else
        {
            n.Kind = Sim.NodeKind.Processor;
            if (b.Grill != null)
            {
                ItemDefinition output = b.Grill.GetSelectedOutput();
                RecipeGraphNodeDefinition recipe = manager.orderConfig != null
                    ? manager.orderConfig.GetProductionRecipe(StationType.Grill, output) : null;
                n.OutputItem = Item(output); n.OutputCapacity = b.Grill.OutputSlotCapacity;
                n.CycleTicks = Cycle(b.Grill.EffectiveProcessTimeSeconds);
                if (recipe != null)
                    graphInputs(recipe, _ => b.Grill.InputSlotCapacity);
                else
                {
                    input(b.Grill.GetSelectedInput(), 1, b.Grill.InputSlotCapacity);
                    input(b.Grill.GetSelectedSecondaryInput(), 1, b.Grill.InputSlotCapacity);
                }
            }
            else if (b.Fryer != null)
            {
                ItemDefinition output = b.Fryer.GetSelectedOutput();
                RecipeGraphNodeDefinition recipe = manager.orderConfig != null
                    ? manager.orderConfig.GetProductionRecipe(StationType.Fryer, output) : null;
                n.OutputItem = Item(output); n.OutputCapacity = b.Fryer.OutputSlotCapacity;
                n.CycleTicks = Cycle(b.Fryer.EffectiveProcessTimeSeconds);
                if (recipe != null)
                    graphInputs(recipe, _ => b.Fryer.InputSlotCapacity);
                else
                    input(b.Fryer.GetSelectedInput(), 1, b.Fryer.InputSlotCapacity);
            }
            else if (b.Cutting != null)
            {
                CuttingRecipeDefinition selected = b.Cutting.GetSelectedRecipe();
                RecipeGraphNodeDefinition recipe = manager.orderConfig != null
                    ? manager.orderConfig.GetProductionRecipe(StationType.Cutting, selected?.output) : null;
                n.OutputItem = Item(selected?.output); n.OutputCapacity = b.Cutting.OutputSlotCapacity;
                n.CycleTicks = Cycle(b.Cutting.EffectiveProcessTimeSeconds);
                if (recipe != null)
                    graphInputs(recipe, _ => b.Cutting.InputSlotCapacity);
                else
                    input(selected?.input, 1, b.Cutting.InputSlotCapacity);
            }
            else if (b.Assembly != null)
            {
                AssemblyRecipeDefinition selected = b.Assembly.GetSelectedRecipe();
                RecipeGraphNodeDefinition recipe = manager.orderConfig != null
                    ? manager.orderConfig.GetProductionRecipe(StationType.Assembly, selected?.output) : null;
                n.OutputItem = Item(selected?.output); n.OutputCapacity = b.Assembly.OutputSlotCapacity;
                n.CycleTicks = Cycle(b.Assembly.EffectiveProcessTimeSeconds);
                if (recipe != null)
                {
                    graphInputs(recipe, port => port <= 0 ? b.Assembly.ProcessedInputCapacity
                        : port == 1 ? b.Assembly.PantryInputCapacity : b.Assembly.ThirdInputCapacity);
                    bool requiresThirdInput = recipe.inputs != null
                        && recipe.inputs.Exists(recipeInput => recipeInput != null && recipeInput.port >= 2
                            && recipeInput.item != null);
                    if (requiresThirdInput && !b.Assembly.IsMk2) n.OutputItem = -1;
                }
                else if (selected != null)
                {
                    if (selected.RequiresProcessedInput) input(selected.processedInput, selected.processedInputAmount, b.Assembly.ProcessedInputCapacity);
                    input(selected.pantryInput, selected.pantryInputAmount, b.Assembly.PantryInputCapacity);
                    input(selected.thirdInput, selected.thirdInputAmount, b.Assembly.ThirdInputCapacity);
                    if (selected.RequiresMk2 && !b.Assembly.IsMk2) n.OutputItem = -1;
                }
            }
            else if (b.Drink != null)
            {
                n.OutputItem = Item(manager.orderConfig != null ? manager.orderConfig.drinkItem : null);
                n.OutputCapacity = StationBufferLimits.PerItem;
                n.StockItem = n.OutputItem;
                StationNode node = station.GetComponent<StationNode>();
                n.CycleTicks = Period(node != null ? node.outputAmountPerMinute : 60f / Mathf.Max(0.001f, b.Drink.processTimeSeconds));
            }
        }
        n.Inputs = inputs.ToArray();
        if (n.Kind == Sim.NodeKind.Processor && n.Inputs.Length == 0 && n.StockItem < 0) n.OutputItem = -1;
        MonoBehaviour component = (MonoBehaviour)b.Grill ?? (MonoBehaviour)b.Fryer ?? (MonoBehaviour)b.Cutting
            ?? (MonoBehaviour)b.Assembly ?? (MonoBehaviour)b.Freezer ?? (MonoBehaviour)b.Pantry ?? b.Drink;
        if (component != null && !component.enabled) { n.CycleTicks = Sim.Never; n.SourcePeriodTicks = Sim.Never; }
        return b;
    }

    bool Accepts(Sim.Node node, int item)
    {
        if (node.Kind == Sim.NodeKind.Pickup)
            return manager.orderConfig != null && manager.orderConfig.GetProductKind(items[item]) != CustomerOrderConfig.ProductKind.None;
        foreach (Sim.Input input in node.Inputs) if (input.Item == item && input.Capacity >= input.Amount) return true;
        return false;
    }

    void BuildTravel(Sim.Definition plan)
    {
        int count = points.Length;
        for (int wi = 0; wi < workers.Count; wi++)
        {
            Sim.Worker worker = plan.Workers[wi];
            worker.TravelTicks = new long[count, count];
            for (int i = 0; i < count; i++) for (int j = 0; j < count; j++) worker.TravelTicks[i, j] = -1;
            var locations = new HashSet<int> { worker.Origin };
            foreach (Sim.Edge edge in plan.Edges)
                if (edge.Flow == worker.Flow) { locations.Add(edge.From); locations.Add(edge.To); }
            float speed = Mathf.Max(0.1f, workers[wi].moveSpeed);
            foreach (Sim.Edge edge in plan.Edges)
            {
                if (edge.Flow != worker.Flow) continue;
                SetTravel(worker, edge.From, edge.To, speed);
                foreach (int location in locations) SetTravel(worker, location, edge.From, speed);
                SetTravel(worker, worker.Origin, edge.To, speed); // resume cargo after editing/loading
            }
        }
    }

    void SetTravel(Sim.Worker worker, int from, int to, float speed)
    {
        if (worker.TravelTicks[from, to] >= 0) return;
        if (!routes.TryGetValue((from, to), out Vector3[] path))
        {
            Vector3 a = points[from], b = points[to];
            a.y = b.y = grid != null ? grid.Origin.y : b.y;
            if (!routeCache.TryGetValue((a, b), out path))
            {
                var waypoints = grid != null ? grid.GetPath(a, b) : new List<Vector3> { a, b };
                if (waypoints.Count == 0 || FlatDistance(waypoints[waypoints.Count - 1], b) > 0.01f)
                    path = Array.Empty<Vector3>();
                else
                {
                    if (FlatDistance(waypoints[0], a) > 0.001f) waypoints.Insert(0, a);
                    path = waypoints.ToArray();
                }
                routeCache[(a, b)] = path;
            }
            routes[(from, to)] = path;
        }
        if (path.Length == 0) return;
        float distance = 0f;
        for (int i = 1; i < path.Length; i++) distance += FlatDistance(path[i - 1], path[i]);
        worker.TravelTicks[from, to] = Sim.Seconds(distance / speed);
    }

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }

    void ImportBuffers(int index)
    {
        Binding b = bindings[index];
        Sim.Buffer state = simulation.Buffers[index];
        Sim.Node n = b.Node;
        IStationBuffer physical = b.Object.GetComponent<IStationBuffer>();
        foreach (Sim.Input input in n.Inputs) state.Inputs[input.Item] = physical != null ? physical.GetInputCount(items[input.Item]) : 0;
        state.Output = n.OutputItem >= 0 && physical != null ? physical.GetOutputCount(items[n.OutputItem]) : 0;
        int processing = b.Grill != null ? b.Grill.ScheduledProcessingUnits : b.Fryer != null ? b.Fryer.ScheduledProcessingUnits
            : b.Cutting != null ? b.Cutting.ScheduledProcessingUnits : b.Assembly != null ? b.Assembly.ProcessingUnitCount : 0;
        float progress = b.Grill != null ? b.Grill.CookProgressSeconds : b.Fryer != null ? b.Fryer.ScheduledProcessingProgress
            : b.Cutting != null ? b.Cutting.ScheduledProcessingProgress : b.Assembly != null ? b.Assembly.ProcessingProgressSeconds : 0f;
        state.Processing = processing;
        state.FinishAt = processing > 0 ? Math.Max(1, n.CycleTicks - (long)(progress * Sim.TicksPerSecond)) : Sim.Never;
        if (n.Kind == Sim.NodeKind.Source && n.SourcePeriodTicks != Sim.Never)
        {
            float credits = b.Freezer != null ? b.Freezer.CaptureScheduledCredits() : b.Pantry.CaptureScheduledCredits();
            state.CreditTicks = (long)Math.Round(credits * n.SourcePeriodTicks);
        }
        if (restoredStations.TryGetValue(b.Object, out ProductionStationCheckpoint saved))
        {
            Array.Clear(state.Inputs, 0, state.Inputs.Length);
            foreach (ProductionStationCheckpoint.Count count in saved.inputs)
            {
                int id = items.FindIndex(x => x != null && x.name == count.item);
                if (id >= 0) state.Inputs[id] = Mathf.Max(0, count.units);
            }
            state.Output = saved.output;
            state.Processing = saved.processing;
            state.FinishAt = saved.processing > 0 ? Math.Max(1, saved.remainingTicks) : Sim.Never;
            state.CreditTicks = saved.creditTicks;
            if (b.Pickup != null)
                foreach (ProductionStationCheckpoint.Food food in saved.pickupFood)
                {
                    int item = items.FindIndex(x => x != null && x.name == food.item);
                    if (item < 0 || b.Pickup.StoreInput(items[item], 1) != 1) continue;
                    IReadOnlyList<HeldMeal> meals = b.Pickup.Meals;
                    meals[meals.Count - 1].placedAt = Time.time - food.age;
                }
        }
    }

    void SynchronizeExternalState()
    {
        bool changed = false;
        KitchenInventory inventory = KitchenInventory.Instance;
        for (int i = 0; i < items.Count; i++)
        {
            int count = inventory != null ? inventory.GetCount(items[i]) : int.MaxValue / 4;
            if (simulation.Stock[i] != count) { simulation.Stock[i] = count; changed = true; }
        }
        simulation.ReconcileStockReservations();
        for (int i = 0; i < bindings.Count; i++)
        {
            HeatLampStation pickup = bindings[i].Pickup;
            if (pickup == null) continue;
            Sim.Buffer state = simulation.Buffers[i];
            for (int item = 0; item < items.Count; item++)
            {
                int count = pickup.GetInputCount(items[item]);
                if (state.Inputs[item] != count) { state.Inputs[item] = count; changed = true; }
            }
        }
        if (changed) nextForecastAt = -1;
    }

    void ApplyEvents()
    {
        foreach (Sim.SimulationEvent e in simulation.Events)
        {
            if (e.Node < 0 || e.Node >= bindings.Count
                || e.Node >= simulation.Plan.Nodes.Length
                || e.Item < 0 || e.Item >= items.Count
                || e.Item >= simulation.Plan.ItemCount
                || (e.Worker >= 0 && (e.Worker >= workers.Count || e.Worker >= simulation.Trips.Length)))
            {
                configurationCheck = true;
                if (!invalidEventLogged)
                {
                    invalidEventLogged = true;
                    Debug.LogError($"Scheduled production discarded an invalid event index (kind={e.Kind}, node={e.Node}/{bindings.Count}, item={e.Item}/{items.Count}, worker={e.Worker}/{workers.Count}). The production plan will rebuild.");
                }
                continue;
            }

            Binding binding = bindings[e.Node];
            ItemDefinition item = items[e.Item];
            if (e.Kind == Sim.EventKind.ConsumeStock)
            {
                if (KitchenInventory.Instance != null && !KitchenInventory.Instance.TryConsume(item, e.Units))
                    Debug.LogError("Scheduled production inventory changed during an atomic event: " + item.name);
                if (binding.Node.Kind == Sim.NodeKind.Source) binding.Metrics?.RecordOutput(e.Units);
            }
            else if (e.Kind == Sim.EventKind.Produce) binding.Metrics?.RecordOutput(item, e.Units);
            else if (e.Kind == Sim.EventKind.Deliver)
            {
                int stored = binding.Pickup != null ? binding.Pickup.StoreInput(item, e.Units) : 0;
                if (stored != e.Units) Debug.LogError("Scheduled pickup reservation was changed outside the production scheduler.");
                if (binding.Pickup != null)
                {
                    IReadOnlyList<HeldMeal> meals = binding.Pickup.Meals;
                    for (int i = Math.Max(0, meals.Count - stored); i < meals.Count; i++)
                        meals[i].placedAt = (float)(epoch + e.At / (double)Sim.TicksPerSecond);
                }
                if (e.Worker >= 0) manager.RecordFlowCompletedOutput(workers[e.Worker], item, stored);
            }
        }
    }

    int InputCount(Sim.Buffer state, ItemDefinition item) => item != null && itemIndex.TryGetValue(item, out int id) ? state.Inputs[id] : 0;

    void ProjectBuffers()
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            Binding b = bindings[i];
            if (b.Object == null) continue;
            Sim.Buffer state = simulation.Buffers[i];
            float progress = state.Processing > 0 && state.FinishAt != Sim.Never
                ? (float)Math.Max(0, b.Node.CycleTicks - (state.FinishAt - simulation.Now)) / Sim.TicksPerSecond : 0f;
            if (b.Grill != null) b.Grill.ApplyScheduledBuffers(InputCount(state, b.Grill.GetSelectedInput()),
                InputCount(state, b.Grill.GetSelectedSecondaryInput()), state.Output, state.Processing, progress);
            else if (b.Fryer != null) b.Fryer.ApplyScheduledBuffers(InputCount(state, b.Fryer.GetSelectedInput()), state.Output, state.Processing, progress);
            else if (b.Cutting != null) b.Cutting.ApplyScheduledBuffers(InputCount(state, b.Cutting.GetSelectedRecipe()?.input), state.Output, state.Processing, progress);
            else if (b.Assembly != null)
            {
                AssemblyRecipeDefinition recipe = b.Assembly.GetSelectedRecipe();
                b.Assembly.ApplyScheduledBuffers(InputCount(state, recipe?.processedInput), InputCount(state, recipe?.pantryInput),
                    InputCount(state, recipe?.thirdInput), state.Output, state.Processing, progress);
            }
            else if (b.Node.Kind == Sim.NodeKind.Source && b.Node.SourcePeriodTicks != Sim.Never)
            {
                float credits = state.CreditTicks / (float)b.Node.SourcePeriodTicks;
                if (b.Freezer != null) b.Freezer.ApplyScheduledCredits(credits);
                else b.Pantry.ApplyScheduledCredits(credits);
            }
        }
    }

    public bool PresentWorker(KitchenEmployee worker)
    {
        if (simulation == null || !workerIndex.TryGetValue(worker, out int wi)) return false;
        Sim.Trip trip = simulation.Trips[wi];
        int target = trip.Phase == Sim.TripPhase.Approach || trip.Phase == Sim.TripPhase.Pickup ? trip.Source : trip.Destination;
        Vector3 position = Position(wi);
        float progress = trip.DueAt > trip.PhaseStarted && trip.DueAt != Sim.Never
            ? Mathf.Clamp01((float)(simulation.Now - trip.PhaseStarted) / (trip.DueAt - trip.PhaseStarted)) : 0f;
        int descriptionKey = (trip.Phase, target, trip.Item, trip.Units,
            trip.ReadyAt > simulation.Now, trip.CargoUnits > 0).GetHashCode();
        if (workerDescriptions[wi] == null || workerDescriptionKeys[wi] != descriptionKey)
        {
            string itemName = trip.Item >= 0 ? items[trip.Item].itemName : "items";
            string stationName = target >= 0 && target < bindings.Count && bindings[target].Object != null
                ? StationNode.EnsureOn(bindings[target].Object).DisplayName : "station";
            workerDescriptions[wi] = trip.Phase == Sim.TripPhase.Approach ? "Walking to " + stationName
                : trip.Phase == Sim.TripPhase.Pickup ? "Picking up " + trip.Units + " " + itemName
                : trip.Phase == Sim.TripPhase.Travel ? "Taking " + trip.Units + " " + itemName + " to " + stationName
                : trip.Phase == Sim.TripPhase.Placement ? "Delivering " + trip.Units + " " + itemName
                : trip.CargoUnits > 0 ? "Carrying items: connect a reachable destination"
                : trip.ReadyAt > simulation.Now ? "Transport pacing (" + worker.transportItemsPerMinute.ToString("0") + "/min)"
                : "Waiting for stock, processing, or free input slots";
            workerDescriptionKeys[wi] = descriptionKey;
        }
        worker.ApplyScheduledPresentation(position, trip.CargoItem >= 0 ? items[trip.CargoItem] : null,
            trip.CargoUnits, workerDescriptions[wi], trip.Phase, progress, target >= 0 && target < bindings.Count ? bindings[target].Object : null);
        return true;
    }

    Vector3 Position(int worker)
    {
        Sim.Trip trip = simulation.Trips[worker];
        Vector3 p = points[trip.Location];
        bool moving = trip.Phase == Sim.TripPhase.Approach || trip.Phase == Sim.TripPhase.Travel;
        int target = trip.Phase == Sim.TripPhase.Approach ? trip.Source : trip.Destination;
        if (moving && routes.TryGetValue((trip.Location, target), out Vector3[] path) && path.Length > 0)
        {
            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += FlatDistance(path[i - 1], path[i]);
            float fraction = (float)(simulation.Now - trip.PhaseStarted) / Math.Max(1, trip.DueAt - trip.PhaseStarted);
            float remaining = total * Mathf.Clamp01(fraction);
            p = path[path.Length - 1];
            for (int i = 1; i < path.Length; i++)
            {
                float length = FlatDistance(path[i - 1], path[i]);
                if (remaining <= length) { p = Vector3.Lerp(path[i - 1], path[i], length > 0f ? remaining / length : 1f); break; }
                remaining -= length;
            }
        }
        p.y = workers[worker].groundHeight != 0f ? workers[worker].groundHeight
            : grid != null ? grid.Origin.y : workers[worker].transform.position.y;
        return p;
    }

    public FlowForecast GetForecast(ProductionFlowPlan flow)
    {
        if (simulation == null || flow == null || !flowIndex.TryGetValue(flow, out int index)) return null;
        bool production = flow.stations.Exists(s => s != null && stationIndex.TryGetValue(s, out int id)
            && bindings[id].Node.Kind != Sim.NodeKind.Pickup);
        if (!production) return null;
        if (plannedTotals == null)
        {
            var plan = new Sim(simulation.Plan) { UnlimitedStock = true, DrainPickup = true };
            plan.AdvanceTo(120 * Sim.TicksPerSecond);
            long[] before = (long[])plan.Delivered.Clone();
            plan.AdvanceTo(720 * Sim.TicksPerSecond);
            plannedTotals = new long[before.Length];
            for (int i = 0; i < before.Length; i++) plannedTotals[i] = plan.Delivered[i] - before[i];
        }
        if (simulation.Now >= nextForecastAt || nextForecastAt < 0)
        {
            long[] counts = simulation.Forecast(60 * Sim.TicksPerSecond);
            for (int i = 0; i < forecasts.Length; i++)
            {
                forecasts[i].Next60 = counts[i];
                forecasts[i].StartsAt = simulation.Now;
                forecasts[i].TargetPerMinute = targetTotals != null && i < targetTotals.Length
                    ? targetTotals[i] : 0f;
                forecasts[i].PlannedPerMinute = plannedTotals[i] / 10f;
                forecasts[i].Warning = ForecastWarning(i);
            }
            nextForecastAt = simulation.Now + Sim.TicksPerSecond;
        }
        return forecasts[index];
    }

    string ForecastWarning(int flow)
    {
        if (forecasts[flow].Workers == 0) return "Assign a production worker.";
        bool hasPickup = false;
        foreach (Sim.Edge edge in simulation.Plan.Edges)
        {
            if (edge.Flow != flow) continue;
            hasPickup |= simulation.Plan.Nodes[edge.To].Kind == Sim.NodeKind.Pickup;
            bool reachable = false;
            foreach (Sim.Worker worker in simulation.Plan.Workers)
                if (worker.Flow == flow && worker.TravelTicks[edge.From, edge.To] >= 0) { reachable = true; break; }
            if (!reachable) return "A flow node is unreachable. Clear its route or move the station.";
        }
        if (!hasPickup) return "Connect the final product to a Pickup Station.";
        if (plannedTotals[flow] == 0)
        {
            foreach (Sim.Edge terminal in simulation.Plan.Edges)
            {
                if (terminal.Flow != flow || simulation.Plan.Nodes[terminal.To].Kind != Sim.NodeKind.Pickup)
                    continue;
                Sim.Node producer = simulation.Plan.Nodes[terminal.From];
                foreach (Sim.Input input in producer.Inputs)
                {
                    bool connected = false;
                    foreach (Sim.Edge incoming in simulation.Plan.Edges)
                        if (incoming.Flow == flow && incoming.To == terminal.From && incoming.Item == input.Item)
                        { connected = true; break; }
                    if (!connected)
                    {
                        string itemName = input.Item >= 0 && input.Item < items.Count
                            ? items[input.Item].itemName : "required item";
                        string stationName = bindings[terminal.From].Object != null
                            ? StationNode.EnsureOn(bindings[terminal.From].Object).DisplayName : "the final station";
                        return "Connect " + itemName + " to " + stationName + ".";
                    }
                }
            }
            return "No output is scheduled. Check station targets and supply connections.";
        }
        float gap = forecasts[flow].TargetPerMinute - forecasts[flow].PlannedPerMinute;
        if (gap > 0.1f)
            return "Short by " + gap.ToString("0.0") + " items/min. Check worker coverage and upstream supply.";
        return null;
    }

    public ProductionStationCheckpoint CaptureStation(GameObject station)
    {
        if (simulation == null || !stationIndex.TryGetValue(station, out int index)) return null;
        Sim.Buffer buffer = simulation.Buffers[index];
        var saved = new ProductionStationCheckpoint { output = buffer.Output, processing = buffer.Processing,
            remainingTicks = buffer.Processing > 0 ? Math.Max(1, buffer.FinishAt - simulation.Now) : 0,
            creditTicks = buffer.CreditTicks };
        for (int i = 0; i < items.Count; i++) if (buffer.Inputs[i] > 0)
            saved.inputs.Add(new ProductionStationCheckpoint.Count { item = items[i].name, units = buffer.Inputs[i] });
        HeatLampStation pickup = bindings[index].Pickup;
        if (pickup != null) foreach (HeldMeal meal in pickup.Meals)
            if (meal?.order?.PrimaryItem != null) saved.pickupFood.Add(new ProductionStationCheckpoint.Food
                { item = meal.order.PrimaryItem.name, age = meal.AgeSeconds });
        return saved;
    }

    public ProductionWorkerCheckpoint CaptureWorker(KitchenEmployee worker)
    {
        if (simulation == null || !workerIndex.TryGetValue(worker, out int index)) return null;
        Sim.Trip trip = simulation.Trips[index];
        return new ProductionWorkerCheckpoint { item = trip.CargoItem >= 0 ? items[trip.CargoItem].name : "",
            units = trip.CargoUnits, sourceGuid = trip.CargoSource >= 0 ? bindings[trip.CargoSource].Node.Key : "",
            readyDelayTicks = Math.Max(0, trip.ReadyAt - simulation.Now) };
    }

    public void RestoreStation(GameObject station, ProductionStationCheckpoint saved)
    {
        if (station != null && saved != null) restoredStations[station] = saved;
        simulation = null;
    }

    public void RestoreWorker(KitchenEmployee worker, ProductionWorkerCheckpoint saved)
    {
        if (worker != null && saved != null) restoredWorkers[worker] = saved;
        simulation = null;
    }
}
