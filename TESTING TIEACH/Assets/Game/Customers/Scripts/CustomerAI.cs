using UnityEngine;

public class CustomerAI : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Patience")]
    [Tooltip("Seconds a customer will wait in line before leaving.")]
    public float patienceDuration = 50f;

    [Header("Components")]
    public CustomerPatienceMeter patienceMeter;
    public CustomerOrderLabel orderLabel;

    Register reg;
    Vector3 targetPos;
    bool hasTarget;
    bool isFront;

    float queueJoinTime;
    public float QueueJoinTime => queueJoinTime;

    CustomerOrder order;
    int salePrice;
    bool waitingInQueue;
    bool leaving;
    bool leavingImpatient;
    Vector3 exitTarget;

    public CustomerOrder GetOrder() => order;
    public int SalePrice => salePrice;
    public bool IsOrderFullyDelivered =>
        order == null || order.lines == null || order.GetTotalQuantity() <= 0;

    void Awake()
    {
        if (patienceMeter == null)
            patienceMeter = GetComponent<CustomerPatienceMeter>();
        if (orderLabel == null)
            orderLabel = GetComponent<CustomerOrderLabel>();
    }

    public void SetOrder(CustomerOrder o)
    {
        order = o != null ? o.Clone() : new CustomerOrder();
        salePrice = order.GetSalePrice();
        RefreshOrderLabel();
    }

    public bool TryReceiveItem(ItemDefinition item)
    {
        if (item == null || order == null) return false;
        if (!order.TryRemoveOne(item)) return false;
        RefreshOrderLabel();
        return true;
    }

    void RefreshOrderLabel()
    {
        if (orderLabel == null)
            orderLabel = GetComponent<CustomerOrderLabel>();

        string label;
        if (IsOrderFullyDelivered)
            label = "Done!";
        else if (order != null && order.lines != null && order.lines.Count > 0)
            label = order.GetDisplayString();
        else
            label = "No order";

        if (orderLabel != null)
            orderLabel.SetText(label);
    }

    public void SetQueueJoinTime(float time)
    {
        queueJoinTime = time;
    }

    public void BeginQueueWait()
    {
        if (waitingInQueue) return;
        waitingInQueue = true;

        if (patienceMeter == null)
            patienceMeter = GetComponent<CustomerPatienceMeter>();

        if (patienceMeter != null)
            patienceMeter.Begin(patienceDuration, OnPatienceExpired);
    }

    void StopPatienceMeter()
    {
        waitingInQueue = false;
        if (patienceMeter != null)
            patienceMeter.Stop();
    }

    void OnPatienceExpired()
    {
        if (!waitingInQueue || leaving || leavingImpatient) return;

        Transform exit = reg != null ? reg.storeExit : null;
        if (reg != null)
            reg.LeaveQueue(this);

        LeaveImpatient(exit);
    }

    public void LeaveImpatient(Transform exit)
    {
        StopPatienceMeter();
        reg = null;
        hasTarget = true;
        leavingImpatient = true;
        exitTarget = exit != null ? exit.position : transform.position + transform.forward * 8f;
        targetPos = exitTarget;
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

    public void OnServed(Transform exit)
    {
        StopPatienceMeter();
        reg = null;
        hasTarget = true;
        leaving = true;
        exitTarget = exit != null ? exit.position : transform.position + transform.forward * 10f;
        targetPos = exitTarget;
    }

    public void OnRegisterDisabled()
    {
        StopPatienceMeter();
        Destroy(gameObject);
    }

    void Update()
    {
        if (!hasTarget) return;

        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (leaving && Vector3.Distance(transform.position, targetPos) < 0.2f)
            Destroy(gameObject);

        if (leavingImpatient && Vector3.Distance(transform.position, targetPos) < 0.2f)
            Destroy(gameObject);
    }
}
