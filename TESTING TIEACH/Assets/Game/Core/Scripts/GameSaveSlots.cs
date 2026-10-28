using System;
using System.Collections.Generic;
using System.Text;
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
        public readonly int milestone;
        public readonly bool introSeen;
        public readonly DateTime lastPlayedUtc;

        public SlotInfo(int slotIndex, bool exists, int day, int cash, int milestone, bool introSeen,
            DateTime lastPlayedUtc)
        {
            this.slotIndex = slotIndex;
            this.exists = exists;
            this.day = day;
            this.cash = cash;
            this.milestone = milestone;
            this.introSeen = introSeen;
            this.lastPlayedUtc = lastPlayedUtc;
        }
    }

    static string Key(int slotIndex, string field) => Prefix + slotIndex + "." + field;

    static string JoinNames(IEnumerable<string> names)
    {
        var value = new StringBuilder();
        if (names == null) return string.Empty;
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (value.Length > 0) value.Append('\n');
            value.Append(name);
        }
        return value.ToString();
    }

    public static SlotInfo GetSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        bool exists = PlayerPrefs.GetInt(Key(slotIndex, "Exists"), 0) == 1;
        int day = Mathf.Max(1, PlayerPrefs.GetInt(Key(slotIndex, "Day"), 1));
        int cash = Mathf.Max(0, PlayerPrefs.GetInt(Key(slotIndex, "Cash"), 1000));
        int milestone = 0;
        string kitchenJson = PlayerPrefs.GetString(Key(slotIndex, "Kitchen"), "");
        if (exists && !string.IsNullOrEmpty(kitchenJson))
        {
            try
            {
                var snapshot = JsonUtility.FromJson<KitchenSaveSnapshot>(kitchenJson);
                if (snapshot != null)
                    milestone = Mathf.Max(0, snapshot.milestone);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not read save-slot milestone: " + ex.Message);
            }
        }
        long.TryParse(PlayerPrefs.GetString(Key(slotIndex, "LastPlayedUtc"), "0"), out long ticks);
        DateTime lastPlayed = ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
        // Saves created before the intro system are considered already introduced.
        bool introSeen = exists && (!PlayerPrefs.HasKey(Key(slotIndex, "IntroSeen"))
            || PlayerPrefs.GetInt(Key(slotIndex, "IntroSeen"), 0) == 1);
        return new SlotInfo(slotIndex, exists, day, cash, milestone, introSeen, lastPlayed);
    }

    public static void WipeSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        string[] fields = { "Exists", "Day", "Cash", "LastPlayedUtc", "Kitchen", "MenuItems", "IntroSeen", "InvestorLetterSent" };
        foreach (string field in fields)
            PlayerPrefs.DeleteKey(Key(slotIndex, field));

        if (PlayerPrefs.GetInt(ActiveSlotKey, 0) == slotIndex)
        {
            PlayerPrefs.DeleteKey(ActiveSlotKey);
            slotSelectedThisSession = false;
        }
        PlayerPrefs.Save();
    }

    public static bool SelectAndLoad(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, SlotCount);
        SlotInfo info = GetSlot(slotIndex);
        PlayerPrefs.SetInt(ActiveSlotKey, slotIndex);
        slotSelectedThisSession = true;
        NotificationCenter.Clear(true);

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
                ShiftPlanningUI.RestoreSaveState(null);
                if (time != null) time.RestoreDay(info.day);
                if (money != null) money.SetMoney(info.cash);
                StoreStatisticsManager.Instance?.ResetCareerStatistics();
                StoreStatisticsManager.Instance?.BeginDay();

                // There is no saved kitchen to restore, so clear any scene state
                // left over from the previously selected slot.
                DebugMenu.ResetStationsAndWorkersRuntime(out _, out _);
            }
        }
        else
        {
            ShiftPlanningUI.RestoreSaveState(null);
            PlayerPrefs.SetInt(Key(slotIndex, "IntroSeen"), 0);
            PlayerPrefs.DeleteKey(Key(slotIndex, "InvestorLetterSent"));
            if (time != null) time.RestoreDay(1);
            if (money != null) money.SetMoney(money.startingMoney);

            // Tutorial completion is a global PlayerPrefs value until a slot snapshot
            // restores it. Reset all campaign state before creating an empty slot's
            // first checkpoint so progress from another save cannot leak into New Game.
            PlayerPrefs.DeleteKey(OnboardingTutorial.PrefsCompleteKey);
            var onboarding = OnboardingTutorial.Instance
                ?? UnityEngine.Object.FindFirstObjectByType<OnboardingTutorial>();
            if (onboarding != null)
                onboarding.Restart();

            if (ProductionManager.Instance != null && ProductionManager.Instance.orderConfig != null)
                ProductionManager.Instance.orderConfig.ClearEnabledMenuItems();

            StoreStatisticsManager.Instance?.ResetCareerStatistics();
            StoreStatisticsManager.Instance?.BeginDay();

            if (MissionProgressManager.Instance != null)
                MissionProgressManager.Instance.ResetProgress();
            if (MilestoneProgressManager.Instance != null)
                MilestoneProgressManager.Instance.ResetAllProgress();
            PlayerPrefs.DeleteKey(Key(slotIndex, "MenuItems"));
        }

        RestoreMenuSelection(slotIndex);
        SaveActiveSlot();
        return true;
    }

    /// <summary>
    /// Persists only the small menu selection. The full kitchen checkpoint is
    /// intentionally left to the normal autosave so a menu click does not scan
    /// every station, worker, flow, and customer.
    /// </summary>
    public static void SaveEnabledMenuSelection(CustomerOrderConfig config)
    {
        if (!slotSelectedThisSession || config == null) return;
        int slotIndex = PlayerPrefs.GetInt(ActiveSlotKey, 0);
        if (slotIndex < 1 || slotIndex > SlotCount) return;
        PlayerPrefs.SetString(Key(slotIndex, "MenuItems"), JoinNames(config.CaptureEnabledMenuItemNames()));
    }

    static void RestoreMenuSelection(int slotIndex)
    {
        string key = Key(slotIndex, "MenuItems");
        if (!PlayerPrefs.HasKey(key)) return;
        ProductionManager production = ProductionManager.Instance;
        CustomerOrderConfig config = production != null ? production.orderConfig : null;
        if (config == null) return;
        string packedNames = PlayerPrefs.GetString(key, string.Empty);
        config.RestoreEnabledMenuItems(packedNames.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public static void MarkActiveSlotIntroSeen()
    {
        int slotIndex = PlayerPrefs.GetInt(ActiveSlotKey, 0);
        if (slotIndex < 1 || slotIndex > SlotCount) return;
        PlayerPrefs.SetInt(Key(slotIndex, "IntroSeen"), 1);
        PlayerPrefs.Save();
    }

    public static bool ActiveSlotInvestorLetterSent()
    {
        int slotIndex = PlayerPrefs.GetInt(ActiveSlotKey, 0);
        if (slotIndex < 1 || slotIndex > SlotCount) return false;
        return PlayerPrefs.GetInt(Key(slotIndex, "InvestorLetterSent"), 0) == 1;
    }

    public static void MarkActiveSlotInvestorLetterSent()
    {
        int slotIndex = PlayerPrefs.GetInt(ActiveSlotKey, 0);
        if (slotIndex < 1 || slotIndex > SlotCount) return;
        PlayerPrefs.SetInt(Key(slotIndex, "InvestorLetterSent"), 1);
        PlayerPrefs.Save();
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
        KitchenSaveSnapshot snapshot = KitchenSaveSnapshot.Capture();
        string checkpoint = JsonUtility.ToJson(snapshot);
        PlayerPrefs.SetString(Key(slotIndex, "Kitchen"), checkpoint);
        PlayerPrefs.SetString(Key(slotIndex, "MenuItems"), JoinNames(snapshot.enabledMenuItemNames));

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
