#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ExpandedFoodMenuSetup
{
    const string Data = "Assets/Game/Customers/Data/";

    [MenuItem("ISE/Setup/Install Expanded Fries and Shakes")]
    public static void Install()
    {
        var config = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(Data + "CustomerOrderConfig.asset");
        if (config == null) { Debug.LogError("CustomerOrderConfig not found."); return; }
        var sauce = Item("Cheese Sauce", "Cheese Sauce.prefab", 2, 8);
        var cheeseFries = Item("Cheese Fries", "C Fries.prefab", 11, 0);
        var cheeseBaconFries = Item("Cheese Bacon Fries", "CB Fries.prefab", 14, 0);
        var cream = Item("Whipped Cream", "Whipped Cream Shake.prefab", 2, 8);
        var sprinkles = Item("Sprinkles", "Sprinkles.prefab", 1, 6);
        var creamShake = Item("Whipped Cream Shake", "Whipped Cream Shake.prefab", 11, 0);
        var sprinkleShake = Item("Whipped Cream Sprinkle Shake", "Whiped Cream Sprinkles Shake.prefab", 13, 0);

        config.cheeseSauceIngredient = sauce;
        config.cheeseFriesItem = cheeseFries;
        config.cheeseBaconFriesItem = cheeseBaconFries;
        config.whippedCreamIngredient = cream;
        config.sprinklesIngredient = sprinkles;
        config.whippedCreamShakeItem = creamShake;
        config.whippedCreamSprinkleShakeItem = sprinkleShake;

        AddRecipe(config, "CL Burger", config.clBurgerItem, config.cheeseburgerItem,
            config.slicedLettuceIngredient, config.lettuceIngredient,
            new[] { StationType.Cutting }, new[] { config.lettuceIngredient, config.slicedLettuceIngredient });
        AddRecipe(config, "CLT Burger", config.cltBurgerItem, config.clBurgerItem,
            config.slicedTomatoIngredient, config.tomatoIngredient,
            new[] { StationType.Cutting }, new[] { config.tomatoIngredient, config.slicedTomatoIngredient });
        AddRecipe(config, "Cheese Sauce", sauce, null, config.cheeseIngredient, sauce,
            new[] { StationType.Pantry, StationType.Grill }, new[] { config.cheeseIngredient, sauce });
        AddRecipe(config, "Cheese Fries", cheeseFries, config.friesItem, sauce, null, null, null);
        AddRecipe(config, "Cheese Bacon Fries", cheeseBaconFries, cheeseFries, config.cookedBaconIngredient,
            config.baconSlabIngredient, new[] { StationType.Freezer, StationType.Cutting, StationType.Grill },
            new[] { config.baconSlabIngredient, config.cutBaconIngredient, config.cookedBaconIngredient });
        AddRecipe(config, "Whipped Cream Shake", creamShake, config.drinkItem, cream, null, null, null, false);
        AddRecipe(config, "Whipped Cream Sprinkle Shake", sprinkleShake, creamShake, sprinkles, null, null, null);
        EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("Installed CL/CLT burgers, Cheese Fries, Cheese Bacon Fries, and whipped-cream shake recipes.");
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

    static void AddRecipe(CustomerOrderConfig c, string name, ItemDefinition output, ItemDefinition processed, ItemDefinition pantry, ItemDefinition raw, StationType[] pipe, ItemDefinition[] stages, bool fromFreezer = false)
    {
        if (c.assemblyRecipes.Exists(recipe => recipe != null && recipe.output == output)) return;
        c.assemblyRecipes.Add(new AssemblyRecipeDefinition { recipeName=name, output=output, processedInput=processed,
            processedInputName=processed != null ? processed.itemName : "", pantryInput=pantry, rawPantryInput=raw,
            processedInputFromFreezer=fromFreezer, supplyPipeline=pipe, supplyStageOutputs=stages });
    }
}
#endif
