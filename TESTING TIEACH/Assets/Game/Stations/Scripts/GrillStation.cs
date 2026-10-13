using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable grill. The selected product is the grill's own output, not the
/// downstream menu item that Assembly will eventually create.
/// </summary>
public class GrillStation : MonoBehaviour, IStationBuffer
{
    const int Mk1SlotCapacity = 4;
    const int Mk2SlotCapacity = 8;
    [Header("Product")]
    [Tooltip("Output this grill produces. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one grill operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 8f;
    [SerializeField, Range(1, 2)] int stationMark = 1;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int pattyUnits;
    [SerializeField, Min(0)] int waitingInputUnits;
    [SerializeField, Min(0)] int secondaryInputUnits;
    [SerializeField, Min(0)] int cookedOutputUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    readonly List<Transform> slotMarkers = new List<Transform>();
    int displayedUnits = -1;
    bool displayedCooked;
    ItemDefinition displayedProduct;
    StationProcessProgressIndicator processProgressIndicator;

    public bool HasProductSelected => selectedProduct != null;
    public bool HasPattyOnGrill => pattyUnits > 0;
    public bool IsCookingPatty => pattyUnits > 0 && cookTimer < processTimeSeconds;
    public int BufferedPattyCount => waitingInputUnits + pattyUnits + cookedOutputUnits + secondaryInputUnits;
    public int SecondaryInputUnitCount => secondaryInputUnits;
    public float CookProgressSeconds => cookTimer;
    public float ProcessRemainingSeconds => IsCookingPatty
        ? Mathf.Max(0f, processTimeSeconds - cookTimer) : 0f;
    public int SlotCapacity => stationMark >= 2 ? Mk2SlotCapacity : Mk1SlotCapacity;
    public int StationMark => Mathf.Clamp(stationMark, 1, 2);
    public int InputSlotCapacity => SlotCapacity;
    public int OutputSlotCapacity => SlotCapacity;

    void OnEnable()
    {
        processProgressIndicator = StationProcessProgressIndicator.Ensure(gameObject);
        StationConfigurationCaution.Ensure(gameObject);
        NormalizeLegacySelection();
        FindBufferMarkers();
        // Legacy saves could contain a full input buffer and a separate full
        // output buffer. Collapse them into the shared physical slot pool,
        // retaining completed food first.
        cookedOutputUnits = Mathf.Clamp(cookedOutputUnits, 0, SlotCapacity);
        pattyUnits = Mathf.Clamp(pattyUnits, 0, SlotCapacity - cookedOutputUnits);
        waitingInputUnits = Mathf.Clamp(waitingInputUnits, 0,
            SlotCapacity - cookedOutputUnits - pattyUnits);
        secondaryInputUnits = Mathf.Clamp(secondaryInputUnits, 0,
            SlotCapacity - cookedOutputUnits - pattyUnits - waitingInputUnits);
        StartNextBatchIfPossible();
        RefreshItemDisplay(force: true);
    }

    public int GetInputCount(ItemDefinition item) => IsSelectedSecondaryInput(item)
        ? secondaryInputUnits : IsSelectedInput(item) ? waitingInputUnits : 0;
    public int GetOutputCount(ItemDefinition item) =>
        IsSelectedOutput(item) ? cookedOutputUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        amount > 0 && (IsSelectedInput(item) || IsSelectedSecondaryInput(item))
            && BufferedPattyCount + amount <= SlotCapacity;
    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        if (IsSelectedSecondaryInput(item)) secondaryInputUnits += amount;
        else waitingInputUnits += amount;
        bufferedOrder = sourceOrder;
        StartNextBatchIfPossible();
        RefreshItemDisplay(true);
        return amount;
    }
    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || !IsSelectedOutput(item) || cookedOutputUnits <= 0) return 0;
        int taken = Mathf.Min(amount, cookedOutputUnits);
        cookedOutputUnits -= taken;
        if (waitingInputUnits + pattyUnits + cookedOutputUnits + secondaryInputUnits == 0) bufferedOrder = null;
        StartNextBatchIfPossible();
        RefreshItemDisplay(true);
        return taken;
    }

    public bool CanProcess(ItemDefinition product)
    {
        NormalizeLegacySelection();
        if (product == null || selectedProduct == null) return false;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (product == selectedProduct) return true;
        if (config == null || !config.IsBurger(product)) return false;
        List<ItemDefinition> stages = config.GetAssemblyChain(product);
        if (stages.Count == 0) return selectedProduct == config.cookedPattyIngredient;
        AssemblyRecipeDefinition first = config.GetAssemblyRecipe(stages[0]);
        return first != null && first.processedInput == selectedProduct;
    }

    public ItemDefinition GetSelectedOutput()
    {
        NormalizeLegacySelection();
        return selectedProduct;
    }
    public ItemDefinition GetSelectedInput() => SelectedInput;
    public ItemDefinition GetSelectedSecondaryInput() => SelectedSecondaryInput;

    public void SetRecipeOutput(ItemDefinition output)
    {
        NormalizeLegacySelection();
        if (selectedProduct != output)
        {
            pattyUnits = 0;
            waitingInputUnits = 0;
            secondaryInputUnits = 0;
            cookedOutputUnits = 0;
            cookTimer = 0f;
            bufferedOrder = null;
        }
        selectedProduct = output;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    ItemDefinition SelectedInput
    {
        get
        {
            CustomerOrderConfig config = ProductionManager.Instance != null
                ? ProductionManager.Instance.orderConfig : null;
            StationProcessingRecipeDefinition recipe = config != null ? config.GetGrillRecipe(selectedProduct) : null;
            return recipe != null ? recipe.input : (config != null ? config.rawPattyIngredient : selectedProduct);
        }
    }
    ItemDefinition SelectedSecondaryInput
    {
        get
        {
            CustomerOrderConfig config = ProductionManager.Instance != null
                ? ProductionManager.Instance.orderConfig : null;
            StationProcessingRecipeDefinition recipe = config != null ? config.GetGrillRecipe(selectedProduct) : null;
            return recipe != null ? recipe.secondaryInput : null;
        }
    }
    bool IsSelectedInput(ItemDefinition item) => item != null && item == SelectedInput;
    bool IsSelectedSecondaryInput(ItemDefinition item) => item != null && item == SelectedSecondaryInput;
    bool IsSelectedOutput(ItemDefinition item) => item != null && item == selectedProduct;

    void NormalizeLegacySelection()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config != null && config.IsBurger(selectedProduct)
            && config.cookedPattyIngredient != null)
            selectedProduct = config.cookedPattyIngredient;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        BufferedPattyCount == 0 || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanPlacePatty()
    {
        return CanAcceptInput(SelectedInput, 1);
    }

    public void PlacePatty()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        StoreInput(config != null ? config.rawPattyIngredient : selectedProduct, 1);
    }

    public int PlacePatties(ItemDefinition item, int amount)
    {
        return StoreInput(item, amount);
    }

    public void UpdateCooking(float deltaTime)
    {
        if (pattyUnits > 0 && cookTimer < processTimeSeconds)
            cookTimer += deltaTime;
        if (pattyUnits > 0 && cookTimer >= processTimeSeconds)
        {
            cookedOutputUnits += pattyUnits;
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(pattyUnits);
            pattyUnits = 0;
            cookTimer = 0f;
            StartNextBatchIfPossible();
        }
    }

    public bool IsCooked()
    {
        return cookedOutputUnits > 0;
    }

    void StartNextBatchIfPossible()
    {
        if (pattyUnits > 0 || waitingInputUnits <= 0
            || (SelectedSecondaryInput != null && secondaryInputUnits <= 0)) return;
        // Raw food already occupies its final grill slots. Starting or finishing
        // cooking changes item state without moving it into another buffer.
        int batch = Mathf.Min(waitingInputUnits, SlotCapacity - cookedOutputUnits);
        if (SelectedSecondaryInput != null) batch = Mathf.Min(batch, secondaryInputUnits);
        if (batch <= 0) return;
        waitingInputUnits -= batch;
        if (SelectedSecondaryInput != null)
            secondaryInputUnits = Mathf.Max(0, secondaryInputUnits - batch);
        pattyUnits = batch;
        cookTimer = 0f;
    }

    public bool TakeCookedPatty()
    {
        return TakeOutput(GetSelectedOutput(), 1) == 1;
    }

    public int TakeCookedPatties(ItemDefinition item, int amount)
    {
        return TakeOutput(item, amount);
    }

    public void RestoreBufferedState(ItemDefinition product, int units, float progressSeconds,
        int secondaryUnits = 0)
    {
        selectedProduct = product;
        NormalizeLegacySelection();
        waitingInputUnits = 0;
        secondaryInputUnits = Mathf.Clamp(secondaryUnits, 0, SlotCapacity);
        cookedOutputUnits = 0;
        pattyUnits = Mathf.Clamp(units, 0, Mathf.Max(0, Mathf.Min(InputSlotCapacity, OutputSlotCapacity) - secondaryInputUnits));
        cookTimer = pattyUnits > 0
            ? Mathf.Clamp(progressSeconds, 0f, processTimeSeconds)
            : 0f;
        bufferedOrder = null;
    }

    void Update()
    {
        UpdateCooking(Time.deltaTime);
        processProgressIndicator?.SetProgress(
            IsCookingPatty, cookTimer / Mathf.Max(0.01f, processTimeSeconds));
        RefreshItemDisplay(force: false);
    }

    void RefreshItemDisplay(bool force)
    {
        bool cooked = IsCooked();
        if (!force && displayedUnits == BufferedPattyCount && displayedCooked == cooked
            && displayedProduct == selectedProduct) return;

        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "GrillItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        displayedUnits = BufferedPattyCount;
        displayedCooked = cooked;
        displayedProduct = selectedProduct;
        if (BufferedPattyCount <= 0) return;

        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig
            : null;
        GameObject inputPrefab = SelectedInput != null ? SelectedInput.prefab : null;
        GameObject outputPrefab = selectedProduct != null ? selectedProduct.prefab : null;
        // Completed food stays in the same authored slots. Newly loaded raw food
        // occupies only the remaining shared slots instead of a separate row.
        int outputVisible = Mathf.Min(cookedOutputUnits, slotMarkers.Count);
        for (int i = 0; outputPrefab != null && i < outputVisible; i++)
            StationItemVisualUtility.SpawnAtMarker(outputPrefab, slotMarkers[i], itemDisplayRoot,
                "GrillOutput_" + i);
        int visibleSlot = outputVisible;
        for (int i = 0; inputPrefab != null && i < waitingInputUnits + pattyUnits
            && visibleSlot < slotMarkers.Count; i++, visibleSlot++)
            StationItemVisualUtility.SpawnAtMarker(inputPrefab, slotMarkers[visibleSlot], itemDisplayRoot,
                "GrillInput_" + i);
        GameObject secondaryPrefab = SelectedSecondaryInput != null ? SelectedSecondaryInput.prefab : null;
        for (int i = 0; secondaryPrefab != null && i < secondaryInputUnits
            && visibleSlot < slotMarkers.Count; i++, visibleSlot++)
            StationItemVisualUtility.SpawnAtMarker(secondaryPrefab, slotMarkers[visibleSlot], itemDisplayRoot,
                "GrillSecondaryInput_" + i);
    }

    void FindBufferMarkers()
    {
        StationBufferLayout.FindMarkers(transform, inputMarkers, outputMarkers);
        slotMarkers.Clear();
        slotMarkers.AddRange(inputMarkers);
        slotMarkers.AddRange(outputMarkers);
        slotMarkers.Sort((a, b) =>
        {
            int z = a.position.z.CompareTo(b.position.z);
            return z != 0 ? z : a.position.x.CompareTo(b.position.x);
        });
    }

    void EnsureBufferMarkers()
    {
        if (slotMarkers.Count == 0) FindBufferMarkers();
    }
}
