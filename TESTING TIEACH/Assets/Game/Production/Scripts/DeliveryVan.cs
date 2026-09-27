using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pickup that leaves a highway tunnel, parks in an open stall, then
/// returns to a tunnel after the driver is back on board.
/// </summary>
public class DeliveryVan : MonoBehaviour
{
    readonly List<Vector3> path = new List<Vector3>();
    int index;
    float speed = 7.5f;
    float cruise = 12.6f;
    bool leaving;
    bool settling;
    float settleT;
    Vector3 settleFrom;
    Quaternion settleFromRot;
    Quaternion stallFacing;
    Vector3 stall;
    Action onParked;
    bool parked;
    Transform[] wheels;
    float wheelRadius = 0.35f;

    public int StallId = -1;
    public bool IsParked => parked && !leaving;
    bool stallReleased;

    public Vector3 DriverDoorPosition
    {
        get
        {
            Vector3 right = transform.right;
            Vector3 back = -transform.forward;
            return transform.position + right * -2.2f + back * 0.15f;
        }
    }

    public void Arrive(Vector3 stallCenter, Quaternion facing, Action parkedCallback)
    {
        stallFacing = facing;
        stall = FitOnPavement(stallCenter);
        onParked = parkedCallback;
        cruise = 10f;
        speed = 3.5f;
        settling = false;
        CacheWheels();
        LoadCargo();
        if (GetComponent<VehicleHeadlights>() == null) gameObject.AddComponent<VehicleHeadlights>();
        path.Clear();
        BuildArrivePath(transform.position, stall);
        VehiclePathMotion.RoundCorners(path, 3.1f);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    void OnDestroy()
    {
        if (stallReleased || StallId < 0) return;
        stallReleased = true;
        ParkingLotDressing.ReleaseStall(StallId);
    }

    public void Leave()
    {
        leaving = true;
        parked = false;
        settling = false;
        cruise = 11f;
        speed = 3.5f;
        path.Clear();
        BuildLeavePath(transform.position);
        VehiclePathMotion.RoundCorners(path, 3.1f);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    void Update()
    {
        if (settling)
        {
            TickSettle();
            return;
        }

        if (index >= path.Count)
        {
            if (leaving)
            {
                if (ContinueToRoadExit())
                    return;
                RoadTrafficController.EndDeliveryLaneClearance();
                Destroy(gameObject);
                return;
            }

            if (!parked)
                BeginSettle();
            return;
        }

        if (RoadTrafficController.TryGetHighway(out _, out _, out float roadZ, out _))
        {
            if (Mathf.Abs(transform.position.z - roadZ) < 6f)
                RoadTrafficController.KeepDeliveryLaneClear(2f);
        }

        if (!VehiclePathMotion.Advance(transform, path, ref index, ref speed, cruise, 120f, 3, Time.deltaTime, wheels, wheelRadius))
            return;

        if (leaving)
        {
            if (ContinueToRoadExit())
                return;
            RoadTrafficController.EndDeliveryLaneClearance();
            Destroy(gameObject);
            return;
        }

        BeginSettle();
    }

    bool ContinueToRoadExit()
    {
        if (!RoadTrafficController.TryGetHighway(out _, out float eastX, out float roadZ, out _))
            return false;
        if (transform.position.x >= eastX - 2f)
            return false;

        path.Clear();
        path.Add(transform.position);
        path.Add(new Vector3(eastX, transform.position.y, roadZ - RoadTrafficController.HighwayLaneOffset));
        index = 0;
        return true;
    }

    void BeginSettle()
    {
        settling = true;
        settleT = 0f;
        settleFrom = transform.position;
        settleFromRot = transform.rotation;
        // A parking bay accepts either heading; keep the one nearest the approach
        // instead of forcing a half-turn after the vehicle has already arrived.
        Quaternion opposite = stallFacing * Quaternion.Euler(0f, 180f, 0f);
        if (Quaternion.Angle(settleFromRot, opposite) < Quaternion.Angle(settleFromRot, stallFacing))
            stallFacing = opposite;
    }

    void TickSettle()
    {
        settleT += Time.deltaTime / 0.7f;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(settleT));
        Vector3 pos = Vector3.Lerp(settleFrom, stall, t);
        pos.y = Mathf.Lerp(settleFrom.y, stall.y, t);
        transform.SetPositionAndRotation(pos, Quaternion.Slerp(settleFromRot, stallFacing, t));
        SpinWheels(Mathf.Lerp(speed, 0f, t) * Time.deltaTime);
        if (t < 1f) return;

        settling = false;
        parked = true;
        speed = 0f;
        RoadTrafficController.EndDeliveryLaneClearance();
        onParked?.Invoke();
        onParked = null;
    }

    void BuildArrivePath(Vector3 from, Vector3 stallCenter)
    {
        float aisleX = AisleX(stallCenter);
        float lotEntryZ = LotEntryZ();
        float y = from.y;

        path.Add(from);
        if (RoadTrafficController.TryGetHighway(out _, out _, out float roadZ, out _))
        {
            float laneZ = roadZ - RoadTrafficController.HighwayLaneOffset;
            AddPoint(new Vector3(aisleX, y, laneZ));
            AddPoint(new Vector3(aisleX, y, lotEntryZ));
        }
        else
        {
            AddPoint(new Vector3(aisleX, y, stallCenter.z + 10f));
        }

        AddPoint(new Vector3(aisleX, y, stallCenter.z));
        AddPoint(stallCenter);
    }

    void BuildLeavePath(Vector3 from)
    {
        float aisleX = AisleX(stall);
        float lotEntryZ = LotEntryZ();
        float y = from.y;

        path.Add(from);
        AddPoint(new Vector3(aisleX, y, stall.z));
        AddPoint(new Vector3(aisleX, y, lotEntryZ));

        if (RoadTrafficController.TryGetHighway(out _, out float eastX, out float roadZ, out _))
        {
            float laneZ = roadZ - RoadTrafficController.HighwayLaneOffset;
            AddPoint(new Vector3(aisleX, y, laneZ));
            AddPoint(new Vector3(eastX, y, laneZ));
        }
        else
        {
            AddPoint(new Vector3(stall.x + 24f, y, stall.z + 18f));
        }
    }

    void AddPoint(Vector3 point)
    {
        if (path.Count > 0 && Horizontal(path[path.Count - 1], point) < 0.4f)
            return;
        path.Add(point);
    }

    static float AisleX(Vector3 stallCenter)
    {
        if (ParkingLotDressing.HasDeliveryStall)
            return ParkingLotDressing.DeliveryAisleX;
        return stallCenter.x - 6.2f;
    }

    static float LotEntryZ()
    {
        if (ParkingLotDressing.HasDeliveryStall)
            return ParkingLotDressing.LotEntryZ;
        return stallZFallback();
    }

    static float stallZFallback()
    {
        return ParkingLotDressing.DeliveryStallCenter.z + 8f;
    }

    Vector3 FitOnPavement(Vector3 desired)
    {
        if (!ParkingLotDressing.TryGetPavementBounds(out Bounds pavement))
            return desired;
        if (!TryGetBodyBounds(out Bounds vehicle))
            return desired;

        Vector3 offset = vehicle.center - transform.position;
        offset.y = 0f;
        float halfX = vehicle.extents.x + 0.25f;
        float halfZ = vehicle.extents.z + 0.25f;
        Vector3 center = desired + offset;
        float minX = pavement.min.x + halfX;
        float maxX = pavement.max.x - halfX;
        float minZ = pavement.min.z + halfZ;
        float maxZ = pavement.max.z - halfZ;
        center.x = minX <= maxX ? Mathf.Clamp(center.x, minX, maxX) : pavement.center.x;
        center.z = minZ <= maxZ ? Mathf.Clamp(center.z, minZ, maxZ) : pavement.center.z;
        Vector3 parked = center - offset;
        parked.y = desired.y;
        return parked;
    }

    void LoadCargo()
    {
        if (transform.Find("Cargo") != null) return;
        if (!TryGetBodyBounds(out Bounds body)) return;
        if (!TryGetBed(body, out Vector3 bedStart, out Vector3 bedEnd, out Vector3 right, out float width))
            return;

        float length = Vector3.Distance(bedStart, bedEnd);
        if (length < 0.4f || width < 0.4f) return;
        Vector3 along = (bedEnd - bedStart) / length;
        int rows = Mathf.Clamp(Mathf.FloorToInt(length / 0.7f), 2, 4);
        int cols = Mathf.Clamp(Mathf.FloorToInt(width / 0.66f), 2, 3);
        float boxL = length / rows * 0.78f;
        float boxW = width / cols * 0.78f;
        float boxH = Mathf.Min(boxL, boxW) * 0.82f;
        float floorY = body.min.y + body.size.y * 0.46f;
        Vector3 usableStart = bedStart + along * (boxL * 0.5f);
        Vector3 usableEnd = bedEnd - along * (boxL * 0.5f);
        float usableWidth = Mathf.Max(0.2f, width - boxW);

        var cargo = new GameObject("Cargo");
        cargo.transform.SetParent(transform, false);
        Color cardboard = new Color(0.72f, 0.55f, 0.32f);
        Color cardboardDark = new Color(0.55f, 0.4f, 0.22f);
        for (int row = 0; row < rows; row++)
        {
            float rowT = rows == 1 ? 0.5f : row / (float)(rows - 1);
            Vector3 rowCenter = Vector3.Lerp(usableStart, usableEnd, rowT);
            for (int col = 0; col < cols; col++)
            {
                float colT = cols == 1 ? 0f : col / (float)(cols - 1) - 0.5f;
                Vector3 spot = rowCenter + right * (colT * usableWidth);
                spot.y = floorY + boxH * 0.5f;
                Color color = ((row + col) % 2 == 0) ? cardboard : cardboardDark;
                PlaceCargoBox(cargo.transform, spot, new Vector3(boxW, boxH, boxL), color);
                if (row >= rows / 2 && (row + col) % 2 == 0)
                {
                    Vector3 stacked = spot + Vector3.up * boxH;
                    PlaceCargoBox(cargo.transform, stacked, new Vector3(boxW * 0.92f, boxH * 0.85f, boxL * 0.92f),
                        color == cardboard ? cardboardDark : cardboard);
                }
            }
        }
    }

    bool TryGetBed(Bounds body, out Vector3 bedStart, out Vector3 bedEnd, out Vector3 right, out float width)
    {
        bedStart = bedEnd = right = Vector3.zero;
        width = 0f;
        Vector3 front = Vector3.zero;
        Vector3 rear = Vector3.zero;
        int frontCount = 0;
        int rearCount = 0;
        Transform[] parts = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i];
            if (part == null) continue;
            string name = part.name;
            if (name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool isFront = name.IndexOf("FR", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("FL", System.StringComparison.Ordinal) >= 0;
            bool isRear = name.IndexOf("RR", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("RL", System.StringComparison.Ordinal) >= 0;
            if (isFront)
            {
                front += part.position;
                frontCount++;
            }
            else if (isRear)
            {
                rear += part.position;
                rearCount++;
            }
        }

        Vector3 forward;
        if (frontCount > 0 && rearCount > 0)
        {
            front /= frontCount;
            rear /= rearCount;
            forward = front - rear;
        }
        else
            forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return false;
        forward.Normalize();
        right = Vector3.Cross(Vector3.up, forward);
        float along = ExtentAlong(body, forward);
        float across = ExtentAlong(body, right);
        // Stay inside the rails. The mesh bounds include the cab, bumper, and side trim.
        float rearLimit = along * 0.72f;
        float sideLimit = across * 0.62f;
        width = sideLimit * 2f;
        if (frontCount > 0 && rearCount > 0)
        {
            float wheelbase = Vector3.Distance(front, rear);
            bedStart = Vector3.Lerp(front, rear, 0.55f);
            bedEnd = rear - forward * (wheelbase * 0.12f);
        }
        else
        {
            bedStart = body.center - forward * (along * 0.08f);
            bedEnd = body.center - forward * (along * 0.62f);
        }
        bedStart = ClampToBed(body.center, bedStart, forward, right, rearLimit, sideLimit);
        bedEnd = ClampToBed(body.center, bedEnd, forward, right, rearLimit, sideLimit);
        bedStart.y = bedEnd.y = body.center.y;
        return true;
    }

    static Vector3 ClampToBed(Vector3 origin, Vector3 point, Vector3 forward, Vector3 right, float rearLimit, float sideLimit)
    {
        Vector3 delta = point - origin;
        float along = Mathf.Clamp(Vector3.Dot(delta, forward), -rearLimit, rearLimit);
        float side = Mathf.Clamp(Vector3.Dot(delta, right), -sideLimit, sideLimit);
        return origin + forward * along + right * side;
    }

    static void PlaceCargoBox(Transform parent, Vector3 worldPos, Vector3 scale, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "CargoBox";
        box.transform.SetParent(parent, true);
        box.transform.position = worldPos;
        box.transform.rotation = parent.parent != null ? parent.parent.rotation : Quaternion.identity;
        box.transform.localScale = scale;
        Collider collider = box.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer == null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        renderer.material = material;
    }

    bool TryGetBodyBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.name == "CargoBox") continue;
            if (renderer.transform.name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
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

    static float ExtentAlong(Bounds bounds, Vector3 axis)
    {
        axis.Normalize();
        return Mathf.Abs(bounds.extents.x * axis.x)
            + Mathf.Abs(bounds.extents.y * axis.y)
            + Mathf.Abs(bounds.extents.z * axis.z);
    }

    void CacheWheels()
    {
        var found = new List<Transform>();
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i] == transform) continue;
            if (all[i].name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            found.Add(all[i]);
        }
        wheels = found.ToArray();

        float radiusSum = 0f;
        int radiusCount = 0;
        for (int i = 0; i < wheels.Length; i++)
        {
            Renderer renderer = wheels[i].GetComponent<Renderer>();
            if (renderer == null) continue;
            radiusSum += Mathf.Max(0.12f, renderer.bounds.extents.y);
            radiusCount++;
        }
        if (radiusCount > 0)
            wheelRadius = radiusSum / radiusCount;
    }

    void SpinWheels(float step)
    {
        if (wheels == null || wheels.Length == 0 || step <= 0f) return;
        float spin = (step / Mathf.Max(0.08f, wheelRadius)) * Mathf.Rad2Deg;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            wheels[i].Rotate(-spin, 0f, 0f, Space.Self);
        }
    }

    static float Horizontal(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }
}
