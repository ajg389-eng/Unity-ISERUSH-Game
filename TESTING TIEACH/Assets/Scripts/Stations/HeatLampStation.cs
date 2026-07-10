using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// One prepared meal sitting under the heat lamp, waiting to be sold or expired.
/// </summary>
[System.Serializable]
public class HeldMeal
{
    public CustomerOrder order;
    public float placedAt;

    public HeldMeal(CustomerOrder order, float placedAt)
    {
        this.order = order;
        this.placedAt = placedAt;
    }

    public float AgeSeconds => Time.time - placedAt;
}

/// <summary>
/// Fast-food holding area. Kitchen delivers finished meals here; registers serve from this stock.
/// Meals expire if held too long — overproducing wastes food.
/// Inventory is viewed in Manage mode by clicking the heat lamp.
/// </summary>
public class HeatLampStation : MonoBehaviour
{
    public static HeatLampStation Instance { get; private set; }

    [Header("Capacity")]
    [Tooltip("Maximum meals that can sit under the lamp at once")]
    public int maxCapacity = 8;
    [Tooltip("Kitchen tries to keep this many meals ready (demand + buffer)")]
    public int targetStock = 3;

    [Header("Expiry")]
    [Tooltip("Seconds a meal can sit before it is thrown out")]
    public float expireAfterSeconds = 28f;

    [Header("Interaction")]
    public Vector3 interactionOffset = Vector3.zero;

    [Header("Optional UI")]
    [Tooltip("Optional TMP label if you want a custom HUD readout.")]
    public TextMeshProUGUI statusLabel;

    readonly List<HeldMeal> meals = new List<HeldMeal>();
    int totalWasted;
    int totalDelivered;
    int totalSold;

