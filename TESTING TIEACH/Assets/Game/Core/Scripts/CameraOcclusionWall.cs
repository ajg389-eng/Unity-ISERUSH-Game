using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a wall section that uses a Sims-style cutaway when it is between the
/// camera and the store. The wall transform and colliders never move.
/// </summary>
public class CameraOcclusionWall : MonoBehaviour
{
    [Tooltip("Direction pointing outside the kitchen (away from store center). Auto-filled if left zero.")]
    public Vector3 outwardNormal = Vector3.zero;

    // Retained so existing scene data remains compatible with older versions.
    [HideInInspector] public float lowerDistance = 4.5f;
    [HideInInspector] public bool fadeRenderers = true;
    [HideInInspector] public float occludedAlpha = 0.18f;

    [Tooltip("Visible wall height, in world units, when this wall is fully cut away.")]
    [Min(0.2f)] public float cutawayHeight = 0.8f;

    float cutawayAmount;
    float targetCutaway;
    Renderer[] renderers;
    Material[][] originalMaterials;
    Material[][] cutawayMaterials;
    bool[] originalRendererEnabled;
    bool[] brickRenderers;
    readonly List<Material> cutawayMaterialInstances = new List<Material>();
    readonly List<CutawayCap> cutawayCaps = new List<CutawayCap>();
    bool usingCutawayMaterials;
    float visualBottom;
    float visualTop;
    static Material brickCutawayTemplate;
    Material capMaterial;

    sealed class CutawayCap
    {
        public Renderer source;
        public GameObject cap;
    }

    public float DuckAmount => cutawayAmount;

    void Awake()
    {
        CacheRenderers();
    }

    void OnEnable()
    {
        CameraWallCutaway.Register(this);
    }

    void OnDisable()
    {
        CameraWallCutaway.Unregister(this);
        targetCutaway = 0f;
        cutawayAmount = 0f;
        ApplyCutaway(0f);
    }

    void OnDestroy()
    {
        RestoreOriginalRendering();
        DestroyCutawayCaps();
        DestroyCutawayMaterials();
    }

    public void SnapUp()
    {
        targetCutaway = 0f;
        cutawayAmount = 0f;
        ApplyCutaway(0f);
    }

    // Kept for callers created before cutaway rendering replaced wall movement.
    public void CaptureRestPose() { }

    public void SetOutwardFromKitchen(Vector3 kitchenFocus)
    {
        Vector3 n = transform.position - kitchenFocus;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            n = transform.forward;
        outwardNormal = n.normalized;
    }

    public void SetOutward(Vector3 worldNormal)
    {
        worldNormal.y = 0f;
        if (worldNormal.sqrMagnitude < 0.0001f) return;
        outwardNormal = worldNormal.normalized;
    }

    public void SetDuckTarget(float target01)
    {
        targetCutaway = Mathf.Clamp01(target01);
    }

    void LateUpdate()
    {
        float speed = CameraWallCutaway.Instance != null
            ? CameraWallCutaway.Instance.duckSpeed
            : 8f;
        cutawayAmount = Mathf.MoveTowards(
            cutawayAmount, targetCutaway, speed * Time.unscaledDeltaTime);
        ApplyCutaway(cutawayAmount);
    }

