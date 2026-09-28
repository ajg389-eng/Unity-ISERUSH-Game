using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Build-menu controls for independent wall, floor, and roof finishes.</summary>
public sealed class StoreAppearanceUI : MonoBehaviour
{
    sealed class SurfaceControls
    {
        public StoreSurfaceKind kind;
        public TextMeshProUGUI textureName;
        public Outline[] swatchOutlines;
    }

    Transform content;
    SurfaceControls[] controls;

    void OnEnable()
    {
        BuildIfNeeded();
        Refresh();
    }

    void BuildIfNeeded()
    {
        if (content != null) return;

        var scrollGo = new GameObject("AppearanceScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(transform, false);
        Stretch((RectTransform)scrollGo.transform);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch((RectTransform)viewport.transform);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, .01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        content = contentGo.transform;
        var layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;

        CreateText(content, "Title", "Store appearance", 22f, FontStyles.Bold, 34f, Color.white);
        CreateText(content, "Subtitle", "Choose a texture, then tint it. New texture assets are discovered automatically.",
            13f, FontStyles.Normal, 40f, GameUITheme.TextSecondary);

        controls = new[]
        {
            CreateSurfaceCard(StoreSurfaceKind.Walls, "Walls"),
            CreateSurfaceCard(StoreSurfaceKind.Floor, "Floors"),
            CreateSurfaceCard(StoreSurfaceKind.Roof, "Roof")
        };
    }

    SurfaceControls CreateSurfaceCard(StoreSurfaceKind kind, string label)
    {
        var card = new GameObject(label + "Card", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        card.transform.SetParent(content, false);
        card.GetComponent<Image>().color = GameUITheme.Surface;
        var cardLe = card.GetComponent<LayoutElement>();
        cardLe.minHeight = 142f;
        cardLe.preferredHeight = 142f;
        var vertical = card.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(12, 12, 8, 8);
        vertical.spacing = 6f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        CreateText(card.transform, "Title", label, 17f, FontStyles.Bold, 24f, Color.white);
        var textureRow = CreateRow(card.transform, "TextureRow", 38f);
        CreateText(textureRow, "Label", "Texture", 13f, FontStyles.Bold, 30f, GameUITheme.TextSecondary)
            .GetComponent<LayoutElement>().preferredWidth = 70f;
        CreateArrowButton(textureRow, "Previous", "<", () => CycleTexture(kind, -1));
        var textureName = CreateText(textureRow, "TextureName", "Original", 14f, FontStyles.Bold, 30f, Color.white);
        textureName.alignment = TextAlignmentOptions.Center;
        textureName.GetComponent<LayoutElement>().flexibleWidth = 1f;
        CreateArrowButton(textureRow, "Next", ">", () => CycleTexture(kind, 1));

        var colorRow = CreateRow(card.transform, "ColorRow", 44f);
        CreateText(colorRow, "Label", "Color", 13f, FontStyles.Bold, 34f, GameUITheme.TextSecondary)
            .GetComponent<LayoutElement>().preferredWidth = 70f;
        var outlines = new Outline[StoreAppearanceController.TintColors.Length];
        for (int i = 0; i < outlines.Length; i++)
        {
            int captured = i;
            var swatch = new GameObject("Tint_" + StoreAppearanceController.TintNames[i],
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(Outline));
            swatch.transform.SetParent(colorRow, false);
            swatch.GetComponent<Image>().color = StoreAppearanceController.TintColors[i];
            var le = swatch.GetComponent<LayoutElement>();
            le.minWidth = 34f; le.preferredWidth = 34f; le.minHeight = 34f; le.preferredHeight = 34f;
            var outline = swatch.GetComponent<Outline>();
            outline.effectColor = GameUITheme.Accent;
            outline.effectDistance = new Vector2(3f, -3f);
            swatch.GetComponent<Button>().onClick.AddListener(() =>
            {
                StoreAppearanceController.Ensure().SetTint(kind, StoreAppearanceController.TintColors[captured]);
                Refresh();
            });
            outlines[i] = outline;
        }

        return new SurfaceControls { kind = kind, textureName = textureName, swatchOutlines = outlines };
    }

    void CycleTexture(StoreSurfaceKind kind, int direction)
    {
        var controller = StoreAppearanceController.Ensure();
        int count = Mathf.Max(1, controller.GetTextureCount(kind));
        int next = (controller.GetTextureIndex(kind) + direction + count) % count;
        controller.SetTexture(kind, next);
        Refresh();
    }

    void Refresh()
    {
        if (controls == null) return;
        var controller = StoreAppearanceController.Ensure();
        foreach (var control in controls)
        {
            int textureIndex = controller.GetTextureIndex(control.kind);
            control.textureName.text = controller.GetTextureName(control.kind, textureIndex);
            Color activeTint = controller.GetTint(control.kind);
            for (int i = 0; i < control.swatchOutlines.Length; i++)
                control.swatchOutlines[i].enabled = Approximately(activeTint, StoreAppearanceController.TintColors[i]);
        }
    }

    static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f;

    static Transform CreateRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 7f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        return row.transform;
    }

    static void CreateArrowButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = GameUITheme.Panel;
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 36f; le.preferredWidth = 36f; le.minHeight = 32f; le.preferredHeight = 32f;
        var text = CreateText(go.transform, "Text", label, 17f, FontStyles.Bold, 30f, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        go.GetComponent<Button>().onClick.AddListener(action);
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, float height, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
        return text;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
