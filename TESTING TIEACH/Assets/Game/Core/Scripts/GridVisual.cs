using UnityEngine;
using System.Collections.Generic;

public class GridVisual : MonoBehaviour
{
    public GameModeManager modeManager;
    public GridManager grid;

    [Header("Line settings")]
    public Material lineMaterial;     // optional (can be empty)
    public float lineWidth = 0.03f;
    public float yOffset = 0.02f;

    [Header("Customer floor grid (visual only)")]
    [Tooltip("Dining/customer floor used to draw a non-placeable grid. Auto-finds CustomerFloor when empty.")]
    public Transform customerFloor;
    public bool showCustomerGrid = true;
    public Color customerGridColor = new Color(1f, 0.55f, 0.12f, 0.9f);

    [Header("Customer queue preview")]
    public Color queueCellColor = new Color(0.05f, 0.82f, 1f, 0.72f);
    public Color queueCellBorderColor = new Color(0.015f, 0.04f, 0.07f, 0.92f);
    [Range(0.5f, 0.98f)] public float queueCellFill = 0.86f;

    private LineRenderer[] lines;
    readonly List<GameObject> queueCells = new List<GameObject>();
    int activeQueueCellCount;
    Mesh queueCellMesh;
    Material queueCellMaterial;
    Material queueCellBorderMaterial;
    float nextQueueRefresh;

    void Start()
    {
        if (!grid) grid = GetComponent<GridManager>() ?? FindObjectOfType<GridManager>();
        FindCustomerFloor();
        if (grid != null)
            grid.GridChanged += OnGridChanged;

        RebuildVisual();
        UpdateVisibility();
    }

    void OnDestroy()
    {
        if (grid != null)
            grid.GridChanged -= OnGridChanged;
    }

    void OnGridChanged()
    {
        RebuildVisual();
    }

    void Update()
    {
        UpdateVisibility();
        if (modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Build
            && Time.unscaledTime >= nextQueueRefresh)
        {
            nextQueueRefresh = Time.unscaledTime + 0.15f;
            RefreshQueueCells();
        }
    }

    void UpdateVisibility()
    {
        bool show = (modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Build);
        if (lines == null) return;
        foreach (var lr in lines) if (lr) lr.enabled = show;
        for (int i = 0; i < queueCells.Count; i++)
            if (queueCells[i] != null)
                queueCells[i].SetActive(show && i < activeQueueCellCount);
    }

