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
    bool liveCustomerSpawned;
    bool starterKitchenHidden;
    int stepIndex;
    public int SaveStepIndex => stepIndex;
    public void RestoreCheckpoint(bool complete, int savedStep)
    {
        PlayerPrefs.SetInt(PrefsCompleteKey, complete ? 1 : 0);
        pendingStart = false;
        running = !complete;
        stepIndex = Mathf.Clamp(savedStep, 0, Steps.Length - 1);
        furthestStepIndex = stepIndex;
        if (complete)
        {
            HideUI();
            ClearHighlight();
            GameTimeManager.Instance?.ReleaseExternalPause(PauseSource);
        }
        else ShowStep();
    }
    int furthestStepIndex = -1;
    bool flowEditCompleted;
    readonly List<Outline> tutorialControlOutlines = new List<Outline>();
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
            bool requireEditedFlow = false)
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
            "Open <b>Build</b> (top-left, or press Q then 1). Each station's <b>first copy is free</b>. Click Buy, then click a floor tile to place it.\n\n" +
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
            "Buy and place a <b>freezer</b>. It holds raw burger patties. A cook walks here first when a burger is ordered, then carries a patty to the grill.\n\n" +
            "If the freezer is missing or empty, burgers never start.\n\nPlace one to continue.",
            "Next", Highlight.Freezer, openInventory: true, requirePlaced: true),
        new Step(
            "Grill",
            "Buy and place a <b>grill</b> next in the burger line. After the freezer, this cooks the patty.\n\n" +
            "Later, in Staff, point the grill's output toward assembly so cooked patties keep moving.\n\nPlace one to continue.",
            "Next", Highlight.Grill, openInventory: true, requirePlaced: true),
        new Step(
            "Fryer",
            "Buy and place a <b>fryer</b>. Fries skip the freezer and grill — they are their own short path.\n\n" +
            "The fries flow is Pantry > Fryer > Pickup Station, starting with potatoes from the pantry.\n\nPlace one to continue.",
            "Next", Highlight.Fryer, openInventory: true, requirePlaced: true),
        new Step(
            "Pantry",
            "Buy and place a <b>pantry</b>. It supplies buns for burgers and potatoes for fries.\n\n" +
            "For a burger, the worker carries the cooked patty here, adds a bun, then continues to assembly.\n\nPlace one to continue.",
            "Next", Highlight.Pantry, openInventory: true, requirePlaced: true),
        new Step(
            "Assembly",
            "Buy and place an <b>assembly</b> table. It always combines two inputs into one output.\n\n" +
            "For the Burger recipe, the inputs are one cooked patty and one bun. Assembly output should point to the Pickup Station.\n\nPlace one to continue.",
            "Next", Highlight.Assembly, openInventory: true, requirePlaced: true),
        new Step(
            "Pickup Station",
            "Buy and place a <b>Pickup Station</b> on the pass. Any finished item can wait here for customer pickup, including food, drinks, and future products.\n\n" +
            "If this sits empty, upstream stations are too slow. If it fills and items expire, you produced more than you can serve.\n\nPlace one to continue.",
            "Next", Highlight.HeatLamp, openInventory: true, requirePlaced: true),
        new Step(
            "Assembly recipe",
            "Select the assembly station in Management to choose its recipe. Burger is the first recipe available, and more recipes can be added later.\n\n" +
            "The station panel shows both required inputs and the single finished output.",
            "Next", Highlight.Management),
        new Step(
            "Buy ingredients",
            "Stations do nothing without stock. Open <b>Business</b> (top-left, or press Q then 3), then <b>Menu & Supply</b>.\n\n" +
            "Buy Burger patties, Buns, and Potatoes. Packs spend cash. A delivery person brings them in through the front door after a short wait.",
            "Next", Highlight.Management, openIngredients: true),
        new Step(
            "Hire workers",
            "Stations only cook if people work a <b>flow</b>. Open <b>Staff</b> (top-left, or press Q then 2).\n\n" +
            "Click <b>Hire</b> at the top of the Workers tab to add staff. Each hire costs money. A worker can cover up to three stations; extra people you do not assign will stand idle.\n\n" +
            "Hire at least one worker to continue.",
            "Next", Highlight.Management, openWorkers: true, requireHiredWorker: true),
        new Step(
            "Create a flow",
            "Still on Workers, click <b>Create Flow</b>. The panel hides so you can see the kitchen.\n\n" +
            "Click stations <b>in production order</b>. The burger line is freezer → grill → assembly → Pickup Station. Confirm when the path looks right.\n\n" +
            "Build Freezer > Grill > Assembly > Pickup Station. Then make a short Pantry > Assembly feeder flow so buns arrive independently.",
            "Next", Highlight.Management, openWorkers: true, requireFlow: true),
        new Step(
            "Edit a flow",
            "Select the flow chip at the top of Workers, then click <b>Edit Flow</b>.\n\n" +
            "Click a station already on the path to trim it back. Click a new station to extend the route. Press Esc to restore the previous path.\n\n" +
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
            "2. Workers follow the flow you built — freezer → grill → assembly for burgers, fryer for fries, drinks for drinks.\n" +
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

        "Open <b>Build</b> at the top-left, or press <b>Q</b> then <b>1</b>. I covered the first copy of each station, so those are free. Click Buy, then choose a kitchen floor tile.\n\n" +
        "Keep equipment out of the lobby. You can rotate a station while its placement ghost is visible.",

        "Every order begins at the <b>register</b>. Open <b>Build</b>, take the free register, then click an open <b>counter</b> tile on the lobby side.\n\n" +
        "Keep some floor space clear for the customer line. Place the register and I will show you the kitchen.",

        "Let's start the burger process. Buy and place a <b>freezer</b>. It stores raw patties, so this is the first stop for every burger.\n\n" +
        "If it is missing or empty, burger production cannot begin.",

        "Next, buy and place a <b>grill</b>. Workers carry raw patties here from the freezer to cook them.\n\n" +
        "Distance matters because every extra tile adds travel time. Put it somewhere sensible and we will connect its output later.",

        "Now place a <b>fryer</b>. Fries use their own short production path, separate from burgers.\n\n" +
        "Its output should eventually lead to a Pickup Station so customers can collect the finished fries.",

        "Place a <b>pantry</b> so the burger line can collect buns. The same pantry supplies potatoes to a fries line.\n\n" +
        "Route burgers from the grill to the pantry, then assembly.",

        "Place an <b>assembly</b> table. Workers combine one cooked patty with one bun here to finish a burger.\n\n" +
        "The burger route should move from grill to pantry to assembly, then from assembly to a Pickup Station.",

        "Place a <b>Pickup Station</b> on the counter. Finished burgers, fries, and drinks wait here until customers collect them.\n\n" +
        "If it stays empty, production may be too slow. If it stays full, we may be producing more than customers need.",

        "Buy and place a <b>pantry</b>. It supplies toppings and other ingredients workers need at assembly.\n\n" +
        "Try placing it nearby. I do not want workers crossing the entire kitchen every time they need one ingredient.",

        "The equipment is useless without material to process. Open <b>Business</b>, or press <b>Q</b> then <b>3</b>, and choose <b>Menu & Supply</b>.\n\n" +
        "Order at least one pack of <b>Burger patties</b>, <b>Buns</b>, and <b>Potatoes</b>. A delivery person will bring the combined order through the front door.",

        "Now we need someone to run the process. Open <b>Staff</b>, or press <b>Q</b> then <b>2</b>, and click <b>Hire</b>. Each employee costs money, so staffing is a capacity decision.\n\n" +
        "A worker can carry up to four items after upgrades, but anyone you do not assign to a flow will remain idle.",

        "Let's define how work should move. In Workers, click <b>Create Flow</b>, then select stations in production order.\n\n" +
        "Build this burger route: <b>Freezer > Grill > Assembly > Pickup Station</b>. Then create a <b>Pantry > Assembly</b> feeder flow for buns.",

        "Plans change, so you need to know how to revise one. Select the flow chip, then click <b>Edit Flow</b>.\n\n" +
        "Selecting an existing stop trims the route back to that point. Selecting a new station extends it. Check the route, then click Finish to save it.",

        "A process plan does nothing until someone owns the work. Select the burger flow, then <b>drag a worker card onto DROP WORKER</b>.\n\n" +
        "Their name will appear on the flow. Assign at least one worker so production can begin.",

        "Here is the system I need you to observe:\n\n" +
        "1. A customer orders at the register.\n2. Workers follow the flows you designed.\n3. Finished items wait at a Pickup Station.\n4. The customer collects each item and leaves.\n\n" +
        "Waiting, walking, and blocked stations are all clues.",

        "I am opening the doors for one test customer. They will order <b>one burger</b>, so we can watch the full flow without a crowd hiding the problems.\n\n" +
        "If production stops, check the worker assignment, station sequence, and ingredient stock. Serve the order, then click Finish."
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
        if (title != null && title.IsShowingTitle)
            return;
        TryBegin();
    }

    public void NotifyGameStarted()
    {
        TryBegin();
    }

    public void TryBegin()
    {
        if (running || IsComplete)
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
        stepIndex = 0;
        furthestStepIndex = -1;
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
        if (nextLabel != null)
            nextLabel.text = "Finish";
        if (bodyText != null)
            bodyText.text = "That's it. You just watched a full service loop: order in, product made, and meal handed off.\n\n" +
                "Click <b>Finish</b>. From here on, I am counting on you to use the data and improve the business.";
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
            bodyText.text = stepIndex < GusDialogue.Length ? GusDialogue[stepIndex] : step.body;
        if (stepLabel != null)
            stepLabel.text = $"Tutorial  {stepIndex + 1} / {Steps.Length}";
        if (nextLabel != null)
            nextLabel.text = step.nextLabel;
        if (backButton != null)
            backButton.interactable = stepIndex > 0;

        ApplyPause(step.pauseSim);
        currentHighlight = step.highlight;
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
        if (flow == null || flow.stations == null || flow.stations.Count != 4) return false;
        foreach (var station in flow.stations)
            if (station == null || !station.activeInHierarchy || station.GetComponent<PlacedBuildItem>() == null)
                return false;
        return flow.stations[0].GetComponent<FreezerStation>() != null
            && flow.stations[1].GetComponent<GrillStation>() != null
            && flow.stations[2].GetComponent<AssemblyStation>() != null
            && flow.stations[3].GetComponent<HeatLampStation>() != null;
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
        foreach (var outline in tutorialControlOutlines)
            if (outline != null) { outline.enabled = false; Destroy(outline); }
        tutorialControlOutlines.Clear();
    }

    void HighlightControl(Component control)
    {
        if (control == null || !control.gameObject.activeInHierarchy) return;
        var image = control.GetComponent<Graphic>();
        if (image == null) return;
        var outline = control.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(5f, -5f);
        outline.effectColor = new Color(1f, 0.82f, 0.15f, 1f);
        tutorialControlOutlines.Add(outline);
    }

    void RefreshControlHighlights()
    {
        if (Time.unscaledTime < nextControlHighlightRefresh) return;
        nextControlHighlightRefresh = Time.unscaledTime + 0.25f;
        ClearControlHighlights();
        var step = Steps[stepIndex];
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
            return;
        }
        RefreshAdvanceGate();
        RefreshControlHighlights();
        if (Steps[stepIndex].liveCustomer && !liveCustomerSpawned)
            SpawnPracticeCustomer();

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

        backButton = CreateSideButton(canvasRoot.transform, "Back", OnBack, new Color(0.28f, 0.32f, 0.4f, 1f), -1);
        nextButton = CreateSideButton(canvasRoot.transform, "Next", OnNext, new Color(0.22f, 0.55f, 0.38f, 1f), 1);
        nextLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();
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
        rt.sizeDelta = new Vector2(188f, 52f);
        rt.anchoredPosition = new Vector2(side < 0 ? -470f : 470f, 78f);
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
