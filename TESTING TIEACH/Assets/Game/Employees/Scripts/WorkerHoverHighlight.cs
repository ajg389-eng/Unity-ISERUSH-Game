using UnityEngine;

/// <summary>
/// Makes a worker's materials glow while hovered in Manage mode.
/// </summary>
public class WorkerHoverHighlight : MonoBehaviour
{
    [Header("Glow")]
    public Color glowColor = new Color(0.35f, 0.85f, 1.2f, 1f);
    [Tooltip("Multiplies emission while hovered.")]
    public float emissionBoost = 2.2f;
    [Tooltip("Also brightens the base albedo slightly.")]
    public float albedoBrighten = 0.35f;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Renderer[] renderers;
    Material[][] materialInstances;
    Color[][] originalAlbedo;
    Color[][] originalEmission;
    bool[][] hadEmissionKeyword;
    bool hovering;
    bool cached;

    public static WorkerHoverHighlight EnsureOn(KitchenEmployee employee)
    {
        if (employee == null) return null;
        var highlight = employee.GetComponent<WorkerHoverHighlight>();
        if (highlight == null)
            highlight = employee.gameObject.AddComponent<WorkerHoverHighlight>();
        return highlight;
    }

    public void SetHovered(bool on)
    {
        if (hovering == on) return;
        hovering = on;
        EnsureCache();
        Apply(on);
    }

    void OnDisable()
    {
        if (hovering)
        {
            hovering = false;
            Apply(false);
        }
    }

    void OnDestroy()
    {
        // Instance materials are cleaned up with the object.
    }

    void EnsureCache()
    {
        if (cached && renderers != null) return;

        renderers = GetComponentsInChildren<Renderer>(true);
        materialInstances = new Material[renderers.Length][];
        originalAlbedo = new Color[renderers.Length][];
        originalEmission = new Color[renderers.Length][];
        hadEmissionKeyword = new bool[renderers.Length][];

        for (int r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            if (renderer == null || ShouldSkip(renderer))
            {
                materialInstances[r] = null;
                continue;
            }

            // Instance materials so we don't mutate shared assets.
            var mats = renderer.materials;
            materialInstances[r] = mats;
            originalAlbedo[r] = new Color[mats.Length];
            originalEmission[r] = new Color[mats.Length];
            hadEmissionKeyword[r] = new bool[mats.Length];

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;

                originalAlbedo[r][m] = ReadAlbedo(mat);
                originalEmission[r][m] = mat.HasProperty(EmissionColorId)
                    ? mat.GetColor(EmissionColorId)
                    : Color.black;
                hadEmissionKeyword[r][m] = mat.IsKeywordEnabled("_EMISSION");
            }
        }

        cached = true;
    }

    void Apply(bool on)
    {
        if (renderers == null) return;

        for (int r = 0; r < renderers.Length; r++)
        {
            var mats = materialInstances[r];
            if (mats == null) continue;

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;

                Color albedo = originalAlbedo[r][m];
                Color emission = originalEmission[r][m];

                if (on)
                {
                    Color brightAlbedo = Color.Lerp(albedo, Color.white, albedoBrighten);
                    WriteAlbedo(mat, brightAlbedo);

                    if (mat.HasProperty(EmissionColorId))
                    {
                        mat.EnableKeyword("_EMISSION");
                        // HDR-ish emission so URP/Built-in both read as a glow.
                        Color glow = glowColor * emissionBoost;
                        mat.SetColor(EmissionColorId, glow + emission);
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
                else
                {
                    WriteAlbedo(mat, albedo);
                    if (mat.HasProperty(EmissionColorId))
                    {
                        mat.SetColor(EmissionColorId, emission);
                        if (!hadEmissionKeyword[r][m])
                            mat.DisableKeyword("_EMISSION");
                    }
                }
            }
        }
    }

    static Color ReadAlbedo(Material mat)
    {
        if (mat.HasProperty(BaseColorId)) return mat.GetColor(BaseColorId);
        if (mat.HasProperty(ColorId)) return mat.GetColor(ColorId);
        return Color.white;
    }

    static void WriteAlbedo(Material mat, Color color)
    {
        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
        if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
    }

    static bool ShouldSkip(Renderer renderer)
    {
        if (renderer == null) return true;
        if (renderer is LineRenderer) return true;
        if (renderer.GetComponentInParent<Canvas>() != null) return true;
        if (!renderer.enabled) return true;
        string n = renderer.gameObject.name;
        if (n.Contains("TaskBar") || n.Contains("Label") || n.Contains("Highlight"))
            return true;
        return false;
    }
}
