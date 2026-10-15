using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ProductionFlowConnection
{
    public GameObject from;
    public GameObject to;

    public ProductionFlowConnection(GameObject from, GameObject to)
    {
        this.from = from;
        this.to = to;
    }
}

[System.Serializable]
public class ProductionFlowPlan
{
    public const int MaxNameLength = 15;

    public string flowName = "Flow";
    public KitchenFlowKind kind = KitchenFlowKind.Custom;
    // Type codes are intentionally allowed to repeat. The station GameObject
    // list and graph connections hold the unique identity of each machine.
    public List<string> stepIds = new List<string>();
    public List<GameObject> stations = new List<GameObject>();
    public List<ProductionFlowConnection> connections = new List<ProductionFlowConnection>();
    public bool graphInitialized;
    public List<KitchenEmployee> workers = new List<KitchenEmployee>();

    public void Clean()
    {
        flowName = NormalizeName(flowName, "Flow");
        if (stepIds == null) stepIds = new List<string>();
        if (stations == null) stations = new List<GameObject>();
        stations.RemoveAll(station => station == null);
        // A flow may contain multiple instances of the same station type and
        // recipe. Keep those distinct GameObjects, while removing repeated
        // references to the very same physical station.
        for (int i = stations.Count - 1; i >= 0; i--)
        {
            for (int previous = 0; previous < i; previous++)
            {
                if (stations[previous] != stations[i]) continue;
                stations.RemoveAt(i);
                break;
            }
        }
        if (connections == null) connections = new List<ProductionFlowConnection>();
        connections.RemoveAll(connection => connection == null || connection.from == null || connection.to == null
            || connection.from == connection.to || !stations.Contains(connection.from) || !stations.Contains(connection.to));
        var seenConnections = new HashSet<(GameObject, GameObject)>();
        connections.RemoveAll(connection => !seenConnections.Add((connection.from, connection.to)));
        if (workers == null) workers = new List<KitchenEmployee>();
        workers.RemoveAll(worker => worker == null);
    }

    public void EnsureLegacyConnections()
    {
        Clean();
        if (graphInitialized) return;
        graphInitialized = true;
        if (connections.Count > 0 || stations.Count < 2) return;
        for (int i = 0; i + 1 < stations.Count; i++)
            connections.Add(new ProductionFlowConnection(stations[i], stations[i + 1]));
    }

    public List<GameObject> GetOutgoing(GameObject station)
    {
        var result = new List<GameObject>();
        GetOutgoing(station, result);
        return result;
    }

    public void GetOutgoing(GameObject station, List<GameObject> result)
    {
        if (result == null) return;
        result.Clear();
        if (station == null) return;
        if (!graphInitialized)
            EnsureLegacyConnections();
        if (connections == null || stations == null || !stations.Contains(station)) return;
        for (int i = 0; i < connections.Count; i++)
        {
            ProductionFlowConnection connection = connections[i];
            if (connection == null || connection.from != station || connection.to == null
                || !stations.Contains(connection.to) || result.Contains(connection.to))
                continue;
            result.Add(connection.to);
        }
    }

    public bool HasConnection(GameObject from, GameObject to)
    {
        if (from == null || to == null || connections == null) return false;
        return connections.Exists(connection => connection != null && connection.from == from && connection.to == to);
    }

    public bool AddConnection(GameObject from, GameObject to)
    {
        if (from == null || to == null || from == to || HasConnection(from, to)) return false;
        graphInitialized = true;
        if (!stations.Contains(from)) stations.Add(from);
        if (!stations.Contains(to)) stations.Add(to);
        connections.Add(new ProductionFlowConnection(from, to));
        return true;
    }

    public void SetName(string value)
    {
        flowName = NormalizeName(value, "Unnamed Flow");
    }

    public static string NormalizeName(string value, string fallback)
    {
        string clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= MaxNameLength ? clean : clean.Substring(0, MaxNameLength);
    }
}

public sealed class TeamBalanceResult
{
    public bool success;
    public string message;
    public float estimatedCycleSeconds;
    public float estimatedThroughputPerMinute;
    public int handoffs;
    public string bottleneck;
    public readonly List<string> assignments = new List<string>();
}

public enum KitchenFlowKind
{
    None = 0,
    BurgerLine = 1,
    FriesLine = 2,
    Register = 3,
    Drinks = 4,
    Custom = 5
}

public sealed class FlowStationDef
{
    public readonly string id;
    public readonly string label;
    public readonly System.Type componentType;

    public FlowStationDef(string id, string label, System.Type componentType)
    {
        this.id = id;
        this.label = label;
        this.componentType = componentType;
    }
}

/// <summary>
/// Assigns a worker to a station chain and wires outputs so they walk it automatically.
/// </summary>
public static class WorkerFlowAssigner
{
    public const int MaxSteps = 16;

    public static readonly FlowStationDef[] Catalog =
    {
        new FlowStationDef("Freezer", "Freezer", typeof(FreezerStation)),
        new FlowStationDef("Grill", "Grill", typeof(GrillStation)),
        new FlowStationDef("Shake", "Shake Station", typeof(ShakeStation)),
        new FlowStationDef("Assembly", "Assembly", typeof(AssemblyStation)),
        new FlowStationDef("Cutting", "Cutting Station", typeof(CuttingStation)),
        new FlowStationDef("Fryer", "Fryer", typeof(FryerStation)),
        new FlowStationDef("Register", "Register", typeof(Register)),
        new FlowStationDef("HeatLamp", "Pickup Station", typeof(HeatLampStation)),
        new FlowStationDef("Pantry", "Pantry", typeof(PantryStation))
    };

    public static string GetTitle(KitchenFlowKind kind)
    {
        switch (kind)
        {
            case KitchenFlowKind.Custom: return "Custom";
            default: return "Flow";
        }
    }

    public static string GetPathLabel(KitchenFlowKind kind)
    {
        return FormatSteps(GetPresetSteps(kind));
    }

    /// <summary>Presets removed — flows are built by clicking stations.</summary>
    public static List<string> GetPresetSteps(KitchenFlowKind kind)
    {
        return new List<string>();
    }

    public static string FormatSteps(IList<string> stepIds)
    {
        if (stepIds == null || stepIds.Count == 0)
            return "No path";
        var labels = new List<string>(stepIds.Count);
        for (int i = 0; i < stepIds.Count; i++)
        {
            var def = FindDef(stepIds[i]);
            labels.Add(def != null ? def.label : stepIds[i]);
        }
        return string.Join(" → ", labels);
    }

