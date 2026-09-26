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
            return transform.position + right * -1.25f + back * 0.15f;
        }
    }

    public void Arrive(Vector3 stallCenter, Quaternion facing, Action parkedCallback)
    {
        stall = stallCenter;
        stallFacing = facing;
        onParked = parkedCallback;
        cruise = 10f;
        speed = 3.5f;
        settling = false;
        CacheWheels();
        if (GetComponent<VehicleHeadlights>() == null) gameObject.AddComponent<VehicleHeadlights>();
        path.Clear();
        BuildArrivePath(transform.position, stallCenter);
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
