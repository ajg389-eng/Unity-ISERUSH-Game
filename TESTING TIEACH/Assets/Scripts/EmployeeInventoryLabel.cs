using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World-space label above a worker showing what they are currently holding.
/// </summary>
public class EmployeeInventoryLabel : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 2.35f, 0f);
    public float scale = 0.014f;
    public Color textColor = new Color(1f, 0.95f, 0.75f, 1f);
    public Color emptyColor = new Color(0.7f, 0.72f, 0.78f, 0.85f);
    [Tooltip("If true, hide the label when the worker holds nothing.")]
    public bool hideWhenEmpty = true;

    KitchenEmployee employee;
    Canvas canvas;
    TextMeshProUGUI labelText;
    Image background;
    string lastText;

    void Start()
    {
        employee = GetComponent<KitchenEmployee>();
        if (employee == null) employee = GetComponentInParent<KitchenEmployee>();
        if (employee == null) return;
        EnsureLabel();
    }

    void EnsureLabel()
    {
        if (canvas != null) return;

        var existing = transform.Find("InventoryLabel");
        if (existing != null) Destroy(existing.gameObject);

        var go = new GameObject("InventoryLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(14f, 2.2f);
        rt.localScale = Vector3.one * scale;

        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(-4f, -2f);
        bgRt.offsetMax = new Vector2(4f, 2f);
        background = bgGo.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.82f);
        background.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        labelText = textGo.AddComponent<TextMeshProUGUI>();
        labelText.fontSize = 11;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = textColor;
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.overflowMode = TextOverflowModes.Overflow;
        labelText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            labelText.font = TMP_Settings.defaultFontAsset;

        go.SetActive(false);
    }

    void LateUpdate()
    {
        if (employee == null)
        {
            employee = GetComponent<KitchenEmployee>();
            if (employee == null) return;
        }
        EnsureLabel();
        if (canvas == null || labelText == null) return;

        canvas.transform.localPosition = offset;
        if (Camera.main != null)
            canvas.transform.forward = Camera.main.transform.forward;

        string text = employee.GetHeldInventoryDisplay();
        bool empty = string.IsNullOrEmpty(text);

        if (empty && hideWhenEmpty)
        {
            canvas.gameObject.SetActive(false);
            lastText = null;
            return;
        }

        canvas.gameObject.SetActive(true);
        string display = empty ? "Holding: —" : "Holding: " + text;
        if (display != lastText)
        {
            lastText = display;
            labelText.text = display;
            labelText.color = empty ? emptyColor : textColor;
        }
    }
}
