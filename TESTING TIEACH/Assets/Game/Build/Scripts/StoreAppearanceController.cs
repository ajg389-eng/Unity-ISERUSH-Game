using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum StoreSurfaceKind { Walls, Floor, Roof }

/// <summary>
/// Independent texture and tint customization for the restaurant shell.
/// Add future textures beneath Resources/StoreCustomization/Walls, Floors, or Roofs.
/// </summary>
public sealed class StoreAppearanceController : MonoBehaviour
{
    public static StoreAppearanceController Instance { get; private set; }

    public int WallTextureIndex { get; private set; }
    public int FloorTextureIndex { get; private set; }
    public int RoofTextureIndex { get; private set; }
    public Color WallTint { get; private set; } = Color.white;
    public Color FloorTint { get; private set; } = Color.white;
    public Color RoofTint { get; private set; } = Color.white;

    sealed class OriginalLook
    {
        public Color[] colors;
        public Texture[] textures;
    }

    readonly Dictionary<Renderer, OriginalLook> originals = new Dictionary<Renderer, OriginalLook>();
    readonly Dictionary<Renderer, MeshRenderer> tintOverlays = new Dictionary<Renderer, MeshRenderer>();
    readonly List<Texture2D> wallTextures = new List<Texture2D>();
    readonly List<Texture2D> floorTextures = new List<Texture2D>();
    readonly List<Texture2D> roofTextures = new List<Texture2D>();
    MaterialPropertyBlock block;
    GridManager grid;
    int lastRendererSignature = -1;
    bool refreshQueued = true;

    public static readonly string[] TintNames = { "Original", "Cream", "Warm", "Sage", "Blue", "Slate" };
    public static readonly Color[] TintColors =
    {
        Color.white,
        new Color(1f, .94f, .82f),
        new Color(1f, .78f, .64f),
        new Color(.72f, .88f, .72f),
        new Color(.68f, .84f, 1f),
        new Color(.62f, .66f, .75f)
    };

