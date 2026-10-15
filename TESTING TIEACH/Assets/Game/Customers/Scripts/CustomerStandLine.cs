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

        Vector3 slot = firstSlot + Vector3.right * (spacing * index);
        slot.y = firstSlot.y;
        return slot;
    }
}
