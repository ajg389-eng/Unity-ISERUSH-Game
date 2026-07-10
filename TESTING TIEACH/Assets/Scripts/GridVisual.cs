using UnityEngine;

public class GridVisual : MonoBehaviour
{
    public GameModeManager modeManager;
    public GridManager grid;

    [Header("Line settings")]
    public Material lineMaterial;     // optional (can be empty)
    public float lineWidth = 0.03f;
    public float yOffset = 0.02f;

    private LineRenderer[] lines;

    void Start()
    {
        if (!grid) grid = GetComponent<GridManager>() ?? FindObjectOfType<GridManager>();
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
    }

    void UpdateVisibility()
    {
        bool show = (modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Build);
        if (lines == null) return;
        foreach (var lr in lines) if (lr) lr.enabled = show;
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
        }

        int w = grid.Width;
        int h = grid.Height;
        float cs = grid.cellSize;

        // There are (w+1) vertical lines and (h+1) horizontal lines
        lines = new LineRenderer[(w + 1) + (h + 1)];
        int idx = 0;

        // Vertical lines (along Z)
        for (int x = 0; x <= w; x++)
        {
            Vector3 start = grid.Origin + new Vector3(x * cs, 0f, 0f);
            Vector3 end = grid.Origin + new Vector3(x * cs, 0f, h * cs);
            lines[idx++] = CreateLine("V_" + x, start, end);
        }

        // Horizontal lines (along X)
        for (int y = 0; y <= h; y++)
        {
            Vector3 start = grid.Origin + new Vector3(0f, 0f, y * cs);
            Vector3 end = grid.Origin + new Vector3(w * cs, 0f, y * cs);
            lines[idx++] = CreateLine("H_" + y, start, end);
        }

        UpdateVisibility();
    }

    LineRenderer CreateLine(string name, Vector3 a, Vector3 b)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;

        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        lr.material = lineMaterial ? lineMaterial : new Material(Shader.Find("Sprites/Default"));

        lr.SetPosition(0, a + Vector3.up * yOffset);
        lr.SetPosition(1, b + Vector3.up * yOffset);

        return lr;
    }
}
