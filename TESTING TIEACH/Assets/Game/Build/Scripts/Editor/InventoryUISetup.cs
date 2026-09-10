#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places Inventory ContentBox + Stations/Floor tabs under InventoryPanel so you can edit them in the scene.
/// Menu: Game / Setup Inventory Tabs
/// </summary>
public static class InventoryUISetup
{
    const string MenuPath = "Game/Setup Inventory Tabs";

    [MenuItem(MenuPath)]
    public static void Setup()
    {
        var inventoryUi = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUi == null)
        {
            EditorUtility.DisplayDialog(
                "Inventory Tabs",
                "Could not find InventoryUI in the open scene.",
                "OK");
            return;
        }

        if (inventoryUi.panel == null)
        {
            var named = GameObject.Find("InventoryPanel");
            if (named != null)
                inventoryUi.panel = named;
        }

        if (inventoryUi.panel == null)
        {
            EditorUtility.DisplayDialog(
                "Inventory Tabs",
                "InventoryUI has no InventoryPanel assigned.",
                "OK");
            return;
        }

        var existing = inventoryUi.panel.transform.Find(InventoryUI.ContentBoxName);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Inventory Tabs",
                "Inventory ContentBox already exists.\n\nReplace it (resets layout), or select the existing one to edit?",
                "Replace",
                "Select Existing");
            if (!replace)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            Object.DestroyImmediate(existing.gameObject);
        }

        inventoryUi.useSceneLayout = true;
        inventoryUi.SetupSceneTabs(forceDefaultLayout: true);

        var contentBox = inventoryUi.panel.transform.Find(InventoryUI.ContentBoxName);
        EditorUtility.SetDirty(inventoryUi);
        if (contentBox != null)
            EditorUtility.SetDirty(contentBox.gameObject);

        Selection.activeGameObject = contentBox != null ? contentBox.gameObject : inventoryUi.panel;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "Inventory Tabs",
            "Created Inventory ContentBox under InventoryPanel.\n\n" +
            "Hierarchy: ContentBox → TabBar (Stations / Floor), StationsPanel, FloorPanel.\n" +
            "Keep \"Use Scene Layout\" enabled on InventoryUI.\n" +
            "Save the scene when finished.",
            "OK");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupValidate() => !EditorApplication.isPlaying;
}
#endif
