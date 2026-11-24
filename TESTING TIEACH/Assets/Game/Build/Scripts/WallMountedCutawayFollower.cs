using System.Collections.Generic;
using UnityEngine;

/// <summary>Keeps independent wall displays aligned with the wall's camera cutaway.</summary>
[DefaultExecutionOrder(100)]
public sealed class WallMountedCutawayFollower : MonoBehaviour
{
    const float MaxWallDistance = 1.5f;

    CameraOcclusionWall wall;
    Vector3 restPosition;
    bool hasRestPosition;
    Transform artwork;
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Canvas> canvases = new List<Canvas>();
    readonly List<Collider> colliders = new List<Collider>();
    bool hiddenFromBehind;

    public Vector3 RestPosition => hasRestPosition ? restPosition : transform.position;
    public float CurrentDrop => wall != null ? wall.CutawayDropDistance : 0f;

    internal bool IsFrontVisible(Camera camera)
    {
        if (camera == null) return true;
        Vector3 towardCamera = camera.orthographic
            ? -camera.transform.forward : camera.transform.position - transform.position;
        return Vector3.Dot(-transform.forward, towardCamera) > 0f;
    }

    internal void SetArtwork(Transform visual) => artwork = visual;

    void Start()
    {
        CollectVisuals(transform);
        if (artwork != null && !artwork.IsChildOf(transform))
            CollectVisuals(artwork);
        UpdateFacingVisibility();
    }

    void CollectVisuals(Transform root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            if (!renderer.forceRenderingOff) renderers.Add(renderer);
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            if (canvas.enabled) canvases.Add(canvas);
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            if (collider.enabled) colliders.Add(collider);
    }

    void UpdateFacingVisibility()
    {
        Camera camera = Camera.main;
        if (camera == null) camera = CameraWallCutaway.Instance?.targetCamera;
        if (camera != null) SetHiddenFromBehind(!IsFrontVisible(camera));
    }

    void SetHiddenFromBehind(bool hidden)
    {
        if (hiddenFromBehind == hidden) return;
        hiddenFromBehind = hidden;
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.forceRenderingOff = hidden;
        foreach (Canvas canvas in canvases)
            if (canvas != null) canvas.enabled = !hidden;
        foreach (Collider collider in colliders)
            if (collider != null) collider.enabled = !hidden;
    }

    void OnEnable()
    {
        restPosition = transform.position;
        hasRestPosition = true;
        ResolveWall();
    }

    void LateUpdate()
    {
        if (!hasRestPosition) return;
        if (wall == null) ResolveWall();
        float drop = wall != null ? wall.CutawayDropDistance : 0f;
        Vector3 position = restPosition;
        position.y -= drop;
        transform.position = position;
        UpdateFacingVisibility();
    }

    void OnDisable()
    {
        SetHiddenFromBehind(false);
        if (hasRestPosition)
            transform.position = restPosition;
    }

    public void SetRestPosition(Vector3 position)
    {
        restPosition = position;
        hasRestPosition = true;
        if (wall == null) ResolveWall();
        position.y -= CurrentDrop;
        transform.position = position;
    }

    void ResolveWall()
    {
        CameraOcclusionWall[] walls = FindObjectsByType<CameraOcclusionWall>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float nearestDistance = MaxWallDistance;
        wall = null;
        foreach (CameraOcclusionWall candidate in walls)
        {
            if (candidate == null) continue;
            float distance = candidate.DistanceToWall(restPosition);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            wall = candidate;
        }
    }
}
