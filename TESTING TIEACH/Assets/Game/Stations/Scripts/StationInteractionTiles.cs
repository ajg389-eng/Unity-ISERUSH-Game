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
        if (buildModeHighlight == null)
            CreateDefaultHighlightQuads();
        ApplyHighlightVisibility(force: true);
    }

    public void RebuildGeneratedHighlight()
    {
        if (buildModeHighlight != null && buildModeHighlight.name == "InteractionHighlight")
            Destroy(buildModeHighlight);
        buildModeHighlight = null;
        CreateDefaultHighlightQuads();
        ApplyHighlightVisibility(force: true);
    }

    /// <summary>
    /// Builds the same neon-green floor stand tiles used by grill / heat lamp / etc.
    /// when this station has none in the prefab (registers).
    /// </summary>
    void CreateDefaultHighlightQuads()
    {
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();

        Material mat = ResolveHighlightMaterial();
        if (mat == null) return;

        float cell = grid != null ? Mathf.Max(0.5f, grid.cellSize) : 1f;
        float y = grid != null ? grid.Origin.y + 0.02f : transform.position.y + 0.02f;

        var root = new GameObject("InteractionHighlight");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var standPositions = GetDefaultHighlightWorldPositions(cell);
        for (int i = 0; i < standPositions.Count; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = standPositions.Count > 1 ? $"Quad ({i})" : "Quad";
            Object.Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(root.transform, true);
            Vector3 p = standPositions[i];
            p.y = y;
            quad.transform.position = p;
            quad.transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            SetWorldScale(quad.transform, Vector3.one * (cell * 0.92f));

            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        buildModeHighlight = root;
    }

    static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        if (target == null) return;
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            worldScale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            worldScale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            worldScale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }

    List<Vector3> GetDefaultHighlightWorldPositions(float cell)
    {
        var list = new List<Vector3>();

        // Prefer configured grid offsets when a grid exists.
        // Counter-mounted stations need their marker derived from the counter edge,
        // because imported prefab pivots are not aligned to the floor grid.
        var mounted = GetComponent<CounterMountedItem>();
        var fromOffsets = mounted == null ? GetInteractionTileWorldPositions() : null;
        if (fromOffsets != null && fromOffsets.Count > 0)
        {
            list.AddRange(fromOffsets);
            return list;
        }

        // Registers / stations without offsets: one stand tile on the worker side of the counter.
        Vector3 kitchenDir = ResolveKitchenStandDirection();
        Vector3 center = transform.position + kitchenDir * cell;
        if (mounted != null && mounted.surface != null)
        {
            Bounds counter = mounted.surface.GetBaseBounds();
            center = GetVisualCenter();
            float targetDepth = Vector3.Dot(counter.center, kitchenDir)
                + ExtentAlong(counter, kitchenDir) + cell * 0.5f;
            center += kitchenDir * (targetDepth - Vector3.Dot(center, kitchenDir));
        }
        if (grid != null)
            center = grid.GetCellCenter(center);

        list.Add(center);

        var fp = footprint != null ? footprint : GetComponent<BuildFootprint>();
        int width = fp != null ? Mathf.Max(1, fp.sizeX) : 1;
        if (width >= 2)
        {
            Vector3 along = Vector3.Cross(Vector3.up, kitchenDir).normalized;
            list.Clear();
            list.Add(center - along * (cell * 0.5f));
            list.Add(center + along * (cell * 0.5f));
            if (grid != null)
            {
                for (int i = 0; i < list.Count; i++)
                    list[i] = grid.GetCellCenter(list[i]);
            }
        }

        return list;
    }

    Vector3 ResolveKitchenStandDirection()
    {
        var reg = GetComponent<Register>();
        if (reg != null)
        {
            Vector3 lobby = reg.GetLobbyDirection();
            lobby.y = 0f;
            if (lobby.sqrMagnitude > 0.01f)
                return -lobby.normalized;
        }

        // Match the side other stations already use for their green stand tiles.
        Vector3 peerDir = ResolveStandDirectionFromPeerStations();
        if (peerDir.sqrMagnitude > 0.01f)
            return peerDir;

        Vector3 localFwd = transform.TransformDirection(Vector3.forward);
        localFwd.y = 0f;
        if (localFwd.sqrMagnitude > 0.01f)
            return localFwd.normalized;

        return Vector3.forward;
    }

    Vector3 GetVisualCenter()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            Transform parent = renderer.transform;
            bool isGeneratedHighlight = false;
            while (parent != null && parent != transform)
            {
                if (parent.name == "InteractionHighlight")
                {
                    isGeneratedHighlight = true;
                    break;
                }
                parent = parent.parent;
            }
            if (isGeneratedHighlight) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found ? bounds.center : transform.position;
    }

    static float ExtentAlong(Bounds bounds, Vector3 direction)
    {
        direction = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
        return Vector3.Dot(bounds.extents, direction);
    }

    Vector3 ResolveStandDirectionFromPeerStations()
    {
        var peers = FindObjectsOfType<StationInteractionTiles>();
        for (int i = 0; i < peers.Length; i++)
        {
            var peer = peers[i];
            if (peer == null || peer == this || peer.buildModeHighlight == null)
                continue;
            // Prefer heat lamp / kitchen stations that already have authored quads.
            if (peer.GetComponent<Register>() != null)
                continue;

            var centers = peer.GetInteractionPositionsFromModel();
            if (centers == null || centers.Count == 0)
                continue;

            Vector3 toQuad = centers[0] - peer.transform.position;
            toQuad.y = 0f;
            if (toQuad.sqrMagnitude > 0.05f)
                return toQuad.normalized;
        }
        return Vector3.zero;
    }

    static Material sharedHighlightMaterial;

    static Material ResolveHighlightMaterial()
    {
        if (sharedHighlightMaterial != null)
            return sharedHighlightMaterial;

        // Prefer the project InteractionHighlight material already used by other stations.
        var existing = FindObjectsOfType<StationInteractionTiles>();
        for (int i = 0; i < existing.Length; i++)
        {
            var tiles = existing[i];
            if (tiles == null || tiles.buildModeHighlight == null) continue;
            var renderers = tiles.buildModeHighlight.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null && renderers[r].sharedMaterial != null
                    && renderers[r].sharedMaterial.name.IndexOf("InteractionHighlight", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sharedHighlightMaterial = renderers[r].sharedMaterial;
                    return sharedHighlightMaterial;
                }
            }
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null && renderers[r].sharedMaterial != null)
                {
                    sharedHighlightMaterial = renderers[r].sharedMaterial;
                    return sharedHighlightMaterial;
                }
            }
        }

