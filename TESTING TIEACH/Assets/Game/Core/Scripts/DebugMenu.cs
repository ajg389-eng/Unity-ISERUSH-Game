using UnityEngine;
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

        statusText.text =
            $"Money: ${cash}\n" +
            $"Workers: {workers}   Jobs: {pending}\n" +
            $"Heat Lamp: {lampCount}/{lampCap}\n" +
            $"Kitchen stock units: {stockUnits}\n" +
            $"Time scale: {Time.timeScale:0.##}x";
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
        prt.sizeDelta = new Vector2(320f, 520f);

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
        statusText.GetComponent<LayoutElement>().minHeight = 90;

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
            var spawner = FindObjectOfType<CustomerSpawner>();
            if (spawner == null) { Toast("No CustomerSpawner"); return; }
            Toast(spawner.SpawnNow() ? "Customer spawned" : "Spawn failed (queue full?)");
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

        CreateButton(panel.transform, "Clear Heat Lamp", () =>
        {
            var lamp = HeatLampStation.Instance ?? FindObjectOfType<HeatLampStation>();
            if (lamp == null) { Toast("No Heat Lamp"); return; }
            lamp.ClearAllMeals();
            Toast("Heat lamp cleared");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Time 1x", () => { Time.timeScale = 1f; Toast("1x"); RefreshStatus(); });
        CreateButton(panel.transform, "Time 2x", () => { Time.timeScale = 2f; Toast("2x"); RefreshStatus(); });
        CreateButton(panel.transform, "Time 3x", () => { Time.timeScale = 3f; Toast("3x"); RefreshStatus(); });
        CreateButton(panel.transform, "Pause / Unpause", () =>
        {
            Time.timeScale = Time.timeScale > 0.01f ? 0f : 1f;
            Toast(Time.timeScale <= 0.01f ? "Paused" : "Unpaused");
            RefreshStatus();
        });

        CreateButton(panel.transform, "Close", () => SetVisible(false));
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
