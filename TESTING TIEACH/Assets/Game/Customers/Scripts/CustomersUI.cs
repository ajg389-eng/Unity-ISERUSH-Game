using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management tab: current customer arrivals + required kitchen output per ordered item.
/// </summary>
public class CustomersUI : MonoBehaviour
{
    public float refreshInterval = 0.4f;

    float nextRefresh;
    bool built;

    TextMeshProUGUI arrivalsSummary;
    TextMeshProUGUI demandTitle;
    TextMeshProUGUI[] demandHeader;
    readonly List<TextMeshProUGUI> metricValues = new List<TextMeshProUGUI>();
    readonly List<Image> metricCardTargets = new List<Image>();
    Button[] demandHeaderInfoButtons;
    Transform demandListRoot;
    RectTransform demandViewport;
    ScrollRect demandScroll;
    readonly List<TextMeshProUGUI[]> demandRows = new List<TextMeshProUGUI[]>();
    readonly List<RawImage> demandImages = new List<RawImage>();
    readonly List<Image[]> demandReputationStars = new List<Image[]>();
    GameObject columnHelpPopup;
    TextMeshProUGUI columnHelpText;
    int openHelpColumn = -1;
    static readonly float[] demandColumnMinimumWidths = { 190f, 85f, 95f, 90f, 95f, 95f, 160f };
    readonly float[] demandColumnWidths = (float[])demandColumnMinimumWidths.Clone();
    static readonly string[] demandColumnHelp =
    {
        "The menu item tracked by this row.",
        "Orders accepted at the register that have not been completed at pickup.",
        "The recent rate of customer orders accepted at the register for this item.",
        "Forecast customer demand for this item, in items per minute.",
        "Item units delivered to a Pickup Station during the last 60 seconds.",
        "Estimated steady output from active flows, considering supply, station and worker timing, and pickup space.",
        "Current item reputation and the demand change that takes effect next shift."
    };

    public Component TutorialMetricTarget(int index) =>
        index >= 0 && index < metricCardTargets.Count ? metricCardTargets[index] : null;
    public Component TutorialArrivalsTarget => arrivalsSummary;
    public Component TutorialDemandHeaderTarget(int index) =>
        demandHeaderInfoButtons != null && index >= 0 && index < demandHeaderInfoButtons.Length
            ? demandHeaderInfoButtons[index] : null;

    void OnEnable()
    {
        EnsureBuilt();
        RefreshAll();
    }

    void OnDisable()
    {
        if (columnHelpPopup != null)
            columnHelpPopup.SetActive(false);
        openHelpColumn = -1;
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;
        RefreshAll();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;

        var panelImage = GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = Color.clear;
            panelImage.raycastTarget = true;
            if (panelImage.GetComponent<GameUIThemeStyled>() == null)
                panelImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        HidePlaceholderCopy();

        var title = transform.Find("Title");
        if (title != null)
        {
            var tmp = title.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = "Analytics";
        }

        BuildMetricCards();

        arrivalsSummary = CreateLabel(transform, "ArrivalsSummary",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -124), new Vector2(-28, 24),
            13, TextAlignmentOptions.MidlineLeft);
        arrivalsSummary.richText = true;
        arrivalsSummary.textWrappingMode = TextWrappingModes.NoWrap;
        arrivalsSummary.enableAutoSizing = true;
        arrivalsSummary.fontSizeMin = 11f;
        arrivalsSummary.fontSizeMax = 13f;
        arrivalsSummary.overflowMode = TextOverflowModes.Overflow;

