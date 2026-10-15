using UnityEngine;

public partial class ProductionManager
{
    [Header("Production scheduling")]
    [Tooltip("One event simulation controls production trips, station processing, and forecasts.")]
    public bool useScheduledProduction = true;
    DeterministicProductionController scheduledProduction;
    public DeterministicProductionController ScheduledProduction =>
        scheduledProduction ?? (scheduledProduction = new DeterministicProductionController(this));

    public static bool IsScheduledStation(GameObject station) => Instance != null
        && Instance.useScheduledProduction && Instance.scheduledProduction != null
        && Instance.scheduledProduction.OwnsStation(station);
}
