using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
    bool showMenu;
    GameObject navigation;
    readonly List<GameObject> menuRows = new List<GameObject>();
    readonly List<GameObject> supplyRows = new List<GameObject>();
    Button suppliesTab, menuTab, recipesTab;
    readonly Dictionary<ItemDefinition, int> cartPacks = new Dictionary<ItemDefinition, int>();
    TextMeshProUGUI deliveryStatusText;
    TextMeshProUGUI cartSummaryText;
    Button clearCartButton;
    Button addAllButton;
    Button placeOrderButton;
    Button expressOrderButton;
    TextMeshProUGUI placeOrderLabel;
    TextMeshProUGUI expressOrderLabel;
    GameObject expandedMenuRow;
    GameObject expandedWorkflow;
    LayoutElement expandedMenuLayout;
    TextMeshProUGUI expandedChevron;
    GameObject recipesOverlay;
    Transform recipeChartRoot;
    RectTransform recipeChartViewport;
    CustomerOrderConfig recipeChartMenu;
    RecipeGraphDrag recipeGraphDrag;
    TextMeshProUGUI recipeZoomLabel;
    TextMeshProUGUI recipeDetailsText;
    readonly List<RecipeGraphNodeView> recipeGraphNodes = new List<RecipeGraphNodeView>();
    readonly List<RecipeGraphEdgeView> recipeGraphEdges = new List<RecipeGraphEdgeView>();
    readonly List<Button> recipeTabButtons = new List<Button>();

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
        if (headerText != null) headerText.gameObject.SetActive(false);
        if (moneyHintText != null) moneyHintText.gameObject.SetActive(false); // Cash remains visible in the main HUD.
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
        scrollRt.offsetMin = new Vector2(12, showMenu ? 56 : 164);
        scrollRt.offsetMax = new Vector2(-12, -62);

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
        var fastScroll = scrollRt.GetComponent<ScrollRect>();
        if (fastScroll != null)
        {
            GameUITheme.ConfigureScroll(fastScroll);
            fastScroll.inertia = true;
            fastScroll.decelerationRate = 0.135f;
        }
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(12, 164);
        scrollRt.offsetMax = new Vector2(-12, -100);
    }

    void RebuildRows()
    {
        EnsureRefs();
        EnsureList();
        if (listContainer == null) return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
        { var old = listContainer.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
        menuRows.Clear();
        supplyRows.Clear();
        if (deliveryStatusText != null) { var old = deliveryStatusText.transform.parent.gameObject; old.SetActive(false); Destroy(old); }
        if (placeOrderButton != null) { var old = placeOrderButton.transform.parent.gameObject; old.SetActive(false); Destroy(old); }
        deliveryStatusText = null;
        cartSummaryText = null;
        clearCartButton = null;
        placeOrderButton = null;
        expressOrderButton = null;
        placeOrderLabel = null;
        expressOrderLabel = null;
        expandedMenuRow = null;
        expandedWorkflow = null;
        expandedMenuLayout = null;
        expandedChevron = null;

        if (inventory == null)
        {
            CreateInfoRow("No KitchenInventory in scene. Use Production > Add Kitchen Inventory.");
            built = true;
            return;
        }

        CustomerOrderConfig menu = inventory.orderConfig;
        if (menu != null)
        {
            int firstMenuRow = listContainer.childCount;
            CreateSectionHeader("Items to Sell");
            var menuItemsByMilestone = new SortedDictionary<int, List<ItemDefinition>>();
            foreach (ItemDefinition item in menu.GetMenuItems())
            {
                if (item == null) continue;
                int milestone = menu.GetMenuItemUnlockMilestone(item);
                if (!menuItemsByMilestone.TryGetValue(milestone, out List<ItemDefinition> items))
                {
                    items = new List<ItemDefinition>();
                    menuItemsByMilestone.Add(milestone, items);
                }
                items.Add(item);
            }
            foreach (var milestoneGroup in menuItemsByMilestone)
            {
                CreateMenuMilestoneHeader(milestoneGroup.Key);
                foreach (ItemDefinition item in milestoneGroup.Value)
                    CreateMenuToggleRow(menu, item);
            }
            for (int i = firstMenuRow; i < listContainer.childCount; i++) menuRows.Add(listContainer.GetChild(i).gameObject);
        }

        int firstSupplyRow = listContainer.childCount;
        CreateSectionHeader("Order Ingredient Packs");
        var ingredientsByMilestone = new SortedDictionary<int, List<ItemDefinition>>();
        foreach (ItemDefinition item in inventory.GetIngredientCatalogItems())
        {
            if (item == null) continue;
            int milestone = inventory.GetOrderItemUnlockMilestone(item);
            if (!ingredientsByMilestone.TryGetValue(milestone, out List<ItemDefinition> items))
            {
                items = new List<ItemDefinition>();
                ingredientsByMilestone.Add(milestone, items);
            }
            items.Add(item);
        }
        foreach (var milestoneGroup in ingredientsByMilestone)
        {
            CreateMilestoneHeader(milestoneGroup.Key);
            foreach (ItemDefinition item in milestoneGroup.Value)
                CreateOrderRow(item);
        }
        for (int i = firstSupplyRow; i < listContainer.childCount; i++) supplyRows.Add(listContainer.GetChild(i).gameObject);
        CreateCartFooter();
        BuildNavigation(menu);
        SelectSection(showMenu);

        built = true;
        GameUITheme.ApplyTo(transform);
        SelectSection(showMenu);
    }

    void OnDisable()
    {
        if (recipesOverlay != null) recipesOverlay.SetActive(false);
        UpdateNavigationTabs();
    }

    static void PinFooter(RectTransform rect, float bottom, float height)
    {
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.offsetMin = new Vector2(12, bottom);
        rect.offsetMax = new Vector2(-12, bottom + height);
        var layout = rect.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
    }

    void BuildNavigation(CustomerOrderConfig menu)
    {
        if (navigation != null) { navigation.SetActive(false); Destroy(navigation); }
        navigation = new GameObject("MenuSupplyNavigation", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        navigation.transform.SetParent(transform, false);
        var rt = (RectTransform)navigation.transform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(12, -56);
        rt.offsetMax = new Vector2(-12, -12);
        var layout = navigation.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        TextMeshProUGUI label;
        suppliesTab = CreateCartButton(navigation.transform, "SuppliesTab", "Supplies", 90, GameUITheme.Surface, out label);
        suppliesTab.onClick.AddListener(() => SelectSection(false));
        menuTab = CreateCartButton(navigation.transform, "MenuTab", "Menu", 80, GameUITheme.Surface, out label);
        menuTab.onClick.AddListener(() => SelectSection(true));
        recipesTab = CreateCartButton(navigation.transform, "RecipesTab", "Recipes", 90, GameUITheme.Surface, out label);
        recipesTab.interactable = menu != null;
        recipesTab.onClick.AddListener(() => OpenRecipes(menu));
    }

    void SelectSection(bool menu)
    {
        showMenu = menu;
        foreach (var row in menuRows) if (row != null) row.SetActive(menu);
        foreach (var row in supplyRows) if (row != null) row.SetActive(!menu);
        UpdateNavigationTabs();
        if (placeOrderButton != null) placeOrderButton.transform.parent.gameObject.SetActive(!menu);
        if (deliveryStatusText != null) deliveryStatusText.transform.parent.gameObject.SetActive(!menu);
        PadListForUndoFooter();
        Canvas.ForceUpdateCanvases();
        var scroll = listContainer.GetComponentInParent<ScrollRect>();
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }

    void UpdateNavigationTabs()
    {
        bool recipesOpen = recipesOverlay != null && recipesOverlay.activeSelf;
        GameUITheme.ApplyTabButton(suppliesTab, !showMenu && !recipesOpen);
        GameUITheme.ApplyTabButton(menuTab, showMenu && !recipesOpen);
        GameUITheme.ApplyTabButton(recipesTab, recipesOpen);
    }

    void CreateRecipesButton(CustomerOrderConfig menu)
    {
        var row = new GameObject("RecipesButtonRow", typeof(RectTransform), typeof(LayoutElement),
            typeof(HorizontalLayoutGroup));
        row.transform.SetParent(listContainer, false);
        row.GetComponent<LayoutElement>().preferredHeight = 42f;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        TextMeshProUGUI label;
        Button button = CreateCartButton(row.transform, "OpenRecipes", "RECIPES", 132f,
            GameUITheme.Coral, out label);
        label.fontSize = 14f;
        button.onClick.AddListener(() => OpenRecipes(menu));
    }

    void OpenRecipes(CustomerOrderConfig menu)
    {
        if (recipesOverlay == null) BuildRecipesOverlay(menu);
        recipesOverlay.SetActive(true);
        UpdateNavigationTabs();
        ShowRecipeTab(menu, 0);
        recipesOverlay.transform.SetAsLastSibling();
        Sfx.Play(SfxId.UiClick);
    }

    void CloseRecipes()
    {
        if (recipesOverlay != null)
            recipesOverlay.SetActive(false);
        UpdateNavigationTabs();
    }

    void BuildRecipesOverlay(CustomerOrderConfig menu)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform.root;
        recipesOverlay = new GameObject("RecipesOverlay", typeof(RectTransform), typeof(Image));
        recipesOverlay.transform.SetParent(parent, false);
        RectTransform overlayRt = (RectTransform)recipesOverlay.transform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        Color overlayColor = GameUITheme.Backdrop;
        overlayColor.a = 0.78f;
        recipesOverlay.GetComponent<Image>().color = overlayColor;

        var panel = new GameObject("RecipeBook", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(recipesOverlay.transform, false);
        RectTransform panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        RectTransform canvasRect = parent as RectTransform;
        float availableCanvasWidth = canvasRect != null && canvasRect.rect.width > 0f
            ? canvasRect.rect.width : 682f;
        float availableCanvasHeight = canvasRect != null && canvasRect.rect.height > 0f
            ? canvasRect.rect.height : 752f;
        float panelWidth = Mathf.Clamp(availableCanvasWidth - 32f, 720f, 1040f);
        float panelHeight = Mathf.Clamp(availableCanvasHeight - 32f, 260f, 720f);
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);
        panel.GetComponent<Image>().color = GameUITheme.Panel;

        TextMeshProUGUI title = CreateAbsoluteText(panel.transform, "Title", "Recipes  |  Drag to Pan  |  Scroll to Zoom", 18f, FontStyles.Bold,
            new Vector2(22f, -56f), new Vector2(-300f, -12f), TextAlignmentOptions.Left);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.enableAutoSizing = true;
        title.fontSizeMin = 12f;
        title.fontSizeMax = 18f;
        title.overflowMode = TextOverflowModes.Overflow;
        TextMeshProUGUI closeLabel;
        Button close = CreateAbsoluteButton(panel.transform, "Close", "X", new Vector2(-58f, -54f),
            new Vector2(-14f, -14f), GameUITheme.Danger, out closeLabel);
        close.onClick.AddListener(() => { CloseRecipes(); Sfx.Play(SfxId.UiClick); });

        TextMeshProUGUI fitLabel;
        Button fit = CreateAbsoluteButton(panel.transform, "FitGraph", "FIT", new Vector2(-270f, -54f),
            new Vector2(-210f, -14f), GameUITheme.Surface, out fitLabel);
        fit.onClick.AddListener(() => { FitRecipeChartToViewport((RectTransform)recipeChartRoot); Sfx.Play(SfxId.UiClick); });
        recipeZoomLabel = CreateAbsoluteText(panel.transform, "Zoom", "100%", 13f, FontStyles.Bold,
            new Vector2(-202f, -50f), new Vector2(-72f, -18f), TextAlignmentOptions.Center);

        var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabs.transform.SetParent(panel.transform, false);
        RectTransform tabsRt = (RectTransform)tabs.transform;
        tabsRt.anchorMin = new Vector2(0f, 1f);
        tabsRt.anchorMax = new Vector2(1f, 1f);
        tabsRt.pivot = new Vector2(0.5f, 1f);
        tabsRt.offsetMin = new Vector2(20f, -104f);
        tabsRt.offsetMax = new Vector2(-20f, -62f);
        var tabsLayout = tabs.GetComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 6f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;

        string[] names = { "BURGER", "FRIES", "SHAKE" };
        recipeTabButtons.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            int captured = i;
            TextMeshProUGUI tabLabel;
            float tabWidth = Mathf.Min(150f, (panelWidth - 52f) / names.Length);
            Button tab = CreateCartButton(tabs.transform, names[i], names[i], tabWidth,
                GameUITheme.Surface, out tabLabel);
            tabLabel.fontSize = 14f;
            tab.onClick.AddListener(() => { ShowRecipeTab(menu, captured); Sfx.Play(SfxId.UiClick); });
            recipeTabButtons.Add(tab);
        }

        var viewport = new GameObject("ChartViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRt = (RectTransform)viewport.transform;
        recipeChartViewport = viewportRt;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(20f, 20f);
        viewportRt.offsetMax = new Vector2(-286f, -114f);
        viewport.GetComponent<Image>().color = GameUITheme.Charcoal;
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        AddGraphGrid(viewport.transform);

        var details = new GameObject("RecipeDetails", typeof(RectTransform), typeof(Image));
        details.transform.SetParent(panel.transform, false);
        RectTransform detailsRt = (RectTransform)details.transform;
        detailsRt.anchorMin = new Vector2(1f, 0f);
        detailsRt.anchorMax = new Vector2(1f, 1f);
        detailsRt.pivot = new Vector2(1f, 0.5f);
        detailsRt.offsetMin = new Vector2(-274f, 20f);
        detailsRt.offsetMax = new Vector2(-20f, -114f);
        details.GetComponent<Image>().color = GameUITheme.Surface;
        recipeDetailsText = CreateAbsoluteText(details.transform, "DetailsText",
            "SELECT A RECIPE\n\nClick a card to inspect its ingredients, station, unlock, and value.",
            14f, FontStyles.Normal, new Vector2(16f, 16f), new Vector2(-16f, -16f),
            TextAlignmentOptions.TopLeft);
        RectTransform detailsTextRt = recipeDetailsText.rectTransform;
        detailsTextRt.anchorMin = Vector2.zero;
        detailsTextRt.anchorMax = Vector2.one;
        detailsTextRt.offsetMin = new Vector2(16f, 16f);
        detailsTextRt.offsetMax = new Vector2(-16f, -16f);
        recipeDetailsText.textWrappingMode = TextWrappingModes.Normal;

        var chart = new GameObject("RecipeChart", typeof(RectTransform));
        chart.transform.SetParent(viewport.transform, false);
        RectTransform chartRt = (RectTransform)chart.transform;
        chartRt.anchorMin = chartRt.anchorMax = new Vector2(0.5f, 0.5f);
        chartRt.pivot = new Vector2(0.5f, 0.5f);
        chartRt.sizeDelta = new Vector2(2000f, 2400f);
        chartRt.anchoredPosition = new Vector2(0f, -240f);
        recipeChartRoot = chart.transform;
        recipeGraphDrag = viewport.AddComponent<RecipeGraphDrag>();
        recipeGraphDrag.content = chartRt;
        recipeGraphDrag.zoomLabel = recipeZoomLabel;
        GameUITheme.ApplyTo(recipesOverlay.transform);
    }

    void ShowRecipeTab(CustomerOrderConfig menu, int tab)
    {
        if (recipeChartRoot == null) return;
        recipeChartMenu = menu;
        recipeGraphNodes.Clear();
        recipeGraphEdges.Clear();
        if (recipeDetailsText != null)
            recipeDetailsText.text = "SELECT A RECIPE\n\nClick a card to inspect its ingredients, station, unlock, and value.";
        RectTransform chart = (RectTransform)recipeChartRoot;
        chart.anchoredPosition = Vector2.zero;
        chart.localScale = Vector3.one;
        for (int i = recipeChartRoot.childCount - 1; i >= 0; i--)
        {
            GameObject oldNode = recipeChartRoot.GetChild(i).gameObject;
            oldNode.SetActive(false);
            Destroy(oldNode);
        }
        for (int i = 0; i < recipeTabButtons.Count; i++)
            GameUITheme.ApplyTabButton(recipeTabButtons[i], i == tab);

        if (tab == 0) BuildBurgerTree(menu);
        else if (tab == 1) BuildFriesTree(menu);
        else BuildShakeTree(menu);
        GameUITheme.ApplyTo(recipeChartRoot);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)recipeChartRoot);
        FitRecipeChartToViewport(chart);
    }

    void FitRecipeChartToViewport(RectTransform chart)
    {
        if (chart == null || recipeChartViewport == null) return;

        Canvas.ForceUpdateCanvases();
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < chart.childCount; i++)
        {
            RectTransform node = chart.GetChild(i) as RectTransform;
            if (node == null || !node.gameObject.activeSelf || !node.name.StartsWith("GraphNode_")) continue;
            Vector2 position = node.anchoredPosition;
            Vector2 size = node.rect.size;
            minX = Mathf.Min(minX, position.x - size.x * 0.5f);
            maxX = Mathf.Max(maxX, position.x + size.x * 0.5f);
            minY = Mathf.Min(minY, position.y - size.y * 0.5f);
            maxY = Mathf.Max(maxY, position.y + size.y * 0.5f);
        }
        if (float.IsInfinity(minX)) return;

        Vector2 graphSize = new Vector2(maxX - minX, maxY - minY);
        Vector2 graphCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        Rect viewport = recipeChartViewport.rect;
        float availableWidth = Mathf.Max(1f, viewport.width - 32f);
        float availableHeight = Mathf.Max(1f, viewport.height - 32f);
        float fitWidth = availableWidth / Mathf.Max(1f, graphSize.x);
        float fitHeight = availableHeight / Mathf.Max(1f, graphSize.y);
        float zoom = fitHeight >= 0.6f ? Mathf.Min(fitWidth, fitHeight) : fitWidth;
        zoom = Mathf.Clamp(zoom, 0.45f, 1f);

        chart.localScale = Vector3.one * zoom;
        float chartY = graphSize.y * zoom <= availableHeight
            ? -graphCenter.y * zoom
            : recipeChartViewport.rect.height * 0.5f - 16f - maxY * zoom;
        chart.anchoredPosition = new Vector2(-graphCenter.x * zoom, chartY);
        recipeGraphDrag?.RefreshZoomLabel();
    }

    void AddGraphGrid(Transform parent)
    {
        for (int x = 24; x < 900; x += 32)
        {
            var line = new GameObject("GridVertical", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(x, 0f); rt.offsetMax = new Vector2(x + 1f, 0f);
            line.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.025f);
            line.GetComponent<Image>().raycastTarget = false;
        }
        for (int y = 24; y < 720; y += 32)
        {
            var line = new GameObject("GridHorizontal", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)line.transform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(0f, y); rt.offsetMax = new Vector2(0f, y + 1f);
            line.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.025f);
            line.GetComponent<Image>().raycastTarget = false;
        }
    }

    void AddStageGuide(float y, string title)
    {
        var band = new GameObject("StageBand_" + title, typeof(RectTransform), typeof(Image));
        band.transform.SetParent(recipeChartRoot, false);
        RectTransform rt = (RectTransform)band.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1040f, 132f);
        rt.anchoredPosition = new Vector2(0f, y);
        band.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.022f);
        band.GetComponent<Image>().raycastTarget = false;
        TextMeshProUGUI label = CreateAbsoluteText(band.transform, "StageLabel", title, 11f,
            FontStyles.Bold, new Vector2(10f, -24f), new Vector2(-10f, -5f), TextAlignmentOptions.TopLeft);
        label.color = GameUITheme.TextSecondary;
        rt.SetAsFirstSibling();
    }

    void BuildBurgerTree(CustomerOrderConfig menu)
    {
        AddStageGuide(560f, "RAW INGREDIENTS");
        AddStageGuide(390f, "PROCESSED INGREDIENTS");
        AddStageGuide(-500f, "INTERMEDIATE ASSEMBLY");
        AddStageGuide(-900f, "FINAL PRODUCT");
        var rawPatty = AddGraphNode(menu.rawPattyIngredient, "RAW PATTY", "FREEZER", new Vector2(-330f, 560f));
        var cookedPatty = AddGraphNode(menu.cookedPattyIngredient, "COOKED PATTY", "GRILL", new Vector2(-330f, 390f));
        ConnectGraphNodes(rawPatty, cookedPatty, "COOK");

        var bun = AddGraphNode(FindRecipeInput(menu, menu.burgerBase, false), "BUN", "PANTRY", new Vector2(-120f, 390f));
        var burger = AddGraphNode(menu.burgerBase, "HAMBURGER", "ASSEMBLY", new Vector2(-330f, 190f), true);
        ConnectGraphNodes(cookedPatty, burger, null, -28f);
        ConnectGraphNodes(bun, burger, "ASSEMBLE", 28f);

        AssemblyRecipeDefinition cheeseRecipe = FindRecipeFeeding(menu, menu.cheeseburgerItem);
        ItemDefinition rawCheese = cheeseRecipe != null ? cheeseRecipe.rawPantryInput : menu.cheeseIngredient;
        ItemDefinition slicedCheese = cheeseRecipe != null ? cheeseRecipe.pantryInput : menu.slicedCheeseIngredient;
        var rawCheeseNode = AddGraphNode(rawCheese, "RAW CHEESE", "FREEZER", new Vector2(60f, 560f));
        var slicedCheeseNode = AddGraphNode(slicedCheese, "SLICED CHEESE", "CUTTING", new Vector2(60f, 390f));
        ConnectGraphNodes(rawCheeseNode, slicedCheeseNode, "SLICE");
        var cheesePatty = AddGraphNode(cheeseRecipe != null ? cheeseRecipe.output : null,
            "CHEESE PATTY", "ASSEMBLY", new Vector2(-60f, 190f), true);
        ConnectGraphNodes(cookedPatty, cheesePatty, null, -28f);
        ConnectGraphNodes(slicedCheeseNode, cheesePatty, "ASSEMBLE", 28f);

        var cheeseburger = AddGraphNode(menu.cheeseburgerItem, "CHEESEBURGER", "ASSEMBLY",
            new Vector2(-60f, -20f), true);
        ConnectGraphNodes(cheesePatty, cheeseburger, null, -28f);
        ConnectGraphNodes(bun, cheeseburger, "ASSEMBLE", 28f);

        var baconSlab = AddGraphNode(menu.baconSlabIngredient, "BACON SLAB", "FREEZER", new Vector2(330f, 190f));
        var cutBacon = AddGraphNode(menu.cutBaconIngredient, "CUT BACON", "CUTTING", new Vector2(330f, 20f));
        var cookedBacon = AddGraphNode(menu.cookedBaconIngredient, "COOKED BACON", "GRILL", new Vector2(330f, -150f));
        var cb = AddGraphNode(menu.cheeseBaconBurgerItem, "BACON CHEESEBURGER", "ASSEMBLY", new Vector2(190f, -690f), true);
        ConnectGraphNodes(baconSlab, cutBacon, "CUT"); ConnectGraphNodes(cutBacon, cookedBacon, "COOK");
        ConnectGraphNodes(cheesePatty, cb, null, -46f);
        ConnectGraphNodes(cookedBacon, cb, null, 0f);
        ConnectGraphNodes(bun, cb, "ASSEMBLE", 46f);

        var lettuce = AddGraphNode(menu.lettuceIngredient, "LETTUCE", "FREEZER", new Vector2(-430f, -150f));
        var slicedLettuce = AddGraphNode(menu.slicedLettuceIngredient, "LETTUCE SLICE", "CUTTING", new Vector2(-430f, -320f));
        var tomato = AddGraphNode(menu.tomatoIngredient, "TOMATO", "FREEZER", new Vector2(-210f, -150f));
        var slicedTomato = AddGraphNode(menu.slicedTomatoIngredient, "TOMATO SLICE", "CUTTING", new Vector2(-210f, -320f));
        var veggieMix = AddGraphNode(menu.veggieMixIngredient, "VEGGIE MIX", "ASSEMBLY", new Vector2(-320f, -500f), true);
        var classic = AddGraphNode(menu.clBurgerItem, "CLASSIC BURGER", "ASSEMBLY", new Vector2(-190f, -690f), true);
        ConnectGraphNodes(lettuce, slicedLettuce, "SLICE");
        ConnectGraphNodes(tomato, slicedTomato, "SLICE");
        ConnectGraphNodes(slicedLettuce, veggieMix, null, -28f);
        ConnectGraphNodes(slicedTomato, veggieMix, "ASSEMBLE", 28f);
        ConnectGraphNodes(cookedPatty, classic, null, -46f);
        ConnectGraphNodes(veggieMix, classic, null, 0f);
        ConnectGraphNodes(bun, classic, "ASSEMBLE", 46f);

        // Repeat this shared ingredient near the final tier. A local reference is
        // clearer than a connector crossing the entire graph from the first grill.
        var extraPatty = AddGraphNode(menu.cookedPattyIngredient, "EXTRA COOKED PATTY", "GRILL",
            new Vector2(0f, -690f));
        var superBurger = AddGraphNode(menu.superBurgerItem, "SUPER BURGER", "MK2 ASSEMBLY",
            new Vector2(0f, -900f), true);
        ConnectGraphNodes(classic, superBurger, null, -46f);
        ConnectGraphNodes(extraPatty, superBurger, "ASSEMBLE", 0f);
        ConnectGraphNodes(cb, superBurger, null, 46f);
    }

    void BuildFriesTree(CustomerOrderConfig menu)
    {
        AddStageGuide(420f, "RAW INGREDIENTS");
        AddStageGuide(105f, "PROCESSING");
        AddStageGuide(-240f, "FINAL PRODUCT");
        var potatoes = AddGraphNode(menu.friesIngredient, "POTATOES", "PANTRY", new Vector2(-260f, 420f));
        var slices = AddGraphNode(menu.slicedPotatoIngredient, "POTATO SLICES", "CUTTING", new Vector2(-260f, 210f));
        var cooked = AddGraphNode(menu.cookedPotatoIngredient, "COOKED POTATO SLICES", "FRYER", new Vector2(-260f, 0f));
        var container = AddGraphNode(menu.fryContainerIngredient, "FRY CONTAINER", "PANTRY", new Vector2(180f, 0f));
        var fries = AddGraphNode(menu.friesItem, "FRIES", "ASSEMBLY", new Vector2(-40f, -240f), true);
        ConnectGraphNodes(potatoes, slices, "SLICE");
        ConnectGraphNodes(slices, cooked, "COOK");
        ConnectGraphNodes(cooked, fries);
        ConnectGraphNodes(container, fries, "ASSEMBLE");
    }

    void BuildShakeTree(CustomerOrderConfig menu)
    {
        AddStageGuide(260f, "INGREDIENTS");
        AddStageGuide(-20f, "FINAL PRODUCT");
        AssemblyRecipeDefinition recipe = menu.GetAssemblyRecipe(menu.drinkItem);
        var milk = AddGraphNode(recipe != null ? recipe.processedInput : null, "MILK", "FREEZER", new Vector2(-230f, 260f));
        var cup = AddGraphNode(recipe != null ? recipe.pantryInput : null, "EMPTY CUP", "PANTRY", new Vector2(230f, 260f));
        var shake = AddGraphNode(menu.drinkItem, "SHAKE", "SHAKE STATION", new Vector2(0f, -20f), true);
        ConnectGraphNodes(milk, shake);
        ConnectGraphNodes(cup, shake, "COMBINE");
    }

    ItemDefinition FindRecipeInput(CustomerOrderConfig menu, ItemDefinition output, bool processed)
    {
        AssemblyRecipeDefinition recipe = menu != null ? menu.GetAssemblyRecipe(output) : null;
        if (recipe == null) return null;
        return processed ? recipe.processedInput : recipe.pantryInput;
    }

    AssemblyRecipeDefinition FindRecipeFeeding(CustomerOrderConfig menu, ItemDefinition output)
    {
        AssemblyRecipeDefinition final = menu != null ? menu.GetAssemblyRecipe(output) : null;
        return final != null && final.processedInput != null
            ? menu.GetAssemblyRecipe(final.processedInput) : null;
    }

    void AddBurgerToppingBranch(CustomerOrderConfig menu, ItemDefinition output, RectTransform baseProduct,
        ItemDefinition raw, ItemDefinition sliced, string rawFallback, string slicedFallback,
        Vector2 rawPosition, Vector2 slicedPosition, Vector2 outputPosition, out RectTransform result)
    {
        var rawNode = AddGraphNode(raw, rawFallback, "FREEZER", rawPosition);
        var slicedNode = AddGraphNode(sliced, slicedFallback, "CUTTING", slicedPosition);
        result = AddGraphNode(output, output != null ? output.itemName : "BURGER", "ASSEMBLY", outputPosition, true);
        ConnectGraphNodes(rawNode, slicedNode, "SLICE");
        ConnectGraphNodes(baseProduct, result);
        ConnectGraphNodes(slicedNode, result, "ASSEMBLE");
    }

    RectTransform AddGraphNode(ItemDefinition item, string fallback, string station, Vector2 position, bool final = false)
    {
        int unlockMilestone = item != null && recipeChartMenu != null
            ? Mathf.Max(recipeChartMenu.GetMenuItemUnlockMilestone(item),
                recipeChartMenu.GetIngredientUnlockMilestone(item)) : 0;
        bool locked = item != null && recipeChartMenu != null
            && (!recipeChartMenu.IsMenuItemUnlocked(item) || !recipeChartMenu.IsIngredientUnlocked(item));
        var node = new GameObject("GraphNode_" + fallback, typeof(RectTransform), typeof(Image));
        node.transform.SetParent(recipeChartRoot, false);
        RectTransform rt = (RectTransform)node.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(180f, 92f);
        rt.anchoredPosition = position;
        Image nodeImage = node.GetComponent<Image>();
        bool assemblyOutput = item != null && recipeChartMenu != null
            && recipeChartMenu.GetAssemblyRecipe(item) != null;
        Color nodeColor = station == "FREEZER" || station == "PANTRY"
            ? new Color(0.16f, 0.19f, 0.23f, 1f)
            : assemblyOutput ? new Color(0.20f, 0.38f, 0.34f, 1f)
            : new Color(0.18f, 0.27f, 0.33f, 1f);
        nodeImage.color = locked ? GameUITheme.Backdrop
            : final ? GameUITheme.Positive : nodeColor;

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        previewGo.transform.SetParent(node.transform, false);
        RectTransform previewRt = (RectTransform)previewGo.transform;
        previewRt.anchorMin = previewRt.anchorMax = new Vector2(0f, 0.5f);
        previewRt.pivot = new Vector2(0f, 0.5f);
        previewRt.anchoredPosition = new Vector2(8f, 0f);
        previewRt.sizeDelta = new Vector2(64f, 64f);
        RawImage preview = previewGo.GetComponent<RawImage>();
        if (item != null && !locked)
            preview.texture = ItemPreviewThumbnails.GetPrefab(item.prefab, item.itemName);
        preview.color = item == null || locked ? Color.clear : Color.white;
        preview.raycastTarget = false;
        if (locked)
        {
            RectTransform lockRect = CreateLockIcon(node.transform, "LockedIndicator", GameUITheme.Accent);
            LayoutElement lockLayout = lockRect.GetComponent<LayoutElement>();
            if (lockLayout != null) lockLayout.ignoreLayout = true;
            lockRect.anchorMin = lockRect.anchorMax = new Vector2(0f, 0.5f);
            lockRect.anchoredPosition = new Vector2(44f, 0f);
        }

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(node.transform, false);
        RectTransform labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(76f, 6f);
        labelRt.offsetMax = new Vector2(-6f, -6f);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        string display = locked ? "Locked Item"
            : item != null && !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : FormatGraphLabel(fallback);
        label.text = "<b>" + display + "</b>\n<color=#B7BEC5>" + FormatGraphLabel(station) + "</color>"
            + (locked ? "\n<color=#D8B365>Unlocks at Milestone " + unlockMilestone + "</color>" : "");
        label.fontSize = 12f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8.5f;
        label.fontSizeMax = 12f;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = GameUITheme.TextPrimary;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        RecipeGraphNodeView view = node.AddComponent<RecipeGraphNodeView>();
        view.owner = this;
        view.item = item;
        view.cardImage = nodeImage;
        view.baseColor = nodeImage.color;
        view.group = node.AddComponent<CanvasGroup>();
        recipeGraphNodes.Add(view);
        return rt;
    }

    static string FormatGraphLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string[] words = value.ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        }
        return string.Join(" ", words);
    }

    void ConnectGraphNodes(RectTransform from, RectTransform to, string action = null,
        float targetPortOffset = 0f)
    {
        if (from == null || to == null) return;
        Vector2 start = from.anchoredPosition + new Vector2(0f, -from.sizeDelta.y * 0.5f);
        Vector2 end = to.anchoredPosition + new Vector2(targetPortOffset, to.sizeDelta.y * 0.5f);
        float middleY = (start.y + end.y) * 0.5f;
        CreateGraphLine(new Vector2(start.x, start.y), new Vector2(start.x, middleY), from, to);
        CreateGraphLine(new Vector2(start.x, middleY), new Vector2(end.x, middleY), from, to);
        CreateGraphLine(new Vector2(end.x, middleY), new Vector2(end.x, end.y), from, to);
        CreateGraphArrow(end, from, to);
        if (!string.IsNullOrWhiteSpace(action))
        {
            var labelGo = new GameObject("Edge_" + action, typeof(RectTransform), typeof(Image));
            labelGo.transform.SetParent(recipeChartRoot, false);
            RectTransform rt = (RectTransform)labelGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(72f, 18f);
            rt.anchoredPosition = new Vector2((start.x + end.x) * 0.5f, middleY);
            Image backing = labelGo.GetComponent<Image>();
            backing.color = GameUITheme.Charcoal;
            backing.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(labelGo.transform, false);
            RectTransform textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 0f);
            textRect.offsetMax = new Vector2(-2f, 0f);
            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = FormatGraphLabel(action);
            text.fontSize = 10f;
            text.fontStyle = FontStyles.Bold;
            text.color = GameUITheme.PositiveAccent;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            RecipeGraphEdgeView edge = labelGo.AddComponent<RecipeGraphEdgeView>();
            edge.from = from;
            edge.to = to;
            edge.visual = labelGo.AddComponent<CanvasGroup>();
            recipeGraphEdges.Add(edge);
            rt.SetAsLastSibling();
        }
    }

    void CreateGraphLine(Vector2 a, Vector2 b, RectTransform from, RectTransform to)
    {
        Vector2 delta = b - a;
        var line = new GameObject("Connector", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(recipeChartRoot, false);
        RectTransform rt = (RectTransform)line.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(2f, delta.magnitude), 2f);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.GetComponent<Image>().color = GameUITheme.PositiveAccent;
        line.GetComponent<Image>().raycastTarget = false;
        RecipeGraphEdgeView edge = line.AddComponent<RecipeGraphEdgeView>();
        edge.from = from;
        edge.to = to;
        edge.visual = line.AddComponent<CanvasGroup>();
        recipeGraphEdges.Add(edge);
        rt.SetAsFirstSibling();
    }

    void CreateGraphArrow(Vector2 end, RectTransform from, RectTransform to)
    {
        var arrow = new GameObject("ConnectorArrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrow.transform.SetParent(recipeChartRoot, false);
        RectTransform rt = (RectTransform)arrow.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(18f, 18f);
        rt.anchoredPosition = end + new Vector2(0f, 7f);
        TextMeshProUGUI text = arrow.GetComponent<TextMeshProUGUI>();
        text.text = "▼";
        text.fontSize = 13f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = GameUITheme.PositiveAccent;
        text.raycastTarget = false;
        RecipeGraphEdgeView edge = arrow.AddComponent<RecipeGraphEdgeView>();
        edge.from = from;
        edge.to = to;
        edge.visual = arrow.AddComponent<CanvasGroup>();
        recipeGraphEdges.Add(edge);
        rt.SetAsFirstSibling();
    }

    string RecipeInputs(AssemblyRecipeDefinition recipe)
    {
        if (recipe == null) return "Assembly recipe";
        string a = recipe.processedInput != null ? recipe.processedInput.itemName : recipe.processedInputName;
        string b = recipe.pantryInput != null ? recipe.pantryInput.itemName : "Ingredient";
        string result = a + " + " + b;
        if (recipe.thirdInput != null)
            result += " + " + recipe.thirdInput.itemName;
        return result;
    }

    void AddTreeBranch(ItemDefinition left, string leftFallback, ItemDefinition right, string rightFallback,
        string leftSubtitle, string rightSubtitle)
    {
        var row = new GameObject("IngredientBranch", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(recipeChartRoot, false);
        row.GetComponent<LayoutElement>().preferredHeight = 94f;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        AddTreeNode(left, leftFallback, leftSubtitle, false, row.transform, 235f);
        AddTreeNode(right, rightFallback, rightSubtitle, false, row.transform, 235f);
    }

    void AddTreeNode(ItemDefinition item, string fallback, string subtitle, bool final,
        Transform parentOverride = null, float width = 500f)
    {
        int unlockMilestone = item != null && recipeChartMenu != null
            ? Mathf.Max(recipeChartMenu.GetMenuItemUnlockMilestone(item),
                recipeChartMenu.GetIngredientUnlockMilestone(item)) : 0;
        bool locked = item != null && recipeChartMenu != null
            && (!recipeChartMenu.IsMenuItemUnlocked(item) || !recipeChartMenu.IsIngredientUnlocked(item));
        var node = new GameObject("Node_" + fallback, typeof(RectTransform), typeof(LayoutElement),
            typeof(Image), typeof(HorizontalLayoutGroup));
        node.transform.SetParent(parentOverride != null ? parentOverride : recipeChartRoot, false);
        var le = node.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.preferredHeight = 116f;
        node.GetComponent<Image>().color = locked ? GameUITheme.Backdrop
            : final ? GameUITheme.Positive : GameUITheme.Panel;
        var layout = node.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 14, 8, 8);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(LayoutElement), typeof(RawImage));
        previewGo.transform.SetParent(node.transform, false);
        previewGo.GetComponent<LayoutElement>().preferredWidth = 70f;
        previewGo.GetComponent<LayoutElement>().preferredHeight = 70f;
        RawImage preview = previewGo.GetComponent<RawImage>();
        if (item != null && !locked)
            preview.texture = ItemPreviewThumbnails.GetPrefab(item.prefab, item.itemName);
        preview.color = item != null && !locked ? Color.white : Color.clear;
        preview.raycastTarget = false;
        if (locked)
        {
            RectTransform lockRect = CreateLockIcon(node.transform, "LockedIndicator", GameUITheme.Accent);
            LayoutElement lockLayout = lockRect.GetComponent<LayoutElement>();
            if (lockLayout != null) lockLayout.ignoreLayout = true;
            lockRect.anchorMin = lockRect.anchorMax = new Vector2(0f, 0.5f);
            lockRect.anchoredPosition = new Vector2(48f, 0f);
        }

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(node.transform, false);
        textGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = "<b>" + (locked ? "Locked Item"
                : item != null && !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : fallback)
            + "</b>\n<color=#B7BEC5>" + FormatGraphLabel(subtitle) + "</color>"
            + (locked ? "\n<color=#D8B365>Unlocks at Milestone " + unlockMilestone + "</color>" : "");
        text.fontSize = 14f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10.5f;
        text.fontSizeMax = 14f;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.Center;
        text.color = GameUITheme.TextPrimary;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
    }

    void AddTreeArrow(string action = null)
    {
        var go = new GameObject("Arrow", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        go.transform.SetParent(recipeChartRoot, false);
        go.GetComponent<LayoutElement>().preferredHeight = string.IsNullOrWhiteSpace(action) ? 30f : 42f;
        TextMeshProUGUI arrow = go.GetComponent<TextMeshProUGUI>();
        arrow.text = string.IsNullOrWhiteSpace(action) ? "▼" : FormatGraphLabel(action) + "\n▼";
        arrow.fontSize = string.IsNullOrWhiteSpace(action) ? 20f : 14f;
        arrow.fontStyle = FontStyles.Bold;
        arrow.color = GameUITheme.Accent;
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.raycastTarget = false;
    }

    static TextMeshProUGUI CreateAbsoluteText(Transform parent, string name, string value, float size,
        FontStyles style, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    static Button CreateAbsoluteButton(Transform parent, string name, string value, Vector2 offsetMin,
        Vector2 offsetMax, Color color, out TextMeshProUGUI label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        go.GetComponent<Image>().color = color;
        label = CreateAbsoluteText(go.transform, "Text", value, 17f, FontStyles.Bold,
            Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        return go.GetComponent<Button>();
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
        tmp.color = GameUITheme.Accent;
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void CreateMenuToggleRow(CustomerOrderConfig menu, ItemDefinition item)
    {
        const float collapsedHeight = 62f;
        const float expandedHeight = 190f;

        var row = new GameObject("Sell_" + item.name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        row.transform.SetParent(listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = collapsedHeight;
        le.preferredHeight = collapsedHeight;
        row.GetComponent<Image>().color = GameUITheme.Surface;

        var rowLayout = row.GetComponent<VerticalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 8, 5, 5);
        rowLayout.spacing = 4f;
        rowLayout.childAlignment = TextAnchor.UpperCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var header = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.transform.SetParent(row.transform, false);
        var headerImage = header.GetComponent<Image>();
        headerImage.color = Color.clear;
        var headerLe = header.GetComponent<LayoutElement>();
        headerLe.minHeight = 52f;
        headerLe.preferredHeight = 52f;
        var layout = header.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(2, 4, 1, 1);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        int unlockMilestone = menu.GetMenuItemUnlockMilestone(item);
        bool itemUnlocked = menu.IsMenuItemUnlocked(item);

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        previewGo.transform.SetParent(header.transform, false);
        var previewLe = previewGo.GetComponent<LayoutElement>();
        previewLe.minWidth = 44;
        previewLe.preferredWidth = 44;
        previewLe.minHeight = 44;
        previewLe.preferredHeight = 44;
        var preview = previewGo.GetComponent<RawImage>();
        preview.texture = ItemPreviewThumbnails.GetPrefab(GetPreviewPrefab(menu, item), item.itemName);
        preview.color = itemUnlocked ? Color.white : Color.clear;
        preview.raycastTarget = false;
        RectTransform lockRect = CreateLockIcon(previewGo.transform, "LockedIndicator", GameUITheme.Accent);
        lockRect.anchorMin = lockRect.anchorMax = new Vector2(0.5f, 0.5f);
        lockRect.anchoredPosition = Vector2.zero;
        CanvasGroup lockVisual = lockRect.GetComponent<CanvasGroup>();
        lockVisual.alpha = itemUnlocked ? 0f : 1f;

        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        nameGo.transform.SetParent(header.transform, false);
        var nameLe = nameGo.GetComponent<LayoutElement>();
        nameLe.flexibleWidth = 1f;
        nameLe.minHeight = 44f;
        nameLe.preferredHeight = 44f;
        var nameText = nameGo.GetComponent<TextMeshProUGUI>();
        nameText.text = itemUnlocked ? inventory.GetDisplayName(item)
            : inventory.GetDisplayName(item) + "\n<color=#D8B365>Unlocks at Milestone " + unlockMilestone + "</color>";
        nameText.fontSize = 14;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = itemUnlocked ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
        row.GetComponent<Image>().color = itemUnlocked
            ? GameUITheme.Surface
            : GameUITheme.Backdrop;
        preview.color = itemUnlocked ? Color.white : Color.clear;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.textWrappingMode = TextWrappingModes.Normal;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) nameText.font = TMP_Settings.defaultFontAsset;

        ProductionManager production = ProductionManager.Instance != null
            ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();
        ItemDefinition captured = item;
        Button targetMinus = CreateCartButton(header.transform, "TargetMinus", "-", 28f,
            GameUITheme.Danger, out _);
        var targetGo = new GameObject("ProductionTarget", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        targetGo.transform.SetParent(header.transform, false);
        var targetLe = targetGo.GetComponent<LayoutElement>();
        targetLe.minWidth = 72f;
        targetLe.preferredWidth = 72f;
        var targetLabel = targetGo.GetComponent<TextMeshProUGUI>();
        targetLabel.fontSize = 13f;
        targetLabel.enableAutoSizing = true;
        targetLabel.fontSizeMin = 10f;
        targetLabel.fontSizeMax = 13f;
        targetLabel.fontStyle = FontStyles.Bold;
        targetLabel.color = GameUITheme.TextSecondary;
        targetLabel.alignment = TextAlignmentOptions.Center;
        targetLabel.textWrappingMode = TextWrappingModes.NoWrap;
        targetLabel.overflowMode = TextOverflowModes.Overflow;
        targetLabel.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) targetLabel.font = TMP_Settings.defaultFontAsset;
        System.Action refreshTarget = () => targetLabel.text = "Target " +
            (production != null ? production.GetProductionTarget(captured) : 0);
        Button targetPlus = CreateCartButton(header.transform, "TargetPlus", "+", 28f,
            GameUITheme.Positive, out _);
        targetMinus.interactable = itemUnlocked;
        targetPlus.interactable = itemUnlocked;
        targetMinus.onClick.AddListener(() =>
        {
            if (production != null) production.SetProductionTarget(captured,
                production.GetProductionTarget(captured) - 1);
            refreshTarget();
        });
        targetPlus.onClick.AddListener(() =>
        {
            if (production != null) production.SetProductionTarget(captured,
                production.GetProductionTarget(captured) + 1);
            refreshTarget();
        });
        refreshTarget();

        var chevronGo = new GameObject("Chevron", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        chevronGo.transform.SetParent(header.transform, false);
        var chevronLe = chevronGo.GetComponent<LayoutElement>();
        chevronLe.minWidth = 24f;
        chevronLe.preferredWidth = 24f;
        var chevron = chevronGo.GetComponent<TextMeshProUGUI>();
        chevron.text = ">";
        chevron.fontSize = 18f;
        chevron.fontStyle = FontStyles.Bold;
        chevron.color = GameUITheme.Accent;
        chevron.alignment = TextAlignmentOptions.Center;
        chevron.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) chevron.font = TMP_Settings.defaultFontAsset;

        Toggle toggle = CreateCheckbox(header.transform);
        toggle.SetIsOnWithoutNotify(menu.IsItemEnabled(item));
        toggle.interactable = itemUnlocked;
        toggle.onValueChanged.AddListener(enabled => menu.SetItemEnabled(captured, enabled));

        var workflowGo = new GameObject("WorkflowDropdown", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        workflowGo.transform.SetParent(row.transform, false);
        workflowGo.GetComponent<Image>().color = GameUITheme.Charcoal;
        workflowGo.GetComponent<Image>().raycastTarget = false;
        var workflowLe = workflowGo.GetComponent<LayoutElement>();
        workflowLe.minHeight = 116f;
        workflowLe.preferredHeight = 116f;

        var workflowTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        workflowTextGo.transform.SetParent(workflowGo.transform, false);
        var workflowRt = (RectTransform)workflowTextGo.transform;
        workflowRt.anchorMin = Vector2.zero;
        workflowRt.anchorMax = Vector2.one;
        workflowRt.offsetMin = new Vector2(10f, 6f);
        workflowRt.offsetMax = new Vector2(-10f, -6f);
        var workflowText = workflowTextGo.GetComponent<TextMeshProUGUI>();
        workflowText.text = HasLockedIngredient() || !itemUnlocked
            ? "Recipe details unlock when the required ingredients are available."
            : GetWorkflowDescription(menu, item);
        workflowText.fontSize = 14f;
        workflowText.color = GameUITheme.TextSecondary;
        workflowText.alignment = TextAlignmentOptions.MidlineLeft;
        workflowText.textWrappingMode = TextWrappingModes.Normal;
        workflowText.overflowMode = TextOverflowModes.Ellipsis;
        workflowText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) workflowText.font = TMP_Settings.defaultFontAsset;
        workflowGo.SetActive(false);

        var headerButton = header.GetComponent<Button>();
        headerButton.targetGraphic = headerImage;
        headerButton.transition = Selectable.Transition.None;
        headerButton.onClick.AddListener(() => ToggleMenuWorkflow(row, workflowGo, le, chevron,
            collapsedHeight, expandedHeight));

        MilestoneLockedMenuRow rowState = row.AddComponent<MilestoneLockedMenuRow>();
        rowState.item = item;
        rowState.label = nameText;
        rowState.workflowText = workflowText;
        rowState.preview = preview;
        rowState.lockVisual = lockVisual;
        rowState.rowBackground = row.GetComponent<Image>();
        rowState.toggle = toggle;
        rowState.targetMinus = targetMinus;
        rowState.targetPlus = targetPlus;
    }

    void ToggleMenuWorkflow(GameObject row, GameObject workflow, LayoutElement rowLayout,
        TextMeshProUGUI chevron, float collapsedHeight, float expandedHeight)
    {
        bool opening = workflow != null && !workflow.activeSelf;

        if (expandedWorkflow != null)
            expandedWorkflow.SetActive(false);
        if (expandedMenuLayout != null)
        {
            expandedMenuLayout.minHeight = collapsedHeight;
            expandedMenuLayout.preferredHeight = collapsedHeight;
        }
        if (expandedChevron != null)
            expandedChevron.text = ">";

        expandedMenuRow = null;
        expandedWorkflow = null;
        expandedMenuLayout = null;
        expandedChevron = null;

        if (opening)
        {
            workflow.SetActive(true);
            rowLayout.minHeight = expandedHeight;
            rowLayout.preferredHeight = expandedHeight;
            chevron.text = "v";
            expandedMenuRow = row;
            expandedWorkflow = workflow;
            expandedMenuLayout = rowLayout;
            expandedChevron = chevron;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)listContainer);
        Sfx.Play(SfxId.UiClick);
    }

    static string GetWorkflowDescription(CustomerOrderConfig menu, ItemDefinition item)
    {
        if (menu == null || item == null) return "";

        const string label = "<color=#91A4C3>FLOW + MATERIAL</color>  ";
        const string arrow = "  <color=#7E8CA6>→</color>  ";
        string Step(string station, string material) =>
            "<b>" + station + "</b> <color=#7FEA9A>[" + material + "]</color>";

        if (menu.IsBurger(item))
        {
            string Name(ItemDefinition definition, string fallback) => definition != null
                && !string.IsNullOrWhiteSpace(definition.itemName) ? definition.itemName : fallback;
            var stages = menu.GetAssemblyChain(item);
            var flow = new List<string>
            {
                Step("Freezer", Name(menu.rawPattyIngredient, "Raw patty")),
                Step("Grill", Name(menu.cookedPattyIngredient, "Cooked patty"))
            };
            var supplies = new List<string>();
            for (int i = 0; i < stages.Count; i++)
            {
                AssemblyRecipeDefinition recipe = menu.GetAssemblyRecipe(stages[i]);
                if (recipe == null) continue;
                string processed = Name(recipe.processedInput, recipe.processedInputName);
                string pantry = Name(recipe.pantryInput, "Ingredient");
                string third = recipe.thirdInput != null
                    ? " + " + Name(recipe.thirdInput, "Ingredient") : "";
                flow.Add(Step("Assembly " + (i + 1), processed + " + " + pantry + third));
                if (menu.AssemblySupplyRequiresCutting(recipe))
                    supplies.Add(Step("Pantry", Name(recipe.rawPantryInput, "Raw ingredient"))
                        + arrow + Step("Cutting", pantry)
                        + arrow + Step("Assembly " + (i + 1), "Input 2"));
                else
                    supplies.Add(Step("Pantry", pantry)
                        + arrow + Step("Assembly " + (i + 1), "Input 2"));
                if (recipe.thirdInput != null)
                {
                    string thirdName = Name(recipe.thirdInput, "Ingredient");
                    if (menu.AssemblyThirdSupplyRequiresProcessing(recipe))
                        supplies.Add(Step("Ingredient source", Name(recipe.rawThirdInput, "Raw ingredient"))
                            + arrow + Step("Processing", thirdName)
                            + arrow + Step("Assembly " + (i + 1), "Input 3"));
                    else
                        supplies.Add(Step("Ingredient source", thirdName)
                            + arrow + Step("Assembly " + (i + 1), "Input 3"));
                }
            }
            flow.Add(Step("Pickup Station", Name(item, "Burger")));
            return label + string.Join(arrow, flow)
                + (supplies.Count > 0 ? "\n" + string.Join("   ", supplies) : "");
        }

        if (menu.IsFries(item))
            return label + Step("Pantry", "Potatoes") + arrow + Step("Cutting", "Potato slices")
                + arrow + Step("Fryer", "Potato slices")
                + arrow + Step("Pickup Station", "Fries");

        if (menu.IsDrink(item))
            return label + Step("Milk Freezer", "Milk") + arrow + Step("Cup Pantry", "Empty Cup")
                + arrow + Step("Shake Station", "Shake") + arrow + Step("Pickup Station", "Finished shake");

        return label + "No workflow configured";
    }

    GameObject GetPreviewPrefab(CustomerOrderConfig menu, ItemDefinition item)
    {
        if (menu == null || item == null) return null;
        if (item.prefab != null) return item.prefab;
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
        background.color = GameUITheme.Charcoal;

        var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        checkGo.transform.SetParent(backgroundGo.transform, false);
        var checkRect = (RectTransform)checkGo.transform;
        checkRect.anchorMin = new Vector2(0.18f, 0.18f);
        checkRect.anchorMax = new Vector2(0.82f, 0.82f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        var check = checkGo.GetComponent<Image>();
        check.color = GameUITheme.PositiveAccent;

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
        tmp.color = GameUITheme.Peach;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void CreateMilestoneHeader(int milestone)
    {
        string label = milestone > 0 ? "Milestone " + milestone : "Available Now";
        var go = new GameObject("IngredientGroup_Milestone" + milestone, typeof(RectTransform));
        go.transform.SetParent(listContainer, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.color = GameUITheme.Accent;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
    }

    void CreateMenuMilestoneHeader(int milestone)
    {
        string label = milestone > 0 ? "Milestone " + milestone : "Available Now";
        var go = new GameObject("MenuGroup_Milestone" + milestone, typeof(RectTransform));
        go.transform.SetParent(listContainer, false);
        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.color = GameUITheme.Accent;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
    }

    static RectTransform CreateLockIcon(Transform parent, string objectName, Color color)
    {
        var root = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(28f, 32f);
        var layout = root.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 28f;
        layout.minHeight = layout.preferredHeight = 32f;
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;
        root.GetComponent<CanvasGroup>().interactable = false;

        CreateLockPiece(root.transform, "ShackleTop", new Vector2(14f, 3f), new Vector2(0f, 9f), color);
        CreateLockPiece(root.transform, "ShackleLeft", new Vector2(3f, 9f), new Vector2(-5.5f, 4f), color);
        CreateLockPiece(root.transform, "ShackleRight", new Vector2(3f, 9f), new Vector2(5.5f, 4f), color);
        CreateLockPiece(root.transform, "LockBody", new Vector2(22f, 15f), new Vector2(0f, -5f), color);
        CreateLockPiece(root.transform, "Keyhole", new Vector2(3f, 7f), new Vector2(0f, -5f), GameUITheme.Charcoal);
        return rootRect;
    }

    static void CreateLockPiece(Transform parent, string objectName, Vector2 size, Vector2 position, Color color)
    {
        var piece = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        piece.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)piece.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = piece.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    bool HasLockedIngredient()
    {
        if (inventory == null) return false;
        foreach (ItemDefinition ingredient in inventory.GetIngredientCatalogItems())
            if (ingredient != null && !inventory.IsOrderItemUnlocked(ingredient)) return true;
        return false;
    }

    void CreateOrderRow(ItemDefinition item)
    {
        var row = new GameObject("Order_" + item.name, typeof(RectTransform));
        row.transform.SetParent(listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 68;
        le.preferredHeight = 68;
        var img = row.AddComponent<Image>();
        img.color = GameUITheme.Surface;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 10, 6, 6);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childForceExpandWidth = false;
        hlg.childControlHeight = true;

        var lockIcon = CreateLockIcon(row.transform, "LockIndicator", GameUITheme.Accent);
        var lockVisual = lockIcon.GetComponent<CanvasGroup>();
        if (lockVisual != null) lockVisual.alpha = 0f;

        var infoGo = new GameObject("Info", typeof(RectTransform));
        infoGo.transform.SetParent(row.transform, false);
        infoGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        var infoTmp = infoGo.AddComponent<TextMeshProUGUI>();
        infoTmp.fontSize = 14;
        infoTmp.color = GameUITheme.TextPrimary;
        infoTmp.alignment = TextAlignmentOptions.Left;
        if (TMP_Settings.defaultFontAsset != null) infoTmp.font = TMP_Settings.defaultFontAsset;

        var stockGo = new GameObject("Stock", typeof(RectTransform));
        stockGo.transform.SetParent(row.transform, false);
        stockGo.AddComponent<LayoutElement>().minWidth = 78;
        var stockTmp = stockGo.AddComponent<TextMeshProUGUI>();
        stockTmp.fontSize = 14;
        stockTmp.color = GameUITheme.TextSecondary;
        stockTmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) stockTmp.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI removeLabel;
        Button removeButton = CreateCartButton(row.transform, "Remove", "-", 36f, GameUITheme.Danger, out removeLabel);

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
        Button addButton = CreateCartButton(row.transform, "Add", "+", 44f, GameUITheme.Positive, out addLabel);

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
        binder.lockVisual = lockVisual;
    }

    static Button CreateCartButton(Transform parent, string objectName, string label, float width, Color color, out TextMeshProUGUI text)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = 42f;
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
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return go.GetComponent<Button>();
    }

    void CreateCartFooter()
    {
        var statusGo = new GameObject("DeliveryStatus", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        statusGo.transform.SetParent(transform, false);
        PinFooter((RectTransform)statusGo.transform, 112f, 42f);
        var statusLe = statusGo.GetComponent<LayoutElement>();
        statusLe.minHeight = 38f;
        statusLe.preferredHeight = 38f;
        statusGo.GetComponent<Image>().color = GameUITheme.Panel;
        var statusTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        statusTextGo.transform.SetParent(statusGo.transform, false);
        var statusTextRt = (RectTransform)statusTextGo.transform;
        statusTextRt.anchorMin = Vector2.zero;
        statusTextRt.anchorMax = Vector2.one;
        statusTextRt.offsetMin = new Vector2(6f, 0f);
        statusTextRt.offsetMax = new Vector2(-6f, 0f);
        deliveryStatusText = statusTextGo.GetComponent<TextMeshProUGUI>();
        deliveryStatusText.fontSize = 14f;
        deliveryStatusText.alignment = TextAlignmentOptions.Center;
        deliveryStatusText.color = GameUITheme.TextSecondary;
        if (TMP_Settings.defaultFontAsset != null) deliveryStatusText.font = TMP_Settings.defaultFontAsset;

        var footer = new GameObject("CartCheckout", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
        footer.transform.SetParent(transform, false);
        PinFooter((RectTransform)footer.transform, 56f, 58f);
        var footerLe = footer.GetComponent<LayoutElement>();
        footerLe.minHeight = 58f;
        footerLe.preferredHeight = 58f;
        footer.GetComponent<Image>().color = GameUITheme.Surface;
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
        cartSummaryText.fontSize = 14f;
        cartSummaryText.alignment = TextAlignmentOptions.Left;
        cartSummaryText.color = GameUITheme.TextPrimary;
        if (TMP_Settings.defaultFontAsset != null) cartSummaryText.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI addAllLabel;
        addAllButton = CreateCartButton(footer.transform, "AddAll", "Add All\n(+1 each)", 96, GameUITheme.Positive, out addAllLabel);
        addAllLabel.fontSize = 14;
        addAllButton.onClick.AddListener(() =>
        {
            if (inventory == null || (IngredientDeliveryService.Instance != null && IngredientDeliveryService.Instance.HasPending)) return;
            foreach (var item in inventory.GetOrderableItems())
                if (item != null) SetCartPacks(item, GetCartPacks(item) + 1);
            SelectSection(false);
            RefreshAll();
        });
        TextMeshProUGUI clearLabel;
        clearCartButton = CreateCartButton(footer.transform, "ClearCart", "Clear", 62f, GameUITheme.Danger, out clearLabel);
        clearLabel.fontSize = 14f;
        clearCartButton.onClick.AddListener(() =>
        {
            cartPacks.Clear();
            RefreshAll();
        });

        placeOrderButton = CreateCartButton(footer.transform, "PlaceOrder", "Place Order", 126f, GameUITheme.Positive, out placeOrderLabel);
        placeOrderLabel.fontSize = 14f;
        placeOrderButton.onClick.AddListener(SubmitCart);

        expressOrderButton = CreateCartButton(footer.transform, "ExpressOrder", "Express", 112f, GameUITheme.Accent, out expressOrderLabel);
        expressOrderLabel.fontSize = 14f;
        expressOrderButton.onClick.AddListener(SubmitExpressCart);
        clearCartButton.transform.SetAsLastSibling();
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
        if (showMenu) return;
        if (inventory == null || cartPacks.Count == 0) return;
        if (inventory.TryOrderCart(cartPacks))
        {
            cartPacks.Clear();
            RefreshAll();
        }
    }

    void SubmitExpressCart()
    {
        if (showMenu) return;
        if (inventory == null || cartPacks.Count == 0) return;
        if (inventory.TryOrderCart(cartPacks, true))
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
        bool lockedIngredients = HasLockedIngredient();
        foreach (GameObject child in menuRows)
        {
            if (child == null) continue;
            MilestoneLockedMenuRow row = child.GetComponent<MilestoneLockedMenuRow>();
            if (row == null || row.item == null || inventory.orderConfig == null) continue;
            int unlockMilestone = inventory.orderConfig.GetMenuItemUnlockMilestone(row.item);
            bool unlocked = inventory.orderConfig.IsMenuItemUnlocked(row.item);
            if (row.label != null)
            {
                row.label.text = unlocked ? inventory.GetDisplayName(row.item)
                    : inventory.GetDisplayName(row.item) + "\n<color=#D8B365>Unlocks at Milestone "
                        + unlockMilestone + "</color>";
                row.label.color = unlocked ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
            }
            if (row.rowBackground != null)
                row.rowBackground.color = unlocked
                    ? GameUITheme.Surface : GameUITheme.Backdrop;
            if (row.preview != null)
                row.preview.color = unlocked ? Color.white : Color.clear;
            if (row.lockVisual != null) row.lockVisual.alpha = unlocked ? 0f : 1f;
            if (row.workflowText != null)
                row.workflowText.text = lockedIngredients || !unlocked
                    ? "Recipe details unlock when the required ingredients are available."
                    : GetWorkflowDescription(inventory.orderConfig, row.item);
            if (row.toggle != null) row.toggle.interactable = unlocked;
            if (row.toggle != null && row.toggle.targetGraphic is Image toggleBackground)
                toggleBackground.color = unlocked ? GameUITheme.Charcoal : GameUITheme.PanelSlate;
            SetButtonState(row.targetMinus, unlocked, GameUITheme.Danger);
            SetButtonState(row.targetPlus, unlocked, GameUITheme.Positive);
        }
        var delivery = IngredientDeliveryService.Instance;
        bool deliveryActive = delivery != null && delivery.HasPending;
        if (addAllButton != null) addAllButton.interactable = !deliveryActive;
        int cartTotal = 0;
        int cartPackCount = 0;
        int cartLockMilestone = 0;
        foreach (GameObject child in supplyRows)
        {
            if (child == null) continue;
            var row = child.GetComponent<IngredientOrderRow>();
            if (row == null || row.item == null) continue;

            string name = inventory.GetDisplayName(row.item);
            int pack = inventory.GetPackSize(row.item);
            int price = inventory.GetPackPrice(row.item);
            bool itemUnlocked = inventory.IsOrderItemUnlocked(row.item);
            int unlockMilestone = inventory.GetOrderItemUnlockMilestone(row.item);
            int stock = itemUnlocked ? inventory.GetCount(row.item) : 0;

            int incoming = 0;
            if (delivery != null)
                incoming = delivery.GetIncomingCount(row.item);

            if (row.infoText != null)
            {
                row.infoText.text = itemUnlocked
                    ? name + "\nPack of " + pack + "  |  $" + price
                    : "Locked Ingredient\n<color=#D8B365>Unlocks at Milestone " + unlockMilestone + "</color>";
                row.infoText.color = itemUnlocked ? GameUITheme.TextPrimary : GameUITheme.TextSecondary;
            }
            Image rowImage = child.GetComponent<Image>();
            if (rowImage != null)
                rowImage.color = itemUnlocked ? GameUITheme.Surface : GameUITheme.Backdrop;
            if (row.lockVisual != null) row.lockVisual.alpha = itemUnlocked ? 0f : 1f;
            if (row.stockText != null)
            {
                row.stockText.text = !itemUnlocked ? "Locked" : incoming > 0
                    ? "Stock\n" + stock + "\n+" + incoming + " incoming"
                    : "Stock\n" + stock;
                row.stockText.color = itemUnlocked ? GameUITheme.TextSecondary : GameUITheme.Accent;
            }
            if (!itemUnlocked) SetCartPacks(row.item, 0);
            int selected = itemUnlocked ? GetCartPacks(row.item) : 0;
            cartPackCount += selected;
            cartTotal += selected * price;
            if (!itemUnlocked && selected > 0 && cartLockMilestone == 0)
                cartLockMilestone = unlockMilestone;
            if (row.cartCountText != null) row.cartCountText.text = "x" + selected;
            SetButtonState(row.removeButton, !deliveryActive && itemUnlocked && selected > 0, GameUITheme.Danger);
            SetButtonState(row.addButton, !deliveryActive && itemUnlocked, GameUITheme.Positive);
        }


        bool affordable = money == null || money.CanAfford(cartTotal);
        int expressTotal = cartTotal + KitchenInventory.ExpressDeliveryFee;
        bool expressAffordable = money == null || money.CanAfford(expressTotal);
        if (deliveryStatusText != null)
        {
            float remaining = delivery != null ? delivery.NextDeliveryRemaining : -1f;
            deliveryStatusText.text = deliveryActive
                ? "Delivery in progress  |  Arrives " + IngredientDeliveryService.FormatCountdown(Mathf.Max(0f, remaining)) + "  |  New orders locked"
                : "Build your cart, then place one combined delivery order.";
            deliveryStatusText.color = deliveryActive
                ? GameUITheme.Accent : GameUITheme.TextSecondary;
        }
        if (cartSummaryText != null)
            cartSummaryText.text = cartPackCount == 0 ? "Cart is empty" : "Cart: " + cartPackCount + " pack" + (cartPackCount == 1 ? "" : "s") + "  |  $" + cartTotal;
        if (clearCartButton != null)
            clearCartButton.interactable = !deliveryActive && cartPackCount > 0;
        if (placeOrderButton != null)
            placeOrderButton.interactable = !deliveryActive && cartPackCount > 0 && affordable
                && cartLockMilestone == 0;
        if (placeOrderLabel != null)
            placeOrderLabel.text = deliveryActive ? "Delivery Active"
                : cartLockMilestone > 0 ? "Milestone " + cartLockMilestone
                : !affordable && cartPackCount > 0 ? "Need $" + cartTotal : "Place Order $" + cartTotal;
        if (expressOrderButton != null)
            expressOrderButton.interactable = !deliveryActive && cartPackCount > 0 && expressAffordable
                && cartLockMilestone == 0;
        if (expressOrderLabel != null)
            expressOrderLabel.text = deliveryActive ? "Delivering"
                : cartLockMilestone > 0 ? "Milestone " + cartLockMilestone
                : !expressAffordable && cartPackCount > 0 ? "Need $" + expressTotal
                : "Express $" + expressTotal;
    }

    public void FocusRecipeNode(RecipeGraphNodeView selected)
    {
        if (selected == null) return;
        var related = new HashSet<RectTransform>();
        RectTransform selectedRect = selected.transform as RectTransform;
        related.Add(selectedRect);
        bool changed;
        do
        {
            changed = false;
            foreach (RecipeGraphEdgeView edge in recipeGraphEdges)
                if (edge != null && edge.to != null && related.Contains(edge.to)
                    && edge.from != null && related.Add(edge.from))
                    changed = true;
        } while (changed);

        foreach (RecipeGraphNodeView node in recipeGraphNodes)
            if (node != null) node.SetFocus(related.Contains(node.transform as RectTransform));
        foreach (RecipeGraphEdgeView edge in recipeGraphEdges)
            if (edge != null) edge.SetFocus(edge.from != null && edge.to != null
                && related.Contains(edge.from) && related.Contains(edge.to));
    }

    public void ClearRecipeFocus()
    {
        foreach (RecipeGraphNodeView node in recipeGraphNodes)
            if (node != null) node.SetFocus(true);
        foreach (RecipeGraphEdgeView edge in recipeGraphEdges)
            if (edge != null) edge.SetFocus(true);
    }

    public void ShowRecipeDetails(ItemDefinition item)
    {
        if (recipeDetailsText == null || item == null || recipeChartMenu == null) return;
        AssemblyRecipeDefinition recipe = recipeChartMenu.GetAssemblyRecipe(item);
        string title = !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName.ToUpperInvariant() : item.name.ToUpperInvariant();
        if (recipe == null)
        {
            recipeDetailsText.text = "<b>" + title + "</b>\n\nIngredient or processed component.\n\nValue: $" + item.price;
            return;
        }

        string inputs = "";
        if (recipe.processedInput != null) inputs += "• " + recipe.processedInput.itemName + " ×" + Mathf.Max(1, recipe.processedInputAmount) + "\n";
        if (recipe.pantryInput != null) inputs += "• " + recipe.pantryInput.itemName + " ×" + Mathf.Max(1, recipe.pantryInputAmount) + "\n";
        if (recipe.thirdInput != null) inputs += "• " + recipe.thirdInput.itemName + " ×" + Mathf.Max(1, recipe.thirdInputAmount) + "\n";
        int milestone = recipeChartMenu.GetMenuItemUnlockMilestone(item);
        recipeDetailsText.text = "<b>" + title + "</b>\n\n<b>INPUTS</b>\n" + inputs
            + "\n<b>STATION</b>\n" + (recipe.RequiresMk2 ? "Assembly Station MK2" : "Assembly Station MK1 or MK2")
            + "\n\n<b>VALUE</b>  $" + item.price
            + (milestone > 0 ? "\n<b>UNLOCK</b>  Milestone " + milestone : "");
    }

    static void SetButtonState(Button button, bool interactable, Color activeColor)
    {
        if (button == null) return;
        button.interactable = interactable;
        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = interactable ? activeColor : GameUITheme.PanelSlate;
    }
}

public sealed class RecipeGraphNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public IngredientsOrderUI owner;
    public ItemDefinition item;
    public Image cardImage;
    public CanvasGroup group;
    public Color baseColor;

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.FocusRecipeNode(this);
        if (cardImage != null) cardImage.color = Color.Lerp(baseColor, Color.white, 0.12f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.ClearRecipeFocus();
        if (cardImage != null) cardImage.color = baseColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.ShowRecipeDetails(item);
        Sfx.Play(SfxId.UiClick);
    }

    public void SetFocus(bool focused)
    {
        if (group != null) group.alpha = focused ? 1f : 0.18f;
    }
}

public sealed class RecipeGraphEdgeView : MonoBehaviour
{
    public RectTransform from;
    public RectTransform to;
    public CanvasGroup visual;
    public void SetFocus(bool focused)
    {
        if (visual != null) visual.alpha = focused ? 1f : 0.08f;
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
    public CanvasGroup lockVisual;
}

public sealed class MilestoneLockedMenuRow : MonoBehaviour
{
    public ItemDefinition item;
    public TextMeshProUGUI label;
    public TextMeshProUGUI workflowText;
    public RawImage preview;
    public CanvasGroup lockVisual;
    public Image rowBackground;
    public Toggle toggle;
    public Button targetMinus;
    public Button targetPlus;
}

/// <summary>Click-and-drag panning for the recipe dependency graph.</summary>
public sealed class RecipeGraphDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    static readonly HashSet<RecipeGraphDrag> ActiveGraphs = new HashSet<RecipeGraphDrag>();
    public static bool IsAnyGraphOpen => ActiveGraphs.Count > 0;

    public RectTransform content;
    public TextMeshProUGUI zoomLabel;
    public float minimumZoom = 0.45f;
    public float maximumZoom = 1.8f;
    public float zoomStep = 0.12f;
    Vector2 startPointer;
    Vector2 startContent;

    void OnEnable() => ActiveGraphs.Add(this);
    void OnDisable() => ActiveGraphs.Remove(this);
    void OnDestroy() => ActiveGraphs.Remove(this);

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (content == null) return;
        startPointer = eventData.position;
        startContent = content.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (content == null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        Vector2 next = startContent + (eventData.position - startPointer) / scale;
        next.x = Mathf.Clamp(next.x, -700f, 700f);
        next.y = Mathf.Clamp(next.y, -1500f, 1500f);
        content.anchoredPosition = next;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (content == null || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
        float oldZoom = content.localScale.x;
        float newZoom = Mathf.Clamp(oldZoom + Mathf.Sign(eventData.scrollDelta.y) * zoomStep,
            minimumZoom, maximumZoom);
        if (Mathf.Approximately(oldZoom, newZoom)) return;

        RectTransform viewport = transform as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        Vector2 pointer;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,
                eventData.position, eventCamera, out pointer))
            pointer = Vector2.zero;

        float ratio = newZoom / oldZoom;
        Vector2 position = content.anchoredPosition;
        content.localScale = Vector3.one * newZoom;
        content.anchoredPosition = pointer + (position - pointer) * ratio;
        RefreshZoomLabel();
    }

    public void RefreshZoomLabel()
    {
        if (zoomLabel != null && content != null)
            zoomLabel.text = Mathf.RoundToInt(content.localScale.x * 100f) + "%";
    }
}
