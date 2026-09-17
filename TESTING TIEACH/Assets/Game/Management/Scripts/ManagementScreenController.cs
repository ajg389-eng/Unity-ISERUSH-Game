using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Opens/closes Management. Entering management switches GameMode to Manage
/// (like Inventory → Build) so you can click stations to assign workers/outputs.
/// </summary>
public class ManagementScreenController : MonoBehaviour
{
    [Header("Open / Close")]
    public GameModeManager modeManager;
    public KeyCode toggleKey = KeyCode.M;
    public Button openButton;
    public GameObject managementPanel;
    public Button closeButton;

    [Header("Behaviour")]
    [Tooltip("Pause game time while management is open. World clicks still work.")]
    public bool pauseWhileOpen = true;

    [Header("Tabs")]
    public Button[] tabButtons;
    public GameObject[] tabPanels;

    [Header("Sections (optional)")]
    public GameObject statsSection;

    bool isOpen;
    ManagementTabInfoUI tabInfoUI;

    void Start()
    {
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (managementPanel != null)
            managementPanel.SetActive(false);

        if (openButton != null)
            openButton.onClick.AddListener(Toggle);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        WireTabButtons();
        EnsureCustomersTab();
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
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(i == index);
        }
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
                HudTabColors.Apply(tabButtons[i], i == index);
        }

        if (tabInfoUI == null)
            tabInfoUI = ManagementTabInfoUI.EnsureOn(managementPanel != null ? managementPanel.transform : null);
        if (tabInfoUI != null)
            tabInfoUI.SetTab(tabPanels[index]);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void Open()
    {
        if (managementPanel == null) return;

        var inv = FindObjectOfType<InventoryUI>();
        if (inv != null && inv.panel != null && inv.panel.activeSelf)
            inv.panel.SetActive(false);

        managementPanel.SetActive(true);
        Sfx.Play(SfxId.UiOpen);
        // Let clicks on empty overlay pass through to stations (like Build mode)
        var panelImg = managementPanel.GetComponent<Image>();
        if (panelImg != null) panelImg.raycastTarget = false;

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
}
