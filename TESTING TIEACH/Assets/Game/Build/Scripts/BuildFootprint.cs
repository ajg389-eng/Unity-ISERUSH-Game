using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildFootprint : MonoBehaviour
{
    public int sizeX = 1;
    public int sizeY = 1;
}

/// <summary>
/// Marks a player-placed customer entrance/exit. KitchenPerimeterWalls reads these
/// markers and leaves a correctly sized opening in the generated brick wall.
/// </summary>
public class CustomerWallDoor : MonoBehaviour
{
    public enum WallSide { South, East, North }
    public enum DoorRole { Entrance, Exit }

    public WallSide wallSide;
    public DoorRole role = DoorRole.Entrance;

    static CustomerWallDoor activePopupDoor;
    GameObject rolePopup;
    Image entranceButtonImage;
    Image exitButtonImage;
    TextMeshProUGUI titleText;

    void Awake()
    {
        EnsureWallCutaway();
    }

    void OnEnable()
    {
        EnsureWallCutaway();
    }

    public void EnsureWallCutaway()
    {
        bool ghost = name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null) continue;
            if (colliders[i].GetComponentInParent<Canvas>() != null) continue;
            colliders[i].isTrigger = true;
        }

        if (ghost) return;

        var occ = GetComponent<CameraOcclusionWall>();
        if (occ == null)
            occ = gameObject.AddComponent<CameraOcclusionWall>();
        occ.duckByLowering = true;
        occ.cutawayHeight = 0.8f;
        occ.SetOutward(OutwardDirection());
        occ.SetPlacementLock(false);
        occ.CaptureRestPose();
    }

    public static bool HasActivePopup => activePopupDoor != null
        && activePopupDoor.rolePopup != null
        && activePopupDoor.rolePopup.activeSelf;

    public void ShowRolePopup()
    {
        if (activePopupDoor != null && activePopupDoor != this)
            activePopupDoor.HideRolePopup();

        EnsureRolePopup();
        activePopupDoor = this;
        rolePopup.SetActive(true);
        RefreshRolePopup();
        PositionRolePopup();
    }

    public void HideRolePopup()
    {
        if (rolePopup != null) rolePopup.SetActive(false);
        if (activePopupDoor == this) activePopupDoor = null;
    }

    public static void HideActivePopup()
    {
        if (activePopupDoor != null) activePopupDoor.HideRolePopup();
    }

    void LateUpdate()
    {
        if (rolePopup == null || !rolePopup.activeSelf) return;
        if (ManagementModeController.Instance == null
            || !ManagementModeController.Instance.IsManageMode)
        {
            HideRolePopup();
            return;
        }
        PositionRolePopup();
    }

    void EnsureRolePopup()
    {
        if (rolePopup != null) return;

        rolePopup = new GameObject("Door Role Popup", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
        RectTransform rootRect = rolePopup.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(300f, 132f);
        rootRect.localScale = Vector3.one * 0.006f;

        Canvas canvas = rolePopup.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 250;
        rolePopup.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 16f;
        rolePopup.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.12f, 0.96f);

        titleText = CreatePopupText(rolePopup.transform, "Title", new Vector2(0f, 46f),
            new Vector2(280f, 34f), 22f);
        Button entranceButton = CreateRoleButton(rolePopup.transform, "Entrance", -72f,
            () => SetRole(DoorRole.Entrance), out entranceButtonImage);
        Button exitButton = CreateRoleButton(rolePopup.transform, "Exit", 72f,
            () => SetRole(DoorRole.Exit), out exitButtonImage);
        entranceButton.navigation = new Navigation { mode = Navigation.Mode.None };
        exitButton.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    static TextMeshProUGUI CreatePopupText(Transform parent, string objectName,
        Vector2 position, Vector2 size, float fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    static Button CreateRoleButton(Transform parent, string label, float x,
        UnityEngine.Events.UnityAction onClick, out Image image)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform),
            typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -24f);
        rect.sizeDelta = new Vector2(132f, 54f);
        image = buttonObject.GetComponent<Image>();
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        TextMeshProUGUI text = CreatePopupText(buttonObject.transform, "Text", Vector2.zero,
            new Vector2(124f, 48f), 20f);
        text.text = label;
        return button;
    }

    void SetRole(DoorRole newRole)
    {
        role = newRole;
        RefreshRolePopup();
        Sfx.Play(SfxId.UiClick);
    }

    void RefreshRolePopup()
    {
        if (titleText != null) titleText.text = "Door: " + role;
        Color selected = new Color(0.24f, 0.63f, 0.39f, 1f);
        Color normal = new Color(0.25f, 0.28f, 0.36f, 1f);
        if (entranceButtonImage != null)
            entranceButtonImage.color = role == DoorRole.Entrance ? selected : normal;
        if (exitButtonImage != null)
            exitButtonImage.color = role == DoorRole.Exit ? selected : normal;
    }

    public static bool IsGameplayDoor(CustomerWallDoor door)
    {
        if (door == null || !door.isActiveAndEnabled) return false;
        if (door.name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return door.GetComponentInParent<Canvas>() == null;
    }

    public static CustomerWallDoor FindRandomDoor(DoorRole desiredRole)
    {
        CustomerWallDoor[] doors = FindObjectsByType<CustomerWallDoor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int matchingCount = 0;
        foreach (CustomerWallDoor door in doors)
            if (IsGameplayDoor(door) && door.role == desiredRole)
                matchingCount++;

        if (matchingCount == 0) return null;
        int selectedIndex = Random.Range(0, matchingCount);
        foreach (CustomerWallDoor door in doors)
        {
            if (!IsGameplayDoor(door) || door.role != desiredRole) continue;
            if (selectedIndex-- == 0) return door;
        }
        return null;
    }

    public Vector3 OutwardDirection()
    {
        switch (wallSide)
        {
            case WallSide.East: return Vector3.right;
            case WallSide.North: return Vector3.forward;
            default: return Vector3.back;
        }
    }

    public void AppendPassage(List<Vector3> into, bool entering)
    {
        if (into == null) return;
        Vector3 outside = GetCustomerWaypoint(true, 2.4f);
        Vector3 threshold = GetCustomerWaypoint(true, 0.15f);
        Vector3 inside = GetCustomerWaypoint(false, 2.0f);
        if (entering)
        {
            into.Add(outside);
            into.Add(threshold);
            into.Add(inside);
        }
        else
        {
            into.Add(inside);
            into.Add(threshold);
            into.Add(outside);
        }
    }

    public Vector3 GetCustomerWaypoint(bool outside, float distance = 1.5f)
    {
        Vector3 outward = OutwardDirection();
        float floorY = ResolveFloorY();
        Vector3 point = transform.position;
        point.y = floorY;
        point += outward * (outside ? distance : -distance);
        return point;
    }

    float ResolveFloorY()
    {
        GameObject floor = GameObject.Find("CustomerFloor");
        if (floor != null)
        {
            Renderer renderer = floor.GetComponentInChildren<Renderer>();
            if (renderer != null) return renderer.bounds.max.y;
        }
        Bounds bounds = GetVisualBounds();
        return bounds.min.y;
    }

    Bounds GetVisualBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(transform.position, Vector3.one);
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    void PositionRolePopup()
    {
        if (rolePopup == null || Camera.main == null) return;
        Bounds bounds = GetVisualBounds();
        rolePopup.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.55f, bounds.center.z);
        rolePopup.transform.forward = Camera.main.transform.forward;
    }

    void OnDestroy()
    {
        if (activePopupDoor == this) activePopupDoor = null;
        if (rolePopup != null) Destroy(rolePopup);
        if (!Application.isPlaying) return;
        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        if (walls != null) walls.RequestRefresh();
    }
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
            if (!IsCounterBodyRenderer(renderer)) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (found) return bounds;
        Collider collider = GetComponentInChildren<Collider>();
        return collider != null ? collider.bounds : bounds;
    }

    bool IsCounterBodyRenderer(Renderer renderer)
    {
        if (renderer == null) return false;
        if (renderer.GetComponentInParent<CounterMountedItem>() != null) return false;
        if (renderer.GetComponentInParent<CounterGridVisual>() != null) return false;

        // Scene-authored equipment may be parented under Countertop without a
        // CounterMountedItem marker. It must not affect counter resize bounds.
        if (renderer.GetComponentInParent<Register>() != null) return false;
        if (renderer.GetComponentInParent<HeatLampStation>() != null) return false;
        if (renderer.GetComponentInParent<StationNode>() != null) return false;
        return renderer.transform == transform || renderer.transform.IsChildOf(transform);
    }

    /// <summary>
    /// Extends only the counter's world-Z length and keeps its north edge fixed.
    /// Mounted equipment is shifted to the matching physical slot afterward.
    /// </summary>
    public void ResizeForKitchenDepth(float targetWorldLength, float targetCenterZ,
        int newSlotCount, int slotIndexDelta)
    {
        targetWorldLength = Mathf.Max(0.01f, targetWorldLength);
        newSlotCount = Mathf.Max(1, newSlotCount);
        RebuildOccupants();

        var bodyRoots = new System.Collections.Generic.List<Transform>();
        var seenRoots = new System.Collections.Generic.HashSet<Transform>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Bounds oldBounds = new Bounds(transform.position, Vector3.zero);
        bool foundBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (!IsCounterBodyRenderer(renderer)) continue;
            if (!foundBounds) { oldBounds = renderer.bounds; foundBounds = true; }
            else oldBounds.Encapsulate(renderer.bounds);

            Transform root = renderer.transform;
            while (root.parent != null && root.parent != transform)
                root = root.parent;
            if (root != transform && seenRoots.Add(root))
                bodyRoots.Add(root);
        }

        if (foundBounds && oldBounds.size.z > 0.001f && bodyRoots.Count > 0)
        {
            float ratio = targetWorldLength / oldBounds.size.z;
            foreach (Transform bodyRoot in bodyRoots)
            {
                Vector3 scale = bodyRoot.localScale;
                float xAlignment = Mathf.Abs(Vector3.Dot(
                    bodyRoot.right.normalized, Vector3.forward));
                float zAlignment = Mathf.Abs(Vector3.Dot(
                    bodyRoot.forward.normalized, Vector3.forward));
                if (xAlignment > zAlignment) scale.x *= ratio;
                else scale.z *= ratio;
                bodyRoot.localScale = scale;

                Vector3 position = bodyRoot.position;
                position.z = targetCenterZ + (position.z - oldBounds.center.z) * ratio;
                bodyRoot.position = position;
            }

            Bounds resizedBounds = GetBaseBounds();
            float correction = targetCenterZ - resizedBounds.center.z;
            if (Mathf.Abs(correction) > 0.0001f)
                foreach (Transform bodyRoot in bodyRoots)
                    bodyRoot.position += Vector3.forward * correction;
        }

        slotCount = newSlotCount;
        foreach (CounterMountedItem item in occupants)
        {
            if (item == null) continue;
            int span = item.itemDefinition != null
                ? Mathf.Max(1, item.itemDefinition.counterSlotSpan)
                : 1;
            item.slotIndex = Mathf.Clamp(
                item.slotIndex + slotIndexDelta, 0, Mathf.Max(0, slotCount - span));
        }

        EnsurePlacementCollider();
        BuildGridVisual();
        RealignMountedItems();
        UpdateGridVisibility();
    }

    void RealignMountedItems()
    {
        foreach (CounterMountedItem item in occupants)
        {
            if (item == null || item.itemDefinition == null) continue;
            ItemDefinition definition = item.itemDefinition;
            int span = Mathf.Max(1, definition.counterSlotSpan);
            Vector3 position = GetMountPosition(
                item.gameObject, item.slotIndex, span, definition.counterEmbedDepth);
            position += transform.TransformVector(definition.counterLocalOffset);
            if (definition.useFixedCounterY)
                position.y = definition.fixedCounterY;
            item.transform.position = position;
        }
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
        return GetNearestSlot(worldPoint, span, true);
    }

    public int GetNearestSlot(Vector3 worldPoint, int span, bool requireAvailable)
    {
        CleanupOccupants();
        span = Mathf.Clamp(span, 1, Mathf.Max(1, slotCount));
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i <= Mathf.Max(1, slotCount) - span; i++)
        {
            if (requireAvailable && !IsSlotRangeAvailable(i, span)) continue;
            float distance = (GetSlotCenter(i, span) - worldPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    public Vector3 GetSlotWorldCenter(int slot, int span = 1)
    {
        return GetSlotCenter(slot, span);
    }

    public Vector2 GetSlotWorldSize(int span = 1)
    {
        Bounds bounds = GetBaseBounds();
        int count = Mathf.Max(1, slotCount);
        span = Mathf.Clamp(span, 1, count);
        if (bounds.size.z >= bounds.size.x)
        {
            GetGridAlignedRange(bounds.center.z, false, out float min, out float max);
            return new Vector2(bounds.size.x, (max - min) * span / count);
        }
        GetGridAlignedRange(bounds.center.x, true, out float xMin, out float xMax);
        return new Vector2((xMax - xMin) * span / count, bounds.size.z);
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
