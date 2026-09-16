#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility to add or rebuild the TopHudBar under PlayerUI in the open scene.
/// Menu: Game / Setup Top HUD Bar
/// </summary>
public static class TopHudBarSetup
{
    const string MenuPath = "Game/Setup Top HUD Bar";

    [MenuItem(MenuPath)]
    public static void SetupTopHudBar()
    {
        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(
                "Top HUD Bar",
                "Could not find a Canvas named PlayerUI in the open scene.",
                "OK");
            return;
        }

        var existing = canvas.transform.Find(TopHudBar.BarObjectName);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Top HUD Bar",
                "TopHudBar already exists under PlayerUI. Replace it?",
                "Replace",
                "Cancel");
            if (!replace) return;
            Object.DestroyImmediate(existing.gameObject);
        }

        var legacy = canvas.transform.Find("GameTimeBar");
        if (legacy != null)
            Object.DestroyImmediate(legacy.gameObject);

        DisableStandaloneMoneyUi();

        var bar = TopHudBar.CreateInCanvas(canvas.transform);
        if (bar == null)
        {
            EditorUtility.DisplayDialog("Top HUD Bar", "Failed to create TopHudBar.", "OK");
            return;
        }

        var moneyManager = Object.FindFirstObjectByType<MoneyManager>();
        if (moneyManager != null)
            bar.money = moneyManager;

        bar.BindReferences();
        bar.ApplyFitLayout();
        bar.RemoveUndoFromBar();
        Selection.activeGameObject = bar.gameObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("TopHudBar created under PlayerUI. Save the scene to keep it.");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupTopHudBarValidate()
    {
        return !EditorApplication.isPlaying;
    }

    static void DisableStandaloneMoneyUi()
    {
        foreach (var moneyUi in Object.FindObjectsByType<MoneyUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (moneyUi == null || moneyUi.GetComponentInParent<TopHudBar>() != null)
                continue;

            moneyUi.gameObject.SetActive(false);
            EditorUtility.SetDirty(moneyUi.gameObject);
        }
    }
}
#endif
