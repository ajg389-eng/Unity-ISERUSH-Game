using System.Collections.Generic;
using UnityEngine;

/// <summary>Processes raw burger toppings into slices ready for assembly.</summary>
public class CuttingStation : MonoBehaviour, IStationBuffer
{
    const int DefaultBufferCapacity = 2;
    [Header("Recipe")]
    [Tooltip("Output selected for this station. The recipe defines its matching raw input.")]
    public ItemDefinition selectedProduct;
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int inputUnits;
    [SerializeField, Min(0)] int outputUnits;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    int displayedInput = -1;
    int displayedOutput = -1;
    ItemDefinition displayedRecipeOutput;

    public bool HasRecipeSelected => GetSelectedRecipe() != null;
    public int InputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, inputMarkers.Count); } }
    public int OutputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, outputMarkers.Count); } }

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
        FindBufferMarkers();
        inputUnits = Mathf.Clamp(inputUnits, 0, InputSlotCapacity);
        outputUnits = Mathf.Clamp(outputUnits, 0, OutputSlotCapacity);
        RefreshItemDisplay(true);
    }

    public CuttingRecipeDefinition GetSelectedRecipe()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config != null ? config.GetCuttingRecipe(selectedProduct) : null;
    }

    public void SetRecipe(CuttingRecipeDefinition recipe)
    {
        if (selectedProduct != (recipe != null ? recipe.output : null))
        {
            inputUnits = 0;
            outputUnits = 0;
            bufferedOrder = null;
        }
        selectedProduct = recipe != null ? recipe.output : null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(true);
    }

    public int GetInputCount(ItemDefinition item)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && item == recipe.input ? inputUnits : 0;
    }

    public int GetOutputCount(ItemDefinition item)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && item == recipe.output ? outputUnits : 0;
    }

    public bool CanAcceptInput(ItemDefinition item, int amount)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && item == recipe.input && amount > 0
            && inputUnits + amount <= InputSlotCapacity;
    }

    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        inputUnits += amount;
        bufferedOrder = sourceOrder;
        RefreshItemDisplay(true);
        return amount;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item != recipe.output || amount <= 0) return 0;
        int taken = Mathf.Min(amount, outputUnits);
        outputUnits -= taken;
        if (inputUnits == 0 && outputUnits == 0) bufferedOrder = null;
        RefreshItemDisplay(true);
        return taken;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        inputUnits + outputUnits == 0 || bufferedOrder == null || order == null || bufferedOrder == order;

    public int ProcessBuffered(int amount)
    {
        amount = Mathf.Max(1, amount);
        int processed = Mathf.Min(amount, inputUnits, OutputSlotCapacity - outputUnits);
        if (processed <= 0) return 0;
        inputUnits -= processed;
        outputUnits += processed;
        RefreshItemDisplay(true);
        return processed;
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

    void Update()
    {
        RefreshItemDisplay(false);
    }

    void FindBufferMarkers()
    {
        StationBufferLayout.FindMarkers(transform, inputMarkers, outputMarkers);
    }

    void EnsureBufferMarkers()
    {
        if (inputMarkers.Count == 0 && outputMarkers.Count == 0) FindBufferMarkers();
    }

    void RefreshItemDisplay(bool force)
    {
        if (!force && displayedInput == inputUnits && displayedOutput == outputUnits
            && displayedRecipeOutput == selectedProduct) return;
        if (inputMarkers.Count == 0 && outputMarkers.Count == 0) FindBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "CuttingItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        displayedInput = inputUnits;
        displayedOutput = outputUnits;
        displayedRecipeOutput = selectedProduct;
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return;

        GameObject inputPrefab = recipe.input != null ? recipe.input.prefab : null;
        GameObject outputPrefab = recipe.output != null ? recipe.output.prefab : null;
        for (int i = 0; inputPrefab != null && i < Mathf.Min(inputUnits, inputMarkers.Count); i++)
            StationItemVisualUtility.SpawnAtMarker(inputPrefab, inputMarkers[i], itemDisplayRoot,
                "CuttingInput_" + i);
        for (int i = 0; outputPrefab != null && i < Mathf.Min(outputUnits, outputMarkers.Count); i++)
            StationItemVisualUtility.SpawnAtMarker(outputPrefab, outputMarkers[i], itemDisplayRoot,
                "CuttingOutput_" + i);
    }
}
