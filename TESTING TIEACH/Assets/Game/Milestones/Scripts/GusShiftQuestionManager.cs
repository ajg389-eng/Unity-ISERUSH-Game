using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Schedules short milestone lessons and related knowledge checks during each shift.</summary>
public sealed class GusShiftQuestionManager : MonoBehaviour
{
    const int LessonsPerShift = 3;
    const int QuestionsPerShift = 3;
    const int ShiftEventsPerShift = LessonsPerShift + QuestionsPerShift;
    const int CorrectAnswerReward = 50;

    public static GusShiftQuestionManager Instance { get; private set; }
    public static event Action PromptStateChanged;
    public static bool ShouldDeferEndOfDay => Instance != null && Instance.HasShiftPromptsRemainingAtEnd();

    int scheduledDay;
    float[] scheduledMinutes = System.Array.Empty<float>();
    int[] questionOrder = System.Array.Empty<int>();
    int questionCursor;
    int pendingQuestionIndex = -1;
    int pendingEventToken = -1;
    int pendingLessonIndex = -1;
    bool questionResolved;
    bool questionVisible;
    bool legacyQuestionSchedule;
    string visibleQuestionId;
    System.Random questionRandom;
    readonly List<int> answerOrder = new List<int>();

    GameObject overlay;
    GameObject canvasRoot;
    GameObject answerRoot;
    RectTransform canvasRect;
    RectTransform targetFrame;
    Image[] targetFrameBars;
    RectTransform calloutArrow;
    Image calloutShaft;
    Image[] calloutHeads;
    RectTransform calloutTarget;
    readonly Vector3[] calloutWorldCorners = new Vector3[4];
    float nextCalloutTargetSearchTime;
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
    public int PendingEventTokenForSave => pendingEventToken;
    public bool LegacyScheduleForSave => legacyQuestionSchedule;
    public bool AskedThisShiftForSave => questionCursor > LessonsPerShift;
    public bool QuestionPendingForSave => pendingQuestionIndex >= 0;
    public float[] ScheduledMinutesForSave => (float[])scheduledMinutes.Clone();
    public int[] QuestionOrderForSave => (int[])questionOrder.Clone();
    public int QuestionCursorForSave => questionCursor;
    public bool HasOpenPrompt => questionVisible && overlay != null && overlay.activeSelf;

