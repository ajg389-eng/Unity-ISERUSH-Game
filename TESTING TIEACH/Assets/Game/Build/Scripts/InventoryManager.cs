using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public List<ItemDefinition> allItems = new List<ItemDefinition>();

    [Header("Station capacity")]
    [Min(1), Tooltip("Owned copies of each station type once Milestone 2 is reached.")]
    public int baseStationCapacity = 2;
    [Min(0), Tooltip("Additional copies of every station type for each numbered milestone after 2.")]
    public int capacityPerCompletedMilestone = 1;

    private Dictionary<ItemDefinition, int> counts = new Dictionary<ItemDefinition, int>();
    /// <summary>Total units ever acquired (starting stock + purchases). First of each item is free.</summary>
    private Dictionary<ItemDefinition, int> acquired = new Dictionary<ItemDefinition, int>();

    public MoneyManager money;
    public ItemDefinition SelectedItem { get; private set; }

    void Awake()
    {
        foreach (var item in allItems)
        {
            if (item == null) continue;
            int start = Mathf.Max(0, item.startingQuantity);
            counts[item] = start;
            acquired[item] = start;
        }
    }

    public int GetCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return counts.TryGetValue(item, out int c) ? c : 0;
    }

    /// <summary>How many of this item have been acquired in total (stock + placed purchases).</summary>
    public int GetAcquiredCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return acquired.TryGetValue(item, out int c) ? c : 0;
    }

    /// <summary>Total owned capacity for one station type, including milestone rewards.</summary>
    public int GetStationCapacity(ItemDefinition item)
    {
        if (item == null) return 0;
        int reached = MilestoneFeatures.HighestReachedNumberedStage();
        if (reached < MilestoneFeatures.CapacityAndOptimization)
            return 1;

        int extraMilestones = reached - MilestoneFeatures.CapacityAndOptimization;
        return Mathf.Max(1, baseStationCapacity + extraMilestones * capacityPerCompletedMilestone);
    }

    public bool IsAtStationCapacity(ItemDefinition item)
    {
        return item == null || GetAcquiredCount(item) >= GetStationCapacity(item);
    }

    /// <summary>Shop price: first unit of each station/item is free.</summary>
    public int GetPurchasePrice(ItemDefinition item)
    {
        if (item == null) return 0;
        return GetAcquiredCount(item) <= 0 ? 0 : Mathf.Max(0, item.price);
    }

    public void SelectItem(ItemDefinition item)
    {
        SelectedItem = item;
    }

    public bool CanPurchase(ItemDefinition item)
    {
        return item != null && !IsAtStationCapacity(item);
    }

    public bool PurchaseOne(ItemDefinition item)
    {
        if (!CanPurchase(item)) return false;
        if (!counts.ContainsKey(item)) counts[item] = 0;
        if (!acquired.ContainsKey(item)) acquired[item] = 0;

        int price = GetPurchasePrice(item);
        int paid = 0;
        if (price > 0 && money != null)
        {
            if (!money.TrySpend(price)) return false;
            paid = price;
        }

        counts[item] += 1;
        acquired[item] += 1;
        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
            undo.RecordStationPurchase(item, paid);
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.StationPurchased);
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.CapacityInvested);
        return true;
    }

    /// <summary>Called when undoing a purchase so the next buy can be free again if appropriate.</summary>
    public void NotifyPurchaseUndone(ItemDefinition item)
    {
        if (item == null) return;
        if (!acquired.ContainsKey(item)) return;
        acquired[item] = Mathf.Max(0, acquired[item] - 1);
    }

    public bool TryConsumeOne(ItemDefinition item)
    {
        if (item == null) return false;
        if (!counts.TryGetValue(item, out int c)) return false;
        if (c <= 0) return false;
        counts[item] = c - 1;
        return true;
    }

    /// <summary>Add one item back to inventory (e.g. when removing a placed object). Does not spend money.</summary>
    public void AddOne(ItemDefinition item)
    {
        if (item == null) return;
        if (!counts.ContainsKey(item)) counts[item] = 0;
        counts[item] += 1;
    }
}
