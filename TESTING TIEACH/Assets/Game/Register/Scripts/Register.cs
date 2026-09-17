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
    [Tooltip("Customers wait in front of the Pickup Station linked to this register (customer / lobby side).")]
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
    [Tooltip("Shared holding area. If unset, uses the nearest HeatLampStation.")]
    public HeatLampStation heatLamp;

    [Header("Worker station")]
    [Tooltip("Where an assigned worker stands to operate this register. Falls back to interaction tiles / behind register.")]
    public Transform workerStandPoint;
    [Tooltip("How close the assigned worker must be to serve customers")]
    public float workerDutyRadius = 1.25f;

    [Header("Ordering")]
    [Tooltip("Seconds the front customer spends ordering before moving to the Pickup Station line.")]
    public float orderTakeSeconds = 1.25f;

    float orderTimer;
    MoneyManager moneyManager;
    HeatLampStation cachedHeatLamp;
    static bool raisedFirstCustomerEvent;
    static bool raisedFirstOrderServedEvent;
    bool queueGrowingRaised;

    void Awake()
    {
        if (storeExit == null)
        {
            GameObject exit = GameObject.Find("Exit");
            if (exit != null) storeExit = exit.transform;
        }
        StationNode.EnsureOn(gameObject);
        EnsureInteractionTiles();
    }

    void OnDisable()
    {
        CustomerAI[] waiting = queue.ToArray();
        CustomerAI[] collecting = pickup.ToArray();
        queue.Clear();
        pickup.Clear();
        foreach (CustomerAI customer in waiting)
            if (customer != null) customer.OnRegisterDisabled();
        foreach (CustomerAI customer in collecting)
            if (customer != null) customer.OnRegisterDisabled();
    }

    void EnsureInteractionTiles()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles == null)
            tiles = gameObject.AddComponent<StationInteractionTiles>();
        // Creates neon-green stand quads when the register prefab has none.
        tiles.EnsureHighlightReference();
    }

    static bool IsDayOne =>
        GameTimeManager.Instance == null || GameTimeManager.Instance.CurrentDay <= 1;

    int EffectiveMaxQueue => Mathf.Min(maxQueue, 4);
    int EffectiveMaxPickup => Mathf.Min(maxPickup, IsDayOne ? 2 : 3);
    int EffectiveMaxInside => IsDayOne ? 4 : 6;

    public bool HasSpace()
    {
        if (!isEnabled) return false;
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
        if (front == null) return false;
        return Vector3.Distance(front.transform.position, GetQueueSlot(0)) <= serveArrivalRadius;
    }

    public void SendCustomerToPickup(CustomerAI customer)
    {
        if (customer == null || pickup.Contains(customer)) return;
        if (pickup.Count >= EffectiveMaxPickup) return;
        if (!queue.Contains(customer)) return;

        queue.Remove(customer);
        pickup.Add(customer);
        orderTimer = 0f;
        UpdateQueueTargets();
        Sfx.Play(SfxId.CustomerArrive);
    }

    /// <summary>
    /// Front-of-line order handoff → heat-lamp pickup line.
    /// Requires a cashier on the stand when one is assigned.
    /// </summary>
    public bool TryTakeFrontOrder()
    {
        if (!isEnabled) return false;
        if (pickup.Count >= EffectiveMaxPickup) return false;

        var front = GetFrontCustomer();
        if (front == null || !IsFrontCustomerReady())
        {
            orderTimer = 0f;
            return false;
        }

        // If a worker is assigned to this register, they must be on duty to take orders.
        if (AssignedWorker != null && !HasWorkerOnDuty())
        {
            orderTimer = 0f;
            return false;
        }

        orderTimer += Time.deltaTime;
        if (orderTimer < Mathf.Max(0.15f, orderTakeSeconds))
            return false;

        SendCustomerToPickup(front);
        return true;
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
        // Prefer an explicit employee-side stand point so cashiers don't path into the customer queue.
        if (workerStandPoint != null)
            return SnapWorkerPositionToTile(workerStandPoint.position);

        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null)
            return tiles.GetFirstInteractionPosition();

        // Fallback: stand on the opposite side of the counter from the queue.
        Vector3 awayFromQueue = -queueDirection.normalized;
        if (awayFromQueue.sqrMagnitude < 0.01f)
            awayFromQueue = -transform.forward;
        return SnapWorkerPositionToTile(transform.position + awayFromQueue * 0.9f);
    }

    static Vector3 SnapWorkerPositionToTile(Vector3 world)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) return world;
        Vector3 centered = grid.GetCellCenter(world);
        centered.y = grid.Origin.y;
        return centered;
    }

    HeatLampStation GetHeatLamp()
    {
        if (heatLamp != null)
        {
            cachedHeatLamp = heatLamp;
            return heatLamp;
        }

        if (cachedHeatLamp != null)
            return cachedHeatLamp;

        // Prefer the nearest placed heat lamp so multi-counter layouts stay local.
        cachedHeatLamp = FindNearestHeatLamp();
        if (cachedHeatLamp != null) return cachedHeatLamp;

        if (HeatLampStation.Instance != null)
        {
            cachedHeatLamp = HeatLampStation.Instance;
            return cachedHeatLamp;
        }

        var pm = ProductionManager.Instance;
        if (pm != null && pm.HeatLamp != null)
        {
            cachedHeatLamp = pm.HeatLamp;
            return cachedHeatLamp;
        }

        cachedHeatLamp = FindObjectOfType<HeatLampStation>();
        return cachedHeatLamp;
    }

    HeatLampStation FindNearestHeatLamp()
    {
        var lamps = FindObjectsOfType<HeatLampStation>();
        if (lamps == null || lamps.Length == 0) return null;

        HeatLampStation best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = transform.position;
        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (lamp == null) continue;
            float d = (lamp.transform.position - origin).sqrMagnitude;
            if (d >= bestDist) continue;
            bestDist = d;
            best = lamp;
        }
        return best;
    }

    /// <summary>Heat lamp used for customer self-serve pickup at the pass.</summary>
    public HeatLampStation GetHeatLampForPickup() => GetHeatLamp();

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
        if (!HasWorkerOnDuty()) return false;
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
        // Line 1: order at register. Line 2: wait at heat lamp for food.
        TryTakeFrontOrder();
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
                if (CounterSurface.IsAuxiliaryPlacementRenderer(rends[i], transform)) continue;
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

    /// <summary>From the register toward the kitchen (worker stand), never the door.</summary>
    Vector3 GetKitchenDir()
    {
        // Lobby is defined by queueDirection; kitchen is the opposite side of the counter.
        if (queueDirection.sqrMagnitude > 0.0001f)
            return -queueDirection.normalized;

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
    public Vector3 GetLobbyDirection() => GetLobbyDir();

    /// <summary>World position of the front order-queue stand (lobby side).</summary>
    public Vector3 GetFrontQueueWorldPosition() => GetQueueSlot(0);

    /// <summary>
    /// Customer / lobby side of the counter. Uses the register's queueDirection
    /// (order-line direction) — never worker interaction quads.
    /// </summary>
    Vector3 GetLobbyDir()
    {
        if (queueDirection.sqrMagnitude > 0.0001f)
            return queueDirection.normalized;
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

    /// <summary>
    /// Pickup line on the customer side of the heat lamp.
    /// Anchored to the same lobby depth as the register order line (cyan dots), then
    /// shifted along the counter to the heat lamp — never uses worker quads / kitchen tiles.
    /// </summary>
    Vector3 GetPickupSlot(int index)
    {
        Vector3 lobby = GetLobbyDir();
        Vector3 alongCounter = Vector3.Cross(Vector3.up, lobby);
        if (alongCounter.sqrMagnitude < 0.01f)
            alongCounter = Vector3.right;
        alongCounter.Normalize();

        // Same customer-side depth as the front of the order line.
        Vector3 orderFront = GetQueueSlot(0);
        Vector3 anchor = orderFront;

        var lamp = GetHeatLamp();
        if (lamp != null)
        {
            // Slide along the counter so the line sits in front of the heat lamp.
            float lateral = Vector3.Dot(lamp.transform.position - orderFront, alongCounter);
            anchor = orderFront + alongCounter * lateral;
        }
        else
        {
            anchor = orderFront + alongCounter * spacing;
        }

        // Extend further into the lobby for people waiting behind the front of pickup.
        Vector3 pos = anchor + lobby * (spacing * Mathf.Max(0, index));
        return SlotHeight(pos);
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

        // Pickup line sits on the lobby side of the linked heat lamp.
        Gizmos.color = Color.magenta;
        for (int i = 0; i < 4; i++)
            Gizmos.DrawSphere(GetPickupSlot(i), 0.12f);

        var lamp = GetHeatLamp();
        if (lamp != null)
        {
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.4f, lamp.transform.position + Vector3.up * 0.4f);
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetInteractionPosition(), 0.2f);
    }
}
