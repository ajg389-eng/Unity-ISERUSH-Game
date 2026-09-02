using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    public GameObject customerPrefab;
    public Transform spawnPoint;
    public float spawnInterval = 3f;

    [Tooltip("Assign to give customers random burger / fries / drink combos")]
    public CustomerOrderConfig orderConfig;

    public List<Register> registers = new List<Register>();

    float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < spawnInterval) return;
        timer = 0f;

        Register r = GetBestRegister();
        if (r == null) return;

        var c = Instantiate(customerPrefab, spawnPoint.position, Quaternion.identity);
        var ai = c.GetComponent<CustomerAI>();

        if (ai != null)
        {
            if (orderConfig != null)
                ai.SetOrder(orderConfig.GenerateRandomOrder());
            else
            {
                ai.SetOrder(new CustomerOrder());
                if (Time.frameCount % 60 == 0) Debug.LogWarning("CustomerSpawner: Assign Order Config so customers get burger/fries/drink orders. Showing 'No order'.");
            }
            ai.SetTargetRegister(r);
        }
    }

    /// <summary>Force-spawn one customer now (used by debug menu).</summary>
    public bool SpawnNow()
    {
        if (customerPrefab == null || spawnPoint == null) return false;
        Register r = GetBestRegister();
        if (r == null) return false;

        var c = Instantiate(customerPrefab, spawnPoint.position, Quaternion.identity);
        var ai = c.GetComponent<CustomerAI>();
        if (ai != null)
        {
            if (orderConfig != null)
                ai.SetOrder(orderConfig.GenerateRandomOrder());
            else
                ai.SetOrder(new CustomerOrder());
            ai.SetTargetRegister(r);
        }
        timer = 0f;
        return true;
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