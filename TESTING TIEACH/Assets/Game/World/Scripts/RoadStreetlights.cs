using System.Collections.Generic;
using UnityEngine;

/// <summary>Roadside lamps driven by the same game clock as vehicle headlights.</summary>
[DisallowMultipleComponent]
public class RoadStreetlights : MonoBehaviour
{
    readonly List<Light> lights = new List<Light>();
    Material poleMaterial, glowMaterial;
    GameObject lampRoot;

    void Start()
    {
        if (!RoadTrafficController.TryGetHighway(out float west, out float east, out float z, out float y)) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return;
        poleMaterial = new Material(shader);
        poleMaterial.SetColor("_BaseColor", new Color(0.15f, 0.17f, 0.19f));
        glowMaterial = new Material(shader);
        glowMaterial.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.65f));
        glowMaterial.EnableKeyword("_EMISSION");
        float edgeZ = z + 6f;
        GameObject roads = GameObject.Find("Roads");
        if (roads != null)
        {
            foreach (Renderer r in roads.GetComponentsInChildren<Renderer>())
                if (r.name.IndexOf("Lane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    edgeZ = Mathf.Max(edgeZ, r.bounds.max.z + 0.8f);
        }
        lampRoot = new GameObject("Road Streetlights");
        lampRoot.transform.SetParent(transform, false);
        // Leave room at both tunnels; use the far shoulder to avoid the delivery driveway.
        float start = west + 12f, end = east - 12f;
        float spacing = 2f * Mathf.Max(18f, (end - start) / 31f);
        for (float x = start; x <= end; x += spacing)
            CreateLamp(new Vector3(x, y, edgeZ));
        Update();
    }

    void CreateLamp(Vector3 position)
    {
        var root = new GameObject("Streetlight");
        root.transform.SetParent(lampRoot.transform, false);
        root.transform.position = position;
        Part(root.transform, "Base", new Vector3(0,0.15f,0), new Vector3(0.65f,0.3f,0.65f), poleMaterial);
        Part(root.transform, "Pole", new Vector3(0,3.5f,0), new Vector3(0.2f,7f,0.2f), poleMaterial);
        Part(root.transform, "Arm", new Vector3(0,6.9f,-0.9f), new Vector3(0.2f,0.18f,2f), poleMaterial);
        Part(root.transform, "Housing", new Vector3(0,6.85f,-1.85f), new Vector3(0.65f,0.25f,1.05f), poleMaterial);
        Part(root.transform, "Glowing Lens", new Vector3(0,6.70f,-1.85f), new Vector3(0.52f,0.08f,0.9f), glowMaterial);
        var bulb = new GameObject("Road Light");
        bulb.transform.SetParent(root.transform, false);
        bulb.transform.localPosition = new Vector3(0,6.6f,-1.85f);
        bulb.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        var light = bulb.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = new Color(1f,0.88f,0.64f);
        light.range = 14f;
        light.spotAngle = 110f;
        light.innerSpotAngle = 65f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        lights.Add(light);
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    void Update()
    {
        float hour = GameTimeManager.Instance != null ? GameTimeManager.Instance.CurrentMinutes / 60f : 12f;
        float daylight = Mathf.SmoothStep(0,1,Mathf.InverseLerp(6,9,hour))
            * (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(17,20,hour)));
        float strength = Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.8f,0.25f,daylight));
        foreach (Light light in lights)
        {
            light.enabled = strength > 0.01f;
            light.intensity = 8f * strength;
        }
        if (glowMaterial != null)
            glowMaterial.SetColor("_EmissionColor", new Color(1f,0.88f,0.64f) * 6f * strength);
    }

    void OnDestroy()
    {
        if (lampRoot != null) Destroy(lampRoot);
        if (poleMaterial != null) Destroy(poleMaterial);
        if (glowMaterial != null) Destroy(glowMaterial);
    }
}
