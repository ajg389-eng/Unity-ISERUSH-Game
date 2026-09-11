using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    public Transform floor;
    public float cellSize = 1f;
    public bool drawGizmos = true;

    [Header("Expansion")]
    [Tooltip("Native size of a Unity Plane mesh (default Plane is 10x10). Used when scaling the floor.")]
    public float floorMeshWorldSize = 10f;
    [Tooltip("Maximum grid width (cells) the player can expand to.")]
    public int maxWidth = 30;
    [Tooltip("Maximum grid height / depth (cells) the player can expand to.")]
    public int maxHeight = 30;

    [Header("Floor texture")]
    [Tooltip("When on, material tiling is set to Width/Height so the pattern stays grid-sized and does not grow when the floor expands. Requires a Repeat-wrapped texture (e.g. URP Lit), not a fixed-frequency Shader Graph checker.")]
    public bool syncFloorTextureToGrid = true;
    [Tooltip("How many texture repeats per grid cell (1 = one tile per cell).")]
    public float textureTilesPerCell = 1f;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Node[,] Nodes { get; private set; }
    public Vector3 Origin { get; private set; }

    public bool HasBaseline { get; private set; }
    public Vector3 BaselineOrigin { get; private set; }
    public int BaselineWidth { get; private set; }
    public int BaselineHeight { get; private set; }

    /// <summary>Fired after Width/Height/Origin/Nodes change (e.g. expand).</summary>
    public event System.Action GridChanged;

    void Awake()
    {
        Instance = this;
        if (GetComponent<KitchenPerimeterWalls>() == null)
            gameObject.AddComponent<KitchenPerimeterWalls>();
        RebuildFromFloor();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [ContextMenu("Rebuild Grid From Floor")]
    public void RebuildFromFloorMenu() => RebuildFromFloor();

    public void SetGridVisible(bool visible)
    {
        drawGizmos = visible;
    }

    public void RebuildFromFloor()
    {
        if (!floor) { Debug.LogError("Assign floor Transform."); return; }

        var r = floor.GetComponentInChildren<Renderer>();
        Bounds b;
        if (r != null) b = r.bounds;
        else
        {
            Vector3 size = new Vector3(floor.localScale.x * floorMeshWorldSize, 0f, floor.localScale.z * floorMeshWorldSize);
            b = new Bounds(floor.position, size);
        }

        Width = Mathf.Max(1, Mathf.FloorToInt(b.size.x / cellSize));
        Height = Mathf.Max(1, Mathf.FloorToInt(b.size.z / cellSize));

        Origin = new Vector3(b.min.x, floor.position.y, b.min.z);

        Nodes = new Node[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                Vector3 world = CellToWorld(x, y);
                Nodes[x, y] = new Node(x, y, world);
            }

        ResyncOccupancyFromScene();
        SyncFloorTextureTiling();
        RememberBaseline();
        RefreshExpandWalls();
    }

    void RememberBaseline()
    {
        if (HasBaseline || Width <= 0 || Height <= 0) return;
        BaselineOrigin = Origin;
        BaselineWidth = Width;
        BaselineHeight = Height;
        HasBaseline = true;
    }

    void RefreshExpandWalls()
    {
        var walls = GetComponent<KitchenPerimeterWalls>();
        if (walls == null)
            walls = gameObject.AddComponent<KitchenPerimeterWalls>();

        walls.generateAtRuntime = true;
        walls.buildWorkNorth = true;
        walls.buildWorkWest = true;
        walls.buildWorkSouth = true;
        walls.buildWorkEastProtrusions = true;
        walls.buildCustomerEast = true;
        walls.buildCustomerSouth = true;
        if (walls.customerFloor == null)
        {
            var cf = GameObject.Find("CustomerFloor");
            if (cf != null) walls.customerFloor = cf.transform;
        }
        walls.FitToGrid();
        ResyncOccupancyFromScene();
    }

    /// <summary>
    /// Grow the placeable grid by the given cell counts along -X and -Z
    /// (opposite of the floor's +X/+Z extent). Existing placements stay put;
    /// Origin and occupancy indices shift to match.
    /// </summary>
    public bool TryExpand(int addWidth, int addHeight)
    {
        addWidth = Mathf.Max(0, addWidth);
        addHeight = Mathf.Max(0, addHeight);
        if (addWidth == 0 && addHeight == 0) return false;
        if (Nodes == null || Width <= 0 || Height <= 0)
            RebuildFromFloor();
        if (Nodes == null) return false;

        int newW = Width + addWidth;
        int newH = Height + addHeight;
        if (newW > maxWidth || newH > maxHeight)
            return false;

        // Grow toward -X / -Z: Origin moves, old cells shift up in index space
        Vector3 newOrigin = Origin + new Vector3(-addWidth * cellSize, 0f, -addHeight * cellSize);
        ResizeFloorToCells(newW, newH, newOrigin);

        Width = newW;
        Height = newH;
        Origin = newOrigin;

        Nodes = new Node[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                Nodes[x, y] = new Node(x, y, CellToWorld(x, y));

        ResyncOccupancyFromScene();
        RefreshExpandWalls();
        return true;
    }

    /// <summary>True if expanding by these amounts would stay under max size.</summary>
    public bool CanExpand(int addWidth, int addHeight)
    {
        addWidth = Mathf.Max(0, addWidth);
        addHeight = Mathf.Max(0, addHeight);
        if (addWidth == 0 && addHeight == 0) return false;
        return Width + addWidth <= maxWidth && Height + addHeight <= maxHeight;
    }

    /// <summary>
    /// Reverse of TryExpand: remove the strip that was added along -X / -Z.
    /// Fails if that strip is occupied or the grid would shrink below 1x1.
    /// </summary>
    public bool TryShrink(int removeWidth, int removeHeight)
    {
        if (!CanShrink(removeWidth, removeHeight))
            return false;

        int newW = Width - removeWidth;
        int newH = Height - removeHeight;
        Vector3 newOrigin = Origin + new Vector3(removeWidth * cellSize, 0f, removeHeight * cellSize);
        ResizeFloorToCells(newW, newH, newOrigin);

        Width = newW;
        Height = newH;
        Origin = newOrigin;

        Nodes = new Node[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                Nodes[x, y] = new Node(x, y, CellToWorld(x, y));

        ResyncOccupancyFromScene();
        RefreshExpandWalls();
        return true;
    }

    /// <summary>
    /// True if the newest expansion strip has no stations and shrinking would not go
    /// below the original kitchen. Perimeter expand-walls do not block undo.
    /// </summary>
    public bool CanShrink(int removeWidth, int removeHeight)
    {
        removeWidth = Mathf.Max(0, removeWidth);
        removeHeight = Mathf.Max(0, removeHeight);
        if (removeWidth == 0 && removeHeight == 0) return false;
        if (Nodes == null || Width <= 0 || Height <= 0) return false;
        if (Width - removeWidth < 1 || Height - removeHeight < 1) return false;
        if (HasBaseline && (Width - removeWidth < BaselineWidth || Height - removeHeight < BaselineHeight))
            return false;
        return !StripHasPlacedStation(removeWidth, removeHeight);
    }

    bool StripHasPlacedStation(int removeWidth, int removeHeight)
    {
        var footprints = FindObjectsByType<BuildFootprint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var fp in footprints)
        {
            if (fp == null || ShouldIgnoreOccupancyObject(fp.gameObject)) continue;
            if (fp.GetComponentInParent<KitchenPerimeterWalls>() != null) continue;
            if (FootprintTouchesStrip(fp.gameObject, fp, removeWidth, removeHeight))
                return true;
        }

        var stations = FindObjectsByType<StationNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var station in stations)
        {
            if (station == null || ShouldIgnoreOccupancyObject(station.gameObject)) continue;
            if (station.GetComponent<BuildFootprint>() != null) continue;
            if (FootprintTouchesStrip(station.gameObject, null, removeWidth, removeHeight))
                return true;
        }

        return false;
    }

    bool FootprintTouchesStrip(GameObject go, BuildFootprint fp, int removeWidth, int removeHeight)
    {
        int sizeX = Mathf.Max(1, fp != null ? fp.sizeX : 1);
        int sizeY = Mathf.Max(1, fp != null ? fp.sizeY : 1);
        int yaw = Mathf.RoundToInt(go.transform.eulerAngles.y / 90f) & 3;
        if (yaw == 1 || yaw == 3)
        {
            int t = sizeX;
            sizeX = sizeY;
            sizeY = t;
        }

        if (TryGetFootprintOriginFromCenter(go.transform.position, sizeX, sizeY, out int ox, out int oy))
        {
            for (int x = ox; x < ox + sizeX; x++)
                for (int y = oy; y < oy + sizeY; y++)
                    if (x < removeWidth || y < removeHeight)
                        return true;
            return false;
        }

        var cols = go.GetComponentsInChildren<Collider>();
        if (cols == null || cols.Length == 0) return false;
        foreach (var col in cols)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            WorldToCell(new Vector3(col.bounds.min.x + 0.01f, col.bounds.center.y, col.bounds.min.z + 0.01f), out int minX, out int minY);
            WorldToCell(new Vector3(col.bounds.max.x - 0.01f, col.bounds.center.y, col.bounds.max.z - 0.01f), out int maxX, out int maxY);
            minX = Mathf.Clamp(minX, 0, Width - 1);
            maxX = Mathf.Clamp(maxX, 0, Width - 1);
            minY = Mathf.Clamp(minY, 0, Height - 1);
            maxY = Mathf.Clamp(maxY, 0, Height - 1);
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    if (x < removeWidth || y < removeHeight)
                        return true;
        }
        return false;
    }

    void ResizeFloorToCells(int cellsW, int cellsH, Vector3 origin)
    {
        if (floor == null) return;

        float worldW = cellsW * cellSize;
        float worldH = cellsH * cellSize;
        float mesh = Mathf.Max(0.01f, floorMeshWorldSize);

        Vector3 scale = floor.localScale;
        floor.localScale = new Vector3(worldW / mesh, scale.y, worldH / mesh);

        // Keep the max-corner fixed in world space; grow toward -X / -Z via new origin
        floor.position = new Vector3(
            origin.x + worldW * 0.5f,
            floor.position.y,
            origin.z + worldH * 0.5f);

        SyncFloorTextureTiling();
    }

    /// <summary>
    /// Sets material tiling to match grid cell counts so the floor pattern keeps a fixed
    /// world size when the plane is scaled (expand/shrink).
    /// No-ops for shaders without a tiled texture (e.g. CheckeredFloor Shader Graph).
    /// </summary>
    public void SyncFloorTextureTiling()
    {
        if (!syncFloorTextureToGrid || floor == null) return;
        if (Width <= 0 || Height <= 0) return;

        var renderer = floor.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        float tilesX = Width * Mathf.Max(0.01f, textureTilesPerCell);
        float tilesY = Height * Mathf.Max(0.01f, textureTilesPerCell);
        var scale = new Vector2(tilesX, tilesY);

        // Prefer a per-renderer instance so we don't mutate the shared project material permanently.
        var mat = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
        if (mat == null) return;

        // Only touch properties that exist — CheckeredFloor has none of these.
        if (mat.HasProperty("_BaseMap"))
            mat.SetTextureScale("_BaseMap", scale);
        if (mat.HasProperty("_MainTex"))
            mat.SetTextureScale("_MainTex", scale);
        if (mat.HasProperty("_BaseColorMap"))
            mat.SetTextureScale("_BaseColorMap", scale);

        if (mat.HasProperty("_BaseMap_ST"))
        {
            Vector4 st = mat.GetVector("_BaseMap_ST");
            st.x = scale.x;
            st.y = scale.y;
            mat.SetVector("_BaseMap_ST", st);
        }
        if (mat.HasProperty("_MainTex_ST"))
        {
            Vector4 st = mat.GetVector("_MainTex_ST");
            st.x = scale.x;
            st.y = scale.y;
            mat.SetVector("_MainTex_ST", st);
        }
    }

    public Vector3 CellToWorld(int x, int y)
        => Origin + new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);

    /// <summary>Snap any world position to the center of its grid cell (Y kept from Origin / floor).</summary>
    public Vector3 GetCellCenter(Vector3 world)
    {
        if (Nodes == null || Width == 0 || Height == 0)
            return world;
        WorldToCell(world, out int x, out int y);
        x = Mathf.Clamp(x, 0, Width - 1);
        y = Mathf.Clamp(y, 0, Height - 1);
        Vector3 c = CellToWorld(x, y);
        c.y = Origin.y;
        return c;
    }

    /// <summary>True if world XZ is within tolerance of the cell center.</summary>
    public bool IsAtCellCenter(Vector3 world, float tolerance = 0.05f)
    {
        Vector3 c = GetCellCenter(world);
        float dx = world.x - c.x;
        float dz = world.z - c.z;
        return dx * dx + dz * dz <= tolerance * tolerance;
    }

    /// <summary>World position for the center of a multi-cell footprint (so 2x1 objects sit centered over both tiles).</summary>
    public Vector3 GetFootprintCenter(int startX, int startY, int sizeX, int sizeY)
    {
        float cx = startX + sizeX * 0.5f;
        float cy = startY + sizeY * 0.5f;
        return Origin + new Vector3(cx * cellSize, 0f, cy * cellSize);
    }

    public bool WorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - Origin.x) / cellSize);
        y = Mathf.FloorToInt((world.z - Origin.z) / cellSize);
        return x >= 0 && y >= 0 && x < Width && y < Height;
    }

    public bool CanPlace(int startX, int startY, int sizeX, int sizeY)
    {
        for (int x = startX; x < startX + sizeX; x++)
            for (int y = startY; y < startY + sizeY; y++)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
                if (Nodes[x, y].occupied) return false;
            }
        return true;
    }

    bool suppressGridChanged;

    public void SetOccupied(int startX, int startY, int sizeX, int sizeY, bool occ)
    {
        for (int x = startX; x < startX + sizeX; x++)
            for (int y = startY; y < startY + sizeY; y++)
                Nodes[x, y].occupied = occ;

        if (!suppressGridChanged)
            GridChanged?.Invoke();
    }

    /// <summary>True if the cell is in bounds, walkable, and not occupied by a station/wall.</summary>
    public bool IsWalkable(int x, int y)
    {
        if (Nodes == null || x < 0 || y < 0 || x >= Width || y >= Height) return false;
        var n = Nodes[x, y];
        return n != null && n.walkable && !n.occupied;
    }

    public void ClearOccupancy()
    {
        if (Nodes == null) return;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                if (Nodes[x, y] == null) continue;
                Nodes[x, y].occupied = false;
            }
    }

    /// <summary>
    /// Rebuild occupied cells from placed stations, footprints, and wall/obstacle colliders in the scene.
    /// </summary>
    public void ResyncOccupancyFromScene()
    {
        if (Nodes == null || Width <= 0 || Height <= 0) return;

        suppressGridChanged = true;
        try
        {
            ClearOccupancy();

            // Placed / footprint objects (stations, equipment).
            var footprints = FindObjectsByType<BuildFootprint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var fp in footprints)
            {
                if (fp == null || ShouldIgnoreOccupancyObject(fp.gameObject)) continue;
                MarkObjectFootprint(fp.gameObject, fp);
            }

            // Stations without a footprint still block their collider footprint.
            var stations = FindObjectsByType<StationNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var station in stations)
            {
                if (station == null || ShouldIgnoreOccupancyObject(station.gameObject)) continue;
                if (station.GetComponent<BuildFootprint>() != null) continue;
                MarkColliderOccupancy(station.gameObject);
            }

            // Walls and explicit grid obstacles (by component or name).
            var obstacles = FindObjectsByType<GridObstacle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var obstacle in obstacles)
            {
                if (obstacle == null || ShouldIgnoreOccupancyObject(obstacle.gameObject)) continue;
                MarkColliderOccupancy(obstacle.gameObject);
            }

            foreach (var col in FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (col == null || !col.enabled) continue;
                if (col.isTrigger) continue;
                if (ShouldIgnoreOccupancyObject(col.gameObject)) continue;
                if (!IsWallLike(col.gameObject)) continue;
                if (col.GetComponentInParent<GridObstacle>() != null) continue;
                if (col.GetComponentInParent<BuildFootprint>() != null) continue;
                if (col.GetComponentInParent<StationNode>() != null) continue;
                MarkBoundsOccupancy(col.bounds);
            }

            // Keep green stand tiles walkable so workers can path onto them.
            ClearInteractionStandOccupancy();
        }
        finally
        {
            suppressGridChanged = false;
            GridChanged?.Invoke();
        }
    }

    void ClearInteractionStandOccupancy()
    {
        var tiles = FindObjectsByType<StationInteractionTiles>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var tile in tiles)
        {
            if (tile == null || ShouldIgnoreOccupancyObject(tile.gameObject)) continue;
            var centers = tile.GetInteractionStandCenters();
            if (centers == null) continue;
            for (int i = 0; i < centers.Count; i++)
            {
                if (!WorldToCell(centers[i], out int x, out int y)) continue;
                if (Nodes[x, y] != null)
                    Nodes[x, y].occupied = false;
            }
        }
    }

    public void MarkObjectFootprint(GameObject go, BuildFootprint fp)
    {
        if (go == null || Nodes == null) return;
        int sizeX = Mathf.Max(1, fp != null ? fp.sizeX : 1);
        int sizeY = Mathf.Max(1, fp != null ? fp.sizeY : 1);

        int yaw = Mathf.RoundToInt(go.transform.eulerAngles.y / 90f) & 3;
        if (yaw == 1 || yaw == 3)
        {
            int t = sizeX;
            sizeX = sizeY;
            sizeY = t;
        }

        if (!TryGetFootprintOriginFromCenter(go.transform.position, sizeX, sizeY, out int ox, out int oy))
        {
            MarkColliderOccupancy(go);
            return;
        }

        SetOccupied(ox, oy, sizeX, sizeY, true);
    }

    public bool TryGetFootprintOriginFromCenter(Vector3 worldCenter, int sizeX, int sizeY, out int originX, out int originY)
    {
        originX = 0;
        originY = 0;
        if (Nodes == null || Width <= 0 || Height <= 0) return false;

        float cx = (worldCenter.x - Origin.x) / cellSize;
        float cy = (worldCenter.z - Origin.z) / cellSize;
        originX = Mathf.RoundToInt(cx - sizeX * 0.5f);
        originY = Mathf.RoundToInt(cy - sizeY * 0.5f);
        return originX >= 0 && originY >= 0 && originX + sizeX <= Width && originY + sizeY <= Height;
    }

    public void MarkColliderOccupancy(GameObject go)
    {
        if (go == null) return;
        var cols = go.GetComponentsInChildren<Collider>();
        if (cols == null || cols.Length == 0)
        {
            MarkBoundsOccupancy(new Bounds(go.transform.position, Vector3.one * cellSize));
            return;
        }

        foreach (var col in cols)
        {
            if (col == null || !col.enabled || col.isTrigger) continue;
            MarkBoundsOccupancy(col.bounds);
        }
    }

    public void MarkBoundsOccupancy(Bounds bounds)
    {
        if (Nodes == null) return;

        WorldToCell(new Vector3(bounds.min.x + 0.01f, bounds.center.y, bounds.min.z + 0.01f), out int minX, out int minY);
        WorldToCell(new Vector3(bounds.max.x - 0.01f, bounds.center.y, bounds.max.z - 0.01f), out int maxX, out int maxY);

        if (maxX < 0 || maxY < 0 || minX >= Width || minY >= Height)
            return;

        minX = Mathf.Max(0, minX);
        maxX = Mathf.Min(Width - 1, maxX);
        minY = Mathf.Max(0, minY);
        maxY = Mathf.Min(Height - 1, maxY);

        float pad = cellSize * 0.25f;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (Nodes[x, y] == null) continue;
                float x0 = Origin.x + x * cellSize;
                float z0 = Origin.z + y * cellSize;
                float overlapX = Mathf.Min(bounds.max.x, x0 + cellSize) - Mathf.Max(bounds.min.x, x0);
                float overlapZ = Mathf.Min(bounds.max.z, z0 + cellSize) - Mathf.Max(bounds.min.z, z0);
                if (overlapX > pad && overlapZ > pad)
                    Nodes[x, y].occupied = true;
            }
        }
    }

    static bool IsWallLike(GameObject go)
    {
        if (go == null) return false;
        string n = go.name;
        return n.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase)
               || n.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool ShouldIgnoreOccupancyObject(GameObject go)
    {
        if (go == null) return true;
        if (!go.activeInHierarchy) return true;
        string n = go.name;
        if (n.IndexOf("Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Preview", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (go.GetComponentInParent<KitchenEmployee>() != null) return true;
        if (go.GetComponentInParent<CustomerAI>() != null) return true;
        if (n.StartsWith("Expand_", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (go.transform.parent != null && go.transform.parent.name == "KitchenExpandWalls") return true;
        return false;
    }

    /// <summary>Returns the nearest walkable cell to (cx, cy), including (cx,cy) or a neighbor.</summary>
    public bool FindNearestWalkable(int cx, int cy, out int outX, out int outY)
    {
        outX = cx;
        outY = cy;
        if (Nodes == null || Width == 0 || Height == 0) return false;
        int px = Mathf.Clamp(cx, 0, Width - 1);
        int py = Mathf.Clamp(cy, 0, Height - 1);
        if (IsWalkable(px, py)) { outX = px; outY = py; return true; }
        for (int r = 1; r <= Mathf.Max(Width, Height); r++)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx != 0 && dy != 0) continue;
                    int nx = px + dx, ny = py + dy;
                    if (nx >= 0 && nx < Width && ny >= 0 && ny < Height && IsWalkable(nx, ny))
                    {
                        outX = nx; outY = ny;
                        return true;
                    }
                }
        }
        return false;
    }

    /// <summary>Path from current world position to target world position using only walkable cells. Returns cell-center world positions.</summary>
    public List<Vector3> GetPath(Vector3 fromWorld, Vector3 toWorld)
    {
        var result = new List<Vector3>();
        if (Nodes == null || Width == 0 || Height == 0) return result;

        WorldToCell(fromWorld, out int sx, out int sy);
        WorldToCell(toWorld, out int tx, out int ty);
        sx = Mathf.Clamp(sx, 0, Width - 1);
        sy = Mathf.Clamp(sy, 0, Height - 1);
        if (!FindNearestWalkable(tx, ty, out int gx, out int gy)) return result;
        if (!IsWalkable(sx, sy) && !FindNearestWalkable(sx, sy, out sx, out sy)) return result;

        // Already on the goal cell — still return that cell center so the worker can snap to it
        if (sx == gx && sy == gy)
        {
            result.Add(CellToWorld(gx, gy));
            return result;
        }

        var open = new List<(int x, int y, float g, float f)>();
        var closed = new HashSet<(int, int)>();
        var parent = new Dictionary<(int, int), (int, int)>();
        open.Add((sx, sy, 0f, Heuristic(sx, sy, gx, gy)));

        while (open.Count > 0)
        {
            open.Sort((a, b) => a.f.CompareTo(b.f));
            var cur = open[0];
            open.RemoveAt(0);
            if (closed.Contains((cur.x, cur.y))) continue;
            closed.Add((cur.x, cur.y));

            if (cur.x == gx && cur.y == gy)
            {
                var path = new List<(int, int)>();
                var p = (cur.x, cur.y);
                while (parent.TryGetValue(p, out var prev))
                {
                    path.Add(p);
                    p = prev;
                }
                path.Add((sx, sy));
                path.Reverse();
                foreach (var c in path)
                    result.Add(CellToWorld(c.Item1, c.Item2));
                return result;
            }

            foreach (var (nx, ny) in Neighbors(cur.x, cur.y))
            {
                if (!IsWalkable(nx, ny) || closed.Contains((nx, ny))) continue;
                float g = cur.g + 1f;
                float f = g + Heuristic(nx, ny, gx, gy);
                open.Add((nx, ny, g, f));
                parent[(nx, ny)] = (cur.x, cur.y);
            }
        }
        return result;
    }

    static float Heuristic(int x, int y, int gx, int gy) => Mathf.Abs(x - gx) + Mathf.Abs(y - gy);

    IEnumerable<(int x, int y)> Neighbors(int x, int y)
    {
        if (x > 0) yield return (x - 1, y);
        if (x < Width - 1) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y < Height - 1) yield return (x, y + 1);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || Nodes == null) return;

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                var n = Nodes[x, y];
                bool blocked = n == null || !n.walkable || n.occupied;
                Gizmos.color = blocked ? new Color(1f, 0.2f, 0.2f, 0.5f) : new Color(0.7f, 0.7f, 0.7f, 0.35f);
                Gizmos.DrawCube(n.world + Vector3.up * 0.02f, new Vector3(cellSize * 0.95f, 0.02f, cellSize * 0.95f));
            }
    }
}

public class Node
{
    public int x, y;
    public Vector3 world;
    public bool walkable = true;
    public bool occupied = false;

    public Node(int x, int y, Vector3 world)
    {
        this.x = x;
        this.y = y;
        this.world = world;
    }
}
