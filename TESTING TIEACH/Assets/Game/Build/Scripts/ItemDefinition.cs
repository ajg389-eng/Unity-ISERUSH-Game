using UnityEngine;

[CreateAssetMenu(menuName = "FactoryGame/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    public string itemName;
    public GameObject prefab;   // what gets placed in the world
    [Tooltip("Sale price when this item is part of a customer order")]
    public int price = 10;
    [Tooltip("Starting kitchen stock when KitchenInventory grants starting stock")]
    public int startingQuantity = 0;

    [Header("Inventory UI")]
    [Tooltip("Optional 2D icon for inventory cards. If empty, a 3D thumbnail is generated from the prefab.")]
    public Sprite previewIcon;

    [Header("Kitchen ordering (Management > Ingredients)")]
    [Tooltip("How many units arrive when the player orders one pack")]
    public int orderPackSize = 10;
    [Tooltip("Cost in money to order one pack. If 0, uses price * packSize / 2.")]
    public int orderPackPrice = 15;
}
