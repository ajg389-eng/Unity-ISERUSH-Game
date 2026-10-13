using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum InputPromptId
{
    KeyboardW,
    KeyboardA,
    KeyboardS,
    KeyboardD,
    KeyboardR,
    KeyboardE,
    Escape,
    Ctrl,
    Enter,
    Space,
    Shift,
    Arrows,
    MouseLeft,
    MouseRight,
    MouseMove,
    MouseScroll
}

/// <summary>
/// Loads Kenney's keyboard and mouse prompt art and adds compact visual input
/// hints to instructional UI. The source PNGs remain regular imported textures.
/// </summary>
public sealed class InputPromptLibrary : ScriptableObject
{
    const float InlineIconSize = 28f;
    const float InlineRowHeight = 32f;

    [SerializeField] Texture2D keyboardW;
    [SerializeField] Texture2D keyboardA;
    [SerializeField] Texture2D keyboardS;
    [SerializeField] Texture2D keyboardD;
    [SerializeField] Texture2D keyboardR;
    [SerializeField] Texture2D keyboardE;
    [SerializeField] Texture2D escape;
    [SerializeField] Texture2D ctrl;
    [SerializeField] Texture2D enter;
    [SerializeField] Texture2D space;
    [SerializeField] Texture2D shift;
    [SerializeField] Texture2D arrows;
    [SerializeField] Texture2D mouseLeft;
    [SerializeField] Texture2D mouseRight;
    [SerializeField] Texture2D mouseMove;
    [SerializeField] Texture2D mouseScroll;

    readonly Dictionary<InputPromptId, Sprite> spriteCache = new Dictionary<InputPromptId, Sprite>();
    static InputPromptLibrary instance;

