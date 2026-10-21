using System.Collections.Generic;
using UnityEngine;

/// <summary>Processes raw burger toppings into slices ready for assembly.</summary>
public partial class CuttingStation : MonoBehaviour, IStationBuffer
{
    [Header("Recipe")]
    [Tooltip("Output selected for this station. The recipe defines its matching raw input.")]
    public ItemDefinition selectedProduct;
    [Tooltip("Time to process one item at 100% rate.")]
    [Min(0f)] public float processTimeSeconds = 6f;
    [SerializeField, Range(1, 2)] int stationMark = 1;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int inputUnits;
    [SerializeField, Min(0)] int outputUnits;
    [SerializeField, Min(0)] int processingUnits;
    [SerializeField, Min(0f)] float processingTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    readonly List<Transform> slotMarkers = new List<Transform>();
    int displayedInput = -1;
    int displayedOutput = -1;
    int displayedProcessing = -1;
    ItemDefinition displayedRecipeOutput;
    StationProcessProgressIndicator processProgressIndicator;

    public bool HasRecipeSelected => GetSelectedRecipe() != null;
    public bool IsProcessing => processingUnits > 0;
    public float BaseProcessCycleSeconds
    {
        get
        {
            CuttingRecipeDefinition recipe = GetSelectedRecipe();
            return recipe != null
                ? StationNode.GetTierAdjustedCycleSeconds(this, recipe.baseCycleSeconds)
                : processTimeSeconds;
        }
    }
    public float EffectiveProcessTimeSeconds => StationNode.GetEffectiveCycleSeconds(this, BaseProcessCycleSeconds);
    public float ProcessRemainingSeconds => IsProcessing
        ? Mathf.Max(0f, EffectiveProcessTimeSeconds - processingTimer) : 0f;
    public int StationMark => Mathf.Clamp(stationMark, 1, 2);
    public int InputSlotCapacity => StationBufferLimits.PerItem;
    public int OutputSlotCapacity => StationBufferLimits.PerItem;
    public int BufferedUnitCount => inputUnits + outputUnits + processingUnits;
    public int AuthoredInputSlotCount { get { EnsureBufferMarkers(); return inputMarkers.Count; } }
    public int AuthoredOutputSlotCount { get { EnsureBufferMarkers(); return outputMarkers.Count; } }
    public int VisibleSlotCount { get { EnsureBufferMarkers(); return slotMarkers.Count; } }

