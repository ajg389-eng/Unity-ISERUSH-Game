#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places the Tasks / Progression side menu under PlayerUI so you can edit its RectTransform in the scene.
/// Menu: Game / Setup Tasks Progression Panel
/// </summary>
public static class MissionListUISetup
{
    const string MenuPath = "Game/Setup Tasks Progression Panel";

    [MenuItem(MenuPath)]
    public static void Setup()
    {
        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(
                "Tasks / Progression Panel",
                "Could not find a Canvas named PlayerUI in the open scene.",
                "OK");
            return;
        }

        var existing = canvas.transform.Find(MissionListUI.PanelObjectName);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Tasks / Progression Panel",
                "SideMenuPanel already exists under PlayerUI.\n\nReplace it (resets position), or select the existing one to edit?",
                "Replace",
                "Select Existing");
            if (!replace)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            Object.DestroyImmediate(existing.gameObject);
        }

        // Remove runtime-only mission list canvases so the scene panel is the single source.
        foreach (var ui in Object.FindObjectsByType<MissionListUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ui == null) continue;
            if (ui.panelRoot != null && ui.panelRoot.transform.IsChildOf(canvas.transform))
                continue;
            if (ui.GetComponentInParent<Canvas>() == canvas)
                continue;
            Object.DestroyImmediate(ui.gameObject);
        }

        var host = new GameObject("MissionListUI");
        host.transform.SetParent(canvas.transform, false);
        var missionUi = host.AddComponent<MissionListUI>();
        missionUi.targetCanvas = canvas;
        missionUi.useSceneLayout = true;
        missionUi.EnsureUIPublic();
        missionUi.ApplyDefaultSceneLayout();

        if (missionUi.panelRoot != null)
            missionUi.panelRoot.transform.SetParent(canvas.transform, false);

        Selection.activeGameObject = missionUi.panelRoot != null ? missionUi.panelRoot : host;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "Tasks / Progression Panel",
            "Created SideMenuPanel under PlayerUI.\n\n" +
            "Select it in the Hierarchy and move/resize with the RectTransform.\n" +
            "Keep \"Use Scene Layout\" enabled on MissionListUI.\n" +
            "Save the scene when finished.",
            "OK");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupValidate() => !EditorApplication.isPlaying;
}
#endif
