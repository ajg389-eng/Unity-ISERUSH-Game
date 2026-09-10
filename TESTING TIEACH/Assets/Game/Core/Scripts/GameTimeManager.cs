using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game clock and Sims-style speed control.
/// Day runs 10 AM → 10 PM; 1 game hour = 1 real minute at 1x speed.
/// Pause sets Time.timeScale to 0 (simulation freezes) but UI input and camera still work.
/// </summary>
public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    public enum SpeedMode
    {
        Paused = 0,
        Play = 1,
        FastForward = 2,
        SuperFast = 3
    }

    public const string PauseTitleScreen = "TitleScreen";
    public const string PauseManagement = "Management";
    public const string PauseMenu = "PauseMenu";

    [Header("Day schedule")]
    [Tooltip("Hour the shift starts (24h). Default 10 = 10 AM.")]
    public int dayStartHour = 10;
    [Tooltip("Hour the shift ends (24h). Default 22 = 10 PM.")]
    public int dayEndHour = 22;

    [Header("Timing")]
    [Tooltip("Real seconds per in-game hour at 1x speed.")]
    public float realSecondsPerGameHour = 60f;
    [Tooltip("Unity time scale while fast-forwarding.")]
    public float fastForwardTimeScale = 3f;
    [Tooltip("Unity time scale for debug super-speed.")]
    public float superFastTimeScale = 20f;
    [Tooltip("Pause automatically when the shift ends.")]
    public bool pauseAtDayEnd = true;

    public SpeedMode CurrentSpeed { get; private set; } = SpeedMode.Play;
    public int CurrentDay { get; private set; } = 1;
    public bool IsShiftOver { get; private set; }

    /// <summary>Minutes from midnight (fractional).</summary>
    public float CurrentMinutes { get; private set; }

    public event Action OnSpeedChanged;
    public event Action OnTimeChanged;
    public event Action OnDayEnded;
    public event Action OnDayStarted;

    readonly HashSet<string> externalPauseSources = new HashSet<string>();

    float DayStartMinutes => dayStartHour * 60f;
    float DayEndMinutes => dayEndHour * 60f;
    float GameMinutesPerRealSecond => 60f / Mathf.Max(1f, realSecondsPerGameHour);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<GameTimeManager>() != null) return;
        var go = new GameObject("GameTimeManager");
        go.AddComponent<GameTimeManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ResetDayClock();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        ApplyTimeScale();

        if (Time.timeScale <= 0f || IsShiftOver)
            return;

        CurrentMinutes += GameMinutesPerRealSecond * Time.deltaTime;

        if (CurrentMinutes >= DayEndMinutes)
        {
            CurrentMinutes = DayEndMinutes;
            EndShift();
        }

        OnTimeChanged?.Invoke();
    }

    public void ResetDayClock()
    {
        CurrentMinutes = DayStartMinutes;
        IsShiftOver = false;
        OnTimeChanged?.Invoke();
    }

    public void StartNextDay()
    {
        CurrentDay++;
        ResetDayClock();
        SetSpeed(SpeedMode.Play);
        OnDayStarted?.Invoke();
    }

    public void SetSpeed(SpeedMode mode)
    {
        if (IsShiftOver && mode != SpeedMode.Paused)
            return;

        if (CurrentSpeed == mode) return;
        CurrentSpeed = mode;
        ApplyTimeScale();
        OnSpeedChanged?.Invoke();
    }

    public void TogglePausePlay()
    {
        if (CurrentSpeed == SpeedMode.Paused)
            SetSpeed(SpeedMode.Play);
        else
            SetSpeed(SpeedMode.Paused);
    }

    public void RequestExternalPause(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        externalPauseSources.Add(source);
        ApplyTimeScale();
    }

    public void ReleaseExternalPause(string source)
    {
        if (string.IsNullOrEmpty(source)) return;
        externalPauseSources.Remove(source);
        ApplyTimeScale();
    }

    public bool IsExternallyPaused => externalPauseSources.Count > 0;
    public bool IsSimulationPaused => Time.timeScale <= 0f;

    public string GetClockText()
    {
        int total = Mathf.FloorToInt(CurrentMinutes);
        int hour24 = (total / 60) % 24;
        int minute = total % 60;
        bool pm = hour24 >= 12;
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        return $"{hour12}:{minute:00} {(pm ? "PM" : "AM")}";
    }

    public string GetDayText() => "Day " + CurrentDay;

    void ApplyTimeScale()
    {
        if (externalPauseSources.Count > 0 || CurrentSpeed == SpeedMode.Paused || IsShiftOver)
        {
            Time.timeScale = 0f;
            return;
        }

        if (CurrentSpeed == SpeedMode.SuperFast)
            Time.timeScale = Mathf.Max(1f, superFastTimeScale);
        else if (CurrentSpeed == SpeedMode.FastForward)
            Time.timeScale = Mathf.Max(1f, fastForwardTimeScale);
        else
            Time.timeScale = 1f;
    }

    public string GetSpeedLabel()
    {
        switch (CurrentSpeed)
        {
            case SpeedMode.Paused: return "Paused";
            case SpeedMode.Play: return "1x";
            case SpeedMode.FastForward: return $"{fastForwardTimeScale:0.##}x";
            case SpeedMode.SuperFast: return $"{superFastTimeScale:0.##}x";
            default: return CurrentSpeed.ToString();
        }
    }

    void EndShift()
    {
        if (IsShiftOver) return;
        IsShiftOver = true;
        if (pauseAtDayEnd)
            SetSpeed(SpeedMode.Paused);
        OnDayEnded?.Invoke();
        TutorialVoiceEvents.Raise(TutorialVoiceEventId.DayEnded);
    }
}
