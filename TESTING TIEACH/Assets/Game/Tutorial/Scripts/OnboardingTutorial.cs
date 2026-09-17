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
    public static bool BlocksAutoCustomers => IsActive;
    public static bool BlocksProgression => IsActive || (!IsComplete && Instance != null && Instance.pendingStart);

    bool running;
    bool pendingStart;
    bool liveCustomerSpawned;
    bool starterKitchenHidden;
    int stepIndex;
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
            bool requireHiredWorker = false)
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
        }
    }

    static readonly Step[] Steps =
    {
        new Step(
            "Empty kitchen",
            "This shift starts with an <b>empty kitchen</b> and the register already in the lobby.\n\n" +
            "You will buy each workstation, stock ingredients, then serve one customer. Use <b>Back</b> and <b>Next</b> to move through the steps.",
            "Next", Highlight.None),
        new Step(
            "Register",
            "Customers enter and line up at the <b>register</b>. They order a burger, fries, drink, or combo here.\n\n" +
            "The register is already built. You do not buy this one — keep the lobby side clear so the line stays outside the kitchen.",
            "Next", Highlight.Register),
        new Step(
            "Buy stations",
            "Open <b>Inventory</b> (top-left). Each station's <b>first copy is free</b>. Click Buy, then click a floor tile to place it.\n\n" +
            "Place stations in the kitchen, not the lobby. You can rotate while placing if the ghost shows a facing arrow.\n\n" +
            "The next buttons stay locked until each station is on the floor.",
            "Next", Highlight.Inventory, openInventory: true),
        new Step(
            "Freezer",
            "Buy and place a <b>freezer</b>. It holds raw burger patties. A cook walks here first when a burger is ordered, then carries a patty to the grill.\n\n" +
            "If the freezer is missing or empty, burgers never start.\n\nPlace one to continue.",
            "Next", Highlight.Freezer, openInventory: true, requirePlaced: true),
        new Step(
            "Grill",
            "Buy and place a <b>grill</b> next in the burger line. After the freezer, this cooks the patty.\n\n" +
            "Later, in Management, point the grill's output toward assembly so cooked patties keep moving.\n\nPlace one to continue.",
            "Next", Highlight.Grill, openInventory: true, requirePlaced: true),
        new Step(
            "Fryer",
            "Buy and place a <b>fryer</b>. Fries skip the freezer and grill — they are their own short path.\n\n" +
            "Point fryer output toward the Pickup Station so fries leave with the rest of the order.\n\nPlace one to continue.",
            "Next", Highlight.Fryer, openInventory: true, requirePlaced: true),
        new Step(
            "Drinks",
            "Buy and place a <b>drink fountain</b>. Like fries, drinks are a simple path of their own.\n\n" +
            "Keep it staffed and point its output toward the Pickup Station, or the drink portion of an order will stall.\n\nPlace one to continue.",
            "Next", Highlight.Drink, openInventory: true, requirePlaced: true),
        new Step(
            "Assembly",
            "Buy and place an <b>assembly</b> table. This finishes burgers: bun, cooked patty, and toppings.\n\n" +
            "Grill output should point here. Assembly output should point to the Pickup Station.\n\nPlace one to continue.",
            "Next", Highlight.Assembly, openInventory: true, requirePlaced: true),
        new Step(
            "Pickup Station",
            "Buy and place a <b>Pickup Station</b> on the pass. Any finished item can wait here for customer pickup, including food, drinks, and future products.\n\n" +
            "If this sits empty, upstream stations are too slow. If it fills and items expire, you produced more than you can serve.\n\nPlace one to continue.",
            "Next", Highlight.HeatLamp, openInventory: true, requirePlaced: true),
        new Step(
            "Pantry",
            "Buy and place a <b>pantry</b> for toppings and extra ingredients workers pull during assembly.\n\n" +
            "Put it near assembly so cooks are not walking across the whole kitchen for a slice of cheese.\n\nPlace one to continue.",
            "Next", Highlight.Pantry, openInventory: true, requirePlaced: true),
        new Step(
            "Buy ingredients",
            "Stations do nothing without stock. Open <b>Management</b> (top-left), then the <b>Ingredients</b> tab.\n\n" +
            "Buy at least one pack of <b>Burger</b>, <b>Fries</b>, and <b>Drink</b>. Packs spend cash and fill kitchen stock the freezer and pantry use.",
            "Next", Highlight.Management, openIngredients: true),
        new Step(
            "Hire workers",
            "Stations only cook if people work a <b>flow</b>. Open <b>Management → Workers</b> (top-left, or press M).\n\n" +
            "Click <b>Hire</b> at the top of the Workers tab to add staff. Each hire costs money. A worker can cover up to three stations; extra people you do not assign will stand idle.\n\n" +
            "Hire at least one worker to continue.",
            "Next", Highlight.Management, openWorkers: true, requireHiredWorker: true),
        new Step(
            "Create a flow",
            "Still on Workers, click <b>Create Flow</b>. The panel hides so you can see the kitchen.\n\n" +
            "Click stations <b>in production order</b>. Example burger line: freezer → grill → assembly → Pickup Station. Confirm when the path looks right.\n\n" +
            "You can make more flows later for fries and drinks. Next stays locked until a flow has at least two stations.",
            "Next", Highlight.Management, openWorkers: true, requireFlow: true),
        new Step(
            "Edit a flow",
            "Select the flow chip at the top of Workers, then click <b>Edit Flow</b>.\n\n" +
            "Click a station already on the path to trim it back. Click a new station to extend the route. Press Esc to restore the previous path.\n\n" +
            "Use Edit when a station was clicked in the wrong order or you want a second line (fryer → Pickup Station).",
            "Next", Highlight.Management, openWorkers: true),
        new Step(
            "Assign workers to a flow",
            "Select the flow, then on a worker card click <b>Assign to Current Flow</b>.\n\n" +
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
            "The clock is running. Only <b>one customer</b> will come in so you can watch the loop.\n\n" +
            "If nobody is cooking, check workers, outputs, and that you bought ingredient packs.\n\n" +
            "Serve that order, then click Finish. Extra customers stay away until the tutorial ends.",
            "Finish", Highlight.Register, liveCustomer: true, requireAllStations: true, requireFlow: true, requireWorkerOnFlow: true),
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
        running = true;
        pendingStart = false;
        stepIndex = 0;
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
            bodyText.text = "That is a full loop: order in, food cooked, meal handed off.\n\nClick <b>Finish</b> to unlock milestones and normal customer traffic.";
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

        if (canvasRoot != null)
            canvasRoot.SetActive(true);
        if (panel != null)
            panel.SetActive(true);
        if (titleText != null)
            titleText.text = step.title;
        if (bodyText != null)
            bodyText.text = step.body;
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
            if (inv != null)
            {
                inv.OpenPanel();
                inv.SelectTab(0);
                inv.RefreshAll();
            }
        }

        if (step.openIngredients)
        {
            var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (mgmt != null)
                mgmt.OpenIngredientsTab();
        }

        if (step.openWorkers)
        {
            var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
            if (mgmt != null)
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
            case Highlight.Freezer: return "Place freezer";
            case Highlight.Grill: return "Place grill";
            case Highlight.Fryer: return "Place fryer";
            case Highlight.Drink: return "Place drinks";
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
            if (production.employees[i] != null)
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
            if (flow.stations != null && flow.stations.Count >= 2)
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
            if (flow.workers != null && flow.workers.Count > 0)
                return true;
        }
        return false;
    }

    bool AllTutorialStationsPlaced()
    {
        return HasPlacedStation(Highlight.Freezer)
            && HasPlacedStation(Highlight.Grill)
            && HasPlacedStation(Highlight.Fryer)
            && HasPlacedStation(Highlight.Drink)
            && HasPlacedStation(Highlight.Assembly)
            && HasPlacedStation(Highlight.HeatLamp)
            && HasPlacedStation(Highlight.Pantry);
    }

    bool HasPlacedStation(Highlight kind)
    {
        switch (kind)
        {
            case Highlight.Freezer: return HasActiveStation<FreezerStation>();
            case Highlight.Grill: return HasActiveStation<GrillStation>();
            case Highlight.Fryer: return HasActiveStation<FryerStation>();
            case Highlight.Drink: return HasActiveStation<DrinkStation>();
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
        liveCustomerSpawned = true;

        var existing = FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
            return;

        var spawner = FindFirstObjectByType<CustomerSpawner>();
        if (spawner != null)
            spawner.SpawnNow();
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
    }

    void PrepareEmptyKitchen()
    {
        CacheStarterKitchen();
        for (int i = 0; i < starterKitchen.Count; i++)
        {
            if (starterKitchen[i] != null)
                starterKitchen[i].SetActive(false);
        }
        starterKitchenHidden = true;

        var stock = KitchenInventory.Instance != null ? KitchenInventory.Instance : FindFirstObjectByType<KitchenInventory>();
        if (stock != null)
            stock.ClearAllStock();

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
        CollectStarter<FreezerStation>();
        CollectStarter<GrillStation>();
        CollectStarter<FryerStation>();
        CollectStarter<DrinkStation>();
        CollectStarter<AssemblyStation>();
        CollectStarter<HeatLampStation>();
        CollectStarter<PantryStation>();
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

    void Update()
    {
        if (!running) return;
        RefreshAdvanceGate();

        if (highlight == null || !highlight.activeSelf || highlightTarget == null)
            return;
        float pulse = 0.5f + Mathf.PingPong(Time.unscaledTime * 1.6f, 0.25f);
        highlight.transform.position = highlightTarget.position + Vector3.up * 2.2f;
        highlight.transform.localScale = Vector3.one * pulse;
    }

    public static bool ShouldHighlightInventoryItem(ItemDefinition item)
    {
        if (item == null || Instance == null || !Instance.running)
            return false;
        return ItemMatchesHighlight(item, Instance.currentHighlight);
    }

    static bool ItemMatchesHighlight(ItemDefinition item, Highlight kind)
    {
        string name = (item.itemName ?? item.name ?? "").ToLowerInvariant();
        switch (kind)
        {
            case Highlight.Freezer: return name.Contains("freezer");
            case Highlight.Grill: return name.Contains("grill");
            case Highlight.Fryer: return name.Contains("fryer");
            case Highlight.Drink: return name.Contains("drink");
            case Highlight.Assembly: return name.Contains("assembly");
            case Highlight.HeatLamp: return name.Contains("heat");
            case Highlight.Pantry: return name.Contains("pantry");
            case Highlight.Inventory:
                return name.Contains("freezer") || name.Contains("grill") || name.Contains("fryer")
                    || name.Contains("drink") || name.Contains("assembly") || name.Contains("heat")
                    || name.Contains("pantry");
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
                return StationOrNull(FindFirstObjectByType<DrinkStation>());
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
        scaler.matchWidthOrHeight = 0.5f;
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

        stepLabel = CreateLabel(panel.transform, "", 14, FontStyles.Normal, new Color(0.7f, 0.75f, 0.85f));
        titleText = CreateLabel(panel.transform, "", 24, FontStyles.Bold, Color.white);
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
