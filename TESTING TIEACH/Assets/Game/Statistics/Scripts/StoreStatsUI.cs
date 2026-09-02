using UnityEngine;
using TMPro;

/// <summary>
/// Displays live store statistics on the Management screen Store Stats tab.
/// Attach to the Stats panel (or assign statsText to a TMP on that panel).
/// </summary>
public class StoreStatsUI : MonoBehaviour
{
    [Tooltip("Text to show metrics; if null, uses first TextMeshProUGUI in children")]
    public TextMeshProUGUI statsText;
    [Tooltip("Refresh interval in seconds")]
    public float refreshInterval = 0.4f;

    float nextRefresh;
    StoreStatisticsManager stats;

    void Start()
    {
        if (statsText == null)
            statsText = GetComponentInChildren<TextMeshProUGUI>(true);
        stats = StoreStatisticsManager.Instance;
    }

    void Update()
    {
        if (statsText == null || stats == null) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;

        statsText.text = BuildStatsString();
    }

    string BuildStatsString()
    {
        var s = stats;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>Core Metrics</b>");
        sb.AppendLine();

        sb.AppendLine("<b>Customers in system (WIP)</b>");
        sb.AppendLine("  " + s.CustomersInSystem + "  <i><size=80%>Little's Law / congestion</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Queue length per station</b>");
        var queues = s.QueueLengthPerStation;
        if (queues.Count == 0)
            sb.AppendLine("  No registers");
        else
            foreach (var kv in queues)
                sb.AppendLine("  " + kv.Key + ": " + kv.Value);
        sb.AppendLine("  <i><size=80%>Bottleneck identification</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Average wait time</b>");
        sb.AppendLine("  " + s.AverageWaitTimeSeconds.ToString("F1") + " s  <i><size=80%>Service performance</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Throughput</b>");
        sb.AppendLine("  " + s.ThroughputOrdersPerMinute.ToString("F2") + " orders/min  <i><size=80%>System output</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Station utilization (%)</b>");
        var util = s.StationUtilizationPercent;
        if (util.Count == 0)
            sb.AppendLine("  —");
        else
            foreach (var kv in util)
                sb.AppendLine("  " + kv.Key + ": " + kv.Value.ToString("F0") + "%");
        sb.AppendLine("  <i><size=80%>Capacity usage</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Worker utilization</b>");
        sb.AppendLine("  " + s.WorkerUtilizationPercent.ToString("F0") + "%  <i><size=80%>Labor efficiency</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Heat lamp stock</b>");
        sb.AppendLine("  " + s.HeatLampStock + " / " + s.HeatLampCapacity + "  <i><size=80%>Make-to-stock buffer</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Food wasted (expired)</b>");
        sb.AppendLine("  " + s.MealsWasted + "  <i><size=80%>Overproduction cost</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Revenue earned</b>");
        sb.AppendLine("  $" + s.RevenueEarned + "  <i><size=80%>Sales from heat lamp</size></i>");
        sb.AppendLine();

        sb.AppendLine("<b>Order completion time</b>");
        sb.AppendLine("  " + s.OrderCompletionTimeSeconds.ToString("F1") + " s  <i><size=80%>Cycle time</size></i>");

        return sb.ToString();
    }
}
