using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top HUD bar for PlayerUI. Wraps money and time controls in a compact centered strip.
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
        ApplyFitLayout();
        RemoveUndoFromBar();
        EnsureUtilityControls();
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
            existing.ApplyFitLayout();
            existing.RemoveUndoFromBar();
            existing.EnsureUtilityControls();
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

        var bar = barGo.AddComponent<TopHudBar>();
        CreateMoneySection(barGo.transform);
        CreateTimeSection(barGo.transform);
        bar.BindReferences();
        bar.ApplyFitLayout();
        return bar;
    }

    public void ApplyFitLayout()
    {
        DestroyIfPresent("LeftSpacer");
        DestroyIfPresent("RightSpacer");

        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(860f, barHeight);

        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.09f, 0.96f);
        bg.raycastTarget = true;

        var mask = GetComponent<RectMask2D>();
        if (mask != null)
            Destroy(mask);

        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
        // Sections are anchored explicitly so variable-width utility text cannot
        // push the clock away from the screen center.
        hlg.enabled = false;

        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var moneySection = transform.Find("MoneySection") as RectTransform;
        if (moneySection != null)
        {
            var le = moneySection.GetComponent<LayoutElement>();
            if (le == null) le = moneySection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.minWidth = 90f;
            le.preferredWidth = 110f;
            le.flexibleWidth = 0f;
            PositionSection(moneySection, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(110f, barHeight - 16f));
        }

        var timeSection = transform.Find(TimeSectionName) as RectTransform;
        if (timeSection != null)
        {
            var le = timeSection.GetComponent<LayoutElement>();
            if (le == null) le = timeSection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.minWidth = 170f;
            le.preferredWidth = 170f;
            le.flexibleWidth = 0f;
            PositionSection(timeSection, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(170f, barHeight - 16f));
        }

        if (moneyText != null)
        {
            moneyText.overflowMode = TextOverflowModes.Ellipsis;
            moneyText.alignment = TextAlignmentOptions.MidlineLeft;
            moneyText.enableAutoSizing = true;
            moneyText.fontSizeMin = 18;
            moneyText.fontSizeMax = 24;
        }


        var musicSection = transform.Find(TopHudUtilityControls.MusicSectionName) as RectTransform;
        if (musicSection != null)
        {
            var le = musicSection.GetComponent<LayoutElement>() ?? musicSection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.minWidth = 206f;
            le.preferredWidth = 206f;
            le.flexibleWidth = 0f;
            PositionSection(musicSection, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-116f, 0f), new Vector2(206f, barHeight - 16f));
        }

        var noticeSection = transform.Find(TopHudUtilityControls.NotificationSectionName) as RectTransform;
        if (noticeSection != null)
        {
            var le = noticeSection.GetComponent<LayoutElement>() ?? noticeSection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.minWidth = 92f;
            le.preferredWidth = 92f;
            le.flexibleWidth = 0f;
            PositionSection(noticeSection, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(92f, barHeight - 16f));
        }
    }

    static void PositionSection(RectTransform section, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        section.anchorMin = anchor;
        section.anchorMax = anchor;
        section.pivot = pivot;
        section.anchoredPosition = position;
        section.sizeDelta = size;
    }

    void EnsureUtilityControls()
    {
        var controls = GetComponent<TopHudUtilityControls>();
        if (controls == null)
            controls = gameObject.AddComponent<TopHudUtilityControls>();
        controls.EnsureLayout();
    }

    public void RemoveUndoFromBar()
    {
        DestroyIfPresent("UndoSection");
    }

    void DestroyIfPresent(string childName)
    {
        var child = transform.Find(childName);
        if (child != null)
            Destroy(child.gameObject);
    }

    static void CreateMoneySection(Transform parent)
    {
        var section = new GameObject("MoneySection", typeof(RectTransform));
        section.transform.SetParent(parent, false);
        var le = section.AddComponent<LayoutElement>();
        le.minWidth = 90f;
        le.preferredWidth = 110f;
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
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
    }

    static void CreateTimeSection(Transform parent)
    {
        var section = new GameObject(TimeSectionName, typeof(RectTransform));
        section.transform.SetParent(parent, false);
        var le = section.AddComponent<LayoutElement>();
        le.minWidth = 320f;
        le.preferredWidth = 400f;
        le.flexibleWidth = 1f;

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
