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
    public readonly string id;
    public readonly bool pinned;
    public bool read;

    public GameNotification(string message, string time, GameNotificationKind kind,
        string id = null, bool pinned = false, bool read = false)
    {
        this.message = message;
        this.time = time;
        this.kind = kind;
        this.id = id;
        this.pinned = pinned;
        this.read = read;
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

    public static bool ContainsId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].id == id)
                return true;
        return false;
    }

    public static void Post(string message, GameNotificationKind kind = GameNotificationKind.Message,
        string dedupeKey = null, float cooldownSeconds = 0f, bool markRead = false, bool pinned = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        if (!string.IsNullOrEmpty(dedupeKey) && ContainsId(dedupeKey))
            return;

        if (!string.IsNullOrEmpty(dedupeKey) && lastPostedByKey.TryGetValue(dedupeKey, out float last) &&
            Time.unscaledTime - last < Mathf.Max(0f, cooldownSeconds))
            return;

        if (!string.IsNullOrEmpty(dedupeKey))
            lastPostedByKey[dedupeKey] = Time.unscaledTime;

        string stamp = DateTime.Now.ToString("h:mm tt");
        var gameTime = GameTimeManager.Instance;
        if (gameTime != null)
            stamp = gameTime.GetClockText();

        entries.Insert(0, new GameNotification(message.Trim(), stamp, kind, dedupeKey, pinned, markRead));
        if (entries.Count > MaxEntries)
        {
            for (int i = entries.Count - 1; i >= MaxEntries; i--)
            {
                if (entries[i].pinned) continue;
                entries.RemoveAt(i);
            }
        }
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

    public static void Clear(bool includePinned = false)
    {
        if (entries.Count == 0)
        {
            if (includePinned)
                lastPostedByKey.Clear();
            return;
        }

        int before = entries.Count;
        if (includePinned)
        {
            entries.Clear();
            lastPostedByKey.Clear();
        }
        else
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].pinned) continue;
                entries.RemoveAt(i);
            }
        }

        if (includePinned || entries.Count != before)
            Changed?.Invoke();
    }
}
