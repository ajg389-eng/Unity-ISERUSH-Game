using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Holds which ingredients can be grabbed here.
/// Actual counts come from KitchenInventory (ordered via Management).
/// </summary>
public partial class PantryStation : MonoBehaviour
{
    [Header("Stored ingredient")]
    [Tooltip("The single ingredient this pantry can dispense. Choose it in Management mode.")]
    public ItemDefinition selectedItem;
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one pantry operation.")]
    [Min(0f)] public float processTimeSeconds = 0f;
    [Tooltip("Maximum amount of its assigned ingredient this pantry can dispense per minute.")]
    [Min(0.1f)] public float maxOutputPerMinute = 15f;
    [Tooltip("Show a caution sign when the assigned ingredient reaches this stock level or lower.")]
    [Min(0)] public int lowStockWarningThreshold = 10;
    public Vector3 interactionOffset = Vector3.zero;

    [System.NonSerialized] float outputTokens;
    [System.NonSerialized] float outputTokensUpdatedAt;
    [System.NonSerialized] bool outputTokensInitialized;

    public float EffectiveMaxOutputPerMinute
    {
        get
        {
            StationNode node = GetComponent<StationNode>();
            return Mathf.Max(0f, maxOutputPerMinute)
                * (node != null ? node.ProductionRateMultiplier : 1f);
        }
    }

