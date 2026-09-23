using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shows the worker's held food as a small world-space model above their head.
/// Up to four carried units are shown as a compact cluster.
/// </summary>
public class EmployeeInventoryLabel : MonoBehaviour
{
    public Vector3 offset = new Vector3(0f, 2.5f, 0f);
    [Min(0.1f)] public float singleItemSize = 0.58f;
    [Min(0.1f)] public float groupedItemSize = 0.36f;
    [Min(0f)] public float groupedSpacing = 0.28f;
    public float rotationSpeed = 28f;
    public float bobHeight = 0.045f;
    public float bobSpeed = 2.4f;

    KitchenEmployee employee;
    Transform previewRoot;
    KitchenEmployee.HeldPreviewKind shownKind = KitchenEmployee.HeldPreviewKind.None;
    ItemDefinition shownItem;
    int shownCount;

    void Start()
    {
        employee = GetComponent<KitchenEmployee>() ?? GetComponentInParent<KitchenEmployee>();
        EnsurePreviewRoot();
    }

    void EnsurePreviewRoot()
    {
        if (previewRoot != null) return;

        Transform oldLabel = transform.Find("InventoryLabel");
        if (oldLabel != null)
        {
            oldLabel.gameObject.SetActive(false);
            Destroy(oldLabel.gameObject);
        }

        Transform existing = transform.Find("HeldItemPreview");
        if (existing != null)
        {
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }

        var root = new GameObject("HeldItemPreview");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = offset;
        previewRoot = root.transform;
        root.SetActive(false);
    }

    void LateUpdate()
    {
        if (employee == null)
            employee = GetComponent<KitchenEmployee>() ?? GetComponentInParent<KitchenEmployee>();
        if (employee == null) return;
        EnsurePreviewRoot();

        KitchenEmployee.HeldPreviewKind kind = employee.GetHeldPreviewKind(out ItemDefinition item);
        int count = kind == KitchenEmployee.HeldPreviewKind.None
            ? 0
            : Mathf.Clamp(employee.HeldPreviewCount, 1, 4);

        if (kind != shownKind || item != shownItem || count != shownCount)
            RebuildPreview(kind, item, count);

        if (previewRoot == null || !previewRoot.gameObject.activeSelf) return;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        previewRoot.localPosition = offset + Vector3.up * bob;
        previewRoot.localRotation = Quaternion.Euler(0f, Time.time * rotationSpeed, 0f);
    }

    void RebuildPreview(KitchenEmployee.HeldPreviewKind kind, ItemDefinition item, int count)
    {
        shownKind = kind;
        shownItem = item;
        shownCount = count;

        for (int i = previewRoot.childCount - 1; i >= 0; i--)
        {
            previewRoot.GetChild(i).gameObject.SetActive(false);
            Destroy(previewRoot.GetChild(i).gameObject);
        }

        if (kind == KitchenEmployee.HeldPreviewKind.None || count <= 0)
        {
            previewRoot.gameObject.SetActive(false);
            return;
        }

        GameObject prefab = ResolvePrefab(kind, item);
        if (prefab == null)
        {
            previewRoot.gameObject.SetActive(false);
            return;
        }

        float size = count == 1 ? singleItemSize : groupedItemSize;
        for (int i = 0; i < count; i++)
        {
            var slot = new GameObject("HeldItem_" + (i + 1));
            slot.transform.SetParent(previewRoot, false);
            slot.transform.localPosition = GetClusterOffset(i, count);

            GameObject model = Instantiate(prefab, slot.transform);
            model.name = prefab.name + "_Preview";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            PreparePreviewModel(model);
            NormalizeModel(model, slot.transform, size);
        }

        previewRoot.gameObject.SetActive(true);
    }

    Vector3 GetClusterOffset(int index, int count)
    {
        if (count <= 1) return Vector3.zero;
        float half = groupedSpacing * 0.5f;
        if (count == 2)
            return new Vector3(index == 0 ? -half : half, 0f, 0f);
        if (count == 3)
        {
            if (index == 0) return new Vector3(-half, 0f, -half);
            if (index == 1) return new Vector3(half, 0f, -half);
            return new Vector3(0f, 0.08f, half);
        }
        return new Vector3(index % 2 == 0 ? -half : half, 0f, index < 2 ? -half : half);
    }

    GameObject ResolvePrefab(KitchenEmployee.HeldPreviewKind kind, ItemDefinition item)
    {
        ManagementModeController controller = ManagementModeController.Instance;
        if (controller != null)
        {
            GameObject fromController = GetPrefabFromController(controller, kind);
            if (fromController != null) return fromController;
        }

        StationManagePopup[] popups = Resources.FindObjectsOfTypeAll<StationManagePopup>();
        foreach (StationManagePopup popup in popups)
        {
            if (popup == null || !popup.gameObject.scene.IsValid()) continue;
            GameObject fromPopup = GetPrefabFromPopup(popup, kind);
            if (fromPopup != null) return fromPopup;
        }

        HeatLampStation lamp = HeatLampStation.Instance;
        if (lamp != null)
        {
            if (kind == KitchenEmployee.HeldPreviewKind.Burger && lamp.burgerDisplayPrefab != null)
                return lamp.burgerDisplayPrefab;
            if (kind == KitchenEmployee.HeldPreviewKind.CookedFries && lamp.friesDisplayPrefab != null)
                return lamp.friesDisplayPrefab;
        }
        return item != null ? item.prefab : null;
    }

    static GameObject GetPrefabFromController(ManagementModeController source, KitchenEmployee.HeldPreviewKind kind)
    {
        switch (kind)
        {
            case KitchenEmployee.HeldPreviewKind.RawPatty: return source.rawPattyPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.CookedPatty: return source.cookedPattyPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.RawFries: return source.rawFriesPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.CookedFries: return source.cookedFriesPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.Burger: return source.burgerPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.Drink: return source.drinkPreviewPrefab;
            default: return null;
        }
    }

    static GameObject GetPrefabFromPopup(StationManagePopup source, KitchenEmployee.HeldPreviewKind kind)
    {
        switch (kind)
        {
            case KitchenEmployee.HeldPreviewKind.RawPatty: return source.rawPattyPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.CookedPatty: return source.cookedPattyPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.RawFries: return source.rawFriesPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.CookedFries: return source.cookedFriesPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.Burger: return source.burgerPreviewPrefab;
            case KitchenEmployee.HeldPreviewKind.Drink: return source.drinkPreviewPrefab;
            default: return null;
        }
    }

    static void PreparePreviewModel(GameObject model)
    {
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (Canvas canvas in model.GetComponentsInChildren<Canvas>(true))
            canvas.enabled = false;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static void NormalizeModel(GameObject model, Transform slot, float targetSize)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest > 0.0001f)
            model.transform.localScale *= targetSize / largest;

        bounds = model.GetComponentsInChildren<Renderer>(true)[0].bounds;
        renderers = model.GetComponentsInChildren<Renderer>(true);
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        model.transform.position += slot.position - bounds.center;
    }
}
