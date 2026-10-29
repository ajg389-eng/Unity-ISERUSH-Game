using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Objectives panel with separate daily tasks, milestone tasks, and milestone status sections.
/// Place under PlayerUI via Game → Setup Tasks Progression Panel to edit position in the scene.
/// </summary>
public class MissionListUI : MonoBehaviour
{
    public const string PanelObjectName = "SideMenuPanel";
    const string TopTabsObjectName = "TasksProgressionTabs";
    const float TopTabsWidth = 210f;
    const float TopTabsHeight = 60f;

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
    public GameObject milestoneTasksPage;
    public GameObject progressionPage;
    public Transform tasksListContent;
    public Transform milestoneTasksListContent;
    public Transform progressionListContent;
    public TextMeshProUGUI progressionDetailText;
    public Button researchButton;
    [HideInInspector]
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
    public Color recipeTaskColor = new Color(1f, 0.82f, 0.32f, 1f);
    public Color workflowTaskColor = new Color(0.45f, 0.72f, 0.96f, 1f);
    public Color serviceTaskColor = new Color(0.44f, 0.82f, 0.58f, 1f);
    public Color financeTaskColor = new Color(0.35f, 0.78f, 0.76f, 1f);
    public Color layoutTaskColor = new Color(0.72f, 0.58f, 0.94f, 1f);
    public Color generalTaskColor = new Color(0.78f, 0.82f, 0.88f, 1f);

    enum TaskType { Recipe, Workflow, Service, Finance, Layout, General }

    MissionProgressManager progress;
    MilestoneProgressManager milestones;
    StoreStatisticsManager statistics;
    SideTab activeTab = SideTab.Tasks;
    bool built;
    ManagementTabInfoUI tabInfoUI;
    GameObject topTabsRoot;
    Button topTasksButton;
    Button topProgressionButton;
    Button topObjectivesButton;
    Button topResearchButton;
    bool panelOpen;
    GameObject objectivesColumnsRoot;
    GameObject tasksScrollRoot;
    GameObject progressionScrollRoot;
    bool combinedLayoutBuilt;
    bool tasksDirty = true;
    bool dailyGoalsDirty = true;
    bool progressionDirty = true;
    bool panelHeightDirty = true;
    bool hasDailyGoalSnapshot;
    int lastDailyOrders;
    int lastDailyRevenue;
    float lastDailyWait;
    float lastCanvasHeight;
    float nextMissingSourceSearchTime;
    bool topTabVisualInitialized;
    bool lastTopTabOpen;

    readonly List<GameObject> taskRowPool = new List<GameObject>();
    readonly List<GameObject> milestoneTaskRowPool = new List<GameObject>();
    readonly List<GameObject> progressionRowPool = new List<GameObject>();

    public static MissionListUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ResolveDataSources();

        EnsureUI();
        EnsureTopTabs();
        SelectTab(SideTab.Tasks, playSound: false);

