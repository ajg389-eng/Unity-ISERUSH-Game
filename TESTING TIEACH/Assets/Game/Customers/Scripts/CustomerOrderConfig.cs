using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class RecipeGraphInputDefinition
{
    [Min(0)] public int port;
    public ItemDefinition item;
    [Min(1)] public int amount = 1;
}

[System.Serializable]
public class RecipeGraphNodeDefinition
{
    public string recipeName;
    public StationType stationType;
    public ItemDefinition output;
    [Min(0.01f)] public float baseCycleSeconds = 4f;
    public List<RecipeGraphInputDefinition> inputs = new List<RecipeGraphInputDefinition>();
}

[System.Serializable]
public class RecipeSupplySourceDefinition
{
    public ItemDefinition item;
    public StationType stationType;
}

[System.Serializable]
public class AssemblyRecipeDefinition
{
    public string recipeName = "Burger";
    public ItemDefinition output;
    [Tooltip("Seconds for one item at 100% on an MK1 station. MK2 stations run this recipe twice as fast.")]
    [Min(0.01f)] public float baseCycleSeconds = 4f;

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
    public StationType[] supplyPipeline = System.Array.Empty<StationType>();
    [Tooltip("Item carried after each station in Supply Pipeline.")]
    public ItemDefinition[] supplyStageOutputs = System.Array.Empty<ItemDefinition>();

    [Header("MK2 third input")]
    [Tooltip("Required only by Assembly Station MK2 recipes. MK1 cannot process recipes with this input.")]
    public ItemDefinition thirdInput;
    [Min(1)] public int thirdInputAmount = 1;
    public ItemDefinition rawThirdInput;
    public StationType[] thirdSupplyPipeline = System.Array.Empty<StationType>();
    public ItemDefinition[] thirdSupplyStageOutputs = System.Array.Empty<ItemDefinition>();

    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName)
        ? recipeName
        : (output != null && !string.IsNullOrWhiteSpace(output.itemName) ? output.itemName : "Recipe");

    public bool RequiresProcessedInput => processedInput != null;
    public bool Produces(ItemDefinition item) => item != null && output == item;
    public bool RequiresMk2 => thirdInput != null;

}

[System.Serializable]
public class StationProcessingRecipeDefinition
{
    public string recipeName;
    public ItemDefinition input;
    [Tooltip("Optional second ingredient consumed by this station recipe.")]
    public ItemDefinition secondaryInput;
    public ItemDefinition output;
    [Tooltip("Seconds for one item at 100% on an MK1 station. MK2 stations run this recipe twice as fast.")]
    [Min(0.01f)] public float baseCycleSeconds = 4f;
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
    public const int MaxItemReputation = 100;
    public const int ReputationPerDemandTier = 20;
    public const int DemandBonusPerTier = 5;
    public const int MaxItemDemandBonusPerMinute = 25;

    [System.Serializable]
    public class SavedItemReputation
    {
        public string itemName;
        public int reputation;
        public int activeDemandTier;
    }

    sealed class ItemReputationState
    {
        public int reputation;
        public int activeDemandTier;
    }

    [Header("Menu items")]
    [Tooltip("Burger / patty line item")]
    public ItemDefinition burgerBase;
    [Tooltip("Optional premium burger assembled through additional recipe stages.")]
    public ItemDefinition cheeseburgerItem;
    public ItemDefinition clBurgerItem;
    public ItemDefinition cltBurgerItem;
    public ItemDefinition cheeseBaconBurgerItem;
    public ItemDefinition superBurgerItem;
    public ItemDefinition cheeseBaconLettuceBurgerItem;
    [HideInInspector] public ItemDefinition cheeseBaconLettuceOnionBurgerItem;
    [HideInInspector] public ItemDefinition cheeseBaconLettuceOnionTomatoBurgerItem;
    [HideInInspector] public ItemDefinition cheeseBaconLettuceOnionTomatoEggBurgerItem;
    [Tooltip("Intermediate made from Tomato Slice and Lettuce Slice.")]
    public ItemDefinition veggieMixIngredient;
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
    [Header("Canonical production graph")]
    [Tooltip("All station recipes in one item graph. Each input points to a produced item or an explicit vendor supply.")]
    public List<RecipeGraphNodeDefinition> productionRecipes = new List<RecipeGraphNodeDefinition>();
    [Tooltip("Items that can be purchased and dispensed by a Freezer or Pantry.")]
    public List<RecipeSupplySourceDefinition> vendorSupplies = new List<RecipeSupplySourceDefinition>();

    [Header("Legacy recipe views")]
    [HideInInspector, Tooltip("Generated compatibility views. Edit Production Recipes above instead.")]
    public List<AssemblyRecipeDefinition> assemblyRecipes = new List<AssemblyRecipeDefinition>();
    [Header("Burger toppings")]
    public ItemDefinition lettuceIngredient;
    public ItemDefinition cheeseIngredient;
    [Tooltip("Processed cheese produced by the Cutting Station and consumed by Assembly recipes.")]
    public ItemDefinition slicedCheeseIngredient;
    public ItemDefinition slicedLettuceIngredient;
    public ItemDefinition tomatoIngredient;
    public ItemDefinition slicedTomatoIngredient;
    [Header("Bacon")]
    [FormerlySerializedAs("rawBaconIngredient")]
    [Tooltip("Bacon slab stocked by a Freezer and carried to a Cutting Station.")]
    public ItemDefinition baconSlabIngredient;
    [Tooltip("Cut bacon produced by a Cutting Station and carried to a Grill.")]
    public ItemDefinition cutBaconIngredient;
    public ItemDefinition cookedBaconIngredient;
    [HideInInspector] public ItemDefinition onionIngredient;
    [HideInInspector] public ItemDefinition onionRingIngredient;
    [HideInInspector] public ItemDefinition friedOnionRingIngredient;
    [HideInInspector] public ItemDefinition eggIngredient;
    [HideInInspector] public ItemDefinition cookedEggIngredient;
    [HideInInspector] public List<StationProcessingRecipeDefinition> grillRecipes = new List<StationProcessingRecipeDefinition>();
    [HideInInspector] public List<StationProcessingRecipeDefinition> fryerRecipes = new List<StationProcessingRecipeDefinition>();
    [Header("Cutting recipes")]
    [HideInInspector] public List<CuttingRecipeDefinition> cuttingRecipes = new List<CuttingRecipeDefinition>();
    [Tooltip("Shake")]
    public ItemDefinition drinkItem;
    public ItemDefinition whippedCreamShakeItem;
    public ItemDefinition whippedCreamSprinkleShakeItem;
    [HideInInspector] public ItemDefinition cheeseSauceIngredient;
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
    // A new run starts with every sellable item disabled. The player explicitly
    // enables each unlocked recipe from the Menu screen.
    [System.NonSerialized] readonly HashSet<ItemDefinition> enabledMenuItems = new HashSet<ItemDefinition>();
    [System.NonSerialized] bool demandScheduleReady;
    [System.NonSerialized] int demandScheduleRevision;
    [System.NonSerialized] int demandCustomersPerMinute;
    [System.NonSerialized] CustomerOrder[] demandOrderCycle = System.Array.Empty<CustomerOrder>();
    [System.NonSerialized] readonly Dictionary<ItemDefinition, int> demandRateByItem =
        new Dictionary<ItemDefinition, int>();
    [System.NonSerialized] readonly Dictionary<string, ItemReputationState> itemReputationByName =
        new Dictionary<string, ItemReputationState>(System.StringComparer.Ordinal);
    [System.NonSerialized] bool recipeGraphReady;
    [System.NonSerialized] bool rebuildingRecipeGraph;

    sealed class DemandProduct
    {
        public ItemDefinition item;
        public int rate;
    }

    void OnEnable()
    {
        EnsureProductionRecipeGraph();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        recipeGraphReady = false;
        EnsureProductionRecipeGraph();
        EditorUtility.SetDirty(this);
    }
#endif

