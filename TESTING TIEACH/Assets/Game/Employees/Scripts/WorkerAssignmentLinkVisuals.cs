using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shows a selected worker's assigned production flow on the floor grid.
/// </summary>
public class WorkerAssignmentLinkVisuals : MonoBehaviour
{
    public static WorkerAssignmentLinkVisuals Instance { get; private set; }

    public GameModeManager modeManager;

    [Header("Grid route look")]
    public Color routeColor = new Color(0.2f, 0.9f, 1f, 0.95f);
    public Color startColor = new Color(0.2f, 1f, 0.35f, 0.95f);
    public Color stopColor = new Color(1f, 0.75f, 0.12f, 0.95f);
    public Color endColor = new Color(1f, 0.25f, 0.2f, 0.95f);
    public Color routeOutlineColor = new Color(0.035f, 0.08f, 0.11f, 0.82f);
    public float lineWidth = 0.11f;
    public float lineOutlineWidth = 0.09f;
    public float floorOffset = 0.08f;
    [Range(0.2f, 0.9f)] public float markerTileScale = 0.42f;
    [Range(0.1f, 0.5f)] public float arrowTileScale = 0.24f;

    [Header("Station assignment link")]
    [Tooltip("How high above the worker/station the assignment bridge runs.")]
    public float raiseHeight = 1.35f;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<Transform> recipePreviewBadges = new List<Transform>();
    readonly List<Mesh> generatedMeshes = new List<Mesh>();
    readonly List<WorkerHoverHighlight> flowWorkerHighlights = new List<WorkerHoverHighlight>();
    Material lineMaterial;
    Material lineOutlineMaterial;
    Material markerCenterMaterial;
    Material arrowMaterial;
    Material startMaterial;
    Material stopMaterial;
    Material endMaterial;
    Texture2D circleTexture;
    Sprite circleSprite;
    Transform visualsRoot;
    bool visible;
    bool billboardPoseCached;
    Transform billboardCamera;
    Vector3 billboardCameraPosition;
    Quaternion billboardCameraRotation;
    KitchenEmployee focusedWorker;
    StationNode focusedStation;
    ProductionFlowPlan focusedFlow;

    static readonly Color[] TeamColors =
    {
        new Color(0.2f, 0.9f, 1f, 0.95f),
        new Color(1f, 0.55f, 0.18f, 0.95f),
        new Color(0.72f, 0.4f, 1f, 0.95f),
        new Color(0.25f, 1f, 0.45f, 0.95f)
    };

    static readonly Color[] FlowBranchColors =
    {
        new Color(0.18f, 0.72f, 0.79f, 0.96f),
        new Color(0.25f, 0.60f, 0.76f, 0.96f),
        new Color(0.23f, 0.67f, 0.62f, 0.96f),
        new Color(0.37f, 0.55f, 0.72f, 0.96f),
        new Color(0.31f, 0.66f, 0.72f, 0.96f),
        new Color(0.36f, 0.62f, 0.58f, 0.96f)
    };

    public static bool HasFocusedWorker => Instance != null && Instance.focusedWorker != null;
    public static ProductionFlowPlan FocusedFlow => Instance != null ? Instance.focusedFlow : null;

    void Awake()
    {
        Instance = this;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        EnsureRoot();
    }

    void OnDestroy()
    {
        focusedFlow = null;
        RefreshFlowWorkerHighlights();
        if (Instance == this) Instance = null;
        DestroyMaterial(lineMaterial);
        DestroyMaterial(lineOutlineMaterial);
        DestroyMaterial(markerCenterMaterial);
        DestroyMaterial(arrowMaterial);
        DestroyMaterial(startMaterial);
        DestroyMaterial(stopMaterial);
        DestroyMaterial(endMaterial);
        if (circleSprite != null) Destroy(circleSprite);
        if (circleTexture != null) Destroy(circleTexture);
    }

    void Update()
    {
        bool manage = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        // Recipe badges belong to Business mode itself. Flow focus only controls
        // route lines, markers, and worker highlighting.
        bool show = manage;

        if (show != visible)
        {
            visible = show;
            if (visible) Refresh();
            else ClearVisuals();
        }

        if (!visible) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Transform cameraTransform = cam.transform;
        if (billboardPoseCached && billboardCamera == cameraTransform
            && (cameraTransform.position - billboardCameraPosition).sqrMagnitude < 0.0001f
            && Quaternion.Angle(cameraTransform.rotation, billboardCameraRotation) < 0.1f)
            return;
        FaceRecipePreviewsTowardCamera();
        billboardCamera = cameraTransform;
        billboardCameraPosition = cameraTransform.position;
        billboardCameraRotation = cameraTransform.rotation;
        billboardPoseCached = true;
    }