    void ApplyCutaway(float amount)
    {
        if (amount <= 0.001f)
        {
            RestoreOriginalRendering();
            return;
        }

        EnsureRendererCacheCurrent();
        EnsureCutawayMaterials();
        if (renderers == null || cutawayMaterials == null) return;

        if (!usingCutawayMaterials)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].sharedMaterials = cutawayMaterials[i];
            }
            usingCutawayMaterials = true;
        }

        float shortTop = Mathf.Min(visualTop, visualBottom + Mathf.Max(0.2f, cutawayHeight));
        float currentTop = Mathf.Lerp(visualTop + 0.02f, shortTop, amount);
        for (int i = 0; i < cutawayMaterialInstances.Count; i++)
        {
            Material material = cutawayMaterialInstances[i];
            if (material != null && material.HasProperty("_CutHeight"))
                material.SetFloat("_CutHeight", currentTop);
        }

        UpdateCutawayCaps(currentTop);

        // Windows and other non-brick inserts should disappear instead of floating
        // above the remaining low wall.
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = brickRenderers[i]
                ? originalRendererEnabled[i]
                : originalRendererEnabled[i] && amount < 0.08f;
        }
    }

    void CacheRenderers()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originalMaterials = new Material[renderers.Length][];
        originalRendererEnabled = new bool[renderers.Length];
        brickRenderers = new bool[renderers.Length];
        bool foundBounds = false;
        Bounds combined = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            originalMaterials[i] = renderer != null ? renderer.sharedMaterials : new Material[0];
            originalRendererEnabled[i] = renderer != null && renderer.enabled;
            brickRenderers[i] = HasBrickMaterial(originalMaterials[i]);
            if (renderer == null || !renderer.enabled) continue;
            if (!foundBounds) { combined = renderer.bounds; foundBounds = true; }
            else combined.Encapsulate(renderer.bounds);
        }

        visualBottom = foundBounds ? combined.min.y : transform.position.y;
        visualTop = foundBounds ? combined.max.y : visualBottom + 4f;
        cutawayMaterials = null;
        usingCutawayMaterials = false;
    }

    void EnsureRendererCacheCurrent()
    {
        Renderer[] current = GetComponentsInChildren<Renderer>(true);
        bool changed = renderers == null || current.Length != renderers.Length;
        if (!changed)
        {
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] == renderers[i]) continue;
                changed = true;
                break;
            }
        }
        if (!changed) return;

        RestoreOriginalRendering();
        DestroyCutawayCaps();
        DestroyCutawayMaterials();
        CacheRenderers();
    }

    void EnsureCutawayMaterials()
    {
        if (cutawayMaterials != null || renderers == null) return;

        cutawayMaterials = new Material[renderers.Length][];
        var clones = new Dictionary<Material, Material>();
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] originals = originalMaterials[rendererIndex];
            cutawayMaterials[rendererIndex] = new Material[originals.Length];
            for (int materialIndex = 0; materialIndex < originals.Length; materialIndex++)
            {
                Material original = originals[materialIndex];
                if (original == null) continue;

                if (!IsBrickMaterial(original))
                {
                    cutawayMaterials[rendererIndex][materialIndex] = original;
                    continue;
                }

                if (!clones.TryGetValue(original, out Material cutaway))
                {
                    Material template = GetBrickCutawayTemplate();
                    cutaway = template != null ? new Material(template) : new Material(original);
                    cutaway.name = original.name + " (Camera Cutaway)";
                    cutaway.hideFlags = HideFlags.HideAndDontSave;
                    clones.Add(original, cutaway);
                    cutawayMaterialInstances.Add(cutaway);
                }
                cutawayMaterials[rendererIndex][materialIndex] = cutaway;
            }
        }
    }

    static Material GetBrickCutawayTemplate()
    {
        if (brickCutawayTemplate == null)
            brickCutawayTemplate = Resources.Load<Material>("CameraFadeBrick");
        return brickCutawayTemplate;
    }

    void UpdateCutawayCaps(float currentTop)
    {
        EnsureCutawayCaps();
        const float capThickness = 0.08f;
        for (int i = 0; i < cutawayCaps.Count; i++)
        {
            CutawayCap record = cutawayCaps[i];
            if (record == null || record.cap == null) continue;
            Renderer source = record.source;
            if (source == null)
            {
                record.cap.SetActive(false);
                continue;
            }

            Bounds bounds = source.bounds;
            bool intersectsCut = originalRendererEnabledFor(source)
                && currentTop > bounds.min.y + 0.01f
                && currentTop < bounds.max.y - 0.01f;
            record.cap.SetActive(intersectsCut);
            if (!intersectsCut) continue;

            record.cap.layer = source.gameObject.layer;
            record.cap.transform.position = new Vector3(
                bounds.center.x,
                currentTop + capThickness * 0.5f,
                bounds.center.z);
            record.cap.transform.rotation = Quaternion.identity;
            record.cap.transform.localScale = new Vector3(
                Mathf.Max(0.04f, bounds.size.x + 0.025f),
                capThickness,
                Mathf.Max(0.04f, bounds.size.z + 0.025f));
        }
    }

    bool originalRendererEnabledFor(Renderer source)
    {
        if (source == null || renderers == null || originalRendererEnabled == null) return false;
        for (int i = 0; i < renderers.Length && i < originalRendererEnabled.Length; i++)
            if (renderers[i] == source) return originalRendererEnabled[i];
        return false;
    }

    void EnsureCutawayCaps()
    {
        if (cutawayCaps.Count > 0 || renderers == null) return;
        Material template = GetBrickCutawayTemplate();
        if (template == null) return;

        capMaterial = new Material(template)
        {
            name = "Camera Cutaway Wall Caps",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (capMaterial.HasProperty("_CutHeight"))
            capMaterial.SetFloat("_CutHeight", 10000f);

        Transform capParent = CameraWallCutaway.Instance != null
            ? CameraWallCutaway.Instance.transform
            : null;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !brickRenderers[i]) continue;
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "CameraCutawayCap_" + gameObject.name + "_" + i;
            if (capParent != null) cap.transform.SetParent(capParent, true);
            Collider collider = cap.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            MeshRenderer capRenderer = cap.GetComponent<MeshRenderer>();
            capRenderer.sharedMaterial = capMaterial;
            capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            capRenderer.receiveShadows = true;
            cap.SetActive(false);
            cutawayCaps.Add(new CutawayCap { source = renderers[i], cap = cap });
        }
    }

    void HideCutawayCaps()
    {
        for (int i = 0; i < cutawayCaps.Count; i++)
            if (cutawayCaps[i] != null && cutawayCaps[i].cap != null)
                cutawayCaps[i].cap.SetActive(false);
    }

    void DestroyCutawayCaps()
    {
        for (int i = 0; i < cutawayCaps.Count; i++)
        {
            GameObject cap = cutawayCaps[i] != null ? cutawayCaps[i].cap : null;
            if (cap == null) continue;
            if (Application.isPlaying) Destroy(cap);
            else DestroyImmediate(cap);
        }
        cutawayCaps.Clear();
        if (capMaterial != null)
        {
            if (Application.isPlaying) Destroy(capMaterial);
            else DestroyImmediate(capMaterial);
            capMaterial = null;
        }
    }

    static bool HasBrickMaterial(Material[] materials)
    {
        if (materials == null) return false;
        for (int i = 0; i < materials.Length; i++)
            if (IsBrickMaterial(materials[i])) return true;
        return false;
    }

    static bool IsBrickMaterial(Material material)
    {
        return material != null && material.shader != null
            && material.shader.name.IndexOf("BrickWall2", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void RestoreOriginalRendering()
    {
        HideCutawayCaps();
        if (renderers == null) return;
        int count = Mathf.Min(renderers.Length, originalRendererEnabled != null
            ? originalRendererEnabled.Length : 0);
        for (int i = 0; i < count; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = originalRendererEnabled[i];
            if (usingCutawayMaterials && originalMaterials != null && i < originalMaterials.Length)
                renderers[i].sharedMaterials = originalMaterials[i];
        }
        usingCutawayMaterials = false;
    }

    void DestroyCutawayMaterials()
    {
        for (int i = 0; i < cutawayMaterialInstances.Count; i++)
        {
            Material material = cutawayMaterialInstances[i];
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        cutawayMaterialInstances.Clear();
        cutawayMaterials = null;
    }

    public Vector3 GetOutward(Vector3 kitchenFocus)
    {
        if (outwardNormal.sqrMagnitude > 0.01f)
            return outwardNormal.normalized;

        Vector3 n = transform.position - kitchenFocus;
        n.y = 0f;
        if (n.sqrMagnitude < 0.0001f)
            n = transform.forward;
        return n.normalized;
    }
}
