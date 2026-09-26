using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ingredient packs are paid for immediately. When the timer ends a van parks
/// in the lot, the driver walks to the counter, hands over stock, and leaves.
/// </summary>
public class IngredientDeliveryService : MonoBehaviour
{
    public const float DeliverySeconds = 60f;
    public const float ArrivalLeadSeconds = 17f;

    public static IngredientDeliveryService Instance { get; private set; }

    public class Pack
    {
        public ItemDefinition item;
        public int amount;
    }

    public class Shipment
    {
        public readonly List<Pack> packs = new List<Pack>();
        public float remaining;
        public float duration;
        public IngredientCourier courier;
        public DeliveryVan van;
        public bool dispatched;
    }

    readonly List<Shipment> shipments = new List<Shipment>();

    public IReadOnlyList<Shipment> Shipments => shipments;
    public bool HasPending
    {
        get
        {
            for (int i = 0; i < shipments.Count; i++)
                if (shipments[i] != null && shipments[i].packs.Count > 0)
                    return true;
            return false;
        }
    }

    public float NextDeliveryRemaining
    {
        get
        {
            float best = -1f;
            for (int i = 0; i < shipments.Count; i++)
            {
                Shipment shipment = shipments[i];
                if (shipment == null || shipment.packs.Count == 0) continue;
                if (best < 0f || shipment.remaining < best)
                    best = shipment.remaining;
            }
            return best;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<IngredientDeliveryService>() != null) return;
        var go = new GameObject("IngredientDeliveryService");
        go.AddComponent<IngredientDeliveryService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void QueuePack(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return;

        Shipment shipment = null;
        for (int i = 0; i < shipments.Count; i++)
        {
            if (shipments[i] != null && !shipments[i].dispatched)
            {
                shipment = shipments[i];
                break;
            }
        }

        if (shipment == null)
        {
            shipment = new Shipment
            {
                remaining = DeliveryDuration(),
                duration = DeliveryDuration()
            };
            shipments.Add(shipment);
        }

        shipment.packs.Add(new Pack { item = item, amount = amount });
    }

    /// <summary>Queue one shipment containing every line in a cart.</summary>
    public bool QueueOrder(IReadOnlyList<ItemDefinition> items, IReadOnlyList<int> amounts)
    {
        if (HasPending || items == null || amounts == null || items.Count != amounts.Count)
            return false;

        var shipment = new Shipment
        {
            remaining = DeliveryDuration(),
            duration = DeliveryDuration()
        };

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null || amounts[i] <= 0) continue;
            shipment.packs.Add(new Pack { item = items[i], amount = amounts[i] });
        }

        if (shipment.packs.Count == 0)
            return false;