    public static void NotifyLinksChanged()
    {
        if (Instance != null)
            Instance.Refresh();
    }

    public static void SetFocusedWorker(KitchenEmployee worker)
    {
        if (Instance == null) return;
        // A flow selected in the Staff UI is persistent. World selections may
        // still open their own panels, but must not replace its grid route.
        if (Instance.focusedFlow != null)
        {
            Instance.focusedWorker = null;
            Instance.focusedStation = null;
            Instance.ApplyFocus();
            return;
        }
        Instance.focusedWorker = worker;
        Instance.focusedStation = null;
        Instance.ApplyFocus();
    }

    public static void SetFocusedStation(StationNode station)
    {
        if (Instance == null) return;
        Instance.focusedStation = station;
        Instance.focusedWorker = null;
        Instance.ApplyFocus();
    }

    public static void SetFocusedFlow(ProductionFlowPlan flow)
    {
        if (Instance == null) return;
        Instance.focusedFlow = flow;
        Instance.focusedWorker = null;
        Instance.focusedStation = null;
        Instance.ApplyFocus();
    }

    public static void ClearFocus()
    {
        if (Instance == null) return;
        Instance.focusedWorker = null;
        Instance.focusedStation = null;
        // Clicking empty space or clearing a world-object selection is only a
        // transient clear. Keep the explicitly selected flow pinned.
        if (Instance.focusedFlow != null)
        {
            Instance.ApplyFocus();
            return;
        }
        Instance.ApplyFocus();
    }

    /// <summary>Explicitly releases the flow selected by the flow UI.</summary>
    public static void ClearFocusedFlow()
    {
        if (Instance == null) return;
        Instance.focusedFlow = null;
        Instance.ApplyFocus();
    }

    public static bool IsWorkerAssociatedWithFocusedFlow(KitchenEmployee worker)
    {
        ProductionFlowPlan flow = FocusedFlow;
        return worker != null && flow != null && flow.workers != null && flow.workers.Contains(worker);
    }

    void ApplyFocus()
    {
        bool manage = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        visible = manage;
        if (visible) Refresh();
        else
        {
            RefreshFlowWorkerHighlights();
            ClearVisuals();
        }
    }

    public void Refresh()
    {
        billboardPoseCached = false;
        RefreshFlowWorkerHighlights();
        EnsureRoot();
        ClearVisuals();

        if (modeManager == null || modeManager.CurrentMode != GameModeManager.Mode.Manage)
        {
            visible = false;
            return;
        }

        DrawAllStationPreviews();

        // A highlighted station narrows the grid overlay to its direct
        // incoming and outgoing connections, even when a flow is selected.
        if (focusedStation != null)
        {
            DrawStationConnections(focusedStation);
            return;
        }

        if (focusedWorker != null)
        {
            DrawWorkerFlow(focusedWorker, routeColor, false);
            return;
        }

        // Flow focus (including live flow capture): always draw the station route on the grid.
        if (focusedFlow != null)
        {
            DrawDraftFlow(focusedFlow);
            return;
        }

    }

    void DrawStationConnections(StationNode station)
    {
        GridManager grid = GridManager.Instance;
        ProductionManager production = ProductionManager.Instance;
        if (grid == null || station == null || production == null
            || production.productionFlows == null)
            return;

        var markedStations = new HashSet<GameObject>();
        GameObject selected = station.gameObject;
        for (int flowIndex = 0; flowIndex < production.productionFlows.Count; flowIndex++)
        {
            ProductionFlowPlan flow = production.productionFlows[flowIndex];
            if (flow == null) continue;
            flow.EnsureLegacyConnections();
            if (flow.connections == null) continue;

            var incoming = new HashSet<GameObject>();
            var outgoing = new HashSet<GameObject>();
            var incomingCount = new Dictionary<GameObject, int>();
            var outgoingCount = new Dictionary<GameObject, int>();
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection == null || connection.from == null || connection.to == null) continue;
                outgoing.Add(connection.from);
                incoming.Add(connection.to);
                outgoingCount[connection.from] = outgoingCount.TryGetValue(connection.from,
                    out int outCount) ? outCount + 1 : 1;
                incomingCount[connection.to] = incomingCount.TryGetValue(connection.to,
                    out int inCount) ? inCount + 1 : 1;
            }

