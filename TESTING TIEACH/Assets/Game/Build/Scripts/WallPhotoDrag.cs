using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(-200)]
public class WallPhotoDrag : MonoBehaviour
{
    public Transform artwork;
    public Vector2 wallXLimits = new Vector2(-9f, 12f);
    public Vector2 wallYLimits = new Vector2(0f, 5f);
    static readonly List<WallPhotoDrag> photos = new List<WallPhotoDrag>();
    static WallPhotoDrag dragging;
    public static bool IsDragging => dragging != null;
    readonly Vector3[] outlinePoints = new Vector3[4];
    static int inputFrame = -1;
    public static bool InputClaimed => dragging != null || inputFrame == Time.frameCount;
    GameModeManager mode;
    Vector3 original, originalPlacement, pointerOffset;
    Plane wallPlane;
    bool valid;
    LineRenderer outline;
    Material outlineMaterial;

    public Vector3 PlacementPosition
    {
        get
        {
            WallMountedCutawayFollower follower = GetComponent<WallMountedCutawayFollower>();
            return follower != null ? follower.RestPosition : transform.position;
        }
    }

    void OnEnable()
    {
        if (GetComponent<WallMountedCutawayFollower>() == null)
            gameObject.AddComponent<WallMountedCutawayFollower>();
        photos.Add(this);
    }
    void OnDisable()
    {
        if (dragging == this) EndDrag(false);
        photos.Remove(this);
    }

    public void SetPosition(Vector3 position)
    {
        WallMountedCutawayFollower follower = GetComponent<WallMountedCutawayFollower>();
        if (follower != null) position.y += follower.CurrentDrop;
        SetPlacementPosition(position);
    }

    public void SetPlacementPosition(Vector3 position)
    {
        // Keep both pieces on their original wall, including their surface offset.
        position.z = transform.position.z;
        Vector3 half = transform.localScale * 0.5f;
        position.x = Mathf.Clamp(position.x, wallXLimits.x + half.x, wallXLimits.y - half.x);
        position.y = Mathf.Clamp(position.y, wallYLimits.x + half.y, wallYLimits.y - half.y);
        Vector3 oldPosition = transform.position;
        WallMountedCutawayFollower follower = GetComponent<WallMountedCutawayFollower>();
        Vector3 delta = position - PlacementPosition;
        if (delta.sqrMagnitude < 0.0000001f) return;
        if (follower != null) follower.SetRestPosition(position);
        else transform.position = position;
        delta = transform.position - oldPosition;
        if (artwork != null) artwork.position += delta;
    }

    void Update()
    {
        if (mode == null) mode = FindFirstObjectByType<GameModeManager>();
        if (mode == null || mode.CurrentMode != GameModeManager.Mode.Build)
        {
            if (dragging == this) EndDrag(false);
            return;
        }
        Camera camera = Camera.main;
        if (camera == null) return;
        if (dragging == null && Input.GetMouseButtonDown(0)
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            if (!Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 500f)) return;
            if (hit.transform != transform && hit.transform != artwork) return;
            original = transform.position;
            originalPlacement = PlacementPosition;
            wallPlane = new Plane(Vector3.forward, original);
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!wallPlane.Raycast(ray, out float distance)) return;
            pointerOffset = original - ray.GetPoint(distance);
            dragging = this;
            inputFrame = Time.frameCount;
            CreateOutline();
        }
        if (dragging != this) return;
        inputFrame = Time.frameCount;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { EndDrag(false); return; }
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Ray pointer = camera.ScreenPointToRay(Input.mousePosition);
        float travel;
        valid = wallPlane.Raycast(pointer, out travel) && !overUI;
        if (valid)
        {
            SetPosition(pointer.GetPoint(travel) + pointerOffset);
            Vector3 size = transform.localScale;
            foreach (WallPhotoDrag other in photos)
            {
                if (other == this || Mathf.Abs(other.transform.position.z - transform.position.z) > 0.1f) continue;
                Vector3 separation = other.transform.position - transform.position;
                Vector3 otherSize = other.transform.localScale;
                if (Mathf.Abs(separation.x) < (size.x + otherSize.x) * 0.5f + 0.08f
                    && Mathf.Abs(separation.y) < (size.y + otherSize.y) * 0.5f + 0.08f) valid = false;
            }
        }
        DrawOutline();
        if (Input.GetMouseButtonUp(0)) EndDrag(valid);
    }

    void CreateOutline()
    {
        var root = new GameObject("Wall photo placement preview");
        outline = root.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.positionCount = 4;
        outline.startWidth = outline.endWidth = 0.025f;
        outlineMaterial = new Material(Shader.Find("Sprites/Default"));
        outline.sharedMaterial = outlineMaterial;
    }

    void DrawOutline()
    {
        Vector3 center = transform.position;
        center.z = artwork != null ? artwork.position.z - 0.025f : center.z - 0.18f;
        Vector3 half = transform.localScale * 0.5f + new Vector3(0.035f, 0.035f, 0f);
        outlinePoints[0] = center + new Vector3(-half.x, -half.y, 0);
        outlinePoints[1] = center + new Vector3(-half.x, half.y, 0);
        outlinePoints[2] = center + new Vector3(half.x, half.y, 0);
        outlinePoints[3] = center + new Vector3(half.x, -half.y, 0);
        outline.SetPositions(outlinePoints);
        outline.startColor = outline.endColor = valid ? Color.green : Color.red;
    }

    void EndDrag(bool commit)
    {
        if (!commit) SetPlacementPosition(originalPlacement);
        if (outline != null) Destroy(outline.gameObject);
        if (outlineMaterial != null) Destroy(outlineMaterial);
        dragging = null;
        inputFrame = Time.frameCount;
    }
}
