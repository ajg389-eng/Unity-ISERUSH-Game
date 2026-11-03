using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class IngredientStockEntry
{
    public ItemDefinition item;
    public int quantity;
    public int supplyRatePerMinute;
    public float supplyUnitProgress;
    public float supplyCostRemainder;
}

/// <summary>
/// Kitchen ingredient stock. Players buy packs from the Management > Food tab.
/// Freezer/Pantry consume from this inventory when workers grab items.
/// </summary>
public class KitchenInventory : MonoBehaviour
{
    public const int ExpressDeliveryFee = 200;
    public const int MaxIngredientStock = 100;
    public const int MaxSupplyRatePerMinute = 80;

    public static KitchenInventory Instance { get; private set; }

    [Header("Catalog")]
    [Tooltip("Used to seed orderable menu items (burger, fries, drink).")]
    public CustomerOrderConfig orderConfig;
    [Tooltip("Extra orderable items beyond the order config.")]
    public List<ItemDefinition> extraOrderableItems = new List<ItemDefinition>();

    [Header("Starting stock")]
    [Tooltip("If true, each catalog item starts with its ItemDefinition.startingQuantity (or defaultStartingStock).")]
    public bool grantStartingStock = true;
    public int defaultStartingStock = 10;

    [Header("Runtime stock (read-only in play)")]
    public List<IngredientStockEntry> stock = new List<IngredientStockEntry>();

