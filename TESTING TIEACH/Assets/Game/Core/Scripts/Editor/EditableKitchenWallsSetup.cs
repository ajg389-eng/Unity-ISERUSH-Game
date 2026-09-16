#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates scene-editable N/E/S/W kitchen walls (no runtime generation).
/// </summary>
public static class EditableKitchenWallsSetup
{
    const string RootName = "KitchenWalls";

    [MenuItem("Game/Setup Editable Kitchen Walls")]
    public static void Setup()
    {
        var grid = Object.FindObjectOfType<GridManager>();
        if (grid == null)
        {
            EditorUtility.DisplayDialog("Kitchen Walls", "No GridManager found in the open scene.", "OK");
            return;
        }

        if (grid.Width <= 0 || grid.Height <= 0)
            grid.RebuildFromFloorMenu();

        // Prefer scene Wall look
        Mesh sharedMesh = null;
        Material[] sharedMats = null;
        float height = 5f;
        float thickness = 1f;
        CollectWallLook(ref sharedMesh, ref sharedMats, ref height, ref thickness);

        Transform parent = grid.floor != null && grid.floor.parent != null
            ? grid.floor.parent
            : null;

        var existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog(
                    "Kitchen Walls",
                    "KitchenWalls already exists. Replace it with a fresh set sized to the current floor?",
                    "Replace", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        // Hide old procedural runtime walls if present
        var runtime = GameObject.Find("KitchenExpandWalls");
        if (runtime != null)
        {
            Undo.RecordObject(runtime, "Hide runtime walls");
            runtime.SetActive(false);
        }

        // Disable runtime generator on GridManager — this tool is for static cubes only.
        var gen = grid.GetComponent<KitchenPerimeterWalls>();
        if (gen == null)
            gen = Undo.AddComponent<KitchenPerimeterWalls>(grid.gameObject);
        Undo.RecordObject(gen, "Note: prefer runtime expanding walls");
        // Do not turn off runtime gen — expanding windowed walls are the default.
        gen.grid = grid;

        EditorUtility.DisplayDialog(
            "Kitchen Walls",
            "Created simple cube walls under KitchenWalls for reference.\n\n" +
            "For walls with WINDOWS that expand with the floor, leave KitchenPerimeterWalls.generateAtRuntime ON " +
            "(North/West/East). Delete or hide KitchenWalls if they overlap.",
            "OK");

        float minX = grid.Origin.x;
        float minZ = grid.Origin.z;
        float maxX = minX + grid.Width * grid.cellSize;
        float maxZ = minZ + grid.Height * grid.cellSize;
        float cx = (minX + maxX) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        float width = Mathf.Max(0.5f, maxX - minX);
        float depth = Mathf.Max(0.5f, maxZ - minZ);
        float y = grid.Origin.y + height * 0.5f;
        float t = Mathf.Max(0.2f, thickness);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create KitchenWalls");
        if (parent != null)
            root.transform.SetParent(parent, true);

        CreateWall(root.transform, "Wall_North", new Vector3(cx, y, maxZ + t * 0.5f), 0f,
            new Vector3(width, height, t), sharedMesh, sharedMats);
        CreateWall(root.transform, "Wall_South", new Vector3(cx, y, minZ - t * 0.5f), 0f,
            new Vector3(width, height, t), sharedMesh, sharedMats);
        CreateWall(root.transform, "Wall_West", new Vector3(minX - t * 0.5f, y, cz), 90f,
            new Vector3(depth, height, t), sharedMesh, sharedMats);
        CreateWall(root.transform, "Wall_East", new Vector3(maxX + t * 0.5f, y, cz), 90f,
            new Vector3(depth, height, t), sharedMesh, sharedMats);

        // Leave originals visible so user can delete/hide them after comparing
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log(
            "Created editable KitchenWalls (North/South/East/West). " +
            "Move/scale them in the Scene view. Runtime wall generation is off. " +
            "Hide or delete the old Wall / Wall (1) objects if you no longer need them.",
            root);
    }

    static void CreateWall(
        Transform parent, string name, Vector3 pos, float yawY, Vector3 scale,
        Mesh mesh, Material[] mats)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yawY, 0f);
        go.transform.localScale = scale;

        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && mesh != null)
            filter.sharedMesh = mesh;

        var rend = go.GetComponent<MeshRenderer>();
        if (rend != null && mats != null && mats.Length > 0)
            rend.sharedMaterials = mats;

        // Pathfinding + camera cutaway pick these up by name ("Wall")
        if (go.GetComponent<GridObstacle>() == null)
            go.AddComponent<GridObstacle>();
        if (go.GetComponent<CameraOcclusionWall>() == null)
        {
            var occ = go.AddComponent<CameraOcclusionWall>();
            occ.lowerDistance = Mathf.Max(3.5f, scale.y * 0.95f);
        }

        // Ensure collider stays for raycasts / blocking if desired
        var col = go.GetComponent<BoxCollider>();
        if (col != null) col.enabled = true;
    }

    static void CollectWallLook(ref Mesh mesh, ref Material[] mats, ref float height, ref float thickness)
    {
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            string n = r.gameObject.name;
            if (n != "Wall" && !n.StartsWith("Wall (")) continue;
            if (n.StartsWith("Wall_")) continue;

            var filter = r.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                mesh = filter.sharedMesh;
            if (r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                mats = r.sharedMaterials;

            height = Mathf.Max(height, r.transform.localScale.y);
            float thick = Mathf.Min(r.transform.localScale.x, r.transform.localScale.z);
            if (thick > 0.15f && thick < 2.5f)
                thickness = thick;
        }
    }
}
#endif
