using System.Collections.Generic;
using UnityEngine;

public class Register : MonoBehaviour
{
    public bool isEnabled = true;

    [Header("Queue")]
    public Transform queueStart;
    public Vector3 queueDirection = Vector3.back;
    public float spacing = 1.2f;
    [Tooltip("Front customer must be this close to the register before they can be served")]
    public float serveArrivalRadius = 0.6f;
    public int maxQueue = 6;

    readonly List<CustomerAI> queue = new List<CustomerAI>();
    public int QueueCount => queue.Count;

    [Header("Prepared orders")]
    [Tooltip("Legacy single-slot buffer. Prefer HeatLampStation for fast-food serving.")]
    CustomerOrder preparedOrder;
    [Tooltip("Where customers walk to after being served")]
    public Transform storeExit;
    [Tooltip("Shared holding area. If unset, uses HeatLampStation.Instance.")]
    public HeatLampStation heatLamp;

    [Header("Worker station")]
    [Tooltip("Where an assigned worker stands to operate this register. Falls back to interaction tiles / behind register.")]
    public Transform workerStandPoint;
    [Tooltip("How close the assigned worker must be to serve customers")]
    public float workerDutyRadius = 1.25f;

    MoneyManager moneyManager;
    static bool raisedFirstCustomerEvent;
    static bool raisedFirstOrderServedEvent;

    void Awake()
    {
        StationNode.EnsureOn(gameObject);
        EnsureInteractionTiles();
    }

