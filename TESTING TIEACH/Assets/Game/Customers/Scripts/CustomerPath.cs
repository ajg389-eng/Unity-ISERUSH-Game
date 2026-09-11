using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-authored waypoint path for customers (e.g. outside → door → inside).
/// Create empty children as waypoints, or assign Transforms in the list.
/// </summary>
public class CustomerPath : MonoBehaviour
{
    [Tooltip("Ordered waypoints. If empty, uses this object's children in hierarchy order.")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("Draw the path in the Scene view.")]
    public bool drawGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.75f, 1f, 0.9f);

    public int Count => GetValidWaypointCount();

    public Vector3 GetSpawnPosition()
    {
        var first = GetFirstValid();
        return first != null ? first.position : transform.position;
    }

    public void GetWorldPoints(List<Vector3> into)
    {
        if (into == null) return;
        into.Clear();
        RebuildFromChildrenIfNeeded();
        if (waypoints == null) return;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] != null)
                into.Add(waypoints[i].position);
        }
    }

    Transform GetFirstValid()
    {
        RebuildFromChildrenIfNeeded();
        if (waypoints == null) return null;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] != null)
                return waypoints[i];
        }
        return null;
    }

    int GetValidWaypointCount()
    {
        RebuildFromChildrenIfNeeded();
        if (waypoints == null) return 0;
        int n = 0;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] != null) n++;
        }
        return n;
    }

    public void RebuildFromChildrenIfNeeded()
    {
        if (waypoints == null)
            waypoints = new List<Transform>();

        bool anyValid = false;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] != null)
            {
                anyValid = true;
                break;
            }
        }

        if (anyValid) return;
        waypoints.Clear();
        for (int i = 0; i < transform.childCount; i++)
            waypoints.Add(transform.GetChild(i));
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        RebuildFromChildrenIfNeeded();
        if (waypoints == null || waypoints.Count == 0) return;

        Gizmos.color = gizmoColor;
        Vector3? prev = null;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Vector3 p = waypoints[i].position + Vector3.up * 0.05f;
            Gizmos.DrawSphere(p, 0.15f);
            if (prev.HasValue)
                Gizmos.DrawLine(prev.Value, p);
            prev = p;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Create 3 Sample Waypoints")]
    void CreateSampleWaypoints()
    {
        RebuildFromChildrenIfNeeded();
        waypoints.Clear();
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject($"Waypoint_{i + 1}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -i * 2f);
            waypoints.Add(go.transform);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
