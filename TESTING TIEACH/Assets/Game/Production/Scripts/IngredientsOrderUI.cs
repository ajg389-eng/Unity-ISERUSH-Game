using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Management Food tab: choose the items sold and order ingredient packs.
/// </summary>
public class IngredientsOrderUI : MonoBehaviour
{
    [Tooltip("Parent for order rows. Created at runtime if null.")]
    public Transform listContainer;
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI moneyHintText;
    public KitchenInventory inventory;
    public MoneyManager money;

    [Header("Menu preview prefabs")]
    public GameObject burgerPreviewPrefab;
    public GameObject friesPreviewPrefab;
    public GameObject drinkPreviewPrefab;

    public float refreshInterval = 0.35f;
    float nextRefresh;
    bool built;
    readonly Dictionary<ItemDefinition, int> cartPacks = new Dictionary<ItemDefinition, int>();
    TextMeshProUGUI deliveryStatusText;
    TextMeshProUGUI cartSummaryText;
    Button clearCartButton;
    Button placeOrderButton;
    TextMeshProUGUI placeOrderLabel;

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
        if (headerText != null) headerText.text = "Food";
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
        deliveryStatusText = null;
        cartSummaryText = null;
        clearCartButton = null;
        placeOrderButton = null;
        placeOrderLabel = null;

        if (inventory == null)
        {
            CreateInfoRow("No KitchenInventory in scene. Use Production > Add Kitchen Inventory.");
            built = true;
            return;
        }

        CustomerOrderConfig menu = inventory.orderConfig;
        if (menu != null)
        {
            CreateSectionHeader("Items to sell");
            foreach (var item in menu.GetMenuItems())
                if (item != null) CreateMenuToggleRow(menu, item);
        }

        CreateSectionHeader("Buy ingredients");
        foreach (var item in inventory.GetOrderableItems())
        {
            if (item == null) continue;
            CreateOrderRow(item);
        }
        CreateCartFooter();

