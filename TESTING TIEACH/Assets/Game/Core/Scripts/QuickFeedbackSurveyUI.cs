using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional, short end-of-shift survey. Responses stay on this device unless
/// the player chooses to copy them into the written feedback form.
/// </summary>
public sealed class QuickFeedbackSurveyUI : MonoBehaviour
{
    const string FeedbackUrl = "https://forms.gle/pBXDWzbBJxpTxkPA7";
    const string FileName = "ISE_Rush_Feedback_Survey.json";

    [Serializable]
    sealed class SurveyResponse
    {
        public int day;
        public string submittedUtc;
        public int productionClarity;
        public int bottleneckDiagnosis;
        public int workerControl;
    }

    [Serializable]
    sealed class SurveyResponseCollection
    {
        public List<SurveyResponse> responses = new List<SurveyResponse>();
    }

    static QuickFeedbackSurveyUI instance;
    readonly int[] selectedRatings = new int[3];
    readonly List<Button[]> ratingButtons = new List<Button[]>();
    GameObject overlay;
    GameObject questionContent;
    GameObject thanksContent;
    TextMeshProUGUI submitLabel;
    TextMeshProUGUI confirmationText;
    int currentDay = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
    }

    static QuickFeedbackSurveyUI EnsureInstance()
    {
        if (instance != null) return instance;
        instance = FindFirstObjectByType<QuickFeedbackSurveyUI>(FindObjectsInactive.Include);
        if (instance != null) return instance;
        GameObject host = new GameObject(nameof(QuickFeedbackSurveyUI));
        instance = host.AddComponent<QuickFeedbackSurveyUI>();
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static void OpenForShift(int day)
    {
        QuickFeedbackSurveyUI survey = EnsureInstance();
        survey.Show(day);
    }

    void Show(int day)
    {
        currentDay = Mathf.Max(1, day);
        for (int i = 0; i < selectedRatings.Length; i++) selectedRatings[i] = 0;
        EnsurePanel();
        if (overlay == null) return;
        RefreshRatingButtons();
        questionContent.SetActive(true);
        thanksContent.SetActive(false);
        overlay.SetActive(true);
        GameTimeManager.Instance?.RequestExternalPause(nameof(QuickFeedbackSurveyUI));
        Sfx.Play(SfxId.UiOpen);
    }

    void Close()
    {
        if (overlay != null) overlay.SetActive(false);
        GameTimeManager.Instance?.ReleaseExternalPause(nameof(QuickFeedbackSurveyUI));
        Sfx.Play(SfxId.UiClick);
    }

    void EnsurePanel()
    {
        if (overlay != null) return;
        Canvas canvas = ResolvePlayerUICanvas();
        if (canvas == null) return;

        overlay = new GameObject("QuickFeedbackSurvey", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform, false);
        RectTransform overlayRect = (RectTransform)overlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image dim = overlay.GetComponent<Image>();
        dim.color = new Color(0.02f, 0.03f, 0.05f, 0.76f);
        dim.raycastTarget = true;

        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(overlay.transform, false);
        RectTransform cardRect = (RectTransform)card.transform;
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(680f, 590f);
        card.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);

        VerticalLayoutGroup cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(28, 28, 24, 24);
        cardLayout.spacing = 12f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(card.transform, "Title", "QUICK SHIFT FEEDBACK", 25f,
            FontStyles.Bold, TextAlignmentOptions.Center, 40f);
        title.color = GameUITheme.TextPrimary;
        TextMeshProUGUI subtitle = CreateText(card.transform, "Subtitle",
            "Three quick ratings help us improve the restaurant systems.", 15f,
            FontStyles.Normal, TextAlignmentOptions.Center, 32f);
        subtitle.color = GameUITheme.TextSecondary;

        questionContent = new GameObject("Questions", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(LayoutElement));
        questionContent.transform.SetParent(card.transform, false);
        LayoutElement questionLayoutElement = questionContent.GetComponent<LayoutElement>();
        questionLayoutElement.flexibleHeight = 1f;
        VerticalLayoutGroup questionsLayout = questionContent.GetComponent<VerticalLayoutGroup>();
        questionsLayout.spacing = 10f;
        questionsLayout.childControlWidth = true;
        questionsLayout.childControlHeight = true;
        questionsLayout.childForceExpandWidth = true;
        questionsLayout.childForceExpandHeight = true;

        ratingButtons.Clear();
        AddQuestion(questionContent.transform, 0, "How clear was it whether your kitchen could meet demand?",
            "Not clear", "Very clear");
        AddQuestion(questionContent.transform, 1, "How easy was it to identify what limited production?",
            "Very hard", "Very easy");
        AddQuestion(questionContent.transform, 2, "How useful was assigning workers between flows during the shift?",
            "Not useful", "Very useful");

        GameObject actions = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        actions.transform.SetParent(card.transform, false);
        actions.GetComponent<LayoutElement>().preferredHeight = 50f;
        HorizontalLayoutGroup actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 12f;
        actionsLayout.childAlignment = TextAnchor.MiddleCenter;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = false;

        CreateButton(actions.transform, "Skip", "Skip", 50f,
            new Color(0.24f, 0.27f, 0.33f, 1f), Close);
        submitLabel = null;
        Button submit = CreateButton(actions.transform, "Submit", "Rate all three questions", 50f,
            new Color(0.35f, 0.62f, 0.49f, 1f), SubmitRatings);
        submitLabel = submit.GetComponentInChildren<TextMeshProUGUI>();

        thanksContent = new GameObject("Thanks", typeof(RectTransform), typeof(VerticalLayoutGroup));
        thanksContent.transform.SetParent(card.transform, false);
        VerticalLayoutGroup thanksLayout = thanksContent.GetComponent<VerticalLayoutGroup>();
        thanksLayout.spacing = 14f;
        thanksLayout.childControlWidth = true;
        thanksLayout.childControlHeight = true;
        thanksLayout.childForceExpandWidth = true;
        thanksLayout.childForceExpandHeight = false;
        LayoutElement thanksElement = thanksContent.AddComponent<LayoutElement>();
        thanksElement.flexibleHeight = 1f;
        confirmationText = CreateText(thanksContent.transform, "Confirmation",
            "Ratings saved on this device. To send them to the developers, copy them into the written feedback form.",
            16f, FontStyles.Normal, TextAlignmentOptions.Center, 90f);
        confirmationText.color = GameUITheme.TextPrimary;
        CreateButton(thanksContent.transform, "CopyAndOpenForm", "Copy Ratings & Open Feedback Form", 52f,
            new Color(0.35f, 0.48f, 0.68f, 1f), CopyAndOpenForm);
        CreateButton(thanksContent.transform, "Done", "Done", 48f,
            new Color(0.24f, 0.27f, 0.33f, 1f), Close);

        overlay.transform.SetAsLastSibling();
        overlay.SetActive(false);
    }

    void AddQuestion(Transform parent, int questionIndex, string question, string lowLabel, string highLabel)
    {
        GameObject row = new GameObject("Question" + questionIndex, typeof(RectTransform),
            typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(0.09f, 0.11f, 0.15f, 1f);
        LayoutElement rowElement = row.GetComponent<LayoutElement>();
        rowElement.minHeight = 110f;
        rowElement.preferredHeight = 110f;
        VerticalLayoutGroup rowLayout = row.GetComponent<VerticalLayoutGroup>();
        rowLayout.padding = new RectOffset(14, 14, 8, 8);
        rowLayout.spacing = 5f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        TextMeshProUGUI questionText = CreateText(row.transform, "Prompt", question, 15f,
            FontStyles.Bold, TextAlignmentOptions.Center, 24f);
        questionText.color = GameUITheme.TextPrimary;

        GameObject ratings = new GameObject("Ratings", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        ratings.transform.SetParent(row.transform, false);
        ratings.GetComponent<LayoutElement>().preferredHeight = 34f;
        HorizontalLayoutGroup ratingsLayout = ratings.GetComponent<HorizontalLayoutGroup>();
        ratingsLayout.spacing = 8f;
        ratingsLayout.childAlignment = TextAnchor.MiddleCenter;
        ratingsLayout.childControlWidth = false;
        ratingsLayout.childControlHeight = false;
        ratingsLayout.childForceExpandWidth = false;
        ratingsLayout.childForceExpandHeight = false;

        Button[] buttons = new Button[5];
        for (int rating = 1; rating <= 5; rating++)
        {
            int capturedRating = rating;
            buttons[rating - 1] = CreateRatingButton(ratings.transform, capturedRating,
                () => SelectRating(questionIndex, capturedRating));
        }
        ratingButtons.Add(buttons);

        GameObject endpoints = new GameObject("Endpoints", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        endpoints.transform.SetParent(row.transform, false);
        endpoints.GetComponent<LayoutElement>().preferredHeight = 18f;
        HorizontalLayoutGroup endpointsLayout = endpoints.GetComponent<HorizontalLayoutGroup>();
        endpointsLayout.childAlignment = TextAnchor.MiddleCenter;
        endpointsLayout.childControlWidth = true;
        endpointsLayout.childControlHeight = true;
        endpointsLayout.childForceExpandWidth = true;
        endpointsLayout.childForceExpandHeight = false;
        TextMeshProUGUI low = CreateText(endpoints.transform, "Low", lowLabel, 11f,
            FontStyles.Normal, TextAlignmentOptions.Left, 18f);
        TextMeshProUGUI high = CreateText(endpoints.transform, "High", highLabel, 11f,
            FontStyles.Normal, TextAlignmentOptions.Right, 18f);
        low.color = GameUITheme.TextSecondary;
        high.color = GameUITheme.TextSecondary;
    }

    Button CreateRatingButton(Transform parent, int rating, UnityEngine.Events.UnityAction callback)
    {
        Button button = CreateButton(parent, "Rating" + rating, rating.ToString(), 32f,
            new Color(0.20f, 0.24f, 0.30f, 1f), callback);
        LayoutElement element = button.GetComponent<LayoutElement>();
        element.minWidth = 48f;
        element.preferredWidth = 48f;
        return button;
    }

    void SelectRating(int questionIndex, int rating)
    {
        selectedRatings[questionIndex] = rating;
        RefreshRatingButtons();
        Sfx.Play(SfxId.UiClick);
    }

    void RefreshRatingButtons()
    {
        for (int question = 0; question < ratingButtons.Count; question++)
        {
            Button[] buttons = ratingButtons[question];
            for (int i = 0; i < buttons.Length; i++)
            {
                Image image = buttons[i] != null ? buttons[i].GetComponent<Image>() : null;
                if (image != null)
                    image.color = selectedRatings[question] == i + 1
                        ? new Color(0.28f, 0.55f, 0.43f, 1f)
                        : new Color(0.20f, 0.24f, 0.30f, 1f);
            }
        }
        if (submitLabel != null)
            submitLabel.text = AllRatingsSelected() ? "Save Ratings" : "Rate all three questions";
    }

    bool AllRatingsSelected() => selectedRatings[0] > 0 && selectedRatings[1] > 0 && selectedRatings[2] > 0;

    void SubmitRatings()
    {
        if (!AllRatingsSelected())
        {
            Sfx.Play(SfxId.UiClick);
            return;
        }

        SurveyResponseCollection data = LoadResponses();
        data.responses.Add(new SurveyResponse
        {
            day = currentDay,
            submittedUtc = DateTime.UtcNow.ToString("o"),
            productionClarity = selectedRatings[0],
            bottleneckDiagnosis = selectedRatings[1],
            workerControl = selectedRatings[2]
        });

        bool saved = false;
        try
        {
            File.WriteAllText(GetSavePath(), JsonUtility.ToJson(data, true));
            saved = true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not save playtest feedback locally: " + exception.Message);
        }

        if (confirmationText != null)
            confirmationText.text = saved
                ? "Ratings saved on this device. To send them to the developers, copy them into the written feedback form."
                : "Ratings could not be saved locally. Open the written feedback form to send them now.";
        questionContent.SetActive(false);
        thanksContent.SetActive(true);
        Sfx.Play(SfxId.UiOpen);
    }

    SurveyResponseCollection LoadResponses()
    {
        try
        {
            string path = GetSavePath();
            if (File.Exists(path))
            {
                SurveyResponseCollection loaded = JsonUtility.FromJson<SurveyResponseCollection>(File.ReadAllText(path));
                if (loaded != null && loaded.responses != null) return loaded;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not read previous playtest feedback: " + exception.Message);
        }
        return new SurveyResponseCollection();
    }

    void CopyAndOpenForm()
    {
        string ratings = "ISE Rush quick feedback, Day " + currentDay
            + "\nProduction clarity: " + selectedRatings[0] + "/5"
            + "\nBottleneck diagnosis: " + selectedRatings[1] + "/5"
            + "\nWorker control: " + selectedRatings[2] + "/5";
        GUIUtility.systemCopyBuffer = ratings;
        Application.OpenURL(FeedbackUrl);
        Sfx.Play(SfxId.UiClick);
    }

    static string GetSavePath() => Path.Combine(Application.persistentDataPath, FileName);

    static Canvas ResolvePlayerUICanvas()
    {
        GameObject playerUI = GameObject.Find("PlayerUI");
        if (playerUI != null)
        {
            Canvas canvas = playerUI.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }
        return FindFirstObjectByType<Canvas>();
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size,
        FontStyles style, TextAlignmentOptions alignment, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = GameUITheme.TextPrimary;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    static Button CreateButton(Transform parent, string name, string label, float height, Color color,
        UnityEngine.Events.UnityAction callback)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(callback);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(go.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = label;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return button;
    }
}
