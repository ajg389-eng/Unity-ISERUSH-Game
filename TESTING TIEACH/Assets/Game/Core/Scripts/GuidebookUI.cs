using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Two-page reference overlay. Built at runtime and opened from the HUD, Gus, or pause menu.
/// </summary>
public class GuidebookUI : MonoBehaviour
{
    const string PrefSpreadIndex = "Guidebook.CurrentSpread";
    const string IllustrationResourcesPath = "GuidebookIllustrations/";
    const float LessonIllustrationHeight = 154f;
    const int GlossaryTermReward = 50;
    static readonly Regex NonGlossaryCharacters = new Regex("[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Color CoverColor = GameUITheme.Backdrop;
    static readonly Color SpineColor = GameUITheme.Edge;
    static readonly Color PageColor = GameUITheme.Panel;
    static readonly Color InkColor = GameUITheme.TextPrimary;
    static readonly Color MutedInk = GameUITheme.TextSecondary;
    static readonly Color NavColor = GameUITheme.Surface;
    static readonly Color NavText = GameUITheme.TextPrimary;
    static readonly Dictionary<string, Sprite> IllustrationCache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> DiscoveredGlossaryTerms = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> ClaimedGlossaryTermRewards = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    GameObject root;
    TextMeshProUGUI leftChapter;
    TextMeshProUGUI leftTitle;
    TextMeshProUGUI leftBody;
    Image leftIllustration;
    TextMeshProUGUI rightChapter;
    TextMeshProUGUI rightTitle;
    TextMeshProUGUI rightBody;
    Image rightIllustration;
    TextMeshProUGUI folioText;
    Button prevButton;
    Button nextButton;
    Button leftRewardButton;
    Button rightRewardButton;
    readonly List<Button> categoryButtons = new List<Button>();
    readonly List<int> categoryPageIndexes = new List<int>();
    int spreadIndex;
    int focusedTermPageIndex = -1;
    string focusedTerm;
    bool visible;

    public bool IsOpen => visible;

    public static bool IsGlossaryTermDiscovered(string term)
    {
        string normalized = NormalizeGlossaryTerm(term);
        return normalized.Length > 0 && DiscoveredGlossaryTerms.Contains(normalized);
    }

    public static bool IsGlossaryTermRewardClaimed(string term)
    {
        string normalized = NormalizeGlossaryTerm(term);
        return normalized.Length > 0 && ClaimedGlossaryTermRewards.Contains(normalized);
    }

    public static bool MarkGlossaryTermDiscovered(string term)
    {
        string normalized = NormalizeGlossaryTerm(term);
        if (normalized.Length == 0 || !DiscoveredGlossaryTerms.Add(normalized)) return false;
        ISEGlossaryLinkHandler.RefreshAllHighlights();
        SaveGlossaryProgress();
        return true;
    }

    public static List<string> CaptureDiscoveredGlossaryTerms()
    {
        return new List<string>(DiscoveredGlossaryTerms);
    }

    public static List<string> CaptureClaimedGlossaryTermRewards()
    {
        return new List<string>(ClaimedGlossaryTermRewards);
    }

    public static void RestoreGlossaryProgress(IEnumerable<string> discovered, IEnumerable<string> claimed)
    {
        DiscoveredGlossaryTerms.Clear();
        ClaimedGlossaryTermRewards.Clear();
        if (discovered != null)
            foreach (string term in discovered)
            {
                string normalized = NormalizeGlossaryTerm(term);
                if (normalized.Length > 0) DiscoveredGlossaryTerms.Add(normalized);
            }
        if (claimed != null)
            foreach (string term in claimed)
            {
                string normalized = NormalizeGlossaryTerm(term);
                if (normalized.Length > 0)
                {
                    DiscoveredGlossaryTerms.Add(normalized);
                    ClaimedGlossaryTermRewards.Add(normalized);
                }
            }
        ISEGlossaryLinkHandler.RefreshAllHighlights();
    }

    static string NormalizeGlossaryTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return string.Empty;
        string normalized = NonGlossaryCharacters.Replace(term.ToLowerInvariant(), " ").Trim();
        if (normalized == "starved") return "starvation";
        if (normalized == "queuing") return "queue";
        if (normalized == "processes") return "process";

        string[] words = normalized.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Length > 5 && word.EndsWith("ies", System.StringComparison.Ordinal))
                words[i] = word.Substring(0, word.Length - 3) + "y";
            else if (word.Length > 4 && word.EndsWith("s", System.StringComparison.Ordinal)
                && !word.EndsWith("ss", System.StringComparison.Ordinal)
                && !word.EndsWith("us", System.StringComparison.Ordinal)
                && !word.EndsWith("is", System.StringComparison.Ordinal))
                words[i] = word.Substring(0, word.Length - 1);
        }
        return string.Join(" ", words);
    }

    static void SaveGlossaryProgress()
    {
        GameSaveSlots.SaveGlossaryProgress(
            CaptureDiscoveredGlossaryTerms(), CaptureClaimedGlossaryTermRewards());
    }

    public static GuidebookUI Create(Transform parent)
    {
        var go = new GameObject("GuidebookUI", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(GuidebookUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32010;
        var ui = go.GetComponent<GuidebookUI>();
        ui.Build();
        ui.Close();
        return ui;
    }

    public void Open()
    {
        if (root == null) Build();
        focusedTermPageIndex = -1;
        focusedTerm = null;
        visible = true;
        root.SetActive(true);
        int maxSpread = Mathf.Max(0, (GuidebookPages.All.Length + 1) / 2 - 1);
        spreadIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefSpreadIndex, 0), 0, maxSpread);
        Refresh();
    }

    public void OpenToChapter(string chapter)
    {
        Open();
        if (string.IsNullOrWhiteSpace(chapter)) return;

        for (int i = 0; i < GuidebookPages.All.Length; i++)
        {
            if (!string.Equals(GuidebookPages.All[i].chapter, chapter,
                    System.StringComparison.OrdinalIgnoreCase))
                continue;

            spreadIndex = Mathf.Clamp(i / 2, 0, Mathf.Max(0, (GuidebookPages.All.Length + 1) / 2 - 1));
            SaveCurrentSpread();
            Refresh();
            return;
        }
    }

    public void OpenToTerm(string term)
    {
        Open();
        if (string.IsNullOrWhiteSpace(term)) return;
        focusedTerm = term.Trim();
        focusedTermPageIndex = GuidebookPages.FindGlossaryPageIndex(focusedTerm);
        JumpToPage(focusedTermPageIndex);
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
        bookRt.sizeDelta = new Vector2(1240f, 780f);
        bookRt.anchoredPosition = new Vector2(0f, -8f);
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

        BuildPage(inner.transform, "LeftPage", out leftChapter, out leftTitle, out leftBody,
            out leftIllustration, out leftRewardButton);
        var spine = new GameObject("Spine", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        spine.transform.SetParent(inner.transform, false);
        spine.GetComponent<Image>().color = SpineColor;
        var spineLe = spine.GetComponent<LayoutElement>();
        spineLe.minWidth = 22f;
        spineLe.preferredWidth = 22f;
        spineLe.flexibleWidth = 0f;
        spineLe.minHeight = 0f;
        spineLe.flexibleHeight = 1f;
        BuildPage(inner.transform, "RightPage", out rightChapter, out rightTitle, out rightBody,
            out rightIllustration, out rightRewardButton);
        leftRewardButton.onClick.AddListener(ClaimFocusedTermReward);
        rightRewardButton.onClick.AddListener(ClaimFocusedTermReward);

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
        folioRt.anchoredPosition = new Vector2(0f, 8f);
        folioRt.sizeDelta = new Vector2(400f, 28f);
    }

    void BuildPage(Transform parent, string name, out TextMeshProUGUI chapter, out TextMeshProUGUI title,
        out TextMeshProUGUI body, out Image illustration, out Button rewardButton)
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
        vlg.padding = new RectOffset(32, 32, 20, 20);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        chapter = CreateLaidOutLabel(page.transform, "Chapter", 15f, FontStyles.Bold, GameUITheme.Accent, 22f);
        chapter.characterSpacing = 1.1f;
        title = CreateLaidOutLabel(page.transform, "Title", 29f, FontStyles.Bold, InkColor, 40f);
        // Keep long chapter titles on one line and shrink them slightly when needed.
        // A wrapped title used to overflow its fixed-height layout slot and collide
        // with the first section heading below it.
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.enableAutoSizing = true;
        title.fontSizeMin = 22f;
        title.fontSizeMax = 29f;
        title.characterSpacing = -0.35f;

        var scrollGo = new GameObject("BodyScroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement), typeof(RectMask2D));
        scrollGo.transform.SetParent(page.transform, false);
        var scrollLe = scrollGo.GetComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 160f;
        scrollLe.preferredHeight = 300f;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(scrollGo.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 12f;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(LayoutElement));
        bodyGo.transform.SetParent(content.transform, false);
        var bodyLe = bodyGo.GetComponent<LayoutElement>();
        bodyLe.flexibleHeight = 0f;

        body = bodyGo.AddComponent<TextMeshProUGUI>();
        body.fontSize = 20f;
        body.lineSpacing = 4f;
        body.color = InkColor;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.richText = true;
        body.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) body.font = TMP_Settings.defaultFontAsset;

        // Keep the illustration in a dedicated footer slot. Placing it inside the
        // scroll content caused long lessons to push it past the viewport and crop it.
        var illustrationGo = new GameObject("LessonIllustration", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        illustrationGo.transform.SetParent(page.transform, false);
        illustration = illustrationGo.GetComponent<Image>();
        illustration.preserveAspect = true;
        illustration.raycastTarget = false;
        var illustrationLe = illustrationGo.GetComponent<LayoutElement>();
        illustrationLe.minHeight = LessonIllustrationHeight;
        illustrationLe.preferredHeight = LessonIllustrationHeight;
        illustrationLe.flexibleHeight = 0f;
        illustrationGo.SetActive(false);

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = (RectTransform)scrollGo.transform;
        scroll.content = contentRt;
        GameUITheme.ConfigureScroll(scroll);

        rewardButton = CreateGlossaryRewardButton(page.transform);
    }

    static Button CreateGlossaryRewardButton(Transform parent)
    {
        var go = new GameObject("ClaimGlossaryReward", typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = 42f;
        layout.flexibleHeight = 0f;
        go.GetComponent<Image>().color = GameUITheme.Positive;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        ApplyButtonColors(button);
        ColorBlock colors = button.colors;
        colors.normalColor = GameUITheme.Positive;
        colors.highlightedColor = GameUITheme.PositiveHover;
        colors.pressedColor = GameUITheme.Accent;
        colors.selectedColor = GameUITheme.PositiveHover;
        colors.disabledColor = NavColor;
        button.colors = colors;
        TextMeshProUGUI label = CreateLabel(go.transform, "Label", "CLAIM $50 FIRST-READ REWARD",
            15f, TextAlignmentOptions.Center, NavText);
        Stretch(label.rectTransform, 8f, 2f, 8f, 2f);
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        go.SetActive(false);
        return button;
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
        ApplyPage(leftIndex < pages.Length ? pages[leftIndex] : default, leftChapter, leftTitle, leftBody, leftIllustration,
            leftIndex < pages.Length, focusedTermPageIndex == leftIndex ? focusedTerm : null);
        ApplyPage(rightIndex < pages.Length ? pages[rightIndex] : default, rightChapter, rightTitle, rightBody, rightIllustration,
            rightIndex < pages.Length, focusedTermPageIndex == rightIndex ? focusedTerm : null);
        RefreshGlossaryRewardButton(leftRewardButton, leftIndex);
        RefreshGlossaryRewardButton(rightRewardButton, rightIndex);

        int shownLeft = leftIndex + 1;
        int shownRight = Mathf.Min(rightIndex + 1, pages.Length);
        folioText.text = shownLeft + "–" + shownRight + "  /  " + pages.Length;

        int maxSpread = (pages.Length + 1) / 2 - 1;
        prevButton.interactable = spreadIndex > 0;
        nextButton.interactable = spreadIndex < maxSpread;
        RefreshCategoryBookmarks(rightIndex);
    }

    void RefreshGlossaryRewardButton(Button button, int pageIndex)
    {
        if (button == null) return;
        bool show = !string.IsNullOrWhiteSpace(focusedTerm)
            && focusedTermPageIndex == pageIndex
            && GuidebookUI.IsGlossaryTermDiscovered(focusedTerm);
        button.gameObject.SetActive(show);
        if (!show) return;

        bool claimed = GuidebookUI.IsGlossaryTermRewardClaimed(focusedTerm);
        button.interactable = !claimed;
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = claimed ? "FIRST-READ REWARD CLAIMED" : "CLAIM $50 FIRST-READ REWARD";
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = claimed ? NavColor : GameUITheme.Positive;
    }

    void ClaimFocusedTermReward()
    {
        if (string.IsNullOrWhiteSpace(focusedTerm)
            || !GuidebookUI.IsGlossaryTermDiscovered(focusedTerm)
            || GuidebookUI.IsGlossaryTermRewardClaimed(focusedTerm))
            return;

        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (money == null) return;
        money.AddMoney(GlossaryTermReward);
        ClaimedGlossaryTermRewards.Add(NormalizeGlossaryTerm(focusedTerm));
        SaveGlossaryProgress();
        GameSaveSlots.SaveActiveSlot();
        Sfx.Play(SfxId.EarnMoney);
        Refresh();
    }

    void RefreshCategoryBookmarks(int visibleRightPage)
    {
        int visibleLeftPage = Mathf.Max(0, visibleRightPage - 1);
        for (int i = 0; i < categoryButtons.Count; i++)
        {
            Button button = categoryButtons[i];
            if (button == null) continue;
            int chapterStart = categoryPageIndexes[i];
            int chapterEnd = i + 1 < categoryPageIndexes.Count
                ? categoryPageIndexes[i + 1] - 1 : GuidebookPages.All.Length - 1;
            bool selected = chapterStart <= visibleRightPage && chapterEnd >= visibleLeftPage;
            if (button.targetGraphic != null)
                button.targetGraphic.color = GameUITheme.Surface;
            // Keep button state tint neutral so Unity's transition colors cannot
            // wash the dark bookmark backgrounds out to white.
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = selected ? new Color(1f, 0.79f, 0.27f, 1f) : GameUITheme.TextPrimary;
        }
    }

    static void ApplyPage(GuidebookPages.Page page, TextMeshProUGUI chapter, TextMeshProUGUI title,
        TextMeshProUGUI body, Image illustration, bool hasPage, string focusedTerm = null)
    {
        if (!hasPage)
        {
            chapter.text = "";
            title.text = "";
            body.text = "";
            illustration.sprite = null;
            illustration.gameObject.SetActive(false);
            return;
        }

        chapter.text = (page.chapter ?? "").ToUpperInvariant();
        title.text = page.title ?? "";
        body.text = GuidebookPages.HighlightGlossaryEntry(page.body ?? "", focusedTerm);
        illustration.sprite = LoadLessonIllustration(page.chapter);
        illustration.gameObject.SetActive(illustration.sprite != null);
        var scroll = body.GetComponentInParent<ScrollRect>();
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    static Sprite LoadLessonIllustration(string chapter)
    {
        string assetName;
        switch (chapter)
        {
            case "ISE Loop": assetName = "ISELoop"; break;
            case "Plan": assetName = "TaktTime"; break;
            case "Diagnose": assetName = "DiagnoseBottleneck"; break;
            case "Flow": assetName = "FlowQueue"; break;
            case "Work Design": assetName = "WorkDesignRoute"; break;
            case "Improve": assetName = "ImproveOneChange"; break;
            default: return null;
        }

        if (IllustrationCache.TryGetValue(assetName, out Sprite cached))
            return cached;

        Texture2D texture = Resources.Load<Texture2D>(IllustrationResourcesPath + assetName);
        if (texture == null)
        {
            Debug.LogWarning("Guidebook illustration was not found: " + assetName);
            IllustrationCache[assetName] = null;
            return null;
        }

        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        IllustrationCache[assetName] = sprite;
        return sprite;
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
        GameIconLibrary.ApplyToButton(btn,
            name == "PrevButton" ? GameIconId.Previous : GameIconId.Next,
            24f, 0f, true);
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
        if (name == "CloseButton")
            GameIconLibrary.ApplyToButton(btn, GameIconId.Cross, 20f, 0f, true);
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
