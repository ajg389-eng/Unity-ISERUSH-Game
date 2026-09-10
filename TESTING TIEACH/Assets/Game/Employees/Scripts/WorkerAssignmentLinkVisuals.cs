using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws world lines from the selected worker to their assigned stations while in Manage mode.
/// </summary>
public class WorkerAssignmentLinkVisuals : MonoBehaviour
{
    public static WorkerAssignmentLinkVisuals Instance { get; private set; }

    public GameModeManager modeManager;

    [Header("Line look")]
    public Color lineColor = new Color(0.35f, 0.85f, 1f, 0.95f);
    public float lineWidth = 0.07f;
    [Tooltip("How high above the worker/station the bridge runs")]
    public float raiseHeight = 1.35f;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    Material lineMaterial;
    Transform linesRoot;
    bool visible;
    KitchenEmployee focusedWorker;
    StationNode focusedStation;

    void Awake()
    {
        Instance = this;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        EnsureRoot();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }

    void Update()
    {
        bool manage = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        bool show = manage && (focusedWorker != null || focusedStation != null);

        if (show != visible)
        {
            visible = show;
            if (visible) Refresh();
            else SetLinesEnabled(false);
        }
        else if (visible && Time.frameCount % 10 == 0)
        {
            Refresh();
        }
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
        Instance.ApplyFocus();
    }

    public static void SetFocusedStation(StationNode station)
    {
        if (Instance == null) return;

        Instance.focusedStation = station;
        Instance.focusedWorker = null;
        Instance.ApplyFocus();
    }

    public static void ClearFocus()
    {
        if (Instance == null) return;
        Instance.focusedWorker = null;
        Instance.focusedStation = null;
        Instance.visible = false;
        Instance.SetLinesEnabled(false);
    }

    void ApplyFocus()
    {
        if (focusedWorker == null && focusedStation == null)
        {
            visible = false;
            SetLinesEnabled(false);
            return;
        }

        visible = true;
        Refresh();
    }

    public void Refresh()
    {
        EnsureRoot();
        ClearLines();

        if (modeManager == null || modeManager.CurrentMode != GameModeManager.Mode.Manage)
        {
            visible = false;
            return;
        }

        if (focusedWorker != null && focusedWorker.operatedStations != null)
        {
            visible = true;
            foreach (var station in focusedWorker.operatedStations)
            {
                if (station == null) continue;
                CreateLink(focusedWorker.gameObject, station);
            }
            return;
        }

        if (focusedStation != null && focusedStation.assignedWorker != null)
        {
            visible = true;
            CreateLink(focusedStation.assignedWorker.gameObject, focusedStation.gameObject);
            return;
        }

        visible = false;
    }

    void CreateLink(GameObject fromWorker, GameObject toStation)
    {
        Vector3 a = GetAnchor(fromWorker);
        Vector3 b = GetAnchor(toStation);

        float bridgeY = Mathf.Max(a.y, b.y) + raiseHeight;
        Vector3 up = new Vector3(a.x, bridgeY, a.z);
        Vector3 across = new Vector3(b.x, bridgeY, b.z);

        var go = new GameObject("WorkerLink_" + fromWorker.name + "_to_" + toStation.name);
        go.transform.SetParent(linesRoot, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 4;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth * 0.7f;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.material = GetLineMaterial();
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        if (lr.material != null && lr.material.HasProperty("_Color"))
            lr.material.color = lineColor;
        if (lr.material != null && lr.material.HasProperty("_BaseColor"))
            lr.material.SetColor("_BaseColor", lineColor);

        lr.SetPosition(0, a);
        lr.SetPosition(1, up);
        lr.SetPosition(2, across);
        lr.SetPosition(3, b);

        lines.Add(lr);
    }

    Vector3 GetAnchor(GameObject go)
    {
        Bounds b = GetBounds(go);
        return new Vector3(b.center.x, b.max.y + 0.1f, b.center.z);
    }

    static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (renderers[i].GetComponentInParent<Canvas>() != null) continue;
                if (renderers[i] is LineRenderer) continue;
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        var col = go.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds;
        return new Bounds(go.transform.position, Vector3.one);
    }

    Material GetLineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        var shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
        lineMaterial = new Material(shader);
        lineMaterial.color = lineColor;
        return lineMaterial;
    }

    void EnsureRoot()
    {
        if (linesRoot != null) return;
        var go = new GameObject("WorkerAssignmentLinks");
        go.transform.SetParent(transform, false);
        linesRoot = go.transform;
    }

    void ClearLines()
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i] != null)
                Destroy(lines[i].gameObject);
        }
        lines.Clear();

        if (linesRoot != null)
        {
            for (int i = linesRoot.childCount - 1; i >= 0; i--)
                Destroy(linesRoot.GetChild(i).gameObject);
        }
    }

    void SetLinesEnabled(bool enabled)
    {
        foreach (var lr in lines)
        {
            if (lr != null) lr.enabled = enabled;
        }
        if (!enabled)
            ClearLines();
    }
}
