using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps game event ids to tutorial voice lines.
/// </summary>
[CreateAssetMenu(fileName = "TutorialVoiceDatabase", menuName = "ISE/Tutorial Voice Database")]
public class TutorialVoiceDatabase : ScriptableObject
{
    public List<TutorialVoiceLine> lines = new List<TutorialVoiceLine>();

    readonly Dictionary<string, TutorialVoiceLine> lookup = new Dictionary<string, TutorialVoiceLine>();

    public bool TryGetLine(string eventId, out TutorialVoiceLine line)
    {
        line = null;
        if (string.IsNullOrEmpty(eventId)) return false;

        if (lookup.Count == 0)
            RebuildLookup();

        return lookup.TryGetValue(eventId, out line) && line != null;
    }

    public void RebuildLookup()
    {
        lookup.Clear();
        if (lines == null) return;

        foreach (var entry in lines)
        {
            if (entry == null || string.IsNullOrEmpty(entry.eventId)) continue;
            lookup[entry.eventId] = entry;
        }
    }

    void OnValidate()
    {
        RebuildLookup();
    }
}
