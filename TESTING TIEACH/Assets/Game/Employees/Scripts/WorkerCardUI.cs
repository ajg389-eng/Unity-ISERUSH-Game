using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Worker card on Management > Workers: name, fire, and which stations they operate.
/// Station assignment is done in Manage mode by clicking stations in the world.
/// </summary>
public class WorkerCardUI : MonoBehaviour
{
    public KitchenEmployee employee;
    public GameObject nameInputObject;
    public Button fireButton;
    [Tooltip("Optional label showing assigned stations")]
    public TextMeshProUGUI stationsLabel;

    // Legacy toggles (hidden/unused — assignment is world-based now)
    public Toggle toggleFreezer;
    public Toggle toggleGrill;
    public Toggle togglePantry;
    public Toggle toggleAssembly;

    ProductionManager production;

    void OnValidate()
    {
        if (nameInputObject == null)
        {
            var t = transform.Find("NameInput") ?? transform.Find("Row1/NameInput");
            if (t != null) nameInputObject = t.gameObject;
        }
        if (fireButton == null)
        {
            var t = transform.Find("Button_Fire") ?? transform.Find("Row1/Button_Fire");
            if (t != null) fireButton = t.GetComponent<Button>();
        }
        if (stationsLabel == null)
        {
            var t = transform.Find("StationsLabel") ?? transform.Find("Row2/StationsLabel");
            if (t != null) stationsLabel = t.GetComponent<TextMeshProUGUI>();
        }
    }

    public void Bind(KitchenEmployee emp)
    {
        RemoveNameListener();
        if (fireButton != null)
            fireButton.onClick.RemoveListener(OnFireClicked);

        employee = emp;
        production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();

        HideLegacyToggles();

        if (emp == null)
        {
            SetNameText("");
            SetStationsText("—");
            return;
        }

        SetNameText(emp.employeeName ?? "Worker");
        SetStationsText(emp.GetAssignedStationsSummary());
        AddNameListener();
        if (fireButton != null)
        {
            fireButton.interactable = true;
            fireButton.onClick.AddListener(OnFireClicked);
        }
    }

    void HideLegacyToggles()
    {
        if (toggleFreezer != null) toggleFreezer.gameObject.SetActive(false);
        if (toggleGrill != null) toggleGrill.gameObject.SetActive(false);
        if (togglePantry != null) togglePantry.gameObject.SetActive(false);
        if (toggleAssembly != null) toggleAssembly.gameObject.SetActive(false);
    }

    void SetStationsText(string text)
    {
        if (stationsLabel != null)
        {
            stationsLabel.text = text;
            return;
        }
        // Fallback: reuse Row2 first TMP if present
        var row2 = transform.Find("Row2");
        if (row2 == null) return;
        var tmp = row2.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null && (tmp.transform.parent == row2 || tmp.name.Contains("Station") || tmp.name.Contains("Label")))
            tmp.text = text;
    }

    void AddNameListener()
    {
        var tmpInput = nameInputObject != null ? nameInputObject.GetComponent<TMP_InputField>() : null;
        var legacyInput = nameInputObject != null ? nameInputObject.GetComponent<InputField>() : null;
        if (tmpInput != null) tmpInput.onEndEdit.AddListener(OnNameChanged);
        if (legacyInput != null) legacyInput.onEndEdit.AddListener(OnNameChanged);
    }

    void RemoveNameListener()
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (tmpInput != null) tmpInput.onEndEdit.RemoveListener(OnNameChanged);
        if (legacyInput != null) legacyInput.onEndEdit.RemoveListener(OnNameChanged);
    }

    void SetNameText(string text)
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (tmpInput != null) tmpInput.text = text;
        if (legacyInput != null) legacyInput.text = text;
    }

    void OnNameChanged(string value)
    {
        if (employee != null)
            employee.employeeName = string.IsNullOrWhiteSpace(value) ? "Worker" : value.Trim();
    }

    void OnFireClicked()
    {
        if (employee == null || production == null) return;
        production.FireWorker(employee);
        employee = null;
        var workersUI = GetComponentInParent<WorkersUI>();
        if (workersUI != null) workersUI.Refresh();
        else gameObject.SetActive(false);
    }
}
