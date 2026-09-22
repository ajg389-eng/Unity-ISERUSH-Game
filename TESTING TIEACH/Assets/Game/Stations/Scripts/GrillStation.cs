using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable grill. Choose which product this grill cooks (Manage mode).
/// Currently used for burgers; selection is required before jobs are accepted.
/// </summary>
public class GrillStation : MonoBehaviour
{
    [Header("Product")]
    [Tooltip("Product this grill is set to cook. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one grill operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    bool hasPatty;
    float cookTimer;

    public bool HasProductSelected => selectedProduct != null;
    public bool HasPattyOnGrill => hasPatty;
    public bool IsCookingPatty => hasPatty && cookTimer < processTimeSeconds;

    public bool CanProcess(ItemDefinition product) =>
        product != null && selectedProduct != null && product == selectedProduct;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanPlacePatty()
    {
        return !hasPatty && HasProductSelected;
    }

    public void PlacePatty()
    {
        if (hasPatty || !HasProductSelected) return;
        hasPatty = true;
        cookTimer = 0f;
    }

    public void UpdateCooking(float deltaTime)
    {
        if (hasPatty && cookTimer < processTimeSeconds)
            cookTimer += deltaTime;
    }

    public bool IsCooked()
    {
        return hasPatty && cookTimer >= processTimeSeconds;
    }

    public bool TakeCookedPatty()
    {
        if (!hasPatty || cookTimer < processTimeSeconds) return false;
        hasPatty = false;
        return true;
    }

    void Update()
    {
        UpdateCooking(Time.deltaTime);
    }
}
