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
    public const string StationRepositioned = "station_repositioned";
    public const string Mk2StationPurchased = "mk2_station_purchased";

    public static string RecipeConfigured(ItemDefinition item) =>
        ItemEvent("recipe_configured", item);

    public static string MenuItemServed(ItemDefinition item) =>
        ItemEvent("menu_item_served", item);

    static string ItemEvent(string prefix, ItemDefinition item)
    {
        if (item == null) return null;
        string value = !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : item.name;
        if (string.IsNullOrWhiteSpace(value)) return null;

        var key = new System.Text.StringBuilder(value.Length);
        bool lastWasSeparator = false;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && key.Length > 0)
            {
                key.Append('_');
                lastWasSeparator = true;
            }
        }
        if (key.Length > 0 && key[key.Length - 1] == '_') key.Length--;
        return key.Length > 0 ? prefix + "_" + key : null;
    }
}
