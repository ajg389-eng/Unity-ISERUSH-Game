using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class CreateWorkerCardPrefab
{
    static CreateWorkerCardPrefab()
    {
        EditorApplication.delayCall += EnsurePrefabAndAssign;
    }

    static void EnsurePrefabAndAssign()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EnsurePrefabFolder();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerCardPrefabBuilder.DefaultPrefabPath);
        if (prefab == null)
        {
            var root = WorkerCardPrefabBuilder.Build();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, WorkerCardPrefabBuilder.DefaultPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + WorkerCardPrefabBuilder.DefaultPrefabPath);
        }

        if (prefab == null) return;

        int count = 0;
        foreach (var workersUI in UnityEngine.Object.FindObjectsByType<WorkersUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (workersUI.workerCardPrefab != null) continue;
            Undo.RecordObject(workersUI, "Assign Worker Card Prefab");
            workersUI.workerCardPrefab = prefab;
            EditorUtility.SetDirty(workersUI);
            count++;
        }

        if (count > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("Assigned Worker Card prefab on " + count + " WorkersUI component(s). Save the scene to keep it.");
        }
    }

    [MenuItem("Production/Create Worker Card UI Prefab")]
    public static void Create()
    {
        EnsurePrefabFolder();
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerCardPrefabBuilder.DefaultPrefabPath);
        if (existing != null && !EditorUtility.DisplayDialog(
                "Worker Card Prefab",
                "WorkerCard.prefab already exists. Replace it with a fresh default layout?",
                "Replace",
                "Cancel"))
            return;

        var root = WorkerCardPrefabBuilder.Build();
        PrefabUtility.SaveAsPrefabAsset(root, WorkerCardPrefabBuilder.DefaultPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssignToScene();
        Debug.Log("Created " + WorkerCardPrefabBuilder.DefaultPrefabPath + " and assigned it in the scene.");
    }

    [MenuItem("Production/Assign Worker Card Prefab To Scene")]
    public static void AssignToScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerCardPrefabBuilder.DefaultPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("No prefab at " + WorkerCardPrefabBuilder.DefaultPrefabPath + ". Run Production > Create Worker Card UI Prefab first.");
            return;
        }

        int count = 0;
        foreach (var workersUI in UnityEngine.Object.FindObjectsByType<WorkersUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(workersUI, "Assign Worker Card Prefab");
            workersUI.workerCardPrefab = prefab;
            EditorUtility.SetDirty(workersUI);
            count++;
        }

        if (count == 0)
            Debug.LogWarning("No WorkersUI found in the open scene(s).");
        else
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("Assigned Worker Card prefab on " + count + " WorkersUI component(s). Save the scene to keep it.");
        }
    }

    static void EnsurePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }
}
