using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// In-game debug panel. Toggle with F1 or backtick (`).
/// Add via Production > Add Debug Menu, or it auto-creates on play if missing.
/// </summary>
public class DebugMenu : MonoBehaviour
{
    public static DebugMenu Instance { get; private set; }

    [Header("Toggle")]
    public KeyCode toggleKey = KeyCode.F1;
    public KeyCode toggleKeyAlt = KeyCode.BackQuote;

    [Header("Cheats")]
    public int moneyGrant = 500;
    public int stockFillAmount = 50;

    GameObject panel;
    TextMeshProUGUI statusText;
    TextMeshProUGUI toastText;
    float toastTimer;
    bool visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectOfType<DebugMenu>() != null) return;
        var go = new GameObject("DebugMenu");
        go.AddComponent<DebugMenu>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsureUI();
        SetVisible(false);
    }

    void Update()
    {
        // Debug access is a global escape hatch. It must remain available during
        // tutorials, modal UI, paused simulation, and while an input field has focus.
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt))
            SetVisible(!visible);

        if (visible)
            RefreshStatus();

        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastTimer <= 0f && toastText != null)
                toastText.text = "";
        }
    }

    void SetVisible(bool on)
    {
        visible = on;
        EnsureUI();
        if (panel != null) panel.SetActive(on);
        if (on) RefreshStatus();
    }

    void Toast(string msg)
    {
        if (toastText != null) toastText.text = msg;
        toastTimer = 2.2f;
    }

    void RefreshStatus()
    {
        if (statusText == null) return;
        var money = FindObjectOfType<MoneyManager>();
        var pm = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();
        var lamp = HeatLampStation.Instance != null ? HeatLampStation.Instance : FindObjectOfType<HeatLampStation>();
        var inv = KitchenInventory.Instance;

        int workers = pm != null && pm.employees != null ? pm.employees.Count : 0;
        int pending = pm != null ? pm.PendingJobCount : 0;
        int lampCount = lamp != null ? lamp.Count : 0;
        int lampCap = lamp != null ? lamp.maxCapacity : 0;
        int cash = money != null ? money.CurrentMoney : 0;

        int stockUnits = 0;
        if (inv != null && inv.stock != null)
        {
            foreach (var e in inv.stock)
                if (e != null) stockUnits += e.quantity;
        }

        var time = GameTimeManager.Instance;
        string clock = time != null ? time.GetClockText() : "—";
        string speed = time != null ? time.GetSpeedLabel() : $"{Time.timeScale:0.##}x";
        var milestones = MilestoneProgressManager.Instance;
        string stage = milestones != null ? milestones.GetActiveMilestoneDebugLabel() : "—";
        string taskProgress = "—";
        if (milestones != null)
        {
            var active = milestones.GetActiveMilestone();
            var missions = active != null ? active.GetMissions() : null;
            var progress = MissionProgressManager.Instance;
            if (missions != null && missions.Count > 0 && progress != null)
            {
                int complete = 0;
                foreach (var mission in missions)
                    if (mission != null && progress.IsComplete(mission)) complete++;
                taskProgress = $"{complete}/{missions.Count} complete";
            }
        }

        statusText.text =
            $"Money: ${cash}\n" +
            $"Workers: {workers}   Jobs: {pending}\n" +
            $"Pickup Station: {lampCount}/{lampCap}\n" +
            $"Kitchen stock units: {stockUnits}\n" +
            $"Clock: {clock}   Speed: {speed}\n" +
            $"Milestone: {stage}  Tasks: {taskProgress}";
    }

    void EnsureUI()
    {
        if (panel != null) return;

        var canvasGo = new GameObject("DebugMenuCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = new Vector2(0f, 0.5f);
        prt.anchorMax = new Vector2(0f, 0.5f);
        prt.pivot = new Vector2(0f, 0.5f);
        prt.anchoredPosition = new Vector2(16f, 0f);
        prt.sizeDelta = new Vector2(390f, 760f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        bg.raycastTarget = true;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.spacing = 5;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        CreateLabel(panel.transform, "DEBUG TOOLS", 20, FontStyles.Bold);
        CreateLabel(panel.transform, "F1 or ` to toggle", 11, FontStyles.Normal).color = new Color(0.62f, 0.7f, 0.8f);

        statusText = CreateLabel(panel.transform, "", 13, FontStyles.Normal);
        statusText.alignment = TextAlignmentOptions.Left;
        statusText.GetComponent<LayoutElement>().minHeight = 112;

        toastText = CreateLabel(panel.transform, "", 12, FontStyles.Italic);
        toastText.color = new Color(0.55f, 0.95f, 0.65f);
        toastText.alignment = TextAlignmentOptions.Left;
        toastText.GetComponent<LayoutElement>().minHeight = 18f;

        Transform actionContent = CreateScrollContent(panel.transform);
        CreateSectionHeader(actionContent, "RESOURCES");
        Transform resourceRow = CreateButtonRow(actionContent, "ResourceRow");

        CreateButton(resourceRow, $"+${moneyGrant} Money", () =>
        {
            var m = FindObjectOfType<MoneyManager>();
            if (m == null) { Toast("No MoneyManager"); return; }
            m.AddMoney(moneyGrant);
            Toast($"+${moneyGrant}");
            RefreshStatus();
        });

        CreateSectionHeader(actionContent, "PEOPLE");
        Transform peopleRow = CreateButtonRow(actionContent, "PeopleRow");
        CreateButton(peopleRow, "Hire Worker", () =>
        {
            var pm = ProductionManager.Instance ?? FindObjectOfType<ProductionManager>();
            if (pm == null) { Toast("No ProductionManager"); return; }
            var emp = pm.HireWorkerFree();
            Toast(emp != null ? "Hired " + emp.employeeName : "Hire failed");
            RefreshStatus();
        });

        CreateButton(peopleRow, "Spawn Customer", () =>
        {
            var spawner = FindFirstObjectByType<CustomerSpawner>(FindObjectsInactive.Include);
            if (spawner == null) { Toast("No CustomerSpawner"); return; }
            if (!spawner.gameObject.activeInHierarchy)
                spawner.gameObject.SetActive(true);
            if (!spawner.SpawnNow(true))
            {
                Toast(string.IsNullOrEmpty(spawner.LastSpawnError)
                    ? "Spawn failed"
                    : spawner.LastSpawnError);
                return;
            }
            Toast(Time.timeScale <= 0.001f
                ? "Customer spawned (unpause to see them walk)"
                : "Customer spawned");
            RefreshStatus();
        });

        CreateButton(resourceRow, $"Ingredients +{stockFillAmount}", () =>
        {
            var inv = KitchenInventory.Instance ?? FindObjectOfType<KitchenInventory>();
            if (inv == null) { Toast("No KitchenInventory"); return; }
            inv.FillAllStock(stockFillAmount);
            Toast($"Added {stockFillAmount} of every ingredient instantly");
            RefreshStatus();
        });

        CreateSectionHeader(actionContent, "KITCHEN");
        Transform kitchenRow = CreateButtonRow(actionContent, "KitchenRow");
        CreateButton(kitchenRow, "Clear Pickup", () =>
        {
            var lamp = HeatLampStation.Instance ?? FindObjectOfType<HeatLampStation>();
            if (lamp == null) { Toast("No Pickup Station"); return; }
            lamp.ClearAllMeals();
            Toast("Pickup Station cleared");
            RefreshStatus();
        });

        CreateButton(kitchenRow, "Place Stations", () =>
        {
            var placer = FindFirstObjectByType<BuildPlacer>();
            if (placer == null) { Toast("No BuildPlacer"); return; }
            int placed = placer.DebugPlaceAllStations();
            Toast(placed > 0
                ? $"Placed {placed} missing stations"
                : "All stations already placed or no space");
            RefreshStatus();
        });

        Transform kitchenRowTwo = CreateButtonRow(actionContent, "KitchenRowTwo");
        CreateButton(kitchenRowTwo, "Reset Stations + Workers", ResetStationsAndWorkers);
        CreateButton(kitchenRowTwo, "Clear All Stations", ClearAllEquipment);

        CreateSectionHeader(actionContent, "TIME");
        Transform timeRow = CreateButtonRow(actionContent, "TimeRow");
        CreateButton(timeRow, "1x", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.Play);
            Toast("1x");
            RefreshStatus();
        });
        CreateButton(timeRow, "3x", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.FastForward);
            Toast("3x");
            RefreshStatus();
        });
        CreateButton(timeRow, "20x", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.SuperFast);
            Toast("20x");
            RefreshStatus();
        });
        Transform timeRowTwo = CreateButtonRow(actionContent, "TimeRowTwo");
        CreateButton(timeRowTwo, "End Day", () =>
        {
            var time = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();
            if (time == null) { Toast("No GameTimeManager"); return; }
            if (!time.DebugSkipToEndOfDay()) { Toast("Day already ended"); return; }
            SetVisible(false);
        });
        CreateButton(timeRowTwo, "Pause / Resume", () =>
        {
            GameTimeManager.Instance?.TogglePausePlay();
            var t = GameTimeManager.Instance;
            Toast(t != null && t.CurrentSpeed == GameTimeManager.SpeedMode.Paused ? "Paused" : "Resumed");
            RefreshStatus();
        });

        CreateSectionHeader(actionContent, "PROGRESSION");
        Transform taskAccessRow = CreateButtonRow(actionContent, "CurrentTasksRow");
        CreateButton(taskAccessRow, "Open Current Tasks", () =>
        {
            if (MissionListUI.Instance == null) { Toast("No task list"); return; }
            MissionListUI.Instance.ShowTasksTab();
            Toast("Current milestone tasks");
        });
        Transform progressionRow = CreateButtonRow(actionContent, "ProgressionRow");
        CreateButton(progressionRow, "Force Quiz", () =>
        {
            var m = MilestoneProgressManager.Instance;
            if (m == null) { Toast("No MilestoneProgressManager"); return; }
            m.DebugForceQuizReady();
            Toast("Quiz ready");
            FindFirstObjectByType<MilestoneQuizUI>()?.Show();
        });
        CreateButton(progressionRow, "Milestone Map", () =>
        {
            if (OnboardingTutorial.BlocksProgression)
            {
                Toast("Finish the tutorial first");
                return;
            }
            if (MissionListUI.Instance != null)
            {
                MissionListUI.Instance.ShowProgressionTab();
                Toast("Progression tab");
            }
            else
            {
                Toast("No side menu");
            }
        });

        Transform researchRow = CreateButtonRow(actionContent, "ResearchDebugRow");
        CreateButton(researchRow, "Reset Research", () =>
        {
            ResearchProgressManager.Ensure().DebugResetAll();
            Toast("All research reset");
            RefreshStatus();
        });
        CreateButton(researchRow, "Complete All Research", () =>
        {
            ResearchProgressManager.Ensure().DebugCompleteAll();
            Toast("All research completed");
            RefreshStatus();
        });

        CreateLabel(actionContent, "Jump to milestone", 12, FontStyles.Bold);
        CreateLabel(actionContent, "Sets the selected milestone active and marks earlier milestones complete.", 10, FontStyles.Normal);
        var jumpRow = new GameObject("MilestoneJumpRow", typeof(RectTransform));
        jumpRow.transform.SetParent(actionContent, false);
        jumpRow.AddComponent<LayoutElement>().minHeight = 32;
        var jumpLayout = jumpRow.AddComponent<HorizontalLayoutGroup>();
        jumpLayout.spacing = 4f;
        jumpLayout.childAlignment = TextAnchor.MiddleCenter;
        jumpLayout.childControlWidth = true;
        jumpLayout.childControlHeight = true;
        jumpLayout.childForceExpandWidth = true;
        jumpLayout.childForceExpandHeight = true;
        MilestoneProgressManager milestoneProgress = MilestoneProgressManager.Instance;
        int milestoneCount = milestoneProgress != null ? milestoneProgress.GetNumberedMilestoneCount() : 0;
        for (int n = 1; n <= milestoneCount; n++)
        {
            int milestoneNumber = n;
            CreateButton(jumpRow.transform, milestoneNumber.ToString(), () => JumpToMilestone(milestoneNumber));
        }
        CreateSectionHeader(actionContent, "TUTORIAL");
        Transform tutorialRow = CreateButtonRow(actionContent, "TutorialRow");
        CreateButton(tutorialRow, "Skip Tutorial", () =>
        {
            var tutorial = OnboardingTutorial.Instance ?? FindFirstObjectByType<OnboardingTutorial>();
            if (tutorial == null) { Toast("No tutorial"); return; }
            tutorial.Skip();
            Toast("Tutorial skipped");
            RefreshStatus();
        });
        CreateButton(tutorialRow, "Restart Tutorial", () =>
        {
            var tutorial = OnboardingTutorial.Instance ?? FindFirstObjectByType<OnboardingTutorial>();
            if (tutorial == null) { Toast("No tutorial"); return; }
            tutorial.Restart();
            Toast("Tutorial restarted");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Close", () => SetVisible(false));
    }

    void ClearAllEquipment()
    {
        var management = ManagementModeController.Instance;
        if (management != null && management.IsCapturingFlow) management.CancelFlowCapture();
        var placer = FindFirstObjectByType<BuildPlacer>();
        if (placer != null) { placer.CancelPlacement(); placer.CancelDrag(); }

        var equipment = new HashSet<GameObject>();
        CollectEquipment<FreezerStation>(equipment);
        CollectEquipment<GrillStation>(equipment);
        CollectEquipment<FryerStation>(equipment);
        CollectEquipment<CuttingStation>(equipment);
        CollectEquipment<DrinkStation>(equipment);
        CollectEquipment<AssemblyStation>(equipment);
        CollectEquipment<ShakeStation>(equipment);
        CollectEquipment<PantryStation>(equipment);
        CollectEquipment<HeatLampStation>(equipment);
        CollectEquipment<Register>(equipment);

        var production = ProductionManager.Instance;
        if (production != null && production.productionFlows != null)
        {
            foreach (var flow in production.productionFlows)
            {
                if (flow == null) continue;
                flow.Clean();
                if (!flow.stations.Exists(station => equipment.Contains(station))) continue;
                foreach (var worker in flow.workers)
                    if (worker != null) worker.ClearAllOperatedStations();
                flow.workers.Clear();
                flow.stations.Clear();
                flow.stepIds.Clear();
                flow.connections.Clear();
                flow.graphInitialized = true;
            }
            production.lastFlowBalance = null;
            production.SyncLegacyFlowSelection();
        }

        var inventory = FindFirstObjectByType<InventoryManager>();
        foreach (var node in FindObjectsByType<StationNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (equipment.Contains(node.gameObject)) node.ClearWorker();
            if (equipment.Contains(node.gameObject) || equipment.Contains(node.outputTarget)) node.SetOutput(null);
        }
        foreach (var station in equipment)
        {
            var mounted = station.GetComponent<CounterMountedItem>();
            if (mounted != null && mounted.surface != null) mounted.surface.Release(mounted);
            // Inactive before Destroy so occupancy scans exclude it in this frame.
            station.SetActive(false);
            Destroy(station);
        }
        inventory?.DebugClearEquipmentInventory();
        PurchaseUndoManager.Instance?.ClearHistory();
        GridManager.Instance?.ResyncOccupancyFromScene();
        FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include)?.RefreshAll();
        FindFirstObjectByType<WorkersUI>(FindObjectsInactive.Include)?.Refresh();
        Toast($"Cleared {equipment.Count} stations");
        RefreshStatus();
    }

    static void CollectEquipment<T>(HashSet<GameObject> equipment) where T : Component
    {
        foreach (var station in FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            equipment.Add(station.gameObject);
    }

    void JumpToMilestone(int number)
    {
        var tutorial = OnboardingTutorial.Instance ?? FindFirstObjectByType<OnboardingTutorial>();
        if (tutorial != null && (OnboardingTutorial.IsActive || !OnboardingTutorial.IsComplete))
            tutorial.Skip();

        var milestones = MilestoneProgressManager.Instance;
        if (milestones == null)
        {
            Toast("No MilestoneProgressManager");
            return;
        }

        if (!milestones.DebugJumpToNumberedMilestone(number, out string label))
        {
            Toast("Could not jump to milestone " + number);
            return;
        }

        Toast("Now on " + label);
        RefreshStatus();
    }

    void ResetStationsAndWorkers()
    {
        ResetStationsAndWorkersRuntime(out int stationCount, out int workerCount);
        Toast($"Reset {stationCount} stations and {workerCount} workers");
        RefreshStatus();
    }

    /// <summary>
    /// Clears transient production, station buffers, and active worker tasks while
    /// preserving placement, configuration, assignments, upgrades, and inventory.
    /// Shared by the debug action and save loading so both reset identically.
    /// </summary>
    public static void ResetStationsAndWorkersRuntime(out int stationCount, out int workerCount)
    {
        var production = ProductionManager.Instance ?? FindFirstObjectByType<ProductionManager>();

        workerCount = 0;
        foreach (KitchenEmployee worker in FindObjectsByType<KitchenEmployee>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (worker == null) continue;
            worker.AbortCurrentWork();
            workerCount++;
        }

        // Abort workers first so they can release their current reservations,
        // then discard any remaining queued or orphaned production work.
        production?.ResetTransientProductionState();

        stationCount = 0;
        foreach (GrillStation station in FindObjectsByType<GrillStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            station.RestoreBufferedState(station.selectedProduct, 0, 0f);
            stationCount++;
        }
        foreach (FryerStation station in FindObjectsByType<FryerStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            station.ResetRuntimeState();
            stationCount++;
        }
        foreach (AssemblyStation station in FindObjectsByType<AssemblyStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            station.RestoreBufferedState(station.selectedProduct, 0, 0, 0);
            stationCount++;
        }
        foreach (HeatLampStation station in FindObjectsByType<HeatLampStation>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            station.ResetRuntimeState();
            stationCount++;
        }
        foreach (Register station in FindObjectsByType<Register>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            station.ResetRuntimeState();
            stationCount++;
        }

        // Stateless source and processing stations still count toward the
        // feedback total even though no buffer needs clearing.
        stationCount += FindObjectsByType<FreezerStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        stationCount += FindObjectsByType<PantryStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        stationCount += FindObjectsByType<CuttingStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        stationCount += FindObjectsByType<DrinkStation>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

        production?.RequestImmediateProduction();
        FindFirstObjectByType<WorkersUI>(FindObjectsInactive.Include)?.Refresh();
    }

    static Transform CreateScrollContent(Transform parent)
    {
        var scrollGo = new GameObject("Actions", typeof(RectTransform), typeof(Image),
            typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(parent, false);
        scrollGo.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.085f, 0.72f);
        var scrollSize = scrollGo.GetComponent<LayoutElement>();
        scrollSize.minHeight = 260f;
        scrollSize.flexibleHeight = 1f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        viewport.GetComponent<Image>().color = Color.clear;
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(3f, 3f);
        viewportRect.offsetMax = new Vector2(-3f, -3f);

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 5, 5);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(scroll);
        return content.transform;
    }

    static void CreateSectionHeader(Transform parent, string title)
    {
        TextMeshProUGUI label = CreateLabel(parent, title, 11f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color(1f, 0.76f, 0.3f, 1f);
        label.characterSpacing = 1.5f;
        label.GetComponent<LayoutElement>().minHeight = 18f;
    }

    static Transform CreateButtonRow(Transform parent, string objectName)
    {
        var row = new GameObject(objectName, typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().minHeight = 31f;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        return row.transform;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles style)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        go.AddComponent<LayoutElement>().minHeight = size + 6;
        return tmp;
    }

    static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.21f, 0.26f, 0.33f, 1f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.82f, 0.88f, 0.94f, 1f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        go.AddComponent<LayoutElement>().minHeight = 30;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 12;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        return btn;
    }
}
