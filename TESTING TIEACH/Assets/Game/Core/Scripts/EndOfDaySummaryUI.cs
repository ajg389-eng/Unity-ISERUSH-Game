using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// End-of-shift summary panel. Shown when GameTimeManager fires OnDayEnded.
/// Continue starts the next day.
/// </summary>
public class EndOfDaySummaryUI : MonoBehaviour
{
    public const string PanelObjectName = "EndOfDaySummaryPanel";
    public const string PauseSource = "EndOfDaySummary";

    public static EndOfDaySummaryUI Instance { get; private set; }

    [Header("Optional scene refs")]
    public GameObject panelRoot;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI summaryText;
    public Button continueButton;

    GameTimeManager timeManager;
    bool visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<EndOfDaySummaryUI>() != null) return;
        var go = new GameObject("EndOfDaySummaryUI");
        go.AddComponent<EndOfDaySummaryUI>();
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

    void Start()
    {
        timeManager = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();
        EnsurePanel();
        Hide();
        WireContinue();

        if (timeManager != null)
        {
            timeManager.OnDayEnded += Show;
            timeManager.OnDayStarted += OnDayStarted;
        }

        if (StoreStatisticsManager.Instance != null && !StoreStatisticsManager.Instance.DayTrackingStarted)
            StoreStatisticsManager.Instance.BeginDay();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (timeManager != null)
        {
            timeManager.OnDayEnded -= Show;
            timeManager.OnDayStarted -= OnDayStarted;
        }
    }

    void OnDayStarted()
    {
        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.BeginDay();
        Hide();
    }

    public void Show()
    {
        EnsurePanel();
        WireContinue();
        RefreshSummary();

        if (panelRoot != null)
            panelRoot.SetActive(true);

        visible = true;
        Sfx.Play(SfxId.UiOpen);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.RequestExternalPause(PauseSource);
    }

    public void Hide()
    {
        visible = false;
        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
    }

    public bool IsVisible => visible;

    public void ContinueToNextDay()
    {
        Sfx.Play(SfxId.UiClick);
        Hide();

        var tm = GameTimeManager.Instance ?? timeManager;
        if (tm != null)
            tm.StartNextDay();
        else if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.BeginDay();
    }

    void RefreshSummary()
    {
        var stats = StoreStatisticsManager.Instance;
        var money = FindFirstObjectByType<MoneyManager>();
        var tm = GameTimeManager.Instance ?? timeManager;

        if (titleText != null)
        {
            string day = tm != null ? tm.GetDayText() : "Day";
            titleText.text = day + " Complete";
        }

        if (summaryText == null) return;

        int revenue = stats != null ? stats.RevenueToday : 0;
        int cashChange = stats != null ? stats.GetCashChangeToday() : 0;
        int balance = money != null ? money.CurrentMoney : 0;
        int orders = stats != null ? stats.OrdersCompletedToday : 0;
        int lost = stats != null ? stats.CustomersLostToday : 0;
        int waste = stats != null ? stats.MealsWastedToday : 0;
        float avgWait = stats != null ? stats.AverageWaitTimeTodaySeconds : 0f;
        float efficiency = stats != null ? stats.ServiceEfficiencyPercent : 0f;

        string cashSign = cashChange > 0 ? "+" : "";
        string cashColor = cashChange >= 0 ? "#7DDB8A" : "#E07A7A";

        summaryText.text =
            "<b>Sales & Cash</b>\n" +
            $"  Revenue earned     <b>${revenue}</b>\n" +
            $"  Cash change        <color={cashColor}>{cashSign}${cashChange}</color>\n" +
            $"  Ending balance     <b>${balance}</b>\n\n" +
            "<b>Customers</b>\n" +
            $"  Orders served      <b>{orders}</b>\n" +
            $"  Walked out         <b>{lost}</b>\n" +
            $"  Service efficiency <b>{efficiency:F0}%</b>\n\n" +
            "<b>Operations</b>\n" +
            $"  Avg wait time      <b>{avgWait:F1}s</b>\n" +
            $"  Food wasted        <b>{waste}</b>";
    }

    void WireContinue()
    {
        if (continueButton == null) return;
        continueButton.onClick.RemoveListener(ContinueToNextDay);
        continueButton.onClick.AddListener(ContinueToNextDay);
    }

    void EnsurePanel()
    {
        if (panelRoot != null)
        {
            BindRefs();
            return;
        }

        var canvas = ResolvePlayerUICanvas();
        if (canvas == null) return;

        var existing = canvas.transform.Find(PanelObjectName);
        if (existing != null)
        {
            panelRoot = existing.gameObject;
            BindRefs();
            return;
        }

        BuildPanel(canvas.transform);
        BindRefs();
    }

    void BindRefs()
    {
        if (panelRoot == null) return;
        if (titleText == null)
            titleText = panelRoot.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
        if (summaryText == null)
            summaryText = panelRoot.transform.Find("Summary")?.GetComponent<TextMeshProUGUI>();
        if (continueButton == null)
            continueButton = panelRoot.transform.Find("ContinueButton")?.GetComponent<Button>();
    }

    void BuildPanel(Transform canvasTransform)
    {
        // Dim overlay
        panelRoot = new GameObject(PanelObjectName, typeof(RectTransform));
        panelRoot.transform.SetParent(canvasTransform, false);
        var overlayRt = (RectTransform)panelRoot.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        var overlayImg = panelRoot.AddComponent<Image>();
        overlayImg.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        overlayImg.raycastTarget = true;

        // Card
        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(panelRoot.transform, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(460f, 520f);

        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.12f, 0.13f, 0.18f, 0.98f);
        cardImg.raycastTarget = true;

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 28, 24);
        vlg.spacing = 16;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        titleText = CreateLabel(card.transform, "Title", "Day Complete", 28, TextAlignmentOptions.Center);
        titleText.fontStyle = FontStyles.Bold;
        titleText.GetComponent<LayoutElement>().minHeight = 40;

        var subtitle = CreateLabel(card.transform, "Subtitle", "Shift summary", 14, TextAlignmentOptions.Center);
        subtitle.color = new Color(0.7f, 0.74f, 0.82f, 1f);
        subtitle.GetComponent<LayoutElement>().minHeight = 22;

        summaryText = CreateLabel(card.transform, "Summary", "", 16, TextAlignmentOptions.TopLeft);
        summaryText.richText = true;
        summaryText.textWrappingMode = TextWrappingModes.Normal;
        var summaryLe = summaryText.GetComponent<LayoutElement>();
        summaryLe.minHeight = 320;
        summaryLe.flexibleHeight = 1;

        continueButton = CreateButton(card.transform, "ContinueButton", "Continue to Next Day");
        panelRoot.transform.SetAsLastSibling();
        panelRoot.SetActive(false);
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().minHeight = size + 8;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static Button CreateButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 44;
        le.preferredHeight = 44;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.28f, 0.48f, 0.36f, 1f);
        var btn = go.AddComponent<Button>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 17;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Canvas ResolvePlayerUICanvas()
    {
        var named = GameObject.Find("PlayerUI");
        if (named != null)
        {
            var c = named.GetComponent<Canvas>();
            if (c != null) return c;
        }

        var title = FindFirstObjectByType<TitleScreenController>();
        if (title != null && title.inGameUIRoot != null)
        {
            var c = title.inGameUIRoot.GetComponent<Canvas>();
            if (c != null) return c;
        }

        return FindFirstObjectByType<Canvas>();
    }
}