    public static string FormatFlow(ProductionFlowPlan flow)
    {
        if (flow == null) return "No flow selected";
        flow.EnsureLegacyConnections();
        if (flow.connections.Count > 0)
        {
            var edges = new List<string>();
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection?.from == null || connection.to == null) continue;
                StationNode fromNode = StationNode.EnsureOn(connection.from);
                StationNode toNode = StationNode.EnsureOn(connection.to);
                string from = fromNode != null ? fromNode.DisplayName : connection.from.name;
                string to = toNode != null ? toNode.DisplayName : connection.to.name;
                edges.Add(from + " → " + to);
            }
            if (edges.Count > 0)
                return string.Join(" | ", edges);
        }
        if (flow.stations.Count > 0)
        {
            var labels = new List<string>();
            foreach (GameObject station in flow.stations)
            {
                StationNode node = StationNode.EnsureOn(station);
                labels.Add(node != null ? node.DisplayName : station.name);
            }
            return string.Join(" → ", labels);
        }
        return FormatSteps(flow.stepIds);
    }

    public static FlowStationDef FindDef(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < Catalog.Length; i++)
        {
            if (Catalog[i].id == id)
                return Catalog[i];
        }
        return null;
    }

    public static List<FlowStationDef> GetStationsInScene()
    {
        var list = new List<FlowStationDef>();
        for (int i = 0; i < Catalog.Length; i++)
        {
            if (Object.FindObjectsByType(Catalog[i].componentType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0)
                list.Add(Catalog[i]);
        }
        return list;
    }

    public static string DescribeMissing(IList<string> stepIds)
    {
        var missing = new List<string>();
        if (stepIds == null) return "";
        for (int i = 0; i < stepIds.Count; i++)
        {
            var def = FindDef(stepIds[i]);
            if (def == null) continue;
            if (Object.FindObjectsByType(def.componentType, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                missing.Add(def.label);
        }
        if (missing.Count == 0) return "";
        return "Need in kitchen: " + string.Join(", ", missing);
    }

    public static string DescribeMissing(KitchenFlowKind kind)
    {
        return DescribeMissing(GetPresetSteps(kind));
    }

    public static bool Apply(KitchenEmployee emp, KitchenFlowKind kind)
    {
        return Apply(emp, GetPresetSteps(kind), kind);
    }

    public static bool Apply(KitchenEmployee emp, IList<string> stepIds, KitchenFlowKind kind)
    {
        if (emp == null) return false;
        emp.ClearAllOperatedStations();
        if (stepIds == null || stepIds.Count == 0 || kind == KitchenFlowKind.None)
        {
            emp.assignedFlow = KitchenFlowKind.None;
            return true;
        }

        var types = new List<System.Type>();
        for (int i = 0; i < stepIds.Count; i++)
        {
            var def = FindDef(stepIds[i]);
            if (def == null) return Fail(emp);
            types.Add(def.componentType);
        }

        bool ok = ApplyChain(emp, kind, types);
        if (!ok)
            emp.assignedFlow = KitchenFlowKind.None;
        return ok;
    }

    public static int ApplyToIdleWorkers(IList<string> stepIds, KitchenFlowKind kind)
    {
        var production = ProductionManager.Instance;
        if (production == null || production.employees == null) return 0;
        int applied = 0;
        foreach (var emp in production.employees)
        {
            if (emp == null || emp.OperatedStationCount > 0) continue;
            if (Apply(emp, stepIds, kind))
                applied++;
        }
        return applied;
    }

    public static int ApplyToIdleWorkers(KitchenFlowKind kind)
    {
        return ApplyToIdleWorkers(GetPresetSteps(kind), kind);
    }

    static bool Fail(KitchenEmployee emp)
    {
        emp.assignedFlow = KitchenFlowKind.None;
        return false;
    }

    static bool ApplyChain(KitchenEmployee emp, KitchenFlowKind kind, List<System.Type> stationTypes)
    {
        var nodes = new List<GameObject>();
        for (int i = 0; i < stationTypes.Count; i++)
        {
            var go = FindOpenStation(stationTypes[i], emp);
            if (go == null)
                return false;
            nodes.Add(go);
        }

        ConfigureProducts(kind, nodes);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = StationNode.EnsureOn(nodes[i]);
            bool isHeatLamp = nodes[i].GetComponent<HeatLampStation>() != null;
            if (!isHeatLamp)
                node.AddWorker(emp);
        }

        emp.assignedFlow = kind;
        emp.SyncFromOperatedStations();
        return emp.CanTakeJobs || kind == KitchenFlowKind.None;
    }

    static void ConfigureProducts(KitchenFlowKind kind, List<GameObject> nodes)
    {
        // Wire burger product onto grill/assembly whenever those stations are on the route.
        var grillOutput = ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null
            ? ProductionManager.Instance.orderConfig.cookedPattyIngredient
            : null;
        var burgerItem = ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null
            ? ProductionManager.Instance.orderConfig.burgerBase
            : null;
        if (burgerItem == null || nodes == null) return;

        foreach (var go in nodes)
        {
            if (go == null) continue;
            var grill = go.GetComponent<GrillStation>();
            if (grill != null && !grill.HasProductSelected && grillOutput != null)
                grill.SetRecipeOutput(grillOutput);
            var assembly = go.GetComponent<AssemblyStation>();
            if (assembly != null && !assembly.HasProductSelected)
                assembly.SetRecipe(ProductionManager.Instance.orderConfig.GetAssemblyRecipe(burgerItem));
        }
    }

    static void ConfigureAssemblyRecipesFromConnections(ProductionFlowPlan flow)
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig
            : null;
        if (flow == null || config == null || flow.connections == null
            || flow.connections.Count == 0 || flow.stations == null)
            return;

        // Resolve connected assembly stages in route order, repeating so an
        // upstream assembly can expose its output to a later assembly stage.
        for (int pass = 0; pass < flow.stations.Count; pass++)
        {
            bool changed = false;
            foreach (GameObject station in flow.stations)
            {
                AssemblyStation assembly = station != null
                    ? station.GetComponent<AssemblyStation>()
                    : null;
                if (assembly == null || assembly.BufferedProcessedInputCount > 0
                    || assembly.BufferedPantryInputCount > 0 || assembly.BufferedThirdInputCount > 0
                    || assembly.BufferedOutputCount > 0 || assembly.ProcessingUnitCount > 0)
                    continue;

                var connectedInputs = new HashSet<ItemDefinition>();
                foreach (ProductionFlowConnection connection in flow.connections)
                {
                    if (connection == null || connection.to != station || connection.from == null)
                        continue;
                    AddConnectedStationProducts(connection.from, connectedInputs);
                }
                if (connectedInputs.Count == 0)
                    continue;

                AssemblyRecipeDefinition match = null;
                bool ambiguous = false;
                foreach (AssemblyRecipeDefinition candidate in config.GetAssemblyRecipes())
                {
                    if (!AllRecipeInputsConnected(candidate, connectedInputs))
                        continue;
                    if (match != null && match != candidate)
                    {
                        ambiguous = true;
                        break;
                    }
                    match = candidate;
                }

                if (ambiguous || match == null || assembly.GetSelectedRecipe() == match)
                    continue;

                assembly.SetRecipe(match);
                changed = true;
            }

            if (!changed)
                break;
        }
    }

    static bool AllRecipeInputsConnected(AssemblyRecipeDefinition recipe,
        HashSet<ItemDefinition> connectedInputs)
    {
        if (recipe == null || connectedInputs == null)
            return false;
        return (recipe.processedInput == null || connectedInputs.Contains(recipe.processedInput))
            && (recipe.pantryInput == null || connectedInputs.Contains(recipe.pantryInput))
            && (recipe.thirdInput == null || connectedInputs.Contains(recipe.thirdInput));
    }

    static void AddConnectedStationProducts(GameObject source, HashSet<ItemDefinition> products)
    {
        if (source == null || products == null)
            return;

        PantryStation pantry = source.GetComponent<PantryStation>();
        if (pantry != null)
        {
            if (pantry.selectedItem != null) products.Add(pantry.selectedItem);
            if (pantry.secondItem != null) products.Add(pantry.secondItem);
            return;
        }

        FreezerStation freezer = source.GetComponent<FreezerStation>();
        if (freezer != null)
        {
            if (freezer.selectedItem != null) products.Add(freezer.selectedItem);
            return;
        }

        GrillStation grill = source.GetComponent<GrillStation>();
        if (grill != null)
        {
            ItemDefinition output = grill.GetSelectedOutput();
            if (output != null) products.Add(output);
            return;
        }

        FryerStation fryer = source.GetComponent<FryerStation>();
        if (fryer != null)
        {
            ItemDefinition output = fryer.GetSelectedOutput();
            if (output != null) products.Add(output);
            return;
        }

        CuttingStation cutting = source.GetComponent<CuttingStation>();
        if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            ItemDefinition output = recipe != null ? recipe.output : cutting.selectedProduct;
            if (output != null) products.Add(output);
            return;
        }

        AssemblyStation upstreamAssembly = source.GetComponent<AssemblyStation>();
        if (upstreamAssembly != null)
        {
            AssemblyRecipeDefinition recipe = upstreamAssembly.GetSelectedRecipe();
            ItemDefinition output = recipe != null ? recipe.output : upstreamAssembly.selectedProduct;
            if (output != null) products.Add(output);
        }
    }

    static GameObject FindOpenStation(System.Type componentType, KitchenEmployee emp)
    {
        var found = Object.FindObjectsByType(componentType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var obj in found)
        {
            var component = obj as Component;
            if (component == null) continue;
            var go = component.gameObject;
            if (go.GetComponent<HeatLampStation>() != null)
                return go;

            var register = go.GetComponent<Register>();
            if (register != null && !register.isEnabled)
                continue;

            return go;
        }
        return null;
    }

    public static string GetStationId(GameObject station)
    {
        if (station == null) return null;
        for (int i = 0; i < Catalog.Length; i++)
            if (station.GetComponent(Catalog[i].componentType) != null)
                return Catalog[i].id;
        return null;
    }

    /// <summary>
    /// Keep stations and stepIds aligned. Stations are the source of truth when present;
    /// otherwise try to resolve GameObjects from stepIds so Edit Flow can continue the route.
    /// </summary>
    public static void SynchronizeFlowRoute(ProductionFlowPlan flow)
    {
        if (flow == null) return;
        flow.Clean();

        if (flow.stations.Count > 0)
        {
            RebuildStepIdsFromStations(flow);
            return;
        }

        if (flow.stepIds == null || flow.stepIds.Count == 0)
            return;

        var used = new HashSet<GameObject>();
        var resolved = new List<GameObject>(flow.stepIds.Count);
        for (int i = 0; i < flow.stepIds.Count; i++)
        {
            FlowStationDef def = FindDef(flow.stepIds[i]);
            if (def == null)
            {
                flow.stepIds.Clear();
                flow.stations.Clear();
                return;
            }

            GameObject station = FindStationForStep(def.componentType, used, flow);
            if (station == null)
            {
                // Can't rebuild the old route — clear so Edit starts clean instead of appending onto stale stepIds.
                flow.stepIds.Clear();
                flow.stations.Clear();
                return;
            }

            resolved.Add(station);
            used.Add(station);
        }

        flow.stations = resolved;
        RebuildStepIdsFromStations(flow);
    }

    public static void RebuildStepIdsFromStations(ProductionFlowPlan flow)
    {
        if (flow == null) return;
        if (flow.stepIds == null) flow.stepIds = new List<string>();
        flow.stepIds.Clear();
        if (flow.stations == null) return;
        for (int i = 0; i < flow.stations.Count; i++)
        {
            string id = GetStationId(flow.stations[i]);
            if (!string.IsNullOrEmpty(id))
                flow.stepIds.Add(id);
        }
    }

    static GameObject FindStationForStep(System.Type componentType, HashSet<GameObject> used, ProductionFlowPlan ownerFlow)
    {
        Object[] found = Object.FindObjectsByType(componentType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        ProductionManager production = ProductionManager.Instance;

        // Prefer stations not claimed by another flow.
        foreach (Object obj in found)
        {
            Component component = obj as Component;
            if (component == null || used.Contains(component.gameObject)) continue;
            GameObject station = component.gameObject;
            Register register = station.GetComponent<Register>();
            if (register != null && !register.isEnabled) continue;
            if (production != null && production.IsStationOnOtherFlow(station, ownerFlow))
                continue;
            return station;
        }

        // Fallback: any matching unused station.
        foreach (Object obj in found)
        {
            Component component = obj as Component;
            if (component == null || used.Contains(component.gameObject)) continue;
            GameObject station = component.gameObject;
            Register register = station.GetComponent<Register>();
            if (register != null && !register.isEnabled) continue;
            return station;
        }

        return null;
    }

    /// <summary>
    /// Wires one player-defined route and assigns every worker to every labor station.
    /// Runtime job claims and reservations prevent workers from duplicating the same task.
    /// </summary>
    public static TeamBalanceResult ApplyBalancedTeam(ProductionFlowPlan flow)
    {
        var result = new TeamBalanceResult();
        if (flow == null)
        {
            result.message = "No flow selected.";
            return result;
        }

        flow.Clean();
        if (flow.stepIds.Count == 0 && flow.stations.Count == 0)
        {
            result.message = "Add at least one station to the flow.";
            return result;
        }
        flow.EnsureLegacyConnections();
        string orderProblem = flow.connections.Count > 0 ? "" : ValidateStepOrder(flow.stepIds);
        if (!string.IsNullOrEmpty(orderProblem))
        {
            result.message = orderProblem;
            return result;
        }
        if (flow.workers.Count == 0)
        {
            result.message = "Assign at least one worker to this flow.";
            return result;
        }

        var team = new HashSet<KitchenEmployee>(flow.workers);
        var route = new List<GameObject>();
        if (flow.stations.Count > 0)
        {
            foreach (GameObject station in flow.stations)
            {
                if (station == null) continue;
                route.Add(station);
            }
        }
        else
        {
            var used = new HashSet<GameObject>();
            foreach (string stepId in flow.stepIds)
            {
                FlowStationDef def = FindDef(stepId);
                if (def == null)
                {
                    result.message = "Unknown station in flow: " + stepId;
                    return result;
                }

                GameObject station = FindStationForTeam(def.componentType, team, used);
                if (station == null)
                {
                    result.message = "No available " + def.label + " station. Another flow may own it.";
                    return result;
                }
                route.Add(station);
                used.Add(station);
            }
            flow.stations = new List<GameObject>(route);
        }

        var workStations = new List<GameObject>();
        foreach (GameObject station in route)
        {
            if (station == null) continue;
            // Heat lamps are shared pass-throughs — never assign labor to them.
            if (station.GetComponent<HeatLampStation>() != null) continue;
            if (StationNode.EnsureOn(station).IsWorkStation)
                workStations.Add(station);
        }

        if (workStations.Count == 0)
        {
            result.message = "This route has no labor station.";
            return result;
        }

        int activeWorkers = flow.workers.Count;

        foreach (KitchenEmployee worker in flow.workers)
            if (worker != null)
                worker.ClearAllOperatedStations();

        ConfigureProducts(flow.kind, route);
        ConfigureAssemblyRecipesFromConnections(flow);
        // Every worker assigned to a flow may operate every work station in it.
        // Jobs and station reservations coordinate multiple workers at runtime.
        for (int workerIndex = 0; workerIndex < activeWorkers; workerIndex++)
        {
            KitchenEmployee worker = flow.workers[workerIndex];
            if (worker == null) continue;
            var stationNames = new List<string>();
            for (int j = 0; j < workStations.Count; j++)
            {
                StationNode node = StationNode.EnsureOn(workStations[j]);
                node.AddWorker(worker);
                stationNames.Add(node.DisplayName);
            }
            worker.assignedFlow = flow.kind;
            worker.assignedFlowName = flow.flowName;
            worker.SyncFromOperatedStations();
            result.assignments.Add(worker.employeeName + ": " + string.Join(" → ", stationNames));
        }

        result.estimatedCycleSeconds = 0f;
        for (int i = 0; i < activeWorkers; i++)
            result.estimatedCycleSeconds = Mathf.Max(result.estimatedCycleSeconds,
                WorkflowAnalysis.GetEstimatedCycleSeconds(flow.workers[i]));
        float stationBottleneck = WorkflowAnalysis.GetFlowBottleneckOutputPerMinute(flow);
        result.estimatedThroughputPerMinute = stationBottleneck > 0.01f
            ? stationBottleneck
            : (result.estimatedCycleSeconds > 0f ? 60f / result.estimatedCycleSeconds : 0f);
        if (stationBottleneck > 0.01f)
            result.estimatedCycleSeconds = 60f / stationBottleneck;
        result.handoffs = Mathf.Max(0, activeWorkers - 1);
        GameObject slowest = workStations[0];
        foreach (GameObject station in workStations)
            if (WorkflowAnalysis.GetStationWorkSeconds(station) > WorkflowAnalysis.GetStationWorkSeconds(slowest))
                slowest = station;
        result.bottleneck = StationNode.EnsureOn(slowest).DisplayName;
        result.success = true;
        result.message = "Every worker can operate every station in the flow.";
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
        StationOutputLinkVisuals.NotifyLinksChanged();
        return result;
    }

    public static string ValidateStepOrder(IList<string> steps)
    {
        int freezer = IndexOf(steps, "Freezer");
        int grill = IndexOf(steps, "Grill");
        int assembly = IndexOf(steps, "Assembly");
        int heatLamp = IndexOf(steps, "HeatLamp");
        if (freezer >= 0 && grill >= 0 && freezer > grill)
            return "Invalid product route: Freezer must come before Grill.";
        if (grill >= 0 && assembly >= 0 && grill > assembly)
            return "Invalid product route: Grill must come before Assembly.";
        if (heatLamp >= 0 && heatLamp != steps.Count - 1)
            return "Pickup Station must be the final stop.";
        return "";
    }

    static int IndexOf(IList<string> steps, string id)
    {
        for (int i = 0; i < steps.Count; i++)
            if (steps[i] == id) return i;
        return -1;
    }

    static GameObject FindStationForTeam(System.Type componentType, HashSet<KitchenEmployee> team, HashSet<GameObject> used)
    {
        Object[] found = Object.FindObjectsByType(componentType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Object obj in found)
        {
            Component component = obj as Component;
            if (component == null || used.Contains(component.gameObject)) continue;
            GameObject station = component.gameObject;
            Register register = station.GetComponent<Register>();
            if (register != null && !register.isEnabled) continue;
            StationNode node = StationNode.EnsureOn(station);
            if (node.IsWorkStation)
                return station;
        }
        return null;
    }
}

