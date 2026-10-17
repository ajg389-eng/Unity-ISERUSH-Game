using System.Collections.Generic;
using UnityEngine;

/// <summary>Shows the roof and walls while suppressing gameplay UI during Gus cutscenes.</summary>
public static class CutscenePresentation
{
    static readonly Dictionary<Canvas, bool> CanvasStates = new Dictionary<Canvas, bool>();
    static readonly Dictionary<CameraOcclusionWall, bool> WallStates = new Dictionary<CameraOcclusionWall, bool>();
    static readonly Dictionary<SimsBuildingRoof, bool> RoofStates = new Dictionary<SimsBuildingRoof, bool>();

    public static bool IsActive { get; private set; }

    public static void Begin()
    {
        if (!IsActive)
        {
            IsActive = true;
            Canvas.willRenderCanvases += SuppressGamePresentation;
        }

        SuppressGamePresentation();
    }

    public static void End()
    {
        if (!IsActive) return;

        IsActive = false;
        Canvas.willRenderCanvases -= SuppressGamePresentation;

        foreach (KeyValuePair<Canvas, bool> entry in CanvasStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }
        CanvasStates.Clear();

        foreach (KeyValuePair<CameraOcclusionWall, bool> entry in WallStates)
        {
            if (entry.Key != null)
                entry.Key.SetCutsceneForceVisible(entry.Value);
        }
        WallStates.Clear();

        foreach (KeyValuePair<SimsBuildingRoof, bool> entry in RoofStates)
        {
            if (entry.Key != null)
                entry.Key.SetCutsceneForceVisible(entry.Value);
        }
        RoofStates.Clear();
    }

    static void SuppressGamePresentation()
    {
        if (!IsActive) return;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isRootCanvas || IsCutsceneCanvas(canvas))
                continue;

            if (!CanvasStates.ContainsKey(canvas))
                CanvasStates.Add(canvas, canvas.enabled);
            if (canvas.enabled)
                canvas.enabled = false;
        }

        CameraOcclusionWall[] walls = Object.FindObjectsByType<CameraOcclusionWall>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < walls.Length; i++)
        {
            CameraOcclusionWall wall = walls[i];
            if (wall == null) continue;

            if (!WallStates.ContainsKey(wall))
                WallStates.Add(wall, wall.CutsceneForceVisible);
            if (!wall.CutsceneForceVisible)
                wall.SetCutsceneForceVisible(true);
        }

        SimsBuildingRoof[] roofs = Object.FindObjectsByType<SimsBuildingRoof>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < roofs.Length; i++)
        {
            SimsBuildingRoof roof = roofs[i];
            if (roof == null) continue;

            if (!RoofStates.ContainsKey(roof))
                RoofStates.Add(roof, roof.CutsceneForceVisible);
            if (!roof.CutsceneForceVisible)
                roof.SetCutsceneForceVisible(true);
        }
    }

    static bool IsCutsceneCanvas(Canvas canvas)
    {
        // The dialogue canvas is created after presentation mode begins. Keep it
        // visible, along with the title controller's fade overlay for transitions.
        return canvas.GetComponent<IntroCutsceneUI>() != null
            || canvas.name == "TitleScreenFadeCanvas";
    }
}
