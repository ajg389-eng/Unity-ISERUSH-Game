using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class CreateSfxSystem
{
    const string LibraryPath = "Assets/Resources/SfxLibrary.asset";
    const string SfxFolder = "Assets/Resources/Audio/SFX";

    [InitializeOnLoadMethod]
    static void AutoEnsureOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<SfxLibrary>(LibraryPath) != null) return;
            EnsureLibrary();
        };
    }

    [MenuItem("Tools/ISE Rush/Create Sfx Library")]
    [MenuItem("Production/Create Sfx Library")]
    public static void EnsureLibrary()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Audio"))
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");
        if (!AssetDatabase.IsValidFolder(SfxFolder))
            AssetDatabase.CreateFolder("Assets/Resources/Audio", "SFX");

        var library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<SfxLibrary>();
            library.entries = BuildDefaultEntries();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        else if (library.entries == null || library.entries.Count == 0)
        {
            library.entries = BuildDefaultEntries();
            EditorUtility.SetDirty(library);
        }

        library.RebuildLookup();
        AssetDatabase.SaveAssets();

        var readmePath = SfxFolder + "/README.txt";
        if (!System.IO.File.Exists(readmePath))
        {
            System.IO.File.WriteAllText(readmePath,
                "Drop sound files here named after SfxId (case-sensitive):\n" +
                "  UiClick.wav, BuildPlace.wav, CustomerArrive.mp3, etc.\n\n" +
                "Or assign clips on Assets/Resources/SfxLibrary in the Inspector.\n");
            AssetDatabase.Refresh();
        }

        Selection.activeObject = library;
        Debug.Log("Sfx library ready. Add clips to " + SfxFolder + " or SfxLibrary asset.");
    }

    [MenuItem("Tools/ISE Rush/Add Sfx Manager To Scene")]
    [MenuItem("Production/Add Sfx Manager To Scene")]
    public static void AddManagerToScene()
    {
        EnsureLibrary();

        var existing = Object.FindFirstObjectByType<SfxManager>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var go = new GameObject("SfxManager");
        Undo.RegisterCreatedObjectUndo(go, "Add Sfx Manager");
        var mgr = go.AddComponent<SfxManager>();
        mgr.library = AssetDatabase.LoadAssetAtPath<SfxLibrary>(LibraryPath);
        Selection.activeGameObject = go;
    }

    [MenuItem("Tools/ISE Rush/Add UiClick Sound To All Buttons")]
    [MenuItem("Production/Add UiClick Sound To All Buttons")]
    public static void AddUiClickToButtons()
    {
        int count = 0;
        foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button == null || button.GetComponent<ButtonSfx>() != null) continue;
            Undo.AddComponent<ButtonSfx>(button.gameObject);
            count++;
        }
        Debug.Log("Added ButtonSfx to " + count + " buttons.");
    }

    static System.Collections.Generic.List<SfxEntry> BuildDefaultEntries()
    {
        var list = new System.Collections.Generic.List<SfxEntry>();
        foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
        {
            list.Add(new SfxEntry { id = id, volume = 1f, pitchVariance = 0.05f });
        }
        return list;
    }
}
