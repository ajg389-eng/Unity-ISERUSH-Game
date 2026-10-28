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
    RaycastHit[] counterHits = new RaycastHit[32];

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
        if (!build || Input.GetMouseButton(1))
        {
            if (highlightInstance) highlightInstance.SetActive(false);
            return;
        }

        // Hide the hover only beneath the visible side panels, not beneath
        // transparent full-screen canvas roots.
        if (UIInputFocusGuard.IsPointerOverBlockingPanel)
        {
            if (highlightInstance) highlightInstance.SetActive(false);
            return;
        }

        // Mouse → Raycast to floor → World pos → Grid cell
        Camera viewCamera = Camera.main;
        if (viewCamera == null) return;
        Ray ray = viewCamera.ScreenPointToRay(Input.mousePosition);
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
        int hitCount = Physics.RaycastNonAlloc(ray, counterHits, 200f, -1);
        // A full NonAlloc buffer can omit the nearest valid counter. Grow and
        // repeat the query until every hit fits, retaining the buffer for later frames.
        while (hitCount == counterHits.Length)
        {
            System.Array.Resize(ref counterHits, counterHits.Length * 2);
            hitCount = Physics.RaycastNonAlloc(ray, counterHits, 200f, -1);
        }

        int span = buildPlacer != null ? buildPlacer.CounterHoverSpan : 1;
        bool requireAvailable = buildPlacer != null && buildPlacer.IsCounterPlacementActive;
        CounterSurface nearestCounter = null;
        int nearestSlot = -1;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = counterHits[i];
            if (hit.distance >= nearestDistance) continue;
            CounterSurface counter = hit.collider.GetComponentInParent<CounterSurface>();
            if (counter == null) continue;

            int slot = counter.GetNearestSlot(hit.point, span, requireAvailable);
            if (slot < 0) continue;
            nearestCounter = counter;
            nearestSlot = slot;
            nearestDistance = hit.distance;
        }

        if (nearestCounter == null) return false;
        Vector3 center = nearestCounter.GetSlotWorldCenter(nearestSlot, span);
        Vector2 size = nearestCounter.GetSlotWorldSize(span);
        highlightInstance.transform.rotation = highlightBaseRotation;
        highlightInstance.transform.localScale = new Vector3(
            size.x * 0.92f,
            size.y * 0.92f,
            highlightBaseScale.z);
        highlightInstance.transform.position = center + Vector3.up * 0.04f;
        highlightInstance.SetActive(true);
        return true;
    }
}
