using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Modal to customize a kitchen worker with clickable color / face / accessory previews.
/// </summary>
public class WorkerCustomizePopup : MonoBehaviour
{
    static WorkerCustomizePopup instance;

    static readonly Color PanelBg = new Color(0.14f, 0.16f, 0.22f, 0.98f);
    static readonly Color SectionBg = new Color(0.1f, 0.12f, 0.16f, 1f);
    static readonly Color FrameIdle = new Color(0.22f, 0.25f, 0.32f, 1f);
    static readonly Color FrameSelected = new Color(1f, 0.85f, 0.35f, 1f);
    static readonly Color NoneSwatch = new Color(0.28f, 0.3f, 0.36f, 1f);

    KitchenEmployee employee;
    PartyCharacterRandomizer appearance;
    TextMeshProUGUI titleText;

    Transform colorGrid;
    Transform faceRow;
    Transform hatRow;

    readonly List<Image> colorFrames = new List<Image>();
    readonly List<Image> faceFrames = new List<Image>();
    readonly List<Image> hatFrames = new List<Image>();

    public static void Show(KitchenEmployee emp)
    {
        if (emp == null) return;

        // Rebuild each open so layout fixes apply after code changes.
        if (instance != null)
        {
            Object.Destroy(instance.gameObject);
            instance = null;
        }

        instance = Create();
        instance.Open(emp);
    }

    public static void Hide()
    {
        if (instance != null)
            instance.gameObject.SetActive(false);
    }

