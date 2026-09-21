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

    float EffectiveSpawnInterval()
    {
        bool dayOne = GameTimeManager.Instance == null || GameTimeManager.Instance.CurrentDay <= 1;
        if (dayOne)
            return Mathf.Max(spawnInterval, 6f);
        return Mathf.Max(spawnInterval, 3.5f);
    }

    /// <summary>Expected customer arrivals per real minute at the current spawn rate.</summary>
    public float CustomersPerMinute => 60f / Mathf.Max(0.1f, EffectiveSpawnInterval());

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
        if (OnboardingTutorial.BlocksAutoCustomers) return;
        if (timer < EffectiveSpawnInterval()) return;
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
        if (orderConfig != null && !orderConfig.HasEnabledItems)
            return false;
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

        // Customers choose from the currently enabled menu only after reaching the register.
        ai.ClearOrder();

        // 1) Start entry walk  2) Join queue immediately so a lineup slot is reserved
        ai.BeginEntryRoute(entryPointsBuffer, grid, exitPath);
        ai.SetTargetRegister(r);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.RecordCustomerVisit();

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

        CustomerWallDoor entrance = CustomerWallDoor.FindRandomDoor(CustomerWallDoor.DoorRole.Entrance);
        if (entrance != null)
        {
            into.Add(entrance.GetCustomerWaypoint(true, 1.75f));
            into.Add(entrance.GetCustomerWaypoint(false, 1.25f));
            return;
        }

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
        RefreshPlacedRegisters();
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

    void RefreshPlacedRegisters()
    {
        registers.RemoveAll(r => r == null || !r.gameObject.activeInHierarchy);
        Register[] placed = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Register register in placed)
            if (register != null && !registers.Contains(register))
                registers.Add(register);
    }
}
