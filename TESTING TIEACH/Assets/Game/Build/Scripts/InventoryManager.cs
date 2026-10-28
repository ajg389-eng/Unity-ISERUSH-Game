using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    const float StationPriceIncreasePerAdditionalCopy = 0.20f;
    const int StationPriceRoundingIncrement = 5;

    public List<ItemDefinition> allItems = new List<ItemDefinition>();

    private Dictionary<ItemDefinition, int> counts = new Dictionary<ItemDefinition, int>();
    /// <summary>Total units ever acquired (starting stock + purchases).</summary>
    private Dictionary<ItemDefinition, int> acquired = new Dictionary<ItemDefinition, int>();

    public MoneyManager money;
    public ItemDefinition SelectedItem { get; private set; }

    void Awake()
    {
        foreach (ItemDefinition stationVariant in Resources.LoadAll<ItemDefinition>("Stations"))
            if (stationVariant != null && !allItems.Contains(stationVariant))
                allItems.Add(stationVariant);

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

    public int GetStationFamilyAcquiredCount(ItemDefinition item)
    {
        if (item == null || !item.IsTieredStation) return GetAcquiredCount(item);
        int total = 0;
        foreach (var pair in acquired)
            if (pair.Key != null && pair.Key.IsTieredStation
                && pair.Key.stationFamily == item.stationFamily)
                total += pair.Value;
        return total;
    }

    public const int UniquePlacementPurchaseCap = 1;

    public bool IsAtUniquePlacementLimit(ItemDefinition item)
    {
        if (item == null) return true;

        // Keep the unique door and customer-wall placement rules. Processing
        // stations have no quantity cap and are limited by cost and floor space.
        bool specialPlacement = item.buildFunction == ItemDefinition.BuildFunction.CustomerDoor
            || item.placementSurface == ItemDefinition.PlacementSurface.CustomerWall;
        return specialPlacement
            && GetStationFamilyAcquiredCount(item) >= UniquePlacementPurchaseCap;
    }

    /// <summary>
    /// Shop price: the first station in a family is free. Each additional copy
    /// increases the base price by 20% of that item's base price per previously
    /// acquired copy, rounded to the nearest $5. MK1 and MK2 share a family count.
    /// </summary>
    public int GetPurchasePrice(ItemDefinition item)
    {
        if (item == null) return 0;
        int familyCount = GetStationFamilyAcquiredCount(item);
        if (familyCount <= 0) return 0;

        int basePrice = Mathf.Max(0, item.price);
        if (basePrice == 0) return 0;

        int additionalCopies = Mathf.Max(0, familyCount - 1);
        float rawPrice = basePrice * (1f + StationPriceIncreasePerAdditionalCopy * additionalCopies);
        return Mathf.Max(0, Mathf.RoundToInt(rawPrice / StationPriceRoundingIncrement)
            * StationPriceRoundingIncrement);
    }

    public void SelectItem(ItemDefinition item)
    {
        SelectedItem = item;
    }

    public bool CanPurchase(ItemDefinition item)
    {
        return !IsShiftInProgress && item != null && !OnboardingTutorial.IsStationLocked(item)
            && !IsResearchLocked(item) && !IsAtUniquePlacementLimit(item);
    }

    static bool IsShiftInProgress => GameTimeManager.Instance != null
        && GameTimeManager.Instance.IsShiftInProgress;

    static bool IsResearchLocked(ItemDefinition item)
    {
        if (item == null || !item.IsTieredStation) return false;
        if (item.stationFamily == "Shake Station" && item.stationMark == 1)
            return !MilestoneFeatures.HasReached(MilestoneFeatures.RestaurantBasics);
        return item.stationMark >= 2 && !MilestoneFeatures.Mk2StationsUnlocked;
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
        if (item.IsTieredStation && item.stationMark >= 2)
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.Mk2StationPurchased);
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
        if (OnboardingTutorial.IsStationLocked(item) || IsResearchLocked(item)) return false;
        if (!counts.TryGetValue(item, out int c)) return false;
        if (c <= 0) return false;
        counts[item] = c - 1;
        return true;
    }

    /// <summary>Add one item back to inventory (e.g. when removing a placed object). Does not spend money.</summary>
    public void DebugClearEquipmentInventory()
    {
        foreach (var item in allItems)
        {
            if (item == null || item.prefab == null) continue;
            var prefab = item.prefab;
            bool equipment = item.buildFunction == ItemDefinition.BuildFunction.Register
                || prefab.GetComponentInChildren<Register>(true) != null
                || prefab.GetComponentInChildren<FreezerStation>(true) != null
                || prefab.GetComponentInChildren<GrillStation>(true) != null
                || prefab.GetComponentInChildren<FryerStation>(true) != null
                || prefab.GetComponentInChildren<DrinkStation>(true) != null
                || prefab.GetComponentInChildren<AssemblyStation>(true) != null
                || prefab.GetComponentInChildren<CuttingStation>(true) != null
                || prefab.GetComponentInChildren<HeatLampStation>(true) != null
                || prefab.GetComponentInChildren<PantryStation>(true) != null;
            if (!equipment) continue;
            counts[item] = 0;
            acquired[item] = 0;
            if (SelectedItem == item) SelectedItem = null;
        }
    }

    public void RestoreCounts(ItemDefinition item, int stored, int totalAcquired)
    {
        if (item == null) return;
        counts[item] = Mathf.Max(0, stored);
        acquired[item] = Mathf.Max(counts[item], totalAcquired);
    }

    public void AddOne(ItemDefinition item)
    {
        if (item == null) return;
        if (!counts.ContainsKey(item)) counts[item] = 0;
        counts[item] += 1;
    }
}
