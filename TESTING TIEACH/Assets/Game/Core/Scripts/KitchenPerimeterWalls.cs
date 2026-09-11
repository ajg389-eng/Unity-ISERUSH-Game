using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Wall layout:
/// - Customer floor: static south + east walls (always from CustomerFloor bounds)
/// - Work floor: north + west + south walls that refit on expand
/// - Work floor east wall only on expand protrusions (keeps register counter open)
/// Avoids a combined AABB so expand doesn't enclose empty grass courtyards.
/// </summary>
public class KitchenPerimeterWalls : MonoBehaviour
{
    public GridManager grid;
    [Tooltip("Dining / lobby floor. Auto-finds CustomerFloor if empty.")]
    public Transform customerFloor;
    public float thickness = 1f;
    public float height = 5f;
    [Tooltip("Pull walls inward onto the floor edge (closes the visible gap between tiles and bricks).")]
    public float wallInset = 0.5f;

    [Header("Runtime generation")]
    [Tooltip("Build walls at runtime and resize work-floor walls when the floor expands.")]
    public bool generateAtRuntime = true;

    [Header("Look")]
    [Tooltip("Brick material (e.g. BrickWall2). Auto-copied from scene Wall if empty.")]
    public Material brickMaterial;

    [Header("Windows")]
    public float windowWidth = 1.7f;
    public float windowHeight = 1.85f;
    public float windowSill = 1.2f;
    [Range(0.08f, 0.45f)]
    public float windowGlassAlpha = 0.22f;

    [Header("Work floor (dynamic — follows expand)")]
    public bool buildWorkNorth = true;
    public bool buildWorkWest = true;
    public bool buildWorkSouth = true;
    [Tooltip("East wall only on work-floor segments that stick past the customer floor (not the register counter).")]
    public bool buildWorkEastProtrusions = true;

    [Header("Customer floor (static — from CustomerFloor bounds)")]
    public bool buildCustomerEast = true;
    public bool buildCustomerSouth = true;

    Transform root;
    Transform west;
    Transform south;
    Transform northCap;
    Transform eastCap;
    Transform windowRoot;
    Transform currentWallGroup;
    readonly List<Transform> windowPool = new List<Transform>();
    readonly List<Transform> brickPool = new List<Transform>();
    readonly List<Transform> wallGroupPool = new List<Transform>();
    int windowsUsed;
    int bricksUsed;
    int wallGroupsUsed;
    Mesh sharedMesh;
    Material[] sharedMaterials;
    Material glassMaterial;
    Transform backWall;
    Transform diningWall;
    Transform entranceDoor;
    bool copyingLook;
    bool rebuilding;

    void Awake()
    {
        if (grid == null)
            grid = GetComponent<GridManager>();
        if (grid == null)
            grid = GridManager.Instance;
        CacheLookFromExistingWalls();

        if (!generateAtRuntime)
            HideRuntimeGeneratedRoot();
    }

