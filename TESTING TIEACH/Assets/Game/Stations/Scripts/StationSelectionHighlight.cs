using UnityEngine;

/// <summary>
/// Shows a framed box highlight around a station while it is selected in Manage mode.
/// </summary>
public class StationSelectionHighlight : MonoBehaviour
{
    [Header("Look")]
    public Color highlightColor = new Color(0.25f, 0.78f, 1f, 0.95f);
    public float padding = 0.05f;
    public float yLift = 0.04f;
    public float lineWidth = 0.045f;

    const int EdgeCount = 12;

    GameObject highlightRoot;
    LineRenderer[] edges;
    Material lineMaterial;
    bool selected;

    public static StationSelectionHighlight EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var highlight = go.GetComponent<StationSelectionHighlight>();
        if (highlight == null)
            highlight = go.AddComponent<StationSelectionHighlight>();
        return highlight;
    }

    public void SetSelected(bool on)
    {
        if (selected == on) return;
        selected = on;

        if (!on)
        {
            if (highlightRoot != null)
                highlightRoot.SetActive(false);
            return;
        }

        EnsureHighlightObject();
        UpdateHighlightTransform();
        highlightRoot.SetActive(true);
    }

    void EnsureHighlightObject()
    {
        if (highlightRoot != null && edges != null) return;

        if (highlightRoot != null)
        {
            Destroy(highlightRoot);
            highlightRoot = null;
            edges = null;
        }

        highlightRoot = new GameObject("SelectionHighlight");
        highlightRoot.transform.SetParent(transform, false);

        edges = new LineRenderer[EdgeCount];
        for (int i = 0; i < EdgeCount; i++)
        {
            var edgeGo = new GameObject("Edge_" + i);
            edgeGo.transform.SetParent(highlightRoot.transform, false);

            var line = edgeGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = GetLineMaterial();
            line.startColor = highlightColor;
            line.endColor = highlightColor;
            line.textureMode = LineTextureMode.Stretch;
            edges[i] = line;
        }
    }

    Material GetLineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;

        var shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
        lineMaterial = new Material(shader);
        lineMaterial.color = highlightColor;
        return lineMaterial;
    }

    void UpdateHighlightTransform()
    {
        if (highlightRoot == null || edges == null) return;

        if (!TryGetLocalBounds(out Bounds localBounds))
            localBounds = new Bounds(Vector3.zero, Vector3.one);

        Vector3 size = localBounds.size + Vector3.one * padding * 2f;
        size.y = Mathf.Max(size.y, 0.2f);

        highlightRoot.transform.localPosition = localBounds.center + Vector3.up * yLift;
        highlightRoot.transform.localRotation = Quaternion.identity;
        highlightRoot.transform.localScale = Vector3.one;

        Vector3 half = size * 0.5f;
        SetEdge(edges[0], new Vector3(-half.x, -half.y, -half.z), new Vector3( half.x, -half.y, -half.z));
        SetEdge(edges[1], new Vector3(-half.x, -half.y,  half.z), new Vector3( half.x, -half.y,  half.z));
        SetEdge(edges[2], new Vector3(-half.x, -half.y, -half.z), new Vector3(-half.x, -half.y,  half.z));
        SetEdge(edges[3], new Vector3( half.x, -half.y, -half.z), new Vector3( half.x, -half.y,  half.z));
        SetEdge(edges[4], new Vector3(-half.x,  half.y, -half.z), new Vector3( half.x,  half.y, -half.z));
        SetEdge(edges[5], new Vector3(-half.x,  half.y,  half.z), new Vector3( half.x,  half.y,  half.z));
        SetEdge(edges[6], new Vector3(-half.x,  half.y, -half.z), new Vector3(-half.x,  half.y,  half.z));
        SetEdge(edges[7], new Vector3( half.x,  half.y, -half.z), new Vector3( half.x,  half.y,  half.z));
        SetEdge(edges[8], new Vector3(-half.x, -half.y, -half.z), new Vector3(-half.x,  half.y, -half.z));
        SetEdge(edges[9], new Vector3( half.x, -half.y, -half.z), new Vector3( half.x,  half.y, -half.z));
        SetEdge(edges[10], new Vector3(-half.x, -half.y,  half.z), new Vector3(-half.x,  half.y,  half.z));
        SetEdge(edges[11], new Vector3( half.x, -half.y,  half.z), new Vector3( half.x,  half.y,  half.z));
    }

    static void SetEdge(LineRenderer line, Vector3 start, Vector3 end)
    {
        if (line == null) return;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    bool TryGetLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        bool hasBounds = false;

        var renderers = GetComponentsInChildren<Renderer>(false);
        foreach (var renderer in renderers)
        {
            if (renderer == null || ShouldSkipRenderer(renderer)) continue;

            var corners = GetBoundsCorners(renderer.bounds);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = transform.InverseTransformPoint(corners[i]);
                if (!hasBounds)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }

        return hasBounds;
    }

    bool ShouldSkipRenderer(Renderer renderer)
    {
        if (IsHighlightRenderer(renderer)) return true;
        if (IsUiRenderer(renderer)) return true;
        if (renderer is LineRenderer) return true;
        if (!renderer.gameObject.activeInHierarchy) return true;
        return IsInteractionHighlightRenderer(renderer);
    }

    bool IsInteractionHighlightRenderer(Renderer renderer)
    {
        var tiles = GetComponent<StationInteractionTiles>();
        if (tiles == null || tiles.buildModeHighlight == null) return false;

        var highlight = tiles.buildModeHighlight.transform;
        return renderer.transform == highlight || renderer.transform.IsChildOf(highlight);
    }

    bool IsHighlightRenderer(Renderer renderer)
    {
        return highlightRoot != null && renderer.transform.IsChildOf(highlightRoot.transform);
    }

    static bool IsUiRenderer(Renderer renderer)
    {
        return renderer.GetComponentInParent<Canvas>() != null;
    }

    static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        return new[]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x,  e.y,  e.z),
        };
    }

    void OnDestroy()
    {
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }
}
