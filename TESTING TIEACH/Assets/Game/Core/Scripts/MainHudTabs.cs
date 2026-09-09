using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-left Inventory / Management tabs. Builds clean equal-sized buttons so
/// scene leftovers (auto-size text, 400x70 buttons) cannot overlap.
/// </summary>
public class MainHudTabs : MonoBehaviour
{
    public const string StripName = "MainHudTabs";

    const float TabWidth = 168f;
    const float TabHeight = 40f;
    const float TabSpacing = 10f;

    static readonly Color StripColor = new Color(0.06f, 0.06f, 0.09f, 0.96f);
    static readonly Color IdleColor = new Color(0.16f, 0.17f, 0.22f, 1f);
    static readonly Color ActiveColor = new Color(0.32f, 0.40f, 0.52f, 1f);

    Button inventorySource;
    Button managementSource;
    Image inventoryBg;
    Image managementBg;
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

        var go = new GameObject(StripName, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(MainHudTabs));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();

        var tabs = go.GetComponent<MainHudTabs>();
        tabs.Build();
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
    }

    public void Build()
    {
        inventorySource = FindInventoryButton();
        managementSource = FindManagementButton();

        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16f, -8f);
        rt.sizeDelta = new Vector2(TabWidth * 2f + TabSpacing + 16f, TabHeight + 12f);

        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = StripColor;
        bg.raycastTarget = true;

        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.spacing = TabSpacing;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        if (!built)
        {
            ClearGeneratedTabs();
            inventoryBg = CreateTab("InventoryTab", "Inventory", OnInventoryClicked);
            managementBg = CreateTab("ManagementTab", "Management", OnManagementClicked);
            built = true;
        }

        HideSourceButtons();
        HideManagementPageTitle();
        RefreshVisuals();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    void ClearGeneratedTabs()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == "InventoryTab" || child.name == "ManagementTab")
                Destroy(child.gameObject);
        }
    }

    Image CreateTab(string objectName, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(RectMask2D));
        go.transform.SetParent(transform, false);

        var le = go.GetComponent<LayoutElement>();
        le.minWidth = TabWidth;
        le.preferredWidth = TabWidth;
        le.flexibleWidth = 0f;
        le.minHeight = TabHeight;
        le.preferredHeight = TabHeight;

        var img = go.GetComponent<Image>();
        img.color = IdleColor;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

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
        if (inventorySource != null)
            inventorySource.onClick.Invoke();
    }

    void OnManagementClicked()
    {
        if (managementSource != null)
            managementSource.onClick.Invoke();
    }

    void HideSourceButtons()
    {
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
}
