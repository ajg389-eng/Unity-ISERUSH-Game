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
        var cardImage = cardGo.AddComponent<Image>();
        cardImage.color = new Color(0.2f, 0.22f, 0.28f, 0.98f);
        var le = cardGo.AddComponent<LayoutElement>();
        le.minHeight = 64;
        le.preferredHeight = 64;
        le.flexibleWidth = 1;

        var hlg = cardGo.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 10, 8, 8);
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        var nameInputGo = CreateNameInput(cardGo.transform);

        var stationsGo = new GameObject("StationsLabel", typeof(RectTransform));
        stationsGo.transform.SetParent(cardGo.transform, false);
        stationsGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        var stationsTmp = stationsGo.AddComponent<TextMeshProUGUI>();
        stationsTmp.text = "Unassigned (idle)";
        stationsTmp.fontSize = 13;
        stationsTmp.color = new Color(0.78f, 0.82f, 0.9f, 1f);
        stationsTmp.alignment = TextAlignmentOptions.Left;
        stationsTmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) stationsTmp.font = TMP_Settings.defaultFontAsset;

        var fireBtn = CreateButton(cardGo.transform, "Fire", new Color(0.62f, 0.22f, 0.22f, 1f));

        var cardUI = cardGo.AddComponent<WorkerCardUI>();
        cardUI.nameInputObject = nameInputGo;
        cardUI.fireButton = fireBtn;
        cardUI.stationsLabel = stationsTmp;
        return cardGo;
    }

    static GameObject CreateNameInput(Transform parent)
    {
        var go = new GameObject("NameInput", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 110;
        le.preferredWidth = 130;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.13f, 0.18f, 1f);
        var input = go.AddComponent<InputField>();

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 2);
        textRect.offsetMax = new Vector2(-8, -2);
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
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 64;
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
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        return btn;
    }
}
