using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public const string ContentBoxName = "ContentBox";
    public const string TabBarName = "TabBar";
    public const string StationsPanelName = "StationsPanel";
    public const string FloorPanelName = "FloorPanel";

    public GameModeManager modeManager;
    public InventoryManager inventory;
    public GridManager grid;
    public MoneyManager money;

    [Header("Build Mode Button")]
    public Button inventoryButton;
    public GameObject panel;

    [Header("List")]
    public Transform contentParent;
    [Tooltip("Square card prefab (InventoryItemCard). Create via Game → Setup Inventory Item Cards.")]
    public GameObject rowPrefab;
    public bool useSquareCards = true;
    [Tooltip("When true, GridLayoutGroup cell size/spacing on Content are left alone for scene editing.")]
    public bool useSceneGridLayout = true;
    public Vector2 cardCellSize = new Vector2(230f, 250f);
    public Vector2 cardSpacing = new Vector2(12f, 12f);

    [Header("Tabs (scene)")]
    [Tooltip("When true, ContentBox / TabBar layout is left alone so you can edit it in the scene.")]
    public bool useSceneLayout = true;
    public Button[] tabButtons;
    public GameObject[] tabPanels;

    [Header("Grid Expansion")]
    [Tooltip("Cost to expand the kitchen floor by expandWidth x expandHeight cells.")]
    public int expandCost = 150;
    [Tooltip("Cells added along width (+X) per purchase.")]
    public int expandWidth = 1;
    [Tooltip("Cells added along depth (+Z) per purchase.")]
    public int expandHeight = 1;

    Button expandButton;
    Button undoExpandButton;
    TextMeshProUGUI expandLabel;
    TextMeshProUGUI undoExpandLabel;
    TextMeshProUGUI expandStatusText;
    bool expandUiBuilt;
    Transform floorContentParent;
    int activeTab;

    void Start()
    {
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();

        if (inventoryButton)
            inventoryButton.onClick.AddListener(TogglePanel);

        BindOrBuildTabs(forceDefaultLayout: false);
        EnsureExpandUi();
        EnsureUndoFooter();
        WireTabButtons();
        RefreshAll();
        SelectTab(0);
        ApplyModeState();
    }

    void Update()
    {
        ApplyModeState();
        RefreshExpandButton();
    }

    void ApplyModeState()
    {
        if (modeManager == null || panel == null) return;

        if (panel.activeSelf)
        {
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt != null && mgmt.IsOpen) mgmt.Close();
            modeManager.SetMode(GameModeManager.Mode.Build);
        }
        else if (modeManager.CurrentMode == GameModeManager.Mode.Build)
        {
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt == null || !mgmt.IsOpen)
                modeManager.SetMode(GameModeManager.Mode.Play);
        }
    }

    public void TogglePanel()
    {
        if (!panel) return;
        bool opening = !panel.activeSelf;
        panel.SetActive(opening);
        Sfx.Play(opening ? SfxId.UiOpen : SfxId.UiClose);
        if (opening)
        {
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt != null && mgmt.IsOpen)
                mgmt.Close();
            BindOrBuildTabs(forceDefaultLayout: false);
            EnsureExpandUi();
            EnsureUndoFooter();
            WireTabButtons();
            RefreshAll();
            SelectTab(activeTab);
            RefreshExpandButton();
        }
    }

    public bool IsPanelOpen => panel != null && panel.activeSelf;

    /// <summary>Editor / runtime: create Inventory ContentBox + tabs under the panel.</summary>
    public void SetupSceneTabs(bool forceDefaultLayout = true)
    {
        BindOrBuildTabs(forceDefaultLayout);
        EnsureStationsScrollSetup();
        EnsureStationsGrid(forceDefaultLayout);
        EnsureExpandUi();
        EnsureUndoFooter();
        WireTabButtons();
        SelectTab(0);
    }

    public void SelectTab(int index)
    {
        BindOrBuildTabs(forceDefaultLayout: false);
        if (tabPanels == null || tabPanels.Length == 0) return;

        activeTab = Mathf.Clamp(index, 0, tabPanels.Length - 1);
        for (int i = 0; i < tabPanels.Length; i++)
        {
            if (tabPanels[i] != null)
                tabPanels[i].SetActive(i == activeTab);
        }

        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
                HudTabColors.Apply(tabButtons[i], i == activeTab);
        }

        if (activeTab == 0)
            RefreshAll();
        else
            RefreshExpandButton();
    }

    public void RefreshAll()
    {
        if (!inventory || !contentParent) return;

        EnsureStationsScrollSetup();
        EnsureStationsGrid();

        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            var child = contentParent.GetChild(i);
            if (child != null && child.name == "ExpandFloorRow") continue;
            DestroyObject(child.gameObject);
        }

        foreach (var item in inventory.allItems)
        {
            if (item == null) continue;

            if (useSquareCards || rowPrefab == null || rowPrefab.GetComponent<InventoryItemCardUI>() != null)
                CreateSquareCard(item);
            else
                CreateLegacyRow(item);
        }

        Canvas.ForceUpdateCanvases();
        if (contentParent is RectTransform contentRt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);

        var sr = contentParent.GetComponentInParent<ScrollRect>();
        if (sr != null)
            sr.normalizedPosition = new Vector2(0f, 1f);
    }

    /// <summary>Editor / runtime: configure the stations Content as a 2-column grid.</summary>
    public void SetupStationsGrid(bool forceDefaultLayout = true)
    {
        EnsureStationsScrollSetup();
        EnsureStationsGrid(forceDefaultLayout);
    }

    /// <summary>
    /// Repair Scroll View hierarchy so Content lives under Viewport and gets clipped.
    /// </summary>
    public void EnsureStationsScrollSetup()
    {
        if (panel == null && contentParent == null) return;

        RectTransform scroll = null;
        if (contentParent != null)
            scroll = contentParent.GetComponentInParent<ScrollRect>()?.transform as RectTransform;

        if (scroll == null && panel != null)
        {
            scroll = panel.transform.Find(ContentBoxName + "/" + StationsPanelName + "/Scroll View") as RectTransform
                ?? panel.transform.Find(StationsPanelName + "/Scroll View") as RectTransform
                ?? panel.transform.Find("Scroll View") as RectTransform;
        }

        if (scroll == null) return;

        var scrollRect = scroll.GetComponent<ScrollRect>();
        if (scrollRect == null)
            scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

        // Viewport must fill the scroll view and clip children.
        Transform viewportT = scroll.Find("Viewport");
        if (viewportT == null)
        {
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportT = vpGo.transform;
            viewportT.SetParent(scroll, false);
        }

        var viewport = viewportT as RectTransform;
        StretchFull(viewport);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;

        // Prefer RectMask2D — Mask + near-zero alpha (or missing sprite) can hide all children.
        var legacyMask = viewportT.GetComponent<Mask>();
        if (legacyMask != null)
            DestroyObject(legacyMask);

        if (viewportT.GetComponent<RectMask2D>() == null)
            viewportT.gameObject.AddComponent<RectMask2D>();

        var vpImage = viewportT.GetComponent<Image>();
        if (vpImage == null) vpImage = viewportT.gameObject.AddComponent<Image>();
        // RectMask2D does not need a stencil sprite; keep a transparent raycast target.
        vpImage.color = new Color(1f, 1f, 1f, 0f);
        vpImage.raycastTarget = true;

        // Content must be a child of Viewport.
        RectTransform content = null;
        if (contentParent != null)
            content = contentParent as RectTransform;
        if (content == null)
            content = viewport.Find("Content") as RectTransform;
        if (content == null)
            content = scroll.Find("Content") as RectTransform;

        if (content == null)
        {
            var contentGo = new GameObject("Content", typeof(RectTransform));
            content = contentGo.transform as RectTransform;
            content.SetParent(viewport, false);
        }

        if (content.parent != viewport)
            content.SetParent(viewport, false);

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        // Width follows viewport; height comes from ContentSizeFitter + grid.
        content.sizeDelta = new Vector2(0f, Mathf.Max(content.sizeDelta.y, 1f));
        content.localScale = Vector3.one;
        contentParent = content;

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        // Keep scroll view filling the stations panel.
        if (scroll.parent != null && scroll.parent.name == StationsPanelName)
        {
            StretchFull(scroll);
            scroll.offsetMin = Vector2.zero;
            scroll.offsetMax = Vector2.zero;
        }
    }

    void EnsureStationsGrid(bool forceDefaultLayout = false)
    {
        if (contentParent == null) return;

        var vertical = contentParent.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
        {
            vertical.enabled = false;
            DestroyObject(vertical);
        }

        var grid = contentParent.GetComponent<GridLayoutGroup>();
        bool created = grid == null;
        if (created)
            grid = contentParent.gameObject.AddComponent<GridLayoutGroup>();

        float viewportWidth = 560f;
        var scrollRect = contentParent.GetComponentInParent<ScrollRect>();
        if (scrollRect != null && scrollRect.viewport != null)
            viewportWidth = Mathf.Max(120f, scrollRect.viewport.rect.width);

        // Fit two columns inside the viewport with padding/spacing.
        float pad = 24f;
        float gap = cardSpacing.x;
        float cellW = Mathf.Floor((viewportWidth - pad - gap) * 0.5f);
        cellW = Mathf.Clamp(cellW, 120f, 280f);
        float cellH = cellW + 30f;

        if (forceDefaultLayout || !useSceneGridLayout || created)
        {
            grid.cellSize = new Vector2(cellW, cellH);
            grid.spacing = cardSpacing;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.padding = new RectOffset(12, 12, 12, 12);
            cardCellSize = grid.cellSize;
        }
        else
        {
            // Keep scene-tuned height, but always fit two columns to the viewport width.
            float height = grid.cellSize.y > 1f ? grid.cellSize.y : cellH;
            grid.cellSize = new Vector2(cellW, height);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
        }

        var fitter = contentParent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = contentParent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void CreateSquareCard(ItemDefinition item)
    {
        InventoryItemCardUI card = null;
        if (rowPrefab != null && rowPrefab.GetComponent<InventoryItemCardUI>() != null)
        {
            card = Object.Instantiate(rowPrefab, contentParent).GetComponent<InventoryItemCardUI>();
            card.gameObject.SetActive(true);
        }
        else
        {
            card = InventoryItemCardBuilder.Create(contentParent);
        }

        ItemDefinition captured = item;
        card.Bind(
            captured,
            inventory.GetCount(captured),
            onSelect: () =>
            {
                inventory.SelectItem(captured);
                var placer = FindObjectOfType<BuildPlacer>();
                if (placer) placer.BeginPlacement(captured);
            },
            onBuy: () =>
            {
                bool bought = inventory.PurchaseOne(captured);
                if (bought)
                {
                    card.SetQuantity(inventory.GetCount(captured));
                    Sfx.Play(SfxId.Purchase);
                }
                else
                    Sfx.Play(SfxId.UiError);
            });
    }

    void CreateLegacyRow(ItemDefinition item)
    {
        var row = Instantiate(rowPrefab, contentParent);

        var nameButton = row.transform.Find("NameButton").GetComponent<Button>();
        var nameText = row.transform.Find("NameButton").GetComponentInChildren<TextMeshProUGUI>(true);
        var qtyText = row.transform.Find("QtyBack/QtyText").GetComponent<TextMeshProUGUI>();
        var buyButton = row.transform.Find("BuyButton").GetComponent<Button>();
        var priceText = row.transform.Find("PriceBack/Price").GetComponent<TextMeshProUGUI>();

        nameText.text = item.itemName;
        qtyText.text = inventory.GetCount(item).ToString();
        priceText.text = "$" + item.price;

        ItemDefinition captured = item;
        nameButton.onClick.AddListener(() =>
        {
            inventory.SelectItem(captured);
            var placer = FindObjectOfType<BuildPlacer>();
            if (placer) placer.BeginPlacement(captured);
        });

        buyButton.onClick.AddListener(() =>
        {
            bool bought = inventory.PurchaseOne(captured);
            if (bought)
            {
                qtyText.text = inventory.GetCount(captured).ToString();
                Sfx.Play(SfxId.Purchase);
            }
            else
                Sfx.Play(SfxId.UiError);
        });
    }

    void BindOrBuildTabs(bool forceDefaultLayout)
    {
        if (panel == null) return;

        if (TryBindExistingTabs() && useSceneLayout && !forceDefaultLayout)
            return;

        BuildTabsHierarchy(forceDefaultLayout || !useSceneLayout);
        TryBindExistingTabs();
    }

    bool TryBindExistingTabs()
    {
        if (panel == null) return false;

        var contentBox = panel.transform.Find(ContentBoxName);
        if (contentBox == null) return false;

        var tabBar = contentBox.Find(TabBarName);
        var stationsPanel = contentBox.Find(StationsPanelName);
        var floorPanel = contentBox.Find(FloorPanelName);
        if (tabBar == null || stationsPanel == null || floorPanel == null)
            return false;

        var stationsTab = tabBar.Find("Tab_Stations")?.GetComponent<Button>();
        var floorTab = tabBar.Find("Tab_Floor")?.GetComponent<Button>();
        if (stationsTab == null || floorTab == null)
            return false;

        tabButtons = new[] { stationsTab, floorTab };
        tabPanels = new[] { stationsPanel.gameObject, floorPanel.gameObject };
        floorContentParent = floorPanel.Find("FloorContent") ?? floorPanel;

        var scrollContent = stationsPanel.Find("Scroll View/Viewport/Content")
            ?? stationsPanel.Find("Scroll View/Content");
        if (scrollContent != null)
            contentParent = scrollContent;

        return true;
    }

    void BuildTabsHierarchy(bool applyDefaultLayout)
    {
        var scroll = panel.transform.Find("Scroll View") as RectTransform;
        if (scroll == null)
            scroll = panel.transform.Find(ContentBoxName + "/" + StationsPanelName + "/Scroll View") as RectTransform;

        Transform contentBox = panel.transform.Find(ContentBoxName);
        if (contentBox == null)
        {
            var boxGo = new GameObject(ContentBoxName, typeof(RectTransform), typeof(Image));
            contentBox = boxGo.transform;
            contentBox.SetParent(panel.transform, false);

            var boxImg = boxGo.GetComponent<Image>();
            boxImg.color = HudTabColors.Panel;
            boxImg.raycastTarget = true;
            applyDefaultLayout = true;
        }

        if (applyDefaultLayout)
        {
            var boxRt = (RectTransform)contentBox;
            boxRt.anchorMin = new Vector2(1f, 0f);
            boxRt.anchorMax = new Vector2(1f, 1f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.anchoredPosition = new Vector2(-300f, 0f);
            boxRt.sizeDelta = new Vector2(600f, 0f);

            var boxImg = contentBox.GetComponent<Image>();
            if (boxImg != null)
                boxImg.color = HudTabColors.Panel;
        }

        Transform stationsPanel = contentBox.Find(StationsPanelName);
        if (stationsPanel == null)
        {
            var stationsGo = new GameObject(StationsPanelName, typeof(RectTransform));
            stationsPanel = stationsGo.transform;
            stationsPanel.SetParent(contentBox, false);
            StretchFull((RectTransform)stationsPanel);
            ((RectTransform)stationsPanel).offsetMax = new Vector2(0f, -86f);
            ((RectTransform)stationsPanel).offsetMin = new Vector2(0f, 52f);
        }

        if (scroll != null && scroll.parent != stationsPanel)
        {
            scroll.SetParent(stationsPanel, false);
            StretchFull(scroll);
            scroll.offsetMin = Vector2.zero;
            scroll.offsetMax = Vector2.zero;
        }

        Transform floorPanel = contentBox.Find(FloorPanelName);
        if (floorPanel == null)
        {
            var floorGo = new GameObject(FloorPanelName, typeof(RectTransform), typeof(Image));
            floorPanel = floorGo.transform;
            floorPanel.SetParent(contentBox, false);
            StretchFull((RectTransform)floorPanel);
            ((RectTransform)floorPanel).offsetMax = new Vector2(0f, -86f);
            ((RectTransform)floorPanel).offsetMin = new Vector2(20f, 72f);

            var floorImg = floorGo.GetComponent<Image>();
            floorImg.color = new Color(0f, 0f, 0f, 0f);
            floorImg.raycastTarget = false;

            var listGo = new GameObject("FloorContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(floorPanel, false);
            var listRt = (RectTransform)listGo.transform;
            listRt.anchorMin = new Vector2(0f, 1f);
            listRt.anchorMax = new Vector2(1f, 1f);
            listRt.pivot = new Vector2(0.5f, 1f);
            listRt.anchoredPosition = Vector2.zero;
            listRt.sizeDelta = new Vector2(-40f, 0f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            floorContentParent = listGo.transform;
        }
        else
        {
            floorContentParent = floorPanel.Find("FloorContent") ?? floorPanel;
        }

        Transform tabBar = contentBox.Find(TabBarName);
        if (tabBar == null)
        {
            var tabGo = new GameObject(TabBarName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            tabBar = tabGo.transform;
            tabBar.SetParent(contentBox, false);
            tabBar.SetAsFirstSibling();

            var tabRt = (RectTransform)tabBar;
            tabRt.anchorMin = new Vector2(0f, 1f);
            tabRt.anchorMax = new Vector2(1f, 1f);
            tabRt.pivot = new Vector2(0.5f, 1f);
            tabRt.anchoredPosition = new Vector2(0f, -12f);
            tabRt.sizeDelta = new Vector2(-40f, 70f);

            var tabBg = tabGo.GetComponent<Image>();
            tabBg.color = HudTabColors.Strip;
            tabBg.raycastTarget = true;

            var hlg = tabGo.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 4, 4);
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
        }

        EnsureTabButton(tabBar, "Tab_Stations", "Stations");
        EnsureTabButton(tabBar, "Tab_Floor", "Floor");

        tabButtons = new[]
        {
            tabBar.Find("Tab_Stations").GetComponent<Button>(),
            tabBar.Find("Tab_Floor").GetComponent<Button>()
        };
        tabPanels = new[] { stationsPanel.gameObject, floorPanel.gameObject };
    }

    void WireTabButtons()
    {
        if (tabButtons == null) return;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            int index = i;
            tabButtons[i].onClick.RemoveAllListeners();
            tabButtons[i].onClick.AddListener(() =>
            {
                if (Application.isPlaying)
                    Sfx.Play(SfxId.UiClick);
                SelectTab(index);
            });
        }
    }

    static Button EnsureTabButton(Transform tabBar, string objectName, string label)
    {
        var existing = tabBar.Find(objectName);
        GameObject go;
        if (existing != null)
            go = existing.gameObject;
        else
        {
            go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(tabBar, false);

            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 52f;
            le.flexibleWidth = 1f;

            var img = go.GetComponent<Image>();
            img.color = HudTabColors.Idle;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            StretchFull((RectTransform)textGo.transform);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
        }

        return go.GetComponent<Button>();
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void EnsureExpandUi()
    {
        BindOrBuildTabs(forceDefaultLayout: false);
        if (expandUiBuilt && expandButton != null) return;

        Transform parent = floorContentParent != null
            ? floorContentParent
            : contentParent != null ? contentParent : panel != null ? panel.transform : null;
        if (parent == null) return;

        if (contentParent != null)
        {
            var legacy = contentParent.Find("ExpandFloorRow") ?? contentParent.Find("ExpandFloorBar");
            if (legacy != null && legacy.parent != parent)
                DestroyObject(legacy.gameObject);
        }

        Transform existing = parent.Find("ExpandFloorRow") ?? parent.Find("ExpandFloorBar");

        GameObject barGo;
        if (existing != null)
        {
            barGo = existing.gameObject;
        }
        else
        {
            barGo = new GameObject("ExpandFloorRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            barGo.transform.SetParent(parent, false);
            barGo.transform.SetAsLastSibling();

            var bg = barGo.GetComponent<Image>();
            bg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
            bg.raycastTarget = true;

            var layout = barGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        var rowLe = barGo.GetComponent<LayoutElement>();
        if (rowLe != null)
        {
            rowLe.minHeight = 168f;
            rowLe.preferredHeight = 168f;
        }

        expandStatusText = barGo.transform.Find("Status")?.GetComponent<TextMeshProUGUI>();
        if (expandStatusText == null)
        {
            var statusGo = new GameObject("Status", typeof(RectTransform));
            statusGo.transform.SetParent(barGo.transform, false);
            expandStatusText = statusGo.AddComponent<TextMeshProUGUI>();
            expandStatusText.fontSize = 16;
            expandStatusText.alignment = TextAlignmentOptions.Center;
            expandStatusText.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            if (TMP_Settings.defaultFontAsset != null)
                expandStatusText.font = TMP_Settings.defaultFontAsset;
            var statusLe = statusGo.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 24f;
        }

        expandButton = barGo.transform.Find("ExpandButton")?.GetComponent<Button>();
        if (expandButton == null)
        {
            var btnGo = new GameObject("ExpandButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(barGo.transform, false);
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = new Color(0.22f, 0.55f, 0.38f, 1f);
            expandButton = btnGo.GetComponent<Button>();
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 44f;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
            StretchFull((RectTransform)labelGo.transform);
            expandLabel = labelGo.AddComponent<TextMeshProUGUI>();
            expandLabel.fontSize = 16;
            expandLabel.alignment = TextAlignmentOptions.Center;
            expandLabel.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                expandLabel.font = TMP_Settings.defaultFontAsset;
        }
        else
        {
            expandLabel = expandButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        expandButton.onClick.RemoveListener(OnExpandClicked);
        expandButton.onClick.AddListener(OnExpandClicked);

        undoExpandButton = barGo.transform.Find("UndoExpandButton")?.GetComponent<Button>();
        if (undoExpandButton == null)
        {
            var undoGo = new GameObject("UndoExpandButton", typeof(RectTransform), typeof(Image), typeof(Button));
            undoGo.transform.SetParent(barGo.transform, false);
            var undoImg = undoGo.GetComponent<Image>();
            undoImg.color = new Color(0.28f, 0.32f, 0.42f, 1f);
            undoExpandButton = undoGo.GetComponent<Button>();
            var undoLe = undoGo.AddComponent<LayoutElement>();
            undoLe.preferredHeight = 44f;

            var undoLabelGo = new GameObject("Label", typeof(RectTransform));
            undoLabelGo.transform.SetParent(undoGo.transform, false);
            StretchFull((RectTransform)undoLabelGo.transform);
            undoExpandLabel = undoLabelGo.AddComponent<TextMeshProUGUI>();
            undoExpandLabel.fontSize = 16;
            undoExpandLabel.alignment = TextAlignmentOptions.Center;
            undoExpandLabel.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                undoExpandLabel.font = TMP_Settings.defaultFontAsset;
        }
        else
        {
            undoExpandLabel = undoExpandButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        undoExpandButton.onClick.RemoveListener(OnUndoFloorClicked);
        undoExpandButton.onClick.AddListener(OnUndoFloorClicked);

        expandUiBuilt = true;
        RefreshExpandButton();
    }

    void EnsureUndoFooter()
    {
        if (panel == null) return;

        var contentBox = panel.transform.Find(ContentBoxName);
        var scroll = contentBox != null
            ? contentBox.Find(StationsPanelName + "/Scroll View") as RectTransform
            : panel.transform.Find("Scroll View") as RectTransform;

        if (scroll != null)
        {
            PurchaseUndoFooter.EnsureMatching(scroll);
        }
        else
        {
            PurchaseUndoFooter.EnsureOnPanel(panel.transform);
        }

        var floorPanel = contentBox != null ? contentBox.Find(FloorPanelName) : null;
        if (floorPanel != null)
        {
            var leftover = floorPanel.Find(PurchaseUndoFooter.ObjectName);
            if (leftover != null)
                DestroyObject(leftover.gameObject);
        }
    }

    void RefreshExpandButton()
    {
        if (!expandUiBuilt) return;
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();

        int w = grid != null ? grid.Width : 0;
        int h = grid != null ? grid.Height : 0;
        bool canSize = grid != null && grid.CanExpand(expandWidth, expandHeight);
        bool canPay = money != null && money.CanAfford(expandCost);
        bool can = canSize && canPay;

        if (expandStatusText != null)
        {
            if (grid == null)
                expandStatusText.text = "Grid: —";
            else if (!canSize)
                expandStatusText.text = $"Floor {w}×{h} (max reached)";
            else
                expandStatusText.text = $"Floor {w}×{h}  →  {w + expandWidth}×{h + expandHeight}";
        }

        if (expandLabel != null)
            expandLabel.text = canSize ? $"Expand Floor  ${expandCost}" : "Floor Maxed";

        if (expandButton != null)
            expandButton.interactable = can;

        var undo = PurchaseUndoManager.Instance != null ? PurchaseUndoManager.Instance : PurchaseUndoManager.Ensure();
        bool canUndoFloor = undo != null && undo.CanUndoFloor;
        if (undoExpandLabel != null)
            undoExpandLabel.text = canUndoFloor ? undo.PeekFloorLabel : "Undo Floor";
        if (undoExpandButton != null)
            undoExpandButton.interactable = canUndoFloor;
    }

    void OnUndoFloorClicked()
    {
        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
            undo.TryUndoLastFloor();
        RefreshExpandButton();
    }

    void OnExpandClicked()
    {
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();
        if (grid == null || money == null) return;

        if (!grid.CanExpand(expandWidth, expandHeight))
        {
            RefreshExpandButton();
            return;
        }
        if (!money.TrySpend(expandCost))
        {
            RefreshExpandButton();
            Sfx.Play(SfxId.UiError);
            return;
        }
        Sfx.Play(SfxId.SpendMoney);

        if (!grid.TryExpand(expandWidth, expandHeight))
        {
            money.AddMoney(expandCost);
            Sfx.Play(SfxId.UiError);
        }
        else
        {
            Sfx.Play(SfxId.GridExpand);
            var undo = PurchaseUndoManager.Ensure();
            if (undo != null)
                undo.RecordFloorExpand(expandWidth, expandHeight, expandCost);
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.FloorExpanded);
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.CapacityInvested);
        }

        RefreshExpandButton();
    }

    static void DestroyObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
