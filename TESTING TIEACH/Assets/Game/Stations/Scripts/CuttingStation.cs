using System.Collections.Generic;
using UnityEngine;

/// <summary>Processes raw burger toppings into slices ready for assembly.</summary>
public class CuttingStation : MonoBehaviour
{
    [Header("Recipe")]
    [Tooltip("Output selected for this station. The recipe defines its matching raw input.")]
    public ItemDefinition selectedProduct;
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    public bool HasRecipeSelected => GetSelectedRecipe() != null;

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
    }

    public CuttingRecipeDefinition GetSelectedRecipe()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config != null ? config.GetCuttingRecipe(selectedProduct) : null;
    }

    public void SetRecipe(CuttingRecipeDefinition recipe)
    {
        selectedProduct = recipe != null ? recipe.output : null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
    }

    public bool CanProcess(ItemDefinition input, ItemDefinition output = null)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && input == recipe.input
            && (output == null || output == recipe.output);
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        return tiles != null ? tiles.GetFirstInteractionPosition() : transform.position + interactionOffset;
    }

    public bool HasIngredients(CustomerOrderConfig config, int amount = 1)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (config == null || recipe == null) return false;
        var inventory = KitchenInventory.Instance;
        if (inventory == null) return true;
        amount = Mathf.Max(1, amount);
        return inventory.Has(recipe.input, amount);
    }

    public bool TryProcess(CustomerOrderConfig config, List<ItemDefinition> processedIngredients, int amount = 1)
    {
        amount = Mathf.Max(1, amount);
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (config == null || recipe == null || processedIngredients == null
            || !HasIngredients(config, amount)) return false;
        var inventory = KitchenInventory.Instance;
        if (inventory != null && !inventory.TryConsume(recipe.input, amount)) return false;
        for (int i = 0; i < amount; i++) processedIngredients.Add(recipe.output);
        return true;
    }

    public bool HasCarriedSupply(CustomerOrderConfig config, List<ItemDefinition> carried, int amount = 1)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (config == null || carried == null || recipe == null) return false;
        amount = Mathf.Max(1, amount);
        int found = 0;
        foreach (ItemDefinition item in carried)
            if (item == recipe.input && ++found >= amount)
                return true;
        return false;
    }

    public bool TryProcessCarried(CustomerOrderConfig config, List<ItemDefinition> carried, int amount = 1)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return false;
        amount = Mathf.Max(1, amount);
        if (!HasCarriedSupply(config, carried, amount)) return false;
        int remaining = amount;
        for (int i = carried.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (carried[i] != recipe.input) continue;
            carried.RemoveAt(i);
            remaining--;
        }
        for (int i = 0; i < amount; i++)
            carried.Add(recipe.output);
        return true;
    }

    public bool TrySliceCheese(CustomerOrderConfig config, List<ItemDefinition> carried, int amount = 1) =>
        TryProcessCarried(config, carried, amount);
}
