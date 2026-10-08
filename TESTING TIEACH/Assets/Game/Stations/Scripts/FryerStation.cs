using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable fryer. Worker loads sliced potatoes and produces cooked potato slices for Assembly.
/// </summary>
public class FryerStation : MonoBehaviour, IStationBuffer
{
    [Header("Product")]
    public ItemDefinition selectedProduct;
    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one fryer operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 8f;
    [SerializeField, Range(1, 2)] int stationMark = 1;
    public Vector3 interactionOffset = Vector3.zero;

    const int DefaultBufferCapacity = 2;
    [SerializeField, Min(0)] int basketUnits;
    [SerializeField, Min(0)] int waitingInputUnits;
    [SerializeField, Min(0)] int cookedOutputUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    int displayedUnits = -1;
    bool displayedCooked;
    ItemDefinition displayedProduct;
    StationProcessProgressIndicator processProgressIndicator;

    ItemDefinition CookedItem
    {
        get
        {
            if (selectedProduct != null) return selectedProduct;
            return ProductionManager.Instance != null ? ProductionManager.Instance.CookedPotatoItem : null;
        }
    }
    ItemDefinition RawItem
    {
        get
        {
            CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            StationProcessingRecipeDefinition recipe = config != null ? config.GetFryerRecipe(CookedItem) : null;
            return recipe != null ? recipe.input
                : (ProductionManager.Instance != null ? ProductionManager.Instance.SlicedPotatoItem : null);
        }
    }
    public bool CanProcess(ItemDefinition output) => output != null && output == CookedItem;
    public ItemDefinition GetSelectedOutput() => CookedItem;
    public ItemDefinition GetSelectedInput() => RawItem;
    public void SetRecipeOutput(ItemDefinition output)
    {
        if (selectedProduct != output) ResetRuntimeState();
        selectedProduct = output;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(true);
    }

    public int InputSlotCapacity => stationMark >= 2 ? 4 : DefaultBufferCapacity;
    public int OutputSlotCapacity => stationMark >= 2 ? 4 : DefaultBufferCapacity;
    public int BufferedUnitCount => waitingInputUnits + basketUnits + cookedOutputUnits;
    public int GetInputCount(ItemDefinition item) => item == RawItem ? waitingInputUnits : 0;
    public int GetOutputCount(ItemDefinition item) => item == CookedItem ? cookedOutputUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        amount > 0 && waitingInputUnits + amount <= InputSlotCapacity
            && item != null && item == RawItem;

    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        waitingInputUnits += amount;
        bufferedOrder = sourceOrder;
        StartNextBatchIfPossible();
        RefreshItemDisplay(true);
        return amount;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || item == null || item != CookedItem || cookedOutputUnits <= 0) return 0;
        int taken = Mathf.Min(amount, cookedOutputUnits);
        cookedOutputUnits -= taken;
        if (BufferedUnitCount == 0) bufferedOrder = null;
        StartNextBatchIfPossible();
        RefreshItemDisplay(true);
        return taken;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        BufferedUnitCount == 0 || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanLoad() => CanAcceptInput(RawItem, 1);

    public bool IsCooking => basketUnits > 0 && cookTimer < processTimeSeconds;
    public float ProcessRemainingSeconds => IsCooking
        ? Mathf.Max(0f, processTimeSeconds - cookTimer) : 0f;

    public bool IsCooked() => cookedOutputUnits > 0;

    /// <summary>Consume one fries unit from kitchen stock and start cooking.</summary>
    public bool TryLoad(ItemDefinition potatoItem)
    {
        return StoreInput(potatoItem, 1) == 1;
    }

    public int TryLoad(ItemDefinition potatoItem, int amount, CustomerOrder sourceOrder = null)
    {
        return StoreInput(potatoItem, amount, sourceOrder);
    }

    public bool TakeCooked()
    {
        return TakeOutput(CookedItem, 1) == 1;
    }

    public int TakeCooked(int amount)
    {
        return TakeOutput(CookedItem, amount);
    }

    public void ResetRuntimeState()
    {
        basketUnits = 0;
        waitingInputUnits = 0;
        cookedOutputUnits = 0;
        cookTimer = 0f;
        bufferedOrder = null;
        RefreshItemDisplay(true);
    }

    void OnEnable()
    {
        processProgressIndicator = StationProcessProgressIndicator.Ensure(gameObject);
        FindBufferMarkers();
        basketUnits = Mathf.Clamp(basketUnits, 0, Mathf.Min(InputSlotCapacity, OutputSlotCapacity));
        waitingInputUnits = Mathf.Clamp(waitingInputUnits, 0, InputSlotCapacity);
        cookedOutputUnits = Mathf.Clamp(cookedOutputUnits, 0, OutputSlotCapacity);
        RefreshItemDisplay(true);
    }

    void Update()
    {
        if (basketUnits > 0 && cookTimer < processTimeSeconds)
            cookTimer += Time.deltaTime;
        if (basketUnits > 0 && cookTimer >= processTimeSeconds
            && cookedOutputUnits + basketUnits <= OutputSlotCapacity)
        {
            cookedOutputUnits += basketUnits;
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(basketUnits);
            basketUnits = 0;
            cookTimer = 0f;
            StartNextBatchIfPossible();
        }
        processProgressIndicator?.SetProgress(
            IsCooking, cookTimer / Mathf.Max(0.01f, processTimeSeconds));
        RefreshItemDisplay(false);
    }

    void StartNextBatchIfPossible()
    {
        if (basketUnits > 0 || waitingInputUnits <= 0) return;
        int outputRoom = OutputSlotCapacity - cookedOutputUnits;
        int batch = Mathf.Min(waitingInputUnits, InputSlotCapacity, outputRoom);
        if (batch <= 0) return;
        waitingInputUnits -= batch;
        basketUnits = batch;
        cookTimer = 0f;
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
        bool cooked = IsCooked();
        if (!force && displayedUnits == BufferedUnitCount && displayedCooked == cooked
            && displayedProduct == selectedProduct) return;
        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "FryerItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);
        displayedUnits = BufferedUnitCount;
        displayedCooked = cooked;
        displayedProduct = selectedProduct;
        if (BufferedUnitCount <= 0) return;
        GameObject inputPrefab = RawItem != null ? RawItem.prefab : null;
        GameObject outputPrefab = CookedItem != null ? CookedItem.prefab : null;
        int rawVisible = Mathf.Min(waitingInputUnits + basketUnits, inputMarkers.Count);
        for (int i = 0; inputPrefab != null && i < rawVisible; i++)
            StationItemVisualUtility.SpawnAtMarker(inputPrefab, inputMarkers[i], itemDisplayRoot,
                "FryerInput_" + i);
        List<Transform> activeOutputs = outputMarkers.Count > 0 ? outputMarkers : inputMarkers;
        int outputVisible = Mathf.Min(cookedOutputUnits, activeOutputs.Count);
        for (int i = 0; outputPrefab != null && i < outputVisible; i++)
            StationItemVisualUtility.SpawnAtMarker(outputPrefab, activeOutputs[i], itemDisplayRoot,
                "FryerOutput_" + i);
    }
}
