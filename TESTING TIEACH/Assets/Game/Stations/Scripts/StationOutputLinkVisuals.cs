using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws world lines from each station to its assigned output while in Manage mode.
/// </summary>
public class StationOutputLinkVisuals : MonoBehaviour
{
    public static StationOutputLinkVisuals Instance { get; private set; }

    public GameModeManager modeManager;

    [Header("Line look")]
    public Color lineColor = new Color(1f, 0.65f, 0.15f, 0.95f);
    public float lineWidth = 0.08f;
    [Tooltip("How high above the taller station the horizontal span runs")]
    public float raiseHeight = 1.75f;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    Material lineMaterial;
    Transform linesRoot;
    bool visible;

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
        bool show = modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage;
        if (show != visible)
        {
            visible = show;
            if (visible) Refresh();
            else SetLinesEnabled(false);
        }
        else if (visible)
        {
            // Keep endpoints updated if stations move
            RefreshPositionsOnly();
        }
    }

    public static void NotifyLinksChanged()
    {
        if (Instance != null && Instance.visible)
            Instance.Refresh();
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

        visible = true;
        var nodes = FindObjectsByType<StationNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var node in nodes)
        {
            if (node == null || node.outputTarget == null) continue;
            CreateLink(node.gameObject, node.outputTarget);
        }
    }

    void RefreshPositionsOnly()
    {
        // Rebuild is cheap enough for station counts; keep simple
        if (Time.frameCount % 15 == 0)
            Refresh();
    }

    void CreateLink(GameObject from, GameObject to)
    {
        Vector3 a = GetAnchor(from);
        Vector3 b = GetAnchor(to);

        // Up from input → across → straight down into output
        float bridgeY = Mathf.Max(a.y, b.y) + raiseHeight;
        Vector3 up = new Vector3(a.x, bridgeY, a.z);
        Vector3 across = new Vector3(b.x, bridgeY, b.z);

        var go = new GameObject("OutputLink_" + from.name + "_to_" + to.name);
        go.transform.SetParent(linesRoot, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 4;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth * 0.65f;
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
        return new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
    }

    static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
        var col = go.GetComponentInChildren<Collider>();
        if (col != null) return col.bounds;
        return new Bounds(go.transform.position, Vector3.one);
    }

    Material GetLineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        lineMaterial = new Material(shader);
        lineMaterial.color = lineColor;
        return lineMaterial;
    }

    void EnsureRoot()
    {
        if (linesRoot != null) return;
        var go = new GameObject("StationOutputLinks");
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
