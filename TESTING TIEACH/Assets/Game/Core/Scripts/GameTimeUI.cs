using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Sims-style clock and speed controls. Lives on TopHudBar/TimeSection.
/// </summary>
public class GameTimeUI : MonoBehaviour
{
    [Header("References")]
    public GameTimeManager timeManager;
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI clockText;
    public TextMeshProUGUI rushText;
    public Button pauseButton;
    public Button playButton;
    public Button fastForwardButton;

    Image pauseHighlight;
    Image playHighlight;
    Image fastHighlight;

    void Awake()
    {
        if (timeManager == null)
            timeManager = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();

        BindReferences();
        if (clockText == null && transform.childCount == 0)
            BuildDefaultLayout();
        TuneTimeRow();
        EnsureSpeedStrip();

        CacheButtonHighlights();
        WireButtons();
    }

    void OnEnable()
    {
        if (timeManager != null)
        {
            timeManager.OnTimeChanged += Refresh;
            timeManager.OnSpeedChanged += Refresh;
            timeManager.OnDayEnded += Refresh;
        }
        EnsureSpeedStrip();
        CacheButtonHighlights();
        WireButtons();
        TuneTimeRow();
        Refresh();
    }

    void OnDisable()
    {
        if (timeManager != null)
        {
            timeManager.OnTimeChanged -= Refresh;
            timeManager.OnSpeedChanged -= Refresh;
            timeManager.OnDayEnded -= Refresh;
        }
    }

    void Update()
    {
        if (timeManager == null)
            timeManager = GameTimeManager.Instance;
        ApplyClockText();
    }

    void BindReferences()
    {
        if (dayText == null)
            dayText = FindComponentByNames<TextMeshProUGUI>("DayText", "Day");

        if (clockText == null)
            clockText = FindComponentByNames<TextMeshProUGUI>("ClockText", "Clock", "TimeText");
        if (rushText == null)
            rushText = FindComponentByNames<TextMeshProUGUI>("RushText");

        if (pauseButton == null)
            pauseButton = FindComponentByNames<Button>("PauseButton", "||");

        if (playButton == null)
            playButton = FindComponentByNames<Button>("PlayButton", ">");

        if (fastForwardButton == null)
            fastForwardButton = FindComponentByNames<Button>("FastForwardButton", "FastButton", ">>");
    }

    void BuildDefaultLayout()
    {
        if (GetComponentInParent<TopHudBar>() != null)
        {
            BuildTimeRow(transform);
            return;
        }

        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -12f);
        rt.sizeDelta = new Vector2(420f, 56f);