        demandTitle = CreateLabel(transform, "DemandTitle",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -150), new Vector2(-28, 30),
            18, TextAlignmentOptions.MidlineLeft);
        demandTitle.text = "Item Demand & Production";
        demandTitle.textWrappingMode = TextWrappingModes.NoWrap;
        demandTitle.enableAutoSizing = true;
        demandTitle.fontSizeMin = 14f;
        demandTitle.fontSizeMax = 18f;
        demandTitle.overflowMode = TextOverflowModes.Overflow;

        var scrollGo = new GameObject("DemandScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.offsetMin = new Vector2(12, 12);
        scrollRt.offsetMax = new Vector2(-12, -184);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        demandScroll = scroll;
        GameUITheme.ConfigureScroll(scroll);
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var verticalScrollbarObject = new GameObject("VerticalScrollbar",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        verticalScrollbarObject.transform.SetParent(scrollGo.transform, false);
        RectTransform verticalScrollbarRect = (RectTransform)verticalScrollbarObject.transform;
        verticalScrollbarRect.anchorMin = new Vector2(1f, 0f);
        verticalScrollbarRect.anchorMax = new Vector2(1f, 1f);
        verticalScrollbarRect.pivot = new Vector2(1f, 0.5f);
        verticalScrollbarRect.anchoredPosition = new Vector2(-2f, 0f);
        verticalScrollbarRect.sizeDelta = new Vector2(8f, 0f);
        Image verticalTrack = verticalScrollbarObject.GetComponent<Image>();
        verticalTrack.color = new Color(0.10f, 0.12f, 0.16f, 0.9f);
        var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleObject.transform.SetParent(verticalScrollbarObject.transform, false);
        RectTransform handleRect = (RectTransform)handleObject.transform;
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(1f, 1f);
        handleRect.offsetMax = new Vector2(-1f, -1f);
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = GameUITheme.MutedText;
        Scrollbar verticalScrollbar = verticalScrollbarObject.GetComponent<Scrollbar>();
        verticalScrollbar.handleRect = handleRect;
        verticalScrollbar.targetGraphic = handleImage;
        verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = verticalScrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = -10f;

        var horizontalScrollbarObject = new GameObject("HorizontalScrollbar",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        horizontalScrollbarObject.transform.SetParent(scrollGo.transform, false);
        RectTransform horizontalScrollbarRect = (RectTransform)horizontalScrollbarObject.transform;
        horizontalScrollbarRect.anchorMin = new Vector2(0f, 0f);
        horizontalScrollbarRect.anchorMax = new Vector2(1f, 0f);
        horizontalScrollbarRect.pivot = new Vector2(0.5f, 0f);
        horizontalScrollbarRect.anchoredPosition = new Vector2(0f, 2f);
        horizontalScrollbarRect.sizeDelta = new Vector2(0f, 8f);
        Image horizontalTrack = horizontalScrollbarObject.GetComponent<Image>();
        horizontalTrack.color = new Color(0.10f, 0.12f, 0.16f, 0.9f);
        var horizontalHandleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        horizontalHandleObject.transform.SetParent(horizontalScrollbarObject.transform, false);
        RectTransform horizontalHandleRect = (RectTransform)horizontalHandleObject.transform;
        horizontalHandleRect.anchorMin = Vector2.zero;
        horizontalHandleRect.anchorMax = Vector2.one;
        horizontalHandleRect.offsetMin = new Vector2(1f, 1f);
        horizontalHandleRect.offsetMax = new Vector2(-1f, -1f);
        Image horizontalHandleImage = horizontalHandleObject.GetComponent<Image>();
        horizontalHandleImage.color = GameUITheme.MutedText;
        Scrollbar horizontalScrollbar = horizontalScrollbarObject.GetComponent<Scrollbar>();
        horizontalScrollbar.handleRect = horizontalHandleRect;
        horizontalScrollbar.targetGraphic = horizontalHandleImage;
        horizontalScrollbar.direction = Scrollbar.Direction.LeftToRight;
        scroll.horizontalScrollbar = horizontalScrollbar;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.horizontalScrollbarSpacing = -10f;

        var scrollImage = scrollGo.GetComponent<Image>();
        if (scrollImage != null)
        {
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            if (scrollImage.GetComponent<GameUIThemeStyled>() == null)
                scrollImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = (RectTransform)viewport.transform;
        demandViewport = vpRt;
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        viewportImage.raycastTarget = true;
        viewport.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        viewportImage.gameObject.AddComponent<GameUIThemeStyled>();
        var viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        demandListRoot = content.transform;
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        // Preserve readable columns on narrow panels; wider panels stretch the item column.
        contentRt.sizeDelta = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 1;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        demandHeader = CreateDemandTableRow("Header", 42f, out _);
        string[] headings = { "Item", "Open\nOrders", "Ordered\n/min", "Demand\n/min", "Produced\n/min", "Predicted\n/min", "Reputation /\nNext Shift" };
        for (int i = 0; i < headings.Length; i++)
        {
            demandHeader[i].text = headings[i];
            demandHeader[i].fontStyle = FontStyles.Bold;
            demandHeader[i].fontSize = 13f;
            demandHeader[i].enableAutoSizing = true;
            demandHeader[i].fontSizeMin = 12f;
            demandHeader[i].fontSizeMax = 13f;
            demandHeader[i].textWrappingMode = TextWrappingModes.Normal;
            demandHeader[i].overflowMode = TextOverflowModes.Overflow;
            demandHeader[i].color = new Color(0.72f, 0.77f, 0.86f, 1f);
        }
        BuildColumnHelpButtons();
        var demandHeaderImage = demandHeader[0].transform.parent.parent.GetComponent<Image>();
        if (demandHeaderImage != null)
        {
            demandHeaderImage.color = Color.clear;
            if (demandHeaderImage.GetComponent<GameUIThemeStyled>() == null)
                demandHeaderImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        scroll.viewport = vpRt;
        scroll.content = contentRt;
        var rectMask = viewport.GetComponent<RectMask2D>();
        if (rectMask != null) Destroy(rectMask);
        var demandMask = viewport.GetComponent<Mask>();
        if (demandMask == null) demandMask = viewport.gameObject.AddComponent<Mask>();
        demandMask.showMaskGraphic = false;
    }

    void BuildColumnHelpButtons()
    {
        demandHeaderInfoButtons = new Button[demandHeader.Length];
        for (int i = 0; i < demandHeader.Length; i++)
        {
            RectTransform cell = demandHeader[i].transform.parent as RectTransform;
            if (cell == null) continue;

            RectTransform label = demandHeader[i].rectTransform;
            label.offsetMax = new Vector2(-25f, -4f);

            GameObject infoObject = new GameObject("ColumnInfo" + i,
                typeof(RectTransform), typeof(Image), typeof(Button));
            infoObject.transform.SetParent(cell, false);
            RectTransform infoRect = (RectTransform)infoObject.transform;
            infoRect.anchorMin = infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.pivot = new Vector2(1f, 1f);
            infoRect.anchoredPosition = new Vector2(-3f, -3f);
            infoRect.sizeDelta = new Vector2(19f, 19f);
            Image infoImage = infoObject.GetComponent<Image>();
            infoImage.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            Button infoButton = infoObject.GetComponent<Button>();
            infoButton.targetGraphic = infoImage;
            GameUITheme.ApplyCompactControlEffects(infoButton);

            TextMeshProUGUI infoLabel = CreateLabel(infoObject.transform, "Label",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                12f, TextAlignmentOptions.Center);
            infoLabel.text = "i";
            infoLabel.fontStyle = FontStyles.Bold;
            infoLabel.raycastTarget = false;
            int columnIndex = i;
            Button capturedButton = infoButton;
            infoButton.onClick.AddListener(() => ToggleColumnHelp(columnIndex, capturedButton));
            demandHeaderInfoButtons[i] = infoButton;
        }
    }

    void ToggleColumnHelp(int columnIndex, Button source)
    {
        if (columnHelpPopup == null)
            BuildColumnHelpPopup();

        if (columnHelpPopup.activeSelf && openHelpColumn == columnIndex)
        {
            columnHelpPopup.SetActive(false);
            openHelpColumn = -1;
            return;
        }

        openHelpColumn = columnIndex;
        columnHelpText.text = demandColumnHelp[Mathf.Clamp(columnIndex, 0, demandColumnHelp.Length - 1)];
        PositionColumnHelpPopup(source);
        columnHelpPopup.SetActive(true);
        columnHelpPopup.transform.SetAsLastSibling();
    }

    void BuildColumnHelpPopup()
    {
        columnHelpPopup = new GameObject("ColumnHelpPopup",
            typeof(RectTransform), typeof(Image), typeof(Outline));
        columnHelpPopup.transform.SetParent(transform, false);
        RectTransform popupRect = (RectTransform)columnHelpPopup.transform;
        popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = new Vector2(310f, 94f);
        Image popupImage = columnHelpPopup.GetComponent<Image>();
        popupImage.color = GameUITheme.Surface;
        popupImage.raycastTarget = true;
        Outline outline = columnHelpPopup.GetComponent<Outline>();
        outline.effectColor = GameUITheme.Accent;
        outline.effectDistance = new Vector2(2f, -2f);

        columnHelpText = CreateLabel(columnHelpPopup.transform, "Explanation",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            14f, TextAlignmentOptions.TopLeft);
        columnHelpText.rectTransform.offsetMin = new Vector2(12f, 12f);
        columnHelpText.rectTransform.offsetMax = new Vector2(-34f, -12f);
        columnHelpText.textWrappingMode = TextWrappingModes.Normal;
        columnHelpText.overflowMode = TextOverflowModes.Truncate;

        GameObject closeObject = new GameObject("Close",
            typeof(RectTransform), typeof(Image), typeof(Button));
        closeObject.transform.SetParent(columnHelpPopup.transform, false);
        RectTransform closeRect = (RectTransform)closeObject.transform;
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-5f, -5f);
        closeRect.sizeDelta = new Vector2(24f, 24f);
        Image closeImage = closeObject.GetComponent<Image>();
        closeImage.color = GameUITheme.Danger;
        Button closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.AddListener(() =>
        {
            columnHelpPopup.SetActive(false);
            openHelpColumn = -1;
        });
        TextMeshProUGUI closeLabel = CreateLabel(closeObject.transform, "Label",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            14f, TextAlignmentOptions.Center);
        closeLabel.text = "x";
        closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.raycastTarget = false;
        columnHelpPopup.SetActive(false);
    }

    void PositionColumnHelpPopup(Button source)
    {
        if (source == null || columnHelpPopup == null) return;
        RectTransform rootRect = transform as RectTransform;
        RectTransform sourceRect = source.transform as RectTransform;
        if (rootRect == null || sourceRect == null) return;

        Vector3[] corners = new Vector3[4];
        sourceRect.GetWorldCorners(corners);
        Vector3 worldCenter = (corners[0] + corners[2]) * 0.5f;
        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas rootCanvas = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen,
                eventCamera, out Vector2 local))
            return;

        RectTransform popupRect = (RectTransform)columnHelpPopup.transform;
        float halfWidth = popupRect.sizeDelta.x * 0.5f;
        float halfHeight = popupRect.sizeDelta.y * 0.5f;
        Rect bounds = rootRect.rect;
        local.x = Mathf.Clamp(local.x, bounds.xMin + halfWidth + 8f, bounds.xMax - halfWidth - 8f);
        local.y = Mathf.Clamp(local.y - halfHeight - 12f,
            bounds.yMin + halfHeight + 8f, bounds.yMax - halfHeight - 8f);
        popupRect.anchoredPosition = local;
    }

    void HidePlaceholderCopy()
    {
        var subtitle = transform.Find("Subtitle");
        if (subtitle != null)
            subtitle.gameObject.SetActive(false);
    }

    void BuildMetricCards()
    {
        var row = new GameObject("LiveMetrics", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(transform, false);
        var rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -46f);
        rowRect.sizeDelta = new Vector2(-28f, 66f);
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        CreateMetricCard(row.transform, "Customers in Store", new Color(0.58f, 0.47f, 0.93f, 1f));
        CreateMetricCard(row.transform, "Order to Pickup", new Color(0.29f, 0.72f, 0.95f, 1f));
        CreateMetricCard(row.transform, "Orders Served / Min", new Color(0.25f, 0.78f, 0.62f, 1f));
        CreateMetricCard(row.transform, "Service Rate", new Color(0.98f, 0.68f, 0.30f, 1f));
    }

    void CreateMetricCard(Transform parent, string label, Color accent)
    {
        var card = new GameObject(label.Replace(' ', '_'), typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        card.transform.SetParent(parent, false);
        var cardImage = card.GetComponent<Image>();
        cardImage.color = Color.Lerp(GameUITheme.Surface, accent, 0.24f);
        cardImage.raycastTarget = false;
        cardImage.gameObject.AddComponent<GameUIThemeStyled>();
        var outline = cardImage.gameObject.AddComponent<Outline>();
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(1f, -1f);
        metricCardTargets.Add(cardImage);
        card.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var caption = CreateLabel(card.transform, "Label",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            12, TextAlignmentOptions.MidlineLeft);
        caption.text = label;
        SetRect(caption.rectTransform, new Vector2(0f, 0.52f), Vector2.one, new Vector2(10f, 0f), new Vector2(-8f, -5f));
        caption.fontStyle = FontStyles.Bold;
        caption.color = new Color(0.72f, 0.77f, 0.86f, 1f);
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.enableAutoSizing = true;
        caption.fontSizeMin = 9f;
        caption.fontSizeMax = 12f;
        caption.overflowMode = TextOverflowModes.Overflow;

        var value = CreateLabel(card.transform, "Value",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            21, TextAlignmentOptions.MidlineLeft);
        value.text = "—";
        SetRect(value.rectTransform, Vector2.zero, new Vector2(1f, 0.58f), new Vector2(10f, 3f), new Vector2(-8f, 0f));
        value.fontStyle = FontStyles.Bold;
        metricValues.Add(value);
    }

    static TextMeshProUGUI CreateLabel(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPos,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Truncate;
        return tmp;
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    void RefreshAll()
    {
        EnsureBuilt();
        RefreshSummary();
        RefreshArrivalsSummary();
        RefreshDemand();
    }

    void RefreshSummary()
    {
        if (metricValues.Count < 4) return;
        var stats = StoreStatisticsManager.Instance;
        metricValues[0].text = stats != null ? stats.CustomersInSystem.ToString() : "—";
        metricValues[1].text = stats != null && stats.OrdersCompletedToday > 0
            ? stats.AverageWaitTimeTodaySeconds.ToString("F1") + " s"
            : "No data";
        metricValues[2].text = stats != null
            ? (stats.OrdersCompletedToday > 0 ? stats.ThroughputOrdersPerMinute.ToString("F1") : "Warming up")
            : "No data";
        int servedOrLost = stats != null ? stats.OrdersCompletedToday + stats.CustomersLostToday : 0;
        metricValues[3].text = stats == null || servedOrLost == 0
            ? "Warming up"
            : stats.ServiceEfficiencyPercent.ToString("F0") + "%";
    }

    void RefreshArrivalsSummary()
    {
        if (arrivalsSummary == null) return;

        var stats = StoreStatisticsManager.Instance;
        if (stats == null)
        {
            arrivalsSummary.text = "Arrivals this hour: <color=#38B7A3>—</color> actual / <color=#F6C85F>—</color> planned";
            return;
        }

        int[] buckets = stats.GetVisitTrendBuckets();
        int currentIdx = stats.GetCurrentHourBucketIndex();
        int actual = currentIdx >= 0 && currentIdx < buckets.Length ? buckets[currentIdx] : 0;
        int gameHour = stats.ShiftStartHour + Mathf.Max(0, currentIdx);
        var spawner = FindFirstObjectByType<CustomerSpawner>();
        float planned = spawner != null ? spawner.ExpectedCustomersPerGameHour(gameHour) : 0f;
        var timeManager = GameTimeManager.Instance;
        if (timeManager != null)
        {
            float hourStart = gameHour * 60f;
            float elapsedFraction = Mathf.Clamp01((timeManager.CurrentMinutes - hourStart) / 60f);
            planned *= elapsedFraction;
        }

        arrivalsSummary.text = $"Arrivals this hour: <color=#38B7A3>{actual}</color> actual / <color=#F6C85F>{Mathf.RoundToInt(planned)}</color> planned so far";
    }
    void RefreshDemand()
    {
        if (demandListRoot == null) return;
        var pm = ProductionManager.Instance != null
            ? ProductionManager.Instance
            : FindObjectOfType<ProductionManager>();
        List<ProductionManager.ItemOutputNeed> needs = pm != null
            ? pm.GetRequiredOutputByItem(includeDrinks: true)
            : new List<ProductionManager.ItemOutputNeed>();

        EnsureDemandRowCount(Mathf.Max(1, needs.Count));
        demandTitle.text = needs.Count == 1
            ? "Item Demand & Production (1 item)"
            : $"Item Demand & Production ({needs.Count} items)";
        UpdateDemandTableLayout();

        // Hide entire rows so inactive items leave no backgrounds or layout gaps.
        for (int i = 0; i < demandRows.Count; i++)
            demandRows[i][0].transform.parent.parent.gameObject.SetActive(i < Mathf.Max(1, needs.Count));

        if (needs.Count == 0)
        {
            for (int column = 0; column < demandRows[0].Length; column++)
                demandRows[0][column].text = column == 0 ? "No cookable menu items yet." : "";
            foreach (Image star in demandReputationStars[0])
                if (star != null) star.enabled = false;
            if (demandImages.Count > 0 && demandImages[0] != null)
            {
                demandImages[0].texture = null;
                demandImages[0].enabled = false;
            }
            return;
        }

        for (int i = 0; i < demandRows.Count; i++)
        {
            if (i >= needs.Count)
            {
                continue;
            }

            var row = needs[i];
            if (i < demandImages.Count && demandImages[i] != null)
            {
                var preview = ItemPreviewThumbnails.Get(row.item);
                demandImages[i].texture = preview;
                demandImages[i].enabled = preview != null;
            }
            string name = row.item != null
                ? (string.IsNullOrEmpty(row.item.itemName) ? row.item.name : row.item.itemName)
                : "Item";
            var cells = demandRows[i];
            cells[0].text = name;
            cells[1].text = row.openOrdersAtRegister.ToString();
            cells[2].text = row.orderedPerMinute.ToString("F1");
            cells[3].text = row.requiredPerMinute.ToString("F1");
            cells[4].text = row.producedLastMinute.ToString();
            cells[5].text = pm != null
                ? pm.GetPredictedOutputPerMinute(row.item).ToString("F1")
                : "0.0";
            CustomerOrderConfig config = pm != null ? pm.orderConfig : null;
            int reputation = config != null ? config.GetItemReputation(row.item) : 0;
            int pendingChange = config != null ? config.GetPendingItemDemandChangePerMinute(row.item) : 0;
            int starsEarned = CustomerOrderConfig.GetItemReputationStars(reputation);
            foreach (Image star in demandReputationStars[i])
                if (star != null) star.enabled = true;
            SetReputationStars(i, starsEarned);
            int progressToNextStar = CustomerOrderConfig.GetServedCountProgressToNextStar(reputation);
            int servedForNextStar = CustomerOrderConfig.GetServedCountNeededForNextStar(reputation);
            string reputationDetail = pendingChange > 0
                ? $"+{pendingChange}/min next shift"
                : pendingChange < 0
                    ? $"{pendingChange}/min next shift"
                    : starsEarned >= CustomerOrderConfig.MaxReputationStars
                        ? "MAX DEMAND"
                        : "+5/min at next star";
            string reputationProgress = starsEarned >= CustomerOrderConfig.MaxReputationStars
                ? $"{reputation} reputation points"
                : $"{progressToNextStar}/{servedForNextStar} points to next star";
            cells[6].text = $"{reputationProgress}\n{reputationDetail}";
            var rowImage = cells[0].transform.parent.parent.GetComponent<Image>();
            if (rowImage != null)
                rowImage.color = Color.clear;
        }
    }

    void EnsureDemandRowCount(int count)
    {
        while (demandRows.Count < count)
        {
            TextMeshProUGUI[] row = CreateDemandTableRow("DemandRow", 60f, out var preview);
            demandRows.Add(row);
            demandImages.Add(preview);
            demandReputationStars.Add(CreateReputationStars(row[6]));
        }
    }

    Image[] CreateReputationStars(TextMeshProUGUI label)
    {
        int count = CustomerOrderConfig.MaxReputationStars;
        var stars = new Image[count];
        Sprite sprite = GameIconLibrary.GetSprite(GameIconId.Star);
        for (int i = 0; i < count; i++)
        {
            var starObject = new GameObject($"ReputationStar{i + 1}",
                typeof(RectTransform), typeof(Image));
            starObject.transform.SetParent(label.transform.parent, false);

            RectTransform rect = (RectTransform)starObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * 18f, 15f);
            rect.sizeDelta = new Vector2(16f, 16f);

            Image image = starObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            stars[i] = image;
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = new Vector2(1f, 0.55f);
        labelRect.offsetMin = new Vector2(2f, 1f);
        labelRect.offsetMax = new Vector2(-2f, -1f);
        return stars;
    }

    void SetReputationStars(int rowIndex, int earned)
    {
        if (rowIndex < 0 || rowIndex >= demandReputationStars.Count) return;
        Image[] stars = demandReputationStars[rowIndex];
        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;
            stars[i].color = i < earned
                ? new Color(1f, 0.78f, 0.30f, 1f)
                : new Color(0.31f, 0.37f, 0.44f, 0.8f);
        }
    }

    TextMeshProUGUI[] CreateDemandTableRow(string name, float height, out RawImage preview)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(demandListRoot, false);
        var rowImage = go.GetComponent<Image>();
        rowImage.color = Color.clear;
        rowImage.gameObject.AddComponent<GameUIThemeStyled>();
        var rowSize = go.GetComponent<LayoutElement>();
        rowSize.minHeight = height;
        rowSize.preferredHeight = height;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 1f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        preview = null;
        var cells = new TextMeshProUGUI[demandColumnWidths.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new GameObject("Column" + i, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            cell.transform.SetParent(go.transform, false);
            var cellImage = cell.GetComponent<Image>();
            cellImage.color = Color.clear;
            cellImage.gameObject.AddComponent<GameUIThemeStyled>();
            var size = cell.GetComponent<LayoutElement>();
            size.minWidth = demandColumnWidths[i];
            size.preferredWidth = demandColumnWidths[i];
            size.flexibleWidth = i == 0 ? 1f : 0f;
            var label = CreateLabel(cell.transform, "Text", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, 13f,
                i == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(i == 0 && name != "Header" ? 44f : 4f, 4f), new Vector2(-4f, -4f));
            label.raycastTarget = false;
        label.fontSize = 13f;
            label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 13f;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.gameObject.AddComponent<GameUIThemeStyled>();
            cells[i] = label;
            if (i != 0 || name == "Header") continue;

            var imageGo = new GameObject("ProductImage", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(cell.transform, false);
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = imageRect.anchorMax = new Vector2(0f, 0.5f);
            imageRect.pivot = new Vector2(0f, 0.5f);
            imageRect.anchoredPosition = new Vector2(6f, 0f);
            imageRect.sizeDelta = new Vector2(36f, 36f);
            preview = imageGo.GetComponent<RawImage>();
            preview.raycastTarget = false;
        }

        if (name != "Header")
        {
            var separator = new GameObject("RowSeparator",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            separator.transform.SetParent(go.transform, false);
            LayoutElement separatorLayout = separator.GetComponent<LayoutElement>();
            separatorLayout.ignoreLayout = true;
            RectTransform separatorRect = (RectTransform)separator.transform;
            separatorRect.anchorMin = new Vector2(0f, 0f);
            separatorRect.anchorMax = new Vector2(1f, 0f);
            separatorRect.pivot = new Vector2(0.5f, 0f);
            separatorRect.offsetMin = new Vector2(8f, 0f);
            separatorRect.offsetMax = new Vector2(-8f, 0f);
            separatorRect.sizeDelta = new Vector2(0f, 1f);
            Image separatorImage = separator.GetComponent<Image>();
            separatorImage.color = new Color(1f, 1f, 1f, 0.42f);
            separatorImage.raycastTarget = false;
        }

        return cells;
    }

    void UpdateDemandTableLayout()
    {
        if (demandListRoot == null || demandViewport == null) return;

        const float contentInsetsAndGaps = 14f; // 4 px padding at each side + 1 px between columns.
        float minimumColumnsWidth = 0f;
        for (int i = 0; i < demandColumnMinimumWidths.Length; i++)
        {
            demandColumnWidths[i] = demandColumnMinimumWidths[i];
            minimumColumnsWidth += demandColumnMinimumWidths[i];
        }

        float viewportWidth = Mathf.Max(1f, demandViewport.rect.width);
        float targetContentWidth = Mathf.Max(viewportWidth, minimumColumnsWidth + contentInsetsAndGaps);
        demandColumnWidths[0] += targetContentWidth - contentInsetsAndGaps - minimumColumnsWidth;

        var contentRect = (RectTransform)demandListRoot;
        contentRect.sizeDelta = new Vector2(Mathf.Max(0f, targetContentWidth - viewportWidth), contentRect.sizeDelta.y);
        if (demandScroll != null)
            demandScroll.horizontal = targetContentWidth > viewportWidth + 0.5f;

        ApplyDemandColumnWidths(demandHeader);
        foreach (var row in demandRows)
            ApplyDemandColumnWidths(row);
    }

    void ApplyDemandColumnWidths(TextMeshProUGUI[] cells)
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Length && i < demandColumnWidths.Length; i++)
        {
            var cell = cells[i].transform.parent;
            var size = cell.GetComponent<LayoutElement>();
            if (size == null) continue;
            size.minWidth = demandColumnWidths[i];
            size.preferredWidth = demandColumnWidths[i];
        }
    }
}
