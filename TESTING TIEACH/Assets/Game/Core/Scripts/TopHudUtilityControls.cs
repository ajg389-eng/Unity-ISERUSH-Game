using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Music controls and notification history attached to the main top HUD.</summary>
public class TopHudUtilityControls : MonoBehaviour
{
    public const string MusicSectionName = "MusicSection";
    public const string NotificationSectionName = "NotificationSection";
    public const string GusHelpSectionName = "GusHelpSection";
    public const string InvestorLetterId = "investor-welcome-letter";
    const string PanelName = "NotificationHistoryPanel";
    const string MusicPanelName = "MusicTrackPicker";
    static readonly string InvestorLetterMessage =
        "Dear Player,\n" +
        "We understand that Gus is difficult. His methods have kept the place open for 25 years, but our internal metrics suggest closure is certain in the next year unless you can implement effective changes. Keep in mind workflows and station arrangements to reduce bottlenecks, and be sure to make data-driven decisions. We will be reviewing your quarterly results closely.\n" +
        "Kindest Regards,\n" +
        "QuickBurger Investment Group";

    static readonly Color Panel = new Color(0.075f, 0.08f, 0.12f, 0.98f);
    static readonly Color Control = new Color(0.18f, 0.20f, 0.28f, 1f);
    static readonly Color Warning = new Color(1f, 0.72f, 0.23f, 1f);

    MusicManager music;
    TextMeshProUGUI trackText;
    TextMeshProUGUI playPauseText;
    TextMeshProUGUI notificationButtonText;
    Button notificationButton;
    GameObject historyPanel;
    RectTransform historyContent;
    GameTimeManager boundTime;
    GameObject musicPanel;
    RectTransform musicList;
    readonly HashSet<GameNotification> expandedNotifications = new HashSet<GameNotification>();

    void Awake() => EnsureLayout();

    void OnEnable()
    {
        NotificationCenter.Changed += RefreshNotifications;
        BindMusic();
        BindTime();
        RefreshMusic();
        RefreshNotifications();
    }

    void OnDisable()
    {
        NotificationCenter.Changed -= RefreshNotifications;
        if (music != null) music.OnPlaybackChanged -= RefreshMusic;
        music = null;
        if (boundTime != null)
        {
            boundTime.OnDayStarted -= OnDayStarted;
            boundTime.OnDayEnded -= OnDayEnded;
        }
        boundTime = null;
    }

    void Update()
    {
        if (music == null) BindMusic();
        if (boundTime == null) BindTime();
        RefreshMusic();
        FlashUnreadNotice();
    }

    void FlashUnreadNotice()
    {
        if (notificationButton == null) return;
        var image = notificationButton.targetGraphic as Image;
        bool unread = NotificationCenter.UnreadCount > 0;
        if (!unread)
        {
            if (image != null) image.color = Control;
            notificationButton.transform.localScale = Vector3.one;
            return;
        }

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
        if (image != null)
            image.color = Color.Lerp(new Color(0.45f, 0.16f, 0.08f, 1f), Warning, pulse);
        if (notificationButtonText != null)
            notificationButtonText.color = Color.Lerp(Color.white, Warning, 0.35f + 0.65f * pulse);
        float scale = 1f + 0.08f * pulse;
        notificationButton.transform.localScale = new Vector3(scale, scale, 1f);
    }

    public void EnsureLayout()
    {
        EnsureMusicSection();
        EnsureMusicPanel();
        EnsureNotificationSection();
        EnsureGusHelpSection();
        EnsureHistoryPanel();
        var bar = GetComponent<TopHudBar>();
        if (bar != null) bar.ApplyFitLayout();
    }

    void BindMusic()
    {
        var found = MusicManager.Instance ?? FindFirstObjectByType<MusicManager>();
        if (found == music) return;
        if (music != null) music.OnPlaybackChanged -= RefreshMusic;
        music = found;
        if (music != null) music.OnPlaybackChanged += RefreshMusic;
    }