        built = true;
    }

    void CreateSectionHeader(string label)
    {
        var go = new GameObject(label.Replace(" ", ""), typeof(RectTransform));
        go.transform.SetParent(listContainer, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 30;
        le.preferredHeight = 30;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 17;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(1f, 0.82f, 0.38f, 1f);
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void CreateMenuToggleRow(CustomerOrderConfig menu, ItemDefinition item)
    {
        var row = new GameObject("Sell_" + item.name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 104;
        le.preferredHeight = 104;
        row.GetComponent<Image>().color = new Color(0.18f, 0.19f, 0.24f, 0.98f);

        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 12, 6, 6);
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        previewGo.transform.SetParent(row.transform, false);
        var previewLe = previewGo.GetComponent<LayoutElement>();
        previewLe.minWidth = 56;
        previewLe.preferredWidth = 56;
        previewLe.minHeight = 56;
        previewLe.preferredHeight = 56;
        var preview = previewGo.GetComponent<RawImage>();
        preview.texture = ItemPreviewThumbnails.GetPrefab(GetPreviewPrefab(menu, item), item.itemName);
        preview.color = Color.white;
        preview.raycastTarget = false;

        var detailsGo = new GameObject("Details", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        detailsGo.transform.SetParent(row.transform, false);
        var detailsLe = detailsGo.GetComponent<LayoutElement>();
        detailsLe.flexibleWidth = 1f;
        detailsLe.minHeight = 88f;
        detailsLe.preferredHeight = 88f;
        var detailsLayout = detailsGo.GetComponent<VerticalLayoutGroup>();
        detailsLayout.spacing = 3f;
        detailsLayout.childAlignment = TextAnchor.MiddleLeft;
        detailsLayout.childControlWidth = true;
        detailsLayout.childControlHeight = true;
        detailsLayout.childForceExpandWidth = true;
        detailsLayout.childForceExpandHeight = false;

        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        nameGo.transform.SetParent(detailsGo.transform, false);
        var nameLe = nameGo.GetComponent<LayoutElement>();
        nameLe.minHeight = 23f;
        nameLe.preferredHeight = 23f;
        var nameText = nameGo.GetComponent<TextMeshProUGUI>();
        nameText.text = inventory.GetDisplayName(item);
        nameText.fontSize = 16;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Left;
        if (TMP_Settings.defaultFontAsset != null) nameText.font = TMP_Settings.defaultFontAsset;

        var workflowGo = new GameObject("Workflow", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        workflowGo.transform.SetParent(detailsGo.transform, false);
        var workflowLe = workflowGo.GetComponent<LayoutElement>();
        workflowLe.minHeight = 58f;
        workflowLe.preferredHeight = 58f;
        var workflowText = workflowGo.GetComponent<TextMeshProUGUI>();
        workflowText.text = GetWorkflowDescription(menu, item);
        workflowText.fontSize = 11.5f;
        workflowText.color = new Color(0.78f, 0.84f, 0.94f, 1f);
        workflowText.alignment = TextAlignmentOptions.TopLeft;
        workflowText.enableWordWrapping = true;
        workflowText.overflowMode = TextOverflowModes.Ellipsis;
        workflowText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) workflowText.font = TMP_Settings.defaultFontAsset;

        Toggle toggle = CreateCheckbox(row.transform);
        toggle.SetIsOnWithoutNotify(menu.IsItemEnabled(item));
        ItemDefinition captured = item;
        toggle.onValueChanged.AddListener(enabled => menu.SetItemEnabled(captured, enabled));
    }

    static string GetWorkflowDescription(CustomerOrderConfig menu, ItemDefinition item)
    {
        if (menu == null || item == null) return "";

        const string label = "<color=#91A4C3>FLOW + MATERIAL</color>  ";
        const string arrow = "  <color=#7E8CA6>→</color>  ";
        string Step(string station, string material) =>
            "<b>" + station + "</b> <color=#7FEA9A>[" + material + "]</color>";

        if (menu.IsBurger(item))
            return label + Step("Freezer", "Frozen patty") + arrow + Step("Grill", "Raw patty")
                + "\n" + Step("Assembly", "Cooked patty") + arrow + Step("Pickup Station", "Burger");

        if (menu.IsFries(item))
            return label + Step("Fryer", "Frozen fries") + arrow + Step("Pickup Station", "Cooked fries");

        if (menu.IsDrink(item))
            return label + Step("Drink Fountain", "Drink stock") + arrow + Step("Pickup Station", "Filled drink");

        return label + "No workflow configured";
    }

    GameObject GetPreviewPrefab(CustomerOrderConfig menu, ItemDefinition item)
    {
        if (menu == null || item == null) return null;
        if (menu.IsBurger(item)) return burgerPreviewPrefab;
        if (menu.IsFries(item)) return friesPreviewPrefab;
        if (menu.IsDrink(item)) return drinkPreviewPrefab;
        return null;
    }

    static Toggle CreateCheckbox(Transform parent)
    {
        var toggleGo = new GameObject("SellToggle", typeof(RectTransform), typeof(LayoutElement), typeof(Toggle));
        toggleGo.transform.SetParent(parent, false);
        var le = toggleGo.GetComponent<LayoutElement>();
        le.minWidth = 34;
        le.preferredWidth = 34;
        le.minHeight = 34;
        le.preferredHeight = 34;

        var backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundGo.transform.SetParent(toggleGo.transform, false);
        var bgRect = (RectTransform)backgroundGo.transform;
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = new Vector2(30f, 30f);
        var background = backgroundGo.GetComponent<Image>();
        background.color = new Color(0.1f, 0.11f, 0.14f, 1f);

        var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        checkGo.transform.SetParent(backgroundGo.transform, false);
        var checkRect = (RectTransform)checkGo.transform;
        checkRect.anchorMin = new Vector2(0.18f, 0.18f);
        checkRect.anchorMax = new Vector2(0.82f, 0.82f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        var check = checkGo.GetComponent<Image>();
        check.color = new Color(0.32f, 0.9f, 0.42f, 1f);

        var toggle = toggleGo.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = check;
        return toggle;
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

        TextMeshProUGUI removeLabel;
        Button removeButton = CreateCartButton(row.transform, "Remove", "-", 36f, new Color(0.32f, 0.33f, 0.4f, 1f), out removeLabel);

        var countGo = new GameObject("CartCount", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        countGo.transform.SetParent(row.transform, false);
        var countLe = countGo.GetComponent<LayoutElement>();
        countLe.minWidth = 46f;
        countLe.preferredWidth = 46f;
        var countTmp = countGo.GetComponent<TextMeshProUGUI>();
        countTmp.fontSize = 14;
        countTmp.fontStyle = FontStyles.Bold;
        countTmp.color = Color.white;
        countTmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) countTmp.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI addLabel;
        Button addButton = CreateCartButton(row.transform, "Add", "+", 44f, new Color(0.27f, 0.62f, 0.4f, 1f), out addLabel);

        var captured = item;
        removeButton.onClick.AddListener(() =>
        {
            SetCartPacks(captured, GetCartPacks(captured) - 1);
            RefreshAll();
        });
        addButton.onClick.AddListener(() =>
        {
            SetCartPacks(captured, GetCartPacks(captured) + 1);
            RefreshAll();
        });

        var binder = row.AddComponent<IngredientOrderRow>();
        binder.item = item;
        binder.infoText = infoTmp;
        binder.stockText = stockTmp;
        binder.removeButton = removeButton;
        binder.addButton = addButton;
        binder.cartCountText = countTmp;
    }

    static Button CreateCartButton(Transform parent, string objectName, string label, float width, Color color, out TextMeshProUGUI text)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = 38f;
        go.GetComponent<Image>().color = color;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return go.GetComponent<Button>();
    }

    void CreateCartFooter()
    {
        var statusGo = new GameObject("DeliveryStatus", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        statusGo.transform.SetParent(listContainer, false);
        var statusLe = statusGo.GetComponent<LayoutElement>();
        statusLe.minHeight = 38f;
        statusLe.preferredHeight = 38f;
        statusGo.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.18f, 0.98f);
        var statusTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        statusTextGo.transform.SetParent(statusGo.transform, false);
        var statusTextRt = (RectTransform)statusTextGo.transform;
        statusTextRt.anchorMin = Vector2.zero;
        statusTextRt.anchorMax = Vector2.one;
        statusTextRt.offsetMin = new Vector2(6f, 0f);
        statusTextRt.offsetMax = new Vector2(-6f, 0f);
        deliveryStatusText = statusTextGo.GetComponent<TextMeshProUGUI>();
        deliveryStatusText.fontSize = 13f;
        deliveryStatusText.alignment = TextAlignmentOptions.Center;
        deliveryStatusText.color = new Color(0.85f, 0.9f, 1f, 1f);
        if (TMP_Settings.defaultFontAsset != null) deliveryStatusText.font = TMP_Settings.defaultFontAsset;

        var footer = new GameObject("CartCheckout", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
        footer.transform.SetParent(listContainer, false);
        var footerLe = footer.GetComponent<LayoutElement>();
        footerLe.minHeight = 52f;
        footerLe.preferredHeight = 52f;
        footer.GetComponent<Image>().color = new Color(0.18f, 0.19f, 0.24f, 0.98f);
        var layout = footer.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 7, 7);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var summaryGo = new GameObject("Summary", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        summaryGo.transform.SetParent(footer.transform, false);
        summaryGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
        cartSummaryText = summaryGo.GetComponent<TextMeshProUGUI>();
        cartSummaryText.fontSize = 13f;
        cartSummaryText.alignment = TextAlignmentOptions.Left;
        cartSummaryText.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) cartSummaryText.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI clearLabel;
        clearCartButton = CreateCartButton(footer.transform, "ClearCart", "Clear", 62f, new Color(0.34f, 0.35f, 0.42f, 1f), out clearLabel);
        clearLabel.fontSize = 12f;
        clearCartButton.onClick.AddListener(() =>
        {
            cartPacks.Clear();
            RefreshAll();
        });

        placeOrderButton = CreateCartButton(footer.transform, "PlaceOrder", "Place Order", 126f, new Color(0.27f, 0.62f, 0.4f, 1f), out placeOrderLabel);
        placeOrderLabel.fontSize = 12f;
        placeOrderButton.onClick.AddListener(SubmitCart);
    }

    int GetCartPacks(ItemDefinition item)
    {
        return item != null && cartPacks.TryGetValue(item, out int count) ? count : 0;
    }

    void SetCartPacks(ItemDefinition item, int count)
    {
        if (item == null) return;
        if (count <= 0) cartPacks.Remove(item);
        else cartPacks[item] = count;
    }

    void SubmitCart()
    {
        if (inventory == null || cartPacks.Count == 0) return;
        if (inventory.TryOrderCart(cartPacks))
        {
            cartPacks.Clear();
            RefreshAll();
        }
    }

    public void Refresh() => RefreshAll();

    void RefreshAll()
    {
        EnsureRefs();
        if (!built) RebuildRows();

        if (moneyHintText != null)
            moneyHintText.text = money != null ? ("Money: $" + money.CurrentMoney) : "Money: —";

        if (listContainer == null || inventory == null) return;
        var delivery = IngredientDeliveryService.Instance;
        bool deliveryActive = delivery != null && delivery.HasPending;
        int cartTotal = 0;
        int cartPackCount = 0;
        foreach (Transform child in listContainer)
        {
            var row = child.GetComponent<IngredientOrderRow>();
            if (row == null || row.item == null) continue;

            string name = inventory.GetDisplayName(row.item);
            int pack = inventory.GetPackSize(row.item);
            int price = inventory.GetPackPrice(row.item);
            int stock = inventory.GetCount(row.item);

            int incoming = 0;
            if (delivery != null)
                incoming = delivery.GetIncomingCount(row.item);

            if (row.infoText != null)
                row.infoText.text = name + "\n<size=85%>Pack of " + pack + "  |  $" + price + "</size>";
            if (row.stockText != null)
            {
                row.stockText.text = incoming > 0
                    ? "Stock\n" + stock + "\n<size=80%>+" + incoming + " incoming</size>"
                    : "Stock\n" + stock;
            }
            int selected = GetCartPacks(row.item);
            cartPackCount += selected;
            cartTotal += selected * price;
            if (row.cartCountText != null) row.cartCountText.text = "x" + selected;
            if (row.removeButton != null) row.removeButton.interactable = !deliveryActive && selected > 0;
            if (row.addButton != null) row.addButton.interactable = !deliveryActive;
        }


        bool affordable = money == null || money.CanAfford(cartTotal);
        if (deliveryStatusText != null)
        {
            float remaining = delivery != null ? delivery.NextDeliveryRemaining : -1f;
            deliveryStatusText.text = deliveryActive
                ? "Delivery in progress  |  Arrives " + IngredientDeliveryService.FormatCountdown(Mathf.Max(0f, remaining)) + "  |  New orders locked"
                : "Build your cart, then place one combined delivery order.";
            deliveryStatusText.color = deliveryActive
                ? new Color(1f, 0.78f, 0.35f, 1f)
                : new Color(0.75f, 0.84f, 0.96f, 1f);
        }
        if (cartSummaryText != null)
            cartSummaryText.text = cartPackCount == 0 ? "Cart is empty" : "Cart: " + cartPackCount + " pack" + (cartPackCount == 1 ? "" : "s") + "  |  $" + cartTotal;
        if (clearCartButton != null)
            clearCartButton.interactable = !deliveryActive && cartPackCount > 0;
        if (placeOrderButton != null)
            placeOrderButton.interactable = !deliveryActive && cartPackCount > 0 && affordable;
        if (placeOrderLabel != null)
            placeOrderLabel.text = deliveryActive ? "Delivery Active" : (!affordable && cartPackCount > 0 ? "Need $" + cartTotal : "Place Order $" + cartTotal);
    }
}

/// <summary>Runtime binder for one ingredient order row.</summary>
public class IngredientOrderRow : MonoBehaviour
{
    public ItemDefinition item;
    public TextMeshProUGUI infoText;
    public TextMeshProUGUI stockText;
    public Button removeButton;
    public Button addButton;
    public TextMeshProUGUI cartCountText;
}
