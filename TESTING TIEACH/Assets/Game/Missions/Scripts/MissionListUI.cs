using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Right-side task / mission list. Always visible during play.
/// </summary>
public class MissionListUI : MonoBehaviour
{
    [Header("References (auto-built if empty)")]
    public Canvas targetCanvas;
    public GameObject panelRoot;
    public TextMeshProUGUI headerText;
    public Transform listContent;

    [Header("Layout")]
    public Vector2 panelSize = new Vector2(300f, 420f);
    [Tooltip("Offset from the right edge (X is negative = inset from right).")]
    public Vector2 screenOffset = new Vector2(-24f, 0f);
    public string headerLabel = "Tasks";

    [Header("Colors")]
    public Color panelColor = new Color(0.08f, 0.1f, 0.14f, 0.88f);
    public Color currentMissionColor = new Color(1f, 0.92f, 0.55f, 1f);
    public Color completeMissionColor = new Color(0.55f, 0.85f, 0.6f, 1f);
    public Color pendingMissionColor = new Color(0.78f, 0.82f, 0.88f, 1f);

    MissionProgressManager progress;

    readonly List<GameObject> rowPool = new List<GameObject>();
    bool built;

    void Start()
    {
        if (progress == null)
            progress = MissionProgressManager.Instance != null
                ? MissionProgressManager.Instance
                : FindObjectOfType<MissionProgressManager>();

        EnsureUI();

        if (progress != null)
        {
            progress.OnMissionsChanged += RefreshList;
            RefreshList();
        }

        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    void OnDestroy()
    {
        if (progress != null)
            progress.OnMissionsChanged -= RefreshList;
    }

    void OnValidate()
    {
        if (built && panelRoot != null)
            ApplyLayout();
    }

    void EnsureUI()
    {
        if (targetCanvas == null)
            targetCanvas = GetComponentInChildren<Canvas>(true);

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject("MissionListCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (panelRoot == null)
        {
            panelRoot = new GameObject("MissionListPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelRoot.transform.SetParent(targetCanvas.transform, false);

            var bg = panelRoot.GetComponent<Image>();
            bg.color = panelColor;
            bg.raycastTarget = false;

            var layout = panelRoot.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panelRoot.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(panelRoot.transform, false);
            headerText = headerGo.AddComponent<TextMeshProUGUI>();
            headerText.text = headerLabel;
            headerText.fontSize = 20;
            headerText.fontStyle = FontStyles.Bold;
            headerText.color = Color.white;
            headerText.alignment = TextAlignmentOptions.Left;
            if (TMP_Settings.defaultFontAsset != null)
                headerText.font = TMP_Settings.defaultFontAsset;
            var headerLe = headerGo.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 28f;

            var listGo = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup));
            listGo.transform.SetParent(panelRoot.transform, false);
            listContent = listGo.transform;
            var listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childAlignment = TextAnchor.UpperLeft;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            var listLe = listGo.AddComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
        }

        built = true;
        ApplyLayout();

        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    void ApplyLayout()
    {
        if (panelRoot == null) return;

        var rt = (RectTransform)panelRoot.transform;
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = screenOffset;
        rt.sizeDelta = panelSize;

        var bg = panelRoot.GetComponent<Image>();
        if (bg != null)
            bg.color = panelColor;

        if (headerText != null)
            headerText.text = headerLabel;
    }

    void RefreshList()
    {
        EnsureUI();
        if (listContent == null || progress == null) return;

        foreach (var row in rowPool)
        {
            if (row != null) Destroy(row);
        }
        rowPool.Clear();

        var missions = progress.GetVisibleMissions();
        var current = progress.GetCurrentMission();

        if (missions.Count == 0)
        {
            CreateRow("—", "No tasks yet", pendingMissionColor, false);
            return;
        }

        foreach (var mission in missions)
        {
            if (mission == null) continue;
            bool done = progress.IsComplete(mission);
            bool isCurrent = !done && mission == current;
            Color color = done ? completeMissionColor : (isCurrent ? currentMissionColor : pendingMissionColor);
            string prefix = done ? "✓ " : (isCurrent ? "► " : "○ ");
            CreateRow(prefix + mission.title, mission.description, color, isCurrent);
        }
    }

    void CreateRow(string title, string description, Color color, bool emphasize)
    {
        var rowGo = new GameObject("MissionRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        rowGo.transform.SetParent(listContent, false);
        rowPool.Add(rowGo);

        var rowLe = rowGo.GetComponent<LayoutElement>();
        rowLe.minHeight = 44f;

        var rowLayout = rowGo.GetComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(rowGo.transform, false);
        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = title;
        titleTmp.fontSize = emphasize ? 17 : 15;
        titleTmp.fontStyle = emphasize ? FontStyles.Bold : FontStyles.Normal;
        titleTmp.color = color;
        titleTmp.alignment = TextAlignmentOptions.Left;
        if (TMP_Settings.defaultFontAsset != null)
            titleTmp.font = TMP_Settings.defaultFontAsset;

        if (!string.IsNullOrEmpty(description))
        {
            var descGo = new GameObject("Description", typeof(RectTransform));
            descGo.transform.SetParent(rowGo.transform, false);
            var descTmp = descGo.AddComponent<TextMeshProUGUI>();
            descTmp.text = description;
            descTmp.fontSize = 12;
            descTmp.color = new Color(color.r, color.g, color.b, 0.85f);
            descTmp.alignment = TextAlignmentOptions.Left;
            descTmp.textWrappingMode = TextWrappingModes.Normal;
            if (TMP_Settings.defaultFontAsset != null)
                descTmp.font = TMP_Settings.defaultFontAsset;
        }
    }
}
