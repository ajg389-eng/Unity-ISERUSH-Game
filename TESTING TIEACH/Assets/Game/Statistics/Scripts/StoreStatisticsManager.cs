using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks live store metrics (ISE concepts: Little's Law, bottlenecks, throughput, utilization).
/// Add to scene once; Register and StoreStatsUI reference Instance.
/// </summary>
public class StoreStatisticsManager : MonoBehaviour
{
    public static StoreStatisticsManager Instance { get; private set; }
    public event System.Action OnStatisticsChanged;

    [Header("Sampling")]
    [Tooltip("Window in seconds for throughput (orders/min) and utilization")]
    public float throughputWindowSeconds = 60f;

    // Simulation timestamps for orders completed in the rolling throughput window.
    readonly List<float> completedOrderTimes = new List<float>();
    const int MaxCompletedOrders = 500;
    int mealsWasted;
    int revenueEarned;
    int careerOrdersServed;
    int careerCustomersVisited;
    int careerCustomersLost;
    int careerShiftsCompleted;
    int careerBestShiftRevenue;
    int careerBestShiftOrders;
    int careerLastCompletedDay;
    float careerWaitTotalSeconds;

    // Day-scoped counters (reset at the start of each shift)
    int dayOrdersCompleted;
    int dayRevenue;
    int dayMealsWasted;
    int dayCustomersLost;
    int dayCustomersVisited;
    float dayWaitSum;
    int dayMoneyStart;
    bool dayTrackingStarted;

    /// <summary>Visits per in-game hour from day start → day end (e.g. 10 AM–10 PM).</summary>
    int[] hourVisitBuckets = System.Array.Empty<int>();

    // Per-register: busy time (queue not empty) in the current window
    readonly Dictionary<Register, float> stationBusyTime = new Dictionary<Register, float>();
    float windowStartTime;
    float throughputTrackingStartedAt;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        windowStartTime = Time.time;
        throughputTrackingStartedAt = Time.time;
        EnsureHourBuckets();
    }

    void Start()
    {
        if (!dayTrackingStarted)
            BeginDay();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RecordOrderCompleted(float orderPlacedTime, Register station, int saleAmount = 0)
    {
        float now = Time.time;
        float wait = Mathf.Max(0f, now - orderPlacedTime);
        completedOrderTimes.Add(now);
        if (completedOrderTimes.Count > MaxCompletedOrders)
            completedOrderTimes.RemoveAt(0);
        if (saleAmount > 0)
            revenueEarned += saleAmount;
        careerOrdersServed++;
        careerWaitTotalSeconds += wait;

        dayOrdersCompleted++;
        dayWaitSum += wait;
        if (saleAmount > 0)
            dayRevenue += saleAmount;
        OnStatisticsChanged?.Invoke();
    }

    public void RecordMealWasted()
    {
        mealsWasted++;
        dayMealsWasted++;
        OnStatisticsChanged?.Invoke();
        NotificationCenter.Post("Prepared food expired at a pickup station. Review customer demand and flow throughput.",
            GameNotificationKind.Warning, "food-wasted", 30f);
    }

    public void RecordCustomerLost()
    {
        dayCustomersLost++;
        careerCustomersLost++;
        OnStatisticsChanged?.Invoke();
        NotificationCenter.Post("A customer left before receiving their order.",
            GameNotificationKind.Warning, "customer-walkout", 20f);
    }

    /// <summary>Called when a customer successfully enters the store.</summary>
    public void RecordCustomerVisit()
    {
        EnsureHourBuckets();
        dayCustomersVisited++;
        careerCustomersVisited++;
        int idx = GetCurrentHourBucketIndex();
        if (idx >= 0 && idx < hourVisitBuckets.Length)
            hourVisitBuckets[idx]++;
        OnStatisticsChanged?.Invoke();
    }

    /// <summary>Reset day counters and snapshot starting cash for the new shift.</summary>
    public void BeginDay()
    {
        dayOrdersCompleted = 0;
        dayRevenue = 0;
        dayMealsWasted = 0;
        dayCustomersLost = 0;
        dayCustomersVisited = 0;
        dayWaitSum = 0f;
        dayMoneyStart = GetCurrentMoney();
        dayTrackingStarted = true;
        EnsureHourBuckets(forceReset: true);
        OnStatisticsChanged?.Invoke();
    }

    /// <summary>Starts a clean rolling throughput window when the player opens the shift.</summary>
    public void BeginShiftThroughputTracking()
    {
        completedOrderTimes.Clear();
        throughputTrackingStartedAt = Time.time;
        OnStatisticsChanged?.Invoke();
    }

    public void RestoreDailyTaskProgress(int completedOrders, int revenue, float waitTotalSeconds)
    {
        dayOrdersCompleted = Mathf.Max(0, completedOrders);
        dayRevenue = Mathf.Max(0, revenue);
        dayWaitSum = Mathf.Max(0f, waitTotalSeconds);
        dayTrackingStarted = true;
        OnStatisticsChanged?.Invoke();
    }

    public int MealsWasted => mealsWasted;
    public int RevenueEarned => revenueEarned;
    public int CareerOrdersServed => careerOrdersServed;
    public int CareerCustomersVisited => careerCustomersVisited;
    public int CareerCustomersLost => careerCustomersLost;
    public int CareerShiftsCompleted => careerShiftsCompleted;
    public int CareerBestShiftRevenue => careerBestShiftRevenue;
    public int CareerBestShiftOrders => careerBestShiftOrders;
    public int CareerLastCompletedDay => careerLastCompletedDay;
    public float CareerAverageWaitTimeSeconds => careerOrdersServed > 0
        ? careerWaitTotalSeconds / careerOrdersServed : 0f;
    public float CareerWaitTotalSeconds => careerWaitTotalSeconds;

    public void RecordShiftCompleted(int day)
    {
        day = Mathf.Max(1, day);
        if (day <= careerLastCompletedDay) return;

        careerLastCompletedDay = day;
        careerShiftsCompleted++;
        careerBestShiftRevenue = Mathf.Max(careerBestShiftRevenue, dayRevenue);
        careerBestShiftOrders = Mathf.Max(careerBestShiftOrders, dayOrdersCompleted);
        OnStatisticsChanged?.Invoke();
    }

    public void RestoreCareerStatistics(int ordersServed, int customersVisited,
        int customersLost, int shiftsCompleted, int totalRevenue, int totalMealsWasted,
        int bestShiftRevenue, int bestShiftOrders, int lastCompletedDay,
        float totalWaitSeconds)
    {
        careerOrdersServed = Mathf.Max(0, ordersServed);
        careerCustomersVisited = Mathf.Max(0, customersVisited);
        careerCustomersLost = Mathf.Max(0, customersLost);
        careerShiftsCompleted = Mathf.Max(0, shiftsCompleted);
        revenueEarned = Mathf.Max(0, totalRevenue);
        mealsWasted = Mathf.Max(0, totalMealsWasted);
        careerBestShiftRevenue = Mathf.Max(0, bestShiftRevenue);
        careerBestShiftOrders = Mathf.Max(0, bestShiftOrders);
        careerLastCompletedDay = Mathf.Max(0, lastCompletedDay);
        careerWaitTotalSeconds = Mathf.Max(0f, totalWaitSeconds);
        OnStatisticsChanged?.Invoke();
    }

    public void ResetCareerStatistics()
    {
        careerOrdersServed = 0;
        careerCustomersVisited = 0;
        careerCustomersLost = 0;
        careerShiftsCompleted = 0;
        careerBestShiftRevenue = 0;
        careerBestShiftOrders = 0;
        careerLastCompletedDay = 0;
        careerWaitTotalSeconds = 0f;
        revenueEarned = 0;
        mealsWasted = 0;
        OnStatisticsChanged?.Invoke();
    }

    public int OrdersCompletedToday => dayOrdersCompleted;
    public int RevenueToday => dayRevenue;
    public int MealsWastedToday => dayMealsWasted;
    public int CustomersLostToday => dayCustomersLost;
    public int CustomersVisitedToday => dayCustomersVisited;
    public int UnservedCustomersToday => Mathf.Max(0, dayCustomersVisited - dayOrdersCompleted - dayCustomersLost);
    public int MoneyAtDayStart => dayMoneyStart;
    public bool DayTrackingStarted => dayTrackingStarted;

    public int ShiftStartHour
    {
        get
        {
            var tm = GameTimeManager.Instance;
            return tm != null ? tm.dayStartHour : 10;
        }
    }

    public int ShiftEndHour
    {
        get
        {
            var tm = GameTimeManager.Instance;
            return tm != null ? tm.dayEndHour : 22;
        }
    }

    /// <summary>Number of hour bars on the 10 AM–10 PM (or configured) visit graph.</summary>
    public int ShiftHourCount => Mathf.Max(1, ShiftEndHour - ShiftStartHour);

    /// <summary>Visits per hour from shift start → end (e.g. index 0 = 10–11 AM).</summary>
    public int[] GetVisitTrendBuckets()
    {
        EnsureHourBuckets();
        var copy = new int[hourVisitBuckets.Length];
        System.Array.Copy(hourVisitBuckets, copy, hourVisitBuckets.Length);
        return copy;
    }

    /// <summary>Short labels for each hour bar (10a, 11a, … 9p).</summary>
    public string[] GetVisitHourLabels()
    {
        int n = ShiftHourCount;
        int start = ShiftStartHour;
        var labels = new string[n];
        for (int i = 0; i < n; i++)
            labels[i] = FormatHourLabel(start + i);
        return labels;
    }

    public int GetCurrentHourBucketIndex()
    {
        EnsureHourBuckets();
        var tm = GameTimeManager.Instance;
        if (tm == null) return 0;
        int hour = Mathf.FloorToInt(tm.CurrentMinutes / 60f);
        int idx = hour - ShiftStartHour;
        if (idx < 0) return 0;
        if (idx >= hourVisitBuckets.Length) return hourVisitBuckets.Length - 1;
        return idx;
    }

    void EnsureHourBuckets(bool forceReset = false)
    {
        int n = ShiftHourCount;
        if (!forceReset && hourVisitBuckets != null && hourVisitBuckets.Length == n)
            return;
        hourVisitBuckets = new int[n];
    }

    static string FormatHourLabel(int hour24)
    {
        hour24 = ((hour24 % 24) + 24) % 24;
        bool pm = hour24 >= 12;
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        return hour12 + (pm ? "p" : "a");
    }

    public float AverageWaitTimeTodaySeconds =>
        dayOrdersCompleted > 0 ? dayWaitSum / dayOrdersCompleted : 0f;

    /// <summary>Share of customers who were served (vs walked out), 0–100.</summary>
    public float ServiceEfficiencyPercent
    {
        get
        {
            int total = dayOrdersCompleted + dayCustomersLost;
            if (total <= 0) return 0f;
            return (dayOrdersCompleted / (float)total) * 100f;
        }
    }

    public int GetCashChangeToday()
    {
        return GetCurrentMoney() - dayMoneyStart;
    }

    static int GetCurrentMoney()
    {
        var money = FindFirstObjectByType<MoneyManager>();
        return money != null ? money.CurrentMoney : 0;
    }

    public int HeatLampStock
    {
        get
        {
            var lamp = HeatLampStation.Instance;
            return lamp != null ? lamp.Count : 0;
        }
    }

    public int HeatLampCapacity
    {
        get
        {
            var lamp = HeatLampStation.Instance;
            return lamp != null ? lamp.maxCapacity : 0;
        }
    }

    void Update()
    {
        float now = Time.time;
        if (now - windowStartTime >= throughputWindowSeconds)
        {
            windowStartTime = now;
            stationBusyTime.Clear();
        }

        IReadOnlyList<Register> registers = Register.ActiveRegisters;
        for (int i = 0; i < registers.Count; i++)
        {
            Register r = registers[i];
            if (r == null || !r.isEnabled) continue;
            if (!stationBusyTime.ContainsKey(r)) stationBusyTime[r] = 0f;
            if (r.QueueCount > 0)
                stationBusyTime[r] += Time.deltaTime;
        }
    }

    // ---- Public metrics ----

    /// <summary>Customers currently in the system (WIP) – Little's Law / congestion.</summary>
    public int CustomersInSystem
    {
        get
        {
            return CustomerAI.OrderBoardCustomers.Count;
        }
    }

    /// <summary>Queue length per station (for bottleneck identification).</summary>
    public Dictionary<string, int> QueueLengthPerStation
    {
        get
        {
            var d = new Dictionary<string, int>();
            IReadOnlyList<Register> registers = Register.ActiveRegisters;
            for (int i = 0; i < registers.Count; i++)
            {
                Register r = registers[i];
                if (r == null) continue;
                string label = r.gameObject.name;
                if (d.ContainsKey(label)) label = label + " (#" + r.GetInstanceID() + ")";
                d[label] = r.QueueCount;
            }
            return d;
        }
    }

    /// <summary>Average order wait (order placed to full pickup) today, in simulation seconds.</summary>
    public float AverageWaitTimeSeconds
    {
        get => AverageWaitTimeTodaySeconds;
    }

    /// <summary>Orders completed in the last throughputWindowSeconds – system output.</summary>
    public float ThroughputOrdersPerMinute
    {
        get
        {
            float windowSeconds = Mathf.Max(1f, throughputWindowSeconds);
            float cutoff = Time.time - windowSeconds;
            int count = 0;
            foreach (float completedAt in completedOrderTimes)
                if (completedAt >= cutoff) count++;
            float observedWindow = Mathf.Clamp(Time.time - throughputTrackingStartedAt,
                1f, windowSeconds);
            return count * 60f / observedWindow;
        }
    }

    /// <summary>Station utilization % (fraction of time queue was non-empty) in current window.</summary>
    public Dictionary<string, float> StationUtilizationPercent
    {
        get
        {
            float elapsed = Time.time - windowStartTime;
            if (elapsed <= 0f) elapsed = 1f;
            var d = new Dictionary<string, float>();
            foreach (var kv in stationBusyTime)
            {
                if (kv.Key == null) continue;
                float pct = Mathf.Clamp01(kv.Value / elapsed) * 100f;
                d[kv.Key.gameObject.name] = pct;
            }
            return d;
        }
    }

    /// <summary>Worker utilization – same as station (one worker per register).</summary>
    public float WorkerUtilizationPercent
    {
        get
        {
            var util = StationUtilizationPercent;
            if (util.Count == 0) return 0f;
            float sum = 0f;
            foreach (var pct in util.Values) sum += pct;
            return sum / util.Count;
        }
    }

    /// <summary>Order completion time (order placed to full pickup) – same as average wait.</summary>
    public float OrderCompletionTimeSeconds => AverageWaitTimeSeconds;
}
