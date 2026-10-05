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
    [Tooltip("When enabled, Input 1 is stocked directly from a Pantry instead of arriving from an earlier production step.")]
    public bool processedInputFromPantry;
    [Tooltip("When enabled, Input 1 is stocked directly from a Freezer instead of arriving from an earlier production step.")]
    public bool processedInputFromFreezer;

    [Header("Input 2: pantry material")]
    public ItemDefinition pantryInput;
    [Min(1)] public int pantryInputAmount = 1;
    [Tooltip("Optional raw material that must be processed at a Cutting Station before becoming the pantry input.")]
    public ItemDefinition rawPantryInput;
    [Tooltip("Optional explicit station path used to prepare Input 2 for this recipe.")]
    public StationType[] supplyPipeline;
    [Tooltip("Item carried after each station in Supply Pipeline.")]
    public ItemDefinition[] supplyStageOutputs;

    [Header("MK2 third input")]
    [Tooltip("Required only by Assembly Station MK2 recipes. MK1 cannot process recipes with this input.")]
    public ItemDefinition thirdInput;
    [Min(1)] public int thirdInputAmount = 1;
    public ItemDefinition rawThirdInput;
    public StationType[] thirdSupplyPipeline;
    public ItemDefinition[] thirdSupplyStageOutputs;

    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName)
        ? recipeName
        : (output != null && !string.IsNullOrWhiteSpace(output.itemName) ? output.itemName : "Recipe");

    public bool Produces(ItemDefinition item) => item != null && output == item;
    public bool RequiresMk2 => thirdInput != null;

}

[System.Serializable]
public class StationProcessingRecipeDefinition
{
    public string recipeName;
    public ItemDefinition input;
    public ItemDefinition output;
    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName) ? recipeName
        : (output != null ? output.itemName : "Processing Recipe");
}

