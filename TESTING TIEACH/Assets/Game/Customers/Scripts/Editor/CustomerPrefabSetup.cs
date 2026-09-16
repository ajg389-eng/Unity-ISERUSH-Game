#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds or updates the Customer prefab with all components and UI children editable in the Inspector.
/// Menu: Game / Setup Customer Prefab
/// </summary>
public static class CustomerPrefabSetup
{
    const string PrefabPath = "Assets/Game/Customers/Prefabs/Customer.prefab";
    const string MenuPath = "Game/Setup Customer Prefab";

    [MenuItem(MenuPath)]
    public static void SetupCustomerPrefab()
    {
        var root = LoadOrCreatePrefabRoot();
        if (root == null) return;

        EnsureBody(root);
        var ai = EnsureComponent<CustomerAI>(root);
        var patience = EnsureComponent<CustomerPatienceMeter>(root);
        var orderLabel = EnsureComponent<CustomerOrderLabel>(root);

        orderLabel.EnsureHierarchy();
        if (orderLabel.labelText != null)
            orderLabel.labelText.text = "Order";

        patience.EnsureHierarchy();
        patience.ConfigureImages();
        if (patience.meterRoot != null)
            patience.meterRoot.SetActive(false);

        ai.patienceMeter = patience;
        ai.orderLabel = orderLabel;
        ai.moveSpeed = 2.5f;
        ai.patienceDuration = 50f;

        SavePrefab(root);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Debug.Log($"Customer prefab updated at {PrefabPath}");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupCustomerPrefabValidate() => !Application.isPlaying;

    static GameObject LoadOrCreatePrefabRoot()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
            return PrefabUtility.LoadPrefabContents(PrefabPath);

        var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Customer";
        root.transform.localPosition = new Vector3(0f, 0.98f, 0f);

        if (!AssetDatabase.IsValidFolder("Assets/Game/Customers/Prefabs"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Customers"))
                AssetDatabase.CreateFolder("Assets/Game", "Customers");
            AssetDatabase.CreateFolder("Assets/Game/Customers", "Prefabs");
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return PrefabUtility.LoadPrefabContents(PrefabPath);
    }

    static void EnsureBody(GameObject root)
    {
        var collider = root.GetComponent<CapsuleCollider>();
        if (collider == null)
            collider = root.AddComponent<CapsuleCollider>();
        collider.radius = 0.5f;
        collider.height = 2f;
        collider.center = Vector3.zero;

        var renderer = root.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial == null)
        {
            var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (mat != null) renderer.sharedMaterial = mat;
        }
    }

    static T EnsureComponent<T>(GameObject root) where T : Component
    {
        var comp = root.GetComponent<T>();
        if (comp == null)
            comp = root.AddComponent<T>();
        return comp;
    }

    static void SavePrefab(GameObject root)
    {
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
