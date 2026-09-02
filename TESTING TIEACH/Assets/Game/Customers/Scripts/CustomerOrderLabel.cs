using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// World-space label above a customer showing their order. Created by CustomerAI.
/// </summary>
public class CustomerOrderLabel : MonoBehaviour
{
    public Vector3 offset = new Vector3(0, 2.2f, 0);
    public float scale = 0.015f;

    TextMeshProUGUI labelText;
    Canvas canvas;

    public void Setup(Transform customerTransform, string orderText)
    {
        CreateCanvas(customerTransform, orderText);
    }

    void CreateCanvas(Transform parent, string orderText)
    {
        var existing = parent.Find("OrderLabel");
        if (existing != null) Destroy(existing.gameObject);

        var go = new GameObject("OrderLabel");
        go.transform.SetParent(parent);
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(12f, 1.5f);
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
        labelText.text = orderText;
        labelText.fontSize = 12;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Overflow;
        if (TMP_Settings.defaultFontAsset != null)
            labelText.font = TMP_Settings.defaultFontAsset;
    }

    public void SetText(string orderText)
    {
        if (labelText != null)
            labelText.text = orderText;
    }

    void LateUpdate()
    {
        if (canvas != null && Camera.main != null)
            canvas.transform.forward = Camera.main.transform.forward;
    }
}