    /// <summary>
    /// The serialized graph is authoritative. The older station-specific recipe
    /// lists are rebuilt as compatibility views for existing UI and station code.
    /// </summary>
    public void EnsureProductionRecipeGraph()
    {
        if (recipeGraphReady || rebuildingRecipeGraph) return;
        rebuildingRecipeGraph = true;
        if (productionRecipes == null) productionRecipes = new List<RecipeGraphNodeDefinition>();
        if (vendorSupplies == null) vendorSupplies = new List<RecipeSupplySourceDefinition>();

        if (productionRecipes.Count == 0)
            MigrateLegacyRecipesIntoGraph();
        if (vendorSupplies.Count == 0)
            InferVendorSuppliesFromGraph();

        RebuildLegacyRecipeViews();
        recipeGraphReady = true;
        rebuildingRecipeGraph = false;
    }

    void MigrateLegacyRecipesIntoGraph()
    {
        productionRecipes.Clear();
        if (assemblyRecipes != null)
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                if (recipe == null || recipe.output == null) continue;
                var node = NewGraphNode(recipe.recipeName, StationType.Assembly, recipe.output,
                    recipe.baseCycleSeconds);
                AddGraphInput(node, 0, recipe.processedInput, recipe.processedInputAmount);
                AddGraphInput(node, 1, recipe.pantryInput, recipe.pantryInputAmount);
                AddGraphInput(node, 2, recipe.thirdInput, recipe.thirdInputAmount);
                productionRecipes.Add(node);
            }

