using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// In Play mode: click an employee to show this panel. Four rows with two slots each (Work at, Deliver to).
/// Click an empty slot, then click a station (or register) in the game to assign it to that slot.
/// </summary>
public class EmployeeAssignmentPanel : MonoBehaviour
{
    public GameModeManager modeManager;
    [Tooltip("Layer for raycasting employees and stations.")]
    public LayerMask clickLayer = -1;

    [Header("Panel UI (optional; created at runtime if null)")]
    public GameObject panelRoot;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;
    public Transform assignmentListContainer;
    public GameObject assignmentRowPrefab;

    const int RowCount = 4;
    KitchenEmployee selectedEmployee;
    /// <summary>When set, next world click assigns to this slot. (rowIndex, 0=workAt, 1=deliverTo)</summary>
    (int row, int slot) selectedSlot = (-1, -1);

    /// <summary>UI row views: [rowIndex] = list of 2 slot buttons (work at, deliver to) and their labels.</summary>
    readonly List<(Button workAtBtn, TextMeshProUGUI workAtLabel, Button deliverToBtn, TextMeshProUGUI deliverToLabel)> slotViews = new List<(Button, TextMeshProUGUI, Button, TextMeshProUGUI)>();

    void Start()
    {
        if (modeManager == null) modeManager = FindObjectOfType<GameModeManager>();
        if (panelRoot != null) panelRoot.SetActive(false);

        // Replaced by ManagementModeController (click stations in Manage mode)
        enabled = false;
    }

    void Update()
    {
        if (modeManager != null && modeManager.CurrentMode != GameModeManager.Mode.Play)
        {
            if (selectedEmployee != null) ClosePanel();
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;
        if (Camera.main == null) return;
        if (IsPointerOverUI()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, clickLayer))
        {
            if (selectedEmployee != null && selectedSlot.row < 0)
                ClosePanel();
            return;
        }

        var emp = hit.collider.GetComponentInParent<KitchenEmployee>();
        if (emp != null)
        {
            SelectEmployee(emp);
            return;
        }

        if (selectedEmployee == null) return;

        if (selectedSlot.row >= 0)
        {
            AssignSelectedSlot(hit);
            return;
        }

        ClosePanel();
    }

    void AssignSelectedSlot(RaycastHit hit)
    {
        GameObject target = hit.collider.GetComponentInParent<FreezerStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<GrillStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<PantryStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<AssemblyStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<FryerStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<DrinkStation>()?.gameObject
            ?? hit.collider.GetComponentInParent<Register>()?.gameObject;
        if (target == null) return;

        selectedEmployee.EnsureAssignmentRows();
        if (selectedEmployee.assignmentRows == null || selectedSlot.row >= selectedEmployee.assignmentRows.Length) return;
        var row = selectedEmployee.assignmentRows[selectedSlot.row];
        if (row == null) return;

        if (selectedSlot.slot == 0)
            row.workAt = target;
        else
            row.deliverTo = target;

        selectedEmployee.SyncFromAssignmentRows();
        selectedSlot = (-1, -1);
        RefreshPanel();
    }

