using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Customers walk an optional outside entry path, then stand on their register queue slot.
/// Queue lineup uses direct movement to Register queue points (not grid pathfinding).
/// </summary>
public class CustomerAI : MonoBehaviour
{
    enum Phase
    {
        Entering,
        GoingToSlot,
        Waiting,
        Leaving
    }

    [Header("Movement")]
    public float moveSpeed = 2.5f;
    [Tooltip("How close to a path / queue point counts as arrived.")]
    public float arrivalDistance = 0.15f;

    [Header("Patience")]
    [Tooltip("Seconds a customer will wait in line before leaving (starts after reaching their queue slot).")]
    public float patienceDuration = 50f;

    [Header("Components")]
    public CustomerPatienceMeter patienceMeter;
    public CustomerOrderLabel orderLabel;
    public GridManager grid;

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

    readonly List<Vector3> route = new List<Vector3>();
    int routeIndex;
    readonly List<Vector3> gridPath = new List<Vector3>();
    Phase phase = Phase.GoingToSlot;
    bool patienceStarted;
    CustomerPath exitPath;

    // Slot assigned by Register even while still walking the entry path
    bool hasQueueSlot;
    Vector3 queuedSlotPos;
    bool queuedIsFront;

    public CustomerOrder GetOrder() => order;
    public int SalePrice => salePrice;
    public bool IsOrderFullyDelivered =>
        order == null || order.lines == null || order.GetTotalQuantity() <= 0;

    public bool IsEntering => phase == Phase.Entering;

    void Awake()
    {
        if (patienceMeter == null)
            patienceMeter = GetComponent<CustomerPatienceMeter>();
        if (orderLabel == null)
            orderLabel = GetComponent<CustomerOrderLabel>();
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
    }

    public void BeginEntryRoute(IList<Vector3> worldPoints, GridManager gridOverride = null, CustomerPath exit = null)
    {
        exitPath = exit;
        if (gridOverride != null)
            grid = gridOverride;
        else if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();

        route.Clear();
        routeIndex = 0;
        gridPath.Clear();
        hasTarget = false;

        if (worldPoints != null)
        {
            for (int i = 0; i < worldPoints.Count; i++)
                route.Add(worldPoints[i]);
        }

        while (route.Count > 0 && HorizontalDist(transform.position, route[0]) <= arrivalDistance)
            route.RemoveAt(0);

        phase = route.Count > 0 ? Phase.Entering : Phase.GoingToSlot;

        // If we already have a queue slot (joined before path setup), start moving once entry is done.
        if (phase != Phase.Entering)
            ApplyQueueSlotMovement();
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

        if (orderLabel != null)
            orderLabel.SetOrder(order, IsOrderFullyDelivered);
    }

    public void SetQueueJoinTime(float time)
    {
        queueJoinTime = time;
    }

    public void BeginQueueWait()
    {
        if (waitingInQueue || patienceStarted) return;
        waitingInQueue = true;
        patienceStarted = true;

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
        hasTarget = false;
        hasQueueSlot = false;
        leaving = true;
        leavingImpatient = true;
        BeginLeaveRoute(exit);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.RecordCustomerLost();
    }

    public void SetTargetRegister(Register r)
    {
        reg = r;
        if (reg == null) return;
        reg.TryJoinQueue(this);
    }

    /// <summary>
    /// Register assigns this customer's place in line. Always remembered;
    /// movement to the slot starts only after the entry path finishes.
    /// </summary>
    public void SetQueueSlot(Register register, Vector3 slotPos, bool front)
    {
        if (phase == Phase.Leaving) return;

        reg = register;
        queuedSlotPos = slotPos;
        queuedIsFront = front;
        hasQueueSlot = true;
        isFront = front;

        if (phase == Phase.Entering)
            return;

        ApplyQueueSlotMovement();
    }

    void ApplyQueueSlotMovement()
    {
        if (!hasQueueSlot || phase == Phase.Entering || phase == Phase.Leaving)
            return;

        bool slotMoved = !hasTarget || HorizontalDist(targetPos, queuedSlotPos) > 0.05f;
        targetPos = queuedSlotPos;
        isFront = queuedIsFront;
        hasTarget = true;

        if (phase == Phase.Waiting && !slotMoved)
            return;

        phase = Phase.GoingToSlot;
    }

    public void OnServed(Transform exit)
    {
        StopPatienceMeter();
        reg = null;
        hasTarget = false;
        hasQueueSlot = false;
        leaving = true;
        leavingImpatient = false;
        BeginLeaveRoute(exit);
    }

    public void OnRegisterDisabled()
    {
        StopPatienceMeter();
        Destroy(gameObject);
    }

    void BeginLeaveRoute(Transform fallbackExit)
    {
        phase = Phase.Leaving;
        route.Clear();
        routeIndex = 0;
        gridPath.Clear();

        if (exitPath != null && exitPath.Count > 0)
            exitPath.GetWorldPoints(route);
        else if (fallbackExit != null)
            route.Add(fallbackExit.position);
        else
            route.Add(transform.position + Vector3.forward * 8f);
    }

    void Update()
    {
        switch (phase)
        {
            case Phase.Entering:
                UpdateEntering();
                break;
            case Phase.GoingToSlot:
                UpdateGoingToSlot();
                break;
            case Phase.Waiting:
                UpdateWaiting();
                break;
            case Phase.Leaving:
                UpdateLeaving();
                break;
        }
    }

    void UpdateEntering()
    {
        if (routeIndex >= route.Count)
        {
            phase = Phase.GoingToSlot;
            ApplyQueueSlotMovement();
            // If join happened but slot never arrived, ask register to refresh.
            if (!hasQueueSlot && reg != null)
                reg.RefreshQueueTargets();
            return;
        }

        MoveStraightTo(route[routeIndex]);
        if (HorizontalDist(transform.position, route[routeIndex]) <= arrivalDistance)
            routeIndex++;
    }

    void UpdateGoingToSlot()
    {
        if (!hasTarget)
        {
            if (hasQueueSlot)
                ApplyQueueSlotMovement();
            else if (reg != null)
                reg.RefreshQueueTargets();
            return;
        }

        // Direct move to the register's queue point so customers line up correctly.
        if (MoveStraightTo(targetPos))
        {
            phase = Phase.Waiting;
            BeginQueueWait();
        }
    }

    void UpdateWaiting()
    {
        if (!hasQueueSlot) return;

        // Keep locked to the current queue slot if the line shifts forward.
        targetPos = queuedSlotPos;
        hasTarget = true;

        if (HorizontalDist(transform.position, targetPos) > arrivalDistance)
        {
            phase = Phase.GoingToSlot;
            return;
        }

        SnapXZ(targetPos);

        if (!patienceStarted)
            BeginQueueWait();
    }

    void UpdateLeaving()
    {
        if (routeIndex >= route.Count)
        {
            Destroy(gameObject);
            return;
        }

        if (MoveStraightTo(route[routeIndex]))
            routeIndex++;
    }

    bool MoveStraightTo(Vector3 target)
    {
        Vector3 pos = transform.position;
        target.y = pos.y;
        if (HorizontalDist(pos, target) <= arrivalDistance)
        {
            SnapXZ(target);
            return true;
        }

        transform.position = Vector3.MoveTowards(pos, target, moveSpeed * Time.deltaTime);
        return HorizontalDist(transform.position, target) <= arrivalDistance;
    }

    void SnapXZ(Vector3 world)
    {
        Vector3 p = transform.position;
        p.x = world.x;
        p.z = world.z;
        transform.position = p;
    }

    static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
