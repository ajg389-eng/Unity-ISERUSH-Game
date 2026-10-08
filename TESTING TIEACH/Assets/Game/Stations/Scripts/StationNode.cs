using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Common physical inventory contract for stations on a production flow.
/// An item transfer must remove units from one buffer before inserting them
/// into the next buffer or a worker's carried inventory.
/// </summary>
public interface IStationBuffer
{
    int InputSlotCapacity { get; }
    int OutputSlotCapacity { get; }
    int GetInputCount(ItemDefinition item);
    int GetOutputCount(ItemDefinition item);
    bool CanAcceptInput(ItemDefinition item, int amount);
    int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null);
    int TakeOutput(ItemDefinition item, int amount);
}

/// <summary>Discovers the authored Input*/Output* buffer markers on station prefabs.</summary>
public static class StationBufferLayout
{
    public static void FindMarkers(Transform station, List<Transform> inputs, List<Transform> outputs)
    {
        inputs?.Clear();
        outputs?.Clear();
        if (station == null) return;

        Transform[] children = station.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform marker = children[i];
            if (marker == station) continue;
            if (IsMarker(marker.name, "Input")) inputs?.Add(marker);
            else if (IsMarker(marker.name, "Output")) outputs?.Add(marker);
        }

        inputs?.Sort(CompareMarkers);
        outputs?.Sort(CompareMarkers);
        HideMarkers(inputs);
        HideMarkers(outputs);
    }

    static bool IsMarker(string value, string prefix)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(prefix,
                System.StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Length == prefix.Length) return true;
        char next = value[prefix.Length];
        return char.IsDigit(next) || char.IsWhiteSpace(next) || next == '(';
    }

    static int CompareMarkers(Transform a, Transform b) =>
        string.Compare(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty,
            System.StringComparison.OrdinalIgnoreCase);

    static void HideMarkers(List<Transform> markers)
    {
        if (markers == null) return;
        for (int i = 0; i < markers.Count; i++)
        {
            Transform marker = markers[i];
            if (marker == null) continue;
            foreach (Renderer renderer in marker.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider collider in marker.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }
    }
}

/// <summary>
/// Attach to kitchen stations. Tracks which worker operates this station
/// and legacy station output data. Active worker routing is owned by production flows.
/// </summary>
public class StationNode : MonoBehaviour
{
    [Tooltip("Worker currently assigned to operate this station")]
    public KitchenEmployee assignedWorker;
    [Tooltip("All workers allowed to operate this shared station. The first is kept in assignedWorker for legacy UI.")]
    public List<KitchenEmployee> assignedWorkers = new List<KitchenEmployee>();

    [Tooltip("Where product from this station is sent (e.g. Pantry → Grill)")]
    public GameObject outputTarget;

    [Header("I/O Amounts")]
    [Tooltip("How many input items this station consumes per minute.")]
    public float inputAmountPerMinute = 10f;
    [Tooltip("How many output items this station produces per minute.")]
    public float outputAmountPerMinute = 10f;
    [Tooltip("Unit label for input (e.g. patties).")]
    public string inputUnit = "";
    [Tooltip("Unit label for output (e.g. cooked patties).")]
    public string outputUnit = "";

    void Awake()
    {
        SyncAssignedWorkers();
        StationRuntimeMetrics.EnsureOn(gameObject);
        StationConfigurationCaution.Ensure(gameObject);
        // Re-apply balance defaults each run so rates stay consistent.
        EnsureIoDefaults(force: true);
    }

    void Reset()
    {
        EnsureIoDefaults(force: true);
    }

