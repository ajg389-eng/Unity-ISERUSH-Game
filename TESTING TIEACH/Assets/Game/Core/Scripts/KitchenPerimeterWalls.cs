using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Left wall on -X expand, stretched back wall, and a few see-through window
/// openings (brick around real holes, glass visible from both sides).
/// </summary>
public class KitchenPerimeterWalls : MonoBehaviour
{
    public GridManager grid;
    public float thickness = 1f;
    public float height = 5f;

    [Header("Windows")]
    public float windowWidth = 1.7f;
    public float windowHeight = 1.85f;
    public float windowSill = 1.2f;
    [Range(0.08f, 0.45f)]
    public float windowGlassAlpha = 0.22f;

    Transform root;
    Transform west;
    Transform south;
    Transform northCap;
    Transform eastCap;
    Transform windowRoot;
    readonly List<Transform> windowPool = new List<Transform>();
    readonly List<Transform> brickPool = new List<Transform>();
    int windowsUsed;
    int bricksUsed;
    Mesh sharedMesh;
    Material[] sharedMaterials;
    Material glassMaterial;
    Transform backWall;
    Transform diningWall;
    Transform entranceDoor;
    bool copyingLook;
    bool rebuilding;

    void Awake()
    {
        if (grid == null)
            grid = GetComponent<GridManager>();
        if (grid == null)
            grid = GridManager.Instance;
        CacheLookFromExistingWalls();
    }

    public void FitToGrid()
    {
        if (rebuilding) return;
        if (grid == null)
            grid = GridManager.Instance != null ? GridManager.Instance : GetComponent<GridManager>();
        if (grid == null || grid.Width <= 0 || grid.Height <= 0) return;
        if (!grid.HasBaseline) return;

        rebuilding = true;
        try
        {
            CacheLookFromExistingWalls();
            CacheEntranceDoor();
            RestoreOriginalWalls();
            EnsureGlassMaterial();
            EnsurePieces();
            Hide(eastCap);
            Hide(south);
            Hide(northCap);
            Hide(west);
            HideNamed("KitchenRoofGlass");
            HideNamed("KitchenRoofTrim_N");
            HideNamed("KitchenRoofTrim_S");
            HideNamed("KitchenRoofTrim_W");
            HideNamed("KitchenRoofTrim_E");

            float minX = grid.Origin.x;
            float minZ = grid.Origin.z;
            float oldMinX = grid.BaselineOrigin.x;
            float t = Mathf.Max(0.2f, thickness);
            float backZ = backWall != null ? backWall.position.z : minZ + grid.Height * grid.cellSize + t * 0.5f;

            float addedWest = oldMinX - minX;
            bool showWest = addedWest > 0.05f;
            float outerX = minX - t;

            if (showWest)
                FitBackWall(outerX);
            else
                RestoreBackWallSize();

            SetOriginalBackSolidVisible(false);
            SetOriginalDiningSolidVisible(false);

            windowsUsed = 0;
            bricksUsed = 0;

            if (backWall != null)
            {
                float length = Mathf.Abs(backWall.localScale.x);
                BuildWallWithWindows(
                    new Vector3(backWall.position.x, grid.Origin.y, backWall.position.z),
                    0f, length, t, forceFlankingDoor: false);
            }

            if (showWest)
            {
                float innerX = minX - 0.05f;
                float westX = innerX - t * 0.5f;
                float z0 = minZ;
                float z1 = backZ + t * 0.5f;
                BuildWallWithWindows(
                    new Vector3(westX, grid.Origin.y, (z0 + z1) * 0.5f),
                    90f, z1 - z0, t, forceFlankingDoor: false);
            }

            if (diningWall != null)
            {
                float length = Mathf.Abs(diningWall.localScale.x);
                BuildWallWithWindows(
                    new Vector3(diningWall.position.x, grid.Origin.y, diningWall.position.z),
                    diningWall.eulerAngles.y, length, t, forceFlankingDoor: true);
            }

            for (int i = windowsUsed; i < windowPool.Count; i++)
                Hide(windowPool[i]);
            for (int i = bricksUsed; i < brickPool.Count; i++)
                Hide(brickPool[i]);
        }
        finally
        {
            rebuilding = false;
        }
    }

