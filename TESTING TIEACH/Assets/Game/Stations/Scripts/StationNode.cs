using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to kitchen stations. Tracks which worker operates this station
/// and where its output is routed (Assign Output → click another station).
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
        FreezerStation freezer = GetComponent<FreezerStation>();
        FryerStation fryer = GetComponent<FryerStation>();
        DrinkStation drink = GetComponent<DrinkStation>();
        PantryStation pantry = GetComponent<PantryStation>();
        if (grill != null)
            SetIo(RateForCycle(grill.processTimeSeconds, batchSize), RateForCycle(grill.processTimeSeconds, batchSize), "patties", "cooked patties");
        else if (assembly != null)
            SetIo(RateForCycle(assembly.processTimeSeconds, batchSize), RateForCycle(assembly.processTimeSeconds, batchSize), "cooked patties", "burgers");
        else if (freezer != null)
            SetIo(0f, RateForCycle(freezer.processTimeSeconds, batchSize), "-", "patties");
        else if (fryer != null)
            SetIo(RateForCycle(fryer.processTimeSeconds, batchSize), RateForCycle(fryer.processTimeSeconds, batchSize), "raw fries", "cooked fries");
        else if (drink != null)
            SetIo(0f, RateForCycle(drink.processTimeSeconds, batchSize), "-", "drinks");
        else if (pantry != null)
            SetIo(0f, RateForCycle(pantry.processTimeSeconds, batchSize), "-", "ingredients");
        else if (GetComponent<Register>() != null)
            SetIo(0f, 10f, "-", "orders");
        else
            SetIo(0f, 10f, "-", "items");
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
            if (t.HasValue) return t.Value.ToString();
            if (GetComponent<HeatLampStation>() != null) return "Pickup Station";
            return gameObject.name;
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
        return node;
    }

    public static StationNode FindFromCollider(Collider col)
    {
        if (col == null) return null;
        var go = col.GetComponentInParent<FreezerStation>()?.gameObject
            ?? col.GetComponentInParent<GrillStation>()?.gameObject
            ?? col.GetComponentInParent<PantryStation>()?.gameObject
            ?? col.GetComponentInParent<AssemblyStation>()?.gameObject
            ?? col.GetComponentInParent<FryerStation>()?.gameObject
            ?? col.GetComponentInParent<DrinkStation>()?.gameObject
            ?? col.GetComponentInParent<HeatLampStation>()?.gameObject
            ?? col.GetComponentInParent<Register>()?.gameObject;
        return go != null ? EnsureOn(go) : null;
    }
}
