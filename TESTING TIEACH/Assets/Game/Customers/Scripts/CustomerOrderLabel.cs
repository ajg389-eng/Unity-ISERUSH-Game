using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// World-space order label. Add to Customer root; uses child OrderLabel if present.
/// Shows spaced food icons for the current order.
/// </summary>
public class CustomerOrderLabel : MonoBehaviour
{
    const float IconPixels = 32f;
    const float IconGap = 18f;
    const float PanelPad = 8f;
    const float IconWorldScale = 0.01f;
    const float MaxWorldWidth = 1.05f;
    const float MeterClearance = 0.14f;

    [Header("Layout")]
    public Vector3 offset = new Vector3(0f, 2.2f, 0f);
    public float scale = 0.015f;
    public Vector2 size = new Vector2(12f, 1.5f);
    public float fontSize = 12f;

    [Header("Prefab references (optional)")]
    public GameObject labelRoot;
    public TextMeshProUGUI labelText;

    Canvas canvas;
    [System.NonSerialized] RectTransform iconsRow;
    [System.NonSerialized] Image panelImage;
    [System.NonSerialized] List<Image> iconSlots = new List<Image>();

    static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
    static Sprite panelSprite;

    void Awake()
    {
        if (iconSlots == null)
            iconSlots = new List<Image>();
        EnsureHierarchy();
    }

