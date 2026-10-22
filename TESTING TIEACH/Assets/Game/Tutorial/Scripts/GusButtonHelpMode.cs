using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows Gus's confirmation dialogue using the same card treatment as tutorial explanations.
/// </summary>
public sealed class GusButtonHelpMode : MonoBehaviour, IPointerClickHandler
{
    static GusButtonHelpMode instance;

    TextMeshProUGUI bodyText;
    TextMeshProUGUI stepText;
    TextMeshProUGUI titleText;
    AudioSource voiceSource;
    readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
    int totalCharacters;
    int visibleCharacters;
    float characterProgress;
    float nextVoiceTime;

    public static void ShowConfirmation()
    {
        if (instance == null)
        {
            var root = new GameObject("GusConfirmationDialogue", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image),
                typeof(GusButtonHelpMode));
            instance = root.GetComponent<GusButtonHelpMode>();
            instance.BuildUI();
        }

        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
        instance.ShowDialogue("What's it now? Click any button and I'll tell you what it does.",
            "ASK GUS", "GUS HELP");
        Sfx.Play(SfxId.UiOpen);
    }

    void Awake()
    {
        instance = this;
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;
        Image inputSurface = GetComponent<Image>();
        inputSurface.color = new Color(0f, 0f, 0f, 0f);
        inputSurface.raycastTarget = true;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void BuildUI()
    {
        RectTransform rootRect = (RectTransform)transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 24f);
        panelRect.sizeDelta = new Vector2(560f, 0f);

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        background.raycastTarget = false;

        VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(16, 16, 12, 12);
        panelLayout.spacing = 6f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader(panel.transform);
        bodyText = CreateLabel(panel.transform, "", 15f, FontStyles.Normal,
            new Color(0.92f, 0.94f, 0.98f, 1f));
        bodyText.name = "Dialogue";
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        BuildFooter(panel.transform);
        BuildVoice();

        Canvas canvas = GetComponent<Canvas>();
        GameUITheme.ApplyToCanvas(canvas);
    }

    void BuildHeader(Transform parent)
    {
        GameObject header = new GameObject("GusHeader", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.transform.SetParent(parent, false);
        LayoutElement headerSize = header.GetComponent<LayoutElement>();
        headerSize.preferredHeight = 60f;

        HorizontalLayoutGroup headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        GameObject iconFrame = new GameObject("GusIconFrame", typeof(RectTransform),
            typeof(Image), typeof(LayoutElement));
        iconFrame.transform.SetParent(header.transform, false);
        iconFrame.GetComponent<Image>().color = GameUITheme.Surface;
        LayoutElement iconSize = iconFrame.GetComponent<LayoutElement>();
        iconSize.minWidth = iconSize.preferredWidth = 58f;
        iconSize.minHeight = iconSize.preferredHeight = 58f;
        Outline iconOutline = iconFrame.AddComponent<Outline>();
        iconOutline.effectColor = GameUITheme.Accent;
        iconOutline.effectDistance = new Vector2(2f, -2f);

        GameObject portrait = new GameObject("GusFace", typeof(RectTransform),
            typeof(RawImage), typeof(GusCutscenePreview));
        portrait.transform.SetParent(iconFrame.transform, false);
        RectTransform portraitRect = (RectTransform)portrait.transform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(3f, 3f);
        portraitRect.offsetMax = new Vector2(-3f, -3f);
        RawImage portraitImage = portrait.GetComponent<RawImage>();
        portraitImage.raycastTarget = false;

        TitleScreenController title = FindFirstObjectByType<TitleScreenController>(
            FindObjectsInactive.Include);
        GameObject gusModel = title != null && title.gusPrefab != null
            ? title.gusPrefab
            : Resources.Load<GameObject>("Prefabs/character_default");
        if (gusModel != null)
            portrait.GetComponent<GusCutscenePreview>().Configure(gusModel, portraitImage, true);
        else
            Destroy(portrait.GetComponent<GusCutscenePreview>());

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

        stepText = CreateLabel(headerText.transform, "ASK GUS", 12f,
            FontStyles.Bold, GameUITheme.Accent);
        stepText.alignment = TextAlignmentOptions.Left;
        titleText = CreateLabel(headerText.transform, "GUS HELP", 20f,
            FontStyles.Bold, Color.white);
        titleText.alignment = TextAlignmentOptions.Left;
    }

    void BuildFooter(Transform parent)
    {
        GameObject footer = new GameObject("Navigation", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(parent, false);
        footer.GetComponent<LayoutElement>().preferredHeight = 46f;
        HorizontalLayoutGroup footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;

        GameObject buttonObject = new GameObject("Close", typeof(RectTransform),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(footer.transform, false);
        LayoutElement buttonSize = buttonObject.GetComponent<LayoutElement>();
        buttonSize.preferredWidth = 188f;
        buttonSize.preferredHeight = 46f;
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.22f, 0.55f, 0.38f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(Close);
        GameUITheme.ApplyCompactControlEffects(button);

        TextMeshProUGUI label = CreateLabel(buttonObject.transform, "DONE", 14f,
            FontStyles.Bold, Color.white);
        label.alignment = TextAlignmentOptions.Center;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    void BuildVoice()
    {
        voiceSource = gameObject.AddComponent<AudioSource>();
        IntroCutsceneUI.ConfigureTypewriterVoiceSource(voiceSource);
    }

    void ShowDialogue(string message, string step, string title)
    {
        if (bodyText == null) return;
        if (voiceSource != null) voiceSource.Stop();
        if (stepText != null) stepText.text = step;
        if (titleText != null) titleText.text = title;
        bodyText.text = ISEVocabularyHighlighter.Highlight(message);
        bodyText.ForceMeshUpdate();
        totalCharacters = bodyText.textInfo.characterCount;
        visibleCharacters = 0;
        characterProgress = 0f;
        nextVoiceTime = Time.unscaledTime;
        bodyText.maxVisibleCharacters = 0;
        Canvas.ForceUpdateCanvases();
        RectTransform panel = transform.Find("Panel") as RectTransform;
        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }
        if (bodyText == null
            || visibleCharacters >= totalCharacters)
            return;

        characterProgress += Time.unscaledDeltaTime * IntroCutsceneUI.TypewriterCharactersPerSecond;
        int targetCharacters = Mathf.Min(totalCharacters, Mathf.FloorToInt(characterProgress));
        if (targetCharacters <= visibleCharacters) return;

        visibleCharacters = targetCharacters;
        bodyText.maxVisibleCharacters = visibleCharacters;
        int index = Mathf.Min(visibleCharacters - 1, bodyText.textInfo.characterCount - 1);
        if (index < 0 || index >= bodyText.textInfo.characterInfo.Length) return;
        if (!char.IsLetterOrDigit(bodyText.textInfo.characterInfo[index].character)) return;
        if (Time.unscaledTime < nextVoiceTime) return;

        nextVoiceTime = Time.unscaledTime + IntroCutsceneUI.TypewriterVoiceInterval
            + Random.Range(0f, 0.025f);
        IntroCutsceneUI.PlayGibberishBlip(voiceSource);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        Button target = FindButtonUnderPointer(eventData);
        if (target == null)
        {
            ShowDialogue("Click a labeled button and I'll explain it. Use DONE when you're finished.",
                "ASK GUS", "PICK A BUTTON");
            return;
        }

        if (target.GetComponentInParent<GusButtonHelpMode>() == this) return;
        if (target.gameObject.name == "GusHelpButton")
        {
            Close();
            return;
        }
        string label = GetButtonLabel(target);
        string title = label.Length > 22 ? label.Substring(0, 19) + "..." : label;
        ShowDialogue(BuildExplanation(target, label), "BUTTON HELP", title.ToUpperInvariant());
        Sfx.Play(SfxId.UiClick);
    }

    Button FindButtonUnderPointer(PointerEventData eventData)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return null;

        raycastResults.Clear();
        eventSystem.RaycastAll(eventData, raycastResults);
        foreach (RaycastResult result in raycastResults)
        {
            if (result.gameObject == null || result.gameObject.transform.IsChildOf(transform))
                continue;

            Button button = result.gameObject.GetComponentInParent<Button>();
            if (button != null && button.isActiveAndEnabled && button.interactable)
                return button;
        }
        return null;
    }

    static string GetButtonLabel(Button button)
    {
        TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            string text = label.text;
            if (!string.IsNullOrWhiteSpace(text))
                return StripRichText(text).Trim();
        }

        string objectName = button.gameObject.name;
        string spaced = System.Text.RegularExpressions.Regex.Replace(objectName,
            "([a-z])([A-Z])", "$1 $2");
        return spaced.Replace("_", " ").Trim();
    }

    static string StripRichText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty);
    }

    static string BuildExplanation(Button button, string label)
    {
        string identity = (button.gameObject.name + " " + label).ToLowerInvariant();
        string path = GetButtonSearchText(button, label);

        if (ContainsAny(path, "debugmenu"))
            return DescribeDebugButton(label, identity);
        if (ContainsAny(path, "titlescreen"))
        {
            if (ContainsAny(identity, "exit")) return "Closes the game.";
            if (ContainsAny(identity, "setting", "option")) return "Opens title screen settings.";
            if (ContainsAny(identity, "wipe", "delete")) return "Opens a confirmation to delete this save slot.";
            if (ContainsAny(identity, "continue")) return "Loads this save slot and resumes the restaurant.";
            if (ContainsAny(identity, "newgame", "new save")) return "Creates a new restaurant save in this slot.";
            if (ContainsAny(identity, "play", "start")) return "Starts the game using the selected save slot.";
            return $"Selects save slot {label}.";
        }
        if (ContainsAny(path, "storeappearance")
            && ContainsAny(identity, "texture_", "tint_", "surface", "swatch", "tile"))
            return "Applies this color or surface style to the restaurant.";
        if (ContainsAny(path, "rolepopup") && ContainsAny(identity, "entrance"))
            return "Marks this door as the restaurant entrance.";
        if (ContainsAny(path, "rolepopup") && ContainsAny(identity, "exit"))
            return "Marks this door as the restaurant exit.";
        if (ContainsAny(path, "flowcapture") && ContainsAny(identity, "finish"))
            return "Saves the station route you have drawn as a flow.";
        if (ContainsAny(path, "flowcapture") && ContainsAny(identity, "cancel"))
            return "Stops drawing the flow and restores its previous route.";
        if (ContainsAny(path, "managementtabinfoui") && ContainsAny(identity, "infobutton"))
            return "Shows help for the selected Build, Staff, or Business tab.";
        if (ContainsAny(identity, "tab_stations", "stationstab"))
            return "Shows equipment you can buy and place in the restaurant.";
        if (ContainsAny(identity, "tab_floor", "floortab"))
            return "Shows floor tiles you can buy to expand the restaurant.";
        if (ContainsAny(identity, "tab_customize", "customizetab"))
            return "Shows options for changing the restaurant's appearance.";
        if (ContainsAny(path, "missionlistui") && ContainsAny(identity, "taskstab"))
            return "Shows today's repeatable tasks.";
        if (ContainsAny(path, "missionlistui") && ContainsAny(identity, "progressiontab"))
            return "Shows milestone goals and your progression through them.";
        if (ContainsAny(path, "creditspage") && ContainsAny(identity, "credit"))
            return "Opens the source page for this credited asset in your browser.";
        if (ContainsAny(path, "guidebook") && ContainsAny(identity, "prevbutton", "previous"))
            return "Turns to the previous guidebook page.";
        if (ContainsAny(path, "guidebook") && ContainsAny(identity, "nextbutton", "next"))
            return "Turns to the next guidebook page.";

        if (ContainsAny(identity, "back step", "backstep"))
            return "Removes the last station added to the route you are drawing.";
        if (ContainsAny(path, "tutorial") && ContainsAny(identity, "next"))
            return "Shows the next tutorial explanation after its required action is complete.";
        if (ContainsAny(path, "cutscene") && ContainsAny(identity, "next", "advance"))
            return "Advances to the next line of Gus's cutscene dialogue.";
        if (ContainsAny(identity, "close", "done", "cancel", "back", "quit", "exit", "skip"))
            return DescribeNavigationButton(label, identity, path);
        if (ContainsAny(identity, "gusbhelpbutton"))
            return "Opens Gus help. Choose a button to hear what it does; choose DONE to close help.";
        if (ContainsAny(identity, "guidebookbutton", "readguidebook"))
            return "Opens the guidebook pages for game systems and controls.";
        if (ContainsAny(identity, "guidebookroot"))
            return "Closes the guidebook when you click outside its pages.";
        if (button.gameObject.name == "Book" && ContainsAny(path, "guidebook"))
            return "Keeps clicks inside the open guidebook so the page underneath is not activated.";
        if (ContainsAny(identity, "notificationbutton"))
            return "Opens recent messages and alerts.";
        if (ContainsAny(identity, "clearbutton") && ContainsAny(path, "notification"))
            return "Clears the notification history.";
        if (ContainsAny(identity, "notification") && !ContainsAny(identity, "notificationbutton"))
            return "Expands this notification to show the full message.";
        if (ContainsAny(identity, "musicpausebutton"))
            return "Pauses or resumes the current music track.";
        if (ContainsAny(identity, "musicnextbutton"))
            return "Skips to the next music track.";
        if (ContainsAny(identity, "track ") || ContainsAny(path, "musiclist"))
            return $"Plays {label}.";

        if (ContainsAny(identity, "statebutton"))
            return DescribeStationState(label);
        if (ContainsAny(identity, "assignworker"))
            return "Opens the worker list. Choose someone to staff this station.";
        if (ContainsAny(identity, "clearworker"))
            return "Unassigns this station's worker.";
        if (ContainsAny(identity, "assignoutput"))
            return "Lets you choose which station receives this station's output.";
        if (ContainsAny(identity, "clearoutput"))
            return "Disconnects this station from its current output destination.";
        if (ContainsAny(identity, "productpick"))
            return $"Sets this station's product to {label}.";
        if (ContainsAny(identity, "recipebutton"))
            return $"Sets the assembly station to make {label}. Its recipe determines the required inputs.";
        if (ContainsAny(identity, "throwoutall", "discardall"))
            return "Empties every slot in this pickup station.";

        if (ContainsAny(identity, "flowexpand"))
            return "Shows or hides the selected flow's route and assigned workers.";
        if (ContainsAny(identity, "expandarrow", "expandbutton"))
        {
            if (ContainsAny(path, "worker"))
                return "Expands this worker card to show their task, flow assignment, and transport upgrade.";
            if (ContainsAny(path, "expandfloorrow") || ContainsAny(identity, "expand floor"))
                return "Buys more floor space for the restaurant at the displayed price.";
            return "Expands this card to show more information and actions.";
        }
        if (ContainsAny(identity, "workerdropzone"))
            return "Adds a worker to this flow. The worker will carry items along its route.";
        if (ContainsAny(identity, "createflow"))
            return "Starts drawing a new flow. Select stations in route order, then finish to save it.";
        if (ContainsAny(identity, "editflow"))
            return "Edits the selected flow's station route.";
        if (ContainsAny(identity, "deleteflow"))
            return "Deletes the selected flow and removes its workers from that route.";
        if (ContainsAny(identity, "finishflow", "flowcapturefinish"))
            return "Saves the station route you have drawn as a flow.";
        if (ContainsAny(identity, "cancelflow", "flowcapturecancel"))
            return "Stops drawing the flow and restores its previous route.";
        if (ContainsAny(path, "workerrow"))
            return $"Removes {label} from this flow. The worker stays on your staff.";
        if (ContainsAny(path, "workerlist") && ContainsAny(path, "stationmanagepopup"))
            return $"Assigns {label} to the selected station.";
        if (ContainsAny(path, "employeeassignmentpanel") && button.gameObject.name == "Slot")
            return DescribeEmployeeAssignmentSlot(button);
        if (ContainsAny(path, "flowtabs", "flowtab"))
            return $"Selects the {label} flow so you can review its stations and workers.";

        if (ContainsAny(identity, "autorestock", "autoorder"))
            return "Cycles this ingredient's automatic reorder target. It keeps stock near the chosen level.";
        if (ContainsAny(identity, "addall"))
            return "Adds one pack of each orderable ingredient to the cart.";
        if (ContainsAny(identity, "clearcart"))
            return "Removes every item from the cart without ordering.";
        if (ContainsAny(identity, "expressorder"))
            return "Places this cart as an express delivery. It arrives sooner and costs more.";
        if (ContainsAny(identity, "placeorder"))
            return "Orders the ingredients in your cart.";
        if (ContainsAny(identity, "remove") && ContainsAny(path, "ingredientorderrow", "cart", "supply", "order"))
            return "Removes one pack of this ingredient from the cart.";
        if (ContainsAny(identity, "add") && ContainsAny(path, "ingredientorderrow", "cart", "supply", "order"))
            return "Adds one pack of this ingredient to the cart.";
        if (ContainsAny(identity, "auto") && ContainsAny(path, "ingredient", "supply", "pantry", "freezer"))
            return "Changes this ingredient's automatic reorder level.";
        if (ContainsAny(identity, "suppliestab"))
            return "Shows ingredients stocked by the pantry and freezer that you can order.";
        if (ContainsAny(identity, "menutab"))
            return "Shows menu recipes and lets you choose what customers can order.";
        if (ContainsAny(identity, "openrecipes"))
            return "Opens recipe flows to show each item's ingredients and production stations.";
        if (ContainsAny(path, "recipetab_"))
            return $"Shows the {label.ToLowerInvariant()} recipe category.";
        if (ContainsAny(path, "recipechart") && !ContainsAny(identity, "close"))
            return $"Selects {label} to inspect its required ingredients, stations, and output rate.";
        if (ContainsAny(path, "menu", "sell_"))
        {
            if (ContainsAny(identity, "header"))
                return $"Expands or collapses {label}'s row to show its recipe workflow.";
            return $"Expands or collapses {label}'s recipe row. The checkbox beside it controls whether customers can order it.";
        }
        if (ContainsAny(identity, "pausebutton"))
            return "Pauses the simulation while you adjust the restaurant.";
        if (ContainsAny(identity, "fastforwardbutton"))
            return "Runs the simulation faster so you can observe more of the shift.";
        if (ContainsAny(identity, "playbutton"))
        {
            GameTimeManager time = FindFirstObjectByType<GameTimeManager>();
            if (time != null && time.IsShiftOver)
                return "Opens the day summary when a shift ends. From the summary, continue to start the next day.";
            return "Runs the restaurant at normal speed.";
        }
        if (ContainsAny(identity, "nextday", "continuetonextday", "continuebutton"))
            return "Closes the day summary and starts the next day.";

        if (ContainsAny(identity, "inventorytab", "buildtab"))
            return "Opens Build to buy equipment and place it in the restaurant.";
        if (ContainsAny(identity, "managementtab", "stafftab"))
            return "Opens Staff to hire workers, assign flows, and upgrade transport.";
        if (ContainsAny(identity, "businesstab"))
            return "Opens Business to manage supplies and the items customers can order.";
        if (ContainsAny(identity, "purchaseundo", "undobutton"))
            return "Reverses your most recent eligible purchase.";
        if (ContainsAny(identity, "buybutton"))
            return $"Buys one {label} and selects it for placement. If you already own an unused copy, it selects that copy instead.";
        if (ContainsAny(identity, "namebutton", "preview"))
            return $"Selects {label} in Build and starts placing it.";
        if (ContainsAny(identity, "mk1", "mk2", "tier"))
            return $"Selects the {label} version to preview or place.";
        if (ContainsAny(identity, "movebutton"))
            return "Picks up the selected station so you can move it.";
        if (ContainsAny(identity, "clearstationselection"))
            return "Cancels the current station selection or move action.";
        if (identity == "clear clear")
            return ContainsAny(path, "build", "placer")
                ? "Clears the selected station so you can leave its move or placement controls."
                : "Clears the current selection.";
        if (ContainsAny(path, "purchaseundo"))
            return "Reverses the most recent eligible purchase shown on this button.";

        if (ContainsAny(identity, "hire", "recruit"))
            return "Hires a worker. Assign them to a flow so they can carry items.";
        if (ContainsAny(identity, "fire", "dismiss"))
            return "Removes this worker from your staff.";
        if (ContainsAny(identity, "customize", "editname", "randomize", "tint_", "face", "hat"))
            return ContainsAny(identity, "randomize")
                ? "Randomizes this worker's appearance."
                : "Changes this worker's name or appearance; it does not change their work rate.";
        if (button.gameObject.name == "Dim" && ContainsAny(path, "workercustomize"))
            return "Closes worker customization without applying another change.";
        if (ContainsAny(identity, "transport", "carry", "upgrade"))
            return "Raises this worker's maximum transport rate, so they can move more items per minute.";
        if (ContainsAny(identity, "work at"))
            return "Chooses the station this worker operates.";
        if (ContainsAny(identity, "deliver to"))
            return "Chooses the station this worker delivers items to.";

        if (ContainsAny(identity, "researchtopbutton", "researchbutton"))
            return "Opens the research tree.";
        if (ContainsAny(path, "researchtree") && !ContainsAny(identity, "close"))
            return $"Starts research for {label} when its prerequisites are met. The node shows its cost and unlocks.";
        if (ContainsAny(identity, "milestone"))
            return "Opens milestone progression and shows the service goals for the next stage.";
        if (ContainsAny(identity, "taskstab", "objectives", "dailytask"))
            return "Shows daily tasks and milestone service goals with their progress.";
        if (ContainsAny(identity, "analytics", "report", "performance"))
            return "Opens Analytics to review orders, food delivered to pickup, service, and item reputation.";

        if (ContainsAny(path, "registerpopup", "registerstation", "register"))
            return label.Equals("Enable", System.StringComparison.OrdinalIgnoreCase)
                ? "Turns this register on so customers can place orders."
                : "Turns this register off so customers cannot place orders here.";
        if (ContainsAny(path, "recipegraph") || ContainsAny(identity, "openrecipes", "recipe"))
            return $"Selects {label} to inspect its ingredients, station steps, and production rate.";
        if (ContainsAny(identity, "recipe", "burger", "fries", "shake", "cheese", "patty"))
            return $"Selects {label}'s recipe or production option.";

        if (ContainsAny(identity, "resume"))
            return "Closes the pause menu and resumes the restaurant.";
        if (ContainsAny(identity, "options"))
            return "Opens settings for sound, display, and controls.";
        if (ContainsAny(identity, "feedback"))
            return "Opens the playtest feedback form.";
        if (ContainsAny(identity, "credits"))
            return "Shows the people and assets credited in the game.";
        if (ContainsAny(identity, "audio", "video", "graphics", "keybinds"))
            return $"Opens the {label} settings page.";
        if (ContainsAny(identity, "prevres", "nextres"))
            return ContainsAny(identity, "prev")
                ? "Selects the previous display resolution."
                : "Selects the next display resolution.";
        if (ContainsAny(identity, "applyresolution"))
            return "Applies the selected display resolution.";
        if (ContainsAny(identity, "prev", "previous", "next", "cycle"))
            return ContainsAny(identity, "prev", "previous")
                ? $"Selects the previous {label} option."
                : $"Selects the next {label} option.";
        if (ContainsAny(identity, "open", "show"))
            return $"Opens {label}.";
        if (ContainsAny(identity, "select", "choose", "tab", "page"))
            return $"Opens the {label} view.";

        DelayedTooltip tooltip = button.GetComponent<DelayedTooltip>();
        if (tooltip != null && !string.IsNullOrWhiteSpace(tooltip.HelpText))
            return StripRichText(tooltip.HelpText).Trim().TrimEnd('.') + ".";
        return DescribeByScreen(button, label);
    }

    static string GetButtonSearchText(Button button, string label)
    {
        System.Text.StringBuilder value = new System.Text.StringBuilder(label);
        for (Transform current = button.transform; current != null; current = current.parent)
            value.Append(' ').Append(current.name);
        return value.ToString().ToLowerInvariant();
    }

    static string DescribeByScreen(Button button, string label)
    {
        string screen = GetButtonContext(button);
        if (screen == "staff and flow management")
            return $"Selects {label} in Staff. Use the displayed worker and flow details to see what assignment it changes.";
        if (screen == "research and progression")
            return $"Selects {label} in progression. Its card shows the requirement and reward.";
        if (screen == "Analytics")
            return $"Selects {label} in Analytics to inspect that performance value.";
        if (screen == "menu and recipe management")
            return $"Selects {label} in Menu or Recipes to inspect its sale setting or production steps.";
        if (screen == "supply ordering")
            return $"Selects {label} in Supplies to adjust ingredient stock or the order cart.";
        if (screen == "Build")
            return $"Selects {label} in Build so you can inspect or place it.";
        if (screen == "game settings")
            return $"Changes the displayed {label} setting.";
        if (screen == "the guidebook")
            return $"Opens the {label} page in the guidebook.";
        return $"Selects {label}. Check its nearby label or value to see which setting it changes.";
    }

    static string GetButtonContext(Button button)
    {
        string key = GetButtonSearchText(button, string.Empty);
        if (ContainsAny(key, "worker", "staff", "employee", "flow")) return "staff and flow management";
        if (ContainsAny(key, "research", "technology", "milestone", "progression")) return "research and progression";
        if (ContainsAny(key, "analytics", "report")) return "Analytics";
        if (ContainsAny(key, "recipe", "menu", "burger", "fries", "shake")) return "menu and recipe management";
        if (ContainsAny(key, "supply", "order", "inventory", "pantry", "freezer")) return "supply ordering";
        if (ContainsAny(key, "build", "station", "placement", "purchase")) return "Build";
        if (ContainsAny(key, "pause", "options", "resolution", "audio", "video", "keybind")) return "game settings";
        if (ContainsAny(key, "guidebook", "guide")) return "the guidebook";
        return "the current screen";
    }

    static string DescribeEmployeeAssignmentSlot(Button button)
    {
        return button.transform.GetSiblingIndex() == 0
            ? "Selects the station this worker will operate. Choose a kitchen or register station."
            : "Selects where this worker will deliver items from the assigned work station.";
    }

    static string DescribeDebugButton(string label, string identity)
    {
        if (ContainsAny(identity, "money")) return "Adds the displayed amount of test cash to your balance.";
        if (ContainsAny(identity, "hire worker")) return "Adds one worker to your staff for free. This is a testing shortcut.";
        if (ContainsAny(identity, "spawn customer")) return "Spawns one test customer immediately.";
        if (ContainsAny(identity, "ingredients")) return "Adds the displayed amount of every ingredient to stock.";
        if (ContainsAny(identity, "clear pickup")) return "Empties the pickup station for testing.";
        if (ContainsAny(identity, "place stations")) return "Automatically places any missing stations that fit in the restaurant.";
        if (ContainsAny(identity, "reset stations")) return "Clears station items in progress and aborts active worker tasks. Keeps station placement, recipes, assignments, upgrades, and stock.";
        if (ContainsAny(identity, "clear all stations")) return "Removes all placed equipment from the restaurant.";
        if (label == "1x") return "Sets simulation speed to normal.";
        if (label == "3x") return "Sets simulation speed to 3x.";
        if (label == "20x") return "Sets simulation speed to 20x.";
        if (ContainsAny(identity, "end day")) return "Moves the current shift directly to its end.";
        if (ContainsAny(identity, "pause / resume")) return "Pauses the simulation or resumes it if already paused.";
        if (ContainsAny(identity, "open current tasks")) return "Opens the current milestone tasks.";
        if (ContainsAny(identity, "milestone map")) return "Opens milestone progression for testing.";
        if (ContainsAny(identity, "reset research")) return "Resets all research progress in this save.";
        if (ContainsAny(identity, "complete all research")) return "Marks every research item complete in this save.";
        if (ContainsAny(identity, "skip tutorial")) return "Marks the tutorial complete and opens the restaurant.";
        if (ContainsAny(identity, "restart tutorial")) return "Restarts the tutorial from its first step.";
        if (ContainsAny(identity, "opening intro")) return "Plays Gus's opening cutscene again.";
        if (ContainsAny(identity, "cutscene")) return $"Plays the {label} milestone cutscene again.";
        if (int.TryParse(label, out int milestone)) return $"Sets Milestone {milestone} active for testing and marks earlier milestones complete.";
        if (ContainsAny(identity, "close")) return "Closes the debug panel.";
        return $"Runs the '{label}' debug action for testing.";
    }

    static string DescribeStationState(string label)
    {
        string state = label.ToLowerInvariant();
        if (state.Contains("working")) return "Shows the station's share of time spent successfully making its recipe.";
        if (state.Contains("blocked")) return "Shows time the station could not output because its output buffer or destination was full.";
        if (state.Contains("starved")) return "Shows time the station could not work because one or more recipe inputs were missing.";
        if (state.Contains("idle")) return "Shows time the station had no active production work to do.";
        return "Selects this station time category to show what the percentage means.";
    }

    static string DescribeNavigationButton(string label, string key, string path)
    {
        if (ContainsAny(key, "skip") && ContainsAny(path, "tutorial")) return "Skips the tutorial and opens the restaurant.";
        if (ContainsAny(key, "skip") && ContainsAny(path, "cutscene")) return "Skips the current cutscene.";
        if (ContainsAny(key, "quit", "exit", "menu")) return "Leaves this screen and returns to the title menu.";
        if (ContainsAny(key, "cancel")) return "Closes this action without applying its pending change.";
        if (ContainsAny(key, "back")) return "Returns to the previous page.";
        if (ContainsAny(key, "close", "done", "dismiss")) return "Closes this panel and returns to the restaurant.";
        return $"Opens {label} in the current screen.";
    }

    static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
            if (value.Contains(term)) return true;
        return false;
    }

    static TextMeshProUGUI CreateLabel(Transform parent, string value, float size,
        FontStyles style, Color color)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 0f;
        layout.flexibleHeight = 0f;
        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return text;
    }

    void Close()
    {
        Sfx.Play(SfxId.UiClose);
        gameObject.SetActive(false);
    }
}
