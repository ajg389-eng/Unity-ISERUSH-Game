using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A customer's order: list of items and quantities (e.g. 1 burger + 1 fries).
/// </summary>
[System.Serializable]
public class CustomerOrder
{
    public List<OrderLine> lines = new List<OrderLine>();

    [System.Serializable]
    public struct OrderLine
    {
        public ItemDefinition item;
        public int quantity;

        public OrderLine(ItemDefinition item, int quantity)
        {
            this.item = item;
            this.quantity = quantity;
        }
    }

    public bool CanFulfill(InventoryManager inventory)
    {
        if (inventory == null) return false;
        foreach (var line in lines)
        {
            if (line.item == null) continue;
            if (inventory.GetCount(line.item) < line.quantity)
                return false;
        }
        return true;
    }

    /// <summary>Returns true if all items were consumed.</summary>
    public bool TryConsumeFrom(InventoryManager inventory)
    {
        if (inventory == null || !CanFulfill(inventory)) return false;
        foreach (var line in lines)
        {
            for (int i = 0; i < line.quantity; i++)
            {
                if (!inventory.TryConsumeOne(line.item))
                    return false;
            }
        }
        return true;
    }

    public string GetDisplayString()
    {
        if (lines == null || lines.Count == 0) return "—";
        var parts = new List<string>();
        foreach (var line in lines)
        {
            if (line.item == null) continue;
            string name = string.IsNullOrEmpty(line.item.itemName) ? line.item.name : line.item.itemName;
            if (string.IsNullOrEmpty(name)) name = "Item";
            if (line.quantity > 1)
                parts.Add(name + " x" + line.quantity);
            else
                parts.Add(name);
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "—";
    }

    /// <summary>True if both orders have the same items and quantities (for matching prepared order to customer).</summary>
    public bool Matches(CustomerOrder other)
    {
        if (other == null || other.lines == null) return lines == null || lines.Count == 0;
        if (lines == null) return false;
        var counts = new Dictionary<ItemDefinition, int>();
        foreach (var line in lines)
        {
            if (line.item == null) continue;
            counts[line.item] = counts.TryGetValue(line.item, out int c) ? c + line.quantity : line.quantity;
        }
        foreach (var line in other.lines)
        {
            if (line.item == null) continue;
            if (!counts.TryGetValue(line.item, out int have) || have < line.quantity) return false;
            counts[line.item] = have - line.quantity;
        }
        foreach (var kv in counts)
            if (kv.Value != 0) return false;
        return true;
    }

    /// <summary>Deep copy for kitchen to deliver a prepared order.</summary>
    public CustomerOrder Clone()
    {
        var copy = new CustomerOrder();
        if (lines != null)
            foreach (var line in lines)
                copy.lines.Add(new OrderLine(line.item, line.quantity));
        return copy;
    }

    /// <summary>Create a single-item order (one heat-lamp meal / production job).</summary>
    public static CustomerOrder FromItem(ItemDefinition item, int quantity = 1)
    {
        var order = new CustomerOrder();
        if (item != null && quantity > 0)
            order.lines.Add(new OrderLine(item, quantity));
        return order;
    }

    /// <summary>First line item, or null.</summary>
    public ItemDefinition PrimaryItem
    {
        get
        {
            if (lines == null) return null;
            foreach (var line in lines)
            {
                if (line.item != null && line.quantity > 0)
                    return line.item;
            }
            return null;
        }
    }

    public int GetTotalQuantity()
    {
        if (lines == null) return 0;
        int n = 0;
        foreach (var line in lines)
        {
            if (line.item == null) continue;
            n += Mathf.Max(0, line.quantity);
        }
        return n;
    }

    /// <summary>Remove one unit of the item from the order. Returns false if it wasn't on the order.</summary>
    public bool TryRemoveOne(ItemDefinition item)
    {
        if (item == null || lines == null) return false;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.item == null || line.quantity <= 0) continue;
            bool same = line.item == item
                || (!string.IsNullOrEmpty(item.itemName) && line.item.itemName == item.itemName);
            if (!same) continue;
            line.quantity--;
            if (line.quantity <= 0)
                lines.RemoveAt(i);
            else
                lines[i] = line;
            return true;
        }
        return false;
    }

    public int GetSalePrice()
    {
        if (lines == null) return 0;
        int sum = 0;
        foreach (var line in lines)
        {
            if (line.item == null) continue;
            sum += Mathf.Max(0, line.item.price) * Mathf.Max(0, line.quantity);
        }
        return sum;
    }
}