    static int WindowCountForLength(float length)
    {
        if (length < 10f) return 1;
        return 2;
    }

    void BuildWallWithWindows(Vector3 baseCenter, float yaw, float length, float thick, bool forceFlankingDoor)
    {
        if (length < 0.5f) return;

        float floorY = baseCenter.y;
        Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        Vector3 wallPos = new Vector3(baseCenter.x, 0f, baseCenter.z);

        float doorAlong = 0f;
        float doorHalf = 0f;
        float doorTop = 0f;
        bool hasDoor = TryDoorOnWall(wallPos, along, length, thick, out doorAlong, out doorHalf, out doorTop);

        float sillH = Mathf.Max(0.35f, windowSill);
        float windowTop = sillH + windowHeight;
        float bandTop = hasDoor ? Mathf.Max(windowTop, doorTop) : windowTop;
        float headerH = Mathf.Max(0.35f, height - bandTop);
        float openingH = Mathf.Max(0.4f, bandTop - sillH);

        var openings = new List<(float start, float end, bool door)>();
        if (hasDoor)
            openings.Add((doorAlong - doorHalf, doorAlong + doorHalf, true));

        if (forceFlankingDoor && hasDoor)
        {
            // Keep a full brick gap between the door edge and each window.
            // Negative along is the lobby-view LEFT side of the door.
            float clearance = 1.6f;
            float leftCenter = doorAlong - doorHalf - clearance - windowWidth * 0.5f;
            float rightCenter = doorAlong + doorHalf + clearance + windowWidth * 0.5f;
            TryAddWindowOpening(openings, leftCenter, length, true, doorAlong, doorHalf);
            TryAddWindowOpening(openings, rightCenter, length, true, doorAlong, doorHalf);
        }
        else if (!forceFlankingDoor)
        {
            int count = WindowCountForLength(length);
            float step = length / (count + 1);
            for (int i = 1; i <= count; i++)
            {
                float centerAlong = -length * 0.5f + step * i;
                TryAddWindowOpening(openings, centerAlong, length, hasDoor, doorAlong, doorHalf);
            }
        }

        openings.Sort((a, b) => a.start.CompareTo(b.start));

        PlaceBrick(
            wallPos + Vector3.up * (floorY + bandTop + headerH * 0.5f),
            yaw, length, headerH, thick);

        if (!hasDoor)
        {
            PlaceBrick(
                wallPos + Vector3.up * (floorY + sillH * 0.5f),
                yaw, length, sillH, thick);
        }

        float cursor = -length * 0.5f;
        for (int i = 0; i < openings.Count; i++)
        {
            var op = openings[i];
            float pierLen = op.start - cursor;
            if (pierLen > 0.08f)
            {
                float pierCenter = cursor + pierLen * 0.5f;
                float pierH = hasDoor ? bandTop : openingH;
                float pierY = hasDoor ? floorY + pierH * 0.5f : floorY + sillH + openingH * 0.5f;
                PlaceBrick(wallPos + along * pierCenter + Vector3.up * pierY, yaw, pierLen, pierH, thick);
            }

            if (op.door)
            {
                // Leave a hole for the existing Door mesh.
            }
            else
            {
                if (hasDoor)
                {
                    PlaceBrick(
                        wallPos + along * ((op.start + op.end) * 0.5f) + Vector3.up * (floorY + sillH * 0.5f),
                        yaw, op.end - op.start, sillH, thick);
                }

                Vector3 winPos = wallPos + along * ((op.start + op.end) * 0.5f)
                    + Vector3.up * (floorY + sillH + openingH * 0.5f);
                PlaceWindow(winPos, yaw, thick, openingH);
            }

            cursor = op.end;
        }

        float lastLen = length * 0.5f - cursor;
        if (lastLen > 0.08f)
        {
            float pierCenter = cursor + lastLen * 0.5f;
            float pierH = hasDoor ? bandTop : openingH;
            float pierY = hasDoor ? floorY + pierH * 0.5f : floorY + sillH + openingH * 0.5f;
            PlaceBrick(wallPos + along * pierCenter + Vector3.up * pierY, yaw, lastLen, pierH, thick);
        }
    }

