using UnityEngine;

[CreateAssetMenu(menuName = "FactoryGame/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    public enum PlacementSurface { Floor, Counter }
    public enum BuildFunction { None, Counter, Register }

    public string itemName;
    public GameObject prefab;   // what gets placed in the world
    [Tooltip("Sale price when this item is part of a customer order")]
    public int price = 10;
    [Tooltip("Starting kitchen stock when KitchenInventory grants starting stock")]
    public int startingQuantity = 0;

    [Header("Placement")]
    [Tooltip("Floor items occupy grid cells. Counter items snap onto a free counter module.")]
    public PlacementSurface placementSurface = PlacementSurface.Floor;
    [Min(1)] public int footprintX = 1;
    [Min(1)] public int footprintY = 1;
    [Tooltip("Scale applied to the source prefab when it is placed.")]
    public Vector3 placementScale = Vector3.one;
    [Tooltip("Rotation applied before the player's 90-degree build rotation.")]
    public Vector3 placementEuler = Vector3.zero;
    [Tooltip("For counter-mounted items, lowers the model into the counter by this many world units.")]
    [Min(0f)] public float counterEmbedDepth = 0f;
    [Tooltip("Additional position adjustment in the counter's local axes after snapping to a slot.")]
    public Vector3 counterLocalOffset = Vector3.zero;
    [Tooltip("Number of adjacent counter positions reserved by this item.")]
    [Min(1)] public int counterSlotSpan = 1;
    [Tooltip("Adds the gameplay behavior needed by special build items sourced from imported art prefabs.")]
    public BuildFunction buildFunction = BuildFunction.None;

    [Header("Inventory UI")]
    [Tooltip("Optional 2D icon for inventory cards. If empty, a 3D thumbnail is generated from the prefab.")]
    public Sprite previewIcon;

    [Header("Kitchen ordering (Management > Food)")]
    [Tooltip("How many units arrive when the player orders one pack")]
    public int orderPackSize = 10;
    [Tooltip("Cost in money to order one pack. If 0, uses price * packSize / 2.")]
    public int orderPackPrice = 15;
}