    /// <summary>
    /// Fills I/O amounts from station type. Use force to overwrite existing values.
    /// </summary>
    public void EnsureIoDefaults(bool force = false)
    {
        if (!force && !string.IsNullOrEmpty(outputUnit))
            return;

        // A cycle processes the active worker's carried batch. Source stations show no input.
        int batchSize = GetActiveBatchSize();
        GrillStation grill = GetComponent<GrillStation>();
        AssemblyStation assembly = GetComponent<AssemblyStation>();
        CuttingStation cutting = GetComponent<CuttingStation>();
        FreezerStation freezer = GetComponent<FreezerStation>();
        FryerStation fryer = GetComponent<FryerStation>();
        DrinkStation drink = GetComponent<DrinkStation>();
        PantryStation pantry = GetComponent<PantryStation>();
        if (grill != null)
        {
            int stationBatch = Mathf.Min(batchSize, grill.InputSlotCapacity, grill.OutputSlotCapacity);
            SetIo(RateForCycle(grill.processTimeSeconds, stationBatch),
                RateForCycle(grill.processTimeSeconds, stationBatch),
                ItemLabel(grill.GetSelectedInput(), "select recipe"),
                ItemLabel(grill.GetSelectedOutput(), "select recipe"));
        }
        else if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            float outputRate = RateForCycle(assembly.processTimeSeconds,
                Mathf.Min(batchSize, assembly.MaxProcessBatch));
            int processedAmount = recipe != null ? Mathf.Max(1, recipe.processedInputAmount) : 1;
            int pantryAmount = recipe != null ? Mathf.Max(1, recipe.pantryInputAmount) : 1;
            string processedName = recipe != null && !string.IsNullOrWhiteSpace(recipe.processedInputName)
                ? recipe.processedInputName : "cooked patty";
            string pantryName = recipe != null && recipe.pantryInput != null
                ? (!string.IsNullOrEmpty(recipe.pantryInput.itemName)
                    ? recipe.pantryInput.itemName : recipe.pantryInput.name)
                : "bun";
            string outputName = recipe != null ? recipe.DisplayName : "burger";
            SetIo(outputRate * (processedAmount + pantryAmount), outputRate,
                processedName + " + " + pantryName, outputName);
        }
        else if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            int stationBatch = Mathf.Min(batchSize, cutting.InputSlotCapacity, cutting.OutputSlotCapacity);
            SetIo(RateForCycle(cutting.processTimeSeconds, stationBatch),
                RateForCycle(cutting.processTimeSeconds, stationBatch),
                ItemLabel(recipe != null ? recipe.input : null, "select ingredient"),
                ItemLabel(recipe != null ? recipe.output : null, "select recipe"));
        }
        else if (freezer != null)
            SetIo(0f, RateForCycle(freezer.processTimeSeconds, batchSize), "-",
                ItemLabel(freezer.selectedItem, "select ingredient"));
        else if (fryer != null)
        {
            int stationBatch = Mathf.Min(batchSize, fryer.InputSlotCapacity, fryer.OutputSlotCapacity);
            SetIo(RateForCycle(fryer.processTimeSeconds, stationBatch),
                RateForCycle(fryer.processTimeSeconds, stationBatch),
                ItemLabel(fryer.GetSelectedInput(), "select recipe"),
                ItemLabel(fryer.GetSelectedOutput(), "select recipe"));
        }
        else if (drink != null)
            SetIo(0f, RateForCycle(drink.processTimeSeconds, batchSize), "-", "drinks");
        else if (pantry != null)
            SetIo(0f, RateForCycle(pantry.processTimeSeconds, batchSize), "-",
                pantry.StoredItemsLabel);
        else if (GetComponent<Register>() != null)
            SetIo(0f, 10f, "-", "orders");
        else
            SetIo(0f, 10f, "-", "items");
    }

    static string ItemLabel(ItemDefinition item, string fallback)
    {
        if (item == null) return fallback;
        return !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
    }

    int GetActiveBatchSize()
    {
        int batch = 1;
        SyncAssignedWorkers();
        foreach (KitchenEmployee worker in assignedWorkers)
            if (worker != null)
                batch = Mathf.Max(batch, worker.CarryCapacity);

        ProductionManager production = ProductionManager.Instance;
        if (production?.productionFlows == null) return batch;
        foreach (ProductionFlowPlan flow in production.productionFlows)
        {
            if (flow?.stations == null || !flow.stations.Contains(gameObject) || flow.workers == null) continue;
            foreach (KitchenEmployee worker in flow.workers)
                if (worker != null)
                    batch = Mathf.Max(batch, worker.CarryCapacity);
        }
        return Mathf.Clamp(batch, 1, 4);
    }

    static float RateForCycle(float seconds, int batchSize)
    {
        return seconds > 0.001f ? 60f * Mathf.Max(1, batchSize) / seconds : 0f;
    }

    void SetIo(float input, float output, string inUnit, string outUnit)
    {
        inputAmountPerMinute = input;
        outputAmountPerMinute = output;
        inputUnit = inUnit;
        outputUnit = outUnit;
    }

    public bool HasInputAmount => inputAmountPerMinute > 0.01f;

    public string DisplayName
    {
        get
        {
            var t = KitchenEmployee.GetStationTypeFrom(gameObject);
            if (GetComponent<ShakeStation>() != null) return "Shake Station MK" + StationMark;
            if (GetComponent<AssemblyStation>() != null) return "Assembly Station MK" + StationMark;
            if (GetComponent<CuttingStation>() != null) return "Cutting Station MK" + StationMark;
            if (GetComponent<GrillStation>() != null) return "Grill MK" + StationMark;
            if (GetComponent<FryerStation>() != null) return "Fryer MK" + StationMark;
            if (GetComponent<HeatLampStation>() != null) return "Pickup Station MK" + StationMark;
            if (t.HasValue) return t.Value.ToString();
            return gameObject.name;
        }
    }

    public int StationMark
    {
        get
        {
            var assembly = GetComponent<AssemblyStation>();
            if (assembly != null) return assembly.IsMk2 ? 2 : 1;
            var cutting = GetComponent<CuttingStation>();
            if (cutting != null) return cutting.InputSlotCapacity >= 4 ? 2 : 1;
            var grill = GetComponent<GrillStation>();
            if (grill != null) return grill.InputSlotCapacity >= 4 ? 2 : 1;
            var fryer = GetComponent<FryerStation>();
            if (fryer != null) return fryer.InputSlotCapacity >= 4 ? 2 : 1;
            var pickup = GetComponent<HeatLampStation>();
            if (pickup != null) return pickup.InputSlotCapacity >= 4 ? 2 : 1;
            return 0;
        }
    }

    public StationType? StationType => KitchenEmployee.GetStationTypeFrom(gameObject);

    public bool IsWorkStation => StationType.HasValue;

    public bool HasAssignedWorker
    {
        get
        {
            SyncAssignedWorkers();
            return assignedWorkers.Count > 0;
        }
    }

    public bool IsWorkerAssigned(KitchenEmployee worker)
    {
        if (worker == null) return false;
        SyncAssignedWorkers();
        return assignedWorkers.Contains(worker);
    }

    void SyncAssignedWorkers()
    {
        if (assignedWorkers == null)
            assignedWorkers = new List<KitchenEmployee>();
        assignedWorkers.RemoveAll(worker => worker == null);
        if (assignedWorker != null && !assignedWorkers.Contains(assignedWorker))
            assignedWorkers.Insert(0, assignedWorker);
        assignedWorker = assignedWorkers.Count > 0 ? assignedWorkers[0] : null;
    }

    public void AddWorker(KitchenEmployee worker)
    {
        if (worker == null) return;
        SyncAssignedWorkers();
        if (!assignedWorkers.Contains(worker))
            assignedWorkers.Add(worker);
        assignedWorker = assignedWorkers[0];
        worker.AddOperatedStation(gameObject);
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
    }

    public void RemoveWorker(KitchenEmployee worker)
    {
        if (worker == null) return;
        SyncAssignedWorkers();
        assignedWorkers.Remove(worker);
        if (worker.IsAssignedTo(gameObject))
            worker.RemoveOperatedStation(gameObject);
        assignedWorker = assignedWorkers.Count > 0 ? assignedWorkers[0] : null;
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
    }

    public void SetWorker(KitchenEmployee worker)
    {
        SyncAssignedWorkers();
        foreach (KitchenEmployee existing in new List<KitchenEmployee>(assignedWorkers))
            if (existing != worker)
                RemoveWorker(existing);
        if (worker == null)
            ClearWorker();
        else
            AddWorker(worker);
    }

    public void ClearWorker()
    {
        SyncAssignedWorkers();
        foreach (KitchenEmployee worker in new List<KitchenEmployee>(assignedWorkers))
            if (worker != null && worker.IsAssignedTo(gameObject))
                worker.RemoveOperatedStation(gameObject);
        assignedWorkers.Clear();
        assignedWorker = null;
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
    }

    public void SetOutput(GameObject target)
    {
        if (target == gameObject) return;
        if (outputTarget == target) return;

        bool isNewLink = target != null;
        outputTarget = target;
        StationOutputLinkVisuals.NotifyLinksChanged();
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();

        if (isNewLink)
            RaiseOutputAssignedEvents();
    }

    static bool raisedFirstOutputEvent;

    static void RaiseOutputAssignedEvents()
    {
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.OutputLinked);
        if (raisedFirstOutputEvent) return;
        raisedFirstOutputEvent = true;
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.OutputAssigned);
    }

    public void ClearOutput()
    {
        outputTarget = null;
        StationOutputLinkVisuals.NotifyLinksChanged();
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
    }

    /// <summary>Heat lamp linked via Assign Output, or null.</summary>
    public HeatLampStation GetOutputHeatLamp()
    {
        if (outputTarget == null) return null;
        return outputTarget.GetComponent<HeatLampStation>();
    }

    public bool HasOutput => outputTarget != null;

    public StationType? GetOutputStationType()
    {
        if (outputTarget == null) return null;
        return KitchenEmployee.GetStationTypeFrom(outputTarget);
    }

    public bool OutputIsHeatLamp => GetOutputHeatLamp() != null;

    /// <summary>Ensure StationNode exists on known station objects.</summary>
    public static StationNode EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var node = go.GetComponent<StationNode>();
        if (node == null) node = go.AddComponent<StationNode>();
        StationRuntimeMetrics.EnsureOn(go);
        StationConfigurationCaution.Ensure(go);
        return node;
    }

    public static StationNode FindFromCollider(Collider col)
    {
        if (col == null) return null;
        var go = col.GetComponentInParent<FreezerStation>()?.gameObject
            ?? col.GetComponentInParent<GrillStation>()?.gameObject
            ?? col.GetComponentInParent<PantryStation>()?.gameObject
            ?? col.GetComponentInParent<AssemblyStation>()?.gameObject
            ?? col.GetComponentInParent<CuttingStation>()?.gameObject
            ?? col.GetComponentInParent<FryerStation>()?.gameObject
            ?? col.GetComponentInParent<DrinkStation>()?.gameObject
            ?? col.GetComponentInParent<HeatLampStation>()?.gameObject
            ?? col.GetComponentInParent<Register>()?.gameObject;
        return go != null ? EnsureOn(go) : null;
    }
}