    void TryAddWindowOpening(
        List<(float start, float end, bool door)> openings,
        float centerAlong, float length, bool hasDoor, float doorAlong, float doorHalf)
    {
        float half = windowWidth * 0.5f;
        float start = centerAlong - half;
        float end = centerAlong + half;
        if (start < -length * 0.5f + 0.2f || end > length * 0.5f - 0.2f)
            return;
        if (hasDoor && start < doorAlong + doorHalf + 1.2f && end > doorAlong - doorHalf - 1.2f)
            return;
        openings.Add((start, end, false));
    }

    bool TryDoorOnWall(Vector3 wallPos, Vector3 along, float length, float thick, out float doorAlong, out float doorHalf, out float doorTop)
    {
        doorAlong = 0f;
        doorHalf = 0f;
        doorTop = 0f;
        CacheEntranceDoor();
        if (entranceDoor == null) return false;

        along = along.normalized;
        Bounds b = entranceDoor.GetComponent<Collider>() != null
            ? entranceDoor.GetComponent<Collider>().bounds
            : new Bounds(entranceDoor.position, entranceDoor.lossyScale);

        Vector3 toDoor = b.center - wallPos;
        toDoor.y = 0f;
        Vector3 inward = Vector3.Cross(Vector3.up, along);
        if (inward.sqrMagnitude > 0.0001f && Mathf.Abs(Vector3.Dot(toDoor, inward.normalized)) > thick + 1.25f)
            return false;

        float minAlong = float.MaxValue;
        float maxAlong = float.MinValue;
        Vector3 ext = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + new Vector3(
                (i & 1) == 0 ? -ext.x : ext.x,
                (i & 2) == 0 ? -ext.y : ext.y,
                (i & 4) == 0 ? -ext.z : ext.z);
            float a = Vector3.Dot(corner - wallPos, along);
            if (a < minAlong) minAlong = a;
            if (a > maxAlong) maxAlong = a;
        }

        doorAlong = (minAlong + maxAlong) * 0.5f;
        doorHalf = (maxAlong - minAlong) * 0.5f + 0.15f;
        if (Mathf.Abs(doorAlong) > length * 0.5f + 0.75f)
            return false;

