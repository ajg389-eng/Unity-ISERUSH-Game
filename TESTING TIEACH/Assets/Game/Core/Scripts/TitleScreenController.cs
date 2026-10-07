using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows a title screen at game start. Pauses the game until Play is clicked.
/// Assign a camera that views the outside of the restaurant as the background.
/// </summary>
public class TitleScreenController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Full-screen panel with title and buttons (shown at start)")]
    public GameObject titlePanel;
    public Button playButton;
    public Button settingsButton;
    public Button exitButton;

    [Header("Presentation")]
    [Tooltip("Logo RectTransform. If unset, a child named Title is found automatically.")]
    public RectTransform animatedTitle;
    [Min(0f)] public float titleBobDistance = 10f;
    [Min(0.01f)] public float titleBobCyclesPerSecond = 0.35f;

    [Header("First New Game Intro")]
    [Tooltip("Optional full-screen background for Gus's opening dialogue. Uses the title background when empty.")]
    public Texture introBackground;
    [Tooltip("NPC model prefab rendered as Gus during the opening dialogue.")]
    public GameObject gusPrefab;
    [Tooltip("Optional portrait for Gus. A labeled placeholder is shown until final artwork is assigned.")]
    public Sprite gusPortrait;

    [Header("Cameras")]
    [Tooltip("Camera showing the restaurant exterior; active while title is visible")]
    public Camera titleCamera;
    [Tooltip("Gameplay camera; active after Play. If unset, uses Camera.main.")]
    public Camera gameCamera;

    [Header("In-game UI (hide while on title screen)")]
    [Tooltip("Canvas or parent of all game UI (HUD, build menu, etc.). Hidden until Play is clicked.")]
    public GameObject inGameUIRoot;

    [Header("Settings (optional)")]
    [Tooltip("Panel to open when Settings is clicked (e.g. volume). Leave empty to do nothing.")]
    public GameObject settingsPanel;

    bool showingTitle = true;
    bool choosingSave;
    bool showingSettings;
    GameObject saveSlotsRoot;
    int pendingWipeSlot;
    float pendingWipeUntil;
    TextMeshProUGUI pendingWipeLabel;
    Vector2 titleRestPosition;
    bool titleAnimationReady;
    bool gameStartCompleted;

    void Start()
    {
        if (titlePanel != null)
            titlePanel.SetActive(true);

        PreparePresentation();

        if (inGameUIRoot == null)
        {
            var myCanvas = GetComponentInParent<Canvas>();
            var all = FindObjectsOfType<Canvas>();
            foreach (var c in all)
            {
                if (c != null && c.gameObject != myCanvas?.gameObject)
                {
                    inGameUIRoot = c.gameObject;
                    break;
                }
            }
        }
        if (inGameUIRoot != null)
            inGameUIRoot.SetActive(false);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.RequestExternalPause(GameTimeManager.PauseTitleScreen);
        else
            Time.timeScale = 0f;

        if (titleCamera != null) titleCamera.enabled = true;
        if (gameCamera != null) gameCamera.enabled = false;
        else if (Camera.main != null && Camera.main != titleCamera)
            Camera.main.enabled = false;

        if (playButton != null)
            playButton.onClick.AddListener(OnPlay);
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettings);
        if (exitButton != null)
            exitButton.onClick.AddListener(OnExit);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    void Update()
    {
        if (showingTitle && titleAnimationReady && animatedTitle != null)
        {
            float phase = Time.unscaledTime * titleBobCyclesPerSecond * Mathf.PI * 2f;
            animatedTitle.anchoredPosition = titleRestPosition
                + Vector2.up * (Mathf.Sin(phase) * titleBobDistance);
        }

        if (showingTitle && Input.GetKeyDown(KeyCode.Escape))
        {
            if (showingSettings)
                PauseMenuUI.Instance?.CloseTitleSettings();
            else if (choosingSave)
                HideSaveSlots();
        }

        if (pendingWipeSlot > 0 && Time.unscaledTime > pendingWipeUntil)
        {
            if (pendingWipeLabel != null)
                pendingWipeLabel.text = "WIPE";
            pendingWipeSlot = 0;
            pendingWipeLabel = null;
        }
    }

    void PreparePresentation()
    {
        if (animatedTitle == null && titlePanel != null)
        {
            var rects = titlePanel.GetComponentsInChildren<RectTransform>(true);
            foreach (var rect in rects)
            {
                if (rect != null && rect != titlePanel.transform
                    && rect.name.Equals("Title", System.StringComparison.OrdinalIgnoreCase))
                {
                    animatedTitle = rect;
                    break;
                }
            }
        }

        if (animatedTitle != null)
        {
            titleRestPosition = animatedTitle.anchoredPosition;
            titleAnimationReady = true;
            AddTitleShadow(animatedTitle.gameObject);
        }

        StyleButton(playButton,
            GameUITheme.Positive,
            GameUITheme.PositiveHover,
            GameUITheme.PositiveAccent);
        StyleButton(settingsButton,
            GameUITheme.Surface,
            GameUITheme.SurfaceHover,
            GameUITheme.Accent);
        StyleButton(exitButton,
            GameUITheme.Danger,
            GameUITheme.DangerHover,
            GameUITheme.DangerAccent);
    }

    static void AddTitleShadow(GameObject title)
    {
        if (title == null || title.GetComponent<Graphic>() == null) return;
        Shadow shadow = null;
        foreach (var effect in title.GetComponents<Shadow>())
        {
            if (effect != null && effect.GetType() == typeof(Shadow))
            {
                shadow = effect;
                break;
            }
        }
        if (shadow == null)
            shadow = title.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.02f, 0.04f, 0.08f, 0.72f);
        shadow.effectDistance = new Vector2(0f, -7f);
        shadow.useGraphicAlpha = true;
    }

    static void StyleButton(Button button, Color normal, Color hover, Color accent)
    {
        if (button == null) return;

        var image = button.GetComponent<Image>();
        if (image == null)
            image = button.gameObject.AddComponent<Image>();
        image.color = normal;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.selectedColor = hover;
        colors.pressedColor = Color.Lerp(hover, Color.black, 0.22f);
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.04f, 0.08f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        Shadow shadow = null;
        foreach (var effect in button.GetComponents<Shadow>())
        {
            if (effect != null && effect.GetType() == typeof(Shadow))
            {
                shadow = effect;
                break;
            }
        }
        if (shadow == null)
            shadow = button.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.48f);
        shadow.effectDistance = new Vector2(0f, -5f);
        shadow.useGraphicAlpha = true;

        EnsureAccent(button.transform, accent);

        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.color = GameUITheme.TextPrimary;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 25f;
            label.characterSpacing = 1.5f;
            label.raycastTarget = false;
        }

        var rect = button.transform as RectTransform;
        if (rect != null)
            rect.sizeDelta = new Vector2(Mathf.Max(280f, rect.sizeDelta.x), Mathf.Max(58f, rect.sizeDelta.y));

        var motion = button.GetComponent<TitleButtonMotion>();
        if (motion == null)
            motion = button.gameObject.AddComponent<TitleButtonMotion>();
        motion.Configure();
    }

    static void EnsureAccent(Transform button, Color color)
    {
        if (button == null) return;
        Transform existing = button.Find("StyleAccent");
        GameObject accent = existing != null ? existing.gameObject : null;
        if (accent == null)
        {
            accent = new GameObject("StyleAccent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(button, false);
        }

        var rect = (RectTransform)accent.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(7f, -6f);

        var image = accent.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        accent.transform.SetAsFirstSibling();
    }

    void OnPlay()
    {
        if (!showingTitle) return;
        ShowSaveSlots();
    }

    void ShowSaveSlots(bool playSound = true)
    {
        choosingSave = true;
        SetMainMenuButtonsVisible(false);
        pendingWipeSlot = 0;
        pendingWipeLabel = null;

        if (saveSlotsRoot != null)
        {
            saveSlotsRoot.SetActive(false);
            Destroy(saveSlotsRoot);
        }

        Transform parent = playButton != null ? playButton.transform.parent : titlePanel.transform;
        saveSlotsRoot = new GameObject("SaveSlots", typeof(RectTransform));
        saveSlotsRoot.transform.SetParent(parent, false);
        var rootRect = (RectTransform)saveSlotsRoot.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.SetAsLastSibling();

        Button[] templates = { playButton, settingsButton, exitButton };
        for (int i = 0; i < GameSaveSlots.SlotCount; i++)
            CreateSaveSlotRow(i + 1, templates[Mathf.Min(i, templates.Length - 1)]);

        if (playSound)
            Sfx.Play(SfxId.UiOpen);
    }

    void CreateSaveSlotRow(int slotIndex, Button template)
    {
        if (template == null || saveSlotsRoot == null) return;

        GameObject rowObject = Instantiate(template.gameObject, saveSlotsRoot.transform);
        rowObject.name = "SaveSlot" + slotIndex;
        rowObject.SetActive(true);

        var sourceRect = template.transform as RectTransform;
        var rowRect = rowObject.transform as RectTransform;
        if (sourceRect != null && rowRect != null)
        {
            rowRect.anchorMin = sourceRect.anchorMin;
            rowRect.anchorMax = sourceRect.anchorMax;
            rowRect.pivot = sourceRect.pivot;
            rowRect.anchoredPosition = sourceRect.anchoredPosition;
            rowRect.sizeDelta = new Vector2(Mathf.Max(560f, sourceRect.sizeDelta.x),
                Mathf.Max(76f, sourceRect.sizeDelta.y));
            rowRect.localScale = Vector3.one;
        }

        var button = rowObject.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        int capturedSlot = slotIndex;
        button.onClick.AddListener(() => StartGame(capturedSlot));
        StyleButton(button, GameUITheme.Surface, GameUITheme.SurfaceHover, GameUITheme.Accent);

        GameSaveSlots.SlotInfo info = GameSaveSlots.GetSlot(slotIndex);
        var label = rowObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            string status;
            string action;
            if (info.exists)
            {
                string played = info.lastPlayedUtc == System.DateTime.MinValue
                    ? "Saved game"
                    : info.lastPlayedUtc.ToLocalTime().ToString("MMM d, h:mm tt");
                string milestone = info.milestone > 0
                    ? "Milestone " + info.milestone
                    : "Tutorial";
                status = $"Day {info.day}  |  ${info.cash:N0}  |  {milestone}\n" +
                         $"<pos=18%><color=#B7BEC5>Saved {played}</color>";
                action = "CONTINUE  >";
            }
            else
            {
                status = "Empty slot";
                action = "NEW GAME  >";
            }

            label.text = info.exists
                ? $"<align=left><b>SAVE {slotIndex}</b><pos=18%>{status}" +
                  $"<pos=67%><color=#D8B365><b>{action}</b></color>"
                : $"<align=left><b>SAVE {slotIndex}</b><pos=25%><color=#B7BEC5>{status}</color>" +
                  $"<pos=79%><color=#D8B365><b>{action}</b></color>";
            label.fontSize = 16f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 11f;
            label.fontSizeMax = 16f;
            label.margin = new Vector4(18f, 4f, info.exists ? 108f : 16f, 4f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (info.exists)
            CreateWipeButton(rowObject.transform, slotIndex);
    }

    void CreateWipeButton(Transform parent, int slotIndex)
    {
        var go = new GameObject("WipeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-10f, 0f);
        rect.sizeDelta = new Vector2(88f, 42f);

        var image = go.GetComponent<Image>();
        image.color = GameUITheme.Danger;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = GameUITheme.Danger;
        colors.highlightedColor = GameUITheme.DangerHover;
        colors.pressedColor = Color.Lerp(GameUITheme.Danger, Color.black, 0.2f);
        colors.selectedColor = GameUITheme.DangerHover;
        button.colors = colors;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = GameUITheme.Edge;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -2f);
        shadow.useGraphicAlpha = true;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(go.transform, false);
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "WIPE";
        label.fontSize = 13f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = GameUITheme.TextPrimary;
        label.raycastTarget = false;
        GameUITheme.ApplyTitleScreenFont(label);

        int capturedSlot = slotIndex;
        button.onClick.AddListener(() => RequestWipe(capturedSlot, label));
        var motion = go.AddComponent<TitleButtonMotion>();
        motion.Configure();
    }

    void RequestWipe(int slotIndex, TextMeshProUGUI label)
    {
        if (pendingWipeSlot == slotIndex && Time.unscaledTime <= pendingWipeUntil)
        {
            GameSaveSlots.WipeSlot(slotIndex);
            pendingWipeSlot = 0;
            pendingWipeLabel = null;
            Sfx.Play(SfxId.UiClose);
            ShowSaveSlots(playSound: false);
            return;
        }

        if (pendingWipeLabel != null)
            pendingWipeLabel.text = "WIPE";
        pendingWipeSlot = slotIndex;
        pendingWipeUntil = Time.unscaledTime + 3f;
        pendingWipeLabel = label;
        if (label != null)
            label.text = "CONFIRM";
        Sfx.Play(SfxId.UiClick);
    }

    void HideSaveSlots()
    {
        choosingSave = false;
        pendingWipeSlot = 0;
        pendingWipeLabel = null;
        if (saveSlotsRoot != null)
        {
            Destroy(saveSlotsRoot);
            saveSlotsRoot = null;
        }
        SetMainMenuButtonsVisible(true);
        Sfx.Play(SfxId.UiClick);
    }

    void SetMainMenuButtonsVisible(bool visible)
    {
        if (playButton != null) playButton.gameObject.SetActive(visible);
        if (settingsButton != null) settingsButton.gameObject.SetActive(visible);
        if (exitButton != null) exitButton.gameObject.SetActive(visible);
    }

    void StartGame(int slotIndex)
    {
        if (!showingTitle) return;
        GameSaveSlots.SlotInfo slot = GameSaveSlots.GetSlot(slotIndex);
        bool showIntro = !slot.exists || !slot.introSeen;
        Texture background = introBackground;
        if (background == null && titlePanel != null)
        {
            var titleBackground = titlePanel.GetComponent<RawImage>();
            if (titleBackground != null)
                background = titleBackground.texture;
        }

        if (!GameSaveSlots.SelectAndLoad(slotIndex)) return;
        choosingSave = false;
        showingTitle = false;

        if (titlePanel != null)
            titlePanel.SetActive(false);

        if (showIntro)
        {
            GameObject introCharacter = gusPrefab != null
                ? gusPrefab
                : Resources.Load<GameObject>("Prefabs/character_default");
            IntroCutsceneUI.Show(background, introCharacter, gusPortrait, () =>
            {
                GameSaveSlots.MarkActiveSlotIntroSeen();
                CompleteGameStart();
            });
            return;
        }

        CompleteGameStart();
    }

    void CompleteGameStart()
    {
        if (gameStartCompleted) return;
        gameStartCompleted = true;

        if (inGameUIRoot != null)
            inGameUIRoot.SetActive(true);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(GameTimeManager.PauseTitleScreen);
        else
            Time.timeScale = 1f;

        if (titleCamera != null) titleCamera.enabled = false;
        if (gameCamera != null)
            gameCamera.enabled = true;
        else if (Camera.main != null)
            Camera.main.enabled = true;

        // Cutaways must follow the camera the player moves after leaving the title.
        CameraWallCutaway.EnsureExists();
        CameraWallCutaway.Instance.targetCamera = gameCamera != null ? gameCamera : Camera.main;

        TutorialVoiceEvents.Raise(TutorialVoiceEventId.ShiftStarted);
        Sfx.Play(SfxId.UiOpen);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.BeginDay();

        var onboarding = OnboardingTutorial.Instance != null
            ? OnboardingTutorial.Instance
            : FindFirstObjectByType<OnboardingTutorial>();
        if (onboarding != null)
            onboarding.NotifyGameStarted();
    }

    void OnSettings()
    {
        if (!showingTitle) return;

        if (showingSettings)
        {
            PauseMenuUI.Instance?.CloseTitleSettings();
            return;
        }

        var menu = PauseMenuUI.Instance ?? FindFirstObjectByType<PauseMenuUI>();
        if (menu == null)
            menu = new GameObject("PauseMenuUI").AddComponent<PauseMenuUI>();

        showingSettings = true;
        SetMainMenuButtonsVisible(false);
        menu.ShowOnTitleScreen(
            playButton != null ? playButton.transform as RectTransform : null,
            exitButton != null ? exitButton.transform as RectTransform : null,
            OnTitleSettingsClosed);
    }

    void OnTitleSettingsClosed()
    {
        showingSettings = false;
        if (showingTitle && !choosingSave)
            SetMainMenuButtonsVisible(true);
    }

    void OnExit()
    {
        var menu = PauseMenuUI.Instance ?? FindFirstObjectByType<PauseMenuUI>();
        if (menu == null)
            menu = new GameObject("PauseMenuUI").AddComponent<PauseMenuUI>();
        menu.ShowQuitConfirmation(
            "Quit Game?",
            "Are you sure you want to exit the game?",
            menu.ConfirmApplicationQuit);
    }

    public bool IsShowingTitle => showingTitle;
}

/// <summary>Small unscaled hover response for title-screen buttons.</summary>
public class TitleButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    Vector3 restScale = Vector3.one;
    float targetScale = 1f;

    public void Configure()
    {
        restScale = transform.localScale;
        targetScale = 1f;
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            restScale * targetScale,
            14f * Time.unscaledDeltaTime);
    }

    public void OnPointerEnter(PointerEventData eventData) => targetScale = 1.035f;
    public void OnPointerExit(PointerEventData eventData) => targetScale = 1f;
    public void OnPointerDown(PointerEventData eventData) => targetScale = 0.975f;
    public void OnPointerUp(PointerEventData eventData) => targetScale = 1.035f;
}
