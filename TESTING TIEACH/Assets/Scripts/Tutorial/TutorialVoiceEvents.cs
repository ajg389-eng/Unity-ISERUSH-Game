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

/// <summary>Common tutorial event ids. Add more as the game grows.</summary>
public static class TutorialVoiceEventId
{
    public const string ShiftStarted = "shift_started";
    public const string FirstCustomerArrived = "first_customer_arrived";
    public const string QueueGrowing = "queue_growing";
    public const string FirstWorkerHired = "first_worker_hired";
    public const string OutputAssigned = "output_assigned";
    public const string FirstOrderServed = "first_order_served";
}
