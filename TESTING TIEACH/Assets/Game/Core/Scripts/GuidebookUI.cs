using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Two-page guidebook overlay. Built at runtime and opened from the pause menu.
/// </summary>
public class GuidebookUI : MonoBehaviour
{
    static readonly Color CoverColor = new Color(0.28f, 0.15f, 0.09f, 1f);
    static readonly Color SpineColor = new Color(0.16f, 0.08f, 0.05f, 1f);
    static readonly Color PageColor = new Color(0.94f, 0.90f, 0.80f, 1f);
    static readonly Color InkColor = new Color(0.16f, 0.11f, 0.08f, 1f);
    static readonly Color MutedInk = new Color(0.38f, 0.28f, 0.20f, 1f);
    static readonly Color NavColor = new Color(0.42f, 0.28f, 0.18f, 1f);
    static readonly Color NavText = new Color(0.96f, 0.92f, 0.86f, 1f);

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
        spreadIndex = 0;
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
        dim.color = new Color(0f, 0f, 0f, 0.45f);
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
        bookRt.sizeDelta = new Vector2(1180f, 720f);
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

        var close = CreateTextButton(book.transform, "CloseButton", "Close", new Vector2(1f, 1f), new Vector2(-18f, -12f), 120f, 36f);
        close.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            if (PauseMenuUI.Instance != null)
                PauseMenuUI.Instance.CloseGuidebook();
        });

        folioText = CreateLabel(book.transform, "Folio", "", 16f, TextAlignmentOptions.Center, MutedInk);
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
        var pageLe = page.GetComponent<LayoutElement>();
        pageLe.minWidth = 200f;
        pageLe.preferredWidth = 520f;
        pageLe.flexibleWidth = 1f;
        pageLe.minHeight = 0f;
        pageLe.flexibleHeight = 1f;
        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 22, 22);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        chapter = CreateLaidOutLabel(page.transform, "Chapter", 15f, FontStyles.Italic, MutedInk, 22f);
        title = CreateLaidOutLabel(page.transform, "Title", 28f, FontStyles.Bold, InkColor, 36f);

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
        body.fontSize = 18f;
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
        scroll.scrollSensitivity = 24f;
    }

    void Turn(int delta)
    {
        int maxSpread = (GuidebookPages.All.Length + 1) / 2 - 1;
        int next = Mathf.Clamp(spreadIndex + delta, 0, Mathf.Max(0, maxSpread));
        if (next == spreadIndex) return;
        spreadIndex = next;
        Sfx.Play(SfxId.UiClick);
        Refresh();
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
        var tmp = CreateLabel(go.transform, "Label", label, 18f, TextAlignmentOptions.Center, NavText);
        Stretch(tmp.rectTransform);
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;
        return btn;
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
