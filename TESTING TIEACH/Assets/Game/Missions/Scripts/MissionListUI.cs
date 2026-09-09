using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Multipurpose side panel with Tasks and Progression tabs.
/// Tasks = current milestone missions. Progression = Tutorial + milestones.
/// Place under PlayerUI via Game → Setup Tasks Progression Panel to edit position in the scene.
/// </summary>
public class MissionListUI : MonoBehaviour
{
    public const string PanelObjectName = "SideMenuPanel";

    public enum SideTab
    {
        Tasks = 0,
        Progression = 1
    }

    [Header("References (auto-built if empty)")]
    public Canvas targetCanvas;
    public GameObject panelRoot;
    public TextMeshProUGUI headerText;
    public Button tasksTabButton;
    public Button progressionTabButton;
    public GameObject tasksPage;
    public GameObject progressionPage;
    public Transform tasksListContent;
    public Transform progressionListContent;
    public TextMeshProUGUI progressionDetailText;
    public Button openQuizButton;

    [Header("Layout")]
    [Tooltip("When true, RectTransform position/size are left alone so you can edit them in the scene.")]
    public bool useSceneLayout = true;
    public Vector2 panelAnchor = new Vector2(1f, 0.5f);
    public Vector2 panelPivot = new Vector2(1f, 0.5f);
    public Vector2 panelSize = new Vector2(320f, 480f);
    [Tooltip("Anchored position used only when Use Scene Layout is off.")]
    public Vector2 screenOffset = new Vector2(-24f, 0f);

    [Header("Colors")]
    public Color panelColor = new Color(0.08f, 0.1f, 0.14f, 0.88f);
    public Color tabActiveColor = new Color(0.28f, 0.36f, 0.48f, 1f);
    public Color tabIdleColor = new Color(0.16f, 0.18f, 0.24f, 1f);
    public Color currentMissionColor = new Color(1f, 0.92f, 0.55f, 1f);
    public Color completeMissionColor = new Color(0.55f, 0.85f, 0.6f, 1f);
    public Color pendingMissionColor = new Color(0.78f, 0.82f, 0.88f, 1f);

    MissionProgressManager progress;
    MilestoneProgressManager milestones;
    SideTab activeTab = SideTab.Tasks;
    bool built;

    readonly List<GameObject> taskRowPool = new List<GameObject>();
    readonly List<GameObject> progressionRowPool = new List<GameObject>();

    public static MissionListUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (progress == null)
            progress = MissionProgressManager.Instance ?? FindObjectOfType<MissionProgressManager>();
        if (milestones == null)
            milestones = MilestoneProgressManager.Instance ?? FindObjectOfType<MilestoneProgressManager>();

        EnsureUI();
        SelectTab(SideTab.Tasks, playSound: false);

        if (progress != null)
            progress.OnMissionsChanged += RefreshTasks;
        if (milestones != null)
            milestones.OnMilestonesChanged += RefreshProgression;

