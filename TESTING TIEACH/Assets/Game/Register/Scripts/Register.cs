using System.Collections.Generic;
using UnityEngine;

public class Register : MonoBehaviour
{
    public bool isEnabled = true;

    [Header("Queue")]
    public Transform queueStart;
    public Vector3 queueDirection = Vector3.back;
    public float spacing = 1.2f;
    [Tooltip("How far behind the register the first customer stands.")]
    public float queueFrontOffset = 1.4f;
    [Tooltip("Front customer must be this close to the first queue slot before they can order")]
    public float serveArrivalRadius = 0.6f;
    public int maxQueue = 6;

    [Header("Pickup line")]
    [Tooltip("Where customers wait after ordering. If empty, a line is placed beside the order queue, into the kitchen.")]
    public Transform pickupStart;
    public Vector3 pickupDirection = Vector3.zero;
    public int maxPickup = 8;

    readonly List<CustomerAI> queue = new List<CustomerAI>();
    readonly List<CustomerAI> pickup = new List<CustomerAI>();
    public int QueueCount => queue.Count;
    public int PickupCount => pickup.Count;

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
    bool queueGrowingRaised;

    void Awake()
    {
        StationNode.EnsureOn(gameObject);
        EnsureInteractionTiles();
    }

    void EnsureInteractionTiles()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles == null)
            tiles = gameObject.AddComponent<StationInteractionTiles>();
        tiles.EnsureHighlightReference();
    }

    static bool IsDayOne =>
        GameTimeManager.Instance == null || GameTimeManager.Instance.CurrentDay <= 1;

    int EffectiveMaxQueue => Mathf.Min(maxQueue, 4);
    int EffectiveMaxPickup => Mathf.Min(maxPickup, IsDayOne ? 2 : 3);
    int EffectiveMaxInside => IsDayOne ? 4 : 6;

    public bool HasSpace()
    {
        if (!isEnabled || queueStart == null) return false;
        if (queue.Count >= EffectiveMaxQueue) return false;
        if (pickup.Count >= EffectiveMaxPickup) return false;
        return queue.Count + pickup.Count < EffectiveMaxInside;
    }

    public bool TryJoinQueue(CustomerAI customer)
    {
        if (!HasSpace() || customer == null) return false;
        if (queue.Contains(customer) || pickup.Contains(customer)) return true;

        customer.SetQueueJoinTime(Time.time);
        queue.Add(customer);
        TryUnlockPickup();
        UpdateQueueTargets();

        if (!raisedFirstCustomerEvent)
        {
            raisedFirstCustomerEvent = true;
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstCustomerArrived);
        }

        Sfx.Play(SfxId.CustomerArrive);
        return true;
    }

    void TryUnlockPickup()
    {
        if (queue.Count < 3 || queueGrowingRaised) return;
        queueGrowingRaised = true;
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.QueueGrowing);
    }

    public void LeaveQueue(CustomerAI customer)
    {
        if (customer == null) return;
        queue.Remove(customer);
        pickup.Remove(customer);
        UpdateQueueTargets();
    }

    public void RefreshQueueTargets() => UpdateQueueTargets();

    public bool HasPreparedOrder => preparedOrder != null && preparedOrder.lines != null && preparedOrder.lines.Count > 0;

    public CustomerOrder GetFrontCustomerOrder()
    {
        var front = GetFrontCustomer();
        return front != null ? front.GetOrder() : null;
    }

    public List<CustomerOrder> GetQueuedOrders()
    {
        var list = new List<CustomerOrder>();
        AddOrders(list, queue);
        AddOrders(list, pickup);
        return list;
    }

    static void AddOrders(List<CustomerOrder> list, List<CustomerAI> customers)
    {
        foreach (var c in customers)
        {
            if (c == null) continue;
            var o = c.GetOrder();
            if (o != null) list.Add(o);
        }
    }

    public CustomerAI GetFrontCustomer()
    {
        if (queue.Count == 0) return null;
        return queue[0];
    }

    public CustomerAI GetPickupCustomer()
    {
        for (int i = 0; i < pickup.Count; i++)
        {
            if (pickup[i] != null) return pickup[i];
        }
        return null;
    }

    public bool IsFrontCustomerReady()
    {
        var front = GetFrontCustomer();
        if (front == null || queueStart == null) return false;
        return Vector3.Distance(front.transform.position, GetQueueSlot(0)) <= serveArrivalRadius;
    }

    public void SendCustomerToPickup(CustomerAI customer)
    {
        if (customer == null || pickup.Contains(customer)) return;
        if (pickup.Count >= EffectiveMaxPickup) return;
        if (!queue.Contains(customer)) return;

        queue.Remove(customer);
        pickup.Add(customer);
        UpdateQueueTargets();
    }

    public KitchenEmployee AssignedWorker
    {
        get
        {
            var node = GetComponent<StationNode>();
            return node != null ? node.assignedWorker : null;
        }
    }

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

    public void TryServeFront() { }

    public bool TryDeliverItem(CustomerAI customer, ItemDefinition item)
    {
        if (!isEnabled || customer == null || item == null) return false;
        if (!pickup.Contains(customer) && !queue.Contains(customer))
            return false;

        if (!customer.TryReceiveItem(item))
            return false;

        Sfx.Play(SfxId.ItemDelivered);

        if (customer.IsOrderFullyDelivered)
            CompleteServeFront(customer, customer.SalePrice);
        return true;
    }

    public bool CompleteServe(CustomerAI customer, CustomerOrder soldOrder)
    {
        if (!isEnabled || customer == null) return false;
        if (!pickup.Contains(customer) && (queue.Count == 0 || queue[0] != customer))
            return false;

        int sale = customer.SalePrice > 0
            ? customer.SalePrice
            : (soldOrder != null ? soldOrder.GetSalePrice() : 0);
        CompleteServeFront(customer, sale);
        return true;
    }

    void CompleteServeFront(CustomerAI front, int sale)
    {
        if (!pickup.Remove(front) && (queue.Count == 0 || queue[0] != front))
            return;
        if (queue.Count > 0 && queue[0] == front)
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
            pos = GetQueueSlot(0) + Vector3.up * 1.8f;
        FloatingMoneyText.Spawn(pos, sale, transform);
    }

    public void Toggle()
    {
        isEnabled = !isEnabled;

        if (!isEnabled)
        {
            foreach (var c in queue)
                if (c != null) c.OnRegisterDisabled();
            foreach (var c in pickup)
                if (c != null) c.OnRegisterDisabled();
            queue.Clear();
            pickup.Clear();
        }

        UpdateQueueTargets();
    }

    void Update()
    {
        // Serving is cashier-driven via KitchenEmployee.
    }

    Vector3 QueueDir
    {
        get
        {
            if (queueDirection.sqrMagnitude > 0.0001f) return queueDirection.normalized;
            return Vector3.back;
        }
    }

    Bounds GetRegisterBounds()
    {
        bool has = false;
        var bounds = new Bounds(transform.position, Vector3.one);
        var cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || !cols[i].enabled) continue;
            if (!has) { bounds = cols[i].bounds; has = true; }
            else bounds.Encapsulate(cols[i].bounds);
        }

        if (!has)
        {
            var rends = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!has) { bounds = rends[i].bounds; has = true; }
                else bounds.Encapsulate(rends[i].bounds);
            }
        }

        if (has)
            bounds.Expand(0.35f);
        return bounds;
    }

    static float ExtentAlong(Bounds bounds, Vector3 dir)
    {
        dir = dir.normalized;
        return Mathf.Abs(dir.x) * bounds.extents.x
            + Mathf.Abs(dir.y) * bounds.extents.y
            + Mathf.Abs(dir.z) * bounds.extents.z;
    }

    /// <summary>From the register toward the kitchen (worker stand / heat lamp), never the door.</summary>
    Vector3 GetKitchenDir()
    {
        Vector3 kitchen = Vector3.zero;

        Vector3 worker = GetInteractionPosition() - transform.position;
        worker.y = 0f;
        if (worker.sqrMagnitude > 0.25f)
            kitchen = worker;

        if (kitchen.sqrMagnitude < 0.04f)
        {
            var lamp = GetHeatLamp();
            if (lamp != null)
            {
                kitchen = lamp.transform.position - transform.position;
                kitchen.y = 0f;
            }
        }

        if (kitchen.sqrMagnitude < 0.04f && storeExit != null)
        {
            Vector3 toDoor = storeExit.position - transform.position;
            toDoor.y = 0f;
            if (toDoor.sqrMagnitude > 0.04f)
                kitchen = -toDoor;
        }

        if (kitchen.sqrMagnitude < 0.04f)
            kitchen = Vector3.back;

        return kitchen.normalized;
    }

    /// <summary>From the register toward the lobby — opposite the kitchen, never into it.</summary>
    Vector3 GetLobbyDir()
    {
        return -GetKitchenDir();
    }

    Vector3 SlotHeight(Vector3 pos)
    {
        if (queueStart != null)
            pos.y = queueStart.position.y;
        else
            pos.y = transform.position.y;
        return pos;
    }

    Vector3 GetQueueSlot(int index)
    {
        Vector3 lobby = GetLobbyDir();
        Bounds bounds = GetRegisterBounds();
        Vector3 first = bounds.center + lobby * (ExtentAlong(bounds, lobby) + Mathf.Max(1.15f, queueFrontOffset));
        return SlotHeight(first) + lobby * (spacing * Mathf.Max(0, index));
    }

    Vector3 GetPickupOrigin()
    {
        if (pickupStart != null) return pickupStart.position;

        Vector3 lobby = GetLobbyDir();
        Vector3 side = Vector3.Cross(Vector3.up, lobby);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();

        Bounds bounds = GetRegisterBounds();
        Vector3 origin = bounds.center
            + lobby * (ExtentAlong(bounds, lobby) + Mathf.Max(1.15f, queueFrontOffset))
            + side * 1.15f;
        return SlotHeight(origin);
    }

    Vector3 GetPickupDir()
    {
        if (pickupDirection.sqrMagnitude > 0.01f)
        {
            Vector3 custom = pickupDirection.normalized;
            if (Vector3.Dot(custom, GetKitchenDir()) > 0.2f)
                return GetLobbyDir();
            return custom;
        }
        return GetLobbyDir();
    }

    Vector3 GetPickupSlot(int index)
    {
        return GetPickupOrigin() + GetPickupDir() * (spacing * Mathf.Max(0, index));
    }

    void UpdateQueueTargets()
    {
        for (int i = 0; i < queue.Count; i++)
        {
            var c = queue[i];
            if (c == null) continue;
            c.SetQueueSlot(this, GetQueueSlot(i), i == 0);
        }

        for (int i = 0; i < pickup.Count; i++)
        {
            var c = pickup[i];
            if (c == null) continue;
            c.SetPickupSlot(this, GetPickupSlot(i), i == 0);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (queueStart)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < Mathf.Min(maxQueue, 6); i++)
                Gizmos.DrawSphere(GetQueueSlot(i), 0.15f);
        }

        Gizmos.color = Color.magenta;
        for (int i = 0; i < 4; i++)
            Gizmos.DrawSphere(GetPickupSlot(i), 0.12f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetInteractionPosition(), 0.2f);
    }
}
