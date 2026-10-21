using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable fryer. Worker loads sliced potatoes and produces cooked potato slices for Assembly.
/// </summary>
public partial class FryerStation : MonoBehaviour, IStationBuffer
{
    [Header("Product")]
    public ItemDefinition selectedProduct;
    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Time to fry one item at 100% rate.")]
    [Min(0f)] public float processTimeSeconds = 6f;
    [SerializeField, Range(1, 2)] int stationMark = 1;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int basketUnits;
    [SerializeField, Min(0)] int waitingInputUnits;
    [SerializeField, Min(0)] int cookedOutputUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    int displayedWaitingInput = -1;
    int displayedBasket = -1;
    int displayedCookedOutput = -1;
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

    public int StationMark => Mathf.Clamp(stationMark, 1, 2);
    public float BaseProcessCycleSeconds
    {
        get
        {
            CustomerOrderConfig config = ProductionManager.Instance != null
                ? ProductionManager.Instance.orderConfig : null;
            StationProcessingRecipeDefinition recipe = config != null
                ? config.GetFryerRecipe(CookedItem) : null;
            return recipe != null
                ? StationNode.GetTierAdjustedCycleSeconds(this, recipe.baseCycleSeconds)
                : processTimeSeconds;
        }
    }
    public float EffectiveProcessTimeSeconds => StationNode.GetEffectiveCycleSeconds(this, BaseProcessCycleSeconds);
    public int InputSlotCapacity => StationBufferLimits.PerItem;
    public int OutputSlotCapacity => StationBufferLimits.PerItem;
    public int BufferedUnitCount => waitingInputUnits + basketUnits + cookedOutputUnits;
    public int VisibleInputSlotCount { get { EnsureBufferMarkers(); return inputMarkers.Count; } }
    public int VisibleOutputSlotCount
    {
        get
        {
            EnsureBufferMarkers();
            return outputMarkers.Count > 0 ? outputMarkers.Count : inputMarkers.Count;
        }
    }
    public int GetInputCount(ItemDefinition item) => item == RawItem ? waitingInputUnits : 0;
    public int GetHeldInputCount(ItemDefinition item) => GetInputCount(item)
        + (item != null && item == RawItem ? basketUnits : 0);
    public int GetOutputCount(ItemDefinition item) => item == CookedItem ? cookedOutputUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        amount > 0 && item != null && item == RawItem
            && waitingInputUnits + amount <= InputSlotCapacity;

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

    public bool IsCooking => basketUnits > 0 && cookTimer < EffectiveProcessTimeSeconds;
    public float ProcessRemainingSeconds => IsCooking
        ? Mathf.Max(0f, EffectiveProcessTimeSeconds - cookTimer) : 0f;

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
        basketUnits = Mathf.Clamp(basketUnits, 0, InputSlotCapacity);
        waitingInputUnits = Mathf.Clamp(waitingInputUnits, 0, InputSlotCapacity);
        cookedOutputUnits = Mathf.Clamp(cookedOutputUnits, 0, OutputSlotCapacity);
        RefreshItemDisplay(true);
    }

    void Update()
    {
        if (ProductionManager.IsScheduledStation(gameObject))
        {
            processProgressIndicator?.SetProgress(
                IsCooking, cookTimer / Mathf.Max(0.01f, EffectiveProcessTimeSeconds));
            RefreshItemDisplay(false);
            return;
        }
        float remainingTime = Mathf.Max(0f, Time.deltaTime);
        while (true)
        {
            if (basketUnits <= 0)
                StartNextBatchIfPossible();
            if (basketUnits <= 0 || cookedOutputUnits >= OutputSlotCapacity) break;

            float cycleSeconds = EffectiveProcessTimeSeconds;
            if (cycleSeconds <= 0.001f)
            {
                basketUnits--;
                cookedOutputUnits++;
                StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(CookedItem, 1);
                continue;
            }

            float untilOutput = Mathf.Max(0f, cycleSeconds - cookTimer);
            float elapsed = Mathf.Min(remainingTime, untilOutput);
            cookTimer += elapsed;
            remainingTime -= elapsed;
            if (cookTimer + 0.0001f < cycleSeconds) break;

            basketUnits--;
            cookedOutputUnits++;
            cookTimer = Mathf.Max(0f, cookTimer - cycleSeconds);
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(CookedItem, 1);
            if (basketUnits <= 0) cookTimer = 0f;
        }
        processProgressIndicator?.SetProgress(
            IsCooking, cookTimer / Mathf.Max(0.01f, EffectiveProcessTimeSeconds));
        RefreshItemDisplay(false);
    }

    void StartNextBatchIfPossible()
    {
        if (basketUnits > 0 || waitingInputUnits <= 0) return;
        int outputRoom = OutputSlotCapacity - cookedOutputUnits;
        int availableItems = Mathf.Min(waitingInputUnits, InputSlotCapacity, outputRoom);
        if (availableItems <= 0) return;
        waitingInputUnits -= availableItems;
        // The fryer can queue several items, but it produces one per cycle.
        basketUnits = availableItems;
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
        if (!force && displayedWaitingInput == waitingInputUnits
            && displayedBasket == basketUnits && displayedCookedOutput == cookedOutputUnits
            && displayedProduct == selectedProduct) return;
        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "FryerItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);
        displayedWaitingInput = waitingInputUnits;
        displayedBasket = basketUnits;
        displayedCookedOutput = cookedOutputUnits;
        displayedProduct = selectedProduct;
        if (BufferedUnitCount <= 0) return;
        GameObject inputPrefab = RawItem != null ? RawItem.prefab : null;
        GameObject outputPrefab = CookedItem != null ? CookedItem.prefab : null;
        if (outputMarkers.Count > 0)
        {
            int rawVisible = Mathf.Min(waitingInputUnits + basketUnits, inputMarkers.Count);
            for (int i = 0; inputPrefab != null && i < rawVisible; i++)
                StationItemVisualUtility.SpawnAtMarker(inputPrefab, inputMarkers[i], itemDisplayRoot,
                    "FryerInput_" + i);
            int outputVisible = Mathf.Min(cookedOutputUnits, outputMarkers.Count);
            for (int i = 0; outputPrefab != null && i < outputVisible; i++)
                StationItemVisualUtility.SpawnAtMarker(outputPrefab, outputMarkers[i], itemDisplayRoot,
                    "FryerOutput_" + i);
        }
        else
        {
            int outputVisible = Mathf.Min(cookedOutputUnits, inputMarkers.Count);
            for (int i = 0; outputPrefab != null && i < outputVisible; i++)
                StationItemVisualUtility.SpawnAtMarker(outputPrefab, inputMarkers[i], itemDisplayRoot,
                    "FryerOutput_" + i);
            int slot = outputVisible;
            int rawVisible = Mathf.Min(waitingInputUnits + basketUnits, inputMarkers.Count - slot);
            for (int i = 0; inputPrefab != null && i < rawVisible; i++, slot++)
                StationItemVisualUtility.SpawnAtMarker(inputPrefab, inputMarkers[slot], itemDisplayRoot,
                    "FryerInput_" + i);
        }
    }
}
