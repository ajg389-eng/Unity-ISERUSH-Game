using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a world-space progress bar above the employee's head while they work at a station.
/// Add to the same GameObject as KitchenEmployee (or ensure it can find KitchenEmployee in parent).
/// </summary>
public class EmployeeTaskBar : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 2f, 0f);
    public float scale = 0.02f;
    public float width = 1.2f;
    public float height = 0.15f;
    public Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    public Color fillColor = new Color(0.2f, 0.7f, 0.3f, 0.95f);

    KitchenEmployee employee;
    GameObject barRoot;
    Image fillImage;

    void Start()
    {
        employee = GetComponent<KitchenEmployee>();
        if (employee == null) employee = GetComponentInParent<KitchenEmployee>();
        if (employee == null) return;

        CreateBar();
    }

    void CreateBar()
    {
        if (barRoot != null) return;

        barRoot = new GameObject("TaskBar");
        barRoot.transform.SetParent(transform);
        barRoot.transform.localPosition = offset;
        barRoot.transform.localRotation = Quaternion.identity;
        barRoot.transform.localScale = Vector3.one;

        var canvas = barRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = barRoot.GetComponent<RectTransform>();
        if (rt == null) rt = barRoot.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width / scale, height / scale);
        rt.localScale = Vector3.one * scale;
        barRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(barRoot.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImage = bgGo.AddComponent<Image>();
        bgImage.color = backgroundColor;
        bgImage.sprite = CreatePixelSprite();

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barRoot.transform, false);
        var fillRect = fillGo.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillImage = fillGo.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.sprite = CreatePixelSprite();

        barRoot.SetActive(false);
    }

    static Sprite CreatePixelSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }

    void LateUpdate()
    {
        if (employee == null || barRoot == null || fillImage == null) return;

        barRoot.transform.localPosition = offset;
        if (Camera.main != null)
            barRoot.transform.forward = Camera.main.transform.forward;

        if (employee.ShowTaskBar)
        {
            barRoot.SetActive(true);
            float p = Mathf.Clamp01(employee.TaskProgress);
            var fillRect = fillImage.GetComponent<RectTransform>();
            if (fillRect != null)
            {
                fillRect.anchorMax = new Vector2(p, 1f);
                fillRect.offsetMax = Vector2.zero;
            }
        }
        else
        {
            barRoot.SetActive(false);
        }
    }
}
