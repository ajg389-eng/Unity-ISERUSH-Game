using UnityEngine;

/// <summary>Keeps independent wall displays aligned with the wall's camera cutaway.</summary>
[DefaultExecutionOrder(100)]
public sealed class WallMountedCutawayFollower : MonoBehaviour
{
    const float MaxWallDistance = 1.5f;

    CameraOcclusionWall wall;
    Vector3 restPosition;
    bool hasRestPosition;

    public Vector3 RestPosition => hasRestPosition ? restPosition : transform.position;
    public float CurrentDrop => wall != null ? wall.CutawayDropDistance : 0f;

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
    }

    void OnDisable()
    {
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
