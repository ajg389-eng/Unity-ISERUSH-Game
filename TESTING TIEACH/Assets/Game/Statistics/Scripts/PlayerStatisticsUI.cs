using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows lifetime performance totals for the active restaurant save.</summary>
public sealed class PlayerStatisticsUI : MonoBehaviour
{
    const float RefreshInterval = 0.5f;
    const int MetricCount = 13;
    const int ColumnCount = 3;
    const float CardHeight = 126f;

    readonly TextMeshProUGUI[] values = new TextMeshProUGUI[MetricCount];
    readonly TextMeshProUGUI[] details = new TextMeshProUGUI[MetricCount];
    StoreStatisticsManager statistics;
    RectTransform viewportRect;
    GridLayoutGroup grid;
    float nextRefresh;
    bool built;

    void OnEnable()
    {
        EnsureBuilt();
        BindStatistics();
        Refresh();
    }

    void OnDisable()
    {
        if (statistics != null)
            statistics.OnStatisticsChanged -= Refresh;
    }

    void Update()
    {
        UpdateCardWidth();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        BindStatistics();
        Refresh();
    }

    void BindStatistics()
    {
        StoreStatisticsManager current = StoreStatisticsManager.Instance
            ?? FindFirstObjectByType<StoreStatisticsManager>();
        if (statistics == current) return;
        if (statistics != null)
            statistics.OnStatisticsChanged -= Refresh;
        statistics = current;
        if (statistics != null)
            statistics.OnStatisticsChanged += Refresh;
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;

        Image background = GetComponent<Image>();
        if (background != null)
        {
            background.color = Color.clear;
            background.raycastTarget = false;
        }

        // Keep the page bounds assigned by AnalyticsPopupUI. Stretching this
        // component to the whole popup caused its heading and cards to cover
        // the analytics title and tabs.
        GameObject scrollObject = new GameObject("PlayerStatsScroll",
            typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(transform, false);
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;

        GameObject viewport = new GameObject("Viewport",
            typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollObject.transform, false);
        viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -4f);
        Image viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.015f);
        viewportImage.raycastTarget = true;

        GameObject content = new GameObject("MetricGrid",
            typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;

        grid = content.GetComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 12);
        grid.spacing = new Vector2(10f, 10f);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = ColumnCount;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;

        string[] labels =
        {
            "SHIFTS COMPLETED", "ORDERS SERVED", "TOTAL REVENUE",
            "CUSTOMERS ENTERED", "CUSTOMERS LOST", "FOOD WASTED",
            "AVERAGE ORDER WAIT", "CUSTOMER COMPLETION", "AVERAGE SALE",
            "AVERAGE REVENUE / SHIFT", "VISITORS / SHIFT", "BEST SHIFT REVENUE",
            "MOST ORDERS IN SHIFT"
        };
        string[] colors =
        {
            "#F2C66D", "#51CDA6", "#51CDA6", "#64BFE8", "#E27676", "#E2B455",
            "#64BFE8", "#51CDA6", "#51CDA6", "#F2C66D", "#F2C66D", "#F2C66D",
            "#F2C66D"
        };

