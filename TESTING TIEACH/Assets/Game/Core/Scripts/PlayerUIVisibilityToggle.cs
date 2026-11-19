using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Hides all game canvases and the pointer for clean screenshots with F2.</summary>
[DisallowMultipleComponent]
public sealed class PlayerUIVisibilityToggle : MonoBehaviour
{
    const float HiddenCanvasScanInterval = 0.5f;

    static PlayerUIVisibilityToggle instance;

    readonly Dictionary<Canvas, bool> canvasEnabledStates = new Dictionary<Canvas, bool>();
    readonly Dictionary<GraphicRaycaster, bool> raycasterEnabledStates = new Dictionary<GraphicRaycaster, bool>();
    bool uiHidden;
    BuildPlacer movingStationOwner;
    bool ShouldHideCanvases => uiHidden || movingStationOwner != null;
    float nextCanvasScanTime;

    internal static void SetStationMoveHidden(BuildPlacer owner, bool hidden)
    {
        if (instance == null) return;
        if (hidden ? instance.movingStationOwner == owner : instance.movingStationOwner != owner)
            return;

        instance.movingStationOwner = hidden ? owner : null;
        instance.RefreshCanvases();
    }

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

        if (ShouldHideCanvases && Time.unscaledTime >= nextCanvasScanTime)
        {
            nextCanvasScanTime = Time.unscaledTime + HiddenCanvasScanInterval;
            HideCurrentCanvases();
        }
    }

    void SetHidden(bool hidden)
    {
        if (uiHidden == hidden) return;

        uiHidden = hidden;
        RefreshCanvases();
        GameCursorController.SetHiddenForPlayerUI(uiHidden);
    }

    void RefreshCanvases()
    {
        if (ShouldHideCanvases)
            HideCurrentCanvases();
        else
            RestoreCanvases();
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

            if (canvas.TryGetComponent(out GraphicRaycaster raycaster))
            {
                if (!raycasterEnabledStates.ContainsKey(raycaster))
                    raycasterEnabledStates.Add(raycaster, raycaster.enabled);
                raycaster.enabled = false;
            }
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
        foreach (KeyValuePair<GraphicRaycaster, bool> entry in raycasterEnabledStates)
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        raycasterEnabledStates.Clear();
    }
}
