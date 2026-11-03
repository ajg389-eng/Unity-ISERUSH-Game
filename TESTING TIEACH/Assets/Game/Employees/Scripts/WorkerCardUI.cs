using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
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
    public Button flowAssignButton;
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
    bool detailsExpanded;
    Button expandButton;
    TextMeshProUGUI expandArrowLabel;
    Image cardImage;
    Outline selectionOutline;
    float nextDetailsRefreshTime;
    float nextSelectionRefreshTime;

    const float DetailsRefreshInterval = 0.2f;
    const float SelectionRefreshInterval = 0.12f;

    void OnValidate()
    {
        BindReferences();
    }

    void Update()
    {
        TrySelectFromCardClick();
        bool planPhase = GameTimeManager.Instance == null || !GameTimeManager.Instance.IsShiftInProgress;
        if (fireButton != null) fireButton.interactable = planPhase;
        float now = Time.unscaledTime;
        if (employee != null && now >= nextDetailsRefreshTime)
            RefreshDetails();
        if (now >= nextSelectionRefreshTime)
        {
            nextSelectionRefreshTime = now + SelectionRefreshInterval;
            RefreshSelectionHighlight();
        }
    }

    void TrySelectFromCardClick()
    {
        if (employee == null || !Input.GetMouseButtonDown(0)) return;
        var management = ManagementModeController.Instance;
        if (management == null || !management.IsManageMode) return;
        if (EventSystem.current == null) return;

        var pointer = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        if (hits.Count == 0 || hits[0].gameObject == null) return;

        Transform clicked = hits[0].gameObject.transform;
        Button clickedButton = clicked.GetComponentInParent<Button>();
        if (clickedButton != null && clickedButton.transform.IsChildOf(transform))
            return;
        TMP_InputField clickedTmpInput = clicked.GetComponentInParent<TMP_InputField>();
        if (clickedTmpInput != null && clickedTmpInput.transform.IsChildOf(transform))
            return;
        TMP_Dropdown clickedDropdown = clicked.GetComponentInParent<TMP_Dropdown>();
        if (clickedDropdown != null && clickedDropdown.transform.IsChildOf(transform))
            return;
        InputField clickedLegacyInput = clicked.GetComponentInParent<InputField>();
        if (clickedLegacyInput != null && clickedLegacyInput.transform.IsChildOf(transform))
            return;

        if (clicked == transform || clicked.IsChildOf(transform))
        {
            management.SelectWorker(employee);
            var cameraController = FindObjectOfType<PlayerCameraController>();
            if (cameraController != null)
                cameraController.PanTo(employee.transform);
        }
    }

    void RefreshSelectionHighlight()
    {
        if (cardImage == null)
            cardImage = GetComponent<Image>();

        bool primarySelected = employee != null
            && ManagementModeController.Instance != null
            && ManagementModeController.Instance.SelectedEmployee == employee;
        bool selected = primarySelected
            || WorkerAssignmentLinkVisuals.IsWorkerAssociatedWithFocusedFlow(employee);

        if (selectionOutline == null)
            selectionOutline = GetComponent<Outline>();
        if (selected && selectionOutline == null)
            selectionOutline = gameObject.AddComponent<Outline>();

        if (cardImage != null)
            cardImage.color = selected
                ? (primarySelected
                    ? new Color(0.20f, 0.38f, 0.62f, 1f)
                    : new Color(0.18f, 0.32f, 0.46f, 1f))
                : Color.clear;

        if (selectionOutline != null)
        {
            if (selectionOutline.enabled != selected) selectionOutline.enabled = selected;
            Color outlineColor = primarySelected
                ? new Color(0.25f, 0.88f, 1f, 1f)
                : new Color(0.20f, 0.72f, 0.88f, 0.95f);
            // Shadow/Outline setters dirty the mesh even for the same value.
            // Selection polling must not rebuild every card's menu geometry.
            if (selectionOutline.effectColor != outlineColor)
                selectionOutline.effectColor = outlineColor;
            selectionOutline.effectDistance = new Vector2(3f, -3f);
            if (selectionOutline.useGraphicAlpha) selectionOutline.useGraphicAlpha = false;
        }
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
        if (flowAssignButton == null)
        {
            var t = transform.Find("Button_AssignFlow") ?? transform.Find("Row1/Button_AssignFlow");
            if (t != null) flowAssignButton = t.GetComponent<Button>();
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
                currentTaskText = details.Find("StatusRow/TaskSection/CurrentTask")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("TaskSection/CurrentTask")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("CurrentTask")?.GetComponent<TextMeshProUGUI>();
            if (assignmentsText == null)
                assignmentsText = details.Find("StatusRow/StationsSection/Assignments")?.GetComponent<TextMeshProUGUI>()
                    ?? details.Find("StationsSection/Assignments")?.GetComponent<TextMeshProUGUI>()
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
        RemoveLegacyWorkerControls();

        if (emp == null)
        {
            SetNameText("");
            RefreshDetails();
            ApplyExpandedState();
            return;
        }

        SetNameText(emp.employeeName ?? "Worker");
        DisableNameEditing();
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
        nextDetailsRefreshTime = Time.unscaledTime + DetailsRefreshInterval;

        if (employee == null)
        {
            SetDetailText(currentTaskText, "—");
            SetDetailText(assignmentsText, "FLOW: Unassigned");
            SetDetailText(heldItemsText, "Nothing");
            if (stationsLabel != null) stationsLabel.text = "—";
            return;
        }

        SetDetailText(currentTaskText, employee.GetCurrentTaskDescription());
        ProductionFlowPlan flow = production != null ? production.GetFlowForWorker(employee) : null;
        string flowName = flow != null ? flow.flowName : employee.assignedFlowName;
        if (string.IsNullOrWhiteSpace(flowName))
            flowName = employee.OperatedStationCount > 0 ? employee.GetAssignmentDetailText() : "Unassigned";
        SetDetailText(assignmentsText, "FLOW: " + flowName);

        if (stationsLabel != null)
            stationsLabel.gameObject.SetActive(false);
    }

    void EnsureLayout()
    {
        HideLegacyUi();

        var details = transform.Find("WorkerDetails");
        if (details == null || (details.Find("TaskSection") == null && details.Find("StatusRow/TaskSection") == null))
        {
            if (details != null)
                Destroy(details.gameObject);

            WorkerCardPrefabBuilder.BuildDetailsSection(transform, out currentTaskText, out assignmentsText, out heldItemsText);
        }

        ApplyCardLayout();
        BindReferences();
        EnsureExpandArrow();

        var detailsRoot = transform.Find("WorkerDetails");
        var carryingSection = detailsRoot != null ? detailsRoot.Find("CarryingSection") : null;
        if (carryingSection != null)
            carryingSection.gameObject.SetActive(false);
        if (detailsRoot != null && detailsRoot.gameObject.activeSelf != detailsExpanded)
            detailsRoot.gameObject.SetActive(detailsExpanded);
    }

    void RemoveLegacyWorkerControls()
    {
        Transform details = transform.Find("WorkerDetails");
        if (details == null) return;

        string[] obsoleteNames = { "PriorityControls", "AssignmentControls" };
        foreach (string obsoleteName in obsoleteNames)
        {
            Transform obsolete = details.Find(obsoleteName);
            if (obsolete != null) Destroy(obsolete.gameObject);
        }

        Transform section = details.Find("StatusRow/StationsSection")
            ?? details.Find("StationsSection");
        if (section == null) return;

        Transform obsoleteDropdown = section.Find("PriorityDropdown");
        if (obsoleteDropdown != null) Destroy(obsoleteDropdown.gameObject);
        TextMeshProUGUI header = section.Find("Header")?.GetComponent<TextMeshProUGUI>();
        if (header != null) header.text = "FLOW";
        if (assignmentsText != null && !assignmentsText.gameObject.activeSelf)
            assignmentsText.gameObject.SetActive(true);
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
            cardLe.minHeight = 88;
            cardLe.preferredHeight = -1;
        }
        else
        {
            cardLe.minHeight = 56;
            cardLe.preferredHeight = 56;
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
            rowLe.minHeight = 36;
            rowLe.preferredHeight = 36;

            var hlg = row1.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
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
                nameLe.minWidth = 96;
                nameLe.preferredWidth = -1;
                var nameLabel = nameInputObject.GetComponentInChildren<TextMeshProUGUI>(true);
                if (nameLabel != null)
                {
                    nameLabel.fontSize = 17f;
                    nameLabel.enableAutoSizing = true;
                    nameLabel.fontSizeMin = 13f;
                    nameLabel.fontSizeMax = 17f;
                    nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
                    nameLabel.overflowMode = TextOverflowModes.Truncate;
                }
                var legacyNameLabel = nameInputObject.GetComponentInChildren<Text>(true);
                if (legacyNameLabel != null) legacyNameLabel.fontSize = 17;
            }

            EnsureCustomizeButton(row1);
            EnsureFlowAssignButton(row1);

            if (flowAssignButton != null)
            {
                flowAssignButton.transform.SetSiblingIndex(2);
                var assignLe = flowAssignButton.GetComponent<LayoutElement>();
                if (assignLe == null) assignLe = flowAssignButton.gameObject.AddComponent<LayoutElement>();
                assignLe.minWidth = 120;
                assignLe.preferredWidth = 120;
                assignLe.flexibleWidth = 0;
                assignLe.minHeight = 34;
                assignLe.preferredHeight = 34;
            }

            if (customizeButton != null)
            {
                customizeButton.transform.SetSiblingIndex(3);
                var customizeLe = customizeButton.GetComponent<LayoutElement>();
                if (customizeLe == null) customizeLe = customizeButton.gameObject.AddComponent<LayoutElement>();
                customizeLe.minWidth = 88;
                customizeLe.preferredWidth = 88;
                customizeLe.flexibleWidth = 0;
            }

            if (fireButton != null)
            {
                fireButton.transform.SetAsLastSibling();
                var fireLe = fireButton.GetComponent<LayoutElement>();
                if (fireLe == null) fireLe = fireButton.gameObject.AddComponent<LayoutElement>();
                fireLe.minWidth = 68;
                fireLe.preferredWidth = 68;
                fireLe.flexibleWidth = 0;
            }
        }

        var details = transform.Find("WorkerDetails");
        if (details != null)
        {
            details.SetAsLastSibling();
            Transform statusRow = EnsureStatusRow(details);
            var detailsLe = details.GetComponent<LayoutElement>();
            if (detailsLe == null) detailsLe = details.gameObject.AddComponent<LayoutElement>();
            detailsLe.minHeight = 0;
            detailsLe.preferredHeight = -1;
            var detailsVlg = details.GetComponent<VerticalLayoutGroup>();
            if (detailsVlg != null)
                detailsVlg.spacing = 4;
            CompactSection(statusRow != null ? statusRow.Find("TaskSection") : null, 28);
            CompactSection(statusRow != null ? statusRow.Find("StationsSection") : null, 28);
            var carryingSection = details.Find("CarryingSection");
            if (carryingSection != null)
                carryingSection.gameObject.SetActive(false);
        }
    }

    void EnsureFlowAssignButton(Transform row1)
    {
        if (row1 == null) return;
        Transform existing = row1.Find("Button_AssignFlow");
        if (existing != null)
        {
            flowAssignButton = existing.GetComponent<Button>();
            return;
        }

        var go = new GameObject("Button_AssignFlow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(row1, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.25f, 0.48f, 0.36f, 1f);
        flowAssignButton = go.GetComponent<Button>();
        flowAssignButton.targetGraphic = image;
        GameUITheme.ApplyCompactControlEffects(flowAssignButton);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        RectTransform labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 1f);
        labelRect.offsetMax = new Vector2(-4f, -1f);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Assign";
        label.fontSize = 14f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = 14f;
        label.overflowMode = TextOverflowModes.Truncate;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
    }

    public void ConfigureFlowAssignment(string label, bool assignedToSelectedFlow, Action onClick)
    {
        if (flowAssignButton == null)
            EnsureFlowAssignButton(transform.Find("Row1"));
        if (flowAssignButton == null) return;

        var text = flowAssignButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.text = label;
        flowAssignButton.onClick.RemoveAllListeners();
        if (onClick != null) flowAssignButton.onClick.AddListener(() => onClick());
        flowAssignButton.interactable = true;
        var image = flowAssignButton.targetGraphic as Image;
        if (image != null)
            image.color = assignedToSelectedFlow
                ? new Color(0.58f, 0.27f, 0.26f, 1f)
                : new Color(0.25f, 0.48f, 0.36f, 1f);
    }

    static Transform EnsureStatusRow(Transform details)
    {
        if (details == null) return null;

        Transform statusRow = details.Find("StatusRow");
        if (statusRow == null)
        {
            var row = new GameObject("StatusRow", typeof(RectTransform));
            row.transform.SetParent(details, false);
            statusRow = row.transform;
        }

        var rowLe = statusRow.GetComponent<LayoutElement>();
        if (rowLe == null) rowLe = statusRow.gameObject.AddComponent<LayoutElement>();
        rowLe.minHeight = 28;
        rowLe.preferredHeight = 28;
        rowLe.flexibleWidth = 1;

        var rowLayout = statusRow.GetComponent<HorizontalLayoutGroup>();
        if (rowLayout == null) rowLayout = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(0, 0, 0, 0);
        rowLayout.spacing = 4;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        Transform flowSection = statusRow.Find("StationsSection") ?? details.Find("StationsSection");
        Transform taskSection = statusRow.Find("TaskSection") ?? details.Find("TaskSection");
        if (flowSection != null)
        {
            flowSection.SetParent(statusRow, false);
            flowSection.SetSiblingIndex(0);
            var flowLe = flowSection.GetComponent<LayoutElement>();
            if (flowLe != null)
            {
                flowLe.minWidth = 142f;
                flowLe.preferredWidth = 142f;
                flowLe.flexibleWidth = 0f;
            }
        }
        if (taskSection != null)
        {
            taskSection.SetParent(statusRow, false);
            taskSection.SetSiblingIndex(1);
            var taskLe = taskSection.GetComponent<LayoutElement>();
            if (taskLe != null)
            {
                taskLe.minWidth = 0f;
                taskLe.preferredWidth = 0f;
                taskLe.flexibleWidth = 1f;
            }

            var taskText = taskSection.Find("CurrentTask")?.GetComponent<TextMeshProUGUI>();
            if (taskText != null)
            {
                taskText.enableAutoSizing = false;
                taskText.textWrappingMode = TextWrappingModes.NoWrap;
                taskText.overflowMode = TextOverflowModes.Truncate;
                var fitter = taskText.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        statusRow.SetAsFirstSibling();
        return statusRow;
    }

    static void CompactSection(Transform section, float minHeight)
    {
        if (section == null) return;
        var header = section.Find("Header");
        if (header != null)
            header.gameObject.SetActive(false);
        var le = section.GetComponent<LayoutElement>();
        if (le != null)
        {
            le.minHeight = minHeight;
            le.preferredHeight = minHeight;
        }
        var vlg = section.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
            vlg.padding = new RectOffset(8, 8, 4, 4);

        foreach (var label in section.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.transform == header) continue;
            var labelLayout = label.GetComponent<LayoutElement>();
            if (labelLayout == null) continue;
            labelLayout.minHeight = 20;
            labelLayout.preferredHeight = 20;
        }
    }

    static void SetDetailText(TextMeshProUGUI label, string text)
    {
        if (label != null && label.text != text)
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

    void DisableNameEditing()
    {
        if (nameInputObject == null) return;
        var tmpInput = nameInputObject.GetComponent<TMP_InputField>();
        if (tmpInput != null)
        {
            tmpInput.readOnly = true;
            tmpInput.enabled = false;
        }
        var legacyInput = nameInputObject.GetComponent<InputField>();
        if (legacyInput != null)
        {
            legacyInput.readOnly = true;
            legacyInput.enabled = false;
        }
        RemoveNameListener();
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
        if (employee == null || production == null
            || (GameTimeManager.Instance != null && GameTimeManager.Instance.IsShiftInProgress)) return;
        WorkerCustomizePopup.Hide();
        production.FireWorker(employee);
        employee = null;
        var workersUI = GetComponentInParent<WorkersUI>();
        if (workersUI != null) workersUI.Refresh();
        else gameObject.SetActive(false);
    }
}
