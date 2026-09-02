#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility to add or rebuild StationManagePopup under PlayerUI in the open scene.
/// Menu: Game / Setup Station Manage Popup
/// </summary>
public static class StationManagePopupSetup
{
    const string MenuPath = "Game/Setup Station Manage Popup";

    [MenuItem(MenuPath)]
    public static void SetupStationManagePopup()
    {
        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog(
                "Station Manage Popup",
                "Could not find a Canvas named PlayerUI in the open scene.",
                "OK");
            return;
        }

        var existing = canvas.transform.Find(StationManagePopup.PopupObjectName);
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Station Manage Popup",
                "StationManagePopup already exists under PlayerUI. Replace it?",
                "Replace",
                "Cancel");
            if (!replace) return;
            Object.DestroyImmediate(existing.gameObject);
        }

        var popup = StationManagePopup.CreateInCanvas(canvas.transform);
        if (popup == null)
        {
            EditorUtility.DisplayDialog("Station Manage Popup", "Failed to create StationManagePopup.", "OK");
            return;
        }

        WireManagementModeController(popup);
        popup.BindReferences();
        popup.ApplyLayout();

        Selection.activeGameObject = popup.gameObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("StationManagePopup created under PlayerUI. Save the scene to keep it.");
    }

    [MenuItem(MenuPath, true)]
    static bool SetupStationManagePopupValidate()
    {
        return !EditorApplication.isPlaying;
    }

    static void WireManagementModeController(StationManagePopup popup)
    {
        if (popup == null) return;

        var controller = Object.FindFirstObjectByType<ManagementModeController>();
        if (controller == null) return;

        popup.CopyReferencesTo(controller);
        EditorUtility.SetDirty(controller);
    }
}
#endif
