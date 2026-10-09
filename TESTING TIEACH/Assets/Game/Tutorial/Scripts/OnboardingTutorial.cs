using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Guided first-run tour: empty kitchen, buy/place stations, order ingredients,
/// then one practice customer. Skip from the debug menu restores the starter kitchen.
/// </summary>
public class OnboardingTutorial : MonoBehaviour
{
    public const string PrefsCompleteKey = "TIEACH_OnboardingComplete";
    public const string PauseSource = "OnboardingTutorial";

    public static OnboardingTutorial Instance { get; private set; }

    public static bool IsComplete => PlayerPrefs.GetInt(PrefsCompleteKey, 0) == 1;
    public static bool IsActive => Instance != null && Instance.running;
    public static bool BlocksAutoCustomers => IsActive || !IsComplete;
    public static bool AllowsPracticeCustomer => IsActive && Steps[Instance.stepIndex].liveCustomer;
    public static bool BlocksProgression => IsActive || (!IsComplete && Instance != null && Instance.pendingStart);
    public static int StationUnlockProgress => BlocksProgression ? Instance.furthestStepIndex : int.MaxValue;

    bool running;
    bool pendingStart;
    bool gameplayReady;
    bool liveCustomerSpawned;
    bool starterKitchenHidden;
    int stepIndex;
    public int SaveStepIndex => stepIndex;
    public void RestoreCheckpoint(bool complete, int savedStep)
    {
        PlayerPrefs.SetInt(PrefsCompleteKey, complete ? 1 : 0);
        stepIndex = Mathf.Clamp(savedStep, 0, Steps.Length - 1);
        furthestStepIndex = stepIndex;
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
    readonly Dictionary<Graphic, GameObject> tutorialBorders = new Dictionary<Graphic, GameObject>();
    float nextControlHighlightRefresh;
    readonly List<GameObject> starterKitchen = new List<GameObject>();

    GameObject canvasRoot;
    GameObject panel;
    TextMeshProUGUI titleText;
    TextMeshProUGUI bodyText;
    TextMeshProUGUI stepLabel;
    Button backButton;
    Button nextButton;
    TextMeshProUGUI nextLabel;
    RectTransform calloutTarget;
    RectTransform calloutArrow;
    GameObject highlight;
    Transform highlightTarget;
    Highlight currentHighlight;

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
        public bool requireAllStations;
        public bool requireFlow;
        public bool requireWorkerOnFlow;
        public bool requireHiredWorker;
        public bool requireEditedFlow;
        public bool requireRecipes;
        public bool requireIngredients;

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
            bool requireIngredients = false)
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
            this.requireAllStations = requireAllStations;
            this.requireFlow = requireFlow;
            this.requireWorkerOnFlow = requireWorkerOnFlow;
            this.requireHiredWorker = requireHiredWorker;
            this.requireEditedFlow = requireEditedFlow;
            this.requireRecipes = requireRecipes;
            this.requireIngredients = requireIngredients;
        }
    }

    static readonly Step[] Steps =
    {
        new Step(
            "Empty kitchen",
            "This shift starts with an <b>empty kitchen</b>. Let's build it one step at a time.\n\n" +
            "You will buy each workstation, stock ingredients, then serve one customer. Use <b>Back</b> and <b>Next</b> to move through the steps.",
            "Next", Highlight.None),
        new Step(
            "Buy stations",
            "Open <b>Build</b> (top-left, or press 1). Each station's <b>first copy is free</b>. Click Buy, then click a floor tile to place it.\n\n" +
            "Place stations in the kitchen, not the lobby. You can rotate while placing if the ghost shows a facing arrow.\n\n" +
            "Stations unlock one at a time as you reach their tutorial section. Previously introduced stations stay available. Next stays locked until the required station is on the floor.",
            "Next", Highlight.Inventory, openInventory: true),
        new Step(
            "Register",
            "Customers enter and line up at the <b>register</b>. They order a burger, fries, drink, or combo here.\n\n" +
            "Open <b>Build</b> and buy your free <b>register</b>. Placement starts automatically: click a free <b>counter</b> on the lobby side.\n\nKeep the queue area clear. Place the register to unlock Next.",
            "Next", Highlight.Register, openInventory: true, requirePlaced: true),
        new Step(
            "Freezer",
            "Buy and place a <b>freezer</b>. Each freezer stores one ingredient. Select it under <b>Business > Staff</b> and choose <b>Raw Patty</b>. A cook carries those patties to the grill.\n\n" +
            "If the freezer is unconfigured or empty, burgers never start.\n\nPlace one to continue.",
            "Next", Highlight.Freezer, openInventory: true, requirePlaced: true),
        new Step(
            "Grill",
            "Buy and place a <b>grill</b> next in the burger line. After the freezer, this cooks the patty.\n\n" +
            "Later, under Business > Staff, point the grill's output toward assembly so cooked patties keep moving.\n\nPlace one to continue.",
            "Next", Highlight.Grill, openInventory: true, requirePlaced: true),
        new Step(
            "Fryer",
            "Buy and place a <b>fryer</b>. Fries skip the freezer and grill — they are their own short path.\n\n" +
            "The fries flow is Potato Pantry > Cutting Station > Fryer > Assembly > Pickup Station. The Fryer makes cooked potato slices, then Assembly combines them with a fry container from a second Pantry.\n\nPlace one to continue.",
            "Next", Highlight.Fryer, openInventory: true, requirePlaced: true),
        new Step(
            "Pantry",
            "Buy and place a <b>pantry</b>. Each pantry stores exactly one ingredient. Select it under <b>Business > Staff</b> and choose <b>Bun</b> for this burger line.\n\n" +
            "Later, use separate pantries for potatoes, raw cheese, lettuce, or tomatoes.\n\nPlace one to continue.",
            "Next", Highlight.Pantry, openInventory: true, requirePlaced: true),
        new Step(
            "Assembly",
            "Buy and place an <b>assembly</b> table. It always combines two inputs into one output.\n\n" +
            "For the Hamburger recipe, the inputs are one cooked patty and one bun. Assembly output should point to the Pickup Station.\n\nPlace one to continue.",
            "Next", Highlight.Assembly, openInventory: true, requirePlaced: true),
        new Step(
            "Pickup Station",
            "Buy and place a <b>Pickup Station</b> on the pass. Any finished item can wait here for customer pickup, including food, drinks, and future products.\n\n" +
            "If this sits empty, upstream stations are too slow. If it fills and items expire, you produced more than you can serve.\n\nPlace one to continue.",
            "Next", Highlight.HeatLamp, openInventory: true, requirePlaced: true),
        new Step(
            "Configure ingredients and recipes",
            "Select stations under <b>Business > Staff</b> to configure them. Set the <b>Freezer to Raw Patty</b>, the <b>Pantry to Bun</b>, and the Assembly recipe to <b>Hamburger</b>.\n\n" +
            "Set the <b>Grill to Cooked Patty</b> too. Assembly combines one cooked patty and one bun. Next unlocks when all four selections are ready.",
            "Next", Highlight.Management, openWorkers: true, requireRecipes: true),
        new Step(
            "Buy ingredients",
            "Stations do nothing without stock. Open <b>Business</b> (top-left, or press 2), then <b>Menu & Supply</b>.\n\n" +
            "Buy Raw Patties, Buns, and Potatoes. Packs spend cash. A delivery person brings them in through the front door after a short wait.",
            "Next", Highlight.Management, openIngredients: true, requireIngredients: true),
        new Step(
            "Hire workers",
            "Stations only cook if people work a <b>flow</b>. Open <b>Business > Staff</b> (top-left, or press 2).\n\n" +
            "Click <b>Hire</b> at the top of the Workers tab to add staff. Each hire costs money. Workers use their priorities to choose jobs within their assigned flow; unassigned workers remain idle.\n\n" +
            "Hire at least one worker to continue.",
            "Next", Highlight.Management, openWorkers: true, requireHiredWorker: true),
        new Step(
            "Create a flow",
            "Still on Workers, click <b>Create Flow</b>. The panel hides so you can see the kitchen.\n\n" +
            "Hold the left mouse button on a station, drag to its destination, and release to add a connection.\n\n" +
            "In one flow, drag these connections: <b>Freezer > Grill</b>, <b>Grill > Assembly</b>, <b>Pantry > Assembly</b>, <b>Assembly > Pickup Station</b>. Click Finish.",
            "Next", Highlight.Management, openWorkers: true, requireFlow: true),
        new Step(
            "Edit a flow",
            "Select the flow chip at the top of Workers, then click <b>Edit Flow</b>.\n\n" +
            "Drag to add a connection. Click selects a station; Ctrl-click removes it and its connections. Press Esc to cancel edits.\n\n" +
            "Edit an existing flow and click Finish to save it before continuing. Later, use Edit to correct a station or build a second line (fryer → Pickup Station).",
            "Next", Highlight.Management, openWorkers: true, requireEditedFlow: true),
        new Step(
            "Assign workers to a flow",
            "Select your burger flow, then <b>drag a worker card onto DROP WORKER</b> in the flow panel.\n\n" +
            "That worker's name appears on the flow. Click the name chip to unassign. The kitchen will not cook until at least one worker is on a flow.\n\n" +
            "Next stays locked until a flow has a worker assigned.",
            "Next", Highlight.Management, openWorkers: true, requireWorkerOnFlow: true),
        new Step(
            "One customer loop",
            "Here is the basic service loop:\n\n" +
            "1. Customer arrives and orders at the register.\n" +
            "2. Workers follow the flow you built: freezer > grill > assembly for burgers, pantry > cutting > fryer for fries, with both grill and pantry feeding burger assembly.\n" +
            "3. Finished items wait at the Pickup Station.\n" +
            "4. Food is handed off and the customer leaves.",
            "Try one customer", Highlight.None, requireAllStations: true, requireFlow: true, requireWorkerOnFlow: true),
        new Step(
            "Serve one order",
            "The clock is running. Only <b>one customer</b> will come in and order <b>one burger</b> so you can watch the loop.\n\n" +
            "If nobody is cooking, check workers, outputs, and that you bought ingredient packs.\n\n" +
            "Serve that order, then click Finish. Extra customers stay away until the tutorial ends.",
            "Finish", Highlight.Register, liveCustomer: true, requireAllStations: true, requireFlow: true, requireWorkerOnFlow: true),
    };

    // Gus speaks the tutorial while the Step data continues to define gameplay gates.
    static readonly string[] GusDialogue =
    {
        "I cleared out the kitchen so we can rebuild the operation properly, one step at a time.\n\n" +
        "I will walk you through the stations, ingredients, and staffing, then we will test your system with one customer.",

        "Open <b>Build</b> at the top-left, or press <b>1</b>. I covered the first copy of each station, so those are free. Click Buy, then choose a kitchen floor tile.\n\n" +
        "Keep equipment out of the lobby. You can rotate a station while its placement ghost is visible.",

        "Every order begins at the <b>register</b>. Open <b>Build</b>, take the free register, then click an open <b>counter</b> tile on the lobby side.\n\n" +
        "Keep some floor space clear for the customer line. Place the register and I will show you the kitchen.",

        "Let's start the burger process. Buy and place a <b>freezer</b>. Each freezer stores one ingredient, so select it under <b>Business > Staff</b> and choose <b>Raw Patty</b>.\n\n" +
        "If it is unconfigured or empty, burger production cannot begin.",

        "Next, buy and place a <b>grill</b>. Workers carry raw patties here from the freezer to cook them.\n\n" +
        "Distance matters because every extra tile adds travel time. Put it somewhere sensible and we will connect its output later.",

        "Now place a <b>fryer</b>. Fries use their own short production path, separate from burgers.\n\n" +
        "Fries use Potato Pantry > Cutting Station > Fryer > Assembly > Pickup Station. The Fryer makes cooked potato slices, and Assembly combines them with a fry container supplied by a second Pantry. We will practice a basic burger first; no cutting station is required for it.",

        "Place a <b>pantry</b> for the burger line. Each pantry stores one ingredient, so select it under <b>Business > Staff</b> and choose <b>Bun</b>.\n\n" +
        "A fries line needs one pantry configured for potatoes and another configured for fry containers.",

        "Place an <b>assembly</b> table. Workers combine one cooked patty with one bun here to finish a burger.\n\n" +
        "Connect Grill > Assembly for cooked patties and Pantry > Assembly for buns, then Assembly > Pickup Station. Both input branches belong in the same flow.",

        "Place a <b>Pickup Station</b> on the counter. Finished burgers, fries, and drinks wait here until customers collect them.\n\n" +
        "If it stays empty, production may be too slow. If it stays full, we may be producing more than customers need.",

        "Select the source stations under <b>Business > Staff</b>. Set the <b>Freezer to Raw Patty</b> and the <b>Pantry to Bun</b>, then choose the Hamburger recipe at Assembly.\n\n" +
        "Set <b>Grill to Cooked Patty</b> too. Click each station outside Create/Edit Flow to select its ingredient or recipe. Next waits for all four settings. A warning above a station means its selection is missing.",

        "The equipment is useless without material to process. Open <b>Business</b>, or press <b>2</b>, and choose <b>Menu & Supply</b>.\n\n" +
        "Order at least one pack of <b>Raw Patties</b>, <b>Buns</b>, and <b>Potatoes</b>. A delivery person will bring the combined order through the front door.",

        "Now we need someone to run the process. Open <b>Business > Staff</b>, or press <b>2</b>, and click <b>Hire</b>. Each employee costs money, so staffing is a capacity decision.\n\n" +
        "A worker can carry up to four items after upgrades, but anyone you do not assign to a flow will remain idle.",

        "Let's define how work should move. In Workers, click <b>Create Flow</b>, then drag from each source station to its destination.\n\n" +
        "In ONE flow, drag Freezer to Grill, Grill to Assembly, Pantry to Assembly, and Assembly to Pickup Station. Release on each destination and click Finish. The pantry supplies buns to the same assembly station.",

        "Plans change, so you need to know how to revise one. Select the flow chip, then click <b>Edit Flow</b>.\n\n" +
        "Drag to connect stations. Click selects a station; Ctrl-click removes it and its connections. Check the route, then click Finish to save it.",

        "A process plan does nothing until someone owns the work. Select the burger flow, then <b>drag a worker card onto DROP WORKER</b>.\n\n" +
        "Their name will appear on the flow. Assign at least one worker so production can begin.",

        "Here is the system I need you to observe:\n\n" +
        "1. A customer orders at the register.\n2. Workers follow the flows you designed.\n3. Finished items wait at a Pickup Station.\n4. The customer collects each item and leaves.\n\n" +
        "Waiting, walking, and blocked stations are all clues.",

        "I am opening the doors for one test customer. They will order <b>one burger</b>, so we can watch the full flow without a crowd hiding the problems.\n\n" +
        "If production stops, check the worker assignment, station sequence, and ingredient stock. Serve the order, then click Finish."
    };

    static readonly string[] CompactTutorialText =
    {
        "Build a small burger line, connect it, and serve one test customer.",
        "Open <b>Build</b>. The highlighted MK1 station is free for this tutorial.",
        "Buy the <b>Register</b>, then place it on a lobby counter.",
        "Buy a <b>Freezer</b>. You will assign Raw Patty after placement.",
        "Buy a <b>Grill</b>. It turns Raw Patty into Cooked Patty.",
        "Buy a <b>Fryer</b>. Fries use Potato Slices, then combine Cooked Potato Slices with a Fry Container at Assembly.",
        "Buy a <b>Pantry</b> for Buns. Milk, lettuce, and tomatoes come from Freezers.",
        "Buy an <b>Assembly Station</b>. Its selected recipe determines its required inputs.",
        "Buy a <b>Pickup Station</b>. Only finished products delivered here are sold.",
        "In <b>Business > Staff</b>, set Freezer to Raw Patty, Grill to Cooked Patty, Pantry to Bun, and Assembly to Hamburger.",
        "In <b>Food</b>, order Raw Patties and Buns. Express costs $200 and makes the delivery van arrive immediately.",
        "In <b>Staff</b>, hire one worker. Worker priorities can favor specific task types.",
        "Create one flow: Freezer > Grill > Assembly, Pantry > Assembly, then Assembly > Pickup Station.",
        "Use <b>Edit Flow</b> to adjust connections, then click Finish.",
        "Assign a worker using the highlighted drop area. The worker can handle every station in the flow.",
        "Customers request a burger and pay for whichever burger reaches the Pickup Station. Food targets control how many to keep ready.",
        "Watch one order move through the line. Use <b>Food > Recipes</b> for recipe trees and <b>Progression > Research</b> for upgrades."
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
    }

    void OnDestroy()
    {
        ClearControlHighlights();
        if (Instance == this)
            Instance = null;
        TutorialVoiceEvents.OnEvent -= OnGameEvent;
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
    }

    void Start()
    {
        TutorialVoiceEvents.OnEvent += OnGameEvent;
        if (!IsComplete)
        {
            pendingStart = true;
            PrepareEmptyKitchen();
        }
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

        PrepareEmptyKitchen();
        foreach (var customer in FindObjectsByType<CustomerAI>(FindObjectsSortMode.None))
        {
            customer.gameObject.SetActive(false);
            Destroy(customer.gameObject);
        }
        running = true;
        pendingStart = false;
        flowEditCompleted = false;
        liveCustomerSpawned = false;
        EnsureUI();
        ShowStep();
    }

    public void Skip()
    {
        Complete(skipped: true);
    }

    public void Restart()
    {
        PlayerPrefs.DeleteKey(PrefsCompleteKey);
        PlayerPrefs.Save();
        pendingStart = true;
        running = false;
        stepIndex = 0;
        furthestStepIndex = -1;
        flowEditCompleted = false;
        liveCustomerSpawned = false;
        HideUI();
        ClearHighlight();
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
        DestroyTutorialPlacements();
        PrepareEmptyKitchen();
        TryBegin();
    }

    void OnGameEvent(string eventId)
    {
        if (!running) return;
        if (eventId != TutorialVoiceEventId.FirstOrderServed && eventId != TutorialVoiceEventId.OrderServed)
            return;
        if (stepIndex != Steps.Length - 1) return;
        Complete(skipped: false);
    }

    void OnBack()
    {
        if (stepIndex <= 0) return;
        Sfx.Play(SfxId.UiClick);
        stepIndex--;
        ShowStep();
    }

    void OnNext()
    {
        if (!StepRequirementMet())
        {
            Sfx.Play(SfxId.UiError);
            RefreshAdvanceGate();
            return;
        }

        Sfx.Play(SfxId.UiClick);
        if (stepIndex >= Steps.Length - 1)
        {
            Complete(skipped: false);
            return;
        }

        stepIndex++;
        ShowStep();
    }

    void ShowStep()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        var step = Steps[stepIndex];
        furthestStepIndex = Mathf.Max(furthestStepIndex, stepIndex);

        if (canvasRoot != null)
            canvasRoot.SetActive(true);
        if (panel != null)
            panel.SetActive(true);
        if (titleText != null)
            titleText.text = step.title;
        if (bodyText != null)
            bodyText.text = stepIndex < CompactTutorialText.Length ? CompactTutorialText[stepIndex] : step.body;
        if (stepLabel != null)
            stepLabel.text = $"Tutorial  {stepIndex + 1} / {Steps.Length}";
        if (nextLabel != null)
            nextLabel.text = step.nextLabel;
        if (nextButton != null)
            nextButton.gameObject.SetActive(!AutomaticallyAdvances(step));
        autoAdvanceStep = -1;

        ApplyPause(step.pauseSim);
        currentHighlight = step.highlight;
        calloutTarget = null;
        UpdateHighlight(step.highlight);

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

        if (step.openWorkers)
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

    void UpdateContextualCallout()
    {
        if (canvasRoot == null || panel == null) return;

        RectTransform rootRect = (RectTransform)canvasRoot.transform;
        RectTransform panelRect = (RectTransform)panel.transform;
        Vector2 targetPosition;
        bool hasTarget = TryGetCalloutTarget(rootRect, out targetPosition);

        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        float panelWidth = panelRect.rect.width > 1f ? panelRect.rect.width : 720f;
        float panelHeight = panelRect.rect.height > 1f ? panelRect.rect.height : 220f;
        float rootWidth = rootRect.rect.width > 1f ? rootRect.rect.width : 1920f;
        float rootHeight = rootRect.rect.height > 1f ? rootRect.rect.height : 1080f;

        Vector2 panelPosition;
        if (hasTarget)
        {
            bool targetOnLeft = targetPosition.x < 0f;
            panelPosition = new Vector2(
                targetPosition.x + (targetOnLeft ? 1f : -1f) * (panelWidth * 0.5f + 92f),
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

        panelRect.anchoredPosition = panelPosition;
        PositionTutorialButtons(panelPosition, panelHeight);
        PositionCalloutArrow(panelPosition, new Vector2(panelWidth, panelHeight), targetPosition, hasTarget);
    }

    bool TryGetCalloutTarget(RectTransform rootRect, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        Vector2 screenPoint;
        if (calloutTarget != null && calloutTarget.gameObject.activeInHierarchy)
        {
            Vector3[] corners = new Vector3[4];
            calloutTarget.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[2]) * 0.5f;
            Canvas targetCanvas = calloutTarget.GetComponentInParent<Canvas>();
            Camera eventCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? targetCanvas.worldCamera : null;
            screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, center);
        }
        else if (highlightTarget != null && Camera.main != null)
        {
            Vector3 worldPoint = highlightTarget.position + Vector3.up * 1.8f;
            Vector3 projected = Camera.main.WorldToScreenPoint(worldPoint);
            if (projected.z <= 0f) return false;
            screenPoint = projected;
        }
        else
        {
            return false;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPoint, null, out localPoint);
    }

    void PositionTutorialButtons(Vector2 panelPosition, float panelHeight)
    {
        float y = panelPosition.y - panelHeight * 0.5f - 30f;
        PositionTutorialButton(nextButton, new Vector2(panelPosition.x, y));
    }

    static void PositionTutorialButton(Button button, Vector2 position)
    {
        if (button == null) return;
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
    }

    void PositionCalloutArrow(Vector2 panelPosition, Vector2 panelSize, Vector2 targetPosition, bool visible)
    {
        if (calloutArrow == null) return;
        Vector2 delta = targetPosition - panelPosition;
        if (!visible || delta.sqrMagnitude < 6400f)
        {
            calloutArrow.gameObject.SetActive(false);
            return;
        }

        Vector2 direction = delta.normalized;
        float tx = Mathf.Abs(direction.x) > 0.001f ? panelSize.x * 0.5f / Mathf.Abs(direction.x) : float.MaxValue;
        float ty = Mathf.Abs(direction.y) > 0.001f ? panelSize.y * 0.5f / Mathf.Abs(direction.y) : float.MaxValue;
        Vector2 start = panelPosition + direction * Mathf.Min(tx, ty);
        Vector2 end = targetPosition - direction * 16f;
        float length = Vector2.Distance(start, end);
        if (length < 24f)
        {
            calloutArrow.gameObject.SetActive(false);
            return;
        }

        calloutArrow.gameObject.SetActive(true);
        calloutArrow.anchorMin = calloutArrow.anchorMax = calloutArrow.pivot = new Vector2(0.5f, 0.5f);
        calloutArrow.anchoredPosition = (start + end) * 0.5f;
        calloutArrow.sizeDelta = new Vector2(length, 24f);
        calloutArrow.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        calloutArrow.SetAsLastSibling();
    }

    void RefreshAdvanceGate()
    {
        if (nextButton == null) return;
        bool ready = StepRequirementMet();
        nextButton.interactable = ready;
        var img = nextButton.GetComponent<Image>();
        if (img != null)
            img.color = ready ? new Color(0.22f, 0.55f, 0.38f, 1f) : new Color(0.22f, 0.24f, 0.28f, 1f);

        if (nextLabel != null && stepIndex >= 0 && stepIndex < Steps.Length)
        {
            var step = Steps[stepIndex];
            nextLabel.text = ready ? step.nextLabel : LockedLabel(step);
            if (ready && step.requirePlaced)
                UpdateHighlight(step.highlight);
        }
    }

    static string LockedLabel(Step step)
    {
        if (step.requireIngredients) return "Order Raw Patties and Buns";
        if (step.requireRecipes) return MissingRecipeLabel();
        if (step.requireEditedFlow) return "Edit and save flow";
        if (step.requireHiredWorker)
            return "Hire a worker";
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

    bool StepRequirementMet()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return true;
        var step = Steps[stepIndex];
        if (step.requireIngredients && !HasTutorialIngredients()) return false;
        if (step.requireRecipes && !BurgerRecipesReady()) return false;
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
        if (step.requireFlow && !HasTutorialFlow())
            return false;
        if (step.requireWorkerOnFlow && !HasWorkerOnFlow())
            return false;
        return true;
    }

    static bool AutomaticallyAdvances(Step step)
    {
        return step.liveCustomer || step.requirePlaced || step.requireAllStations
            || step.requireFlow || step.requireWorkerOnFlow || step.requireHiredWorker
            || step.requireEditedFlow || step.requireRecipes || step.requireIngredients;
    }

    void RefreshAutomaticAdvance()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        Step step = Steps[stepIndex];
        if (!AutomaticallyAdvances(step) || step.liveCustomer || !StepRequirementMet())
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
        if (IsActive && wasEdit && flow != null && flow.stations != null && flow.stations.Count >= 2)
            Instance.flowEditCompleted = true;
    }

    bool AllTutorialStationsPlaced()
    {
        // Follow the authored placement lessons. A removed/replaced station
        // must not remain a hidden requirement for the customer demonstration.
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
        if (liveCustomerSpawned) return;
        var existing = FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
        {
            liveCustomerSpawned = true;
            return;
        }

        if (CustomerWallDoor.FindEntryDoor() == null || !HasPlacedStation(Highlight.Register)) return;

        var spawner = FindFirstObjectByType<CustomerSpawner>();
        if (spawner != null)
            liveCustomerSpawned = spawner.SpawnTutorialCustomer();
    }

    void Complete(bool skipped)
    {
        running = false;
        pendingStart = false;
        currentHighlight = Highlight.None;
        PlayerPrefs.SetInt(PrefsCompleteKey, 1);
        PlayerPrefs.Save();

        HideUI();
        ClearHighlight();
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);

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

        // The automatic spawner was intentionally suppressed throughout onboarding.
        // Hand it an explicit restart so normal traffic resumes immediately instead
        // of waiting on the opening-hour or vehicle-arrival state.
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

    void RestoreStarterKitchen()
    {
        DestroyTutorialPlacements();
        for (int i = 0; i < starterKitchen.Count; i++)
        {
            if (starterKitchen[i] != null)
                starterKitchen[i].SetActive(true);
        }
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
            if (found[i] != null && !starterKitchen.Contains(found[i].gameObject))
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
            var col = highlight.GetComponent<Collider>();
            if (col != null)
                col.enabled = false;
            var rend = highlight.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    var mat = new Material(shader) { name = "OnboardingHighlight" };
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f, 0.85f));
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(1f, 0.85f, 0.2f, 0.85f));
                    rend.sharedMaterial = mat;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        highlight.SetActive(true);
        bool ui = kind == Highlight.Inventory || kind == Highlight.Management;
        highlight.transform.position = target.position + (ui ? Vector3.zero : Vector3.up * 2.2f);
        highlight.transform.localScale = Vector3.one * (ui ? 0.01f : 0.55f);
        if (ui)
            highlight.SetActive(false);
    }

    void ClearControlHighlights()
    {
        foreach (var border in tutorialBorders.Values)
            if (border != null) border.SetActive(false);
    }

    void HighlightControl(Component control)
    {
        if (control == null || !control.gameObject.activeInHierarchy) return;
        if (calloutTarget == null)
            calloutTarget = control.transform as RectTransform;
        var graphic = control.GetComponent<Graphic>();
        if (graphic == null) return;
        if (!tutorialBorders.TryGetValue(graphic, out var border) || border == null)
        {
            border = new GameObject("Tutorial Steady Border", typeof(RectTransform), typeof(LayoutElement));
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
                if (side < 2) { r.sizeDelta = new Vector2(0, 3); r.pivot = new Vector2(0.5f, side); }
                else { r.sizeDelta = new Vector2(3, 0); r.pivot = new Vector2(side - 2, 0.5f); }
            }
            tutorialBorders[graphic] = border;
        }
        border.SetActive(true);
        border.transform.SetAsLastSibling();
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

        if (!step.openWorkers) return;
        var workers = FindFirstObjectByType<WorkersUI>();
        if (workers != null)
        {
            if (step.requireHiredWorker && !HasHiredWorker()) HighlightControl(workers.hireButton);
            foreach (var button in workers.GetComponentsInChildren<Button>())
            {
                if (step.requireEditedFlow && !flowEditCompleted && button.name == "EditFlow"
                    || step.requireFlow && !step.requireEditedFlow && !HasTutorialFlow() && button.name == "CreateFlow"
                    || step.requireWorkerOnFlow && !HasWorkerOnFlow() && button.name == "WorkerDropZone")
                    HighlightControl(button);
                if (step.requireWorkerOnFlow && !HasWorkerOnFlow() && button.name == "WorkerDropZone")
                {
                    var flowPanel = button.transform.parent.GetComponentInParent<Image>();
                    if (flowPanel != null) HighlightControl(flowPanel);
                }
            }
            if (step.requireWorkerOnFlow && !HasWorkerOnFlow())
                foreach (var card in workers.GetComponentsInChildren<WorkerCardUI>())
                    if (card.employee != null) HighlightControl(card);
        }
        var management = ManagementModeController.Instance;
        if (management != null && management.IsCapturingFlow)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (button.name == "Finish" && button.interactable) HighlightControl(button);
        }
    }

    void Update()
    {
        if (!running)
        {
            ClearControlHighlights();
            if (pendingStart && !IsComplete)
                TryBegin();
            return;
        }
        RefreshAdvanceGate();
        RefreshControlHighlights();
        UpdateContextualCallout();
        if (Steps[stepIndex].liveCustomer && !liveCustomerSpawned)
            SpawnPracticeCustomer();
        RefreshAutomaticAdvance();

        if (highlight == null || !highlight.activeSelf || highlightTarget == null)
            return;
        float pulse = 0.5f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
        highlight.transform.position = highlightTarget.position + Vector3.up * 2.2f;
        highlight.transform.localScale = Vector3.one * pulse;
    }

    public static bool ShouldHighlightInventoryItem(ItemDefinition item)
    {
        if (item == null || Instance == null || !Instance.running || IsStationLocked(item))
            return false;
        return ItemMatchesHighlight(item, Instance.currentHighlight);
    }

    // Use the authored station steps so the unlock order follows the tutorial.
    // Non-station build items (such as counters) remain available.
    public static bool IsStationLocked(ItemDefinition item)
    {
        if (item == null || !BlocksProgression) return false;
        for (int i = 0; i < Steps.Length; i++)
        {
            if (Steps[i].requirePlaced && ItemMatchesHighlight(item, Steps[i].highlight))
                return i > Instance.furthestStepIndex;
        }
        return false;
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
                var reg = FindFirstObjectByType<Register>();
                return reg != null ? reg.transform : null;
            case Highlight.Inventory:
                var tabsInv = FindFirstObjectByType<MainHudTabs>();
                return tabsInv != null ? tabsInv.InventoryTabTransform : null;
            case Highlight.Management:
                var tabsMgmt = FindFirstObjectByType<MainHudTabs>();
                return tabsMgmt != null ? tabsMgmt.ManagementTabTransform : null;
            case Highlight.Freezer:
                return StationOrNull(FindFirstObjectByType<FreezerStation>());
            case Highlight.Grill:
                return StationOrNull(FindFirstObjectByType<GrillStation>());
            case Highlight.Fryer:
                return StationOrNull(FindFirstObjectByType<FryerStation>());
            case Highlight.Drink:
                return StationOrNull(FindFirstObjectByType<CuttingStation>());
            case Highlight.Assembly:
                return StationOrNull(FindFirstObjectByType<AssemblyStation>());
            case Highlight.HeatLamp:
                var lamp = HeatLampStation.Instance != null ? HeatLampStation.Instance : FindFirstObjectByType<HeatLampStation>();
                return StationOrNull(lamp);
            case Highlight.Pantry:
                return StationOrNull(FindFirstObjectByType<PantryStation>());
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
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
        else if (panel != null)
            panel.SetActive(false);
    }

    void EnsureUI()
    {
        if (canvasRoot != null && nextButton != null && nextButton.transform.parent == canvasRoot.transform)
        {
            canvasRoot.SetActive(true);
            if (panel != null) panel.SetActive(true);
            return;
        }

        if (canvasRoot != null)
            Destroy(canvasRoot);

        canvasRoot = new GameObject("OnboardingTutorialCanvas", typeof(RectTransform));
        canvasRoot.transform.SetParent(transform, false);
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
        rt.sizeDelta = new Vector2(720f, 0f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        bg.raycastTarget = true;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 18);
        layout.spacing = 10f;
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
        headerLayout.spacing = 14f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        header.GetComponent<LayoutElement>().preferredHeight = 76f;

        var iconFrame = new GameObject("GusIconFrame", typeof(RectTransform), typeof(Image),
            typeof(LayoutElement));
        iconFrame.transform.SetParent(header.transform, false);
        iconFrame.GetComponent<Image>().color = GameUITheme.Surface;
        var iconLayout = iconFrame.GetComponent<LayoutElement>();
        iconLayout.minWidth = 72f;
        iconLayout.preferredWidth = 72f;
        iconLayout.minHeight = 72f;
        iconLayout.preferredHeight = 72f;
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

        stepLabel = CreateLabel(headerText.transform, "", 14, FontStyles.Bold, GameUITheme.Accent);
        stepLabel.alignment = TextAlignmentOptions.Left;
        titleText = CreateLabel(headerText.transform, "", 24, FontStyles.Bold, Color.white);
        titleText.alignment = TextAlignmentOptions.Left;
        bodyText = CreateLabel(panel.transform, "", 17, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;

        nextButton = CreateSideButton(canvasRoot.transform, "Next", OnNext, new Color(0.22f, 0.55f, 0.38f, 1f), 1);
        nextLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();
        calloutArrow = CreateCalloutArrow(canvasRoot.transform);
    }

    static RectTransform CreateCalloutArrow(Transform parent)
    {
        var root = new GameObject("TutorialArrow", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(80f, 24f);
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var shaft = new GameObject("Shaft", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        shaft.transform.SetParent(root.transform, false);
        var shaftRect = (RectTransform)shaft.transform;
        shaftRect.anchorMin = new Vector2(0f, 0.5f);
        shaftRect.anchorMax = new Vector2(1f, 0.5f);
        shaftRect.offsetMin = new Vector2(0f, -3f);
        shaftRect.offsetMax = new Vector2(-12f, 3f);
        var shaftImage = shaft.GetComponent<UnityEngine.UI.Image>();
        shaftImage.color = GameUITheme.Accent;
        shaftImage.raycastTarget = false;

        CreateArrowHead(root.transform, "HeadUpper", 38f);
        CreateArrowHead(root.transform, "HeadLower", -38f);
        root.SetActive(false);
        return rootRect;
    }

    static void CreateArrowHead(Transform parent, string name, float rotation)
    {
        var head = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        head.transform.SetParent(parent, false);
        var rect = (RectTransform)head.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(20f, 6f);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        var image = head.GetComponent<UnityEngine.UI.Image>();
        image.color = GameUITheme.Accent;
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
        tmp.fontSize = 20;
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
