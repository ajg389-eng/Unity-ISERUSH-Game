using System;
using UnityEngine;

/// <summary>
/// Legacy serialized milestone reward data. Runtime progression ignores this type;
/// gameplay unlocks are granted by completed research projects.
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
    [Tooltip("Legacy label retained for serialized data; runtime progression ignores it.")]
    public string displayName;
}
