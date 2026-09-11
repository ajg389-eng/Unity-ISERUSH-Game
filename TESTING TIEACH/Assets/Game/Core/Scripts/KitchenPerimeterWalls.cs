using UnityEngine;

/// <summary>
/// Adds a left wall when the kitchen grows on -X, and extends the original
/// back wall to match. Does not add a south wall or lobby-side walls.
/// </summary>
public class KitchenPerimeterWalls : MonoBehaviour
{
    public GridManager grid;
    public float thickness = 1f;
    public float height = 5f;

    Transform root;
    Transform west;
    Transform south;
    Transform northCap;
    Transform eastCap;
    Mesh sharedMesh;
    Material[] sharedMaterials;
    Transform backWall;
    Transform diningWall;
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
            RestoreOriginalWalls();
            EnsurePieces();
            Hide(eastCap);
            Hide(south);
            Hide(northCap);
            HideNamed("KitchenRoofGlass");
            HideNamed("KitchenRoofTrim_N");
            HideNamed("KitchenRoofTrim_S");
            HideNamed("KitchenRoofTrim_W");
            HideNamed("KitchenRoofTrim_E");

            float minX = grid.Origin.x;
            float minZ = grid.Origin.z;
            float oldMinX = grid.BaselineOrigin.x;
            float y = backWall != null ? backWall.position.y : grid.Origin.y + height * 0.5f;
            float t = Mathf.Max(0.2f, thickness);
            float backZ = backWall != null ? backWall.position.z : minZ + grid.Height * grid.cellSize + t * 0.5f;

            float addedWest = oldMinX - minX;
            bool showWest = addedWest > 0.05f;
            float outerX = minX - t;

            if (showWest)
                FitBackWall(outerX);
            else
                RestoreBackWallSize();

            if (showWest)
            {
                float z0 = minZ;
                float z1 = backZ + t * 0.5f;
                // Keep the inner face just outside the first floor cell so stations can sit flush.
                float innerX = minX - 0.05f;
                Place(west, new Vector3(innerX - t * 0.5f, y, (z0 + z1) * 0.5f), 90f, z1 - z0, t);
            }
            else
            {
                Hide(west);
            }
        }
        finally
        {
            rebuilding = false;
        }
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

    void CacheLookFromExistingWalls()
    {
        if (copyingLook) return;
        var walls = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < walls.Length; i++)
        {
            var go = walls[i] != null ? walls[i].gameObject : null;
            if (go == null) continue;
            if (go.GetComponentInParent<KitchenPerimeterWalls>() != null) continue;
            if (go.name != "Wall" && !go.name.StartsWith("Wall (")) continue;

            if (go.name == "Wall")
                backWall = go.transform;
            else if (diningWall == null)
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

    void Place(Transform wall, Vector3 worldPos, float yaw, float length, float thick)
    {
        if (wall == null) return;
        wall.gameObject.SetActive(true);
        wall.position = worldPos;
        wall.rotation = Quaternion.Euler(0f, yaw, 0f);
        wall.localScale = new Vector3(Mathf.Max(0.25f, length), height, thick);
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