    public void FitToGrid()
    {
        if (!generateAtRuntime)
        {
            HideRuntimeGeneratedRoot();
            return;
        }
        if (rebuilding) return;
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : GetComponent<GridManager>();
        if (grid == null || grid.Width <= 0 || grid.Height <= 0) return;
        if (!grid.HasBaseline) return;

        rebuilding = true;
        try
        {
            CacheLookFromExistingWalls();
            CacheEntranceDoor();
            RestoreOriginalWalls();
            EnsureGlassMaterial();
            EnsurePieces();
            if (root != null)
                root.gameObject.SetActive(true);
            Hide(eastCap);
            Hide(south);
            Hide(northCap);
            Hide(west);
            HideNamed("KitchenRoofGlass");
            HideNamed("KitchenRoofTrim_N");
            HideNamed("KitchenRoofTrim_S");
            HideNamed("KitchenRoofTrim_W");
            HideNamed("KitchenRoofTrim_E");

            // Hide authored Wall / Wall (1) — replaced by brick pieces.
            SetOriginalBackSolidVisible(false);
            SetOriginalDiningSolidVisible(false);

            HideNamed("KitchenWalls");
            HideNamed("Wall_North");
            HideNamed("Wall_South");
            HideNamed("Wall_East");
            HideNamed("Wall_West");

            EnsureCustomerFloor();
            float wMinX = 0f, wMaxX = 0f, wMinZ = 0f, wMaxZ = 0f;
            if (!TryGetFloorBounds(grid.floor != null ? grid.floor : null, out wMinX, out wMaxX, out wMinZ, out wMaxZ)
                && grid != null)
            {
                wMinX = grid.Origin.x;
                wMinZ = grid.Origin.z;
                wMaxX = wMinX + grid.Width * grid.cellSize;
                wMaxZ = wMinZ + grid.Height * grid.cellSize;
            }

            float cMinX = 0f, cMaxX = 0f, cMinZ = 0f, cMaxZ = 0f;
            bool hasCustomer = customerFloor != null
                && TryGetFloorBounds(customerFloor, out cMinX, out cMaxX, out cMinZ, out cMaxZ);

            float y = grid.Origin.y;
            float t = Mathf.Max(0.2f, thickness);
            // Slight overlap so corners don't leave gaps
            float corner = t;
            // Positive inset pulls wall centers onto the floor edge (was sitting fully outside).
            float inset = Mathf.Clamp(wallInset, 0f, t);

            windowsUsed = 0;
            bricksUsed = 0;
            wallGroupsUsed = 0;
            currentWallGroup = null;

            // --- Work floor dynamic walls (follow expand) ---
            if (buildWorkNorth)
            {
                float northZ = hasCustomer ? Mathf.Max(wMaxZ, cMaxZ) : wMaxZ;
                float nMinX = hasCustomer ? Mathf.Min(wMinX, cMinX) : wMinX;
                float nMaxX = hasCustomer ? Mathf.Max(wMaxX, cMaxX) : wMaxX;
                BuildWallWithWindows(
                    new Vector3((nMinX + nMaxX) * 0.5f, y, northZ + t * 0.5f - inset),
                    0f, (nMaxX - nMinX) + corner, t, forceFlankingDoor: false, Vector3.forward);
            }

            if (buildWorkWest)
            {
                BuildWallWithWindows(
                    new Vector3(wMinX - t * 0.5f + inset, y, (wMinZ + wMaxZ) * 0.5f),
                    90f, (wMaxZ - wMinZ) + corner, t, forceFlankingDoor: false, Vector3.left);
            }

            if (buildWorkSouth)
            {
                BuildWallWithWindows(
                    new Vector3((wMinX + wMaxX) * 0.5f, y, wMinZ - t * 0.5f + inset),
                    180f, (wMaxX - wMinX) + corner, t, forceFlankingDoor: false, Vector3.back);
            }

            // East wall only on work-floor protrusions past the customer floor (not at registers).
            if (buildWorkEastProtrusions && hasCustomer)
            {
                // South of customer
                if (wMinZ < cMinZ - 0.05f)
                {
                    float z0 = wMinZ;
                    float z1 = Mathf.Min(wMaxZ, cMinZ);
                    float len = z1 - z0;
                    if (len > 0.35f)
                    {
                        BuildWallWithWindows(
                            new Vector3(wMaxX + t * 0.5f - inset, y, (z0 + z1) * 0.5f),
                            90f, len + corner * 0.5f, t, forceFlankingDoor: false, Vector3.right);
                    }
                }

                // North of customer
                if (wMaxZ > cMaxZ + 0.05f)
                {
                    float z0 = Mathf.Max(wMinZ, cMaxZ);
                    float z1 = wMaxZ;
                    float len = z1 - z0;
                    if (len > 0.35f)
                    {
                        BuildWallWithWindows(
                            new Vector3(wMaxX + t * 0.5f - inset, y, (z0 + z1) * 0.5f),
                            90f, len + corner * 0.5f, t, forceFlankingDoor: false, Vector3.right);
                    }
                }
            }
            else if (buildWorkEastProtrusions && !hasCustomer)
            {
                // No dining floor — full work east wall
                BuildWallWithWindows(
                    new Vector3(wMaxX + t * 0.5f - inset, y, (wMinZ + wMaxZ) * 0.5f),
                    90f, (wMaxZ - wMinZ) + corner, t, forceFlankingDoor: false, Vector3.right);
            }

            // --- Customer floor static walls (from CustomerFloor bounds only) ---
            if (hasCustomer && buildCustomerEast)
            {
                BuildWallWithWindows(
                    new Vector3(cMaxX + t * 0.5f - inset, y, (cMinZ + cMaxZ) * 0.5f),
                    90f, (cMaxZ - cMinZ) + corner, t, forceFlankingDoor: false, Vector3.right);
            }

            if (hasCustomer && buildCustomerSouth)
            {
                BuildWallWithWindows(
                    new Vector3((cMinX + cMaxX) * 0.5f, y, cMinZ - t * 0.5f + inset),
                    180f, (cMaxX - cMinX) + corner, t, forceFlankingDoor: true, Vector3.back);
            }

            for (int i = windowsUsed; i < windowPool.Count; i++)
                Hide(windowPool[i]);
            for (int i = bricksUsed; i < brickPool.Count; i++)
                Hide(brickPool[i]);
            for (int i = wallGroupsUsed; i < wallGroupPool.Count; i++)
                Hide(wallGroupPool[i]);
            currentWallGroup = null;
        }
        finally
        {
            rebuilding = false;
        }
    }

