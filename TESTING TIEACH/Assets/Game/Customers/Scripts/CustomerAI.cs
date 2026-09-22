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
    bool waitingForPickup;
    bool waitingAtDesignatedArea;
    bool hasWaitAreaReservation;
    HeatLampStation pickupStation;
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

    float standY;
    bool standYReady;
    static float cachedFloorY;
    static int cachedFloorFrame = -1;

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

        while (route.Count > 0 && HorizontalDist(transform.position, route[0]) <= arrivalDistance)
            route.RemoveAt(0);

        phase = route.Count > 0 ? Phase.Entering : Phase.GoingToSlot;
        SnapFeetToFloor();

        // If we already have a queue slot (joined before path setup), start moving once entry is done.
        if (phase != Phase.Entering)
            ApplyQueueSlotMovement();
    }

    public void SetOrder(CustomerOrder o)
    {
        order = o != null ? o.Clone() : new CustomerOrder();
        salePrice = order.GetSalePrice();
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
        ReleaseWaitAreaReservation();
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
        CustomerWaitAreaManager areas = CustomerWaitAreaManager.Instance;
        if (areas != null && areas.TryReserveNearestWaitPosition(this, transform.position, out Vector3 waitPosition))
        {
            // Ordered customers wait on the customer floor, not in front of a pass.
            pickupStation = null;
            waitingAtDesignatedArea = true;
            hasWaitAreaReservation = true;
            waitingForPickup = false;
            queuedSlotPos = waitPosition;
            queuedIsFront = false;
            hasQueueSlot = true;
            ApplyQueueSlotMovement();
            return;
        }
        JoinNextPickupStation();
    }

    void JoinNextPickupStation()
    {
        if (pickupStation != null)
            pickupStation.LeavePickupQueue(this);
        pickupStation = null;

        ItemDefinition nextItem = order != null ? order.PrimaryItem : null;
        if (nextItem == null)
        {
            if (reg != null) reg.CompleteServe(this, order);
            return;
        }

        HeatLampStation next = HeatLampStation.FindBestPickupFor(nextItem, transform.position);
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
        ReleaseWaitAreaReservation();
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

        CustomerWallDoor exitDoor = CustomerWallDoor.FindRandomDoor(CustomerWallDoor.DoorRole.Exit);
        if (exitDoor == null)
            exitDoor = CustomerWallDoor.FindRandomDoor(CustomerWallDoor.DoorRole.Entrance);
        if (exitDoor != null)
            exitDoor.AppendPassage(route, false);
        else if (exitPath != null && exitPath.Count > 0)
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

        // Keep a wait tile reserved until this customer has begun leaving it.
        if (hasWaitAreaReservation && !waitingAtDesignatedArea && HorizontalDist(transform.position, targetPos) > 0.6f)
            ReleaseWaitAreaReservation();

        // Direct move to the register's queue point so customers line up correctly.
        if (MoveStraightTo(targetPos))
        {
            phase = Phase.Waiting;
            BeginQueueWait();
        }
    }

    void UpdateWaiting()
    {
        if (waitingAtDesignatedArea)
        {
            ItemDefinition nextItem = order != null ? order.PrimaryItem : null;
            HeatLampStation ready = HeatLampStation.FindReadyPickupFor(nextItem, transform.position);
            if (ready != null && ready.TryJoinPickupQueue(this))
            {
                waitingAtDesignatedArea = false;
            }
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
        // No interaction tiles / quads — front of the pickup line can take a ready order.
        if (reg == null || pickupStation == null || leaving || !isFront) return;
        if (IsOrderFullyDelivered)
        {
            reg.CompleteServe(this, order);
            return;
        }

        ItemDefinition item = order != null ? order.PrimaryItem : null;
        if (item == null) return;
        if (!pickupStation.TryCustomerTakeSingleItem(item)) return;
        if (!order.TryRemoveOne(item)) return;

        RefreshOrderLabel();
        Sfx.Play(SfxId.ItemDelivered);
        pickupStation.LeavePickupQueue(this);
        pickupStation = null;
        if (IsOrderFullyDelivered)
            reg.CompleteServe(this, order);
        else
            BeginPickupJourney();
    }

    void ReleaseWaitAreaReservation()
    {
        if (!hasWaitAreaReservation) return;
        CustomerWaitAreaManager.Instance?.Release(this);
        hasWaitAreaReservation = false;
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
        SnapFeetToFloor();
        Vector3 pos = transform.position;
        target.y = pos.y;
        if (HorizontalDist(pos, target) <= arrivalDistance)
        {
            SnapXZ(target);
            return true;
        }

        transform.position = Vector3.MoveTowards(pos, target, moveSpeed * Time.deltaTime);
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.FaceTowardAdjacent(target, smooth: true);
        return HorizontalDist(transform.position, target) <= arrivalDistance;
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