    public static bool HasOpenShiftPrompt => Instance != null && Instance.HasOpenPrompt;

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
        manager.pendingEventToken = -1;
        manager.pendingLessonIndex = -1;
        manager.legacyQuestionSchedule = true;
        manager.questionResolved = false;
        manager.questionVisible = false;
        manager.visibleQuestionId = null;
        if (manager.overlay != null) manager.overlay.SetActive(false);
        if (manager.calloutArrow != null) manager.calloutArrow.gameObject.SetActive(false);
        if (manager.targetFrame != null) manager.targetFrame.gameObject.SetActive(false);
    }

    public static void RestoreLegacyState(int day, int savedQuestionIndex, bool asked, bool pending)
    {
        GusShiftQuestionManager manager = EnsureInstance();
        manager.scheduledDay = Mathf.Max(0, day);
        manager.scheduledMinutes = System.Array.Empty<float>();
        manager.questionOrder = System.Array.Empty<int>();
        manager.questionCursor = asked ? QuestionsPerShift : 0;
        manager.pendingQuestionIndex = pending ? savedQuestionIndex : -1;
        manager.pendingEventToken = -1;
        manager.pendingLessonIndex = -1;
        manager.legacyQuestionSchedule = true;
        manager.questionResolved = false;
        manager.questionVisible = false;
        manager.visibleQuestionId = null;
        if (manager.overlay != null) manager.overlay.SetActive(false);
        if (manager.calloutArrow != null) manager.calloutArrow.gameObject.SetActive(false);
        if (manager.targetFrame != null) manager.targetFrame.gameObject.SetActive(false);
    }

    public static void RestoreShiftEventsState(int day, float[] savedMinutes, int[] savedOrder,
        int cursor, int pendingToken)
    {
        GusShiftQuestionManager manager = EnsureInstance();
        manager.scheduledDay = Mathf.Max(0, day);
        manager.scheduledMinutes = savedMinutes != null
            ? (float[])savedMinutes.Clone() : System.Array.Empty<float>();
        manager.questionOrder = savedOrder != null
            ? (int[])savedOrder.Clone() : System.Array.Empty<int>();
        manager.questionCursor = Mathf.Clamp(cursor, 0, ShiftEventsPerShift);
        manager.pendingEventToken = pendingToken >= 0 && pendingToken < ShiftEventsPerShift
            ? pendingToken : -1;
        manager.pendingLessonIndex = manager.pendingEventToken >= 0
            && manager.pendingEventToken < LessonsPerShift ? manager.pendingEventToken : -1;
        manager.pendingQuestionIndex = manager.pendingEventToken >= LessonsPerShift
            && manager.questionOrder.Length >= QuestionsPerShift
            ? manager.questionOrder[manager.pendingEventToken - LessonsPerShift] : -1;
        manager.legacyQuestionSchedule = false;
        manager.questionResolved = false;
        manager.questionVisible = false;
        manager.visibleQuestionId = null;
        if (manager.overlay != null) manager.overlay.SetActive(false);
        if (manager.calloutArrow != null) manager.calloutArrow.gameObject.SetActive(false);
        if (manager.targetFrame != null) manager.targetFrame.gameObject.SetActive(false);
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

    void Start()
    {
        GameTimeManager time = GameTimeManager.Instance;
        if (time != null)
            time.OnShiftEnding += HandleShiftEnding;
    }

    void OnDestroy()
    {
        GameTimeManager time = GameTimeManager.Instance;
        if (time != null)
            time.OnShiftEnding -= HandleShiftEnding;
        if (canvasRoot != null) Destroy(canvasRoot);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        GameTimeManager time = GameTimeManager.Instance;
        if (time == null) return;

        ResetForNewDay(time.CurrentDay);
        if (legacyQuestionSchedule)
        {
            UpdateLegacyQuestionSchedule(time);
            return;
        }

        if (OnboardingTutorial.BlocksProgression
            || (!time.IsShiftInProgress && !time.IsShiftOver))
            return;

        MilestoneDefinition milestone = GetActiveMilestone();
        if (!HasShiftContent(milestone)) return;
        if (!HasValidSchedule(time.CurrentDay, milestone))
            ScheduleForCurrentShift(time, milestone);
        if (!HasValidSchedule(time.CurrentDay, milestone)) return;

        if (pendingEventToken >= 0 && !questionVisible)
        {
            ShowSavedPendingEvent(milestone);
            return;
        }
        if (questionVisible)
        {
            UpdateCallout();
            return;
        }
        ShowNextDueShiftEvent(time, milestone);
    }

    void UpdateLegacyQuestionSchedule(GameTimeManager time)
    {
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
        if (!HasValidLegacySchedule(time.CurrentDay, milestone))
            ScheduleLegacyForCurrentShift(time, milestone);
        if (questionCursor < scheduledMinutes.Length
            && time.CurrentMinutes >= scheduledMinutes[questionCursor])
            AskNextLegacyQuestion(milestone);
    }

    void HandleShiftEnding()
    {
        if (legacyQuestionSchedule || OnboardingTutorial.BlocksProgression) return;
        GameTimeManager time = GameTimeManager.Instance;
        MilestoneDefinition milestone = GetActiveMilestone();
        if (time == null || !HasShiftContent(milestone)) return;
        if (!HasValidSchedule(time.CurrentDay, milestone))
            ScheduleForCurrentShift(time, milestone);
        ShowNextDueShiftEvent(time, milestone);
        PromptStateChanged?.Invoke();
    }

    void ShowSavedPendingEvent(MilestoneDefinition milestone)
    {
        if (pendingEventToken < LessonsPerShift)
        {
            pendingLessonIndex = pendingEventToken;
            if (pendingLessonIndex >= 0 && pendingLessonIndex < milestone.shiftLessons.Count)
                ShowLesson(milestone, milestone.shiftLessons[pendingLessonIndex]);
            else
                DismissPendingQuestion();
            return;
        }

        int questionPosition = pendingEventToken - LessonsPerShift;
        if (questionOrder != null && questionPosition >= 0 && questionPosition < questionOrder.Length)
        {
            pendingQuestionIndex = questionOrder[questionPosition];
            if (IsValidQuestion(milestone, pendingQuestionIndex))
                ShowQuestion(milestone, milestone.shiftQuestions[pendingQuestionIndex]);
            else
                DismissPendingQuestion();
        }
        else
            DismissPendingQuestion();
    }

    void ShowNextDueShiftEvent(GameTimeManager time, MilestoneDefinition milestone)
    {
        if (questionVisible || questionCursor >= ShiftEventsPerShift
            || questionCursor >= scheduledMinutes.Length
            || time.CurrentMinutes < scheduledMinutes[questionCursor])
            return;

        int eventIndex = questionCursor++;
        pendingEventToken = eventIndex;
        if (eventIndex < LessonsPerShift)
        {
            pendingLessonIndex = eventIndex;
            ShowLesson(milestone, milestone.shiftLessons[eventIndex]);
            return;
        }

        int scheduledQuestion = eventIndex - LessonsPerShift;
        pendingLessonIndex = -1;
        pendingQuestionIndex = scheduledQuestion < questionOrder.Length
            ? questionOrder[scheduledQuestion] : -1;
        if (IsValidQuestion(milestone, pendingQuestionIndex))
            ShowQuestion(milestone, milestone.shiftQuestions[pendingQuestionIndex]);
        else
            DismissPendingQuestion();
    }

    void ResetForNewDay(int currentDay)
    {
        if (scheduledDay == currentDay) return;
        scheduledDay = currentDay;
        scheduledMinutes = System.Array.Empty<float>();
        questionOrder = System.Array.Empty<int>();
        questionCursor = 0;
        pendingQuestionIndex = -1;
        pendingEventToken = -1;
        pendingLessonIndex = -1;
        legacyQuestionSchedule = false;
        questionResolved = false;
        questionVisible = false;
        visibleQuestionId = null;
        if (overlay != null) overlay.SetActive(false);
        if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
        if (targetFrame != null) targetFrame.gameObject.SetActive(false);
        PromptStateChanged?.Invoke();
    }

    void ScheduleForCurrentShift(GameTimeManager time, MilestoneDefinition milestone)
    {
        scheduledDay = time.CurrentDay;
        questionCursor = 0;
        pendingEventToken = -1;
        pendingLessonIndex = -1;
        if (!HasShiftContent(milestone))
        {
            scheduledMinutes = System.Array.Empty<float>();
            questionOrder = System.Array.Empty<int>();
            questionCursor = ShiftEventsPerShift;
            return;
        }

        questionOrder = new int[QuestionsPerShift];
        for (int i = 0; i < QuestionsPerShift; i++) questionOrder[i] = i;

        float start = time.dayStartHour * 60f;
        float duration = Mathf.Max(1f, (time.dayEndHour - time.dayStartHour) * 60f);
        scheduledMinutes = new float[ShiftEventsPerShift];
        for (int i = 0; i < ShiftEventsPerShift; i++)
            scheduledMinutes[i] = start + duration * (i + 1) / ShiftEventsPerShift;
    }

    void ScheduleLegacyForCurrentShift(GameTimeManager time, MilestoneDefinition milestone)
    {
        scheduledDay = time.CurrentDay;
        questionCursor = 0;
        if (!HasOrderedQuestions(milestone))
        {
            scheduledMinutes = System.Array.Empty<float>();
            questionOrder = System.Array.Empty<int>();
            questionCursor = QuestionsPerShift;
            return;
        }

        questionOrder = new int[QuestionsPerShift];
        for (int i = 0; i < QuestionsPerShift; i++) questionOrder[i] = i;

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

    bool HasValidLegacySchedule(int day, MilestoneDefinition milestone)
    {
        if (scheduledDay != day || scheduledMinutes == null || scheduledMinutes.Length != QuestionsPerShift
            || questionOrder == null || questionOrder.Length != QuestionsPerShift)
            return false;

        for (int i = 0; i < QuestionsPerShift; i++)
            if (scheduledMinutes[i] <= 0f || questionOrder[i] != i
                || !IsValidQuestion(milestone, questionOrder[i]))
                return false;
        return true;
    }

    bool HasValidSchedule(int day, MilestoneDefinition milestone)
    {
        if (scheduledDay != day || scheduledMinutes == null
            || scheduledMinutes.Length != ShiftEventsPerShift
            || questionOrder == null || questionOrder.Length != QuestionsPerShift
            || !HasLessons(milestone))
            return false;

        for (int i = 0; i < scheduledMinutes.Length; i++)
            if (scheduledMinutes[i] <= 0f) return false;
        for (int i = 0; i < questionOrder.Length; i++)
            if (questionOrder[i] != i || !IsValidQuestion(milestone, questionOrder[i])) return false;
        return true;
    }

    bool HasShiftPromptsRemainingAtEnd()
    {
        GameTimeManager time = GameTimeManager.Instance;
        if (time == null || !time.IsShiftOver || legacyQuestionSchedule)
            return false;
        MilestoneDefinition milestone = GetActiveMilestone();
        return HasShiftContent(milestone)
            && (HasOpenPrompt || pendingEventToken >= 0 || questionCursor < ShiftEventsPerShift);
    }

    static bool HasShiftContent(MilestoneDefinition milestone)
    {
        if (!HasLessons(milestone) || !HasOrderedQuestions(milestone)) return false;
        for (int i = 0; i < LessonsPerShift; i++)
        {
            string lessonId = milestone.shiftLessons[i].lessonId;
            string questionId = milestone.shiftQuestions[i].questionId;
            if (string.IsNullOrWhiteSpace(lessonId)
                || !string.Equals(lessonId, questionId, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    static bool HasOrderedQuestions(MilestoneDefinition milestone)
    {
        if (!HasQuestions(milestone) || milestone.shiftQuestions.Count < QuestionsPerShift)
            return false;
        for (int i = 0; i < QuestionsPerShift; i++)
            if (!IsValidQuestion(milestone, i)) return false;
        return true;
    }

    static bool HasLessons(MilestoneDefinition milestone)
    {
        if (milestone == null || milestone.isTutorial || milestone.shiftLessons == null
            || milestone.shiftLessons.Count < LessonsPerShift)
            return false;

        for (int i = 0; i < LessonsPerShift; i++)
        {
            MilestoneDefinition.ShiftLesson lesson = milestone.shiftLessons[i];
            if (lesson == null || string.IsNullOrWhiteSpace(lesson.title)
                || string.IsNullOrWhiteSpace(lesson.explanation))
                return false;
        }
        return true;
    }

    void AskNextLegacyQuestion(MilestoneDefinition milestone)
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
        pendingLessonIndex = -1;
        calloutTarget = null;
        if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
        if (targetFrame != null) targetFrame.gameObject.SetActive(false);
        titleText.text = ISEVocabularyHighlighter.Highlight("ISE Knowledge Check");
        milestoneText.text = MilestoneProgressManager.Instance != null
            ? MilestoneProgressManager.Instance.GetMilestoneDisplayLabel(milestone)
            : milestone.displayName;
        milestoneText.text = ISEVocabularyHighlighter.Highlight(milestoneText.text);
        promptText.text = ISEVocabularyHighlighter.Highlight(question.prompt);
        ResizePromptText();
        feedbackText.text = string.Empty;
        feedbackText.gameObject.SetActive(true);
        answerRoot.SetActive(true);
        skipButton.gameObject.SetActive(true);
        continueButton.gameObject.SetActive(false);
        SetButtonLabel(skipButton, "Skip");

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
                ISEVocabularyHighlighter.Highlight(question.answers[answerIndex]),
                44f, GameUITheme.Surface,
                () => SelectAnswer(question, answerIndex));
            answerButtons.Add(button);
        }

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        questionVisible = true;
        visibleQuestionId = questionId;
        Sfx.Play(SfxId.UiOpen);
    }

    void ShowLesson(MilestoneDefinition milestone, MilestoneDefinition.ShiftLesson lesson)
    {
        EnsurePanel();
        if (overlay == null || lesson == null) return;

        pendingQuestionIndex = -1;
        pendingLessonIndex = Mathf.Clamp(pendingEventToken, 0, LessonsPerShift - 1);
        questionResolved = false;
        titleText.text = ISEVocabularyHighlighter.Highlight(lesson.title);
        milestoneText.text = MilestoneProgressManager.Instance != null
            ? MilestoneProgressManager.Instance.GetMilestoneDisplayLabel(milestone)
            : milestone.displayName;
        milestoneText.text = ISEVocabularyHighlighter.Highlight(milestoneText.text);
        promptText.text = ISEVocabularyHighlighter.Highlight(lesson.explanation);
        ResizePromptText();
        feedbackText.text = string.Empty;
        feedbackText.gameObject.SetActive(false);
        answerRoot.SetActive(false);
        skipButton.gameObject.SetActive(false);
        continueButton.gameObject.SetActive(true);
        SetButtonLabel(continueButton, "Continue");

        for (int i = 0; i < answerButtons.Count; i++)
            if (answerButtons[i] != null) Destroy(answerButtons[i].gameObject);
        answerButtons.Clear();
        answerOrder.Clear();

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        questionVisible = true;
        visibleQuestionId = string.IsNullOrWhiteSpace(lesson.lessonId)
            ? lesson.title : lesson.lessonId;
        calloutTarget = FindLessonTarget(lesson.target);
        nextCalloutTargetSearchTime = Time.unscaledTime + 0.5f;
        UpdateCallout();
        Sfx.Play(SfxId.UiOpen);
    }

    static void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = label;
    }

    void ResizePromptText()
    {
        if (promptText == null) return;
        LayoutElement layout = promptText.GetComponent<LayoutElement>();
        if (layout != null)
            layout.preferredHeight = Mathf.Clamp(promptText.preferredHeight + 8f, 58f, 100f);
    }

    static RectTransform FindLessonTarget(MilestoneDefinition.ShiftLessonTarget target)
    {
        string label;
        switch (target)
        {
            case MilestoneDefinition.ShiftLessonTarget.Business: label = "Business"; break;
            case MilestoneDefinition.ShiftLessonTarget.Objectives: label = "Objectives"; break;
            case MilestoneDefinition.ShiftLessonTarget.Recipes: label = "Recipes"; break;
            default: label = "Analytics"; break;
        }

        TextMeshProUGUI[] labels = FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI candidate = labels[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy
                || !string.Equals(candidate.text.Trim(), label, StringComparison.OrdinalIgnoreCase))
                continue;

            Button button = candidate.GetComponentInParent<Button>();
            return button != null ? button.transform as RectTransform : candidate.rectTransform;
        }
        return null;
    }

    void UpdateCallout()
    {
        bool show = pendingLessonIndex >= 0 && overlay != null && overlay.activeSelf;
        if (!show)
        {
            if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
            if (targetFrame != null) targetFrame.gameObject.SetActive(false);
            return;
        }

        if (calloutTarget == null || !calloutTarget.gameObject.activeInHierarchy)
        {
            if (Time.unscaledTime >= nextCalloutTargetSearchTime)
            {
                nextCalloutTargetSearchTime = Time.unscaledTime + 0.5f;
                MilestoneDefinition milestone = GetActiveMilestone();
                if (milestone != null && milestone.shiftLessons != null
                    && pendingLessonIndex < milestone.shiftLessons.Count)
                    calloutTarget = FindLessonTarget(milestone.shiftLessons[pendingLessonIndex].target);
            }
        }
        if (calloutTarget == null || canvasRect == null || overlay == null)
        {
            if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
            if (targetFrame != null) targetFrame.gameObject.SetActive(false);
            return;
        }

        if (!TryGetCanvasBounds(calloutTarget, out Rect targetBounds)
            || !TryGetCanvasBounds(overlay.transform as RectTransform, out Rect cardBounds))
        {
            if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
            if (targetFrame != null) targetFrame.gameObject.SetActive(false);
            return;
        }

        if (targetFrame != null)
        {
            targetFrame.gameObject.SetActive(true);
            targetFrame.anchoredPosition = targetBounds.center;
            targetFrame.sizeDelta = targetBounds.size + Vector2.one * 10f;
            float pulse = 0.72f + Mathf.PingPong(Time.unscaledTime * 0.75f, 0.22f);
            Color frameColor = GameUITheme.Accent;
            frameColor.a = pulse;
            if (targetFrameBars != null)
                for (int i = 0; i < targetFrameBars.Length; i++)
                    if (targetFrameBars[i] != null) targetFrameBars[i].color = frameColor;
        }

        if (calloutArrow != null)
        {
            Vector2 direction = (targetBounds.center - cardBounds.center).normalized;
            Vector2 start = cardBounds.center + GetRectEdgeOffset(cardBounds, direction);
            Vector2 end = targetBounds.center - GetRectEdgeOffset(targetBounds, direction) - direction * 8f;
            Vector2 delta = end - start;
            float length = delta.magnitude;
            calloutArrow.anchoredPosition = (start + end) * 0.5f;
            calloutArrow.sizeDelta = new Vector2(length, 26f);
            calloutArrow.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            Color arrowColor = GameUITheme.Accent;
            arrowColor.a = 0.82f;
            if (calloutShaft != null) calloutShaft.color = arrowColor;
            if (calloutHeads != null)
                for (int i = 0; i < calloutHeads.Length; i++)
                    if (calloutHeads[i] != null) calloutHeads[i].color = arrowColor;
            calloutArrow.gameObject.SetActive(true);
        }
    }

    bool TryGetCanvasBounds(RectTransform target, out Rect bounds)
    {
        bounds = new Rect();
        if (target == null || canvasRect == null) return false;

        target.GetWorldCorners(calloutWorldCorners);
        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Camera eventCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? targetCanvas.worldCamera : null;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < calloutWorldCorners.Length; i++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, calloutWorldCorners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                    screenPoint, null, out Vector2 localPoint))
                continue;
            min = Vector2.Min(min, localPoint);
            max = Vector2.Max(max, localPoint);
        }
        if (min.x == float.MaxValue) return false;
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    static Vector2 GetRectEdgeOffset(Rect rect, Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return Vector2.zero;
        float xDistance = Mathf.Abs(direction.x) > 0.0001f
            ? rect.width * 0.5f / Mathf.Abs(direction.x) : float.MaxValue;
        float yDistance = Mathf.Abs(direction.y) > 0.0001f
            ? rect.height * 0.5f / Mathf.Abs(direction.y) : float.MaxValue;
        return direction * Mathf.Min(xDistance, yDistance);
    }

    void EnsureCalloutVisuals()
    {
        if (canvasRoot == null || canvasRect == null) return;
        if (calloutArrow == null)
        {
            GameObject arrowObject = new GameObject("GusLessonArrow", typeof(RectTransform));
            arrowObject.transform.SetParent(canvasRoot.transform, false);
            RectTransform arrowRect = (RectTransform)arrowObject.transform;
            arrowRect.anchorMin = arrowRect.anchorMax = arrowRect.pivot = new Vector2(0.5f, 0.5f);
            calloutArrow = arrowRect;
            calloutShaft = CreateArrowSegment(arrowRect, "Shaft", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-18f, 4f), Vector2.zero);
            calloutHeads = new Image[2];
            calloutHeads[0] = CreateArrowSegment(arrowRect, "HeadUpper", new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(22f, 4f), Vector2.zero);
            calloutHeads[0].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 38f);
            calloutHeads[1] = CreateArrowSegment(arrowRect, "HeadLower", new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(22f, 4f), Vector2.zero);
            calloutHeads[1].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            arrowObject.SetActive(false);
        }

        if (targetFrame == null)
        {
            GameObject frameObject = new GameObject("GusLessonTargetFrame", typeof(RectTransform));
            frameObject.transform.SetParent(canvasRoot.transform, false);
            targetFrame = frameObject.GetComponent<RectTransform>();
            targetFrame.anchorMin = targetFrame.anchorMax = targetFrame.pivot = new Vector2(0.5f, 0.5f);
            targetFrameBars = new Image[4];
            targetFrameBars[0] = CreateFrameBar(targetFrame, "Top", new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            targetFrameBars[1] = CreateFrameBar(targetFrame, "Bottom", Vector2.zero,
                new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            targetFrameBars[2] = CreateFrameBar(targetFrame, "Left", Vector2.zero,
                new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            targetFrameBars[3] = CreateFrameBar(targetFrame, "Right", new Vector2(1f, 0f),
                Vector2.one, new Vector2(1f, 0.5f));
            targetFrame.gameObject.SetActive(false);
        }

        calloutArrow.transform.SetAsFirstSibling();
        targetFrame.transform.SetAsLastSibling();
    }

    static Image CreateArrowSegment(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position)
    {
        GameObject segment = new GameObject(name, typeof(RectTransform), typeof(Image));
        segment.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)segment.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = segment.GetComponent<Image>();
        image.color = GameUITheme.Accent;
        image.raycastTarget = false;
        return image;
    }

    static Image CreateFrameBar(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot)
    {
        GameObject bar = new GameObject(name, typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = anchorMin.x == anchorMax.x
            ? new Vector2(3f, 0f) : new Vector2(0f, 3f);
        Image image = bar.GetComponent<Image>();
        image.color = GameUITheme.Accent;
        image.raycastTarget = false;
        return image;
    }

    void SelectAnswer(MilestoneDefinition.ShiftQuestion question, int answerIndex)
    {
        if (questionResolved) return;
        questionResolved = true;

        bool correct = answerIndex == question.correctAnswerIndex;
        pendingQuestionIndex = -1;
        pendingEventToken = -1;
        if (correct)
        {
            FindFirstObjectByType<MoneyManager>()?.AddMoney(CorrectAnswerReward);
            feedbackText.text = ISEVocabularyHighlighter.Highlight(
                "Correct! +$50\n" + question.explanation);
            feedbackText.color = GameUITheme.PositiveAccent;
            Sfx.Play(SfxId.EarnMoney);
        }
        else
        {
            string correctAnswer = question.answers[question.correctAnswerIndex];
            feedbackText.text = ISEVocabularyHighlighter.Highlight(
                "Not quite. " + correctAnswer + "\n" + question.explanation);
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
        pendingEventToken = -1;
        pendingLessonIndex = -1;
        questionResolved = false;
        if (overlay != null) overlay.SetActive(false);
        if (calloutArrow != null) calloutArrow.gameObject.SetActive(false);
        if (targetFrame != null) targetFrame.gameObject.SetActive(false);
        questionVisible = false;
        visibleQuestionId = null;
        calloutTarget = null;
        PromptStateChanged?.Invoke();
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
        canvasRect = (RectTransform)canvasRoot.transform;
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;
        EnsureCalloutVisuals();

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
        ISEGlossaryLinkHandler.Attach(milestoneText);
        milestoneText.color = GameUITheme.Accent;
        titleText = CreateText(headerText.transform, "Title", "Gus has a question", 22f, 28f,
            TextAlignmentOptions.Left, FontStyles.Bold);
        ISEGlossaryLinkHandler.Attach(titleText);
        titleText.color = GameUITheme.TextPrimary;

        promptText = CreateText(overlay.transform, "Prompt", "", 19f, 58f,
            TextAlignmentOptions.Left, FontStyles.Normal);
        ISEGlossaryLinkHandler.Attach(promptText);
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
        ISEGlossaryLinkHandler.Attach(feedbackText);
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
