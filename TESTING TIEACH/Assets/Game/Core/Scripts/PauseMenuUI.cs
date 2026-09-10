using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Escape pause overlay with Audio (volume) and Video (resolution) tabs.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    public const string PauseSource = GameTimeManager.PauseMenu;
    const string PrefVolume = "PauseMenu.Volume";
    const string PrefWidth = "PauseMenu.Width";
    const string PrefHeight = "PauseMenu.Height";
    const string PrefFullscreen = "PauseMenu.Fullscreen";

    public static PauseMenuUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.visible;
    public static bool EscapeHandledThisFrame { get; private set; }

    public static void MarkEscapeHandled()
    {
        EscapeHandledThisFrame = true;
    }

    GameObject overlay;
    GameObject audioPage;
    GameObject videoPage;
    Button audioTab;
    Button videoTab;
    Slider volumeSlider;
    TextMeshProUGUI volumeValueText;
    TextMeshProUGUI resolutionLabel;
    Toggle fullscreenToggle;
    readonly List<Resolution> uniqueResolutions = new List<Resolution>();
    int resolutionIndex;
    bool visible;
    bool built;

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
            if (visible)
                Hide();
            else
                Show();
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
        SelectTab(0, playSound: false);
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
        if (overlay != null)
            overlay.SetActive(false);

        if (playSound)
            Sfx.Play(SfxId.UiClose);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(PauseSource);
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
        var overlayRt = (RectTransform)overlay.transform;
        Stretch(overlayRt);
        overlay.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.78f);
        overlay.GetComponent<Image>().raycastTarget = true;

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(overlay.transform, false);
        var cardRt = (RectTransform)card.transform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(520f, 420f);
        card.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.18f, 0.98f);
        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 22, 20);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var title = CreateLabel(card.transform, "Title", "Paused", 28, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.GetComponent<LayoutElement>().preferredHeight = 36f;

        var tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabs.transform.SetParent(card.transform, false);
        tabs.GetComponent<LayoutElement>().preferredHeight = 40f;
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
        pages.GetComponent<LayoutElement>().preferredHeight = 220f;
        pages.GetComponent<LayoutElement>().flexibleHeight = 1f;

        audioPage = BuildAudioPage(pages.transform);
        videoPage = BuildVideoPage(pages.transform);

        var resume = CreateButton(card.transform, "ResumeButton", "Resume", new Color(0.28f, 0.48f, 0.36f, 1f));
        resume.onClick.AddListener(() =>
        {
            Sfx.Play(SfxId.UiClick);
            Hide(playSound: false);
        });
    }

    GameObject BuildAudioPage(Transform parent)
    {
        var page = new GameObject("AudioPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 14f;
        v.padding = new RectOffset(8, 8, 12, 8);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var header = CreateLabel(page.transform, "AudioHeader", "Volume", 18, TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        header.GetComponent<LayoutElement>().preferredHeight = 26f;

        var hint = CreateLabel(page.transform, "AudioHint", "Master volume for music and sound effects.", 14, TextAlignmentOptions.Left);
        hint.color = new Color(0.75f, 0.78f, 0.85f, 1f);
        hint.GetComponent<LayoutElement>().preferredHeight = 22f;

        var row = new GameObject("VolumeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(page.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = 36f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 12f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        volumeSlider = CreateSlider(row.transform, "VolumeSlider");
        volumeSlider.GetComponent<LayoutElement>().flexibleWidth = 1f;
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        volumeValueText = CreateLabel(row.transform, "VolumeValue", "100%", 16, TextAlignmentOptions.MidlineRight);
        volumeValueText.GetComponent<LayoutElement>().preferredWidth = 64f;
        volumeValueText.GetComponent<LayoutElement>().minWidth = 64f;

        return page;
    }

    GameObject BuildVideoPage(Transform parent)
    {
        var page = new GameObject("VideoPage", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        var v = page.GetComponent<VerticalLayoutGroup>();
        v.spacing = 14f;
        v.padding = new RectOffset(8, 8, 12, 8);
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var header = CreateLabel(page.transform, "VideoHeader", "Resolution", 18, TextAlignmentOptions.Left);
        header.fontStyle = FontStyles.Bold;
        header.GetComponent<LayoutElement>().preferredHeight = 26f;

        var row = new GameObject("ResolutionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(page.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = 40f;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var prev = CreateButton(row.transform, "PrevRes", "<", HudTabColors.Idle);
        prev.GetComponent<LayoutElement>().preferredWidth = 44f;
        prev.GetComponent<LayoutElement>().flexibleWidth = 0f;
        prev.onClick.AddListener(() => CycleResolution(-1));

        resolutionLabel = CreateLabel(row.transform, "ResolutionLabel", "1920 x 1080", 16, TextAlignmentOptions.Center);
        var resLe = resolutionLabel.GetComponent<LayoutElement>();
        resLe.flexibleWidth = 1f;
        resLe.minWidth = 180f;

        var next = CreateButton(row.transform, "NextRes", ">", HudTabColors.Idle);
        next.GetComponent<LayoutElement>().preferredWidth = 44f;
        next.GetComponent<LayoutElement>().flexibleWidth = 0f;
        next.onClick.AddListener(() => CycleResolution(1));

        var apply = CreateButton(page.transform, "ApplyResolution", "Apply Resolution", new Color(0.32f, 0.40f, 0.52f, 1f));
        apply.onClick.AddListener(ApplySelectedResolution);

        var fsRow = new GameObject("FullscreenRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        fsRow.transform.SetParent(page.transform, false);
        fsRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        var fsH = fsRow.GetComponent<HorizontalLayoutGroup>();
        fsH.spacing = 10f;
        fsH.childAlignment = TextAnchor.MiddleLeft;
        fsH.childControlWidth = true;
        fsH.childControlHeight = true;
        fsH.childForceExpandWidth = false;

        fullscreenToggle = CreateToggle(fsRow.transform, "FullscreenToggle");
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        var fsLabel = CreateLabel(fsRow.transform, "FullscreenLabel", "Fullscreen", 16, TextAlignmentOptions.Left);
        fsLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

        return page;
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

    void OnVolumeChanged(float value)
    {
        SetMasterVolume(value);
        if (volumeValueText != null)
            volumeValueText.text = Mathf.RoundToInt(value * 100f) + "%";
        PlayerPrefs.SetFloat(PrefVolume, value);
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

    void RefreshAudioControls()
    {
        float vol = PlayerPrefs.HasKey(PrefVolume) ? PlayerPrefs.GetFloat(PrefVolume) : AudioListener.volume;
        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(vol);
        if (volumeValueText != null)
            volumeValueText.text = Mathf.RoundToInt(vol * 100f) + "%";
        SetMasterVolume(vol);
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

    static void SetMasterVolume(float value)
    {
        AudioListener.volume = Mathf.Clamp01(value);
    }

    static void ApplySavedAudio()
    {
        float vol = PlayerPrefs.HasKey(PrefVolume) ? PlayerPrefs.GetFloat(PrefVolume, 1f) : 1f;
        SetMasterVolume(vol);
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
        go.GetComponent<LayoutElement>().preferredHeight = size + 8f;
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

    static Button CreateTabButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
    {
        var btn = CreateButton(parent, name, label, HudTabColors.Idle);
        btn.onClick.AddListener(onClick);
        return btn;
    }

    static Button CreateButton(Transform parent, string name, string label, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 40f;
        le.minHeight = 40f;
        var img = go.GetComponent<Image>();
        img.color = color;
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
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Slider CreateSlider(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 28f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(go.transform, false);
        Stretch((RectTransform)bg.transform);
        bg.GetComponent<Image>().color = new Color(0.18f, 0.19f, 0.24f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = (RectTransform)fillArea.transform;
        Stretch(faRt);
        faRt.offsetMin = new Vector2(6f, 6f);
        faRt.offsetMax = new Vector2(-6f, -6f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch((RectTransform)fill.transform);
        fill.GetComponent<Image>().color = new Color(0.45f, 0.55f, 0.72f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)handleArea.transform);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = (RectTransform)handle.transform;
        handleRt.sizeDelta = new Vector2(18f, 0f);
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
        le.preferredWidth = 28f;
        le.preferredHeight = 28f;
        le.minWidth = 28f;
        go.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.3f, 1f);

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(go.transform, false);
        Stretch((RectTransform)check.transform);
        var inset = (RectTransform)check.transform;
        inset.offsetMin = new Vector2(5f, 5f);
        inset.offsetMax = new Vector2(-5f, -5f);
        check.GetComponent<Image>().color = new Color(0.7f, 0.82f, 1f, 1f);

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = go.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = Screen.fullScreen;
        return toggle;
    }
}
