using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public List<ItemDefinition> allItems = new List<ItemDefinition>();

    private Dictionary<ItemDefinition, int> counts = new Dictionary<ItemDefinition, int>();

    public MoneyManager money;
    public ItemDefinition SelectedItem { get; private set; }

    void Awake()
    {
        // Initialize counts
        foreach (var item in allItems)
        {
            if (item == null) continue;
            counts[item] = Mathf.Max(0, item.startingQuantity);
        }
    }

    public int GetCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return counts.TryGetValue(item, out int c) ? c : 0;
    }

    public void SelectItem(ItemDefinition item)
    {
        SelectedItem = item;
    }

    public bool CanPurchase(ItemDefinition item) => item != null; // money later

    public bool PurchaseOne(ItemDefinition item)
    {
        if (item == null) return false;
        if (!counts.ContainsKey(item)) counts[item] = 0;

        // If money manager exists, enforce price
        if (money != null)
        {
            if (!money.TrySpend(item.price)) return false;
        }

        counts[item] += 1;
        return true;
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