/// <summary>
/// Lightweight ISE decision support for workflow assignment. Estimates combine station
/// capacity with trained item handling and the walking time for required transfers.
/// </summary>
public static class WorkflowAnalysis
{
    public sealed class FlowLayoutMetrics
    {
        public float routeTiles;
        public float travelSeconds;
        public float workerTravelSecondsPerMinute;
        public float stationWorkSeconds;
        /// <summary>Station item throughput required to deliver max flow output at Pickup.</summary>
        public float totalStationOutputPerMinute;
        public float stationCapacityPerMinute;
        public float laborCapacityPerMinute;
        public float travelOverheadPerMinute;
        public float workerOperationSecondsPerMinute;
        public float workerOperationOverheadPerMinute;
        public float travelOverheadPerTilePerMinute;
        public float totalRateNeededPerMinute;
        public float transportShortfallPerMinute;
        public float effectiveOutputPerMinute;
        public bool usesScheduledForecast;
        public long next60Output;
        public string forecastWarning;
        public float effectiveCycleSeconds;
        public bool hasAssignedWorker;
        public int activeWorkerCount;
        public int baseWorkersNeeded;
        public int minimumCarryCapacity = 1;
        public int maximumCarryCapacity = 1;
        public string bottleneck = "None";
        public string stationBottleneckName = "";
        public float layoutEfficiencyPercent;
        public float runtimeWorkingPercent;
        public float runtimeWaitPercent;
        /// <summary>Output observed at pickup stations during the trailing minute.</summary>
        public float actualOutputPerMinute;
        public float throughputEfficiencyPercent;
    }

    public static List<GameObject> GetOrderedRoute(KitchenEmployee employee)
    {
        var route = new List<GameObject>();
        if (employee == null || employee.operatedStations == null || employee.operatedStations.Count == 0)
            return route;

        ProductionManager production = ProductionManager.Instance;
        ProductionFlowPlan flow = production != null ? production.GetFlowForWorker(employee) : null;
        if (flow?.stations != null && flow.stations.Count > 0)
        {
            flow.EnsureLegacyConnections();
            var assigned = new HashSet<GameObject>(employee.operatedStations);
            var included = new HashSet<GameObject>();
            var incoming = new HashSet<GameObject>();
            foreach (ProductionFlowConnection connection in flow.connections)
                if (connection?.from != null && connection.to != null && assigned.Contains(connection.from))
                    incoming.Add(connection.to);

            foreach (GameObject station in flow.stations)
                if (station != null && assigned.Contains(station) && !incoming.Contains(station))
                    AppendWorkerGraphRoute(flow, station, assigned, included, route);

            foreach (GameObject station in flow.stations)
                if (station != null && assigned.Contains(station) && !included.Contains(station))
                    AppendWorkerGraphRoute(flow, station, assigned, included, route);

            if (route.Count > 0)
                return route;
        }

        GameObject current = FindRouteHead(employee.operatedStations);
        var visited = new HashSet<GameObject>();
        while (current != null && visited.Add(current))
        {
            route.Add(current);
            StationNode node = StationNode.EnsureOn(current);
            GameObject next = node != null ? node.outputTarget : null;
            StationNode nextNode = next != null ? StationNode.EnsureOn(next) : null;
            if (nextNode != null && nextNode.IsWorkStation && !nextNode.IsWorkerAssigned(employee))
            {
                route.Add(next);
                break;
            }
            current = next;
        }
        return route;
    }

    static void AppendWorkerGraphRoute(ProductionFlowPlan flow, GameObject station,
        HashSet<GameObject> assigned, HashSet<GameObject> included, List<GameObject> route)
    {
        if (station == null || !included.Add(station)) return;
        route.Add(station);
        foreach (GameObject next in flow.GetOutgoing(station))
        {
            if (next == null) continue;
            if (assigned.Contains(next))
                AppendWorkerGraphRoute(flow, next, assigned, included, route);
            else if (!included.Contains(next))
            {
                // Include the first handoff destination so return and distance
                // calculations end where this worker actually delivers.
                included.Add(next);
                route.Add(next);
            }
        }
    }

    static GameObject FindRouteHead(List<GameObject> assigned)
    {
        foreach (GameObject candidate in assigned)
        {
            if (candidate == null) continue;
            bool isTarget = false;
            foreach (GameObject other in assigned)
            {
                if (other == null || other == candidate) continue;
                StationNode otherNode = StationNode.EnsureOn(other);
                if (otherNode != null && otherNode.outputTarget == candidate)
                {
                    isTarget = true;
                    break;
                }
            }
            if (!isTarget) return candidate;
        }
        return assigned[0];
    }

