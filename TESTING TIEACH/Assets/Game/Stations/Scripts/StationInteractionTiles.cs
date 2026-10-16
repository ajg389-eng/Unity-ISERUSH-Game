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
    static readonly Vector2Int[] PickupStandRingDirections =
    {
        new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(1, 0), new Vector2Int(1, 1),
        new Vector2Int(0, 1), new Vector2Int(-1, 1),
        new Vector2Int(-1, 0), new Vector2Int(-1, -1),
    };

    public GridManager grid;
    public GameModeManager modeManager;

    [Tooltip("Fallback grid offsets if no highlight quads are found. (0, -1) = one tile in front.")]
    public List<Vector2Int> interactionTileOffsets = new List<Vector2Int> { new Vector2Int(0, -1) };

    [Header("Build mode highlight (part of station model)")]
    [Tooltip("Parent of the green interaction Quad(s), or the Quad itself. Shown in Inventory/Build mode, hidden otherwise.")]
    public GameObject buildModeHighlight;
    [Tooltip("Max XZ distance from a quad center to count as standing on it. If 0, uses 10% of cell size (or 0.1).")]
    public float interactionRadius = 0f;
    [Tooltip("Extra distance to keep an employee clear of the station after reaching its stand tile.")]
    [Min(0f)] public float employeeClearance = 0.4f;

    BuildFootprint footprint;
    bool? lastShown;
    readonly List<Vector3> interactionStandCenterCache = new List<Vector3>();
    readonly List<Vector3> interactionPositionScratch = new List<Vector3>();
    GridManager subscribedGrid;
    bool interactionStandCenterCacheValid;
    Vector3 cachedStationPosition;
    Quaternion cachedStationRotation;
    Vector3 cachedStationScale;
    Vector3 cachedGridOrigin;
    float cachedGridCellSize;
    int cachedGridWidth;
    int cachedGridHeight;
    int cachedFootprintSizeX;
    int cachedFootprintSizeY;
    int cachedOffsetSignature;
    GameObject cachedHighlight;
    int cachedHighlightChildCount;

    void Awake()
    {
        footprint = GetComponent<BuildFootprint>();
        if (grid == null) grid = FindObjectOfType<GridManager>();
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (interactionTileOffsets == null || interactionTileOffsets.Count == 0)
            interactionTileOffsets = new List<Vector2Int> { new Vector2Int(0, -1) };
        if (buildModeHighlight == null)
            TryAutoFindHighlight();
        EnsureGridSubscription();
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
        // Authored prefab highlights already follow the station transform and must
        // remain the placement-bounds exclusion reference. Replacing one leaves
        // its renderer in the prefab hierarchy, which can offset later pickups.
        if (buildModeHighlight != null && buildModeHighlight.name != "InteractionHighlight")
        {
            ApplyHighlightVisibility(force: true);
            return;
        }

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
            color = new Color(0.02f, 1f, 0.1f, 0.55f)
        };
        if (sharedHighlightMaterial.HasProperty("_BaseColor"))
            sharedHighlightMaterial.SetColor("_BaseColor", new Color(0.02f, 1f, 0.1f, 0.55f));
        if (sharedHighlightMaterial.HasProperty("_EmissionColor"))
        {
            sharedHighlightMaterial.EnableKeyword("_EMISSION");
            sharedHighlightMaterial.SetColor("_EmissionColor", new Color(0.08f, 3f, 0.3f, 1f));
        }
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
        EnsureGridSubscription();
        ApplyHighlightVisibility(force: true);
    }

    void OnDisable()
    {
        if (subscribedGrid != null)
            subscribedGrid.GridChanged -= InvalidateInteractionStandCenterCache;
        subscribedGrid = null;
        interactionStandCenterCacheValid = false;
    }

    void OnDestroy()
    {
        GridManager ownerGrid = grid != null ? grid : GridManager.Instance;
        ownerGrid?.ReleaseInteractionHighlightForPlacement(this);
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
        GetInteractionTileWorldPositions(list);
        return list;
    }

    public void GetInteractionTileWorldPositions(List<Vector3> list)
    {
        if (list == null) return;
        list.Clear();
        if (grid == null || interactionTileOffsets == null) return;

        GetOriginCell(out int ox, out int oy);
        foreach (var offset in interactionTileOffsets)
        {
            int gx = ox + offset.x;
            int gy = oy + offset.y;
            if (gx >= 0 && gx < grid.Width && gy >= 0 && gy < grid.Height)
                list.Add(grid.CellToWorld(gx, gy));
        }
    }

    /// <summary>World centers of the green interaction quads on the model.</summary>
    public List<Vector3> GetInteractionPositionsFromModel()
    {
        var list = new List<Vector3>();
        GetInteractionPositionsFromModel(list);
        return list;
    }

    public void GetInteractionPositionsFromModel(List<Vector3> list)
    {
        if (list == null) return;
        list.Clear();
        if (buildModeHighlight == null)
            TryAutoFindHighlight();
        if (buildModeHighlight == null) return;

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
            return;
        }

        // Highlight itself is the quad
        list.Add(FlattenToGround(buildModeHighlight.transform.position));
    }

    /// <summary>Returns the authored quad positions as if the station root were placed at this pose.</summary>
    public List<Vector3> GetInteractionPositionsAt(Vector3 rootPosition, Quaternion rootRotation)
    {
        List<Vector3> positions = GetInteractionPositionsFromModel();
        TransformPositionsAt(positions, rootPosition, rootRotation);
        return positions;
    }

    public void GetInteractionPositionsAt(Vector3 rootPosition, Quaternion rootRotation,
        List<Vector3> positions)
    {
        if (positions == null) return;
        GetInteractionPositionsFromModel(positions);
        TransformPositionsAt(positions, rootPosition, rootRotation);
    }

    void TransformPositionsAt(List<Vector3> positions, Vector3 rootPosition,
        Quaternion rootRotation)
    {
        Quaternion inverseCurrentRotation = Quaternion.Inverse(transform.rotation);
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 localOffset = inverseCurrentRotation * (positions[i] - transform.position);
            positions[i] = rootPosition + rootRotation * localOffset;
            if (grid != null)
                positions[i] = FlattenToGround(positions[i]);
        }
    }

    Vector3 FlattenToGround(Vector3 world)
    {
        if (grid != null)
            world.y = grid.Origin.y;
        return world;
    }

    /// <summary>First interaction stand position, which is also the station's flow-node tile.</summary>
    public Vector3 GetFirstInteractionPosition()
    {
        var centers = GetInteractionStandCenters();
        if (centers.Count > 0)
            return centers[0];
        return SnapToTileCenter(FlattenToGround(transform.position));
    }

    /// <summary>Tile center used by both the station's flow node and its workers.</summary>
    public Vector3 GetFlowNodeInteractionPosition()
    {
        Vector3 position = GetFirstInteractionPosition();
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (grid == null)
            return position;

        position = grid.GetCellCenter(position);
        position.y = grid.Origin.y;
        return position;
    }

    /// <summary>Stand positions authored as green quads, or configured fallback offsets.</summary>
    public List<Vector3> GetInteractionStandCenters()
    {
        EnsureGridSubscription();
        if (!IsInteractionStandCenterCacheCurrent())
            RebuildInteractionStandCenterCache();
        return interactionStandCenterCache;
    }

    void RebuildInteractionStandCenterCache()
    {
        List<Vector3> list = interactionStandCenterCache;
        list.Clear();

        List<Vector3> fromModel = interactionPositionScratch;
        GetInteractionPositionsFromModel(fromModel);
        if (fromModel != null && fromModel.Count > 0)
        {
            foreach (var p in fromModel)
                AddUniqueTileCenter(list, p);
        }
        else
        {
            GetInteractionTileWorldPositions(fromModel);
            foreach (var p in fromModel)
                AddUniqueTileCenter(list, p);
        }

        CacheInteractionStandCenterState();
    }

    bool IsInteractionStandCenterCacheCurrent()
    {
        if (!interactionStandCenterCacheValid)
            return false;

        if (grid != subscribedGrid
            || transform.position != cachedStationPosition
            || transform.rotation != cachedStationRotation
            || transform.lossyScale != cachedStationScale
            || buildModeHighlight != cachedHighlight
            || (buildModeHighlight != null
                && buildModeHighlight.transform.childCount != cachedHighlightChildCount)
            || (grid != null && (grid.Origin != cachedGridOrigin
                || grid.cellSize != cachedGridCellSize
                || grid.Width != cachedGridWidth || grid.Height != cachedGridHeight)))
            return false;

        int sizeX = footprint != null ? Mathf.Max(1, footprint.sizeX) : 1;
        int sizeY = footprint != null ? Mathf.Max(1, footprint.sizeY) : 1;
        return sizeX == cachedFootprintSizeX && sizeY == cachedFootprintSizeY
            && GetInteractionOffsetSignature() == cachedOffsetSignature;
    }

    void CacheInteractionStandCenterState()
    {
        cachedStationPosition = transform.position;
        cachedStationRotation = transform.rotation;
        cachedStationScale = transform.lossyScale;
        cachedGridOrigin = grid != null ? grid.Origin : Vector3.zero;
        cachedGridCellSize = grid != null ? grid.cellSize : 0f;
        cachedGridWidth = grid != null ? grid.Width : 0;
        cachedGridHeight = grid != null ? grid.Height : 0;
        cachedFootprintSizeX = footprint != null ? Mathf.Max(1, footprint.sizeX) : 1;
        cachedFootprintSizeY = footprint != null ? Mathf.Max(1, footprint.sizeY) : 1;
        cachedOffsetSignature = GetInteractionOffsetSignature();
        cachedHighlight = buildModeHighlight;
        cachedHighlightChildCount = buildModeHighlight != null
            ? buildModeHighlight.transform.childCount : 0;
        interactionStandCenterCacheValid = true;
    }

    int GetInteractionOffsetSignature()
    {
        unchecked
        {
            int signature = interactionTileOffsets != null ? interactionTileOffsets.Count : 0;
            if (interactionTileOffsets != null)
                for (int i = 0; i < interactionTileOffsets.Count; i++)
                    signature = signature * 397 ^ interactionTileOffsets[i].GetHashCode();
            return signature;
        }
    }

    void EnsureGridSubscription()
    {
        if (grid == null && GridManager.Instance != null)
            grid = GridManager.Instance;
        if (subscribedGrid == grid)
            return;

        if (subscribedGrid != null)
            subscribedGrid.GridChanged -= InvalidateInteractionStandCenterCache;
        subscribedGrid = grid;
        if (subscribedGrid != null)
            subscribedGrid.GridChanged += InvalidateInteractionStandCenterCache;
        interactionStandCenterCacheValid = false;
    }

    void InvalidateInteractionStandCenterCache()
    {
        interactionStandCenterCacheValid = false;
    }

    void AddReachableFootprintStandCenter(List<Vector3> centers, int x, int y)
    {
        // Keep route candidates limited to free grid cells and unique centers.
        if (centers == null || grid == null || !grid.IsWalkable(x, y))
            return;

        Vector3 center = grid.CellToWorld(x, y);
        center.y = grid.Origin.y;
        for (int i = 0; i < centers.Count; i++)
            if ((centers[i] - center).sqrMagnitude < 0.001f)
                return;
        centers.Add(center);
    }

    void AddNearbyPickupStandCenters(List<Vector3> centers, int centerX, int centerY)
    {
        for (int radius = 1; radius <= 4; radius++)
        {
            int before = centers.Count;
            for (int i = 0; i < PickupStandRingDirections.Length; i++)
                AddReachableFootprintStandCenter(centers,
                    centerX + PickupStandRingDirections[i].x * radius,
                    centerY + PickupStandRingDirections[i].y * radius);
            if (centers.Count > before) return;
        }
    }

    /// <summary>
    /// Adds nearby walkable positions for pathfinding when a mounted pickup
    /// station's authored and immediately adjacent stand tiles are unreachable.
    /// These are route fallbacks only and are deliberately not included in the
    /// general interaction tiles used to clear grid occupancy.
    /// </summary>
    public void AddPickupRouteFallbackCenters(List<Vector3> centers)
    {
        if (centers == null || !HasPickupStation())
            return;
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (grid == null || !grid.WorldToCell(transform.position, out int centerX, out int centerY))
            return;

        // Search a small service area around the station. This lets the worker
        // use a reachable aisle tile when the exact counter-adjacent tile is
        // occupied, while keeping the handoff close to the pickup point.
        for (int radius = 1; radius <= 2; radius++)
            for (int i = 0; i < PickupStandRingDirections.Length; i++)
                AddReachableFootprintStandCenter(centers,
                    centerX + PickupStandRingDirections[i].x * radius,
                    centerY + PickupStandRingDirections[i].y * radius);
    }

    bool HasPickupStation()
    {
        return GetComponent<HeatLampStation>() != null
            || GetComponentInParent<HeatLampStation>() != null
            || GetComponentInChildren<HeatLampStation>(true) != null;
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
        if (grid != null && IsInsideOwnFootprint(centered))
        {
            Vector3 exitDirection = centered - transform.position;
            exitDirection.y = 0f;
            if (exitDirection.sqrMagnitude < 0.001f)
            {
                exitDirection = transform.forward;
                exitDirection.y = 0f;
            }
            if (exitDirection.sqrMagnitude < 0.001f)
                exitDirection = Vector3.forward;
            exitDirection.Normalize();

            int maxSteps = Mathf.Max(3, footprint != null
                ? Mathf.Max(footprint.sizeX, footprint.sizeY) + 2
                : 4);
            for (int i = 0; i < maxSteps && IsInsideOwnFootprint(centered); i++)
                centered = SnapToTileCenter(centered + exitDirection * grid.cellSize);
        }

        if (employeeClearance > 0f)
        {
            Vector3 awayFromStation = centered - transform.position;
            awayFromStation.y = 0f;
            if (awayFromStation.sqrMagnitude > 0.001f)
            {
                centered += awayFromStation.normalized * employeeClearance;
                if (grid != null) centered.y = grid.Origin.y;
            }
        }
        for (int i = 0; i < list.Count; i++)
        {
            float dx = list[i].x - centered.x;
            float dz = list[i].z - centered.z;
            if (dx * dx + dz * dz < 0.001f)
                return;
        }
        list.Add(centered);
    }

    bool IsInsideOwnFootprint(Vector3 world)
    {
        if (grid == null) return false;
        if (footprint == null) footprint = GetComponent<BuildFootprint>();
        if (footprint == null) return false;

        int sizeX = Mathf.Max(1, footprint.sizeX);
        int sizeY = Mathf.Max(1, footprint.sizeY);
        int yaw = Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3;
        if (yaw == 1 || yaw == 3)
        {
            int temp = sizeX;
            sizeX = sizeY;
            sizeY = temp;
        }

        if (!grid.TryGetFootprintOriginFromCenter(transform.position, sizeX, sizeY, out int originX, out int originY))
            return false;
        if (!grid.WorldToCell(world, out int cellX, out int cellY))
            return false;

        return cellX >= originX && cellX < originX + sizeX
            && cellY >= originY && cellY < originY + sizeY;
    }

    float GetStandTolerance()
    {
        if (interactionRadius > 0f) return interactionRadius;
        // Slightly generous so cell-center arrival still counts for green stand quads.
        if (grid != null) return grid.cellSize * 0.4f;
        return 0.35f;
    }

    /// <summary>True when the employee is at any authored station interaction tile.</summary>
    public bool IsEmployeeOnInteractionTile(Vector3 worldPosition)
    {
        var centers = GetInteractionStandCenters();
        if (centers == null || centers.Count == 0)
            return true; // no quads configured

        float tol = GetStandTolerance();
        float tolSq = tol * tol;
        for (int i = 0; i < centers.Count; i++)
        {
            float dx = worldPosition.x - centers[i].x;
            float dz = worldPosition.z - centers[i].z;
            if (dx * dx + dz * dz <= tolSq)
                return true;
        }
        return false;
    }
}
