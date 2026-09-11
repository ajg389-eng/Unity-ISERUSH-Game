using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    public GameObject customerPrefab;
    [Tooltip("Fallback spawn position if no entry path / waypoints are set.")]
    public Transform spawnPoint;

    [Header("Entry path (pick ONE)")]
    [Tooltip("Preferred: assign a CustomerPath (Game → Create Customer Entry Path).")]
    public CustomerPath entryPath;
    [Tooltip("Or drag waypoint Transforms here in order (Outside → Door → Inside).")]
    public List<Transform> entryWaypoints = new List<Transform>();

    [Header("Exit path (optional)")]
    public CustomerPath exitPath;
    public GridManager grid;

    public float spawnInterval = 3f;

    [Tooltip("Assign to give customers random burger / fries / drink combos")]
    public CustomerOrderConfig orderConfig;

    public List<Register> registers = new List<Register>();

    float timer;
    readonly List<Vector3> entryPointsBuffer = new List<Vector3>();

    void Awake()
    {
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        ResolveEntryPathIfNeeded();
    }

    void OnValidate()
    {
        ResolveEntryPathIfNeeded();
    }

    void ResolveEntryPathIfNeeded()
    {
        if (entryPath != null) return;
        if (HasInlineWaypoints()) return;

        var paths = FindObjectsByType<CustomerPath>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == null) continue;
            string n = paths[i].gameObject.name;
            if (n.IndexOf("Exit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            entryPath = paths[i];
            return;
        }
        if (paths.Length > 0)
            entryPath = paths[0];
    }

    bool HasInlineWaypoints()
    {
        if (entryWaypoints == null) return false;
        for (int i = 0; i < entryWaypoints.Count; i++)
        {
            if (entryWaypoints[i] != null) return true;
        }
        return false;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < spawnInterval) return;
        timer = 0f;
        TrySpawn();
    }

    public bool SpawnNow()
    {
        if (!TrySpawn()) return false;
        timer = 0f;
        return true;
    }

    bool TrySpawn()
    {
        if (customerPrefab == null) return false;
        ResolveEntryPathIfNeeded();

        Register r = GetBestRegister();
        if (r == null) return false;

        BuildEntryPoints(entryPointsBuffer);
        Vector3 spawnPos = entryPointsBuffer.Count > 0
            ? entryPointsBuffer[0]
            : (spawnPoint != null ? spawnPoint.position : transform.position);

        var c = Instantiate(customerPrefab, spawnPos, Quaternion.identity);
        var ai = c.GetComponent<CustomerAI>();
        if (ai == null) return true;

        if (orderConfig != null)
            ai.SetOrder(orderConfig.GenerateRandomOrder());
        else
            ai.SetOrder(new CustomerOrder());

        // 1) Start entry walk  2) Join queue immediately so a lineup slot is reserved
        ai.BeginEntryRoute(entryPointsBuffer, grid, exitPath);
        ai.SetTargetRegister(r);

        if (entryPointsBuffer.Count < 2)
        {
            Debug.LogWarning(
                "CustomerSpawner: Entry path needs at least 2 waypoints (outside → inside). " +
                "Customers will go to the queue after spawn.",
                this);
        }

        return true;
    }

    void BuildEntryPoints(List<Vector3> into)
    {
        into.Clear();

        if (HasInlineWaypoints())
        {
            for (int i = 0; i < entryWaypoints.Count; i++)
            {
                if (entryWaypoints[i] != null)
                    into.Add(entryWaypoints[i].position);
            }
            return;
        }

        if (entryPath != null && entryPath.Count > 0)
        {
            entryPath.GetWorldPoints(into);
            return;
        }

        if (spawnPoint != null)
            into.Add(spawnPoint.position);
    }

    Register GetBestRegister()
    {
        Register best = null;
        int bestCount = int.MaxValue;

        foreach (var r in registers)
        {
            if (r == null) continue;
            if (!r.HasSpace()) continue;

            int count = r.QueueCount;
            if (count < bestCount)
            {
                bestCount = count;
                best = r;
            }
        }

        return best;
    }
}
