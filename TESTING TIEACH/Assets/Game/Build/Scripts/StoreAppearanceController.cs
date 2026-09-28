using System.Collections.Generic;
using UnityEngine;

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
        foreach (var texture in Resources.LoadAll<Texture2D>(path))
            if (texture != null && !destination.Contains(texture)) destination.Add(texture);
    }

    public int GetTextureCount(StoreSurfaceKind kind) => GetTextures(kind).Count;

    public string GetTextureName(StoreSurfaceKind kind, int index)
    {
        var textures = GetTextures(kind);
        index = Mathf.Clamp(index, 0, textures.Count - 1);
        return index == 0 || textures[index] == null ? "Original" : textures[index].name;
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
    }

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