    public static float GetRouteDistanceTiles(KitchenEmployee employee)
    {
        if (employee == null) return 0f;
        GridManager grid = employee.grid != null ? employee.grid : GridManager.Instance;
        if (grid == null) return 0f;

        ProductionManager production = ProductionManager.Instance;
        ProductionFlowPlan flow = production != null ? production.GetFlowForWorker(employee) : null;
        if (flow != null)
        {
            flow.EnsureLegacyConnections();
            var assigned = new HashSet<GameObject>(employee.operatedStations);
            float graphTiles = 0f;
            bool foundEdge = false;
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection?.from == null || connection.to == null || !assigned.Contains(connection.from))
                    continue;
                graphTiles += GetDistanceTiles(grid, connection.from, connection.to);
                foundEdge = true;
            }
            if (foundEdge) return graphTiles;
        }

        List<GameObject> route = GetOrderedRoute(employee);
        float tiles = 0f;
        for (int i = 1; i < route.Count; i++)
            tiles += GetDistanceTiles(grid, route[i - 1], route[i]);
        return tiles;
    }

    public static float GetEstimatedCycleSeconds(KitchenEmployee employee)
    {
        return GetEstimatedWorkSeconds(employee) + GetEstimatedTravelSeconds(employee);
    }

    public static float GetEstimatedWorkSeconds(KitchenEmployee employee)
    {
        if (employee == null) return 0f;
        float work = 0f;
        foreach (GameObject station in GetOrderedRoute(employee))
        {
            StationNode node = StationNode.EnsureOn(station);
            if (node == null || !node.IsWorkStation || node.IsWorkerAssigned(employee))
                work += GetStationWorkSeconds(station);
        }
        return work;
    }

    public static float GetEstimatedTravelSeconds(KitchenEmployee employee)
    {
        if (employee == null) return 0f;
        return GetRouteDistanceTiles(employee) * GetSecondsPerTravelTile(employee);
    }

    public static float GetSecondsPerTravelTile(KitchenEmployee employee)
    {
        if (employee == null) return 0f;
        GridManager grid = employee.grid != null ? employee.grid : GridManager.Instance;
        float cellSize = grid != null ? grid.cellSize : 1f;
        return cellSize / Mathf.Max(0.1f, employee.moveSpeed);
    }

    /// <summary>
    /// Trained maximum transport rate for one worker. Route travel is included in
    /// the system's required rate, so it must not reduce this cap a second time.
    /// </summary>
    public static float GetEstimatedDeliveryPerMinute(KitchenEmployee employee)
    {
        return GetEstimatedDeliveryPerMinute(employee, employee != null ? employee.UpgradeLevel : 0);
    }

    public static float GetEstimatedDeliveryPerMinute(KitchenEmployee employee, int upgradeLevel)
    {
        if (employee == null || employee.OperatedStationCount <= 0) return 0f;
        return Mathf.Max(0.01f, KitchenEmployee.GetTransportItemsPerMinuteForLevel(upgradeLevel));
    }

    public sealed class SystemRateSummary
    {
        public float stationRatePerMinute;
        public float travelOverheadPerMinute;
        public float workerOperationOverheadPerMinute;
        public float travelOverheadPerTilePerMinute;
        public float totalRateNeededPerMinute;
        public float travelTiles;
        public float travelSeconds;
        public float workerDeliveryCapacityPerMinute;
        public float transportShortfallPerMinute;
        public int activeWorkerCount;
        public int baseWorkersNeeded;
        public int additionalBaseWorkersNeeded;
        public float sustainableOutputPerMinute;
    }

    /// <summary>
    /// Sums only station output required by finished products delivered to pickup,
    /// then reports travel workload, transport coverage, and finished output.
    /// </summary>
    public static SystemRateSummary AnalyzeSystemRates()
    {
        var result = new SystemRateSummary();
        ProductionManager production = ProductionManager.Instance;
        if (production == null || production.productionFlows == null) return result;

        var countedWorkers = new HashSet<KitchenEmployee>();
        foreach (ProductionFlowPlan flow in production.productionFlows)
        {
            if (flow == null) continue;
            FlowLayoutMetrics metrics = AnalyzeFlowLayout(flow);
            float flowStationRate = metrics.totalStationOutputPerMinute;
            result.stationRatePerMinute += flowStationRate;

            // Use the same per-flow travel workload used by the selected-flow
            // staffing estimate, so the global summary and flow panel agree.
            if (flowStationRate > 0.01f)
            {
                result.travelOverheadPerMinute += metrics.travelOverheadPerMinute;
                result.workerOperationOverheadPerMinute += metrics.workerOperationOverheadPerMinute;
                result.travelTiles += metrics.routeTiles;
                result.travelSeconds += metrics.travelSeconds;
            }

            if (flow.workers != null)
            {
                foreach (KitchenEmployee worker in flow.workers)
                {
                    if (worker == null || !countedWorkers.Add(worker)) continue;
                    if (worker.OperatedStationCount > 0)
                        result.activeWorkerCount++;
                    result.workerDeliveryCapacityPerMinute += GetEstimatedDeliveryPerMinute(worker);
                }
            }

            if (metrics.hasAssignedWorker && metrics.stationCapacityPerMinute > 0.01f)
                result.sustainableOutputPerMinute += metrics.effectiveOutputPerMinute;
        }
        result.totalRateNeededPerMinute = result.stationRatePerMinute
            + result.workerOperationOverheadPerMinute + result.travelOverheadPerMinute;
        result.travelOverheadPerTilePerMinute = result.travelTiles > 0.01f
            ? result.travelOverheadPerMinute / result.travelTiles
            : 0f;
        result.transportShortfallPerMinute = Mathf.Max(0f,
            result.totalRateNeededPerMinute - result.workerDeliveryCapacityPerMinute);
        result.baseWorkersNeeded = GetBaseWorkersNeeded(result.totalRateNeededPerMinute);
        result.additionalBaseWorkersNeeded = GetBaseWorkersNeeded(result.transportShortfallPerMinute);
        return result;
    }

    public static int GetBaseWorkersNeeded(float requiredRatePerMinute)
    {
        float baseRate = KitchenEmployee.GetTransportItemsPerMinuteForLevel(0);
        return requiredRatePerMinute > 0.01f
            ? Mathf.CeilToInt(requiredRatePerMinute / Mathf.Max(0.01f, baseRate))
            : 0;
    }

    public static string GetWorkerDecisionSummary(KitchenEmployee employee)
    {
        if (employee == null) return "No worker selected";
        ProductionManager scheduleManager = ProductionManager.Instance;
        if (scheduleManager != null && scheduleManager.useScheduledProduction)
        {
            ProductionFlowPlan assigned = scheduleManager.GetFlowForWorker(employee);
            DeterministicProductionController.FlowForecast scheduled = scheduleManager.ScheduledProduction.GetForecast(assigned);
            if (scheduled != null)
                return employee.employeeName + " | " + employee.transportItemsPerMinute.ToString("0")
                    + " items/min transport limit | 4 items/trip\n" + assigned.flowName
                    + ": " + scheduled.PlannedPerMinute.ToString("0.0") + " planned output/min";
        }
        int count = employee.OperatedStationCount;
        if (count == 0)
            return employee.employeeName + " | Unassigned: no productive capacity";

        float distance = GetRouteDistanceTiles(employee);
        float laborRate = GetEstimatedDeliveryPerMinute(employee);
        float travelSeconds = GetEstimatedTravelSeconds(employee);
        float secondsPerTile = GetSecondsPerTravelTile(employee);
        string risk = distance >= 12f ? " | Risk: excess walking" : "";
        string flow = !string.IsNullOrEmpty(employee.assignedFlowName)
            ? employee.assignedFlowName + " | "
            : "";
        return flow + employee.employeeName + " | " + count
            + " stations | Route " + distance.ToString("F0") + " tiles | Delivery "
            + laborRate.ToString("0.0") + " items/min max | Travel "
            + distance.ToString("F0") + " tiles Ã— " + secondsPerTile.ToString("F2") + "s/tile = "
            + travelSeconds.ToString("F1") + "s/trip" + risk;
    }

    public static string GetAssignmentPreview(KitchenEmployee employee, StationNode candidate)
    {
        if (employee == null || candidate == null) return "";
        if (!candidate.IsWorkStation) return "This station is an output, not a labor assignment.";
        if (employee.IsAssignedTo(candidate.gameObject))
            return candidate.DisplayName + " is already on this worker's route.";
        float approach = 0f;
        GridManager grid = employee.grid != null ? employee.grid : GridManager.Instance;
        if (grid != null && employee.operatedStations.Count > 0)
            approach = GetDistanceTiles(grid, employee.operatedStations[employee.operatedStations.Count - 1], candidate.gameObject);
        float work = GetStationWorkSeconds(candidate.gameObject);
        return "Preview: add " + candidate.DisplayName
            + " | +" + approach.ToString("F0") + " travel tiles | " + work.ToString("F1") + "s station work";
    }

    public static List<string> GetSystemDiagnostics()
    {
        var notes = new List<string>();
        StationNode[] nodes = Object.FindObjectsByType<StationNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int unstaffed = 0;
        int missingOutputs = 0;

        foreach (StationNode node in nodes)
        {
            if (node == null || !node.IsWorkStation) continue;
            Register register = node.GetComponent<Register>();
            if (register != null && !register.isEnabled) continue;
            if (!node.HasAssignedWorker) unstaffed++;
            if (RequiresOutput(node) && node.outputTarget == null) missingOutputs++;
        }

        if (missingOutputs > 0)
            notes.Add(missingOutputs + " production station(s) have no output link, so work can stall.");
        if (unstaffed > 0)
            notes.Add(unstaffed + " station(s) are unstaffed, creating unused capacity.");

        ProductionManager production = ProductionManager.Instance;
        if (production != null && production.employees != null)
        {
            foreach (KitchenEmployee employee in production.employees)
            {
                if (employee == null) continue;
                float distance = GetRouteDistanceTiles(employee);
                if (distance >= 12f)
                    notes.Add(employee.employeeName + " walks about " + distance.ToString("F0") + " tiles per route. Move stations closer or specialize the worker.");
            }
        }

        if (notes.Count == 0)
            notes.Add("The configured workflow has no obvious staffing, routing, or walking warning.");
        return notes;
    }

    static bool RequiresOutput(StationNode node)
    {
        StationType? type = node.StationType;
        return type == StationType.Freezer || type == StationType.Grill
            || type == StationType.Assembly || type == StationType.Cutting || type == StationType.Pantry
            || type == StationType.Fryer;
    }

    static float GetDistanceTiles(GridManager grid, GameObject from, GameObject to)
    {
        if (grid == null || from == null || to == null) return 0f;
        Vector3 a = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(from));
        Vector3 b = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(to));
        List<Vector3> path = grid.GetPath(a, b);
        if (path.Count >= 2)
        {
            int distance = 0;
            for (int i = 1; i < path.Count; i++)
            {
                grid.WorldToCell(path[i - 1], out int fromX, out int fromY);
                grid.WorldToCell(path[i], out int toX, out int toY);
                distance += Mathf.Abs(toX - fromX) + Mathf.Abs(toY - fromY);
            }
            return distance;
        }

        grid.WorldToCell(a, out int ax, out int ay);
        grid.WorldToCell(b, out int bx, out int by);
        int dx = Mathf.Abs(ax - bx);
        int dy = Mathf.Abs(ay - by);
        return dx + dy;
    }

    public static float GetStationWorkSeconds(GameObject station)
    {
        if (station == null) return 0f;
        FreezerStation freezer = station.GetComponent<FreezerStation>();
        if (freezer != null)
            return StationOutputRateLimiter.SecondsPerItem(freezer.EffectiveMaxOutputPerMinute);
        GrillStation grill = station.GetComponent<GrillStation>();
        if (grill != null) return grill.EffectiveProcessTimeSeconds;
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        if (assembly != null) return assembly.EffectiveProcessTimeSeconds;
        CuttingStation cutting = station.GetComponent<CuttingStation>();
        if (cutting != null) return cutting.EffectiveProcessTimeSeconds;
        FryerStation fryer = station.GetComponent<FryerStation>();
        if (fryer != null) return fryer.EffectiveProcessTimeSeconds;
        DrinkStation drink = station.GetComponent<DrinkStation>();
        if (drink != null) return StationNode.GetEffectiveCycleSeconds(drink, drink.processTimeSeconds);
        PantryStation pantry = station.GetComponent<PantryStation>();
        if (pantry != null)
            return StationOutputRateLimiter.SecondsPerItem(pantry.EffectiveMaxOutputPerMinute);
        return 0f;
    }

    public sealed class FlowEconomics
    {
        public string summary = "";
        public readonly List<string> lines = new List<string>();
        public readonly List<ItemDefinition> requiredResources = new List<ItemDefinition>();
    }

    /// <summary>
    /// Per-product timing and money for a player-built production flow.
    /// </summary>
    public static FlowEconomics AnalyzeFlow(ProductionFlowPlan flow)
    {
        var result = new FlowEconomics();
        if (flow == null)
        {
            result.summary = "No flow selected.";
            return result;
        }

        flow.Clean();
        if (flow.stations.Count == 0 && (flow.stepIds == null || flow.stepIds.Count == 0))
        {
            result.summary = "Add stations to see time, cost, and profit.";
            return result;
        }

        bool hasRegister = FlowHas(flow, "Register", typeof(Register));
        bool hasShake = FlowHas(flow, "Shake", typeof(ShakeStation));
        bool canBurger = FlowHas(flow, "Freezer", typeof(FreezerStation))
            && FlowHas(flow, "Grill", typeof(GrillStation))
            && FlowHas(flow, "Assembly", typeof(AssemblyStation));
        bool canFries = FlowHas(flow, "Pantry", typeof(PantryStation))
            && FlowHas(flow, "Cutting", typeof(CuttingStation))
            && FlowHas(flow, "Fryer", typeof(FryerStation))
            && FlowHas(flow, "Assembly", typeof(AssemblyStation));
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        var inventory = Object.FindFirstObjectByType<KitchenInventory>();

        if (hasRegister && !canBurger && !canFries)
        {
            result.summary = hasShake
                ? "Service flow serves completed food and shakes to customers."
                : "Service flow — register hands finished food to customers.";
            if (hasShake && config != null && config.drinkItem != null)
                result.requiredResources.Add(config.drinkItem);
            return result;
        }

        FlowLayoutMetrics layout = AnalyzeFlowLayout(flow);
        float perMinute = layout.effectiveOutputPerMinute;
        float cycle = perMinute > 0.01f ? 60f / perMinute : 0f;
        string cycleLabel = perMinute > 0.01f
            ? cycle.ToString("0.0") + "s  (~" + FormatRate(perMinute) + "/min)"
            : "—";

        var products = new List<ItemDefinition>();
        int assemblyCount = CountStations(flow, typeof(AssemblyStation));
        if (canBurger && config != null)
            foreach (ItemDefinition menuItem in config.GetEnabledMenuItems())
                if (menuItem != null && config.IsBurger(menuItem)
                    && assemblyCount >= config.GetAssemblyChain(menuItem).Count)
                    products.Add(menuItem);
        if (canFries && config != null)
            foreach (ItemDefinition menuItem in config.GetEnabledMenuItems())
                if (config.IsFries(menuItem)
                    && assemblyCount >= config.GetAssemblyChain(menuItem).Count)
                    products.Add(menuItem);
        if (hasShake && config != null)
            foreach (ItemDefinition menuItem in config.GetEnabledMenuItems())
                if (config.IsDrink(menuItem)
                    && assemblyCount >= config.GetAssemblyChain(menuItem).Count)
                    products.Add(menuItem);

        // Report the outputs this route is configured to make. A flow can be
        // evaluated before its final handoff, so do not require delivery to a
        // pickup station before showing the product or its modeled capacity.
        products.RemoveAll(product => !IsProductProducedByFlow(flow, product, config));

        if (products.Count == 0)
        {
            result.summary = "Cycle " + cycleLabel + " — No cookable menu item detected on this route.";
            return result;
        }

        foreach (ItemDefinition product in products)
        {
            ItemDefinition resource = config != null && config.IsFries(product)
                ? (config.friesIngredient != null ? config.friesIngredient : product)
                : (config != null && config.IsBurger(product) ? config.rawPattyIngredient : product);
            if (resource != null && !result.requiredResources.Contains(resource))
                result.requiredResources.Add(resource);
            if (config != null)
            {
                foreach (ItemDefinition stageOutput in config.GetAssemblyChain(product))
                {
                    AssemblyRecipeDefinition recipe = config.GetAssemblyRecipe(stageOutput);
                    ItemDefinition source = config.GetAssemblySupplySource(recipe);
                    if (source != null && !result.requiredResources.Contains(source))
                        result.requiredResources.Add(source);
                    ItemDefinition thirdSource = config.GetAssemblyThirdSupplySource(recipe);
                    if (thirdSource != null && !result.requiredResources.Contains(thirdSource))
                        result.requiredResources.Add(thirdSource);
                }
            }
        }

        result.summary = (layout.hasAssignedWorker ? "Cycle " : "Projected cycle ") + cycleLabel
            + "\nLayout: " + layout.routeTiles.ToString("0") + " route tiles"
            + " (" + layout.travelSeconds.ToString("0.0") + "s travel/trip)"
            + "  |  Carry: " + FormatCarryRange(layout)
            + "  |  Bottleneck: " + layout.bottleneck
            + "\nEfficiency: process " + layout.stationWorkSeconds.ToString("0.0") + "s"
            + "  |  travel " + layout.travelSeconds.ToString("0.0") + "s"
            + "  |  layout " + layout.layoutEfficiencyPercent.ToString("0") + "%"
            + "  |  wait " + layout.runtimeWaitPercent.ToString("0") + "%"
            + "\nOutput capacity: " + layout.effectiveOutputPerMinute.ToString("0.0") + "/min"
            + "  |  " + layout.actualOutputPerMinute.ToString("0.0") + "/min delivered"
            + "  |  score " + layout.throughputEfficiencyPercent.ToString("0") + "%";
        foreach (ItemDefinition item in products)
        {
            string name = inventory != null ? inventory.GetDisplayName(item)
                : (!string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name);
            ItemDefinition resource = config != null && config.IsFries(item)
                ? (config.friesIngredient != null ? config.friesIngredient : item)
                : (config != null && config.IsBurger(item) ? config.rawPattyIngredient : item);
            float unitCost = GetIngredientUnitCost(resource, inventory);
            if (config != null)
            {
                foreach (ItemDefinition stageOutput in config.GetAssemblyChain(item))
                {
                    AssemblyRecipeDefinition recipe = config.GetAssemblyRecipe(stageOutput);
                    ItemDefinition source = config.GetAssemblySupplySource(recipe);
                    if (source != null)
                        unitCost += GetIngredientUnitCost(source, inventory)
                            * Mathf.Max(1, recipe.pantryInputAmount);
                    ItemDefinition thirdSource = config.GetAssemblyThirdSupplySource(recipe);
                    if (thirdSource != null)
                        unitCost += GetIngredientUnitCost(thirdSource, inventory)
                            * Mathf.Max(1, recipe.thirdInputAmount);
                }
            }
            int sell = Mathf.Max(0, item.price);
            float profit = sell - unitCost;
            result.lines.Add(
                "Produces " + name
                + "  |  Ingredients $" + unitCost.ToString("0.##")
                + "  |  Value $" + sell
                + "  |  Profit $" + profit.ToString("0.##"));
        }

        if (hasRegister)
            result.lines.Add("Also includes register service.");
        return result;
    }

    static bool FlowHas(ProductionFlowPlan flow, string stepId, System.Type componentType)
    {
        if (flow.stepIds != null)
        {
            for (int i = 0; i < flow.stepIds.Count; i++)
                if (flow.stepIds[i] == stepId) return true;
        }
        if (flow.stations == null) return false;
        foreach (GameObject station in flow.stations)
        {
            if (station != null && station.GetComponent(componentType) != null)
                return true;
        }
        return false;
    }

    static bool IsProductProducedByFlow(ProductionFlowPlan flow, ItemDefinition product,
        CustomerOrderConfig config)
    {
        if (flow?.stations == null || product == null) return false;
        foreach (GameObject station in flow.stations)
        {
            if (station == null) continue;
            bool produces = false;
            AssemblyStation assembly = station.GetComponent<AssemblyStation>();
            if (assembly != null)
            {
                AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
                produces = recipe != null && recipe.output == product;
            }
            else if (station.GetComponent<DrinkStation>() != null)
            {
                produces = config != null && product == config.drinkItem;
            }
            if (!produces) continue;

            // Only the product made at the terminal production station counts.
            // Capacity analysis separately requires that product to reach Pickup.
            bool hasDownstreamProduction = false;
            foreach (GameObject next in flow.GetOutgoing(station))
            {
                if (next == null || !flow.stations.Contains(next)) continue;
                if (next.GetComponent<HeatLampStation>() != null
                    || next.GetComponent<Register>() != null)
                    continue;
                StationNode nextNode = StationNode.EnsureOn(next);
                if (nextNode != null && nextNode.IsWorkStation)
                {
                    hasDownstreamProduction = true;
                    break;
                }
            }
            if (!hasDownstreamProduction)
                return true;
        }
        return false;
    }

    static int CountStations(ProductionFlowPlan flow, System.Type componentType)
    {
        if (flow?.stations == null) return 0;
        int count = 0;
        foreach (GameObject station in flow.stations)
            if (station != null && station.GetComponent(componentType) != null)
                count++;
        return count;
    }

    public static float EstimateFlowCycleSeconds(ProductionFlowPlan flow)
    {
        return AnalyzeFlowLayout(flow).effectiveCycleSeconds;
    }

    /// <summary>
    /// Flow throughput is limited by the equipment ceiling and the fraction of
    /// total station plus travel workload the assigned workers can cover.
    /// </summary>
    public static float GetFlowBottleneckOutputPerMinute(ProductionFlowPlan flow)
    {
        FlowLayoutMetrics metrics = AnalyzeFlowLayout(flow);
        return metrics.hasAssignedWorker ? metrics.effectiveOutputPerMinute : 0f;
    }

    /// <summary>
    /// Deterministic layout model shared by the flow UI and configured production rate.
    /// Walking is measured on the same grid path workers use. Trained worker rate stays fixed;
    /// travel adds to required handling workload. No random congestion penalty is applied.
    /// </summary>
    public static FlowLayoutMetrics AnalyzeFlowLayout(ProductionFlowPlan flow)
    {
        var result = new FlowLayoutMetrics();
        if (flow == null) return result;
        ProductionManager scheduledManager = ProductionManager.Instance;
        DeterministicProductionController.FlowForecast forecast = scheduledManager != null
            && scheduledManager.useScheduledProduction && Application.isPlaying
            ? scheduledManager.ScheduledProduction.GetForecast(flow) : null;
        if (forecast != null)
        {
            result.usesScheduledForecast = true;
            result.next60Output = forecast.Next60;
            result.forecastWarning = forecast.Warning;
            result.activeWorkerCount = forecast.Workers;
            result.hasAssignedWorker = forecast.Workers > 0;
            result.effectiveOutputPerMinute = forecast.PlannedPerMinute;
            result.stationCapacityPerMinute = forecast.PlannedPerMinute;
            result.effectiveCycleSeconds = forecast.PlannedPerMinute > 0f ? 60f / forecast.PlannedPerMinute : 0f;
            result.actualOutputPerMinute = scheduledManager.GetFlowCompletedOutputPerMinute(flow);
            result.minimumCarryCapacity = result.maximumCarryCapacity = 4;
            result.bottleneck = forecast.Warning ?? "Scheduled station processing and worker trips";
            foreach (KitchenEmployee worker in flow.workers)
                if (worker != null) result.laborCapacityPerMinute += worker.transportItemsPerMinute;
            result.throughputEfficiencyPercent = forecast.PlannedPerMinute > 0f
                ? Mathf.Clamp(result.actualOutputPerMinute * 100f / forecast.PlannedPerMinute, 0f, 100f) : 0f;
            return result;
        }
        flow.Clean();

        if (flow.stations == null || flow.stations.Count == 0)
            WorkerFlowAssigner.SynchronizeFlowRoute(flow);

        var route = new List<GameObject>();
        if (flow.stations != null)
        {
            foreach (GameObject station in flow.stations)
                if (station != null) route.Add(station);
        }

        GridManager grid = GridManager.Instance;
        GetFlowWorkerTransportProfile(flow, out _,
            out float averageTransportRate, out float moveSpeed,
            out float travelSecondsPerItemTile);
        flow.EnsureLegacyConnections();

        float trackedWorking = 0f;
        float trackedWaiting = 0f;
        var trackedStations = new HashSet<GameObject>();
        foreach (GameObject station in route)
        {
            if (station == null || !trackedStations.Add(station)) continue;
            StationRuntimeMetrics metrics = StationRuntimeMetrics.EnsureOn(station);
            if (metrics == null) continue;
            trackedWorking += metrics.WorkingSeconds;
            trackedWaiting += metrics.StarvedSeconds + metrics.BlockedSeconds + metrics.IdleSeconds;
        }
        float trackedTotal = trackedWorking + trackedWaiting;
        result.runtimeWorkingPercent = trackedTotal > 0.01f ? trackedWorking * 100f / trackedTotal : 0f;
        result.runtimeWaitPercent = trackedTotal > 0.01f ? trackedWaiting * 100f / trackedTotal : 0f;

        result.stationCapacityPerMinute = GetNominalStationCapacityPerMinute(route, flow,
            out result.stationBottleneckName, out result.totalStationOutputPerMinute,
            out result.stationWorkSeconds, out result.routeTiles,
            out result.workerTravelSecondsPerMinute,
            out result.workerOperationSecondsPerMinute, travelSecondsPerItemTile);
        if (grid != null)
            result.travelSeconds = result.routeTiles * grid.cellSize / Mathf.Max(0.1f, moveSpeed);

        float productiveCycleSeconds = result.stationWorkSeconds + result.travelSeconds;
        result.layoutEfficiencyPercent = productiveCycleSeconds > 0.01f
            ? result.stationWorkSeconds * 100f / productiveCycleSeconds : 0f;

        // The trained rate is enforced across pickup and placement handling.
        // Walking adds worker time for each required batch transfer, independent
        // of how long autonomous station processing takes.
        result.travelOverheadPerMinute = result.workerTravelSecondsPerMinute
            * averageTransportRate / 60f;
        // Drink dispensers and stocked sources hold a worker for their configured
        // operation time. Convert that worker time to the same item-equivalent
        // workload used by the transport rating, so the forecast includes time
        // that the live task runner actually spends waiting at those stations.
        result.workerOperationOverheadPerMinute = result.workerOperationSecondsPerMinute
            * averageTransportRate / 60f;
        result.totalRateNeededPerMinute = result.totalStationOutputPerMinute
            + result.workerOperationOverheadPerMinute
            + result.travelOverheadPerMinute;
        result.travelOverheadPerTilePerMinute = result.routeTiles > 0.01f
            ? result.travelOverheadPerMinute / result.routeTiles
            : 0f;
        result.baseWorkersNeeded = GetBaseWorkersNeeded(result.totalRateNeededPerMinute);

        float staffedLaborCapacity = 0f;
        result.minimumCarryCapacity = int.MaxValue;
        result.maximumCarryCapacity = 1;
        var countedFlowWorkers = new HashSet<KitchenEmployee>();
        if (flow.workers != null)
        {
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || !countedFlowWorkers.Add(worker)
                    || worker.OperatedStationCount == 0) continue;
                result.hasAssignedWorker = true;
                result.activeWorkerCount++;
                int carry = Mathf.Clamp(worker.CarryCapacity, 1, 4);
                result.minimumCarryCapacity = Mathf.Min(result.minimumCarryCapacity, carry);
                result.maximumCarryCapacity = Mathf.Max(result.maximumCarryCapacity, carry);
                float deliveryRate = GetEstimatedDeliveryPerMinute(worker);
                if (deliveryRate > 0.01f)
                {
                    // Workers on the same shared flow operate in parallel. Their
                    // labor capacities add together; taking the minimum made a
                    // second worker appear to provide no throughput improvement.
                    staffedLaborCapacity += deliveryRate;
                }
            }
        }

        if (!result.hasAssignedWorker)
        {
            result.minimumCarryCapacity = 1;
            result.maximumCarryCapacity = 1;
        }

        result.laborCapacityPerMinute = staffedLaborCapacity;
        result.transportShortfallPerMinute = Mathf.Max(0f,
            result.totalRateNeededPerMinute - result.laborCapacityPerMinute);
        float staffingCoverage = result.totalRateNeededPerMinute > 0.01f
            ? Mathf.Clamp01(result.laborCapacityPerMinute / result.totalRateNeededPerMinute)
            : 0f;
        result.effectiveOutputPerMinute = result.stationCapacityPerMinute * staffingCoverage;

        result.effectiveCycleSeconds = result.effectiveOutputPerMinute > 0.01f
            ? 60f / result.effectiveOutputPerMinute
            : 0f;

        bool laborLimited = result.transportShortfallPerMinute > 0.01f;
        result.bottleneck = laborLimited ? "Worker transport"
            : (!string.IsNullOrEmpty(result.stationBottleneckName)
                ? result.stationBottleneckName : "None");
        ProductionManager production = ProductionManager.Instance;
        result.actualOutputPerMinute = production != null
            ? production.GetFlowCompletedOutputPerMinute(flow) : 0f;
        result.throughputEfficiencyPercent = result.effectiveOutputPerMinute > 0.01f
            ? Mathf.Clamp(result.actualOutputPerMinute * 100f / result.effectiveOutputPerMinute, 0f, 100f)
            : 0f;
        return result;
    }

    static void GetFlowWorkerTransportProfile(ProductionFlowPlan flow,
        out int workerCount, out float averageTransportRate,
        out float averageMoveSpeed,
        out float travelSecondsPerItemTile)
    {
        workerCount = 0;
        float totalRate = 0f;
        float speedWeighted = 0f;
        float travelTimeWeighted = 0f;
        GridManager grid = GridManager.Instance;
        float cellSize = grid != null ? grid.cellSize : 1f;
        var countedWorkers = new HashSet<KitchenEmployee>();
        if (flow?.workers != null)
        {
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || !countedWorkers.Add(worker)
                    || worker.OperatedStationCount <= 0) continue;
                float rate = GetEstimatedDeliveryPerMinute(worker);
                if (rate <= 0.01f) continue;
                workerCount++;
                totalRate += rate;
                int carry = Mathf.Clamp(worker.CarryCapacity, 1, 4);
                float speed = Mathf.Max(0.1f, worker.moveSpeed);
                speedWeighted += rate * speed;
                travelTimeWeighted += rate * cellSize / (carry * speed);
            }
        }

        if (workerCount > 0 && totalRate > 0.01f)
        {
            averageTransportRate = totalRate / workerCount;
            averageMoveSpeed = speedWeighted / totalRate;
            travelSecondsPerItemTile = travelTimeWeighted / totalRate;
            return;
        }

        averageTransportRate = KitchenEmployee.GetTransportItemsPerMinuteForLevel(0);
        averageMoveSpeed = GetDefaultMoveSpeed();
        travelSecondsPerItemTile = cellSize / Mathf.Max(0.1f, averageMoveSpeed);
    }

    static string FormatCarryRange(FlowLayoutMetrics layout)
    {
        if (layout == null) return "1 item";
        int min = Mathf.Clamp(layout.minimumCarryCapacity, 1, 4);
        int max = Mathf.Clamp(layout.maximumCarryCapacity, min, 4);
        return min == max
            ? min + (min == 1 ? " item" : " items")
            : min + "-" + max + " items";
    }

    sealed class StationRateInput
    {
        public ItemDefinition item;
        public float unitsPerOutput;
    }

    sealed class StationRateNode
    {
        public GameObject station;
        public float capacityPerMinute;
        public float ratePerMinute;
        public float outputToPickupPerMinute;
        public bool requiresInput;
        public bool outputsToPickup;
        public readonly List<ItemDefinition> outputs = new List<ItemDefinition>();
        public readonly List<StationRateInput> inputs = new List<StationRateInput>();
        public readonly List<StationRateNode> outgoing = new List<StationRateNode>();
        public readonly List<StationRateNode> usedOutgoing = new List<StationRateNode>();
        public readonly Dictionary<StationRateNode, float> usedOutgoingRates =
            new Dictionary<StationRateNode, float>();
        public readonly List<GameObject> pickupTargets = new List<GameObject>();
    }

    /// <summary>
    /// Estimates finished output through the connected station graph. Rates from
    /// parallel stations are added when they supply the same recipe input. A
    /// station's output is shared across its outgoing connections, so a split
    /// cannot make one producer count as full capacity on every branch.
    /// </summary>
    static float GetNominalStationCapacityPerMinute(List<GameObject> route,
        ProductionFlowPlan flow, out string bottleneckName,
        out float requiredStationOutputPerMinute, out float requiredStationWorkSeconds,
        out float requiredRouteTiles, out float requiredWorkerTravelSecondsPerMinute,
        out float requiredWorkerOperationSecondsPerMinute,
        float travelSecondsPerItemTile)
    {
        bottleneckName = "";
        requiredStationOutputPerMinute = 0f;
        requiredStationWorkSeconds = 0f;
        requiredRouteTiles = 0f;
        requiredWorkerTravelSecondsPerMinute = 0f;
        requiredWorkerOperationSecondsPerMinute = 0f;
        if (route == null || route.Count == 0) return 0f;

        var nodes = new List<StationRateNode>();
        var byStation = new Dictionary<GameObject, StationRateNode>();
        foreach (GameObject station in route)
        {
            if (station == null || byStation.ContainsKey(station)
                || !TryGetStationOutputPerMinute(station, out float output))
                continue;

            var node = new StationRateNode
            {
                station = station,
                capacityPerMinute = output,
                ratePerMinute = output
            };
            GetStationRateItems(station, node.outputs);
            GetStationRateInputs(station, node.inputs, out node.requiresInput);
            nodes.Add(node);
            byStation.Add(station, node);
        }

        if (nodes.Count == 0) return 0f;

        if (flow != null && !flow.graphInitialized)
            flow.EnsureLegacyConnections();
        if (flow?.connections != null)
        {
            ProductionManager production = ProductionManager.Instance;
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection?.from == null || connection.to == null
                    || !byStation.TryGetValue(connection.from, out StationRateNode from))
                    continue;

                bool canTransferAnyOutput = false;
                if (production != null)
                {
                    foreach (ItemDefinition outputItem in from.outputs)
                    {
                        if (outputItem == null
                            || !production.CanFlowStationReceiveItem(connection.to, outputItem))
                            continue;
                        canTransferAnyOutput = true;
                        break;
                    }
                }
                if (!canTransferAnyOutput) continue;

                if (connection.to.GetComponent<HeatLampStation>() != null)
                {
                    from.outputsToPickup = true;
                    if (!from.pickupTargets.Contains(connection.to))
                        from.pickupTargets.Add(connection.to);
                    continue;
                }
                if (!byStation.TryGetValue(connection.to, out StationRateNode to)
                    || from.outgoing.Contains(to)) continue;
                from.outgoing.Add(to);
            }
        }

        // Process nodes in graph order. Cyclic stations are left at zero because
        // a closed loop cannot create ingredients without an upstream source.
        var indegree = new Dictionary<StationRateNode, int>();
        var ready = new Queue<StationRateNode>();
        foreach (StationRateNode node in nodes)
        {
            indegree[node] = 0;
            if (node.requiresInput && node.inputs.Count == 0)
                node.ratePerMinute = 0f;
        }
        foreach (StationRateNode node in nodes)
            foreach (StationRateNode next in node.outgoing)
                indegree[next]++;
        foreach (StationRateNode node in nodes)
            if (indegree[node] == 0) ready.Enqueue(node);

        var ordered = new List<StationRateNode>(nodes.Count);
        while (ready.Count > 0)
        {
            StationRateNode node = ready.Dequeue();
            ordered.Add(node);
            foreach (StationRateNode next in node.outgoing)
            {
                indegree[next]--;
                if (indegree[next] == 0) ready.Enqueue(next);
            }
        }
        if (ordered.Count != nodes.Count)
        {
            foreach (StationRateNode node in nodes)
                if (indegree[node] > 0) node.ratePerMinute = 0f;
        }

        // Rebalance shared upstream output as downstream stations discover
        // shortages in their other ingredients. The bounded passes stay cheap
        // for the small station graphs used by a single flow.
        int iterations = Mathf.Clamp(nodes.Count * 2, 8, 32);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var incomingRates = new Dictionary<StationRateNode, Dictionary<ItemDefinition, float>>();
            var genericIncomingRates = new Dictionary<StationRateNode, float>();
            foreach (StationRateNode node in ordered)
            {
                if (node.ratePerMinute <= 0.001f || node.outgoing.Count == 0) continue;

                float totalDemand = 0f;
                var demands = new List<(StationRateNode consumer, ItemDefinition item, float amount)>();
                foreach (StationRateNode consumer in node.outgoing)
                {
                    foreach (ItemDefinition outputItem in node.outputs)
                    {
                        float inputAmount = GetRequiredUnitsPerOutput(consumer, outputItem);
                        if (inputAmount <= 0f) continue;
                        float amount = consumer.ratePerMinute * inputAmount;
                        if (amount <= 0.001f) continue;
                        demands.Add((consumer, outputItem, amount));
                        totalDemand += amount;
                    }
                }

                if (totalDemand <= 0.001f) continue;
                float allocation = Mathf.Min(1f, node.ratePerMinute / totalDemand);
                foreach (var demand in demands)
                {
                    float supplied = demand.amount * allocation;
                    if (demand.item == null)
                    {
                        genericIncomingRates[demand.consumer] =
                            genericIncomingRates.TryGetValue(demand.consumer, out float currentGeneric)
                                ? currentGeneric + supplied : supplied;
                        continue;
                    }

                    if (!incomingRates.TryGetValue(demand.consumer,
                        out Dictionary<ItemDefinition, float> itemRates))
                    {
                        itemRates = new Dictionary<ItemDefinition, float>();
                        incomingRates.Add(demand.consumer, itemRates);
                    }
                    itemRates[demand.item] = itemRates.TryGetValue(demand.item, out float current)
                        ? current + supplied : supplied;
                }
            }

            float largestChange = 0f;
            foreach (StationRateNode node in ordered)
            {
                float nextRate = node.capacityPerMinute;
                if (node.requiresInput)
                {
                    if (node.inputs.Count == 0)
                        nextRate = 0f;
                    else
                    {
                        incomingRates.TryGetValue(node,
                            out Dictionary<ItemDefinition, float> itemRates);
                        genericIncomingRates.TryGetValue(node, out float genericRate);
                        float allIncoming = genericRate;
                        if (itemRates != null)
                            foreach (float itemRate in itemRates.Values) allIncoming += itemRate;

                        foreach (StationRateInput input in node.inputs)
                        {
                            float available = input.item == null
                                ? allIncoming
                                : genericRate + (itemRates != null
                                    && itemRates.TryGetValue(input.item, out float matchingRate)
                                        ? matchingRate : 0f);
                            nextRate = Mathf.Min(nextRate,
                                available / Mathf.Max(0.001f, input.unitsPerOutput));
                        }
                    }
                }

                largestChange = Mathf.Max(largestChange, Mathf.Abs(node.ratePerMinute - nextRate));
                node.ratePerMinute = nextRate;
            }

            if (largestChange < 0.01f) break;
        }

        float finishedOutput = 0f;
        int terminalCount = 0;
        string limitingName = "";
        foreach (StationRateNode node in nodes)
        {
            if (!node.outputsToPickup || node.ratePerMinute <= 0.001f) continue;
            float downstreamDemand = 0f;
            foreach (StationRateNode consumer in node.outgoing)
                foreach (ItemDefinition outputItem in node.outputs)
                    downstreamDemand += consumer.ratePerMinute
                        * GetRequiredUnitsPerOutput(consumer, outputItem);
            node.outputToPickupPerMinute = Mathf.Max(0f,
                node.ratePerMinute - Mathf.Min(node.ratePerMinute, downstreamDemand));
            if (node.outputToPickupPerMinute <= 0.001f) continue;
            finishedOutput += node.outputToPickupPerMinute;
            terminalCount++;
            if (node.ratePerMinute + 0.01f < node.capacityPerMinute)
                limitingName = GetName(node.station) + " input supply";
            else if (string.IsNullOrEmpty(limitingName))
                limitingName = GetName(node.station);
        }

        bottleneckName = terminalCount <= 1 ? limitingName : "Flow branches";
        CalculateRequiredStationWorkload(ordered, nodes, out requiredStationOutputPerMinute,
            out requiredStationWorkSeconds, out requiredRouteTiles,
            out requiredWorkerTravelSecondsPerMinute,
            out requiredWorkerOperationSecondsPerMinute, travelSecondsPerItemTile);
        return finishedOutput;
    }

    static void CalculateRequiredStationWorkload(List<StationRateNode> ordered,
        List<StationRateNode> nodes, out float requiredOutputPerMinute,
        out float requiredWorkSeconds, out float requiredRouteTiles,
        out float requiredWorkerTravelSecondsPerMinute,
        out float requiredWorkerOperationSecondsPerMinute,
        float travelSecondsPerItemTile)
    {
        requiredOutputPerMinute = 0f;
        requiredWorkSeconds = 0f;
        requiredRouteTiles = 0f;
        requiredWorkerTravelSecondsPerMinute = 0f;
        requiredWorkerOperationSecondsPerMinute = 0f;
        if (ordered == null || nodes == null) return;

        var requiredItems = new Dictionary<StationRateNode, Dictionary<ItemDefinition, float>>();
        var requiredGeneric = new Dictionary<StationRateNode, float>();
        var remainingCapacity = new Dictionary<StationRateNode, float>();
        foreach (StationRateNode node in nodes)
            remainingCapacity[node] = Mathf.Max(0f,
                node.ratePerMinute - node.outputToPickupPerMinute);

        // Start at finished products that are actually connected to the pickup
        // station, then work backward through the recipes to find used inputs.
        foreach (StationRateNode node in nodes)
        {
            if (node.outputToPickupPerMinute <= 0.001f)
                continue;
            ItemDefinition item = node.outputs.Count == 1 ? node.outputs[0] : null;
            AddRequiredRate(node, item, node.outputToPickupPerMinute,
                requiredItems, requiredGeneric);
        }

        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            StationRateNode node = ordered[i];
            float requestedRate = requiredGeneric.TryGetValue(node, out float genericRate)
                ? genericRate : 0f;
            if (requiredItems.TryGetValue(node, out Dictionary<ItemDefinition, float> itemRates))
                foreach (float itemRate in itemRates.Values)
                    requestedRate += itemRate;

            float usedRate = Mathf.Min(node.ratePerMinute, requestedRate);
            if (usedRate <= 0.001f) continue;

            requiredOutputPerMinute += usedRate;
            requiredWorkSeconds += GetStationWorkSeconds(node.station);
            requiredWorkerOperationSecondsPerMinute += usedRate
                * GetWorkerOccupiedOperationSecondsPerOutput(node.station);
            if (!node.requiresInput || node.inputs.Count == 0) continue;

            foreach (StationRateInput input in node.inputs)
            {
                float needed = usedRate * input.unitsPerOutput;
                AllocateRequiredInputRate(node, input.item, needed, nodes,
                    requiredItems, requiredGeneric, remainingCapacity);
            }
        }

        GridManager grid = GridManager.Instance;
        if (grid == null) return;
        foreach (StationRateNode node in nodes)
        {
            foreach (StationRateNode next in node.usedOutgoing)
            {
                float distance = GetDistanceTiles(grid, node.station, next.station);
                requiredRouteTiles += distance;
                if (node.usedOutgoingRates.TryGetValue(next, out float itemRate))
                    requiredWorkerTravelSecondsPerMinute += itemRate * distance
                        * travelSecondsPerItemTile;
            }
            if (node.outputToPickupPerMinute > 0.001f)
            {
                float outputRatePerPickup = node.outputToPickupPerMinute
                    / Mathf.Max(1, node.pickupTargets.Count);
                foreach (GameObject pickup in node.pickupTargets)
                {
                    float distance = GetDistanceTiles(grid, node.station, pickup);
                    requiredRouteTiles += distance;
                    requiredWorkerTravelSecondsPerMinute += outputRatePerPickup * distance
                        * travelSecondsPerItemTile;
                }
            }
        }
    }

    static void AllocateRequiredInputRate(StationRateNode consumer, ItemDefinition item,
        float neededRate, List<StationRateNode> nodes,
        Dictionary<StationRateNode, Dictionary<ItemDefinition, float>> requiredItems,
        Dictionary<StationRateNode, float> requiredGeneric,
        Dictionary<StationRateNode, float> remainingCapacity)
    {
        if (neededRate <= 0.001f) return;

        var suppliers = new List<StationRateNode>();
        float availableRate = 0f;
        foreach (StationRateNode supplier in nodes)
        {
            if (!supplier.outgoing.Contains(consumer)
                || !ProducesRateItem(supplier, item)
                || !remainingCapacity.TryGetValue(supplier, out float available)
                || available <= 0.001f)
                continue;
            suppliers.Add(supplier);
            availableRate += available;
        }

        float allocatedRate = Mathf.Min(neededRate, availableRate);
        if (allocatedRate <= 0.001f) return;
        foreach (StationRateNode supplier in suppliers)
        {
            float available = remainingCapacity[supplier];
            float share = allocatedRate * available / availableRate;
            share = Mathf.Min(available, share);
            remainingCapacity[supplier] = available - share;
            AddRequiredRate(supplier, item, share, requiredItems, requiredGeneric);
            supplier.usedOutgoingRates[consumer] =
                supplier.usedOutgoingRates.TryGetValue(consumer, out float currentRate)
                    ? currentRate + share : share;
            if (share > 0.001f && !supplier.usedOutgoing.Contains(consumer))
                supplier.usedOutgoing.Add(consumer);
        }
    }

    static bool ProducesRateItem(StationRateNode station, ItemDefinition item)
    {
        if (item == null) return true;
        foreach (ItemDefinition output in station.outputs)
            if (output == null || output == item) return true;
        return false;
    }

    static void AddRequiredRate(StationRateNode station, ItemDefinition item, float rate,
        Dictionary<StationRateNode, Dictionary<ItemDefinition, float>> requiredItems,
        Dictionary<StationRateNode, float> requiredGeneric)
    {
        if (station == null || rate <= 0.001f) return;
        if (item == null)
        {
            requiredGeneric[station] = requiredGeneric.TryGetValue(station, out float current)
                ? current + rate : rate;
            return;
        }

        if (!requiredItems.TryGetValue(station, out Dictionary<ItemDefinition, float> rates))
        {
            rates = new Dictionary<ItemDefinition, float>();
            requiredItems.Add(station, rates);
        }
        rates[item] = rates.TryGetValue(item, out float existing) ? existing + rate : rate;
    }

    static void GetStationRateItems(GameObject station, List<ItemDefinition> outputs)
    {
        outputs.Clear();
        if (station == null) return;

        ItemDefinition output = null;
        FreezerStation freezer = station.GetComponent<FreezerStation>();
        PantryStation pantry = station.GetComponent<PantryStation>();
        GrillStation grill = station.GetComponent<GrillStation>();
        FryerStation fryer = station.GetComponent<FryerStation>();
        CuttingStation cutting = station.GetComponent<CuttingStation>();
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        DrinkStation drink = station.GetComponent<DrinkStation>();

        if (freezer != null) output = freezer.selectedItem;
        else if (pantry != null)
        {
            AddRateItem(outputs, pantry.selectedItem);
            AddRateItem(outputs, pantry.secondItem);
        }
        else if (grill != null) output = grill.GetSelectedOutput();
        else if (fryer != null) output = fryer.GetSelectedOutput();
        else if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            output = recipe != null ? recipe.output : null;
        }
        else if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            output = recipe != null ? recipe.output : null;
        }
        else if (drink != null && ProductionManager.Instance != null
            && ProductionManager.Instance.orderConfig != null)
            output = ProductionManager.Instance.orderConfig.drinkItem;

        if (output != null) AddRateItem(outputs, output);
        if (outputs.Count == 0) outputs.Add(null);
    }

    static void AddRateItem(List<ItemDefinition> items, ItemDefinition item)
    {
        if (item != null && !items.Contains(item)) items.Add(item);
    }

    static void GetStationRateInputs(GameObject station, List<StationRateInput> inputs,
        out bool requiresInput)
    {
        inputs.Clear();
        requiresInput = false;
        if (station == null) return;

        GrillStation grill = station.GetComponent<GrillStation>();
        FryerStation fryer = station.GetComponent<FryerStation>();
        CuttingStation cutting = station.GetComponent<CuttingStation>();
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();

        if (grill != null)
        {
            requiresInput = true;
            AddRateInput(inputs, grill.GetSelectedInput(), 1f);
            AddRateInput(inputs, grill.GetSelectedSecondaryInput(), 1f);
        }
        else if (fryer != null)
        {
            requiresInput = true;
            AddRateInput(inputs, fryer.GetSelectedInput(), 1f);
        }
        else if (cutting != null)
        {
            requiresInput = true;
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            AddRateInput(inputs, recipe != null ? recipe.input : null, 1f);
        }
        else if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            if (recipe == null)
            {
                requiresInput = true;
                return;
            }

            if (recipe.RequiresProcessedInput)
                AddRateInput(inputs, recipe.processedInput, Mathf.Max(1, recipe.processedInputAmount));
            if (recipe.pantryInput != null)
                AddRateInput(inputs, recipe.pantryInput, Mathf.Max(1, recipe.pantryInputAmount));
            if (recipe.thirdInput != null)
                AddRateInput(inputs, recipe.thirdInput, Mathf.Max(1, recipe.thirdInputAmount));
            requiresInput = inputs.Count > 0;
        }
    }

    static void AddRateInput(List<StationRateInput> inputs, ItemDefinition item, float units)
    {
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].item != item) continue;
            inputs[i].unitsPerOutput += units;
            return;
        }
        inputs.Add(new StationRateInput { item = item, unitsPerOutput = units });
    }

    static float GetRequiredUnitsPerOutput(StationRateNode consumer, ItemDefinition item)
    {
        float units = 0f;
        foreach (StationRateInput input in consumer.inputs)
            if (input.item == null || input.item == item)
                units += input.unitsPerOutput;
        return units;
    }

    static float GetDefaultMoveSpeed()
    {
        if (ProductionManager.Instance != null && ProductionManager.Instance.employeePrefab != null)
        {
            KitchenEmployee sample = ProductionManager.Instance.employeePrefab.GetComponent<KitchenEmployee>();
            if (sample != null && sample.moveSpeed > 0.1f)
                return sample.moveSpeed;
        }
        return 3f;
    }

    static float GetWorkerOccupiedOperationSecondsPerOutput(GameObject station)
    {
        if (station == null) return 0f;
        FreezerStation freezer = station.GetComponent<FreezerStation>();
        if (freezer != null) return Mathf.Max(0f, freezer.processTimeSeconds);
        PantryStation pantry = station.GetComponent<PantryStation>();
        if (pantry != null) return Mathf.Max(0f, pantry.processTimeSeconds);
        DrinkStation drink = station.GetComponent<DrinkStation>();
        if (drink != null)
            return Mathf.Max(0f,
                StationNode.GetEffectiveCycleSeconds(drink, drink.processTimeSeconds));
        return 0f;
    }

    static bool TryGetStationOutputPerMinute(GameObject station, out float outputPerMinute)
    {
        outputPerMinute = 0f;
        if (station == null) return false;
        if (station.GetComponent<HeatLampStation>() != null) return false;
        if (station.GetComponent<Register>() != null) return false;

        FreezerStation freezer = station.GetComponent<FreezerStation>();
        PantryStation pantry = station.GetComponent<PantryStation>();
        GrillStation grill = station.GetComponent<GrillStation>();
        FryerStation fryer = station.GetComponent<FryerStation>();
        CuttingStation cutting = station.GetComponent<CuttingStation>();
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        if ((freezer != null && !freezer.HasItemSelected)
            || (pantry != null && !pantry.HasItemSelected)
            || (grill != null && !grill.HasProductSelected)
            || (fryer != null && fryer.GetSelectedOutput() == null)
            || (cutting != null && !cutting.HasRecipeSelected)
            || (assembly != null && !assembly.HasProductSelected))
            return false;

        StationNode node = StationNode.EnsureOn(station);
        if (node == null) return false;
        node.EnsureIoDefaults(force: true);
        if (node.outputAmountPerMinute <= 0.01f) return false;

        outputPerMinute = node.outputAmountPerMinute;
        return true;
    }

    static string FormatRate(float rate)
    {
        if (Mathf.Approximately(rate, Mathf.Round(rate))) return rate.ToString("0");
        if (rate >= 10f) return rate.ToString("0");
        return rate.ToString("0.0");
    }

    public static float EstimateFlowCycleSecondsFromTiming(ProductionFlowPlan flow)
    {
        if (flow == null) return 0f;
        flow.Clean();

        // Staffed flows: bottleneck is the slowest worker segment.
        if (flow.workers != null && flow.workers.Count > 0)
        {
            float bottleneck = 0f;
            bool any = false;
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || worker.OperatedStationCount == 0) continue;
                bottleneck = Mathf.Max(bottleneck, GetEstimatedCycleSeconds(worker));
                any = true;
            }
            if (any) return bottleneck;
        }

        // Unstaffed: full route work + walk between consecutive stations.
        var route = new List<GameObject>();
        if (flow.stations != null)
        {
            foreach (GameObject station in flow.stations)
                if (station != null) route.Add(station);
        }

        float work = 0f;
        foreach (GameObject station in route)
        {
            StationNode node = StationNode.EnsureOn(station);
            if (node != null && node.IsWorkStation)
                work += GetStationWorkSeconds(station);
        }

        float walk = 0f;
        GridManager grid = GridManager.Instance;
        float moveSpeed = 3.5f;
        if (ProductionManager.Instance != null && ProductionManager.Instance.employeePrefab != null)
        {
            var sample = ProductionManager.Instance.employeePrefab.GetComponent<KitchenEmployee>();
            if (sample != null && sample.moveSpeed > 0.1f)
                moveSpeed = sample.moveSpeed;
        }

        if (grid != null)
        {
            for (int i = 1; i < route.Count; i++)
            {
                float tiles = GetDistanceTiles(grid, route[i - 1], route[i]);
                walk += tiles * grid.cellSize / moveSpeed;
            }
        }

        return work + walk;
    }

    public static float GetIngredientUnitCost(ItemDefinition item, KitchenInventory inventory = null)
    {
        if (item == null) return 0f;
        if (inventory == null)
            inventory = Object.FindFirstObjectByType<KitchenInventory>();
        if (inventory != null)
        {
            int pack = inventory.GetPackSize(item);
            int price = inventory.GetPackPrice(item);
            return pack > 0 ? (float)price / pack : price;
        }
        int packSize = Mathf.Max(1, item.orderPackSize);
        int packPrice = item.orderPackPrice > 0
            ? item.orderPackPrice
            : Mathf.Max(1, item.price) * packSize / 2;
        return (float)packPrice / packSize;
    }

    static string GetName(GameObject station)
    {
        StationNode node = StationNode.EnsureOn(station);
        return node != null ? node.DisplayName : station.name;
    }
}
