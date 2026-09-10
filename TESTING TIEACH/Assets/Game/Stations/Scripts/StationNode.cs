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

    public string DisplayName
    {
        get
        {
            var t = KitchenEmployee.GetStationTypeFrom(gameObject);
            if (t.HasValue) return t.Value.ToString();
            if (GetComponent<HeatLampStation>() != null) return "Heat Lamp";
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
