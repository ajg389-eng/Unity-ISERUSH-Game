using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Escape pause overlay. Main screen matches a simple Resume / Options / Quit to Menu list.
/// Options holds Audio, Video, Graphics, and Keybinds.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    public const string PauseSource = GameTimeManager.PauseMenu;
    const string FeedbackUrl = "https://forms.gle/pBXDWzbBJxpTxkPA7";
    const string PrefMaster = "PauseMenu.MasterVolume";
    const string PrefVoice = "PauseMenu.VoiceVolume";
    const string PrefMusic = "PauseMenu.MusicVolume";
    const string PrefSoundEffects = "PauseMenu.SoundEffectsVolume";
    const string PrefUiSounds = "PauseMenu.UiSoundsVolume";
    const string PrefVolumeLegacy = "PauseMenu.Volume";
    const string PrefWidth = "PauseMenu.Width";
    const string PrefHeight = "PauseMenu.Height";
    const string PrefFullscreen = "PauseMenu.Fullscreen";
    const string PrefShadows = "PauseMenu.Graphics.Shadows";
    const string PrefShadowQuality = "PauseMenu.Graphics.ShadowQuality";
    const string PrefAntiAliasing = "PauseMenu.Graphics.AntiAliasing";
    const string PrefTextureQuality = "PauseMenu.Graphics.TextureQuality";
    const string PrefVSync = "PauseMenu.Graphics.VSync";
    const string PrefGraphicsPreset = "PauseMenu.Graphics.Preset";
    const float MusicFullVolume = 0.35f;

    static readonly Color ButtonColor = GameUITheme.Surface;
    static readonly Color MainButtonColor = GameUITheme.Surface;
    static readonly Color MainBoxColor = GameUITheme.Backdrop;
    static readonly Color ButtonTextColor = GameUITheme.TextPrimary;
    static readonly Color PanelColor = GameUITheme.Backdrop;
    static readonly Color OptionsButtonColor = GameUITheme.Surface;

    public static PauseMenuUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.visible;
    public static bool EscapeHandledThisFrame { get; private set; }

    public static void MarkEscapeHandled()
    {
        EscapeHandledThisFrame = true;
    }

    GameObject overlay;
    GameObject mainPage;
    GameObject optionsPage;
    GameObject creditsPage;
    GuidebookUI guidebook;
    GameObject audioPage;
    GameObject videoPage;
    GameObject graphicsPage;
    GameObject keybindsPage;
    GameObject quitConfirmation;
    TextMeshProUGUI quitConfirmationTitle;
    TextMeshProUGUI quitConfirmationBody;
    Button audioTab;
    Button videoTab;
    Button graphicsTab;
    Button keybindsTab;
    Toggle lockNorthToggle;
    Toggle lockEastToggle;
    Toggle lockSouthToggle;
    Toggle lockWestToggle;
    Slider masterSlider;
    Slider voiceSlider;
    Slider musicSlider;
    Slider soundEffectsSlider;
    Slider uiSoundsSlider;
    TextMeshProUGUI masterValueText;
    TextMeshProUGUI voiceValueText;
    TextMeshProUGUI musicValueText;
    TextMeshProUGUI soundEffectsValueText;
    TextMeshProUGUI uiSoundsValueText;
    TextMeshProUGUI resolutionLabel;
    Toggle fullscreenToggle;
    Toggle shadowsToggle;
    Toggle vSyncToggle;
    TextMeshProUGUI shadowQualityValue;
    TextMeshProUGUI antiAliasingValue;
    TextMeshProUGUI textureQualityValue;
    TextMeshProUGUI graphicsPresetValue;
    int shadowQualityIndex;
    int antiAliasingIndex;
    int textureQualityIndex;
    readonly List<Resolution> uniqueResolutions = new List<Resolution>();
    int resolutionIndex;
    bool visible;
    bool built;
    bool showingOptions;
    bool showingGuidebook;
    bool showingCredits;
    bool titleSettingsMode;
    bool confirmationOwnsOverlay;
    bool confirmationPausedGame;
    bool allowApplicationQuit;
    System.Action confirmedQuitAction;
    System.Action titleSettingsClosed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PauseMenuUI>() != null) return;
        var go = new GameObject("PauseMenuUI");
        go.AddComponent<PauseMenuUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Application.wantsToQuit += HandleApplicationQuitRequest;
        ApplySavedAudio();
        ApplySavedVideo(false);
        ApplySavedGraphics();
    }

    void OnDestroy()
    {
        Application.wantsToQuit -= HandleApplicationQuitRequest;
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        EnsureUi();
        Hide(playSound: false);
    }

    void LateUpdate()
    {
        // Escape is a global pause override. Other systems may also use it to cancel
        // their current action, but they must not prevent the pause menu from opening.
        bool escape = Input.GetKeyDown(KeyCode.Escape);
        if (escape && quitConfirmation != null && quitConfirmation.activeSelf)
        {
            CloseQuitConfirmation();
            EscapeHandledThisFrame = false;
            return;
        }
        if (escape && !IsTitleVisible())
        {
            if (!visible)
                Show();
            else if (showingGuidebook)
                CloseGuidebook();
            else if (showingCredits)
                ShowMain();
            else if (showingOptions)
                ShowMain();
            else
                Hide();
        }
        EscapeHandledThisFrame = false;
    }

    public void Show()
    {
        EnsureUi();
        if (overlay == null) return;

        if (titleSettingsMode)
            CloseTitleSettings(invokeCallback: false);

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        visible = true;
        ShowMain();
        RefreshAudioControls();
        RefreshVideoControls();
        RefreshGraphicsControls();
        Sfx.Play(SfxId.UiOpen);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.RequestExternalPause(PauseSource);
        else
            Time.timeScale = 0f;
    }

    public void Hide(bool playSound = true)
    {
        if (confirmationPausedGame)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
            else
                Time.timeScale = 1f;
        }
        if (quitConfirmation != null)
            quitConfirmation.SetActive(false);
        confirmedQuitAction = null;
        confirmationOwnsOverlay = false;
        confirmationPausedGame = false;
        visible = false;
        showingOptions = false;
        showingGuidebook = false;
        showingCredits = false;
        if (guidebook != null)
            guidebook.Close();
        if (overlay != null)
            overlay.SetActive(false);

        if (playSound)
            Sfx.Play(SfxId.UiClose);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
    }

    void ShowMain()
    {
        if (titleSettingsMode)
        {
            CloseTitleSettings();
            return;
        }
        showingOptions = false;
        showingGuidebook = false;
        showingCredits = false;
        if (guidebook != null)
            guidebook.Close();
        if (mainPage != null) mainPage.SetActive(true);
        if (optionsPage != null) optionsPage.SetActive(false);
        if (creditsPage != null) creditsPage.SetActive(false);
    }

    public void CloseGuidebook()
    {
        showingGuidebook = false;
        if (guidebook != null)
            guidebook.Close();
        ShowMain();
    }

    void ShowGuidebook()
    {
        showingGuidebook = true;
        showingOptions = false;
        if (mainPage != null) mainPage.SetActive(false);
        if (optionsPage != null) optionsPage.SetActive(false);
        if (guidebook != null)
            guidebook.Open();
    }

    void ShowOptions()
    {
        showingOptions = true;
        if (mainPage != null) mainPage.SetActive(false);
        if (optionsPage != null) optionsPage.SetActive(true);
        SelectTab(0, playSound: false);
        RefreshAudioControls();
        RefreshVideoControls();
        RefreshGraphicsControls();
    }

    /// <summary>
    /// Shows the functional settings controls over the title screen without the pause
    /// backdrop or outer card. The controls occupy the area previously used by the
    /// title menu buttons.
    /// </summary>
    public void ShowOnTitleScreen(RectTransform firstButton, RectTransform lastButton,
        System.Action onClosed)
    {
        EnsureUi();
        if (overlay == null || optionsPage == null) return;

        titleSettingsMode = true;
        titleSettingsClosed = onClosed;
        visible = false;
        showingOptions = true;
        showingGuidebook = false;

        overlay.SetActive(true);
        var overlayImage = overlay.GetComponent<Image>();
        if (overlayImage != null)
        {
            overlayImage.color = Color.clear;
            overlayImage.raycastTarget = false;
        }

        if (mainPage != null) mainPage.SetActive(false);
        optionsPage.SetActive(true);
        SetTitleSettingsPresentation(firstButton, lastButton);
        SelectTab(0, playSound: false);
        RefreshAudioControls();
        RefreshVideoControls();
        RefreshGraphicsControls();
        Canvas.ForceUpdateCanvases();
        GameUITheme.ApplyTo(optionsPage.transform);
        Sfx.Play(SfxId.UiOpen);
    }

    public void CloseTitleSettings() => CloseTitleSettings(invokeCallback: true);

    void CloseTitleSettings(bool invokeCallback)
    {
        if (!titleSettingsMode) return;

        titleSettingsMode = false;
        showingOptions = false;
        RestorePauseSettingsPresentation();
        if (optionsPage != null) optionsPage.SetActive(false);
        if (overlay != null) overlay.SetActive(false);

        var callback = titleSettingsClosed;
        titleSettingsClosed = null;
        if (invokeCallback)
        {
            Sfx.Play(SfxId.UiClose);
            callback?.Invoke();
        }
    }

    void SetTitleSettingsPresentation(RectTransform firstButton, RectTransform lastButton)
    {
        var rect = optionsPage.transform as RectTransform;
        if (rect == null) return;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(620f, 440f);

        Vector2 screenTop = new Vector2(Screen.width * 0.5f, Screen.height * 0.56f);
        if (firstButton != null)
        {
            Vector3[] corners = new Vector3[4];
            firstButton.GetWorldCorners(corners);
            Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            screenTop = new Vector2((topLeft.x + topRight.x) * 0.5f,
                Mathf.Max(topLeft.y, topRight.y));
        }

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlay.transform as RectTransform, screenTop, null, out Vector2 localTop))
            rect.anchoredPosition = localTop;

        var image = optionsPage.GetComponent<Image>();
        if (image != null)
        {
            image.color = PanelColor;
            image.raycastTarget = true;
        }
        SetPanelChromeEnabled(optionsPage, true);

        Transform heading = optionsPage.transform.Find("OptionsTitle");
        if (heading != null) heading.gameObject.SetActive(false);
    }

    void RestorePauseSettingsPresentation()
    {
        if (overlay != null && overlay.TryGetComponent(out Image overlayImage))
        {
            overlayImage.color = new Color(0f, 0f, 0f, 0.35f);
            overlayImage.raycastTarget = true;
        }

        if (optionsPage == null) return;
        var rect = optionsPage.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(640f, 500f);
        }

        var image = optionsPage.GetComponent<Image>();
        if (image != null)
        {
            image.color = PanelColor;
            image.raycastTarget = true;
        }
        SetPanelChromeEnabled(optionsPage, true);

        Transform heading = optionsPage.transform.Find("OptionsTitle");
        if (heading != null) heading.gameObject.SetActive(true);
    }

    static bool IsTitleVisible()
    {
        var title = FindFirstObjectByType<TitleScreenController>();
        return title != null && title.IsShowingTitle;
    }

    void EnsureUi()
    {
        if (built && overlay != null) return;
        built = true;

        var canvasGo = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;

        overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasGo.transform, false);
        Stretch((RectTransform)overlay.transform);
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        overlay.GetComponent<Image>().raycastTarget = true;

        mainPage = BuildMainPage(overlay.transform);
        optionsPage = BuildOptionsPage(overlay.transform);
        optionsPage.SetActive(false);
        creditsPage = BuildCreditsPage(overlay.transform);
        creditsPage.SetActive(false);
        guidebook = GuidebookUI.Create(overlay.transform);
        quitConfirmation = BuildQuitConfirmation(overlay.transform);
        quitConfirmation.SetActive(false);

        // This menu is created after the global theme's scene pass. Finalize it now so
        // it never renders one frame with the old gray controls or competing tints.
        Canvas.ForceUpdateCanvases();
        GameUITheme.ApplyTo(canvasGo.transform);
    }

    GameObject BuildMainPage(Transform parent)
    {
        var page = new GameObject("MainPage", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        page.transform.SetParent(parent, false);
        var rt = (RectTransform)page.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 360f);
        page.GetComponent<Image>().color = MainBoxColor;
        AddPanelChrome(page);

        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 24, 26);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = page.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = CreateLabel(page.transform, "PauseTitle", "Paused", 28f, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1.2f;
        title.GetComponent<LayoutElement>().preferredHeight = 40f;

        var resume = CreateMenuButton(page.transform, "ResumeButton", "Resume", 54f, 360f, 22f, GameUITheme.Positive);
        resume.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            Hide(playSound: false);
        });

        var options = CreateMenuButton(page.transform, "OptionsButton", "Options", 54f, 360f, 22f, MainButtonColor);
        options.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowOptions();
        });

        var guidebookBtn = CreateMenuButton(page.transform, "GuidebookButton", "Guidebook", 54f, 360f, 22f, MainButtonColor);
        guidebookBtn.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowGuidebook();
        });

        var feedback = CreateMenuButton(page.transform, "FeedbackButton", "Give Feedback", 54f, 360f, 22f, MainButtonColor);
        feedback.onClick.AddListener(OpenFeedbackForm);

        var credits = CreateMenuButton(page.transform, "CreditsButton", "Credits", 54f, 360f, 22f, MainButtonColor);
        credits.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowCredits();
        });

        var quit = CreateMenuButton(page.transform, "QuitToMenuButton", "Quit to Menu", 54f, 360f, 22f, GameUITheme.Danger);
        quit.onClick.AddListener(OnQuitToMenu);
        return page;
    }

    void ShowCredits()
    {
        showingCredits = true;
        showingOptions = false;
        showingGuidebook = false;
        if (guidebook != null) guidebook.Close();
        if (mainPage != null) mainPage.SetActive(false);
        if (optionsPage != null) optionsPage.SetActive(false);
        if (creditsPage != null) creditsPage.SetActive(true);
    }

    GameObject BuildCreditsPage(Transform parent)
    {
        var card = new GameObject("CreditsPage", typeof(RectTransform), typeof(UnityEngine.UI.Image),
            typeof(VerticalLayoutGroup));
        card.transform.SetParent(parent, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(760f, 820f);
        card.GetComponent<UnityEngine.UI.Image>().color = PanelColor;
        AddPanelChrome(card);

        var cardLayout = card.GetComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(28, 28, 22, 22);
        cardLayout.spacing = 12f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        var title = CreateLabel(card.transform, "CreditsTitle", "Credits", 30f, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.GetComponent<LayoutElement>().preferredHeight = 42f;

        var scroll = new GameObject("CreditsScrollView", typeof(RectTransform), typeof(LayoutElement),
            typeof(ScrollRect));
        scroll.transform.SetParent(card.transform, false);
        var scrollLayout = scroll.GetComponent<LayoutElement>();
        scrollLayout.preferredHeight = 660f;
        scrollLayout.flexibleHeight = 1f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image),
            typeof(Mask));
        viewport.transform.SetParent(scroll.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        Stretch(viewportRt);
        viewport.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.14f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(16, 16, 14, 14);
        contentLayout.spacing = 8f;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateCreditSection(content.transform, "UNITY ASSET STORE PACKAGES");
        CreateCreditLink(content.transform, "Fast Food Restaurant Kit", "Brick Project Studio",
            "https://assetstore.unity.com/packages/3d/environments/fast-food-restaurant-kit-239419");
        CreateCreditLink(content.transform, "Pandazole - Kitchen Food low poly pack", "Pandazole",
            "https://assetstore.unity.com/packages/3d/props/food/pandazole-kitchen-food-low-poly-pack-204525");
        CreateCreditLink(content.transform, "FREE Party Game Characters", "OlyPoly",
            "https://assetstore.unity.com/packages/3d/characters/humanoids/free-party-game-characters-342650");
        CreateCreditLink(content.transform, "Free Cooking Animations", "EEJANAI, TEAM",
            "https://assetstore.unity.com/packages/3d/animations/free-cooking-animations-318012");
        CreateCreditLink(content.transform, "FREE Casual Game SFX Pack", "Dustryroom",
            "https://assetstore.unity.com/packages/audio/sound-fx/free-casual-game-sfx-pack-54116");
        CreateCreditLink(content.transform, "Toony Kitchen & Ingredients Model FREE", "Sigun Studio",
            "https://assetstore.unity.com/packages/3d/props/toony-kitchen-ingredients-model-free-301805");
        CreateCreditLink(content.transform, "Customizable skybox", "Key Mouse",
            "https://assetstore.unity.com/packages/p/customizable-skybox-174576");
        CreateCreditLink(content.transform, "Low Poly Environment - Nature Free", "PolyTope Studio",
            "https://assetstore.unity.com/packages/3d/environments/low-poly-environment-nature-free-lowpoly-medieval-fantasy-series-187052");
        CreateCreditLink(content.transform, "Ultimate Food Pack - Low Poly 3D Food & Kitchen Assets", "AZ Studios",
            "https://assetstore.unity.com/packages/3d/props/food/ultimate-food-pack-low-poly-3d-food-kitchen-assets-403436");

        CreateCreditSection(content.transform, "MUSIC");
        CreateCreditLink(content.transform, "Dreamy", "Jan-Michael Hökenschnieder x Fachhochschule Dortmund",
            "https://freemusicarchive.org/search/?quicksearch=dreamy&search-genre=");
        CreateCreditLink(content.transform, "Peak Hours Calm", "Picratio",
            "https://freemusicarchive.org/search?adv=1&quicksearch=Easy%20listening&&");
        CreateCreditLink(content.transform, "Corporate Candy", "Picratio",
            "https://freemusicarchive.org/search?adv=1&quicksearch=Easy%20listening&&");
        CreateCreditLink(content.transform, "Walkabout", "Picratio",
            "https://freemusicarchive.org/search?adv=1&quicksearch=Easy%20listening&&");

        var scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.content = contentRt;
        scrollRect.viewport = viewportRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 32f;
        GameUITheme.ConfigureScroll(scrollRect);

        var back = CreateMenuButton(card.transform, "CreditsBackButton", "Back", 46f, 340f, 18f, OptionsButtonColor);
        back.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowMain();
        });
        return card;
    }

    static void CreateCreditSection(Transform parent, string text)
    {
        var label = CreateLabel(parent, text.Replace(" ", string.Empty) + "Header", text, 15f,
            TextAlignmentOptions.MidlineLeft);
        label.fontStyle = FontStyles.Bold;
        label.color = GameUITheme.Accent;
        var layout = label.GetComponent<LayoutElement>();
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
    }

    static void CreateCreditLink(Transform parent, string packageName, string artist, string url)
    {
        var button = CreateMenuButton(parent, packageName.Replace(" ", string.Empty) + "Credit",
            packageName + "\n<size=80%>by " + artist + "  •  Open source page</size>",
            64f, 650f, 16f, GameUITheme.Surface);
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
        }
        button.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            Application.OpenURL(url);
        });
    }

    GameObject BuildOptionsPage(Transform parent)
    {
        var card = new GameObject("OptionsPage", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(parent, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(640f, 500f);
        card.GetComponent<Image>().color = PanelColor;
        AddPanelChrome(card);

        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(28, 28, 18, 18);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var title = CreateLabel(card.transform, "OptionsTitle", "Options", 28, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1.2f;
        title.GetComponent<LayoutElement>().preferredHeight = 36f;

        var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabs.transform.SetParent(card.transform, false);
        var tabsLayout = tabs.GetComponent<LayoutElement>();
        tabsLayout.minHeight = 44f;
        tabsLayout.preferredHeight = 44f;
        tabsLayout.flexibleHeight = 0f;
        var tabsH = tabs.GetComponent<HorizontalLayoutGroup>();
        tabsH.spacing = 8f;
        tabsH.childAlignment = TextAnchor.MiddleCenter;
        tabsH.childControlWidth = true;
        tabsH.childControlHeight = true;
        tabsH.childForceExpandWidth = true;
        tabsH.childForceExpandHeight = false;
        audioTab = CreateTabButton(tabs.transform, "AudioTab", "Audio", () => SelectTab(0));
        videoTab = CreateTabButton(tabs.transform, "VideoTab", "Video", () => SelectTab(1));
        graphicsTab = CreateTabButton(tabs.transform, "GraphicsTab", "Graphics", () => SelectTab(2));
        keybindsTab = CreateTabButton(tabs.transform, "KeybindsTab", "Keybinds", () => SelectTab(3));

        var pages = new GameObject("Pages", typeof(RectTransform), typeof(LayoutElement));
        pages.transform.SetParent(card.transform, false);
        var pagesLayout = pages.GetComponent<LayoutElement>();
        pagesLayout.minHeight = 292f;
        pagesLayout.preferredHeight = 292f;
        pagesLayout.flexibleHeight = 0f;

        audioPage = BuildAudioPage(pages.transform);
        videoPage = BuildVideoPage(pages.transform);
        graphicsPage = BuildGraphicsPage(pages.transform);
        keybindsPage = BuildKeybindsPage(pages.transform);

        var back = CreateMenuButton(card.transform, "BackButton", "Back", 46f, 340f, 18f, OptionsButtonColor);
        back.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowMain();
        });
        return card;
    }

    GameObject BuildQuitConfirmation(Transform parent)
    {
        var dim = new GameObject("QuitConfirmation", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        dim.transform.SetParent(parent, false);
        Stretch((RectTransform)dim.transform);
        var dimImage = dim.GetComponent<UnityEngine.UI.Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.72f);
        dimImage.raycastTarget = true;

        var card = new GameObject("ConfirmationCard", typeof(RectTransform), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.VerticalLayoutGroup));
        card.transform.SetParent(dim.transform, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(720f, 270f);
        card.GetComponent<UnityEngine.UI.Image>().color = GameUITheme.Backdrop;
        AddPanelChrome(card);

        var vertical = card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vertical.padding = new RectOffset(32, 32, 28, 28);
        vertical.spacing = 18f;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        quitConfirmationTitle = CreateLabel(card.transform, "Title", "Quit Game?", 28f, TextAlignmentOptions.Center);
        quitConfirmationTitle.fontStyle = FontStyles.Bold;
        quitConfirmationTitle.GetComponent<LayoutElement>().preferredHeight = 40f;

        quitConfirmationBody = CreateLabel(card.transform, "Message", "Are you sure you want to quit?", 18f, TextAlignmentOptions.Center);
        quitConfirmationBody.textWrappingMode = TextWrappingModes.Normal;
        quitConfirmationBody.GetComponent<LayoutElement>().preferredHeight = 64f;

        var actions = new GameObject("Actions", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup),
            typeof(UnityEngine.UI.LayoutElement));
        actions.transform.SetParent(card.transform, false);
        actions.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 52f;
        var horizontal = actions.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        horizontal.spacing = 14f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = true;
        horizontal.childForceExpandHeight = false;

        var cancel = CreateMenuButton(actions.transform, "CancelButton", "Cancel", 52f, 200f, 19f, GameUITheme.Surface);
        cancel.onClick.AddListener(CloseQuitConfirmation);
        var feedback = CreateMenuButton(actions.transform, "FeedbackButton", "Give Feedback", 52f, 220f, 19f, GameUITheme.Positive);
        feedback.onClick.AddListener(OpenFeedbackForm);
        var confirm = CreateMenuButton(actions.transform, "ConfirmButton", "Quit", 52f, 200f, 19f, GameUITheme.Danger);
        confirm.onClick.AddListener(ConfirmQuit);
        return dim;
    }

    GameObject BuildAudioPage(Transform parent)
    {
        var page = new GameObject("AudioPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 8f;
        v.padding = new RectOffset(4, 4, 10, 4);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        masterSlider = CreateVolumeRow(page.transform, "Master", "Master Volume", out masterValueText);
        masterSlider.onValueChanged.AddListener(v0 => OnChannelVolumeChanged(PrefMaster, v0, ApplyMasterVolume, masterValueText));

        voiceSlider = CreateVolumeRow(page.transform, "Voice", "Narrator Voice", out voiceValueText);
        voiceSlider.onValueChanged.AddListener(v0 => OnChannelVolumeChanged(PrefVoice, v0, ApplyVoiceVolume, voiceValueText));

        musicSlider = CreateVolumeRow(page.transform, "Music", "Music", out musicValueText);
        musicSlider.onValueChanged.AddListener(v0 => OnChannelVolumeChanged(PrefMusic, v0, ApplyMusicVolume, musicValueText));

        soundEffectsSlider = CreateVolumeRow(page.transform, "SoundEffects", "Sound Effects", out soundEffectsValueText);
        soundEffectsSlider.onValueChanged.AddListener(v0 => OnChannelVolumeChanged(PrefSoundEffects, v0, ApplySoundEffectsVolume, soundEffectsValueText));

        uiSoundsSlider = CreateVolumeRow(page.transform, "UiSounds", "UI Sounds", out uiSoundsValueText);
        uiSoundsSlider.onValueChanged.AddListener(v0 => OnChannelVolumeChanged(PrefUiSounds, v0, ApplyUiSoundsVolume, uiSoundsValueText));
        return page;
    }

    GameObject BuildVideoPage(Transform parent)
    {
        var page = new GameObject("VideoPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.padding = new RectOffset(4, 4, 10, 4);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var header = CreateLabel(page.transform, "VideoHeader", "Resolution", 16, TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        header.GetComponent<LayoutElement>().preferredHeight = 22f;

        var row = new GameObject("ResolutionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(page.transform, false);
        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 42f;
        rowLe.preferredHeight = 42f;
        rowLe.flexibleHeight = 0f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var prev = CreateSmallButton(row.transform, "PrevRes", "<");
        prev.onClick.AddListener(() => CycleResolution(-1));

        resolutionLabel = CreateLabel(row.transform, "ResolutionLabel", "1920 x 1080", 15, TextAlignmentOptions.Center);
        var resLe = resolutionLabel.GetComponent<LayoutElement>();
        resLe.flexibleWidth = 1f;
        resLe.minWidth = 160f;

        var next = CreateSmallButton(row.transform, "NextRes", ">");
        next.onClick.AddListener(() => CycleResolution(1));

        var apply = CreateMenuButton(page.transform, "ApplyResolution", "Apply", 40f, 260f, 17f, OptionsButtonColor);
        apply.onClick.AddListener(ApplySelectedResolution);

        var fsRow = new GameObject("FullscreenRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        fsRow.transform.SetParent(page.transform, false);
        var fsLe = fsRow.GetComponent<LayoutElement>();
        fsLe.minHeight = 34f;
        fsLe.preferredHeight = 34f;
        fsLe.flexibleHeight = 0f;
        var fsH = fsRow.GetComponent<HorizontalLayoutGroup>();
        fsH.spacing = 10f;
        fsH.childAlignment = TextAnchor.MiddleLeft;
        fsH.childControlWidth = true;
        fsH.childControlHeight = true;
        fsH.childForceExpandWidth = false;

        fullscreenToggle = CreateToggle(fsRow.transform, "FullscreenToggle");
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        var fsLabel = CreateLabel(fsRow.transform, "FullscreenLabel", "Fullscreen", 15, TextAlignmentOptions.Left);
        fsLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return page;
    }

    GameObject BuildGraphicsPage(Transform parent)
    {
        var page = new GameObject("GraphicsPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.padding = new RectOffset(4, 4, 8, 4);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var header = CreateLabel(page.transform, "GraphicsHeader", "Graphics quality", 16, TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        header.GetComponent<LayoutElement>().preferredHeight = 22f;

        graphicsPresetValue = CreateChoiceRow(page.transform, "GraphicsPreset", "Preset",
            () => CycleGraphicsPreset(-1), () => CycleGraphicsPreset(1));
        shadowsToggle = CreateSettingsToggleRow(page.transform, "Shadows", "Realtime shadows", OnShadowsChanged);
        shadowQualityValue = CreateChoiceRow(page.transform, "ShadowQuality", "Shadow quality",
            () => CycleShadowQuality(-1), () => CycleShadowQuality(1));
        antiAliasingValue = CreateChoiceRow(page.transform, "AntiAliasing", "Anti-aliasing",
            () => CycleAntiAliasing(-1), () => CycleAntiAliasing(1));
        textureQualityValue = CreateChoiceRow(page.transform, "TextureQuality", "Texture quality",
            () => CycleTextureQuality(-1), () => CycleTextureQuality(1));
        vSyncToggle = CreateSettingsToggleRow(page.transform, "VSync", "Vertical sync", OnVSyncChanged);
        return page;
    }

    GameObject BuildVisualPage(Transform parent)
    {
        var page = new GameObject("VisualPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 7f;
        v.padding = new RectOffset(4, 4, 8, 4);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var header = CreateLabel(page.transform, "VisualHeader", "Lock wall cutaway", 16, TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        header.GetComponent<LayoutElement>().preferredHeight = 22f;

        var hint = CreateLabel(page.transform, "VisualHint", "Locked walls stay full height and will not drop when the camera faces them.", 13, TextAlignmentOptions.Left);
        hint.textWrappingMode = TextWrappingModes.Normal;
        hint.GetComponent<LayoutElement>().preferredHeight = 32f;

        lockNorthToggle = CreateWallLockRow(page.transform, "LockNorth", "North wall", CameraWallCutaway.WallSide.North);
        lockEastToggle = CreateWallLockRow(page.transform, "LockEast", "East wall", CameraWallCutaway.WallSide.East);
        lockEastToggle.interactable = false;
        lockSouthToggle = CreateWallLockRow(page.transform, "LockSouth", "South wall", CameraWallCutaway.WallSide.South);
        lockWestToggle = CreateWallLockRow(page.transform, "LockWest", "West wall", CameraWallCutaway.WallSide.West);
        return page;
    }

    GameObject BuildKeybindsPage(Transform parent)
    {
        var page = new GameObject("KeybindsPage", typeof(RectTransform));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(page.transform, false);
        var viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(4f, 4f);
        viewportRt.offsetMax = new Vector2(-18f, -4f);
        var viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(GameUITheme.Panel.r, GameUITheme.Panel.g, GameUITheme.Panel.b, 0.72f);
        viewportImage.raycastTarget = true;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = (RectTransform)content.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 7, 7);
        contentLayout.spacing = 4f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateKeybindSection(content.transform, "Camera");
        CreateKeybindRow(content.transform, "Move camera", "WASD / Arrow Keys");
        CreateKeybindRow(content.transform, "Rotate camera", "Hold Right Mouse + Drag");
        CreateKeybindRow(content.transform, "Zoom camera", "Mouse Wheel");

        CreateKeybindSection(content.transform, "Menus");
        CreateKeybindRow(content.transform, "Build / Business / Tasks / Progression", "1 / 2 / 3 / 4");
        CreateKeybindRow(content.transform, "Previous / next submenu", "Q / E");
        CreateKeybindRow(content.transform, "Progression", "J");
        CreateKeybindRow(content.transform, "Pause / back / cancel", "Esc");

        CreateKeybindSection(content.transform, "Building and interaction");
        CreateKeybindRow(content.transform, "Select / interact / place", "Left Mouse");
        CreateKeybindRow(content.transform, "Rotate placement", "R");
        CreateKeybindRow(content.transform, "Cancel moved station", "Right Mouse / Esc");
        CreateKeybindRow(content.transform, "Undo last purchase", "Ctrl + Z");

        CreateKeybindSection(content.transform, "Guidebook");
        CreateKeybindRow(content.transform, "Previous page", "A / Left Arrow");
        CreateKeybindRow(content.transform, "Next page", "D / Right Arrow");

        CreateKeybindSection(content.transform, "Debug");
        CreateKeybindRow(content.transform, "Toggle debug menu", "F1 / `");

        var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        scrollbarGo.transform.SetParent(page.transform, false);
        var scrollbarRt = (RectTransform)scrollbarGo.transform;
        scrollbarRt.anchorMin = new Vector2(1f, 0f);
        scrollbarRt.anchorMax = Vector2.one;
        scrollbarRt.pivot = new Vector2(1f, 0.5f);
        scrollbarRt.offsetMin = new Vector2(-12f, 4f);
        scrollbarRt.offsetMax = new Vector2(-4f, -4f);
        scrollbarGo.GetComponent<Image>().color = GameUITheme.Charcoal;

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(scrollbarGo.transform, false);
        var handleRt = (RectTransform)handle.transform;
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = new Vector2(2f, 2f);
        handleRt.offsetMax = new Vector2(-2f, -2f);
        handle.GetComponent<Image>().color = GameUITheme.SurfaceHover;

        var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var scrollRect = page.AddComponent<ScrollRect>();
        scrollRect.content = contentRt;
        scrollRect.viewport = viewportRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(scrollRect);
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return page;
    }

    static void CreateKeybindSection(Transform parent, string label)
    {
        var text = CreateLabel(parent, label.Replace(" ", string.Empty) + "Header", label.ToUpperInvariant(), 11f, TextAlignmentOptions.MidlineLeft);
        text.fontStyle = FontStyles.Bold;
        text.color = GameUITheme.Accent;
        var layout = text.GetComponent<LayoutElement>();
        layout.minHeight = 22f;
        layout.preferredHeight = 22f;
    }

    static void CreateKeybindRow(Transform parent, string action, string binding)
    {
        var row = new GameObject(action.Replace(" ", string.Empty) + "Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = GameUITheme.Surface;
        row.GetComponent<Image>().raycastTarget = false;
        var rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.minHeight = 30f;
        rowLayout.preferredHeight = 30f;
        rowLayout.flexibleHeight = 0f;

        var horizontal = row.GetComponent<HorizontalLayoutGroup>();
        horizontal.padding = new RectOffset(10, 10, 3, 3);
        horizontal.spacing = 8f;
        horizontal.childAlignment = TextAnchor.MiddleLeft;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = false;

        var actionText = CreateLabel(row.transform, "Action", action, 13f, TextAlignmentOptions.MidlineLeft);
        var actionLayout = actionText.GetComponent<LayoutElement>();
        actionLayout.minWidth = 180f;
        actionLayout.flexibleWidth = 1f;
        actionLayout.minHeight = 22f;
        actionLayout.preferredHeight = 22f;

        var bindingText = CreateLabel(row.transform, "Binding", binding, 13f, TextAlignmentOptions.MidlineRight);
        bindingText.fontStyle = FontStyles.Bold;
        bindingText.color = GameUITheme.Accent;
        var bindingLayout = bindingText.GetComponent<LayoutElement>();
        bindingLayout.minWidth = 190f;
        bindingLayout.preferredWidth = 190f;
        bindingLayout.flexibleWidth = 0f;
        bindingLayout.minHeight = 22f;
        bindingLayout.preferredHeight = 22f;
    }

    Toggle CreateWallLockRow(Transform parent, string id, string label, CameraWallCutaway.WallSide side)
    {
        var row = new GameObject(id + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 32f;
        rowLe.preferredHeight = 32f;
        rowLe.flexibleHeight = 0f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;

        var toggle = CreateToggle(row.transform, id + "Toggle");
        toggle.onValueChanged.AddListener(on =>
        {
            CameraWallCutaway.SetWallLocked(side, on);
            Sfx.Play(SfxId.UiClick);
        });
        var text = CreateLabel(row.transform, id + "Label", label, 15, TextAlignmentOptions.Left);
        text.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return toggle;
    }

    Toggle CreateSettingsToggleRow(Transform parent, string id, string label,
        UnityEngine.Events.UnityAction<bool> onChanged)
    {
        var row = new GameObject(id + "Row", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = GameUITheme.Panel;
        row.GetComponent<Image>().raycastTarget = false;
        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 38f;
        rowLe.preferredHeight = 38f;
        rowLe.flexibleHeight = 0f;

        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.padding = new RectOffset(12, 12, 7, 7);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var toggle = CreateToggle(row.transform, id + "Toggle");
        toggle.onValueChanged.AddListener(onChanged);
        var text = CreateLabel(row.transform, id + "Label", label, 14, TextAlignmentOptions.MidlineLeft);
        text.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return toggle;
    }

    TextMeshProUGUI CreateChoiceRow(Transform parent, string id, string label,
        UnityEngine.Events.UnityAction previous, UnityEngine.Events.UnityAction next)
    {
        var row = new GameObject(id + "Row", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = GameUITheme.Panel;
        row.GetComponent<Image>().raycastTarget = false;
        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 42f;
        rowLe.preferredHeight = 42f;
        rowLe.flexibleHeight = 0f;

        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.padding = new RectOffset(12, 12, 4, 4);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var title = CreateLabel(row.transform, id + "Label", label, 14, TextAlignmentOptions.MidlineLeft);
        var titleLe = title.GetComponent<LayoutElement>();
        titleLe.minWidth = 170f;
        titleLe.preferredWidth = 170f;
        titleLe.flexibleWidth = 1f;

        var prev = CreateSmallButton(row.transform, id + "Previous", "<");
        prev.onClick.AddListener(previous);

        var value = CreateLabel(row.transform, id + "Value", string.Empty, 14, TextAlignmentOptions.Center);
        value.fontStyle = FontStyles.Bold;
        value.color = GameUITheme.Accent;
        var valueLe = value.GetComponent<LayoutElement>();
        valueLe.minWidth = 110f;
        valueLe.preferredWidth = 110f;
        valueLe.flexibleWidth = 0f;

        var nextButton = CreateSmallButton(row.transform, id + "Next", ">");
        nextButton.onClick.AddListener(next);
        return value;
    }

    Slider CreateVolumeRow(Transform parent, string id, string label, out TextMeshProUGUI valueLabel)
    {
        var row = new GameObject(id + "Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = GameUITheme.Panel;
        row.GetComponent<Image>().raycastTarget = false;
        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 52f;
        rowLe.preferredHeight = 52f;
        rowLe.flexibleHeight = 0f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.padding = new RectOffset(12, 12, 8, 8);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var header = CreateLabel(row.transform, id + "Label", label, 14, TextAlignmentOptions.MidlineLeft);
        var headerLe = header.GetComponent<LayoutElement>();
        headerLe.minWidth = 126f;
        headerLe.preferredWidth = 126f;
        headerLe.flexibleWidth = 0f;
        headerLe.minHeight = 24f;
        headerLe.preferredHeight = 24f;

        var slider = CreateSlider(row.transform, id + "Slider");
        var sliderLe = slider.GetComponent<LayoutElement>();
        sliderLe.preferredWidth = 238f;
        sliderLe.flexibleWidth = 1f;
        sliderLe.minWidth = 180f;
        sliderLe.minHeight = 22f;
        sliderLe.preferredHeight = 22f;
        sliderLe.flexibleHeight = 0f;

        valueLabel = CreateLabel(row.transform, id + "Value", "100%", 13, TextAlignmentOptions.Center);
        var valLe = valueLabel.GetComponent<LayoutElement>();
        valLe.preferredWidth = 48f;
        valLe.minWidth = 48f;
        valLe.minHeight = 24f;
        valLe.preferredHeight = 24f;
        return slider;
    }

    void SelectTab(int index, bool playSound = true)
    {
        if (audioPage != null) audioPage.SetActive(index == 0);
        if (videoPage != null) videoPage.SetActive(index == 1);
        if (graphicsPage != null) graphicsPage.SetActive(index == 2);
        if (keybindsPage != null) keybindsPage.SetActive(index == 3);
        HudTabColors.Apply(audioTab, index == 0);
        HudTabColors.Apply(videoTab, index == 1);
        HudTabColors.Apply(graphicsTab, index == 2);
        HudTabColors.Apply(keybindsTab, index == 3);
        if (playSound)
            Sfx.Play(SfxId.UiClick);
    }

    void OnChannelVolumeChanged(string prefKey, float value, System.Action<float> apply, TextMeshProUGUI valueLabel)
    {
        apply(value);
        if (valueLabel != null)
            valueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
        PlayerPrefs.SetFloat(prefKey, value);
        PlayerPrefs.Save();
    }

    void CycleResolution(int delta)
    {
        BuildResolutionList();
        if (uniqueResolutions.Count == 0) return;
        resolutionIndex = (resolutionIndex + delta + uniqueResolutions.Count) % uniqueResolutions.Count;
        RefreshResolutionLabel();
        Sfx.Play(SfxId.UiClick);
    }

    void ApplySelectedResolution()
    {
        BuildResolutionList();
        if (uniqueResolutions.Count == 0 || resolutionIndex < 0 || resolutionIndex >= uniqueResolutions.Count)
            return;

        var res = uniqueResolutions[resolutionIndex];
        bool full = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen;
        Screen.SetResolution(res.width, res.height, full);
        PlayerPrefs.SetInt(PrefWidth, res.width);
        PlayerPrefs.SetInt(PrefHeight, res.height);
        PlayerPrefs.SetInt(PrefFullscreen, full ? 1 : 0);
        PlayerPrefs.Save();
        Sfx.Play(SfxId.UiClick);
        RefreshResolutionLabel();
    }

    void OnFullscreenChanged(bool on)
    {
        Screen.fullScreen = on;
        PlayerPrefs.SetInt(PrefFullscreen, on ? 1 : 0);
        PlayerPrefs.Save();
        Sfx.Play(SfxId.UiClick);
    }

    void OpenFeedbackForm()
    {
        Sfx.Play(SfxId.UiClick);
        Application.OpenURL(FeedbackUrl);
    }

    void OnQuitToMenu()
    {
        ShowQuitConfirmation(
            "Quit to Menu?",
            "Your current game will be saved before returning to the title screen.",
            QuitToMenuConfirmed);
    }

    void QuitToMenuConfirmed()
    {
        Sfx.Play(SfxId.UiClick);
        GameSaveSlots.SaveActiveSlot();

        // Reloading the gameplay scene rebuilds its original title-screen state and
        // clears all runtime-only objects without closing the application.
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
            SceneManager.LoadScene(activeScene.buildIndex);
        else
            SceneManager.LoadScene(activeScene.name);
    }

    public void ShowQuitConfirmation(string title, string message, System.Action onConfirm)
    {
        EnsureUi();
        if (overlay == null || quitConfirmation == null) return;

        confirmationOwnsOverlay = !overlay.activeSelf;
        confirmationPausedGame = !visible && !IsTitleVisible();
        if (confirmationPausedGame)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.RequestExternalPause(PauseSource);
            else
                Time.timeScale = 0f;
        }
        confirmedQuitAction = onConfirm;
        if (quitConfirmationTitle != null) quitConfirmationTitle.text = title;
        if (quitConfirmationBody != null) quitConfirmationBody.text = message;
        overlay.SetActive(true);
        quitConfirmation.SetActive(true);
        quitConfirmation.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        GameUITheme.ApplyTo(quitConfirmation.transform);
        Sfx.Play(SfxId.UiOpen);
    }

    bool HandleApplicationQuitRequest()
    {
#if UNITY_EDITOR
        return true;
#else
        if (allowApplicationQuit) return true;
        ShowQuitConfirmation(
            "Quit Game?",
            "Are you sure you want to exit the game?",
            ConfirmApplicationQuit);
        return false;
#endif
    }

    public void ConfirmApplicationQuit()
    {
        allowApplicationQuit = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void CloseQuitConfirmation()
    {
        if (quitConfirmation != null) quitConfirmation.SetActive(false);
        confirmedQuitAction = null;
        if (confirmationOwnsOverlay && overlay != null)
            overlay.SetActive(false);
        if (confirmationPausedGame)
        {
            if (GameTimeManager.Instance != null)
                GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
            else
                Time.timeScale = 1f;
        }
        confirmationOwnsOverlay = false;
        confirmationPausedGame = false;
        Sfx.Play(SfxId.UiClose);
    }

    void ConfirmQuit()
    {
        System.Action action = confirmedQuitAction;
        confirmedQuitAction = null;
        if (quitConfirmation != null) quitConfirmation.SetActive(false);
        confirmationOwnsOverlay = false;
        action?.Invoke();
    }

    void RefreshAudioControls()
    {
        float master = ReadVolume(PrefMaster, PrefVolumeLegacy, 1f);
        float voice = ReadVolume(PrefVoice, null, 1f);
        float music = ReadVolume(PrefMusic, null, 1f);
        float soundEffects = ReadVolume(PrefSoundEffects, null, 1f);
        float uiSounds = ReadVolume(PrefUiSounds, null, 1f);

        SetSlider(masterSlider, masterValueText, master);
        SetSlider(voiceSlider, voiceValueText, voice);
        SetSlider(musicSlider, musicValueText, music);
        SetSlider(soundEffectsSlider, soundEffectsValueText, soundEffects);
        SetSlider(uiSoundsSlider, uiSoundsValueText, uiSounds);

        ApplyMasterVolume(master);
        ApplyVoiceVolume(voice);
        ApplyMusicVolume(music);
        ApplySoundEffectsVolume(soundEffects);
        ApplyUiSoundsVolume(uiSounds);
    }

    void RefreshVideoControls()
    {
        BuildResolutionList();
        MatchCurrentResolutionIndex();
        RefreshResolutionLabel();
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
    }

    void RefreshGraphicsControls()
    {
        bool shadows = PlayerPrefs.GetInt(PrefShadows, 1) == 1;
        shadowQualityIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefShadowQuality, 1), 0, 2);
        antiAliasingIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefAntiAliasing, 2), 0, 3);
        textureQualityIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefTextureQuality, 0), 0, 2);
        bool vSync = PlayerPrefs.GetInt(PrefVSync, 1) == 1;
        int preset = Mathf.Clamp(PlayerPrefs.GetInt(PrefGraphicsPreset, 2), -1, 3);

        if (graphicsPresetValue != null)
            graphicsPresetValue.text = GraphicsPresetName(preset);
        if (shadowsToggle != null)
            shadowsToggle.SetIsOnWithoutNotify(shadows);
        if (vSyncToggle != null)
            vSyncToggle.SetIsOnWithoutNotify(vSync);
        if (shadowQualityValue != null)
            shadowQualityValue.text = ShadowQualityName(shadowQualityIndex);
        if (antiAliasingValue != null)
            antiAliasingValue.text = AntiAliasingName(antiAliasingIndex);
        if (textureQualityValue != null)
            textureQualityValue.text = TextureQualityName(textureQualityIndex);

        SetShadowQualityInteractable(shadows);
        ApplyGraphicsSettings(shadows, shadowQualityIndex, antiAliasingIndex, textureQualityIndex, vSync);
    }

    void OnShadowsChanged(bool enabled)
    {
        MarkGraphicsPresetCustom();
        PlayerPrefs.SetInt(PrefShadows, enabled ? 1 : 0);
        PlayerPrefs.Save();
        SetShadowQualityInteractable(enabled);
        ApplySavedGraphics();
        Sfx.Play(SfxId.UiClick);
    }

    void OnVSyncChanged(bool enabled)
    {
        MarkGraphicsPresetCustom();
        PlayerPrefs.SetInt(PrefVSync, enabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplySavedGraphics();
        Sfx.Play(SfxId.UiClick);
    }

    void CycleShadowQuality(int delta)
    {
        if (shadowsToggle != null && !shadowsToggle.isOn) return;
        MarkGraphicsPresetCustom();
        shadowQualityIndex = WrapIndex(shadowQualityIndex + delta, 3);
        PlayerPrefs.SetInt(PrefShadowQuality, shadowQualityIndex);
        PlayerPrefs.Save();
        if (shadowQualityValue != null)
            shadowQualityValue.text = ShadowQualityName(shadowQualityIndex);
        ApplySavedGraphics();
        Sfx.Play(SfxId.UiClick);
    }

    void CycleAntiAliasing(int delta)
    {
        MarkGraphicsPresetCustom();
        antiAliasingIndex = WrapIndex(antiAliasingIndex + delta, 4);
        PlayerPrefs.SetInt(PrefAntiAliasing, antiAliasingIndex);
        PlayerPrefs.Save();
        if (antiAliasingValue != null)
            antiAliasingValue.text = AntiAliasingName(antiAliasingIndex);
        ApplySavedGraphics();
        Sfx.Play(SfxId.UiClick);
    }

    void CycleTextureQuality(int delta)
    {
        MarkGraphicsPresetCustom();
        textureQualityIndex = WrapIndex(textureQualityIndex + delta, 3);
        PlayerPrefs.SetInt(PrefTextureQuality, textureQualityIndex);
        PlayerPrefs.Save();
        if (textureQualityValue != null)
            textureQualityValue.text = TextureQualityName(textureQualityIndex);
        ApplySavedGraphics();
        Sfx.Play(SfxId.UiClick);
    }

    void CycleGraphicsPreset(int delta)
    {
        int current = PlayerPrefs.GetInt(PrefGraphicsPreset, 2);
        if (current < 0 || current > 3)
            current = delta >= 0 ? -1 : 0;
        ApplyGraphicsPreset(WrapIndex(current + delta, 4));
        Sfx.Play(SfxId.UiClick);
    }

    void ApplyGraphicsPreset(int preset)
    {
        preset = Mathf.Clamp(preset, 0, 3);

        // Low, Medium, High, Ultra. Texture quality uses Unity's mip limit,
        // where zero is full resolution and larger values reduce memory use.
        bool shadows = preset > 0;
        int shadowQuality = preset <= 1 ? 0 : preset - 1;
        int antiAliasing = preset;
        int textureQuality = preset == 0 ? 2 : preset == 1 ? 1 : 0;
        bool vSync = preset > 0;

        PlayerPrefs.SetInt(PrefGraphicsPreset, preset);
        PlayerPrefs.SetInt(PrefShadows, shadows ? 1 : 0);
        PlayerPrefs.SetInt(PrefShadowQuality, shadowQuality);
        PlayerPrefs.SetInt(PrefAntiAliasing, antiAliasing);
        PlayerPrefs.SetInt(PrefTextureQuality, textureQuality);
        PlayerPrefs.SetInt(PrefVSync, vSync ? 1 : 0);
        PlayerPrefs.Save();
        RefreshGraphicsControls();
    }

    void MarkGraphicsPresetCustom()
    {
        PlayerPrefs.SetInt(PrefGraphicsPreset, -1);
        if (graphicsPresetValue != null)
            graphicsPresetValue.text = GraphicsPresetName(-1);
    }

    void SetShadowQualityInteractable(bool enabled)
    {
        if (shadowQualityValue == null) return;
        Transform row = shadowQualityValue.transform.parent;
        if (row == null) return;
        foreach (var button in row.GetComponentsInChildren<Button>(true))
            button.interactable = enabled;
        shadowQualityValue.color = enabled ? GameUITheme.Accent : GameUITheme.TextSecondary;
    }

    static int WrapIndex(int value, int count)
    {
        if (count <= 0) return 0;
        return (value % count + count) % count;
    }

    static string ShadowQualityName(int index) => index switch
    {
        0 => "Low",
        2 => "High",
        _ => "Medium"
    };

    static string AntiAliasingName(int index) => index switch
    {
        0 => "Off",
        1 => "2x MSAA",
        3 => "8x MSAA",
        _ => "4x MSAA"
    };

    static string TextureQualityName(int index) => index switch
    {
        1 => "Half",
        2 => "Quarter",
        _ => "Full"
    };

    static string GraphicsPresetName(int index) => index switch
    {
        0 => "Low",
        1 => "Medium",
        2 => "High",
        3 => "Ultra",
        _ => "Custom"
    };

    void RefreshVisualControls()
    {
        if (lockNorthToggle != null)
            lockNorthToggle.SetIsOnWithoutNotify(CameraWallCutaway.IsWallLocked(CameraWallCutaway.WallSide.North));
        if (lockEastToggle != null)
            lockEastToggle.SetIsOnWithoutNotify(CameraWallCutaway.IsWallLocked(CameraWallCutaway.WallSide.East));
        if (lockSouthToggle != null)
            lockSouthToggle.SetIsOnWithoutNotify(CameraWallCutaway.IsWallLocked(CameraWallCutaway.WallSide.South));
        if (lockWestToggle != null)
            lockWestToggle.SetIsOnWithoutNotify(CameraWallCutaway.IsWallLocked(CameraWallCutaway.WallSide.West));
    }

    void RefreshResolutionLabel()
    {
        if (resolutionLabel == null) return;
        if (uniqueResolutions.Count == 0 || resolutionIndex < 0 || resolutionIndex >= uniqueResolutions.Count)
        {
            resolutionLabel.text = Screen.width + " x " + Screen.height;
            return;
        }
        var res = uniqueResolutions[resolutionIndex];
        resolutionLabel.text = res.width + " x " + res.height;
    }

    void BuildResolutionList()
    {
        uniqueResolutions.Clear();
        var seen = new HashSet<string>();
        foreach (var res in Screen.resolutions)
        {
            if (res.width < 800 || res.height < 600) continue;
            string key = res.width + "x" + res.height;
            if (!seen.Add(key))
            {
                int existing = uniqueResolutions.FindIndex(r => r.width == res.width && r.height == res.height);
                if (existing >= 0 && RefreshOf(res) > RefreshOf(uniqueResolutions[existing]))
                    uniqueResolutions[existing] = res;
                continue;
            }
            uniqueResolutions.Add(res);
        }
        uniqueResolutions.Sort((a, b) =>
        {
            int c = a.width.CompareTo(b.width);
            return c != 0 ? c : a.height.CompareTo(b.height);
        });
    }

    void MatchCurrentResolutionIndex()
    {
        resolutionIndex = 0;
        for (int i = 0; i < uniqueResolutions.Count; i++)
        {
            if (uniqueResolutions[i].width == Screen.width && uniqueResolutions[i].height == Screen.height)
            {
                resolutionIndex = i;
                return;
            }
        }
    }

    static int RefreshOf(Resolution res)
    {
        return Mathf.RoundToInt((float)res.refreshRateRatio.value);
    }

    static void SetSlider(Slider slider, TextMeshProUGUI valueLabel, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(value);
        if (valueLabel != null)
            valueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    static float ReadVolume(string key, string legacyKey, float fallback)
    {
        if (PlayerPrefs.HasKey(key))
            return PlayerPrefs.GetFloat(key, fallback);
        if (!string.IsNullOrEmpty(legacyKey) && PlayerPrefs.HasKey(legacyKey))
            return PlayerPrefs.GetFloat(legacyKey, fallback);
        return fallback;
    }

    static void ApplyMasterVolume(float value)
    {
        AudioListener.volume = Mathf.Clamp01(value);
    }

    static void ApplyVoiceVolume(float value)
    {
        var voice = TutorialVoiceManager.Instance ?? FindFirstObjectByType<TutorialVoiceManager>();
        if (voice != null)
            voice.SetVolume(value);
    }

    static void ApplyMusicVolume(float value)
    {
        if (MusicManager.Instance != null)
            MusicManager.Instance.SetVolume(Mathf.Clamp01(value) * MusicFullVolume);
    }

    static void ApplySoundEffectsVolume(float value)
    {
        var effects = SfxManager.Instance ?? FindFirstObjectByType<SfxManager>();
        if (effects != null)
            effects.SetSoundEffectsVolume(value);
    }

    static void ApplyUiSoundsVolume(float value)
    {
        var effects = SfxManager.Instance ?? FindFirstObjectByType<SfxManager>();
        if (effects != null)
            effects.SetUiVolume(value);
    }

    static void ApplySavedAudio()
    {
        ApplyMasterVolume(ReadVolume(PrefMaster, PrefVolumeLegacy, 1f));
        ApplyVoiceVolume(ReadVolume(PrefVoice, null, 1f));
        ApplyMusicVolume(ReadVolume(PrefMusic, null, 1f));
        ApplySoundEffectsVolume(ReadVolume(PrefSoundEffects, null, 1f));
        ApplyUiSoundsVolume(ReadVolume(PrefUiSounds, null, 1f));
    }

    static void ApplySavedVideo(bool force)
    {
        if (!force && !PlayerPrefs.HasKey(PrefWidth)) return;
        int w = PlayerPrefs.GetInt(PrefWidth, Screen.width);
        int h = PlayerPrefs.GetInt(PrefHeight, Screen.height);
        bool full = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        Screen.SetResolution(w, h, full);
    }

    static void ApplySavedGraphics()
    {
        bool shadows = PlayerPrefs.GetInt(PrefShadows, 1) == 1;
        int shadowQuality = Mathf.Clamp(PlayerPrefs.GetInt(PrefShadowQuality, 1), 0, 2);
        int antiAliasing = Mathf.Clamp(PlayerPrefs.GetInt(PrefAntiAliasing, 2), 0, 3);
        int textureQuality = Mathf.Clamp(PlayerPrefs.GetInt(PrefTextureQuality, 0), 0, 2);
        bool vSync = PlayerPrefs.GetInt(PrefVSync, 1) == 1;
        ApplyGraphicsSettings(shadows, shadowQuality, antiAliasing, textureQuality, vSync);
    }

    static void ApplyGraphicsSettings(bool shadows, int shadowQuality, int antiAliasing,
        int textureQuality, bool vSync)
    {
        int[] shadowResolutions = { 1024, 2048, 4096 };
        float[] shadowDistances = { 40f, 70f, 100f };
        int[] msaaSamples = { 1, 2, 4, 8 };

        shadowQuality = Mathf.Clamp(shadowQuality, 0, shadowResolutions.Length - 1);
        antiAliasing = Mathf.Clamp(antiAliasing, 0, msaaSamples.Length - 1);
        textureQuality = Mathf.Clamp(textureQuality, 0, 2);

        QualitySettings.shadows = shadows
            ? (shadowQuality == 0 ? UnityEngine.ShadowQuality.HardOnly : UnityEngine.ShadowQuality.All)
            : UnityEngine.ShadowQuality.Disable;
        QualitySettings.shadowResolution = shadowQuality switch
        {
            0 => UnityEngine.ShadowResolution.Low,
            2 => UnityEngine.ShadowResolution.VeryHigh,
            _ => UnityEngine.ShadowResolution.High
        };
        QualitySettings.shadowDistance = shadows ? shadowDistances[shadowQuality] : 0f;
        QualitySettings.globalTextureMipmapLimit = textureQuality;
        QualitySettings.antiAliasing = antiAliasing == 0 ? 0 : msaaSamples[antiAliasing];
        QualitySettings.vSyncCount = vSync ? 1 : 0;

        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
            pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null) return;

        pipeline.shadowDistance = shadows ? shadowDistances[shadowQuality] : 0f;
        pipeline.mainLightShadowmapResolution = shadowResolutions[shadowQuality];
        pipeline.msaaSampleCount = msaaSamples[antiAliasing];

        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light == null || light.type != LightType.Directional) continue;
            light.shadows = !shadows
                ? LightShadows.None
                : shadowQuality == 0 ? LightShadows.Hard : LightShadows.Soft;
        }
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void AddPanelChrome(GameObject panel)
    {
        if (panel == null) return;
        var outline = panel.GetComponent<Outline>();
        if (outline == null) outline = panel.AddComponent<Outline>();
        outline.effectColor = GameUITheme.Edge;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        var shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(0f, -6f);
        shadow.useGraphicAlpha = true;
    }

    static void SetPanelChromeEnabled(GameObject panel, bool enabled)
    {
        if (panel == null) return;
        foreach (var effect in panel.GetComponents<Shadow>())
            if (effect != null) effect.enabled = enabled;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = size + 6f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        GameUITheme.ApplyTitleScreenFont(tmp);
        return tmp;
    }

    static Button CreateMenuButton(Transform parent, string name, string label, float height = 52f, float width = 260f, float fontSize = 22f, Color? fill = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        le.preferredWidth = width;
        var color = fill ?? ButtonColor;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        var colors = btn.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
        colors.selectedColor = color;
        btn.colors = colors;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)textGo.transform);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = ButtonTextColor;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        GameUITheme.ApplyTitleScreenFont(tmp);
        return btn;
    }

    static Button CreateTabButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
    {
        var btn = CreateSmallButton(parent, name, label);
        var layout = btn.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.preferredWidth = 120f;
        layout.preferredHeight = 44f;
        layout.minHeight = 44f;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    static Button CreateSmallButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 38f;
        le.minHeight = 38f;
        le.flexibleHeight = 0f;
        le.preferredWidth = 42f;
        le.minWidth = 42f;
        var img = go.GetComponent<Image>();
        img.color = ButtonColor;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)textGo.transform);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = ButtonTextColor;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        GameUITheme.ApplyTitleScreenFont(tmp);
        return btn;
    }

    static Slider CreateSlider(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var sliderLayout = go.GetComponent<LayoutElement>();
        sliderLayout.minHeight = 22f;
        sliderLayout.preferredHeight = 22f;
        sliderLayout.flexibleHeight = 0f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        var bgRt = (RectTransform)bg.transform;
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.offsetMin = new Vector2(0f, -3f);
        bgRt.offsetMax = new Vector2(0f, 3f);
        bg.GetComponent<Image>().color = GameUITheme.Charcoal;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = (RectTransform)fillArea.transform;
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.offsetMin = new Vector2(0f, -3f);
        faRt.offsetMax = new Vector2(-8f, 3f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch((RectTransform)fill.transform);
        fill.GetComponent<Image>().color = GameUITheme.Accent;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)handleArea.transform);
        var handleAreaRt = (RectTransform)handleArea.transform;
        handleAreaRt.offsetMin = new Vector2(8f, 0f);
        handleAreaRt.offsetMax = new Vector2(-8f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = (RectTransform)handle.transform;
        handleRt.anchorMin = new Vector2(0.5f, 0.5f);
        handleRt.anchorMax = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(16f, 20f);
        handle.GetComponent<Image>().color = GameUITheme.TextPrimary;

        var slider = go.GetComponent<Slider>();
        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;
        return slider;
    }

    static Toggle CreateToggle(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Toggle), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 22f;
        le.preferredHeight = 22f;
        le.minWidth = 22f;
        le.minHeight = 22f;
        le.flexibleHeight = 0f;
        go.GetComponent<Image>().color = GameUITheme.Surface;

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(go.transform, false);
        Stretch((RectTransform)check.transform);
        var inset = (RectTransform)check.transform;
        inset.offsetMin = new Vector2(4f, 4f);
        inset.offsetMax = new Vector2(-4f, -4f);
        check.GetComponent<Image>().color = GameUITheme.PositiveAccent;

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = go.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = Screen.fullScreen;
        return toggle;
    }
}
