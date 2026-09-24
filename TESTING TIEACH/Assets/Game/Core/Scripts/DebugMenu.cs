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
        if (!UIInputFocusGuard.IsTyping && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt)))
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

        statusText.text =
            $"Money: ${cash}\n" +
            $"Workers: {workers}   Jobs: {pending}\n" +
            $"Pickup Station: {lampCount}/{lampCap}\n" +
            $"Kitchen stock units: {stockUnits}\n" +
            $"Clock: {clock}   Speed: {speed}\n" +
            $"Milestone: {stage}";
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
        prt.sizeDelta = new Vector2(320f, 1000f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        bg.raycastTarget = true;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 6;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;

        CreateLabel(panel.transform, "DEBUG MENU", 20, FontStyles.Bold);
        CreateLabel(panel.transform, "F1 / ` to toggle", 12, FontStyles.Normal).color = new Color(0.7f, 0.75f, 0.85f);

        statusText = CreateLabel(panel.transform, "", 13, FontStyles.Normal);
        statusText.alignment = TextAlignmentOptions.Left;
        statusText.GetComponent<LayoutElement>().minHeight = 108;

        toastText = CreateLabel(panel.transform, "", 12, FontStyles.Italic);
        toastText.color = new Color(0.55f, 0.95f, 0.65f);
        toastText.alignment = TextAlignmentOptions.Left;

        CreateButton(panel.transform, $"+${moneyGrant} Money", () =>
        {
            var m = FindObjectOfType<MoneyManager>();
            if (m == null) { Toast("No MoneyManager"); return; }
            m.AddMoney(moneyGrant);
            Toast($"+${moneyGrant}");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Hire Worker (free)", () =>
        {
            var pm = ProductionManager.Instance ?? FindObjectOfType<ProductionManager>();
            if (pm == null) { Toast("No ProductionManager"); return; }
            var emp = pm.HireWorkerFree();
            Toast(emp != null ? "Hired " + emp.employeeName : "Hire failed");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Spawn Customer", () =>
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

        CreateButton(panel.transform, "Fill Kitchen Stock", () =>
        {
            var inv = KitchenInventory.Instance ?? FindObjectOfType<KitchenInventory>();
            if (inv == null) { Toast("No KitchenInventory"); return; }
            inv.FillAllStock(stockFillAmount);
            Toast($"+{stockFillAmount} each ingredient");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Clear Pickup Station", () =>
        {
            var lamp = HeatLampStation.Instance ?? FindObjectOfType<HeatLampStation>();
            if (lamp == null) { Toast("No Pickup Station"); return; }
            lamp.ClearAllMeals();
            Toast("Pickup Station cleared");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Place All Stations", () =>
        {
            var placer = FindFirstObjectByType<BuildPlacer>();
            if (placer == null) { Toast("No BuildPlacer"); return; }
            int placed = placer.DebugPlaceAllStations();
            Toast(placed > 0
                ? $"Placed {placed} missing stations"
                : "All stations already placed or no space");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Clear All Stations", ClearAllEquipment);

        CreateButton(panel.transform, "Time 1x", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.Play);
            Toast("1x");
            RefreshStatus();
        });
        CreateButton(panel.transform, "Time 3x", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.FastForward);
            Toast("3x");
            RefreshStatus();
        });
        CreateButton(panel.transform, "Time 20x (Super)", () =>
        {
            GameTimeManager.Instance?.SetSpeed(GameTimeManager.SpeedMode.SuperFast);
            Toast("20x");
            RefreshStatus();
        });
        CreateButton(panel.transform, "Skip to End of Day", () =>
        {
            var time = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();
            if (time == null) { Toast("No GameTimeManager"); return; }
            if (!time.DebugSkipToEndOfDay()) { Toast("Day already ended"); return; }
            SetVisible(false);
        });
        CreateButton(panel.transform, "Force Milestone Quiz", () =>
        {
            var m = MilestoneProgressManager.Instance;
            if (m == null) { Toast("No MilestoneProgressManager"); return; }
            m.DebugForceQuizReady();
            Toast("Quiz ready");
            FindFirstObjectByType<MilestoneQuizUI>()?.Show();
        });
        CreateButton(panel.transform, "Open Milestone Map", () =>
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

        CreateLabel(panel.transform, "Skip to milestone", 13, FontStyles.Bold);
        var jumpRow = new GameObject("MilestoneJumpRow", typeof(RectTransform));
        jumpRow.transform.SetParent(panel.transform, false);
        jumpRow.AddComponent<LayoutElement>().minHeight = 32;
        var jumpLayout = jumpRow.AddComponent<HorizontalLayoutGroup>();
        jumpLayout.spacing = 4f;
        jumpLayout.childAlignment = TextAnchor.MiddleCenter;
        jumpLayout.childControlWidth = true;
        jumpLayout.childControlHeight = true;
        jumpLayout.childForceExpandWidth = true;
        jumpLayout.childForceExpandHeight = true;
        for (int n = 1; n <= 6; n++)
        {
            int milestoneNumber = n;
            CreateButton(jumpRow.transform, milestoneNumber.ToString(), () => JumpToMilestone(milestoneNumber));
        }
        CreateButton(panel.transform, "Pause / Unpause", () =>
        {
            GameTimeManager.Instance?.TogglePausePlay();
            var t = GameTimeManager.Instance;
            Toast(t != null && t.CurrentSpeed == GameTimeManager.SpeedMode.Paused ? "Paused" : "Unpaused");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Skip Tutorial", () =>
        {
            var tutorial = OnboardingTutorial.Instance ?? FindFirstObjectByType<OnboardingTutorial>();
            if (tutorial == null) { Toast("No tutorial"); return; }
            tutorial.Skip();
            Toast("Tutorial skipped");
            RefreshStatus();
        });
        CreateButton(panel.transform, "Restart Tutorial", () =>
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
        CollectEquipment<DrinkStation>(equipment);
        CollectEquipment<AssemblyStation>(equipment);
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
        img.color = new Color(0.28f, 0.34f, 0.42f, 1f);
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        go.AddComponent<LayoutElement>().minHeight = 34;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
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
