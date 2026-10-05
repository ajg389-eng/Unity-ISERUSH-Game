using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management tab: customer visit trend graph + required kitchen output per ordered item.
/// </summary>
public class CustomersUI : MonoBehaviour
{
    public float refreshInterval = 0.4f;

    float nextRefresh;
    bool built;

    TextMeshProUGUI graphTitle;
    TextMeshProUGUI demandTitle;
    readonly List<TextMeshProUGUI> metricValues = new List<TextMeshProUGUI>();
    RectTransform graphBarsRoot;
    Transform demandListRoot;
    readonly List<Image> barFills = new List<Image>();
    readonly List<Image> barTracks = new List<Image>();
    readonly List<Image> barRanges = new List<Image>();
    readonly List<Image> barMeans = new List<Image>();
    readonly List<TextMeshProUGUI> barValues = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> barLabels = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> demandRows = new List<TextMeshProUGUI>();
    readonly List<RawImage> demandImages = new List<RawImage>();

    void OnEnable()
    {
        EnsureBuilt();
        RefreshAll();
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

        HidePlaceholderCopy();

        var title = transform.Find("Title");
        if (title != null)
        {
            var tmp = title.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = "Customers";
        }

        BuildMetricCards();

        graphTitle = CreateLabel(transform, "GraphTitle",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -126), new Vector2(-28, 28),
            15, TextAlignmentOptions.MidlineLeft);
        graphTitle.text = "Arrivals / hour   <color=#38B7A3>Actual</color>  ·  <color=#F0BE55>Usual range (most hours)</color>  ·  <color=#F8F5EE>Average</color>";
        graphTitle.richText = true;

        var graphFrame = new GameObject("VisitGraph", typeof(RectTransform));
        graphFrame.transform.SetParent(transform, false);
        var graphRt = (RectTransform)graphFrame.transform;
        graphRt.anchorMin = new Vector2(0, 1);
        graphRt.anchorMax = new Vector2(1, 1);
        graphRt.pivot = new Vector2(0.5f, 1f);
        graphRt.anchoredPosition = new Vector2(0, -158);
        graphRt.sizeDelta = new Vector2(-28, 240);
        var frameImg = graphFrame.AddComponent<Image>();
        frameImg.color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

        var barsGo = new GameObject("Bars", typeof(RectTransform));
        barsGo.transform.SetParent(graphFrame.transform, false);
        graphBarsRoot = (RectTransform)barsGo.transform;
        graphBarsRoot.anchorMin = Vector2.zero;
        graphBarsRoot.anchorMax = Vector2.one;
        graphBarsRoot.offsetMin = new Vector2(8, 34);
        graphBarsRoot.offsetMax = new Vector2(-8, -8);
        var hlg = barsGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3;
        hlg.childAlignment = TextAnchor.LowerCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(2, 2, 2, 2);

        int bucketCount = StoreStatisticsManager.Instance != null
            ? StoreStatisticsManager.Instance.ShiftHourCount
            : 12;
        for (int i = 0; i < bucketCount; i++)
            CreateBarColumn(graphBarsRoot);

