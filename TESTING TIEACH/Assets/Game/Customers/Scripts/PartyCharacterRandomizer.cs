using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomizes FREE Party Character body color, face, and hat on spawn.
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
    [Tooltip("Chance a customer gets a hat (0 = never, 1 = always).")]
    public float hatChance = 1f;

    [Tooltip("Randomize on Start (after the character model is ready).")]
    public bool randomizeOnStart = true;

    static Material[] bodyMats;
    static Material[] faceMats;
    static GameObject[] hatPrefabs;
    static bool catalogLoaded;

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

        var bodySmr = FindBodyRenderer();
        if (bodySmr != null)
        {
            var mats = bodySmr.materials;
            if (bodyMats != null && bodyMats.Length > 0 && mats.Length > 0)
                mats[0] = bodyMats[Random.Range(0, bodyMats.Length)];
            if (faceMats != null && faceMats.Length > 0 && mats.Length > 1)
                mats[1] = faceMats[Random.Range(0, faceMats.Length)];
            bodySmr.materials = mats;
            RandomizeBlendShapes(bodySmr);
        }

        ApplyRandomHat();
    }

    void ApplyRandomHat()
    {
        Transform attach = GetOrCreateHatAttach();
        if (attach == null)
        {
            Debug.LogWarning("PartyCharacterRandomizer: could not find Head bone for hats on " + name, this);
            return;
        }

        ClearHatChildren(attach);

        if (hatPrefabs == null || hatPrefabs.Length == 0)
        {
            Debug.LogWarning("PartyCharacterRandomizer: no hat prefabs in Resources/" + HatsPath, this);
            return;
        }

        if (Random.value > hatChance)
            return;

        var prefab = hatPrefabs[Random.Range(0, hatPrefabs.Length)];
        if (prefab == null) return;

        var hat = Instantiate(prefab, attach, false);
        hat.name = prefab.name;
        hat.transform.localPosition = Vector3.zero;
        hat.transform.localRotation = Quaternion.identity;
        hat.transform.localScale = Vector3.one;
        hat.SetActive(true);
    }

    static void EnsureCatalog()
    {
        if (catalogLoaded) return;
        catalogLoaded = true;

        bodyMats = Resources.LoadAll<Material>(BodyMatsPath);
        faceMats = Resources.LoadAll<Material>(FaceMatsPath);
        hatPrefabs = Resources.LoadAll<GameObject>(HatsPath);

        // Named fallbacks if LoadAll path fails for any reason
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
            // Prefer the main body mesh (usually has 2+ materials: body + face)
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
            // Ensure pack-authored scale so hat meshes are visible
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
