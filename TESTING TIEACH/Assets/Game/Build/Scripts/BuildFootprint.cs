using UnityEngine;

public class BuildFootprint : MonoBehaviour
{
    public int sizeX = 1;
    public int sizeY = 1;
}

/// <summary>One modular counter cell that can hold one counter-mounted station.</summary>
public class CounterSurface : MonoBehaviour
{
    [Min(1)] public int slotCount = 1;
    [Header("Build grid")]
    public bool showGridInBuildMode = true;
    public Color gridColor = new Color(0.15f, 0.95f, 1f, 0.9f);
    [Min(0.005f)] public float gridLineWidth = 0.035f;

    readonly System.Collections.Generic.List<CounterMountedItem> occupants = new System.Collections.Generic.List<CounterMountedItem>();
    GameModeManager modeManager;
    GridManager placementGrid;
    GameObject gridVisual;
    static Material sharedGridMaterial;

    public bool IsAvailable
    {
        get
        {
            CleanupOccupants();
            return occupants.Count < Mathf.Max(1, slotCount);
        }
    }

    void Awake()
    {
        RebuildOccupants();
        placementGrid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
    }

    void Start()
    {
        modeManager = FindObjectOfType<GameModeManager>();
        BuildGridVisual();
        UpdateGridVisibility();
    }

    void LateUpdate()
    {
        UpdateGridVisibility();
    }

    public Bounds GetBaseBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            if (renderer.GetComponentInParent<CounterMountedItem>() != null) continue;
            if (renderer.GetComponentInParent<CounterGridVisual>() != null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (found) return bounds;
        Collider collider = GetComponentInChildren<Collider>();
        return collider != null ? collider.bounds : bounds;
    }