    void BindTime()
    {
        var found = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();
        if (found == boundTime) return;
        if (boundTime != null)
        {
            boundTime.OnDayStarted -= OnDayStarted;
            boundTime.OnDayEnded -= OnDayEnded;
        }
        boundTime = found;
        if (boundTime != null)
        {
            boundTime.OnDayStarted += OnDayStarted;
            boundTime.OnDayEnded += OnDayEnded;
            NotificationCenter.Post("Day " + boundTime.CurrentDay + " shift active.",
                GameNotificationKind.Message, "day-active-" + boundTime.CurrentDay);
            EnsureInvestorLetter();
        }
    }

    void EnsureInvestorLetter()
    {
        bool alreadySent = GameSaveSlots.ActiveSlotInvestorLetterSent();
        NotificationCenter.Post(InvestorLetterMessage, GameNotificationKind.Message,
            InvestorLetterId, 0f, alreadySent, true);
        if (!alreadySent)
            GameSaveSlots.MarkActiveSlotInvestorLetterSent();
    }

    void OnDayStarted()
    {
        NotificationCenter.Post("Day " + boundTime.CurrentDay + " shift started.", GameNotificationKind.Message,
            "day-start-" + boundTime.CurrentDay);
    }

    void OnDayEnded()
    {
        NotificationCenter.Post("Day " + boundTime.CurrentDay + " shift complete. Review the daily summary.",
            GameNotificationKind.Message, "day-end-" + boundTime.CurrentDay);
    }

    void EnsureMusicSection()
    {
        Transform existing = transform.Find(MusicSectionName);
        GameObject section = existing != null ? existing.gameObject : new GameObject(MusicSectionName, typeof(RectTransform));
        if (section.transform.parent != transform) section.transform.SetParent(transform, false);

        var layout = section.GetComponent<HorizontalLayoutGroup>() ?? section.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        trackText = FindText(section.transform, "TrackText");
        if (trackText == null)
        {
            trackText = CreateLabel(section.transform, "TrackText", "No track", 13);
            var le = trackText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 116f;
            le.preferredWidth = 116f;
        }
        var trackLayout = trackText.GetComponent<LayoutElement>() ?? trackText.gameObject.AddComponent<LayoutElement>();
        trackLayout.minWidth = 116f;
        trackLayout.preferredWidth = 116f;
        trackLayout.flexibleWidth = 0f;
        trackText.alignment = TextAlignmentOptions.Center;
        trackText.overflowMode = TextOverflowModes.Ellipsis;
        trackText.enableWordWrapping = false;
        EnsureTrackBox(section.transform);
        Button picker = trackText.transform.parent.GetComponent<Button>();
        if (picker == null) picker = trackText.transform.parent.gameObject.AddComponent<Button>();
        picker.onClick.RemoveAllListeners();
        picker.onClick.AddListener(ToggleMusicPicker);
        var pickerColors = picker.colors;
        pickerColors.normalColor = Color.white;
        picker.colors = pickerColors;

        Button pause = FindButton(section.transform, "MusicPauseButton");
        if (pause == null)
            pause = CreateButton(section.transform, "MusicPauseButton", "||", 34f, out playPauseText);
        else
            playPauseText = pause.GetComponentInChildren<TextMeshProUGUI>(true);
        SetSquareButtonLayout(pause, 34f);
        GameUITheme.ApplyCompactControlEffects(pause);
        pause.onClick.RemoveAllListeners();
        pause.onClick.AddListener(() => { Sfx.Play(SfxId.UiClick); BindMusic(); music?.TogglePause(); });

        Button next = FindButton(section.transform, "MusicNextButton");
        if (next == null)
            next = CreateButton(section.transform, "MusicNextButton", ">>", 34f, out _);
        SetSquareButtonLayout(next, 34f);
        GameUITheme.ApplyCompactControlEffects(next);
        next.onClick.RemoveAllListeners();
        next.onClick.AddListener(() => { Sfx.Play(SfxId.UiClick); BindMusic(); music?.Next(); });
    }

    void EnsureNotificationSection()
    {
        Transform existing = transform.Find(NotificationSectionName);
        GameObject section = existing != null ? existing.gameObject : new GameObject(NotificationSectionName, typeof(RectTransform));
        if (section.transform.parent != transform) section.transform.SetParent(transform, false);

        notificationButton = FindButton(section.transform, "NotificationButton");
        if (notificationButton == null)
            notificationButton = CreateButton(section.transform, "NotificationButton", "!  NOTICES", 88f, out notificationButtonText);
        else
            notificationButtonText = notificationButton.GetComponentInChildren<TextMeshProUGUI>(true);

        var buttonRect = (RectTransform)notificationButton.transform;
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(88f, 34f);
        notificationButton.onClick.RemoveAllListeners();
        notificationButton.onClick.AddListener(ToggleHistory);
    }

