using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Paints parking stalls on the east side of the restaurant lot and
/// tracks which stalls are free for arriving cars and the delivery van.
/// The scene bus is hidden and kept as the pose a visiting bus parks in.
/// </summary>
public class ParkingLotDressing : MonoBehaviour
{
    sealed class Stall
    {
        public Vector3 center;
        public Quaternion facing;
        public bool taken;
        public bool overlapsBus;
        public bool deliveryOnly;
    }

    public static Vector3 DeliveryStallCenter { get; private set; }
    public static Quaternion DeliveryStallFacing { get; private set; } = Quaternion.LookRotation(Vector3.left, Vector3.up);
    public static bool HasDeliveryStall { get; private set; }
    public static bool HasParkingStalls => stalls.Count > 0;
    public static bool HasBusBay { get; private set; }
    public static float DeliveryAisleX { get; private set; }
    public static float LotEntryZ { get; private set; }

    static readonly List<Stall> stalls = new List<Stall>();
    static bool busBayTaken;
    static Vector3 busBayPosition;
    static Quaternion busBayRotation;
    static Bounds busBayBounds;
    static bool hasBusBayBounds;
    static GameObject busTemplate;

    public static bool TryClaimRandomStall(out int stallId, out Vector3 center, out Quaternion facing)
    {
        stallId = -1;
        center = default;
        facing = DeliveryStallFacing;
        int available = 0;
        for (int i = 0; i < stalls.Count; i++)
        {
            if (IsStallFree(stalls[i]))
                available++;
        }
        if (available == 0) return false;

        int choice = Random.Range(0, available);
        for (int i = 0; i < stalls.Count; i++)
        {
            if (!IsStallFree(stalls[i])) continue;
            if (choice > 0)
            {
                choice--;
                continue;
            }

            stalls[i].taken = true;
            stallId = i;
            center = stalls[i].center;
            facing = stalls[i].facing;
            return true;
        }

        return false;
    }

    public static void ReleaseStall(int stallId)
    {
        if (stallId < 0 || stallId >= stalls.Count) return;
        stalls[stallId].taken = false;
    }

    public static bool TryClaimBusBay(out Vector3 position, out Quaternion rotation)
    {
        position = busBayPosition;
        rotation = busBayRotation;
        if (!HasBusBay || busBayTaken) return false;
        for (int i = 0; i < stalls.Count; i++)
        {
            if (stalls[i].overlapsBus && stalls[i].taken)
                return false;
        }

        busBayTaken = true;
        return true;
    }

    public static void ReleaseBusBay()
    {
        busBayTaken = false;
    }

    public static GameObject CreateBus(Vector3 position, Quaternion rotation)
    {
        if (busTemplate == null) return null;
        GameObject bus = Instantiate(busTemplate, position, rotation);
        bus.SetActive(true);
        bus.name = "ArrivingBus";
        return bus;
    }

    public static void RoadApproach(out Vector3 spawnPos, out Quaternion spawnFacing)
    {
        if (RoadTrafficController.TryGetHighway(out float westX, out _, out float roadZ, out float roadY))
        {
            float laneZ = roadZ - RoadTrafficController.HighwayLaneOffset;
            float x = westX;
            for (int n = 0; n < 6; n++)
            {
                if (!VehiclePathMotion.VehicleWithin(new Vector3(x, roadY, laneZ), 7f))
                    break;
                x -= 8f;
            }

            spawnPos = new Vector3(x, roadY, laneZ);
            spawnFacing = Quaternion.LookRotation(Vector3.right, Vector3.up);
            return;
        }

        spawnPos = DeliveryStallCenter + Vector3.forward * 16f + Vector3.left * 4f;
        spawnFacing = Quaternion.LookRotation(Vector3.right, Vector3.up);
    }

    static bool IsStallFree(Stall stall)
    {
        if (stall == null || stall.taken || stall.deliveryOnly) return false;
        if (stall.overlapsBus && busBayTaken) return false;
        return true;
    }

