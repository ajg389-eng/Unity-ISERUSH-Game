using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the square inventory item card hierarchy used by InventoryUI.
/// </summary>
public static class InventoryItemCardBuilder
{
    public const string CardName = "InventoryItemCard";

    public static InventoryItemCardUI Create(Transform parent)
    {
        var cardGo = new GameObject(CardName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(InventoryItemCardUI));
        cardGo.transform.SetParent(parent, false);

        var cardImg = cardGo.GetComponent<Image>();
        cardImg.color = InventoryItemCardUI.CardColor;
        cardImg.raycastTarget = true;

        var cardLe = cardGo.GetComponent<LayoutElement>();
        cardLe.minWidth = 160f;
        cardLe.minHeight = 200f;
        cardLe.preferredWidth = 220f;
        cardLe.preferredHeight = 240f;

        var vlg = cardGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var card = cardGo.GetComponent<InventoryItemCardUI>();

        // Name (click to place)
        var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        nameGo.transform.SetParent(cardGo.transform, false);
        nameGo.GetComponent<LayoutElement>().preferredHeight = 28f;
        var nameBg = nameGo.GetComponent<Image>();
        nameBg.color = InventoryItemCardUI.CardColor;
        nameBg.raycastTarget = true;
        var nameBtn = nameGo.GetComponent<Button>();
        nameBtn.targetGraphic = nameBg;
        nameBtn.transition = Selectable.Transition.None;
        card.nameText = CreateTmp(nameGo.transform, "Label", "Item", 16, FontStyles.Bold, InventoryItemCardUI.TextColor);

        // Preview area (click to place)
        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        previewGo.transform.SetParent(cardGo.transform, false);
        var previewLe = previewGo.GetComponent<LayoutElement>();
        previewLe.flexibleHeight = 1f;
        previewLe.minHeight = 110f;
        previewLe.preferredHeight = 140f;

        var previewBg = previewGo.GetComponent<Image>();
        previewBg.color = ItemPreviewThumbnails.BackgroundColor;
        var previewBtn = previewGo.GetComponent<Button>();
        previewBtn.targetGraphic = previewBg;
        previewBtn.transition = Selectable.Transition.None;

        var spriteGo = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
        spriteGo.transform.SetParent(previewGo.transform, false);
        Stretch(spriteGo.transform as RectTransform, 8f);
        card.previewImage = spriteGo.GetComponent<Image>();
        card.previewImage.color = Color.white;
        card.previewImage.preserveAspect = true;
        card.previewImage.raycastTarget = false;
        card.previewImage.enabled = false;

        var rawGo = new GameObject("Raw", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        rawGo.transform.SetParent(previewGo.transform, false);
        var rawRect = rawGo.transform as RectTransform;
        rawRect.anchorMin = Vector2.zero;
        rawRect.anchorMax = Vector2.one;
        rawRect.offsetMin = new Vector2(8f, 8f);
        rawRect.offsetMax = new Vector2(-8f, -8f);
        card.previewRawImage = rawGo.GetComponent<RawImage>();
        card.previewRawImage.color = Color.white;
        card.previewRawImage.raycastTarget = false;
        var aspect = rawGo.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1f;

        card.selectButtons = new[] { nameBtn, previewBtn };

        // Footer: qty | price | +
        var footer = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(cardGo.transform, false);
        footer.GetComponent<LayoutElement>().preferredHeight = 40f;
        var hlg = footer.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        card.qtyText = CreateFooterChip(footer.transform, "Qty", "0");
        card.priceText = CreateFooterChip(footer.transform, "Price", "$0");
        card.buyButton = CreateBuyButton(footer.transform);

        return card;
    }

    static TextMeshProUGUI CreateFooterChip(Transform parent, string name, string value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.color = InventoryItemCardUI.ChipColor;
        bg.raycastTarget = false;
        go.GetComponent<LayoutElement>().flexibleWidth = 1f;

        return CreateTmp(go.transform, "Text", value, 15, FontStyles.Bold, InventoryItemCardUI.TextColor);
    }

    static Button CreateBuyButton(Transform parent)
    {
        var go = new GameObject("BuyButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.28f, 0.62f, 0.42f, 1f);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 36f;
        le.flexibleWidth = 0f;
        le.minWidth = 34f;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        CreateTmp(go.transform, "Label", "+", 22, FontStyles.Bold, Color.white);
        return btn;
    }

    static TextMeshProUGUI CreateTmp(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch(go.transform as RectTransform, 2f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 10f;
        tmp.fontSizeMax = size;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static void Stretch(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }
}
