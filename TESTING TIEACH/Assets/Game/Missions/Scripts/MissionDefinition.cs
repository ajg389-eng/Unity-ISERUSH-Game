using UnityEngine;

/// <summary>
/// One tutorial / mission objective shown in the left task list.
/// </summary>
[CreateAssetMenu(fileName = "MissionDefinition", menuName = "ISE/Mission Definition")]
public class MissionDefinition : ScriptableObject
{
    [Tooltip("Unique id for this mission.")]
    public string missionId;

    [Tooltip("Short label in the task list.")]
    public string title;

    [TextArea(2, 4)]
    public string description;

    [Tooltip("TutorialVoiceEventId or custom event that completes this mission.")]
    public string completionEventId;

    [Tooltip("Show in the list before it is completed.")]
    public bool showBeforeComplete = true;

    [Tooltip("Keep visible in the list after completion (shown with a checkmark).")]
    public bool showAfterComplete = true;
}
