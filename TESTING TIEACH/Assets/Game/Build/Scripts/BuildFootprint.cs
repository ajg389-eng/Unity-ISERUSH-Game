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
    [Tooltip("Prevents a required entrance or exit from being deleted or assigned the other role.")]
    public bool permanentFixture;

    const float SwingOpenAngle = 82f;
    const float SwingOpenDegreesPerSecond = 260f;
    const float SwingCloseDegreesPerSecond = SwingOpenDegreesPerSecond / 0.6f;
    const float SwingTriggerRadius = 4.2f;
    const float SwingCloseRadius = 2.4f;

    struct SwingLeaf
    {
        public Transform transform;
        public Quaternion closedLocal;
        public float outwardSign;
    }

    readonly List<SwingLeaf> swingLeaves = new List<SwingLeaf>();
    bool swingReady;
    float passageClearance = 2f;
    float swingAngle;
    int swingDirection; // -1 open in (arrivals), +1 open out (departures), 0 closed

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

        DisableImportedDoorClickScripts();
        CacheSwingLeaves();

        // The brick wall already cuts away. Lowering the door with that cutaway
        // sinks the mesh into the floor, so the entrance looks missing.
        CameraOcclusionWall occ = GetComponent<CameraOcclusionWall>();
        if (occ != null)
        {
            occ.SnapUp();
            Destroy(occ);
        }
    }

    public static bool HasActivePopup => activePopupDoor != null
        && activePopupDoor.rolePopup != null
        && activePopupDoor.rolePopup.activeSelf;

    public void ShowRolePopup()
    {
        if (permanentFixture) return;
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
        UpdateDoorSwing();

        if (rolePopup == null || !rolePopup.activeSelf) return;
        if (ManagementModeController.Instance == null
            || !ManagementModeController.Instance.IsManageMode)
        {
            HideRolePopup();
            return;
        }
        PositionRolePopup();
    }

    bool IsGhostDoor => name.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0;

    void DisableImportedDoorClickScripts()
    {
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;
            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("openclose", System.StringComparison.OrdinalIgnoreCase) >= 0)
                behaviour.enabled = false;
        }
    }

    void CacheSwingLeaves()
    {
        swingLeaves.Clear();
        swingReady = false;
        passageClearance = 2f;
        swingAngle = 0f;
        swingDirection = 0;

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        Vector3 outward = OutwardDirection();
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null) continue;
            animator.enabled = false;

            Transform leaf = animator.transform;
            Vector3 localCenter = Vector3.zero;
            BoxCollider box = leaf.GetComponent<BoxCollider>();
            if (box != null)
                localCenter = box.center;
            else
            {
                Renderer renderer = leaf.GetComponent<Renderer>();
                if (renderer != null)
                    localCenter = leaf.InverseTransformPoint(renderer.bounds.center);
            }
            localCenter.y = 0f;
            if (localCenter.sqrMagnitude < 0.01f)
                localCenter = Vector3.left;

            // The customer must clear the full hinge-to-edge reach before turning.
            // Measure the closed leaf, so opening/closing never moves route targets.
            Renderer leafRenderer = leaf.GetComponentInChildren<Renderer>();
            if (leafRenderer != null)
            {
                Bounds leafBounds = leafRenderer.bounds;
                Vector3 hinge = leaf.position;
                float reachX = Mathf.Max(Mathf.Abs(leafBounds.min.x - hinge.x), Mathf.Abs(leafBounds.max.x - hinge.x));
                float reachZ = Mathf.Max(Mathf.Abs(leafBounds.min.z - hinge.z), Mathf.Abs(leafBounds.max.z - hinge.z));
                passageClearance = Mathf.Max(passageClearance, Mathf.Sqrt(reachX * reachX + reachZ * reachZ) + 0.75f);
            }

            Vector3 worldSwing = leaf.TransformDirection(Vector3.Cross(Vector3.up, localCenter));
            float sign = Mathf.Sign(Vector3.Dot(worldSwing, outward));
            if (Mathf.Abs(sign) < 0.01f) sign = 1f;

            swingLeaves.Add(new SwingLeaf
            {
                transform = leaf,
                closedLocal = leaf.localRotation,
                outwardSign = sign
            });
        }

        swingReady = swingLeaves.Count > 0;
        ApplySwingAngle();
    }

    void UpdateDoorSwing()
    {
        if (IsGhostDoor) return;
        if (!swingReady) CacheSwingLeaves();
        if (!swingReady) return;

        int desired = ResolveSwingDirection();
        float target = desired * SwingOpenAngle;
        if (Mathf.Abs(swingAngle - target) < 0.05f)
        {
            if (Mathf.Abs(swingAngle - target) > 0.0001f)
            {
                swingAngle = target;
                ApplySwingAngle();
            }
            swingDirection = desired;
            return;
        }

        swingAngle = Mathf.MoveTowards(swingAngle, target,
            (desired == 0 ? SwingCloseDegreesPerSecond : SwingOpenDegreesPerSecond) * Time.deltaTime);
        swingDirection = desired;
        ApplySwingAngle();
    }

    int ResolveSwingDirection()
    {
        Vector3 doorPos = transform.position;
        CustomerAI[] customers = FindObjectsByType<CustomerAI>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        float nearestEnter = float.MaxValue;
        float nearestLeave = float.MaxValue;
        bool customerInSweep = false;
        float triggerRadius = Mathf.Max(SwingTriggerRadius, passageClearance + 1f);
        for (int i = 0; i < customers.Length; i++)
        {
            CustomerAI customer = customers[i];
            if (customer == null) continue;
            float dist = HorizontalDistance(doorPos, customer.transform.position);
            if (dist < passageClearance) customerInSweep = true;
            if (dist > triggerRadius) continue;
            if (customer.IsEntering)
                nearestEnter = Mathf.Min(nearestEnter, dist);
            else if (customer.IsLeaving)
                nearestLeave = Mathf.Min(nearestLeave, dist);
        }

        List<IngredientCourier> couriers = IngredientCourier.ActiveCouriers;
        for (int i = 0; i < couriers.Count; i++)
        {
            IngredientCourier courier = couriers[i];
            if (courier == null) continue;
            float dist = HorizontalDistance(doorPos, courier.transform.position);
            if (dist < passageClearance) customerInSweep = true;
            if (dist > triggerRadius) continue;
            if (courier.IsWalkingIn)
                nearestEnter = Mathf.Min(nearestEnter, dist);
            else if (courier.IsWalkingOut)
                nearestLeave = Mathf.Min(nearestLeave, dist);
        }

        // Preserve the opening direction until everyone has cleared the panel,
        // including customers who have just switched from entry to queue movement.
        if (customerInSweep && swingDirection != 0) return swingDirection;
        bool enterNear = nearestEnter <= triggerRadius;
        bool leaveNear = nearestLeave <= triggerRadius;
        if (enterNear && (!leaveNear || nearestEnter <= nearestLeave))
            return -1;
        if (leaveNear)
            return 1;

        if (Mathf.Abs(swingAngle) > 1f
            && (nearestEnter <= SwingCloseRadius || nearestLeave <= SwingCloseRadius))
            return swingDirection;
        return 0;
    }

    void ApplySwingAngle()
    {
        for (int i = 0; i < swingLeaves.Count; i++)
        {
            SwingLeaf leaf = swingLeaves[i];
            if (leaf.transform == null) continue;
            leaf.transform.localRotation = leaf.closedLocal
                * Quaternion.AngleAxis(swingAngle * leaf.outwardSign, Vector3.up);
        }
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
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

    /// <summary>Door used to arrive from the bus. Prefers the East wall and never uses Exit doors on other walls.</summary>
    public static CustomerWallDoor FindEntryDoor()
    {
        CustomerWallDoor eastEntrance = null;
        CustomerWallDoor eastAny = null;
        CustomerWallDoor anyEntrance = null;
        CustomerWallDoor[] doors = FindObjectsByType<CustomerWallDoor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (CustomerWallDoor door in doors)
        {
            if (!IsGameplayDoor(door)) continue;
            if (door.wallSide == WallSide.East)
            {
                if (door.role == DoorRole.Entrance && eastEntrance == null)
                    eastEntrance = door;
                if (eastAny == null)
                    eastAny = door;
            }
            if (door.role == DoorRole.Entrance && anyEntrance == null)
                anyEntrance = door;
        }
        if (eastEntrance != null) return eastEntrance;
        if (eastAny != null) return eastAny;
        return anyEntrance;
    }

    /// <summary>Customers leave through the same door they entered through.</summary>
    public static CustomerWallDoor FindExitDoor()
    {
        return FindEntryDoor();
    }

    public static bool TryGetBusAlightPoint(out Vector3 point)
    {
        point = Vector3.zero;
        Renderer bus = FindBusRenderer();
        if (bus == null) return false;
        Bounds bounds = bus.bounds;
        float floorY = bounds.min.y;
        GameObject floor = GameObject.Find("CustomerFloor");
        Renderer floorRenderer = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        if (floorRenderer != null) floorY = floorRenderer.bounds.max.y;
        // Bus sits east of the restaurant. Alight on the store-facing (west) side.
        point = new Vector3(bounds.min.x - 0.45f, floorY, bounds.center.z);
        return true;
    }

    static Renderer FindBusRenderer()
    {
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Renderer best = null;
        float bestSize = 0f;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            bool isBus = false;
            for (Transform t = renderer.transform; t != null; t = t.parent)
            {
                if (t.name.IndexOf("Bus", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isBus = true;
                    break;
                }
            }
            if (!isBus) continue;
            if (renderer.GetComponentInParent<Canvas>() != null) continue;
            float size = renderer.bounds.size.sqrMagnitude;
            if (size > bestSize)
            {
                best = renderer;
                bestSize = size;
            }
        }
        return best;
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
        Vector3 outside = GetCustomerWaypoint(true, Mathf.Max(2.4f, passageClearance));
        Vector3 threshold = GetCustomerWaypoint(true, 0.15f);
        float insideDistance = passageClearance;
        Vector3 inside = GetCustomerWaypoint(false, insideDistance);

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

    /// <summary>
    /// After stepping through an East door, walk along the inner east wall to the
    /// register row before crossing the lobby (door → south → west to the counter).
    /// </summary>
    public void AppendEastEntryElbow(List<Vector3> into, Vector3 lobbyTarget)
    {
        if (into == null || into.Count == 0 || wallSide != WallSide.East) return;
        Vector3 last = into[into.Count - 1];
        float destZ = lobbyTarget.z;
        if (Mathf.Abs(destZ - last.z) < 0.35f) return;
        into.Add(new Vector3(last.x, last.y, destZ));
    }

    /// <summary>
    /// True once the leaf has swung far enough for this step to cross the doorway.
    /// Steps that stay on one side of the door are always clear.
    /// </summary>
    public bool IsClearToCross(Vector3 from, Vector3 to)
    {
        if (!swingReady) return true;
        Vector3 center = GetPassageCenter();
        Vector3 outward = OutwardDirection();
        float fromSide = Vector3.Dot(from - center, outward);
        float toSide = Vector3.Dot(to - center, outward);
        if (fromSide * toSide > 0.04f) return true;
        if (Mathf.Abs(fromSide) > passageClearance + 0.35f) return true;

        // Either swing clears the walkway. Holding for the opposite direction
        // left the courier standing in the opening after a delivery.
        return Mathf.Abs(swingAngle) >= SwingOpenAngle * 0.72f;
    }

    public Vector3 GetCustomerWaypoint(bool outside, float distance = 1.5f)
    {
        Vector3 outward = OutwardDirection();
        Vector3 point = GetPassageCenter();
        point += outward * (outside ? distance : -distance);
        return point;
    }

    /// <summary>World point in the middle of the doorway opening, on the floor.</summary>
    public Vector3 GetPassageCenter()
    {
        Bounds bounds = GetVisualBounds();
        Vector3 center = bounds.center;
        center.y = ResolveFloorY();
        return center;
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
    readonly System.Collections.Generic.List<GameObject> counterSegments = new System.Collections.Generic.List<GameObject>();
    readonly HashSet<int> groundStationSlots = new HashSet<int>();
    readonly Dictionary<int, GroundPickupStationPlacement> groundStationItems = new Dictionary<int, GroundPickupStationPlacement>();
    Transform counterTemplate;
    Vector3 counterSegmentScale;
    float originalSlotLength;
    Bounds segmentedCounterBounds;
    bool counterSegmentMode;
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
        RebuildOccupants();
        RealignMountedItems();
        modeManager = FindObjectOfType<GameModeManager>();
        BuildGridVisual();
        UpdateGridVisibility();
    }

    public void InitializeGroundStationPlacement()
    {
        if (!counterSegmentMode) BuildCounterSegments();
        else if (counterSegments.Count != slotCount) RebuildCounterSegments();
        if (counterSegmentMode) EnsurePlacementCollider();
    }

    void LateUpdate()
    {
        UpdateGridVisibility();
    }

    public Bounds GetBaseBounds()
    {
        if (counterSegmentMode) return segmentedCounterBounds;

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

        if (counterSegmentMode)
        {
            Bounds oldSegmentBounds = segmentedCounterBounds;
            segmentedCounterBounds = new Bounds(
                new Vector3(oldSegmentBounds.center.x, oldSegmentBounds.center.y, targetCenterZ),
                new Vector3(oldSegmentBounds.size.x, oldSegmentBounds.size.y, targetWorldLength));
            slotCount = newSlotCount;
            RebuildCounterSegments();
            foreach (CounterMountedItem item in occupants)
            {
                if (item == null) continue;
                int span = item.itemDefinition != null
                    ? Mathf.Max(1, item.itemDefinition.counterSlotSpan)
                    : 1;
                item.slotIndex = Mathf.Clamp(
                    item.slotIndex + slotIndexDelta, 0, Mathf.Max(0, slotCount - span));
            }
            BuildGridVisual();
            RealignMountedItems();
            UpdateGridVisibility();
            return;
        }

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
        // Keep a trigger over the full counter footprint so pointer raycasts can
        // target its tiles, including the openings where pickup stations sit.
        placementCollider.isTrigger = counterSegmentMode;
        placementCollider.enabled = true;
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
        if (groundStationSlots.Contains(slot)) return false;
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
        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return true;
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
        // Saved counter stations are instantiated at their previously saved world
        // position before Attach is called. Recompute the mount after attachment so
        // they honor the current definition and sit on top of the counter as well.
        RealignMountedItems();
        return true;
    }

    public bool TryGetGroundStationSlot(Vector3 floorPosition, out int slot)
    {
        slot = -1;
        if (!counterSegmentMode && !BuildCounterSegments()) return false;
        if (counterSegments.Count == 0) return false;

        float cell = segmentedCounterBounds.size.z / Mathf.Max(1, slotCount);
        if (Mathf.Abs(floorPosition.x - segmentedCounterBounds.center.x) > segmentedCounterBounds.size.x * 0.5f)
            return false;

        float minZ = segmentedCounterBounds.center.z - segmentedCounterBounds.size.z * 0.5f;
        float maxZ = minZ + cell * slotCount;
        if (floorPosition.z < minZ || floorPosition.z > maxZ) return false;

        // Snap to the nearest free tile so a click near an occupied tile can still
        // use the next open section of the counter.
        float bestDistance = float.PositiveInfinity;
        for (int candidate = 0; candidate < slotCount; candidate++)
        {
            if (!IsSlotAvailable(candidate)) continue;
            Vector3 center = GetSlotCenter(candidate);
            float distance = (new Vector2(center.x, center.z)
                - new Vector2(floorPosition.x, floorPosition.z)).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            slot = candidate;
        }
        return slot >= 0;
    }

    public bool TryGetGroundStationSlotAtPosition(Vector3 worldPosition, out int slot)
    {
        slot = -1;
        if (!counterSegmentMode && !BuildCounterSegments()) return false;
        if (counterSegments.Count == 0) return false;
        if (Mathf.Abs(worldPosition.x - segmentedCounterBounds.center.x) > segmentedCounterBounds.size.x * 0.5f)
            return false;

        float cell = segmentedCounterBounds.size.z / Mathf.Max(1, slotCount);
        float minZ = segmentedCounterBounds.center.z - segmentedCounterBounds.size.z * 0.5f;
        float maxZ = minZ + cell * slotCount;
        if (worldPosition.z < minZ || worldPosition.z > maxZ) return false;
        slot = Mathf.Clamp(Mathf.FloorToInt((worldPosition.z - minZ) / cell), 0, slotCount - 1);
        return true;
    }

    public GroundPickupStationPlacement GetGroundStationAtSlot(int slot)
    {
        if (groundStationItems.TryGetValue(slot, out GroundPickupStationPlacement placement)
            && placement != null)
            return placement;
        groundStationItems.Remove(slot);
        return null;
    }

    public void RegisterGroundStation(GroundPickupStationPlacement placement, int slot)
    {
        if (placement == null || slot < 0 || slot >= slotCount) return;
        groundStationItems[slot] = placement;
    }

    public void UnregisterGroundStation(GroundPickupStationPlacement placement, int slot)
    {
        if (slot < 0) return;
        if (groundStationItems.TryGetValue(slot, out GroundPickupStationPlacement current)
            && current == placement)
            groundStationItems.Remove(slot);
    }

    public bool OpenGroundStationSlot(int slot)
    {
        if (!counterSegmentMode && !BuildCounterSegments()) return false;
        if (slot < 0 || slot >= slotCount || slot >= counterSegments.Count) return false;
        groundStationSlots.Add(slot);
        GameObject segment = counterSegments[slot];
        if (segment != null) segment.SetActive(false);
        EnsurePlacementCollider();
        return true;
    }

    public void CloseGroundStationSlot(int slot)
    {
        if (slot < 0 || slot >= counterSegments.Count) return;
        groundStationSlots.Remove(slot);
        if (counterSegments[slot] != null) counterSegments[slot].SetActive(true);
    }

    bool BuildCounterSegments()
    {
        Bounds bounds = GetBaseBounds();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.GetComponentInParent<CounterMountedItem>() != null) continue;
            Transform root = renderer.transform;
            while (root.parent != null && root.parent != transform) root = root.parent;
            if (root.parent == transform && root.name.IndexOf("Counter", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                counterTemplate = root;
                break;
            }
        }
        if (counterTemplate == null || bounds.size.z <= 0.01f) return false;

        originalSlotLength = bounds.size.z / Mathf.Max(1, slotCount);
        counterSegmentScale = counterTemplate.localScale;
        counterSegmentScale.z /= Mathf.Max(1, slotCount);
        GetGridAlignedRange(bounds.center.z, false, out float minZ, out float maxZ);
        segmentedCounterBounds = new Bounds(
            new Vector3(bounds.center.x, bounds.center.y, (minZ + maxZ) * 0.5f),
            new Vector3(bounds.size.x, bounds.size.y, maxZ - minZ));
        counterSegmentMode = true;
        RebuildCounterSegments();
        EnsurePlacementCollider();
        return true;
    }

    void RebuildCounterSegments()
    {
        for (int i = 0; i < counterSegments.Count; i++)
        {
            if (counterSegments[i] == null) continue;
            counterSegments[i].SetActive(false);
            Destroy(counterSegments[i]);
        }
        counterSegments.Clear();

        if (counterTemplate == null) return;
        counterTemplate.gameObject.SetActive(false);
        float cellLength = segmentedCounterBounds.size.z / Mathf.Max(1, slotCount);
        Vector3 segmentScale = counterSegmentScale;
        segmentScale.z *= cellLength / Mathf.Max(0.01f, originalSlotLength);

        for (int slot = 0; slot < slotCount; slot++)
        {
            GameObject segment = Instantiate(counterTemplate.gameObject, counterTemplate.parent, false);
            segment.name = "CounterSegment_" + slot;
            segment.SetActive(true);
            segment.transform.localScale = segmentScale;
            Renderer[] segmentRenderers = segment.GetComponentsInChildren<Renderer>();
            if (segmentRenderers.Length == 0)
            {
                Destroy(segment);
                counterSegments.Add(null);
                continue;
            }
            Bounds segmentBounds = segmentRenderers[0].bounds;
            for (int r = 1; r < segmentRenderers.Length; r++)
                segmentBounds.Encapsulate(segmentRenderers[r].bounds);

            float firstCenterZ = segmentedCounterBounds.center.z - segmentedCounterBounds.size.z * 0.5f + cellLength * 0.5f;
            Vector3 target = new Vector3(segmentedCounterBounds.center.x, segmentedCounterBounds.center.y,
                firstCenterZ + slot * cellLength);
            segment.transform.position += target - segmentBounds.center;
            counterSegments.Add(segment);
        }

        for (int i = 0; i < counterSegments.Count; i++)
            if (groundStationSlots.Contains(i) && counterSegments[i] != null)
                counterSegments[i].SetActive(false);
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

    /// <summary>
    /// Empty counter tile (no register / pickup) plus a lobby stand two tiles east of it.
    /// </summary>
    public static bool TryGetEmptyDeliverySpot(out Vector3 dropTop, out Vector3 lobbyStand)
    {
        dropTop = Vector3.zero;
        lobbyStand = Vector3.zero;
        float cell = GridManager.Instance != null ? Mathf.Max(0.01f, GridManager.Instance.cellSize) : 1f;
        Register[] registers = Object.FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        HeatLampStation[] lamps = Object.FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        CounterSurface[] surfaces = Object.FindObjectsByType<CounterSurface>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        Vector3 hint = Vector3.zero;
        if (registers.Length > 0 && registers[0] != null)
            hint = registers[0].transform.position;

        bool found = false;
        float bestScore = float.MaxValue;
        Bounds bestSlab = default;
        Vector3 bestDrop = Vector3.zero;

        for (int s = 0; s < surfaces.Length; s++)
        {
            CounterSurface surface = surfaces[s];
            if (surface == null) continue;
            Bounds slab = surface.GetBaseBounds();
            int slots = Mathf.Max(1, surface.slotCount);
            for (int slot = 0; slot < slots; slot++)
            {
                if (!surface.IsSlotAvailable(slot)) continue;
                Vector3 center = surface.GetSlotWorldCenter(slot);
                if (TileBlockedByStation(center, registers, lamps)) continue;
                float score = (center - hint).sqrMagnitude;
                if (found && score >= bestScore) continue;
                found = true;
                bestScore = score;
                bestDrop = center;
                bestSlab = slab;
            }
        }

        if (!found) return false;

        dropTop = new Vector3(bestDrop.x, bestSlab.max.y, bestDrop.z);
        GameObject floor = GameObject.Find("CustomerFloor");
        Renderer floorRenderer = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        float standY = dropTop.y;
        if (floorRenderer != null)
            standY = floorRenderer.bounds.max.y;
        lobbyStand = new Vector3(bestSlab.max.x + 2f * cell, standY, bestDrop.z);
        return true;
    }

    static bool TileBlockedByStation(Vector3 slotCenter, Register[] registers, HeatLampStation[] lamps)
    {
        for (int i = 0; i < registers.Length; i++)
        {
            if (registers[i] == null) continue;
            Vector3 p = registers[i].transform.position;
            if (Mathf.Abs(p.x - slotCenter.x) < 0.85f && Mathf.Abs(p.z - slotCenter.z) < 0.85f)
                return true;
        }
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null) continue;
            Vector3 p = lamps[i].transform.position;
            if (Mathf.Abs(p.x - slotCenter.x) < 0.85f && Mathf.Abs(p.z - slotCenter.z) < 0.85f)
                return true;
        }
        return false;
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

/// <summary>Tracks the countertop tile removed to make room for a floor pickup station.</summary>
public class GroundPickupStationPlacement : MonoBehaviour
{
    public CounterSurface surface;
    public int slotIndex = -1;
    public ItemDefinition itemDefinition;

    void OnEnable()
    {
        if (surface != null && slotIndex >= 0)
            surface.RegisterGroundStation(this, slotIndex);
    }

    public ItemDefinition GetItemDefinition()
    {
        if (itemDefinition != null) return itemDefinition;
        PlacedBuildItem placed = GetComponent<PlacedBuildItem>();
        return placed != null ? placed.itemDefinition : null;
    }

    public void BindSlot(CounterSurface newSurface, int newSlot, ItemDefinition definition)
    {
        if (surface != newSurface || slotIndex != newSlot) ReleaseSlot();
        surface = newSurface;
        slotIndex = newSlot;
        if (definition != null) itemDefinition = definition;
        if (surface != null) surface.RegisterGroundStation(this, slotIndex);
    }

    public void ReleaseSlot()
    {
        if (surface != null && slotIndex >= 0)
        {
            surface.UnregisterGroundStation(this, slotIndex);
            surface.CloseGroundStationSlot(slotIndex);
        }
        surface = null;
        slotIndex = -1;
    }

    void OnDestroy()
    {
        if (Application.isPlaying) ReleaseSlot();
    }
}
