using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class CreateIngredientsOrderTab
{
    const string OrderConfigPath = "Assets/OrderSystem/CustomerOrderConfig.asset";

    [InitializeOnLoadMethod]
    static void AutoSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureKitchenInventoryInScene(false);
            // Soft-add ingredients tab if management screen exists without it
            var controller = Object.FindFirstObjectByType<ManagementScreenController>();
            if (controller != null && controller.managementPanel != null)
            {
                var content = controller.managementPanel.transform.Find("ContentBox/TabContentArea");
                if (content != null && content.Find("IngredientsPanel") == null)
                    AddIngredientsTab();
            }
        };
    }

    [MenuItem("Production/Add Kitchen Inventory")]
    public static void AddKitchenInventoryMenu()
    {
        EnsureKitchenInventoryInScene(true);
    }

    [MenuItem("Production/Add Ingredients Tab To Management Screen")]
    [MenuItem("GameObject/UI/Add Ingredients Tab to Management Screen", false, 12)]
    public static void AddIngredientsTab()
    {
        EnsureKitchenInventoryInScene(true);

        var controller = Object.FindFirstObjectByType<ManagementScreenController>();
        if (controller == null)
        {
            Debug.LogWarning("No ManagementScreenController found.");
            return;
        }
        if (controller.managementPanel == null)
        {
            Debug.LogWarning("Management screen has no panel assigned.");
            return;
        }

        Transform contentBox = controller.managementPanel.transform.Find("ContentBox");
        if (contentBox == null)
        {
            Debug.LogWarning("ContentBox not found under management panel.");
            return;
        }
        Transform tabBar = contentBox.Find("TabBar");
        Transform tabContentArea = contentBox.Find("TabContentArea");
        if (tabBar == null || tabContentArea == null)
        {
            Debug.LogWarning("TabBar or TabContentArea not found.");
            return;
        }

        if (tabContentArea.Find("IngredientsPanel") != null)
        {
            Debug.Log("Ingredients tab already present.");
            return;
        }

        GameObject tabBtn = CreateTabButton("Tab_Ingredients", tabBar, "Ingredients");
        GameObject panel = CreateIngredientsPanel("IngredientsPanel", tabContentArea);
        panel.SetActive(false);

        Undo.RegisterCreatedObjectUndo(tabBtn, "Add Ingredients Tab");
        Undo.RegisterCreatedObjectUndo(panel, "Add Ingredients Panel");

        var so = new SerializedObject(controller);
        var tabButtonsProp = so.FindProperty("tabButtons");
        var tabPanelsProp = so.FindProperty("tabPanels");
        if (tabButtonsProp != null && tabPanelsProp != null && tabButtonsProp.isArray && tabPanelsProp.isArray)
        {
            // Insert after Workers (index 2) if possible, else append
            int insertIndex = Mathf.Min(2, tabButtonsProp.arraySize);
            tabButtonsProp.InsertArrayElementAtIndex(insertIndex);
            tabButtonsProp.GetArrayElementAtIndex(insertIndex).objectReferenceValue = tabBtn.GetComponent<Button>();
            tabPanelsProp.InsertArrayElementAtIndex(insertIndex);
            tabPanelsProp.GetArrayElementAtIndex(insertIndex).objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        // Rebind tab listeners by forcing domain-friendly dirty; controller Start already ran in edit mode only once
        Debug.Log("Ingredients tab added. Open Management → Ingredients to order packs with money. Save the scene.");
    }

    static void EnsureKitchenInventoryInScene(bool log)
    {
        var existing = Object.FindFirstObjectByType<KitchenInventory>();
        if (existing != null)
        {
            if (existing.orderConfig == null)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(OrderConfigPath);
                if (cfg != null)
                {
                    Undo.RecordObject(existing, "Assign Order Config");
                    existing.orderConfig = cfg;
                    EditorUtility.SetDirty(existing);
                }
            }
            if (log) Debug.Log("KitchenInventory already in scene.", existing);
            return;
        }

        var go = new GameObject("KitchenInventory");
        Undo.RegisterCreatedObjectUndo(go, "Create Kitchen Inventory");
        var inv = go.AddComponent<KitchenInventory>();
        inv.orderConfig = AssetDatabase.LoadAssetAtPath<CustomerOrderConfig>(OrderConfigPath);
        inv.defaultStartingStock = 8;
        inv.grantStartingStock = true;

        // Parent under ScriptObjects if present
        var scriptObjects = GameObject.Find("ScriptObjects");
        if (scriptObjects != null)
            go.transform.SetParent(scriptObjects.transform, true);

        if (log) Debug.Log("KitchenInventory created. Assign orderConfig if needed.", inv);
    }

    static GameObject CreateTabButton(string name, Transform parent, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 100;
        le.preferredWidth = 120;
        le.flexibleHeight = 1;

        var image = go.AddComponent<Image>();
        image.color = new Color(0.35f, 0.35f, 0.4f, 1f);
        go.AddComponent<Button>();

        GameObject textGo = new GameObject("Text (TMP)", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return go;
    }

    public static GameObject CreateIngredientsPanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        GameObject titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panel.transform, false);
        var titleRt = (RectTransform)titleGo.transform;
        titleRt.anchorMin = new Vector2(0, 1f);
        titleRt.anchorMax = new Vector2(1, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0, -10);
        titleRt.sizeDelta = new Vector2(-24, 28);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "Order Ingredients";
        title.fontSize = 20;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;

        GameObject moneyGo = new GameObject("MoneyHint", typeof(RectTransform));
        moneyGo.transform.SetParent(panel.transform, false);
        var moneyRt = (RectTransform)moneyGo.transform;
        moneyRt.anchorMin = new Vector2(0, 1f);
        moneyRt.anchorMax = new Vector2(1, 1f);
        moneyRt.pivot = new Vector2(0.5f, 1f);
        moneyRt.anchoredPosition = new Vector2(0, -38);
        moneyRt.sizeDelta = new Vector2(-24, 22);
        var moneyTmp = moneyGo.AddComponent<TextMeshProUGUI>();
        moneyTmp.text = "Money: —";
        moneyTmp.fontSize = 14;
        moneyTmp.alignment = TextAlignmentOptions.Center;
        moneyTmp.color = new Color(0.85f, 0.9f, 0.7f, 1f);
        if (TMP_Settings.defaultFontAsset != null) moneyTmp.font = TMP_Settings.defaultFontAsset;

        var ui = panel.AddComponent<IngredientsOrderUI>();
        ui.headerText = title;
        ui.moneyHintText = moneyTmp;
        ui.inventory = Object.FindFirstObjectByType<KitchenInventory>();
        return panel;
    }
}
