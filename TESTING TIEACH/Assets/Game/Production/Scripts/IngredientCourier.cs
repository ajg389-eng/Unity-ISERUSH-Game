using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// After the timer ends, walks from the parked van through the front door to
/// the counter, hands off ingredients, then walks back out to the van.
/// </summary>
public class IngredientCourier : MonoBehaviour
{
    enum Phase
    {
        ToCounter,
        HandingOff,
        ToVan
    }

    IngredientDeliveryService.Shipment shipment;
    DeliveryVan van;
    readonly List<Vector3> path = new List<Vector3>();
    int index;
    float speed = 6.02f;
    Phase phase = Phase.ToCounter;
    float handoffTimer;
    Vector3 handoff;
    Vector3 vanDoor;

    public void Begin(IngredientDeliveryService.Shipment owningShipment, Vector3 counter, Vector3 doorAtVan, DeliveryVan parkedVan)
    {
        shipment = owningShipment;
        van = parkedVan;
        handoff = counter;
        vanDoor = doorAtVan;
        path.Clear();
        List<Vector3> inbound = IngredientDeliveryService.BuildWalkToCounter(doorAtVan);
        for (int i = 0; i < inbound.Count; i++)
            path.Add(inbound[i]);
        index = 0;
        phase = Phase.ToCounter;
        HideRootCapsule();
        DisableCollision();
        SnapFeetToFloor();
        transform.position = doorAtVan;
    }

    public void Depart()
    {
        if (phase == Phase.ToVan) return;
        phase = Phase.ToVan;
        path.Clear();
        Vector3 door = van != null ? van.DriverDoorPosition : vanDoor;
        List<Vector3> outbound = IngredientDeliveryService.BuildWalkToVan(transform.position, door);
        for (int i = 0; i < outbound.Count; i++)
            path.Add(outbound[i]);
        index = 0;
        speed = 6.44f;
    }

    public void CancelAndLeave()
    {
        shipment = null;
        Depart();
    }

    void Update()
    {
        SnapFeetToFloor();
        PushOutOfCounters();

        if (phase == Phase.HandingOff)
        {
            FacePoint(handoff + Vector3.left);
            handoffTimer -= Time.deltaTime;
            if (handoffTimer <= 0f)
                IngredientDeliveryService.Instance?.NotifyArrived(this);
            return;
        }

        if (index >= path.Count)
        {
            if (phase == Phase.ToCounter)
            {
                if (HorizontalDist(transform.position, handoff) > 0.45f)
                {
                    path.Add(handoff);
                    return;
                }

                phase = Phase.HandingOff;
                handoffTimer = 0.85f;
                FacePoint(handoff + Vector3.left);
                DropGoodsOnCounter();
                return;
            }

            IngredientDeliveryService.Instance?.NotifyDriverReturned(this);
            Destroy(gameObject);
            return;
        }

        Vector3 target = path[index];
        target.y = transform.position.y;
        FacePoint(target);
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        PushOutOfCounters();
        if (HorizontalDist(transform.position, target) <= 0.14f)
            index++;
    }

    void FacePoint(Vector3 target)
    {
        var facing = PartyCharacterAnimator.EnsureOn(gameObject);
        if (facing != null)
            facing.FaceTowardAdjacent(target, smooth: true);
    }

    void DisableCollision()
    {
        var controller = GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;
        var body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
    }

    void PushOutOfCounters()
    {
        Register nearest = NearestRegister();
        if (nearest == null) return;
        Vector3 stand = nearest.GetDeliveryStandPosition();
        Vector3 p = transform.position;
        if (p.x < stand.x)
        {
            p.x = stand.x;
            transform.position = p;
        }
    }

