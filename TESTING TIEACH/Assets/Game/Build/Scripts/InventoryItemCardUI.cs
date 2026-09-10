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

    public void Bind(
        ItemDefinition item,
        int quantity,
        System.Action onSelect,
        System.Action onBuy)
    {
        if (item == null) return;

        if (nameText != null)
            nameText.text = item.itemName;

        if (qtyText != null)
            qtyText.text = quantity.ToString();

        if (priceText != null)
            priceText.text = "$" + item.price;

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
            }
        }
    }
}
