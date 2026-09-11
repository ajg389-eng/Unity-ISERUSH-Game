using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Worker card on Management > Workers: name, live task, assignments, inventory, and fire.
/// </summary>
public class WorkerCardUI : MonoBehaviour
{
    public KitchenEmployee employee;
    public GameObject nameInputObject;
    public Button fireButton;
    public TextMeshProUGUI currentTaskText;
    public TextMeshProUGUI assignmentsText;
    public TextMeshProUGUI heldItemsText;

    [Tooltip("Legacy summary label; hidden when detail labels are present")]
    public TextMeshProUGUI stationsLabel;

    public Toggle toggleFreezer;
    public Toggle toggleGrill;
    public Toggle togglePantry;
    public Toggle toggleAssembly;

    ProductionManager production;

    void OnValidate()
    {
        BindReferences();
    }

    void Update()
    {
        if (employee != null)
            RefreshDetails();
    }

    void BindReferences()
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

        var details = transform.Find("WorkerDetails");
        if (details != null)
        {
            if (currentTaskText == null)
                currentTaskText = details.Find("TaskSection/CurrentTask")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("CurrentTask")?.GetComponent<TextMeshProUGUI>();
            if (assignmentsText == null)
                assignmentsText = details.Find("StationsSection/Assignments")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("Assignments")?.GetComponent<TextMeshProUGUI>();
            if (heldItemsText == null)
                heldItemsText = details.Find("CarryingSection/HeldItems")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("HeldItems")?.GetComponent<TextMeshProUGUI>();
        }
    }

    public void Bind(KitchenEmployee emp)
    {
        RemoveNameListener();
        if (fireButton != null)
            fireButton.onClick.RemoveListener(OnFireClicked);

        employee = emp;
        production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();

        HideLegacyUi();
        EnsureLayout();
        BindReferences();

        if (emp == null)
        {
            SetNameText("");
            RefreshDetails();
            return;
        }

        SetNameText(emp.employeeName ?? "Worker");
        LockNameField();
        RefreshDetails();
        if (fireButton != null)
        {
            fireButton.interactable = true;
            fireButton.onClick.AddListener(OnFireClicked);
        }
    }

    public void RefreshDetails()
    {
        EnsureLayout();

        if (employee == null)
        {
            SetDetailText(currentTaskText, "—");
            SetDetailText(assignmentsText, "No stations assigned");
            SetDetailText(heldItemsText, "Nothing");
            if (stationsLabel != null) stationsLabel.text = "—";
            return;
        }

        SetDetailText(currentTaskText, employee.GetCurrentTaskDescription());
        SetDetailText(assignmentsText, employee.GetCompactRouteText());

        string held = employee.GetHeldInventoryDisplay();
        bool carrying = !string.IsNullOrEmpty(held);
        SetDetailText(heldItemsText, carrying ? held : "—");
        var carryingSection = heldItemsText != null ? heldItemsText.transform.parent : null;
        if (carryingSection != null)
            carryingSection.gameObject.SetActive(carrying);

        if (stationsLabel != null)
            stationsLabel.gameObject.SetActive(false);
    }

    static string FormatAssignments(string detailText)
    {
        if (string.IsNullOrEmpty(detailText) || detailText == "No stations assigned")
            return "No stations assigned";

        return detailText.Replace("• ", "  • ").Replace("\n", "\n\n");
    }

    void EnsureLayout()
    {
        HideLegacyUi();

        var details = transform.Find("WorkerDetails");
        if (details == null || details.Find("TaskSection") == null)
        {
            if (details != null)
                Destroy(details.gameObject);

            WorkerCardPrefabBuilder.BuildDetailsSection(transform, out currentTaskText, out assignmentsText, out heldItemsText);
        }

        ApplyCardLayout();
        BindReferences();
    }

    void ApplyCardLayout()
    {
        var cardLe = GetComponent<LayoutElement>();
        if (cardLe == null) cardLe = gameObject.AddComponent<LayoutElement>();
        cardLe.minHeight = 96;
        cardLe.preferredHeight = -1;
        cardLe.flexibleWidth = 1;

        var cardImage = GetComponent<Image>();
        if (cardImage != null)
            cardImage.color = new Color(0.18f, 0.2f, 0.26f, 0.98f);

        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            var hlg = GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) Destroy(hlg);
            vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        }
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var row1 = transform.Find("Row1");
        if (row1 != null)
        {
            row1.SetAsFirstSibling();
            var rowLe = row1.GetComponent<LayoutElement>();
            if (rowLe == null) rowLe = row1.gameObject.AddComponent<LayoutElement>();
            rowLe.minHeight = 28;
            rowLe.preferredHeight = 28;

            var hlg = row1.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.spacing = 12;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            if (nameInputObject != null)
            {
                var nameLe = nameInputObject.GetComponent<LayoutElement>();
                if (nameLe == null) nameLe = nameInputObject.AddComponent<LayoutElement>();
                nameLe.flexibleWidth = 1;
                nameLe.minWidth = 160;
                nameLe.preferredWidth = -1;
            }

            if (fireButton != null)
            {
                fireButton.transform.SetAsLastSibling();
                var fireLe = fireButton.GetComponent<LayoutElement>();
                if (fireLe == null) fireLe = fireButton.gameObject.AddComponent<LayoutElement>();
                fireLe.minWidth = 72;
                fireLe.preferredWidth = 72;
                fireLe.flexibleWidth = 0;
            }
        }

        var details = transform.Find("WorkerDetails");
        if (details != null)
        {
            details.SetAsLastSibling();
            var detailsVlg = details.GetComponent<VerticalLayoutGroup>();
            if (detailsVlg != null)
                detailsVlg.spacing = 4;
            CompactSection(details.Find("TaskSection"), 36);
            CompactSection(details.Find("StationsSection"), 32);
            CompactSection(details.Find("CarryingSection"), 28);
        }
    }

    static void CompactSection(Transform section, float minHeight)
    {
        if (section == null) return;
        var header = section.Find("Header");
        if (header != null)
            header.gameObject.SetActive(false);
        var le = section.GetComponent<LayoutElement>();
        if (le != null)
            le.minHeight = minHeight;
        var vlg = section.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
            vlg.padding = new RectOffset(8, 8, 4, 4);
    }

    static void SetDetailText(TextMeshProUGUI label, string text)
    {
        if (label != null)
            label.text = text;
    }

    void HideLegacyUi()
    {
        if (toggleFreezer != null) toggleFreezer.gameObject.SetActive(false);
        if (toggleGrill != null) toggleGrill.gameObject.SetActive(false);
        if (togglePantry != null) togglePantry.gameObject.SetActive(false);
        if (toggleAssembly != null) toggleAssembly.gameObject.SetActive(false);

        var row2 = transform.Find("Row2");
        if (row2 != null)
            row2.gameObject.SetActive(false);
    }

    void AddNameListener() { }

    void LockNameField()
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        if (tmpInput != null)
        {
            tmpInput.readOnly = true;
            tmpInput.interactable = false;
        }
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (legacyInput != null)
            legacyInput.interactable = false;
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
