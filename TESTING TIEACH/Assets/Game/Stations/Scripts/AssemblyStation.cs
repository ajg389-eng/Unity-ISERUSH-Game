using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Burger assembly only. Choose the burger product in Manage mode; fries/drinks do not use this station.
/// </summary>
public class AssemblyStation : MonoBehaviour
{
    [Header("Product")]
    [Tooltip("Burger product this station assembles. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;
    [System.NonSerialized] AssemblyRecipeDefinition selectedRecipe;

    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one assembly operation.")]
    [Min(0f)] public float processTimeSeconds = 1.2f;
    public Vector3 interactionOffset = Vector3.zero;
    [SerializeField, Min(0)] int bufferedPantryInputs;

    public int BufferedPantryInputCount => bufferedPantryInputs;

    public bool HasProductSelected => GetSelectedRecipe() != null;

    public AssemblyRecipeDefinition GetSelectedRecipe()
    {
        if (selectedRecipe != null)
        {
            if (selectedProduct != selectedRecipe.output)
                selectedProduct = selectedRecipe.output;
            return selectedRecipe;
        }

        var manager = ProductionManager.Instance;
        var config = manager != null ? manager.orderConfig : null;
        selectedRecipe = config != null ? config.GetAssemblyRecipe(selectedProduct) : null;
        if (selectedRecipe != null)
            selectedProduct = selectedRecipe.output;
        return selectedRecipe;
    }

    public void SetRecipe(AssemblyRecipeDefinition recipe)
    {
        selectedRecipe = recipe;
        selectedProduct = recipe != null ? recipe.output : null;
    }

    public bool CanProcess(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return product != null && recipe != null && recipe.Produces(product);
    }

    public ItemDefinition GetPantryInput(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && recipe.Produces(product) ? recipe.pantryInput : null;
    }

    public int ReceivePantryInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || item != recipe.pantryInput || amount <= 0) return 0;
        int capacity = 8 * Mathf.Max(1, recipe.pantryInputAmount);
        int accepted = Mathf.Min(amount, Mathf.Max(0, capacity - bufferedPantryInputs));
        bufferedPantryInputs += accepted;
        return accepted;
    }

    public bool HasRequiredInputs(ItemDefinition product, IReadOnlyList<ItemDefinition> pantryMaterials, int units)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || !recipe.Produces(product) || units <= 0 || recipe.pantryInput == null)
            return false;

        int required = units * Mathf.Max(1, recipe.pantryInputAmount);
        int present = 0;
        if (pantryMaterials != null)
        {
            for (int i = 0; i < pantryMaterials.Count; i++)
                if (pantryMaterials[i] == recipe.pantryInput)
                    present++;
        }
        return present + bufferedPantryInputs >= required;
    }

    public bool TryAssemble(ItemDefinition product, List<ItemDefinition> pantryMaterials, int units)
    {
        if (!HasRequiredInputs(product, pantryMaterials, units)) return false;
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        int consume = units * Mathf.Max(1, recipe.pantryInputAmount);
        for (int i = pantryMaterials.Count - 1; i >= 0 && consume > 0; i--)
        {
            if (pantryMaterials[i] != recipe.pantryInput) continue;
            pantryMaterials.RemoveAt(i);
            consume--;
        }
        if (consume > 0)
        {
            int fromBuffer = Mathf.Min(consume, bufferedPantryInputs);
            bufferedPantryInputs -= fromBuffer;
            consume -= fromBuffer;
        }
        return consume == 0;
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }
}
