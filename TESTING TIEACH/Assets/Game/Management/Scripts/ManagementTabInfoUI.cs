using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared contextual help for every tab on the Management screen.
/// The button stays in the same place while its title and copy follow the selected tab.
/// </summary>
public class ManagementTabInfoUI : MonoBehaviour
{
    public const string ObjectName = "ManagementTabInfoUI";

    Button infoButton;
    Button guidebookButton;
    TextMeshProUGUI guidebookButtonText;
    GameObject overlay;
    TextMeshProUGUI titleText;
    TextMeshProUGUI bodyText;
    GameObject activePanel;
    string activeGuidebookChapter;

    public static ManagementTabInfoUI EnsureOn(Transform managementPanel)
    {
        if (managementPanel == null) return null;

        Transform existing = managementPanel.Find(ObjectName);
        ManagementTabInfoUI ui;
        if (existing != null)
        {
            ui = existing.GetComponent<ManagementTabInfoUI>();
            if (ui == null) ui = existing.gameObject.AddComponent<ManagementTabInfoUI>();
        }
        else
        {
            var root = new GameObject(ObjectName, typeof(RectTransform), typeof(ManagementTabInfoUI));
            root.transform.SetParent(managementPanel, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var layout = root.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            ui = root.GetComponent<ManagementTabInfoUI>();
        }

        ui.BuildIfNeeded();
        ui.transform.SetAsLastSibling();
        return ui;
    }

    public void SetTab(GameObject panel)
    {
        activePanel = panel;
        if (overlay != null && overlay.activeSelf)
            RefreshCopy();
        transform.SetAsLastSibling();
    }

    public void SetButtonPosition(Vector2 anchoredPosition)
    {
        BuildIfNeeded();
        if (infoButton != null)
            ((RectTransform)infoButton.transform).anchoredPosition = anchoredPosition;
    }

    void BuildIfNeeded()
    {
        if (infoButton != null && overlay != null) return;

        Transform foundButton = transform.Find("InfoButton");
        if (foundButton != null) infoButton = foundButton.GetComponent<Button>();
        if (infoButton == null) infoButton = CreateInfoButton();

        Transform foundOverlay = transform.Find("InfoOverlay");
        if (foundOverlay != null) overlay = foundOverlay.gameObject;
        if (overlay == null) CreateOverlay();

        infoButton.onClick.RemoveListener(ToggleInfo);
        infoButton.onClick.AddListener(ToggleInfo);
        infoButton.gameObject.SetActive(false);
        overlay.SetActive(false);
    }

    Button CreateInfoButton()
    {
        var go = new GameObject("InfoButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-14f, -10f);
        rt.sizeDelta = new Vector2(32f, 32f);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.25f, 0.47f, 0.72f, 1f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        Stretch(labelGo.GetComponent<RectTransform>(), 0f);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "i";
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        Button button = go.GetComponent<Button>();
        GameIconLibrary.ApplyToButton(button, GameIconId.Information, 20f, 0f, true);
        return button;
    }

    void CreateOverlay()
    {
        overlay = new GameObject("InfoOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(transform, false);
        Stretch(overlay.GetComponent<RectTransform>(), 0f);
        var blocker = overlay.GetComponent<Image>();
        blocker.color = new Color(0.025f, 0.03f, 0.05f, 0.55f);
        blocker.raycastTarget = true;

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(overlay.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(700f, 620f);
        card.GetComponent<Image>().color = new Color(0.105f, 0.115f, 0.16f, 1f);

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(card.transform, false);
        var accentRt = accent.GetComponent<RectTransform>();
        accentRt.anchorMin = new Vector2(0f, 1f);
        accentRt.anchorMax = new Vector2(1f, 1f);
        accentRt.pivot = new Vector2(0.5f, 1f);
        accentRt.anchoredPosition = Vector2.zero;
        accentRt.sizeDelta = new Vector2(0f, 5f);
        accent.GetComponent<Image>().color = new Color(0.28f, 0.64f, 0.92f, 1f);

        titleText = CreateText(card.transform, "Title", 23f, FontStyles.Bold);
        var titleRt = (RectTransform)titleText.transform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -18f);
        titleRt.sizeDelta = new Vector2(-92f, 38f);
        titleText.alignment = TextAlignmentOptions.MidlineLeft;

        var close = CreateCloseButton(card.transform);
        close.onClick.AddListener(Close);

        var bodyFrame = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        bodyFrame.transform.SetParent(card.transform, false);
        var bodyRt = bodyFrame.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(18f, 64f);
        bodyRt.offsetMax = new Vector2(-18f, -68f);
        bodyFrame.GetComponent<Image>().color = new Color(0.065f, 0.072f, 0.10f, 0.92f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(bodyFrame.transform, false);
        var viewportRt = viewport.GetComponent<RectTransform>();
        Stretch(viewportRt, 10f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        bodyText = CreateText(viewport.transform, "HelpText", 15f, FontStyles.Normal);
        var textRt = (RectTransform)bodyText.transform;
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.pivot = new Vector2(0.5f, 1f);
        textRt.anchoredPosition = Vector2.zero;
        textRt.sizeDelta = Vector2.zero;
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        var fitter = bodyText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = bodyFrame.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        GameUITheme.ConfigureScroll(scroll);
        scroll.viewport = viewportRt;
        scroll.content = textRt;

        guidebookButton = CreateGuidebookButton(card.transform);
    }

    Button CreateCloseButton(Transform parent)
    {
        var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-14f, -14f);
        rt.sizeDelta = new Vector2(36f, 36f);
        go.GetComponent<Image>().color = new Color(0.32f, 0.34f, 0.42f, 1f);

        var label = CreateText(go.transform, "Label", 23f, FontStyles.Normal);
        Stretch((RectTransform)label.transform, 0f);
        label.text = "×";
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        Button button = go.GetComponent<Button>();
        GameIconLibrary.ApplyToButton(button, GameIconId.Cross, 20f, 0f, true);
        return button;
    }

    Button CreateGuidebookButton(Transform parent)
    {
        var go = new GameObject("ReadGuidebookButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-18f, 14f);
        rt.sizeDelta = new Vector2(190f, 36f);

        var image = go.GetComponent<Image>();
        image.color = GameUITheme.Surface;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        guidebookButtonText = CreateText(go.transform, "Label", 14f, FontStyles.Bold);
        Stretch((RectTransform)guidebookButtonText.transform, 0f);
        guidebookButtonText.text = "Read Guidebook";
        guidebookButtonText.alignment = TextAlignmentOptions.Center;

        button.onClick.AddListener(OpenGuidebookReference);
        GameUITheme.ApplyCompactControlEffects(button);
        return button;
    }

    static TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.94f, 0.96f, 1f, 1f);
        tmp.raycastTarget = false;
        tmp.lineSpacing = 5f;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    void ToggleInfo()
    {
        if (overlay.activeSelf) Close();
        else Open();
    }

    void Open()
    {
        RefreshCopy();
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null && overlay.transform.parent != rootCanvas.transform)
            overlay.transform.SetParent(rootCanvas.transform, false);
        Stretch((RectTransform)overlay.transform, 0f);
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        Sfx.Play(SfxId.UiOpen);
    }

    void OnDisable()
    {
        if (overlay != null)
            overlay.SetActive(false);
    }

    public static bool OpenActiveHelp()
    {
        ManagementTabInfoUI[] helpers = FindObjectsByType<ManagementTabInfoUI>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < helpers.Length; i++)
        {
            ManagementTabInfoUI helper = helpers[i];
            if (helper == null || !helper.gameObject.activeInHierarchy || helper.activePanel == null)
                continue;
            helper.Open();
            return true;
        }
        return false;
    }

    public void Close()
    {
        if (overlay == null || !overlay.activeSelf) return;
        overlay.SetActive(false);
        Sfx.Play(SfxId.UiClose);
    }

    void Update()
    {
        if (overlay != null && overlay.activeSelf && !UIInputFocusGuard.IsTyping && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    void RefreshCopy()
    {
        GetCopy(activePanel, out string title, out string body, out activeGuidebookChapter);
        if (titleText != null) titleText.text = "GUS  |  " + title;
        if (bodyText != null) bodyText.text = body;
        if (guidebookButton != null)
            guidebookButton.interactable = !string.IsNullOrWhiteSpace(activeGuidebookChapter);
        if (guidebookButtonText != null)
            guidebookButtonText.text = "Read Guidebook";
    }

    void OpenGuidebookReference()
    {
        if (string.IsNullOrWhiteSpace(activeGuidebookChapter)) return;

        PauseMenuUI pauseMenu = PauseMenuUI.Instance;
        if (pauseMenu == null)
            pauseMenu = FindFirstObjectByType<PauseMenuUI>();
        if (pauseMenu == null) return;

        Close();
        pauseMenu.OpenGuidebookFromHud(activeGuidebookChapter);
    }

    static string FormatHelp(string whatYouSee, string nextStep, string iseConnection)
    {
        return "<b>What you are seeing</b>\n" + whatYouSee + "\n\n" +
            "<b>Try this</b>\n" + nextStep + "\n\n" +
            "<color=#73BFF2><b>ISE connection</b></color>\n" + iseConnection;
    }

    static void GetCopy(GameObject panel, out string title, out string body, out string guidebookChapter)
    {
        if (panel != null && panel.name == InventoryUI.StationsPanelName)
        {
            title = "Stations";
            guidebookChapter = "Work Design";
            body = FormatHelp(
                "Station cards show how many you own and the next purchase cost. There is no station quantity cap, so plan around cost and floor space.",
                "Choose a station, place it on a valid footprint, and leave a clear route between work areas.",
                "Layout affects worker travel, handling effort, and total system capacity.");
            return;
        }

        if (panel != null && panel.name == InventoryUI.FloorPanelName)
        {
            title = "Floor";
            guidebookChapter = "Work Design";
            body = FormatHelp(
                "The Floor tab shows the kitchen size, the next expansion, and its price.",
                "Expand when the current layout cannot fit the needed stations or clear walking space.",
                "More floor adds capacity for equipment, but can also increase travel.");
            return;
        }

        if (panel != null && panel.name == InventoryUI.CustomizePanelName)
        {
            title = "Customize";
            guidebookChapter = "Work Design";
            body = FormatHelp(
                "These controls change the look of the walls, floor, and roof.",
                "Use clear visual differences to make work areas easier to recognize.",
                "Readable environments help people orient themselves. These appearance settings do not change production.");
            return;
        }

        if (panel != null && (panel.name == "TasksPage" || panel.name == "DailyTasksPage"))
        {
            title = "Tasks";
            guidebookChapter = "ISE Loop";
            body = FormatHelp(
                "The highlighted task is the current objective. Later tasks show what comes next.",
                "Read the task condition and complete that action before changing other systems.",
                "Clear, staged objectives help you learn a complex process one step at a time.");
            return;
        }

        if (panel != null && panel.name == "ProgressionPage")
        {
            title = "Progression";
            guidebookChapter = "Improve";
            body = FormatHelp(
                "Milestones group tasks into stages. Research projects become available as you progress.",
                "Complete the current milestone tasks, then choose research that addresses your next capacity need.",
                "Milestones connect practice with gradual system improvement.");
            return;
        }

        if (panel != null && panel.GetComponentInChildren<IngredientsOrderUI>(true) != null)
        {
            title = "Supply and Menu";
            guidebookChapter = "Flow";
            body = FormatHelp(
                "The menu controls which products customers can order. Supply shows current and incoming ingredients.",
                "Enable the products you plan to make, then check ingredient stock against their recipes before the shift.",
                "Inventory planning balances shortage risk against cash tied up in stock.");
            return;
        }

        if (panel != null && panel.GetComponentInChildren<WorkersUI>(true) != null)
        {
            title = "Workers and Flows";
            guidebookChapter = "Flow";
            body = FormatHelp(
                "Flows connect the stations that move and transform items. Workers assigned to a flow handle its transfers.",
                "Check the selected flow's planned workload and worker coverage. Confirm every node is connected through to Pickup.",
                "Balance labor and station workload across the full process instead of optimizing one station alone.");
            return;
        }

        if (panel != null && panel.GetComponentInChildren<CustomersUI>(true) != null)
        {
            title = "Customer Analytics";
            guidebookChapter = "Plan";
            body = FormatHelp(
                "This screen tracks customer visits, demand, and product output.",
                "Compare each item's demand rate with its completed rate. Use orders per minute for customer service and items per minute for production.",
                "Demand and throughput are different measures. Match the production system to the work customers request.");
            return;
        }

        title = "Store Stats";
        guidebookChapter = "Diagnose";
        body = FormatHelp(
            "Store Stats brings together queues, utilization, wait, Pickup stock, waste, and revenue.",
            "Find where work is accumulating, then compare that queue with the station or worker serving it.",
            "Work in process and waiting time help reveal constraints across the whole system.");
    }
}
