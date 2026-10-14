using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Employee grabs a patty here (consumes from KitchenInventory).
/// </summary>
public class FreezerStation : MonoBehaviour, IStationBuffer
{
    [Header("Stored ingredient")]
    [Tooltip("The only ingredient this freezer can dispense. Choose it in Management mode.")]
    public ItemDefinition selectedItem;

    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one freezer operation.")]
    [Min(0f)] public float processTimeSeconds = 0f;
    [Tooltip("Maximum sustained amount this freezer can dispense per minute.")]
    [Min(0.1f)] public float maxOutputPerMinute = 30f;
    public Vector3 interactionOffset = Vector3.zero;

    [System.NonSerialized] float outputTokens;
    [System.NonSerialized] float outputTokensUpdatedAt;
    [System.NonSerialized] bool outputTokensInitialized;

    public int InputSlotCapacity => 0;
    public int OutputSlotCapacity => int.MaxValue;
    public float EffectiveMaxOutputPerMinute
    {
        get
        {
            StationNode node = GetComponent<StationNode>();
            return Mathf.Max(0f, maxOutputPerMinute)
                * (node != null ? node.ProductionRateMultiplier : 1f);
        }
    }

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
    }

    public int GetInputCount(ItemDefinition item) => 0;
    public int GetOutputCount(ItemDefinition item)
    {
        if (!CanSupply(item)) return 0;
        var inv = KitchenInventory.Instance;
        return inv != null ? inv.GetCount(selectedItem) : int.MaxValue;
    }
    public bool CanAcceptInput(ItemDefinition item, int amount) => false;
    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null) => 0;
    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (!CanSupply(item) || amount <= 0) return 0;
        var inv = KitchenInventory.Instance;
        int stock = inv != null ? inv.GetCount(selectedItem) : amount;
        int requested = Mathf.Min(amount, stock);
        int limited = StationOutputRateLimiter.TakeUnits(requested, EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        if (limited <= 0) return 0;
        if (inv == null)
        {
            StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(limited);
            return limited;
        }
        if (!inv.TryConsume(selectedItem, limited))
        {
            StationOutputRateLimiter.RefundUnits(limited, ref outputTokens);
            return 0;
        }
        int taken = limited;
        StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(taken);
        return taken;
    }

    public int GetReadyOutputCount(ItemDefinition item)
    {
        if (!CanSupply(item)) return 0;
        var inv = KitchenInventory.Instance;
        int stock = inv != null ? inv.GetCount(selectedItem) : int.MaxValue;
        int rateReady = StationOutputRateLimiter.GetAvailableUnits(EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        return Mathf.Min(stock, rateReady);
    }

    public bool HasItemSelected => selectedItem != null;

    public void SetStoredItem(ItemDefinition item)
    {
        selectedItem = item;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
    }

    public bool CanSupply(ItemDefinition requested)
    {
        if (selectedItem == null || requested == null) return false;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config != null && config.IsDrink(selectedItem)) return false;
        if (requested == selectedItem) return true;
        return config != null && selectedItem == config.rawPattyIngredient && config.IsBurger(requested);
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    /// <summary>Consume one patty/burger base from kitchen stock.</summary>
    public bool TryTakePatty(ItemDefinition pattyItem)
    {
        return TakeOutput(pattyItem, 1) == 1;
    }

    public bool HasPatty(ItemDefinition pattyItem)
    {
        return GetReadyOutputCount(pattyItem) > 0;
    }
}
