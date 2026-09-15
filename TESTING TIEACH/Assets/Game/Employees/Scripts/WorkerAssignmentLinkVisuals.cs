using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shows a selected worker's ordered station flow on the floor grid.
/// Station focus still uses the elevated worker-to-station assignment link.
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
    public float lineWidth = 0.14f;
    public float floorOffset = 0.08f;
    [Range(0.2f, 0.9f)] public float markerTileScale = 0.58f;

    [Header("Station assignment link")]
    [Tooltip("How high above the worker/station the assignment bridge runs.")]
    public float raiseHeight = 1.35f;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<TextMeshPro> labels = new List<TextMeshPro>();
    Material lineMaterial;
    Material startMaterial;
    Material stopMaterial;
    Material endMaterial;
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

    public static bool HasFocusedWorker => Instance != null && Instance.focusedWorker != null;

    void Awake()
    {
        Instance = this;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        EnsureRoot();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DestroyMaterial(lineMaterial);
        DestroyMaterial(startMaterial);
        DestroyMaterial(stopMaterial);
        DestroyMaterial(endMaterial);
    }

    void Update()
    {
        bool manage = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        bool show = manage && (focusedWorker != null || focusedStation != null || focusedFlow != null);

        if (show != visible)
        {
            visible = show;
            if (visible) Refresh();
            else ClearVisuals();
        }

        FaceLabelsTowardCamera();
    }

    public static void NotifyLinksChanged()
    {
        if (Instance != null)
            Instance.Refresh();
    }

    public static void SetFocusedWorker(KitchenEmployee worker)
    {
        if (Instance == null) return;
        Instance.focusedWorker = worker;
        Instance.focusedStation = null;
        Instance.focusedFlow = null;
        Instance.ApplyFocus();
    }

    public static void SetFocusedStation(StationNode station)
    {
        if (Instance == null) return;
        Instance.focusedStation = station;
        Instance.focusedWorker = null;
        Instance.focusedFlow = null;
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
        Instance.focusedFlow = null;
        Instance.visible = false;
        Instance.ClearVisuals();
    }

    void ApplyFocus()
    {
        visible = focusedWorker != null || focusedStation != null || focusedFlow != null;
        if (visible) Refresh();
        else ClearVisuals();
    }

    public void Refresh()
    {
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

        if (focusedStation != null && focusedStation.assignedWorker != null)
            CreateAssignmentLink(focusedStation.assignedWorker.gameObject, focusedStation.gameObject);
    }

    void DrawDraftFlow(ProductionFlowPlan flow)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || flow == null || flow.stations == null || flow.stations.Count == 0)
            return;

        var points = new List<Vector3>();
        int drawn = 0;
        for (int i = 0; i < flow.stations.Count; i++)
        {
            GameObject station = flow.stations[i];
            if (station == null) continue;

            Vector3 position = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(station));
            position.y = grid.Origin.y + floorOffset;
            Color color = drawn == 0
                ? startColor
                : (i == flow.stations.Count - 1 || IsLastNonNullStation(flow, i) ? endColor : stopColor);
            CreateMarker(position, (drawn + 1) + "\n" + GetDisplayName(station), color, grid.cellSize);

            if (drawn > 0)
            {
                GameObject previousStation = null;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (flow.stations[j] != null)
                    {
                        previousStation = flow.stations[j];
                        break;
                    }
                }
                if (previousStation != null)
                {
                    Vector3 previous = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(previousStation));
                    AppendGridLeg(points, grid, previous, position);
                }
            }
            drawn++;
        }

        if (points.Count >= 2)
            CreateGridLine(points, (string.IsNullOrEmpty(flow.flowName) ? "Flow" : flow.flowName) + "_Draft", routeColor);
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
                bool isHandoff = isEnd && stopNode != null && stopNode.IsWorkStation && stopNode.assignedWorker != worker;
                string role = isHandoff ? "HANDOFF" : isStart && isEnd ? "START / END" : isStart ? "START" : isEnd ? "END" : "STOP " + i;
                Color markerColor = isStart ? startColor : isEnd ? endColor : stopColor;
                string owner = showOwner ? worker.employeeName + "\n" : "";
                CreateMarker(stopPosition, owner + role + "\n" + GetDisplayName(chain[i]), markerColor, grid.cellSize);

                if (i == 0) continue;
                Vector3 previous = grid.GetCellCenter(KitchenEmployee.GetInteractionPosition(chain[i - 1]));
                AppendGridLeg(routePoints, grid, previous, stopPosition);
            }

            if (routePoints.Count >= 2)
                CreateGridLine(routePoints, worker.employeeName + "_Flow_" + chainIndex, workerColor);
        }
    }

    static List<List<GameObject>> BuildFlowChains(KitchenEmployee worker)
    {
        var assigned = new List<GameObject>();
        foreach (GameObject station in worker.operatedStations)
            if (station != null && !assigned.Contains(station))
                assigned.Add(station);

        var targetedAssignedStations = new HashSet<GameObject>();
        foreach (GameObject station in assigned)
        {
            StationNode node = StationNode.EnsureOn(station);
            if (node != null && node.outputTarget != null && assigned.Contains(node.outputTarget))
                targetedAssignedStations.Add(node.outputTarget);
        }

        var heads = new List<GameObject>();
        foreach (GameObject station in assigned)
            if (!targetedAssignedStations.Contains(station))
                heads.Add(station);
        if (heads.Count == 0 && assigned.Count > 0)
            heads.Add(assigned[0]);

        var chains = new List<List<GameObject>>();
        var globallyVisited = new HashSet<GameObject>();
        foreach (GameObject head in heads)
            AddChain(worker, head, chains, globallyVisited);
        foreach (GameObject station in assigned)
            if (!globallyVisited.Contains(station))
                AddChain(worker, station, chains, globallyVisited);
        return chains;
    }

    static void AddChain(KitchenEmployee worker, GameObject head, List<List<GameObject>> chains, HashSet<GameObject> globallyVisited)
    {
        var chain = new List<GameObject>();
        var chainVisited = new HashSet<GameObject>();
        GameObject current = head;

        while (current != null && chainVisited.Add(current))
        {
            chain.Add(current);
            globallyVisited.Add(current);
            StationNode node = StationNode.EnsureOn(current);
            GameObject next = node != null ? node.outputTarget : null;
            StationNode nextNode = next != null ? StationNode.EnsureOn(next) : null;
            if (nextNode != null && nextNode.IsWorkStation && nextNode.assignedWorker != worker)
            {
                chain.Add(next);
                break;
            }
            current = next;
        }

        if (chain.Count > 0)
            chains.Add(chain);
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

    void CreateGridLine(List<Vector3> points, string routeName, Color color)
    {
        var go = new GameObject(routeName);
        go.transform.SetParent(visualsRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.numCapVertices = 5;
        lr.numCornerVertices = 5;
        lr.material = GetLineMaterial();
        lr.startColor = color;
        lr.endColor = color;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lines.Add(lr);
    }

    void CreateMarker(Vector3 position, string text, Color color, float cellSize)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "FlowMarker_" + text.Replace("\n", "_");
        marker.transform.SetParent(visualsRoot, true);
        marker.transform.position = position;
        float diameter = Mathf.Max(0.18f, cellSize * markerTileScale);
        marker.transform.localScale = new Vector3(diameter, 0.025f, diameter);
        var collider = marker.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = GetMarkerMaterial(color);

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(visualsRoot, false);
        labelObject.transform.position = position + Vector3.up * 0.18f;
        var label = labelObject.AddComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = 2.1f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(5f, 1.4f);
        labels.Add(label);
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
        CreateGridLine(points, "WorkerLink_" + fromWorker.name + "_to_" + toStation.name, routeColor);
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
