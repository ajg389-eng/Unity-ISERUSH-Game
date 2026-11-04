using UnityEngine;

/// <summary>Applies the Kenney pointer icon as the system cursor across scenes.</summary>
public sealed class GameCursorController : MonoBehaviour
{
    const string CursorResourcePath = "Cursors/KenneyUpLeftCursorOutlined";
    static readonly Vector2 CursorHotspot = new Vector2(12f, 12f);
    static GameCursorController instance;
    static bool hiddenForPlayerUi;

    Texture2D cursorTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        hiddenForPlayerUi = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        new GameObject(nameof(GameCursorController)).AddComponent<GameCursorController>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        cursorTexture = Resources.Load<Texture2D>(CursorResourcePath);
        if (cursorTexture == null)
        {
            Debug.LogWarning($"Custom cursor texture was not found at Resources/{CursorResourcePath}.");
            return;
        }

        ApplyCursor();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ApplyCursor();
            UpdateCursorVisibility();
        }
    }

    void LateUpdate()
    {
        // Camera free-look can change Cursor.visible while the screenshot toggle is active.
        if (hiddenForPlayerUi && Cursor.visible)
            Cursor.visible = false;
    }

    public static void SetHiddenForPlayerUI(bool hidden)
    {
        hiddenForPlayerUi = hidden;
        if (instance != null)
            instance.UpdateCursorVisibility();
    }

    void ApplyCursor()
    {
        if (cursorTexture != null)
            Cursor.SetCursor(cursorTexture, CursorHotspot, CursorMode.Auto);

        UpdateCursorVisibility();
    }

    void UpdateCursorVisibility()
    {
        if (hiddenForPlayerUi)
            Cursor.visible = false;
        else if (Cursor.lockState != CursorLockMode.Locked)
            Cursor.visible = true;
    }
}
