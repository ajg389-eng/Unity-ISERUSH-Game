using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management screen Workers tab: hire workers and list them.
/// Station assignment is done in Manage mode by clicking stations in the world.
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

    void Start()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();
        PurchaseUndoFooter.EnsureOnPanel(transform);
    }

    void OnEnable()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();
        ConfigureWorkerScroll();
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

    /// <summary>Reorganize the Workers panel into a tidy header + list layout.</summary>
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
            title.anchoredPosition = new Vector2(0f, -10f);
            title.sizeDelta = new Vector2(-24f, 28f);
            var titleTmp = title.GetComponent<TextMeshProUGUI>();
            if (titleTmp != null)
            {
                titleTmp.alignment = TextAlignmentOptions.Center;
                titleTmp.fontSize = 20;
            }
        }

        // Header bar: Workers count | Cost | Hire button
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
        headerRt.anchoredPosition = new Vector2(0f, -44f);
        headerRt.sizeDelta = new Vector2(-24f, 44f);

        var headerImg = header.GetComponent<Image>();
        if (headerImg == null) headerImg = header.gameObject.AddComponent<Image>();
        headerImg.color = new Color(0.18f, 0.18f, 0.24f, 0.9f);
        headerImg.raycastTarget = false;

        var hlg = header.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(12, 12, 6, 6);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Move count / cost / hire into header
        if (countText != null)
        {
            countText.transform.SetParent(header, false);
            var le = countText.GetComponent<LayoutElement>();
            if (le == null) le = countText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 100;
            le.flexibleWidth = 1;
            countText.alignment = TextAlignmentOptions.Left;
            countText.fontSize = 15;
            countText.raycastTarget = false;
        }

        if (costText != null)
        {
            costText.transform.SetParent(header, false);
            var le = costText.GetComponent<LayoutElement>();
            if (le == null) le = costText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 90;
            le.preferredWidth = 110;
            costText.alignment = TextAlignmentOptions.Center;
            costText.fontSize = 15;
            costText.color = new Color(0.85f, 0.88f, 0.75f, 1f);
            costText.raycastTarget = false;
        }

        if (hireButton != null)
        {
            hireButton.transform.SetParent(header, false);
            var le = hireButton.GetComponent<LayoutElement>();
            if (le == null) le = hireButton.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 130;
            le.preferredWidth = 140;
            le.minHeight = 32;
            var img = hireButton.GetComponent<Image>();
            if (img != null) img.color = new Color(0.3f, 0.48f, 0.36f, 1f);
            var label = hireButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = "Hire Worker";
                label.fontSize = 15;
            }
        }

        // Hint under header
        if (hintText == null)
        {
            var hintGo = new GameObject("HintText", typeof(RectTransform));
            hintGo.transform.SetParent(transform, false);
            hintText = hintGo.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) hintText.font = TMP_Settings.defaultFontAsset;
        }

        var hintRt = hintText.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 1f);
        hintRt.anchorMax = new Vector2(1f, 1f);
        hintRt.pivot = new Vector2(0.5f, 1f);
        hintRt.anchoredPosition = new Vector2(0f, -94f);
        hintRt.sizeDelta = new Vector2(-28f, 36f);
        hintText.fontSize = 12;
        hintText.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        hintText.alignment = TextAlignmentOptions.TopLeft;
        hintText.textWrappingMode = TextWrappingModes.Normal;
        hintText.raycastTarget = false;
        hintText.text = "Each card shows live task, station assignments, and inventory. Assign stations in Manage mode by clicking them in the world.";

        // Keep header above list in hierarchy for clarity
        header.SetSiblingIndex(1);
        hintText.transform.SetSiblingIndex(2);
    }

    void EnsureCardContainer()
    {
        // Prefer existing scroll if present
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

        var blocker = scrollGo.AddComponent<ScrollRectWheelBlocker>();
        blocker.scrollRect = scroll;

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
        vlg.spacing = 12;
        vlg.padding = new RectOffset(4, 4, 4, 8);
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

        var scrollImage = workerCardsScroll.GetComponent<Image>();
        if (scrollImage == null)
            scrollImage = workerCardsScroll.gameObject.AddComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0f);
        scrollImage.raycastTarget = true;

        var blocker = workerCardsScroll.GetComponent<ScrollRectWheelBlocker>();
        if (blocker == null)
            blocker = workerCardsScroll.gameObject.AddComponent<ScrollRectWheelBlocker>();
        blocker.scrollRect = workerCardsScroll;

        var viewport = workerCardsScroll.viewport;
        if (viewport != null)
        {
            var viewportImage = viewport.GetComponent<Image>();
            if (viewportImage == null)
                viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.raycastTarget = true;
        }
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
        scroll.offsetMax = new Vector2(-12f, -138f); // leave room for title + header + hint
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

    public void Refresh()
    {
        EnsureRefs();
        ApplyCleanLayout();
        EnsureCardContainer();

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
        string costLabel = "Hire: $" + production.hireCost;
        if (production.employeePrefab == null)
        {
            canHire = false;
            costLabel = "Hire: N/A";
        }
        else if (money != null && !money.CanAfford(production.hireCost))
        {
            canHire = false;
            costLabel = "Hire: $" + production.hireCost + " (broke)";
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
