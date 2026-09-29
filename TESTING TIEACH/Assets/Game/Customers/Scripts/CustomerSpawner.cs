using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    public GameObject customerPrefab;
    [Tooltip("Fallback spawn position if no entry path / waypoints are set.")]
    public Transform spawnPoint;

    [Header("Entry path (pick ONE)")]
    [Tooltip("Preferred: assign a CustomerPath (Game → Create Customer Entry Path).")]
    public CustomerPath entryPath;
    [Tooltip("Or drag waypoint Transforms here in order (Outside → Door → Inside).")]
    public List<Transform> entryWaypoints = new List<Transform>();

    [Header("Exit path (optional)")]
    public CustomerPath exitPath;
    public GridManager grid;

    [Header("Arrivals (Poisson process)")]
    [Tooltip("Mean customers per real minute on Milestone 1. A shift is 12 real minutes, so 2.4 is about 29 expected arrivals. Later milestones add to this. Gaps stay random, but they are capped so the morning is not empty.")]
    [Min(0.05f)]
    public float milestoneOneCustomersPerMinute = 2.4f;

    [Tooltip("Added to the mean arrival rate for each numbered milestone after the first.")]
    [Min(0f)]
    public float extraCustomersPerMinutePerMilestone = 0.85f;

    [Tooltip("Rush hours (Milestone 2+, lunch and dinner) multiply the mean arrival rate.")]
    [Min(1f)]
    public float rushArrivalMultiplier = 2.2f;

    [Tooltip("Assign to give customers random burger / fries / drink combos")]
    public CustomerOrderConfig orderConfig;

    public List<Register> registers = new List<Register>();

    float waitRemaining;
    float scheduledRatePerSecond;
    bool arrivalScheduled;
    bool waitingForOpening = true;
    bool clockHooked;
    readonly List<Vector3> entryPointsBuffer = new List<Vector3>();
    readonly List<Transform> customerSpawnPoints = new List<Transform>();
    int lastCustomerSpawnPoint = -1;

    /// <summary>
    /// Expected arrivals per real minute. Milestone 1 uses the base rate;
    /// each later numbered milestone adds to it. Rush hours scale the mean.
    /// </summary>
    public float CustomersPerMinute => MeanCustomersPerMinute();

    float MeanCustomersPerMinute()
    {
        int stage = MilestoneFeatures.HighestReachedNumberedStage();
        int stepsPastFirst = Mathf.Max(0, stage - 1);
        float rate = milestoneOneCustomersPerMinute + stepsPastFirst * extraCustomersPerMinutePerMilestone;
        var clock = GameTimeManager.Instance;
        if (clock != null && clock.IsRushHour)
            rate *= rushArrivalMultiplier;
        return Mathf.Max(0.05f, rate);
    }

    void ScheduleNextArrival(float customersPerSecond)
    {
        float rate = Mathf.Max(0.0001f, customersPerSecond);
        if (waitingForOpening)
        {
            // First customer of the shift: always during the opening hour
            // (about 10:12–10:40), never a multi-hour empty morning.
            waitRemaining = UnityEngine.Random.Range(12f, 38f);
        }
        else
        {
            // Trimmed exponential. Mean stays near 1/λ, but a single gap
            // cannot run from open until early afternoon.
            float u = UnityEngine.Random.Range(0.14f, 0.86f);
            float gap = -Mathf.Log(u) / rate;
            waitRemaining = Mathf.Clamp(gap, 20f, 65f);
        }
        scheduledRatePerSecond = rate;
        arrivalScheduled = true;
    }

    void HandleDayStarted()
    {
        waitingForOpening = true;
        arrivalScheduled = false;
    }

    void EnsureDayHook()
    {
        if (clockHooked) return;
        GameTimeManager clock = GameTimeManager.Instance;
        if (clock == null) return;
        clock.OnDayStarted += HandleDayStarted;
        clockHooked = true;
    }

    void Awake()
    {
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        ResolveEntryPathIfNeeded();
    }

    void OnValidate()
    {
        ResolveEntryPathIfNeeded();
    }

    void ResolveEntryPathIfNeeded()
    {
        if (entryPath != null) return;
        if (HasInlineWaypoints()) return;

        var paths = FindObjectsByType<CustomerPath>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == null) continue;
            string n = paths[i].gameObject.name;
            if (n.IndexOf("Exit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            entryPath = paths[i];
            return;
        }
        if (paths.Length > 0)
            entryPath = paths[0];
    }

    bool HasInlineWaypoints()
    {
        if (entryWaypoints == null) return false;
        for (int i = 0; i < entryWaypoints.Count; i++)
        {
            if (entryWaypoints[i] != null) return true;
        }
        return false;
    }

    void OnDisable()
    {
        if (!clockHooked || GameTimeManager.Instance == null) return;
        GameTimeManager.Instance.OnDayStarted -= HandleDayStarted;
        clockHooked = false;
    }

    void Update()
    {
        EnsureDayHook();
        if (OnboardingTutorial.BlocksAutoCustomers)
        {
            arrivalScheduled = false;
            return;
        }

        float rate = MeanCustomersPerMinute() / 60f;
        if (!arrivalScheduled || scheduledRatePerSecond <= 0f)
            ScheduleNextArrival(rate);
        else if (!waitingForOpening && Mathf.Abs(rate - scheduledRatePerSecond) > 0.0001f)
        {
            // Piecewise-constant Poisson process: stretch or compress the
            // remaining wait when the rate changes (rush hour, new milestone).
            waitRemaining *= scheduledRatePerSecond / rate;
            waitRemaining = Mathf.Clamp(waitRemaining, 8f, 65f);
            scheduledRatePerSecond = rate;
        }

        waitRemaining -= Time.deltaTime;
        if (waitRemaining > 0f) return;

        if (!TrySpawn(false))
        {
            // A missing register should retry soon, not skip the rest of the morning.
            waitRemaining = 5f;
            arrivalScheduled = true;
            return;
        }

        waitingForOpening = false;
        ScheduleNextArrival(MeanCustomersPerMinute() / 60f);
    }

    public string LastSpawnError { get; private set; }

    public bool SpawnNow(bool force = false)
    {
        if (!TrySpawn(force)) return false;
        ScheduleNextArrival(MeanCustomersPerMinute() / 60f);
        return true;
    }

    /// <summary>
    /// Clears the tutorial arrival block and schedules a visible customer promptly.
    /// </summary>
    public void ResumeAfterTutorial()
    {
        waitingForOpening = false;
        scheduledRatePerSecond = MeanCustomersPerMinute() / 60f;
        waitRemaining = 3f;
        arrivalScheduled = true;
    }

    public bool SpawnTutorialCustomer()
    {
        return OnboardingTutorial.AllowsPracticeCustomer && TrySpawn(false, true);
    }

    bool TrySpawn(bool force = false, bool tutorialPractice = false)
    {
        LastSpawnError = null;
        if (OnboardingTutorial.BlocksAutoCustomers && !tutorialPractice)
        {
            LastSpawnError = "Customers arrive during the tutorial customer step.";
            return false;
        }
        if (customerPrefab == null)
        {
            LastSpawnError = "No customer prefab on CustomerSpawner";
            return false;
        }
        if (!force && orderConfig != null && !orderConfig.HasEnabledItems)
        {
            LastSpawnError = "No menu items enabled";
            return false;
        }
        ResolveEntryPathIfNeeded();

        Register r = GetBestRegister();
        if (r == null && force)
            r = GetAnyRegister();
        if (r == null && !force)
        {
            LastSpawnError = "Need an open register";
            return false;
        }

        return SpawnOnFoot(r, false, Vector3.zero, null);
    }

    /// <summary>
    /// Spawns one customer at a vehicle door. When <paramref name="rideHome"/> is set,
    /// they walk back to that car after the visit and the car leaves.
    /// </summary>
    public bool SpawnArrivingCustomer(Vector3 doorWorld, LotArrivalVehicle rideHome)
    {
        if (OnboardingTutorial.BlocksAutoCustomers) return false;
        if (customerPrefab == null) return false;
        if (orderConfig != null && !orderConfig.HasEnabledItems) return false;

        Register r = GetBestRegister();
        if (r == null)
            r = GetAnyRegister();
        if (r == null) return false;

        return SpawnOnFoot(r, true, doorWorld, rideHome);
    }

    bool SpawnOnFoot(Register r, bool fromVehicle, Vector3 doorWorld, LotArrivalVehicle rideHome)
    {
        BuildEntryPoints(entryPointsBuffer, r, !fromVehicle);
        Vector3 spawnPos;
        if (fromVehicle)
        {
            spawnPos = doorWorld;
            if (entryPointsBuffer.Count > 0)
                spawnPos.y = entryPointsBuffer[0].y;
            entryPointsBuffer.Insert(0, spawnPos);
            if (rideHome != null && !rideHome.HoldsBusBay)
                entryPointsBuffer.Insert(1, ParkingLotDressing.PedestrianAislePoint(spawnPos));
        }
        else
        {
            spawnPos = entryPointsBuffer.Count > 0
                ? entryPointsBuffer[0]
                : (spawnPoint != null ? spawnPoint.position : transform.position);
        }

        var c = Instantiate(customerPrefab, spawnPos, Quaternion.identity);
        if (rideHome != null)
        {
            var ride = c.AddComponent<ParkedCarRide>();
            ride.Bind(rideHome, spawnPos);
        }

        TutorialVoiceEvents.Raise(TutorialVoiceEventId.FirstCustomerArrived);
        var ai = c.GetComponent<CustomerAI>();
        if (ai == null) return true;

        ai.ClearOrder();
        ai.BeginEntryRoute(entryPointsBuffer, grid, exitPath);
        if (!fromVehicle)
            ai.SetReturnSpawnPoint(spawnPos);
        if (r != null)
            ai.SetTargetRegister(r);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.RecordCustomerVisit();

        if (entryPointsBuffer.Count < 2)
        {
            Debug.LogWarning(
                "CustomerSpawner: Entry path needs at least 2 waypoints (outside → inside). " +
                "Customers will go to the queue after spawn.",
                this);
        }

        return true;
    }

    void BuildEntryPoints(List<Vector3> into, Register register, bool includeSceneSpawnPoint)
    {
        into.Clear();
        Transform sceneSpawn = includeSceneSpawnPoint ? PickCustomerSpawnPoint() : null;

        CustomerWallDoor entrance = CustomerWallDoor.FindEntryDoor();
        if (entrance != null)
        {
            entrance.AppendPassage(into, true);
            Vector3 elbowTarget = register != null
                ? register.GetFrontQueueWorldPosition()
                : GetCustomerFloorSouthHint(into);
            entrance.AppendEastEntryElbow(into, elbowTarget);
            PrependOutdoorRoute(into, sceneSpawn);
            return;
        }

        if (HasInlineWaypoints())
        {
            for (int i = 0; i < entryWaypoints.Count; i++)
            {
                if (entryWaypoints[i] != null)
                    into.Add(entryWaypoints[i].position);
            }
            PrependOutdoorRoute(into, sceneSpawn);
            return;
        }

        if (entryPath != null && entryPath.Count > 0)
        {
            entryPath.GetWorldPoints(into);
            PrependOutdoorRoute(into, sceneSpawn);
            return;
        }

        if (sceneSpawn != null)
            into.Add(sceneSpawn.position);
        else if (spawnPoint != null)
            into.Add(spawnPoint.position);
    }

    Transform PickCustomerSpawnPoint()
    {
        customerSpawnPoints.RemoveAll(point => point == null || !point.gameObject.activeInHierarchy);
        if (customerSpawnPoints.Count == 0)
        {
            Transform[] sceneTransforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < sceneTransforms.Length; i++)
            {
                Transform point = sceneTransforms[i];
                if (point == null || !point.name.StartsWith(
                    "CustomerSpawnPoint", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                customerSpawnPoints.Add(point);
            }
            customerSpawnPoints.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        if (customerSpawnPoints.Count == 0) return spawnPoint;
        int index = Random.Range(0, customerSpawnPoints.Count);
        if (customerSpawnPoints.Count > 1 && index == lastCustomerSpawnPoint)
            index = (index + Random.Range(1, customerSpawnPoints.Count)) % customerSpawnPoints.Count;
        lastCustomerSpawnPoint = index;
        return customerSpawnPoints[index];
    }

    static void PrependOutdoorRoute(List<Vector3> route, Transform point)
    {
        if (route == null || point == null) return;
        Vector3 position = point.position;
        if (route.Count == 0)
        {
            route.Add(position);
            return;
        }
        if (Vector3.SqrMagnitude(route[0] - position) <= 0.01f) return;

        var outdoor = new List<Vector3>();
        OutdoorCustomerPathfinder.Build(position, route[0], outdoor);
        // The final outdoor point is already the first door-passage point.
        for (int i = outdoor.Count - 2; i >= 0; i--)
            route.Insert(0, outdoor[i]);
    }

    static Vector3 GetCustomerFloorSouthHint(List<Vector3> into)
    {
        Vector3 last = into != null && into.Count > 0 ? into[into.Count - 1] : Vector3.zero;
        GameObject floor = GameObject.Find("CustomerFloor");
        Renderer renderer = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        if (renderer == null) return last;
        Bounds bounds = renderer.bounds;
        float destZ = Mathf.Lerp(last.z, bounds.min.z + 1.2f, 0.85f);
        destZ = Mathf.Clamp(destZ, bounds.min.z + 0.6f, bounds.max.z - 0.6f);
        return new Vector3(last.x, last.y, destZ);
    }

    Register GetBestRegister()
    {
        RefreshPlacedRegisters();
        Register best = null;
        int bestCount = int.MaxValue;

        foreach (var r in registers)
        {
            if (r == null) continue;
            if (!r.HasSpace()) continue;

            int count = r.QueueCount;
            if (count < bestCount)
            {
                bestCount = count;
                best = r;
            }
        }

        return best;
    }

    Register GetAnyRegister()
    {
        RefreshPlacedRegisters();
        foreach (var r in registers)
        {
            if (r != null && r.IsPlacedRegister)
                return r;
        }
        return null;
    }

    void RefreshPlacedRegisters()
    {
        registers.RemoveAll(r => r == null || !r.IsPlacedRegister);
        Register[] placed = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Register register in placed)
            if (register != null && register.IsPlacedRegister && !registers.Contains(register))
                registers.Add(register);
    }
}

