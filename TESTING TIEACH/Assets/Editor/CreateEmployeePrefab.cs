using UnityEngine;
using UnityEditor;

public static class CreateEmployeePrefab
{
    const string PrefabPath = "Assets/BuildSystem/KitchenEmployee.prefab";

    [MenuItem("Production/Create Default Employee Prefab")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log("KitchenEmployee.prefab already exists at " + PrefabPath + ". Assign it to ProductionManager > Employee Prefab.");
            return;
        }

        var root = new GameObject("KitchenEmployee");
        root.AddComponent<KitchenEmployee>();
        root.AddComponent<EmployeeTaskBar>();
        root.AddComponent<EmployeeInventoryLabel>();

        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Model";
        capsule.transform.SetParent(root.transform);
        capsule.transform.localPosition = new Vector3(0, 1f, 0);
        capsule.transform.localScale = Vector3.one;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created " + PrefabPath + ". Assign it to ProductionManager > Employee Prefab and set Spawn Point if desired.");
    }
}