    /// <summary>
    /// Registers use the same green interaction quads as kitchen stations:
    /// visible in Inventory/Build mode, hidden in Play/Manage.
    /// </summary>
    void EnsureInteractionTiles()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles == null)
            tiles = gameObject.AddComponent<StationInteractionTiles>();
        tiles.EnsureHighlightReference();
    }

    public bool HasSpace()
    {
        return isEnabled && queueStart != null && queue.Count < maxQueue;
    }

    public bool TryJoinQueue(CustomerAI customer)
    {
        if (!HasSpace() || customer == null) return false;
        if (queue.Contains(customer)) return true;

        customer.SetQueueJoinTime(Time.time);
        customer.BeginQueueWait();
        queue.Add(customer);
        UpdateQueueTargets();

        if (!raisedFirstCustomerEvent)
        {
            raisedFirstCustomerEvent = true;
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstCustomerArrived);
        }

        Sfx.Play(SfxId.CustomerArrive);

        if (queue.Count >= 3)
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.QueueGrowing);

        return true;
    }

    public void LeaveQueue(CustomerAI customer)
    {
        if (customer == null) return;
        queue.Remove(customer);
        UpdateQueueTargets();
    }

    public bool HasPreparedOrder => preparedOrder != null && preparedOrder.lines != null && preparedOrder.lines.Count > 0;

    public CustomerOrder GetFrontCustomerOrder()
    {
        if (queue.Count == 0) return null;
        var front = queue[0];
        return front != null ? front.GetOrder() : null;
    }

    public List<CustomerOrder> GetQueuedOrders()
    {
        var list = new List<CustomerOrder>(queue.Count);
        foreach (var c in queue)
        {
            if (c == null) continue;
            var o = c.GetOrder();
            if (o != null) list.Add(o);
        }
        return list;
    }

    public CustomerAI GetFrontCustomer()
    {
        if (queue.Count == 0) return null;
        return queue[0];
    }

    /// <summary>Front customer is close enough to the first queue slot to be served.</summary>
    public bool IsFrontCustomerReady()
    {
        var front = GetFrontCustomer();
        if (front == null || queueStart == null) return false;
        return Vector3.Distance(front.transform.position, queueStart.position) <= serveArrivalRadius;
    }

    public KitchenEmployee AssignedWorker
    {
        get
        {
            var node = GetComponent<StationNode>();
            return node != null ? node.assignedWorker : null;
        }
    }

    /// <summary>True when a worker is assigned and close enough to operate the register.</summary>
    public bool HasWorkerOnDuty()
    {
        var worker = AssignedWorker;
        if (worker == null) return false;
        Vector3 stand = GetInteractionPosition();
        return Vector3.Distance(worker.transform.position, stand) <= workerDutyRadius;
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        if (workerStandPoint != null) return workerStandPoint.position;
        return transform.position + transform.forward * -0.8f;
    }

    HeatLampStation GetHeatLamp()
    {
        if (heatLamp != null) return heatLamp;
        return HeatLampStation.Instance;
    }

    public void DeliverOrder(CustomerOrder order)
    {
        var lamp = GetHeatLamp();
        if (lamp != null)
        {
            lamp.DeliverMeal(order);
            return;
        }

        preparedOrder = order != null ? order.Clone() : null;
    }

    /// <summary>Legacy auto-serve disabled — cashiers fetch/deliver items via TryDeliverItem.</summary>
    public void TryServeFront() { }

    /// <summary>
    /// Cashier hands one item to the front customer. Removes it from their order label.
    /// When the order is empty, completes the sale and sends them out.
    /// </summary>
    public bool TryDeliverItem(CustomerAI customer, ItemDefinition item)
    {
        if (!isEnabled || customer == null || item == null) return false;
        if (queue.Count == 0 || queue[0] != customer) return false;
        if (!HasWorkerOnDuty()) return false;
        if (!IsFrontCustomerReady()) return false;

        if (!customer.TryReceiveItem(item))
            return false;

        Sfx.Play(SfxId.ItemDelivered);

        if (customer.IsOrderFullyDelivered)
        {
            CompleteServeFront(customer, customer.SalePrice);
        }
        return true;
    }

    /// <summary>Cashier finished the full order (legacy path).</summary>
    public bool CompleteServe(CustomerAI customer, CustomerOrder soldOrder)
    {
        if (!isEnabled || customer == null) return false;
        if (queue.Count == 0 || queue[0] != customer) return false;
        if (!HasWorkerOnDuty()) return false;
        if (!IsFrontCustomerReady()) return false;

        int sale = customer.SalePrice > 0
            ? customer.SalePrice
            : (soldOrder != null ? soldOrder.GetSalePrice() : 0);
        CompleteServeFront(customer, sale);
        return true;
    }

    void CompleteServeFront(CustomerAI front, int sale)
    {
        if (queue.Count == 0 || queue[0] != front) return;
        queue.RemoveAt(0);

        if (sale > 0)
        {
            if (moneyManager == null) moneyManager = FindObjectOfType<MoneyManager>();
            if (moneyManager != null) moneyManager.AddMoney(sale);
            ShowSalePopup(sale);
            Sfx.Play(SfxId.EarnMoney);
        }

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.RecordOrderCompleted(front.QueueJoinTime, this, sale);
        if (front != null) front.OnServed(storeExit);
        UpdateQueueTargets();

        TutorialVoiceEvents.Raise(TutorialVoiceEventId.OrderServed);
        if (!raisedFirstOrderServedEvent)
        {
            raisedFirstOrderServedEvent = true;
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstOrderServed);
        }

        Sfx.Play(SfxId.CustomerServed);
    }

    void ShowSalePopup(int sale)
    {
        Vector3 pos = transform.position + Vector3.up * 1.6f;
        if (queueStart != null)
            pos = queueStart.position + Vector3.up * 1.8f;
        FloatingMoneyText.Spawn(pos, sale, transform);
    }

    public void Toggle()
    {
        isEnabled = !isEnabled;

        if (!isEnabled)
        {
            foreach (var c in queue)
                if (c != null) c.OnRegisterDisabled();
            queue.Clear();
        }

        UpdateQueueTargets();
    }

    void Update()
    {
        // Serving is cashier-driven via KitchenEmployee
    }

    void UpdateQueueTargets()
    {
        for (int i = 0; i < queue.Count; i++)
        {
            var c = queue[i];
            if (c == null) continue;

            Vector3 dir = queueDirection.normalized;
            Vector3 slotPos = queueStart.position + dir * (spacing * i);
            c.SetQueueSlot(this, slotPos, i == 0);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (queueStart)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < maxQueue; i++)
            {
                Vector3 slot = queueStart.position + queueDirection.normalized * (spacing * i);
                Gizmos.DrawSphere(slot, 0.15f);
            }
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetInteractionPosition(), 0.2f);
    }
}
