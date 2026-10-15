using UnityEngine;

/// <summary>
/// One straight file into the lobby. Index 0 stands at the counter and each
/// following customer uses the configured spacing on the same row.
/// </summary>
public static class CustomerStandLine
{
    // Keep neighboring character models from overlapping on one-cell lobby grids.
    const float MinimumSpacing = 1.25f;

    public static Vector3 Place(Vector3 firstSlot, int index, float spacing)
    {
        index = Mathf.Max(0, index);
        spacing = Mathf.Max(MinimumSpacing, spacing);
        Vector3 slot = firstSlot + Vector3.right * (spacing * index);
        slot.y = firstSlot.y;
        return slot;
    }
}
