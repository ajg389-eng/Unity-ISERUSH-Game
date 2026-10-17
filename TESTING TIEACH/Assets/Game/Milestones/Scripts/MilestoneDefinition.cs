using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One progression chapter: Tutorial or a numbered main milestone.
/// Player advances after finishing all assigned missions.
/// </summary>
[CreateAssetMenu(fileName = "MilestoneDefinition", menuName = "ISE/Milestone Definition")]
public class MilestoneDefinition : ScriptableObject
{
    [Tooltip("Unique id (e.g. tutorial, milestone_01).")]
    public string milestoneId;

    public string displayName = "Milestone";

    [TextArea(2, 5)]
    public string description;

    [Tooltip("Gus's dialogue shown in the exterior cutscene after this milestone is completed.")]
    [TextArea(2, 4)]
    public string[] completionDialogue = System.Array.Empty<string>();

    [Tooltip("True for the onboarding tutorial section (index 0).")]
    public bool isTutorial;

    [Tooltip("Missions that must all be completed before this milestone advances.")]
    public List<MissionDefinition> missions = new List<MissionDefinition>();

    [Tooltip("Optional shared mission list asset. Used when missions list is empty.")]
    public MissionDatabase missionDatabase;

    // Legacy serialized field kept to read existing project assets. Progression ignores it.
    [HideInInspector]
    public QuizDefinition quiz;

    // Retained for old serialized assets. Runtime progression ignores milestone rewards.
    [HideInInspector]
    public List<MilestoneUnlock> unlocks = new List<MilestoneUnlock>();

    public IReadOnlyList<MissionDefinition> GetMissions()
    {
        if (missions != null && missions.Count > 0)
            return missions;
        if (missionDatabase != null && missionDatabase.missions != null)
            return missionDatabase.missions;
        return System.Array.Empty<MissionDefinition>();
    }

}
