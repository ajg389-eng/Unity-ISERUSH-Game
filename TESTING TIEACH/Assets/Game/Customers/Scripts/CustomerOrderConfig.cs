using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AssemblyRecipeDefinition
{
    public string recipeName = "Burger";
    public ItemDefinition output;

    [Header("Input 1: processed work")]
    [Tooltip("Optional output from an earlier Assembly recipe. Leave empty when the input comes directly from the grill.")]
    public ItemDefinition processedInput;
    public string processedInputName = "Cooked patty";
    [Min(1)] public int processedInputAmount = 1;

    [Header("Input 2: pantry material")]
    public ItemDefinition pantryInput;
    [Min(1)] public int pantryInputAmount = 1;

    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName)
        ? recipeName
        : (output != null && !string.IsNullOrWhiteSpace(output.itemName) ? output.itemName : "Recipe");

    public bool Produces(ItemDefinition item) => item != null && output == item;
}

/// <summary>
/// Config for generating customer orders: any combination of burger, fries, and drink.
/// Create via Assets > Create > FactoryGame > Customer Order Config.
/// </summary>
[CreateAssetMenu(menuName = "FactoryGame/Customer Order Config", fileName = "CustomerOrderConfig")]
public class CustomerOrderConfig : ScriptableObject
{
    [Header("Menu items")]
    [Tooltip("Burger / patty line item")]
    public ItemDefinition burgerBase;
    [Tooltip("Optional premium burger assembled through additional recipe stages.")]
    public ItemDefinition cheeseburgerItem;
    [Tooltip("Fries side")]
    public ItemDefinition friesItem;
    [Tooltip("Raw potatoes consumed at the pantry before fries are cooked")]
    public ItemDefinition friesIngredient;
    [Header("Assembly recipes")]
    [Tooltip("Recipes players can choose on an Assembly Station.")]
    public List<AssemblyRecipeDefinition> assemblyRecipes = new List<AssemblyRecipeDefinition>();
    [Header("Burger toppings")]
    public ItemDefinition lettuceIngredient;
    public ItemDefinition cheeseIngredient;
    public ItemDefinition tomatoIngredient;
    [Tooltip("Drink")]
    public ItemDefinition drinkItem;

    [Header("Order chances (each item rolled independently)")]
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a burger")]
    public float burgerChance = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants fries")]
    public float friesChance = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a drink")]
    public float drinkChance = 0.7f;

    // Runtime menu choices. These deliberately are not serialized back into the shared asset.
    [System.NonSerialized] bool burgerEnabled = true;
    [System.NonSerialized] bool cheeseburgerEnabled = true;
    [System.NonSerialized] bool friesEnabled = true;
    [System.NonSerialized] bool drinkEnabled = true;

    public bool IsBurger(ItemDefinition item) => item != null && (item == burgerBase || item == cheeseburgerItem);
    public bool IsCheeseburger(ItemDefinition item) => item != null && item == cheeseburgerItem;
    public bool IsFries(ItemDefinition item) => item != null && item == friesItem;
    public bool IsDrink(ItemDefinition item) => item != null && item == drinkItem;

    public bool IsItemEnabled(ItemDefinition item)
    {
        if (IsCheeseburger(item)) return cheeseburgerEnabled;
        if (item == burgerBase) return burgerEnabled;
        if (IsFries(item)) return friesEnabled;
        if (IsDrink(item)) return drinkEnabled;
        return false;
    }

    public void SetItemEnabled(ItemDefinition item, bool enabled)
    {
        if (IsCheeseburger(item)) cheeseburgerEnabled = enabled;
        else if (item == burgerBase) burgerEnabled = enabled;
        else if (IsFries(item)) friesEnabled = enabled;
        else if (IsDrink(item)) drinkEnabled = enabled;
    }

    public bool HasEnabledItems =>
        (burgerBase != null && burgerEnabled)
        || (cheeseburgerItem != null && cheeseburgerEnabled)
        || (friesItem != null && friesEnabled)
        || (drinkItem != null && drinkEnabled);

    public enum ProductKind { None, Burger, Fries, Drink }

