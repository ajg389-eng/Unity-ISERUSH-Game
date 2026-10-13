using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RegisterPopup : MonoBehaviour
{
    public Vector3 offset = new Vector3(0, 2.5f, 0);

    public Button toggleButton;
    public TextMeshProUGUI buttonText;

    Register target;
    bool bound;

    public void Bind(Register reg)
    {
        target = reg;
        bound = true;
        target.GetComponent<StationConfigurationCaution>()?.SetControlPopupVisible(true);
        UpdateText();
        GameUITheme.ApplyButton(toggleButton);
    }

    void Start()
    {
        toggleButton.onClick.AddListener(() =>
        {
            if (!target) return;
            target.Toggle();
            UpdateText();
            GameUITheme.ApplyButton(toggleButton);
        });
    }

    void OnEnable()
    {
        if (target != null) target.GetComponent<StationConfigurationCaution>()?.SetControlPopupVisible(true);
    }

    void OnDisable()
    {
        if (target != null) target.GetComponent<StationConfigurationCaution>()?.SetControlPopupVisible(false);
    }

    void LateUpdate()
    {
        if (!bound) return;
        if (!target) { Destroy(gameObject); return; }

        transform.position = target.transform.position + offset;

        if (Camera.main)
            transform.forward = Camera.main.transform.forward;
    }

    void UpdateText()
    {
        if (!buttonText || !target) return;
        buttonText.text = target.isEnabled ? "Disable" : "Enable";
    }
}
