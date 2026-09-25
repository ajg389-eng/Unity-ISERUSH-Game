using UnityEngine;

/// <summary>
/// Moves a low-poly vehicle along the highway and spins its separated wheels.
/// </summary>
public class DrivingCar : MonoBehaviour
{
    float speed;
    float direction;
    float endX;
    float bouncePhase;
    float bounceAmount;
    float wheelRadius = 0.35f;
    float groundY;
    Transform[] wheels;

    public float Direction => direction;

    public void SetPaused(bool paused)
    {
        enabled = !paused;
    }

    public void Configure(float driveSpeed, float driveDirection, float stopX)
    {
        speed = Mathf.Max(1f, driveSpeed);
        direction = Mathf.Sign(driveDirection);
        endX = stopX;
        groundY = transform.position.y;
        bouncePhase = Random.Range(0f, Mathf.PI * 2f);
        bounceAmount = Random.Range(0.015f, 0.035f);
        CacheWheels();
        if (GetComponent<VehicleHeadlights>() == null) gameObject.AddComponent<VehicleHeadlights>();
    }

    void CacheWheels()
    {
        var found = new System.Collections.Generic.List<Transform>();
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i] == transform) continue;
            if (all[i].name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) < 0)
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

        DisablePhysics();
    }

    void DisablePhysics()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bouncePhase += dt * 18f;
        float bodyBob = Mathf.Sin(bouncePhase) * bounceAmount;
        Vector3 pos = transform.position;
        pos.x += direction * speed * dt;
        pos.y = groundY + bodyBob;
        transform.position = pos;

        float spin = (speed * dt / Mathf.Max(0.08f, wheelRadius)) * Mathf.Rad2Deg * -direction;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            wheels[i].Rotate(spin, 0f, 0f, Space.Self);
        }

        bool finished = direction > 0f ? pos.x >= endX : pos.x <= endX;
        if (finished)
            Destroy(gameObject);
    }
}
