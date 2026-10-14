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
    const float SupplyCardHeight = 54f;
    const float SupplyGridGap = 4f;
    float nextRefresh;
    bool built;
    bool showMenu;
    bool standaloneBusinessTabs;
    GameObject navigation;
    readonly List<GameObject> menuRows = new List<GameObject>();
    readonly List<GameObject> menuLayoutRows = new List<GameObject>();
    readonly List<GameObject> supplyRows = new List<GameObject>();
    readonly List<GameObject> supplyLayoutRows = new List<GameObject>();
    Button suppliesTab, menuTab;
    int builtCatalogHash = int.MinValue;
    readonly Dictionary<ItemDefinition, int> cartPacks = new Dictionary<ItemDefinition, int>();
    TextMeshProUGUI deliveryStatusText;
    TextMeshProUGUI cartSummaryText;
    Button clearCartButton;
    Button addAllButton;
    Button placeOrderButton;
    Button expressOrderButton;
    TextMeshProUGUI placeOrderLabel;
    TextMeshProUGUI expressOrderLabel;
    GameObject expressLockedOverlay;
    GameObject expandedMenuRow;
    GameObject expandedWorkflow;
    LayoutElement expandedMenuLayout;
    TextMeshProUGUI expandedChevron;
    GameObject recipesOverlay;
    Transform recipeChartRoot;
    Transform recipeChartContainer;
    RectTransform recipeChartViewport;
    CustomerOrderConfig recipeChartMenu;
    RecipeGraphDrag recipeGraphDrag;
    TextMeshProUGUI recipeZoomLabel;
    TextMeshProUGUI recipeDetailsText;
    readonly GameObject[] recipeTabRoots = new GameObject[3];
    int activeRecipeTab = -1;
    readonly List<RecipeGraphNodeView> recipeGraphNodes = new List<RecipeGraphNodeView>();
    readonly List<RecipeGraphEdgeView> recipeGraphEdges = new List<RecipeGraphEdgeView>();
    readonly List<Button> recipeTabButtons = new List<Button>();

    public Button TutorialSuppliesTab => suppliesTab;
    public Button TutorialMenuTab => menuTab;
    public Button TutorialPlaceOrderButton => placeOrderButton;
    public int TutorialCartPacks(ItemDefinition item) => GetCartPacks(item);

    public Toggle TutorialMenuToggle(ItemDefinition item)
    {
        if (item == null || menuRows == null) return null;
        foreach (GameObject rowObject in menuRows)
        {
            if (rowObject == null) continue;
            MilestoneLockedMenuRow row = rowObject.GetComponent<MilestoneLockedMenuRow>();
            if (row != null && row.item == item) return row.toggle;
        }
        return null;
    }

    public void ConfigureBusinessSection(bool menu)
    {
        standaloneBusinessTabs = true;
        if (!built) return;
        SelectSection(menu);
        if (navigation != null)
            navigation.SetActive(false);
    }

    public void OpenRecipesFromHud()
    {
        EnsureRefs();
        CustomerOrderConfig menu = inventory != null ? inventory.orderConfig : null;
        if (menu == null) return;
        if (!built) RebuildRows();
        OpenRecipes(menu);
    }

    public void CloseRecipesFromHud()
    {
        CloseRecipes();
    }

    public Button TutorialAddButton(ItemDefinition item)
    {
        if (item == null || supplyRows == null) return null;
        foreach (GameObject rowObject in supplyRows)
        {
            if (rowObject == null) continue;
            IngredientOrderRow row = rowObject.GetComponent<IngredientOrderRow>();
            if (row != null && row.item == item) return row.addButton;
        }
        return null;
    }

    void OnEnable()
    {
        EnsureRefs();
        EnsureList();
        // Keep the generated hierarchy alive while the Business screen is
        // opened and closed. Rebuild only when the actual item catalog changes.
        if (!built || builtCatalogHash != GetCatalogHash())
            RebuildRows();
        else
        {
            ClearListBackgrounds();
            FitListArea();
        }
        PurchaseUndoFooter.RemoveFromPanel(transform);
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
        var panelImage = GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = Color.clear;
            panelImage.raycastTarget = true;
            if (panelImage.GetComponent<GameUIThemeStyled>() == null)
                panelImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

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
            ClearListBackgrounds();
            FitListArea();
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

        var scrollImage = scrollGo.GetComponent<Image>();
        if (scrollImage != null)
        {
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            if (scrollImage.GetComponent<GameUIThemeStyled>() == null)
                scrollImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = (RectTransform)viewport.transform;
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        viewportImage.raycastTarget = true;
        viewport.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        viewportImage.gameObject.AddComponent<GameUIThemeStyled>();
        var viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

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
        scroll.enabled = true;
        viewport.gameObject.SetActive(true);
        content.gameObject.SetActive(true);
        contentRt.localScale = Vector3.one;
        listContainer = content.transform;
        ClearListBackgrounds();
        FitListArea();
    }

    void ClearListBackgrounds()
    {
        if (listContainer == null) return;

        var contentImage = listContainer.GetComponent<Image>();
        if (contentImage != null)
        {
            contentImage.color = Color.clear;
            contentImage.raycastTarget = false;
            if (contentImage.GetComponent<GameUIThemeStyled>() == null)
                contentImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var scroll = listContainer.GetComponentInParent<ScrollRect>();
        if (scroll == null) return;

        var scrollImage = scroll.GetComponent<Image>();
        if (scrollImage != null)
        {
            scrollImage.color = Color.clear;
            scrollImage.raycastTarget = true;
            if (scrollImage.GetComponent<GameUIThemeStyled>() == null)
                scrollImage.gameObject.AddComponent<GameUIThemeStyled>();
        }

        var viewportImage = scroll.viewport != null ? scroll.viewport.GetComponent<Image>() : null;
        if (scroll.viewport != null)
        {
            var rectMask = scroll.viewport.GetComponent<RectMask2D>();
            if (rectMask != null) Destroy(rectMask);
            var viewportMask = scroll.viewport.GetComponent<Mask>();
            if (viewportMask == null) viewportMask = scroll.viewport.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
        }
        if (viewportImage != null)
        {
            viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
            viewportImage.raycastTarget = true;
            var viewportRenderer = viewportImage.GetComponent<CanvasRenderer>();
            if (viewportRenderer != null) viewportRenderer.cullTransparentMesh = false;
            if (viewportImage.GetComponent<GameUIThemeStyled>() == null)
                viewportImage.gameObject.AddComponent<GameUIThemeStyled>();
        }
    }

    void FitListArea()
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
        scrollRt.offsetMin = new Vector2(12, showMenu ? 120 : 164);
        // Keep the first row close to the Supply/Menu tab bar. The footer is
        // reserved by offsetMin, so a large top inset only creates dead space.
        scrollRt.offsetMax = new Vector2(-12, 0f);
    }

    void RebuildRows()
    {
        EnsureRefs();
        EnsureList();
        if (listContainer == null) return;

        for (int i = listContainer.childCount - 1; i >= 0; i--)
        { var old = listContainer.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
        menuRows.Clear();
        menuLayoutRows.Clear();
        supplyRows.Clear();
        supplyLayoutRows.Clear();
        if (deliveryStatusText != null) { var old = deliveryStatusText.transform.parent.gameObject; old.SetActive(false); Destroy(old); }
        if (placeOrderButton != null) { var old = placeOrderButton.transform.parent.gameObject; old.SetActive(false); Destroy(old); }
        deliveryStatusText = null;
        cartSummaryText = null;
        clearCartButton = null;
        placeOrderButton = null;
        expressOrderButton = null;
        expressLockedOverlay = null;
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
            builtCatalogHash = GetCatalogHash();
            return;
        }

        CustomerOrderConfig menu = inventory.orderConfig;
        if (menu != null)
        {
            menuLayoutRows.Add(CreateSectionHeader("Items to Sell"));
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
                menuLayoutRows.Add(CreateMenuMilestoneHeader(milestoneGroup.Key));
                GameObject grid = CreateMenuGrid(milestoneGroup.Key);
                menuLayoutRows.Add(grid);
                for (int i = 0; i < milestoneGroup.Value.Count; i += 2)
                {
                    GameObject pair = CreateMenuGridRow(grid.transform);
                    menuRows.Add(CreateMenuToggleRow(menu, milestoneGroup.Value[i], pair.transform));
                    if (i + 1 < milestoneGroup.Value.Count)
                        menuRows.Add(CreateMenuToggleRow(menu, milestoneGroup.Value[i + 1], pair.transform));
                    else
                        CreateMenuGridSpacer(pair.transform);
                }
            }
        }

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
            supplyLayoutRows.Add(CreateMilestoneHeader(milestoneGroup.Key));
            GameObject grid = CreateSupplyGrid(milestoneGroup.Key);
            supplyLayoutRows.Add(grid);
            foreach (ItemDefinition item in milestoneGroup.Value)
                supplyRows.Add(CreateOrderRow(item, grid.transform));
        }
        CreateCartFooter();
        BuildNavigation(menu);
        SelectSection(showMenu);

        built = true;
        builtCatalogHash = GetCatalogHash();
        GameUITheme.ApplyTo(transform);
        SelectSection(showMenu);
    }

    int GetCatalogHash()
    {
        if (inventory == null) return 0;

        unchecked
        {
            int hash = 17;
            CustomerOrderConfig menu = inventory.orderConfig;
            if (menu != null)
            {
                foreach (ItemDefinition item in menu.GetMenuItems())
                {
                    if (item == null) continue;
                    hash = MixCatalogHash(hash, item, menu.GetMenuItemUnlockMilestone(item));
                }
            }

            foreach (ItemDefinition item in inventory.GetIngredientCatalogItems())
            {
                if (item == null) continue;
                hash = MixCatalogHash(hash, item, inventory.GetOrderItemUnlockMilestone(item));
            }

            return hash;
        }
    }

    static int MixCatalogHash(int hash, ItemDefinition item, int milestone)
    {
        if (item == null) return hash * 31;
        hash = hash * 31 + item.GetInstanceID();
        hash = hash * 31 + milestone;
        return hash;
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
        suppliesTab = CreateCartButton(navigation.transform, "SuppliesTab", "Supplies", 112, GameUITheme.Surface, out label);
        MakeNavigationTabFill(suppliesTab);
        suppliesTab.onClick.AddListener(() => SelectSection(false));
        DelayedTooltip.Attach(suppliesTab, "Supplies: order ingredient stock");
        menuTab = CreateCartButton(navigation.transform, "MenuTab", "Menu", 96, GameUITheme.Surface, out label);
        MakeNavigationTabFill(menuTab);
        menuTab.onClick.AddListener(() => SelectSection(true));
        DelayedTooltip.Attach(menuTab, "Menu: choose which unlocked recipes to sell");
        if (standaloneBusinessTabs)
            navigation.SetActive(false);
    }

    static void MakeNavigationTabFill(Button button)
    {
        if (button == null) return;
        LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 0f;
        layout.preferredWidth = 0f;
        layout.flexibleWidth = 1f;
    }

    void SelectSection(bool menu)
    {
        showMenu = menu;
        if (menu)
            OnboardingTutorial.NotifyMenuScreenOpened();
        foreach (var row in menuLayoutRows) if (row != null) row.SetActive(menu);
        foreach (var row in menuRows) if (row != null) row.SetActive(menu);
        foreach (var row in supplyRows) if (row != null) row.SetActive(!menu);
        foreach (var row in supplyLayoutRows) if (row != null) row.SetActive(!menu);
        UpdateNavigationTabs();
        if (placeOrderButton != null) placeOrderButton.transform.parent.gameObject.SetActive(!menu);
        if (deliveryStatusText != null) deliveryStatusText.transform.parent.gameObject.SetActive(!menu);
        FitListArea();
        if (standaloneBusinessTabs && navigation != null)
            navigation.SetActive(false);
        Canvas.ForceUpdateCanvases();
        FitSupplyGrids();
        if (listContainer is RectTransform listRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        var scroll = listContainer.GetComponentInParent<ScrollRect>();
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }

    void FitSupplyGrids()
    {
        if (listContainer == null) return;
        foreach (GameObject root in supplyLayoutRows)
        {
            if (root == null) continue;
            var grid = root.GetComponent<GridLayoutGroup>();
            if (grid == null) continue;

            var gridRect = root.transform as RectTransform;
            float width = gridRect != null ? gridRect.rect.width : 0f;
            if (width <= 0f && listContainer is RectTransform listRect)
                width = listRect.rect.width - 8f;
            if (width <= 0f) continue;

            float cellWidth = (width - grid.spacing.x - grid.padding.horizontal) * 0.5f;
            grid.cellSize = new Vector2(Mathf.Max(238f, cellWidth), SupplyCardHeight);
        }
    }

    void UpdateNavigationTabs()
    {
        GameUITheme.ApplyTabButton(suppliesTab, !showMenu);
        GameUITheme.ApplyTabButton(menuTab, showMenu);
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
        Button button = CreateCartButton(row.transform, "OpenRecipes", "RECIPES", 52f,
            GameUITheme.Coral, out label);
        label.fontSize = 14f;
        button.onClick.AddListener(() => OpenRecipes(menu));
        DelayedTooltip.Attach(button, "Open recipes");
    }

    void OpenRecipes(CustomerOrderConfig menu)
    {
        if (recipesOverlay == null) BuildRecipesOverlay(menu);
        recipesOverlay.SetActive(true);
        UpdateNavigationTabs();
        ShowRecipeTab(menu, 0);
        recipesOverlay.transform.SetAsLastSibling();
        OnboardingTutorial.NotifyRecipeScreenOpened();
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
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
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
        RectTransform canvasRect = parent as RectTransform;
        HudTabColors.SizeOverlayPanel(panelRt, canvasRect);
        float panelWidth = panelRt.sizeDelta.x;
        panel.GetComponent<Image>().color = GameUITheme.Panel;

        TextMeshProUGUI title = CreateAbsoluteText(panel.transform, "Title", "Recipes  |  Drag to Pan  |  Scroll to Zoom", 18f, FontStyles.Bold,
            new Vector2(22f, -56f), new Vector2(-220f, -12f), TextAlignmentOptions.Left);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.enableAutoSizing = true;
        title.fontSizeMin = 12f;
        title.fontSizeMax = 18f;
        title.overflowMode = TextOverflowModes.Overflow;
        TextMeshProUGUI closeLabel;
        Button close = CreateAbsoluteButton(panel.transform, "Close", "X", new Vector2(-58f, -54f),
            new Vector2(-14f, -14f), GameUITheme.Danger, out closeLabel);
        close.onClick.AddListener(() => { CloseRecipes(); Sfx.Play(SfxId.UiClick); });

        recipeZoomLabel = CreateAbsoluteText(panel.transform, "Zoom", "100%", 13f, FontStyles.Bold,
            new Vector2(390f, -46f), new Vector2(470f, -2f), TextAlignmentOptions.Top);
        recipeZoomLabel.rectTransform.anchorMin = recipeZoomLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        recipeZoomLabel.rectTransform.pivot = new Vector2(0f, 1f);

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
        recipeChartContainer = chart.transform;
        recipeChartRoot = recipeChartContainer;
        recipeGraphDrag = viewport.AddComponent<RecipeGraphDrag>();
        recipeGraphDrag.content = chartRt;
        recipeGraphDrag.zoomLabel = recipeZoomLabel;
        GameUITheme.ApplyTo(recipesOverlay.transform);
    }

    void ShowRecipeTab(CustomerOrderConfig menu, int tab)
    {
        if (recipeChartContainer == null || recipeGraphDrag == null) return;
        tab = Mathf.Clamp(tab, 0, recipeTabRoots.Length - 1);
        activeRecipeTab = tab;
        recipeChartMenu = menu;
        if (recipeDetailsText != null)
            recipeDetailsText.text = "SELECT A RECIPE\n\nClick a card to inspect its ingredients, station, unlock, and value.";

        for (int i = 0; i < recipeTabRoots.Length; i++)
        {
            if (recipeTabRoots[i] != null)
                recipeTabRoots[i].SetActive(i == tab);
        }

        GameObject tabRoot = recipeTabRoots[tab];
        bool firstBuild = tabRoot == null;
        if (firstBuild)
        {
            tabRoot = new GameObject("RecipeTab_" + tab, typeof(RectTransform));
            tabRoot.transform.SetParent(recipeChartContainer, false);
            RectTransform tabRect = (RectTransform)tabRoot.transform;
            tabRect.anchorMin = tabRect.anchorMax = tabRect.pivot = new Vector2(0.5f, 0.5f);
            tabRect.sizeDelta = new Vector2(2000f, 2400f);
            tabRect.anchoredPosition = Vector2.zero;
            recipeTabRoots[tab] = tabRoot;
        }

        recipeChartRoot = tabRoot.transform;
        RectTransform chart = (RectTransform)recipeChartRoot;
        chart.anchoredPosition = Vector2.zero;
        chart.localScale = Vector3.one;
        recipeGraphDrag.content = chart;

        recipeGraphNodes.Clear();
        recipeGraphEdges.Clear();
        foreach (RecipeGraphNodeView node in recipeChartRoot.GetComponentsInChildren<RecipeGraphNodeView>(true))
            if (node != null) recipeGraphNodes.Add(node);
        foreach (RecipeGraphEdgeView edge in recipeChartRoot.GetComponentsInChildren<RecipeGraphEdgeView>(true))
            if (edge != null) recipeGraphEdges.Add(edge);

        for (int i = 0; i < recipeTabButtons.Count; i++)
            GameUITheme.ApplyTabButton(recipeTabButtons[i], i == tab);

        if (firstBuild)
        {
            if (tab == 0) BuildBurgerTree(menu);
            else if (tab == 1) BuildFriesTree(menu);
            else BuildShakeTree(menu);
        }
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
        var rawPatty = AddGraphNode(menu.rawPattyIngredient, "RAW PATTY", "FREEZER", new Vector2(-330f, 560f));
        var cookedPatty = AddGraphNode(menu.cookedPattyIngredient, "COOKED PATTY", "GRILL", new Vector2(-330f, 390f));
        ConnectGraphNodes(rawPatty, cookedPatty, "COOK");

        var bun = AddGraphNode(FindRecipeInput(menu, menu.burgerBase, false), "BUN", "PANTRY", new Vector2(-120f, 390f));
        var burger = AddGraphNode(menu.burgerBase, "HAMBURGER", "ASSEMBLY", new Vector2(-330f, 190f), true);
        ConnectGraphNodes(cookedPatty, burger, null, -28f);
        ConnectGraphNodes(bun, burger, "ASSEMBLE", 28f);

        ItemDefinition rawCheese = menu.cheeseIngredient;
        ItemDefinition slicedCheese = menu.slicedCheeseIngredient;
        var rawCheeseNode = AddGraphNode(rawCheese, "RAW CHEESE", "PANTRY", new Vector2(60f, 560f));
        var slicedCheeseNode = AddGraphNode(slicedCheese, "SLICED CHEESE", "CUTTING", new Vector2(60f, 390f));
        ConnectGraphNodes(rawCheeseNode, slicedCheeseNode, "SLICE");
        var burgerWithCheese = AddGraphNode(FindGrillOutput(menu, menu.cheeseburgerItem),
            "BURGER WITH CHEESE", "GRILL", new Vector2(-60f, 190f), true);
        ConnectGraphNodes(rawPatty, burgerWithCheese, "GRILL", -28f);
        ConnectGraphNodes(slicedCheeseNode, burgerWithCheese, "ADD CHEESE", 28f);

        var cheeseburger = AddGraphNode(menu.cheeseburgerItem, "CHEESEBURGER", "ASSEMBLY",
            new Vector2(-60f, -20f), true);
        ConnectGraphNodes(burgerWithCheese, cheeseburger, null, -28f);
        ConnectGraphNodes(bun, cheeseburger, "ASSEMBLE", 28f);

        var baconSlab = AddGraphNode(menu.baconSlabIngredient, "BACON SLAB", "FREEZER", new Vector2(330f, 190f));
        var cutBacon = AddGraphNode(menu.cutBaconIngredient, "CUT BACON", "CUTTING", new Vector2(330f, 20f));
        var cookedBacon = AddGraphNode(menu.cookedBaconIngredient, "COOKED BACON", "GRILL", new Vector2(330f, -150f));
        var cb = AddGraphNode(menu.cheeseBaconBurgerItem, "BACON CHEESEBURGER", "ASSEMBLY", new Vector2(190f, -690f), true);
        ConnectGraphNodes(baconSlab, cutBacon, "CUT"); ConnectGraphNodes(cutBacon, cookedBacon, "COOK");
        ConnectGraphNodes(burgerWithCheese, cb, null, -46f);
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
        AssemblyRecipeDefinition recipe = FindConfiguredAssemblyRecipe(menu, menu.drinkItem);
        var milk = AddGraphNode(recipe != null ? recipe.processedInput : null, "MILK", "FREEZER", new Vector2(-230f, 260f));
        var cup = AddGraphNode(recipe != null ? recipe.pantryInput : null, "EMPTY CUP", "PANTRY", new Vector2(230f, 260f));
        var shake = AddGraphNode(menu.drinkItem, "SHAKE", "SHAKE STATION", new Vector2(0f, -20f), true);
        ConnectGraphNodes(milk, shake);
        ConnectGraphNodes(cup, shake, "COMBINE");
    }

    static AssemblyRecipeDefinition FindConfiguredAssemblyRecipe(CustomerOrderConfig menu, ItemDefinition output)
    {
        if (menu == null || output == null || menu.assemblyRecipes == null) return null;
        for (int i = 0; i < menu.assemblyRecipes.Count; i++)
        {
            AssemblyRecipeDefinition recipe = menu.assemblyRecipes[i];
            if (recipe != null && recipe.Produces(output)) return recipe;
        }
        return null;
    }

    ItemDefinition FindRecipeInput(CustomerOrderConfig menu, ItemDefinition output, bool processed)
    {
        AssemblyRecipeDefinition recipe = menu != null ? menu.GetAssemblyRecipe(output) : null;
        if (recipe == null) return null;
        return processed ? recipe.processedInput : recipe.pantryInput;
    }

    ItemDefinition FindGrillOutput(CustomerOrderConfig menu, ItemDefinition finalProduct)
    {
        if (menu == null || finalProduct == null) return null;
        List<ItemDefinition> chain = menu.GetAssemblyChain(finalProduct);
        if (chain == null || chain.Count == 0) return null;
        AssemblyRecipeDefinition first = menu.GetAssemblyRecipe(chain[0]);
        return first != null ? first.processedInput : null;
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
            + (locked ? "\n<color=#D8B365>Research: " + CustomerOrderConfig.RecipeResearchTitleForStage(unlockMilestone) + "</color>" : "");
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
            Vector2 labelPosition;
            if (!TryFindGraphActionLabelPosition(start, end, middleY, from, to, out labelPosition))
                return;

            var labelGo = new GameObject("Edge_" + action, typeof(RectTransform), typeof(Image));
            labelGo.transform.SetParent(recipeChartRoot, false);
            RectTransform rt = (RectTransform)labelGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(72f, 18f);
            rt.anchoredPosition = labelPosition;
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

    bool TryFindGraphActionLabelPosition(Vector2 start, Vector2 end, float middleY,
        RectTransform from, RectTransform to, out Vector2 position)
    {
        const float labelWidth = 72f;
        const float labelHeight = 18f;
        // Try several points on the horizontal connector, starting near the
        // midpoint. Skip the label if every candidate would cover a recipe node.
        float[] offsets = { 0f, -0.15f, 0.15f, -0.3f, 0.3f, -0.45f, 0.45f };
        float centerX = (start.x + end.x) * 0.5f;
        float span = end.x - start.x;
        for (int i = 0; i < offsets.Length; i++)
        {
            float horizontalOffset = Mathf.Abs(span) < labelWidth
                ? offsets[i] * (labelWidth + 24f)
                : span * offsets[i];
            Vector2 candidate = new Vector2(centerX + horizontalOffset, middleY);
            Rect labelBounds = Rect.MinMaxRect(candidate.x - labelWidth * 0.5f,
                candidate.y - labelHeight * 0.5f, candidate.x + labelWidth * 0.5f,
                candidate.y + labelHeight * 0.5f);
            bool overlapsNode = false;
            for (int nodeIndex = 0; nodeIndex < recipeGraphNodes.Count; nodeIndex++)
            {
                RectTransform node = recipeGraphNodes[nodeIndex] != null
                    ? recipeGraphNodes[nodeIndex].transform as RectTransform : null;
                if (node == null) continue;
                Vector2 nodeCenter = node.anchoredPosition;
                Rect nodeBounds = Rect.MinMaxRect(nodeCenter.x - node.sizeDelta.x * 0.5f,
                    nodeCenter.y - node.sizeDelta.y * 0.5f, nodeCenter.x + node.sizeDelta.x * 0.5f,
                    nodeCenter.y + node.sizeDelta.y * 0.5f);
                if (labelBounds.Overlaps(nodeBounds))
                {
                    overlapsNode = true;
                    break;
                }
            }
            if (overlapsNode) continue;
            position = candidate;
            return true;
        }
        position = Vector2.zero;
        return false;
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
            + (locked ? "\n<color=#D8B365>Research: " + CustomerOrderConfig.RecipeResearchTitleForStage(unlockMilestone) + "</color>" : "");
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
        Button button = go.GetComponent<Button>();
        if (name == "Close")
            GameIconLibrary.ApplyToButton(button, GameIconId.Cross, 18f, 0f, true);
        return button;
    }

    GameObject CreateSectionHeader(string label)
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
        return go;
    }

    GameObject CreateMenuGrid(int milestone)
    {
        var go = new GameObject("MenuGrid_" + milestone, typeof(RectTransform),
            typeof(LayoutElement), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(listContainer, false);

        var layout = go.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;

        var vertical = go.GetComponent<VerticalLayoutGroup>();
        vertical.spacing = 6f;
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    static GameObject CreateMenuGridRow(Transform parent)
    {
        var go = new GameObject("MenuRow", typeof(RectTransform), typeof(LayoutElement),
            typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.minHeight = 62f;

        var horizontal = go.GetComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 6f;
        horizontal.childAlignment = TextAnchor.UpperLeft;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = true;
        horizontal.childForceExpandHeight = false;
        return go;
    }

    static void CreateMenuGridSpacer(Transform parent)
    {
        var spacer = new GameObject("MenuSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        var layout = spacer.GetComponent<LayoutElement>();
        layout.minWidth = 0f;
        layout.preferredWidth = 0f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 62f;
    }

    GameObject CreateMenuToggleRow(CustomerOrderConfig menu, ItemDefinition item, Transform parent)
    {
        const float collapsedHeight = 62f;
        const float expandedHeight = 190f;

        var row = new GameObject("Sell_" + item.name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        row.transform.SetParent(parent != null ? parent : listContainer, false);
        var le = row.AddComponent<LayoutElement>();
        // Menu pairs divide the available width evenly. Explicitly clear the
        // content-driven width so long names cannot make one column wider.
        le.minWidth = 0f;
        le.preferredWidth = 0f;
        le.minHeight = collapsedHeight;
        le.preferredHeight = collapsedHeight;
        le.flexibleWidth = 1f;
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
        nameText.text = itemUnlocked ? inventory.GetDisplayName(item) : string.Empty;
        nameText.fontSize = 14;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = GameUITheme.TextPrimary;
        row.GetComponent<Image>().color = GameUITheme.Surface;
        preview.color = itemUnlocked ? Color.white : Color.clear;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 11f;
        nameText.fontSizeMax = 14f;
        nameText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) nameText.font = TMP_Settings.defaultFontAsset;

        ItemDefinition captured = item;

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

        GameObject lockedOverlay = CreateLockedCardOverlay(row.transform);
        MilestoneLockedMenuRow rowState = row.AddComponent<MilestoneLockedMenuRow>();
        rowState.item = item;
        rowState.label = nameText;
        rowState.workflowText = workflowText;
        rowState.preview = preview;
        rowState.lockVisual = lockVisual;
        rowState.rowBackground = row.GetComponent<Image>();
        rowState.toggle = toggle;
        rowState.lockedOverlay = lockedOverlay;
        return row;
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

    GameObject CreateMilestoneHeader(int milestone)
    {
        string label = milestone > 0 ? CustomerOrderConfig.RecipeResearchTitleForStage(milestone) : "Available Now";
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
        return go;
    }

    GameObject CreateSupplyGrid(int milestone)
    {
        var go = new GameObject("IngredientGrid_" + milestone, typeof(RectTransform),
            typeof(LayoutElement), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(listContainer, false);

        var layout = go.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;

        var grid = go.GetComponent<GridLayoutGroup>();
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(250f, SupplyCardHeight);
        grid.spacing = new Vector2(SupplyGridGap, SupplyGridGap);

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    GameObject CreateMenuMilestoneHeader(int milestone)
    {
        string label = milestone > 0 ? CustomerOrderConfig.RecipeResearchTitleForStage(milestone) : "Available Now";
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
        return go;
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

        var iconObject = new GameObject("KenneyLock", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(root.transform, false);
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(2f, 2f);
        iconRect.offsetMax = new Vector2(-2f, -2f);
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = GameIconLibrary.GetSprite(GameIconId.Locked);
        icon.color = color;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
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

    GameObject CreateOrderRow(ItemDefinition item, Transform parent)
    {
        var row = new GameObject("Order_" + item.name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = SupplyCardHeight;
        le.preferredHeight = SupplyCardHeight;
        var img = row.AddComponent<Image>();
        img.color = GameUITheme.Surface;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(5, 5, 3, 3);
        hlg.spacing = 2;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childForceExpandWidth = false;
        hlg.childControlHeight = true;

        var infoGo = new GameObject("Info", typeof(RectTransform));
        infoGo.transform.SetParent(row.transform, false);
        var infoLayout = infoGo.AddComponent<LayoutElement>();
        infoLayout.minWidth = 62f;
        infoLayout.flexibleWidth = 1f;
        var infoTmp = infoGo.AddComponent<TextMeshProUGUI>();
        infoTmp.fontSize = 12;
        infoTmp.color = GameUITheme.TextPrimary;
        infoTmp.alignment = TextAlignmentOptions.Left;
        if (TMP_Settings.defaultFontAsset != null) infoTmp.font = TMP_Settings.defaultFontAsset;

        var stockGo = new GameObject("Stock", typeof(RectTransform));
        stockGo.transform.SetParent(row.transform, false);
        var stockLayout = stockGo.AddComponent<LayoutElement>();
        stockLayout.minWidth = 48f;
        stockLayout.preferredWidth = 48f;
        var stockTmp = stockGo.AddComponent<TextMeshProUGUI>();
        stockTmp.fontSize = 11;
        stockTmp.color = GameUITheme.TextSecondary;
        stockTmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) stockTmp.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI removeLabel;
        Button removeButton = CreateCartButton(row.transform, "Remove", "-", 28f, GameUITheme.Danger, out removeLabel);
        SetCompactCartButton(removeButton, 28f, 30f);

        var countGo = new GameObject("CartCount", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        countGo.transform.SetParent(row.transform, false);
        var countLe = countGo.GetComponent<LayoutElement>();
        countLe.minWidth = 28f;
        countLe.preferredWidth = 28f;
        var countTmp = countGo.GetComponent<TextMeshProUGUI>();
        countTmp.fontSize = 11;
        countTmp.fontStyle = FontStyles.Bold;
        countTmp.color = Color.white;
        countTmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) countTmp.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI addLabel;
        Button addButton = CreateCartButton(row.transform, "Add", "+", 28f, GameUITheme.Positive, out addLabel);
        SetCompactCartButton(addButton, 28f, 30f);

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

        GameObject lockedOverlay = CreateLockedCardOverlay(row.transform);
        var binder = row.AddComponent<IngredientOrderRow>();
        binder.item = item;
        binder.infoText = infoTmp;
        binder.stockText = stockTmp;
        binder.removeButton = removeButton;
        binder.addButton = addButton;
        binder.cartCountText = countTmp;
        binder.lockedOverlay = lockedOverlay;
        return row;
    }

    static GameObject CreateLockedCardOverlay(Transform parent)
    {
        var go = new GameObject("LockedOverlay", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var layout = go.GetComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var image = go.GetComponent<Image>();
        image.color = new Color(GameUITheme.Backdrop.r, GameUITheme.Backdrop.g,
            GameUITheme.Backdrop.b, 1f);
        image.raycastTarget = true;

        var lockIcon = CreateLockIcon(go.transform, "Lock", GameUITheme.Accent);
        lockIcon.anchorMin = lockIcon.anchorMax = new Vector2(0.5f, 0.5f);
        lockIcon.pivot = new Vector2(0.5f, 0.5f);
        lockIcon.anchoredPosition = Vector2.zero;
        lockIcon.sizeDelta = new Vector2(36f, 38f);
        var lockLayout = lockIcon.GetComponent<LayoutElement>();
        if (lockLayout != null)
        {
            lockLayout.ignoreLayout = true;
            lockLayout.minWidth = lockLayout.preferredWidth = 36f;
            lockLayout.minHeight = lockLayout.preferredHeight = 38f;
        }

        go.SetActive(false);
        return go;
    }

    static void SetCompactCartButton(Button button, float width, float height)
    {
        if (button == null) return;
        var layout = button.GetComponent<LayoutElement>();
        if (layout == null) layout = button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.fontSize = 15f;
    }

    static void ConfigureCartActionLabel(TextMeshProUGUI label)
    {
        if (label == null) return;
        // The footer has a fixed row height, so keep labels on one line and
        // let TMP reduce the font slightly for longer states such as
        // "Research" instead of clipping them with an ellipsis.
        label.fontSize = 14f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10.5f;
        label.fontSizeMax = 14f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
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
        Button button = go.GetComponent<Button>();
        switch (objectName)
        {
            case "Remove":
                GameIconLibrary.ApplyToButton(button, GameIconId.Minus, 18f, 0f, true);
                break;
            case "Add":
            case "AddAll":
                GameIconLibrary.ApplyToButton(button, GameIconId.Plus, 18f, 7f, objectName == "Add");
                break;
            case "ClearCart":
                // This control is intentionally icon-only so the compact cart footer
                // does not spend its width on a repeated "Clear" label.
                GameIconLibrary.ApplyToButton(button, GameIconId.Trash, 17f, 0f, true);
                break;
            case "PlaceOrder":
                GameIconLibrary.ApplyToButton(button, GameIconId.Cart, 18f, 8f);
                break;
            case "ExpressOrder":
                GameIconLibrary.ApplyToButton(button, GameIconId.FastForward, 18f, 8f);
                break;
            case "SuppliesTab":
                GameIconLibrary.ApplyToButton(button, GameIconId.Cart, 22f, 8f);
                break;
            case "MenuTab":
                GameIconLibrary.ApplyToButton(button, GameIconId.MenuGrid, 22f, 8f);
                break;
            case "OpenRecipes":
                GameIconLibrary.ApplyToButton(button, GameIconId.MenuList, 22f, 0f, true);
                break;
        }
        return button;
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
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var summaryGo = new GameObject("Summary", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        summaryGo.transform.SetParent(footer.transform, false);
        var summaryLayout = summaryGo.GetComponent<LayoutElement>();
        summaryLayout.minWidth = 110f;
        summaryLayout.preferredWidth = 110f;
        summaryLayout.flexibleWidth = 0f;
        cartSummaryText = summaryGo.GetComponent<TextMeshProUGUI>();
        cartSummaryText.fontSize = 14f;
        cartSummaryText.alignment = TextAlignmentOptions.Left;
        cartSummaryText.color = GameUITheme.TextPrimary;
        if (TMP_Settings.defaultFontAsset != null) cartSummaryText.font = TMP_Settings.defaultFontAsset;

        TextMeshProUGUI addAllLabel;
        addAllButton = CreateCartButton(footer.transform, "AddAll", "Add All", 92, GameUITheme.Positive, out addAllLabel);
        addAllButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
        ConfigureCartActionLabel(addAllLabel);
        addAllButton.onClick.AddListener(() =>
        {
            if (inventory == null || (IngredientDeliveryService.Instance != null && IngredientDeliveryService.Instance.HasPending)) return;
            foreach (var item in inventory.GetOrderableItems())
                if (item != null) SetCartPacks(item, GetCartPacks(item) + 1);
            SelectSection(false);
            RefreshAll();
        });
        TextMeshProUGUI clearLabel;
        clearCartButton = CreateCartButton(footer.transform, "ClearCart", string.Empty, 38f, GameUITheme.Danger, out clearLabel);
        clearCartButton.GetComponent<LayoutElement>().flexibleWidth = 0f;
        clearCartButton.onClick.AddListener(() =>
        {
            cartPacks.Clear();
            RefreshAll();
        });

        placeOrderButton = CreateCartButton(footer.transform, "PlaceOrder", "Order", 102f, GameUITheme.Positive, out placeOrderLabel);
        placeOrderButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
        ConfigureCartActionLabel(placeOrderLabel);
        placeOrderButton.onClick.AddListener(SubmitCart);

        expressOrderButton = CreateCartButton(footer.transform, "ExpressOrder", "Express", 102f, GameUITheme.Accent, out expressOrderLabel);
        expressOrderButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
        ConfigureCartActionLabel(expressOrderLabel);
        expressOrderButton.onClick.AddListener(SubmitExpressCart);
        expressLockedOverlay = CreateLockedCardOverlay(expressOrderButton.transform);
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
        else if (builtCatalogHash != GetCatalogHash())
        {
            RebuildRows();
            return;
        }

        if (moneyHintText != null)
            moneyHintText.text = money != null ? ("Money: $" + money.CurrentMoney) : "Money: —";

        if (listContainer == null || inventory == null) return;
        listContainer.gameObject.SetActive(true);
        ClearListBackgrounds();
        foreach (GameObject child in menuRows)
        {
            if (child == null) continue;
            MilestoneLockedMenuRow row = child.GetComponent<MilestoneLockedMenuRow>();
            if (row == null || row.item == null || inventory.orderConfig == null) continue;
            bool unlocked = inventory.orderConfig.IsMenuItemUnlocked(row.item);
            if (row.label != null)
            {
                row.label.text = unlocked ? inventory.GetDisplayName(row.item) : string.Empty;
                row.label.color = GameUITheme.TextPrimary;
            }
            if (row.rowBackground != null)
                row.rowBackground.color = GameUITheme.Surface;
            if (row.preview != null)
                row.preview.color = unlocked ? Color.white : Color.clear;
            if (row.lockVisual != null) row.lockVisual.alpha = 0f;
            if (row.lockedOverlay != null) row.lockedOverlay.SetActive(!unlocked);
            if (row.workflowText != null)
                row.workflowText.text = unlocked
                    ? GetWorkflowDescription(inventory.orderConfig, row.item)
                    : string.Empty;
            if (row.toggle != null) row.toggle.interactable = unlocked;
            if (row.toggle != null && row.toggle.targetGraphic is Image toggleBackground)
                toggleBackground.color = unlocked ? GameUITheme.Charcoal : GameUITheme.PanelSlate;
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
            int stock = inventory.GetCount(row.item);

            int incoming = 0;
            if (delivery != null)
                incoming = delivery.GetIncomingCount(row.item);

            if (row.infoText != null)
            {
                row.infoText.text = itemUnlocked ? name + "\nPack of " + pack + "  |  $" + price : string.Empty;
                row.infoText.color = GameUITheme.TextPrimary;
            }
            Image rowImage = child.GetComponent<Image>();
            if (rowImage != null)
                rowImage.color = GameUITheme.Surface;
            if (row.lockVisual != null) row.lockVisual.alpha = 0f;
            if (row.lockedOverlay != null) row.lockedOverlay.SetActive(!itemUnlocked);
            if (row.stockText != null)
            {
                row.stockText.text = !itemUnlocked ? string.Empty : incoming > 0
                    ? "Stock\n" + stock + "\n+" + incoming + " incoming"
                    : "Stock\n" + stock;
                row.stockText.color = GameUITheme.TextSecondary;
            }
            if (!itemUnlocked) SetCartPacks(row.item, 0);
            int selected = itemUnlocked ? GetCartPacks(row.item) : 0;
            cartPackCount += selected;
            cartTotal += selected * price;
            if (row.cartCountText != null) row.cartCountText.text = itemUnlocked ? "x" + selected : string.Empty;
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
                ? "Delivering  |  Arrives " + IngredientDeliveryService.FormatCountdown(Mathf.Max(0f, remaining))
                : "Build cart, then order.";
            deliveryStatusText.color = deliveryActive
                ? GameUITheme.Accent : GameUITheme.TextSecondary;
        }
        if (cartSummaryText != null)
            cartSummaryText.text = cartPackCount == 0 ? "Empty" : "x" + cartPackCount + "  $" + cartTotal;
        if (clearCartButton != null)
            clearCartButton.interactable = !deliveryActive && cartPackCount > 0;
        if (placeOrderButton != null)
            placeOrderButton.interactable = !deliveryActive && cartPackCount > 0 && affordable
                && cartLockMilestone == 0;
        if (placeOrderLabel != null)
            placeOrderLabel.text = deliveryActive ? "Busy"
                : cartLockMilestone > 0 ? "Research"
                : !affordable && cartPackCount > 0 ? "Funds" : "Order";
        bool expressUnlocked = MilestoneFeatures.IsResearchComplete("express_ordering");
        if (expressLockedOverlay != null)
            expressLockedOverlay.SetActive(!expressUnlocked);
        if (expressOrderButton != null)
            expressOrderButton.interactable = expressUnlocked && !deliveryActive && cartPackCount > 0 && expressAffordable
                && cartLockMilestone == 0;
        if (expressOrderLabel != null)
            expressOrderLabel.text = !expressUnlocked ? "Research"
                : deliveryActive ? "Busy"
                : cartLockMilestone > 0 ? "Research"
                : !expressAffordable && cartPackCount > 0 ? "Funds"
                : "Express";
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
        float cost = CalculateRecipeCost(recipeChartMenu, item, new HashSet<ItemDefinition>());
        float value = Mathf.Max(0f, item.price);
        float profit = value - cost;
        if (recipe == null)
        {
            recipeDetailsText.text = "<b>" + title + "</b>\n\nIngredient or processed component.\n\n"
                + FormatEconomics(cost, value, profit);
            return;
        }

        string inputs = "";
        if (recipe.processedInput != null) inputs += "• " + recipe.processedInput.itemName + " ×" + Mathf.Max(1, recipe.processedInputAmount) + "\n";
        if (recipe.pantryInput != null) inputs += "• " + recipe.pantryInput.itemName + " ×" + Mathf.Max(1, recipe.pantryInputAmount) + "\n";
        if (recipe.thirdInput != null) inputs += "• " + recipe.thirdInput.itemName + " ×" + Mathf.Max(1, recipe.thirdInputAmount) + "\n";
        int researchStage = recipeChartMenu.GetMenuItemUnlockMilestone(item);
        recipeDetailsText.text = "<b>" + title + "</b>\n\n<b>INPUTS</b>\n" + inputs
            + "\n<b>STATION</b>\n" + (recipe.RequiresMk2 ? "Assembly Station MK2" : "Assembly Station MK1 or MK2")
            + "\n\n" + FormatEconomics(cost, value, profit)
            + (researchStage > 0 ? "\n<b>RESEARCH</b>  " + CustomerOrderConfig.RecipeResearchTitleForStage(researchStage) : "");
    }

    static string FormatEconomics(float cost, float value, float profit)
    {
        string profitColor = profit >= 0f ? "#7DDB8A" : "#E07A7A";
        return "<b>COST</b>  $" + cost.ToString("0.00")
            + "\n<b>VALUE</b>  $" + value.ToString("0.00")
            + "\n<b>PROFIT</b>  <color=" + profitColor + ">$" + profit.ToString("0.00") + "</color>";
    }

    static float CalculateRecipeCost(CustomerOrderConfig menu, ItemDefinition item, HashSet<ItemDefinition> visited)
    {
        if (menu == null || item == null || visited == null || !visited.Add(item)) return 0f;
        AssemblyRecipeDefinition recipe = menu.GetAssemblyRecipe(item);
        if (recipe == null)
        {
            CuttingRecipeDefinition cutting = menu.GetCuttingRecipe(item);
            if (cutting != null && cutting.input != null)
                return CalculateRecipeCost(menu, cutting.input, visited);
            StationProcessingRecipeDefinition grill = menu.GetGrillRecipe(item);
            if (grill != null && grill.input != null)
                return CalculateRecipeCost(menu, grill.input, visited);
            StationProcessingRecipeDefinition fryer = menu.GetFryerRecipe(item);
            if (fryer != null && fryer.input != null)
                return CalculateRecipeCost(menu, fryer.input, visited);
            return WorkflowAnalysis.GetIngredientUnitCost(item);
        }

        float cost = 0f;
        cost += CalculateComponentCost(menu, recipe.processedInput, recipe.processedInputAmount, visited);
        cost += CalculateComponentCost(menu, recipe.rawPantryInput != null ? recipe.rawPantryInput : recipe.pantryInput,
            recipe.pantryInputAmount, visited);
        cost += CalculateComponentCost(menu, recipe.rawThirdInput != null ? recipe.rawThirdInput : recipe.thirdInput,
            recipe.thirdInputAmount, visited);
        return cost;
    }

    static float CalculateComponentCost(CustomerOrderConfig menu, ItemDefinition item, int amount,
        HashSet<ItemDefinition> visited)
    {
        if (item == null) return 0f;
        return Mathf.Max(1, amount) * CalculateRecipeCost(menu, item, visited);
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
    public GameObject lockedOverlay;
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
    public GameObject lockedOverlay;
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
    public float edgePadding = 8f;
    public bool allowPan = true;
    Vector2 startPointer;
    Vector2 startContent;

    void OnEnable() => ActiveGraphs.Add(this);
    void OnDisable() => ActiveGraphs.Remove(this);
    void OnDestroy() => ActiveGraphs.Remove(this);

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (content == null || !allowPan) return;
        startPointer = eventData.position;
        startContent = content.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (content == null || !allowPan) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        Vector2 next = startContent + (eventData.position - startPointer) / scale;
        content.anchoredPosition = ClampContentPosition(next);
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
        content.anchoredPosition = ClampContentPosition(pointer + (position - pointer) * ratio);
        RefreshZoomLabel();
    }

    Vector2 ClampContentPosition(Vector2 position)
    {
        if (content == null) return position;
        RectTransform viewport = transform as RectTransform;
        if (viewport == null) return position;

        float zoom = Mathf.Max(0.01f, content.localScale.x);
        Vector2 viewportSize = viewport.rect.size;
        Vector2 contentSize = content.rect.size * zoom;
        float maxX = Mathf.Max(0f, (contentSize.x - viewportSize.x) * 0.5f + edgePadding);
        float maxY = Mathf.Max(0f, (contentSize.y - viewportSize.y) * 0.5f + edgePadding);
        return new Vector2(Mathf.Clamp(position.x, -maxX, maxX),
            Mathf.Clamp(position.y, -maxY, maxY));
    }

    public void RefreshZoomLabel()
    {
        if (zoomLabel != null && content != null)
            zoomLabel.text = "(" + Mathf.RoundToInt(content.localScale.x * 100f) + "%)";
    }
}
