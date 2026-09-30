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
    [Min(0f)] public float processTimeSeconds = 1f;
    public Vector3 interactionOffset = Vector3.zero;

    public int InputSlotCapacity => 0;
    public int OutputSlotCapacity => int.MaxValue;

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
        if (inv == null) return amount;
        int taken = Mathf.Min(amount, inv.GetCount(selectedItem));
        return taken > 0 && inv.TryConsume(selectedItem, taken) ? taken : 0;
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
        if (requested == selectedItem) return true;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
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
        return GetOutputCount(pattyItem) > 0;
    }
}
