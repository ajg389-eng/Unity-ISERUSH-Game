using System.Collections.Generic;
using TMPro;
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
    readonly List<TextMeshPro> labels = new List<TextMeshPro>();
    readonly List<Transform> recipePreviewBadges = new List<Transform>();
    readonly List<Mesh> generatedMeshes = new List<Mesh>();
    readonly List<WorkerHoverHighlight> flowWorkerHighlights = new List<WorkerHoverHighlight>();
    Material lineMaterial;
    Material lineOutlineMaterial;
    Material markerCenterMaterial;
    Material labelBackgroundMaterial;
    Material arrowMaterial;
    Material arrowOutlineMaterial;
    Material startMaterial;
    Material stopMaterial;
    Material endMaterial;
    Texture2D circleTexture;
    Sprite circleSprite;
    Transform visualsRoot;
    bool visible;
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
        DestroyMaterial(labelBackgroundMaterial);
        DestroyMaterial(arrowMaterial);
        DestroyMaterial(arrowOutlineMaterial);
        DestroyMaterial(startMaterial);
        DestroyMaterial(stopMaterial);
        DestroyMaterial(endMaterial);
        if (circleSprite != null) Destroy(circleSprite);
        if (circleTexture != null) Destroy(circleTexture);
    }

    void Update()
    {
        bool manage = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        bool show = manage && (focusedWorker != null || focusedFlow != null);

        if (show != visible)
        {
            visible = show;
            if (visible) Refresh();
            else ClearVisuals();
        }

        FaceLabelsTowardCamera();
        FaceRecipePreviewsTowardCamera();
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
        if (Instance.focusedFlow != null)
        {
            Instance.focusedWorker = null;
            Instance.focusedStation = null;
            Instance.ApplyFocus();
            return;
        }
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
        Instance.visible = false;
        Instance.ClearVisuals();
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
        visible = focusedWorker != null || focusedFlow != null;
        if (visible) Refresh();
        else
        {
            RefreshFlowWorkerHighlights();
            ClearVisuals();
        }
    }

    public void Refresh()
    {
        RefreshFlowWorkerHighlights();
        EnsureRoot();
        ClearVisuals();

        if (modeManager == null || modeManager.CurrentMode != GameModeManager.Mode.Manage)
        {
            visible = false;
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

            Vector3 position = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(station));
            position.y = grid.Origin.y + floorOffset;
            bool root = !incoming.Contains(station);
            bool end = !outgoing.Contains(station);
            Color color = root ? startColor : end ? endColor : stopColor;
            bool split = outgoingCount.TryGetValue(station, out int branches) && branches > 1;
            bool merge = incomingCount.TryGetValue(station, out int sources) && sources > 1;
            string role = root ? "START" : end ? "END" : split ? "SPLIT" : merge ? "MERGE" : "STOP";
            CreateMarker(position, role, GetDisplayName(station), color, grid.cellSize);
            CreateSelectedItemPreview(station);
        }

        var edgePaths = new List<List<Vector3>>();
        foreach (ProductionFlowConnection connection in flow.connections)
        {
            if (connection == null || connection.from == null || connection.to == null) continue;
            var points = new List<Vector3>();
            Vector3 from = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(connection.from));
            Vector3 to = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(connection.to));
            AppendGridLeg(points, grid, from, to);
            if (points.Count >= 2) edgePaths.Add(points);
        }

        Dictionary<int, List<int>> overlapGroups = BuildPathOverlapGroups(edgePaths, grid.cellSize);
        float laneSpacing = lineWidth + lineOutlineWidth + 0.035f;
        for (int edgeIndex = 0; edgeIndex < edgePaths.Count; edgeIndex++)
        {
            List<Vector3> points = ApplySharedLaneOffsets(edgePaths[edgeIndex], edgeIndex,
                overlapGroups, grid.cellSize, laneSpacing);
            Color branchColor = FlowBranchColors[edgeIndex % FlowBranchColors.Length];
            CreateGridLine(points, (string.IsNullOrEmpty(flow.flowName) ? "Flow" : flow.flowName)
                + "_Branch_" + edgeIndex, branchColor);
        }
    }

    static Dictionary<int, List<int>> BuildPathOverlapGroups(List<List<Vector3>> paths,
        float cellSize)
    {
        var adjacent = new List<HashSet<int>>(paths.Count);
        for (int i = 0; i < paths.Count; i++) adjacent.Add(new HashSet<int>());
        float corridorTolerance = Mathf.Max(0.01f, cellSize * 0.04f);
        float minimumSharedLength = Mathf.Max(0.04f, cellSize * 0.12f);
        for (int first = 0; first < paths.Count; first++)
            for (int second = first + 1; second < paths.Count; second++)
                if (PathsShareCorridor(paths[first], paths[second], corridorTolerance,
                        minimumSharedLength))
                {
                    adjacent[first].Add(second);
                    adjacent[second].Add(first);
                }

        var groups = new Dictionary<int, List<int>>();
        var visited = new HashSet<int>();
        for (int start = 0; start < paths.Count; start++)
        {
            if (!visited.Add(start)) continue;
            var group = new List<int>();
            var open = new Stack<int>();
            open.Push(start);
            while (open.Count > 0)
            {
                int current = open.Pop();
                group.Add(current);
                foreach (int next in adjacent[current])
                    if (visited.Add(next)) open.Push(next);
            }
            group.Sort();
            foreach (int member in group) groups[member] = group;
        }
        return groups;
    }

    static bool PathsShareCorridor(List<Vector3> first, List<Vector3> second,
        float tolerance, float minimumSharedLength)
    {
        if (first == null || second == null) return false;
        for (int a = 1; a < first.Count; a++)
            for (int b = 1; b < second.Count; b++)
                if (SegmentsShareCorridor(first[a - 1], first[a], second[b - 1], second[b],
                        tolerance, minimumSharedLength))
                    return true;
        return false;
    }

    static bool SegmentsShareCorridor(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1,
        float tolerance, float minimumSharedLength)
    {
        Vector2 aStart = new Vector2(a0.x, a0.z);
        Vector2 aEnd = new Vector2(a1.x, a1.z);
        Vector2 bStart = new Vector2(b0.x, b0.z);
        Vector2 bEnd = new Vector2(b1.x, b1.z);
        Vector2 aDelta = aEnd - aStart;
        Vector2 bDelta = bEnd - bStart;
        float aLength = aDelta.magnitude;
        float bLength = bDelta.magnitude;
        if (aLength <= 0.001f || bLength <= 0.001f) return false;

        Vector2 aDirection = aDelta / aLength;
        Vector2 bDirection = bDelta / bLength;
        if (Mathf.Abs(Vector2.Dot(aDirection, bDirection)) < 0.999f) return false;

        Vector2 normal = new Vector2(-aDirection.y, aDirection.x);
        if (Mathf.Abs(Vector2.Dot(bStart - aStart, normal)) > tolerance
            || Mathf.Abs(Vector2.Dot(bEnd - aStart, normal)) > tolerance)
            return false;

        float bProjection0 = Vector2.Dot(bStart - aStart, aDirection);
        float bProjection1 = Vector2.Dot(bEnd - aStart, aDirection);
        float bMin = Mathf.Min(bProjection0, bProjection1);
        float bMax = Mathf.Max(bProjection0, bProjection1);
        float overlap = Mathf.Min(aLength, bMax) - Mathf.Max(0f, bMin);
        return overlap >= minimumSharedLength;
    }

    static List<Vector3> ApplySharedLaneOffsets(List<Vector3> source, int pathIndex,
        Dictionary<int, List<int>> overlapGroups, float cellSize, float spacing)
    {
        float laneDistance = GetPathLaneDistance(pathIndex, overlapGroups, spacing);
        var result = new List<Vector3>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            if (Mathf.Abs(laneDistance) <= 0.000001f)
            {
                result.Add(source[i]);
                continue;
            }
            Vector3 previousOffset = Vector3.zero;
            Vector3 nextOffset = Vector3.zero;
            if (i > 0)
                previousOffset = GetConsistentLaneOffset(
                    source[i - 1], source[i], cellSize, laneDistance);
            if (i + 1 < source.Count)
                nextOffset = GetConsistentLaneOffset(
                    source[i], source[i + 1], cellSize, laneDistance);

            Vector3 offset;
            if (previousOffset.sqrMagnitude <= 0.000001f) offset = nextOffset;
            else if (nextOffset.sqrMagnitude <= 0.000001f) offset = previousOffset;
            else if (Vector3.Dot(previousOffset.normalized, nextOffset.normalized) > 0.999f)
                offset = (previousOffset + nextOffset) * 0.5f;
            else
                // The intersection of two perpendicular offset grid segments is
                // the sum of their normals. Averaging here creates off-angle kinks.
                offset = previousOffset + nextOffset;
            result.Add(source[i] + offset);
        }
        // Lane separation is corridor-only. Every edge must still originate and
        // terminate at the exact center of its station marker.
        result[0] = source[0];
        result[result.Count - 1] = source[source.Count - 1];
        return PolishRoute(CardinalizePolyline(result), cellSize);
    }

    static List<Vector3> PolishRoute(List<Vector3> source, float cellSize)
    {
        if (source == null || source.Count < 2) return source;
        var simplified = new List<Vector3>();
        foreach (Vector3 point in source)
        {
            AddPointIfDistinct(simplified, point);
            while (simplified.Count >= 3)
            {
                int last = simplified.Count - 1;
                Vector3 before = simplified[last - 1] - simplified[last - 2];
                Vector3 after = simplified[last] - simplified[last - 1];
                before.y = 0f;
                after.y = 0f;
                if (before.sqrMagnitude <= 0.000001f || after.sqrMagnitude <= 0.000001f
                    || Vector3.Dot(before.normalized, after.normalized) < 0.9999f)
                    break;
                Vector3 end = simplified[last];
                simplified.RemoveAt(last);
                simplified[last - 1] = end;
            }
        }
        if (simplified.Count < 3) return simplified;

        float standardChamfer = Mathf.Max(0.04f, cellSize * 0.16f);
        var polished = new List<Vector3> { simplified[0] };
        for (int i = 1; i < simplified.Count - 1; i++)
        {
            Vector3 previous = simplified[i - 1];
            Vector3 corner = simplified[i];
            Vector3 next = simplified[i + 1];
            Vector3 incoming = corner - previous;
            Vector3 outgoing = next - corner;
            incoming.y = 0f;
            outgoing.y = 0f;
            float incomingLength = incoming.magnitude;
            float outgoingLength = outgoing.magnitude;
            if (incomingLength <= 0.001f || outgoingLength <= 0.001f
                || Mathf.Abs(Vector3.Dot(incoming.normalized, outgoing.normalized)) > 0.999f
                || !IsCardinalDirection(incoming) || !IsCardinalDirection(outgoing))
            {
                AddPointIfDistinct(polished, corner);
                continue;
            }

            float chamfer = Mathf.Min(standardChamfer,
                incomingLength * 0.35f, outgoingLength * 0.35f);
            AddPointIfDistinct(polished, corner - incoming.normalized * chamfer);
            AddPointIfDistinct(polished, corner + outgoing.normalized * chamfer);
        }
        AddPointIfDistinct(polished, simplified[simplified.Count - 1]);
        return polished;
    }

    static bool IsCardinalDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.000001f) return false;
        direction.Normalize();
        return Mathf.Abs(direction.x) < 0.001f || Mathf.Abs(direction.z) < 0.001f;
    }

    static float GetPathLaneDistance(int pathIndex,
        Dictionary<int, List<int>> overlapGroups, float spacing)
    {
        if (!overlapGroups.TryGetValue(pathIndex, out List<int> group) || group.Count < 2)
            return 0f;
        int lane = group.IndexOf(pathIndex);
        if (lane < 0) return 0f;
        return (lane - (group.Count - 1) * 0.5f) * spacing;
    }

    static Vector3 GetConsistentLaneOffset(Vector3 a, Vector3 b, float cellSize,
        float laneDistance)
    {
        Vector3 direction = CanonicalSegmentDirection(a, b, cellSize);
        return new Vector3(-direction.z, 0f, direction.x).normalized * laneDistance;
    }

    static List<Vector3> CardinalizePolyline(List<Vector3> source)
    {
        if (source == null || source.Count < 2) return source;
        var result = new List<Vector3> { source[0] };
        const float epsilon = 0.001f;
        for (int i = 1; i < source.Count; i++)
        {
            Vector3 from = result[result.Count - 1];
            Vector3 to = source[i];
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float ax = Mathf.Abs(dx);
            float az = Mathf.Abs(dz);
            if (ax > epsilon && az > epsilon && Mathf.Abs(ax - az) > epsilon)
            {
                float diagonal = Mathf.Min(ax, az);
                Vector3 bend = new Vector3(from.x + Mathf.Sign(dx) * diagonal,
                    Mathf.Lerp(from.y, to.y, 0.5f),
                    from.z + Mathf.Sign(dz) * diagonal);
                AddPointIfDistinct(result, bend);
            }
            AddPointIfDistinct(result, to);
        }
        return result;
    }

    static Vector3 CanonicalSegmentDirection(Vector3 a, Vector3 b, float cellSize)
    {
        float scale = Mathf.Max(0.01f, cellSize);
        var first = new Vector2Int(Mathf.RoundToInt(a.x / scale), Mathf.RoundToInt(a.z / scale));
        var second = new Vector2Int(Mathf.RoundToInt(b.x / scale), Mathf.RoundToInt(b.z / scale));
        bool reverse = first.x > second.x || (first.x == second.x && first.y > second.y);
        Vector3 direction = reverse ? a - b : b - a;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
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
        var workerPaths = new List<List<Vector3>>();
        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            List<GameObject> chain = chains[chainIndex];
            if (chain.Count == 0) continue;

            var routePoints = new List<Vector3>();
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

                if (i == 0) continue;
                Vector3 previous = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(chain[i - 1]));
                AppendGridLeg(routePoints, grid, previous, stopPosition);
            }

            if (routePoints.Count >= 2) workerPaths.Add(routePoints);
        }

        Dictionary<int, List<int>> overlapGroups = BuildPathOverlapGroups(workerPaths, grid.cellSize);
        float laneSpacing = lineWidth + lineOutlineWidth + 0.035f;
        for (int pathIndex = 0; pathIndex < workerPaths.Count; pathIndex++)
        {
            List<Vector3> points = ApplySharedLaneOffsets(workerPaths[pathIndex], pathIndex,
                overlapGroups, grid.cellSize, laneSpacing);
            CreateGridLine(points, worker.employeeName + "_Flow_" + pathIndex, workerColor);
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

    void CreateGridLine(List<Vector3> points, string routeName, Color color, bool showDirection = true)
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
            CreateDirectionArrow(points, routeName + "_Direction", color);
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

        float totalLength = 0f;
        for (int i = 1; i < points.Count; i++)
            totalLength += Vector3.Distance(points[i - 1], points[i]);
        if (totalLength < 0.2f) return;

        int bestSegment = 1;
        float bestScore = -1f;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 segment = points[i] - points[i - 1];
            segment.y = 0f;
            float length = segment.magnitude;
            if (length < 0.01f) continue;
            float endpointPenalty = i == 1 || i == points.Count - 1 ? 0.72f : 1f;
            float score = length * endpointPenalty;
            if (score > bestScore)
            {
                bestScore = score;
                bestSegment = i;
            }
        }
        Vector3 position = Vector3.Lerp(points[bestSegment - 1], points[bestSegment], 0.5f);
        Vector3 direction = points[bestSegment] - points[bestSegment - 1];
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;

        float cellSize = GridManager.Instance != null ? GridManager.Instance.cellSize : 1f;
        CreateArrowMesh(arrowName + "_Outline", position + Vector3.up * 0.004f, direction,
            cellSize * (arrowTileScale + 0.06f), GetArrowOutlineMaterial());
        CreateArrowMesh(arrowName, position + Vector3.up * 0.012f, direction,
            cellSize * arrowTileScale, GetArrowMaterial(color));
    }

    void CreateArrowMesh(string objectName, Vector3 position, Vector3 direction, float size, Material material)
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
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateNormals();
        generatedMeshes.Add(mesh);
        arrow.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = arrow.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 5;
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

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(visualsRoot, false);
        labelObject.transform.position = position + Vector3.up * 0.26f;
        var label = labelObject.AddComponent<TextMeshPro>();
        string roleHex = ColorUtility.ToHtmlStringRGB(color);
        string displayName = stationName.Replace("\n", " · ");
        label.text = "<color=#" + roleHex + "><b>" + role + "</b></color>  " + displayName;
        label.fontSize = 1.45f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontStyle = FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.outlineColor = new Color32(8, 12, 18, 235);
        label.outlineWidth = 0.24f;
        label.rectTransform.sizeDelta = new Vector2(4.2f, 0.65f);
        label.sortingOrder = 42;

        var background = new GameObject("LabelBackground", typeof(MeshFilter), typeof(MeshRenderer));
        background.name = "LabelBackground";
        background.transform.SetParent(labelObject.transform, false);
        background.transform.localPosition = new Vector3(0f, 0f, 0.025f);
        float labelWidth = Mathf.Clamp(0.48f + (role.Length + displayName.Length) * 0.067f, 1.05f, 3.25f);
        const float labelHeight = 0.31f;
        Mesh capsuleMesh = CreateCapsuleMesh(labelWidth, labelHeight, 10);
        generatedMeshes.Add(capsuleMesh);
        background.GetComponent<MeshFilter>().sharedMesh = capsuleMesh;
        var backgroundRenderer = background.GetComponent<MeshRenderer>();
        if (backgroundRenderer != null)
        {
            backgroundRenderer.sharedMaterial = GetLabelBackgroundMaterial();
            backgroundRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backgroundRenderer.receiveShadows = false;
            backgroundRenderer.sortingOrder = 41;
        }
        labels.Add(label);
    }

    static Mesh CreateCapsuleMesh(float width, float height, int arcSegments)
    {
        float radius = height * 0.5f;
        float straightHalfWidth = Mathf.Max(0f, width * 0.5f - radius);
        int safeSegments = Mathf.Max(3, arcSegments);
        var perimeter = new List<Vector3>((safeSegments + 1) * 2);

        for (int i = 0; i <= safeSegments; i++)
        {
            float angle = Mathf.Lerp(-90f, 90f, i / (float)safeSegments) * Mathf.Deg2Rad;
            perimeter.Add(new Vector3(straightHalfWidth + Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius, 0f));
        }
        for (int i = 0; i <= safeSegments; i++)
        {
            float angle = Mathf.Lerp(90f, 270f, i / (float)safeSegments) * Mathf.Deg2Rad;
            perimeter.Add(new Vector3(-straightHalfWidth + Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius, 0f));
        }

        var vertices = new Vector3[perimeter.Count + 1];
        var uv = new Vector2[vertices.Length];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < perimeter.Count; i++)
        {
            vertices[i + 1] = perimeter[i];
            uv[i + 1] = new Vector2(perimeter[i].x / width + 0.5f,
                perimeter[i].y / height + 0.5f);
        }

        var triangles = new int[perimeter.Count * 3];
        for (int i = 0; i < perimeter.Count; i++)
        {
            int next = (i + 1) % perimeter.Count;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = next + 1;
        }

        var mesh = new Mesh { name = "FlowLabelCapsule" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
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

    void FaceLabelsTowardCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        for (int i = 0; i < labels.Count; i++)
        {
            if (labels[i] == null) continue;
            labels[i].transform.rotation = Quaternion.LookRotation(labels[i].transform.position - cam.transform.position, cam.transform.up);
        }
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
        badgeRect.position = new Vector3(stationBounds.center.x,
            stationBounds.max.y + 0.75f, stationBounds.center.z);
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

    Material GetLabelBackgroundMaterial()
    {
        if (labelBackgroundMaterial == null)
            labelBackgroundMaterial = CreateMaterial(new Color(0.035f, 0.05f, 0.075f, 0.9f));
        return labelBackgroundMaterial;
    }

    Material GetArrowMaterial(Color color)
    {
        if (arrowMaterial == null)
            arrowMaterial = CreateMaterial(color);
        arrowMaterial.color = color;
        if (arrowMaterial.HasProperty("_BaseColor")) arrowMaterial.SetColor("_BaseColor", color);
        return arrowMaterial;
    }

    Material GetArrowOutlineMaterial()
    {
        if (arrowOutlineMaterial == null)
            arrowOutlineMaterial = CreateMaterial(routeOutlineColor);
        return arrowOutlineMaterial;
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
        labels.Clear();
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
