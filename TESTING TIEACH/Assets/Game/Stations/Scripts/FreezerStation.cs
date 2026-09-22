using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Employee grabs a patty here (consumes from KitchenInventory).
/// </summary>
public class FreezerStation : MonoBehaviour
{
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one freezer operation.")]
    [Min(0f)] public float processTimeSeconds = 1f;
    public Vector3 interactionOffset = Vector3.zero;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    /// <summary>Consume one patty/burger base from kitchen stock.</summary>
    public bool TryTakePatty(ItemDefinition pattyItem)
    {
        if (pattyItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true; // legacy infinite
        return inv.TryConsume(pattyItem, 1);
    }

    public bool HasPatty(ItemDefinition pattyItem)
    {
        if (pattyItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.Has(pattyItem);
    }
}
