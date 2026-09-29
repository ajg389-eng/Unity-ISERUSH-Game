using System.Collections;
using UnityEngine;

/// <summary>Decorative SimplePoly props placed outside traffic and customer routes.</summary>
public sealed class StreetScenery : MonoBehaviour
{
    Transform wings;

    IEnumerator Start()
    {
        // Traffic extends the road and the grid establishes the restaurant first.
        yield return null;
        if (!RoadTrafficController.TryGetHighway(out _, out _, out float roadZ, out _))
            yield break;
        var grid = GridManager.Instance;
        if (grid == null || grid.floor == null) yield break;
        var floor = grid.floor.GetComponentInChildren<Renderer>();
        if (floor == null) yield break;
        Bounds building = floor.bounds;
        if (grid.customerFloor != null)
        {
            var lobby = grid.customerFloor.GetComponentInChildren<Renderer>();
            if (lobby != null) building.Encapsulate(lobby.bounds);
        }
        float farSide = building.center.z < roadZ ? 1f : -1f;
        float yaw = farSide > 0 ? 180f : 0f;

        // Restrict placement to distant hills, keeping the windmill in the backdrop.
        Vector3 hill = default;
        float bestScore = float.NegativeInfinity;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            for (float x = building.center.x - 100f; x <= building.center.x + 100f; x += 8f)
            for (float distance = 125f; distance <= 240f; distance += 8f)
            {
                Vector3 point = new Vector3(x, 0, roadZ + farSide * distance);
                float u = (point.x-origin.x)/size.x, v = (point.z-origin.z)/size.z;
                if (u < 0.03f || u > 0.97f || v < 0.03f || v > 0.97f) continue;
                float slope = terrain.terrainData.GetSteepness(u, v);
                if (slope > 18f) continue;
                point.y = terrain.SampleHeight(point) + origin.y;
                float score = point.y - slope*0.7f + distance*0.025f;
                if (score > bestScore) { bestScore = score; hill = point; }
            }
        }
        if (bestScore > float.NegativeInfinity)
        {
            GameObject mill = Place("Windmill", hill, yaw, 16f);
            if (mill != null)
                foreach (Transform child in mill.GetComponentsInChildren<Transform>())
                    if (child.name == "Wings") { wings = child; break; }
        }
        else Debug.LogWarning("Street scenery: no suitable terrain found for the background windmill.");
    }

    GameObject Place(string asset, Vector3 ground, float yaw, float height)
    {
        GameObject prefab = Resources.Load<GameObject>("StreetProps/" + asset);
        if (prefab == null) { Debug.LogWarning("Missing street prop: " + asset); return null; }
        GameObject prop = Instantiate(prefab, ground, Quaternion.Euler(0, yaw, 0), transform);
        prop.name = asset;
        Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(prop); return null; }
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        prop.transform.localScale *= height / Mathf.Max(0.01f, bounds.size.y);
        bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        prop.transform.position += new Vector3(ground.x-bounds.center.x, ground.y-bounds.min.y, ground.z-bounds.center.z);
        // Scenery must not intercept construction clicks or alter existing AI paths.
        foreach (Collider collider in prop.GetComponentsInChildren<Collider>()) collider.enabled = false;
        return prop;
    }

    void Update()
    {
        if (wings != null) wings.Rotate(Vector3.forward, 12f * Time.deltaTime, Space.Self);
    }
}
