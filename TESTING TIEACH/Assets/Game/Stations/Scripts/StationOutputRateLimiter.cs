using UnityEngine;

/// <summary>
/// Runtime output budget for stocked source stations. Tokens allow workers to
/// collect a four-item batch while keeping sustained output at the station's
/// items/min cap.
/// </summary>
public static class StationOutputRateLimiter
{
    const int MaximumBurstUnits = 4;

    public static int GetAvailableUnits(float itemsPerMinute, ref float tokens,
        ref float lastUpdatedAt, ref bool initialized)
    {
        float rate = Mathf.Max(0f, itemsPerMinute);
        float now = Time.time;
        if (rate <= 0.001f)
        {
            tokens = 0f;
            lastUpdatedAt = now;
            initialized = true;
            return 0;
        }

        if (!initialized || now < lastUpdatedAt)
        {
            // Start with one full worker batch, then replenish strictly at the rate cap.
            tokens = rate > 0f ? MaximumBurstUnits : 0f;
            lastUpdatedAt = now;
            initialized = true;
        }
        else
        {
            float elapsed = Mathf.Max(0f, now - lastUpdatedAt);
            tokens = Mathf.Min(MaximumBurstUnits, tokens + elapsed * rate / 60f);
            lastUpdatedAt = now;
        }

        // Clamp tokens created before the one-item burst limit was introduced.
        tokens = Mathf.Clamp(tokens, 0f, MaximumBurstUnits);

        return Mathf.FloorToInt(tokens + 0.0001f);
    }

    public static int TakeUnits(int requestedUnits, float itemsPerMinute,
        ref float tokens, ref float lastUpdatedAt, ref bool initialized)
    {
        int available = GetAvailableUnits(itemsPerMinute,
            ref tokens, ref lastUpdatedAt, ref initialized);
        int taken = Mathf.Clamp(requestedUnits, 0, available);
        tokens = Mathf.Max(0f, tokens - taken);
        return taken;
    }

    public static void RefundUnits(int units, ref float tokens)
    {
        if (units <= 0) return;
        tokens = Mathf.Min(MaximumBurstUnits, tokens + units);
    }

    public static float SecondsPerItem(float itemsPerMinute) =>
        itemsPerMinute > 0.01f ? 60f / itemsPerMinute : 0f;
}