        RefreshAll();
        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (progress != null)
            progress.OnMissionsChanged -= RefreshTasks;
        if (milestones != null)
            milestones.OnMilestonesChanged -= RefreshProgression;
    }

    void OnValidate()
    {
        // Don't fight the inspector while you're dragging the RectTransform.
        if (built && panelRoot != null && !useSceneLayout)
            ApplyLayout();
    }

    public void ShowTasksTab() => SelectTab(SideTab.Tasks);
    public void ShowProgressionTab() => SelectTab(SideTab.Progression);

    public void SelectTab(SideTab tab, bool playSound = true)
    {
        EnsureUI();
        activeTab = tab;

        if (tasksPage != null) tasksPage.SetActive(tab == SideTab.Tasks);
        if (progressionPage != null) progressionPage.SetActive(tab == SideTab.Progression);

        SetTabVisual(tasksTabButton, tab == SideTab.Tasks);
        SetTabVisual(progressionTabButton, tab == SideTab.Progression);

        if (headerText != null)
            headerText.text = tab == SideTab.Tasks ? "Tasks" : "Progression";

        if (tab == SideTab.Tasks)
            RefreshTasks();
        else
            RefreshProgression();

        if (playSound)
            Sfx.Play(SfxId.UiClick);
    }

    void RefreshAll()
    {
        RefreshTasks();
        RefreshProgression();
    }

    void RefreshTasks()
    {
        EnsureUI();
        if (tasksListContent == null) return;

        ClearPool(taskRowPool);

        if (progress == null)
        {
            CreateTaskRow("—", "No mission system", pendingMissionColor, false);
            return;
        }

        var missions = progress.GetVisibleMissions();
        var current = progress.GetCurrentMission();

        if (missions.Count == 0)
        {
            string empty = milestones != null && milestones.IsQuizReady
                ? "All tasks done — take the milestone quiz."
                : "No tasks yet";
            CreateTaskRow("—", empty, pendingMissionColor, false);
            return;
        }

        foreach (var mission in missions)
        {
            if (mission == null) continue;
            bool done = progress.IsComplete(mission);
            bool isCurrent = !done && mission == current;
            Color color = done ? completeMissionColor : (isCurrent ? currentMissionColor : pendingMissionColor);
            string prefix = done ? "✓ " : (isCurrent ? "► " : "○ ");
            string title = prefix + mission.title;
            int needed = Mathf.Max(1, mission.requiredCount);
            if (!done && needed > 1)
                title += $" ({progress.GetProgress(mission)}/{needed})";
            CreateTaskRow(title, mission.description, color, isCurrent);
        }
    }

    void RefreshProgression()
    {
        EnsureUI();
        if (progressionListContent == null) return;

        ClearPool(progressionRowPool);

        if (milestones == null)
            milestones = MilestoneProgressManager.Instance;

        var db = milestones != null ? milestones.database : null;
        if (db == null || db.milestones == null || db.milestones.Count == 0)
        {
            if (progressionDetailText != null)
                progressionDetailText.text = "No MilestoneDatabase found.\nRun Game → Setup Milestone Database.";
            if (openQuizButton != null)
                openQuizButton.gameObject.SetActive(false);
            return;
        }

        var active = milestones.GetActiveMilestone();
        if (progressionDetailText != null)
        {
            if (active == null)
                progressionDetailText.text = "All milestones complete.";
            else if (milestones.IsQuizReady)
                progressionDetailText.text = $"Active: {active.displayName}\nMissions done — quiz ready.";
            else
                progressionDetailText.text = $"Active: {active.displayName}\nFinish all tasks, then take the quiz.";
        }

        if (openQuizButton != null)
            openQuizButton.gameObject.SetActive(milestones.IsQuizReady);

        for (int i = 0; i < db.milestones.Count; i++)
        {
            var milestone = db.milestones[i];
            if (milestone == null) continue;
            CreateProgressionRow(milestone, milestones.GetState(milestone), i);
        }
    }

    void CreateTaskRow(string title, string description, Color color, bool emphasize)
    {
        var rowGo = new GameObject("TaskRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        rowGo.transform.SetParent(tasksListContent, false);
        taskRowPool.Add(rowGo);

        var rowLe = rowGo.GetComponent<LayoutElement>();
        rowLe.minHeight = 44f;

        var rowLayout = rowGo.GetComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        AddText(rowGo.transform, "Title", title, emphasize ? 17 : 15, color, emphasize ? FontStyles.Bold : FontStyles.Normal);

        if (!string.IsNullOrEmpty(description))
            AddText(rowGo.transform, "Description", description, 12, new Color(color.r, color.g, color.b, 0.85f), FontStyles.Normal);
    }

    void CreateProgressionRow(MilestoneDefinition milestone, MilestoneProgressManager.MilestoneState state, int index)
    {
        var go = new GameObject(milestone.milestoneId, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        go.transform.SetParent(progressionListContent, false);
        progressionRowPool.Add(go);

        go.GetComponent<LayoutElement>().minHeight = 52f;
        go.GetComponent<Image>().color = ProgressionStateColor(state);

        string prefix = milestone.isTutorial ? "Tutorial" : $"Milestone {Mathf.Max(1, index)}";
        string label = $"{prefix}: {milestone.displayName}\n{ProgressionStateLabel(state)}";
        AddText(go.transform, "Label", label, 13, Color.white, FontStyles.Normal, stretch: true);
    }

    static string ProgressionStateLabel(MilestoneProgressManager.MilestoneState state)
    {
        switch (state)
        {
            case MilestoneProgressManager.MilestoneState.Active: return "In progress";
            case MilestoneProgressManager.MilestoneState.QuizReady: return "Quiz ready";
            case MilestoneProgressManager.MilestoneState.Completed: return "Completed";
            default: return "Locked";
        }
    }

    static Color ProgressionStateColor(MilestoneProgressManager.MilestoneState state)
    {
        switch (state)
        {
            case MilestoneProgressManager.MilestoneState.Active:
                return new Color(0.22f, 0.32f, 0.42f, 1f);
            case MilestoneProgressManager.MilestoneState.QuizReady:
                return new Color(0.35f, 0.4f, 0.22f, 1f);
            case MilestoneProgressManager.MilestoneState.Completed:
                return new Color(0.2f, 0.38f, 0.28f, 1f);
            default:
                return new Color(0.14f, 0.15f, 0.18f, 1f);
        }
    }

    void OpenQuiz()
    {
        var quiz = FindFirstObjectByType<MilestoneQuizUI>();
        if (quiz != null)
            quiz.Show();
    }

    void EnsureUI()
    {
        TryFindScenePanel();

        if (targetCanvas == null)
            targetCanvas = GetComponentInChildren<Canvas>(true);

        if (targetCanvas == null && panelRoot != null)
            targetCanvas = panelRoot.GetComponentInParent<Canvas>();

        if (targetCanvas == null)
        {
            var playerUi = GameObject.Find("PlayerUI");
            if (playerUi != null)
                targetCanvas = playerUi.GetComponent<Canvas>();
        }

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject("MissionListCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (panelRoot == null)
        {
            BuildPanel();
            useSceneLayout = false;
        }
        else if (panelRoot.transform.Find("Tabs") == null)
        {
            bool wasScene = useSceneLayout;
            var parent = panelRoot.transform.parent;
            var oldRt = panelRoot.transform as RectTransform;
            Vector2 oldPos = oldRt != null ? oldRt.anchoredPosition : screenOffset;
            Vector2 oldSize = oldRt != null ? oldRt.sizeDelta : panelSize;
            Vector2 oldAnchor = oldRt != null ? oldRt.anchorMin : panelAnchor;
            Vector2 oldPivot = oldRt != null ? oldRt.pivot : panelPivot;

            Destroy(panelRoot);
            panelRoot = null;
            BuildPanel();

            if (wasScene && panelRoot != null)
            {
                useSceneLayout = true;
                var rt = (RectTransform)panelRoot.transform;
                rt.anchorMin = oldAnchor;
                rt.anchorMax = oldAnchor;
                rt.pivot = oldPivot;
                rt.anchoredPosition = oldPos;
                rt.sizeDelta = oldSize;
            }
            else
            {
                useSceneLayout = false;
            }
        }
        else
        {
            BindExisting();
            useSceneLayout = true;
        }

        built = true;
        ApplyLayout();
        WireTabs();
    }

    void TryFindScenePanel()
    {
        if (panelRoot != null) return;

        // Prefer a panel under PlayerUI so scene edits stick.
        var playerUi = GameObject.Find("PlayerUI");
        if (playerUi != null)
        {
            var t = playerUi.transform.Find(PanelObjectName);
            if (t != null)
            {
                panelRoot = t.gameObject;
                targetCanvas = playerUi.GetComponent<Canvas>();
                useSceneLayout = true;
                return;
            }
        }

        // Or this component is already on the panel itself.
        if (gameObject.name == PanelObjectName || transform.Find("Tabs") != null)
        {
            panelRoot = gameObject;
            useSceneLayout = true;
        }
    }

    /// <summary>Builds a panel under the given canvas for scene placement / editor setup.</summary>
    public static MissionListUI CreateInCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        var existing = canvasTransform.Find(PanelObjectName);
        if (existing != null)
        {
            var ui = existing.GetComponent<MissionListUI>() ?? existing.gameObject.AddComponent<MissionListUI>();
            ui.panelRoot = existing.gameObject;
            ui.targetCanvas = canvasTransform.GetComponent<Canvas>();
            ui.useSceneLayout = true;
            ui.EnsureUIPublic();
            return ui;
        }

        var host = new GameObject("MissionListUI");
        host.transform.SetParent(canvasTransform, false);
        var missionUi = host.AddComponent<MissionListUI>();
        missionUi.targetCanvas = canvasTransform.GetComponent<Canvas>();
        missionUi.useSceneLayout = true;
        missionUi.EnsureUIPublic();

        if (missionUi.panelRoot != null)
        {
            // Move panel under canvas and put the component on the panel for easy selection.
            missionUi.panelRoot.transform.SetParent(canvasTransform, false);
            var onPanel = missionUi.panelRoot.GetComponent<MissionListUI>();
            if (onPanel == null)
            {
                // Keep logic on host; panel is the visual root.
            }
            missionUi.ApplyDefaultSceneLayout();
        }

        return missionUi;
    }

    public void EnsureUIPublic() => EnsureUI();

    public void ApplyDefaultSceneLayout()
    {
        if (panelRoot == null) return;
        var rt = (RectTransform)panelRoot.transform;
        rt.anchorMin = panelAnchor;
        rt.anchorMax = panelAnchor;
        rt.pivot = panelPivot;
        rt.anchoredPosition = screenOffset;
        rt.sizeDelta = panelSize;
    }

    void BuildPanel()
    {
        panelRoot = new GameObject("SideMenuPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panelRoot.transform.SetParent(targetCanvas.transform, false);

        var bg = panelRoot.GetComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = true;

        var layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        headerText = CreateLabel(panelRoot.transform, "Header", "Tasks", 20, FontStyles.Bold);
        headerText.GetComponent<LayoutElement>().preferredHeight = 26f;

        var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabs.transform.SetParent(panelRoot.transform, false);
        tabs.GetComponent<LayoutElement>().minHeight = 34f;
        var tabsH = tabs.GetComponent<HorizontalLayoutGroup>();
        tabsH.spacing = 8f;
        tabsH.childForceExpandWidth = true;
        tabsH.childControlHeight = true;

        tasksTabButton = CreateTabButton(tabs.transform, "TasksTab", "Tasks");
        progressionTabButton = CreateTabButton(tabs.transform, "ProgressionTab", "Progression");

        tasksPage = CreatePage(panelRoot.transform, "TasksPage");
        tasksListContent = CreateScrollList(tasksPage.transform, "TasksList");

        progressionPage = CreatePage(panelRoot.transform, "ProgressionPage");
        progressionDetailText = CreateLabel(progressionPage.transform, "Detail", "", 12, FontStyles.Normal);
        progressionDetailText.color = new Color(0.75f, 0.8f, 0.88f, 1f);
        progressionDetailText.textWrappingMode = TextWrappingModes.Normal;
        progressionDetailText.GetComponent<LayoutElement>().minHeight = 44f;

        progressionListContent = CreateScrollList(progressionPage.transform, "ProgressionList");

        openQuizButton = CreateButton(progressionPage.transform, "OpenQuizButton", "Open Quiz", new Color(0.4f, 0.45f, 0.25f, 1f));
        openQuizButton.onClick.AddListener(OpenQuiz);
        openQuizButton.gameObject.SetActive(false);

        progressionPage.SetActive(false);
    }

    void BindExisting()
    {
        if (headerText == null)
            headerText = panelRoot.transform.Find("Header")?.GetComponent<TextMeshProUGUI>();
        if (tasksTabButton == null)
            tasksTabButton = panelRoot.transform.Find("Tabs/TasksTab")?.GetComponent<Button>();
        if (progressionTabButton == null)
            progressionTabButton = panelRoot.transform.Find("Tabs/ProgressionTab")?.GetComponent<Button>();
        if (tasksPage == null)
            tasksPage = panelRoot.transform.Find("TasksPage")?.gameObject;
        if (progressionPage == null)
            progressionPage = panelRoot.transform.Find("ProgressionPage")?.gameObject;
        if (tasksListContent == null)
            tasksListContent = panelRoot.transform.Find("TasksPage/TasksList/Viewport/Content")
                               ?? panelRoot.transform.Find("TasksPage/TasksList")
                               ?? panelRoot.transform.Find("List");
        if (progressionListContent == null)
            progressionListContent = panelRoot.transform.Find("ProgressionPage/ProgressionList/Viewport/Content")
                                     ?? panelRoot.transform.Find("ProgressionPage/ProgressionList");
        if (progressionDetailText == null)
            progressionDetailText = panelRoot.transform.Find("ProgressionPage/Detail")?.GetComponent<TextMeshProUGUI>();
        if (openQuizButton == null)
            openQuizButton = panelRoot.transform.Find("ProgressionPage/OpenQuizButton")?.GetComponent<Button>();
    }

    void WireTabs()
    {
        if (tasksTabButton != null)
        {
            tasksTabButton.onClick.RemoveAllListeners();
            tasksTabButton.onClick.AddListener(ShowTasksTab);
        }
        if (progressionTabButton != null)
        {
            progressionTabButton.onClick.RemoveAllListeners();
            progressionTabButton.onClick.AddListener(ShowProgressionTab);
        }
        if (openQuizButton != null)
        {
            openQuizButton.onClick.RemoveListener(OpenQuiz);
            openQuizButton.onClick.AddListener(OpenQuiz);
        }
    }

    void ApplyLayout()
    {
        if (panelRoot == null) return;

        if (!useSceneLayout)
        {
            var rt = (RectTransform)panelRoot.transform;
            rt.anchorMin = panelAnchor;
            rt.anchorMax = panelAnchor;
            rt.pivot = panelPivot;
            rt.anchoredPosition = screenOffset;
            rt.sizeDelta = panelSize;
        }

        var bg = panelRoot.GetComponent<Image>();
        if (bg != null)
            bg.color = panelColor;
    }

    void SetTabVisual(Button button, bool active)
    {
        if (button == null) return;
        var img = button.GetComponent<Image>();
        if (img != null)
            img.color = active ? tabActiveColor : tabIdleColor;
    }

    static void ClearPool(List<GameObject> pool)
    {
        foreach (var row in pool)
        {
            if (row != null) Destroy(row);
        }
        pool.Clear();
    }

    static GameObject CreatePage(Transform parent, string name)
    {
        var page = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        page.transform.SetParent(parent, false);
        page.GetComponent<LayoutElement>().flexibleHeight = 1f;
        page.GetComponent<LayoutElement>().minHeight = 280f;
        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return page;
    }

    static Transform CreateScrollList(Transform parent, string name)
    {
        var scrollGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(parent, false);
        var le = scrollGo.GetComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.minHeight = 220f;
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        scrollGo.GetComponent<Image>().raycastTarget = true;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = (RectTransform)viewport.transform;
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.02f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRt;
        scroll.content = contentRt;

        var blocker = scrollGo.AddComponent<ScrollRectWheelBlocker>();
        blocker.scrollRect = scroll;

        return content.transform;
    }

    static Button CreateTabButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = 32f;
        go.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.24f, 1f);
        var btn = go.GetComponent<Button>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Button CreateButton(Transform parent, string name, string label, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = 36f;
        go.GetComponent<Image>().color = color;
        var btn = go.GetComponent<Button>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().minHeight = size + 6f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static void AddText(Transform parent, string name, string text, float size, Color color, FontStyles style, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        if (stretch)
        {
            var tr = (RectTransform)go.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10f, 4f);
            tr.offsetMax = new Vector2(-10f, -4f);
        }
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }
}