    public int Count => meals.Count;
    public int TotalWasted => totalWasted;
    public int TotalDelivered => totalDelivered;
    public int TotalSold => totalSold;
    public bool HasSpace => meals.Count < maxCapacity;
    public IReadOnlyList<HeldMeal> Meals => meals;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple HeatLampStation objects; using the newest.", this);
        }
        Instance = this;

        // Remove leftover world label from older builds
        var leftover = transform.Find("HeatLampInventoryLabel");
        if (leftover != null)
            Destroy(leftover.gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        ExpireStaleMeals();
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    /// <summary>Seconds left before this meal expires (0 if already expired / no expiry).</summary>
    public float GetSecondsUntilExpire(HeldMeal meal)
    {
        if (meal == null || expireAfterSeconds <= 0f) return 0f;
        return Mathf.Max(0f, expireAfterSeconds - meal.AgeSeconds);
    }

    static string FormatExpireTime(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        if (s < 60) return s + "s";
        int m = s / 60;
        int rem = s % 60;
        return m + ":" + rem.ToString("00");
    }

    static string MealDisplayName(CustomerOrder order)
    {
        if (order == null) return "Item";
        string name = order.GetDisplayString();
        return string.IsNullOrEmpty(name) || name == "—" ? "Item" : name;
    }

    /// <summary>One line per held meal with time until expiry.</summary>
    public string GetInventoryDisplay()
    {
        if (meals.Count == 0) return "Empty";

        var parts = new List<string>();
        for (int i = 0; i < meals.Count; i++)
        {
            var meal = meals[i];
            if (meal?.order == null) continue;
            string itemName = MealDisplayName(meal.order);
            float left = GetSecondsUntilExpire(meal);
            parts.Add($"{itemName}  —  {FormatExpireTime(left)}");
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "Empty";
    }

    public string GetManagePanelText()
    {
        string text = $"Stock: {meals.Count}/{maxCapacity}\n{GetInventoryDisplay()}";
        if (totalWasted > 0)
            text += $"\nWaste: {totalWasted}";
        return text;
    }

    public bool DeliverMeal(CustomerOrder order)
    {
        if (order == null || order.lines == null || order.lines.Count == 0)
            return false;
        if (!HasSpace)
            return false;

        meals.Add(new HeldMeal(order.Clone(), Time.time));
        totalDelivered++;
        RefreshStatusLabel();
        return true;
    }

    public bool CanFulfill(CustomerOrder order)
    {
        return CountMissing(order) == 0;
    }

    public int CountMissing(CustomerOrder order)
    {
        if (order?.lines == null) return 0;
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        var need = new Dictionary<ItemDefinition, int>();
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            if (config != null && config.IsDrink(line.item)) continue;
            need[line.item] = need.TryGetValue(line.item, out int c) ? c + line.quantity : line.quantity;
        }

        foreach (var m in meals)
        {
            var item = m?.order?.PrimaryItem;
            if (item == null || !need.TryGetValue(item, out int left) || left <= 0) continue;
            need[item] = left - 1;
        }

        int missing = 0;
        foreach (var kv in need)
            if (kv.Value > 0) missing += kv.Value;
        return missing;
    }

    public void ClearAllMeals()
    {
        int n = meals.Count;
        meals.Clear();
        totalWasted += n;
        RefreshStatusLabel();
    }

    public bool HasSingleItem(ItemDefinition item)
    {
        return FindSingleItemIndex(item) >= 0;
    }

    public CustomerOrder TryTakeSingleItem(ItemDefinition item)
    {
        int idx = FindSingleItemIndex(item);
        if (idx < 0) return null;
        var meal = meals[idx];
        meals.RemoveAt(idx);
        RefreshStatusLabel();
        return meal.order;
    }

    public bool HasMatching(CustomerOrder order)
    {
        return CanFulfill(order) || FindMatchingIndex(order) >= 0;
    }

    public int CountMatches(CustomerOrder order)
    {
        if (order == null) return 0;
        if (CanFulfill(order)) return 1;
        int n = 0;
        for (int i = 0; i < meals.Count; i++)
        {
            if (meals[i]?.order != null && order.Matches(meals[i].order))
                n++;
        }
        return n;
    }

    public CustomerOrder TryTakeMatching(CustomerOrder order)
    {
        if (order == null) return null;

        int exact = FindMatchingIndex(order);
        if (exact >= 0)
        {
            var meal = meals[exact];
            meals.RemoveAt(exact);
            totalSold++;
            RefreshStatusLabel();
            return meal.order;
        }

        if (!CanFulfill(order)) return null;

        foreach (var line in order.lines)
        {
            if (line.item == null) continue;
            for (int q = 0; q < line.quantity; q++)
            {
                int idx = FindSingleItemIndex(line.item);
                if (idx < 0) return null;
                meals.RemoveAt(idx);
            }
        }

        totalSold++;
        RefreshStatusLabel();
        return order.Clone();
    }

    int FindSingleItemIndex(ItemDefinition item)
    {
        if (item == null) return -1;
        for (int i = 0; i < meals.Count; i++)
        {
            var o = meals[i]?.order;
            if (o == null || o.lines == null || o.lines.Count != 1) continue;
            if (o.PrimaryItem == item) return i;
        }
        for (int i = 0; i < meals.Count; i++)
        {
            var o = meals[i]?.order;
            if (o != null && o.PrimaryItem == item && o.Matches(CustomerOrder.FromItem(item, 1)))
                return i;
        }
        return -1;
    }

    public List<CustomerOrder> GetHeldOrderClones()
    {
        var list = new List<CustomerOrder>(meals.Count);
        foreach (var m in meals)
        {
            if (m?.order != null)
                list.Add(m.order.Clone());
        }
        return list;
    }

    int FindMatchingIndex(CustomerOrder order)
    {
        if (order == null) return -1;
        for (int i = 0; i < meals.Count; i++)
        {
            if (meals[i]?.order != null && order.Matches(meals[i].order))
                return i;
        }
        return -1;
    }

    void ExpireStaleMeals()
    {
        if (expireAfterSeconds <= 0f) return;
        bool changed = false;
        for (int i = meals.Count - 1; i >= 0; i--)
        {
            if (meals[i] == null || meals[i].AgeSeconds < expireAfterSeconds)
                continue;
            meals.RemoveAt(i);
            totalWasted++;
            changed = true;
            if (StoreStatisticsManager.Instance != null)
                StoreStatisticsManager.Instance.RecordMealWasted();
        }
        if (changed) RefreshStatusLabel();
    }

    void RefreshStatusLabel()
    {
        if (statusLabel == null) return;
        statusLabel.text = GetManagePanelText();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.85f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(1.2f, 1f, 1.2f));
        Gizmos.DrawSphere(GetInteractionPosition(), 0.15f);
    }
}
