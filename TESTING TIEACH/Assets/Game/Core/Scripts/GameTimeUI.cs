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
        if ((pauseButton == null || clockText == null) && transform.childCount == 0)
            BuildDefaultLayout();

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
        if (timeManager != null && clockText != null)
            clockText.text = timeManager.GetClockText();
    }

    void BindReferences()
    {
        if (dayText == null)
            dayText = FindComponentByNames<TextMeshProUGUI>("DayText", "Day");

        if (clockText == null)
            clockText = FindComponentByNames<TextMeshProUGUI>("ClockText", "Clock", "TimeText");

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
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        dayText = CreateLabel(row.transform, "DayText", "Day 1", 18, 110f);
        dayText.fontStyle = FontStyles.Bold;
        dayText.color = new Color(0.85f, 0.88f, 0.95f, 1f);

        clockText = CreateLabel(row.transform, "ClockText", "10:00 AM", 28, 150f);
        clockText.fontStyle = FontStyles.Bold;

        var controls = new GameObject("Controls", typeof(RectTransform));
        controls.transform.SetParent(row.transform, false);
        controls.AddComponent<LayoutElement>().minWidth = 150f;
        var controlsH = controls.AddComponent<HorizontalLayoutGroup>();
        controlsH.spacing = 6f;
        controlsH.childAlignment = TextAnchor.MiddleCenter;
        controlsH.childControlWidth = true;
        controlsH.childControlHeight = true;
        controlsH.childForceExpandWidth = true;
        controlsH.childForceExpandHeight = true;

        pauseButton = CreateSpeedButton(controls.transform, "PauseButton", "||", out pauseHighlight);
        playButton = CreateSpeedButton(controls.transform, "PlayButton", ">", out playHighlight);
        fastForwardButton = CreateSpeedButton(controls.transform, "FastForwardButton", ">>", out fastHighlight);
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
                    timeManager.StartNextDay();
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

        if (clockText != null)
            clockText.text = timeManager.GetClockText();

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
        go.AddComponent<LayoutElement>().minWidth = 44f;

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
