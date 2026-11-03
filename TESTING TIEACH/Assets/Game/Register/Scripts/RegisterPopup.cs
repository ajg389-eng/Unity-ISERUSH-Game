using UnityEngine;

/// <summary>
/// Legacy popup component retained for prefab compatibility. Registers no
/// longer expose an enable/disable control, so any old popup instance closes.
/// </summary>
public class RegisterPopup : MonoBehaviour
{
    public void Bind(Register register)
    {
        Destroy(gameObject);
    }

    void Awake()
    {
        Destroy(gameObject);
    }
}
