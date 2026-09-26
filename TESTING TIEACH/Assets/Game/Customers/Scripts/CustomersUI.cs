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

    TextMeshProUGUI summaryText;
    TextMeshProUGUI graphTitle;
    TextMeshProUGUI demandTitle;
    RectTransform graphBarsRoot;
    Transform demandListRoot;
    readonly List<Image> barFills = new List<Image>();
    readonly List<TextMeshProUGUI> barLabels = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> demandRows = new List<TextMeshProUGUI>();

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

        summaryText = CreateLabel(transform, "Summary",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -48), new Vector2(-28, 36),
            16, TextAlignmentOptions.TopLeft);

        graphTitle = CreateLabel(transform, "GraphTitle",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -92), new Vector2(-28, 24),
            15, TextAlignmentOptions.MidlineLeft);
        graphTitle.text = "Visit trend (10 AM – 10 PM)";

        var graphFrame = new GameObject("VisitGraph", typeof(RectTransform));
        graphFrame.transform.SetParent(transform, false);
        var graphRt = (RectTransform)graphFrame.transform;
        graphRt.anchorMin = new Vector2(0, 1);
        graphRt.anchorMax = new Vector2(1, 1);
        graphRt.pivot = new Vector2(0.5f, 1f);
        graphRt.anchoredPosition = new Vector2(0, -120);
        graphRt.sizeDelta = new Vector2(-28, 150);
        var frameImg = graphFrame.AddComponent<Image>();
        frameImg.color = new Color(0.12f, 0.13f, 0.16f, 0.95f);

        var barsGo = new GameObject("Bars", typeof(RectTransform));
        barsGo.transform.SetParent(graphFrame.transform, false);
        graphBarsRoot = (RectTransform)barsGo.transform;
        graphBarsRoot.anchorMin = Vector2.zero;
        graphBarsRoot.anchorMax = Vector2.one;
        graphBarsRoot.offsetMin = new Vector2(8, 26);
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
            new Vector2(0, -286), new Vector2(-28, 24),
            15, TextAlignmentOptions.MidlineLeft);
        demandTitle.text = "Live demand and throughput (last 60 simulation seconds)";

        var scrollGo = new GameObject("DemandScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.offsetMin = new Vector2(12, 12);
        scrollRt.offsetMax = new Vector2(-12, -318);

        var scroll = scrollGo.AddComponent<ScrollRect>();
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
        track.AddComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f, 1f);

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

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(col.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0.24f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 9;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        label.text = "—";
        label.enableWordWrapping = true;
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

    void RefreshAll()
    {
        EnsureBuilt();
        RefreshSummary();
        RefreshGraph();
        RefreshDemand();
    }

    void RefreshSummary()
    {
        if (summaryText == null) return;
        var stats = StoreStatisticsManager.Instance;
        int inSystem = stats != null ? stats.CustomersInSystem : 0;
        int visited = stats != null ? stats.CustomersVisitedToday : 0;
        int lost = stats != null ? stats.CustomersLostToday : 0;
        summaryText.text =
            $"In store now: <b>{inSystem}</b>    Visited today: <b>{visited}</b>    Walked out: <b>{lost}</b>";
    }

    void RefreshGraph()
    {
        var stats = StoreStatisticsManager.Instance;
        if (stats == null || graphBarsRoot == null) return;

        int startH = stats.ShiftStartHour;
        int endH = stats.ShiftEndHour;
        if (graphTitle != null)
            graphTitle.text = $"Visit trend ({FormatClockHour(startH)} – {FormatClockHour(endH)})";

        int[] buckets = stats.GetVisitTrendBuckets();
        string[] hourLabels = stats.GetVisitHourLabels();
        int currentIdx = stats.GetCurrentHourBucketIndex();

        int max = 1;
        for (int i = 0; i < buckets.Length; i++)
            if (buckets[i] > max) max = buckets[i];

        while (barFills.Count < buckets.Length)
            CreateBarColumn(graphBarsRoot);

        Color normal = new Color(0.35f, 0.72f, 0.95f, 1f);
        Color current = new Color(0.95f, 0.72f, 0.28f, 1f);

        for (int i = 0; i < barFills.Count; i++)
        {
            bool active = i < buckets.Length;
            barFills[i].transform.parent.parent.gameObject.SetActive(active);
            if (!active) continue;

            int value = buckets[i];
            float t = max > 0 ? value / (float)max : 0f;
            var fillRt = (RectTransform)barFills[i].transform;
            fillRt.anchorMax = new Vector2(1f, Mathf.Clamp01(Mathf.Max(t, value > 0 ? 0.08f : 0f)));
            barFills[i].color = i == currentIdx ? current : normal;

            if (i < barLabels.Count)
            {
                string hour = i < hourLabels.Length ? hourLabels[i] : "";
                barLabels[i].text = value > 0 ? $"{hour}\n{value}" : hour;
            }
        }
    }

    static string FormatClockHour(int hour24)
    {
        hour24 = ((hour24 % 24) + 24) % 24;
        bool pm = hour24 >= 12;
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        return $"{hour12} {(pm ? "PM" : "AM")}";
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
            string name = row.item != null
                ? (string.IsNullOrEmpty(row.item.itemName) ? row.item.name : row.item.itemName)
                : "Item";
            demandRows[i].text =
                $"<b>{name}</b>  ·  outstanding <color=#FFB070><b>{row.requested}</b></color>" +
                $"  ·  ready {row.ready}  ·  in production {row.cooking}  ·  shortfall {row.requiredOutput}\n" +
                $"<size=85%>ordered {row.orderedLastMinute}/min  ·  completed {row.completedLastMinute}/min</size>";
        }
    }

    void EnsureDemandRowCount(int count)
    {
        while (demandRows.Count < count)
        {
            var go = new GameObject("DemandRow", typeof(RectTransform));
            go.transform.SetParent(demandListRoot, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 48;
            le.preferredHeight = 50;
            go.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.22f, 0.9f);
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 2);
            trt.offsetMax = new Vector2(-10, -2);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            demandRows.Add(tmp);
        }
    }
}
