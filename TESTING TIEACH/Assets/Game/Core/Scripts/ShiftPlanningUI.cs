using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds a reversible pre-shift planning card and measures station constraints
/// for the existing end-of-day review. It does not change production or saves.
/// </summary>
public sealed class ShiftPlanningUI : MonoBehaviour
{
    const string PauseSource = "ShiftPlanning";
    const float LiveRefreshSeconds = 1f;

    [System.Serializable]
    public sealed class ShiftResult
    {
        public string itemName;
        public float targetRate;
        public float deliveredRate;
        public int ordersServed;
        public float averageWait;
    }

    [System.Serializable]
    public sealed class SavedState
    {
        public string selectedGoalItemName;
        public ShiftResult latestShift;
        public ShiftResult previousShift;
    }

    struct StationSnapshot
    {
        public float working;
        public float starved;
        public float blocked;
    }

    sealed class StationIssue
    {
        public StationNode node;
        public float lostSeconds;
        public float share;
        public bool blocked;
    }

    public static ShiftPlanningUI Instance { get; private set; }
    static SavedState pendingRestoreState;
    static bool hasPendingRestoreState;

    readonly Dictionary<int, StationSnapshot> stationBaseline = new Dictionary<int, StationSnapshot>();
    readonly List<ItemDefinition> goalItems = new List<ItemDefinition>();
    GameTimeManager timeManager;
    GameObject panelRoot;
    GameObject livePanelRoot;
    TextMeshProUGUI dayTitle;
    TextMeshProUGUI demandText;
    TextMeshProUGUI goalComparisonText;
    TextMeshProUGUI liveItemRatesText;
    TextMeshProUGUI liveStatsText;
    TextMeshProUGUI liveDiagnosisText;
    Button startShiftButton;
    ItemDefinition selectedGoalItem;
    string selectedGoalItemName;
    StationNode diagnosedStation;
    ShiftResult latestShift;
    ShiftResult previousShift;
    bool initialized;
    bool planHiddenForStationPopup;
    float nextLiveRefreshTime;
    int lastPlanDemandRevision = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ShiftPlanningUI>() != null) return;
        new GameObject("ShiftPlanningUI").AddComponent<ShiftPlanningUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (hasPendingRestoreState)
            ApplySaveState(pendingRestoreState);
    }

    void Start()
    {
        StartCoroutine(InitializeWhenGameplayIsReady());
    }

    void OnDestroy()
    {
        if (timeManager != null)
            timeManager.OnDayStarted -= HandleDayStarted;
        if (timeManager != null)
            timeManager.OnShiftEnding -= RecordShiftCompletion;
        if (timeManager != null)
            timeManager.ReleaseExternalPause(PauseSource);
        if (Instance == this) Instance = null;
    }

    IEnumerator InitializeWhenGameplayIsReady()
    {
        while (GameTimeManager.Instance == null)
            yield return null;

        timeManager = GameTimeManager.Instance;
        timeManager.OnDayStarted += HandleDayStarted;
        timeManager.OnShiftEnding += RecordShiftCompletion;

        // Save restoration and the title or intro sequence can finish after this
        // component starts. Wait for them so a resumed mid-shift save is not
        // interrupted by a fresh planning card.
        while (TitleScreenController.IsTitleScreenActive || OnboardingTutorial.BlocksProgression)
            yield return null;

        initialized = true;
        if (IsAtShiftStart())
            ShowPlanningCard();
        else
        {
            CaptureStationBaseline();
            ShowLivePanel();
        }
    }

    void Update()
    {
        if (Time.unscaledTime < nextLiveRefreshTime) return;
        nextLiveRefreshTime = Time.unscaledTime + LiveRefreshSeconds;
        if (panelRoot != null && panelRoot.activeSelf)
        {
            CustomerOrderConfig planConfig = ResolveOrderConfig();
            int revision = planConfig != null ? planConfig.DemandScheduleRevision : -1;
            if (revision != lastPlanDemandRevision)
                RefreshPlan();
        }
        if (livePanelRoot != null && livePanelRoot.activeSelf)
            RefreshLivePanel();
    }

    bool IsAtShiftStart()
    {
        if (timeManager == null || timeManager.IsShiftOver) return false;
        return Mathf.Abs(timeManager.CurrentMinutes - timeManager.dayStartHour * 60f) < 0.5f;
    }

    void HandleDayStarted()
    {
        if (!initialized || TitleScreenController.IsTitleScreenActive || OnboardingTutorial.BlocksProgression)
            return;
        CaptureStationBaseline();
        ShowPlanningCard();
    }

    void ShowPlanningCard()
    {
        if (timeManager == null || timeManager.IsShiftOver
            || TitleScreenController.IsTitleScreenActive || OnboardingTutorial.BlocksProgression)
            return;

        EnsurePanel();
        if (panelRoot == null) return;
        if (livePanelRoot != null) livePanelRoot.SetActive(false);
        RefreshPlan();
        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();
        ManagementModeController management = ManagementModeController.Instance;
        if (management != null && management.HasOpenStationPanel)
            SetStationPopupOpen(true);
        CaptureStationBaseline();
        timeManager.RequestExternalPause(PauseSource);
        Sfx.Play(SfxId.UiOpen);
    }

    void EnsurePanel()
    {
        if (panelRoot != null)
        {
            EnsureLivePanel(ResolvePlayerUICanvas());
            return;
        }

        Canvas canvas = ResolvePlayerUICanvas();
        if (canvas == null) return;

        panelRoot = new GameObject("ShiftPlanCard", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup));
        panelRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = (RectTransform)panelRoot.transform;
        rootRect.anchorMin = new Vector2(1f, 0f);
        rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-20f, 20f);
        rootRect.sizeDelta = new Vector2(480f, 400f);

        Image background = panelRoot.GetComponent<Image>();
        background.color = new Color(GameUITheme.Panel.r, GameUITheme.Panel.g,
            GameUITheme.Panel.b, 0.985f);
        // The card stays visible while the player opens Build and Staff. Only
        // its buttons catch clicks, so planning remains possible underneath it.
        background.raycastTarget = false;

        VerticalLayoutGroup layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        dayTitle = CreateText(panelRoot.transform, "DayTitle", "SHIFT PLAN", 24f,
            FontStyles.Bold, 32f, GameUITheme.Accent);
        TextMeshProUGUI intro = CreateText(panelRoot.transform, "Intro",
            "Review demand, tune the kitchen, then start the shift.",
            13f, FontStyles.Normal, 28f, GameUITheme.TextPrimary);
        intro.textWrappingMode = TextWrappingModes.Normal;

        CreateText(panelRoot.transform, "DemandHeading", "MENU DEMAND PER MINUTE", 13f,
            FontStyles.Bold, 20f, GameUITheme.Accent);
        demandText = CreateText(panelRoot.transform, "DemandList", "Loading demand...", 16f,
            FontStyles.Normal, 32f, GameUITheme.TextPrimary);
        demandText.textWrappingMode = TextWrappingModes.Normal;
        demandText.enableAutoSizing = true;
        demandText.fontSizeMin = 12f;
        demandText.fontSizeMax = 16f;

        goalComparisonText = CreateText(panelRoot.transform, "PreviousShiftResult", "No previous shift result yet.",
            13f, FontStyles.Normal, 38f, GameUITheme.TextSecondary);
        goalComparisonText.textWrappingMode = TextWrappingModes.Normal;

        TextMeshProUGUI runHint = CreateText(panelRoot.transform, "RunHint",
            "Shift stays paused until you start. Pause or fast-forward during the run; review lost production time afterward.",
            12f, FontStyles.Normal, 40f, GameUITheme.TextSecondary);
        runHint.textWrappingMode = TextWrappingModes.Normal;

        startShiftButton = CreateButton(panelRoot.transform, "StartShiftButton", "START SHIFT");
        startShiftButton.onClick.AddListener(StartShift);
        panelRoot.SetActive(false);
        EnsureLivePanel(canvas);
        FitPlanPanel();
    }

    void EnsureLivePanel(Canvas canvas)
    {
        if (livePanelRoot != null || canvas == null) return;

        livePanelRoot = new GameObject("ShiftRunStatus", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup));
        livePanelRoot.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)livePanelRoot.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-20f, 20f);
        rect.sizeDelta = new Vector2(430f, 190f);

        Image background = livePanelRoot.GetComponent<Image>();
        background.color = new Color(0.075f, 0.09f, 0.12f, 0.96f);
        background.raycastTarget = false;

        VerticalLayoutGroup layout = livePanelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateText(livePanelRoot.transform, "RunHeading", "SHIFT IN PROGRESS", 14f,
            FontStyles.Bold, 22f, GameUITheme.Accent);
        liveStatsText = CreateText(livePanelRoot.transform, "KitchenProductionSummary",
            "Kitchen output: 0 items/min  |  Demand covered: 0%", 14f,
            FontStyles.Bold, 42f, GameUITheme.TextPrimary);
        liveStatsText.textWrappingMode = TextWrappingModes.Normal;
        liveStatsText.enableAutoSizing = true;
        liveStatsText.fontSizeMin = 11f;
        liveStatsText.fontSizeMax = 14f;
        liveItemRatesText = CreateText(livePanelRoot.transform, "ItemProductionRates",
            "Delivered to pickup: preparing...", 13f,
            FontStyles.Normal, 40f, GameUITheme.TextSecondary);
        liveItemRatesText.textWrappingMode = TextWrappingModes.Normal;
        liveItemRatesText.enableAutoSizing = true;
        liveItemRatesText.fontSizeMin = 11f;
        liveItemRatesText.fontSizeMax = 13f;
        liveDiagnosisText = CreateText(livePanelRoot.transform, "LiveDiagnosis", "Checking station activity...", 13f,
            FontStyles.Normal, 42f, GameUITheme.TextPrimary);
        liveDiagnosisText.textWrappingMode = TextWrappingModes.Normal;
        livePanelRoot.SetActive(false);
    }

    void RefreshPlan()
    {
        if (dayTitle != null)
            dayTitle.text = "DAY " + (timeManager != null ? timeManager.CurrentDay : 1) + "  |  PLAN";

        CustomerOrderConfig config = ResolveOrderConfig();
        goalItems.Clear();
        if (config == null)
        {
            lastPlanDemandRevision = -1;
            if (demandText != null) demandText.text = "Demand schedule is not ready yet.";
            SetDemandListHeight(2);
            FitPlanPanel();
            return;
        }

        lastPlanDemandRevision = config.DemandScheduleRevision;

        var items = new List<ItemDefinition>(config.GetEnabledMenuItems());
        items.Sort((a, b) =>
        {
            int rateCompare = config.GetCustomerDemandPerMinute(b)
                .CompareTo(config.GetCustomerDemandPerMinute(a));
            if (rateCompare != 0) return rateCompare;
            string aName = a != null ? a.itemName : string.Empty;
            string bName = b != null ? b.itemName : string.Empty;
            return string.Compare(aName, bName, System.StringComparison.OrdinalIgnoreCase);
        });

        foreach (ItemDefinition item in items)
            if (item != null && config.GetCustomerDemandPerMinute(item) > 0)
                goalItems.Add(item);

        if (selectedGoalItem != null && !ContainsGoalItem(selectedGoalItem.itemName))
            selectedGoalItem = null;
        if (selectedGoalItem == null && !string.IsNullOrEmpty(selectedGoalItemName))
            for (int i = 0; i < goalItems.Count; i++)
                if (goalItems[i].itemName == selectedGoalItemName)
                {
                    selectedGoalItem = goalItems[i];
                    break;
                }
        if (selectedGoalItem == null && goalItems.Count > 0)
            selectedGoalItem = goalItems[0];
        selectedGoalItemName = selectedGoalItem != null ? selectedGoalItem.itemName : string.Empty;

        if (demandText != null && goalItems.Count == 0)
        {
            demandText.text = "No menu items are enabled. Open Business > Menu to choose what to sell.";
            SetDemandListHeight(2);
        }
        else if (demandText != null)
        {
            var lines = new StringBuilder();
            int visibleItems = Mathf.Min(6, goalItems.Count);
            for (int i = 0; i < visibleItems; i++)
            {
                ItemDefinition item = goalItems[i];
                if (lines.Length > 0) lines.Append('\n');
                lines.Append("-  ").Append(item.itemName).Append("    ")
                    .Append(config.GetCustomerDemandPerMinute(item).ToString("0.#"))
                    .Append(" items/min");
            }
            if (goalItems.Count > visibleItems)
                lines.Append("\n+").Append(goalItems.Count - visibleItems).Append(" more items.");
            demandText.text = lines.ToString();
            SetDemandListHeight(Mathf.Max(2, visibleItems
                + (goalItems.Count > visibleItems ? 1 : 0)));
        }

        if (goalComparisonText != null)
            goalComparisonText.text = GetLatestShiftResultText();
        FitPlanPanel();
    }

    void SetDemandListHeight(int lineCount)
    {
        if (demandText == null) return;
        LayoutElement layout = demandText.GetComponent<LayoutElement>();
        if (layout == null) return;
        float height = Mathf.Clamp(lineCount * 20f + 4f, 28f, 144f);
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    void FitPlanPanel()
    {
        if (panelRoot == null) return;
        RectTransform rect = panelRoot.transform as RectTransform;
        RectTransform canvasRect = panelRoot.transform.parent as RectTransform;
        if (rect == null) return;

        VerticalLayoutGroup layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        float preferredHeight = layout != null
            ? layout.padding.vertical + layout.spacing * Mathf.Max(0, panelRoot.transform.childCount - 1)
            : 0f;
        for (int i = 0; i < panelRoot.transform.childCount; i++)
        {
            RectTransform child = panelRoot.transform.GetChild(i) as RectTransform;
            if (child != null && child.gameObject.activeSelf)
                preferredHeight += LayoutUtility.GetPreferredHeight(child);
        }
        float desiredHeight = Mathf.Max(360f, preferredHeight + 4f);
        float availableHeight = canvasRect != null
            ? Mathf.Max(240f, canvasRect.rect.height - 40f)
            : Mathf.Max(240f, Screen.height - 40f);
        float width = canvasRect != null
            ? Mathf.Min(480f, Mathf.Max(320f, canvasRect.rect.width - 40f))
            : Mathf.Min(480f, Mathf.Max(320f, Screen.width - 40f));

        rect.sizeDelta = new Vector2(width, desiredHeight);
        rect.localScale = desiredHeight > availableHeight
            ? Vector3.one * (availableHeight / desiredHeight) : Vector3.one;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    void StartShift()
    {
        ProductionManager.Instance?.BeginShiftThroughputTracking();
        StoreStatisticsManager.Instance?.BeginShiftThroughputTracking();
        CaptureStationBaseline();
        planHiddenForStationPopup = false;
        if (panelRoot != null) panelRoot.SetActive(false);
        ShowLivePanel();
        if (timeManager != null) timeManager.ReleaseExternalPause(PauseSource);
        Sfx.Play(SfxId.UiClick);
    }

    public static void SetStationPopupOpen(bool isOpen)
    {
        ShiftPlanningUI planning = Instance;
        if (planning == null || planning.panelRoot == null) return;

        if (isOpen)
        {
            if (!planning.panelRoot.activeSelf) return;
            planning.planHiddenForStationPopup = true;
            planning.panelRoot.SetActive(false);
            return;
        }

        if (!planning.planHiddenForStationPopup) return;
        planning.planHiddenForStationPopup = false;
        if (planning.timeManager == null || planning.timeManager.IsShiftOver
            || !planning.initialized)
            return;

        planning.panelRoot.SetActive(true);
        planning.panelRoot.transform.SetAsLastSibling();
    }

    CustomerOrderConfig ResolveOrderConfig()
    {
        return ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null
            ? ProductionManager.Instance.orderConfig
            : FindFirstObjectByType<CustomerOrderConfig>();
    }

    bool ContainsGoalItem(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return false;
        for (int i = 0; i < goalItems.Count; i++)
            if (goalItems[i] != null && goalItems[i].itemName == itemName)
                return true;
        return false;
    }

    void ShowLivePanel()
    {
        EnsurePanel();
        if (panelRoot != null) panelRoot.SetActive(false);
        if (livePanelRoot == null) return;
        if (goalItems.Count == 0) RefreshPlan();
        livePanelRoot.SetActive(true);
        livePanelRoot.transform.SetAsLastSibling();
        nextLiveRefreshTime = 0f;
        RefreshLivePanel();
    }

    void RefreshLivePanel()
    {
        if (livePanelRoot == null) return;
        CustomerOrderConfig config = ResolveOrderConfig();
        ProductionManager production = ProductionManager.Instance;
        var items = config != null
            ? new List<ItemDefinition>(config.GetEnabledMenuItems())
            : new List<ItemDefinition>();
        items.RemoveAll(item => item == null);
        if (config != null)
            items.Sort((a, b) =>
            {
                int rateCompare = config.GetCustomerDemandPerMinute(b)
                    .CompareTo(config.GetCustomerDemandPerMinute(a));
                return rateCompare != 0 ? rateCompare
                    : string.Compare(a.itemName, b.itemName, System.StringComparison.OrdinalIgnoreCase);
            });

        var itemLines = new StringBuilder();
        itemLines.Append("Delivered to pickup (items/min)");
        float totalDeliveredRate = 0f;
        float totalTargetRate = 0f;
        float coveredTargetRate = 0f;
        foreach (ItemDefinition item in items)
        {
            float targetRate = config.GetCustomerDemandPerMinute(item);
            float deliveredRate = production != null
                ? production.GetPickupDeliveryRatePerMinute(item) : 0f;
            totalDeliveredRate += deliveredRate;
            totalTargetRate += targetRate;
            coveredTargetRate += Mathf.Min(deliveredRate, targetRate);
            itemLines.Append('\n').Append(item.itemName).Append("  ")
                .Append(deliveredRate.ToString("0.0")).Append(" / ")
                .Append(targetRate.ToString("0.#"));
        }
        if (items.Count == 0)
            itemLines.Append("\nNo enabled menu items.");

        float demandCoverage = totalTargetRate > 0f
            ? coveredTargetRate / totalTargetRate * 100f : 0f;
        StoreStatisticsManager stats = StoreStatisticsManager.Instance;
        if (liveStatsText != null)
            liveStatsText.text = "Kitchen output: " + totalDeliveredRate.ToString("0.0")
                + " items/min  |  Demand covered: " + demandCoverage.ToString("0") + "%"
                + "\nOrders: " + (stats != null ? stats.ThroughputOrdersPerMinute : 0f).ToString("0.0")
                + "/min  |  Average wait: "
                + (stats != null ? stats.AverageWaitTimeTodaySeconds : 0f).ToString("0") + "s";
        if (liveItemRatesText != null)
        {
            liveItemRatesText.text = itemLines.ToString();
            LayoutElement itemListSize = liveItemRatesText.GetComponent<LayoutElement>();
            if (itemListSize != null)
            {
                float itemListHeight = 20f + Mathf.Max(1, items.Count + 1) * 16f;
                itemListSize.minHeight = itemListHeight;
                itemListSize.preferredHeight = itemListHeight;
            }
        }
        RectTransform panelRect = livePanelRoot.GetComponent<RectTransform>();
        if (panelRect != null)
            panelRect.sizeDelta = new Vector2(430f, Mathf.Clamp(190f + items.Count * 17f, 205f, 410f));

        string diagnosis = BuildShiftProductionDiagnosis();
        if (liveDiagnosisText != null)
            liveDiagnosisText.text = string.IsNullOrEmpty(diagnosis)
                ? "No major station delay detected." : diagnosis;

    }
    void CaptureStationBaseline()
    {
        stationBaseline.Clear();
        StationNode[] nodes = FindObjectsByType<StationNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (StationNode node in nodes)
        {
            if (node == null) continue;
            StationRuntimeMetrics metrics = StationRuntimeMetrics.EnsureOn(node.gameObject);
            if (metrics == null) continue;
            stationBaseline[metrics.GetInstanceID()] = new StationSnapshot
            {
                working = metrics.WorkingSeconds,
                starved = metrics.StarvedSeconds,
                blocked = metrics.BlockedSeconds
            };
        }
    }

    public static string GetShiftProductionDiagnosis()
    {
        return Instance != null ? Instance.BuildShiftProductionDiagnosis() : string.Empty;
    }

    public static string GetShiftComparisonSummary()
    {
        return Instance != null ? Instance.BuildShiftComparisonSummary() : string.Empty;
    }

    public static SavedState CaptureSaveState()
    {
        return Instance != null ? Instance.MakeSavedState() : new SavedState();
    }

    public static void RestoreSaveState(SavedState state)
    {
        pendingRestoreState = CloneSavedState(state);
        hasPendingRestoreState = true;
        if (Instance != null)
            Instance.ApplySaveState(pendingRestoreState);
    }

    SavedState MakeSavedState()
    {
        return new SavedState
        {
            selectedGoalItemName = selectedGoalItemName,
            latestShift = CloneResult(latestShift),
            previousShift = CloneResult(previousShift)
        };
    }

    void ApplySaveState(SavedState state)
    {
        selectedGoalItemName = state != null ? state.selectedGoalItemName : string.Empty;
        latestShift = state != null ? CloneResult(state.latestShift) : null;
        previousShift = state != null ? CloneResult(state.previousShift) : null;
        selectedGoalItem = null;
        hasPendingRestoreState = false;
    }

    static SavedState CloneSavedState(SavedState state)
    {
        return state == null ? null : new SavedState
        {
            selectedGoalItemName = state.selectedGoalItemName,
            latestShift = CloneResult(state.latestShift),
            previousShift = CloneResult(state.previousShift)
        };
    }

    static ShiftResult CloneResult(ShiftResult result)
    {
        return result == null ? null : new ShiftResult
        {
            itemName = result.itemName,
            targetRate = result.targetRate,
            deliveredRate = result.deliveredRate,
            ordersServed = result.ordersServed,
            averageWait = result.averageWait
        };
    }

    void RecordShiftCompletion()
    {
        if (selectedGoalItem == null) return;
        CustomerOrderConfig config = ResolveOrderConfig();
        StoreStatisticsManager stats = StoreStatisticsManager.Instance;
        previousShift = CloneResult(latestShift);
        latestShift = new ShiftResult
        {
            itemName = selectedGoalItem.itemName,
            targetRate = config != null ? config.GetCustomerDemandPerMinute(selectedGoalItem) : 0f,
            deliveredRate = ProductionManager.Instance != null
                ? ProductionManager.Instance.GetPickupDeliveryRatePerMinute(selectedGoalItem) : 0f,
            ordersServed = stats != null ? stats.OrdersCompletedToday : 0,
            averageWait = stats != null ? stats.AverageWaitTimeTodaySeconds : 0f
        };
    }

    string GetLatestShiftResultText()
    {
        if (latestShift == null || string.IsNullOrEmpty(latestShift.itemName))
            return "No previous shift result yet.";
        string result = "Last shift: " + latestShift.itemName + " delivered "
            + latestShift.deliveredRate.ToString("0.0") + "/"
            + latestShift.targetRate.ToString("0.#") + " items/min.";
        if (previousShift != null && previousShift.itemName == latestShift.itemName)
        {
            float change = latestShift.deliveredRate - previousShift.deliveredRate;
            result += change >= 0f
                ? " Up " + change.ToString("0.0") + "/min from the shift before."
                : " Down " + Mathf.Abs(change).ToString("0.0") + "/min from the shift before.";
        }
        else
            result += " Orders served " + latestShift.ordersServed
                + "; average wait " + latestShift.averageWait.ToString("0") + "s.";
        return result;
    }

    string BuildShiftComparisonSummary()
    {
        if (latestShift == null || string.IsNullOrEmpty(latestShift.itemName))
            return string.Empty;
        string line = latestShift.itemName + " delivered " + latestShift.deliveredRate.ToString("0.0")
            + "/" + latestShift.targetRate.ToString("0.#") + " items/min";
        if (previousShift != null && previousShift.itemName == latestShift.itemName)
        {
            float change = latestShift.deliveredRate - previousShift.deliveredRate;
            line += change >= 0f
                ? " (up " + change.ToString("0.0") + "/min from the previous shift)."
                : " (down " + Mathf.Abs(change).ToString("0.0") + "/min from the previous shift).";
        }
        else
            line += ". Orders served " + latestShift.ordersServed
                + "; average wait " + latestShift.averageWait.ToString("0") + "s.";
        return line;
    }

    string BuildShiftProductionDiagnosis()
    {
        diagnosedStation = null;
        StationIssue worstIssue = null;
        StationNode[] nodes = FindObjectsByType<StationNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (StationNode node in nodes)
        {
            if (node == null) continue;
            StationRuntimeMetrics metrics = node.GetComponent<StationRuntimeMetrics>();
            if (metrics == null) continue;

            stationBaseline.TryGetValue(metrics.GetInstanceID(), out StationSnapshot start);
            float working = Mathf.Max(0f, metrics.WorkingSeconds - start.working);
            float starved = Mathf.Max(0f, metrics.StarvedSeconds - start.starved);
            float blocked = Mathf.Max(0f, metrics.BlockedSeconds - start.blocked);
            float neededTime = working + starved + blocked;
            if (neededTime < 10f) continue;

            bool isBlocked = blocked >= starved;
            float lostTime = isBlocked ? blocked : starved;
            float share = lostTime / neededTime;
            if (share < 0.12f || lostTime < 5f) continue;
            if (worstIssue == null || lostTime > worstIssue.lostSeconds)
            {
                worstIssue = new StationIssue
                {
                    node = node,
                    lostSeconds = lostTime,
                    share = share,
                    blocked = isBlocked
                };
            }
        }

        if (worstIssue == null || worstIssue.node == null) return string.Empty;
        diagnosedStation = worstIssue.node;
        string stationName = diagnosedStation.DisplayName;
        string percent = Mathf.RoundToInt(worstIssue.share * 100f) + "%";
        string duration = worstIssue.lostSeconds.ToString("0") + "s";
        return worstIssue.blocked
            ? stationName + " could not move output for " + percent + " (" + duration
                + "). Inspect its downstream link and available buffer space."
            : stationName + " lacked inputs for " + percent + " (" + duration
                + "). Inspect its incoming link and worker coverage.";
    }
    static Canvas ResolvePlayerUICanvas()
    {
        GameObject playerUi = GameObject.Find("PlayerUI");
        if (playerUi != null)
        {
            Canvas canvas = playerUi.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        TitleScreenController title = FindFirstObjectByType<TitleScreenController>();
        if (title != null && title.inGameUIRoot != null)
        {
            Canvas canvas = title.inGameUIRoot.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }
        return FindFirstObjectByType<Canvas>();
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, float height, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        LayoutElement sizeHint = go.GetComponent<LayoutElement>();
        sizeHint.minHeight = height;
        sizeHint.preferredHeight = height;
        return text;
    }

    static Button CreateButton(Transform parent, string name, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement size = go.GetComponent<LayoutElement>();
        size.minHeight = 48f;
        size.preferredHeight = 48f;
        Image image = go.GetComponent<Image>();
        image.color = GameUITheme.Positive;
        Button button = go.GetComponent<Button>();
        GameUITheme.ApplyButton(button);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(go.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return button;
    }
}
