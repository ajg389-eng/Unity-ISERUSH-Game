using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Recipe-driven assembly station. Recipes may create final menu items or intermediates
/// that feed a later assembly station.
/// </summary>
public partial class AssemblyStation : MonoBehaviour, IStationBuffer
{
    const int Mk1InputCapacityPerType = 4;
    const int Mk1OutputCapacity = 4;
    const int Mk2InputCapacityPerType = 8;
    const int Mk2OutputCapacity = 8;

    [Header("Product")]
    [Tooltip("Recipe output this station assembles. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;
    [System.NonSerialized] AssemblyRecipeDefinition selectedRecipe;

    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Time to produce one item at 100% rate.")]
    [Min(0f)] public float processTimeSeconds = 4f;
    [SerializeField, Range(1, 2)] int stationMark = 1;
    public Vector3 interactionOffset = Vector3.zero;
    [SerializeField, Min(0)] int bufferedProcessedInputs;
    [SerializeField, Min(0)] int bufferedPantryInputs;
    [SerializeField, Min(0)] int bufferedThirdInputs;
    [SerializeField, Min(0)] int bufferedOutputs;
    [SerializeField, Min(0)] int processingUnits;
    [SerializeField, Min(0f)] float processingTimer;

    [Header("Table display")]
    [Tooltip("Fallback model for a cooked patty when the recipe input has no ItemDefinition prefab.")]
    public GameObject defaultProcessedInputDisplayPrefab;
    [Tooltip("Fallback model for an output whose ItemDefinition has no prefab.")]
    public GameObject defaultOutputDisplayPrefab;
    [Tooltip("Authored tabletop markers for the processed ingredient.")]
    public Transform[] processedInputDisplaySlots = new Transform[4];
    [Tooltip("Authored tabletop markers for the pantry ingredient.")]
    public Transform[] pantryInputDisplaySlots = new Transform[4];
    public Transform[] thirdInputDisplaySlots = new Transform[4];
    [Tooltip("Authored tabletop markers for completed outputs.")]
    public Transform[] outputDisplaySlots = new Transform[4];

    Transform tableDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    bool layoutReady;
    StationProcessProgressIndicator processProgressIndicator;

    public int BufferedProcessedInputCount => bufferedProcessedInputs;
    public int BufferedPantryInputCount => bufferedPantryInputs;
    public int BufferedThirdInputCount => bufferedThirdInputs;
    public bool IsMk2 => stationMark >= 2;
    public int BufferedOutputCount => bufferedOutputs;
    public bool IsProcessing => processingUnits > 0;
    public int ProcessingUnitCount => processingUnits;
    public float ProcessingProgressSeconds => processingTimer;
    public float BaseProcessCycleSeconds
    {
        get
        {
            AssemblyRecipeDefinition recipe = GetSelectedRecipe();
            return recipe != null
                ? StationNode.GetTierAdjustedCycleSeconds(this, recipe.baseCycleSeconds)
                : processTimeSeconds;
        }
    }
    public float EffectiveProcessTimeSeconds => StationNode.GetEffectiveCycleSeconds(this, BaseProcessCycleSeconds);
    public float ProcessRemainingSeconds => IsProcessing
        ? Mathf.Max(0f, EffectiveProcessTimeSeconds - processingTimer) : 0f;
    public int InputSlotCapacity => IsMk2
        ? Mk2InputCapacityPerType * 3
        : Mk1InputCapacityPerType * 2;
    public int OutputSlotCapacity => IsMk2 ? Mk2OutputCapacity : Mk1OutputCapacity;
    public int ProcessedInputCapacity => IsMk2
        ? Mk2InputCapacityPerType : Mk1InputCapacityPerType;
    public int PantryInputCapacity => IsMk2
        ? Mk2InputCapacityPerType : Mk1InputCapacityPerType;
    public int ThirdInputCapacity => IsMk2 ? Mk2InputCapacityPerType : 0;
    /// <summary>Per-ingredient capacity retained for UI and legacy callers.</summary>
    public int IngredientCapacity => Mathf.Max(ProcessedInputCapacity,
        Mathf.Max(PantryInputCapacity, ThirdInputCapacity));
    public int MaxProcessBatch
    {
        get
        {
            AssemblyRecipeDefinition recipe = GetSelectedRecipe();
            if (recipe == null) return 0;
            int processedLimit = recipe.RequiresProcessedInput
                ? ProcessedInputCapacity / Mathf.Max(1, recipe.processedInputAmount) : int.MaxValue;
            int pantryLimit = recipe.pantryInput != null
                ? PantryInputCapacity / Mathf.Max(1, recipe.pantryInputAmount) : int.MaxValue;
            int thirdLimit = recipe.thirdInput != null
                ? ThirdInputCapacity / Mathf.Max(1, recipe.thirdInputAmount) : int.MaxValue;
            return Mathf.Max(0, Mathf.Min(OutputSlotCapacity,
                processedLimit, pantryLimit, thirdLimit));
        }
    }

    public int GetInputCount(ItemDefinition item)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null) return 0;
        if (item == recipe.pantryInput) return bufferedPantryInputs;
        if (item == recipe.thirdInput) return bufferedThirdInputs;
        if (recipe.RequiresProcessedInput
            && (recipe.processedInput == null || item == recipe.processedInput))
            return bufferedProcessedInputs;
        return 0;
    }

