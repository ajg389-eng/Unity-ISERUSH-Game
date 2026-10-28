using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps city trees/bushes fully visible and lit at play-camera distances.
/// Distant foliage was going dark because of short shadow range, terrain
/// billboard swap, and self-shadowing on the SimplePoly nature meshes.
/// Does not change camera clip distance.
/// </summary>
public class EnvironmentFoliageVisibility : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<EnvironmentFoliageVisibility>() != null) return;
        var go = new GameObject("EnvironmentFoliageVisibility");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<EnvironmentFoliageVisibility>();
    }

    void Start()
    {
        ApplyTerrainTreeDistances();
        BrightenFoliageRenderers();
    }

    static void ApplyTerrainTreeDistances()
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null) return;
            terrain.treeBillboardDistance = 100f;
            terrain.treeCrossFadeLength = 10f;
            terrain.treeMaximumFullLODCount = 100;
            terrain.treeDistance = 1000f;
            terrain.detailObjectDistance = 80f;
        }
    }

    static void BrightenFoliageRenderers()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsFoliage(renderer)) continue;

            renderer.receiveShadows = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }
    }

    static bool IsFoliage(Renderer renderer)
    {
        Transform t = renderer.transform;
        while (t != null)
        {
            if (NameLooksLikeFoliage(t.name))
                return true;
            t = t.parent;
        }

        Material[] materials = renderer.sharedMaterials;
        if (materials == null) return false;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && material.name.IndexOf("Nature", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static bool NameLooksLikeFoliage(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.IndexOf("Street", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return name.IndexOf("Tree", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Bush", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Natures_", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Fir", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
