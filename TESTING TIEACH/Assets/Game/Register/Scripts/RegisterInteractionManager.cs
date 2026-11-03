using UnityEngine;

public class RegisterInteractionManager : MonoBehaviour
{
    public Camera cam;

    RegisterHover lastHover;

    void Update()
    {
        if (!cam) cam = Camera.main;
        if (lastHover) lastHover.SetHover(false);
        lastHover = null;
        if (cam == null || UIInputFocusGuard.IsPointerOverInteractiveUI) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;
        RegisterHover hover = hit.collider.GetComponentInParent<RegisterHover>();
        if (hover == null) return;
        hover.SetHover(true);
        lastHover = hover;
    }
}
