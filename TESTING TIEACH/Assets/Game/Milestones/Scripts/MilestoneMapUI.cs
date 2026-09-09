using UnityEngine;

/// <summary>
/// Compatibility shim. Progression now lives in MissionListUI's Progression tab.
/// Keep this component so old scene/debug references still compile.
/// </summary>
public class MilestoneMapUI : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.J;

    void Start()
    {
        // Hide any leftover standalone map panel from older builds.
        var leftover = GameObject.Find(PanelNameLegacy);
        if (leftover != null)
            leftover.SetActive(false);
    }

    const string PanelNameLegacy = "MilestoneMapPanel";

    void Update()
    {
        if (!Input.GetKeyDown(toggleKey)) return;
        OpenProgressionTab();
    }

    public void SetVisible(bool on)
    {
        if (on) OpenProgressionTab();
        else if (MissionListUI.Instance != null)
            MissionListUI.Instance.ShowTasksTab();
    }

    public void Hide()
    {
        if (MissionListUI.Instance != null)
            MissionListUI.Instance.ShowTasksTab();
    }

    static void OpenProgressionTab()
    {
        if (MissionListUI.Instance != null)
            MissionListUI.Instance.ShowProgressionTab();
    }
}
