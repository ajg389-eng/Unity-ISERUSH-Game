using UnityEngine;

/// <summary>
/// Placeable drink dispenser. Cashiers pour drinks here and bring them straight to the customer (not the heat lamp).
/// </summary>
public class DrinkStation : MonoBehaviour
{
    [Tooltip("Time in seconds to pour / dispense one drink.")]
    public float interactionTimeSeconds = 1.5f;
    public Vector3 interactionOffset = Vector3.zero;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool HasStock(ItemDefinition drinkItem)
    {
        if (drinkItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(drinkItem);
    }

    /// <summary>Consume one drink from kitchen stock.</summary>
    public bool TryDispense(ItemDefinition drinkItem)
    {
        if (drinkItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(drinkItem, 1);
    }
}
