using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>First-new-game dialogue sequence shown before onboarding begins.</summary>
public sealed class IntroCutsceneUI : MonoBehaviour
{
    const string VoiceVolumePreference = "PauseMenu.VoiceVolume";
    const float TypewriterVoiceVolume = 0.42f;
    public const float TypewriterCharactersPerSecond = 45f;
    public const float TypewriterVoiceInterval = 0.075f;
    static AudioClip[] typewriterVoiceClips;

    static readonly string[] OpeningLines =
    {
        "Hey look who it is – it’s 10:30 I’ve been expecting you for the last 30 minutes. First day on the job and you’re already late...",
        "Let me get one thing straight, the only reason you're here is because the investors threatened to pull the plug on this whole place if I didn’t take their advice and hire an Industrial & Systems Engineer...",
        "Apparently they think a young kid like you could do a better job at managing the place than me. I’ve been in this business longer than you’ve been alive...",
        "You want to know what’s wrong with this place? Lunch line's out the door, fries come out cold, and we threw out forty patties last week. Kids I hire these days just don't move fast enough.",
        "Now follow me, I’ll show you around. Hopefully you paid attention in school, investors told me you were the real deal – I’ll believe it if I see it...",
        "And if I don’t see it, I’ll fire you myself!"
    };

    public static bool IsPlaying { get; private set; }

    Action finished;
    string[] lines = OpeningLines;
    string chapterTitle = "FIRST SHIFT";
    TextMeshProUGUI dialogue;
    TextMeshProUGUI progress;
    TextMeshProUGUI nextLabel;
    AudioSource typewriterVoiceSource;
    RectTransform portraitFrame;
    int lineIndex;
    int visibleCharacters;
    int totalCharacters;
    float characterProgress;
    float nextTypewriterVoiceTime;
    bool closing;
    Camera liveBackgroundCamera;
    RenderTexture previousCameraTarget;
    int previousCameraTargetDisplay;
    bool previousCameraEnabled;

    public static void Show(Texture background, GameObject gusPrefab, Sprite gusPortrait,
        Action onFinished, Camera liveCamera = null)
    {
        var existing = FindFirstObjectByType<IntroCutsceneUI>();
        if (existing != null)
        {
            existing.finished = onFinished;
            return;
        }

        var go = new GameObject("GusIntroCutscene");
        var cutscene = go.AddComponent<IntroCutsceneUI>();
        cutscene.finished = onFinished;
        try
        {
            cutscene.Build(background, gusPrefab, gusPortrait, liveCamera);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            cutscene.FailOpen();
        }
    }

    public static void ShowMilestone(string title, IReadOnlyList<string> dialogueLines,
        Action onFinished, Camera liveCamera)
    {
        var existing = FindFirstObjectByType<IntroCutsceneUI>();
        if (existing != null)
        {
            existing.finished = onFinished;
            return;
        }

        var go = new GameObject("GusMilestoneCutscene");
        var cutscene = go.AddComponent<IntroCutsceneUI>();
        cutscene.finished = onFinished;
        cutscene.chapterTitle = string.IsNullOrWhiteSpace(title) ? "MILESTONE COMPLETE" : title;
        cutscene.lines = CopyDialogueLines(dialogueLines);
        try
        {
            cutscene.Build(null, null, null, liveCamera);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            cutscene.FailOpen();
        }
    }

    static string[] CopyDialogueLines(IReadOnlyList<string> source)
    {
        if (source == null || source.Count == 0)
            return new[] { "Good work. Take a moment to review what changed, then get back to improving the process." };

        var result = new List<string>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            string line = source[i];
            if (!string.IsNullOrWhiteSpace(line))
                result.Add(line.Trim());
        }

