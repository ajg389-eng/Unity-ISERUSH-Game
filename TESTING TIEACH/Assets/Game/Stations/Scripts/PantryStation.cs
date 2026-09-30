using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Holds which ingredients can be grabbed here.
/// Actual counts come from KitchenInventory (ordered via Management).
/// </summary>
public class PantryStation : MonoBehaviour
{
    void Start()
    {
        RestaurantDetails.StockPantry(transform);
    }

    [Header("Stored ingredient")]
    [Tooltip("The only ingredient this pantry can dispense. Choose it in Management mode.")]
    public ItemDefinition selectedItem;
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one pantry operation.")]
    [Min(0f)] public float processTimeSeconds = 0.5f;
    public Vector3 interactionOffset = Vector3.zero;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool HasItem(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (!CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true; // legacy infinite if no inventory system
        return inv.Has(item, amount);
    }

    public bool TakeItem(ItemDefinition item)
    {
        if (item == null || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(item, 1);
    }

    public bool TakeItems(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(item, amount);
    }

    public bool HasItemSelected => selectedItem != null;

    public void SetStoredItem(ItemDefinition item)
    {
        selectedItem = item;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
    }

    public bool CanDispense(ItemDefinition item) => item != null && item == selectedItem;
}
