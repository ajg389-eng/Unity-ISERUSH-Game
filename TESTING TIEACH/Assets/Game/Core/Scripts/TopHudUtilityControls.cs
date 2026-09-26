using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Music controls and notification history attached to the main top HUD.</summary>
public class TopHudUtilityControls : MonoBehaviour
{
    public const string MusicSectionName = "MusicSection";
    public const string NotificationSectionName = "NotificationSection";
    const string PanelName = "NotificationHistoryPanel";

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
    }

    public void EnsureLayout()
    {
        EnsureMusicSection();
        EnsureNotificationSection();
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
        }
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
        layout.childForceExpandHeight = true;

        trackText = FindText(section.transform, "TrackText");
        if (trackText == null)
        {
            trackText = CreateLabel(section.transform, "TrackText", "No track", 13);
            var le = trackText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 132f;
            le.preferredWidth = 132f;
        }
        trackText.alignment = TextAlignmentOptions.MidlineRight;
        trackText.overflowMode = TextOverflowModes.Ellipsis;
        trackText.enableWordWrapping = false;

        Button pause = FindButton(section.transform, "MusicPauseButton");
        if (pause == null)
            pause = CreateButton(section.transform, "MusicPauseButton", "||", 34f, out playPauseText);
        else
            playPauseText = pause.GetComponentInChildren<TextMeshProUGUI>(true);
        pause.onClick.RemoveAllListeners();
        pause.onClick.AddListener(() => { Sfx.Play(SfxId.UiClick); BindMusic(); music?.TogglePause(); });

        Button next = FindButton(section.transform, "MusicNextButton");
        if (next == null)
            next = CreateButton(section.transform, "MusicNextButton", ">>", 40f, out _);
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

        Button clear = CreateButton(root, "ClearButton", "Clear", 66f, out _);
        var clearRt = (RectTransform)clear.transform;
        clearRt.anchorMin = clearRt.anchorMax = new Vector2(1f, 1f);
        clearRt.pivot = new Vector2(1f, 1f);
        clearRt.anchoredPosition = new Vector2(-12f, -10f);
        clearRt.sizeDelta = new Vector2(66f, 32f);
        clear.onClick.AddListener(() => { Sfx.Play(SfxId.UiClick); NotificationCenter.Clear(); });

        var scrollGo = new GameObject("Scroll View", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(root, false);
        SetRect((RectTransform)scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -54f));

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        SetRect((RectTransform)viewport.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.GetComponent<Image>().color = new Color(0.04f, 0.045f, 0.07f, 0.8f);

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
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = historyContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
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
            var row = new GameObject("Notification", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(historyContent, false);
            row.GetComponent<Image>().color = entry.kind == GameNotificationKind.Warning
                ? new Color(0.22f, 0.16f, 0.07f, 0.95f)
                : new Color(0.12f, 0.14f, 0.20f, 0.95f);
            row.GetComponent<LayoutElement>().preferredHeight = 58f;

            var label = CreateLabel(row.transform, "Text", "<color=#8FA4BC>" + entry.time + "</color>  " + entry.message, 13);
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-10f, -6f));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = true;
        }
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
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }
}
