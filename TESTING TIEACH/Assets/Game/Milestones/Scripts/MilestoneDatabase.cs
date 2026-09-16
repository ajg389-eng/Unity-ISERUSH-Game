using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ordered progression: Tutorial + six main milestones.
/// </summary>
[CreateAssetMenu(fileName = "MilestoneDatabase", menuName = "ISE/Milestone Database")]
public class MilestoneDatabase : ScriptableObject
{
    [Tooltip("Index 0 should be the Tutorial section, then six main milestones.")]
    public List<MilestoneDefinition> milestones = new List<MilestoneDefinition>();

    public MilestoneDefinition GetById(string milestoneId)
    {
        if (string.IsNullOrEmpty(milestoneId) || milestones == null) return null;
        foreach (var m in milestones)
        {
            if (m != null && m.milestoneId == milestoneId)
                return m;
        }
        return null;
    }

    public MilestoneDefinition GetAt(int index)
    {
        if (milestones == null || index < 0 || index >= milestones.Count)
            return null;
        return milestones[index];
    }

    public int IndexOf(string milestoneId)
    {
        if (milestones == null) return -1;
        for (int i = 0; i < milestones.Count; i++)
        {
            if (milestones[i] != null && milestones[i].milestoneId == milestoneId)
                return i;
        }
        return -1;
    }
}
