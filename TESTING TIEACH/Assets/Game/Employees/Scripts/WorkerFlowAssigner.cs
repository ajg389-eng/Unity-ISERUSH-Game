using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ProductionFlowPlan
{
    public const int MaxNameLength = 15;

    public string flowName = "Flow";
    public KitchenFlowKind kind = KitchenFlowKind.Custom;
    public List<string> stepIds = new List<string>();
    public List<GameObject> stations = new List<GameObject>();
    public List<KitchenEmployee> workers = new List<KitchenEmployee>();

    public void Clean()
    {
        flowName = NormalizeName(flowName, "Flow");
        if (stepIds == null) stepIds = new List<string>();
        if (stations == null) stations = new List<GameObject>();
        stations.RemoveAll(station => station == null);
        if (workers == null) workers = new List<KitchenEmployee>();
        workers.RemoveAll(worker => worker == null);
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
    public const int MaxSteps = 8;

    public static readonly FlowStationDef[] Catalog =
    {
        new FlowStationDef("Freezer", "Freezer", typeof(FreezerStation)),
        new FlowStationDef("Grill", "Grill", typeof(GrillStation)),
        new FlowStationDef("Assembly", "Assembly", typeof(AssemblyStation)),
        new FlowStationDef("Fryer", "Fryer", typeof(FryerStation)),
        new FlowStationDef("Drink", "Drink", typeof(DrinkStation)),
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
        flow.Clean();
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

        int assigned = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = StationNode.EnsureOn(nodes[i]);
            bool isHeatLamp = nodes[i].GetComponent<HeatLampStation>() != null;
            if (!isHeatLamp && assigned < KitchenEmployee.MaxStations)
            {
                node.AddWorker(emp);
                assigned++;
            }

            if (i + 1 < nodes.Count)
                node.SetOutput(nodes[i + 1]);
        }

        emp.assignedFlow = kind;
        emp.SyncFromOperatedStations();
        return emp.CanTakeJobs || kind == KitchenFlowKind.None;
    }

    static void ConfigureProducts(KitchenFlowKind kind, List<GameObject> nodes)
    {
        // Wire burger product onto grill/assembly whenever those stations are on the route.
        var burgerItem = ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null
            ? ProductionManager.Instance.orderConfig.burgerBase
            : null;
        if (burgerItem == null || nodes == null) return;

        foreach (var go in nodes)
        {
            if (go == null) continue;
            var grill = go.GetComponent<GrillStation>();
            if (grill != null) grill.selectedProduct = burgerItem;
            var assembly = go.GetComponent<AssemblyStation>();
            if (assembly != null) assembly.selectedProduct = burgerItem;
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
    /// Wires one player-defined route, then divides its work stations into contiguous
    /// segments. Each segment is assigned to one team member so handoffs stay visible.
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
        string orderProblem = ValidateStepOrder(flow.stepIds);
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

        bool containsRegister = workStations.Exists(station => station.GetComponent<Register>() != null);
        // Cashiers must retain their drink stop because serving is one coupled customer task.
        int activeWorkers = containsRegister ? 1 : Mathf.Min(flow.workers.Count, workStations.Count);
        if (workStations.Count > activeWorkers * KitchenEmployee.MaxStations)
        {
            result.message = "Add more workers. A worker can cover at most " + KitchenEmployee.MaxStations + " stations.";
            return result;
        }

        foreach (KitchenEmployee worker in flow.workers)
            if (worker != null)
                worker.ClearAllOperatedStations();

        ConfigureProducts(flow.kind, route);
        // Wire consecutive stations. Never clear the last output — fryer/assembly may
        // already target a shared heat lamp (and clearing it stops the loop).
        for (int i = 0; i < route.Count - 1; i++)
        {
            StationNode node = StationNode.EnsureOn(route[i]);
            if (node != null)
                node.SetOutput(route[i + 1]);
        }

        int start = 0;
        for (int workerIndex = 0; workerIndex < activeWorkers; workerIndex++)
        {
            int workersLeft = activeWorkers - workerIndex;
            int stationsLeft = workStations.Count - start;
            int take = workersLeft == 1
                ? stationsLeft
                : ChooseBalancedSegmentLength(workStations, start, stationsLeft, workersLeft);
            take = Mathf.Clamp(take, 1, KitchenEmployee.MaxStations);

            KitchenEmployee worker = flow.workers[workerIndex];
            var stationNames = new List<string>();
            for (int j = 0; j < take; j++)
            {
                StationNode node = StationNode.EnsureOn(workStations[start + j]);
                node.AddWorker(worker);
                stationNames.Add(node.DisplayName);
            }
            worker.assignedFlow = flow.kind;
            worker.assignedFlowName = flow.flowName;
            worker.SyncFromOperatedStations();
            result.assignments.Add(worker.employeeName + ": " + string.Join(" → ", stationNames));
            start += take;
        }

        for (int i = activeWorkers; i < flow.workers.Count; i++)
        {
            KitchenEmployee worker = flow.workers[i];
            if (worker == null) continue;
            worker.assignedFlow = flow.kind;
            worker.assignedFlowName = flow.flowName;
            result.assignments.Add(worker.employeeName + ": reserve (no open stage)");
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
        result.message = flow.workers.Count > activeWorkers
            ? (flow.workers.Count - activeWorkers) + " worker(s) are reserve because every stage already has an owner."
            : "Work split into contiguous stages.";
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

    static int ChooseBalancedSegmentLength(List<GameObject> stations, int start, int stationsLeft, int workersLeft)
    {
        float remainingLoad = 0f;
        for (int i = start; i < stations.Count; i++)
            remainingLoad += WorkflowAnalysis.GetStationWorkSeconds(stations[i]);
        float target = remainingLoad / workersLeft;
        int maxTake = Mathf.Min(KitchenEmployee.MaxStations, stationsLeft - (workersLeft - 1));
        int bestTake = 1;
        float load = 0f;
        float bestDifference = float.MaxValue;
        for (int take = 1; take <= maxTake; take++)
        {
            load += WorkflowAnalysis.GetStationWorkSeconds(stations[start + take - 1]);
            float difference = Mathf.Abs(load - target);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                bestTake = take;
            }
        }
        return bestTake;
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
/// Lightweight ISE decision support for workflow assignment. Estimates are deliberately
/// transparent: station work time plus grid walking time, without pretending to predict demand.
/// </summary>
public static class WorkflowAnalysis
{
    public sealed class FlowLayoutMetrics
    {
        public float routeTiles;
        public float travelSeconds;
        public float stationWorkSeconds;
        public float stationCapacityPerMinute;
        public float laborCapacityPerMinute;
        public float effectiveOutputPerMinute;
        public float effectiveCycleSeconds;
        public bool hasAssignedWorker;
        public int minimumCarryCapacity = 1;
        public int maximumCarryCapacity = 1;
        public string bottleneck = "None";
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
            int first = int.MaxValue;
            int last = -1;
            for (int i = 0; i < flow.stations.Count; i++)
            {
                GameObject station = flow.stations[i];
                if (station == null || !employee.operatedStations.Contains(station)) continue;
                first = Mathf.Min(first, i);
                last = Mathf.Max(last, i);
            }

            if (last >= 0)
            {
                int routeEnd = Mathf.Min(flow.stations.Count - 1, last + 1);
                for (int i = first; i <= routeEnd; i++)
                    if (flow.stations[i] != null)
                        route.Add(flow.stations[i]);
                return route;
            }
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

        List<GameObject> route = GetOrderedRoute(employee);
        float tiles = 0f;
        for (int i = 1; i < route.Count; i++)
            tiles += GetDistanceTiles(grid, route[i - 1], route[i]);
        return tiles;
    }

    public static float GetEstimatedCycleSeconds(KitchenEmployee employee)
    {
        if (employee == null) return 0f;
        float work = 0f;
        foreach (GameObject station in GetOrderedRoute(employee))
        {
            StationNode node = StationNode.EnsureOn(station);
            if (node == null || !node.IsWorkStation || node.IsWorkerAssigned(employee))
                work += GetStationWorkSeconds(station);
        }
        float walkWorld = GetRouteDistanceTiles(employee)
            * ((employee.grid != null ? employee.grid : GridManager.Instance)?.cellSize ?? 1f);
        float walk = walkWorld / Mathf.Max(0.1f, employee.moveSpeed);
        return work + walk;
    }

    public static string GetWorkerDecisionSummary(KitchenEmployee employee)
    {
        if (employee == null) return "No worker selected";
        int count = employee.OperatedStationCount;
        if (count == 0)
            return employee.employeeName + " | Unassigned: no productive capacity";

        float distance = GetRouteDistanceTiles(employee);
        float cycle = GetEstimatedCycleSeconds(employee);
        int carry = Mathf.Clamp(employee.CarryCapacity, 1, 4);
        float laborRate = cycle > 0.01f ? 60f * carry / cycle : 0f;
        string risk = count >= KitchenEmployee.MaxStations
            ? " | Risk: high task switching"
            : distance >= 12f ? " | Risk: excess walking" : "";
        string flow = !string.IsNullOrEmpty(employee.assignedFlowName)
            ? employee.assignedFlowName + " | "
            : "";
        return flow + employee.employeeName + " | " + count + "/" + KitchenEmployee.MaxStations
            + " stations | Route " + distance.ToString("F0") + " tiles | Base cycle "
            + cycle.ToString("F1") + "s | Carry " + carry + " | Labor "
            + laborRate.ToString("0.0") + "/min" + risk;
    }

    public static string GetAssignmentPreview(KitchenEmployee employee, StationNode candidate)
    {
        if (employee == null || candidate == null) return "";
        if (!candidate.IsWorkStation) return "This station is an output, not a labor assignment.";
        if (employee.IsAssignedTo(candidate.gameObject))
            return candidate.DisplayName + " is already on this worker's route.";
        if (employee.OperatedStationCount >= KitchenEmployee.MaxStations)
            return "At capacity. Remove a station before adding " + candidate.DisplayName + ".";

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
                if (employee.OperatedStationCount >= KitchenEmployee.MaxStations)
                    notes.Add(employee.employeeName + " covers three stations. Watch for task-switching delays.");
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
            || type == StationType.Assembly || type == StationType.Fryer;
    }

    static float GetDistanceTiles(GridManager grid, GameObject from, GameObject to)
    {
        if (grid == null || from == null || to == null) return 0f;
        Vector3 a = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(from));
        Vector3 b = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(to));
        List<Vector3> path = grid.GetPath(a, b);
        if (path.Count >= 2)
        {
            float distance = 0f;
            for (int i = 1; i < path.Count; i++)
                distance += Vector3.Distance(path[i - 1], path[i]) / Mathf.Max(0.01f, grid.cellSize);
            return distance;
        }

        grid.WorldToCell(a, out int ax, out int ay);
        grid.WorldToCell(b, out int bx, out int by);
        int dx = Mathf.Abs(ax - bx);
        int dy = Mathf.Abs(ay - by);
        return Mathf.Min(dx, dy) * 1.41421356f + Mathf.Abs(dx - dy);
    }

    public static float GetStationWorkSeconds(GameObject station)
    {
        if (station == null) return 0f;
        FreezerStation freezer = station.GetComponent<FreezerStation>();
        if (freezer != null) return freezer.processTimeSeconds;
        GrillStation grill = station.GetComponent<GrillStation>();
        if (grill != null) return grill.processTimeSeconds;
        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        if (assembly != null) return assembly.processTimeSeconds;
        FryerStation fryer = station.GetComponent<FryerStation>();
        if (fryer != null) return fryer.processTimeSeconds;
        DrinkStation drink = station.GetComponent<DrinkStation>();
        if (drink != null) return drink.processTimeSeconds;
        PantryStation pantry = station.GetComponent<PantryStation>();
        if (pantry != null) return pantry.processTimeSeconds;
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
        bool hasDrink = FlowHas(flow, "Drink", typeof(DrinkStation));
        bool canBurger = FlowHas(flow, "Freezer", typeof(FreezerStation))
            || FlowHas(flow, "Grill", typeof(GrillStation))
            || FlowHas(flow, "Assembly", typeof(AssemblyStation));
        bool canFries = FlowHas(flow, "Fryer", typeof(FryerStation));
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        var inventory = Object.FindFirstObjectByType<KitchenInventory>();

        if (hasRegister && !canBurger && !canFries)
        {
            result.summary = hasDrink
                ? "Service flow — serves full orders (food + drinks) to customers."
                : "Service flow — register hands finished food to customers.";
            if (hasDrink && config != null && config.drinkItem != null)
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
        if (canBurger && config != null && config.burgerBase != null)
            products.Add(config.burgerBase);
        if (canFries && config != null && config.friesItem != null)
            products.Add(config.friesItem);
        if (hasDrink && config != null && config.drinkItem != null)
            products.Add(config.drinkItem);

        if (products.Count == 0)
        {
            result.summary = "Cycle " + cycleLabel + " — no cookable menu item detected on this route.";
            return result;
        }

        foreach (ItemDefinition product in products)
            if (product != null && !result.requiredResources.Contains(product))
                result.requiredResources.Add(product);

        result.summary = (layout.hasAssignedWorker ? "Cycle " : "Projected cycle ") + cycleLabel
            + "\nLayout: " + layout.routeTiles.ToString("0") + " route tiles"
            + " (" + layout.travelSeconds.ToString("0.0") + "s travel/trip)"
            + "  |  Carry: " + FormatCarryRange(layout)
            + "  |  Bottleneck: " + layout.bottleneck;
        foreach (ItemDefinition item in products)
        {
            string name = inventory != null ? inventory.GetDisplayName(item)
                : (!string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name);
            float unitCost = GetIngredientUnitCost(item, inventory);
            int sell = Mathf.Max(0, item.price);
            float profit = sell - unitCost;
            result.lines.Add(
                "Produces " + name
                + "  |  ingredients $" + unitCost.ToString("0.##")
                + "  |  value $" + sell
                + "  |  profit $" + profit.ToString("0.##"));
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

    public static float EstimateFlowCycleSeconds(ProductionFlowPlan flow)
    {
        return AnalyzeFlowLayout(flow).effectiveCycleSeconds;
    }

    /// <summary>
    /// Flow throughput is limited by the slowest station output on the route
    /// (e.g. grill 5/min with assembly 10/min → 5/min).
    /// </summary>
    public static float GetFlowBottleneckOutputPerMinute(ProductionFlowPlan flow)
    {
        FlowLayoutMetrics metrics = AnalyzeFlowLayout(flow);
        return metrics.hasAssignedWorker ? metrics.effectiveOutputPerMinute : 0f;
    }

    /// <summary>
    /// Deterministic layout model shared by the flow UI and configured production rate.
    /// Throughput is the lower of equipment capacity and worker route capacity. Walking is
    /// measured on the same grid path workers use, with no random congestion penalty.
    /// </summary>
    public static FlowLayoutMetrics AnalyzeFlowLayout(ProductionFlowPlan flow)
    {
        var result = new FlowLayoutMetrics();
        if (flow == null) return result;
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
        float moveSpeed = GetDefaultMoveSpeed();
        for (int i = 0; i < route.Count; i++)
        {
            result.stationWorkSeconds += GetStationWorkSeconds(route[i]);
            if (i == 0 || grid == null) continue;
            result.routeTiles += GetDistanceTiles(grid, route[i - 1], route[i]);
        }
        if (grid != null)
            result.travelSeconds = result.routeTiles * grid.cellSize / Mathf.Max(0.1f, moveSpeed);

        result.stationCapacityPerMinute = GetNominalStationCapacityPerMinute(route, out string stationBottleneck);

        float laborCycle = 0f;
        float staffedLaborCapacity = float.MaxValue;
        bool foundStaffedSegment = false;
        result.minimumCarryCapacity = int.MaxValue;
        result.maximumCarryCapacity = 1;
        if (flow.workers != null)
        {
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null || worker.OperatedStationCount == 0) continue;
                result.hasAssignedWorker = true;
                float workerCycle = GetEstimatedCycleSeconds(worker);
                int carry = Mathf.Clamp(worker.CarryCapacity, 1, 4);
                result.minimumCarryCapacity = Mathf.Min(result.minimumCarryCapacity, carry);
                result.maximumCarryCapacity = Mathf.Max(result.maximumCarryCapacity, carry);
                laborCycle = Mathf.Max(laborCycle, workerCycle);
                if (workerCycle > 0.01f)
                {
                    staffedLaborCapacity = Mathf.Min(staffedLaborCapacity, 60f * carry / workerCycle);
                    foundStaffedSegment = true;
                }
            }
        }

        // Before staffing, preview this layout using the employee prefab's movement speed.
        // Once staffed, the slowest worker-owned route segment becomes the labor constraint.
        if (!result.hasAssignedWorker)
        {
            laborCycle = result.stationWorkSeconds + result.travelSeconds;
            result.minimumCarryCapacity = 1;
            result.maximumCarryCapacity = 1;
        }

        result.laborCapacityPerMinute = result.hasAssignedWorker
            ? (foundStaffedSegment ? staffedLaborCapacity : 0f)
            : (laborCycle > 0.01f ? 60f / laborCycle : 0f);
        if (result.stationCapacityPerMinute > 0.01f && result.laborCapacityPerMinute > 0.01f)
            result.effectiveOutputPerMinute = Mathf.Min(result.stationCapacityPerMinute, result.laborCapacityPerMinute);
        else
            result.effectiveOutputPerMinute = Mathf.Max(result.stationCapacityPerMinute, result.laborCapacityPerMinute);

        result.effectiveCycleSeconds = result.effectiveOutputPerMinute > 0.01f
            ? 60f / result.effectiveOutputPerMinute
            : laborCycle;

        bool laborLimited = result.laborCapacityPerMinute > 0.01f
            && (result.stationCapacityPerMinute <= 0.01f
                || result.laborCapacityPerMinute < result.stationCapacityPerMinute - 0.01f);
        result.bottleneck = laborLimited ? "Worker travel/workload"
            : (!string.IsNullOrEmpty(stationBottleneck) ? stationBottleneck : "None");
        return result;
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

    static float GetNominalStationCapacityPerMinute(List<GameObject> route, out string bottleneckName)
    {
        bottleneckName = "";
        float minOut = float.MaxValue;
        bool any = false;

        if (route != null)
        {
            foreach (GameObject station in route)
            {
                if (!TryGetStationOutputPerMinute(station, out float output)) continue;
                if (!any || output < minOut)
                {
                    minOut = output;
                    bottleneckName = GetName(station);
                }
                any = true;
            }
        }

        return any ? minOut : 0f;
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

    static bool TryGetStationOutputPerMinute(GameObject station, out float outputPerMinute)
    {
        outputPerMinute = 0f;
        if (station == null) return false;
        if (station.GetComponent<HeatLampStation>() != null) return false;
        if (station.GetComponent<Register>() != null) return false;

        StationNode node = StationNode.EnsureOn(station);
        if (node == null) return false;
        node.EnsureIoDefaults();
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
