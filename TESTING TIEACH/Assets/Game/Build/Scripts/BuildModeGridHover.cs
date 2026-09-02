using UnityEngine;

public class BuildModeGridHover : MonoBehaviour
{
    public GameModeManager modeManager;
    public GridManager grid;

    [Header("Highlight")]
    public GameObject highlightPrefab;   // simple quad/cube prefab
    private GameObject highlightInstance;

    [Header("Raycast")]
    public LayerMask floorLayer;         // set to Floor layer

    void Start()
    {
        if (highlightPrefab)
            highlightInstance = Instantiate(highlightPrefab);

        if (highlightInstance)
            highlightInstance.SetActive(false);
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
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, floorLayer))
        {
            if (grid.WorldToCell(hit.point, out int x, out int y))
            {
                if (highlightInstance)
                {
                    highlightInstance.SetActive(true);
                    highlightInstance.transform.position = grid.CellToWorld(x, y) + Vector3.up * 0.03f;
                }
            }
        }
        else
        {
            if (highlightInstance) highlightInstance.SetActive(false);
        }
    }
}