using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Watches operational state that does not expose events and posts only when a
/// condition crosses a meaningful threshold.
/// </summary>
public class OperationalNotificationMonitor : MonoBehaviour
{
    const int CongestedQueueThreshold = 3;
    const int LowCashThreshold = 100;
    const float PollInterval = 1f;

    readonly HashSet<int> congestedRegisters = new HashSet<int>();
    readonly HashSet<int> congestedPickupStations = new HashSet<int>();
    readonly HashSet<int> liveRegisters = new HashSet<int>();
    readonly HashSet<int> livePickups = new HashSet<int>();
    float nextPoll;
    bool cashInitialized;
    bool cashWasLow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<OperationalNotificationMonitor>() != null) return;
        new GameObject("OperationalNotificationMonitor").AddComponent<OperationalNotificationMonitor>();
    }

    void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + PollInterval;
        CheckQueues();
        CheckCash();
    }

    void CheckQueues()
    {
        liveRegisters.Clear();
        IReadOnlyList<Register> registers = Register.ActiveRegisters;
        for (int i = 0; i < registers.Count; i++)
        {
            Register register = registers[i];
            if (register == null || !register.IsPlacedRegister || !register.isEnabled) continue;
            int id = register.GetInstanceID();
            liveRegisters.Add(id);
            bool congested = register.QueueCount >= CongestedQueueThreshold;
            if (congested && congestedRegisters.Add(id))
            {
                NotificationCenter.Post("A register queue has reached " + register.QueueCount +
                    " customers. Check service capacity.", GameNotificationKind.Warning,
                    "register-queue-" + id, 30f);
            }
            else if (!congested)
                congestedRegisters.Remove(id);
        }
        congestedRegisters.RemoveWhere(id => !liveRegisters.Contains(id));

        livePickups.Clear();
        IReadOnlyList<HeatLampStation> pickups = HeatLampStation.ActiveStations;
        for (int i = 0; i < pickups.Count; i++)
        {
            HeatLampStation pickup = pickups[i];
            if (pickup == null || !pickup.isActiveAndEnabled) continue;
            int id = pickup.GetInstanceID();
            livePickups.Add(id);
            bool congested = pickup.PickupQueueCount >= CongestedQueueThreshold;
            if (congested && congestedPickupStations.Add(id))
            {
                NotificationCenter.Post("A pickup queue has reached " + pickup.PickupQueueCount +
                    " customers. Check pickup stock and production flow.", GameNotificationKind.Warning,
                    "pickup-queue-" + id, 30f);
            }
            else if (!congested)
                congestedPickupStations.Remove(id);
        }
        congestedPickupStations.RemoveWhere(id => !livePickups.Contains(id));
    }

    void CheckCash()
    {
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (money == null) return;
        bool low = money.CurrentMoney < LowCashThreshold;
        if (!cashInitialized)
        {
            cashInitialized = true;
            cashWasLow = low;
            return;
        }
        if (low && !cashWasLow)
        {
            NotificationCenter.Post("Cash is below $" + LowCashThreshold + ". Review spending and throughput.",
                GameNotificationKind.Warning, "low-cash", 30f);
        }
        cashWasLow = low;
    }
}
