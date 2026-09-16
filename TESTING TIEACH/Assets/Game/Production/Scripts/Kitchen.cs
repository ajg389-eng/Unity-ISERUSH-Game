using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Legacy timer kitchen. When ProductionManager exists this is a no-op.
/// Without it, still fills the heat lamp (or register) on a simple timer.
/// </summary>
public class Kitchen : MonoBehaviour
{
    [Header("Registers")]
    public List<Register> registers = new List<Register>();

    [Header("Prep")]
    [Tooltip("Seconds to create/assemble one order (only used when no ProductionManager in scene)")]
    public float prepTimePerOrder = 5f;

    [Header("Orders")]
    public CustomerOrderConfig orderConfig;
    public HeatLampStation heatLamp;

    float prepTimer;
    CustomerOrder orderInProgress;

    void Update()
    {
        if (ProductionManager.Instance != null)
            return;

        if (heatLamp == null)
            heatLamp = HeatLampStation.Instance;

        if (orderInProgress != null)
        {
            prepTimer -= Time.deltaTime;
            if (prepTimer <= 0f)
            {
                if (heatLamp != null)
                    heatLamp.DeliverMeal(orderInProgress);
                else
                {
                    foreach (var reg in registers)
                    {
                        if (reg != null && reg.isEnabled)
                        {
                            reg.DeliverOrder(orderInProgress);
                            break;
                        }
                    }
                }
                orderInProgress = null;
            }
            return;
        }

        if (heatLamp != null)
        {
            if (!heatLamp.HasSpace || heatLamp.Count >= heatLamp.targetStock)
                return;
            if (orderConfig == null) return;
            orderInProgress = orderConfig.GenerateRandomOrder();
            prepTimer = prepTimePerOrder;
            return;
        }

        foreach (var reg in registers)
        {
            if (reg == null || !reg.isEnabled) continue;
            if (reg.HasPreparedOrder) continue;

            CustomerOrder frontOrder = reg.GetFrontCustomerOrder();
            if (frontOrder == null || frontOrder.lines == null || frontOrder.lines.Count == 0)
                continue;

            orderInProgress = frontOrder.Clone();
            prepTimer = prepTimePerOrder;
            return;
        }
    }
}