    void EnsureCustomerFloor()
    {
        if (customerFloor != null) return;
        var go = GameObject.Find("CustomerFloor");
        if (go != null) customerFloor = go.transform;
    }

    bool TryGetBuildingBounds(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        // Kept for any external callers; prefer work/customer-specific bounds in FitToGrid.
        minX = maxX = minZ = maxZ = 0f;
        EnsureCustomerFloor();
        bool any = TryGetFloorBounds(grid != null ? grid.floor : null, out minX, out maxX, out minZ, out maxZ);
        if (customerFloor != null && TryGetFloorBounds(customerFloor, out float cMinX, out float cMaxX, out float cMinZ, out float cMaxZ))
        {
            if (!any)
            {
                minX = cMinX; maxX = cMaxX; minZ = cMinZ; maxZ = cMaxZ;
                return true;
            }
            minX = Mathf.Min(minX, cMinX);
            maxX = Mathf.Max(maxX, cMaxX);
            minZ = Mathf.Min(minZ, cMinZ);
            maxZ = Mathf.Max(maxZ, cMaxZ);
            return true;
        }
        return any;
    }

    static bool TryGetFloorBounds(Transform floor, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;
        if (floor == null) return false;
        var rend = floor.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Bounds b = rend.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minZ = b.min.z;
            maxZ = b.max.z;
            return b.size.x > 0.01f && b.size.z > 0.01f;
        }

