using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks mission completion from game events. Fires OnMissionsChanged when the list updates.
/// </summary>
public class MissionProgressManager : MonoBehaviour
{
    public static MissionProgressManager Instance { get; private set; }

    public MissionDatabase database;

    readonly HashSet<string> completedMissionIds = new HashSet<string>();

    public event System.Action OnMissionsChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MissionProgressManager>() != null) return;

        var go = new GameObject("MissionSystem");
        go.AddComponent<MissionProgressManager>();
        go.AddComponent<MissionListUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (database == null)
            database = Resources.Load<MissionDatabase>("MissionDatabase");
    }

    void OnEnable()
    {
        TutorialVoiceEvents.OnEvent += HandleGameEvent;
    }

    void OnDisable()
    {
        TutorialVoiceEvents.OnEvent -= HandleGameEvent;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void HandleGameEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || database?.missions == null) return;

        bool anyNew = false;
        foreach (var mission in database.missions)
        {
            if (mission == null || string.IsNullOrEmpty(mission.missionId)) continue;
            if (completedMissionIds.Contains(mission.missionId)) continue;
            if (mission.completionEventId != eventId) continue;

            completedMissionIds.Add(mission.missionId);
            anyNew = true;
            Sfx.Play(SfxId.MissionComplete);
        }

        if (anyNew)
            OnMissionsChanged?.Invoke();
    }

    public bool IsComplete(MissionDefinition mission)
    {
        return mission != null
            && !string.IsNullOrEmpty(mission.missionId)
            && completedMissionIds.Contains(mission.missionId);
    }

    public bool IsComplete(string missionId)
    {
        return !string.IsNullOrEmpty(missionId) && completedMissionIds.Contains(missionId);
    }

    /// <summary>Missions that should appear in the task list right now.</summary>
    public List<MissionDefinition> GetVisibleMissions()
    {
        var list = new List<MissionDefinition>();
        if (database?.missions == null) return list;

        foreach (var mission in database.missions)
        {
            if (mission == null) continue;
            bool done = IsComplete(mission);
            if (done && !mission.showAfterComplete) continue;
            if (!done && !mission.showBeforeComplete) continue;
            list.Add(mission);
        }
        return list;
    }

    public MissionDefinition GetCurrentMission()
    {
        if (database?.missions == null) return null;
        foreach (var mission in database.missions)
        {
            if (mission == null) continue;
            if (!IsComplete(mission))
                return mission;
        }
        return null;
    }

    public void ResetProgress()
    {
        completedMissionIds.Clear();
        OnMissionsChanged?.Invoke();
    }
}
