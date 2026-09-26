using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Applies a cohesive visual language to existing and runtime-created screen UI.
/// World-space labels and gameplay markers are deliberately excluded.
/// </summary>
public class GameUITheme : MonoBehaviour
{
    // Casual restaurant palette: quiet foundations with restrained warm accents.
    public static readonly Color Charcoal = Hex(0x23, 0x28, 0x30);
    public static readonly Color PanelSlate = Hex(0x2D, 0x34, 0x3E);
    public static readonly Color RaisedSlate = Hex(0x3A, 0x44, 0x50);
    public static readonly Color RaisedSlateHover = Hex(0x4A, 0x56, 0x63);
    public static readonly Color Coral = Hex(0xD9, 0x7A, 0x5F);
    public static readonly Color Peach = Hex(0xE9, 0xA1, 0x7E);
    public static readonly Color Mustard = Hex(0xD8, 0xB3, 0x65);
    public static readonly Color Sage = Hex(0x6F, 0x9D, 0x89);
    public static readonly Color SageHover = Hex(0x86, 0xB3, 0x9F);
    public static readonly Color MutedRed = Hex(0xB8, 0x5C, 0x5C);
    public static readonly Color MutedRedHover = Hex(0xC9, 0x6A, 0x65);
    public static readonly Color Cream = Hex(0xF2, 0xEE, 0xE7);
    public static readonly Color MutedText = Hex(0xB7, 0xBE, 0xC5);

    public static readonly Color Backdrop = WithAlpha(Charcoal, 0.97f);
    public static readonly Color Panel = WithAlpha(PanelSlate, 0.97f);
    public static readonly Color Surface = RaisedSlate;
    public static readonly Color SurfaceHover = RaisedSlateHover;
    public static readonly Color Positive = Sage;
    public static readonly Color PositiveHover = SageHover;
    public static readonly Color Danger = MutedRed;
    public static readonly Color DangerHover = MutedRedHover;
    public static readonly Color Accent = Mustard;
    public static readonly Color PositiveAccent = SageHover;
    public static readonly Color DangerAccent = Peach;
    public static readonly Color TextPrimary = Cream;
    public static readonly Color TextSecondary = MutedText;
    public static readonly Color Edge = new Color(0.08f, 0.095f, 0.115f, 0.82f);

