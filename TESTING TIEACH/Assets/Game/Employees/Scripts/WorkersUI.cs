using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management screen Workers tab: hire staff, design production flows by clicking stations, assign workers.
/// </summary>
public class WorkersUI : MonoBehaviour
{
    [Header("Header")]
    public TextMeshProUGUI countText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI hintText;
    public Button hireButton;

    [Header("List")]
    public GameObject workerCardPrefab;
    public Transform cardContainer;
    public ScrollRect workerCardsScroll;

    ProductionManager production;
    bool listenerAdded;
    bool layoutApplied;
    Transform configuredCardContainer;
    RectTransform flowPanel;
    Transform productionFlowsPanel;
    Transform flowListRow;
    Transform flowActionRow;
    Transform flowTabsContent;
    Button deleteFlowButton;
    ScrollRect flowTabsScroll;
    Transform stepRow;
    Transform workerRow;
    TMP_InputField flowNameInput;
    bool flowNameEditStarted;
    Button createFlowButton;
    TextMeshProUGUI flowStatus;
    TextMeshProUGUI workerAssignmentSummary;
    Transform systemEfficiencyRoot;
    TextMeshProUGUI systemEfficiencyHeading;
    TextMeshProUGUI systemEfficiencySummary;
    TextMeshProUGUI systemEfficiencyExplanation;
    Transform systemEfficiencyDetailsRow;
    Button systemEfficiencyDetailsButton;
    bool systemEfficiencyDetailsExpanded;
    readonly Button[] systemEfficiencyButtons = new Button[4];
    readonly TextMeshProUGUI[] systemEfficiencyButtonLabels = new TextMeshProUGUI[4];
    int expandedSystemEfficiencyState = -1;
    float nextSystemEfficiencyRefresh;
    Button flowExpandButton;
    bool displayedShiftInProgress;
    bool hasDisplayedEmployeeList;
    int displayedEmployeeListSignature;
    TextMeshProUGUI flowExpandArrowLabel;
    TextMeshProUGUI flowHeaderLabel;
    bool flowExpanded = true;
    bool flowSectionNeedsTheme;
    bool flowRefreshRequested;
    readonly HashSet<StationNode> efficiencyStations = new HashSet<StationNode>();
    readonly float[] efficiencyExact = new float[4];
    readonly float[] efficiencyRemainders = new float[4];
    readonly int[] efficiencyPercents = new int[4];
    readonly int[] displayedEfficiencyPercents = { -1, -1, -1, -1 };
    static readonly string[] EfficiencyNames = { "WORKING", "BLOCKED", "STARVED", "IDLE" };
    string displayedEfficiencyFlowName;
    int displayedEfficiencyStationCount = -1;
    readonly List<ProductionFlowPlan> displayedTabFlows = new List<ProductionFlowPlan>();
    readonly List<string> displayedTabNames = new List<string>();
    readonly List<Button> displayedTabButtons = new List<Button>();
    int displayedSelectedTab = -1;
    Button wiredDeleteFlowButton;

    void OnEnable()
    {
        KitchenEmployee.WorkerAssignmentsChanged += OnWorkerAssignmentsChanged;
        if (MilestoneProgressManager.Instance != null)
            MilestoneProgressManager.Instance.OnMilestonesChanged += Refresh;
        if (ResearchProgressManager.Instance != null)
            ResearchProgressManager.Instance.Changed += Refresh;
        Refresh();
        displayedShiftInProgress = GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress;
        nextSystemEfficiencyRefresh = Time.unscaledTime + 0.5f;
        PurchaseUndoFooter.RemoveFromPanel(transform);
    }

    void OnDisable()
    {
        KitchenEmployee.WorkerAssignmentsChanged -= OnWorkerAssignmentsChanged;
        if (MilestoneProgressManager.Instance != null)
            MilestoneProgressManager.Instance.OnMilestonesChanged -= Refresh;
        if (ResearchProgressManager.Instance != null)
            ResearchProgressManager.Instance.Changed -= Refresh;
        // Leaving the Workers/Flows UI is an explicit flow deselection. Ordinary
        // world clicks are handled separately and keep the selected route pinned.
        WorkerAssignmentLinkVisuals.ClearFocusedFlow();
    }

    void Update()
    {
        if (flowRefreshRequested)
        {
            flowRefreshRequested = false;
            RefreshFlowSection();
        }

        bool shiftInProgress = GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress;
        if (shiftInProgress != displayedShiftInProgress)
        {
            displayedShiftInProgress = shiftInProgress;
            Refresh();
        }
        if (systemEfficiencyRoot == null || !systemEfficiencyRoot.gameObject.activeInHierarchy
            || Time.unscaledTime < nextSystemEfficiencyRefresh)
            return;
        nextSystemEfficiencyRefresh = Time.unscaledTime + 0.5f;
        RefreshSystemEfficiency();
    }

    void OnWorkerAssignmentsChanged()
    {
        flowRefreshRequested = true;
    }

    void EnsureRefs()
    {
        if (production == null)
            production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();

        if (countText == null)
        {
            var t = transform.Find("CountText") ?? transform.Find("HeaderBar/CountText");
            if (t != null) countText = t.GetComponent<TextMeshProUGUI>();
        }
        if (costText == null)
        {
            var t = transform.Find("CostText") ?? transform.Find("HeaderBar/CostText");
            if (t != null) costText = t.GetComponent<TextMeshProUGUI>();
        }
        if (hireButton == null)
        {
            var t = transform.Find("HireButton") ?? transform.Find("HeaderBar/HireButton");
            if (t != null) hireButton = t.GetComponent<Button>();
        }
        if (hintText == null)
        {
            var t = transform.Find("HintText");
            if (t != null) hintText = t.GetComponent<TextMeshProUGUI>();
        }

        if (hireButton != null && !listenerAdded)
        {
            hireButton.onClick.AddListener(OnHireClicked);
            listenerAdded = true;
        }
    }

    void ApplyCleanLayout()
    {
        if (layoutApplied) return;
        layoutApplied = true;

        var panelImage = GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = Color.clear;
            panelImage.raycastTarget = true;
            if (panelImage.GetComponent<GameUIThemeStyled>() == null)
                panelImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var title = transform.Find("Title") as RectTransform;
        if (title != null)
        {
            title.gameObject.SetActive(false);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.anchoredPosition = new Vector2(0f, -8f);
            title.sizeDelta = new Vector2(-24f, 24f);
            var titleTmp = title.GetComponent<TextMeshProUGUI>();
            if (titleTmp != null)
            {
                titleTmp.alignment = TextAlignmentOptions.Center;
                titleTmp.fontSize = 18;
            }
        }

        Transform header = transform.Find("HeaderBar");
        if (header == null)
        {
            var headerGo = new GameObject("HeaderBar", typeof(RectTransform));
            headerGo.transform.SetParent(transform, false);
            header = headerGo.transform;
        }

        var headerRt = (RectTransform)header;
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = new Vector2(0f, -4f);
        headerRt.sizeDelta = new Vector2(-24f, 36f);

        var headerImg = header.GetComponent<Image>();
        if (headerImg == null) headerImg = header.gameObject.AddComponent<Image>();
        headerImg.color = Color.clear;
        headerImg.raycastTarget = false;
        if (headerImg.GetComponent<GameUIThemeStyled>() == null)
            headerImg.gameObject.AddComponent<GameUIThemeStyled>();

        var hlg = header.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 10, 4, 4);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        if (countText != null)
        {
            countText.transform.SetParent(header, false);
            countText.text = string.Empty;
            var le = countText.GetComponent<LayoutElement>();
            if (le == null) le = countText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 90;
            le.flexibleWidth = 1;
            countText.alignment = TextAlignmentOptions.Left;
            countText.fontSize = 14;
            countText.raycastTarget = false;
        }

        if (costText != null)
        {
            costText.transform.SetParent(header, false);
            var le = costText.GetComponent<LayoutElement>();
            if (le == null) le = costText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 80;
            le.preferredWidth = 90;
            costText.alignment = TextAlignmentOptions.Center;
            costText.fontSize = 14;
            costText.color = new Color(0.85f, 0.88f, 0.75f, 1f);
            costText.raycastTarget = false;
        }