    Register NearestRegister()
    {
        Register[] registers = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Register best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < registers.Length; i++)
        {
            if (registers[i] == null) continue;
            float d = HorizontalDist(registers[i].transform.position, transform.position);
            if (d >= bestDist) continue;
            bestDist = d;
            best = registers[i];
        }
        return best;
    }

    void DropGoodsOnCounter()
    {
        Vector3 counterTop = handoff + Vector3.left * 0.55f;
        if (CounterSurface.TryGetEmptyDeliverySpot(out Vector3 drop, out _))
            counterTop = drop;
        else
        {
            Register nearest = NearestRegister();
            if (nearest != null)
                counterTop = nearest.GetCounterDropPosition();
        }

        GameObject box = BuildWoodCrate();
        box.name = "IngredientDrop";
        box.transform.position = new Vector3(counterTop.x, counterTop.y + 0.28f, counterTop.z);
        box.transform.rotation = Quaternion.identity;
        Destroy(box, 5f);
    }

    static GameObject BuildWoodCrate()
    {
        var root = new GameObject("IngredientDrop");
        AddCrateBoard(root.transform, Vector3.zero, new Vector3(0.72f, 0.46f, 0.58f), new Color(0.62f, 0.4f, 0.2f));
        AddCrateBoard(root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.76f, 0.07f, 0.62f), new Color(0.45f, 0.28f, 0.13f));
        AddCrateBoard(root.transform, new Vector3(-0.34f, 0f, 0f), new Vector3(0.07f, 0.5f, 0.62f), new Color(0.38f, 0.23f, 0.1f));
        AddCrateBoard(root.transform, new Vector3(0.34f, 0f, 0f), new Vector3(0.07f, 0.5f, 0.62f), new Color(0.38f, 0.23f, 0.1f));
        AddCrateBoard(root.transform, new Vector3(0f, -0.18f, 0.26f), new Vector3(0.72f, 0.08f, 0.07f), new Color(0.5f, 0.32f, 0.15f));
        AddCrateBoard(root.transform, new Vector3(0f, -0.18f, -0.26f), new Vector3(0.72f, 0.08f, 0.07f), new Color(0.5f, 0.32f, 0.15f));
        return root;
    }

    static void AddCrateBoard(Transform parent, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Board";
        board.transform.SetParent(parent, false);
        board.transform.localPosition = localPos;
        board.transform.localScale = scale;
        Collider collider = board.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        var renderer = board.GetComponent<Renderer>();
        if (renderer == null) return;
        var material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard"));
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        renderer.material = material;
    }

    void HideRootCapsule()
    {
        var mesh = GetComponent<MeshRenderer>();
        if (mesh != null && GetComponentInChildren<SkinnedMeshRenderer>() != null)
            mesh.enabled = false;
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
            capsule.enabled = false;
    }

    void SnapFeetToFloor()
    {
        float groundY = ResolveGroundY();
        Vector3 pos = transform.position;
        if (TryGetVisualBounds(out Bounds bounds))
            pos.y += groundY - bounds.min.y;
        else
            pos.y = groundY;
        transform.position = pos;
    }

    float ResolveGroundY()
    {
        Vector3 p = transform.position;
        GameObject floor = GameObject.Find("CustomerFloor");
        Renderer lobby = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        if (lobby != null)
        {
            Bounds b = lobby.bounds;
            if (p.x >= b.min.x - 0.4f && p.x <= b.max.x + 0.4f
                && p.z >= b.min.z - 0.4f && p.z <= b.max.z + 0.4f)
                return b.max.y;
        }

        GameObject parking = GameObject.Find("ParkingLot");
        if (parking != null)
        {
            Renderer[] renderers = parking.GetComponentsInChildren<Renderer>();
            float bestY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.name.IndexOf("Sidewalk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                Bounds b = renderer.bounds;
                if (p.x < b.min.x - 0.5f || p.x > b.max.x + 0.5f
                    || p.z < b.min.z - 0.5f || p.z > b.max.z + 0.5f)
                    continue;
                if (b.max.y < bestY) continue;
                bestY = b.max.y;
                found = true;
            }
            if (found)
                return bestY;
        }

        if (lobby != null)
            return lobby.bounds.max.y;
        return 0.02f;
    }

    bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] == null || !skinned[i].enabled) continue;
            if (!found) { bounds = skinned[i].bounds; found = true; }
            else bounds.Encapsulate(skinned[i].bounds);
        }
        if (found) return true;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled) continue;
            if (renderers[i].GetComponentInParent<Canvas>() != null) continue;
            if (!found) { bounds = renderers[i].bounds; found = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return found;
    }

    static float HorizontalDist(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }
}
