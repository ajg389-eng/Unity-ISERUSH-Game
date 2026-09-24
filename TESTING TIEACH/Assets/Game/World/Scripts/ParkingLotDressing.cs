using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Paints parking stalls on the east (right) side of the restaurant lot
/// and parks a couple of cars in those spaces.
/// </summary>
public class ParkingLotDressing : MonoBehaviour
{
    public static Vector3 DeliveryStallCenter { get; private set; }
    public static Quaternion DeliveryStallFacing { get; private set; } = Quaternion.LookRotation(Vector3.left, Vector3.up);
    public static bool HasDeliveryStall { get; private set; }
    public static float DeliveryAisleX { get; private set; }
    public static float LotEntryZ { get; private set; }

    public static bool TryGetDeliveryStall(out Vector3 center, out Quaternion facing)
    {
        center = DeliveryStallCenter;
        facing = DeliveryStallFacing;
        return HasDeliveryStall;
    }

    const float StallWidth = 2.65f;
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
        int stallCount = Mathf.Max(3, Mathf.FloorToInt((north - south) / StallWidth));
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
                new Vector3(StallDepth, LineHeight, LineWidth));
        }

        ParkCars(lot, west, east, z0, stallCount);
        int middle = stallCount >= 3 ? stallCount / 2 : 1;
        Vector3 middleStall = StallCenter(west, east, z0, Mathf.Clamp(middle, 0, stallCount - 1));
        middleStall.y = lot.min.y;
        DeliveryStallCenter = middleStall;
        DeliveryStallFacing = Quaternion.LookRotation(Vector3.left, Vector3.up);
        DeliveryAisleX = west - 2.15f;
        LotEntryZ = lot.max.z - 0.65f;
        HasDeliveryStall = true;
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

    static void ParkCars(Bounds lot, float west, float east, float z0, int stallCount)
    {
        GameObject[] prefabs = Resources.LoadAll<GameObject>("Traffic");
        if (prefabs == null || prefabs.Length == 0) return;

        Renderer bus = FindBus();
        var occupied = new List<int>();
        int[] preferred = stallCount >= 3
            ? new[] { stallCount - 1 }
            : new[] { stallCount - 1 };

        for (int p = 0; p < preferred.Length; p++)
        {
            int stall = Mathf.Clamp(preferred[p], 0, stallCount - 1);
            if (occupied.Contains(stall)) continue;
            Vector3 stallCenter = StallCenter(west, east, z0, stall);
            if (bus != null && OverlapsBus(stallCenter, bus))
                continue;
            occupied.Add(stall);
            PlaceParkedCar(prefabs[p % prefabs.Length], stallCenter, lot.min.y);
        }
    }

    static Vector3 StallCenter(float west, float east, float z0, int stall)
    {
        return new Vector3((west + east) * 0.5f, 0f, z0 + (stall + 0.5f) * StallWidth);
    }

    static bool OverlapsBus(Vector3 stallCenter, Renderer bus)
    {
        Bounds pad = bus.bounds;
        pad.Expand(1.6f);
        return pad.Contains(new Vector3(stallCenter.x, pad.center.y, stallCenter.z));
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

    static void PlaceParkedCar(GameObject prefab, Vector3 stallCenter, float groundY)
    {
        if (prefab == null) return;
        Quaternion facing = Quaternion.LookRotation(Vector3.left, Vector3.up);
        GameObject car = Instantiate(prefab, new Vector3(stallCenter.x, groundY, stallCenter.z), facing);
        car.name = "Parked_" + prefab.name;
        Collider[] colliders = car.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;
    }
}
