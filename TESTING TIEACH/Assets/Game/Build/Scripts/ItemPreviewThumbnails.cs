using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds one-shot 3D thumbnails for inventory cards and customize previews from prefabs.
/// </summary>
public static class ItemPreviewThumbnails
{
    const int Size = 256;
    const int CacheVersion = 2;
    const string RootName = "__ItemPreviewThumbnails";

    static readonly Dictionary<int, RenderTexture> cache = new Dictionary<int, RenderTexture>();
    static Camera previewCamera;
    static Transform stage;

    public static Texture Get(ItemDefinition item)
    {
        if (item == null || item.prefab == null)
            return null;
        return GetPrefab(item.prefab, item.itemName);
    }

    public static Texture GetPrefab(GameObject prefab, string displayName = null)
    {
        if (prefab == null)
            return null;

        int key = prefab.GetInstanceID() ^ (CacheVersion * 397);
        if (cache.TryGetValue(key, out var existing) && existing != null)
            return existing;

        EnsureStage();
        string label = !string.IsNullOrEmpty(displayName) ? displayName : prefab.name;
        var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 2,
            filterMode = FilterMode.Bilinear,
            name = "Preview_" + label
        };

        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab, stage);
            instance.name = "PreviewInstance_" + label;
            SetLayerRecursively(instance, stage.gameObject.layer);

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(15f, 150f, 0f);
            instance.transform.localScale = Vector3.one;

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
            stageGo.layer = 31;
            stage = stageGo.transform;
        }

        var camT = root.transform.Find("PreviewCamera");
        if (camT == null)
        {
            var camGo = new GameObject("PreviewCamera", typeof(Camera));
            camGo.transform.SetParent(root.transform, false);
            camT = camGo.transform;
        }

        previewCamera = camT.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.16f, 0.17f, 0.2f, 1f);
        previewCamera.cullingMask = 1 << 31;
        previewCamera.enabled = false;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 50f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = true;
    }

    static Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }
}
