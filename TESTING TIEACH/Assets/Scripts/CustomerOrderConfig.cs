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

    public bool IsBurger(ItemDefinition item) => item != null && item == burgerBase;
    public bool IsFries(ItemDefinition item) => item != null && item == friesItem;
    public bool IsDrink(ItemDefinition item) => item != null && item == drinkItem;

    public enum ProductKind { None, Burger, Fries, Drink }

    public ProductKind GetProductKind(ItemDefinition item)
    {
        if (IsBurger(item)) return ProductKind.Burger;
        if (IsFries(item)) return ProductKind.Fries;
        if (IsDrink(item)) return ProductKind.Drink;
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
                return System.Array.Empty<StationType>();
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

        bool wantBurger = burgerBase != null && Random.value < burgerChance;
        bool wantFries = friesItem != null && Random.value < friesChance;
        bool wantDrink = drinkItem != null && Random.value < drinkChance;

        if (!wantBurger && !wantFries && !wantDrink)
        {
            if (burgerBase != null) wantBurger = true;
            else if (friesItem != null) wantFries = true;
            else if (drinkItem != null) wantDrink = true;
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
        if (burgerBase != null) options.Add(burgerBase);
        if (friesItem != null) options.Add(friesItem);
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
}
