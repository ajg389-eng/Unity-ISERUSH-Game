using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies a line-of-sight wall fade around the point at the center of the view.
/// Individual compass sides can be locked at full height from Options → Visual.
/// </summary>
public class CameraWallCutaway : MonoBehaviour
{
    public enum WallSide
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public static CameraWallCutaway Instance { get; private set; }

    static readonly List<CameraOcclusionWall> Walls = new List<CameraOcclusionWall>();
    static readonly bool[] LockedSides = new bool[4];
    static bool locksLoaded;

    const string PrefLockNorth = "PauseMenu.LockWall.North";
    const string PrefLockEast = "PauseMenu.LockWall.East";
    const string PrefLockSouth = "PauseMenu.LockWall.South";
    const string PrefLockWest = "PauseMenu.LockWall.West";
    const string PrefAutomaticCutawayDefault = "CameraWallCutaway.AutomaticDefault.v1";

    [Tooltip("Camera used for cutaway. Defaults to main / PlayerCameraController.")]
    public Camera targetCamera;

    [Tooltip("Store focus point. Defaults to GridManager floor center.")]
    public Transform focusOverride;

    public GridManager grid;

    [Tooltip("How strongly a wall must face the camera side before it cuts away (0-1).")]
    [Range(0.05f, 0.9f)]
    public float duckDotThreshold = 0.2f;

    [Tooltip("How fast walls transition between full height and cutaway height.")]
    public float duckSpeed = 7f;

    [Tooltip("Auto-tag scene objects named like Wall on start.")]
    public bool autoTagNamedWalls = true;

    [Tooltip("Also duck window glass pieces with the bricks.")]
    public bool includeWindowGlass = true;

    [Header("Line of sight fade")]
    [Tooltip("Wall sections within this radius of the camera focus become fully transparent.")]
    [Min(0.1f)] public float focusClearRadius = 4.5f;
    [Tooltip("Wall sections within this radius of the focus receive a softer fade.")]
    [Min(0.5f)] public float surroundingFadeRadius = 9f;
    [Tooltip("Fade amount applied at the outside edge of the surrounding radius.")]
    [Range(0f, 1f)] public float surroundingFadeAmount = 0.35f;
    [Tooltip("Horizontal half-width of the camera line of sight. Sections inside this corridor become fully transparent when they block the view.")]
    [Min(0f)] public float lineOfSightHalfWidth = 3f;

    Transform customerFloor;

    void Awake()
    {
        Instance = this;
        LoadLocks();
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
        if (wall.name.StartsWith("KitchenWallGroup_", System.StringComparison.OrdinalIgnoreCase))
            wall.sectionContainer = true;
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
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
            if (targetCamera == null || !targetCamera.isActiveAndEnabled) return;
        }

        Vector3 focus = GetFocus();
        Vector3 viewTarget = GetViewTarget(focus);
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector3 cameraToTarget = viewTarget - cameraPosition;
        float targetDistance = cameraToTarget.magnitude;
        if (targetDistance < 0.01f)
            cameraToTarget = targetCamera.transform.forward;
        else
            cameraToTarget /= targetDistance;
        Ray viewRay = new Ray(cameraPosition, cameraToTarget);

