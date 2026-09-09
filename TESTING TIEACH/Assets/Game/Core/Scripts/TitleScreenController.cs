using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a title screen at game start. Pauses the game until Play is clicked.
/// Assign a camera that views the outside of the restaurant as the background.
/// </summary>
public class TitleScreenController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Full-screen panel with title and buttons (shown at start)")]
    public GameObject titlePanel;
    public Button playButton;
    public Button settingsButton;
    public Button exitButton;

    [Header("Cameras")]
    [Tooltip("Camera showing the restaurant exterior; active while title is visible")]
    public Camera titleCamera;
    [Tooltip("Gameplay camera; active after Play. If unset, uses Camera.main.")]
    public Camera gameCamera;

    [Header("In-game UI (hide while on title screen)")]
    [Tooltip("Canvas or parent of all game UI (HUD, build menu, etc.). Hidden until Play is clicked.")]
    public GameObject inGameUIRoot;

    [Header("Settings (optional)")]
    [Tooltip("Panel to open when Settings is clicked (e.g. volume). Leave empty to do nothing.")]
    public GameObject settingsPanel;

    bool showingTitle = true;

    void Start()
    {
        if (titlePanel != null)
            titlePanel.SetActive(true);

        if (inGameUIRoot == null)
        {
            var myCanvas = GetComponentInParent<Canvas>();
            var all = FindObjectsOfType<Canvas>();
            foreach (var c in all)
            {
                if (c != null && c.gameObject != myCanvas?.gameObject)
                {
                    inGameUIRoot = c.gameObject;
                    break;
                }
            }
        }
        if (inGameUIRoot != null)
            inGameUIRoot.SetActive(false);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.RequestExternalPause(GameTimeManager.PauseTitleScreen);
        else
            Time.timeScale = 0f;

        if (titleCamera != null) titleCamera.enabled = true;
        if (gameCamera != null) gameCamera.enabled = false;
        else if (Camera.main != null && Camera.main != titleCamera)
            Camera.main.enabled = false;

        if (playButton != null)
            playButton.onClick.AddListener(OnPlay);
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettings);
        if (exitButton != null)
            exitButton.onClick.AddListener(OnExit);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    void OnPlay()
    {
        if (!showingTitle) return;
        showingTitle = false;

        if (titlePanel != null)
            titlePanel.SetActive(false);

        if (inGameUIRoot != null)
            inGameUIRoot.SetActive(true);

        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.ReleaseExternalPause(GameTimeManager.PauseTitleScreen);
        else
            Time.timeScale = 1f;

        if (titleCamera != null) titleCamera.enabled = false;
        if (gameCamera != null)
            gameCamera.enabled = true;
        else if (Camera.main != null)
            Camera.main.enabled = true;

        TutorialVoiceEvents.Raise(TutorialVoiceEventId.ShiftStarted);
        Sfx.Play(SfxId.UiOpen);

        if (StoreStatisticsManager.Instance != null)
            StoreStatisticsManager.Instance.BeginDay();
    }

    void OnSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public bool IsShowingTitle => showingTitle;
}
