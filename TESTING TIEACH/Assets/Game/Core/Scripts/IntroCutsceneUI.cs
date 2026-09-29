using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>First-new-game dialogue sequence shown before onboarding begins.</summary>
public sealed class IntroCutsceneUI : MonoBehaviour
{
    static readonly string[] Lines =
    {
        "Hey, I'm Gus, the owner of this restaurant. Thanks for coming. I could really use your help.",
        "Business has been struggling. Orders take too long, ingredients pile up, and too many customers leave without their food.",
        "That is why I am hiring you as our industrial and systems engineer. I need you to study how work moves through the restaurant and figure out what is slowing us down.",
        "Design better workflows, arrange stations, assign workers, and use the data to find bottlenecks before they cost us more customers.",
        "If you can make this place faster, more efficient, and more reliable, we might turn the business around. Ready to get started?"
    };

    public static bool IsPlaying { get; private set; }

    Action finished;
    TextMeshProUGUI dialogue;
    TextMeshProUGUI progress;
    TextMeshProUGUI nextLabel;
    RectTransform portraitFrame;
    int lineIndex;
    int visibleCharacters;
    int totalCharacters;
    float characterProgress;
    bool closing;

    public static void Show(Texture background, GameObject gusPrefab, Sprite gusPortrait,
        Action onFinished)
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
            cutscene.Build(background, gusPrefab, gusPortrait);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            cutscene.FailOpen();
        }
    }

    void Build(Texture background, GameObject gusPrefab, Sprite gusPortrait)
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

        var backgroundObject = new GameObject("CutsceneBackground", typeof(RectTransform),
            typeof(RawImage), typeof(Button));
        backgroundObject.transform.SetParent(transform, false);
        Stretch((RectTransform)backgroundObject.transform);
        var backgroundImage = backgroundObject.GetComponent<RawImage>();
        backgroundImage.texture = background;
        backgroundImage.color = background != null ? Color.white : GameUITheme.Charcoal;
        var advanceButton = backgroundObject.GetComponent<Button>();
        advanceButton.targetGraphic = backgroundImage;
        advanceButton.transition = Selectable.Transition.None;
        advanceButton.onClick.AddListener(Advance);

        var tint = CreateImage(backgroundObject.transform, "CinematicTint",
            new Color(0.04f, 0.055f, 0.075f, background != null ? 0.34f : 0.96f));
        Stretch((RectTransform)tint.transform);
        tint.raycastTarget = false;

        CreatePortrait(backgroundObject.transform, gusPrefab, gusPortrait);
        CreateDialogueBox(backgroundObject.transform);

        var chapter = CreateText(backgroundObject.transform, "Chapter", "FIRST SHIFT", 18f,
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

        characterProgress += Time.unscaledDeltaTime * 45f;
        int target = Mathf.Min(totalCharacters, Mathf.FloorToInt(characterProgress));
        if (target != visibleCharacters)
        {
            visibleCharacters = target;
            dialogue.maxVisibleCharacters = visibleCharacters;
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

        if (lineIndex + 1 >= Lines.Length)
        {
            Finish();
            return;
        }

        ShowLine(lineIndex + 1);
        Sfx.Play(SfxId.UiClick);
    }

    void ShowLine(int index)
    {
        lineIndex = Mathf.Clamp(index, 0, Lines.Length - 1);
        dialogue.text = Lines[lineIndex];
        dialogue.ForceMeshUpdate();
        totalCharacters = dialogue.textInfo.characterCount;
        visibleCharacters = 0;
        characterProgress = 0f;
        dialogue.maxVisibleCharacters = 0;
        progress.text = (lineIndex + 1) + " / " + Lines.Length + "     SPACE TO CONTINUE";
        if (nextLabel != null)
            nextLabel.text = lineIndex == Lines.Length - 1 ? "START  >" : "NEXT  >";
    }

    void Finish()
    {
        if (closing) return;
        closing = true;
        IsPlaying = false;
        Sfx.Play(SfxId.UiOpen);
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
        Action callback = finished;
        finished = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    void OnDestroy()
    {
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
        var shadow = target.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = shadowDistance;
        shadow.useGraphicAlpha = true;
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
    GameObject previewWorld;
    RenderTexture renderTexture;
    Camera previewCamera;

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
        previewWorld.transform.position = new Vector3(0f, -8000f, 0f);

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

        CreateLight("GusKeyLight", new Vector3(35f, 145f, 0f), 1.25f);
        CreateLight("GusFillLight", new Vector3(20f, -35f, 0f), 0.55f);
    }

    void CreateLight(string objectName, Vector3 rotation, float intensity)
    {
        var lightObject = new GameObject(objectName, typeof(Light));
        lightObject.transform.SetParent(previewWorld.transform, false);
        lightObject.transform.localRotation = Quaternion.Euler(rotation);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
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
