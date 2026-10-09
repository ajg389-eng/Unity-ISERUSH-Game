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

    TextMeshProUGUI revenueValue;
    TextMeshProUGUI cashChangeValue;
    TextMeshProUGUI ordersValue;
    TextMeshProUGUI efficiencyValue;
    TextMeshProUGUI balanceValue;
    TextMeshProUGUI averageWaitValue;
    TextMeshProUGUI walkedOutValue;
    TextMeshProUGUI wasteValue;

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
        string diagnosis = BuildOperationsDiagnosis(stats, cashChange, avgWait, lost, waste);

        if (revenueValue != null && cashChangeValue != null && ordersValue != null &&
            efficiencyValue != null && balanceValue != null && averageWaitValue != null &&
            walkedOutValue != null && wasteValue != null)
        {
            revenueValue.text = "$" + revenue;
            cashChangeValue.text = cashSign + "$" + cashChange;
            cashChangeValue.color = cashChange >= 0
                ? new Color(0.49f, 0.86f, 0.54f) : new Color(0.88f, 0.48f, 0.48f);
            ordersValue.text = orders.ToString();
            efficiencyValue.text = efficiency.ToString("F0") + "%";
            bool hadCustomers = orders + lost > 0;
            efficiencyValue.color = !hadCustomers ? new Color(0.72f, 0.76f, 0.82f)
                : efficiency >= 80f ? new Color(0.49f, 0.86f, 0.54f)
                : efficiency >= 50f ? new Color(0.95f, 0.78f, 0.43f)
                : new Color(0.88f, 0.48f, 0.48f);
            balanceValue.text = "$" + balance;
            averageWaitValue.text = avgWait.ToString("F1") + "s";
            walkedOutValue.text = lost.ToString();
            wasteValue.text = waste.ToString();
            summaryText.text = diagnosis;
            return;
        }

        summaryText.text =
            "<color=#AAB7D1><b>FINANCIAL</b></color>\n" +
            $"Revenue<pos=38%><b>${revenue}</b><pos=57%>Cash change<pos=84%><color={cashColor}><b>{cashSign}${cashChange}</b></color>\n" +
            $"Ending balance<pos=38%><b>${balance}</b>\n\n" +
            "<color=#AAB7D1><b>CUSTOMERS</b></color>\n" +
            $"Orders served<pos=38%><b>{orders}</b><pos=57%>Walked out<pos=84%><b>{lost}</b>\n" +
            $"Service efficiency<pos=38%><b>{efficiency:F0}%</b>\n\n" +
            "<color=#AAB7D1><b>OPERATIONS</b></color>\n" +
            $"Average wait<pos=38%><b>{avgWait:F1}s</b><pos=57%>Food wasted<pos=84%><b>{waste}</b>\n\n" +
            "<color=#AAB7D1><b>PROCESS REVIEW</b></color>\n" + diagnosis;
    }

    static string BuildOperationsDiagnosis(StoreStatisticsManager stats, int cashChange, float avgWait, int lost, int waste)
    {
        var notes = new System.Collections.Generic.List<string>();

        if (lost > 0 || avgWait >= 35f)
            notes.Add("<color=#F2C66D><b>Queue:</b></color> Demand exceeded capacity. Inspect the register and bottleneck station.");
        else if (stats != null && stats.OrdersCompletedToday > 0)
            notes.Add("<color=#7DDB8A><b>Flow:</b></color> Customer waiting remained controlled.");

        if (waste > 0)
            notes.Add("<color=#F2C66D><b>Waste:</b></color> Lower pickup stock or match output to demand.");
        if (cashChange < 0)
            notes.Add("<color=#F2C66D><b>Cost:</b></color> Spending exceeded revenue. Check capacity utilization.");

        var workflowNotes = WorkflowAnalysis.GetSystemDiagnostics();
        if (workflowNotes.Count > 0 && notes.Count < 3)
            notes.Add("<color=#83D9ED><b>System:</b></color> " + workflowNotes[0]);

        if (notes.Count == 0)
            notes.Add("<color=#7DDB8A><b>Stable:</b></color> No major operational loss detected.");

        var lines = new System.Text.StringBuilder();
        int noteCount = Mathf.Min(notes.Count, 3);
        for (int i = 0; i < noteCount; i++)
            lines.Append("\u2022 ").Append(notes[i]).Append(i + 1 < noteCount ? "\n" : "");
        return lines.ToString();
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
            titleText = panelRoot.transform.Find("Title")?.GetComponent<TextMeshProUGUI>()
                ?? panelRoot.transform.Find("Card/Title")?.GetComponent<TextMeshProUGUI>();
        if (summaryText == null)
            summaryText = panelRoot.transform.Find("Summary")?.GetComponent<TextMeshProUGUI>()
                ?? panelRoot.transform.Find("Card/SummaryPanel/Summary")?.GetComponent<TextMeshProUGUI>();
        if (continueButton == null)
            continueButton = panelRoot.transform.Find("ContinueButton")?.GetComponent<Button>()
                ?? panelRoot.transform.Find("Card/ContinueButton")?.GetComponent<Button>();
        revenueValue = FindText("Card/HeadlineMetrics/Revenue/Value");
        cashChangeValue = FindText("Card/HeadlineMetrics/CashChange/Value");
        ordersValue = FindText("Card/HeadlineMetrics/Orders/Value");
        efficiencyValue = FindText("Card/HeadlineMetrics/Efficiency/Value");
        balanceValue = FindText("Card/OperationsMetrics/Balance/Value");
        averageWaitValue = FindText("Card/OperationsMetrics/AverageWait/Value");
        walkedOutValue = FindText("Card/OperationsMetrics/WalkedOut/Value");
        wasteValue = FindText("Card/OperationsMetrics/Waste/Value");
    }

    TextMeshProUGUI FindText(string path) => panelRoot != null
        ? panelRoot.transform.Find(path)?.GetComponent<TextMeshProUGUI>() : null;

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
        cardRt.sizeDelta = new Vector2(820f, 660f);

        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.12f, 0.13f, 0.18f, 0.98f);
        cardImg.raycastTarget = true;

        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 24, 24);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        titleText = CreateLabel(card.transform, "Title", "Day Complete", 30, TextAlignmentOptions.Center);
        titleText.fontStyle = FontStyles.Bold;
        titleText.GetComponent<LayoutElement>().minHeight = 42;

        var subtitle = CreateLabel(card.transform, "Subtitle", "SHIFT PERFORMANCE SUMMARY", 13, TextAlignmentOptions.Center);
        subtitle.color = new Color(0.7f, 0.74f, 0.82f, 1f);
        subtitle.GetComponent<LayoutElement>().minHeight = 24;

        var headline = CreateMetricRow(card.transform, "HeadlineMetrics", 142f);
        revenueValue = CreateMetricCard(headline.transform, "Revenue", "REVENUE", "$0",
            new Color(0.18f, 0.30f, 0.28f, 1f), new Color(0.49f, 0.86f, 0.54f));
        cashChangeValue = CreateMetricCard(headline.transform, "CashChange", "CASH CHANGE", "$0",
            new Color(0.18f, 0.30f, 0.28f, 1f), new Color(0.49f, 0.86f, 0.54f));
        ordersValue = CreateMetricCard(headline.transform, "Orders", "ORDERS SERVED", "0",
            new Color(0.16f, 0.24f, 0.31f, 1f), new Color(0.51f, 0.85f, 0.93f));
        efficiencyValue = CreateMetricCard(headline.transform, "Efficiency", "SERVICE RATE", "0%",
            new Color(0.16f, 0.24f, 0.31f, 1f), new Color(0.51f, 0.85f, 0.93f));

        var operations = CreateMetricRow(card.transform, "OperationsMetrics", 104f);
        balanceValue = CreateMetricCard(operations.transform, "Balance", "ENDING CASH", "$0",
            new Color(0.11f, 0.13f, 0.18f, 1f), Color.white, 24f);
        averageWaitValue = CreateMetricCard(operations.transform, "AverageWait", "AVERAGE WAIT", "0.0s",
            new Color(0.11f, 0.13f, 0.18f, 1f), Color.white, 24f);
        walkedOutValue = CreateMetricCard(operations.transform, "WalkedOut", "WALKED OUT", "0",
            new Color(0.11f, 0.13f, 0.18f, 1f), Color.white, 24f);
        wasteValue = CreateMetricCard(operations.transform, "Waste", "FOOD WASTED", "0",
            new Color(0.11f, 0.13f, 0.18f, 1f), Color.white, 24f);

        var summaryPanel = new GameObject("SummaryPanel", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        summaryPanel.transform.SetParent(card.transform, false);
        summaryPanel.GetComponent<Image>().color = new Color(0.075f, 0.085f, 0.12f, 0.92f);
        summaryPanel.GetComponent<Image>().raycastTarget = false;
        var summaryPanelLayout = summaryPanel.GetComponent<LayoutElement>();
        summaryPanelLayout.minHeight = 175f;
        summaryPanelLayout.flexibleHeight = 1f;

        TextMeshProUGUI reviewTitle = CreateLabel(summaryPanel.transform, "ReviewTitle", "PROCESS REVIEW", 13,
            TextAlignmentOptions.TopLeft);
        reviewTitle.fontStyle = FontStyles.Bold;
        reviewTitle.color = new Color(0.65f, 0.72f, 0.84f, 1f);
        RectTransform reviewTitleRt = (RectTransform)reviewTitle.transform;
        reviewTitleRt.anchorMin = new Vector2(0f, 1f);
        reviewTitleRt.anchorMax = new Vector2(1f, 1f);
        reviewTitleRt.offsetMin = new Vector2(18f, -42f);
        reviewTitleRt.offsetMax = new Vector2(-18f, -14f);

        summaryText = CreateLabel(summaryPanel.transform, "Summary", "", 15, TextAlignmentOptions.TopLeft);
        summaryText.richText = true;
        summaryText.textWrappingMode = TextWrappingModes.Normal;
        summaryText.lineSpacing = 7f;
        var summaryRt = (RectTransform)summaryText.transform;
        summaryRt.anchorMin = Vector2.zero;
        summaryRt.anchorMax = Vector2.one;
        summaryRt.offsetMin = new Vector2(18f, 14f);
        summaryRt.offsetMax = new Vector2(-18f, -48f);

        continueButton = CreateButton(card.transform, "ContinueButton", "Continue to Next Day");
        panelRoot.transform.SetAsLastSibling();
        panelRoot.SetActive(false);
    }

    static GameObject CreateMetricRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        var element = row.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        return row;
    }

    static TextMeshProUGUI CreateMetricCard(Transform parent, string name, string label, string initialValue,
        Color background, Color valueColor, float valueSize = 30f)
    {
        var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        card.transform.SetParent(parent, false);

        var image = card.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = false;

        var layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 10);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var labelText = CreateLabel(card.transform, "Label", label, 11f, TextAlignmentOptions.Center);
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color(0.68f, 0.73f, 0.82f, 1f);
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 9f;
        labelText.fontSizeMax = 11f;
        labelText.GetComponent<LayoutElement>().minHeight = 22f;

        var value = CreateLabel(card.transform, "Value", initialValue, valueSize, TextAlignmentOptions.Center);
        value.fontStyle = FontStyles.Bold;
        value.color = valueColor;
        value.enableAutoSizing = true;
        value.fontSizeMin = 18f;
        value.fontSizeMax = valueSize;
        var valueLayout = value.GetComponent<LayoutElement>();
        valueLayout.minHeight = valueSize + 10f;
        valueLayout.flexibleHeight = 1f;
        return value;
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
        le.minHeight = 50;
        le.preferredHeight = 50;

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
        tmp.fontSize = 18;
        tmp.fontStyle = FontStyles.Bold;
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
