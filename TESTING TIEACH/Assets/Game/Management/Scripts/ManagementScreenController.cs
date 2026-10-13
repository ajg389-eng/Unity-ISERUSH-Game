using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Opens/closes Management. Entering management switches GameMode to Manage
/// (like Inventory → Build) so you can click stations to assign workers/outputs.
/// </summary>
public class ManagementScreenController : MonoBehaviour
{
    static readonly KeyCode[] NumberRowTabKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
        KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    [Header("Open / Close")]
    public GameModeManager modeManager;
    public KeyCode toggleKey = KeyCode.E;
    public Button openButton;
    public GameObject managementPanel;
    public Button closeButton;

    [Header("Behaviour")]
    [Tooltip("Pause game time while management is open. World clicks still work.")]
    public bool pauseWhileOpen = true;

    [Header("Tabs")]
    public Button[] tabButtons;
    public GameObject[] tabPanels;

    bool isOpen;
    ManagementTabInfoUI tabInfoUI;
    int selectedTabIndex = -1;

    void Start()
    {
        // Older scenes serialized Alpha2 here. Keep the runtime binding authoritative.
        toggleKey = KeyCode.E;
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        EnsurePanelClickBlocker(managementPanel);
        ApplyManagementBackdrop();
        if (managementPanel != null)
            managementPanel.SetActive(false);

        if (openButton != null)
            openButton.onClick.AddListener(Toggle);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        RemoveLegacyAnalyticsFromManagement();
        EnsureBusinessTabs();
        WireTabButtons();
        tabInfoUI = ManagementTabInfoUI.EnsureOn(managementPanel != null ? managementPanel.transform : null);
        if (tabInfoUI != null)
            tabInfoUI.SetButtonPosition(new Vector2(-14f, -66f));
        SelectTab(0);
        EnsureManagementModeController();
        if (managementPanel != null)
            GameUITheme.ApplyTo(managementPanel.transform);
    }

    void RemoveLegacyAnalyticsFromManagement()
    {
        if (managementPanel == null) return;

        var keepButtons = new List<Button>();
        var keepPanels = new List<GameObject>();
        int count = Mathf.Max(tabButtons != null ? tabButtons.Length : 0, tabPanels != null ? tabPanels.Length : 0);
        for (int i = 0; i < count; i++)
        {
            Button button = tabButtons != null && i < tabButtons.Length ? tabButtons[i] : null;
            GameObject panel = tabPanels != null && i < tabPanels.Length ? tabPanels[i] : null;
            bool analytics = panel != null && panel.GetComponentInChildren<CustomersUI>(true) != null;
            if (analytics)
            {
                if (button != null) button.gameObject.SetActive(false);
                if (panel != null) panel.SetActive(false);
                continue;
            }

            if (button != null || panel != null)
            {
                keepButtons.Add(button);
                keepPanels.Add(panel);
            }
        }

        tabButtons = keepButtons.ToArray();
        tabPanels = keepPanels.ToArray();

        var strip = FindDeep(managementPanel.transform, "StaffDemandTabs");
        if (strip != null)
            Destroy(strip.gameObject);

        // Older scenes can still contain the dormant Customers tab objects even
        // when they are not serialized in tabButtons/tabPanels. Keep them out of
        // the Business screen so the only visible tabs are Workers, Supply, and Menu.
        var legacyButton = FindDeep(managementPanel.transform, "Tab_Other");
        if (legacyButton != null)
            legacyButton.gameObject.SetActive(false);
        var legacyPanel = FindDeep(managementPanel.transform, "OtherPanel");
        if (legacyPanel != null)
            legacyPanel.gameObject.SetActive(false);
    }

    void WireTabButtons()
    {
        if (tabButtons == null) return;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            if (tabButtons[i] == null) continue;
            tabButtons[i].onClick.RemoveAllListeners();
            tabButtons[i].onClick.AddListener(() => SelectTab(index));
        }

        Transform tabBar = null;
        foreach (var button in tabButtons)
        {
            if (button != null)
            {
                tabBar = button.transform.parent;
                break;
            }
        }
        HudTabColors.ConfigureTabGroup(tabBar, tabButtons);