        if (GetComponent<Image>() == null)
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.12f, 0.88f);
            bg.raycastTarget = true;
        }

        BuildTimeRow(transform);
    }

    void BuildTimeRow(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);

        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = Vector2.zero;
        rowRt.offsetMax = Vector2.zero;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        dayText = CreateLabel(row.transform, "DayText", "Day 1", 18, 70f);
        dayText.fontStyle = FontStyles.Bold;
        dayText.color = new Color(0.85f, 0.88f, 0.95f, 1f);

        clockText = CreateLabel(row.transform, "ClockText", "10:00 AM", 20, 118f);
        clockText.fontStyle = FontStyles.Bold;
        clockText.overflowMode = TextOverflowModes.Truncate;
        clockText.enableWordWrapping = false;
        clockText.enableAutoSizing = true;
        clockText.fontSizeMin = 16;
        clockText.fontSizeMax = 20;

        rushText = CreateLabel(row.transform, "RushText", "RUSH", 16, 56f);
        rushText.fontStyle = FontStyles.Bold;
        rushText.color = new Color(1f, 0.58f, 0.22f, 1f);
        rushText.overflowMode = TextOverflowModes.Truncate;
        rushText.enableWordWrapping = false;
    }

    const string SpeedStripName = "SpeedControls";

    void EnsureSpeedStrip()
    {
        Transform host = transform.parent;
        var bar = GetComponentInParent<TopHudBar>();
        if (bar != null && bar.transform.parent != null)
            host = bar.transform.parent;
        if (host == null)
            host = transform;

        Transform existing = host.Find(SpeedStripName);
        if (existing == null && bar != null)
            existing = bar.transform.Find(SpeedStripName);

        GameObject strip;
        if (existing != null)
        {
            strip = existing.gameObject;
        }
        else
        {
            strip = new GameObject(SpeedStripName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        }

        if (strip.transform.parent != host)
            strip.transform.SetParent(host, false);

        var ignore = strip.GetComponent<LayoutElement>() ?? strip.AddComponent<LayoutElement>();
        ignore.ignoreLayout = true;

        var hlg = strip.GetComponent<HorizontalLayoutGroup>() ?? strip.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        if (pauseButton == null)
            pauseButton = FindNamedButton(strip.transform, "PauseButton");
        if (playButton == null)
            playButton = FindNamedButton(strip.transform, "PlayButton");
        if (fastForwardButton == null)
            fastForwardButton = FindNamedButton(strip.transform, "FastButton") ?? FindNamedButton(strip.transform, "FastForwardButton");

        if (pauseButton != null && pauseButton.transform.parent != strip.transform)
            pauseButton.transform.SetParent(strip.transform, false);
        if (playButton != null && playButton.transform.parent != strip.transform)
            playButton.transform.SetParent(strip.transform, false);
        if (fastForwardButton != null && fastForwardButton.transform.parent != strip.transform)
            fastForwardButton.transform.SetParent(strip.transform, false);

        if (pauseButton == null)
            pauseButton = CreateSpeedButton(strip.transform, "PauseButton", "||", out pauseHighlight);
        if (playButton == null)
            playButton = CreateSpeedButton(strip.transform, "PlayButton", ">", out playHighlight);
        if (fastForwardButton == null)
            fastForwardButton = CreateSpeedButton(strip.transform, "FastForwardButton", ">>", out fastHighlight);

        pauseButton.transform.SetSiblingIndex(0);
        playButton.transform.SetSiblingIndex(1);
        fastForwardButton.transform.SetSiblingIndex(2);

        var leftover = transform.Find("Row/Controls");
        if (leftover != null)
            Destroy(leftover.gameObject);

        PositionSpeedStrip(strip.GetComponent<RectTransform>(), bar);
    }

    static Button FindNamedButton(Transform root, string objectName)
    {
        foreach (var btn in root.GetComponentsInChildren<Button>(true))
        {
            if (btn != null && btn.gameObject.name == objectName)
                return btn;
        }
        return null;
    }

    void LateUpdate()
    {
        Transform host = transform.parent;
        var bar = GetComponentInParent<TopHudBar>();
        if (bar != null && bar.transform.parent != null)
            host = bar.transform.parent;
        if (host == null) return;
        var strip = host.Find(SpeedStripName) as RectTransform;
        if (strip == null && bar != null)
            strip = bar.transform.Find(SpeedStripName) as RectTransform;
        if (strip != null)
            PositionSpeedStrip(strip, bar);
    }

    static void PositionSpeedStrip(RectTransform strip, TopHudBar bar)
    {
        if (strip == null) return;
        strip.anchorMin = new Vector2(0.5f, 1f);
        strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(158f, 36f);

        float y = -62f;
        if (bar != null)
        {
            var barRt = (RectTransform)bar.transform;
            y = barRt.anchoredPosition.y - barRt.rect.height - 6f;
        }
        strip.anchoredPosition = new Vector2(0f, y);
    }

    void CacheButtonHighlights()
    {
        if (pauseHighlight == null && pauseButton != null)
            pauseHighlight = pauseButton.GetComponent<Image>();
        if (playHighlight == null && playButton != null)
            playHighlight = playButton.GetComponent<Image>();
        if (fastHighlight == null && fastForwardButton != null)
            fastHighlight = fastForwardButton.GetComponent<Image>();
    }

    T FindComponentByNames<T>(params string[] names) where T : Component
    {
        foreach (var name in names)
        {
            var t = transform.Find(name);
            if (t != null)
            {
                var direct = t.GetComponent<T>();
                if (direct != null) return direct;
                direct = t.GetComponentInChildren<T>(true);
                if (direct != null) return direct;
            }
        }

        foreach (var name in names)
        {
            foreach (var comp in GetComponentsInChildren<T>(true))
            {
                if (comp != null && comp.gameObject.name == name)
                    return comp;
            }
        }

        return null;
    }

    void WireButtons()
    {
        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveAllListeners();
            pauseButton.onClick.AddListener(() =>
            {
                Sfx.Play(SfxId.UiClick);
                timeManager?.SetSpeed(GameTimeManager.SpeedMode.Paused);
            });
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(() =>
            {
                Sfx.Play(SfxId.UiClick);
                if (timeManager != null && timeManager.IsShiftOver)
                {
                    if (EndOfDaySummaryUI.Instance != null)
                    {
                        if (EndOfDaySummaryUI.Instance.IsVisible)
                            EndOfDaySummaryUI.Instance.ContinueToNextDay();
                        else
                            EndOfDaySummaryUI.Instance.Show();
                    }
                    else
                        timeManager.StartNextDay();
                }
                else
                    timeManager?.SetSpeed(GameTimeManager.SpeedMode.Play);
            });
        }

        if (fastForwardButton != null)
        {
            fastForwardButton.onClick.RemoveAllListeners();
            fastForwardButton.onClick.AddListener(() =>
            {
                Sfx.Play(SfxId.UiClick);
                timeManager?.SetSpeed(GameTimeManager.SpeedMode.FastForward);
            });
        }
    }

    void Refresh()
    {
        if (timeManager == null) return;

        if (dayText != null)
            dayText.text = timeManager.GetDayText();

        ApplyClockText();

        SetHighlight(pauseHighlight, timeManager.CurrentSpeed == GameTimeManager.SpeedMode.Paused);
        SetHighlight(playHighlight, timeManager.CurrentSpeed == GameTimeManager.SpeedMode.Play && !timeManager.IsShiftOver);
        SetHighlight(fastHighlight, timeManager.CurrentSpeed == GameTimeManager.SpeedMode.FastForward);

        if (playButton != null)
        {
            var label = playButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = timeManager.IsShiftOver ? "Next" : ">";
        }
    }

    void ApplyClockText()
    {
        if (timeManager == null) return;
        EnsureRushLabel();

        if (clockText != null)
        {
            clockText.enableWordWrapping = false;
            clockText.overflowMode = TextOverflowModes.Truncate;
            clockText.text = timeManager.GetClockText();
            clockText.color = Color.white;
        }

        if (rushText != null)
        {
            if (!rushText.gameObject.activeSelf)
                rushText.gameObject.SetActive(true);
            bool rush = timeManager.IsRushHour;
            rushText.text = rush ? "RUSH" : "";
            rushText.color = new Color(1f, 0.58f, 0.22f, 1f);
            rushText.overflowMode = TextOverflowModes.Truncate;
            rushText.enableWordWrapping = false;
            var rushLayout = rushText.GetComponent<LayoutElement>() ?? rushText.gameObject.AddComponent<LayoutElement>();
            rushLayout.minWidth = 56f;
            rushLayout.preferredWidth = 56f;
            rushLayout.flexibleWidth = 0f;
        }
    }

    void TuneTimeRow()
    {
        var row = transform.Find("Row");
        if (row != null)
        {
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = 8f;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
            }
        }

        if (dayText != null)
        {
            var le = dayText.GetComponent<LayoutElement>() ?? dayText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 64f;
            le.preferredWidth = 70f;
            le.flexibleWidth = 0f;
        }
        if (clockText != null)
        {
            var le = clockText.GetComponent<LayoutElement>() ?? clockText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 110f;
            le.preferredWidth = 118f;
            le.flexibleWidth = 0f;
            clockText.enableAutoSizing = true;
            clockText.fontSizeMin = 16;
            clockText.fontSizeMax = 20;
            clockText.enableWordWrapping = false;
            clockText.overflowMode = TextOverflowModes.Truncate;
        }
        if (rushText != null)
        {
            var le = rushText.GetComponent<LayoutElement>() ?? rushText.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 56f;
            le.preferredWidth = 56f;
            le.flexibleWidth = 0f;
            rushText.gameObject.SetActive(true);
        }
    }

    void EnsureRushLabel()
    {
        if (rushText != null) return;
        Transform parent = clockText != null ? clockText.transform.parent : transform;
        if (parent == null) return;

        rushText = CreateLabel(parent, "RushText", "RUSH", 16, 56f);
        rushText.fontStyle = FontStyles.Bold;
        rushText.overflowMode = TextOverflowModes.Truncate;
        rushText.enableWordWrapping = false;
        if (clockText != null)
            rushText.transform.SetSiblingIndex(clockText.transform.GetSiblingIndex() + 1);
    }

    static void SetHighlight(Image img, bool on)
    {
        if (img == null) return;
        img.color = on
            ? new Color(0.35f, 0.55f, 0.42f, 1f)
            : new Color(0.22f, 0.24f, 0.3f, 1f);
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string objectName, string text, float size, float width)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static Button CreateSpeedButton(Transform parent, string objectName, string label, out Image highlight)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        highlight = go.AddComponent<Image>();
        highlight.color = new Color(0.22f, 0.24f, 0.3f, 1f);
        var btn = go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 44f;
        le.preferredWidth = 44f;
        le.minHeight = 32f;
        le.preferredHeight = 32f;
        le.flexibleWidth = 0f;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 18;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        return btn;
    }
}
