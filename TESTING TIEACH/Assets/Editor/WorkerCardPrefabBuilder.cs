using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the default Worker Card UI hierarchy for the Management Workers tab prefab.
/// </summary>
public static class WorkerCardPrefabBuilder
{
    public const string DefaultPrefabPath = "Assets/Prefabs/WorkerCard.prefab";

    public static GameObject Build()
    {
        var cardGo = new GameObject("WorkerCard", typeof(RectTransform));
        var cardRect = (RectTransform)cardGo.transform;
        cardRect.sizeDelta = new Vector2(0, 72);
        var cardImage = cardGo.AddComponent<Image>();
        cardImage.color = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        var le = cardGo.AddComponent<LayoutElement>();
        le.minHeight = 72;
        le.preferredHeight = 72;
        le.flexibleWidth = 1;

        var vlg = cardGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.padding = new RectOffset(8, 8, 6, 6);
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        var row1 = new GameObject("Row1", typeof(RectTransform));
        row1.transform.SetParent(cardGo.transform, false);
        var row1Rect = (RectTransform)row1.transform;
        row1Rect.sizeDelta = new Vector2(0, 28);
        var row1HLG = row1.AddComponent<HorizontalLayoutGroup>();
        row1HLG.spacing = 8;
        row1HLG.childForceExpandWidth = false;
        row1HLG.childControlWidth = true;
        var row1LE = row1.AddComponent<LayoutElement>();
        row1LE.minHeight = 28;
        row1LE.preferredHeight = 28;

        var nameInputGo = CreateNameInput(row1.transform);
        var fireBtn = CreateButton(row1.transform, "Fire", new Color(0.6f, 0.2f, 0.2f, 1f));

        var row2 = new GameObject("Row2", typeof(RectTransform));
        row2.transform.SetParent(cardGo.transform, false);
        var row2HLG = row2.AddComponent<HorizontalLayoutGroup>();
        row2HLG.spacing = 12;
        row2HLG.childForceExpandWidth = false;
        var row2LE = row2.AddComponent<LayoutElement>();
        row2LE.minHeight = 24;
        row2LE.preferredHeight = 24;

        var tFreezer = CreateStationToggle(row2.transform, "Freezer");
        var tGrill = CreateStationToggle(row2.transform, "Grill");
        var tPantry = CreateStationToggle(row2.transform, "Pantry");
        var tAssembly = CreateStationToggle(row2.transform, "Assembly");

        var cardUI = cardGo.AddComponent<WorkerCardUI>();
        cardUI.nameInputObject = nameInputGo;
        cardUI.fireButton = fireBtn;
        cardUI.toggleFreezer = tFreezer;
        cardUI.toggleGrill = tGrill;
        cardUI.togglePantry = tPantry;
        cardUI.toggleAssembly = tAssembly;
        return cardGo;
    }

    static GameObject CreateNameInput(Transform parent)
    {
        var go = new GameObject("NameInput", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(180, 26);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 120;
        le.preferredWidth = 180;
        le.flexibleWidth = 1;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        var input = go.AddComponent<InputField>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 2);
        textRect.offsetMax = new Vector2(-6, -2);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
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
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(70, 26);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 70;
        le.preferredWidth = 70;
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
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }

    static Toggle CreateStationToggle(Transform parent, string label)
    {
        var go = new GameObject("Toggle_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(80, 22);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 80;
        le.preferredWidth = 80;
        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bg;

        var checkGo = new GameObject("Checkmark", typeof(RectTransform));
        checkGo.transform.SetParent(go.transform, false);
        var checkRect = (RectTransform)checkGo.transform;
        checkRect.anchorMin = new Vector2(0, 0.25f);
        checkRect.anchorMax = new Vector2(0, 0.75f);
        checkRect.offsetMin = new Vector2(4, 0);
        checkRect.offsetMax = new Vector2(22, 0);
        var checkImg = checkGo.AddComponent<Image>();
        checkImg.color = new Color(0.3f, 0.8f, 0.3f, 1f);
        toggle.graphic = checkImg;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(26, 0);
        labelRect.offsetMax = new Vector2(-4, 0);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = new Color(0.9f, 0.9f, 0.95f, 1f);
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return toggle;
    }
}
