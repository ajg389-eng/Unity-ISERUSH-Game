using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameNotificationKind
{
    Message,
    Warning
}

public sealed class GameNotification
{
    public readonly string message;
    public readonly string time;
    public readonly GameNotificationKind kind;
    public bool read;

    public GameNotification(string message, string time, GameNotificationKind kind)
    {
        this.message = message;
        this.time = time;
        this.kind = kind;
    }
}

/// <summary>Runtime history for player-facing messages and operational warnings.</summary>
public static class NotificationCenter
{
    const int MaxEntries = 50;
    static readonly List<GameNotification> entries = new List<GameNotification>();
    static readonly Dictionary<string, float> lastPostedByKey = new Dictionary<string, float>();

    public static event Action Changed;
    public static IReadOnlyList<GameNotification> Entries => entries;
    public static int UnreadCount
    {
        get
        {
            int count = 0;
            foreach (var entry in entries)
                if (!entry.read) count++;
            return count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        entries.Clear();
        lastPostedByKey.Clear();
        Changed = null;
    }

    public static void Post(string message, GameNotificationKind kind = GameNotificationKind.Message,
        string dedupeKey = null, float cooldownSeconds = 0f)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        if (!string.IsNullOrEmpty(dedupeKey) && lastPostedByKey.TryGetValue(dedupeKey, out float last) &&
            Time.unscaledTime - last < Mathf.Max(0f, cooldownSeconds))
            return;

        if (!string.IsNullOrEmpty(dedupeKey))
            lastPostedByKey[dedupeKey] = Time.unscaledTime;

        string stamp = DateTime.Now.ToString("h:mm tt");
        var gameTime = GameTimeManager.Instance;
        if (gameTime != null)
            stamp = gameTime.GetClockText();

        entries.Insert(0, new GameNotification(message.Trim(), stamp, kind));
        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        Changed?.Invoke();
    }

    public static void MarkAllRead()
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            if (!entry.read)
            {
                entry.read = true;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public static void Clear()
    {
        if (entries.Count == 0) return;
        entries.Clear();
        Changed?.Invoke();
    }
}
