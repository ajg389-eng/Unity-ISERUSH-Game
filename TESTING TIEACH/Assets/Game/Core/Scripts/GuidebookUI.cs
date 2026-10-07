using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Two-page guidebook overlay. Built at runtime and opened from the pause menu.
/// </summary>
public class GuidebookUI : MonoBehaviour
{
    const string PrefSpreadIndex = "Guidebook.CurrentSpread";
    static readonly Color CoverColor = GameUITheme.Backdrop;
    static readonly Color SpineColor = GameUITheme.Edge;
    static readonly Color PageColor = GameUITheme.Panel;
    static readonly Color InkColor = GameUITheme.TextPrimary;
    static readonly Color MutedInk = GameUITheme.TextSecondary;
    static readonly Color NavColor = GameUITheme.Surface;
    static readonly Color NavText = GameUITheme.TextPrimary;

    GameObject root;
    TextMeshProUGUI leftChapter;
    TextMeshProUGUI leftTitle;
    TextMeshProUGUI leftBody;
    TextMeshProUGUI rightChapter;
    TextMeshProUGUI rightTitle;
    TextMeshProUGUI rightBody;
    TextMeshProUGUI folioText;
    Button prevButton;
    Button nextButton;
    readonly List<Button> categoryButtons = new List<Button>();
    readonly List<int> categoryPageIndexes = new List<int>();
    int spreadIndex;
    bool visible;

    public bool IsOpen => visible;

    public static GuidebookUI Create(Transform parent)
    {
        var go = new GameObject("GuidebookUI", typeof(RectTransform), typeof(GuidebookUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        var ui = go.GetComponent<GuidebookUI>();
        ui.Build();
        ui.Close();
        return ui;
    }

    public void Open()
    {
        if (root == null) Build();
        visible = true;
        root.SetActive(true);
        int maxSpread = Mathf.Max(0, (GuidebookPages.All.Length + 1) / 2 - 1);
        spreadIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefSpreadIndex, 0), 0, maxSpread);
        Refresh();
    }

    public void Close()
    {
        visible = false;
        if (root != null)
            root.SetActive(false);
    }

    void Update()
    {
        if (!visible) return;
        if (UIInputFocusGuard.IsTyping) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            Turn(-1);
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            Turn(1);
    }

    void Build()
    {
        root = new GameObject("GuidebookRoot", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(transform, false);
        Stretch((RectTransform)root.transform);
        var dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.68f);
        dim.raycastTarget = true;
        var dimBtn = root.GetComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() =>
        {
            if (PauseMenuUI.Instance != null)
                PauseMenuUI.Instance.CloseGuidebook();
        });

        var book = new GameObject("Book", typeof(RectTransform), typeof(Image), typeof(Button));
        book.transform.SetParent(root.transform, false);
        var bookRt = (RectTransform)book.transform;
        bookRt.anchorMin = new Vector2(0.5f, 0.5f);
        bookRt.anchorMax = new Vector2(0.5f, 0.5f);
        bookRt.pivot = new Vector2(0.5f, 0.5f);
        bookRt.sizeDelta = new Vector2(1240f, 760f);
        book.GetComponent<Image>().color = CoverColor;
        var eatClicks = book.GetComponent<Button>();
        eatClicks.transition = Selectable.Transition.None;

        var inner = new GameObject("Inner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        inner.transform.SetParent(book.transform, false);
        Stretch((RectTransform)inner.transform, 22f, 70f, 22f, 22f);
        var hlg = inner.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 0f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        BuildPage(inner.transform, "LeftPage", out leftChapter, out leftTitle, out leftBody);
        var spine = new GameObject("Spine", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        spine.transform.SetParent(inner.transform, false);
        spine.GetComponent<Image>().color = SpineColor;
        var spineLe = spine.GetComponent<LayoutElement>();
        spineLe.minWidth = 22f;
        spineLe.preferredWidth = 22f;
        spineLe.flexibleWidth = 0f;
        spineLe.minHeight = 0f;
        spineLe.flexibleHeight = 1f;
        BuildPage(inner.transform, "RightPage", out rightChapter, out rightTitle, out rightBody);

        prevButton = CreateNavButton(book.transform, "PrevButton", "‹", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f));
        prevButton.onClick.AddListener(() => Turn(-1));
        nextButton = CreateNavButton(book.transform, "NextButton", "›", new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f));
        nextButton.onClick.AddListener(() => Turn(1));
        ((RectTransform)prevButton.transform).anchoredPosition = new Vector2(-14f, -300f);
        ((RectTransform)nextButton.transform).anchoredPosition = new Vector2(146f, -300f);
        BuildCategoryBookmarks(book.transform);

