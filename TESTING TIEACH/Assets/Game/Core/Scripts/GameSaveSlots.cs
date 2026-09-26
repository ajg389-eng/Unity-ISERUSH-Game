using System;
using UnityEngine;

/// <summary>
/// Three-slot persistence for kitchen checkpoints and campaign metadata.
/// </summary>
public static class GameSaveSlots
{
    public const int SlotCount = 3;
    public const string ActiveSlotKey = "ISE_RUSH.Save.ActiveSlot";

    const string Prefix = "ISE_RUSH.Save.";
    static bool slotSelectedThisSession;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { slotSelectedThisSession = false; }

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

    public static bool SelectAndLoad(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        SlotInfo info = GetSlot(slotIndex);
        PlayerPrefs.SetInt(ActiveSlotKey, slotIndex);
        slotSelectedThisSession = true;

        var time = GameTimeManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<GameTimeManager>();
        var money = UnityEngine.Object.FindFirstObjectByType<MoneyManager>();

        if (info.exists)
        {
            string json = PlayerPrefs.GetString(Key(slotIndex, "Kitchen"), "");
            if (!string.IsNullOrEmpty(json))
            {
                KitchenSaveSnapshot checkpoint = null;
                try { checkpoint = JsonUtility.FromJson<KitchenSaveSnapshot>(json); }
                catch (Exception ex) { Debug.LogError("Could not read kitchen save: " + ex.Message); }
                if (checkpoint == null || !checkpoint.Restore())
                {
                    slotSelectedThisSession = false;
                    Debug.LogError("Kitchen save could not be restored. The saved checkpoint has been preserved.");
                    return false;
                }
            }
            else
            {
                // Older saves never contained a layout or clock time.
                if (time != null) time.RestoreDay(info.day);
                if (money != null) money.SetMoney(info.cash);
            }
        }
        else
        {
            if (time != null) time.RestoreDay(1);
            if (money != null) money.SetMoney(money.startingMoney);
        }

        SaveActiveSlot();
        return true;
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
        string checkpoint = JsonUtility.ToJson(KitchenSaveSnapshot.Capture());
        PlayerPrefs.SetString(Key(slotIndex, "Kitchen"), checkpoint);

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
    float nextSave;

    void Update()
    {
        if (Time.unscaledTime < nextSave) return;
        nextSave = Time.unscaledTime + 30f;
        Save();
    }

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
