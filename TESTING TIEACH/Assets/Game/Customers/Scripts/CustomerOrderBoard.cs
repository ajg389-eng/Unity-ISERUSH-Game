using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Movable wall display with a larger, live customer-order view.</summary>
public class CustomerOrderBoard : MonoBehaviour
{
    TextMeshProUGUI wallText;
    GameObject overlay;
    RectTransform content;
    TextMeshProUGUI heading;
    GameModeManager mode;
    float nextRefresh;
    RectTransform orderViewport;
    float lastGridWidth = -1f;
    class OrderCard
    {
        public RectTransform root;
        public TextMeshProUGUI header, patience;
        public readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
        public readonly List<CustomerOrder.OrderLine> lines = new List<CustomerOrder.OrderLine>();
    }
    readonly Dictionary<CustomerAI, OrderCard> rows = new Dictionary<CustomerAI, OrderCard>();
    readonly List<CustomerAI> removed = new List<CustomerAI>();
    readonly List<CustomerAI> customers = new List<CustomerAI>();

    void Awake()
    {
        if (GetComponent<WallMountedCutawayFollower>() == null)
            gameObject.AddComponent<WallMountedCutawayFollower>();
        var face = new GameObject("Order board face", typeof(RectTransform), typeof(Canvas));
        face.transform.SetParent(transform, false);
        face.transform.localPosition = new Vector3(0, 0, -0.51f);
        face.transform.localScale = new Vector3(1f / 900f, 1f / 450f, 1f);
        face.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 450);
        face.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var background = MakeRect("Background", face.transform, new Vector2(900, 450));
        background.gameObject.AddComponent<Image>().color = new Color(.07f, .11f, .14f);
        wallText = MakeText(background, 38);
        wallText.alignment = TextAlignmentOptions.Center;
        wallText.margin = new Vector4(20, 20, 20, 20);
    }

    void OnMouseUpAsButton()
    {
        if (mode == null) mode = FindFirstObjectByType<GameModeManager>();
        if (WallPhotoDrag.InputClaimed || (mode != null && mode.CurrentMode == GameModeManager.Mode.Build)) return;
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        OpenOrderView();
    }

    void OpenOrderView()
    {
        var inventory = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var management = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (inventory != null && inventory.IsPanelOpen) inventory.TogglePanel();
        if (management != null && management.IsOpen) management.Close();
        if (mode == null) mode = FindFirstObjectByType<GameModeManager>();
        if (mode != null) mode.SetMode(GameModeManager.Mode.Play);
        if (overlay == null) CreateOverlay();
        overlay.SetActive(true);
        nextRefresh = 0;
    }

    void Update()
    {
        if ((Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            && !WallPhotoDrag.InputClaimed && !IsTyping() && !PauseMenuUI.IsOpen)
        {
            if (overlay != null && overlay.activeSelf)
                overlay.SetActive(false);
            else
                OpenOrderView();
        }
        if (overlay != null && overlay.activeSelf && Input.GetKeyDown(KeyCode.Escape)) overlay.SetActive(false);
        if (WallPhotoDrag.IsDragging || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .5f;
        customers.Clear();
        foreach (var customer in CustomerAI.OrderBoardCustomers)
            if (customer != null && !customer.IsLeaving && !customer.IsOrderFullyDelivered) customers.Add(customer);
        customers.Sort((a, b) => a.OrderNumber.CompareTo(b.OrderNumber));
        var preview = new StringBuilder("<b>CUSTOMER ORDERS</b>\n");
        preview.Append(customers.Count).Append(" waiting\n\n");
        for (int i = 0; i < Mathf.Min(3, customers.Count); i++)
            preview.Append(customers[i].CustomerDisplayName).Append("   #").Append(customers[i].OrderNumber).Append('\n');
        if (customers.Count == 0) preview.Append("No open orders\n");
        preview.Append("\n<size=26>Click or press 5 • Drag in Build mode</size>");
        SetText(wallText, preview.ToString());
        if (overlay == null || !overlay.activeSelf) return;
        SetText(heading, "Customer orders  •  " + customers.Count + " waiting");
        removed.Clear();
        foreach (var pair in rows) if (!customers.Contains(pair.Key)) { Destroy(pair.Value.root.gameObject); removed.Add(pair.Key); }
        foreach (var customer in removed) rows.Remove(customer);
        foreach (var customer in customers)
        {
            if (!rows.TryGetValue(customer, out var card))
            {
                card = CreateCard(customer);
                rows.Add(customer, card);
            }
            var remaining = customer.GetOrder();
            SetText(card.header, "<b>" + customer.CustomerDisplayName + "   #" + customer.OrderNumber + "</b>   •   $" + customer.SalePrice);
            for (int i = 0; i < card.lines.Count; i++)
            {
                var line = card.lines[i];
                int left = remaining.CountQuantityOf(line.item);
                string name = string.IsNullOrEmpty(line.item.itemName) ? line.item.name : line.item.itemName;
                SetText(card.labels[i], name + "  ×" + line.quantity + "\n<size=19>" + (left == 0 ? "Delivered" : left + " remaining") + "</size>");
            }
            bool active = customer.patienceMeter != null && customer.patienceMeter.IsActive;
            SetText(card.patience, "Patience: " + (active ? Mathf.RoundToInt(customer.patienceMeter.Normalized * 100) + "%" : "not counting down"));
            int index = customers.IndexOf(customer);
            if (card.root.GetSiblingIndex() != index) card.root.SetSiblingIndex(index);
        }
        LayoutOrderGrid();
    }

    void LateUpdate()
    {
        if (overlay != null && overlay.activeSelf && orderViewport != null
            && Mathf.Abs(orderViewport.rect.width - lastGridWidth) > .5f)
            LayoutOrderGrid();
    }

    void LayoutOrderGrid()
    {
        if (orderViewport == null) return;
        float width = orderViewport.rect.width;
        if (width <= 0) return;
        lastGridWidth = width;
        const float gap = 14f;
        int columns = width >= 800f ? 2 : 1;
        float cardWidth = (width - gap * (columns - 1)) / columns;
        float top = 0f;
        for (int start = 0; start < customers.Count; start += columns)
        {
            float rowHeight = 0;
            for (int column = 0; column < columns && start + column < customers.Count; column++)
                rowHeight = Mathf.Max(rowHeight, 90 + rows[customers[start + column]].lines.Count * 76);
            for (int column = 0; column < columns && start + column < customers.Count; column++)
            {
                var rect = rows[customers[start + column]].root;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                Vector2 size = new Vector2(cardWidth, rowHeight);
                Vector2 position = new Vector2(column * (cardWidth + gap), -top);
                if (rect.sizeDelta != size) rect.sizeDelta = size;
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            }
            top += rowHeight + gap;
        }
        float height = Mathf.Max(0, top - gap);
        if (Mathf.Abs(content.sizeDelta.y - height) > .5f)
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    OrderCard CreateCard(CustomerAI customer)
    {
        var card = new OrderCard();
        card.root = MakeRect("Order", content, Vector2.zero);
        card.root.gameObject.AddComponent<Image>().color = new Color(.16f, .21f, .25f);
        if (customer.OriginalOrder != null) foreach (var line in customer.OriginalOrder.lines)
            if (line.item != null && line.quantity > 0) card.lines.Add(line);
        card.header = MakeText(CardLine(card.root, "Customer", 20, 10, 35), 23);
        card.header.enableAutoSizing = true;
        card.header.fontSizeMin = 18; card.header.fontSizeMax = 23;
        for (int i = 0; i < card.lines.Count; i++)
        {
            var line = card.lines[i];
            var icon = MakeRect("Item picture", card.root, new Vector2(64, 64));
            icon.anchorMin = icon.anchorMax = new Vector2(0, 1);
            icon.pivot = new Vector2(0, 1);
            icon.anchoredPosition = new Vector2(20, -48 - i * 76);
            var image = icon.gameObject.AddComponent<Image>();
            image.sprite = line.item.previewIcon != null ? line.item.previewIcon : CustomerOrderLabel.GetFoodSprite(line.item);
            image.preserveAspect = true;
            image.raycastTarget = false;
            var label = MakeText(CardLine(card.root, "Item name and quantity", 100, 48 + i * 76, 64), 23);
            label.enableAutoSizing = true;
            label.fontSizeMin = 18; label.fontSizeMax = 23;
            card.labels.Add(label);
        }
        card.patience = MakeText(CardLine(card.root, "Patience", 20, 48 + card.lines.Count * 76, 32), 20);
        return card;
    }

    static RectTransform CardLine(Transform parent, string name, float left, float top, float height)
    {
        var rect = MakeRect(name, parent, Vector2.zero);
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-20, -top);
        return rect;
    }

    static void SetText(TextMeshProUGUI text, string value)
    {
        if (text.text != value) text.text = value;
    }

    static bool IsTyping()
    {
        var events = UnityEngine.EventSystems.EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        return selected != null && (selected.GetComponentInParent<TMP_InputField>() != null
            || selected.GetComponentInParent<InputField>() != null);
    }

    void CreateOverlay()
    {
        overlay = new GameObject("Customer orders view", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var shade = MakeRect("Backdrop", overlay.transform, Vector2.zero);
        Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .8f);
        var panel = MakeRect("Panel", shade, Vector2.zero);
        Stretch(panel); panel.anchorMin = new Vector2(.04f, .08f); panel.anchorMax = new Vector2(.96f, .90f);
        panel.gameObject.AddComponent<Image>().color = new Color(.08f, .12f, .16f);
        var title = MakeRect("Title", panel, Vector2.zero);
        Stretch(title); title.anchorMin = new Vector2(0, .87f); title.offsetMin = new Vector2(20, 0); title.offsetMax = new Vector2(-90, -8);
        heading = MakeText(title, 29);
        var close = MakeRect("Close", panel, new Vector2(64, 44));
        close.anchorMin = close.anchorMax = new Vector2(1, 1); close.anchoredPosition = new Vector2(-42, -30);
        close.gameObject.AddComponent<Image>().color = new Color(.3f, .36f, .42f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() => overlay.SetActive(false));
        var closeText = MakeText(close, 23); closeText.text = "Close"; closeText.alignment = TextAlignmentOptions.Center;
        var viewport = MakeRect("Orders", panel, Vector2.zero);
        orderViewport = viewport;
        Stretch(viewport); viewport.anchorMax = new Vector2(1, .85f); viewport.offsetMin = new Vector2(20, 20); viewport.offsetMax = new Vector2(-20, -4);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .01f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        content = MakeRect("Content", viewport, Vector2.zero);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.scrollSensitivity = 60f;
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    static TextMeshProUGUI MakeText(Transform parent, float size)
    {
        var rect = MakeRect("Text", parent, Vector2.zero); Stretch(rect);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size; text.color = Color.white; text.raycastTarget = false; return text;
    }
    void OnDestroy() { if (overlay != null) Destroy(overlay); }
}
