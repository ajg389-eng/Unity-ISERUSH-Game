using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks live store metrics (ISE concepts: Little's Law, bottlenecks, throughput, utilization).
/// Add to scene once; Register and StoreStatsUI reference Instance.
/// </summary>
public class StoreStatisticsManager : MonoBehaviour
{
    public static StoreStatisticsManager Instance { get; private set; }

    [Header("Sampling")]
    [Tooltip("Window in seconds for throughput (orders/min) and utilization")]
    public float throughputWindowSeconds = 60f;

    // Completed orders: (gameTime when completed, wait duration)
    readonly List<(float completedAt, float waitSeconds)> completedOrders = new List<(float, float)>();
    const int MaxCompletedOrders = 500;
    int mealsWasted;
    int revenueEarned;

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

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        windowStartTime = Time.time;
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

    public void RecordOrderCompleted(float queueJoinTime, Register station, int saleAmount = 0)
    {
        float now = Time.time;
        float wait = Mathf.Max(0f, now - queueJoinTime);
        completedOrders.Add((now, wait));
        if (completedOrders.Count > MaxCompletedOrders)
            completedOrders.RemoveAt(0);
        if (saleAmount > 0)
            revenueEarned += saleAmount;

        dayOrdersCompleted++;
        dayWaitSum += wait;
        if (saleAmount > 0)
            dayRevenue += saleAmount;
    }

    public void RecordMealWasted()
    {
        mealsWasted++;
        dayMealsWasted++;
    }

    public void RecordCustomerLost()
    {
        dayCustomersLost++;
    }

    /// <summary>Called when a customer successfully enters the store.</summary>
    public void RecordCustomerVisit()
    {
        EnsureHourBuckets();
        dayCustomersVisited++;
        int idx = GetCurrentHourBucketIndex();
        if (idx >= 0 && idx < hourVisitBuckets.Length)
            hourVisitBuckets[idx]++;
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
    }

    public int MealsWasted => mealsWasted;
    public int RevenueEarned => revenueEarned;

    public int OrdersCompletedToday => dayOrdersCompleted;
    public int RevenueToday => dayRevenue;
    public int MealsWastedToday => dayMealsWasted;
    public int CustomersLostToday => dayCustomersLost;
    public int CustomersVisitedToday => dayCustomersVisited;
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

        var registers = FindRegisters();
        foreach (var r in registers)
        {
            if (r == null || !r.isEnabled) continue;
            if (!stationBusyTime.ContainsKey(r)) stationBusyTime[r] = 0f;
            if (r.QueueCount > 0)
                stationBusyTime[r] += Time.deltaTime;
        }
    }

    Register[] FindRegisters()
    {
        return FindObjectsByType<Register>(FindObjectsSortMode.None);
    }

    // ---- Public metrics ----

    /// <summary>Customers currently in the system (WIP) – Little's Law / congestion.</summary>
    public int CustomersInSystem
    {
        get
        {
            var customers = FindObjectsByType<CustomerAI>(FindObjectsSortMode.None);
            return customers != null ? customers.Length : 0;
        }
    }

    /// <summary>Queue length per station (for bottleneck identification).</summary>
    public Dictionary<string, int> QueueLengthPerStation
    {
        get
        {
            var d = new Dictionary<string, int>();
            foreach (var r in FindRegisters())
            {
                if (r == null) continue;
                string label = r.gameObject.name;
                if (d.ContainsKey(label)) label = label + " (#" + r.GetInstanceID() + ")";
                d[label] = r.QueueCount;
            }
            return d;
        }
    }

    /// <summary>Average wait time (queue join to served) in seconds – service performance.</summary>
    public float AverageWaitTimeSeconds
    {
        get
        {
            if (completedOrders.Count == 0) return 0f;
            float sum = 0f;
            foreach (var (_, wait) in completedOrders)
                sum += wait;
            return sum / completedOrders.Count;
        }
    }

    /// <summary>Orders completed in the last throughputWindowSeconds – system output.</summary>
    public float ThroughputOrdersPerMinute
    {
        get
        {
            float cutoff = Time.time - throughputWindowSeconds;
            int count = 0;
            foreach (var (completedAt, _) in completedOrders)
                if (completedAt >= cutoff) count++;
            return throughputWindowSeconds > 0 ? (count / throughputWindowSeconds) * 60f : 0f;
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

    /// <summary>Order completion time (cycle time) – same as average wait.</summary>
    public float OrderCompletionTimeSeconds => AverageWaitTimeSeconds;
}
