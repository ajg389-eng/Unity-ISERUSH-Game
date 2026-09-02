using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Sims-style clock and speed controls for a scene-placed GameTimeBar.
/// Assign references in the Inspector, or use child names listed in BindReferences().
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

    void CacheButtonHighlights()
    {
        pauseHighlight = pauseButton != null ? pauseButton.GetComponent<Image>() : null;
        playHighlight = playButton != null ? playButton.GetComponent<Image>() : null;
        fastHighlight = fastForwardButton != null ? fastForwardButton.GetComponent<Image>() : null;
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
}
