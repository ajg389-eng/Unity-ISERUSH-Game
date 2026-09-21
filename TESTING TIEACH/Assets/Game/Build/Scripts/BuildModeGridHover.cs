using UnityEngine;

public class BuildModeGridHover : MonoBehaviour
{
    public GameModeManager modeManager;
    public GridManager grid;

    [Header("Highlight")]
    public GameObject highlightPrefab;   // simple quad/cube prefab
    private GameObject highlightInstance;
    private Vector3 highlightBaseScale;
    private Quaternion highlightBaseRotation;
    private BuildPlacer buildPlacer;

    [Header("Raycast")]
    public LayerMask floorLayer;         // set to Floor layer

    void Start()
    {
        if (highlightPrefab)
            highlightInstance = Instantiate(highlightPrefab);

        if (highlightInstance)
        {
            highlightBaseScale = highlightInstance.transform.localScale;
            highlightBaseRotation = highlightInstance.transform.rotation;
            foreach (Collider collider in highlightInstance.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            highlightInstance.SetActive(false);
        }
        buildPlacer = FindObjectOfType<BuildPlacer>();
    }

    void Update()
    {
        if (!modeManager || !grid) return;

        bool build = modeManager.CurrentMode == GameModeManager.Mode.Build;

        // Only show highlight in Build Mode
        if (!build)
        {
            if (highlightInstance) highlightInstance.SetActive(false);
            return;
        }

        // Mouse → Raycast to floor → World pos → Grid cell
        if (Camera.main == null) return;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (TryShowCounterHighlight(ray)) return;
        if (TryShowCustomerDoorHighlight()) return;

        if (Physics.Raycast(ray, out RaycastHit hit, 200f, floorLayer))
        {
            if (grid.WorldToCell(hit.point, out int x, out int y))
            {
                if (highlightInstance)
                {
                    highlightInstance.SetActive(true);
                    highlightInstance.transform.rotation = highlightBaseRotation;
                    highlightInstance.transform.localScale = highlightBaseScale;
                    highlightInstance.transform.position = grid.CellToWorld(x, y) + Vector3.up * 0.03f;
                }
            }
        }
        else
        {
            if (highlightInstance) highlightInstance.SetActive(false);
        }
    }

    bool TryShowCustomerDoorHighlight()
    {
        if (highlightInstance == null || buildPlacer == null) return false;
        if (!buildPlacer.TryGetCustomerDoorHighlight(out Vector3 center, out float tileSize))
            return false;

        highlightInstance.transform.rotation = highlightBaseRotation;
        highlightInstance.transform.localScale = new Vector3(
            highlightBaseScale.x * tileSize,
            highlightBaseScale.y * tileSize,
            highlightBaseScale.z);
        highlightInstance.transform.position = center;
        highlightInstance.SetActive(true);
        return true;
    }

    bool TryShowCounterHighlight(Ray ray)
    {
        if (highlightInstance == null) return false;
        RaycastHit[] hits = Physics.RaycastAll(ray, 200f, -1);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        int span = buildPlacer != null ? buildPlacer.CounterHoverSpan : 1;
        bool requireAvailable = buildPlacer != null && buildPlacer.IsCounterPlacementActive;
        for (int i = 0; i < hits.Length; i++)
        {
            CounterSurface counter = hits[i].collider.GetComponentInParent<CounterSurface>();
            if (counter == null) continue;

            int slot = counter.GetNearestSlot(hits[i].point, span, requireAvailable);
            if (slot < 0) continue;

            Vector3 center = counter.GetSlotWorldCenter(slot, span);
            Vector2 size = counter.GetSlotWorldSize(span);
            highlightInstance.transform.rotation = highlightBaseRotation;
            highlightInstance.transform.localScale = new Vector3(
                size.x * 0.92f,
                size.y * 0.92f,
                highlightBaseScale.z);
            highlightInstance.transform.position = center + Vector3.up * 0.04f;
            highlightInstance.SetActive(true);
            return true;
        }

        return false;
    }
}
