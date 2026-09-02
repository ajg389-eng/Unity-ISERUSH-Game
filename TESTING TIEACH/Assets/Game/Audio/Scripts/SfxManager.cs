using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global sound effects. Play anywhere with Sfx.Play(SfxId.UiClick).
/// Clips come from SfxLibrary and/or Game/Audio/Resources/Audio/SFX/{SfxId}.wav
/// </summary>
public class SfxManager : MonoBehaviour
{
    public static SfxManager Instance { get; private set; }

    const string ResourcesFolder = "Audio/SFX";

    public SfxLibrary library;
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("How many sounds can overlap at once.")]
    public int poolSize = 8;

    readonly List<AudioSource> pool = new List<AudioSource>();
    readonly Dictionary<SfxId, AudioClip> resourceCache = new Dictionary<SfxId, AudioClip>();
    int poolIndex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<SfxManager>() != null) return;
        var go = new GameObject("SfxManager");
        go.AddComponent<SfxManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (library == null)
            library = Resources.Load<SfxLibrary>("SfxLibrary");

        if (library != null)
            library.RebuildLookup();

        EnsurePool();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void EnsurePool()
    {
        while (pool.Count < Mathf.Max(1, poolSize))
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            pool.Add(src);
        }
    }

    /// <summary>Play a sound effect. Safe to call even if SfxManager is missing.</summary>
    public static void Play(SfxId id)
    {
        if (Instance == null) return;
        Instance.PlayInternal(id);
    }

    public void PlayInternal(SfxId id)
    {
        if (!TryResolveClip(id, out AudioClip clip, out float volume, out float pitchVariance))
            return;

        if (pool.Count == 0) EnsurePool();
        var source = pool[poolIndex];
        poolIndex = (poolIndex + 1) % pool.Count;

        source.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        source.PlayOneShot(clip, volume * masterVolume);
    }

    bool TryResolveClip(SfxId id, out AudioClip clip, out float volume, out float pitchVariance)
    {
        clip = null;
        volume = 1f;
        pitchVariance = 0.05f;

        if (library != null && library.TryGet(id, out SfxEntry entry) && entry.clip != null)
        {
            clip = entry.clip;
            volume = entry.volume;
            pitchVariance = entry.pitchVariance;
            return true;
        }

        if (!resourceCache.TryGetValue(id, out clip))
        {
            clip = Resources.Load<AudioClip>($"{ResourcesFolder}/{id}");
            resourceCache[id] = clip;
        }

        return clip != null;
    }
}

/// <summary>Shorthand for SfxManager.Play.</summary>
public static class Sfx
{
    public static void Play(SfxId id) => SfxManager.Play(id);
}
