using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Independent post-tutorial Move/Delete menu. It shares BuildPlacer's selection
/// and action methods, but owns a separate screen-space canvas from the tutorial bar.
/// </summary>
public sealed class PostTutorialStationActionMenu : MonoBehaviour
{
    const string CanvasName = "PostTutorialStationActionCanvas";
    const string MenuName = "PostTutorialStationActionMenu";

    BuildPlacer placer;
    Canvas menuCanvas;
    CanvasGroup canvasGroup;
    TextMeshProUGUI selectionLabel;
    TextMeshProUGUI messageLabel;
    GameObject instructionPanel;
    Button moveButton;
    Button deleteButton;
    float messageUntil;
    float lastCanvasWidth = -1f;
    int displayedSelectionCount = -1;
    bool displayedDragging;
    bool displayedFixedDoorSelected;
    bool placementInstructionActive;

    public bool IsPointerOver
    {
        get
        {
            RectTransform rect = transform as RectTransform;
            return gameObject.activeInHierarchy && canvasGroup != null && canvasGroup.blocksRaycasts
                && rect != null && RectTransformUtility.RectangleContainsScreenPoint(
                    rect, Input.mousePosition, null);
        }
    }

    public static PostTutorialStationActionMenu EnsureFor(BuildPlacer owner)
    {
        if (owner == null) return null;

        PostTutorialStationActionMenu existing = FindFirstObjectByType<PostTutorialStationActionMenu>(
            FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            if (!existing.enabled) existing.enabled = true;
            existing.placer = owner;
            existing.EnsureBuilt();
            existing.Refresh();
            return existing;
        }

        GameObject canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        Canvas referenceCanvas = ResolvePlayerUICanvas();
        canvas.sortingLayerID = referenceCanvas != null ? referenceCanvas.sortingLayerID : 0;
        canvas.sortingOrder = Mathf.Clamp(Mathf.Max(3200,
            referenceCanvas != null ? referenceCanvas.sortingOrder + 3200 : 3200),
            short.MinValue, short.MaxValue);
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        GameObject menuObject = new GameObject(MenuName, typeof(RectTransform), typeof(Image),
            typeof(CanvasGroup), typeof(HorizontalLayoutGroup), typeof(PostTutorialStationActionMenu));
        menuObject.transform.SetParent(canvasObject.transform, false);
        PostTutorialStationActionMenu menu = menuObject.GetComponent<PostTutorialStationActionMenu>();
        menu.placer = owner;
        menu.menuCanvas = canvas;
        menu.EnsureBuilt();
        menu.Refresh();
        return menu;
    }

    void Update()
    {
        if (placer == null)
            placer = FindFirstObjectByType<BuildPlacer>();
        if (menuCanvas == null)
            menuCanvas = GetComponentInParent<Canvas>();
        FitToCanvasWidth();

        if (messageLabel != null && messageLabel.gameObject.activeSelf
            && Time.unscaledTime >= messageUntil)
        {
            messageLabel.gameObject.SetActive(false);
            if (selectionLabel != null) selectionLabel.gameObject.SetActive(true);
        }

        Refresh();
    }

    void EnsureBuilt()
    {
        if (menuCanvas == null)
            menuCanvas = GetComponentInParent<Canvas>();
        if (menuCanvas != null)
        {
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            menuCanvas.overrideSorting = true;
            Canvas referenceCanvas = ResolvePlayerUICanvas();
            if (referenceCanvas != null)
                menuCanvas.sortingLayerID = referenceCanvas.sortingLayerID;
            menuCanvas.sortingOrder = Mathf.Clamp(Mathf.Max(3200,
                referenceCanvas != null ? referenceCanvas.sortingOrder + 3200 : 3200),
                short.MinValue, short.MaxValue);
        }

        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 18f);
        rect.sizeDelta = new Vector2(340f, 54f);

