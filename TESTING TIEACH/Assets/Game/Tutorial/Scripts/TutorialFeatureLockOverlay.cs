using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Visual and interaction lock for a control that the tutorial has not introduced yet.</summary>
public sealed class TutorialFeatureLockOverlay : MonoBehaviour
{
    const string OverlayName = "TutorialFeatureLockOverlay";

    Button targetButton;
    Navigation originalNavigation;
    bool capturedNavigation;

    public static void SetLocked(Button target, bool locked)
    {
        if (target == null) return;
        TutorialFeatureLockOverlay overlay = FindOrCreate(target.transform, locked);
        if (overlay == null) return;
        overlay.Apply(target);
    }

    public static void SetLocked(RectTransform target, bool locked)
    {
        if (target == null) return;
        TutorialFeatureLockOverlay overlay = FindOrCreate(target, locked);
        if (overlay != null) overlay.Apply(null);
    }

    static TutorialFeatureLockOverlay FindOrCreate(Transform target, bool locked)
    {
        Transform existing = target.Find(OverlayName);
        TutorialFeatureLockOverlay overlay = existing != null
            ? existing.GetComponent<TutorialFeatureLockOverlay>() : null;

        if (!locked)
        {
            if (overlay != null) overlay.Release();
            else if (existing != null) Object.Destroy(existing.gameObject);
            return null;
        }

        return overlay != null ? overlay : Create(target);
    }

    static TutorialFeatureLockOverlay Create(Transform target)
    {
        GameObject overlayObject = new GameObject(OverlayName,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement),
            typeof(TutorialFeatureLockOverlay));
        overlayObject.transform.SetParent(target, false);
        overlayObject.transform.SetAsLastSibling();

        LayoutElement layoutElement = overlayObject.GetComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        RectTransform rect = (RectTransform)overlayObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = overlayObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.045f, 0.065f, 0.84f);
        background.raycastTarget = true;

        Button blocker = overlayObject.GetComponent<Button>();
        blocker.targetGraphic = background;
        blocker.transition = Selectable.Transition.None;
        blocker.navigation = new Navigation { mode = Navigation.Mode.None };
        blocker.onClick.AddListener(() => { });

        GameObject lockObject = new GameObject("LockIcon", typeof(RectTransform), typeof(Image));
        lockObject.transform.SetParent(overlayObject.transform, false);
        RectTransform lockRect = (RectTransform)lockObject.transform;
        lockRect.anchorMin = lockRect.anchorMax = new Vector2(0.5f, 0.5f);
        lockRect.pivot = new Vector2(0.5f, 0.5f);
        lockRect.sizeDelta = new Vector2(22f, 22f);
        lockRect.anchoredPosition = Vector2.zero;

        Image lockImage = lockObject.GetComponent<Image>();
        lockImage.sprite = GameIconLibrary.GetSprite(GameIconId.Locked);
        lockImage.color = GameUITheme.Accent;
        lockImage.preserveAspect = true;
        lockImage.raycastTarget = false;
        if (lockImage.sprite == null)
        {
            lockImage.enabled = false;
            TextMeshProUGUI fallback = lockObject.AddComponent<TextMeshProUGUI>();
            fallback.text = "LOCKED";
            fallback.fontSize = 9f;
            fallback.fontStyle = FontStyles.Bold;
            fallback.alignment = TextAlignmentOptions.Center;
            fallback.color = Color.white;
            fallback.raycastTarget = false;
            lockRect.sizeDelta = new Vector2(54f, 20f);
        }

        return overlayObject.GetComponent<TutorialFeatureLockOverlay>();
    }

    void Apply(Button target)
    {
        transform.SetAsLastSibling();
        if (target != null && targetButton != target)
        {
            RestoreButtonNavigation();
            targetButton = target;
            originalNavigation = target.navigation;
            capturedNavigation = true;
            Navigation blockedNavigation = originalNavigation;
            blockedNavigation.mode = Navigation.Mode.None;
            target.navigation = blockedNavigation;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == target.gameObject)
                eventSystem.SetSelectedGameObject(null);
        }
    }

    void Release()
    {
        RestoreButtonNavigation();
        targetButton = null;
        Destroy(gameObject);
    }

    void RestoreButtonNavigation()
    {
        if (capturedNavigation && targetButton != null)
            targetButton.navigation = originalNavigation;
        capturedNavigation = false;
    }

    void OnDestroy()
    {
        RestoreButtonNavigation();
    }
}
