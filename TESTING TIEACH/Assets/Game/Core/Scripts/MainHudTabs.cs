using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-left Kitchen workspace navigation. Place under PlayerUI via
/// Game → Setup Main HUD Tabs so you can edit position in the scene.
/// </summary>
public class MainHudTabs : MonoBehaviour
{
    public const string StripName = "MainHudTabs";
    const string KitchenWorkspaceTabsName = "KitchenWorkspaceTabs";

    const float TabWidth = 168f;
    const float TabHeight = 40f;
    const float TabSpacing = 10f;
    const float WorkspaceTabSpacing = 4f;
    const float WorkspaceTabTopOffset = 50f;

    static readonly string[] WorkspaceTabNames =
    {
        "Stations", "Workers", "Supply", "Customize"
    };
    static readonly string[] WorkspaceTabObjectNames =
    {
        "WorkspaceTab_Stations", "WorkspaceTab_Workers",
        "WorkspaceTab_Supply", "WorkspaceTab_Customize"
    };

    static readonly Color StripColor = HudTabColors.Strip;
    static readonly Color IdleColor = HudTabColors.Idle;
    static readonly Color ActiveColor = HudTabColors.Active;

    [Header("Layout")]
    [Tooltip("When true, RectTransform position/size are left alone so you can edit them in the scene.")]
    public bool useSceneLayout = true;

    [Tooltip("Anchored position used only when Use Scene Layout is off.")]
    public Vector2 screenOffset = new Vector2(16f, -8f);

    Image inventoryBg;
    Image managementBg;
    Image businessBg;
    Button inventoryTabButton;
    Button managementTabButton;
    Button businessTabButton;
    InventoryUI cachedInventoryUI;
    ManagementScreenController cachedManagementScreen;
    bool visualStateKnown;
    bool displayedInventoryOpen;
    bool displayedManagementOpen;
    bool displayedBuildLocked;
    bool sourceButtonsResolved;
    Button inventorySourceButton;
    Button managementSourceButton;
    float nextPageControllerRefresh;
    bool built;
    RectTransform workspaceTabsRect;
    HorizontalLayoutGroup workspaceTabsLayout;
    readonly Button[] workspaceTabButtons = new Button[4];
    int selectedWorkspaceTab;
    bool workspaceTabsBuilt;

    public Transform InventoryTabTransform => inventoryTabButton != null ? inventoryTabButton.transform : null;
    public Transform ManagementTabTransform => InventoryTabTransform;
    public Transform BusinessTabTransform => InventoryTabTransform;
    public Button KitchenButton => inventoryTabButton;
    public RectTransform WorkspaceTabsRect => workspaceTabsRect;

    public Button WorkspaceTabButton(int index)
    {
        return index >= 0 && index < workspaceTabButtons.Length ? workspaceTabButtons[index] : null;
    }

    public void PrepareWorkspaceTabForNextOpen(int index)
    {
        if (index >= 0 && index < workspaceTabButtons.Length)
            selectedWorkspaceTab = index;
    }

    public bool TryGetWorkspaceTabIndex(Button button, out int index)
    {
        for (int i = 0; i < workspaceTabButtons.Length; i++)
        {
            if (workspaceTabButtons[i] != button) continue;
            index = i;
            return true;
        }
        index = -1;
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInScene();
    }

