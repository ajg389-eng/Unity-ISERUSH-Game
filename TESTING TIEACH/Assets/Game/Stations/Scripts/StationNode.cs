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
    public const float MinimumProductionRatePercent = 1f;
    public const float MaximumProductionRatePercent = 100f;
    static readonly Dictionary<string, StationNode> activeStationsById =
        new Dictionary<string, StationNode>(System.StringComparer.Ordinal);
    static readonly Dictionary<StationNode, string> displayNameCache =
        new Dictionary<StationNode, string>();
    static bool displayNameCacheDirty = true;

    [SerializeField, HideInInspector] string stableStationId;

    /// <summary>Persistent identity for this physical station instance.</summary>
    public string StableStationId
    {
        get
        {
            EnsureStableStationId();
            return stableStationId;
        }
    }

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
    [Header("Production rate")]
    [Tooltip("Clock speed for this station. The target rate scales from the selected recipe's base capacity.")]
    [SerializeField, Range(MinimumProductionRatePercent, MaximumProductionRatePercent)]
    float productionRatePercent = 100f;
    [System.NonSerialized] float baseOutputAmountPerMinute;

    public float ProductionRatePercent => Mathf.Clamp(productionRatePercent,
        MinimumProductionRatePercent, MaximumProductionRatePercent);
    public float ProductionRateMultiplier => ProductionRatePercent / 100f;
    public float BaseOutputAmountPerMinute => baseOutputAmountPerMinute;
    public bool SupportsProductionRateControl => GetComponent<FreezerStation>() != null
        || GetComponent<GrillStation>() != null
        || GetComponent<AssemblyStation>() != null
        || GetComponent<CuttingStation>() != null
        || GetComponent<FryerStation>() != null
        || GetComponent<DrinkStation>() != null
        || GetComponent<PantryStation>() != null;

    void Awake()
    {
        EnsureStableStationId();
        productionRatePercent = Mathf.Clamp(productionRatePercent,
            MinimumProductionRatePercent, MaximumProductionRatePercent);
        SyncAssignedWorkers();
        StationRuntimeMetrics.EnsureOn(gameObject);
        StationConfigurationCaution.Ensure(gameObject);
        // Re-apply balance defaults each run so rates stay consistent.
        EnsureIoDefaults(force: true);
    }

    void OnEnable()
    {
        EnsureStableStationId();
        displayNameCacheDirty = true;
        ProductionManager.Instance?.InvalidateStationCache();
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(stableStationId)
            && activeStationsById.TryGetValue(stableStationId, out StationNode active)
            && active == this)
            activeStationsById.Remove(stableStationId);
        displayNameCacheDirty = true;
        ProductionManager.Instance?.InvalidateStationCache();
    }

    void EnsureStableStationId()
    {
        if (string.IsNullOrWhiteSpace(stableStationId))
            stableStationId = System.Guid.NewGuid().ToString("N");

        if (activeStationsById.TryGetValue(stableStationId, out StationNode existing)
            && existing != null && existing != this)
            stableStationId = System.Guid.NewGuid().ToString("N");

        if (isActiveAndEnabled)
            activeStationsById[stableStationId] = this;
    }

    /// <summary>Restores the saved identity before saved flow links are used.</summary>
    public void RestoreStableStationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            EnsureStableStationId();
            return;
        }

        if (!string.IsNullOrEmpty(stableStationId)
            && activeStationsById.TryGetValue(stableStationId, out StationNode active)
            && active == this)
            activeStationsById.Remove(stableStationId);
        stableStationId = value.Trim();
        EnsureStableStationId();
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

        // Station capacity is the machine's own recipe rate. Worker travel and
        // carry limits are accounted for separately by flow labor metrics.
        GrillStation grill = GetComponent<GrillStation>();
        AssemblyStation assembly = GetComponent<AssemblyStation>();
        CuttingStation cutting = GetComponent<CuttingStation>();
        FreezerStation freezer = GetComponent<FreezerStation>();
        FryerStation fryer = GetComponent<FryerStation>();
        DrinkStation drink = GetComponent<DrinkStation>();
        PantryStation pantry = GetComponent<PantryStation>();
        if (grill != null)
        {
            ItemDefinition secondaryInput = grill.GetSelectedSecondaryInput();
            float outputRate = RateForCycle(grill.BaseProcessCycleSeconds);
            string inputLabel = ItemLabel(grill.GetSelectedInput(), "select recipe");
            if (secondaryInput != null)
                inputLabel += " + " + ItemLabel(secondaryInput, "select recipe");
            SetIo(outputRate * (secondaryInput != null ? 2f : 1f),
                outputRate,
                inputLabel,
                ItemLabel(grill.GetSelectedOutput(), "select recipe"));
        }
        else if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            float outputRate = recipe != null
                ? RateForCycle(assembly.BaseProcessCycleSeconds) : 0f;
            int processedAmount = recipe != null && recipe.RequiresProcessedInput
                ? Mathf.Max(1, recipe.processedInputAmount) : 0;
            int pantryAmount = recipe != null && recipe.pantryInput != null
                ? Mathf.Max(1, recipe.pantryInputAmount) : 0;
            int thirdAmount = recipe != null && recipe.thirdInput != null
                ? Mathf.Max(1, recipe.thirdInputAmount) : 0;
            string processedName = recipe != null && !string.IsNullOrWhiteSpace(recipe.processedInputName)
                ? recipe.processedInputName
                : recipe != null && recipe.RequiresProcessedInput ? "cooked patty" : "";
            string pantryName = recipe != null && recipe.pantryInput != null
                ? (!string.IsNullOrEmpty(recipe.pantryInput.itemName)
                    ? recipe.pantryInput.itemName : recipe.pantryInput.name)
                : "";
            string thirdName = recipe != null && recipe.thirdInput != null
                ? (!string.IsNullOrEmpty(recipe.thirdInput.itemName)
                    ? recipe.thirdInput.itemName : recipe.thirdInput.name)
                : "";
            string outputName = recipe != null ? recipe.DisplayName : "burger";
            var inputNames = new List<string>();
            if (processedAmount > 0) inputNames.Add(processedName);
            if (pantryAmount > 0) inputNames.Add(pantryName);
            if (thirdAmount > 0) inputNames.Add(thirdName);
            SetIo(outputRate * (processedAmount + pantryAmount + thirdAmount), outputRate,
                inputNames.Count > 0 ? string.Join(" + ", inputNames) : "-", outputName);
        }
        else if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            SetIo(RateForCycle(cutting.BaseProcessCycleSeconds),
                RateForCycle(cutting.BaseProcessCycleSeconds),
                ItemLabel(recipe != null ? recipe.input : null, "select ingredient"),
                ItemLabel(recipe != null ? recipe.output : null, "select recipe"));
        }
        else if (freezer != null)
            SetIo(0f, Mathf.Max(0f, freezer.maxOutputPerMinute), "-",
                ItemLabel(freezer.selectedItem, "select ingredient"));
        else if (fryer != null)
        {
            SetIo(RateForCycle(fryer.BaseProcessCycleSeconds),
                RateForCycle(fryer.BaseProcessCycleSeconds),
                ItemLabel(fryer.GetSelectedInput(), "select recipe"),
                ItemLabel(fryer.GetSelectedOutput(), "select recipe"));
        }
        else if (drink != null)
            SetIo(0f, RateForCycle(drink.processTimeSeconds), "-", "drinks");
        else if (pantry != null)
            SetIo(0f, Mathf.Max(0f, pantry.maxOutputPerMinute), "-",
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

    static float RateForCycle(float seconds)
    {
        return seconds > 0.001f ? 60f / seconds : 0f;
    }

    /// <summary>Returns an MK1 recipe cycle adjusted for the selected station tier.</summary>
    public static float GetTierAdjustedCycleSeconds(Component station, float mk1CycleSeconds)
    {
        if (station == null || mk1CycleSeconds <= 0f) return mk1CycleSeconds;
        StationNode node = station.GetComponent<StationNode>();
        return node != null && node.StationMark >= 2 ? mk1CycleSeconds * 0.5f : mk1CycleSeconds;
    }

    void SetIo(float input, float output, string inUnit, string outUnit)
    {
        baseOutputAmountPerMinute = Mathf.Max(0f, output);
        float rateScale = SupportsProductionRateControl ? ProductionRateMultiplier : 1f;
        inputAmountPerMinute = Mathf.Max(0f, input) * rateScale;
        outputAmountPerMinute = baseOutputAmountPerMinute * rateScale;
        inputUnit = inUnit;
        outputUnit = outUnit;
    }

    public void SetProductionRatePercent(float percent)
    {
        if (!SupportsProductionRateControl) return;
        productionRatePercent = Mathf.Clamp(percent,
            MinimumProductionRatePercent, MaximumProductionRatePercent);
        EnsureIoDefaults(force: true);
    }

    public void SetTargetOutputPerMinute(float targetRate)
    {
        if (!SupportsProductionRateControl) return;
        EnsureIoDefaults(force: true);
        if (baseOutputAmountPerMinute <= 0.001f) return;
        float requestedPercent = targetRate / baseOutputAmountPerMinute * 100f;
        SetProductionRatePercent(requestedPercent);
    }

    public float GetEffectiveCycleSeconds(float nominalSeconds)
    {
        return nominalSeconds > 0f ? nominalSeconds / ProductionRateMultiplier : nominalSeconds;
    }

    public float GetEffectiveOutputRate(float nominalRate)
    {
        return Mathf.Max(0f, nominalRate) * ProductionRateMultiplier;
    }

    public static float GetEffectiveCycleSeconds(Component station, float nominalSeconds)
    {
        if (station == null) return nominalSeconds;
        StationNode node = station.GetComponent<StationNode>();
        return node != null ? node.GetEffectiveCycleSeconds(nominalSeconds) : nominalSeconds;
    }

    public bool HasInputAmount => inputAmountPerMinute > 0.01f;

    public string DisplayName
    {
        get
        {
            if (displayNameCacheDirty) RebuildDisplayNameCache();
            return displayNameCache.TryGetValue(this, out string label)
                ? label : BuildBaseDisplayName();
        }
    }

    string BuildBaseDisplayName()
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

    static void RebuildDisplayNameCache()
    {
        displayNameCache.Clear();
        StationNode[] nodes = FindObjectsByType<StationNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var groups = new Dictionary<string, List<StationNode>>();
        for (int i = 0; i < nodes.Length; i++)
        {
            StationNode node = nodes[i];
            if (node == null) continue;
            string baseName = node.BuildBaseDisplayName();
            if (!groups.TryGetValue(baseName, out List<StationNode> group))
            {
                group = new List<StationNode>();
                groups.Add(baseName, group);
            }
            group.Add(node);
        }

        foreach (KeyValuePair<string, List<StationNode>> pair in groups)
        {
            List<StationNode> group = pair.Value;
            if (group.Count == 1)
            {
                displayNameCache[group[0]] = pair.Key;
                continue;
            }

            // Position-based order makes the labels easy to match to the layout.
            group.Sort((a, b) =>
            {
                int order = a.transform.position.x.CompareTo(b.transform.position.x);
                if (order != 0) return order;
                order = a.transform.position.z.CompareTo(b.transform.position.z);
                return order != 0 ? order : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            for (int i = 0; i < group.Count; i++)
                displayNameCache[group[i]] = pair.Key + " #" + (i + 1);
        }

        displayNameCacheDirty = false;
    }

    public int StationMark
    {
        get
        {
            var assembly = GetComponent<AssemblyStation>();
            if (assembly != null) return assembly.IsMk2 ? 2 : 1;
            var cutting = GetComponent<CuttingStation>();
            if (cutting != null) return cutting.StationMark;
            var grill = GetComponent<GrillStation>();
            if (grill != null) return grill.StationMark;
            var fryer = GetComponent<FryerStation>();
            if (fryer != null) return fryer.StationMark;
            var pickup = GetComponent<HeatLampStation>();
            if (pickup != null) return pickup.StationMark;
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
        node.EnsureStableStationId();
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
    struct EfficiencySample { public float time; public float seconds; public StationRuntimeState state; }

    /// <summary>
    /// Satisfactory's machine productivity display is a rolling uptime value over
    /// approximately five minutes. Keeping the same window makes short stalls
    /// visible without letting a station's entire lifetime hide recent changes.
    /// </summary>
    public const float EfficiencyWindowSeconds = 300f;
    const float StateSampleIntervalSeconds = 0.1f;

    readonly Queue<OutputSample> outputSamples = new Queue<OutputSample>();
    readonly Queue<EfficiencySample> efficiencySamples = new Queue<EfficiencySample>();
    float outputTrackingStartedAt;
    float rollingObservedSeconds;
    float rollingWorkingSeconds;
    StationNode node;
    float workingSeconds;
    float starvedSeconds;
    float blockedSeconds;
    float idleSeconds;
    float stateSampleElapsed;
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
    /// <summary>Lifetime time spent in a state, retained for diagnostics and flow summaries.</summary>
    public float DemandSeconds => workingSeconds + starvedSeconds + blockedSeconds;
    /// <summary>Time represented by the current rolling efficiency window.</summary>
    public float ObservedSeconds => rollingObservedSeconds;
    /// <summary>
    /// Productive uptime over the recent rolling window. Every second spent
    /// starved, blocked, or idle lowers this value, matching Satisfactory's
    /// productivity display behavior.
    /// </summary>
    public float EfficiencyPercent => rollingObservedSeconds > 0.01f
        ? rollingWorkingSeconds * 100f / rollingObservedSeconds : 0f;
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
        ProductionManager.Instance?.NotifyProductionStateChanged();
    }

    public void RecordOutput(ItemDefinition item, int units)
    {
        if (item == null || units <= 0) return;
        RecordOutput(units);
        MissionProgressManager.Instance?.RecordProducedItem(item, units);
    }

    void PruneOutputSamples()
    {
        float cutoff = Time.time - 60f;
        while (outputSamples.Count > 0 && outputSamples.Peek().time < cutoff)
            outputSamples.Dequeue();
    }

    void Update()
    {
        stateSampleElapsed += Time.deltaTime;
        if (stateSampleElapsed < StateSampleIntervalSeconds) return;

        // Station state feeds analytics. Sampling at 10 Hz keeps the display
        // responsive without rechecking buffers on every rendered frame.
        float elapsed = stateSampleElapsed;
        stateSampleElapsed = 0f;
        if (node == null) node = GetComponent<StationNode>();
        CurrentState = EvaluateState();
        if (CurrentState == previousState)
            CurrentStateSeconds += elapsed;
        else
        {
            previousState = CurrentState;
            CurrentStateSeconds = 0f;
            ProductionManager.Instance?.NotifyProductionStateChanged();
        }
        switch (CurrentState)
        {
            case StationRuntimeState.Working: workingSeconds += elapsed; break;
            case StationRuntimeState.Starved: starvedSeconds += elapsed; break;
            case StationRuntimeState.Blocked: blockedSeconds += elapsed; break;
            default: idleSeconds += elapsed; break;
        }
        RecordEfficiencySample(CurrentState, elapsed);
    }

    void RecordEfficiencySample(StationRuntimeState state, float seconds)
    {
        if (seconds <= 0f) return;
        efficiencySamples.Enqueue(new EfficiencySample
        {
            time = Time.time,
            seconds = seconds,
            state = state
        });
        rollingObservedSeconds += seconds;
        if (state == StationRuntimeState.Working)
            rollingWorkingSeconds += seconds;

        float cutoff = Time.time - EfficiencyWindowSeconds;
        while (efficiencySamples.Count > 0 && efficiencySamples.Peek().time < cutoff)
        {
            EfficiencySample sample = efficiencySamples.Dequeue();
            rollingObservedSeconds -= sample.seconds;
            if (sample.state == StationRuntimeState.Working)
                rollingWorkingSeconds -= sample.seconds;
        }

        // Floating point accumulation can leave a tiny negative remainder after
        // pruning many samples. Keep the public values stable at zero.
        rollingObservedSeconds = Mathf.Max(0f, rollingObservedSeconds);
        rollingWorkingSeconds = Mathf.Max(0f, rollingWorkingSeconds);
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

        // An empty station with no unfinished route demand is intentionally idle.
        // Evaluate this before station-specific starvation checks so the scheduler
        // only receives urgency signals for work that is actually needed.
        ProductionManager manager = ProductionManager.Instance;
        if (!HasBufferedWork() && (manager == null || !manager.HasActiveDemandForStation(gameObject)))
            return StationRuntimeState.Idle;

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

        CuttingStation cutting = GetComponent<CuttingStation>();
        if (cutting != null)
        {
            if (cutting.IsProcessing) return StationRuntimeState.Working;
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            if (cutting.BufferedUnitCount >= cutting.SlotCapacity)
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
