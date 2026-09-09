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

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Node[,] Nodes { get; private set; }
    public Vector3 Origin { get; private set; }

    /// <summary>Fired after Width/Height/Origin/Nodes change (e.g. expand).</summary>
    public event System.Action GridChanged;

    void Awake()
    {
        Instance = this;
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

        GridChanged?.Invoke();
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

        // Preserve occupancy before reallocating
        int oldW = Width;
        int oldH = Height;
        bool[,] occupied = new bool[oldW, oldH];
        for (int x = 0; x < oldW; x++)
            for (int y = 0; y < oldH; y++)
                occupied[x, y] = Nodes[x, y] != null && Nodes[x, y].occupied;

        // Grow toward -X / -Z: Origin moves, old cells shift up in index space
        Vector3 newOrigin = Origin + new Vector3(-addWidth * cellSize, 0f, -addHeight * cellSize);
        ResizeFloorToCells(newW, newH, newOrigin);

        Width = newW;
        Height = newH;
        Origin = newOrigin;

        Nodes = new Node[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                Nodes[x, y] = new Node(x, y, CellToWorld(x, y));
                int ox = x - addWidth;
                int oy = y - addHeight;
                if (ox >= 0 && oy >= 0 && ox < oldW && oy < oldH)
                    Nodes[x, y].occupied = occupied[ox, oy];
            }

        GridChanged?.Invoke();
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

        int oldW = Width;
        int oldH = Height;
        bool[,] occupied = new bool[oldW, oldH];
        for (int x = 0; x < oldW; x++)
            for (int y = 0; y < oldH; y++)
                occupied[x, y] = Nodes[x, y] != null && Nodes[x, y].occupied;

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
            {
                Nodes[x, y] = new Node(x, y, CellToWorld(x, y));
                int ox = x + removeWidth;
                int oy = y + removeHeight;
                if (ox < oldW && oy < oldH)
                    Nodes[x, y].occupied = occupied[ox, oy];
            }

        GridChanged?.Invoke();
        return true;
    }

    /// <summary>True if the newest expansion strip is empty and shrinking would leave at least a 1x1 grid.</summary>
    public bool CanShrink(int removeWidth, int removeHeight)
    {
        removeWidth = Mathf.Max(0, removeWidth);
        removeHeight = Mathf.Max(0, removeHeight);
        if (removeWidth == 0 && removeHeight == 0) return false;
        if (Nodes == null || Width <= 0 || Height <= 0) return false;
        if (Width - removeWidth < 1 || Height - removeHeight < 1) return false;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (x >= removeWidth && y >= removeHeight) continue;
                if (Nodes[x, y] != null && Nodes[x, y].occupied)
                    return false;
            }
        }
        return true;
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

    public void SetOccupied(int startX, int startY, int sizeX, int sizeY, bool occ)
    {
        for (int x = startX; x < startX + sizeX; x++)
            for (int y = startY; y < startY + sizeY; y++)
                Nodes[x, y].occupied = occ;
    }

    /// <summary>True if the cell is in bounds and not occupied (employee can walk here).</summary>
    public bool IsWalkable(int x, int y)
    {
        if (Nodes == null || x < 0 || y < 0 || x >= Width || y >= Height) return false;
        return !Nodes[x, y].occupied;
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
                Gizmos.color = n.walkable ? new Color(0.7f, 0.7f, 0.7f, 0.35f) : new Color(1f, 0.2f, 0.2f, 0.5f);
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