/// <summary>
/// Config for generating customer orders: any combination of burger, fries, and shake.
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
    public ItemDefinition clBurgerItem;
    public ItemDefinition cltBurgerItem;
    public ItemDefinition cheeseBaconBurgerItem;
    public ItemDefinition cheeseBaconLettuceBurgerItem;
    public ItemDefinition cheeseBaconLettuceOnionBurgerItem;
    public ItemDefinition cheeseBaconLettuceOnionTomatoBurgerItem;
    public ItemDefinition cheeseBaconLettuceOnionTomatoEggBurgerItem;
    [Tooltip("Fries side")]
    public ItemDefinition friesItem;
    public ItemDefinition cheeseFriesItem;
    public ItemDefinition cheeseBaconFriesItem;
    [Tooltip("Raw potatoes consumed at the pantry before fries are cooked")]
    public ItemDefinition friesIngredient;
    [Tooltip("Potato slices produced by a Cutting Station and consumed by the Fryer")]
    public ItemDefinition slicedPotatoIngredient;
    [Tooltip("Cooked potato slices produced by the Fryer and consumed by the Fries assembly recipe")]
    public ItemDefinition cookedPotatoIngredient;
    [Tooltip("Empty fry container supplied by a Pantry for final Fries assembly")]
    public ItemDefinition fryContainerIngredient;
    [Header("Production stage items")]
    [Tooltip("Raw patty produced by the Freezer and carried to the Grill.")]
    public ItemDefinition rawPattyIngredient;
    [Tooltip("Cooked patty produced by the Grill and carried to Assembly.")]
    public ItemDefinition cookedPattyIngredient;
    [Header("Assembly recipes")]
    [Tooltip("Recipes players can choose on an Assembly Station.")]
    public List<AssemblyRecipeDefinition> assemblyRecipes = new List<AssemblyRecipeDefinition>();
    [Header("Burger toppings")]
    public ItemDefinition lettuceIngredient;
    public ItemDefinition cheeseIngredient;
    [Tooltip("Processed cheese produced by the Cutting Station and consumed by Assembly recipes.")]
    public ItemDefinition slicedCheeseIngredient;
    public ItemDefinition slicedLettuceIngredient;
    public ItemDefinition tomatoIngredient;
    public ItemDefinition slicedTomatoIngredient;
    [Header("Bacon, onion, and egg")]
    public ItemDefinition rawBaconIngredient;
    public ItemDefinition cookedBaconIngredient;
    public ItemDefinition onionIngredient;
    public ItemDefinition onionRingIngredient;
    public ItemDefinition friedOnionRingIngredient;
    public ItemDefinition eggIngredient;
    public ItemDefinition cookedEggIngredient;
    public List<StationProcessingRecipeDefinition> grillRecipes = new List<StationProcessingRecipeDefinition>();
    public List<StationProcessingRecipeDefinition> fryerRecipes = new List<StationProcessingRecipeDefinition>();
    [Header("Cutting recipes")]
    public List<CuttingRecipeDefinition> cuttingRecipes = new List<CuttingRecipeDefinition>();
    [Tooltip("Shake")]
    public ItemDefinition drinkItem;
    public ItemDefinition whippedCreamShakeItem;
    public ItemDefinition whippedCreamSprinkleShakeItem;
    public ItemDefinition cheeseSauceIngredient;
    public ItemDefinition whippedCreamIngredient;
    public ItemDefinition sprinklesIngredient;

    [Header("Order chances (each item rolled independently)")]
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a burger")]
    public float burgerChance = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants fries")]
    public float friesChance = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a shake")]
    public float drinkChance = 0.7f;

    // Runtime menu choices. These deliberately are not serialized back into the shared asset.
    [System.NonSerialized] bool burgerEnabled = true;
    [System.NonSerialized] bool cheeseburgerEnabled = true;
    [System.NonSerialized] bool clBurgerEnabled = true;
    [System.NonSerialized] bool cltBurgerEnabled = true;
    [System.NonSerialized] bool friesEnabled = true;
    [System.NonSerialized] bool drinkEnabled = true;
    [System.NonSerialized] readonly HashSet<ItemDefinition> disabledExpandedBurgers = new HashSet<ItemDefinition>();

    public bool IsBurger(ItemDefinition item) => item != null && (item == burgerBase
        || item == cheeseburgerItem || item == cheeseBaconBurgerItem
        || item == cheeseBaconLettuceBurgerItem || item == cheeseBaconLettuceOnionBurgerItem
        || item == cheeseBaconLettuceOnionTomatoBurgerItem
        || item == cheeseBaconLettuceOnionTomatoEggBurgerItem);
    public bool IsCheeseburger(ItemDefinition item) => item != null && item == cheeseburgerItem;
    public bool IsFries(ItemDefinition item) => item != null && (item == friesItem || item == cheeseFriesItem || item == cheeseBaconFriesItem);
    public bool IsDrink(ItemDefinition item) => item != null && (item == drinkItem || item == whippedCreamShakeItem || item == whippedCreamSprinkleShakeItem);

    public bool IsItemEnabled(ItemDefinition item)
    {
        if (IsCheeseburger(item)) return cheeseburgerEnabled;
        if (item == clBurgerItem) return clBurgerEnabled;
        if (item == cltBurgerItem) return cltBurgerEnabled;
        if (item == burgerBase) return burgerEnabled;
        if (IsBurger(item)) return !disabledExpandedBurgers.Contains(item);
        if (IsFries(item)) return friesEnabled;
        if (IsDrink(item)) return drinkEnabled;
        return false;
    }

    public void SetItemEnabled(ItemDefinition item, bool enabled)
    {
        if (IsCheeseburger(item)) cheeseburgerEnabled = enabled;
        else if (item == clBurgerItem) clBurgerEnabled = enabled;
        else if (item == cltBurgerItem) cltBurgerEnabled = enabled;
        else if (item == burgerBase) burgerEnabled = enabled;
        else if (IsBurger(item))
        {
            if (enabled) disabledExpandedBurgers.Remove(item);
            else disabledExpandedBurgers.Add(item);
        }
        else if (IsFries(item)) friesEnabled = enabled;
        else if (IsDrink(item)) drinkEnabled = enabled;
    }

    public bool HasEnabledItems =>
        (burgerBase != null && burgerEnabled)
        || (cheeseburgerItem != null && cheeseburgerEnabled)
        || (clBurgerItem != null && clBurgerEnabled)
        || (cltBurgerItem != null && cltBurgerEnabled)
        || (cheeseBaconBurgerItem != null && !disabledExpandedBurgers.Contains(cheeseBaconBurgerItem))
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
        if (label.IndexOf("shake", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("drink", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("soda", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("cola", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Drink;
        return ProductKind.None;
    }

    /// <summary>
    /// Production pipeline for a single menu item (not including heat lamp delivery).
    /// Burger main line: Freezer → Grill → Assembly. Pantry supplies buns in parallel.
    /// Fries: Potato Pantry → Cutting → Fryer → Assembly. A Fry Container Pantry supplies Assembly.
    /// Shake: two Pantry ingredients assembled at a Shake Station.
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
                int friesAssemblyCount = Mathf.Max(1, GetAssemblyChain(item).Count);
                var friesPipeline = new StationType[3 + friesAssemblyCount];
                friesPipeline[0] = StationType.Pantry; friesPipeline[1] = StationType.Cutting; friesPipeline[2] = StationType.Fryer;
                for (int i = 0; i < friesAssemblyCount; i++) friesPipeline[3 + i] = StationType.Assembly;
                return friesPipeline;
            case ProductKind.Drink:
                int shakeAssemblyCount = Mathf.Max(1, GetAssemblyChain(item).Count);
                var shakePipeline = new StationType[shakeAssemblyCount];
                for (int i = 0; i < shakeAssemblyCount; i++) shakePipeline[i] = StationType.Assembly;
                return shakePipeline;
            default:
                return System.Array.Empty<StationType>();
        }
    }

    /// <summary>Outputs the grill can be configured to produce.</summary>
    public IEnumerable<ItemDefinition> GetGrillProducts()
    {
        var yielded = new HashSet<ItemDefinition>();
        if (cookedPattyIngredient != null && yielded.Add(cookedPattyIngredient)) yield return cookedPattyIngredient;
        if (grillRecipes != null)
            foreach (StationProcessingRecipeDefinition recipe in grillRecipes)
                if (recipe != null && recipe.output != null && yielded.Add(recipe.output)) yield return recipe.output;
    }

    public StationProcessingRecipeDefinition GetGrillRecipe(ItemDefinition output) =>
        grillRecipes != null ? grillRecipes.Find(r => r != null && r.output == output) : null;
    public StationProcessingRecipeDefinition GetFryerRecipe(ItemDefinition output) =>
        fryerRecipes != null ? fryerRecipes.Find(r => r != null && r.output == output) : null;
    public IEnumerable<ItemDefinition> GetFryerProducts()
    {
        var yielded = new HashSet<ItemDefinition>();
        if (cookedPotatoIngredient != null && yielded.Add(cookedPotatoIngredient)) yield return cookedPotatoIngredient;
        if (fryerRecipes != null)
            foreach (StationProcessingRecipeDefinition recipe in fryerRecipes)
                if (recipe != null && recipe.output != null && yielded.Add(recipe.output)) yield return recipe.output;
    }

    /// <summary>Raw ingredients a Freezer may be configured to dispense.</summary>
    public IEnumerable<ItemDefinition> GetFreezerIngredients()
    {
        var yielded = new HashSet<ItemDefinition>();
        if (rawPattyIngredient != null && yielded.Add(rawPattyIngredient)) yield return rawPattyIngredient;
        if (lettuceIngredient != null && yielded.Add(lettuceIngredient)) yield return lettuceIngredient;
        if (tomatoIngredient != null && yielded.Add(tomatoIngredient)) yield return tomatoIngredient;
        if (rawBaconIngredient != null && yielded.Add(rawBaconIngredient)) yield return rawBaconIngredient;
        if (onionIngredient != null && yielded.Add(onionIngredient)) yield return onionIngredient;
        if (eggIngredient != null && yielded.Add(eggIngredient)) yield return eggIngredient;
        if (assemblyRecipes != null)
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
                if (recipe != null && recipe.processedInputFromFreezer
                    && recipe.processedInput != null && yielded.Add(recipe.processedInput))
                    yield return recipe.processedInput;
    }

    /// <summary>Raw ingredients a Pantry may be configured to dispense.</summary>
    public IEnumerable<ItemDefinition> GetPantryIngredients()
    {
        var yielded = new HashSet<ItemDefinition>();
        if (assemblyRecipes != null)
        {
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                ItemDefinition source = GetAssemblySupplySource(recipe);
                if (source != null && !IsFreezerIngredient(source) && yielded.Add(source)) yield return source;
                if (recipe != null && recipe.processedInputFromPantry
                    && recipe.processedInput != null && yielded.Add(recipe.processedInput))
                    yield return recipe.processedInput;
            }
        }
        if (friesIngredient != null && yielded.Add(friesIngredient)) yield return friesIngredient;
        if (cheeseIngredient != null && yielded.Add(cheeseIngredient)) yield return cheeseIngredient;
        if (sprinklesIngredient != null && yielded.Add(sprinklesIngredient)) yield return sprinklesIngredient;
    }

    public bool IsFreezerIngredient(ItemDefinition item)
    {
        if (item == null) return false;
        if (item == rawPattyIngredient || item == lettuceIngredient || item == tomatoIngredient
            || item == rawBaconIngredient || item == onionIngredient || item == eggIngredient || item == whippedCreamIngredient) return true;
        if (assemblyRecipes == null) return false;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.processedInputFromFreezer && recipe.processedInput == item)
                return true;
        return false;
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

    public IEnumerable<CuttingRecipeDefinition> GetCuttingRecipes()
    {
        if (cuttingRecipes == null) yield break;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.input != null && recipe.output != null)
                yield return recipe;
    }

    public CuttingRecipeDefinition GetCuttingRecipe(ItemDefinition output)
    {
        if (output == null || cuttingRecipes == null) return null;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.output == output)
                return recipe;
        return null;
    }

    public CuttingRecipeDefinition GetCuttingRecipeForInput(ItemDefinition input)
    {
        if (input == null || cuttingRecipes == null) return null;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.input == input)
                return recipe;
        return null;
    }

    public AssemblyRecipeDefinition GetAssemblyRecipe(ItemDefinition output)
    {
        if (output == null || assemblyRecipes == null) return null;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.Produces(output))
                return recipe;
        return null;
    }

    public ItemDefinition GetAssemblySupplySource(AssemblyRecipeDefinition recipe)
    {
        if (recipe == null) return null;
        return recipe.rawPantryInput != null ? recipe.rawPantryInput : recipe.pantryInput;
    }

    public bool AssemblySupplyRequiresCutting(AssemblyRecipeDefinition recipe) =>
        recipe != null && recipe.rawPantryInput != null && recipe.pantryInput != null
        && recipe.rawPantryInput != recipe.pantryInput;

    public ItemDefinition GetAssemblyThirdSupplySource(AssemblyRecipeDefinition recipe)
    {
        if (recipe == null) return null;
        return recipe.rawThirdInput != null ? recipe.rawThirdInput : recipe.thirdInput;
    }

    public bool AssemblyThirdSupplyRequiresProcessing(AssemblyRecipeDefinition recipe) =>
        recipe != null && recipe.rawThirdInput != null && recipe.thirdInput != null
        && recipe.rawThirdInput != recipe.thirdInput;

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
        var available = new List<ItemDefinition>();
        foreach (ItemDefinition item in GetMenuItems())
            if (IsBurger(item) && IsItemEnabled(item)) available.Add(item);
        return available.Count > 0 ? available[Random.Range(0, available.Count)] : null;
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
        foreach (ItemDefinition item in GetMenuItems())
            if (IsItemEnabled(item)) options.Add(item);
        if (options.Count == 0) return new CustomerOrder();

        var pick = options[Random.Range(0, options.Count)];
        return CustomerOrder.FromItem(pick, 1);
    }

    public IEnumerable<ItemDefinition> GetMenuItems()
    {
        if (burgerBase != null) yield return burgerBase;
        if (cheeseburgerItem != null) yield return cheeseburgerItem;
        if (cheeseBaconBurgerItem != null) yield return cheeseBaconBurgerItem;
        if (cheeseBaconLettuceBurgerItem != null) yield return cheeseBaconLettuceBurgerItem;
        if (cheeseBaconLettuceOnionBurgerItem != null) yield return cheeseBaconLettuceOnionBurgerItem;
        if (cheeseBaconLettuceOnionTomatoBurgerItem != null) yield return cheeseBaconLettuceOnionTomatoBurgerItem;
        if (cheeseBaconLettuceOnionTomatoEggBurgerItem != null) yield return cheeseBaconLettuceOnionTomatoEggBurgerItem;
        if (friesItem != null) yield return friesItem;
        if (cheeseFriesItem != null) yield return cheeseFriesItem;
        if (cheeseBaconFriesItem != null) yield return cheeseBaconFriesItem;
        if (drinkItem != null) yield return drinkItem;
        if (whippedCreamShakeItem != null) yield return whippedCreamShakeItem;
        if (whippedCreamSprinkleShakeItem != null) yield return whippedCreamSprinkleShakeItem;
    }

    public IEnumerable<ItemDefinition> GetEnabledMenuItems()
    {
        foreach (ItemDefinition item in GetMenuItems())
            if (IsItemEnabled(item))
                yield return item;
    }

    public IEnumerable<ItemDefinition> GetIngredientItems()
    {
        var yielded = new HashSet<ItemDefinition>();
        foreach (ItemDefinition freezerItem in GetFreezerIngredients())
            if (freezerItem != null && yielded.Add(freezerItem)) yield return freezerItem;
        foreach (ItemDefinition pantryItem in GetPantryIngredients())
            if (pantryItem != null && yielded.Add(pantryItem)) yield return pantryItem;
        if (friesIngredient == null && friesItem != null && yielded.Add(friesItem)) yield return friesItem;
    }
}

[System.Serializable]
public class CuttingRecipeDefinition
{
    public string recipeName = "Sliced Ingredient";
    public ItemDefinition input;
    public ItemDefinition output;

    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName)
        ? recipeName
        : (output != null && !string.IsNullOrWhiteSpace(output.itemName)
            ? output.itemName : "Cutting Recipe");
}
