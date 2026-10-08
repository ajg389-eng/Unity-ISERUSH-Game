using UnityEngine;

/// <summary>Small world-space radial meter shown while a station is processing food.</summary>
public sealed class StationProcessProgressIndicator : MonoBehaviour
{
    const float WorldScale = 0.006f;
    const float Diameter = 46f;
    const float HeightPadding = 0.45f;
    static Sprite circleSprite;

    Canvas canvas;
    UnityEngine.UI.Image fill;
    Camera cachedCamera;

    public static StationProcessProgressIndicator Ensure(GameObject station)
    {
        if (station == null) return null;
        var indicator = station.GetComponent<StationProcessProgressIndicator>();
        return indicator != null ? indicator : station.AddComponent<StationProcessProgressIndicator>();
    }

    public void SetProgress(bool processing, float normalizedProgress)
    {
        EnsureVisuals();
        canvas.gameObject.SetActive(processing);
        if (processing)
            fill.fillAmount = Mathf.Clamp01(normalizedProgress);
    }

    void LateUpdate()
    {
        if (canvas == null || !canvas.gameObject.activeSelf) return;
        UpdateWorldTransform();
    }

    void OnDestroy()
    {
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    void UpdateWorldTransform()
    {
        if (canvas == null) return;

        canvas.transform.position = FindWorldPosition();
        canvas.transform.localScale = Vector3.one * WorldScale;
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            cachedCamera = Camera.main;
        if (cachedCamera != null)
        {
            Vector3 cameraToIndicator = canvas.transform.position - cachedCamera.transform.position;
            if (cameraToIndicator.sqrMagnitude > 0.0001f)
                canvas.transform.rotation = Quaternion.LookRotation(
                    cameraToIndicator.normalized, cachedCamera.transform.up);
        }
    }

    void EnsureVisuals()
    {
        if (canvas != null) return;

        var root = new GameObject("ProcessProgress", typeof(RectTransform), typeof(Canvas));

        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;
        var canvasRect = (RectTransform)root.transform;
        canvasRect.sizeDelta = new Vector2(Diameter, Diameter);
        UpdateWorldTransform();

        CreateCircle(root.transform, "Background", new Color(0.055f, 0.07f, 0.09f, 0.92f), false);
        fill = CreateCircle(root.transform, "Fill", new Color(0.20f, 0.85f, 0.55f, 1f), true);

        var center = CreateCircle(root.transform, "Center", new Color(0.09f, 0.11f, 0.14f, 1f), false);
        center.rectTransform.sizeDelta = new Vector2(Diameter * 0.56f, Diameter * 0.56f);
    }

    Vector3 FindWorldPosition()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return transform.position + Vector3.up * (1.5f + HeightPadding);

        float top = transform.position.y + 1.5f;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
                top = Mathf.Max(top, renderers[i].bounds.max.y);
        }
        return new Vector3(transform.position.x, top + HeightPadding, transform.position.z);
    }

    static UnityEngine.UI.Image CreateCircle(Transform parent, string objectName, Color color, bool radial)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(Diameter, Diameter);

        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.sprite = GetCircleSprite();
        image.color = color;
        image.raycastTarget = false;
        if (radial)
        {
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            image.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
        }
        return image;
    }

    static Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeProcessCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
            byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(radius + 1f - distance));
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        circleSprite.name = "RuntimeProcessCircleSprite";
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }
}