            for (int edgeIndex = 0; edgeIndex < flow.connections.Count; edgeIndex++)
            {
                ProductionFlowConnection connection = flow.connections[edgeIndex];
                if (connection == null || connection.from == null || connection.to == null
                    || (connection.from != selected && connection.to != selected))
                    continue;

                if (markedStations.Add(connection.from))
                {
                    CreateStationConnectionMarker(connection.from, grid, incoming, outgoing,
                        incomingCount, outgoingCount);
                    CreateStationNodeConnector(connection.from, grid,
                        GetFlowNodeColor(connection.from, incoming, outgoing), "Selected_" + flowIndex);
                }
                if (markedStations.Add(connection.to))
                {
                    CreateStationConnectionMarker(connection.to, grid, incoming, outgoing,
                        incomingCount, outgoingCount);
                    CreateStationNodeConnector(connection.to, grid,
                        GetFlowNodeColor(connection.to, incoming, outgoing), "Selected_" + flowIndex);
                }

                Vector3 from = grid.GetCellCenter(
                    KitchenEmployee.GetInteractionPosition(connection.from));
                Vector3 to = grid.GetCellCenter(
                    KitchenEmployee.GetInteractionPosition(connection.to));
                var points = new List<Vector3>();
                AppendGridLeg(points, grid, from, to);
                if (points.Count < 2) continue;

                Color branchColor = FlowBranchColors[edgeIndex % FlowBranchColors.Length];
                Color sourceColor = GetFlowNodeColor(connection.from, incoming, outgoing);
                CreateGridLine(points,
                    (string.IsNullOrEmpty(flow.flowName) ? "Flow" : flow.flowName)
                    + "_Station_Connection_" + flowIndex + "_" + edgeIndex,
                    branchColor, true, sourceColor);
            }
        }
    }

    Color GetFlowNodeColor(GameObject station, HashSet<GameObject> incoming,
        HashSet<GameObject> outgoing)
    {
        if (station == null || !incoming.Contains(station)) return startColor;
        return outgoing.Contains(station) ? stopColor : endColor;
    }

    void CreateStationConnectionMarker(GameObject station, GridManager grid,
        HashSet<GameObject> incoming, HashSet<GameObject> outgoing,
        Dictionary<GameObject, int> incomingCount, Dictionary<GameObject, int> outgoingCount)
    {
        Vector3 position = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(station));
        position.y = grid.Origin.y + floorOffset;
        bool root = !incoming.Contains(station);
        bool end = !outgoing.Contains(station);
        bool split = outgoingCount.TryGetValue(station, out int branches) && branches > 1;
        bool merge = incomingCount.TryGetValue(station, out int sources) && sources > 1;
        string role = root ? "START" : end ? "END" : split ? "SPLIT" : merge ? "MERGE" : "STOP";
        Color color = root ? startColor : end ? endColor : stopColor;
        CreateMarker(position, role, GetDisplayName(station), color, grid.cellSize);
    }

    void RefreshFlowWorkerHighlights()
    {
        for (int i = 0; i < flowWorkerHighlights.Count; i++)
            if (flowWorkerHighlights[i] != null)
                flowWorkerHighlights[i].SetFlowHighlighted(false);
        flowWorkerHighlights.Clear();

        if (focusedFlow?.workers == null) return;
        foreach (KitchenEmployee worker in focusedFlow.workers)
        {
            if (worker == null) continue;
            WorkerHoverHighlight highlight = WorkerHoverHighlight.EnsureOn(worker);
            if (highlight == null || flowWorkerHighlights.Contains(highlight)) continue;
            highlight.SetFlowHighlighted(true);
            flowWorkerHighlights.Add(highlight);
        }
    }

    void DrawDraftFlow(ProductionFlowPlan flow)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || flow == null || flow.stations == null || flow.stations.Count == 0)
            return;

        flow.EnsureLegacyConnections();
        var incoming = new HashSet<GameObject>();
        var outgoing = new HashSet<GameObject>();
        var incomingCount = new Dictionary<GameObject, int>();
        var outgoingCount = new Dictionary<GameObject, int>();
        foreach (ProductionFlowConnection connection in flow.connections)
        {
            if (connection == null || connection.from == null || connection.to == null) continue;
            outgoing.Add(connection.from);
            incoming.Add(connection.to);
            outgoingCount[connection.from] = outgoingCount.TryGetValue(connection.from, out int outCount)
                ? outCount + 1 : 1;
            incomingCount[connection.to] = incomingCount.TryGetValue(connection.to, out int inCount)
                ? inCount + 1 : 1;
        }

        for (int i = 0; i < flow.stations.Count; i++)
        {
            GameObject station = flow.stations[i];
            if (station == null) continue;

            // Route markers sit at the same tile center the workers use. Using
            // the model's interaction point here left short diagonal stubs at
            // the start and end of otherwise grid-aligned routes.
            Vector3 position = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(station));
            position.y = grid.Origin.y + floorOffset;
            bool root = !incoming.Contains(station);
            bool end = !outgoing.Contains(station);
            Color color = root ? startColor : end ? endColor : stopColor;
            bool split = outgoingCount.TryGetValue(station, out int branches) && branches > 1;
            bool merge = incomingCount.TryGetValue(station, out int sources) && sources > 1;
            string role = root ? "START" : end ? "END" : split ? "SPLIT" : merge ? "MERGE" : "STOP";
            CreateMarker(position, role, GetDisplayName(station), color, grid.cellSize);
            CreateStationNodeConnector(station, grid, color, "Draft_" + flow.GetHashCode());
        }

        int edgeIndex = 0;
        foreach (ProductionFlowConnection connection in flow.connections)
        {
            if (connection == null || connection.from == null || connection.to == null) continue;
            var points = new List<Vector3>();
            Vector3 from = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(connection.from));
            Vector3 to = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(connection.to));
            AppendGridLeg(points, grid, from, to);
            if (points.Count < 2) continue;
            Color branchColor = FlowBranchColors[edgeIndex % FlowBranchColors.Length];
            Color sourceColor = GetFlowNodeColor(connection.from, incoming, outgoing);
            CreateGridLine(points, (string.IsNullOrEmpty(flow.flowName) ? "Flow" : flow.flowName)
                + "_Branch_" + edgeIndex, branchColor, true, sourceColor);
            edgeIndex++;
        }
    }

    static bool IsLastNonNullStation(ProductionFlowPlan flow, int index)
    {
        for (int i = index + 1; i < flow.stations.Count; i++)
            if (flow.stations[i] != null) return false;
        return true;
    }

    void DrawWorkerFlow(KitchenEmployee worker, Color workerColor, bool showOwner)
    {
        GridManager grid = worker.grid != null ? worker.grid : GridManager.Instance;
        if (grid == null || worker.operatedStations == null || worker.operatedStations.Count == 0)
            return;

        List<List<GameObject>> chains = BuildFlowChains(worker);
        int pathIndex = 0;
        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            List<GameObject> chain = chains[chainIndex];
            if (chain.Count == 0) continue;

            for (int i = 0; i < chain.Count; i++)
            {
                Vector3 stopPosition = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(chain[i]));
                stopPosition.y = grid.Origin.y + floorOffset;

                bool isStart = i == 0;
                bool isEnd = i == chain.Count - 1;
                StationNode stopNode = StationNode.EnsureOn(chain[i]);
                bool isHandoff = isEnd && stopNode != null && stopNode.IsWorkStation && !stopNode.IsWorkerAssigned(worker);
                string role = isHandoff ? "HANDOFF" : isStart && isEnd ? "START / END" : isStart ? "START" : isEnd ? "END" : "STOP " + i;
                Color markerColor = isStart ? startColor : isEnd ? endColor : stopColor;
                string owner = showOwner ? worker.employeeName + "\n" : "";
                CreateMarker(stopPosition, role, owner + GetDisplayName(chain[i]), markerColor, grid.cellSize);
                CreateStationNodeConnector(chain[i], grid, markerColor,
                    "Worker_" + worker.GetInstanceID() + "_" + chainIndex + "_" + i);

                if (i == 0) continue;
                Vector3 previous = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(chain[i - 1]));
                var edgePoints = new List<Vector3>();
                AppendGridLeg(edgePoints, grid, previous, stopPosition);
                if (edgePoints.Count >= 2)
                {
                    Color sourceColor = i == 1 ? startColor : stopColor;
                    CreateGridLine(edgePoints,
                        worker.employeeName + "_Flow_" + pathIndex++, workerColor, true,
                        sourceColor);
                }
            }
        }
    }

    static List<List<GameObject>> BuildFlowChains(KitchenEmployee worker)
    {
        var chains = new List<List<GameObject>>();
        ProductionFlowPlan flow = ProductionManager.Instance != null
            ? ProductionManager.Instance.GetFlowForWorker(worker) : null;
        if (flow != null)
        {
            flow.EnsureLegacyConnections();
            var assigned = new HashSet<GameObject>();
            foreach (GameObject station in worker.operatedStations)
                if (station != null) assigned.Add(station);

            var incomingAssigned = new HashSet<GameObject>();
            foreach (ProductionFlowConnection connection in flow.connections)
                if (connection != null && assigned.Contains(connection.from) && assigned.Contains(connection.to))
                    incomingAssigned.Add(connection.to);

            foreach (GameObject station in worker.operatedStations)
                if (station != null && !incomingAssigned.Contains(station))
                    AddWorkerGraphPaths(flow, worker, station, new List<GameObject>(), chains);

            if (chains.Count > 0) return chains;
        }

        List<GameObject> route = WorkflowAnalysis.GetOrderedRoute(worker);
        if (route.Count > 0)
            chains.Add(route);
        return chains;
    }

    static void AddWorkerGraphPaths(ProductionFlowPlan flow, KitchenEmployee worker, GameObject current,
        List<GameObject> prefix, List<List<GameObject>> chains)
    {
        if (current == null || prefix.Contains(current)) return;
        var path = new List<GameObject>(prefix) { current };
        List<GameObject> outgoing = flow.GetOutgoing(current);
        bool extended = false;
        foreach (GameObject next in outgoing)
        {
            if (next == null) continue;
            bool workerOwnsNext = worker.IsAssignedTo(next);
            if (workerOwnsNext)
            {
                AddWorkerGraphPaths(flow, worker, next, path, chains);
                extended = true;
            }
            else
            {
                var handoff = new List<GameObject>(path) { next };
                chains.Add(handoff);
                extended = true;
            }
        }
        if (!extended)
            chains.Add(path);
    }

    void AppendGridLeg(List<Vector3> points, GridManager grid, Vector3 from, Vector3 to)
    {
        // Route vertices are always snapped to tile centers. GridManager uses
        // four-neighbor pathfinding, so each segment is straight or a 90-degree turn.
        from = grid.GetCellCenter(from);
        to = grid.GetCellCenter(to);
        from.y = grid.Origin.y + floorOffset;
        to.y = grid.Origin.y + floorOffset;
        AddPointIfDistinct(points, from);

        List<Vector3> path = grid.GetPath(from, grid.GetCellCenter(to));
        if (path != null && path.Count > 0)
        {
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 point = path[i];
                point.y = grid.Origin.y + floorOffset;
                AddPointIfDistinct(points, point);
            }
        }
        else
        {
            // Fallback: Manhattan corridor so the draft still shows if pathing fails mid-capture.
            Vector3 mid = new Vector3(to.x, from.y, from.z);
            AddPointIfDistinct(points, mid);
        }

        AddPointIfDistinct(points, to);
    }

    static void AddPointIfDistinct(List<Vector3> points, Vector3 point)
    {
        if (points.Count == 0 || Vector3.SqrMagnitude(points[points.Count - 1] - point) > 0.001f)
            points.Add(point);
    }

    void CreateGridLine(List<Vector3> points, string routeName, Color color,
        bool showDirection = true, Color? directionColor = null)
    {
        var outlineObject = new GameObject(routeName + "_Outline");
        outlineObject.transform.SetParent(visualsRoot, false);
        var outline = outlineObject.AddComponent<LineRenderer>();
        ConfigureLine(outline, points, lineWidth + lineOutlineWidth,
            GetLineOutlineMaterial(), routeOutlineColor);
        lines.Add(outline);

        var go = new GameObject(routeName);
        go.transform.SetParent(visualsRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        ConfigureLine(lr, points, lineWidth, GetLineMaterial(), color);
        lines.Add(lr);

        if (showDirection)
            CreateDirectionArrow(points, routeName + "_Direction", directionColor ?? color);
    }

    static void ConfigureLine(LineRenderer line, List<Vector3> points, float width,
        Material material, Color color)
    {
        line.useWorldSpace = true;
        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 6;
        line.numCornerVertices = 6;
        line.material = material;
        line.startColor = color;
        line.endColor = color;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = width > 0.15f ? 2 : 3;
    }

    void CreateDirectionArrow(List<Vector3> points, string arrowName, Color color)
    {
        if (points == null || points.Count < 2) return;

        float cellSize = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        if (TryGetFirstSegmentDirection(points, out Vector3 direction))
            CreateDirectionArrowAtNode(points[0], direction, arrowName, color, cellSize);
    }

    void CreateDirectionArrowAtNode(Vector3 nodePosition, Vector3 direction,
        string arrowName, Color color, float cellSize)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        direction.y = 0f;
        direction.Normalize();
        Vector3 position = nodePosition + direction * (cellSize * 0.2f)
            + Vector3.up * 0.025f;
        CreateArrowMesh(arrowName,
            position + Vector3.up * 0.002f, direction,
            cellSize * arrowTileScale * 1.25f, GetArrowMaterial(), color);
    }

    static bool TryGetFirstSegmentDirection(List<Vector3> points, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (points == null || points.Count < 2) return false;

        for (int i = 1; i < points.Count; i++)
        {
            direction = points[i] - points[i - 1];
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) continue;
            direction.Normalize();
            return true;
        }

        return false;
    }

    void CreateArrowMesh(string objectName, Vector3 position, Vector3 direction, float size,
        Material material, Color color)
    {
        var arrow = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        arrow.transform.SetParent(visualsRoot, false);
        arrow.transform.position = position;
        arrow.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        var mesh = new Mesh { name = objectName + "Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 0f, size * 0.62f),
            new Vector3(-size * 0.48f, 0f, -size * 0.42f),
            new Vector3(size * 0.48f, 0f, -size * 0.42f)
        };
        // The arrow lies flat on the floor. Reverse the winding so its front
        // face points upward and remains visible from the player's camera.
        mesh.triangles = new[] { 0, 2, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        arrow.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = arrow.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        var colorBlock = new MaterialPropertyBlock();
        colorBlock.SetColor("_Color", color);
        colorBlock.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(colorBlock);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 15;
    }

    void CreateMarker(Vector3 position, string role, string stationName, Color color, float cellSize)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "FlowMarker_" + role + "_" + stationName.Replace("\n", "_");
        marker.transform.SetParent(visualsRoot, true);
        marker.transform.position = position + Vector3.up * 0.035f;
        float diameter = Mathf.Max(0.18f, cellSize * markerTileScale);
        marker.transform.localScale = new Vector3(diameter, 0.018f, diameter);
        var collider = marker.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetMarkerMaterial(color);
            renderer.sortingOrder = 20;
        }

        var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        center.name = "Center";
        center.transform.SetParent(marker.transform, false);
        center.transform.localPosition = new Vector3(0f, 0.58f, 0f);
        center.transform.localScale = new Vector3(0.58f, 0.35f, 0.58f);
        var centerCollider = center.GetComponent<Collider>();
        if (centerCollider != null) Destroy(centerCollider);
        var centerRenderer = center.GetComponent<Renderer>();
        if (centerRenderer != null)
        {
            centerRenderer.sharedMaterial = GetMarkerCenterMaterial();
            centerRenderer.sortingOrder = 21;
        }

    }

    void CreateStationNodeConnector(GameObject station, GridManager grid, Color color,
        string context)
    {
        if (station == null || grid == null) return;

        Vector3 flowNode = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(station));
        Vector3 placedTile = GetNearestPlacedTileCenter(station, grid, flowNode);
        placedTile.y = flowNode.y = grid.Origin.y + floorOffset;
        if ((flowNode - placedTile).sqrMagnitude < 0.001f) return;

        var points = new List<Vector3>(3) { placedTile };
        if (!Mathf.Approximately(placedTile.x, flowNode.x)
            && !Mathf.Approximately(placedTile.z, flowNode.z))
            points.Add(new Vector3(flowNode.x, placedTile.y, placedTile.z));
        points.Add(flowNode);

        CreateGridLine(points, "Station_Node_Connector_" + context + "_"
            + station.GetInstanceID(), color, showDirection: false);
    }

    public static Vector3 GetNearestPlacedTileCenter(GameObject station, GridManager grid,
        Vector3 flowNode)
    {
        BuildFootprint footprint = station.GetComponent<BuildFootprint>();
        PlacedBuildItem placedItem = station.GetComponent<PlacedBuildItem>();
        int sizeX = footprint != null ? Mathf.Max(1, footprint.sizeX)
            : placedItem != null && placedItem.itemDefinition != null
                ? Mathf.Max(1, placedItem.itemDefinition.footprintX) : 1;
        int sizeY = footprint != null ? Mathf.Max(1, footprint.sizeY)
            : placedItem != null && placedItem.itemDefinition != null
                ? Mathf.Max(1, placedItem.itemDefinition.footprintY) : 1;
        int yaw = Mathf.RoundToInt(station.transform.eulerAngles.y / 90f) & 3;
        if (yaw == 1 || yaw == 3)
        {
            int swap = sizeX;
            sizeX = sizeY;
            sizeY = swap;
        }

        if (grid.TryGetFootprintOriginFromCenter(station.transform.position,
                sizeX, sizeY, out int originX, out int originY))
        {
            Vector3 nearest = grid.CellToWorld(originX, originY);
            float nearestDistance = float.MaxValue;
            for (int x = originX; x < originX + sizeX; x++)
            for (int y = originY; y < originY + sizeY; y++)
            {
                Vector3 candidate = grid.CellToWorld(x, y);
                float distance = (candidate - flowNode).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = candidate;
                nearestDistance = distance;
            }
            return nearest;
        }

        return grid.GetCellCenter(station.transform.position);
    }

    Material GetMarkerMaterial(Color color)
    {
        if (Approximately(color, startColor))
            return startMaterial != null ? startMaterial : startMaterial = CreateMaterial(startColor);
        if (Approximately(color, endColor))
            return endMaterial != null ? endMaterial : endMaterial = CreateMaterial(endColor);
        return stopMaterial != null ? stopMaterial : stopMaterial = CreateMaterial(stopColor);
    }

    void CreateAssignmentLink(GameObject fromWorker, GameObject toStation)
    {
        Vector3 a = GetAnchor(fromWorker);
        Vector3 b = GetAnchor(toStation);
        float bridgeY = Mathf.Max(a.y, b.y) + raiseHeight;

        var points = new List<Vector3>
        {
            a,
            new Vector3(a.x, bridgeY, a.z),
            new Vector3(b.x, bridgeY, b.z),
            b
        };
        CreateGridLine(points, "WorkerLink_" + fromWorker.name + "_to_" + toStation.name, routeColor, false);
    }

    static string GetDisplayName(GameObject go)
    {
        StationNode node = StationNode.EnsureOn(go);
        return node != null ? node.DisplayName : go.name;
    }

    static Vector3 GetAnchor(GameObject go)
    {
        Bounds b = GetBounds(go);
        return new Vector3(b.center.x, b.max.y + 0.1f, b.center.z);
    }

    static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds bounds = new Bounds(go.transform.position, Vector3.one);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer || renderer.GetComponentInParent<Canvas>() != null)
                continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (found) return bounds;
        Collider col = go.GetComponentInChildren<Collider>();
        return col != null ? col.bounds : bounds;
    }

    Material GetLineMaterial()
    {
        if (lineMaterial == null)
            lineMaterial = CreateMaterial(Color.white);
        return lineMaterial;
    }

    void CreateSelectedItemPreview(GameObject station)
    {
        ItemDefinition item = GetSelectedStationItem(station);
        CreateSelectedItemPreview(station, item, 0f);
    }

    void DrawAllStationPreviews()
    {
        StationNode[] stations = FindObjectsByType<StationNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < stations.Length; i++)
        {
            StationNode station = stations[i];
            if (station == null || !station.gameObject.scene.IsValid()) continue;
            CreateSelectedItemPreview(station.gameObject);
        }
    }

    void CreateSelectedItemPreview(GameObject station, ItemDefinition item, float horizontalOffset)
    {
        if (item == null || item.prefab == null || visualsRoot == null) return;

        Texture thumbnail = ItemPreviewThumbnails.Get(item);
        if (thumbnail == null) return;

        var badge = new GameObject("FlowRecipePreview_" + station.name + "_" + item.name,
            typeof(RectTransform), typeof(Canvas));
        badge.transform.SetParent(visualsRoot, false);
        Canvas canvas = badge.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 30;
        RectTransform badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.sizeDelta = new Vector2(96f, 96f);
        badgeRect.localScale = Vector3.one * 0.006f;

        var borderObject = new GameObject("CircularBorder", typeof(RectTransform),
            typeof(UnityEngine.UI.Image));
        borderObject.transform.SetParent(badge.transform, false);
        RectTransform borderRect = borderObject.GetComponent<RectTransform>();
        borderRect.anchorMin = borderRect.anchorMax = new Vector2(0.5f, 0.5f);
        borderRect.sizeDelta = new Vector2(96f, 96f);
        UnityEngine.UI.Image border = borderObject.GetComponent<UnityEngine.UI.Image>();
        border.sprite = GetCircleSprite();
        border.color = new Color(0.36f, 0.78f, 0.58f, 1f);
        border.raycastTarget = false;

        var maskObject = new GameObject("CircularPreview", typeof(RectTransform),
            typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
        maskObject.transform.SetParent(borderObject.transform, false);
        RectTransform maskRect = maskObject.GetComponent<RectTransform>();
        maskRect.anchorMin = maskRect.anchorMax = new Vector2(0.5f, 0.5f);
        maskRect.sizeDelta = new Vector2(84f, 84f);
        UnityEngine.UI.Image maskImage = maskObject.GetComponent<UnityEngine.UI.Image>();
        maskImage.sprite = GetCircleSprite();
        maskImage.color = ItemPreviewThumbnails.BackgroundColor;
        maskImage.raycastTarget = false;
        maskObject.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = true;

        var imageObject = new GameObject("RecipeThumbnail", typeof(RectTransform),
            typeof(UnityEngine.UI.RawImage));
        imageObject.transform.SetParent(maskObject.transform, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
        UnityEngine.UI.RawImage image = imageObject.GetComponent<UnityEngine.UI.RawImage>();
        image.texture = thumbnail;
        image.color = Color.white;
        image.raycastTarget = false;

        Bounds stationBounds = GetBounds(station);
        Vector3 side = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        badgeRect.position = new Vector3(stationBounds.center.x,
            stationBounds.max.y + 0.75f, stationBounds.center.z) + side * horizontalOffset;
        recipePreviewBadges.Add(badge.transform);
    }

    void FaceRecipePreviewsTowardCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        for (int i = recipePreviewBadges.Count - 1; i >= 0; i--)
        {
            Transform badge = recipePreviewBadges[i];
            if (badge == null)
            {
                recipePreviewBadges.RemoveAt(i);
                continue;
            }
            badge.rotation = Quaternion.LookRotation(badge.position - cam.transform.position,
                cam.transform.up);
        }
    }

    Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int size = 64;
        circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "FlowRecipeCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 1f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        circleTexture.SetPixels32(pixels);
        circleTexture.Apply(false, true);
        circleSprite = Sprite.Create(circleTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        circleSprite.name = "FlowRecipeCircleSprite";
        return circleSprite;
    }

    static ItemDefinition GetSelectedStationItem(GameObject station)
    {
        if (station == null) return null;

        PantryStation pantry = station.GetComponent<PantryStation>();
        if (pantry != null) return pantry.selectedItem;

        FreezerStation freezer = station.GetComponent<FreezerStation>();
        if (freezer != null) return freezer.selectedItem;

        GrillStation grill = station.GetComponent<GrillStation>();
        if (grill != null) return grill.GetSelectedOutput();

        CuttingStation cutting = station.GetComponent<CuttingStation>();
        if (cutting != null)
        {
            CuttingRecipeDefinition recipe = cutting.GetSelectedRecipe();
            return recipe != null ? recipe.output : cutting.selectedProduct;
        }

        AssemblyStation assembly = station.GetComponent<AssemblyStation>();
        if (assembly != null)
        {
            AssemblyRecipeDefinition recipe = assembly.GetSelectedRecipe();
            return recipe != null ? recipe.output : assembly.selectedProduct;
        }

        ProductionManager production = ProductionManager.Instance;
        if (station.GetComponent<FryerStation>() != null)
            return production != null ? production.CookedPotatoItem : null;
        if (station.GetComponent<DrinkStation>() != null)
            return production != null && production.orderConfig != null
                ? production.orderConfig.drinkItem : null;
        return null;
    }

    Material GetLineOutlineMaterial()
    {
        if (lineOutlineMaterial == null)
            lineOutlineMaterial = CreateMaterial(routeOutlineColor);
        return lineOutlineMaterial;
    }

    Material GetMarkerCenterMaterial()
    {
        if (markerCenterMaterial == null)
            markerCenterMaterial = CreateMaterial(new Color(0.055f, 0.075f, 0.1f, 0.92f));
        return markerCenterMaterial;
    }

    Material GetArrowMaterial()
    {
        if (arrowMaterial == null)
            arrowMaterial = CreateMaterial(Color.white);
        return arrowMaterial;
    }

    static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default")
                        ?? Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        return material;
    }

    static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.001f
            && Mathf.Abs(a.g - b.g) < 0.001f
            && Mathf.Abs(a.b - b.b) < 0.001f;
    }

    void EnsureRoot()
    {
        if (visualsRoot != null) return;
        var go = new GameObject("WorkerFlowVisuals");
        go.transform.SetParent(transform, false);
        visualsRoot = go.transform;
    }

    void ClearVisuals()
    {
        lines.Clear();
        recipePreviewBadges.Clear();
        for (int i = 0; i < generatedMeshes.Count; i++)
            if (generatedMeshes[i] != null) Destroy(generatedMeshes[i]);
        generatedMeshes.Clear();
        if (visualsRoot == null) return;
        for (int i = visualsRoot.childCount - 1; i >= 0; i--)
            Destroy(visualsRoot.GetChild(i).gameObject);
    }

    static void DestroyMaterial(Material material)
    {
        if (material != null)
            Destroy(material);
    }
}
