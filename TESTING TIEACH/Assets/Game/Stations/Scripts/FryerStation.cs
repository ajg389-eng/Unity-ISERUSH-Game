using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable fryer. Worker loads sliced potatoes, cooks them, then delivers fries to pickup.
/// </summary>
public class FryerStation : MonoBehaviour, IStationBuffer
{
    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one fryer operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 12f;
    public Vector3 interactionOffset = Vector3.zero;

    bool hasBasket;
    float cookTimer;
    CustomerOrder bufferedOrder;

    ItemDefinition RawItem => ProductionManager.Instance != null
        ? ProductionManager.Instance.SlicedPotatoItem : null;
    ItemDefinition CookedItem => ProductionManager.Instance != null
        ? ProductionManager.Instance.FriesItem : null;

    public int InputSlotCapacity => 1;
    public int OutputSlotCapacity => 1;
    public int GetInputCount(ItemDefinition item) => item == RawItem && IsCooking ? 1 : 0;
    public int GetOutputCount(ItemDefinition item) => item == CookedItem && IsCooked() ? 1 : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        !hasBasket && amount == 1 && item != null && item == RawItem;

    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        hasBasket = true;
        cookTimer = 0f;
        bufferedOrder = sourceOrder;
        return 1;
    }

    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || item == null || item != CookedItem || !IsCooked()) return 0;
        hasBasket = false;
        cookTimer = 0f;
        bufferedOrder = null;
        return 1;
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        !hasBasket || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanLoad() => !hasBasket;

    public bool IsCooking => hasBasket && cookTimer < processTimeSeconds;

    public bool IsCooked() => hasBasket && cookTimer >= processTimeSeconds;

    /// <summary>Consume one fries unit from kitchen stock and start cooking.</summary>
    public bool TryLoad(ItemDefinition potatoItem)
    {
        return StoreInput(potatoItem, 1) == 1;
    }

    public bool TakeCooked()
    {
        return TakeOutput(CookedItem, 1) == 1;
    }

    public void ResetRuntimeState()
    {
        hasBasket = false;
        cookTimer = 0f;
        bufferedOrder = null;
    }

    void Update()
    {
        if (hasBasket && cookTimer < processTimeSeconds)
            cookTimer += Time.deltaTime;
    }
}
