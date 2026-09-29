using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Build-mode editor and visual store for customer waiting tiles.</summary>
public class CustomerWaitAreaManager : MonoBehaviour
{
    public static CustomerWaitAreaManager Instance { get; private set; }
    public Color highlightColor = new Color(0.06f, 0.35f, 1f, 0.72f);
    public Color borderColor = new Color(0.01f, 0.03f, 0.12f, 0.95f);

    readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, GameObject> tiles = new Dictionary<Vector2Int, GameObject>();
    readonly Dictionary<int, Vector2Int> reservations = new Dictionary<int, Vector2Int>();
    bool editing;
    GridManager grid;
    Transform customerFloor;
    Mesh tileMesh;
    Material fillMaterial;
    Material borderMaterial;
    bool draggingArea;
    Vector2Int dragStart;

    public bool IsEditing => editing;
    public IReadOnlyCollection<Vector2Int> Cells => cells;

    public static CustomerWaitAreaManager Ensure()
    {
        if (Instance != null) return Instance;
        GameObject host = new GameObject("CustomerWaitAreaManager");
        return host.AddComponent<CustomerWaitAreaManager>();
    }

    void Awake()
    {
        Instance = this;
        grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        GameObject floor = GameObject.Find("CustomerFloor");
        customerFloor = floor != null ? floor.transform : null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetEditing(bool enabled) => editing = enabled;

    public void ClearAreas()
    {
        cells.Clear();
        reservations.Clear();
        foreach (GameObject tile in tiles.Values)
            if (tile != null) Destroy(tile);
        tiles.Clear();
    }

    public bool TryGetNearestWaitPosition(Vector3 from, out Vector3 position)
    {
        position = default;
        if (cells.Count == 0) return false;
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        float cell = grid != null ? Mathf.Max(0.01f, grid.cellSize) : 1f;
        Vector3 origin = grid != null ? grid.Origin : Vector3.zero;
        float bestDistance = float.MaxValue;
        foreach (Vector2Int key in cells)
        {
            Vector3 candidate = new Vector3(origin.x + (key.x + .5f) * cell, origin.y, origin.z + (key.y + .5f) * cell);
            float distance = (candidate - from).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            position = candidate;
        }
        return true;
    }

    public bool TryReserveNearestWaitPosition(CustomerAI customer, Vector3 from, out Vector3 position)
    {
        position = default;
        if (customer == null || cells.Count == 0) return false;
        int id = customer.GetInstanceID();
        if (reservations.ContainsKey(id)) Release(customer);
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        float cell = grid != null ? Mathf.Max(0.01f, grid.cellSize) : 1f;
        Vector3 origin = grid != null ? grid.Origin : Vector3.zero;
        float bestDistance = float.MaxValue;
        Vector2Int best = default;
        bool found = false;
        foreach (Vector2Int key in cells)
        {
            if (reservations.ContainsValue(key)) continue;
            Vector3 candidate = new Vector3(origin.x + (key.x + .5f) * cell, origin.y, origin.z + (key.y + .5f) * cell);
            float distance = (candidate - from).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = key;
            position = candidate;
            found = true;
        }
        if (found) reservations[id] = best;
        return found;
    }

    public void Release(CustomerAI customer)
    {
        if (customer != null) reservations.Remove(customer.GetInstanceID());
    }

    void Update()
    {
        if (!editing)
        {
            draggingArea = false;
            return;
        }

        if (Input.GetMouseButtonDown(0)
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            && TryGetPointerCell(out Vector2Int start, out _, out _, out _))
        {
            dragStart = start;
            draggingArea = true;
        }

        if (!draggingArea || !Input.GetMouseButtonUp(0)) return;
        draggingArea = false;
        if (!TryGetPointerCell(out Vector2Int end, out float cell, out Vector3 origin, out float floorY)) return;

        if (end == dragStart)
        {
            if (!cells.Add(end))
            {
                cells.Remove(end);
                if (tiles.TryGetValue(end, out GameObject old) && old != null) Destroy(old);
                tiles.Remove(end);
                return;
            }
            tiles[end] = CreateTile(end, cell, origin, floorY);
            return;
        }

        int minX = Mathf.Min(dragStart.x, end.x), maxX = Mathf.Max(dragStart.x, end.x);
        int minY = Mathf.Min(dragStart.y, end.y), maxY = Mathf.Max(dragStart.y, end.y);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int key = new Vector2Int(x, y);
                if (cells.Add(key))
                    tiles[key] = CreateTile(key, cell, origin, floorY);
            }
    }

    bool TryGetPointerCell(out Vector2Int key, out float cell, out Vector3 origin, out float floorY)
    {
        key = default;
        cell = 1f;
        origin = Vector3.zero;
        floorY = 0f;
        if (Camera.main == null || !TryGetCustomerFloor(out Renderer floorRenderer)) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, -1) || !hit.collider.transform.IsChildOf(customerFloor)) return false;

        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        cell = grid != null ? Mathf.Max(0.01f, grid.cellSize) : 1f;
        origin = grid != null ? grid.Origin : Vector3.zero;
        floorY = floorRenderer.bounds.max.y;
        key = new Vector2Int(
            Mathf.FloorToInt((hit.point.x - origin.x) / cell),
            Mathf.FloorToInt((hit.point.z - origin.z) / cell));
        return true;
    }

    bool TryGetCustomerFloor(out Renderer renderer)
    {
        renderer = null;
        if (customerFloor == null)
        {
            GameObject floor = GameObject.Find("CustomerFloor");
            customerFloor = floor != null ? floor.transform : null;
        }
        if (customerFloor == null) return false;
        renderer = customerFloor.GetComponentInChildren<Renderer>();
        return renderer != null;
    }

    GameObject CreateTile(Vector2Int key, float cell, Vector3 origin, float y)
    {
        EnsureResources();
        GameObject root = new GameObject("CustomerWaitTile_" + key.x + "_" + key.y);
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(origin.x + (key.x + 0.5f) * cell, y + 0.035f, origin.z + (key.y + 0.5f) * cell);
        root.transform.localScale = new Vector3(cell * 0.9f, 1f, cell * 0.9f);
        AddLayer(root.transform, borderMaterial, 1f, 0f);
        AddLayer(root.transform, fillMaterial, 0.76f, 0.004f);
        return root;
    }

    void AddLayer(Transform parent, Material material, float scale, float y)
    {
        GameObject layer = new GameObject("TileLayer");
        layer.transform.SetParent(parent, false);
        layer.transform.localPosition = Vector3.up * y;
        layer.transform.localScale = new Vector3(scale, 1f, scale);
        layer.AddComponent<MeshFilter>().sharedMesh = tileMesh;
        MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void EnsureResources()
    {
        if (tileMesh == null)
        {
            tileMesh = new Mesh { name = "CustomerWaitAreaTile" };
            tileMesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f) };
            tileMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            tileMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        }
        Shader shader = Shader.Find("Sprites/Default");
        if (fillMaterial == null) { fillMaterial = new Material(shader); fillMaterial.color = highlightColor; }
        if (borderMaterial == null) { borderMaterial = new Material(shader); borderMaterial.color = borderColor; }
    }
}
