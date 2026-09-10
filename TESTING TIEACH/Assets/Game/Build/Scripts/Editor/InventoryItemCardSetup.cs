#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates the InventoryItemCard prefab and configures the stations Content grid in the scene.
/// Menu: Game / Setup Inventory Item Cards
/// </summary>
public static class InventoryItemCardSetup
{
    const string MenuPath = "Game/Setup Inventory Item Cards";
    const string PrefabFolder = "Assets/Game/Build/Prefabs";
    const string PrefabPath = PrefabFolder + "/InventoryItemCard.prefab";

    [MenuItem(MenuPath)]
    public static void Setup()
    {
        EnsureFolders();

        var prefab = CreateOrUpdateCardPrefab();
        var inventoryUi = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUi == null)
        {
            EditorUtility.DisplayDialog(
                "Inventory Item Cards",
                "Created card prefab at:\n" + PrefabPath + "\n\n" +
                "Could not find InventoryUI in the open scene to wire it up.",
                "OK");
            Selection.activeObject = prefab;
            return;
        }

        inventoryUi.rowPrefab = prefab;
        inventoryUi.useSquareCards = true;
        inventoryUi.useSceneGridLayout = true;

        if (inventoryUi.contentParent == null)
            TryFindContent(inventoryUi);

        if (inventoryUi.contentParent != null)
        {
            inventoryUi.EnsureStationsScrollSetup();
            inventoryUi.SetupStationsGrid(forceDefaultLayout: true);
            PlaceDesignPreviewCards(inventoryUi, prefab);
            EditorUtility.SetDirty(inventoryUi.contentParent.gameObject);
        }

        EditorUtility.SetDirty(inventoryUi);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Selection.activeObject = prefab;
        EditorUtility.DisplayDialog(
            "Inventory Item Cards",
            "Created / updated:\n" + PrefabPath + "\n\n" +
            "• Assigned to InventoryUI.rowPrefab\n" +
            "• Stations Content uses a 2-column Grid Layout Group\n" +
            "• Preview cards placed under Content for editing (cleared on Play refresh)\n\n" +
            "Edit the prefab (or a preview card) then save the scene.",
            "OK");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupValidate() => !EditorApplication.isPlaying;

    static GameObject CreateOrUpdateCardPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Inventory Item Cards",
                "InventoryItemCard prefab already exists.\n\nReplace it (resets card layout), or keep the existing prefab?",
                "Replace",
                "Keep Existing");
            if (!replace)
                return existing;
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        var tempParent = new GameObject("__InventoryCardPrefabTemp");
        try
        {
            var card = InventoryItemCardBuilder.Create(tempParent.transform);
            card.nameText.text = "Station";
            card.qtyText.text = "0";
            card.priceText.text = "$0";

            var prefab = PrefabUtility.SaveAsPrefabAsset(card.gameObject, PrefabPath);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(tempParent);
        }
    }

    static void PlaceDesignPreviewCards(InventoryUI inventoryUi, GameObject prefab)
    {
        var content = inventoryUi.contentParent;
        if (content == null || prefab == null) return;

        // Clear previous design previews only (keep ExpandFloorRow if any).
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            if (child == null) continue;
            if (child.name == "ExpandFloorRow" || child.name == "ExpandFloorBar") continue;
            Object.DestroyImmediate(child.gameObject);
        }

        int count = 4;
        if (inventoryUi.inventory != null && inventoryUi.inventory.allItems != null)
            count = Mathf.Max(1, inventoryUi.inventory.allItems.Count);

        for (int i = 0; i < count; i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, content);
            instance.name = InventoryItemCardBuilder.CardName + "_Preview";
            var card = instance.GetComponent<InventoryItemCardUI>();
            if (card == null) continue;

            ItemDefinition item = null;
            if (inventoryUi.inventory != null && inventoryUi.inventory.allItems != null
                && i < inventoryUi.inventory.allItems.Count)
                item = inventoryUi.inventory.allItems[i];

            if (item != null)
            {
                card.Bind(item, 0, null, null);
            }
            else
            {
                if (card.nameText != null) card.nameText.text = "Station " + (i + 1);
                if (card.qtyText != null) card.qtyText.text = "0";
                if (card.priceText != null) card.priceText.text = "$100";
            }
        }
    }

    static void TryFindContent(InventoryUI inventoryUi)
    {
        if (inventoryUi.panel == null) return;
        var content = inventoryUi.panel.transform.Find(
            InventoryUI.ContentBoxName + "/" + InventoryUI.StationsPanelName + "/Scroll View/Content");
        if (content == null)
            content = inventoryUi.panel.transform.Find("Scroll View/Content");
        if (content != null)
            inventoryUi.contentParent = content;
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game"))
            AssetDatabase.CreateFolder("Assets", "Game");
        if (!AssetDatabase.IsValidFolder("Assets/Game/Build"))
            AssetDatabase.CreateFolder("Assets/Game", "Build");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Game/Build", "Prefabs");
    }
}
#endif
