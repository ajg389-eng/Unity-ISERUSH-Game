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
        if (!RoadTrafficController.TryGetHighway(out float west, out float east, out float roadZ, out float roadY))
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
        float roadEdge = roadZ + farSide * 5f;
        GameObject roads = GameObject.Find("Roads");
        if (roads != null)
            foreach (Renderer r in roads.GetComponentsInChildren<Renderer>())
                if (r.name.IndexOf("Lane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    roadEdge = farSide > 0 ? Mathf.Max(roadEdge, r.bounds.max.z) : Mathf.Min(roadEdge, r.bounds.min.z);

        // Across the road, away from the east-side parking entrance.
        float stopX = Mathf.Clamp(building.min.x - 9f, west + 15f, east - 15f);
        float yaw = farSide > 0 ? 180f : 0f;
        Vector3 stop = new Vector3(stopX, roadY, roadEdge + farSide * 3.3f);
        // The bench remains in its roadside position; center the shelter over it.
        Vector3 benchPosition = stop + Vector3.right * 4.5f;
        GameObject shelter = Place("Bus Stop", benchPosition, yaw, 2.8f);
        float binX = benchPosition.x + 2.5f;
        if (shelter != null)
        {
            Bounds shelterBounds = shelter.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer r in shelter.GetComponentsInChildren<Renderer>()) shelterBounds.Encapsulate(r.bounds);
            // Measure the front edge, rather than the pivot, so the shelter
            // sits just off the roadside path without projecting into traffic.
            float frontZ = farSide > 0 ? shelterBounds.min.z : shelterBounds.max.z;
            float shift = roadEdge + farSide * 0.45f - frontZ;
            shelter.transform.position += Vector3.forward * shift;
            benchPosition.z += shift;
            binX = shelterBounds.max.x + 1.35f;
        }
        Place("Bench_1", benchPosition, yaw, 0.6f);
        Place("Dustbin", new Vector3(binX, roadY, benchPosition.z), 0, 0.9f);
        Place("Hydrant", new Vector3(stopX + 9f, roadY, roadEdge + farSide * 1.1f), yaw, 0.9f);

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
