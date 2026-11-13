using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;

/// <summary>
/// Guided first-run tour: diagnose and improve an inherited burger kitchen.
/// </summary>
public class OnboardingTutorial : MonoBehaviour
{
    public const string PrefsCompleteKey = "TIEACH_OnboardingComplete";
    public const string PauseSource = "OnboardingTutorial";
    const string CustomerJourneyPauseSource = "OnboardingTutorial.CustomerJourney";
    const int AnalyticsTourCloseStage = 12;
    const int OnboardingStepCount = 6;
    const int MilestoneOneBottleneckStep = 6;
    const int MilestoneOneAnalyticsStep = 7;

    public static OnboardingTutorial Instance { get; private set; }

    public static bool IsComplete => PlayerPrefs.GetInt(PrefsCompleteKey, 0) == 1;
    public static bool IsActive => Instance != null && Instance.running
        && !Instance.milestoneOneLessonActive;
    public static bool CanStartShiftFromPlan => !IsActive
        || (Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireShiftStarted && !Instance.tutorialShiftStarted);
    public static bool BlocksAutoCustomers => IsActive || !IsComplete;
    public static bool AllowsPracticeCustomer => IsActive && Steps[Instance.stepIndex].liveCustomer;
    public static bool BlocksProgression => IsActive || (!IsComplete && Instance != null && Instance.pendingStart);
    public static int StationUnlockProgress => BlocksProgression ? Instance.furthestStepIndex : int.MaxValue;
    public static bool IsResearchTutorialActive => IsCurrentStep(step => step.requireResearch);
    public static bool IsRecipeTutorialActive => IsCurrentStep(step => step.requireRecipeScreen);
    public static bool IsAnalyticsTutorialActive => IsCurrentStep(step => step.requireAnalyticsScreen)
        || Instance != null && Instance.milestoneOneLessonActive
            && Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireAnalyticsScreen;
    public static bool IsMenuTutorialActive => IsCurrentStep(step => step.requireMenuScreen);
    public static bool IsObjectivesTutorialActive => IsCurrentStep(step => step.requireObjectivesScreen);
    public static bool IsTimeControlTutorialActive => IsCurrentStep(step => step.requireTimeControlInteractions);
    public static bool ShouldShowPlanCardDuringTutorial => IsActive
        && (Instance.planScreenIntroduced
            || IsCurrentStep(step => step.requireHamburger || step.liveCustomer || step.requireShiftStarted));

    public static bool TryGetCurrentExpectedFlowLink(ProductionFlowPlan flow,
        out GameObject source, out GameObject destination)
    {
        source = null;
        destination = null;
        if (!IsCurrentStep(step => step.requireFlow && !step.requireEditedFlow) || flow == null)
            return false;

        var expected = GetExpectedFlowLink(flow);
        source = expected.source != null ? expected.source.gameObject : null;
        destination = expected.destination != null ? expected.destination.gameObject : null;
        return source != null && destination != null;
    }

    static bool IsCurrentStep(System.Predicate<Step> predicate)
    {
        return Instance != null && Instance.running && Instance.stepIndex >= 0
            && Instance.stepIndex < Steps.Length && predicate(Steps[Instance.stepIndex]);
    }

    public static bool IsTutorialResearchRoot(string id)
    {
        return id == "production_basics" || id == "core_workforce" || id == "manual_operations";
    }

    public static bool IsTutorialResearchComplete()
    {
        ResearchProgressManager research = ResearchProgressManager.Instance;
        return research != null && research.IsCompleted("production_basics")
            && research.IsCompleted("core_workforce")
            && research.IsCompleted("manual_operations");
    }

    public static int TutorialResearchCategory
    {
        get
        {
            ResearchProgressManager research = ResearchProgressManager.Instance;
            if (research == null || !research.IsCompleted("production_basics")) return 0;
            if (!research.IsCompleted("core_workforce")) return 1;
            return 2;
        }
    }

    public static void NotifyResearchScreenOpened()
    {
        if (IsResearchTutorialActive) Instance.researchScreenOpened = true;
    }

    public static void NotifyRecipeScreenOpened()
    {
        if (IsRecipeTutorialActive) Instance.recipeScreenOpened = true;
    }

    public static void NotifyAnalyticsScreenOpened()
    {
        if (!IsAnalyticsTutorialActive) return;
        Instance.analyticsScreenOpened = true;
        Instance.analyticsScreenClosed = false;
        if (Instance.analyticsTutorialStage < 0)
            Instance.analyticsTutorialStage = 0;
        Instance.SetTutorialBodyText(Instance.GetAnalyticsTourInstruction());
        Instance.nextControlHighlightRefresh = 0f;
        Instance.RefreshAdvanceGate();
        Instance.RefreshControlHighlights();
        Instance.UpdateContextualCallout();
    }

    public static void NotifyAnalyticsScreenClosed()
    {
        if (!IsAnalyticsTutorialActive || !Instance.analyticsScreenOpened) return;
        Instance.analyticsScreenClosed = true;
        Instance.SetTutorialBodyText(Instance.analyticsTutorialStage >= AnalyticsTourCloseStage
            ? "Analytics review complete. Click <b>Finish</b> to end the tutorial."
            : "Open Analytics again to continue the guided review.");
        Instance.nextControlHighlightRefresh = 0f;
        Instance.RefreshAdvanceGate();
        Instance.RefreshControlHighlights();
        Instance.UpdateContextualCallout();
    }

    public static void NotifyMenuScreenOpened()
    {
        if (IsMenuTutorialActive) Instance.menuScreenOpened = true;
    }

    public static void NotifyHamburgerAddedToPlan(ItemDefinition item)
    {
        if (!IsCurrentStep(step => step.requireHamburger) || item == null) return;
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (config == null || item != config.burgerBase) return;

        Instance.tutorialVisibleCharacters = Instance.tutorialTotalCharacters;
        if (Instance.bodyText != null)
            Instance.bodyText.maxVisibleCharacters = Instance.tutorialVisibleCharacters;
        Instance.autoAdvanceStep = -1;
        Instance.stepIndex++;
        Instance.ShowStep();
    }

    public static void NotifyObjectivesScreenOpened()
    {
        if (!IsObjectivesTutorialActive) return;
        Instance.objectivesScreenOpened = true;
        Instance.SetTutorialBodyText(Steps[Instance.stepIndex].body);
        Instance.RefreshAdvanceGate();
        Instance.nextControlHighlightRefresh = 0f;
        Instance.RefreshControlHighlights();
        Instance.UpdateContextualCallout();
    }

    public static void NotifyGlossaryTermClicked(string term)
    {
        if (!IsCurrentStep(step => step.requireGlossaryTermClick)
            || string.IsNullOrWhiteSpace(term))
            return;

        Instance.glossaryTermClicked = true;
        Instance.RefreshAdvanceGate();
    }

    public static void NotifyTutorialPauseButtonClicked()
    {
        if (!IsTimeControlTutorialActive) return;
        Instance.tutorialPauseControlUsed = true;
        Instance.RefreshTimeControlTutorialStep();
        Instance.RefreshAdvanceGate();
    }

    public static void NotifyTutorialPlayButtonClicked()
    {
        if (!IsTimeControlTutorialActive || !Instance.tutorialPauseControlUsed) return;
        Instance.tutorialPlayControlUsed = true;
        Instance.RefreshTimeControlTutorialStep();
        Instance.RefreshAdvanceGate();
    }

    public static void NotifyTutorialFastForwardButtonClicked()
    {
        if (!IsTimeControlTutorialActive || !Instance.tutorialPauseControlUsed
            || !Instance.tutorialPlayControlUsed
            || GameTimeManager.Instance == null
            || GameTimeManager.Instance.CurrentSpeed != GameTimeManager.SpeedMode.FastForward)
            return;
        Instance.tutorialFastForwardControlUsed = true;
        Instance.RecordTutorialFastForwardRate(GameTimeManager.Instance.SelectedFastForwardRateIndex);
        Instance.RefreshTimeControlTutorialStep();
        Instance.RefreshAdvanceGate();
    }

    public static void NotifyTutorialFastForwardRateSelected()
    {
        if (!IsTimeControlTutorialActive || !Instance.tutorialFastForwardControlUsed
            || GameTimeManager.Instance == null
            || GameTimeManager.Instance.CurrentSpeed != GameTimeManager.SpeedMode.FastForward)
            return;
        Instance.RecordTutorialFastForwardRate(GameTimeManager.Instance.SelectedFastForwardRateIndex);
        Instance.RefreshTimeControlTutorialStep();
        Instance.RefreshAdvanceGate();
    }

    public static void NotifyTutorialShiftStarted()
    {
        if (!IsCurrentStep(step => step.requireShiftStarted)) return;
        Instance.tutorialShiftStarted = true;
        Instance.RefreshAdvanceGate();
    }

