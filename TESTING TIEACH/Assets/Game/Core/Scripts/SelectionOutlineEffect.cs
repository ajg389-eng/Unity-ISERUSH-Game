using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Soft glowing silhouette rim around selected stations/workers.
/// Uses a fresnel rim on mesh copies (baked for skinned meshes) — avoids inverted-hull
/// artifacts on flat kitchen props.
/// </summary>
[DisallowMultipleComponent]
public class SelectionOutlineEffect : MonoBehaviour
{
    static readonly List<Renderer> RendererBuffer = new List<Renderer>(16);
    static Material sharedMaterial;
    static Shader rimShader;

    [Header("Look")]
    public Color outlineColor = new Color(0.25f, 0.95f, 1.45f, 1f);
    [Range(0.5f, 8f)]
    public float rimPower = 2.4f;
    [Range(0.5f, 8f)]
    public float rimIntensity = 3.2f;
    [Tooltip("Kept for inspector compatibility with older callers.")]
    [Range(0.005f, 0.08f)]
    public float outlineWidth = 0.028f;

    readonly List<OutlineSlot> slots = new List<OutlineSlot>();
    Material runtimeMaterial;
    bool active;

    struct OutlineSlot
    {
        public SkinnedMeshRenderer sourceSkinned;
        public MeshFilter sourceFilter;
        public GameObject outlineObject;
        public MeshFilter outlineFilter;
        public MeshRenderer outlineRenderer;
        public Mesh bakedMesh;
    }