        if (hireButton != null)
        {
            hireButton.transform.SetParent(header, false);
            var le = hireButton.GetComponent<LayoutElement>();
            if (le == null) le = hireButton.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 110;
            le.preferredWidth = 118;
            le.minHeight = 28;
            var img = hireButton.GetComponent<Image>();
            if (img != null) img.color = new Color(0.3f, 0.48f, 0.36f, 1f);
            var label = hireButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "Hire";
                label.fontSize = 14;
            }
        }

        if (hintText != null)
            hintText.gameObject.SetActive(false);

        header.SetSiblingIndex(1);
        EnsureFlowSection();
        // Hiring belongs to the worker list section; hide the legacy top row.
        header.gameObject.SetActive(false);
    }

    void EnsureCardContainer()
    {
        if (cardContainer == null)
        {
            var existing = transform.Find("WorkerCardsScroll/Viewport/CardContainer");
            if (existing != null) cardContainer = existing;
        }

        if (cardContainer != null)
        {
            if (configuredCardContainer != cardContainer)
            {
                ConfigureCardContainer();
                FitScrollArea();
                ConfigureWorkerScroll();
                configuredCardContainer = cardContainer;
            }
            return;
        }

        var scrollGo = new GameObject("WorkerCardsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(scroll);
        workerCardsScroll = scroll;

        var scrollBg = scrollGo.AddComponent<Image>();
        scrollBg.color = Color.clear;
        scrollBg.raycastTarget = true;
        scrollBg.gameObject.AddComponent<GameUIThemeStyled>();
        scrollGo.AddComponent<ScrollRectWheelBlocker>().scrollRect = scroll;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRect = (RectTransform)viewport.transform;
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        viewportImage.raycastTarget = true;
        viewport.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        viewportImage.gameObject.AddComponent<GameUIThemeStyled>();
        var viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        var content = new GameObject("CardContainer", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(2, 2, 2, 4);
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        scroll.viewport = vpRect;
        scroll.content = contentRect;
        cardContainer = content.transform;
        ConfigureCardContainer();
        FitScrollArea();
        ConfigureWorkerScroll();
        configuredCardContainer = cardContainer;
    }

    void ConfigureCardContainer()
    {
        if (cardContainer == null) return;

        var contentRect = cardContainer as RectTransform;
        if (contentRect != null)
        {
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
        }

        var fitter = cardContainer.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = cardContainer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var layout = cardContainer.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = cardContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(10, 10, 8, 10);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Image sectionBackground = cardContainer.GetComponent<Image>();
        if (sectionBackground == null) sectionBackground = cardContainer.gameObject.AddComponent<Image>();
        sectionBackground.color = new Color(0.14f, 0.15f, 0.2f, 0.95f);
        sectionBackground.raycastTarget = false;
    }

    void ConfigureWorkerScroll()
    {
        if (workerCardsScroll == null)
        {
            var existing = transform.Find("WorkerCardsScroll");
            if (existing != null)
                workerCardsScroll = existing.GetComponent<ScrollRect>();
        }
        if (workerCardsScroll == null) return;
        workerCardsScroll.horizontal = false;
        workerCardsScroll.vertical = true;
        workerCardsScroll.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(workerCardsScroll);
        workerCardsScroll.inertia = true;
        workerCardsScroll.decelerationRate = 0.135f;

        var scrollImage = workerCardsScroll.GetComponent<Image>();
        if (scrollImage != null)
        {
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            if (scrollImage.GetComponent<GameUIThemeStyled>() == null)
                scrollImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var existingViewportImage = workerCardsScroll.viewport != null
            ? workerCardsScroll.viewport.GetComponent<Image>() : null;
        if (workerCardsScroll.viewport != null)
        {
            var rectMask = workerCardsScroll.viewport.GetComponent<RectMask2D>();
            if (rectMask != null) Destroy(rectMask);
            var viewportMask = workerCardsScroll.viewport.GetComponent<Mask>();
            if (viewportMask == null) viewportMask = workerCardsScroll.viewport.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
        }
        if (existingViewportImage != null)
        {
            existingViewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            existingViewportImage.raycastTarget = true;
            var viewportRenderer = existingViewportImage.GetComponent<CanvasRenderer>();
            if (viewportRenderer != null) viewportRenderer.cullTransparentMesh = false;
            if (existingViewportImage.GetComponent<GameUIThemeStyled>() == null)
                existingViewportImage.gameObject.AddComponent<GameUIThemeStyled>();
        }
    }

    void FitScrollArea(bool rebuildFlowLayout = true)
    {
        var scroll = transform.Find("WorkerCardsScroll") as RectTransform;
        if (scroll == null && cardContainer != null)
            scroll = cardContainer.parent != null && cardContainer.parent.parent != null
                ? cardContainer.parent.parent as RectTransform
                : null;
        if (scroll == null) return;

        float topInset = 310f;
        if (flowPanel != null)
        {
            if (rebuildFlowLayout) LayoutRebuilder.ForceRebuildLayoutImmediate(flowPanel);
            float panelHeight = Mathf.Max(44f, flowPanel.rect.height);
            // The header ends near the top of the content area; keep the flow
            // panel and worker list on the same compact baseline.
            // Keep the panel below the Business submenu row. This space is
            // required in planning as well as during live management.
            topInset = 44f + panelHeight + 10f;
        }

        scroll.anchorMin = new Vector2(0f, 0f);
        scroll.anchorMax = Vector2.one;
        scroll.offsetMin = new Vector2(12f, 12f);
        scroll.offsetMax = new Vector2(-12f, -topInset);
        PurchaseUndoFooter.RemoveFromPanel(transform);
        scroll.SetAsLastSibling();
    }

    void OnHireClicked()
    {
        if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress) return;
        if (production == null)
        {
            production = FindObjectOfType<ProductionManager>();
            if (production == null) return;
        }
        production.HireWorker();
        Refresh();
    }

    void SetHireButtonLabel(string label)
    {
        if (hireButton == null) return;
        TextMeshProUGUI hireLabel = hireButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (hireLabel != null) hireLabel.text = label;
    }

    void EnsureFlowSection()
    {
        if (flowPanel != null) return;
        var existing = transform.Find("FlowPathPanel") as RectTransform;
        // Rebuild outdated panels so economics/layout fixes apply.
        if (existing != null && existing.Find("LayoutV13") == null)
        {
            Destroy(existing.gameObject);
            existing = null;
            flowPanel = null;
        }

        if (existing != null)
        {
            flowSectionNeedsTheme = true;
            flowPanel = existing;
            productionFlowsPanel = existing.Find("ProductionFlowsPanel");
            flowListRow = productionFlowsPanel != null
                ? productionFlowsPanel.Find("FlowListRow") : existing.Find("FlowListRow");
            BindFlowTabScroller(flowListRow);
            flowActionRow = productionFlowsPanel != null
                ? productionFlowsPanel.Find("FlowActionRow") : existing.Find("FlowActionRow");
            stepRow = existing.Find("StepRow");
            workerRow = productionFlowsPanel != null
                ? productionFlowsPanel.Find("WorkerRow") : existing.Find("WorkerRow");
            workerAssignmentSummary = workerRow != null
                ? workerRow.Find("WorkerAssignmentSummary")?.GetComponent<TextMeshProUGUI>()
                : null;
            flowNameInput = existing.Find("ProductionFlowsPanel/FlowActionRow/FlowName")?.GetComponent<TMP_InputField>()
                ?? existing.Find("FlowActionRow/FlowName")?.GetComponent<TMP_InputField>()
                ?? existing.Find("FlowHeader/FlowName")?.GetComponent<TMP_InputField>()
                ?? existing.Find("NameRow/FlowName")?.GetComponent<TMP_InputField>();
            if (flowNameInput != null)
                flowNameInput.characterLimit = ProductionFlowPlan.MaxNameLength;
            BindFlowHeader(productionFlowsPanel != null ? productionFlowsPanel : existing);
            createFlowButton = existing.Find("ProductionFlowsPanel/FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? existing.Find("FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? existing.Find("ActionsRow/CreateFlow")?.GetComponent<Button>()
                ?? existing.Find("FlowListRow/CreateFlow")?.GetComponent<Button>();
            flowStatus = existing.Find("EconomicsPanel/FlowStats")?.GetComponent<TextMeshProUGUI>()
                ?? existing.Find("FlowStats")?.GetComponent<TextMeshProUGUI>()
                ?? existing.Find("Status")?.GetComponent<TextMeshProUGUI>();
            BindSystemEfficiencyBar(existing.Find("EconomicsPanel/SystemEfficiency"));
            WireFlowActionButtons();
            LayoutFlowPanel();
            return;
        }

        var go = new GameObject("FlowPathPanel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        flowSectionNeedsTheme = true;
        flowPanel = go.GetComponent<RectTransform>();
        var bg = go.GetComponent<Image>();
        bg.color = Color.clear;
        bg.raycastTarget = false;

        // Version marker — presence means this panel has the cleaned layout.
        var version = new GameObject("LayoutV13", typeof(RectTransform), typeof(LayoutElement));
        version.transform.SetParent(flowPanel, false);
        var versionLe = version.GetComponent<LayoutElement>();
        versionLe.ignoreLayout = true;
        versionLe.minHeight = 0;
        versionLe.preferredHeight = 0;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        productionFlowsPanel = BuildSectionCard(flowPanel, "ProductionFlowsPanel");
        BuildFlowHeader(productionFlowsPanel);

        flowListRow = MakeRow(productionFlowsPanel, "FlowListRow", 38);
        BuildFlowTabScroller(flowListRow);
        flowActionRow = BuildFlowActionRow(productionFlowsPanel);
        // The route already appears in-world when the flow is selected. Repeating
        // every station connection here consumes most of the card without adding
        // another decision, so keep the panel focused on flow selection and economics.
        stepRow = null;
        workerRow = MakeRow(productionFlowsPanel, "WorkerRow", 44);
        BuildEconomicsPanel(flowPanel);

        LayoutFlowPanel();
        var header = transform.Find("HeaderBar");
        if (header != null)
            flowPanel.SetSiblingIndex(header.GetSiblingIndex() + 1);
    }

    Transform BuildSectionCard(Transform parent, string name)
    {
        var card = new GameObject(name, typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(ContentSizeFitter));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.2f, 0.95f);
        card.GetComponent<Image>().raycastTarget = false;
        LayoutElement size = card.GetComponent<LayoutElement>();
        size.flexibleWidth = 1f;
        ContentSizeFitter fitter = card.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        VerticalLayoutGroup layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 10);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return card.transform;
    }

    void BuildFlowHeader(Transform parent)
    {
        var header = MakeRow(parent, "FlowHeader", 36);
        flowHeaderLabel = MakeLabel(header, "Production Flows", 18,
            new Color(0.94f, 0.76f, 0.34f, 1f), 30);
        flowHeaderLabel.fontStyle = FontStyles.Bold;
        var headerLayout = flowHeaderLabel.GetComponent<LayoutElement>();
        if (headerLayout != null)
        {
            headerLayout.minWidth = 130f;
            headerLayout.flexibleWidth = 1f;
        }
        createFlowButton = MakeChip(header, "+ New Flow", 112f);
        createFlowButton.gameObject.name = "CreateFlow";
        WireFlowActionButtons();
        UpdateFlowHeader();
    }

    Transform BuildFlowActionRow(Transform parent)
    {
        Transform row = MakeRow(parent, "FlowActionRow", 36);
        TextMeshProUGUI nameLabel = MakeLabel(row, "Name", 14f,
            new Color(0.78f, 0.83f, 0.91f, 1f), 30f);
        LayoutElement nameLabelLayout = nameLabel.GetComponent<LayoutElement>();
        nameLabelLayout.minWidth = 48f;
        nameLabelLayout.preferredWidth = 48f;
        nameLabelLayout.flexibleWidth = 0f;

        flowNameInput = MakeNameInput(row);
        Button editFlowButton = MakeChip(row, "Edit Flow", 100f);
        editFlowButton.gameObject.name = "EditFlow";
        deleteFlowButton = MakeChip(row, "Delete", 78f);
        deleteFlowButton.gameObject.name = "DeleteFlow";
        deleteFlowButton.GetComponent<Image>().color = new Color(0.65f, 0.19f, 0.19f, 1f);
        WireFlowActionButtons();
        return row;
    }

    void BuildFlowTabScroller(Transform row)
    {
        if (row == null) return;
        var scrollGo = new GameObject("FlowTabsScroll", typeof(RectTransform),
            typeof(Image), typeof(Mask), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(row, false);
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        scrollGo.GetComponent<Mask>().showMaskGraphic = false;
        LayoutElement scrollLayout = scrollGo.GetComponent<LayoutElement>();
        scrollLayout.minWidth = 72f;
        scrollLayout.flexibleWidth = 1f;
        scrollLayout.minHeight = 32f;
        scrollLayout.preferredHeight = 32f;

        RectTransform viewport = (RectTransform)scrollGo.transform;
        var content = new GameObject("FlowTabsContent", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport, false);
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 0f);
        contentRect.anchorMax = new Vector2(0f, 1f);
        contentRect.pivot = new Vector2(0f, 0.5f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        HorizontalLayoutGroup contentLayout = content.GetComponent<HorizontalLayoutGroup>();
        contentLayout.spacing = 4f;
        contentLayout.childAlignment = TextAnchor.MiddleLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = true;
        ContentSizeFitter contentFitter = content.GetComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        flowTabsScroll = scrollGo.GetComponent<ScrollRect>();
        flowTabsScroll.content = contentRect;
        flowTabsScroll.viewport = viewport;
        flowTabsScroll.horizontal = true;
        flowTabsScroll.vertical = false;
        flowTabsScroll.movementType = ScrollRect.MovementType.Clamped;
        flowTabsScroll.inertia = true;
        flowTabsScroll.decelerationRate = 0.135f;

        flowTabsContent = content.transform;
    }

    void BindFlowTabScroller(Transform row)
    {
        if (row == null) return;
        Transform scrollRoot = row.Find("FlowTabsScroll");
        if (scrollRoot == null) return;
        flowTabsScroll = scrollRoot.GetComponent<ScrollRect>();
        flowTabsContent = scrollRoot.Find("FlowTabsContent");
        Transform actionRow = row.parent != null ? row.parent.Find("FlowActionRow") : null;
        deleteFlowButton = actionRow != null ? actionRow.Find("DeleteFlow")?.GetComponent<Button>() : null;
    }

    void BindFlowHeader(Transform root)
    {
        Transform header = root != null ? root.Find("FlowHeader") : null;
        if (header == null) return;
        flowExpandButton = header.Find("ExpandFlow")?.GetComponent<Button>();
        flowExpandArrowLabel = flowExpandButton != null
            ? flowExpandButton.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        flowHeaderLabel = header.Find("Label")?.GetComponent<TextMeshProUGUI>();
        WireFlowExpandButton();
        UpdateFlowHeader();
        ApplyFlowExpandedState();
    }

    void WireFlowExpandButton()
    {
        if (flowExpandButton == null) return;
        flowExpandButton.onClick.RemoveListener(ToggleFlowExpanded);
        flowExpandButton.onClick.AddListener(ToggleFlowExpanded);
    }

    void ToggleFlowExpanded()
    {
        flowExpanded = !flowExpanded;
        ApplyFlowExpandedState();
        Sfx.Play(SfxId.UiClick);
    }

    void ApplyFlowExpandedState()
    {
        if (flowPanel == null) return;
        for (int i = 0; i < flowPanel.childCount; i++)
        {
            Transform child = flowPanel.GetChild(i);
            if (child.name.StartsWith("LayoutV") || child.name == "FlowHeader") continue;
            child.gameObject.SetActive(flowExpanded);
        }

        if (flowExpandArrowLabel != null)
        {
            flowExpandArrowLabel.text = ">";
            flowExpandArrowLabel.rectTransform.localEulerAngles = flowExpanded
                ? new Vector3(0f, 0f, -90f)
                : Vector3.zero;
        }

        LayoutFlowPanel();
    }

    void UpdateFlowHeader()
    {
        if (flowHeaderLabel == null) return;
        flowHeaderLabel.text = "Production Flows";
        flowHeaderLabel.enableAutoSizing = true;
        flowHeaderLabel.fontSizeMin = 14f;
        flowHeaderLabel.fontSizeMax = 18f;
        flowHeaderLabel.textWrappingMode = TextWrappingModes.NoWrap;
        flowHeaderLabel.overflowMode = TextOverflowModes.Truncate;
    }

    void BuildEconomicsPanel(Transform parent)
    {
        var panel = new GameObject("EconomicsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = new Color(0.11f, 0.12f, 0.16f, 0.98f);
        var panelLe = panel.AddComponent<LayoutElement>();
        panelLe.minHeight = 80;
        panelLe.flexibleWidth = 1;

        var panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var panelVlg = panel.AddComponent<VerticalLayoutGroup>();
        panelVlg.padding = new RectOffset(10, 10, 8, 8);
        panelVlg.spacing = 4;
        panelVlg.childAlignment = TextAnchor.UpperLeft;
        panelVlg.childControlWidth = true;
        panelVlg.childControlHeight = true;
        panelVlg.childForceExpandWidth = true;
        panelVlg.childForceExpandHeight = false;

        var heading = MakeLabel(panel.transform, "Capacity & Staffing", 16f,
            new Color(0.94f, 0.76f, 0.34f, 1f), 24f);
        heading.fontStyle = FontStyles.Bold;
        heading.gameObject.name = "CapacityHeading";

        var statusGo = new GameObject("FlowStats", typeof(RectTransform));
        statusGo.transform.SetParent(panel.transform, false);
        flowStatus = statusGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) flowStatus.font = TMP_Settings.defaultFontAsset;
        flowStatus.fontSize = 15;
        flowStatus.color = new Color(0.9f, 0.92f, 0.96f, 1f);
        flowStatus.alignment = TextAlignmentOptions.TopLeft;
        flowStatus.textWrappingMode = TextWrappingModes.Normal;
        flowStatus.lineSpacing = 1f;
        flowStatus.raycastTarget = false;
        var statusLe = statusGo.AddComponent<LayoutElement>();
        statusLe.minHeight = 40;
        statusLe.flexibleWidth = 1;
        var statusFitter = statusGo.AddComponent<ContentSizeFitter>();
        statusFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        statusFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildSystemEfficiencyBar(panel.transform);
    }

    void BuildSystemEfficiencyBar(Transform parent)
    {
        var root = new GameObject("SystemEfficiency", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(ContentSizeFitter));
        root.transform.SetParent(parent, false);
        LayoutElement rootSize = root.GetComponent<LayoutElement>();
        rootSize.flexibleWidth = 1f;
        ContentSizeFitter rootFitter = root.GetComponent<ContentSizeFitter>();
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 3f;
        rootLayout.padding = new RectOffset(0, 0, 2, 0);
        rootLayout.childAlignment = TextAnchor.UpperLeft;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        Transform headingRow = MakeRow(root.transform, "EfficiencyHeader", 28f);
        systemEfficiencyHeading = MakeLabel(headingRow, "Station Health", 14f,
            GameUITheme.TextSecondary, 26f);
        systemEfficiencyHeading.gameObject.name = "StationHealth";
        systemEfficiencyHeading.fontStyle = FontStyles.Bold;
        LayoutElement headingLayout = systemEfficiencyHeading.GetComponent<LayoutElement>();
        headingLayout.minWidth = 110f;
        headingLayout.flexibleWidth = 1f;
        systemEfficiencyDetailsButton = MakeChip(headingRow, "Details", 72f);
        systemEfficiencyDetailsButton.gameObject.name = "DetailsButton";
        systemEfficiencyDetailsButton.onClick.AddListener(ToggleSystemEfficiencyDetails);

        systemEfficiencySummary = MakeLabel(root.transform, "", 13f,
            new Color(0.82f, 0.87f, 0.94f, 1f), 22f);
        systemEfficiencySummary.gameObject.name = "Summary";
        systemEfficiencySummary.textWrappingMode = TextWrappingModes.NoWrap;
        systemEfficiencySummary.overflowMode = TextOverflowModes.Truncate;

        var row = new GameObject("StateButtons", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(root.transform, false);
        systemEfficiencyDetailsRow = row.transform;
        LayoutElement rowSize = row.GetComponent<LayoutElement>();
        rowSize.minHeight = 34f;
        rowSize.preferredHeight = 34f;
        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 4f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        row.SetActive(false);

        string[] labels = { "WORKING", "BLOCKED", "STARVED", "IDLE" };
        string[] colors = { "#55C9A5", "#E27676", "#E2B455", "#B7BEC5" };
        for (int i = 0; i < systemEfficiencyButtons.Length; i++)
        {
            int category = i;
            var buttonObject = new GameObject("StateButton" + i,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(row.transform, false);
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = GameUITheme.Charcoal;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;
            LayoutElement buttonSize = buttonObject.GetComponent<LayoutElement>();
            buttonSize.minWidth = 66f;
            buttonSize.flexibleWidth = 1f;
            buttonSize.minHeight = 34f;
            buttonSize.preferredHeight = 34f;

            var labelObject = new GameObject("Label", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(2f, 1f);
            labelRect.offsetMax = new Vector2(-2f, -1f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 12f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 13f;
            label.text = labels[i] + "  0%";
            label.color = ColorUtility.TryParseHtmlString(colors[i], out Color parsed)
                ? parsed : Color.white;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            systemEfficiencyButtons[i] = button;
            systemEfficiencyButtonLabels[i] = label;
            button.onClick.AddListener(() => ToggleSystemEfficiencyExplanation(category));
        }

        var explanationObject = new GameObject("StateExplanation", typeof(RectTransform),
            typeof(TextMeshProUGUI), typeof(LayoutElement));
        explanationObject.transform.SetParent(root.transform, false);
        systemEfficiencyExplanation = explanationObject.GetComponent<TextMeshProUGUI>();
        systemEfficiencyExplanation.fontSize = 12f;
        systemEfficiencyExplanation.textWrappingMode = TextWrappingModes.Normal;
        systemEfficiencyExplanation.alignment = TextAlignmentOptions.Center;
        systemEfficiencyExplanation.color = GameUITheme.TextPrimary;
        systemEfficiencyExplanation.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            systemEfficiencyExplanation.font = TMP_Settings.defaultFontAsset;
        LayoutElement explanationSize = explanationObject.GetComponent<LayoutElement>();
        explanationSize.minHeight = 0f;
        explanationSize.preferredHeight = 0f;
        explanationObject.SetActive(false);
        BindSystemEfficiencyBar(root.transform);
    }

    void BindSystemEfficiencyBar(Transform root)
    {
        systemEfficiencyRoot = root;
        if (root == null) return;
        displayedEfficiencyStationCount = -1;
        for (int i = 0; i < displayedEfficiencyPercents.Length; i++)
            displayedEfficiencyPercents[i] = -1;
        systemEfficiencyHeading = root.Find("EfficiencyHeader/StationHealth")?.GetComponent<TextMeshProUGUI>();
        systemEfficiencySummary = root.Find("Summary")?.GetComponent<TextMeshProUGUI>();
        systemEfficiencyExplanation = root.Find("StateExplanation")?.GetComponent<TextMeshProUGUI>();
        systemEfficiencyDetailsRow = root.Find("StateButtons");
        systemEfficiencyDetailsExpanded = systemEfficiencyDetailsRow != null
            && systemEfficiencyDetailsRow.gameObject.activeSelf;
        systemEfficiencyDetailsButton = root.Find("EfficiencyHeader/DetailsButton")?.GetComponent<Button>();
        if (systemEfficiencyDetailsButton != null)
        {
            systemEfficiencyDetailsButton.onClick.RemoveAllListeners();
            systemEfficiencyDetailsButton.onClick.AddListener(ToggleSystemEfficiencyDetails);
            var buttonText = systemEfficiencyDetailsButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null) buttonText.text = systemEfficiencyDetailsExpanded ? "Hide" : "Details";
        }
        Transform row = root.Find("StateButtons");
        for (int i = 0; i < systemEfficiencyButtons.Length; i++)
        {
            Transform buttonTransform = row != null ? row.Find("StateButton" + i) : null;
            systemEfficiencyButtons[i] = buttonTransform != null
                ? buttonTransform.GetComponent<Button>() : null;
            systemEfficiencyButtonLabels[i] = buttonTransform != null
                ? buttonTransform.Find("Label")?.GetComponent<TextMeshProUGUI>() : null;
            if (systemEfficiencyButtons[i] == null) continue;
            int category = i;
            systemEfficiencyButtons[i].onClick.RemoveAllListeners();
            systemEfficiencyButtons[i].onClick.AddListener(
                () => ToggleSystemEfficiencyExplanation(category));
        }
    }

    void ToggleSystemEfficiencyExplanation(int category)
    {
        if (systemEfficiencyExplanation == null) return;
        if (!systemEfficiencyDetailsExpanded)
            ToggleSystemEfficiencyDetails();
        bool show = expandedSystemEfficiencyState != category;
        expandedSystemEfficiencyState = show ? category : -1;
        systemEfficiencyExplanation.gameObject.SetActive(show);
        if (show)
        {
            switch (category)
            {
                case 0:
                    systemEfficiencyExplanation.text = "Working: Share of tracked station time spent processing items.";
                    break;
                case 1:
                    systemEfficiencyExplanation.text = "Blocked: Share of time stations could not move work forward.";
                    break;
                case 2:
                    systemEfficiencyExplanation.text = "Starved: Share of time stations lacked required inputs.";
                    break;
                default:
                    systemEfficiencyExplanation.text = "Idle: Share of time no active work was needed.";
                    break;
            }
            LayoutElement explanationSize = systemEfficiencyExplanation.GetComponent<LayoutElement>();
            if (explanationSize != null)
                explanationSize.minHeight = explanationSize.preferredHeight = 40f;
        }
        else
        {
            LayoutElement explanationSize = systemEfficiencyExplanation.GetComponent<LayoutElement>();
            if (explanationSize != null)
                explanationSize.minHeight = explanationSize.preferredHeight = 0f;
        }
        LayoutFlowPanel();
    }

    void ToggleSystemEfficiencyDetails()
    {
        systemEfficiencyDetailsExpanded = !systemEfficiencyDetailsExpanded;
        if (systemEfficiencyDetailsRow != null)
            systemEfficiencyDetailsRow.gameObject.SetActive(systemEfficiencyDetailsExpanded);
        if (!systemEfficiencyDetailsExpanded)
        {
            expandedSystemEfficiencyState = -1;
            if (systemEfficiencyExplanation != null)
            {
                systemEfficiencyExplanation.gameObject.SetActive(false);
                LayoutElement explanationSize = systemEfficiencyExplanation.GetComponent<LayoutElement>();
                if (explanationSize != null)
                    explanationSize.minHeight = explanationSize.preferredHeight = 0f;
            }
        }
        if (systemEfficiencyDetailsButton != null)
        {
            var label = systemEfficiencyDetailsButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = systemEfficiencyDetailsExpanded ? "Hide" : "Details";
        }
        LayoutFlowPanel();
    }

    void RefreshSystemEfficiency()
    {
        if (systemEfficiencyRoot == null || production == null) return;
        HashSet<StationNode> stations = efficiencyStations;
        stations.Clear();
        // SelectedFlow normalizes every route and allocates while doing so.
        // The recurring efficiency display only needs to read the selection.
        List<ProductionFlowPlan> flows = production.productionFlows;
        ProductionFlowPlan flow = flows != null && flows.Count > 0
            ? flows[Mathf.Clamp(production.selectedFlowIndex, 0, flows.Count - 1)] : null;
        if (flow?.stations != null)
        {
            foreach (GameObject stationObject in flow.stations)
            {
                if (stationObject == null
                    || stationObject.GetComponent<HeatLampStation>() != null
                    || stationObject.GetComponent<Register>() != null
                    || stationObject.GetComponent<FreezerStation>() != null
                    || stationObject.GetComponent<PantryStation>() != null)
                    continue;
                StationNode node = stationObject.GetComponent<StationNode>();
                if (node != null) stations.Add(node);
            }
        }

        float workingSeconds = 0f;
        float blockedSeconds = 0f;
        float starvedSeconds = 0f;
        float idleSeconds = 0f;
        foreach (StationNode node in stations)
        {
            StationRuntimeMetrics metrics = node != null
                ? node.GetComponent<StationRuntimeMetrics>() : null;
            if (metrics == null) continue;
            workingSeconds += metrics.WorkingSeconds;
            blockedSeconds += metrics.BlockedSeconds;
            starvedSeconds += metrics.StarvedSeconds;
            idleSeconds += metrics.IdleSeconds;
        }

        float total = workingSeconds + blockedSeconds + starvedSeconds + idleSeconds;
        float[] exact = efficiencyExact;
        exact[0] = total > 0.001f ? workingSeconds * 100f / total : 0f;
        exact[1] = total > 0.001f ? blockedSeconds * 100f / total : 0f;
        exact[2] = total > 0.001f ? starvedSeconds * 100f / total : 0f;
        exact[3] = total > 0.001f ? idleSeconds * 100f / total : 100f;
        int[] percents = efficiencyPercents;
        float[] remainders = efficiencyRemainders;
        int assigned = 0;
        for (int i = 0; i < exact.Length; i++)
        {
            percents[i] = Mathf.FloorToInt(exact[i]);
            remainders[i] = exact[i] - percents[i];
            assigned += percents[i];
        }
        for (int left = 100 - assigned; left > 0; left--)
        {
            int largest = 0;
            for (int i = 1; i < remainders.Length; i++)
                if (remainders[i] > remainders[largest]) largest = i;
            percents[largest]++;
            remainders[largest] = -1f;
        }

        string sourceName = flow != null ? flow.flowName : null;
        if (systemEfficiencyHeading != null && (sourceName != displayedEfficiencyFlowName
            || stations.Count != displayedEfficiencyStationCount))
        {
            systemEfficiencyHeading.text = "Station Health · " + stations.Count;
            displayedEfficiencyFlowName = sourceName;
            displayedEfficiencyStationCount = stations.Count;
        }
        for (int i = 0; i < systemEfficiencyButtonLabels.Length; i++)
            if (systemEfficiencyButtonLabels[i] != null && displayedEfficiencyPercents[i] != percents[i])
            {
                systemEfficiencyButtonLabels[i].text = EfficiencyNames[i] + "  " + percents[i] + "%";
                displayedEfficiencyPercents[i] = percents[i];
            }
        if (systemEfficiencySummary != null)
        {
            systemEfficiencySummary.text = "<color=#55C9A5>Working " + percents[0]
                + "%</color>   <color=#E27676>Blocked " + percents[1]
                + "%</color>   <color=#E2B455>Starved " + percents[2]
                + "%</color>   <color=#B7BEC5>Idle " + percents[3] + "%</color>";
        }
    }

    void LayoutFlowPanel()
    {
        if (flowPanel == null) return;
        flowPanel.anchorMin = new Vector2(0f, 1f);
        flowPanel.anchorMax = new Vector2(1f, 1f);
        flowPanel.pivot = new Vector2(0.5f, 1f);
        flowPanel.anchoredPosition = new Vector2(0f, -44f);
        flowPanel.sizeDelta = new Vector2(-24f, flowPanel.sizeDelta.y);

        var fitter = flowPanel.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = flowPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutRebuilder.ForceRebuildLayoutImmediate(flowPanel);
        FitScrollArea(rebuildFlowLayout: false);
    }

    Transform MakeRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return row.transform;
    }

    TextMeshProUGUI MakeLabel(Transform parent, string text, float size, Color color, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return tmp;
    }

    void MakeRowPrefix(Transform row, string text, float width = 62f)
    {
        var label = MakeLabel(row, text, 11f, new Color(0.62f, 0.68f, 0.78f, 1f), 24f);
        var layout = label.GetComponent<LayoutElement>();
        if (layout == null) return;
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
    }

    TMP_InputField MakeNameInput(Transform parent)
    {
        var go = new GameObject("FlowName", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        Image inputBackground = go.GetComponent<Image>();
        inputBackground.color = new Color(0.1f, 0.12f, 0.17f, 1f);
        inputBackground.raycastTarget = true;
        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 110f;
        layout.preferredWidth = 180f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 34f;

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 2f);
        rect.offsetMax = new Vector2(-8f, -2f);
        var text = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 16f;
        text.color = Color.white;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        text.enableAutoSizing = true;
        text.fontSizeMin = 13f;
        text.fontSizeMax = 16f;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(go.transform, false);
        var phRect = placeholderGo.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(8f, 2f);
        phRect.offsetMax = new Vector2(-8f, -2f);
        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) placeholder.font = TMP_Settings.defaultFontAsset;
        placeholder.text = "Flow name";
        placeholder.fontSize = 15f;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.raycastTarget = false;
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.textWrappingMode = TextWrappingModes.NoWrap;
        placeholder.overflowMode = TextOverflowModes.Truncate;

        var input = go.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.textViewport = rect;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.targetGraphic = inputBackground;
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.characterLimit = ProductionFlowPlan.MaxNameLength;
        input.onSelect.AddListener(OnFlowNameSelected);
        input.onEndEdit.AddListener(OnFlowNameEdited);
        return input;
    }

    void OnFlowNameSelected(string value)
    {
        flowNameEditStarted = true;
    }

    void OnFlowNameEdited(string value)
    {
        if (!flowNameEditStarted) return;
        flowNameEditStarted = false;
        if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress)
        {
            UpdateFlowHeader();
            return;
        }
        if (production == null) return;
        production.SelectedFlow.SetName(value);
        string clean = production.SelectedFlow.flowName;
        if (production.SelectedFlow.workers != null)
        {
            foreach (KitchenEmployee worker in production.SelectedFlow.workers)
                if (worker != null)
                    worker.assignedFlowName = clean;
        }
        if (flowNameInput != null && flowNameInput.text != clean)
            flowNameInput.SetTextWithoutNotify(clean);
        RebuildFlowListRow();
        RebuildWorkerRow(production.SelectedFlow);
        ConfigureWorkerAssignmentButtons();
        UpdateFlowHeader();
        LayoutFlowPanel();
    }

    Button MakeChip(Transform parent, string label, float width)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = HudTabColors.Idle;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.flexibleWidth = 0;
        le.minHeight = 32;
        le.preferredHeight = 32;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 11f;
        tmp.fontSizeMax = 14f;
        return go.GetComponent<Button>();
    }

    void WireFlowActionButtons()
    {
        if (createFlowButton == null && flowPanel != null)
            createFlowButton = flowPanel.Find("ProductionFlowsPanel/FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("ActionsRow/CreateFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowListRow/CreateFlow")?.GetComponent<Button>();
        if (createFlowButton != null)
        {
            createFlowButton.onClick.RemoveListener(CreateFlow);
            createFlowButton.onClick.AddListener(CreateFlow);
        }

        Button editFlowButton = null;
        if (flowPanel != null)
            editFlowButton = flowPanel.Find("ProductionFlowsPanel/FlowActionRow/EditFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowActionRow/EditFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowHeader/EditFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("ActionsRow/EditFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowListRow/EditFlow")?.GetComponent<Button>();
        if (editFlowButton != null)
        {
            editFlowButton.onClick.RemoveListener(EditFlow);
            editFlowButton.onClick.AddListener(EditFlow);
        }
    }

    void CreateFlow()
    {
        if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress) return;
        if (production == null) return;
        ManagementModeController controller = ManagementModeController.Instance;
        if (controller == null)
            controller = FindObjectOfType<ManagementModeController>();
        if (controller == null) return;
        if (controller.IsCapturingFlow) return;

        production.EnsureProductionFlows();
        ProductionFlowPlan flow = production.SelectedFlow;
        bool reuseEmpty = flow != null
            && flow.stations.Count == 0
            && (flow.stepIds == null || flow.stepIds.Count == 0);
        if (!reuseEmpty)
            flow = production.CreateProductionFlow();

        controller.BeginFlowCapture(flow, isNew: true);
    }

    void EditFlow()
    {
        if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress
            && !ShiftPlanningUI.IsPlanning) return;
        if (production == null) return;
        ManagementModeController controller = ManagementModeController.Instance;
        if (controller == null)
            controller = FindObjectOfType<ManagementModeController>();
        if (controller == null) return;
        if (controller.IsCapturingFlow) return;

        production.EnsureProductionFlows();
        ProductionFlowPlan flow = production.SelectedFlow;
        if (flow == null) return;

        WorkerFlowAssigner.SynchronizeFlowRoute(flow);

        if (flow.stations.Count == 0 && (flow.stepIds == null || flow.stepIds.Count == 0))
        {
            controller.BeginFlowCapture(flow, isNew: true);
            return;
        }

        controller.BeginFlowEdit(flow);
    }

    void RebuildFlowListRow()
    {
        if (flowListRow == null || flowTabsContent == null || production == null) return;

        if (production.productionFlows == null || production.productionFlows.Count == 0)
            production.EnsureProductionFlows();
        bool rebuild = displayedTabFlows.Count != production.productionFlows.Count;
        for (int i = 0; !rebuild && i < production.productionFlows.Count; i++)
        {
            ProductionFlowPlan flow = production.productionFlows[i];
            rebuild = displayedTabFlows[i] != flow
                || displayedTabNames[i] != flow?.flowName
                || (flow != null && displayedTabButtons[i] == null);
        }

        if (rebuild)
        {
            for (int i = flowTabsContent.childCount - 1; i >= 0; i--)
            {
                GameObject oldTab = flowTabsContent.GetChild(i).gameObject;
                oldTab.SetActive(false);
                Destroy(oldTab);
            }
            displayedTabFlows.Clear();
            displayedTabNames.Clear();
            displayedTabButtons.Clear();
            displayedSelectedTab = -1;
        }

        if (flowListRow.gameObject.activeSelf != flowExpanded)
            flowListRow.gameObject.SetActive(flowExpanded);
        for (int i = 0; i < production.productionFlows.Count; i++)
        {
            ProductionFlowPlan flow = production.productionFlows[i];
            if (rebuild)
            {
                displayedTabFlows.Add(flow);
                displayedTabNames.Add(flow?.flowName);
                displayedTabButtons.Add(null);
            }
            if (flow == null) continue;
            int index = i;
            bool selected = index == production.selectedFlowIndex;
            Button chip = displayedTabButtons[i];
            if (rebuild)
            {
                string label = string.IsNullOrEmpty(flow.flowName) ? ("Flow " + (i + 1)) : flow.flowName;
                chip = MakeChip(flowTabsContent, label, Mathf.Clamp(18f + label.Length * 7f, 82f, 150f));
                chip.gameObject.name = "FlowTab_" + (i + 1);
                displayedTabButtons[i] = chip;
                chip.onClick.AddListener(() =>
                {
                    if (ManagementModeController.Instance != null && ManagementModeController.Instance.IsCapturingFlow)
                        return;
                    production.SelectProductionFlow(index);
                    if (ManagementModeController.Instance != null)
                        ManagementModeController.Instance.SelectFlow(flow);
                    var cameraController = FindObjectOfType<PlayerCameraController>();
                    if (cameraController != null)
                        cameraController.PanTo(flow);
                    RefreshFlowSection();
                });
                chip.gameObject.AddComponent<WorkerFlowDropTarget>();
            }
            if (!rebuild && displayedSelectedTab == production.selectedFlowIndex) continue;
            var img = chip.GetComponent<Image>();
            GameUITheme.ApplyCompactTabButton(chip, selected);
            chip.GetComponent<WorkerFlowDropTarget>()?.Bind(this, flow, img != null ? img.color : HudTabColors.Idle);
        }
        displayedSelectedTab = production.selectedFlowIndex;

        if (rebuild && flowTabsContent is RectTransform tabsRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabsRect);
        if (rebuild && flowTabsScroll != null)
            LayoutRebuilder.MarkLayoutForRebuild(flowTabsScroll.transform as RectTransform);

        if (deleteFlowButton == null) return;
        deleteFlowButton.interactable = (GameTimeManager.Instance == null || !GameTimeManager.Instance.IsShiftInProgress)
            && (ManagementModeController.Instance == null || !ManagementModeController.Instance.IsCapturingFlow);
        if (wiredDeleteFlowButton != deleteFlowButton)
        {
            deleteFlowButton.onClick.RemoveAllListeners();
            deleteFlowButton.onClick.AddListener(DeleteSelectedFlow);
            wiredDeleteFlowButton = deleteFlowButton;
        }
    }

    void DeleteSelectedFlow()
    {
        if (production == null) return;
        if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress) return;
        if (ManagementModeController.Instance != null && ManagementModeController.Instance.IsCapturingFlow) return;
        production.RemoveProductionFlow(production.SelectedFlow);
        if (ManagementModeController.Instance != null)
            ManagementModeController.Instance.SelectFlow(production.SelectedFlow);
        WorkerAssignmentLinkVisuals.SetFocusedFlow(production.SelectedFlow);
        WorkerAssignmentLinkVisuals.NotifyLinksChanged();
        StationOutputLinkVisuals.NotifyLinksChanged();
        Refresh();
    }

    void RebuildStepRow()
    {
        if (stepRow == null) return;
        for (int i = stepRow.childCount - 1; i >= 0; i--)
            Destroy(stepRow.GetChild(i).gameObject);

        MakeRowPrefix(stepRow, "Stations");

        ProductionFlowPlan flow = production != null ? production.SelectedFlow : null;
        int count = flow != null && flow.stations.Count > 0 ? flow.stations.Count : flow != null ? flow.stepIds.Count : 0;
        if (count == 0)
        {
            MakeLabel(stepRow, "No stations", 12,
                new Color(0.7f, 0.74f, 0.8f, 1f), 24);
            return;
        }

        flow.EnsureLegacyConnections();
        if (flow.connections.Count > 0)
        {
            foreach (ProductionFlowConnection connection in flow.connections)
            {
                if (connection == null || connection.from == null || connection.to == null) continue;
                StationNode fromNode = StationNode.EnsureOn(connection.from);
                StationNode toNode = StationNode.EnsureOn(connection.to);
                string edgeLabel = (fromNode != null ? fromNode.DisplayName : connection.from.name)
                    + " → " + (toNode != null ? toNode.DisplayName : connection.to.name);
                Button edgeChip = MakeChip(stepRow, edgeLabel, Mathf.Clamp(48f + edgeLabel.Length * 6f, 110f, 210f));
                edgeChip.interactable = false;
            }
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                var arrow = MakeLabel(stepRow, "→", 13, new Color(0.7f, 0.73f, 0.8f, 1f), 24);
                arrow.alignment = TextAlignmentOptions.Center;
                var arrowLayout = arrow.GetComponent<LayoutElement>();
                arrowLayout.minWidth = 14f;
                arrowLayout.preferredWidth = 14f;
                arrowLayout.flexibleWidth = 0f;
            }
            string label;
            if (flow.stations.Count > 0)
            {
                StationNode node = StationNode.EnsureOn(flow.stations[i]);
                label = node != null ? node.DisplayName : flow.stations[i].name;
            }
            else
            {
                FlowStationDef def = WorkerFlowAssigner.FindDef(flow.stepIds[i]);
                label = def != null ? def.label : flow.stepIds[i];
            }
            Button stationChip = MakeChip(stepRow, label, 86f);
            stationChip.interactable = false;
        }
    }

    void RebuildWorkerRow(ProductionFlowPlan flow)
    {
        if (workerRow == null) return;
        for (int i = workerRow.childCount - 1; i >= 0; i--)
            Destroy(workerRow.GetChild(i).gameObject);
        workerAssignmentSummary = MakeLabel(workerRow, "No flow selected.", 14f,
            new Color(0.9f, 0.92f, 0.96f, 1f), 42f);
        workerAssignmentSummary.gameObject.name = "WorkerAssignmentSummary";
        workerAssignmentSummary.textWrappingMode = TextWrappingModes.Normal;
        var summaryLayout = workerAssignmentSummary.GetComponent<LayoutElement>();
        if (summaryLayout != null)
        {
            summaryLayout.minWidth = 120f;
            summaryLayout.flexibleWidth = 1f;
        }

        if (flow != null)
        {
            int assignedCount = 0;
            if (flow.workers != null)
                foreach (KitchenEmployee worker in flow.workers)
                    if (worker != null) assignedCount++;
            workerAssignmentSummary.text = assignedCount > 0
                ? "Assigned · " + assignedCount + (assignedCount == 1 ? " worker" : " workers")
                : "No workers assigned. Drag one here or use Assign below.";
        }
        CreateWorkerDropZone(workerRow, flow);
    }

    void CreateWorkerDropZone(Transform parent, ProductionFlowPlan flow)
    {
        Color idleColor = new Color(0.20f, 0.29f, 0.40f, 1f);
        Button dropZone = MakeChip(parent, "Drop Worker Here", 142f);
        dropZone.interactable = true;
        dropZone.gameObject.name = "WorkerDropZone";
        var dropLayout = dropZone.GetComponent<LayoutElement>();
        if (dropLayout != null)
        {
            dropLayout.minHeight = 38f;
            dropLayout.preferredHeight = 38f;
        }
        var image = dropZone.GetComponent<Image>();
        if (image != null) image.color = idleColor;
        var label = dropZone.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.fontSize = 14f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.78f, 0.88f, 1f, 1f);
            label.fontSizeMin = 11f;
            label.fontSizeMax = 14f;
            label.enableAutoSizing = true;
        }
        var target = dropZone.gameObject.AddComponent<WorkerFlowDropTarget>();
        target.Bind(this, flow, idleColor);
    }

    void RefreshFlowSection()
    {
        try
        {
            EnsureFlowSection();
            if (production == null) return;

            production.EnsureProductionFlows();
            RefreshSystemEfficiency();
            ProductionFlowPlan flow = production.productionFlows[production.selectedFlowIndex];
            bool planPhase = GameTimeManager.Instance == null || !GameTimeManager.Instance.IsShiftInProgress;
            bool canEditFlow = planPhase || ShiftPlanningUI.IsPlanning;
            if (createFlowButton != null) createFlowButton.interactable = planPhase;
            if (flowNameInput != null) flowNameInput.interactable = planPhase;
            Button editButton = flowPanel != null
                ? flowPanel.Find("ProductionFlowsPanel/FlowActionRow/EditFlow")?.GetComponent<Button>()
                    ?? flowPanel.Find("FlowActionRow/EditFlow")?.GetComponent<Button>()
                    ?? flowPanel.Find("FlowHeader/EditFlow")?.GetComponent<Button>()
                    ?? flowPanel.Find("ActionsRow/EditFlow")?.GetComponent<Button>()
                    ?? flowPanel.Find("FlowListRow/EditFlow")?.GetComponent<Button>()
                : null;
            if (editButton != null) editButton.interactable = canEditFlow
                && (ManagementModeController.Instance == null || !ManagementModeController.Instance.IsCapturingFlow);
            UpdateFlowHeader();
            if (flow != null
                && (ManagementModeController.Instance == null || !ManagementModeController.Instance.IsCapturingFlow))
            {
                WorkerFlowAssigner.SynchronizeFlowRoute(flow);
            }

            RebuildFlowListRow();
            RebuildStepRow();
            RebuildWorkerRow(flow);
            ConfigureWorkerAssignmentButtons();
            if (flowNameInput != null && !flowNameInput.isFocused && flowNameInput.text != flow.flowName)
                flowNameInput.SetTextWithoutNotify(flow.flowName);
            WorkerAssignmentLinkVisuals.SetFocusedFlow(flow);

            if (flowStatus == null)
            {
                LayoutFlowPanel();
                return;
            }

            if (flow.stations.Count == 0 && (flow.stepIds == null || flow.stepIds.Count == 0))
            {
                flowStatus.text = "Add stations with <b>Edit Flow</b> to see this flow's capacity.";
                AppendShiftWorkerHint();
                LayoutFlowPanel();
                return;
            }

            var layout = WorkflowAnalysis.AnalyzeFlowLayout(flow);
            var sb = new System.Text.StringBuilder();
            int stationCount = flow.stations != null ? flow.stations.Count : 0;
            int workerCount = flow.workers != null ? flow.workers.Count : 0;
            sb.Append("<b>Output</b>  ").Append(FormatItemRate(layout.effectiveOutputPerMinute))
                .Append(" / ").Append(FormatItemRate(layout.stationCapacityPerMinute)).Append(" items/min");
            sb.Append("\n<b>Workers</b>  ").Append(workerCount).Append(" assigned");
            if (layout.usesScheduledForecast)
            {
                sb.Append(" · Next minute ").Append(layout.next60Output).Append(" items")
                    .Append("\n<b>Delivered recently</b>  ")
                    .Append(FormatItemRate(layout.actualOutputPerMinute)).Append(" items/min");
            }
            else
            {
                sb.Append(" · ").Append(stationCount).Append(" stations")
                    .Append("\n<b>Delivered recently</b>  ")
                    .Append(FormatItemRate(layout.actualOutputPerMinute)).Append(" items/min");
                if (layout.transportShortfallPerMinute > 0.01f)
                {
                    int extraWorkers = WorkflowAnalysis.GetBaseWorkersNeeded(layout.transportShortfallPerMinute);
                    sb.Append("\n<color=#F2C66D>Coverage short by ")
                        .Append(FormatItemRate(layout.transportShortfallPerMinute)).Append("/min")
                        .Append(extraWorkers > 0 ? " · add " + extraWorkers + " worker(s)" : "")
                        .Append("</color>");
                }
            }
            flowStatus.text = sb.ToString();
            AppendShiftWorkerHint();
            LayoutFlowPanel();
        }
        finally
        {
            if (flowSectionNeedsTheme && flowPanel != null)
            {
                GameUITheme.ApplyTo(flowPanel);
                flowSectionNeedsTheme = false;
            }
        }
    }

    void AppendShiftWorkerHint()
    {
        if (flowStatus == null || GameTimeManager.Instance == null
            || !GameTimeManager.Instance.IsShiftInProgress)
            return;
        flowStatus.text += "\n<color=#77C7A5>During a shift, changes apply after a worker finishes their current delivery.</color>";
    }

    string GetRawResourceName(ItemDefinition resource)
    {
        if (resource == null) return "Unknown";

        CustomerOrderConfig config = production != null ? production.orderConfig : null;
        if (config != null)
        {
            if (config.IsBurger(resource)) return "Patty";
            if (resource == config.friesIngredient) return "Potatoes";
            if (config.IsFries(resource)) return "Potatoes";
            if (config.IsDrink(resource)) return "Shake";
        }

        return KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetDisplayName(resource)
            : (!string.IsNullOrEmpty(resource.itemName) ? resource.itemName : resource.name);
    }

    static void AppendDeliveryRateSummary(System.Text.StringBuilder text,
        ProductionFlowPlan selectedFlow, WorkflowAnalysis.FlowLayoutMetrics selectedMetrics)
    {
        if (text == null) return;

        if (selectedMetrics == null || selectedFlow == null
            || selectedFlow.stations == null || selectedFlow.stations.Count == 0)
        {
            text.Append("Add stations to see this flow capacity.");
            return;
        }

        if (selectedMetrics.usesScheduledForecast)
        {
            text.Append("<b>Workers:</b> ").Append(selectedMetrics.activeWorkerCount)
                .Append(" assigned | 4 items/trip")
                .Append("\n<b>Output:</b> ").Append(FormatItemRate(selectedMetrics.effectiveOutputPerMinute))
                .Append(" predicted / ").Append(FormatItemRate(selectedMetrics.stationCapacityPerMinute))
                .Append(" items/min target");
            float outputGap = selectedMetrics.stationCapacityPerMinute
                - selectedMetrics.effectiveOutputPerMinute;
            if (outputGap > 0.1f)
                text.Append(" (short ").Append(FormatItemRate(outputGap)).Append("/min)");
            text.Append("\n<b>Next minute:</b> ").Append(selectedMetrics.next60Output)
                .Append(" items at current stock");
            if (!string.IsNullOrEmpty(selectedMetrics.forecastWarning))
                text.Append("\n").Append(selectedMetrics.forecastWarning);
            return;
        }
        bool understaffed = selectedMetrics.transportShortfallPerMinute > 0.01f;
        text.Append("<b>Workers:</b> ")
            .Append(FormatItemRate(selectedMetrics.laborCapacityPerMinute)).Append(" / ")
            .Append(FormatItemRate(selectedMetrics.totalRateNeededPerMinute))
            .Append(" items/min load incl. station time and travel")
            .Append(" (").Append(selectedMetrics.activeWorkerCount).Append(" assigned");
        if (understaffed)
        {
            int extraWorkers = WorkflowAnalysis.GetBaseWorkersNeeded(selectedMetrics.transportShortfallPerMinute);
            text.Append("; short ").Append(FormatItemRate(selectedMetrics.transportShortfallPerMinute))
                .Append("; add ").Append(extraWorkers)
                .Append(extraWorkers == 1 ? " worker" : " workers")
                .Append(" at ")
                .Append(FormatItemRate(KitchenEmployee.GetTransportItemsPerMinuteForLevel(0)))
                .Append("/min");
        }
        else
        {
            text.Append("; covered");
        }
        text.Append(")")
            .Append("\n<b>Output:</b> ")
            .Append(FormatItemRate(selectedMetrics.effectiveOutputPerMinute)).Append(" estimated / ")
            .Append(FormatItemRate(selectedMetrics.stationCapacityPerMinute)).Append(" items/min max");
        if (!string.IsNullOrEmpty(selectedMetrics.stationBottleneckName))
            text.Append(" (").Append(selectedMetrics.stationBottleneckName).Append(" limit)");
    }

    static string FormatItemRate(float rate)
    {
        return rate.ToString("0.0");
    }

    public void RefreshFlowOnly()
    {
        EnsureRefs();
        RefreshFlowSection();
    }

    public void AssignDraggedWorker(KitchenEmployee employee, ProductionFlowPlan flow)
    {
        if (production == null || employee == null || flow == null) return;
        production.AddWorkerToFlow(flow, employee);
        int flowIndex = production.productionFlows.IndexOf(flow);
        if (flowIndex >= 0)
            production.SelectProductionFlow(flowIndex);
        Sfx.Play(SfxId.UiClick);
        Refresh();
    }

    public void Refresh()
    {
        bool workerCardsRebuilt = false;
        try
        {
            EnsureRefs();
            ApplyCleanLayout();
            EnsureCardContainer();
            EnsureFlowSection();
            RefreshFlowSection();

            if (production == null)
            {
                if (countText != null) countText.text = string.Empty;
                if (costText != null) costText.text = "Hiring unavailable";
                if (hireButton != null) hireButton.interactable = false;
                SetHireButtonLabel("Hiring unavailable");
                return;
            }

            if (countText != null)
                countText.text = string.Empty;

            bool canHire = production.employeePrefab != null;
            if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress)
                canHire = false;
            var money = FindObjectOfType<MoneyManager>();
            int hireCostNow = production.GetHireCost();
            string hireLabel = hireCostNow <= 0 ? "Hire Worker · Free" : "Hire Worker · $" + hireCostNow;
            if (production.employeePrefab == null)
            {
                canHire = false;
                hireLabel = "Hiring unavailable";
            }
            else if (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress)
            {
                canHire = false;
                hireLabel = "Available after shift";
            }
            else if (!production.CanHireWorker())
            {
                canHire = false;
                hireLabel = "Hiring unavailable";
            }
            else if (hireCostNow > 0 && money != null && !money.CanAfford(hireCostNow))
            {
                canHire = false;
                hireLabel = "Hire Worker · $" + hireCostNow;
            }

            if (costText != null)
                costText.text = string.Empty;

            if (hireButton != null)
            {
                hireButton.interactable = canHire;
                SetHireButtonLabel(hireLabel);
            }

            if (cardContainer == null) return;
            int employeeListSignature = GetEmployeeListSignature(production.employees);
            if (!hasDisplayedEmployeeList || employeeListSignature != displayedEmployeeListSignature)
            {
                Transform listHeader = cardContainer.Find("WorkerListHeader");
                for (int i = cardContainer.childCount - 1; i >= 0; i--)
                {
                    Transform child = cardContainer.GetChild(i);
                    if (child != listHeader) Destroy(child.gameObject);
                }

                int workerCount = production.employees != null ? production.employees.Count : 0;
                if (listHeader == null)
                    CreateWorkerListHeader(cardContainer, workerCount);
                else
                {
                    TextMeshProUGUI workerHeading = listHeader.Find("Title")?.GetComponent<TextMeshProUGUI>();
                    if (workerHeading != null) workerHeading.text = "Workers · " + workerCount;
                }
                if (production.employees != null)
                {
                    foreach (var emp in production.employees)
                    {
                        if (emp == null) continue;
                        var card = CreateCard();
                        if (card != null)
                        {
                            card.gameObject.name = "WorkerCard_" + emp.GetInstanceID();
                            card.Bind(emp);
                            card.transform.SetAsLastSibling();
                        }
                    }
                }

                displayedEmployeeListSignature = employeeListSignature;
                hasDisplayedEmployeeList = true;
                workerCardsRebuilt = true;
            }

            ConfigureWorkerAssignmentButtons();

            if (workerCardsRebuilt && cardContainer is RectTransform containerRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
        }
        finally
        {
            if (workerCardsRebuilt)
                GameUITheme.ApplyTo(transform);
        }
    }

    static int GetEmployeeListSignature(IList<KitchenEmployee> employees)
    {
        unchecked
        {
            int signature = 17;
            if (employees == null) return signature * 31;
            signature = signature * 31 + employees.Count;
            for (int i = 0; i < employees.Count; i++)
            {
                KitchenEmployee employee = employees[i];
                signature = signature * 31 + (employee != null ? employee.GetInstanceID() : 0);
                signature = signature * 31 + (employee != null && employee.employeeName != null
                    ? employee.employeeName.GetHashCode() : 0);
            }
            return signature;
        }
    }

    void CreateWorkerListHeader(Transform parent, int workerCount)
    {
        if (parent == null) return;
        var header = new GameObject("WorkerListHeader", typeof(RectTransform), typeof(LayoutElement),
            typeof(VerticalLayoutGroup));
        header.transform.SetParent(parent, false);
        var size = header.GetComponent<LayoutElement>();
        size.minHeight = 88f;
        size.preferredHeight = 88f;
        size.flexibleWidth = 1f;

        var layout = header.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 2, 2);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = MakeLabel(header.transform,
            "Workers  ·  " + workerCount, 18f,
            new Color(0.94f, 0.76f, 0.34f, 1f), 24f);
        title.gameObject.name = "Title";
        title.fontStyle = FontStyles.Bold;
        TextMeshProUGUI hint = MakeLabel(header.transform,
            "Drag a worker to a flow above, or assign them from their card.", 14f,
            new Color(0.82f, 0.86f, 0.92f, 1f), 20f);
        hint.gameObject.name = "Hint";

        Transform hireRow = MakeRow(header.transform, "HireRow", 36f);
        HorizontalLayoutGroup hireRowLayout = hireRow.GetComponent<HorizontalLayoutGroup>();
        if (hireRowLayout != null) hireRowLayout.childAlignment = TextAnchor.MiddleRight;
        if (costText != null)
        {
            costText.transform.SetParent(hireRow, false);
            var costLayout = costText.GetComponent<LayoutElement>();
            if (costLayout == null) costLayout = costText.gameObject.AddComponent<LayoutElement>();
            costLayout.minWidth = 110f;
            costLayout.flexibleWidth = 1f;
            costText.alignment = TextAlignmentOptions.Left;
            costText.fontSize = 14f;
            costText.enableAutoSizing = true;
            costText.fontSizeMin = 12f;
            costText.fontSizeMax = 14f;
            costText.textWrappingMode = TextWrappingModes.NoWrap;
            costText.overflowMode = TextOverflowModes.Truncate;
            costText.gameObject.SetActive(false);
        }
        if (hireButton != null)
        {
            hireButton.transform.SetParent(hireRow, false);
            var hireLayout = hireButton.GetComponent<LayoutElement>();
            if (hireLayout == null) hireLayout = hireButton.gameObject.AddComponent<LayoutElement>();
            hireLayout.minWidth = 205f;
            hireLayout.preferredWidth = 210f;
            hireLayout.flexibleWidth = 0f;
            hireLayout.minHeight = 36f;
            hireLayout.preferredHeight = 36f;
            var hireLabel = hireButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (hireLabel != null)
            {
                hireLabel.text = "Hire Worker";
                hireLabel.fontSize = 15f;
                hireLabel.enableAutoSizing = true;
                hireLabel.fontSizeMin = 12f;
                hireLabel.fontSizeMax = 15f;
                hireLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }

    void ConfigureWorkerAssignmentButtons()
    {
        if (production == null || cardContainer == null) return;
        ProductionFlowPlan selectedFlow = production.SelectedFlow;
        if (selectedFlow == null) return;
        for (int i = 0; i < cardContainer.childCount; i++)
        {
            WorkerCardUI card = cardContainer.GetChild(i).GetComponent<WorkerCardUI>();
            if (card == null || card.employee == null) continue;

            KitchenEmployee worker = card.employee;
            ProductionFlowPlan currentFlow = production.GetFlowForWorker(worker);
            bool assignedHere = currentFlow == selectedFlow;
            string label = assignedHere ? "Remove" : currentFlow == null
                ? "Assign" : "Move here";

            card.ConfigureFlowAssignment(label, assignedHere, () =>
            {
                if (assignedHere)
                {
                    production.RemoveWorkerFromFlow(selectedFlow, worker);
                    Sfx.Play(SfxId.UiClick);
                    Refresh();
                }
                else
                {
                    AssignDraggedWorker(worker, selectedFlow);
                }
            });
        }
    }

    WorkerCardUI CreateCard()
    {
        GameObject cardGo;
        if (workerCardPrefab != null)
            cardGo = Instantiate(workerCardPrefab, cardContainer);
        else
        {
            cardGo = WorkerCardPrefabBuilder.Build();
            cardGo.transform.SetParent(cardContainer, false);
        }

        var card = cardGo.GetComponent<WorkerCardUI>();
        if (card == null)
        {
            Debug.LogWarning("Worker card is missing WorkerCardUI.", cardGo);
            Destroy(cardGo);
            return null;
        }
        var dragSource = cardGo.GetComponent<WorkerFlowDragSource>();
        if (dragSource == null) dragSource = cardGo.AddComponent<WorkerFlowDragSource>();
        dragSource.Bind(card);
        return card;
    }
}

class WorkerFlowDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static KitchenEmployee ActiveWorker { get; private set; }
    static WorkerFlowDragSource activeSource;

    WorkerCardUI card;
    CanvasGroup cardGroup;
    float originalAlpha;
    bool originalBlocksRaycasts;
    GameObject dragLabel;
    RectTransform dragLabelRect;
    Canvas rootCanvas;

    public void Bind(WorkerCardUI workerCard)
    {
        card = workerCard;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (card == null) card = GetComponent<WorkerCardUI>();
        if (card == null || card.employee == null) return;

        FinishActiveDrag();
        activeSource = this;
        ActiveWorker = card.employee;
        cardGroup = GetComponent<CanvasGroup>();
        if (cardGroup == null) cardGroup = gameObject.AddComponent<CanvasGroup>();
        originalAlpha = cardGroup.alpha;
        originalBlocksRaycasts = cardGroup.blocksRaycasts;
        cardGroup.alpha = 0.55f;
        cardGroup.blocksRaycasts = false;

        CreateDragLabel(card.employee.employeeName);
        MoveDragLabel(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (ActiveWorker == null) return;
        MoveDragLabel(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        CleanupDrag();
    }

    void OnDisable()
    {
        if (activeSource == this)
            CleanupDrag();
    }

    void OnDestroy()
    {
        if (activeSource == this)
            CleanupDrag();
    }

    public static void FinishActiveDrag()
    {
        if (activeSource != null)
            activeSource.CleanupDrag();
        else
            ActiveWorker = null;
    }

    void CleanupDrag()
    {
        if (cardGroup != null)
        {
            cardGroup.alpha = originalAlpha;
            cardGroup.blocksRaycasts = originalBlocksRaycasts;
        }
        if (dragLabel != null) Destroy(dragLabel);
        dragLabel = null;
        dragLabelRect = null;
        rootCanvas = null;
        if (activeSource == this)
            activeSource = null;
        ActiveWorker = null;
    }

    void CreateDragLabel(string workerName)
    {
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        dragLabel = new GameObject("WorkerDragLabel", typeof(RectTransform), typeof(Image),
            typeof(CanvasGroup));
        dragLabel.transform.SetParent(rootCanvas.transform, false);
        dragLabel.transform.SetAsLastSibling();
        dragLabelRect = (RectTransform)dragLabel.transform;
        dragLabelRect.sizeDelta = new Vector2(190f, 34f);
        dragLabel.GetComponent<Image>().color = new Color(0.20f, 0.38f, 0.62f, 0.96f);
        var group = dragLabel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(dragLabel.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = workerName;
        text.fontSize = 13f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    void MoveDragLabel(PointerEventData eventData)
    {
        if (dragLabelRect == null || rootCanvas == null) return;
        RectTransform canvasRect = rootCanvas.transform as RectTransform;
        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : rootCanvas.worldCamera;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, camera, out Vector2 localPoint))
            dragLabelRect.localPosition = localPoint;
    }
}

class WorkerFlowDropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    WorkersUI owner;
    ProductionFlowPlan flow;
    Image image;
    Color normalColor;

    public void Bind(WorkersUI workersUi, ProductionFlowPlan targetFlow, Color color)
    {
        owner = workersUi;
        flow = targetFlow;
        image = GetComponent<Image>();
        normalColor = color;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (WorkerFlowDragSource.ActiveWorker != null && image != null)
            image.color = new Color(0.24f, 0.62f, 0.48f, 1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (image != null) image.color = normalColor;
    }

    public void OnDrop(PointerEventData eventData)
    {
        KitchenEmployee employee = WorkerFlowDragSource.ActiveWorker;
        if (employee == null || owner == null || flow == null) return;
        WorkerFlowDragSource.FinishActiveDrag();
        owner.AssignDraggedWorker(employee, flow);
    }
}
