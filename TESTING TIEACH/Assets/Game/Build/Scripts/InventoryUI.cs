using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    public GameModeManager modeManager;
    public InventoryManager inventory;
    public GridManager grid;
    public MoneyManager money;

    [Header("Build Mode Button")]
    public Button inventoryButton;        // the "Inventory" button (only build mode)
    public GameObject panel;              // popup panel (menu)

    [Header("List")]
    public Transform contentParent;       // Vertical Layout Group content
    public GameObject rowPrefab;          // prefab for one row

    [Header("Grid Expansion")]
    [Tooltip("Cost to expand the kitchen floor by expandWidth x expandHeight cells.")]
    public int expandCost = 150;
    [Tooltip("Cells added along width (+X) per purchase.")]
    public int expandWidth = 1;
    [Tooltip("Cells added along depth (+Z) per purchase.")]
    public int expandHeight = 1;

    Button expandButton;
    TextMeshProUGUI expandLabel;
    TextMeshProUGUI expandStatusText;
    bool expandUiBuilt;

    void Start()
    {
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();

        if (inventoryButton)
            inventoryButton.onClick.AddListener(TogglePanel);

        EnsureExpandUi();
        EnsureUndoFooter();
        RefreshAll();
        ApplyModeState();
    }

    void Update()
    {
        // Inventory controls Build mode now: open panel => Build, closed => Play.
        ApplyModeState();
        RefreshExpandButton();
    }

    void ApplyModeState()
    {
        if (modeManager == null || panel == null) return;

        if (panel.activeSelf)
        {
            // Closing management if switching to build
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt != null && mgmt.IsOpen) mgmt.Close();
            modeManager.SetMode(GameModeManager.Mode.Build);
        }
        else if (modeManager.CurrentMode == GameModeManager.Mode.Build)
        {
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt == null || !mgmt.IsOpen)
                modeManager.SetMode(GameModeManager.Mode.Play);
        }
    }

    public void TogglePanel()
    {
        if (!panel) return;
        bool opening = !panel.activeSelf;
        panel.SetActive(opening);
        Sfx.Play(opening ? SfxId.UiOpen : SfxId.UiClose);
        if (opening)
        {
            var mgmt = FindObjectOfType<ManagementScreenController>();
            if (mgmt != null && mgmt.IsOpen)
                mgmt.Close();
            EnsureExpandUi();
            EnsureUndoFooter();
            RefreshAll();
            RefreshExpandButton();
        }
    }

    public bool IsPanelOpen => panel != null && panel.activeSelf;

    public void RefreshAll()
    {
        if (!inventory || !contentParent || !rowPrefab) return;

        // clear old rows (keep expand footer if it lives under contentParent)
        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            var child = contentParent.GetChild(i);
            if (child != null && child.name == "ExpandFloorRow") continue;
            Destroy(child.gameObject);
        }

        foreach (var item in inventory.allItems)
        {
            if (item == null) continue;

            var row = Instantiate(rowPrefab, contentParent);

            // Row references (updated for your hierarchy)
            var nameButton = row.transform.Find("NameButton").GetComponent<Button>();

            // Get TMP text inside NameButton (works even if it's named "Text (TMP)")
            var nameText = row.transform.Find("NameButton").GetComponentInChildren<TextMeshProUGUI>(true);

            var qtyText = row.transform.Find("QtyBack/QtyText").GetComponent<TextMeshProUGUI>();

            var buyButton = row.transform.Find("BuyButton").GetComponent<Button>();

            var priceText = row.transform.Find("PriceBack/Price").GetComponent<TextMeshProUGUI>();

            nameText.text = item.itemName;
            qtyText.text = inventory.GetCount(item).ToString();
            priceText.text = "$" + item.price;

            nameButton.onClick.AddListener(() =>
            {
                inventory.SelectItem(item);


                // Tell placer to begin placement
                var placer = FindObjectOfType<BuildPlacer>();
                if (placer) placer.BeginPlacement(item);
            });

            buyButton.onClick.AddListener(() =>
            {
                bool bought = inventory.PurchaseOne(item);
                if (bought)
                {
                    qtyText.text = inventory.GetCount(item).ToString();
                    Sfx.Play(SfxId.Purchase);
                }
                else
                    Sfx.Play(SfxId.UiError);
            });
        }

        // Keep expand controls at the bottom of the list
        if (expandButton != null)
            expandButton.transform.parent.SetAsLastSibling();
    }

    void EnsureExpandUi()
    {
        if (expandUiBuilt && expandButton != null) return;

        // Prefer the scroll list so the control scrolls with items; else panel footer
        Transform parent = contentParent != null ? contentParent : panel != null ? panel.transform : null;
        if (parent == null) return;

        Transform existing = parent.Find("ExpandFloorRow") ?? parent.Find("ExpandFloorBar");

        GameObject barGo;
        if (existing != null)
        {
            barGo = existing.gameObject;
        }
        else
        {
            barGo = new GameObject("ExpandFloorRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            barGo.transform.SetParent(parent, false);
            barGo.transform.SetAsLastSibling();

            var bg = barGo.GetComponent<Image>();
            bg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
            bg.raycastTarget = true;

            var le = barGo.GetComponent<LayoutElement>();
            le.minHeight = 72f;
            le.preferredHeight = 72f;

            var layout = barGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        expandStatusText = barGo.transform.Find("Status")?.GetComponent<TextMeshProUGUI>();
        if (expandStatusText == null)
        {
            var statusGo = new GameObject("Status", typeof(RectTransform));
            statusGo.transform.SetParent(barGo.transform, false);
            expandStatusText = statusGo.AddComponent<TextMeshProUGUI>();
            expandStatusText.fontSize = 13;
            expandStatusText.alignment = TextAlignmentOptions.Center;
            expandStatusText.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            if (TMP_Settings.defaultFontAsset != null)
                expandStatusText.font = TMP_Settings.defaultFontAsset;
            var statusLe = statusGo.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 18f;
        }

        expandButton = barGo.transform.Find("ExpandButton")?.GetComponent<Button>();
        if (expandButton == null)
        {
            var btnGo = new GameObject("ExpandButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(barGo.transform, false);
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = new Color(0.22f, 0.55f, 0.38f, 1f);
            expandButton = btnGo.GetComponent<Button>();
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 34f;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            expandLabel = labelGo.AddComponent<TextMeshProUGUI>();
            expandLabel.fontSize = 15;
            expandLabel.alignment = TextAlignmentOptions.Center;
            expandLabel.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                expandLabel.font = TMP_Settings.defaultFontAsset;
        }
        else
        {
            expandLabel = expandButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        expandButton.onClick.RemoveListener(OnExpandClicked);
        expandButton.onClick.AddListener(OnExpandClicked);
        expandUiBuilt = true;
        RefreshExpandButton();
        EnsureUndoFooter();
    }

    void EnsureUndoFooter()
    {
        if (panel == null) return;

        var scroll = panel.transform.Find("Scroll View") as RectTransform;
        if (scroll != null)
        {
            scroll.offsetMin = new Vector2(scroll.offsetMin.x, 52f);
            PurchaseUndoFooter.EnsureMatching(scroll);
            return;
        }

        PurchaseUndoFooter.EnsureOnPanel(panel.transform);
    }

    void RefreshExpandButton()
    {
        if (!expandUiBuilt) return;
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();

        int w = grid != null ? grid.Width : 0;
        int h = grid != null ? grid.Height : 0;
        bool canSize = grid != null && grid.CanExpand(expandWidth, expandHeight);
        bool canPay = money != null && money.CanAfford(expandCost);
        bool can = canSize && canPay;

        if (expandStatusText != null)
        {
            if (grid == null)
                expandStatusText.text = "Grid: —";
            else if (!canSize)
                expandStatusText.text = $"Floor {w}×{h} (max reached)";
            else
                expandStatusText.text = $"Floor {w}×{h}  →  {w + expandWidth}×{h + expandHeight}";
        }

        if (expandLabel != null)
            expandLabel.text = canSize ? $"Expand Floor  ${expandCost}" : "Floor Maxed";

        if (expandButton != null)
            expandButton.interactable = can;
    }

    void OnExpandClicked()
    {
        if (grid == null) grid = GridManager.Instance != null ? GridManager.Instance : FindObjectOfType<GridManager>();
        if (money == null) money = FindObjectOfType<MoneyManager>();
        if (grid == null || money == null) return;

        if (!grid.CanExpand(expandWidth, expandHeight))
        {
            RefreshExpandButton();
            return;
        }
        if (!money.TrySpend(expandCost))
        {
            RefreshExpandButton();
            Sfx.Play(SfxId.UiError);
            return;
        }
        Sfx.Play(SfxId.SpendMoney);

        if (!grid.TryExpand(expandWidth, expandHeight))
        {
            money.AddMoney(expandCost);
            Sfx.Play(SfxId.UiError);
        }
        else
        {
            Sfx.Play(SfxId.GridExpand);
            var undo = PurchaseUndoManager.Ensure();
            if (undo != null)
                undo.RecordFloorExpand(expandWidth, expandHeight, expandCost);
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.FloorExpanded);
            TutorialVoiceEvents.Raise(TutorialVoiceEventId.CapacityInvested);
        }

        RefreshExpandButton();
    }
}
