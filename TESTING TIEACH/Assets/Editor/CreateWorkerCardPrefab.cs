using UnityEngine;
using UnityEditor;

public static class CreateWorkerCardPrefab
{
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
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created " + WorkerCardPrefabBuilder.DefaultPrefabPath + ". Assign it on WorkersUI > Worker Card Prefab (or use Production > Assign Worker Card Prefab To Scene).");
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
        foreach (var workersUI in Object.FindObjectsByType<WorkersUI>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(workersUI, "Assign Worker Card Prefab");
            workersUI.workerCardPrefab = prefab;
            EditorUtility.SetDirty(workersUI);
            count++;
        }

        if (count == 0)
            Debug.LogWarning("No WorkersUI found in the open scene(s).");
        else
            Debug.Log("Assigned Worker Card prefab on " + count + " WorkersUI component(s).");
    }

    static void EnsurePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }
}
