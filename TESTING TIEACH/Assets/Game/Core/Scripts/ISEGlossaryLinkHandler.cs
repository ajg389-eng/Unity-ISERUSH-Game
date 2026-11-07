using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes only highlighted ISE links receive UI raycasts, so surrounding tutorial
/// and cutscene text continues to pass clicks through to the game world.
/// </summary>
public sealed class ISEGlossaryLinkHandler : MonoBehaviour, IPointerClickHandler, ICanvasRaycastFilter
{
    const string LinkPrefix = "ise:";

    TMP_Text targetText;

    public static void Attach(TMP_Text text)
    {
        if (text == null) return;
        ISEGlossaryLinkHandler handler = text.GetComponent<ISEGlossaryLinkHandler>();
        if (handler == null) handler = text.gameObject.AddComponent<ISEGlossaryLinkHandler>();
        handler.targetText = text;
        text.raycastTarget = true;
    }

    void Awake()
    {
        targetText = GetComponent<TMP_Text>();
        if (targetText != null) targetText.raycastTarget = true;
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        return TryGetVisibleLink(screenPoint, eventCamera, out _);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left
            || !TryGetVisibleLink(eventData.position, eventData.pressEventCamera, out int linkIndex))
            return;

        string linkId = targetText.textInfo.linkInfo[linkIndex].GetLinkID();
        if (string.IsNullOrWhiteSpace(linkId)
            || !linkId.StartsWith(LinkPrefix, System.StringComparison.OrdinalIgnoreCase))
            return;

        string term = linkId.Substring(LinkPrefix.Length).Trim();
        if (term.Length == 0) return;

        PauseMenuUI pauseMenu = PauseMenuUI.Instance;
        if (pauseMenu == null)
            pauseMenu = FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include);
        pauseMenu?.OpenGuidebookTermFromHud(term);
    }

    bool TryGetVisibleLink(Vector2 screenPoint, Camera eventCamera, out int linkIndex)
    {
        linkIndex = -1;
        if (targetText == null || !targetText.isActiveAndEnabled
            || targetText.textInfo == null || targetText.textInfo.linkCount == 0)
            return false;

        linkIndex = TMP_TextUtilities.FindIntersectingLink(targetText, screenPoint, eventCamera);
        if (linkIndex < 0 || linkIndex >= targetText.textInfo.linkInfo.Length) return false;

        TMP_LinkInfo link = targetText.textInfo.linkInfo[linkIndex];
        if (!link.GetLinkID().StartsWith(LinkPrefix, System.StringComparison.OrdinalIgnoreCase))
            return false;

        int linkEnd = link.linkTextfirstCharacterIndex + link.linkTextLength;
        return targetText.maxVisibleCharacters >= linkEnd;
    }
}
