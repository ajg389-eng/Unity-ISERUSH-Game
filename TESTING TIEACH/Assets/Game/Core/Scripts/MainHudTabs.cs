using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-left Inventory / Management tabs. Place under PlayerUI via
/// Game → Setup Main HUD Tabs so you can edit position in the scene.
/// </summary>
public class MainHudTabs : MonoBehaviour
{
    public const string StripName = "MainHudTabs";

    const float TabWidth = 168f;
    const float TabHeight = 40f;
    const float TabSpacing = 10f;

    static readonly Color StripColor = HudTabColors.Strip;
    static readonly Color IdleColor = HudTabColors.Idle;
    static readonly Color ActiveColor = HudTabColors.Active;

    [Header("Layout")]
    [Tooltip("When true, RectTransform position/size are left alone so you can edit them in the scene.")]
    public bool useSceneLayout = true;

    [Tooltip("Anchored position used only when Use Scene Layout is off.")]
    public Vector2 screenOffset = new Vector2(16f, -8f);

    Image inventoryBg;
    Image managementBg;
    Button inventoryTabButton;
    Button managementTabButton;
    bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInScene();
    }

    public static MainHudTabs EnsureInScene()
    {
        var existing = FindFirstObjectByType<MainHudTabs>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.Build();
            return existing;
        }

        var canvas = GameObject.Find("PlayerUI")?.GetComponent<Canvas>();
        if (canvas == null) return null;

        return CreateInCanvas(canvas.transform);
    }

    /// <summary>Create a scene-editable strip under the given canvas (used by editor setup).</summary>
    public static MainHudTabs CreateInCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        var go = new GameObject(StripName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(MainHudTabs));
        go.transform.SetParent(canvasTransform, false);
        go.transform.SetAsLastSibling();

        var tabs = go.GetComponent<MainHudTabs>();
        tabs.useSceneLayout = true;
        tabs.Build(forceDefaultLayout: true);
        return tabs;
    }

    void Awake()
    {
        Build();
    }

    void LateUpdate()
    {
        HideSourceButtons();
        RefreshVisuals();
        // Stay above full-screen overlays that use raycastTarget=false parents
        // but still have opaque children.
        transform.SetAsLastSibling();
    }

    public void Build(bool forceDefaultLayout = false)
    {
        var rt = (RectTransform)transform;
        if (forceDefaultLayout || !useSceneLayout)
            ApplyDefaultLayout(rt);

        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.raycastTarget = true;
        bg.color = StripColor;

        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
        if (forceDefaultLayout || !useSceneLayout)
            ApplyDefaultLayoutGroup(hlg);

        EnsureTabVisuals();
        WireTabButtons();
        HideSourceButtons();
        HideManagementPageTitle();
        RefreshVisuals();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        built = true;
    }

    public void ApplyDefaultLayout()
    {
        ApplyDefaultLayout((RectTransform)transform);
    }

    void ApplyDefaultLayout(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = screenOffset;
        rt.sizeDelta = new Vector2(TabWidth * 2f + TabSpacing + 16f, TabHeight + 12f);
    }

    static void ApplyDefaultLayoutGroup(HorizontalLayoutGroup hlg)
    {
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.spacing = TabSpacing;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
    }

    void EnsureTabVisuals()
    {
        // Prefer scene-authored tabs; only create missing ones.
        inventoryBg = FindTabImage("InventoryTab");
        managementBg = FindTabImage("ManagementTab");

        if (inventoryBg == null)
            inventoryBg = CreateTab("InventoryTab", "Inventory");
        if (managementBg == null)
            managementBg = CreateTab("ManagementTab", "Management");

        inventoryTabButton = inventoryBg != null ? inventoryBg.GetComponent<Button>() : null;
        managementTabButton = managementBg != null ? managementBg.GetComponent<Button>() : null;

        // Remove accidental duplicates left from older rebuild logic.
        RemoveDuplicateTabs("InventoryTab", inventoryBg != null ? inventoryBg.transform : null);
        RemoveDuplicateTabs("ManagementTab", managementBg != null ? managementBg.transform : null);
    }

    Image FindTabImage(string objectName)
    {
        var t = transform.Find(objectName);
        return t != null ? t.GetComponent<Image>() : null;
    }

    void RemoveDuplicateTabs(string objectName, Transform keep)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null || child.name != objectName || child == keep)
                continue;
            DestroyObject(child.gameObject);
        }
    }

    void WireTabButtons()
    {
        if (inventoryTabButton != null)
        {
            inventoryTabButton.onClick.RemoveAllListeners();
            inventoryTabButton.onClick.AddListener(OnInventoryClicked);
            inventoryTabButton.transition = Selectable.Transition.None;
            if (inventoryTabButton.targetGraphic == null)
                inventoryTabButton.targetGraphic = inventoryBg;
        }

        if (managementTabButton != null)
        {
            managementTabButton.onClick.RemoveAllListeners();
            managementTabButton.onClick.AddListener(OnManagementClicked);
            managementTabButton.transition = Selectable.Transition.None;
            if (managementTabButton.targetGraphic == null)
                managementTabButton.targetGraphic = managementBg;
        }
    }

    Image CreateTab(string objectName, string label)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(transform, false);

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = TabWidth;
        le.preferredWidth = TabWidth;
        le.flexibleWidth = 1f;
        le.minHeight = TabHeight;
        le.preferredHeight = TabHeight;

        var img = go.GetComponent<Image>();
        img.color = IdleColor;
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8f, 2f);
        textRt.offsetMax = new Vector2(-8f, -2f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.enableAutoSizing = false;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        return img;
    }

    void OnInventoryClicked()
    {
        var inv = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inv != null)
        {
            inv.TogglePanel();
            return;
        }

        // Fallback: invoke the legacy scene button if InventoryUI is missing.
        var source = FindInventoryButton();
        if (source != null)
            source.onClick.Invoke();
    }

    void OnManagementClicked()
    {
        var mgmt = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (mgmt != null)
        {
            mgmt.Toggle();
            return;
        }

        var source = FindManagementButton();
        if (source != null)
            source.onClick.Invoke();
    }

    void HideSourceButtons()
    {
        var inventorySource = FindInventoryButton();
        var managementSource = FindManagementButton();

        if (inventorySource != null)
            inventorySource.gameObject.SetActive(false);
        if (managementSource != null)
            managementSource.gameObject.SetActive(false);
    }

    static void HideManagementPageTitle()
    {
        var panel = GameObject.Find("ManagementPanel");
        if (panel == null) return;
        var title = panel.transform.Find("ContentBox/Title");
        if (title != null)
            title.gameObject.SetActive(false);
    }

    void RefreshVisuals()
    {
        var inv = FindFirstObjectByType<InventoryUI>();
        var mgmt = FindFirstObjectByType<ManagementScreenController>();
        bool inventoryOpen = inv != null && inv.IsPanelOpen;
        bool managementOpen = mgmt != null && mgmt.IsOpen;

        if (inventoryBg != null)
            inventoryBg.color = inventoryOpen ? ActiveColor : IdleColor;
        if (managementBg != null)
            managementBg.color = managementOpen ? ActiveColor : IdleColor;
    }

    static Button FindInventoryButton()
    {
        var ui = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (ui != null && ui.inventoryButton != null)
            return ui.inventoryButton;

        var named = GameObject.Find("InventoryButton");
        return named != null ? named.GetComponent<Button>() : null;
    }

    static Button FindManagementButton()
    {
        var ui = FindFirstObjectByType<ManagementScreenController>(FindObjectsInactive.Include);
        if (ui != null && ui.openButton != null)
            return ui.openButton;

        var named = GameObject.Find("ManagementButton");
        return named != null ? named.GetComponent<Button>() : null;
    }

    static void DestroyObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
