#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class CustomerPathSetup
{
    [MenuItem("Game/Create Customer Entry Path")]
    public static void CreateEntryPath()
    {
        var root = new GameObject("CustomerEntryPath");
        var path = root.AddComponent<CustomerPath>();

        // Place near scene view / origin so it's easy to find
        var view = SceneView.lastActiveSceneView;
        Vector3 origin = view != null ? view.pivot : Vector3.zero;
        origin.y = 0f;
        root.transform.position = origin;

        path.waypoints.Clear();
        string[] names = { "OutsideSpawn", "Approach", "DoorEntry" };
        for (int i = 0; i < names.Length; i++)
        {
            var wp = new GameObject(names[i]);
            wp.transform.SetParent(root.transform, false);
            wp.transform.localPosition = new Vector3(0f, 0f, -i * 2.5f);
            path.waypoints.Add(wp.transform);
        }

        Undo.RegisterCreatedObjectUndo(root, "Create Customer Entry Path");
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("CustomerEntryPath created. Move the waypoints: OutsideSpawn → Approach → DoorEntry (inside). Then assign it to CustomerSpawner → Entry Path.");
    }

    [MenuItem("Game/Create Customer Exit Path")]
    public static void CreateExitPath()
    {
        var root = new GameObject("CustomerExitPath");
        var path = root.AddComponent<CustomerPath>();

        var view = SceneView.lastActiveSceneView;
        Vector3 origin = view != null ? view.pivot : Vector3.zero;
        origin.y = 0f;
        root.transform.position = origin;

        path.waypoints.Clear();
        path.gizmoColor = new Color(1f, 0.45f, 0.2f, 0.9f);
        string[] names = { "LeaveRegister", "DoorExit", "OutsideLeave" };
        for (int i = 0; i < names.Length; i++)
        {
            var wp = new GameObject(names[i]);
            wp.transform.SetParent(root.transform, false);
            wp.transform.localPosition = new Vector3(0f, 0f, i * 2.5f);
            path.waypoints.Add(wp.transform);
        }

        Undo.RegisterCreatedObjectUndo(root, "Create Customer Exit Path");
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("CustomerExitPath created. Move waypoints along the leave route, then assign to CustomerSpawner → Exit Path.");
    }
}
#endif