        var close = CreateTextButton(book.transform, "CloseButton", "Close", new Vector2(1f, 1f), new Vector2(-18f, -12f), 120f, 36f);
        close.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            if (PauseMenuUI.Instance != null)
                PauseMenuUI.Instance.CloseGuidebook();
        });

        folioText = CreateLabel(book.transform, "Folio", "", 18f, TextAlignmentOptions.Center, GameUITheme.Accent);
        var folioRt = folioText.rectTransform;
        folioRt.anchorMin = new Vector2(0.5f, 0f);
        folioRt.anchorMax = new Vector2(0.5f, 0f);
        folioRt.pivot = new Vector2(0.5f, 0f);
        folioRt.anchoredPosition = new Vector2(0f, 16f);
        folioRt.sizeDelta = new Vector2(400f, 28f);
    }

    void BuildPage(Transform parent, string name, out TextMeshProUGUI chapter, out TextMeshProUGUI title, out TextMeshProUGUI body)
    {
        var page = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        page.transform.SetParent(parent, false);
        page.GetComponent<Image>().color = PageColor;
        var pageOutline = page.AddComponent<Outline>();
        pageOutline.effectColor = GameUITheme.Edge;
        pageOutline.effectDistance = new Vector2(2f, -2f);
        pageOutline.useGraphicAlpha = true;
        var pageLe = page.GetComponent<LayoutElement>();
        pageLe.minWidth = 200f;
        pageLe.preferredWidth = 520f;
        pageLe.flexibleWidth = 1f;
        pageLe.minHeight = 0f;
        pageLe.flexibleHeight = 1f;
        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(32, 32, 26, 26);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        chapter = CreateLaidOutLabel(page.transform, "Chapter", 17f, FontStyles.Bold, GameUITheme.Accent, 24f);
        title = CreateLaidOutLabel(page.transform, "Title", 32f, FontStyles.Bold, InkColor, 42f);

        var scrollGo = new GameObject("BodyScroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement), typeof(RectMask2D));
        scrollGo.transform.SetParent(page.transform, false);
        var scrollLe = scrollGo.GetComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 200f;
        scrollLe.preferredHeight = 520f;

        var content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
        content.transform.SetParent(scrollGo.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(ContentSizeFitter));
        bodyGo.transform.SetParent(content.transform, false);
        Stretch((RectTransform)bodyGo.transform);
        var bodyRt = (RectTransform)bodyGo.transform;
        bodyRt.anchorMin = new Vector2(0f, 1f);
        bodyRt.anchorMax = Vector2.one;
        bodyRt.pivot = new Vector2(0.5f, 1f);
        bodyRt.anchoredPosition = Vector2.zero;
        bodyRt.sizeDelta = Vector2.zero;
        bodyGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        body = bodyGo.AddComponent<TextMeshProUGUI>();
        body.fontSize = 21f;
        body.lineSpacing = 5f;
        body.color = InkColor;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.richText = true;
        body.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) body.font = TMP_Settings.defaultFontAsset;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = (RectTransform)scrollGo.transform;
        scroll.content = contentRt;
        GameUITheme.ConfigureScroll(scroll);
    }

    void Turn(int delta)
    {
        int maxSpread = (GuidebookPages.All.Length + 1) / 2 - 1;
        int next = Mathf.Clamp(spreadIndex + delta, 0, Mathf.Max(0, maxSpread));
        if (next == spreadIndex) return;
        spreadIndex = next;
        SaveCurrentSpread();
        Sfx.Play(SfxId.UiClick);
        Refresh();
    }

    void JumpToPage(int pageIndex)
    {
        int maxSpread = Mathf.Max(0, (GuidebookPages.All.Length + 1) / 2 - 1);
        spreadIndex = Mathf.Clamp(pageIndex / 2, 0, maxSpread);
        SaveCurrentSpread();
        Sfx.Play(SfxId.UiClick);
        Refresh();
    }

    void SaveCurrentSpread()
    {
        PlayerPrefs.SetInt(PrefSpreadIndex, spreadIndex);
        PlayerPrefs.Save();
    }

    void BuildCategoryBookmarks(Transform book)
    {
        categoryButtons.Clear();
        categoryPageIndexes.Clear();

        string previousChapter = null;
        int bookmarkIndex = 0;
        for (int pageIndex = 0; pageIndex < GuidebookPages.All.Length; pageIndex++)
        {
            string chapter = GuidebookPages.All[pageIndex].chapter ?? "Guide";
            if (chapter == previousChapter) continue;
            previousChapter = chapter;

            int targetPage = pageIndex;
            Button button = CreateBookmarkButton(book, chapter, bookmarkIndex,
                () => JumpToPage(targetPage));
            categoryButtons.Add(button);
            categoryPageIndexes.Add(targetPage);
            bookmarkIndex++;
        }
    }

    static Button CreateBookmarkButton(Transform parent, string label, int index, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("Bookmark_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.one;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(-4f, -62f - index * 44f);
        rt.sizeDelta = new Vector2(138f, 38f);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        ApplyButtonColors(button);

        var text = CreateLabel(go.transform, "Label", label, 15f, TextAlignmentOptions.MidlineLeft, NavText);
        Stretch(text.rectTransform, 14f, 0f, 8f, 0f);
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return button;
    }

    void Refresh()
    {
        var pages = GuidebookPages.All;
        int leftIndex = spreadIndex * 2;
        int rightIndex = leftIndex + 1;
        ApplyPage(leftIndex < pages.Length ? pages[leftIndex] : default, leftChapter, leftTitle, leftBody, leftIndex < pages.Length);
        ApplyPage(rightIndex < pages.Length ? pages[rightIndex] : default, rightChapter, rightTitle, rightBody, rightIndex < pages.Length);

        int shownLeft = leftIndex + 1;
        int shownRight = Mathf.Min(rightIndex + 1, pages.Length);
        folioText.text = shownLeft + "–" + shownRight + "  /  " + pages.Length;

        int maxSpread = (pages.Length + 1) / 2 - 1;
        prevButton.interactable = spreadIndex > 0;
        nextButton.interactable = spreadIndex < maxSpread;
        RefreshCategoryBookmarks(rightIndex);
    }

    void RefreshCategoryBookmarks(int visibleRightPage)
    {
        int active = 0;
        for (int i = 0; i < categoryPageIndexes.Count; i++)
        {
            if (categoryPageIndexes[i] <= visibleRightPage)
                active = i;
        }

        for (int i = 0; i < categoryButtons.Count; i++)
        {
            Button button = categoryButtons[i];
            if (button == null) continue;
            bool selected = i == active;
            var colors = button.colors;
            colors.normalColor = selected ? GameUITheme.Accent : GameUITheme.Surface;
            colors.selectedColor = selected ? GameUITheme.Accent : GameUITheme.SurfaceHover;
            button.colors = colors;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = selected ? GameUITheme.Charcoal : GameUITheme.TextPrimary;
        }
    }

    static void ApplyPage(GuidebookPages.Page page, TextMeshProUGUI chapter, TextMeshProUGUI title, TextMeshProUGUI body, bool hasPage)
    {
        if (!hasPage)
        {
            chapter.text = "";
            title.text = "";
            body.text = "";
            return;
        }

        chapter.text = page.chapter ?? "";
        title.text = page.title ?? "";
        body.text = page.body ?? "";
        var scroll = body.GetComponentInParent<ScrollRect>();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    static Button CreateNavButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(52f, 88f);
        go.GetComponent<Image>().color = NavColor;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        ApplyButtonColors(btn);
        var tmp = CreateLabel(go.transform, "Label", label, 40f, TextAlignmentOptions.Center, NavText);
        Stretch(tmp.rectTransform);
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;
        return btn;
    }

    static Button CreateTextButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(w, h);
        go.GetComponent<Image>().color = NavColor;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        ApplyButtonColors(btn);
        var tmp = CreateLabel(go.transform, "Label", label, 18f, TextAlignmentOptions.Center, NavText);
        Stretch(tmp.rectTransform);
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;
        return btn;
    }

    static void ApplyButtonColors(Button button)
    {
        var colors = button.colors;
        colors.normalColor = GameUITheme.Surface;
        colors.highlightedColor = GameUITheme.SurfaceHover;
        colors.pressedColor = GameUITheme.Accent;
        colors.selectedColor = GameUITheme.SurfaceHover;
        colors.disabledColor = new Color(GameUITheme.Surface.r, GameUITheme.Surface.g, GameUITheme.Surface.b, 0.38f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    static TextMeshProUGUI CreateLaidOutLabel(Transform parent, string name, float size, FontStyles style, Color color, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        le.flexibleHeight = 0f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }
}
