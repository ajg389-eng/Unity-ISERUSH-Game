using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime access to the Kenney Game Icons pack. The source files are kept as
/// Texture2D assets and converted to cached UI sprites when a screen requests
/// an icon. This avoids duplicating the imported art or requiring prefab edits.
/// </summary>
public enum GameIconId
{
    Plus,
    Minus,
    Trash,
    Checkmark,
    Warning,
    Exclamation,
    Locked,
    Unlocked,
    Wrench,
    Save,
    ZoomIn,
    ZoomOut,
    ZoomDefault,
    MusicOn,
    MusicOff,
    Pause,
    Play,
    Next,
    Previous,
    Gear,
    Cart,
    MenuGrid,
    MenuList,
    Open,
    Cross,
    Home,
    Information,
    FastForward,
    Analytics
}

public sealed class GameIconLibrary : ScriptableObject
{
    [SerializeField] Texture2D plus;
    [SerializeField] Texture2D minus;
    [SerializeField] Texture2D trash;
    [SerializeField] Texture2D checkmark;
    [SerializeField] Texture2D warning;
    [SerializeField] Texture2D exclamation;
    [SerializeField] Texture2D locked;
    [SerializeField] Texture2D unlocked;
    [SerializeField] Texture2D wrench;
    [SerializeField] Texture2D save;
    [SerializeField] Texture2D zoomIn;
    [SerializeField] Texture2D zoomOut;
    [SerializeField] Texture2D zoomDefault;
    [SerializeField] Texture2D musicOn;
    [SerializeField] Texture2D musicOff;
    [SerializeField] Texture2D pause;
    [SerializeField] Texture2D play;
    [SerializeField] Texture2D next;
    [SerializeField] Texture2D previous;
    [SerializeField] Texture2D gear;
    [SerializeField] Texture2D cart;
    [SerializeField] Texture2D menuGrid;
    [SerializeField] Texture2D menuList;
    [SerializeField] Texture2D open;
    [SerializeField] Texture2D cross;
    [SerializeField] Texture2D home;
    [SerializeField] Texture2D information;
    [SerializeField] Texture2D fastForward;
    [SerializeField] Texture2D analytics;

    readonly Dictionary<GameIconId, Sprite> spriteCache = new Dictionary<GameIconId, Sprite>();
    static GameIconLibrary instance;

    public static GameIconLibrary Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<GameIconLibrary>("GameIconLibrary");
            return instance;
        }
    }

    public static Sprite GetSprite(GameIconId id)
    {
        return Instance != null ? Instance.Get(id) : null;
    }

    public Sprite Get(GameIconId id)
    {
        if (spriteCache.TryGetValue(id, out Sprite cached))
            return cached;

        Texture2D texture = GetTexture(id);
        if (texture == null)
            return null;

        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        sprite.name = "Kenney_" + id;
        spriteCache[id] = sprite;
        return sprite;
    }

    /// <summary>
    /// Adds an icon to a dynamically created uGUI button while retaining its
    /// text label. Existing labels are padded so the icon never covers text.
    /// </summary>
    public static void ApplyToButton(Button button, GameIconId id, float size = 20f, float leftPadding = 8f, bool hideLabel = false)
    {
        if (button == null) return;
        Sprite sprite = GetSprite(id);
        if (sprite == null) return;

        Transform existing = button.transform.Find("KenneyIcon");
        GameObject iconObject = existing != null ? existing.gameObject : new GameObject("KenneyIcon", typeof(RectTransform), typeof(Image));
        if (iconObject.transform.parent != button.transform)
            iconObject.transform.SetParent(button.transform, false);

        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = hideLabel ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
        iconRect.anchorMax = hideLabel ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
        iconRect.pivot = hideLabel ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = hideLabel ? Vector2.zero : new Vector2(leftPadding, 0f);
        iconRect.sizeDelta = new Vector2(size, size);

        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;

        for (int i = 0; i < button.transform.childCount; i++)
        {
            Transform child = button.transform.GetChild(i);
            if (child == iconObject.transform) continue;
            TextMeshProUGUI label = child.GetComponent<TextMeshProUGUI>();
            if (label == null) continue;

            if (hideLabel)
            {
                label.enabled = false;
                continue;
            }

            RectTransform labelRect = label.rectTransform;
            if (labelRect.anchorMin.x <= 0.01f && labelRect.anchorMax.x >= 0.99f)
            {
                Vector2 offsetMin = labelRect.offsetMin;
                offsetMin.x = Mathf.Max(offsetMin.x, leftPadding + size + 6f);
                labelRect.offsetMin = offsetMin;
            }
        }
    }

    Texture2D GetTexture(GameIconId id)
    {
        switch (id)
        {
            case GameIconId.Plus: return plus;
            case GameIconId.Minus: return minus;
            case GameIconId.Trash: return trash;
            case GameIconId.Checkmark: return checkmark;
            case GameIconId.Warning: return warning;
            case GameIconId.Exclamation: return exclamation;
            case GameIconId.Locked: return locked;
            case GameIconId.Unlocked: return unlocked;
            case GameIconId.Wrench: return wrench;
            case GameIconId.Save: return save;
            case GameIconId.ZoomIn: return zoomIn;
            case GameIconId.ZoomOut: return zoomOut;
            case GameIconId.ZoomDefault: return zoomDefault;
            case GameIconId.MusicOn: return musicOn;
            case GameIconId.MusicOff: return musicOff;
            case GameIconId.Pause: return pause;
            case GameIconId.Play: return play;
            case GameIconId.Next: return next;
            case GameIconId.Previous: return previous;
            case GameIconId.Gear: return gear;
            case GameIconId.Cart: return cart;
            case GameIconId.MenuGrid: return menuGrid;
            case GameIconId.MenuList: return menuList;
            case GameIconId.Open: return open;
            case GameIconId.Cross: return cross;
            case GameIconId.Home: return home;
            case GameIconId.Information: return information;
            case GameIconId.FastForward: return fastForward;
            case GameIconId.Analytics: return analytics;
            default: return null;
        }
    }
}
