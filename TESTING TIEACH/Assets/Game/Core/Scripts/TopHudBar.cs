using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top HUD bar for PlayerUI. Keeps money and time centered while exposing compact
/// icon actions on the right side of the shared HUD strip.
/// </summary>
public class TopHudBar : MonoBehaviour
{
    public const string BarObjectName = "TopHudBar";
    public const string BackdropObjectName = "TopHudConnectedBackdrop";
    public const string TimeSectionName = "TimeSection";
    public const string MoneyTextName = "MoneyText";
    public const string HudActionsName = "HudActions";
    const float BarWidth = 760f;
    const int StoreReputationMaxStars = 5;
    const int StoreReputationMaxPoints = 750;
    const int PointsPerCompletedOrder = 1;
    const int PointsPerDailyTask = 10;
    const int PointsPerMilestone = 50;
    static readonly int[] StoreReputationStarThresholds = { 50, 150, 300, 500, 750 };

    [Header("References")]
    public MoneyManager money;
    public TextMeshProUGUI moneyText;
    public GameTimeUI timeUI;

    [Header("Layout")]
    public float barHeight = 56f;

    TextMeshProUGUI displayedMoneyText;
    int displayedMoney;
    Button storeReputationButton;
    Image[] storeReputationStars;
    GameObject storeReputationPopup;
    TextMeshProUGUI storeReputationBreakdown;
    int displayedStoreReputationScore = -1;
    int displayedStoreReputationOrders = -1;
    int displayedStoreReputationDailyTasks = -1;
    int displayedStoreReputationMilestones = -1;
    float nextStoreReputationRefresh;

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
        EnsureHudActions();
        ApplyFitLayout();
        RemoveUndoFromBar();
        EnsureUtilityControls();
    }

    void Update()
    {
        if (money == null)
            money = FindFirstObjectByType<MoneyManager>();
        if (money != null && moneyText != null)
        {
            int amount = money.CurrentMoney;
            if (displayedMoneyText != moneyText || displayedMoney != amount)
            {
                moneyText.text = "$" + amount;
                displayedMoneyText = moneyText;
                displayedMoney = amount;
            }
        }

        if (Time.unscaledTime >= nextStoreReputationRefresh)
        {
            nextStoreReputationRefresh = Time.unscaledTime + 0.5f;
            RefreshStoreReputation();
        }
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
            existing.EnsureHudActions();
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
        // 760 fits between the 550-wide left and right tab strips at 1920
        // without either side covering the money or notification controls.
        rt.sizeDelta = new Vector2(BarWidth, barHeight);

        EnsureConnectedBackdrop(rt.parent, Mathf.Max(60f, barHeight));

        var bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        // The full-width sibling supplies the shared background. Keep this
        // graphic as a raycast surface without tinting the center a second time.
        bg.color = Color.clear;
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
            le.minWidth = 76f;
            le.preferredWidth = 76f;
            le.flexibleWidth = 0f;
            PositionSection(moneySection, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(168f, 0f), new Vector2(76f, barHeight - 16f));
        }

        var gusHelpSection = transform.Find(TopHudUtilityControls.GusHelpSectionName) as RectTransform;
        if (gusHelpSection != null)
        {
            var le = gusHelpSection.GetComponent<LayoutElement>() ?? gusHelpSection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            PositionSection(gusHelpSection, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(42f, 42f));
        }

        var guidebookSection = transform.Find(TopHudUtilityControls.GuidebookSectionName) as RectTransform;
        if (guidebookSection != null)
        {
            var le = guidebookSection.GetComponent<LayoutElement>() ?? guidebookSection.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            PositionSection(guidebookSection, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(70f, 0f), new Vector2(42f, 42f));
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

        var hudActions = FindHudActions();
        if (hudActions != null)
        {
            var le = hudActions.GetComponent<LayoutElement>() ?? hudActions.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            le.minWidth = 240f;
            le.preferredWidth = 240f;
            le.flexibleWidth = 0f;
            hudActions.anchorMin = new Vector2(1f, 1f);
            hudActions.anchorMax = new Vector2(1f, 1f);
            hudActions.pivot = new Vector2(1f, 1f);
            hudActions.anchoredPosition = new Vector2(-235f, -8f);
            hudActions.sizeDelta = new Vector2(240f, 48f);
        }

        if (moneyText != null)
        {
            moneyText.overflowMode = TextOverflowModes.Truncate;
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

    static void EnsureConnectedBackdrop(Transform canvasTransform, float height)
    {
        if (canvasTransform == null) return;

        Transform existing = canvasTransform.Find(BackdropObjectName);
        GameObject backdrop = existing != null
            ? existing.gameObject
            : new GameObject(BackdropObjectName, typeof(RectTransform), typeof(Image));

        if (backdrop.transform.parent != canvasTransform)
            backdrop.transform.SetParent(canvasTransform, false);

        var rect = (RectTransform)backdrop.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, height);

        var image = backdrop.GetComponent<Image>();
        image.color = HudTabColors.Strip;
        image.raycastTarget = false;

        // It only fills the gaps between the existing controls and must never
        // cover or reposition them.
        backdrop.transform.SetAsFirstSibling();
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

    void EnsureHudActions()
    {
        Transform host = transform.parent != null ? transform.parent : transform;
        Transform existing = host.Find(HudActionsName) ?? transform.Find(HudActionsName);
        GameObject section = existing != null ? existing.gameObject
            : new GameObject(HudActionsName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        if (section.transform.parent != host)
            section.transform.SetParent(host, false);

        var layout = section.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(3, 3, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Button recipes = FindOrCreateHudAction(section.transform, "RecipesButton", "Recipes", GameIconId.MenuList);
        Button research = FindOrCreateHudAction(section.transform, "ResearchButton", "Research", GameIconId.Gear);
        Button analytics = FindOrCreateHudAction(section.transform, "AnalyticsButton", "Analytics", GameIconId.Analytics);
        EnsureStoreReputationDisplay(section.transform, recipes);

        recipes.onClick.RemoveAllListeners();
        recipes.onClick.AddListener(OpenRecipesFromHud);
        research.onClick.RemoveAllListeners();
        research.onClick.AddListener(OpenResearchFromHud);
        analytics.onClick.RemoveAllListeners();
        analytics.onClick.AddListener(OpenAnalyticsFromHud);
    }

    void EnsureStoreReputationDisplay(Transform parent, Button recipesButton)
    {
        if (parent == null) return;
        Transform existing = parent.Find("StoreReputation");
        GameObject root = existing != null ? existing.gameObject
            : new GameObject("StoreReputation", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        if (root.transform.parent != parent)
            root.transform.SetParent(parent, false);

        Image background = root.GetComponent<Image>();
        Button button = root.GetComponent<Button>();
        if (background == null) background = root.AddComponent<Image>();
        if (button == null) button = root.AddComponent<Button>();
        storeReputationButton = button;
        background.color = HudTabColors.Idle;
        background.raycastTarget = true;
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.interactable = true;
        button.onClick.RemoveListener(ToggleStoreReputationPopup);
        button.onClick.AddListener(ToggleStoreReputationPopup);

        LayoutElement element = root.GetComponent<LayoutElement>();
        if (element == null) element = root.AddComponent<LayoutElement>();
        element.minWidth = 78f;
        element.preferredWidth = 78f;
        element.flexibleWidth = 0f;
        element.minHeight = 40f;
        element.preferredHeight = 40f;

        HorizontalLayoutGroup starsLayout = root.GetComponent<HorizontalLayoutGroup>();
        if (starsLayout == null) starsLayout = root.AddComponent<HorizontalLayoutGroup>();
        starsLayout.padding = new RectOffset(3, 3, 2, 2);
        starsLayout.spacing = 1f;
        starsLayout.childAlignment = TextAnchor.MiddleCenter;
        starsLayout.childControlWidth = false;
        starsLayout.childControlHeight = false;
        starsLayout.childForceExpandWidth = false;
        starsLayout.childForceExpandHeight = false;

        storeReputationStars = new Image[StoreReputationMaxStars];
        Sprite starSprite = GameIconLibrary.GetSprite(GameIconId.Star);
        for (int i = 0; i < StoreReputationMaxStars; i++)
        {
            string starName = "Star" + (i + 1);
            Transform starTransform = root.transform.Find(starName);
            GameObject starObject = starTransform != null ? starTransform.gameObject
                : new GameObject(starName, typeof(RectTransform), typeof(Image));
            if (starObject.transform.parent != root.transform)
                starObject.transform.SetParent(root.transform, false);
            RectTransform starRect = (RectTransform)starObject.transform;
            starRect.sizeDelta = new Vector2(13f, 13f);
            Image star = starObject.GetComponent<Image>();
            star.sprite = starSprite;
            star.preserveAspect = true;
            star.raycastTarget = false;
            storeReputationStars[i] = star;
        }

        if (recipesButton != null)
        {
            int rootIndex = root.transform.GetSiblingIndex();
            int recipesIndex = recipesButton.transform.GetSiblingIndex();
            int targetIndex = rootIndex < recipesIndex ? recipesIndex - 1 : recipesIndex;
            if (rootIndex != targetIndex)
                root.transform.SetSiblingIndex(targetIndex);
        }
        DelayedTooltip.Attach(button, "Click to view store reputation factors.");
        EnsureStoreReputationPopup(root.transform);
        RefreshStoreReputation(true);
    }

    void EnsureStoreReputationPopup(Transform parent)
    {
        if (parent == null) return;
        Transform existing = parent.Find("StoreReputationPopup");
        storeReputationPopup = existing != null ? existing.gameObject
            : new GameObject("StoreReputationPopup", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
        if (storeReputationPopup.transform.parent != parent)
            storeReputationPopup.transform.SetParent(parent, false);

        RectTransform popupRect = (RectTransform)storeReputationPopup.transform;
        popupRect.anchorMin = Vector2.zero;
        popupRect.anchorMax = Vector2.zero;
        popupRect.pivot = new Vector2(0f, 1f);
        popupRect.anchoredPosition = new Vector2(0f, -6f);
        popupRect.sizeDelta = new Vector2(280f, 174f);

        LayoutElement popupElement = storeReputationPopup.GetComponent<LayoutElement>();
        popupElement.ignoreLayout = true;
        Image popupBackground = storeReputationPopup.GetComponent<Image>();
        popupBackground.color = new Color(0.12f, 0.14f, 0.18f, 0.98f);
        popupBackground.raycastTarget = true;

        VerticalLayoutGroup layout = storeReputationPopup.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Transform titleTransform = storeReputationPopup.transform.Find("Title");
        if (titleTransform == null)
        {
            GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            titleObject.transform.SetParent(storeReputationPopup.transform, false);
            TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;
            title.text = "STORE REPUTATION";
            title.fontSize = 15f;
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.78f, 0.30f, 1f);
            title.raycastTarget = false;
            titleObject.GetComponent<LayoutElement>().preferredHeight = 22f;
        }

        Transform detailsTransform = storeReputationPopup.transform.Find("Breakdown");
        if (detailsTransform == null)
        {
            GameObject detailsObject = new GameObject("Breakdown", typeof(RectTransform), typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            detailsObject.transform.SetParent(storeReputationPopup.transform, false);
            storeReputationBreakdown = detailsObject.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) storeReputationBreakdown.font = TMP_Settings.defaultFontAsset;
            storeReputationBreakdown.fontSize = 13f;
            storeReputationBreakdown.color = Color.white;
            storeReputationBreakdown.textWrappingMode = TextWrappingModes.Normal;
            storeReputationBreakdown.raycastTarget = false;
            detailsObject.GetComponent<LayoutElement>().flexibleHeight = 1f;
        }
        else
        {
            storeReputationBreakdown = detailsTransform.GetComponent<TextMeshProUGUI>();
        }

        if (existing == null)
            storeReputationPopup.SetActive(false);
        storeReputationPopup.transform.SetAsLastSibling();
    }

    void ToggleStoreReputationPopup()
    {
        if (storeReputationPopup == null) return;
        storeReputationPopup.SetActive(!storeReputationPopup.activeSelf);
        RefreshStoreReputation(true);
        Sfx.Play(storeReputationPopup.activeSelf ? SfxId.UiOpen : SfxId.UiClick);
    }

    void RefreshStoreReputation(bool force = false)
    {
        if (storeReputationStars == null || storeReputationStars.Length == 0) return;
        StoreStatisticsManager statistics = StoreStatisticsManager.Instance;
        int servedOrders = statistics != null ? statistics.CareerOrdersServed : 0;
        int dailyTasks = statistics != null ? statistics.CareerDailyTasksCompleted : 0;
        MilestoneProgressManager milestones = MilestoneProgressManager.Instance;
        int completedMilestones = milestones != null ? milestones.CompletedNumberedMilestoneCount : 0;
        int score = servedOrders * PointsPerCompletedOrder
            + dailyTasks * PointsPerDailyTask
            + completedMilestones * PointsPerMilestone;
        if (!force && score == displayedStoreReputationScore
            && servedOrders == displayedStoreReputationOrders
            && dailyTasks == displayedStoreReputationDailyTasks
            && completedMilestones == displayedStoreReputationMilestones) return;
        displayedStoreReputationScore = score;
        displayedStoreReputationOrders = servedOrders;
        displayedStoreReputationDailyTasks = dailyTasks;
        displayedStoreReputationMilestones = completedMilestones;

        int starsEarned = GetStoreReputationStars(score);
        Color earned = new Color(1f, 0.78f, 0.30f, 1f);
        Color unearned = new Color(0.31f, 0.37f, 0.44f, 0.8f);
        for (int i = 0; i < storeReputationStars.Length; i++)
            if (storeReputationStars[i] != null)
                storeReputationStars[i].color = i < starsEarned ? earned : unearned;

        if (storeReputationBreakdown != null)
        {
            int cappedScore = Mathf.Min(StoreReputationMaxPoints, score);
            int progress = starsEarned >= StoreReputationMaxStars
                ? 0 : Mathf.Max(0, cappedScore - (starsEarned > 0 ? StoreReputationStarThresholds[starsEarned - 1] : 0));
            int needed = starsEarned >= StoreReputationMaxStars
                ? 0 : StoreReputationStarThresholds[starsEarned] - (starsEarned > 0 ? StoreReputationStarThresholds[starsEarned - 1] : 0);
            string nextStar = starsEarned >= StoreReputationMaxStars
                ? "All 5 stars earned."
                : "Next star: " + progress + "/" + needed + " points";
            storeReputationBreakdown.text = "Orders served      " + servedOrders + " × 1 = " + servedOrders
                + "\nDaily tasks           " + dailyTasks + " × " + PointsPerDailyTask + " = " + (dailyTasks * PointsPerDailyTask)
                + "\nMilestones           " + completedMilestones + " × " + PointsPerMilestone + " = " + (completedMilestones * PointsPerMilestone)
                + "\n\nReputation: " + cappedScore + "/" + StoreReputationMaxPoints + " points"
                + "\n" + nextStar;
        }
    }

    static int GetStoreReputationStars(int score)
    {
        int stars = 0;
        int cappedScore = Mathf.Clamp(score, 0, StoreReputationMaxPoints);
        for (int i = 0; i < StoreReputationStarThresholds.Length; i++)
        {
            if (cappedScore < StoreReputationStarThresholds[i]) break;
            stars++;
        }
        return stars;
    }

    RectTransform FindHudActions()
    {
        Transform host = transform.parent != null ? transform.parent : transform;
        return host.Find(HudActionsName) as RectTransform
            ?? transform.Find(HudActionsName) as RectTransform;
    }

    static Button FindOrCreateHudAction(Transform parent, string objectName, string label, GameIconId icon)
    {
        Transform existing = parent.Find(objectName);
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = labelGo.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 12f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        }

        var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 42f;
        layout.preferredWidth = 42f;
        layout.flexibleWidth = 0f;
        layout.minHeight = 40f;
        layout.preferredHeight = 40f;
        button.GetComponent<Image>().color = GameUITheme.Surface;
        GameIconLibrary.ApplyToButton(button, icon, 23f, 0f, true);
        GameUITheme.ApplyCompactControlEffects(button);
        DelayedTooltip.Attach(button, label + ": open");
        return button;
    }

    void OpenRecipesFromHud()
    {
        Sfx.Play(SfxId.UiClick);
        // Recipes are a utility overlay. Keep the current Build or Business
        // page underneath instead of switching the player into Supply first.
        IngredientsOrderUI ingredients = FindFirstObjectByType<IngredientsOrderUI>(FindObjectsInactive.Include);
        if (ingredients != null)
            ingredients.OpenRecipesFromHud();
    }

    void OpenResearchFromHud()
    {
        Sfx.Play(SfxId.UiClick);
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            ResearchTreeUI.Open(canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform);
    }

    void OpenAnalyticsFromHud()
    {
        Sfx.Play(SfxId.UiClick);
        // Analytics is its own overlay, like Recipes and Research. Do not route
        // through the Business controller, which can change the active page.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
            AnalyticsPopupUI.Open(canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform);
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
        le.minWidth = 76f;
        le.preferredWidth = 76f;
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
        tmp.overflowMode = TextOverflowModes.Truncate;
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
