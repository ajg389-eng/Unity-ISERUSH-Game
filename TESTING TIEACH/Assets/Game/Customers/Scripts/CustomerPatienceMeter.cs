using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Circular ring patience meter. Add to Customer root; uses child PatienceMeter if present.
/// </summary>
public class CustomerPatienceMeter : MonoBehaviour
{
    [Header("Layout")]
    public Vector3 offset = new Vector3(0f, 2.55f, 0f);
    public float scale = 0.012f;
    public float diameter = 64f;
    [Range(0.2f, 0.9f)]
    public float innerRadiusPercent = 0.58f;

    [Header("Prefab references (optional)")]
    public GameObject meterRoot;
    public Image backgroundImage;
    public Image fillImage;

    const int SpriteResolution = 128;

    static Sprite sharedRingSprite;

    Canvas canvas;
    float duration;
    float remaining;
    bool active;
    Action onExpired;

    public bool IsActive => active;
    public float Normalized => duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;

    void Awake()
    {
        EnsureHierarchy();
        if (meterRoot != null)
            meterRoot.SetActive(false);
    }

    public void Begin(float waitSeconds, Action expiredCallback)
    {
        EnsureHierarchy();
        ConfigureImages();

        duration = Mathf.Max(0.1f, waitSeconds);
        remaining = duration;
        onExpired = expiredCallback;
        active = true;

        if (meterRoot != null)
            meterRoot.SetActive(true);

        RefreshVisual();
    }

    public void Stop()
    {
        active = false;
        onExpired = null;
        if (meterRoot != null)
            meterRoot.SetActive(false);
    }

    void Update()
    {
        if (!active) return;

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            remaining = 0f;
            active = false;
            RefreshVisual();
            onExpired?.Invoke();
            onExpired = null;
            return;
        }

        RefreshVisual();
    }

    void LateUpdate()
    {
        if (canvas != null && Camera.main != null)
            canvas.transform.forward = Camera.main.transform.forward;
    }

    void RefreshVisual()
    {
        if (fillImage == null) return;

        float t = Normalized;
        fillImage.fillAmount = t;
        fillImage.color = EvaluateColor(t);
    }

    public static Color EvaluateColor(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        if (normalized > 0.5f)
            return Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.85f, 0.35f), (normalized - 0.5f) * 2f);
        return Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.1f), normalized * 2f);
    }

    public void EnsureHierarchy()
    {
        if (meterRoot == null || fillImage == null)
        {
            var existing = transform.Find("PatienceMeter");
            if (existing != null)
                BindFromRoot(existing.gameObject);
            else
                BuildHierarchy(transform);
        }

        if (meterRoot != null)
            canvas = meterRoot.GetComponent<Canvas>();

        ConfigureImages();
    }

    public void BindFromRoot(GameObject root)
    {
        meterRoot = root;
        canvas = root.GetComponent<Canvas>();

        if (backgroundImage == null)
        {
            var bg = root.transform.Find("Background");
            if (bg != null) backgroundImage = bg.GetComponent<Image>();
        }

        if (fillImage == null)
        {
            var fill = root.transform.Find("Fill");
            if (fill != null) fillImage = fill.GetComponent<Image>();
        }
    }

    public void ConfigureImages()
    {
        var sprite = GetRingSprite();

        if (backgroundImage != null)
        {
            backgroundImage.sprite = sprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.preserveAspect = true;
            backgroundImage.raycastTarget = false;
        }

        if (fillImage != null)
        {
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Radial360;
            fillImage.fillOrigin = (int)Image.Origin360.Top;
            fillImage.fillClockwise = false;
            fillImage.preserveAspect = true;
            fillImage.raycastTarget = false;
            fillImage.fillAmount = Normalized > 0f ? Normalized : 1f;
        }
    }

    public void BuildHierarchy(Transform parent)
    {
        var go = new GameObject("PatienceMeter");
        meterRoot = go;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(diameter, diameter);
        rt.localScale = Vector3.one * scale;

        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        Stretch(bgGo.transform as RectTransform);
        backgroundImage = bgGo.AddComponent<Image>();

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(go.transform, false);
        Stretch(fillGo.transform as RectTransform);
        fillImage = fillGo.AddComponent<Image>();

        ConfigureImages();
        if (backgroundImage != null)
            backgroundImage.color = new Color(0.1f, 0.1f, 0.12f, 0.9f);
        if (fillImage != null)
            fillImage.color = EvaluateColor(1f);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Sprite GetRingSprite()
    {
        if (sharedRingSprite != null) return sharedRingSprite;

        var tex = BuildRingTexture(SpriteResolution, 0.58f);
        sharedRingSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return sharedRingSprite;
    }

    public static Texture2D BuildRingTexture(int size, float innerRadiusPercent)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float center = size * 0.5f;
        float outerRadius = size * 0.46f;
        float innerRadius = outerRadius * Mathf.Clamp(innerRadiusPercent, 0.2f, 0.9f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float outerEdge = Mathf.Clamp01(outerRadius + 1.5f - dist);
                float innerEdge = Mathf.Clamp01(dist - (innerRadius - 1.5f));
                float alpha = Mathf.Clamp01(outerEdge * innerEdge);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        return tex;
    }
}
