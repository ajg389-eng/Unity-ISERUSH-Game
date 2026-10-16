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
    public static bool IsResearchTutorialActive => IsCurrentStep(step => step.requireResearch);
    public static bool IsRecipeTutorialActive => IsCurrentStep(step => step.requireRecipeScreen);
    public static bool IsAnalyticsTutorialActive => IsCurrentStep(step => step.requireAnalyticsScreen);
    public static bool IsMenuTutorialActive => IsCurrentStep(step => step.requireMenuScreen);

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
        if (IsAnalyticsTutorialActive) Instance.analyticsScreenOpened = true;
    }

    public static void NotifyMenuScreenOpened()
    {
        if (IsMenuTutorialActive) Instance.menuScreenOpened = true;
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
    bool starterKitchenHidden;
    int stepIndex;
    public int SaveStepIndex => stepIndex;
    public bool OrderServedForSave => orderServed;

    public void RestoreCheckpoint(bool complete, int savedStep, bool savedOrderServed = false)
    {
        PlayerPrefs.SetInt(PrefsCompleteKey, complete ? 1 : 0);
        stepIndex = Mathf.Clamp(savedStep, 0, Steps.Length - 1);
        furthestStepIndex = stepIndex;
        if (stepIndex == 0)
            ResetCameraTutorialInputs();
        orderServed = savedOrderServed;
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
    bool recipeBusinessOpened;
    bool researchScreenOpened;
    bool recipeScreenOpened;
    bool analyticsScreenOpened;
    bool menuScreenOpened;
    bool orderServed;
    bool tutorialReviewShown;
    bool cleanedOldWorldCallout;
    bool tutorialCameraMoved;
    bool tutorialCameraRotated;
    bool tutorialCameraZoomed;
    int recipeTutorialStationIndex;
    int ingredientTutorialStep;
    float ingredientExplanationUntil;
    string tutorialFocusText;
    string tutorialTextBeingRevealed;
    int tutorialVisibleCharacters;
    int tutorialTotalCharacters;
    float tutorialCharacterProgress;
    float nextTutorialVoiceTime;
    AudioSource tutorialTypewriterVoiceSource;
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
    GameObject cameraPromptPanel;
    TextMeshProUGUI cameraMovePromptStatus;
    TextMeshProUGUI cameraRotatePromptStatus;
    TextMeshProUGUI cameraZoomPromptStatus;
    RectTransform calloutTarget;
    RectTransform calloutArrow;
    GameObject highlight;
    Transform highlightTarget;
    Transform secondaryHighlightTarget;
    GameObject secondaryHighlight;
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
        public bool requireStationSequence;
        public bool openResearch;
        public bool requireResearch;
        public bool openRecipes;
        public bool requireRecipeScreen;
        public bool openAnalytics;
        public bool requireAnalyticsScreen;
        public bool requireMenuScreen;
        public bool requireHamburger;
        public bool requireRegisterEnabled;
        public bool requireOrderServed;
        public bool requireCameraControls;

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
            bool requireHamburger = false,
            bool requireOrderServed = false,
            bool requireRegisterEnabled = false,
            bool requireCameraControls = false)
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
            this.requireStationSequence = requireStationSequence;
            this.openResearch = openResearch;
            this.requireResearch = requireResearch;
            this.openRecipes = openRecipes;
            this.requireRecipeScreen = requireRecipeScreen;
            this.openAnalytics = openAnalytics;
            this.requireAnalyticsScreen = requireAnalyticsScreen;
            this.requireMenuScreen = requireMenuScreen;
            this.requireHamburger = requireHamburger;
            this.requireOrderServed = requireOrderServed;
            this.requireRegisterEnabled = requireRegisterEnabled;
            this.requireCameraControls = requireCameraControls;
        }
    }

    static readonly Step[] Steps =
    {
        new Step(
            "Move around the kitchen",
            "Before we start building, get comfortable looking around. Use <b>WASD</b> to move the camera, hold <b>right mouse</b> and drag to turn the view, then use the <b>scroll wheel</b> to zoom. Try each control and I will wait here until you have.",
            "Next", Highlight.None, requireCameraControls: true),
        new Step(
            "Empty kitchen",
            "This shift starts with an <b>empty kitchen</b>. Let's build it one step at a time.\n\n" +
            "You will buy each workstation, stock ingredients, then serve one customer. Use <b>Back</b> and <b>Next</b> to move through the steps.",
            "Next", Highlight.None),
        new Step(
            "Buy stations",
            "Open <b>Build</b> (top-left, or press 1). Each station's <b>first copy is free</b>. Click Buy, then click a floor tile to place it.\n\n" +
            "Place stations in the kitchen, not the lobby. You can rotate while placing if the ghost shows a facing arrow. Shorter routes reduce worker travel and help the line keep up.\n\n" +
            "Stations unlock one at a time as you reach their tutorial section. Previously introduced stations stay available. Next stays locked until the required station is on the floor.",
            "Next", Highlight.Inventory),
        new Step(
            "Register",
            "Customers enter and line up at the <b>register</b>. They order a burger, fries, drink, or combo here. The queue grows when arrivals outpace your service capacity.\n\n" +
            "Open <b>Build</b> and buy your free <b>register</b>. Placement starts automatically: click a free <b>counter</b> on the lobby side.\n\n" +
            "Keep the queue area clear. After placing it, click the register and choose <b>Enable</b>. Next unlocks after the register is both placed and enabled.",
            "Next", Highlight.Register, openInventory: true, requirePlaced: true, requireRegisterEnabled: true),
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
            "Research the three foundations",
            "Before we choose a recipe, open <b>Research</b>. Gus needs you to complete the first card in each branch: Restaurant Basics, Core Workforce, and Manual Operations. Each tutorial card finishes in one second.",
            "Next", Highlight.None, requireResearch: true),
        new Step(
            "Open the recipe book",
            "The three foundations are complete. Open the <b>Recipes</b> screen and look through the Burger, Fries, and Shake production trees before we configure the stations.",
            "Next", Highlight.None, requireRecipeScreen: true),
        new Step(
            "Configure ingredients and recipes",
            "Click the highlighted station in the kitchen. Gus will point out the exact choice to make in its station panel. We will configure each station one at a time.",
            "Next", Highlight.Management, openWorkers: true, requireRecipes: true, requireStationSequence: true),
        new Step(
            "Buy ingredients",
            "Stations do nothing without stock. Open <b>Business</b> (top-left, or press 2), then choose <b>Supply</b>.\n\n" +
            "Buy Raw Patties and Buns for the burger line. Packs spend cash. A delivery person brings them in through the front door after a short wait. Potatoes are used later for fries.",
            "Next", Highlight.Management, openIngredients: true, requireIngredients: true, requireStationSequence: true),
        new Step(
            "Hire workers",
            "Stations only cook if people work a <b>flow</b>. Open <b>Business > Staff</b> (top-left, or press 2).\n\n" +
            "Click <b>Hire</b> at the top of the Workers tab to add staff. Each hire costs money. Workers use their priorities to choose jobs within their assigned flow; unassigned workers remain idle.\n\n" +
            "Hire at least one worker to continue.",
            "Next", Highlight.Management, openWorkers: true, requireHiredWorker: true, requireStationSequence: true),
        new Step(
            "Create a flow",
            "Start by clicking <b>Create Flow</b>. Gus will guide you through each connection in the kitchen. Drag from the source station to the destination station, then release.",
            "Next", Highlight.Management, openWorkers: true, requireFlow: true, requireStationSequence: true),
        new Step(
            "Choose what to sell",
            "Click the highlighted <b>Menu</b> tab. Every item starts turned off so you choose exactly what customers can order. Enable <b>Hamburger</b> to make the burger line available.",
            "Next", Highlight.Management, requireMenuScreen: true, requireHamburger: true),
        new Step(
            "Edit a flow",
            "Select the flow chip at the top of Workers, then click <b>Edit Flow</b>.\n\n" +
            "Drag to add a connection. Click selects a station; Ctrl-click removes it and its connections. Press Esc to cancel edits.\n\n" +
            "Edit an existing flow and click Finish to save it before continuing. Later, use Edit to correct a station or build a second line (fryer → Pickup Station).",
            "Next", Highlight.Management, openWorkers: true, requireEditedFlow: true, requireStationSequence: true),
        new Step(
            "Assign workers to a flow",
            "Select your burger flow, then <b>drag a worker card onto DROP WORKER</b> in the flow panel.\n\n" +
            "That worker's name appears on the flow. Click the name chip to unassign. The kitchen will not cook until at least one worker is on a flow.\n\n" +
            "Next stays locked until a flow has a worker assigned.",
            "Next", Highlight.Management, openWorkers: true, requireWorkerOnFlow: true, requireStationSequence: true),
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
            "Serve that order, then continue to Analytics. Extra customers stay away until the tutorial ends.",
            "Next", Highlight.Register, liveCustomer: true, requireAllStations: true, requireFlow: true, requireWorkerOnFlow: true, requireOrderServed: true),
        new Step(
            "Review analytics",
            "You have seen the complete service loop. Click the highlighted <b>Analytics</b> button on the HUD to inspect demand, queues, throughput, and utilization after the first order.",
            "Finish", Highlight.None, requireAnalyticsScreen: true),
    };

    // Gus speaks the tutorial while the Step data continues to define gameplay gates.
    static readonly string[] GusDialogue =
    {
        "Before we start building, get comfortable looking around. Use WASD to move the camera, hold the right mouse button and drag to turn the view, then use the scroll wheel to zoom. Try each control and I will wait here until you have.",

        "I cleared out the kitchen so we can rebuild the operation properly, one step at a time.\n\n" +
        "I will walk you through the stations, ingredients, and staffing, then we will test your system with one customer.",

        "Open <b>Build</b> at the top-left, or press <b>1</b>. I covered the first copy of each station, so those are free. Click Buy, then choose a kitchen floor tile.\n\n" +
        "Keep equipment out of the lobby. You can rotate a station while its placement ghost is visible.",

        "Every order begins at the <b>register</b>. Open <b>Build</b>, take the free register, then click an open <b>counter</b> tile on the lobby side.\n\n" +
        "Keep some floor space clear for the customer line. After placing it, click the register and choose <b>Enable</b> so customers can order.",

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

        "Before we choose a recipe, open <b>Research</b>. Complete the first card in each branch: Restaurant Basics, Core Workforce, and Manual Operations. These first cards finish in one second for the tutorial.",

        "The foundations are complete. Open the <b>Recipes</b> screen and inspect the Burger, Fries, and Shake production trees. We will configure the burger line next.",

        "Select the source stations under <b>Business > Staff</b>. Set the <b>Freezer to Raw Patty</b> and the <b>Pantry to Bun</b>, then choose the Hamburger recipe at Assembly.\n\n" +
        "Set <b>Grill to Cooked Patty</b> too. Click each station outside Create/Edit Flow to select its ingredient or recipe. Next waits for all four settings. A warning above a station means its selection is missing.",

        "The equipment is useless without material to process. Open <b>Business</b>, or press <b>2</b>, and choose <b>Supply</b>.\n\n" +
        "Order at least one pack of <b>Raw Patties</b> and <b>Buns</b> for the burger line. A delivery person will bring the combined order through the front door. Potatoes are for the later fries line.",

        "Now we need someone to run the process. Open <b>Business > Staff</b>, or press <b>2</b>, and click <b>Hire</b>. Each employee costs money, so staffing is a capacity decision.\n\n" +
        "Transport upgrades raise a worker's trained limit up to 120 items per minute. Route travel lowers their delivery rate, and workers not assigned to a flow remain idle.",

        "Let's define how work should move. In Workers, click <b>Create Flow</b>, then drag from each source station to its destination.\n\n" +
        "In ONE flow, drag Freezer to Grill, Grill to Assembly, Pantry to Assembly, and Assembly to Pickup Station. Release on each destination and click Finish. The pantry supplies buns to the same assembly station.",

        "Now choose the product customers can order. Open <b>Business</b>, click the <b>Menu</b> tab, and enable <b>Hamburger</b>.\n\n" +
        "Every sellable item starts turned off. That keeps your menu intentional and prevents customers from ordering a recipe before its line is ready.",

        "Plans change, so you need to know how to revise one. Select the flow chip, then click <b>Edit Flow</b>.\n\n" +
        "Drag to connect stations. Click selects a station; Ctrl-click removes it and its connections. Check the route, then click Finish to save it.",

        "A process plan does nothing until someone owns the work. Select the burger flow, then <b>drag a worker card onto DROP WORKER</b>.\n\n" +
        "Their name will appear on the flow. Assign at least one worker so production can begin.",

        "Here is the system I need you to observe:\n\n" +
        "1. A customer orders at the register.\n2. Workers follow the flows you designed.\n3. Finished items wait at a Pickup Station.\n4. The customer collects each item and leaves.\n\n" +
        "Waiting, walking, and blocked stations are all clues.",

        "I am opening the doors for one test customer. They will order <b>one burger</b>, so we can watch the full flow without a crowd hiding the problems.\n\n" +
        "If production stops, check the worker assignment, station sequence, and ingredient stock. Serve the order, then continue to Analytics.",

        "You have completed the first service loop. Click the highlighted <b>Analytics</b> button on the HUD to read demand, queues, throughput, and utilization."
    };

    static readonly string[] CompactTutorialText =
    {
        "Use <b>WASD</b> to move around. Hold <b>right mouse</b> and drag to turn the camera, then scroll to zoom. Try all three controls before continuing.",
        "Build a small burger line, connect it, and serve one test customer.",
        "Open <b>Build</b>. The highlighted MK1 station is free for this tutorial.",
        "Buy the <b>Register</b>, place it on a lobby counter, then click it and choose <b>Enable</b>.",
        "Buy a <b>Freezer</b>. You will assign Raw Patty after placement.",
        "Buy a <b>Grill</b>. It turns Raw Patty into Cooked Patty.",
        "Buy a <b>Fryer</b>. Fries use Potato Slices, then combine Cooked Potato Slices with a Fry Container at Assembly.",
        "Buy a <b>Pantry</b> for Buns. Milk, lettuce, and tomatoes come from Freezers.",
        "Buy an <b>Assembly Station</b>. Its selected recipe determines its required inputs.",
        "Buy a <b>Pickup Station</b> and <b>click a counter slot to place it</b>. Only finished products delivered here are sold.",
        "In <b>Research</b>, complete the first card in each of the three branches. Each first card finishes in one second during the tutorial.",
        "Open <b>Recipes</b> and inspect the Burger, Fries, and Shake production trees before configuring a station.",
        "In <b>Business > Staff</b>, set Freezer to Raw Patty, Grill to Cooked Patty, Pantry to Bun, and Assembly to Hamburger.",
        "In <b>Food</b>, order Raw Patties and Buns. Express costs $200 and makes the delivery van arrive immediately.",
        "In <b>Staff</b>, hire one worker. Worker priorities can favor specific task types.",
        "Create one flow: Freezer > Grill > Assembly, Pantry > Assembly, then Assembly > Pickup Station.",
        "Open <b>Business > Menu</b> and enable <b>Hamburger</b>. All sellable items start off until you choose them.",
        "Use <b>Edit Flow</b> to adjust connections, then click Finish.",
        "Assign a worker using the highlighted drop area. The worker can handle every station in the flow.",
        "Customers request a burger and pay when the ordered burger reaches the Pickup Station. Workers keep the line moving, but only stage food that a waiting customer ordered.",
        "Watch your worker prepare a <b>burger</b> and deliver it to the <b>Pickup Station</b>. The customer will collect the order and leave.",
        "Open <b>Analytics</b> to review demand, queues, throughput, and utilization, then click Finish."
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
        PlayerPrefs.DeleteKey(PrefsCompleteKey);
        PlayerPrefs.Save();
        pendingStart = true;
        running = false;
        stepIndex = 0;
        furthestStepIndex = -1;
        flowEditCompleted = false;
        liveCustomerSpawned = false;
        researchScreenOpened = false;
        recipeScreenOpened = false;
        analyticsScreenOpened = false;
        menuScreenOpened = false;
        orderServed = false;
        ResetCameraTutorialInputs();
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
        if (eventId != TutorialVoiceEventId.FirstOrderServed && eventId != TutorialVoiceEventId.OrderServed)
            return;

        // Keep this latched even if the event arrives during a step transition
        // or immediately before a save restore. Serving is a gameplay fact, not
        // a fact that should depend on the current tutorial panel timing.
        orderServed = true;
        if (!running) return;
        if (stepIndex >= 0 && stepIndex < Steps.Length && Steps[stepIndex].requireOrderServed)
        {
            RefreshAdvanceGate();
            UpdateContextualCallout();
            return;
        }
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

        if (tutorialVisibleCharacters < tutorialTotalCharacters)
        {
            tutorialVisibleCharacters = tutorialTotalCharacters;
            tutorialCharacterProgress = tutorialTotalCharacters;
            if (bodyText != null)
                bodyText.maxVisibleCharacters = tutorialVisibleCharacters;
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
        recipeTutorialStationIndex = 0;
        recipeBusinessOpened = false;
        ingredientTutorialStep = 0;
        ingredientExplanationUntil = 0f;
        tutorialFocusText = null;
        if (!step.requireMenuScreen)
            menuScreenOpened = false;
        furthestStepIndex = Mathf.Max(furthestStepIndex, stepIndex);

        if (canvasRoot != null)
            canvasRoot.SetActive(true);
        if (panel != null)
            panel.SetActive(true);
        if (cameraPromptPanel != null)
            cameraPromptPanel.SetActive(step.requireCameraControls);
        if (titleText != null)
            titleText.text = step.title;
        if (bodyText != null)
            bodyText.text = !string.IsNullOrEmpty(tutorialFocusText) ? tutorialFocusText
                : stepIndex < CompactTutorialText.Length ? CompactTutorialText[stepIndex] : step.body;
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

        if (!step.openResearch)
            ResearchTreeUI.CloseIfOpen();
        if (!step.openRecipes)
        {
            var recipes = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
            if (recipes != null) recipes.CloseRecipesFromHud();
        }
        if (!step.openAnalytics)
            AnalyticsPopupUI.CloseIfOpen();

        if (stepIndex == 2)
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

        if (step.requireRecipes)
        {
            var inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (inventoryUI != null && inventoryUI.IsPanelOpen) inventoryUI.TogglePanel();
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management != null && management.IsOpen) management.Close();
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
        bool hasTarget = TryGetCalloutTarget(rootRect, out targetPosition);

        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        float panelWidth = panelRect.rect.width > 1f ? panelRect.rect.width : 560f;
        float panelHeight = panelRect.rect.height > 1f ? panelRect.rect.height : 180f;
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

        // Top-bar callouts sit beneath the navigation, leaving its labels readable.
        if (hasTarget && targetPosition.y > rootHeight * 0.5f - 180f
            && (currentHighlight == Highlight.Inventory || currentHighlight == Highlight.Management))
            panelPosition.y = targetPosition.y - panelHeight * 0.5f - 60f;

        // Keep the entire flow editor readable, not just the highlighted button.
        if (hasTarget && calloutTarget != null)
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
        // The final step is for watching the kitchen, rather than pointing at a control.
        if (running && Steps[stepIndex].liveCustomer)
        {
            hasTarget = false;
            panelPosition = new Vector2(rootWidth * 0.5f - panelWidth * 0.5f - 18f,
                -rootHeight * 0.5f + panelHeight * 0.5f + 18f);
        }
        // Apply screen bounds after every contextual adjustment, including bottom-row targets.
        panelPosition.x = Mathf.Clamp(panelPosition.x,
            -rootWidth * 0.5f + panelWidth * 0.5f + 18f,
            rootWidth * 0.5f - panelWidth * 0.5f - 18f);
        panelPosition.y = Mathf.Clamp(panelPosition.y,
            -rootHeight * 0.5f + panelHeight * 0.5f + 18f,
            rootHeight * 0.5f - panelHeight * 0.5f - 18f);
        panelRect.anchoredPosition = panelPosition;
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

    string LockedLabel(Step step)
    {
        if (step.highlight == Highlight.Inventory) return "Open Build";
        if (step.requireResearch) return "Research all three foundations";
        if (step.requireRecipeScreen) return "Open Recipes";
        if (step.requireAnalyticsScreen) return "Open Analytics";
        if (step.requireMenuScreen && !menuScreenOpened) return "Open Menu";
        if (step.requireHamburger) return "Enable Hamburger";
        if (step.requireCameraControls)
        {
            if (!tutorialCameraMoved) return "Move camera with WASD";
            if (!tutorialCameraRotated) return "Rotate with right mouse + drag";
            if (!tutorialCameraZoomed) return "Zoom with scroll wheel";
        }
        if (step.requireRegisterEnabled)
            return HasPlacedStation(step.highlight) ? "Enable the register" : "Place register on counter";
        if (step.requireOrderServed) return "Serve the burger";
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

    static bool IsTutorialRegisterEnabled()
    {
        Register register = FindTutorialRegister();
        return register != null && register.isEnabled;
    }

    bool StepRequirementMet()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return true;
        var step = Steps[stepIndex];
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
        if (step.requireAnalyticsScreen && !analyticsScreenOpened) return false;
        if (step.requireMenuScreen && !menuScreenOpened) return false;
        if (step.requireHamburger && !IsHamburgerEnabled()) return false;
        if (step.requireRegisterEnabled && !IsTutorialRegisterEnabled()) return false;
        if (step.requireOrderServed && !orderServed) return false;
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
        if (step.requireFlow && !HasTutorialFlow())
            return false;
        if (step.requireWorkerOnFlow && !HasWorkerOnFlow())
            return false;
        return true;
    }

    static bool AutomaticallyAdvances(Step step)
    {
        return (step.liveCustomer && !step.requireOrderServed) || step.requirePlaced || step.requireAllStations
            || step.requireFlow || step.requireWorkerOnFlow || step.requireHiredWorker
            || step.requireEditedFlow || step.requireRecipes || step.requireIngredients
            || step.requireResearch || step.requireHamburger || step.requireRegisterEnabled
            || step.requireOrderServed;
    }

    void RefreshAutomaticAdvance()
    {
        if (stepIndex < 0 || stepIndex >= Steps.Length) return;
        Step step = Steps[stepIndex];
        // The live-customer step waits for the order-served event, then advances
        // automatically. Other live-customer steps still remain manual.
        bool waitingForServedOrder = step.liveCustomer && !step.requireOrderServed;
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
        if (!skipped && !tutorialReviewShown)
        {
            tutorialReviewShown = true;
            ShowTutorialReview();
        }
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

    void ShowTutorialReview()
    {
        StoreStatisticsManager stats = StoreStatisticsManager.Instance;
        int served = stats != null ? stats.OrdersCompletedToday : 0;
        float wait = stats != null ? stats.AverageWaitTimeTodaySeconds : 0f;
        int waste = stats != null ? stats.MealsWastedToday : 0;
        string next = served == 0
            ? "Next: check that your flow has a worker and that ingredients are in stock."
            : wait > 35f
                ? "Next: shorten the route or add capacity at the busiest station."
                : waste > 0
                    ? "Next: connect the final station to Pickup and watch delivery follow waiting orders."
                    : "Next: add capacity and watch your kitchen keep pace as more customers arrive.";
        NotificationCenter.Post("Tutorial complete. You served " + served + " order" + (served == 1 ? "" : "s") +
            "; average wait " + wait.ToString("F0") + "s; wasted food " + waste + ". " + next,
            GameNotificationKind.Message, "tutorial-review", 14f);
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

    void EnsureSecondaryHighlight()
    {
        if (secondaryHighlight != null) return;
        secondaryHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        secondaryHighlight.name = "OnboardingSecondaryHighlight";
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

        if (step.requireRegisterEnabled && !IsTutorialRegisterEnabled())
        {
            Register register = FindTutorialRegister();
            if (register != null)
            {
                highlightTarget = register.transform;
                RegisterPopup popup = FindFirstObjectByType<RegisterPopup>(FindObjectsInactive.Exclude);
                if (popup != null && popup.toggleButton != null
                    && popup.toggleButton.gameObject.activeInHierarchy)
                {
                    tutorialFocusText = "Click <b>Enable</b> on the register popup to let customers order.";
                    if (bodyText != null) bodyText.text = tutorialFocusText;
                    HighlightControl(popup.toggleButton);
                }
                else
                {
                    tutorialFocusText = "Click the placed <b>register</b>, then click <b>Enable</b>.";
                    if (bodyText != null) bodyText.text = tutorialFocusText;
                }
            }
            return;
        }

        if (step.requireMenuScreen && !menuScreenOpened)
        {
            ManagementScreenController menuManagement = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            // Open the Business panel so the tutorial can point directly at the
            // action this step requires. The player should not have to guess
            // that Business is only an intermediate click before Menu.
            if (menuManagement != null && !menuManagement.IsOpen)
                menuManagement.Open();
            tutorialFocusText = "Click the <b>Menu</b> tab to choose what items to sell.";
            if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
            HighlightHudButton("Tab_Menu");
            return;
        }
        if (step.requireHamburger && !IsHamburgerEnabled())
        {
            IngredientsOrderUI ingredients = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
            CustomerOrderConfig config = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : null;
            Toggle target = ingredients != null && config != null
                ? ingredients.TutorialMenuToggle(config.burgerBase) : null;
            if (target != null)
            {
                tutorialFocusText = "Click the <b>Hamburger</b> toggle to add it to the menu.";
                if (bodyText != null) bodyText.text = tutorialFocusText;
                // This step replaces the earlier Business-tab target with the
                // actual menu toggle. Focus explicitly so both the border and
                // arrow follow the action the player needs to take.
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
        if (step.requireAnalyticsScreen && !analyticsScreenOpened)
        {
            HighlightHudButton("AnalyticsButton");
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
        if (step.requireRecipes && recipeTutorialStationIndex < 4)
        {
            Component station = GetRecipeTutorialStation(recipeTutorialStationIndex);
            ManagementModeController manager = ManagementModeController.Instance;
            if (station != null && manager != null && manager.TutorialSelectedStation == station.gameObject
                && manager.stationPopup != null && manager.stationPopup.activeInHierarchy)
            {
                string target = recipeTutorialStationIndex switch
                {
                    0 => "Raw Patty",
                    1 => "Cooked Patty",
                    2 => "Bun",
                    _ => "Hamburger",
                };
                foreach (Button button in manager.stationPopup.GetComponentsInChildren<Button>(true))
                {
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (label != null && label.text.IndexOf(target, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        HighlightControl(button);
                        break;
                    }
                }
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
            if (pendingStart && !IsComplete)
                TryBegin();
            return;
        }
        TrackCameraTutorialInputs();
        RefreshCameraPromptStatus();
        RefreshAdvanceGate();
        RefreshControlHighlights();
        UpdateContextualCallout();
        UpdateTutorialWorldHighlights();
        if (Steps[stepIndex].liveCustomer && !liveCustomerSpawned)
            SpawnPracticeCustomer();
        RefreshAutomaticAdvance();
        UpdateTutorialTextReveal();

        if (highlight == null || !highlight.activeSelf || highlightTarget == null)
            return;
        float pulse = 0.5f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
        highlight.transform.position = highlightTarget.position + Vector3.up * 2.2f;
        highlight.transform.localScale = Vector3.one * pulse;
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

    void UpdateTutorialWorldHighlights()
    {
        if (running && stepIndex >= 0 && stepIndex < Steps.Length
            && Steps[stepIndex].requireRegisterEnabled && !IsTutorialRegisterEnabled())
        {
            RegisterPopup registerPopup = FindFirstObjectByType<RegisterPopup>(FindObjectsInactive.Exclude);
            if (registerPopup != null && registerPopup.toggleButton != null
                && registerPopup.toggleButton.gameObject.activeInHierarchy)
            {
                // The yellow world marker sits over this floating button from
                // the camera angle. The control border and callout already point
                // to it, so suppress the filled marker while Enable is shown.
                if (highlight != null) highlight.SetActive(false);
                if (secondaryHighlight != null) secondaryHighlight.SetActive(false);
                return;
            }
        }
        if (!running || stepIndex < 0 || stepIndex >= Steps.Length || highlightTarget == null)
        {
            if (secondaryHighlight != null) secondaryHighlight.SetActive(false);
            return;
        }
        if (highlight != null)
        {
            highlight.SetActive(true);
            float markerPulse = 0.48f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.22f);
            highlight.transform.position = highlightTarget.position + Vector3.up * 2.2f;
            highlight.transform.localScale = Vector3.one * markerPulse;
        }
        if (secondaryHighlightTarget != null)
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

    void UpdateStationSequenceStep(Step step)
    {
        if (step.requireRecipes && !recipeBusinessOpened)
        {
            var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (management == null || !management.IsOpen)
            {
                tutorialFocusText = "Click <b>Business</b> at the top to start configuring ingredients and recipes.";
                if (bodyText != null) bodyText.text = tutorialFocusText;
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
                tutorialFocusText = "Click the highlighted <b>" + station.name.Replace("(Clone)", "").Trim() + "</b> in the kitchen. Gus will show you its recipe choice.";
                if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
                return;
            }

            tutorialFocusText = GetRecipeTutorialPrompt(recipeTutorialStationIndex, station.name.Replace("(Clone)", "").Trim());
            if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
            if (IsRecipeTutorialStationConfigured(recipeTutorialStationIndex, station))
            {
                recipeTutorialStationIndex++;
                if (recipeTutorialStationIndex >= 4)
                {
                    tutorialFocusText = "All four recipe settings are ready. Freezer = Raw Patty, Grill = Cooked Patty, Pantry = Bun, Assembly = Hamburger.";
                    if (bodyText != null) bodyText.text = tutorialFocusText;
                    secondaryHighlightTarget = null;
                    return;
                }
                station = GetRecipeTutorialStation(recipeTutorialStationIndex);
                if (station == null) return;
                tutorialFocusText = "Good. Now click the highlighted <b>" + station.name.Replace("(Clone)", "").Trim() + "</b> and choose its burger-line item or recipe.";
                if (bodyText != null) bodyText.text = tutorialFocusText;
            }
            highlightTarget = station.transform;
            calloutTarget = null;
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
                string name = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;
                if (ingredients.TutorialCartPacks(item) > 0)
                {
                    ingredientTutorialStep++;
                    if (ingredientTutorialStep == 2)
                    {
                        ingredientExplanationUntil = Time.unscaledTime + 3f;
                        tutorialFocusText = "A burger needs a patty and a bun. The green <b>+</b> adds a pack and the gray <b>−</b> removes one. <b>Add All</b> adds one pack of every supply; <b>Clear</b> empties the cart.";
                        calloutTarget = null;
                        if (bodyText != null) bodyText.text = tutorialFocusText;
                    }
                    return;
                }

                Button add = ingredients.TutorialAddButton(item);
                tutorialFocusText = "In <b>Supplies</b>, click the green <b>+</b> beside <b>" + name +
                    "</b> to add one pack. The gray <b>−</b> beside it removes one pack.";
                FocusTutorialControl(add);
                return;
            }

            if (ingredientTutorialStep == 2)
            {
                if (string.IsNullOrEmpty(tutorialFocusText))
                {
                    tutorialFocusText = "A burger needs a patty and a bun. The green <b>+</b> adds a pack and the gray <b>−</b> removes one. <b>Add All</b> adds one pack of every supply; <b>Clear</b> empties the cart.";
                    ingredientExplanationUntil = Time.unscaledTime + 3f;
                    if (bodyText != null) bodyText.text = tutorialFocusText;
                }
                calloutTarget = null;
                if (Time.unscaledTime < ingredientExplanationUntil) return;
                ingredientTutorialStep = 3;
                tutorialFocusText = null;
            }

            Button placeOrder = ingredients.TutorialPlaceOrderButton;
            tutorialFocusText = "Click <b>Place Order</b> to send the cart by regular delivery. <b>Express</b> costs $200 and arrives immediately.";
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
                var workers = FindFirstObjectByType<WorkersUI>();
                tutorialFocusText = "Flow tabs select a saved workflow. <b>Edit</b> changes the selected route, <b>+ Flow</b> creates a new one, and <b>Delete</b> removes the selected flow. Click <b>+ Flow</b> to continue.";
                if (workers != null)
                    foreach (Button button in workers.GetComponentsInChildren<Button>(true))
                        if (button != null && button.name == "CreateFlow") { FocusTutorialControl(button); break; }
                if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
                return;
            }

            var flow = manager.CapturedFlow;
            var expected = GetExpectedFlowLink(flow);
            if (expected.source != null && expected.destination != null)
            {
                highlightTarget = expected.source.transform;
                secondaryHighlightTarget = expected.destination.transform;
                tutorialFocusText = "<b>Next link:</b> click and drag from the highlighted <b>" + expected.source.name.Replace("(Clone)", "").Trim() +
                    "</b> to the highlighted <b>" + expected.destination.name.Replace("(Clone)", "").Trim() + "</b>, then release.";
                if (expected.source.GetComponent<PantryStation>() != null)
                    tutorialFocusText = "Start a separate <b>branch</b> from <b>Pantry</b>: click and drag to the existing <b>Assembly</b> station, then release to merge it into the same flow.";
                else if (expected.source.GetComponent<AssemblyStation>() != null)
                    tutorialFocusText = "Bring the flow to the <b>Pickup Station</b>: click and drag from <b>Assembly</b> to <b>Pickup Station</b>, then release.";
                if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
            }
            else if (HasTutorialFlow())
            {
                tutorialFocusText = "Flow connected. Click <b>Finish</b> to save it.";
                if (manager.flowCaptureFinishButton != null)
                    FocusTutorialControl(manager.flowCaptureFinishButton);
                if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
            }
            else
            {
                tutorialFocusText = "Drag from the highlighted source station to its destination. The pointer will move to the next connection after each link.";
                if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
            }
        }

        if (step.requireWorkerOnFlow && !HasWorkerOnFlow())
        {
            tutorialFocusText = "Choose a worker card, then drag it onto <b>Drop Worker</b> on the selected flow. The worker's name chip removes them from that flow.";
            var workers = FindFirstObjectByType<WorkersUI>();
            if (workers != null)
                foreach (WorkerCardUI card in workers.GetComponentsInChildren<WorkerCardUI>(true))
                    if (card != null && card.employee != null)
                    {
                        calloutTarget = card.transform as RectTransform;
                        break;
                    }
            if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
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
            if (bodyText != null && bodyText.text != tutorialFocusText) bodyText.text = tutorialFocusText;
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

    Component GetRecipeTutorialStation(int index)
    {
        switch (index)
        {
            case 0: return FindFirstObjectByType<FreezerStation>();
            case 1: return FindFirstObjectByType<GrillStation>();
            case 2: return FindFirstObjectByType<PantryStation>();
            default: return FindFirstObjectByType<AssemblyStation>();
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
                    ? pantry.selectedItem == expected || pantry.secondItem == expected
                    : ItemNameContains(pantry.selectedItem, "Bun") || ItemNameContains(pantry.secondItem, "Bun"));
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

    static string GetRecipeTutorialPrompt(int index, string stationName)
    {
        string task = index switch
        {
            0 => "choose <b>Raw Patty</b> as its stored ingredient",
            1 => "choose <b>Cooked Patty</b> as its output",
            2 => "choose <b>Bun</b> as its stored ingredient",
            _ => "choose the <b>Hamburger</b> recipe",
        };
        return "Click the highlighted <b>" + stationName + "</b> in the kitchen. In the station panel, " + task + ".";
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
        if (tutorialTypewriterVoiceSource != null)
            tutorialTypewriterVoiceSource.Stop();
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
        else if (panel != null)
            panel.SetActive(false);
    }

    void EnsureUI()
    {
        EnsureTutorialTypewriterVoice();
        if (canvasRoot != null && panel != null && nextButton != null && cameraPromptPanel != null
            && nextButton.transform.IsChildOf(panel.transform))
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
        rt.sizeDelta = new Vector2(560f, 0f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
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
        iconFrame.GetComponent<Image>().color = GameUITheme.Surface;
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

        stepLabel = CreateLabel(headerText.transform, "", 12, FontStyles.Bold, GameUITheme.Accent);
        stepLabel.alignment = TextAlignmentOptions.Left;
        titleText = CreateLabel(headerText.transform, "", 20, FontStyles.Bold, Color.white);
        titleText.alignment = TextAlignmentOptions.Left;
        bodyText = CreateLabel(panel.transform, "", 15, FontStyles.Normal, new Color(0.92f, 0.94f, 0.98f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;

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
        calloutArrow = CreateCalloutArrow(canvasRoot.transform);
        GameUITheme.ApplyToCanvas(canvas);
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

        TextMeshProUGUI actionLabel = CreateLabel(row.transform, action, 13f,
            FontStyles.Bold, new Color(0.92f, 0.94f, 0.98f));
        actionLabel.alignment = TextAlignmentOptions.Left;
        actionLabel.textWrappingMode = TextWrappingModes.NoWrap;
        var actionSize = actionLabel.GetComponent<LayoutElement>();
        actionSize.minWidth = 0f;
        actionSize.flexibleWidth = 1f;

        TextMeshProUGUI status = CreateLabel(row.transform, "TRY IT", 10f,
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
