using UnityEngine;

public class CustomerAI : MonoBehaviour
{
    public float moveSpeed = 2.5f;

    private Register reg;
    private Vector3 targetPos;
    private bool hasTarget;

    private bool isFront;

    float queueJoinTime;
    public float QueueJoinTime => queueJoinTime;

    /// <summary>Items still owed to this customer (shrinks as the cashier delivers).</summary>
    CustomerOrder order;
    /// <summary>Full sale price locked in when the order was placed.</summary>
    int salePrice;
    CustomerOrderLabel orderLabel;

    public CustomerOrder GetOrder() => order;
    public int SalePrice => salePrice;
    public bool IsOrderFullyDelivered =>
        order == null || order.lines == null || order.GetTotalQuantity() <= 0;

    public void SetOrder(CustomerOrder o)
    {
        order = o != null ? o.Clone() : new CustomerOrder();
        salePrice = order.GetSalePrice();
        RefreshOrderLabel();
    }

    /// <summary>Hand one item to the customer; removes it from the floating order list.</summary>
    public bool TryReceiveItem(ItemDefinition item)
    {
        if (item == null || order == null) return false;
        if (!order.TryRemoveOne(item)) return false;
        RefreshOrderLabel();
        return true;
    }

    void RefreshOrderLabel()
    {
        if (orderLabel == null) orderLabel = GetComponent<CustomerOrderLabel>();
        if (orderLabel == null) orderLabel = gameObject.AddComponent<CustomerOrderLabel>();

        string label;
        if (IsOrderFullyDelivered)
            label = "Done!";
        else if (order != null && order.lines != null && order.lines.Count > 0)
            label = order.GetDisplayString();
        else
            label = "No order";

        var existing = transform.Find("OrderLabel");
        if (existing == null)
            orderLabel.Setup(transform, label);
        else
            orderLabel.SetText(label);
    }

    public void SetQueueJoinTime(float time)
    {
        queueJoinTime = time;
    }

    public void SetTargetRegister(Register r)
    {
        reg = r;
        if (reg == null) return;

        reg.TryJoinQueue(this);
    }

    public void SetQueueSlot(Register register, Vector3 slotPos, bool front)
    {
        reg = register;
        targetPos = slotPos;
        isFront = front;
        hasTarget = true;
    }

    bool leaving;
    Vector3 exitTarget;

    public void OnServed(Transform exit)
    {
        reg = null;
        hasTarget = true;
        leaving = true;
        exitTarget = exit != null ? exit.position : transform.position + transform.forward * 10f;
        targetPos = exitTarget;
    }

    public void OnRegisterDisabled()
    {
        Destroy(gameObject);
    }

    void Update()
    {
        if (!hasTarget) return;

        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (leaving && Vector3.Distance(transform.position, targetPos) < 0.2f)
            Destroy(gameObject);
    }
}