public enum StationRuntimeState
{
    Idle,
    Working,
    Starved,
    Blocked
}

/// <summary>
/// Measures what a station actually spends time doing. The totals are runtime observations,
/// not theoretical rates, so they expose starving and blocked buffers caused by the layout.
/// </summary>
public sealed class StationRuntimeMetrics : MonoBehaviour
{
    struct OutputSample { public float time; public int units; }
    readonly Queue<OutputSample> outputSamples = new Queue<OutputSample>();
    float outputTrackingStartedAt;
    StationNode node;
    float workingSeconds;
    float starvedSeconds;
    float blockedSeconds;
    float idleSeconds;
    StationRuntimeState previousState = StationRuntimeState.Idle;

    public StationRuntimeState CurrentState { get; private set; } = StationRuntimeState.Idle;
    public float CurrentStateSeconds { get; private set; }
    public float TotalSeconds => workingSeconds + starvedSeconds + blockedSeconds + idleSeconds;
    public float WorkingSeconds => workingSeconds;
    public float StarvedSeconds => starvedSeconds;
    public float BlockedSeconds => blockedSeconds;
    public float IdleSeconds => idleSeconds;
    public float WorkingPercent => Percent(workingSeconds);
    public float StarvedPercent => Percent(starvedSeconds);
    public float BlockedPercent => Percent(blockedSeconds);
    public float IdlePercent => Percent(idleSeconds);
    /// <summary>
    /// Productive time divided only by time in which production was demanded.
    /// Intentional idle time after targets are met does not reduce efficiency.
    /// </summary>
    public float DemandSeconds => workingSeconds + starvedSeconds + blockedSeconds;
    public float EfficiencyPercent => DemandSeconds > 0.01f
        ? workingSeconds * 100f / DemandSeconds : 0f;
    public float ActualOutputPerMinute
    {
        get
        {
            PruneOutputSamples();
            int units = 0;
            foreach (OutputSample sample in outputSamples) units += sample.units;
            float window = Mathf.Clamp(Time.time - outputTrackingStartedAt, 1f, 60f);
            return units * 60f / window;
        }
    }

