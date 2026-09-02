using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GameModeManager : MonoBehaviour
{
    public enum Mode { Build, Play, Manage }
    public Mode CurrentMode { get; private set; } = Mode.Play;

    [Header("UI")]
    public TextMeshProUGUI modeText;

    void Start()
    {
        ConfigureCanvasScaling();
        CurrentMode = Mode.Play;
        UpdateUI();
    }

    void ConfigureCanvasScaling()
    {
        var canvases = FindObjectsOfType<Canvas>(true);
        if (canvases == null) return;
        foreach (var c in canvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = c.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    /// <summary>Legacy hook (old toggle button). Kept to avoid missing reference errors.</summary>
    public void ToggleMode()
    {
        UpdateUI();
    }

    public void SetMode(Mode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        UpdateUI();
    }

    void UpdateUI()
    {
        if (modeText) modeText.gameObject.SetActive(false);
    }
}
