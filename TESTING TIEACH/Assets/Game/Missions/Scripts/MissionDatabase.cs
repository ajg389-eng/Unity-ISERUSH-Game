using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ordered list of missions for the left-side task panel.
/// </summary>
[CreateAssetMenu(fileName = "MissionDatabase", menuName = "ISE/Mission Database")]
public class MissionDatabase : ScriptableObject
{
    public List<MissionDefinition> missions = new List<MissionDefinition>();
}