        AddLegacyProcessingRecipes(StationType.Grill, grillRecipes);
        AddLegacyProcessingRecipes(StationType.Fryer, fryerRecipes);
        if (cuttingRecipes != null)
            foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            {
                if (recipe == null || recipe.output == null) continue;
                var node = NewGraphNode(recipe.recipeName, StationType.Cutting, recipe.output,
                    recipe.baseCycleSeconds);
                AddGraphInput(node, 0, recipe.input, 1);
                productionRecipes.Add(node);
            }
    }

    void AddLegacyProcessingRecipes(StationType stationType,
        List<StationProcessingRecipeDefinition> recipes)
    {
        if (recipes == null) return;
        foreach (StationProcessingRecipeDefinition recipe in recipes)
        {
            if (recipe == null || recipe.output == null) continue;
            var node = NewGraphNode(recipe.recipeName, stationType, recipe.output,
                recipe.baseCycleSeconds);
            AddGraphInput(node, 0, recipe.input, 1);
            AddGraphInput(node, 1, recipe.secondaryInput, 1);
            productionRecipes.Add(node);
        }
    }

    static RecipeGraphNodeDefinition NewGraphNode(string name, StationType stationType,
        ItemDefinition output, float cycleSeconds) => new RecipeGraphNodeDefinition
    {
        recipeName = name,
        stationType = stationType,
        output = output,
        baseCycleSeconds = Mathf.Max(0.01f, cycleSeconds),
        inputs = new List<RecipeGraphInputDefinition>()
    };

    static void AddGraphInput(RecipeGraphNodeDefinition node, int port,
        ItemDefinition item, int amount)
    {
        if (node == null || item == null) return;
        node.inputs.Add(new RecipeGraphInputDefinition
        {
            port = port,
            item = item,
            amount = Mathf.Max(1, amount)
        });
    }

    void InferVendorSuppliesFromGraph()
    {
        vendorSupplies.Clear();
        AddVendorSupply(rawPattyIngredient, StationType.Freezer);
        AddVendorSupply(lettuceIngredient, StationType.Freezer);
        AddVendorSupply(tomatoIngredient, StationType.Freezer);
        AddVendorSupply(baconSlabIngredient, StationType.Freezer);
        AddVendorSupply(whippedCreamIngredient, StationType.Freezer);
        AddVendorSupply(friesIngredient, StationType.Pantry);
        AddVendorSupply(cheeseIngredient, StationType.Pantry);
        AddVendorSupply(sprinklesIngredient, StationType.Pantry);

        if (assemblyRecipes != null)
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                if (recipe == null) continue;
                if (recipe.processedInputFromFreezer)
                    AddVendorSupply(recipe.processedInput, StationType.Freezer);
                if (recipe.processedInputFromPantry)
                    AddVendorSupply(recipe.processedInput, StationType.Pantry);
            }

        // Any recipe input with no production step is a raw supply. Default it to
        // Pantry unless the configuration explicitly identifies it as Freezer stock.
        foreach (RecipeGraphNodeDefinition node in productionRecipes)
        {
            if (node?.inputs == null) continue;
            foreach (RecipeGraphInputDefinition input in node.inputs)
            {
                if (input?.item == null || HasRecipeProducer(input.item)
                    || IsRemovedIngredient(input.item)) continue;
                if (FindVendorSupply(input.item) != null) continue;
                AddVendorSupply(input.item, IsKnownFreezerSupply(input.item)
                    ? StationType.Freezer : StationType.Pantry);
            }
        }
    }

    bool IsKnownFreezerSupply(ItemDefinition item)
    {
        if (item == rawPattyIngredient || item == lettuceIngredient || item == tomatoIngredient
            || item == baconSlabIngredient || item == whippedCreamIngredient) return true;
        if (assemblyRecipes == null) return false;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.processedInputFromFreezer && recipe.processedInput == item)
                return true;
        return false;
    }

    void AddVendorSupply(ItemDefinition item, StationType stationType)
    {
        if (item == null || (stationType != StationType.Pantry && stationType != StationType.Freezer)
            || IsRemovedIngredient(item) || FindVendorSupply(item) != null) return;
        vendorSupplies.Add(new RecipeSupplySourceDefinition { item = item, stationType = stationType });
    }

    RecipeSupplySourceDefinition FindVendorSupply(ItemDefinition item)
    {
        if (item == null || vendorSupplies == null) return null;
        return vendorSupplies.Find(source => source != null && source.item == item);
    }

    bool HasRecipeProducer(ItemDefinition item)
    {
        return item != null && productionRecipes != null
            && productionRecipes.Exists(recipe => recipe != null && recipe.output == item);
    }

    void RebuildLegacyRecipeViews()
    {
        assemblyRecipes = new List<AssemblyRecipeDefinition>();
        grillRecipes = new List<StationProcessingRecipeDefinition>();
        fryerRecipes = new List<StationProcessingRecipeDefinition>();
        cuttingRecipes = new List<CuttingRecipeDefinition>();
        if (productionRecipes == null) return;

        foreach (RecipeGraphNodeDefinition node in productionRecipes)
        {
            if (node == null || node.output == null) continue;
            List<RecipeGraphInputDefinition> inputs = node.inputs ?? new List<RecipeGraphInputDefinition>();
            inputs.Sort((a, b) => (a?.port ?? 0).CompareTo(b?.port ?? 0));
            if (node.stationType == StationType.Assembly)
            {
                var recipe = new AssemblyRecipeDefinition
                {
                    recipeName = node.recipeName,
                    output = node.output,
                    baseCycleSeconds = Mathf.Max(0.01f, node.baseCycleSeconds),
                    supplyPipeline = System.Array.Empty<StationType>(),
                    supplyStageOutputs = System.Array.Empty<ItemDefinition>(),
                    thirdSupplyPipeline = System.Array.Empty<StationType>(),
                    thirdSupplyStageOutputs = System.Array.Empty<ItemDefinition>()
                };
                foreach (RecipeGraphInputDefinition input in inputs)
                {
                    if (input == null || input.item == null) continue;
                    int amount = Mathf.Max(1, input.amount);
                    switch (Mathf.Clamp(input.port, 0, 2))
                    {
                        case 0:
                            recipe.processedInput = input.item;
                            recipe.processedInputName = input.item.itemName;
                            recipe.processedInputAmount = amount;
                            RecipeSupplySourceDefinition processedSource = FindVendorSupply(input.item);
                            recipe.processedInputFromFreezer = processedSource?.stationType == StationType.Freezer;
                            recipe.processedInputFromPantry = processedSource?.stationType == StationType.Pantry;
                            break;
                        case 1:
                            recipe.pantryInput = input.item;
                            recipe.pantryInputAmount = amount;
                            AddLegacySupplyPathHints(recipe, input.item, false);
                            break;
                        case 2:
                            recipe.thirdInput = input.item;
                            recipe.thirdInputAmount = amount;
                            AddLegacySupplyPathHints(recipe, input.item, true);
                            break;
                    }
                }
                assemblyRecipes.Add(recipe);
            }
            else if (node.stationType == StationType.Grill || node.stationType == StationType.Fryer)
            {
                var recipe = new StationProcessingRecipeDefinition
                {
                    recipeName = node.recipeName,
                    output = node.output,
                    baseCycleSeconds = Mathf.Max(0.01f, node.baseCycleSeconds)
                };
                foreach (RecipeGraphInputDefinition input in inputs)
                {
                    if (input == null || input.item == null) continue;
                    if (input.port <= 0) recipe.input = input.item;
                    else recipe.secondaryInput = input.item;
                }
                (node.stationType == StationType.Grill ? grillRecipes : fryerRecipes).Add(recipe);
            }
            else if (node.stationType == StationType.Cutting)
            {
                RecipeGraphInputDefinition input = inputs.Find(value => value != null && value.item != null);
                cuttingRecipes.Add(new CuttingRecipeDefinition
                {
                    recipeName = node.recipeName,
                    input = input != null ? input.item : null,
                    output = node.output,
                    baseCycleSeconds = Mathf.Max(0.01f, node.baseCycleSeconds)
                });
            }
        }
    }

    void AddLegacySupplyPathHints(AssemblyRecipeDefinition recipe, ItemDefinition item, bool third)
    {
        var stations = new List<StationType>();
        var stages = new List<ItemDefinition>();
        if (!TryBuildSupplyPath(item, new HashSet<ItemDefinition>(), stations, stages)
            || stations.Count <= 1) return;
        if (third)
        {
            recipe.rawThirdInput = stages[0];
            recipe.thirdSupplyPipeline = stations.ToArray();
            recipe.thirdSupplyStageOutputs = stages.ToArray();
        }
        else
        {
            recipe.rawPantryInput = stages[0];
            recipe.supplyPipeline = stations.ToArray();
            recipe.supplyStageOutputs = stages.ToArray();
        }
    }

    bool TryBuildSupplyPath(ItemDefinition item, HashSet<ItemDefinition> visited,
        List<StationType> stations, List<ItemDefinition> stages)
    {
        if (item == null || !visited.Add(item)) return false;
        RecipeSupplySourceDefinition source = FindVendorSupply(item);
        if (source != null)
        {
            stations.Add(source.stationType);
            stages.Add(item);
            return true;
        }
        RecipeGraphNodeDefinition producer = productionRecipes != null
            ? productionRecipes.Find(recipe => recipe != null && recipe.output == item) : null;
        RecipeGraphInputDefinition upstream = producer?.inputs?.Find(value => value != null && value.item != null);
        if (producer == null || upstream == null
            || !TryBuildSupplyPath(upstream.item, visited, stations, stages)) return false;
        stations.Add(producer.stationType);
        stages.Add(item);
        return true;
    }

    public IReadOnlyList<RecipeGraphNodeDefinition> GetProductionRecipes()
    {
        EnsureProductionRecipeGraph();
        return productionRecipes;
    }

    public RecipeGraphNodeDefinition GetProductionRecipe(StationType stationType, ItemDefinition output)
    {
        EnsureProductionRecipeGraph();
        return productionRecipes.Find(recipe => recipe != null
            && recipe.stationType == stationType && recipe.output == output);
    }

    public StationType? GetVendorSupplyStation(ItemDefinition item)
    {
        EnsureProductionRecipeGraph();
        RecipeSupplySourceDefinition source = FindVendorSupply(item);
        return source != null ? source.stationType : (StationType?)null;
    }

    public void AddOrReplaceProductionRecipe(RecipeGraphNodeDefinition recipe)
    {
        if (recipe == null || recipe.output == null) return;
        EnsureProductionRecipeGraph();
        productionRecipes.RemoveAll(existing => existing != null
            && existing.stationType == recipe.stationType && existing.output == recipe.output);
        productionRecipes.Add(recipe);
        recipeGraphReady = false;
        EnsureProductionRecipeGraph();
    }

    public void SetVendorSupply(ItemDefinition item, StationType stationType)
    {
        if (item == null || (stationType != StationType.Pantry && stationType != StationType.Freezer)) return;
        EnsureProductionRecipeGraph();
        RecipeSupplySourceDefinition existing = FindVendorSupply(item);
        if (existing == null)
            vendorSupplies.Add(new RecipeSupplySourceDefinition { item = item, stationType = stationType });
        else
            existing.stationType = stationType;
        recipeGraphReady = false;
        EnsureProductionRecipeGraph();
    }

    public List<string> ValidateProductionRecipeGraph()
    {
        EnsureProductionRecipeGraph();
        var issues = new List<string>();
        var producers = new Dictionary<ItemDefinition, RecipeGraphNodeDefinition>();
        var usedItems = new HashSet<ItemDefinition>();

        foreach (RecipeSupplySourceDefinition source in vendorSupplies)
        {
            if (source == null || source.item == null)
            {
                issues.Add("A vendor supply entry has no item.");
                continue;
            }
            if (source.stationType != StationType.Pantry && source.stationType != StationType.Freezer)
                issues.Add(source.item.itemName + " has an invalid vendor source station.");
            if (!IsRemovedIngredient(source.item) && HasRecipeProducer(source.item))
                issues.Add(source.item.itemName + " is both purchased and produced by a recipe.");
        }

        foreach (RecipeGraphNodeDefinition recipe in productionRecipes)
        {
            if (recipe == null || recipe.output == null)
            {
                issues.Add("A production recipe has no output item.");
                continue;
            }
            if (recipe.stationType != StationType.Assembly && recipe.stationType != StationType.Grill
                && recipe.stationType != StationType.Fryer && recipe.stationType != StationType.Cutting)
                issues.Add(recipe.recipeName + " uses a station that cannot process recipes.");
            if (recipe.baseCycleSeconds <= 0f)
                issues.Add(recipe.recipeName + " needs a positive cycle time.");
            if (producers.ContainsKey(recipe.output))
                issues.Add(recipe.output.itemName + " has more than one production recipe.");
            else
                producers.Add(recipe.output, recipe);

            if (recipe.inputs == null || recipe.inputs.Count == 0)
                issues.Add(recipe.recipeName + " has no inputs.");
            else
                foreach (RecipeGraphInputDefinition input in recipe.inputs)
                {
                    if (input == null || input.item == null)
                    {
                        issues.Add(recipe.recipeName + " has an empty input.");
                        continue;
                    }
                    usedItems.Add(input.item);
                    if (input.amount < 1)
                        issues.Add(recipe.recipeName + " requires at least one " + input.item.itemName + ".");
                    int maxPort = recipe.stationType == StationType.Assembly ? 2
                        : recipe.stationType == StationType.Grill ? 1 : 0;
                    if (input.port < 0 || input.port > maxPort)
                        issues.Add(recipe.recipeName + " has an input in an unsupported station slot.");
                    if (!IsRemovedIngredient(input.item) && !HasRecipeProducer(input.item)
                        && FindVendorSupply(input.item) == null)
                        issues.Add(recipe.recipeName + " needs " + input.item.itemName
                            + ", but it has no producer or vendor source.");
                }
        }

        var visited = new HashSet<ItemDefinition>();
        var visiting = new HashSet<ItemDefinition>();
        var reportedCycles = new HashSet<ItemDefinition>();
        foreach (ItemDefinition item in producers.Keys)
            FindRecipeCycle(item, producers, visited, visiting, reportedCycles, issues);

        var roots = new List<ItemDefinition>(GetMenuItems());
        foreach (ItemDefinition item in roots)
            if (item != null && !IsRemovedIngredient(item) && !CanMakeItem(item,
                new HashSet<ItemDefinition>(), producers))
                issues.Add("Menu item " + item.itemName + " has no complete recipe path from vendor supplies.");

        foreach (RecipeGraphNodeDefinition recipe in productionRecipes)
            if (recipe != null && recipe.output != null && !IsRemovedIngredient(recipe.output)
                && !usedItems.Contains(recipe.output) && !roots.Contains(recipe.output))
                issues.Add("Recipe output " + recipe.output.itemName + " is unused by another recipe or the menu.");

        return issues;
    }

    void FindRecipeCycle(ItemDefinition item,
        Dictionary<ItemDefinition, RecipeGraphNodeDefinition> producers,
        HashSet<ItemDefinition> visited, HashSet<ItemDefinition> visiting,
        HashSet<ItemDefinition> reportedCycles, List<string> issues)
    {
        if (item == null || visited.Contains(item) || !producers.TryGetValue(item, out RecipeGraphNodeDefinition recipe))
            return;
        if (!visiting.Add(item))
        {
            if (reportedCycles.Add(item)) issues.Add("Recipe dependency loop includes " + item.itemName + ".");
            return;
        }
        if (recipe.inputs != null)
            foreach (RecipeGraphInputDefinition input in recipe.inputs)
                if (input != null && input.item != null)
                    FindRecipeCycle(input.item, producers, visited, visiting, reportedCycles, issues);
        visiting.Remove(item);
        visited.Add(item);
    }

    bool CanMakeItem(ItemDefinition item, HashSet<ItemDefinition> visiting,
        Dictionary<ItemDefinition, RecipeGraphNodeDefinition> producers)
    {
        if (item == null || IsRemovedIngredient(item)) return false;
        if (FindVendorSupply(item) != null) return true;
        if (!producers.TryGetValue(item, out RecipeGraphNodeDefinition recipe)) return false;
        if (!visiting.Add(item)) return false;
        bool complete = recipe.inputs != null && recipe.inputs.Count > 0;
        if (complete)
            foreach (RecipeGraphInputDefinition input in recipe.inputs)
                if (input == null || !CanMakeItem(input.item, visiting, producers))
                {
                    complete = false;
                    break;
                }
        visiting.Remove(item);
        return complete;
    }

    /// <summary>Customers scheduled per simulation minute to satisfy the enabled menu rates.</summary>
    public int DemandCustomersPerMinute
    {
        get
        {
            EnsureDemandSchedule();
            return demandCustomersPerMinute;
        }
    }

    public int CalculateDemandCustomersPerMinute()
    {
        int[] categoryRates = new int[3];
        foreach (ItemDefinition item in GetEnabledMenuItems())
        {
            int categoryIndex = (int)GetProductKind(item) - 1;
            if (categoryIndex < 0 || categoryIndex >= categoryRates.Length) continue;
            categoryRates[categoryIndex] += GetDemandRate(item);
        }
        return Mathf.Max(categoryRates[0], Mathf.Max(categoryRates[1], categoryRates[2]));
    }

    public int DemandScheduleRevision
    {
        get
        {
            EnsureDemandSchedule();
            return demandScheduleRevision;
        }
    }

    public int GetCustomerDemandPerMinute(ItemDefinition item)
    {
        EnsureDemandSchedule();
        return item != null && demandRateByItem.TryGetValue(item, out int rate) ? rate : 0;
    }

    public int GetItemReputation(ItemDefinition item)
    {
        return TryGetItemReputation(item, out ItemReputationState state) ? state.reputation : 0;
    }

    public int GetPendingItemDemandChangePerMinute(ItemDefinition item)
    {
        if (!TryGetItemReputation(item, out ItemReputationState state)) return 0;
        int targetTier = GetDemandTier(state.reputation);
        return (targetTier - state.activeDemandTier) * DemandBonusPerTier;
    }

    public void RecordItemServed(ItemDefinition item)
    {
        if (!IsTrackedMenuItem(item)) return;
        ItemReputationState state = GetOrCreateItemReputation(item);
        state.reputation = Mathf.Min(MaxItemReputation, state.reputation + 1);
    }

    public void RecordItemLeftUnserved(ItemDefinition item, int quantity)
    {
        if (!IsTrackedMenuItem(item) || quantity <= 0) return;
        ItemReputationState state = GetOrCreateItemReputation(item);
        state.reputation = Mathf.Max(0, state.reputation - quantity * 2);
    }

    /// <summary>Applies reputation earned during the previous shift to today's fixed demand schedule.</summary>
    public void ApplyItemReputationForNewShift()
    {
        bool changed = false;
        foreach (ItemReputationState state in itemReputationByName.Values)
        {
            int tier = GetDemandTier(state.reputation);
            if (state.activeDemandTier == tier) continue;
            state.activeDemandTier = tier;
            changed = true;
        }

        if (changed)
            InvalidateDemandSchedule();
    }

    public List<SavedItemReputation> CaptureItemReputation()
    {
        var saved = new List<SavedItemReputation>(itemReputationByName.Count);
        foreach (KeyValuePair<string, ItemReputationState> pair in itemReputationByName)
        {
            if (pair.Value == null) continue;
            saved.Add(new SavedItemReputation
            {
                itemName = pair.Key,
                reputation = pair.Value.reputation,
                activeDemandTier = pair.Value.activeDemandTier
            });
        }
        return saved;
    }

    public void RestoreItemReputation(IEnumerable<SavedItemReputation> saved)
    {
        itemReputationByName.Clear();
        if (saved != null)
        {
            var menuItemNames = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ItemDefinition item in GetMenuItems())
                if (item != null && !string.IsNullOrEmpty(item.name)) menuItemNames.Add(item.name);

            foreach (SavedItemReputation record in saved)
            {
                if (record == null || string.IsNullOrEmpty(record.itemName)
                    || !menuItemNames.Contains(record.itemName)) continue;
                itemReputationByName[record.itemName] = new ItemReputationState
                {
                    reputation = Mathf.Clamp(record.reputation, 0, MaxItemReputation),
                    activeDemandTier = Mathf.Clamp(record.activeDemandTier, 0,
                        MaxItemDemandBonusPerMinute / DemandBonusPerTier)
                };
            }
        }
        InvalidateDemandSchedule();
    }

    public void ClearItemReputation()
    {
        itemReputationByName.Clear();
        InvalidateDemandSchedule();
    }

    bool TryGetItemReputation(ItemDefinition item, out ItemReputationState state)
    {
        state = null;
        return item != null && !string.IsNullOrEmpty(item.name)
            && itemReputationByName.TryGetValue(item.name, out state) && state != null;
    }

    ItemReputationState GetOrCreateItemReputation(ItemDefinition item)
    {
        if (TryGetItemReputation(item, out ItemReputationState state)) return state;
        state = new ItemReputationState();
        itemReputationByName[item.name] = state;
        return state;
    }

    bool IsTrackedMenuItem(ItemDefinition item)
    {
        if (item == null) return false;
        foreach (ItemDefinition candidate in GetMenuItems())
            if (candidate == item) return true;
        return false;
    }

    static int GetDemandTier(int reputation)
    {
        return Mathf.Clamp(reputation / ReputationPerDemandTier, 0,
            MaxItemDemandBonusPerMinute / DemandBonusPerTier);
    }

    /// <summary>Returns the next order in the exact repeating one-minute demand cycle.</summary>
    public CustomerOrder GenerateScheduledOrder(int sequenceIndex)
    {
        EnsureDemandSchedule();
        if (demandOrderCycle.Length == 0) return new CustomerOrder();
        int index = (sequenceIndex % demandOrderCycle.Length + demandOrderCycle.Length)
            % demandOrderCycle.Length;
        return demandOrderCycle[index].Clone();
    }

    /// <summary>Returns an immutable copy of the current repeating order cycle for backlog scheduling.</summary>
    public CustomerOrder[] GetScheduledOrderCycleSnapshot()
    {
        EnsureDemandSchedule();
        if (demandOrderCycle.Length == 0) return System.Array.Empty<CustomerOrder>();

        var snapshot = new CustomerOrder[demandOrderCycle.Length];
        for (int i = 0; i < demandOrderCycle.Length; i++)
            snapshot[i] = demandOrderCycle[i] != null
                ? demandOrderCycle[i].Clone()
                : new CustomerOrder();
        return snapshot;
    }

    void EnsureDemandSchedule()
    {
        if (demandScheduleReady) return;

        demandRateByItem.Clear();
        var categories = new List<DemandProduct>[3]
        {
            new List<DemandProduct>(),
            new List<DemandProduct>(),
            new List<DemandProduct>()
        };
        int[] categoryRates = new int[3];
        foreach (ItemDefinition item in GetEnabledMenuItems())
        {
            ProductKind kind = GetProductKind(item);
            int categoryIndex = (int)kind - 1;
            if (categoryIndex < 0 || categoryIndex >= categories.Length) continue;

            int adjustedRate = GetDemandRate(item);
            demandRateByItem[item] = adjustedRate;
            categories[categoryIndex].Add(new DemandProduct { item = item, rate = adjustedRate });
            categoryRates[categoryIndex] += adjustedRate;
        }

        demandCustomersPerMinute = Mathf.Max(categoryRates[0],
            Mathf.Max(categoryRates[1], categoryRates[2]));
        if (demandCustomersPerMinute > 0)
        {
            demandOrderCycle = new CustomerOrder[demandCustomersPerMinute];
            for (int i = 0; i < demandOrderCycle.Length; i++)
                demandOrderCycle[i] = new CustomerOrder();

            for (int categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
                AddCategoryDemandToCycle(categories[categoryIndex], categoryRates[categoryIndex],
                    demandCustomersPerMinute, demandOrderCycle);
        }
        else
            demandOrderCycle = System.Array.Empty<CustomerOrder>();

        demandScheduleReady = true;
        demandScheduleRevision++;
    }

    int GetDemandRate(ItemDefinition item)
    {
        AssemblyRecipeDefinition recipe = GetAssemblyRecipe(item);
        float baseRate = recipe != null && recipe.baseCycleSeconds > 0.01f
            ? 60f / recipe.baseCycleSeconds
            : 15f;
        // Each item starts at recipe output; reputation adds fixed five-item steps.
        int baseDemand = Mathf.Max(5, Mathf.FloorToInt(baseRate / 5f + 0.5f) * 5);
        int reputationBonus = TryGetItemReputation(item, out ItemReputationState state)
            ? state.activeDemandTier * DemandBonusPerTier
            : 0;
        return baseDemand + Mathf.Clamp(reputationBonus, 0, MaxItemDemandBonusPerMinute);
    }

    static void AddCategoryDemandToCycle(List<DemandProduct> products, int categoryRate,
        int customersPerMinute, CustomerOrder[] cycle)
    {
        if (products == null || products.Count == 0 || categoryRate <= 0
            || customersPerMinute <= 0 || cycle == null) return;

        List<ItemDefinition> weightedCycle = BuildWeightedItemCycle(products, categoryRate);
        int emittedBefore = 0;
        for (int slot = 0; slot < customersPerMinute; slot++)
        {
            int emittedAfter = ((slot + 1) * categoryRate) / customersPerMinute;
            for (int emitted = emittedBefore; emitted < emittedAfter; emitted++)
                cycle[slot].lines.Add(new CustomerOrder.OrderLine(weightedCycle[emitted], 1));
            emittedBefore = emittedAfter;
        }
    }

    static List<ItemDefinition> BuildWeightedItemCycle(List<DemandProduct> products, int totalRate)
    {
        var result = new List<ItemDefinition>(totalRate);
        if (products == null || products.Count == 0 || totalRate <= 0) return result;

        var credits = new int[products.Count];
        for (int step = 0; step < totalRate; step++)
        {
            int selected = 0;
            for (int i = 0; i < products.Count; i++)
            {
                credits[i] += products[i].rate;
                if (credits[i] > credits[selected]) selected = i;
            }
            result.Add(products[selected].item);
            credits[selected] -= totalRate;
        }
        return result;
    }

    void InvalidateDemandSchedule() => demandScheduleReady = false;

    public bool IsBurger(ItemDefinition item) => item != null && (item == burgerBase
        || item == cheeseburgerItem || item == cheeseBaconBurgerItem || item == clBurgerItem
        || item == superBurgerItem);
    bool IsRemovedIngredient(ItemDefinition item) => item != null
        && (item == onionIngredient || item == onionRingIngredient || item == friedOnionRingIngredient
            || item == eggIngredient || item == cookedEggIngredient
            || item == cheeseBaconLettuceOnionBurgerItem
            || item == cheeseBaconLettuceOnionTomatoBurgerItem
            || item == cheeseBaconLettuceOnionTomatoEggBurgerItem);

    static string RecipeResearchId(int stage)
    {
        return stage switch
        {
            <= 1 => "recipe_upgrades_1",
            2 => "recipe_upgrades_2",
            3 => "recipe_upgrades_3",
            4 => "recipe_upgrades_4",
            _ => "recipe_upgrades_5",
        };
    }

    /// <summary>Returns the research title for a recipe progression stage.</summary>
    public static string RecipeResearchTitleForStage(int stage)
    {
        if (stage <= 1) return "Restaurant Basics / Recipe Upgrades I";
        ResearchProgressManager.Definition definition = ResearchProgressManager.Get(RecipeResearchId(stage));
        return definition != null && !string.IsNullOrWhiteSpace(definition.title)
            ? definition.title
            : "Recipe Research";
    }
    public bool IsCheeseburger(ItemDefinition item) => item != null && item == cheeseburgerItem;
    public bool IsFries(ItemDefinition item) => item != null && (item == friesItem || item == cheeseFriesItem || item == cheeseBaconFriesItem);
    public bool IsDrink(ItemDefinition item) => item != null && (item == drinkItem || item == whippedCreamShakeItem || item == whippedCreamSprinkleShakeItem);

    public int GetMenuItemUnlockMilestone(ItemDefinition item)
    {
        if (item == null) return 0;
        if (item == burgerBase || item == friesItem || item == drinkItem) return 1;
        if (item == cheeseburgerItem || item == cheeseFriesItem || item == whippedCreamShakeItem) return 2;
        if (item == cheeseBaconBurgerItem || item == clBurgerItem) return 3;
        if (item == cheeseBaconFriesItem || item == whippedCreamSprinkleShakeItem) return 4;
        if (item == superBurgerItem) return 5;
        return 0;
    }

    string MenuItemResearchId(ItemDefinition item)
    {
        if (item == null) return null;
        if (item == burgerBase) return "production_basics";
        int milestone = GetMenuItemUnlockMilestone(item);
        return milestone > 0 ? RecipeResearchId(milestone) : null;
    }

    public string RecipeResearchTitleForItem(ItemDefinition item)
    {
        string researchId = IngredientResearchId(item);
        ResearchProgressManager.Definition definition = !string.IsNullOrEmpty(researchId)
            ? ResearchProgressManager.Get(researchId) : null;
        return definition != null && !string.IsNullOrWhiteSpace(definition.title)
            ? definition.title
            : RecipeResearchTitleForStage(GetIngredientMilestone(item));
    }

    public bool IsMenuItemUnlocked(ItemDefinition item)
    {
        if (IsRemovedIngredient(item)) return false;
        if (OnboardingTutorial.BlocksProgression && IsTutorialMenuItem(item)) return true;
        string researchId = MenuItemResearchId(item);
        return string.IsNullOrEmpty(researchId) || MilestoneFeatures.IsResearchComplete(researchId);
    }

    public bool IsIngredientUnlocked(ItemDefinition item)
    {
        EnsureProductionRecipeGraph();
        if (IsRemovedIngredient(item)) return false;
        if (IsTutorialBurgerItem(item))
            return true;
        if (item == null) return false;
        string researchId = IngredientResearchId(item);
        return string.IsNullOrEmpty(researchId) || MilestoneFeatures.IsResearchComplete(researchId);
    }

    string IngredientResearchId(ItemDefinition item)
    {
        if (item == null) return null;
        EnsureProductionRecipeGraph();
        if (GetMenuItemUnlockMilestone(item) > 0)
            return MenuItemResearchId(item);
        if (item == rawPattyIngredient || IsBaseBurgerRecipeInput(item))
            return "production_basics";
        return RecipeResearchId(GetIngredientMilestone(item));
    }

    bool IsBaseBurgerRecipeInput(ItemDefinition item)
    {
        if (item == null || assemblyRecipes == null) return false;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.Produces(burgerBase) && RecipeUsesIngredient(recipe, item))
                return true;
        return false;
    }

    bool IsTutorialMenuItem(ItemDefinition item) => item != null && item == burgerBase;

    bool IsTutorialBurgerItem(ItemDefinition item)
    {
        if (!OnboardingTutorial.BlocksProgression || item == null) return false;
        if (item == rawPattyIngredient || item == cookedPattyIngredient || item == burgerBase)
            return true;
        if (assemblyRecipes == null) return false;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.Produces(burgerBase) && RecipeUsesIngredient(recipe, item))
                return true;
        return false;
    }

    public int GetIngredientUnlockMilestone(ItemDefinition item) => GetIngredientMilestone(item);

    int GetRecipeUnlockMilestone(AssemblyRecipeDefinition recipe)
    {
        EnsureProductionRecipeGraph();
        if (recipe == null) return int.MaxValue;
        int milestone = GetMenuItemUnlockMilestone(recipe.output);
        if (milestone == 0) milestone = GetIngredientMilestone(recipe.output);
        milestone = Mathf.Max(milestone, GetIngredientMilestone(recipe.processedInput));
        milestone = Mathf.Max(milestone, GetIngredientMilestone(recipe.pantryInput));
        milestone = Mathf.Max(milestone, GetIngredientMilestone(recipe.rawPantryInput));
        milestone = Mathf.Max(milestone, GetIngredientMilestone(recipe.thirdInput));
        milestone = Mathf.Max(milestone, GetIngredientMilestone(recipe.rawThirdInput));
        return Mathf.Max(1, milestone);
    }

    int GetIngredientMilestone(ItemDefinition item)
    {
        EnsureProductionRecipeGraph();
        if (item == null) return 1;
        int knownMilestone = GetKnownIngredientMilestone(item);
        if (knownMilestone > 0) return knownMilestone;

        // Recipe inputs without their own config field (such as milk and shake cups)
        // inherit the earliest milestone of a product that needs them.
        int earliestRecipeMilestone = 0;
        if (assemblyRecipes != null)
        {
            foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            {
                if (recipe == null || !RecipeUsesIngredient(recipe, item)) continue;
                int recipeMilestone = GetKnownIngredientMilestone(recipe.output);
                recipeMilestone = Mathf.Max(recipeMilestone, GetKnownIngredientMilestone(recipe.processedInput == item ? null : recipe.processedInput));
                recipeMilestone = Mathf.Max(recipeMilestone, GetKnownIngredientMilestone(recipe.pantryInput == item ? null : recipe.pantryInput));
                recipeMilestone = Mathf.Max(recipeMilestone, GetKnownIngredientMilestone(recipe.rawPantryInput == item ? null : recipe.rawPantryInput));
                recipeMilestone = Mathf.Max(recipeMilestone, GetKnownIngredientMilestone(recipe.thirdInput == item ? null : recipe.thirdInput));
                recipeMilestone = Mathf.Max(recipeMilestone, GetKnownIngredientMilestone(recipe.rawThirdInput == item ? null : recipe.rawThirdInput));
                if (recipeMilestone > 0 && (earliestRecipeMilestone == 0 || recipeMilestone < earliestRecipeMilestone))
                    earliestRecipeMilestone = recipeMilestone;
            }
        }
        return earliestRecipeMilestone > 0 ? earliestRecipeMilestone : 1;
    }

    int GetKnownIngredientMilestone(ItemDefinition item)
    {
        if (item == null) return 0;
        int menuMilestone = GetMenuItemUnlockMilestone(item);
        if (menuMilestone > 0) return menuMilestone;
        if (item == cheeseIngredient || item == slicedCheeseIngredient) return 2;
        if (item == rawPattyIngredient || item == cookedPattyIngredient
            || item == friesIngredient || item == slicedPotatoIngredient
            || item == cookedPotatoIngredient || item == fryContainerIngredient) return 1;
        if (item == lettuceIngredient || item == slicedLettuceIngredient
            || item == tomatoIngredient || item == slicedTomatoIngredient
            || item == veggieMixIngredient || item == cheeseSauceIngredient) return 3;
        if (item == baconSlabIngredient || item == cutBaconIngredient || item == cookedBaconIngredient) return 3;
        if (item == onionIngredient || item == onionRingIngredient || item == friedOnionRingIngredient
            || item == eggIngredient || item == cookedEggIngredient) return 4;
        if (item == whippedCreamIngredient) return 2;
        if (item == sprinklesIngredient) return 4;
        return 0;
    }

    static bool RecipeUsesIngredient(AssemblyRecipeDefinition recipe, ItemDefinition item)
    {
        return recipe != null && item != null && (recipe.processedInput == item
            || recipe.pantryInput == item || recipe.rawPantryInput == item
            || recipe.thirdInput == item || recipe.rawThirdInput == item);
    }

    bool IsRecipeUnlocked(AssemblyRecipeDefinition recipe)
    {
        EnsureProductionRecipeGraph();
        if (recipe != null && (IsRemovedIngredient(recipe.output)
            || IsRemovedIngredient(recipe.processedInput)
            || IsRemovedIngredient(recipe.pantryInput)
            || IsRemovedIngredient(recipe.rawPantryInput)
            || IsRemovedIngredient(recipe.thirdInput)
            || IsRemovedIngredient(recipe.rawThirdInput))) return false;
        if (recipe != null && (recipe.output == cheeseBaconLettuceBurgerItem
            || recipe.output == cheeseBaconLettuceOnionBurgerItem
            || recipe.output == cheeseBaconLettuceOnionTomatoBurgerItem
            || recipe.output == cheeseBaconLettuceOnionTomatoEggBurgerItem
            || recipe.output == cltBurgerItem))
            return false;
        if (OnboardingTutorial.BlocksProgression && recipe != null && recipe.Produces(burgerBase))
            return true;
        if (recipe == null) return false;
        if (GetMenuItemUnlockMilestone(recipe.output) > 0)
        {
            return IsMenuItemUnlocked(recipe.output)
                && IsOptionalIngredientUnlocked(recipe.processedInput)
                && IsOptionalIngredientUnlocked(recipe.pantryInput)
                && IsOptionalIngredientUnlocked(recipe.rawPantryInput)
                && IsOptionalIngredientUnlocked(recipe.thirdInput)
                && IsOptionalIngredientUnlocked(recipe.rawThirdInput);
        }
        return MilestoneFeatures.IsResearchComplete(RecipeResearchId(GetRecipeUnlockMilestone(recipe)));
    }

    bool IsOptionalIngredientUnlocked(ItemDefinition item) => item == null || IsIngredientUnlocked(item);

    public bool IsAssemblyRecipeAvailable(AssemblyRecipeDefinition recipe) => IsRecipeUnlocked(recipe);

    public bool IsProcessingRecipeAvailable(StationProcessingRecipeDefinition recipe) =>
        IsProcessingRecipeUnlocked(recipe);

    public bool IsItemEnabled(ItemDefinition item)
    {
        if (item == null || !IsCustomerRecipeUnlocked(item)) return false;
        return (IsBurger(item) || IsFries(item) || IsDrink(item))
            && enabledMenuItems.Contains(item);
    }

    /// <summary>Returns stable asset names for the menu choices selected by the player.</summary>
    public List<string> CaptureEnabledMenuItemNames()
    {
        var names = new List<string>();
        foreach (ItemDefinition item in GetEnabledMenuItems())
        {
            if (item != null && !string.IsNullOrEmpty(item.name))
                names.Add(item.name);
        }
        return names;
    }

    /// <summary>
    /// Restores menu choices from a save. The set is cleared first so loading a
    /// different slot cannot inherit selections from the previous slot.
    /// Locked or removed items are ignored and remain unavailable.
    /// </summary>
    public void RestoreEnabledMenuItems(IEnumerable<string> savedNames)
    {
        enabledMenuItems.Clear();
        if (savedNames == null)
        {
            InvalidateDemandSchedule();
            return;
        }

        var names = new HashSet<string>();
        foreach (string name in savedNames)
            if (!string.IsNullOrEmpty(name)) names.Add(name);

        foreach (ItemDefinition item in GetMenuItems())
        {
            if (item == null || !names.Contains(item.name)) continue;
            if (IsCustomerRecipeUnlocked(item))
                enabledMenuItems.Add(item);
        }
        InvalidateDemandSchedule();
    }

    /// <summary>Clears menu choices when starting a fresh save slot.</summary>
    public void ClearEnabledMenuItems()
    {
        enabledMenuItems.Clear();
        ClearItemReputation();
        InvalidateDemandSchedule();
    }

    /// <summary>
    /// Customer orders are driven by the recipes the player can currently use.
    /// Menu milestone unlocks still gate the product family, while the assembly
    /// recipe gate prevents a customer from ordering a product whose recipe is
    /// still locked in Research.
    /// </summary>
    public bool IsCustomerRecipeUnlocked(ItemDefinition item)
    {
        if (item == null || !IsMenuItemUnlocked(item)) return false;
        if (!IsBurger(item) && !IsFries(item) && !IsDrink(item)) return false;

        // Keep simple/custom configs that do not define assembly recipes usable.
        // The shipped config has recipes for every customer-facing product.
        if (assemblyRecipes == null || assemblyRecipes.Count == 0) return true;
        return GetAssemblyRecipe(item) != null;
    }

    public void SetItemEnabled(ItemDefinition item, bool enabled)
    {
        if (item == null || !(IsBurger(item) || IsFries(item) || IsDrink(item))) return;
        if (enabled && !IsMenuItemUnlocked(item)) return;
        bool changed = enabled
            ? enabledMenuItems.Add(item)
            : enabledMenuItems.Remove(item);
        if (changed) InvalidateDemandSchedule();
    }

    public bool HasEnabledItems
    {
        get
        {
            foreach (ItemDefinition item in GetMenuItems())
                if (IsItemEnabled(item)) return true;
            return false;
        }
    }

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
        EnsureProductionRecipeGraph();
        var yielded = new HashSet<ItemDefinition>();
        if (cookedPattyIngredient != null && IsIngredientUnlocked(cookedPattyIngredient)
            && yielded.Add(cookedPattyIngredient)) yield return cookedPattyIngredient;
        if (grillRecipes != null)
            foreach (StationProcessingRecipeDefinition recipe in grillRecipes)
                if (recipe != null && recipe.output != null && IsProcessingRecipeUnlocked(recipe)
                    && yielded.Add(recipe.output)) yield return recipe.output;
    }

    public StationProcessingRecipeDefinition GetGrillRecipe(ItemDefinition output)
    {
        EnsureProductionRecipeGraph();
        return grillRecipes != null
            ? grillRecipes.Find(r => r != null && r.output == output && IsProcessingRecipeUnlocked(r)) : null;
    }

    public StationProcessingRecipeDefinition GetFryerRecipe(ItemDefinition output)
    {
        EnsureProductionRecipeGraph();
        return fryerRecipes != null
            ? fryerRecipes.Find(r => r != null && r.output == output && IsProcessingRecipeUnlocked(r)) : null;
    }

    bool IsProcessingRecipeUnlocked(StationProcessingRecipeDefinition recipe)
    {
        if (recipe == null) return false;
        if (IsRemovedIngredient(recipe.input) || IsRemovedIngredient(recipe.secondaryInput)
            || IsRemovedIngredient(recipe.output)) return false;
        return IsOptionalIngredientUnlocked(recipe.input)
            && IsOptionalIngredientUnlocked(recipe.secondaryInput)
            && IsOptionalIngredientUnlocked(recipe.output);
    }

    public IEnumerable<ItemDefinition> GetFryerProducts()
    {
        EnsureProductionRecipeGraph();
        var yielded = new HashSet<ItemDefinition>();
        if (cookedPotatoIngredient != null && IsIngredientUnlocked(cookedPotatoIngredient)
            && yielded.Add(cookedPotatoIngredient)) yield return cookedPotatoIngredient;
        if (fryerRecipes != null)
            foreach (StationProcessingRecipeDefinition recipe in fryerRecipes)
                if (recipe != null && recipe.output != null && IsProcessingRecipeUnlocked(recipe)
                    && yielded.Add(recipe.output)) yield return recipe.output;
    }

    /// <summary>Raw ingredients a Freezer may be configured to dispense.</summary>
    public IEnumerable<ItemDefinition> GetFreezerIngredients(bool includeLocked = false)
    {
        EnsureProductionRecipeGraph();
        foreach (RecipeSupplySourceDefinition source in vendorSupplies)
            if (source != null && source.item != null && source.stationType == StationType.Freezer
                && !IsRemovedIngredient(source.item) && !IsDrink(source.item)
                && (includeLocked || IsIngredientUnlocked(source.item)))
                yield return source.item;
    }

    /// <summary>Raw ingredients a Pantry may be configured to dispense.</summary>
    public IEnumerable<ItemDefinition> GetPantryIngredients(bool includeLocked = false)
    {
        EnsureProductionRecipeGraph();
        foreach (RecipeSupplySourceDefinition source in vendorSupplies)
            if (source != null && source.item != null && source.stationType == StationType.Pantry
                && !IsRemovedIngredient(source.item) && !IsDrink(source.item)
                && (includeLocked || IsIngredientUnlocked(source.item)))
                yield return source.item;
    }

    public bool IsFreezerIngredient(ItemDefinition item, bool includeLocked = false)
    {
        if (item == null || IsDrink(item) || IsRemovedIngredient(item)) return false;
        return GetVendorSupplyStation(item) == StationType.Freezer
            && (includeLocked || IsIngredientUnlocked(item));
    }

    /// <summary>Products the assembly station can be set to make (burgers only).</summary>
    public IEnumerable<ItemDefinition> GetAssemblyProducts()
    {
        bool yielded = false;
        foreach (AssemblyRecipeDefinition recipe in GetAssemblyRecipes())
        {
            if (recipe == null || recipe.output == null) continue;
            yielded = true;
            yield return recipe.output;
        }
        if (!yielded && burgerBase != null) yield return burgerBase;
    }

    public IEnumerable<AssemblyRecipeDefinition> GetAssemblyRecipes()
    {
        EnsureProductionRecipeGraph();
        if (assemblyRecipes == null) yield break;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.output != null && IsRecipeUnlocked(recipe))
                yield return recipe;
    }

    public IEnumerable<CuttingRecipeDefinition> GetCuttingRecipes()
    {
        EnsureProductionRecipeGraph();
        if (cuttingRecipes == null) yield break;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.input != null && recipe.output != null
                && IsIngredientUnlocked(recipe.input) && IsIngredientUnlocked(recipe.output))
                yield return recipe;
    }

    public CuttingRecipeDefinition GetCuttingRecipe(ItemDefinition output)
    {
        EnsureProductionRecipeGraph();
        if (output == null || cuttingRecipes == null) return null;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.output == output
                && IsIngredientUnlocked(recipe.input) && IsIngredientUnlocked(recipe.output))
                return recipe;
        return null;
    }

    public CuttingRecipeDefinition GetCuttingRecipeForInput(ItemDefinition input)
    {
        EnsureProductionRecipeGraph();
        if (input == null || cuttingRecipes == null) return null;
        foreach (CuttingRecipeDefinition recipe in cuttingRecipes)
            if (recipe != null && recipe.input == input
                && IsIngredientUnlocked(recipe.input) && IsIngredientUnlocked(recipe.output))
                return recipe;
        return null;
    }

    public AssemblyRecipeDefinition GetAssemblyRecipe(ItemDefinition output)
    {
        EnsureProductionRecipeGraph();
        if (output == null || assemblyRecipes == null) return null;
        foreach (AssemblyRecipeDefinition recipe in assemblyRecipes)
            if (recipe != null && recipe.Produces(output) && IsRecipeUnlocked(recipe))
                return recipe;
        return null;
    }

    public bool IsAssemblyRecipeUnlocked(AssemblyRecipeDefinition recipe) => IsRecipeUnlocked(recipe);

    public ItemDefinition GetAssemblySupplySource(AssemblyRecipeDefinition recipe)
    {
        if (recipe == null) return null;
        if (recipe.rawPantryInput != null && recipe.rawPantryInput != recipe.output)
            return recipe.rawPantryInput;
        RecipeGraphNodeDefinition node = GetProductionRecipe(StationType.Assembly, recipe.output);
        RecipeGraphInputDefinition input = node?.inputs?.Find(value => value != null && value.port == 1);
        return input != null ? FindVendorSourceFor(input.item, new HashSet<ItemDefinition>()) : recipe.pantryInput;
    }

    public bool AssemblySupplyRequiresCutting(AssemblyRecipeDefinition recipe) =>
        recipe != null && HasPreparedSupply(recipe.pantryInput, new HashSet<ItemDefinition>());

    public ItemDefinition GetAssemblyThirdSupplySource(AssemblyRecipeDefinition recipe)
    {
        if (recipe == null) return null;
        if (recipe.rawThirdInput != null && recipe.rawThirdInput != recipe.output)
            return recipe.rawThirdInput;
        return FindVendorSourceFor(recipe.thirdInput, new HashSet<ItemDefinition>());
    }

    public bool AssemblyThirdSupplyRequiresProcessing(AssemblyRecipeDefinition recipe) =>
        recipe != null && HasPreparedSupply(recipe.thirdInput, new HashSet<ItemDefinition>());

    ItemDefinition FindVendorSourceFor(ItemDefinition item, HashSet<ItemDefinition> visited)
    {
        if (item == null || !visited.Add(item)) return null;
        RecipeSupplySourceDefinition source = FindVendorSupply(item);
        if (source != null) return item;
        RecipeGraphNodeDefinition producer = productionRecipes != null
            ? productionRecipes.Find(recipe => recipe != null && recipe.output == item) : null;
        if (producer?.inputs == null) return null;
        foreach (RecipeGraphInputDefinition input in producer.inputs)
        {
            ItemDefinition found = FindVendorSourceFor(input?.item, visited);
            if (found != null) return found;
        }
        return null;
    }

    bool HasPreparedSupply(ItemDefinition item, HashSet<ItemDefinition> visited)
    {
        if (item == null || !visited.Add(item) || FindVendorSupply(item) != null) return false;
        RecipeGraphNodeDefinition producer = productionRecipes != null
            ? productionRecipes.Find(recipe => recipe != null && recipe.output == item) : null;
        if (producer == null) return false;
        foreach (RecipeGraphInputDefinition input in producer.inputs)
            if (input != null && HasPreparedSupply(input.item, visited)) return true;
        return true;
    }

    /// <summary>Assembly outputs in production order, including intermediate recipes.</summary>
    public List<ItemDefinition> GetAssemblyChain(ItemDefinition finalProduct)
    {
        EnsureProductionRecipeGraph();
        var chain = new List<ItemDefinition>();
        var visited = new HashSet<ItemDefinition>();
        AppendAssemblyChain(finalProduct, chain, visited);
        return chain;
    }

    void AppendAssemblyChain(ItemDefinition output, List<ItemDefinition> chain, HashSet<ItemDefinition> visited)
    {
        if (output == null || !visited.Add(output)) return;
        if (GetAssemblyRecipe(output) == null) return;
        RecipeGraphNodeDefinition recipe = GetProductionRecipe(StationType.Assembly, output);
        if (recipe == null) return;
        if (recipe.inputs != null)
            foreach (RecipeGraphInputDefinition input in recipe.inputs)
                if (input?.item != null && GetAssemblyRecipe(input.item) != null)
                    AppendAssemblyChain(input.item, chain, visited);
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
        bool wantFries = PickEnabledProduct(ProductKind.Fries) != null && Random.value < friesChance;
        bool wantDrink = PickEnabledProduct(ProductKind.Drink) != null && Random.value < drinkChance;

        if (!wantBurger && !wantFries && !wantDrink)
        {
            // Every order needs at least one item. Pick uniformly from the
            // currently available menu so this fallback does not favor burgers.
            var available = new List<ProductKind>();
            if (PickEnabledBurger() != null) available.Add(ProductKind.Burger);
            if (PickEnabledProduct(ProductKind.Fries) != null) available.Add(ProductKind.Fries);
            if (PickEnabledProduct(ProductKind.Drink) != null) available.Add(ProductKind.Drink);

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
            order.lines.Add(new CustomerOrder.OrderLine(orderedBurger, 1));
        if (wantFries)
            order.lines.Add(new CustomerOrder.OrderLine(PickEnabledProduct(ProductKind.Fries), 1));
        if (wantDrink)
            order.lines.Add(new CustomerOrder.OrderLine(PickEnabledProduct(ProductKind.Drink), 1));

        return order;
    }

    ItemDefinition PickEnabledProduct(ProductKind kind)
    {
        var available = new List<ItemDefinition>();
        foreach (ItemDefinition item in GetMenuItems())
            if (GetProductKind(item) == kind && IsItemEnabled(item)) available.Add(item);
        return available.Count > 0 ? available[Random.Range(0, available.Count)] : null;
    }

    /// <summary>One random heat-lamp item (burger or fries — drinks are cashier-served).</summary>
    public CustomerOrder GenerateRandomSingleItemOrder()
    {
        var options = new List<ItemDefinition>();
        foreach (ItemDefinition item in GetMenuItems())
            if (IsItemEnabled(item)) options.Add(item);
        if (options.Count == 0) return new CustomerOrder();

        return CustomerOrder.FromItem(options[Random.Range(0, options.Count)], 1);
    }

    public IEnumerable<ItemDefinition> GetMenuItems()
    {
        if (burgerBase != null) yield return burgerBase;
        if (cheeseburgerItem != null) yield return cheeseburgerItem;
        if (clBurgerItem != null) yield return clBurgerItem;
        if (cheeseBaconBurgerItem != null) yield return cheeseBaconBurgerItem;
        if (superBurgerItem != null) yield return superBurgerItem;
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

    public IEnumerable<ItemDefinition> GetIngredientItems(bool includeLocked = false)
    {
        var yielded = new HashSet<ItemDefinition>();
        foreach (ItemDefinition freezerItem in GetFreezerIngredients(includeLocked))
            if (freezerItem != null && !IsDrink(freezerItem) && yielded.Add(freezerItem)) yield return freezerItem;
        foreach (ItemDefinition pantryItem in GetPantryIngredients(includeLocked))
            if (pantryItem != null && !IsDrink(pantryItem) && yielded.Add(pantryItem)) yield return pantryItem;
    }
}

[System.Serializable]
public class CuttingRecipeDefinition
{
    public string recipeName = "Sliced Ingredient";
    public ItemDefinition input;
    public ItemDefinition output;
    [Tooltip("Seconds for one item at 100% on an MK1 station. MK2 stations run this recipe twice as fast.")]
    [Min(0.01f)] public float baseCycleSeconds = 4f;

    public string DisplayName => !string.IsNullOrWhiteSpace(recipeName)
        ? recipeName
        : (output != null && !string.IsNullOrWhiteSpace(output.itemName)
            ? output.itemName : "Cutting Recipe");
}
