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
    [Tooltip("Mean customers per real minute on Milestone 1. A shift is 12 real minutes, so 0.8 ≈ 10 expected arrivals. Later milestones add to this. Gaps stay random, but they are capped so the morning is not empty.")]
    [Min(0.05f)]
    public float milestoneOneCustomersPerMinute = 0.8f;

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

        if (!TrySpawn(false, true))
        {
            // A full lot or a missing register should retry soon, not skip
            // the rest of the morning.
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
        if (!TrySpawn(force, false)) return false;
        ScheduleNextArrival(MeanCustomersPerMinute() / 60f);
        return true;
    }

    bool TrySpawn(bool force = false, bool allowCar = false)
    {
        LastSpawnError = null;
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

        if (allowCar && CustomerArrivalTraffic.TryDispatchCar(this))
            return true;

        if (allowCar && ParkingLotDressing.HasParkingStalls)
        {
            LastSpawnError = "Parking lot is full";
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
        BuildEntryPoints(entryPointsBuffer, r);
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

    void BuildEntryPoints(List<Vector3> into, Register register)
    {
        into.Clear();

        CustomerWallDoor entrance = CustomerWallDoor.FindEntryDoor();
        if (entrance != null)
        {
            entrance.AppendPassage(into, true);
            Vector3 elbowTarget = register != null
                ? register.GetFrontQueueWorldPosition()
                : GetCustomerFloorSouthHint(into);
            entrance.AppendEastEntryElbow(into, elbowTarget);
            return;
        }

        if (HasInlineWaypoints())
        {
            for (int i = 0; i < entryWaypoints.Count; i++)
            {
                if (entryWaypoints[i] != null)
                    into.Add(entryWaypoints[i].position);
            }
            return;
        }

        if (entryPath != null && entryPath.Count > 0)
        {
            entryPath.GetWorldPoints(into);
            return;
        }

        if (spawnPoint != null)
            into.Add(spawnPoint.position);
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