    public void EnsurePlacementCollider()
    {
        BoxCollider placementCollider = GetComponent<BoxCollider>();
        if (placementCollider == null) placementCollider = gameObject.AddComponent<BoxCollider>();

        Bounds bounds = GetBaseBounds();
        placementCollider.center = transform.InverseTransformPoint(bounds.center);
        Vector3 scale = transform.lossyScale;
        placementCollider.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    public int GetNearestAvailableSlot(Vector3 worldPoint, int span)
    {
        CleanupOccupants();
        span = Mathf.Clamp(span, 1, Mathf.Max(1, slotCount));
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i <= Mathf.Max(1, slotCount) - span; i++)
        {
            if (!IsSlotRangeAvailable(i, span)) continue;
            float distance = (GetSlotCenter(i, span) - worldPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    public bool IsSlotAvailable(int slot)
    {
        CleanupOccupants();
        if (slot < 0 || slot >= Mathf.Max(1, slotCount)) return false;
        foreach (CounterMountedItem item in occupants)
        {
            if (item == null) continue;
            int span = item.itemDefinition != null ? Mathf.Max(1, item.itemDefinition.counterSlotSpan) : 1;
            if (slot >= item.slotIndex && slot < item.slotIndex + span) return false;
        }
        return true;
    }

    public bool IsSlotRangeAvailable(int firstSlot, int span)
    {
        span = Mathf.Max(1, span);
        if (firstSlot < 0 || firstSlot + span > Mathf.Max(1, slotCount)) return false;
        for (int slot = firstSlot; slot < firstSlot + span; slot++)
            if (!IsSlotAvailable(slot)) return false;
        return true;
    }

    public Vector3 GetMountPosition(GameObject mountedObject, int slot, int span, float embedDepth)
    {
        Bounds counter = GetBaseBounds();
        Vector3 slotCenter = GetSlotCenter(slot, span);
        if (mountedObject == null)
            return new Vector3(slotCenter.x, counter.max.y - embedDepth, slotCenter.z);

        Renderer[] renderers = mountedObject.GetComponentsInChildren<Renderer>();
        bool foundRenderer = false;
        Bounds item = new Bounds(mountedObject.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (IsAuxiliaryPlacementRenderer(renderer, mountedObject.transform)) continue;
            if (!foundRenderer) { item = renderer.bounds; foundRenderer = true; }
            else item.Encapsulate(renderer.bounds);
        }
        if (!foundRenderer)
            return new Vector3(slotCenter.x, counter.max.y - embedDepth, slotCenter.z);

        Vector3 pivotToCenter = mountedObject.transform.position - item.center;
        return new Vector3(
            slotCenter.x + pivotToCenter.x,
            counter.max.y + (mountedObject.transform.position.y - item.min.y) - embedDepth,
            slotCenter.z + pivotToCenter.z);
    }

    public static bool IsAuxiliaryPlacementRenderer(Renderer renderer, Transform itemRoot)
    {
        if (renderer == null) return true;
        if (renderer.GetComponentInParent<CounterGridVisual>() != null) return true;

        StationInteractionTiles interactionTiles = itemRoot != null
            ? itemRoot.GetComponent<StationInteractionTiles>()
            : null;
        if (interactionTiles != null
            && interactionTiles.buildModeHighlight != null
            && (renderer.transform == interactionTiles.buildModeHighlight.transform
                || renderer.transform.IsChildOf(interactionTiles.buildModeHighlight.transform)))
            return true;

        Transform current = renderer.transform;
        while (current != null && current != itemRoot)
        {
            if (current.name == "InteractionHighlight"
                || current.name.StartsWith("QueueVisual", System.StringComparison.Ordinal))
                return true;
            current = current.parent;
        }
        return false;
    }

    public bool Attach(CounterMountedItem item, int slot)
    {
        if (item == null) return false;
        int span = item.itemDefinition != null ? Mathf.Max(1, item.itemDefinition.counterSlotSpan) : 1;
        if (!IsSlotRangeAvailable(slot, span) && !occupants.Contains(item)) return false;
        if (!occupants.Contains(item)) occupants.Add(item);
        item.surface = this;
        item.slotIndex = slot;
        item.transform.SetParent(transform, true);
        return true;
    }

    public void Release(CounterMountedItem item)
    {
        occupants.Remove(item);
        if (item != null && item.surface == this) item.surface = null;
    }

    Vector3 GetSlotCenter(int slot, int span = 1)
    {
        Bounds bounds = GetBaseBounds();
        int count = Mathf.Max(1, slotCount);
        span = Mathf.Clamp(span, 1, count);
        float t = (Mathf.Clamp(slot, 0, count - span) + span * 0.5f) / count;
        if (bounds.size.z >= bounds.size.x)
        {
            GetGridAlignedRange(bounds.center.z, false, out float min, out float max);
            return new Vector3(bounds.center.x, bounds.max.y, Mathf.Lerp(min, max, t));
        }
        GetGridAlignedRange(bounds.center.x, true, out float xMin, out float xMax);
        return new Vector3(Mathf.Lerp(xMin, xMax, t), bounds.max.y, bounds.center.z);
    }

    void RebuildOccupants()
    {
        occupants.Clear();
        occupants.AddRange(GetComponentsInChildren<CounterMountedItem>(true));
    }

    void CleanupOccupants()
    {
        occupants.RemoveAll(item => item == null || item.surface != this);
    }

    void BuildGridVisual()
    {
        if (gridVisual != null) Destroy(gridVisual);

        Bounds bounds = GetBaseBounds();
        float y = bounds.max.y + 0.025f;
        int count = Mathf.Max(1, slotCount);

        gridVisual = new GameObject("Counter Placement Grid");
        gridVisual.transform.SetParent(transform, true);
        gridVisual.AddComponent<CounterGridVisual>();

        if (bounds.size.z >= bounds.size.x)
        {
            GetGridAlignedRange(bounds.center.z, false, out float min, out float max);
            CreateGridLine("Left Edge", new Vector3(bounds.min.x, y, min), new Vector3(bounds.min.x, y, max));
            CreateGridLine("Right Edge", new Vector3(bounds.max.x, y, min), new Vector3(bounds.max.x, y, max));
            for (int i = 0; i <= count; i++)
            {
                float z = Mathf.Lerp(min, max, i / (float)count);
                CreateGridLine($"Slot {i}", new Vector3(bounds.min.x, y, z), new Vector3(bounds.max.x, y, z));
            }
        }
        else
        {
            GetGridAlignedRange(bounds.center.x, true, out float min, out float max);
            CreateGridLine("Bottom Edge", new Vector3(min, y, bounds.min.z), new Vector3(max, y, bounds.min.z));
            CreateGridLine("Top Edge", new Vector3(min, y, bounds.max.z), new Vector3(max, y, bounds.max.z));
            for (int i = 0; i <= count; i++)
            {
                float x = Mathf.Lerp(min, max, i / (float)count);
                CreateGridLine($"Slot {i}", new Vector3(x, y, bounds.min.z), new Vector3(x, y, bounds.max.z));
            }
        }
    }

    void GetGridAlignedRange(float counterCenter, bool xAxis, out float min, out float max)
    {
        if (placementGrid == null)
            placementGrid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();

        int count = Mathf.Max(1, slotCount);
        float cell = placementGrid != null ? Mathf.Max(0.01f, placementGrid.cellSize) : 1f;
        float origin = placementGrid != null
            ? (xAxis ? placementGrid.Origin.x : placementGrid.Origin.z)
            : 0f;
        float desiredMin = counterCenter - count * cell * 0.5f;
        min = origin + Mathf.Round((desiredMin - origin) / cell) * cell;
        max = min + count * cell;
    }

    void CreateGridLine(string lineName, Vector3 start, Vector3 end)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(gridVisual.transform, true);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = gridLineWidth;
        line.endWidth = gridLineWidth;
        line.startColor = gridColor;
        line.endColor = gridColor;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetGridMaterial();
    }

    static Material GetGridMaterial()
    {
        if (sharedGridMaterial != null) return sharedGridMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        sharedGridMaterial = new Material(shader)
        {
            name = "Counter Grid Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedGridMaterial;
    }

    void UpdateGridVisibility()
    {
        if (gridVisual == null) return;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        bool visible = showGridInBuildMode
            && modeManager != null
            && modeManager.CurrentMode == GameModeManager.Mode.Build;
        if (gridVisual.activeSelf != visible) gridVisual.SetActive(visible);
    }
}

/// <summary>Excludes the counter's build overlay from placement bounds.</summary>
public class CounterGridVisual : MonoBehaviour { }

/// <summary>Marks a station as occupying a counter-top slot instead of a floor-grid slot.</summary>
public class CounterMountedItem : MonoBehaviour
{
    public CounterSurface surface;
    public ItemDefinition itemDefinition;
    public int slotIndex = -1;

    void OnDestroy()
    {
        if (surface != null) surface.Release(this);
    }
}
