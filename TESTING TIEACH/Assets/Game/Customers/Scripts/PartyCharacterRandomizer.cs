using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomizes / applies FREE Party Character body color, face, and hat.
/// Materials/hats load from the pack's Resources folders.
/// </summary>
[DisallowMultipleComponent]
public class PartyCharacterRandomizer : MonoBehaviour
{
    const string BodyMatsPath = "Materials/Body";
    const string FaceMatsPath = "Materials/Face";
    const string HatsPath = "Prefabs/Hats";
    const string HatAttachPath = "Armature/Hips/Spine/Spine1/Spine2/Head/customize_objects";

    // Pack uses cm-scale bones; customize_objects is authored at this local transform.
    static readonly Vector3 HatSocketLocalPos = new Vector3(0f, 29.9f, 0f);
    static readonly Vector3 HatSocketLocalScale = new Vector3(100f, 100f, 100f);

    [Range(0f, 1f)]
    [Tooltip("Chance a character gets a hat on Randomize (0 = never, 1 = always).")]
    public float hatChance = 1f;

    [Tooltip("Randomize on Start (after the character model is ready).")]
    public bool randomizeOnStart = true;

    [Header("Current look")]
    public int bodyIndex;
    public int faceIndex;
    [Tooltip("-1 = no accessory")]
    public int hatIndex = -1;

    static Material[] bodyMats;
    static Material[] faceMats;
    static GameObject[] hatPrefabs;
    static bool catalogLoaded;

    public static int BodyCount { get { EnsureCatalog(); return bodyMats != null ? bodyMats.Length : 0; } }
    public static int FaceCount { get { EnsureCatalog(); return faceMats != null ? faceMats.Length : 0; } }
    public static int HatCount { get { EnsureCatalog(); return hatPrefabs != null ? hatPrefabs.Length : 0; } }

    public static Material GetBodyMaterial(int index)
    {
        EnsureCatalog();
        if (bodyMats == null || index < 0 || index >= bodyMats.Length) return null;
        return bodyMats[index];
    }

    public static Material GetFaceMaterial(int index)
    {
        EnsureCatalog();
        if (faceMats == null || index < 0 || index >= faceMats.Length) return null;
        return faceMats[index];
    }

    public static GameObject GetHatPrefab(int index)
    {
        EnsureCatalog();
        if (hatPrefabs == null || index < 0 || index >= hatPrefabs.Length) return null;
        return hatPrefabs[index];
    }

    public static Color GetMaterialSwatchColor(Material mat)
    {
        if (mat == null) return Color.gray;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        return Color.gray;
    }

    public static Texture GetMaterialPreviewTexture(Material mat)
    {
        if (mat == null) return null;
        if (mat.HasProperty("_BaseMap"))
        {
            var t = mat.GetTexture("_BaseMap");
            if (t != null) return t;
        }
        if (mat.HasProperty("_MainTex"))
        {
            var t = mat.GetTexture("_MainTex");
            if (t != null) return t;
        }
        return mat.mainTexture;
    }

    public static Color GetHatPreviewColor(int index)
    {
        var prefab = GetHatPrefab(index);
        if (prefab == null) return new Color(0.35f, 0.35f, 0.4f, 1f);

        // Hat textures are atlases that look like spectrum grids in UI — use named colors.
        string n = prefab.name.ToLowerInvariant();
        if (n.Contains("chef")) return new Color(0.95f, 0.95f, 0.98f, 1f);
        if (n.Contains("party")) return new Color(0.9f, 0.28f, 0.55f, 1f);
        if (n.Contains("fedora") || n.Contains("orange")) return new Color(0.92f, 0.48f, 0.12f, 1f);

        var renderer = prefab.GetComponentInChildren<Renderer>(true);
        if (renderer == null || renderer.sharedMaterial == null)
            return new Color(0.55f, 0.55f, 0.6f, 1f);
        return GetMaterialSwatchColor(renderer.sharedMaterial);
    }

    public static Texture GetHatPreviewTexture(int index)
    {
        var prefab = GetHatPrefab(index);
        if (prefab == null) return null;
        var renderer = prefab.GetComponentInChildren<Renderer>(true);
        if (renderer == null || renderer.sharedMaterial == null) return null;
        return GetMaterialPreviewTexture(renderer.sharedMaterial);
    }

