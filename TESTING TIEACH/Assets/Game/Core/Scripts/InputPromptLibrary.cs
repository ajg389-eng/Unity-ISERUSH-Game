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
