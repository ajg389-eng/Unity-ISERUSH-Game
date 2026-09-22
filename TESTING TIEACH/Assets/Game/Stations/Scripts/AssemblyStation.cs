using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Burger assembly only. Choose the burger product in Manage mode; fries/drinks do not use this station.
/// </summary>
public class AssemblyStation : MonoBehaviour
{
    [Header("Product")]
    [Tooltip("Burger product this station assembles. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one assembly operation.")]
    [Min(0f)] public float processTimeSeconds = 1.2f;
    public Vector3 interactionOffset = Vector3.zero;

    public bool HasProductSelected => selectedProduct != null;

    public bool CanProcess(ItemDefinition product) =>
        product != null && selectedProduct != null && product == selectedProduct;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }
}
