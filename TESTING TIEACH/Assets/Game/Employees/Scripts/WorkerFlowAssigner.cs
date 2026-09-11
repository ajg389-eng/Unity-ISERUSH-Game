using System.Collections.Generic;
using UnityEngine;

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
    public const int MaxSteps = 4;

    public static readonly FlowStationDef[] Catalog =
    {
        new FlowStationDef("Freezer", "Freezer", typeof(FreezerStation)),
        new FlowStationDef("Grill", "Grill", typeof(GrillStation)),
        new FlowStationDef("Assembly", "Assembly", typeof(AssemblyStation)),
        new FlowStationDef("Fryer", "Fryer", typeof(FryerStation)),
        new FlowStationDef("Drink", "Drink", typeof(DrinkStation)),
        new FlowStationDef("Register", "Register", typeof(Register)),
        new FlowStationDef("HeatLamp", "Heat Lamp", typeof(HeatLampStation)),
        new FlowStationDef("Pantry", "Pantry", typeof(PantryStation))
    };

    public static string GetTitle(KitchenFlowKind kind)
    {
        switch (kind)
        {
            case KitchenFlowKind.BurgerLine: return "Burger";
            case KitchenFlowKind.FriesLine: return "Fries";
            case KitchenFlowKind.Drinks: return "Drinks";
            case KitchenFlowKind.Register: return "Register";
            case KitchenFlowKind.Custom: return "Custom";
            default: return "None";
        }
    }

    public static string GetPathLabel(KitchenFlowKind kind)
    {
        return FormatSteps(GetPresetSteps(kind));
    }

    public static List<string> GetPresetSteps(KitchenFlowKind kind)
    {
        var steps = new List<string>();
        switch (kind)
        {
            case KitchenFlowKind.BurgerLine:
                steps.AddRange(new[] { "Freezer", "Grill", "Assembly", "HeatLamp" });
                break;
            case KitchenFlowKind.FriesLine:
                steps.AddRange(new[] { "Fryer", "HeatLamp" });
                break;
            case KitchenFlowKind.Drinks:
                steps.Add("Drink");
                break;
            case KitchenFlowKind.Register:
                steps.Add("Register");
                if (Object.FindFirstObjectByType<DrinkStation>() != null)
                    steps.Add("Drink");
                break;
        }
        return steps;
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
                node.SetWorker(emp);
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
        bool burger = kind == KitchenFlowKind.BurgerLine;
        if (!burger)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && (nodes[i].GetComponent<GrillStation>() != null
                    || nodes[i].GetComponent<AssemblyStation>() != null))
                    burger = true;
            }
        }
        if (!burger) return;

        var burgerItem = ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null
            ? ProductionManager.Instance.orderConfig.burgerBase
            : null;
        if (burgerItem == null) return;

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

            var node = StationNode.EnsureOn(go);
            if (node.assignedWorker == null || node.assignedWorker == emp)
                return go;
        }
        return null;
    }
}