    public static SelectionOutlineEffect EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var effect = go.GetComponent<SelectionOutlineEffect>();
        if (effect == null)
            effect = go.AddComponent<SelectionOutlineEffect>();
        return effect;
    }

    public void SetActive(bool on)
    {
        if (active == on)
        {
            if (on)
            {
                ApplyMaterialSettings();
                BakeSkinnedSlots();
            }
            return;
        }

        active = on;
        if (on)
        {
            DestroySlots();
            EnsureSlots();
            ApplyMaterialSettings();
            SetOutlineVisible(true);
        }
        else
        {
            SetOutlineVisible(false);
            DestroySlots();
        }
    }

    public void SetColor(Color color)
    {
        outlineColor = color;
        ApplyMaterialSettings();
    }

    void LateUpdate()
    {
        if (!active) return;
        BakeSkinnedSlots();
    }

    void OnDisable()
    {
        if (active)
        {
            active = false;
            SetOutlineVisible(false);
        }
    }

    void OnDestroy()
    {
        DestroySlots();
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    void EnsureSlots()
    {
        if (slots.Count > 0) return;

        EnsureMaterial();
        RendererBuffer.Clear();
        GetComponentsInChildren(true, RendererBuffer);

        for (int i = 0; i < RendererBuffer.Count; i++)
        {
            var renderer = RendererBuffer[i];
            if (renderer == null || ShouldSkip(renderer)) continue;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) continue;
                if (!IsReasonableMesh(skinned.sharedMesh, skinned.bounds)) continue;
                CreateSkinnedSlot(skinned);
            }
            else if (renderer is MeshRenderer meshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (!IsReasonableMesh(filter.sharedMesh, meshRenderer.bounds)) continue;
                CreateStaticSlot(filter, meshRenderer.transform);
            }
        }
    }

    void CreateSkinnedSlot(SkinnedMeshRenderer skinned)
    {
        var slot = CreateOutlineObject(skinned.gameObject.name + "_Rim", skinned.transform);
        slot.sourceSkinned = skinned;
        slot.bakedMesh = new Mesh { name = skinned.sharedMesh.name + "_RimBake" };
        slot.bakedMesh.MarkDynamic();
        slot.outlineFilter.sharedMesh = slot.bakedMesh;
        slots.Add(slot);
    }

    void CreateStaticSlot(MeshFilter filter, Transform source)
    {
        var slot = CreateOutlineObject(filter.gameObject.name + "_Rim", source);
        slot.sourceFilter = filter;
        slot.outlineFilter.sharedMesh = filter.sharedMesh;
        slot.outlineObject.transform.SetParent(source, false);
        slot.outlineObject.transform.localPosition = Vector3.zero;
        slot.outlineObject.transform.localRotation = Quaternion.identity;
        slot.outlineObject.transform.localScale = Vector3.one;
        slots.Add(slot);
    }

    OutlineSlot CreateOutlineObject(string objectName, Transform parent)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.layer = gameObject.layer;
        go.hideFlags = HideFlags.DontSave;

        var filter = go.AddComponent<MeshFilter>();
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = runtimeMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        return new OutlineSlot
        {
            outlineObject = go,
            outlineFilter = filter,
            outlineRenderer = meshRenderer
        };
    }

    void BakeSkinnedSlots()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.sourceSkinned == null || slot.bakedMesh == null || slot.outlineFilter == null)
                continue;

            bool show = slot.sourceSkinned.enabled && slot.sourceSkinned.gameObject.activeInHierarchy;
            if (slot.outlineObject != null && slot.outlineObject.activeSelf != show)
                slot.outlineObject.SetActive(show);
            if (!show) continue;

            // Bake without scale — parent transform already carries character scale.
            slot.sourceSkinned.BakeMesh(slot.bakedMesh, false);
            // Do not reassign sharedMesh every frame (causes flicker).

            var t = slot.outlineObject.transform;
            t.SetParent(slot.sourceSkinned.transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }
    }

    void SetOutlineVisible(bool visible)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].outlineObject != null)
                slots[i].outlineObject.SetActive(visible);
        }

        if (visible)
            BakeSkinnedSlots();
    }

    void DestroySlots()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].bakedMesh != null)
                Destroy(slots[i].bakedMesh);
            if (slots[i].outlineObject != null)
                Destroy(slots[i].outlineObject);
        }
        slots.Clear();
    }

    void EnsureMaterial()
    {
        if (runtimeMaterial != null) return;

        if (rimShader == null)
            rimShader = Shader.Find("TIEACH/SelectionRimGlow");

        if (rimShader == null)
        {
            Debug.LogWarning("SelectionOutlineEffect: missing shader TIEACH/SelectionRimGlow", this);
            return;
        }

        if (sharedMaterial == null)
            sharedMaterial = new Material(rimShader) { name = "SelectionRim_Shared" };

        runtimeMaterial = new Material(sharedMaterial)
        {
            name = "SelectionRim_" + gameObject.name
        };
        ApplyMaterialSettings();
    }

    void ApplyMaterialSettings()
    {
        if (runtimeMaterial == null) return;
        runtimeMaterial.SetColor("_OutlineColor", outlineColor);
        runtimeMaterial.SetFloat("_RimPower", rimPower);
        runtimeMaterial.SetFloat("_RimIntensity", rimIntensity);
        // Map legacy width to a little fill so thicker settings still read
        runtimeMaterial.SetFloat("_Fill", Mathf.Lerp(0.0f, 0.04f, Mathf.InverseLerp(0.005f, 0.08f, outlineWidth)));

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].outlineRenderer != null)
                slots[i].outlineRenderer.sharedMaterial = runtimeMaterial;
        }
    }

    static bool IsReasonableMesh(Mesh mesh, Bounds worldBounds)
    {
        if (mesh == null) return false;
        if (mesh.vertexCount < 3) return false;

        // Skip huge/degenerate volumes (floor pieces, giant collision proxies).
        Vector3 size = worldBounds.size;
        float maxDim = Mathf.Max(size.x, size.y, size.z);
        if (maxDim > 8f) return false;

        // Skip extremely flat giant panels that only produce solid slabs.
        float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
        if (maxDim > 1.5f && minDim < 0.02f) return false;

        return true;
    }

    static bool ShouldSkip(Renderer renderer)
    {
        if (renderer == null) return true;
        if (!renderer.enabled) return true;
        if (renderer is LineRenderer) return true;
        if (renderer is ParticleSystemRenderer) return true;
        if (renderer.GetComponentInParent<Canvas>() != null) return true;

        string n = renderer.gameObject.name;
        if (n.Contains("TaskBar") || n.Contains("Label") || n.Contains("Highlight")
            || n.Contains("Outline") || n.Contains("_Rim") || n.Contains("Grid"))
            return true;

        if (renderer.GetComponentInParent<StationInteractionTiles>() != null
            && n.IndexOf("Interaction", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }
}
