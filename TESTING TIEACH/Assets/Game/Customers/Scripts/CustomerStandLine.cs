using UnityEngine;

/// <summary>
/// One straight file into the lobby. Index 0 stands at the counter and each
/// following customer uses consistent grid-cell spacing on the same row.
/// </summary>
public static class CustomerStandLine
{
    // Used only when the scene has no grid to align the queue to.
    const float MinimumSpacingWithoutGrid = 1.25f;

    public static Vector3 Place(Vector3 firstSlot, int index, float spacing)
    {
        return Place(firstSlot, index, spacing, Vector3.right);
    }

    public static Vector3 Place(Vector3 firstSlot, int index, float spacing, Vector3 direction)
    {
        index = Mathf.Max(0, index);
        GridManager grid = GridManager.Instance;
        if (grid != null)
        {
            // Queue markers are rendered as grid cells. Keep every customer on
            // consecutive cells instead of accumulating fractional-cell drift
            // (for example, 1.25m steps on a 1m grid skip a tile every few spots).
            float cellSize = Mathf.Max(0.01f, grid.cellSize);
            int cellsPerSpot = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(cellSize, spacing) / cellSize));
            spacing = cellsPerSpot * cellSize;
        }
        else
        {
            spacing = Mathf.Max(MinimumSpacingWithoutGrid, spacing);
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.right;
        else
            direction.Normalize();

        Vector3 slot = firstSlot + direction * (spacing * index);
        slot.y = firstSlot.y;
        return slot;
    }
}
