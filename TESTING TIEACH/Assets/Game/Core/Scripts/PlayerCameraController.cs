using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    public float moveSpeed = 30f;
    [Tooltip("How fast WASD reaches full speed. Higher = tighter / more responsive.")]
    public float moveResponse = 42f;
    [Tooltip("How fast movement stops after releasing keys. Higher = less coasting.")]
    public float moveStopResponse = 32f;
    public float zoomSpeed = 15f;
    [Tooltip("Time in seconds to reach target zoom (lower = snappier)")]
    public float zoomSmoothTime = 0.1f;
    float zoomVelocity;
    public float rotationSpeed = 5f;

    public float minY = 8f;
    public float maxY = 40f;

    float targetZoomY;
    Vector3 currentMoveVelocity;

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
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        forward.y = 0f;
        right.y = 0f;

        Vector3 desiredDir = forward.normalized * v + right.normalized * h;
        if (desiredDir.sqrMagnitude > 1f)
            desiredDir.Normalize();

        Vector3 targetVelocity = desiredDir * moveSpeed;

        float dt = InteractionDeltaTime;
        if (dt <= 0f) return;

        bool hasInput = targetVelocity.sqrMagnitude > 0.01f;
        float response = hasInput ? moveResponse : moveStopResponse;
        float blend = 1f - Mathf.Exp(-response * dt);
        currentMoveVelocity = Vector3.Lerp(currentMoveVelocity, targetVelocity, blend);

        if (currentMoveVelocity.sqrMagnitude < 0.0001f)
            return;

        Vector3 pos = transform.position;
        pos += currentMoveVelocity * dt;
        transform.position = pos;
    }

    void Zoom()
    {
        float scroll = Input.mouseScrollDelta.y * 0.1f;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetZoomY -= scroll * zoomSpeed;
            targetZoomY = Mathf.Clamp(targetZoomY, minY, maxY);
        }

        float dt = InteractionDeltaTime;
        if (dt <= 0f) return;

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
            float mouseX = Input.GetAxisRaw("Mouse X");
            transform.Rotate(Vector3.up, mouseX * rotationSpeed * 300f * InteractionDeltaTime, Space.World);
        }
    }

    /// <summary>Real-time delta so camera keeps moving while simulation is paused.</summary>
    static float InteractionDeltaTime => Time.unscaledDeltaTime;
}