    public ProductKind GetProductKind(ItemDefinition item)
    {
        if (IsBurger(item)) return ProductKind.Burger;
        if (IsFries(item)) return ProductKind.Fries;
        if (IsDrink(item)) return ProductKind.Drink;
        return InferProductKind(item);
    }

    /// <summary>
    /// True when both definitions are the same menu product, even if the kitchen
    /// used a different ItemDefinition asset than the customer's order line.
    /// </summary>
    public bool SameMenuProduct(ItemDefinition a, ItemDefinition b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (!string.IsNullOrEmpty(a.itemName) && a.itemName == b.itemName) return true;
        ProductKind kindA = GetProductKind(a);
        ProductKind kindB = GetProductKind(b);
        // Burger variants are distinct sellable products. A burger must never satisfy
        // a cheeseburger order merely because both use the burger production family.
        if (kindA == ProductKind.Burger || kindB == ProductKind.Burger) return false;
        return kindA != ProductKind.None && kindA == kindB;
    }

    static ProductKind InferProductKind(ItemDefinition item)
    {
        if (item == null) return ProductKind.None;
        string label = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
        if (string.IsNullOrEmpty(label)) return ProductKind.None;
        if (label.IndexOf("burger", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Burger;
        if (label.IndexOf("fries", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("fry", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Fries;
        if (label.IndexOf("drink", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("soda", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("cola", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Drink;
        return ProductKind.None;
    }

    /// <summary>
    /// Production pipeline for a single menu item (not including heat lamp delivery).
    /// Burger main line: Freezer → Grill → Assembly. Pantry supplies buns in parallel.
    /// Fries: Pantry → Fryer
    /// Drink: Drink Fountain
    /// </summary>
    public StationType[] GetPipeline(ItemDefinition item)
    {
        switch (GetProductKind(item))
        {
            case ProductKind.Burger:
                var stages = GetAssemblyChain(item);
                int assemblyCount = Mathf.Max(1, stages.Count);
                var pipeline = new StationType[2 + assemblyCount];
                pipeline[0] = StationType.Freezer;
                pipeline[1] = StationType.Grill;
                for (int i = 0; i < assemblyCount; i++)
                    pipeline[2 + i] = StationType.Assembly;
                return pipeline;
            case ProductKind.Fries:
                return new[] { StationType.Pantry, StationType.Fryer };
            case ProductKind.Drink:
                return new[] { StationType.Drink };
            default:
                return System.Array.Empty<StationType>();
        }
    }

    /// <summary>Products the grill can be set to cook.</summary>
    public IEnumerable<ItemDefinition> GetGrillProducts()
    {
        if (burgerBase != null) yield return burgerBase;
    }

    /// <summary>Products the assembly station can be set to make (burgers only).</summary>
    public IEnumerable<ItemDefinition> GetAssemblyProducts()
    {
        bool yielded = false;
        if (assemblyRecipes != null)
        {
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                if (recipe == null || recipe.output == null) continue;
                yielded = true;
                yield return recipe.output;
            }
        }
        if (!yielded && burgerBase != null) yield return burgerBase;
    }

    public IEnumerable<AssemblyRecipeDefinition> GetAssemblyRecipes()
    {
        if (assemblyRecipes == null) yield break;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.output != null)
                yield return recipe;
    }

    public AssemblyRecipeDefinition GetAssemblyRecipe(ItemDefinition output)
    {
        if (output == null || assemblyRecipes == null) return null;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.Produces(output))
                return recipe;
        return null;
    }

    /// <summary>Assembly outputs in production order, including intermediate recipes.</summary>
    public List<ItemDefinition> GetAssemblyChain(ItemDefinition finalProduct)
    {
        var chain = new List<ItemDefinition>();
        var visited = new HashSet<ItemDefinition>();
        AppendAssemblyChain(finalProduct, chain, visited);
        return chain;
    }

    void AppendAssemblyChain(ItemDefinition output, List<ItemDefinition> chain, HashSet<ItemDefinition> visited)
    {
        if (output == null || !visited.Add(output)) return;
        AssemblyRecipeDefinition recipe = GetAssemblyRecipe(output);
        if (recipe == null) return;
        if (recipe.processedInput != null && GetAssemblyRecipe(recipe.processedInput) != null)
            AppendAssemblyChain(recipe.processedInput, chain, visited);
        chain.Add(output);
    }

    ItemDefinition PickEnabledBurger()
    {
        bool regular = burgerBase != null && burgerEnabled;
        bool cheese = cheeseburgerItem != null && cheeseburgerEnabled;
        if (regular && cheese) return Random.value < 0.5f ? burgerBase : cheeseburgerItem;
        if (regular) return burgerBase;
        return cheese ? cheeseburgerItem : null;
    }

    public CustomerOrder GenerateRandomOrder()
    {
        var order = new CustomerOrder();

        // Burgers are the restaurant's central product: when enabled, every
        // customer order includes one. Fries and drinks remain optional sides.
        ItemDefinition orderedBurger = PickEnabledBurger();
        bool wantBurger = orderedBurger != null;
        bool wantFries = friesItem != null && friesEnabled && Random.value < friesChance;
        bool wantDrink = drinkItem != null && drinkEnabled && Random.value < drinkChance;

        if (!wantBurger && !wantFries && !wantDrink)
        {
            // Every order needs at least one item. Pick uniformly from the
            // currently available menu so this fallback does not favor burgers.
            var available = new List<ProductKind>();
            if (PickEnabledBurger() != null) available.Add(ProductKind.Burger);
            if (friesItem != null && friesEnabled) available.Add(ProductKind.Fries);
            if (drinkItem != null && drinkEnabled) available.Add(ProductKind.Drink);

            if (available.Count > 0)
            {
                switch (available[Random.Range(0, available.Count)])
                {
                    case ProductKind.Burger: wantBurger = true; break;
                    case ProductKind.Fries: wantFries = true; break;
                    case ProductKind.Drink: wantDrink = true; break;
                }
            }
        }

        if (wantBurger)
            order.lines.Add(new CustomerOrder.OrderLine(orderedBurger ?? PickEnabledBurger(), 1));
        if (wantFries)
            order.lines.Add(new CustomerOrder.OrderLine(friesItem, 1));
        if (wantDrink)
            order.lines.Add(new CustomerOrder.OrderLine(drinkItem, 1));

        return order;
    }

    /// <summary>One random heat-lamp item (burger or fries — drinks are cashier-served).</summary>
    public CustomerOrder GenerateRandomSingleItemOrder()
    {
        var options = new List<ItemDefinition>();
        if (burgerBase != null && burgerEnabled) options.Add(burgerBase);
        if (cheeseburgerItem != null && cheeseburgerEnabled) options.Add(cheeseburgerItem);
        if (friesItem != null && friesEnabled) options.Add(friesItem);
        if (drinkItem != null && drinkEnabled) options.Add(drinkItem);
        if (options.Count == 0) return new CustomerOrder();

        var pick = options[Random.Range(0, options.Count)];
        return CustomerOrder.FromItem(pick, 1);
    }

    public IEnumerable<ItemDefinition> GetMenuItems()
    {
        if (burgerBase != null) yield return burgerBase;
        if (cheeseburgerItem != null) yield return cheeseburgerItem;
        if (friesItem != null) yield return friesItem;
        if (drinkItem != null) yield return drinkItem;
    }

    public IEnumerable<ItemDefinition> GetEnabledMenuItems()
    {
        foreach (ItemDefinition item in GetMenuItems())
            if (IsItemEnabled(item))
                yield return item;
    }

    public IEnumerable<ItemDefinition> GetIngredientItems()
    {
        if (burgerBase != null) yield return burgerBase;
        if (assemblyRecipes != null)
        {
            var yieldedRecipeInputs = new HashSet<ItemDefinition>();
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                if (recipe != null && recipe.pantryInput != null && yieldedRecipeInputs.Add(recipe.pantryInput))
                    yield return recipe.pantryInput;
            }
        }
        if (friesIngredient != null) yield return friesIngredient;
        else if (friesItem != null) yield return friesItem;
        if (drinkItem != null) yield return drinkItem;
    }
}
