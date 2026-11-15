using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Schedules three milestone-specific Gus knowledge checks during each shift.</summary>
public sealed class GusShiftQuestionManager : MonoBehaviour
{
    const int QuestionsPerShift = 3;
    const int CorrectAnswerReward = 50;

    public static GusShiftQuestionManager Instance { get; private set; }

    int scheduledDay;
    float[] scheduledMinutes = System.Array.Empty<float>();
    int[] questionOrder = System.Array.Empty<int>();
    int questionCursor;
    int pendingQuestionIndex = -1;
    bool questionResolved;
    bool questionVisible;
    string visibleQuestionId;
    System.Random questionRandom;
    readonly List<int> answerOrder = new List<int>();

    GameObject overlay;
    GameObject canvasRoot;
    GameObject answerRoot;
    TextMeshProUGUI titleText;
    TextMeshProUGUI milestoneText;
    TextMeshProUGUI promptText;
    TextMeshProUGUI feedbackText;
    Button skipButton;
    Button continueButton;
    readonly List<Button> answerButtons = new List<Button>();

    public int ScheduledDayForSave => scheduledDay;
    public float ScheduledMinuteForSave => questionCursor < scheduledMinutes.Length
        ? scheduledMinutes[questionCursor] : 0f;
    public int QuestionIndexForSave => pendingQuestionIndex;
    public bool AskedThisShiftForSave => questionCursor > 0;
    public bool QuestionPendingForSave => pendingQuestionIndex >= 0;
    public float[] ScheduledMinutesForSave => (float[])scheduledMinutes.Clone();
    public int[] QuestionOrderForSave => (int[])questionOrder.Clone();
    public int QuestionCursorForSave => questionCursor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
    }

    public static GusShiftQuestionManager EnsureInstance()
    {
        if (Instance != null) return Instance;
        Instance = FindFirstObjectByType<GusShiftQuestionManager>(FindObjectsInactive.Include);
        if (Instance != null) return Instance;
        return new GameObject(nameof(GusShiftQuestionManager)).AddComponent<GusShiftQuestionManager>();
    }

    public static void RestoreState(int day, float[] savedMinutes, int[] savedOrder,
        int cursor, int pendingIndex)
    {
        GusShiftQuestionManager manager = EnsureInstance();
        manager.scheduledDay = Mathf.Max(0, day);
        manager.scheduledMinutes = savedMinutes != null
            ? (float[])savedMinutes.Clone() : System.Array.Empty<float>();
        manager.questionOrder = savedOrder != null
            ? (int[])savedOrder.Clone() : System.Array.Empty<int>();
        manager.questionCursor = Mathf.Clamp(cursor, 0, QuestionsPerShift);
        manager.pendingQuestionIndex = pendingIndex;
        manager.questionResolved = false;
        manager.questionVisible = false;
        manager.visibleQuestionId = null;
        if (pendingIndex < 0 && manager.overlay != null)
            manager.overlay.SetActive(false);
    }

    public static void RestoreLegacyState(int day, int savedQuestionIndex, bool asked, bool pending)
    {
        GusShiftQuestionManager manager = EnsureInstance();
        manager.scheduledDay = Mathf.Max(0, day);
        manager.scheduledMinutes = System.Array.Empty<float>();
        manager.questionOrder = System.Array.Empty<int>();
        manager.questionCursor = asked ? QuestionsPerShift : 0;
        manager.pendingQuestionIndex = pending ? savedQuestionIndex : -1;
        manager.questionResolved = false;
        manager.questionVisible = false;
        manager.visibleQuestionId = null;
        if (manager.pendingQuestionIndex < 0 && manager.overlay != null)
            manager.overlay.SetActive(false);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        questionRandom = new System.Random(System.Environment.TickCount ^ GetInstanceID());
    }

    void OnDestroy()
    {
        if (canvasRoot != null) Destroy(canvasRoot);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        GameTimeManager time = GameTimeManager.Instance;
        if (time == null) return;

        ResetForNewDay(time.CurrentDay);
        if (time.IsShiftOver)
        {
            DismissPendingQuestion();
            return;
        }
        if (OnboardingTutorial.BlocksProgression || !time.IsShiftInProgress)
            return;

        MilestoneDefinition milestone = GetActiveMilestone();
        if (!HasQuestions(milestone)) return;

        if (pendingQuestionIndex >= 0)
        {
            bool nextQuestionIsDue = questionCursor < scheduledMinutes.Length
                && time.CurrentMinutes >= scheduledMinutes[questionCursor];
            if (nextQuestionIsDue)
                DismissPendingQuestion();
            else if (IsValidQuestion(milestone, pendingQuestionIndex))
            {
                ShowQuestion(milestone, milestone.shiftQuestions[pendingQuestionIndex]);
                return;
            }
            else
                DismissPendingQuestion();
        }

        if (questionCursor >= QuestionsPerShift) return;
        if (!HasValidSchedule(time.CurrentDay, milestone))
            ScheduleForCurrentShift(time, milestone);
        if (questionCursor < scheduledMinutes.Length
            && time.CurrentMinutes >= scheduledMinutes[questionCursor])
            AskNextQuestion(milestone);
    }

    void ResetForNewDay(int currentDay)
    {
        if (scheduledDay == currentDay) return;
        scheduledDay = currentDay;
        scheduledMinutes = System.Array.Empty<float>();
        questionOrder = System.Array.Empty<int>();
        questionCursor = 0;
        pendingQuestionIndex = -1;
        questionResolved = false;
        questionVisible = false;
        visibleQuestionId = null;
        if (overlay != null) overlay.SetActive(false);
    }

    void ScheduleForCurrentShift(GameTimeManager time, MilestoneDefinition milestone)
    {
        List<int> validQuestions = new List<int>();
        for (int i = 0; i < milestone.shiftQuestions.Count; i++)
            if (IsValidQuestion(milestone, i)) validQuestions.Add(i);

        scheduledDay = time.CurrentDay;
        questionCursor = 0;
        if (validQuestions.Count < QuestionsPerShift)
        {
            scheduledMinutes = System.Array.Empty<float>();
            questionOrder = System.Array.Empty<int>();
            questionCursor = QuestionsPerShift;
            return;
        }

        for (int i = validQuestions.Count - 1; i > 0; i--)
        {
            int swapIndex = questionRandom.Next(0, i + 1);
            int swapValue = validQuestions[i];
            validQuestions[i] = validQuestions[swapIndex];
            validQuestions[swapIndex] = swapValue;
        }
        questionOrder = new int[QuestionsPerShift];
        for (int i = 0; i < QuestionsPerShift; i++) questionOrder[i] = validQuestions[i];

        int start = Mathf.RoundToInt(time.dayStartHour * 60f);
        int end = Mathf.RoundToInt(time.dayEndHour * 60f);
        int earliest = Mathf.Max(start + 60, Mathf.CeilToInt(time.CurrentMinutes + 30f));
        int latest = end - 60;
        if (earliest > latest)
        {
            scheduledMinutes = System.Array.Empty<float>();
            questionCursor = QuestionsPerShift;
            return;
        }

        scheduledMinutes = new float[QuestionsPerShift];
        int windowLength = latest - earliest + 1;
        for (int i = 0; i < QuestionsPerShift; i++)
        {
            int windowStart = earliest + windowLength * i / QuestionsPerShift;
            int windowEndExclusive = earliest + windowLength * (i + 1) / QuestionsPerShift;
            if (windowEndExclusive <= windowStart) windowEndExclusive = windowStart + 1;
            scheduledMinutes[i] = questionRandom.Next(windowStart, windowEndExclusive);
        }
    }

    bool HasValidSchedule(int day, MilestoneDefinition milestone)
    {
        if (scheduledDay != day || scheduledMinutes == null || scheduledMinutes.Length != QuestionsPerShift
            || questionOrder == null || questionOrder.Length != QuestionsPerShift)
            return false;

        for (int i = 0; i < QuestionsPerShift; i++)
            if (scheduledMinutes[i] <= 0f || !IsValidQuestion(milestone, questionOrder[i]))
                return false;
        return true;
    }

    void AskNextQuestion(MilestoneDefinition milestone)
    {
        if (questionCursor >= questionOrder.Length) return;
        pendingQuestionIndex = questionOrder[questionCursor];
        questionCursor++;
        ShowQuestion(milestone, milestone.shiftQuestions[pendingQuestionIndex]);
    }

    void ShowQuestion(MilestoneDefinition milestone, MilestoneDefinition.ShiftQuestion question)
    {
        string questionId = question.questionId ?? string.Empty;
        if (questionVisible && overlay != null && overlay.activeSelf
            && visibleQuestionId == questionId) return;
        EnsurePanel();
        if (overlay == null) return;

        questionResolved = false;
        titleText.text = "Gus has a question";
        milestoneText.text = MilestoneProgressManager.Instance != null
            ? MilestoneProgressManager.Instance.GetMilestoneDisplayLabel(milestone)
            : milestone.displayName;
        promptText.text = question.prompt;
        feedbackText.text = string.Empty;
        answerRoot.SetActive(true);
        skipButton.gameObject.SetActive(true);
        continueButton.gameObject.SetActive(false);

        for (int i = 0; i < answerButtons.Count; i++)
        {
            Button button = answerButtons[i];
            if (button != null) Destroy(button.gameObject);
        }
        answerButtons.Clear();
        answerOrder.Clear();
        for (int i = 0; i < question.answers.Length; i++) answerOrder.Add(i);
        for (int i = answerOrder.Count - 1; i > 0; i--)
        {
            int swapIndex = questionRandom.Next(0, i + 1);
            int swapValue = answerOrder[i];
            answerOrder[i] = answerOrder[swapIndex];
            answerOrder[swapIndex] = swapValue;
        }
        for (int i = 0; i < answerOrder.Count; i++)
        {
            int answerIndex = answerOrder[i];
            Button button = CreateButton(answerRoot.transform, "Answer" + i,
                question.answers[answerIndex], 44f, GameUITheme.Surface,
                () => SelectAnswer(question, answerIndex));
            answerButtons.Add(button);
        }

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        questionVisible = true;
        visibleQuestionId = questionId;
        Sfx.Play(SfxId.UiOpen);
    }

    void SelectAnswer(MilestoneDefinition.ShiftQuestion question, int answerIndex)
    {
        if (questionResolved) return;
        questionResolved = true;

        bool correct = answerIndex == question.correctAnswerIndex;
        pendingQuestionIndex = -1;
        if (correct)
        {
            FindFirstObjectByType<MoneyManager>()?.AddMoney(CorrectAnswerReward);
            feedbackText.text = "Correct! +$50\n" + question.explanation;
            feedbackText.color = GameUITheme.PositiveAccent;
            Sfx.Play(SfxId.EarnMoney);
        }
        else
        {
            string correctAnswer = question.answers[question.correctAnswerIndex];
            feedbackText.text = "Not quite. " + correctAnswer + "\n" + question.explanation;
            feedbackText.color = GameUITheme.TextPrimary;
            Sfx.Play(SfxId.UiClick);
        }

        for (int i = 0; i < answerButtons.Count; i++)
        {
            Button button = answerButtons[i];
            if (button == null) continue;
            button.interactable = false;
            Image image = button.GetComponent<Image>();
            int displayedAnswer = i < answerOrder.Count ? answerOrder[i] : -1;
            if (image != null && displayedAnswer == question.correctAnswerIndex)
                image.color = GameUITheme.Positive;
            else if (image != null && displayedAnswer == answerIndex)
                image.color = GameUITheme.Danger;
        }
        skipButton.gameObject.SetActive(false);
        continueButton.gameObject.SetActive(true);
    }

    void SkipQuestion()
    {
        CloseQuestion();
    }

    void CloseQuestion()
    {
        DismissPendingQuestion();
        Sfx.Play(SfxId.UiClick);
    }

    void DismissPendingQuestion()
    {
        pendingQuestionIndex = -1;
        questionResolved = false;
        if (overlay != null) overlay.SetActive(false);
        questionVisible = false;
        visibleQuestionId = null;
    }

    static MilestoneDefinition GetActiveMilestone()
    {
        return MilestoneProgressManager.Instance != null
            ? MilestoneProgressManager.Instance.GetActiveMilestone()
            : null;
    }

    static bool HasQuestions(MilestoneDefinition milestone)
    {
        return milestone != null && !milestone.isTutorial
            && milestone.shiftQuestions != null && milestone.shiftQuestions.Count > 0;
    }

    static bool IsValidQuestion(MilestoneDefinition milestone, int index)
    {
        if (!HasQuestions(milestone) || index < 0 || index >= milestone.shiftQuestions.Count) return false;
        MilestoneDefinition.ShiftQuestion question = milestone.shiftQuestions[index];
        return question != null && !string.IsNullOrWhiteSpace(question.prompt)
            && question.answers != null && question.answers.Length >= 2
            && question.correctAnswerIndex >= 0 && question.correctAnswerIndex < question.answers.Length;
    }

    void EnsurePanel()
    {
        if (overlay != null) return;
        Canvas canvas = ResolvePlayerUICanvas();
        if (canvas == null) return;

        canvasRoot = new GameObject("GusShiftQuestionCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas questionCanvas = canvasRoot.GetComponent<Canvas>();
        questionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        questionCanvas.overrideSorting = true;
        Canvas sourceRootCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        questionCanvas.sortingLayerID = sourceRootCanvas.sortingLayerID;
        questionCanvas.sortingOrder = 4200;
        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;

        canvasRoot.transform.SetParent(sourceRootCanvas.transform, false);
        RectTransform canvasRect = (RectTransform)canvasRoot.transform;
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        overlay = new GameObject("Panel", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        overlay.transform.SetParent(canvasRoot.transform, false);
        RectTransform cardRect = (RectTransform)overlay.transform;
        cardRect.anchorMin = new Vector2(0.5f, 0f);
        cardRect.anchorMax = new Vector2(0.5f, 0f);
        cardRect.pivot = new Vector2(0.5f, 0f);
        cardRect.anchoredPosition = new Vector2(0f, 24f);
        cardRect.sizeDelta = new Vector2(560f, 0f);
        Image background = overlay.GetComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.985f);
        background.raycastTarget = false;

        VerticalLayoutGroup layout = overlay.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = overlay.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject header = new GameObject("GusHeader", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.transform.SetParent(overlay.transform, false);
        LayoutElement headerElement = header.GetComponent<LayoutElement>();
        headerElement.minHeight = headerElement.preferredHeight = 60f;
        HorizontalLayoutGroup headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        GameObject portraitFrame = new GameObject("GusIconFrame", typeof(RectTransform),
            typeof(Image), typeof(LayoutElement), typeof(Outline));
        portraitFrame.transform.SetParent(header.transform, false);
        portraitFrame.GetComponent<Image>().color = GameUITheme.Surface;
        portraitFrame.GetComponent<Image>().raycastTarget = false;
        LayoutElement portraitLayout = portraitFrame.GetComponent<LayoutElement>();
        portraitLayout.minWidth = portraitLayout.preferredWidth = 58f;
        portraitLayout.minHeight = portraitLayout.preferredHeight = 58f;
        Outline portraitOutline = portraitFrame.GetComponent<Outline>();
        portraitOutline.effectColor = GameUITheme.Accent;
        portraitOutline.effectDistance = new Vector2(2f, -2f);

        GameObject portrait = new GameObject("GusFace", typeof(RectTransform), typeof(RawImage),
            typeof(GusCutscenePreview));
        portrait.transform.SetParent(portraitFrame.transform, false);
        RectTransform portraitRect = (RectTransform)portrait.transform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(3f, 3f);
        portraitRect.offsetMax = new Vector2(-3f, -3f);
        RawImage portraitImage = portrait.GetComponent<RawImage>();
        portraitImage.color = Color.white;
        portraitImage.raycastTarget = false;
        TitleScreenController titleController = FindFirstObjectByType<TitleScreenController>();
        GameObject gusModel = titleController != null && titleController.gusPrefab != null
            ? titleController.gusPrefab
            : Resources.Load<GameObject>("Prefabs/character_default");
        if (gusModel != null)
            portrait.GetComponent<GusCutscenePreview>().Configure(gusModel, portraitImage, true);

        GameObject headerText = new GameObject("GusHeaderText", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        headerText.transform.SetParent(header.transform, false);
        headerText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        VerticalLayoutGroup headerTextLayout = headerText.GetComponent<VerticalLayoutGroup>();
        headerTextLayout.spacing = 1f;
        headerTextLayout.childAlignment = TextAnchor.MiddleLeft;
        headerTextLayout.childControlWidth = true;
        headerTextLayout.childControlHeight = true;
        headerTextLayout.childForceExpandWidth = true;
        headerTextLayout.childForceExpandHeight = false;

        milestoneText = CreateText(headerText.transform, "Milestone", "Milestone", 14f, 18f,
            TextAlignmentOptions.Left, FontStyles.Bold);
        milestoneText.color = GameUITheme.Accent;
        titleText = CreateText(headerText.transform, "Title", "Gus has a question", 22f, 28f,
            TextAlignmentOptions.Left, FontStyles.Bold);
        titleText.color = GameUITheme.TextPrimary;

        promptText = CreateText(overlay.transform, "Prompt", "", 19f, 58f,
            TextAlignmentOptions.Left, FontStyles.Normal);
        promptText.color = new Color(0.92f, 0.94f, 0.98f);
        promptText.alignment = TextAlignmentOptions.TopLeft;

        answerRoot = new GameObject("Answers", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(LayoutElement));
        answerRoot.transform.SetParent(overlay.transform, false);
        answerRoot.GetComponent<LayoutElement>().preferredHeight = 150f;
        VerticalLayoutGroup answersLayout = answerRoot.GetComponent<VerticalLayoutGroup>();
        answersLayout.spacing = 6f;
        answersLayout.childControlWidth = true;
        answersLayout.childControlHeight = true;
        answersLayout.childForceExpandWidth = true;
        answersLayout.childForceExpandHeight = false;

        feedbackText = CreateText(overlay.transform, "Feedback", "", 16f, 58f,
            TextAlignmentOptions.Left, FontStyles.Normal);
        feedbackText.color = GameUITheme.TextPrimary;

        GameObject actions = new GameObject("Actions", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        actions.transform.SetParent(overlay.transform, false);
        actions.GetComponent<LayoutElement>().preferredHeight = 46f;
        HorizontalLayoutGroup actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 12f;
        actionsLayout.childAlignment = TextAnchor.MiddleCenter;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;

        skipButton = CreateButton(actions.transform, "Skip", "Skip", 44f,
            GameUITheme.Surface, SkipQuestion);
        continueButton = CreateButton(actions.transform, "Continue", "Continue", 44f,
            GameUITheme.Positive, CloseQuestion);
        continueButton.gameObject.SetActive(false);
        GameUITheme.ApplyToCanvas(questionCanvas);
        overlay.SetActive(false);
    }

    static Canvas ResolvePlayerUICanvas()
    {
        GameObject playerUI = GameObject.Find("PlayerUI");
        if (playerUI != null)
        {
            Canvas canvas = playerUI.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas canvas in canvases)
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                return canvas;
        return null;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        float height, TextAlignmentOptions alignment, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = GameUITheme.TextPrimary;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(Transform parent, string name, string label, float height,
        Color color, UnityEngine.Events.UnityAction callback)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(callback);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(go.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = label;
        text.fontSize = 17f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = GameUITheme.TextPrimary;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return button;
    }
}
