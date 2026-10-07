using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Opens/closes Management. Entering management switches GameMode to Manage
/// (like Inventory → Build) so you can click stations to assign workers/outputs.
/// </summary>
public class ManagementScreenController : MonoBehaviour
{
    static readonly Color StaffTabActive = new Color(0.52f, 0.34f, 0.78f, 1f);
    static readonly Color StaffTabIdle = new Color(0.25f, 0.19f, 0.36f, 1f);
    static readonly Color DemandTabActive = new Color(0.12f, 0.66f, 0.60f, 1f);
    static readonly Color DemandTabIdle = new Color(0.10f, 0.32f, 0.31f, 1f);
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
    Button workersInnerTab;
    Button customersInnerTab;
    int selectedBusinessPanel = -1;

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

        WireTabButtons();
        EnsureCustomersTab();
        EnsureStaffDemandTabs();
        tabInfoUI = ManagementTabInfoUI.EnsureOn(managementPanel != null ? managementPanel.transform : null);
        if (tabInfoUI != null)
            tabInfoUI.SetButtonPosition(new Vector2(-14f, -66f));
        SelectTab(0);
        EnsureManagementModeController();
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
    }

    /// <summary>
    /// Wire the unused Other tab (or create one) as Customers with visit trend + demand output.
    /// </summary>
    void EnsureCustomersTab()
    {
        if (managementPanel == null) return;
        if (managementPanel.GetComponentInChildren<CustomersUI>(true) != null)
            return;

        Button tabButton = FindNamedButton(managementPanel.transform, "Tab_Other");
        GameObject panel = FindNamedObject(managementPanel.transform, "OtherPanel");

        if (tabButton == null || panel == null)
        {
            if (!TryCreateCustomersTab(out tabButton, out panel))
                return;
        }

        tabButton.gameObject.SetActive(true);
        tabButton.gameObject.name = "Tab_Customers";
        panel.name = "CustomersPanel";
        panel.SetActive(false);

        var label = tabButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = "Customers";

        var title = panel.transform.Find("Title");
        if (title != null)
        {
            var titleTmp = title.GetComponent<TextMeshProUGUI>();
            if (titleTmp != null) titleTmp.text = "Customers";
        }

        if (panel.GetComponent<CustomersUI>() == null)
            panel.AddComponent<CustomersUI>();

        AppendTab(tabButton, panel);
        WireTabButtons();
    }

    bool TryCreateCustomersTab(out Button tabButton, out GameObject panel)
    {
        tabButton = null;
        panel = null;
        if (tabButtons == null || tabButtons.Length == 0 || tabButtons[0] == null) return false;
        if (tabPanels == null || tabPanels.Length == 0 || tabPanels[0] == null) return false;

        Transform tabParent = tabButtons[0].transform.parent;
        Transform panelParent = tabPanels[0].transform.parent;
        if (tabParent == null || panelParent == null) return false;

        var tabGo = Instantiate(tabButtons[0].gameObject, tabParent);
        tabGo.name = "Tab_Customers";
        tabButton = tabGo.GetComponent<Button>();
        var label = tabGo.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = "Customers";

        panel = new GameObject("CustomersPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(panelParent, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = HudTabColors.Panel;
        panel.SetActive(false);
        panel.AddComponent<CustomersUI>();
        return tabButton != null;
    }

    void AppendTab(Button button, GameObject panel)
    {
        if (button == null || panel == null) return;

        int n = tabButtons != null ? tabButtons.Length : 0;
        var buttons = new Button[n + 1];
        var panels = new GameObject[n + 1];
        for (int i = 0; i < n; i++)
        {
            buttons[i] = tabButtons[i];
            panels[i] = i < tabPanels.Length ? tabPanels[i] : null;
        }
        buttons[n] = button;
        panels[n] = panel;
        tabButtons = buttons;
        tabPanels = panels;
    }

    static Button FindNamedButton(Transform root, string objectName)
    {
        var t = FindDeep(root, objectName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    static GameObject FindNamedObject(Transform root, string objectName)
    {
        var t = FindDeep(root, objectName);
        return t != null ? t.gameObject : null;
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
        int staffIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<WorkersUI>(true) != null);
        int demandIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<CustomersUI>(true) != null);
        int selectedIndex = index;
        int outerIndex = index;
        if (staffIndex >= 0 && demandIndex >= 0 && staffIndex != demandIndex)
        {
            if (index == staffIndex || index == demandIndex)
            {
                if (index == demandIndex)
                    selectedBusinessPanel = demandIndex;
                else if (selectedBusinessPanel < 0 || selectedBusinessPanel == staffIndex)
                    selectedBusinessPanel = staffIndex;
                selectedIndex = selectedBusinessPanel;
                outerIndex = staffIndex;
            }

            // The inner tab strip sits above a single shared content area.
            foreach (int pageIndex in new[] { staffIndex, demandIndex })
            {
                var page = (RectTransform)tabPanels[pageIndex].transform;
                page.anchorMin = Vector2.zero;
                page.anchorMax = Vector2.one;
                page.offsetMin = new Vector2(12f, 12f);
                page.offsetMax = new Vector2(-12f, -60f);
            }
        }
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(i == selectedIndex);
        }
        if (workersInnerTab != null && workersInnerTab.transform.parent != null)
            workersInnerTab.transform.parent.gameObject.SetActive(outerIndex == staffIndex);
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
                HudTabColors.Apply(tabButtons[i], i == outerIndex);
        }

        UpdateStaffDemandTabColors();

        if (tabInfoUI == null)
            tabInfoUI = ManagementTabInfoUI.EnsureOn(managementPanel != null ? managementPanel.transform : null);
        if (tabInfoUI != null)
            tabInfoUI.SetTab(tabPanels[selectedIndex]);
    }

    static void ApplyBusinessTabColor(Button button, bool staff, bool selected)
    {
        if (button == null) return;
        Color tint = staff
            ? (selected ? StaffTabActive : StaffTabIdle)
            : (selected ? DemandTabActive : DemandTabIdle);
        var image = button.targetGraphic as Image;
        if (image != null) image.color = tint;
        var colors = button.colors;
        colors.normalColor = tint;
        colors.selectedColor = tint;
        colors.highlightedColor = Color.Lerp(tint, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(tint, Color.black, 0.12f);
        colors.disabledColor = tint;
        button.colors = colors;
    }

    void EnsureStaffDemandTabs()
    {
        if (managementPanel == null || tabPanels == null) return;
        int staffIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<WorkersUI>(true) != null);
        int demandIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<CustomersUI>(true) != null);
        if (staffIndex < 0 || demandIndex < 0 || staffIndex == demandIndex) return;

        Transform parent = tabPanels[staffIndex].transform.parent;
        if (parent == null) return;
        Transform found = parent.Find("StaffDemandTabs");
        GameObject strip;
        if (found == null)
        {
            strip = new GameObject("StaffDemandTabs", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            strip.transform.SetParent(parent, false);
            var rt = (RectTransform)strip.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(-24f, 42f);
            strip.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.14f, 0.96f);
            var layout = strip.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 4, 4);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }
        else strip = found.gameObject;

        workersInnerTab = FindInnerTab(strip.transform, "WorkersTab") ?? CreateInnerTab(strip.transform, "WorkersTab", "Workers");
        customersInnerTab = FindInnerTab(strip.transform, "CustomersTab") ?? CreateInnerTab(strip.transform, "CustomersTab", "Customers");
        strip.transform.SetAsLastSibling();
        workersInnerTab.onClick.RemoveAllListeners();
        workersInnerTab.onClick.AddListener(() => SelectBusinessPanel(false));
        customersInnerTab.onClick.RemoveAllListeners();
        customersInnerTab.onClick.AddListener(() => SelectBusinessPanel(true));
        UpdateStaffDemandTabColors();
    }

    static Button FindInnerTab(Transform parent, string name)
    {
        Transform found = parent != null ? parent.Find(name) : null;
        return found != null ? found.GetComponent<Button>() : null;
    }

    Button CreateInnerTab(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var layout = go.GetComponent<LayoutElement>();
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
        layout.flexibleWidth = 1f;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        var button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        return button;
    }

    void SelectBusinessPanel(bool customers)
    {
        if (tabPanels == null) return;
        int staffIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<WorkersUI>(true) != null);
        int demandIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<CustomersUI>(true) != null);
        if (staffIndex < 0 || demandIndex < 0) return;
        selectedBusinessPanel = customers ? demandIndex : staffIndex;
        SelectTab(staffIndex);
        Sfx.Play(SfxId.UiClick);
    }

    void UpdateStaffDemandTabColors()
    {
        if (workersInnerTab == null || customersInnerTab == null || tabPanels == null) return;
        int staffIndex = System.Array.FindIndex(tabPanels, p => p != null && p.GetComponentInChildren<WorkersUI>(true) != null);
        ApplyBusinessTabColor(workersInnerTab, true, selectedBusinessPanel == staffIndex);
        ApplyBusinessTabColor(customersInnerTab, false, selectedBusinessPanel >= 0 && selectedBusinessPanel != staffIndex);
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

        var inv = FindObjectOfType<InventoryUI>();
        if (inv != null && inv.panel != null && inv.panel.activeSelf)
            inv.panel.SetActive(false);

        managementPanel.SetActive(true);
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
                selectedBusinessPanel = i;
                SelectTab(i);
                return;
            }
        }
    }

    public void OpenCustomersTab()
    {
        if (!isOpen)
            Open();
        if (tabPanels == null) return;
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null && tabPanels[i].GetComponentInChildren<CustomersUI>(true) != null)
            {
                selectedBusinessPanel = i;
                SelectTab(i);
                return;
            }
        }
    }

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
            bool customers = i < tabPanels.Length && tabPanels[i] != null
                && tabPanels[i].GetComponentInChildren<CustomersUI>(true) != null;
            if (label != null)
                label.text = workers ? "Staff and Demand" : customers ? "Demand" : "Menu & Supply";
            if (customers && !workers) tabButtons[i].gameObject.SetActive(false);
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
