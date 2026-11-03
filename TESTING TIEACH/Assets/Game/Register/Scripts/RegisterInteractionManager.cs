using UnityEngine;

public class RegisterInteractionManager : MonoBehaviour
{
    public Camera cam;
    public RegisterPopup popupPrefab;

    RegisterHover lastHover;
    RegisterPopup activePopup;

    void Update()
    {
        if (!cam) cam = Camera.main;

        // Ignore actual controls, but allow a register click through transparent
        // space in the open Build or Business panel.
        if (Input.GetMouseButtonDown(0) && UIInputFocusGuard.IsPointerOverInteractiveUI)
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