        // Unity Plane fallback (10x10 mesh)
        float hx = Mathf.Abs(floor.lossyScale.x) * 5f;
        float hz = Mathf.Abs(floor.lossyScale.z) * 5f;
        minX = floor.position.x - hx;
        maxX = floor.position.x + hx;
        minZ = floor.position.z - hz;
        maxZ = floor.position.z + hz;
        return true;
    }

    static int WindowCountForLength(float length)
    {
        if (length < 10f) return 1;
        return 2;
    }

    void BuildWallWithWindows(Vector3 baseCenter, float yaw, float length, float thick, bool forceFlankingDoor, Vector3 outward)
    {
        if (length < 0.5f) return;

        BeginWallGroup(outward, baseCenter);

        float floorY = baseCenter.y;
        Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        Vector3 wallPos = new Vector3(baseCenter.x, 0f, baseCenter.z);

        float doorAlong = 0f;
        float doorHalf = 0f;
        float doorTop = 0f;
        bool hasDoor = TryDoorOnWall(wallPos, along, length, thick, out doorAlong, out doorHalf, out doorTop);

        float sillH = Mathf.Max(0.35f, windowSill);
        float windowTop = sillH + windowHeight;
        float bandTop = hasDoor ? Mathf.Max(windowTop, doorTop) : windowTop;
        float headerH = Mathf.Max(0.35f, height - bandTop);
        float openingH = Mathf.Max(0.4f, bandTop - sillH);

        var openings = new List<(float start, float end, bool door)>();
        if (hasDoor)
            openings.Add((doorAlong - doorHalf, doorAlong + doorHalf, true));

        if (forceFlankingDoor && hasDoor)
        {
            // Keep a full brick gap between the door edge and each window.
            // Negative along is the lobby-view LEFT side of the door.
            float clearance = 1.6f;
            float leftCenter = doorAlong - doorHalf - clearance - windowWidth * 0.5f;
            float rightCenter = doorAlong + doorHalf + clearance + windowWidth * 0.5f;
            TryAddWindowOpening(openings, leftCenter, length, true, doorAlong, doorHalf);
            TryAddWindowOpening(openings, rightCenter, length, true, doorAlong, doorHalf);
        }
        else if (!forceFlankingDoor)
        {
            int count = WindowCountForLength(length);
            float step = length / (count + 1);
            for (int i = 1; i <= count; i++)
            {
                float centerAlong = -length * 0.5f + step * i;
                TryAddWindowOpening(openings, centerAlong, length, hasDoor, doorAlong, doorHalf);
            }
        }

        openings.Sort((a, b) => a.start.CompareTo(b.start));

        PlaceBrick(
            wallPos + Vector3.up * (floorY + bandTop + headerH * 0.5f),
            yaw, length, headerH, thick);

        if (!hasDoor)
        {
            PlaceBrick(
                wallPos + Vector3.up * (floorY + sillH * 0.5f),
                yaw, length, sillH, thick);
        }

        float cursor = -length * 0.5f;
        for (int i = 0; i < openings.Count; i++)
        {
            var op = openings[i];
            float pierLen = op.start - cursor;
            if (pierLen > 0.08f)
            {
                float pierCenter = cursor + pierLen * 0.5f;
                float pierH = hasDoor ? bandTop : openingH;
                float pierY = hasDoor ? floorY + pierH * 0.5f : floorY + sillH + openingH * 0.5f;
                PlaceBrick(wallPos + along * pierCenter + Vector3.up * pierY, yaw, pierLen, pierH, thick);
            }

            if (op.door)
            {
                // Leave a hole for the existing Door mesh.
            }
            else
            {
                if (hasDoor)
                {
                    PlaceBrick(
                        wallPos + along * ((op.start + op.end) * 0.5f) + Vector3.up * (floorY + sillH * 0.5f),
                        yaw, op.end - op.start, sillH, thick);
                }

                Vector3 winPos = wallPos + along * ((op.start + op.end) * 0.5f)
                    + Vector3.up * (floorY + sillH + openingH * 0.5f);
                PlaceWindow(winPos, yaw, thick, openingH);
            }

            cursor = op.end;
        }

        float lastLen = length * 0.5f - cursor;
        if (lastLen > 0.08f)
        {
            float pierCenter = cursor + lastLen * 0.5f;
            float pierH = hasDoor ? bandTop : openingH;
            float pierY = hasDoor ? floorY + pierH * 0.5f : floorY + sillH + openingH * 0.5f;
            PlaceBrick(wallPos + along * pierCenter + Vector3.up * pierY, yaw, lastLen, pierH, thick);
        }
    }

    void BeginWallGroup(Vector3 outward, Vector3 worldAnchor)
    {
        Transform g = EnsureWallGroup(wallGroupsUsed);
        wallGroupsUsed++;
        g.gameObject.SetActive(true);
        // Anchor at the wall center so cutaway side-tests use the real wall position.
        g.position = new Vector3(worldAnchor.x, 0f, worldAnchor.z);
        g.rotation = Quaternion.identity;
        g.localScale = Vector3.one;

        var occ = g.GetComponent<CameraOcclusionWall>();
        if (occ != null)
            occ.SnapUp();

        currentWallGroup = g;
        TagOcclusionWall(g, outward);
    }

    Transform EnsureWallGroup(int index)
    {
        while (wallGroupPool.Count <= index)
        {
            var go = new GameObject("KitchenWallGroup_" + wallGroupPool.Count);
            go.transform.SetParent(root != null ? root : transform, false);
            wallGroupPool.Add(go.transform);
        }
        return wallGroupPool[index];
    }

    void TryAddWindowOpening(
        List<(float start, float end, bool door)> openings,
        float centerAlong, float length, bool hasDoor, float doorAlong, float doorHalf)
    {
        float half = windowWidth * 0.5f;
        float start = centerAlong - half;
        float end = centerAlong + half;
        if (start < -length * 0.5f + 0.2f || end > length * 0.5f - 0.2f)
            return;
        if (hasDoor && start < doorAlong + doorHalf + 1.2f && end > doorAlong - doorHalf - 1.2f)
            return;
        openings.Add((start, end, false));
    }

    bool TryDoorOnWall(Vector3 wallPos, Vector3 along, float length, float thick, out float doorAlong, out float doorHalf, out float doorTop)
    {
        doorAlong = 0f;
        doorHalf = 0f;
        doorTop = 0f;
        CacheEntranceDoor();
        if (entranceDoor == null) return false;

        along = along.normalized;
        Bounds b = entranceDoor.GetComponent<Collider>() != null
            ? entranceDoor.GetComponent<Collider>().bounds
            : new Bounds(entranceDoor.position, entranceDoor.lossyScale);

        Vector3 toDoor = b.center - wallPos;
        toDoor.y = 0f;
        Vector3 inward = Vector3.Cross(Vector3.up, along);
        if (inward.sqrMagnitude > 0.0001f && Mathf.Abs(Vector3.Dot(toDoor, inward.normalized)) > thick + 1.25f)
            return false;

        float minAlong = float.MaxValue;
        float maxAlong = float.MinValue;
        Vector3 ext = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + new Vector3(
                (i & 1) == 0 ? -ext.x : ext.x,
                (i & 2) == 0 ? -ext.y : ext.y,
                (i & 4) == 0 ? -ext.z : ext.z);
            float a = Vector3.Dot(corner - wallPos, along);
            if (a < minAlong) minAlong = a;
            if (a > maxAlong) maxAlong = a;
        }

        doorAlong = (minAlong + maxAlong) * 0.5f;
        doorHalf = (maxAlong - minAlong) * 0.5f + 0.15f;
        if (Mathf.Abs(doorAlong) > length * 0.5f + 0.75f)
            return false;

        doorTop = Mathf.Max(2.4f, b.max.y - (grid != null ? grid.Origin.y : 0f));
        return true;
    }

    void PlaceWindow(Vector3 pos, float yaw, float thick, float openingH)
    {
        Transform win = EnsureWindow(windowsUsed);
        windowsUsed++;
        StripPieceOcclusion(win);
        win.gameObject.SetActive(true);
        if (currentWallGroup != null)
            win.SetParent(currentWallGroup, true);
        win.position = pos;
        win.rotation = Quaternion.Euler(0f, yaw, 0f);

        Transform frameT = win.Find("Frame");
        if (frameT != null)
            frameT.gameObject.SetActive(false);
        float glassDeep = 0.04f;
        Transform glassA = win.Find("GlassA");
        Transform glassB = win.Find("GlassB");
        if (glassA != null)
        {
            glassA.localPosition = new Vector3(0f, 0f, thick * 0.5f);
            glassA.localScale = new Vector3(windowWidth, openingH, glassDeep);
        }
        if (glassB != null)
        {
            glassB.localPosition = new Vector3(0f, 0f, -thick * 0.5f);
            glassB.localScale = new Vector3(windowWidth, openingH, glassDeep);
        }
    }

    void PlaceBrick(Vector3 pos, float yaw, float along, float up, float thick)
    {
        Transform b = EnsureBrick(bricksUsed);
        bricksUsed++;
        StripPieceOcclusion(b);
        b.gameObject.SetActive(true);
        if (currentWallGroup != null)
            b.SetParent(currentWallGroup, true);
        b.position = pos;
        b.rotation = Quaternion.Euler(0f, yaw, 0f);
        b.localScale = new Vector3(Mathf.Max(0.08f, along), Mathf.Max(0.08f, up), thick);
    }

    static void StripPieceOcclusion(Transform piece)
    {
        if (piece == null) return;
        var occ = piece.GetComponent<CameraOcclusionWall>();
        if (occ != null)
            Object.Destroy(occ);
    }

    void TagOcclusionWall(Transform wall, Vector3 outward)
    {
        if (wall == null) return;
        var occ = wall.GetComponent<CameraOcclusionWall>();
        if (occ == null)
            occ = wall.gameObject.AddComponent<CameraOcclusionWall>();

        occ.SetOutward(outward);
        occ.lowerDistance = Mathf.Max(3.5f, height * 0.95f);
        occ.SnapUp();
        CameraWallCutaway.EnsureExists();
    }

    Transform EnsureWindow(int index)
    {
        while (windowPool.Count <= index)
        {
            var go = new GameObject("KitchenWindow_" + windowPool.Count);
            go.transform.SetParent(windowRoot, false);

            CreateWindowPart(go.transform, "GlassA", brick: false);
            CreateWindowPart(go.transform, "GlassB", brick: false);
            windowPool.Add(go.transform);
        }

        return windowPool[index];
    }

    void CreateWindowPart(Transform parent, string name, bool brick)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        var col = part.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var rend = part.GetComponent<MeshRenderer>();
        if (rend == null) return;
        if (brick)
        {
            if (sharedMaterials != null && sharedMaterials.Length > 0)
                rend.sharedMaterials = sharedMaterials;
        }
        else
        {
            if (glassMaterial != null)
                rend.sharedMaterial = glassMaterial;
            rend.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    Transform EnsureBrick(int index)
    {
        while (brickPool.Count <= index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "KitchenWallBrick_" + brickPool.Count;
            go.transform.SetParent(windowRoot, false);
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && sharedMesh != null)
                filter.sharedMesh = sharedMesh;
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var brick = GetBrickMaterial();
                if (brick != null)
                    rend.sharedMaterial = brick;
                else if (sharedMaterials != null && sharedMaterials.Length > 0)
                    rend.sharedMaterials = sharedMaterials;
            }
            brickPool.Add(go.transform);
        }

        // Refresh material on pooled bricks (in case look was cached later).
        var existing = brickPool[index];
        var existingRend = existing != null ? existing.GetComponent<MeshRenderer>() : null;
        var mat = GetBrickMaterial();
        if (existingRend != null && mat != null)
            existingRend.sharedMaterial = mat;

        return brickPool[index];
    }

    Vector3 origBackWorldPos;
    Vector3 origBackScale;
    bool origBackSaved;

    void FitBackWall(float outerX)
    {
        if (backWall == null) return;
        if (!origBackSaved)
        {
            origBackWorldPos = backWall.position;
            origBackScale = backWall.localScale;
            origBackSaved = true;
        }

        float origWest = origBackWorldPos.x - Mathf.Abs(origBackScale.x) * 0.5f;
        float origEast = origBackWorldPos.x + Mathf.Abs(origBackScale.x) * 0.5f;
        float westX = Mathf.Min(origWest, outerX);
        float length = Mathf.Max(0.25f, origEast - westX);
        backWall.position = new Vector3(westX + length * 0.5f, origBackWorldPos.y, origBackWorldPos.z);
        backWall.localScale = new Vector3(length, origBackScale.y, origBackScale.z);
    }

    void RestoreBackWallSize()
    {
        if (backWall == null || !origBackSaved) return;
        backWall.position = origBackWorldPos;
        backWall.localScale = origBackScale;
    }

    void SetOriginalBackSolidVisible(bool vis)
    {
        if (backWall == null) return;
        var rend = backWall.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = vis;
        var col = backWall.GetComponent<Collider>();
        if (col != null) col.enabled = vis;
    }

    void SetOriginalDiningSolidVisible(bool vis)
    {
        if (diningWall == null) return;
        var rend = diningWall.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = vis;
        var col = diningWall.GetComponent<Collider>();
        if (col != null) col.enabled = vis;
    }

    void HideRuntimeGeneratedRoot()
    {
        var existing = GameObject.Find("KitchenExpandWalls");
        if (existing != null)
            existing.SetActive(false);
        if (root != null)
            root.gameObject.SetActive(false);
    }

    void CacheLookFromExistingWalls()
    {
        // Keep re-trying until we have a brick material.
        bool needMats = sharedMaterials == null || sharedMaterials.Length == 0;
        if (copyingLook && !needMats && brickMaterial == null) return;

        var walls = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < walls.Length; i++)
        {
            var r = walls[i];
            var go = r != null ? r.gameObject : null;
            if (go == null) continue;
            if (go.name.StartsWith("KitchenWall") || go.name.StartsWith("KitchenWindow")) continue;

            bool isSceneWall = go.name == "Wall" || go.name.StartsWith("Wall (");
            bool isBrickShader = r.sharedMaterial != null && r.sharedMaterial.shader != null
                && r.sharedMaterial.shader.name.IndexOf("BrickWall", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isSceneWall && !isBrickShader) continue;

            if (go.name == "Wall")
                backWall = go.transform;
            else if (go.name.StartsWith("Wall ("))
                diningWall = go.transform;

            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                sharedMesh = filter.sharedMesh;

            if (r.sharedMaterials != null && r.sharedMaterials.Length > 0)
            {
                sharedMaterials = r.sharedMaterials;
                if (brickMaterial == null)
                    brickMaterial = r.sharedMaterial;
            }

            height = Mathf.Max(height, go.transform.localScale.y);
            float thick = Mathf.Min(go.transform.localScale.x, go.transform.localScale.z);
            if (thick > 0.15f && thick < 2.5f)
                thickness = thick;
        }

        if (brickMaterial != null)
            sharedMaterials = new[] { brickMaterial };

        copyingLook = backWall != null || diningWall != null || sharedMaterials != null;
        CacheEntranceDoor();
    }

    Material GetBrickMaterial()
    {
        if (brickMaterial != null) return brickMaterial;
        if (sharedMaterials != null && sharedMaterials.Length > 0 && sharedMaterials[0] != null)
            return sharedMaterials[0];
        return null;
    }

    void CacheEntranceDoor()
    {
        if (entranceDoor != null) return;
        var named = GameObject.Find("Door");
        if (named != null)
        {
            entranceDoor = named.transform;
            return;
        }

        var doors = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = float.MaxValue;
        Vector3 hint = diningWall != null ? diningWall.position : (backWall != null ? backWall.position : Vector3.zero);
        for (int i = 0; i < doors.Length; i++)
        {
            var t = doors[i];
            if (t == null || t.name != "Door") continue;
            float d = (t.position - hint).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        entranceDoor = best;
    }

    void RestoreOriginalWalls()
    {
        if (backWall != null && !backWall.gameObject.activeSelf)
            backWall.gameObject.SetActive(true);
        if (diningWall != null && !diningWall.gameObject.activeSelf)
            diningWall.gameObject.SetActive(true);
    }

    Transform WallParent()
    {
        if (grid != null && grid.floor != null && grid.floor.parent != null)
            return grid.floor.parent;
        return null;
    }

    void EnsurePieces()
    {
        Transform parent = WallParent();
        if (root == null)
        {
            var existing = GameObject.Find("KitchenExpandWalls");
            if (existing != null)
                root = existing.transform;
            else
                root = new GameObject("KitchenExpandWalls").transform;
            if (parent != null && root.parent != parent)
                root.SetParent(parent, true);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }

        if (windowRoot == null)
        {
            var existingWin = root.Find("KitchenWindows");
            windowRoot = existingWin != null ? existingWin : new GameObject("KitchenWindows").transform;
            windowRoot.SetParent(root, false);
            windowRoot.localPosition = Vector3.zero;
            windowRoot.localRotation = Quaternion.identity;
            windowRoot.localScale = Vector3.one;
        }

        west = EnsurePiece("Expand_West");
        south = EnsurePiece("Expand_South");
        northCap = EnsurePiece("Expand_NorthCap");
        eastCap = EnsurePiece("Expand_EastCap");
    }

    Transform EnsurePiece(string name)
    {
        Transform t = root.Find(name);
        if (t != null)
        {
            var extra = t.GetComponent<GridObstacle>();
            if (extra != null)
                Object.Destroy(extra);
            return t;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root, false);
        go.layer = 0;

        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && sharedMesh != null)
            filter.sharedMesh = sharedMesh;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && sharedMaterials != null && sharedMaterials.Length > 0)
            renderer.sharedMaterials = sharedMaterials;

        return go.transform;
    }

    void EnsureGlassMaterial()
    {
        Color tint = new Color(0.62f, 0.84f, 0.95f, windowGlassAlpha);
        if (glassMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            glassMaterial = new Material(shader) { name = "KitchenWindowGlass" };
        }

        if (glassMaterial.HasProperty("_Surface"))
            glassMaterial.SetFloat("_Surface", 1f);
        if (glassMaterial.HasProperty("_Blend"))
            glassMaterial.SetFloat("_Blend", 0f);
        if (glassMaterial.HasProperty("_Cull"))
            glassMaterial.SetFloat("_Cull", 0f);
        if (glassMaterial.HasProperty("_CullMode"))
            glassMaterial.SetFloat("_CullMode", 0f);
        if (glassMaterial.HasProperty("_Smoothness"))
            glassMaterial.SetFloat("_Smoothness", 0.95f);
        glassMaterial.SetOverrideTag("RenderType", "Transparent");
        glassMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        glassMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        glassMaterial.SetInt("_ZWrite", 0);
        glassMaterial.DisableKeyword("_ALPHATEST_ON");
        glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glassMaterial.EnableKeyword("_DOUBLE_SIDED");
        glassMaterial.renderQueue = (int)RenderQueue.Transparent;
        if (glassMaterial.HasProperty("_BaseColor"))
            glassMaterial.SetColor("_BaseColor", tint);
        if (glassMaterial.HasProperty("_Color"))
            glassMaterial.SetColor("_Color", tint);
        glassMaterial.doubleSidedGI = true;
    }

    void HideNamed(string name)
    {
        if (root != null)
            Hide(root.Find(name));

        var go = GameObject.Find(name);
        if (go != null)
            go.SetActive(false);
    }

    static void Hide(Transform wall)
    {
        if (wall != null)
            wall.gameObject.SetActive(false);
    }
}