    public static StationRuntimeMetrics EnsureOn(GameObject station)
    {
        if (station == null) return null;
        StationRuntimeMetrics metrics = station.GetComponent<StationRuntimeMetrics>();
        return metrics != null ? metrics : station.AddComponent<StationRuntimeMetrics>();
    }

    void Awake()
    {
        node = GetComponent<StationNode>();
        outputTrackingStartedAt = Time.time;
    }

    public void RecordOutput(int units)
    {
        if (units <= 0) return;
        outputSamples.Enqueue(new OutputSample { time = Time.time, units = units });
        PruneOutputSamples();
    }

    void PruneOutputSamples()
    {
        float cutoff = Time.time - 60f;
        while (outputSamples.Count > 0 && outputSamples.Peek().time < cutoff)
            outputSamples.Dequeue();
    }

    void Update()
    {
        if (node == null) node = GetComponent<StationNode>();
        CurrentState = EvaluateState();
        float elapsed = Time.deltaTime;
        if (CurrentState == previousState)
            CurrentStateSeconds += elapsed;
        else
        {
            previousState = CurrentState;
            CurrentStateSeconds = 0f;
        }
        switch (CurrentState)
        {
            case StationRuntimeState.Working: workingSeconds += elapsed; break;
            case StationRuntimeState.Starved: starvedSeconds += elapsed; break;
            case StationRuntimeState.Blocked: blockedSeconds += elapsed; break;
            default: idleSeconds += elapsed; break;
        }
    }