    [ContextMenu("Rebuild Grid Visual")]
    public void RebuildVisual()
    {
        if (!grid) grid = GetComponent<GridManager>();
        if (!grid) { Debug.LogError("GridVisual needs a GridManager reference."); return; }

        // Clear old
        if (lines != null)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
            }
            queueCells.Clear();
        }

        int w = grid.Width;
        int h = grid.Height;
        float cs = grid.cellSize;

        var rebuiltLines = new List<LineRenderer>((w + 1) + (h + 1));

        // Vertical lines (along Z)
        for (int x = 0; x <= w; x++)
        {
            Vector3 start = grid.Origin + new Vector3(x * cs, 0f, 0f);
            Vector3 end = grid.Origin + new Vector3(x * cs, 0f, h * cs);
            rebuiltLines.Add(CreateLine("Work_V_" + x, start, end, false));
        }

        // Horizontal lines (along X)
        for (int y = 0; y <= h; y++)
        {
            Vector3 start = grid.Origin + new Vector3(0f, 0f, y * cs);
            Vector3 end = grid.Origin + new Vector3(w * cs, 0f, y * cs);
            rebuiltLines.Add(CreateLine("Work_H_" + y, start, end, false));
        }

        BuildCustomerGrid(rebuiltLines, cs);
        lines = rebuiltLines.ToArray();

        UpdateVisibility();
    }

    void RefreshQueueCells()
    {
        if (!TryGetCustomerGridBounds(out Bounds bounds, out float originX, out float originZ))
        {
            SetQueueCellCount(0);
            return;
        }

        float cs = Mathf.Max(0.01f, grid.cellSize);
        var occupiedCells = new HashSet<Vector2Int>();
        var positions = new List<Vector3>();

        Register[] registers = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Register register in registers)
        {
            if (register == null) continue;
            for (int i = 0; i < register.QueuePreviewCount; i++)
                AddQueueCell(register.GetQueuePreviewPosition(i), bounds, originX, originZ, cs, occupiedCells, positions);
        }

        HeatLampStation[] lamps = FindObjectsByType<HeatLampStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (HeatLampStation lamp in lamps)
        {
            if (lamp == null) continue;
            for (int i = 0; i < 4; i++)
                AddQueueCell(lamp.GetCustomerPickupPosition(i), bounds, originX, originZ, cs, occupiedCells, positions);
        }

        SetQueueCellCount(positions.Count);
        float y = bounds.max.y + yOffset * 1.6f;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 center = positions[i];
            center.y = y;
            GameObject cell = queueCells[i];
            cell.transform.position = center;
            cell.transform.rotation = Quaternion.identity;
            cell.transform.localScale = new Vector3(cs * queueCellFill, 1f, cs * queueCellFill);
            cell.SetActive(true);
        }
    }

    void AddQueueCell(Vector3 world, Bounds customerBounds, float originX, float originZ, float cs,
        HashSet<Vector2Int> occupiedCells, List<Vector3> positions)
    {
        int x = Mathf.FloorToInt((world.x - originX) / cs);
        int z = Mathf.FloorToInt((world.z - originZ) / cs);
        Vector3 center = new Vector3(originX + (x + 0.5f) * cs, customerBounds.center.y,
            originZ + (z + 0.5f) * cs);
        if (center.x < customerBounds.min.x || center.x > customerBounds.max.x
            || center.z < customerBounds.min.z || center.z > customerBounds.max.z)
            return;

        var key = new Vector2Int(x, z);
        if (occupiedCells.Add(key))
            positions.Add(center);
    }

    void SetQueueCellCount(int count)
    {
        while (queueCells.Count < count)
            queueCells.Add(CreateQueueCell("CustomerQueueCell_" + queueCells.Count));

        activeQueueCellCount = count;
        for (int i = 0; i < queueCells.Count; i++)
            if (queueCells[i] != null)
                queueCells[i].SetActive(i < count);
    }

    GameObject CreateQueueCell(string cellName)
    {
        EnsureQueueCellResources();

        GameObject root = new GameObject(cellName);
        root.transform.SetParent(transform, false);
        AddQueueCellLayer(root.transform, "DarkBorder", queueCellBorderMaterial, 1f, 0f);
        AddQueueCellLayer(root.transform, "BrightCenter", queueCellMaterial, 0.78f, 0.004f);
        return root;
    }

    void AddQueueCellLayer(Transform parent, string layerName, Material material, float scale, float height)
    {
        GameObject layer = new GameObject(layerName);
        layer.transform.SetParent(parent, false);
        layer.transform.localPosition = Vector3.up * height;
        layer.transform.localScale = new Vector3(scale, 1f, scale);
        MeshFilter filter = layer.AddComponent<MeshFilter>();
        filter.sharedMesh = queueCellMesh;
        MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void EnsureQueueCellResources()
    {
        if (queueCellMesh == null)
        {
            queueCellMesh = new Mesh { name = "StaticCustomerQueueTile" };
            queueCellMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
            };
            queueCellMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            queueCellMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            queueCellMesh.RecalculateBounds();
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (queueCellMaterial == null)
        {
            queueCellMaterial = new Material(shader) { name = "Customer Queue Tile Bright" };
            queueCellMaterial.color = queueCellColor;
        }
        if (queueCellBorderMaterial == null)
        {
            queueCellBorderMaterial = new Material(shader) { name = "Customer Queue Tile Border" };
            queueCellBorderMaterial.color = queueCellBorderColor;
        }
    }

    bool TryGetCustomerGridBounds(out Bounds bounds, out float originX, out float originZ)
    {
        bounds = default;
        originX = originZ = 0f;
        FindCustomerFloor();
        if (customerFloor == null || grid == null) return false;
        Renderer floorRenderer = customerFloor.GetComponentInChildren<Renderer>();
        if (floorRenderer == null) return false;

        bounds = floorRenderer.bounds;
        float cs = Mathf.Max(0.01f, grid.cellSize);
        originX = grid.Origin.x + Mathf.Round((bounds.min.x - grid.Origin.x) / cs) * cs;
        originZ = grid.Origin.z + Mathf.Round((bounds.min.z - grid.Origin.z) / cs) * cs;
        return true;
    }

    void BuildCustomerGrid(List<LineRenderer> rebuiltLines, float cellSize)
    {
        if (!showCustomerGrid) return;
        FindCustomerFloor();
        if (customerFloor == null) return;

        if (!TryGetCustomerGridBounds(out Bounds bounds, out float originX, out float originZ)) return;
        int width = Mathf.Max(1, Mathf.RoundToInt(bounds.size.x / cellSize));
        int height = Mathf.Max(1, Mathf.RoundToInt(bounds.size.z / cellSize));
        float y = bounds.max.y;

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = new Vector3(originX + x * cellSize, y, originZ);
            Vector3 end = new Vector3(originX + x * cellSize, y, originZ + height * cellSize);
            rebuiltLines.Add(CreateLine("Customer_V_" + x, start, end, true));
        }

        for (int z = 0; z <= height; z++)
        {
            Vector3 start = new Vector3(originX, y, originZ + z * cellSize);
            Vector3 end = new Vector3(originX + width * cellSize, y, originZ + z * cellSize);
            rebuiltLines.Add(CreateLine("Customer_H_" + z, start, end, true));
        }
    }

    void FindCustomerFloor()
    {
        if (customerFloor != null) return;
        GameObject found = GameObject.Find("CustomerFloor");
        if (found != null)
            customerFloor = found.transform;
    }

    LineRenderer CreateLine(string name, Vector3 a, Vector3 b, bool customerLine)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;

        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        lr.material = lineMaterial ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
        if (customerLine)
        {
            lr.startColor = customerGridColor;
            lr.endColor = customerGridColor;
        }

        lr.SetPosition(0, a + Vector3.up * yOffset);
        lr.SetPosition(1, b + Vector3.up * yOffset);

        return lr;
    }
}