        Image background = GetComponent<Image>();
        background.color = HudTabColors.Strip;
        background.raycastTarget = true;

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        HorizontalLayoutGroup layout = GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 7, 7);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        Transform instruction = transform.Find("InstructionPanel");
        if (instruction == null)
        {
            instructionPanel = new GameObject("InstructionPanel", typeof(RectTransform),
                typeof(Image), typeof(LayoutElement));
            instructionPanel.transform.SetParent(transform, false);
            instruction = instructionPanel.transform;
        }
        else instructionPanel = instruction.gameObject;

        LayoutElement instructionLayout = instructionPanel.GetComponent<LayoutElement>();
        instructionLayout.ignoreLayout = true;
        RectTransform instructionRect = (RectTransform)instruction;
        instructionRect.anchorMin = new Vector2(0.5f, 0f);
        instructionRect.anchorMax = new Vector2(0.5f, 0f);
        instructionRect.pivot = new Vector2(0.5f, 0f);
        instructionRect.anchoredPosition = new Vector2(0f, 54f);
        instructionRect.sizeDelta = new Vector2(440f, 36f);
        Image instructionBackground = instructionPanel.GetComponent<Image>();
        instructionBackground.color = HudTabColors.Strip;
        instructionBackground.raycastTarget = false;

        selectionLabel = GetOrCreateLabel(instruction, "Selection");
        StretchLabel(selectionLabel.rectTransform, 16f);
        selectionLabel.alignment = TextAlignmentOptions.Center;
        selectionLabel.enableAutoSizing = true;
        selectionLabel.fontSizeMin = 11f;
        selectionLabel.fontSizeMax = 15f;

        messageLabel = GetOrCreateLabel(instruction, "Message");
        StretchLabel(messageLabel.rectTransform, 16f);
        messageLabel.alignment = TextAlignmentOptions.Center;
        messageLabel.color = GameUITheme.Accent;
        messageLabel.gameObject.SetActive(false);

        moveButton = transform.Find("Move")?.GetComponent<Button>();
        if (moveButton == null)
            moveButton = CreateButton("Move", "Move", () => placer?.BeginMoveSelected());
        deleteButton = transform.Find("Delete")?.GetComponent<Button>();
        if (deleteButton == null)
            deleteButton = CreateButton("Delete", "Delete", () => placer?.DeleteSelectedStationsAndRefund());

        GameUITheme.ApplyTo(transform);
        Image moveImage = moveButton != null ? moveButton.GetComponent<Image>() : null;
        if (moveImage != null) moveImage.color = GameUITheme.Positive;
        moveButton?.GetComponent<GameUIThemeHoverFeedback>()?.SetBaseColor(GameUITheme.Positive);
        Image deleteImage = deleteButton != null ? deleteButton.GetComponent<Image>() : null;
        if (deleteImage != null) deleteImage.color = GameUITheme.Danger;
        deleteButton?.GetComponent<GameUIThemeHoverFeedback>()?.SetBaseColor(GameUITheme.Danger);
        FitToCanvasWidth();
    }

    static Canvas ResolvePlayerUICanvas()
    {
        GameObject playerUI = GameObject.Find("PlayerUI");
        if (playerUI != null)
        {
            Canvas canvas = playerUI.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        TitleScreenController title = FindFirstObjectByType<TitleScreenController>();
        if (title != null && title.inGameUIRoot != null)
        {
            Canvas canvas = title.inGameUIRoot.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
            if (canvases[i] != null && canvases[i].isRootCanvas
                && canvases[i].renderMode != RenderMode.WorldSpace)
                return canvases[i];
        return null;
    }

    TextMeshProUGUI GetOrCreateLabel(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.GetComponent<TextMeshProUGUI>();

        GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(LayoutElement),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);
        LayoutElement element = labelObject.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 470f;
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.color = GameUITheme.TextPrimary;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    Button CreateButton(string objectName, string text, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(transform, false);
        LayoutElement element = buttonObject.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 150f;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        button.onClick.AddListener(action);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 16f;
        label.fontStyle = FontStyles.Bold;
        label.color = GameUITheme.TextPrimary;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        if (objectName == "Move")
            GameIconLibrary.ApplyToButton(button, GameIconId.Wrench, 19f, 10f);
        return button;
    }

    void FitToCanvasWidth()
    {
        RectTransform canvasRect = transform.parent as RectTransform;
        if (canvasRect == null) return;
        float canvasWidth = canvasRect.rect.width;
        if (canvasWidth <= 0.01f || Mathf.Abs(canvasWidth - lastCanvasWidth) < 0.5f) return;
        lastCanvasWidth = canvasWidth;

        float width = Mathf.Min(340f, Mathf.Max(280f, canvasWidth - 16f));
        ((RectTransform)transform).sizeDelta = new Vector2(width, 54f);
        float buttonWidth = Mathf.Max(110f, (width - 40f) * 0.5f);
        SetButtonWidth(moveButton, buttonWidth);
        SetButtonWidth(deleteButton, buttonWidth);
        if (instructionPanel != null)
            ((RectTransform)instructionPanel.transform).sizeDelta = new Vector2(width, 36f);
    }

    static void SetButtonWidth(Button button, float width)
    {
        if (button == null) return;
        LayoutElement element = button.GetComponent<LayoutElement>();
        if (element == null) element = button.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = width;
        element.flexibleWidth = 0f;
    }

    static void StretchLabel(RectTransform rect, float horizontalPadding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(horizontalPadding, 0f);
        rect.offsetMax = new Vector2(-horizontalPadding, 0f);
    }

    bool IsPostTutorialBuildMode => placer != null && placer.modeManager != null
        && OnboardingTutorial.IsComplete
        && !TitleScreenController.IsTitleScreenActive
        && placer.modeManager.CurrentMode == GameModeManager.Mode.Build;

    public void Refresh()
    {
        if (placer == null || selectionLabel == null) return;
        int count = placer.SelectedStationCount;
        bool fixedDoorSelected = placer.HasFixedDoorSelected;
        if (!placementInstructionActive && (messageLabel == null || !messageLabel.gameObject.activeSelf)
            && (displayedSelectionCount != count || displayedDragging != placer.IsDragging
                || displayedFixedDoorSelected != fixedDoorSelected))
        {
            displayedSelectionCount = count;
            displayedDragging = placer.IsDragging;
            displayedFixedDoorSelected = fixedDoorSelected;
            selectionLabel.text = count == 0
                ? "Select a station"
                : fixedDoorSelected && count == 1 ? "Entrance door is fixed"
                : fixedDoorSelected ? count + " selected  |  Entrance door is fixed"
                : placer.IsDragging ? "Click a tile to place"
                : count == 1 ? "Double-click a station or tile to move"
                : count + " selected  |  Double-click or Move";
        }

        if (instructionPanel != null)
            instructionPanel.SetActive(count > 0 || placementInstructionActive
                || (messageLabel != null && messageLabel.gameObject.activeSelf));
        bool planPhase = GameTimeManager.Instance == null
            || !GameTimeManager.Instance.IsShiftInProgress || ShiftPlanningUI.IsPlanning;
        bool canEdit = IsPostTutorialBuildMode && planPhase && count > 0
            && !fixedDoorSelected && !placer.IsDragging && !placer.IsPlacing;
        if (moveButton != null) moveButton.interactable = canEdit;
        if (deleteButton != null) deleteButton.interactable = canEdit;
        SetVisible(IsPostTutorialBuildMode);
    }

    void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    public void SetPlacementInstruction(string instruction)
    {
        placementInstructionActive = !string.IsNullOrWhiteSpace(instruction);
        displayedSelectionCount = -1;
        if (selectionLabel != null)
        {
            selectionLabel.gameObject.SetActive(true);
            if (placementInstructionActive) selectionLabel.text = instruction;
            InputPromptLibrary.ApplyInlineInstruction(
                instructionPanel != null ? instructionPanel.transform : selectionLabel.transform.parent,
                placementInstructionActive ? instruction : null);
            selectionLabel.gameObject.SetActive(!placementInstructionActive);
        }
        if (messageLabel != null && placementInstructionActive)
            messageLabel.gameObject.SetActive(false);
        Refresh();
    }

    public void ShowMessage(string message)
    {
        if (messageLabel == null) return;
        messageLabel.text = message;
        if (selectionLabel != null) selectionLabel.gameObject.SetActive(false);
        if (instructionPanel != null) instructionPanel.SetActive(true);
        messageLabel.gameObject.SetActive(true);
        messageUntil = Time.unscaledTime + 2.5f;
        Refresh();
    }
}