        return result.Count > 0
            ? result.ToArray()
            : new[] { "Good work. Take a moment to review what changed, then get back to improving the process." };
    }

    void Build(Texture background, GameObject gusPrefab, Sprite gusPortrait, Camera liveCamera)
    {
        IsPlaying = true;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        gameObject.AddComponent<GraphicRaycaster>();
        SetupTypewriterVoice();

        ConfigureLiveBackground(liveCamera);
        bool usingLiveCamera = liveBackgroundCamera != null;
        Transform contentRoot = transform;

        if (!usingLiveCamera)
        {
            var backgroundObject = new GameObject("CutsceneBackground", typeof(RectTransform),
                typeof(RawImage), typeof(Button));
            backgroundObject.transform.SetParent(transform, false);
            Stretch((RectTransform)backgroundObject.transform);
            var backgroundImage = backgroundObject.GetComponent<RawImage>();
            Texture shownBackground = background;
            backgroundImage.texture = shownBackground;
            backgroundImage.color = shownBackground != null ? Color.white : GameUITheme.Charcoal;
            var advanceButton = backgroundObject.GetComponent<Button>();
            advanceButton.targetGraphic = backgroundImage;
            advanceButton.transition = Selectable.Transition.None;
            advanceButton.onClick.AddListener(Advance);

            var tint = CreateImage(backgroundObject.transform, "CinematicTint",
                new Color(0.04f, 0.055f, 0.075f,
                    shownBackground != null ? 0.34f : 0.96f));
            Stretch((RectTransform)tint.transform);
            tint.raycastTarget = false;

            CreatePortrait(backgroundObject.transform, gusPrefab, gusPortrait);
            contentRoot = backgroundObject.transform;
        }

        // In live-camera mode the scene itself is the background. Avoid adding
        // any screen-sized graphic over the camera output; the dialogue elements
        // below are the only visuals drawn by this overlay.
        CreateDialogueBox(contentRoot);

        var chapter = CreateText(contentRoot, "Chapter",
            ISEVocabularyHighlighter.Highlight(chapterTitle), 18f,
            TextAlignmentOptions.TopRight, FontStyles.Bold, GameUITheme.Accent);
        var chapterRect = chapter.rectTransform;
        chapterRect.anchorMin = new Vector2(0.68f, 0.91f);
        chapterRect.anchorMax = new Vector2(0.94f, 0.97f);
        chapterRect.offsetMin = Vector2.zero;
        chapterRect.offsetMax = Vector2.zero;
        chapter.characterSpacing = 2f;

        ShowLine(0);
        GameUITheme.ApplyTo(transform);
    }

    void ConfigureLiveBackground(Camera camera)
    {
        if (camera == null)
            return;

        previousCameraTarget = camera.targetTexture;
        previousCameraTargetDisplay = camera.targetDisplay;
        previousCameraEnabled = camera.enabled;
        liveBackgroundCamera = camera;
        camera.targetTexture = null;
        camera.targetDisplay = 0;
        camera.enabled = true;
    }

    void RestoreLiveBackground()
    {
        if (liveBackgroundCamera != null)
        {
            liveBackgroundCamera.targetTexture = previousCameraTarget;
            liveBackgroundCamera.targetDisplay = previousCameraTargetDisplay;
            liveBackgroundCamera.enabled = previousCameraEnabled;
        }

        liveBackgroundCamera = null;
        previousCameraTarget = null;
    }

    void CreatePortrait(Transform parent, GameObject prefab, Sprite portrait)
    {
        var previewObject = new GameObject("GusLive3DPreview", typeof(RectTransform),
            typeof(RawImage), typeof(GusCutscenePreview));
        previewObject.transform.SetParent(parent, false);
        portraitFrame = (RectTransform)previewObject.transform;
        portraitFrame.anchorMin = new Vector2(0.065f, 0.29f);
        portraitFrame.anchorMax = new Vector2(0.30f, 0.89f);
        portraitFrame.offsetMin = Vector2.zero;
        portraitFrame.offsetMax = Vector2.zero;
        var previewImage = previewObject.GetComponent<RawImage>();
        previewImage.color = Color.white;
        previewImage.raycastTarget = false;

        if (prefab != null)
        {
            previewObject.GetComponent<GusCutscenePreview>().Configure(prefab, previewImage);
        }
        else if (portrait != null)
        {
            Destroy(previewObject.GetComponent<GusCutscenePreview>());
            var portraitObject = new GameObject("GusPortrait", typeof(RectTransform), typeof(Image));
            portraitObject.transform.SetParent(previewObject.transform, false);
            var image = portraitObject.GetComponent<Image>();
            image.sprite = portrait;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            Stretch((RectTransform)portraitObject.transform);
        }
        else
        {
            Destroy(previewObject.GetComponent<GusCutscenePreview>());
            var initial = CreateText(previewObject.transform, "GusPlaceholder", "G", 210f,
                TextAlignmentOptions.Center, FontStyles.Bold, GameUITheme.SageHover);
            Stretch(initial.rectTransform);
        }
    }

    void CreateDialogueBox(Transform parent)
    {
        var panel = CreateImage(parent, "DialoguePanel", GameUITheme.Backdrop);
        var panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.07f, 0.045f);
        panelRect.anchorMax = new Vector2(0.93f, 0.285f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        AddChrome(panel.gameObject, new Vector2(3f, -3f), new Vector2(0f, -7f));

        var nameBadge = CreateImage(panel.transform, "SpeakerBadge", GameUITheme.Coral);
        var badgeRect = nameBadge.rectTransform;
        badgeRect.anchorMin = new Vector2(0f, 1f);
        badgeRect.anchorMax = new Vector2(0f, 1f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(26f, 0f);
        badgeRect.sizeDelta = new Vector2(150f, 42f);
        var name = CreateText(nameBadge.transform, "SpeakerName", "GUS", 21f,
            TextAlignmentOptions.Center, FontStyles.Bold, GameUITheme.TextPrimary);
        Stretch(name.rectTransform);
        name.characterSpacing = 1.5f;

        dialogue = CreateText(panel.transform, "Dialogue", string.Empty, 24f,
            TextAlignmentOptions.TopLeft, FontStyles.Normal, GameUITheme.TextPrimary);
        dialogue.rectTransform.anchorMin = new Vector2(0f, 0f);
        dialogue.rectTransform.anchorMax = new Vector2(1f, 1f);
        dialogue.rectTransform.offsetMin = new Vector2(34f, 58f);
        dialogue.rectTransform.offsetMax = new Vector2(-260f, -38f);
        dialogue.textWrappingMode = TextWrappingModes.Normal;
        dialogue.lineSpacing = 8f;

        progress = CreateText(panel.transform, "Progress", string.Empty, 14f,
            TextAlignmentOptions.BottomLeft, FontStyles.Bold, GameUITheme.TextSecondary);
        progress.rectTransform.anchorMin = Vector2.zero;
        progress.rectTransform.anchorMax = Vector2.zero;
        progress.rectTransform.pivot = Vector2.zero;
        progress.rectTransform.anchoredPosition = new Vector2(34f, 22f);
        progress.rectTransform.sizeDelta = new Vector2(160f, 26f);

        var next = CreateButton(panel.transform, "NextButton", "NEXT  >", GameUITheme.Positive,
            new Vector2(210f, 52f));
        var nextRect = (RectTransform)next.transform;
        nextRect.anchorMin = new Vector2(1f, 0f);
        nextRect.anchorMax = new Vector2(1f, 0f);
        nextRect.pivot = Vector2.zero;
        nextRect.anchoredPosition = new Vector2(-232f, 24f);
        nextLabel = next.GetComponentInChildren<TextMeshProUGUI>();
        next.onClick.AddListener(Advance);

        var skip = CreateButton(parent, "SkipButton", "SKIP", GameUITheme.Surface,
            new Vector2(120f, 42f));
        var skipRect = (RectTransform)skip.transform;
        skipRect.anchorMin = new Vector2(1f, 1f);
        skipRect.anchorMax = new Vector2(1f, 1f);
        skipRect.pivot = Vector2.one;
        skipRect.anchoredPosition = new Vector2(-40f, -38f);
        skip.onClick.AddListener(Finish);
    }

    void Update()
    {
        if (closing) return;

        characterProgress += Time.unscaledDeltaTime * TypewriterCharactersPerSecond;
        int target = Mathf.Min(totalCharacters, Mathf.FloorToInt(characterProgress));
        if (target != visibleCharacters)
        {
            visibleCharacters = target;
            dialogue.maxVisibleCharacters = visibleCharacters;
            PlayTypewriterVoice();
        }

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter))
            Advance();
        else if (Input.GetKeyDown(KeyCode.Escape))
            Finish();
    }

    void Advance()
    {
        if (closing || dialogue == null) return;
        if (visibleCharacters < totalCharacters)
        {
            visibleCharacters = totalCharacters;
            characterProgress = totalCharacters;
            dialogue.maxVisibleCharacters = totalCharacters;
            return;
        }

        if (lineIndex + 1 >= lines.Length)
        {
            Finish();
            return;
        }

        ShowLine(lineIndex + 1);
        Sfx.Play(SfxId.UiClick);
    }

    void ShowLine(int index)
    {
        lineIndex = Mathf.Clamp(index, 0, lines.Length - 1);
        dialogue.text = ISEVocabularyHighlighter.Highlight(lines[lineIndex]);
        dialogue.ForceMeshUpdate();
        totalCharacters = dialogue.textInfo.characterCount;
        visibleCharacters = 0;
        characterProgress = 0f;
        dialogue.maxVisibleCharacters = 0;
        progress.text = (lineIndex + 1) + " / " + lines.Length + "     SPACE TO CONTINUE";
        if (nextLabel != null)
            nextLabel.text = lineIndex == lines.Length - 1
                ? chapterTitle == "FIRST SHIFT" ? "START  >" : "BACK TO SHIFT  >"
                : "NEXT  >";
    }

    void SetupTypewriterVoice()
    {
        typewriterVoiceSource = gameObject.AddComponent<AudioSource>();
        ConfigureTypewriterVoiceSource(typewriterVoiceSource);
    }

    public static void ConfigureTypewriterVoiceSource(AudioSource source)
    {
        if (source == null) return;
        EnsureTypewriterVoiceClips();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        source.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceVolumePreference, 1f))
            * TypewriterVoiceVolume;
    }

    public static void PlayGibberishBlip(AudioSource source)
    {
        if (source == null) return;
        EnsureTypewriterVoiceClips();
        source.pitch = UnityEngine.Random.Range(0.88f, 1.14f);
        AudioClip clip = typewriterVoiceClips[UnityEngine.Random.Range(0, typewriterVoiceClips.Length)];
        source.PlayOneShot(clip, UnityEngine.Random.Range(0.82f, 1f));
    }

    static void EnsureTypewriterVoiceClips()
    {
        if (typewriterVoiceClips == null)
            typewriterVoiceClips = CreateTypewriterVoiceClips();
    }

    void PlayTypewriterVoice()
    {
        if (typewriterVoiceSource == null || visibleCharacters <= 0
            || Time.unscaledTime < nextTypewriterVoiceTime)
            return;

        int characterIndex = Mathf.Min(visibleCharacters - 1, dialogue.textInfo.characterCount - 1);
        if (characterIndex < 0 || characterIndex >= dialogue.textInfo.characterInfo.Length)
            return;

        char character = dialogue.textInfo.characterInfo[characterIndex].character;
        if (!char.IsLetterOrDigit(character))
            return;

        nextTypewriterVoiceTime = Time.unscaledTime + TypewriterVoiceInterval
            + UnityEngine.Random.Range(0f, 0.025f);
        PlayGibberishBlip(typewriterVoiceSource);
    }

    static AudioClip[] CreateTypewriterVoiceClips()
    {
        const int sampleRate = 22050;
        const float duration = 0.065f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[][] harmonicProfiles =
        {
            new[] { 0.34f, 0.48f, 0.32f, 0.17f, 0.08f, 0.035f },
            new[] { 0.42f, 0.34f, 0.47f, 0.25f, 0.09f, 0.025f },
            new[] { 0.48f, 0.55f, 0.23f, 0.11f, 0.06f, 0.025f }
        };

        var clips = new AudioClip[harmonicProfiles.Length];
        for (int clipIndex = 0; clipIndex < harmonicProfiles.Length; clipIndex++)
        {
            var samples = new float[sampleCount];
            float startFrequency = 175f + clipIndex * 16f;
            float endFrequency = 240f - clipIndex * 12f;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = sampleIndex / (float)sampleRate;
                float normalizedTime = time / duration;
                float envelope = Mathf.Min(1f, time / 0.006f)
                    * Mathf.Clamp01((duration - time) / 0.018f)
                    * Mathf.Exp(-normalizedTime * 1.4f);
                float phase = 2f * Mathf.PI * (startFrequency * time
                    + 0.5f * (endFrequency - startFrequency) * time * normalizedTime);

                float sample = 0f;
                float[] harmonics = harmonicProfiles[clipIndex];
                for (int harmonic = 0; harmonic < harmonics.Length; harmonic++)
                    sample += Mathf.Sin(phase * (harmonic + 1)) * harmonics[harmonic];

                samples[sampleIndex] = sample * envelope * 0.55f;
            }

            AudioClip clip = AudioClip.Create("Gus_Gibberish_" + clipIndex,
                sampleCount, 1, sampleRate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(samples, 0);
            clips[clipIndex] = clip;
        }

        return clips;
    }

    void Finish()
    {
        if (closing) return;
        closing = true;
        IsPlaying = false;
        Sfx.Play(SfxId.UiOpen);
        RestoreLiveBackground();
        Action callback = finished;
        finished = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    void FailOpen()
    {
        if (closing) return;
        closing = true;
        IsPlaying = false;
        RestoreLiveBackground();
        Action callback = finished;
        finished = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    void OnDestroy()
    {
        RestoreLiveBackground();
        if (!closing)
        {
            IsPlaying = false;
            Action callback = finished;
            finished = null;
            callback?.Invoke();
        }
    }

    static Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, string value, float size,
        TextAlignmentOptions alignment, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        GameUITheme.ApplyTitleScreenFont(text);
        return text;
    }

    static Button CreateButton(Transform parent, string name, string label, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        var text = CreateText(go.transform, "Label", label, 18f, TextAlignmentOptions.Center,
            FontStyles.Bold, GameUITheme.TextPrimary);
        Stretch(text.rectTransform, 5f);
        return button;
    }

    static void AddChrome(GameObject target, Vector2 edgeDistance, Vector2 shadowDistance)
    {
        var outline = target.AddComponent<Outline>();
        outline.effectColor = GameUITheme.Edge;
        outline.effectDistance = edgeDistance;
        outline.useGraphicAlpha = true;
    }

    static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}

