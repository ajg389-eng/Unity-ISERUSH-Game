using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds one-shot 3D thumbnails for inventory cards and customize previews from prefabs.
/// </summary>
public static class ItemPreviewThumbnails
{
    const int Size = 256;
    const int CacheVersion = 10;
    const string RootName = "__ItemPreviewThumbnails";
    const int PreviewLayer = 31;

    // Shared with the inventory preview frame so fitted square thumbnails do not
    // reveal a second background shade around their edges.
    public static readonly Color BackgroundColor = GameUITheme.Backdrop;

    static readonly Dictionary<int, RenderTexture> cache = new Dictionary<int, RenderTexture>();
    static Camera previewCamera;
    static Transform stage;

    public static Texture Get(ItemDefinition item)
    {
        if (item == null || item.prefab == null)
            return null;
        return GetPrefab(item.prefab, item.itemName, item.placementScale);
    }

    public static Texture GetPrefab(GameObject prefab, string displayName = null, Vector3? previewScale = null)
    {
        if (prefab == null)
            return null;

        Vector3 scale = previewScale ?? prefab.transform.localScale;
        int key = prefab.GetInstanceID() ^ (CacheVersion * 397) ^ scale.GetHashCode();
        if (cache.TryGetValue(key, out var existing))
        {
            if (existing != null && existing.IsCreated())
                return existing;
            cache.Remove(key);
            if (existing != null)
            {
                existing.Release();
                Object.Destroy(existing);
            }
        }

        EnsureStage();
        string label = !string.IsNullOrEmpty(displayName) ? displayName : prefab.name;
        var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 2,
            filterMode = FilterMode.Bilinear,
            name = "Preview_" + label
        };
        rt.Create();

        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab, stage);
            instance.name = "PreviewInstance_" + label;
            SetLayerRecursively(instance, stage.gameObject.layer);
            StripPreviewHelpers(instance);

            instance.transform.localPosition = Vector3.zero;
            // Assembly prefabs are authored with their working side opposite the
            // standard shop camera, so turn only their thumbnail around.
            bool assemblyPreview = instance.GetComponentInChildren<AssemblyStation>(true) != null;
            instance.transform.localRotation = Quaternion.Euler(assemblyPreview ? -15f : 15f,
                assemblyPreview ? 330f : 150f, 0f);
            // Match the same non-uniform scale used by the placed item. Uniform scale is
            // handled by camera framing, but preserving the proportions prevents compact
            // counter stations from appearing squeezed in their inventory cards.
            instance.transform.localScale = scale;

            Bounds bounds = CalculateBounds(instance);
            // Frame so the longest side fits — keeps stations to scale in a square render.
            float halfW = Mathf.Max(bounds.extents.x, bounds.extents.z, 0.05f);
            float halfH = Mathf.Max(bounds.extents.y, 0.05f);
            float ortho = Mathf.Max(halfW, halfH) * 1.2f;
            Vector3 center = bounds.center;

            previewCamera.targetTexture = rt;
            previewCamera.aspect = 1f;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = ortho;
            previewCamera.transform.position = center + new Vector3(0f, halfH * 0.35f, -Mathf.Max(halfW, halfH) * 2.6f);
            previewCamera.transform.LookAt(center);
            previewCamera.Render();
            previewCamera.targetTexture = null;
        }
        finally
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }

        cache[key] = rt;
        return rt;
    }

    static void EnsureStage()
    {
        if (previewCamera != null && stage != null)
            return;

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;
        }

        stage = root.transform.Find("Stage");
        if (stage == null)
        {
            var stageGo = new GameObject("Stage");
            stageGo.transform.SetParent(root.transform, false);
            stageGo.transform.position = new Vector3(0f, -5000f, 0f);
            stageGo.layer = PreviewLayer;
            stage = stageGo.transform;
        }

        // Remove the short-lived isolated-lighting experiment if its hidden
        // preview root survived a script reload.
        Transform keyLight = root.transform.Find("KeyLight");
        if (keyLight != null) Object.DestroyImmediate(keyLight.gameObject);
        Transform fillLight = root.transform.Find("FillLight");
        if (fillLight != null) Object.DestroyImmediate(fillLight.gameObject);

        var camT = root.transform.Find("PreviewCamera");
        if (camT == null)
        {
            var camGo = new GameObject("PreviewCamera", typeof(Camera));
            camGo.transform.SetParent(root.transform, false);
            camT = camGo.transform;
        }

        previewCamera = camT.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = BackgroundColor;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.enabled = false;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 50f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = true;
    }

    static void StripPreviewHelpers(GameObject instance)
    {
        // The authored interaction highlight is intentionally part of the station
        // prefab for build mode. Exclude its exact referenced object from thumbnails,
        // regardless of whether the artist named it Quad or InteractionHighlight.
        StationInteractionTiles[] interactionTiles =
            instance.GetComponentsInChildren<StationInteractionTiles>(true);
        for (int i = 0; i < interactionTiles.Length; i++)
        {
            StationInteractionTiles tiles = interactionTiles[i];
            if (tiles == null || tiles.buildModeHighlight == null) continue;
            GameObject highlight = tiles.buildModeHighlight;
            tiles.buildModeHighlight = null;
            Object.DestroyImmediate(highlight);
        }

        Transform[] parts = instance.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i];
            if (part == null) continue;
            if (part.name != "InteractionHighlight") continue;
            Object.DestroyImmediate(part.gameObject);
        }

        Canvas[] canvases = instance.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null)
                canvases[i].gameObject.SetActive(false);
        }
    }

    static Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Vector3 origin = go.transform.position;
        Bounds bounds = default;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            // Floor markers spawned in Awake can sit on the real grid while the
            // preview model is parked far below the world, which frames the
            // station out of the picture.
            if (Vector3.Distance(renderer.bounds.center, origin) > 12f)
            {
                renderer.enabled = false;
                continue;
            }
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        return found ? bounds : new Bounds(origin, Vector3.one);
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }
}
