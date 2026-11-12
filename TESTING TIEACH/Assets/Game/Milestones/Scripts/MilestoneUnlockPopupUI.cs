using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MilestoneUnlockPopupUI : MonoBehaviour
{
    const string PauseSource = "MilestoneUnlockPopup";
    static MilestoneUnlockPopupUI instance;

    GameObject popupRoot;
    bool isVisible;
    System.Action tutorialContinueCallback;

    public static RectTransform ActivePanelRect
    {
        get
        {
            if (instance == null || !instance.isVisible || instance.popupRoot == null) return null;
            return instance.popupRoot.transform.Find("Panel") as RectTransform;
        }
    }

    public static void ShowAfterTransition(MilestoneDefinition milestone)
    {
        MilestoneUnlockPopupUI popup = GetOrCreateInstance(milestone);
        if (popup == null) return;
        popup.StartCoroutine(popup.ShowWhenReady(milestone));
    }

    public static void ShowDetails(MilestoneDefinition milestone)
    {
        MilestoneUnlockPopupUI popup = GetOrCreateInstance(milestone);
        if (popup == null) return;
        popup.ShowImmediately(milestone, false);
    }

    public static bool ShowTutorialUnlocks(MilestoneDefinition tutorialMilestone,
        System.Action onContinue = null)
    {
        MilestoneUnlockPopupUI popup = GetOrCreateInstance(tutorialMilestone);
        if (popup == null) return false;
        return popup.ShowImmediately(tutorialMilestone, true, onContinue);
    }

    static MilestoneUnlockPopupUI GetOrCreateInstance(MilestoneDefinition milestone)
    {
        if (milestone == null) return null;
        if (instance == null)
        {
            MilestoneProgressManager manager = MilestoneProgressManager.Instance;
            instance = manager != null
                ? manager.GetComponent<MilestoneUnlockPopupUI>()
                : FindFirstObjectByType<MilestoneUnlockPopupUI>(FindObjectsInactive.Include);
            if (instance == null)
            {
                GameObject host = manager != null ? manager.gameObject : new GameObject("MilestoneUnlockPresentation");
                instance = host.AddComponent<MilestoneUnlockPopupUI>();
            }
        }
        return instance;
    }

    IEnumerator ShowWhenReady(MilestoneDefinition milestone)
    {
        // Milestone completion dialogue runs first. Show the new rewards after Gus's cutscene.
        yield return null;
        while (CutscenePresentation.IsActive || IntroCutsceneUI.IsPlaying)
            yield return null;

        Canvas canvas = ResolvePlayerCanvas();
        if (canvas == null) yield break;
        ShowImmediately(milestone, true);
    }

    bool ShowImmediately(MilestoneDefinition milestone, bool newlyUnlocked,
        System.Action onTutorialContinue = null)
    {
        tutorialContinueCallback = onTutorialContinue;
        Canvas canvas = ResolvePlayerCanvas();
        if (canvas == null)
        {
            tutorialContinueCallback = null;
            return false;
        }
        Build(canvas.transform, milestone, newlyUnlocked);
        GameTimeManager.Instance?.RequestExternalPause(PauseSource);
        isVisible = true;
        return true;
    }

    static Canvas ResolvePlayerCanvas()
    {
        GameObject playerUi = GameObject.Find("PlayerUI");
        if (playerUi != null)
        {
            Canvas canvas = playerUi.GetComponent<Canvas>();
            if (canvas != null) return canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        }

        TopHudBar hud = FindFirstObjectByType<TopHudBar>(FindObjectsInactive.Include);
        if (hud != null)
        {
            Canvas canvas = hud.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas.GetComponent<IntroCutsceneUI>() != null) continue;
            return canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        }
        return null;
    }

    void Build(Transform canvas, MilestoneDefinition milestone, bool newlyUnlocked)
    {
        if (popupRoot != null) Destroy(popupRoot);
        popupRoot = new GameObject("MilestoneUnlockPopup", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image));
        popupRoot.transform.SetParent(canvas, false);
        popupRoot.transform.SetAsLastSibling();

        // Player menus can live on separate canvases with their own sorting
        // order. Give the modal its own top-level order so it cannot be
        // partially covered and look like a stray milestone banner.
        Canvas popupCanvas = popupRoot.GetComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 7000;

        RectTransform overlayRect = popupRoot.transform as RectTransform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlay = popupRoot.GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.02f, 0.03f, 0.78f);
        overlay.raycastTarget = true;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
            typeof(Outline), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(popupRoot.transform, false);
        RectTransform panelRect = panel.transform as RectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        RectTransform canvasRect = canvas as RectTransform;
        Vector2 canvasSize = canvasRect != null && canvasRect.rect.size.sqrMagnitude > 0.01f
            ? canvasRect.rect.size : new Vector2(Screen.width, Screen.height);
        panelRect.sizeDelta = new Vector2(
            Mathf.Min(760f, canvasSize.x * 0.9f),
            Mathf.Min(560f, canvasSize.y * 0.86f));
        panel.GetComponent<Image>().color = GameUITheme.PanelSlate;
        Outline panelBorder = panel.GetComponent<Outline>();
        panelBorder.effectColor = GameUITheme.Accent;
        panelBorder.effectDistance = new Vector2(2f, -2f);
        panelBorder.useGraphicAlpha = false;

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        // Match the planning panel's 18 px inset below Start Shift.
        panelLayout.padding = new RectOffset(32, 32, 28, 18);
        panelLayout.spacing = 10f;
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        MilestoneProgressManager manager = MilestoneProgressManager.Instance;
        string milestoneLabel = manager != null
            ? manager.GetMilestoneDisplayLabel(milestone)
            : milestone.displayName;
        string overline = newlyUnlocked
            ? milestone.isTutorial ? "TUTORIAL UNLOCK" : "NEW MILESTONE"
            : "MILESTONE DETAILS";
        CreateLayoutText(panel.transform, "Overline", overline, 14f,
            FontStyles.Bold, 28f, GameUITheme.Accent, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "Title", milestoneLabel, 30f,
            FontStyles.Bold, 48f, GameUITheme.TextPrimary, TextAlignmentOptions.Left);
        SplitDescriptionAndTopics(milestone.description, out string description, out string topics);
        TextMeshProUGUI descriptionText = CreateLayoutText(panel.transform, "Description", description, 18f,
            FontStyles.Normal, 50f, GameUITheme.TextPrimary, TextAlignmentOptions.Left);
        descriptionText.enableAutoSizing = true;
        descriptionText.fontSizeMin = 16f;
        descriptionText.fontSizeMax = 18f;
        if (!string.IsNullOrWhiteSpace(topics))
        {
            CreateLayoutText(panel.transform, "FocusHeader", "FOCUS AREAS", 16f,
                FontStyles.Bold, 26f, GameUITheme.Accent, TextAlignmentOptions.Left);
            string[] topicParts = topics.Split(',');
            var topicMarkup = new StringBuilder();
            for (int i = 0; i < topicParts.Length; i++)
            {
                string topic = topicParts[i].Trim();
                if (topic.StartsWith("and ", System.StringComparison.OrdinalIgnoreCase))
                    topic = topic.Substring(4).Trim();
                if (topic.Length == 0) continue;
                if (topicMarkup.Length > 0) topicMarkup.Append("   <color=#D8B365>•</color>   ");
                topicMarkup.Append("<color=#F2EEE7>").Append(ToTitleCaseTopic(topic)).Append("</color>");
            }
            TextMeshProUGUI topicText = CreateLayoutText(panel.transform, "FocusTopics",
                topicMarkup.ToString(), 17f, FontStyles.Normal, 36f,
                GameUITheme.TextSecondary, TextAlignmentOptions.Left);
            topicText.enableAutoSizing = true;
            topicText.fontSizeMin = 15f;
            topicText.fontSizeMax = 17f;
        }
        CreateLayoutText(panel.transform, "UnlockHeader", newlyUnlocked ? "UNLOCKED" : "UNLOCKS", 16f,
            FontStyles.Bold, 26f, GameUITheme.Accent, TextAlignmentOptions.Left);

        System.Collections.Generic.IReadOnlyList<string> unlocks =
            MilestoneFeatures.GetUnlocksForMilestone(milestone);
        var rewardList = new GameObject("UnlockList", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        rewardList.transform.SetParent(panel.transform, false);
        VerticalLayoutGroup rewardLayout = rewardList.GetComponent<VerticalLayoutGroup>();
        rewardLayout.spacing = 3f;
        rewardLayout.childAlignment = TextAnchor.UpperLeft;
        rewardLayout.childControlWidth = true;
        rewardLayout.childControlHeight = true;
        rewardLayout.childForceExpandWidth = true;
        rewardLayout.childForceExpandHeight = false;
        LayoutElement rewardListSize = rewardList.GetComponent<LayoutElement>();
        float rewardListHeight = Mathf.Max(70f, unlocks.Count * 33f);
        rewardListSize.minHeight = rewardListHeight;
        rewardListSize.preferredHeight = rewardListHeight;
        rewardListSize.flexibleHeight = 0f;

        for (int i = 0; i < unlocks.Count; i++)
        {
            if (string.Equals(unlocks[i], "Express ingredient ordering", System.StringComparison.OrdinalIgnoreCase))
            {
                CreateExpressUnlockRow(rewardList.transform, unlocks[i]);
                continue;
            }
            TextMeshProUGUI rewardLine = CreateLayoutText(rewardList.transform,
                "Unlock_" + i, "<color=#D8B365>•</color>  " + unlocks[i], 19f,
                FontStyles.Normal, 30f, GameUITheme.TextPrimary, TextAlignmentOptions.Left);
            rewardLine.enableAutoSizing = true;
            rewardLine.fontSizeMin = 16f;
            rewardLine.fontSizeMax = 19f;
        }
        if (unlocks.Count == 0)
            CreateLayoutText(rewardList.transform, "NoUnlocks",
                "No additional content unlocks at this milestone.", 16f,
                FontStyles.Normal, 32f, GameUITheme.TextSecondary, TextAlignmentOptions.Left);

        // Keep the action near the panel bottom while allowing the rewards to
        // claim their full height first. This flexible gap never overlaps text.
        GameObject footerSpace = new GameObject("FooterSpace", typeof(RectTransform), typeof(LayoutElement));
        footerSpace.transform.SetParent(panel.transform, false);
        footerSpace.GetComponent<LayoutElement>().minHeight = 54f;
        footerSpace.GetComponent<LayoutElement>().preferredHeight = 54f;

        GameObject buttonObject = new GameObject("ContinueButton", typeof(RectTransform),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(panel.transform, false);
        LayoutElement buttonSize = buttonObject.GetComponent<LayoutElement>();
        buttonSize.ignoreLayout = true;
        RectTransform buttonRect = buttonObject.transform as RectTransform;
        buttonRect.anchorMin = new Vector2(0f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.offsetMin = new Vector2(32f, 18f);
        buttonRect.offsetMax = new Vector2(-32f, 66f);
        buttonObject.GetComponent<Image>().color = GameUITheme.Positive;
        Outline buttonBorder = buttonObject.AddComponent<Outline>();
        buttonBorder.effectColor = Color.white;
        buttonBorder.effectDistance = new Vector2(1.5f, -1.5f);
        buttonBorder.useGraphicAlpha = false;
        buttonObject.GetComponent<Button>().onClick.AddListener(ContinueFromPopup);
        TextMeshProUGUI buttonLabel = CreateLayoutText(buttonObject.transform, "Label", "CONTINUE", 19f,
            FontStyles.Bold, 48f, GameUITheme.TextPrimary, TextAlignmentOptions.Center);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = Vector2.zero;
        buttonLabel.rectTransform.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        float desiredHeight = LayoutUtility.GetPreferredHeight(panelRect);
        float maxHeight = Mathf.Min(560f, canvasSize.y * 0.86f);
        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Min(maxHeight, desiredHeight + panelLayout.padding.top + panelLayout.padding.bottom));
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    static string ToTitleCaseTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return topic;
        string[] words = topic.Trim().ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0 || words[i] == "and") continue;
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        }
        return string.Join(" ", words);
    }

    static void SplitDescriptionAndTopics(string source, out string description, out string topics)
    {
        description = source ?? string.Empty;
        topics = string.Empty;
        int marker = description.IndexOf("\n\nTopics:", System.StringComparison.OrdinalIgnoreCase);
        int markerLength = 9;
        if (marker < 0)
        {
            marker = description.IndexOf("\nTopics:", System.StringComparison.OrdinalIgnoreCase);
            markerLength = 8;
        }
        if (marker < 0) return;
        int valueStart = marker + markerLength;
        topics = description.Substring(valueStart).Trim();
        description = description.Substring(0, marker).Trim();
    }

    void CreateExpressUnlockRow(Transform parent, string label)
    {
        GameObject row = new GameObject("ExpressOrderingUnlock", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        LayoutElement rowSize = row.GetComponent<LayoutElement>();
        rowSize.minHeight = rowSize.preferredHeight = 36f;

        TextMeshProUGUI rewardLine = CreateLayoutText(row.transform, "Label",
            "<color=#D8B365>•</color>  " + label, 17f, FontStyles.Normal, 32f,
            GameUITheme.TextPrimary, TextAlignmentOptions.Left);
        rewardLine.GetComponent<LayoutElement>().flexibleWidth = 1f;
        rewardLine.enableAutoSizing = true;
        rewardLine.fontSizeMin = 14f;
        rewardLine.fontSizeMax = 17f;

        GameObject buttonObject = new GameObject("DetailsButton", typeof(RectTransform),
            typeof(Image), typeof(Button), typeof(Outline), typeof(LayoutElement));
        buttonObject.transform.SetParent(row.transform, false);
        LayoutElement buttonSize = buttonObject.GetComponent<LayoutElement>();
        buttonSize.minWidth = buttonSize.preferredWidth = 102f;
        buttonSize.minHeight = buttonSize.preferredHeight = 32f;
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = GameUITheme.RaisedSlate;
        Outline buttonBorder = buttonObject.GetComponent<Outline>();
        buttonBorder.effectColor = Color.white;
        buttonBorder.effectDistance = new Vector2(1f, -1f);
        buttonBorder.useGraphicAlpha = false;
        Button button = buttonObject.GetComponent<Button>();
        GameUITheme.ApplyButton(button);
        button.onClick.AddListener(ShowExpressOrderingDetails);

        TextMeshProUGUI buttonLabel = CreateLayoutText(buttonObject.transform, "Label", "DETAILS", 12f,
            FontStyles.Bold, 32f, GameUITheme.TextPrimary, TextAlignmentOptions.Center);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = Vector2.zero;
        buttonLabel.rectTransform.offsetMax = Vector2.zero;
    }

    void ShowExpressOrderingDetails()
    {
        if (popupRoot == null || popupRoot.transform.Find("ExpressOrderingDetails") != null) return;
        GameObject overlay = new GameObject("ExpressOrderingDetails", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(popupRoot.transform, false);
        overlay.transform.SetAsLastSibling();
        RectTransform overlayRect = overlay.transform as RectTransform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.03f, 0.72f);

        GameObject panel = new GameObject("DetailsPanel", typeof(RectTransform), typeof(Image),
            typeof(Outline), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(overlay.transform, false);
        RectTransform panelRect = panel.transform as RectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(Mathf.Min(560f, Screen.width * 0.9f),
            Mathf.Min(440f, Screen.height * 0.9f));
        panel.GetComponent<Image>().color = GameUITheme.PanelSlate;
        Outline border = panel.GetComponent<Outline>();
        border.effectColor = GameUITheme.Accent;
        border.effectDistance = new Vector2(2f, -2f);
        border.useGraphicAlpha = false;
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 24, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateLayoutText(panel.transform, "Overline", "UNLOCKED FEATURE", 12f,
            FontStyles.Bold, 20f, GameUITheme.Accent, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "Title", "Express Ingredient Ordering", 24f,
            FontStyles.Bold, 36f, GameUITheme.TextPrimary, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "Description",
            "Send the ingredients in your current order cart by express delivery. It skips the regular delivery wait and adds a $200 fee to the ingredient cost.",
            16f, FontStyles.Normal, 70f, GameUITheme.TextSecondary, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "HowToHeader", "HOW TO USE", 13f,
            FontStyles.Bold, 22f, GameUITheme.Accent, TextAlignmentOptions.Left);
        TextMeshProUGUI steps = CreateLayoutText(panel.transform, "Steps",
            "1. Open Ingredients and add the packs you need to the cart.\n2. Select <b>Express</b> instead of <b>Place Order</b>.\n3. The van is dispatched immediately, without the regular delivery countdown.",
            15f, FontStyles.Normal, 92f, GameUITheme.TextPrimary, TextAlignmentOptions.TopLeft);
        steps.enableAutoSizing = true;
        steps.fontSizeMin = 13f;
        steps.fontSizeMax = 15f;

        GameObject footerSpacer = new GameObject("FooterSpacer", typeof(RectTransform), typeof(LayoutElement));
        footerSpacer.transform.SetParent(panel.transform, false);
        footerSpacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

        GameObject closeObject = new GameObject("CloseButton", typeof(RectTransform),
            typeof(Image), typeof(Button), typeof(Outline), typeof(LayoutElement));
        closeObject.transform.SetParent(panel.transform, false);
        LayoutElement closeSize = closeObject.GetComponent<LayoutElement>();
        closeSize.minWidth = closeSize.preferredWidth = 150f;
        closeSize.minHeight = closeSize.preferredHeight = 42f;
        closeSize.flexibleWidth = 0f;
        closeObject.GetComponent<Image>().color = GameUITheme.Positive;
        Outline closeBorder = closeObject.GetComponent<Outline>();
        closeBorder.effectColor = Color.white;
        closeBorder.effectDistance = new Vector2(1.25f, -1.25f);
        closeBorder.useGraphicAlpha = false;
        closeObject.GetComponent<Button>().onClick.AddListener(() => Destroy(overlay));
        TextMeshProUGUI closeLabel = CreateLayoutText(closeObject.transform, "Label", "GOT IT", 14f,
            FontStyles.Bold, 42f, GameUITheme.TextPrimary, TextAlignmentOptions.Center);
        closeLabel.rectTransform.anchorMin = Vector2.zero;
        closeLabel.rectTransform.anchorMax = Vector2.one;
        closeLabel.rectTransform.offsetMin = Vector2.zero;
        closeLabel.rectTransform.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    static TextMeshProUGUI CreateLayoutText(Transform parent, string name, string value, float size,
        FontStyles style, float preferredHeight, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        return text;
    }

    void Update()
    {
        if (isVisible && tutorialContinueCallback == null && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void ContinueFromPopup()
    {
        System.Action onContinue = tutorialContinueCallback;
        tutorialContinueCallback = null;
        Close();
        onContinue?.Invoke();
    }

    void Close()
    {
        tutorialContinueCallback = null;
        if (popupRoot != null)
        {
            popupRoot.SetActive(false);
            Destroy(popupRoot);
        }
        popupRoot = null;
        isVisible = false;
        GameTimeManager.Instance?.ReleaseExternalPause(PauseSource);
    }

    void OnDestroy()
    {
        GameTimeManager.Instance?.ReleaseExternalPause(PauseSource);
        if (instance == this) instance = null;
    }
}
