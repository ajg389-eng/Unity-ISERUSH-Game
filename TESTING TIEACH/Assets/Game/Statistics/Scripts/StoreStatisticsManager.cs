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
    float dayWaitSum;
    int dayMoneyStart;
    bool dayTrackingStarted;

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

    /// <summary>Reset day counters and snapshot starting cash for the new shift.</summary>
    public void BeginDay()
    {
        dayOrdersCompleted = 0;
        dayRevenue = 0;
        dayMealsWasted = 0;
        dayCustomersLost = 0;
        dayWaitSum = 0f;
        dayMoneyStart = GetCurrentMoney();
        dayTrackingStarted = true;
    }

    public int MealsWasted => mealsWasted;
    public int RevenueEarned => revenueEarned;

    public int OrdersCompletedToday => dayOrdersCompleted;
    public int RevenueToday => dayRevenue;
    public int MealsWastedToday => dayMealsWasted;
    public int CustomersLostToday => dayCustomersLost;
    public int MoneyAtDayStart => dayMoneyStart;
    public bool DayTrackingStarted => dayTrackingStarted;

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