    Transform itemDisplayRoot;
    readonly List<Transform> itemSpawnMarkers = new List<Transform>();
    ItemDefinition displayedItem;
    int displayedCount = -1;

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
        FindDisplayMarkers();
        RefreshItemDisplay(force: true);
    }

    void Update()
    {
        int stock = selectedItem != null && KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetCount(selectedItem)
            : 0;
        if (displayedItem != selectedItem || displayedCount != stock)
            RefreshItemDisplay(force: true);
    }

    public Vector3 GetInteractionPosition()
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles != null) return tiles.GetFirstInteractionPosition();
        return transform.position + interactionOffset;
    }

    public bool HasItem(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (!CanDispense(item)) return false;
        return GetReadyOutputCount(item) >= amount;
    }

    public int GetReadyOutputCount(ItemDefinition item)
    {
        if (item == null || !CanDispense(item)) return 0;
        var inv = KitchenInventory.Instance;
        int stock = inv != null ? inv.GetCount(item) : int.MaxValue;
        int ready = StationOutputRateLimiter.GetAvailableUnits(EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        return Mathf.Min(stock, ready);
    }

    public bool TakeItem(ItemDefinition item)
    {
        return TakeItems(item, 1);
    }

    public bool TakeItems(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        int ready = GetReadyOutputCount(item);
        int requested = Mathf.Min(amount, ready);
        if (requested <= 0) return false;
        if (requested < amount || ready < amount) return false;
        int limited = StationOutputRateLimiter.TakeUnits(requested, EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        if (limited != amount)
        {
            StationOutputRateLimiter.RefundUnits(limited, ref outputTokens);
            return false;
        }
        if (inv != null && !inv.TryConsume(item, limited))
        {
            StationOutputRateLimiter.RefundUnits(limited, ref outputTokens);
            return false;
        }
        StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(limited);
        return true;
    }

    public bool HasItemSelected => selectedItem != null;
    public bool HasAnyStock => selectedItem != null && HasStoredItem(selectedItem);

    bool HasStoredItem(ItemDefinition item)
    {
        KitchenInventory inventory = KitchenInventory.Instance;
        return inventory == null || inventory.GetCount(item) > 0;
    }

    public string StoredItemsLabel => ItemName(selectedItem);

    static string ItemName(ItemDefinition item) => item == null ? "Select ingredient"
        : string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

    public bool IsAssignedIngredientLow
    {
        get
        {
            if (KitchenInventory.Instance == null) return false;
            int threshold = Mathf.Max(0, lowStockWarningThreshold);
            return selectedItem != null && KitchenInventory.Instance.GetCount(selectedItem) <= threshold;
        }
    }

    public void SetStoredItem(ItemDefinition item)
    {
        selectedItem = item;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    public bool ToggleStoredItem(ItemDefinition item)
    {
        if (item == null) return false;
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        if (config != null && config.IsFreezerIngredient(item)) return false;
        selectedItem = selectedItem == item ? null : item;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
        return true;
    }

    public void SetStoredItems(ItemDefinition first, ItemDefinition second)
    {
        // Keep loading older save data that allowed two ingredients. A Pantry
        // now carries one ingredient, preferring the saved primary selection.
        selectedItem = first != null ? first : second;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    public bool CanDispense(ItemDefinition item)
    {
        if (item == null || item != selectedItem) return false;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config == null || !config.IsFreezerIngredient(item);
    }

    void RefreshItemDisplay(bool force)
    {
        if (!isActiveAndEnabled && !force) return;
        if (itemSpawnMarkers.Count == 0)
            FindDisplayMarkers();

        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "PantryItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        int stock = selectedItem != null && KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetCount(selectedItem)
            : 0;
        displayedItem = selectedItem;
        displayedCount = stock;
        SpawnIngredient(selectedItem, stock, 0, itemSpawnMarkers.Count);
    }

    void FindDisplayMarkers()
    {
        StationItemVisualUtility.FindMarkers(transform, itemSpawnMarkers);
        // Keep each ingredient on adjacent shelves, preserving marker order within a shelf.
        for (int i = 1; i < itemSpawnMarkers.Count; i++)
        {
            Transform marker = itemSpawnMarkers[i];
            float height = transform.InverseTransformPoint(marker.position).y;
            int j = i - 1;
            while (j >= 0 && transform.InverseTransformPoint(itemSpawnMarkers[j].position).y < height - 0.001f)
            {
                itemSpawnMarkers[j + 1] = itemSpawnMarkers[j];
                j--;
            }
            itemSpawnMarkers[j + 1] = marker;
        }
    }

    void SpawnIngredient(ItemDefinition item, int stock, int start, int slots)
    {
        if (item == null || item.prefab == null) return;
        for (int i = 0; i < Mathf.Min(stock, slots); i++)
            StationItemVisualUtility.SpawnAtMarker(item.prefab, itemSpawnMarkers[start + i], itemDisplayRoot,
                "PantryItem_" + (start + i));
    }
}

/// <summary>
/// Displays the same overlay caution icon used by pickup stations whenever a
/// configurable station has no selection, or its assigned stock is low.
/// </summary>
internal sealed class StationConfigurationCaution : MonoBehaviour
{
    const float RefreshInterval = 0.15f;
    const float IconWorldSize = 0.78f;
    const float HeightPadding = 0.45f;

    GameObject indicator;
    RectTransform indicatorRect;
    TextMeshProUGUI explanation;
    CanvasGroup explanationGroup;
    float explanationShownAt = float.NegativeInfinity;
    string currentDetails = "";
    float nextRefresh;

    public void SetControlPopupVisible(bool visible)
    {
        controlPopupVisible = visible;
        RefreshVisibility();
    }

    bool controlPopupVisible;

    public static void Ensure(GameObject station)
    {
        if (station != null && station.GetComponent<StationConfigurationCaution>() == null)
            station.AddComponent<StationConfigurationCaution>();
    }

    void OnEnable()
    {
        nextRefresh = 0f;
        RefreshVisibility();
    }

    void Update()
    {
        HandleClick();
        UpdateExplanationFade();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        RefreshVisibility();
    }

    void LateUpdate()
    {
        if (indicator == null || !indicator.activeSelf) return;
        UpdateTransform();
    }

    void OnDestroy()
    {
        if (indicator != null)
            Destroy(indicator);
    }

    void RefreshVisibility()
    {
        bool show = !controlPopupVisible && TryGetCautionDetails(out currentDetails);
        if (show && indicator == null)
            indicator = CreateIndicator();
        if (indicator == null) return;

        indicator.SetActive(show);
        if (show) UpdateTransform();
    }

    bool TryGetCautionDetails(out string details)
    {
        Register register = GetComponent<Register>();
        if (register != null && !register.isEnabled)
        {
            details = "REGISTER DISABLED\nClick the register and choose Enable.";
            return true;
        }

        if (!HasConfiguration())
        {
            details = "NOT CONFIGURED\nSelect a recipe or ingredient.";
            return true;
        }
        if (TryGetLowStockItem(out ItemDefinition lowItem, out int stock))
        {
            string item = !string.IsNullOrEmpty(lowItem.itemName) ? lowItem.itemName : lowItem.name;
            details = stock <= 0
                ? item.ToUpperInvariant() + " OUT OF STOCK\nOrder more stock."
                : item.ToUpperInvariant() + " LOW (" + stock + " LEFT)\nOrder more stock.";
            return true;
        }
        return TryGetBottleneckDetails(out details);
    }

    bool TryGetBottleneckDetails(out string details)
    {
        details = "";
        ProductionManager production = ProductionManager.Instance;
        StationNode ownNode = GetComponent<StationNode>();
        if (production == null || ownNode == null
            || production.productionFlows == null) return false;

        foreach (ProductionFlowPlan flow in production.productionFlows)
        {
            if (flow?.stations == null || !flow.stations.Contains(gameObject)
                || flow.workers == null || flow.workers.Count == 0) continue;
            flow.EnsureLegacyConnections();

            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection?.to != gameObject || connection.from == null) continue;
                StationRuntimeMetrics upstream = connection.from.GetComponent<StationRuntimeMetrics>();
                if (upstream == null || upstream.CurrentState != StationRuntimeState.Blocked
                    || upstream.CurrentStateSeconds < 4f) continue;
                details = "INPUT BLOCKED " + upstream.CurrentStateSeconds.ToString("0") + "s\n"
                    + "Free space at " + ownNode.DisplayName + ".";
                return true;
            }

            // Capacity is determined by connected, recipe-compatible supply.
            // A low-rate parallel station can still contribute to a merge, so
            // comparing this station against the lowest rate in the whole flow
            // produces false bottleneck warnings. Runtime input-blocked states
            // above remain the reliable local warning for this overlay.
        }
        return false;
    }

    bool HasConfiguration()
    {
        var grill = GetComponent<GrillStation>();
        if (grill != null) return grill.HasProductSelected;

        var assembly = GetComponent<AssemblyStation>();
        if (assembly != null) return assembly.HasProductSelected;

        var cutting = GetComponent<CuttingStation>();
        if (cutting != null) return cutting.HasRecipeSelected;

        var pantry = GetComponent<PantryStation>();
        if (pantry != null) return pantry.HasItemSelected;

        var freezer = GetComponent<FreezerStation>();
        if (freezer != null) return freezer.HasItemSelected;

        return true;
    }

    bool TryGetLowStockItem(out ItemDefinition item, out int stock)
    {
        item = null;
        stock = 0;
        KitchenInventory inventory = KitchenInventory.Instance;
        if (inventory == null) return false;

        PantryStation pantry = GetComponent<PantryStation>();
        if (pantry != null)
        {
            int threshold = Mathf.Max(0, pantry.lowStockWarningThreshold);
            if (IsLow(pantry.selectedItem, threshold, inventory, out stock))
            {
                item = pantry.selectedItem;
                return !OnboardingTutorial.BlocksProgression;
            }
            return false;
        }

        FreezerStation freezer = GetComponent<FreezerStation>();
        if (freezer == null || !IsLow(freezer.selectedItem,
                Mathf.Max(0, freezer.lowStockWarningThreshold), inventory, out stock))
            return false;
        item = freezer.selectedItem;
        // During onboarding, stock is empty until the later buy-ingredients step.
        // Keep the yellow icon for missing selections only.
        return !OnboardingTutorial.BlocksProgression;
    }

    static bool IsLow(ItemDefinition candidate, int threshold, KitchenInventory inventory,
        out int stock)
    {
        stock = 0;
        if (candidate == null) return false;
        stock = inventory.GetCount(candidate);
        return stock <= threshold;
    }

    GameObject CreateIndicator()
    {
        Texture2D texture = Resources.Load<Texture2D>("UI/HeatLampCaution");
        if (texture == null)
        {
            Debug.LogWarning("Station caution icon was not found at Resources/UI/HeatLampCaution.", this);
            return null;
        }

        var root = new GameObject("MissingRecipeCaution", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        // Keep the world warning above scene geometry, but below HUDs, menus,
        // popups, and other screen-space UI.
        canvas.sortingOrder = -100;

        var contentObject = new GameObject("Indicator", typeof(RectTransform));
        contentObject.transform.SetParent(root.transform, false);
        var rect = contentObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one * 100f;
        rect.localScale = Vector3.one * (IconWorldSize / 100f);
        indicatorRect = rect;

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        icon.transform.SetParent(contentObject.transform, false);
        var iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        var image = icon.GetComponent<UnityEngine.UI.RawImage>();
        image.texture = texture;
        image.raycastTarget = false;

        var panel = new GameObject("CautionExplanation", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
        panel.transform.SetParent(contentObject.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 14f);
        panelRect.sizeDelta = new Vector2(360f, 78f);

        var background = panel.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.055f, 0.07f, 0.09f, 0.94f);
        background.raycastTarget = false;
        var border = panel.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = new Color(1f, 0.73f, 0.12f, 0.95f);
        border.effectDistance = new Vector2(2f, -2f);

        var message = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        message.transform.SetParent(panel.transform, false);
        var messageRect = message.GetComponent<RectTransform>();
        messageRect.anchorMin = Vector2.zero;
        messageRect.anchorMax = Vector2.one;
        messageRect.offsetMin = new Vector2(12f, 8f);
        messageRect.offsetMax = new Vector2(-12f, -8f);

        explanationGroup = panel.GetComponent<CanvasGroup>();
        explanationGroup.alpha = 0f;
        explanationGroup.interactable = false;
        explanationGroup.blocksRaycasts = false;
        explanation = message.GetComponent<TextMeshProUGUI>();
        explanation.fontSize = 21f;
        explanation.fontStyle = FontStyles.Bold;
        explanation.alignment = TextAlignmentOptions.Center;
        explanation.color = Color.white;
        explanation.outlineWidth = 0f;
        explanation.textWrappingMode = TextWrappingModes.Normal;
        explanation.raycastTarget = false;
        panel.SetActive(false);
        GameUITheme.ApplyToCanvas(canvas);
        return root;
    }

    void HandleClick()
    {
        if (indicator == null || !indicator.activeSelf || indicatorRect == null
            || !Input.GetMouseButtonDown(0)) return;
        Camera cam = Camera.main;
        if (cam == null || !RectTransformUtility.RectangleContainsScreenPoint(
                indicatorRect, Input.mousePosition, null)) return;
        if (explanation == null || explanationGroup == null || string.IsNullOrEmpty(currentDetails)) return;
        explanation.text = currentDetails;
        explanationGroup.gameObject.SetActive(true);
        explanationGroup.alpha = 1f;
        explanationShownAt = Time.unscaledTime;
    }

    void UpdateExplanationFade()
    {
        if (explanationGroup == null || !explanationGroup.gameObject.activeSelf) return;
        float age = Time.unscaledTime - explanationShownAt;
        if (age >= 6f)
        {
            explanationGroup.alpha = 0f;
            explanationGroup.gameObject.SetActive(false);
            return;
        }
        explanationGroup.alpha = age <= 5f ? 1f : 1f - (age - 5f);
    }

    void UpdateTransform()
    {
        if (indicator == null) return;

        float top = transform.position.y + 1.5f;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
                top = Mathf.Max(top, renderers[i].bounds.max.y);
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        // Keep the warning tied to the station while rendering it in an overlay
        // canvas, so walls and other meshes cannot cover the icon or its text.
        Vector3 worldPosition = new Vector3(transform.position.x, top + HeightPadding, transform.position.z);
        Vector3 screenPosition = cam.WorldToScreenPoint(worldPosition);
        Vector3 screenEdge = cam.WorldToScreenPoint(worldPosition + cam.transform.right * IconWorldSize);
        float screenSize = Vector2.Distance(
            new Vector2(screenPosition.x, screenPosition.y),
            new Vector2(screenEdge.x, screenEdge.y));
        RectTransform iconTransform = indicatorRect;
        RectTransform canvasRect = indicator.GetComponent<RectTransform>();
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, new Vector2(screenPosition.x, screenPosition.y), null, out Vector2 localPosition))
            iconTransform.anchoredPosition = localPosition;
        else
            iconTransform.position = screenPosition;
        iconTransform.localScale = Vector3.one * (Mathf.Max(1f, screenSize) / 100f);
        iconTransform.rotation = Quaternion.identity;
    }
}

