#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class ExpandedFoodMenuSetup
{
    const string Data = "Assets/Game/Customers/Data/";

    [MenuItem("ISE/Setup/Install Expanded Fries and Shakes")]
    public static void Install()
    {
        var config = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(Data + "CustomerOrderConfig.asset");
        if (config == null) { Debug.LogError("CustomerOrderConfig not found."); return; }
        RemoveRetiredBranches(config);
        var cheeseFries = Item("Cheese Fries", "C Fries.prefab", 11, 0);
        var cheeseBaconFries = Item("Cheese Bacon Fries", "CB Fries.prefab", 14, 0);
        var cream = Item("Whipped Cream", "Whipped Cream Shake.prefab", 2, 8);
        var sprinkles = Item("Sprinkles", "Sprinkles.prefab", 1, 6);
        var creamShake = Item("Whipped Cream Shake", "Whipped Cream Shake.prefab", 11, 0);
        var sprinkleShake = Item("Whipped Cream Sprinkle Shake", "Whiped Cream Sprinkles Shake.prefab", 13, 0);

        config.cheeseSauceIngredient = null;
        config.cheeseFriesItem = cheeseFries;
        config.cheeseBaconFriesItem = cheeseBaconFries;
        config.whippedCreamIngredient = cream;
        config.sprinklesIngredient = sprinkles;
        config.whippedCreamShakeItem = creamShake;
        config.whippedCreamSprinkleShakeItem = sprinkleShake;
        config.SetVendorSupply(cream, StationType.Freezer);
        config.SetVendorSupply(sprinkles, StationType.Pantry);

        AddRecipe(config, "Cheese Fries", cheeseFries, config.friesItem, config.slicedCheeseIngredient);
        AddRecipe(config, "Cheese Bacon Fries", cheeseBaconFries, cheeseFries, config.cookedBaconIngredient);
        AddRecipe(config, "Whipped Cream Shake", creamShake, config.drinkItem, cream);
        AddRecipe(config, "Whipped Cream Sprinkle Shake", sprinkleShake, creamShake, sprinkles);
        EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("Installed expanded fries and shake recipes.");
    }

    static void RemoveRetiredBranches(CustomerOrderConfig config)
    {
        config.EnsureProductionRecipeGraph();
        var retiredItems = new HashSet<ItemDefinition>
        {
            config.onionIngredient,
            config.onionRingIngredient,
            config.friedOnionRingIngredient,
            config.eggIngredient,
            config.cookedEggIngredient,
            config.cheeseSauceIngredient,
            config.cheeseBaconLettuceOnionBurgerItem,
            config.cheeseBaconLettuceOnionTomatoBurgerItem,
            config.cheeseBaconLettuceOnionTomatoEggBurgerItem
        };
        retiredItems.Remove(null);

        config.productionRecipes.RemoveAll(recipe => recipe == null || recipe.output == null
            || retiredItems.Contains(recipe.output)
            || (recipe.inputs != null && recipe.inputs.Exists(input => input != null
                && retiredItems.Contains(input.item))));
        config.vendorSupplies.RemoveAll(source => source == null || source.item == null
            || retiredItems.Contains(source.item));

        config.onionIngredient = null;
        config.onionRingIngredient = null;
        config.friedOnionRingIngredient = null;
        config.eggIngredient = null;
        config.cookedEggIngredient = null;
        config.cheeseSauceIngredient = null;
        config.cheeseBaconLettuceOnionBurgerItem = null;
        config.cheeseBaconLettuceOnionTomatoBurgerItem = null;
        config.cheeseBaconLettuceOnionTomatoEggBurgerItem = null;
    }

    static ItemDefinition Item(string name, string prefabName, int price, int packPrice)
    {
        string path = Data + name + ".asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null) { item = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(item, path); }
        item.itemName = name; item.price = price; item.orderPackSize = 10; item.orderPackPrice = packPrice;
        item.prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Food Models/" + prefabName);
        EditorUtility.SetDirty(item); return item;
    }

    static void AddRecipe(CustomerOrderConfig c, string name, ItemDefinition output,
        ItemDefinition processed, ItemDefinition pantry)
    {
        var recipe = new RecipeGraphNodeDefinition
        {
            recipeName = name,
            stationType = StationType.Assembly,
            output = output,
            baseCycleSeconds = 4f,
            inputs = new List<RecipeGraphInputDefinition>()
        };
        if (processed != null)
            recipe.inputs.Add(new RecipeGraphInputDefinition { port = 0, item = processed, amount = 1 });
        if (pantry != null)
            recipe.inputs.Add(new RecipeGraphInputDefinition
            {
                port = 1,
                item = pantry,
                amount = 1
            });
        c.AddOrReplaceProductionRecipe(recipe);
    }
}
#endif