    public static StoreAppearanceController Ensure()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<StoreAppearanceController>(FindObjectsInactive.Include);
        if (existing != null) return existing;
        var host = GridManager.Instance != null ? GridManager.Instance.gameObject : new GameObject("StoreAppearanceController");
        return host.AddComponent<StoreAppearanceController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        block = new MaterialPropertyBlock();
        grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        LoadTextureLibraries();
        if (grid != null) grid.GridChanged += QueueRefresh;
    }

    void Start() => ApplyAll();

    void OnDestroy()
    {
        if (grid != null) grid.GridChanged -= QueueRefresh;
        if (Instance == this) Instance = null;
        foreach (var overlay in tintOverlays.Values)
            if (overlay != null && overlay.sharedMaterial != null)
                Destroy(overlay.sharedMaterial);
        tintOverlays.Clear();
    }

    void LateUpdate()
    {
        int signature = CalculateRendererSignature();
        if (!refreshQueued && signature == lastRendererSignature) return;
        refreshQueued = false;
        lastRendererSignature = signature;
        ApplyAll();
    }

    void LoadTextureLibraries()
    {
        LoadTextures(wallTextures, "StoreCustomization/Walls");
        LoadTextures(floorTextures, "StoreCustomization/Floors");
        LoadTextures(roofTextures, "StoreCustomization/Roofs");
    }

    static void LoadTextures(List<Texture2D> destination, string path)
    {
        destination.Clear();
        destination.Add(null); // Original material texture.
        var discovered = Resources.LoadAll<Texture2D>(path);
        System.Array.Sort(discovered, (a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        foreach (var texture in discovered)
            if (texture != null && !destination.Contains(texture)) destination.Add(texture);
    }

    public int GetTextureCount(StoreSurfaceKind kind) => GetTextures(kind).Count;

    public string GetTextureName(StoreSurfaceKind kind, int index)
    {
        var textures = GetTextures(kind);
        index = Mathf.Clamp(index, 0, textures.Count - 1);
        if (index == 0 || textures[index] == null) return "Original";
        string name = textures[index].name;
        return name.Length > 3 && char.IsDigit(name[0]) && char.IsDigit(name[1]) && name[2] == ' '
            ? name.Substring(3)
            : name;
    }

    public Texture2D GetTexturePreview(StoreSurfaceKind kind, int index)
    {
        var textures = GetTextures(kind);
        index = Mathf.Clamp(index, 0, textures.Count - 1);
        return textures[index];
    }

    public int GetTextureIndex(StoreSurfaceKind kind) => kind switch
    {
        StoreSurfaceKind.Walls => WallTextureIndex,
        StoreSurfaceKind.Roof => RoofTextureIndex,
        _ => FloorTextureIndex
    };

    public Color GetTint(StoreSurfaceKind kind) => kind switch
    {
        StoreSurfaceKind.Walls => WallTint,
        StoreSurfaceKind.Roof => RoofTint,
        _ => FloorTint
    };

    public void SetTexture(StoreSurfaceKind kind, int index, bool playSound = true)
    {
        index = Mathf.Clamp(index, 0, GetTextureCount(kind) - 1);
        if (kind == StoreSurfaceKind.Walls) WallTextureIndex = index;
        else if (kind == StoreSurfaceKind.Roof) RoofTextureIndex = index;
        else FloorTextureIndex = index;
        ApplyAll();
        if (playSound) Sfx.Play(SfxId.UiClick);
    }

    public void SetTint(StoreSurfaceKind kind, Color tint, bool playSound = true)
    {
        tint.a = 1f;
        if (kind == StoreSurfaceKind.Walls) WallTint = tint;
        else if (kind == StoreSurfaceKind.Roof) RoofTint = tint;
        else FloorTint = tint;
        ApplyAll();
        if (playSound) Sfx.Play(SfxId.UiClick);
    }

    public void RestoreState(int wallTexture, int floorTexture, int roofTexture,
        Color wallTint, Color floorTint, Color roofTint)
    {
        WallTextureIndex = Mathf.Clamp(wallTexture, 0, wallTextures.Count - 1);
        FloorTextureIndex = Mathf.Clamp(floorTexture, 0, floorTextures.Count - 1);
        RoofTextureIndex = Mathf.Clamp(roofTexture, 0, roofTextures.Count - 1);
        WallTint = ValidTint(wallTint);
        FloorTint = ValidTint(floorTint);
        RoofTint = ValidTint(roofTint);
        ApplyAll();
    }

    static Color ValidTint(Color color) => color.a <= 0f ? Color.white : new Color(color.r, color.g, color.b, 1f);
    public void QueueRefresh() => refreshQueued = true;
    public void RefreshNow() { refreshQueued = false; ApplyAll(); }

    void ApplyAll()
    {
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        if (grid == null) return;

        Texture floorTexture = GetTexture(floorTextures, FloorTextureIndex);
        ApplyToTransform(grid.floor, FloorTint, floorTexture);
        if (grid.customerFloor == null)
        {
            var customerFloor = GameObject.Find("CustomerFloor");
            if (customerFloor != null) grid.customerFloor = customerFloor.transform;
        }
        ApplyToTransform(grid.customerFloor, FloorTint, floorTexture);

        ApplyWalls(WallTint, GetTexture(wallTextures, WallTextureIndex));
        ApplyRoof(RoofTint, GetTexture(roofTextures, RoofTextureIndex));
    }

    static Texture GetTexture(List<Texture2D> textures, int index) =>
        index > 0 && index < textures.Count ? textures[index] : null;

    List<Texture2D> GetTextures(StoreSurfaceKind kind) => kind switch
    {
        StoreSurfaceKind.Walls => wallTextures,
        StoreSurfaceKind.Roof => roofTextures,
        _ => floorTextures
    };

    void ApplyWalls(Color tint, Texture texture)
    {
        ApplyNamed("KitchenWalls", tint, texture);
        ApplyNamed("Wall_North", tint, texture);
        ApplyNamed("Wall_South", tint, texture);
        ApplyNamed("Wall_East", tint, texture);
        ApplyNamed("Wall_West", tint, texture);

        var root = GameObject.Find("KitchenExpandWalls");
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<SimsBuildingRoof>() != null) continue;
            if (renderer.transform.name.IndexOf("Window", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            ApplyToRenderer(renderer, tint, texture);
        }
    }

    void ApplyRoof(Color tint, Texture texture)
    {
        var root = GameObject.Find("KitchenExpandWalls");
        var roof = root != null ? root.transform.Find("SimsRoof") : null;
        ApplyToTransform(roof, tint, texture);
    }

    void ApplyNamed(string objectName, Color tint, Texture texture)
    {
        var target = GameObject.Find(objectName);
        if (target != null) ApplyToTransform(target.transform, tint, texture);
    }

    void ApplyToTransform(Transform target, Color tint, Texture texture)
    {
        if (target == null) return;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            ApplyToRenderer(renderer, tint, texture);
    }

    void ApplyToRenderer(Renderer renderer, Color tint, Texture textureOverride)
    {
        if (renderer == null) return;
        if (renderer.gameObject.name == "StoreAppearanceTint") return;
        if (block == null) block = new MaterialPropertyBlock();
        Material[] materials = renderer.sharedMaterials;
        if (!originals.TryGetValue(renderer, out OriginalLook look) || look.colors.Length != materials.Length)
        {
            look = new OriginalLook { colors = new Color[materials.Length], textures = new Texture[materials.Length] };
            for (int i = 0; i < materials.Length; i++)
            {
                look.colors[i] = ReadMaterialColor(materials[i]);
                look.textures[i] = ReadMaterialTexture(materials[i]);
            }
            originals[renderer] = look;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;
            block.Clear();
            Color color = Multiply(look.colors[i], tint);
            Texture texture = textureOverride != null ? textureOverride : look.textures[i];
            if (material.HasProperty("_BaseColor")) block.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) block.SetColor("_Color", color);
            if (texture != null && material.HasProperty("_BaseMap")) block.SetTexture("_BaseMap", texture);
            if (texture != null && material.HasProperty("_MainTex")) block.SetTexture("_MainTex", texture);
            renderer.SetPropertyBlock(block, i);
        }

        ApplyAppearanceOverlay(renderer, tint, textureOverride);
    }

    void ApplyAppearanceOverlay(Renderer source, Color tint, Texture texture)
    {
        bool needsOverlay = texture != null || !ApproximatelyWhite(tint);
        if (!needsOverlay)
        {
            if (tintOverlays.TryGetValue(source, out MeshRenderer existing) && existing != null)
                existing.gameObject.SetActive(false);
            return;
        }

        if (!(source is MeshRenderer) || !source.TryGetComponent(out MeshFilter sourceFilter)
            || sourceFilter.sharedMesh == null)
            return;

        if (!tintOverlays.TryGetValue(source, out MeshRenderer overlay) || overlay == null)
        {
            Shader shader = Shader.Find("ISE Rush/Surface Tint Multiply");
            if (shader == null) return;
            var overlayObject = new GameObject("StoreAppearanceTint", typeof(MeshFilter), typeof(MeshRenderer));
            overlayObject.transform.SetParent(source.transform, false);
            overlayObject.layer = source.gameObject.layer;
            overlayObject.GetComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            overlay = overlayObject.GetComponent<MeshRenderer>();
            overlay.sharedMaterial = new Material(shader)
            {
                name = source.name + " Tint (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            tintOverlays[source] = overlay;
        }

        overlay.gameObject.SetActive(source.enabled);
        overlay.sharedMaterial.SetColor("_Tint", tint);
        overlay.sharedMaterial.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
        overlay.sharedMaterial.SetFloat("_Tiling", TextureWorldTiling(texture));
        bool hasTexture = texture != null;
        overlay.sharedMaterial.SetFloat("_TextureMode", hasTexture ? 1f : 0f);
        overlay.sharedMaterial.SetFloat("_SrcBlend", hasTexture
            ? (float)BlendMode.One : (float)BlendMode.DstColor);
        overlay.sharedMaterial.SetFloat("_DstBlend", (float)BlendMode.Zero);
        overlay.sharedMaterial.SetFloat("_ZWrite", hasTexture ? 1f : 0f);
    }

    static float TextureWorldTiling(Texture texture)
    {
        if (texture != null && texture.name.IndexOf("Cream Tile", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return 2f;
        return .5f;
    }

    static bool ApproximatelyWhite(Color color) =>
        Mathf.Abs(color.r - 1f) < 0.002f
        && Mathf.Abs(color.g - 1f) < 0.002f
        && Mathf.Abs(color.b - 1f) < 0.002f;

    static Color ReadMaterialColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    static Texture ReadMaterialTexture(Material material)
    {
        if (material == null) return null;
        if (material.HasProperty("_BaseMap")) return material.GetTexture("_BaseMap");
        if (material.HasProperty("_MainTex")) return material.GetTexture("_MainTex");
        return null;
    }

    static Color Multiply(Color a, Color b) => new Color(
        Mathf.Clamp01(a.r * b.r), Mathf.Clamp01(a.g * b.g), Mathf.Clamp01(a.b * b.b), a.a);

    int CalculateRendererSignature()
    {
        int count = 0;
        var walls = GameObject.Find("KitchenExpandWalls");
        if (walls != null) count += walls.GetComponentsInChildren<Renderer>(true).Length * 17;
        if (grid != null && grid.floor != null) count += grid.floor.GetComponentsInChildren<Renderer>(true).Length * 31;
        return count;
    }
}
