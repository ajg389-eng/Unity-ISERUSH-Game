using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Config for generating customer orders: any combination of burger, fries, and drink.
/// Create via Assets > Create > FactoryGame > Customer Order Config.
/// </summary>
[CreateAssetMenu(menuName = "FactoryGame/Customer Order Config", fileName = "CustomerOrderConfig")]
public class CustomerOrderConfig : ScriptableObject
{
    [Header("Menu items")]
    [Tooltip("Burger / patty line item")]
    public ItemDefinition burgerBase;
    [Tooltip("Fries side")]
    public ItemDefinition friesItem;
    [Tooltip("Drink")]
    public ItemDefinition drinkItem;

    [Header("Order chances (each item rolled independently)")]
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a burger")]
    public float burgerChance = 0.85f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants fries")]
    public float friesChance = 0.7f;
    [Range(0f, 1f)]
    [Tooltip("Chance the customer wants a drink")]
    public float drinkChance = 0.55f;

    // Runtime menu choices. These deliberately are not serialized back into the shared asset.
    [System.NonSerialized] bool burgerEnabled = true;
    [System.NonSerialized] bool friesEnabled = true;
    [System.NonSerialized] bool drinkEnabled = true;

    public bool IsBurger(ItemDefinition item) => item != null && item == burgerBase;
    public bool IsFries(ItemDefinition item) => item != null && item == friesItem;
    public bool IsDrink(ItemDefinition item) => item != null && item == drinkItem;

    public bool IsItemEnabled(ItemDefinition item)
    {
        if (IsBurger(item)) return burgerEnabled;
        if (IsFries(item)) return friesEnabled;
        if (IsDrink(item)) return drinkEnabled;
        return false;
    }

    public void SetItemEnabled(ItemDefinition item, bool enabled)
    {
        if (IsBurger(item)) burgerEnabled = enabled;
        else if (IsFries(item)) friesEnabled = enabled;
        else if (IsDrink(item)) drinkEnabled = enabled;
    }

    public bool HasEnabledItems =>
        (burgerBase != null && burgerEnabled)
        || (friesItem != null && friesEnabled)
        || (drinkItem != null && drinkEnabled);

    public enum ProductKind { None, Burger, Fries, Drink }

    public ProductKind GetProductKind(ItemDefinition item)
    {
        if (IsBurger(item)) return ProductKind.Burger;
        if (IsFries(item)) return ProductKind.Fries;
        if (IsDrink(item)) return ProductKind.Drink;
        return InferProductKind(item);
    }

    /// <summary>
    /// True when both definitions are the same menu product, even if the kitchen
    /// used a different ItemDefinition asset than the customer's order line.
    /// </summary>
    public bool SameMenuProduct(ItemDefinition a, ItemDefinition b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (!string.IsNullOrEmpty(a.itemName) && a.itemName == b.itemName) return true;
        ProductKind kindA = GetProductKind(a);
        ProductKind kindB = GetProductKind(b);
        return kindA != ProductKind.None && kindA == kindB;
    }

    static ProductKind InferProductKind(ItemDefinition item)
    {
        if (item == null) return ProductKind.None;
        string label = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
        if (string.IsNullOrEmpty(label)) return ProductKind.None;
        if (label.IndexOf("burger", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Burger;
        if (label.IndexOf("fries", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("fry", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Fries;
        if (label.IndexOf("drink", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("soda", System.StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("cola", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return ProductKind.Drink;
        return ProductKind.None;
    }

    /// <summary>
    /// Production pipeline for a single menu item (not including heat lamp delivery).
    /// Burger: Freezer → Grill → Assembly
    /// Fries: Fryer
    /// Drink: none (cashier dispenses straight to the customer)
    /// </summary>
    public StationType[] GetPipeline(ItemDefinition item)
    {
        switch (GetProductKind(item))
        {
            case ProductKind.Burger:
                return new[] { StationType.Freezer, StationType.Grill, StationType.Assembly };
            case ProductKind.Fries:
                return new[] { StationType.Fryer };
            case ProductKind.Drink:
                return new[] { StationType.Drink };
            default:
                return System.Array.Empty<StationType>();
        }
    }

    /// <summary>Products the grill can be set to cook.</summary>
    public IEnumerable<ItemDefinition> GetGrillProducts()
    {
        if (burgerBase != null) yield return burgerBase;
    }

    /// <summary>Products the assembly station can be set to make (burgers only).</summary>
    public IEnumerable<ItemDefinition> GetAssemblyProducts()
    {
        if (burgerBase != null) yield return burgerBase;
    }

    public CustomerOrder GenerateRandomOrder()
    {
        var order = new CustomerOrder();

        bool wantBurger = burgerBase != null && burgerEnabled && Random.value < burgerChance;
        bool wantFries = friesItem != null && friesEnabled && Random.value < friesChance;
        bool wantDrink = drinkItem != null && drinkEnabled && Random.value < drinkChance;

        if (!wantBurger && !wantFries && !wantDrink)
        {
            if (burgerBase != null && burgerEnabled) wantBurger = true;
            else if (friesItem != null && friesEnabled) wantFries = true;
            else if (drinkItem != null && drinkEnabled) wantDrink = true;
        }

        if (wantBurger)
            order.lines.Add(new CustomerOrder.OrderLine(burgerBase, 1));
        if (wantFries)
            order.lines.Add(new CustomerOrder.OrderLine(friesItem, 1));
        if (wantDrink)
            order.lines.Add(new CustomerOrder.OrderLine(drinkItem, 1));

        return order;
    }

    /// <summary>One random heat-lamp item (burger or fries — drinks are cashier-served).</summary>
    public CustomerOrder GenerateRandomSingleItemOrder()
    {
        var options = new List<ItemDefinition>();
        if (burgerBase != null && burgerEnabled) options.Add(burgerBase);
        if (friesItem != null && friesEnabled) options.Add(friesItem);
        if (drinkItem != null && drinkEnabled) options.Add(drinkItem);
        if (options.Count == 0) return new CustomerOrder();

        var pick = options[Random.Range(0, options.Count)];
        return CustomerOrder.FromItem(pick, 1);
    }

    public IEnumerable<ItemDefinition> GetMenuItems()
    {
        if (burgerBase != null) yield return burgerBase;
        if (friesItem != null) yield return friesItem;
        if (drinkItem != null) yield return drinkItem;
    }

    public IEnumerable<ItemDefinition> GetEnabledMenuItems()
    {
        foreach (ItemDefinition item in GetMenuItems())
            if (IsItemEnabled(item))
                yield return item;
    }
}