    void EnsureGusHelpSection()
    {
        Transform existing = transform.Find(GusHelpSectionName);
        GameObject section = existing != null
            ? existing.gameObject
            : new GameObject(GusHelpSectionName, typeof(RectTransform));
        if (section.transform.parent != transform)
            section.transform.SetParent(transform, false);

        UnityEngine.UI.Button button = FindButton(section.transform, "GusHelpButton");
        if (button == null)
        {
            var buttonObject = new GameObject("GusHelpButton", typeof(RectTransform),
                typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            buttonObject.transform.SetParent(section.transform, false);
            button = buttonObject.GetComponent<UnityEngine.UI.Button>();
            buttonObject.GetComponent<UnityEngine.UI.Image>().color = new Color(0.10f, 0.12f, 0.17f, 1f);

            var portraitObject = new GameObject("GusPortrait", typeof(RectTransform),
                typeof(UnityEngine.UI.RawImage), typeof(GusCutscenePreview));
            portraitObject.transform.SetParent(buttonObject.transform, false);
            RectTransform portraitRect = (RectTransform)portraitObject.transform;
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(3f, 3f);
            portraitRect.offsetMax = new Vector2(-3f, -3f);
            portraitObject.GetComponent<UnityEngine.UI.RawImage>().raycastTarget = false;

            TitleScreenController title = FindFirstObjectByType<TitleScreenController>(FindObjectsInactive.Include);
            GameObject gusPrefab = title != null ? title.gusPrefab : null;
            if (gusPrefab != null)
                portraitObject.GetComponent<GusCutscenePreview>().Configure(
                    gusPrefab, portraitObject.GetComponent<UnityEngine.UI.RawImage>(), true);
            else
            {
                Destroy(portraitObject.GetComponent<GusCutscenePreview>());
                var label = CreateLabel(portraitObject.transform, "Fallback", "G", 24f);
                label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.Bold;
                label.color = GameUITheme.Accent;
                SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
        }

        RectTransform buttonRect = (RectTransform)button.transform;
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            if (!ManagementTabInfoUI.OpenActiveHelp())
                NotificationCenter.Post("Open a management tab and ask Gus for details.",
                    GameNotificationKind.Message, "gus-help-no-tab", 2f);
        });
        GameUITheme.ApplyCompactControlEffects(button);
    }

    void EnsureHistoryPanel()
    {
        if (historyPanel != null) return;
        Transform canvas = GetComponentInParent<Canvas>()?.transform;
        if (canvas == null) return;

        Transform old = canvas.Find(PanelName);
        historyPanel = old != null ? old.gameObject : new GameObject(PanelName, typeof(RectTransform), typeof(Image));
        historyPanel.transform.SetParent(canvas, false);
        historyPanel.transform.SetAsLastSibling();

        var rt = (RectTransform)historyPanel.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(430f, -62f);
        rt.sizeDelta = new Vector2(390f, 340f);
        historyPanel.GetComponent<Image>().color = Panel;

        if (historyPanel.transform.childCount == 0)
            BuildHistoryPanel(historyPanel.transform);
        else
            historyContent = historyPanel.transform.Find("Scroll View/Viewport/Content") as RectTransform;
        historyPanel.SetActive(false);
    }

