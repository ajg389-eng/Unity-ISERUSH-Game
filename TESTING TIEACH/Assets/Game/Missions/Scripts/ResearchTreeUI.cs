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
    Transform categoryTabs;
    TextMeshProUGUI activeStatus;
    TextMeshProUGUI feedback;
    int selectedCategory;
    readonly List<Button> categoryTabButtons = new List<Button>();
    readonly List<NodeView> nodeViews = new List<NodeView>();
    readonly Dictionary<string, RectTransform> nodeRects = new Dictionary<string, RectTransform>();
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
        instance.gameObject.SetActive(false);
        instance.transform.SetAsLastSibling();
        instance.BuildTree();
        Canvas.ForceUpdateCanvases();
        instance.gameObject.SetActive(true);
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
        TextMeshProUGUI hint = CreateText(panel.transform, "Hint", "DRAG TO PAN  |  SCROLL TO ZOOM", 11f,
            FontStyles.Bold, new Vector2(30f, -76f), new Vector2(-90f, -54f), TextAlignmentOptions.Left);
        hint.rectTransform.anchorMin = new Vector2(0f, 1f); hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.pivot = new Vector2(0.5f, 1f); hint.color = GameUITheme.TextSecondary;

        Button close = CreateButton(panel.transform, "Close", "X", new Vector2(-64f, -58f),
            new Vector2(-18f, -12f), new Color(0.58f, 0.27f, 0.29f, 1f));
        close.onClick.AddListener(Close);

        var statusBar = new GameObject("ActiveResearch", typeof(RectTransform), typeof(Image));
        statusBar.transform.SetParent(panel.transform, false);
        RectTransform statusRt = (RectTransform)statusBar.transform;
        statusRt.anchorMin = new Vector2(0f, 1f); statusRt.anchorMax = new Vector2(1f, 1f); statusRt.pivot = new Vector2(0.5f, 1f);
        statusRt.offsetMin = new Vector2(24f, -126f); statusRt.offsetMax = new Vector2(-24f, -84f);
        statusBar.GetComponent<Image>().color = new Color(0.075f, 0.09f, 0.12f, 1f);
        activeStatus = CreateText(statusBar.transform, "Status", "NO ACTIVE RESEARCH", 13f, FontStyles.Bold,
            new Vector2(14f, 0f), new Vector2(-390f, 0f), TextAlignmentOptions.Left);
        feedback = CreateText(statusBar.transform, "Feedback", "Select an available project", 11f, FontStyles.Normal,
            new Vector2(390f, 0f), new Vector2(-14f, 0f), TextAlignmentOptions.Right);
        feedback.color = GameUITheme.TextSecondary;
        feedback.textWrappingMode = TextWrappingModes.NoWrap;

        var viewport = new GameObject("ResearchViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(24f, 24f); viewportRt.offsetMax = new Vector2(-24f, -176f);
        viewport.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.09f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var tabs = new GameObject("CategoryTabs", typeof(RectTransform));
        tabs.transform.SetParent(panel.transform, false);
        categoryTabs = tabs.transform;
        RectTransform tabsRt = (RectTransform)tabs.transform;
        tabsRt.anchorMin = new Vector2(0f, 1f); tabsRt.anchorMax = new Vector2(1f, 1f);
        tabsRt.pivot = new Vector2(0.5f, 1f);
        tabsRt.offsetMin = new Vector2(24f, -170f); tabsRt.offsetMax = new Vector2(-24f, -132f);
        CreateCategoryTab(0, "PRODUCTION", GetBranchColor(0));
        CreateCategoryTab(1, "WORKFORCE", GetBranchColor(1));
        CreateCategoryTab(2, "AUTOMATION", GetBranchColor(2));

        var chartObject = new GameObject("ResearchChart", typeof(RectTransform));
        chartObject.transform.SetParent(viewport.transform, false);
        chart = (RectTransform)chartObject.transform;
        chart.anchorMin = chart.anchorMax = chart.pivot = new Vector2(0.5f, 0.5f);
        chart.sizeDelta = new Vector2(2400f, 1100f);
        RecipeGraphDrag drag = viewport.AddComponent<RecipeGraphDrag>();
        drag.content = chart; drag.minimumZoom = 0.35f; drag.maximumZoom = 1.28f;
        drag.allowPan = true;
        BuildTree();
    }

    void BuildTree()
    {
        if (chart == null) return;
        for (int i = chart.childCount - 1; i >= 0; i--)
        {
            GameObject old = chart.GetChild(i).gameObject; old.SetActive(false); Destroy(old);
        }
        nodeViews.Clear(); nodeRects.Clear();
        chart.anchoredPosition = Vector2.zero;
        // Fit the full category tree on every rebuild, including tab changes.
        chart.localScale = Vector3.one * 0.4f;

        CreateBranchHeader(selectedCategory, selectedCategory == 0 ? "PRODUCTION"
            : selectedCategory == 1 ? "WORKFORCE" : "AUTOMATION", GetBranchColor(selectedCategory));

        Dictionary<string, Vector2> positions = BuildNodePositions();
        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition != null && definition.category == selectedCategory
                && positions.TryGetValue(definition.id, out Vector2 position))
                CreateNode(definition, position);
        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition != null && definition.category == selectedCategory
                && !string.IsNullOrEmpty(definition.parentId)
                && nodeRects.TryGetValue(definition.parentId, out RectTransform parent))
                CreateConnector(parent, nodeRects[definition.id], definition.parentId,
                    definition.id, definition.category);
        foreach (NodeView view in nodeViews) view.button.transform.SetAsLastSibling();
        // Keep branch labels above the card layer when a connector or card reaches
        // the header band. They remain visible while the map is dragged or zoomed.
        for (int i = chart.childCount - 1; i >= 0; i--)
        {
            Transform child = chart.GetChild(i);
            if (child.name.EndsWith("Branch")) child.SetAsLastSibling();
        }
        RefreshStatus(); RefreshNodes();
    }

    void CreateBranchHeader(int category, string label, Color color)
    {
        var group = new GameObject(label + "Branch", typeof(RectTransform));
        group.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)group.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(340f, 60f);
        rt.anchoredPosition = new Vector2(0f, chart.sizeDelta.y * 0.5f - 54f);
        TextMeshProUGUI text = CreateText(group.transform, "Label", label, 15f, FontStyles.Bold,
            new Vector2(0f, 0f), new Vector2(0f, 34f), TextAlignmentOptions.Center);
        text.color = color;
    }

    void CreateCategoryTab(int category, string label, Color color)
    {
        Button tab = CreateButton(categoryTabs, label + "Tab", label,
            Vector2.zero, Vector2.zero, new Color(0.08f, 0.1f, 0.14f, 1f));
        RectTransform rt = (RectTransform)tab.transform;
        rt.anchorMin = new Vector2(category / 3f, 0f);
        rt.anchorMax = new Vector2((category + 1) / 3f, 1f);
        rt.offsetMin = new Vector2(category == 0 ? 0f : 4f, 0f);
        rt.offsetMax = new Vector2(category == 2 ? 0f : -4f, 0f);
        Image image = tab.GetComponent<Image>();
        image.color = Color.Lerp(new Color(0.08f, 0.1f, 0.14f, 1f), color,
            category == selectedCategory ? 0.38f : 0.12f);
        TextMeshProUGUI text = tab.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.color = color;
        int capturedCategory = category;
        tab.onClick.AddListener(() => SelectCategory(capturedCategory));
        categoryTabButtons.Add(tab);
    }

    void SelectCategory(int category)
    {
        if (selectedCategory == category) return;
        selectedCategory = Mathf.Clamp(category, 0, 2);
        for (int i = 0; i < categoryTabButtons.Count; i++)
        {
            Color branch = GetBranchColor(i);
            Image image = categoryTabButtons[i].GetComponent<Image>();
            if (image != null)
                image.color = i == selectedCategory
                    ? Color.Lerp(new Color(0.08f, 0.1f, 0.14f, 1f), branch, 0.38f)
                    : Color.Lerp(new Color(0.08f, 0.1f, 0.14f, 1f), branch, 0.12f);
        }
        BuildTree();
        Sfx.Play(SfxId.UiClick);
    }

    Dictionary<string, Vector2> BuildNodePositions()
    {
        var positions = new Dictionary<string, Vector2>();
        var definitions = ResearchProgressManager.Definitions;
        const float center = 0f;
        float top = chart.sizeDelta.y * 0.5f - 175f;
        const float rowSpacing = 170f;
        const float leafSpacing = 330f;

        for (int category = selectedCategory; category == selectedCategory; category++)
        {
            var branch = new List<ResearchProgressManager.Definition>();
            foreach (ResearchProgressManager.Definition definition in definitions)
                if (definition != null && definition.category == category) branch.Add(definition);
            var roots = new List<ResearchProgressManager.Definition>();
            foreach (ResearchProgressManager.Definition definition in branch)
                if (string.IsNullOrEmpty(definition.parentId)
                    || branch.Find(candidate => candidate.id == definition.parentId) == null)
                    roots.Add(definition);

            int totalLeaves = 0;
            foreach (ResearchProgressManager.Definition root in roots)
                totalLeaves += CountLeaves(root, branch);
            float cursor = center - (Mathf.Max(1, totalLeaves) - 1) * leafSpacing * 0.5f;
            foreach (ResearchProgressManager.Definition root in roots)
            {
                int leaves = CountLeaves(root, branch);
                float rootX = cursor + (leaves - 1) * leafSpacing * 0.5f;
                PlaceTreeNode(root, rootX, 0, top, rowSpacing, leafSpacing, branch, positions);
                cursor += leaves * leafSpacing;
            }
        }
        return positions;
    }

    static int CountLeaves(ResearchProgressManager.Definition definition,
        List<ResearchProgressManager.Definition> branch)
    {
        int children = 0;
        int leaves = 0;
        foreach (ResearchProgressManager.Definition candidate in branch)
            if (candidate.parentId == definition.id)
            {
                children++;
                leaves += CountLeaves(candidate, branch);
            }
        return children == 0 ? 1 : leaves;
    }

    static void PlaceTreeNode(ResearchProgressManager.Definition definition, float x, int depth,
        float top, float rowSpacing, float leafSpacing,
        List<ResearchProgressManager.Definition> branch, Dictionary<string, Vector2> positions)
    {
        positions[definition.id] = new Vector2(x, top - depth * rowSpacing);
        var children = new List<ResearchProgressManager.Definition>();
        foreach (ResearchProgressManager.Definition candidate in branch)
            if (candidate.parentId == definition.id) children.Add(candidate);
        if (children.Count == 0) return;

        int totalLeaves = 0;
        foreach (ResearchProgressManager.Definition child in children)
            totalLeaves += CountLeaves(child, branch);
        float cursor = x - (totalLeaves - 1) * leafSpacing * 0.5f;
        foreach (ResearchProgressManager.Definition child in children)
        {
            int leaves = CountLeaves(child, branch);
            float childX = cursor + (leaves - 1) * leafSpacing * 0.5f;
            PlaceTreeNode(child, childX, depth + 1, top, rowSpacing, leafSpacing, branch, positions);
            cursor += leaves * leafSpacing;
        }
    }
    void CreateNode(ResearchProgressManager.Definition definition, Vector2 position)
    {
        var node = new GameObject(definition.id, typeof(RectTransform), typeof(Image), typeof(Button));
        node.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)node.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300f, 130f); rt.anchoredPosition = position;
        Image background = node.GetComponent<Image>();
        Button button = node.GetComponent<Button>(); button.targetGraphic = background;
        Color branchColor = GetBranchColor(definition.category);
        background.color = new Color(0.055f, 0.068f, 0.08f, 1f);
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock buttonColors = button.colors;
        buttonColors.normalColor = background.color;
        buttonColors.highlightedColor = Color.Lerp(background.color, branchColor, 0.32f);
        buttonColors.pressedColor = Color.Lerp(background.color, branchColor, 0.52f);
        buttonColors.selectedColor = buttonColors.highlightedColor;
        buttonColors.disabledColor = new Color(0.075f, 0.085f, 0.095f, 1f);
        button.colors = buttonColors;

        var stripe = new GameObject("BranchAccent", typeof(RectTransform), typeof(Image));
        stripe.transform.SetParent(node.transform, false);
        RectTransform stripeRect = (RectTransform)stripe.transform;
        stripeRect.anchorMin = new Vector2(0f, 0f);
        stripeRect.anchorMax = new Vector2(0f, 1f);
        stripeRect.pivot = new Vector2(0f, 0.5f);
        stripeRect.offsetMin = Vector2.zero;
        stripeRect.offsetMax = new Vector2(6f, 0f);
        Image stripeImage = stripe.GetComponent<Image>();
        stripeImage.color = branchColor;
        stripeImage.raycastTarget = false;
        TextMeshProUGUI title = CreateText(node.transform, "Title", definition.title, 14f, FontStyles.Bold,
            new Vector2(16f, 35f), new Vector2(-12f, 57f), TextAlignmentOptions.Left);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f); titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f); titleRect.anchoredPosition = new Vector2(0f, -10f);
        titleRect.sizeDelta = new Vector2(-28f, 22f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        TextMeshProUGUI description = CreateText(node.transform, "Description", definition.description, 11f, FontStyles.Normal,
            new Vector2(14f, -20f), new Vector2(-12f, 34f), TextAlignmentOptions.TopLeft);
        RectTransform descriptionRect = description.rectTransform;
        descriptionRect.anchorMin = new Vector2(0f, 1f); descriptionRect.anchorMax = new Vector2(1f, 1f);
        descriptionRect.pivot = new Vector2(0.5f, 1f); descriptionRect.anchoredPosition = new Vector2(0f, -35f);
        descriptionRect.sizeDelta = new Vector2(-28f, 52f);
        description.color = new Color(0.76f, 0.79f, 0.81f, 1f);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Overflow;
        TextMeshProUGUI meta = CreateText(node.transform, "Meta", "", 11f, FontStyles.Bold,
            new Vector2(14f, -58f), new Vector2(-12f, -40f), TextAlignmentOptions.Left);
        RectTransform metaRect = meta.rectTransform;
        metaRect.anchorMin = new Vector2(0f, 0f); metaRect.anchorMax = new Vector2(1f, 0f);
        metaRect.pivot = new Vector2(0.5f, 0f); metaRect.anchoredPosition = new Vector2(0f, 9f);
        metaRect.sizeDelta = new Vector2(-28f, 18f);
        meta.textWrappingMode = TextWrappingModes.NoWrap;
        meta.overflowMode = TextOverflowModes.Ellipsis;

        var track = new GameObject("ProgressTrack", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(node.transform, false);
        RectTransform trackRt = (RectTransform)track.transform;
        trackRt.anchorMin = Vector2.zero; trackRt.anchorMax = new Vector2(1f, 0f);
        trackRt.offsetMin = new Vector2(12f, 2f); trackRt.offsetMax = new Vector2(-12f, 6f);
        track.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 1f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        RectTransform fillRt = (RectTransform)fill.transform;
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        Image progress = fill.GetComponent<Image>(); progress.color = GetBranchColor(definition.category);

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

    static Color GetBranchColor(int category) => category switch
    {
        0 => new Color(0.25f, 0.68f, 0.54f, 1f),
        1 => new Color(0.28f, 0.58f, 0.92f, 1f),
        _ => new Color(0.98f, 0.76f, 0.16f, 1f),
    };

    void RefreshStatus()
    {
        if (activeStatus == null) return;
        ResearchProgressManager manager = Manager;
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (manager.HasActiveResearch)
        {
            ResearchProgressManager.Definition active = ResearchProgressManager.Get(manager.ActiveId);
            activeStatus.text = "RESEARCHING  " + (active != null ? active.title : "Project")
                + "  |  " + FormatTime(manager.RemainingMinutes) + " REMAINING";
            activeStatus.color = GameUITheme.PositiveAccent;
        }
        else
        {
            activeStatus.text = "NO ACTIVE RESEARCH  |  AVAILABLE CASH $" + (money != null ? money.CurrentMoney : 0);
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
            Color branch = GetBranchColor(d.category);
            view.background.color = complete ? Color.Lerp(new Color(0.06f, 0.075f, 0.085f, 1f), branch, 0.4f)
                : active ? Color.Lerp(new Color(0.06f, 0.075f, 0.085f, 1f), branch, 0.3f)
            : available ? Color.Lerp(new Color(0.12f, 0.15f, 0.20f, 1f), GetBranchColor(d.category), 0.23f)
                : new Color(0.095f, 0.11f, 0.15f, 1f);
            view.title.color = complete || active || available ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
            if (complete) view.meta.text = "<color=#73D9A0>COMPLETED</color>";
            else if (active) view.meta.text = "<color=#73D9C5>" + FormatTime(manager.RemainingMinutes) + " REMAINING</color>";
            else if (!milestone) view.meta.text = "REQUIRES MILESTONE " + d.milestone;
            else if (!prerequisite) view.meta.text = "REQUIRES " + (ResearchProgressManager.Get(d.parentId)?.title ?? "PREREQUISITE").ToUpperInvariant();
            else if (manager.HasActiveResearch) view.meta.text = "RESEARCH SLOT IN USE";
            else view.meta.text = (affordable ? "<color=#F2C66D>" : "<color=#E07A7A>")
                + "$" + d.cost + "</color>  |  " + FormatTime(d.durationMinutes);

            float progress = complete ? 1f : active ? 1f - manager.RemainingMinutes / Mathf.Max(1f, d.durationMinutes) : 0f;
            RectTransform fill = (RectTransform)view.progress.transform;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }
    }

    void CreateConnector(RectTransform from, RectTransform to, string parentId, string childId, int category)
    {
        Vector2 a = from.anchoredPosition;
        Vector2 b = to.anchoredPosition;
        Color branch = GetBranchColor(category);
        bool parentComplete = Manager.IsCompleted(parentId);
        bool childComplete = Manager.IsCompleted(childId);
        float alpha = parentComplete && childComplete ? 0.9f : 0.34f;
        Color color = new Color(branch.r, branch.g, branch.b, alpha);

        // Route each dependency as a clean branch: vertical, horizontal, vertical.
        // This keeps every split at a right angle and prevents arbitrary shallow diagonals.
        // Leave a small visual gap at each card so the branch never appears to
        // run underneath or through the card edge.
        const float cardHalfHeight = 65f;
        const float edgeGap = 6f;
        float parentEdge = a.y > b.y ? a.y - cardHalfHeight - edgeGap : a.y + cardHalfHeight + edgeGap;
        float childEdge = a.y > b.y ? b.y + cardHalfHeight + edgeGap : b.y - cardHalfHeight - edgeGap;
        float junctionY = (parentEdge + childEdge) * 0.5f;
        CreateConnectorSegment(new Vector2(a.x, parentEdge), new Vector2(a.x, junctionY), color);
        CreateConnectorSegment(new Vector2(a.x, junctionY), new Vector2(b.x, junctionY), color);
        CreateConnectorSegment(new Vector2(b.x, junctionY), new Vector2(b.x, childEdge), color);
    }

    void CreateConnectorSegment(Vector2 start, Vector2 end, Color color)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < 0.01f) return;
        var line = new GameObject("Connector", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)line.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (start + end) * 0.5f;
        rt.sizeDelta = new Vector2(Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? Mathf.Abs(delta.x) : 3f,
            Mathf.Abs(delta.y) > Mathf.Abs(delta.x) ? Mathf.Abs(delta.y) : 3f);
        line.GetComponent<Image>().color = color;
        line.transform.SetAsFirstSibling();
    }

    static string FormatTime(float minutes)
    {
        int rounded = Mathf.CeilToInt(Mathf.Max(0f, minutes));
        return rounded >= 60 ? (rounded / 60) + "h " + (rounded % 60) + "m" : rounded + "m";
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
