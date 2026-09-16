using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lowers walls on the camera-facing side of the kitchen so you can see into the store.
/// Add to any scene object (or it auto-creates beside the camera).
/// </summary>
public class CameraWallCutaway : MonoBehaviour
{
    public static CameraWallCutaway Instance { get; private set; }

    static readonly List<CameraOcclusionWall> Walls = new List<CameraOcclusionWall>();

    [Tooltip("Camera used for cutaway. Defaults to main / PlayerCameraController.")]
    public Camera targetCamera;

    [Tooltip("Store focus point. Defaults to GridManager floor center.")]
    public Transform focusOverride;

    public GridManager grid;

    [Tooltip("How strongly a wall must face the camera side before it ducks (0–1).")]
    [Range(0.05f, 0.9f)]
    public float duckDotThreshold = 0.2f;

    [Tooltip("How fast walls lower / rise.")]
    public float duckSpeed = 7f;

    [Tooltip("Auto-tag scene objects named like Wall on start.")]
    public bool autoTagNamedWalls = true;

    [Tooltip("Also duck window glass pieces with the bricks.")]
    public bool includeWindowGlass = true;

    Transform customerFloor;

    void Awake()
    {
        Instance = this;
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (targetCamera == null)
        {
            var pcc = FindObjectOfType<PlayerCameraController>();
            if (pcc != null) targetCamera = pcc.GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
        }
    }

    void Start()
    {
        if (autoTagNamedWalls)
            AutoTagSceneWalls();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void Register(CameraOcclusionWall wall)
    {
        if (wall == null) return;
        if (!Walls.Contains(wall))
            Walls.Add(wall);
        EnsureExists();
    }

    public static void Unregister(CameraOcclusionWall wall)
    {
        Walls.Remove(wall);
    }

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("CameraWallCutaway");
        go.AddComponent<CameraWallCutaway>();
    }

    void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        Vector3 focus = GetFocus();
        Vector3 cam = targetCamera.transform.position;
        Vector3 camFromFocus = cam - focus;
        camFromFocus.y = 0f;
        if (camFromFocus.sqrMagnitude < 0.01f) return;
        Vector3 camDir = camFromFocus.normalized;

        for (int i = Walls.Count - 1; i >= 0; i--)
        {
            var wall = Walls[i];
            if (wall == null)
            {
                Walls.RemoveAt(i);
                continue;
            }

            Vector3 outward = wall.GetOutward(focus);
            float face = Vector3.Dot(camDir, outward);

            // Also duck walls that sit on the camera side of the store (helps L-shaped dining).
            Vector3 toWall = wall.transform.position - focus;
            toWall.y = 0f;
            float onCamSide = toWall.sqrMagnitude > 0.01f
                ? Vector3.Dot(toWall.normalized, camDir)
                : 0f;

            float side = Mathf.Max(face, onCamSide * 0.9f);
            // Soft blend near the threshold so orbiting feels smooth
            float t = Mathf.InverseLerp(duckDotThreshold, duckDotThreshold + 0.35f, side);
            wall.SetDuckTarget(t);
        }
    }

    Vector3 GetFocus()
    {
        if (focusOverride != null)
            return focusOverride.position;

        if (grid == null)
            grid = GridManager.Instance;

        Vector3 focus = Vector3.zero;
        bool any = false;

        if (grid != null && grid.Width > 0 && grid.Height > 0)
        {
            focus = grid.Origin + new Vector3(
                grid.Width * grid.cellSize * 0.5f,
                0f,
                grid.Height * grid.cellSize * 0.5f);
            any = true;
        }

        // Include dining / customer floor so cutaway aims at the whole store, not just kitchen.
        if (customerFloor == null)
        {
            var go = GameObject.Find("CustomerFloor");
            if (go != null) customerFloor = go.transform;
        }
        if (customerFloor != null)
        {
            var rend = customerFloor.GetComponentInChildren<Renderer>();
            Vector3 cCenter = rend != null ? rend.bounds.center : customerFloor.position;
            cCenter.y = 0f;
            if (any)
                focus = (focus + cCenter) * 0.5f;
            else
                focus = cCenter;
            any = true;
        }

        return any ? focus : Vector3.zero;
    }

    void AutoTagSceneWalls()
    {
        Vector3 focus = GetFocus();
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null) continue;
            string n = t.name;
            // Procedural bricks/windows duck with their KitchenWallGroup parent — don't tag per-piece.
            if (n.StartsWith("KitchenWallBrick_", System.StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("KitchenWindow_", System.StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("KitchenWallGroup_", System.StringComparison.OrdinalIgnoreCase))
                continue;

            bool isWall = n.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase);

            if (!isWall) continue;
            if (t.GetComponent<CameraOcclusionWall>() != null) continue;

            // Skip tiny helper empties without visuals
            if (t.GetComponentInChildren<Renderer>(true) == null) continue;

            var occ = t.gameObject.AddComponent<CameraOcclusionWall>();
            occ.SetOutwardFromKitchen(focus);
            occ.CaptureRestPose();
        }
    }
}
