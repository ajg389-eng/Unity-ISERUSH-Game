using UnityEngine;

/// <summary>Expands the lobby floor once at runtime without changing the placeable work grid.</summary>
public class CustomerFloorRuntimeExtension : MonoBehaviour
{
    bool applied;
    Bounds originalBounds;

    public bool HasOriginalBounds => applied;
    public Bounds OriginalBounds => originalBounds;

    public void ApplyOneTileNorthEast(float cellSize)
    {
        if (applied) return;
        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer == null || cellSize <= 0.001f) return;

        Bounds before = renderer.bounds;
        if (before.size.x <= 0.001f || before.size.z <= 0.001f) return;

        originalBounds = before;
        // Keep the existing south-west edge fixed and add one cell north/east.
        transform.localScale = new Vector3(
            transform.localScale.x * (before.size.x + cellSize) / before.size.x,
            transform.localScale.y,
            transform.localScale.z * (before.size.z + cellSize) / before.size.z);
        transform.position += new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
        applied = true;
    }
}
