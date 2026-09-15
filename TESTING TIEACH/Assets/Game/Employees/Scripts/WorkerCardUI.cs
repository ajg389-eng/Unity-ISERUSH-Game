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
    public Button customizeButton;
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
    Transform assignmentControls;
    string assignmentSignature;
    bool detailsExpanded;
    Button expandButton;
    TextMeshProUGUI expandArrowLabel;

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
        if (customizeButton == null)
        {
            var t = transform.Find("Button_Customize") ?? transform.Find("Row1/Button_Customize");
            if (t != null) customizeButton = t.GetComponent<Button>();
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
        if (customizeButton != null)
            customizeButton.onClick.RemoveListener(OnCustomizeClicked);
        if (expandButton != null)
            expandButton.onClick.RemoveListener(ToggleDetailsExpanded);

        employee = emp;
        production = ProductionManager.Instance != null ? ProductionManager.Instance : FindObjectOfType<ProductionManager>();
        detailsExpanded = false;

        HideLegacyUi();
        EnsureLayout();
        BindReferences();

        if (emp == null)
        {
            SetNameText("");
            RefreshDetails();
            ApplyExpandedState();
            return;
        }

        SetNameText(emp.employeeName ?? "Worker");
        EnableNameEditing();
        EnsureExpandArrow();
        RefreshDetails();
        ApplyExpandedState();
        if (customizeButton != null)
        {
            customizeButton.interactable = true;
            customizeButton.onClick.AddListener(OnCustomizeClicked);
        }
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
        string flowPrefix = !string.IsNullOrEmpty(employee.assignedFlowName)
            ? "FLOW: " + employee.assignedFlowName + "\n"
            : "";
        float transport = Mathf.Max(0.1f, employee.transportItemsPerMinute);
        SetDetailText(
            assignmentsText,
            flowPrefix
            + FormatUpgradeStars(employee.UpgradeLevel)
            + "  Carry " + employee.CarryCapacity
            + "  ·  Transport: " + FormatRate(transport) + " items / min\n"
            + "Stations: " + employee.GetAssignedStationsSummary());

        string held = employee.GetHeldInventoryDisplay();
        bool carrying = !string.IsNullOrEmpty(held);
        SetDetailText(heldItemsText, carrying ? held : "—");
        var carryingSection = heldItemsText != null ? heldItemsText.transform.parent : null;
        if (carryingSection != null)
            carryingSection.gameObject.SetActive(carrying);

        if (stationsLabel != null)
            stationsLabel.gameObject.SetActive(false);

        EnsureAssignmentControls();
        RefreshUpgradeButton();
    }

    static string FormatUpgradeStars(int level)
    {
        level = Mathf.Clamp(level, 0, KitchenEmployee.MaxUpgradeLevel);
        // ASCII-safe star readout (avoids missing TMP glyphs).
        return "[" + level + "/" + KitchenEmployee.MaxUpgradeLevel + "]";
    }

    static string FormatRate(float rate)
    {
        if (rate >= 10f) return rate.ToString("0");
        return rate.ToString("0.0");
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
        EnsureExpandArrow();

        var detailsRoot = transform.Find("WorkerDetails");
        if (detailsRoot != null && detailsRoot.gameObject.activeSelf != detailsExpanded)
            detailsRoot.gameObject.SetActive(detailsExpanded);
    }

    void EnsureExpandArrow()
    {
        var row1 = transform.Find("Row1");
        if (row1 == null) return;

        // Remove old name overlay that blocked editing.
        if (nameInputObject != null)
        {
            Transform oldHit = nameInputObject.transform.Find("ExpandHit");
            if (oldHit != null)
                Destroy(oldHit.gameObject);
        }

        Transform existing = row1.Find("ExpandArrow");
        if (existing == null)
        {
            var go = new GameObject("ExpandArrow", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(row1, false);
            go.transform.SetAsFirstSibling();
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 28;
            le.preferredWidth = 28;
            le.minHeight = 28;
            le.preferredHeight = 28;
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;
            go.GetComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 1f);
            expandButton = go.GetComponent<Button>();

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)textGo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            expandArrowLabel = textGo.AddComponent<TextMeshProUGUI>();
            expandArrowLabel.text = ">";
            expandArrowLabel.fontSize = 18;
            expandArrowLabel.fontStyle = FontStyles.Bold;
            expandArrowLabel.alignment = TextAlignmentOptions.Center;
            expandArrowLabel.color = Color.white;
            expandArrowLabel.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                expandArrowLabel.font = TMP_Settings.defaultFontAsset;
        }
        else
        {
            existing.SetAsFirstSibling();
            expandButton = existing.GetComponent<Button>();
            if (expandArrowLabel == null)
                expandArrowLabel = existing.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var le = existing.GetComponent<LayoutElement>();
            if (le == null) le = existing.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 28;
            le.preferredWidth = 28;
            le.minHeight = 28;
            le.preferredHeight = 28;
            le.flexibleWidth = 0;
            le.flexibleHeight = 0;
            if (expandArrowLabel != null)
            {
                expandArrowLabel.text = ">";
                expandArrowLabel.fontSize = 18;
                expandArrowLabel.fontStyle = FontStyles.Bold;
            }
        }

        if (expandButton != null)
        {
            expandButton.onClick.RemoveListener(ToggleDetailsExpanded);
            expandButton.onClick.AddListener(ToggleDetailsExpanded);
        }

        UpdateExpandArrowLabel();
    }

    void ToggleDetailsExpanded()
    {
        detailsExpanded = !detailsExpanded;
        ApplyExpandedState();
    }

    void ApplyExpandedState()
    {
        var details = transform.Find("WorkerDetails");
        if (details != null && details.gameObject.activeSelf != detailsExpanded)
            details.gameObject.SetActive(detailsExpanded);

        var cardLe = GetComponent<LayoutElement>();
        if (cardLe == null) cardLe = gameObject.AddComponent<LayoutElement>();
        if (detailsExpanded)
        {
            cardLe.minHeight = 96;
            cardLe.preferredHeight = -1;
        }
        else
        {
            cardLe.minHeight = 44;
            cardLe.preferredHeight = 44;
        }

        UpdateExpandArrowLabel();

        if (transform is RectTransform cardRect)
            LayoutRebuilder.MarkLayoutForRebuild(cardRect);
        var parentRect = transform.parent as RectTransform;
        if (parentRect != null)
            LayoutRebuilder.MarkLayoutForRebuild(parentRect);
    }

    void UpdateExpandArrowLabel()
    {
        if (expandArrowLabel == null) return;
        expandArrowLabel.text = ">";
        // Rotate a reliable ">" glyph: right when collapsed, down when expanded.
        expandArrowLabel.rectTransform.localEulerAngles = detailsExpanded
            ? new Vector3(0f, 0f, -90f)
            : Vector3.zero;
    }

    void ApplyCardLayout()
    {
        var cardLe = GetComponent<LayoutElement>();
        if (cardLe == null) cardLe = gameObject.AddComponent<LayoutElement>();
        cardLe.flexibleWidth = 1;
        // Height is driven by ApplyExpandedState (collapsed header vs full details).

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

            EnsureExpandArrow();

            if (expandButton != null)
            {
                var expandLe = expandButton.GetComponent<LayoutElement>();
                if (expandLe == null) expandLe = expandButton.gameObject.AddComponent<LayoutElement>();
                expandLe.minWidth = 28;
                expandLe.preferredWidth = 28;
                expandLe.minHeight = 28;
                expandLe.preferredHeight = 28;
                expandLe.flexibleWidth = 0;
                expandLe.flexibleHeight = 0;
            }

            if (nameInputObject != null)
            {
                nameInputObject.transform.SetSiblingIndex(1);
                var nameLe = nameInputObject.GetComponent<LayoutElement>();
                if (nameLe == null) nameLe = nameInputObject.AddComponent<LayoutElement>();
                nameLe.flexibleWidth = 1;
                nameLe.minWidth = 120;
                nameLe.preferredWidth = -1;
            }

            EnsureCustomizeButton(row1);

            if (customizeButton != null)
            {
                customizeButton.transform.SetAsLastSibling();
                var customizeLe = customizeButton.GetComponent<LayoutElement>();
                if (customizeLe == null) customizeLe = customizeButton.gameObject.AddComponent<LayoutElement>();
                customizeLe.minWidth = 96;
                customizeLe.preferredWidth = 96;
                customizeLe.flexibleWidth = 0;
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

    void EnsureAssignmentControls()
    {
        var details = transform.Find("WorkerDetails");
        if (details == null) return;

        if (assignmentControls == null)
        {
            Transform oldPriorities = details.Find("PriorityControls");
            if (oldPriorities != null) Destroy(oldPriorities.gameObject);
            Transform oldAssignments = details.Find("AssignmentControls");
            if (oldAssignments != null) Destroy(oldAssignments.gameObject);
        }

        string signature = employee == null ? "none" : employee.GetInstanceID().ToString();
        if (employee != null)
            signature += ":u" + employee.UpgradeLevel;
        if (employee != null && employee.operatedStations != null)
            foreach (GameObject station in employee.operatedStations)
                signature += ":" + (station != null ? station.GetInstanceID().ToString() : "null");
        if (assignmentControls != null && assignmentSignature == signature) return;
        assignmentSignature = signature;

        if (assignmentControls != null)
            Destroy(assignmentControls.gameObject);

        var root = new GameObject("AssignmentControls", typeof(RectTransform));
        root.transform.SetParent(details, false);
        assignmentControls = root.transform;
        var vertical = root.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 3;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        var rootLayout = root.AddComponent<LayoutElement>();
        rootLayout.minHeight = 92f;
        rootLayout.preferredHeight = rootLayout.minHeight;

        Button upgrade = CreateControlButton(assignmentControls, GetUpgradeButtonLabel(), 28f);
        upgrade.name = "UpgradeTransport";
        upgrade.onClick.AddListener(OnUpgradeClicked);
        ApplyUpgradeButtonStyle(upgrade);

        Button inspect = CreateControlButton(assignmentControls, "Inspect / Assign on Grid", 28f);
        inspect.onClick.AddListener(() =>
        {
            if (employee != null && ManagementModeController.Instance != null)
                ManagementModeController.Instance.SelectWorker(employee);
        });

        Button assignFlow = CreateControlButton(assignmentControls, "Assign to Current Flow", 28f);
        assignFlow.onClick.AddListener(() =>
        {
            if (employee == null) return;
            ProductionManager manager = ProductionManager.Instance != null
                ? ProductionManager.Instance
                : FindObjectOfType<ProductionManager>();
            if (manager == null) return;
            manager.AddWorkerToSelectedFlow(employee);
            WorkersUI workersUi = FindObjectOfType<WorkersUI>();
            if (workersUi != null) workersUi.Refresh();
        });
    }

    string GetUpgradeButtonLabel()
    {
        if (employee == null) return "Upgrade";
        if (employee.IsMaxUpgraded)
            return "Carry Max " + FormatUpgradeStars(employee.UpgradeLevel);
        int cost = employee.GetNextUpgradeCost();
        return "Upgrade Carry  $" + cost + "  → " + FormatUpgradeStars(employee.UpgradeLevel + 1)
            + " (" + (employee.UpgradeLevel + 2) + " items)";
    }

    void RefreshUpgradeButton()
    {
        if (assignmentControls == null) return;
        Transform t = assignmentControls.Find("UpgradeTransport");
        if (t == null) return;
        var btn = t.GetComponent<Button>();
        if (btn == null) return;
        var label = t.Find("Text")?.GetComponent<TextMeshProUGUI>();
        if (label != null)
            label.text = GetUpgradeButtonLabel();
        ApplyUpgradeButtonStyle(btn);
    }

    void ApplyUpgradeButtonStyle(Button btn)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        var money = FindObjectOfType<MoneyManager>();
        bool maxed = employee != null && employee.IsMaxUpgraded;
        int cost = employee != null ? employee.GetNextUpgradeCost() : -1;
        bool canPay = maxed || cost <= 0 || money == null || money.CanAfford(cost);
        btn.interactable = employee != null && !maxed && canPay;
        if (img != null)
        {
            if (maxed)
                img.color = new Color(0.22f, 0.28f, 0.34f, 0.98f);
            else if (!canPay)
                img.color = new Color(0.32f, 0.24f, 0.24f, 0.98f);
            else
                img.color = new Color(0.28f, 0.42f, 0.36f, 0.98f);
        }
    }

    void OnUpgradeClicked()
    {
        if (employee == null) return;
        if (!employee.TryUpgrade())
        {
            Sfx.Play(SfxId.UiError);
            RefreshUpgradeButton();
            return;
        }
        assignmentSignature = null; // force rebuild so label/level update
        RefreshDetails();
    }

    static Button CreateControlButton(Transform parent, string text, float height)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.25f, 0.32f, 0.42f, 0.98f);
        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        if (text.Length <= 2)
        {
            layout.minWidth = 28f;
            layout.preferredWidth = 28f;
        }
        else
            layout.flexibleWidth = 1f;

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        var rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 11f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go.GetComponent<Button>();
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

    void AddNameListener()
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (tmpInput != null) tmpInput.onEndEdit.AddListener(OnNameChanged);
        if (legacyInput != null) legacyInput.onEndEdit.AddListener(OnNameChanged);
    }

    void EnableNameEditing()
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        if (tmpInput != null)
        {
            tmpInput.readOnly = false;
            tmpInput.interactable = true;
            tmpInput.enabled = true;
        }
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (legacyInput != null)
        {
            legacyInput.interactable = true;
            legacyInput.enabled = true;
        }
        RemoveNameListener();
        AddNameListener();
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
        if (employee == null) return;
        string cleaned = string.IsNullOrWhiteSpace(value) ? "Worker" : value.Trim();
        if (cleaned.StartsWith("▾ ") || cleaned.StartsWith("▸ "))
            cleaned = cleaned.Substring(2).Trim();
        if (string.IsNullOrEmpty(cleaned)) cleaned = "Worker";
        employee.employeeName = cleaned;
        SetNameText(cleaned);
    }

    void EnsureCustomizeButton(Transform row1)
    {
        if (row1 == null) return;
        if (customizeButton != null) return;

        var existing = row1.Find("Button_Customize");
        if (existing != null)
        {
            customizeButton = existing.GetComponent<Button>();
            return;
        }

        var go = new GameObject("Button_Customize", typeof(RectTransform));
        go.transform.SetParent(row1, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 96;
        le.preferredWidth = 96;
        le.minHeight = 28;
        le.preferredHeight = 28;
        le.flexibleWidth = 0;
        go.AddComponent<Image>().color = new Color(0.28f, 0.4f, 0.55f, 1f);
        customizeButton = go.AddComponent<Button>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "Customize";
        tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void OnCustomizeClicked()
    {
        if (employee == null) return;
        WorkerCustomizePopup.Show(employee);
    }

    void OnFireClicked()
    {
        if (employee == null || production == null) return;
        WorkerCustomizePopup.Hide();
        production.FireWorker(employee);
        employee = null;
        var workersUI = GetComponentInParent<WorkersUI>();
        if (workersUI != null) workersUI.Refresh();
        else gameObject.SetActive(false);
    }
}