    bool IsPointerOverUI()
    {
        return UnityEngine.EventSystems.EventSystem.current != null &&
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    void SelectEmployee(KitchenEmployee emp)
    {
        selectedEmployee = emp;
        selectedSlot = (-1, -1);
        EnsurePanelExists();
        EnsureScrollContentLayout();
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshPanel();
    }

    /// <summary>Ensures the assignment list container (e.g. Scroll View Content) has layout so rows appear in the viewport.</summary>
    void EnsureScrollContentLayout()
    {
        if (assignmentListContainer == null) return;
        var rect = assignmentListContainer as RectTransform;
        if (rect != null)
        {
            var vlg = assignmentListContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
            {
                vlg = assignmentListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4;
                vlg.childForceExpandWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandHeight = false;
            }
            var csf = assignmentListContainer.GetComponent<ContentSizeFitter>();
            if (csf == null)
            {
                csf = assignmentListContainer.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
        }
    }

    void ClosePanel()
    {
        selectedEmployee = null;
        selectedSlot = (-1, -1);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void RefreshPanel()
    {
        if (selectedEmployee == null) return;
        if (nameText != null) nameText.text = string.IsNullOrEmpty(selectedEmployee.employeeName) ? "Worker" : selectedEmployee.employeeName;

        if (statusText != null)
        {
            if (selectedSlot.row >= 0)
            {
                string slotName = selectedSlot.slot == 0 ? "Work at" : "Deliver to";
                statusText.text = $"Row {selectedSlot.row + 1} – {slotName}: click a station or register in the game.";
            }
            else
                statusText.text = "Click a slot below, then click a station in the game to assign.";
        }

        selectedEmployee.EnsureAssignmentRows();
        EnsureSlotViews();
        for (int r = 0; r < RowCount; r++)
        {
            var row = selectedEmployee.assignmentRows != null && r < selectedEmployee.assignmentRows.Length ? selectedEmployee.assignmentRows[r] : null;
            GameObject workAt = row?.workAt;
            GameObject deliverTo = row?.deliverTo;
            if (r < slotViews.Count)
            {
                SetSlotLabel(slotViews[r].workAtLabel, workAt);
                SetSlotLabel(slotViews[r].deliverToLabel, deliverTo);
                int rowIndex = r;
                if (slotViews[r].workAtBtn != null)
                {
                    slotViews[r].workAtBtn.onClick.RemoveAllListeners();
                    slotViews[r].workAtBtn.onClick.AddListener(() => OnSlotClicked(rowIndex, 0));
                }
                if (slotViews[r].deliverToBtn != null)
                {
                    slotViews[r].deliverToBtn.onClick.RemoveAllListeners();
                    slotViews[r].deliverToBtn.onClick.AddListener(() => OnSlotClicked(rowIndex, 1));
                }
            }
        }
    }

    void SetSlotLabel(TextMeshProUGUI label, GameObject station)
    {
        if (label == null) return;
        if (station == null) { label.text = "Empty"; return; }
        var reg = station.GetComponent<Register>();
        if (reg != null) { label.text = station.name; return; }
        var t = KitchenEmployee.GetStationTypeFrom(station);
        label.text = t.HasValue ? t.Value.ToString() : station.name;
    }

    void OnSlotClicked(int rowIndex, int slotIndex)
    {
        selectedSlot = (rowIndex, slotIndex);
        RefreshPanel();
    }

    void EnsureSlotViews()
    {
        if (assignmentListContainer == null) return;
        while (slotViews.Count < RowCount)
        {
            int r = slotViews.Count;
            var (workAtBtn, workAtLabel, deliverToBtn, deliverToLabel) = CreateRowView(r);
            slotViews.Add((workAtBtn, workAtLabel, deliverToBtn, deliverToLabel));
        }
    }

    (Button workAtBtn, TextMeshProUGUI workAtLabel, Button deliverToBtn, TextMeshProUGUI deliverToLabel) CreateRowView(int rowIndex)
    {
        Button workAtBtn = null;
        TextMeshProUGUI workAtLabel = null;
        Button deliverToBtn = null;
        TextMeshProUGUI deliverToLabel = null;

        GameObject rowGo;
        if (assignmentRowPrefab != null)
        {
            rowGo = Instantiate(assignmentRowPrefab, assignmentListContainer);
            rowGo.transform.SetParent(assignmentListContainer, false);
            var rowRect = rowGo.transform as RectTransform;
            if (rowRect != null)
            {
                rowRect.anchorMin = new Vector2(0, 1);
                rowRect.anchorMax = new Vector2(1, 1);
                rowRect.pivot = new Vector2(0.5f, 1f);
                rowRect.anchoredPosition = Vector2.zero;
                rowRect.sizeDelta = new Vector2(0, 40);
                var le = rowGo.GetComponent<LayoutElement>();
                if (le == null) le = rowGo.AddComponent<LayoutElement>();
                le.minHeight = 36;
                le.preferredHeight = 40;
                le.flexibleWidth = 1;
                le.flexibleHeight = 0;
                EnsureRowVisible(rowGo, rowRect);
            }
            var rowHelper = rowGo.GetComponent<EmployeeAssignmentRowPrefab>();
            if (rowHelper != null)
            {
                workAtBtn = rowHelper.workAtButton;
                workAtLabel = rowHelper.workAtLabel;
                deliverToBtn = rowHelper.deliverToButton;
                deliverToLabel = rowHelper.deliverToLabel;
            }
            if (workAtBtn == null || deliverToBtn == null)
            {
                var btns = rowGo.GetComponentsInChildren<Button>(true);
                var tmps = rowGo.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (btns != null && btns.Length >= 2) { workAtBtn = btns[0]; deliverToBtn = btns[1]; }
                if (tmps != null && tmps.Length >= 2) { workAtLabel = tmps[0]; deliverToLabel = tmps[1]; }
            }
        }
        else
        {
            rowGo = CreateSimpleRowWithTwoSlots(rowIndex, out workAtBtn, out workAtLabel, out deliverToBtn, out deliverToLabel);
            if (rowGo != null)
                rowGo.transform.SetParent(assignmentListContainer, false);
        }

        return (workAtBtn, workAtLabel, deliverToBtn, deliverToLabel);
    }

    /// <summary>Makes instantiated row prefab visible: background + fixed-height slots so they don't stretch weird.</summary>
    void EnsureRowVisible(GameObject rowRoot, RectTransform rowRect)
    {
        if (rowRoot == null || rowRect == null) return;
        if (rowRoot.GetComponent<Image>() == null)
        {
            var img = rowRoot.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.28f, 0.98f);
            img.raycastTarget = true;
        }
        var hlg = rowRoot.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null)
        {
            hlg = rowRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(4, 4, 4, 4);
        }
        else
        {
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
        }
        int rowHeight = 40;
        for (int i = 0; i < rowRoot.transform.childCount; i++)
        {
            var child = rowRoot.transform.GetChild(i);
            var childRect = child as RectTransform;
            if (childRect == null) continue;
            childRect.anchorMin = Vector2.zero;
            childRect.anchorMax = Vector2.one;
            childRect.pivot = new Vector2(0.5f, 0.5f);
            childRect.offsetMin = Vector2.zero;
            childRect.offsetMax = Vector2.zero;
            var le = child.GetComponent<LayoutElement>();
            if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.minHeight = rowHeight - 8;
            le.preferredHeight = rowHeight - 8;
            le.flexibleHeight = 0;
            EnsureSlotButtonsHeight(child, rowHeight - 12);
        }
    }

    /// <summary>Give slot buttons (and their labels) a fixed height so they don't render as thin strips.</summary>
    void EnsureSlotButtonsHeight(Transform slotContainer, int slotHeight)
    {
        if (slotContainer == null || slotHeight <= 0) return;
        for (int i = 0; i < slotContainer.childCount; i++)
        {
            var slot = slotContainer.GetChild(i);
            var slotRect = slot as RectTransform;
            if (slotRect == null) continue;
            var le = slot.GetComponent<LayoutElement>();
            if (le == null) le = slot.gameObject.AddComponent<LayoutElement>();
            le.minHeight = slotHeight;
            le.preferredHeight = slotHeight;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;
            if (le.minWidth <= 0) le.minWidth = 60;
        }
    }

    GameObject CreateSimpleRowWithTwoSlots(int rowIndex, out Button workAtBtn, out TextMeshProUGUI workAtLabel, out Button deliverToBtn, out TextMeshProUGUI deliverToLabel)
    {
        workAtBtn = null;
        workAtLabel = null;
        deliverToBtn = null;
        deliverToLabel = null;

        var go = new GameObject("AssignmentRow_" + rowIndex, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(0, 28);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 28;
        le.preferredHeight = 28;

        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6;
        hl.childForceExpandWidth = true;
        hl.childControlWidth = true;

        var workAtGo = CreateSlotButton("Work at", out workAtBtn, out workAtLabel);
        workAtGo.transform.SetParent(go.transform, false);
        var deliverToGo = CreateSlotButton("Deliver to", out deliverToBtn, out deliverToLabel);
        deliverToGo.transform.SetParent(go.transform, false);

        return go;
    }

    static GameObject CreateSlotButton(string defaultLabel, out Button btn, out TextMeshProUGUI tmp)
    {
        var go = new GameObject("Slot", typeof(RectTransform));
        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        btn = go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minWidth = 80;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = defaultLabel;
        tmp.fontSize = 12;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        return go;
    }

    void EnsurePanelExists()
    {
        if (panelRoot != null) return;
        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        panelRoot = new GameObject("EmployeeAssignmentPanel", typeof(RectTransform));
        panelRoot.transform.SetParent(canvas.transform, false);
        var panelRect = (RectTransform)panelRoot.transform;
        panelRect.anchorMin = new Vector2(0, 1f);
        panelRect.anchorMax = new Vector2(0, 1f);
        panelRect.pivot = new Vector2(0, 1f);
        panelRect.anchoredPosition = new Vector2(20, -20);
        panelRect.sizeDelta = new Vector2(320, 260);
        var panelImg = panelRoot.AddComponent<Image>();
        panelImg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        var vlg = panelRoot.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;

        var nameGo = new GameObject("Name", typeof(RectTransform));
        nameGo.transform.SetParent(panelRoot.transform, false);
        nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.text = "Worker";
        nameText.fontSize = 18;
        nameText.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) nameText.font = TMP_Settings.defaultFontAsset;
        nameGo.AddComponent<LayoutElement>().minHeight = 28;

        var statusGo = new GameObject("Status", typeof(RectTransform));
        statusGo.transform.SetParent(panelRoot.transform, false);
        statusText = statusGo.AddComponent<TextMeshProUGUI>();
        statusText.text = "Click a slot, then click a station in the game.";
        statusText.fontSize = 12;
        statusText.color = new Color(0.85f, 0.85f, 0.9f, 1f);
        statusText.textWrappingMode = TextWrappingModes.Normal;
        if (TMP_Settings.defaultFontAsset != null) statusText.font = TMP_Settings.defaultFontAsset;
        statusGo.AddComponent<LayoutElement>().minHeight = 36;

        var listGo = new GameObject("AssignmentList", typeof(RectTransform));
        listGo.transform.SetParent(panelRoot.transform, false);
        assignmentListContainer = listGo.transform;
        listGo.AddComponent<VerticalLayoutGroup>().spacing = 4;
        listGo.AddComponent<LayoutElement>().flexibleHeight = 1;
        listGo.GetComponent<LayoutElement>().minHeight = 120;

        slotViews.Clear();
    }
}