        // HudTabColors gives every management tab a common default footprint.
        // Business has three tabs and must fit the same rendered row as the two
        // primary HUD tabs, so apply its row-specific widths after that pass.
        if (tabButtons.Length == 3
            && tabButtons[0] != null && tabButtons[0].gameObject.name == "Tab_Workers"
            && tabButtons[1] != null && tabButtons[1].gameObject.name == "Tab_Supplies"
            && tabButtons[2] != null && tabButtons[2].gameObject.name == "Tab_Menu")
        {
            ConfigureBusinessTabWidths(tabBar, tabButtons[0], tabButtons[1], tabButtons[2]);
        }
    }

    static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null) return null;
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), objectName);
            if (found != null) return found;
        }
        return null;
    }

    static void SetTabLabel(Button button, string label)
    {
        if (button == null) return;
        var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.text = label;
    }

    int FindButtonIndex(string objectName)
    {
        if (tabButtons == null) return -1;
        for (int i = 0; i < tabButtons.Length; i++)
            if (tabButtons[i] != null && tabButtons[i].gameObject.name == objectName)
                return i;
        return -1;
    }

    void EnsureManagementModeController()
    {
        if (FindObjectOfType<ManagementModeController>() != null) return;
        var go = new GameObject("ManagementModeController");
        var mmc = go.AddComponent<ManagementModeController>();
        mmc.modeManager = modeManager;
    }

    public void SelectTab(int index)
    {
        if (tabPanels == null || tabPanels.Length == 0) return;
        index = Mathf.Clamp(index, 0, tabPanels.Length - 1);
        selectedTabIndex = index;

        GameObject selectedPanel = tabPanels[index];
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(tabPanels[i] == selectedPanel);
        }

        if (selectedPanel != null)
        {
            var page = selectedPanel.transform as RectTransform;
            if (page != null)
            {
                page.anchorMin = Vector2.zero;
                page.anchorMax = Vector2.one;
                page.offsetMin = new Vector2(12f, 12f);
                page.offsetMax = new Vector2(-12f, -12f);
            }

            var ingredients = selectedPanel.GetComponentInChildren<IngredientsOrderUI>(true);
            if (ingredients != null)
            {
                int menuIndex = FindButtonIndex("Tab_Menu");
                ingredients.ConfigureBusinessSection(index == menuIndex);
            }
        }

        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
                HudTabColors.Apply(tabButtons[i], i == index);
        }

        if (tabInfoUI == null)
            tabInfoUI = ManagementTabInfoUI.EnsureOn(managementPanel != null ? managementPanel.transform : null);
        if (tabInfoUI != null)
            tabInfoUI.SetTab(selectedPanel);
    }

    void EnsureBusinessTabs()
    {
        if (managementPanel == null || tabPanels == null) return;
        int staffIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<WorkersUI>(true) != null);
        int foodIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<IngredientsOrderUI>(true) != null);
        if (staffIndex < 0 || foodIndex < 0 || tabButtons == null || foodIndex >= tabButtons.Length) return;

        Button workers = tabButtons[staffIndex];
        Button supplies = tabButtons[foodIndex];
        workers.gameObject.name = "Tab_Workers";
        supplies.gameObject.name = "Tab_Supplies";
        SetTabLabel(workers, "Workers");
        SetTabLabel(supplies, "Supply");

        Transform tabBar = supplies.transform.parent;
        Button menu = tabBar != null ? tabBar.Find("Tab_Menu")?.GetComponent<Button>() : null;
        if (menu == null)
        {
            var clone = Instantiate(supplies.gameObject, tabBar);
            clone.name = "Tab_Menu";
            menu = clone.GetComponent<Button>();
        }
        SetTabLabel(menu, "Menu");

        var buttons = new Button[] { workers, supplies, menu };
        var panels = new GameObject[] { tabPanels[staffIndex], tabPanels[foodIndex], tabPanels[foodIndex] };
        tabButtons = buttons;
        tabPanels = panels;

        var foodUi = panels[1].GetComponentInChildren<IngredientsOrderUI>(true);
        if (foodUi != null)
            foodUi.ConfigureBusinessSection(false);
    }

    static void ConfigureBusinessTabWidths(Transform tabBar, Button workers, Button supplies, Button menu)
    {
        if (tabBar == null) return;

        var layout = tabBar.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            // Keep the existing padding, gap, height, colors, and alignment. Only
            // stop the group from forcing all three buttons to the same width.
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
        }

        MatchBusinessTabBarWidth(tabBar);

        float workersWidth = 184f;
        float otherWidth = 170f;
        if (layout != null)
        {
            var tabRect = tabBar as RectTransform;
            if (tabRect != null)
            {
                float available = tabRect.rect.width - layout.padding.horizontal - layout.spacing * 2f;
                if (available > 0f)
                {
                    workersWidth = available * (184f / 524f);
                    otherWidth = available * (170f / 524f);
                }
            }
        }

        SetBusinessTabWidth(workers, workersWidth);
        SetBusinessTabWidth(supplies, otherWidth);
        SetBusinessTabWidth(menu, otherWidth);
    }

    static void MatchBusinessTabBarWidth(Transform tabBar)
    {
        var tabRect = tabBar as RectTransform;
        var parentRect = tabRect != null ? tabRect.parent as RectTransform : null;
        var mainHud = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
        var mainRect = mainHud != null ? mainHud.transform as RectTransform : null;
        if (tabRect == null || parentRect == null || mainRect == null) return;

        Vector3[] mainCorners = new Vector3[4];
        Vector3[] parentCorners = new Vector3[4];
        mainRect.GetWorldCorners(mainCorners);
        parentRect.GetWorldCorners(parentCorners);
        float targetWorldWidth = Vector3.Distance(mainCorners[0], mainCorners[3]);
        float parentWorldWidth = Vector3.Distance(parentCorners[0], parentCorners[3]);
        if (targetWorldWidth <= 0.01f || parentWorldWidth <= 0.01f) return;

        float parentScale = Mathf.Abs(parentRect.lossyScale.x);
        if (parentScale <= 0.0001f) return;
        var mainLayout = mainHud.GetComponent<HorizontalLayoutGroup>();
        var businessLayout = tabRect.GetComponent<HorizontalLayoutGroup>();
        if (mainLayout != null)
        {
            // Match the colored button row, excluding the primary strip's own
            // outer padding, then add this bar's padding back.
            float mainContentLocal = Mathf.Max(0f, mainRect.rect.width - mainLayout.padding.horizontal);
            targetWorldWidth = mainContentLocal * Mathf.Abs(mainRect.lossyScale.x);
        }

        float businessPadding = businessLayout != null ? businessLayout.padding.horizontal : 0f;
        float targetLocalWidth = targetWorldWidth / parentScale + businessPadding;
        tabRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetLocalWidth);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tabRect);
    }

    static void SetBusinessTabWidth(Button button, float width)
    {
        if (button == null) return;
        var elements = button.GetComponents<LayoutElement>();
        if (elements == null || elements.Length == 0)
            elements = new[] { button.gameObject.AddComponent<LayoutElement>() };

        foreach (var element in elements)
        {
            if (element == null) continue;
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }
    }

    void Update()
    {
        if (UIInputFocusGuard.IsTyping || PauseMenuUI.IsOpen) return;
        bool unifiedNavigation = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include) != null;
        if (!unifiedNavigation && Input.GetKeyDown(toggleKey))
        {
            Toggle();
            return;
        }

        if (!unifiedNavigation && isOpen)
            HandleNumberRowTabShortcut();
    }

    void HandleNumberRowTabShortcut()
    {
        // 5 switches directly to the customer order board.
        if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return;
        int requestedPosition = -1;
        for (int i = 0; i < NumberRowTabKeys.Length; i++)
        {
            if (!Input.GetKeyDown(NumberRowTabKeys[i])) continue;
            requestedPosition = i;
            break;
        }
        if (requestedPosition < 0) return;

        int count = tabPanels != null && tabButtons != null
            ? Mathf.Min(Mathf.Min(tabPanels.Length, tabButtons.Length), NumberRowTabKeys.Length)
            : 0;
        if (requestedPosition >= count) return;

        int[] visualOrder = new int[count];
        for (int i = 0; i < count; i++)
            visualOrder[i] = i;
        System.Array.Sort(visualOrder, (a, b) => GetTabSiblingIndex(tabButtons[a])
            .CompareTo(GetTabSiblingIndex(tabButtons[b])));

        SelectTab(visualOrder[requestedPosition]);
        Sfx.Play(SfxId.UiClick);
    }

    static int GetTabSiblingIndex(Button button) => button != null
        ? button.transform.GetSiblingIndex()
        : int.MaxValue;

    public void Open()
    {
        if (managementPanel == null) return;

        // Prepare hidden so Unity never presents the previous tab or a partially
        // rebuilt list for a frame while the screen opens.
        bool wasActive = managementPanel.activeSelf;
        if (!wasActive)
            managementPanel.SetActive(false);

        var inv = FindObjectOfType<InventoryUI>();
        if (inv != null && inv.panel != null && inv.panel.activeSelf)
            inv.panel.SetActive(false);

        Sfx.Play(SfxId.UiOpen);
        EnsurePanelClickBlocker(managementPanel);
        ApplyManagementBackdrop();
        SetUnifiedPrimaryPage(true);

        if (openButton != null)
            openButton.gameObject.SetActive(false);
        SelectTab(0);
        isOpen = true;

        if (modeManager != null)
            modeManager.SetMode(GameModeManager.Mode.Manage);

        GameUITheme.ApplyTo(managementPanel.transform);
        Canvas.ForceUpdateCanvases();
        managementPanel.SetActive(true);

        if (pauseWhileOpen)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.RequestExternalPause(GameTimeManager.PauseManagement);
            else
                Time.timeScale = 0f;
        }
    }

    void ApplyManagementBackdrop()
    {
        if (managementPanel == null) return;

        var rootImage = managementPanel.GetComponent<Image>();
        if (rootImage != null)
        {
            // ContentBox supplies the visible surface. Leaving the root clear
            // avoids doubling the translucent HUD color where both overlap.
            rootImage.color = Color.clear;
            rootImage.raycastTarget = true;
        }

        // ContentBox is the visible fill in the scene-authored hierarchy. Match
        // it to the connected top HUD instead of leaving a lighter slate block.
        Transform contentBox = managementPanel.transform.Find("ContentBox");
        var contentImage = contentBox != null ? contentBox.GetComponent<Image>() : null;
        if (contentImage != null)
            contentImage.color = HudTabColors.Strip;
    }

    /// <summary>
    /// Hide the management panel but stay in Manage mode so the player can click stations in the world.
    /// </summary>
    public void SuspendForWorldCapture()
    {
        if (managementPanel != null)
            managementPanel.SetActive(false);
        isOpen = false;
        if (openButton != null)
            openButton.gameObject.SetActive(false);

        if (modeManager != null)
            modeManager.SetMode(GameModeManager.Mode.Manage);

        if (pauseWhileOpen)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.ReleaseExternalPause(GameTimeManager.PauseManagement);
            else
                Time.timeScale = 1f;
        }
    }

    /// <summary>Re-open management after world flow capture, preferably on the Workers tab.</summary>
    public void ResumeAfterWorldCapture()
    {
        Open();
        OpenWorkersTab();
    }

    public void OpenWorkersTab()
    {
        if (!isOpen)
            Open();
        if (tabPanels == null) return;
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null && tabPanels[i].GetComponentInChildren<WorkersUI>(true) != null)
            {
                SelectTab(i);
                return;
            }
        }
    }

    public void OpenAnalyticsTab()
    {
        // Analytics is a modal overlay. Leave the current Business page open
        // underneath it so closing Analytics returns to the same tab instead
        // of collapsing the entire Build/Business navigation state.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
            AnalyticsPopupUI.Open(canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform);
    }

    // Compatibility alias for existing tutorial and shortcut callers.
    public void OpenCustomersTab() => OpenAnalyticsTab();

    public void SetUnifiedPrimaryPage(bool business)
    {
        if (tabButtons == null || tabButtons.Length == 0) return;

        Transform tabBar = null;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            if (tabBar == null) tabBar = tabButtons[i].transform.parent;

            bool workers = i < tabPanels.Length && tabPanels[i] != null
                && tabPanels[i].GetComponentInChildren<WorkersUI>(true) != null;
            tabButtons[i].gameObject.SetActive(true);

            var label = tabButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = workers ? "Workers"
                    : tabButtons[i].gameObject.name == "Tab_Menu" ? "Menu" : "Supply";
            tabButtons[i].gameObject.SetActive(true);
        }

        if (tabBar != null)
            tabBar.gameObject.SetActive(true);
    }

    public void OpenIngredientsTab()
    {
        if (!isOpen)
            Open();
        if (tabPanels == null) return;
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null && tabPanels[i].GetComponentInChildren<IngredientsOrderUI>(true) != null)
            {
                SelectTab(i);
                return;
            }
        }
    }

    public void CycleBusinessTab(int direction)
    {
        if (tabButtons == null || tabButtons.Length == 0) return;
        int current = selectedTabIndex >= 0 && selectedTabIndex < tabButtons.Length ? selectedTabIndex : 0;
        int next = (current + (direction >= 0 ? 1 : -1) + tabButtons.Length) % tabButtons.Length;
        if (tabButtons[next] != null)
            tabButtons[next].onClick.Invoke();
    }

    public void Close()
    {
        if (managementPanel == null) return;
        if (tabInfoUI != null) tabInfoUI.Close();
        managementPanel.SetActive(false);
        Sfx.Play(SfxId.UiClose);
        isOpen = false;

        if (modeManager != null && modeManager.CurrentMode == GameModeManager.Mode.Manage)
            modeManager.SetMode(GameModeManager.Mode.Play);

        if (pauseWhileOpen)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.ReleaseExternalPause(GameTimeManager.PauseManagement);
            else
                Time.timeScale = 1f;
        }
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public bool IsOpen => isOpen;

    /// <summary>
    /// The panel's full visible rect must receive pointer events, even in empty
    /// space between controls, so world station selection cannot leak through it.
    /// </summary>
    static void EnsurePanelClickBlocker(GameObject panel)
    {
        if (panel == null) return;
        Image image = panel.GetComponent<Image>();
        if (image == null)
        {
            image = panel.AddComponent<Image>();
            image.color = Color.clear;
        }
        image.raycastTarget = true;
    }
}
