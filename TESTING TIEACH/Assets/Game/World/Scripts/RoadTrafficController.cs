using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extends the highway into both tunnels and periodically drives cars both ways.
/// </summary>
public class RoadTrafficController : MonoBehaviour
{
    public const float HighwayLaneOffset = 2.35f;
    const float LaneOffset = HighwayLaneOffset;
    const float MinSpawnGap = 16f;

    [SerializeField] float minSpawnInterval = 2.4f;
    [SerializeField] float maxSpawnInterval = 5.2f;
    [SerializeField] float minSpeed = 11f;
    [SerializeField] float maxSpeed = 16f;

    readonly List<GameObject> carPrefabs = new List<GameObject>();
    readonly List<DrivingCar> liveCars = new List<DrivingCar>();

    public static float WestSpawnX { get; private set; }
    public static float EastSpawnX { get; private set; }
    public static float HighwayZ { get; private set; }
    public static float HighwayY { get; private set; }
    public static bool HasHighway { get; private set; }

    static RoadTrafficController instance;
    int eastboundHold;
    float eastboundHoldUntil;

    public static bool TryGetHighway(out float westX, out float eastX, out float roadZ, out float roadY)
    {
        westX = WestSpawnX;
        eastX = EastSpawnX;
        roadZ = HighwayZ;
        roadY = HighwayY;
        return HasHighway;
    }

