using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management screen Workers tab: hire button, and a scrollable list of worker cards (name, fire, assign stations).
/// </summary>
public class WorkersUI : MonoBehaviour
{
    [Tooltip("Shows current number of employees")]
    public TextMeshProUGUI countText;
    [Tooltip("Shows hire cost (e.g. Cost: $100)")]
    public TextMeshProUGUI costText;
    [Tooltip("Click to hire one worker")]
    public Button hireButton;
    [Tooltip("Prefab for one worker row (must include WorkerCardUI)")]
    public GameObject workerCardPrefab;
    [Tooltip("Parent for worker cards. If null, a scroll area is created at runtime.")]
    public Transform cardContainer;

    ProductionManager production;
    bool listenerAdded;

    void Start()
    {
        EnsureRefs();
        EnsureCardContainer();
    }

    void OnEnable()
    {
        EnsureRefs();
        EnsureCardContainer();
        Refresh();
    }

    void EnsureRefs()
    {
        if (production == null)
            production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();
        if (countText == null) countText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (hireButton == null) hireButton = GetComponentInChildren<Button>(true);
        if (hireButton != null && !listenerAdded)
        {
            hireButton.onClick.AddListener(OnHireClicked);
            listenerAdded = true;
        }
    }

    void EnsureCardContainer()
    {
        if (cardContainer != null) return;
        var rect = GetComponent<RectTransform>();
        if (rect == null) return;

        GameObject scrollGo = new GameObject("WorkerCardsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);
        var scrollRect = (RectTransform)scrollGo.transform;
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(12, 12);
        scrollRect.offsetMax = new Vector2(-12, -140);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRect = (RectTransform)viewport.transform;
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("CardContainer", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        scroll.viewport = vpRect;
        scroll.content = contentRect;
        cardContainer = content.transform;
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
        EnsureCardContainer();

        if (production == null)
        {
            if (countText != null) countText.text = "Workers: —";
            if (costText != null) costText.text = "Cost: — (No ProductionManager in scene)";
            if (hireButton != null) hireButton.interactable = false;
            return;
        }

        int count = production.employees != null ? production.employees.Count : 0;
        if (countText != null)
            countText.text = "Workers: " + count;

        bool canHire = production.employeePrefab != null;
        var money = FindObjectOfType<MoneyManager>();
        string status = "";
        if (production.employeePrefab == null)
        {
            canHire = false;
            status = " — Assign Employee Prefab on ProductionManager.";
        }
        else if (money != null && !money.CanAfford(production.hireCost))
        {
            canHire = false;
            status = " — Not enough money.";
        }

        if (costText != null)
            costText.text = "Cost: $" + production.hireCost + status;

        if (hireButton != null)
            hireButton.interactable = canHire;

        if (cardContainer == null) return;
        for (int i = cardContainer.childCount - 1; i >= 0; i--)
            Destroy(cardContainer.GetChild(i).gameObject);

        if (workerCardPrefab == null)
        {
            Debug.LogWarning("WorkersUI: assign Worker Card Prefab (Assets/Prefabs/WorkerCard.prefab). Create via Production > Create Worker Card UI Prefab.", this);
            return;
        }

        if (production.employees == null) return;
        foreach (var emp in production.employees)
        {
            if (emp == null) continue;
            var cardGo = Instantiate(workerCardPrefab, cardContainer);
            var card = cardGo.GetComponent<WorkerCardUI>();
            if (card == null)
            {
                Debug.LogWarning("Worker card prefab is missing WorkerCardUI: " + workerCardPrefab.name, workerCardPrefab);
                Destroy(cardGo);
                continue;
            }
            card.Bind(emp);
        }
    }
}
