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
    GameObject overlay;
    TextMeshProUGUI titleText;
    TextMeshProUGUI bodyText;
    GameObject activePanel;

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

        return go.GetComponent<Button>();
    }

    void CreateOverlay()
    {
        overlay = new GameObject("InfoOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(transform, false);
        Stretch(overlay.GetComponent<RectTransform>(), 0f);
        var blocker = overlay.GetComponent<Image>();
        blocker.color = new Color(0.025f, 0.03f, 0.05f, 0.82f);
        blocker.raycastTarget = true;

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(overlay.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.08f, 0.10f);
        cardRt.anchorMax = new Vector2(0.92f, 0.90f);
        cardRt.offsetMin = Vector2.zero;
        cardRt.offsetMax = Vector2.zero;
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
        bodyRt.offsetMin = new Vector2(18f, 18f);
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
        scroll.scrollSensitivity = 24f;
        scroll.viewport = viewportRt;
        scroll.content = textRt;
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
        return go.GetComponent<Button>();
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
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        Sfx.Play(SfxId.UiOpen);
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
        GetCopy(activePanel, out string title, out string body);
        if (titleText != null) titleText.text = title;
        if (bodyText != null) bodyText.text = body;
    }

    static void GetCopy(GameObject panel, out string title, out string body)
    {
        if (panel != null && panel.name == InventoryUI.StationsPanelName)
        {
            title = "Stations tab help";
            body =
                "<b>Station cards</b>\nEach card shows a station you can buy and place. The first number is how many you own and the second is your current cap. The price is removed when the station is purchased.\n\n" +
                "<b>Capacity limit</b>\nYou begin with a cap of 2 for every station type. Completing a milestone raises every station cap by 1. MAX means you must complete another milestone before buying more of that station.\n\n" +
                "<b>Place a station</b>\nSelect a card, move the preview over a valid grid tile, rotate if needed, and click to place it. Green tiles are valid; blocked or occupied tiles cannot be used.\n\n" +
                "<b>Production stations</b>\nFreezers and pantries supply materials. Grills, fryers, drink stations, and assembly stations transform materials. The Pickup Station holds finished products. Registers take customer orders.\n\n" +
                "<b>Counter equipment</b>\nRegisters sit on top of counter slots. Pickup Stations use counter-grid placement and hold finished products for collection.\n\n" +
                "<b>Undo purchase</b>\nThe bottom button reverses the latest eligible purchase.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Layout affects travel distance, handling time, congestion, and therefore the capacity of the whole workflow.";
            return;
        }

        if (panel != null && panel.name == InventoryUI.FloorPanelName)
        {
            title = "Floor tab help";
            body =
                "<b>Current floor size</b>\nShows the kitchen grid dimensions now and the dimensions after the next expansion.\n\n" +
                "<b>Expand Floor</b>\nPurchases the displayed number of rows and columns for the shown price. The button is disabled when you cannot afford it or the floor is already at its limit.\n\n" +
                "<b>Undo Floor</b>\nReverses the latest eligible floor expansion when the added area can be safely removed.\n\n" +
                "<b>Planning before buying</b>\nExpansion creates more placement capacity, but it can also increase walking distance. Use the existing footprint efficiently before adding space.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Facility size and layout are capacity decisions. More space is useful only when the improvement is worth its cost and added travel.";
            return;
        }

        if (panel != null && panel.name == "TasksPage")
        {
            title = "Tasks tab help";
            body =
                "<b>Current task</b>\nThe highlighted task is the next objective to complete. Its description states the gameplay condition being measured.\n\n" +
                "<b>Status symbols</b>\nA pointer marks the current task, an open circle marks a later task, and a check mark marks a completed task.\n\n" +
                "<b>Why tasks matter</b>\nTasks introduce the restaurant systems in a controlled order and give you a practical reason to apply each ISE concept.\n\n" +
                "<color=#73BFF2><b>Tip:</b></color> If progress stops, inspect the exact condition in the current task rather than changing several systems at once.";
            return;
        }

        if (panel != null && panel.name == "ProgressionPage")
        {
            title = "Progression tab help";
            body =
                "<b>Milestones</b>\nMilestones group related tasks into larger learning stages. Completed stages remain visible so you can track what has been mastered.\n\n" +
                "<b>Locked progression</b>\nFinish the onboarding tutorial before later milestones become available. Then complete the required tasks in the current stage.\n\n" +
                "<b>Quiz</b>\nWhen a milestone is ready, Open Quiz tests the ideas used during that stage. Passing unlocks the next part of the game.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Progression connects game decisions to concepts such as flow, capacity, inventory, bottlenecks, and continuous improvement.";
            return;
        }

        if (panel != null && panel.GetComponentInChildren<IngredientsOrderUI>(true) != null)
        {
            title = "Food tab help";
            body =
                "<b>Items to sell</b>\nThe green toggle determines whether customers can order that product. Each item also shows its station sequence and the material used at every step.\n\n" +
                "<b>Workflow</b>\nRead the stations from left to right. A product can only be completed when its required stations exist, have materials, and belong to a usable worker flow.\n\n" +
                "<b>Buy ingredients</b>\nEach row shows the ingredient, current stock, pack size, and purchase price. Ordering adds one pack to kitchen inventory and subtracts its cost from your money.\n\n" +
                "<b>Undo purchase</b>\nThe bottom button reverses your latest eligible purchase.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Inventory protects production from shortages, but excess stock ties up money. Match purchasing to expected demand.";
            return;
        }

        if (panel != null && panel.GetComponentInChildren<WorkersUI>(true) != null)
        {
            title = "Workers tab help";
            body =
                "<b>Hire</b>\nAdds a worker for the displayed cost. More labor can increase capacity, but every hire must be used effectively.\n\n" +
                "<b>Flow chips</b>\nSelect a saved flow to inspect or modify it. Click the name field to rename the selected flow.\n\n" +
                "<b>Create Flow</b>\nReturns you to the kitchen. Click stations in the exact order work should travel, then finish the route.\n\n" +
                "<b>Edit Flow</b>\nReopens the selected route so its station order can be changed. The station chips show the current sequence.\n\n" +
                "<b>Workers on a flow</b>\nAssign workers from their cards below. Multiple workers share the flow's work automatically. Click a worker name in the flow to remove that assignment.\n\n" +
                "<b>Economics</b>\nThe analysis highlights route length, cycle behavior, and likely imbalance. A long route or one slow station can limit the entire system.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Balance labor and station capacity around the bottleneck instead of maximizing every station independently.";
            return;
        }

        if (panel != null && panel.GetComponentInChildren<CustomersUI>(true) != null)
        {
            title = "Customers tab help";
            body =
                "<b>In store now</b>\nThe number of customers currently inside the system. This is customer work-in-process, or WIP.\n\n" +
                "<b>Visited today</b>\nAll customers who entered during the current shift. <b>Walked out</b> counts customers who left without completing service.\n\n" +
                "<b>Visit trend</b>\nEach bar shows arrivals during one hour. The highlighted bar is the current hour. Use the pattern to prepare inventory and labor before a rush.\n\n" +
                "<b>Required output per minute</b>\nThe estimated production rate needed for each menu item. Ordered, ready, and cooking values show where demand currently sits in the system.\n\n" +
                "<color=#73BFF2><b>ISE idea:</b></color> Demand changes over time. Capacity that works during a quiet hour may fail during the peak.";
            return;
        }

        title = "Store Stats tab help";
        body =
            "<b>Customers in system (WIP)</b>\nEveryone currently waiting or being served. Rising WIP often signals congestion.\n\n" +
            "<b>Queue length</b>\nCustomers waiting at each service point. A consistently long queue helps locate a bottleneck.\n\n" +
            "<b>Average wait time</b>\nHow long customers wait before service. <b>Throughput</b> is completed orders per minute.\n\n" +
            "<b>Station and worker utilization</b>\nThe percentage of available time spent working. Very high utilization can create long queues because the resource has little spare capacity.\n\n" +
            "<b>Pickup Station stock</b>\nFinished products waiting for customers. Too little creates shortages; too much risks waste.\n\n" +
            "<b>Waste and revenue</b>\nWaste counts expired products. Revenue counts money earned from completed sales.\n\n" +
            "<b>Order completion time</b>\nThe full cycle time from order creation to pickup.\n\n" +
            "<color=#73BFF2><b>ISE idea:</b></color> Use several measures together. Improving one number can hurt another, such as raising inventory to reduce waits while increasing waste.";
    }
}
