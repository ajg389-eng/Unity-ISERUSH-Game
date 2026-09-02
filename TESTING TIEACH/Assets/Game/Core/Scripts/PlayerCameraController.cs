using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    public float moveSpeed = 20f;
    public float zoomSpeed = 15f;
    [Tooltip("Time in seconds to reach target zoom (lower = snappier)")]
    public float zoomSmoothTime = 0.15f;
    float zoomVelocity;
    public float rotationSpeed = 5f;

    public float minY = 8f;
    public float maxY = 40f;

    float targetZoomY;

    void Start()
    {
        targetZoomY = transform.position.y;
    }

    void Update()
    {
        Move();
        Zoom();
        Rotate();
    }

    void Move()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        forward.y = 0f;
        right.y = 0f;

        Vector3 dir = (forward.normalized * v + right.normalized * h);
        transform.position += dir * moveSpeed * Time.deltaTime;
    }

    void Zoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetZoomY -= scroll * zoomSpeed;
            targetZoomY = Mathf.Clamp(targetZoomY, minY, maxY);
        }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return; // avoid NaN when paused (e.g. Management screen open)

        Vector3 pos = transform.position;
        if (float.IsNaN(pos.y)) pos.y = targetZoomY;
        if (float.IsNaN(zoomVelocity)) zoomVelocity = 0f;
        float newY = Mathf.SmoothDamp(pos.y, targetZoomY, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
        if (float.IsNaN(newY)) newY = targetZoomY;
        pos.y = newY;
        transform.position = pos;
    }

    void Rotate()
    {
        if (Input.GetMouseButton(1)) // Hold Right Mouse Button
        {
            float mouseX = Input.GetAxis("Mouse X");
            transform.Rotate(Vector3.up, mouseX * rotationSpeed * 300f * Time.deltaTime, Space.World);
        }
    }
}