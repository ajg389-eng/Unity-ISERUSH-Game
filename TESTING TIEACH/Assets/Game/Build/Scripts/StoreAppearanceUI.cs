using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Build-menu controls for independent wall, floor, and roof finishes.</summary>
public sealed class StoreAppearanceUI : MonoBehaviour
{
    sealed class SurfaceControls
    {
        public StoreSurfaceKind kind;
        public Outline[] textureOutlines;
        public Outline[] swatchOutlines;
    }

    Transform content;
    SurfaceControls[] controls;

    public void AppendOptions(Transform options)
    {
        if (options == null) return;
        BuildIfNeeded();
        options.SetParent(content, false);
        options.SetAsLastSibling();
    }

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
        layout.padding = new RectOffset(16, 16, 8, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        GameUITheme.ConfigureScroll(scroll);
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;

        CreateText(content, "Title", "Store appearance", 22f, FontStyles.Bold, 36f, Color.white);
        CreateText(content, "Subtitle", "Choose a texture, then tint it. New texture assets are discovered automatically.",
            15f, FontStyles.Normal, 52f, GameUITheme.TextPrimary);

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
        cardLe.minHeight = 210f;
        cardLe.preferredHeight = 210f;
        var vertical = card.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(12, 12, 8, 8);
        vertical.spacing = 6f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        CreateText(card.transform, "Title", label, 17f, FontStyles.Bold, 24f, Color.white);
        var controller = StoreAppearanceController.Ensure();
        var textureRow = CreateRow(card.transform, "TexturePreviews", 96f);
        var textureOutlines = new Outline[controller.GetTextureCount(kind)];
        for (int i = 0; i < textureOutlines.Length; i++)
        {
            int captured = i;
            var tile = new GameObject("Texture_" + controller.GetTextureName(kind, i),
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(Outline),
                typeof(VerticalLayoutGroup));
            tile.transform.SetParent(textureRow, false);
            tile.GetComponent<Image>().color = GameUITheme.Panel;
            var tileLe = tile.GetComponent<LayoutElement>();
            tileLe.minWidth = 64f; tileLe.preferredWidth = 64f;
            tileLe.minHeight = 92f; tileLe.preferredHeight = 92f;
            var tileLayout = tile.GetComponent<VerticalLayoutGroup>();
            tileLayout.padding = new RectOffset(4, 4, 4, 3);
            tileLayout.spacing = 2f;
            tileLayout.childAlignment = TextAnchor.MiddleCenter;
            tileLayout.childControlWidth = true;
            tileLayout.childControlHeight = true;
            tileLayout.childForceExpandWidth = true;
            tileLayout.childForceExpandHeight = false;

            var previewFrame = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            previewFrame.transform.SetParent(tile.transform, false);
            var preview = previewFrame.GetComponent<RawImage>();
            preview.texture = controller.GetTexturePreview(kind, i);
            preview.color = preview.texture != null ? Color.white : new Color(.42f, .45f, .48f, 1f);
            preview.raycastTarget = false;
            var previewLe = previewFrame.GetComponent<LayoutElement>();
            previewLe.minHeight = 58f; previewLe.preferredHeight = 58f;

            var name = CreateText(tile.transform, "Name", controller.GetTextureName(kind, i),
                10f, FontStyles.Bold, 22f, Color.white);
            name.alignment = TextAlignmentOptions.Center;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var outline = tile.GetComponent<Outline>();
            outline.effectColor = GameUITheme.Accent;
            outline.effectDistance = new Vector2(3f, -3f);
            tile.GetComponent<Button>().onClick.AddListener(() =>
            {
                StoreAppearanceController.Ensure().SetTexture(kind, captured);
                Refresh();
            });
            textureOutlines[i] = outline;
        }

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

        return new SurfaceControls { kind = kind, textureOutlines = textureOutlines, swatchOutlines = outlines };
    }

    void Refresh()
    {
        if (controls == null) return;
        var controller = StoreAppearanceController.Ensure();
        foreach (var control in controls)
        {
            int textureIndex = controller.GetTextureIndex(control.kind);
            for (int i = 0; i < control.textureOutlines.Length; i++)
                control.textureOutlines[i].enabled = i == textureIndex;
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