/// <summary>Shared support for scene-authored ItemSpawn marker spheres.</summary>
internal static class StationItemVisualUtility
{
    public static void FindMarkers(Transform station, List<Transform> results)
    {
        results.Clear();
        if (station == null) return;
        Transform[] children = station.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == station || !IsMarkerName(child.name)) continue;
            results.Add(child);
            SetMarkerVisible(child, false);
        }
        results.Sort((a, b) => MarkerIndex(a.name).CompareTo(MarkerIndex(b.name)));
    }

    public static Transform GetOrCreateDisplayRoot(Transform station, string name)
    {
        Transform existing = station.Find(name);
        if (existing != null) return existing;
        GameObject root = new GameObject(name);
        root.transform.SetParent(station, false);
        return root.transform;
    }

    public static void ClearChildren(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
            Object.Destroy(root.GetChild(i).gameObject);
    }

    public static GameObject SpawnAtMarker(GameObject prefab, Transform marker, Transform parent, string name)
    {
        if (prefab == null || marker == null || parent == null) return null;
        GameObject display = Object.Instantiate(prefab, parent);
        display.name = name + "_" + prefab.name;
        display.transform.position = marker.position;
        display.transform.rotation = marker.rotation * prefab.transform.localRotation;
        SetNativeWorldScale(display.transform, prefab.transform.localScale);
        foreach (Collider collider in display.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        return display;
    }

    static bool IsMarkerName(string value) =>
        value == "ItemSpawn" || value.StartsWith("ItemSpawn (")
        || IsNumberedInput(value);

    static bool IsNumberedInput(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("Input",
                System.StringComparison.OrdinalIgnoreCase) || value.Length <= 5) return false;
        return char.IsDigit(value[5]);
    }

    static int MarkerIndex(string value)
    {
        if (value == "ItemSpawn") return 0;
        int open = value.LastIndexOf('(');
        int close = value.LastIndexOf(')');
        return open >= 0 && close > open && int.TryParse(value.Substring(open + 1, close - open - 1), out int index)
            ? index
            : int.MaxValue;
    }

    static void SetMarkerVisible(Transform marker, bool visible)
    {
        foreach (Renderer renderer in marker.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
        foreach (Collider collider in marker.GetComponentsInChildren<Collider>(true))
            collider.enabled = visible;
    }

    static void SetNativeWorldScale(Transform target, Vector3 sourceScale)
    {
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            sourceScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            sourceScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            sourceScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }
}
