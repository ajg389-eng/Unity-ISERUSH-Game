using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SfxEntry
{
    public SfxId id;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 0.35f)] public float pitchVariance = 0.05f;
}

/// <summary>
/// Maps SfxId to clips. Create via Tools → ISE Rush → Create Sfx Library.
/// </summary>
[CreateAssetMenu(fileName = "SfxLibrary", menuName = "ISE/Sfx Library")]
public class SfxLibrary : ScriptableObject
{
    public List<SfxEntry> entries = new List<SfxEntry>();

    readonly Dictionary<SfxId, SfxEntry> lookup = new Dictionary<SfxId, SfxEntry>();

    public bool TryGet(SfxId id, out SfxEntry entry)
    {
        if (lookup.Count == 0)
            RebuildLookup();
        return lookup.TryGetValue(id, out entry) && entry != null;
    }

    public void RebuildLookup()
    {
        lookup.Clear();
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e == null) continue;
            lookup[e.id] = e;
        }
    }

    void OnValidate() => RebuildLookup();
}