/// <summary>Renders the actual Gus prefab continuously onto a transparent UI texture.</summary>
public sealed class GusCutscenePreview : MonoBehaviour
{
    const int PreviewLayer = 31;
    const float PreviewWorldSpacing = 10f;
    static readonly HashSet<int> ActivePreviewSlots = new HashSet<int>();

    GameObject previewWorld;
    RenderTexture renderTexture;
    Camera previewCamera;
    int previewSlot = -1;

    public void Configure(GameObject prefab, RawImage target, bool headshot = false)
    {
        if (prefab == null || target == null) return;

        int textureWidth = headshot ? 256 : 512;
        int textureHeight = headshot ? 256 : 768;
        renderTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = "Gus_Live_Preview",
            antiAliasing = 4,
            filterMode = FilterMode.Bilinear
        };
        renderTexture.Create();
        target.texture = renderTexture;

        previewWorld = new GameObject("__GusCutscenePreviewWorld");
        previewWorld.hideFlags = HideFlags.HideAndDontSave;
        // Every preview camera uses the same private render layer. Give each preview
        // a nearby, recycled slot so its camera cannot see other Gus clones.
        previewSlot = AcquirePreviewSlot();
        previewWorld.transform.position = new Vector3(previewSlot * PreviewWorldSpacing, -8000f, 0f);

