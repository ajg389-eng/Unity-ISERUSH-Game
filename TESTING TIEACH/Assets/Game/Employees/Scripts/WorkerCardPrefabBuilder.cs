using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the default Worker Card UI hierarchy for the Management Workers tab prefab.
/// </summary>
public static class WorkerCardPrefabBuilder
{
    public const string DefaultPrefabPath = "Assets/Prefabs/WorkerCard.prefab";

    static readonly Color SectionHeaderColor = new Color(0.62f, 0.66f, 0.74f, 1f);
    static readonly Color SectionPanelColor = new Color(0.13f, 0.15f, 0.2f, 0.92f);
    static readonly Color TaskColor = new Color(0.78f, 0.93f, 1f, 1f);
    static readonly Color BodyColor = new Color(0.88f, 0.9f, 0.96f, 1f);
    static readonly Color MutedColor = new Color(0.78f, 0.82f, 0.9f, 1f);

    public static GameObject Build()
    {
        var cardGo = new GameObject("WorkerCard", typeof(RectTransform));
        cardGo.AddComponent<Image>().color = new Color(0.18f, 0.2f, 0.26f, 0.98f);
        var le = cardGo.AddComponent<LayoutElement>();
        le.minHeight = 210;
        le.flexibleWidth = 1;

        var vlg = cardGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 12, 12);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var row1 = CreateHeaderRow(cardGo.transform, out var nameInputGo, out var fireBtn);
        BuildDetailsSection(cardGo.transform, out var currentTask, out var assignments, out var heldItems);

        var cardUI = cardGo.AddComponent<WorkerCardUI>();
        cardUI.nameInputObject = nameInputGo;
        cardUI.fireButton = fireBtn;
        cardUI.currentTaskText = currentTask;
        cardUI.assignmentsText = assignments;
        cardUI.heldItemsText = heldItems;
        return cardGo;
    }

    public static void BuildDetailsSection(
        Transform cardTransform,
        out TextMeshProUGUI currentTask,
        out TextMeshProUGUI assignments,
        out TextMeshProUGUI heldItems)
    {
        var detailsGo = new GameObject("WorkerDetails", typeof(RectTransform));
        detailsGo.transform.SetParent(cardTransform, false);
        var detailsLe = detailsGo.AddComponent<LayoutElement>();
        detailsLe.flexibleWidth = 1;
        detailsLe.minHeight = 140;

        var detailsVlg = detailsGo.AddComponent<VerticalLayoutGroup>();
        detailsVlg.spacing = 10;
        detailsVlg.childAlignment = TextAnchor.UpperLeft;
        detailsVlg.childControlWidth = true;
        detailsVlg.childControlHeight = true;
        detailsVlg.childForceExpandWidth = true;
        detailsVlg.childForceExpandHeight = false;

        var taskSection = CreateSection(detailsGo.transform, "TaskSection", minHeight: 52);
        CreateSectionHeader(taskSection, "CURRENT TASK");
        currentTask = CreateBodyLabel(taskSection, "CurrentTask", "—", 14, TaskColor, minHeight: 22);

        var stationsSection = CreateSection(detailsGo.transform, "StationsSection", minHeight: 88);
        CreateSectionHeader(stationsSection, "STATIONS");
        assignments = CreateBodyLabel(stationsSection, "Assignments", "No stations assigned", 13, BodyColor, minHeight: 56);
        assignments.lineSpacing = 6;
        assignments.paragraphSpacing = 4;

        var carryingSection = CreateSection(detailsGo.transform, "CarryingSection", minHeight: 48);
        CreateSectionHeader(carryingSection, "CARRYING");
        heldItems = CreateBodyLabel(carryingSection, "HeldItems", "Nothing", 13, MutedColor, minHeight: 20);
    }

    static Transform CreateHeaderRow(Transform parent, out GameObject nameInputGo, out Button fireBtn)
    {
        var row1 = new GameObject("Row1", typeof(RectTransform));
        row1.transform.SetParent(parent, false);
        var rowLe = row1.AddComponent<LayoutElement>();
        rowLe.minHeight = 36;
        rowLe.preferredHeight = 36;
        var hlg = row1.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        nameInputGo = CreateNameInput(row1.transform);
        fireBtn = CreateButton(row1.transform, "Fire", new Color(0.62f, 0.22f, 0.22f, 1f));
        return row1.transform;
    }

    static Transform CreateSection(Transform parent, string name, float minHeight)
    {
        var sectionGo = new GameObject(name, typeof(RectTransform));
        sectionGo.transform.SetParent(parent, false);
        sectionGo.AddComponent<Image>().color = SectionPanelColor;

        var le = sectionGo.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 1;

        var vlg = sectionGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return sectionGo.transform;
    }

    static void CreateSectionHeader(Transform section, string text)
    {
        var go = new GameObject("Header", typeof(RectTransform));
        go.transform.SetParent(section, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 14;
        le.preferredHeight = 14;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 10;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = SectionHeaderColor;
        tmp.characterSpacing = 4f;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    static TextMeshProUGUI CreateBodyLabel(
        Transform parent,
        string objectName,
        string text,
        float fontSize,
        Color color,
        float minHeight)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 1;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return tmp;
    }

    static GameObject CreateNameInput(Transform parent)
    {
        var go = new GameObject("NameInput", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 160;
        le.flexibleWidth = 1;
        le.minHeight = 32;
        le.preferredHeight = 32;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.11f, 0.16f, 1f);
        var input = go.AddComponent<InputField>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 4);
        textRect.offsetMax = new Vector2(-10, -4);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.supportRichText = false;
        input.textComponent = text;
        input.interactable = true;
        input.transition = Selectable.Transition.ColorTint;
        return go;
    }

    static Button CreateButton(Transform parent, string label, Color color)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 72;
        le.preferredWidth = 72;
        le.minHeight = 32;
        le.preferredHeight = 32;
        le.flexibleWidth = 0;
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }
}
