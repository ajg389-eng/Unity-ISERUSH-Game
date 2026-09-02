using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// World-space order label. Add to Customer root; uses child OrderLabel if present.
/// </summary>
public class CustomerOrderLabel : MonoBehaviour
{
    [Header("Layout")]
    public Vector3 offset = new Vector3(0f, 2.2f, 0f);
    public float scale = 0.015f;
    public Vector2 size = new Vector2(12f, 1.5f);
    public float fontSize = 12f;

    [Header("Prefab references (optional)")]
    public GameObject labelRoot;
    public TextMeshProUGUI labelText;

    Canvas canvas;

    void Awake()
    {
        EnsureHierarchy();
    }

    public void EnsureHierarchy()
    {
        if (labelText != null && labelRoot != null)
        {
            canvas = labelRoot.GetComponent<Canvas>();
            return;
        }

        var existing = transform.Find("OrderLabel");
        if (existing != null)
        {
            BindFromRoot(existing.gameObject);
            return;
        }

        BuildHierarchy(transform, "Order");
    }

    public void BindFromRoot(GameObject root)
    {
        labelRoot = root;
        canvas = root.GetComponent<Canvas>();
        if (labelText == null)
        {
            var text = root.transform.Find("Text");
            if (text != null)
                labelText = text.GetComponent<TextMeshProUGUI>();
        }
    }

    public void BuildHierarchy(Transform parent, string initialText)
    {
        var go = new GameObject("OrderLabel");
        labelRoot = go;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;

        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRT = textGo.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        labelText = textGo.AddComponent<TextMeshProUGUI>();
        labelText.text = initialText;
        labelText.fontSize = fontSize;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Overflow;
        labelText.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            labelText.font = TMP_Settings.defaultFontAsset;
    }

    public void SetText(string orderText)
    {
        if (labelText == null)
            EnsureHierarchy();
        if (labelText != null)
            labelText.text = orderText;
    }

    void LateUpdate()
    {
        if (canvas != null && Camera.main != null)
            canvas.transform.forward = Camera.main.transform.forward;
    }
}
