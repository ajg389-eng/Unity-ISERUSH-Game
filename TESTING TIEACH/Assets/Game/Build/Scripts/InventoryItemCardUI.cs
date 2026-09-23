using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Square inventory shop card: name, preview, qty / price / buy.
/// </summary>
public class InventoryItemCardUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public Image previewImage;
    public RawImage previewRawImage;
    public TextMeshProUGUI qtyText;
    public TextMeshProUGUI priceText;
    public Button buyButton;
    public Button[] selectButtons;

    bool tutorialHighlight;
    Image cardImage;
    Color cardBaseColor = new Color(0.92f, 0.93f, 0.95f, 1f);
    Outline cardOutline;

    public void Bind(
        ItemDefinition item,
        int quantity,
        System.Action onSelect,
        System.Action onBuy,
        int? displayPrice = null)
    {
        if (item == null) return;

        if (nameText != null)
            nameText.text = item.itemName;

        if (qtyText != null)
            qtyText.text = quantity.ToString();

        SetPrice(displayPrice ?? item.price);

        ApplyPreview(item);

        if (selectButtons != null)
        {
            foreach (var btn in selectButtons)
            {
                if (btn == null) continue;
                btn.onClick.RemoveAllListeners();
                if (onSelect != null)
                    btn.onClick.AddListener(() => onSelect());
            }
        }

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            if (onBuy != null)
                buyButton.onClick.AddListener(() => onBuy());
        }
    }

    public void SetQuantity(int quantity)
    {
        if (qtyText != null)
            qtyText.text = quantity.ToString();
    }

    public void SetPrice(int price)
    {
        if (priceText == null) return;
        priceText.text = price <= 0 ? "FREE" : "$" + price;
    }

    public void SetOwnedCapacity(int owned, int capacity)
    {
        capacity = Mathf.Max(1, capacity);
        bool atCapacity = owned >= capacity;
        if (qtyText != null)
            qtyText.text = Mathf.Max(0, owned) + "/" + capacity;
        if (buyButton != null)
            buyButton.interactable = !atCapacity;
        if (atCapacity && priceText != null)
            priceText.text = MilestoneFeatures.ExtraEquipmentUnlocked ? "MAX" : "MILESTONE 2";
    }

    void ApplyPreview(ItemDefinition item)
    {
        bool hasSprite = item.previewIcon != null;
        if (previewImage != null)
        {
            previewImage.enabled = hasSprite;
            previewImage.sprite = item.previewIcon;
            previewImage.preserveAspect = true;
            if (hasSprite)
                previewImage.color = Color.white;
        }

        if (previewRawImage != null)
        {
            if (hasSprite)
            {
                previewRawImage.enabled = false;
                previewRawImage.texture = null;
            }
            else
            {
                var tex = ItemPreviewThumbnails.Get(item);
                previewRawImage.enabled = tex != null;
                previewRawImage.texture = tex;
                previewRawImage.color = Color.white;
                EnsurePreviewAspect(previewRawImage);
            }
        }
    }

    static void EnsurePreviewAspect(RawImage raw)
    {
        if (raw == null) return;
        var fitter = raw.GetComponent<AspectRatioFitter>();
        if (fitter == null)
            fitter = raw.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        if (raw.texture != null && raw.texture.height > 0)
            fitter.aspectRatio = (float)raw.texture.width / raw.texture.height;
        else
            fitter.aspectRatio = 1f;
    }

    public void SetTutorialHighlight(bool on)
    {
        tutorialHighlight = on;
        if (cardImage == null)
        {
            cardImage = GetComponent<Image>();
            if (cardImage != null)
                cardBaseColor = cardImage.color;
        }

        if (cardImage != null && cardOutline == null)
        {
            cardOutline = cardImage.GetComponent<Outline>();
            if (cardOutline == null)
                cardOutline = cardImage.gameObject.AddComponent<Outline>();
        }

        if (!on)
        {
            if (cardImage != null)
                cardImage.color = cardBaseColor;
            if (cardOutline != null)
            {
                cardOutline.enabled = false;
                cardOutline.effectColor = Color.clear;
            }
            return;
        }

        if (cardOutline != null)
        {
            cardOutline.enabled = true;
            cardOutline.effectDistance = new Vector2(6f, -6f);
            cardOutline.effectColor = new Color(1f, 0.82f, 0.15f, 0.95f);
        }
    }

    void Update()
    {
        if (!tutorialHighlight || cardImage == null) return;
        float pulse = 0.55f + Mathf.PingPong(Time.unscaledTime * 2.2f, 0.45f);
        cardImage.color = Color.Lerp(cardBaseColor, new Color(1f, 0.92f, 0.35f, 1f), pulse);
        if (cardOutline != null)
            cardOutline.effectColor = new Color(1f, 0.75f, 0.1f, pulse);
    }
}
