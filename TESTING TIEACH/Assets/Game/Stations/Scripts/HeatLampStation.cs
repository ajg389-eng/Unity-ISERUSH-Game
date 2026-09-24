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
    public const int FixedCapacity = 4;
    public static HeatLampStation Instance { get; private set; }

    [Header("Capacity")]
    [Tooltip("Maximum meals that can sit under the lamp at once")]
    public int maxCapacity = FixedCapacity;
    [Tooltip("Kitchen tries to keep this many meals ready (demand + buffer)")]
    public int targetStock = 3;

    [Header("Expiry")]
    [Tooltip("Seconds a meal can sit before it is thrown out")]
    public float expireAfterSeconds = 28f;

    [Header("Interaction")]
    public Vector3 interactionOffset = Vector3.zero;

    [Header("Customer pickup (lobby / pass-through side)")]
    [Tooltip("Offset from the Pickup Station to the customer stand. Leave zero to auto-place opposite the worker tile.")]
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

    [Header("Food display")]
    [Tooltip("Finished burger model shown on an occupied Pickup Station tile.")]
    public GameObject burgerDisplayPrefab;
    [Tooltip("Finished fries model shown on an occupied Pickup Station tile.")]
    public GameObject friesDisplayPrefab;
    [Tooltip("Finished drink model shown on an occupied Pickup Station tile.")]
    public GameObject drinkDisplayPrefab;
    [Tooltip("Height of product models above the Pickup Station root.")]
    public float foodDisplayHeight = 0.55f;
    [Tooltip("Uniform world-space scale used by displayed food models.")]
    public float foodDisplayScale = 0.42f;
    [Tooltip("Maximum world-space size of the drink model. Drink prefabs use different native dimensions than food prefabs.")]
    public float drinkDisplaySize = 0.42f;
    [Tooltip("Local X/Z center of the four display pans on the current 1x1 model.")]
    public Vector2 foodDisplayCenter = new Vector2(-0.004f, 0.032f);
    [Tooltip("Local X/Z spacing between the four display positions.")]
    public Vector2 foodDisplaySpacing = new Vector2(0.5f, 0.5f);

    readonly List<HeldMeal> meals = new List<HeldMeal>();
    readonly List<CustomerAI> customerPickupQueue = new List<CustomerAI>();
    int totalWasted;
    int totalDelivered;
    int totalSold;
    GameObject cautionIndicator;
    RectTransform cautionIndicatorRect;
    TextMeshProUGUI cautionMessage;
    CanvasGroup cautionMessageGroup;
    float cautionMessageShownAt = float.NegativeInfinity;
    float nextCautionRefresh;
    Transform foodDisplayRoot;
    readonly List<GameObject> foodDisplayObjects = new List<GameObject>();

    public int Count => meals.Count;
    public int TotalWasted => totalWasted;
    public int TotalDelivered => totalDelivered;
    public int TotalSold => totalSold;
    public bool HasSpace => meals.Count < maxCapacity;
    public IReadOnlyList<HeldMeal> Meals => meals;
    public int PickupQueueCount => customerPickupQueue.Count;

    void Awake()
    {
        maxCapacity = FixedCapacity;
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
        RefreshFoodDisplay();
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
        CustomerAI[] waiting = customerPickupQueue.ToArray();
        customerPickupQueue.Clear();
        foreach (CustomerAI customer in waiting)
            if (customer != null) customer.OnPickupStationUnavailable(this);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (cautionIndicator != null)
            Destroy(cautionIndicator);
    }

    /// <summary>
    /// Rebuilds the physical stock display from the real held-meal list.
    /// </summary>
    void RefreshFoodDisplay()
    {
        EnsureFoodDisplayRoot();
        ClearFoodDisplay();

        int visibleCount = Mathf.Min(meals.Count, FixedCapacity);
        for (int i = 0; i < visibleCount; i++)
        {
            ItemDefinition item = meals[i]?.order?.PrimaryItem;
            GameObject prefab = GetFoodDisplayPrefab(item);
            if (prefab == null) continue;

            GameObject display = Instantiate(prefab, foodDisplayRoot);
            display.name = "HeldFood_" + i + "_" + prefab.name;
            display.transform.localPosition = GetFoodDisplaySlot(i);
            display.transform.localRotation = Quaternion.identity;
            if (prefab == drinkDisplayPrefab)
                NormalizeDisplaySize(display, drinkDisplaySize);
            else
                SetUniformWorldScale(display.transform, foodDisplayScale);
            DisableDisplayColliders(display);
            foodDisplayObjects.Add(display);
        }
    }

    void EnsureFoodDisplayRoot()
    {
        if (foodDisplayRoot != null) return;
        Transform existing = transform.Find("HeldFoodDisplay");
        if (existing != null)
        {
            foodDisplayRoot = existing;
            return;
        }

        var root = new GameObject("HeldFoodDisplay");
        foodDisplayRoot = root.transform;
        foodDisplayRoot.SetParent(transform, false);
    }

    void ClearFoodDisplay()
    {
        foodDisplayObjects.Clear();

        // Also handles play-mode script reloads where the non-serialized list is lost.
        if (foodDisplayRoot == null) return;
        for (int i = foodDisplayRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = foodDisplayRoot.GetChild(i).gameObject;
            if (child != null)
                Destroy(child);
        }
    }

    Vector3 GetFoodDisplaySlot(int index)
    {
        int column = index % 2;
        int row = index / 2;
        float x = foodDisplayCenter.x + (column - 0.5f) * foodDisplaySpacing.x;
        float z = foodDisplayCenter.y + (row - 0.5f) * foodDisplaySpacing.y;
        return new Vector3(x, foodDisplayHeight, z);
    }

    GameObject GetFoodDisplayPrefab(ItemDefinition item)
    {
        if (item == null) return null;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig
            : null;

        if (config != null)
        {
            if (config.IsBurger(item)) return burgerDisplayPrefab;
            if (config.IsFries(item)) return friesDisplayPrefab;
            if (config.IsDrink(item)) return drinkDisplayPrefab;
        }

        string label = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
        if (label.IndexOf("burger", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return burgerDisplayPrefab;
        if (label.IndexOf("fries", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return friesDisplayPrefab;
        if (label.IndexOf("drink", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return drinkDisplayPrefab;
        return null;
    }

    static void SetUniformWorldScale(Transform target, float scale)
    {
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            scale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }

    static void NormalizeDisplaySize(GameObject display, float targetSize)
    {
        display.transform.localScale = Vector3.one;

        Renderer[] renderers = display.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > 0.0001f)
        {
            float uniformScale = Mathf.Max(0.01f, targetSize) / largestDimension;
            display.transform.localScale = Vector3.one * uniformScale;
        }
    }

    static void DisableDisplayColliders(GameObject display)
    {
        foreach (Collider collider in display.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    void Update()
    {
        if (customerPickupQueue.RemoveAll(customer => customer == null) > 0)
            RefreshPickupQueueTargets();
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
        UpdateCautionIndicatorTransform();
    }

    void UpdateCautionIndicatorTransform()
    {
        if (cautionIndicator == null) return;

        Transform indicator = cautionIndicator.transform;
        indicator.position = transform.position + cautionIndicatorOffset;
        indicator.localScale = Vector3.one * (Mathf.Max(0.1f, cautionIndicatorSize) / 100f);

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 cameraToIndicator = indicator.position - cam.transform.position;
        if (cameraToIndicator.sqrMagnitude > 0.0001f)
            indicator.rotation = Quaternion.LookRotation(cameraToIndicator.normalized, cam.transform.up);
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
        // Customer floor is always on the world-east side of the counter.
        // Keep this independent of the prefab rotation and worker stand tiles.
        return Vector3.right;
    }

    /// <summary>World stand point for a customer at the pass (index 0 = at the counter).</summary>
    public Vector3 GetCustomerPickupPosition(int index = 0)
    {
        GridManager placementGrid = GridManager.Instance;
        float cell = placementGrid != null
            ? Mathf.Max(0.01f, placementGrid.cellSize)
            : Mathf.Max(0.5f, customerPickupSpacing);
        Vector3 origin = placementGrid != null ? placementGrid.Origin : Vector3.zero;

        Renderer visual = GetComponentInChildren<Renderer>();
        float maxX = visual != null ? visual.bounds.max.x : transform.position.x;
        float centerZ = visual != null ? visual.bounds.center.z : transform.position.z;

        int firstX = Mathf.FloorToInt((maxX + 0.01f - origin.x) / cell);
        int rowZ = Mathf.FloorToInt((centerZ - origin.z) / cell);

        Vector3 slot = new Vector3(
            origin.x + (firstX + Mathf.Max(0, index) + 0.5f) * cell,
            transform.position.y,
            origin.z + (rowZ + 0.5f) * cell);
        return SnapPickupToGround(slot);
    }

    public static HeatLampStation FindNearest(Vector3 worldPosition)
    {
        HeatLampStation[] stations = FindObjectsByType<HeatLampStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        HeatLampStation best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < stations.Length; i++)
        {
            HeatLampStation station = stations[i];
            if (station == null) continue;
            float distance = (station.transform.position - worldPosition).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = station;
        }
        return best;
    }

    public bool TryJoinPickupQueue(CustomerAI customer)
    {
        if (customer == null) return false;
        if (!customerPickupQueue.Contains(customer))
            customerPickupQueue.Add(customer);
        RefreshPickupQueueTargets();
        return true;
    }

    public void LeavePickupQueue(CustomerAI customer)
    {
        if (customer == null) return;
        if (customerPickupQueue.Remove(customer))
            RefreshPickupQueueTargets();
    }

    void RefreshPickupQueueTargets()
    {
        for (int i = 0; i < customerPickupQueue.Count; i++)
        {
            CustomerAI customer = customerPickupQueue[i];
            if (customer == null) continue;
            customer.SetPickupSlot(this, GetCustomerPickupPosition(i), i == 0);
        }
    }

    public static HeatLampStation FindBestPickupFor(ItemDefinition item, Vector3 customerPosition)
    {
        if (item == null) return null;
        HeatLampStation[] stations = FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        HeatLampStation best = null;
        int bestQueue = int.MaxValue;
        float bestDistance = float.MaxValue;

        for (int pass = 0; pass < 2 && best == null; pass++)
        {
            foreach (HeatLampStation station in stations)
            {
                if (station == null) continue;
                bool suitable = station.CanProvideItem(item);
                if (pass == 0 && !suitable) continue;

                int queue = station.PickupQueueCount;
                float distance = (station.transform.position - customerPosition).sqrMagnitude;
                if (queue > bestQueue || (queue == bestQueue && distance >= bestDistance)) continue;
                best = station;
                bestQueue = queue;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Nearest pass that already has the requested item ready for collection.</summary>
    public static HeatLampStation FindReadyPickupFor(ItemDefinition item, Vector3 customerPosition)
    {
        if (item == null) return null;
        HeatLampStation best = null;
        float bestDistance = float.MaxValue;
        foreach (HeatLampStation station in FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (station == null || !station.HasSingleItem(item)) continue;
            float distance = (station.transform.position - customerPosition).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = station;
        }
        return best;
    }

    /// <summary>Best pickup station holding at least one item still on this order.</summary>
    public static HeatLampStation FindReadyPickupForOrder(CustomerOrder order,
        Vector3 customerPosition, HeatLampStation excluded = null)
    {
        if (order == null || order.GetTotalQuantity() <= 0) return null;
        HeatLampStation best = null;
        int bestQueue = int.MaxValue;
        float bestDistance = float.MaxValue;
        foreach (HeatLampStation station in FindObjectsByType<HeatLampStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (station == null || station == excluded || !station.HasAnyItemFor(order)) continue;
            int queue = station.PickupQueueCount;
            float distance = (station.transform.position - customerPosition).sqrMagnitude;
            if (queue > bestQueue || (queue == bestQueue && distance >= bestDistance)) continue;
            best = station;
            bestQueue = queue;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>
    /// Finds a station that is ready now, or otherwise one whose assigned flow can
    /// eventually provide any remaining item in the order.
    /// </summary>
    public static HeatLampStation FindBestPickupForOrder(CustomerOrder order, Vector3 customerPosition)
    {
        HeatLampStation ready = FindReadyPickupForOrder(order, customerPosition);
        if (ready != null) return ready;
        if (order?.lines == null) return null;

        HeatLampStation best = null;
        int bestQueue = int.MaxValue;
        float bestDistance = float.MaxValue;
        foreach (HeatLampStation station in FindObjectsByType<HeatLampStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (station == null || !station.CanProvideAnyItem(order)) continue;
            int queue = station.PickupQueueCount;
            float distance = (station.transform.position - customerPosition).sqrMagnitude;
            if (queue > bestQueue || (queue == bestQueue && distance >= bestDistance)) continue;
            best = station;
            bestQueue = queue;
            bestDistance = distance;
        }
        return best != null ? best : FindNearest(customerPosition);
    }

    public bool CanProvideItem(ItemDefinition item)
    {
        if (item == null) return false;
        if (HasSingleItem(item)) return true;
        var production = ProductionManager.Instance;
        if (production == null || production.productionFlows == null) return false;

        foreach (ProductionFlowPlan flow in production.productionFlows)
        {
            if (flow == null || flow.stations == null) continue;
            int lampIndex = flow.stations.IndexOf(gameObject);
            if (lampIndex < 0) continue;
            foreach (ItemDefinition product in GetFlowProducts(flow, lampIndex))
                if (CustomerOrder.ItemsEquivalent(product, item)) return true;
        }
        return false;
    }

    public bool CanProvideAnyItem(CustomerOrder customerOrder)
    {
        if (customerOrder?.lines == null) return false;
        foreach (CustomerOrder.OrderLine line in customerOrder.lines)
            if (line.item != null && line.quantity > 0 && CanProvideItem(line.item))
                return true;
        return false;
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

        float queueStep = GridManager.Instance != null
            ? Mathf.Max(0.01f, GridManager.Instance.cellSize)
            : customerPickupSpacing;
        Vector3 pos = origin + alongCounter * lateral + Vector3.right * (queueStep * Mathf.Max(0, index));
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

        // Use the work grid's alignment without clamping to its bounds. Customer
        // pickup cells live on the separate customer floor east of that grid.
        float size = Mathf.Max(0.01f, grid.cellSize);
        int x = Mathf.FloorToInt((world.x - grid.Origin.x) / size);
        int z = Mathf.FloorToInt((world.z - grid.Origin.z) / size);
        return new Vector3(
            grid.Origin.x + (x + 0.5f) * size,
            grid.Origin.y,
            grid.Origin.z + (z + 0.5f) * size);
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

        // A pickup station should only report products from flows that actually
        // deliver to this specific station. Previously, every demanded item was
        // checked and a missing incoming entry was interpreted as a zero rate.
        // That made unrelated underproduction show a caution sign here.
        var needs = production.GetRequiredOutputByItem(includeDrinks: true);
        var shortfalls = new List<string>();
        foreach (KeyValuePair<ItemDefinition, float> delivery in incoming)
        {
            if (delivery.Key == null || delivery.Value <= 0.01f) continue;

            float requiredPerMinute = 0f;
            foreach (ProductionManager.ItemOutputNeed need in needs)
            {
                if (need.item != null && CustomerOrder.ItemsEquivalent(need.item, delivery.Key))
                    requiredPerMinute += Mathf.Max(0f, need.requiredPerMinute);
            }

            if (requiredPerMinute > 0.01f && delivery.Value + 0.01f < requiredPerMinute)
                shortfalls.Add(GetItemDisplayName(delivery.Key) + " is underproducing");
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
            UpdateCautionIndicatorTransform();
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
        root.transform.position = transform.position + cautionIndicatorOffset;

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

    /// <summary>Full menu demand at this pickup point, including worker-delivered drinks.</summary>
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
            if (station.GetComponent<DrinkStation>() != null && config != null && config.drinkItem != null)
                found.Add(config.drinkItem);
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
        var need = new Dictionary<ItemDefinition, int>();
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
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

    public bool HasAnyItemFor(CustomerOrder customerOrder)
    {
        return TryGetAvailableItem(customerOrder, out _);
    }

    public bool TryGetAvailableItem(CustomerOrder customerOrder, out ItemDefinition item)
    {
        item = null;
        if (customerOrder?.lines == null) return false;
        foreach (CustomerOrder.OrderLine line in customerOrder.lines)
        {
            if (line.item == null || line.quantity <= 0 || !HasSingleItem(line.item)) continue;
            item = line.item;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Next ready item this customer may take. People ahead in this pickup line
    /// keep first claim on matching food.
    /// </summary>
    public bool TryGetAvailableItemForCustomer(CustomerAI customer, CustomerOrder customerOrder, out ItemDefinition item)
    {
        item = null;
        if (customer == null || customerOrder?.lines == null) return false;
        foreach (CustomerOrder.OrderLine line in customerOrder.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;
            if (CountHeldMatching(line.item) <= CountClaimsAhead(customer, line.item)) continue;
            item = line.item;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Takes one ready item from any placed pickup station while preserving the
    /// customer's priority in the pickup queue they are currently standing in.
    /// Pickup stations therefore behave as one shared serving inventory instead
    /// of trapping customers behind the stock assigned to a particular counter.
    /// </summary>
    public static bool TryCustomerTakeAvailableItem(CustomerAI customer,
        HeatLampStation queueStation, CustomerOrder customerOrder, out ItemDefinition item)
    {
        item = null;
        if (customer == null || customerOrder?.lines == null) return false;

        HeatLampStation[] stations = FindObjectsByType<HeatLampStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (CustomerOrder.OrderLine line in customerOrder.lines)
        {
            if (line.item == null || line.quantity <= 0) continue;

            int totalHeld = 0;
            foreach (HeatLampStation station in stations)
                if (station != null)
                    totalHeld += station.CountHeldMatching(line.item);

            int claimsAhead = queueStation != null
                ? queueStation.CountClaimsAhead(customer, line.item)
                : 0;
            if (totalHeld <= claimsAhead) continue;

            HeatLampStation source = null;
            float nearestDistance = float.MaxValue;
            foreach (HeatLampStation station in stations)
            {
                if (station == null || !station.HasSingleItem(line.item)) continue;
                float distance = (station.transform.position - customer.transform.position).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                source = station;
            }

            if (source == null || !source.TryCustomerTakeSingleItem(line.item)) continue;
            item = line.item;
            return true;
        }

        return false;
    }

    int CountHeldMatching(ItemDefinition item)
    {
        if (item == null) return 0;
        int n = 0;
        for (int i = 0; i < meals.Count; i++)
        {
            CustomerOrder held = meals[i]?.order;
            if (held == null) continue;
            n += held.CountQuantityOf(item);
        }
        return n;
    }

    int CountClaimsAhead(CustomerAI customer, ItemDefinition item)
    {
        if (customer == null || item == null) return 0;
        int claims = 0;
        for (int i = 0; i < customerPickupQueue.Count; i++)
        {
            CustomerAI other = customerPickupQueue[i];
            if (other == customer) break;
            if (other == null) continue;
            CustomerOrder otherOrder = other.GetOrder();
            if (otherOrder == null) continue;
            claims += otherOrder.CountQuantityOf(item);
        }
        return claims;
    }

    public CustomerOrder TryTakeSingleItem(ItemDefinition item)
    {
        int idx = FindSingleItemIndex(item);
        if (idx < 0) return null;
        var meal = meals[idx];
        ItemDefinition held = meal?.order?.PrimaryItem;
        if (meal?.order == null || !meal.order.TryRemoveOne(item)) return null;
        if (meal.order.GetTotalQuantity() <= 0)
            meals.RemoveAt(idx);
        RefreshStatusLabel();
        return CustomerOrder.FromItem(held != null ? held : item);
    }

    public bool TryCustomerTakeSingleItem(ItemDefinition item)
    {
        CustomerOrder taken = TryTakeSingleItem(item);
        if (taken == null) return false;
        totalSold++;
        return true;
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

    /// <summary>
    /// Customer grab from the pickup station: removes every stored product needed
    /// for the order, including drinks and future menu item types.
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
                if (CustomerOrder.ItemsEquivalent(line.item, item))
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
        RefreshFoodDisplay();
        // Stock changes may make a later customer's order ready before the first
        // customer's order, so reevaluate the full pickup line immediately.
        RefreshPickupQueueTargets();
        if (statusLabel != null)
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
