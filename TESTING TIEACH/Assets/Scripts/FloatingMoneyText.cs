using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World-space green money popup that floats up and fades out (e.g. "+$20").
/// </summary>
public class FloatingMoneyText : MonoBehaviour
{
    public float lifetime = 1.4f;
    public float riseSpeed = 1.1f;
    public float startScale = 0.02f;
    public Vector3 offset = new Vector3(0f, 1.6f, 0f);

    TextMeshProUGUI label;
    float age;
    Color baseColor = new Color(0.25f, 0.95f, 0.4f, 1f);

    public static void Spawn(Vector3 worldPosition, int amount, Transform parent = null)
    {
        if (amount <= 0) return;

        var go = new GameObject("FloatingMoney");
        if (parent != null)
            go.transform.SetParent(parent, true);
        go.transform.position = worldPosition;

        var fx = go.AddComponent<FloatingMoneyText>();
        fx.Build("+$" + amount);
    }

    void Build(string text)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = gameObject.GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(10f, 2.5f);
        rt.localScale = Vector3.one * startScale;

        gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        label = textGo.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 28;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = baseColor;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        // Soft outline for readability
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color(0f, 0.25f, 0.1f, 0.85f);
    }

    void LateUpdate()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

        if (Camera.main != null)
            transform.forward = Camera.main.transform.forward;

        float t = Mathf.Clamp01(age / lifetime);
        if (label != null)
        {
            var c = baseColor;
            c.a = 1f - t;
            label.color = c;
        }

        // Slight pop at the start
        float pop = 1f + 0.15f * Mathf.Sin(Mathf.Clamp01(age * 8f) * Mathf.PI);
        transform.localScale = Vector3.one * (startScale * pop);

        if (age >= lifetime)
            Destroy(gameObject);
    }
}
