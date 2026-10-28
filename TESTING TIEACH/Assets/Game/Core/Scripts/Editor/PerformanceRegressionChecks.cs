using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PerformanceRegressionChecks
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        Material original = null;
        CameraOcclusionWall wall = null;
        try
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null, "URP Lit shader is available");
            original = new Material(shader);
            root = new GameObject("Fade Regression Wall");
            SceneManager.MoveGameObjectToScene(root, scene);
            var visible = MakeRenderer(root, original, "Visible");
            var authoredHidden = MakeRenderer(root, original, "Authored Hidden");
            authoredHidden.forceRenderingOff = true;
            var authoredDisabled = MakeRenderer(root, original, "Authored Disabled");
            authoredDisabled.enabled = false;
            wall = root.AddComponent<CameraOcclusionWall>();
            Invoke(wall, "CacheRenderers");
            Invoke(wall, "Start");

            var variants = (IList)typeof(CameraOcclusionWall)
                .GetField("opacityMaterialInstances", PrivateInstance).GetValue(wall);
            Require(variants.Count == 1, "Repeated source materials share one prepared fade variant");

            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(visible.enabled && !visible.forceRenderingOff, "Partly faded wall remains renderable");
            Require(visible.sharedMaterial != original, "Partly faded wall uses fade variant");

            Invoke(wall, "ApplyCutaway", 1f);
            Require(visible.forceRenderingOff, "Fully faded wall omits rendering");
            Require(visible.enabled, "Full fade preserves enabled state");

            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(!visible.forceRenderingOff, "Wall resumes rendering on fade-in");
            Require(authoredHidden.forceRenderingOff, "Fade-in preserves authored forceRenderingOff");
            Require(!authoredDisabled.enabled, "Fade-in preserves authored disabled state");

            Invoke(wall, "ApplyCutaway", 0f);
            Require(visible.sharedMaterial == original, "Full opacity restores original material");
            Require(!visible.forceRenderingOff && authoredHidden.forceRenderingOff,
                "Full opacity restores authored rendering flags");

            Invoke(wall, "ApplyCutaway", 1f);
            Invoke(wall, "OnDisable");
            Require(!visible.forceRenderingOff && visible.sharedMaterial == original,
                "Disabling a hidden wall restores original rendering");

            Invoke(wall, "ApplyCutaway", 1f);
            MakeRenderer(root, original, "Added Geometry");
            Invoke(wall, "OnTransformChildrenChanged");
            Invoke(wall, "ApplyCutaway", 0.5f);
            Require(!visible.forceRenderingOff && authoredHidden.forceRenderingOff,
                "Hierarchy recache preserves authored visibility after full fade");

            Debug.Log("PERFORMANCE_REGRESSION_CHECKS_PASSED: fade variants, full-hide rendering, fade-in, original flags, disable and hierarchy recache.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (wall != null)
            {
                Invoke(wall, "RestoreOriginalRendering");
                Invoke(wall, "DestroyOpacityMaterials");
            }
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (original != null) UnityEngine.Object.DestroyImmediate(original);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static Renderer MakeRenderer(GameObject root, Material original, string name)
    {
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = name;
        child.transform.SetParent(root.transform, false);
        var renderer = child.GetComponent<Renderer>();
        renderer.sharedMaterial = original;
        return renderer;
    }

    static void Invoke(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
    }

    static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
