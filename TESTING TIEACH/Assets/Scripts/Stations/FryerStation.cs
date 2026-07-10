using UnityEngine;

/// <summary>
/// Placeable fryer. Worker loads fries from kitchen stock, cooks, then delivers to the heat lamp.
/// </summary>
public class FryerStation : MonoBehaviour
{
    [Tooltip("Time to load a basket of fries.")]
    public float loadTimeSeconds = 0.5f;
    [Tooltip("Cook time once loaded.")]
    public float cookTimeSeconds = 5f;
    [Tooltip("Wait after cooked before take.")]
    public float waitAfterCookedSeconds = 0.4f;
    [Tooltip("Time to take fries out.")]
    public float takeTimeSeconds = 0.5f;
    public Vector3 interactionOffset = Vector3.zero;

    bool hasBasket;
    float cookTimer;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanLoad() => !hasBasket;

    public bool IsCooking => hasBasket && cookTimer < cookTimeSeconds;

    public bool IsCooked() => hasBasket && cookTimer >= cookTimeSeconds;

    /// <summary>Consume one fries unit from kitchen stock and start cooking.</summary>
    public bool TryLoad(ItemDefinition friesItem)
    {
        if (hasBasket || friesItem == null) return false;
        var inv = KitchenInventory.Instance;
        if (inv != null && !inv.TryConsume(friesItem, 1))
            return false;
        hasBasket = true;
        cookTimer = 0f;
        return true;
    }

    public bool TakeCooked()
    {
        if (!IsCooked()) return false;
        hasBasket = false;
        cookTimer = 0f;
        return true;
    }

    void Update()
    {
        if (hasBasket && cookTimer < cookTimeSeconds)
            cookTimer += Time.deltaTime;
    }
}
