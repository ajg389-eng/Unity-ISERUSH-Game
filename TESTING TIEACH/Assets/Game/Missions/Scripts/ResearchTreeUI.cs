using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResearchTreeUI : MonoBehaviour
{
    const float NodeWidth = 400f;
    const float NodeHeight = 160f;
    const float BranchOffset = 460f;
    const int MinimumMilestoneSections = 6;
    const float MilestoneRowSpacing = 270f;

    sealed class NodeView
    {
        public ResearchProgressManager.Definition definition;
        public Button button;
        public Image background;
        public Image progress;
        public TextMeshProUGUI title, description, meta;
        public Image statusIcon;
    }

    sealed class BranchView
    {
        public int category;
        public TextMeshProUGUI progress;
    }

    static ResearchTreeUI instance;
    RectTransform chart;
    TextMeshProUGUI activeStatus;
    TextMeshProUGUI feedback;
    int selectedCategory;
    readonly List<NodeView> nodeViews = new List<NodeView>();
    readonly List<BranchView> branchViews = new List<BranchView>();
    readonly Dictionary<string, RectTransform> nodeRects = new Dictionary<string, RectTransform>();
    float nextRefresh;
    int builtDefinitionHash = int.MinValue;
    bool treeBuilt;

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
        if (OnboardingTutorial.IsResearchTutorialActive)
            instance.selectedCategory = OnboardingTutorial.TutorialResearchCategory;
        instance.EnsureTreeBuilt();
        instance.RefreshStatus();
        instance.RefreshNodes();
        Canvas.ForceUpdateCanvases();
        instance.gameObject.SetActive(true);
        OnboardingTutorial.NotifyResearchScreenOpened();
        Sfx.Play(SfxId.UiOpen);
    }

    public static void CloseIfOpen()
    {
        if (instance != null && instance.gameObject.activeSelf)
            instance.gameObject.SetActive(false);
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

    void OnResearchChanged()
    {
        if (OnboardingTutorial.IsResearchTutorialActive)
        {
            int requiredCategory = OnboardingTutorial.TutorialResearchCategory;
            if (selectedCategory != requiredCategory)
            {
                selectedCategory = requiredCategory;
                BuildTree();
                return;
            }
        }
        RefreshStatus(); RefreshNodes();
    }

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
        if (OnboardingTutorial.IsResearchTutorialActive)
            return;
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
        HudTabColors.SizeOverlayPanel(panelRt, transform as RectTransform);
        panel.GetComponent<Image>().color = new Color(0.105f, 0.12f, 0.16f, 0.995f);

        TextMeshProUGUI title = CreateText(panel.transform, "Title", "RESEARCH & DEVELOPMENT", 24f,
            FontStyles.Bold, new Vector2(28f, -54f), new Vector2(-90f, -10f), TextAlignmentOptions.Left);
        title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        TextMeshProUGUI hint = CreateText(panel.transform, "Hint", "DRAG TO PAN  |  SCROLL TO ZOOM  |  SELECT A PROJECT", 11f,
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
        viewportRt.offsetMin = new Vector2(24f, 24f); viewportRt.offsetMax = new Vector2(-24f, -142f);
        viewport.GetComponent<Image>().color = new Color(0.055f, 0.065f, 0.09f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var chartObject = new GameObject("ResearchChart", typeof(RectTransform));
        chartObject.transform.SetParent(viewport.transform, false);
        chart = (RectTransform)chartObject.transform;
        chart.anchorMin = chart.anchorMax = chart.pivot = new Vector2(0.5f, 0.5f);
        chart.sizeDelta = new Vector2(5200f, GetChartHeight());
        RecipeGraphDrag drag = viewport.AddComponent<RecipeGraphDrag>();
        drag.content = chart; drag.minimumZoom = 0.35f; drag.maximumZoom = 1.28f;
        drag.allowPan = true;
        BuildTree();
    }

    void BuildTree()
    {
        if (chart == null) return;
        treeBuilt = false;
        for (int i = chart.childCount - 1; i >= 0; i--)
        {
            GameObject old = chart.GetChild(i).gameObject; old.SetActive(false); Destroy(old);
        }
        nodeViews.Clear(); nodeRects.Clear();
        branchViews.Clear();
        chart.anchoredPosition = Vector2.zero;
        // Keep all three branches visible in one map. Players can still pan and
        // zoom the map to inspect a branch without switching screens.
        chart.localScale = Vector3.one * 0.5f;

        CreateMilestoneSections();
        float[] branchCenters = { -1600f, 0f, 1600f };
        for (int category = 0; category < 3; category++)
        {
            string label = category == 0 ? "PRODUCTION" : category == 1 ? "WORKFORCE" : "AUTOMATION";
            CreateBranchLane(category, label, GetBranchColor(category), branchCenters[category]);
            Dictionary<string, Vector2> positions = BuildNodePositions(category, branchCenters[category]);
            foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
                if (definition != null && definition.category == category
                    && positions.TryGetValue(definition.id, out Vector2 position))
                    CreateNode(definition, position);
        }
        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition != null
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
        builtDefinitionHash = GetDefinitionHash();
        treeBuilt = true;
    }

    void EnsureTreeBuilt()
    {
        if (chart == null) return;
        int definitionHash = GetDefinitionHash();
        if (!treeBuilt || builtDefinitionHash != definitionHash)
            BuildTree();
    }

    static int GetDefinitionHash()
    {
        unchecked
        {
            int hash = 17;
            foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            {
                if (definition == null)
                {
                    hash = hash * 31;
                    continue;
                }

                hash = hash * 31 + (definition.id ?? string.Empty).GetHashCode();
                hash = hash * 31 + definition.category;
                hash = hash * 31 + definition.milestone;
                hash = hash * 31 + (definition.parentId ?? string.Empty).GetHashCode();
            }
            return hash;
        }
    }

    void CreateMilestoneSections()
    {
        float firstRow = chart.sizeDelta.y * 0.5f - 240f;
        for (int milestone = 1; milestone <= GetMilestoneSectionCount(); milestone++)
        {
            var section = new GameObject("Milestone" + milestone + "Section", typeof(RectTransform), typeof(Image));
            section.transform.SetParent(chart, false);
            RectTransform sectionRt = (RectTransform)section.transform;
            sectionRt.anchorMin = sectionRt.anchorMax = sectionRt.pivot = new Vector2(0.5f, 0.5f);
            sectionRt.sizeDelta = new Vector2(chart.sizeDelta.x - 80f, 224f);
            sectionRt.anchoredPosition = new Vector2(0f, firstRow - (milestone - 1) * MilestoneRowSpacing);
            Image sectionImage = section.GetComponent<Image>();
            sectionImage.color = milestone % 2 == 0
                ? new Color(0.08f, 0.10f, 0.14f, 0.38f)
                : new Color(0.04f, 0.055f, 0.08f, 0.48f);
            sectionImage.raycastTarget = false;
            section.transform.SetAsFirstSibling();

            TextMeshProUGUI label = CreateText(section.transform, "Label", "MILESTONE " + milestone,
                18f, FontStyles.Bold, Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(18f, 0f);
            labelRect.sizeDelta = new Vector2(300f, 30f);
            label.color = GameUITheme.TextSecondary;
        }
    }

    int GetMilestoneSectionCount()
    {
        int count = MinimumMilestoneSections;
        if (MilestoneProgressManager.Instance != null)
            count = Mathf.Max(count, MilestoneProgressManager.Instance.GetNumberedMilestoneCount());
        foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            if (definition != null) count = Mathf.Max(count, definition.milestone);
        return count;
    }

    float GetChartHeight()
    {
        return Mathf.Max(1350f, 500f + (GetMilestoneSectionCount() - 1) * MilestoneRowSpacing);
    }

    void CreateBranchLane(int category, string label, Color color, float x)
    {
        var lane = new GameObject(label + "Lane", typeof(RectTransform), typeof(Image));
        lane.transform.SetParent(chart, false);
        RectTransform laneRt = (RectTransform)lane.transform;
        laneRt.anchorMin = laneRt.anchorMax = laneRt.pivot = new Vector2(0.5f, 0.5f);
        laneRt.sizeDelta = new Vector2(1500f, chart.sizeDelta.y - 100f);
        laneRt.anchoredPosition = new Vector2(x, -30f);
        Image laneImage = lane.GetComponent<Image>();
        laneImage.color = new Color(color.r, color.g, color.b, 0.055f);
        laneImage.raycastTarget = false;
        lane.transform.SetAsFirstSibling();

        var group = new GameObject(label + "Branch", typeof(RectTransform));
        group.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)group.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1460f, 110f);
        rt.anchoredPosition = new Vector2(x, chart.sizeDelta.y * 0.5f - 50f);
        Image headerImage = group.AddComponent<Image>();
        headerImage.color = new Color(0.07f, 0.085f, 0.11f, 0.98f);
        headerImage.raycastTarget = false;
        TextMeshProUGUI text = CreateText(group.transform, "Label", label, 60f, FontStyles.Bold,
            Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(1f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(0f, 20f);
        textRect.sizeDelta = new Vector2(-28f, 60f);
        text.color = color;
        TextMeshProUGUI progress = CreateText(group.transform, "Progress", "0 / 0 COMPLETE", 24f, FontStyles.Bold,
            Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        RectTransform progressRect = progress.rectTransform;
        progressRect.anchorMin = new Vector2(0f, 0.5f);
        progressRect.anchorMax = new Vector2(1f, 0.5f);
        progressRect.pivot = new Vector2(0.5f, 0.5f);
        progressRect.anchoredPosition = new Vector2(0f, -27f);
        progressRect.sizeDelta = new Vector2(-28f, 24f);
        progress.color = color;
        branchViews.Add(new BranchView { category = category, progress = progress });
    }

    Dictionary<string, Vector2> BuildNodePositions(int category, float xOffset)
    {
        var positions = new Dictionary<string, Vector2>();
        var definitions = ResearchProgressManager.Definitions;
        float top = chart.sizeDelta.y * 0.5f - 240f;
        const float rowSpacing = MilestoneRowSpacing;

        var branch = new List<ResearchProgressManager.Definition>();
        foreach (ResearchProgressManager.Definition definition in definitions)
            if (definition != null && definition.category == category) branch.Add(definition);
        // Keep the dependency branches visible while placing every milestone
        // on a consistent horizontal row. The source coordinates identify the
        // left, centre, and right branch within each category.
        for (int milestone = 1; milestone <= GetMilestoneSectionCount(); milestone++)
        {
            var row = branch.FindAll(definition => definition.milestone == milestone);
            row.Sort((a, b) =>
            {
                int compare = a.position.x.CompareTo(b.position.x);
                return compare != 0 ? compare : string.CompareOrdinal(a.id, b.id);
            });
            int count = row.Count;
            for (int index = 0; index < count; index++)
            {
                float branchOffset = count switch
                {
                    1 => 0f,
                    2 => index == 0 ? -BranchOffset : BranchOffset,
                    _ => -BranchOffset + index * ((BranchOffset * 2f) / Mathf.Max(1, count - 1))
                };
                positions[row[index].id] = new Vector2(xOffset + branchOffset,
                    top - (milestone - 1) * rowSpacing);
            }
        }
        return positions;
    }
    void CreateNode(ResearchProgressManager.Definition definition, Vector2 position)
    {
        var node = new GameObject(definition.id, typeof(RectTransform), typeof(Image), typeof(Button));
        node.transform.SetParent(chart, false);
        RectTransform rt = (RectTransform)node.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(NodeWidth, NodeHeight); rt.anchoredPosition = position;
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

        var statusIconObject = new GameObject("StatusIcon", typeof(RectTransform), typeof(Image));
        statusIconObject.transform.SetParent(node.transform, false);
        RectTransform statusIconRect = (RectTransform)statusIconObject.transform;
        statusIconRect.anchorMin = new Vector2(1f, 1f);
        statusIconRect.anchorMax = new Vector2(1f, 1f);
        statusIconRect.pivot = new Vector2(1f, 1f);
        statusIconRect.anchoredPosition = new Vector2(-10f, -9f);
        statusIconRect.sizeDelta = new Vector2(22f, 22f);
        Image statusIcon = statusIconObject.GetComponent<Image>();
        statusIcon.preserveAspect = true;
        statusIcon.raycastTarget = false;
        TextMeshProUGUI title = CreateText(node.transform, "Title", definition.title, 22f, FontStyles.Bold,
            new Vector2(16f, 35f), new Vector2(-12f, 57f), TextAlignmentOptions.Left);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f); titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f); titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(-32f, 28f);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        TextMeshProUGUI description = CreateText(node.transform, "Description", definition.description, 18f, FontStyles.Normal,
            new Vector2(14f, -20f), new Vector2(-12f, 34f), TextAlignmentOptions.TopLeft);
        RectTransform descriptionRect = description.rectTransform;
        descriptionRect.anchorMin = new Vector2(0f, 1f); descriptionRect.anchorMax = new Vector2(1f, 1f);
        descriptionRect.pivot = new Vector2(0.5f, 1f); descriptionRect.anchoredPosition = new Vector2(0f, -44f);
        descriptionRect.sizeDelta = new Vector2(-32f, 72f);
        description.color = new Color(0.76f, 0.79f, 0.81f, 1f);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Ellipsis;
        TextMeshProUGUI meta = CreateText(node.transform, "Meta", "", 15f, FontStyles.Bold,
            new Vector2(14f, -58f), new Vector2(-12f, -40f), TextAlignmentOptions.Left);
        RectTransform metaRect = meta.rectTransform;
        metaRect.anchorMin = new Vector2(0f, 0f); metaRect.anchorMax = new Vector2(1f, 0f);
        metaRect.pivot = new Vector2(0.5f, 0f); metaRect.anchoredPosition = new Vector2(0f, 10f);
        metaRect.sizeDelta = new Vector2(-32f, 22f);
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
            progress = progress, title = title, description = description, meta = meta, statusIcon = statusIcon };
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
                + "  |  " + (manager.IsTutorialTimedResearch
                    ? FormatSeconds(manager.RemainingSeconds)
                    : FormatTime(manager.RemainingMinutes)) + " REMAINING";
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
            bool tutorialLocked = OnboardingTutorial.IsResearchTutorialActive
                && !OnboardingTutorial.IsTutorialResearchRoot(d.id);
            bool available = !complete && !active && !manager.HasActiveResearch && milestone && prerequisite
                && !tutorialLocked;
            bool affordable = money != null && money.CanAfford(d.cost);
            view.button.interactable = available;
            Color branch = GetBranchColor(d.category);
            view.background.color = complete ? Color.Lerp(new Color(0.06f, 0.075f, 0.085f, 1f), branch, 0.4f)
                : active ? Color.Lerp(new Color(0.06f, 0.075f, 0.085f, 1f), branch, 0.3f)
            : available ? Color.Lerp(new Color(0.12f, 0.15f, 0.20f, 1f), GetBranchColor(d.category), 0.23f)
                : new Color(0.095f, 0.11f, 0.15f, 1f);
            view.title.color = complete || active || available ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
            if (view.statusIcon != null)
            {
                view.statusIcon.sprite = GameIconLibrary.GetSprite(
                    complete ? GameIconId.Checkmark : active ? GameIconId.Wrench : available ? GameIconId.Unlocked : GameIconId.Locked);
                view.statusIcon.color = complete ? GameUITheme.PositiveAccent : branch;
            }
            if (complete) view.meta.text = "<color=#73D9A0>COMPLETED</color>";
            else if (active) view.meta.text = "<color=#73D9C5>" + (manager.IsTutorialTimedResearch
                ? FormatSeconds(manager.RemainingSeconds)
                : FormatTime(manager.RemainingMinutes)) + " REMAINING</color>";
            else if (tutorialLocked) view.meta.text = "TUTORIAL: START WITH THE FOUNDATION CARD";
            else if (!milestone) view.meta.text = "REQUIRES MILESTONE " + d.milestone;
            else if (!prerequisite) view.meta.text = "REQUIRES " + (ResearchProgressManager.Get(d.parentId)?.title ?? "PREREQUISITE").ToUpperInvariant();
            else if (manager.HasActiveResearch) view.meta.text = "RESEARCH SLOT IN USE";
            else view.meta.text = (affordable ? "<color=#F2C66D>" : "<color=#E07A7A>")
                + "$" + d.cost + "</color>  |  " + FormatTime(d.durationMinutes);

            float progress = complete ? 1f : active
                ? manager.IsTutorialTimedResearch
                    ? 1f - manager.RemainingSeconds / 1f
                    : 1f - manager.RemainingMinutes / Mathf.Max(1f, d.durationMinutes)
                : 0f;
            RectTransform fill = (RectTransform)view.progress.transform;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        foreach (BranchView branchView in branchViews)
        {
            int total = 0;
            int completed = 0;
            foreach (ResearchProgressManager.Definition definition in ResearchProgressManager.Definitions)
            {
                if (definition == null || definition.category != branchView.category) continue;
                total++;
                if (manager.IsCompleted(definition.id)) completed++;
            }
            if (branchView.progress != null)
                branchView.progress.text = completed + " / " + total + " COMPLETE";
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
        float cardHalfHeight = NodeHeight * 0.5f;
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

    static string FormatSeconds(float seconds)
    {
        return Mathf.CeilToInt(Mathf.Max(0f, seconds)) + "s";
    }

    static Button CreateButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.offsetMin = min; rt.offsetMax = max;
        go.GetComponent<Image>().color = color;
        CreateText(go.transform, "Label", label, 18f, FontStyles.Bold, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        Button button = go.GetComponent<Button>();
        if (name == "Close")
            GameIconLibrary.ApplyToButton(button, GameIconId.Cross, 20f, 0f, true);
        return button;
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
