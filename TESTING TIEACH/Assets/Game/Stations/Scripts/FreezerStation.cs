using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Employee grabs a patty here (consumes from KitchenInventory).
/// </summary>
public class FreezerStation : MonoBehaviour, IStationBuffer
{
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one freezer operation.")]
    [Min(0f)] public float processTimeSeconds = 1f;
    public Vector3 interactionOffset = Vector3.zero;

    public int InputSlotCapacity => 0;
    public int OutputSlotCapacity => int.MaxValue;

    public int GetInputCount(ItemDefinition item) => 0;
    public int GetOutputCount(ItemDefinition item)
    {
        if (item == null) return 0;
        var inv = KitchenInventory.Instance;
        return inv != null ? inv.GetCount(item) : int.MaxValue;
    }
    public bool CanAcceptInput(ItemDefinition item, int amount) => false;
    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null) => 0;
    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        var inv = KitchenInventory.Instance;
        if (inv == null) return amount;
        int taken = Mathf.Min(amount, inv.GetCount(item));
        return taken > 0 && inv.TryConsume(item, taken) ? taken : 0;
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
