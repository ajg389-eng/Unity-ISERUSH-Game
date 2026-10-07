using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Brief top-of-screen notice when an ingredient delivery is handed over.
/// Only one banner exists at a time.
/// </summary>
public class DeliveryArrivedNotice : MonoBehaviour
{
    const float Lifetime = 3.8f;
    static DeliveryArrivedNotice instance;

    CanvasGroup group;
    TextMeshProUGUI bodyLabel;
    float age;

    public static void Show(IngredientDeliveryService.Shipment shipment)
    {
        string body = FormatBody(shipment);
        NotificationCenter.Post("Ingredient delivery arrived: " + FormatDeliveryDetails(shipment),
            GameNotificationKind.Message, "ingredient-delivery", 2f);
        DeliveryArrivedNotice live = instance;
        if (live == null)
            live = FindFirstObjectByType<DeliveryArrivedNotice>();

        if (live != null)
        {
            live.SetBody(body);
            live.age = 0f;
            if (live.group != null)
                live.group.alpha = 1f;
            live.PlaceUnderSpeedControls();
            return;
        }

        DeliveryArrivedNotice[] leftovers = FindObjectsByType<DeliveryArrivedNotice>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < leftovers.Length; i++)
        {
            if (leftovers[i] != null)
                DestroyImmediate(leftovers[i].gameObject);
        }

        Canvas hostCanvas = FindHudCanvas();
        Transform parent = hostCanvas != null ? hostCanvas.transform : null;
        var root = new GameObject("DeliveryArrivedNotice", typeof(RectTransform));
        if (parent != null)
            root.transform.SetParent(parent, false);
        else
        {
            root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;
        }

        var notice = root.AddComponent<DeliveryArrivedNotice>();
        instance = notice;
        notice.Build(body);
        notice.PlaceUnderSpeedControls();
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    static Canvas FindHudCanvas()
    {
        var bar = FindFirstObjectByType<TopHudBar>(FindObjectsInactive.Include);
        if (bar != null)
        {
            var canvas = bar.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.rootCanvas;
        }

        var named = GameObject.Find("PlayerUI");
        if (named != null)
        {
            var canvas = named.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }

        return null;
    }

    void PlaceUnderSpeedControls()
    {
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        float availableWidth = Screen.width / scale - 32f;
        rt.sizeDelta = new Vector2(Mathf.Max(220f, Mathf.Min(680f, availableWidth)), 92f);

        float y = -118f;
        var bar = FindFirstObjectByType<TopHudBar>(FindObjectsInactive.Include);
        RectTransform strip = null;
        if (bar != null)
        {
            Transform host = bar.transform.parent != null ? bar.transform.parent : bar.transform;
            strip = host.Find("SpeedControls") as RectTransform;
            if (strip == null)
                strip = bar.transform.Find("SpeedControls") as RectTransform;
        }

        if (strip != null)
            y = strip.anchoredPosition.y - strip.rect.height - 12f;
        else if (bar != null)
        {
            var barRt = (RectTransform)bar.transform;
            y = barRt.anchoredPosition.y - barRt.rect.height - 56f;
        }

        rt.anchoredPosition = new Vector2(0f, y);
        rt.SetAsLastSibling();
    }

    static string FormatBody(IngredientDeliveryService.Shipment shipment)
    {
        if (shipment == null || shipment.packs.Count == 0)
            return "Ingredients are now in stock.";

        var itemTypes = new HashSet<ItemDefinition>();
        int totalUnits = 0;
        for (int i = 0; i < shipment.packs.Count; i++)
        {
            IngredientDeliveryService.Pack pack = shipment.packs[i];
            if (pack == null || pack.item == null) continue;
            itemTypes.Add(pack.item);
            totalUnits += Mathf.Max(0, pack.amount);
        }

        if (itemTypes.Count == 0)
            return "Ingredients are now in stock.";

        return itemTypes.Count + (itemTypes.Count == 1 ? " ingredient type restocked" : " ingredient types restocked")
            + "  ·  " + totalUnits + (totalUnits == 1 ? " unit now in stock" : " units now in stock");
    }

    static string FormatDeliveryDetails(IngredientDeliveryService.Shipment shipment)
    {
        if (shipment == null || shipment.packs.Count == 0)
            return "Ingredients are now in stock.";

        var amounts = new Dictionary<string, int>();
        var order = new List<string>();
        for (int i = 0; i < shipment.packs.Count; i++)
        {
            IngredientDeliveryService.Pack pack = shipment.packs[i];
            if (pack == null || pack.item == null) continue;
            string name = string.IsNullOrEmpty(pack.item.itemName) ? pack.item.name : pack.item.itemName;
            if (!amounts.ContainsKey(name))
            {
                amounts[name] = 0;
                order.Add(name);
            }
            amounts[name] += pack.amount;
        }

        if (order.Count == 0)
            return "Ingredients are now in stock.";

        var parts = new List<string>();
        for (int i = 0; i < order.Count; i++)
            parts.Add(order[i] + "  ×" + amounts[order[i]]);
        return string.Join("   ·   ", parts);
    }

    void SetBody(string body)
    {
        if (bodyLabel != null)
            bodyLabel.text = body;
    }

    void Build(string body)
    {
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0.035f, 0.09f, 0.075f, 0.98f);
        bg.raycastTarget = false;
        var outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.38f, 0.78f, 0.57f, 1f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 9, 9);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        group = panel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        titleGo.transform.SetParent(panel.transform, false);
        var title = titleGo.GetComponent<TextMeshProUGUI>();
        title.text = "DELIVERY ARRIVED";
        title.fontSize = 20f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.55f, 0.95f, 0.69f, 1f);
        title.raycastTarget = false;
        title.enableWordWrapping = false;
        titleGo.GetComponent<LayoutElement>().preferredHeight = 27f;
        titleGo.GetComponent<LayoutElement>().minHeight = 27f;

        var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        bodyGo.transform.SetParent(panel.transform, false);
        bodyLabel = bodyGo.GetComponent<TextMeshProUGUI>();
        bodyLabel.text = body;
        bodyLabel.fontSize = 16f;
        bodyLabel.alignment = TextAlignmentOptions.Center;
        bodyLabel.color = Color.white;
        bodyLabel.raycastTarget = false;
        bodyLabel.enableWordWrapping = true;
        bodyLabel.lineSpacing = 2f;
        var bodyLe = bodyGo.GetComponent<LayoutElement>();
        bodyLe.minHeight = 24f;
        bodyLe.preferredHeight = 32f;
        bodyLe.flexibleHeight = 1f;
    }

    void Update()
    {
        age += Time.unscaledDeltaTime;
        if (group != null)
        {
            if (age < 0.18f)
                group.alpha = age / 0.18f;
            else if (age > Lifetime - 0.4f)
                group.alpha = Mathf.Clamp01((Lifetime - age) / 0.4f);
            else
                group.alpha = 1f;
        }

        if (age >= Lifetime)
            Destroy(gameObject);
    }
}
