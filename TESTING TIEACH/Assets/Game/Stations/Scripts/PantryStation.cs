using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Placeable station. Holds which ingredients can be grabbed here.
/// Actual counts come from KitchenInventory (ordered via Management).
/// </summary>
public class PantryStation : MonoBehaviour
{
    [Header("Stored ingredient")]
    [Tooltip("The only ingredient this pantry can dispense. Choose it in Management mode.")]
    public ItemDefinition selectedItem;
    [FormerlySerializedAs("interactionTimeSeconds")]
    [Tooltip("Total time for one pantry operation.")]
    [Min(0f)] public float processTimeSeconds = 0.5f;
    [Tooltip("Show a caution sign when the assigned ingredient reaches this stock level or lower.")]
    [Min(0)] public int lowStockWarningThreshold = 10;
    public Vector3 interactionOffset = Vector3.zero;

    Transform itemDisplayRoot;
    readonly List<Transform> itemSpawnMarkers = new List<Transform>();
    ItemDefinition displayedItem;
    int displayedCount = -1;

    void OnEnable()
    {
        StationConfigurationCaution.Ensure(gameObject);
        StationItemVisualUtility.FindMarkers(transform, itemSpawnMarkers);
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
        var inv = KitchenInventory.Instance;
        if (inv == null) return true; // legacy infinite if no inventory system
        return inv.Has(item, amount);
    }

    public bool TakeItem(ItemDefinition item)
    {
        if (item == null || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(item, 1);
    }

    public bool TakeItems(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0 || !CanDispense(item)) return false;
        var inv = KitchenInventory.Instance;
        if (inv == null) return true;
        return inv.TryConsume(item, amount);
    }

    public bool HasItemSelected => selectedItem != null;

    public bool IsAssignedIngredientLow
    {
        get
        {
            if (selectedItem == null || KitchenInventory.Instance == null) return false;
            return KitchenInventory.Instance.GetCount(selectedItem) <= Mathf.Max(0, lowStockWarningThreshold);
        }
    }

    public void SetStoredItem(ItemDefinition item)
    {
        selectedItem = item;
        GetComponent<StationNode>()?.EnsureIoDefaults(force: true);
        RefreshItemDisplay(force: true);
    }

    public bool CanDispense(ItemDefinition item) => item != null && item == selectedItem;

    void RefreshItemDisplay(bool force)
    {
        if (!isActiveAndEnabled && !force) return;
        if (itemSpawnMarkers.Count == 0)
            StationItemVisualUtility.FindMarkers(transform, itemSpawnMarkers);

        if (itemDisplayRoot == null)
            itemDisplayRoot = StationItemVisualUtility.GetOrCreateDisplayRoot(transform, "PantryItemDisplay");
        StationItemVisualUtility.ClearChildren(itemDisplayRoot);

        int stock = selectedItem != null && KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetCount(selectedItem)
            : 0;
        displayedItem = selectedItem;
        displayedCount = stock;
        if (selectedItem == null || selectedItem.prefab == null) return;

        int visibleCount = Mathf.Min(stock, itemSpawnMarkers.Count);
        for (int i = 0; i < visibleCount; i++)
            StationItemVisualUtility.SpawnAtMarker(selectedItem.prefab, itemSpawnMarkers[i], itemDisplayRoot,
                "PantryItem_" + i);
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
        bool show = !HasConfiguration() || HasLowPantryStock();
        if (show && indicator == null)
            indicator = CreateIndicator();
        if (indicator == null) return;

        indicator.SetActive(show);
        if (show) UpdateTransform();
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
        return pantry != null && pantry.HasItemSelected && pantry.IsAssignedIngredientLow;
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
        return root;
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