    public static MainHudTabs EnsureInScene()
    {
        var existing = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.Build();
            return existing;
        }

        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null) return null;

        return CreateInCanvas(canvas.transform);
    }

    /// <summary>Create a scene-editable strip under the given canvas (used by editor setup).</summary>
    public static MainHudTabs CreateInCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        var go = new GameObject(StripName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(MainHudTabs));
        go.transform.SetParent(canvasTransform, false);
        go.transform.SetAsLastSibling();

        var tabs = go.GetComponent<MainHudTabs>();
        tabs.useSceneLayout = true;
        tabs.Build(forceDefaultLayout: true);
        return tabs;
    }

    void Awake()
    {
        Build();
    }

    void LateUpdate()
    {
        HideSourceButtons();
        RefreshVisuals();
        RefreshWorkspaceTabs();
    }

    void Update()
    {
        if (UIInputFocusGuard.IsTyping || PauseMenuUI.IsOpen) return;

        int workspaceTab = GetNumberRowIndex();
        if (workspaceTab >= 0)
        {
            OpenWorkspaceTab(workspaceTab);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            CycleWorkspaceTab(-1);
            return;
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            CycleWorkspaceTab(1);
            return;
        }
    }

    public void Build(bool forceDefaultLayout = false)
    {
        RefreshPageControllerReferences();
        GameUITheme.EnsureHudCanvas(transform, 10);
        var rt = (RectTransform)transform;
        rt.localScale = Vector3.one * TopHudBar.HudScale;
        if (forceDefaultLayout || !useSceneLayout)
            ApplyDefaultLayout(rt);

        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.raycastTarget = true;
        // The connected top-HUD backdrop supplies the strip color across the
        // full screen, including the gaps between all three control groups.
        bg.color = Color.clear;

        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
        if (forceDefaultLayout || !useSceneLayout)
            ApplyDefaultLayoutGroup(hlg);

        // Scene-authored strips can retain a different expansion mode from an
        // earlier layout. Force the same 40px visible button height used by the
        // build and business sub-tabs every time the HUD is rebuilt.
        hlg.childControlHeight = true;
        hlg.childForceExpandHeight = false;

        EnsureTabVisuals();
        SetPrimaryTabHeight(inventoryTabButton);
        SetPrimaryTabHeight(businessTabButton);
        EnsureWorkspaceTabs();
        WireTabButtons();
        WireWorkspaceTabButtons();
        HideSourceButtons();
        HideManagementPageTitle();
        RefreshVisuals();
        RefreshWorkspaceTabs();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        built = true;
    }

    /// <summary>Restores the primary navigation after cutscene presentation hides canvases.</summary>
    public void RestoreNavigation()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Build();

        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = true;
        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster != null)
            raycaster.enabled = true;

        if (inventoryTabButton != null && !inventoryTabButton.gameObject.activeSelf)
            inventoryTabButton.gameObject.SetActive(true);
        if (businessTabButton != null && businessTabButton.gameObject.activeSelf)
            businessTabButton.gameObject.SetActive(false);

        RefreshVisuals();
    }

    public void ApplyDefaultLayout()
    {
        ApplyDefaultLayout((RectTransform)transform);
    }

    void ApplyDefaultLayout(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = screenOffset;
        rt.sizeDelta = new Vector2(TabWidth * 2f + TabSpacing + 16f, TabHeight + 12f);
    }

    static void ApplyDefaultLayoutGroup(HorizontalLayoutGroup hlg)
    {
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.spacing = TabSpacing;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
    }

    static void SetPrimaryTabHeight(Button button)
    {
        if (button == null) return;
        var elements = button.GetComponents<LayoutElement>();
        if (elements == null || elements.Length == 0)
            elements = new[] { button.gameObject.AddComponent<LayoutElement>() };

        foreach (var element in elements)
        {
            if (element == null) continue;
            element.minHeight = TabHeight;
            element.preferredHeight = TabHeight;
            element.flexibleHeight = 0f;
        }
    }

    void EnsureTabVisuals()
    {
        // Prefer scene-authored tabs; only create missing ones.
        inventoryBg = FindTabImage("InventoryTab");
        managementBg = FindTabImage("ManagementTab");
        businessBg = FindTabImage("BusinessTab");

        if (inventoryBg == null)
            inventoryBg = CreateTab("InventoryTab", "Kitchen");
        if (managementBg == null)
            managementBg = CreateTab("ManagementTab", "Staff");
        if (businessBg == null)
            businessBg = CreateTab("BusinessTab", "Business");

        inventoryTabButton = inventoryBg != null ? inventoryBg.GetComponent<Button>() : null;
        managementTabButton = managementBg != null ? managementBg.GetComponent<Button>() : null;
        businessTabButton = businessBg != null ? businessBg.GetComponent<Button>() : null;
        SetTabLabel(inventoryBg, "Kitchen");
        SetTabLabel(managementBg, "Staff");
        SetTabLabel(businessBg, "Business");
        if (managementBg != null)
            managementBg.gameObject.SetActive(false);
        if (businessBg != null)
            businessBg.gameObject.SetActive(false);

        // Remove accidental duplicates left from older rebuild logic.
        RemoveDuplicateTabs("InventoryTab", inventoryBg != null ? inventoryBg.transform : null);
        RemoveDuplicateTabs("ManagementTab", managementBg != null ? managementBg.transform : null);
        RemoveDuplicateTabs("BusinessTab", businessBg != null ? businessBg.transform : null);
    }

    Image FindTabImage(string objectName)
    {
        var t = transform.Find(objectName);
        return t != null ? t.GetComponent<Image>() : null;
    }

    void RemoveDuplicateTabs(string objectName, Transform keep)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null || child.name != objectName || child == keep)
                continue;
            DestroyObject(child.gameObject);
        }
    }

    void WireTabButtons()
    {
        if (inventoryTabButton != null)
        {
            inventoryTabButton.onClick.RemoveAllListeners();
            inventoryTabButton.onClick.AddListener(ToggleKitchenWorkspace);
            if (inventoryTabButton.targetGraphic == null)
                inventoryTabButton.targetGraphic = inventoryBg;
            GameUITheme.ApplyTabButton(inventoryTabButton, false);
        }

        if (managementTabButton != null)
        {
            managementTabButton.onClick.RemoveAllListeners();
            if (managementTabButton.targetGraphic == null)
                managementTabButton.targetGraphic = managementBg;
            GameUITheme.ApplyTabButton(managementTabButton, false);
        }

        if (businessTabButton != null)
        {
            businessTabButton.onClick.RemoveAllListeners();
            businessTabButton.onClick.AddListener(() => OpenWorkspaceTab(1));
            if (businessTabButton.targetGraphic == null)
                businessTabButton.targetGraphic = businessBg;
            GameUITheme.ApplyTabButton(businessTabButton, false);
        }

        if (inventoryTabButton != null)
            DelayedTooltip.Attach(inventoryTabButton, "Open the Kitchen workspace. Layout changes are available during planning.");
    }

    void EnsureWorkspaceTabs()
    {
        if (workspaceTabsBuilt && workspaceTabsRect != null)
        {
            bool allButtonsFound = true;
            for (int i = 0; i < workspaceTabButtons.Length; i++)
                if (workspaceTabButtons[i] == null) allButtonsFound = false;
            if (allButtonsFound) return;
        }

        if (workspaceTabsRect == null)
        {
            Transform existing = transform.Find(KitchenWorkspaceTabsName);
            if (existing != null)
                workspaceTabsRect = existing as RectTransform;
        }

        if (workspaceTabsRect == null)
        {
            GameObject row = new GameObject(KitchenWorkspaceTabsName, typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(CanvasGroup));
            row.transform.SetParent(transform, false);
            workspaceTabsRect = (RectTransform)row.transform;
        }

        workspaceTabsRect.anchorMin = new Vector2(0f, 1f);
        workspaceTabsRect.anchorMax = new Vector2(1f, 1f);
        workspaceTabsRect.pivot = new Vector2(0f, 1f);
        workspaceTabsRect.anchoredPosition = new Vector2(0f, -WorkspaceTabTopOffset);
        workspaceTabsRect.sizeDelta = new Vector2(0f, TabHeight);
        LayoutElement rowElement = workspaceTabsRect.GetComponent<LayoutElement>();
        if (rowElement == null) rowElement = workspaceTabsRect.gameObject.AddComponent<LayoutElement>();
        rowElement.ignoreLayout = true;

        workspaceTabsLayout = workspaceTabsRect.GetComponent<HorizontalLayoutGroup>();
        workspaceTabsLayout.padding = new RectOffset(8, 8, 0, 0);
        workspaceTabsLayout.spacing = WorkspaceTabSpacing;
        workspaceTabsLayout.childAlignment = TextAnchor.MiddleCenter;
        workspaceTabsLayout.childControlWidth = true;
        workspaceTabsLayout.childControlHeight = true;
        workspaceTabsLayout.childForceExpandWidth = true;
        workspaceTabsLayout.childForceExpandHeight = true;

        for (int i = 0; i < workspaceTabButtons.Length; i++)
        {
            string objectName = WorkspaceTabObjectNames[i];
            Transform child = workspaceTabsRect.Find(objectName);
            Button button = child != null ? child.GetComponent<Button>() : null;
            if (button == null)
                button = CreateWorkspaceTabButton(objectName, WorkspaceTabNames[i], workspaceTabsRect);
            int index = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OpenWorkspaceTab(index));
            workspaceTabButtons[i] = button;
        }

        workspaceTabsBuilt = true;
    }

    Button CreateWorkspaceTabButton(string objectName, string label, Transform parent)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = IdleColor;
        image.raycastTarget = true;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        LayoutElement element = buttonObject.GetComponent<LayoutElement>();
        element.minWidth = 62f;
        element.preferredWidth = 90f;
        element.flexibleWidth = 1f;
        element.minHeight = TabHeight;
        element.preferredHeight = TabHeight;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(3f, 2f);
        labelRect.offsetMax = new Vector2(-3f, -2f);
        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 14f;
        text.fontSizeMin = 11f;
        text.fontSizeMax = 14f;
        text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        GameUITheme.ApplyTabButton(button, false);
        return button;
    }

    void WireWorkspaceTabButtons()
    {
        for (int i = 0; i < workspaceTabButtons.Length; i++)
        {
            Button button = workspaceTabButtons[i];
            if (button == null) continue;
            int index = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OpenWorkspaceTab(index));
        }
    }

    Image CreateTab(string objectName, string label)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(transform, false);

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = TabWidth;
        le.preferredWidth = TabWidth;
        le.flexibleWidth = 1f;
        le.minHeight = TabHeight;
        le.preferredHeight = TabHeight;

        var img = go.GetComponent<Image>();
        img.color = IdleColor;
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8f, 2f);
        textRt.offsetMax = new Vector2(-8f, -2f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.enableAutoSizing = false;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        return img;
    }

    public void OpenBuildPage() => OpenWorkspaceTab(0);
    public void OpenStaffPage() => OpenWorkspaceTab(1);
    public void OpenBusinessPage() => OpenWorkspaceTab(1);

    void ToggleKitchenWorkspace()
    {
        if (AnyPanelOpen())
        {
            CloseKitchenWorkspace();
            return;
        }

        if (IsBuildLocked() && (selectedWorkspaceTab == 0 || selectedWorkspaceTab == 3))
            selectedWorkspaceTab = 1;
        OpenWorkspaceTab(selectedWorkspaceTab);
    }

    public void OpenWorkspaceTab(int index)
    {
        EnsureWorkspaceTabs();
        if (index < 0 || index >= workspaceTabButtons.Length) return;
        if (index == 2 && !MilestoneFeatures.SupplyPlanningUnlocked) return;
        if (OnboardingTutorial.IsFeatureLocked(WorkspaceTabButton(index))) return;

        bool buildTab = index == 0 || index == 3;
        if (buildTab && IsBuildLocked())
        {
            Sfx.Play(SfxId.UiError);
            return;
        }

        selectedWorkspaceTab = index;
        var inv = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (inv != null) cachedInventoryUI = inv;
        if (mgmt != null) cachedManagementScreen = mgmt;

        if (buildTab)
        {
            if (mgmt != null && mgmt.IsOpen) mgmt.Close();
            if (inv != null)
            {
                inv.OpenPanel();
                int targetPanel = FindInventoryPanelIndex(inv,
                    index == 0 ? InventoryUI.StationsPanelName : InventoryUI.CustomizePanelName);
                if (targetPanel >= 0) inv.SelectTab(targetPanel);
            }
            else FindInventoryButton()?.onClick.Invoke();
            HideLegacyWorkspaceTabs();
            return;
        }

        if (mgmt == null)
        {
            FindManagementButton()?.onClick.Invoke();
            return;
        }

        if (index == 1) mgmt.OpenWorkersTab();
        else mgmt.OpenIngredientsTab();
        HideLegacyWorkspaceTabs();
    }

    static int FindInventoryPanelIndex(InventoryUI inventory, string panelName)
    {
        if (inventory == null || inventory.tabPanels == null) return -1;
        for (int i = 0; i < inventory.tabPanels.Length; i++)
            if (inventory.tabPanels[i] != null && inventory.tabPanels[i].name == panelName)
                return i;
        return -1;
    }

    internal void CloseKitchenWorkspace()
    {
        var inv = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (inv != null && inv.IsPanelOpen) inv.TogglePanel();
        if (mgmt != null && mgmt.IsOpen) mgmt.Close();
    }

    static int GetNumberRowIndex()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) return 2;
        if (Input.GetKeyDown(KeyCode.Alpha4)) return 3;
        return -1;
    }

    void CycleWorkspaceTab(int direction)
    {
        if (!AnyPanelOpen()) return;
        EnsureWorkspaceTabs();
        int step = direction >= 0 ? 1 : -1;
        for (int distance = 1; distance <= workspaceTabButtons.Length; distance++)
        {
            int next = (selectedWorkspaceTab + step * distance + workspaceTabButtons.Length * 2)
                % workspaceTabButtons.Length;
            Button candidate = workspaceTabButtons[next];
            if (candidate == null || !candidate.interactable
                || OnboardingTutorial.IsFeatureLocked(candidate))
                continue;
            OpenWorkspaceTab(next);
            return;
        }
    }

    static bool AnyPanelOpen()
    {
        var inv = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        return (inv != null && inv.IsPanelOpen) || (mgmt != null && mgmt.IsOpen);
    }

    void HideSourceButtons()
    {
        if (!sourceButtonsResolved)
        {
            inventorySourceButton = FindInventoryButton();
            managementSourceButton = FindManagementButton();
            sourceButtonsResolved = true;
        }

        if (inventorySourceButton != null && inventorySourceButton.gameObject.activeSelf)
            inventorySourceButton.gameObject.SetActive(false);
        if (managementSourceButton != null && managementSourceButton.gameObject.activeSelf)
            managementSourceButton.gameObject.SetActive(false);
    }

    static void HideManagementPageTitle()
    {
        var panel = GameObject.Find("ManagementPanel");
        if (panel == null) return;
        var title = panel.transform.Find("ContentBox/Title");
        if (title != null)
            title.gameObject.SetActive(false);
    }

    void RefreshVisuals()
    {
        RefreshPageControllerReferences();
        bool buildLocked = IsBuildLocked();
        if (buildLocked && cachedInventoryUI != null && cachedInventoryUI.IsPanelOpen)
            cachedInventoryUI.TogglePanel();
        bool inventoryOpen = cachedInventoryUI != null && cachedInventoryUI.IsPanelOpen;
        bool managementOpen = cachedManagementScreen != null && cachedManagementScreen.IsOpen;

        if (visualStateKnown && inventoryOpen == displayedInventoryOpen
            && managementOpen == displayedManagementOpen
            && buildLocked == displayedBuildLocked)
            return;

        HudTabColors.Apply(inventoryTabButton, inventoryOpen || managementOpen);
        if (inventoryTabButton != null)
        {
            inventoryTabButton.interactable = true;
            SetTabLabel(inventoryBg, "Kitchen");
            var label = inventoryBg != null
                ? inventoryBg.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (label != null)
            {
                label.color = Color.white;
                label.enableAutoSizing = false;
                label.fontSize = 24f;
                label.fontSizeMin = 24f;
                label.fontSizeMax = 24f;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 0.8f;
            }
        }
        displayedInventoryOpen = inventoryOpen;
        displayedManagementOpen = managementOpen;
        displayedBuildLocked = buildLocked;
        visualStateKnown = true;
    }

    void RefreshWorkspaceTabs()
    {
        RefreshPageControllerReferences();
        EnsureWorkspaceTabs();
        HideLegacyWorkspaceTabs();

        bool inventoryOpen = cachedInventoryUI != null && cachedInventoryUI.IsPanelOpen;
        bool managementOpen = cachedManagementScreen != null && cachedManagementScreen.IsOpen;
        bool workspaceOpen = inventoryOpen || managementOpen;
        if (workspaceTabsRect != null && workspaceTabsRect.gameObject.activeSelf != workspaceOpen)
            workspaceTabsRect.gameObject.SetActive(workspaceOpen);

        if (workspaceOpen)
        {
            if (inventoryOpen)
                selectedWorkspaceTab = GetActiveInventoryWorkspaceTab(cachedInventoryUI);
            else if (managementOpen)
                selectedWorkspaceTab = GetActiveManagementWorkspaceTab(cachedManagementScreen);
        }

        bool buildLocked = IsBuildLocked();
        for (int i = 0; i < workspaceTabButtons.Length; i++)
        {
            Button button = workspaceTabButtons[i];
            if (button == null) continue;
            bool phaseAllowsTab = (!buildLocked || (i != 0 && i != 3))
                && (i != 2 || MilestoneFeatures.SupplyPlanningUnlocked);
            button.interactable = phaseAllowsTab && !OnboardingTutorial.IsFeatureLocked(button);
            if (!phaseAllowsTab)
            {
                Image image = button.GetComponent<Image>();
                if (image != null) image.color = GameUITheme.PanelSlate;
            }
            else
                HudTabColors.Apply(button, workspaceOpen && selectedWorkspaceTab == i);
        }
    }

    static int GetActiveInventoryWorkspaceTab(InventoryUI inventory)
    {
        if (inventory == null || inventory.tabPanels == null) return 0;
        for (int i = 0; i < inventory.tabPanels.Length; i++)
        {
            GameObject panel = inventory.tabPanels[i];
            if (panel == null || !panel.activeSelf) continue;
            if (panel.name == InventoryUI.CustomizePanelName || panel.name == InventoryUI.FloorPanelName)
                return 3;
            return 0;
        }
        return 0;
    }

    static int GetActiveManagementWorkspaceTab(ManagementScreenController management)
    {
        if (management == null || management.tabPanels == null) return 1;
        for (int i = 0; i < management.tabPanels.Length; i++)
        {
            GameObject panel = management.tabPanels[i];
            if (panel == null || !panel.activeSelf) continue;
            return panel.GetComponentInChildren<WorkersUI>(true) != null ? 1 : 2;
        }
        return 1;
    }

    void HideLegacyWorkspaceTabs()
    {
        RefreshPageControllerReferences();
        HideLegacyTabBar(cachedInventoryUI != null ? cachedInventoryUI.tabButtons : null);
        HideLegacyTabBar(cachedManagementScreen != null ? cachedManagementScreen.tabButtons : null);
    }

    static void HideLegacyTabBar(Button[] buttons)
    {
        if (buttons == null || buttons.Length == 0 || buttons[0] == null) return;
        Transform tabBar = buttons[0].transform.parent;
        if (tabBar == null) return;
        CanvasGroup group = tabBar.GetComponent<CanvasGroup>();
        if (group == null) group = tabBar.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        if (tabBar.gameObject.activeSelf)
            tabBar.gameObject.SetActive(false);
    }

    static bool IsBuildLocked()
    {
        return GameTimeManager.Instance != null
            && GameTimeManager.Instance.IsShiftInProgress
            && !ShiftPlanningUI.IsPlanning;
    }

    void RefreshPageControllerReferences()
    {
        if (cachedInventoryUI != null && cachedManagementScreen != null)
            return;
        if (Time.unscaledTime < nextPageControllerRefresh)
            return;
        nextPageControllerRefresh = Time.unscaledTime + 0.5f;
        if (cachedInventoryUI == null)
            cachedInventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (cachedManagementScreen == null)
            cachedManagementScreen = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
    }

    static void SetTabLabel(Image tab, string value)
    {
        if (tab == null) return;
        var label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = value;
    }

    static Button FindInventoryButton()
    {
        var ui = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (ui != null && ui.inventoryButton != null)
            return ui.inventoryButton;

        var named = GameObject.Find("InventoryButton");
        return named != null ? named.GetComponent<Button>() : null;
    }

    static Button FindManagementButton()
    {
        var ui = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (ui != null && ui.openButton != null)
            return ui.openButton;

        var named = GameObject.Find("ManagementButton");
        return named != null ? named.GetComponent<Button>() : null;
    }

    static void DestroyObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
