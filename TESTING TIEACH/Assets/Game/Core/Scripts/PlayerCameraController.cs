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
    public float rotationSpeed = 5f;
    [Tooltip("Time for the camera to pan to a selected worker or flow.")]
    public float focusSmoothTime = 0.28f;

    public float minY = 8f;
    public float maxY = 40f;

    [Header("Movement Bounds")]
    [Tooltip("Extra radius beyond the work floor's corners.")]
    [Min(0f)] public float movementBoundsMargin = 3f;

    float targetZoomY;
    Vector3 currentMoveVelocity;
    Vector3 focusVelocity;
    Vector3 focusTargetPosition;
    bool isFocusing;

    void Start()
    {
        targetZoomY = transform.position.y;
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
        if (Input.GetMouseButton(1)) // Hold Right Mouse Button
        {
            float mouseX = Input.GetAxisRaw("Mouse X");
            transform.Rotate(Vector3.up, mouseX * rotationSpeed * 300f * InteractionDeltaTime, Space.World);
        }
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

        Vector3 forward = transform.forward;
        Vector3 viewCenter = cameraPosition;
        if (Mathf.Abs(forward.y) > 0.001f)
        {
            float distanceToFloor = (grid.Origin.y - cameraPosition.y) / forward.y;
            if (distanceToFloor > 0f)
                viewCenter = cameraPosition + forward * distanceToFloor;
        }

        float floorWidth = grid.Width * grid.cellSize;
        float floorDepth = grid.Height * grid.cellSize;
        Vector2 floorCenter = new Vector2(
            grid.Origin.x + floorWidth * 0.5f,
            grid.Origin.z + floorDepth * 0.5f);
        float floorCornerRadius = 0.5f * Mathf.Sqrt(floorWidth * floorWidth + floorDepth * floorDepth);
        float allowedRadius = floorCornerRadius + Mathf.Max(0f, movementBoundsMargin);

        Vector2 centerOffset = new Vector2(viewCenter.x, viewCenter.z) - floorCenter;
        if (centerOffset.sqrMagnitude > allowedRadius * allowedRadius)
        {
            Vector2 clampedCenter = floorCenter + centerOffset.normalized * allowedRadius;
            cameraPosition.x += clampedCenter.x - viewCenter.x;
            cameraPosition.z += clampedCenter.y - viewCenter.z;
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
