using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeable station. Holds which ingredients can be grabbed here.
/// Actual counts come from KitchenInventory (ordered via Management).
/// </summary>
public class PantryStation : MonoBehaviour
{
    [Tooltip("Ingredients this pantry can dispense (must also have stock in KitchenInventory)")]
    public List<ItemDefinition> stockedItems = new List<ItemDefinition>();
    [Tooltip("Time in seconds for the employee to grab one ingredient.")]
    public float interactionTimeSeconds = 0.5f;
    public Vector3 interactionOffset = Vector3.zero;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool HasItem(ItemDefinition item)
    {
        if (item == null) return false;
        if (!CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true; // legacy infinite if no inventory system
        return inv.Has(item);
    }

    public bool TakeItem(ItemDefinition item)
    {
        if (item == null || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(item, 1);
    }

    /// <summary>
    /// Empty stockedItems = dispense any kitchen stock item (fries, drink, etc.).
    /// Otherwise only listed items.
    /// </summary>
    bool CanDispense(ItemDefinition item)
    {
        if (stockedItems == null || stockedItems.Count == 0)
            return true;
        return stockedItems.Contains(item);
    }
}
