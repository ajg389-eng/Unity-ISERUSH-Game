using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Square inventory shop card: name, preview, qty / price / buy.
/// </summary>
public class InventoryItemCardUI : MonoBehaviour
{
    public static readonly Color CardColor = GameUITheme.Panel;
    public static readonly Color ChipColor = GameUITheme.Surface;
    public static readonly Color TextColor = GameUITheme.Cream;

    public TextMeshProUGUI nameText;
    public Image previewImage;
    public RawImage previewRawImage;
    public TextMeshProUGUI qtyText;
    public TextMeshProUGUI priceText;
    public Button buyButton;
    public Button[] selectButtons;
    public UnityEngine.UI.Button mark1Button;
    public UnityEngine.UI.Button mark2Button;

    bool tutorialHighlight;
    bool tutorialLocked;
    Image cardImage;
    Color cardBaseColor = CardColor;
    Outline cardOutline;
    TextMeshProUGUI tutorialPrompt;
    ItemDefinition boundItem;

    void Awake()
    {
        ApplyCompactLayout();
        ApplyPalette();
    }

    void ApplyCompactLayout()
    {
        var vertical = GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (vertical != null)
        {
            vertical.padding = new RectOffset(6, 6, 6, 6);
            vertical.spacing = 4f;
        }

        ConfigureResponsiveText(nameText, 10f, 16f);
        ConfigureResponsiveText(qtyText, 10f, 14f);
        ConfigureResponsiveText(priceText, 9f, 14f);

        Transform footer = qtyText != null && qtyText.transform.parent != null
            ? qtyText.transform.parent.parent : null;
        var footerLayout = footer != null
            ? footer.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() : null;
        if (footerLayout != null)
            footerLayout.spacing = 3f;

        var buyLayout = buyButton != null
            ? buyButton.GetComponent<UnityEngine.UI.LayoutElement>() : null;
        if (buyLayout != null)
        {
            buyLayout.minWidth = 34f;
            buyLayout.preferredWidth = 36f;
            buyLayout.flexibleWidth = 0f;
        }
    }

    static void ConfigureResponsiveText(TextMeshProUGUI text, float minimum, float maximum)
    {
        if (text == null) return;
        text.enableAutoSizing = true;
        text.fontSizeMin = minimum;
        text.fontSizeMax = maximum;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    public void Bind(
        ItemDefinition item,
        int quantity,
        System.Action onSelect,
        System.Action onBuy,
        int? displayPrice = null)
    {
        if (item == null) return;
        ApplyCompactLayout();
        ApplyPalette();
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

    void ApplyPalette()
    {
        cardImage = GetComponent<Image>();
        if (cardImage != null)
        {
            cardImage.color = CardColor;
            cardBaseColor = CardColor;
        }

        SetTextAndParentColor(nameText, TextColor, CardColor);
        SetTextAndParentColor(qtyText, TextColor, ChipColor);
        SetTextAndParentColor(priceText, TextColor, ChipColor);

        Transform preview = previewRawImage != null ? previewRawImage.transform.parent
            : previewImage != null ? previewImage.transform.parent : null;
        if (preview != null && preview.TryGetComponent(out Image previewBackground))
            previewBackground.color = ItemPreviewThumbnails.BackgroundColor;

        if (buyButton != null)
        {
            var label = buyButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.color = Color.white;
        }
    }

    static void SetTextAndParentColor(TextMeshProUGUI text, Color textColor, Color backgroundColor)
    {
        if (text == null) return;
        text.color = textColor;
        if (text.transform.parent != null && text.transform.parent.TryGetComponent(out Image background))
            background.color = backgroundColor;
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
        if (!locked)
        {
            if (selectButtons != null)
                foreach (var button in selectButtons)
                    if (button != null) button.interactable = true;
            return;
        }
        if (priceText != null) priceText.text = "LOCKED";
        if (buyButton != null) buyButton.interactable = false;
        if (selectButtons != null)
            foreach (var button in selectButtons)
                if (button != null) button.interactable = false;
    }

    public void SetDisplayName(string displayName)
    {
        if (nameText != null) nameText.text = displayName;
    }

    public void ConfigureMarkSelector(bool hasMk1, bool hasMk2, int selectedMark,
        System.Action<int> onSelected)
    {
        EnsureMarkSelector();
        Transform row = mark1Button != null ? mark1Button.transform.parent : null;
        if (row != null) row.gameObject.SetActive(true);
        ConfigureMarkButton(mark1Button, 1, hasMk1, selectedMark, onSelected);
        ConfigureMarkButton(mark2Button, 2, hasMk2, selectedMark, onSelected);
    }

    public void HideMarkSelector()
    {
        Transform row = mark1Button != null ? mark1Button.transform.parent : null;
        if (row != null) row.gameObject.SetActive(false);
    }

    void EnsureMarkSelector()
    {
        if (mark1Button != null && mark2Button != null) return;
        var row = new GameObject("MarkSelector", typeof(RectTransform),
            typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
        row.transform.SetParent(transform, false);
        row.transform.SetSiblingIndex(Mathf.Min(1, transform.childCount - 1));
        row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 24f;
        var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        mark1Button = CreateMarkButton(row.transform, "MK1");
        mark2Button = CreateMarkButton(row.transform, "MK2");
    }

    static UnityEngine.UI.Button CreateMarkButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        go.transform.SetParent(parent, false);
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(go.transform, false);
        var rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 13f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = TextColor;
        text.raycastTarget = false;
        return go.GetComponent<UnityEngine.UI.Button>();
    }

    static void ConfigureMarkButton(UnityEngine.UI.Button button, int mark, bool exists,
        int selectedMark, System.Action<int> onSelected)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.interactable = exists;
        if (exists && onSelected != null)
            button.onClick.AddListener(() => onSelected(mark));
        var image = button.GetComponent<UnityEngine.UI.Image>();
        if (image != null)
            image.color = mark == selectedMark
                ? new Color(0.34f, 0.63f, 0.51f, 1f)
                : new Color(0.15f, 0.19f, 0.23f, exists ? 1f : 0.45f);
        var outline = button.GetComponent<UnityEngine.UI.Outline>();
        if (outline == null) outline = button.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.enabled = mark == selectedMark;
        outline.effectColor = new Color(0.72f, 0.95f, 0.84f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
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
