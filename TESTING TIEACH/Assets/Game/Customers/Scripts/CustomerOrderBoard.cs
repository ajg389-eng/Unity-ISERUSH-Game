using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Movable wall display with a compact live customer-order preview.</summary>
public class CustomerOrderBoard : MonoBehaviour
{
    TextMeshProUGUI wallText;
    float nextRefresh;
    readonly List<CustomerAI> customers = new List<CustomerAI>();
    readonly StringBuilder preview = new StringBuilder();

    void Awake()
    {
        if (GetComponent<WallMountedCutawayFollower>() == null)
            gameObject.AddComponent<WallMountedCutawayFollower>();
        var face = new GameObject("Order board face", typeof(RectTransform), typeof(Canvas));
        face.transform.SetParent(transform, false);
        face.transform.localPosition = new Vector3(0, 0, -0.51f);
        face.transform.localScale = new Vector3(1f / 900f, 1f / 450f, 1f);
        face.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 450);
        face.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var background = MakeRect("Background", face.transform, new Vector2(900, 450));
        background.gameObject.AddComponent<Image>().color = new Color(.07f, .11f, .14f);
        wallText = MakeText(background, 38);
        wallText.alignment = TextAlignmentOptions.Center;
        wallText.margin = new Vector4(20, 20, 20, 20);
        GameUITheme.ApplyToCanvas(face.GetComponent<Canvas>());
    }

    void Update()
    {
        if (WallPhotoDrag.IsDragging || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .5f;
        customers.Clear();
        foreach (var customer in CustomerAI.OrderBoardCustomers)
            if (customer != null && !customer.IsLeaving && !customer.IsOrderFullyDelivered)
                customers.Add(customer);
        customers.Sort((a, b) => a.OrderNumber.CompareTo(b.OrderNumber));

        preview.Clear();
        preview.Append("<b>CUSTOMER ORDERS</b>\n");
        preview.Append(customers.Count).Append(" waiting\n\n");
        for (int i = 0; i < Mathf.Min(3, customers.Count); i++)
            preview.Append(customers[i].CustomerDisplayName).Append("   #")
                .Append(customers[i].OrderNumber).Append('\n');
        if (customers.Count == 0) preview.Append("No open orders\n");
        preview.Append("\n<size=26>Drag in Build mode</size>");
        if (wallText.text != preview.ToString()) wallText.text = preview.ToString();
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        return rect;
    }

    static TextMeshProUGUI MakeText(Transform parent, float size)
    {
        var rect = MakeRect("Text", parent, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
