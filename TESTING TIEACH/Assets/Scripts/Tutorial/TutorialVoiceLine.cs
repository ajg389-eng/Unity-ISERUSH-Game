using UnityEngine;

/// <summary>
/// One spoken tutorial line triggered by a game event id.
/// Create assets via Assets → Create → ISE → Tutorial Voice Line.
/// </summary>
[CreateAssetMenu(fileName = "TutorialVoiceLine", menuName = "ISE/Tutorial Voice Line")]
public class TutorialVoiceLine : ScriptableObject
{
    [Tooltip("Event id that triggers this line (e.g. shift_started).")]
    public string eventId;

    [TextArea(2, 6)]
    [Tooltip("Subtitle shown in the on-screen bubble while this line plays.")]
    public string subtitleText;

    [Tooltip("Optional voice-over clip. If empty, subtitle stays for Display Duration.")]
    public AudioClip voiceClip;

    [Tooltip("Only play the first time this event id is raised.")]
    public bool playOnce = true;

    [Tooltip("Seconds to show the bubble when Voice Clip is not assigned.")]
    public float displayDuration = 4f;

    [Tooltip("Extra seconds to keep the bubble visible after the clip ends.")]
    public float postClipPadding = 0.25f;
}
