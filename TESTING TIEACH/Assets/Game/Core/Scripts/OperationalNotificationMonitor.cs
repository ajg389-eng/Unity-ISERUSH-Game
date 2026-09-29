using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Watches operational state that does not expose events and posts only when a
/// condition crosses a meaningful threshold.
/// </summary>
public class OperationalNotificationMonitor : MonoBehaviour
{
    const int LowStockThreshold = 2;
    const int CongestedQueueThreshold = 3;
    const int LowCashThreshold = 100;
    const float PollInterval = 1f;

    readonly Dictionary<ItemDefinition, int> stockBands = new Dictionary<ItemDefinition, int>();
    readonly HashSet<int> congestedRegisters = new HashSet<int>();
    readonly HashSet<int> congestedPickupStations = new HashSet<int>();
    float nextPoll;
    bool cashInitialized;
    bool cashWasLow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<OperationalNotificationMonitor>() != null) return;
        new GameObject("OperationalNotificationMonitor").AddComponent<OperationalNotificationMonitor>();
    }

    void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + PollInterval;
        CheckIngredientStock();
        CheckQueues();
        CheckCash();
    }

    void CheckIngredientStock()
    {
        KitchenInventory inventory = KitchenInventory.Instance;
        if (inventory == null || inventory.stock == null) return;

        foreach (IngredientStockEntry entry in inventory.stock)
        {
            if (entry == null || entry.item == null) continue;
            int band = entry.quantity <= 0 ? 0 : entry.quantity <= LowStockThreshold ? 1 : 2;
            if (!stockBands.TryGetValue(entry.item, out int previous))
            {
                stockBands[entry.item] = band;
                continue;
            }
            if (band >= previous)
            {
                stockBands[entry.item] = band;
                continue;
            }

            string itemName = inventory.GetDisplayName(entry.item);
            string message = band == 0
                ? itemName + " ingredients are out of stock."
                : itemName + " ingredients are running low (" + entry.quantity + " left).";
            NotificationCenter.Post(message, GameNotificationKind.Warning,
                "stock-" + entry.item.GetInstanceID() + "-" + band, 20f);
            stockBands[entry.item] = band;
        }
    }

    void CheckQueues()
    {
        var liveRegisters = new HashSet<int>();
        foreach (Register register in FindObjectsByType<Register>(FindObjectsSortMode.None))
        {
            if (register == null || !register.IsPlacedRegister || !register.isEnabled) continue;
            int id = register.GetInstanceID();
            liveRegisters.Add(id);
            bool congested = register.QueueCount >= CongestedQueueThreshold;
            if (congested && congestedRegisters.Add(id))
            {
                NotificationCenter.Post("A register queue has reached " + register.QueueCount +
                    " customers. Check service capacity.", GameNotificationKind.Warning,
                    "register-queue-" + id, 30f);
            }
            else if (!congested)
                congestedRegisters.Remove(id);
        }
        congestedRegisters.RemoveWhere(id => !liveRegisters.Contains(id));

        var livePickups = new HashSet<int>();
        foreach (HeatLampStation pickup in FindObjectsByType<HeatLampStation>(FindObjectsSortMode.None))
        {
            if (pickup == null || !pickup.isActiveAndEnabled) continue;
            int id = pickup.GetInstanceID();
            livePickups.Add(id);
            bool congested = pickup.PickupQueueCount >= CongestedQueueThreshold;
            if (congested && congestedPickupStations.Add(id))
            {
                NotificationCenter.Post("A pickup queue has reached " + pickup.PickupQueueCount +
                    " customers. Check pickup stock and production flow.", GameNotificationKind.Warning,
                    "pickup-queue-" + id, 30f);
            }
            else if (!congested)
                congestedPickupStations.Remove(id);
        }
        congestedPickupStations.RemoveWhere(id => !livePickups.Contains(id));
    }

    void CheckCash()
    {
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (money == null) return;
        bool low = money.CurrentMoney < LowCashThreshold;
        if (!cashInitialized)
        {
            cashInitialized = true;
            cashWasLow = low;
            return;
        }
        if (low && !cashWasLow)
        {
            NotificationCenter.Post("Cash is below $" + LowCashThreshold + ". Review spending and throughput.",
                GameNotificationKind.Warning, "low-cash", 30f);
        }
        cashWasLow = low;
    }
}
