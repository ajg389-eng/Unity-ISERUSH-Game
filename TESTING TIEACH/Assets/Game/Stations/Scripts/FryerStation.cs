using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable fryer. Worker loads fries from kitchen stock, cooks, then delivers to the heat lamp.
/// </summary>
public class FryerStation : MonoBehaviour
{
    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one fryer operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 12f;
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

    public bool IsCooking => hasBasket && cookTimer < processTimeSeconds;

    public bool IsCooked() => hasBasket && cookTimer >= processTimeSeconds;

    /// <summary>Consume one fries unit from kitchen stock and start cooking.</summary>
    public bool TryLoad(ItemDefinition potatoItem)
    {
        if (hasBasket || potatoItem == null) return false;
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
        if (hasBasket && cookTimer < processTimeSeconds)
            cookTimer += Time.deltaTime;
    }
}
