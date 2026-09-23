using UnityEngine;
using TMPro;

public class BuildPlacer : MonoBehaviour
{
    public GameModeManager modeManager;
    public GridManager grid;
    public InventoryManager inventory;

    [Header("Raycast")]
    public LayerMask floorLayer;
    [Tooltip("Layers to raycast for drag-to-move. Leave as Nothing to use Everything. Or set to a 'Placeable' layer and put station prefabs on that layer so the floor doesn't block clicks.")]
    public LayerMask placeableLayer;

    [Header("UI")]
    public TextMeshProUGUI placementHintText; // "LMB place | ESC cancel"

    private ItemDefinition placingItem;
    private GameObject ghost;
    /// <summary>0, 1, 2, 3 = 0�, 90�, 180�, 270� around Y. Used while placing.</summary>
    private int placementRotation;

    private GameObject draggingObject;
    private BuildFootprint dragFootprint;
    private StationSelectionHighlight dragSelectionHighlight;
    private int dragOrigX, dragOrigY;
    /// <summary>0, 1, 2, 3 = 0�, 90�, 180�, 270� while dragging.</summary>
    private int dragRotation;
    private int dragOrigRotation;
    private CounterMountedItem draggingMountedItem;
    private CounterSurface dragOriginalSurface;
    private int dragOriginalSlot = -1;
    private Vector3 dragOriginalPosition;
    private Quaternion dragOriginalWorldRotation;
    private CustomerWallDoor draggingWallDoor;
    private CustomerWallDoor.WallSide dragOriginalWallSide;
    bool hasDoorPreviewWallLocation;
    Vector3 lastDoorPreviewWallPosition;
    CustomerWallDoor.WallSide lastDoorPreviewWallSide;
    const float EditDoubleClickSeconds = 0.4f;
    float lastEditClickTime = -999f;
    int lastEditClickId;

    public bool IsPlacing => placingItem != null;
    public bool IsDragging => draggingObject != null;
    public bool IsCounterPlacementActive =>
        (placingItem != null && placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter)
        || draggingMountedItem != null;
    public bool IsCustomerWallPlacementActive =>
        (placingItem != null && placingItem.placementSurface == ItemDefinition.PlacementSurface.CustomerWall)
        || draggingWallDoor != null;
    public int CounterHoverSpan => placingItem != null
        && placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter
            ? Mathf.Max(1, placingItem.counterSlotSpan)
            : draggingMountedItem != null && draggingMountedItem.itemDefinition != null
                ? Mathf.Max(1, draggingMountedItem.itemDefinition.counterSlotSpan)
                : 1;

    void Start()
    {
        GameObject staticCounter = GameObject.Find("Countertop");
        if (staticCounter != null)
        {
            CounterSurface surface = staticCounter.GetComponent<CounterSurface>();
            if (surface == null) surface = staticCounter.AddComponent<CounterSurface>();
            surface.slotCount = 10;
            surface.EnsurePlacementCollider();
            if (staticCounter.GetComponent<GridObstacle>() == null)
                staticCounter.AddComponent<GridObstacle>();
            if (grid != null) grid.ResyncOccupancyFromScene();
        }
        EnsureRequiredCustomerDoors();
        SetHint(false);
    }