    void BuildHistoryPanel(Transform root)
    {
        var title = CreateLabel(root, "Title", "Notifications", 20);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -48f), new Vector2(-90f, -8f));
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        var hint = CreateLabel(root, "Hint", "Click a notice to expand · scroll for older", 10);
        hint.color = new Color(0.68f, 0.72f, 0.8f, 1f);
        hint.alignment = TextAlignmentOptions.MidlineLeft;
        SetRect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(16f, -72f), new Vector2(-78f, -52f));

        Button clear = CreateButton(root, "ClearButton", "Clear", 66f, out _);
        var clearRt = (RectTransform)clear.transform;
        clearRt.anchorMin = clearRt.anchorMax = new Vector2(1f, 1f);
        clearRt.pivot = new Vector2(1f, 1f);
        clearRt.anchoredPosition = new Vector2(-12f, -10f);
        clearRt.sizeDelta = new Vector2(66f, 32f);
        clear.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            expandedNotifications.Clear();
            NotificationCenter.Clear();
        });

        var scrollGo = new GameObject("Scroll View", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(root, false);
        SetRect((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -76f));

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        SetRect((RectTransform)viewport.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12f, 0f));
        viewport.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.07f, 0.8f);

        var scrollbarGo = new GameObject("Vertical Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        scrollbarGo.transform.SetParent(scrollGo.transform, false);
        SetRect((RectTransform)scrollbarGo.transform, new Vector2(1f, 0f), Vector2.one,
            new Vector2(-9f, 4f), new Vector2(-3f, -4f));
        scrollbarGo.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.1f, 0.9f);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(scrollbarGo.transform, false);
        SetRect((RectTransform)handleGo.transform, Vector2.zero, Vector2.one,
            new Vector2(1f, 1f), new Vector2(-1f, -1f));
        var handleImage = handleGo.GetComponent<Image>();
        handleImage.color = new Color(0.48f, 0.55f, 0.67f, 0.9f);
        var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
        scrollbar.handleRect = (RectTransform)handleGo.transform;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        historyContent = (RectTransform)content.transform;
        historyContent.anchorMin = new Vector2(0f, 1f);
        historyContent.anchorMax = new Vector2(1f, 1f);
        historyContent.pivot = new Vector2(0.5f, 1f);
        historyContent.anchoredPosition = Vector2.zero;
        historyContent.sizeDelta = Vector2.zero;
        var vertical = content.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(8, 8, 8, 8);
        vertical.spacing = 6f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        GameUITheme.ConfigureScroll(scroll);
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = historyContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    void ToggleHistory()
    {
        Sfx.Play(SfxId.UiOpen);
        EnsureHistoryPanel();
        if (historyPanel == null) return;
        bool show = !historyPanel.activeSelf;
        historyPanel.SetActive(show);
        if (show)
        {
            historyPanel.transform.SetAsLastSibling();
            RebuildHistory();
            NotificationCenter.MarkAllRead();
        }
    }

    void RefreshMusic()
    {
        if (trackText != null)
            trackText.text = music != null ? music.CurrentTrackName : "No music";
        if (playPauseText != null)
            playPauseText.text = music != null && music.IsPaused ? ">" : "||";
        if (musicPanel != null && musicPanel.activeSelf) RebuildMusicList();
    }

    void EnsureMusicPanel()
    {
        if (musicPanel != null) return;
        Transform canvas = GetComponentInParent<Canvas>()?.transform;
        if (canvas == null) return;
        Transform existing = canvas.Find(MusicPanelName);
        musicPanel = existing != null ? existing.gameObject : new GameObject(MusicPanelName, typeof(RectTransform), typeof(Image));
        musicPanel.transform.SetParent(canvas, false);
        var rt = (RectTransform)musicPanel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-216f, -62f);
        rt.sizeDelta = new Vector2(300f, 310f);
        musicPanel.GetComponent<Image>().color = Panel;
        if (musicPanel.transform.childCount == 0) BuildMusicPanel(musicPanel.transform);
        else musicList = musicPanel.transform.Find("Scroll View/Viewport/Content") as RectTransform;
        musicPanel.SetActive(false);
    }

    void BuildMusicPanel(Transform root)
    {
        var heading = CreateLabel(root, "Heading", "NOW PLAYING", 11);
        heading.color = new Color(0.42f, 0.86f, 0.62f, 1f);
        heading.fontStyle = FontStyles.Bold;
        SetRect(heading.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -34f), new Vector2(-14f, -8f));
        var scrollGo = new GameObject("Scroll View", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(root, false);
        SetRect((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -42f));
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        SetRect((RectTransform)viewport.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.07f, 0.45f);
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        musicList = (RectTransform)content.transform;
        musicList.anchorMin = new Vector2(0f, 1f); musicList.anchorMax = new Vector2(1f, 1f);
        musicList.pivot = new Vector2(0.5f, 1f); musicList.sizeDelta = Vector2.zero;
        var vertical = content.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(4, 4, 4, 4); vertical.spacing = 3f;
        vertical.childControlWidth = true; vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true; vertical.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollGo.GetComponent<ScrollRect>();
        GameUITheme.ConfigureScroll(scroll); scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = musicList; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    void ToggleMusicPicker()
    {
        Sfx.Play(SfxId.UiOpen);
        EnsureMusicPanel();
        if (musicPanel == null) return;
        bool show = !musicPanel.activeSelf;
        musicPanel.SetActive(show);
        if (show)
        {
            musicPanel.transform.SetAsLastSibling();
            BindMusic();
            RebuildMusicList();
        }
    }

    void RebuildMusicList()
    {
        if (musicList == null) return;
        for (int i = musicList.childCount - 1; i >= 0; i--) Destroy(musicList.GetChild(i).gameObject);
        if (music == null || music.TrackCount == 0)
        {
            var empty = CreateLabel(musicList, "Empty", "No tracks available", 13);
            empty.color = new Color(0.65f, 0.68f, 0.75f, 1f);
            empty.alignment = TextAlignmentOptions.Center;
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            return;
        }
        for (int i = 0; i < music.TrackCount; i++)
        {
            int trackIndex = i;
            bool active = i == music.CurrentTrackIndex;
            var row = new GameObject("Track " + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(musicList, false);
            row.GetComponent<Image>().color = active ? new Color(0.12f, 0.28f, 0.21f, 1f) : new Color(0.10f, 0.12f, 0.17f, 0.9f);
            row.GetComponent<LayoutElement>().preferredHeight = 46f;
            var title = CreateLabel(row.transform, "Title", music.GetTrack(i).name, 13);
            title.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            title.color = active ? new Color(0.48f, 0.91f, 0.66f, 1f) : Color.white;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.overflowMode = TextOverflowModes.Ellipsis;
            SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-34f, -2f));
            if (active)
            {
                var playing = CreateLabel(row.transform, "Playing", "♫", 16);
                playing.color = new Color(0.48f, 0.91f, 0.66f, 1f);
                SetRect(playing.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-30f, 0f), new Vector2(-6f, 0f));
                playing.alignment = TextAlignmentOptions.Center;
            }
            var button = row.GetComponent<Button>();
            button.onClick.AddListener(() => { Sfx.Play(SfxId.UiClick); BindMusic(); music?.PlayTrack(trackIndex); musicPanel.SetActive(false); });
            GameUITheme.ApplyCompactControlEffects(button);
        }
    }

    void RefreshNotifications()
    {
        if (notificationButtonText != null)
        {
            int unread = NotificationCenter.UnreadCount;
            notificationButtonText.text = unread > 0 ? "!  " + Mathf.Min(99, unread) : "!  NOTICES";
            notificationButtonText.color = unread > 0 ? Warning : Color.white;
        }
        if (historyPanel != null && historyPanel.activeSelf)
            RebuildHistory();
    }

    void RebuildHistory()
    {
        if (historyContent == null) return;
        for (int i = historyContent.childCount - 1; i >= 0; i--)
            Destroy(historyContent.GetChild(i).gameObject);

        if (NotificationCenter.Entries.Count == 0)
        {
            var empty = CreateLabel(historyContent, "Empty", "No notifications yet.", 14);
            empty.color = new Color(0.65f, 0.68f, 0.75f, 1f);
            empty.alignment = TextAlignmentOptions.Center;
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
            return;
        }

        foreach (var entry in NotificationCenter.Entries)
        {
            bool expanded = expandedNotifications.Contains(entry);
            var row = new GameObject("Notification", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(historyContent, false);
            var rowImage = row.GetComponent<Image>();
            rowImage.color = entry.kind == GameNotificationKind.Warning
                ? new Color(0.22f, 0.16f, 0.07f, 0.95f)
                : new Color(0.12f, 0.14f, 0.20f, 0.95f);
            row.GetComponent<Button>().transition = Selectable.Transition.None;

            string displayMessage = expanded
                ? entry.message
                : GetNotificationPreview(entry.message);
            var label = CreateLabel(row.transform, "Text", "<color=#AEBBCD>" + entry.time + "</color>  " + displayMessage, 12);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 5f), new Vector2(-32f, -5f));
            label.alignment = expanded ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = expanded;
            label.overflowMode = expanded ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;

            var toggle = CreateLabel(row.transform, "ExpandIndicator", expanded ? "−" : "+", 16);
            SetRect(toggle.rectTransform, new Vector2(1f, 0f), Vector2.one,
                new Vector2(-28f, 0f), new Vector2(-4f, 0f));
            toggle.alignment = TextAlignmentOptions.Center;
            toggle.color = new Color(0.7f, 0.78f, 0.88f, 1f);

            float height = 38f;
            if (expanded)
            {
                float panelWidth = ((RectTransform)historyPanel.transform).sizeDelta.x;
                float availableTextWidth = Mathf.Max(120f, panelWidth - 80f);
                float textHeight = label.GetPreferredValues(label.text, availableTextWidth, 0f).y;
                height = Mathf.Max(54f, textHeight + 12f);
            }
            row.GetComponent<LayoutElement>().preferredHeight = height;
            row.GetComponent<Button>().onClick.AddListener(() =>
            {
                Sfx.Play(SfxId.UiClick);
                if (!expandedNotifications.Add(entry))
                    expandedNotifications.Remove(entry);
                RebuildHistory();
            });
        }
    }

    static string GetNotificationPreview(string message)
    {
        const string deliveryPrefix = "Ingredient delivery arrived:";
        if (message.StartsWith(deliveryPrefix, System.StringComparison.OrdinalIgnoreCase))
            return "Ingredient delivery arrived";

        const int previewLength = 46;
        return message.Length <= previewLength
            ? message
            : message.Substring(0, previewLength - 1).TrimEnd() + "…";
    }

    static Button CreateButton(Transform parent, string name, string label, float width, out TextMeshProUGUI text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Control;
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = 34f;
        le.preferredHeight = 34f;
        text = CreateLabel(go.transform, "Label", label, 13);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        return go.GetComponent<Button>();
    }

    static void SetSquareButtonLayout(Button button, float size)
    {
        if (button == null) return;
        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = size;
        layout.preferredWidth = size;
        layout.flexibleWidth = 0f;
        layout.minHeight = size;
        layout.preferredHeight = size;
        layout.flexibleHeight = 0f;
    }

    void EnsureTrackBox(Transform section)
    {
        if (section == null || trackText == null) return;

        Transform existing = section.Find("MusicTrackBox");
        int siblingIndex = trackText.transform.parent == section
            ? trackText.transform.GetSiblingIndex()
            : 0;
        GameObject box = existing != null
            ? existing.gameObject
            : new GameObject("MusicTrackBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));

        if (box.transform.parent != section)
            box.transform.SetParent(section, false);
        box.transform.SetSiblingIndex(siblingIndex);

        var layout = box.GetComponent<LayoutElement>() ?? box.AddComponent<LayoutElement>();
        layout.minWidth = 116f;
        layout.preferredWidth = 116f;
        layout.flexibleWidth = 0f;
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
        layout.flexibleHeight = 0f;

        var image = box.GetComponent<Image>() ?? box.AddComponent<Image>();
        image.color = GameUITheme.Surface;
        image.raycastTarget = true;

        var outline = box.GetComponent<Outline>() ?? box.AddComponent<Outline>();
        outline.effectColor = GameUITheme.Edge;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        Shadow shadow = null;
        foreach (var effect in box.GetComponents<Shadow>())
        {
            if (effect != null && effect.GetType() == typeof(Shadow))
            {
                shadow = effect;
                break;
            }
        }
        if (shadow == null) shadow = box.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(0f, -1f);
        shadow.useGraphicAlpha = true;

        // Prevent the global container pass from changing this control to a
        // panel shade after it has been matched to the adjacent buttons.
        if (box.GetComponent<GameUIThemeStyled>() == null)
            box.AddComponent<GameUIThemeStyled>();

        if (trackText.transform.parent != box.transform)
            trackText.transform.SetParent(box.transform, false);
        SetRect(trackText.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(8f, 2f), new Vector2(-8f, -2f));
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string value, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    static Button FindButton(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        return t != null ? t.GetComponent<Button>() : null;
    }

    static TextMeshProUGUI FindText(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t.GetComponent<TextMeshProUGUI>();

        foreach (var text in parent.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text != null && text.name == name)
                return text;
        return null;
    }
}
