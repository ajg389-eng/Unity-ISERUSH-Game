using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rate-based supply planning screen. The older IngredientsOrderUI remains in
/// the project as a legacy screen; this component is the active business tab.
/// </summary>
public class SupplyPlanningScreenUI : MonoBehaviour
{
    sealed class IngredientRow
    {
        public ItemDefinition item;
        public bool unlocked;
        public TMP_Text status;
        public TMP_Text forecast;
        public TMP_Text rate;
        public TMP_Text projection;
        public TMP_Text lockedText;
        public Button minus;
        public Button plus;
        public Button orderPack;
        public GameObject lockedOverlay;
    }

    readonly Dictionary<ItemDefinition, int> pendingRates = new Dictionary<ItemDefinition, int>();
    readonly List<IngredientRow> rows = new List<IngredientRow>();

    KitchenInventory inventory;
    MoneyManager money;
    CustomerOrderConfig orderConfig;
    TMP_Text capacityText;
    TMP_Text capacityHintText;
    TMP_Text upgradeLabel;
    TMP_Text summaryText;
    TMP_Text applyLabel;
    TMP_Text statusText;
    Button upgradeButton;
    Button balanceButton;
    Button applyButton;
    ScrollRect scrollRect;
    float nextRefreshAt;
    bool built;
    bool planDirty;
    bool wasPlanning;
    bool planningStateInitialized;

    public Button TutorialPlaceOrderButton => applyButton;
    internal Component TutorialCapacityTarget => capacityText;
    internal Component TutorialBalanceRatesTarget => balanceButton;
    internal Button TutorialOrderPackTarget => rows.Find(row =>
        row.orderPack.isActiveAndEnabled && row.orderPack.interactable)?.orderPack;

    public Button TutorialAddButton(ItemDefinition item)
    {
        IngredientRow row = FindRow(item);
        return row != null ? row.plus : null;
    }

    public int TutorialCartPacks(ItemDefinition item)
    {
        if (item == null) return 0;
        if (!pendingRates.TryGetValue(item, out int rate))
            rate = inventory != null ? inventory.GetSupplyRatePerMinute(item) : 0;
        return rate > 0 ? 1 : 0;
    }

    public static SupplyPlanningScreenUI EnsureOn(GameObject panel)
    {
        if (panel == null) return null;
        SupplyPlanningScreenUI screen = panel.GetComponent<SupplyPlanningScreenUI>();
        if (screen == null) screen = panel.AddComponent<SupplyPlanningScreenUI>();
        screen.EnsureBuilt();
        return screen;
    }

    void Awake()
    {
        inventory = KitchenInventory.Instance;
        EnsureBuilt();
    }

