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
    readonly Dictionary<string, int> progressByMissionId = new Dictionary<string, int>();

    public event System.Action OnMissionsChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MissionProgressManager>() != null) return;

        var go = new GameObject("MissionSystem");
        go.AddComponent<MissionProgressManager>();

        // Prefer a scene-placed panel under PlayerUI; only create runtime UI if missing.
        if (FindFirstObjectByType<MissionListUI>() == null)
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

        bool anyChange = false;
        foreach (var mission in database.missions)
        {
            if (mission == null || string.IsNullOrEmpty(mission.missionId)) continue;
            if (completedMissionIds.Contains(mission.missionId)) continue;
            if (mission.completionEventId != eventId) continue;

            int needed = Mathf.Max(1, mission.requiredCount);
            int current = GetProgress(mission.missionId) + 1;
            progressByMissionId[mission.missionId] = current;
            anyChange = true;

            if (current >= needed)
            {
                completedMissionIds.Add(mission.missionId);
                Sfx.Play(SfxId.MissionComplete);
            }
        }

        if (anyChange)
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

    public int GetProgress(string missionId)
    {
        if (string.IsNullOrEmpty(missionId)) return 0;
        return progressByMissionId.TryGetValue(missionId, out int n) ? n : 0;
    }

    public int GetProgress(MissionDefinition mission)
    {
        return mission != null ? GetProgress(mission.missionId) : 0;
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

    public bool AreAllMissionsComplete()
    {
        if (database?.missions == null || database.missions.Count == 0)
            return false;

        foreach (var mission in database.missions)
        {
            if (mission == null || string.IsNullOrEmpty(mission.missionId)) continue;
            if (!IsComplete(mission))
                return false;
        }
        return true;
    }

    /// <summary>Swap which missions are tracked (used when entering a new milestone).</summary>
    public void SetActiveMissions(IReadOnlyList<MissionDefinition> missions, bool resetProgress = true)
    {
        if (database == null)
            database = ScriptableObject.CreateInstance<MissionDatabase>();

        if (database.missions == null)
            database.missions = new List<MissionDefinition>();
        else
            database.missions.Clear();

        if (missions != null)
        {
            foreach (var m in missions)
            {
                if (m != null)
                    database.missions.Add(m);
            }
        }

        if (resetProgress)
        {
            completedMissionIds.Clear();
            progressByMissionId.Clear();
        }

        OnMissionsChanged?.Invoke();
    }

    public void ResetProgress()
    {
        completedMissionIds.Clear();
        progressByMissionId.Clear();
        OnMissionsChanged?.Invoke();
    }
}
