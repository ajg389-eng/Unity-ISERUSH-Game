using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Customers walk an optional outside entry path, then use customer-floor tile centers
/// to reach register, waiting-area, and pickup queue positions.
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
    bool waitingForPickup;
    HeatLampStation pickupStation;
    bool leaving;
    bool leavingImpatient;

    readonly List<Vector3> route = new List<Vector3>();
    int routeIndex;
    readonly List<Vector3> gridPath = new List<Vector3>();
    Vector3 gridPathDestination = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
    Renderer customerFloorRenderer;
    Phase phase = Phase.GoingToSlot;
    bool patienceStarted;
    CustomerPath exitPath;
    bool hasReturnSpawnPoint;
    Vector3 returnSpawnPoint;

    // Slot assigned by Register even while still walking the entry path
    bool hasQueueSlot;
    Vector3 queuedSlotPos;
    bool queuedIsFront;

    public CustomerOrder GetOrder() => order;
    public int SalePrice => salePrice;
    public bool IsOrderFullyDelivered =>
        order == null || order.lines == null || order.GetTotalQuantity() <= 0;

    public bool IsEntering => phase == Phase.Entering;
    public bool IsLeaving => phase == Phase.Leaving;

    /// <summary>
    /// Distance along <paramref name="forward"/> to the nearest customer
    /// walking between a car and the building. Large when the lane is clear.
    /// </summary>
    public static float ClosestWalkerAhead(Vector3 from, Vector3 forward, float sideLimit)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return 80f;
        forward.Normalize();
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);
        float best = 80f;
        for (int i = 0; i < ActiveCustomers.Count; i++)
        {
            CustomerAI customer = ActiveCustomers[i];
            if (customer == null) continue;
            if (!customer.IsEntering && !customer.IsLeaving) continue;
            Vector3 to = customer.transform.position - from;
            to.y = 0f;
            float along = Vector3.Dot(to, forward);
            if (along < 0.15f || along >= best) continue;
            if (Mathf.Abs(Vector3.Dot(to, side)) > sideLimit) continue;
            best = along;
        }
        foreach (IngredientCourier courier in IngredientCourier.ActiveCouriers)
        {
            if (courier == null) continue;
            Vector3 to = courier.transform.position - from;
            to.y = 0f;
            float along = Vector3.Dot(to, forward);
            if (along < 0f || along >= best) continue;
            if (Mathf.Abs(Vector3.Dot(to, side)) <= sideLimit) best = along;
        }
        return best;
    }
    public bool HasReservedSlot => hasQueueSlot;
    public Vector3 ReservedSlotPosition => queuedSlotPos;

    float standY;
    bool standYReady;
    static float cachedFloorY;
    static int cachedFloorFrame = -1;
    static readonly List<CustomerAI> ActiveCustomers = new List<CustomerAI>();

    void OnEnable()
    {
        if (!ActiveCustomers.Contains(this))
            ActiveCustomers.Add(this);
    }

    void OnDisable()
    {
        ActiveCustomers.Remove(this);
    }

    void Awake()
    {
        if (patienceMeter == null)
            patienceMeter = GetComponent<CustomerPatienceMeter>();
        if (orderLabel == null)
            orderLabel = GetComponent<CustomerOrderLabel>();
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        PartyCharacterAnimator.EnsureOn(gameObject);
        PartyCharacterRandomizer.EnsureOn(gameObject);
        HideRootCapsule();
        SnapFeetToFloor();
    }

    void Start()
    {
        SnapFeetToFloor();
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

        DetourAroundBus(route);

        while (route.Count > 0 && HorizontalDist(transform.position, route[0]) <= arrivalDistance)
            route.RemoveAt(0);

        phase = route.Count > 0 ? Phase.Entering : Phase.GoingToSlot;
        SnapFeetToFloor();

        // If we already have a queue slot (joined before path setup), start moving once entry is done.
        if (phase != Phase.Entering)
            ApplyQueueSlotMovement();
    }

    public void SetReturnSpawnPoint(Vector3 point)
    {
        returnSpawnPoint = point;
        hasReturnSpawnPoint = true;
    }

    public void SetOrder(CustomerOrder o)
    {
        order = o != null ? o.Clone() : new CustomerOrder();
        salePrice = order.GetSalePrice();
        if (ProductionManager.Instance != null)
            ProductionManager.Instance.RecordCustomerOrder(order);
        if (orderLabel != null)
        {
            orderLabel.EnsureHierarchy();
            if (orderLabel.labelRoot != null) orderLabel.labelRoot.SetActive(true);
        }
        RefreshOrderLabel();
    }

    public void ClearOrder()
    {
        order = null;
        salePrice = 0;
        if (orderLabel == null) orderLabel = GetComponent<CustomerOrderLabel>();
        if (orderLabel != null)
        {
            orderLabel.ClearDisplay();
            orderLabel.EnsureHierarchy();
            if (orderLabel.labelRoot != null) orderLabel.labelRoot.SetActive(false);
        }
    }

    public bool TryReceiveItem(ItemDefinition item)
    {
        if (item == null || order == null) return false;
        if (!order.TryRemoveOne(item)) return false;
        RefreshOrderLabel();
        return true;
    }

    /// <summary>
    /// Replaces this item's base contribution with the value calculated from its
    /// individual pickup-station age. This supports mixed-freshness orders.
    /// </summary>
    public void ApplyPickupSaleValue(ItemDefinition item, int adjustedValue)
    {
        if (item == null) return;
        salePrice = Mathf.Max(0, salePrice - Mathf.Max(0, item.price) + Mathf.Max(0, adjustedValue));
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
        waitingForPickup = false;
        if (pickupStation != null) pickupStation.LeavePickupQueue(this);
        pickupStation = null;
        leaving = true;
        leavingImpatient = true;
        BeginLeaveRoute(exit);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.RecordCustomerLost();
    }

    public void SetTargetRegister(Register r)
    {
        if (r != null && !r.IsPlacedRegister) return;
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
        ApplySlot(register, slotPos, front, pickup: false);
    }

    public void SetPickupSlot(Register register, Vector3 slotPos, bool front)
    {
        ApplySlot(register, slotPos, front, pickup: true);
    }

    public void SetPickupSlot(HeatLampStation station, Vector3 slotPos, bool front)
    {
        if (phase == Phase.Leaving) return;
        pickupStation = station;
        queuedSlotPos = slotPos;
        queuedIsFront = front;
        hasQueueSlot = true;
        isFront = front;
        waitingForPickup = true;
        if (phase != Phase.Entering)
            ApplyQueueSlotMovement();
    }

    public void BeginPickupJourney()
    {
        JoinNextPickupStation();
    }

    void JoinNextPickupStation()
    {
        if (pickupStation != null)
            pickupStation.LeavePickupQueue(this);
        pickupStation = null;

        if (order == null || order.GetTotalQuantity() <= 0)
        {
            if (reg != null) reg.CompleteServe(this, order);
            return;
        }

        // Pickup lines are shared waiting areas. Choose the least crowded area;
        // the customer's order can be fulfilled from inventory at any pass.
        HeatLampStation next = HeatLampStation.FindBestWaitingArea(transform.position);
        if (next == null)
            next = HeatLampStation.FindNearest(transform.position);
        if (next == null || !next.TryJoinPickupQueue(this))
        {
            hasQueueSlot = false;
            hasTarget = false;
            waitingForPickup = true;
        }
    }

    public void OnPickupStationUnavailable(HeatLampStation station)
    {
        if (pickupStation != station) return;
        pickupStation = null;
        hasQueueSlot = false;
        hasTarget = false;
        BeginPickupJourney();
    }

    void ApplySlot(Register register, Vector3 slotPos, bool front, bool pickup)
    {
        if (phase == Phase.Leaving) return;

        reg = register;
        queuedSlotPos = slotPos;
        queuedIsFront = front;
        hasQueueSlot = true;
        isFront = front;
        waitingForPickup = pickup;

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
        if (leaving) return;
        StopPatienceMeter();
        waitingForPickup = false;
        if (pickupStation != null) pickupStation.LeavePickupQueue(this);
        pickupStation = null;
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
        if (pickupStation != null) pickupStation.LeavePickupQueue(this);
        pickupStation = null;
        Destroy(gameObject);
    }

    void BeginLeaveRoute(Transform fallbackExit)
    {
        phase = Phase.Leaving;
        route.Clear();
        routeIndex = 0;
        gridPath.Clear();

        CustomerWallDoor exitDoor = CustomerWallDoor.FindExitDoor();
        if (exitDoor != null)
            exitDoor.AppendPassage(route, false);
        else if (exitPath != null && exitPath.Count > 0)
            exitPath.GetWorldPoints(route);
        else if (fallbackExit != null)
            route.Add(fallbackExit.position);
        else
            route.Add(transform.position + Vector3.forward * 8f);

        AppendReturnToSpawn(route);
        AppendCarReturn(route);
        DetourAroundBus(route);
    }

    void AppendReturnToSpawn(List<Vector3> into)
    {
        if (!hasReturnSpawnPoint || into == null) return;
        Vector3 from = into.Count > 0 ? into[into.Count - 1] : transform.position;
        returnSpawnPoint.y = from.y;
        var outdoor = new List<Vector3>();
        OutdoorCustomerPathfinder.Build(from, returnSpawnPoint, outdoor);
        for (int i = 1; i < outdoor.Count; i++)
            into.Add(outdoor[i]);
    }

    void AppendCarReturn(List<Vector3> into)
    {
        var ride = GetComponent<ParkedCarRide>();
        if (ride == null || !ride.TryGetReturnPoint(out Vector3 door)) return;
        door.y = transform.position.y;
        into.Add(ParkingLotDressing.PedestrianAislePoint(door));
        into.Add(door);
    }

    void Update()
    {
        if (!leaving && order != null && IsOrderFullyDelivered
            && phase != Phase.Entering)
        {
            FinishPickupAndLeave();
            return;
        }

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
            // Entry uses direct door waypoints; start a fresh grid path for the queue.
            gridPath.Clear();
            gridPathDestination = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            ApplyQueueSlotMovement();
            // If join happened but slot never arrived, ask register to refresh.
            if (!hasQueueSlot && reg != null)
                reg.RefreshQueueTargets();
            return;
        }

        // Door waypoints already provide panel clearance. Re-snapping each one
        // onto the grid can send the customer backwards between waypoints.
        if (MoveStraightTo(route[routeIndex]))
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

        if (waitingForPickup)
            TrySelfServeFromHeatLamp();

        if (leaving) return;
        if (MoveOnCustomerGrid(targetPos))
        {
            phase = Phase.Waiting;
            BeginQueueWait();
            if (waitingForPickup)
                TrySelfServeFromHeatLamp();
        }
    }

    void UpdateWaiting()
    {
        if (leaving) return;
        if (waitingForPickup && IsOrderFullyDelivered)
        {
            FinishPickupAndLeave();
            return;
        }
        if (waitingForPickup && pickupStation == null)
        {
            JoinNextPickupStation();
            return;
        }
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

        FaceLineFront();

        if (!patienceStarted)
            BeginQueueWait();

        // Stay in the register order line until the register finishes taking the order.
        // (Register / cashier moves them to the heat-lamp pickup line.)

        // Self-serve: grab matching food from the pass when at the front of pickup.
        if (waitingForPickup)
            TrySelfServeFromHeatLamp();
    }

    void FaceLineFront()
    {
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing == null) return;

        if (waitingForPickup)
        {
            HeatLampStation lamp = pickupStation;
            if (lamp != null)
                facing.FaceTowardAdjacentObject(lamp.gameObject, smooth: true);
            else if (reg != null)
                facing.FaceTowardAdjacentObject(reg.gameObject, smooth: true);
        }
        else if (reg != null)
        {
            facing.FaceTowardAdjacentObject(reg.gameObject, smooth: true);
        }
    }

    void TrySelfServeFromHeatLamp()
    {
        if (leaving) return;
        if (IsOrderFullyDelivered)
        {
            FinishPickupAndLeave();
            return;
        }

        if (reg == null || pickupStation == null) return;
        if (HorizontalDist(transform.position, queuedSlotPos) > 0.85f) return;

        if (!HeatLampStation.TryCustomerTakeAvailableItem(
                this, pickupStation, order, out ItemDefinition item,
                out int adjustedSaleValue)) return;
        if (!order.TryRemoveOne(item)) return;
        ApplyPickupSaleValue(item, adjustedSaleValue);

        RefreshOrderLabel();
        Sfx.Play(SfxId.ItemDelivered);

        if (IsOrderFullyDelivered)
            FinishPickupAndLeave();
        // Otherwise stay in this pickup slot. Remaining items may arrive at any
        // placed pickup station and can be collected from this same queue.
    }

    void FinishPickupAndLeave()
    {
        if (leaving) return;
        if (pickupStation != null)
        {
            pickupStation.LeavePickupQueue(this);
            pickupStation = null;
        }
        hasQueueSlot = false;
        hasTarget = false;
        isFront = false;
        Transform exit = reg != null ? reg.storeExit : null;
        if (reg != null)
            reg.CompleteServe(this, order);
        // The register only starts the walk home when this customer is still
        // in its queue. A finished order must leave either way.
        if (!leaving)
            OnServed(exit);
    }

    float leaveStuckTime;

    void UpdateLeaving()
    {
        if (routeIndex >= route.Count)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 before = transform.position;
        if (MoveOnCustomerGrid(route[routeIndex]))
        {
            routeIndex++;
            leaveStuckTime = 0f;
            gridPath.Clear();
            return;
        }

        if (HorizontalDist(before, transform.position) < 0.03f)
            leaveStuckTime += Time.deltaTime;
        else
            leaveStuckTime = 0f;

        // A bad doorway point or a neighbor in the corner must not trap the group.
        if (leaveStuckTime < 1.6f) return;
        routeIndex++;
        leaveStuckTime = 0f;
        gridPath.Clear();
    }

    void DetourAroundBus(List<Vector3> points)
    {
        if (points == null || points.Count < 2) return;
        if (!ParkingLotDressing.TryGetBusBounds(out Bounds bus)) return;

        Bounds pad = bus;
        pad.Expand(0.8f);
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (!SegmentCrossesBus(points[i], points[i + 1], pad)) continue;
            float y = points[i].y;
            float nextX = points[i + 1].x;
            bool goNorth = (points[i].z + points[i + 1].z) * 0.5f >= bus.center.z;
            float sideZ = goNorth ? pad.max.z + 0.75f : pad.min.z - 0.75f;
            points.Insert(i + 1, new Vector3(points[i].x, y, sideZ));
            points.Insert(i + 2, new Vector3(nextX, y, sideZ));
            i += 2;
        }
    }

    static bool SegmentCrossesBus(Vector3 a, Vector3 b, Bounds pad)
    {
        Vector3 aa = new Vector3(a.x, pad.center.y, a.z);
        Vector3 bb = new Vector3(b.x, pad.center.y, b.z);
        if (pad.Contains(aa) || pad.Contains(bb))
            return true;
        for (int i = 1; i < 8; i++)
        {
            if (pad.Contains(Vector3.Lerp(aa, bb, i / 8f)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mirrors worker movement on the customer grid: visit orthogonally adjacent tile
    /// centers, then take the final short step to the exact queue or door position.
    /// Outside portions of entry and exit routes remain direct.
    /// </summary>
    bool MoveOnCustomerGrid(Vector3 target)
    {
        SnapFeetToFloor();
        Vector3 exactTarget = target;
        exactTarget.y = transform.position.y;

        if (HorizontalDist(transform.position, exactTarget) <= arrivalDistance)
        {
            SnapXZ(exactTarget);
            gridPath.Clear();
            gridPathDestination = exactTarget;
            return true;
        }

        if (!TryGetCustomerGrid(out Bounds bounds, out float originX, out float originZ,
                out float cellSize, out int width, out int height)
            || !ContainsXZ(bounds, transform.position)
            || !ContainsXZ(bounds, exactTarget))
        {
            gridPath.Clear();
            gridPathDestination = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            return MoveStraightTo(exactTarget);
        }

        if (gridPath.Count == 0 || HorizontalDist(gridPathDestination, exactTarget) > 0.01f)
        {
            gridPathDestination = exactTarget;
            BuildCustomerGridPath(transform.position, exactTarget, originX, originZ,
                cellSize, width, height);
        }

        if (gridPath.Count == 0)
            return MoveStraightTo(exactTarget);

        if (!MoveStraightTo(gridPath[0]))
            return false;

        gridPath.RemoveAt(0);
        return gridPath.Count == 0;
    }

    void BuildCustomerGridPath(Vector3 from, Vector3 target, float originX, float originZ,
        float cellSize, int width, int height)
    {
        gridPath.Clear();
        int startX = Mathf.Clamp(Mathf.FloorToInt((from.x - originX) / cellSize), 0, width - 1);
        int startZ = Mathf.Clamp(Mathf.FloorToInt((from.z - originZ) / cellSize), 0, height - 1);
        int targetX = Mathf.Clamp(Mathf.FloorToInt((target.x - originX) / cellSize), 0, width - 1);
        int targetZ = Mathf.Clamp(Mathf.FloorToInt((target.z - originZ) / cellSize), 0, height - 1);

        Vector3 Center(int x, int z) => new Vector3(
            originX + (x + 0.5f) * cellSize,
            transform.position.y,
            originZ + (z + 0.5f) * cellSize);

        Vector3 startCenter = Center(startX, startZ);
        if (HorizontalDist(from, startCenter) > arrivalDistance)
            gridPath.Add(startCenter);

        int x = startX;
        int z = startZ;
        while (x != targetX || z != targetZ)
        {
            if (x != targetX) x += targetX > x ? 1 : -1;
            if (z != targetZ) z += targetZ > z ? 1 : -1;
            gridPath.Add(Center(x, z));
        }

        if (gridPath.Count == 0 || HorizontalDist(gridPath[gridPath.Count - 1], target) > arrivalDistance)
            gridPath.Add(target);
        else
            gridPath[gridPath.Count - 1] = target;
    }

    bool TryGetCustomerGrid(out Bounds bounds, out float originX, out float originZ,
        out float cellSize, out int width, out int height)
    {
        bounds = default;
        originX = originZ = 0f;
        cellSize = grid != null ? Mathf.Max(0.01f, grid.cellSize) : 1f;
        width = height = 0;

        if (customerFloorRenderer == null)
        {
            GameObject floor = GameObject.Find("CustomerFloor");
            if (floor != null)
                customerFloorRenderer = floor.GetComponentInChildren<Renderer>();
        }
        if (customerFloorRenderer == null) return false;

        bounds = customerFloorRenderer.bounds;
        Vector3 sharedOrigin = grid != null ? grid.Origin : bounds.min;
        originX = sharedOrigin.x + Mathf.Round((bounds.min.x - sharedOrigin.x) / cellSize) * cellSize;
        originZ = sharedOrigin.z + Mathf.Round((bounds.min.z - sharedOrigin.z) / cellSize) * cellSize;
        width = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize));
        height = Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize));
        return true;
    }

    static bool ContainsXZ(Bounds bounds, Vector3 point)
    {
        const float edgeTolerance = 0.05f;
        return point.x >= bounds.min.x - edgeTolerance
            && point.x <= bounds.max.x + edgeTolerance
            && point.z >= bounds.min.z - edgeTolerance
            && point.z <= bounds.max.z + edgeTolerance;
    }

    bool MoveStraightTo(Vector3 target)
    {
        SnapFeetToFloor();
        Vector3 pos = transform.position;
        target.y = pos.y;
        if (HorizontalDist(pos, target) <= moveSpeed * Time.deltaTime)
        {
            SnapXZ(target);
            return true;
        }

        if (IsBlockedByOtherCustomer(target))
            return false;

        transform.position = Vector3.MoveTowards(pos, target, moveSpeed * Time.deltaTime);
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.FaceMovementToward(target, smooth: true);
        return HorizontalDist(transform.position, target) <= arrivalDistance;
    }

    bool IsBlockedByOtherCustomer(Vector3 dest)
    {
        // Leaving customers have to clear the door. Holding them for the person
        // ahead piles the whole group in the corner.
        if (phase == Phase.Leaving) return false;
        // Register and pickup slots are one tile apart in a straight file.
        // Always finish the walk onto your own tile. Only refuse a step that
        // would land on someone who is already standing on a different tile.
        if (hasQueueSlot && StepApproachesOwnSlot(dest))
        {
            for (int i = 0; i < ActiveCustomers.Count; i++)
            {
                CustomerAI other = ActiveCustomers[i];
                if (other == null || other == this || !other.hasQueueSlot) continue;
                // The order line stands between the register and the pickup.
                // Walking to the pickup station has to pass those people.
                if (waitingForPickup && !other.waitingForPickup) continue;
                if (HorizontalDist(other.queuedSlotPos, queuedSlotPos) < 0.35f) continue;
                bool otherHome = HorizontalDist(other.transform.position, other.queuedSlotPos) <= 0.3f;
                if (!otherHome) continue;
                if (HorizontalDist(dest, other.transform.position) < 0.45f)
                    return true;
            }
            return false;
        }

        const float followDistance = 0.82f;
        Vector3 move = dest - transform.position;
        move.y = 0f;
        if (move.sqrMagnitude < 0.0001f) return false;
        Vector3 moveDir = move.normalized;

        for (int i = 0; i < ActiveCustomers.Count; i++)
        {
            CustomerAI other = ActiveCustomers[i];
            if (other == null || other == this) continue;
            if (waitingForPickup && !other.waitingForPickup) continue;

            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0f;
            float gap = toOther.magnitude;
            if (gap >= followDistance) continue;
            if (gap < 0.001f) return GetInstanceID() > other.GetInstanceID();

            float ahead = Vector3.Dot(moveDir, toOther / gap);
            if (ahead > 0.35f)
                return true;
        }

        return false;
    }

    bool StepApproachesOwnSlot(Vector3 dest)
    {
        float now = HorizontalDist(transform.position, queuedSlotPos);
        float next = HorizontalDist(dest, queuedSlotPos);
        return next <= 0.25f || next < now - 0.02f;
    }

    void SnapXZ(Vector3 world)
    {
        Vector3 p = transform.position;
        p.x = world.x;
        p.z = world.z;
        transform.position = p;
        SnapFeetToFloor();
    }

    void SnapFeetToFloor()
    {
        if (standYReady)
        {
            Vector3 p = transform.position;
            if (Mathf.Abs(p.y - standY) > 0.001f)
            {
                p.y = standY;
                transform.position = p;
            }
            return;
        }

        float floorY = ResolveCustomerFloorY();
        if (!TryGetVisualBounds(out Bounds bounds))
            return;

        standY = transform.position.y + (floorY - bounds.min.y);
        Vector3 pos = transform.position;
        pos.y = standY;
        transform.position = pos;
        if (GetComponentInChildren<SkinnedMeshRenderer>() != null)
            standYReady = true;
    }

    void HideRootCapsule()
    {
        var mesh = GetComponent<MeshRenderer>();
        if (mesh != null && GetComponentInChildren<SkinnedMeshRenderer>() != null)
            mesh.enabled = false;
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
            capsule.enabled = false;
    }

    float ResolveCustomerFloorY()
    {
        if (cachedFloorFrame == Time.frameCount)
            return cachedFloorY;

        cachedFloorFrame = Time.frameCount;
        GameObject floor = GameObject.Find("CustomerFloor");
        if (floor != null)
        {
            Renderer renderer = floor.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                cachedFloorY = renderer.bounds.max.y;
                return cachedFloorY;
            }
        }

        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        cachedFloorY = grid != null ? grid.Origin.y : 0f;
        return cachedFloorY;
    }

    bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;
        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>();
        bool found = false;
        for (int i = 0; i < skinned.Length; i++)
        {
            var renderer = skinned[i];
            if (renderer == null || !renderer.enabled) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        if (found) return true;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.GetComponentInParent<Canvas>() != null) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    static float HorizontalDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