    void OnEnable()
    {
        EnsureBuilt();
        RefreshData();
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy || Time.unscaledTime < nextRefreshAt) return;
        nextRefreshAt = Time.unscaledTime + 0.4f;
        if (!built)
        {
            EnsureBuilt();
            return;
        }
        RefreshData();
    }

    public void RefreshScreen()
    {
        EnsureBuilt();
        RefreshData();
    }

    void EnsureBuilt()
    {
        if (built) return;
        inventory = KitchenInventory.Instance != null ? KitchenInventory.Instance : FindObjectOfType<KitchenInventory>();
        if (inventory == null) return;
        orderConfig = ProductionManager.Instance != null ? ProductionManager.Instance.orderConfig : inventory.orderConfig;
        BuildScreen();
        built = true;
        wasPlanning = ShiftPlanningUI.IsPlanning;
        planningStateInitialized = true;
        SetFooter(wasPlanning
            ? "Rates are saved when you apply the plan."
            : "Rates are locked during the shift. You can still order packs.");
        RefreshData();
    }

    void BuildScreen()
    {
        RectTransform root = transform as RectTransform;
        if (root == null) return;
        root.name = "SupplyPlanningPanel";

        Image background = GetComponent<Image>();
        if (background == null) background = gameObject.AddComponent<Image>();
        background.color = GameUITheme.Panel;
        background.raycastTarget = true;
        if (background.GetComponent<GameUIThemeStyled>() == null)
            background.gameObject.AddComponent<GameUIThemeStyled>();

        CreateText("SupplyTitle", transform, "SUPPLY PLAN", 24f, GameUITheme.Accent,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -44f), new Vector2(-16f, -8f), FontStyles.Bold);
        CreateText("SupplyDescription", transform,
            "Plan incoming rates against forecast demand.",
            15f, GameUITheme.TextSecondary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -75f), new Vector2(-16f, -48f), FontStyles.Normal);

        GameObject capacityCard = CreatePanel("SupplierCapacity", transform, GameUITheme.Surface,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -164f), new Vector2(-12f, -89f));
        capacityText = CreateText("CapacityValue", capacityCard.transform, "Supplier capacity 10/10 items/min",
            18f, GameUITheme.TextPrimary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(12f, -31f), new Vector2(-178f, -8f), FontStyles.Bold);
        capacityHintText = CreateText("CapacityHint", capacityCard.transform, "Shared supplier cap; stock holds up to 100 each",
            13f, GameUITheme.TextSecondary, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(12f, 5f), new Vector2(-178f, 28f), FontStyles.Normal);
        upgradeButton = CreateButton("UpgradeSupplier", capacityCard.transform, "UPGRADE",
            GameUITheme.Positive, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-84f, 0f), new Vector2(154f, 48f), out upgradeLabel);
        upgradeButton.onClick.AddListener(UpgradeSupplier);

        summaryText = CreateText("SupplySummary", transform,
            "Shift need 0 items  |  Est. supply cost $0",
            14f, GameUITheme.TextPrimary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -194f), new Vector2(-154f, -166f), FontStyles.Bold);
        balanceButton = CreateButton("BalanceToForecast", transform, "BALANCE RATES",
            GameUITheme.Surface, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-78f, -180f), new Vector2(136f, 28f), out _);
        balanceButton.onClick.AddListener(BalanceRatesToForecast);

        BuildIngredientColumns();

        applyButton = CreateButton("ApplySupplyPlan", transform, "APPLY SUPPLY PLAN",
            GameUITheme.Positive, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(12f, 12f), new Vector2(-12f, 58f), out applyLabel);
        applyButton.onClick.AddListener(ApplyPlan);
        statusText = CreateText("PlanStatus", transform, "Rates are saved when you apply the plan.",
            12f, GameUITheme.TextSecondary, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(12f, 60f), new Vector2(-12f, 82f), FontStyles.Normal);
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 10f;
        statusText.fontSizeMax = 12f;

        GameUITheme.ApplyTo(transform);
        background.color = GameUITheme.Panel;
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null && scrollRect.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
    }

    void BuildIngredientColumns()
    {
        GameObject scrollObject = new GameObject("IngredientForecast", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(transform, false);
        RectTransform scrollRectTransform = (RectTransform)scrollObject.transform;
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(12f, 90f);
        scrollRectTransform.offsetMax = new Vector2(-12f, -204f);
        Image scrollBackground = scrollObject.GetComponent<Image>();
        scrollBackground.color = GameUITheme.Charcoal;
        scrollBackground.raycastTarget = false;
        scrollBackground.gameObject.AddComponent<GameUIThemeStyled>();

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewportRect = (RectTransform)viewportObject.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = GameUITheme.Charcoal;
        viewportImage.raycastTarget = true;
        viewportImage.gameObject.AddComponent<GameUIThemeStyled>();

        GameObject contentObject = new GameObject("Columns", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform contentRect = (RectTransform)contentObject.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        HorizontalLayoutGroup columnsLayout = contentObject.GetComponent<HorizontalLayoutGroup>();
        columnsLayout.padding = new RectOffset(6, 6, 6, 6);
        columnsLayout.spacing = 8f;
        columnsLayout.childAlignment = TextAnchor.UpperCenter;
        columnsLayout.childControlWidth = true;
        columnsLayout.childForceExpandWidth = true;
        columnsLayout.childControlHeight = true;
        columnsLayout.childForceExpandHeight = false;
        ContentSizeFitter columnsFitter = contentObject.GetComponent<ContentSizeFitter>();
        columnsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        columnsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        GameUITheme.ConfigureScroll(scrollRect);

        Transform pantry = CreateColumn(contentObject.transform, "PANTRY INGREDIENTS");
        Transform freezer = CreateColumn(contentObject.transform, "FREEZER INGREDIENTS");
        IReadOnlyList<ItemDefinition> items = inventory.GetIngredientCatalogItems();
        foreach (ItemDefinition item in items)
        {
            if (item == null) continue;
            bool isFreezer = orderConfig != null
                && orderConfig.GetVendorSupplyStation(item) == StationType.Freezer;
            CreateIngredientRow(item, isFreezer ? freezer : pantry);
        }
    }

    Transform CreateColumn(Transform parent, string title)
    {
        GameObject column = new GameObject(title.Replace(' ', '_'), typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        column.transform.SetParent(parent, false);
        LayoutElement width = column.AddComponent<LayoutElement>();
        width.minWidth = 190f;
        width.flexibleWidth = 1f;
        VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(2, 2, 0, 8);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = column.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject heading = CreatePanel("ColumnHeader", column.transform, GameUITheme.Panel,
            new Vector2(0f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        LayoutElement headerLayout = heading.AddComponent<LayoutElement>();
        headerLayout.minHeight = headerLayout.preferredHeight = 36f;
        CreateText("HeaderText", heading.transform, title, 15f, GameUITheme.Accent,
            Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f), FontStyles.Bold);
        return column.transform;
    }

    void CreateIngredientRow(ItemDefinition item, Transform parent)
    {
        GameObject card = CreatePanel("Ingredient_" + item.name, parent, GameUITheme.Surface,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        LayoutElement cardLayout = card.AddComponent<LayoutElement>();
        cardLayout.minHeight = cardLayout.preferredHeight = 192f;

        GameObject iconObject = new GameObject("ItemIcon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(card.transform, false);
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = new Vector2(0f, 1f);
        iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = new Vector2(8f, -8f);
        iconRect.sizeDelta = new Vector2(48f, 48f);
        Image iconBackground = iconObject.GetComponent<Image>();
        iconBackground.color = GameUITheme.Charcoal;
        iconBackground.raycastTarget = false;
        iconBackground.gameObject.AddComponent<GameUIThemeStyled>();

        GameObject rawPreviewObject = new GameObject("ModelPreview", typeof(RectTransform), typeof(RawImage));
        rawPreviewObject.transform.SetParent(iconObject.transform, false);
        RectTransform rawPreviewRect = (RectTransform)rawPreviewObject.transform;
        rawPreviewRect.anchorMin = Vector2.zero;
        rawPreviewRect.anchorMax = Vector2.one;
        rawPreviewRect.offsetMin = new Vector2(2f, 2f);
        rawPreviewRect.offsetMax = new Vector2(-2f, -2f);
        RawImage icon = rawPreviewObject.GetComponent<RawImage>();
        Texture itemTexture = ItemPreviewThumbnails.Get(item);
        icon.texture = itemTexture;
        icon.enabled = itemTexture != null;
        icon.color = Color.white;
        icon.raycastTarget = false;

        GameObject spritePreviewObject = new GameObject("SpritePreview", typeof(RectTransform), typeof(Image));
        spritePreviewObject.transform.SetParent(iconObject.transform, false);
        RectTransform spritePreviewRect = (RectTransform)spritePreviewObject.transform;
        spritePreviewRect.anchorMin = Vector2.zero;
        spritePreviewRect.anchorMax = Vector2.one;
        spritePreviewRect.offsetMin = new Vector2(2f, 2f);
        spritePreviewRect.offsetMax = new Vector2(-2f, -2f);
        Image spritePreview = spritePreviewObject.GetComponent<Image>();
        spritePreview.sprite = item.previewIcon;
        spritePreview.preserveAspect = true;
        spritePreview.enabled = itemTexture == null && item.previewIcon != null;
        spritePreview.raycastTarget = false;

        string itemName = inventory.GetDisplayName(item);
        TMP_Text name = CreateText("ItemName", card.transform, itemName, 15f, GameUITheme.TextPrimary,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(64f, -54f), new Vector2(-8f, -8f), FontStyles.Bold);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.enableAutoSizing = true;
        name.fontSizeMin = 12f;
        name.fontSizeMax = 15f;

        IngredientRow row = new IngredientRow
        {
            item = item,
            unlocked = inventory.IsOrderItemUnlocked(item)
        };
        if (!pendingRates.ContainsKey(item))
            pendingRates[item] = inventory.GetSupplyRatePerMinute(item);

        row.status = CreateText("Stock", card.transform, "Stock 0/100",
            13f, GameUITheme.TextSecondary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(8f, -78f), new Vector2(-8f, -60f), FontStyles.Normal);
        row.forecast = CreateText("Forecast", card.transform, "Need 0/shift  |  0/3 shifts",
            11f, GameUITheme.TextSecondary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(8f, -98f), new Vector2(-8f, -80f), FontStyles.Normal);
        row.projection = CreateText("Projection", card.transform, "End stock 0/100",
            11f, GameUITheme.TextSecondary, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(8f, -118f), new Vector2(-8f, -100f), FontStyles.Normal);
        ConfigureCompactLine(row.status, 11f, 13f);
        ConfigureCompactLine(row.forecast, 10f, 12f);
        ConfigureCompactLine(row.projection, 10f, 12f);

        row.minus = CreateButton("RateMinus", card.transform, "-", GameUITheme.Danger,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(25f, 48f), new Vector2(34f, 28f), out _);
        row.rate = CreateText("Rate", card.transform, "0/min", 14f, GameUITheme.TextPrimary,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(47f, 34f), new Vector2(104f, 62f), FontStyles.Bold);
        row.rate.alignment = TextAlignmentOptions.Center;
        row.plus = CreateButton("RatePlus", card.transform, "+", GameUITheme.Positive,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(129f, 48f), new Vector2(34f, 28f), out _);
        row.minus.onClick.AddListener(() => AdjustRate(row, -5));
        row.plus.onClick.AddListener(() => AdjustRate(row, 5));
        row.orderPack = CreateButton("OrderPack", card.transform,
            $"Order {inventory.GetPackSize(item)} · ${inventory.GetPackPrice(item)}", GameUITheme.Surface,
            Vector2.zero, new Vector2(1f, 0f), new Vector2(8f, 4f), new Vector2(-8f, 32f), out TMP_Text orderLabel);
        ConfigureCompactLine(orderLabel, 10f, 13f);
        row.orderPack.onClick.AddListener(() => OrderPack(row));
        DelayedTooltip.Attach(row.orderPack, "Buy one ingredient pack. It arrives by van during planning or during the shift.");

        row.lockedOverlay = CreatePanel("LockStatus", card.transform,
            new Color(GameUITheme.Charcoal.r, GameUITheme.Charcoal.g, GameUITheme.Charcoal.b, 0.92f),
            new Vector2(0f, 0f), new Vector2(1f, 0.54f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
        Image overlay = row.lockedOverlay.GetComponent<Image>();
        overlay.raycastTarget = false;
        overlay.transform.SetAsLastSibling();
        row.lockedText = CreateText("LockLabel", row.lockedOverlay.transform,
            "Unlock at Milestone " + inventory.GetOrderItemUnlockMilestone(item), 12f,
            GameUITheme.Accent, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f), FontStyles.Bold);
        row.lockedText.alignment = TextAlignmentOptions.Center;
        ConfigureCompactLine(row.lockedText, 10f, 12f);
        row.lockedOverlay.SetActive(!row.unlocked);

        rows.Add(row);
    }

    void AdjustRate(IngredientRow row, int delta)
    {
        if (!ShiftPlanningUI.IsPlanning || row == null || !row.unlocked || inventory == null) return;
        int current = pendingRates.TryGetValue(row.item, out int value)
            ? value : inventory.GetSupplyRatePerMinute(row.item);
        int next = Mathf.Clamp(current + delta, 0, KitchenInventory.MaxSupplyRatePerMinute);
        if (delta > 0)
        {
            // The staged rate for every other row plus this row must fit the shared limit.
            int otherPending = TotalPendingRate() - current;
            if (otherPending + next > inventory.SupplierCapacityPerMinute) return;
        }
        pendingRates[row.item] = next;
        planDirty = true;
        RefreshData();
    }

    void OrderPack(IngredientRow row)
    {
        if (inventory == null || row == null || !row.orderPack.interactable) return;
        if (!inventory.TryOrderPack(row.item))
        {
            SetFooter("Order unavailable. Check space, cash, and pending deliveries.");
            RefreshData();
            return;
        }

        string item = inventory.GetDisplayName(row.item);
        SetFooter(ShiftPlanningUI.IsPlanning ? $"Ordered {item}. Watch for the van before opening."
            : $"Ordered {item}. Close Kitchen to resume the shift and watch for the van.");
        RefreshData();
    }

    int TotalPendingRate()
    {
        int total = 0;
        foreach (IngredientRow row in rows)
            if (row != null && row.unlocked && pendingRates.TryGetValue(row.item, out int rate))
                total += rate;
        return total;
    }

    void UpgradeSupplier()
    {
        if (!ShiftPlanningUI.IsPlanning || inventory == null) return;
        if (!inventory.TryUpgradeSupplierCapacity(out string reason))
        {
            SetFooter(reason);
            return;
        }
        SetFooter("Supplier capacity upgraded. Recheck planned rates against the new limit.");
        RefreshData();
    }

    void BalanceRatesToForecast()
    {
        if (!ShiftPlanningUI.IsPlanning || inventory == null) return;
        float shiftMinutes = ShiftPlanningUI.GetPlannedShiftDurationMinutes();
        if (shiftMinutes <= 0f) return;
        Dictionary<ItemDefinition, float> needs = ShiftPlanningUI.GetForecastIngredientNeedsPerShift();
        int capacity = inventory.SupplierCapacityPerMinute;
        var targets = new Dictionary<ItemDefinition, int>();
        int targetTotal = 0;
        foreach (IngredientRow row in rows)
        {
            if (row == null || !row.unlocked) continue;
            float need = needs != null && needs.TryGetValue(row.item, out float count) ? Mathf.Max(0f, count) : 0f;
            int target = Mathf.Clamp(Mathf.CeilToInt((need / shiftMinutes) / 5f) * 5,
                0, KitchenInventory.MaxSupplyRatePerMinute);
            targets[row.item] = target;
            targetTotal += target;
        }

        if (targetTotal <= capacity)
        {
            foreach (KeyValuePair<ItemDefinition, int> pair in targets)
                pendingRates[pair.Key] = pair.Value;
        }
        else
        {
            int assigned = 0;
            var remainders = new List<KeyValuePair<ItemDefinition, float>>();
            foreach (KeyValuePair<ItemDefinition, int> pair in targets)
            {
                float exactSteps = pair.Value / 5f * (capacity / (float)targetTotal);
                int rate = Mathf.FloorToInt(exactSteps) * 5;
                pendingRates[pair.Key] = rate;
                assigned += rate;
                remainders.Add(new KeyValuePair<ItemDefinition, float>(pair.Key, exactSteps - Mathf.Floor(exactSteps)));
            }
            remainders.Sort((a, b) => b.Value.CompareTo(a.Value));
            int remaining = Mathf.Max(0, capacity - assigned);
            foreach (KeyValuePair<ItemDefinition, float> pair in remainders)
            {
                if (remaining < 5) break;
                if (pendingRates[pair.Key] >= targets[pair.Key]) continue;
                pendingRates[pair.Key] += 5;
                remaining -= 5;
            }
        }

        planDirty = true;
        SetFooter("Rates were balanced to this shift's forecast within supplier capacity. Apply to activate them.");
        RefreshData();
    }

    void ApplyPlan()
    {
        if (!ShiftPlanningUI.IsPlanning || inventory == null) return;
        // Free capacity first, then allocate the newly available throughput.
        foreach (IngredientRow row in rows)
        {
            if (row == null || !row.unlocked) continue;
            if (pendingRates.TryGetValue(row.item, out int rate)
                && rate < inventory.GetSupplyRatePerMinute(row.item))
                inventory.SetSupplyRatePerMinute(row.item, rate);
        }
        foreach (IngredientRow row in rows)
        {
            if (row == null || !row.unlocked) continue;
            if (pendingRates.TryGetValue(row.item, out int rate)
                && rate > inventory.GetSupplyRatePerMinute(row.item))
                inventory.SetSupplyRatePerMinute(row.item, rate);
        }
        inventory.NormalizeConfiguredSupplyRatesToCapacity();
        ProductionManager.Instance?.NotifyProductionStateChanged();
        planDirty = false;
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.IngredientsOrdered);
        SetFooter("Supply plan applied. Inbound deliveries run during shifts and stop at stock capacity.");
        RefreshData();
    }

    void RefreshData()
    {
        if (inventory == null) inventory = KitchenInventory.Instance;
        if (inventory == null || !built && capacityText == null) return;
        bool planning = ShiftPlanningUI.IsPlanning;
        if (planningStateInitialized && wasPlanning && !planning)
        {
            planDirty = false;
            SetFooter("Rates are locked during the shift. You can still order packs.");
        }
        else if (planningStateInitialized && !wasPlanning && planning)
        {
            SetFooter("Plan phase is open. Review and apply supply changes before starting the shift.");
        }
        else if (!planning && !planningStateInitialized)
        {
            SetFooter("Shift underway. Supply changes are locked until the next planning phase.");
        }
        wasPlanning = planning;
        planningStateInitialized = true;
        if (orderConfig == null && ProductionManager.Instance != null)
            orderConfig = ProductionManager.Instance.orderConfig;

        foreach (IngredientRow row in rows)
        {
            if (row == null || row.item == null) continue;
            bool unlockedNow = inventory.IsOrderItemUnlocked(row.item);
            if (unlockedNow && !row.unlocked && !pendingRates.ContainsKey(row.item))
                pendingRates[row.item] = inventory.GetSupplyRatePerMinute(row.item);
            row.unlocked = unlockedNow;
            if (!planDirty)
                pendingRates[row.item] = inventory.GetSupplyRatePerMinute(row.item);
        }

        int assigned = TotalPendingRate();
        if (capacityText != null)
            capacityText.text = "Planned supplier load  " + assigned + "/" + inventory.SupplierCapacityPerMinute + " items/min";
        bool canUpgrade = inventory.CanUpgradeSupplierCapacity(out string upgradeReason);
        if (capacityHintText != null)
            capacityHintText.text = !planning
                ? "Supply settings are locked until the next planning phase."
                : canUpgrade
                ? "100 max stock per item. Apply rates to activate."
                : upgradeReason;
        if (upgradeButton != null)
        {
            upgradeButton.interactable = planning && canUpgrade;
            if (upgradeLabel != null)
            {
                upgradeLabel.text = !planning ? "PLAN PHASE ONLY"
                    : canUpgrade
                    ? "UPGRADE  " + inventory.GetNextSupplierCapacityPerMinute() + "/min  $" + inventory.GetNextSupplierUpgradeCost()
                    : inventory.SupplierCapacityTierIndex >= 5 ? "MAX CAPACITY"
                    : upgradeReason.StartsWith("Not enough cash", StringComparison.OrdinalIgnoreCase)
                        ? "NEED $" + inventory.GetNextSupplierUpgradeCost() : "LOCKED";
            }
        }

        Dictionary<ItemDefinition, float> currentForecast = ShiftPlanningUI.GetForecastIngredientNeedsPerShift();
        float shiftMinutes = Mathf.Max(0f, ShiftPlanningUI.GetPlannedShiftDurationMinutes());
        var threeShiftForecast = new Dictionary<ItemDefinition, float>();
        for (int offset = 0; offset < 3; offset++)
        {
            Dictionary<ItemDefinition, float> forecast = ShiftPlanningUI.GetForecastIngredientNeedsPerShift(offset);
            if (forecast == null) continue;
            foreach (KeyValuePair<ItemDefinition, float> pair in forecast)
            {
                if (pair.Key == null) continue;
                threeShiftForecast[pair.Key] = (threeShiftForecast.TryGetValue(pair.Key, out float existing) ? existing : 0f)
                    + Mathf.Max(0f, pair.Value);
            }
        }

        float totalNeed = 0f;
        float estimatedShiftCost = 0f;
        if (money == null) money = FindFirstObjectByType<MoneyManager>();
        bool canOrder = (planning || GameTimeManager.Instance?.IsShiftInProgress == true)
            && IngredientDeliveryService.Instance?.HasPending != true;
        foreach (IngredientRow row in rows)
        {
            if (row == null || row.item == null) continue;
            int rate = pendingRates.TryGetValue(row.item, out int pending)
                ? pending : inventory.GetSupplyRatePerMinute(row.item);
            float need = row.unlocked && currentForecast != null && currentForecast.TryGetValue(row.item, out float forecast)
                ? Mathf.Max(0f, forecast) : 0f;
            float nextThreeNeed = row.unlocked && threeShiftForecast.TryGetValue(row.item, out float futureNeed)
                ? Mathf.Max(0f, futureNeed) : 0f;
            int stock = inventory.GetCount(row.item);
            int incoming = IngredientDeliveryService.Instance != null
                ? IngredientDeliveryService.Instance.GetIncomingCount(row.item) : 0;
            float rawEndStock = stock + incoming + rate * shiftMinutes - need;
            float unitCost = inventory.GetPackPrice(row.item) / (float)inventory.GetPackSize(row.item);

            row.status.text = row.unlocked
                ? "Stock " + stock + "/" + KitchenInventory.MaxIngredientStock
                    + (incoming > 0 ? "  |  Incoming " + incoming : string.Empty)
                : "Locked ingredient";
            row.forecast.text = "Need " + Mathf.CeilToInt(need) + "/shift  |  "
                + Mathf.CeilToInt(nextThreeNeed) + "/3 shifts";
            if (rawEndStock < 0f)
            {
                row.projection.text = "Short " + Mathf.CeilToInt(-rawEndStock) + " after shift";
                row.projection.color = GameUITheme.DangerAccent;
            }
            else if (rawEndStock > KitchenInventory.MaxIngredientStock)
            {
                row.projection.text = "End stock at cap; surplus "
                    + Mathf.CeilToInt(rawEndStock - KitchenInventory.MaxIngredientStock);
                row.projection.color = GameUITheme.Accent;
            }
            else
            {
                row.projection.text = "End stock " + Mathf.FloorToInt(rawEndStock) + "/100";
                row.projection.color = GameUITheme.TextSecondary;
            }
            row.rate.text = rate + "/min";
            row.status.gameObject.SetActive(row.unlocked);
            row.forecast.gameObject.SetActive(row.unlocked);
            row.projection.gameObject.SetActive(row.unlocked);
            row.minus.gameObject.SetActive(row.unlocked);
            row.rate.gameObject.SetActive(row.unlocked);
            row.plus.gameObject.SetActive(row.unlocked);
            row.orderPack.gameObject.SetActive(row.unlocked);
            row.orderPack.interactable = canOrder && row.unlocked
                && inventory.GetAvailableStockCapacity(row.item) >= inventory.GetPackSize(row.item)
                && (money == null || money.CanAfford(inventory.GetPackPrice(row.item)));
            row.minus.interactable = planning && row.unlocked && rate > 0;
            int otherRates = assigned - rate;
            row.plus.interactable = planning && row.unlocked && rate < KitchenInventory.MaxSupplyRatePerMinute
                && otherRates + rate + 5 <= inventory.SupplierCapacityPerMinute;
            row.lockedOverlay.SetActive(!row.unlocked);
            if (!row.unlocked && row.lockedText != null)
                row.lockedText.text = "Unlock at Milestone " + inventory.GetOrderItemUnlockMilestone(row.item);

            totalNeed += need;
            estimatedShiftCost += rate * shiftMinutes * unitCost;
        }

        if (summaryText != null)
            summaryText.text = "Shift need  " + Mathf.CeilToInt(totalNeed) + " items  |  Est. supply cost  $"
                + Mathf.CeilToInt(estimatedShiftCost);
        if (applyButton != null)
        {
            bool changed = false;
            foreach (IngredientRow row in rows)
                if (row != null && row.unlocked && pendingRates.TryGetValue(row.item, out int pendingRate)
                    && pendingRate != inventory.GetSupplyRatePerMinute(row.item))
                { changed = true; break; }
            applyButton.interactable = planning && changed;
            if (applyLabel != null) applyLabel.text = "APPLY SUPPLY PLAN";
        }
        if (balanceButton != null)
            balanceButton.interactable = planning;
    }

    void SetFooter(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    static void ConfigureCompactLine(TMP_Text text, float minimum, float maximum)
    {
        if (text == null) return;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = minimum;
        text.fontSizeMax = maximum;
    }

    IngredientRow FindRow(ItemDefinition item)
    {
        if (item == null) return null;
        foreach (IngredientRow row in rows)
            if (row != null && row.item == item) return row;
        return null;
    }

    static GameObject CreatePanel(string name, Transform parent, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.color = color;
        go.AddComponent<GameUIThemeStyled>();
        return go;
    }

    static TMP_Text CreateText(string name, Transform parent, string value, float size, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        TMP_Text text = go.GetComponent<TMP_Text>();
        go.AddComponent<GameUIThemeStyled>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(string name, Transform parent, string label, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 size,
        out TMP_Text labelText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        if (anchorMin == anchorMax)
        {
            rect.anchoredPosition = offsetMin;
            rect.sizeDelta = size;
        }
        else
        {
            rect.offsetMin = offsetMin;
            rect.offsetMax = size;
        }
        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        labelText = CreateText("Label", go.transform, label, 13f, GameUITheme.TextPrimary,
            Vector2.zero, Vector2.one, new Vector2(3f, 2f), new Vector2(-3f, -2f), FontStyles.Bold);
        labelText.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
