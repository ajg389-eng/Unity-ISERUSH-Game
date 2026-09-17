using UnityEngine;

/// <summary>
/// Attach to kitchen stations. Tracks which worker operates this station
/// and where its output is routed (Assign Output → click another station).
/// </summary>
public class StationNode : MonoBehaviour
{
    [Tooltip("Worker currently assigned to operate this station")]
    public KitchenEmployee assignedWorker;

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

        // inputAmount 0 = no input shown (source stations)
        if (GetComponent<GrillStation>() != null)
            SetIo(5f, 5f, "patties", "cooked patties");
        else if (GetComponent<AssemblyStation>() != null)
            SetIo(10f, 10f, "cooked patties", "burgers");
        else if (GetComponent<FreezerStation>() != null)
            SetIo(0f, 5f, "-", "patties");
        else if (GetComponent<FryerStation>() != null)
            SetIo(10f, 10f, "raw fries", "cooked fries");
        else if (GetComponent<DrinkStation>() != null)
            SetIo(0f, 10f, "-", "drinks");
        else if (GetComponent<PantryStation>() != null)
            SetIo(0f, 10f, "-", "ingredients");
        else if (GetComponent<Register>() != null)
            SetIo(0f, 10f, "-", "orders");
        else
            SetIo(0f, 10f, "-", "items");
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

    public void SetWorker(KitchenEmployee worker)
    {
        if (assignedWorker == worker) return;

        if (assignedWorker != null)
            assignedWorker.RemoveOperatedStation(gameObject);

        assignedWorker = worker;

        if (worker != null)
            worker.AddOperatedStation(gameObject);

        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
    }

    public void ClearWorker()
    {
        SetWorker(null);
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
