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

    void EnsureManagementModeController()
    {
        if (FindObjectOfType<ManagementModeController>() != null) return;
        var go = new GameObject("ManagementModeController");
        var mmc = go.AddComponent<ManagementModeController>();
        mmc.modeManager = modeManager;
    }

    public void SelectTab(int index)
    {
        if (tabPanels == null) return;
        index = Mathf.Clamp(index, 0, tabPanels.Length - 1);
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(i == index);
        }
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] != null && tabButtons[i].targetGraphic is Image img)
                    img.color = (i == index) ? new Color(0.45f, 0.45f, 0.55f, 1f) : new Color(0.35f, 0.35f, 0.4f, 1f);
            }
        }
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

    public void Close()
    {
        if (managementPanel == null) return;
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