        for (int i = Walls.Count - 1; i >= 0; i--)
        {
            var wall = Walls[i];
            if (wall == null)
            {
                Walls.RemoveAt(i);
                continue;
            }
            if (wall.sectionContainer)
            {
                wall.SetDuckTarget(0f);
                continue;
            }

            float fadeAmount = GetFadeAmount(wall, focus, viewTarget, viewRay,
                targetDistance, cameraToTarget);
            if (IsOutwardLocked(wall.GetOutward(focus)))
                fadeAmount = 0f;
            wall.SetDuckTarget(fadeAmount);
        }
    }

    Vector3 GetViewTarget(Vector3 fallback)
    {
        if (targetCamera == null) return fallback;

        Ray centerRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane floorPlane = new Plane(Vector3.up, new Vector3(0f, fallback.y, 0f));
        if (floorPlane.Raycast(centerRay, out float enter) && enter > 0f)
            return centerRay.GetPoint(enter);
        return fallback;
    }

    float GetFadeAmount(CameraOcclusionWall wall, Vector3 focus, Vector3 viewTarget,
        Ray viewRay, float targetDistance, Vector3 cameraToTarget)
    {
        if (wall == null || targetDistance <= 0.01f)
            return 0f;

        Bounds bounds = wall.WorldBounds;
        if (bounds.size.sqrMagnitude < 0.0001f)
            return 0f;

        // A wall only fades when the view is aimed toward the store and that
        // wall is on the camera side of the store focus. Looking away keeps
        // every wall opaque, including walls behind the building.
        float focusDepth = Vector3.Dot(focus - viewRay.origin, viewRay.direction);
        float wallDepth = Vector3.Dot(bounds.center - viewRay.origin, viewRay.direction);
        if (focusDepth <= 0f || wallDepth <= 0f || wallDepth > focusDepth + 1f)
            return 0f;

        // Only the exterior face seen by the camera can fade. The opposite
        // wall has an outward normal pointing away from the camera and stays
        // opaque even when it is near the focus or ray.
        Vector3 toCamera = viewRay.origin - bounds.center;
        toCamera.y = 0f;
        Vector3 outward = wall.GetOutward(focus);
        outward.y = 0f;
        if (toCamera.sqrMagnitude < 0.0001f || outward.sqrMagnitude < 0.0001f)
            return 0f;
        float facingCamera = Vector3.Dot(outward.normalized, toCamera.normalized);
        if (facingCamera < duckDotThreshold)
            return 0f;

        // Treat each section as a vertical prism for line-of-sight purposes.
        // The header, sill, and pier pieces that share one strip therefore
        // receive the same fade even when the center ray passes through a
        // window opening rather than through the brick band itself.
        Bounds verticalBounds = bounds;
        verticalBounds.center = new Vector3(bounds.center.x, viewTarget.y, bounds.center.z);
        float sightExpand = Mathf.Max(0f, lineOfSightHalfWidth) * 2f;
        verticalBounds.size = new Vector3(
            bounds.size.x + sightExpand,
            Mathf.Max(bounds.size.y, targetDistance + 10f),
            bounds.size.z + sightExpand);
        if (verticalBounds.IntersectRay(viewRay, out float hitDistance)
            && hitDistance > 0f && hitDistance < targetDistance - 0.05f)
            return 1f;

        float distanceToFocus = wall.DistanceToWall(viewTarget);
        if (distanceToFocus > surroundingFadeRadius)
            return 0f;

        // Do not fade sections behind the viewed area. Sections beside the
        // focus remain as a softer visual guide around the clear area.
        Vector3 targetToWall = bounds.center - viewTarget;
        targetToWall.y = 0f;
        Vector3 horizontalCameraToTarget = cameraToTarget;
        horizontalCameraToTarget.y = 0f;
        if (horizontalCameraToTarget.sqrMagnitude > 0.0001f)
            horizontalCameraToTarget.Normalize();
        float depthPastTarget = Vector3.Dot(targetToWall, horizontalCameraToTarget);
        if (depthPastTarget > 2f)
            return 0f;

        if (distanceToFocus <= focusClearRadius)
            return 1f;

        float proximity = 1f - Mathf.InverseLerp(
            focusClearRadius, surroundingFadeRadius, distanceToFocus);
        return Mathf.Lerp(surroundingFadeAmount, 1f, proximity);
    }

    public static bool IsWallLocked(WallSide side)
    {
        if (side == WallSide.East)
            return true;
        LoadLocks();
        int index = (int)side;
        if (index < 0 || index >= LockedSides.Length) return false;
        return LockedSides[index];
    }

    public static void SetWallLocked(WallSide side, bool locked)
    {
        if (side == WallSide.East)
            locked = true;
        LoadLocks();
        int index = (int)side;
        if (index < 0 || index >= LockedSides.Length) return;
        LockedSides[index] = locked;
        PlayerPrefs.SetInt(PrefKey(side), locked ? 1 : 0);
        PlayerPrefs.Save();
        if (locked)
            SnapLockedWallsUp(side);
    }

    static string PrefKey(WallSide side)
    {
        switch (side)
        {
            case WallSide.North: return PrefLockNorth;
            case WallSide.East: return PrefLockEast;
            case WallSide.South: return PrefLockSouth;
            default: return PrefLockWest;
        }
    }

    static void LoadLocks()
    {
        if (locksLoaded) return;
        // One-time reset of legacy locks: automatic cutaways should behave the
        // same before and after onboarding. Later explicit choices still persist.
        if (PlayerPrefs.GetInt(PrefAutomaticCutawayDefault, 0) != 1)
        {
            PlayerPrefs.SetInt(PrefLockNorth, 0);
            PlayerPrefs.SetInt(PrefLockEast, 0);
            PlayerPrefs.SetInt(PrefLockSouth, 0);
            PlayerPrefs.SetInt(PrefLockWest, 0);
            PlayerPrefs.SetInt(PrefAutomaticCutawayDefault, 1);
            PlayerPrefs.Save();
        }
        LockedSides[(int)WallSide.North] = PlayerPrefs.GetInt(PrefLockNorth, 0) == 1;
        LockedSides[(int)WallSide.East] = PlayerPrefs.GetInt(PrefLockEast, 0) == 1;
        LockedSides[(int)WallSide.South] = PlayerPrefs.GetInt(PrefLockSouth, 0) == 1;
        LockedSides[(int)WallSide.West] = PlayerPrefs.GetInt(PrefLockWest, 0) == 1;
        locksLoaded = true;
    }

    static bool IsOutwardLocked(Vector3 outward)
    {
        LoadLocks();
        return LockedSides[(int)SideFromOutward(outward)];
    }

    public static WallSide SideFromOutward(Vector3 outward)
    {
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
            return WallSide.South;
        if (Mathf.Abs(outward.x) >= Mathf.Abs(outward.z))
            return outward.x >= 0f ? WallSide.East : WallSide.West;
        return outward.z >= 0f ? WallSide.North : WallSide.South;
    }

    static void SnapLockedWallsUp(WallSide side)
    {
        Vector3 focus = Instance != null ? Instance.GetFocus() : Vector3.zero;
        for (int i = 0; i < Walls.Count; i++)
        {
            var wall = Walls[i];
            if (wall == null) continue;
            if (SideFromOutward(wall.GetOutward(focus)) != side) continue;
            wall.SetDuckTarget(0f);
            wall.SnapUp();
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
            // Wall groups are containers. Their brick and optional window pieces
            // are registered independently so each section can fade on its own.
            if (n.StartsWith("KitchenWallGroup_", System.StringComparison.OrdinalIgnoreCase))
                continue;

            bool isProceduralSection = n.StartsWith("KitchenWallBrick_", System.StringComparison.OrdinalIgnoreCase)
                || (includeWindowGlass && n.StartsWith("KitchenWindow_", System.StringComparison.OrdinalIgnoreCase));
            if (isProceduralSection)
            {
                if (t.GetComponentInChildren<Renderer>(true) == null) continue;
                var section = t.GetComponent<CameraOcclusionWall>();
                if (section == null) section = t.gameObject.AddComponent<CameraOcclusionWall>();
                section.sectionContainer = false;
                section.SetOutwardFromKitchen(focus);
                section.RecaptureRestPose();
                continue;
            }

            bool isWall = n.StartsWith("Wall", System.StringComparison.OrdinalIgnoreCase);
            if (!isWall) continue;
            if (t.GetComponent<CustomerWallDoor>() != null) continue;
            if (t.GetComponent<CameraOcclusionWall>() != null) continue;

            if (t.GetComponentInChildren<Renderer>(true) == null) continue;

            var occ = t.gameObject.AddComponent<CameraOcclusionWall>();
            occ.SetOutwardFromKitchen(focus);
            occ.CaptureRestPose();
        }
    }
}


