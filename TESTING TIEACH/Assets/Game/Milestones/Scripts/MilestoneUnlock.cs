using System;
using UnityEngine;

/// <summary>
/// Feature unlocked when a milestone quiz is passed.
/// Frame only — gameplay systems can later check MilestoneProgressManager.IsFeatureUnlocked.
/// </summary>
public enum MilestoneUnlockKind
{
    Station = 0,
    Recipe = 1,
    Expansion = 2,
    Feature = 3,
    Other = 4
}

[Serializable]
public class MilestoneUnlock
{
    public MilestoneUnlockKind kind = MilestoneUnlockKind.Feature;
    [Tooltip("Stable id used by gameplay gates (e.g. fryer_station, drink_recipe).")]
    public string unlockId;
    [Tooltip("Player-facing label shown on the milestone / unlock toast.")]
    public string displayName;
}
