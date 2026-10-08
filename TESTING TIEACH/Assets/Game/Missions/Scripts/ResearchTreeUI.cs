using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ResearchTreeUI : MonoBehaviour
{
    sealed class Entry
    {
        public string title;
        public string description;
        public int milestone;
        public Vector2 position;
        public int parent;

        public Entry(string title, string description, int milestone, Vector2 position, int parent = -1)
        {
            this.title = title;
            this.description = description;
            this.milestone = milestone;
            this.position = position;
            this.parent = parent;
        }
    }

    static ResearchTreeUI instance;
    RectTransform chart;
    readonly List<Button> tabs = new List<Button>();
    int selectedTab;

    public static void Open(Transform canvas)
    {
        if (instance == null)
        {
            var root = new GameObject("ResearchOverlay", typeof(RectTransform), typeof(Image), typeof(ResearchTreeUI));
            root.transform.SetParent(canvas, false);
            instance = root.GetComponent<ResearchTreeUI>();
            instance.Build();
        }
        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
        instance.ShowTab(instance.selectedTab);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Close()
    {
        gameObject.SetActive(false);
        Sfx.Play(SfxId.UiClose);
    }

    void Build()
    {
        RectTransform overlay = (RectTransform)transform;
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.04f, 0.8f);

        var panel = new GameObject("ResearchPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(900f, 760f);
        panel.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.995f);

        TextMeshProUGUI title = CreateText(panel.transform, "Title", "RESEARCH  |  DRAG TO PAN  |  SCROLL TO ZOOM", 20f,
            FontStyles.Bold, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(24f, -58f);
        titleRect.offsetMax = new Vector2(-82f, -12f);
        Button close = CreateButton(panel.transform, "Close", "X", new Vector2(-62f, -58f),
            new Vector2(-16f, -12f), new Color(0.58f, 0.27f, 0.29f, 1f));
        close.onClick.AddListener(Close);

        var tabRow = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabRow.transform.SetParent(panel.transform, false);
        RectTransform tabsRt = (RectTransform)tabRow.transform;
        tabsRt.anchorMin = new Vector2(0f, 1f);
        tabsRt.anchorMax = new Vector2(1f, 1f);
        tabsRt.pivot = new Vector2(0.5f, 1f);
        tabsRt.offsetMin = new Vector2(20f, -108f);
        tabsRt.offsetMax = new Vector2(-20f, -66f);
        var layout = tabRow.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 7f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        string[] names = { "PRODUCTION", "WORKFORCE", "AUTOMATION" };
        for (int i = 0; i < names.Length; i++)
        {
            int tab = i;
            Button button = CreateLayoutButton(tabRow.transform, names[i]);
            button.onClick.AddListener(() => { ShowTab(tab); Sfx.Play(SfxId.UiClick); });
            tabs.Add(button);
        }

        var viewport = new GameObject("ResearchViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(20f, 20f);
        viewportRt.offsetMax = new Vector2(-20f, -118f);
        viewport.GetComponent<Image>().color = new Color(0.075f, 0.088f, 0.115f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var chartObject = new GameObject("ResearchChart", typeof(RectTransform));
        chartObject.transform.SetParent(viewport.transform, false);
        chart = (RectTransform)chartObject.transform;
        chart.anchorMin = chart.anchorMax = chart.pivot = new Vector2(0.5f, 0.5f);
        chart.sizeDelta = new Vector2(1500f, 1300f);
        var drag = viewport.AddComponent<RecipeGraphDrag>();
        drag.content = chart;
        drag.minimumZoom = 0.55f;
        drag.maximumZoom = 1.55f;
        ShowTab(0);
    }

    void ShowTab(int tab)
    {
        selectedTab = Mathf.Clamp(tab, 0, 2);
        if (chart == null) return;
        for (int i = chart.childCount - 1; i >= 0; i--) Destroy(chart.GetChild(i).gameObject);
        chart.anchoredPosition = Vector2.zero;
        chart.localScale = Vector3.one;
        for (int i = 0; i < tabs.Count; i++)
            tabs[i].GetComponent<Image>().color = i == selectedTab
                ? new Color(0.28f, 0.63f, 0.49f, 1f) : new Color(0.17f, 0.2f, 0.25f, 1f);

        List<Entry> entries = selectedTab == 0 ? ProductionEntries()
            : selectedTab == 1 ? WorkforceEntries() : AutomationEntries();
        var nodes = new List<RectTransform>();
        foreach (Entry entry in entries) nodes.Add(CreateNode(entry));
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].parent >= 0) CreateConnector(nodes[entries[i].parent], nodes[i]);
        foreach (RectTransform node in nodes) node.SetAsLastSibling();
    }

    static List<Entry> ProductionEntries() => new List<Entry>
    {
        new Entry("Restaurant Basics", "Core MK1 production", 1, new Vector2(0, 390)),
        new Entry("Additional Stations", "Buy extra station copies", 2, new Vector2(-250, 150), 0),
        new Entry("MK2 Stations", "4 input and 4 output slots", 3, new Vector2(250, 150), 0),
        new Entry("Expanded Capacity", "Higher station ownership limits", 4, new Vector2(-250, -110), 1),
        new Entry("Advanced Processing", "Higher-volume production layouts", 4, new Vector2(250, -110), 2),
        new Entry("Industrial Kitchen", "Maximum production capacity", 4, new Vector2(0, -370), 3)
    };

    static List<Entry> WorkforceEntries() => new List<Entry>
    {
        new Entry("Core Workforce", "Hire and assign workers", 1, new Vector2(0, 390)),
        new Entry("Fourth Worker", "Increase the hiring limit", 2, new Vector2(-250, 150), 0),
        new Entry("Multi-Worker Flows", "Assign two workers to one flow", 2, new Vector2(250, 150), 0),
        new Entry("Carry Training", "Unlock higher carry upgrades", 3, new Vector2(-250, -110), 1),
        new Entry("Specialist Training", "Improve processing efficiency", 4, new Vector2(250, -110), 2),
        new Entry("Expert Workforce", "Maximum worker capacity", 4, new Vector2(0, -370), 3)
    };

    static List<Entry> AutomationEntries() => new List<Entry>
    {
        new Entry("Manual Operations", "Order and monitor supplies", 1, new Vector2(0, 390)),
        new Entry("Queue Analytics", "See queues and utilization", 2, new Vector2(-250, 150), 0),
        new Entry("Production Targets", "Set desired product inventory", 3, new Vector2(250, 150), 0),
        new Entry("Automatic Ordering", "Reorder ingredients below a target", 4, new Vector2(-250, -110), 1),
        new Entry("Bottleneck Insights", "Identify constrained stations", 4, new Vector2(250, -110), 2),
        new Entry("Smart Operations", "Coordinate targets and supplies", 4, new Vector2(0, -370), 3)
    };

    RectTransform CreateNode(Entry entry)
    {
        bool unlocked = MilestoneFeatures.HasReached(entry.milestone);
        var node = new GameObject(entry.title.Replace(" ", string.Empty), typeof(RectTransform), typeof(Image));
        node.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)node.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(260f, 112f);
        rt.anchoredPosition = entry.position;
        node.GetComponent<Image>().color = unlocked
            ? new Color(0.16f, 0.43f, 0.34f, 1f) : new Color(0.12f, 0.14f, 0.19f, 1f);
        string status = unlocked ? "UNLOCKED" : "MILESTONE " + entry.milestone;
        var text = CreateText(node.transform, "Label", entry.title + "\n<size=72%>" + entry.description
            + "\n<color=#73D9C5>" + status + "</color></size>", 16f, FontStyles.Bold,
            new Vector2(12f, 10f), new Vector2(-12f, -10f), TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.Normal;
        return rt;
    }

    void CreateConnector(RectTransform from, RectTransform to)
    {
        Vector2 a = from.anchoredPosition + new Vector2(0f, -from.sizeDelta.y * 0.5f);
        Vector2 b = to.anchoredPosition + new Vector2(0f, to.sizeDelta.y * 0.5f);
        Vector2 delta = b - a;
        var line = new GameObject("Connector", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)line.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(delta.magnitude, 3f);
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.GetComponent<Image>().color = new Color(0.28f, 0.78f, 0.82f, 0.9f);
        line.transform.SetAsFirstSibling();
    }

    static Button CreateLayoutButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.17f, 0.2f, 0.25f, 1f);
        CreateText(go.transform, "Label", label, 13f, FontStyles.Bold, Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    static Button CreateButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = min;
        rt.offsetMax = max;
        go.GetComponent<Image>().color = color;
        CreateText(go.transform, "Label", label, 18f, FontStyles.Bold, Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, Vector2 min, Vector2 max, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = min;
        rt.offsetMax = max;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = GameUITheme.TextPrimary;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }
}
