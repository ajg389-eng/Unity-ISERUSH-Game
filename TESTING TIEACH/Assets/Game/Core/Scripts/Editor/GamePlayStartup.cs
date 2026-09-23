#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

// Always start gameplay in the restaurant, even if the editor reopened Untitled.
// playModeStartScene leaves the user's edit-time scene intact on exiting Play.
[InitializeOnLoad]
public static class GamePlayStartup
{
    const string GameScenePath = "Assets/Scenes/ISE_RUSH.unity";

    static GamePlayStartup()
    {
        EditorApplication.delayCall += ConfigureStartupScene;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            ConfigureStartupScene();
    }

    static void ConfigureStartupScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath);
        if (scene != null)
            EditorSceneManager.playModeStartScene = scene;
    }
}
#endif
