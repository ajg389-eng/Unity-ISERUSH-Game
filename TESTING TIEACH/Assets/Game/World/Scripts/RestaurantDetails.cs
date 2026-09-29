using UnityEngine;

/// <summary>Visual-only decorations: no colliders, station components or inventory state.</summary>
public static class RestaurantDetails
{
    public static GameObject Add(string asset, Transform parent, Vector3 localBase, Vector3 maxSize)
    {
        var prefab = Resources.Load<GameObject>("RestaurantDetails/" + asset);
        if (prefab == null) return null;
        var root = new GameObject("Decorative " + asset);
        root.transform.SetParent(parent, false);
        var model = Object.Instantiate(prefab, root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
        Bounds bounds;
        if (!LocalBounds(model.transform, root.transform, out bounds)) { Object.Destroy(root); return null; }
        float scale = Mathf.Min(maxSize.x / Mathf.Max(bounds.size.x, 0.001f),
            maxSize.y / Mathf.Max(bounds.size.y, 0.001f), maxSize.z / Mathf.Max(bounds.size.z, 0.001f));
        model.transform.localScale *= scale;
        LocalBounds(model.transform, root.transform, out bounds);
        model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        root.transform.localPosition = localBase;
        return root;
    }

    public static bool LocalBounds(Transform model, Transform space, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null) continue;
            Bounds b = mesh.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                p = space.InverseTransformPoint(mesh.transform.TransformPoint(p));
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }
        return found;
    }

    public static void StockPantry(Transform pantry)
    {
        if (pantry.Find("Pantry Shelf Details") != null) return;
        Transform shelves = null;
        foreach (Transform child in pantry.GetComponentsInChildren<Transform>())
            if (child.name.Contains("StorageShelve")) { shelves = child; break; }
        if (shelves == null || !LocalBounds(shelves, pantry, out Bounds b)) return;
        var root = new GameObject("Pantry Shelf Details");
        root.transform.SetParent(pantry, false);
        // Keep the arrangement inside the rack and leave space between supplies.
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            Vector3 position = new Vector3(
                Mathf.Lerp(b.min.x, b.max.x, 0.24f + column * 0.26f),
                Mathf.Lerp(b.min.y, b.max.y, 0.09f + row * 0.27f),
                b.center.z);
            Add((row + column) % 2 == 0 ? "Box" : "Bottle", root.transform, position,
                new Vector3(b.size.x * 0.21f, b.size.y * 0.20f, b.size.z * 0.60f));
        }
    }
}
