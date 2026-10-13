using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Marks a wall section that fades when it is between the camera and the store.
/// Each section keeps its authored position. The camera controller supplies a
/// fade amount based on line of sight and neighboring section distance.
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

    [Tooltip("Legacy compatibility flag. Wall transforms remain fixed while occlusion uses opacity.")]
    public bool duckByLowering = false;

    [HideInInspector] public bool sectionContainer;

    float cutawayAmount;
    Vector3 restPosition;
    bool hasRestPose;
    bool wasLowered;
    float restVisualHeight;
    float targetCutaway;
    Renderer[] renderers;
    Material[][] originalMaterials;
    Material[][] cutawayMaterials;
    bool[] originalRendererEnabled;
    bool[] brickRenderers;
    readonly List<Material> cutawayMaterialInstances = new List<Material>();
    readonly List<CutawayCap> cutawayCaps = new List<CutawayCap>();
    bool usingCutawayMaterials;
    bool usingOpacityMaterials;
    Material[][] opacityMaterials;
    readonly List<Material> opacityMaterialInstances = new List<Material>();
    bool placementLocked;
    float visualBottom;
    float visualTop;
    Bounds worldBounds;
    static Material brickCutawayTemplate;
    Material capMaterial;

    sealed class CutawayCap
    {
        public Renderer source;
        public GameObject cap;
    }

    public float DuckAmount => cutawayAmount;
    public bool IsPlacementLocked => placementLocked;
    public float CutawayDropDistance => 0f;
    public Bounds WorldBounds => worldBounds;

    public float DistanceToWall(Vector3 point)
    {
        // Fade decisions are based on the section's footprint on the floor.
        // Header, sill, and pier pieces in one vertical strip must share the
        // same distance even though their bounds have different heights.
        point.y = worldBounds.center.y;
        Vector3 nearest = worldBounds.ClosestPoint(point);
        nearest.y = point.y;
        return Vector3.Distance(nearest, point);
    }

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
        DestroyOpacityMaterials();
    }

    public void SetPlacementLock(bool locked)
    {
        placementLocked = locked;
        if (locked)
        {
            targetCutaway = 0f;
            cutawayAmount = 0f;
            wasLowered = false;
        }
        else
            CaptureRestPose();
        enabled = !locked;
        if (locked)
            CameraWallCutaway.Unregister(this);
        else
            CameraWallCutaway.Register(this);
    }

    public void SnapUp()
    {
        targetCutaway = 0f;
        cutawayAmount = 0f;
        ApplyCutaway(0f);
    }

    // Kept for callers created before cutaway rendering replaced wall movement.
    public Vector3 RestWorldOffset()
    {
        // During build placement the transform itself is the live preview position.
        // Applying the saved camera-cutaway offset would leave the wall opening behind.
        if (placementLocked) return Vector3.zero;
        if (!hasRestPose) return Vector3.zero;
        return restPosition - transform.position;
    }

    public void CaptureRestPose()
    {
        if (wasLowered) return;
        restPosition = transform.position;
        hasRestPose = true;
        CacheRenderers();
        restVisualHeight = Mathf.Max(0.2f, visualTop - visualBottom);
    }

    public void RecaptureRestPose()
    {
        wasLowered = false;
        restPosition = transform.position;
        hasRestPose = true;
        CacheRenderers();
        restVisualHeight = Mathf.Max(0.2f, visualTop - visualBottom);
    }

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
        // Wall transforms never move. The amount only controls screen-space
        // opacity, allowing line-of-sight sections to disappear while nearby
        // sections remain readable.
        if (placementLocked)
            amount = 0f;
        EnsureRendererCacheCurrent();
        if (amount <= 0.001f)
        {
            RestoreOriginalRendering();
            return;
        }

        EnsureOpacityMaterials();
        if (renderers == null || opacityMaterials == null) return;

        if (!usingOpacityMaterials)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || opacityMaterials[i] == null) continue;
                renderers[i].sharedMaterials = opacityMaterials[i];
            }
            usingOpacityMaterials = true;
        }

        float opacity = amount >= 0.995f
            ? 0f
            : Mathf.Lerp(1f, Mathf.Clamp01(occludedAlpha), amount);
        SetOpacity(opacity);
    }

    void CacheRenderers()
    {
        if (renderers != null)
            RestoreOriginalRendering();
        DestroyOpacityMaterials();
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
            if (renderer.GetComponentInParent<Canvas>() != null) continue;
            if (!foundBounds) { combined = renderer.bounds; foundBounds = true; }
            else combined.Encapsulate(renderer.bounds);
        }

        visualBottom = foundBounds ? combined.min.y : transform.position.y;
        visualTop = foundBounds ? combined.max.y : visualBottom + 4f;
        worldBounds = foundBounds ? combined : new Bounds(transform.position, new Vector3(1f, 4f, 0.4f));
        if (!hasRestPose)
            restVisualHeight = Mathf.Max(0.2f, visualTop - visualBottom);
        cutawayMaterials = null;
        usingCutawayMaterials = false;
    }

    void ApplyLowering(float amount)
    {
        // Lowering must preserve the authored wall appearance. If this wall was
        // previously using shader clipping, restore its exact original materials first.
        if (usingCutawayMaterials)
            RestoreOriginalRendering();
        HideCutawayCaps();

        if (amount <= 0.001f)
        {
            if (hasRestPose)
                transform.position = restPosition;
            wasLowered = false;
            if (!hasRestPose)
                CaptureRestPose();
            return;
        }

        if (!hasRestPose)
            CaptureRestPose();

        wasLowered = true;
        float drop = Mathf.Max(0f, restVisualHeight - Mathf.Max(0.2f, cutawayHeight)) * amount;
        transform.position = restPosition + Vector3.down * drop;
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
        DestroyOpacityMaterials();
        CacheRenderers();
    }

    void EnsureOpacityMaterials()
    {
        if (opacityMaterials != null || renderers == null) return;

        Shader fadeShader = Shader.Find("Universal Render Pipeline/Lit");
        Shader brickFadeShader = Shader.Find("ISE Rush/Camera Cutaway Triplanar");
        if (fadeShader == null && brickFadeShader == null) return;

        opacityMaterials = new Material[renderers.Length][];
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] originals = originalMaterials[rendererIndex];
            opacityMaterials[rendererIndex] = new Material[originals.Length];
            for (int materialIndex = 0; materialIndex < originals.Length; materialIndex++)
            {
                Material original = originals[materialIndex];
                if (original == null) continue;

                Material fade;
                bool useTriplanarFade = brickFadeShader != null
                    && (IsBrickMaterial(original)
                        || (original.HasProperty("_Tiling") && original.HasProperty("_Tint")));
                if (useTriplanarFade)
                {
                    Material template = IsBrickMaterial(original)
                        ? GetBrickCutawayTemplate() : null;
                    fade = template != null
                        ? new Material(template)
                        : new Material(brickFadeShader);
                }
                else if (fadeShader != null)
                {
                    fade = new Material(fadeShader);
                }
                else
                {
                    continue;
                }
                fade.name = original.name + " (Camera Fade)";
                fade.hideFlags = HideFlags.HideAndDontSave;
                CopyFadeMaterialLook(original, fade);
                opacityMaterials[rendererIndex][materialIndex] = fade;
                opacityMaterialInstances.Add(fade);
            }
        }
    }

    static void CopyFadeMaterialLook(Material original, Material fade)
    {
        // Material.mainTexture and Material.color handle custom shaders that
        // expose a texture/color under a name other than URP's usual properties.
        // Copy the standard names as well so the brick shader's tiling survives.
        Texture baseMap = original.mainTexture;
        Vector2 textureScale = original.mainTextureScale;
        Vector2 textureOffset = original.mainTextureOffset;
        if (original.HasProperty("_BaseMap"))
        {
            baseMap = original.GetTexture("_BaseMap") ?? baseMap;
            textureScale = original.GetTextureScale("_BaseMap");
            textureOffset = original.GetTextureOffset("_BaseMap");
        }
        else if (original.HasProperty("_MainTex"))
        {
            baseMap = original.GetTexture("_MainTex") ?? baseMap;
            textureScale = original.GetTextureScale("_MainTex");
            textureOffset = original.GetTextureOffset("_MainTex");
        }
        if (baseMap != null)
        {
            fade.SetTexture("_BaseMap", baseMap);
            fade.SetTextureScale("_BaseMap", textureScale);
            fade.SetTextureOffset("_BaseMap", textureOffset);
            if (original.HasProperty("_Tile"))
            {
                float tile = original.GetFloat("_Tile");
                if (fade.HasProperty("_Tile"))
                    fade.SetFloat("_Tile", tile);
                if (tile > 0.001f && fade.HasProperty("_BaseMap"))
                    fade.SetTextureScale("_BaseMap", Vector2.one * tile);
            }
        }
        if (original.HasProperty("_Tiling") && fade.HasProperty("_Tile"))
            fade.SetFloat("_Tile", original.GetFloat("_Tiling"));
        if (original.HasProperty("_BlendSharpness") && fade.HasProperty("_BlendSharpness"))
            fade.SetFloat("_BlendSharpness", original.GetFloat("_BlendSharpness"));

        Color baseColor = original.color;
        if (original.HasProperty("_BaseColor"))
            baseColor = original.GetColor("_BaseColor");
        else if (original.HasProperty("_Color"))
            baseColor = original.GetColor("_Color");
        else if (original.HasProperty("_Tint"))
            baseColor = original.GetColor("_Tint");
        baseColor.a = 1f;
        fade.SetColor("_BaseColor", baseColor);
        fade.color = baseColor;

        if (original.HasProperty("_Metallic") && fade.HasProperty("_Metallic"))
            fade.SetFloat("_Metallic", original.GetFloat("_Metallic"));
        if (original.HasProperty("_Smoothness") && fade.HasProperty("_Smoothness"))
            fade.SetFloat("_Smoothness", original.GetFloat("_Smoothness"));

        fade.SetFloat("_Surface", 1f);
        fade.SetFloat("_Blend", 0f);
        fade.SetFloat("_AlphaClip", 0f);
        fade.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        fade.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        fade.SetFloat("_ZWrite", 0f);
        if (fade.HasProperty("_Opacity"))
            fade.SetFloat("_Opacity", 1f);
        fade.SetOverrideTag("RenderType", "Transparent");
        fade.renderQueue = (int)RenderQueue.Transparent;
        fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        fade.DisableKeyword("_ALPHATEST_ON");
        if (fade.HasProperty("_ReceiveShadows"))
            fade.SetFloat("_ReceiveShadows", 0f);
    }

    void SetOpacity(float opacity)
    {
        for (int i = 0; i < opacityMaterialInstances.Count; i++)
        {
            Material material = opacityMaterialInstances[i];
            if (material == null) continue;
            Color color = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor") : Color.white;
            color.a = Mathf.Clamp01(opacity);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Opacity"))
                material.SetFloat("_Opacity", Mathf.Clamp01(opacity));
        }
    }

    void DestroyOpacityMaterials()
    {
        for (int i = 0; i < opacityMaterialInstances.Count; i++)
        {
            Material material = opacityMaterialInstances[i];
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        opacityMaterialInstances.Clear();
        opacityMaterials = null;
        usingOpacityMaterials = false;
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
            if ((usingCutawayMaterials || usingOpacityMaterials)
                && originalMaterials != null && i < originalMaterials.Length)
                renderers[i].sharedMaterials = originalMaterials[i];
        }
        usingCutawayMaterials = false;
        usingOpacityMaterials = false;
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
