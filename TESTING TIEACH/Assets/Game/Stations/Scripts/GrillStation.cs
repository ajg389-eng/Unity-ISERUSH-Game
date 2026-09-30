using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable grill. Choose which product this grill cooks (Manage mode).
/// Currently used for burgers; selection is required before jobs are accepted.
/// </summary>
public class GrillStation : MonoBehaviour, IStationBuffer
{
    public const int BufferCapacity = 4;
    [Header("Product")]
    [Tooltip("Product this grill is set to cook. Must be chosen in Manage mode.")]
    public ItemDefinition selectedProduct;

    [FormerlySerializedAs("cookTimeSeconds")]
    [Tooltip("Total time for one grill operation. Loading, cooking, and unloading are included.")]
    [Min(0f)] public float processTimeSeconds = 4f;
    public Vector3 interactionOffset = Vector3.zero;

    [SerializeField, Min(0)] int pattyUnits;
    float cookTimer;
    CustomerOrder bufferedOrder;

    public bool HasProductSelected => selectedProduct != null;
    public bool HasPattyOnGrill => pattyUnits > 0;
    public bool IsCookingPatty => pattyUnits > 0 && cookTimer < processTimeSeconds;
    public int BufferedPattyCount => pattyUnits;
    public float CookProgressSeconds => cookTimer;
    public int InputSlotCapacity => BufferCapacity;
    public int OutputSlotCapacity => BufferCapacity;

    public int GetInputCount(ItemDefinition item) =>
        CanProcess(item) && IsCookingPatty ? pattyUnits : 0;
    public int GetOutputCount(ItemDefinition item) =>
        CanProcess(item) && IsCooked() ? pattyUnits : 0;
    public bool CanAcceptInput(ItemDefinition item, int amount) =>
        amount > 0 && CanProcess(item) && pattyUnits == 0 && amount <= BufferCapacity;
    public int StoreInput(ItemDefinition item, int amount, CustomerOrder sourceOrder = null)
    {
        if (!CanAcceptInput(item, amount)) return 0;
        pattyUnits = amount;
        cookTimer = 0f;
        bufferedOrder = sourceOrder;
        return amount;
    }
    public int TakeOutput(ItemDefinition item, int amount)
    {
        if (amount <= 0 || !CanProcess(item) || !IsCooked()) return 0;
        int taken = Mathf.Min(amount, pattyUnits);
        pattyUnits -= taken;
        if (pattyUnits <= 0)
        {
            pattyUnits = 0;
            cookTimer = 0f;
            bufferedOrder = null;
        }
        return taken;
    }

    public bool CanProcess(ItemDefinition product)
    {
        if (product == null || selectedProduct == null) return false;
        if (product == selectedProduct) return true;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config != null && config.IsBurger(product) && config.IsBurger(selectedProduct);
    }

    public bool IsHoldingOrder(CustomerOrder order) =>
        !HasPattyOnGrill || bufferedOrder == null || order == null || bufferedOrder == order;

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool CanPlacePatty()
    {
        return pattyUnits == 0 && HasProductSelected;
    }

    public void PlacePatty()
    {
        StoreInput(selectedProduct, 1);
    }

    public int PlacePatties(ItemDefinition item, int amount)
    {
        return StoreInput(item, amount);
    }

    public void UpdateCooking(float deltaTime)
    {
        if (pattyUnits > 0 && cookTimer < processTimeSeconds)
            cookTimer += deltaTime;
    }

    public bool IsCooked()
    {
        return pattyUnits > 0 && cookTimer >= processTimeSeconds;
    }

    public bool TakeCookedPatty()
    {
        return TakeOutput(selectedProduct, 1) == 1;
    }

    public int TakeCookedPatties(ItemDefinition item, int amount)
    {
        return TakeOutput(item, amount);
    }

    public void RestoreBufferedState(ItemDefinition product, int units, float progressSeconds)
    {
        selectedProduct = product;
        pattyUnits = Mathf.Clamp(units, 0, BufferCapacity);
        cookTimer = pattyUnits > 0
            ? Mathf.Clamp(progressSeconds, 0f, processTimeSeconds)
            : 0f;
        bufferedOrder = null;
    }

    void Update()
    {
        UpdateCooking(Time.deltaTime);
    }
}
