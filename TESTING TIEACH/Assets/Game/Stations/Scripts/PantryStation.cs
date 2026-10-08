using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Holds which ingredients can be grabbed here.
/// Actual counts come from KitchenInventory (ordered via Management).
/// </summary>
public class PantryStation : MonoBehaviour
{
    [Header("Stored ingredients (up to two)")]
    [Tooltip("First ingredient this pantry can dispense. Choose ingredients in Management mode.")]
    public ItemDefinition selectedItem;
    public ItemDefinition secondItem;
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one pantry operation.")]
    [Min(0f)] public float processTimeSeconds = 0f;
    [Tooltip("Show a caution sign when the assigned ingredient reaches this stock level or lower.")]
    [Min(0)] public int lowStockWarningThreshold = 10;
    public Vector3 interactionOffset = Vector3.zero;

    Transform itemDisplayRoot;
    readonly List<Transform> itemSpawnMarkers = new List<Transform>();
    ItemDefinition displayedItem;
    int displayedCount = -1;
    ItemDefinition displayedSecondItem;
    int displayedSecondCount = -1;

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
        int secondStock = secondItem != null && KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetCount(secondItem) : 0;
        if (displayedItem != selectedItem || displayedSecondItem != secondItem
            || displayedCount != stock || displayedSecondCount != secondStock)
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
        var inv = KitchenInventory.Instance;
        if (inv == null) return true; // legacy infinite if no inventory system
        return inv.Has(item, amount);
    }

    public bool TakeItem(ItemDefinition item)
    {
        if (item == null || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        bool taken = inv == null || inv.TryConsume(item, 1);
        if (taken) StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(1);
        return taken;
    }

    public bool TakeItems(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        bool taken = inv == null || inv.TryConsume(item, amount);
        if (taken) StationRuntimeMetrics.EnsureOn(gameObject)?.RecordOutput(amount);
        return taken;
    }

    public bool HasItemSelected => selectedItem != null || secondItem != null;
    public bool HasAnyStock => (selectedItem != null && HasItem(selectedItem))
        || (secondItem != null && HasItem(secondItem));
    public string StoredItemsLabel => selectedItem == null
        ? ItemName(secondItem)
        : secondItem == null ? ItemName(selectedItem) : ItemName(selectedItem) + " / " + ItemName(secondItem);

    static string ItemName(ItemDefinition item) => item == null ? "Select ingredients"
        : string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

    public bool IsAssignedIngredientLow
    {
        get
        {
            if (KitchenInventory.Instance == null) return false;
            int threshold = Mathf.Max(0, lowStockWarningThreshold);
            return (selectedItem != null && KitchenInventory.Instance.GetCount(selectedItem) <= threshold)
                || (secondItem != null && KitchenInventory.Instance.GetCount(secondItem) <= threshold);
        }
    }

    public void SetStoredItem(ItemDefinition item)
    {
        selectedItem = item;
        if (secondItem == item) secondItem = null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    public bool ToggleStoredItem(ItemDefinition item)
    {
        if (item == null) return false;
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        if (config != null && config.IsFreezerIngredient(item)) return false;
        if (selectedItem == item) { selectedItem = secondItem; secondItem = null; }
        else if (secondItem == item) secondItem = null;
        else if (selectedItem == null) selectedItem = item;
        else if (secondItem == null) secondItem = item;
        else return false;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
        return true;
    }

    public void SetStoredItems(ItemDefinition first, ItemDefinition second)
    {
        selectedItem = first != null ? first : second;
        secondItem = first != null && second != first ? second : null;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    public bool CanDispense(ItemDefinition item)
    {
        if (item == null || (item != selectedItem && item != secondItem)) return false;
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
        displayedSecondItem = secondItem;
        displayedSecondCount = secondItem != null && KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetCount(secondItem) : 0;
        int firstSlots = secondItem != null ? (itemSpawnMarkers.Count + 1) / 2 : itemSpawnMarkers.Count;
        SpawnIngredient(selectedItem, stock, 0, firstSlots);
        SpawnIngredient(secondItem, displayedSecondCount, firstSlots, itemSpawnMarkers.Count - firstSlots);
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
/// Displays the same world-space caution icon used by pickup stations whenever a
/// configurable station has no selection, or a pantry's assigned stock is low.
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
        bool show = TryGetCautionDetails(out currentDetails);
        if (show && indicator == null)
            indicator = CreateIndicator();
        if (indicator == null) return;

        indicator.SetActive(show);
        if (show) UpdateTransform();
    }

    bool TryGetCautionDetails(out string details)
    {
        if (!HasConfiguration())
        {
            details = "NOT CONFIGURED\nSelect a recipe or ingredient.";
            return true;
        }
        if (HasLowPantryStock())
        {
            PantryStation pantry = GetComponent<PantryStation>();
            string item = pantry != null && pantry.selectedItem != null
                ? (!string.IsNullOrEmpty(pantry.selectedItem.itemName)
                    ? pantry.selectedItem.itemName : pantry.selectedItem.name)
                : "Ingredient";
            details = item.ToUpperInvariant() + " LOW\nOrder more stock.";
            return true;
        }
        return TryGetBottleneckDetails(out details);
    }

    bool TryGetBottleneckDetails(out string details)
    {
        details = "";
        ProductionManager production = ProductionManager.Instance;
        StationRuntimeMetrics ownMetrics = GetComponent<StationRuntimeMetrics>();
        StationNode ownNode = GetComponent<StationNode>();
        if (production == null || ownMetrics == null || ownNode == null
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

            float ownRate = ownNode.outputAmountPerMinute;
            if (ownRate <= 0.01f || ownMetrics.CurrentState != StationRuntimeState.Working) continue;
            float requiredBusySeconds = Mathf.Max(6f,
                WorkflowAnalysis.GetStationWorkSeconds(gameObject) * 2f);
            if (ownMetrics.CurrentStateSeconds < requiredBusySeconds) continue;

            float minimumRate = float.MaxValue;
            foreach (GameObject station in flow.stations)
            {
                if (station == null || station.GetComponent<HeatLampStation>() != null) continue;
                StationNode node = StationNode.EnsureOn(station);
                if (node != null && node.outputAmountPerMinute > 0.01f)
                    minimumRate = Mathf.Min(minimumRate, node.outputAmountPerMinute);
            }
            if (minimumRate == float.MaxValue || ownRate > minimumRate + 0.01f) continue;

            details = "BOTTLENECK: " + ownNode.DisplayName.ToUpperInvariant() + "\n"
                + ownRate.ToString("0.0") + "/min. Upgrade or add another.";
            return true;
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

    bool HasLowPantryStock()
    {
        var pantry = GetComponent<PantryStation>();
        if (pantry == null || !pantry.HasItemSelected || !pantry.IsAssignedIngredientLow)
            return false;
        // During onboarding, stock is empty until the later buy-ingredients step.
        // Keep the yellow icon for missing selections only.
        return !OnboardingTutorial.BlocksProgression;
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
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = Vector2.one * 100f;
        rect.localScale = Vector3.one * (IconWorldSize / 100f);
        indicatorRect = rect;

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        icon.transform.SetParent(root.transform, false);
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
        panel.transform.SetParent(root.transform, false);
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
        return root;
    }

    void HandleClick()
    {
        if (indicator == null || !indicator.activeSelf || indicatorRect == null
            || !Input.GetMouseButtonDown(0)) return;
        Camera cam = Camera.main;
        if (cam == null || !RectTransformUtility.RectangleContainsScreenPoint(
                indicatorRect, Input.mousePosition, cam)) return;
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

        Transform iconTransform = indicator.transform;
        iconTransform.position = new Vector3(transform.position.x, top + HeightPadding, transform.position.z);
        iconTransform.localScale = Vector3.one * (IconWorldSize / 100f);

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 cameraToIcon = iconTransform.position - cam.transform.position;
        if (cameraToIcon.sqrMagnitude > 0.0001f)
            iconTransform.rotation = Quaternion.LookRotation(cameraToIcon.normalized, cam.transform.up);
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
