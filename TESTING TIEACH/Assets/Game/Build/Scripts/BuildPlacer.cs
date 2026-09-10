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
    private int dragOrigX, dragOrigY;
    /// <summary>0, 1, 2, 3 = 0�, 90�, 180�, 270� while dragging.</summary>
    private int dragRotation;
    private int dragOrigRotation;

    public bool IsPlacing => placingItem != null;
    public bool IsDragging => draggingObject != null;

    void Start()
    {
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
        if (Input.GetKeyDown(KeyCode.R))
        {
            dragRotation = (dragRotation + 1) % 4;
            draggingObject.transform.rotation = Quaternion.Euler(0f, dragRotation * 90f, 0f);
            Sfx.Play(SfxId.BuildRotate);
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

        // R = rotate while placing
        if (Input.GetKeyDown(KeyCode.R))
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
        ghost.transform.rotation = Quaternion.identity;

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
        placementHintText.gameObject.SetActive(show);
        if (show) placementHintText.text = dragging ? "LMB: Place    R: Rotate    RMB: Remove & return to inventory    ESC: Cancel" : "LMB: Place    R: Rotate    ESC: Cancel";
    }

    /// <summary>Combined bounds of all renderers (or colliders) in world space.</summary>
    static Bounds GetCombinedBounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
        var colliders = obj.GetComponentsInChildren<Collider>();
        if (colliders != null && colliders.Length > 0)
        {
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                b.Encapsulate(colliders[i].bounds);
            return b;
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
        if (fp != null) { sizeX = Mathf.Max(1, fp.sizeX); sizeY = Mathf.Max(1, fp.sizeY); }
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
            var fp = hit.collider.GetComponentInParent<BuildFootprint>();
            if (fp == null) continue;

            GameObject root = fp.gameObject;
            if (root == ghost) continue;

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

    public void CancelDrag()
    {
        if (draggingObject == null || dragFootprint == null) return;

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
        draggingObject = null;
        dragFootprint = null;
        SetHint(IsPlacing);
    }

    void RemoveDraggedAndReturnToInventory()
    {
        if (draggingObject == null) return;

        var pbi = draggingObject.GetComponent<PlacedBuildItem>();
        if (pbi != null && pbi.itemDefinition != null && inventory != null)
            inventory.AddOne(pbi.itemDefinition);

        Object.Destroy(draggingObject);
        draggingObject = null;
        dragFootprint = null;
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
        Vector3 pos = grid.GetFootprintCenter(x, y, sizeX, sizeY);
        placed.transform.position = pos;
        placed.transform.rotation = Quaternion.Euler(0f, placementRotation * 90f, 0f);
        pos.y = GetYOnFloor(placed, grid.Origin.y);
        placed.transform.position = pos;
        var pbi = placed.AddComponent<PlacedBuildItem>();
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
}