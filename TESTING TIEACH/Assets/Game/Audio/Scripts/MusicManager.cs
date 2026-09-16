using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays looping 2D music. Put mp3/wav/ogg files in Game/Audio/Resources/Audio/Music/
/// and they will play as a playlist. You can also assign clips in the Inspector.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    const string ResourcesFolder = "Audio/Music";

    [Header("Soundtrack")]
    [Tooltip("Optional playlist. If empty, loads every AudioClip from Resources/Audio/Music.")]
    public AudioClip[] playlist;
    [Tooltip("Legacy single-track field; added to the playlist if set.")]
    public AudioClip soundtrack;
    [Range(0f, 1f)]
    public float volume = 0.35f;
    [Tooltip("Keep playing when loading another scene")]
    public bool persistAcrossScenes = true;
    [Tooltip("Start playing as soon as the game loads")]
    public bool playOnStart = true;
    [Tooltip("Shuffle order when the playlist starts")]
    public bool shuffle = true;

    AudioSource source;
    readonly List<AudioClip> tracks = new List<AudioClip>();
    int index;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MusicManager>() != null) return;
        if (LoadAllFromResources().Length == 0) return;

        var go = new GameObject("MusicManager");
        go.AddComponent<AudioSource>();
        go.AddComponent<MusicManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (persistAcrossScenes)
            DontDestroyOnLoad(gameObject);

        source = GetComponent<AudioSource>();
        ConfigureSource();
        BuildPlaylist();

        if (playOnStart && tracks.Count > 0)
            PlayCurrent();
    }

    void Update()
    {
        if (source == null || tracks.Count == 0) return;
        if (!source.isPlaying && source.clip != null)
            Next();
    }

    void ConfigureSource()
    {
        source.playOnAwake = false;
        source.loop = false; // playlist advances track-by-track
        source.spatialBlend = 0f;
        source.volume = volume;
    }

    void OnValidate()
    {
        if (source == null)
            source = GetComponent<AudioSource>();
        if (source != null)
            source.volume = volume;
    }

    void BuildPlaylist()
    {
        tracks.Clear();

        if (playlist != null)
        {
            foreach (var clip in playlist)
            {
                if (clip != null && !tracks.Contains(clip))
                    tracks.Add(clip);
            }
        }

        if (soundtrack != null && !tracks.Contains(soundtrack))
            tracks.Add(soundtrack);

        if (tracks.Count == 0)
        {
            foreach (var clip in LoadAllFromResources())
            {
                if (clip != null && !tracks.Contains(clip))
                    tracks.Add(clip);
            }
        }

        if (shuffle && tracks.Count > 1)
            Shuffle(tracks);

        index = 0;
    }

    static AudioClip[] LoadAllFromResources()
    {
        return Resources.LoadAll<AudioClip>(ResourcesFolder);
    }

    static void Shuffle(List<AudioClip> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void PlayCurrent()
    {
        if (tracks.Count == 0 || source == null) return;
        index = ((index % tracks.Count) + tracks.Count) % tracks.Count;
        var clip = tracks[index];
        if (clip == null) return;

        soundtrack = clip;
        source.clip = clip;
        source.volume = volume;
        source.loop = tracks.Count == 1;
        source.Play();
    }

    public void Play(AudioClip clip)
    {
        if (clip == null || source == null) return;
        soundtrack = clip;
        if (!tracks.Contains(clip))
            tracks.Insert(0, clip);
        index = tracks.IndexOf(clip);
        PlayCurrent();
    }

    public void PlaySoundtrack()
    {
        BuildPlaylist();
        PlayCurrent();
    }

    public void Next()
    {
        if (tracks.Count == 0) return;
        index = (index + 1) % tracks.Count;
        PlayCurrent();
    }

    public void Stop()
    {
        if (source != null)
            source.Stop();
    }

    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (source != null)
            source.volume = volume;
    }

    public void SetMuted(bool muted)
    {
        if (source != null)
            source.mute = muted;
    }

    public bool IsPlaying => source != null && source.isPlaying;
}
