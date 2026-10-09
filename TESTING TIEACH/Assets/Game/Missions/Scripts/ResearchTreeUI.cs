using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResearchTreeUI : MonoBehaviour
{
    sealed class NodeView
    {
        public ResearchProgressManager.Definition definition;
        public Button button;
        public Image background;
        public Image progress;
        public TextMeshProUGUI title, description, meta;
    }

    static ResearchTreeUI instance;
    RectTransform chart;
    TextMeshProUGUI activeStatus;
    TextMeshProUGUI feedback;
    readonly List<Button> tabs = new List<Button>();
    readonly List<NodeView> nodeViews = new List<NodeView>();
    readonly Dictionary<string, RectTransform> nodeRects = new Dictionary<string, RectTransform>();
    int selectedTab;
    float nextRefresh;

    ResearchProgressManager Manager
    {
        get
        {
            if (ResearchProgressManager.Instance != null) return ResearchProgressManager.Instance;
            ResearchProgressManager existing = FindFirstObjectByType<ResearchProgressManager>();
            return existing != null ? existing
                : new GameObject("ResearchProgressManager").AddComponent<ResearchProgressManager>();
        }
    }

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
        Sfx.Play(SfxId.UiOpen);
    }

    void OnEnable()
    {
        if (ResearchProgressManager.Instance != null)
            ResearchProgressManager.Instance.Changed += OnResearchChanged;
    }

    void OnDisable()
    {
        if (ResearchProgressManager.Instance != null)
            ResearchProgressManager.Instance.Changed -= OnResearchChanged;
    }

    void OnResearchChanged() { RefreshStatus(); RefreshNodes(); }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        RefreshStatus();
        RefreshNodes();
    }

    void Close()
    {
        gameObject.SetActive(false);
        Sfx.Play(SfxId.UiClose);
    }

    void Build()
    {
        RectTransform overlay = (RectTransform)transform;
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.03f, 0.86f);

        var panel = new GameObject("ResearchPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(1000f, 790f);
        panel.GetComponent<Image>().color = new Color(0.105f, 0.12f, 0.16f, 0.995f);

        TextMeshProUGUI title = CreateText(panel.transform, "Title", "RESEARCH & DEVELOPMENT", 24f,
            FontStyles.Bold, new Vector2(28f, -54f), new Vector2(-90f, -10f), TextAlignmentOptions.Left);
        title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        TextMeshProUGUI hint = CreateText(panel.transform, "Hint", "DRAG TO PAN   •   SCROLL TO ZOOM", 11f,
            FontStyles.Bold, new Vector2(30f, -76f), new Vector2(-90f, -54f), TextAlignmentOptions.Left);
        hint.rectTransform.anchorMin = new Vector2(0f, 1f); hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.pivot = new Vector2(0.5f, 1f); hint.color = GameUITheme.TextSecondary;

        Button close = CreateButton(panel.transform, "Close", "X", new Vector2(-64f, -58f),
            new Vector2(-18f, -12f), new Color(0.58f, 0.27f, 0.29f, 1f));
        close.onClick.AddListener(Close);

        var tabRow = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabRow.transform.SetParent(panel.transform, false);
        RectTransform tabsRt = (RectTransform)tabRow.transform;
        tabsRt.anchorMin = new Vector2(0f, 1f); tabsRt.anchorMax = new Vector2(1f, 1f); tabsRt.pivot = new Vector2(0.5f, 1f);
        tabsRt.offsetMin = new Vector2(24f, -126f); tabsRt.offsetMax = new Vector2(-24f, -84f);
        HorizontalLayoutGroup tabLayout = tabRow.GetComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 8f; tabLayout.childControlWidth = tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        string[] names = { "PRODUCTION", "WORKFORCE", "AUTOMATION" };
        for (int i = 0; i < names.Length; i++)
        {
            int tab = i; Button button = CreateLayoutButton(tabRow.transform, names[i]);
            button.onClick.AddListener(() => { ShowTab(tab); Sfx.Play(SfxId.UiClick); });
            tabs.Add(button);
        }

        var statusBar = new GameObject("ActiveResearch", typeof(RectTransform), typeof(Image));
        statusBar.transform.SetParent(panel.transform, false);
        RectTransform statusRt = (RectTransform)statusBar.transform;
        statusRt.anchorMin = new Vector2(0f, 1f); statusRt.anchorMax = new Vector2(1f, 1f); statusRt.pivot = new Vector2(0.5f, 1f);
        statusRt.offsetMin = new Vector2(24f, -172f); statusRt.offsetMax = new Vector2(-24f, -134f);
        statusBar.GetComponent<Image>().color = new Color(0.075f, 0.09f, 0.12f, 1f);
        activeStatus = CreateText(statusBar.transform, "Status", "NO ACTIVE RESEARCH", 13f, FontStyles.Bold,
            new Vector2(14f, 0f), new Vector2(-220f, 0f), TextAlignmentOptions.Left);
        feedback = CreateText(statusBar.transform, "Feedback", "Select an available project", 11f, FontStyles.Normal,
            new Vector2(650f, 0f), new Vector2(-14f, 0f), TextAlignmentOptions.Right);
        feedback.color = GameUITheme.TextSecondary;

        var viewport = new GameObject("ResearchViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(24f, 24f); viewportRt.offsetMax = new Vector2(-24f, -182f);
        viewport.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.09f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var chartObject = new GameObject("ResearchChart", typeof(RectTransform));
        chartObject.transform.SetParent(viewport.transform, false);
        chart = (RectTransform)chartObject.transform;
        chart.anchorMin = chart.anchorMax = chart.pivot = new Vector2(0.5f, 0.5f);
        chart.sizeDelta = new Vector2(1600f, 1350f);
        RecipeGraphDrag drag = viewport.AddComponent<RecipeGraphDrag>();
        drag.content = chart; drag.minimumZoom = 0.55f; drag.maximumZoom = 1.55f;
        ShowTab(0);
    }

    void ShowTab(int tab)
    {
        selectedTab = Mathf.Clamp(tab, 0, 2);
        if (chart == null) return;
        for (int i = chart.childCount - 1; i >= 0; i--)
        {
            GameObject old = chart.GetChild(i).gameObject; old.SetActive(false); Destroy(old);
        }
        nodeViews.Clear(); nodeRects.Clear();
        chart.anchoredPosition = Vector2.zero; chart.localScale = Vector3.one;
        for (int i = 0; i < tabs.Count; i++)
            tabs[i].GetComponent<Image>().color = i == selectedTab
                ? new Color(0.27f, 0.64f, 0.50f, 1f) : new Color(0.15f, 0.18f, 0.24f, 1f);

        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition.category == selectedTab) CreateNode(definition);
        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition.category == selectedTab && !string.IsNullOrEmpty(definition.parentId)
                && nodeRects.TryGetValue(definition.parentId, out RectTransform parent))
                CreateConnector(parent, nodeRects[definition.id]);
        foreach (NodeView view in nodeViews) view.button.transform.SetAsLastSibling();
        RefreshStatus(); RefreshNodes();
    }

    void CreateNode(ResearchProgressManager.Definition definition)
    {
        var node = new GameObject(definition.id, typeof(RectTransform), typeof(Image), typeof(Button));
        node.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)node.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(290f, 132f); rt.anchoredPosition = definition.position;
        Image background = node.GetComponent<Image>();
        Button button = node.GetComponent<Button>(); button.targetGraphic = background;

        var stripe = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        stripe.transform.SetParent(node.transform, false);
        RectTransform stripeRt = (RectTransform)stripe.transform;
        stripeRt.anchorMin = Vector2.zero; stripeRt.anchorMax = new Vector2(0f, 1f);
        stripeRt.offsetMin = Vector2.zero; stripeRt.offsetMax = new Vector2(6f, 0f);
        stripe.GetComponent<Image>().color = new Color(0.30f, 0.80f, 0.66f, 1f);

        TextMeshProUGUI title = CreateText(node.transform, "Title", definition.title, 16f, FontStyles.Bold,
            new Vector2(18f, 82f), new Vector2(-12f, -12f), TextAlignmentOptions.Left);
        TextMeshProUGUI description = CreateText(node.transform, "Description", definition.description, 11f, FontStyles.Normal,
            new Vector2(18f, 42f), new Vector2(-12f, -48f), TextAlignmentOptions.TopLeft);
        description.color = GameUITheme.TextSecondary; description.textWrappingMode = TextWrappingModes.Normal;
        TextMeshProUGUI meta = CreateText(node.transform, "Meta", "", 11f, FontStyles.Bold,
            new Vector2(18f, 14f), new Vector2(-12f, -92f), TextAlignmentOptions.Left);

        var track = new GameObject("ProgressTrack", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(node.transform, false);
        RectTransform trackRt = (RectTransform)track.transform;
        trackRt.anchorMin = Vector2.zero; trackRt.anchorMax = new Vector2(1f, 0f);
        trackRt.offsetMin = new Vector2(12f, 7f); trackRt.offsetMax = new Vector2(-12f, 13f);
        track.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 1f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        RectTransform fillRt = (RectTransform)fill.transform;
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        Image progress = fill.GetComponent<Image>(); progress.color = new Color(0.30f, 0.80f, 0.66f, 1f);

        NodeView view = new NodeView { definition = definition, button = button, background = background,
            progress = progress, title = title, description = description, meta = meta };
        button.onClick.AddListener(() => StartResearch(view));
        nodeViews.Add(view); nodeRects[definition.id] = rt;
    }

    void StartResearch(NodeView view)
    {
        if (view == null) return;
        bool started = Manager.TryStart(view.definition.id, out string reason);
        feedback.text = reason;
        feedback.color = started ? GameUITheme.PositiveAccent : GameUITheme.Danger;
        RefreshStatus(); RefreshNodes();
    }

    void RefreshStatus()
    {
        if (activeStatus == null) return;
        ResearchProgressManager manager = Manager;
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (manager.HasActiveResearch)
        {
            ResearchProgressManager.Definition active = ResearchProgressManager.Get(manager.ActiveId);
            activeStatus.text = "RESEARCHING  " + (active != null ? active.title : "Project")
                + "   •   " + FormatTime(manager.RemainingMinutes) + " REMAINING";
            activeStatus.color = GameUITheme.PositiveAccent;
        }
        else
        {
            activeStatus.text = "NO ACTIVE RESEARCH   •   AVAILABLE CASH $" + (money != null ? money.CurrentMoney : 0);
            activeStatus.color = GameUITheme.TextPrimary;
        }
    }

    void RefreshNodes()
    {
        ResearchProgressManager manager = Manager;
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        foreach (NodeView view in nodeViews)
        {
            var d = view.definition;
            bool complete = manager.IsCompleted(d.id);
            bool active = manager.IsActive(d.id);
            bool milestone = manager.IsMilestoneAvailable(d);
            bool prerequisite = manager.HasPrerequisite(d);
            bool available = !complete && !active && !manager.HasActiveResearch && milestone && prerequisite;
            bool affordable = money != null && money.CanAfford(d.cost);
            view.button.interactable = available;
            view.background.color = complete ? new Color(0.13f, 0.38f, 0.30f, 1f)
                : active ? new Color(0.18f, 0.34f, 0.40f, 1f)
                : available ? new Color(0.16f, 0.20f, 0.27f, 1f)
                : new Color(0.095f, 0.11f, 0.15f, 1f);
            view.title.color = complete || active || available ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
            if (complete) view.meta.text = "<color=#73D9A0>COMPLETED</color>";
            else if (active) view.meta.text = "<color=#73D9C5>" + FormatTime(manager.RemainingMinutes) + " REMAINING</color>";
            else if (!milestone) view.meta.text = "REQUIRES MILESTONE " + d.milestone;
            else if (!prerequisite) view.meta.text = "REQUIRES " + (ResearchProgressManager.Get(d.parentId)?.title ?? "PREREQUISITE").ToUpperInvariant();
            else if (manager.HasActiveResearch) view.meta.text = "RESEARCH SLOT IN USE";
            else view.meta.text = (affordable ? "<color=#F2C66D>" : "<color=#E07A7A>")
                + "$" + d.cost + "</color>   •   " + FormatTime(d.durationMinutes);

            float progress = complete ? 1f : active ? 1f - manager.RemainingMinutes / Mathf.Max(1f, d.durationMinutes) : 0f;
            RectTransform fill = (RectTransform)view.progress.transform;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }
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
        rt.anchoredPosition = (a + b) * 0.5f; rt.sizeDelta = new Vector2(delta.magnitude, 3f);
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.GetComponent<Image>().color = new Color(0.22f, 0.56f, 0.62f, 0.8f);
        line.transform.SetAsFirstSibling();
    }

    static string FormatTime(float minutes)
    {
        int rounded = Mathf.CeilToInt(Mathf.Max(0f, minutes));
        return rounded >= 60 ? (rounded / 60) + "h " + (rounded % 60) + "m" : rounded + "m";
    }

    static Button CreateLayoutButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        CreateText(go.transform, "Label", label, 13f, FontStyles.Bold, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    static Button CreateButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.offsetMin = min; rt.offsetMax = max;
        go.GetComponent<Image>().color = color;
        CreateText(go.transform, "Label", label, 18f, FontStyles.Bold, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, Vector2 min, Vector2 max, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = min; rt.offsetMax = max;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value; text.fontSize = size; text.fontStyle = style; text.alignment = alignment;
        text.color = GameUITheme.TextPrimary; text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }
}
