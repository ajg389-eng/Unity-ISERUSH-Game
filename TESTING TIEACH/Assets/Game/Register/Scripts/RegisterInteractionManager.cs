using UnityEngine;
using UnityEngine.EventSystems;

public class RegisterInteractionManager : MonoBehaviour
{
    public Camera cam;
    public RegisterPopup popupPrefab;

    RegisterHover lastHover;
    RegisterPopup activePopup;

    void Update()
    {
        if (!cam) cam = Camera.main;

        // If you click on UI (the popup button), don't treat it as "elsewhere"
        if (Input.GetMouseButtonDown(0) && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Hover highlight
        if (lastHover) lastHover.SetHover(false);
        lastHover = null;

        bool hitRegister = false;

        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
        {
            var hover = hit.collider.GetComponentInParent<RegisterHover>();
            if (hover)
            {
                hover.SetHover(true);
                lastHover = hover;
            }

            // Click register -> open popup
            if (Input.GetMouseButtonDown(0))
            {
                var reg = hit.collider.GetComponentInParent<Register>();
                if (reg != null)
                {
                    hitRegister = true;

                    if (popupPrefab)
                    {
                        if (activePopup) Destroy(activePopup.gameObject);
                        activePopup = Instantiate(popupPrefab);
                        activePopup.Bind(reg);
                    }
                }
            }
        }

        // Click anywhere else -> close popup
        if (Input.GetMouseButtonDown(0) && !hitRegister)
        {
            if (activePopup) Destroy(activePopup.gameObject);
        }
    }
}