using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends a car for each automatic customer arrival.
/// </summary>
public class CustomerArrivalTraffic : MonoBehaviour
{
    static GameObject[] carPrefabs;

    public static bool TryDispatchCar(CustomerSpawner spawner)
    {
        if (spawner == null) return false;
        if (!ParkingLotDressing.TryClaimRandomStall(out int stallId, out Vector3 center, out Quaternion facing))
            return false;

        GameObject prefab = RandomCarPrefab();
        if (prefab == null)
        {
            ParkingLotDressing.ReleaseStall(stallId);
            return false;
        }

        ParkingLotDressing.RoadApproach(out Vector3 spawnPos, out Quaternion spawnFacing);
        GameObject car = Instantiate(prefab, spawnPos, spawnFacing);
        car.name = "CustomerCar";
        DisableColliders(car);
        DrivingCar stray = car.GetComponent<DrivingCar>();
        if (stray != null)
            Destroy(stray);

        var vehicle = car.AddComponent<LotArrivalVehicle>();
        vehicle.StallId = stallId;
        vehicle.SideDoor = true;
        vehicle.Arrive(center, facing, () =>
        {
            if (spawner == null)
            {
                vehicle.Leave();
                return;
            }

            Vector3 door = vehicle.PassengerDoor();
            if (!spawner.SpawnArrivingCustomer(door, vehicle))
                vehicle.Leave();
        });
        return true;
    }

    static GameObject RandomCarPrefab()
    {
        if (carPrefabs == null)
            carPrefabs = LoadCarPrefabs();
        if (carPrefabs == null || carPrefabs.Length == 0) return null;
        return carPrefabs[Random.Range(0, carPrefabs.Length)];
    }

    static GameObject[] LoadCarPrefabs()
    {
        GameObject[] loaded = Resources.LoadAll<GameObject>("Traffic");
        var cars = new List<GameObject>();
        var fallback = new List<GameObject>();
        for (int i = 0; i < loaded.Length; i++)
        {
            GameObject prefab = loaded[i];
            if (prefab == null) continue;
            string name = prefab.name;
            if (name.IndexOf("Bus", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            fallback.Add(prefab);
            if (name.IndexOf("Pick", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf("Truck", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf("Police", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            cars.Add(prefab);
        }

        if (cars.Count > 0) return cars.ToArray();
        return fallback.ToArray();
    }

    static void DisableColliders(GameObject vehicle)
    {
        if (vehicle == null) return;
        Collider[] colliders = vehicle.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }
}

/// <summary>
/// Keeps a customer tied to the car that dropped them off.
/// When they finish and walk back, the car pulls out.
/// </summary>
public class ParkedCarRide : MonoBehaviour
{
    LotArrivalVehicle vehicle;
    Vector3 boardSpot;
    bool hasSpot;

    public void Bind(LotArrivalVehicle ride, Vector3 returnSpot)
    {
        vehicle = ride;
        boardSpot = returnSpot;
        hasSpot = true;
        if (vehicle != null)
            vehicle.TrackPassenger();
    }

    public bool TryGetReturnPoint(out Vector3 door)
    {
        door = boardSpot;
        if (vehicle == null || !vehicle.IsParked) return false;
        if (!hasSpot)
            door = vehicle.PassengerDoor();
        return true;
    }

    void OnDestroy()
    {
        if (vehicle != null)
            vehicle.NotifyPassengerDone();
    }
}