    const float RefreshInterval = 0.6f;
    float nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<GameUITheme>() != null) return;
        new GameObject("GameUITheme").AddComponent<GameUITheme>();
    }

    void OnEnable() => ApplyToAllCanvases();

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        ApplyToAllCanvases();
    }

    public static void ApplyToAllCanvases()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var canvas in canvases)
        {
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
            ApplyCanvas(canvas);
        }
    }

    static void ApplyCanvas(Canvas canvas)
    {
        foreach (var image in canvas.GetComponentsInChildren<Image>(true)) StyleContainer(image);
        foreach (var button in canvas.GetComponentsInChildren<Button>(true)) StyleButton(button);
        foreach (var input in canvas.GetComponentsInChildren<TMP_InputField>(true)) StyleInput(input);
        foreach (var slider in canvas.GetComponentsInChildren<Slider>(true)) StyleSlider(slider);
        foreach (var toggle in canvas.GetComponentsInChildren<Toggle>(true)) StyleToggle(toggle);
        foreach (var scrollbar in canvas.GetComponentsInChildren<Scrollbar>(true)) StyleScrollbar(scrollbar);
        foreach (var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true)) StyleText(text);
    }

    static void StyleContainer(Image image)
    {
        if (image == null || image.GetComponent<GameUIThemeStyled>() != null) return;
        if (image.GetComponent<Button>() != null || image.GetComponent<Toggle>() != null) return;
        if (image.sprite != null || image.color.a < 0.18f) return;

        string name = image.name.ToLowerInvariant();
        if (!ContainsAny(name, "panel", "popup", "window", "dialog", "card", "box", "section",
                "header", "footer", "content", "background", "backdrop", "frame", "bar", "strip")) return;

        Color target = ContainsAny(name, "backdrop", "overlay", "dim") ? Backdrop
            : ContainsAny(name, "header", "section", "footer", "frame") ? Surface : Panel;
        target.a = image.color.a;
        image.color = target;
        image.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleButton(Button button)
    {
        if (button == null || button.GetComponent<GameUIThemeStyled>() != null) return;
        if (button.GetComponentInParent<TitleScreenController>() != null) return;
        var image = button.GetComponent<Image>();
        if (image == null) return;

        var tmp = button.GetComponentInChildren<TextMeshProUGUI>(true);
        var legacy = tmp == null ? button.GetComponentInChildren<Text>(true) : null;
        string label = tmp != null ? tmp.text : legacy != null ? legacy.text : string.Empty;
        // Image-only buttons are often preview click targets, dimmers, swatches,
        // or model thumbnails. Their existing visuals carry functional meaning.
        if (string.IsNullOrWhiteSpace(label))
        {
            button.gameObject.AddComponent<GameUIThemeStyled>();
            return;
        }
        string key = (button.name + " " + label).ToLowerInvariant();
        bool tab = ContainsAny(key, "tab", "previous", "nextpage");
        bool positive = IsPositive(key, image.color);
        bool danger = IsDanger(key, image.color);
        Color normal = danger ? Danger : positive ? Positive : Surface;
        Color hover = danger ? DangerHover : positive ? PositiveHover : SurfaceHover;
        Color accent = danger ? DangerAccent : positive ? PositiveAccent : Accent;

        if (!tab)
        {
            image.color = normal;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = hover;
            colors.pressedColor = Color.Lerp(hover, Color.black, 0.2f);
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.42f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        if (tmp != null)
        {
            tmp.color = TextPrimary;
            tmp.fontStyle |= FontStyles.Bold;
            tmp.raycastTarget = false;
        }
        if (legacy != null)
        {
            legacy.color = TextPrimary;
            legacy.fontStyle = FontStyle.Bold;
            legacy.raycastTarget = false;
        }

        var rect = button.transform as RectTransform;
        bool major = !tab && !string.IsNullOrWhiteSpace(label) && rect != null
            && rect.rect.width >= 92f && rect.rect.height >= 34f;
        AddEdge(button.gameObject, major ? new Vector2(2f, -2f) : new Vector2(1f, -1f));
        if (major)
        {
            EnsureAccent(button.transform, accent);
            if (button.GetComponent<GameUIThemeButtonMotion>() == null)
                button.gameObject.AddComponent<GameUIThemeButtonMotion>();
        }
        button.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleInput(TMP_InputField input)
    {
        if (input == null || input.GetComponent<GameUIThemeStyled>() != null) return;
        var image = input.GetComponent<Image>();
        if (image != null) image.color = Surface;
        if (input.textComponent != null) input.textComponent.color = TextPrimary;
        if (input.placeholder is TMP_Text placeholder) placeholder.color = TextSecondary;
        AddEdge(input.gameObject, new Vector2(1f, -1f));
        input.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleSlider(Slider slider)
    {
        if (slider == null || slider.GetComponent<GameUIThemeStyled>() != null) return;
        foreach (var image in slider.GetComponentsInChildren<Image>(true))
        {
            string name = image.name.ToLowerInvariant();
            if (name.Contains("fill")) image.color = Accent;
            else if (name.Contains("handle")) image.color = TextPrimary;
            else if (name.Contains("background")) image.color = Backdrop;
        }
        slider.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleToggle(Toggle toggle)
    {
        if (toggle == null || toggle.GetComponent<GameUIThemeStyled>() != null) return;
        if (toggle.targetGraphic is Image background) background.color = Surface;
        if (toggle.graphic is Image check) check.color = PositiveAccent;
        AddEdge(toggle.gameObject, new Vector2(1f, -1f));
        toggle.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleScrollbar(Scrollbar scrollbar)
    {
        if (scrollbar == null || scrollbar.GetComponent<GameUIThemeStyled>() != null) return;
        var background = scrollbar.GetComponent<Image>();
        if (background != null) background.color = new Color(Backdrop.r, Backdrop.g, Backdrop.b, 0.9f);
        if (scrollbar.targetGraphic is Image handle) handle.color = SurfaceHover;
        scrollbar.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void StyleText(TextMeshProUGUI text)
    {
        if (text == null || text.GetComponent<GameUIThemeStyled>() != null) return;
        if (IsNeutral(text.color))
            text.color = text.color.a < 0.8f ? TextSecondary : TextPrimary;
        else if (text.color.r > text.color.g * 1.25f)
            text.color = Danger;
        else if (text.color.g > text.color.r * 1.15f)
            text.color = SageHover;
        else
            text.color = Accent;
        text.gameObject.AddComponent<GameUIThemeStyled>();
    }

    static void AddEdge(GameObject target, Vector2 distance)
    {
        if (target == null || target.GetComponent<Graphic>() == null) return;
        var outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = Edge;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    static void EnsureAccent(Transform button, Color color)
    {
        Transform existing = button.Find("ThemeAccent");
        GameObject go = existing != null ? existing.gameObject : null;
        if (go == null)
        {
            go = new GameObject("ThemeAccent", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(button, false);
        }
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(5f, -4f);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        go.transform.SetAsFirstSibling();
    }

    static bool IsPositive(string key, Color current)
    {
        return ContainsAny(key, "play", "resume", "continue", "apply", "confirm", "finish",
            "hire", "buy", "purchase", "order", "add", "create", "assign", "upgrade", "expand")
            || (current.g > current.r * 1.2f && current.g > current.b * 1.08f);
    }

    static bool IsDanger(string key, Color current)
    {
        return ContainsAny(key, "exit", "quit", "fire", "delete", "remove", "clear", "cancel")
            || (current.r > current.g * 1.35f && current.r > current.b * 1.18f);
    }

    static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
            if (value.Contains(term)) return true;
        return false;
    }

    static Color Hex(byte r, byte g, byte b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    static bool IsNeutral(Color color)
    {
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        return max - min < 0.16f;
    }
}

public sealed class GameUIThemeStyled : MonoBehaviour { }

public sealed class GameUIThemeButtonMotion : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    Vector3 restScale;
    float targetScale = 1f;

    void Awake() => restScale = transform.localScale;

    void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            restScale * targetScale,
            14f * Time.unscaledDeltaTime);
    }

    public void OnPointerEnter(PointerEventData eventData) => targetScale = 1.025f;
    public void OnPointerExit(PointerEventData eventData) => targetScale = 1f;
    public void OnPointerDown(PointerEventData eventData) => targetScale = 0.98f;
    public void OnPointerUp(PointerEventData eventData) => targetScale = 1.025f;
}
