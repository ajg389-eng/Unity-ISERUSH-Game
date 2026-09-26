using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives in from the highway, parks in a stall or the bus bay, then leaves
/// back onto the road when the visit is finished.
/// </summary>
public class LotArrivalVehicle : MonoBehaviour
{
    readonly List<Vector3> path = new List<Vector3>();
    int index;
    float speed = 11f;
    float cruise = 11f;
    bool leaving;
    bool settling;
    float settleT;
    Vector3 settleFrom;
    Quaternion settleFromRot;
    Quaternion stallFacing;
    Vector3 stall;
    Action onParked;
    bool parked;
    bool departWhenClear;
    Transform[] wheels;
    float wheelRadius = 0.35f;
    int passengersAlive;
    int boardingIndex;
    bool claimReleased;

    public int StallId = -1;
    public bool HoldsBusBay;
    public bool SideDoor = true;
    public bool HoldForBoarding;
    public bool HasPassengers => passengersAlive > 0;
    public bool IsParked => parked && !leaving;
    public bool IsLeaving => leaving;

    public Vector3 PassengerDoor()
    {
        if (!SideDoor)
        {
            Renderer renderer = GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                return new Vector3(bounds.min.x - 1.15f, transform.position.y, bounds.center.z);
            }
        }

        return transform.position - transform.right * 2.2f - transform.forward * 0.15f;
    }

    public Vector3 NextBoardingPoint()
    {
        Vector3 door = PassengerDoor();
        if (!HoldsBusBay)
            return door;

        // Line up beside the bus, outside the body. The first rider used to
        // spawn inside the mesh and then freeze after the order.
        Vector3 side = transform.right;
        side.y = 0f;
        if (side.sqrMagnitude < 0.01f)
            side = Vector3.forward;
        side.Normalize();
        float along = boardingIndex * 1.15f;
        boardingIndex++;
        return door + side * along;
    }

    public void TrackPassenger()
    {
        passengersAlive++;
    }

    public void NotifyPassengerDone()
    {
        passengersAlive = Mathf.Max(0, passengersAlive - 1);
        if (HoldForBoarding || passengersAlive > 0) return;
        departWhenClear = true;
    }

    public void Arrive(Vector3 stallCenter, Quaternion facing, Action parkedCallback, float driveSpeed = 11f)
    {
        stall = stallCenter;
        stallFacing = facing;
        onParked = parkedCallback;
        cruise = driveSpeed;
        speed = driveSpeed * 0.35f;
        settling = false;
        CacheWheels();
        if (GetComponent<VehicleHeadlights>() == null)
            gameObject.AddComponent<VehicleHeadlights>();
        path.Clear();
        BuildArrivePath(transform.position, stallCenter);
        VehiclePathMotion.RoundCorners(path, HoldsBusBay ? 4.8f : 2.4f);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    public void Leave()
    {
        if (leaving) return;
        leaving = true;
        parked = false;
        settling = false;
        cruise = HoldsBusBay ? 8f : 10f;
        speed = cruise * 0.3f;
        path.Clear();
        BuildLeavePath(transform.position);
        VehiclePathMotion.RoundCorners(path, HoldsBusBay ? 4.8f : 2.4f);
        index = 0;
        RoadTrafficController.BeginDeliveryLaneClearance();
    }

    void OnDestroy()
    {
        ReleaseClaim();
    }

    void ReleaseClaim()
    {
        if (claimReleased) return;
        claimReleased = true;
        if (StallId >= 0)
            ParkingLotDressing.ReleaseStall(StallId);
        if (HoldsBusBay)
            ParkingLotDressing.ReleaseBusBay();
    }

    void Update()
    {
        if (departWhenClear && parked && !leaving)
        {
            if (!PullOutIsClear())
                return;
            departWhenClear = false;
            Leave();
            return;
        }

        if (settling)
        {
            TickSettle();
            return;
        }

        if (index >= path.Count)
        {
            if (leaving)
            {
                if (ExtendLeaveToRoad())
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

        float yaw = HoldsBusBay ? 78f : 150f;
        int lookAhead = HoldsBusBay ? 5 : 3;
        if (!VehiclePathMotion.Advance(transform, path, ref index, ref speed, cruise, yaw, lookAhead, Time.deltaTime, wheels, wheelRadius))
            return;

        if (leaving)
        {
            if (ExtendLeaveToRoad())
                return;
            RoadTrafficController.EndDeliveryLaneClearance();
            Destroy(gameObject);
            return;
        }

        BeginSettle();
    }

    bool ExtendLeaveToRoad()
    {
        if (!RoadTrafficController.TryGetHighway(out _, out float eastX, out float roadZ, out _))
            return false;
        float laneZ = roadZ - RoadTrafficController.HighwayLaneOffset;
        if (transform.position.x >= eastX - 2f)
            return false;

        path.Clear();
        path.Add(transform.position);
        path.Add(new Vector3(eastX, transform.position.y, laneZ));
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

    bool PullOutIsClear()
    {
        float aisleX = AisleX(stall);
        float y = transform.position.y;
        Vector3 mouth = new Vector3(aisleX, y, stall.z);
        Vector3 lane = new Vector3(aisleX, y, LotEntryZ());
        const float clearance = 2.2f;
        if (VehiclePathMotion.PathIsBlocked(transform, transform.position, mouth, clearance))
            return false;
        if (VehiclePathMotion.PathIsBlocked(transform, mouth, lane, clearance))
            return false;

        if (RoadTrafficController.TryGetHighway(out _, out _, out float roadZ, out _))
        {
            float laneZ = roadZ - RoadTrafficController.HighwayLaneOffset;
            Vector3 merge = new Vector3(aisleX, y, laneZ);
            if (VehiclePathMotion.PathIsBlocked(transform, lane, merge, clearance))
                return false;
        }

        if (CustomerAI.ClosestWalkerAhead(transform.position, mouth - transform.position, 2.1f) < 7f)
            return false;
        if (CustomerAI.ClosestWalkerAhead(mouth, lane - mouth, 2.1f) < Vector3.Distance(mouth, lane) + 1.2f)
            return false;
        return true;
    }

    void BuildLeavePath(Vector3 from)
    {
        float aisleX = AisleX(stall);
        float lotEntryZ = LotEntryZ();
        float y = from.y;

        path.Add(from);
        if (HoldsBusBay)
            AddPoint(new Vector3(from.x, y, lotEntryZ));
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

/// <summary>
/// Shared steering for lot vehicles: rounded corners, eased speed, and a
/// lookahead heading so cars and the bus turn instead of pivoting in place.
/// </summary>
public static class VehiclePathMotion
{
    static readonly Dictionary<Transform, float> noseLengths = new Dictionary<Transform, float>();
    static int noseCacheFrame = -1;
    public static void RoundCorners(List<Vector3> path, float radius)
    {
        if (path == null || path.Count < 3) return;
        float y = path[0].y;
        var rounded = new List<Vector3>(path.Count * 4);
        Add(rounded, path[0], y);
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector3 prev = path[i - 1];
            Vector3 corner = path[i];
            Vector3 next = path[i + 1];
            Vector3 into = corner - prev;
            Vector3 outOf = next - corner;
            float inLen = into.magnitude;
            float outLen = outOf.magnitude;
            if (inLen < 0.4f || outLen < 0.4f)
            {
                Add(rounded, corner, y);
                continue;
            }

            into /= inLen;
            outOf /= outLen;
            float cut = Mathf.Min(radius, inLen * 0.42f, outLen * 0.42f);
            if (cut < 0.35f || Vector3.Dot(into, outOf) > 0.92f)
            {
                Add(rounded, corner, y);
                continue;
            }

            Vector3 start = corner - into * cut;
            Vector3 end = corner + outOf * cut;
            const int steps = 6;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float u = 1f - t;
                Vector3 point = (u * u) * start + (2f * u * t) * corner + (t * t) * end;
                Add(rounded, point, y);
            }
        }

        Add(rounded, path[path.Count - 1], y);
        path.Clear();
        for (int i = 0; i < rounded.Count; i++)
            path.Add(rounded[i]);
    }

    public static bool Advance(
        Transform body,
        List<Vector3> path,
        ref int index,
        ref float speed,
        float cruise,
        float yawPerSecond,
        int lookAhead,
        float deltaTime,
        Transform[] wheels,
        float wheelRadius)
    {
        if (body == null || path == null || index >= path.Count || deltaTime <= 0f)
            return index >= (path != null ? path.Count : 0);

        Vector3 pos = body.position;
        Vector3 target = path[index];
        target.y = pos.y;
        int look = Mathf.Min(path.Count - 1, index + Mathf.Max(1, lookAhead));
        Vector3 steerPoint = path[look];
        steerPoint.y = pos.y;
        Vector3 steer = steerPoint - pos;
        steer.y = 0f;

        float corner = steer.sqrMagnitude > 0.01f ? Vector3.Angle(body.forward, steer) : 0f;
        float remaining = 0f;
        Vector3 cursor = pos;
        for (int i = index; i < path.Count; i++)
        {
            Vector3 point = path[i];
            point.y = pos.y;
            remaining += Vector3.Distance(cursor, point);
            cursor = point;
        }

        float turnScale = Mathf.Lerp(1f, 0.75f, Mathf.InverseLerp(18f, 70f, corner));
        float endScale = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(remaining / 5.5f));
        float brakeForTurn = BrakeBeforeTurn(path, index, pos);
        // Ease off before a corner, then hold a steady pace. Never coast to a stop.
        float desired = Mathf.Max(3.2f, cruise) * turnScale * endScale * brakeForTurn;
        speed = Mathf.MoveTowards(speed, desired, deltaTime * cruise * 1.4f);

        if (steer.sqrMagnitude > 0.05f)
        {
            Quaternion want = Quaternion.LookRotation(steer.normalized, Vector3.up);
            body.rotation = Quaternion.RotateTowards(body.rotation, want, yawPerSecond * deltaTime);
        }

        float budget = speed * deltaTime;
        float moved = 0f;
        bool hold = HoldBeforeDriveway(body, path, index);
        // Spend the full travel budget across the short fillet segments. Each
        // segment still checks vehicles and walkers before advancing.
        while (budget > 0f && index < path.Count && !hold)
        {
            target = path[index];
            target.y = body.position.y;
            Vector3 drive = target - body.position;
            float distance = drive.magnitude;
            if (distance < 0.001f) { index++; continue; }
            float vehicleGap = ClosestVehicleAhead(body, drive, 2.15f);
            float walkerGap = CustomerAI.ClosestWalkerAhead(body.position, drive, 1.85f);
            float room = Mathf.Max(0f, Mathf.Min(vehicleGap - 1.2f, walkerGap - NoseLength(body) - 0.85f));
            float step = Mathf.Min(budget, Mathf.Min(distance, room));
            body.position += drive / distance * step;
            moved += step;
            budget -= step;
            if (step >= distance - 0.001f) index++;
            if (room < Mathf.Min(budget + step, distance)) { speed = 0f; break; }
        }
        if (hold) speed = 0f;
        if (wheels != null && moved > 0f)
        {
            float spin = (moved / Mathf.Max(0.08f, wheelRadius)) * Mathf.Rad2Deg;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                wheels[i].Rotate(-spin, 0f, 0f, Space.Self);
            }
        }


        return index >= path.Count;
    }

    static float BrakeBeforeTurn(List<Vector3> path, int index, Vector3 pos)
    {
        if (path == null || index >= path.Count) return 1f;

        Vector3 forward = path[index] - pos;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.05f) return 1f;
        forward.Normalize();

        // Fillets split one corner into small steps, so watch the heading
        // change over the next stretch instead of a single sharp point.
        float traveled = 0f;
        Vector3 at = pos;
        for (int i = index; i < path.Count && traveled < 14f; i++)
        {
            Vector3 point = path[i];
            point.y = at.y;
            Vector3 leg = point - at;
            leg.y = 0f;
            float step = leg.magnitude;
            if (step > 0.25f && Vector3.Angle(forward, leg) > 25f)
                return Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(1.4f, 13f, traveled));
            traveled += step;
            at = point;
        }

        return 1f;
    }

    static bool HoldBeforeDriveway(Transform self, List<Vector3> path, int index)
    {
        if (self == null || path == null || !ParkingLotDressing.HasDeliveryStall) return false;
        float aisleX = ParkingLotDressing.DeliveryAisleX;
        float fromAisle = Mathf.Abs(self.position.x - aisleX);
        if (fromAisle < 3.2f || fromAisle > 16f)
            return false;

        bool headingIn = false;
        int last = Mathf.Min(path.Count, index + 6);
        for (int i = index; i < last; i++)
        {
            if (Mathf.Abs(path[i].x - aisleX) < 2.4f)
            {
                headingIn = true;
                break;
            }
        }
        if (!headingIn) return false;
        return DrivewayOccupied(self, aisleX);
    }

    static bool DrivewayOccupied(Transform self, float aisleX)
    {
        LotArrivalVehicle[] cars = UnityEngine.Object.FindObjectsByType<LotArrivalVehicle>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cars.Length; i++)
        {
            LotArrivalVehicle car = cars[i];
            if (car == null || car.transform == self || car.IsParked) continue;
            if (Mathf.Abs(car.transform.position.x - aisleX) < 3.2f)
                return true;
        }

        DeliveryVan[] vans = UnityEngine.Object.FindObjectsByType<DeliveryVan>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < vans.Length; i++)
        {
            DeliveryVan van = vans[i];
            if (van == null || van.transform == self || van.IsParked) continue;
            if (Mathf.Abs(van.transform.position.x - aisleX) < 3.2f)
                return true;
        }
        return false;
    }

    public static float ClosestVehicleAhead(Transform self, Vector3 forward, float sideLimit)
    {
        if (self == null) return 80f;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return 80f;
        forward.Normalize();
        float best = 80f;
        float nose = NoseLength(self);

        LotArrivalVehicle[] cars = UnityEngine.Object.FindObjectsByType<LotArrivalVehicle>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cars.Length; i++)
        {
            LotArrivalVehicle car = cars[i];
            if (car == null || car.IsParked) continue;
            NoteAhead(self, car.transform, forward, sideLimit, nose, ref best);
        }

        DeliveryVan[] vans = UnityEngine.Object.FindObjectsByType<DeliveryVan>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < vans.Length; i++)
        {
            DeliveryVan van = vans[i];
            if (van == null || van.IsParked) continue;
            NoteAhead(self, van.transform, forward, sideLimit, nose, ref best);
        }

        DrivingCar[] traffic = UnityEngine.Object.FindObjectsByType<DrivingCar>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < traffic.Length; i++)
        {
            DrivingCar trafficCar = traffic[i];
            if (trafficCar == null) continue;
            NoteAhead(self, trafficCar.transform, forward, sideLimit, nose, ref best);
        }

        return best;
    }

    static void NoteAhead(Transform self, Transform other, Vector3 forward, float sideLimit, float nose, ref float best)
    {
        if (other == null || other == self) return;
        Vector3 to = other.position - self.position;
        to.y = 0f;
        float along = Vector3.Dot(to, forward);
        if (along < 0.3f) return;
        float side = Mathf.Abs(Vector3.Dot(to, new Vector3(-forward.z, 0f, forward.x)));
        if (side > sideLimit) return;
        float bumper = along - nose - NoseLength(other);
        if (bumper < best)
            best = bumper;
    }

    public static float NoseLength(Transform body)
    {
        if (body == null) return 1.8f;
        if (noseCacheFrame != Time.frameCount)
        {
            noseLengths.Clear();
            noseCacheFrame = Time.frameCount;
        }
        if (noseLengths.TryGetValue(body, out float cached)) return cached;
        // The first renderer can be a wheel, which understates pedestrian clearance.
        float length = 1.2f;
        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                Vector3 world = renderer.transform.TransformPoint(bounds.center + offset);
                length = Mathf.Max(length, Mathf.Abs(Vector3.Dot(world - body.position, body.forward)));
            }
        }
        noseLengths[body] = length;
        return length;
    }

    public static bool VehicleWithin(Vector3 world, float radius)
    {
        return NearestVehicle(null, world) < radius;
    }

    public static bool PathIsBlocked(Transform self, Vector3 from, Vector3 to, float clearance)
    {
        clearance = Mathf.Max(0.5f, clearance);
        LotArrivalVehicle[] cars = UnityEngine.Object.FindObjectsByType<LotArrivalVehicle>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cars.Length; i++)
        {
            LotArrivalVehicle car = cars[i];
            if (car == null || car.transform == self) continue;
            // A parked car is in its stall, not in the exit lane. A parked bus
            // still fills the bay, so the pull-out has to go around it.
            if (car.IsParked && !car.HoldsBusBay) continue;
            float gap = car.HoldsBusBay
                ? DistanceFromBody(car.transform, from, to)
                : DistanceToSegment(car.transform.position, from, to);
            if (gap < (car.HoldsBusBay ? 0.4f : clearance))
                return true;
        }

        DeliveryVan[] vans = UnityEngine.Object.FindObjectsByType<DeliveryVan>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < vans.Length; i++)
        {
            DeliveryVan van = vans[i];
            if (van == null || van.transform == self || van.IsParked) continue;
            if (DistanceToSegment(van.transform.position, from, to) < clearance)
                return true;
        }
        return false;
    }

    static float DistanceFromBody(Transform body, Vector3 from, Vector3 to)
    {
        Renderer renderer = body != null ? body.GetComponentInChildren<Renderer>() : null;
        if (renderer == null)
            return DistanceToSegment(body != null ? body.position : from, from, to);
        float best = float.MaxValue;
        for (int i = 0; i <= 4; i++)
        {
            Vector3 point = Vector3.Lerp(from, to, i / 4f);
            Vector3 closest = renderer.bounds.ClosestPoint(point);
            closest.y = point.y;
            best = Mathf.Min(best, Vector3.Distance(closest, point));
        }
        return best;
    }

    static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        point.y = a.y = b.y = 0f;
        Vector3 span = b - a;
        float length = span.sqrMagnitude;
        if (length < 0.0001f)
            return Vector3.Distance(point, a);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, span) / length);
        return Vector3.Distance(point, a + span * t);
    }

    static float NearestVehicle(Transform self, Vector3 world)
    {
        float best = 99f;
        LotArrivalVehicle[] cars = UnityEngine.Object.FindObjectsByType<LotArrivalVehicle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null || cars[i].transform == self) continue;
            float d = HorizontalDistance(world, cars[i].transform.position);
            if (d < best) best = d;
        }
        DeliveryVan[] vans = UnityEngine.Object.FindObjectsByType<DeliveryVan>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < vans.Length; i++)
        {
            if (vans[i] == null || vans[i].transform == self) continue;
            float d = HorizontalDistance(world, vans[i].transform.position);
            if (d < best) best = d;
        }
        return best;
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static void Add(List<Vector3> path, Vector3 point, float y)
    {
        point.y = y;
        if (path.Count > 0)
        {
            Vector3 last = path[path.Count - 1];
            last.y = point.y;
            if (Vector3.Distance(last, point) < 0.12f)
                return;
        }
        path.Add(point);
    }
}
