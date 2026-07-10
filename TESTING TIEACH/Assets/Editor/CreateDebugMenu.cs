using UnityEngine;
using UnityEditor;

public static class CreateDebugMenu
{
    [MenuItem("Production/Add Debug Menu")]
    public static void Create()
    {
        var existing = Object.FindFirstObjectByType<DebugMenu>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("DebugMenu already in the scene. Press F1 or ` in Play mode to toggle.");
            return;
        }

        var go = new GameObject("DebugMenu");
        Undo.RegisterCreatedObjectUndo(go, "Create Debug Menu");
        go.AddComponent<DebugMenu>();
        Selection.activeGameObject = go;
        Debug.Log("DebugMenu added. Press F1 or ` in Play mode to open it.");
    }
}
