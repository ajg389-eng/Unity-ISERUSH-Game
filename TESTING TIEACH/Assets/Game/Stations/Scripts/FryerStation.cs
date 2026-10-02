using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable fryer. Worker loads sliced potatoes and produces cooked potato slices for Assembly.
/// </summary>
public class FryerStation : MonoBehaviour, IStationBuffer
{
    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one fryer operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 12f;
    public Vector3 interactionOffset = Vector3.zero;

    const int DefaultBufferCapacity = 2;
    [SerializeField, Min(0)] int basketUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;
    Transform itemDisplayRoot;
    readonly List<Transform> inputMarkers = new List<Transform>();
    readonly List<Transform> outputMarkers = new List<Transform>();
    int displayedUnits = -1;
    bool displayedCooked;

    ItemDefinition RawItem => ProductionManager.Instance != null
        ? ProductionManager.Instance.SlicedPotatoItem : null;
    ItemDefinition CookedItem => ProductionManager.Instance != null
        ? ProductionManager.Instance.CookedPotatoItem : null;

    public int InputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, inputMarkers.Count); } }
    public int OutputSlotCapacity { get { EnsureBufferMarkers(); return Mathf.Max(DefaultBufferCapacity, outputMarkers.Count > 0 ? outputMarkers.Count : inputMarkers.Count); } }
    public int BufferedUnitCount => basketUnits;
    public int GetInputCount(ItemDefinition item) => item == RawItem && IsCooking ? basketUnits : 0;
    public int GetOutputCount(ItemDefinition item) => item == CookedItem && IsCooked() ? basketUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        basketUnits == 0 && amount > 0 && amount <= InputSlotCapacity && item != null && item == RawItem;

    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        basketUnits = amount;
        cookTimer = 0f;
        bufferedOrder = sourceOrder;
        RefreshItemDisplay(true);
        return amount;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || item == null || item != CookedItem || !IsCooked()) return 0;
        int taken = Mathf.Min(amount, basketUnits);
        basketUnits -= taken;
        if (basketUnits <= 0)
        {
            basketUnits = 0;
            cookTimer = 0f;
            bufferedOrder = null;
        }
        RefreshItemDisplay(true);
        return taken;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        basketUnits == 0 || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanLoad() => basketUnits == 0;

    public bool IsCooking => basketUnits > 0 && cookTimer < processTimeSeconds;

    public bool IsCooked() => basketUnits > 0 && cookTimer >= processTimeSeconds;

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
        cookTimer = 0f;
        bufferedOrder = null;
        RefreshItemDisplay(true);
    }

    void OnEnable()
    {
        FindBufferMarkers();
        basketUnits = Mathf.Clamp(basketUnits, 0, Mathf.Min(InputSlotCapacity, OutputSlotCapacity));
        RefreshItemDisplay(true);
    }

    void Update()
    {
        if (basketUnits > 0 && cookTimer < processTimeSeconds)
            cookTimer += Time.deltaTime;
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
        bool cooked = IsCooked();
        if (!force && displayedUnits == basketUnits && displayedCooked == cooked) return;
        EnsureBufferMarkers();
        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "FryerItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);
        displayedUnits = basketUnits;
        displayedCooked = cooked;
        if (basketUnits <= 0) return;

        ItemDefinition visualItem = cooked ? CookedItem : RawItem;
        GameObject prefab = visualItem != null ? visualItem.prefab : null;
        if (prefab == null) return;
        List<Transform> markers = cooked && outputMarkers.Count > 0 ? outputMarkers : inputMarkers;
        int visible = Mathf.Min(basketUnits, markers.Count);
        for (int i = 0; i < visible; i++)
            StationItemVisualUtility.SpawnAtMarker(prefab, markers[i], itemDisplayRoot,
                (cooked ? "CookedPotatoSlice_" : "SlicedPotato_") + i);
    }
}