    public static string GetBodyName(int index)
    {
        EnsureCatalog();
        if (bodyMats == null || index < 0 || index >= bodyMats.Length || bodyMats[index] == null)
            return "Color " + (index + 1);
        return CleanName(bodyMats[index].name);
    }

    public static string GetFaceName(int index)
    {
        EnsureCatalog();
        if (faceMats == null || index < 0 || index >= faceMats.Length || faceMats[index] == null)
            return "Face " + (index + 1);
        return CleanName(faceMats[index].name);
    }

    public static string GetHatName(int index)
    {
        if (index < 0) return "None";
        EnsureCatalog();
        if (hatPrefabs == null || index >= hatPrefabs.Length || hatPrefabs[index] == null)
            return "Hat " + (index + 1);
        return CleanName(hatPrefabs[index].name);
    }

    static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "—";
        // Unity sometimes appends " (Instance)"
        int cut = raw.IndexOf(" (Instance)");
        return cut >= 0 ? raw.Substring(0, cut) : raw;
    }

    public static PartyCharacterRandomizer EnsureOn(GameObject go)
    {
        if (go == null) return null;
        var existing = go.GetComponent<PartyCharacterRandomizer>();
        if (existing != null) return existing;
        if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null
            && go.GetComponentInChildren<Animator>(true) == null)
            return null;
        return go.AddComponent<PartyCharacterRandomizer>();
    }

    void Start()
    {
        if (randomizeOnStart)
            Randomize();
    }

    public void Randomize()
    {
        EnsureCatalog();

        // Workers only use owned cosmetics; customers keep full random wardrobe.
        if (GetComponent<KitchenEmployee>() != null || GetComponentInParent<KitchenEmployee>() != null)
        {
            var cosmetics = CosmeticsUnlockManager.Ensure();
            if (cosmetics != null)
            {
                cosmetics.ApplyRandomOwned(this);
                var workerSmr = FindBodyRenderer();
                if (workerSmr != null)
                    RandomizeBlendShapes(workerSmr);
                return;
            }
        }

        if (bodyMats != null && bodyMats.Length > 0)
            bodyIndex = Random.Range(0, bodyMats.Length);
        if (faceMats != null && faceMats.Length > 0)
            faceIndex = Random.Range(0, faceMats.Length);
        if (hatPrefabs != null && hatPrefabs.Length > 0 && Random.value <= hatChance)
            hatIndex = Random.Range(0, hatPrefabs.Length);
        else
            hatIndex = -1;
        ApplyCurrent();
        var bodySmr = FindBodyRenderer();
        if (bodySmr != null)
            RandomizeBlendShapes(bodySmr);
    }

    public void SetBodyIndex(int index)
    {
        EnsureCatalog();
        if (bodyMats == null || bodyMats.Length == 0) return;
        bodyIndex = Wrap(index, bodyMats.Length);
        ApplyBodyAndFace();
    }

    public void SetFaceIndex(int index)
    {
        EnsureCatalog();
        if (faceMats == null || faceMats.Length == 0) return;
        faceIndex = Wrap(index, faceMats.Length);
        ApplyBodyAndFace();
    }

    public void SetHatIndex(int index)
    {
        EnsureCatalog();
        int count = hatPrefabs != null ? hatPrefabs.Length : 0;
        // Allow -1 (none) through count-1
        if (count <= 0)
        {
            hatIndex = -1;
            ApplyHat();
            return;
        }
        if (index < -1) index = count - 1;
        if (index >= count) index = -1;
        hatIndex = index;
        ApplyHat();
    }

    public void CycleBody(int delta) => SetBodyIndex(bodyIndex + delta);
    public void CycleFace(int delta) => SetFaceIndex(faceIndex + delta);
    public void CycleHat(int delta) => SetHatIndex(hatIndex + delta);

    public void ApplyCurrent()
    {
        EnsureCatalog();
        ApplyBodyAndFace();
        ApplyHat();
    }

    void ApplyBodyAndFace()
    {
        var bodySmr = FindBodyRenderer();
        if (bodySmr == null) return;

        var mats = bodySmr.materials;
        if (bodyMats != null && bodyMats.Length > 0 && mats.Length > 0)
        {
            bodyIndex = Mathf.Clamp(bodyIndex, 0, bodyMats.Length - 1);
            if (bodyMats[bodyIndex] != null)
                mats[0] = bodyMats[bodyIndex];
        }
        if (faceMats != null && faceMats.Length > 0 && mats.Length > 1)
        {
            faceIndex = Mathf.Clamp(faceIndex, 0, faceMats.Length - 1);
            if (faceMats[faceIndex] != null)
                mats[1] = faceMats[faceIndex];
        }
        bodySmr.materials = mats;
    }

    void ApplyHat()
    {
        Transform attach = GetOrCreateHatAttach();
        if (attach == null) return;

        ClearHatChildren(attach);

        if (hatIndex < 0 || hatPrefabs == null || hatIndex >= hatPrefabs.Length)
            return;

        var prefab = hatPrefabs[hatIndex];
        if (prefab == null) return;

        var hat = Instantiate(prefab, attach, false);
        hat.name = prefab.name;
        hat.transform.localPosition = Vector3.zero;
        hat.transform.localRotation = Quaternion.identity;
        hat.transform.localScale = Vector3.one;
        hat.SetActive(true);
    }

    static int Wrap(int index, int count)
    {
        if (count <= 0) return 0;
        index %= count;
        if (index < 0) index += count;
        return index;
    }

    static void EnsureCatalog()
    {
        if (catalogLoaded) return;
        catalogLoaded = true;

        bodyMats = Resources.LoadAll<Material>(BodyMatsPath);
        faceMats = Resources.LoadAll<Material>(FaceMatsPath);
        hatPrefabs = Resources.LoadAll<GameObject>(HatsPath);

        if (hatPrefabs == null || hatPrefabs.Length == 0)
        {
            var list = new List<GameObject>();
            TryAddHat(list, "Prefabs/Hats/party hat");
            TryAddHat(list, "Prefabs/Hats/chef hat");
            TryAddHat(list, "Prefabs/Hats/orange fedora");
            hatPrefabs = list.ToArray();
        }
    }

    static void TryAddHat(List<GameObject> list, string path)
    {
        var go = Resources.Load<GameObject>(path);
        if (go != null) list.Add(go);
    }

    SkinnedMeshRenderer FindBodyRenderer()
    {
        Transform hatRoot = FindExistingHatAttach();
        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (hatRoot != null && r.transform.IsChildOf(hatRoot)) continue;
            if (r.sharedMaterials != null && r.sharedMaterials.Length >= 2)
                return r;
        }
        return renderers.Length > 0 ? renderers[0] : null;
    }

    Transform FindExistingHatAttach()
    {
        var animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] == null) continue;
            var t = animators[i].transform.Find(HatAttachPath);
            if (t != null) return t;
        }

        var direct = transform.Find(HatAttachPath);
        if (direct != null) return direct;

        return FindDeepChild(transform, "customize_objects");
    }

    Transform GetOrCreateHatAttach()
    {
        Transform attach = FindExistingHatAttach();
        if (attach != null)
        {
            attach.localPosition = HatSocketLocalPos;
            attach.localRotation = Quaternion.identity;
            attach.localScale = HatSocketLocalScale;
            return attach;
        }

        Transform head = FindDeepChild(transform, "Head");
        if (head == null) return null;

        var go = new GameObject("customize_objects");
        go.transform.SetParent(head, false);
        go.transform.localPosition = HatSocketLocalPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = HatSocketLocalScale;
        return go.transform;
    }

    static void ClearHatChildren(Transform attach)
    {
        if (attach == null) return;
        for (int i = attach.childCount - 1; i >= 0; i--)
        {
            var child = attach.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeepChild(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    void RandomizeBlendShapes(SkinnedMeshRenderer smr)
    {
        if (smr == null || smr.sharedMesh == null) return;
        int count = smr.sharedMesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            float weight = Random.Range(0f, 55f);
            smr.SetBlendShapeWeight(i, weight);
        }
    }
}
