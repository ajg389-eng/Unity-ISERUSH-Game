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

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RecordOrderCompleted(float queueJoinTime, Register station, int saleAmount = 0)
    {
        float now = Time.time;
        float wait = now - queueJoinTime;
        completedOrders.Add((now, wait));
        if (completedOrders.Count > MaxCompletedOrders)
            completedOrders.RemoveAt(0);
        if (saleAmount > 0)
            revenueEarned += saleAmount;
    }

    public void RecordMealWasted()
    {
        mealsWasted++;
    }

    public int MealsWasted => mealsWasted;
    public int RevenueEarned => revenueEarned;

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
