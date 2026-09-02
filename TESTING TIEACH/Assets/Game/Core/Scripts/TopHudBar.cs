using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top HUD bar for PlayerUI. Holds money, time controls, and room for more widgets.
/// </summary>
public class TopHudBar : MonoBehaviour
{
    public const string BarObjectName = "TopHudBar";
    public const string TimeSectionName = "TimeSection";
    public const string MoneyTextName = "MoneyText";

    [Header("References")]
    public MoneyManager money;
    public TextMeshProUGUI moneyText;
    public GameTimeUI timeUI;

    [Header("Layout")]
    public float barHeight = 56f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInScene();
    }

    void Awake()
    {
        if (money == null)
            money = FindFirstObjectByType<MoneyManager>();
        BindReferences();
    }

    void Update()
    {
        if (money == null)
            money = FindFirstObjectByType<MoneyManager>();
        if (money != null && moneyText != null)
            moneyText.text = "$" + money.CurrentMoney;
    }

    public void BindReferences()
    {
        if (moneyText == null)
        {
            var moneySection = transform.Find("MoneySection");
            if (moneySection != null)
            {
                var textT = moneySection.Find(MoneyTextName);
                if (textT != null)
                    moneyText = textT.GetComponent<TextMeshProUGUI>();
            }
        }

        if (timeUI == null)
        {
            var timeSection = transform.Find(TimeSectionName);
            if (timeSection != null)
                timeUI = timeSection.GetComponent<GameTimeUI>();
        }
    }

    public static TopHudBar EnsureInScene()
    {
        var existing = FindFirstObjectByType<TopHudBar>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.BindReferences();
            return existing;
        }

        var canvas = FindPlayerUICanvas();
        if (canvas == null) return null;

        return CreateInCanvas(canvas.transform);
    }

    public static TopHudBar CreateInCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null) return null;

        var legacyTime = canvasTransform.Find("GameTimeBar");
        if (legacyTime != null)
            Destroy(legacyTime.gameObject);

        var barGo = new GameObject(BarObjectName, typeof(RectTransform));
        barGo.transform.SetParent(canvasTransform, false);

        var rt = (RectTransform)barGo.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 56f);

        var bg = barGo.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        bg.raycastTarget = true;

        var hlg = barGo.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 8, 8);
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        var bar = barGo.AddComponent<TopHudBar>();

        CreateMoneySection(barGo.transform);
        CreateSpacer(barGo.transform, "LeftSpacer");
        CreateTimeSection(barGo.transform);
        CreateSpacer(barGo.transform, "RightSpacer");

        bar.BindReferences();
        return bar;
    }

    static void CreateMoneySection(Transform parent)
    {
        var section = new GameObject("MoneySection", typeof(RectTransform));
        section.transform.SetParent(parent, false);
        var le = section.AddComponent<LayoutElement>();
        le.minWidth = 140f;
        le.preferredWidth = 140f;
        le.flexibleWidth = 0f;

        var moneyTextGo = new GameObject(MoneyTextName, typeof(RectTransform));
        moneyTextGo.transform.SetParent(section.transform, false);
        var textRt = (RectTransform)moneyTextGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var tmp = moneyTextGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "$0";
        tmp.fontSize = 24;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.55f, 0.95f, 0.55f, 1f);
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
    }

    static void CreateSpacer(Transform parent, string name)
    {
        var spacer = new GameObject(name, typeof(RectTransform));
        spacer.transform.SetParent(parent, false);
        var le = spacer.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 10f;
    }

    static void CreateTimeSection(Transform parent)
    {
        var section = new GameObject(TimeSectionName, typeof(RectTransform));
        section.transform.SetParent(parent, false);
        var le = section.AddComponent<LayoutElement>();
        le.minWidth = 360f;
        le.preferredWidth = 420f;
        le.flexibleWidth = 0f;

        if (section.GetComponent<GameTimeUI>() == null)
            section.AddComponent<GameTimeUI>();
    }

    static Canvas FindPlayerUICanvas()
    {
        var named = GameObject.Find("PlayerUI");
        if (named != null)
        {
            var canvas = named.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c != null && c.isRootCanvas && !c.gameObject.name.Contains("Title"))
                return c;
        }

        return null;
    }
}
