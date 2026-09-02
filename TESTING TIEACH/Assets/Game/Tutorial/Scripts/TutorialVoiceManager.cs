using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays tutorial voice lines in response to TutorialVoiceEvents.
/// Shows a subtitle bubble while audio (or timed text) is active.
/// Add to the scene once and assign a TutorialVoiceDatabase.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TutorialVoiceManager : MonoBehaviour
{
    public static TutorialVoiceManager Instance { get; private set; }

    [Header("Content")]
    public TutorialVoiceDatabase database;

    [Header("Components")]
    [Tooltip("Subtitle bubble UI. Auto-created on child object 'TutorialVoiceUI' if empty.")]
    public TutorialVoiceUI voiceUI;

    [Header("Playback")]
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Tooltip("Minimum gap between back-to-back lines.")]
    public float lineGapSeconds = 0.35f;

    AudioSource voiceSource;
    readonly Queue<TutorialVoiceLine> pendingLines = new Queue<TutorialVoiceLine>();
    readonly HashSet<string> playedOnceIds = new HashSet<string>();
    Coroutine playbackRoutine;
    bool isPlaying;

    public bool IsPlaying => isPlaying;
    public bool IsBubbleVisible => voiceUI != null && voiceUI.IsVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<TutorialVoiceManager>() != null) return;

        var go = new GameObject("TutorialVoiceSystem");
        go.AddComponent<AudioSource>();
        go.AddComponent<TutorialVoiceManager>();
    }

    void Reset()
    {
        EnsureVoiceUI();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        voiceSource = GetComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 0f;

        EnsureVoiceUI();

        if (database == null)
            database = Resources.Load<TutorialVoiceDatabase>("TutorialVoiceDatabase");

        if (database != null)
            database.RebuildLookup();
        else
            Debug.LogWarning("TutorialVoiceManager: No TutorialVoiceDatabase found. Assign Assets/Game/Tutorial/Resources/TutorialVoiceDatabase.");
    }

    void EnsureVoiceUI()
    {
        if (voiceUI != null) return;

        var child = transform.Find("TutorialVoiceUI");
        if (child != null)
            voiceUI = child.GetComponent<TutorialVoiceUI>();

        if (voiceUI == null)
        {
            var uiGo = new GameObject("TutorialVoiceUI");
            uiGo.transform.SetParent(transform, false);
            voiceUI = uiGo.AddComponent<TutorialVoiceUI>();
        }
    }

    void OnEnable()
    {
        TutorialVoiceEvents.OnEvent += HandleEvent;
    }

    void OnDisable()
    {
        TutorialVoiceEvents.OnEvent -= HandleEvent;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Convenience wrapper for TutorialVoiceEvents.Raise.</summary>
    public static void RaiseEvent(string eventId)
    {
        TutorialVoiceEvents.Raise(eventId);
    }

    void HandleEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;

        if (database == null)
        {
            Debug.LogWarning($"TutorialVoice: event '{eventId}' fired but no database is assigned.");
            return;
        }

        if (!database.TryGetLine(eventId, out var line) || line == null)
        {
            Debug.LogWarning($"TutorialVoice: no line for event '{eventId}'. Add it to TutorialVoiceDatabase.");
            return;
        }

        if (line.playOnce && playedOnceIds.Contains(eventId))
            return;

        if (line.playOnce)
            playedOnceIds.Add(eventId);

        pendingLines.Enqueue(line);
        if (!isPlaying)
            playbackRoutine = StartCoroutine(PlayQueueRoutine());
    }

    IEnumerator PlayQueueRoutine()
    {
        isPlaying = true;

        while (pendingLines.Count > 0)
        {
            var line = pendingLines.Dequeue();
            yield return PlayLineRoutine(line);

            if (pendingLines.Count > 0 && lineGapSeconds > 0f)
                yield return new WaitForSeconds(lineGapSeconds);
        }

        isPlaying = false;
        playbackRoutine = null;
    }

    IEnumerator PlayLineRoutine(TutorialVoiceLine line)
    {
        if (line == null) yield break;

        string text = string.IsNullOrEmpty(line.subtitleText) ? "" : line.subtitleText;
        if (voiceUI != null)
            voiceUI.Show(text);

        float wait = Mathf.Max(0.1f, line.displayDuration);

        if (line.voiceClip != null)
        {
            voiceSource.Stop();
            voiceSource.clip = line.voiceClip;
            voiceSource.volume = voiceVolume;
            voiceSource.Play();
            wait = line.voiceClip.length + line.postClipPadding;
        }

        yield return new WaitForSeconds(wait);

        if (voiceUI != null)
            voiceUI.Hide();
    }

    /// <summary>Skip the current line and hide the bubble.</summary>
    public void SkipCurrent()
    {
        if (playbackRoutine != null)
            StopCoroutine(playbackRoutine);

        playbackRoutine = null;
        pendingLines.Clear();
        isPlaying = false;

        if (voiceSource != null && voiceSource.isPlaying)
            voiceSource.Stop();

        if (voiceUI != null)
            voiceUI.Hide();
    }

    /// <summary>Clear play-once memory so lines can replay (e.g. new game).</summary>
    public void ResetPlayedLines()
    {
        playedOnceIds.Clear();
    }
}
