using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Night glow for the placed street-light models. The poles already exist in the
/// scene, so this only adds the lens and downward light the old runtime lamps used.
/// </summary>
[DisallowMultipleComponent]
public class RoadStreetlights : MonoBehaviour
{
    readonly List<Light> lights = new List<Light>();
    Material glowMaterial;
    GameObject lampRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<RoadStreetlights>() == null)
            new GameObject("Road Streetlights").AddComponent<RoadStreetlights>();
    }

    void Start()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return;
        glowMaterial = new Material(shader);
        glowMaterial.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.65f));
        glowMaterial.EnableKeyword("_EMISSION");

        lampRoot = new GameObject("Streetlight Glow");
        lampRoot.transform.SetParent(transform, false);
        LightPlacedProps();
        Update();
    }

    void LightPlacedProps()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer.name.IndexOf("Street Light", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (renderer.GetComponentInChildren<Light>() != null) continue;
            if (!TryGetLampHead(renderer, out Vector3 head)) continue;

            float height = renderer.bounds.size.y;
            var root = new GameObject("Streetlight Glow");
            root.transform.SetParent(lampRoot.transform, false);
            root.transform.position = head;

            var lens = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lens.name = "Glowing Lens";
            lens.transform.SetParent(root.transform, false);
            lens.transform.localPosition = new Vector3(0f, -height * 0.012f, 0f);
            // Same thin housing lens the runtime lamps used, scaled to this model.
            lens.transform.localScale = new Vector3(height * 0.07f, height * 0.012f, height * 0.12f);
            Collider collider = lens.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            MeshRenderer lensRenderer = lens.GetComponent<MeshRenderer>();
            lensRenderer.sharedMaterial = glowMaterial;
            lensRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lensRenderer.receiveShadows = false;

            var bulb = new GameObject("Road Light");
            bulb.transform.SetParent(root.transform, false);
            bulb.transform.localPosition = new Vector3(0f, -height * 0.02f, 0f);
            bulb.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var light = bulb.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.88f, 0.64f);
            light.range = 14f;
            light.spotAngle = 110f;
            light.innerSpotAngle = 65f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            lights.Add(light);
        }
    }

    static bool TryGetLampHead(Renderer renderer, out Vector3 head)
    {
        head = default;
        Bounds bounds = renderer.bounds;
        if (bounds.size.y < 1.2f || bounds.size.y > 14f) return false;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;
        Vector3[] vertices = filter.sharedMesh.vertices;
        if (vertices == null || vertices.Length == 0) return false;

        Transform transform = renderer.transform;
        float maxY = float.MinValue;
        int step = Mathf.Max(1, vertices.Length / 160);
        for (int i = 0; i < vertices.Length; i += step)
            maxY = Mathf.Max(maxY, transform.TransformPoint(vertices[i]).y);

        Vector3 sum = Vector3.zero;
        int count = 0;
        float cutoff = maxY - Mathf.Max(0.12f, bounds.size.y * 0.04f);
        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 world = transform.TransformPoint(vertices[i]);
            if (world.y < cutoff) continue;
            sum += world;
            count++;
        }
        if (count == 0) return false;

        head = sum / count;
        return head.y > bounds.center.y;
    }

    void Update()
    {
        float hour = GameTimeManager.Instance != null ? GameTimeManager.Instance.CurrentMinutes / 60f : 12f;
        float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(6, 9, hour))
            * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(17, 20, hour)));
        float strength = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.8f, 0.25f, daylight));
        foreach (Light light in lights)
        {
            if (light == null) continue;
            light.enabled = strength > 0.01f;
            light.intensity = 8f * strength;
        }
        if (glowMaterial != null)
            glowMaterial.SetColor("_EmissionColor", new Color(1f, 0.88f, 0.64f) * 6f * strength);
    }

    void OnDestroy()
    {
        if (lampRoot != null) Destroy(lampRoot);
        if (glowMaterial != null) Destroy(glowMaterial);
    }
}
