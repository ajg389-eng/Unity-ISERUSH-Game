using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

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
    [Tooltip("Degrees rotated per mouse input while holding right mouse button.")]
    public float rotationSpeed = 5f;
    [Tooltip("Vertical free-look limits. Kept just inside 90 degrees to prevent camera flips.")]
    public float minPitch = -89f;
    public float maxPitch = 89f;
    [Tooltip("Time used to smooth right-mouse free-look rotation.")]
    [Range(0.01f, 0.3f)] public float rotationSmoothTime = 0.065f;
    [Tooltip("Time for the camera to pan to a selected worker or flow.")]
    public float focusSmoothTime = 0.28f;

    public float minY = 8f;
    public float maxY = 40f;

    [Header("Movement Bounds")]
    [Tooltip("Extra radius beyond the work floor's corners.")]
    [Min(0f)] public float movementBoundsMargin = 3f;
    [Tooltip("Scales the complete movement radius. Kept at three so the camera can frame the restaurant from a distance.")]
    [Min(1f)] public float movementBoundsRadiusMultiplier = 3f;

    float targetZoomY;
    Vector3 currentMoveVelocity;
    Vector3 focusVelocity;
    Vector3 focusTargetPosition;
    bool isFocusing;
    float yaw;
    float pitch;
    float targetYaw;
    float targetPitch;
    float yawVelocity;
    float pitchVelocity;
    bool freeLooking;

    void Start()
    {
        targetZoomY = transform.position.y;
        Vector3 initialEuler = transform.eulerAngles;
        yaw = initialEuler.y;
        pitch = initialEuler.x > 180f ? initialEuler.x - 360f : initialEuler.x;
        targetYaw = yaw;
        targetPitch = pitch;
        CameraWallCutaway.EnsureExists();
    }

    void Update()
    {
        if (PauseMenuUI.IsOpen)
        {
            currentMoveVelocity = Vector3.zero;
            zoomVelocity = 0f;
            return;
        }

        if (UIInputFocusGuard.IsTyping)
        {
            currentMoveVelocity = Vector3.zero;
            if (isFocusing)
                UpdateFocusPan();
            return;
        }

        bool manualMove = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f
            || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;
        if (isFocusing && !manualMove)
            UpdateFocusPan();
        else
        {
            if (manualMove) isFocusing = false;
            Move();
        }
        Zoom();
        Rotate();
        ClampToPlayableBounds();
    }

    public void PanTo(Vector3 worldPoint)
    {
        Vector3 cameraPosition = transform.position;
        Vector3 forward = transform.forward;
        Vector3 currentCenter = cameraPosition;

        if (Mathf.Abs(forward.y) > 0.001f)
        {
            float distanceToPlane = (worldPoint.y - cameraPosition.y) / forward.y;
            if (distanceToPlane > 0f)
                currentCenter = cameraPosition + forward * distanceToPlane;
        }

        Vector3 offset = worldPoint - currentCenter;
        focusTargetPosition = new Vector3(
            cameraPosition.x + offset.x,
            cameraPosition.y,
            cameraPosition.z + offset.z);
        focusTargetPosition = ClampPositionToPlayableBounds(focusTargetPosition);
        focusVelocity = Vector3.zero;
        currentMoveVelocity = Vector3.zero;
        isFocusing = true;
    }

    public void PanTo(Transform target)
    {
        if (target != null)
            PanTo(target.position);
    }

    public void PanTo(ProductionFlowPlan flow)
    {
        if (flow == null) return;

        Vector3 total = Vector3.zero;
        int count = 0;
        if (flow.stations != null)
        {
            foreach (GameObject station in flow.stations)
            {
                if (station == null) continue;
                total += station.transform.position;
                count++;
            }
        }

        if (count == 0 && flow.workers != null)
        {
            foreach (KitchenEmployee worker in flow.workers)
            {
                if (worker == null) continue;
                total += worker.transform.position;
                count++;
            }
        }

        if (count > 0)
            PanTo(total / count);
    }

    void UpdateFocusPan()
    {
        float dt = InteractionDeltaTime;
        if (dt <= 0f) return;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            focusTargetPosition,
            ref focusVelocity,
            Mathf.Max(0.05f, focusSmoothTime),
            Mathf.Infinity,
            dt);
        ClampToPlayableBounds();

        if ((transform.position - focusTargetPosition).sqrMagnitude < 0.0025f
            && focusVelocity.sqrMagnitude < 0.0025f)
        {
            transform.position = focusTargetPosition;
            focusVelocity = Vector3.zero;
            isFocusing = false;
        }
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
        ClampToPlayableBounds();
    }

    void Zoom()
    {
        if (ShouldBlockZoom())
            return;

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
        if (Input.GetMouseButtonDown(1))
        {
            freeLooking = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (freeLooking && Input.GetMouseButton(1))
        {
            // Full editor-style free-look: horizontal movement changes yaw and
            // vertical movement changes pitch. No automatic return to a preset tilt.
            targetYaw += Input.GetAxisRaw("Mouse X") * rotationSpeed;
            targetPitch -= Input.GetAxisRaw("Mouse Y") * rotationSpeed;
            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        }

        // Continue a very short ease-out after releasing RMB rather than stopping
        // on the last raw mouse delta.
        float dt = Mathf.Max(0.0001f, InteractionDeltaTime);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, rotationSmoothTime, Mathf.Infinity, dt);
        pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime, Mathf.Infinity, dt);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (freeLooking && Input.GetMouseButtonUp(1))
        {
            freeLooking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void OnDisable()
    {
        if (!freeLooking) return;
        freeLooking = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ClampToPlayableBounds()
    {
        transform.position = ClampPositionToPlayableBounds(transform.position);
    }

    Vector3 ClampPositionToPlayableBounds(Vector3 cameraPosition)
    {
        GridManager grid = GridManager.Instance;
        if (grid == null || grid.Width <= 0 || grid.Height <= 0)
            return cameraPosition;

        float floorWidth = grid.Width * grid.cellSize;
        float floorDepth = grid.Height * grid.cellSize;
        Vector2 floorCenter = new Vector2(
            grid.Origin.x + floorWidth * 0.5f,
            grid.Origin.z + floorDepth * 0.5f);
        float floorCornerRadius = 0.5f * Mathf.Sqrt(floorWidth * floorWidth + floorDepth * floorDepth);
        float allowedRadius = (floorCornerRadius + Mathf.Max(0f, movementBoundsMargin))
            * Mathf.Max(1f, movementBoundsRadiusMultiplier);

        // Clamp the camera's world position to a fixed circle around the work
        // floor. Using the camera's projected look point here makes orbiting
        // rotate that point and incorrectly pushes the camera around.
        Vector2 centerOffset = new Vector2(cameraPosition.x, cameraPosition.z) - floorCenter;
        if (centerOffset.sqrMagnitude > allowedRadius * allowedRadius)
        {
            Vector2 clampedPosition = floorCenter + centerOffset.normalized * allowedRadius;
            cameraPosition.x = clampedPosition.x;
            cameraPosition.z = clampedPosition.y;
        }
        return cameraPosition;
    }

    /// <summary>Real-time delta so camera keeps moving while simulation is paused.</summary>
    static float InteractionDeltaTime => Time.unscaledDeltaTime;

    static readonly List<RaycastResult> ZoomRaycastHits = new List<RaycastResult>();

    static bool ShouldBlockZoom()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        var pointer = new PointerEventData(es) { position = Input.mousePosition };
        ZoomRaycastHits.Clear();
        es.RaycastAll(pointer, ZoomRaycastHits);

        for (int i = 0; i < ZoomRaycastHits.Count; i++)
        {
            var go = ZoomRaycastHits[i].gameObject;
            if (go != null && go.GetComponentInParent<ScrollRect>() != null)
                return true;
        }

        return false;
    }
}

public static class UIInputFocusGuard
{
    public static bool IsTyping
    {
        get
        {
            var eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null) return false;

            var tmpInput = selected.GetComponentInParent<TMP_InputField>();
            if (tmpInput != null && tmpInput.isFocused) return true;

            var legacyInput = selected.GetComponentInParent<InputField>();
            return legacyInput != null && legacyInput.isFocused;
        }
    }

}