        demandTitle = CreateLabel(transform, "DemandTitle",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -410), new Vector2(-28, 30),
            18, TextAlignmentOptions.MidlineLeft);
        demandTitle.text = "Live demand by item";

        var scrollGo = new GameObject("DemandScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.offsetMin = new Vector2(12, 12);
        scrollRt.offsetMax = new Vector2(-12, -448);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        GameUITheme.ConfigureScroll(scroll);
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.horizontal = false;
        scroll.vertical = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = (RectTransform)viewport.transform;
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.02f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        demandListRoot = content.transform;
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0, 0);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRt;
        scroll.content = contentRt;
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
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        CreateMetricCard(row.transform, "WIP · IN SYSTEM", new Color(0.58f, 0.47f, 0.93f, 1f));
        CreateMetricCard(row.transform, "AVG WAIT", new Color(0.29f, 0.72f, 0.95f, 1f));
        CreateMetricCard(row.transform, "ORDERS / MIN", new Color(0.25f, 0.78f, 0.62f, 1f));
        CreateMetricCard(row.transform, "SERVICE RATE", new Color(0.98f, 0.68f, 0.30f, 1f));
    }

    void CreateMetricCard(Transform parent, string label, Color accent)
    {
        var card = new GameObject(label.Replace(' ', '_'), typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 0.98f);
        card.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var stripe = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        stripe.transform.SetParent(card.transform, false);
        var stripeRect = (RectTransform)stripe.transform;
        stripeRect.anchorMin = Vector2.zero;
        stripeRect.anchorMax = new Vector2(0f, 1f);
        stripeRect.sizeDelta = new Vector2(4f, 0f);
        stripe.GetComponent<Image>().color = accent;

        var caption = CreateLabel(card.transform, "Label",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            10, TextAlignmentOptions.MidlineLeft);
        caption.text = label;
        SetRect(caption.rectTransform, new Vector2(0f, 0.52f), Vector2.one, new Vector2(12f, 0f), new Vector2(-5f, -5f));
        caption.fontStyle = FontStyles.Bold;
        caption.color = new Color(0.72f, 0.77f, 0.86f, 1f);
        caption.enableWordWrapping = false;

        var value = CreateLabel(card.transform, "Value",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            21, TextAlignmentOptions.MidlineLeft);
        value.text = "—";
        SetRect(value.rectTransform, Vector2.zero, new Vector2(1f, 0.58f), new Vector2(12f, 3f), new Vector2(-5f, 0f));
        value.fontStyle = FontStyles.Bold;
        metricValues.Add(value);
    }

    void CreateBarColumn(Transform parent)
    {
        var col = new GameObject("Bar", typeof(RectTransform));
        col.transform.SetParent(parent, false);
        var le = col.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 10f;

        var track = new GameObject("Track", typeof(RectTransform));
        track.transform.SetParent(col.transform, false);
        var trackRt = (RectTransform)track.transform;
        trackRt.anchorMin = new Vector2(0.15f, 0.24f);
        trackRt.anchorMax = new Vector2(0.85f, 1f);
        trackRt.offsetMin = Vector2.zero;
        trackRt.offsetMax = Vector2.zero;
        var trackImage = track.AddComponent<Image>();
        trackImage.color = new Color(0.2f, 0.22f, 0.28f, 1f);
        barTracks.Add(trackImage);

        var range = new GameObject("ExpectedRange", typeof(RectTransform), typeof(Image));
        range.transform.SetParent(track.transform, false);
        var rangeRect = (RectTransform)range.transform;
        rangeRect.anchorMin = Vector2.zero;
        rangeRect.anchorMax = new Vector2(1f, 0f);
        rangeRect.pivot = new Vector2(0.5f, 0f);
        rangeRect.offsetMin = Vector2.zero;
        rangeRect.offsetMax = Vector2.zero;
        range.GetComponent<Image>().color = new Color(0.94f, 0.70f, 0.24f, 0.34f);
        range.GetComponent<Image>().raycastTarget = false;
        barRanges.Add(range.GetComponent<Image>());

        var meanLine = new GameObject("ExpectedMean", typeof(RectTransform), typeof(Image));
        meanLine.transform.SetParent(track.transform, false);
        var meanRect = (RectTransform)meanLine.transform;
        meanRect.anchorMin = new Vector2(0f, 0f);
        meanRect.anchorMax = new Vector2(1f, 0f);
        meanRect.pivot = new Vector2(0.5f, 0f);
        meanRect.sizeDelta = new Vector2(0f, 2f);
        meanLine.GetComponent<Image>().color = new Color(1f, 0.79f, 0.39f, 1f);
        meanLine.GetComponent<Image>().raycastTarget = false;
        barMeans.Add(meanLine.GetComponent<Image>());

        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(track.transform, false);
        var fillRt = (RectTransform)fill.transform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 0f);
        fillRt.pivot = new Vector2(0.5f, 0f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.35f, 0.72f, 0.95f, 1f);
        barFills.Add(fillImg);
        meanLine.transform.SetAsLastSibling();

        var valueGo = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
        valueGo.transform.SetParent(track.transform, false);
        var valueRect = (RectTransform)valueGo.transform;
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = new Vector2(1f, 0f);
        valueRect.pivot = new Vector2(0.5f, 0f);
        valueRect.sizeDelta = new Vector2(0f, 16f);
        var valueText = valueGo.GetComponent<TextMeshProUGUI>();
        valueText.fontSize = 10;
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.color = new Color(0.82f, 0.86f, 0.92f, 1f);
        valueText.raycastTarget = false;
        valueText.enableWordWrapping = false;
        barValues.Add(valueText);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(col.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0.24f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 10;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        label.text = "—";
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        barLabels.Add(label);
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
        tmp.overflowMode = TextOverflowModes.Ellipsis;
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
        RefreshGraph();
        RefreshDemand();
    }

    void RefreshSummary()
    {
        if (metricValues.Count < 4) return;
        var stats = StoreStatisticsManager.Instance;
        metricValues[0].text = stats != null ? stats.CustomersInSystem.ToString() : "—";
        metricValues[1].text = stats != null ? stats.AverageWaitTimeSeconds.ToString("F1") + " s" : "—";
        metricValues[2].text = stats != null ? stats.ThroughputOrdersPerMinute.ToString("F1") : "—";
        int servedOrLost = stats != null ? stats.OrdersCompletedToday + stats.CustomersLostToday : 0;
        metricValues[3].text = stats == null || servedOrLost == 0
            ? "—"
            : stats.ServiceEfficiencyPercent.ToString("F0") + "%";
    }

    void RefreshGraph()
    {
        var stats = StoreStatisticsManager.Instance;
        if (stats == null || graphBarsRoot == null) return;

        int startH = stats.ShiftStartHour;
        if (graphTitle != null)
            graphTitle.text = "Arrivals / hour   <color=#38B7A3>Actual</color>  ·  <color=#F0BE55>Usual range (most hours)</color>  ·  <color=#F8F5EE>Average</color>";

        int[] buckets = stats.GetVisitTrendBuckets();
        string[] hourLabels = stats.GetVisitHourLabels();
        int currentIdx = stats.GetCurrentHourBucketIndex();

        var spawner = FindFirstObjectByType<CustomerSpawner>();
        var lowerBounds = new int[buckets.Length];
        var upperBounds = new int[buckets.Length];
        var means = new float[buckets.Length];
        int max = 1;
        for (int i = 0; i < buckets.Length; i++)
        {
            means[i] = spawner != null ? spawner.ExpectedCustomersPerGameHour(startH + i) : 0f;
            GetPoissonRange(means[i], out lowerBounds[i], out upperBounds[i]);
            max = Mathf.Max(max, Mathf.Max(buckets[i], upperBounds[i]));
        }

        while (barFills.Count < buckets.Length)
            CreateBarColumn(graphBarsRoot);

        Color normal = new Color(0.22f, 0.72f, 0.66f, 1f);
        Color current = new Color(0.36f, 0.91f, 0.78f, 1f);

        for (int i = 0; i < barFills.Count; i++)
        {
            bool active = i < buckets.Length;
            barFills[i].transform.parent.parent.gameObject.SetActive(active);
            if (!active) continue;

            int value = buckets[i];
            float t = max > 0 ? value / (float)max : 0f;
            var fillRt = (RectTransform)barFills[i].transform;
            float barHeight = Mathf.Clamp01(Mathf.Max(t, value > 0 ? 0.06f : 0f));
            fillRt.anchorMax = new Vector2(1f, barHeight);
            barFills[i].color = i == currentIdx ? current : normal;
            if (i < barTracks.Count)
                barTracks[i].color = i == currentIdx
                    ? new Color(0.31f, 0.27f, 0.19f, 1f)
                    : new Color(0.2f, 0.22f, 0.28f, 1f);
            if (i < barValues.Count)
            {
                barValues[i].text = value.ToString();
                barValues[i].color = i == currentIdx
                    ? new Color(1f, 0.79f, 0.40f, 1f)
                    : new Color(0.82f, 0.86f, 0.92f, 1f);
                var valueRt = (RectTransform)barValues[i].transform;
                valueRt.anchoredPosition = new Vector2(0f, Mathf.Max(0f, barHeight * ((RectTransform)barTracks[i].transform).rect.height - 14f));
            }
            if (i < means.Length && i < barRanges.Count && i < barMeans.Count)
            {
                var trackRect = (RectTransform)barTracks[i].transform;
                var rangeRect = (RectTransform)barRanges[i].transform;
                rangeRect.anchorMin = new Vector2(0f, lowerBounds[i] / (float)max);
                rangeRect.anchorMax = new Vector2(1f, upperBounds[i] / (float)max);
                rangeRect.offsetMin = Vector2.zero;
                rangeRect.offsetMax = Vector2.zero;
                barRanges[i].color = i == currentIdx
                    ? new Color(1f, 0.73f, 0.25f, 0.46f)
                    : new Color(0.94f, 0.70f, 0.24f, 0.34f);
                var meanRect = (RectTransform)barMeans[i].transform;
                meanRect.anchoredPosition = new Vector2(0f, means[i] / max * trackRect.rect.height - 1f);
            }

            if (i < barLabels.Count)
            {
                string hour = i < hourLabels.Length ? hourLabels[i] : "";
                barLabels[i].text = hour;
            }
        }
    }

    static void GetPoissonRange(float mean, out int lower, out int upper)
    {
        if (mean <= 0f)
        {
            lower = 0;
            upper = 0;
            return;
        }

        double probability = System.Math.Exp(-mean);
        double cumulative = probability;
        int value = 0;
        lower = -1;
        upper = -1;
        while (value < 512)
        {
            if (lower < 0 && cumulative >= 0.025d) lower = value;
            if (upper < 0 && cumulative >= 0.975d)
            {
                upper = value;
                break;
            }
            value++;
            probability *= mean / value;
            cumulative += probability;
        }
        if (lower < 0) lower = 0;
        if (upper < 0) upper = value;
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

        if (needs.Count == 0)
        {
            demandRows[0].text = "No cookable menu items yet.";
            if (demandImages.Count > 0 && demandImages[0] != null)
            {
                demandImages[0].texture = null;
                demandImages[0].enabled = false;
            }
            for (int i = 1; i < demandRows.Count; i++)
                demandRows[i].gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < demandRows.Count; i++)
        {
            if (i >= needs.Count)
            {
                demandRows[i].gameObject.SetActive(false);
                continue;
            }

            demandRows[i].gameObject.SetActive(true);
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
            string state = row.requiredOutput > 0
                ? $"<color=#FF9C79><b>SHORT {row.requiredOutput}</b></color>"
                : row.requested > 0
                    ? "<color=#77D9AE><b>COVERED</b></color>"
                    : "<color=#9AA7B8>NO OPEN ORDERS</color>";
            demandRows[i].text =
                $"<b>{name}</b>  ·  {state}\n" +
                $"Open orders <b>{row.requested}</b>    Ready {row.ready}    Cooking {row.cooking}\n" +
                $"<size=85%>Last minute  ·  ordered {row.orderedLastMinute}    completed {row.completedLastMinute}</size>";
            var rowImage = demandRows[i].transform.parent.GetComponent<Image>();
            if (rowImage != null)
                rowImage.color = row.requiredOutput > 0
                    ? new Color(0.22f, 0.14f, 0.15f, 0.98f)
                    : new Color(0.13f, 0.16f, 0.20f, 0.98f);
        }
    }

    void EnsureDemandRowCount(int count)
    {
        while (demandRows.Count < count)
        {
            var go = new GameObject("DemandRow", typeof(RectTransform));
            go.transform.SetParent(demandListRoot, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 62;
            le.preferredHeight = 64;
            go.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.22f, 0.9f);
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(64, 2);
            trt.offsetMax = new Vector2(-10, -2);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            demandRows.Add(tmp);

            var imageGo = new GameObject("ProductImage", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            imageGo.transform.SetParent(go.transform, false);
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = new Vector2(0f, 0.5f);
            imageRect.anchorMax = new Vector2(0f, 0.5f);
            imageRect.pivot = new Vector2(0f, 0.5f);
            imageRect.anchoredPosition = new Vector2(9f, 0f);
            imageRect.sizeDelta = new Vector2(44f, 44f);
            var rawImage = imageGo.GetComponent<RawImage>();
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;
            var aspect = imageGo.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;
            demandImages.Add(rawImage);
        }
    }
}
