using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles pre-shift menu planning, presents the active plan, and measures
/// station constraints for the end-of-day review.
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
        public List<StationBaselineRecord> stationBaselines = new List<StationBaselineRecord>();
    }

    [System.Serializable]
    public sealed class StationBaselineRecord
    {
        public string stationGuid;
        public float working;
        public float starved;
        public float blocked;
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

    sealed class DemandRow
    {
        public GameObject root;
        public ItemDefinition item;
        public RawImage preview;
        public TextMeshProUGUI itemName;
        public TextMeshProUGUI rate;
        public TextMeshProUGUI shiftTotal;
        public Button changeButton;
        public Button removeButton;
    }

    public static ShiftPlanningUI Instance { get; private set; }
    public static bool IsPlanning => Instance != null
        && Instance.preShiftPlanningActive
        && (Instance.timeManager == null || !Instance.timeManager.IsShiftOver);
    static SavedState pendingRestoreState;
    static bool hasPendingRestoreState;

    readonly Dictionary<string, StationSnapshot> stationBaseline =
        new Dictionary<string, StationSnapshot>(System.StringComparer.Ordinal);
    readonly Dictionary<ItemDefinition, Button> itemPickerOptionButtons = new Dictionary<ItemDefinition, Button>();
    readonly List<ItemDefinition> goalItems = new List<ItemDefinition>();
    readonly List<DemandRow> demandRows = new List<DemandRow>();
    GameTimeManager timeManager;
    GameObject panelRoot;
    GameObject livePanelRoot;
    TextMeshProUGUI dayTitle;
    TextMeshProUGUI shiftDurationText;
    GameObject demandHeaderRow;
    GameObject demandRowsRoot;
    TextMeshProUGUI demandEmptyText;
    LayoutElement demandRowsLayout;
    Button addMenuItemButton;
    GameObject itemPickerRoot;
    TextMeshProUGUI itemPickerTitle;
    GameObject itemPickerRowsRoot;
    TextMeshProUGUI itemPickerEmptyText;
    DemandRow itemPickerTargetRow;
    TextMeshProUGUI liveItemRatesText;
    TextMeshProUGUI liveStatsText;
    TextMeshProUGUI liveDiagnosisText;
    Button startShiftButton;
    GameObject startShiftWarningOverlay;
    TextMeshProUGUI startShiftWarningDetails;
    RectTransform startShiftWarningCardRect;
    ItemDefinition selectedGoalItem;
    string selectedGoalItemName;
    StationNode diagnosedStation;
    ShiftResult latestShift;
    ShiftResult previousShift;
    bool initialized;
    bool planHiddenForStationPopup;
    bool preShiftPlanningActive;
    bool hasSavedStationBaseline;
    float nextLiveRefreshTime;
    int lastPlanDemandRevision = int.MinValue;

    public Button TutorialAddMenuItemButton => addMenuItemButton;
    public bool IsMenuItemPickerOpen => itemPickerRoot != null && itemPickerRoot.activeSelf;
    public RectTransform TutorialPlanningCard => panelRoot != null ? panelRoot.transform as RectTransform : null;

    public Button TutorialMenuItemButton(ItemDefinition item)
    {
        return item != null && itemPickerOptionButtons.TryGetValue(item, out Button button) ? button : null;
    }

    public void FocusPlanningCard()
    {
        if (panelRoot == null || !IsPlanning) return;
        planHiddenForStationPopup = false;
        ShowAndFitPlanningPanel();
    }

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
        while (TitleScreenController.IsTitleScreenActive
            || (OnboardingTutorial.BlocksProgression && !OnboardingTutorial.IsActive))
            yield return null;

        initialized = true;
        if (IsAtShiftStart())
            ShowPlanningCard();
        else
        {
            if (!hasSavedStationBaseline)
                CaptureStationBaseline();
            ShowLivePanel();
        }
    }

    void Update()
    {
        if (panelRoot != null && preShiftPlanningActive && !planHiddenForStationPopup)
        {
            // Once Gus introduces the plan, keep it visible throughout setup.
            bool showPlan = !OnboardingTutorial.IsActive
                || OnboardingTutorial.ShouldShowPlanCardDuringTutorial;
            if (panelRoot.activeSelf != showPlan)
            {
                if (showPlan)
                    ShowAndFitPlanningPanel();
                else
                    panelRoot.SetActive(false);
            }
        }

        if (startShiftButton != null)
        {
            CustomerOrderConfig menu = ResolveOrderConfig();
            startShiftButton.interactable = OnboardingTutorial.CanStartShiftFromPlan
                && menu != null && menu.HasEnabledItems;
        }
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
            || TitleScreenController.IsTitleScreenActive
            || (OnboardingTutorial.BlocksProgression && !OnboardingTutorial.IsActive))
            return;

        EnsurePanel();
        if (panelRoot == null) return;
        EnsureMainHudNavigation();
        if (livePanelRoot != null) livePanelRoot.SetActive(false);
        RefreshPlan();
        ResetWorkersForPlanning();
        // Keep the planning phase active even if another UI panel temporarily
        // hides the card. Station movement remains available until Start Shift.
        preShiftPlanningActive = true;
        ShowAndFitPlanningPanel();
        ManagementModeController management = ManagementModeController.Instance;
        if (management != null && management.HasOpenStationPanel)
            SetStationPopupOpen(true);
        CaptureStationBaseline();
        timeManager.RequestExternalPause(PauseSource);
        Sfx.Play(SfxId.UiOpen);
    }

    void ShowAndFitPlanningPanel()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        // RefreshPlan can run while the card is inactive during save restore.
        // Unity skips layout for inactive hierarchies, so rebuild after activation
        // before the first visible frame to keep the footer clear of menu rows.
        Canvas.ForceUpdateCanvases();
        FitPlanPanel();
        Canvas.ForceUpdateCanvases();
    }

    public static void ReturnToPlanPhaseAfterTutorial()
    {
        ShiftPlanningUI planning = Instance != null
            ? Instance
            : FindFirstObjectByType<ShiftPlanningUI>(FindObjectsInactive.Include);
        GameTimeManager clock = GameTimeManager.Instance;
        if (clock != null)
            clock.ResetDayClock();
        if (planning == null)
            return;

        planning.timeManager = clock != null ? clock : planning.timeManager;
        planning.planHiddenForStationPopup = false;
        planning.ShowPlanningCard();
    }

    /// <summary>Restores the HUD and, when interrupted, the active Plan phase after a Gus cutscene.</summary>
    public static void RestoreAfterGusCutscene(bool returnToPlan)
    {
        ShiftPlanningUI planning = Instance != null
            ? Instance
            : FindFirstObjectByType<ShiftPlanningUI>(FindObjectsInactive.Include);
        if (planning == null)
        {
            MainHudTabs fallbackTabs = MainHudTabs.EnsureInScene();
            if (fallbackTabs != null)
                fallbackTabs.RestoreNavigation();
            return;
        }

        if (returnToPlan)
        {
            planning.timeManager = GameTimeManager.Instance != null
                ? GameTimeManager.Instance : planning.timeManager;
            planning.planHiddenForStationPopup = false;
            planning.ShowPlanningCard();
        }
        else
        {
            planning.EnsureMainHudNavigation();
        }
    }

    void EnsureMainHudNavigation()
    {
        MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
        if (tabs == null)
        {
            Canvas canvas = ResolvePlayerUICanvas();
            if (canvas != null)
                tabs = MainHudTabs.CreateInCanvas(canvas.transform);
        }

        if (tabs == null) return;
        tabs.RestoreNavigation();
        tabs.transform.SetAsLastSibling();
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

        panelRoot = new GameObject("ShiftPlanCard", typeof(RectTransform), typeof(Image), typeof(Outline),
            typeof(VerticalLayoutGroup));
        panelRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = (RectTransform)panelRoot.transform;
        rootRect.anchorMin = new Vector2(1f, 0f);
        rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-20f, 20f);
        rootRect.sizeDelta = new Vector2(520f, 400f);

        Image background = panelRoot.GetComponent<Image>();
        background.color = new Color(GameUITheme.Panel.r, GameUITheme.Panel.g,
            GameUITheme.Panel.b, 0.985f);
        // The card stays visible while the player opens Build and Staff. Only
        // its buttons catch clicks, so planning remains possible underneath it.
        background.raycastTarget = false;

        // A muted gold edge separates the charcoal planning card from the scene
        // without competing with the green primary action.
        Outline border = panelRoot.GetComponent<Outline>();
        border.effectColor = new Color(GameUITheme.Accent.r, GameUITheme.Accent.g,
            GameUITheme.Accent.b, 0.72f);
        border.effectDistance = new Vector2(2f, -2f);
        border.useGraphicAlpha = false;

        VerticalLayoutGroup layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        // Keep the final action clear of the card edge even when the menu list
        // or wrapped summary changes height.
        layout.padding = new RectOffset(18, 18, 16, 18);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        dayTitle = CreateText(panelRoot.transform, "DayTitle", "SHIFT PLAN", 26f,
            FontStyles.Bold, 32f, GameUITheme.Accent);
        TextMeshProUGUI intro = CreateText(panelRoot.transform, "Intro",
            "Review demand, tune the kitchen, then start the shift.",
            14f, FontStyles.Normal, 28f, GameUITheme.TextPrimary);
        intro.textWrappingMode = TextWrappingModes.Normal;

        shiftDurationText = CreateText(panelRoot.transform, "ShiftDuration",
            "SHIFT LENGTH  10:00 AM–10:00 PM  |  12 MINUTES AT 1x",
            14f, FontStyles.Bold, 20f, GameUITheme.TextSecondary);

        CreateText(panelRoot.transform, "DemandHeading", "MENU DEMAND", 14f,
            FontStyles.Bold, 20f, GameUITheme.Accent);
        demandHeaderRow = CreateDemandHeader(panelRoot.transform);
        demandEmptyText = CreateText(panelRoot.transform, "DemandEmptyMessage", "Loading demand...", 14f,
            FontStyles.Normal, 32f, GameUITheme.TextPrimary);
        demandEmptyText.textWrappingMode = TextWrappingModes.Normal;
        demandEmptyText.gameObject.SetActive(false);
        demandRowsRoot = CreateDemandRowsRoot(panelRoot.transform);
        demandRowsLayout = demandRowsRoot.GetComponent<LayoutElement>();
        EnsureDemandRowCapacity(11);
        addMenuItemButton = CreateCompactButton(panelRoot.transform, "AddMenuItemButton", "+ ADD ITEM TO MENU",
            34f, 12f, GameUITheme.Positive);
        addMenuItemButton.onClick.AddListener(() => ShowItemPicker(null));
        CreateItemPicker(panelRoot.transform);

        // Reserve a footer in the vertical layout while positioning the action
        // against the card bottom. The button therefore stays put as menu rows grow.
        var startShiftFooter = new GameObject("StartShiftFooterSpace", typeof(RectTransform),
            typeof(LayoutElement));
        startShiftFooter.transform.SetParent(panelRoot.transform, false);
        LayoutElement footerSize = startShiftFooter.GetComponent<LayoutElement>();
        footerSize.minHeight = footerSize.preferredHeight = 54f;
        startShiftButton = CreateButton(panelRoot.transform, "StartShiftButton", "START SHIFT");
        LayoutElement startShiftLayout = startShiftButton.GetComponent<LayoutElement>();
        startShiftLayout.ignoreLayout = true;
        RectTransform startShiftRect = (RectTransform)startShiftButton.transform;
        startShiftRect.anchorMin = new Vector2(0f, 0f);
        startShiftRect.anchorMax = new Vector2(1f, 0f);
        startShiftRect.pivot = new Vector2(0.5f, 0f);
        startShiftRect.offsetMin = new Vector2(18f, 18f);
        startShiftRect.offsetMax = new Vector2(-18f, 66f);
        startShiftButton.onClick.AddListener(StartShift);
        panelRoot.SetActive(false);
        EnsureLivePanel(canvas);
        EnsureStartShiftWarningDialog(canvas);
        FitPlanPanel();
    }

    void EnsureStartShiftWarningDialog(Canvas canvas)
    {
        if (startShiftWarningOverlay != null || canvas == null) return;

        startShiftWarningOverlay = new GameObject("StartShiftWarningOverlay",
            typeof(RectTransform), typeof(Image));
        startShiftWarningOverlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRect = (RectTransform)startShiftWarningOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayImage = startShiftWarningOverlay.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.72f);
        overlayImage.raycastTarget = true;

        GameObject card = new GameObject("WarningCard",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(startShiftWarningOverlay.transform, false);
        startShiftWarningCardRect = (RectTransform)card.transform;
        startShiftWarningCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        startShiftWarningCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        startShiftWarningCardRect.pivot = new Vector2(0.5f, 0.5f);
        startShiftWarningCardRect.sizeDelta = new Vector2(620f, 500f);
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = GameUITheme.Panel;
        cardImage.raycastTarget = true;

        VerticalLayoutGroup cardLayout = card.GetComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(22, 22, 20, 20);
        cardLayout.spacing = 10f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(card.transform, "WarningTitle",
            "SHIFT READINESS WARNING", 23f, FontStyles.Bold, 32f, GameUITheme.Accent);
        title.alignment = TextAlignmentOptions.Center;

        TextMeshProUGUI message = CreateText(card.transform, "WarningMessage",
            "The plan may not cover menu demand. Review output rates and ingredient stock. Customers may wait or leave, and missed orders can reduce item reputation. Start anyway?",
            15f, FontStyles.Normal, 58f, GameUITheme.TextPrimary);
        message.textWrappingMode = TextWrappingModes.Normal;

        startShiftWarningDetails = CreateText(card.transform, "WarningItemRates",
            string.Empty, 15f, FontStyles.Bold, 100f, GameUITheme.TextPrimary);
        startShiftWarningDetails.textWrappingMode = TextWrappingModes.Normal;
        startShiftWarningDetails.enableAutoSizing = true;
        startShiftWarningDetails.fontSizeMin = 12f;
        startShiftWarningDetails.fontSizeMax = 15f;
        startShiftWarningDetails.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject actions = new GameObject("WarningActions", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        actions.transform.SetParent(card.transform, false);
        LayoutElement actionsSize = actions.GetComponent<LayoutElement>();
        actionsSize.minHeight = actionsSize.preferredHeight = 48f;
        actionsSize.flexibleHeight = 0f;
        HorizontalLayoutGroup actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 10f;
        actionsLayout.childAlignment = TextAnchor.MiddleCenter;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;

        Button reviewButton = CreateCompactButton(actions.transform, "ReviewPlanButton",
            "REVIEW PLAN", 46f, 12f, GameUITheme.Charcoal);
        Button continueButton = CreateCompactButton(actions.transform, "StartAnywayButton",
            "START SHIFT ANYWAY", 46f, 12f, GameUITheme.Positive);
        foreach (Button button in new[] { reviewButton, continueButton })
        {
            LayoutElement buttonSize = button.GetComponent<LayoutElement>();
            buttonSize.minWidth = 0f;
            buttonSize.preferredWidth = 0f;
            buttonSize.flexibleWidth = 1f;
            buttonSize.flexibleHeight = 0f;
        }
        reviewButton.onClick.AddListener(CloseStartShiftWarning);
        continueButton.onClick.AddListener(ConfirmStartShiftAnyway);

        startShiftWarningOverlay.SetActive(false);
    }

    void EnsureLivePanel(Canvas canvas)
    {
        if (livePanelRoot != null || canvas == null) return;

        livePanelRoot = new GameObject("ShiftRunStatus", typeof(RectTransform), typeof(Image),
            typeof(Outline), typeof(VerticalLayoutGroup));
        livePanelRoot.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)livePanelRoot.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-20f, 20f);
        rect.sizeDelta = new Vector2(430f, 190f);

        Image background = livePanelRoot.GetComponent<Image>();
        background.color = new Color(GameUITheme.PanelSlate.r, GameUITheme.PanelSlate.g,
            GameUITheme.PanelSlate.b, 0.98f);
        background.raycastTarget = false;

        Outline border = livePanelRoot.GetComponent<Outline>();
        border.effectColor = Color.black;
        border.effectDistance = new Vector2(2f, -2f);
        border.useGraphicAlpha = false;

        VerticalLayoutGroup layout = livePanelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 7f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateText(livePanelRoot.transform, "RunHeading", "SHIFT IN PROGRESS", 15f,
            FontStyles.Bold, 24f, GameUITheme.Accent);
        liveStatsText = CreateText(livePanelRoot.transform, "KitchenProductionSummary",
            "Kitchen output: 0 items/min  |  Demand covered: 0%", 14f,
            FontStyles.Normal, 46f, GameUITheme.TextPrimary);
        liveStatsText.alignment = TextAlignmentOptions.TopLeft;
        liveStatsText.textWrappingMode = TextWrappingModes.Normal;
        liveStatsText.enableAutoSizing = true;
        liveStatsText.fontSizeMin = 12f;
        liveStatsText.fontSizeMax = 14f;
        liveItemRatesText = CreateText(livePanelRoot.transform, "ItemProductionRates",
            "Delivered to pickup: preparing...", 13f,
            FontStyles.Normal, 44f, GameUITheme.TextSecondary);
        liveItemRatesText.alignment = TextAlignmentOptions.TopLeft;
        liveItemRatesText.textWrappingMode = TextWrappingModes.Normal;
        liveItemRatesText.enableAutoSizing = true;
        liveItemRatesText.fontSizeMin = 11.5f;
        liveItemRatesText.fontSizeMax = 13f;
        liveDiagnosisText = CreateText(livePanelRoot.transform, "LiveDiagnosis", "Checking station activity...", 13f,
            FontStyles.Normal, 48f, GameUITheme.TextPrimary);
        liveDiagnosisText.alignment = TextAlignmentOptions.TopLeft;
        liveDiagnosisText.textWrappingMode = TextWrappingModes.Normal;
        liveDiagnosisText.enableAutoSizing = true;
        liveDiagnosisText.fontSizeMin = 11.5f;
        liveDiagnosisText.fontSizeMax = 13f;
        livePanelRoot.SetActive(false);
    }

    GameObject CreateDemandHeader(Transform parent)
    {
        GameObject row = CreateDemandColumnRow(parent, "DemandColumnHeader", 20f);
        CreateDemandCell(row.transform, "ItemHeader", "ITEM", 0f, true,
            12f, TextAlignmentOptions.MidlineLeft, GameUITheme.TextSecondary);
        CreateDemandCell(row.transform, "RateHeader", "ITEMS/MIN", 72f, false,
            11f, TextAlignmentOptions.MidlineRight, GameUITheme.TextSecondary);
        CreateDemandCell(row.transform, "TotalHeader", "SHIFT TOTAL", 86f, false,
            10f, TextAlignmentOptions.MidlineRight, GameUITheme.TextSecondary);
        return row;
    }

    GameObject CreateDemandRowsRoot(Transform parent)
    {
        GameObject root = new GameObject("DemandRows", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        root.transform.SetParent(parent, false);

        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 1f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        LayoutElement size = root.GetComponent<LayoutElement>();
        size.minHeight = 0f;
        size.preferredHeight = 0f;
        return root;
    }

    DemandRow CreateDemandRow(Transform parent, int index)
    {
        GameObject rowRoot = CreateDemandColumnRow(parent, "DemandRow_" + index, 52f);
        var previewFrame = new GameObject("ItemPreviewFrame", typeof(RectTransform), typeof(Image),
            typeof(Outline), typeof(LayoutElement));
        previewFrame.transform.SetParent(rowRoot.transform, false);
        LayoutElement previewSize = previewFrame.GetComponent<LayoutElement>();
        previewSize.minWidth = previewSize.preferredWidth = 44f;
        previewSize.minHeight = previewSize.preferredHeight = 44f;
        Image frameImage = previewFrame.GetComponent<Image>();
        frameImage.color = GameUITheme.Surface;
        frameImage.raycastTarget = false;
        ApplyWhiteButtonOutline(previewFrame.GetComponent<Outline>());

        var previewObject = new GameObject("ItemPreview", typeof(RectTransform), typeof(RawImage));
        previewObject.transform.SetParent(previewFrame.transform, false);
        RectTransform previewRect = (RectTransform)previewObject.transform;
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(2f, 2f);
        previewRect.offsetMax = new Vector2(-2f, -2f);
        RawImage preview = previewObject.GetComponent<RawImage>();
        preview.color = Color.white;
        preview.raycastTarget = false;

        var buttonGap = new GameObject("ChangeRemoveGap", typeof(RectTransform), typeof(LayoutElement));
        buttonGap.transform.SetParent(rowRoot.transform, false);
        LayoutElement buttonGapSize = buttonGap.GetComponent<LayoutElement>();
        buttonGapSize.minWidth = buttonGapSize.preferredWidth = 12f;
        buttonGapSize.flexibleWidth = 0f;

        DemandRow row = new DemandRow
        {
            root = rowRoot,
            preview = preview,
            itemName = CreateDemandCell(rowRoot.transform, "ItemName", string.Empty, 0f, true,
                15f, TextAlignmentOptions.MidlineLeft, GameUITheme.TextPrimary),
            changeButton = CreateCompactButton(rowRoot.transform, "ChangeItem", "CHANGE", 36f, 11f,
                GameUITheme.Surface),
            removeButton = CreateCompactButton(rowRoot.transform, "RemoveItem", "REMOVE", 36f, 11f,
                GameUITheme.Danger),
            rate = CreateDemandCell(rowRoot.transform, "Rate", string.Empty, 72f, false,
                15f, TextAlignmentOptions.MidlineRight, GameUITheme.TextPrimary),
            shiftTotal = CreateDemandCell(rowRoot.transform, "ShiftTotal", string.Empty, 86f, false,
                15f, TextAlignmentOptions.MidlineRight, GameUITheme.TextPrimary)
        };
        row.itemName.enableAutoSizing = true;
        row.itemName.fontSizeMin = 11f;
        row.itemName.fontSizeMax = 15f;
        buttonGap.transform.SetSiblingIndex(row.changeButton.transform.GetSiblingIndex() + 1);
        row.changeButton.onClick.AddListener(() => ShowItemPicker(row));
        row.removeButton.onClick.AddListener(() => RemoveMenuItem(row));
        rowRoot.SetActive(false);
        return row;
    }

    void CreateItemPicker(Transform parent)
    {
        itemPickerRoot = new GameObject("MenuItemPicker", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        itemPickerRoot.transform.SetParent(parent, false);
        itemPickerRoot.GetComponent<Image>().color = GameUITheme.Surface;
        itemPickerRoot.GetComponent<Image>().raycastTarget = true;
        VerticalLayoutGroup layout = itemPickerRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        itemPickerRoot.GetComponent<LayoutElement>().flexibleHeight = 0f;

        var headingRow = new GameObject("PickerHeading", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        headingRow.transform.SetParent(itemPickerRoot.transform, false);
        headingRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        HorizontalLayoutGroup headingLayout = headingRow.GetComponent<HorizontalLayoutGroup>();
        headingLayout.childControlWidth = true;
        headingLayout.childControlHeight = true;
        headingLayout.childForceExpandWidth = true;
        headingLayout.childForceExpandHeight = true;
        itemPickerTitle = CreateText(headingRow.transform, "PickerTitle", "CHOOSE ITEM", 12f,
            FontStyles.Bold, 28f, GameUITheme.Accent);
        Button cancel = CreateCompactButton(headingRow.transform, "CancelPicker", "CANCEL", 28f, 10f,
            GameUITheme.Danger);
        cancel.onClick.AddListener(CloseItemPicker);

        itemPickerRowsRoot = new GameObject("PickerItems", typeof(RectTransform), typeof(GridLayoutGroup),
            typeof(LayoutElement));
        itemPickerRowsRoot.transform.SetParent(itemPickerRoot.transform, false);
        GridLayoutGroup rowsLayout = itemPickerRowsRoot.GetComponent<GridLayoutGroup>();
        rowsLayout.cellSize = new Vector2(200f, 40f);
        rowsLayout.spacing = new Vector2(4f, 3f);
        rowsLayout.childAlignment = TextAnchor.UpperCenter;
        rowsLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        rowsLayout.constraintCount = 2;
        itemPickerRowsRoot.GetComponent<LayoutElement>().flexibleHeight = 0f;
        itemPickerEmptyText = CreateText(itemPickerRoot.transform, "PickerEmpty", "No other unlocked items are available.",
            13f, FontStyles.Normal, 28f, GameUITheme.TextSecondary);
        itemPickerEmptyText.gameObject.SetActive(false);
        itemPickerRoot.SetActive(false);
    }

    GameObject CreatePickerOption(ItemDefinition item)
    {
        Button button = CreateCompactButton(itemPickerRowsRoot.transform, "Option_" + item.name,
            item.itemName, 38f, 13f, GameUITheme.Charcoal);
        button.GetComponent<LayoutElement>().flexibleWidth = 1f;
        GameObject previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage),
            typeof(LayoutElement));
        previewObject.transform.SetParent(button.transform, false);
        RectTransform previewRect = (RectTransform)previewObject.transform;
        previewRect.anchorMin = new Vector2(0f, 0.5f);
        previewRect.anchorMax = new Vector2(0f, 0.5f);
        previewRect.pivot = new Vector2(0f, 0.5f);
        previewRect.anchoredPosition = new Vector2(6f, 0f);
        previewRect.sizeDelta = new Vector2(30f, 30f);
        RawImage preview = previewObject.GetComponent<RawImage>();
        preview.texture = GetMenuItemPreview(item);
        preview.raycastTarget = false;
        TextMeshProUGUI label = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(42f, 0f, 8f, 0f);
        ItemDefinition capturedItem = item;
        button.onClick.AddListener(() => SelectMenuItem(capturedItem));
        return button.gameObject;
    }

    static Button CreateCompactButton(Transform parent, string name, string label, float height,
        float fontSize, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement size = go.GetComponent<LayoutElement>();
        size.minHeight = size.preferredHeight = height;
        size.minWidth = 0f;
        size.preferredWidth = label == "REMOVE" || label == "CHANGE" ? 58f : 92f;
        size.flexibleWidth = 0f;
        Image image = go.GetComponent<Image>();
        image.color = color;
        ApplyWhiteButtonOutline(go.GetComponent<Outline>());
        Button button = go.GetComponent<Button>();
        GameUITheme.ApplyButton(button);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(go.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(3f, 0f);
        labelRect.offsetMax = new Vector2(-3f, 0f);
        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = GameUITheme.TextPrimary;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(8f, fontSize - 2f);
        text.fontSizeMax = fontSize;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return button;
    }

    Texture GetMenuItemPreview(ItemDefinition item)
    {
        if (item == null) return null;

        // Prefer an authored product icon when one is available. Generated prefab
        // thumbnails can be blank for menu products whose order preview is supplied
        // by the menu configuration instead of ItemDefinition.prefab.
        if (item.previewIcon != null)
            return item.previewIcon.texture;

        GameObject prefab = item.prefab;
        if (prefab == null)
        {
            IngredientsOrderUI menuUi = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
            CustomerOrderConfig config = ResolveOrderConfig();
            if (menuUi != null && config != null)
            {
                if (config.IsBurger(item)) prefab = menuUi.burgerPreviewPrefab;
                else if (config.IsFries(item)) prefab = menuUi.friesPreviewPrefab;
                else if (config.IsDrink(item)) prefab = menuUi.drinkPreviewPrefab;
            }
        }
        if (prefab != null)
        {
            Texture thumbnail = ItemPreviewThumbnails.GetPrefab(prefab, item.itemName);
            if (thumbnail != null) return thumbnail;
        }

        return ItemPreviewThumbnails.Get(item);
    }

    static GameObject CreateDemandColumnRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name, typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        LayoutElement size = row.GetComponent<LayoutElement>();
        size.minHeight = height;
        size.preferredHeight = height;
        return row;
    }

    static TextMeshProUGUI CreateDemandCell(Transform parent, string name, string value,
        float width, bool flexible, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        TextMeshProUGUI text = CreateText(parent, name, value, fontSize,
            name.EndsWith("Header") ? FontStyles.Bold : FontStyles.Normal, 22f, color);
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;

        LayoutElement size = text.GetComponent<LayoutElement>();
        size.minWidth = flexible ? 0f : width;
        size.preferredWidth = flexible ? 0f : width;
        size.flexibleWidth = flexible ? 1f : 0f;
        return text;
    }

    void RefreshPlan()
    {
        if (dayTitle != null)
            dayTitle.text = "DAY " + (timeManager != null ? timeManager.CurrentDay : 1) + "  |  PLAN";
        if (shiftDurationText != null)
            shiftDurationText.text = GetShiftDurationSummary();

        CustomerOrderConfig config = ResolveOrderConfig();
        goalItems.Clear();
        if (config == null)
        {
            lastPlanDemandRevision = -1;
            SetDemandEmptyState("Demand schedule is not ready yet.");
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
            if (item != null)
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

        EnsureDemandRowCapacity(goalItems.Count);
        if (goalItems.Count == 0)
        {
            SetDemandEmptyState("No items selected. Add items here to choose what customers can order.");
            if (addMenuItemButton != null) addMenuItemButton.interactable = config != null;
        }
        else
        {
            int visibleItems = goalItems.Count;
            for (int i = 0; i < visibleItems; i++)
            {
                ItemDefinition item = goalItems[i];
                float rate = config.GetCustomerDemandPerMinute(item);
                int shiftTotal = Mathf.RoundToInt(rate * GetShiftDurationMinutes());
                DemandRow row = demandRows[i];
                row.item = item;
                row.preview.texture = GetMenuItemPreview(item);
                row.preview.enabled = row.preview.texture != null;
                row.itemName.text = item.itemName;
                row.rate.text = rate.ToString("0.#") + "/min";
                row.shiftTotal.text = shiftTotal.ToString("N0");
                row.root.SetActive(true);
            }
            for (int i = visibleItems; i < demandRows.Count; i++)
            {
                demandRows[i].item = null;
                demandRows[i].root.SetActive(false);
            }

            if (demandEmptyText != null) demandEmptyText.gameObject.SetActive(false);
            if (demandHeaderRow != null) demandHeaderRow.SetActive(true);
            if (demandRowsRoot != null) demandRowsRoot.SetActive(true);
            if (addMenuItemButton != null) addMenuItemButton.interactable = true;
            SetDemandListHeight(visibleItems);
        }

        if (addMenuItemButton != null && itemPickerRoot != null && !itemPickerRoot.activeSelf)
            addMenuItemButton.gameObject.SetActive(true);
        if (startShiftButton != null)
            startShiftButton.interactable = OnboardingTutorial.CanStartShiftFromPlan
                && config.HasEnabledItems;
        FitPlanPanel();
    }

    void EnsureDemandRowCapacity(int count)
    {
        while (demandRows.Count < count)
            demandRows.Add(CreateDemandRow(demandRowsRoot.transform, demandRows.Count));
    }

    void ShowItemPicker(DemandRow targetRow)
    {
        if (itemPickerRoot == null || GameTimeManager.Instance != null
            && GameTimeManager.Instance.IsShiftInProgress)
            return;

        itemPickerTargetRow = targetRow;
        CustomerOrderConfig config = ResolveOrderConfig();
        if (itemPickerTitle != null)
            itemPickerTitle.text = targetRow != null ? "CHOOSE A REPLACEMENT ITEM" : "CHOOSE AN ITEM TO SELL";
        for (int i = itemPickerRowsRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(itemPickerRowsRoot.transform.GetChild(i).gameObject);
        itemPickerOptionButtons.Clear();

        int optionCount = 0;
        if (config != null)
        {
            foreach (ItemDefinition item in config.GetMenuItems())
            {
                if (item == null || item == (targetRow != null ? targetRow.item : null)
                    || config.IsItemEnabled(item) || !config.IsCustomerRecipeUnlocked(item))
                    continue;
                GameObject option = CreatePickerOption(item);
                itemPickerOptionButtons[item] = option.GetComponent<Button>();
                optionCount++;
            }
        }

        if (itemPickerEmptyText != null)
        {
            itemPickerEmptyText.text = config == null
                ? "Menu data is not ready yet."
                : "No other unlocked menu items are available.";
            itemPickerEmptyText.gameObject.SetActive(optionCount == 0);
        }
        LayoutElement rowsSize = itemPickerRowsRoot.GetComponent<LayoutElement>();
        if (rowsSize != null)
        {
            int optionRows = Mathf.CeilToInt(optionCount / 2f);
            rowsSize.minHeight = rowsSize.preferredHeight = optionRows * 43f;
        }

        if (demandHeaderRow != null) demandHeaderRow.SetActive(false);
        if (demandRowsRoot != null) demandRowsRoot.SetActive(false);
        if (demandEmptyText != null) demandEmptyText.gameObject.SetActive(false);
        if (addMenuItemButton != null) addMenuItemButton.gameObject.SetActive(false);
        itemPickerRoot.SetActive(true);
        FitPlanPanel();
    }

    void CloseItemPicker()
    {
        if (itemPickerRoot != null) itemPickerRoot.SetActive(false);
        itemPickerTargetRow = null;
        RefreshPlan();
    }

    void SelectMenuItem(ItemDefinition item)
    {
        if (item == null || GameTimeManager.Instance != null
            && GameTimeManager.Instance.IsShiftInProgress)
            return;
        CustomerOrderConfig config = ResolveOrderConfig();
        if (config == null) return;

        if (itemPickerTargetRow != null && itemPickerTargetRow.item != null)
            config.SetItemEnabled(itemPickerTargetRow.item, false);
        config.SetItemEnabled(item, true);
        selectedGoalItem = item;
        selectedGoalItemName = item.itemName;
        GameSaveSlots.SaveEnabledMenuSelection(config);
        lastPlanDemandRevision = int.MinValue;
        CloseItemPicker();
    }

    void RemoveMenuItem(DemandRow row)
    {
        if (row == null || row.item == null || GameTimeManager.Instance != null
            && GameTimeManager.Instance.IsShiftInProgress)
            return;
        CustomerOrderConfig config = ResolveOrderConfig();
        if (config == null) return;
        config.SetItemEnabled(row.item, false);
        GameSaveSlots.SaveEnabledMenuSelection(config);
        lastPlanDemandRevision = int.MinValue;
        RefreshPlan();
    }

    float GetShiftDurationMinutes()
    {
        if (timeManager == null) return 12f;
        float shiftHours = Mathf.Max(0f, timeManager.dayEndHour - timeManager.dayStartHour);
        return shiftHours * Mathf.Max(1f, timeManager.realSecondsPerGameHour) / 60f;
    }

    string GetShiftDurationSummary()
    {
        if (timeManager == null)
            return "SHIFT LENGTH  12 MINUTES AT 1x";

        float durationMinutes = GetShiftDurationMinutes();
        return "SHIFT LENGTH  " + FormatShiftHour(timeManager.dayStartHour)
            + "–" + FormatShiftHour(timeManager.dayEndHour)
            + "  |  " + durationMinutes.ToString("0.#") + " MINUTES AT 1x";
    }

    static string FormatShiftHour(int hour24)
    {
        int hour = ((hour24 % 24) + 24) % 24;
        int hour12 = hour % 12;
        if (hour12 == 0) hour12 = 12;
        return hour12 + ":00 " + (hour < 12 ? "AM" : "PM");
    }

    void SetDemandEmptyState(string message)
    {
        if (demandEmptyText != null)
        {
            demandEmptyText.text = message;
            demandEmptyText.gameObject.SetActive(true);
        }
        if (demandHeaderRow != null) demandHeaderRow.SetActive(false);
        if (demandRowsRoot != null) demandRowsRoot.SetActive(false);
        if (addMenuItemButton != null)
        {
            addMenuItemButton.gameObject.SetActive(true);
            addMenuItemButton.interactable = ResolveOrderConfig() != null;
        }
    }

    void SetDemandListHeight(int rowCount)
    {
        if (demandRowsLayout == null) return;
        float height = rowCount * 52f;
        demandRowsLayout.minHeight = height;
        demandRowsLayout.preferredHeight = height;
    }

    void FitPlanPanel()
    {
        if (panelRoot == null) return;
        RectTransform rect = panelRoot.transform as RectTransform;
        RectTransform canvasRect = panelRoot.transform.parent as RectTransform;
        if (rect == null) return;

        VerticalLayoutGroup layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        float preferredHeight = layout != null ? layout.padding.vertical : 0f;
        int activeChildCount = 0;
        for (int i = 0; i < panelRoot.transform.childCount; i++)
        {
            RectTransform child = panelRoot.transform.GetChild(i) as RectTransform;
            if (child != null && child.gameObject.activeSelf)
            {
                LayoutElement childLayout = child.GetComponent<LayoutElement>();
                if (childLayout != null && childLayout.ignoreLayout)
                    continue;
                preferredHeight += LayoutUtility.GetPreferredHeight(child);
                activeChildCount++;
            }
        }
        if (layout != null)
            preferredHeight += layout.spacing * Mathf.Max(0, activeChildCount - 1);
        // Keep a small inset below Start Shift. Hidden UI elements do not add
        // phantom spacing to the plan card's measured height.
        float desiredHeight = Mathf.Max(360f, preferredHeight + 16f);
        float availableHeight = canvasRect != null
            ? Mathf.Max(240f, canvasRect.rect.height - 40f)
            : Mathf.Max(240f, Screen.height - 40f);
        float width = canvasRect != null
            ? Mathf.Min(520f, Mathf.Max(320f, canvasRect.rect.width - 40f))
            : Mathf.Min(520f, Mathf.Max(320f, Screen.width - 40f));

        rect.sizeDelta = new Vector2(width, desiredHeight);
        rect.localScale = desiredHeight > availableHeight
            ? Vector3.one * (availableHeight / desiredHeight) : Vector3.one;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    void StartShift()
    {
        if (!OnboardingTutorial.CanStartShiftFromPlan) return;
        if (TryGetStartShiftWarnings(out List<string> warnings)
            && ShowStartShiftWarning(warnings)) return;
        BeginShift();
    }

    bool TryGetStartShiftWarnings(out List<string> warnings)
    {
        warnings = new List<string>();
        CustomerOrderConfig config = ResolveOrderConfig();
        ProductionManager production = ProductionManager.Instance;
        if (config == null) return false;

        float shiftMinutes = GetShiftDurationMinutes();
        var requiredIngredients = new Dictionary<ItemDefinition, float>();
        foreach (ItemDefinition item in config.GetEnabledMenuItems())
        {
            if (item == null) continue;
            float demand = config.GetCustomerDemandPerMinute(item);
            float predicted = production != null
                ? production.GetPredictedOutputPerMinute(item) : 0f;
            if (predicted + 0.01f < demand)
            {
                string predictedText = predicted <= 0.01f
                    ? "no output predicted"
                    : predicted.ToString("0.#") + "/min predicted";
                warnings.Add("OUTPUT SHORTFALL: " + item.itemName + ": " + predictedText
                    + "; " + demand.ToString("0.#") + "/min demand");
            }

            float shiftDemand = demand * shiftMinutes;
            AccumulateVendorIngredientNeeds(config, item, shiftDemand,
                requiredIngredients, new HashSet<ItemDefinition>());
        }

        KitchenInventory inventory = KitchenInventory.Instance;
        if (inventory != null)
        {
            bool supplyRatesActive = MilestoneFeatures.IsResearchComplete("automatic_ordering");
            foreach (KeyValuePair<ItemDefinition, float> requirement in requiredIngredients)
            {
                ItemDefinition ingredient = requirement.Key;
                if (ingredient == null) continue;
                int needed = Mathf.CeilToInt(requirement.Value);
                int onHand = inventory.GetCount(ingredient);
                int supplyRate = supplyRatesActive
                    ? inventory.GetSupplyRatePerMinute(ingredient) : 0;
                float expectedSupply = supplyRate * shiftMinutes;
                // Cap cumulative receipts by free storage plus items consumed during the shift.
                expectedSupply = Mathf.Min(expectedSupply,
                    Mathf.Max(0, KitchenInventory.MaxIngredientStock - onHand + needed));

                bool noStock = onHand <= 0;
                bool lowForShift = onHand < needed;
                if (!noStock && !lowForShift) continue;

                string stockState = noStock ? "NO STOCK" : "LOW STOCK";
                string line = stockState + ": " + ingredient.itemName + " "
                    + onHand.ToString("N0") + "/" + needed.ToString("N0") + " needed for shift";
                if (expectedSupply > 0.01f)
                    line += "; +" + expectedSupply.ToString("0.#") + " scheduled supply";
                warnings.Add(line);
            }
        }

        return warnings.Count > 0;
    }

    static void AccumulateVendorIngredientNeeds(CustomerOrderConfig config, ItemDefinition output,
        float outputUnits, Dictionary<ItemDefinition, float> requiredIngredients,
        HashSet<ItemDefinition> path)
    {
        if (config == null || output == null || outputUnits <= 0f) return;
        if (config.GetVendorSupplyStation(output).HasValue)
        {
            requiredIngredients[output] = requiredIngredients.TryGetValue(output, out float current)
                ? current + outputUnits : outputUnits;
            return;
        }
        if (!path.Add(output)) return;

        IReadOnlyList<RecipeGraphNodeDefinition> recipes = config.GetProductionRecipes();
        RecipeGraphNodeDefinition producer = null;
        for (int i = 0; recipes != null && i < recipes.Count; i++)
        {
            RecipeGraphNodeDefinition candidate = recipes[i];
            if (candidate != null && candidate.output == output)
            {
                producer = candidate;
                break;
            }
        }
        if (producer?.inputs != null)
        {
            foreach (RecipeGraphInputDefinition input in producer.inputs)
                if (input != null && input.item != null)
                    AccumulateVendorIngredientNeeds(config, input.item,
                        outputUnits * Mathf.Max(1, input.amount), requiredIngredients, path);
        }

        path.Remove(output);
    }

    bool ShowStartShiftWarning(List<string> shortfalls)
    {
        if (startShiftWarningOverlay == null)
            EnsureStartShiftWarningDialog(ResolvePlayerUICanvas());
        if (startShiftWarningOverlay == null || startShiftWarningDetails == null) return false;

        var details = new StringBuilder();
        for (int i = 0; i < shortfalls.Count; i++)
        {
            if (i > 0) details.Append('\n');
            details.Append("- ").Append(shortfalls[i]);
        }
        startShiftWarningDetails.text = details.ToString();
        LayoutElement detailsSize = startShiftWarningDetails.GetComponent<LayoutElement>();
        if (detailsSize != null)
        {
            float detailHeight = Mathf.Clamp(24f + shortfalls.Count * 16f, 46f, 320f);
            detailsSize.minHeight = detailsSize.preferredHeight = detailHeight;
        }

        Canvas canvas = startShiftWarningOverlay.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect != null && startShiftWarningCardRect != null)
        {
            float width = Mathf.Min(620f, Mathf.Max(320f, canvasRect.rect.width - 48f));
            float desiredHeight = 208f + (detailsSize != null ? detailsSize.preferredHeight : 100f);
            float availableHeight = Mathf.Max(280f, canvasRect.rect.height - 48f);
            float height = Mathf.Min(desiredHeight, availableHeight);
            startShiftWarningCardRect.sizeDelta = new Vector2(width, height);
        }

        startShiftWarningOverlay.SetActive(true);
        startShiftWarningOverlay.transform.SetAsLastSibling();
        Sfx.Play(SfxId.UiOpen);
        return true;
    }

    void CloseStartShiftWarning()
    {
        if (startShiftWarningOverlay != null)
            startShiftWarningOverlay.SetActive(false);
        Sfx.Play(SfxId.UiClick);
    }

    void ConfirmStartShiftAnyway()
    {
        if (!OnboardingTutorial.CanStartShiftFromPlan) return;
        if (startShiftWarningOverlay != null)
            startShiftWarningOverlay.SetActive(false);
        BeginShift();
    }

    void BeginShift()
    {
        if (!OnboardingTutorial.CanStartShiftFromPlan) return;
        preShiftPlanningActive = false;
        if (OnboardingTutorial.IsActive && timeManager != null)
            timeManager.BeginTutorialPracticeShift();
        CustomerSpawner.Instance?.BeginPlannedShiftDemandCycle();
        ProductionManager.Instance?.BeginShiftThroughputTracking();
        StoreStatisticsManager.Instance?.BeginShiftThroughputTracking();
        CaptureStationBaseline();
        planHiddenForStationPopup = false;
        if (panelRoot != null) panelRoot.SetActive(false);
        ShowLivePanel();
        // The planning/tutorial overlays can leave the primary navigation behind
        // another HUD layer. Restore it after the live status panel is shown so
        // Build and Business remain available/visible during the shift.
        EnsureMainHudNavigation();
        if (timeManager != null) timeManager.ReleaseExternalPause(PauseSource);
        Sfx.Play(SfxId.UiClick);
    }

    static void ResetWorkersForPlanning()
    {
        KitchenEmployee[] workers = FindObjectsByType<KitchenEmployee>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (KitchenEmployee worker in workers)
            if (worker != null)
                worker.AbortCurrentWork();
    }

    public static void SetStationPopupOpen(bool isOpen)
    {
        ShiftPlanningUI planning = Instance;
        if (planning == null || planning.panelRoot == null) return;

        if (isOpen)
        {
            if (!planning.panelRoot.activeSelf) return;
            if (OnboardingTutorial.ShouldShowPlanCardDuringTutorial)
            {
                planning.planHiddenForStationPopup = false;
                return;
            }
            planning.planHiddenForStationPopup = true;
            planning.panelRoot.SetActive(false);
            return;
        }

        if (!planning.planHiddenForStationPopup) return;
        planning.planHiddenForStationPopup = false;
        if (planning.timeManager == null || planning.timeManager.IsShiftOver
            || !planning.initialized)
            return;

        planning.ShowAndFitPlanningPanel();
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
        itemLines.Append("<b><color=#D8B365>DELIVERED TO PICKUP</color></b>  <color=#B7BEC5>ITEMS/MIN</color>");
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
            itemLines.Append('\n').Append(item.itemName).Append("  <b>")
                .Append(deliveredRate.ToString("0.0")).Append("</b> <color=#B7BEC5>/ ")
                .Append(targetRate.ToString("0.#")).Append("</color>");
        }
        if (items.Count == 0)
            itemLines.Append("\nNo enabled menu items.");

        float demandCoverage = totalTargetRate > 0f
            ? coveredTargetRate / totalTargetRate * 100f : 0f;
        StoreStatisticsManager stats = StoreStatisticsManager.Instance;
        if (liveStatsText != null)
            liveStatsText.text = "<b><color=#D8B365>OUTPUT</color></b>  "
                + totalDeliveredRate.ToString("0.0") + " items/min   <color=#B7BEC5>|</color>   "
                + "<b><color=#D8B365>DEMAND COVERED</color></b>  " + demandCoverage.ToString("0") + "%"
                + "\n<b><color=#D8B365>ORDERS</color></b>  "
                + (stats != null ? stats.ThroughputOrdersPerMinute : 0f).ToString("0.0")
                + "/min   <color=#B7BEC5>|</color>   "
                + "<b><color=#D8B365>AVERAGE WAIT</color></b>  "
                + (stats != null ? stats.AverageWaitTimeTodaySeconds : 0f).ToString("0") + "s";
        if (liveItemRatesText != null)
        {
            liveItemRatesText.text = itemLines.ToString();
            LayoutElement itemListSize = liveItemRatesText.GetComponent<LayoutElement>();
            if (itemListSize != null)
            {
                float itemListHeight = 24f + Mathf.Max(1, items.Count) * 18f;
                itemListSize.minHeight = itemListHeight;
                itemListSize.preferredHeight = itemListHeight;
            }
        }
        RectTransform panelRect = livePanelRoot.GetComponent<RectTransform>();
        if (panelRect != null)
            panelRect.sizeDelta = new Vector2(430f, Mathf.Clamp(212f + items.Count * 22f, 232f, 460f));

        string diagnosis = BuildShiftProductionDiagnosis();
        if (liveDiagnosisText != null)
            liveDiagnosisText.text = string.IsNullOrEmpty(diagnosis)
                ? "<b><color=#6F9D89>STATUS</color></b>  No major station delay detected."
                : "<b><color=#D8B365>ATTENTION</color></b>  " + diagnosis;

    }
    void CaptureStationBaseline()
    {
        stationBaseline.Clear();
        hasSavedStationBaseline = false;
        StationNode[] nodes = FindObjectsByType<StationNode>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (StationNode node in nodes)
        {
            if (node == null) continue;
            StationRuntimeMetrics metrics = StationRuntimeMetrics.EnsureOn(node.gameObject);
            if (metrics == null) continue;
            string stationGuid = node.StableStationId;
            if (string.IsNullOrEmpty(stationGuid)) continue;
            stationBaseline[stationGuid] = new StationSnapshot
            {
                working = metrics.WorkingSeconds,
                starved = metrics.StarvedSeconds,
                blocked = metrics.BlockedSeconds
            };
        }
        hasSavedStationBaseline = stationBaseline.Count > 0;
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
        var state = new SavedState
        {
            selectedGoalItemName = selectedGoalItemName,
            latestShift = CloneResult(latestShift),
            previousShift = CloneResult(previousShift)
        };
        foreach (KeyValuePair<string, StationSnapshot> entry in stationBaseline)
            state.stationBaselines.Add(new StationBaselineRecord
            {
                stationGuid = entry.Key,
                working = entry.Value.working,
                starved = entry.Value.starved,
                blocked = entry.Value.blocked
            });
        return state;
    }

    void ApplySaveState(SavedState state)
    {
        selectedGoalItemName = state != null ? state.selectedGoalItemName : string.Empty;
        latestShift = state != null ? CloneResult(state.latestShift) : null;
        previousShift = state != null ? CloneResult(state.previousShift) : null;
        stationBaseline.Clear();
        if (state != null && state.stationBaselines != null)
        {
            foreach (StationBaselineRecord saved in state.stationBaselines)
            {
                if (saved == null || string.IsNullOrEmpty(saved.stationGuid)) continue;
                stationBaseline[saved.stationGuid] = new StationSnapshot
                {
                    working = Mathf.Max(0f, saved.working),
                    starved = Mathf.Max(0f, saved.starved),
                    blocked = Mathf.Max(0f, saved.blocked)
                };
            }
        }
        hasSavedStationBaseline = stationBaseline.Count > 0;
        selectedGoalItem = null;
        hasPendingRestoreState = false;
    }

    static SavedState CloneSavedState(SavedState state)
    {
        if (state == null) return null;
        var clone = new SavedState
        {
            selectedGoalItemName = state.selectedGoalItemName,
            latestShift = CloneResult(state.latestShift),
            previousShift = CloneResult(state.previousShift),
            stationBaselines = state.stationBaselines != null
                ? new List<StationBaselineRecord>(state.stationBaselines.Count)
                : new List<StationBaselineRecord>()
        };
        if (state.stationBaselines != null)
            foreach (StationBaselineRecord baseline in state.stationBaselines)
                if (baseline != null)
                    clone.stationBaselines.Add(new StationBaselineRecord
                    {
                        stationGuid = baseline.stationGuid,
                        working = baseline.working,
                        starved = baseline.starved,
                        blocked = baseline.blocked
                    });
        return clone;
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

            string stationGuid = node.StableStationId;
            if (string.IsNullOrEmpty(stationGuid)) continue;
            stationBaseline.TryGetValue(stationGuid, out StationSnapshot start);
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
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement size = go.GetComponent<LayoutElement>();
        size.minHeight = 48f;
        size.preferredHeight = 48f;
        Image image = go.GetComponent<Image>();
        image.color = GameUITheme.Positive;
        ApplyWhiteButtonOutline(go.GetComponent<Outline>());
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

    static void ApplyWhiteButtonOutline(Outline outline)
    {
        if (outline == null) return;
        outline.effectColor = new Color(1f, 1f, 1f, 0.9f);
        outline.effectDistance = new Vector2(1.25f, -1.25f);
        outline.useGraphicAlpha = false;
    }
}
