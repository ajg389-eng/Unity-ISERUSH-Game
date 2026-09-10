using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Escape pause overlay. Main screen matches a simple Resume / Options / Quit list.
/// Options holds Audio (volume sliders) and Video (resolution).
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    public const string PauseSource = GameTimeManager.PauseMenu;
    const string PrefMaster = "PauseMenu.MasterVolume";
    const string PrefVoice = "PauseMenu.VoiceVolume";
    const string PrefMusic = "PauseMenu.MusicVolume";
    const string PrefVolumeLegacy = "PauseMenu.Volume";
    const string PrefWidth = "PauseMenu.Width";
    const string PrefHeight = "PauseMenu.Height";
    const string PrefFullscreen = "PauseMenu.Fullscreen";
    const float MusicFullVolume = 0.35f;

    static readonly Color ButtonColor = new Color(0.82f, 0.82f, 0.84f, 1f);
    static readonly Color MainButtonColor = new Color(0.48f, 0.48f, 0.51f, 1f);
    static readonly Color MainBoxColor = new Color(0.16f, 0.16f, 0.18f, 0.95f);
    static readonly Color ButtonTextColor = new Color(0.08f, 0.08f, 0.1f, 1f);
    static readonly Color PanelColor = new Color(0.12f, 0.13f, 0.18f, 0.96f);

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
    GameObject audioPage;
    GameObject videoPage;
    Button audioTab;
    Button videoTab;
    Slider masterSlider;
    Slider voiceSlider;
    Slider musicSlider;
    TextMeshProUGUI masterValueText;
    TextMeshProUGUI voiceValueText;
    TextMeshProUGUI musicValueText;
    TextMeshProUGUI resolutionLabel;
    Toggle fullscreenToggle;
    readonly List<Resolution> uniqueResolutions = new List<Resolution>();
    int resolutionIndex;
    bool visible;
    bool built;
    bool showingOptions;

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
        ApplySavedAudio();
        ApplySavedVideo(false);
    }

    void OnDestroy()
    {
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
        bool escape = Input.GetKeyDown(KeyCode.Escape);
        if (escape && !EscapeHandledThisFrame && !IsTitleVisible())
        {
            if (!visible)
                Show();
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

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        visible = true;
        ShowMain();
        RefreshAudioControls();
        RefreshVideoControls();
        Sfx.Play(SfxId.UiOpen);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.RequestExternalPause(PauseSource);
        else
            Time.timeScale = 0f;
    }

    public void Hide(bool playSound = true)
    {
        visible = false;
        showingOptions = false;
        if (overlay != null)
            overlay.SetActive(false);

        if (playSound)
            Sfx.Play(SfxId.UiClose);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
    }

    void ShowMain()
    {
        showingOptions = false;
        if (mainPage != null) mainPage.SetActive(true);
        if (optionsPage != null) optionsPage.SetActive(false);
    }

    void ShowOptions()
    {
        showingOptions = true;
        if (mainPage != null) mainPage.SetActive(false);
        if (optionsPage != null) optionsPage.SetActive(true);
        SelectTab(0, playSound: false);
        RefreshAudioControls();
        RefreshVideoControls();
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
        scaler.matchWidthOrHeight = 0.5f;

        overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasGo.transform, false);
        Stretch((RectTransform)overlay.transform);
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        overlay.GetComponent<Image>().raycastTarget = true;

        mainPage = BuildMainPage(overlay.transform);
        optionsPage = BuildOptionsPage(overlay.transform);
        optionsPage.SetActive(false);
    }

    GameObject BuildMainPage(Transform parent)
    {
        var page = new GameObject("MainPage", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        page.transform.SetParent(parent, false);
        var rt = (RectTransform)page.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(360f, 280f);
        page.GetComponent<Image>().color = MainBoxColor;

        var vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 22, 22);
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = page.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var resume = CreateMenuButton(page.transform, "ResumeButton", "Resume", 64f, 320f, 26f, MainButtonColor);
        resume.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            Hide(playSound: false);
        });

        var options = CreateMenuButton(page.transform, "OptionsButton", "Options", 64f, 320f, 26f, MainButtonColor);
        options.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowOptions();
        });

        var quit = CreateMenuButton(page.transform, "QuitButton", "Quit", 64f, 320f, 26f, MainButtonColor);
        quit.onClick.AddListener(OnQuit);
        return page;
    }

    GameObject BuildOptionsPage(Transform parent)
    {
        var card = new GameObject("OptionsPage", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(parent, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(420f, 430f);
        card.GetComponent<Image>().color = PanelColor;

        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 18, 16);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var title = CreateLabel(card.transform, "OptionsTitle", "Options", 24, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.GetComponent<LayoutElement>().preferredHeight = 32f;

        var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabs.transform.SetParent(card.transform, false);
        tabs.GetComponent<LayoutElement>().preferredHeight = 36f;
        var tabsH = tabs.GetComponent<HorizontalLayoutGroup>();
        tabsH.spacing = 8f;
        tabsH.childAlignment = TextAnchor.MiddleCenter;
        tabsH.childControlWidth = true;
        tabsH.childControlHeight = true;
        tabsH.childForceExpandWidth = true;
        tabsH.childForceExpandHeight = true;
        audioTab = CreateTabButton(tabs.transform, "AudioTab", "Audio", () => SelectTab(0));
        videoTab = CreateTabButton(tabs.transform, "VideoTab", "Video", () => SelectTab(1));

        var pages = new GameObject("Pages", typeof(RectTransform), typeof(LayoutElement));
        pages.transform.SetParent(card.transform, false);
        pages.GetComponent<LayoutElement>().preferredHeight = 250f;
        pages.GetComponent<LayoutElement>().flexibleHeight = 1f;

        audioPage = BuildAudioPage(pages.transform);
        videoPage = BuildVideoPage(pages.transform);

        var back = CreateMenuButton(card.transform, "BackButton", "Back");
        back.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            ShowMain();
        });
        return card;
    }

    GameObject BuildAudioPage(Transform parent)
    {
        var page = new GameObject("AudioPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 10f;
        v.padding = new RectOffset(4, 4, 8, 4);
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
        return page;
    }

    GameObject BuildVideoPage(Transform parent)
    {
        var page = new GameObject("VideoPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 12f;
        v.padding = new RectOffset(4, 4, 8, 4);
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
        row.GetComponent<LayoutElement>().preferredHeight = 36f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var prev = CreateSmallButton(row.transform, "PrevRes", "<");
        prev.onClick.AddListener(() => CycleResolution(-1));

        resolutionLabel = CreateLabel(row.transform, "ResolutionLabel", "1920 x 1080", 15, TextAlignmentOptions.Center);
        var resLe = resolutionLabel.GetComponent<LayoutElement>();
        resLe.flexibleWidth = 1f;
        resLe.minWidth = 160f;

        var next = CreateSmallButton(row.transform, "NextRes", ">");
        next.onClick.AddListener(() => CycleResolution(1));

        var apply = CreateMenuButton(page.transform, "ApplyResolution", "Apply");
        apply.GetComponent<LayoutElement>().preferredHeight = 40f;
        apply.onClick.AddListener(ApplySelectedResolution);

        var fsRow = new GameObject("FullscreenRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        fsRow.transform.SetParent(page.transform, false);
        fsRow.GetComponent<LayoutElement>().preferredHeight = 28f;
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

    Slider CreateVolumeRow(Transform parent, string id, string label, out TextMeshProUGUI valueLabel)
    {
        var block = new GameObject(id + "Block", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        block.transform.SetParent(parent, false);
        block.GetComponent<LayoutElement>().preferredHeight = 52f;
        var bv = block.GetComponent<VerticalLayoutGroup>();
        bv.spacing = 4f;
        bv.childAlignment = TextAnchor.UpperLeft;
        bv.childControlWidth = true;
        bv.childControlHeight = true;
        bv.childForceExpandWidth = true;
        bv.childForceExpandHeight = false;

        var header = CreateLabel(block.transform, id + "Label", label, 14, TextAlignmentOptions.Left);
        header.GetComponent<LayoutElement>().preferredHeight = 18f;

        var row = new GameObject(id + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(block.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = 18f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.padding = new RectOffset(0, 40, 0, 0);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var slider = CreateSlider(row.transform, id + "Slider");
        var sliderLe = slider.GetComponent<LayoutElement>();
        sliderLe.preferredWidth = 210f;
        sliderLe.flexibleWidth = 0f;
        sliderLe.minWidth = 210f;
        sliderLe.preferredHeight = 14f;

        valueLabel = CreateLabel(row.transform, id + "Value", "100%", 13, TextAlignmentOptions.MidlineRight);
        var valLe = valueLabel.GetComponent<LayoutElement>();
        valLe.preferredWidth = 44f;
        valLe.minWidth = 44f;
        return slider;
    }

    void SelectTab(int index, bool playSound = true)
    {
        if (audioPage != null) audioPage.SetActive(index == 0);
        if (videoPage != null) videoPage.SetActive(index == 1);
        HudTabColors.Apply(audioTab, index == 0);
        HudTabColors.Apply(videoTab, index == 1);
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

    void OnQuit()
    {
        Sfx.Play(SfxId.UiClick);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void RefreshAudioControls()
    {
        float master = ReadVolume(PrefMaster, PrefVolumeLegacy, 1f);
        float voice = ReadVolume(PrefVoice, null, 1f);
        float music = ReadVolume(PrefMusic, null, 1f);

        SetSlider(masterSlider, masterValueText, master);
        SetSlider(voiceSlider, voiceValueText, voice);
        SetSlider(musicSlider, musicValueText, music);

        ApplyMasterVolume(master);
        ApplyVoiceVolume(voice);
        ApplyMusicVolume(music);
    }

    void RefreshVideoControls()
    {
        BuildResolutionList();
        MatchCurrentResolutionIndex();
        RefreshResolutionLabel();
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
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

    static void ApplySavedAudio()
    {
        ApplyMasterVolume(ReadVolume(PrefMaster, PrefVolumeLegacy, 1f));
        ApplyVoiceVolume(ReadVolume(PrefVoice, null, 1f));
        ApplyMusicVolume(ReadVolume(PrefMusic, null, 1f));
    }

    static void ApplySavedVideo(bool force)
    {
        if (!force && !PlayerPrefs.HasKey(PrefWidth)) return;
        int w = PlayerPrefs.GetInt(PrefWidth, Screen.width);
        int h = PlayerPrefs.GetInt(PrefHeight, Screen.height);
        bool full = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        Screen.SetResolution(w, h, full);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
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
        var colors = btn.colors;
        colors.highlightedColor = color * 1.12f;
        colors.pressedColor = color * 0.85f;
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
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Button CreateTabButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
    {
        var btn = CreateSmallButton(parent, name, label);
        btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        btn.GetComponent<LayoutElement>().preferredWidth = 120f;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    static Button CreateSmallButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 36f;
        le.minHeight = 36f;
        le.preferredWidth = 44f;
        var img = go.GetComponent<Image>();
        img.color = ButtonColor;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

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
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Slider CreateSlider(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 14f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        Stretch((RectTransform)bg.transform);
        var bgRt = (RectTransform)bg.transform;
        bgRt.offsetMin = new Vector2(0f, 4f);
        bgRt.offsetMax = new Vector2(0f, -4f);
        bg.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.3f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = (RectTransform)fillArea.transform;
        Stretch(faRt);
        faRt.offsetMin = new Vector2(0f, 4f);
        faRt.offsetMax = new Vector2(-8f, -4f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch((RectTransform)fill.transform);
        fill.GetComponent<Image>().color = new Color(0.55f, 0.62f, 0.78f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)handleArea.transform);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = (RectTransform)handle.transform;
        handleRt.sizeDelta = new Vector2(12f, 12f);
        handle.GetComponent<Image>().color = Color.white;

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
        le.preferredWidth = 24f;
        le.preferredHeight = 24f;
        le.minWidth = 24f;
        go.GetComponent<Image>().color = ButtonColor;

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(go.transform, false);
        Stretch((RectTransform)check.transform);
        var inset = (RectTransform)check.transform;
        inset.offsetMin = new Vector2(4f, 4f);
        inset.offsetMax = new Vector2(-4f, -4f);
        check.GetComponent<Image>().color = ButtonTextColor;

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = go.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = Screen.fullScreen;
        return toggle;
    }
}
