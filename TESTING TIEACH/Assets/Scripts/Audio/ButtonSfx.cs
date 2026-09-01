using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a sound when this UI Button is clicked. Add to any button in the Inspector.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonSfx : MonoBehaviour
{
    public SfxId sfx = SfxId.UiClick;

    void Awake()
    {
        var button = GetComponent<Button>();
        if (button != null)
            button.onClick.AddListener(() => Sfx.Play(sfx));
    }
}
