using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Frame quiz UI. Opens when the active milestone's missions are all complete.
/// Player must answer every question correctly to advance.
/// </summary>
public class MilestoneQuizUI : MonoBehaviour
{
    public const string PanelName = "MilestoneQuizPanel";

    public GameObject panelRoot;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statusText;
    public Transform questionList;
    public Button submitButton;
    public Button closeButton;

    readonly List<ToggleGroup> questionGroups = new List<ToggleGroup>();
    readonly List<int> correctIndexes = new List<int>();

    void Start()
    {
        EnsurePanel();
        Hide();

        var milestones = MilestoneProgressManager.Instance;
        if (milestones != null)
        {
            milestones.OnQuizReady += Show;
            milestones.OnMilestonesChanged += OnMilestonesChanged;
        }
    }

    void OnDestroy()
    {
        var milestones = MilestoneProgressManager.Instance;
        if (milestones != null)
        {
            milestones.OnQuizReady -= Show;
            milestones.OnMilestonesChanged -= OnMilestonesChanged;
        }
    }

    void OnMilestonesChanged()
    {
        var milestones = MilestoneProgressManager.Instance;
        if (milestones != null && milestones.IsQuizReady && panelRoot != null && !panelRoot.activeSelf)
            Show();
    }

    public void Show()
    {
        EnsurePanel();
        BuildQuestions();
        if (panelRoot != null)
            panelRoot.SetActive(true);
        Sfx.Play(SfxId.UiOpen);
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    void Submit()
    {
        if (!AllAnswersCorrect(out int wrongCount))
        {
            if (statusText != null)
                statusText.text = wrongCount == 1
                    ? "Not quite — 1 answer is incorrect. Try again."
                    : $"Not quite — {wrongCount} answers are incorrect. Try again.";
            statusText.color = new Color(0.95f, 0.55f, 0.5f, 1f);
            Sfx.Play(SfxId.UiError);
            return;
        }

        var milestones = MilestoneProgressManager.Instance;
        if (milestones == null || !milestones.TryCompleteActiveQuiz())
        {
            if (statusText != null)
            {
                statusText.text = "Quiz ready, but progress could not advance.";
                statusText.color = new Color(0.95f, 0.55f, 0.5f, 1f);
            }
            return;
        }

        if (statusText != null)
        {
            statusText.text = "Perfect! Milestone unlocked.";
            statusText.color = new Color(0.55f, 0.9f, 0.6f, 1f);
        }

        Sfx.Play(SfxId.MissionComplete);
        Hide();
    }

    bool AllAnswersCorrect(out int wrongCount)
    {
        wrongCount = 0;
        for (int i = 0; i < questionGroups.Count; i++)
        {
            var group = questionGroups[i];
            if (group == null) { wrongCount++; continue; }

            int selected = -1;
            var toggles = group.GetComponentsInChildren<Toggle>(true);
            for (int t = 0; t < toggles.Length; t++)
            {
                if (toggles[t] != null && toggles[t].isOn)
                {
                    selected = t;
                    break;
                }
            }

            if (selected < 0 || selected != correctIndexes[i])
                wrongCount++;
        }
        return wrongCount == 0 && questionGroups.Count > 0;
    }

    void BuildQuestions()
    {
        if (questionList == null) return;

        for (int i = questionList.childCount - 1; i >= 0; i--)
            Destroy(questionList.GetChild(i).gameObject);

        questionGroups.Clear();
        correctIndexes.Clear();

        var milestone = MilestoneProgressManager.Instance != null
            ? MilestoneProgressManager.Instance.GetActiveMilestone()
            : null;
        var quiz = milestone != null ? milestone.quiz : null;

        if (titleText != null)
            titleText.text = quiz != null && !string.IsNullOrEmpty(quiz.title)
                ? quiz.title
                : (milestone != null ? milestone.displayName + " Quiz" : "Milestone Quiz");

        if (statusText != null)
        {
            statusText.color = new Color(0.75f, 0.8f, 0.88f, 1f);
            statusText.text = quiz != null && !string.IsNullOrEmpty(quiz.introText)
                ? quiz.introText
                : "Answer every question correctly to continue.";
        }

        if (quiz == null || quiz.questions == null || quiz.questions.Count == 0)
        {
            // Frame stub: no authored questions yet — allow pass with a confirmation question.
            AddQuestion(
                "Frame stub: this milestone has no quiz content yet. Select Pass to continue.",
                new List<string> { "Pass", "Not yet" },
                0);
            return;
        }

        foreach (var q in quiz.questions)
        {
            if (q == null) continue;
            AddQuestion(q.prompt, q.choices, q.correctChoiceIndex);
        }
    }

    void AddQuestion(string prompt, List<string> choices, int correctIndex)
    {
        var block = new GameObject("Question", typeof(RectTransform));
        block.transform.SetParent(questionList, false);
        var vlg = block.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.padding = new RectOffset(0, 0, 4, 8);
        block.AddComponent<LayoutElement>().minHeight = 80;

        var promptGo = new GameObject("Prompt", typeof(RectTransform));
        promptGo.transform.SetParent(block.transform, false);
        var promptTmp = promptGo.AddComponent<TextMeshProUGUI>();
        promptTmp.text = prompt;
        promptTmp.fontSize = 15;
        promptTmp.color = Color.white;
        promptTmp.alignment = TextAlignmentOptions.TopLeft;
        promptTmp.textWrappingMode = TextWrappingModes.Normal;
        if (TMP_Settings.defaultFontAsset != null) promptTmp.font = TMP_Settings.defaultFontAsset;
        promptGo.AddComponent<LayoutElement>().minHeight = 28;

        var group = block.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        if (choices == null) choices = new List<string>();
        for (int i = 0; i < choices.Count; i++)
        {
            var choiceGo = new GameObject("Choice_" + i, typeof(RectTransform));
            choiceGo.transform.SetParent(block.transform, false);
            choiceGo.AddComponent<LayoutElement>().minHeight = 28;
            var img = choiceGo.AddComponent<Image>();
            img.color = new Color(0.2f, 0.23f, 0.3f, 1f);

            var toggle = choiceGo.AddComponent<Toggle>();
            toggle.group = group;
            toggle.targetGraphic = img;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(choiceGo.transform, false);
            var lr = (RectTransform)labelGo.transform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(12, 2);
            lr.offsetMax = new Vector2(-8, -2);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = ((char)('A' + i)) + ". " + choices[i];
            label.fontSize = 14;
            label.color = Color.white;
            label.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        }

        questionGroups.Add(group);
        correctIndexes.Add(Mathf.Clamp(correctIndex, 0, Mathf.Max(0, choices.Count - 1)));
    }

    void EnsurePanel()
    {
        if (panelRoot != null)
        {
            Bind();
            return;
        }

        var canvas = ResolveCanvas();
        if (canvas == null) return;

        var existing = canvas.transform.Find(PanelName);
        if (existing != null)
        {
            panelRoot = existing.gameObject;
            Bind();
            return;
        }

        panelRoot = new GameObject(PanelName, typeof(RectTransform));
        panelRoot.transform.SetParent(canvas.transform, false);
        var overlay = (RectTransform)panelRoot.transform;
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.75f);

        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(panelRoot.transform, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(520f, 560f);
        card.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.18f, 0.98f);

        var cardVlg = card.AddComponent<VerticalLayoutGroup>();
        cardVlg.padding = new RectOffset(24, 24, 24, 20);
        cardVlg.spacing = 12;
        cardVlg.childControlWidth = true;
        cardVlg.childControlHeight = true;
        cardVlg.childForceExpandWidth = true;

        titleText = CreateLabel(card.transform, "Title", "Milestone Quiz", 24);
        titleText.fontStyle = FontStyles.Bold;
        statusText = CreateLabel(card.transform, "Status", "", 14);
        statusText.color = new Color(0.75f, 0.8f, 0.88f, 1f);

        var scrollGo = new GameObject("QuestionScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(card.transform, false);
        scrollGo.AddComponent<LayoutElement>().minHeight = 280;
        scrollGo.GetComponent<LayoutElement>().flexibleHeight = 1;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vpRt = (RectTransform)viewport.transform;
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.02f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var contentVlg = content.AddComponent<VerticalLayoutGroup>();
        contentVlg.spacing = 10;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childControlHeight = true;
        scroll.viewport = vpRt;
        scroll.content = contentRt;
        questionList = content.transform;

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(card.transform, false);
        buttons.AddComponent<LayoutElement>().minHeight = 44;
        var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childForceExpandWidth = true;

        closeButton = CreateButton(buttons.transform, "Close", new Color(0.35f, 0.38f, 0.45f, 1f));
        submitButton = CreateButton(buttons.transform, "Submit", new Color(0.28f, 0.48f, 0.36f, 1f));

        closeButton.onClick.AddListener(Hide);
        submitButton.onClick.AddListener(Submit);
        panelRoot.transform.SetAsLastSibling();
    }

    void Bind()
    {
        if (panelRoot == null) return;
        var card = panelRoot.transform.Find("Card");
        if (card == null) return;
        if (titleText == null) titleText = card.Find("Title")?.GetComponent<TextMeshProUGUI>();
        if (statusText == null) statusText = card.Find("Status")?.GetComponent<TextMeshProUGUI>();
        if (questionList == null)
            questionList = card.Find("QuestionScroll/Viewport/Content");
        if (closeButton == null)
            closeButton = card.Find("Buttons/Close")?.GetComponent<Button>();
        if (submitButton == null)
            submitButton = card.Find("Buttons/Submit")?.GetComponent<Button>();

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
            closeButton.onClick.AddListener(Hide);
        }
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(Submit);
            submitButton.onClick.AddListener(Submit);
        }
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().minHeight = size + 8;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static Button CreateButton(Transform parent, string label, Color color)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().minHeight = 40;
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Canvas ResolveCanvas()
    {
        var named = GameObject.Find("PlayerUI");
        if (named != null)
        {
            var c = named.GetComponent<Canvas>();
            if (c != null) return c;
        }
        return FindFirstObjectByType<Canvas>();
    }
}