    static WorkerCustomizePopup Create()
    {
        Canvas canvas = FindPreferredCanvas();
        if (canvas == null)
        {
            var canvasGo = new GameObject("WorkerCustomizeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        var root = new GameObject("WorkerCustomizePopup", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        StretchFull((RectTransform)root.transform);

        var popup = root.AddComponent<WorkerCustomizePopup>();
        popup.BuildUi(root.transform);
        return popup;
    }

    static Canvas FindPreferredCanvas()
    {
        var workers = Object.FindObjectOfType<WorkersUI>();
        if (workers != null)
        {
            var c = workers.GetComponentInParent<Canvas>();
            if (c != null) return c;
        }
        Canvas best = null;
        int bestOrder = int.MinValue;
        foreach (var c in Object.FindObjectsOfType<Canvas>())
        {
            if (c == null || !c.isActiveAndEnabled) continue;
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (c.sortingOrder >= bestOrder)
            {
                bestOrder = c.sortingOrder;
                best = c;
            }
        }
        return best;
    }

    void BuildUi(Transform root)
    {
        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
        dim.transform.SetParent(root, false);
        StretchFull((RectTransform)dim.transform);
        dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        dim.GetComponent<Button>().onClick.AddListener(Close);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root, false);
        var panelRt = (RectTransform)panel.transform;
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(540f, 700f);
        var panelImg = panel.GetComponent<Image>();
        panelImg.color = PanelBg;
        panelImg.raycastTarget = true;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        titleText = CreateLabel(panel.transform, "Title", "Customize Worker", 20, FontStyles.Bold, 26f);
        CreateLabel(panel.transform, "Hint", "Free defaults are unlocked. Click a locked option to buy it.", 12,
            FontStyles.Normal, 18f, new Color(0.75f, 0.78f, 0.86f, 1f));

        // 36 colors @ 9 cols = 4 rows. Cell 40 + gap 6 → needs ~190px.
        colorGrid = CreateScrollGridSection(panel.transform, "Color", 200f, cell: 40f, columns: 9);
        faceRow = CreateHorizontalSection(panel.transform, "Expression", 112f);
        hatRow = CreateHorizontalSection(panel.transform, "Accessory", 120f);

        BuildColorOptions();
        BuildFaceOptions();
        BuildHatOptions();

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var buttonsLe = buttons.AddComponent<LayoutElement>();
        buttonsLe.minHeight = 40;
        buttonsLe.preferredHeight = 40;
        var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        CreateActionButton(buttons.transform, "Randomize", new Color(0.28f, 0.38f, 0.52f, 1f), OnRandomize);
        CreateActionButton(buttons.transform, "Done", new Color(0.22f, 0.52f, 0.36f, 1f), Close);
    }

    Transform CreateScrollGridSection(Transform parent, string title, float gridHeight, float cell, int columns)
    {
        var section = CreateSectionShell(parent, title, gridHeight + 30f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(section, false);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        var viewLe = viewport.AddComponent<LayoutElement>();
        viewLe.minHeight = gridHeight;
        viewLe.preferredHeight = gridHeight;
        viewLe.flexibleWidth = 1;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        // Top-left anchored so items never spill off the left edge.
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.offsetMin = new Vector2(0f, contentRt.offsetMin.y);
        contentRt.offsetMax = new Vector2(0f, 0f);

        var grid = content.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(6f, 6f);
        grid.padding = new RectOffset(6, 6, 6, 6);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        return content.transform;
    }

    Transform CreateHorizontalSection(Transform parent, string title, float rowHeight)
    {
        // header(18) + spacing(6) + padding(16) + row
        var section = CreateSectionShell(parent, title, rowHeight + 50f);

        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(section, false);
        var rowLe = row.AddComponent<LayoutElement>();
        rowLe.minHeight = rowHeight;
        rowLe.preferredHeight = rowHeight;
        rowLe.flexibleWidth = 1;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 2, 6);
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        return row.transform;
    }

    Transform CreateSectionShell(Transform parent, string title, float totalHeight)
    {
        var section = new GameObject(title + "Section", typeof(RectTransform), typeof(Image));
        section.transform.SetParent(parent, false);
        section.GetComponent<Image>().color = SectionBg;
        var sectionLe = section.AddComponent<LayoutElement>();
        sectionLe.minHeight = totalHeight;
        sectionLe.preferredHeight = totalHeight;
        sectionLe.flexibleWidth = 1;

        var sectionVlg = section.AddComponent<VerticalLayoutGroup>();
        sectionVlg.padding = new RectOffset(10, 10, 8, 8);
        sectionVlg.spacing = 6;
        sectionVlg.childAlignment = TextAnchor.UpperLeft;
        sectionVlg.childControlWidth = true;
        sectionVlg.childControlHeight = true;
        sectionVlg.childForceExpandWidth = true;
        sectionVlg.childForceExpandHeight = false;

        var header = CreateLabel(section.transform, "Header", title, 13, FontStyles.Bold, 18f,
            new Color(0.85f, 0.88f, 0.95f, 1f));
        header.alignment = TextAlignmentOptions.Left;

        return section.transform;
    }

    void BuildColorOptions()
    {
        ClearChildren(colorGrid);
        colorFrames.Clear();
        var cosmetics = CosmeticsUnlockManager.Ensure();
        int count = PartyCharacterRandomizer.BodyCount;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            Color swatch = PartyCharacterRandomizer.GetMaterialSwatchColor(
                PartyCharacterRandomizer.GetBodyMaterial(i));
            bool owned = cosmetics.IsOwned(CosmeticKind.Body, index);
            var frame = CreateColorSwatch(colorGrid, swatch, () =>
            {
                if (!TrySelectCosmetic(CosmeticKind.Body, index)) return;
                appearance.SetBodyIndex(index);
                RebuildAllOptions();
            });
            if (!owned)
                AddLockOverlay(frame.transform, cosmetics.GetPrice(CosmeticKind.Body), compact: true);
            colorFrames.Add(frame);
        }
    }