    static InputPromptLibrary Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<InputPromptLibrary>("InputPromptLibrary");
            return instance;
        }
    }

    public static Sprite GetSprite(InputPromptId id)
    {
        return Instance != null ? Instance.Get(id) : null;
    }

    Sprite Get(InputPromptId id)
    {
        if (spriteCache.TryGetValue(id, out Sprite cached))
            return cached;
        Texture2D texture = GetTexture(id);
        if (texture == null) return null;
        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = "KenneyPrompt_" + id;
        spriteCache[id] = sprite;
        return sprite;
    }

    /// <summary>
    /// Adds or refreshes a small icon strip based on the controls mentioned in
    /// an instruction string. It works with both anchored labels and vertical
    /// instruction cards used by the flow editor.
    /// </summary>
    public static void ApplyToInstruction(Transform host, TextMeshProUGUI label, string instruction)
    {
        if (host == null) return;
        Transform existing = host.Find("InputPromptIcons");
        GameObject strip = existing != null ? existing.gameObject : new GameObject("InputPromptIcons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        if (strip.transform.parent != host)
            strip.transform.SetParent(host, false);

        var layout = strip.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.padding = new RectOffset(2, 2, 2, 2);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (int i = strip.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(strip.transform.GetChild(i).gameObject);

        List<InputPromptId> prompts = GetPrompts(instruction);
        bool verticalCard = host.GetComponent<VerticalLayoutGroup>() != null;
        RectTransform stripRect = (RectTransform)strip.transform;
        if (verticalCard)
        {
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(1f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.sizeDelta = new Vector2(0f, 27f);
            strip.GetComponent<LayoutElement>().minHeight = 27f;
            strip.GetComponent<LayoutElement>().preferredHeight = 27f;
        }
        else
        {
            stripRect.anchorMin = new Vector2(1f, 0.5f);
            stripRect.anchorMax = new Vector2(1f, 0.5f);
            stripRect.pivot = new Vector2(1f, 0.5f);
            stripRect.anchoredPosition = new Vector2(-8f, 0f);
            stripRect.sizeDelta = new Vector2(Mathf.Max(24f, prompts.Count * 25f + 8f), 24f);
            if (label != null)
            {
                RectTransform labelRect = label.rectTransform;
                Vector2 offsetMax = labelRect.offsetMax;
                offsetMax.x = -stripRect.sizeDelta.x - 12f;
                labelRect.offsetMax = offsetMax;
            }
        }

        for (int i = 0; i < prompts.Count; i++)
        {
            Sprite sprite = GetSprite(prompts[i]);
            if (sprite == null) continue;
            var iconObject = new GameObject("Prompt_" + prompts[i], typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(strip.transform, false);
            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = 22f;
            iconLayout.minHeight = iconLayout.preferredHeight = 22f;
            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        strip.SetActive(prompts.Count > 0);
    }

    /// <summary>
    /// Builds an inline instruction row where each input icon sits immediately
    /// before the action it controls. This is used by the compact placement bar;
    /// the older icon strip remains available for instructional cards.
    /// </summary>
    public static void ApplyInlineInstruction(Transform host, string instruction)
    {
        if (host == null) return;

        Transform oldStrip = host.Find("InputPromptIcons");
        if (oldStrip != null)
            oldStrip.gameObject.SetActive(false);

        Transform existing = host.Find("InlineInputInstruction");
        GameObject row = existing != null
            ? existing.gameObject
            : new GameObject("InlineInputInstruction", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        if (row.transform.parent != host)
            row.transform.SetParent(host, false);

        RectTransform rowRect = (RectTransform)row.transform;
        bool verticalHost = host.GetComponent<VerticalLayoutGroup>() != null;
        if (verticalHost)
        {
            rowRect.anchorMin = new Vector2(0f, 0f);
            rowRect.anchorMax = new Vector2(1f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = new Vector2(0f, InlineRowHeight);
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.minHeight = rowElement.preferredHeight = InlineRowHeight;
            rowElement.flexibleHeight = 0f;
        }
        else
        {
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = new Vector2(14f, 2f);
            rowRect.offsetMax = new Vector2(-14f, -2f);
        }

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (int i = row.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(row.transform.GetChild(i).gameObject);

        if (string.IsNullOrWhiteSpace(instruction))
        {
            row.SetActive(false);
            return;
        }

        row.SetActive(true);
        rowRect.localScale = Vector3.one;
        string upper = instruction.ToUpperInvariant();
        int cursor = 0;
        while (cursor < instruction.Length)
        {
            int markerIndex = FindNextMarker(upper, cursor, out string marker, out InputPromptId prompt);
            if (markerIndex < 0)
            {
                AddInlineText(row.transform, instruction.Substring(cursor).Trim());
                break;
            }

            if (markerIndex > cursor)
                AddInlineText(row.transform, instruction.Substring(cursor, markerIndex - cursor).Trim());

            int actionStart = markerIndex + marker.Length;
            int nextMarker = FindNextMarker(upper, actionStart, out _, out _);
            int actionLength = nextMarker >= 0 ? nextMarker - actionStart : instruction.Length - actionStart;
            string action = instruction.Substring(actionStart, actionLength).Trim();
            AddInlinePrompt(row.transform, prompt, action);
            cursor = nextMarker >= 0 ? nextMarker : instruction.Length;
        }

        // Prompt hosts have a fixed width. Fit the complete row as one unit so
        // long instructions stay readable instead of being clipped at the edge.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        float availableWidth = rowRect.rect.width;
        float preferredWidth = LayoutUtility.GetPreferredWidth(rowRect);
        rowRect.localScale = preferredWidth > availableWidth && availableWidth > 0.01f
            ? Vector3.one * Mathf.Clamp(availableWidth / preferredWidth, 0.72f, 1f)
            : Vector3.one;
    }

    static int FindNextMarker(string upper, int start, out string marker, out InputPromptId prompt)
    {
        marker = null;
        prompt = InputPromptId.MouseLeft;
        string[] markers = { "LMB:", "RMB:", "DRAG:", "CLICK:", "ESC:", "CTRL:", "R:" };
        InputPromptId[] prompts =
        {
            InputPromptId.MouseLeft, InputPromptId.MouseRight, InputPromptId.MouseMove,
            InputPromptId.MouseLeft, InputPromptId.Escape, InputPromptId.Ctrl,
            InputPromptId.KeyboardR
        };

        int best = -1;
        for (int i = 0; i < markers.Length; i++)
        {
            int index = upper.IndexOf(markers[i], start, System.StringComparison.Ordinal);
            if (index < 0 || (best >= 0 && index >= best)) continue;
            best = index;
            marker = markers[i];
            prompt = prompts[i];
        }
        return best;
    }

    static void AddInlineText(Transform parent, string value)
    {
        if (parent == null || string.IsNullOrWhiteSpace(value)) return;
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.color = GameUITheme.TextPrimary;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.ForceMeshUpdate(true, true);
        LayoutElement element = textObject.GetComponent<LayoutElement>();
        float preferredWidth = Mathf.Ceil(text.GetPreferredValues(value).x);
        element.minWidth = element.preferredWidth = preferredWidth;
        element.flexibleWidth = 0f;
        element.minHeight = 22f;
    }

    static void AddInlinePrompt(Transform parent, InputPromptId prompt, string action)
    {
        GameObject group = new GameObject("PromptAction", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        group.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = group.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = group.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Sprite sprite = GetSprite(prompt);
        if (sprite != null)
        {
            GameObject iconObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(group.transform, false);
            LayoutElement iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = InlineIconSize;
            iconLayout.minHeight = iconLayout.preferredHeight = InlineIconSize;
            Image image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        AddInlineText(group.transform, action);
    }

    static List<InputPromptId> GetPrompts(string instruction)
    {
        var prompts = new List<InputPromptId>();
        if (string.IsNullOrWhiteSpace(instruction)) return prompts;
        string value = instruction.ToUpperInvariant();
        if (value.Contains("LMB") || value.Contains("CLICK")) prompts.Add(InputPromptId.MouseLeft);
        if (value.Contains("RMB")) prompts.Add(InputPromptId.MouseRight);
        if (value.Contains("DRAG")) prompts.Add(InputPromptId.MouseMove);
        if (value.Contains("ESC")) prompts.Add(InputPromptId.Escape);
        if (value.Contains("CTRL")) prompts.Add(InputPromptId.Ctrl);
        if (value.Contains("R: ROTATE") || value.Contains(" R: ")) prompts.Add(InputPromptId.KeyboardR);
        if (value.Contains("SCROLL")) prompts.Add(InputPromptId.MouseScroll);
        return prompts;
    }

    Texture2D GetTexture(InputPromptId id)
    {
        switch (id)
        {
            case InputPromptId.KeyboardW: return keyboardW;
            case InputPromptId.KeyboardA: return keyboardA;
            case InputPromptId.KeyboardS: return keyboardS;
            case InputPromptId.KeyboardD: return keyboardD;
            case InputPromptId.KeyboardR: return keyboardR;
            case InputPromptId.KeyboardE: return keyboardE;
            case InputPromptId.Escape: return escape;
            case InputPromptId.Ctrl: return ctrl;
            case InputPromptId.Enter: return enter;
            case InputPromptId.Space: return space;
            case InputPromptId.Shift: return shift;
            case InputPromptId.Arrows: return arrows;
            case InputPromptId.MouseLeft: return mouseLeft;
            case InputPromptId.MouseRight: return mouseRight;
            case InputPromptId.MouseMove: return mouseMove;
            case InputPromptId.MouseScroll: return mouseScroll;
            default: return null;
        }
    }
}
