using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class IngredientStockEntry
{
    public ItemDefinition item;
    public int quantity;
}

/// <summary>
/// Kitchen ingredient stock. Players buy packs from the Management > Ingredients tab.
/// Freezer/Pantry consume from this inventory when workers grab items.
/// </summary>
public class KitchenInventory : MonoBehaviour
{
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

    public IReadOnlyList<ItemDefinition> GetOrderableItems()
    {
        var list = new List<ItemDefinition>();
        void Add(ItemDefinition item)
        {
            if (item != null && !list.Contains(item))
                list.Add(item);
        }

        if (orderConfig != null)
        {
            foreach (var item in orderConfig.GetMenuItems())
                Add(item);
        }

        if (extraOrderableItems != null)
        {
            foreach (var i in extraOrderableItems)
                Add(i);
        }

        return list;
    }

    void EnsureCatalogStock()
    {
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
            entry.quantity = Mathf.Max(0, start);
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
                return e.quantity;
        }
        return 0;
    }

    public bool Has(ItemDefinition item, int amount = 1)
    {
        return GetCount(item) >= amount;
    }

    public bool TryConsume(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        var entry = EnsureEntry(item);
        if (entry.quantity < amount) return false;
        entry.quantity -= amount;
        return true;
    }

    public void AddStock(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return;
        var entry = EnsureEntry(item);
        entry.quantity += amount;
    }

    /// <summary>Debug: add amount to every catalog item.</summary>
    public void FillAllStock(int amount = 50)
    {
        EnsureCatalogStock();
        foreach (var item in GetOrderableItems())
            AddStock(item, amount);
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

    /// <summary>Spend money and add one pack of the ingredient.</summary>
    public bool TryOrderPack(ItemDefinition item)
    {
        if (item == null) return false;
        if (money == null) money = FindObjectOfType<MoneyManager>();

        int price = GetPackPrice(item);
        int pack = GetPackSize(item);

        if (money != null)
        {
            if (!money.TrySpend(price))
                return false;
        }

        AddStock(item, pack);
        var undo = PurchaseUndoManager.Ensure();
        if (undo != null)
            undo.RecordIngredientPack(item, pack, money != null ? price : 0);
        return true;
    }
}
