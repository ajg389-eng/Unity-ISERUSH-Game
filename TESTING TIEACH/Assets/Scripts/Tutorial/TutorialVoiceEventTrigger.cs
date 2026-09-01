using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Raises a tutorial voice event from the Inspector or other UnityEvents.
/// </summary>
public class TutorialVoiceEventTrigger : MonoBehaviour
{
    [Tooltip("Event id raised on trigger (e.g. shift_started).")]
    public string eventId;

    [Tooltip("Raise automatically when this object is enabled.")]
    public bool raiseOnEnable;

    [Tooltip("Raise once per play session.")]
    public bool raiseOnce = true;

    static readonly System.Collections.Generic.HashSet<string> raisedThisSession = new System.Collections.Generic.HashSet<string>();

    public UnityEvent onTriggered;

    void OnEnable()
    {
        if (raiseOnEnable)
            Trigger();
    }

    public void Trigger()
    {
        if (string.IsNullOrEmpty(eventId)) return;

        string key = gameObject.name + ":" + eventId;
        if (raiseOnce && raisedThisSession.Contains(key))
            return;

        if (raiseOnce)
            raisedThisSession.Add(key);

        TutorialVoiceEvents.Raise(eventId);
        onTriggered?.Invoke();
    }

    public void TriggerWithId(string customEventId)
    {
        eventId = customEventId;
        raiseOnce = false;
        Trigger();
    }
}
