using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
/// Fast-food holding / pass-through. Kitchen workers deliver finished meals here;
/// customers grab matching orders from the lobby side of the counter.
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

    [Header("Customer pickup (lobby / pass-through side)")]
    [Tooltip("Offset from the heat lamp to the customer stand. Leave zero to auto-place opposite the worker tile.")]
    public Vector3 customerPickupOffset = Vector3.zero;
    [Tooltip("Spacing between customers waiting at the pass.")]
    public float customerPickupSpacing = 1.15f;

    [Header("Optional UI")]
    [Tooltip("Optional TMP label if you want a custom HUD readout.")]
    public TextMeshProUGUI statusLabel;
    [Tooltip("World-space position of the supply-shortfall warning above the lamp.")]
    public Vector3 cautionIndicatorOffset = new Vector3(0f, 2.5f, 0f);
    [Tooltip("World-space width and height of the caution indicator.")]
    public float cautionIndicatorSize = 0.85f;

    readonly List<HeldMeal> meals = new List<HeldMeal>();
    int totalWasted;
    int totalDelivered;
    int totalSold;
    GameObject cautionIndicator;
    RectTransform cautionIndicatorRect;
    TextMeshProUGUI cautionMessage;
    CanvasGroup cautionMessageGroup;
    float cautionMessageShownAt = float.NegativeInfinity;
    float nextCautionRefresh;

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

        var leftover = transform.Find("HeatLampInventoryLabel");
        if (leftover != null)
            Destroy(leftover.gameObject);
    }

    void OnEnable()
    {
        Instance = this;
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        ExpireStaleMeals();
        if (Time.unscaledTime >= nextCautionRefresh)
        {
            nextCautionRefresh = Time.unscaledTime + 0.5f;
            RefreshCautionIndicator();
        }
        HandleCautionClick();
        UpdateCautionMessageFade();
    }

    void LateUpdate()
    {
        if (cautionIndicator == null || !cautionIndicator.activeSelf) return;
        Camera cam = Camera.main;
        if (cam != null)
            cautionIndicator.transform.rotation = cam.transform.rotation;
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    /// <summary>Direction from the lamp into the lobby (customer side).</summary>
    public Vector3 GetCustomerQueueDirection()
    {
        Vector3 offset = GetCustomerSideOffset();
        if (offset.sqrMagnitude < 0.01f) return Vector3.back;
        return offset.normalized;
    }

    /// <summary>World stand point for a customer at the pass (index 0 = at the counter).</summary>
    public Vector3 GetCustomerPickupPosition(int index = 0)
    {
        Vector3 intoLobby = GetCustomerQueueDirection();
        float standOff = GetCustomerStandDistance();
        Vector3 origin = transform.position + intoLobby * standOff;
        Vector3 pos = origin + intoLobby * (customerPickupSpacing * Mathf.Max(0, index));
        return SnapPickupToGround(pos);
    }

    /// <summary>Pickup stand near a register, still on the customer side of this lamp.</summary>
    public Vector3 GetCustomerPickupPositionNear(Vector3 nearWorld, int index = 0)
    {
        Vector3 intoLobby = GetCustomerQueueDirection();
        float standOff = GetCustomerStandDistance();
        Vector3 origin = transform.position + intoLobby * standOff;

        Vector3 alongCounter = Vector3.Cross(Vector3.up, intoLobby);
        if (alongCounter.sqrMagnitude < 0.01f)
            alongCounter = transform.right;
        alongCounter.Normalize();

        Vector3 toNear = nearWorld - origin;
        toNear.y = 0f;
        float lateral = Mathf.Clamp(Vector3.Dot(toNear, alongCounter), -2.5f, 2.5f);

        Vector3 pos = origin + alongCounter * lateral + intoLobby * (customerPickupSpacing * Mathf.Max(0, index));
        return SnapPickupToGround(pos);
    }

    float GetCustomerStandDistance()
    {
        float cell = 1f;
        if (GridManager.Instance != null)
            cell = Mathf.Max(0.5f, GridManager.Instance.cellSize);

        // Clear the station footprint, then one stand cell into the lobby.
        var fp = GetComponent<BuildFootprint>();
        int depth = fp != null ? Mathf.Max(1, Mathf.Max(fp.sizeX, fp.sizeY)) : 2;
        return Mathf.Max(customerPickupSpacing, cell * (depth * 0.5f + 0.75f));
    }

    Vector3 GetCustomerSideOffset()
    {
        if (customerPickupOffset.sqrMagnitude > 0.01f)
            return customerPickupOffset;

        return ResolveLobbySideDirection() * GetCustomerStandDistance();
    }

    /// <summary>
    /// Customer / lobby side of the pass = opposite the green worker stand,
    /// forced to agree with the nearest register's lobby direction.
    /// </summary>
    Vector3 ResolveLobbySideDirection()
    {
        Vector3 worker = GetInteractionPosition();
        Vector3 awayFromWorker = transform.position - worker;
        awayFromWorker.y = 0f;

        Vector3 dir;
        if (awayFromWorker.sqrMagnitude > 0.05f)
            dir = awayFromWorker.normalized;
        else
        {
            // Prefab worker quad sits at local +Z.
            dir = -transform.TransformDirection(Vector3.forward);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.back;
            else
                dir.Normalize();
        }

        // Registers already know which way the lobby is (order queue side).
        // If "opposite worker" disagrees, flip — fixes lamps whose pivot sits on the kitchen half.
        Register nearest = FindNearestRegister();
        if (nearest != null)
        {
            Vector3 lobby = nearest.GetLobbyDirection();
            lobby.y = 0f;
            if (lobby.sqrMagnitude > 0.01f && Vector3.Dot(dir, lobby.normalized) < 0f)
                dir = -dir;
        }

        return dir;
    }

    Register FindNearestRegister()
    {
        var registers = FindObjectsOfType<Register>();
        if (registers == null || registers.Length == 0) return null;

        Register best = null;
        float bestDist = float.MaxValue;
        Vector3 origin = transform.position;
        for (int i = 0; i < registers.Length; i++)
        {
            var reg = registers[i];
            if (reg == null) continue;
            float d = (reg.transform.position - origin).sqrMagnitude;
            if (d >= bestDist) continue;
            bestDist = d;
            best = reg;
        }
        return best;
    }

    static Vector3 SnapPickupToGround(Vector3 world)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null) return world;
        Vector3 cell = grid.GetCellCenter(world);
        cell.y = grid.Origin.y;
        return cell;
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
        string text = $"Stock: {meals.Count}/{maxCapacity}\n{GetIncomingRateDisplay()}\n{GetCustomerDemandRateDisplay()}\n{GetInventoryDisplay()}";
        if (totalWasted > 0)
            text += $"\nWaste: {totalWasted}";
        return text;
    }

    /// <summary>
    /// Configured production throughput for every food flow that feeds this lamp.
    /// Multiple lines producing the same item are added together.
    /// </summary>
    public string GetIncomingRateDisplay()
    {
        Dictionary<ItemDefinition, float> rates = GetIncomingRates();

        if (rates.Count == 0)
            return "Incoming: None";

        var parts = new List<string>();
        foreach (var pair in rates)
            parts.Add(GetItemDisplayName(pair.Key) + ": " + FormatRate(pair.Value) + "/min");
        parts.Sort(System.StringComparer.OrdinalIgnoreCase);
        return "Incoming: " + string.Join(", ", parts);
    }

    Dictionary<ItemDefinition, float> GetIncomingRates()
    {
        var rates = new Dictionary<ItemDefinition, float>();
        var production = ProductionManager.Instance;
        if (production == null || production.productionFlows == null)
            return rates;

        foreach (ProductionFlowPlan flow in production.productionFlows)
        {
            if (flow == null || flow.stations == null) continue;

            int lampIndex = flow.stations.IndexOf(gameObject);
            if (lampIndex < 0) continue;

            float rate = WorkflowAnalysis.GetFlowBottleneckOutputPerMinute(flow);
            if (rate <= 0.01f) continue;

            foreach (ItemDefinition item in GetFlowProducts(flow, lampIndex))
                rates[item] = rates.TryGetValue(item, out float current) ? current + rate : rate;
        }
        return rates;
    }

    public bool HasProductionShortfall()
    {
        return TryGetProductionShortfallDetails(out _);
    }

    bool TryGetProductionShortfallDetails(out string details)
    {
        details = "";
        var production = ProductionManager.Instance;
        if (production == null) return false;

        Dictionary<ItemDefinition, float> incoming = GetIncomingRates();

        var shortfalls = new List<string>();
        foreach (ProductionManager.ItemOutputNeed need in production.GetRequiredOutputByItem(includeDrinks: true))
        {
            if (need.item == null || need.requiredPerMinute <= 0.01f) continue;
            incoming.TryGetValue(need.item, out float supplied);
            if (supplied + 0.01f < need.requiredPerMinute)
                shortfalls.Add(GetItemDisplayName(need.item) + " is underproducing");
        }
        if (shortfalls.Count == 0) return false;
        details = string.Join("\n", shortfalls);
        return true;
    }

    void RefreshCautionIndicator()
    {
        bool show = HasProductionShortfall();
        if (show && cautionIndicator == null)
            cautionIndicator = CreateCautionIndicator();
        if (cautionIndicator != null)
        {
            cautionIndicator.transform.localPosition = cautionIndicatorOffset;
            cautionIndicator.SetActive(show);
        }
    }

    void HandleCautionClick()
    {
        if (cautionIndicator == null || !cautionIndicator.activeSelf || cautionIndicatorRect == null)
            return;
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null || !RectTransformUtility.RectangleContainsScreenPoint(cautionIndicatorRect, Input.mousePosition, cam))
            return;
        if (!TryGetProductionShortfallDetails(out string details))
            return;

        cautionMessage.text = details;
        cautionMessageGroup.alpha = 1f;
        cautionMessageGroup.gameObject.SetActive(true);
        cautionMessageShownAt = Time.unscaledTime;
    }

    void UpdateCautionMessageFade()
    {
        if (cautionMessageGroup == null || !cautionMessageGroup.gameObject.activeSelf) return;
        float age = Time.unscaledTime - cautionMessageShownAt;
        if (age >= 5f)
        {
            cautionMessageGroup.alpha = 0f;
            cautionMessageGroup.gameObject.SetActive(false);
            return;
        }
        cautionMessageGroup.alpha = age <= 4f ? 1f : 1f - (age - 4f);
    }

    GameObject CreateCautionIndicator()
    {
        Texture2D texture = Resources.Load<Texture2D>("UI/HeatLampCaution");
        if (texture == null)
        {
            Debug.LogWarning("Heat lamp caution icon was not found at Resources/UI/HeatLampCaution.", this);
            return null;
        }

        var root = new GameObject("ProductionShortfallCaution", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        root.transform.localPosition = cautionIndicatorOffset;

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = Vector2.one * 100f;
        rect.localScale = Vector3.one * (Mathf.Max(0.1f, cautionIndicatorSize) / 100f);
        cautionIndicatorRect = rect;

        var imageObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(root.transform, false);
        var imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        var image = imageObject.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;

        var messageObject = new GameObject("ShortfallMessage", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
        messageObject.transform.SetParent(root.transform, false);
        var messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.5f, 1f);
        messageRect.anchorMax = new Vector2(0.5f, 1f);
        messageRect.pivot = new Vector2(0.5f, 0f);
        messageRect.anchoredPosition = new Vector2(0f, 12f);
        messageRect.sizeDelta = new Vector2(360f, 95f);

        cautionMessageGroup = messageObject.GetComponent<CanvasGroup>();
        cautionMessageGroup.alpha = 0f;
        cautionMessageGroup.interactable = false;
        cautionMessageGroup.blocksRaycasts = false;

        cautionMessage = messageObject.GetComponent<TextMeshProUGUI>();
        cautionMessage.fontSize = 25f;
        cautionMessage.fontStyle = FontStyles.Bold;
        cautionMessage.alignment = TextAlignmentOptions.Bottom;
        cautionMessage.color = Color.white;
        cautionMessage.outlineColor = new Color32(35, 38, 40, 255);
        cautionMessage.outlineWidth = 0.28f;
        cautionMessage.enableWordWrapping = true;
        cautionMessage.raycastTarget = false;
        messageObject.SetActive(false);
        return root;
    }

    /// <summary>Full menu demand at this pickup point, including cashier-served drinks.</summary>
    public string GetCustomerDemandRateDisplay()
    {
        var production = ProductionManager.Instance;
        if (production == null)
            return "Customer demand: None";

        var parts = new List<string>();
        foreach (ProductionManager.ItemOutputNeed need in production.GetRequiredOutputByItem(includeDrinks: true))
        {
            if (need.item == null) continue;
            parts.Add(GetItemDisplayName(need.item) + ": " + FormatRate(need.requiredPerMinute) + "/min");
        }

        if (parts.Count == 0)
            return "Customer demand: None";

        parts.Sort(System.StringComparer.OrdinalIgnoreCase);
        return "Customer demand: " + string.Join(", ", parts);
    }

    IEnumerable<ItemDefinition> GetFlowProducts(ProductionFlowPlan flow, int lampIndex)
    {
        var found = new HashSet<ItemDefinition>();
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;

        // Only stations before this lamp can contribute food to it.
        for (int i = 0; i < lampIndex; i++)
        {
            GameObject station = flow.stations[i];
            if (station == null) continue;

            var assembly = station.GetComponent<AssemblyStation>();
            var grill = station.GetComponent<GrillStation>();
            ItemDefinition burger = assembly != null ? assembly.selectedProduct
                : grill != null ? grill.selectedProduct
                : null;
            if (burger == null && (assembly != null || grill != null) && config != null)
                burger = config.burgerBase;
            if (burger != null)
                found.Add(burger);

            if (station.GetComponent<FryerStation>() != null && config != null && config.friesItem != null)
                found.Add(config.friesItem);
        }

        return found;
    }

    static string GetItemDisplayName(ItemDefinition item)
    {
        if (item == null) return "Item";
        var inventory = KitchenInventory.Instance;
        if (inventory != null) return inventory.GetDisplayName(item);
        return !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
    }

    static string FormatRate(float rate)
    {
        if (Mathf.Approximately(rate, Mathf.Round(rate))) return rate.ToString("0");
        if (rate >= 10f) return rate.ToString("0");
        return rate.ToString("0.0");
    }

    public bool DeliverMeal(CustomerOrder order)
    {
        if (order == null || order.lines == null || order.lines.Count == 0)
            return false;
        if (!HasSpace)
            return false;

        meals.Add(new HeldMeal(order.Clone(), Time.time));
        totalDelivered++;
        Sfx.Play(SfxId.HeatLampStock);
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
            var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            if (config != null && config.IsDrink(line.item)) continue;
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

    /// <summary>
    /// Customer grab from the pass: removes food for this order from the lamp.
    /// Drinks are not stored under the lamp (customer fountain / included at pickup).
    /// </summary>
    public bool TryCustomerTakeOrder(CustomerOrder order)
    {
        if (order == null) return false;
        if (!CanFulfill(order) && FindMatchingIndex(order) < 0)
            return false;
        return TryTakeMatching(order) != null;
    }

    int FindSingleItemIndex(ItemDefinition item)
    {
        if (item == null) return -1;
        for (int i = 0; i < meals.Count; i++)
        {
            var o = meals[i]?.order;
            if (o?.lines == null) continue;
            foreach (var line in o.lines)
            {
                if (line.quantity <= 0 || line.item == null) continue;
                if (line.item == item) return i;
                if (!string.IsNullOrEmpty(item.itemName) && line.item.itemName == item.itemName)
                    return i;
            }
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
            Sfx.Play(SfxId.FoodWasted);
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

        Gizmos.color = new Color(0.2f, 0.9f, 0.35f, 0.9f);
        for (int i = 0; i < 4; i++)
            Gizmos.DrawSphere(GetCustomerPickupPosition(i), 0.12f);
    }
}
