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
        if (milestone == null) return;
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
        instance.StartCoroutine(instance.ShowWhenReady(milestone));
    }

    IEnumerator ShowWhenReady(MilestoneDefinition milestone)
    {
        // Milestone completion dialogue runs first. Show the new rewards after Gus's cutscene.
        yield return null;
        while (CutscenePresentation.IsActive || IntroCutsceneUI.IsPlaying)
            yield return null;

        Canvas canvas = ResolvePlayerCanvas();
        if (canvas == null) yield break;
        Build(canvas.transform, milestone);
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

    void Build(Transform canvas, MilestoneDefinition milestone)
    {
        if (popupRoot != null) Destroy(popupRoot);
        popupRoot = new GameObject("MilestoneUnlockPopup", typeof(RectTransform), typeof(Image));
        popupRoot.transform.SetParent(canvas, false);
        popupRoot.transform.SetAsLastSibling();

        RectTransform overlayRect = popupRoot.transform as RectTransform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlay = popupRoot.GetComponent<Image>();
        overlay.color = new Color(0.015f, 0.02f, 0.03f, 0.78f);
        overlay.raycastTarget = true;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(popupRoot.transform, false);
        RectTransform panelRect = panel.transform as RectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        RectTransform canvasRect = canvas as RectTransform;
        Vector2 canvasSize = canvasRect != null && canvasRect.rect.size.sqrMagnitude > 0.01f
            ? canvasRect.rect.size : new Vector2(Screen.width, Screen.height);
        panelRect.sizeDelta = new Vector2(
            Mathf.Min(760f, canvasSize.x * 0.92f),
            Mathf.Min(520f, canvasSize.y * 0.9f));
        panel.GetComponent<Image>().color = GameUITheme.PanelSlate;

        MilestoneProgressManager manager = MilestoneProgressManager.Instance;
        string milestoneLabel = manager != null
            ? manager.GetMilestoneDisplayLabel(milestone)
            : milestone.displayName;
        CreateText(panel.transform, "Overline", "NEW MILESTONE", 13f, FontStyles.Bold,
            new Vector2(36f, -48f), new Vector2(-36f, -22f), GameUITheme.Accent,
            TextAlignmentOptions.Left);
        CreateText(panel.transform, "Title", milestoneLabel, 27f, FontStyles.Bold,
            new Vector2(36f, -92f), new Vector2(-36f, -50f), GameUITheme.TextPrimary,
            TextAlignmentOptions.Left);
        CreateText(panel.transform, "Description", milestone.description, 15f, FontStyles.Normal,
            new Vector2(36f, -158f), new Vector2(-36f, -105f), GameUITheme.TextSecondary,
            TextAlignmentOptions.Left);
        CreateText(panel.transform, "UnlockHeader", "UNLOCKED", 15f, FontStyles.Bold,
            new Vector2(36f, -205f), new Vector2(-36f, -174f), GameUITheme.Accent,
            TextAlignmentOptions.Left);

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
        TextMeshProUGUI rewardText = CreateText(panel.transform, "UnlockList", rewards.ToString(),
            18f, FontStyles.Normal, new Vector2(44f, -410f), new Vector2(-36f, -218f),
            GameUITheme.TextPrimary, TextAlignmentOptions.TopLeft);
        rewardText.enableAutoSizing = true;
        rewardText.fontSizeMin = 14f;
        rewardText.fontSizeMax = 18f;
        rewardText.overflowMode = TextOverflowModes.Ellipsis;

        GameObject buttonObject = new GameObject("ContinueButton", typeof(RectTransform),
            typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(panel.transform, false);
        RectTransform buttonRect = buttonObject.transform as RectTransform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 24f);
        buttonRect.sizeDelta = new Vector2(230f, 50f);
        buttonObject.GetComponent<Image>().color = GameUITheme.Positive;
        buttonObject.GetComponent<Button>().onClick.AddListener(Close);
        TextMeshProUGUI buttonLabel = CreateText(buttonObject.transform, "Label", "CONTINUE", 17f,
            FontStyles.Bold, Vector2.zero, Vector2.zero, GameUITheme.TextPrimary,
            TextAlignmentOptions.Center);
        buttonLabel.rectTransform.anchorMin = Vector2.zero;
        buttonLabel.rectTransform.anchorMax = Vector2.one;
        buttonLabel.rectTransform.offsetMin = Vector2.zero;
        buttonLabel.rectTransform.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        FontStyles style, Vector2 min, Vector2 max, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.transform as RectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = min;
        rect.offsetMax = max;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
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
