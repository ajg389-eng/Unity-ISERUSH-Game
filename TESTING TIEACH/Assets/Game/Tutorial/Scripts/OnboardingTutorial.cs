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
    const string TutorialArrowCursorResource = "Cursors/KenneyUpLeftCursorOutlined";
    const int AnalyticsTourCloseStage = 5;
    const int OnboardingStepCount = 4;
    const int MilestoneOneBottleneckStep = 4;
    const int MilestoneTwoAnalyticsStep = 5;
    const int MilestoneOneIntroStep = 6;
    const int MilestoneOneFlowDemoStep = 7;
    const int MilestoneOneRecipesStep = 8;
    const int MilestoneOneBuildStep = 9;
    const int MilestoneOneObjectivesStep = 10;
    const int MilestoneThreeSupplyStep = 11;
    const int HireWorkerStep = 12;
    const int AssignWorkerStep = 13;
    const int SupplyTourLastPage = 2;
    const int BaselineRunCustomerCount = 15;
    const float BaselineRunDurationSeconds = 180f;
    const float FeatureLockRefreshSeconds = 0.35f;

    public static OnboardingTutorial Instance { get; private set; }

    public static bool IsComplete => PlayerPrefs.GetInt(PrefsCompleteKey, 0) == 1;
    public static bool IsActive => Instance != null && Instance.running
        && !Instance.milestoneOneLessonActive;
    public static bool CanStartShiftFromPlan => Instance == null || (!Instance.milestoneOneLessonActive
        && (!IsActive || (Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireShiftStarted && !Instance.tutorialShiftStarted)));
    public static bool BlocksAutoCustomers => IsActive || !IsComplete;
    public static bool AllowsPracticeCustomer => IsActive
        && (Steps[Instance.stepIndex].liveCustomer || Instance.observingImprovedLayout);
    public static bool BlocksProgression => IsActive || (!IsComplete && Instance != null && Instance.pendingStart);
    public static int StationUnlockProgress => BlocksProgression ? Instance.furthestStepIndex : int.MaxValue;
    public static bool IsResearchTutorialActive => IsCurrentStep(step => step.requireResearch);
    public static bool IsRecipeTutorialActive => IsCurrentStep(step => step.requireRecipeScreen);
    internal static bool IsFirstDayRecipeAccessLocked => IsFirstDayFeatureLockedUntil(MilestoneOneRecipesStep);
    internal static bool IsFirstDayObjectivesAccessLocked => IsFirstDayFeatureLockedUntil(MilestoneOneObjectivesStep);
    internal static bool IsAnalyticsAccessLocked => IsComplete
        && !MilestoneFeatures.HasReached(MilestoneFeatures.CapacityAndOptimization);

    internal static bool HasIntroducedShiftQuestions(MilestoneDefinition milestone)
    {
        OnboardingTutorial tutorial = Instance;
        if (tutorial == null || milestone == null || !IsComplete) return false;
        return milestone.milestoneId switch
        {
            "milestone_01" => tutorial.milestoneOneIntroProgress > MilestoneOneObjectivesStep,
            "milestone_02" => tutorial.milestoneOneAnalyticsLessonDone,
            "milestone_03" => tutorial.supplyLessonDone,
            _ => true
        };
    }

    static bool IsFirstDayFeatureLockedUntil(int step)
    {
        return Instance != null && IsComplete && Instance.milestoneOneIntroProgress < step
            && MilestoneProgressManager.Instance?.ActiveMilestoneId == "milestone_01";
    }
    public static bool IsAnalyticsTutorialActive => IsCurrentStep(step => step.requireAnalyticsScreen)
        || Instance != null && Instance.milestoneOneLessonActive
            && Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireAnalyticsScreen;
    public static bool IsMenuTutorialActive => IsCurrentStep(step => step.requireMenuScreen);
    public static bool IsObjectivesTutorialActive => IsCurrentStep(step => step.requireObjectivesScreen);
    public static bool IsBuildTutorialActive => IsCurrentStep(step => step.requireBuildOverview);
    internal static bool IsLayoutImprovementLesson => IsCurrentStep(step => step.requireOptimizedLayout);
    public static bool IsTimeControlTutorialActive => IsCurrentStep(step =>
        step.requireTimeControlInteractions || step.requireFastForwardControl);
    public static bool IsFeatureLocked(Button button)
    {
        if (button == null) return false;

        for (Transform ancestor = button.transform; ancestor != null; ancestor = ancestor.parent)
        {
            Transform overlayTransform = ancestor.Find("TutorialFeatureLockOverlay");
            if (overlayTransform != null
                && overlayTransform.GetComponent<TutorialFeatureLockOverlay>() != null)
                return true;
        }

        OnboardingTutorial tutorial = Instance;
        if (tutorial == null) return false;

        bool onboardingGateActive = tutorial.gameplayReady && !IsComplete
            && (tutorial.running || tutorial.pendingStart);
        if (onboardingGateActive)
            return !tutorial.IsFeatureIntroduced(button);

        bool deferredGateActive = tutorial.gameplayReady && IsComplete
            && (IsAnalyticsAccessLocked || !MilestoneFeatures.SupplyPlanningUnlocked
                || IsFirstDayObjectivesAccessLocked);
        return deferredGateActive && tutorial.IsDeferredMilestoneFeatureLocked(button);
    }
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

    void RefreshTutorialFeatureLocks(bool force = false)
    {
        bool tutorialGateActive = gameplayReady && !IsComplete && (running || pendingStart);
        bool deferredLessonGateActive = gameplayReady && IsComplete
            && (IsAnalyticsAccessLocked || !MilestoneFeatures.SupplyPlanningUnlocked
                || IsFirstDayObjectivesAccessLocked);
        if (!tutorialGateActive && !deferredLessonGateActive)
        {
            if (tutorialFeatureLocksApplied)
            {
                ClearTutorialFeatureLocks();
                tutorialFeatureLocksApplied = false;
            }
            return;
        }

        if (!force && Time.unscaledTime < nextFeatureLockRefresh) return;
        nextFeatureLockRefresh = Time.unscaledTime + FeatureLockRefreshSeconds;
        tutorialFeatureLocksApplied = true;

        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        bool onboardingLocksActive = tutorialGateActive;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.GetComponent<TutorialFeatureLockOverlay>() != null)
                continue;

            bool locked = onboardingLocksActive
                ? !IsFeatureIntroduced(button)
                : IsDeferredMilestoneFeatureLocked(button);
            TutorialFeatureLockOverlay.SetLocked(button, locked);
        }

        RefreshTutorialPanelLocks(onboardingLocksActive);
    }

    void RefreshTutorialPanelLocks(bool onboardingLocksActive)
    {
        InventoryUI build = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (build != null)
        {
            TutorialFeatureLockOverlay.SetLocked(build.panel != null
                ? build.panel.transform as RectTransform : null,
                onboardingLocksActive && stepIndex < 3);

            if (build.tabPanels != null)
            {
                for (int i = 0; i < build.tabPanels.Length; i++)
                {
                    bool isUnintroducedBuildTab = i == 1 || i == 2;
                    if (!isUnintroducedBuildTab || build.tabPanels[i] == null) continue;
                    TutorialFeatureLockOverlay.SetLocked(build.tabPanels[i].transform as RectTransform,
                        onboardingLocksActive);
                }
            }
        }

        ManagementScreenController business = FindFirstObjectByType<ManagementScreenController>(
            FindObjectsInactive.Include);
        if (business != null)
        {
            TutorialFeatureLockOverlay.SetLocked(business.managementPanel != null
                ? business.managementPanel.transform as RectTransform : null,
                onboardingLocksActive && stepIndex < 3);

            if (business.tabPanels != null)
            {
                for (int i = 0; i < business.tabPanels.Length; i++)
                {
                    GameObject panelObject = business.tabPanels[i];
                    if (panelObject == null
                        || panelObject.GetComponentInChildren<SupplyPlanningScreenUI>(true) == null)
                        continue;

                    TutorialFeatureLockOverlay.SetLocked(panelObject.transform as RectTransform,
                        onboardingLocksActive);
                }
            }
        }

        ShiftPlanningUI planning = FindFirstObjectByType<ShiftPlanningUI>(FindObjectsInactive.Include);
        TutorialFeatureLockOverlay.SetLocked(planning != null ? planning.TutorialPlanningCard : null,
            onboardingLocksActive && stepIndex < 1);
        TutorialFeatureLockOverlay.SetLocked(planning != null ? planning.TutorialPlanningWarning : null,
            onboardingLocksActive && stepIndex < 1);

        AnalyticsPopupUI analytics = FindFirstObjectByType<AnalyticsPopupUI>(FindObjectsInactive.Include);
        bool analyticsLocked = onboardingLocksActive || IsAnalyticsAccessLocked;
        TutorialFeatureLockOverlay.SetLocked(analytics != null
            ? analytics.transform as RectTransform : null, analyticsLocked);

        ManagementModeController stationManagement = ManagementModeController.Instance;
        RectTransform stationPopupRect = stationManagement != null && stationManagement.stationPopup != null
            ? stationManagement.stationPopup.transform as RectTransform : null;
        bool stationHealthIntroduced = IsCurrentStep(step => step.requireStationHealthReview);
        bool stationHealthLocked = onboardingLocksActive && !stationHealthIntroduced;
        TutorialFeatureLockOverlay.SetLocked(stationPopupRect, stationHealthLocked);

        MissionListUI objectives = FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
        if (objectives != null && objectives.panelRoot != null)
            TutorialFeatureLockOverlay.SetLocked(objectives.panelRoot.transform as RectTransform,
                onboardingLocksActive && !IsObjectivesTutorialActive);
    }

    bool IsFeatureIntroduced(Button button)
    {
        if (button == null) return false;
        if (canvasRoot != null && button.transform.IsChildOf(canvasRoot.transform)) return true;
        if (button.GetComponentInParent<MilestoneUnlockPopupUI>(true) != null
            || button.GetComponentInParent<DebugMenu>(true) != null
            || button.GetComponentInParent<GusButtonHelpMode>(true) != null
            || button.GetComponentInParent<GuidebookUI>(true) != null)
            return true;
        if (button.GetComponentInParent<IngredientsOrderUI>(true) != null)
            return false;
        if (button.GetComponentInParent<MissionListUI>(true) != null)
            return IsObjectivesTutorialActive;

        string buttonName = button.gameObject.name;
        if (buttonName == "GusHelpButton" || buttonName == "GuidebookButton") return true;
        if (buttonName == "ObjectivesTopTab") return IsObjectivesTutorialActive;
        if (buttonName == "RecipesButton") return false;
        if (buttonName == "InventoryTab") return stepIndex >= 3;
        if (buttonName == "BusinessTab") return stepIndex >= 3;
        if (buttonName == "Tab_Workers") return stepIndex >= 3;
        if (buttonName == "Tab_Supplies") return false;

        MainHudTabs mainTabs = button.GetComponentInParent<MainHudTabs>(true);
        if (mainTabs != null)
        {
            if (mainTabs.TryGetWorkspaceTabIndex(button, out int workspaceTab))
                return workspaceTab == 0 || workspaceTab == 1 ? stepIndex >= 3 : false;
            if (button.transform == mainTabs.InventoryTabTransform) return stepIndex >= 3;
            if (button.transform == mainTabs.BusinessTabTransform) return stepIndex >= 3;
            return false;
        }

        if (IsAnalyticsControl(button))
            return false;

        if (button.GetComponentInParent<GameTimeUI>(true) != null
            || buttonName == "PauseButton" || buttonName == "PlayButton"
            || buttonName == "FastButton" || buttonName == "FastForwardButton"
            || buttonName == "FastForwardRateButton")
            return stepIndex >= 3 || IsTimeControlTutorialActive;

        ShiftPlanningUI planning = ShiftPlanningUI.Instance;
        if (planning != null && planning.IsTutorialPlanningControl(button))
            return stepIndex >= 1;

        SupplyPlanningScreenUI supply = button.GetComponentInParent<SupplyPlanningScreenUI>(true);
        if (supply != null)
            return false;

        WorkersUI workers = button.GetComponentInParent<WorkersUI>(true);
        if (workers != null)
        {
            return false;
        }

        if (button.GetComponentInParent<StoreAppearanceUI>(true) != null)
            return false;

        InventoryItemCardUI stationCard = button.GetComponentInParent<InventoryItemCardUI>(true);
        if (stationCard != null) return false;

        InventoryUI build = button.GetComponentInParent<InventoryUI>(true);
        if (build != null)
        {
            if (buttonName == "Tab_Floor" || buttonName == "Tab_Customize") return false;
            for (Transform ancestor = button.transform; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.name == InventoryUI.FloorPanelName
                    || ancestor.name == InventoryUI.CustomizePanelName)
                    return false;
            return stepIndex >= 3;
        }

        if (buttonName == "Tab_Stations") return stepIndex >= 3;
        if (buttonName == "Tab_Floor" || buttonName == "Tab_Customize") return false;
        if (IsWithinNamedPanel(button.transform, InventoryUI.StationsPanelName)) return stepIndex >= 3;
        if (IsWithinNamedPanel(button.transform, InventoryUI.FloorPanelName)
            || IsWithinNamedPanel(button.transform, InventoryUI.CustomizePanelName))
            return false;

        ManagementScreenController business = button.GetComponentInParent<ManagementScreenController>(true);
        if (business != null)
        {
            return stepIndex >= 3;
        }

        if (button.GetComponentInParent<BuildStationActionBar>(true) != null)
            return buttonName == "Move" && stepIndex >= 3;

        if (button.GetComponentInParent<ShiftPlanningUI>(true) != null)
            return stepIndex >= 1;

        if (button.GetComponentInParent<AnalyticsPopupUI>(true) != null
            || button.GetComponentInParent<MissionListUI>(true) != null
            || button.GetComponentInParent<ManagementModeController>(true) != null)
            return false;

        return false;
    }

    bool IsDeferredMilestoneFeatureLocked(Button button)
    {
        if (IsAnalyticsControl(button)) return IsAnalyticsAccessLocked;
        if (button != null)
        {
            if (button.name == "ObjectivesTopTab") return IsFirstDayObjectivesAccessLocked;
            if (button.name == "RecipesButton" || button.name == "OpenRecipes")
                return IsFirstDayRecipeAccessLocked;
            if (button.name == "WorkspaceTab_Supply" || button.name == "Tab_Supplies")
                return !MilestoneFeatures.SupplyPlanningUnlocked;
        }

        return false;
    }

    static bool IsAnalyticsControl(Button button)
    {
        return button != null && (button.gameObject.name == "AnalyticsButton"
            || button.GetComponentInParent<AnalyticsPopupUI>(true) != null);
    }

    static bool IsWithinNamedPanel(Transform target, string panelName)
    {
        for (Transform ancestor = target; ancestor != null; ancestor = ancestor.parent)
            if (ancestor.name == panelName)
                return true;
        return false;
    }

    void ClearTutorialFeatureLocks()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null && buttons[i].GetComponent<TutorialFeatureLockOverlay>() == null)
                TutorialFeatureLockOverlay.SetLocked(buttons[i], false);

        TutorialFeatureLockOverlay[] overlays = FindObjectsByType<TutorialFeatureLockOverlay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < overlays.Length; i++)
            if (overlays[i] != null && overlays[i].gameObject.name == "TutorialFeatureLockOverlay")
                Destroy(overlays[i].gameObject);
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

    internal static void NotifyFirstDayRecipeReviewed(ItemDefinition item)
    {
        OnboardingTutorial tutorial = Instance;
        if (tutorial == null || !tutorial.IsFirstDayRecipeReview) return;
        CustomerOrderConfig menu = ProductionManager.Instance?.orderConfig;
        if (item == null || menu == null) return;
        if (item == menu.friesItem)
            tutorial.firstDayFriesReviewed = true;
        else if (item == menu.drinkItem && tutorial.firstDayFriesReviewed)
            tutorial.firstDayShakeReviewed = true;
        tutorial.nextControlHighlightRefresh = 0f;
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
        if (Instance.gusDialogueText != null)
            Instance.gusDialogueText.maxVisibleCharacters = Instance.tutorialVisibleCharacters;
        Instance.autoAdvanceStep = -1;
        Instance.stepIndex++;
        Instance.ShowStep();
    }

    public static void NotifyObjectivesScreenOpened()
    {
        if (!IsObjectivesTutorialActive) return;
        if (Instance.IsMilestoneOneIntro)
            FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include)?.CloseKitchenWorkspace();
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
        if (!IsTimeControlTutorialActive || GameTimeManager.Instance == null
            || GameTimeManager.Instance.CurrentSpeed != GameTimeManager.SpeedMode.FastForward)
            return;

        if (IsCurrentStep(step => step.requireFastForwardControl))
        {
            Instance.tutorialFastForwardControlUsed = true;
            Instance.RefreshAdvanceGate();
            Instance.RefreshControlHighlights();
            return;
        }

        if (!Instance.tutorialPauseControlUsed || !Instance.tutorialPlayControlUsed)
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
    bool baselinePracticeRunStarted;
    float baselinePracticeElapsedSeconds;
    float nextBaselineCustomerAtSeconds;
    int baselineCustomersSpawned;
    int baselineCustomersServed;
    int baselineCustomersLeft;
    bool milestoneOneLessonActive;
    bool milestoneOneBottleneckLessonDone;
    bool milestoneOneAnalyticsLessonDone;
    bool tutorialStationHealthReviewed;
    GameObject lastStationHealthLessonTarget;
    bool milestoneOneStationClickObserved;
    int stationHealthLessonSelectionRevision;
    bool burgerArrivedAtPickup;
    bool BaselinePracticeRunComplete => baselineCustomersSpawned >= BaselineRunCustomerCount
        && baselineCustomersServed + baselineCustomersLeft >= BaselineRunCustomerCount;
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
    bool observingImprovedLayout;
    bool IsObservingPracticeRun => stepIndex == 2 || observingImprovedLayout;
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
    public bool BaselinePracticeRunStartedForSave => baselinePracticeRunStarted;
    public bool ObservingImprovedLayoutForSave => observingImprovedLayout;
    public int BaselineCustomersSpawnedForSave => baselineCustomersSpawned;
    public int BaselineCustomersServedForSave => baselineCustomersServed;
    public int BaselineCustomersLeftForSave => baselineCustomersLeft;
    public float BaselinePracticeElapsedSecondsForSave => baselinePracticeElapsedSeconds;
    public bool MilestoneOneBottleneckLessonDoneForSave => milestoneOneBottleneckLessonDone;
    public bool MilestoneOneAnalyticsLessonDoneForSave => milestoneOneAnalyticsLessonDone;
    int milestoneOneIntroProgress;
    bool IsMilestoneOneIntro => milestoneOneLessonActive && stepIndex >= MilestoneOneIntroStep
        && stepIndex <= MilestoneOneObjectivesStep;
    bool IsFirstDayRecipeReview => IsMilestoneOneIntro && stepIndex == MilestoneOneRecipesStep;
    bool IsFirstDayBuildLesson => IsMilestoneOneIntro && stepIndex == MilestoneOneBuildStep;
    int DeletePantryStage => IsFirstDayBuildLesson ? 3 : 2;
    bool BuildTutorialComplete => buildTutorialStage > DeletePantryStage;
    [System.Serializable]
    public sealed class PantryPracticeState
    {
        public int stage, paid;
        public bool purchased;
        public string stationId;
    }
    PantryPracticeState pendingPantryPractice;
    int buildTutorialPantryPaid;

    public PantryPracticeState CapturePantryPractice()
    {
        if (!IsFirstDayBuildLesson) return pendingPantryPractice;
        return new PantryPracticeState
        {
            stage = buildTutorialStage,
            paid = buildTutorialPantryPaid,
            purchased = buildTutorialPantryPurchaseMade,
            stationId = buildTutorialPantry != null
                ? StationNode.EnsureOn(buildTutorialPantry)?.StableStationId : null
        };
    }
    bool IsSupplyLesson => milestoneOneLessonActive && stepIndex == MilestoneThreeSupplyStep;
    bool supplyLessonDone;
    int supplyLessonPage;
    public bool SupplyLessonDoneForSave => supplyLessonDone;
    bool IsWorkerLesson => milestoneOneLessonActive
        && (stepIndex == HireWorkerStep || stepIndex == AssignWorkerStep);
    ProductionFlowPlan workerLessonFlow;
    bool workerLessonDone;
    bool workerLessonHireReviewed;
    public bool WorkerLessonDoneForSave => workerLessonDone;
    public bool WorkerLessonHireReviewedForSave => workerLessonHireReviewed;

    public int CaptureWorkerLessonFlowIndex(List<ProductionFlowPlan> flows)
    {
        if (workerLessonFlow == null || flows == null) return -1;
        int index = 0;
        foreach (ProductionFlowPlan flow in flows)
        {
            if (flow == workerLessonFlow) return index;
            if (flow != null) index++;
        }
        return -1;
    }

    bool firstDayFriesReviewed;
    bool firstDayShakeReviewed;
    public int MilestoneOneIntroProgressForSave => milestoneOneIntroProgress;

    public void RestoreCheckpoint(bool complete, int savedStep, bool savedOrderServed = false,
        bool savedPauseUsed = false, bool savedPlayUsed = false, bool savedFastForwardUsed = false,
        bool savedShiftStarted = false, int savedFastForwardRateMask = 0,
        int savedAnalyticsStage = -1, bool preserveKitchen = false,
        float savedBaselineRate = 0f, float savedTargetRate = 0f,
        bool savedStationMoved = false, bool savedMilestoneOneBottleneckLessonDone = false,
        bool savedMilestoneOneAnalyticsLessonDone = false,
        bool savedBaselinePracticeRunStarted = false, int savedBaselineCustomersSpawned = 0,
        int savedBaselineCustomersServed = 0, int savedBaselineCustomersLeft = 0,
        float savedBaselinePracticeElapsedSeconds = 0f, bool savedObservingImprovedLayout = false,
        int savedMilestoneOneIntroProgress = 0, bool savedSupplyLessonDone = false,
        PantryPracticeState savedPantryPractice = null, bool savedWorkerLessonDone = false,
        bool savedWorkerLessonHireReviewed = false, ProductionFlowPlan savedWorkerLessonFlow = null)
    {
        PlayerPrefs.SetInt(PrefsCompleteKey, complete ? 1 : 0);
        // Save restore replaces scene-authored stations with checkpoint objects.
        // Keep the tutorial's station references aligned with those replacements.
        starterKitchen.Clear();
        if (!complete) CacheStarterKitchen();
        stepIndex = complete ? 0 : Mathf.Clamp(savedStep, 0, Steps.Length - 1);
        // Older checkpoints used step 2 for planning, before the baseline shift
        // was added. They have not started the practice run yet, so resume at
        // the new planning step instead of entering the run without a shift.
        if (!complete && stepIndex == 2 && !savedShiftStarted)
            stepIndex = 1;
        observingImprovedLayout = !complete && Steps[stepIndex].requireOptimizedLayout
            && savedObservingImprovedLayout;
        baselinePracticeRunStarted = !complete && IsObservingPracticeRun && savedBaselinePracticeRunStarted;
        baselineCustomersSpawned = baselinePracticeRunStarted
            ? Mathf.Clamp(savedBaselineCustomersSpawned, 0, BaselineRunCustomerCount) : 0;
        baselineCustomersServed = baselinePracticeRunStarted
            ? Mathf.Clamp(savedBaselineCustomersServed, 0, baselineCustomersSpawned) : 0;
        baselineCustomersLeft = baselinePracticeRunStarted
            ? Mathf.Clamp(savedBaselineCustomersLeft, 0, baselineCustomersSpawned - baselineCustomersServed) : 0;
        baselinePracticeElapsedSeconds = baselinePracticeRunStarted
            ? Mathf.Max(0f, savedBaselinePracticeElapsedSeconds) : 0f;
        float baselineSpawnInterval = BaselineRunDurationSeconds / BaselineRunCustomerCount;
        nextBaselineCustomerAtSeconds = baselineSpawnInterval * (baselineCustomersSpawned + 1);
        tutorialShiftStarted = savedShiftStarted || stepIndex >= 2;
        if (!complete && tutorialShiftStarted && IsObservingPracticeRun)
            ShiftPlanningUI.Instance?.ResumeTutorialPracticeShift();
        glossaryTermClicked = false;
        objectivesScreenOpened = false;
        planScreenIntroduced = !complete && stepIndex >= 1;
        tutorialBurgerUnlockShown = true;
        preserveLoadedKitchen = preserveKitchen;
        inheritedKitchenPrepared = preserveKitchen;
        inheritedBaselineRate = savedBaselineRate;
        inheritedTargetRate = complete ? savedTargetRate : 5f;
        inheritedStationMoved = savedStationMoved;
        milestoneOneBottleneckLessonDone = savedMilestoneOneBottleneckLessonDone;
        milestoneOneAnalyticsLessonDone = savedMilestoneOneAnalyticsLessonDone;
        milestoneOneIntroProgress = Mathf.Clamp(savedMilestoneOneIntroProgress, 0, MilestoneOneObjectivesStep + 1);
        supplyLessonDone = savedSupplyLessonDone;
        workerLessonDone = savedWorkerLessonDone;
        workerLessonFlow = complete && !workerLessonDone ? savedWorkerLessonFlow : null;
        workerLessonHireReviewed = workerLessonFlow != null && savedWorkerLessonHireReviewed;
        pendingPantryPractice = savedPantryPractice;
        milestoneOneLessonActive = false;
        tutorialStationHealthReviewed = false;
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
        burgerArrivedAtPickup = stepIndex == 2 && (savedOrderServed || HasTutorialBurgerAtPickup());
        tutorialCustomerOrderServed = stepIndex == 2 && savedOrderServed;
        customerJourneyComplete = false;
        if (stepIndex == 2 && savedOrderServed)
        {
            CustomerAI[] restoredCustomers = FindObjectsByType<CustomerAI>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            customerJourneyComplete = restoredCustomers == null || restoredCustomers.Length == 0;
        }
        liveCustomerSpawned = baselinePracticeRunStarted && baselineCustomersSpawned > 0;
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
    int businessOverviewStage;
    int businessFlowOverviewStage;
    int businessTutorialFlowConnectionCount;
    ProductionFlowPlan businessTutorialFlow;
    bool businessOverviewStarted;
    int buildTutorialStage;
    bool buildTutorialPantryPurchaseMade;
    GameObject buildTutorialPantry;
    readonly HashSet<GameObject> buildTutorialStartingPantries = new HashSet<GameObject>();
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
    float nextFeatureLockRefresh;
    bool tutorialFeatureLocksApplied;
    readonly List<GameObject> starterKitchen = new List<GameObject>();

    GameObject canvasRoot;
    GameObject gusPanel;
    GameObject panel;
    TutorialPanelDragHandle panelDragHandle;
    TextMeshProUGUI gusDialogueText;
    TextMeshProUGUI titleText;
    TextMeshProUGUI bodyText;
    TextMeshProUGUI coreStationChecklistText;
    TextMeshProUGUI stepLabel;
    Button backButton;
    Button nextButton;
    TextMeshProUGUI nextLabel;
    LayoutElement nextButtonLayout;
    LayoutElement navigationLayout;
    string measuredNextLabel;
    float measuredNextLabelWidth;
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
        public bool requireFastForwardControl;
        public bool requireBusinessOverview;
        public bool requireProductionFlowOverview;
        public bool requireBuildOverview;

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
            bool requireStationHealthReview = false,
            bool requireBusinessOverview = false,
            bool requireProductionFlowOverview = false,
            bool requireBuildOverview = false,
            bool requireFastForwardControl = false)
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
            this.requireFastForwardControl = requireFastForwardControl;
            this.requireBusinessOverview = requireBusinessOverview;
            this.requireProductionFlowOverview = requireProductionFlowOverview;
            this.requireBuildOverview = requireBuildOverview;
        }
    }

    static readonly Step[] Steps =
    {
        new Step("Controls",
            "Move with <b>WASD</b>. Right-drag to rotate. Scroll to zoom.",
            "Next", Highlight.None, requireCameraControls: true),
        new Step("Run the current design",
            "Start a shift without changing the kitchen. We will observe the existing layout as a <b>baseline</b>.",
            "Next", Highlight.None, requirePlanIntroduction: true, requireShiftStarted: true),
        new Step("Observe the baseline",
            "Run the kitchen for three minutes. Fifteen customers will arrive. The baseline ends when all 15 visits are resolved, either served or lost to waiting.",
            "Improve Line", Highlight.None, liveCustomer: true, requirePickupDelivery: true,
            requireFastForwardControl: true),
        new Step("Improve the burger line",
            "Open <b>Kitchen → Stations</b> and move a station to improve the layout. Then test the new line with customers.",
            "Test Layout", Highlight.None, requireOptimizedLayout: true),
        new Step("Identify a bottleneck",
            "It is noon. Open <b>Kitchen</b>, choose <b>Workers</b>, and select a production station. Read its Working, Blocked, Starved, and Idle percentages to see where time is going.",
            "Got it", Highlight.None, requireStationHealthReview: true),
        new Step("Review Analytics",
            "Open <b>Analytics</b> to compare service, demand, and production.",
            "Finish", Highlight.None, pauseSim: true, requireAnalyticsScreen: true),
        new Step("Expand the menu",
            "Let's look at your <b>burger flow</b> before building the new lines.",
            "Show me", Highlight.None, pauseSim: true),
        new Step("Build a flow",
            "Follow the burger flow on the kitchen floor.",
            "Next", Highlight.None, pauseSim: true, openWorkers: true, requireProductionFlowOverview: true),
        new Step("Review fries and shakes",
            "Open <b>Recipes</b> to see the ingredients and stations for fries and shakes.",
            "Next", Highlight.None, pauseSim: true, requireRecipeScreen: true),
        new Step("Set up a workstation",
            "Buy a Pantry and practice choosing what it supplies.",
            "Next", Highlight.None, pauseSim: true, requireBuildOverview: true),
        new Step("Check your objectives",
            "Open <b>Objectives</b>. Complete the listed tasks in one shift to unlock the next milestone.",
            "Finish", Highlight.None, pauseSim: true, requireObjectivesScreen: true),
        new Step("Plan your supply",
            "Open <b>Kitchen → Supply</b> to see your ingredient stock.",
            "Next", Highlight.None, pauseSim: true),
        new Step("Hire a worker",
            "In <b>Workers</b>, click <b>Hire Worker</b>. If you already have a worker to use, click <b>Next</b>.",
            "Next", Highlight.None, pauseSim: true, openWorkers: true),
        new Step("Assign a worker",
            "Click <b>Assign</b> on a worker's card, or drag the card onto your new flow's tab.",
            "Finish", Highlight.None, pauseSim: true, openWorkers: true)
    };

    // Gus gives the player context and motivation. The moving instruction card
    // carries the mechanics and ISE explanations.
    static readonly string[] GusDialogue =
    {
        "All right, get used to moving around this joint. We got work to do.",
        "Let's run this kitchen as it is. I want to see what the line can do before we start moving things around.",
        "Let's see how this process handles 15 customers over three minutes before we start changing anything.",
        "Let's shorten the route between stations and see if we can get more burgers out.",
        "That line's slowing down around noon. Help me see where things are getting stuck.",
        "Analytics shows where the kitchen is keeping up and where we need more capacity.",
        "I stocked potatoes, fry containers, milk, and cups. Let's add fries and shakes.",
        "A flow tells workers where to pick up ingredients and where to deliver them.",
        "The recipe diagrams show what goes into each item and where it gets made.",
        "Let's use an extra Pantry to practice buying, setting an ingredient, and deleting a station.",
        "Check the tasks before you open, then set up the menu and kitchen to meet them.",
        "I've handled ingredient deliveries up to now. It's your turn to run Supply.",
        "Your flow is saved. Let's get someone to run it.",
        "Give a worker this flow so they know which stations to run."
    };

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
        ClearTutorialFeatureLocks();
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
        observingImprovedLayout = false;
        milestoneOneIntroProgress = 0;
        supplyLessonDone = false;
        workerLessonDone = false;
        workerLessonFlow = null;
        workerLessonHireReviewed = false;
        pendingPantryPractice = null;
        inheritedBaselineRate = 0f;
        inheritedTargetRate = 0f;
        furthestStepIndex = -1;
        flowEditCompleted = false;
        flowCreateCompleted = false;
        recipeBusinessOpened = false;
        liveCustomerSpawned = false;
        baselinePracticeRunStarted = false;
        baselinePracticeElapsedSeconds = 0f;
        nextBaselineCustomerAtSeconds = 0f;
        baselineCustomersSpawned = 0;
        baselineCustomersServed = 0;
        baselineCustomersLeft = 0;
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
        tutorialStationHealthReviewed = false;
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
        buildTutorialStage = 0;
        buildTutorialPantryPurchaseMade = false;
        buildTutorialPantry = null;
        buildTutorialStartingPantries.Clear();
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
        if (running && IsObservingPracticeRun && eventId == TutorialVoiceEventId.CustomerLeft)
        {
            baselineCustomersLeft = Mathf.Min(BaselineRunCustomerCount, baselineCustomersLeft + 1);
            RefreshAdvanceGate();
            return;
        }
        if (eventId != TutorialVoiceEventId.FirstOrderServed && eventId != TutorialVoiceEventId.OrderServed)
            return;

        if (running && IsObservingPracticeRun && eventId == TutorialVoiceEventId.OrderServed)
        {
            baselineCustomersServed = Mathf.Min(BaselineRunCustomerCount, baselineCustomersServed + 1);
            RefreshAdvanceGate();
        }

        // Keep this latched even if the event arrives during a step transition
        // or immediately before a save restore. Serving is a gameplay fact, not
        // a fact that should depend on the current tutorial panel timing.
        orderServed = true;
        if (!running) return;
        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireCustomerJourney)
        {
            tutorialCustomerOrderServed = true;
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
                if (gusDialogueText != null)
                    gusDialogueText.maxVisibleCharacters = tutorialVisibleCharacters;
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
                if (gusDialogueText != null)
                    gusDialogueText.maxVisibleCharacters = tutorialVisibleCharacters;
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
                if (gusDialogueText != null)
                    gusDialogueText.maxVisibleCharacters = tutorialVisibleCharacters;
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
                && Steps[stepIndex].requireBusinessOverview && !IsBusinessOverviewOpen())
            {
                Sfx.Play(SfxId.UiClick);
                MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
                if (tabs != null)
                    tabs.OpenBusinessPage();
                else
                {
                    ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(
                        FindObjectsInactive.Include);
                    if (management != null)
                    {
                        management.SetUnifiedPrimaryPage(true);
                        management.OpenWorkersTab();
                    }
                }
                nextControlHighlightRefresh = 0f;
                RefreshAdvanceGate();
                RefreshControlHighlights();
                UpdateContextualCallout();
                return;
            }
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
            if (gusDialogueText != null)
                gusDialogueText.maxVisibleCharacters = tutorialVisibleCharacters;
            return;
        }

        Sfx.Play(SfxId.UiClick);
        if (Steps[stepIndex].requireOptimizedLayout && !observingImprovedLayout)
        {
            BeginImprovedLayoutRun();
            return;
        }
        if (Steps[stepIndex].requireBuildOverview)
        {
            InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventory != null && inventory.IsPanelOpen) inventory.TogglePanel();
        }
        if (milestoneOneLessonActive && stepIndex == MilestoneTwoAnalyticsStep)
        {
            milestoneOneAnalyticsLessonDone = true;
            EndMilestoneOneLesson();
            GameSaveSlots.SaveActiveSlot();
            return;
        }

        if (IsWorkerLesson)
        {
            if (stepIndex == HireWorkerStep)
                ShowWorkerAssignmentStep();
            else
            {
                workerLessonDone = true;
                workerLessonFlow = null;
                EndMilestoneOneLesson();
                GameSaveSlots.SaveActiveSlot();
            }
            return;
        }

        if (IsSupplyLesson)
        {
            if (supplyLessonPage < SupplyTourLastPage)
            {
                supplyLessonPage++;
                nextControlHighlightRefresh = 0f;
                RefreshControlHighlights();
                return;
            }
            supplyLessonDone = true;
            EndMilestoneOneLesson();
            GameSaveSlots.SaveActiveSlot();
            return;
        }

        if (Steps[stepIndex].requireBusinessOverview)
        {
            if (businessOverviewStage <= 2 && !IsBusinessWorkersTabActive())
            {
                Sfx.Play(SfxId.UiError);
                RefreshAdvanceGate();
                return;
            }
            if (businessOverviewStage == 3 && !IsBusinessSupplyTabActive())
            {
                Sfx.Play(SfxId.UiError);
                RefreshAdvanceGate();
                return;
            }
            if (businessOverviewStage < 4)
            {
                businessOverviewStage++;
                SetBusinessOverviewInstruction();
                RefreshAdvanceGate();
                UpdateContextualCallout();
                return;
            }
        }
        if (Steps[stepIndex].requireProductionFlowOverview)
        {
            if (businessFlowOverviewStage < GetBusinessFlowTutorialFinalStage())
            {
                businessFlowOverviewStage++;
                SetProductionFlowInstruction();
                RefreshAdvanceGate();
                UpdateContextualCallout();
                return;
            }
            ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(
                FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.Close();
            WorkerAssignmentLinkVisuals.ClearTutorialFlowProgress();
        }

        if (IsMilestoneOneIntro)
        {
            milestoneOneIntroProgress = stepIndex + 1;
            if (stepIndex == MilestoneOneObjectivesStep)
            {
                EndMilestoneOneLesson();
                GameSaveSlots.SaveActiveSlot();
            }
            else
                ShowMilestoneOneIntroStep();
            return;
        }

        if (stepIndex >= OnboardingStepCount - 1)
        {
            Complete(skipped: false);
            return;
        }

        if (Steps[stepIndex].requirePickupDelivery && Steps[stepIndex].requireFastForwardControl)
        {
            // The baseline run is an isolated tutorial shift. Return to planning
            // before unlocking station controls so the player can improve the
            // original layout without ending or advancing a campaign day.
            CustomerAI.DespawnAllCustomers();
            ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
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
        if (step.requireStationHealthReview && !milestoneOneLessonActive)
        {
            tutorialStationHealthReviewed = false;
            milestoneOneStationClickObserved = false;
            ManagementModeController management = ManagementModeController.Instance;
            lastStationHealthLessonTarget = management != null
                ? management.TutorialSelectedStation : null;
            stationHealthLessonSelectionRevision = management != null
                ? management.TutorialStationSelectionRevision : 0;
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
        bool gusOnlyShiftStart = step.requireShiftStarted;
        bool hideTutorialCardForBaseline = stepIndex == 2;
        bool titleOnlyStep = stepIndex == 3;
        if (panel != null)
            panel.SetActive(!gusOnlyShiftStart && !hideTutorialCardForBaseline);
        bool cameraControlsOnly = step.requireCameraControls;
        if (stepLabel != null) stepLabel.gameObject.SetActive(!cameraControlsOnly && !titleOnlyStep);
        if (titleText != null) titleText.gameObject.SetActive(true);
        if (bodyText != null)
            bodyText.gameObject.SetActive(!cameraControlsOnly
                && (!titleOnlyStep || step.requireOptimizedLayout));
        if (panelDragHandle != null) panelDragHandle.gameObject.SetActive(!titleOnlyStep);
        if (gusPanel != null) gusPanel.SetActive(!cameraControlsOnly);
        SetFastForwardHelpVisible(step.requireTimeControlInteractions && running);
        if (cameraPromptPanel != null)
            cameraPromptPanel.SetActive(step.requireCameraControls);
        if (titleText != null)
            titleText.text = ISEVocabularyHighlighter.Highlight(step.title);
        string gusLine = stepIndex < GusDialogue.Length
            ? GusDialogue[stepIndex]
            : "All right, let's get this sorted out.";
        if (stepIndex == 2)
            gusLine = ISEVocabularyHighlighter.Highlight(gusLine);
        SetGusDialogueText(observingImprovedLayout
            ? "Let's see how the new layout handles the same customer rush."
            : gusLine);
        string initialBody = !string.IsNullOrEmpty(tutorialFocusText) ? tutorialFocusText : step.body;
        if (step.requireCameraControls)
            initialBody = string.Empty;
        else if (step.requireStationChecklist)
            initialBody = GetFreezerWalkthroughText(freezerWalkthroughPhase);
        else if (step.requireTimeControlInteractions)
            initialBody = GetTimeControlInstruction();
        else if (step.requireAnalyticsScreen)
            initialBody = step.body;
        SetTutorialBodyText(initialBody);
        if (step.requireBuildOverview)
        {
            BeginBuildTutorial();
            SetBuildTutorialInstruction();
        }
        if (step.requireBusinessOverview)
        {
            businessOverviewStarted = false;
            businessOverviewStage = 0;
        }
        if (step.requireProductionFlowOverview)
        {
            businessFlowOverviewStage = 0;
            ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.OpenWorkersTab();
            BeginBusinessFlowWalkthrough();
            SetProductionFlowInstruction();
        }
        else
            WorkerAssignmentLinkVisuals.ClearTutorialFlowProgress();
        if (step.requireCustomerJourney)
            customerJourneyMessageHoldUntil = 0f;
        if (coreStationChecklistText != null)
        {
            coreStationChecklistText.gameObject.SetActive(
                step.requireStationChecklist || step.requireTimeControlInteractions
                || step.requireOptimizedLayout && !titleOnlyStep);
            if (step.requireStationChecklist)
                RefreshStationChecklist();
            else if (step.requireTimeControlInteractions)
                RefreshTimeControlChecklist();
            else if (step.requireOptimizedLayout)
                RefreshInheritedLayoutText();
        }
        if (stepLabel != null)
            stepLabel.text = milestoneOneLessonActive
                ? IsWorkerLesson ? "WORKERS"
                    : IsSupplyLesson ? "MILESTONE 3"
                    : stepIndex == MilestoneTwoAnalyticsStep ? "MILESTONE 2" : "MILESTONE 1"
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

        if ((IsBaselinePracticeStep(step) || observingImprovedLayout) && !baselinePracticeRunStarted)
            BeginBaselinePracticeRun();
        else if (step.liveCustomer)
            SpawnPracticeCustomer();

        RebuildTutorialLayout();
        ResetTutorialTextReveal();
        RefreshCameraPromptStatus();
        RefreshAdvanceGate();
        RefreshTutorialFeatureLocks(true);
    }

    void RebuildTutorialLayout()
    {
        if (panel == null) return;
        if (titleText != null)
            titleText.ForceMeshUpdate();
        if (bodyText != null)
            bodyText.ForceMeshUpdate();
        if (gusDialogueText != null)
            gusDialogueText.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        if (gusPanel != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)gusPanel.transform);
        UpdateContextualCallout();
    }

    void AdvanceFromTutorialUnlock()
    {
        if (!running || stepIndex != 5 || !tutorialUnlockContinuePending) return;
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
        if (gusDialogueText == null) return;

        tutorialTextBeingRevealed = gusDialogueText.text;
        gusDialogueText.ForceMeshUpdate();
        tutorialTotalCharacters = gusDialogueText.textInfo.characterCount;
        tutorialVisibleCharacters = 0;
        tutorialCharacterProgress = 0f;
        nextTutorialVoiceTime = Time.unscaledTime;
        gusDialogueText.maxVisibleCharacters = 0;
    }

    void UpdateTutorialTextReveal()
    {
        if (!running || gusDialogueText == null || !gusDialogueText.gameObject.activeInHierarchy)
            return;

        if (tutorialTextBeingRevealed != gusDialogueText.text)
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
        gusDialogueText.maxVisibleCharacters = tutorialVisibleCharacters;
        if (Time.unscaledTime < nextTutorialVoiceTime)
            return;

        int characterIndex = Mathf.Min(tutorialVisibleCharacters - 1,
            gusDialogueText.textInfo.characterCount - 1);
        if (characterIndex < 0 || characterIndex >= gusDialogueText.textInfo.characterInfo.Length)
            return;

        char character = gusDialogueText.textInfo.characterInfo[characterIndex].character;
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
            bodyText.maxVisibleCharacters = int.MaxValue;
        }
    }

    void SetGusDialogueText(string text)
    {
        if (gusDialogueText == null) return;
        string dialogue = string.IsNullOrWhiteSpace(text)
            ? "Let's get to work."
            : text.Trim();
        if (gusDialogueText.text == dialogue) return;
        gusDialogueText.text = dialogue;
        ResetTutorialTextReveal();
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

            if (termStart >= 0)
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
        float desiredPanelWidth = IsFirstDayRecipeReview && IngredientsOrderUI.IsRecipeOverlayOpen ? 400f
            : recipePickerPanel != null ? 420f
            : objectivesPanelRect != null ? 460f
            : analyticsTutorial ? 440f : 560f;
        if (Mathf.Abs(panelRect.sizeDelta.x - desiredPanelWidth) > 1f)
        {
            panelRect.sizeDelta = new Vector2(desiredPanelWidth, panelRect.sizeDelta.y);
            FitTutorialButton();
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
            // Start the movable information card near the bottom. The overlap
            // resolver moves it above or beside Gus's fixed dialogue card.
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
        if (running && stepIndex == 5 && tutorialUnlockContinuePending)
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
        bool fixedImproveLineCard = running && Steps[stepIndex].requireOptimizedLayout;
        if (fixedImproveLineCard || milestoneOneLessonActive)
        {
            const float topBarGap = 26f;
            float cardY = rootHeight * 0.5f - 122f - panelHeight * 0.5f;
            MissionListUI objectives = MissionListUI.Instance != null
                ? MissionListUI.Instance
                : FindFirstObjectByType<MissionListUI>(FindObjectsInactive.Include);
            Button objectivesButton = objectives != null ? objectives.TutorialObjectivesButton : null;
            bool dockLeft = IsMilestoneOneIntro && objectivesTutorial != null;
            Button dockButton = dockLeft
                ? FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include)?.KitchenButton
                : objectivesButton;
            if (dockButton != null && TryGetRectInRoot(
                    dockButton.transform as RectTransform, rootRect, out Rect dockBounds))
                cardY = dockBounds.yMin - panelHeight * 0.5f - topBarGap;

            panelPosition = new Vector2(
                (dockLeft ? -1f : 1f) * (rootWidth * 0.5f - panelWidth * 0.5f - 20f),
                cardY);
        }
        PositionGusDialogue(rootRect, rootWidth, rootHeight);
        panelPosition = AvoidOpenPanelOverlap(rootRect, panelPosition, panelWidth,
            panelHeight, rootWidth, rootHeight);
        // Apply screen bounds after every contextual adjustment, including bottom-row targets.
        float calloutScreenMargin = running && stepIndex == 5 && tutorialUnlockContinuePending ? 10f : 18f;
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

        bool customTimeControlArrows = running && stepIndex >= 0 && stepIndex < Steps.Length
            && IsBaselinePracticeStep(Steps[stepIndex]) && !BaselinePracticeRunComplete;
        bool pointDownAtLayoutStation = fixedImproveLineCard && calloutTarget == null
            && highlightTarget != null;
        PositionCalloutArrow(targetPosition, targetBounds, hasTarget && !customTimeControlArrows,
            pointDownAtLayoutStation ? false : (bool?)null);
    }

    void PositionGusDialogue(RectTransform rootRect, float rootWidth, float rootHeight)
    {
        if (gusPanel == null || !gusPanel.activeInHierarchy) return;
        var rect = (RectTransform)gusPanel.transform;
        Vector2 halfSize = rect.rect.size * 0.5f;
        Vector2 preferred = new Vector2(0f, -rootHeight * 0.5f + halfSize.y + 24f);
        Vector2 center = AvoidOpenPanelOverlap(rootRect, preferred, rect.rect.width,
            rect.rect.height, rootWidth, rootHeight, includeGus: false);
        rect.anchoredPosition = center - preferred + new Vector2(0f, 24f);
    }

    Vector2 AvoidOpenPanelOverlap(RectTransform rootRect, Vector2 preferred, float panelWidth,
        float panelHeight, float rootWidth, float rootHeight, bool includeGus = true)
    {
        var blockers = new List<RectTransform>();
        InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventory != null && inventory.IsPanelOpen && inventory.panel != null)
            blockers.Add(inventory.panel.transform as RectTransform);
        ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(
            FindObjectsInactive.Include);
        if (management != null && management.IsOpen && management.managementPanel != null)
            blockers.Add(management.managementPanel.transform as RectTransform);
        ManagementModeController stationManagement = ManagementModeController.Instance;
        if (stationManagement != null && stationManagement.HasOpenStationPanel)
            blockers.Add(stationManagement.stationPopup.transform as RectTransform);
        if (ShiftPlanningUI.Instance != null && ShiftPlanningUI.IsPlanning
            && ShiftPlanningUI.Instance.TutorialPlanningCard != null)
            blockers.Add(ShiftPlanningUI.Instance.TutorialPlanningCard);
        if (includeGus && gusPanel != null && gusPanel.activeInHierarchy)
            blockers.Add(gusPanel.transform as RectTransform);
        MissionListUI missions = MissionListUI.Instance;
        if (missions != null && missions.IsOpen && missions.panelRoot != null)
            blockers.Add(missions.panelRoot.transform as RectTransform);

        var blockerRects = new List<Rect>();
        foreach (RectTransform blocker in blockers)
            if (blocker != null && blocker.gameObject.activeInHierarchy
                && TryGetRectInRoot(blocker, rootRect, out Rect rect))
            {
                float clearance = includeGus ? 0f : 28f;
                blockerRects.Add(Rect.MinMaxRect(rect.xMin - clearance, rect.yMin - clearance,
                    rect.xMax + clearance, rect.yMax + clearance));
            }
        if (blockerRects.Count == 0) return preferred;

        float halfW = panelWidth * 0.5f;
        float halfH = panelHeight * 0.5f;
        float margin = 18f;
        float gap = 16f;
        Vector2 Clamp(Vector2 p) => new Vector2(
            Mathf.Clamp(p.x, -rootWidth * 0.5f + halfW + margin, rootWidth * 0.5f - halfW - margin),
            Mathf.Clamp(p.y, -rootHeight * 0.5f + halfH + margin, rootHeight * 0.5f - halfH - margin));
        bool Overlaps(Vector2 p, Rect r) => p.x + halfW > r.xMin && p.x - halfW < r.xMax
            && p.y + halfH > r.yMin && p.y - halfH < r.yMax;

        Vector2 best = Clamp(preferred);
        float bestCost = float.MaxValue;
        var candidateX = new List<float> { best.x };
        var candidateY = new List<float> { best.y };
        foreach (Rect r in blockerRects)
        {
            candidateX.Add(r.xMin - halfW - gap);
            candidateX.Add(r.xMax + halfW + gap);
            candidateY.Add(r.yMin - halfH - gap);
            candidateY.Add(r.yMax + halfH + gap);
        }
        foreach (float x in candidateX)
        foreach (float y in candidateY)
        {
            Vector2 candidate = Clamp(new Vector2(x, y));
            bool collision = false;
            foreach (Rect r in blockerRects)
                if (Overlaps(candidate, r)) { collision = true; break; }
            if (collision) continue;
            float cost = (candidate - preferred).sqrMagnitude;
            if (cost < bestCost) { best = candidate; bestCost = cost; }
        }
        return best;
    }

    static bool TryGetRectInRoot(RectTransform target, RectTransform root, out Rect rect)
    {
        rect = default;
        if (target == null || root == null) return false;
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        Canvas canvas = target.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local))
                return false;
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
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
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect, projected, null, out localPoint))
                return false;

            if (running && stepIndex >= 0 && stepIndex < Steps.Length
                && Steps[stepIndex].requireOptimizedLayout)
            {
                Bounds stationBounds = GetTutorialStationBounds(highlightTarget);
                Vector3 boundsMin = stationBounds.min;
                Vector3 boundsMax = stationBounds.max;
                Vector2 screenMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 screenMax = new Vector2(float.MinValue, float.MinValue);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = new Vector3(
                        (corner & 1) == 0 ? boundsMin.x : boundsMax.x,
                        (corner & 2) == 0 ? boundsMin.y : boundsMax.y,
                        (corner & 4) == 0 ? boundsMin.z : boundsMax.z);
                    Vector3 screenCorner = Camera.main.WorldToScreenPoint(worldCorner);
                    if (screenCorner.z <= 0f) continue;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            rootRect, screenCorner, null, out Vector2 localCorner))
                        continue;
                    screenMin = Vector2.Min(screenMin, localCorner);
                    screenMax = Vector2.Max(screenMax, localCorner);
                }
                if (screenMin.x != float.MaxValue)
                {
                    localBounds = Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
                    localPoint = localBounds.center;
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    void PositionCalloutArrow(Vector2 targetPosition, Rect targetBounds, bool visible,
        bool? placeArrowBelowOverride = null)
    {
        if (calloutArrow == null) return;
        if (!visible)
        {
            calloutArrow.gameObject.SetActive(false);
            return;
        }

        RectTransform canvasRect = canvasRoot != null ? canvasRoot.transform as RectTransform : null;
        float canvasMiddleY = canvasRect != null ? canvasRect.rect.center.y : 0f;
        float spaceAbove = canvasRect != null ? canvasRect.rect.yMax - targetBounds.yMax : 0f;
        float spaceBelow = canvasRect != null ? targetBounds.yMin - canvasRect.rect.yMin : 0f;
        bool placeArrowBelow = placeArrowBelowOverride ?? (targetPosition.y > canvasMiddleY
            || Mathf.Approximately(targetPosition.y, canvasMiddleY) && spaceBelow >= spaceAbove);
        Vector2 direction = placeArrowBelow ? Vector2.up : Vector2.down;
        float targetEdgeDistance = 0f;
        if (targetBounds.width > 1f && targetBounds.height > 1f)
        {
            Vector2 targetHalfSize = targetBounds.size * 0.5f;
            float edgeX = Mathf.Abs(direction.x) > 0.001f
                ? targetHalfSize.x / Mathf.Abs(direction.x) : float.MaxValue;
            float edgeY = Mathf.Abs(direction.y) > 0.001f
                ? targetHalfSize.y / Mathf.Abs(direction.y) : float.MaxValue;
            targetEdgeDistance = Mathf.Min(edgeX, edgeY);
        }

        calloutArrow.gameObject.SetActive(true);
        calloutArrow.anchorMin = calloutArrow.anchorMax = calloutArrow.pivot = new Vector2(0.5f, 0.5f);
        const float pointerSize = 80f;
        const float pointerTipNormalizedOffset = 20f / 64f;
        const float pointerBaseAngle = 135f;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - pointerBaseAngle;
        Vector2 pointerTipOffset = new Vector2(-pointerSize * pointerTipNormalizedOffset,
            pointerSize * pointerTipNormalizedOffset);
        Vector3 rotatedTip = Quaternion.Euler(0f, 0f, angle)
            * new Vector3(pointerTipOffset.x, pointerTipOffset.y, 0f);
        Vector2 rotatedTipOffset = new Vector2(rotatedTip.x, rotatedTip.y);
        Vector2 tipPosition = targetPosition - direction * (targetEdgeDistance + 5f);
        float bounce = Mathf.Sin(Time.unscaledTime * 5f) * 5f;

        calloutArrow.sizeDelta = new Vector2(pointerSize, pointerSize);
        calloutArrow.anchoredPosition = tipPosition - rotatedTipOffset + direction * bounce;
        calloutArrow.localRotation = Quaternion.Euler(0f, 0f, angle);
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
            FitTutorialButton();
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
            && Steps[stepIndex].requireRecipeScreen
            && (!IsFirstDayRecipeReview || !IngredientsOrderUI.IsRecipeOverlayOpen);
        bool canOpenObjectives = !ready && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireObjectivesScreen && !objectivesScreenOpened;
        bool canOpenBusiness = !ready && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireBusinessOverview && !IsBusinessOverviewOpen();
        bool canOpenRequiredScreen = canOpenRecipes || canOpenObjectives || canOpenBusiness;
        nextButton.interactable = ready || canOpenRequiredScreen;
        var img = nextButton.GetComponent<Image>();
        if (img != null)
            img.color = ready || canOpenRequiredScreen
                ? new Color(0.22f, 0.55f, 0.38f, 1f)
                : new Color(0.22f, 0.24f, 0.28f, 1f);

        if (nextLabel != null && stepIndex >= 0 && stepIndex < Steps.Length)
        {
            var step = Steps[stepIndex];
            string labelText = ready
                ? observingImprovedLayout || (IsSupplyLesson && supplyLessonPage == SupplyTourLastPage)
                    ? "Finish" : freezerGuideNext ? "Next" : step.nextLabel
                : LockedLabel(step);
            nextLabel.text = labelText;
            FitTutorialButton();
            if (ready && step.requirePlaced)
                UpdateHighlight(step.highlight);
        }
    }

    string LockedLabel(Step step)
    {
        if (IsSupplyLesson) return "Open Supply";
        if (IsBaselinePracticeStep(step))
        {
            if (!tutorialShiftStarted) return "Start Shift";
            if (!BaselinePracticeRunComplete)
                return $"Resolve visits: {baselineCustomersServed + baselineCustomersLeft}/{BaselineRunCustomerCount} (served {baselineCustomersServed}, left {baselineCustomersLeft})";
            return "Baseline complete";
        }
        if (step.highlight == Highlight.Inventory) return "Open Build";
        if (step.requireGlossaryTermClick && !glossaryTermClicked)
            return "Click the highlighted ISE term";
        if (step.requireObjectivesScreen && !objectivesScreenOpened)
            return "Open Objectives";
        if (step.requireBusinessOverview && !IsBusinessOverviewOpen())
            return "Open Business";
        if (step.requireBuildOverview)
        {
            if (IsFirstDayBuildLesson && buildTutorialStage == 2) return "Set Potatoes";
            if (!IsBuildInventoryOpen()) return "Open Stations";
            if (buildTutorialStage <= 1) return "Buy and place a Pantry";
            if (buildTutorialStage == DeletePantryStage) return "Delete the practice Pantry";
        }
        if (step.requireProductionFlowOverview && !IsBusinessOverviewOpen())
            return "Open Business";
        if (step.requireBusinessOverview && businessOverviewStage <= 2 && !IsBusinessWorkersTabActive())
            return "Click Workers to continue";
        if (step.requireBusinessOverview && businessOverviewStage == 3)
            return "Click Supply to continue";
        if (step.requireProductionFlowOverview && ManagementModeController.Instance != null
            && ManagementModeController.Instance.IsCapturingFlow)
            return "Finish or cancel the flow edit";
        if (step.requireStationChecklist && !TutorialChecklistStationsPlaced())
            return "Place the seven stations";
        if (step.requireResearch) return "Research all three foundations";
        if (IsFirstDayRecipeReview && IngredientsOrderUI.IsRecipeOverlayOpen)
            return firstDayShakeReviewed ? "Close Recipes" : firstDayFriesReviewed ? "Review Shake" : "Review Fries";
        if (step.requireRecipeScreen) return "Open Recipes";
        if (step.requireAnalyticsScreen) return "Click Analytics";
        if (step.requireMenuScreen && !menuScreenOpened) return "Open Menu";
        if (step.requireHamburger) return "Add Hamburger to the menu";
        if (step.requireStationHealthReview && !milestoneOneLessonActive
            && !tutorialStationHealthReviewed)
            return "Review a production station";
        if (step.requireOptimizedLayout)
        {
            if (observingImprovedLayout)
                return $"Visits resolved: {baselineCustomersServed + baselineCustomersLeft}/{BaselineRunCustomerCount}";
            return "Move a station";
        }
        if (step.requireFastForwardControl)
        {
            if (!tutorialFastForwardControlUsed) return "Click Fast Forward";
            if (!burgerArrivedAtPickup) return "Fast-forward until burgers reach Pickup";
            return "Review the baseline, then continue";
        }
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
        if (IsFirstDayRecipeReview)
        {
            if (!firstDayFriesReviewed || !firstDayShakeReviewed || IngredientsOrderUI.IsRecipeOverlayOpen)
                return false;
        }
        else if (step.requireRecipeScreen && !recipeScreenOpened) return false;
        if (step.requireAnalyticsScreen
            && (!analyticsScreenOpened || !analyticsScreenClosed
                || analyticsTutorialStage < AnalyticsTourCloseStage)) return false;
        if (step.requireBusinessOverview && !IsBusinessOverviewOpen()) return false;
        if (IsSupplyLesson && !IsBusinessSupplyTabActive()) return false;
        if (step.requireBuildOverview && !BuildTutorialComplete) return false;
        if (step.requireProductionFlowOverview && !IsBusinessOverviewOpen()) return false;
        if (step.requireBusinessOverview && businessOverviewStage <= 2 && !IsBusinessWorkersTabActive()) return false;
        if (step.requireBusinessOverview && businessOverviewStage == 3 && !IsBusinessSupplyTabActive()) return false;
        if (step.requireProductionFlowOverview && ManagementModeController.Instance != null
            && ManagementModeController.Instance.IsCapturingFlow) return false;
        if (step.requireMenuScreen && !menuScreenOpened) return false;
        if (step.requireHamburger && !IsHamburgerEnabled()) return false;
        if (step.requireOptimizedLayout)
        {
            if (observingImprovedLayout)
                return BaselinePracticeRunComplete;
            if (!inheritedStationMoved) return false;
        }
        if (step.requireStationChecklist && !TutorialChecklistStationsPlaced()) return false;
        if (step.requireShiftStarted && !tutorialShiftStarted) return false;
        if (step.requireOrderServed && !orderServed) return false;
        if (step.requirePickupDelivery && !burgerArrivedAtPickup
            && !IsBaselinePracticeStep(step)) return false;
        if (IsBaselinePracticeStep(step) && !BaselinePracticeRunComplete) return false;
        if (step.requireCustomerJourney && !customerJourneyComplete) return false;
        if (step.requireStationHealthReview)
        {
            if (milestoneOneLessonActive)
            {
                if (!HasSelectedProductionStation()) return false;
            }
            else if (!tutorialStationHealthReviewed)
                return false;
        }
        if (step.requireFastForwardControl && !tutorialFastForwardControlUsed
            && !IsBaselinePracticeStep(step)) return false;
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
        return IsBaselinePracticeStep(step)
            || (!step.requireFastForwardControl
                && ((step.liveCustomer && !step.requireOrderServed) || step.requirePlaced
                    || step.requireStationChecklist || step.requireAllStations || step.requireFlow
                    || step.requireWorkerOnFlow || step.requireHiredWorker || step.requireEditedFlow
                    || step.requireRecipes || step.requireIngredients || step.requireResearch
                    || step.requireHamburger || step.requireShiftStarted || step.requirePickupDelivery
                    || step.requireCustomerJourney || step.requireOrderServed));
    }

    static bool IsBaselinePracticeStep(Step step)
    {
        return step.liveCustomer && step.requirePickupDelivery
            && step.requireFastForwardControl;
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
        bool targetReached = current + 0.05f >= inheritedTargetRate && inheritedTargetRate > 0f;
        string text = targetReached
            ? "<color=#A8FFD4><b>Layout covers demand</b></color>"
            : inheritedStationMoved
                ? "Keep improving the layout to cover demand."
                : "Move a station to improve the layout.";
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
            case 0: return "<b>Service Rate</b> shows the share of customers you served.";
            case 1: return "<b>Demand / Min</b> shows how many of each item customers are expected to order.";
            case 2: return "<b>Produced / Min</b> shows what reached Pickup. Compare it with demand to spot shortfalls.";
            case 3: return "A <b>bottleneck</b> limits the whole line's output. If production falls short, check which station is holding up the flow.";
            case 4: return "<b>Customers in Store</b> counts people still here. If they arrive faster than you serve them, the waiting line grows.";
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
        if (IsBaselinePracticeStep(step))
            waitingForServedOrder = false;
        if (!AutomaticallyAdvances(step) || waitingForServedOrder
            || tutorialVisibleCharacters < tutorialTotalCharacters || !StepRequirementMet())
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
        OnboardingTutorial tutorial = Instance;
        if (!wasEdit && flow != null && IsComplete && tutorial != null
            && !tutorial.workerLessonDone && tutorial.workerLessonFlow == null)
        {
            tutorial.workerLessonFlow = flow;
            tutorial.workerLessonHireReviewed = false;
        }

        if (!IsActive || flow == null || flow.stations == null || flow.stations.Count < 2)
            return;

        if (wasEdit)
            Instance.flowEditCompleted = true;
        else if (Instance.stepIndex >= 0 && Instance.stepIndex < Steps.Length
            && Steps[Instance.stepIndex].requireFlow && IsBurgerTutorialFlow(flow))
            Instance.flowCreateCompleted = true;
    }

    internal static void NotifyWorkerHired()
    {
        if (Instance != null && Instance.IsWorkerLesson && Instance.stepIndex == HireWorkerStep)
            Instance.ShowWorkerAssignmentStep();
    }

    void ShowWorkerAssignmentStep()
    {
        workerLessonHireReviewed = true;
        stepIndex = AssignWorkerStep;
        ShowStep();
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

    void BeginBaselinePracticeRun()
    {
        baselinePracticeRunStarted = true;
        baselinePracticeElapsedSeconds = 0f;
        nextBaselineCustomerAtSeconds = BaselineRunDurationSeconds / BaselineRunCustomerCount;
        baselineCustomersSpawned = 0;
        baselineCustomersServed = 0;
        baselineCustomersLeft = 0;
        liveCustomerSpawned = false;
        burgerArrivedAtPickup = false;
        CustomerAI.DespawnAllCustomers();
    }

    void BeginImprovedLayoutRun()
    {
        observingImprovedLayout = true;
        FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include)?.CloseKitchenWorkspace();
        BeginBaselinePracticeRun();
        GameTimeManager clock = GameTimeManager.Instance;
        clock?.BeginPlannedShift();
        clock?.SetSpeed(GameTimeManager.SpeedMode.Play);
        ShiftPlanningUI.Instance?.ResumeTutorialPracticeShift();
        ClearHighlight();
        ShowStep();
    }

    void UpdateBaselinePracticeRun()
    {
        if (!baselinePracticeRunStarted || !IsObservingPracticeRun || !tutorialShiftStarted
            || baselineCustomersSpawned >= BaselineRunCustomerCount)
            return;

        // Time.deltaTime follows the selected game speed, so fast-forward shortens
        // the real wait without changing the 3-minute simulated service window.
        baselinePracticeElapsedSeconds += Time.deltaTime;
        if (baselinePracticeElapsedSeconds < nextBaselineCustomerAtSeconds)
            return;

        CustomerSpawner spawner = CustomerSpawner.Instance != null
            ? CustomerSpawner.Instance : FindFirstObjectByType<CustomerSpawner>();
        if (spawner == null || !spawner.SpawnTutorialCustomer())
            return;

        baselineCustomersSpawned++;
        liveCustomerSpawned = true;
        float interval = BaselineRunDurationSeconds / BaselineRunCustomerCount;
        nextBaselineCustomerAtSeconds = interval * (baselineCustomersSpawned + 1);
    }

    void BeginTutorialCustomerFollow(CustomerAI customer)
    {
        if (customer == null) return;
        tutorialCustomer = customer;
        customer.patienceDuration = CustomerAI.DefaultPatienceDurationSeconds;
        customerJourneyStage = -1;
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

        if (!liveCustomerSpawned || customerJourneyComplete) return;

        bool customerLeft = tutorialCustomer == null || tutorialCustomer.IsLeaving;
        if (!customerLeft) return;

        ClearTutorialCustomerFollow();
        tutorialCustomer = null;
        if (tutorialCustomerOrderServed)
        {
            customerJourneyComplete = true;
            ShiftPlanningUI.ReturnToPlanPhaseAfterTutorial();
            RefreshAdvanceGate();
            return;
        }

        // If a practice customer leaves without completing the order, try one
        // more customer while keeping the same single explanatory card visible.
        liveCustomerSpawned = false;
        customerJourneyStage = -1;
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
        observingImprovedLayout = false;
        running = false;
        pendingStart = false;
        currentHighlight = Highlight.None;
        WorkerAssignmentLinkVisuals.ClearTutorialFlowProgress();
        GameTimeManager.Instance?.ReleaseExternalPause(CustomerJourneyPauseSource);
        PlayerPrefs.SetInt(PrefsCompleteKey, 1);
        PlayerPrefs.Save();
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.TutorialCompleted);

        HideUI();
        ClearHighlight();

        if (skipped)
            RestoreStarterKitchen();

        var milestones = MilestoneProgressManager.Instance;
        bool onboardingMilestoneCompleted = false;
        if (milestones != null)
        {
            string activeMilestoneBefore = milestones.GetActiveMilestone() != null
                ? milestones.GetActiveMilestone().milestoneId : null;
            milestones.NotifyOnboardingFinished();
            MilestoneDefinition activeMilestoneAfter = milestones.GetActiveMilestone();
            onboardingMilestoneCompleted = !string.IsNullOrEmpty(activeMilestoneBefore)
                && (activeMilestoneAfter == null
                    || activeMilestoneAfter.milestoneId != activeMilestoneBefore);
        }

        if (skipped)
            Sfx.Play(SfxId.UiClose);
        else if (!onboardingMilestoneCompleted)
            Sfx.Play(SfxId.MissionComplete);
        var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventoryUI != null) inventoryUI.RefreshAll();

        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null)
            placer.EnsureCustomerEntrance();

        // The tutorial customer is practice only. Begin the real campaign day at
        // opening time and show its Plan card before normal arrivals can start.
        CustomerAI.DespawnAllCustomers();
        ProductionManager.ClearShiftProduction();
        StoreStatisticsManager.Instance?.BeginDay();

        // The tutorial finishes from Analytics while the Business panel is still
        // open underneath it. Close both management panels before showing Plan so
        // their full-height backdrop cannot cover the Kitchen navigation.
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

        RefreshTutorialFeatureLocks(true);
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
        inheritedTargetRate = 5f;
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
        SetHighlightTarget(target, kind == Highlight.Inventory || kind == Highlight.Management);
    }

    void SetHighlightTarget(Transform target, bool ui = false)
    {
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

        bool suppressLayoutWorldPulse = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireOptimizedLayout && !(target is RectTransform);
        highlight.SetActive(!suppressLayoutWorldPulse);
        highlight.transform.position = target.position + (ui ? Vector3.zero : Vector3.up * 2.2f);
        highlight.transform.localScale = Vector3.one * (ui ? 0.01f : 0.55f);
        if (ui) highlight.SetActive(false);
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

    void HighlightHudButton(string buttonName, int? tabForNextOpen = null)
    {
        if (string.IsNullOrEmpty(buttonName)) return;
        if (buttonName == "BusinessTab" || buttonName == "ManagementButton"
            || buttonName == "InventoryTab")
        {
            MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            if (tabs != null && tabs.KitchenButton != null)
            {
                int index = buttonName == "InventoryTab" ? 0 : 1;
                Button workspaceButton = tabs.WorkspaceTabButton(index);
                if (workspaceButton != null && workspaceButton.gameObject.activeInHierarchy)
                {
                    FocusTutorialControl(workspaceButton);
                    return;
                }
                tabs.PrepareWorkspaceTabForNextOpen(tabForNextOpen ?? index);
                FocusTutorialControl(tabs.KitchenButton);
                return;
            }
        }
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (button != null && button.name == buttonName && button.gameObject.activeInHierarchy)
            {
                FocusTutorialControl(button);
                return;
            }
        }
    }

    static bool IsBusinessOverviewOpen()
    {
        ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(
            FindObjectsInactive.Include);
        return management != null && management.IsOpen;
    }

    static bool IsBuildInventoryOpen()
    {
        InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        return inventory != null && inventory.IsPanelOpen;
    }

    void BeginBuildTutorial()
    {
        buildTutorialStage = 0;
        buildTutorialPantryPurchaseMade = false;
        buildTutorialPantry = null;
        buildTutorialPantryPaid = 0;
        buildTutorialStartingPantries.Clear();
        foreach (PantryStation pantry in FindObjectsByType<PantryStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pantry == null) continue;
            PlacedBuildItem placed = pantry.GetComponentInParent<PlacedBuildItem>();
            if (placed != null) buildTutorialStartingPantries.Add(placed.gameObject);
        }

        if (!IsFirstDayBuildLesson) return;
        if (pendingPantryPractice == null)
        {
            ItemDefinition pantryItem = FindBuildTutorialPantryItem();
            InventoryManager inventory = FindFirstObjectByType<InventoryManager>();
            buildTutorialPantryPurchaseMade = pantryItem != null && inventory != null && inventory.GetCount(pantryItem) > 0;
            return;
        }
        PantryPracticeState saved = pendingPantryPractice;
        pendingPantryPractice = null;
        buildTutorialStage = Mathf.Clamp(saved.stage, 0, DeletePantryStage + 1);
        buildTutorialPantryPurchaseMade = saved.purchased;
        buildTutorialPantryPaid = Mathf.Max(0, saved.paid);
        if (!string.IsNullOrEmpty(saved.stationId))
            foreach (GameObject pantry in buildTutorialStartingPantries)
                if (pantry.GetComponent<StationNode>()?.StableStationId == saved.stationId)
                {
                    buildTutorialPantry = pantry;
                    break;
                }
        if (buildTutorialStage >= 2 && !BuildTutorialComplete && buildTutorialPantry == null)
        {
            buildTutorialStage = 1;
            buildTutorialPantryPurchaseMade = false;
        }
    }

    void RefreshBuildTutorialProgress()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length || !Steps[stepIndex].requireBuildOverview)
            return;
        if (buildTutorialStage == 0 && IsBuildInventoryOpen())
        {
            buildTutorialStage = 1;
            currentHighlight = Highlight.Pantry;
            FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include)?.RefreshAll();
            SetBuildTutorialInstruction();
        }
        if (buildTutorialStage == 1 && buildTutorialPantryPurchaseMade)
        {
            buildTutorialPantry = FindNewTutorialPantry();
            if (buildTutorialPantry != null)
            {
                buildTutorialStage = 2;
                currentHighlight = Highlight.None;
                FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include)?.RefreshAll();
                SetBuildTutorialInstruction();
            }
        }
        if (IsFirstDayBuildLesson && buildTutorialStage >= 2 && !BuildTutorialComplete)
        {
            if (buildTutorialPantry == null)
            {
                buildTutorialStage = 1;
                buildTutorialPantryPurchaseMade = false;
                SetBuildTutorialInstruction();
                return;
            }
            ItemDefinition potatoes = ProductionManager.Instance?.orderConfig?.friesIngredient;
            PantryStation pantry = buildTutorialPantry.GetComponentInChildren<PantryStation>();
            if (buildTutorialStage == 2 && potatoes != null && pantry != null && pantry.CanDispense(potatoes))
            {
                buildTutorialStage = DeletePantryStage;
                SetBuildTutorialInstruction();
            }
        }
    }

    GameObject FindNewTutorialPantry()
    {
        foreach (PantryStation pantry in FindObjectsByType<PantryStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pantry == null) continue;
            PlacedBuildItem placed = pantry.GetComponentInParent<PlacedBuildItem>();
            if (placed == null || buildTutorialStartingPantries.Contains(placed.gameObject)) continue;
            return placed.gameObject;
        }
        return null;
    }

    void SetBuildTutorialInstruction()
    {
        string text = buildTutorialStage switch
        {
            0 => "Open <b>Kitchen</b>, choose <b>Stations</b>, and find the Pantry.",
            1 => buildTutorialPantryPurchaseMade
                ? "Place the Pantry on a clear floor tile. Use <b>Place</b> on its card if you closed the preview."
                : "Choose <b>Buy</b> on the Pantry card, then click a clear floor tile.",
            2 when IsFirstDayBuildLesson => "Open <b>Workers</b> and select the new Pantry. Click its ingredient box and choose <b>Potatoes</b>.",
            _ when BuildTutorialComplete => "Practice complete. Now you can set up your own stations.",
            _ => "Switch to <b>Stations</b>, select the practice Pantry, and click <b>Delete</b> to refund it."
        };
        SetTutorialBodyText(text);
        RefreshAdvanceGate();
        UpdateContextualCallout();
    }

    InventoryItemCardUI FindBuildTutorialPantryCard()
    {
        ItemDefinition pantryItem = FindBuildTutorialPantryItem();
        if (pantryItem == null) return null;
        foreach (InventoryItemCardUI card in FindObjectsByType<InventoryItemCardUI>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (card != null && card.gameObject.activeInHierarchy && card.BoundItem == pantryItem)
                return card;
        return null;
    }

    void RefreshBusinessTutorialProgress()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        if (Steps[stepIndex].requireBusinessOverview && IsBusinessOverviewOpen())
        {
            if (!businessOverviewStarted)
            {
                businessOverviewStarted = true;
                businessOverviewStage = 0;
                SetBusinessOverviewInstruction();
            }
            if (businessOverviewStage == 3 && IsBusinessSupplyTabActive())
            {
                businessOverviewStage = 4;
                SetBusinessOverviewInstruction();
            }
        }
    }

    void SetBusinessOverviewInstruction()
    {
        string[] pages =
        {
            "<b>Workers</b> is the staff view. It lists your employees and lets you hire and assign them. Start by checking the flow and workers sections.",
            "<b>Production Flows</b> are saved station routes. The flow tabs select which route you are viewing; the selected flow determines the steps workers can follow.",
            "Worker cards show each employee's role, pay, and assignment. Assign qualified workers to a flow so they can work its stations.",
            "Now click the highlighted <b>Supply</b> tab to review stock and ingredient planning.",
            "<b>Supply</b> shows ingredient stock, demand forecasts, and ordering controls. Use it to plan deliveries so production has the ingredients it needs."
        };
        businessOverviewStage = Mathf.Clamp(businessOverviewStage, 0, pages.Length - 1);
        SetTutorialBodyText(pages[businessOverviewStage]);
    }

    void SetProductionFlowInstruction()
    {
        if (businessTutorialFlow == null)
        {
            SetTutorialBodyText(businessFlowOverviewStage == 0
                ? "The burger route has no links to show. Click <b>Next</b> to see where flows are created."
                : "<b>+ New Flow</b> in <b>Workers</b> is where you can experiment later. Click <b>Next</b> to continue.");
            return;
        }

        businessTutorialFlow.EnsureLegacyConnections();
        businessTutorialFlowConnectionCount = businessTutorialFlow.connections != null
            ? businessTutorialFlow.connections.Count : 0;
        businessFlowOverviewStage = Mathf.Clamp(businessFlowOverviewStage, 0,
            GetBusinessFlowTutorialFinalStage());
        int revealedConnections = Mathf.Clamp(businessFlowOverviewStage, 0,
            businessTutorialFlowConnectionCount);
        WorkerAssignmentLinkVisuals.ShowTutorialFlowProgress(businessTutorialFlow, revealedConnections);

        if (businessFlowOverviewStage == 0)
        {
            GameObject startStation = businessTutorialFlow.stations != null
                ? businessTutorialFlow.stations.Find(station => station != null) : null;
            SetTutorialBodyText("Look for <b>" + TutorialFlowStationName(startStation)
                + "</b>, marked START on the floor. Click <b>Next</b> to reveal the first connection.");
            return;
        }

        if (businessFlowOverviewStage <= businessTutorialFlowConnectionCount)
        {
            ProductionFlowConnection connection = businessTutorialFlow.connections[businessFlowOverviewStage - 1];
            string from = TutorialFlowStationName(connection != null ? connection.from : null);
            string to = TutorialFlowStationName(connection != null ? connection.to : null);
            SetTutorialBodyText("<b>Connection " + businessFlowOverviewStage + " of "
                + businessTutorialFlowConnectionCount + ":</b> The worker carries items from <b>" + from
                + "</b> to <b>" + to + "</b>. Follow the arrow on the floor.");
            return;
        }

        SetTutorialBodyText("The full route is now visible. Use <b>Workers → + New Flow</b> when you're ready to experiment. Click <b>Next</b> to continue.");
    }

    void BeginBusinessFlowWalkthrough()
    {
        ProductionManager production = ProductionManager.Instance;
        businessTutorialFlow = production != null
            ? production.productionFlows.Find(flow => flow != null && IsBurgerTutorialFlow(flow))
            : null;
        if (businessTutorialFlow == null)
        {
            var example = new ProductionFlowPlan { flowName = "Burger example", graphInitialized = true };
            for (int i = 0; i < 4; i++)
            {
                var link = GetExpectedFlowLink(example);
                if (link.source == null || link.destination == null) break;
                example.AddConnection(link.source.gameObject, link.destination.gameObject);
            }
            if (example.connections.Count > 0)
                businessTutorialFlow = example;
        }
        if (businessTutorialFlow != null)
        {
            businessTutorialFlow.EnsureLegacyConnections();
            businessTutorialFlowConnectionCount = businessTutorialFlow.connections != null
                ? businessTutorialFlow.connections.Count : 0;
        }
        else
            businessTutorialFlowConnectionCount = 0;
    }

    int GetBusinessFlowTutorialFinalStage()
    {
        return businessTutorialFlowConnectionCount + 1;
    }

    static string TutorialFlowStationName(GameObject station)
    {
        if (station == null) return "the first station";
        StationNode node = StationNode.EnsureOn(station);
        return node != null && !string.IsNullOrEmpty(node.DisplayName)
            ? node.DisplayName : station.name.Replace("(Clone)", string.Empty).Trim();
    }

    void HighlightBusinessTab(ManagementScreenController management, string buttonName)
    {
        MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
        if (tabs != null)
        {
            int workspaceIndex = buttonName == "Tab_Workers" ? 1
                : buttonName == "Tab_Supplies" ? 2 : -1;
            Button workspaceButton = tabs.WorkspaceTabButton(workspaceIndex);
            if (workspaceButton != null && workspaceButton.gameObject.activeInHierarchy)
            {
                FocusTutorialControl(workspaceButton);
                return;
            }
        }
        if (management?.tabButtons == null) return;
        foreach (Button button in management.tabButtons)
            if (button != null && button.name == buttonName && button.gameObject.activeInHierarchy)
            {
                FocusTutorialControl(button);
                return;
            }
    }

    static bool IsBusinessSupplyTabActive()
    {
        ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (management == null || !management.IsOpen || management.tabPanels == null) return false;
        foreach (GameObject tab in management.tabPanels)
            if (tab != null && tab.activeSelf
                && tab.GetComponentInChildren<SupplyPlanningScreenUI>(true) != null) return true;
        return false;
    }

    static bool IsBusinessWorkersTabActive()
    {
        ManagementScreenController management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (management == null || !management.IsOpen || management.tabPanels == null) return false;
        foreach (GameObject tab in management.tabPanels)
            if (tab != null && tab.activeSelf
                && tab.GetComponentInChildren<WorkersUI>(true) != null) return true;
        return false;
    }

    void RefreshLayoutTutorialHighlight()
    {
        if (observingImprovedLayout)
        {
            ClearHighlight();
            SetTutorialBodyText(BaselinePracticeRunComplete
                ? $"New layout: {baselineCustomersServed} served, {baselineCustomersLeft} left. Click <b>Finish</b> to continue."
                : $"Watch the new layout serve the same {BaselineRunCustomerCount}-customer rush.\n"
                    + $"Served: {baselineCustomersServed} · Left: {baselineCustomersLeft}");
            return;
        }

        InventoryUI inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventory == null || !inventory.IsPanelOpen)
        {
            ClearHighlight();
            HighlightHudButton("InventoryTab", tabForNextOpen: 1);
            MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            Button stationsTab = tabs != null ? tabs.WorkspaceTabButton(0) : null;
            SetLayoutTutorialText(stationsTab != null && stationsTab.gameObject.activeInHierarchy
                ? "Choose <b>Stations</b> to move a station."
                : "Open <b>Kitchen</b> to find the <b>Stations</b> tab.");
            return;
        }

        BuildPlacer placer = FindFirstObjectByType<BuildPlacer>(FindObjectsInactive.Include);
        if (placer == null)
        {
            SetLayoutTutorialText("Select a station, click <b>Move</b>, then place it on an open tile.");
            return;
        }

        GameObject moveTarget = FindLayoutTutorialMoveTarget();
        bool targetSelected = false;
        for (int i = 0; moveTarget != null && i < placer.SelectedObjects.Count; i++)
            if (placer.SelectedObjects[i] == moveTarget)
            {
                targetSelected = true;
                break;
            }
        BuildStationActionBar actionBar = FindFirstObjectByType<BuildStationActionBar>(
            FindObjectsInactive.Include);

        if (!inheritedStationMoved && placer.IsDragging)
        {
            SetLayoutTutorialText("Left-click to place. Right-drag to rotate the camera. Esc cancels the move.");
            ClearHighlight();
            if (actionBar != null && actionBar.PlacementInstructionTarget != null)
                FocusTutorialControl(actionBar.PlacementInstructionTarget);
            return;
        }

        if (!inheritedStationMoved && targetSelected)
        {
            SetLayoutTutorialText("Click <b>Move</b>, then choose an open tile.");
            ClearHighlight();
            if (actionBar != null && actionBar.MoveButton != null)
                FocusTutorialControl(actionBar.MoveButton);
            return;
        }

        if (!inheritedStationMoved)
        {
            ClearHighlight();
            Transform stationTarget = moveTarget != null ? moveTarget.transform : null;
            SetLayoutTutorialText(stationTarget != null
                ? "Select the highlighted station, then click <b>Move</b>."
                : "Select a station and click <b>Move</b>.");
            if (stationTarget != null)
            {
                Highlight kind = moveTarget.GetComponentInChildren<AssemblyStation>(true) != null
                    ? Highlight.Assembly : Highlight.Grill;
                UpdateHighlight(kind);
                calloutTarget = null;
            }
            return;
        }

        ClearHighlight();
        SetTutorialBodyText($"Forecast: {CurrentInheritedOutputRate():0.0} burgers/min. Demand: {inheritedTargetRate:0.#}/min.\n"
            + "Keep adjusting or click <b>Test Layout</b> to watch the new line.");
    }

    void RefreshFirstDayRecipeReview()
    {
        ClearHighlight();
        if (!IngredientsOrderUI.IsRecipeOverlayOpen)
        {
            if (firstDayShakeReviewed)
                SetTutorialBodyText("Build the two routes in <b>Workers</b>. Use <b>Shift Plan → Edit Menu</b> when you're ready to sell them.");
            else
            {
                SetTutorialBodyText("Open <b>Recipes</b> to see the ingredients and stations.");
                HighlightHudButton("RecipesButton");
            }
            return;
        }

        IngredientsOrderUI recipes = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
        if (recipes == null) return;
        if (firstDayShakeReviewed)
        {
            SetTutorialBodyText("Close <b>Recipes</b> to return to the kitchen.");
            FocusTutorialControl(recipes.TutorialRecipeCloseButton);
            return;
        }

        CustomerOrderConfig menu = ProductionManager.Instance?.orderConfig;
        int tab = firstDayFriesReviewed ? 2 : 1;
        string name = firstDayFriesReviewed ? "Shake" : "Fries";
        if (recipes.TutorialActiveRecipeTab != tab)
        {
            SetTutorialBodyText(firstDayFriesReviewed ? "Select the <b>Shakes</b> tab." : "Select the <b>Fries</b> tab.");
            FocusTutorialControl(recipes.GetTutorialRecipeTab(tab));
        }
        else
        {
            SetTutorialBodyText("Open <b>" + name + "</b> to see its production flow.");
            FocusTutorialControl(recipes.GetTutorialRecipeHeader(firstDayFriesReviewed ? menu?.drinkItem : menu?.friesItem));
        }
    }

    void SetLayoutTutorialText(string action)
    {
        const string layoutGoal = "Place stations closer to increase output.";
        SetTutorialBodyText(action + "\n\n" + layoutGoal);
    }

    GameObject FindLayoutTutorialMoveTarget()
    {
        Component station = FindTutorialPlacedStation<AssemblyStation>();
        PlacedBuildItem placed = station != null ? station.GetComponentInParent<PlacedBuildItem>() : null;
        if (placed != null) return placed.gameObject;

        station = FindTutorialPlacedStation<GrillStation>();
        placed = station != null ? station.GetComponentInParent<PlacedBuildItem>() : null;
        return placed != null ? placed.gameObject : null;
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
                return Steps.Length > 2 ? Steps[2].body : "Review the production plan.";
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

        if (stepIndex == 1 && nextButton != null)
            nextButton.gameObject.SetActive(phase == FreezerWalkthroughPhase.StationName
                || phase == FreezerWalkthroughPhase.ModelPreview
                || phase == FreezerWalkthroughPhase.OwnedCount);

        SetTutorialBodyText(GetFreezerWalkthroughText(phase));
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
        if (!running || stepIndex != 1
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
        if (IsWorkerLesson)
        {
            RefreshWorkerLesson();
            return;
        }
        if (IsSupplyLesson)
        {
            RefreshSupplyLesson();
            return;
        }
        if (IsFirstDayRecipeReview)
        {
            RefreshFirstDayRecipeReview();
            return;
        }

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

        if (step.requireBuildOverview)
        {
            if (BuildTutorialComplete) return;
            if (IsFirstDayBuildLesson && buildTutorialStage == 2)
            {
                if (!IsBusinessWorkersTabActive())
                {
                    HighlightHudButton("BusinessTab");
                    return;
                }
                ManagementModeController manager = ManagementModeController.Instance;
                if (manager != null && manager.TutorialSelectedStation == buildTutorialPantry)
                {
                    ItemDefinition potatoes = ProductionManager.Instance?.orderConfig?.friesIngredient;
                    FocusTutorialControl(manager.TutorialRecipePickerOpen
                        ? manager.TutorialRecipeChoice(potatoes?.itemName)
                        : manager.TutorialOutputSelectButton);
                }
                else if (buildTutorialPantry != null)
                    SetHighlightTarget(buildTutorialPantry.transform);
                return;
            }
            if (!IsBuildInventoryOpen())
            {
                HighlightHudButton("InventoryTab");
                return;
            }
            if (buildTutorialStage == 1)
            {
                InventoryItemCardUI pantryCard = FindBuildTutorialPantryCard();
                if (pantryCard != null)
                    FocusTutorialControl(pantryCard.buyButton);
                return;
            }
            if (buildTutorialStage == DeletePantryStage && buildTutorialPantry != null)
            {
                BuildPlacer placer = FindFirstObjectByType<BuildPlacer>(FindObjectsInactive.Include);
                bool selected = placer != null && placer.SelectedObjects.Count == 1
                    && placer.SelectedObjects[0] == buildTutorialPantry;
                if (selected)
                {
                    highlightTarget = null;
                    if (highlight != null) highlight.SetActive(false);
                    Button delete = IsFirstDayBuildLesson
                        ? FindFirstObjectByType<PostTutorialStationActionMenu>(FindObjectsInactive.Include)?.DeleteButton
                        : FindFirstObjectByType<BuildStationActionBar>(FindObjectsInactive.Include)?.DeleteButton;
                    FocusTutorialControl(delete);
                }
                else
                    SetHighlightTarget(buildTutorialPantry.transform);
                return;
            }
            return;
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

        if (step.requireStationHealthReview && !milestoneOneLessonActive
            && !tutorialStationHealthReviewed)
        {
            ManagementModeController stationManagement = ManagementModeController.Instance;
            ManagementScreenController managementScreen = FindFirstObjectByType<ManagementScreenController>(
                FindObjectsInactive.Include);
            if (stationManagement == null || managementScreen == null || !managementScreen.IsOpen)
                HighlightHudButton("BusinessTab");
            else if (HasSelectedProductionStation())
            {
                GameObject selectedStation = stationManagement.TutorialSelectedStation;
                if (selectedStation != null)
                    SetHighlightTarget(selectedStation.transform);
            }
            else
            {
                Component station = StarterStation<AssemblyStation>();
                if (station == null) station = StarterStation<GrillStation>();
                if (station != null)
                {
                    SetHighlightTarget(station.transform);
                }
            }
            return;
        }

        if (step.requireStationHealthReview && milestoneOneLessonActive)
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

        if (step.requireOptimizedLayout)
        {
            RefreshLayoutTutorialHighlight();
            return;
        }

        if (step.requireBusinessOverview)
        {
            if (!IsBusinessOverviewOpen())
            {
                HighlightHudButton("BusinessTab");
                return;
            }

            ManagementScreenController businessManagement = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (!IsBusinessWorkersTabActive()) HighlightBusinessTab(businessManagement, "Tab_Workers");
            else if (businessOverviewStage == 0) HighlightBusinessTab(businessManagement, "Tab_Workers");
            else if (businessOverviewStage == 1 || businessOverviewStage == 2)
            {
                WorkersUI businessWorkers = FindFirstObjectByType<WorkersUI>(FindObjectsInactive.Exclude);
                Component target = businessOverviewStage == 1
                    ? businessWorkers?.TutorialFlowTabsTarget : businessWorkers?.TutorialWorkerCardsTarget;
                if (target != null) FocusTutorialControl(target);
            }
            else if (businessOverviewStage == 3) HighlightBusinessTab(businessManagement, "Tab_Supplies");
            else
            {
                SupplyPlanningScreenUI supply = FindFirstObjectByType<SupplyPlanningScreenUI>(FindObjectsInactive.Exclude);
                if (supply != null) FocusTutorialControl(supply);
            }
            return;
        }

        if (step.requireProductionFlowOverview)
        {
            if (!IsBusinessOverviewOpen())
            {
                HighlightHudButton("BusinessTab");
                return;
            }
            WorkersUI flowWorkers = FindFirstObjectByType<WorkersUI>(FindObjectsInactive.Exclude);
            Component target = flowWorkers != null && businessFlowOverviewStage == GetBusinessFlowTutorialFinalStage()
                ? flowWorkers.TutorialCreateFlowTarget : null;
            if (target != null) FocusTutorialControl(target);
            ManagementModeController capture = ManagementModeController.Instance;
            if (capture != null && capture.IsCapturingFlow)
            {
                if (capture.flowCaptureCancelButton != null) FocusTutorialControl(capture.flowCaptureCancelButton);
            }
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
            Component target = analyticsTutorialStage switch
            {
                0 => analytics?.TutorialMetricTarget(3),
                1 => analytics?.TutorialDemandHeaderTarget(3),
                2 => analytics?.TutorialDemandHeaderTarget(4),
                3 => analytics?.TutorialDemandHeaderTarget(4),
                4 => analytics?.TutorialMetricTarget(0),
                _ => AnalyticsPopupUI.TutorialCloseButton
            };

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
            SupplyPlanningScreenUI ingredients = FindFirstObjectByType<SupplyPlanningScreenUI>();
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
        if (IsBaselinePracticeStep(step))
        {
            if (!tutorialFastForwardControlUsed)
            {
                GameTimeUI timeControls = FindFirstObjectByType<GameTimeUI>(FindObjectsInactive.Exclude);
                if (timeControls != null && timeControls.fastForwardButton != null)
                    HighlightControl(timeControls.fastForwardButton);
            }
            else if (BaselinePracticeRunComplete && nextButton != null
                && nextButton.gameObject.activeInHierarchy)
                FocusTutorialControl(nextButton);
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
        RefreshTutorialFeatureLocks();
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
                TryBeginMilestoneLesson();
            return;
        }
        if (milestoneOneLessonActive)
        {
            bool hideLesson = MilestoneUnlockPopupUI.IsPendingOrVisible
                || (IsWorkerLesson && ManagementModeController.Instance?.IsCapturingFlow == true);
            if (canvasRoot != null && canvasRoot.activeSelf == hideLesson)
                canvasRoot.SetActive(!hideLesson);
            if (hideLesson)
            {
                if (IsWorkerLesson) ClearControlHighlights();
                return;
            }
            if (IsWorkerLesson && ProductionManager.Instance?.productionFlows.Contains(workerLessonFlow) == false)
            {
                workerLessonFlow = null;
                EndMilestoneOneLesson();
                return;
            }
        }
        TrackCameraTutorialInputs();
        RefreshCameraPromptStatus();
        RefreshBuildTutorialProgress();
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
        if (IsBaselinePracticeStep(Steps[stepIndex]) || observingImprovedLayout)
            UpdateBaselinePracticeRun();
        else if (Steps[stepIndex].liveCustomer && !liveCustomerSpawned)
            SpawnPracticeCustomer();
        if (stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireStationHealthReview)
            RefreshStationHealthLesson();
        RefreshCustomerJourneyCard();
        RefreshAutomaticAdvance();
        UpdateTutorialTextReveal();
        ApplyISEVocabularyHighlighting();
        UpdateGlossaryTermFlashBox();

        if (highlight == null || !highlight.activeSelf || highlightTarget == null)
            return;
        float pulse = 0.5f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
        highlight.transform.position = GetTutorialWorldAimPoint(highlightTarget);
        highlight.transform.localScale = Vector3.one * pulse;
    }

    void TryBeginMilestoneLesson()
    {
        if (!gameplayReady || !IsComplete || !ShiftPlanningUI.IsPlanning
            || TitleScreenController.IsTitleScreenActive || CutscenePresentation.IsActive
            || IntroCutsceneUI.IsPlaying || MilestoneUnlockPopupUI.IsPendingOrVisible
            || PauseMenuUI.IsOpen)
            return;

        string milestone = MilestoneProgressManager.Instance?.ActiveMilestoneId;
        ProductionManager production = ProductionManager.Instance;
        if (workerLessonFlow != null && production != null)
        {
            int flowIndex = production.productionFlows.IndexOf(workerLessonFlow);
            if (flowIndex < 0)
                workerLessonFlow = null;
            else if (ManagementModeController.Instance?.IsCapturingFlow != true)
            {
                bool selectionChanged = production.selectedFlowIndex != flowIndex;
                if (selectionChanged)
                    production.SelectProductionFlow(flowIndex);
                BeginMilestoneLesson(workerLessonHireReviewed ? AssignWorkerStep : HireWorkerStep);
                if (selectionChanged)
                    FindFirstObjectByType<WorkersUI>()?.RefreshFlowOnly();
                return;
            }
        }

        if (milestone == "milestone_02" && !milestoneOneAnalyticsLessonDone)
        {
            BeginMilestoneLesson(MilestoneTwoAnalyticsStep);
            return;
        }
        if (milestone == "milestone_03" && !supplyLessonDone)
        {
            supplyLessonPage = 0;
            BeginMilestoneLesson(MilestoneThreeSupplyStep);
            return;
        }
        if (milestone != "milestone_01" || milestoneOneIntroProgress > MilestoneOneObjectivesStep)
            return;

        if (milestoneOneIntroProgress == 0)
        {
            CustomerOrderConfig menu = ProductionManager.Instance?.orderConfig;
            KitchenInventory stock = KitchenInventory.Instance;
            if (menu == null || stock == null) return;
            AssemblyRecipeDefinition shake = menu.GetAssemblyRecipe(menu.drinkItem);
            stock.AddStock(menu.friesIngredient, 12);
            stock.AddStock(menu.fryContainerIngredient, 12);
            stock.AddStock(shake?.processedInput, 12);
            stock.AddStock(shake?.pantryInput, 12);
            milestoneOneIntroProgress = MilestoneOneIntroStep;
        }

        milestoneOneLessonActive = true;
        running = true;
        pendingStart = false;
        EnsureUI();
        ShowMilestoneOneIntroStep();
    }

    void ShowMilestoneOneIntroStep()
    {
        stepIndex = milestoneOneIntroProgress;
        if (IsFirstDayRecipeReview)
        {
            firstDayFriesReviewed = false;
            firstDayShakeReviewed = false;
        }
        ShowStep();
    }

    void BeginMilestoneLesson(int lessonStep)
    {
        milestoneOneLessonActive = true;
        running = true;
        pendingStart = false;
        stepIndex = lessonStep;
        analyticsScreenOpened = false;
        analyticsScreenClosed = false;
        if (lessonStep == MilestoneTwoAnalyticsStep)
            analyticsTutorialStage = -1;
        EnsureUI();
        ShowStep();
    }

    void RefreshWorkerLesson()
    {
        ProductionManager production = ProductionManager.Instance;
        if (production == null) return;
        bool assigning = stepIndex == AssignWorkerStep;
        if (assigning && workerLessonFlow?.workers?.Exists(worker => worker != null) == true)
        {
            SetTutorialBodyText("Worker assigned. Click <b>Finish</b> to continue.");
            FocusTutorialControl(nextButton);
            return;
        }

        WorkersUI workers = IsBusinessWorkersTabActive() ? FindFirstObjectByType<WorkersUI>() : null;
        if (workers == null)
        {
            SetTutorialBodyText("Open <b>Kitchen → Workers</b>.");
            MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            Button workersTab = tabs?.WorkspaceTabButton(1);
            if (workersTab != null && workersTab.gameObject.activeInHierarchy)
                FocusTutorialControl(workersTab);
            else
                FocusTutorialControl(tabs?.KitchenButton);
            return;
        }

        if (!assigning)
        {
            SetTutorialBodyText(Steps[HireWorkerStep].body);
            FocusTutorialControl(workers.hireButton);
            return;
        }

        if (production.SelectedFlow != workerLessonFlow)
        {
            SetTutorialBodyText("Select the flow you just saved.");
            FocusTutorialControl(workers.GetTutorialFlowTab(workerLessonFlow));
            return;
        }

        WorkerCardUI availableCard = null;
        WorkerCardUI assignedCard = null;
        foreach (WorkerCardUI card in workers.GetComponentsInChildren<WorkerCardUI>())
        {
            if (card.employee == null || card.flowAssignButton == null) continue;
            if (production.GetFlowForWorker(card.employee) == null)
            {
                availableCard = card;
                break;
            }
            if (assignedCard == null) assignedCard = card;
        }

        if (availableCard != null)
        {
            SetTutorialBodyText(Steps[AssignWorkerStep].body);
            FocusTutorialControl(availableCard.flowAssignButton);
        }
        else if (assignedCard != null)
        {
            SetTutorialBodyText("To give a worker this flow, click <b>Move here</b> on their card.");
            FocusTutorialControl(assignedCard.flowAssignButton);
        }
        else
        {
            SetTutorialBodyText("Hire a worker, then click <b>Assign</b> on their card.");
            FocusTutorialControl(workers.hireButton);
        }
    }

    void RefreshSupplyLesson()
    {
        if (!IsBusinessSupplyTabActive())
        {
            SetTutorialBodyText("Open <b>Kitchen → Supply</b> to start.");
            MainHudTabs tabs = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            Button supplyTab = tabs?.WorkspaceTabButton(2);
            if (supplyTab != null && supplyTab.gameObject.activeInHierarchy)
                FocusTutorialControl(supplyTab);
            else if (tabs != null && tabs.KitchenButton != null)
            {
                tabs.PrepareWorkspaceTabForNextOpen(2);
                FocusTutorialControl(tabs.KitchenButton);
            }
            return;
        }

        SupplyPlanningScreenUI supply = FindFirstObjectByType<SupplyPlanningScreenUI>();
        switch (supplyLessonPage)
        {
            case 0:
                SetTutorialBodyText("Compare <b>Stock</b> with the <b>Forecast</b>. Supplier capacity limits your total delivery rate.");
                FocusTutorialControl(supply?.TutorialCapacityTarget);
                break;
            case 1:
                SetTutorialBodyText("Use <b>−</b> and <b>+</b> to set delivery rates. <b>Balance Rates</b> suggests rates within your supplier capacity.");
                FocusTutorialControl(supply?.TutorialBalanceRatesTarget);
                break;
            default:
                SetTutorialBodyText("Click <b>Apply Supply Plan</b> to use your rates. Deliveries run during the shift and stop when stock is full.");
                FocusTutorialControl(supply?.TutorialPlaceOrderButton);
                break;
        }
    }

    void RefreshStationHealthLesson()
    {
        bool onboardingReview = !milestoneOneLessonActive;
        if (onboardingReview && (stepIndex < 0 || stepIndex >= Steps.Length
            || !Steps[stepIndex].requireStationHealthReview))
            return;

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
            if (onboardingReview)
            {
                tutorialStationHealthReviewed = true;
                SetLayoutTutorialText("Open <b>Kitchen</b> and choose <b>Stations</b> to move a station.");
            }
            else
            {
                SetTutorialBodyText(
                    "Read the station's percentages. They add to 100%. <b>Working</b> is processing time. <b>Blocked</b> means output cannot move; <b>Starved</b> means inputs are missing. <b>Idle</b> means no work is needed, often because demand is low. High Blocked or Starved time points to a bottleneck to investigate.");
            }
            RefreshAdvanceGate();
            UpdateContextualCallout();
        }
        else if (!onboardingReview && !milestoneOneStationClickObserved && selectionChanged)
        {
            SetTutorialBodyText(
                "Open <b>Kitchen</b>, choose <b>Workers</b>, then click a production station. Its card shows the share of time spent Working, Blocked, Starved, and Idle.");
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
        RefreshTutorialFeatureLocks(true);
    }

    Vector3 GetTutorialWorldAimPoint(Transform target)
    {
        bool improvingLayout = running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireOptimizedLayout && target != null;
        if (improvingLayout)
            return GetTutorialStationBounds(target).center;

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
        if (Steps[stepIndex].requireProductionFlowOverview && businessTutorialFlow?.connections != null
            && businessFlowOverviewStage > 0 && businessFlowOverviewStage <= businessTutorialFlow.connections.Count)
        {
            ProductionFlowConnection edge = businessTutorialFlow.connections[businessFlowOverviewStage - 1];
            UpdateTutorialFlowGuide(edge?.from != null ? edge.from.transform : null,
                edge?.to != null ? edge.to.transform : null);
            return;
        }
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
        bool hideWorldMarker = recipeConfigurationStep
            || Steps[stepIndex].requireOptimizedLayout;
        if (highlight != null)
        {
            if (hideWorldMarker)
                highlight.SetActive(false);
            else
            {
                highlight.SetActive(true);
                float markerPulse = 0.48f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.22f);
                highlight.transform.position = GetTutorialWorldAimPoint(highlightTarget);
                highlight.transform.localScale = Vector3.one * markerPulse;
            }
        }
        if (!hideWorldMarker && secondaryHighlightTarget != null)
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
            SupplyPlanningScreenUI ingredients = FindFirstObjectByType<SupplyPlanningScreenUI>();
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
        if (IsBuildTutorialActive)
            return Instance.buildTutorialStage == 1 && item == FindBuildTutorialPantryItem();
        return ItemMatchesHighlight(item, Instance.currentHighlight);
    }

    public static void NotifyTutorialStationPurchased(ItemDefinition item, int paid)
    {
        if (!IsBuildTutorialActive || Instance.buildTutorialStage != 1
            || item == null || item != FindBuildTutorialPantryItem()) return;
        Instance.buildTutorialPantryPurchaseMade = true;
        Instance.buildTutorialPantryPaid = paid;
        Instance.SetBuildTutorialInstruction();
    }

    public static bool CanRemoveTutorialPurchasedPantry(GameObject target)
    {
        return IsBuildTutorialActive && Instance.buildTutorialStage == Instance.DeletePantryStage
            && target != null && target == Instance.buildTutorialPantry;
    }

    public static void NotifyTutorialPantryRemoved(GameObject target)
    {
        if (!CanRemoveTutorialPurchasedPantry(target)) return;
        Instance.buildTutorialStage = Instance.DeletePantryStage + 1;
        Instance.buildTutorialPantry = null;
        Instance.SetBuildTutorialInstruction();
        Instance.RefreshAdvanceGate();
    }

    internal static int? GetPracticePantryRefund(GameObject target) =>
        CanRemoveTutorialPurchasedPantry(target) && Instance.IsFirstDayBuildLesson
            ? Instance.buildTutorialPantryPaid : (int?)null;

    internal static bool IsPracticePantryBeingConfigured(GameObject target) =>
        Instance != null && Instance.IsFirstDayBuildLesson && Instance.buildTutorialStage == 2
        && target != null && target == Instance.buildTutorialPantry;

    // Checklist stations unlock together after the tutorial introduces them.
    public static bool IsStationLocked(ItemDefinition item)
    {
        if (item == null || !BlocksProgression) return false;
        if (IsBuildTutorialActive && Instance.buildTutorialStage == 1
            && item == FindBuildTutorialPantryItem()) return false;
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

    static ItemDefinition FindBuildTutorialPantryItem()
    {
        InventoryManager inventory = FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include);
        if (inventory == null || inventory.allItems == null) return null;
        foreach (ItemDefinition item in inventory.allItems)
            if (item != null && item.prefab != null
                && item.prefab.GetComponentInChildren<PantryStation>(true) != null)
                return item;
        return null;
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
        if (canvasRoot != null && panel != null && gusPanel != null && gusDialogueText != null
            && nextButton != null && cameraPromptPanel != null
            && fastForwardHelpCard != null && fastForwardHelpArrow != null
            && fastForwardRateHelpArrow != null
            && nextButton.transform.IsChildOf(panel.transform))
        {
            EnsureFlowLinkingVideoUi();
            ISEGlossaryLinkHandler.Attach(titleText);
            ISEGlossaryLinkHandler.Attach(bodyText);
            ISEGlossaryLinkHandler.Attach(gusDialogueText);
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
        var border = panel.AddComponent<Outline>();
        border.effectColor = GameUITheme.Accent;
        border.effectDistance = new Vector2(2f, -2f);
        border.useGraphicAlpha = false;
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

        var header = new GameObject("InstructionHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup),
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

        var headerText = new GameObject("InstructionHeaderText", typeof(RectTransform),
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
        titleText = CreateLabel(headerText.transform, "", 24, FontStyles.Bold, Color.white);
        titleText.alignment = TextAlignmentOptions.Left;
        ISEGlossaryLinkHandler.Attach(titleText);
        bodyText = CreateLabel(panel.transform, "", 24, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 19f;
        bodyText.fontSizeMax = 24f;
        bodyText.overflowMode = TextOverflowModes.Overflow;
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
        cameraPromptLayout.padding = new RectOffset(0, 0, 4, 4);
        cameraPromptLayout.spacing = 5f;
        cameraPromptLayout.childAlignment = TextAnchor.MiddleCenter;
        cameraPromptLayout.childControlWidth = true;
        cameraPromptLayout.childControlHeight = true;
        cameraPromptLayout.childForceExpandWidth = true;
        cameraPromptLayout.childForceExpandHeight = false;
        var cameraPromptSize = cameraPromptPanel.GetComponent<LayoutElement>();
        cameraPromptSize.minHeight = cameraPromptSize.preferredHeight = 150f;
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
        navigationLayout = footer.GetComponent<LayoutElement>();
        var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        nextButton = CreateTutorialButton(footer.transform, "Next", OnNext, new Color(0.22f, 0.55f, 0.38f, 1f));
        nextButtonLayout = nextButton.gameObject.AddComponent<LayoutElement>();
        nextLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();
        measuredNextLabel = null;
        FitTutorialButton();
        CreateGusDialoguePanel(canvasRoot.transform);
        ISEGlossaryLinkHandler.Attach(gusDialogueText);
        calloutArrow = CreateCalloutArrow(canvasRoot.transform);
        CreateFastForwardHelpCard(canvasRoot.transform);
        GameUITheme.ApplyToCanvas(canvas);
    }

    void CreateGusDialoguePanel(Transform parent)
    {
        gusPanel = new GameObject("GusDialogueCard", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(Outline));
        gusPanel.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gusPanel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(600f, 132f);

        Image background = gusPanel.GetComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.985f);
        background.raycastTarget = false;
        Outline outline = gusPanel.GetComponent<Outline>();
        outline.effectColor = GameUITheme.Accent;
        outline.effectDistance = new Vector2(2f, -2f);

        HorizontalLayoutGroup layout = gusPanel.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 18, 12, 12);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        GameObject portraitFrame = new GameObject("GusPortraitFrame", typeof(RectTransform),
            typeof(Image), typeof(LayoutElement), typeof(Outline));
        portraitFrame.transform.SetParent(gusPanel.transform, false);
        Image portraitBackground = portraitFrame.GetComponent<Image>();
        portraitBackground.color = GameUITheme.Surface;
        portraitBackground.raycastTarget = false;
        LayoutElement portraitLayout = portraitFrame.GetComponent<LayoutElement>();
        portraitLayout.minWidth = portraitLayout.preferredWidth = 82f;
        portraitLayout.minHeight = portraitLayout.preferredHeight = 82f;
        Outline portraitOutline = portraitFrame.GetComponent<Outline>();
        portraitOutline.effectColor = GameUITheme.Accent;
        portraitOutline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject portraitObject = new GameObject("GusFace", typeof(RectTransform),
            typeof(RawImage), typeof(GusCutscenePreview));
        portraitObject.transform.SetParent(portraitFrame.transform, false);
        RectTransform portraitRect = (RectTransform)portraitObject.transform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(3f, 3f);
        portraitRect.offsetMax = new Vector2(-3f, -3f);
        RawImage portraitImage = portraitObject.GetComponent<RawImage>();
        portraitImage.color = Color.white;
        portraitImage.raycastTarget = false;
        TitleScreenController titleController = FindFirstObjectByType<TitleScreenController>();
        GameObject gusModel = titleController != null && titleController.gusPrefab != null
            ? titleController.gusPrefab
            : Resources.Load<GameObject>("Prefabs/character_default");
        if (gusModel != null)
            portraitObject.GetComponent<GusCutscenePreview>().Configure(gusModel, portraitImage, true);

        GameObject textColumn = new GameObject("GusDialogue", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColumn.transform.SetParent(gusPanel.transform, false);
        LayoutElement textColumnSize = textColumn.GetComponent<LayoutElement>();
        textColumnSize.minWidth = 0f;
        textColumnSize.flexibleWidth = 1f;
        VerticalLayoutGroup textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
        textLayout.spacing = 3f;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;

        TextMeshProUGUI gusLabel = CreateLabel(textColumn.transform, "GUS", 15f,
            FontStyles.Bold, GameUITheme.Accent);
        gusLabel.alignment = TextAlignmentOptions.Left;
        gusLabel.textWrappingMode = TextWrappingModes.NoWrap;
        gusDialogueText = CreateLabel(textColumn.transform, "", 21f,
            FontStyles.Normal, new Color(0.94f, 0.95f, 0.98f));
        gusDialogueText.alignment = TextAlignmentOptions.Left;
        gusDialogueText.enableAutoSizing = true;
        gusDialogueText.fontSizeMin = 18f;
        gusDialogueText.fontSizeMax = 21f;
        gusDialogueText.overflowMode = TextOverflowModes.Ellipsis;
        gusDialogueText.raycastTarget = false;
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
        bool validTutorialStep = running && stepIndex >= 0 && stepIndex < Steps.Length
            && !tutorialUnlockContinuePending;
        bool showHelpCard = validTutorialStep && Steps[stepIndex].requireTimeControlInteractions;
        bool showArrows = validTutorialStep
            && (IsBaselinePracticeStep(Steps[stepIndex]) && !BaselinePracticeRunComplete
                || Steps[stepIndex].requireTimeControlInteractions);
        if (fastForwardHelpCard != null && fastForwardHelpCard.activeSelf != showHelpCard)
            fastForwardHelpCard.SetActive(showHelpCard);
        if (!showArrows || canvasRoot == null)
        {
            if (fastForwardHelpArrow != null) fastForwardHelpArrow.gameObject.SetActive(false);
            if (fastForwardRateHelpArrow != null) fastForwardRateHelpArrow.gameObject.SetActive(false);
            return;
        }

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

        if (showHelpCard && fastForwardHelpCardRect != null)
        {
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
            fastForwardHelpCardRect.anchoredPosition = new Vector2(centerX, cardY);
        }

        PositionSpecificCalloutArrow(fastForwardHelpArrow, fastBounds.center, fastBounds,
            placeArrowBelowTarget: true);
        PositionSpecificCalloutArrow(fastForwardRateHelpArrow, rateBounds.center, rateBounds,
            placeArrowBelowTarget: false);
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

    void PositionSpecificCalloutArrow(RectTransform arrow, Vector2 targetPosition, Rect targetBounds,
        bool placeArrowBelowTarget)
    {
        if (arrow == null) return;
        RectTransform previousArrow = calloutArrow;
        calloutArrow = arrow;
        PositionCalloutArrow(targetPosition, targetBounds, true, placeArrowBelowTarget);
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
        rowLayout.padding = new RectOffset(12, 12, 2, 2);
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        var rowSize = row.GetComponent<LayoutElement>();
        rowSize.minHeight = rowSize.preferredHeight = 44f;
        rowSize.flexibleHeight = 0f;

        var iconGroup = new GameObject("InputIcons", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        iconGroup.transform.SetParent(row.transform, false);
        var iconLayout = iconGroup.GetComponent<HorizontalLayoutGroup>();
        iconLayout.spacing = 4f;
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
            iconSize.minWidth = iconSize.preferredWidth = 30f;
            iconSize.minHeight = iconSize.preferredHeight = 30f;
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            iconCount++;
        }
        iconGroup.GetComponent<LayoutElement>().preferredWidth = iconCount * 30f
            + Mathf.Max(0, iconCount - 1) * iconLayout.spacing;

        TextMeshProUGUI actionLabel = CreateLabel(row.transform, action, 17f,
            FontStyles.Bold, new Color(0.92f, 0.94f, 0.98f));
        actionLabel.alignment = TextAlignmentOptions.Left;
        actionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var actionSize = actionLabel.GetComponent<LayoutElement>();
        actionSize.minWidth = 0f;
        actionSize.flexibleWidth = 1f;

        TextMeshProUGUI status = CreateLabel(row.transform, "TRY IT", 13f,
            FontStyles.Bold, new Color(0.72f, 0.76f, 0.82f, 1f));
        status.alignment = TextAlignmentOptions.Right;
        status.textWrappingMode = TextWrappingModes.NoWrap;
        var statusSize = status.GetComponent<LayoutElement>();
        statusSize.minWidth = statusSize.preferredWidth = 64f;
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
        var root = new GameObject("TutorialArrow", typeof(RectTransform), typeof(CanvasGroup),
            typeof(RawImage));
        root.transform.SetParent(parent, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(80f, 80f);
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        RawImage cursorArrow = root.GetComponent<RawImage>();
        cursorArrow.texture = Resources.Load<Texture2D>(TutorialArrowCursorResource);
        cursorArrow.color = new Color32(255, 202, 48, 255);
        cursorArrow.raycastTarget = false;
        root.transform.SetAsLastSibling();
        root.SetActive(false);
        return rootRect;
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

    void FitTutorialButton()
    {
        if (nextLabel == null) return;
        const float horizontalPadding = 24f;
        const float verticalPadding = 12f;
        float availableWidth = Mathf.Max(horizontalPadding + 1f,
            ((RectTransform)panel.transform).rect.width - 32f);
        if (measuredNextLabel == nextLabel.text && Mathf.Approximately(measuredNextLabelWidth, availableWidth))
            return;

        measuredNextLabel = nextLabel.text;
        measuredNextLabelWidth = availableWidth;
        Vector2 naturalSize = nextLabel.GetPreferredValues(nextLabel.text);
        float width = Mathf.Min(availableWidth, Mathf.Max(188f, Mathf.Ceil(naturalSize.x) + horizontalPadding));
        Vector2 wrappedSize = nextLabel.GetPreferredValues(nextLabel.text, width - horizontalPadding, float.PositiveInfinity);
        float height = Mathf.Max(46f, Mathf.Ceil(wrappedSize.y) + verticalPadding);
        nextButtonLayout.preferredWidth = width;
        nextButtonLayout.preferredHeight = height;
        navigationLayout.preferredHeight = height;
    }

    static Button CreateTutorialButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(12f, 6f);
        tr.offsetMax = new Vector2(-12f, -6f);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 18;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }
}
