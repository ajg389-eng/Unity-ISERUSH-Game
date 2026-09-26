using UnityEngine;

/// <summary>
/// One straight file into the lobby. Index 0 stands at the counter.
/// Each person behind them is exactly one tile further east, on the same row.
/// </summary>
public static class CustomerStandLine
{
    public static Vector3 Place(Vector3 firstSlot, int index, float cell)
    {
        index = Mathf.Max(0, index);
        cell = Mathf.Max(0.5f, cell);
        Vector3 slot = firstSlot + Vector3.right * (cell * index);
        slot.y = firstSlot.y;
        return slot;
    }
}
