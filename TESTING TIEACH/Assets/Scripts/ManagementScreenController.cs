using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Opens/closes a Management screen in play mode via a key or UI button.
/// Assign the panel and optional open/close buttons in the Inspector.
/// </summary>
public class ManagementScreenController : MonoBehaviour
{
    [Header("Open / Close")]
    [Tooltip("Only visible in Play mode when assigned; leave empty to always allow")]
    public GameModeManager modeManager;
    [Tooltip("Key to toggle the management screen (e.g. M for Management)")]
    public KeyCode toggleKey = KeyCode.M;
    [Tooltip("Optional: button on the HUD that toggles the management screen")]
    public Button openButton;
    [Tooltip("The main overlay panel (full-screen or large panel)")]
    public GameObject managementPanel;
    [Tooltip("Optional: button inside the panel to close it")]
    public Button closeButton;

    [Header("Behaviour")]
    [Tooltip("If true, game time is paused while the management screen is open")]
    public bool pauseWhileOpen = true;

    [Header("Tabs")]
    [Tooltip("Tab buttons (e.g. Store Stats, Settings, Other)")]
    public Button[] tabButtons;
    [Tooltip("Panel for each tab, in same order as tab buttons")]
    public GameObject[] tabPanels;

    [Header("Sections (optional – for custom wiring)")]
    public GameObject statsSection;
    public GameObject settingsSection;
    public GameObject otherSection;

    bool isOpen;

    void Start()
    {
        if (managementPanel != null)
            managementPanel.SetActive(false);

        if (openButton != null)
        {
            openButton.onClick.AddListener(Toggle);
            ApplyModeVisibility();
        }

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        for (int i = 0; i < tabButtons?.Length; i++)
        {
            int index = i;
            if (tabButtons[i] != null)
                tabButtons[i].onClick.AddListener(() => SelectTab(index));
        }

        SelectTab(0);
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
        // Optional: visual feedback on selected tab (e.g. dim unselected)
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
        ApplyModeVisibility();
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    bool IsInPlayMode()
    {
        if (modeManager == null) return true;
        return modeManager.CurrentMode == GameModeManager.Mode.Play;
    }

    void ApplyModeVisibility()
    {
        if (openButton != null)
            openButton.gameObject.SetActive(true);
    }

    public void Open()
    {
        if (managementPanel == null) return;
        managementPanel.SetActive(true);
        if (openButton != null)
            openButton.transform.SetAsLastSibling();
        SelectTab(0);
        isOpen = true;
        if (pauseWhileOpen)
            Time.timeScale = 0f;
    }

    public void Close()
    {
        if (managementPanel == null) return;
        managementPanel.SetActive(false);
        isOpen = false;
        if (pauseWhileOpen)
            Time.timeScale = 1f;
    }

    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

    public bool IsOpen => isOpen;
}
