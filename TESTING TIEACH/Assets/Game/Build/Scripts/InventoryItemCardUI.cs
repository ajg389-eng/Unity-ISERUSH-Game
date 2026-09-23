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
    bool tutorialLocked;
    Image cardImage;
    Color cardBaseColor = new Color(0.92f, 0.93f, 0.95f, 1f);
    Outline cardOutline;
    TextMeshProUGUI tutorialPrompt;
    ItemDefinition boundItem;

    public void Bind(
        ItemDefinition item,
        int quantity,
        System.Action onSelect,
        System.Action onBuy,
        int? displayPrice = null)
    {
        if (item == null) return;
        boundItem = item;

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
            buyButton.interactable = !atCapacity && !tutorialLocked;
        if (atCapacity && priceText != null)
            priceText.text = "MAX";
    }

    public void SetTutorialLocked(bool locked)
    {
        tutorialLocked = locked;
        if (!locked) return; // Price and capacity are set before this method.
        if (priceText != null) priceText.text = "LOCKED";
        if (buyButton != null) buyButton.interactable = false;
        if (selectButtons != null)
            foreach (var button in selectButtons)
                if (button != null) button.interactable = false;
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

    public void SetTutorialHighlight(bool on, int available = 0, int owned = 0)
    {
        tutorialHighlight = on;
        if (on)
        {
            EnsureTutorialPrompt();
            string placementPrompt = boundItem != null && boundItem.placementSurface == ItemDefinition.PlacementSurface.Counter
                ? "PLACE ON COUNTER"
                : boundItem != null && boundItem.placementSurface == ItemDefinition.PlacementSurface.CustomerWall
                    ? "PLACE ON A LOBBY WALL" : "PLACE ON A FLOOR TILE";
            tutorialPrompt.text = available > 0 ? placementPrompt : owned > 0 ? "PLACED - CLICK NEXT" : "BUY THIS  (+)";
        }
        if (tutorialPrompt != null)
            tutorialPrompt.transform.parent.gameObject.SetActive(on);
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
            cardOutline.effectDistance = new Vector2(8f, -8f);
            cardOutline.effectColor = new Color(1f, 0.82f, 0.15f, 0.95f);
        }
    }

    void EnsureTutorialPrompt()
    {
        if (tutorialPrompt != null) return;
        // Overlay the preview without shrinking the image or blocking its button.
        Transform parent = previewRawImage != null ? previewRawImage.transform.parent
            : previewImage != null ? previewImage.transform.parent : transform;
        var banner = new GameObject("TutorialPrompt", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        banner.transform.SetParent(parent, false);
        banner.GetComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)banner.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, 32f);
        var background = banner.GetComponent<Image>();
        background.color = new Color(1f, 0.82f, 0.15f, 1f);
        background.raycastTarget = false;

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(banner.transform, false);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 0f);
        labelRect.offsetMax = new Vector2(-4f, 0f);
        tutorialPrompt = label.GetComponent<TextMeshProUGUI>();
        if (nameText != null) tutorialPrompt.font = nameText.font;
        tutorialPrompt.fontSize = 16f;
        tutorialPrompt.enableAutoSizing = true;
        tutorialPrompt.fontSizeMin = 9f;
        tutorialPrompt.fontSizeMax = 16f;
        tutorialPrompt.fontStyle = FontStyles.Bold;
        tutorialPrompt.alignment = TextAlignmentOptions.Center;
        tutorialPrompt.textWrappingMode = TextWrappingModes.NoWrap;
        tutorialPrompt.color = new Color(0.12f, 0.1f, 0.04f, 1f);
        tutorialPrompt.raycastTarget = false;
    }

    void Update()
    {
        if (!tutorialHighlight || cardImage == null) return;
        float pulse = 0.75f + Mathf.Sin(Time.unscaledTime * 3f) * 0.2f;
        cardImage.color = Color.Lerp(cardBaseColor, new Color(1f, 0.92f, 0.35f, 1f), pulse);
        if (cardOutline != null)
            cardOutline.effectColor = new Color(1f, 0.75f, 0.1f, pulse);
    }
}