    const float StallWidth = 4.4f;
    const float StallDepth = 5.4f;
    const float LineWidth = 0.09f;
    const float LineHeight = 0.035f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ParkingLotDressing>() != null) return;
        var go = new GameObject("ParkingLotDressing");
        go.AddComponent<ParkingLotDressing>();
    }

    void Start()
    {
        stalls.Clear();
        HasDeliveryStall = false;
        CaptureBusBay();
        if (!TryGetLotBounds(out Bounds lot))
            return;

        Material paint = CreateLineMaterial();
        Transform root = transform;
        root.SetParent(GameObject.Find("ParkingLot")?.transform, true);

        float paintY = lot.max.y + 0.025f;
        float east = lot.max.x - 0.28f;
        float west = east - StallDepth;
        float south = lot.min.z + 0.35f;
        float north = lot.max.z - 0.35f;
        int stallCount = Mathf.Max(1, Mathf.FloorToInt((north - south) / StallWidth));
        float used = stallCount * StallWidth;
        float z0 = (south + north - used) * 0.5f;

        AddLine(root, paint,
            new Vector3((west + east) * 0.5f, paintY, z0),
            new Vector3(StallDepth, LineHeight, LineWidth));
        AddLine(root, paint,
            new Vector3((west + east) * 0.5f, paintY, z0 + used),
            new Vector3(StallDepth, LineHeight, LineWidth));
        AddLine(root, paint,
            new Vector3(east, paintY, z0 + used * 0.5f),
            new Vector3(LineWidth, LineHeight, used));

        for (int i = 1; i < stallCount; i++)
        {
            AddLine(root, paint,
                new Vector3((west + east) * 0.5f, paintY, z0 + i * StallWidth),
                new Vector3(StallDepth, LineHeight, 1.2f));
        }

        Quaternion facing = Quaternion.LookRotation(Vector3.left, Vector3.up);
        for (int i = 0; i < stallCount; i++)
        {
            Vector3 center = StallCenter(west, east, z0, i);
            center.y = lot.min.y;
            stalls.Add(new Stall
            {
                center = center,
                facing = facing,
                overlapsBus = OverlapsBusBay(center)
            });
        }

        int middle = stallCount >= 3 ? stallCount / 2 : 1;
        DeliveryStallCenter = stalls[Mathf.Clamp(middle, 0, stallCount - 1)].center;
        DeliveryStallFacing = facing;
        stalls[Mathf.Clamp(middle, 0, stallCount - 1)].deliveryOnly = true;
        DeliveryAisleX = west - 2.15f;
        LotEntryZ = lot.max.z - 0.65f;
        HasDeliveryStall = stalls.Count > 0;
    }

    public static Vector3 PedestrianAislePoint(Vector3 door)
    {
        return HasDeliveryStall ? new Vector3(DeliveryAisleX, door.y, door.z) : door;
    }

    static void CaptureBusBay()
    {
        HasBusBay = false;
        hasBusBayBounds = false;
        busBayTaken = false;
        busTemplate = null;

        GameObject root = FindBusRoot();
        if (root == null) return;

        busBayPosition = root.transform.position;
        busBayRotation = root.transform.rotation;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderers[i].bounds);
        }

        if (found)
        {
            busBayBounds = bounds;
            hasBusBayBounds = true;
        }

        busTemplate = root;
        HasBusBay = true;
        root.SetActive(false);
    }

    static bool OverlapsBusBay(Vector3 stallCenter)
    {
        if (!hasBusBayBounds) return false;
        Bounds pad = busBayBounds;
        pad.Expand(1.6f);
        return pad.Contains(new Vector3(stallCenter.x, pad.center.y, stallCenter.z));
    }

    static GameObject FindBusRoot()
    {
        Renderer bus = FindBus();
        if (bus == null) return null;
        Transform root = bus.transform;
        for (Transform t = bus.transform; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Bus", System.StringComparison.OrdinalIgnoreCase) >= 0)
                root = t;
        }
        return root.gameObject;
    }

    static bool TryGetLotBounds(out Bounds lot)
    {
        lot = default;
        GameObject parking = GameObject.Find("ParkingLot");
        if (parking == null) return false;

        Renderer[] renderers = parking.GetComponentsInChildren<Renderer>();
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer.name.IndexOf("Tile", System.StringComparison.OrdinalIgnoreCase) < 0
                && renderer.name.IndexOf("Road", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (renderer.name.IndexOf("Sidewalk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (!found)
            {
                lot = renderer.bounds;
                found = true;
            }
            else
                lot.Encapsulate(renderer.bounds);
        }

        return found;
    }

    static Material CreateLineMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader);
        Color paint = new Color(0.96f, 0.96f, 0.9f, 1f);
        material.color = paint;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", paint);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", paint);
        material.renderQueue = 2450;
        return material;
    }

    static void AddLine(Transform parent, Material paint, Vector3 center, Vector3 size)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = "StallLine";
        line.transform.SetParent(parent, true);
        line.transform.position = center;
        line.transform.localScale = size;
        Collider collider = line.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        MeshRenderer renderer = line.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = paint;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Vector3 StallCenter(float west, float east, float z0, int stall)
    {
        return new Vector3((west + east) * 0.5f, 0f, z0 + (stall + 0.5f) * StallWidth);
    }

    public static bool TryGetBusBounds(out Bounds bounds)
    {
        Renderer bus = FindBus();
        if (bus == null)
        {
            bounds = default;
            return false;
        }
        bounds = bus.bounds;
        return true;
    }

    static Renderer FindBus()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Renderer best = null;
        float bestSize = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            bool isBus = false;
            for (Transform t = renderer.transform; t != null; t = t.parent)
            {
                if (t.name.IndexOf("Bus", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isBus = true;
                    break;
                }
            }
            if (!isBus || renderer.GetComponentInParent<Canvas>() != null) continue;
            float size = renderer.bounds.size.sqrMagnitude;
            if (size <= bestSize) continue;
            best = renderer;
            bestSize = size;
        }
        return best;
    }
}
