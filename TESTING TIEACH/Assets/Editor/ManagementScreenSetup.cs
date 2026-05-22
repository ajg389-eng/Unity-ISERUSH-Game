using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public static class ManagementScreenSetup
{
    const string MenuName = "GameObject/UI/Management Screen";

    static ManagementScreenSetup()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            HideManagementButtonsInScene();
    }

    static void HideManagementButtonsInScene()
    {
        foreach (var controller in Object.FindObjectsByType<ManagementScreenController>(FindObjectsSortMode.None))
        {
            if (controller != null && controller.openButton != null)
                controller.openButton.gameObject.SetActive(false);
        }
    }

    [MenuItem("GameObject/UI/Employee Assignment Panel (in-world)", false, 11)]
    public static void CreateEmployeeAssignmentPanel()
    {
        var go = new GameObject("EmployeeAssignmentPanel");
        var panel = go.AddComponent<EmployeeAssignmentPanel>();
        var modeManager = Object.FindFirstObjectByType<GameModeManager>();
        if (modeManager != null) panel.modeManager = modeManager;
        Undo.RegisterCreatedObjectUndo(go, "Create Employee Assignment Panel");
        Selection.activeGameObject = go;
        Debug.Log("Employee Assignment Panel added. In Play mode, click an employee to show assignments; click stations to assign. Ensure employees and stations have Colliders for clicking.");
    }

    [MenuItem(MenuName, false, 10)]
    public static void CreateManagementScreen()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("No Canvas found in scene. Create a Canvas first (e.g. GameObject > UI > Canvas).");
            return;
        }

        Transform parent = canvas.transform;

        // Root: ManagementScreen (holds the controller)
        GameObject root = new GameObject("ManagementScreen", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<ManagementScreenController>();
        var modeManager = Object.FindFirstObjectByType<GameModeManager>();
        if (modeManager != null) controller.modeManager = modeManager;

        // ---- Open button (on HUD) – visible only in Play mode, hidden in Build mode ----
        GameObject openBtnGo = CreateUIButton("ManagementButton", root.transform, new Vector2(0.5f, 0.5f), new Vector2(160, 30), new Vector2(170, -214));
        TextMeshProUGUI openBtnText = openBtnGo.GetComponentInChildren<TextMeshProUGUI>();
        if (openBtnText != null) openBtnText.text = "Management";
        controller.openButton = openBtnGo.GetComponent<Button>();
        openBtnGo.SetActive(false); // visible only in play mode (controller turns on in Start)

        // ---- Panel (overlay) ----
        GameObject panel = new GameObject("ManagementPanel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);

        controller.managementPanel = panel;
        panel.SetActive(false);

        // Panel content area (centered box)
        GameObject contentBox = new GameObject("ContentBox", typeof(RectTransform));
        contentBox.transform.SetParent(panel.transform, false);
        RectTransform boxRect = (RectTransform)contentBox.transform;
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(500, 400);
        boxRect.anchoredPosition = Vector2.zero;

        var boxImage = contentBox.AddComponent<Image>();
        boxImage.color = new Color(0.2f, 0.2f, 0.25f, 0.98f);

        // Title
        GameObject titleGo = CreateTMPText("Title", contentBox.transform, "Management", 28);
        RectTransform titleRect = (RectTransform)titleGo.transform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -20);
        titleRect.sizeDelta = new Vector2(400, 40);

        // Close button (top-right of content box)
        GameObject closeBtnGo = CreateUIButton("CloseButton", contentBox.transform, new Vector2(1f, 1f), new Vector2(100, 32), new Vector2(-60, -20));
        TextMeshProUGUI closeBtnText = closeBtnGo.GetComponentInChildren<TextMeshProUGUI>();
        if (closeBtnText != null) closeBtnText.text = "Close";
        controller.closeButton = closeBtnGo.GetComponent<Button>();

        // Tab bar (horizontal row of tab buttons)
        GameObject tabBar = new GameObject("TabBar", typeof(RectTransform));
        tabBar.transform.SetParent(contentBox.transform, false);
        RectTransform tabBarRect = (RectTransform)tabBar.transform;
        tabBarRect.anchorMin = new Vector2(0, 1f);
        tabBarRect.anchorMax = new Vector2(1, 1f);
        tabBarRect.pivot = new Vector2(0.5f, 1f);
        tabBarRect.anchoredPosition = new Vector2(0, -60);
        tabBarRect.sizeDelta = new Vector2(0, 36);

        var tabBarHLG = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabBarHLG.spacing = 8;
        tabBarHLG.padding = new RectOffset(10, 10, 4, 4);
        tabBarHLG.childAlignment = TextAnchor.MiddleLeft;
        tabBarHLG.childControlWidth = false;
        tabBarHLG.childControlHeight = true;
        tabBarHLG.childForceExpandWidth = false;
        tabBarHLG.childForceExpandHeight = true;

        GameObject statsTabBtn = CreateTabButton("Tab_StoreStats", tabBar.transform, "Store Stats");
        GameObject workersTabBtn = CreateTabButton("Tab_Workers", tabBar.transform, "Workers");
        GameObject settingsTabBtn = CreateTabButton("Tab_Settings", tabBar.transform, "Settings");
        GameObject otherTabBtn = CreateTabButton("Tab_Other", tabBar.transform, "Other");

        // Area where tab panels sit (below tab bar)
        GameObject tabContentArea = new GameObject("TabContentArea", typeof(RectTransform));
        tabContentArea.transform.SetParent(contentBox.transform, false);
        RectTransform tabContentRect = (RectTransform)tabContentArea.transform;
        tabContentRect.anchorMin = new Vector2(0, 0);
        tabContentRect.anchorMax = new Vector2(1, 1);
        tabContentRect.offsetMin = new Vector2(20, 20);
        tabContentRect.offsetMax = new Vector2(-20, -100);

        EnsureStoreStatisticsManager();
        GameObject statsPanel = CreateStoreStatsPanel("StatsPanel", tabContentArea.transform);
        GameObject workersPanel = CreateWorkersPanel("WorkersPanel", tabContentArea.transform);
        GameObject settingsPanel = CreateTabPanel("SettingsPanel", tabContentArea.transform, "Settings", "Game options, volume, etc.");
        GameObject otherPanel = CreateTabPanel("OtherPanel", tabContentArea.transform, "Other", "Add your management tools here.");

        statsPanel.SetActive(true);
        workersPanel.SetActive(false);
        settingsPanel.SetActive(false);
        otherPanel.SetActive(false);

        controller.tabButtons = new Button[] { statsTabBtn.GetComponent<Button>(), workersTabBtn.GetComponent<Button>(), settingsTabBtn.GetComponent<Button>(), otherTabBtn.GetComponent<Button>() };
        controller.tabPanels = new GameObject[] { statsPanel, workersPanel, settingsPanel, otherPanel };

        Undo.RegisterCreatedObjectUndo(root, "Create Management Screen");
        Selection.activeGameObject = root;
        Debug.Log("Management Screen added: button is visible only in Play mode. Tabs: Store Stats, Workers, Settings, Other. Run Production > Create Worker Card UI Prefab if needed. Assign ProductionManager > Employee Prefab to hire workers.");
    }

    [MenuItem("Production/Add Workers Tab to Existing Management Screen")]
    [MenuItem("GameObject/UI/Add Workers Tab to Management Screen", false, 11)]
    public static void AddWorkersTabToExisting()
    {
        var controller = Object.FindFirstObjectByType<ManagementScreenController>();
        if (controller == null)
        {
            Debug.LogWarning("No ManagementScreenController found in the scene.");
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
            Debug.LogWarning("TabBar or TabContentArea not found. Is this a Management Screen created by GameObject > UI > Management Screen?");
            return;
        }

        // Already has Workers tab?
        if (contentBox.Find("TabBar/Tab_Workers") != null || tabContentArea.Find("WorkersPanel") != null)
        {
            Debug.Log("Workers tab already present.");
            return;
        }

        GameObject workersTabBtn = CreateTabButton("Tab_Workers", tabBar, "Workers");
        GameObject workersPanel = CreateWorkersPanel("WorkersPanel", tabContentArea);
        workersPanel.SetActive(false);

        Undo.RegisterCreatedObjectUndo(workersTabBtn, "Add Workers Tab");
        Undo.RegisterCreatedObjectUndo(workersPanel, "Add Workers Panel");

        var so = new SerializedObject(controller);
        SerializedProperty tabButtonsProp = so.FindProperty("tabButtons");
        SerializedProperty tabPanelsProp = so.FindProperty("tabPanels");
        if (tabButtonsProp == null || tabPanelsProp == null || !tabButtonsProp.isArray || !tabPanelsProp.isArray)
        {
            Debug.LogWarning("Could not find tabButtons or tabPanels on controller.");
            return;
        }
        int insertIndex = 1;
        tabButtonsProp.InsertArrayElementAtIndex(insertIndex);
        tabButtonsProp.GetArrayElementAtIndex(insertIndex).objectReferenceValue = workersTabBtn.GetComponent<Button>();
        tabPanelsProp.InsertArrayElementAtIndex(insertIndex);
        tabPanelsProp.GetArrayElementAtIndex(insertIndex).objectReferenceValue = workersPanel;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);

        Debug.Log("Workers tab added to Management Screen (second tab). Run Production > Create Worker Card UI Prefab and Assign Worker Card Prefab To Scene if needed. Assign ProductionManager > Employee Prefab to hire workers.");
    }

    static GameObject CreateUIButton(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        var image = go.AddComponent<Image>();
        image.color = new Color(0.35f, 0.35f, 0.4f, 1f);

        var button = go.AddComponent<Button>();

        GameObject textGo = new GameObject("Text (TMP)", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Button";
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        AssignTMPFont(tmp);
        return go;
    }

    static void AssignTMPFont(TextMeshProUGUI tmp)
    {
        if (tmp.font != null) return;
        var defaultFont = TMP_Settings.defaultFontAsset;
        if (defaultFont != null) tmp.font = defaultFont;
    }

    static GameObject CreateTMPText(string name, Transform parent, string text, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        AssignTMPFont(tmp);
        return go;
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
        AssignTMPFont(tmp);
        return go;
    }

    static void EnsureStoreStatisticsManager()
    {
        if (Object.FindFirstObjectByType<StoreStatisticsManager>() != null) return;
        var go = new GameObject("StoreStatisticsManager");
        go.AddComponent<StoreStatisticsManager>();
    }

    static GameObject CreateStoreStatsPanel(string name, Transform parent)
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

        GameObject titleGo = CreateTMPText("Title", panel.transform, "Store Stats", 20);
        RectTransform titleRect = (RectTransform)titleGo.transform;
        titleRect.anchorMin = new Vector2(0, 1f);
        titleRect.anchorMax = new Vector2(1, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -12);
        titleRect.sizeDelta = new Vector2(0, 28);

        GameObject scrollGo = new GameObject("StatsScrollView", typeof(RectTransform));
        scrollGo.transform.SetParent(panel.transform, false);
        RectTransform scrollRect = (RectTransform)scrollGo.transform;
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(1, 1);
        scrollRect.offsetMin = new Vector2(12, 8);
        scrollRect.offsetMax = new Vector2(-12, -8);
        scrollRect.anchoredPosition = new Vector2(0, -20);
        scrollRect.sizeDelta = new Vector2(0, -40);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var viewportRT = (RectTransform)viewport.transform;
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = (RectTransform)content.transform;
        contentRT.anchorMin = new Vector2(0, 1f);
        contentRT.anchorMax = new Vector2(1, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 400);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        scroll.viewport = viewportRT;
        scroll.content = contentRT;

        GameObject statsTextGo = new GameObject("StatsText", typeof(RectTransform));
        statsTextGo.transform.SetParent(content.transform, false);
        var statsTextRT = (RectTransform)statsTextGo.transform;
        statsTextRT.anchorMin = new Vector2(0, 1f);
        statsTextRT.anchorMax = new Vector2(1, 1f);
        statsTextRT.sizeDelta = new Vector2(0, 380);
        var le = statsTextGo.AddComponent<LayoutElement>();
        le.minHeight = 300;
        le.flexibleHeight = 1;
        var tmp = statsTextGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Loading…";
        tmp.fontSize = 14;
        tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = new Color(0.9f, 0.9f, 0.95f, 1f);
        tmp.enableWordWrapping = true;
        AssignTMPFont(tmp);

        var storeStatsUI = panel.AddComponent<StoreStatsUI>();
        storeStatsUI.statsText = tmp;

        return panel;
    }

    static GameObject CreateWorkersPanel(string name, Transform parent)
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

        GameObject titleGo = CreateTMPText("Title", panel.transform, "Workers", 20);
        RectTransform titleRect = (RectTransform)titleGo.transform;
        titleRect.anchorMin = new Vector2(0, 1f);
        titleRect.anchorMax = new Vector2(1, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -12);
        titleRect.sizeDelta = new Vector2(0, 28);

        GameObject countGo = CreateTMPText("CountText", panel.transform, "Workers: 0", 16);
        RectTransform countRect = (RectTransform)countGo.transform;
        countRect.anchorMin = new Vector2(0, 1f);
        countRect.anchorMax = new Vector2(1, 1f);
        countRect.pivot = new Vector2(0.5f, 1f);
        countRect.anchoredPosition = new Vector2(0, -50);
        countRect.sizeDelta = new Vector2(-32, 24);
        countGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopLeft;
        AssignTMPFont(countGo.GetComponent<TextMeshProUGUI>());

        GameObject costGo = CreateTMPText("CostText", panel.transform, "Cost: $100", 14);
        RectTransform costRect = (RectTransform)costGo.transform;
        costRect.anchorMin = new Vector2(0, 1f);
        costRect.anchorMax = new Vector2(1, 1f);
        costRect.pivot = new Vector2(0.5f, 1f);
        costRect.anchoredPosition = new Vector2(0, -78);
        costRect.sizeDelta = new Vector2(-32, 22);
        costGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopLeft;
        costGo.GetComponent<TextMeshProUGUI>().color = new Color(0.85f, 0.85f, 0.9f, 1f);
        AssignTMPFont(costGo.GetComponent<TextMeshProUGUI>());

        GameObject hireBtnGo = CreateUIButton("HireButton", panel.transform, new Vector2(0.5f, 1f), new Vector2(140, 36), new Vector2(0, -120));
        TextMeshProUGUI hireBtnText = hireBtnGo.GetComponentInChildren<TextMeshProUGUI>();
        if (hireBtnText != null) hireBtnText.text = "Hire Worker";

        var workersUI = panel.AddComponent<WorkersUI>();
        workersUI.countText = countGo.GetComponent<TextMeshProUGUI>();
        workersUI.costText = costGo.GetComponent<TextMeshProUGUI>();
        workersUI.hireButton = hireBtnGo.GetComponent<Button>();
        workersUI.cardContainer = CreateWorkerCardsScroll(panel.transform);
        workersUI.workerCardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerCardPrefabBuilder.DefaultPrefabPath);

        return panel;
    }

    static Transform CreateWorkerCardsScroll(Transform parent)
    {
        GameObject scrollGo = new GameObject("WorkerCardsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        RectTransform scrollRect = (RectTransform)scrollGo.transform;
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(12, 12);
        scrollRect.offsetMax = new Vector2(-12, -140);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var viewportRT = (RectTransform)viewport.transform;
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("CardContainer", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRT = (RectTransform)content.transform;
        contentRT.anchorMin = new Vector2(0, 1f);
        contentRT.anchorMax = Vector2.one;
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0, 0);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        scroll.viewport = viewportRT;
        scroll.content = contentRT;
        return content.transform;
    }

    static GameObject CreateTabPanel(string name, Transform parent, string title, string subtitle)
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

        GameObject titleGo = CreateTMPText("Title", panel.transform, title, 20);
        RectTransform titleRect = (RectTransform)titleGo.transform;
        titleRect.anchorMin = new Vector2(0, 1f);
        titleRect.anchorMax = new Vector2(1, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -12);
        titleRect.sizeDelta = new Vector2(0, 28);

        GameObject subGo = CreateTMPText("Subtitle", panel.transform, subtitle, 14);
        RectTransform subRect = (RectTransform)subGo.transform;
        subRect.anchorMin = new Vector2(0, 0);
        subRect.anchorMax = new Vector2(1, 1);
        subRect.offsetMin = new Vector2(16, 16);
        subRect.offsetMax = new Vector2(-16, -40);
        subGo.GetComponent<TextMeshProUGUI>().color = new Color(0.85f, 0.85f, 0.9f, 1f);
        subGo.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopLeft;
        AssignTMPFont(subGo.GetComponent<TextMeshProUGUI>());
        return panel;
    }
}
