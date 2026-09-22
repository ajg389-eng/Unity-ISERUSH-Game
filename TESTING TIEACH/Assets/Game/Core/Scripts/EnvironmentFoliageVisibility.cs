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
        QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 180f);
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
            terrain.treeBillboardDistance = 5000f;
            terrain.treeCrossFadeLength = 0f;
            terrain.treeMaximumFullLODCount = 400;
            terrain.treeDistance = 5000f;
            terrain.detailObjectDistance = Mathf.Max(terrain.detailObjectDistance, 200f);
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
            renderer.shadowCastingMode = ShadowCastingMode.On;
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