        GameObject model = Instantiate(prefab, previewWorld.transform, false);
        model.name = "Gus";
        model.transform.localPosition = Vector3.zero;
        // This prefab's front points opposite the inventory-preview angle.
        model.transform.localRotation = Quaternion.Euler(0f, -30f, 0f);
        SetLayerRecursively(model, PreviewLayer);

        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;

        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
        {
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
        }

        Bounds bounds = CalculateBounds(model);
        float aspect = renderTexture.width / (float)renderTexture.height;
        float horizontalHalf = Mathf.Max(bounds.extents.x, bounds.extents.z, 0.1f);
        float framedHalf = Mathf.Max(bounds.size.y * (headshot ? 0.20f : 0.31f), 0.1f);
        float orthographicSize = headshot
            ? framedHalf
            : Mathf.Max(framedHalf, horizontalHalf / aspect) * 1.04f;
        // Aim slightly below the face for headshots so the rendered character moves
        // upward inside the small icon while keeping the chef hat visible.
        float framedCenterY = bounds.min.y + bounds.size.y * (headshot ? 0.74f : 0.69f);
        Vector3 framedCenter = new Vector3(bounds.center.x, framedCenterY, bounds.center.z);

        var cameraObject = new GameObject("GusPreviewCamera", typeof(Camera));
        cameraObject.transform.SetParent(previewWorld.transform, true);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = orthographicSize;
        previewCamera.aspect = aspect;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = orthographicSize * 8f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = true;
        previewCamera.targetTexture = renderTexture;
        previewCamera.transform.position = framedCenter + Vector3.forward * orthographicSize * 3f;
        previewCamera.transform.LookAt(framedCenter);

