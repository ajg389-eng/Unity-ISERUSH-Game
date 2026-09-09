using System;

/// <summary>
/// Global tutorial voice event bus. Raise events from anywhere:
/// TutorialVoiceEvents.Raise(TutorialVoiceEventId.ShiftStarted);
/// </summary>
public static class TutorialVoiceEvents
{
    public static event Action<string> OnEvent;

    public static void Raise(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;
        OnEvent?.Invoke(eventId);
    }
}

/// <summary>Common tutorial / mission event ids. Add more as the game grows.</summary>
public static class TutorialVoiceEventId
{
    public const string ShiftStarted = "shift_started";
    public const string FirstCustomerArrived = "first_customer_arrived";
    public const string QueueGrowing = "queue_growing";
    public const string FirstWorkerHired = "first_worker_hired";
    public const string OutputAssigned = "output_assigned";
    public const string FirstOrderServed = "first_order_served";

    // Repeatable gameplay events (milestone tasks)
    public const string OrderServed = "order_served";
    public const string WorkerHired = "worker_hired";
    public const string WorkerAssigned = "worker_assigned";
    public const string OutputLinked = "output_linked";
    public const string DayEnded = "day_ended";
    public const string IngredientsOrdered = "ingredients_ordered";
    public const string StationPurchased = "station_purchased";
    public const string FloorExpanded = "floor_expanded";
    public const string CapacityInvested = "capacity_invested";
}