    public void EnsureHierarchy()
    {
        if (labelText != null && labelRoot != null)
        {
            canvas = labelRoot.GetComponent<Canvas>();
            EnsureIconsRow();
            return;
        }

        var existing = transform.Find("OrderLabel");
        if (existing != null)
        {
            BindFromRoot(existing.gameObject);
            EnsureIconsRow();
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

        EnsureIconsRow();
    }

    public void SetOrder(CustomerOrder order, bool isComplete)
    {
        if (labelRoot == null || labelText == null)
            EnsureHierarchy();

        if (isComplete)
        {
            SetText("Done!");
            return;
        }

        if (order == null || order.lines == null)
        {
            SetText("No order");
            return;
        }

        var items = new List<ItemDefinition>();
        foreach (var line in order.lines)
        {
            if (line.item == null || line.quantity <= 0)
                continue;
            int copies = Mathf.Max(1, line.quantity);
            for (int i = 0; i < copies; i++)
                items.Add(line.item);
        }

        if (items.Count == 0)
        {
            SetText("No order");
            return;
        }

        ShowIcons(items);
    }

    public void SetText(string orderText)
    {
        if (labelText == null)
            EnsureHierarchy();

        HideIcons();
        RestoreTextLayout();

        if (labelText != null)
        {
            labelText.gameObject.SetActive(true);
            labelText.text = orderText;
        }
    }

    void ShowIcons(List<ItemDefinition> items)
    {
        EnsureIconsRow();
        if (labelRoot == null || iconsRow == null)
            return;
        if (iconSlots == null)
            iconSlots = new List<Image>();

        if (labelText != null)
            labelText.gameObject.SetActive(false);

        iconsRow.gameObject.SetActive(true);

        int count = items.Count;
        float width = PanelPad * 2f + count * IconPixels + Mathf.Max(0, count - 1) * IconGap;
        float height = PanelPad * 2f + IconPixels;
        float worldScale = IconWorldScale;
        if (width * worldScale > MaxWorldWidth)
            worldScale = MaxWorldWidth / width;

        var canvasRt = labelRoot.GetComponent<RectTransform>();
        if (canvasRt != null)
        {
            canvasRt.sizeDelta = new Vector2(width, height);
            canvasRt.localScale = Vector3.one * worldScale;
        }

        if (canvas != null)
            canvas.sortingOrder = 2;

        if (panelImage != null)
        {
            panelImage.gameObject.SetActive(true);
            panelImage.sprite = GetPanelSprite();
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            panelImage.raycastTarget = false;
        }

        while (iconSlots.Count < count)
            iconSlots.Add(CreateIconSlot(iconSlots.Count));

        for (int i = 0; i < iconSlots.Count; i++)
        {
            bool used = i < count;
            var image = iconSlots[i];
            if (image == null)
                continue;

            image.gameObject.SetActive(used);
            if (!used)
                continue;

            float x = (i - (count - 1) * 0.5f) * (IconPixels + IconGap);
            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(IconPixels, IconPixels);
            rt.anchoredPosition = new Vector2(x, 0f);

            image.sprite = GetFoodSprite(items[i]);
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
        }
    }

    void HideIcons()
    {
        if (iconsRow != null)
            iconsRow.gameObject.SetActive(false);
        if (panelImage != null)
            panelImage.gameObject.SetActive(false);
    }

    void RestoreTextLayout()
    {
        if (labelRoot == null)
            return;

        var rt = labelRoot.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.sizeDelta = size;
        rt.localScale = Vector3.one * scale;
        rt.localPosition = offset;
    }

    void EnsureIconsRow()
    {
        if (labelRoot == null)
            return;

        if (iconsRow == null)
        {
            var existing = labelRoot.transform.Find("Icons");
            if (existing != null)
                iconsRow = existing as RectTransform;
        }

        if (iconsRow == null)
        {
            var iconsGo = new GameObject("Icons", typeof(RectTransform));
            iconsGo.transform.SetParent(labelRoot.transform, false);
            iconsRow = iconsGo.GetComponent<RectTransform>();
            iconsRow.anchorMin = Vector2.zero;
            iconsRow.anchorMax = Vector2.one;
            iconsRow.offsetMin = Vector2.zero;
            iconsRow.offsetMax = Vector2.zero;
            iconsRow.gameObject.SetActive(false);
        }

        EnsurePanel();
    }

    void EnsurePanel()
    {
        if (labelRoot == null)
            return;

        if (panelImage == null)
        {
            var existing = labelRoot.transform.Find("Panel");
            if (existing != null)
                panelImage = existing.GetComponent<Image>();
        }

        if (panelImage != null)
            return;

        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.transform.SetParent(labelRoot.transform, false);
        panelGo.transform.SetAsFirstSibling();
        var rt = panelGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        panelImage = panelGo.AddComponent<Image>();
        panelImage.sprite = GetPanelSprite();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        panelImage.raycastTarget = false;
        panelImage.gameObject.SetActive(false);
    }

    Image CreateIconSlot(int index)
    {
        var go = new GameObject("Icon" + index, typeof(RectTransform));
        go.transform.SetParent(iconsRow, false);
        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    static Sprite GetFoodSprite(ItemDefinition item)
    {
        string key = "item";
        if (item != null)
        {
            if (!string.IsNullOrEmpty(item.itemName))
                key = item.itemName;
            else if (!string.IsNullOrEmpty(item.name))
                key = item.name;
        }

        string cacheKey = key.ToLowerInvariant();
        if (iconCache.TryGetValue(cacheKey, out Sprite cached) && cached != null)
            return cached;

        Texture2D tex;
        if (cacheKey.Contains("burger"))
            tex = DrawBurger();
        else if (cacheKey.Contains("fries") || cacheKey.Contains("fry"))
            tex = DrawFries();
        else if (cacheKey.Contains("drink") || cacheKey.Contains("soda") || cacheKey.Contains("cup"))
            tex = DrawDrink();
        else
            tex = DrawGeneric(cacheKey);

        var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 64f);
        sprite.name = key + "Icon";
        iconCache[cacheKey] = sprite;
        return sprite;
    }

    static Texture2D DrawBurger()
    {
        int s = 64;
        var tex = Blank(s);
        FillEllipse(tex, 32, 22, 22, 8, new Color(0.83f, 0.55f, 0.22f));
        FillRect(tex, 10, 24, 44, 6, new Color(0.45f, 0.22f, 0.08f));
        FillRect(tex, 11, 30, 42, 5, new Color(0.95f, 0.78f, 0.18f));
        FillRect(tex, 12, 35, 40, 5, new Color(0.35f, 0.72f, 0.28f));
        FillEllipse(tex, 32, 44, 22, 11, new Color(0.91f, 0.64f, 0.28f));
        FillEllipse(tex, 22, 48, 2, 2, new Color(0.95f, 0.9f, 0.75f));
        FillEllipse(tex, 32, 50, 2, 2, new Color(0.95f, 0.9f, 0.75f));
        FillEllipse(tex, 42, 47, 2, 2, new Color(0.95f, 0.9f, 0.75f));
        tex.Apply();
        return tex;
    }

    static Texture2D DrawFries()
    {
        int s = 64;
        var tex = Blank(s);
        FillRect(tex, 16, 8, 32, 28, new Color(0.86f, 0.18f, 0.18f));
        FillRect(tex, 18, 30, 6, 22, new Color(0.98f, 0.78f, 0.18f));
        FillRect(tex, 26, 32, 6, 24, new Color(1f, 0.84f, 0.25f));
        FillRect(tex, 34, 29, 6, 23, new Color(0.96f, 0.72f, 0.16f));
        FillRect(tex, 42, 33, 6, 20, new Color(1f, 0.8f, 0.22f));
        tex.Apply();
        return tex;
    }

    static Texture2D DrawDrink()
    {
        int s = 64;
        var tex = Blank(s);
        FillRect(tex, 18, 10, 28, 34, new Color(0.9f, 0.2f, 0.22f));
        FillRect(tex, 20, 12, 24, 26, new Color(0.45f, 0.78f, 0.95f));
        FillRect(tex, 16, 42, 32, 6, new Color(0.95f, 0.95f, 0.95f));
        FillRect(tex, 40, 44, 4, 14, new Color(0.95f, 0.95f, 0.95f));
        tex.Apply();
        return tex;
    }

    static Texture2D DrawGeneric(string key)
    {
        int s = 64;
        var tex = Blank(s);
        float hue = (Mathf.Abs(key.GetHashCode()) % 100) / 100f;
        var color = Color.HSVToRGB(hue, 0.55f, 0.95f);
        FillEllipse(tex, 32, 32, 20, 20, color);
        tex.Apply();
        return tex;
    }

    static Texture2D Blank(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, clear);
        }
        return tex;
    }

    static void FillRect(Texture2D tex, int x, int y, int w, int h, Color color)
    {
        int x1 = Mathf.Clamp(x + w, 0, tex.width);
        int y1 = Mathf.Clamp(y + h, 0, tex.height);
        x = Mathf.Clamp(x, 0, tex.width);
        y = Mathf.Clamp(y, 0, tex.height);
        for (int py = y; py < y1; py++)
        {
            for (int px = x; px < x1; px++)
                tex.SetPixel(px, py, color);
        }
    }

    static void FillEllipse(Texture2D tex, int cx, int cy, int rx, int ry, Color color)
    {
        float rx2 = Mathf.Max(1f, rx * rx);
        float ry2 = Mathf.Max(1f, ry * ry);
        int x0 = Mathf.Max(0, cx - rx);
        int x1 = Mathf.Min(tex.width - 1, cx + rx);
        int y0 = Mathf.Max(0, cy - ry);
        int y1 = Mathf.Min(tex.height - 1, cy + ry);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                if ((dx * dx) / rx2 + (dy * dy) / ry2 <= 1f)
                    tex.SetPixel(x, y, color);
            }
        }
    }

    static Sprite GetPanelSprite()
    {
        if (panelSprite != null)
            return panelSprite;

        var tex = DrawRoundedPanel(64, 12);
        panelSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            64f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(16f, 16f, 16f, 16f));
        panelSprite.name = "OrderPanel";
        return panelSprite;
    }

    static Texture2D DrawRoundedPanel(int size, int radius)
    {
        var tex = Blank(size);
        int r2 = radius * radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = (x >= radius && x < size - radius) || (y >= radius && y < size - radius);
                if (!inside)
                {
                    int cx = Mathf.Clamp(x, radius, size - radius - 1);
                    int cy = Mathf.Clamp(y, radius, size - radius - 1);
                    float dx = x - cx;
                    float dy = y - cy;
                    inside = dx * dx + dy * dy <= r2;
                }
                if (inside)
                    tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();
        return tex;
    }

    void LateUpdate()
    {
        if (canvas == null && labelRoot != null)
            canvas = labelRoot.GetComponent<Canvas>();
        if (canvas == null || Camera.main == null)
            return;

        Transform canvasTf = canvas.transform;
        canvasTf.forward = Camera.main.transform.forward;

        float ringRadius = 0.4f;
        var patience = GetComponent<CustomerPatienceMeter>();
        Transform meter = transform.Find("PatienceMeter");
        Vector3 meterPos = transform.position + Vector3.up * 2.55f;
        if (patience != null)
            ringRadius = patience.diameter * 0.5f * patience.scale;
        if (meter != null && meter.gameObject.activeInHierarchy)
            meterPos = meter.position;

        var canvasRt = canvasTf as RectTransform;
        float iconHalf = 0.2f;
        if (canvasRt != null)
            iconHalf = canvasRt.sizeDelta.y * 0.5f * Mathf.Abs(canvasRt.lossyScale.y);

        canvasTf.position = meterPos + Camera.main.transform.up * (ringRadius + iconHalf + MeterClearance);
    }
}