        CreateLight("GusKeyLight", new Vector3(-1.5f, 2.5f, -1.5f), 3.5f);
        CreateLight("GusFillLight", new Vector3(1.5f, 1.5f, 1.5f), 1.5f);
    }

    void CreateLight(string objectName, Vector3 localPosition, float intensity)
    {
        var lightObject = new GameObject(objectName, typeof(Light));
        lightObject.transform.SetParent(previewWorld.transform, false);
        lightObject.transform.localPosition = localPosition;
        var light = lightObject.GetComponent<Light>();
        // Directional lights affect every scene object in some render pipelines,
        // even when this preview is rendered into a private camera. Keep preview
        // illumination local so opening a Gus card cannot relight the restaurant.
        light.type = LightType.Point;
        light.range = 6f;
        light.shadows = LightShadows.None;
        light.intensity = intensity;
        light.color = new Color(1f, 0.92f, 0.82f);
        light.cullingMask = 1 << PreviewLayer;
    }

    static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    static int AcquirePreviewSlot()
    {
        int slot = 0;
        while (ActivePreviewSlots.Contains(slot))
            slot++;
        ActivePreviewSlots.Add(slot);
        return slot;
    }

    void OnEnable()
    {
        if (previewCamera != null)
            previewCamera.enabled = true;
    }

    void OnDisable()
    {
        if (previewCamera != null)
            previewCamera.enabled = false;
    }

    void OnDestroy()
    {
        if (previewSlot >= 0)
        {
            ActivePreviewSlots.Remove(previewSlot);
            previewSlot = -1;
        }
        if (previewCamera != null)
            previewCamera.targetTexture = null;
        if (previewWorld != null)
            Destroy(previewWorld);
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