    float Percent(float seconds)
    {
        return TotalSeconds > 0.01f ? seconds * 100f / TotalSeconds : 0f;
    }

    StationRuntimeState EvaluateState()
    {
        if (node != null && node.assignedWorkers != null)
        {
            foreach (KitchenEmployee worker in node.assignedWorkers)
            {
                if (worker == null || worker.GetCurrentStationObject() != gameObject) continue;
                if (worker.IsActivelyWorkingAt(gameObject)) return StationRuntimeState.Working;
                if (worker.CurrentActivity == KitchenEmployee.WorkerActivityState.Blocked)
                    return StationRuntimeState.Blocked;
            }
        }

        AssemblyStation assembly = GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            if (assembly.IsProcessing) return StationRuntimeState.Working;
            if (assembly.BufferedOutputCount >= assembly.OutputSlotCapacity)
                return StationRuntimeState.Blocked;
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            if (recipe != null)
            {
                if (assembly.BufferedProcessedInputCount < Mathf.Max(1, recipe.processedInputAmount)
                    || assembly.BufferedPantryInputCount < Mathf.Max(1, recipe.pantryInputAmount)
                    || (recipe.thirdInput != null && assembly.BufferedThirdInputCount
                        < Mathf.Max(1, recipe.thirdInputAmount)))
                    return StationRuntimeState.Starved;
            }
        }

