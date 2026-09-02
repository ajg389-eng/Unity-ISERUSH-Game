using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines where employees must stand to use a station.
/// Prefers the green interaction quads on the station model; workers must be
/// at the center of a quad to interact.
/// Quads are visible in Inventory/Build mode and hidden in Play/Manage.
/// </summary>
public class StationInteractionTiles : MonoBehaviour
{
    public GridManager grid;
    public GameModeManager modeManager;

    [Tooltip("Fallback grid offsets if no highlight quads are found. (0, -1) = one tile in front.")]
    public List<Vector2Int> interactionTileOffsets = new List<Vector2Int> { new Vector2Int(0, -1) };

    [Header("Build mode highlight (part of station model)")]
    [Tooltip("Parent of the green interaction Quad(s), or the Quad itself. Shown in Inventory/Build mode, hidden otherwise.")]
    public GameObject buildModeHighlight;
    [Tooltip("Max XZ distance from a quad center to count as standing on it. If 0, uses 10% of cell size (or 0.1).")]
    public float interactionRadius = 0f;

    BuildFootprint footprint;
    bool? lastShown;

    void Awake()
    {
        footprint = GetComponent<BuildFootprint>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (interactionTileOffsets == null || interactionTileOffsets.Count == 0)
            interactionTileOffsets = new List<Vector2Int> { new Vector2Int(0, -1) };
        if (buildModeHighlight == null)
            TryAutoFindHighlight();
    }

    void Start()
    {
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (buildModeHighlight == null)
            TryAutoFindHighlight();
        ApplyHighlightVisibility(force: true);
    }

    void TryAutoFindHighlight()
    {
        // Common setup: child named like the green interaction quad
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (t.name == "Quad" || t.name.IndexOf("Interaction", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                buildModeHighlight = t.gameObject;
                return;
            }
        }

        // Fallback: any child using a Quad mesh (registers often add these by hand)
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null || mf.gameObject == gameObject || mf.sharedMesh == null) continue;
            if (mf.sharedMesh.name.IndexOf("Quad", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                buildModeHighlight = mf.gameObject;
                return;
            }
        }
    }

    /// <summary>Re-scan children for a green interaction quad if none is assigned.</summary>
    public void EnsureHighlightReference()
    {
        if (buildModeHighlight == null)
            TryAutoFindHighlight();
        ApplyHighlightVisibility(force: true);
    }

    void Update()
    {
        ApplyHighlightVisibility(force: false);
    }

    void OnEnable()
    {
        ApplyHighlightVisibility(force: true);
    }

    void ApplyHighlightVisibility(bool force)
    {
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (buildModeHighlight == null) return;

        // Same as other stations: visible only while Inventory opens Build mode
        bool show = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Build;
        if (!force && lastShown.HasValue && lastShown.Value == show) return;
        lastShown = show;
        buildModeHighlight.SetActive(show);
    }

    public void GetOriginCell(out int originX, out int originY)
    {
        originX = 0;
        originY = 0;
        if (grid == null) return;

        int sizeX = footprint != null ? Mathf.Max(1, footprint.sizeX) : 1;
        int sizeY = footprint != null ? Mathf.Max(1, footprint.sizeY) : 1;
        float continuousX = (transform.position.x - grid.Origin.x) / grid.cellSize;
        float continuousZ = (transform.position.z - grid.Origin.z) / grid.cellSize;
        originX = Mathf.RoundToInt(continuousX - sizeX * 0.5f);
        originY = Mathf.RoundToInt(continuousZ - sizeY * 0.5f);
    }

    /// <summary>Fallback stand positions from grid offsets.</summary>
    public List<Vector3> GetInteractionTileWorldPositions()
    {
        var list = new List<Vector3>();
        if (grid == null || interactionTileOffsets == null) return list;

        GetOriginCell(out int ox, out int oy);
        foreach (var offset in interactionTileOffsets)
        {
            int gx = ox + offset.x;
            int gy = oy + offset.y;
            if (gx >= 0 && gx < grid.Width && gy >= 0 && gy < grid.Height)
                list.Add(grid.CellToWorld(gx, gy));
        }
        return list;
    }

    /// <summary>World centers of the green interaction quads on the model.</summary>
    public List<Vector3> GetInteractionPositionsFromModel()
    {
        var list = new List<Vector3>();
        if (buildModeHighlight == null) return list;

        // If highlight has child quads, each child is a stand point
        int childCount = buildModeHighlight.transform.childCount;
        if (childCount > 0)
        {
            for (int i = 0; i < childCount; i++)
            {
                var child = buildModeHighlight.transform.GetChild(i);
                if (child != null)
                    list.Add(FlattenToGround(child.position));
            }
            return list;
        }

        // Highlight itself is the quad
        list.Add(FlattenToGround(buildModeHighlight.transform.position));
        return list;
    }

    Vector3 FlattenToGround(Vector3 world)
    {
        if (grid != null)
            world.y = grid.Origin.y;
        return world;
    }

    /// <summary>First interaction stand position (center of first green quad).</summary>
    public Vector3 GetFirstInteractionPosition()
    {
        var centers = GetInteractionStandCenters();
        if (centers.Count > 0)
            return centers[0];
        return FlattenToGround(transform.position);
    }

    /// <summary>
    /// Stand positions: green quad centers first (exact XZ), then grid-offset fallback.
    /// </summary>
    public List<Vector3> GetInteractionStandCenters()
    {
        var list = new List<Vector3>();

        var fromModel = GetInteractionPositionsFromModel();
        if (fromModel != null && fromModel.Count > 0)
        {
            foreach (var p in fromModel)
                list.Add(p);
            return list;
        }

        var fromOffsets = GetInteractionTileWorldPositions();
        if (fromOffsets != null)
        {
            foreach (var p in fromOffsets)
                list.Add(p);
        }
        return list;
    }

    float GetStandTolerance()
    {
        if (interactionRadius > 0f) return interactionRadius;
        if (grid != null) return grid.cellSize * 0.1f;
        return 0.1f;
    }

    /// <summary>True only when the employee is at the center of a green interaction quad.</summary>
    public bool IsEmployeeOnInteractionTile(Vector3 worldPosition)
    {
        var centers = GetInteractionStandCenters();
        if (centers == null || centers.Count == 0)
            return true; // no quads configured

        float tol = GetStandTolerance();
        float tolSq = tol * tol;

        for (int i = 0; i < centers.Count; i++)
        {
            Vector3 c = centers[i];
            float dx = worldPosition.x - c.x;
            float dz = worldPosition.z - c.z;
            if (dx * dx + dz * dz <= tolSq)
                return true;
        }
        return false;
    }
}