    public int GetOutputCount(ItemDefinition item)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return recipe != null && recipe.Produces(item) ? bufferedOutputs : 0;
    }

    public bool CanAcceptInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || amount <= 0) return false;
        if (item == recipe.pantryInput)
            return bufferedPantryInputs + amount <= PantryInputCapacity;
        if (item == recipe.thirdInput)
            return IsMk2 && bufferedThirdInputs + amount <= ThirdInputCapacity;
        return recipe.RequiresProcessedInput
            && (recipe.processedInput == null || item == recipe.processedInput)
            && bufferedProcessedInputs + amount <= ProcessedInputCapacity;
    }

    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (!CanAcceptInput(item, amount) || recipe == null) return 0;
        if (item == recipe.pantryInput)
            return ReceivePantryInput(item, amount);
        if (item == recipe.thirdInput)
            return ReceiveThirdInput(item, amount);
        return ReceiveProcessedInput(item, amount);
    }

    public bool HasProductSelected => GetSelectedRecipe() != null;

    public AssemblyRecipeDefinition GetSelectedRecipe()
    {
        var manager = ProductionManager.Instance;
        var config = manager != null ? manager.orderConfig : null;
        if (selectedRecipe != null)
        {
            if (config != null && !config.IsAssemblyRecipeUnlocked(selectedRecipe))
                selectedRecipe = null;
        }
        if (selectedRecipe != null)
        {
            if (selectedProduct != selectedRecipe.output)
                selectedProduct = selectedRecipe.output;
            return selectedRecipe;
        }

        selectedRecipe = config != null ? config.GetAssemblyRecipe(selectedProduct) : null;
        if (selectedRecipe != null)
            selectedProduct = selectedRecipe.output;
        return selectedRecipe;
    }

    public void SetRecipe(AssemblyRecipeDefinition recipe)
    {
        if (selectedRecipe != recipe)
        {
            bufferedProcessedInputs = 0;
            bufferedPantryInputs = 0;
            bufferedThirdInputs = 0;
            bufferedOutputs = 0;
            processingUnits = 0;
            processingTimer = 0f;
        }
        selectedRecipe = recipe;
        selectedProduct = recipe != null ? recipe.output : null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshTableDisplay();
    }

    public bool CanProcess(ItemDefinition product)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        return product != null && recipe != null && recipe.Produces(product)
            && (!recipe.RequiresMk2 || IsMk2);
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
        int accepted = Mathf.Min(amount, Mathf.Max(0, PantryInputCapacity - bufferedPantryInputs));
        bufferedPantryInputs += accepted;
        if (accepted > 0)
        {
            RefreshTableDisplay();
            AdvanceAutomaticProcessing(0f);
        }
        return accepted;
    }

    public int ReceiveThirdInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (!IsMk2 || recipe == null || item == null || item != recipe.thirdInput || amount <= 0) return 0;
        int accepted = Mathf.Min(amount, Mathf.Max(0, ThirdInputCapacity - bufferedThirdInputs));
        bufferedThirdInputs += accepted;
        if (accepted > 0)
        {
            RefreshTableDisplay();
            AdvanceAutomaticProcessing(0f);
        }
        return accepted;
    }

    public bool CanReceiveProcessedInput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || !recipe.RequiresProcessedInput || amount <= 0) return false;
        if (recipe.processedInput != null && item != recipe.processedInput) return false;
        return bufferedProcessedInputs + amount <= ProcessedInputCapacity;
    }

    public int ReceiveProcessedInput(ItemDefinition item, int amount)
    {
        if (!CanReceiveProcessedInput(item, amount)) return 0;
        bufferedProcessedInputs += amount;
        RefreshTableDisplay();
        AdvanceAutomaticProcessing(0f);
        return amount;
    }

    public bool CanStoreOutput(int amount)
    {
        return amount > 0 && bufferedOutputs + amount <= OutputSlotCapacity;
    }

    public int StoreOutput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || !recipe.Produces(item) || amount <= 0)
            return 0;
        int stored = Mathf.Min(amount, Mathf.Max(0, OutputSlotCapacity - bufferedOutputs));
        bufferedOutputs += stored;
        if (stored > 0) RefreshTableDisplay();
        return stored;
    }

    public bool HasRequiredInputs(ItemDefinition product, IReadOnlyList<ItemDefinition> pantryMaterials, int units)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || !recipe.Produces(product) || units <= 0)
            return false;

        int required = recipe.pantryInput != null
            ? units * Mathf.Max(1, recipe.pantryInputAmount) : 0;
        int present = 0;
        if (recipe.pantryInput != null && pantryMaterials != null)
        {
            for (int i = 0; i < pantryMaterials.Count; i++)
                if (pantryMaterials[i] == recipe.pantryInput)
                    present++;
        }
        int processedRequired = recipe.RequiresProcessedInput
            ? units * Mathf.Max(1, recipe.processedInputAmount) : 0;
        int thirdRequired = recipe.thirdInput != null ? units * Mathf.Max(1, recipe.thirdInputAmount) : 0;
        return (!recipe.RequiresMk2 || IsMk2)
            && (!recipe.RequiresProcessedInput || bufferedProcessedInputs >= processedRequired)
            && present + bufferedPantryInputs >= required
            && bufferedThirdInputs >= thirdRequired
            && CanStoreOutput(units);
    }

    public bool TryAssemble(ItemDefinition product, List<ItemDefinition> pantryMaterials, int units)
    {
        // The timed worker operation is one craft. Keep the old parameter so
        // serialized jobs and existing callers remain compatible.
        units = 1;
        if (!HasRequiredInputs(product, pantryMaterials, units)) return false;
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        int processedConsume = recipe.RequiresProcessedInput
            ? units * Mathf.Max(1, recipe.processedInputAmount) : 0;
        int consume = recipe.pantryInput != null
            ? units * Mathf.Max(1, recipe.pantryInputAmount) : 0;
        for (int i = pantryMaterials != null ? pantryMaterials.Count - 1 : -1;
            i >= 0 && consume > 0; i--)
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
        if (consume != 0) return false;

        bufferedProcessedInputs = Mathf.Max(0, bufferedProcessedInputs - processedConsume);
        if (recipe.thirdInput != null)
            bufferedThirdInputs = Mathf.Max(0, bufferedThirdInputs - units * Mathf.Max(1, recipe.thirdInputAmount));
        bufferedOutputs = Mathf.Min(OutputSlotCapacity, bufferedOutputs + units);
        RefreshTableDisplay();
        return true;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || item == null || !recipe.Produces(item) || amount <= 0) return 0;
        int taken = Mathf.Min(amount, bufferedOutputs);
        bufferedOutputs -= taken;
        if (taken > 0) RefreshTableDisplay();
        return taken;
    }

    public void RestoreBufferedState(ItemDefinition product, int processedInputs, int pantryInputs, int outputs)
    {
        RestoreBufferedState(product, processedInputs, pantryInputs, 0, outputs);
    }

    public void RestoreBufferedState(ItemDefinition product, int processedInputs, int pantryInputs,
        int thirdInputs, int outputs, int activeProcessingUnits = 0, float activeProcessingProgress = 0f)
    {
        selectedProduct = product;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        selectedRecipe = config != null ? config.GetAssemblyRecipe(product) : null;
        bufferedProcessedInputs = Mathf.Clamp(processedInputs, 0, ProcessedInputCapacity);
        bufferedPantryInputs = Mathf.Clamp(pantryInputs, 0, PantryInputCapacity);
        bufferedThirdInputs = Mathf.Clamp(thirdInputs, 0, ThirdInputCapacity);
        bufferedOutputs = Mathf.Clamp(outputs, 0, OutputSlotCapacity);
        processingUnits = Mathf.Clamp(activeProcessingUnits, 0, MaxProcessBatch);
        processingTimer = processingUnits > 0
            ? Mathf.Clamp(activeProcessingProgress, 0f, Mathf.Max(0f, EffectiveProcessTimeSeconds)) : 0f;
        RefreshTableDisplay();
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    void OnEnable()
    {
        processProgressIndicator = StationProcessProgressIndicator.Ensure(gameObject);
        StationConfigurationCaution.Ensure(gameObject);
        EnsureBufferLayout(true);
        bufferedProcessedInputs = Mathf.Clamp(bufferedProcessedInputs, 0, ProcessedInputCapacity);
        bufferedPantryInputs = Mathf.Clamp(bufferedPantryInputs, 0, PantryInputCapacity);
        bufferedThirdInputs = Mathf.Clamp(bufferedThirdInputs, 0, ThirdInputCapacity);
        bufferedOutputs = Mathf.Clamp(bufferedOutputs, 0, OutputSlotCapacity);
        HideSlotMarkers();
        RefreshTableDisplay();
    }

    void Update()
    {
        if (!ProductionManager.IsScheduledStation(gameObject)) AdvanceAutomaticProcessing(Time.deltaTime);
        processProgressIndicator?.SetProgress(
            IsProcessing, processingTimer / Mathf.Max(0.01f, EffectiveProcessTimeSeconds));
    }

    void AdvanceAutomaticProcessing(float deltaTime)
    {
        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null || (recipe.RequiresMk2 && !IsMk2)) return;
        int processedPerUnit = recipe.RequiresProcessedInput
            ? Mathf.Max(1, recipe.processedInputAmount) : 0;
        int pantryPerUnit = recipe.pantryInput != null
            ? Mathf.Max(1, recipe.pantryInputAmount) : 0;
        int thirdPerUnit = recipe.thirdInput != null
            ? Mathf.Max(1, recipe.thirdInputAmount) : 0;
        float remainingTime = Mathf.Max(0f, deltaTime);
        bool changed = false;

        while (true)
        {
            if (processingUnits <= 0)
            {
                if (bufferedOutputs >= OutputSlotCapacity
                    || MaxProcessBatch <= 0
                    || (processedPerUnit > 0 && bufferedProcessedInputs < processedPerUnit)
                    || (pantryPerUnit > 0 && bufferedPantryInputs < pantryPerUnit)
                    || (thirdPerUnit > 0 && bufferedThirdInputs < thirdPerUnit))
                    break;

                if (processedPerUnit > 0) bufferedProcessedInputs -= processedPerUnit;
                if (pantryPerUnit > 0) bufferedPantryInputs -= pantryPerUnit;
                if (thirdPerUnit > 0) bufferedThirdInputs -= thirdPerUnit;
                processingUnits = 1;
                processingTimer = 0f;
                changed = true;
            }

            float cycleSeconds = EffectiveProcessTimeSeconds;
            if (cycleSeconds <= 0.001f)
            {
                bufferedOutputs++;
                processingUnits--;
                StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(1);
                changed = true;
                if (bufferedOutputs >= OutputSlotCapacity) break;
                continue;
            }

            float untilOutput = Mathf.Max(0f, cycleSeconds - processingTimer);
            float elapsed = Mathf.Min(remainingTime, untilOutput);
            processingTimer += elapsed;
            remainingTime -= elapsed;
            if (processingTimer + 0.0001f < cycleSeconds) break;
            if (bufferedOutputs >= OutputSlotCapacity) break;

            bufferedOutputs++;
            processingUnits = Mathf.Max(0, processingUnits - 1);
            processingTimer = Mathf.Max(0f, processingTimer - cycleSeconds);
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(1);
            changed = true;
            if (processingUnits <= 0) processingTimer = 0f;
        }

        if (changed) RefreshTableDisplay();
    }

    void RefreshTableDisplay()
    {
        EnsureTableDisplayRoot();
        ClearTableDisplay();

        AssemblyRecipeDefinition recipe = GetSelectedRecipe();
        if (recipe == null) return;

        GameObject processedPrefab = recipe.processedInput != null && recipe.processedInput.prefab != null
            ? recipe.processedInput.prefab : defaultProcessedInputDisplayPrefab;
        GameObject pantryPrefab = recipe.pantryInput != null ? recipe.pantryInput.prefab : null;
        GameObject thirdPrefab = recipe.thirdInput != null ? recipe.thirdInput.prefab : null;
        GameObject outputPrefab = recipe.output != null && recipe.output.prefab != null
            ? recipe.output.prefab : defaultOutputDisplayPrefab;

        SpawnDisplayedItems(processedPrefab, Mathf.Min(bufferedProcessedInputs, ProcessedInputCapacity),
            processedInputDisplaySlots, "ProcessedInput");
        SpawnDisplayedItems(pantryPrefab, Mathf.Min(bufferedPantryInputs, PantryInputCapacity),
            pantryInputDisplaySlots, "PantryInput");
        SpawnDisplayedItems(thirdPrefab, Mathf.Min(bufferedThirdInputs, ThirdInputCapacity),
            thirdInputDisplaySlots, "ThirdInput");
        SpawnDisplayedItems(outputPrefab, Mathf.Min(bufferedOutputs, OutputSlotCapacity),
            outputDisplaySlots, "Output");
    }

    void EnsureBufferLayout(bool force = false)
    {
        if (layoutReady && !force) return;
        StationBufferLayout.FindMarkers(transform, inputMarkers, outputMarkers);
        if (inputMarkers.Count > 0)
        {
            var processed = new List<Transform>();
            var pantry = new List<Transform>();
            var third = new List<Transform>();
            for (int i = 0; i < inputMarkers.Count; i++)
            {
                Transform marker = inputMarkers[i];
                string markerName = marker.name.ToLowerInvariant();
                if (markerName.Contains("input3")) third.Add(marker);
                else if (markerName.Contains("input2")) pantry.Add(marker);
                else if (markerName.Contains("input1")) processed.Add(marker);
                else if ((i & 1) == 0) processed.Add(marker);
                else pantry.Add(marker);
            }
            processedInputDisplaySlots = processed.ToArray();
            pantryInputDisplaySlots = pantry.ToArray();
            thirdInputDisplaySlots = third.ToArray();
        }
        if (outputMarkers.Count > 0)
            outputDisplaySlots = outputMarkers.ToArray();
        layoutReady = true;
    }

    void EnsureTableDisplayRoot()
    {
        if (tableDisplayRoot != null) return;
        Transform existing = transform.Find("AssemblyTableDisplay");
        if (existing != null)
        {
            tableDisplayRoot = existing;
            return;
        }

        tableDisplayRoot = new GameObject("AssemblyTableDisplay").transform;
        tableDisplayRoot.SetParent(transform, false);
    }

    void ClearTableDisplay()
    {
        if (tableDisplayRoot == null) return;
        for (int i = tableDisplayRoot.childCount - 1; i >= 0; i--)
            Destroy(tableDisplayRoot.GetChild(i).gameObject);
    }

    void SpawnDisplayedItems(GameObject prefab, int count, Transform[] slots, string label)
    {
        if (prefab == null || count <= 0 || slots == null) return;

        int visible = Mathf.Min(count, slots.Length);
        for (int i = 0; i < visible; i++)
        {
            Transform slot = slots[i];
            if (slot == null) continue;

            GameObject display = Instantiate(prefab, tableDisplayRoot);
            display.name = label + "_" + (i + 1) + "_" + prefab.name;
            Vector3 sourceScale = prefab.transform.localScale;
            display.transform.localPosition = transform.InverseTransformPoint(slot.position);
            display.transform.localRotation = prefab.transform.localRotation;
            SetNativeWorldScale(display.transform, sourceScale);
            DisableDisplayColliders(display);
        }
    }

    void HideSlotMarkers()
    {
        HideSlotMarkers(processedInputDisplaySlots);
        HideSlotMarkers(pantryInputDisplaySlots);
        HideSlotMarkers(thirdInputDisplaySlots);
        HideSlotMarkers(outputDisplaySlots);
    }

    static void HideSlotMarkers(Transform[] slots)
    {
        if (slots == null) return;
        foreach (Transform slot in slots)
        {
            if (slot == null) continue;
            foreach (Renderer renderer in slot.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider collider in slot.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }
    }

    static void SetNativeWorldScale(Transform target, Vector3 sourceScale)
    {
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            sourceScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            sourceScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            sourceScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }

    static void DisableDisplayColliders(GameObject display)
    {
        foreach (Collider collider in display.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

}
