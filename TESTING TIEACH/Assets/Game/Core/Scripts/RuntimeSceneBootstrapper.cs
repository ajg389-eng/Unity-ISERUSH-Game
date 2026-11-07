using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Re-runs game startup bootstraps after an in-process scene reload. Runtime
/// initialization callbacks run for the first scene only, while Quit to Menu
/// reloads that scene and destroys scene-owned runtime services.
/// </summary>
public static class RuntimeSceneBootstrapper
{
    /// <summary>Rebuild scene-owned runtime systems after Quit to Menu reloads the scene.</summary>
    public static void RebuildAfterNextSingleSceneLoad()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RebuildSceneBootstrappedSystems();
    }

    static void RebuildSceneBootstrappedSystems()
    {
        Assembly gameAssembly = typeof(RuntimeSceneBootstrapper).Assembly;
        foreach (Type type in gameAssembly.GetTypes())
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in methods)
            {
                var bootstrap = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (bootstrap == null || bootstrap.loadType != RuntimeInitializeLoadType.AfterSceneLoad
                    || method.GetParameters().Length != 0 || method.ContainsGenericParameters)
                    continue;

                try
                {
                    method.Invoke(null, null);
                }
                catch (Exception exception)
                {
                    Exception cause = exception is TargetInvocationException invocation
                        && invocation.InnerException != null
                        ? invocation.InnerException
                        : exception;
                    Debug.LogException(cause);
                }
            }
        }
    }
}