        GrillStation grill = GetComponent<GrillStation>();
        if (grill != null)
        {
            if (grill.IsCookingPatty) return StationRuntimeState.Working;
            if (grill.IsCooked() && grill.BufferedPattyCount >= grill.SlotCapacity)
                return StationRuntimeState.Blocked;
            if (!grill.HasPattyOnGrill) return StationRuntimeState.Starved;
        }

        // No queued demand and no physical work-in-progress is intentional idle
        // time. It must not be reported as starvation or reduce efficiency.
        ProductionManager manager = ProductionManager.Instance;
        if (!HasBufferedWork() && (manager == null || !manager.HasActiveDemandForStation(gameObject)))
            return StationRuntimeState.Idle;

        CuttingStation cutting = GetComponent<CuttingStation>();
        if (cutting != null)
        {
            if (cutting.IsProcessing) return StationRuntimeState.Working;
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            if (recipe != null && cutting.GetOutputCount(recipe.output) >= cutting.OutputSlotCapacity)
                return StationRuntimeState.Blocked;
            if (recipe != null && cutting.GetInputCount(recipe.input) <= 0)
                return StationRuntimeState.Starved;
        }

        FryerStation fryer = GetComponent<FryerStation>();
        if (fryer != null)
        {
            if (fryer.IsCooking) return StationRuntimeState.Working;
            if (fryer.GetOutputCount(fryer.GetSelectedOutput()) >= fryer.OutputSlotCapacity)
                return StationRuntimeState.Blocked;
            if (fryer.GetInputCount(fryer.GetSelectedInput()) <= 0)
                return StationRuntimeState.Starved;
        }

        HeatLampStation pickup = GetComponent<HeatLampStation>();
        if (pickup != null)
            return pickup.Count >= pickup.maxCapacity ? StationRuntimeState.Blocked : StationRuntimeState.Idle;

        FreezerStation freezer = GetComponent<FreezerStation>();
        if (freezer != null)
        {
            if (!freezer.HasItemSelected || freezer.GetOutputCount(freezer.selectedItem) <= 0)
                return StationRuntimeState.Starved;
        }


        PantryStation pantry = GetComponent<PantryStation>();
        if (pantry != null && (!pantry.HasItemSelected || !pantry.HasAnyStock))
            return StationRuntimeState.Starved;

        return StationRuntimeState.Idle;
    }

    bool HasBufferedWork()
    {
        AssemblyStation assembly = GetComponent<AssemblyStation>();
        if (assembly != null)
            return assembly.IsProcessing || assembly.BufferedProcessedInputCount > 0
                || assembly.BufferedPantryInputCount > 0 || assembly.BufferedThirdInputCount > 0
                || assembly.BufferedOutputCount > 0;
        GrillStation grill = GetComponent<GrillStation>();
        if (grill != null) return grill.BufferedPattyCount > 0;
        CuttingStation cutting = GetComponent<CuttingStation>();
        if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            return recipe != null && (cutting.GetInputCount(recipe.input) > 0
                || cutting.GetOutputCount(recipe.output) > 0 || cutting.IsProcessing);
        }
        FryerStation fryer = GetComponent<FryerStation>();
        if (fryer != null) return fryer.BufferedUnitCount > 0;
        return false;
    }
}
