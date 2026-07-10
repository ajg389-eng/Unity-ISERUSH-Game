using System;
using System.IO;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class CreateMusicManager
{
    const string ResourcesMusicFolder = "Assets/Resources/Audio/Music";
    const string PlaceholderPath = ResourcesMusicFolder + "/Soundtrack.wav";

    static CreateMusicManager()
    {
        EditorApplication.delayCall += EnsurePlaceholderOnLoad;
    }

    static void EnsurePlaceholderOnLoad()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureFolders();
        if (!File.Exists(PlaceholderPath))
        {
            EnsurePlaceholderSoundtrack();
            Debug.Log("Created placeholder soundtrack at " + PlaceholderPath + ". Replace it with your own music anytime. Music plays automatically in Play mode.");
        }
    }

    [MenuItem("GameObject/Audio/Music Manager", false, 10)]
    [MenuItem("Production/Add Music Manager To Scene")]
    public static void Create()
    {
        EnsureFolders();
        EnsurePlaceholderSoundtrack();

        var existing = UnityEngine.Object.FindFirstObjectByType<MusicManager>();
        if (existing != null)
        {
            if (existing.soundtrack == null)
            {
                Undo.RecordObject(existing, "Assign Soundtrack");
                existing.soundtrack = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderPath);
                EditorUtility.SetDirty(existing);
            }
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("MusicManager already in the scene. Soundtrack assigned if it was empty.");
            return;
        }

        var go = new GameObject("MusicManager");
        Undo.RegisterCreatedObjectUndo(go, "Create Music Manager");

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0.35f;

        var music = go.AddComponent<MusicManager>();
        music.volume = 0.35f;
        music.playOnStart = true;
        music.persistAcrossScenes = true;

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderPath);
        if (clip == null)
            clip = Resources.Load<AudioClip>("Audio/Music/Soundtrack");
        if (clip != null)
        {
            music.soundtrack = clip;
            source.clip = clip;
        }

        Selection.activeGameObject = go;
        Debug.Log(clip != null
            ? "MusicManager added with soundtrack: " + clip.name + ". Replace Assets/Resources/Audio/Music/Soundtrack.wav with your own track anytime."
            : "MusicManager added. Drop your music at Assets/Resources/Audio/Music/Soundtrack.mp3 (or .wav/.ogg).");
    }

    [MenuItem("Production/Create Placeholder Soundtrack")]
    public static void CreatePlaceholderOnly()
    {
        EnsureFolders();
        if (File.Exists(PlaceholderPath))
            File.Delete(PlaceholderPath);
        EnsurePlaceholderSoundtrack();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderPath);
        if (clip != null)
        {
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }
        Debug.Log("Placeholder soundtrack ready at " + PlaceholderPath + ". Replace it with your own music when you have a track.");
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Audio"))
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");
        if (!AssetDatabase.IsValidFolder(ResourcesMusicFolder))
            AssetDatabase.CreateFolder("Assets/Resources/Audio", "Music");
    }

    static void EnsurePlaceholderSoundtrack()
    {
        if (File.Exists(PlaceholderPath))
            return;

        WriteSoftLoopWav(PlaceholderPath, seconds: 16f, sampleRate: 22050);
        AssetDatabase.ImportAsset(PlaceholderPath);

        var importer = AssetImporter.GetAtPath(PlaceholderPath) as AudioImporter;
        if (importer != null)
        {
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.SaveAndReimport();
        }
    }

    /// <summary>Quiet looping pad: soft sine tones with a slow volume swell.</summary>
    static void WriteSoftLoopWav(string path, float seconds, int sampleRate)
    {
        int sampleCount = Mathf.RoundToInt(seconds * sampleRate);
        short[] samples = new short[sampleCount];

        const double freqA = 110.0;  // A2
        const double freqB = 164.81; // E3
        const double freqC = 220.0;  // A3
        double twoPi = Math.PI * 2.0;

        for (int i = 0; i < sampleCount; i++)
        {
            double t = i / (double)sampleRate;
            double phase = t / seconds;
            double envelope = 0.55 + 0.45 * Math.Sin(phase * twoPi);

            double wave =
                0.45 * Math.Sin(twoPi * freqA * t) +
                0.30 * Math.Sin(twoPi * freqB * t) +
                0.20 * Math.Sin(twoPi * freqC * t + 0.4);

            wave *= 0.85 + 0.15 * Math.Sin(twoPi * 0.25 * t);

            double sample = wave * envelope * 0.22;
            samples[i] = (short)Mathf.Clamp((int)(sample * short.MaxValue), short.MinValue, short.MaxValue);
        }

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            int byteRate = sampleRate * 2;
            int dataSize = samples.Length * 2;

            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            for (int i = 0; i < samples.Length; i++)
                bw.Write(samples[i]);
        }
    }
}