    void BuildFaceOptions()
    {
        ClearChildren(faceRow);
        faceFrames.Clear();
        var cosmetics = CosmeticsUnlockManager.Ensure();
        int count = PartyCharacterRandomizer.FaceCount;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var mat = PartyCharacterRandomizer.GetFaceMaterial(i);
            Texture tex = PartyCharacterRandomizer.GetMaterialPreviewTexture(mat);
            string label = PartyCharacterRandomizer.GetFaceName(i);
            bool owned = cosmetics.IsOwned(CosmeticKind.Face, index);
            var frame = CreateFaceCard(faceRow, tex, label, () =>
            {
                if (!TrySelectCosmetic(CosmeticKind.Face, index)) return;
                appearance.SetFaceIndex(index);
                RebuildAllOptions();
            });
            if (!owned)
                AddLockOverlay(frame.transform, cosmetics.GetPrice(CosmeticKind.Face), compact: false);
            faceFrames.Add(frame);
        }
    }

    void BuildHatOptions()
    {
        ClearChildren(hatRow);
        hatFrames.Clear();
        var cosmetics = CosmeticsUnlockManager.Ensure();

        hatFrames.Add(CreateHatCard(hatRow, null, "None", true, () =>
        {
            if (appearance == null) return;
            appearance.SetHatIndex(-1);
            RefreshSelection();
        }));

        int count = PartyCharacterRandomizer.HatCount;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var prefab = PartyCharacterRandomizer.GetHatPrefab(i);
            Texture preview = ItemPreviewThumbnails.GetPrefab(prefab, PartyCharacterRandomizer.GetHatName(i));
            string label = PartyCharacterRandomizer.GetHatName(i);
            bool owned = cosmetics.IsOwned(CosmeticKind.Hat, index);
            hatFrames.Add(CreateHatCard(hatRow, preview, label, false, () =>
            {
                if (!TrySelectCosmetic(CosmeticKind.Hat, index)) return;
                appearance.SetHatIndex(index);
                RebuildAllOptions();
            }));
            if (!owned)
                AddLockOverlay(hatFrames[hatFrames.Count - 1].transform, cosmetics.GetPrice(CosmeticKind.Hat), compact: false);
        }
    }

    void RebuildAllOptions()
    {
        BuildColorOptions();
        BuildFaceOptions();
        BuildHatOptions();
        RefreshSelection();
    }

    bool TrySelectCosmetic(CosmeticKind kind, int index)
    {
        if (appearance == null) return false;
        var cosmetics = CosmeticsUnlockManager.Ensure();
        if (cosmetics.IsOwned(kind, index))
            return true;
        return cosmetics.TryPurchase(kind, index);
    }

    static void AddLockOverlay(Transform parent, int price, bool compact)
    {
        if (parent == null) return;

        var overlay = new GameObject("Lock", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(parent, false);
        var rt = (RectTransform)overlay.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = compact ? new Vector2(2f, 2f) : new Vector2(4f, 20f);
        rt.offsetMax = new Vector2(-2f, -2f);
        var img = overlay.GetComponent<Image>();
        img.color = new Color(0.05f, 0.06f, 0.08f, 0.62f);
        img.raycastTarget = false;

        var labelGo = new GameObject("Price", typeof(RectTransform));
        labelGo.transform.SetParent(overlay.transform, false);
        StretchFull((RectTransform)labelGo.transform);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "$" + price;
        tmp.fontSize = compact ? 10 : 13;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.92f, 0.45f, 1f);
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    Image CreateColorSwatch(Transform parent, Color fill, System.Action onClick)
    {
        var go = new GameObject("Color", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var frame = go.GetComponent<Image>();
        frame.color = FrameIdle;
        go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

        var inner = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        inner.transform.SetParent(go.transform, false);
        var innerRt = (RectTransform)inner.transform;
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(3f, 3f);
        innerRt.offsetMax = new Vector2(-3f, -3f);
        var fillImg = inner.GetComponent<Image>();
        fillImg.color = fill;
        fillImg.raycastTarget = false;
        return frame;
    }

    Image CreateFaceCard(Transform parent, Texture tex, string caption, System.Action onClick)
    {
        var go = new GameObject("Face", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 96;
        le.preferredWidth = 96;
        le.minHeight = 104;
        le.preferredHeight = 104;
        var frame = go.GetComponent<Image>();
        frame.color = FrameIdle;
        go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

        if (tex != null)
        {
            var rawGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(go.transform, false);
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(5f, 22f);
            rawRt.offsetMax = new Vector2(-5f, -5f);
            var raw = rawGo.GetComponent<RawImage>();
            raw.texture = tex;
            raw.uvRect = new Rect(0.15f, 0.35f, 0.7f, 0.55f);
            raw.color = Color.white;
            raw.raycastTarget = false;
        }
        else
        {
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(go.transform, false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(5f, 22f);
            fillRt.offsetMax = new Vector2(-5f, -5f);
            fill.GetComponent<Image>().color = new Color(0.85f, 0.75f, 0.55f, 1f);
            fill.GetComponent<Image>().raycastTarget = false;
        }

        AddCaption(go.transform, caption);
        return frame;
    }

    Image CreateHatCard(Transform parent, Texture preview, string caption, bool isNone, System.Action onClick)
    {
        var go = new GameObject("Hat", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 104;
        le.preferredWidth = 104;
        le.minHeight = 112;
        le.preferredHeight = 112;
        var frame = go.GetComponent<Image>();
        frame.color = FrameIdle;
        go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(go.transform, false);
        var fillRt = (RectTransform)fill.transform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(5f, 22f);
        fillRt.offsetMax = new Vector2(-5f, -5f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.color = isNone ? NoneSwatch : new Color(0.18f, 0.2f, 0.26f, 1f);
        fillImg.raycastTarget = false;

        if (isNone)
        {
            var slash = new GameObject("Slash", typeof(RectTransform), typeof(Image));
            slash.transform.SetParent(go.transform, false);
            var slashRt = (RectTransform)slash.transform;
            slashRt.anchorMin = new Vector2(0.5f, 0.55f);
            slashRt.anchorMax = new Vector2(0.5f, 0.55f);
            slashRt.sizeDelta = new Vector2(48f, 4f);
            slashRt.localEulerAngles = new Vector3(0f, 0f, 35f);
            slash.GetComponent<Image>().color = new Color(0.95f, 0.4f, 0.4f, 1f);
            slash.GetComponent<Image>().raycastTarget = false;
        }
        else if (preview != null)
        {
            var rawGo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(go.transform, false);
            var rawRt = (RectTransform)rawGo.transform;
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(6f, 22f);
            rawRt.offsetMax = new Vector2(-6f, -6f);
            var raw = rawGo.GetComponent<RawImage>();
            raw.texture = preview;
            raw.color = Color.white;
            raw.raycastTarget = false;
        }

        AddCaption(go.transform, caption);
        return frame;
    }

    static void AddCaption(Transform parent, string caption)
    {
        var labelGo = new GameObject("Caption", typeof(RectTransform));
        labelGo.transform.SetParent(parent, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0f);
        labelRt.pivot = new Vector2(0.5f, 0f);
        labelRt.sizeDelta = new Vector2(-4f, 18f);
        labelRt.anchoredPosition = new Vector2(0f, 3f);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = Shorten(caption, 12);
        tmp.fontSize = 10;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    static string Shorten(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max - 1) + "…";
    }

    static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    void Open(KitchenEmployee emp)
    {
        employee = emp;
        appearance = PartyCharacterRandomizer.EnsureOn(emp.gameObject);
        var cosmetics = CosmeticsUnlockManager.Ensure();
        if (appearance != null)
        {
            appearance.randomizeOnStart = false;
            // If this worker somehow has a locked look, snap back to owned defaults.
            if (cosmetics != null
                && (!cosmetics.IsOwned(CosmeticKind.Body, appearance.bodyIndex)
                    || !cosmetics.IsOwned(CosmeticKind.Face, appearance.faceIndex)
                    || !cosmetics.IsOwned(CosmeticKind.Hat, appearance.hatIndex)))
            {
                cosmetics.ApplyRandomOwned(appearance);
            }
            else
            {
                appearance.ApplyCurrent();
            }
        }

        if (titleText != null)
            titleText.text = "Customize " + (string.IsNullOrEmpty(emp.employeeName) ? "Worker" : emp.employeeName);

        RebuildAllOptions();
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    void RefreshSelection()
    {
        if (appearance == null) return;

        for (int i = 0; i < colorFrames.Count; i++)
        {
            if (colorFrames[i] != null)
                colorFrames[i].color = i == appearance.bodyIndex ? FrameSelected : FrameIdle;
        }

        for (int i = 0; i < faceFrames.Count; i++)
        {
            if (faceFrames[i] != null)
                faceFrames[i].color = i == appearance.faceIndex ? FrameSelected : FrameIdle;
        }

        for (int i = 0; i < hatFrames.Count; i++)
        {
            if (hatFrames[i] == null) continue;
            int hatOption = i - 1; // 0 = None
            hatFrames[i].color = appearance.hatIndex == hatOption ? FrameSelected : FrameIdle;
        }
    }

    void OnRandomize()
    {
        if (appearance == null) return;
        var cosmetics = CosmeticsUnlockManager.Ensure();
        if (cosmetics != null)
            cosmetics.ApplyRandomOwned(appearance);
        else
            appearance.Randomize();
        RebuildAllOptions();
    }

    void Close()
    {
        gameObject.SetActive(false);
        employee = null;
        appearance = null;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI CreateLabel(
        Transform parent,
        string name,
        string text,
        float fontSize,
        FontStyles style,
        float height,
        Color? color = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color ?? Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static void CreateActionButton(Transform parent, string label, Color color, System.Action onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 36;
        le.preferredHeight = 36;
        le.flexibleWidth = 1;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }
}
