#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WorkflowDecisionHudSetup
{
    const string ScenePath = "Assets/Scenes/ISE_RUSH.unity";
    const string HudName = "WorkflowDecisionHUD";

    [InitializeOnLoadMethod]
    static void InstallIntoOpenScene()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += TryInstallIntoActiveScene;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        TryInstallIntoScene(scene);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryInstallIntoActiveScene;
    }

    static void TryInstallIntoActiveScene()
    {
        TryInstallIntoScene(SceneManager.GetActiveScene());
    }

    static void TryInstallIntoScene(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || scene.path != ScenePath)
            return;

        GameObject playerUi = GameObject.Find("PlayerUI");
        if (playerUi == null || playerUi.transform.Find(HudName) != null)
            return;

        CreateOrUpdate(registerUndo: false);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Game/Setup Workflow Decision HUD")]
    public static void CreateFromMenu()
    {
        CreateOrUpdate(registerUndo: true);
        Selection.activeGameObject = GameObject.Find(HudName);
    }

    public static void CreateForBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CreateOrUpdate(registerUndo: false);
        EditorSceneManager.SaveScene(scene);
    }

    static void CreateOrUpdate(bool registerUndo)
    {
        GameObject playerUi = GameObject.Find("PlayerUI");
        if (playerUi == null || playerUi.GetComponent<Canvas>() == null)
        {
            Debug.LogError("Workflow HUD setup: PlayerUI canvas was not found.");
            return;
        }

        Transform existing = playerUi.transform.Find(HudName);
        GameObject panel = existing != null ? existing.gameObject : CreateUiObject(HudName, playerUi.transform, registerUndo);
        panel.layer = playerUi.layer;

        RectTransform panelRect = EnsureRect(panel);
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-24f, 24f);
        panelRect.sizeDelta = new Vector2(380f, 154f);

        Image panelImage = EnsureComponent<Image>(panel);
        panelImage.color = new Color(0.08f, 0.1f, 0.14f, 0.88f);
        panelImage.raycastTarget = false;

        TextMeshProUGUI title = EnsureText(panel.transform, "Title", registerUndo);
        ConfigureStretch(title.rectTransform, new Vector2(12f, -40f), new Vector2(-12f, -8f), topAnchored: true);
        title.text = "Workflow";
        title.fontSize = 18f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject section = EnsureChild(panel.transform, "AnalysisHeader", registerUndo);
        RectTransform sectionRect = EnsureRect(section);
        sectionRect.anchorMin = new Vector2(0f, 1f);
        sectionRect.anchorMax = new Vector2(1f, 1f);
        sectionRect.pivot = new Vector2(0.5f, 1f);
        sectionRect.anchoredPosition = new Vector2(0f, -42f);
        sectionRect.sizeDelta = new Vector2(-24f, 30f);
        Image sectionImage = EnsureComponent<Image>(section);
        sectionImage.color = new Color(0.28f, 0.36f, 0.48f, 1f);
        sectionImage.raycastTarget = false;

        TextMeshProUGUI sectionLabel = EnsureText(section.transform, "Label", registerUndo);
        Stretch(sectionLabel.rectTransform, 8f, 8f, 2f, 2f);
        sectionLabel.text = "Assignment Analysis";
        sectionLabel.fontSize = 14f;
        sectionLabel.color = Color.white;
        sectionLabel.alignment = TextAlignmentOptions.Center;

        TextMeshProUGUI body = EnsureText(panel.transform, "Text", registerUndo);
        body.rectTransform.anchorMin = Vector2.zero;
        body.rectTransform.anchorMax = Vector2.one;
        body.rectTransform.offsetMin = new Vector2(12f, 10f);
        body.rectTransform.offsetMax = new Vector2(-12f, -78f);
        body.text = "Select a worker to inspect their route, workload, and assignment options.";
        body.fontSize = 14f;
        body.color = new Color(0.78f, 0.82f, 0.88f, 1f);
        body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.raycastTarget = false;

        SetLayerRecursively(panel.transform, playerUi.layer);
        panel.SetActive(true);
        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static GameObject EnsureChild(Transform parent, string name, bool registerUndo)
    {
        Transform found = parent.Find(name);
        return found != null ? found.gameObject : CreateUiObject(name, parent, registerUndo);
    }

    static TextMeshProUGUI EnsureText(Transform parent, string name, bool registerUndo)
    {
        GameObject go = EnsureChild(parent, name, registerUndo);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (text == null) text = go.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        return text;
    }

    static GameObject CreateUiObject(string name, Transform parent, bool registerUndo)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        if (registerUndo) Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        return go;
    }

    static RectTransform EnsureRect(GameObject go)
    {
        return go.GetComponent<RectTransform>();
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    static void ConfigureStretch(RectTransform rect, Vector2 min, Vector2 max, bool topAnchored)
    {
        rect.anchorMin = topAnchored ? new Vector2(0f, 1f) : Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
#endif