    MoneyManager money;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        money = FindObjectOfType<MoneyManager>();
        EnsureCatalogStock();
        if (grantStartingStock)
            ApplyStartingStock();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Time.timeScale <= 0f || ShiftPlanningUI.IsPlanning
            || (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftOver))
            return;

        // Rates are measured per one-minute simulation interval. Time.deltaTime
        // includes fast-forward, so the supplier scales with the rest of the shift.
        DeliverConfiguredSupplyRates(Time.deltaTime / 60f);
    }

    public IReadOnlyList<ItemDefinition> GetOrderableItems()
    {
        var list = new List<ItemDefinition>();
        void Add(ItemDefinition item)
        {
            if (CanPurchaseIngredient(item)
                && (orderConfig == null || (!orderConfig.IsDrink(item) && IsOrderItemUnlocked(item)))
                && !list.Contains(item))
                list.Add(item);
        }

        if (orderConfig != null)
        {
            foreach (var item in orderConfig.GetIngredientItems(
                         includeLocked: OnboardingTutorial.BlocksProgression))
                Add(item);
        }

        if (extraOrderableItems != null)
        {
            foreach (var i in extraOrderableItems)
                Add(i);
        }

        return list;
    }

    /// <summary>Full ingredient catalog for UI previews, including future locked items.</summary>
    public IReadOnlyList<ItemDefinition> GetIngredientCatalogItems()
    {
        var list = new List<ItemDefinition>();
        void Add(ItemDefinition item)
        {
            if (CanPurchaseIngredient(item)
                && (orderConfig == null || !orderConfig.IsDrink(item))
                && !list.Contains(item))
                list.Add(item);
        }

        if (orderConfig != null)
            foreach (ItemDefinition item in orderConfig.GetIngredientItems(includeLocked: true))
                Add(item);

        if (extraOrderableItems != null)
            foreach (ItemDefinition item in extraOrderableItems)
                Add(item);

        return list;
    }

    public bool IsOrderItemUnlocked(ItemDefinition item)
    {
        if (!CanPurchaseIngredient(item)) return false;
        if (OnboardingTutorial.BlocksProgression && orderConfig != null
            && (item == orderConfig.rawPattyIngredient || IsTutorialBun(item)))
            return true;
        return item != null && (orderConfig == null
            || (orderConfig.IsIngredientUnlocked(item) && orderConfig.IsMenuItemUnlocked(item)));
    }

    bool CanPurchaseIngredient(ItemDefinition item)
    {
        if (item == null) return false;
        if (extraOrderableItems != null && extraOrderableItems.Contains(item)) return true;
        return orderConfig == null || orderConfig.GetVendorSupplyStation(item).HasValue;
    }

    bool IsTutorialBun(ItemDefinition item)
    {
        if (item == null || orderConfig == null) return false;
        AssemblyRecipeDefinition recipe = orderConfig.GetAssemblyRecipe(orderConfig.burgerBase);
        return recipe != null && orderConfig.GetAssemblySupplySource(recipe) == item;
    }

    public int GetOrderItemUnlockMilestone(ItemDefinition item)
    {
        if (item == null || orderConfig == null) return 0;
        return Mathf.Max(orderConfig.GetIngredientUnlockMilestone(item),
            orderConfig.GetMenuItemUnlockMilestone(item));
    }

    void EnsureCatalogStock()
    {
        foreach (IngredientStockEntry entry in stock)
            if (entry != null)
                entry.quantity = Mathf.Clamp(entry.quantity, 0, MaxIngredientStock);
        foreach (var item in GetOrderableItems())
            EnsureEntry(item);
    }

    void ApplyStartingStock()
    {
        foreach (var item in GetOrderableItems())
        {
            var entry = EnsureEntry(item);
            if (entry.quantity > 0) continue;
            int start = item.startingQuantity > 0 ? item.startingQuantity : defaultStartingStock;
            entry.quantity = Mathf.Clamp(start, 0, MaxIngredientStock);
        }
    }

    IngredientStockEntry EnsureEntry(ItemDefinition item)
    {
        if (item == null) return null;
        foreach (var e in stock)
        {
            if (e != null && e.item == item)
                return e;
        }
        var created = new IngredientStockEntry { item = item, quantity = 0 };
        stock.Add(created);
        return created;
    }

    public int GetCount(ItemDefinition item)
    {
        if (item == null) return 0;
        foreach (var e in stock)
        {
            if (e != null && e.item == item)
                return Mathf.Clamp(e.quantity, 0, MaxIngredientStock);
        }

        // A newly introduced ingredient will not exist in older saves or in a
        // play session that was already running when the catalog changed.
        IngredientStockEntry created = EnsureEntry(item);
        if (grantStartingStock && created != null)
        {
            int start = item.startingQuantity > 0 ? item.startingQuantity : defaultStartingStock;
            created.quantity = Mathf.Clamp(start, 0, MaxIngredientStock);
        }
        return created != null ? Mathf.Clamp(created.quantity, 0, MaxIngredientStock) : 0;
    }

    public int GetAvailableStockCapacity(ItemDefinition item, bool includeIncoming = true)
    {
        if (item == null) return 0;
        int incoming = 0;
        IngredientDeliveryService service = IngredientDeliveryService.Instance;
        if (includeIncoming && service != null) incoming = service.GetIncomingCount(item);
        return Mathf.Max(0, MaxIngredientStock - GetCount(item) - incoming);
    }

    public bool Has(ItemDefinition item, int amount = 1)
    {
        return GetCount(item) >= amount;
    }

    public int GetSupplyRatePerMinute(ItemDefinition item)
    {
        return EnsureEntry(item)?.supplyRatePerMinute ?? 0;
    }

    public void SetSupplyRatePerMinute(ItemDefinition item, int rate)
    {
        IngredientStockEntry entry = EnsureEntry(item);
        if (entry == null) return;
        int clampedRate = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0, rate) / 5f) * 5,
            0, MaxSupplyRatePerMinute);
        entry.supplyRatePerMinute = clampedRate;
        if (clampedRate == 0)
            entry.supplyUnitProgress = 0f;
    }

    public void RestoreSupplySettings(ItemDefinition item, int rate, float unitProgress, float costRemainder)
    {
        IngredientStockEntry entry = EnsureEntry(item);
        if (entry == null) return;
        entry.supplyRatePerMinute = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0, rate) / 5f) * 5,
            0, MaxSupplyRatePerMinute);
        entry.supplyUnitProgress = Mathf.Clamp(unitProgress, 0f, 0.9999f);
        entry.supplyCostRemainder = Mathf.Clamp(costRemainder, 0f, 0.9999f);
    }

    public void ClearAllSupplySettings()
    {
        foreach (IngredientStockEntry entry in stock)
        {
            if (entry == null) continue;
            entry.supplyRatePerMinute = 0;
            entry.supplyUnitProgress = 0f;
            entry.supplyCostRemainder = 0f;
        }
    }

    void DeliverConfiguredSupplyRates(float elapsedMinutes)
    {
        if (elapsedMinutes <= 0f || !MilestoneFeatures.IsResearchComplete("automatic_ordering")) return;
        if (money == null) money = FindObjectOfType<MoneyManager>();
        bool stockChanged = false;
        for (int i = 0; i < stock.Count; i++)
        {
            IngredientStockEntry entry = stock[i];
            ItemDefinition item = entry != null ? entry.item : null;
            if (item == null || entry.supplyRatePerMinute <= 0 || !IsOrderItemUnlocked(item)) continue;

            int availableSpace = GetAvailableStockCapacity(item);
            if (availableSpace <= 0)
            {
                entry.supplyUnitProgress = Mathf.Min(entry.supplyUnitProgress, 0.9999f);
                continue;
            }

            entry.supplyUnitProgress += entry.supplyRatePerMinute * elapsedMinutes;
            int units = Mathf.Min(Mathf.FloorToInt(entry.supplyUnitProgress), availableSpace);
            if (units <= 0) continue;

            float unitCost = GetPackPrice(item) / (float)GetPackSize(item);
            float accumulatedCost = entry.supplyCostRemainder + unitCost * units;
            int charge = Mathf.FloorToInt(accumulatedCost + 0.0001f);
            if (money != null && charge > 0 && !money.TrySpend(charge))
            {
                entry.supplyUnitProgress = Mathf.Min(entry.supplyUnitProgress, 0.9999f);
                continue;
            }

            entry.quantity = Mathf.Min(MaxIngredientStock, entry.quantity + units);
            entry.supplyUnitProgress -= units;
            if (entry.supplyUnitProgress >= 1f)
                entry.supplyUnitProgress = 0.9999f;
            entry.supplyCostRemainder = Mathf.Clamp(accumulatedCost - charge, 0f, 0.9999f);
            stockChanged = true;
        }

        if (stockChanged)
            ProductionManager.Instance?.NotifyProductionStateChanged();
    }

    public bool TryConsume(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        var entry = EnsureEntry(item);
        if (entry.quantity < amount) return false;
        entry.quantity -= amount;
        ProductionManager.Instance?.NotifyProductionStateChanged();
        return true;
    }

    public void AddStock(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return;
        var entry = EnsureEntry(item);
        entry.quantity = Mathf.Min(MaxIngredientStock, Mathf.Max(0, entry.quantity) + amount);
        ProductionManager.Instance?.NotifyProductionStateChanged();
    }

    /// <summary>Debug: add amount to every catalog item.</summary>
    public void FillAllStock(int amount = 50)
    {
        EnsureCatalogStock();
        foreach (var item in GetOrderableItems())
            AddStock(item, amount);
    }

    /// <summary>Zero all ingredient stock (used by the first-run tutorial).</summary>
    public void ClearAllStock()
    {
        EnsureCatalogStock();
        foreach (var e in stock)
        {
            if (e != null)
                e.quantity = 0;
        }
        ProductionManager.Instance?.NotifyProductionStateChanged(0f);
    }

    /// <summary>Reset catalog items to their starting quantities.</summary>
    public void RestoreStartingStock()
    {
        EnsureCatalogStock();
        foreach (var item in GetOrderableItems())
        {
            var entry = EnsureEntry(item);
            int start = item != null && item.startingQuantity > 0 ? item.startingQuantity : defaultStartingStock;
            entry.quantity = Mathf.Clamp(start, 0, MaxIngredientStock);
        }
        ProductionManager.Instance?.NotifyProductionStateChanged(0f);
    }

    public int GetPackSize(ItemDefinition item)
    {
        if (item == null) return 10;
        return Mathf.Max(1, item.orderPackSize);
    }

    public int GetPackPrice(ItemDefinition item)
    {
        if (item == null) return 0;
        if (item.orderPackPrice > 0) return item.orderPackPrice;
        // Fallback: cheap wholesale vs sale price
        return Mathf.Max(1, item.price) * GetPackSize(item) / 2;
    }

    public string GetDisplayName(ItemDefinition item)
    {
        if (item == null) return "Item";
        if (!string.IsNullOrEmpty(item.itemName)) return item.itemName;
        return item.name;
    }

    /// <summary>Purchase one ingredient pack. During planning, stock is added immediately.</summary>
    public bool TryOrderPack(ItemDefinition item)
    {
        if (item == null) return false;
        var cart = new Dictionary<ItemDefinition, int> { { item, 1 } };
        return TryOrderCart(cart);
    }

    /// <summary>Pay for an ingredient cart. During planning, stock is added immediately.</summary>
    public bool TryOrderCart(IReadOnlyDictionary<ItemDefinition, int> packCounts)
    {
        return TryOrderCart(packCounts, false, true);
    }

    /// <summary>Pay for a cart and either queue normal delivery or add it to stock immediately.</summary>
    public bool TryOrderCart(IReadOnlyDictionary<ItemDefinition, int> packCounts, bool express)
    {
        return TryOrderCart(packCounts, express, true);
    }

    bool TryOrderCart(IReadOnlyDictionary<ItemDefinition, int> packCounts, bool express, bool playerInitiated)
    {
        if (packCounts == null || packCounts.Count == 0) return false;
        if (money == null) money = FindObjectOfType<MoneyManager>();

        bool instantPurchase = ShiftPlanningUI.IsPlanning;
        IngredientDeliveryService service = IngredientDeliveryService.Instance;
        if (!instantPurchase && service == null)
        {
            var host = new GameObject("IngredientDeliveryService");
            service = host.AddComponent<IngredientDeliveryService>();
        }
        if (!instantPurchase && !express && service.HasPending) return false;

        var items = new List<ItemDefinition>();
        var amounts = new List<int>();
        int totalPrice = 0;
        foreach (var pair in packCounts)
        {
            if (pair.Key == null || pair.Value <= 0) continue;
            if (!IsOrderItemUnlocked(pair.Key)) return false;
            int amount = GetPackSize(pair.Key) * pair.Value;
            if (amount > GetAvailableStockCapacity(pair.Key)) return false;
            items.Add(pair.Key);
            amounts.Add(amount);
            totalPrice += GetPackPrice(pair.Key) * pair.Value;
        }
        if (items.Count == 0) return false;

        if (express && !instantPurchase)
            totalPrice += ExpressDeliveryFee;

        if (money != null && !money.TrySpend(totalPrice))
            return false;

        bool accepted = instantPurchase;
        if (instantPurchase)
        {
            for (int i = 0; i < items.Count; i++)
                AddStock(items[i], amounts[i]);
        }
        else
        {
            accepted = express
                ? service.QueueExpressOrder(items, amounts)
                : service.QueueOrder(items, amounts);
        }
        if (!accepted)
        {
            if (money != null) money.AddMoney(totalPrice);
            return false;
        }

        if (playerInitiated)
        {
            var undo = PurchaseUndoManager.Ensure();
            if (undo != null)
                undo.RecordIngredientOrder(items, amounts, money != null ? totalPrice : 0);
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.IngredientsOrdered);
        }
        return true;
    }
}
