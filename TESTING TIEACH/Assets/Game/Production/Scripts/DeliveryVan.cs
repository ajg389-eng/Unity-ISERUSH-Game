using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pickup that leaves a highway tunnel, parks in the middle stall, then
/// returns to a tunnel after the driver is back on board.
/// </summary>
public class DeliveryVan : MonoBehaviour
{
    readonly List<Vector3> path = new List<Vector3>();
    int index;
    float speed = 7.5f;
    bool leaving;
    Quaternion stallFacing;
    Vector3 stall;
    Action onParked;
    bool parked;
    Transform[] wheels;
    float wheelRadius = 0.35f;

    public Vector3 DriverDoorPosition
    {
        get
        {
            Vector3 right = transform.right;
            Vector3 back = -transform.forward;
            return transform.position + right * -1.25f + back * 0.15f;
        }
    }

    public void Arrive(Vector3 stallCenter, Quaternion facing, Action parkedCallback)
    {
        stall = stallCenter;
        stallFacing = facing;
        onParked = parkedCallback;
        speed = 12.6f;
        CacheWheels();
        if (GetComponent<VehicleHeadlights>() == null) gameObject.AddComponent<VehicleHeadlights>();
        path.Clear();
        BuildArrivePath(transform.position, stallCenter);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    public void Leave()
    {
        leaving = true;
        parked = false;
        speed = 14f;
        path.Clear();
        BuildLeavePath(transform.position);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    void Update()
    {
        if (index >= path.Count)
        {
            if (leaving)
            {
                RoadTrafficController.EndDeliveryLaneClearance();
                Destroy(gameObject);
                return;
            }

            if (!parked)
            {
                parked = true;
                transform.SetPositionAndRotation(stall, stallFacing);
                RoadTrafficController.EndDeliveryLaneClearance();
                onParked?.Invoke();
                onParked = null;
            }
            return;
        }

        if (RoadTrafficController.TryGetHighway(out _, out _, out float roadZ, out _))
        {
            if (Mathf.Abs(transform.position.z - roadZ) < 6f)
                RoadTrafficController.KeepDeliveryLaneClear(2f);
        }

        Vector3 target = path[index];
        target.y = transform.position.y;
        Vector3 to = target - transform.position;
        to.y = 0f;
        float step = speed * Time.deltaTime;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), Time.deltaTime * 5f);

        transform.position = Vector3.MoveTowards(transform.position, target, step);
        SpinWheels(step);
        if (Vector3.Distance(Flatten(transform.position), Flatten(target)) <= 0.35f)
            index++;
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

    static Vector3 Flatten(Vector3 p)
    {
        p.y = 0f;
        return p;
    }
}