/// <summary>
/// Builds short outdoor routes around parked vehicles. This is intentionally
/// separate from the customer-floor grid, which only covers the restaurant.
/// </summary>
public static class OutdoorCustomerPathfinder
{
    const float CellSize = 0.8f;
    const float SearchMargin = 5f;
    const float VehicleClearance = 0.85f;

    public static void Build(Vector3 start, Vector3 goal, List<Vector3> into)
    {
        if (into == null) return;
        into.Clear();
        into.Add(start);

        List<Bounds> obstacles = CollectVehicleBounds(start, goal);
        if (obstacles.Count == 0 || SegmentClear(start, goal, obstacles))
        {
            into.Add(goal);
            return;
        }

        float minX = Mathf.Min(start.x, goal.x) - SearchMargin;
        float maxX = Mathf.Max(start.x, goal.x) + SearchMargin;
        float minZ = Mathf.Min(start.z, goal.z) - SearchMargin;
        float maxZ = Mathf.Max(start.z, goal.z) + SearchMargin;
        int width = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / CellSize) + 1, 2, 160);
        int height = Mathf.Clamp(Mathf.CeilToInt((maxZ - minZ) / CellSize) + 1, 2, 160);
        int total = width * height;

        int ToIndex(int x, int z) => z * width + x;
        Vector3 ToWorld(int index)
        {
            int x = index % width;
            int z = index / width;
            return new Vector3(minX + x * CellSize, start.y, minZ + z * CellSize);
        }
        int ToCell(Vector3 point)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((point.x - minX) / CellSize), 0, width - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((point.z - minZ) / CellSize), 0, height - 1);
            return ToIndex(x, z);
        }

        int startIndex = ToCell(start);
        int goalIndex = ToCell(goal);
        var blocked = new bool[total];
        for (int i = 0; i < total; i++)
            blocked[i] = InsideAny(ToWorld(i), obstacles);
        blocked[startIndex] = false;
        blocked[goalIndex] = false;

        var cost = new float[total];
        var parent = new int[total];
        var closed = new bool[total];
        for (int i = 0; i < total; i++)
        {
            cost[i] = float.PositiveInfinity;
            parent[i] = -1;
        }

        var open = new List<int> { startIndex };
        cost[startIndex] = 0f;
        int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dz = { -1, -1, -1, 0, 0, 1, 1, 1 };
        bool found = false;
        while (open.Count > 0)
        {
            int bestAt = 0;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < open.Count; i++)
            {
                Vector3 point = ToWorld(open[i]);
                float estimate = cost[open[i]] + Mathf.Abs(point.x - goal.x) + Mathf.Abs(point.z - goal.z);
                if (estimate >= bestScore) continue;
                bestScore = estimate;
                bestAt = i;
            }

            int current = open[bestAt];
            open.RemoveAt(bestAt);
            if (closed[current]) continue;
            closed[current] = true;
            if (current == goalIndex) { found = true; break; }

            int cx = current % width;
            int cz = current / width;
            for (int n = 0; n < dx.Length; n++)
            {
                int nx = cx + dx[n];
                int nz = cz + dz[n];
                if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;
                int next = ToIndex(nx, nz);
                if (blocked[next] || closed[next]) continue;
                if (dx[n] != 0 && dz[n] != 0
                    && (blocked[ToIndex(cx + dx[n], cz)] || blocked[ToIndex(cx, cz + dz[n])]))
                    continue;
                float nextCost = cost[current] + (dx[n] == 0 || dz[n] == 0 ? 1f : 1.4142f);
                if (nextCost >= cost[next]) continue;
                cost[next] = nextCost;
                parent[next] = current;
                open.Add(next);
            }
        }

        if (!found)
        {
            into.Add(goal);
            return;
        }

        var raw = new List<Vector3>();
        for (int at = goalIndex; at >= 0 && at != startIndex; at = parent[at])
        {
            raw.Add(ToWorld(at));
            if (parent[at] < 0) break;
        }
        raw.Reverse();
        raw.Add(goal);

        Vector3 anchor = start;
        int cursor = 0;
        while (cursor < raw.Count)
        {
            int furthest = cursor;
            for (int i = cursor; i < raw.Count; i++)
            {
                if (!SegmentClear(anchor, raw[i], obstacles)) break;
                furthest = i;
            }
            Vector3 waypoint = raw[furthest];
            if ((waypoint - into[into.Count - 1]).sqrMagnitude > 0.01f)
                into.Add(waypoint);
            anchor = waypoint;
            cursor = furthest + 1;
        }
        if ((goal - into[into.Count - 1]).sqrMagnitude > 0.01f)
            into.Add(goal);
    }

    static List<Bounds> CollectVehicleBounds(Vector3 start, Vector3 goal)
    {
        var obstacles = new List<Bounds>();
        Bounds search = new Bounds((start + goal) * 0.5f,
            new Vector3(Mathf.Abs(goal.x - start.x) + SearchMargin * 2f, 20f,
                Mathf.Abs(goal.z - start.z) + SearchMargin * 2f));

        GameObject vehicles = GameObject.Find("Vehicles");
        if (vehicles != null)
        {
            for (int i = 0; i < vehicles.transform.childCount; i++)
                AddRootBounds(vehicles.transform.GetChild(i), search, obstacles);
        }
        foreach (DeliveryVan van in Object.FindObjectsByType<DeliveryVan>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (van != null) AddRootBounds(van.transform, search, obstacles);
        foreach (LotArrivalVehicle car in Object.FindObjectsByType<LotArrivalVehicle>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (car != null) AddRootBounds(car.transform, search, obstacles);
        return obstacles;
    }

    static void AddRootBounds(Transform root, Bounds search, List<Bounds> into)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
        bounds.Expand(new Vector3(VehicleClearance * 2f, 0f, VehicleClearance * 2f));
        if (IntersectsXZ(bounds, search)) into.Add(bounds);
    }

    static bool IntersectsXZ(Bounds a, Bounds b) =>
        a.max.x >= b.min.x && a.min.x <= b.max.x && a.max.z >= b.min.z && a.min.z <= b.max.z;

    static bool InsideAny(Vector3 point, List<Bounds> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            Bounds bounds = obstacles[i];
            if (point.x >= bounds.min.x && point.x <= bounds.max.x
                && point.z >= bounds.min.z && point.z <= bounds.max.z)
                return true;
        }
        return false;
    }

    static bool SegmentClear(Vector3 a, Vector3 b, List<Bounds> obstacles)
    {
        float distance = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        int samples = Mathf.Max(2, Mathf.CeilToInt(distance / 0.3f));
        for (int step = 0; step <= samples; step++)
        {
            Vector3 point = Vector3.Lerp(a, b, step / (float)samples);
            if (InsideAny(point, obstacles)) return false;
        }
        return true;
    }
}
