using System.Collections.Generic;
using UnityEngine;

/// <summary>Hides all game canvases and the pointer for clean screenshots with F2.</summary>
[DisallowMultipleComponent]
public sealed class PlayerUIVisibilityToggle : MonoBehaviour
{
    const float HiddenCanvasScanInterval = 0.5f;

    static PlayerUIVisibilityToggle instance;

    readonly Dictionary<Canvas, bool> canvasEnabledStates = new Dictionary<Canvas, bool>();
    bool uiHidden;
    float nextCanvasScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        new GameObject(nameof(PlayerUIVisibilityToggle)).AddComponent<PlayerUIVisibilityToggle>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2))
            SetHidden(!uiHidden);

        if (uiHidden && Time.unscaledTime >= nextCanvasScanTime)
        {
            nextCanvasScanTime = Time.unscaledTime + HiddenCanvasScanInterval;
            HideCurrentCanvases();
        }
    }

    void SetHidden(bool hidden)
    {
        if (uiHidden == hidden) return;

        uiHidden = hidden;
        if (uiHidden)
            HideCurrentCanvases();
        else
            RestoreCanvases();

        GameCursorController.SetHiddenForPlayerUI(uiHidden);
    }

    void HideCurrentCanvases()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null) continue;

            if (!canvasEnabledStates.ContainsKey(canvas))
                canvasEnabledStates.Add(canvas, canvas.enabled);

            if (canvas.enabled)
                canvas.enabled = false;
        }
    }

    void RestoreCanvases()
    {
        foreach (KeyValuePair<Canvas, bool> entry in canvasEnabledStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        canvasEnabledStates.Clear();
    }
}