    static Transform FindHudCanvas()
    {
        Canvas canvas = null;
        TopHudBar hud = FindFirstObjectByType<TopHudBar>(FindObjectsInactive.Include);
        if (hud != null) canvas = hud.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            if (tabs != null) canvas = tabs.GetComponentInParent<Canvas>();
        }
        if (canvas == null)
        {
            ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null) canvas = management.GetComponentInParent<Canvas>();
        }
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        return canvas != null ? (canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform) : null;
    }

    bool running;
    bool pendingStart;
    bool gameplayReady;
    bool liveCustomerSpawned;
    bool milestoneOneLessonActive;
    bool milestoneOneBottleneckLessonDone;
    bool milestoneOneAnalyticsLessonDone;
    GameObject lastStationHealthLessonTarget;
    bool milestoneOneStationClickObserved;
    int stationHealthLessonSelectionRevision;
    bool burgerArrivedAtPickup;
    bool customerJourneyComplete;
    bool tutorialCustomerOrderServed;
    int customerJourneyStage = -1;
    CustomerAI tutorialCustomer;
    PlayerCameraController tutorialFollowCamera;
    readonly Queue<string> customerJourneyCardQueue = new Queue<string>();
    bool customerJourneyCustomerGone;
    bool customerJourneyFinalMessageDisplayed;
    bool customerJourneyActionMessageActive;
    float customerJourneyMessageHoldUntil;
    bool planScreenIntroduced;
    bool tutorialBurgerUnlockShown;
    bool tutorialUnlockContinuePending;
    bool starterKitchenHidden;
    bool inheritedKitchenPrepared;
    bool inheritedSetupWarningLogged;
    bool preserveLoadedKitchen;
    bool inheritedStationMoved;
    float inheritedBaselineRate;
    float inheritedTargetRate;
    float inheritedCurrentRate;
    float nextInheritedForecastRefresh;
    int stepIndex;
    public int SaveStepIndex => stepIndex;
    public bool OrderServedForSave => orderServed;
    public bool TutorialPauseUsedForSave => tutorialPauseControlUsed;
    public bool TutorialPlayUsedForSave => tutorialPlayControlUsed;
    public bool TutorialFastForwardUsedForSave => tutorialFastForwardControlUsed;
    public bool TutorialShiftStartedForSave => tutorialShiftStarted;
    public int TutorialFastForwardRateMaskForSave => tutorialFastForwardRateMask;
    public int TutorialAnalyticsStageForSave => analyticsTutorialStage;
    public float TutorialBaselineRateForSave => inheritedBaselineRate;
    public float TutorialTargetRateForSave => inheritedTargetRate;
    public bool TutorialStationMovedForSave => inheritedStationMoved;
    public bool MilestoneOneBottleneckLessonDoneForSave => milestoneOneBottleneckLessonDone;
    public bool MilestoneOneAnalyticsLessonDoneForSave => milestoneOneAnalyticsLessonDone;

    public void RestoreCheckpoint(bool complete, int savedStep, bool savedOrderServed = false,
        bool savedPauseUsed = false, bool savedPlayUsed = false, bool savedFastForwardUsed = false,
        bool savedShiftStarted = false, int savedFastForwardRateMask = 0,
        int savedAnalyticsStage = -1, bool preserveKitchen = false,
        float savedBaselineRate = 0f, float savedTargetRate = 0f,
        bool savedStationMoved = false, bool savedMilestoneOneBottleneckLessonDone = false,
        bool savedMilestoneOneAnalyticsLessonDone = false)
    {
        PlayerPrefs.SetInt(PrefsCompleteKey, complete ? 1 : 0);
        // Save restore replaces scene-authored stations with checkpoint objects.
        // Keep the tutorial's station references aligned with those replacements.
        starterKitchen.Clear();
        if (!complete) CacheStarterKitchen();
        stepIndex = Mathf.Clamp(savedStep, 0, Steps.Length - 1);
        tutorialShiftStarted = savedShiftStarted || stepIndex >= 3;
        if (!complete && tutorialShiftStarted && stepIndex == 3)
            ShiftPlanningUI.Instance?.ResumeTutorialPracticeShift();
        glossaryTermClicked = false;
        objectivesScreenOpened = false;
        planScreenIntroduced = !complete && stepIndex >= 2;
        tutorialBurgerUnlockShown = true;
        preserveLoadedKitchen = preserveKitchen;
        inheritedKitchenPrepared = preserveKitchen;
        inheritedBaselineRate = savedBaselineRate;
        inheritedTargetRate = savedTargetRate;
        inheritedStationMoved = savedStationMoved;
        milestoneOneBottleneckLessonDone = savedMilestoneOneBottleneckLessonDone;
        milestoneOneAnalyticsLessonDone = savedMilestoneOneAnalyticsLessonDone;
        milestoneOneLessonActive = false;
        milestoneOneStationClickObserved = false;
        lastStationHealthLessonTarget = null;
        stationHealthLessonSelectionRevision = 0;
        stationChecklistInitialized = false;
        nextStationChecklistRefresh = 0f;
        furthestStepIndex = stepIndex;
        tutorialPauseControlUsed = savedPauseUsed;
        tutorialPlayControlUsed = savedPlayUsed;
        tutorialFastForwardControlUsed = savedFastForwardUsed;
        tutorialFastForwardRateMask = savedFastForwardRateMask;
        analyticsTutorialStage = savedAnalyticsStage;
        analyticsScreenOpened = false;
        analyticsScreenClosed = false;
        if (stepIndex >= 0 && stepIndex < Steps.Length && Steps[stepIndex].requireCameraControls)
            ResetCameraTutorialInputs();
        orderServed = savedOrderServed;
        burgerArrivedAtPickup = stepIndex == 3 && (savedOrderServed || HasTutorialBurgerAtPickup());
        tutorialCustomerOrderServed = stepIndex == 4 && savedOrderServed;
        customerJourneyComplete = false;
        if (stepIndex == 4 && savedOrderServed)
        {
            CustomerAI[] restoredCustomers = FindObjectsByType<CustomerAI>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            customerJourneyComplete = restoredCustomers == null || restoredCustomers.Length == 0;
        }
        if (stepIndex == 3)
        {
            CustomerAI.DespawnAllCustomers();
            liveCustomerSpawned = false;
        }
        customerJourneyStage = -1;
        customerJourneyCardQueue.Clear();
        customerJourneyCustomerGone = false;
        customerJourneyFinalMessageDisplayed = false;
        customerJourneyActionMessageActive = false;
        customerJourneyMessageHoldUntil = 0f;
        GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
        if (complete)
        {
            pendingStart = false;
            running = false;
            HideUI();
            ClearHighlight();
            GameTimeManager.Instance?.ReleaseExternalPause(PauseSource);
        }
        else
        {
            pendingStart = true;
            running = false;
            HideUI();
            if (gameplayReady)
                TryBegin();
        }
    }
    int furthestStepIndex = -1;
    int autoAdvanceStep = -1;
    float autoAdvanceAt;
    bool flowEditCompleted;
    bool flowCreateCompleted;
    bool workerDraggedToFlow;
    bool recipeBusinessOpened;
    bool researchScreenOpened;
    bool recipeScreenOpened;
    bool analyticsScreenOpened;
    bool analyticsScreenClosed;
    int analyticsTutorialStage = -1;
    int tutorialFastForwardRateMask;
    bool menuScreenOpened;
    bool objectivesScreenOpened;
    bool orderServed;
    bool glossaryTermClicked;
    bool cleanedOldWorldCallout;
    bool tutorialCameraMoved;
    bool tutorialCameraRotated;
    bool tutorialCameraZoomed;
    bool tutorialPauseControlUsed;
    bool tutorialPlayControlUsed;
    bool tutorialFastForwardControlUsed;
    bool tutorialShiftStarted;
    int recipeTutorialStationIndex;
    int ingredientTutorialStep;
    float ingredientExplanationUntil;
    readonly bool[] stationChecklistStatus = new bool[7];
    bool stationChecklistInitialized;
    bool stationChecklistComplete;
    float nextStationChecklistRefresh;
    float nextFreezerWalkthroughRefresh;
    FreezerWalkthroughPhase freezerWalkthroughPhase = FreezerWalkthroughPhase.Inactive;
    string tutorialFocusText;
    string tutorialTextBeingRevealed;
    string lastTutorialHighlightedText;
    int tutorialVisibleCharacters;
    int tutorialTotalCharacters;
    float tutorialCharacterProgress;
    float nextGlossaryTermFlashRefresh;
    GameObject glossaryTermFlashBox;
    RectTransform glossaryTermFlashBoxRect;
    CanvasGroup glossaryTermFlashBoxGroup;
    float nextTutorialVoiceTime;
    AudioSource tutorialTypewriterVoiceSource;
    readonly Dictionary<Graphic, GameObject> tutorialBorders = new Dictionary<Graphic, GameObject>();
    float nextControlHighlightRefresh;
    readonly List<GameObject> starterKitchen = new List<GameObject>();

    GameObject canvasRoot;
    GameObject panel;
    TutorialPanelDragHandle panelDragHandle;
    TextMeshProUGUI titleText;
    TextMeshProUGUI bodyText;
    TextMeshProUGUI coreStationChecklistText;
    TextMeshProUGUI stepLabel;
    Button backButton;
    Button nextButton;
    TextMeshProUGUI nextLabel;
    GameObject cameraPromptPanel;
    GameObject flowLinkingVideoFrame;
    RawImage flowLinkingVideoImage;
    VideoPlayer flowLinkingVideoPlayer;
    RenderTexture flowLinkingVideoTexture;
    TextMeshProUGUI cameraMovePromptStatus;
    TextMeshProUGUI cameraRotatePromptStatus;
    TextMeshProUGUI cameraZoomPromptStatus;
    RectTransform calloutTarget;
    RectTransform calloutArrow;
    GameObject fastForwardHelpCard;
    RectTransform fastForwardHelpCardRect;
    RectTransform fastForwardHelpArrow;
    RectTransform fastForwardRateHelpArrow;
    GameTimeUI cachedTutorialTimeControls;
    readonly Vector3[] fastForwardButtonCorners = new Vector3[4];
    readonly Vector3[] fastForwardRateButtonCorners = new Vector3[4];
    Vector2 calloutPanelPositionVelocity;
    bool calloutPanelPositionInitialized;
    int lastCalloutPanelMotionFrame = -1;
    GameObject highlight;
    Transform highlightTarget;
    Transform secondaryHighlightTarget;
    GameObject secondaryHighlight;
    LineRenderer tutorialFlowGuideLine;
    readonly Vector3[] tutorialFlowGuidePoints = new Vector3[4];
    Transform tutorialFlowGuideArrow;
    Mesh tutorialFlowGuideArrowMesh;
    Material tutorialFlowGuideMaterial;
    Highlight currentHighlight;
    const string HighlightMaterialResourcePath = "Materials/OnboardingHighlight";
    const string FlowLinkingVideoResourcePath = "Tutorial/LinkStationsVidTutorial";

    enum Highlight
    {
        None,
        Register,
        Inventory,
        Management,
        Freezer,
        Grill,
        Fryer,
        Drink,
        Assembly,
        HeatLamp,
        Pantry
    }

    enum FreezerWalkthroughPhase
    {
        Inactive,
        StationName,
        ModelPreview,
        OwnedCount,
        PurchaseButton,
        Placement,
        RemainingStations
    }

    struct Step
    {
        public string title;
        public string body;
        public string nextLabel;
        public bool liveCustomer;
        public bool pauseSim;
        public Highlight highlight;
        public bool openInventory;
        public bool openIngredients;
        public bool openWorkers;
        public bool requirePlaced;
        public bool requireStationChecklist;
        public bool requireAllStations;
        public bool requireFlow;
        public bool requireWorkerOnFlow;
        public bool requireWorkerDragToFlow;
        public bool requireHiredWorker;
        public bool requireEditedFlow;
        public bool requireRecipes;
        public bool requireIngredients;
        public bool requireStationSequence;
        public bool openResearch;
        public bool requireResearch;
        public bool openRecipes;
        public bool requireRecipeScreen;
        public bool openAnalytics;
        public bool requireAnalyticsScreen;
        public bool requireMenuScreen;
        public bool requireObjectivesScreen;
        public bool requirePlanIntroduction;
        public bool requireHamburger;
        public bool requireOrderServed;
        public bool requireCameraControls;
        public bool requireGlossaryTermClick;
        public bool requireTimeControlInteractions;
        public bool requireShiftStarted;
        public bool requireOptimizedLayout;
        public bool requirePickupDelivery;
        public bool requireCustomerJourney;
        public bool requireStationHealthReview;

        public Step(
            string title,
            string body,
            string nextLabel,
            Highlight highlight,
            bool pauseSim = false,
            bool liveCustomer = false,
            bool openInventory = false,
            bool openIngredients = false,
            bool openWorkers = false,
            bool requirePlaced = false,
            bool requireAllStations = false,
            bool requireFlow = false,
            bool requireWorkerOnFlow = false,
            bool requireHiredWorker = false,
            bool requireEditedFlow = false, bool requireRecipes = false,
            bool requireIngredients = false,
            bool requireStationSequence = false,
            bool openResearch = false,
            bool requireResearch = false,
            bool openRecipes = false,
            bool requireRecipeScreen = false,
            bool openAnalytics = false,
            bool requireAnalyticsScreen = false,
            bool requireMenuScreen = false,
            bool requireObjectivesScreen = false,
            bool requirePlanIntroduction = false,
            bool requireHamburger = false,
            bool requireOrderServed = false,
            bool requireCameraControls = false,
            bool requireStationChecklist = false,
            bool requireWorkerDragToFlow = false,
            bool requireGlossaryTermClick = false,
            bool requireTimeControlInteractions = false,
            bool requireShiftStarted = false,
            bool requireOptimizedLayout = false,
            bool requirePickupDelivery = false,
            bool requireCustomerJourney = false,
            bool requireStationHealthReview = false)
        {
            this.title = title;
            this.body = body;
            this.nextLabel = nextLabel;
            this.highlight = highlight;
            this.pauseSim = pauseSim;
            this.liveCustomer = liveCustomer;
            this.openInventory = openInventory;
            this.openIngredients = openIngredients;
            this.openWorkers = openWorkers;
            this.requirePlaced = requirePlaced;
            this.requireStationChecklist = requireStationChecklist;
            this.requireAllStations = requireAllStations;
            this.requireFlow = requireFlow;
            this.requireWorkerOnFlow = requireWorkerOnFlow;
            this.requireWorkerDragToFlow = requireWorkerDragToFlow;
            this.requireHiredWorker = requireHiredWorker;
            this.requireEditedFlow = requireEditedFlow;
            this.requireRecipes = requireRecipes;
            this.requireIngredients = requireIngredients;
            this.requireStationSequence = requireStationSequence;
            this.openResearch = openResearch;
            this.requireResearch = requireResearch;
            this.openRecipes = openRecipes;
            this.requireRecipeScreen = requireRecipeScreen;
            this.openAnalytics = openAnalytics;
            this.requireAnalyticsScreen = requireAnalyticsScreen;
            this.requireMenuScreen = requireMenuScreen;
            this.requireObjectivesScreen = requireObjectivesScreen;
            this.requirePlanIntroduction = requirePlanIntroduction;
            this.requireHamburger = requireHamburger;
            this.requireOrderServed = requireOrderServed;
            this.requireCameraControls = requireCameraControls;
            this.requireGlossaryTermClick = requireGlossaryTermClick;
            this.requireTimeControlInteractions = requireTimeControlInteractions;
            this.requireShiftStarted = requireShiftStarted;
            this.requireOptimizedLayout = requireOptimizedLayout;
            this.requirePickupDelivery = requirePickupDelivery;
            this.requireCustomerJourney = requireCustomerJourney;
            this.requireStationHealthReview = requireStationHealthReview;
        }
    }

    static readonly Step[] Steps =
    {
        new Step("The kitchen you inherited",
            "You are the restaurant's <b>industrial and systems engineer</b>. A hamburger line is ready. We will learn how to improve it while the kitchen runs.",
            "Next", Highlight.None),
        new Step("Move around the kitchen",
            "Move with <b>WASD</b>. Right-drag to turn the camera, and scroll to zoom in and out. Try each input to continue.",
            "Next", Highlight.None, requireCameraControls: true),
        new Step("Start your first shift",
            "The Plan card shows hamburger demand for this shift. Take a quick look, then click <b>Start Shift</b> to see the line in action.",
            "Next", Highlight.None, requirePlanIntroduction: true, requireShiftStarted: true),
        new Step("Follow the worker",
            "Follow the worker from station to station. Watch the burger arrive at Pickup.",
            "Next", Highlight.None, requirePickupDelivery: true),
        new Step("Follow one customer",
            "A customer will enter at the door. Follow them to the register, then to Pickup, and out of the restaurant.",
            "Next", Highlight.None, liveCustomer: true, requireCustomerJourney: true),
        new Step("Improve the layout",
            "In the <b>Build Menu</b>, click a station and drag it to shorten worker travel. Press <b>R</b> to rotate it. Reach the output target below.",
            "Finish", Highlight.None, requireOptimizedLayout: true),
        new Step("Identify a bottleneck",
            "It is noon. Open <b>Business</b> and select a production station. Read its Working, Blocked, Starved, and Idle percentages to see where time is going.",
            "Got it", Highlight.None, requireStationHealthReview: true),
        new Step("Review Analytics",
            "It is 6 PM. Open <b>Analytics</b> and review how demand, orders, production, and reputation are performing.",
            "Finish", Highlight.None, requireAnalyticsScreen: true)
    };

    // Gus speaks the tutorial while the Step data continues to define gameplay gates.
    static readonly string[] CompactTutorialText =
    {
        "You are the restaurant's <b>industrial and systems engineer</b>. A hamburger line is ready. We will learn how to improve it while the kitchen runs.",
        "Move with <b>WASD</b>. Right-drag to turn the camera, and scroll to zoom in and out. Try each input to continue.",
        "The Plan card shows hamburger demand for this shift. Take a quick look, then click <b>Start Shift</b> to see the line in action.",
        "Follow the worker through the process. Watch the burger arrive at Pickup.",
        "Follow the customer from the door. See how they order, collect their burger, and leave.",
        "Now improve the line. In <b>Build</b>, move an existing station to shorten worker travel and raise predicted output to the target below."
    };

    static readonly string[] GusDialogue = CompactTutorialText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<OnboardingTutorial>() != null) return;
        var go = new GameObject("OnboardingTutorial");
        go.AddComponent<OnboardingTutorial>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        pendingStart = !IsComplete;
        if (!IsComplete) CacheStarterKitchen();
    }

    void OnDestroy()
    {
        ClearTutorialCustomerFollow();
        ClearControlHighlights();
        ReleaseFlowLinkingVideo();
        DestroyTutorialFlowGuide();
        if (Instance == this)
            Instance = null;
        TutorialVoiceEvents.OnEvent -= OnGameEvent;
        if (GameTimeManager.Instance != null)
        {
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
            GameTimeManager.Instance.ReleaseExternalPause(CustomerJourneyPauseSource);
        }
    }

    void Start()
    {
        TutorialVoiceEvents.OnEvent += OnGameEvent;
        if (!IsComplete)
            pendingStart = true;
        Invoke(nameof(TryBeginIfNoTitle), 0.15f);
    }

    void TryBeginIfNoTitle()
    {
        var title = FindFirstObjectByType<TitleScreenController>();
        if (title != null) return;
        gameplayReady = true;
        TryBegin();
    }

    public void NotifyGameStarted()
    {
        gameplayReady = true;
        TryBegin();
    }

    public void TryBegin()
    {
        if (running || IsComplete || !gameplayReady)
            return;

        var title = FindFirstObjectByType<TitleScreenController>();
        if (title != null && title.IsShowingTitle)
            return;

        if (!inheritedKitchenPrepared && !preserveLoadedKitchen)
            PrepareInheritedKitchen();
        if (!inheritedKitchenPrepared) return;
        if (!preserveLoadedKitchen)
            foreach (var customer in FindObjectsByType<CustomerAI>(FindObjectsSortMode.None))
            {
                customer.gameObject.SetActive(false);
                Destroy(customer.gameObject);
            }
        running = true;
        pendingStart = false;
        if (!tutorialShiftStarted && !ShiftPlanningUI.IsPlanning)
            ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
        PauseMenuUI.SuppressPauseInputFor(0.35f);
        flowEditCompleted = false;
        flowCreateCompleted = false;
        liveCustomerSpawned = false;
        tutorialCustomer = null;
        ClearTutorialCustomerFollow();
        EnsureUI();
        ShowStep();
        if (stepIndex == 0)
        {
            var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventoryUI != null && inventoryUI.IsPanelOpen) inventoryUI.TogglePanel();
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.Close();
            var objectives = FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
            if (objectives != null) objectives.SetVisible(false);
        }
    }

    public void Skip()
    {
        Complete(skipped: true);
    }

    public void Restart()
    {
        panelDragHandle?.ResetUserPosition();
        PlayerPrefs.DeleteKey(PrefsCompleteKey);
        PlayerPrefs.Save();
        pendingStart = true;
        running = false;
        gameplayReady = false;
        stepIndex = 0;
        glossaryTermClicked = false;
        objectivesScreenOpened = false;
        stationChecklistInitialized = false;
        nextStationChecklistRefresh = 0f;
        inheritedKitchenPrepared = false;
        inheritedSetupWarningLogged = false;
        preserveLoadedKitchen = false;
        inheritedStationMoved = false;
        inheritedBaselineRate = 0f;
        inheritedTargetRate = 0f;
        furthestStepIndex = -1;
        flowEditCompleted = false;
        flowCreateCompleted = false;
        recipeBusinessOpened = false;
        liveCustomerSpawned = false;
        burgerArrivedAtPickup = false;
        customerJourneyComplete = false;
        tutorialCustomerOrderServed = false;
        customerJourneyStage = -1;
        customerJourneyCardQueue.Clear();
        customerJourneyCustomerGone = false;
        customerJourneyFinalMessageDisplayed = false;
        customerJourneyActionMessageActive = false;
        customerJourneyMessageHoldUntil = 0f;
        GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
        milestoneOneLessonActive = false;
        milestoneOneBottleneckLessonDone = false;
        milestoneOneAnalyticsLessonDone = false;
        milestoneOneStationClickObserved = false;
        lastStationHealthLessonTarget = null;
        stationHealthLessonSelectionRevision = 0;
        tutorialCustomer = null;
        ClearTutorialCustomerFollow();
        researchScreenOpened = false;
        recipeScreenOpened = false;
        analyticsScreenOpened = false;
        analyticsScreenClosed = false;
        analyticsTutorialStage = -1;
        menuScreenOpened = false;
        orderServed = false;
        tutorialShiftStarted = false;
        ResetTutorialTimeControlProgress();
        cleanedOldWorldCallout = false;
        planScreenIntroduced = false;
        tutorialBurgerUnlockShown = false;
        ResetCameraTutorialInputs();
        HideUI();
        ClearHighlight();
        if (GameTimeManager.Instance != null)
        {
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
            GameTimeManager.Instance.ReleaseExternalPause(CustomerJourneyPauseSource);
        }
        ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
        starterKitchen.Clear();
        DestroyTutorialPlacements();
        TryBegin();
    }

    void OnGameEvent(string eventId)
    {
        CustomerOrderConfig tutorialConfig = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        if (running && tutorialConfig != null && tutorialConfig.burgerBase != null
            && eventId == TutorialVoiceEventId.PickupItemDelivered(tutorialConfig.burgerBase))
        {
            burgerArrivedAtPickup = true;
            RefreshAdvanceGate();
            UpdateContextualCallout();
            return;
        }
        if (eventId == TutorialVoiceEventId.StationRepositioned && running)
        {
            inheritedStationMoved = true;
            nextInheritedForecastRefresh = 0f;
            RefreshAdvanceGate();
            return;
        }
        if (eventId != TutorialVoiceEventId.FirstOrderServed && eventId != TutorialVoiceEventId.OrderServed)
            return;

        // Keep this latched even if the event arrives during a step transition
        // or immediately before a save restore. Serving is a gameplay fact, not
        // a fact that should depend on the current tutorial panel timing.
        orderServed = true;
        if (!running) return;
        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireCustomerJourney)
        {
            tutorialCustomerOrderServed = true;
            QueueCustomerJourneyThroughStage(3);
            RefreshCustomerJourneyCard();
            RefreshAdvanceGate();
            UpdateContextualCallout();
        }
        if (stepIndex >= 0 && stepIndex < Steps.Length && Steps[stepIndex].requireOrderServed)
        {
            if (stepIndex < Steps.Length - 1)
                ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
            if (Steps[stepIndex].requireTimeControlInteractions)
                RefreshTimeControlTutorialStep();
            RefreshAdvanceGate();
            UpdateContextualCallout();
            return;
        }
    }

    void OnBack()
    {
        if (stepIndex <= 0) return;
        Sfx.Play(SfxId.UiClick);
        if (Steps[stepIndex].requireObjectivesScreen)
            MissionListUI.Instance?.SetVisible(false);
        stepIndex--;
        ShowStep();
    }

    void OnNext()
    {
        if (milestoneOneLessonActive && stepIndex == MilestoneOneBottleneckStep
            && StepRequirementMet())
        {
            if (tutorialVisibleCharacters < tutorialTotalCharacters)
            {
                tutorialVisibleCharacters = tutorialTotalCharacters;
                tutorialCharacterProgress = tutorialTotalCharacters;
                if (bodyText != null)
                    bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
                return;
            }
            Sfx.Play(SfxId.UiClick);
            milestoneOneBottleneckLessonDone = true;
            EndMilestoneOneLesson();
            return;
        }

        if (Steps[stepIndex].requireStationChecklist
            && IsFreezerExplanationPhase(freezerWalkthroughPhase))
        {
            if (tutorialVisibleCharacters < tutorialTotalCharacters)
            {
                tutorialVisibleCharacters = tutorialTotalCharacters;
                tutorialCharacterProgress = tutorialTotalCharacters;
                if (bodyText != null)
                    bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
                return;
            }

            Sfx.Play(SfxId.UiClick);
            SetFreezerWalkthroughPhase((FreezerWalkthroughPhase)((int)freezerWalkthroughPhase + 1));
            return;
        }

        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireAnalyticsScreen && !StepRequirementMet())
        {
            if (!analyticsScreenOpened || analyticsScreenClosed || !AnalyticsPopupUI.IsOpen
                || analyticsTutorialStage < 0 || analyticsTutorialStage >= AnalyticsTourCloseStage)
            {
                Sfx.Play(SfxId.UiError);
                RefreshAdvanceGate();
                return;
            }

            if (tutorialVisibleCharacters < tutorialTotalCharacters)
            {
                tutorialVisibleCharacters = tutorialTotalCharacters;
                tutorialCharacterProgress = tutorialTotalCharacters;
                if (bodyText != null)
                    bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
                return;
            }

            Sfx.Play(SfxId.UiClick);
            analyticsTutorialStage++;
            SetTutorialBodyText(GetAnalyticsTourInstruction());
            nextControlHighlightRefresh = 0f;
            RefreshAdvanceGate();
            RefreshControlHighlights();
            UpdateContextualCallout();
            return;
        }

        if (!StepRequirementMet())
        {
            if (stepIndex >= 0 && stepIndex < Steps.Length
                && Steps[stepIndex].requireRecipeScreen)
            {
                Sfx.Play(SfxId.UiClick);
                IngredientsOrderUI recipes = FindFirstObjectByType<IngredientsOrderUI>(
                    FindObjectsInactive.Include);
                if (recipes != null)
                    recipes.OpenRecipesFromHud();
                return;
            }
            if (stepIndex >= 0 && stepIndex < Steps.Length
                && Steps[stepIndex].requireObjectivesScreen && !objectivesScreenOpened)
            {
                Sfx.Play(SfxId.UiClick);
                MissionListUI objectives = MissionListUI.Instance;
                Button objectivesButton = objectives != null ? objectives.TutorialObjectivesButton : null;
                if (objectivesButton != null)
                    objectivesButton.onClick.Invoke();
                return;
            }
            Sfx.Play(SfxId.UiError);
            RefreshAdvanceGate();
            return;
        }

        if (tutorialVisibleCharacters < tutorialTotalCharacters)
        {
            tutorialVisibleCharacters = tutorialTotalCharacters;
            tutorialCharacterProgress = tutorialTotalCharacters;
            if (bodyText != null)
                bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
            return;
        }

        Sfx.Play(SfxId.UiClick);
        if (milestoneOneLessonActive && stepIndex == MilestoneOneAnalyticsStep)
        {
            milestoneOneAnalyticsLessonDone = true;
            EndMilestoneOneLesson();
            return;
        }

        if (stepIndex >= OnboardingStepCount - 1)
        {
            Complete(skipped: false);
            return;
        }

        if (Steps[stepIndex].requireObjectivesScreen)
            MissionListUI.Instance?.SetVisible(false);
        stepIndex++;
        ShowStep();
    }

    void ShowStep()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        var step = Steps[stepIndex];
        if (!step.requireCustomerJourney)
        {
            GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
            customerJourneyActionMessageActive = false;
            tutorialCustomer = null;
            customerJourneyStage = -1;
            ClearTutorialCustomerFollow();
        }
        else if (!customerJourneyComplete)
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.Play);
        }
        if (step.requireAnalyticsScreen)
        {
            analyticsScreenOpened = false;
            analyticsScreenClosed = false;
            if (analyticsTutorialStage < 0)
                analyticsTutorialStage = 0;
        }
        // A manual drag only overrides placement for the current step. Let the
        // next step return to its contextual position, animating from here.
        panelDragHandle?.ResetUserPosition();
        calloutPanelPositionVelocity = Vector2.zero;
        tutorialUnlockContinuePending = false;
        if (step.requireObjectivesScreen)
        {
            objectivesScreenOpened = false;
            MissionListUI objectives = FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
            if (objectives != null) objectives.SetVisible(false);
        }
        if (step.requireStationChecklist && !tutorialBurgerUnlockShown)
        {
            MilestoneProgressManager milestoneProgress = MilestoneProgressManager.Instance;
            MilestoneDefinition tutorialMilestone = milestoneProgress != null
                ? milestoneProgress.database?.GetById("tutorial")
                : null;
            if (tutorialMilestone != null)
            {
                tutorialBurgerUnlockShown = true;
                tutorialUnlockContinuePending = true;
                if (!MilestoneUnlockPopupUI.ShowTutorialUnlocks(tutorialMilestone,
                        AdvanceFromTutorialUnlock))
                    tutorialUnlockContinuePending = false;
            }
        }
        if (step.requirePlanIntroduction)
            planScreenIntroduced = true;
        if (step.requireShiftStarted)
        {
            var build = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (build != null && build.IsPanelOpen) build.TogglePanel();
            ShiftPlanningUI.Instance?.FocusPlanningCard();
        }
        else if (!milestoneOneLessonActive && planScreenIntroduced && !step.liveCustomer)
            ShiftPlanningUI.Instance?.FocusPlanningCard();
        if (step.requireWorkerDragToFlow)
            workerDraggedToFlow = false;
        recipeTutorialStationIndex = 0;
        recipeBusinessOpened = false;
        ingredientTutorialStep = 0;
        ingredientExplanationUntil = 0f;
        tutorialFocusText = null;
        freezerWalkthroughPhase = step.requireStationChecklist
            ? HasPlacedStation(Highlight.Freezer)
                ? FreezerWalkthroughPhase.RemainingStations
                : FreezerWalkthroughPhase.StationName
            : FreezerWalkthroughPhase.Inactive;
        if (!step.requireMenuScreen)
            menuScreenOpened = false;
        furthestStepIndex = Mathf.Max(furthestStepIndex, stepIndex);

        // Hide the previous step's demo before reactivating the panel to avoid
        // a one-frame video flash during step transitions.
        SetFlowLinkingVideoVisible(step.requireFlow);

        if (canvasRoot != null)
            canvasRoot.SetActive(true);
        Canvas tutorialCanvas = canvasRoot != null ? canvasRoot.GetComponent<Canvas>() : null;
        if (tutorialCanvas != null)
            tutorialCanvas.sortingOrder = tutorialUnlockContinuePending ? 7100 : 4200;
        if (panel != null)
            panel.SetActive(true);
        SetFastForwardHelpVisible(stepIndex == 3 && running);
        if (cameraPromptPanel != null)
            cameraPromptPanel.SetActive(step.requireCameraControls);
        if (titleText != null)
            titleText.text = ISEVocabularyHighlighter.Highlight(step.title);
        string initialBody = !string.IsNullOrEmpty(tutorialFocusText) ? tutorialFocusText
            : stepIndex < CompactTutorialText.Length ? CompactTutorialText[stepIndex] : step.body;
        if (step.requireStationChecklist)
            initialBody = GetFreezerWalkthroughText(freezerWalkthroughPhase);
        else if (step.requirePlanIntroduction)
            initialBody += "\n\n<color=#63E6FF><b>Current output:</b></color> <color=#63E6FF>"
                + inheritedBaselineRate.ToString("0.0") + " burgers/min.</color> "
                + "<color=#FFE45C><b>Improvement target:</b> "
                + inheritedTargetRate.ToString("0.0") + " burgers/min.</color>";
        else if (step.requireTimeControlInteractions)
            initialBody = GetTimeControlInstruction();
        else if (step.requireAnalyticsScreen)
            initialBody = step.body;
        SetTutorialBodyText(initialBody);
        if (step.requireCustomerJourney)
            customerJourneyMessageHoldUntil = 0f;
        if (coreStationChecklistText != null)
        {
            coreStationChecklistText.gameObject.SetActive(
                step.requireStationChecklist || step.requireTimeControlInteractions
                || step.requireOptimizedLayout);
            if (step.requireStationChecklist)
                RefreshStationChecklist();
            else if (step.requireTimeControlInteractions)
                RefreshTimeControlChecklist();
            else if (step.requireOptimizedLayout)
                RefreshInheritedLayoutText();
        }
        if (stepLabel != null)
            stepLabel.text = milestoneOneLessonActive
                ? stepIndex == MilestoneOneBottleneckStep ? "MILESTONE 1  |  12 PM" : "MILESTONE 1  |  6 PM"
                : $"Tutorial  {stepIndex + 1} / {OnboardingStepCount}";
        if (nextLabel != null)
            nextLabel.text = step.nextLabel;
        if (nextButton != null)
        {
            bool freezerGuideNext = step.requireStationChecklist
                && IsFreezerExplanationPhase(freezerWalkthroughPhase);
            nextButton.gameObject.SetActive(step.requireAnalyticsScreen
                ? false
                : freezerGuideNext || (!AutomaticallyAdvances(step)
                    && !tutorialUnlockContinuePending));
        }
        autoAdvanceStep = -1;

        ApplyPause(step.pauseSim);
        currentHighlight = step.requireStationChecklist
            && freezerWalkthroughPhase != FreezerWalkthroughPhase.RemainingStations
                ? Highlight.Freezer : step.highlight;
        calloutTarget = null;
        UpdateHighlight(currentHighlight);

        if (!step.openResearch)
            ResearchTreeUI.CloseIfOpen();
        if (!step.openRecipes)
        {
            var recipes = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
            if (recipes != null) recipes.CloseRecipesFromHud();
        }
        if (!step.openAnalytics)
            AnalyticsPopupUI.CloseIfOpen();

        if (step.requireStationChecklist)
        {
            var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventoryUI != null && inventoryUI.IsPanelOpen) inventoryUI.TogglePanel();
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.Close();
            var objectives = FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
            if (objectives != null) objectives.SetVisible(false);
        }

        if (step.openInventory)
        {
            var inv = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            var tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            if (tabs != null)
                tabs.OpenBuildPage();
            if (inv != null)
            {
                if (tabs == null)
                    inv.OpenBuildPage();
                inv.RefreshAll();
            }
        }

        if (step.openIngredients)
        {
            var tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (tabs != null)
            {
                tabs.OpenBusinessPage();
                if (mgmt != null) mgmt.OpenIngredientsTab();
            }
            else if (mgmt != null)
                mgmt.OpenIngredientsTab();
        }

        if (step.openResearch)
        {
            var inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventory != null && inventory.IsPanelOpen) inventory.TogglePanel();
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.Close();
            Transform canvas = FindHudCanvas();
            if (canvas != null)
                ResearchTreeUI.Open(canvas);
        }
        if (step.openRecipes)
        {
            var recipes = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
            if (recipes != null)
                recipes.OpenRecipesFromHud();
        }
        if (step.openAnalytics)
        {
            Transform canvas = FindHudCanvas();
            if (canvas != null)
                AnalyticsPopupUI.Open(canvas);
        }
        if (step.openWorkers && !step.requireRecipes)
        {
            var tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (tabs != null)
                tabs.OpenStaffPage();
            else if (mgmt != null)
                mgmt.OpenWorkersTab();
        }

        if (step.liveCustomer)
            SpawnPracticeCustomer();

        RebuildTutorialLayout();
        ResetTutorialTextReveal();
        RefreshCameraPromptStatus();
        RefreshAdvanceGate();
    }

    void RebuildTutorialLayout()
    {
        if (panel == null) return;
        if (titleText != null)
            titleText.ForceMeshUpdate();
        if (bodyText != null)
            bodyText.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        UpdateContextualCallout();
    }

    void AdvanceFromTutorialUnlock()
    {
        if (!running || stepIndex != 3 || !tutorialUnlockContinuePending) return;
        tutorialUnlockContinuePending = false;
        Sfx.Play(SfxId.UiClick);
        stepIndex++;
        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireTimeControlInteractions)
            ResetTutorialTimeControlProgress();
        ShowStep();
    }

    void ResetTutorialTextReveal()
    {
        if (bodyText == null) return;

        tutorialTextBeingRevealed = bodyText.text;
        bodyText.ForceMeshUpdate();
        tutorialTotalCharacters = bodyText.textInfo.characterCount;
        tutorialVisibleCharacters = 0;
        tutorialCharacterProgress = 0f;
        nextTutorialVoiceTime = Time.unscaledTime;
        bodyText.maxVisibleCharacters = 0;
    }

    void UpdateTutorialTextReveal()
    {
        if (!running || bodyText == null || !bodyText.gameObject.activeInHierarchy)
            return;

        ApplyISEVocabularyHighlighting();
        if (tutorialTextBeingRevealed != bodyText.text)
            ResetTutorialTextReveal();

        if (tutorialVisibleCharacters >= tutorialTotalCharacters)
            return;

        tutorialCharacterProgress += Time.unscaledDeltaTime
            * IntroCutsceneUI.TypewriterCharactersPerSecond;
        int targetCharacters = Mathf.Min(tutorialTotalCharacters,
            Mathf.FloorToInt(tutorialCharacterProgress));
        if (targetCharacters <= tutorialVisibleCharacters)
            return;

        tutorialVisibleCharacters = targetCharacters;
        bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
        if (Time.unscaledTime < nextTutorialVoiceTime)
            return;

        int characterIndex = Mathf.Min(tutorialVisibleCharacters - 1,
            bodyText.textInfo.characterCount - 1);
        if (characterIndex < 0 || characterIndex >= bodyText.textInfo.characterInfo.Length)
            return;

        char character = bodyText.textInfo.characterInfo[characterIndex].character;
        if (!char.IsLetterOrDigit(character))
            return;

        nextTutorialVoiceTime = Time.unscaledTime + IntroCutsceneUI.TypewriterVoiceInterval
            + Random.Range(0f, 0.025f);
        IntroCutsceneUI.PlayGibberishBlip(tutorialTypewriterVoiceSource);
    }

    void SetTutorialBodyText(string sourceText)
    {
        if (bodyText == null) return;

        string highlightedText = ISEVocabularyHighlighter.Highlight(sourceText);
        lastTutorialHighlightedText = highlightedText;
        if (bodyText.text != highlightedText)
        {
            // A changed instruction can have a different focus target. Resume
            // contextual placement so the dragged card cannot hide that target.
            bool keepPanelPlacement = running && stepIndex >= 0 && stepIndex < Steps.Length
                && (Steps[stepIndex].requireAnalyticsScreen || Steps[stepIndex].requireRecipes
                    || Steps[stepIndex].requireOptimizedLayout);
            if (!keepPanelPlacement)
            {
                panelDragHandle?.ResetUserPosition();
                calloutPanelPositionVelocity = Vector2.zero;
            }
            bodyText.text = highlightedText;
        }
    }

    void ApplyISEVocabularyHighlighting()
    {
        if (bodyText == null || bodyText.text == lastTutorialHighlightedText) return;
        SetTutorialBodyText(bodyText.text);
    }

    void UpdateGlossaryTermFlashBox()
    {
        if (bodyText == null) return;

        bool shouldFlash = running && stepIndex == 0 && !glossaryTermClicked
            && bodyText.gameObject.activeInHierarchy;
        if (!shouldFlash)
        {
            if (glossaryTermFlashBox != null)
                glossaryTermFlashBox.SetActive(false);
            return;
        }

        if (Time.unscaledTime >= nextGlossaryTermFlashRefresh)
        {
            nextGlossaryTermFlashRefresh = Time.unscaledTime + 0.08f;
            bodyText.ForceMeshUpdate();

            const string term = "industrial and systems engineer";
            TMP_TextInfo textInfo = bodyText.textInfo;
            int termStart = -1;
            for (int start = 0; start <= textInfo.characterCount - term.Length; start++)
            {
                bool matches = true;
                for (int offset = 0; offset < term.Length; offset++)
                {
                    char actual = char.ToLowerInvariant(textInfo.characterInfo[start + offset].character);
                    if (actual != term[offset])
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches) continue;
                termStart = start;
                break;
            }

            if (termStart >= 0 && tutorialVisibleCharacters >= termStart + term.Length)
            {
                Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
                Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
                for (int i = termStart; i < termStart + term.Length; i++)
                {
                    TMP_CharacterInfo character = textInfo.characterInfo[i];
                    if (!character.isVisible) continue;
                    min = Vector3.Min(min, character.bottomLeft);
                    max = Vector3.Max(max, character.topRight);
                }

                if (float.IsInfinity(min.x) || float.IsInfinity(min.y))
                {
                    if (glossaryTermFlashBox != null)
                        glossaryTermFlashBox.SetActive(false);
                }
                else
                {
                    EnsureGlossaryTermFlashBox();
                    glossaryTermFlashBoxRect.anchoredPosition = (Vector2)((min + max) * 0.5f);
                    glossaryTermFlashBoxRect.sizeDelta = new Vector2(max.x - min.x + 10f,
                        max.y - min.y + 8f);
                    glossaryTermFlashBox.SetActive(true);
                }
            }
            else if (glossaryTermFlashBox != null)
            {
                glossaryTermFlashBox.SetActive(false);
            }
        }

        if (glossaryTermFlashBoxGroup != null && glossaryTermFlashBox.activeSelf)
            glossaryTermFlashBoxGroup.alpha = Mathf.Lerp(0.08f, 1f,
                (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f) + 1f) * 0.5f);
    }

    void EnsureGlossaryTermFlashBox()
    {
        if (glossaryTermFlashBox != null || bodyText == null) return;

        glossaryTermFlashBox = new GameObject("Glossary Term Flash Box", typeof(RectTransform),
            typeof(CanvasGroup));
        glossaryTermFlashBox.transform.SetParent(bodyText.transform, false);
        glossaryTermFlashBox.transform.SetAsLastSibling();
        glossaryTermFlashBoxRect = (RectTransform)glossaryTermFlashBox.transform;
        glossaryTermFlashBoxRect.anchorMin = glossaryTermFlashBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
        glossaryTermFlashBoxRect.pivot = new Vector2(0.5f, 0.5f);
        glossaryTermFlashBoxGroup = glossaryTermFlashBox.GetComponent<CanvasGroup>();
        glossaryTermFlashBoxGroup.interactable = false;
        glossaryTermFlashBoxGroup.blocksRaycasts = false;

        Color yellow = new Color(1f, 0.82f, 0.15f, 1f);
        for (int side = 0; side < 4; side++)
        {
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(glossaryTermFlashBox.transform, false);
            Image image = edge.GetComponent<Image>();
            image.color = yellow;
            image.raycastTarget = false;
            RectTransform rect = (RectTransform)edge.transform;
            rect.anchorMin = side == 1 ? Vector2.up : side == 3 ? Vector2.right : Vector2.zero;
            rect.anchorMax = side == 0 ? Vector2.right : side == 2 ? Vector2.up : Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (side < 2)
            {
                rect.sizeDelta = new Vector2(0f, 2.5f);
                rect.pivot = new Vector2(0.5f, side);
            }
            else
            {
                rect.sizeDelta = new Vector2(2.5f, 0f);
                rect.pivot = new Vector2(side - 2, 0.5f);
            }
        }
    }

    void UpdateContextualCallout()
    {
        if (canvasRoot == null || panel == null) return;
        if (running)
        {
            Step activeStep = Steps[stepIndex];
            if (activeStep.requireStationSequence)
                UpdateStationSequenceStep(activeStep);
        }

        RectTransform rootRect = (RectTransform)canvasRoot.transform;
        RectTransform panelRect = (RectTransform)panel.transform;
        Vector2 targetPosition;
        Rect targetBounds;
        bool hasTarget = TryGetCalloutTarget(rootRect, out targetPosition, out targetBounds);

        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        ManagementModeController stationTutorialManager = running
            && Steps[stepIndex].requireRecipes ? ManagementModeController.Instance : null;
        RectTransform recipePickerPanel = stationTutorialManager != null
            ? stationTutorialManager.TutorialRecipePickerPanel : null;
        MissionListUI objectivesTutorial = running && Steps[stepIndex].requireObjectivesScreen
            && objectivesScreenOpened && MissionListUI.Instance != null
            && MissionListUI.Instance.IsOpen ? MissionListUI.Instance : null;
        RectTransform objectivesPanelRect = objectivesTutorial != null
            && objectivesTutorial.panelRoot != null
                ? objectivesTutorial.panelRoot.transform as RectTransform : null;
        bool analyticsTutorial = running && Steps[stepIndex].requireAnalyticsScreen;
        bool stationaryRecipeTutorial = running && Steps[stepIndex].requireRecipes;
        bool timeControlTutorial = running && Steps[stepIndex].requireTimeControlInteractions;
        float desiredPanelWidth = recipePickerPanel != null ? 420f
            : objectivesPanelRect != null ? 460f
            : analyticsTutorial ? 440f : 560f;
        if (Mathf.Abs(panelRect.sizeDelta.x - desiredPanelWidth) > 1f)
        {
            panelRect.sizeDelta = new Vector2(desiredPanelWidth, panelRect.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
        float panelWidth = panelRect.rect.width > 1f ? panelRect.rect.width : 560f;
        float panelHeight = panelRect.rect.height > 1f ? panelRect.rect.height : 180f;
        float rootWidth = rootRect.rect.width > 1f ? rootRect.rect.width : 1920f;
        float rootHeight = rootRect.rect.height > 1f ? rootRect.rect.height : 1080f;

        Vector2 panelPosition;
        if (analyticsTutorial || stationaryRecipeTutorial)
        {
            panelPosition = new Vector2(0f, -rootHeight * 0.12f);
        }
        else if (timeControlTutorial)
        {
            float bottomY = -rootHeight * 0.5f + panelHeight * 0.5f + 18f;
            panelPosition = new Vector2(0f, bottomY);
        }
        else if (recipePickerPanel != null)
        {
            Vector3[] pickerCorners = new Vector3[4];
            recipePickerPanel.GetWorldCorners(pickerCorners);
            Canvas pickerCanvas = recipePickerPanel.GetComponentInParent<Canvas>();
            Camera pickerCamera = pickerCanvas != null && pickerCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? pickerCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                RectTransformUtility.WorldToScreenPoint(pickerCamera, pickerCorners[0]), null, out Vector2 pickerMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                RectTransformUtility.WorldToScreenPoint(pickerCamera, pickerCorners[2]), null, out Vector2 pickerMax);
            float leftSpace = pickerMin.x - (-rootWidth * 0.5f);
            float rightSpace = rootWidth * 0.5f - pickerMax.x;
            bool placeLeft = leftSpace >= rightSpace;
            float pickerCalloutGap = running && Steps[stepIndex].requireRecipes ? 36f : 12f;
            float sideX = placeLeft
                ? pickerMin.x - panelWidth * 0.5f - pickerCalloutGap
                : pickerMax.x + panelWidth * 0.5f + pickerCalloutGap;
            panelPosition = new Vector2(sideX, hasTarget ? targetPosition.y : 0f);
        }
        else if (hasTarget)
        {
            bool targetOnLeft = targetPosition.x < 0f;
            float targetCalloutGap = running && Steps[stepIndex].requireRecipes ? 160f : 92f;
            panelPosition = new Vector2(
                targetPosition.x + (targetOnLeft ? 1f : -1f) * (panelWidth * 0.5f + targetCalloutGap),
                targetPosition.y);
            panelPosition.x = Mathf.Clamp(panelPosition.x,
                -rootWidth * 0.5f + panelWidth * 0.5f + 18f,
                rootWidth * 0.5f - panelWidth * 0.5f - 18f);
            panelPosition.y = Mathf.Clamp(panelPosition.y,
                -rootHeight * 0.5f + panelHeight * 0.5f + 76f,
                rootHeight * 0.5f - panelHeight * 0.5f - 18f);
        }
        else
        {
            panelPosition = new Vector2(0f, -rootHeight * 0.5f + panelHeight * 0.5f + 82f);
        }

        // Top-bar callouts sit beneath the navigation, leaving its labels readable.
        if (recipePickerPanel == null && hasTarget && targetPosition.y > rootHeight * 0.5f - 180f
            && (currentHighlight == Highlight.Inventory || currentHighlight == Highlight.Management))
            panelPosition.y = targetPosition.y - panelHeight * 0.5f - 60f;

        // Keep the entire flow editor readable, not just the highlighted button.
        if (recipePickerPanel == null && hasTarget && calloutTarget != null)
        {
            Transform owner = calloutTarget;
            while (owner != null && owner.name != "FlowCaptureHUD") owner = owner.parent;
            if (owner is RectTransform flowHud)
            {
                var corners = new Vector3[4];
                flowHud.GetWorldCorners(corners);
                Canvas targetCanvas = flowHud.GetComponentInParent<Canvas>();
                Camera camera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? targetCanvas.worldCamera : null;
                Vector2 screenBottom = RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[3]) * 0.5f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenBottom, null, out Vector2 bottom);
                panelPosition.x = Mathf.Clamp(targetPosition.x,
                    -rootWidth * 0.5f + panelWidth * 0.5f + 18f,
                    rootWidth * 0.5f - panelWidth * 0.5f - 18f);
                panelPosition.y = bottom.y - panelHeight * 0.5f - 32f;
            }
        }
        // Keep the menu-choice instruction to the left of the Plan card. The
        // highlighted item lives inside that card, so centering the callout on
        // its target makes the tutorial overlap the very control it describes.
        if (running && Steps[stepIndex].requireHamburger && hasTarget
            && ShiftPlanningUI.Instance != null
            && ShiftPlanningUI.Instance.TutorialPlanningCard != null)
        {
            RectTransform planCard = ShiftPlanningUI.Instance.TutorialPlanningCard;
            Vector3[] planCorners = new Vector3[4];
            planCard.GetWorldCorners(planCorners);
            Canvas planCanvas = planCard.GetComponentInParent<Canvas>();
            Camera planCamera = planCanvas != null && planCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? planCanvas.worldCamera : null;
            Vector2 planMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 planMax = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < planCorners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(planCamera, planCorners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rootRect, screenPoint, null, out Vector2 localPoint))
                    continue;
                planMin = Vector2.Min(planMin, localPoint);
                planMax = Vector2.Max(planMax, localPoint);
            }
            if (planMin.x != float.MaxValue)
            {
                panelPosition.y = targetPosition.y;
                float leftSpace = planMin.x + rootWidth * 0.5f;
                float rightSpace = rootWidth * 0.5f - planMax.x;
                bool placeLeft = leftSpace >= rightSpace;
                if (leftSpace >= panelWidth + 28f) placeLeft = true;
                else if (rightSpace >= panelWidth + 28f) placeLeft = false;
                if (leftSpace >= panelWidth + 28f || rightSpace >= panelWidth + 28f)
                {
                    panelPosition.x = placeLeft
                        ? planMin.x - panelWidth * 0.5f - 14f
                        : planMax.x + panelWidth * 0.5f + 14f;
                }
                else
                {
                    float aboveSpace = rootHeight * 0.5f - planMax.y;
                    float belowSpace = planMin.y + rootHeight * 0.5f;
                    panelPosition.x = (planMin.x + planMax.x) * 0.5f;
                    panelPosition.y = aboveSpace >= belowSpace
                        ? planMax.y + panelHeight * 0.5f + 14f
                        : planMin.y - panelHeight * 0.5f - 14f;
                }
            }
            else
                panelPosition.y = targetPosition.y;
        }
        if (running && Steps[stepIndex].requireShiftStarted && hasTarget
            && ShiftPlanningUI.Instance != null
            && ShiftPlanningUI.Instance.TutorialPlanningCard != null)
        {
            RectTransform planCard = ShiftPlanningUI.Instance.TutorialPlanningCard;
            Vector3[] planCorners = new Vector3[4];
            planCard.GetWorldCorners(planCorners);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                RectTransformUtility.WorldToScreenPoint(null, planCorners[0]), null, out Vector2 planBottomLeft);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                RectTransformUtility.WorldToScreenPoint(null, planCorners[1]), null, out Vector2 planTopLeft);
            float planLeft = Mathf.Min(planBottomLeft.x, planTopLeft.x);
            panelPosition.x = planLeft - panelWidth * 0.5f - 14f;
            panelPosition.y = targetPosition.y;
        }
        if (running && Steps[stepIndex].requireTimeControlInteractions && hasTarget)
        {
            panelPosition.x = targetPosition.x;
            panelPosition.y = targetBounds.yMin - panelHeight * 0.5f - 24f;
        }
        if (objectivesPanelRect != null)
        {
            Vector3[] objectivesCorners = new Vector3[4];
            objectivesPanelRect.GetWorldCorners(objectivesCorners);
            Canvas objectivesCanvas = objectivesPanelRect.GetComponentInParent<Canvas>();
            Camera objectivesCamera = objectivesCanvas != null
                && objectivesCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? objectivesCanvas.worldCamera : null;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < objectivesCorners.Length; i++)
            {
                Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(
                    objectivesCamera, objectivesCorners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rootRect, screenCorner, null, out Vector2 localCorner))
                    continue;
                min = Vector2.Min(min, localCorner);
                max = Vector2.Max(max, localCorner);
            }

            if (min.x != float.MaxValue)
                panelPosition = new Vector2(
                    min.x - panelWidth * 0.5f - 14f,
                    (min.y + max.y) * 0.5f);
        }
        // Normal live-service steps are for observation. Shift-start and
        // time-control lessons keep their active UI target and arrow.
        bool observingLiveCustomer = running && Steps[stepIndex].liveCustomer
            && !Steps[stepIndex].requireShiftStarted
            && !Steps[stepIndex].requireTimeControlInteractions;
        if (observingLiveCustomer)
        {
            hasTarget = false;
            // Keep Gus at the tutorial's standard bottom-center card position.
            panelPosition = new Vector2(0f,
                -rootHeight * 0.5f + panelHeight * 0.5f + 82f);
        }
        // Keep the station checklist and flow-linking tutorial card in the
        // same upper-right position while the player works in the kitchen.
        bool creatingFlowTutorial = running && Steps[stepIndex].requireFlow
            && !Steps[stepIndex].requireEditedFlow;
        if (running && (Steps[stepIndex].requireStationChecklist || creatingFlowTutorial))
        {
            panelPosition = new Vector2(
                rootWidth * 0.5f - panelWidth * 0.5f - 24f,
                rootHeight * 0.5f - panelHeight * 0.5f - 170f);
        }
        if (running && stepIndex == 3 && tutorialUnlockContinuePending)
        {
            RectTransform unlockPanel = MilestoneUnlockPopupUI.ActivePanelRect;
            if (unlockPanel != null)
            {
                Vector3[] unlockCorners = new Vector3[4];
                unlockPanel.GetWorldCorners(unlockCorners);
                Canvas unlockCanvas = unlockPanel.GetComponentInParent<Canvas>();
                Camera unlockCamera = unlockCanvas != null
                    && unlockCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? unlockCanvas.worldCamera : null;
                Vector2 unlockMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 unlockMax = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < unlockCorners.Length; i++)
                {
                    Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(unlockCamera, unlockCorners[i]);
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                            screenCorner, null, out Vector2 localCorner))
                        continue;
                    unlockMin = Vector2.Min(unlockMin, localCorner);
                    unlockMax = Vector2.Max(unlockMax, localCorner);
                }

                if (unlockMin.x != float.MaxValue)
                    panelPosition = new Vector2(
                        unlockMax.x + panelWidth * 0.5f + 10f,
                        unlockMax.y + panelHeight * 0.5f + 10f);
            }
            else
            {
                panelPosition = new Vector2(
                    rootWidth * 0.5f - panelWidth * 0.5f - 18f,
                    rootHeight * 0.5f - panelHeight * 0.5f - 18f);
            }
        }
        if (stationaryRecipeTutorial)
            panelPosition = new Vector2(0f, -rootHeight * 0.12f);
        // Apply screen bounds after every contextual adjustment, including bottom-row targets.
        float calloutScreenMargin = running && stepIndex == 3 && tutorialUnlockContinuePending ? 10f : 18f;
        panelPosition.x = Mathf.Clamp(panelPosition.x,
            -rootWidth * 0.5f + panelWidth * 0.5f + calloutScreenMargin,
            rootWidth * 0.5f - panelWidth * 0.5f - calloutScreenMargin);
        panelPosition.y = Mathf.Clamp(panelPosition.y,
            -rootHeight * 0.5f + panelHeight * 0.5f + calloutScreenMargin,
            rootHeight * 0.5f - panelHeight * 0.5f - calloutScreenMargin);
        if (!calloutPanelPositionInitialized)
        {
            if (panelDragHandle != null)
                panelDragHandle.SetAutomaticPosition(panelPosition);
            else
                panelRect.anchoredPosition = panelPosition;
            calloutPanelPositionVelocity = Vector2.zero;
            calloutPanelPositionInitialized = true;
            lastCalloutPanelMotionFrame = Time.frameCount;
        }
        else if (lastCalloutPanelMotionFrame != Time.frameCount)
        {
            lastCalloutPanelMotionFrame = Time.frameCount;
            Vector2 smoothedPosition = Vector2.SmoothDamp(
                panelRect.anchoredPosition,
                panelPosition,
                ref calloutPanelPositionVelocity,
                0.22f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            if (panelDragHandle != null)
                panelDragHandle.SetAutomaticPosition(smoothedPosition);
            else
                panelRect.anchoredPosition = smoothedPosition;

            if ((panelRect.anchoredPosition - panelPosition).sqrMagnitude < 0.25f)
            {
                if (panelDragHandle != null)
                    panelDragHandle.SetAutomaticPosition(panelPosition);
                else
                    panelRect.anchoredPosition = panelPosition;
                calloutPanelPositionVelocity = Vector2.zero;
            }
        }

        // Most UI targets use an outline only. Keep a directional arrow for
        // the station configuration sequence and the other guided click steps.
        bool arrowTargetIsUI = calloutTarget != null
            && calloutTarget.GetComponentInParent<Canvas>() != null
            && !(running && (Steps[stepIndex].requireWorkerDragToFlow
                || Steps[stepIndex].requireRecipes
                || Steps[stepIndex].requireHamburger
                || Steps[stepIndex].requireShiftStarted
                || Steps[stepIndex].requireTimeControlInteractions
                || Steps[stepIndex].requireAnalyticsScreen));
        PositionCalloutArrow(panelRect.anchoredPosition, new Vector2(panelWidth, panelHeight),
            targetPosition, targetBounds, hasTarget && !arrowTargetIsUI);
    }

    bool TryGetCalloutTarget(RectTransform rootRect, out Vector2 localPoint, out Rect localBounds)
    {
        localPoint = Vector2.zero;
        localBounds = new Rect(localPoint, Vector2.zero);
        if (calloutTarget != null && calloutTarget.gameObject.activeInHierarchy)
        {
            Vector3[] corners = new Vector3[4];
            calloutTarget.GetWorldCorners(corners);
            Canvas targetCanvas = calloutTarget.GetComponentInParent<Canvas>();
            Camera eventCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? targetCanvas.worldCamera : null;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,
                        screenCorner, null, out Vector2 localCorner))
                    continue;
                min = Vector2.Min(min, localCorner);
                max = Vector2.Max(max, localCorner);
            }
            if (min.x == float.MaxValue) return false;
            localBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            localPoint = localBounds.center;
            return true;
        }
        else if (highlightTarget != null && Camera.main != null)
        {
            Vector3 worldPoint = GetTutorialWorldAimPoint(highlightTarget);
            Vector3 projected = Camera.main.WorldToScreenPoint(worldPoint);
            if (projected.z <= 0f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, projected, null, out localPoint);
        }
        else
        {
            return false;
        }
    }

    void PositionCalloutArrow(Vector2 panelPosition, Vector2 panelSize, Vector2 targetPosition,
        Rect targetBounds, bool visible)
    {
        if (calloutArrow == null) return;
        Vector2 delta = targetPosition - panelPosition;
        if (!visible || delta.sqrMagnitude < 1f)
        {
            calloutArrow.gameObject.SetActive(false);
            return;
        }

        Vector2 direction = delta.normalized;
        float tx = Mathf.Abs(direction.x) > 0.001f ? panelSize.x * 0.5f / Mathf.Abs(direction.x) : float.MaxValue;
        float ty = Mathf.Abs(direction.y) > 0.001f ? panelSize.y * 0.5f / Mathf.Abs(direction.y) : float.MaxValue;
        Vector2 start = panelPosition + direction * Mathf.Min(tx, ty);
        Vector2 end;
        if (targetBounds.width > 1f && targetBounds.height > 1f)
        {
            Vector2 targetHalfSize = targetBounds.size * 0.5f;
            float edgeX = Mathf.Abs(direction.x) > 0.001f
                ? targetHalfSize.x / Mathf.Abs(direction.x) : float.MaxValue;
            float edgeY = Mathf.Abs(direction.y) > 0.001f
                ? targetHalfSize.y / Mathf.Abs(direction.y) : float.MaxValue;
            float edgeDistance = Mathf.Min(edgeX, edgeY);
            end = targetPosition - direction * (edgeDistance + 7f);
        }
        else
        {
            end = targetPosition - direction * 16f;
        }
        float length = Vector2.Distance(start, end);
        if (length < 1f)
        {
            calloutArrow.gameObject.SetActive(false);
            return;
        }

        calloutArrow.gameObject.SetActive(true);
        calloutArrow.anchorMin = calloutArrow.anchorMax = calloutArrow.pivot = new Vector2(0.5f, 0.5f);
        calloutArrow.anchoredPosition = (start + end) * 0.5f;
        calloutArrow.sizeDelta = new Vector2(length, 34f);
        calloutArrow.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    void RefreshAdvanceGate()
    {
        if (nextButton == null) return;
        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireAnalyticsScreen)
        {
            bool analyticsOpen = analyticsScreenOpened && !analyticsScreenClosed
                && AnalyticsPopupUI.IsOpen;
            bool touring = analyticsOpen && analyticsTutorialStage >= 0
                && analyticsTutorialStage < AnalyticsTourCloseStage;
            bool reviewComplete = StepRequirementMet();
            nextButton.gameObject.SetActive(touring || reviewComplete);
            nextButton.interactable = touring || reviewComplete;
            if (nextLabel != null)
                nextLabel.text = reviewComplete ? Steps[stepIndex].nextLabel : "Next";
            Image analyticsButtonImage = nextButton.GetComponent<Image>();
            if (analyticsButtonImage != null)
                analyticsButtonImage.color = touring || reviewComplete
                    ? new Color(0.22f, 0.55f, 0.38f, 1f)
                    : new Color(0.22f, 0.24f, 0.28f, 1f);
            return;
        }

        bool freezerGuideNext = Steps[stepIndex].requireStationChecklist
            && IsFreezerExplanationPhase(freezerWalkthroughPhase);
        bool ready = freezerGuideNext || StepRequirementMet();
        bool canOpenRecipes = !ready && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireRecipeScreen;
        bool canOpenObjectives = !ready && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireObjectivesScreen && !objectivesScreenOpened;
        bool canOpenRequiredScreen = canOpenRecipes || canOpenObjectives;
        nextButton.interactable = ready || canOpenRequiredScreen;
        var img = nextButton.GetComponent<Image>();
        if (img != null)
            img.color = ready || canOpenRequiredScreen
                ? new Color(0.22f, 0.55f, 0.38f, 1f)
                : new Color(0.22f, 0.24f, 0.28f, 1f);

        if (nextLabel != null && stepIndex >= 0 && stepIndex < Steps.Length)
        {
            var step = Steps[stepIndex];
            string labelText = ready ? freezerGuideNext ? "Next" : step.nextLabel : LockedLabel(step);
            nextLabel.text = labelText;
            bool cameraMovePrompt = labelText == "Move camera with WASD";
            nextLabel.enableAutoSizing = !cameraMovePrompt;
            if (cameraMovePrompt)
                nextLabel.fontSize = 13f;
            else
            {
                nextLabel.fontSizeMin = 16f;
                nextLabel.fontSizeMax = 22f;
            }
            if (ready && step.requirePlaced)
                UpdateHighlight(step.highlight);
        }
    }

    string LockedLabel(Step step)
    {
        if (step.highlight == Highlight.Inventory) return "Open Build";
        if (step.requireGlossaryTermClick && !glossaryTermClicked)
            return "Click the highlighted ISE term";
        if (step.requireObjectivesScreen && !objectivesScreenOpened)
            return "Open Objectives";
        if (step.requireStationChecklist && !TutorialChecklistStationsPlaced())
            return "Place the seven stations";
        if (step.requireResearch) return "Research all three foundations";
        if (step.requireRecipeScreen) return "Open Recipes";
        if (step.requireAnalyticsScreen) return "Click Analytics";
        if (step.requireMenuScreen && !menuScreenOpened) return "Open Menu";
        if (step.requireHamburger) return "Add Hamburger to the menu";
        if (step.requireOptimizedLayout)
            return inheritedStationMoved
                ? "Reach " + inheritedTargetRate.ToString("0.0") + " burgers/min"
                : "Move a station";
        if (step.requireShiftStarted) return "Start Shift";
        if (step.requireCameraControls)
        {
            if (!tutorialCameraMoved) return "Move camera with WASD";
            if (!tutorialCameraRotated) return "Rotate with right mouse + drag";
            if (!tutorialCameraZoomed) return "Zoom with scroll wheel";
        }
        if (step.requireTimeControlInteractions)
        {
            if (!tutorialPauseControlUsed) return "Click Pause";
            if (!tutorialPlayControlUsed) return "Click Play";
            if (!tutorialFastForwardControlUsed) return "Click Fast Forward";
            if (!AllTutorialFastForwardRatesVisited()) return "Cycle through each fast-forward speed";
        }
        if (step.requireOrderServed && !orderServed) return "Serve the burger";
        if (step.requirePickupDelivery && !burgerArrivedAtPickup) return "Wait for the burger at Pickup";
        if (step.requireCustomerJourney && !customerJourneyComplete) return "Follow the customer to the exit";
        if (step.requireIngredients) return "Order Raw Patties and Buns";
        if (step.requireRecipes) return MissingRecipeLabel();
        if (step.requireEditedFlow) return "Edit and save flow";
        if (step.requireHiredWorker)
            return "Hire a worker";
        if (step.requireWorkerDragToFlow && !workerDraggedToFlow)
            return "Drag worker card to Drop Worker Here";
        if (step.requireWorkerOnFlow)
            return "Assign a worker";
        if (step.requireFlow)
            return "Create a flow";
        if (step.requireAllStations)
            return "Place all stations";
        switch (step.highlight)
        {
            case Highlight.Register: return "Place register on counter";
            case Highlight.Freezer: return "Place freezer";
            case Highlight.Grill: return "Place grill";
            case Highlight.Fryer: return "Place fryer";
            case Highlight.Drink: return "Place cutting station";
            case Highlight.Assembly: return "Place assembly";
            case Highlight.HeatLamp: return "Place Pickup Station";
            case Highlight.Pantry: return "Place pantry";
            default: return "Place station";
        }
    }

    static bool IsHamburgerEnabled()
    {
        CustomerOrderConfig config = ProductionManager.Instance != null
            ? ProductionManager.Instance.orderConfig : null;
        return config != null && config.burgerBase != null && config.IsItemEnabled(config.burgerBase);
    }

    static Register FindTutorialRegister()
    {
        Register[] registers = FindObjectsByType<Register>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Register register in registers)
        {
            if (register == null || !register.IsPlacedRegister) continue;
            if (Instance != null && Instance.starterKitchen.Contains(register.gameObject)) continue;
            if (register.GetComponent<PlacedBuildItem>() != null) return register;
        }
        return null;
    }

    bool StepRequirementMet()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return true;
        var step = Steps[stepIndex];
        if (step.requireGlossaryTermClick && !glossaryTermClicked) return false;
        if (step.requireObjectivesScreen && !objectivesScreenOpened) return false;
        if (step.requireCameraControls
            && (!tutorialCameraMoved || !tutorialCameraRotated || !tutorialCameraZoomed))
            return false;
        if (step.highlight == Highlight.Inventory)
        {
            var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventoryUI == null || !inventoryUI.IsPanelOpen) return false;
        }
        if (step.requireIngredients && !HasTutorialIngredients()) return false;
        if (step.requireResearch && (!researchScreenOpened || !IsTutorialResearchComplete())) return false;
        if (step.requireRecipeScreen && !recipeScreenOpened) return false;
        if (step.requireAnalyticsScreen
            && (!analyticsScreenOpened || !analyticsScreenClosed
                || analyticsTutorialStage < AnalyticsTourCloseStage)) return false;
        if (step.requireMenuScreen && !menuScreenOpened) return false;
        if (step.requireHamburger && !IsHamburgerEnabled()) return false;
        if (step.requireOptimizedLayout && (!inheritedStationMoved
            || inheritedTargetRate <= 0f
            || CurrentInheritedOutputRate() + 0.05f < inheritedTargetRate)) return false;
        if (step.requireStationChecklist && !TutorialChecklistStationsPlaced()) return false;
        if (step.requireShiftStarted && !tutorialShiftStarted) return false;
        if (step.requireOrderServed && !orderServed) return false;
        if (step.requirePickupDelivery && !burgerArrivedAtPickup) return false;
        if (step.requireCustomerJourney && !customerJourneyComplete) return false;
        if (step.requireStationHealthReview && !HasSelectedProductionStation()) return false;
        if (step.requireTimeControlInteractions
            && (!tutorialPauseControlUsed || !tutorialPlayControlUsed || !tutorialFastForwardControlUsed
                || !AllTutorialFastForwardRatesVisited()))
            return false;
        if (step.requireTimeControlInteractions
            && tutorialVisibleCharacters < tutorialTotalCharacters)
            return false;
        if (step.requireRecipes && (!recipeBusinessOpened || !BurgerRecipesReady())) return false;
        if (step.requireEditedFlow && !flowEditCompleted) return false;
        if ((step.requireFlow || step.requireWorkerOnFlow || step.requireEditedFlow)
            && ManagementModeController.Instance != null && ManagementModeController.Instance.IsCapturingFlow)
            return false;
        if (step.requireAllStations && !AllTutorialStationsPlaced())
            return false;
        if (step.requirePlaced && !HasPlacedStation(step.highlight))
            return false;
        if (step.requireHiredWorker && !HasHiredWorker())
            return false;
        if (step.requireFlow && (!flowCreateCompleted || !HasTutorialFlow()))
            return false;
        if (step.requireWorkerOnFlow && (!HasWorkerOnFlow()
            || step.requireWorkerDragToFlow && !workerDraggedToFlow))
            return false;
        return true;
    }

    static bool AutomaticallyAdvances(Step step)
    {
        return (step.liveCustomer && !step.requireOrderServed) || step.requirePlaced || step.requireStationChecklist || step.requireAllStations
            || step.requireFlow || step.requireWorkerOnFlow || step.requireHiredWorker
            || step.requireEditedFlow || step.requireRecipes || step.requireIngredients
            || step.requireResearch || step.requireHamburger
            || step.requireShiftStarted || step.requirePickupDelivery || step.requireCustomerJourney
            || step.requireOrderServed;
    }

    bool TutorialChecklistStationsPlaced()
    {
        if (!stationChecklistInitialized || Time.unscaledTime >= nextStationChecklistRefresh)
            RefreshStationChecklist();
        return stationChecklistComplete;
    }

    void RefreshStationChecklist()
    {
        if (!stationChecklistInitialized || Time.unscaledTime >= nextStationChecklistRefresh)
        {
            stationChecklistStatus[0] = HasPlacedStation(Highlight.Freezer);
            stationChecklistStatus[1] = HasPlacedStation(Highlight.Grill);
            stationChecklistStatus[2] = HasPlacedStation(Highlight.Pantry);
            stationChecklistStatus[3] = HasPlacedStation(Highlight.Assembly);
            stationChecklistStatus[4] = HasPlacedStation(Highlight.Register);
            stationChecklistStatus[5] = HasPlacedStation(Highlight.Fryer);
            stationChecklistStatus[6] = HasPlacedStation(Highlight.HeatLamp);
            stationChecklistComplete = true;
            for (int i = 0; i < stationChecklistStatus.Length; i++)
                stationChecklistComplete &= stationChecklistStatus[i];
            stationChecklistInitialized = true;
            nextStationChecklistRefresh = Time.unscaledTime + 0.25f;
        }

        if (coreStationChecklistText == null) return;
        string text = ChecklistLine("Freezer", stationChecklistStatus[0]) + "\n"
            + ChecklistLine("Grill", stationChecklistStatus[1]) + "\n"
            + ChecklistLine("Pantry", stationChecklistStatus[2]) + "\n"
            + ChecklistLine("Assembly Station", stationChecklistStatus[3]) + "\n"
            + ChecklistLine("Register", stationChecklistStatus[4]) + "\n"
            + ChecklistLine("Fryer", stationChecklistStatus[5]) + "\n"
            + ChecklistLine("Pickup Station", stationChecklistStatus[6]);
        if (coreStationChecklistText.text != text)
            coreStationChecklistText.text = text;
    }

    float CurrentInheritedOutputRate()
    {
        if (nextInheritedForecastRefresh > Time.unscaledTime)
            return inheritedCurrentRate;
        nextInheritedForecastRefresh = Time.unscaledTime + 0.5f;
        ProductionManager production = ProductionManager.Instance;
        inheritedCurrentRate = production != null && production.orderConfig != null
            ? production.GetPredictedOutputPerMinute(production.orderConfig.burgerBase) : 0f;
        return inheritedCurrentRate;
    }

    void RefreshInheritedLayoutText()
    {
        if (coreStationChecklistText == null) return;
        float current = CurrentInheritedOutputRate();
        string text = "<color=#B9C5D2>BASELINE  " + inheritedBaselineRate.ToString("0.0") + "/min</color>"
            + "   <color=#63E6FF><b>CURRENT  " + current.ToString("0.0") + "/min</b></color>"
            + "   <color=#FFE45C><b>TARGET  " + inheritedTargetRate.ToString("0.0") + "/min</b></color>\n"
            + (inheritedStationMoved ? "Station moved" : "Open Build Menu and move a station")
            + "\n" + (current + 0.05f >= inheritedTargetRate && inheritedTargetRate > 0f
                ? "<mark=#1D6849 padding=\"5,3,5,3\"><color=#A8FFD4><b>✓ OUTPUT TARGET REACHED</b></color></mark>"
                : "<color=#FFF04A>Shorten the worker route</color>");
        if (coreStationChecklistText.text != text)
            coreStationChecklistText.text = text;
    }

    static string ChecklistLine(string label, bool complete)
    {
        string marker = complete
            ? "<color=#59D6A6>[x]</color>"
            : "<color=#AEB7C4>[ ]</color>";
        return marker + "  Buy and place " + label;
    }

    static string TimeControlChecklistLine(string label, string description, bool complete)
    {
        string marker = complete
            ? "<color=#59D6A6>[x]</color>"
            : "<color=#AEB7C4>[ ]</color>";
        return marker + "  " + label + ": " + description;
    }

    void RefreshTimeControlChecklist()
    {
        if (coreStationChecklistText == null) return;
        string text = TimeControlChecklistLine("Pause", "inspect the system", tutorialPauseControlUsed) + "\n"
            + TimeControlChecklistLine("Play", "resume normal speed", tutorialPlayControlUsed) + "\n"
            + TimeControlChecklistLine("Fast Forward", "speed through the shift", tutorialFastForwardControlUsed);
        GameTimeManager clock = GameTimeManager.Instance;
        int rateCount = clock != null ? Mathf.Min(clock.FastForwardRateCount, 10) : 0;
        for (int i = 0; i < rateCount; i++)
        {
            float rate = GetTutorialFastForwardRate(clock, i);
            if (rate > 3f) continue;
            bool visited = (tutorialFastForwardRateMask & (1 << i)) != 0;
            text += "\n" + TimeControlChecklistLine(rate.ToString("0.##") + "x", "fast-forward rate", visited);
        }
        if (coreStationChecklistText.text != text)
            coreStationChecklistText.text = text;
    }

    string GetTimeControlInstruction()
    {
        if (!tutorialPauseControlUsed)
            return "<b>Pause</b> stops simulation time so you can inspect the process. Click Pause.";
        if (!tutorialPlayControlUsed)
            return "<b>Play</b> resumes the shift at normal speed. Click Play.";
        if (!tutorialFastForwardControlUsed)
            return "<b>Fast Forward</b> speeds through the shift so you can observe the process sooner. Click Fast Forward.";
        if (!AllTutorialFastForwardRatesVisited())
            return "Fast-forward is running. Try each multiplier up to 3x; you do not need to test the higher speeds.";
        if (!orderServed)
            return "Controls checked. Let Fast Forward run until the customer collects the burger.";
        return "Controls checked and the customer collected the burger. Moving on to Analytics.";
    }

    void RecordTutorialFastForwardRate(int index)
    {
        if (index < 0 || index >= 30) return;
        GameTimeManager clock = GameTimeManager.Instance;
        if (clock == null || GetTutorialFastForwardRate(clock, index) > 3f) return;
        tutorialFastForwardRateMask |= 1 << index;
        RefreshTimeControlChecklist();
    }

    static float GetTutorialFastForwardRate(GameTimeManager clock, int index)
    {
        if (clock == null) return 1f;
        return clock.fastForwardRates != null && index >= 0 && index < clock.fastForwardRates.Length
            ? clock.fastForwardRates[index] : clock.SelectedFastForwardRate;
    }

    bool AllTutorialFastForwardRatesVisited()
    {
        GameTimeManager clock = GameTimeManager.Instance;
        int rateCount = clock != null ? Mathf.Min(clock.FastForwardRateCount, 30) : 0;
        int requiredMask = 0;
        for (int i = 0; i < rateCount; i++)
            if (GetTutorialFastForwardRate(clock, i) <= 3f)
                requiredMask |= 1 << i;
        return (tutorialFastForwardRateMask & requiredMask) == requiredMask;
    }

    string GetAnalyticsTourInstruction()
    {
        switch (analyticsTutorialStage)
        {
            case 0: return "<b>Customers in Store</b> counts customers currently waiting or being served.";
            case 1: return "<b>Order to Pickup</b> is average time from register order to collecting the full order.";
            case 2: return "<b>Orders Served / Min</b> measures completed customer orders per minute.";
            case 3: return "<b>Service Rate</b> is the share of customers served instead of lost.";
            case 4: return "<b>Arrivals this hour</b> compares actual customer arrivals with planned arrivals so far.";
            case 5: return "<b>Item</b> names the menu product tracked by each row. Click an <b>i</b> for a column definition.";
            case 6: return "<b>Open Orders</b> counts register orders still waiting for pickup.";
            case 7: return "<b>Ordered / Min</b> is the recent rate customers ordered this item at the register.";
            case 8: return "<b>Demand / Min</b> is the forecast rate of customer demand for this item.";
            case 9: return "<b>Produced / Min</b> counts units delivered to Pickup Stations in the last 60 seconds.";
            case 10: return "<b>Predicted / Min</b> estimates steady flow output using timing, supply, and pickup space.";
            case 11: return "<b>Reputation / Next Shift</b> shows item reputation and the demand change applied next shift.";
            default: return analyticsScreenOpened && !analyticsScreenClosed && AnalyticsPopupUI.IsOpen
                ? "Close Analytics. Then click <b>Finish</b> to complete the tutorial."
                : "Open Analytics again to close it and finish the walkthrough.";
        }
    }

    void RefreshTimeControlTutorialStep()
    {
        if (!running || stepIndex < 0 || stepIndex >= Steps.Length
            || !Steps[stepIndex].requireTimeControlInteractions)
            return;
        RefreshTimeControlChecklist();
        SetTutorialBodyText(GetTimeControlInstruction());
        nextControlHighlightRefresh = 0f;
        RefreshControlHighlights();
        UpdateContextualCallout();
    }

    void RefreshAutomaticAdvance()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        Step step = Steps[stepIndex];
        // Observation steps remain open. Shift-start, pickup-delivery, and
        // completed-customer steps advance once their requirements are met.
        bool waitingForServedOrder = step.liveCustomer && !step.requireOrderServed
            && !step.requireShiftStarted && !step.requireCustomerJourney;
        if (!AutomaticallyAdvances(step) || waitingForServedOrder || !StepRequirementMet())
        {
            autoAdvanceStep = -1;
            return;
        }

        if (autoAdvanceStep != stepIndex)
        {
            autoAdvanceStep = stepIndex;
            autoAdvanceAt = Time.unscaledTime + 0.45f;
            return;
        }

        if (Time.unscaledTime < autoAdvanceAt) return;
        autoAdvanceStep = -1;
        OnNext();
    }

    static bool HasTutorialIngredients()
    {
        ProductionManager production = ProductionManager.Instance;
        KitchenInventory inventory = KitchenInventory.Instance;
        if (production == null || production.orderConfig == null || inventory == null)
            return false;

        CustomerOrderConfig config = production.orderConfig;
        AssemblyRecipeDefinition burgerRecipe = config.GetAssemblyRecipe(config.burgerBase);
        ItemDefinition patty = config.rawPattyIngredient;
        ItemDefinition bun = config.GetAssemblySupplySource(burgerRecipe);
        if (patty == null || bun == null) return false;

        IngredientDeliveryService delivery = IngredientDeliveryService.Instance;
        int patties = inventory.GetCount(patty) + (delivery != null ? delivery.GetIncomingCount(patty) : 0);
        int buns = inventory.GetCount(bun) + (delivery != null ? delivery.GetIncomingCount(bun) : 0);
        return patties > 0 && buns > 0;
    }

    bool HasHiredWorker()
    {
        var production = ProductionManager.Instance != null ? ProductionManager.Instance : FindFirstObjectByType<ProductionManager>();
        if (production == null || production.employees == null) return false;
        for (int i = 0; i < production.employees.Count; i++)
        {
            if (production.employees[i] != null && production.employees[i].gameObject.activeInHierarchy)
                return true;
        }
        return false;
    }

    bool HasTutorialFlow()
    {
        var production = ProductionManager.Instance != null ? ProductionManager.Instance : FindFirstObjectByType<ProductionManager>();
        if (production == null || production.productionFlows == null) return false;
        for (int i = 0; i < production.productionFlows.Count; i++)
        {
            var flow = production.productionFlows[i];
            if (flow == null) continue;
            flow.Clean();
            if (IsBurgerTutorialFlow(flow))
                return true;
        }
        return false;
    }

    bool HasWorkerOnFlow()
    {
        var production = ProductionManager.Instance != null ? ProductionManager.Instance : FindFirstObjectByType<ProductionManager>();
        if (production == null || production.productionFlows == null) return false;
        for (int i = 0; i < production.productionFlows.Count; i++)
        {
            var flow = production.productionFlows[i];
            if (flow == null) continue;
            flow.Clean();
            if (!IsBurgerTutorialFlow(flow) || flow.workers == null) continue;
            foreach (var worker in flow.workers)
                if (worker != null && worker.gameObject.activeInHierarchy
                    && production.employees != null && production.employees.Contains(worker))
                    return true;
        }
        return false;
    }

    bool IsWorkerAssignmentStepIncomplete(Step step)
    {
        return !HasWorkerOnFlow()
            || (step.requireWorkerDragToFlow && !workerDraggedToFlow);
    }

    public static void NotifyWorkerDroppedOnFlow()
    {
        OnboardingTutorial tutorial = Instance;
        if (tutorial == null || !tutorial.running || tutorial.stepIndex < 0
            || tutorial.stepIndex >= Steps.Length
            || !Steps[tutorial.stepIndex].requireWorkerDragToFlow)
            return;

        tutorial.workerDraggedToFlow = true;
        tutorial.RefreshAdvanceGate();
    }

    static bool IsBurgerTutorialFlow(ProductionFlowPlan flow)
    {
        if (flow == null) return false;
        flow.EnsureLegacyConnections();
        foreach (var assembly in flow.stations)
        {
            if (!IsPlacedFlowStation<AssemblyStation>(assembly)) continue;
            bool pantry = false, cooked = false, pickup = false;
            foreach (var edge in flow.connections)
            {
                if (edge.to == assembly && IsPlacedFlowStation<PantryStation>(edge.from)) pantry = true;
                if (edge.from == assembly && IsPlacedFlowStation<HeatLampStation>(edge.to)) pickup = true;
                if (edge.to != assembly || !IsPlacedFlowStation<GrillStation>(edge.from)) continue;
                foreach (var input in flow.connections)
                    if (input.to == edge.from && IsPlacedFlowStation<FreezerStation>(input.from)) cooked = true;
            }
            if (pantry && cooked && pickup) return true;
        }
        return false;
    }

    static bool IsPlacedFlowStation<T>(GameObject station) where T : Component =>
        station != null && station.activeInHierarchy && station.GetComponent<PlacedBuildItem>() != null
        && station.GetComponent<T>() != null;

    bool BurgerRecipesReady() => MissingRecipeLabel() == null;

    static string MissingRecipeLabel()
    {
        var config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        if (config == null || config.burgerBase == null || config.rawPattyIngredient == null)
            return "Set ingredients and recipes";
        var recipe = config.GetAssemblyRecipe(config.burgerBase);
        if (recipe == null || config.GetAssemblySupplySource(recipe) == null)
            return "Set ingredients and recipes";

        bool freezer = false, pantry = false, grill = false, assembly = false;
        foreach (var s in FindObjectsByType<FreezerStation>(FindObjectsSortMode.None))
            freezer |= IsPlacedFlowStation<FreezerStation>(s.gameObject) && s.selectedItem == config.rawPattyIngredient;
        foreach (var s in FindObjectsByType<PantryStation>(FindObjectsSortMode.None))
            pantry |= IsPlacedFlowStation<PantryStation>(s.gameObject) && s.CanDispense(config.GetAssemblySupplySource(recipe));
        foreach (var s in FindObjectsByType<GrillStation>(FindObjectsSortMode.None))
            grill |= IsPlacedFlowStation<GrillStation>(s.gameObject) && s.GetSelectedOutput() == config.cookedPattyIngredient;
        foreach (var s in FindObjectsByType<AssemblyStation>(FindObjectsSortMode.None))
            assembly |= IsPlacedFlowStation<AssemblyStation>(s.gameObject) && s.GetSelectedRecipe() == recipe;

        if (freezer && pantry && grill && assembly) return null;
        if (!freezer && !pantry && !grill && !assembly) return "Set ingredients and recipes";

        var missing = new List<string>();
        if (!freezer) missing.Add("freezer");
        if (!pantry) missing.Add("pantry");
        if (!grill) missing.Add("grill");
        if (!assembly) missing.Add("assembly");
        return "Set " + string.Join(", ", missing);
    }

    public static void NotifyFlowSaved(ProductionFlowPlan flow, bool wasEdit)
    {
        if (!IsActive || flow == null || flow.stations == null || flow.stations.Count < 2)
            return;

        if (wasEdit)
            Instance.flowEditCompleted = true;
        else if (Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireFlow && IsBurgerTutorialFlow(flow))
            Instance.flowCreateCompleted = true;
    }

    bool AllTutorialStationsPlaced()
    {
        if (!TutorialChecklistStationsPlaced()) return false;
        // Ensure the customer demonstration cannot start with a missing
        // checklist station.
        foreach (var step in Steps)
            if (step.requirePlaced && !HasPlacedStation(step.highlight))
                return false;
        return true;
    }

    bool HasPlacedStation(Highlight kind)
    {
        switch (kind)
        {
            case Highlight.Register:
                // Only a placed purchase counts, never the starter or placement preview.
                var registers = FindObjectsByType<Register>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var register in registers)
                    if (!starterKitchen.Contains(register.gameObject)
                        && register.GetComponent<PlacedBuildItem>() != null)
                        return true;
                return false;
            case Highlight.Freezer: return HasActiveStation<FreezerStation>();
            case Highlight.Grill: return HasActiveStation<GrillStation>();
            case Highlight.Fryer: return HasActiveStation<FryerStation>();
            case Highlight.Drink: return HasActiveStation<CuttingStation>();
            case Highlight.Assembly: return HasActiveStation<AssemblyStation>();
            case Highlight.HeatLamp: return HasActiveStation<HeatLampStation>();
            case Highlight.Pantry: return HasActiveStation<PantryStation>();
            default: return true;
        }
    }

    bool HasActiveStation<T>() where T : MonoBehaviour
    {
        var found = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null) continue;
            if (starterKitchen.Contains(found[i].gameObject)) continue;
            if (found[i].GetComponent<PlacedBuildItem>() == null) continue;
            return true;
        }
        return false;
    }

    void ApplyPause(bool pauseSim)
    {
        var time = GameTimeManager.Instance;
        if (time == null) return;
        if (pauseSim)
            time.RequestExternalPause(PauseSource);
        else
            time.ReleaseExternalPause(PauseSource);
    }

    void SpawnPracticeCustomer()
    {
        if (customerJourneyComplete) return;
        if (liveCustomerSpawned) return;
        if (ShiftPlanningUI.IsPlanning) return;
        var existing = FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
        {
            liveCustomerSpawned = true;
            if (stepIndex >= 0 && stepIndex < Steps.Length && Steps[stepIndex].requireCustomerJourney)
                BeginTutorialCustomerFollow(existing[0]);
            return;
        }

        var spawner = FindFirstObjectByType<CustomerSpawner>();
        if (spawner != null)
            liveCustomerSpawned = spawner.SpawnTutorialCustomer();
        if (liveCustomerSpawned && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireCustomerJourney)
        {
            CustomerAI[] spawned = FindObjectsByType<CustomerAI>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (spawned != null && spawned.Length > 0)
                BeginTutorialCustomerFollow(spawned[0]);
        }
    }

    void BeginTutorialCustomerFollow(CustomerAI customer)
    {
        if (customer == null) return;
        tutorialCustomer = customer;
        customer.patienceDuration = Mathf.Max(customer.patienceDuration, 180f);
        customerJourneyStage = -1;
        if (tutorialFollowCamera == null)
            tutorialFollowCamera = FindFirstObjectByType<PlayerCameraController>(FindObjectsInactive.Exclude);
        if (tutorialFollowCamera != null)
            tutorialFollowCamera.BeginLockedFollow(customer.transform);
    }

    void ClearTutorialCustomerFollow()
    {
        if (tutorialFollowCamera != null)
            tutorialFollowCamera.EndLockedFollow();
        tutorialFollowCamera = null;
    }

    bool HasTutorialBurgerAtPickup()
    {
        ItemDefinition burger = ProductionManager.Instance != null
            && ProductionManager.Instance.orderConfig != null
                ? ProductionManager.Instance.orderConfig.burgerBase : null;
        if (burger == null) return false;
        HeatLampStation[] pickupStations = FindObjectsByType<HeatLampStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (HeatLampStation station in pickupStations)
            if (station != null && station.GetOutputCount(burger) > 0)
                return true;
        return false;
    }

    void RefreshCustomerJourneyCard()
    {
        if (!running || stepIndex < 0 || stepIndex >= Steps.Length
            || !Steps[stepIndex].requireCustomerJourney)
            return;

        if (tutorialCustomer == null)
        {
            if (!liveCustomerSpawned || customerJourneyComplete)
                return;
            if (!tutorialCustomerOrderServed)
            {
                QueueCustomerJourneyThroughStage(4);
                liveCustomerSpawned = false;
                ShowNextCustomerJourneyMessage();
                return;
            }

            if (!customerJourneyCustomerGone)
            {
                customerJourneyCustomerGone = true;
                customerJourneyStage = 5;
                ClearTutorialCustomerFollow();
                customerJourneyCardQueue.Enqueue(
                    "The customer collected the burger and left. The order is complete and the sale is recorded.");
                BeginCustomerJourneyActionMessage();
            }
            ShowNextCustomerJourneyMessage();
            return;
        }

        int stage = tutorialCustomer.IsLeaving
            ? tutorialCustomerOrderServed ? 3 : 4
            : tutorialCustomer.IsAtPickupQueueSlot ? 2
            : tutorialCustomer.HasOrderedAtRegister ? 1 : 0;
        QueueCustomerJourneyThroughStage(stage);
        ShowNextCustomerJourneyMessage();
    }

    void QueueCustomerJourneyThroughStage(int stage)
    {
        while (customerJourneyStage < stage)
        {
            customerJourneyStage++;
            switch (customerJourneyStage)
            {
                case 0:
                    customerJourneyCardQueue.Enqueue(
                        "The customer enters through the door and joins the register queue. The register records the order.");
                    break;
                case 1:
                    customerJourneyCardQueue.Enqueue(
                        "The register accepts the order. Patience starts, and the customer moves to the Pickup queue.");
                    break;
                case 2:
                    customerJourneyCardQueue.Enqueue(
                        "At Pickup, the customer waits for the worker to deliver the burger. Ordering and collection happen in separate queues.");
                    break;
                case 3:
                    customerJourneyCardQueue.Enqueue(
                        "The customer collected the burger and is leaving. The completed pickup records the sale.");
                    break;
                case 4:
                    customerJourneyCardQueue.Enqueue(
                        "This customer left before receiving the burger. The tutorial will send another customer to try the process.");
                    break;
            }
        }
        if (customerJourneyCardQueue.Count > 0)
            BeginCustomerJourneyActionMessage();
    }

    void BeginCustomerJourneyActionMessage()
    {
        GameTimeManager.Instance?.RequestExternalPause(CustomerJourneyPauseSource);
        if (customerJourneyActionMessageActive || customerJourneyCardQueue.Count == 0)
            return;

        string message = customerJourneyCardQueue.Dequeue();
        SetTutorialBodyText(message);
        ResetTutorialTextReveal();
        customerJourneyMessageHoldUntil = 0f;
        customerJourneyActionMessageActive = true;
        if (customerJourneyCustomerGone && customerJourneyCardQueue.Count == 0)
            customerJourneyFinalMessageDisplayed = true;
    }

    void ShowNextCustomerJourneyMessage()
    {
        if (!customerJourneyActionMessageActive && customerJourneyCardQueue.Count > 0)
            BeginCustomerJourneyActionMessage();

        if (customerJourneyActionMessageActive
            && tutorialVisibleCharacters >= tutorialTotalCharacters)
        {
            if (customerJourneyMessageHoldUntil <= 0f)
                customerJourneyMessageHoldUntil = Time.unscaledTime + 4.5f;
            else if (Time.unscaledTime >= customerJourneyMessageHoldUntil)
            {
                customerJourneyActionMessageActive = false;
                customerJourneyMessageHoldUntil = 0f;
                if (customerJourneyCardQueue.Count > 0)
                    BeginCustomerJourneyActionMessage();
                else
                    GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
            }
        }

        if (!customerJourneyCustomerGone || !customerJourneyFinalMessageDisplayed
            || customerJourneyCardQueue.Count > 0
            || customerJourneyActionMessageActive
            || tutorialVisibleCharacters < tutorialTotalCharacters
            || customerJourneyComplete)
            return;

        customerJourneyComplete = true;
        ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
        RefreshAdvanceGate();
    }

    void Complete(bool skipped)
    {
        running = false;
        pendingStart = false;
        currentHighlight = Highlight.None;
        GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
        PlayerPrefs.SetInt(PrefsCompleteKey, 1);
        PlayerPrefs.Save();
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.TutorialCompleted);

        HideUI();
        ClearHighlight();

        if (skipped)
            RestoreStarterKitchen();

        var milestones = MilestoneProgressManager.Instance;
        if (milestones != null)
            milestones.NotifyOnboardingFinished();

        Sfx.Play(skipped ? SfxId.UiClose : SfxId.MissionComplete);
        var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUI != null) inventoryUI.RefreshAll();

        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureCustomerEntrance();

        // The tutorial customer is practice only. Begin the real campaign day at
        // opening time and show its Plan card before normal arrivals can start.
        CustomerAI.DespawnAllCustomers();
        StoreStatisticsManager.Instance?.BeginDay();

        // The tutorial finishes from Analytics while the Business panel is still
        // open underneath it. Close both management panels before showing Plan so
        // their full-height backdrop cannot cover the Build / Business navigation.
        InventoryUI openInventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (openInventory != null && openInventory.IsPanelOpen)
            openInventory.TogglePanel();
        ManagementScreenController openManagement = FindFirstObjectByType<ManagementScreenController>(
            FindObjectsInactive.Include);
        if (openManagement != null && openManagement.IsOpen)
            openManagement.Close();

        ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();

        if (GameTimeManager.Instance != null)
        {
            GameTimeManager.Instance.SetSpeed(GameTimeManager.SpeedMode.Play);
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
            GameTimeManager.Instance.ReleaseExternalPause(CustomerJourneyPauseSource);
        }

        // Reset the deterministic arrival schedule. The Plan phase pause holds
        // the clock until the player explicitly starts the first shift.
        var spawners = FindObjectsByType<CustomerSpawner>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < spawners.Length; i++)
            if (spawners[i] != null)
                spawners[i].ResumeAfterTutorial();
    }

    void PrepareEmptyKitchen()
    {
        CacheStarterKitchen();
        for (int i = 0; i < starterKitchen.Count; i++)
        {
            if (starterKitchen[i] == null) continue;
            if (starterKitchen[i].GetComponent<CustomerWallDoor>() != null)
                continue;
            starterKitchen[i].SetActive(false);
        }
        starterKitchenHidden = true;

        var stock = KitchenInventory.Instance != null ? KitchenInventory.Instance : FindFirstObjectByType<KitchenInventory>();
        if (stock != null)
            stock.ClearAllStock();

        ClearExtraDoors();
        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureCustomerEntrance();
        ResyncGridSoon();
    }

    void PrepareInheritedKitchen()
    {
        CacheStarterKitchen();
        DestroyTutorialPlacements();
        starterKitchenHidden = false;
        ClearExtraDoors();
        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureTutorialBurgerStations();
        starterKitchen.Clear();
        CacheStarterKitchen();

        ProductionManager production = ProductionManager.Instance;
        CustomerOrderConfig config = production != null ? production.orderConfig : null;
        FreezerStation freezer = StarterStation<FreezerStation>();
        PantryStation pantry = StarterStation<PantryStation>();
        GrillStation grill = StarterStation<GrillStation>();
        AssemblyStation assembly = StarterStation<AssemblyStation>();
        HeatLampStation pickup = StarterStation<HeatLampStation>();
        AssemblyRecipeDefinition burger = config != null
            ? config.GetAssemblyRecipe(config.burgerBase) : null;
        if (production == null || burger == null || freezer == null || pantry == null
            || grill == null || assembly == null || pickup == null)
        {
            if (!inheritedSetupWarningLogged)
            {
                Debug.LogError("Tutorial burger setup is incomplete: production=" + (production != null)
                    + ", recipe=" + (burger != null) + ", freezer=" + (freezer != null)
                    + ", pantry=" + (pantry != null) + ", grill=" + (grill != null)
                    + ", assembly=" + (assembly != null) + ", pickup=" + (pickup != null) + ".");
                inheritedSetupWarningLogged = true;
            }
            return;
        }
        inheritedSetupWarningLogged = false;

        // Authored from the player's improved Play Mode layout. These are the
        // actual station roots, so their colliders and interaction tiles follow.
        PositionStarterStation(freezer.gameObject, new Vector3(-5.5f, 1f, 0.5f), 0f);
        PositionStarterStation(pantry.gameObject, new Vector3(-1.5f, 1f, 0.5f), 0f);
        PositionStarterStation(grill.gameObject, new Vector3(-5.5f, 0.5077203f, 7.5f), -180f);
        PositionStarterStation(assembly.gameObject, new Vector3(-1f, 0.5f, 7.5f), 0f);
        Physics.SyncTransforms();

        freezer.SetStoredItem(config.rawPattyIngredient);
        pantry.SetStoredItem(burger.rawPantryInput != null
            ? burger.rawPantryInput : burger.pantryInput);
        grill.SetRecipeOutput(config.cookedPattyIngredient);
        assembly.SetRecipe(burger);
        config.ClearEnabledMenuItems();
        config.SetItemEnabled(config.burgerBase, true);
        KitchenInventory stock = KitchenInventory.Instance;
        if (stock != null)
        {
            stock.RestoreStartingStock();
            stock.AddStock(config.rawPattyIngredient, KitchenInventory.MaxIngredientStock);
            stock.AddStock(pantry.selectedItem, KitchenInventory.MaxIngredientStock);
        }

        production.EnsureProductionFlows();
        production.productionFlows.Clear();
        production.EnsureProductionFlows();
        ProductionFlowPlan flow = production.SelectedFlow;
        flow.SetAutomaticName("Hamburger #1");
        flow.connections.Clear();
        flow.stations.Clear();
        flow.graphInitialized = true;
        flow.AddConnection(freezer.gameObject, grill.gameObject);
        flow.AddConnection(grill.gameObject, assembly.gameObject);
        flow.AddConnection(pantry.gameObject, assembly.gameObject);
        flow.AddConnection(assembly.gameObject, pickup.gameObject);
        WorkerFlowAssigner.RebuildStepIdsFromStations(flow);
        KitchenEmployee worker = production.employees.Find(value => value != null);
        if (worker == null) worker = production.HireWorkerFree();
        if (worker != null) production.AddWorkerToFlow(flow, worker);
        else Debug.LogError("Tutorial starter burger kitchen could not hire a worker.");

        GridManager grid = GridManager.Instance;
        if (grid != null) grid.ResyncOccupancyFromScene();
        production.NotifyProductionStateChanged(0f);
        production.ScheduledProduction.Tick();
        inheritedBaselineRate = production.GetPredictedOutputPerMinute(config.burgerBase);
        inheritedTargetRate = Mathf.Min(5f, inheritedBaselineRate + 0.1f);
        inheritedCurrentRate = inheritedBaselineRate;
        inheritedStationMoved = false;
        inheritedKitchenPrepared = true;
        nextInheritedForecastRefresh = 0f;
        if (placer != null) placer.EnsureCustomerEntrance();
    }

    T StarterStation<T>() where T : Component
    {
        foreach (GameObject station in starterKitchen)
            if (station != null && station.TryGetComponent(out T component))
                return component;
        return null;
    }

    static void PositionStarterStation(GameObject station, Vector3 position, float yaw)
    {
        if (station == null) return;
        station.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    void RestoreStarterKitchen()
    {
        if (!preserveLoadedKitchen)
            DestroyTutorialPlacements();
        starterKitchenHidden = false;

        var stock = KitchenInventory.Instance != null ? KitchenInventory.Instance : FindFirstObjectByType<KitchenInventory>();
        if (stock != null)
            stock.RestoreStartingStock();

        ResyncGridSoon();
    }

    void CacheStarterKitchen()
    {
        if (starterKitchen.Count > 0) return;
        CollectStarter<Register>();
        CollectStarter<FreezerStation>();
        CollectStarter<GrillStation>();
        CollectStarter<FryerStation>();
        CollectStarter<CuttingStation>();
        CollectStarter<AssemblyStation>();
        CollectStarter<HeatLampStation>();
        CollectStarter<PantryStation>();
        CollectStarter<CustomerWallDoor>();
    }

    void ClearExtraDoors()
    {
        CustomerWallDoor[] doors = FindObjectsByType<CustomerWallDoor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < doors.Length; i++)
        {
            CustomerWallDoor door = doors[i];
            if (door == null || !CustomerWallDoor.IsGameplayDoor(door)) continue;
            if (starterKitchen.Contains(door.gameObject)) continue;
            Destroy(door.gameObject);
        }
    }

    void CollectStarter<T>() where T : MonoBehaviour
    {
        var found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null) continue;
            // Destroy is deferred until the frame ends. Do not recache an old
            // placed station that Restart has already deactivated for removal.
            if (!found[i].gameObject.activeSelf
                && found[i].GetComponent<PlacedBuildItem>() != null)
                continue;
            if (!starterKitchen.Contains(found[i].gameObject))
                starterKitchen.Add(found[i].gameObject);
        }
    }

    void DestroyTutorialPlacements()
    {
        var placed = FindObjectsByType<PlacedBuildItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < placed.Length; i++)
        {
            if (placed[i] == null) continue;
            if (starterKitchen.Contains(placed[i].gameObject)) continue;
            placed[i].gameObject.SetActive(false);
            Destroy(placed[i].gameObject);
        }
    }

    void ResyncGridSoon()
    {
        CancelInvoke(nameof(ResyncGridNow));
        Invoke(nameof(ResyncGridNow), 0.05f);
    }

    void ResyncGridNow()
    {
        var grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.ResyncOccupancyFromScene();
    }

    void UpdateHighlight(Highlight kind)
    {
        Transform target = FindHighlightTarget(kind);
        highlightTarget = target;
        if (target is RectTransform targetRect && target.gameObject.activeInHierarchy)
            calloutTarget = targetRect;
        if (target == null)
        {
            ClearHighlight();
            return;
        }

        if (highlight == null)
        {
            highlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            highlight.name = "OnboardingHighlight";
            highlight.layer = LayerMask.NameToLayer("Ignore Raycast");
            var col = highlight.GetComponent<Collider>();
            if (col != null)
                col.enabled = false;
            var rend = highlight.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                // This must be a project asset, rather than a material created with Shader.Find.
                // Runtime-created URP shaders can be stripped from player builds, rendering pink.
                var material = Resources.Load<Material>(HighlightMaterialResourcePath);
                if (material != null)
                {
                    rend.sharedMaterial = material;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                else
                    Debug.LogError("Tutorial highlight material is missing from Resources/Materials.");
            }
        }

        highlight.SetActive(true);
        bool ui = kind == Highlight.Inventory || kind == Highlight.Management;
        highlight.transform.position = target.position + (ui ? Vector3.zero : Vector3.up * 2.2f);
        highlight.transform.localScale = Vector3.one * (ui ? 0.01f : 0.55f);
        if (ui)
            highlight.SetActive(false);
    }

    void EnsureSecondaryHighlight()
    {
        if (secondaryHighlight != null) return;
        secondaryHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        secondaryHighlight.name = "OnboardingSecondaryHighlight";
        secondaryHighlight.layer = LayerMask.NameToLayer("Ignore Raycast");
        Collider collider = secondaryHighlight.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        Renderer renderer = secondaryHighlight.GetComponent<Renderer>();
        if (renderer != null && highlight != null)
        {
            renderer.sharedMaterial = highlight.GetComponent<Renderer>().sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        secondaryHighlight.transform.localScale = Vector3.one * 0.65f;
    }

    void ClearControlHighlights()
    {
        foreach (var border in tutorialBorders.Values)
            if (border != null) border.SetActive(false);
    }

    void HighlightControl(Component control)
    {
        if (control == null || !control.gameObject.activeInHierarchy) return;
        bool ingredientPurchaseStep = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireIngredients;
        float borderThickness = ingredientPurchaseStep ? 6f : 3f;
        if (calloutTarget == null)
            calloutTarget = control.transform as RectTransform;
        var graphic = control.GetComponent<Graphic>();
        if (graphic == null) return;
        if (!tutorialBorders.TryGetValue(graphic, out var border) || border == null)
        {
            border = new GameObject("Tutorial Steady Border", typeof(RectTransform),
                typeof(LayoutElement), typeof(CanvasGroup));
            border.transform.SetParent(control.transform, false);
            border.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)border.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            for (int side = 0; side < 4; side++)
            {
                var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(border.transform, false);
                var image = edge.GetComponent<Image>();
                image.color = new Color(1f, 0.82f, 0.15f, 1f);
                image.raycastTarget = false;
                var r = (RectTransform)edge.transform;
                r.anchorMin = side == 1 ? Vector2.up : side == 3 ? Vector2.right : Vector2.zero;
                r.anchorMax = side == 0 ? Vector2.right : side == 2 ? Vector2.up : Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
                if (side < 2) { r.sizeDelta = new Vector2(0, borderThickness); r.pivot = new Vector2(0.5f, side); }
                else { r.sizeDelta = new Vector2(borderThickness, 0); r.pivot = new Vector2(side - 2, 0.5f); }
            }
            tutorialBorders[graphic] = border;
        }
        for (int side = 0; side < 4; side++)
        {
            var edgeRect = border.transform.GetChild(side) as RectTransform;
            if (edgeRect == null) continue;
            edgeRect.sizeDelta = side < 2
                ? new Vector2(0f, borderThickness)
                : new Vector2(borderThickness, 0f);
        }
        border.SetActive(true);
        border.transform.SetAsLastSibling();
    }

    void UpdateControlHighlightPulse()
    {
        bool ingredientPulse = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireIngredients;
        bool workerDropPulse = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireWorkerDragToFlow && !workerDraggedToFlow;
        bool pulse = ingredientPulse || workerDropPulse;
        float pulseRate = workerDropPulse ? Mathf.PI * 3f : 6f;
        float alpha = pulse
            ? Mathf.Lerp(0.08f, 1f, (Mathf.Sin(Time.unscaledTime * pulseRate) + 1f) * 0.5f)
            : 1f;

        foreach (GameObject border in tutorialBorders.Values)
        {
            if (border == null) continue;
            CanvasGroup group = border.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = alpha;
        }
    }

    void HighlightHudButton(string buttonName)
    {
        if (string.IsNullOrEmpty(buttonName)) return;
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (button != null && button.name == buttonName && button.gameObject.activeInHierarchy)
            {
                FocusTutorialControl(button);
                return;
            }
        }
    }

    string GetFreezerWalkthroughText(FreezerWalkthroughPhase phase)
    {
        switch (phase)
        {
            case FreezerWalkthroughPhase.StationName:
                return "Start with the <b>Freezer</b>. The card title names the facility. Click Next.";
            case FreezerWalkthroughPhase.ModelPreview:
                return "The picture previews the station model you will place. Click Next.";
            case FreezerWalkthroughPhase.OwnedCount:
                return "The number shows how many freezers you own. Click Next.";
            case FreezerWalkthroughPhase.PurchaseButton:
                return "The green button shows the cost, or <b>PLACE</b> if you already own one. Click it to buy or select the freezer and start placement.";
            case FreezerWalkthroughPhase.Placement:
                return "Move the freezer preview onto a clear floor tile, then left-click to place it. Press <b>R</b> to rotate.";
            case FreezerWalkthroughPhase.RemainingStations:
                return "Freezer placed. Use the checklist for the other stations. Register and Pickup go on counters. Shorter distances reduce worker travel.";
            default:
                return CompactTutorialText.Length > 2 ? CompactTutorialText[2] : Steps[2].body;
        }
    }

    static bool IsFreezerExplanationPhase(FreezerWalkthroughPhase phase)
    {
        return phase == FreezerWalkthroughPhase.StationName
            || phase == FreezerWalkthroughPhase.ModelPreview
            || phase == FreezerWalkthroughPhase.OwnedCount;
    }

    void SetFreezerWalkthroughPhase(FreezerWalkthroughPhase phase)
    {
        if (freezerWalkthroughPhase == phase) return;
        FreezerWalkthroughPhase previousPhase = freezerWalkthroughPhase;
        freezerWalkthroughPhase = phase;
        calloutTarget = null;

        bool showingCardDetail = phase == FreezerWalkthroughPhase.StationName
            || phase == FreezerWalkthroughPhase.ModelPreview
            || phase == FreezerWalkthroughPhase.OwnedCount
            || phase == FreezerWalkthroughPhase.PurchaseButton;
        currentHighlight = showingCardDetail ? Highlight.Freezer : Highlight.None;
        UpdateHighlight(currentHighlight);

        if (stepIndex == 2 && nextButton != null)
            nextButton.gameObject.SetActive(phase == FreezerWalkthroughPhase.StationName
                || phase == FreezerWalkthroughPhase.ModelPreview
                || phase == FreezerWalkthroughPhase.OwnedCount);

        SetTutorialBodyText(GetFreezerWalkthroughText(phase));
        ResetTutorialTextReveal();
        RebuildTutorialLayout();

        if (phase == FreezerWalkthroughPhase.Placement
            || phase == FreezerWalkthroughPhase.RemainingStations
            || (phase == FreezerWalkthroughPhase.PurchaseButton
                && previousPhase == FreezerWalkthroughPhase.Placement))
        {
            InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            inventory?.RefreshAll();
        }
        RefreshAdvanceGate();
    }

    void RefreshFreezerWalkthroughProgress()
    {
        if (!running || stepIndex != 2
            || freezerWalkthroughPhase == FreezerWalkthroughPhase.Inactive
            || freezerWalkthroughPhase == FreezerWalkthroughPhase.RemainingStations
            || Time.unscaledTime < nextFreezerWalkthroughRefresh)
            return;
        nextFreezerWalkthroughRefresh = Time.unscaledTime + 0.1f;

        if (HasPlacedStation(Highlight.Freezer))
        {
            SetFreezerWalkthroughPhase(FreezerWalkthroughPhase.RemainingStations);
            return;
        }

        BuildPlacer placer = FindFirstObjectByType<BuildPlacer>(FindObjectsInactive.Include);
        ItemDefinition placementItem = placer != null ? placer.CurrentPlacementItem : null;
        bool placingFreezer = placer != null && placer.IsPlacing && IsFreezerDefinition(placementItem);
        if (placingFreezer)
        {
            if (freezerWalkthroughPhase != FreezerWalkthroughPhase.Placement)
                SetFreezerWalkthroughPhase(FreezerWalkthroughPhase.Placement);
        }
        else if (freezerWalkthroughPhase == FreezerWalkthroughPhase.Placement)
        {
            // Esc cancels placement but keeps the purchased station in inventory.
            SetFreezerWalkthroughPhase(FreezerWalkthroughPhase.PurchaseButton);
        }
    }

    static bool IsFreezerDefinition(ItemDefinition item)
    {
        return item != null && ((item.prefab != null
                && item.prefab.GetComponentInChildren<FreezerStation>(true) != null)
            || ItemMatchesHighlight(item, Highlight.Freezer));
    }

    void RefreshFreezerWalkthroughHighlight()
    {
        if (freezerWalkthroughPhase == FreezerWalkthroughPhase.Inactive
            || freezerWalkthroughPhase == FreezerWalkthroughPhase.RemainingStations)
            return;

        if (freezerWalkthroughPhase == FreezerWalkthroughPhase.Placement)
        {
            BuildStationActionBar actionBar = FindFirstObjectByType<BuildStationActionBar>(
                FindObjectsInactive.Include);
            if (actionBar != null && actionBar.PlacementInstructionTarget != null)
                FocusTutorialControl(actionBar.PlacementInstructionTarget);
            return;
        }

        InventoryItemCardUI[] cards = FindObjectsByType<InventoryItemCardUI>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (InventoryItemCardUI card in cards)
        {
            if (card == null || !card.gameObject.activeInHierarchy
                || !IsFreezerDefinition(card.BoundItem))
                continue;

            Component target = freezerWalkthroughPhase switch
            {
                FreezerWalkthroughPhase.StationName => card.nameText,
                FreezerWalkthroughPhase.ModelPreview => card.previewRawImage != null
                    ? (Component)card.previewRawImage : card.previewImage,
                FreezerWalkthroughPhase.OwnedCount => card.qtyText,
                FreezerWalkthroughPhase.PurchaseButton => card.buyButton,
                _ => null
            };
            if (target != null)
                FocusTutorialControl(target);
            return;
        }
    }

    void RefreshControlHighlights()
    {
        if (Time.unscaledTime < nextControlHighlightRefresh) return;
        nextControlHighlightRefresh = Time.unscaledTime + 0.25f;
        ClearControlHighlights();
        calloutTarget = null;
        var step = Steps[stepIndex];

        if (step.openInventory)
        {
            foreach (var card in FindObjectsByType<InventoryItemCardUI>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (card != null && card.IsTutorialHighlighted)
                {
                    calloutTarget = card.transform as RectTransform;
                    break;
                }
            }
        }

        if (calloutTarget == null && highlightTarget is RectTransform targetRect
            && targetRect.gameObject.activeInHierarchy)
            calloutTarget = targetRect;

        if (step.requireStationChecklist)
        {
            RefreshFreezerWalkthroughHighlight();
            return;
        }

        if (step.requireObjectivesScreen)
        {
            MissionListUI objectives = MissionListUI.Instance != null
                ? MissionListUI.Instance
                : FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
            if (!objectivesScreenOpened && objectives != null
                && objectives.TutorialObjectivesButton != null)
                HighlightControl(objectives.TutorialObjectivesButton);
            else if (objectivesScreenOpened && objectives != null && objectives.IsOpen
                && objectives.panelRoot != null)
                calloutTarget = objectives.panelRoot.transform as RectTransform;
            return;
        }

        if (step.requireStationHealthReview)
        {
            ManagementModeController stationManagement = ManagementModeController.Instance;
            ManagementScreenController managementScreen = FindFirstObjectByType<ManagementScreenController>(
                FindObjectsInactive.Include);
            if (stationManagement == null || managementScreen == null || !managementScreen.IsOpen)
                HighlightHudButton("ManagementButton");
            else if (HasSelectedProductionStation() && stationManagement.stationPopup != null)
                calloutTarget = stationManagement.stationPopup.transform as RectTransform;
            return;
        }

        if (step.requireAnalyticsScreen)
        {
            if (analyticsScreenClosed && analyticsTutorialStage >= AnalyticsTourCloseStage)
            {
                if (nextButton != null && nextButton.gameObject.activeInHierarchy)
                    FocusTutorialControl(nextButton);
                return;
            }

            if (!analyticsScreenOpened || analyticsScreenClosed || !AnalyticsPopupUI.IsOpen)
            {
                HighlightHudButton("AnalyticsButton");
                return;
            }

            CustomersUI analytics = FindFirstObjectByType<CustomersUI>(FindObjectsInactive.Exclude);
            Component target = null;
            if (analyticsTutorialStage >= 0 && analyticsTutorialStage < 4)
                target = analytics != null ? analytics.TutorialMetricTarget(analyticsTutorialStage) : null;
            else if (analyticsTutorialStage == 4)
                target = analytics != null ? analytics.TutorialArrivalsTarget : null;
            else if (analyticsTutorialStage >= 5 && analyticsTutorialStage < AnalyticsTourCloseStage)
                target = analytics != null
                    ? analytics.TutorialDemandHeaderTarget(analyticsTutorialStage - 5) : null;
            else if (analyticsTutorialStage >= AnalyticsTourCloseStage)
                target = AnalyticsPopupUI.TutorialCloseButton;

            if (target != null)
                FocusTutorialControl(target);
            return;
        }

        if (step.requireShiftStarted)
        {
            ShiftPlanningUI planning = ShiftPlanningUI.Instance;
            if (planning != null)
            {
                planning.FocusPlanningCard();
                HighlightControl(planning.TutorialStartShiftButton);
            }
            return;
        }

        if (step.requireIngredients)
        {
            IngredientsOrderUI ingredients = FindFirstObjectByType<IngredientsOrderUI>();
            CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            ItemDefinition item = config != null && ingredientTutorialStep < 2
                ? GetTutorialIngredient(ingredientTutorialStep, config) : null;
            Button target = null;
            if (ingredients != null)
            {
                if (ingredientTutorialStep < 2 && item != null)
                    target = ingredients.TutorialAddButton(item);
                else if (ingredientTutorialStep >= 3)
                    target = ingredients.TutorialPlaceOrderButton;
            }
            if (target != null) HighlightControl(target);
        }

        if (step.requireHamburger && !IsHamburgerEnabled())
        {
            CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            ShiftPlanningUI planning = ShiftPlanningUI.Instance;
            if (planning != null) planning.FocusPlanningCard();
            Button target = planning != null && config != null && planning.IsMenuItemPickerOpen
                ? planning.TutorialMenuItemButton(config.burgerBase)
                : planning != null ? planning.TutorialAddMenuItemButton : null;
            if (target != null)
            {
                tutorialFocusText = planning.IsMenuItemPickerOpen
                    ? "Choose <b>Hamburger</b> from the item list."
                    : "Click <b>Add Item to Menu</b> on the Plan card.";
                SetTutorialBodyText(tutorialFocusText);
                FocusTutorialControl(target);
            }
            return;
        }

        if (step.requireResearch && !researchScreenOpened)
        {
            HighlightHudButton("ResearchButton");
            return;
        }
        if (step.requireRecipeScreen && !recipeScreenOpened)
        {
            HighlightHudButton("RecipesButton");
            return;
        }
        if (step.requireTimeControlInteractions)
        {
            RefreshTimeControlChecklist();
            SetTutorialBodyText(GetTimeControlInstruction());
            GameTimeUI timeControls = FindFirstObjectByType<GameTimeUI>(FindObjectsInactive.Exclude);
            Button target = timeControls == null ? null
                : !tutorialPauseControlUsed ? timeControls.pauseButton
                : !tutorialPlayControlUsed ? timeControls.playButton
                : !tutorialFastForwardControlUsed ? timeControls.fastForwardButton
                : !AllTutorialFastForwardRatesVisited() ? timeControls.fastForwardRateButton
                : null;
            if (target != null) HighlightControl(target);
            return;
        }
        if (step.requireResearch)
        {
            string rootId = ResearchProgressManager.Instance != null
                ? (ResearchProgressManager.Instance.IsCompleted("production_basics")
                    ? ResearchProgressManager.Instance.IsCompleted("core_workforce")
                        ? "manual_operations" : "core_workforce"
                    : "production_basics")
                : "production_basics";
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (button != null && button.name == rootId && button.interactable)
                {
                    HighlightControl(button);
                    break;
                }
            }
            return;
        }
        if (!step.openWorkers) return;
        var workers = FindFirstObjectByType<WorkersUI>();
        if (workers != null)
        {
            if (step.requireHiredWorker && !HasHiredWorker()) HighlightControl(workers.hireButton);
            foreach (var button in workers.GetComponentsInChildren<Button>())
            {
                if (step.requireEditedFlow && !flowEditCompleted && button.name == "EditFlow"
                    || step.requireFlow && !step.requireEditedFlow
                        && !(flowCreateCompleted && HasTutorialFlow()) && button.name == "CreateFlow"
                    || step.requireWorkerOnFlow && IsWorkerAssignmentStepIncomplete(step)
                        && button.name == "WorkerDropZone")
                    HighlightControl(button);
            }
            if (step.requireWorkerOnFlow && IsWorkerAssignmentStepIncomplete(step))
                foreach (WorkerCardUI card in workers.GetComponentsInChildren<WorkerCardUI>())
                    if (card != null && card.employee != null)
                        HighlightControl(card);
        }
        if (step.requireRecipes && recipeTutorialStationIndex < 4)
        {
            Component station = GetRecipeTutorialStation(recipeTutorialStationIndex);
            ManagementModeController manager = ManagementModeController.Instance;
            if (station != null && manager != null && manager.TutorialSelectedStation == station.gameObject
                && manager.stationPopup != null && manager.stationPopup.activeInHierarchy)
            {
                if (manager.TutorialRecipePickerOpen)
                    HighlightControl(manager.TutorialRecipeChoice(GetRecipeTutorialChoiceName(recipeTutorialStationIndex)));
                else if (manager.TutorialOutputSelectButton != null)
                    HighlightControl(manager.TutorialOutputSelectButton);
            }
        }
        var management = ManagementModeController.Instance;
        if (management != null && management.IsCapturingFlow
            && management.CapturedFlow != null && IsBurgerTutorialFlow(management.CapturedFlow))
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (button.name == "Finish" && button.interactable) HighlightControl(button);
        }
    }

    void Update()
    {
        if (!cleanedOldWorldCallout)
        {
            cleanedOldWorldCallout = true;
            foreach (Canvas oldCallout in FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (oldCallout != null && oldCallout.name == "TutorialGusWorldCallout")
                {
                    oldCallout.gameObject.SetActive(false);
                    Destroy(oldCallout.gameObject);
                }
        }
        if (!running)
        {
            ClearControlHighlights();
            SetFastForwardHelpVisible(false);
            if (pendingStart && !IsComplete)
                TryBegin();
            else
                UpdateMilestoneOneLessonSchedule();
            return;
        }
        TrackCameraTutorialInputs();
        RefreshCameraPromptStatus();
        RefreshAdvanceGate();
        RefreshFreezerWalkthroughProgress();
        RefreshControlHighlights();
        UpdateControlHighlightPulse();
        UpdateFastForwardHelpCard();
        if (Steps[stepIndex].requireStationChecklist)
            RefreshStationChecklist();
        if (Steps[stepIndex].requireOptimizedLayout)
            RefreshInheritedLayoutText();
        UpdateContextualCallout();
        UpdateTutorialWorldHighlights();
        if (Steps[stepIndex].liveCustomer && !liveCustomerSpawned)
            SpawnPracticeCustomer();
        if (milestoneOneLessonActive && stepIndex == MilestoneOneBottleneckStep)
            RefreshStationHealthLesson();
        RefreshCustomerJourneyCard();
        RefreshAutomaticAdvance();
        UpdateTutorialTextReveal();
        UpdateGlossaryTermFlashBox();

        if (highlight == null || !highlight.activeSelf || highlightTarget == null)
            return;
        float pulse = 0.5f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
        highlight.transform.position = GetTutorialWorldAimPoint(highlightTarget);
        highlight.transform.localScale = Vector3.one * pulse;
    }

    void UpdateMilestoneOneLessonSchedule()
    {
        if (!IsComplete || milestoneOneLessonActive || ShiftPlanningUI.IsPlanning)
            return;

        MilestoneProgressManager milestones = MilestoneProgressManager.Instance;
        GameTimeManager clock = GameTimeManager.Instance;
        if (milestones == null || milestones.ActiveMilestoneId != "milestone_01"
            || clock == null || !clock.IsShiftInProgress || clock.IsShiftOver)
            return;

        if (!milestoneOneBottleneckLessonDone && clock.CurrentMinutes >= 12f * 60f)
            BeginMilestoneOneLesson(MilestoneOneBottleneckStep);
        else if (milestoneOneBottleneckLessonDone && !milestoneOneAnalyticsLessonDone
            && clock.CurrentMinutes >= 18f * 60f)
            BeginMilestoneOneLesson(MilestoneOneAnalyticsStep);
    }

    void BeginMilestoneOneLesson(int lessonStep)
    {
        if (lessonStep < MilestoneOneBottleneckStep || lessonStep > MilestoneOneAnalyticsStep)
            return;

        milestoneOneLessonActive = true;
        running = true;
        pendingStart = false;
        stepIndex = lessonStep;
        analyticsScreenOpened = false;
        analyticsScreenClosed = false;
        if (lessonStep == MilestoneOneAnalyticsStep)
            analyticsTutorialStage = -1;
        ManagementModeController management = ManagementModeController.Instance;
        lastStationHealthLessonTarget = management != null
            ? management.TutorialSelectedStation : null;
        stationHealthLessonSelectionRevision = management != null
            ? management.TutorialStationSelectionRevision : 0;
        milestoneOneStationClickObserved = false;
        EnsureUI();
        ShowStep();
        ApplyPause(true);
        if (lessonStep == MilestoneOneBottleneckStep)
            SetTutorialBodyText(Steps[lessonStep].body);
    }

    void RefreshStationHealthLesson()
    {
        ManagementModeController management = ManagementModeController.Instance;
        GameObject selected = management != null ? management.TutorialSelectedStation : null;
        bool selectionChanged = selected != lastStationHealthLessonTarget;
        if (selectionChanged)
            lastStationHealthLessonTarget = selected;

        bool clickedAfterLessonStarted = management != null
            && management.TutorialStationSelectionRevision != stationHealthLessonSelectionRevision;
        if (!milestoneOneStationClickObserved && clickedAfterLessonStarted && selected != null
            && IsProductionStationWithHealth(selected) && management.stationPopup != null
            && management.stationPopup.activeInHierarchy)
        {
            milestoneOneStationClickObserved = true;
            SetTutorialBodyText(
                "Read the station's percentages. They add to 100%. <b>Working</b> is processing time. <b>Blocked</b> means output cannot move; <b>Starved</b> means inputs are missing. <b>Idle</b> means no work is needed, often because demand is low. High Blocked or Starved time points to a bottleneck to investigate.");
            RefreshAdvanceGate();
            UpdateContextualCallout();
        }
        else if (!milestoneOneStationClickObserved && selectionChanged)
        {
            SetTutorialBodyText(
                "Open <b>Business</b>, then click a production station. Its card shows the share of time spent Working, Blocked, Starved, and Idle.");
            RefreshAdvanceGate();
            UpdateContextualCallout();
        }
    }

    bool HasSelectedProductionStation()
    {
        if (!milestoneOneStationClickObserved) return false;
        ManagementModeController management = ManagementModeController.Instance;
        return management != null
            && IsProductionStationWithHealth(management.TutorialSelectedStation)
            && management.stationPopup != null && management.stationPopup.activeInHierarchy;
    }

    static bool IsProductionStationWithHealth(GameObject station)
    {
        if (station == null || station.GetComponent<StationNode>() == null) return false;
        return station.GetComponent<FreezerStation>() == null
            && station.GetComponent<PantryStation>() == null
            && station.GetComponent<Register>() == null
            && station.GetComponent<HeatLampStation>() == null;
    }

    void EndMilestoneOneLesson()
    {
        milestoneOneLessonActive = false;
        running = false;
        pendingStart = false;
        HideUI();
        ClearHighlight();
        ApplyPause(false);
    }

    Vector3 GetTutorialWorldAimPoint(Transform target)
    {
        bool clickingFlowStation = running && stepIndex >= 0 && stepIndex < Steps.Length
            && (Steps[stepIndex].requireFlow || Steps[stepIndex].requireEditedFlow) && target != null
            && target.GetComponent<StationNode>() != null;
        if (clickingFlowStation)
            return GetTutorialStationBounds(target).center;

        bool choosingStationRecipe = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireRecipes && target != null
            && target.GetComponent<StationNode>() != null;
        return target != null
            ? target.position + Vector3.up * (choosingStationRecipe ? 0.9f : 2.2f)
            : Vector3.zero;
    }

    void TrackCameraTutorialInputs()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length || !Steps[stepIndex].requireCameraControls)
            return;
        if (PauseMenuUI.IsOpen || UIInputFocusGuard.IsTyping)
            return;

        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f
            || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f)
            tutorialCameraMoved = true;

        Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        if (Input.GetMouseButton(1) && mouseDelta.sqrMagnitude > 0.0004f)
            tutorialCameraRotated = true;

        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
            tutorialCameraZoomed = true;
    }

    void RefreshCameraPromptStatus()
    {
        SetCameraPromptStatus(cameraMovePromptStatus, tutorialCameraMoved);
        SetCameraPromptStatus(cameraRotatePromptStatus, tutorialCameraRotated);
        SetCameraPromptStatus(cameraZoomPromptStatus, tutorialCameraZoomed);
    }

    static void SetCameraPromptStatus(TextMeshProUGUI label, bool complete)
    {
        if (label == null) return;
        string value = complete ? "DONE" : "TRY IT";
        if (label.text != value)
        {
            label.text = value;
            label.color = complete
                ? new Color(0.35f, 0.86f, 0.62f, 1f)
                : new Color(0.72f, 0.76f, 0.82f, 1f);
        }
    }

    void ResetCameraTutorialInputs()
    {
        tutorialCameraMoved = false;
        tutorialCameraRotated = false;
        tutorialCameraZoomed = false;
        RefreshCameraPromptStatus();
    }

    void ResetTutorialTimeControlProgress()
    {
        tutorialPauseControlUsed = false;
        tutorialPlayControlUsed = false;
        tutorialFastForwardControlUsed = false;
        tutorialFastForwardRateMask = 0;
    }

    void UpdateTutorialWorldHighlights()
    {
        if (!running || stepIndex < 0 || stepIndex >= Steps.Length)
        {
            if (secondaryHighlight != null) secondaryHighlight.SetActive(false);
            SetTutorialFlowGuideVisible(false);
            return;
        }

        ManagementModeController manager = ManagementModeController.Instance;
        bool showingFlowGuide = Steps[stepIndex].requireFlow
            && manager != null && manager.IsCapturingFlow;
        if (showingFlowGuide)
        {
            // Use a route cue during the connection lesson instead of pulsing
            // markers above each station. The route advances after each link.
            if (highlight != null) highlight.SetActive(false);
            if (secondaryHighlight != null) secondaryHighlight.SetActive(false);
            UpdateTutorialFlowGuide(highlightTarget, secondaryHighlightTarget);
            return;
        }

        SetTutorialFlowGuideVisible(false);
        if (highlightTarget == null)
        {
            if (secondaryHighlight != null) secondaryHighlight.SetActive(false);
            return;
        }

        bool recipeConfigurationStep = Steps[stepIndex].requireRecipes;
        if (highlight != null)
        {
            if (recipeConfigurationStep)
                highlight.SetActive(false);
            else
            {
                highlight.SetActive(true);
                float markerPulse = 0.48f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.22f);
                highlight.transform.position = GetTutorialWorldAimPoint(highlightTarget);
                highlight.transform.localScale = Vector3.one * markerPulse;
            }
        }
        if (!recipeConfigurationStep && secondaryHighlightTarget != null)
        {
            EnsureSecondaryHighlight();
            secondaryHighlight.SetActive(true);
            float pulse = 0.62f + Mathf.PingPong(Time.unscaledTime * 1.5f, 0.2f);
            secondaryHighlight.transform.position = secondaryHighlightTarget.position + Vector3.up * 2.2f;
            secondaryHighlight.transform.localScale = Vector3.one * pulse;
        }
        else if (secondaryHighlight != null)
            secondaryHighlight.SetActive(false);
    }

    void UpdateTutorialFlowGuide(Transform source, Transform destination)
    {
        if (source == null || destination == null || source == destination)
        {
            SetTutorialFlowGuideVisible(false);
            return;
        }

        EnsureTutorialFlowGuide();
        if (tutorialFlowGuideLine == null || tutorialFlowGuideArrow == null) return;

        Bounds sourceBounds = GetTutorialStationBounds(source);
        Bounds destinationBounds = GetTutorialStationBounds(destination);
        Vector3 direction = destinationBounds.center - sourceBounds.center;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            SetTutorialFlowGuideVisible(false);
            return;
        }
        direction.Normalize();

        // Rise from the source click point, cross above both stations, then
        // descend into the destination click point.
        float guideHeight = Mathf.Max(sourceBounds.max.y, destinationBounds.max.y) + 0.55f;
        Vector3 start = sourceBounds.center;
        Vector3 overSource = new Vector3(start.x, guideHeight, start.z);
        Vector3 end = destinationBounds.center;
        Vector3 overDestination = new Vector3(end.x, guideHeight, end.z);
        tutorialFlowGuidePoints[0] = start;
        tutorialFlowGuidePoints[1] = overSource;
        tutorialFlowGuidePoints[2] = overDestination;
        tutorialFlowGuidePoints[3] = end;
        tutorialFlowGuideLine.positionCount = tutorialFlowGuidePoints.Length;
        tutorialFlowGuideLine.SetPositions(tutorialFlowGuidePoints);

        float totalLength = 0f;
        for (int i = 1; i < tutorialFlowGuidePoints.Length; i++)
            totalLength += Vector3.Distance(tutorialFlowGuidePoints[i - 1], tutorialFlowGuidePoints[i]);
        float distanceAlong = Mathf.Repeat(Time.unscaledTime * 1.2f, 1f) * totalLength;
        for (int i = 1; i < tutorialFlowGuidePoints.Length; i++)
        {
            Vector3 segment = tutorialFlowGuidePoints[i] - tutorialFlowGuidePoints[i - 1];
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.001f) continue;
            if (distanceAlong > segmentLength)
            {
                distanceAlong -= segmentLength;
                continue;
            }

            Vector3 arrowDirection = segment / segmentLength;
            tutorialFlowGuideArrow.position = tutorialFlowGuidePoints[i - 1]
                + arrowDirection * distanceAlong;
            Vector3 orientationUp = Mathf.Abs(Vector3.Dot(arrowDirection, Vector3.up)) > 0.98f
                ? Vector3.forward : Vector3.up;
            tutorialFlowGuideArrow.rotation = Quaternion.LookRotation(arrowDirection, orientationUp);
            break;
        }
        SetTutorialFlowGuideVisible(true);
    }

    static Bounds GetTutorialStationBounds(Transform station)
    {
        if (station == null) return new Bounds();
        Collider collider = station.GetComponentInChildren<Collider>();
        if (collider != null && collider.enabled && !collider.isTrigger)
            return collider.bounds;
        Renderer renderer = station.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.bounds : new Bounds(station.position, Vector3.one);
    }

    void EnsureTutorialFlowGuide()
    {
        if (tutorialFlowGuideLine != null && tutorialFlowGuideArrow != null) return;

        if (tutorialFlowGuideMaterial == null)
        {
            Material source = Resources.Load<Material>(HighlightMaterialResourcePath);
            if (source == null) return;
            tutorialFlowGuideMaterial = new Material(source);
            tutorialFlowGuideMaterial.name = "Tutorial Flow Guide Material";
            tutorialFlowGuideMaterial.color = new Color(0.20f, 0.94f, 0.85f, 1f);
            if (tutorialFlowGuideMaterial.HasProperty("_BaseColor"))
                tutorialFlowGuideMaterial.SetColor("_BaseColor", tutorialFlowGuideMaterial.color);
            if (tutorialFlowGuideMaterial.HasProperty("_Color"))
                tutorialFlowGuideMaterial.SetColor("_Color", tutorialFlowGuideMaterial.color);
        }

        if (tutorialFlowGuideLine == null)
        {
            GameObject lineObject = new GameObject("Tutorial Flow Guide Line");
            tutorialFlowGuideLine = lineObject.AddComponent<LineRenderer>();
            tutorialFlowGuideLine.useWorldSpace = true;
            tutorialFlowGuideLine.positionCount = 2;
            tutorialFlowGuideLine.startWidth = 0.10f;
            tutorialFlowGuideLine.endWidth = 0.10f;
            tutorialFlowGuideLine.numCapVertices = 4;
            tutorialFlowGuideLine.sharedMaterial = tutorialFlowGuideMaterial;
            tutorialFlowGuideLine.startColor = tutorialFlowGuideMaterial.color;
            tutorialFlowGuideLine.endColor = tutorialFlowGuideMaterial.color;
            tutorialFlowGuideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tutorialFlowGuideLine.receiveShadows = false;
            lineObject.SetActive(false);
        }

        if (tutorialFlowGuideArrow == null)
        {
            GameObject arrowObject = new GameObject("Tutorial Flow Guide Arrow",
                typeof(MeshFilter), typeof(MeshRenderer));
            tutorialFlowGuideArrow = arrowObject.transform;
            tutorialFlowGuideArrowMesh = new Mesh { name = "Tutorial Flow Guide Arrow Mesh" };
            tutorialFlowGuideArrowMesh.vertices = new[]
            {
                new Vector3(-0.22f, 0f, -0.30f),
                new Vector3(0.22f, 0f, -0.30f),
                new Vector3(0f, 0f, 0.38f)
            };
            tutorialFlowGuideArrowMesh.triangles = new[] { 0, 2, 1 };
            tutorialFlowGuideArrowMesh.RecalculateNormals();
            arrowObject.GetComponent<MeshFilter>().sharedMesh = tutorialFlowGuideArrowMesh;
            MeshRenderer arrowRenderer = arrowObject.GetComponent<MeshRenderer>();
            arrowRenderer.sharedMaterial = tutorialFlowGuideMaterial;
            arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrowRenderer.receiveShadows = false;
            arrowObject.SetActive(false);
        }
    }

    void SetTutorialFlowGuideVisible(bool visible)
    {
        if (tutorialFlowGuideLine != null && tutorialFlowGuideLine.gameObject.activeSelf != visible)
            tutorialFlowGuideLine.gameObject.SetActive(visible);
        if (tutorialFlowGuideArrow != null && tutorialFlowGuideArrow.gameObject.activeSelf != visible)
            tutorialFlowGuideArrow.gameObject.SetActive(visible);
    }

    void DestroyTutorialFlowGuide()
    {
        if (tutorialFlowGuideLine != null)
            Destroy(tutorialFlowGuideLine.gameObject);
        if (tutorialFlowGuideArrow != null)
            Destroy(tutorialFlowGuideArrow.gameObject);
        if (tutorialFlowGuideArrowMesh != null)
            Destroy(tutorialFlowGuideArrowMesh);
        if (tutorialFlowGuideMaterial != null)
            Destroy(tutorialFlowGuideMaterial);
        tutorialFlowGuideLine = null;
        tutorialFlowGuideArrow = null;
        tutorialFlowGuideArrowMesh = null;
        tutorialFlowGuideMaterial = null;
    }

    void UpdateStationSequenceStep(Step step)
    {
        if (step.requireRecipes && !recipeBusinessOpened)
        {
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management == null || !management.IsOpen)
            {
                UpdateHighlight(Highlight.Management);
                return;
            }
            recipeBusinessOpened = true;
            calloutTarget = null;
        }
        if (step.requireRecipes)
        {
            if (recipeTutorialStationIndex >= 4) return;
            Component station = GetRecipeTutorialStation(recipeTutorialStationIndex);
            if (station == null) return;
            bool selected = ManagementModeController.Instance != null
                && ManagementModeController.Instance.TutorialSelectedStation == station.gameObject;
            if (!selected)
            {
                highlightTarget = station.transform;
                secondaryHighlightTarget = null;
                calloutTarget = null;
                return;
            }

            if (IsRecipeTutorialStationConfigured(recipeTutorialStationIndex, station))
            {
                recipeTutorialStationIndex++;
                if (recipeTutorialStationIndex >= 4)
                {
                    secondaryHighlightTarget = null;
                    return;
                }
                station = GetRecipeTutorialStation(recipeTutorialStationIndex);
                if (station == null) return;
            }

            ManagementModeController manager = ManagementModeController.Instance;
            if (manager == null || manager.TutorialSelectedStation != station.gameObject)
            {
                highlightTarget = station.transform;
                secondaryHighlightTarget = null;
                calloutTarget = null;
                return;
            }
            highlightTarget = null;
            secondaryHighlightTarget = null;
            if (manager != null && manager.TutorialRecipePickerOpen)
            {
                Button recipeChoice = manager.TutorialRecipeChoice(
                    GetRecipeTutorialChoiceName(recipeTutorialStationIndex));
                if (recipeChoice != null) FocusTutorialControl(recipeChoice);
                else calloutTarget = null;
            }
            else
            {
                if (manager != null && manager.TutorialOutputSelectButton != null)
                    FocusTutorialControl(manager.TutorialOutputSelectButton);
                else calloutTarget = null;
            }
            highlightTarget = station.transform;
            if (manager != null && (manager.TutorialRecipePickerOpen
                || manager.TutorialOutputSelectButton != null))
                highlightTarget = null;
            return;
        }

        if (step.requireIngredients)
        {
            IngredientsOrderUI ingredients = FindFirstObjectByType<IngredientsOrderUI>();
            CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            if (ingredients == null || config == null) return;
            ItemDefinition item = ingredientTutorialStep < 2
                ? GetTutorialIngredient(ingredientTutorialStep, config) : null;
            while (item == null && ingredientTutorialStep < 2)
            {
                ingredientTutorialStep++;
                item = GetTutorialIngredient(ingredientTutorialStep, config);
            }

            if (ingredientTutorialStep < 2 && item != null)
            {
                if (ingredients.TutorialCartPacks(item) > 0)
                {
                    ingredientTutorialStep++;
                    if (ingredientTutorialStep == 2)
                    {
                        ingredientExplanationUntil = 0f;
                        calloutTarget = null;
                    }
                    return;
                }

                Button add = ingredients.TutorialAddButton(item);
                FocusTutorialControl(add);
                return;
            }

            if (ingredientTutorialStep == 2)
            {
                calloutTarget = null;
                ingredientTutorialStep = 3;
            }

            Button placeOrder = ingredients.TutorialPlaceOrderButton;
            FocusTutorialControl(placeOrder);
            return;
        }

        if (step.requireHiredWorker && !HasHiredWorker())
        {
            WorkersUI workers = FindFirstObjectByType<WorkersUI>();
            tutorialFocusText = "The Workers count shows your staff. The <b>FREE</b> value shows available capacity. Click <b>Hire</b> to recruit one worker; the hire cost is shown beside it.";
            FocusTutorialControl(workers != null ? workers.hireButton : null);
            return;
        }

        if (step.requireFlow)
        {
            // This step starts with a generic Business highlight. Do not let
            // that old target remain available as the callout's fallback while
            // its focus moves to Create Flow or the captured route stations.
            highlightTarget = null;
            secondaryHighlightTarget = null;
            calloutTarget = null;
            var manager = ManagementModeController.Instance;
            if (manager == null) return;
            if (!manager.IsCapturingFlow)
            {
                if (flowCreateCompleted && HasTutorialFlow())
                {
                    tutorialFocusText = "Burger flow saved. Moving to the next tutorial step.";
                    SetTutorialBodyText(tutorialFocusText);
                    return;
                }

                var workers = FindFirstObjectByType<WorkersUI>();
                tutorialFocusText = "Flow tabs select a saved workflow. <b>Edit</b> changes the selected route, <b>+ Flow</b> creates a new one, and <b>Delete</b> removes the selected flow. Click <b>+ Flow</b> to continue.";
                if (workers != null)
                    foreach (Button button in workers.GetComponentsInChildren<Button>(true))
                        if (button != null && button.name == "CreateFlow") { FocusTutorialControl(button); break; }
                SetTutorialBodyText(tutorialFocusText);
                return;
            }

            var flow = manager.CapturedFlow;
            var expected = GetExpectedFlowLink(flow);
            if (expected.source != null && expected.destination != null)
            {
                highlightTarget = expected.source.transform;
                secondaryHighlightTarget = expected.destination.transform;
                tutorialFocusText = "<b>Next link:</b> follow the animated arrow. Click and drag from <b>" + expected.source.name.Replace("(Clone)", "").Trim() +
                    "</b> to <b>" + expected.destination.name.Replace("(Clone)", "").Trim() + "</b>, then release.";
                if (expected.source.GetComponent<PantryStation>() != null)
                    tutorialFocusText = "Follow the animated arrow to start a separate <b>branch</b> from <b>Pantry</b>. Drag to the existing <b>Assembly</b> station, then release to merge it into the same flow.";
                else if (expected.source.GetComponent<AssemblyStation>() != null)
                    tutorialFocusText = "Follow the animated arrow to bring the flow to the <b>Pickup Station</b>. Drag from <b>Assembly</b> to <b>Pickup Station</b>, then release.";
                SetTutorialBodyText(tutorialFocusText);
            }
            else if (HasTutorialFlow())
            {
                tutorialFocusText = "Flow connected. Click <b>Finish</b> to save it.";
                if (manager.flowCaptureFinishButton != null)
                    FocusTutorialControl(manager.flowCaptureFinishButton);
                SetTutorialBodyText(tutorialFocusText);
            }
            else
            {
                tutorialFocusText = "Follow the animated arrow from the source station to its destination. It moves to the next connection after each link.";
                SetTutorialBodyText(tutorialFocusText);
            }
        }

        if (step.requireWorkerOnFlow && IsWorkerAssignmentStepIncomplete(step))
        {
            tutorialFocusText = "Click and drag the worker into flashing yellow box.";
            var workers = FindFirstObjectByType<WorkersUI>();
            if (workers != null)
            {
                foreach (Button button in workers.GetComponentsInChildren<Button>(true))
                    if (button != null && button.name == "WorkerDropZone" && button.gameObject.activeInHierarchy)
                    {
                        calloutTarget = button.transform as RectTransform;
                        highlightTarget = null;
                        break;
                    }
            }
            SetTutorialBodyText(tutorialFocusText);
        }

        if (step.requireEditedFlow && !flowEditCompleted)
        {
            var manager = ManagementModeController.Instance;
            if (manager == null || !manager.IsCapturingFlow)
            {
                var workers = FindFirstObjectByType<WorkersUI>();
                tutorialFocusText = "Select the burger flow tab, then click <b>Edit</b>. Drag between stations to change the route. <b>Finish</b> saves; <b>Esc</b> cancels.";
                if (workers != null)
                    foreach (Button button in workers.GetComponentsInChildren<Button>(true))
                        if (button != null && button.name == "EditFlow") { FocusTutorialControl(button); break; }
            }
            else
            {
                calloutTarget = null;
                tutorialFocusText = "Edit the burger route in the kitchen. Gus points to each station; click <b>Finish</b> to save your edit.";
            }
            SetTutorialBodyText(tutorialFocusText);
        }
    }

    void FocusTutorialControl(Component control)
    {
        if (control == null || !control.gameObject.activeInHierarchy) return;
        calloutTarget = control.transform as RectTransform;
        HighlightControl(control);
    }

    static ItemDefinition GetTutorialIngredient(int index, CustomerOrderConfig config)
    {
        if (config == null) return null;
        if (index == 0) return config.rawPattyIngredient;
        if (index == 1)
        {
            AssemblyRecipeDefinition recipe = config.GetAssemblyRecipe(config.burgerBase);
            return config.GetAssemblySupplySource(recipe);
        }
        return index == 2 ? config.friesIngredient : null;
    }

    static T FindTutorialPlacedStation<T>() where T : Component
    {
        T[] stations = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (T station in stations)
            if (station != null && IsPlacedFlowStation<T>(station.gameObject))
                return station;

        return stations.Length > 0 ? stations[0] : null;
    }

    Component GetRecipeTutorialStation(int index)
    {
        switch (index)
        {
            case 0: return FindTutorialPlacedStation<FreezerStation>();
            case 1: return FindTutorialPlacedStation<GrillStation>();
            case 2: return FindTutorialPlacedStation<PantryStation>();
            default: return FindTutorialPlacedStation<AssemblyStation>();
        }
    }

    static bool IsRecipeTutorialStationConfigured(int index, Component station)
    {
        CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
        switch (index)
        {
            case 0:
            {
                ItemDefinition selected = station.GetComponent<FreezerStation>()?.selectedItem;
                return config != null ? selected == config.rawPattyIngredient : ItemNameContains(selected, "Raw Patty");
            }
            case 1:
            {
                ItemDefinition selected = station.GetComponent<GrillStation>()?.selectedProduct;
                return config != null ? selected == config.cookedPattyIngredient : ItemNameContains(selected, "Cooked Patty");
            }
            case 2:
            {
                PantryStation pantry = station.GetComponent<PantryStation>();
                AssemblyRecipeDefinition burgerRecipe = config != null ? config.GetAssemblyRecipe(config.burgerBase) : null;
                ItemDefinition expected = config != null ? config.GetAssemblySupplySource(burgerRecipe) : null;
                return pantry != null && (expected != null
                    ? pantry.selectedItem == expected
                    : ItemNameContains(pantry.selectedItem, "Bun"));
            }
            default:
            {
                AssemblyRecipeDefinition selected = station.GetComponent<AssemblyStation>()?.GetSelectedRecipe();
                return selected != null && (config != null && config.burgerBase != null
                    ? selected.output == config.burgerBase
                    : (selected.recipeName ?? "").IndexOf("Hamburger", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || ItemNameContains(selected.output, "Hamburger"));
            }
        }
    }

    static bool ItemNameContains(ItemDefinition item, string expected) => item != null
        && ((item.itemName ?? item.name ?? "").IndexOf(expected, System.StringComparison.OrdinalIgnoreCase) >= 0);

    static string GetRecipeTutorialChoiceName(int index)
    {
        return index switch
        {
            0 => "Raw Patty",
            1 => "Cooked Patty",
            2 => "Bun",
            _ => "Hamburger",
        };
    }

    static (Component source, Component destination) GetExpectedFlowLink(ProductionFlowPlan flow)
    {
        if (flow == null) return (null, null);
        var links = new (StationType? from, StationType? to)[]
        {
            (StationType.Freezer, StationType.Grill),
            (StationType.Grill, StationType.Assembly),
            (StationType.Pantry, StationType.Assembly),
            (StationType.Assembly, (StationType?)null),
        };
        int next = 0;
        for (; next < links.Length; next++)
        {
            bool found = false;
            if (flow.connections != null)
                foreach (ProductionFlowConnection connection in flow.connections)
                    if (connection != null && connection.from != null && connection.to != null
                        && MatchesExpectedStation(connection.from, links[next].from)
                        && MatchesExpectedStation(connection.to, links[next].to))
                    { found = true; break; }
            if (!found) break;
        }
        if (next >= links.Length) return (null, null);
        Component source = FindFlowLinkStation(flow, links[next].from);
        Component destination = FindFlowLinkStation(flow, links[next].to);
        // A burger flow is complete only after its last link reaches the Pickup Station.
        // Until then keep Finish out of focus and point Gus at that final destination.
        return (source, destination);
    }

    static Component FindFlowLinkStation(ProductionFlowPlan flow, StationType? expected)
    {
        // Prefer nodes already selected into this flow. That keeps the tutorial
        // arrow attached to the same station when duplicate equipment exists.
        if (flow != null && flow.stations != null)
            foreach (GameObject station in flow.stations)
                if (station != null && MatchesExpectedStation(station, expected))
                    return FindExpectedStationComponent(station, expected);

        // The first source and the next unconnected destination have not yet
        // been added to the captured graph, so resolve them from placed objects
        // only. Unplaced catalog previews and stale StationNodes are not valid
        // tutorial targets.
        foreach (StationNode node in FindObjectsByType<StationNode>(FindObjectsSortMode.None))
            if (node != null && node.GetComponent<PlacedBuildItem>() != null
                && MatchesExpectedStation(node.gameObject, expected))
                return FindExpectedStationComponent(node.gameObject, expected);

        if (expected == null)
            foreach (HeatLampStation pickup in FindObjectsByType<HeatLampStation>(FindObjectsSortMode.None))
                if (pickup != null && pickup.GetComponent<PlacedBuildItem>() != null)
                    return pickup;
        return null;
    }

    static Component FindExpectedStationComponent(GameObject station, StationType? expected)
    {
        if (station == null) return null;
        if (!expected.HasValue) return station.GetComponent<HeatLampStation>();
        return expected.Value switch
        {
            StationType.Freezer => station.GetComponent<FreezerStation>(),
            StationType.Grill => station.GetComponent<GrillStation>(),
            StationType.Pantry => station.GetComponent<PantryStation>(),
            StationType.Assembly => station.GetComponent<AssemblyStation>(),
            _ => station.GetComponent<StationNode>(),
        };
    }

    static bool MatchesExpectedStation(GameObject station, StationType? expected)
    {
        if (station == null) return false;
        if (!expected.HasValue) return station.GetComponent<HeatLampStation>() != null;
        return KitchenEmployee.GetStationTypeFrom(station) == expected;
    }

    public static bool ShouldHighlightInventoryItem(ItemDefinition item)
    {
        if (item == null || Instance == null || !Instance.running || IsStationLocked(item))
            return false;
        return ItemMatchesHighlight(item, Instance.currentHighlight);
    }

    // Checklist stations unlock together after the tutorial introduces them.
    public static bool IsStationLocked(ItemDefinition item)
    {
        if (item == null || !BlocksProgression) return false;
        if (IsChecklistStation(item))
            return true;
        for (int i = 0; i < Steps.Length; i++)
        {
            if (Steps[i].requirePlaced && ItemMatchesHighlight(item, Steps[i].highlight))
                return i > Instance.furthestStepIndex;
        }
        return false;
    }

    static bool IsChecklistStation(ItemDefinition item)
    {
        return ItemMatchesHighlight(item, Highlight.Freezer)
            || ItemMatchesHighlight(item, Highlight.Grill)
            || ItemMatchesHighlight(item, Highlight.Pantry)
            || ItemMatchesHighlight(item, Highlight.Assembly)
            || ItemMatchesHighlight(item, Highlight.Register)
            || ItemMatchesHighlight(item, Highlight.Fryer)
            || ItemMatchesHighlight(item, Highlight.HeatLamp)
            || ItemMatchesHighlight(item, Highlight.Drink)
            || (item.itemName != null && item.itemName.Contains("Shake Station"));
    }

    static bool ItemMatchesHighlight(ItemDefinition item, Highlight kind)
    {
        string name = (item.itemName ?? item.name ?? "").ToLowerInvariant();
        switch (kind)
        {
            case Highlight.Register: return item.buildFunction == ItemDefinition.BuildFunction.Register;
            case Highlight.Freezer: return name.Contains("freezer");
            case Highlight.Grill: return name.Contains("grill");
            case Highlight.Fryer: return name.Contains("fryer");
            case Highlight.Drink: return name.Contains("cutting");
            case Highlight.Assembly: return name.Contains("assembly");
            case Highlight.HeatLamp: return name.Contains("heat") || name.Contains("pickup");
            case Highlight.Pantry: return name.Contains("pantry");
            case Highlight.Inventory:
                return name.Contains("freezer") || name.Contains("grill") || name.Contains("fryer")
                    || name.Contains("drink") || name.Contains("assembly") || name.Contains("heat")
                    || name.Contains("pickup") || name.Contains("pantry");
            default:
                return false;
        }
    }

    Transform FindHighlightTarget(Highlight kind)
    {
        switch (kind)
        {
            case Highlight.Register:
                var reg = FindTutorialRegister();
                return reg != null ? reg.transform : null;
            case Highlight.Inventory:
                var tabsInv = FindFirstObjectByType<MainHudTabs>();
                return tabsInv != null ? tabsInv.InventoryTabTransform : null;
            case Highlight.Management:
                var tabsMgmt = FindFirstObjectByType<MainHudTabs>();
                return tabsMgmt != null ? tabsMgmt.ManagementTabTransform : null;
            case Highlight.Freezer:
                return StationOrNull(FindTutorialPlacedStation<FreezerStation>());
            case Highlight.Grill:
                return StationOrNull(FindTutorialPlacedStation<GrillStation>());
            case Highlight.Fryer:
                return StationOrNull(FindTutorialPlacedStation<FryerStation>());
            case Highlight.Drink:
                return StationOrNull(FindTutorialPlacedStation<CuttingStation>());
            case Highlight.Assembly:
                return StationOrNull(FindTutorialPlacedStation<AssemblyStation>());
            case Highlight.HeatLamp:
                return StationOrNull(FindTutorialPlacedStation<HeatLampStation>());
            case Highlight.Pantry:
                return StationOrNull(FindTutorialPlacedStation<PantryStation>());
            default:
                return null;
        }
    }

    static Transform StationOrNull(Component c)
    {
        if (c == null || !c.gameObject.activeInHierarchy) return null;
        return c.transform;
    }

    void ClearHighlight()
    {
        if (highlight != null)
            highlight.SetActive(false);
        highlightTarget = null;
    }

    void HideUI()
    {
        ClearTutorialCustomerFollow();
        if (tutorialTypewriterVoiceSource != null)
            tutorialTypewriterVoiceSource.Stop();
        SetFlowLinkingVideoVisible(false);
        SetTutorialFlowGuideVisible(false);
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
        else if (panel != null)
            panel.SetActive(false);
    }

    void EnsureUI()
    {
        EnsureTutorialTypewriterVoice();
        if (canvasRoot != null && panel != null && nextButton != null && cameraPromptPanel != null
            && fastForwardHelpCard != null && fastForwardHelpArrow != null
            && fastForwardRateHelpArrow != null
            && nextButton.transform.IsChildOf(panel.transform))
        {
            EnsureFlowLinkingVideoUi();
            ISEGlossaryLinkHandler.Attach(titleText);
            ISEGlossaryLinkHandler.Attach(bodyText);
            canvasRoot.SetActive(true);
            if (panel != null) panel.SetActive(true);
            return;
        }

        if (canvasRoot != null)
        {
            ReleaseFlowLinkingVideo();
            Destroy(canvasRoot);
        }

        canvasRoot = new GameObject("OnboardingTutorialCanvas", typeof(RectTransform));
        canvasRoot.transform.SetParent(transform, false);
        calloutPanelPositionVelocity = Vector2.zero;
        calloutPanelPositionInitialized = false;
        lastCalloutPanelMotionFrame = -1;
        var canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4200;
        var scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasRoot.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(560f, 0f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.985f);
        // The tutorial card can overlap a highlighted station in the kitchen.
        // Let world clicks pass through its background; the Next button remains
        // independently clickable.
        bg.raycastTarget = false;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = new GameObject("GusHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        header.transform.SetParent(panel.transform, false);
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        header.GetComponent<LayoutElement>().preferredHeight = 60f;

        var iconFrame = new GameObject("GusIconFrame", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement));
        iconFrame.transform.SetParent(header.transform, false);
        Image iconFrameImage = iconFrame.GetComponent<Image>();
        iconFrameImage.color = GameUITheme.Surface;
        iconFrameImage.raycastTarget = false;
        var iconLayout = iconFrame.GetComponent<LayoutElement>();
        iconLayout.minWidth = 58f;
        iconLayout.preferredWidth = 58f;
        iconLayout.minHeight = 58f;
        iconLayout.preferredHeight = 58f;
        var iconOutline = iconFrame.AddComponent<Outline>();
        iconOutline.effectColor = GameUITheme.Accent;
        iconOutline.effectDistance = new Vector2(2f, -2f);

        var iconObject = new GameObject("GusFace", typeof(RectTransform), typeof(RawImage),
            typeof(GusCutscenePreview));
        iconObject.transform.SetParent(iconFrame.transform, false);
        var iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(3f, 3f);
        iconRect.offsetMax = new Vector2(-3f, -3f);
        var iconImage = iconObject.GetComponent<RawImage>();
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        var titleController = FindFirstObjectByType<TitleScreenController>();
        GameObject gusModel = titleController != null && titleController.gusPrefab != null
            ? titleController.gusPrefab
            : Resources.Load<GameObject>("Prefabs/character_default");
        if (gusModel != null)
            iconObject.GetComponent<GusCutscenePreview>().Configure(gusModel, iconImage, true);

        var headerText = new GameObject("GusHeaderText", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        headerText.transform.SetParent(header.transform, false);
        var textLayout = headerText.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 1f;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        headerText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var dragHandle = new GameObject("DragHandle", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement), typeof(TutorialPanelDragHandle));
        dragHandle.transform.SetParent(header.transform, false);
        panelDragHandle = dragHandle.GetComponent<TutorialPanelDragHandle>();
        var dragHandleImage = dragHandle.GetComponent<Image>();
        dragHandleImage.color = new Color(0.2f, 0.23f, 0.29f, 1f);
        dragHandleImage.raycastTarget = true;
        panelDragHandle.Initialize((RectTransform)panel.transform, (RectTransform)canvasRoot.transform);
        var dragHandleLayout = dragHandle.GetComponent<LayoutElement>();
        dragHandleLayout.minWidth = dragHandleLayout.preferredWidth = 52f;
        dragHandleLayout.minHeight = dragHandleLayout.preferredHeight = 52f;
        var moveHandleGlyph = new GameObject("MoveHandleGlyph", typeof(RectTransform), typeof(Image));
        moveHandleGlyph.transform.SetParent(dragHandle.transform, false);
        var glyphRect = (RectTransform)moveHandleGlyph.transform;
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        glyphRect.offsetMin = new Vector2(8f, 8f);
        glyphRect.offsetMax = new Vector2(-8f, -8f);
        var glyphImage = moveHandleGlyph.GetComponent<Image>();
        glyphImage.sprite = Resources.Load<Sprite>("Tutorial/MoveHandleWhite");
        glyphImage.preserveAspect = true;
        glyphImage.raycastTarget = false;

        stepLabel = CreateLabel(headerText.transform, "", 14, FontStyles.Bold, GameUITheme.Accent);
        stepLabel.alignment = TextAlignmentOptions.Left;
        titleText = CreateLabel(headerText.transform, "", 22, FontStyles.Bold, Color.white);
        titleText.alignment = TextAlignmentOptions.Left;
        ISEGlossaryLinkHandler.Attach(titleText);
        bodyText = CreateLabel(panel.transform, "", 20, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Truncate;
        ISEGlossaryLinkHandler.Attach(bodyText);

        EnsureFlowLinkingVideoUi();

        coreStationChecklistText = CreateLabel(panel.transform, "", 17, FontStyles.Bold, GameUITheme.TextPrimary);
        coreStationChecklistText.alignment = TextAlignmentOptions.TopLeft;
        coreStationChecklistText.textWrappingMode = TextWrappingModes.Normal;
        coreStationChecklistText.fontSize = 15f;
        coreStationChecklistText.fontSizeMin = 13f;
        coreStationChecklistText.fontSizeMax = 15f;
        coreStationChecklistText.enableAutoSizing = true;
        coreStationChecklistText.overflowMode = TextOverflowModes.Truncate;
        coreStationChecklistText.gameObject.SetActive(false);

        cameraPromptPanel = new GameObject("CameraControlPrompts", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        cameraPromptPanel.transform.SetParent(panel.transform, false);
        var cameraPromptLayout = cameraPromptPanel.GetComponent<VerticalLayoutGroup>();
        cameraPromptLayout.padding = new RectOffset(0, 0, 2, 2);
        cameraPromptLayout.spacing = 3f;
        cameraPromptLayout.childAlignment = TextAnchor.MiddleCenter;
        cameraPromptLayout.childControlWidth = true;
        cameraPromptLayout.childControlHeight = true;
        cameraPromptLayout.childForceExpandWidth = true;
        cameraPromptLayout.childForceExpandHeight = false;
        var cameraPromptSize = cameraPromptPanel.GetComponent<LayoutElement>();
        cameraPromptSize.minHeight = cameraPromptSize.preferredHeight = 96f;
        cameraPromptSize.flexibleHeight = 0f;
        cameraMovePromptStatus = CreateCameraPromptRow(cameraPromptPanel.transform, "Move camera",
            InputPromptId.KeyboardW, InputPromptId.KeyboardA,
            InputPromptId.KeyboardS, InputPromptId.KeyboardD);
        cameraRotatePromptStatus = CreateCameraPromptRow(cameraPromptPanel.transform, "Rotate view",
            InputPromptId.MouseRight, InputPromptId.MouseMove);
        cameraZoomPromptStatus = CreateCameraPromptRow(cameraPromptPanel.transform, "Zoom camera",
            InputPromptId.MouseScroll);
        cameraPromptPanel.SetActive(false);

        var footer = new GameObject("Navigation", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(panel.transform, false);
        footer.GetComponent<LayoutElement>().preferredHeight = 46f;
        var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        nextButton = CreateSideButton(footer.transform, "Next", OnNext, new Color(0.22f, 0.55f, 0.38f, 1f), 1);
        var nextLayout = nextButton.gameObject.AddComponent<LayoutElement>();
        nextLayout.preferredWidth = 188f;
        nextLayout.preferredHeight = 46f;
        nextLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();
        nextLabel.enableAutoSizing = true;
        nextLabel.fontSizeMin = 16f;
        nextLabel.fontSizeMax = 22f;
        calloutArrow = CreateCalloutArrow(canvasRoot.transform);
        CreateFastForwardHelpCard(canvasRoot.transform);
        GameUITheme.ApplyToCanvas(canvas);
    }

    void CreateFastForwardHelpCard(Transform parent)
    {
        fastForwardHelpArrow = CreateCalloutArrow(parent);
        fastForwardRateHelpArrow = CreateCalloutArrow(parent);

        fastForwardHelpCard = new GameObject("FastForwardTutorialCard", typeof(RectTransform),
            typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        fastForwardHelpCard.transform.SetParent(parent, false);
        fastForwardHelpCardRect = (RectTransform)fastForwardHelpCard.transform;
        fastForwardHelpCardRect.anchorMin = fastForwardHelpCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        fastForwardHelpCardRect.pivot = new Vector2(0.5f, 0.5f);
        fastForwardHelpCardRect.sizeDelta = new Vector2(430f, 104f);

        Image background = fastForwardHelpCard.GetComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.985f);
        background.raycastTarget = false;

        VerticalLayoutGroup layout = fastForwardHelpCard.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        LayoutElement cardSize = fastForwardHelpCard.GetComponent<LayoutElement>();
        cardSize.minWidth = cardSize.preferredWidth = 430f;
        cardSize.minHeight = cardSize.preferredHeight = 104f;
        cardSize.flexibleWidth = cardSize.flexibleHeight = 0f;

        TextMeshProUGUI heading = CreateLabel(fastForwardHelpCard.transform,
            "SHIFT SPEED", 16f, FontStyles.Bold, GameUITheme.Accent);
        heading.alignment = TextAlignmentOptions.Left;
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        TextMeshProUGUI instructions = CreateLabel(fastForwardHelpCard.transform,
            "<b>Fast Forward:</b> Run the shift faster.\n<b>Speed:</b> Choose the rate Fast Forward uses.",
            18f, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f));
        instructions.alignment = TextAlignmentOptions.Left;
        instructions.textWrappingMode = TextWrappingModes.Normal;
        fastForwardHelpCard.SetActive(false);
    }

    void UpdateFastForwardHelpCard()
    {
        bool show = running && stepIndex == 3 && !tutorialUnlockContinuePending;
        SetFastForwardHelpVisible(show);
        if (!show || fastForwardHelpCardRect == null || canvasRoot == null)
            return;

        if (cachedTutorialTimeControls == null)
            cachedTutorialTimeControls = FindFirstObjectByType<GameTimeUI>(FindObjectsInactive.Exclude);
        GameTimeUI timeControls = cachedTutorialTimeControls;
        RectTransform fastButton = timeControls != null && timeControls.fastForwardButton != null
            ? timeControls.fastForwardButton.transform as RectTransform : null;
        RectTransform rateButton = timeControls != null && timeControls.fastForwardRateButton != null
            ? timeControls.fastForwardRateButton.transform as RectTransform : null;
        RectTransform rootRect = canvasRoot.transform as RectTransform;
        if (fastButton == null || rateButton == null || rootRect == null
            || !fastButton.gameObject.activeInHierarchy || !rateButton.gameObject.activeInHierarchy)
        {
            SetFastForwardHelpVisible(false);
            return;
        }

        if (!TryGetScreenLocalBounds(fastButton, rootRect, fastForwardButtonCorners, out Rect fastBounds)
            || !TryGetScreenLocalBounds(rateButton, rootRect, fastForwardRateButtonCorners, out Rect rateBounds))
        {
            SetFastForwardHelpVisible(false);
            return;
        }

        Vector2 cardSize = fastForwardHelpCardRect.sizeDelta;
        float centerX = (fastBounds.center.x + rateBounds.center.x) * 0.5f;
        float cardY = Mathf.Min(fastBounds.yMin, rateBounds.yMin) - cardSize.y * 0.5f - 28f;
        float lowerLimit = rootRect.rect.yMin + cardSize.y * 0.5f + 150f;
        if (cardY < lowerLimit)
            cardY = Mathf.Max(fastBounds.yMax, rateBounds.yMax) + cardSize.y * 0.5f + 26f;
        centerX = Mathf.Clamp(centerX,
            rootRect.rect.xMin + cardSize.x * 0.5f + 12f,
            rootRect.rect.xMax - cardSize.x * 0.5f - 12f);
        cardY = Mathf.Clamp(cardY,
            rootRect.rect.yMin + cardSize.y * 0.5f + 12f,
            rootRect.rect.yMax - cardSize.y * 0.5f - 12f);

        Vector2 cardPosition = new Vector2(centerX, cardY);
        fastForwardHelpCardRect.anchoredPosition = cardPosition;
        PositionSpecificCalloutArrow(fastForwardHelpArrow, cardPosition, cardSize,
            fastBounds.center, fastBounds);
        PositionSpecificCalloutArrow(fastForwardRateHelpArrow, cardPosition, cardSize,
            rateBounds.center, rateBounds);
    }

    void SetFastForwardHelpVisible(bool visible)
    {
        if (fastForwardHelpCard != null && fastForwardHelpCard.activeSelf != visible)
            fastForwardHelpCard.SetActive(visible);
        if (!visible)
        {
            if (fastForwardHelpArrow != null) fastForwardHelpArrow.gameObject.SetActive(false);
            if (fastForwardRateHelpArrow != null) fastForwardRateHelpArrow.gameObject.SetActive(false);
        }
    }

    void PositionSpecificCalloutArrow(RectTransform arrow, Vector2 cardPosition, Vector2 cardSize,
        Vector2 targetPosition, Rect targetBounds)
    {
        if (arrow == null) return;
        RectTransform previousArrow = calloutArrow;
        calloutArrow = arrow;
        PositionCalloutArrow(cardPosition, cardSize, targetPosition, targetBounds, true);
        calloutArrow = previousArrow;
    }

    static bool TryGetScreenLocalBounds(RectTransform target, RectTransform rootRect,
        Vector3[] corners, out Rect bounds)
    {
        bounds = default;
        if (target == null || rootRect == null || corners == null || corners.Length < 4) return false;
        target.GetWorldCorners(corners);
        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Camera eventCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? targetCanvas.worldCamera : null;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null,
                    out Vector2 local))
                continue;
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        if (min.x == float.MaxValue) return false;
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    void EnsureFlowLinkingVideoUi()
    {
        if (flowLinkingVideoFrame != null || panel == null || canvasRoot == null)
            return;

        VideoClip clip = Resources.Load<VideoClip>(FlowLinkingVideoResourcePath);
        if (clip == null)
        {
            Debug.LogWarning($"Tutorial video not found at Resources/{FlowLinkingVideoResourcePath}.");
            return;
        }

        flowLinkingVideoFrame = new GameObject("FlowLinkingDemo", typeof(RectTransform),
            typeof(Image), typeof(LayoutElement));
        flowLinkingVideoFrame.transform.SetParent(panel.transform, false);
        Image frameImage = flowLinkingVideoFrame.GetComponent<Image>();
        frameImage.color = new Color(0.025f, 0.03f, 0.04f, 1f);
        frameImage.raycastTarget = false;
        var frameLayout = flowLinkingVideoFrame.GetComponent<LayoutElement>();
        frameLayout.minHeight = 220f;
        frameLayout.preferredHeight = 270f;
        frameLayout.flexibleHeight = 0f;

        GameObject videoImageObject = new GameObject("Video", typeof(RectTransform),
            typeof(RawImage), typeof(AspectRatioFitter));
        videoImageObject.transform.SetParent(flowLinkingVideoFrame.transform, false);
        RectTransform videoImageRect = (RectTransform)videoImageObject.transform;
        videoImageRect.anchorMin = Vector2.zero;
        videoImageRect.anchorMax = Vector2.one;
        videoImageRect.offsetMin = Vector2.zero;
        videoImageRect.offsetMax = Vector2.zero;
        flowLinkingVideoImage = videoImageObject.GetComponent<RawImage>();
        flowLinkingVideoImage.raycastTarget = false;
        var aspect = videoImageObject.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 16f / 9f;

        flowLinkingVideoTexture = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32)
        {
            name = "TutorialFlowLinkingVideoTexture"
        };
        flowLinkingVideoTexture.Create();
        flowLinkingVideoImage.texture = flowLinkingVideoTexture;

        GameObject playerObject = new GameObject("FlowLinkingDemoPlayer", typeof(VideoPlayer));
        playerObject.transform.SetParent(canvasRoot.transform, false);
        flowLinkingVideoPlayer = playerObject.GetComponent<VideoPlayer>();
        flowLinkingVideoPlayer.playOnAwake = false;
        flowLinkingVideoPlayer.isLooping = true;
        flowLinkingVideoPlayer.skipOnDrop = true;
        flowLinkingVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        flowLinkingVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        flowLinkingVideoPlayer.targetTexture = flowLinkingVideoTexture;
        flowLinkingVideoPlayer.source = VideoSource.VideoClip;
        flowLinkingVideoPlayer.clip = clip;

        if (bodyText != null)
            flowLinkingVideoFrame.transform.SetSiblingIndex(bodyText.transform.GetSiblingIndex() + 1);
        flowLinkingVideoFrame.SetActive(false);
    }

    void SetFlowLinkingVideoVisible(bool visible)
    {
        if (flowLinkingVideoFrame != null)
            flowLinkingVideoFrame.SetActive(visible);

        if (flowLinkingVideoPlayer == null)
            return;

        if (visible)
            flowLinkingVideoPlayer.Play();
        else if (flowLinkingVideoPlayer.isPlaying)
            flowLinkingVideoPlayer.Stop();
    }

    void ReleaseFlowLinkingVideo()
    {
        if (flowLinkingVideoPlayer != null)
        {
            flowLinkingVideoPlayer.Stop();
            flowLinkingVideoPlayer.targetTexture = null;
        }

        if (flowLinkingVideoTexture != null)
        {
            flowLinkingVideoTexture.Release();
            Destroy(flowLinkingVideoTexture);
            flowLinkingVideoTexture = null;
        }

        flowLinkingVideoFrame = null;
        flowLinkingVideoImage = null;
        flowLinkingVideoPlayer = null;
    }

    static TextMeshProUGUI CreateCameraPromptRow(Transform parent, string action,
        params InputPromptId[] prompts)
    {
        var row = new GameObject(action, typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.96f);
        row.GetComponent<Image>().raycastTarget = false;
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 8, 1, 1);
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        var rowSize = row.GetComponent<LayoutElement>();
        rowSize.minHeight = rowSize.preferredHeight = 28f;
        rowSize.flexibleHeight = 0f;

        var iconGroup = new GameObject("InputIcons", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        iconGroup.transform.SetParent(row.transform, false);
        var iconLayout = iconGroup.GetComponent<HorizontalLayoutGroup>();
        iconLayout.spacing = 2f;
        iconLayout.childAlignment = TextAnchor.MiddleLeft;
        iconLayout.childControlWidth = true;
        iconLayout.childControlHeight = true;
        iconLayout.childForceExpandWidth = false;
        iconLayout.childForceExpandHeight = false;
        int iconCount = 0;
        foreach (InputPromptId prompt in prompts)
        {
            Sprite sprite = InputPromptLibrary.GetSprite(prompt);
            if (sprite == null) continue;
            var icon = new GameObject("Prompt_" + prompt, typeof(RectTransform),
                typeof(Image), typeof(LayoutElement));
            icon.transform.SetParent(iconGroup.transform, false);
            var iconSize = icon.GetComponent<LayoutElement>();
            iconSize.minWidth = iconSize.preferredWidth = 21f;
            iconSize.minHeight = iconSize.preferredHeight = 21f;
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            iconCount++;
        }
        iconGroup.GetComponent<LayoutElement>().preferredWidth = iconCount * 21f
            + Mathf.Max(0, iconCount - 1) * iconLayout.spacing;

        TextMeshProUGUI actionLabel = CreateLabel(row.transform, action, 14f,
            FontStyles.Bold, new Color(0.92f, 0.94f, 0.98f));
        actionLabel.alignment = TextAlignmentOptions.Left;
        actionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var actionSize = actionLabel.GetComponent<LayoutElement>();
        actionSize.minWidth = 0f;
        actionSize.flexibleWidth = 1f;

        TextMeshProUGUI status = CreateLabel(row.transform, "TRY IT", 11f,
            FontStyles.Bold, new Color(0.72f, 0.76f, 0.82f, 1f));
        status.alignment = TextAlignmentOptions.Right;
        status.textWrappingMode = TextWrappingModes.NoWrap;
        var statusSize = status.GetComponent<LayoutElement>();
        statusSize.minWidth = statusSize.preferredWidth = 56f;
        statusSize.flexibleWidth = 0f;
        return status;
    }

    void EnsureTutorialTypewriterVoice()
    {
        if (tutorialTypewriterVoiceSource != null)
            return;

        var voiceObject = new GameObject("TutorialCardSpeech");
        voiceObject.transform.SetParent(transform, false);
        tutorialTypewriterVoiceSource = voiceObject.AddComponent<AudioSource>();
        IntroCutsceneUI.ConfigureTypewriterVoiceSource(tutorialTypewriterVoiceSource);
    }

    static RectTransform CreateCalloutArrow(Transform parent)
    {
        var root = new GameObject("TutorialArrow", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(56f, 24f);
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Color outlineColor = new Color32(25, 30, 39, 255);
        CreateArrowShaft(root.transform, "ShaftOutline", 8f, 15f, outlineColor);
        CreateArrowShaft(root.transform, "Shaft", 4f, 14f, GameUITheme.Accent);
        CreateArrowHead(root.transform, "HeadUpperOutline", 42f, 27f, 9f, outlineColor);
        CreateArrowHead(root.transform, "HeadLowerOutline", -42f, 27f, 9f, outlineColor);
        CreateArrowHead(root.transform, "HeadUpper", 42f, 22f, 4f, GameUITheme.Accent);
        CreateArrowHead(root.transform, "HeadLower", -42f, 22f, 4f, GameUITheme.Accent);
        root.transform.SetAsFirstSibling();
        root.SetActive(false);
        return rootRect;
    }

    static void CreateArrowShaft(Transform parent, string name, float thickness, float tipInset, Color color)
    {
        var shaft = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        shaft.transform.SetParent(parent, false);
        var shaftRect = (RectTransform)shaft.transform;
        shaftRect.anchorMin = new Vector2(0f, 0.5f);
        shaftRect.anchorMax = new Vector2(1f, 0.5f);
        shaftRect.offsetMin = new Vector2(0f, -thickness * 0.5f);
        shaftRect.offsetMax = new Vector2(-tipInset, thickness * 0.5f);
        UnityEngine.UI.Image shaftImage = shaft.GetComponent<UnityEngine.UI.Image>();
        shaftImage.color = color;
        shaftImage.raycastTarget = false;
    }

    static void CreateArrowHead(Transform parent, string name, float rotation,
        float width, float thickness, Color color)
    {
        var head = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        head.transform.SetParent(parent, false);
        var rect = (RectTransform)head.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, thickness);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        var image = head.GetComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = false;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 0f;
        le.flexibleHeight = 0f;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return tmp;
    }

    static Button CreateSideButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color, int side)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(188f, 46f);
        rt.anchoredPosition = new Vector2(470f, side < 0 ? 51f : 103f);
        go.GetComponent<Image>().color = color;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 22;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }
}