        RefreshAll();
        if (panelRoot != null)
            panelRoot.SetActive(false);
        panelOpen = false;
        RefreshTopTabs();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (progress != null)
            progress.OnMissionsChanged -= RefreshTasks;
        if (statistics != null)
            statistics.OnStatisticsChanged -= OnStatisticsChanged;
        if (milestones != null)
            milestones.OnMilestonesChanged -= RefreshProgression;
    }

    void OnValidate()
    {
        // Don't fight the inspector while you're dragging the RectTransform.
        if (built && panelRoot != null && !useSceneLayout)
            ApplyLayout();
    }

    void LateUpdate()
    {
        if (ResolveDataSources())
            RefreshAll();

        RefreshTopTabs();

        FlushPendingRefreshes();
    }

    bool ResolveDataSources()
    {
        bool searchMissingSources = !Application.isPlaying
            || Time.unscaledTime >= nextMissingSourceSearchTime;
        if (searchMissingSources)
            nextMissingSourceSearchTime = Time.unscaledTime + 0.5f;

        // Use Unity's overloaded null check here: a destroyed singleton wrapper can
        // still be a non-null CLR reference after returning to the game scene.
        MissionProgressManager nextProgress = MissionProgressManager.Instance;
        if (nextProgress == null)
            nextProgress = progress != null ? progress
                : searchMissingSources ? FindObjectOfType<MissionProgressManager>() : null;

        MilestoneProgressManager nextMilestones = MilestoneProgressManager.Instance;
        if (nextMilestones == null)
            nextMilestones = milestones != null ? milestones
                : searchMissingSources ? FindObjectOfType<MilestoneProgressManager>() : null;

        StoreStatisticsManager nextStatistics = StoreStatisticsManager.Instance;
        if (nextStatistics == null)
            nextStatistics = statistics != null ? statistics
                : searchMissingSources ? FindObjectOfType<StoreStatisticsManager>() : null;
        bool changed = false;

        if (progress != nextProgress)
        {
            if (progress != null) progress.OnMissionsChanged -= RefreshTasks;
            progress = nextProgress;
            if (progress != null) progress.OnMissionsChanged += RefreshTasks;
            changed = true;
        }

        if (milestones != nextMilestones)
        {
            if (milestones != null) milestones.OnMilestonesChanged -= RefreshProgression;
            milestones = nextMilestones;
            if (milestones != null) milestones.OnMilestonesChanged += RefreshProgression;
            changed = true;
        }

        if (statistics != nextStatistics)
        {
            if (statistics != null) statistics.OnStatisticsChanged -= OnStatisticsChanged;
            statistics = nextStatistics;
            if (statistics != null) statistics.OnStatisticsChanged += OnStatisticsChanged;
            hasDailyGoalSnapshot = false;
            OnStatisticsChanged();
            changed = true;
        }

        return changed;
    }

    void Update()
    {
        if (!IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape) && !UIInputFocusGuard.IsTyping)
        {
            SetVisible(false);
            Sfx.Play(SfxId.UiClose);
            return;
        }

    }

    public static bool IsPointerOverObjectivesScrollArea
    {
        get
        {
            MissionListUI ui = Instance;
            return ui != null && ui.IsOpen
                && (ui.GetScrollRectAtPointer(ui.tasksListContent) != null
                    || ui.GetScrollRectAtPointer(ui.milestoneTasksListContent) != null
                    || ui.GetScrollRectAtPointer(ui.progressionListContent) != null);
        }
    }

    ScrollRect GetScrollRectAtPointer(Transform content)
    {
        if (content == null) return null;
        ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
        RectTransform rect = scroll != null ? scroll.transform as RectTransform : null;
        if (rect == null) return null;

        Canvas canvas = scroll.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eventCamera)
            ? scroll
            : null;
    }

    public bool IsOpen => panelOpen && panelRoot != null && panelRoot.activeSelf;

    public void ShowTasksTab()
    {
        SetVisible(true);
        SelectTab(SideTab.Tasks);
    }

    public void ShowProgressionTab()
    {
        SetVisible(true);
        SelectTab(SideTab.Progression);
    }

    public void ToggleTasksTab()
    {
        ToggleTopTab(SideTab.Tasks);
    }

    public void ToggleProgressionTab()
    {
        ToggleTopTab(SideTab.Progression);
    }

    /// <summary>Toggle the single HUD entry that contains both task and progression views.</summary>
    public void ToggleObjectives()
    {
        if (IsOpen)
        {
            SetVisible(false);
            Sfx.Play(SfxId.UiClose);
            return;
        }

        SetVisible(true);
        SelectTab(activeTab);
    }

    public void SetVisible(bool visible)
    {
        EnsureUI();
        EnsureTopTabs();
        panelOpen = visible;
        if (panelRoot != null)
            panelRoot.SetActive(visible);
        if (visible && ResolveDataSources())
            RefreshAll();
        if (!visible && tabInfoUI != null)
            tabInfoUI.Close();
        RefreshTopTabs();
    }

    void ToggleTopTab(SideTab tab)
    {
        if (IsOpen && activeTab == tab)
        {
            SetVisible(false);
            Sfx.Play(SfxId.UiClose);
            return;
        }

        SetVisible(true);
        SelectTab(tab);
    }

    public void SelectTab(SideTab tab, bool playSound = true)
    {
        EnsureUI();

        // The objectives view is now a single page. Keep the enum and public
        // methods for compatibility with older callers that still open a
        // specific tab.
        activeTab = SideTab.Tasks;

        if (tasksPage != null) tasksPage.SetActive(true);
        if (milestoneTasksPage != null) milestoneTasksPage.SetActive(true);
        if (progressionPage != null) progressionPage.SetActive(true);

        SetTabVisual(tasksTabButton, false);
        SetTabVisual(progressionTabButton, false);

        if (tabInfoUI == null && panelRoot != null)
            tabInfoUI = ManagementTabInfoUI.EnsureOn(panelRoot.transform);
        if (tabInfoUI != null)
            tabInfoUI.SetTab(tasksPage);

        if (headerText != null)
            headerText.text = "Objectives";

        if (playSound)
            Sfx.Play(SfxId.UiClick);

        RefreshTopTabs();
    }

    void RefreshAll()
    {
        RefreshTasks();
        dailyGoalsDirty = true;
        RefreshProgression();
    }

    void RefreshTasks()
    {
        tasksDirty = true;
    }

    void OnStatisticsChanged()
    {
        int orders = statistics != null ? statistics.OrdersCompletedToday : 0;
        int revenue = statistics != null ? statistics.RevenueToday : 0;
        float wait = statistics != null ? statistics.AverageWaitTimeTodaySeconds : 0f;
        // Visits, walkouts, waste and rolling throughput do not change these
        // goals. In particular, they must not rebuild a hidden objectives panel.
        if (hasDailyGoalSnapshot && orders == lastDailyOrders
            && revenue == lastDailyRevenue && wait == lastDailyWait)
            return;

        hasDailyGoalSnapshot = true;
        lastDailyOrders = orders;
        lastDailyRevenue = revenue;
        lastDailyWait = wait;
        dailyGoalsDirty = true;
    }

    void FlushPendingRefreshes()
    {
        // Simulation events only mark data dirty. Coalesce them until the end
        // of the frame, and leave the hierarchy untouched while it is closed.
        if (!IsOpen) return;
        EnsureUI();

        if (dailyGoalsDirty)
        {
            dailyGoalsDirty = false;
            ClearPool(taskRowPool);
            CreateDailyGoalRows();
            UpdatePanelHeight();
        }
        if (tasksDirty)
        {
            tasksDirty = false;
            RebuildTasks();
        }
        if (progressionDirty)
        {
            progressionDirty = false;
            RebuildProgression();
        }

        if (targetCanvas != null && targetCanvas.pixelRect.height != lastCanvasHeight)
            panelHeightDirty = true;
        if (panelHeightDirty)
        {
            panelHeightDirty = false;
            ApplyPanelHeight();
        }
    }

    void RebuildTasks()
    {
        if (tasksListContent == null || milestoneTasksListContent == null) return;

        ClearPool(milestoneTaskRowPool);

        if (progress == null)
        {
            CreateTaskRow("—", "No mission system", generalTaskColor, false, false, false,
                milestoneTasksListContent, milestoneTaskRowPool);
            UpdatePanelHeight();
            return;
        }

        var missions = progress.GetVisibleMissions();
        var current = progress.GetCurrentMission();

        if (missions.Count == 0)
        {
            string empty = milestones != null && milestones.CompletedMilestoneCount > 0
                && milestones.GetActiveMilestone() == null
                ? "All milestones complete."
                : "No tasks yet";
            CreateTaskRow("—", empty, generalTaskColor, false, false, false,
                milestoneTasksListContent, milestoneTaskRowPool);
            UpdatePanelHeight();
            return;
        }

        foreach (var mission in missions)
        {
            if (mission == null) continue;
            bool done = progress.IsComplete(mission);
            bool isCurrent = !done && mission == current;
            Color color = GetTaskColor(GetMissionTaskType(mission));
            int needed = Mathf.Max(1, mission.requiredCount);
            string title = mission.title;
            if (!done && needed > 1)
                title += $" ({progress.GetProgress(mission)}/{needed})";
            CreateTaskRow(title, mission.description, color, isCurrent, done, true,
                milestoneTasksListContent, milestoneTaskRowPool);
        }
        UpdatePanelHeight();
    }

    void CreateDailyGoalRows()
    {
        int revenueTarget = StoreStatisticsManager.DailyRevenueTaskTarget;
        float waitTarget = StoreStatisticsManager.DailyWaitTaskTargetSeconds;
        int minimumOrders = StoreStatisticsManager.MinimumOrdersForDailyWaitTask;
        int orders = statistics != null ? statistics.OrdersCompletedToday : 0;
        int revenue = statistics != null ? statistics.RevenueToday : 0;
        float wait = statistics != null ? statistics.AverageWaitTimeTodaySeconds : 0f;

        CreateTaskRow("Earn $" + revenueTarget,
            "$" + Mathf.Min(revenue, revenueTarget) + "/$" + revenueTarget + " revenue",
            GetTaskColor(TaskType.Finance), false, revenue >= revenueTarget);
        bool waitComplete = (statistics != null && statistics.DailyWaitTaskAwarded)
            || (orders >= minimumOrders && wait <= waitTarget);
        CreateTaskRow("Average wait of " + waitTarget.ToString("0") + "s or less",
            orders > 0
                ? wait.ToString("0") + "s average — " + Mathf.Min(orders, minimumOrders) + "/" + minimumOrders + " orders"
                : "Complete " + minimumOrders + " orders to measure average wait",
            GetTaskColor(TaskType.Service), false, waitComplete);
    }

    void RefreshProgression()
    {
        progressionDirty = true;
        tasksDirty = true;
    }

    void RebuildProgression()
    {
        if (progressionListContent == null) return;

        ClearPool(progressionRowPool);

        var db = milestones != null ? milestones.database : null;
        if (db == null || db.milestones == null || db.milestones.Count == 0)
        {
            if (openQuizButton != null)
                openQuizButton.gameObject.SetActive(false);
            UpdatePanelHeight();
            return;
        }

        for (int i = 0; i < db.milestones.Count; i++)
        {
            var milestone = db.milestones[i];
            if (milestone == null) continue;
            CreateProgressionRow(milestone, milestones.GetState(milestone), i);
        }
        UpdatePanelHeight();
    }

    void CreateTaskRow(string title, string description, Color color, bool emphasize,
        bool complete, bool showCheckbox = true, Transform rowParent = null,
        List<GameObject> rowPool = null)
    {
        var rowGo = new GameObject("TaskRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        rowGo.transform.SetParent(rowParent != null ? rowParent : tasksListContent, false);
        (rowPool ?? taskRowPool).Add(rowGo);

        var rowLe = rowGo.GetComponent<LayoutElement>();
        rowLe.minHeight = 44f;

        var rowLayout = rowGo.GetComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var titleRow = new GameObject("TitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        titleRow.transform.SetParent(rowGo.transform, false);
        titleRow.GetComponent<LayoutElement>().minHeight = 20f;
        var titleLayout = titleRow.GetComponent<HorizontalLayoutGroup>();
        titleLayout.spacing = 6f;
        titleLayout.childAlignment = TextAnchor.UpperLeft;
        titleLayout.childControlWidth = true;
        titleLayout.childControlHeight = true;
        titleLayout.childForceExpandWidth = false;
        titleLayout.childForceExpandHeight = false;

        if (showCheckbox)
            CreateTaskCheckbox(titleRow.transform, color, complete);

        var textColumn = new GameObject("Text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(titleRow.transform, false);
        textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 2f;
        textLayout.childAlignment = TextAnchor.UpperLeft;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;

        // Keep task titles the same size as progress changes; bold marks the current objective.
        AddText(textColumn.transform, "Title", title, 16, color, emphasize ? FontStyles.Bold : FontStyles.Normal);
        if (!string.IsNullOrEmpty(description))
            AddText(textColumn.transform, "Description", description, 14, new Color(color.r, color.g, color.b, 0.85f), FontStyles.Normal);
    }

    static void CreateTaskCheckbox(Transform parent, Color color, bool complete)
    {
        var box = new GameObject("TaskCheckbox", typeof(RectTransform), typeof(LayoutElement));
        box.transform.SetParent(parent, false);
        var rect = (RectTransform)box.transform;
        rect.sizeDelta = new Vector2(16f, 16f);
        var layout = box.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 16f;
        layout.minHeight = layout.preferredHeight = 16f;

        CreateTaskCheckboxPart(box.transform, "Top", color,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -2f), Vector2.zero);
        CreateTaskCheckboxPart(box.transform, "Bottom", color,
            Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
        CreateTaskCheckboxPart(box.transform, "Left", color,
            Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 2f), new Vector2(2f, -2f));
        CreateTaskCheckboxPart(box.transform, "Right", color,
            new Vector2(1f, 0f), Vector2.one, new Vector2(-2f, 2f), new Vector2(0f, -2f));
        CreateTaskCheckboxPart(box.transform, "Fill", complete ? color : Color.clear,
            Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));

        if (complete)
        {
            // The default font has no checkmark glyph. Draw the two strokes
            // directly so completing a task cannot trigger font warning spam.
            CreateTaskCheckStroke(box.transform, new Vector2(-3f, -1f), 5f, -45f);
            CreateTaskCheckStroke(box.transform, new Vector2(1f, 0f), 9f, 45f);
        }
    }

    static void CreateTaskCheckStroke(Transform parent, Vector2 position, float width, float angle)
    {
        var stroke = new GameObject("Check", typeof(RectTransform), typeof(Image));
        stroke.transform.SetParent(parent, false);
        var rect = (RectTransform)stroke.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 2f);
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        var image = stroke.GetComponent<Image>();
        image.color = new Color(0.06f, 0.08f, 0.1f, 1f);
        image.raycastTarget = false;
    }

    static void CreateTaskCheckboxPart(Transform parent, string objectName, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var part = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);
        var rect = (RectTransform)part.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        var image = part.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    TaskType GetMissionTaskType(MissionDefinition mission)
    {
        string identity = ((mission != null ? mission.missionId : "") + " "
            + (mission != null ? mission.title : "") + " "
            + (mission != null ? mission.description : "") + " "
            + (mission != null ? mission.completionEventId : "")).ToLowerInvariant();
        if (identity.Contains("recipe")) return TaskType.Recipe;
        if (identity.Contains("flow") || identity.Contains("output_link")) return TaskType.Workflow;
        if (identity.Contains("revenue") || identity.Contains("earn") || identity.Contains("money")) return TaskType.Finance;
        if (identity.Contains("reposition") || identity.Contains("station") || identity.Contains("layout")) return TaskType.Layout;
        if (identity.Contains("serve") || identity.Contains("customer") || identity.Contains("queue") || identity.Contains("wait"))
            return TaskType.Service;
        return TaskType.General;
    }

    Color GetTaskColor(TaskType type)
    {
        switch (type)
        {
            case TaskType.Recipe: return recipeTaskColor;
            case TaskType.Workflow: return workflowTaskColor;
            case TaskType.Service: return serviceTaskColor;
            case TaskType.Finance: return financeTaskColor;
            case TaskType.Layout: return layoutTaskColor;
            default: return generalTaskColor;
        }
    }

    void CreateProgressionRow(MilestoneDefinition milestone, MilestoneProgressManager.MilestoneState state, int index)
    {
        var go = new GameObject(milestone.milestoneId, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        go.transform.SetParent(progressionListContent, false);
        progressionRowPool.Add(go);

        go.GetComponent<LayoutElement>().minHeight = 52f;
        var rowImage = go.GetComponent<Image>();
        rowImage.color = ProgressionStateColor(state);
        rowImage.raycastTarget = false;

        string milestoneLabel = milestones != null
            ? milestones.GetMilestoneDisplayLabel(milestone)
            : milestone.isTutorial ? "Tutorial" : $"Milestone {Mathf.Max(1, index)}";
        string label = $"{milestoneLabel}\n{ProgressionStateLabel(state)}";
        AddText(go.transform, "Label", label, 13, Color.white, FontStyles.Normal, stretch: true);
    }

    static string ProgressionStateLabel(MilestoneProgressManager.MilestoneState state)
    {
        switch (state)
        {
            case MilestoneProgressManager.MilestoneState.Active: return "In progress";
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
            case MilestoneProgressManager.MilestoneState.Completed:
                return new Color(0.2f, 0.38f, 0.28f, 1f);
            default:
                return new Color(0.14f, 0.15f, 0.18f, 1f);
        }
    }

    void EnsureUI()
    {
        if (built && panelRoot != null && combinedLayoutBuilt
            && tasksListContent != null && milestoneTasksListContent != null
            && progressionListContent != null)
            return;

        bool applyTheme = !built;
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
            scaler.matchWidthOrHeight = 0f;
        }

        if (panelRoot == null)
        {
            BuildPanel();
            useSceneLayout = false;
        }
        // A scene-authored panel may still contain the old tab layout, or a
        // previous play session may have left an empty runtime hierarchy when
        // domain/scene reload is disabled. Rebuild only when our combined
        // layout marker is absent. Once it exists, refreshes reuse the panel.
        else if (panelRoot.transform.Find("ObjectivesSplitLayout") == null)
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
            combinedLayoutBuilt = false;
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

        EnsureCombinedLayout();
        built = true;
        ApplyLayout();
        WireTabs();
        EnsureTopTabs();
        if (applyTheme && panelRoot != null)
            GameUITheme.ApplyTo(panelRoot.transform);
        MakePanelClickThrough();
        RefreshAll();
    }

    void MakePanelClickThrough()
    {
        if (panelRoot == null) return;
        foreach (Graphic graphic in panelRoot.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.GetComponentInParent<Button>() != null) continue;
            // Keep the scroll viewport interactive so users can drag the full
            // objectives list even though its text and decorative graphics
            // remain click-through.
            if (graphic.GetComponent<Mask>() != null
                && graphic.GetComponentInParent<ScrollRect>() != null)
            {
                graphic.raycastTarget = true;
                continue;
            }
            graphic.raycastTarget = false;
        }
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
        ApplyTopHudBackground(bg);
        bg.raycastTarget = false;

        var layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    void EnsureCombinedLayout()
    {
        if (panelRoot == null) return;
        Transform combinedMarker = panelRoot.transform.Find("ObjectivesSplitLayout");
        if (combinedLayoutBuilt && combinedMarker != null) return;
        if (!combinedLayoutBuilt && combinedMarker != null)
        {
            BindExisting();
            combinedLayoutBuilt = true;
            return;
        }

        // Scene versions may still contain the earlier objectives layout.
        // Keep the scene-authored root and placement, and rebuild its contents
        // with daily and milestone tasks on the left and milestone progress on the right.
        ClearChildren(panelRoot.transform);
        progressionDetailText = null;

        headerText = CreateLabel(panelRoot.transform, "Header", "Objectives", 20, FontStyles.Bold);
        headerText.GetComponent<LayoutElement>().preferredHeight = 26f;

        objectivesColumnsRoot = new GameObject("ObjectivesSplitLayout", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        objectivesColumnsRoot.transform.SetParent(panelRoot.transform, false);
        var columnsLayout = objectivesColumnsRoot.GetComponent<LayoutElement>();
        columnsLayout.minHeight = 180f;
        columnsLayout.preferredHeight = 220f;
        columnsLayout.flexibleWidth = 1f;
        columnsLayout.flexibleHeight = 0f;
        var columnsGroup = objectivesColumnsRoot.GetComponent<HorizontalLayoutGroup>();
        columnsGroup.spacing = 10f;
        columnsGroup.childAlignment = TextAnchor.UpperLeft;
        columnsGroup.childControlWidth = true;
        columnsGroup.childControlHeight = true;
        columnsGroup.childForceExpandWidth = true;
        columnsGroup.childForceExpandHeight = true;

        Transform taskColumnContent = CreateColumnScroll(objectivesColumnsRoot.transform,
            "TasksScroll", out tasksScrollRoot);
        tasksPage = CreatePage(taskColumnContent, "DailyTasksPage");
        ConfigureSectionPage(tasksPage, "Daily Tasks");
        tasksListContent = CreateSectionList(tasksPage.transform, "DailyTasksList");

        milestoneTasksPage = CreatePage(taskColumnContent, "MilestoneTasksPage");
        ConfigureSectionPage(milestoneTasksPage, "Milestone Tasks");
        milestoneTasksListContent = CreateSectionList(milestoneTasksPage.transform, "MilestoneTasksList");

        Transform progressionColumnContent = CreateColumnScroll(objectivesColumnsRoot.transform,
            "ProgressionScroll", out progressionScrollRoot);
        progressionPage = CreatePage(progressionColumnContent, "ProgressionPage");
        ConfigureSectionPage(progressionPage, "Milestone Progress");
        researchButton = CreateButton(progressionPage.transform, "ResearchButton", "Research",
            new Color(0.22f, 0.48f, 0.58f, 1f));
        researchButton.onClick.AddListener(OpenResearch);
        researchButton.gameObject.SetActive(false);
        progressionListContent = CreateSectionList(progressionPage.transform, "ProgressionList");

        tasksPage.SetActive(true);
        milestoneTasksPage.SetActive(true);
        progressionPage.SetActive(true);
        combinedLayoutBuilt = true;
    }

    static void ConfigureSectionPage(GameObject page, string title)
    {
        if (page == null) return;
        var le = page.GetComponent<LayoutElement>() ?? page.AddComponent<LayoutElement>();
        le.minWidth = 0f;
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        var header = CreateLabel(page.transform, "SectionHeader", title, 15, FontStyles.Bold);
        header.alignment = TextAlignmentOptions.Left;
        header.GetComponent<LayoutElement>().preferredHeight = 24f;
    }

    void BindExisting()
    {
        Transform objectivesColumns = panelRoot.transform.Find("ObjectivesSplitLayout");
        if (objectivesColumns != null)
        {
            objectivesColumnsRoot = objectivesColumns.gameObject;
            Transform taskContent = objectivesColumns.Find("TasksScroll/Viewport/Content");
            Transform progressContent = objectivesColumns.Find("ProgressionScroll/Viewport/Content");
            tasksScrollRoot = objectivesColumns.Find("TasksScroll")?.gameObject;
            progressionScrollRoot = objectivesColumns.Find("ProgressionScroll")?.gameObject;
            tasksPage = taskContent != null ? taskContent.Find("DailyTasksPage")?.gameObject : null;
            milestoneTasksPage = taskContent != null ? taskContent.Find("MilestoneTasksPage")?.gameObject : null;
            progressionPage = progressContent != null ? progressContent.Find("ProgressionPage")?.gameObject : null;
            if (tasksListContent == null)
                tasksListContent = taskContent != null ? taskContent.Find("DailyTasksPage/DailyTasksList") : null;
            if (milestoneTasksListContent == null)
                milestoneTasksListContent = taskContent != null ? taskContent.Find("MilestoneTasksPage/MilestoneTasksList") : null;
            if (progressionListContent == null)
                progressionListContent = progressContent != null ? progressContent.Find("ProgressionPage/ProgressionList") : null;
        }
        if (headerText == null)
            headerText = panelRoot.transform.Find("Header")?.GetComponent<TextMeshProUGUI>();
        if (tasksTabButton == null)
            tasksTabButton = panelRoot.transform.Find("Tabs/TasksTab")?.GetComponent<Button>();
        if (progressionTabButton == null)
            progressionTabButton = panelRoot.transform.Find("Tabs/ProgressionTab")?.GetComponent<Button>();
        if (tasksPage == null)
            tasksPage = panelRoot.transform.Find("TasksPage")?.gameObject;
        if (milestoneTasksPage == null)
            milestoneTasksPage = panelRoot.transform.Find("MilestoneTasksPage")?.gameObject;
        if (progressionPage == null)
            progressionPage = panelRoot.transform.Find("ProgressionPage")?.gameObject;
        if (tasksListContent == null)
            tasksListContent = panelRoot.transform.Find("TasksPage/TasksList/Viewport/Content")
                               ?? panelRoot.transform.Find("TasksPage/TasksList")
                               ?? panelRoot.transform.Find("List");
        if (milestoneTasksListContent == null && milestoneTasksPage != null)
            milestoneTasksListContent = milestoneTasksPage.transform.Find("MilestoneTasksList");
        if (progressionListContent == null)
            progressionListContent = panelRoot.transform.Find("ProgressionPage/ProgressionList/Viewport/Content")
                                     ?? panelRoot.transform.Find("ProgressionPage/ProgressionList");
        if (progressionDetailText == null)
            progressionDetailText = panelRoot.transform.Find("ProgressionPage/Detail")?.GetComponent<TextMeshProUGUI>();
        if (researchButton == null && progressionPage != null)
            researchButton = progressionPage.transform.Find("ResearchButton")?.GetComponent<Button>();
        if (researchButton == null && progressionPage != null)
        {
            researchButton = CreateButton(progressionPage.transform, "ResearchButton", "Research",
                new Color(0.22f, 0.48f, 0.58f, 1f));

            Transform progressionList = progressionPage.transform.Find("ProgressionList");
            if (progressionList != null)
                researchButton.transform.SetSiblingIndex(progressionList.GetSiblingIndex());
        }
        if (researchButton != null)
            researchButton.gameObject.SetActive(false);
        if (openQuizButton == null)
            openQuizButton = panelRoot.transform.Find("ProgressionPage/OpenQuizButton")?.GetComponent<Button>();
        if (openQuizButton != null)
        {
            openQuizButton.gameObject.SetActive(false);
            openQuizButton.onClick.RemoveAllListeners();
        }
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
        if (researchButton != null)
        {
            researchButton.onClick.RemoveListener(OpenResearch);
            researchButton.onClick.AddListener(OpenResearch);
        }
    }

    void OpenResearch()
    {
        ResearchTreeUI.Open(targetCanvas != null ? targetCanvas.transform : transform.root);
        Sfx.Play(SfxId.UiClick);
    }

    void EnsureTopTabs()
    {
        if (targetCanvas == null) return;
        if (topTabsRoot != null && topObjectivesButton != null && topResearchButton != null)
            return;

        topTabVisualInitialized = false;

        Transform existing = targetCanvas.transform.Find(TopTabsObjectName);
        topTabsRoot = existing != null
            ? existing.gameObject
            : new GameObject(TopTabsObjectName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        if (topTabsRoot.transform.parent != targetCanvas.transform)
            topTabsRoot.transform.SetParent(targetCanvas.transform, false);

        var rt = (RectTransform)topTabsRoot.transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-25f, 0f);
        rt.sizeDelta = new Vector2(TopTabsWidth, TopTabsHeight);

        var bg = topTabsRoot.GetComponent<Image>() ?? topTabsRoot.AddComponent<Image>();
        // The child buttons remain clickable; the clear strip between them passes through.
        bg.color = Color.clear;
        bg.raycastTarget = false;

        var layout = topTabsRoot.GetComponent<HorizontalLayoutGroup>() ?? topTabsRoot.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Tasks and progression now share one top-level HUD entry. Keep the old
        // objects hidden so scenes saved with the previous layout do not produce
        // duplicate clickable tabs.
        topTasksButton = FindOrCreateTopTab("TasksTopTab", "Tasks");
        topProgressionButton = FindOrCreateTopTab("ProgressionTopTab", "Progression");
        topTasksButton.gameObject.SetActive(false);
        topProgressionButton.gameObject.SetActive(false);
        topObjectivesButton = FindOrCreateTopTab("ObjectivesTopTab", "Objectives");
        topObjectivesButton.gameObject.SetActive(true);
        topResearchButton = FindOrCreateTopResearchButton();

        topObjectivesButton.onClick.RemoveAllListeners();
        topObjectivesButton.onClick.AddListener(ToggleObjectives);
        topResearchButton.onClick.RemoveAllListeners();
        topResearchButton.onClick.AddListener(OpenResearch);
        topTabsRoot.transform.SetAsLastSibling();
        RefreshTopTabs();
    }

    Button FindOrCreateTopTab(string objectName, string label)
    {
        Transform existing = topTabsRoot.transform.Find(objectName);
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(topTabsRoot.transform, false);
            button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        }

        var le = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        le.minWidth = 168f;
        le.preferredWidth = 168f;
        le.flexibleWidth = 1f;
        le.minHeight = 40f;
        le.preferredHeight = 40f;
        if (button.targetGraphic == null)
            button.targetGraphic = button.GetComponent<Graphic>();
        HudTabColors.Apply(button, false);
        return button;
    }

    Button FindOrCreateTopResearchButton()
    {
        Transform existing = topTabsRoot.transform.Find("ResearchTopButton");
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null)
        {
            var go = new GameObject("ResearchTopButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(topTabsRoot.transform, false);
            button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
        }

        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 44f;
        layout.preferredWidth = 44f;
        layout.flexibleWidth = 0f;
        layout.minHeight = 40f;
        layout.preferredHeight = 40f;
        button.GetComponent<Image>().color = GameUITheme.Surface;
        button.transition = Selectable.Transition.ColorTint;
        GameIconLibrary.ApplyToButton(button, GameIconId.Gear, 24f, 0f, true);
        GameUITheme.ApplyCompactControlEffects(button);
        return button;
    }

    void RefreshTopTabs()
    {
        bool open = IsOpen;
        if (!topTabVisualInitialized || lastTopTabOpen != open)
        {
            SetTopTabVisual(topObjectivesButton, open);
            lastTopTabOpen = open;
            topTabVisualInitialized = topObjectivesButton != null;
        }
        if (topResearchButton != null && topResearchButton.gameObject.activeSelf)
            topResearchButton.gameObject.SetActive(false);
    }

    static void SetTopTabVisual(Button button, bool active)
    {
        if (button == null) return;
        // Use the same authoritative chrome, font, edge, and shadow as the
        // Inventory and Management tabs while keeping the label at a fixed size.
        HudTabColors.Apply(button, active);
    }

    void ApplyLayout()
    {
        if (panelRoot == null) return;

        // Keep the panel below its top tabs and let it grow only as far as its content.
        var rt = (RectTransform)panelRoot.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-25f, -68f);
        rt.sizeDelta = new Vector2(500f, 320f);

        if (headerText != null)
            headerText.gameObject.SetActive(false);
        Transform nestedTabs = panelRoot.transform.Find("Tabs");
        if (nestedTabs != null)
            nestedTabs.gameObject.SetActive(true);

        var bg = panelRoot.GetComponent<Image>();
        if (bg != null)
        {
            ApplyTopHudBackground(bg);
            bg.raycastTarget = false;
        }

        var panelLayout = panelRoot.GetComponent<VerticalLayoutGroup>();
        if (panelLayout != null)
        {
            // The two objectives columns should use the full panel width.
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
        }

        SetPageMinimumHeight(tasksPage, 0f);
        SetPageMinimumHeight(milestoneTasksPage, 0f);
        SetPageMinimumHeight(progressionPage, 0f);

        UpdatePanelHeight();
    }

    static void ApplyTopHudBackground(Image image)
    {
        if (image == null) return;
        image.color = HudTabColors.Strip;
        if (image.GetComponent<GameUIThemeStyled>() == null)
            image.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void SetPageMinimumHeight(GameObject page, float minHeight)
    {
        if (page == null) return;
        var layout = page.GetComponent<LayoutElement>();
        if (layout == null) return;
        layout.minHeight = minHeight;
        layout.flexibleHeight = 0f;
    }

    void UpdatePanelHeight()
    {
        panelHeightDirty = true;
        // The scene setup command also uses this class outside Play Mode,
        // where LateUpdate does not run to flush the pending layout.
        if (!Application.isPlaying)
        {
            ApplyPanelHeight();
            panelHeightDirty = false;
        }
    }

    void ApplyPanelHeight()
    {
        if (panelRoot == null || targetCanvas == null) return;

        float screenHeight = targetCanvas.pixelRect.height;
        lastCanvasHeight = screenHeight;
        if (screenHeight <= 0f) screenHeight = Screen.height;
        float maxHeight = Mathf.Max(220f, screenHeight - 150f);
        var panelLayout = panelRoot.GetComponent<VerticalLayoutGroup>();
        float panelPadding = panelLayout != null
            ? panelLayout.padding.top + panelLayout.padding.bottom
            : 24f;
        float maxColumnsHeight = Mathf.Max(180f, maxHeight - panelPadding);
        // Resolve only this panel's text and layout, once after all row changes.
        // Forcing every canvas from customer/statistics events stalls gameplay.
        var panelRect = panelRoot.transform as RectTransform;
        if (panelRect == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);

        float taskHeight = GetScrollContentHeight(tasksScrollRoot);
        float progressionHeight = GetScrollContentHeight(progressionScrollRoot);
        float columnsHeight = Mathf.Max(taskHeight, progressionHeight, 180f);
        columnsHeight = Mathf.Clamp(columnsHeight, 180f, maxColumnsHeight);

        SetScrollHeight(tasksScrollRoot, columnsHeight);
        SetScrollHeight(progressionScrollRoot, columnsHeight);
        var columnsLayout = objectivesColumnsRoot != null
            ? objectivesColumnsRoot.GetComponent<LayoutElement>() : null;
        if (columnsLayout != null)
        {
            columnsLayout.minHeight = columnsHeight;
            columnsLayout.preferredHeight = columnsHeight;
            columnsLayout.flexibleHeight = 0f;
        }

        float panelHeight = Mathf.Clamp(columnsHeight + panelPadding, 120f, maxHeight);

        if (!Mathf.Approximately(panelRect.rect.height, panelHeight))
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
        LayoutRebuilder.MarkLayoutForRebuild(panelRect);
    }

    static float GetScrollContentHeight(GameObject scrollRoot)
    {
        var scroll = scrollRoot != null ? scrollRoot.GetComponent<ScrollRect>() : null;
        RectTransform content = scroll != null ? scroll.content : null;
        if (content == null) return 0f;
        return LayoutUtility.GetPreferredHeight(content);
    }

    static void SetScrollHeight(GameObject scrollRoot, float height)
    {
        if (scrollRoot == null) return;
        var layout = scrollRoot.GetComponent<LayoutElement>();
        if (layout == null) return;
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
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
            if (row != null)
            {
                row.SetActive(false);
                Destroy(row);
            }
        }
        pool.Clear();
    }

    static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child != null)
            {
                child.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }
    }

    static GameObject CreatePage(Transform parent, string name)
    {
        var page = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        page.transform.SetParent(parent, false);
        page.GetComponent<LayoutElement>().flexibleHeight = 0f;
        page.GetComponent<LayoutElement>().minHeight = 0f;
        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return page;
    }

    static Transform CreateColumnScroll(Transform parent, string name, out GameObject scrollRoot)
    {
        scrollRoot = new GameObject(name, typeof(RectTransform), typeof(Image),
            typeof(ScrollRect), typeof(LayoutElement));
        scrollRoot.transform.SetParent(parent, false);
        scrollRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        scrollRoot.GetComponent<Image>().raycastTarget = false;

        var scrollLayout = scrollRoot.GetComponent<LayoutElement>();
        scrollLayout.minWidth = 0f;
        scrollLayout.flexibleWidth = 1f;
        scrollLayout.minHeight = 180f;
        scrollLayout.preferredHeight = 220f;
        scrollLayout.flexibleHeight = 1f;

        var scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(scroll);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollRoot.transform, false);
        var viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
        viewport.GetComponent<Image>().raycastTarget = false;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 14f;
        contentLayout.padding = new RectOffset(4, 4, 4, 4);
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        var wheelBlocker = scrollRoot.AddComponent<ScrollRectWheelBlocker>();
        wheelBlocker.scrollRect = scroll;
        return content.transform;
    }

    static Transform CreateSectionList(Transform parent, string name)
    {
        var content = new GameObject(name, typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        content.transform.SetParent(parent, false);
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(4, 4, 2, 4);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var element = content.GetComponent<LayoutElement>();
        element.minHeight = 0f;
        element.flexibleHeight = 0f;
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
        GameIconLibrary.ApplyToButton(btn,
            name.Contains("Tasks") ? GameIconId.Checkmark : GameIconId.Gear,
            17f, 7f);
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
        if (name == "ResearchButton")
            GameIconLibrary.ApplyToButton(btn, GameIconId.Gear, 18f, 8f);
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
        go.AddComponent<GameUIThemeStyled>();
    }
}