    void OnEnable()
    {
        processProgressIndicator = StationProcessProgressIndicator.Ensure(gameObject);
        StationConfigurationCaution.Ensure(gameObject);
        FindBufferMarkers();
        outputUnits = Mathf.Clamp(outputUnits, 0, OutputSlotCapacity);
        processingUnits = Mathf.Clamp(processingUnits, 0, 1);
        inputUnits = Mathf.Clamp(inputUnits, 0, InputSlotCapacity - processingUnits);
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
            processingUnits = 0;
            processingTimer = 0f;
            bufferedOrder = null;
        }
        selectedProduct = recipe != null ? recipe.output : null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(true);
    }

    /// <summary>
    /// Restores the authored selection without applying current tutorial or
    /// milestone filters. Unlock state is restored later in the load sequence.
    /// </summary>
    public void RestoreSelectedProduct(ItemDefinition product)
    {
        selectedProduct = product;
        inputUnits = 0;
        outputUnits = 0;
        processingUnits = 0;
        processingTimer = 0f;
        bufferedOrder = null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(true);
    }

    public int GetInputCount(ItemDefinition item)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && item == recipe.input ? inputUnits : 0;
    }

    public int GetHeldInputCount(ItemDefinition item)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && item == recipe.input ? inputUnits + processingUnits : 0;
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
        if (BufferedUnitCount == 0) bufferedOrder = null;
        RefreshItemDisplay(true);
        return taken;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        BufferedUnitCount == 0 || bufferedOrder == null || order == null || bufferedOrder == order;

    public int ProcessBuffered(int amount)
    {
        // One call represents one item cycle, regardless of older batch callers.
        if (outputUnits >= OutputSlotCapacity) return 0;
        int processed = Mathf.Min(1, inputUnits);
        if (processed <= 0) return 0;
        inputUnits -= processed;
        outputUnits += processed;
        StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(GetSelectedRecipe()?.output, processed);
        RefreshItemDisplay(true);
        return processed;
    }

    void AdvanceAutomaticProcessing(float deltaTime)
    {
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return;

        float remainingTime = Mathf.Max(0f, deltaTime);
        bool changed = false;
        while (true)
        {
            if (outputUnits >= OutputSlotCapacity) break;
            if (processingUnits <= 0)
            {
                if (inputUnits <= 0) break;
                inputUnits--;
                processingUnits = 1;
                processingTimer = 0f;
                changed = true;
            }

            float cycleSeconds = EffectiveProcessTimeSeconds;
            if (cycleSeconds <= 0.001f)
            {
                outputUnits++;
                processingUnits--;
                StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(recipe.output, 1);
                changed = true;
                continue;
            }

            float untilOutput = Mathf.Max(0f, cycleSeconds - processingTimer);
            float elapsed = Mathf.Min(remainingTime, untilOutput);
            processingTimer += elapsed;
            remainingTime -= elapsed;
            if (processingTimer + 0.0001f < cycleSeconds) break;
            outputUnits++;
            processingUnits = Mathf.Max(0, processingUnits - 1);
            processingTimer = Mathf.Max(0f, processingTimer - cycleSeconds);
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(recipe.output, 1);
            changed = true;
            if (processingUnits <= 0) processingTimer = 0f;
        }

        if (changed) RefreshItemDisplay(true);
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
        amount = 1;
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
        amount = 1;
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
        if (!ProductionManager.IsScheduledStation(gameObject)) AdvanceAutomaticProcessing(Time.deltaTime);
        processProgressIndicator?.SetProgress(
            IsProcessing, processingTimer / Mathf.Max(0.01f, EffectiveProcessTimeSeconds));
        RefreshItemDisplay(false);
    }

    void FindBufferMarkers()
    {
        StationBufferLayout.FindMarkers(transform, inputMarkers, outputMarkers);
        slotMarkers.Clear();
        slotMarkers.AddRange(inputMarkers);
        slotMarkers.AddRange(outputMarkers);
        slotMarkers.Sort((a, b) => string.Compare(a != null ? a.name : string.Empty,
            b != null ? b.name : string.Empty, System.StringComparison.OrdinalIgnoreCase));
    }

    void EnsureBufferMarkers()
    {
        // Shared-slot prefabs can author only Input* markers. No separate
        // Output* markers are needed for finished items.
        if (slotMarkers.Count == 0) FindBufferMarkers();
    }

    void RefreshItemDisplay(bool force)
    {
        if (!force && displayedInput == inputUnits && displayedOutput == outputUnits
            && displayedProcessing == processingUnits
            && displayedRecipeOutput == selectedProduct) return;
        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "CuttingItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        displayedInput = inputUnits;
        displayedOutput = outputUnits;
        displayedProcessing = processingUnits;
        displayedRecipeOutput = selectedProduct;
        CuttingRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return;

        GameObject inputPrefab = recipe.input != null ? recipe.input.prefab : null;
        GameObject outputPrefab = recipe.output != null ? recipe.output.prefab : null;
        int slot = 0;
        int rawUnits = inputUnits + processingUnits;
        for (int i = 0; inputPrefab != null && i < rawUnits && slot < slotMarkers.Count; i++, slot++)
            StationItemVisualUtility.SpawnAtMarker(inputPrefab, slotMarkers[slot], itemDisplayRoot,
                "CuttingInput_" + i);
        for (int i = 0; outputPrefab != null && i < outputUnits && slot < slotMarkers.Count; i++, slot++)
            StationItemVisualUtility.SpawnAtMarker(outputPrefab, slotMarkers[slot], itemDisplayRoot,
                "CuttingOutput_" + i);
    }
}
