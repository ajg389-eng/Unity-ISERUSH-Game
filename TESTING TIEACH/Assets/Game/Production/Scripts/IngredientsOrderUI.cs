using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management tab: view kitchen stock and order ingredient packs with money.
/// </summary>
public class IngredientsOrderUI : MonoBehaviour
{
    [Tooltip("Parent for order rows. Created at runtime if null.")]
    public Transform listContainer;
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI moneyHintText;
    public KitchenInventory inventory;
    public MoneyManager money;

    public float refreshInterval = 0.35f;
    float nextRefresh;
    bool built;

    void OnEnable()
    {
        EnsureRefs();
        EnsureList();
        RebuildRows();
        PurchaseUndoFooter.EnsureOnPanel(transform);
        RefreshAll();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;
        RefreshAll();
    }

    void EnsureRefs()
    {
        if (inventory == null) inventory = KitchenInventory.Instance != null ? KitchenInventory.Instance : FindObjectOfType<KitchenInventory>();
        if (money == null) money = FindObjectOfType<MoneyManager>();
        if (headerText == null)
        {
            var t = transform.Find("Title");
            if (t != null) headerText = t.GetComponent<TextMeshProUGUI>();
        }
    }

    void EnsureList()
    {
        if (listContainer != null)
        {
            PadListForUndoFooter();
            return;
        }

        var scrollGo = new GameObject("IngredientsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(transform, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(12, 56);
        scrollRt.offsetMax = new Vector2(-12, -48);

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
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        scroll.viewport = vpRt;
        scroll.content = contentRt;
        listContainer = content.transform;
        PadListForUndoFooter();
    }

    void PadListForUndoFooter()
    {
        var scrollRt = transform.Find("IngredientsScroll") as RectTransform;
        if (scrollRt == null && listContainer != null)
        {
            var sr = listContainer.GetComponentInParent<ScrollRect>();
            if (sr != null) scrollRt = sr.transform as RectTransform;
        }
        if (scrollRt == null) return;
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(12, 56);
        scrollRt.offsetMax = new Vector2(-12, -48);
    }

    void RebuildRows()
    {
        EnsureRefs();
        EnsureList();
        if (listContainer == null) return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
            Destroy(listContainer.GetChild(i).gameObject);

        if (inventory == null)
        {
            CreateInfoRow("No KitchenInventory in scene. Use Production > Add Kitchen Inventory.");
            built = true;
            return;
        }

        foreach (var item in inventory.GetOrderableItems())
        {
            if (item == null) continue;
            CreateOrderRow(item);
        }

        built = true;
    }

    void CreateInfoRow(string message)
    {
        var go = new GameObject("Info", typeof(RectTransform));
        go.transform.SetParent(listContainer, false);
        go.AddComponent<LayoutElement>().minHeight = 40;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 14;
        tmp.color = new Color(1f, 0.7f, 0.5f, 1f);
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void CreateOrderRow(ItemDefinition item)
    {
        var row = new GameObject("Order_" + item.name, typeof(RectTransform));
        row.transform.SetParent(listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 56;
        le.preferredHeight = 56;
        var img = row.AddComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 10, 6, 6);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childForceExpandWidth = false;
        hlg.childControlHeight = true;

        var infoGo = new GameObject("Info", typeof(RectTransform));
        infoGo.transform.SetParent(row.transform, false);
        infoGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        var infoTmp = infoGo.AddComponent<TextMeshProUGUI>();
        infoTmp.fontSize = 14;
        infoTmp.color = Color.white;
        infoTmp.alignment = TextAlignmentOptions.Left;
        if (TMP_Settings.defaultFontAsset != null) infoTmp.font = TMP_Settings.defaultFontAsset;

        var stockGo = new GameObject("Stock", typeof(RectTransform));
        stockGo.transform.SetParent(row.transform, false);
        stockGo.AddComponent<LayoutElement>().minWidth = 70;
        var stockTmp = stockGo.AddComponent<TextMeshProUGUI>();
        stockTmp.fontSize = 14;
        stockTmp.color = new Color(0.85f, 0.9f, 1f, 1f);
        stockTmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) stockTmp.font = TMP_Settings.defaultFontAsset;

        var btnGo = new GameObject("OrderButton", typeof(RectTransform));
        btnGo.transform.SetParent(row.transform, false);
        btnGo.AddComponent<LayoutElement>().minWidth = 110;
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.color = new Color(0.28f, 0.45f, 0.32f, 1f);
        var btn = btnGo.AddComponent<Button>();

        var btnTextGo = new GameObject("Text", typeof(RectTransform));
        btnTextGo.transform.SetParent(btnGo.transform, false);
        var btnTmp = btnTextGo.AddComponent<TextMeshProUGUI>();
        btnTmp.fontSize = 13;
        btnTmp.alignment = TextAlignmentOptions.Center;
        btnTmp.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) btnTmp.font = TMP_Settings.defaultFontAsset;
        var btnRt = (RectTransform)btnTextGo.transform;
        btnRt.anchorMin = Vector2.zero;
        btnRt.anchorMax = Vector2.one;
        btnRt.offsetMin = Vector2.zero;
        btnRt.offsetMax = Vector2.zero;

        var captured = item;
        btn.onClick.AddListener(() =>
        {
            if (inventory == null) return;
            if (inventory.TryOrderPack(captured))
                RefreshAll();
        });

        var binder = row.AddComponent<IngredientOrderRow>();
        binder.item = item;
        binder.infoText = infoTmp;
        binder.stockText = stockTmp;
        binder.orderButton = btn;
        binder.orderButtonLabel = btnTmp;
    }

    public void Refresh() => RefreshAll();

    void RefreshAll()
    {
        EnsureRefs();
        if (!built) RebuildRows();

        if (moneyHintText != null)
            moneyHintText.text = money != null ? ("Money: $" + money.CurrentMoney) : "Money: —";

        if (listContainer == null || inventory == null) return;
        foreach (Transform child in listContainer)
        {
            var row = child.GetComponent<IngredientOrderRow>();
            if (row == null || row.item == null) continue;

            string name = inventory.GetDisplayName(row.item);
            int pack = inventory.GetPackSize(row.item);
            int price = inventory.GetPackPrice(row.item);
            int stock = inventory.GetCount(row.item);

            if (row.infoText != null)
                row.infoText.text = name + "\n<size=85%>Pack of " + pack + "</size>";
            if (row.stockText != null)
                row.stockText.text = "Stock\n" + stock;
            if (row.orderButtonLabel != null)
                row.orderButtonLabel.text = "Order $" + price;

            bool can = money == null || money.CanAfford(price);
            if (row.orderButton != null)
                row.orderButton.interactable = can;
        }
    }
}

/// <summary>Runtime binder for one ingredient order row.</summary>
public class IngredientOrderRow : MonoBehaviour
{
    public ItemDefinition item;
    public TextMeshProUGUI infoText;
    public TextMeshProUGUI stockText;
    public Button orderButton;
    public TextMeshProUGUI orderButtonLabel;
}