        doorTop = Mathf.Max(2.4f, b.max.y - (grid != null ? grid.Origin.y : 0f));
        return true;
    }

    void PlaceWindow(Vector3 pos, float yaw, float thick, float openingH)
    {
        Transform win = EnsureWindow(windowsUsed);
        windowsUsed++;
        win.gameObject.SetActive(true);
        win.position = pos;
        win.rotation = Quaternion.Euler(0f, yaw, 0f);

        Transform frameT = win.Find("Frame");
        if (frameT != null)
            frameT.gameObject.SetActive(false);
        float glassDeep = 0.04f;
        Transform glassA = win.Find("GlassA");
        Transform glassB = win.Find("GlassB");
        if (glassA != null)
        {
            glassA.localPosition = new Vector3(0f, 0f, thick * 0.5f);
            glassA.localScale = new Vector3(windowWidth, openingH, glassDeep);
        }
        if (glassB != null)
        {
            glassB.localPosition = new Vector3(0f, 0f, -thick * 0.5f);
            glassB.localScale = new Vector3(windowWidth, openingH, glassDeep);
        }
    }

    void PlaceBrick(Vector3 pos, float yaw, float along, float up, float thick)
    {
        Transform b = EnsureBrick(bricksUsed);
        bricksUsed++;
        b.gameObject.SetActive(true);
        b.position = pos;
        b.rotation = Quaternion.Euler(0f, yaw, 0f);
        b.localScale = new Vector3(Mathf.Max(0.08f, along), Mathf.Max(0.08f, up), thick);
    }

    Transform EnsureWindow(int index)
    {
        while (windowPool.Count <= index)
        {
            var go = new GameObject("KitchenWindow_" + windowPool.Count);
            go.transform.SetParent(windowRoot, false);

            CreateWindowPart(go.transform, "GlassA", brick: false);
            CreateWindowPart(go.transform, "GlassB", brick: false);
            windowPool.Add(go.transform);
        }

        return windowPool[index];
    }

    void CreateWindowPart(Transform parent, string name, bool brick)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        var col = part.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var rend = part.GetComponent<MeshRenderer>();
        if (rend == null) return;
        if (brick)
        {
            if (sharedMaterials != null && sharedMaterials.Length > 0)
                rend.sharedMaterials = sharedMaterials;
        }
        else
        {
            if (glassMaterial != null)
                rend.sharedMaterial = glassMaterial;
            rend.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    Transform EnsureBrick(int index)
    {
        while (brickPool.Count <= index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "KitchenWallBrick_" + brickPool.Count;
            go.transform.SetParent(windowRoot, false);
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && sharedMesh != null)
                filter.sharedMesh = sharedMesh;
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null && sharedMaterials != null && sharedMaterials.Length > 0)
                rend.sharedMaterials = sharedMaterials;
            brickPool.Add(go.transform);
        }

        return brickPool[index];
    }

    Vector3 origBackWorldPos;
    Vector3 origBackScale;
    bool origBackSaved;

    void FitBackWall(float outerX)
    {
        if (backWall == null) return;
        if (!origBackSaved)
        {
            origBackWorldPos = backWall.position;
            origBackScale = backWall.localScale;
            origBackSaved = true;
        }

        float origWest = origBackWorldPos.x - Mathf.Abs(origBackScale.x) * 0.5f;
        float origEast = origBackWorldPos.x + Mathf.Abs(origBackScale.x) * 0.5f;
        float westX = Mathf.Min(origWest, outerX);
        float length = Mathf.Max(0.25f, origEast - westX);
        backWall.position = new Vector3(westX + length * 0.5f, origBackWorldPos.y, origBackWorldPos.z);
        backWall.localScale = new Vector3(length, origBackScale.y, origBackScale.z);
    }

    void RestoreBackWallSize()
    {
        if (backWall == null || !origBackSaved) return;
        backWall.position = origBackWorldPos;
        backWall.localScale = origBackScale;
    }

    void SetOriginalBackSolidVisible(bool vis)
    {
        if (backWall == null) return;
        var rend = backWall.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = vis;
        var col = backWall.GetComponent<Collider>();
        if (col != null) col.enabled = vis;
    }

    void SetOriginalDiningSolidVisible(bool vis)
    {
        if (diningWall == null) return;
        var rend = diningWall.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = vis;
        var col = diningWall.GetComponent<Collider>();
        if (col != null) col.enabled = vis;
    }

    void CacheLookFromExistingWalls()
    {
        if (copyingLook) return;
        var walls = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < walls.Length; i++)
        {
            var go = walls[i] != null ? walls[i].gameObject : null;
            if (go == null) continue;
            if (go.name.StartsWith("KitchenWall") || go.name.StartsWith("KitchenWindow")) continue;
            if (go.name != "Wall" && !go.name.StartsWith("Wall (")) continue;

            if (go.name == "Wall")
                backWall = go.transform;
            else if (go.name.StartsWith("Wall ("))
                diningWall = go.transform;

            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                sharedMesh = filter.sharedMesh;
            if (walls[i].sharedMaterials != null && walls[i].sharedMaterials.Length > 0)
                sharedMaterials = walls[i].sharedMaterials;

            height = Mathf.Max(height, go.transform.localScale.y);
            float thick = Mathf.Min(go.transform.localScale.x, go.transform.localScale.z);
            if (thick > 0.15f && thick < 2.5f)
                thickness = thick;
        }
        copyingLook = backWall != null || diningWall != null;
        CacheEntranceDoor();
    }

    void CacheEntranceDoor()
    {
        if (entranceDoor != null) return;
        var named = GameObject.Find("Door");
        if (named != null)
        {
            entranceDoor = named.transform;
            return;
        }

        var doors = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = float.MaxValue;
        Vector3 hint = diningWall != null ? diningWall.position : (backWall != null ? backWall.position : Vector3.zero);
        for (int i = 0; i < doors.Length; i++)
        {
            var t = doors[i];
            if (t == null || t.name != "Door") continue;
            float d = (t.position - hint).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        entranceDoor = best;
    }

    void RestoreOriginalWalls()
    {
        if (backWall != null && !backWall.gameObject.activeSelf)
            backWall.gameObject.SetActive(true);
        if (diningWall != null && !diningWall.gameObject.activeSelf)
            diningWall.gameObject.SetActive(true);
    }

    Transform WallParent()
    {
        if (grid != null && grid.floor != null && grid.floor.parent != null)
            return grid.floor.parent;
        return null;
    }

    void EnsurePieces()
    {
        Transform parent = WallParent();
        if (root == null)
        {
            var existing = GameObject.Find("KitchenExpandWalls");
            if (existing != null)
                root = existing.transform;
            else
                root = new GameObject("KitchenExpandWalls").transform;
            if (parent != null && root.parent != parent)
                root.SetParent(parent, true);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }

        if (windowRoot == null)
        {
            var existingWin = root.Find("KitchenWindows");
            windowRoot = existingWin != null ? existingWin : new GameObject("KitchenWindows").transform;
            windowRoot.SetParent(root, false);
            windowRoot.localPosition = Vector3.zero;
            windowRoot.localRotation = Quaternion.identity;
            windowRoot.localScale = Vector3.one;
        }

        west = EnsurePiece("Expand_West");
        south = EnsurePiece("Expand_South");
        northCap = EnsurePiece("Expand_NorthCap");
        eastCap = EnsurePiece("Expand_EastCap");
    }

    Transform EnsurePiece(string name)
    {
        Transform t = root.Find(name);
        if (t != null)
        {
            var extra = t.GetComponent<GridObstacle>();
            if (extra != null)
                Object.Destroy(extra);
            return t;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root, false);
        go.layer = 0;

        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && sharedMesh != null)
            filter.sharedMesh = sharedMesh;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && sharedMaterials != null && sharedMaterials.Length > 0)
            renderer.sharedMaterials = sharedMaterials;

        return go.transform;
    }

    void EnsureGlassMaterial()
    {
        Color tint = new Color(0.62f, 0.84f, 0.95f, windowGlassAlpha);
        if (glassMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            glassMaterial = new Material(shader) { name = "KitchenWindowGlass" };
        }

        if (glassMaterial.HasProperty("_Surface"))
            glassMaterial.SetFloat("_Surface", 1f);
        if (glassMaterial.HasProperty("_Blend"))
            glassMaterial.SetFloat("_Blend", 0f);
        if (glassMaterial.HasProperty("_Cull"))
            glassMaterial.SetFloat("_Cull", 0f);
        if (glassMaterial.HasProperty("_CullMode"))
            glassMaterial.SetFloat("_CullMode", 0f);
        if (glassMaterial.HasProperty("_Smoothness"))
            glassMaterial.SetFloat("_Smoothness", 0.95f);
        glassMaterial.SetOverrideTag("RenderType", "Transparent");
        glassMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        glassMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        glassMaterial.SetInt("_ZWrite", 0);
        glassMaterial.DisableKeyword("_ALPHATEST_ON");
        glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glassMaterial.EnableKeyword("_DOUBLE_SIDED");
        glassMaterial.renderQueue = (int)RenderQueue.Transparent;
        if (glassMaterial.HasProperty("_BaseColor"))
            glassMaterial.SetColor("_BaseColor", tint);
        if (glassMaterial.HasProperty("_Color"))
            glassMaterial.SetColor("_Color", tint);
        glassMaterial.doubleSidedGI = true;
    }

    void HideNamed(string name)
    {
        if (root == null) return;
        Hide(root.Find(name));
    }

    static void Hide(Transform wall)
    {
        if (wall != null)
            wall.gameObject.SetActive(false);
    }
}
