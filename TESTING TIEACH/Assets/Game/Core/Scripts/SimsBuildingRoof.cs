using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sims-style hip roof over the restaurant. Visible when the camera is zoomed
/// out, hidden when you move in close so the interior stays playable.
/// </summary>
public class SimsBuildingRoof : MonoBehaviour
{
    [Tooltip("Camera height where the roof is fully hidden.")]
    public float hideBelowHeight = 22f;
    [Tooltip("Camera height where the roof is fully shown.")]
    public float showAboveHeight = 34f;
    [Tooltip("Seconds to fade fully in or out.")]
    public float fadeSeconds = 0.7f;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh mesh;
    Material roofMaterial;
    static Texture2D tileTexture;
    const float TileMeters = 0.55f;
    float visibility;
    bool opaqueMode = true;
    readonly Color roofColor = new Color(0.42f, 0.18f, 0.16f, 1f);

    public void Rebuild(float minX, float maxX, float minZ, float maxZ, float eaveY,
        float peakHeight, float overhang = 0.85f)
    {
        EnsureParts();
        BuildHipMesh(minX, maxX, minZ, maxZ, eaveY, Mathf.Max(0.6f, peakHeight),
            Mathf.Max(0.35f, overhang));
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
    }

    void LateUpdate()
    {
        if (meshRenderer == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        float hideY = Mathf.Min(hideBelowHeight, showAboveHeight - 1f);
        float showY = Mathf.Max(showAboveHeight, hideY + 1f);
        float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hideY, showY, cam.transform.position.y));
        float speed = 1f / Mathf.Max(0.15f, fadeSeconds);
        visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime * speed);

        bool show = visibility > 0.01f;
        meshRenderer.enabled = show;
        if (!show) return;

        ApplyFade(visibility);
    }

    void EnsureParts()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        Collider col = GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        var obstacle = GetComponent<GridObstacle>();
        if (obstacle != null)
            Destroy(obstacle);

        var cutaway = GetComponent<CameraOcclusionWall>();
        if (cutaway != null)
            Destroy(cutaway);

        if (mesh == null)
        {
            mesh = new Mesh { name = "SimsBuildingRoofMesh" };
            mesh.MarkDynamic();
        }
        meshFilter.sharedMesh = mesh;
        EnsureMaterials();
        meshRenderer.sharedMaterial = roofMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
    }

    void EnsureMaterials()
    {
        if (roofMaterial == null)
            roofMaterial = CreateRoofMaterial("SimsRoofShingles", roofColor);
    }

    static Material CreateRoofMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var material = new Material(shader) { name = name };
        material.doubleSidedGI = true;
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.12f);
        Texture2D tiles = GetTileTexture();
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", tiles);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", tiles);
        SetOpaque(material);
        SetMaterialColor(material, Color.white);
        return material;
    }

    static Texture2D GetTileTexture()
    {
        if (tileTexture != null) return tileTexture;

        const int size = 256;
        const int cols = 8;
        const int rows = 10;
        tileTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "SimsRoofTiles",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[size * size];
        Color grout = new Color(0.22f, 0.12f, 0.1f, 1f);
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;
            int row = Mathf.FloorToInt(v * rows);
            float rowOffset = (row & 1) == 0 ? 0f : 0.5f;
            float localV = v * rows - row;
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float shifted = u * cols + rowOffset;
                float localU = shifted - Mathf.Floor(shifted);

                bool seam = localU < 0.07f || localV < 0.14f || localU > 0.97f;
                float shade = 0.82f + 0.12f * Mathf.PerlinNoise(shifted * 1.7f, row * 0.37f);
                if (localV > 0.78f) shade *= 0.88f;
                if (localU < 0.18f) shade *= 0.93f;

                Color tile = new Color(0.48f * shade, 0.2f * shade, 0.16f * shade, 1f);
                pixels[y * size + x] = seam ? grout : tile;
            }
        }

        tileTexture.SetPixels(pixels);
        tileTexture.Apply(true, true);
        return tileTexture;
    }

    static void SetOpaque(Material material)
    {
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.One);
        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
    }

    static void SetFading(Material material)
    {
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        // Keep depth so stations and grid cannot draw through the roof.
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 1);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.AlphaTest;
    }

    void ApplyFade(float alpha)
    {
        bool wantOpaque = alpha >= 0.995f;
        if (wantOpaque != opaqueMode)
        {
            opaqueMode = wantOpaque;
            if (wantOpaque)
                SetOpaque(roofMaterial);
            else
                SetFading(roofMaterial);
        }

        Color color = Color.white;
        color.a = wantOpaque ? 1f : alpha;
        SetMaterialColor(roofMaterial, color);
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null) return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    void BuildHipMesh(float minX, float maxX, float minZ, float maxZ, float eaveY,
        float peakHeight, float overhang)
    {
        if (maxX - minX < 0.5f || maxZ - minZ < 0.5f) return;

        float inMinX = minX;
        float inMaxX = maxX;
        float inMinZ = minZ;
        float inMaxZ = maxZ;
        minX -= overhang;
        maxX += overhang;
        minZ -= overhang;
        maxZ += overhang;

        float peakY = eaveY + peakHeight;
        float soffitY = eaveY - 0.08f;
        float cx = (minX + maxX) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        float width = maxX - minX;
        float depth = maxZ - minZ;

        Vector3 sw = new Vector3(minX, eaveY, minZ);
        Vector3 se = new Vector3(maxX, eaveY, minZ);
        Vector3 ne = new Vector3(maxX, eaveY, maxZ);
        Vector3 nw = new Vector3(minX, eaveY, maxZ);

        var verts = new List<Vector3>(40);
        var uvs = new List<Vector2>(40);
        var tris = new List<int>(60);

        if (width >= depth)
        {
            float hip = depth * 0.5f;
            float ridgeMinX = Mathf.Min(cx, minX + hip);
            float ridgeMaxX = Mathf.Max(cx, maxX - hip);
            Vector3 ridgeW = new Vector3(ridgeMinX, peakY, cz);
            Vector3 ridgeE = new Vector3(ridgeMaxX, peakY, cz);
            AddQuad(verts, uvs, tris, sw, se, ridgeE, ridgeW, width, peakHeight + 0.2f);
            AddQuad(verts, uvs, tris, ne, nw, ridgeW, ridgeE, width, peakHeight + 0.2f);
            AddTri(verts, uvs, tris, nw, sw, ridgeW, depth);
            AddTri(verts, uvs, tris, se, ne, ridgeE, depth);
        }
        else
        {
            float hip = width * 0.5f;
            float ridgeMinZ = Mathf.Min(cz, minZ + hip);
            float ridgeMaxZ = Mathf.Max(cz, maxZ - hip);
            Vector3 ridgeS = new Vector3(cx, peakY, ridgeMinZ);
            Vector3 ridgeN = new Vector3(cx, peakY, ridgeMaxZ);
            AddQuad(verts, uvs, tris, se, ne, ridgeN, ridgeS, depth, peakHeight + 0.2f);
            AddQuad(verts, uvs, tris, nw, sw, ridgeS, ridgeN, depth, peakHeight + 0.2f);
            AddTri(verts, uvs, tris, sw, se, ridgeS, width);
            AddTri(verts, uvs, tris, ne, nw, ridgeN, width);
        }

        AddSoffitRing(verts, uvs, tris,
            inMinX, inMaxX, inMinZ, inMaxZ,
            minX, maxX, minZ, maxZ, soffitY);

        mesh.Clear();
        mesh.subMeshCount = 1;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    static void AddSoffitRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        float inMinX, float inMaxX, float inMinZ, float inMaxZ,
        float outMinX, float outMaxX, float outMinZ, float outMaxZ, float y)
    {
        // Down-facing strips so the overhang reads from below.
        AddQuad(verts, uvs, tris,
            new Vector3(outMinX, y, outMinZ),
            new Vector3(inMinX, y, inMinZ),
            new Vector3(inMaxX, y, inMinZ),
            new Vector3(outMaxX, y, outMinZ),
            outMaxX - outMinX, inMinZ - outMinZ);
        AddQuad(verts, uvs, tris,
            new Vector3(outMaxX, y, outMaxZ),
            new Vector3(inMaxX, y, inMaxZ),
            new Vector3(inMinX, y, inMaxZ),
            new Vector3(outMinX, y, outMaxZ),
            outMaxX - outMinX, outMaxZ - inMaxZ);
        AddQuad(verts, uvs, tris,
            new Vector3(outMinX, y, outMaxZ),
            new Vector3(inMinX, y, inMaxZ),
            new Vector3(inMinX, y, inMinZ),
            new Vector3(outMinX, y, outMinZ),
            outMaxZ - outMinZ, inMinX - outMinX);
        AddQuad(verts, uvs, tris,
            new Vector3(outMaxX, y, outMinZ),
            new Vector3(inMaxX, y, inMinZ),
            new Vector3(inMaxX, y, inMaxZ),
            new Vector3(outMaxX, y, outMaxZ),
            outMaxZ - outMinZ, outMaxX - inMaxX);
    }

    static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uSpan, float vSpan)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(Mathf.Abs(uSpan) / TileMeters, 0f));
        uvs.Add(new Vector2(Mathf.Abs(uSpan) / TileMeters, Mathf.Abs(vSpan) / TileMeters));
        uvs.Add(new Vector2(0f, Mathf.Abs(vSpan) / TileMeters));
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    static void AddTri(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, float uSpan)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(Mathf.Abs(uSpan) / TileMeters, 0f));
        uvs.Add(new Vector2(Mathf.Abs(uSpan) * 0.5f / TileMeters, 0.85f));
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }
}
