using UnityEngine;

/// <summary>
/// Burger assembly only. Choose the burger product in Manage mode; fries/drinks do not use this station.
/// </summary>
public class AssemblyStation : MonoBehaviour
{
    [Header("Product")]
    [Tooltip("Burger product this station assembles. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [Tooltip("Time in seconds for the employee to assemble the burger.")]
    public float interactionTimeSeconds = 1.2f;
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