    float westSpawnX;
    float eastSpawnX;
    float westEndX;
    float eastEndX;
    float roadZ;
    float roadY;
    float nextEastbound;
    float nextWestbound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<RoadTrafficController>() != null) return;
        var go = new GameObject("RoadTraffic");
        go.AddComponent<RoadTrafficController>();
    }

    void Start()
    {
        instance = this;
        LoadCarPrefabs();
        ExtendRoadIntoTunnels();
        ResolveDrivePath();
        nextEastbound = Random.Range(0.2f, 1.2f);
        nextWestbound = Random.Range(0.8f, 2.0f);
        SpawnCar(1f);
        SpawnCar(-1f);
    }

    void Update()
    {
        if (carPrefabs.Count == 0) return;

        bool holdEast = eastboundHold > 0 || Time.time < eastboundHoldUntil;
        SetEastboundPaused(holdEast);

        nextEastbound -= holdEast ? 0f : Time.deltaTime;
        nextWestbound -= Time.deltaTime;
        if (nextEastbound <= 0f)
        {
            if (!holdEast)
                SpawnCar(1f);
            nextEastbound = holdEast ? 0.8f : Random.Range(minSpawnInterval, maxSpawnInterval);
        }
        if (nextWestbound <= 0f)
        {
            SpawnCar(-1f);
            nextWestbound = Random.Range(minSpawnInterval, maxSpawnInterval);
        }
    }

    void LoadCarPrefabs()
    {
        carPrefabs.Clear();
        GameObject[] loaded = Resources.LoadAll<GameObject>("Traffic");
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] != null)
                carPrefabs.Add(loaded[i]);
        }
    }

    void ExtendRoadIntoTunnels()
    {
        GameObject roadsRoot = GameObject.Find("Roads");
        if (roadsRoot == null) return;

        List<Renderer> lanes = CollectLaneRenderers(roadsRoot.transform);
        if (lanes.Count == 0) return;

        Renderer template = lanes[0];
        Bounds roadBounds = template.bounds;
        for (int i = 1; i < lanes.Count; i++)
            roadBounds.Encapsulate(lanes[i].bounds);

        float tileLength = Mathf.Max(4f, template.bounds.size.x);
        Quaternion tileRotation = template.transform.rotation;
        float z = template.transform.position.z;
        float y = template.transform.position.y;

        if (!TryGetTunnelBounds(out Bounds westTunnel, out Bounds eastTunnel))
            return;

        float targetMinX = westTunnel.center.x - 2f;
        float targetMaxX = eastTunnel.center.x + 2f;

        HashSet<int> occupied = new HashSet<int>();
        for (int i = 0; i < lanes.Count; i++)
            occupied.Add(Mathf.RoundToInt(lanes[i].transform.position.x / tileLength));

        for (float x = roadBounds.center.x; x > targetMinX; x -= tileLength)
            TryPlaceLane(roadsRoot.transform, template.gameObject, x, y, z, tileRotation, occupied, tileLength);
        for (float x = roadBounds.center.x; x < targetMaxX; x += tileLength)
            TryPlaceLane(roadsRoot.transform, template.gameObject, x, y, z, tileRotation, occupied, tileLength);
    }

    static void TryPlaceLane(Transform parent, GameObject template, float x, float y, float z,
        Quaternion rotation, HashSet<int> occupied, float tileLength)
    {
        int key = Mathf.RoundToInt(x / tileLength);
        if (!occupied.Add(key)) return;

        GameObject tile = Instantiate(template, parent);
        tile.name = template.name + "_Extend";
        tile.transform.SetPositionAndRotation(new Vector3(x, y, z), rotation);
        Collider[] colliders = tile.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;
    }

    static List<Renderer> CollectLaneRenderers(Transform roadsRoot)
    {
        var list = new List<Renderer>();
        Renderer[] renderers = roadsRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer.name.IndexOf("Lane", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            list.Add(renderer);
        }
        return list;
    }

    static bool TryGetTunnelBounds(out Bounds west, out Bounds east)
    {
        west = default;
        east = default;
        var found = new List<Renderer>();
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (renderers[i].transform.parent != null
                && renderers[i].transform.parent.name.IndexOf("TunnelInside", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (renderers[i].gameObject.name.IndexOf("Tunnel", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (renderers[i].gameObject.name.IndexOf("TunnelInside", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            found.Add(renderers[i]);
        }

        if (found.Count < 2) return false;

        found.Sort((a, b) => a.bounds.center.x.CompareTo(b.bounds.center.x));
        west = found[0].bounds;
        east = found[found.Count - 1].bounds;
        return true;
    }

    void ResolveDrivePath()
    {
        GameObject roadsRoot = GameObject.Find("Roads");
        Bounds road = new Bounds(new Vector3(0f, 0f, 20f), new Vector3(120f, 1f, 10f));
        if (roadsRoot != null)
        {
            List<Renderer> lanes = CollectLaneRenderers(roadsRoot.transform);
            if (lanes.Count > 0)
            {
                road = lanes[0].bounds;
                for (int i = 1; i < lanes.Count; i++)
                    road.Encapsulate(lanes[i].bounds);
            }
        }

        roadZ = road.center.z;
        roadY = 0.02f;

        if (TryGetTunnelBounds(out Bounds westTunnel, out Bounds eastTunnel))
        {
            westSpawnX = westTunnel.center.x;
            eastSpawnX = eastTunnel.center.x;
            westEndX = westTunnel.center.x - 4f;
            eastEndX = eastTunnel.center.x + 4f;
        }
        else
        {
            westSpawnX = road.min.x - 2f;
            eastSpawnX = road.max.x + 2f;
            westEndX = westSpawnX - 4f;
            eastEndX = eastSpawnX + 4f;
        }

        WestSpawnX = westSpawnX;
        EastSpawnX = eastSpawnX;
        HighwayZ = roadZ;
        HighwayY = roadY;
        HasHighway = true;
    }

    public static void BeginDeliveryLaneClearance()
    {
        if (instance == null)
            instance = FindFirstObjectByType<RoadTrafficController>();
        if (instance == null) return;
        instance.eastboundHold++;
        instance.eastboundHoldUntil = Time.time + 2.5f;
        instance.DespawnEastbound();
        instance.SetEastboundPaused(true);
    }

    public static void KeepDeliveryLaneClear(float extraSeconds = 1.5f)
    {
        if (instance == null)
            instance = FindFirstObjectByType<RoadTrafficController>();
        if (instance == null) return;
        instance.eastboundHoldUntil = Mathf.Max(instance.eastboundHoldUntil, Time.time + extraSeconds);
    }

    public static void EndDeliveryLaneClearance()
    {
        if (instance == null) return;
        instance.eastboundHold = Mathf.Max(0, instance.eastboundHold - 1);
        instance.eastboundHoldUntil = Mathf.Max(instance.eastboundHoldUntil, Time.time + 1.8f);
    }

    void DespawnEastbound()
    {
        PruneCars();
        for (int i = liveCars.Count - 1; i >= 0; i--)
        {
            DrivingCar car = liveCars[i];
            if (car == null) continue;
            if (car.Direction <= 0f) continue;
            Destroy(car.gameObject);
            liveCars.RemoveAt(i);
        }
    }

    void SetEastboundPaused(bool paused)
    {
        PruneCars();
        for (int i = 0; i < liveCars.Count; i++)
        {
            DrivingCar car = liveCars[i];
            if (car == null || car.Direction <= 0f) continue;
            car.SetPaused(paused);
        }
    }

    void SpawnCar(float direction)
    {
        if (carPrefabs.Count == 0) return;
        PruneCars();

        float spawnX = direction > 0f ? westSpawnX : eastSpawnX;
        float laneZ = roadZ + (direction > 0f ? -LaneOffset : LaneOffset);
        if (LaneOccupied(spawnX, laneZ, direction))
            return;
        if (DeliveryVanBlocksLane(spawnX, laneZ, direction))
            return;

        GameObject prefab = carPrefabs[Random.Range(0, carPrefabs.Count)];
        Vector3 pos = new Vector3(spawnX, roadY, laneZ);
        Quaternion facing = Quaternion.LookRotation(direction > 0f ? Vector3.right : Vector3.left, Vector3.up);
        GameObject car = Instantiate(prefab, pos, facing, transform);
        car.name = prefab.name;

        var driver = car.GetComponent<DrivingCar>();
        if (driver == null)
            driver = car.AddComponent<DrivingCar>();
        driver.Configure(Random.Range(minSpeed, maxSpeed), direction, direction > 0f ? eastEndX : westEndX);
        liveCars.Add(driver);
    }

    bool LaneOccupied(float spawnX, float laneZ, float direction)
    {
        for (int i = 0; i < liveCars.Count; i++)
        {
            DrivingCar car = liveCars[i];
            if (car == null) continue;
            if (Mathf.Abs(car.transform.position.z - laneZ) > 1.2f) continue;
            float along = (car.transform.position.x - spawnX) * direction;
            if (along >= 0f && along < MinSpawnGap)
                return true;
        }
        return false;
    }

    static bool DeliveryVanBlocksLane(float spawnX, float laneZ, float direction)
    {
        DeliveryVan van = FindFirstObjectByType<DeliveryVan>();
        if (van == null) return false;
        if (Mathf.Abs(van.transform.position.z - laneZ) > 2.8f) return false;
        float along = (van.transform.position.x - spawnX) * direction;
        return along >= -10f && along < MinSpawnGap + 14f;
    }

    void PruneCars()
    {
        for (int i = liveCars.Count - 1; i >= 0; i--)
            if (liveCars[i] == null)
                liveCars.RemoveAt(i);
    }
}
