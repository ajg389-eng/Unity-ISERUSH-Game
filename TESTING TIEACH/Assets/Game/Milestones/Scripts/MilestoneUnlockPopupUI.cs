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

    public static void ShowTutorialUnlocks(MilestoneDefinition tutorialMilestone)
    {
        MilestoneUnlockPopupUI popup = GetOrCreateInstance(tutorialMilestone);
        if (popup == null) return;
        popup.ShowImmediately(tutorialMilestone, true);
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

    void ShowImmediately(MilestoneDefinition milestone, bool newlyUnlocked)
    {
        Canvas canvas = ResolvePlayerCanvas();
        if (canvas == null) return;
        Build(canvas.transform, milestone, newlyUnlocked);
        GameTimeManager.Instance?.RequestExternalPause(PauseSource);
        isVisible = true;
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
            typeof(VerticalLayoutGroup));
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

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(32, 32, 28, 28);
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
        CreateLayoutText(panel.transform, "Overline", overline, 13f,
            FontStyles.Bold, 24f, GameUITheme.Accent, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "Title", milestoneLabel, 27f,
            FontStyles.Bold, 42f, GameUITheme.TextPrimary, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "Description", milestone.description, 15f,
            FontStyles.Normal, 58f, GameUITheme.TextSecondary, TextAlignmentOptions.Left);
        CreateLayoutText(panel.transform, "UnlockHeader", newlyUnlocked ? "UNLOCKED" : "UNLOCKS", 15f,
            FontStyles.Bold, 24f, GameUITheme.Accent, TextAlignmentOptions.Left);

        var rewards = new StringBuilder();
        System.Collections.Generic.IReadOnlyList<string> unlocks =
            MilestoneFeatures.GetUnlocksForMilestone(milestone);
        for (int i = 0; i < unlocks.Count; i++)
        {
            if (i > 0) rewards.Append('\n');
            rewards.Append("<color=#E5B84F>•</color> ").Append(unlocks[i]);
        }
        if (rewards.Length == 0)
            rewards.Append("No additional content unlocks at this milestone.");
        TextMeshProUGUI rewardText = CreateLayoutText(panel.transform, "UnlockList", rewards.ToString(),
            18f, FontStyles.Normal, 90f, GameUITheme.TextPrimary, TextAlignmentOptions.TopLeft);
        rewardText.enableAutoSizing = true;
        rewardText.fontSizeMin = 14f;
        rewardText.fontSizeMax = 18f;
        rewardText.overflowMode = TextOverflowModes.Ellipsis;
        rewardText.GetComponent<LayoutElement>().flexibleHeight = 1f;

        GameObject footer = new GameObject("Footer", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        footerLayout.childControlWidth = false;
        footerLayout.childControlHeight = false;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        LayoutElement footerSize = footer.GetComponent<LayoutElement>();
        footerSize.minHeight = footerSize.preferredHeight = 58f;

        GameObject buttonObject = new GameObject("ContinueButton", typeof(RectTransform),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(footer.transform, false);
        LayoutElement buttonSize = buttonObject.GetComponent<LayoutElement>();
        buttonSize.minWidth = buttonSize.preferredWidth = 230f;
        buttonSize.minHeight = buttonSize.preferredHeight = 50f;
        buttonObject.GetComponent<Image>().color = GameUITheme.Positive;
        buttonObject.GetComponent<Button>().onClick.AddListener(Close);
        TextMeshProUGUI buttonLabel = CreateLayoutText(buttonObject.transform, "Label", "CONTINUE", 17f,
            FontStyles.Bold, 50f, GameUITheme.TextPrimary, TextAlignmentOptions.Center);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = Vector2.zero;
        buttonLabel.rectTransform.offsetMax = Vector2.zero;
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
        if (isVisible && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Close()
    {
        if (popupRoot != null) Destroy(popupRoot);
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
