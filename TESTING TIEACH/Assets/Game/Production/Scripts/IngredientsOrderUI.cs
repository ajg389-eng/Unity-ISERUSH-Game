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
    Button suppliesTab, menuTab;
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
    CustomerOrderConfig recipeChartMenu;
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
            CreateSectionHeader("Items to sell");

            foreach (var item in menu.GetMenuItems())
                if (item != null) CreateMenuToggleRow(menu, item);
            for (int i = firstMenuRow; i < listContainer.childCount; i++) menuRows.Add(listContainer.GetChild(i).gameObject);
        }

        int firstSupplyRow = listContainer.childCount;
        CreateSectionHeader("Order ingredient packs");
        foreach (var item in inventory.GetIngredientCatalogItems())
        {
            if (item == null) continue;
            CreateOrderRow(item);
        }
        for (int i = firstSupplyRow; i < listContainer.childCount; i++) supplyRows.Add(listContainer.GetChild(i).gameObject);
        CreateCartFooter();
        BuildNavigation(menu);
        SelectSection(showMenu);

        built = true;
    }

    void OnDisable()
    {
        if (recipesOverlay != null) recipesOverlay.SetActive(false);
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
        suppliesTab = CreateCartButton(navigation.transform, "SuppliesTab", "Supplies", 90, new Color(0.2f,0.3f,0.4f), out label);
        suppliesTab.onClick.AddListener(() => SelectSection(false));
        menuTab = CreateCartButton(navigation.transform, "MenuTab", "Menu", 80, new Color(0.2f,0.3f,0.4f), out label);
        menuTab.onClick.AddListener(() => SelectSection(true));
        var recipes = CreateCartButton(navigation.transform, "RecipesTab", "Recipes", 90, new Color(0.24f,0.48f,0.58f), out label);
        recipes.interactable = menu != null;
        recipes.onClick.AddListener(() => OpenRecipes(menu));
    }

    void SelectSection(bool menu)
    {
        showMenu = menu;
        foreach (var row in menuRows) if (row != null) row.SetActive(menu);
        foreach (var row in supplyRows) if (row != null) row.SetActive(!menu);
        if (suppliesTab != null) suppliesTab.GetComponent<Image>().color = !menu ? new Color(0.68f,0.49f,0.19f) : new Color(0.49f,0.37f,0.18f);
        if (menuTab != null) menuTab.GetComponent<Image>().color = menu ? new Color(0.27f,0.62f,0.4f) : new Color(0.23f,0.48f,0.33f);
        if (placeOrderButton != null) placeOrderButton.transform.parent.gameObject.SetActive(!menu);
        if (deliveryStatusText != null) deliveryStatusText.transform.parent.gameObject.SetActive(!menu);
        PadListForUndoFooter();
        Canvas.ForceUpdateCanvases();
        var scroll = listContainer.GetComponentInParent<ScrollRect>();
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
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
            new Color(0.24f, 0.48f, 0.58f, 1f), out label);
        label.fontSize = 13f;
        button.onClick.AddListener(() => OpenRecipes(menu));
    }

    void OpenRecipes(CustomerOrderConfig menu)
    {
        if (recipesOverlay == null) BuildRecipesOverlay(menu);
        recipesOverlay.SetActive(true);
        ShowRecipeTab(menu, 0);
        recipesOverlay.transform.SetAsLastSibling();
        Sfx.Play(SfxId.UiClick);
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
        recipesOverlay.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.04f, 0.78f);

        var panel = new GameObject("RecipeBook", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(recipesOverlay.transform, false);
        RectTransform panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(650f, 720f);
        panel.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.995f);

        CreateAbsoluteText(panel.transform, "Title", "RECIPES  •  DRAG TO PAN  •  SCROLL TO ZOOM", 18f, FontStyles.Bold,
            new Vector2(22f, -56f), new Vector2(-80f, -12f), TextAlignmentOptions.Center);
        TextMeshProUGUI closeLabel;
        Button close = CreateAbsoluteButton(panel.transform, "Close", "X", new Vector2(-58f, -54f),
            new Vector2(-14f, -14f), new Color(0.55f, 0.25f, 0.27f, 1f), out closeLabel);
        close.onClick.AddListener(() => { recipesOverlay.SetActive(false); Sfx.Play(SfxId.UiClick); });

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
            Button tab = CreateCartButton(tabs.transform, names[i], names[i], 150f,
                new Color(0.17f, 0.2f, 0.25f, 1f), out tabLabel);
            tabLabel.fontSize = 13f;
            tab.onClick.AddListener(() => { ShowRecipeTab(menu, captured); Sfx.Play(SfxId.UiClick); });
            recipeTabButtons.Add(tab);
        }

        var viewport = new GameObject("ChartViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(20f, 20f);
        viewportRt.offsetMax = new Vector2(-20f, -114f);
        viewport.GetComponent<Image>().color = new Color(0.08f, 0.095f, 0.125f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var chart = new GameObject("RecipeChart", typeof(RectTransform));
        chart.transform.SetParent(viewport.transform, false);
        RectTransform chartRt = (RectTransform)chart.transform;
        chartRt.anchorMin = chartRt.anchorMax = new Vector2(0.5f, 0.5f);
        chartRt.pivot = new Vector2(0.5f, 0.5f);
        chartRt.sizeDelta = new Vector2(2000f, 2400f);
        chartRt.anchoredPosition = new Vector2(0f, -240f);
        recipeChartRoot = chart.transform;
        RecipeGraphDrag drag = viewport.AddComponent<RecipeGraphDrag>();
        drag.content = chartRt;
    }

    void ShowRecipeTab(CustomerOrderConfig menu, int tab)
    {
        if (recipeChartRoot == null) return;
        recipeChartMenu = menu;
        RectTransform chart = (RectTransform)recipeChartRoot;
        chart.anchoredPosition = new Vector2(0f, -240f);
        chart.localScale = Vector3.one;
        for (int i = recipeChartRoot.childCount - 1; i >= 0; i--)
            Destroy(recipeChartRoot.GetChild(i).gameObject);
        for (int i = 0; i < recipeTabButtons.Count; i++)
            recipeTabButtons[i].GetComponent<Image>().color = i == tab
                ? new Color(0.28f, 0.63f, 0.49f, 1f)
                : new Color(0.17f, 0.2f, 0.25f, 1f);

        if (tab == 0) BuildBurgerTree(menu);
        else if (tab == 1) BuildFriesTree(menu);
        else BuildShakeTree(menu);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)recipeChartRoot);
    }

    void BuildBurgerTree(CustomerOrderConfig menu)
    {
        var rawPatty = AddGraphNode(menu.rawPattyIngredient, "RAW PATTY", "FREEZER", new Vector2(-520f, 500f));
        var cookedPatty = AddGraphNode(menu.cookedPattyIngredient, "COOKED PATTY", "GRILL", new Vector2(-520f, 310f));
        ConnectGraphNodes(rawPatty, cookedPatty, "COOK");

        var bun = AddGraphNode(FindRecipeInput(menu, menu.burgerBase, false), "BUN", "PANTRY", new Vector2(-250f, 310f));
        var burger = AddGraphNode(menu.burgerBase, "HAMBURGER", "ASSEMBLY", new Vector2(-390f, 100f), true);
        ConnectGraphNodes(cookedPatty, burger);
        ConnectGraphNodes(bun, burger, "ASSEMBLE");

        AssemblyRecipeDefinition cheeseRecipe = FindRecipeFeeding(menu, menu.cheeseburgerItem);
        ItemDefinition rawCheese = cheeseRecipe != null ? cheeseRecipe.rawPantryInput : menu.cheeseIngredient;
        ItemDefinition slicedCheese = cheeseRecipe != null ? cheeseRecipe.pantryInput : menu.slicedCheeseIngredient;
        var rawCheeseNode = AddGraphNode(rawCheese, "RAW CHEESE", "FREEZER", new Vector2(40f, 500f));
        var slicedCheeseNode = AddGraphNode(slicedCheese, "SLICED CHEESE", "CUTTING", new Vector2(40f, 310f));
        ConnectGraphNodes(rawCheeseNode, slicedCheeseNode, "SLICE");
        var cheesePatty = AddGraphNode(cheeseRecipe != null ? cheeseRecipe.output : null,
            "CHEESE PATTY", "ASSEMBLY", new Vector2(-100f, 100f), true);
        ConnectGraphNodes(cookedPatty, cheesePatty);
        ConnectGraphNodes(slicedCheeseNode, cheesePatty, "ASSEMBLE");

        var cheeseburger = AddGraphNode(menu.cheeseburgerItem, "CHEESEBURGER", "ASSEMBLY",
            new Vector2(-245f, -120f), true);
        ConnectGraphNodes(cheesePatty, cheeseburger);
        ConnectGraphNodes(bun, cheeseburger, "ASSEMBLE");

        var baconSlab = AddGraphNode(menu.baconSlabIngredient, "BACON SLAB", "FREEZER", new Vector2(330f, 100f));
        var cutBacon = AddGraphNode(menu.cutBaconIngredient, "CUT BACON", "CUTTING", new Vector2(330f, -80f));
        var cookedBacon = AddGraphNode(menu.cookedBaconIngredient, "COOKED BACON", "GRILL", new Vector2(330f, -260f));
        var cb = AddGraphNode(menu.cheeseBaconBurgerItem, "BACON CHEESEBURGER", "ASSEMBLY", new Vector2(40f, -480f), true);
        ConnectGraphNodes(baconSlab, cutBacon, "CUT"); ConnectGraphNodes(cutBacon, cookedBacon, "COOK");
        ConnectGraphNodes(cheesePatty, cb);
        ConnectGraphNodes(cookedBacon, cb);
        ConnectGraphNodes(bun, cb, "ASSEMBLE");

        var lettuce = AddGraphNode(menu.lettuceIngredient, "LETTUCE", "FREEZER", new Vector2(-520f, -410f));
        var slicedLettuce = AddGraphNode(menu.slicedLettuceIngredient, "LETTUCE SLICE", "CUTTING", new Vector2(-520f, -600f));
        var tomato = AddGraphNode(menu.tomatoIngredient, "TOMATO", "FREEZER", new Vector2(-230f, -410f));
        var slicedTomato = AddGraphNode(menu.slicedTomatoIngredient, "TOMATO SLICE", "CUTTING", new Vector2(-230f, -600f));
        var veggieMix = AddGraphNode(menu.veggieMixIngredient, "VEGGIE MIX", "ASSEMBLY", new Vector2(-375f, -800f), true);
        var classic = AddGraphNode(menu.clBurgerItem, "CLASSIC BURGER", "ASSEMBLY", new Vector2(-375f, -1030f), true);
        ConnectGraphNodes(lettuce, slicedLettuce, "SLICE");
        ConnectGraphNodes(tomato, slicedTomato, "SLICE");
        ConnectGraphNodes(slicedLettuce, veggieMix);
        ConnectGraphNodes(slicedTomato, veggieMix, "ASSEMBLE");
        ConnectGraphNodes(cookedPatty, classic);
        ConnectGraphNodes(veggieMix, classic);
        ConnectGraphNodes(bun, classic, "ASSEMBLE");
    }

    void BuildFriesTree(CustomerOrderConfig menu)
    {
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
        rt.sizeDelta = new Vector2(220f, 92f);
        rt.anchoredPosition = position;
        node.GetComponent<Image>().color = locked
            ? new Color(0.11f, 0.12f, 0.16f, 1f)
            : final ? new Color(0.18f, 0.43f, 0.34f, 1f)
            : new Color(0.15f, 0.17f, 0.22f, 1f);

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        previewGo.transform.SetParent(node.transform, false);
        RectTransform previewRt = (RectTransform)previewGo.transform;
        previewRt.anchorMin = new Vector2(0f, 0f);
        previewRt.anchorMax = new Vector2(0f, 1f);
        previewRt.offsetMin = new Vector2(8f, 8f);
        previewRt.offsetMax = new Vector2(80f, -8f);
        RawImage preview = previewGo.GetComponent<RawImage>();
        if (item != null) preview.texture = ItemPreviewThumbnails.GetPrefab(item.prefab, item.itemName);
        preview.color = item == null ? Color.clear
            : locked ? new Color(0.58f, 0.61f, 0.67f, 0.55f) : Color.white;
        preview.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(node.transform, false);
        RectTransform labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(88f, 8f);
        labelRt.offsetMax = new Vector2(-8f, -8f);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        string display = item != null && !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : fallback;
        label.text = "<b>" + display + "</b>\n<size=70%><color=#91A4C3>" + station + "</color></size>"
            + (locked && unlockMilestone > 0
                ? "\n<size=68%><color=#F1C66D>LOCKED · MILESTONE " + unlockMilestone + "</color></size>" : "");
        label.fontSize = locked ? 13f : 15f;
        label.color = locked ? new Color(0.68f, 0.7f, 0.74f, 1f) : Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        return rt;
    }

    void ConnectGraphNodes(RectTransform from, RectTransform to, string action = null)
    {
        if (from == null || to == null) return;
        Vector2 start = from.anchoredPosition + new Vector2(0f, -from.sizeDelta.y * 0.5f);
        Vector2 end = to.anchoredPosition + new Vector2(0f, to.sizeDelta.y * 0.5f);
        float middleY = (start.y + end.y) * 0.5f;
        CreateGraphLine(new Vector2(start.x, start.y), new Vector2(start.x, middleY));
        CreateGraphLine(new Vector2(start.x, middleY), new Vector2(end.x, middleY));
        CreateGraphLine(new Vector2(end.x, middleY), new Vector2(end.x, end.y));
        if (!string.IsNullOrWhiteSpace(action))
        {
            var textGo = new GameObject("Edge_" + action, typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(recipeChartRoot, false);
            RectTransform rt = (RectTransform)textGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 24f);
            rt.anchoredPosition = new Vector2((start.x + end.x) * 0.5f, middleY + 14f);
            TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = action;
            text.fontSize = 10f;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.45f, 0.9f, 0.8f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            rt.SetAsFirstSibling();
        }
    }

    void CreateGraphLine(Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        var line = new GameObject("Connector", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(recipeChartRoot, false);
        RectTransform rt = (RectTransform)line.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(3f, delta.magnitude), 3f);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.GetComponent<Image>().color = new Color(0.38f, 0.68f, 0.72f, 0.9f);
        line.GetComponent<Image>().raycastTarget = false;
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
        var node = new GameObject("Node_" + fallback, typeof(RectTransform), typeof(LayoutElement),
            typeof(Image), typeof(HorizontalLayoutGroup));
        node.transform.SetParent(parentOverride != null ? parentOverride : recipeChartRoot, false);
        var le = node.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.preferredHeight = 88f;
        node.GetComponent<Image>().color = final
            ? new Color(0.20f, 0.42f, 0.34f, 1f)
            : new Color(0.16f, 0.18f, 0.23f, 1f);
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
        if (item != null)
            preview.texture = ItemPreviewThumbnails.GetPrefab(item.prefab, item.itemName);
        preview.color = item != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        preview.raycastTarget = false;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(node.transform, false);
        textGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = "<b>" + (item != null && !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : fallback)
            + "</b>\n<size=75%><color=#AFC0D8>" + subtitle + "</color></size>";
        text.fontSize = 17f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
    }

    void AddTreeArrow(string action = null)
    {
        var go = new GameObject("Arrow", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        go.transform.SetParent(recipeChartRoot, false);
        go.GetComponent<LayoutElement>().preferredHeight = string.IsNullOrWhiteSpace(action) ? 30f : 42f;
        TextMeshProUGUI arrow = go.GetComponent<TextMeshProUGUI>();
        arrow.text = string.IsNullOrWhiteSpace(action)
            ? "▼"
            : "<size=55%>" + action + "</size>\n▼";
        arrow.fontSize = 20f;
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
        tmp.color = new Color(1f, 0.82f, 0.38f, 1f);
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void CreateMenuToggleRow(CustomerOrderConfig menu, ItemDefinition item)
    {
        const float collapsedHeight = 62f;
        const float expandedHeight = 154f;

        var row = new GameObject("Sell_" + item.name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        row.transform.SetParent(listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = collapsedHeight;
        le.preferredHeight = collapsedHeight;
        row.GetComponent<Image>().color = new Color(0.18f, 0.19f, 0.24f, 0.98f);

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

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
        previewGo.transform.SetParent(header.transform, false);
        var previewLe = previewGo.GetComponent<LayoutElement>();
        previewLe.minWidth = 44;
        previewLe.preferredWidth = 44;
        previewLe.minHeight = 44;
        previewLe.preferredHeight = 44;
        var preview = previewGo.GetComponent<RawImage>();
        preview.texture = ItemPreviewThumbnails.GetPrefab(GetPreviewPrefab(menu, item), item.itemName);
        preview.color = Color.white;
        preview.raycastTarget = false;

        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        nameGo.transform.SetParent(header.transform, false);
        var nameLe = nameGo.GetComponent<LayoutElement>();
        nameLe.flexibleWidth = 1f;
        nameLe.minHeight = 44f;
        nameLe.preferredHeight = 44f;
        var nameText = nameGo.GetComponent<TextMeshProUGUI>();
        int unlockMilestone = menu.GetMenuItemUnlockMilestone(item);
        bool itemUnlocked = menu.IsMenuItemUnlocked(item);
        nameText.text = inventory.GetDisplayName(item)
            + (!itemUnlocked && unlockMilestone > 0
                ? $"  <color=#F1C66D>LOCKED · MILESTONE {unlockMilestone}</color>" : "");
        nameText.fontSize = 16;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = itemUnlocked ? Color.white : new Color(0.68f, 0.7f, 0.74f, 1f);
        row.GetComponent<Image>().color = itemUnlocked
            ? new Color(0.18f, 0.19f, 0.24f, 0.98f)
            : new Color(0.11f, 0.12f, 0.16f, 0.98f);
        preview.color = itemUnlocked ? Color.white : new Color(0.58f, 0.61f, 0.67f, 0.62f);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
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
        targetLe.minWidth = 66f;
        targetLe.preferredWidth = 66f;
        var targetLabel = targetGo.GetComponent<TextMeshProUGUI>();
        targetLabel.fontSize = 11f;
        targetLabel.fontStyle = FontStyles.Bold;
        targetLabel.color = new Color(0.85f, 0.9f, 1f, 1f);
        targetLabel.alignment = TextAlignmentOptions.Center;
        targetLabel.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) targetLabel.font = TMP_Settings.defaultFontAsset;
        System.Action refreshTarget = () => targetLabel.text = "TARGET " +
            (production != null ? production.GetProductionTarget(captured) : 0);
        Button targetPlus = CreateCartButton(header.transform, "TargetPlus", "+", 28f,
            new Color(0.27f, 0.62f, 0.4f, 1f), out _);
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
        workflowGo.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.17f, 0.96f);
        workflowGo.GetComponent<Image>().raycastTarget = false;
        var workflowLe = workflowGo.GetComponent<LayoutElement>();
        workflowLe.minHeight = 83f;
        workflowLe.preferredHeight = 83f;

        var workflowTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        workflowTextGo.transform.SetParent(workflowGo.transform, false);
        var workflowRt = (RectTransform)workflowTextGo.transform;
        workflowRt.anchorMin = Vector2.zero;
        workflowRt.anchorMax = Vector2.one;
        workflowRt.offsetMin = new Vector2(10f, 6f);
        workflowRt.offsetMax = new Vector2(-10f, -6f);
        var workflowText = workflowTextGo.GetComponent<TextMeshProUGUI>();
        workflowText.text = GetWorkflowDescription(menu, item);
        workflowText.fontSize = 11.5f;
        workflowText.color = new Color(0.78f, 0.84f, 0.94f, 1f);
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
        rowState.preview = preview;
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
        statusGo.transform.SetParent(transform, false);
        PinFooter((RectTransform)statusGo.transform, 112f, 42f);
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
        footer.transform.SetParent(transform, false);
        PinFooter((RectTransform)footer.transform, 56f, 52f);
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

        TextMeshProUGUI addAllLabel;
        addAllButton = CreateCartButton(footer.transform, "AddAll", "Add All\n(+1 each)", 90, new Color(0.27f,0.48f,0.36f), out addAllLabel);
        addAllLabel.fontSize = 12;
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
        clearLabel.fontSize = 12f;
        clearCartButton.onClick.AddListener(() =>
        {
            cartPacks.Clear();
            RefreshAll();
        });

        placeOrderButton = CreateCartButton(footer.transform, "PlaceOrder", "Place Order", 126f, new Color(0.27f, 0.62f, 0.4f, 1f), out placeOrderLabel);
        placeOrderLabel.fontSize = 12f;
        placeOrderButton.onClick.AddListener(SubmitCart);

        expressOrderButton = CreateCartButton(footer.transform, "ExpressOrder", "Express", 112f, new Color(0.85f, 0.53f, 0.16f, 1f), out expressOrderLabel);
        expressOrderLabel.fontSize = 12f;
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
        foreach (GameObject child in menuRows)
        {
            if (child == null) continue;
            MilestoneLockedMenuRow row = child.GetComponent<MilestoneLockedMenuRow>();
            if (row == null || row.item == null || inventory.orderConfig == null) continue;
            int unlockMilestone = inventory.orderConfig.GetMenuItemUnlockMilestone(row.item);
            bool unlocked = inventory.orderConfig.IsMenuItemUnlocked(row.item);
            if (row.label != null)
            {
                row.label.text = inventory.GetDisplayName(row.item)
                    + (!unlocked && unlockMilestone > 0
                        ? $"  <color=#F1C66D>LOCKED · MILESTONE {unlockMilestone}</color>" : "");
                row.label.color = unlocked ? Color.white : new Color(0.68f, 0.7f, 0.74f, 1f);
            }
            if (row.rowBackground != null)
                row.rowBackground.color = unlocked
                    ? new Color(0.18f, 0.19f, 0.24f, 0.98f)
                    : new Color(0.11f, 0.12f, 0.16f, 0.98f);
            if (row.preview != null)
                row.preview.color = unlocked ? Color.white : new Color(0.58f, 0.61f, 0.67f, 0.62f);
            if (row.toggle != null) row.toggle.interactable = unlocked;
            if (row.targetMinus != null) row.targetMinus.interactable = unlocked;
            if (row.targetPlus != null) row.targetPlus.interactable = unlocked;
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
                    ? name + "\n<size=85%>Pack of " + pack + "  |  $" + price + "</size>"
                    : name + "\n<size=85%><color=#F1C66D>LOCKED · UNLOCKS IN MILESTONE "
                        + unlockMilestone + "</color></size>";
                row.infoText.color = itemUnlocked ? Color.white : new Color(0.68f, 0.7f, 0.74f, 1f);
            }
            Image rowImage = child.GetComponent<Image>();
            if (rowImage != null)
                rowImage.color = itemUnlocked
                    ? new Color(0.22f, 0.22f, 0.28f, 0.95f)
                    : new Color(0.12f, 0.13f, 0.17f, 0.95f);
            if (row.stockText != null)
            {
                row.stockText.text = !itemUnlocked ? "LOCKED" : incoming > 0
                    ? "Stock\n" + stock + "\n<size=80%>+" + incoming + " incoming</size>"
                    : "Stock\n" + stock;
                row.stockText.color = itemUnlocked
                    ? new Color(0.85f, 0.9f, 1f, 1f) : new Color(0.68f, 0.7f, 0.74f, 1f);
            }
            int selected = GetCartPacks(row.item);
            cartPackCount += selected;
            cartTotal += selected * price;
            if (!itemUnlocked && selected > 0 && cartLockMilestone == 0)
                cartLockMilestone = unlockMilestone;
            if (row.cartCountText != null) row.cartCountText.text = "x" + selected;
            if (row.removeButton != null) row.removeButton.interactable = !deliveryActive && selected > 0;
            if (row.addButton != null) row.addButton.interactable = !deliveryActive && itemUnlocked;
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
                ? new Color(1f, 0.78f, 0.35f, 1f)
                : new Color(0.75f, 0.84f, 0.96f, 1f);
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
                : cartLockMilestone > 0 ? "Unlock Milestone " + cartLockMilestone
                : !affordable && cartPackCount > 0 ? "Need $" + cartTotal : "Place Order $" + cartTotal;
        if (expressOrderButton != null)
            expressOrderButton.interactable = !deliveryActive && cartPackCount > 0 && expressAffordable
                && cartLockMilestone == 0;
        if (expressOrderLabel != null)
            expressOrderLabel.text = deliveryActive ? "Delivery Active"
                : cartLockMilestone > 0 ? "Unlock Milestone " + cartLockMilestone
                : !expressAffordable && cartPackCount > 0 ? "Need $" + expressTotal
                : "EXPRESS $" + expressTotal + "\nDispatch now (+$" + KitchenInventory.ExpressDeliveryFee + ")";
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

public sealed class MilestoneLockedMenuRow : MonoBehaviour
{
    public ItemDefinition item;
    public TextMeshProUGUI label;
    public RawImage preview;
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
    }
}
