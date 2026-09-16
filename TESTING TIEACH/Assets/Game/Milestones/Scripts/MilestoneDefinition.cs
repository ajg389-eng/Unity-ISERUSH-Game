using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One progression chapter: Tutorial or a numbered main milestone.
/// Player must finish all assigned missions, then pass the quiz to advance.
/// </summary>
[CreateAssetMenu(fileName = "MilestoneDefinition", menuName = "ISE/Milestone Definition")]
public class MilestoneDefinition : ScriptableObject
{
    [Tooltip("Unique id (e.g. tutorial, milestone_01).")]
    public string milestoneId;

    public string displayName = "Milestone";

    [TextArea(2, 5)]
    public string description;

    [Tooltip("True for the onboarding tutorial section (index 0).")]
    public bool isTutorial;

    [Tooltip("Missions that must all be completed before the quiz unlocks.")]
    public List<MissionDefinition> missions = new List<MissionDefinition>();

    [Tooltip("Optional shared mission list asset. Used when missions list is empty.")]
    public MissionDatabase missionDatabase;

    [Tooltip("Quiz required after missions. Leave empty for a stub pass-through.")]
    public QuizDefinition quiz;

    [Tooltip("Features unlocked when this milestone's quiz is passed.")]
    public List<MilestoneUnlock> unlocks = new List<MilestoneUnlock>();

    public IReadOnlyList<MissionDefinition> GetMissions()
    {
        if (missions != null && missions.Count > 0)
            return missions;
        if (missionDatabase != null && missionDatabase.missions != null)
            return missionDatabase.missions;
        return System.Array.Empty<MissionDefinition>();
    }

    public bool HasQuiz => quiz != null && quiz.questions != null && quiz.questions.Count > 0;
}
