#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places MainHudTabs under PlayerUI so you can edit its RectTransform in the scene.
/// Menu: Game / Setup Main HUD Tabs
/// </summary>
public static class MainHudTabsSetup
{
    const string MenuPath = "Game/Setup Main HUD Tabs";

    [MenuItem(MenuPath)]
    public static void Setup()
    {
        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(
                "Main HUD Tabs",
                "Could not find a Canvas named PlayerUI in the open scene.",
                "OK");
            return;
        }

        var existing = canvas.transform.Find(MainHudTabs.StripName);
        if (existing == null)
        {
            var orphan = Object.FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
            if (orphan != null)
                existing = orphan.transform;
        }

        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Main HUD Tabs",
                "MainHudTabs already exists.\n\nReplace it (resets position), or select the existing one to edit?",
                "Replace",
                "Select Existing");
            if (!replace)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            Object.DestroyImmediate(existing.gameObject);
        }

        var tabs = MainHudTabs.CreateInCanvas(canvas.transform);
        if (tabs == null)
        {
            EditorUtility.DisplayDialog("Main HUD Tabs", "Failed to create MainHudTabs.", "OK");
            return;
        }

        tabs.useSceneLayout = true;
        Selection.activeGameObject = tabs.gameObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "Main HUD Tabs",
            "Created MainHudTabs under PlayerUI.\n\n" +
            "Select it in the Hierarchy and move/resize with the RectTransform.\n" +
            "Keep \"Use Scene Layout\" enabled on MainHudTabs.\n" +
            "Save the scene when finished.",
            "OK");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupValidate() => !EditorApplication.isPlaying;
}
#endif