        for (int i = 0; i < MetricCount; i++)
            CreateCard(content.transform, i, labels[i], colors[i]);
        UpdateCardWidth();
    }

    void CreateCard(Transform parent, int index, string label, string colorHex)
    {
        GameObject card = new GameObject("MetricCard" + index,
            typeof(RectTransform), typeof(Image));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = GameUITheme.Charcoal;

        Color accent = ColorUtility.TryParseHtmlString(colorHex, out Color parsed)
            ? parsed : GameUITheme.TextSecondary;
        TextMeshProUGUI title = CreateText(card.transform, "Label", label, 12f,
            FontStyles.Bold, accent, TextAlignmentOptions.MidlineLeft);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(12f, -32f);
        titleRect.offsetMax = new Vector2(-10f, -8f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.enableAutoSizing = true;
        title.fontSizeMin = 9f;
        title.fontSizeMax = 12f;

        values[index] = CreateText(card.transform, "Value", "0", 27f,
            FontStyles.Bold, GameUITheme.TextPrimary, TextAlignmentOptions.MidlineLeft);
        RectTransform valueRect = values[index].rectTransform;
        valueRect.anchorMin = new Vector2(0f, 0.34f);
        valueRect.anchorMax = new Vector2(1f, 0.72f);
        valueRect.offsetMin = new Vector2(12f, 0f);
        valueRect.offsetMax = new Vector2(-10f, 0f);
        values[index].enableAutoSizing = true;
        values[index].fontSizeMin = 17f;
        values[index].fontSizeMax = 27f;
        values[index].textWrappingMode = TextWrappingModes.NoWrap;

        details[index] = CreateText(card.transform, "Detail", string.Empty, 11f,
            FontStyles.Normal, GameUITheme.TextSecondary, TextAlignmentOptions.BottomLeft);
        RectTransform detailRect = details[index].rectTransform;
        detailRect.anchorMin = Vector2.zero;
        detailRect.anchorMax = Vector2.one;
        detailRect.offsetMin = new Vector2(12f, 8f);
        detailRect.offsetMax = new Vector2(-10f, -82f);
        details[index].enableAutoSizing = true;
        details[index].fontSizeMin = 9f;
        details[index].fontSizeMax = 11f;
        details[index].textWrappingMode = TextWrappingModes.Normal;
    }

    void UpdateCardWidth()
    {
        if (grid == null || viewportRect == null) return;
        float width = Mathf.Max(1f, viewportRect.rect.width);
        float available = width - grid.padding.horizontal - grid.spacing.x * (ColumnCount - 1);
        float cardWidth = Mathf.Max(1f, available / ColumnCount);
        if (Mathf.Abs(grid.cellSize.x - cardWidth) < 0.5f
            && Mathf.Abs(grid.cellSize.y - CardHeight) < 0.5f) return;
        grid.cellSize = new Vector2(cardWidth, CardHeight);
    }

    void Refresh()
    {
        if (statistics == null) return;
        int shifts = statistics.CareerShiftsCompleted;
        int orders = statistics.CareerOrdersServed;
        int visits = statistics.CareerCustomersVisited;
        int lost = statistics.CareerCustomersLost;
        int concluded = orders + lost;
        float conversion = concluded > 0 ? orders * 100f / concluded : 0f;

        values[0].text = shifts.ToString("N0");
        details[0].text = "Completed operating shifts in this save";
        values[1].text = orders.ToString("N0");
        details[1].text = "Average " + (shifts > 0 ? (orders / (float)shifts).ToString("F1") : "0.0") + " per shift";
        values[2].text = "$" + statistics.RevenueEarned.ToString("N0");
        details[2].text = "All completed customer sales";
        values[3].text = visits.ToString("N0");
        details[3].text = "Customers who entered the restaurant";
        values[4].text = lost.ToString("N0");
        details[4].text = concluded > 0 ? (lost * 100f / concluded).ToString("F1") + "% of completed or lost visits" : "No completed visits yet";
        values[5].text = statistics.MealsWasted.ToString("N0");
        details[5].text = "Meals expired before customer pickup";
        values[6].text = statistics.CareerAverageWaitTimeSeconds.ToString("F1") + " s";
        details[6].text = "Average order to full pickup";
        values[7].text = conversion.ToString("F1") + "%";
        details[7].text = "Served orders among served or lost customers";
        values[8].text = "$" + (orders > 0
            ? (statistics.RevenueEarned / (float)orders).ToString("F2") : "0.00");
        details[8].text = "Revenue per served order";
        values[9].text = "$" + (shifts > 0
            ? (statistics.RevenueEarned / (float)shifts).ToString("N0") : "0");
        details[9].text = "Average completed-shift revenue";
        values[10].text = shifts > 0
            ? (statistics.CareerCustomersVisited / (float)shifts).ToString("F1") : "0.0";
        details[10].text = "Average customers entering per shift";
        values[11].text = "$" + statistics.CareerBestShiftRevenue.ToString("N0");
        details[11].text = "Highest revenue in one completed shift";
        values[12].text = statistics.CareerBestShiftOrders.ToString("N0");
        details[12].text = "Highest orders served in one shift";
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string content,
        float size, FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name,
            typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        return text;
    }
}