    void Update()
    {
        if (!modeManager || !grid || !inventory) return;

        // Only allow placement in Build mode
        if (modeManager.CurrentMode != GameModeManager.Mode.Build)
        {
            CancelPlacement();
            CancelDrag();
            return;
        }

        // Cancel (placement or drag). Only consume Escape when there is something to cancel
        // so the pause menu can still open in Build mode.
        if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.Escape) && (IsDragging || IsPlacing))
        {
            if (PauseMenuUI.IsOpen)
                return;

            if (IsDragging)
                CancelDrag();
            else
                CancelPlacement();
            PauseMenuUI.MarkEscapeHandled();
            return;
        }

        // Block only the visible Inventory / Management rectangles. Transparent
        // full-screen canvas roots must not prevent interaction with the world.
        if (UIInputFocusGuard.IsPointerOverBlockingPanel) return;

        // ---- Dragging a placed object ----
        if (IsDragging)
        {
            if (Input.GetMouseButtonDown(1))
            {
                if (draggingWallDoor != null && draggingWallDoor.permanentFixture)
                    CancelDrag();
                else
                    RemoveDraggedAndReturnToInventory();
                return;
            }

            if (draggingWallDoor != null)
            {
                if (TryGetCustomerWallPlacement(draggingObject, out Vector3 doorPosition,
                    out Quaternion doorRotation, out CustomerWallDoor.WallSide doorSide))
                {
                    draggingObject.transform.SetPositionAndRotation(doorPosition, doorRotation);
                    draggingWallDoor.wallSide = doorSide;
                    RequestDoorPreviewWallRefresh(doorPosition, doorSide);
                    if (Input.GetMouseButtonDown(0))
                        TryPlaceDraggedDoor(doorPosition, doorRotation, doorSide);
                }
                return;
            }

            if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.R) && !IsCurrentDragRotationLocked())
            {
                dragRotation = (dragRotation + 1) % 4;
                ApplyDraggedRotation();
                Sfx.Play(SfxId.BuildRotate);
            }

            if (draggingMountedItem != null)
            {
                if (TryGetHoveredCounter(out CounterSurface counter, out int slot, requireAvailable: true))
                {
                    PositionOnCounter(draggingObject, draggingMountedItem.itemDefinition, counter, slot, dragRotation);
                    if (Input.GetMouseButtonDown(0))
                        TryPlaceDraggedOnCounter(counter, slot);
                }
                return;
            }

            if (TryGetHoveredCell(out int dx, out int dy))
            {
                GetEffectiveDragSize(out int sx, out int sy);
                ClampPlacementOrigin(ref dx, ref dy, sx, sy);
                Vector3 pos = grid.GetFootprintCenter(dx, dy, sx, sy);
                pos.y = GetYOnFloor(draggingObject, grid.Origin.y);
                draggingObject.transform.position = pos;
                if (Input.GetMouseButtonDown(0))
                    TryPlaceDraggedAt(dx, dy);
            }
            return;
        }

        // ---- Placing new item from inventory ----
        if (!IsPlacing)
        {
            if (Input.GetMouseButtonDown(0))
                TryStartDrag();
            return;
        }

        if (placingItem.placementSurface == ItemDefinition.PlacementSurface.CustomerWall)
        {
            if (TryGetCustomerWallPlacement(ghost, out Vector3 doorPosition,
                out Quaternion doorRotation, out CustomerWallDoor.WallSide doorSide))
            {
                ghost.transform.SetPositionAndRotation(doorPosition, doorRotation);
                if (Input.GetMouseButtonDown(0))
                    TryPlaceCustomerDoor(doorPosition, doorRotation, doorSide);
            }
            return;
        }

        if (placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter)
        {
            if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.R) && !IsRotationLocked(placingItem, ghost))
            {
                placementRotation = (placementRotation + 1) % 4;
                Sfx.Play(SfxId.BuildRotate);
            }

            if (TryGetHoveredCounter(out CounterSurface counter, out int slot, requireAvailable: true))
            {
                PositionOnCounter(ghost, placingItem, counter, slot, placementRotation);
                if (Input.GetMouseButtonDown(0))
                    TryPlaceOnCounter(counter, slot);
            }
            return;
        }

        // R = rotate while placing
        if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.R) && !IsRotationLocked(placingItem, ghost))
        {
            placementRotation = (placementRotation + 1) % 4;
            if (ghost)
                ghost.transform.rotation = Quaternion.Euler(placingItem.placementEuler + Vector3.up * (placementRotation * 90f));
            Sfx.Play(SfxId.BuildRotate);
        }

        // Move ghost to hovered cell (centered on footprint if multi-tile)
        if (TryGetHoveredCell(out int x, out int y))
        {
            GetEffectivePlacementSize(out int sizeX, out int sizeY);
            ClampPlacementOrigin(ref x, ref y, sizeX, sizeY);
            if (ghost)
            {
                Vector3 pos = grid.GetFootprintCenter(x, y, sizeX, sizeY);
                pos.y = GetYOnFloor(ghost, grid.Origin.y);
                ghost.transform.position = pos;
            }
            if (Input.GetMouseButtonDown(0))
                TryPlaceAt(x, y);
        }
    }

    public void BeginPlacement(ItemDefinition item)
    {
        if (item == null || item.prefab == null) return;
        if (OnboardingTutorial.IsStationLocked(item)) return;

        placingItem = item;
        placementRotation = GetInitialPlacementRotation(item);
        hasDoorPreviewWallLocation = false;

        // Make a ghost preview
        if (ghost) Destroy(ghost);
        ghost = Instantiate(item.prefab);
        ghost.name = item.prefab.name + " Ghost";
        ghost.transform.localScale = GetPlacementScale(item);
        ghost.transform.rotation = Quaternion.Euler(item.placementEuler + Vector3.up * (placementRotation * 90f));

        if (item.placementSurface == ItemDefinition.PlacementSurface.CustomerWall
            && ghost.GetComponent<CustomerWallDoor>() == null)
            ghost.AddComponent<CustomerWallDoor>();

        // Keep the original opaque materials: custom station shaders do not all support transparency.

        // Optional: disable colliders so raycasts don�t hit the ghost
        foreach (var c in ghost.GetComponentsInChildren<Collider>())
            c.enabled = false;

        SetHint(true);
        Sfx.Play(SfxId.BuildPickup);
    }

    static int GetInitialPlacementRotation(ItemDefinition item)
    {
        // Keep mounting directions for counters and doors. Floor stations start
        // reversed from the original default, ready to back onto the wall.
        if (item.placementSurface != ItemDefinition.PlacementSurface.Floor
            || IsRotationLocked(item, item.prefab)) return 0;
        // Assembly's model faces opposite the other floor stations.
        if (item.prefab != null && item.prefab.GetComponentInChildren<AssemblyStation>(true) != null)
            return 0;
        return 2;
    }
    public void CancelPlacement()
    {
        placingItem = null;
        hasDoorPreviewWallLocation = false;

        if (ghost) Destroy(ghost);
        ghost = null;

        SetHint(false);
    }

    void SetHint(bool show, bool dragging = false)
    {
        if (!placementHintText) return;
        Transform hintParent = placementHintText.transform.parent;
        GameObject hintRoot = hintParent != null && hintParent.name == "KeybindTipPanel"
            ? hintParent.gameObject
            : placementHintText.gameObject;
        hintRoot.SetActive(show);
        if (!show) return;

        bool rotationLocked = dragging
            ? IsCurrentDragRotationLocked()
            : IsRotationLocked(placingItem, ghost);
        if (dragging)
            placementHintText.text = rotationLocked
                ? "LMB: Place    RMB: Remove & return to inventory    ESC: Cancel"
                : "LMB: Place    R: Rotate    RMB: Remove & return to inventory    ESC: Cancel";
        else if (placingItem != null && placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter)
            placementHintText.text = rotationLocked
                ? "LMB: Place on counter    ESC: Cancel"
                : "LMB: Place on counter    R: Rotate    ESC: Cancel";
        else if (placingItem != null && placingItem.placementSurface == ItemDefinition.PlacementSurface.CustomerWall)
            placementHintText.text = "Click a lobby wall    LMB: Place    ESC: Cancel";
        else
            placementHintText.text = rotationLocked
                ? "LMB: Place    ESC: Cancel"
                : "LMB: Place    R: Rotate    ESC: Cancel";
    }

    bool IsCurrentDragRotationLocked()
    {
        ItemDefinition item = draggingMountedItem != null
            ? draggingMountedItem.itemDefinition
            : draggingObject != null ? draggingObject.GetComponent<PlacedBuildItem>()?.itemDefinition : null;
        return IsRotationLocked(item, draggingObject);
    }

    static bool IsRotationLocked(ItemDefinition item, GameObject instance)
    {
        if (instance != null
            && (instance.GetComponent<Register>() != null || instance.GetComponent<HeatLampStation>() != null))
            return true;
        if (item == null) return false;
        if (item.placementSurface == ItemDefinition.PlacementSurface.CustomerWall)
            return true;
        if (item.buildFunction == ItemDefinition.BuildFunction.Register)
            return true;
        return item.prefab != null && item.prefab.GetComponent<HeatLampStation>() != null;
    }

    /// <summary>Combined bounds of all renderers (or colliders) in world space.</summary>
    static Bounds GetCombinedBounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds result = new Bounds(obj.transform.position, Vector3.one);
        if (renderers != null)
        {
            foreach (Renderer renderer in renderers)
            {
                CounterMountedItem mounted = renderer.GetComponentInParent<CounterMountedItem>();
                if (mounted != null && mounted.gameObject != obj) continue;
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (found) return result;
        }
        var colliders = obj.GetComponentsInChildren<Collider>();
        if (colliders != null)
        {
            foreach (Collider collider in colliders)
            {
                CounterMountedItem mounted = collider.GetComponentInParent<CounterMountedItem>();
                if (mounted != null && mounted.gameObject != obj) continue;
                if (!found) { result = collider.bounds; found = true; }
                else result.Encapsulate(collider.bounds);
            }
            if (found) return result;
        }
        return new Bounds(obj.transform.position, Vector3.one);
    }

    /// <summary>World Y so the bottom of the object sits on floorY.</summary>
    static float GetYOnFloor(GameObject obj, float floorY)
    {
        Bounds b = GetCombinedBounds(obj);
        return floorY - b.min.y + obj.transform.position.y;
    }

    /// <summary>Effective footprint size for current drag rotation (90/270 swap X and Y).</summary>
    void GetEffectiveDragSize(out int sizeX, out int sizeY)
    {
        sizeX = Mathf.Max(1, dragFootprint != null ? dragFootprint.sizeX : 1);
        sizeY = Mathf.Max(1, dragFootprint != null ? dragFootprint.sizeY : 1);
        if (dragRotation == 1 || dragRotation == 3)
        {
            int t = sizeX;
            sizeX = sizeY;
            sizeY = t;
        }
    }

    /// <summary>Effective footprint size for current placement rotation (90/270 swap X and Y).</summary>
    void GetEffectivePlacementSize(out int sizeX, out int sizeY)
    {
        sizeX = 1;
        sizeY = 1;
        if (placingItem?.prefab == null) return;
        var fp = placingItem.prefab.GetComponent<BuildFootprint>();
        sizeX = Mathf.Max(1, fp != null ? fp.sizeX : placingItem.footprintX);
        sizeY = Mathf.Max(1, fp != null ? fp.sizeY : placingItem.footprintY);
        if (placementRotation == 1 || placementRotation == 3)
        {
            int t = sizeX;
            sizeX = sizeY;
            sizeY = t;
        }
    }

    bool TryGetHoveredCell(out int x, out int y)
    {
        x = y = 0;
        if (grid == null || grid.Nodes == null || !TryGetFloorAimPoint(out Vector3 aim)) return false;
        int sizeX, sizeY;
        if (IsDragging) GetEffectiveDragSize(out sizeX, out sizeY);
        else GetEffectivePlacementSize(out sizeX, out sizeY);

        // Search legal origins using the entire rotated footprint, not just one tile.
        // Projecting onto the floor plane lets the cursor pass through walls.
        float bestDistance = float.PositiveInfinity;
        bool found = false;
        for (int cx = 0; cx <= grid.Width - sizeX; cx++)
            for (int cy = 0; cy <= grid.Height - sizeY; cy++)
            {
                if (!grid.CanPlace(cx, cy, sizeX, sizeY)) continue;
                Vector3 center = grid.GetFootprintCenter(cx, cy, sizeX, sizeY);
                float distance = (new Vector2(center.x, center.z) - new Vector2(aim.x, aim.z)).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                x = cx;
                y = cy;
                found = true;
            }
        return found;
    }

    bool TryGetFloorAimPoint(out Vector3 aim)
    {
        aim = Vector3.zero;
        if (Camera.main == null || grid == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var floorPlane = new Plane(Vector3.up, new Vector3(0f, grid.Origin.y, 0f));
        if (!floorPlane.Raycast(ray, out float distance) || distance > 500f) return false;
        aim = ray.GetPoint(distance);
        return true;
    }
    bool TryGetHoveredCounter(out CounterSurface surface, out int slot, bool requireAvailable)
    {
        surface = null;
        slot = -1;
        if (Camera.main == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, -1);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            CounterSurface candidate = hit.collider.GetComponentInParent<CounterSurface>();
            if (candidate == null) continue;
            if (requireAvailable && !candidate.IsAvailable) continue;
            int span = placingItem != null
                ? Mathf.Max(1, placingItem.counterSlotSpan)
                : draggingMountedItem != null && draggingMountedItem.itemDefinition != null
                    ? Mathf.Max(1, draggingMountedItem.itemDefinition.counterSlotSpan)
                    : 1;
            int candidateSlot = candidate.GetNearestAvailableSlot(hit.point, span);
            if (candidateSlot < 0) continue;
            surface = candidate;
            slot = candidateSlot;
            return true;
        }

        // A wall or floor click near a counter snaps to its nearest free slot.
        if (!TryGetFloorAimPoint(out Vector3 aim)) return false;
        int requiredSpan = placingItem != null ? Mathf.Max(1, placingItem.counterSlotSpan)
            : draggingMountedItem != null && draggingMountedItem.itemDefinition != null
                ? Mathf.Max(1, draggingMountedItem.itemDefinition.counterSlotSpan) : 1;
        float bestDistance = float.PositiveInfinity;
        foreach (var candidate in FindObjectsByType<CounterSurface>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (ghost != null && candidate.transform.IsChildOf(ghost.transform)) continue;
            if (draggingObject != null && candidate.transform.IsChildOf(draggingObject.transform)) continue;
            if (requireAvailable && !candidate.IsAvailable) continue;
            int candidateSlot = candidate.GetNearestAvailableSlot(aim, requiredSpan);
            if (candidateSlot < 0) continue;
            Vector3 center = candidate.GetSlotWorldCenter(candidateSlot, requiredSpan);
            float distance = (new Vector2(center.x, center.z) - new Vector2(aim.x, aim.z)).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            surface = candidate;
            slot = candidateSlot;
        }
        return surface != null;
    }

    static void PositionOnCounter(GameObject obj, ItemDefinition item, CounterSurface surface, int slot, int rotation)
    {
        if (obj == null || item == null || surface == null) return;
        obj.transform.SetParent(null, true);
        obj.transform.localScale = GetPlacementScale(item);
        obj.transform.rotation = surface.transform.rotation
            * Quaternion.Euler(item.placementEuler + Vector3.up * (rotation * 90f));
        Vector3 mountPosition = surface.GetMountPosition(
            obj, slot, Mathf.Max(1, item.counterSlotSpan), item.counterEmbedDepth)
            + surface.transform.TransformVector(item.counterLocalOffset);
        if (item.useFixedCounterY) mountPosition.y = item.fixedCounterY;
        obj.transform.position = mountPosition;
    }

    /// <summary>Clamp (x,y) so that footprint (sizeX, sizeY) fits fully inside the grid.</summary>
    void ClampPlacementOrigin(ref int x, ref int y, int sizeX, int sizeY)
    {
        if (grid == null) return;
        x = Mathf.Clamp(x, 0, grid.Width - sizeX);
        y = Mathf.Clamp(y, 0, grid.Height - sizeY);
    }

    /// <summary>Get footprint origin (bottom-left cell) from an object's world position (center) and effective size.</summary>
    bool GetFootprintOriginFromCenter(Vector3 worldCenter, int sizeX, int sizeY, out int originX, out int originY)
    {
        originX = 0;
        originY = 0;
        if (grid == null) return false;
        float cx = (worldCenter.x - grid.Origin.x) / grid.cellSize;
        float cy = (worldCenter.z - grid.Origin.z) / grid.cellSize;
        originX = Mathf.RoundToInt(cx - sizeX * 0.5f);
        originY = Mathf.RoundToInt(cy - sizeY * 0.5f);
        return originX >= 0 && originX + sizeX <= grid.Width && originY >= 0 && originY + sizeY <= grid.Height;
    }

    void TryStartDrag()
    {
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int layerMask = placeableLayer.value != 0 ? placeableLayer.value : -1;
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            CustomerWallDoor wallDoor = hit.collider.GetComponentInParent<CustomerWallDoor>();
            if (wallDoor != null)
            {
                if (!IsDoubleClickEdit(wallDoor.gameObject)) return;
                draggingObject = wallDoor.gameObject;
                draggingWallDoor = wallDoor;
                dragOriginalPosition = draggingObject.transform.position;
                dragOriginalWorldRotation = draggingObject.transform.rotation;
                dragOriginalWallSide = wallDoor.wallSide;
                wallDoor.GetComponent<CameraOcclusionWall>()?.SetPlacementLock(true);
                SetDraggedObjectHighlighted(draggingObject);
                SetHint(true, true);
                Sfx.Play(SfxId.BuildPickup);
                return;
            }

            var mounted = hit.collider.GetComponentInParent<CounterMountedItem>();
            if (mounted != null)
            {
                if (!IsDoubleClickEdit(mounted.gameObject)) return;
                Register activeRegister = mounted.GetComponent<Register>();
                HeatLampStation stockedLamp = mounted.GetComponent<HeatLampStation>();
                if ((activeRegister != null && activeRegister.QueueCount + activeRegister.PickupCount > 0)
                    || (stockedLamp != null && stockedLamp.Count > 0))
                {
                    Sfx.Play(SfxId.UiError);
                    return;
                }
                draggingObject = mounted.gameObject;
                draggingMountedItem = mounted;
                dragOriginalSurface = mounted.surface;
                dragOriginalSlot = mounted.slotIndex;
                dragOriginalPosition = draggingObject.transform.position;
                dragOriginalWorldRotation = draggingObject.transform.rotation;
                float authoredY = mounted.itemDefinition != null ? mounted.itemDefinition.placementEuler.y : 0f;
                float surfaceY = dragOriginalSurface != null ? dragOriginalSurface.transform.eulerAngles.y : 0f;
                float relativeY = Mathf.DeltaAngle(surfaceY + authoredY, draggingObject.transform.eulerAngles.y);
                dragRotation = (Mathf.RoundToInt(relativeY / 90f) % 4 + 4) % 4;
                if (IsRotationLocked(mounted.itemDefinition, draggingObject))
                    dragRotation = 0;
                if (dragOriginalSurface != null)
                    dragOriginalSurface.Release(mounted);
                draggingObject.transform.SetParent(null, true);
                SetDraggedObjectHighlighted(draggingObject);
                SetHint(true, true);
                Sfx.Play(SfxId.BuildPickup);
                return;
            }

            var fp = hit.collider.GetComponentInParent<BuildFootprint>();
            if (fp == null) continue;

            GameObject root = fp.gameObject;
            if (root == ghost) continue;
            CounterSurface occupiedCounter = root.GetComponent<CounterSurface>();
            if (occupiedCounter != null && !occupiedCounter.IsAvailable)
            {
                Sfx.Play(SfxId.UiError);
                return;
            }

            // Compute effective size from rotation first (needed for origin)

            // Derive rotation from current object (nearest 90�)
            float ay = root.transform.eulerAngles.y;
            int rot = (Mathf.RoundToInt(ay / 90f) % 4 + 4) % 4;
            int sizeX = Mathf.Max(1, fp.sizeX);
            int sizeY = Mathf.Max(1, fp.sizeY);
            if (rot == 1 || rot == 3) { int t = sizeX; sizeX = sizeY; sizeY = t; }

            if (!GetFootprintOriginFromCenter(root.transform.position, sizeX, sizeY, out int ox, out int oy))
                continue;

            if (!IsDoubleClickEdit(root)) return;

            grid.SetOccupied(ox, oy, sizeX, sizeY, false);
            draggingObject = root;
            dragFootprint = fp;
            SetDraggedObjectHighlighted(root);
            dragOrigX = ox;
            dragOrigY = oy;
            dragRotation = rot;
            dragOrigRotation = rot;
            SetHint(true, true);
            Sfx.Play(SfxId.BuildPickup);
            return;
        }
    }

    bool IsDoubleClickEdit(GameObject target)
    {
        if (target == null) return false;
        int id = target.GetInstanceID();
        float now = Time.unscaledTime;
        bool doubled = id == lastEditClickId && now - lastEditClickTime <= EditDoubleClickSeconds;
        lastEditClickId = doubled ? 0 : id;
        lastEditClickTime = now;
        return doubled;
    }

    void TryPlaceDraggedAt(int x, int y)
    {
        if (draggingObject == null || dragFootprint == null) return;

        GetEffectiveDragSize(out int sizeX, out int sizeY);
        if (!grid.CanPlace(x, y, sizeX, sizeY))
        {
            Sfx.Play(SfxId.BuildPlaceFail);
            return;
        }

        Vector3 pos = grid.GetFootprintCenter(x, y, sizeX, sizeY);
        pos.y = GetYOnFloor(draggingObject, grid.Origin.y);
        draggingObject.transform.position = pos;
        grid.SetOccupied(x, y, sizeX, sizeY, true);
        Sfx.Play(SfxId.BuildPlace);
        EndDrag();
    }

    void TryPlaceDraggedOnCounter(CounterSurface surface, int slot)
    {
        if (draggingObject == null || draggingMountedItem == null || surface == null || !surface.IsAvailable)
            return;
        PositionOnCounter(draggingObject, draggingMountedItem.itemDefinition, surface, slot, dragRotation);
        if (!surface.Attach(draggingMountedItem, slot)) return;
        FinalizeMountedOrientation(draggingObject);
        Sfx.Play(SfxId.BuildPlace);
        EndDrag();
    }

    public void CancelDrag()
    {
        if (draggingObject == null) return;

        if (draggingWallDoor != null)
        {
            draggingObject.transform.SetPositionAndRotation(dragOriginalPosition, dragOriginalWorldRotation);
            draggingWallDoor.wallSide = dragOriginalWallSide;
            RefreshPerimeterWalls();
            EndDrag();
            return;
        }

        if (draggingMountedItem != null)
        {
            draggingObject.transform.position = dragOriginalPosition;
            draggingObject.transform.rotation = dragOriginalWorldRotation;
            if (dragOriginalSurface != null)
                dragOriginalSurface.Attach(draggingMountedItem, dragOriginalSlot);
            EndDrag();
            return;
        }

        if (dragFootprint == null) return;

        draggingObject.transform.rotation = Quaternion.Euler(0f, dragOrigRotation * 90f, 0f);
        int sizeX = Mathf.Max(1, dragFootprint.sizeX);
        int sizeY = Mathf.Max(1, dragFootprint.sizeY);
        if (dragOrigRotation == 1 || dragOrigRotation == 3) { int t = sizeX; sizeX = sizeY; sizeY = t; }
        Vector3 pos = grid.GetFootprintCenter(dragOrigX, dragOrigY, sizeX, sizeY);
        pos.y = GetYOnFloor(draggingObject, grid.Origin.y);
        draggingObject.transform.position = pos;
        grid.SetOccupied(dragOrigX, dragOrigY, sizeX, sizeY, true);
        EndDrag();
    }

    void EndDrag()
    {
        if (draggingWallDoor != null)
            draggingWallDoor.GetComponent<CameraOcclusionWall>()?.SetPlacementLock(false);
        ClearDraggedObjectHighlight();
        draggingObject = null;
        dragFootprint = null;
        draggingMountedItem = null;
        draggingWallDoor = null;
        dragOriginalSurface = null;
        dragOriginalSlot = -1;
        SetHint(IsPlacing);
    }

    void ApplyDraggedRotation()
    {
        if (draggingObject == null) return;
        if (draggingMountedItem != null)
        {
            ItemDefinition item = draggingMountedItem.itemDefinition;
            Quaternion baseRotation = dragOriginalSurface != null ? dragOriginalSurface.transform.rotation : Quaternion.identity;
            Vector3 authored = item != null ? item.placementEuler : Vector3.zero;
            draggingObject.transform.rotation = baseRotation * Quaternion.Euler(authored + Vector3.up * (dragRotation * 90f));
            return;
        }
        draggingObject.transform.rotation = Quaternion.Euler(0f, dragRotation * 90f, 0f);
    }

    void SetDraggedObjectHighlighted(GameObject selectedObject)
    {
        ClearDraggedObjectHighlight();
        dragSelectionHighlight = StationSelectionHighlight.EnsureOn(selectedObject);
        if (dragSelectionHighlight != null)
            dragSelectionHighlight.SetSelected(true);
    }

    void ClearDraggedObjectHighlight()
    {
        if (dragSelectionHighlight != null)
            dragSelectionHighlight.SetSelected(false);
        dragSelectionHighlight = null;
    }

    void RemoveDraggedAndReturnToInventory()
    {
        if (draggingObject == null) return;

        ItemDefinition returnedItem = draggingMountedItem != null
            ? draggingMountedItem.itemDefinition
            : draggingObject.GetComponent<PlacedBuildItem>()?.itemDefinition;
        if (returnedItem != null && inventory != null)
            inventory.AddOne(returnedItem);

        if (draggingMountedItem != null && draggingMountedItem.surface != null)
            draggingMountedItem.surface.Release(draggingMountedItem);

        ClearDraggedObjectHighlight();
        Object.Destroy(draggingObject);
        draggingObject = null;
        dragFootprint = null;
        draggingMountedItem = null;
        draggingWallDoor = null;
        dragOriginalSurface = null;
        dragOriginalSlot = -1;
        SetHint(IsPlacing);
        Sfx.Play(SfxId.BuildRemove);

        var invUI = FindObjectOfType<InventoryUI>();
        if (invUI != null) invUI.RefreshAll();
    }

    void TryPlaceAt(int x, int y)
    {
        if (placingItem == null) return;

        // Must own one to place
        if (inventory.GetCount(placingItem) <= 0) return;

        GetEffectivePlacementSize(out int sizeX, out int sizeY);

        // Check space
        if (!grid.CanPlace(x, y, sizeX, sizeY))
        {
            Sfx.Play(SfxId.BuildPlaceFail);
            return;
        }

        // Consume inventory
        if (!inventory.TryConsumeOne(placingItem))
        {
            Sfx.Play(SfxId.UiError);
            return;
        }

        // Place real object (centered on footprint, bottom on floor, with placement rotation)
        var placed = Instantiate(placingItem.prefab);
        placed.transform.localScale = GetPlacementScale(placingItem);
        placed.transform.rotation = Quaternion.Euler(placingItem.placementEuler + Vector3.up * (placementRotation * 90f));
        ConfigurePlacedObject(placed, placingItem);
        Vector3 pos = grid.GetFootprintCenter(x, y, sizeX, sizeY);
        placed.transform.position = pos;
        pos.y = GetYOnFloor(placed, grid.Origin.y);
        placed.transform.position = pos;
        var pbi = placed.GetComponent<PlacedBuildItem>();
        if (pbi == null) pbi = placed.AddComponent<PlacedBuildItem>();
        pbi.itemDefinition = placingItem;

        // Refresh inventory UI so quantities update
        var invUI = FindObjectOfType<InventoryUI>();
        if (invUI) invUI.RefreshAll();

        // Mark occupied + not walkable
        grid.SetOccupied(x, y, sizeX, sizeY, true);
        Sfx.Play(SfxId.BuildPlace);

        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
            undo.NotifyStationPlaced(placingItem, placed);

        // Keep placing until user cancels (or you can auto-cancel if you want)
        // If you want auto-cancel after 1 placement, uncomment:
        // CancelPlacement();
    }

    void TryPlaceCustomerDoor(Vector3 position, Quaternion rotation, CustomerWallDoor.WallSide side)
    {
        if (placingItem == null || placingItem.placementSurface != ItemDefinition.PlacementSurface.CustomerWall)
            return;
        CustomerWallDoor previewDoor = ghost != null ? ghost.GetComponent<CustomerWallDoor>() : null;
        if (inventory.GetCount(placingItem) <= 0 || !IsDoorLocationAvailable(side, position, previewDoor))
        {
            Sfx.Play(SfxId.BuildPlaceFail);
            return;
        }
        if (!inventory.TryConsumeOne(placingItem))
        {
            Sfx.Play(SfxId.UiError);
            return;
        }

        GameObject placed = Instantiate(placingItem.prefab);
        placed.transform.localScale = GetPlacementScale(placingItem);
        placed.transform.SetPositionAndRotation(position, rotation);
        CustomerWallDoor door = placed.GetComponent<CustomerWallDoor>();
        if (door == null) door = placed.AddComponent<CustomerWallDoor>();
        door.wallSide = side;
        door.EnsureWallCutaway();

        PlacedBuildItem pbi = placed.GetComponent<PlacedBuildItem>();
        if (pbi == null) pbi = placed.AddComponent<PlacedBuildItem>();
        pbi.itemDefinition = placingItem;

        InventoryUI invUI = FindObjectOfType<InventoryUI>();
        if (invUI != null) invUI.RefreshAll();
        RefreshPerimeterWalls();
        Sfx.Play(SfxId.BuildPlace);

        PurchaseUndoManager undo = PurchaseUndoManager.Ensure();
        if (undo != null) undo.NotifyStationPlaced(placingItem, placed);
    }

    void TryPlaceDraggedDoor(Vector3 position, Quaternion rotation, CustomerWallDoor.WallSide side)
    {
        if (draggingObject == null || draggingWallDoor == null) return;
        if (!IsDoorLocationAvailable(side, position, draggingWallDoor))
        {
            Sfx.Play(SfxId.BuildPlaceFail);
            return;
        }

        draggingObject.transform.SetPositionAndRotation(position, rotation);
        draggingWallDoor.wallSide = side;
        draggingWallDoor.EnsureWallCutaway();
        RefreshPerimeterWalls();
        Sfx.Play(SfxId.BuildPlace);
        EndDrag();
    }

    bool TryGetCustomerWallPlacement(GameObject preview, out Vector3 position,
        out Quaternion rotation, out CustomerWallDoor.WallSide side)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        side = CustomerWallDoor.WallSide.South;
        if (!TryGetCustomerDoorTile(out Vector3 tileCenter, out float floorY,
            out Vector3 wallPosition, out side)) return false;

        float yaw = side == CustomerWallDoor.WallSide.East
            ? 90f
            : side == CustomerWallDoor.WallSide.North ? 180f : 0f;

        ItemDefinition doorDefinition = placingItem;
        if (doorDefinition == null && preview != null)
        {
            PlacedBuildItem placedItem = preview.GetComponent<PlacedBuildItem>();
            if (placedItem != null) doorDefinition = placedItem.itemDefinition;
        }
        Quaternion candidateRotation = Quaternion.Euler(doorDefinition != null
            ? doorDefinition.placementEuler + Vector3.up * yaw
            : Vector3.up * yaw);
        Vector3 candidatePosition = wallPosition;

        if (preview != null)
        {
            preview.transform.rotation = candidateRotation;
            preview.transform.position = candidatePosition;
            candidatePosition = AlignDoorModelToWall(preview, candidatePosition, floorY, side);
            preview.transform.position = candidatePosition;
            CustomerWallDoor previewDoor = preview.GetComponent<CustomerWallDoor>();
            if (previewDoor != null)
            {
                previewDoor.wallSide = side;
                RequestDoorPreviewWallRefresh(candidatePosition, side);
            }
        }

        CustomerWallDoor ignored = draggingWallDoor != null
            ? draggingWallDoor
            : preview != null ? preview.GetComponent<CustomerWallDoor>() : null;
        if (!IsDoorLocationAvailable(side, candidatePosition, ignored)) return false;
        position = candidatePosition;
        rotation = candidateRotation;
        return true;
    }

    void RequestDoorPreviewWallRefresh(Vector3 position, CustomerWallDoor.WallSide side)
    {
        bool changed = !hasDoorPreviewWallLocation
            || side != lastDoorPreviewWallSide
            || (position - lastDoorPreviewWallPosition).sqrMagnitude > 0.0001f;
        if (!changed) return;

        hasDoorPreviewWallLocation = true;
        lastDoorPreviewWallPosition = position;
        lastDoorPreviewWallSide = side;
        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        if (walls != null) walls.RequestRefresh();
    }

    static Vector3 AlignDoorModelToWall(GameObject doorObject, Vector3 wallBoundary,
        float floorY, CustomerWallDoor.WallSide side)
    {
        if (doorObject == null) return wallBoundary;

        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        float halfThickness = walls != null ? Mathf.Max(0.05f, walls.thickness * 0.5f) : 0.5f;
        float inset = walls != null ? walls.wallInset : 0f;
        float outwardOffset = Mathf.Max(0f, halfThickness - inset);

        Bounds bounds = GetCombinedBounds(doorObject);
        Vector3 correction = Vector3.zero;
        correction.y = floorY - bounds.min.y;

        if (side == CustomerWallDoor.WallSide.East)
        {
            float wallCenterX = wallBoundary.x + outwardOffset;
            correction.x = wallCenterX - bounds.center.x;
            correction.z = wallBoundary.z - bounds.center.z;
        }
        else
        {
            float direction = side == CustomerWallDoor.WallSide.North ? 1f : -1f;
            float wallCenterZ = wallBoundary.z + direction * outwardOffset;
            correction.x = wallBoundary.x - bounds.center.x;
            correction.z = wallCenterZ - bounds.center.z;
        }

        return doorObject.transform.position + correction;
    }

    public bool TryGetCustomerDoorHighlight(out Vector3 tileCenter, out float tileSize)
    {
        tileCenter = Vector3.zero;
        tileSize = grid != null ? Mathf.Max(0.1f, grid.cellSize) : 1f;
        if (!IsCustomerWallPlacementActive) return false;
        return TryGetCustomerDoorTile(out tileCenter, out _, out _, out _);
    }

    bool TryGetCustomerDoorTile(out Vector3 tileCenter, out float floorY,
        out Vector3 wallPosition, out CustomerWallDoor.WallSide side)
    {
        tileCenter = Vector3.zero;
        floorY = 0f;
        wallPosition = Vector3.zero;
        side = CustomerWallDoor.WallSide.South;
        if (Camera.main == null || grid == null) return false;

        Transform customer = grid.customerFloor;
        if (customer == null)
        {
            GameObject found = GameObject.Find("CustomerFloor");
            if (found != null) customer = found.transform;
        }
        Renderer customerRenderer = customer != null ? customer.GetComponentInChildren<Renderer>() : null;
        if (customerRenderer == null) return false;

        float minX = customerRenderer.bounds.min.x;
        float maxX = customerRenderer.bounds.max.x;
        float minZ = customerRenderer.bounds.min.z;
        float maxZ = customerRenderer.bounds.max.z;
        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        if (walls != null && walls.TryGetLobbyWallBounds(out float wallMinX, out float wallMaxX,
            out float wallMinZ, out float wallMaxZ))
        {
            minX = wallMinX;
            maxX = wallMaxX;
            minZ = wallMinZ;
            maxZ = wallMaxZ;
        }
        floorY = customerRenderer.bounds.max.y;

        float cell = Mathf.Max(0.1f, grid.cellSize);
        float originX = grid.Origin.x
            + Mathf.Round((minX - grid.Origin.x) / cell) * cell;
        float originZ = grid.Origin.z
            + Mathf.Round((minZ - grid.Origin.z) / cell) * cell;
        int width = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) / cell));
        int height = Mathf.Max(1, Mathf.RoundToInt((maxZ - minZ) / cell));
        maxX = originX + width * cell;
        maxZ = originZ + height * cell;

        if (!TryGetDoorAimPoint(floorY, out Vector3 aim))
            return false;

        float distSouth = Mathf.Abs(aim.z - originZ);
        float distEast = Mathf.Abs(aim.x - maxX);
        float distNorth = Mathf.Abs(aim.z - maxZ);
        float nearest = Mathf.Min(distSouth, Mathf.Min(distEast, distNorth));
        float snapRange = cell * 6f;
        if (nearest > snapRange)
            return false;

        if (distEast <= distSouth && distEast <= distNorth)
            side = CustomerWallDoor.WallSide.East;
        else if (distSouth <= distNorth)
            side = CustomerWallDoor.WallSide.South;
        else
            side = CustomerWallDoor.WallSide.North;

        // Keep the wide door frame off the corners.
        float cornerPad = cell * 1.5f;
        float alongMin;
        float alongMax;
        float along;
        if (side == CustomerWallDoor.WallSide.East)
        {
            alongMin = originZ + cornerPad;
            alongMax = maxZ - cornerPad;
            along = Mathf.Clamp(aim.z, alongMin, alongMax);
            int z = Mathf.Clamp(Mathf.FloorToInt((along - originZ) / cell), 1, height - 2);
            tileCenter = new Vector3(maxX - cell * 0.5f, floorY + 0.04f, originZ + (z + 0.5f) * cell);
            wallPosition = new Vector3(maxX, floorY, tileCenter.z);
        }
        else
        {
            alongMin = originX + cornerPad;
            alongMax = maxX - cornerPad;
            along = Mathf.Clamp(aim.x, alongMin, alongMax);
            int x = Mathf.Clamp(Mathf.FloorToInt((along - originX) / cell), 1, width - 2);
            float wallZ = side == CustomerWallDoor.WallSide.South ? originZ : maxZ;
            tileCenter = new Vector3(originX + (x + 0.5f) * cell, floorY + 0.04f, wallZ);
            wallPosition = new Vector3(tileCenter.x, floorY, wallZ);
        }

        return alongMax > alongMin;
    }

    bool TryGetDoorAimPoint(float floorY, out Vector3 aim)
    {
        aim = Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null) continue;
            if (ghost != null && col.transform.IsChildOf(ghost.transform)) continue;
            if (draggingObject != null && col.transform.IsChildOf(draggingObject.transform))
            {
                aim = hits[i].point;
                return true;
            }
            aim = hits[i].point;
            return true;
        }

        Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
        if (!floorPlane.Raycast(ray, out float rayDistance) || rayDistance < 0f)
            return false;
        aim = ray.GetPoint(rayDistance);
        return true;
    }

    bool IsDoorLocationAvailable(CustomerWallDoor.WallSide side, Vector3 position, CustomerWallDoor ignored)
    {
        CustomerWallDoor[] doors = FindObjectsByType<CustomerWallDoor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float minimumSpacing = Mathf.Max(1.75f, grid != null ? grid.cellSize * 1.75f : 1.75f);
        foreach (CustomerWallDoor door in doors)
        {
            if (door == null || door == ignored || door.wallSide != side) continue;
            float distance = side == CustomerWallDoor.WallSide.East
                ? Mathf.Abs(door.transform.position.z - position.z)
                : Mathf.Abs(door.transform.position.x - position.x);
            if (distance < minimumSpacing) return false;
        }
        return true;
    }

    static void RefreshPerimeterWalls()
    {
        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        if (walls != null) walls.FitToGrid();
    }

    void EnsureRequiredCustomerDoors()
    {
        CustomerWallDoor entrance = null;
        CustomerWallDoor[] existing = FindObjectsByType<CustomerWallDoor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            CustomerWallDoor door = existing[i];
            if (!CustomerWallDoor.IsGameplayDoor(door)) continue;
            if (door.role == CustomerWallDoor.DoorRole.Entrance && entrance == null)
                entrance = door;
        }

        ItemDefinition definition = FindCustomerDoorDefinition();
        if (definition == null || definition.prefab == null) return;

        if (entrance == null)
            entrance = CreateRequiredCustomerDoor(definition, CustomerWallDoor.DoorRole.Entrance,
                CustomerWallDoor.WallSide.East, 0.36f);
        ConfigureRequiredDoor(entrance, definition, CustomerWallDoor.DoorRole.Entrance);
        for (int i = 0; i < existing.Length; i++)
        {
            CustomerWallDoor door = existing[i];
            if (!CustomerWallDoor.IsGameplayDoor(door) || door == entrance) continue;
            Destroy(door.gameObject);
        }
        RefreshPerimeterWalls();
    }

    ItemDefinition FindCustomerDoorDefinition()
    {
        if (inventory == null || inventory.allItems == null) return null;
        for (int i = 0; i < inventory.allItems.Count; i++)
        {
            ItemDefinition item = inventory.allItems[i];
            if (item != null
                && item.prefab != null
                && item.placementSurface == ItemDefinition.PlacementSurface.CustomerWall)
                return item;
        }
        return null;
    }

    CustomerWallDoor CreateRequiredCustomerDoor(ItemDefinition definition,
        CustomerWallDoor.DoorRole role, CustomerWallDoor.WallSide side, float along01)
    {
        if (!TryGetDefaultDoorBoundary(side, along01, out Vector3 boundary, out float floorY))
            return null;

        GameObject placed = Instantiate(definition.prefab);
        placed.name = role == CustomerWallDoor.DoorRole.Entrance
            ? "Customer Entrance"
            : "Customer Exit";
        placed.transform.localScale = GetPlacementScale(definition);
        float yaw = side == CustomerWallDoor.WallSide.East
            ? 90f
            : side == CustomerWallDoor.WallSide.North ? 180f : 0f;
        placed.transform.rotation = Quaternion.Euler(definition.placementEuler + Vector3.up * yaw);
        placed.transform.position = boundary;
        placed.transform.position = AlignDoorModelToWall(placed, boundary, floorY, side);

        CustomerWallDoor door = placed.GetComponent<CustomerWallDoor>();
        if (door == null) door = placed.AddComponent<CustomerWallDoor>();
        door.wallSide = side;
        door.role = role;
        door.permanentFixture = true;

        PlacedBuildItem placedItem = placed.GetComponent<PlacedBuildItem>();
        if (placedItem == null) placedItem = placed.AddComponent<PlacedBuildItem>();
        placedItem.itemDefinition = definition;
        door.EnsureWallCutaway();
        return door;
    }

    static void ConfigureRequiredDoor(CustomerWallDoor door, ItemDefinition definition,
        CustomerWallDoor.DoorRole role)
    {
        if (door == null) return;
        door.role = role;
        door.permanentFixture = true;
        door.gameObject.name = role == CustomerWallDoor.DoorRole.Entrance
            ? "Customer Entrance"
            : "Customer Exit";
        PlacedBuildItem placedItem = door.GetComponent<PlacedBuildItem>();
        if (placedItem == null) placedItem = door.gameObject.AddComponent<PlacedBuildItem>();
        if (placedItem.itemDefinition == null) placedItem.itemDefinition = definition;
        door.EnsureWallCutaway();
    }

    bool TryGetDefaultDoorBoundary(CustomerWallDoor.WallSide side, float along01,
        out Vector3 boundary, out float floorY)
    {
        boundary = Vector3.zero;
        floorY = 0f;
        Transform customer = grid != null ? grid.customerFloor : null;
        if (customer == null)
        {
            GameObject found = GameObject.Find("CustomerFloor");
            if (found != null) customer = found.transform;
        }
        Renderer floorRenderer = customer != null ? customer.GetComponentInChildren<Renderer>() : null;
        if (floorRenderer == null) return false;

        Bounds floorBounds = floorRenderer.bounds;
        float minX = floorBounds.min.x;
        float maxX = floorBounds.max.x;
        float minZ = floorBounds.min.z;
        float maxZ = floorBounds.max.z;
        KitchenPerimeterWalls walls = FindFirstObjectByType<KitchenPerimeterWalls>();
        if (walls != null && walls.TryGetLobbyWallBounds(out float wallMinX,
            out float wallMaxX, out float wallMinZ, out float wallMaxZ))
        {
            minX = wallMinX;
            maxX = wallMaxX;
            minZ = wallMinZ;
            maxZ = wallMaxZ;
        }

        floorY = floorBounds.max.y;
        float cell = grid != null ? Mathf.Max(0.1f, grid.cellSize) : 1f;
        float pad = cell * 1.5f;
        along01 = Mathf.Clamp01(along01);
        if (side == CustomerWallDoor.WallSide.East)
        {
            float z = Mathf.Lerp(minZ + pad, maxZ - pad, along01);
            boundary = new Vector3(maxX, floorY, z);
        }
        else
        {
            float x = Mathf.Lerp(minX + pad, maxX - pad, along01);
            float z = side == CustomerWallDoor.WallSide.North ? maxZ : minZ;
            boundary = new Vector3(x, floorY, z);
        }
        return maxX - minX > pad * 2f && maxZ - minZ > pad * 2f;
    }

    void TryPlaceOnCounter(CounterSurface surface, int slot)
    {
        if (placingItem == null || surface == null || !surface.IsAvailable) return;
        if (inventory.GetCount(placingItem) <= 0) return;
        if (!inventory.TryConsumeOne(placingItem))
        {
            Sfx.Play(SfxId.UiError);
            return;
        }

        GameObject placed = Instantiate(placingItem.prefab);
        ConfigurePlacedObject(placed, placingItem);
        var mounted = placed.GetComponent<CounterMountedItem>();
        if (mounted == null) mounted = placed.AddComponent<CounterMountedItem>();
        mounted.itemDefinition = placingItem;
        PositionOnCounter(placed, placingItem, surface, slot, placementRotation);
        surface.Attach(mounted, slot);
        FinalizeMountedOrientation(placed);

        var pbi = placed.GetComponent<PlacedBuildItem>();
        if (pbi == null) pbi = placed.AddComponent<PlacedBuildItem>();
        pbi.itemDefinition = placingItem;

        var invUI = FindObjectOfType<InventoryUI>();
        if (invUI != null) invUI.RefreshAll();
        Sfx.Play(SfxId.BuildPlace);

        var undo = PurchaseUndoManager.Ensure();
        if (undo != null) undo.NotifyStationPlaced(placingItem, placed);
    }

    static void ConfigurePlacedObject(GameObject placed, ItemDefinition item)
    {
        if (placed == null || item == null) return;

        if (item.placementSurface == ItemDefinition.PlacementSurface.Floor)
        {
            BuildFootprint footprint = placed.GetComponent<BuildFootprint>();
            if (footprint == null) footprint = placed.AddComponent<BuildFootprint>();
            footprint.sizeX = Mathf.Max(1, item.footprintX);
            footprint.sizeY = Mathf.Max(1, item.footprintY);
        }

        if (item.buildFunction == ItemDefinition.BuildFunction.Counter)
        {
            if (placed.GetComponent<CounterSurface>() == null)
                placed.AddComponent<CounterSurface>();
        }
        else if (item.buildFunction == ItemDefinition.BuildFunction.Register)
        {
            if (placed.GetComponentInChildren<Collider>() == null)
                placed.AddComponent<BoxCollider>();
            Register register = placed.GetComponent<Register>();
            if (register == null) register = placed.AddComponent<Register>();
            register.isEnabled = OnboardingTutorial.IsActive;
            RegisterHover hover = placed.GetComponent<RegisterHover>();
            if (hover == null) hover = placed.AddComponent<RegisterHover>();
            if (hover.rend == null) hover.rend = placed.GetComponentInChildren<Renderer>();
        }
    }

    static Vector3 GetPlacementScale(ItemDefinition item)
    {
        if (item != null)
        {
            Vector3 configured = item.placementScale;
            if (Mathf.Abs(configured.x) > 0.0001f
                && Mathf.Abs(configured.y) > 0.0001f
                && Mathf.Abs(configured.z) > 0.0001f)
                return configured;
            if (item.prefab != null)
                return item.prefab.transform.localScale;
        }
        return Vector3.one;
    }

    static void FinalizeMountedOrientation(GameObject placed)
    {
        if (placed == null) return;
        Register register = placed.GetComponent<Register>();
        if (register == null) return;

        Vector3 lobby = -placed.transform.forward;
        lobby.y = 0f;
        register.queueDirection = lobby.sqrMagnitude > 0.01f ? lobby.normalized : Vector3.right;

        StationInteractionTiles tiles = placed.GetComponent<StationInteractionTiles>();
        if (tiles != null) tiles.RebuildGeneratedHighlight();
    }
}