#if UNITY_EDITOR
        sharedHighlightMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Game/Stations/Materials/InteractionHighlight.mat");
        if (sharedHighlightMaterial != null)
            return sharedHighlightMaterial;
#endif

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        sharedHighlightMaterial = new Material(shader)
        {
            name = "InteractionHighlight_Runtime",
            color = new Color(0.15f, 1f, 0.35f, 0.85f)
        };
        if (sharedHighlightMaterial.HasProperty("_BaseColor"))
            sharedHighlightMaterial.SetColor("_BaseColor", new Color(0.15f, 1f, 0.35f, 0.85f));
        if (sharedHighlightMaterial.HasProperty("_Surface"))
            sharedHighlightMaterial.SetFloat("_Surface", 1f);
        return sharedHighlightMaterial;
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
        return SnapToTileCenter(FlattenToGround(transform.position));
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
                AddUniqueTileCenter(list, p);
            return list;
        }

        var fromOffsets = GetInteractionTileWorldPositions();
        if (fromOffsets != null)
        {
            foreach (var p in fromOffsets)
                AddUniqueTileCenter(list, p);
        }
        return list;
    }

    Vector3 SnapToTileCenter(Vector3 world)
    {
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (grid == null) return world;

        Vector3 centered = grid.GetCellCenter(world);
        centered.y = grid.Origin.y;
        return centered;
    }

    void AddUniqueTileCenter(List<Vector3> list, Vector3 world)
    {
        Vector3 centered = SnapToTileCenter(world);
        for (int i = 0; i < list.Count; i++)
        {
            float dx = list[i].x - centered.x;
            float dz = list[i].z - centered.z;
            if (dx * dx + dz * dz < 0.001f)
                return;
        }
        list.Add(centered);
    }

    float GetStandTolerance()
    {
        if (interactionRadius > 0f) return interactionRadius;
        // Slightly generous so cell-center arrival still counts for green stand quads.
        if (grid != null) return grid.cellSize * 0.4f;
        return 0.35f;
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
