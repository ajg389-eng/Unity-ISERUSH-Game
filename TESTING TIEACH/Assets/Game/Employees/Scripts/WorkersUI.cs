using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management screen Workers tab: hire workers, pick a station-to-station flow, list staff.
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
    Transform presetRow;
    Transform stepRow;
    TextMeshProUGUI flowStatus;
    readonly Dictionary<KitchenFlowKind, Button> presetButtons = new Dictionary<KitchenFlowKind, Button>();

    static readonly KitchenFlowKind[] Presets =
    {
        KitchenFlowKind.BurgerLine,
        KitchenFlowKind.FriesLine,
        KitchenFlowKind.Drinks,
        KitchenFlowKind.Register
    };

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

        scroll.anchorMin = new Vector2(0f, 0f);
        scroll.anchorMax = Vector2.one;
        scroll.offsetMin = new Vector2(12f, 56f);
        scroll.offsetMax = new Vector2(-12f, -210f);
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
        if (existing != null && existing.Find("StepRow") == null)
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
            presetRow = existing.Find("PresetRow");
            stepRow = existing.Find("StepRow");
            flowStatus = existing.Find("Status")?.GetComponent<TextMeshProUGUI>();
            LayoutFlowPanel();
            return;
        }

        var go = new GameObject("FlowPathPanel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        flowPanel = go.GetComponent<RectTransform>();
        var bg = go.GetComponent<Image>();
        bg.color = new Color(0.14f, 0.15f, 0.2f, 0.95f);
        bg.raycastTarget = true;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        MakeLabel(flowPanel, "Presets — then edit hops below", 11, new Color(0.72f, 0.75f, 0.82f, 1f), 16);

        presetRow = MakeRow(flowPanel, "PresetRow", 26);
        foreach (var kind in Presets)
        {
            var captured = kind;
            var btn = MakeChip(presetRow, WorkerFlowAssigner.GetTitle(kind), 72f);
            btn.onClick.AddListener(() => SelectPreset(captured));
            presetButtons[kind] = btn;
        }

        var assign = MakeChip(presetRow, "Assign idle", 92f);
        assign.onClick.AddListener(() =>
        {
            if (production == null) return;
            WorkerFlowAssigner.ApplyToIdleWorkers(production.hireFlowSteps, production.hireFlow);
            Refresh();
        });

        MakeLabel(flowPanel, "Click a station to change the next stop", 11, new Color(0.72f, 0.75f, 0.82f, 1f), 16);

        stepRow = MakeRow(flowPanel, "StepRow", 28);

        var statusGo = new GameObject("Status", typeof(RectTransform));
        statusGo.transform.SetParent(flowPanel, false);
        flowStatus = statusGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) flowStatus.font = TMP_Settings.defaultFontAsset;
        flowStatus.fontSize = 11;
        flowStatus.color = new Color(0.82f, 0.86f, 0.7f, 1f);
        flowStatus.alignment = TextAlignmentOptions.MidlineLeft;
        var statusLe = statusGo.AddComponent<LayoutElement>();
        statusLe.minHeight = 16;
        statusLe.preferredHeight = 16;

        LayoutFlowPanel();
        var header = transform.Find("HeaderBar");
        if (header != null)
            flowPanel.SetSiblingIndex(header.GetSiblingIndex() + 1);
    }

    void LayoutFlowPanel()
    {
        if (flowPanel == null) return;
        flowPanel.anchorMin = new Vector2(0f, 1f);
        flowPanel.anchorMax = new Vector2(1f, 1f);
        flowPanel.pivot = new Vector2(0.5f, 1f);
        flowPanel.anchoredPosition = new Vector2(0f, -74f);
        flowPanel.sizeDelta = new Vector2(-24f, 128f);
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

    void SelectPreset(KitchenFlowKind kind)
    {
        if (production == null) return;
        production.hireFlow = kind;
        production.hireFlowSteps = WorkerFlowAssigner.GetPresetSteps(kind);
        RefreshFlowSection();
    }

    List<string> Steps
    {
        get
        {
            if (production == null) return new List<string>();
            if (production.hireFlowSteps == null)
                production.hireFlowSteps = new List<string>();
            if (production.hireFlowSteps.Count == 0)
                production.hireFlowSteps = WorkerFlowAssigner.GetPresetSteps(production.hireFlow);
            return production.hireFlowSteps;
        }
    }

    void CycleStep(int index)
    {
        var available = WorkerFlowAssigner.GetStationsInScene();
        if (available.Count == 0) available.AddRange(WorkerFlowAssigner.Catalog);
        if (index < 0 || index >= Steps.Count || available.Count == 0) return;

        string current = Steps[index];
        int found = 0;
        for (int i = 0; i < available.Count; i++)
        {
            if (available[i].id == current) { found = i; break; }
        }
        Steps[index] = available[(found + 1) % available.Count].id;
        production.hireFlow = KitchenFlowKind.Custom;
        RefreshFlowSection();
    }

    void AddStep()
    {
        if (Steps.Count >= WorkerFlowAssigner.MaxSteps) return;
        var available = WorkerFlowAssigner.GetStationsInScene();
        if (available.Count == 0) available.AddRange(WorkerFlowAssigner.Catalog);
        string nextId = "HeatLamp";
        for (int i = 0; i < available.Count; i++)
        {
            if (!Steps.Contains(available[i].id))
            {
                nextId = available[i].id;
                break;
            }
        }
        Steps.Add(nextId);
        production.hireFlow = KitchenFlowKind.Custom;
        RefreshFlowSection();
    }

    void RemoveLastStep()
    {
        if (Steps.Count <= 1) return;
        Steps.RemoveAt(Steps.Count - 1);
        production.hireFlow = KitchenFlowKind.Custom;
        RefreshFlowSection();
    }

    void RebuildStepRow()
    {
        if (stepRow == null) return;
        for (int i = stepRow.childCount - 1; i >= 0; i--)
            Destroy(stepRow.GetChild(i).gameObject);

        var steps = Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                var arrow = MakeLabel(stepRow, "→", 13, new Color(0.7f, 0.73f, 0.8f, 1f), 24);
                arrow.alignment = TextAlignmentOptions.Center;
                var ale = arrow.GetComponent<LayoutElement>();
                ale.minWidth = 14;
                ale.preferredWidth = 14;
                ale.flexibleWidth = 0;
            }

            int captured = i;
            var def = WorkerFlowAssigner.FindDef(steps[i]);
            var btn = MakeChip(stepRow, def != null ? def.label : steps[i], 86f);
            btn.onClick.AddListener(() => CycleStep(captured));
        }

        if (steps.Count < WorkerFlowAssigner.MaxSteps)
        {
            var add = MakeChip(stepRow, "+", 28f);
            add.onClick.AddListener(AddStep);
        }

        if (steps.Count > 1)
        {
            var remove = MakeChip(stepRow, "–", 28f);
            remove.onClick.AddListener(RemoveLastStep);
        }
    }

    void RefreshFlowSection()
    {
        EnsureFlowSection();
        if (production == null) return;

        RebuildStepRow();

        foreach (var kv in presetButtons)
            HudTabColors.Apply(kv.Value, production.hireFlow == kv.Key);

        if (flowStatus == null) return;
        string missing = WorkerFlowAssigner.DescribeMissing(production.hireFlowSteps);
        if (!string.IsNullOrEmpty(missing))
            flowStatus.text = missing;
        else
            flowStatus.text = "New hires: " + WorkerFlowAssigner.FormatSteps(production.hireFlowSteps);
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
        string costLabel = "$" + production.hireCost;
        if (production.employeePrefab == null)
        {
            canHire = false;
            costLabel = "N/A";
        }
        else if (money != null && !money.CanAfford(production.hireCost))
        {
            canHire = false;
            costLabel = "$" + production.hireCost;
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
