using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One worker's card in the Management > Workers tab: name field, fire button, and station toggles.
/// Lives on the worker card prefab; bound at runtime by WorkersUI.
/// </summary>
public class WorkerCardUI : MonoBehaviour
{
    public KitchenEmployee employee;
    [Tooltip("Name field (TMP_InputField or Unity UI InputField).")]
    public GameObject nameInputObject;
    public Button fireButton;
    public Toggle toggleFreezer;
    public Toggle toggleGrill;
    public Toggle togglePantry;
    public Toggle toggleAssembly;

    ProductionManager production;

    void OnValidate()
    {
        if (nameInputObject == null)
        {
            var t = transform.Find("Row1/NameInput");
            if (t != null) nameInputObject = t.gameObject;
        }
        if (fireButton == null)
        {
            var t = transform.Find("Row1/Button_Fire");
            if (t != null) fireButton = t.GetComponent<Button>();
        }
        if (toggleFreezer == null)
        {
            var t = transform.Find("Row2/Toggle_Freezer");
            if (t != null) toggleFreezer = t.GetComponent<Toggle>();
        }
        if (toggleGrill == null)
        {
            var t = transform.Find("Row2/Toggle_Grill");
            if (t != null) toggleGrill = t.GetComponent<Toggle>();
        }
        if (togglePantry == null)
        {
            var t = transform.Find("Row2/Toggle_Pantry");
            if (t != null) togglePantry = t.GetComponent<Toggle>();
        }
        if (toggleAssembly == null)
        {
            var t = transform.Find("Row2/Toggle_Assembly");
            if (t != null) toggleAssembly = t.GetComponent<Toggle>();
        }
    }

    public void Bind(KitchenEmployee emp)
    {
        RemoveNameListener();
        if (fireButton != null)
            fireButton.onClick.RemoveListener(OnFireClicked);
        RemoveStationListeners();

        employee = emp;
        production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();

        if (emp == null)
        {
            SetNameText("");
            SetToggles(false, false, false, false);
            return;
        }

        SetNameText(emp.employeeName ?? "Worker");
        AddNameListener();
        if (fireButton != null)
        {
            fireButton.interactable = true;
            fireButton.onClick.AddListener(OnFireClicked);
        }

        SetToggles(
            emp.assignedStations != null && emp.assignedStations.Contains(StationType.Freezer),
            emp.assignedStations != null && emp.assignedStations.Contains(StationType.Grill),
            emp.assignedStations != null && emp.assignedStations.Contains(StationType.Pantry),
            emp.assignedStations != null && emp.assignedStations.Contains(StationType.Assembly));
        AddStationListeners();
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

    string GetNameText()
    {
        if (nameInputObject == null) return "";
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (tmpInput != null) return tmpInput.text;
        if (legacyInput != null) return legacyInput.text;
        return "";
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

    void AddStationListeners()
    {
        if (toggleFreezer != null) toggleFreezer.onValueChanged.AddListener(_ => SyncStations());
        if (toggleGrill != null) toggleGrill.onValueChanged.AddListener(_ => SyncStations());
        if (togglePantry != null) togglePantry.onValueChanged.AddListener(_ => SyncStations());
        if (toggleAssembly != null) toggleAssembly.onValueChanged.AddListener(_ => SyncStations());
    }

    void RemoveStationListeners()
    {
        if (toggleFreezer != null) toggleFreezer.onValueChanged.RemoveAllListeners();
        if (toggleGrill != null) toggleGrill.onValueChanged.RemoveAllListeners();
        if (togglePantry != null) togglePantry.onValueChanged.RemoveAllListeners();
        if (toggleAssembly != null) toggleAssembly.onValueChanged.RemoveAllListeners();
    }

    void SetToggles(bool freezer, bool grill, bool pantry, bool assembly)
    {
        if (toggleFreezer != null) toggleFreezer.SetIsOnWithoutNotify(freezer);
        if (toggleGrill != null) toggleGrill.SetIsOnWithoutNotify(grill);
        if (togglePantry != null) togglePantry.SetIsOnWithoutNotify(pantry);
        if (toggleAssembly != null) toggleAssembly.SetIsOnWithoutNotify(assembly);
    }

    void SyncStations()
    {
        if (employee == null) return;
        var list = new List<StationType>();
        if (toggleFreezer != null && toggleFreezer.isOn) list.Add(StationType.Freezer);
        if (toggleGrill != null && toggleGrill.isOn) list.Add(StationType.Grill);
        if (togglePantry != null && togglePantry.isOn) list.Add(StationType.Pantry);
        if (toggleAssembly != null && toggleAssembly.isOn) list.Add(StationType.Assembly);
        employee.SetAssignedStations(list);
    }
}
