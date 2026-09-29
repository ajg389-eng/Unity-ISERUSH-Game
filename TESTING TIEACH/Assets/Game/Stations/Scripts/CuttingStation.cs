using System.Collections.Generic;
using UnityEngine;

/// <summary>Processes raw burger toppings into slices ready for assembly.</summary>
public class CuttingStation : MonoBehaviour
{
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        return tiles != null ? tiles.GetFirstInteractionPosition() : transform.position + interactionOffset;
    }

    public bool HasIngredients(CustomerOrderConfig config, int amount = 1)
    {
        if (config == null) return false;
        var inventory = KitchenInventory.Instance;
        if (inventory == null) return true;
        amount = Mathf.Max(1, amount);
        return inventory.Has(config.lettuceIngredient, amount)
            && inventory.Has(config.cheeseIngredient, amount)
            && inventory.Has(config.tomatoIngredient, amount);
    }

    public bool TryProcess(CustomerOrderConfig config, List<ItemDefinition> processedIngredients, int amount = 1)
    {
        amount = Mathf.Max(1, amount);
        if (config == null || processedIngredients == null || !HasIngredients(config, amount)) return false;
        var inventory = KitchenInventory.Instance;
        if (inventory != null)
        {
            if (!inventory.TryConsume(config.lettuceIngredient, amount)) return false;
            if (!inventory.TryConsume(config.cheeseIngredient, amount)) return false;
            if (!inventory.TryConsume(config.tomatoIngredient, amount)) return false;
        }
        processedIngredients.Add(config.lettuceIngredient);
        processedIngredients.Add(config.cheeseIngredient);
        processedIngredients.Add(config.tomatoIngredient);
        return true;
    }
}