        shipments.Add(shipment);
        return true;
    }

    /// <summary>Cancel the outstanding shipment only when it matches the recorded order.</summary>
    public bool TryCancelOrder(IReadOnlyList<ItemDefinition> items, IReadOnlyList<int> amounts)
    {
        if (items == null || amounts == null || items.Count != amounts.Count)
            return false;

        for (int i = shipments.Count - 1; i >= 0; i--)
        {
            Shipment shipment = shipments[i];
            if (shipment == null || shipment.packs.Count != items.Count) continue;

            bool[] matched = new bool[shipment.packs.Count];
            bool same = true;
            for (int line = 0; line < items.Count && same; line++)
            {
                bool found = false;
                for (int p = 0; p < shipment.packs.Count; p++)
                {
                    Pack pack = shipment.packs[p];
                    if (matched[p] || pack.item != items[line] || pack.amount != amounts[line]) continue;
                    matched[p] = true;
                    found = true;
                    break;
                }
                if (!found) same = false;
            }

            if (!same) continue;
            CancelShipment(i);
            return true;
        }
        return false;
    }

    public bool TryCancelPack(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return false;
        for (int i = shipments.Count - 1; i >= 0; i--)
        {
            Shipment shipment = shipments[i];
            if (shipment == null) continue;
            for (int p = shipment.packs.Count - 1; p >= 0; p--)
            {
                Pack pack = shipment.packs[p];
                if (pack.item != item || pack.amount != amount) continue;
                shipment.packs.RemoveAt(p);
                if (shipment.packs.Count == 0)
                    CancelShipment(i);
                return true;
            }
        }
        return false;
    }

    public int GetIncomingCount(ItemDefinition item)
    {
        if (item == null) return 0;
        int n = 0;
        for (int i = 0; i < shipments.Count; i++)
        {
            Shipment shipment = shipments[i];
            if (shipment == null) continue;
            for (int p = 0; p < shipment.packs.Count; p++)
            {
                if (shipment.packs[p].item == item)
                    n += shipment.packs[p].amount;
            }
        }
        return n;
    }

    public float GetRemainingFor(ItemDefinition item)
    {
        if (item == null) return -1f;
        float best = -1f;
        for (int i = 0; i < shipments.Count; i++)
        {
            Shipment shipment = shipments[i];
            if (shipment == null) continue;
            bool hasItem = false;
            for (int p = 0; p < shipment.packs.Count; p++)
            {
                if (shipment.packs[p].item == item)
                {
                    hasItem = true;
                    break;
                }
            }
            if (!hasItem) continue;
            if (best < 0f || shipment.remaining < best)
                best = shipment.remaining;
        }
        return best;
    }

    public static string FormatCountdown(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    void Update()
    {
        for (int i = shipments.Count - 1; i >= 0; i--)
        {
            Shipment shipment = shipments[i];
            if (shipment == null)
            {
                shipments.RemoveAt(i);
                continue;
            }

            if (shipment.remaining > 0f)
                shipment.remaining = Mathf.Max(0f, shipment.remaining - Time.deltaTime);

            if (!shipment.dispatched && shipment.remaining <= ArrivalLead(shipment))
                BeginArrival(shipment);
        }
    }

    void BeginArrival(Shipment shipment)
    {
        shipment.dispatched = true;
        Vector3 stall;
        Quaternion facing;
        if (!ParkingLotDressing.TryGetDeliveryStall(out stall, out facing))
        {
            stall = GetHandoffPoint() + Vector3.right * 8f;
            facing = Quaternion.LookRotation(Vector3.left, Vector3.up);
        }

        ResolveVanSpawn(stall, facing, out Vector3 spawnPos, out Quaternion spawnFacing);

        GameObject vanPrefab = LoadVanPrefab();
        GameObject vanGo;
        if (vanPrefab != null)
        {
            vanGo = Instantiate(vanPrefab, spawnPos, spawnFacing);
            vanGo.name = "DeliveryVan";
            Collider[] colliders = vanGo.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++)
                if (colliders[c] != null)
                    colliders[c].enabled = false;
        }
        else
        {
            vanGo = new GameObject("DeliveryVan");
            vanGo.transform.SetPositionAndRotation(spawnPos, spawnFacing);
        }

        var van = vanGo.AddComponent<DeliveryVan>();
        van.Arrive(stall, facing, () => SpawnDriver(shipment, van));
        shipment.van = van;
    }

    void SpawnDriver(Shipment shipment, DeliveryVan van)
    {
        if (shipment == null) return;
        Vector3 spawn = van != null ? van.DriverDoorPosition : GetHandoffPoint();
        GameObject body = SpawnCourierBody(spawn);
        if (body == null)
        {
            Complete(shipment);
            return;
        }

        var courier = body.AddComponent<IngredientCourier>();
        courier.Begin(shipment, GetHandoffPoint(), spawn, van);
        shipment.courier = courier;
    }

    public void NotifyArrived(IngredientCourier courier)
    {
        if (courier == null) return;
        for (int i = 0; i < shipments.Count; i++)
        {
            if (shipments[i] != null && shipments[i].courier == courier)
            {
                Complete(shipments[i]);
                return;
            }
        }
    }

    public void NotifyDriverReturned(IngredientCourier courier)
    {
        for (int i = 0; i < shipments.Count; i++)
        {
            Shipment shipment = shipments[i];
            if (shipment == null || shipment.courier != courier) continue;
            if (shipment.van != null)
                shipment.van.Leave();
            shipments.RemoveAt(i);
            return;
        }
    }

    void Complete(Shipment shipment)
    {
        if (shipment == null) return;
        shipment.remaining = 0f;
        var kitchen = KitchenInventory.Instance;
        for (int i = 0; i < shipment.packs.Count; i++)
        {
            Pack pack = shipment.packs[i];
            if (kitchen != null && pack.item != null)
                kitchen.AddStock(pack.item, pack.amount);
        }

        Sfx.Play(SfxId.ItemDelivered);
        DeliveryArrivedNotice.Show(shipment);
        shipment.packs.Clear();
        if (shipment.courier != null)
            shipment.courier.Depart();

        var ui = FindFirstObjectByType<IngredientsOrderUI>();
        if (ui != null)
            ui.Refresh();
    }

    void CancelShipment(int index)
    {
        Shipment shipment = shipments[index];
        if (shipment.courier != null)
            shipment.courier.CancelAndLeave();
        if (shipment.van != null)
            shipment.van.Leave();
        shipments.RemoveAt(index);
    }

    static float ArrivalLead(Shipment shipment)
    {
        float duration = shipment != null ? shipment.duration : DeliverySeconds;
        if (OnboardingTutorial.IsActive)
            return duration;
        return Mathf.Min(duration, ArrivalLeadSeconds);
    }

    static float DeliveryDuration()
    {
        return OnboardingTutorial.IsActive ? ArrivalLeadSeconds : DeliverySeconds;
    }

    static GameObject LoadVanPrefab()
    {
        GameObject[] loaded = Resources.LoadAll<GameObject>("Traffic");
        GameObject fallback = null;
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] == null) continue;
            string name = loaded[i].name;
            if (name.IndexOf("Pick", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return loaded[i];
            if (name.IndexOf("Truck", System.StringComparison.OrdinalIgnoreCase) >= 0)
                fallback = loaded[i];
        }
        return fallback;
    }

    static GameObject SpawnCourierBody(Vector3 spawn)
    {
        CustomerSpawner spawner = FindFirstObjectByType<CustomerSpawner>();
        GameObject prefab = spawner != null ? spawner.customerPrefab : null;
        if (prefab == null)
            prefab = Resources.Load<GameObject>("Prefabs/character_default");
        if (prefab == null) return null;

        GameObject body = Instantiate(prefab, spawn, Quaternion.identity);
        body.name = "IngredientCourier";
        var ai = body.GetComponent<CustomerAI>();
        if (ai != null)
        {
            ai.enabled = false;
            Destroy(ai);
        }
        var patience = body.GetComponent<CustomerPatienceMeter>();
        if (patience != null)
            Destroy(patience);
        var orderLabel = body.GetComponent<CustomerOrderLabel>();
        if (orderLabel != null)
        {
            orderLabel.enabled = false;
            orderLabel.EnsureHierarchy();
            if (orderLabel.labelRoot != null)
                orderLabel.labelRoot.SetActive(false);
        }
        var look = PartyCharacterRandomizer.EnsureOn(body);
        look.randomizeOnStart = true;
        look.hatChance = 1f;
        PartyCharacterAnimator.EnsureOn(body);
        return body;
    }

    public static Vector3 GetHandoffPoint()
    {
        if (CounterSurface.TryGetEmptyDeliverySpot(out _, out Vector3 stand))
            return stand;

        Register register = FindNearestRegister();
        if (register != null)
            return register.GetDeliveryStandPosition();

        GameObject floor = GameObject.Find("CustomerFloor");
        Renderer renderer = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        if (renderer != null)
        {
            Bounds b = renderer.bounds;
            return new Vector3(b.min.x + 2.5f, b.max.y, b.center.z);
        }
        return Vector3.zero;
    }

    public static List<Vector3> BuildWalkToCounter(Vector3 from)
    {
        var path = new List<Vector3>();
        Add(path, from);
        Vector3 handoff = GetHandoffPoint();
        CustomerWallDoor door = CustomerWallDoor.FindEntryDoor();
        if (door != null)
        {
            Vector3 outside = door.GetCustomerWaypoint(true, 2.4f);
            AppendAroundBus(path, from, outside);
            Add(path, outside);
            Add(path, door.GetCustomerWaypoint(true, 0.15f));
            Vector3 inside = door.GetCustomerWaypoint(false, 1.0f);
            Add(path, inside);
            Add(path, new Vector3(inside.x, inside.y, handoff.z));
        }
        else
            AppendAroundBus(path, from, handoff);
        Add(path, handoff);
        return path;
    }

    public static List<Vector3> BuildWalkToVan(Vector3 from, Vector3 vanDoor)
    {
        var path = new List<Vector3>();
        Add(path, from);
        CustomerWallDoor door = CustomerWallDoor.FindExitDoor();
        Vector3 outside = vanDoor;
        if (door != null)
        {
            Add(path, door.GetCustomerWaypoint(false, 1.0f));
            Add(path, door.GetCustomerWaypoint(true, 0.15f));
            outside = door.GetCustomerWaypoint(true, 2.4f);
            Add(path, outside);
        }
        AppendAroundBus(path, path.Count > 0 ? path[path.Count - 1] : from, vanDoor);
        Add(path, vanDoor);
        return path;
    }

    static void AppendAroundBus(List<Vector3> path, Vector3 from, Vector3 to)
    {
        if (!ParkingLotDressing.TryGetBusBounds(out Bounds bus)
            && !TryGetParkedCarBounds(out bus))
            return;

        Bounds pad = bus;
        if (TryGetParkedCarBounds(out Bounds parked))
        {
            pad.Encapsulate(parked);
        }
        pad.Expand(1.85f);
        if (!SegmentHitsBoundsXZ(from, to, pad))
            return;

        float y = from.y;
        float southZ = pad.min.z - 0.55f;
        Add(path, new Vector3(from.x, y, southZ));
        Add(path, new Vector3(to.x, y, southZ));
    }

    static bool TryGetParkedCarBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            Transform t = renderer.transform;
            bool parked = false;
            while (t != null)
            {
                if (t.name.StartsWith("Parked_", System.StringComparison.OrdinalIgnoreCase))
                {
                    parked = true;
                    break;
                }
                t = t.parent;
            }
            if (!parked) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    static bool SegmentHitsBoundsXZ(Vector3 a, Vector3 b, Bounds pad)
    {
        Vector3 aa = new Vector3(a.x, pad.center.y, a.z);
        Vector3 bb = new Vector3(b.x, pad.center.y, b.z);
        if (pad.Contains(aa) || pad.Contains(bb))
            return true;

        const int samples = 8;
        for (int i = 1; i < samples; i++)
        {
            float t = i / (float)samples;
            Vector3 p = Vector3.Lerp(aa, bb, t);
            if (pad.Contains(p))
                return true;
        }
        return false;
    }

    static void Add(List<Vector3> path, Vector3 point)
    {
        if (path.Count > 0 && HorizontalDist(path[path.Count - 1], point) < 0.2f)
            return;
        path.Add(point);
    }

    static void ResolveVanSpawn(Vector3 stall, Quaternion facing, out Vector3 spawnPos, out Quaternion spawnFacing)
    {
        if (RoadTrafficController.TryGetHighway(out float westX, out _, out float roadZ, out float roadY))
        {
            spawnPos = new Vector3(westX, roadY, roadZ - RoadTrafficController.HighwayLaneOffset);
            spawnFacing = Quaternion.LookRotation(Vector3.right, Vector3.up);
            return;
        }

        spawnPos = stall + Vector3.forward * 16f + Vector3.left * 4f;
        spawnFacing = facing;
    }

    static Register FindNearestRegister()
    {
        Register[] registers = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Register bestEnabled = null;
        Register bestAny = null;
        float bestEnabledDist = float.MaxValue;
        float bestAnyDist = float.MaxValue;
        Vector3 hint = Vector3.zero;
        GameObject floor = GameObject.Find("CustomerFloor");
        if (floor != null)
            hint = floor.transform.position;
        for (int i = 0; i < registers.Length; i++)
        {
            if (registers[i] == null) continue;
            float d = (registers[i].transform.position - hint).sqrMagnitude;
            if (d < bestAnyDist)
            {
                bestAnyDist = d;
                bestAny = registers[i];
            }
            if (!registers[i].isEnabled) continue;
            if (d >= bestEnabledDist) continue;
            bestEnabledDist = d;
            bestEnabled = registers[i];
        }
        return bestEnabled != null ? bestEnabled : bestAny;
    }

    static float HorizontalDist(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }
}
