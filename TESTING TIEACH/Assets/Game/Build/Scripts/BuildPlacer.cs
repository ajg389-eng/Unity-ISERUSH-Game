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

    public bool IsPlacing => placingItem != null;
    public bool IsDragging => draggingObject != null;
    public bool IsCounterPlacementActive =>
        (placingItem != null && placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter)
        || draggingMountedItem != null;
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
        if (Input.GetKeyDown(KeyCode.Escape) && (IsDragging || IsPlacing))
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

        // ---- Dragging a placed object ----
        if (IsDragging)
        {
            if (Input.GetMouseButtonDown(1))
            {
                RemoveDraggedAndReturnToInventory();
                return;
            }
            if (Input.GetKeyDown(KeyCode.R) && !IsCurrentDragRotationLocked())
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

        if (placingItem.placementSurface == ItemDefinition.PlacementSurface.Counter)
        {
            if (Input.GetKeyDown(KeyCode.R) && !IsRotationLocked(placingItem, ghost))
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
        if (Input.GetKeyDown(KeyCode.R) && !IsRotationLocked(placingItem, ghost))
        {
            placementRotation = (placementRotation + 1) % 4;
            if (ghost)
                ghost.transform.rotation = Quaternion.Euler(0f, placementRotation * 90f, 0f);
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

        placingItem = item;
        placementRotation = 0;

        // Make a ghost preview
        if (ghost) Destroy(ghost);
        ghost = Instantiate(item.prefab);
        ghost.name = item.prefab.name + " Ghost";
        ghost.transform.localScale = GetPlacementScale(item);
        ghost.transform.rotation = Quaternion.Euler(item.placementEuler);

        MakeTranslucent(ghost, 0.7f); // 0.5 = 50% transparent

        // Optional: disable colliders so raycasts don�t hit the ghost
        foreach (var c in ghost.GetComponentsInChildren<Collider>())
            c.enabled = false;

        SetHint(true);
        Sfx.Play(SfxId.BuildPickup);
    }

    public void CancelPlacement()
    {
        placingItem = null;

        if (ghost) Destroy(ghost);
        ghost = null;

        SetHint(false);
    }

    void MakeTranslucent(GameObject obj, float alpha)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();

        foreach (var r in renderers)
        {
            foreach (var mat in r.materials)
            {
                // Switch material to transparent mode (URP compatible)
                mat.SetFloat("_Surface", 1); // 1 = Transparent in URP
                mat.SetFloat("_Blend", 0);
                mat.SetFloat("_AlphaClip", 0);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;

                Color c = mat.color;
                c.a = alpha;
                mat.color = c;
            }
        }
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
                ? "Hover a free counter    LMB: Place    ESC: Cancel"
                : "Hover a free counter    LMB: Place    R: Rotate    ESC: Cancel";
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
        if (Camera.main == null || grid == null) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, floorLayer))
            return false;

        if (!grid.WorldToCell(hit.point, out int cx, out int cy))
            return false;
        x = Mathf.Clamp(cx, 0, grid.Width - 1);
        y = Mathf.Clamp(cy, 0, grid.Height - 1);
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
        return false;
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
            var mounted = hit.collider.GetComponentInParent<CounterMountedItem>();
            if (mounted != null)
            {
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
        ClearDraggedObjectHighlight();
        draggingObject = null;
        dragFootprint = null;
        draggingMountedItem = null;
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
            register.isEnabled = false;
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
