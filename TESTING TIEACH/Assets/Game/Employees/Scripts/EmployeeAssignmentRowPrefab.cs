using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Optional: attach to your assignment ROW prefab (one row = two slots).
/// EmployeeAssignmentPanel expects each row prefab to have, in hierarchy order:
/// - First two Buttons (in children) = Work at slot, Deliver to slot
/// - First two TextMeshProUGUI (in children) = labels for those slots
/// This component documents that and can auto-find refs for validation.
/// </summary>
public class EmployeeAssignmentRowPrefab : MonoBehaviour
{
    [Tooltip("First slot: 'Work at' station. Second: 'Deliver to' station.")]
    public Button workAtButton;
    public TextMeshProUGUI workAtLabel;
    public Button deliverToButton;
    public TextMeshProUGUI deliverToLabel;

    void OnValidate()
    {
        if (workAtButton != null && workAtLabel == null)
            workAtLabel = workAtButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (deliverToButton != null && deliverToLabel == null)
            deliverToLabel = deliverToButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }
}
