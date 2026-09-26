using System;
using UnityEngine;

/// <summary>
/// Lightweight three-slot persistence for the core campaign state shown on the title screen.
/// World-layout serialization can be added behind this API without changing the slot UI.
/// </summary>
public static class GameSaveSlots
{
    public const int SlotCount = 3;
    public const string ActiveSlotKey = "ISE_RUSH.Save.ActiveSlot";

    const string Prefix = "ISE_RUSH.Save.";
    static bool slotSelectedThisSession;

    public readonly struct SlotInfo
    {
        public readonly int slotIndex;
        public readonly bool exists;
        public readonly int day;
        public readonly int cash;
        public readonly DateTime lastPlayedUtc;

        public SlotInfo(int slotIndex, bool exists, int day, int cash, DateTime lastPlayedUtc)
        {
            this.slotIndex = slotIndex;
            this.exists = exists;
            this.day = day;
            this.cash = cash;
            this.lastPlayedUtc = lastPlayedUtc;
        }
    }

    static string Key(int slotIndex, string field) => Prefix + slotIndex + "." + field;

    public static SlotInfo GetSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        bool exists = PlayerPrefs.GetInt(Key(slotIndex, "Exists"), 0) == 1;
        int day = Mathf.Max(1, PlayerPrefs.GetInt(Key(slotIndex, "Day"), 1));
        int cash = Mathf.Max(0, PlayerPrefs.GetInt(Key(slotIndex, "Cash"), 1000));
        long.TryParse(PlayerPrefs.GetString(Key(slotIndex, "LastPlayedUtc"), "0"), out long ticks);
        DateTime lastPlayed = ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
        return new SlotInfo(slotIndex, exists, day, cash, lastPlayed);
    }

    public static void SelectAndLoad(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        SlotInfo info = GetSlot(slotIndex);
        PlayerPrefs.SetInt(ActiveSlotKey, slotIndex);
        slotSelectedThisSession = true;

        var time = GameTimeManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<GameTimeManager>();
        var money = UnityEngine.Object.FindFirstObjectByType<MoneyManager>();

        if (info.exists)
        {
            if (time != null) time.RestoreDay(info.day);
            if (money != null) money.SetMoney(info.cash);
        }
        else
        {
            if (time != null) time.RestoreDay(1);
            if (money != null) money.SetMoney(money.startingMoney);
        }

        SaveActiveSlot();
    }

    public static void SaveActiveSlot()
    {
        if (!slotSelectedThisSession) return;
        int slotIndex = PlayerPrefs.GetInt(ActiveSlotKey, 0);
        if (slotIndex < 1 || slotIndex > SlotCount) return;

        var time = GameTimeManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<GameTimeManager>();
        var money = UnityEngine.Object.FindFirstObjectByType<MoneyManager>();
        int day = time != null ? time.CurrentDay : 1;
        int cash = money != null ? money.CurrentMoney : 1000;

        PlayerPrefs.SetInt(Key(slotIndex, "Exists"), 1);
        PlayerPrefs.SetInt(Key(slotIndex, "Day"), Mathf.Max(1, day));
        PlayerPrefs.SetInt(Key(slotIndex, "Cash"), Mathf.Max(0, cash));
        PlayerPrefs.SetString(Key(slotIndex, "LastPlayedUtc"), DateTime.UtcNow.Ticks.ToString());
        PlayerPrefs.Save();
    }
}

/// <summary>Records the selected slot at day boundaries and when the application is suspended.</summary>
public sealed class GameSaveSlotAutosave : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<GameSaveSlotAutosave>() != null) return;
        new GameObject("GameSaveSlotAutosave").AddComponent<GameSaveSlotAutosave>();
    }

    GameTimeManager clock;

    void Start()
    {
        clock = GameTimeManager.Instance ?? FindFirstObjectByType<GameTimeManager>();
        if (clock != null)
        {
            clock.OnDayEnded += Save;
            clock.OnDayStarted += Save;
        }
    }

    void OnDestroy()
    {
        if (clock != null)
        {
            clock.OnDayEnded -= Save;
            clock.OnDayStarted -= Save;
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) Save();
    }

    void OnApplicationQuit() => Save();
    void Save() => GameSaveSlots.SaveActiveSlot();
}
