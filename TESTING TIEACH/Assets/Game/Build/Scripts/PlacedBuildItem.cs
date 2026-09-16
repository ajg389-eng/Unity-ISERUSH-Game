using UnityEngine;

/// <summary>
/// Attached to placed build objects so we know which ItemDefinition to return to inventory when removed.
/// BuildPlacer adds this when placing from inventory.
/// </summary>
public class PlacedBuildItem : MonoBehaviour
{
    public ItemDefinition itemDefinition;
}
