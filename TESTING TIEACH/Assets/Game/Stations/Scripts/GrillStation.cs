using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable grill. The selected product is the grill's own output, not the
/// downstream menu item that Assembly will eventually create.
/// </summary>
public class GrillStation : MonoBehaviour, IStationBuffer
{
    const int DefaultBufferCapacity = 2;
    [Header("Product")]
    [Tooltip("Output this grill produces. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one grill operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int pattyUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    int displayedUnits = -1;
    bool displayedCooked;
    ItemDefinition displayedProduct;

    public bool HasProductSelected => selectedProduct != null;
    public bool HasPattyOnGrill => pattyUnits > 0;
    public bool IsCookingPatty => pattyUnits > 0 && cookTimer < processTimeSeconds;
    public int BufferedPattyCount => pattyUnits;
    public float CookProgressSeconds => cookTimer;
    public int InputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, inputMarkers.Count); } }
    public int OutputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, outputMarkers.Count); } }

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
        NormalizeLegacySelection();
        FindBufferMarkers();
        pattyUnits = Mathf.Clamp(pattyUnits, 0, Mathf.Min(InputSlotCapacity, OutputSlotCapacity));
        RefreshItemDisplay(force: true);
    }

    public int GetInputCount(ItemDefinition item) =>
        IsRawPatty(item) && IsCookingPatty ? pattyUnits : 0;
    public int GetOutputCount(ItemDefinition item) =>
        IsCookedPatty(item) && IsCooked() ? pattyUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        amount > 0 && IsRawPatty(item) && pattyUnits == 0 && amount <= InputSlotCapacity;
    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        pattyUnits = amount;
        cookTimer = 0f;
        bufferedOrder = sourceOrder;
        return amount;
    }
    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || !IsCookedPatty(item) || !IsCooked()) return 0;
        int taken = Mathf.Min(amount, pattyUnits);
        pattyUnits -= taken;
        if (pattyUnits <= 0)
        {
            pattyUnits = 0;
            cookTimer = 0f;
            bufferedOrder = null;
        }
        return taken;
    }

    public bool CanProcess(ItemDefinition product)
    {
        NormalizeLegacySelection();
        if (product == null || selectedProduct == null) return false;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config == null) return product == selectedProduct;
        return selectedProduct == config.cookedPattyIngredient
            && (config.IsBurger(product) || product == config.rawPattyIngredient
                || product == config.cookedPattyIngredient);
    }

    public ItemDefinition GetSelectedOutput()
    {
        NormalizeLegacySelection();
        return selectedProduct;
    }

    public void SetRecipeOutput(ItemDefinition output)
    {
        NormalizeLegacySelection();
        if (selectedProduct != output)
        {
            pattyUnits = 0;
            cookTimer = 0f;
            bufferedOrder = null;
        }
        selectedProduct = output;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    bool IsRawPatty(ItemDefinition item)
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config != null ? item == config.rawPattyIngredient : item == selectedProduct;
    }

    bool IsCookedPatty(ItemDefinition item)
    {
        NormalizeLegacySelection();
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return item != null && (item == selectedProduct
            || (config != null && (item == config.cookedPattyIngredient
                || config.IsBurger(item))));
    }

    void NormalizeLegacySelection()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config != null && config.IsBurger(selectedProduct)
            && config.cookedPattyIngredient != null)
            selectedProduct = config.cookedPattyIngredient;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        !HasPattyOnGrill || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanPlacePatty()
    {
        return pattyUnits == 0 && HasProductSelected;
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
    }

    public bool IsCooked()
    {
        return pattyUnits > 0 && cookTimer >= processTimeSeconds;
    }

    public bool TakeCookedPatty()
    {
        return TakeOutput(GetSelectedOutput(), 1) == 1;
    }

    public int TakeCookedPatties(ItemDefinition item, int amount)
    {
        return TakeOutput(item, amount);
    }

    public void RestoreBufferedState(ItemDefinition product, int units, float progressSeconds)
    {
        selectedProduct = product;
        NormalizeLegacySelection();
        pattyUnits = Mathf.Clamp(units, 0, Mathf.Min(InputSlotCapacity, OutputSlotCapacity));
        cookTimer = pattyUnits > 0
            ? Mathf.Clamp(progressSeconds, 0f, processTimeSeconds)
            : 0f;
        bufferedOrder = null;
    }

    void Update()
    {
        UpdateCooking(Time.deltaTime);
        RefreshItemDisplay(force: false);
    }

    void RefreshItemDisplay(bool force)
    {
        bool cooked = IsCooked();
        if (!force && displayedUnits == pattyUnits && displayedCooked == cooked
            && displayedProduct == selectedProduct) return;

        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "GrillItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        displayedUnits = pattyUnits;
        displayedCooked = cooked;
        displayedProduct = selectedProduct;
        if (pattyUnits <= 0) return;

        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig
            : null;
        ItemDefinition visualItem = cooked
            ? (config != null ? config.cookedPattyIngredient : selectedProduct)
            : (config != null ? config.rawPattyIngredient : selectedProduct);
        GameObject prefab = visualItem != null ? visualItem.prefab : null;
        if (prefab == null) return;

        List<Transform> activeMarkers = cooked ? outputMarkers : inputMarkers;
        int visibleCount = Mathf.Min(pattyUnits, activeMarkers.Count);
        for (int i = 0; i < visibleCount; i++)
            StationItemVisualUtility.SpawnAtMarker(prefab, activeMarkers[i], itemDisplayRoot,
                (cooked ? "CookedPatty_" : "RawPatty_") + i);
    }

    void FindBufferMarkers()
    {
        StationBufferLayout.FindMarkers(transform, inputMarkers, outputMarkers);
    }

    void EnsureBufferMarkers()
    {
        if (inputMarkers.Count == 0 && outputMarkers.Count == 0) FindBufferMarkers();
    }
}
