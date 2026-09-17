using System.Collections.Generic;
using UnityEngine;
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
    RectTransform flowPanel;
    Transform flowListRow;
    Transform stepRow;
    Transform workerRow;
    TMP_InputField flowNameInput;
    Button createFlowButton;
    TextMeshProUGUI flowStatus;
    Button flowExpandButton;
    TextMeshProUGUI flowExpandArrowLabel;
    TextMeshProUGUI flowHeaderLabel;
    bool flowExpanded = true;

    void Start()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();
        EnsureFlowSection();
        PurchaseUndoFooter.EnsureOnPanel(transform);
    }

    void OnEnable()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();
        ConfigureWorkerScroll();
        EnsureFlowSection();
        PurchaseUndoFooter.EnsureOnPanel(transform);
        Refresh();
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

        var title = transform.Find("Title") as RectTransform;
        if (title != null)
        {
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
        headerRt.anchoredPosition = new Vector2(0f, -34f);
        headerRt.sizeDelta = new Vector2(-24f, 36f);

        var headerImg = header.GetComponent<Image>();
        if (headerImg == null) headerImg = header.gameObject.AddComponent<Image>();
        headerImg.color = new Color(0.18f, 0.18f, 0.24f, 0.9f);
        headerImg.raycastTarget = false;

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
            FitScrollArea();
            ConfigureWorkerScroll();
            return;
        }

        var scrollGo = new GameObject("WorkerCardsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        workerCardsScroll = scroll;

        var scrollBg = scrollGo.AddComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0f);
        scrollBg.raycastTarget = true;
        scrollGo.AddComponent<ScrollRectWheelBlocker>().scrollRect = scroll;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRect = (RectTransform)viewport.transform;
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

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
        FitScrollArea();
        ConfigureWorkerScroll();
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
        workerCardsScroll.scrollSensitivity = 28f;
    }

    void FitScrollArea()
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
            LayoutRebuilder.ForceRebuildLayoutImmediate(flowPanel);
            float panelHeight = Mathf.Max(44f, flowPanel.rect.height);
            // Header ends ~70px from top; panel starts at 74px; leave 10px gap under panel.
            topInset = 74f + panelHeight + 10f;
        }

        scroll.anchorMin = new Vector2(0f, 0f);
        scroll.anchorMax = Vector2.one;
        scroll.offsetMin = new Vector2(12f, 56f);
        scroll.offsetMax = new Vector2(-12f, -topInset);
        PurchaseUndoFooter.EnsureOnPanel(transform);
        scroll.SetSiblingIndex(Mathf.Max(0, transform.childCount - 2));
    }

    void OnHireClicked()
    {
        if (production == null)
        {
            production = FindObjectOfType<ProductionManager>();
            if (production == null) return;
        }
        production.HireWorker();
        Refresh();
    }

    void EnsureFlowSection()
    {
        var existing = transform.Find("FlowPathPanel") as RectTransform;
        // Rebuild outdated panels so economics/layout fixes apply.
        if (existing != null && existing.Find("LayoutV4") == null)
        {
            Destroy(existing.gameObject);
            existing = null;
            flowPanel = null;
        }

        if (flowPanel != null)
        {
            LayoutFlowPanel();
            return;
        }

        if (existing != null)
        {
            flowPanel = existing;
            flowListRow = existing.Find("FlowListRow");
            stepRow = existing.Find("StepRow");
            workerRow = existing.Find("WorkerRow");
            flowNameInput = existing.Find("FlowHeader/FlowName")?.GetComponent<TMP_InputField>()
                ?? existing.Find("NameRow/FlowName")?.GetComponent<TMP_InputField>();
            if (flowNameInput != null)
                flowNameInput.characterLimit = ProductionFlowPlan.MaxNameLength;
            BindFlowHeader(existing);
            createFlowButton = existing.Find("FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? existing.Find("ActionsRow/CreateFlow")?.GetComponent<Button>()
                ?? existing.Find("FlowListRow/CreateFlow")?.GetComponent<Button>();
            flowStatus = existing.Find("EconomicsPanel/FlowStats")?.GetComponent<TextMeshProUGUI>()
                ?? existing.Find("FlowStats")?.GetComponent<TextMeshProUGUI>()
                ?? existing.Find("Status")?.GetComponent<TextMeshProUGUI>();
            WireFlowActionButtons();
            LayoutFlowPanel();
            return;
        }

        var go = new GameObject("FlowPathPanel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        flowPanel = go.GetComponent<RectTransform>();
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0.14f, 0.15f, 0.2f, 0.95f);
        bg.raycastTarget = true;

        // Version marker — presence means this panel has the cleaned layout.
        var version = new GameObject("LayoutV4", typeof(RectTransform), typeof(LayoutElement));
        version.transform.SetParent(flowPanel, false);
        var versionLe = version.GetComponent<LayoutElement>();
        versionLe.ignoreLayout = true;
        versionLe.minHeight = 0;
        versionLe.preferredHeight = 0;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 8);
        vlg.spacing = 3;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildFlowHeader();

        flowListRow = MakeRow(flowPanel, "FlowListRow", 28);
        stepRow = MakeRow(flowPanel, "StepRow", 28);
        workerRow = MakeRow(flowPanel, "WorkerRow", 28);
        BuildEconomicsPanel(flowPanel);

        LayoutFlowPanel();
        var header = transform.Find("HeaderBar");
        if (header != null)
            flowPanel.SetSiblingIndex(header.GetSiblingIndex() + 1);
    }

    void BuildFlowHeader()
    {
        var header = MakeRow(flowPanel, "FlowHeader", 28);
        flowExpandButton = MakeChip(header, ">", 28f);
        flowExpandButton.gameObject.name = "ExpandFlow";
        flowExpandArrowLabel = flowExpandButton.GetComponentInChildren<TextMeshProUGUI>(true);
        flowHeaderLabel = MakeLabel(header, "FLOW", 10,
            new Color(0.72f, 0.8f, 0.95f, 1f), 24);
        var headerLayout = flowHeaderLabel.GetComponent<LayoutElement>();
        if (headerLayout != null)
        {
            headerLayout.minWidth = 38f;
            headerLayout.preferredWidth = 38f;
            headerLayout.flexibleWidth = 0f;
        }
        flowNameInput = MakeNameInput(header);
        Button editFlowButton = MakeChip(header, "Edit", 62f);
        editFlowButton.gameObject.name = "EditFlow";
        createFlowButton = MakeChip(header, "+ Flow", 72f);
        createFlowButton.gameObject.name = "CreateFlow";
        WireFlowActionButtons();
        WireFlowExpandButton();
        UpdateFlowHeader();
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
        flowHeaderLabel.text = "FLOW";
    }

    void BuildEconomicsPanel(Transform parent)
    {
        var panel = new GameObject("EconomicsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = new Color(0.11f, 0.12f, 0.16f, 0.98f);
        var panelLe = panel.AddComponent<LayoutElement>();
        panelLe.minHeight = 40;
        panelLe.flexibleWidth = 1;

        var panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var panelVlg = panel.AddComponent<VerticalLayoutGroup>();
        panelVlg.padding = new RectOffset(6, 6, 4, 5);
        panelVlg.spacing = 2;
        panelVlg.childAlignment = TextAnchor.UpperLeft;
        panelVlg.childControlWidth = true;
        panelVlg.childControlHeight = true;
        panelVlg.childForceExpandWidth = true;
        panelVlg.childForceExpandHeight = false;

        var statusGo = new GameObject("FlowStats", typeof(RectTransform));
        statusGo.transform.SetParent(panel.transform, false);
        flowStatus = statusGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) flowStatus.font = TMP_Settings.defaultFontAsset;
        flowStatus.fontSize = 12;
        flowStatus.color = new Color(0.86f, 0.9f, 0.78f, 1f);
        flowStatus.alignment = TextAlignmentOptions.TopLeft;
        flowStatus.textWrappingMode = TextWrappingModes.Normal;
        flowStatus.lineSpacing = 4f;
        flowStatus.raycastTarget = false;
        var statusLe = statusGo.AddComponent<LayoutElement>();
        statusLe.minHeight = 28;
        statusLe.flexibleWidth = 1;
        var statusFitter = statusGo.AddComponent<ContentSizeFitter>();
        statusFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        statusFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void LayoutFlowPanel()
    {
        if (flowPanel == null) return;
        flowPanel.anchorMin = new Vector2(0f, 1f);
        flowPanel.anchorMax = new Vector2(1f, 1f);
        flowPanel.pivot = new Vector2(0.5f, 1f);
        flowPanel.anchoredPosition = new Vector2(0f, -74f);
        flowPanel.sizeDelta = new Vector2(-24f, flowPanel.sizeDelta.y);

        var fitter = flowPanel.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = flowPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutRebuilder.ForceRebuildLayoutImmediate(flowPanel);
        FitScrollArea();
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
        var label = MakeLabel(row, text, 9f, new Color(0.62f, 0.68f, 0.78f, 1f), 24f);
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
        go.GetComponent<Image>().color = new Color(0.1f, 0.12f, 0.17f, 1f);
        var layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 160f;
        layout.preferredWidth = 220f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 24f;

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 2f);
        rect.offsetMax = new Vector2(-8f, -2f);
        var text = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 13f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
        placeholderGo.transform.SetParent(go.transform, false);
        var phRect = placeholderGo.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(8f, 2f);
        phRect.offsetMax = new Vector2(-8f, -2f);
        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) placeholder.font = TMP_Settings.defaultFontAsset;
        placeholder.text = "Click to name this flow…";
        placeholder.fontSize = 13f;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.alignment = TextAlignmentOptions.Left;

        var input = go.GetComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.textViewport = rect;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = ProductionFlowPlan.MaxNameLength;
        input.onEndEdit.AddListener(OnFlowNameEdited);
        return input;
    }

    void OnFlowNameEdited(string value)
    {
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
        UpdateFlowHeader();
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
        le.minHeight = 24;

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
        tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return go.GetComponent<Button>();
    }

    void WireFlowActionButtons()
    {
        if (createFlowButton == null && flowPanel != null)
            createFlowButton = flowPanel.Find("FlowHeader/CreateFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("ActionsRow/CreateFlow")?.GetComponent<Button>()
                ?? flowPanel.Find("FlowListRow/CreateFlow")?.GetComponent<Button>();
        if (createFlowButton != null)
        {
            createFlowButton.onClick.RemoveListener(CreateFlow);
            createFlowButton.onClick.AddListener(CreateFlow);
        }

        Button editFlowButton = null;
        if (flowPanel != null)
            editFlowButton = flowPanel.Find("FlowHeader/EditFlow")?.GetComponent<Button>()
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
        if (flowListRow == null || production == null) return;

        for (int i = flowListRow.childCount - 1; i >= 0; i--)
            Destroy(flowListRow.GetChild(i).gameObject);

        production.EnsureProductionFlows();
        flowListRow.gameObject.SetActive(flowExpanded);
        for (int i = 0; i < production.productionFlows.Count; i++)
        {
            ProductionFlowPlan flow = production.productionFlows[i];
            if (flow == null) continue;
            int index = i;
            bool selected = index == production.selectedFlowIndex;
            string label = string.IsNullOrEmpty(flow.flowName) ? ("Flow " + (i + 1)) : flow.flowName;
            Button chip = MakeChip(flowListRow, label, Mathf.Clamp(18f + label.Length * 7f, 72f, 140f));
            var img = chip.GetComponent<Image>();
            if (img != null)
                img.color = selected ? HudTabColors.Active : HudTabColors.Idle;
            chip.onClick.AddListener(() =>
            {
                if (ManagementModeController.Instance != null && ManagementModeController.Instance.IsCapturingFlow)
                    return;
                production.SelectProductionFlow(index);
                var cameraController = FindObjectOfType<PlayerCameraController>();
                if (cameraController != null)
                    cameraController.PanTo(flow);
                RefreshFlowSection();
                if (flowNameInput != null)
                    flowNameInput.ActivateInputField();
            });
        }
    }

    void RebuildStepRow()
    {
        if (stepRow == null) return;
        for (int i = stepRow.childCount - 1; i >= 0; i--)
            Destroy(stepRow.GetChild(i).gameObject);

        MakeRowPrefix(stepRow, "STATIONS");

        ProductionFlowPlan flow = production != null ? production.SelectedFlow : null;
        int count = flow != null && flow.stations.Count > 0 ? flow.stations.Count : flow != null ? flow.stepIds.Count : 0;
        if (count == 0)
        {
            MakeLabel(stepRow, "No stations", 12,
                new Color(0.7f, 0.74f, 0.8f, 1f), 24);
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

        MakeRowPrefix(workerRow, "WORKERS");

        if (flow == null || flow.workers.Count == 0)
        {
            MakeLabel(workerRow, "No workers assigned", 12,
                new Color(0.7f, 0.74f, 0.8f, 1f), 24);
            return;
        }

        foreach (KitchenEmployee worker in new List<KitchenEmployee>(flow.workers))
        {
            if (worker == null) continue;
            KitchenEmployee capturedWorker = worker;
            Button chip = MakeChip(workerRow, worker.employeeName + "  ×", 118f);
            chip.onClick.AddListener(() =>
            {
                production.RemoveWorkerFromFlow(flow, capturedWorker);
                Refresh();
            });
        }
    }

    void RefreshFlowSection()
    {
        EnsureFlowSection();
        if (production == null) return;

        production.EnsureProductionFlows();
        ProductionFlowPlan flow = production.SelectedFlow;
        UpdateFlowHeader();
        if (flow != null
            && (ManagementModeController.Instance == null || !ManagementModeController.Instance.IsCapturingFlow))
        {
            WorkerFlowAssigner.SynchronizeFlowRoute(flow);
        }

        RebuildFlowListRow();
        RebuildStepRow();
        RebuildWorkerRow(flow);

        if (flowNameInput != null && !flowNameInput.isFocused)
            flowNameInput.SetTextWithoutNotify(flow.flowName);
        WorkerAssignmentLinkVisuals.SetFocusedFlow(flow);

        if (flowStatus == null)
        {
            LayoutFlowPanel();
            return;
        }

        if (flow.stations.Count == 0 && (flow.stepIds == null || flow.stepIds.Count == 0))
        {
            flowStatus.text = "<size=10><b>ECONOMICS</b></size>  Add stations to analyze this flow.\n"
                + "<b>Resources required:</b> None";
            LayoutFlowPanel();
            return;
        }

        var economics = WorkflowAnalysis.AnalyzeFlow(flow);
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(economics.summary))
            sb.Append(economics.summary);
        for (int i = 0; i < economics.lines.Count; i++)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(economics.lines[i]);
        }
        if (sb.Length > 0) sb.Append('\n');
        sb.Append("<b>Resources required:</b> ");
        if (economics.requiredResources.Count > 0)
        {
            for (int i = 0; i < economics.requiredResources.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                ItemDefinition resource = economics.requiredResources[i];
                string resourceName = GetRawResourceName(resource);
                sb.Append(resourceName).Append(" ×1");
            }
        }
        else
        {
            sb.Append("None");
        }
        flowStatus.text = "<size=10><b>ECONOMICS</b></size>  " + sb;
        LayoutFlowPanel();
    }

    string GetRawResourceName(ItemDefinition resource)
    {
        if (resource == null) return "Unknown";

        CustomerOrderConfig config = production != null ? production.orderConfig : null;
        if (config != null)
        {
            if (config.IsBurger(resource)) return "Patty";
            if (config.IsFries(resource)) return "Frozen Fries";
            if (config.IsDrink(resource)) return "Drink Stock";
        }

        return KitchenInventory.Instance != null
            ? KitchenInventory.Instance.GetDisplayName(resource)
            : (!string.IsNullOrEmpty(resource.itemName) ? resource.itemName : resource.name);
    }

    public void RefreshFlowOnly()
    {
        EnsureRefs();
        RefreshFlowSection();
    }

    public void Refresh()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();
        EnsureFlowSection();
        RefreshFlowSection();

        if (production == null)
        {
            if (countText != null) countText.text = "Workers: —";
            if (costText != null) costText.text = "Hire: —";
            if (hireButton != null) hireButton.interactable = false;
            return;
        }

        int count = production.employees != null ? production.employees.Count : 0;
        if (countText != null)
            countText.text = "Workers: " + count;

        bool canHire = production.employeePrefab != null;
        var money = FindObjectOfType<MoneyManager>();
        int hireCostNow = production.GetHireCost();
        string costLabel = hireCostNow <= 0 ? "FREE" : "$" + hireCostNow;
        if (production.employeePrefab == null)
        {
            canHire = false;
            costLabel = "N/A";
        }
        else if (hireCostNow > 0 && money != null && !money.CanAfford(hireCostNow))
        {
            canHire = false;
            costLabel = "$" + hireCostNow;
        }

        if (costText != null)
            costText.text = costLabel;

        if (hireButton != null)
            hireButton.interactable = canHire;

        if (cardContainer == null) return;
        for (int i = cardContainer.childCount - 1; i >= 0; i--)
            Destroy(cardContainer.GetChild(i).gameObject);

        if (production.employees == null) return;
        foreach (var emp in production.employees)
        {
            if (emp == null) continue;
            var card = CreateCard();
            if (card != null)
                card.Bind(emp);
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
        return card;
    }
}
