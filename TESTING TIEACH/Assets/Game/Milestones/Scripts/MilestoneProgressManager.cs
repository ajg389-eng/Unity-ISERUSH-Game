using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Progression frame: Tutorial + six milestones.
/// Flow: complete all milestone missions → pass quiz (all answers correct) → unlock rewards → next milestone.
/// </summary>
public class MilestoneProgressManager : MonoBehaviour
{
    public static MilestoneProgressManager Instance { get; private set; }

    public enum MilestoneState
    {
        Locked = 0,
        Active = 1,
        QuizReady = 2,
        Completed = 3
    }

    public MilestoneDatabase database;

    [Tooltip("If true, auto-bind MissionProgressManager to the active milestone's mission list.")]
    public bool driveMissionList = true;

    readonly HashSet<string> completedMilestoneIds = new HashSet<string>();
    readonly HashSet<string> unlockedFeatureIds = new HashSet<string>();

    string activeMilestoneId;
    bool quizReady;

    public event Action OnMilestonesChanged;
    public event Action OnQuizReady;
    public event Action<MilestoneDefinition> OnMilestoneCompleted;
    public event Action<MilestoneUnlock> OnFeatureUnlocked;

    public string ActiveMilestoneId => activeMilestoneId;
    public bool IsQuizReady => quizReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MilestoneProgressManager>() != null) return;
        var go = new GameObject("MilestoneSystem");
        go.AddComponent<MilestoneProgressManager>();
        go.AddComponent<MilestoneQuizUI>();
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
            database = Resources.Load<MilestoneDatabase>("MilestoneDatabase");
    }

    void Start()
    {
        if (MissionProgressManager.Instance != null)
            MissionProgressManager.Instance.OnMissionsChanged += EvaluateActiveMilestone;

        EnsureActiveMilestone();
        ApplyActiveMissions(reset: false);
        EvaluateActiveMilestone();
        OnMilestonesChanged?.Invoke();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (MissionProgressManager.Instance != null)
            MissionProgressManager.Instance.OnMissionsChanged -= EvaluateActiveMilestone;
    }

    public MilestoneDefinition GetActiveMilestone()
    {
        return database != null ? database.GetById(activeMilestoneId) : null;
    }

    public MilestoneState GetState(MilestoneDefinition milestone)
    {
        if (milestone == null || string.IsNullOrEmpty(milestone.milestoneId))
            return MilestoneState.Locked;

        if (completedMilestoneIds.Contains(milestone.milestoneId))
            return MilestoneState.Completed;

        if (milestone.milestoneId == activeMilestoneId)
            return quizReady ? MilestoneState.QuizReady : MilestoneState.Active;

        return MilestoneState.Locked;
    }

    public MilestoneState GetState(string milestoneId)
    {
        return GetState(database != null ? database.GetById(milestoneId) : null);
    }

    public bool IsFeatureUnlocked(string unlockId)
    {
        return !string.IsNullOrEmpty(unlockId) && unlockedFeatureIds.Contains(unlockId);
    }

    public IReadOnlyCollection<string> GetUnlockedFeatureIds() => unlockedFeatureIds;

    public bool AreActiveMissionsComplete()
    {
        var milestone = GetActiveMilestone();
        if (milestone == null) return false;

        var missions = milestone.GetMissions();
        if (missions == null || missions.Count == 0)
            return true;

        var progress = MissionProgressManager.Instance;
        if (progress == null) return false;

        foreach (var mission in missions)
        {
            if (mission == null || string.IsNullOrEmpty(mission.missionId)) continue;
            if (!progress.IsComplete(mission.missionId))
                return false;
        }
        return true;
    }

    /// <summary>Called by quiz UI when every answer is correct.</summary>
    public bool TryCompleteActiveQuiz()
    {
        if (!quizReady) return false;

        var current = GetActiveMilestone();
        if (current == null) return false;

        completedMilestoneIds.Add(current.milestoneId);
        GrantUnlocks(current);
        quizReady = false;

        OnMilestoneCompleted?.Invoke(current);

        int index = database != null ? database.IndexOf(current.milestoneId) : -1;
        MilestoneDefinition next = null;
        if (database != null && index >= 0 && index + 1 < database.milestones.Count)
            next = database.milestones[index + 1];

        if (next != null && !string.IsNullOrEmpty(next.milestoneId))
        {
            activeMilestoneId = next.milestoneId;
            ApplyActiveMissions(reset: true);
        }
        else
        {
            activeMilestoneId = null;
            ApplyActiveMissions(reset: true);
        }

        OnMilestonesChanged?.Invoke();
        Sfx.Play(SfxId.MissionComplete);
        return true;
    }

    /// <summary>Debug / stub helper: mark missions complete enough to open the quiz.</summary>
    public void DebugForceQuizReady()
    {
        quizReady = true;
        OnQuizReady?.Invoke();
        OnMilestonesChanged?.Invoke();
    }

    public void ResetAllProgress()
    {
        completedMilestoneIds.Clear();
        unlockedFeatureIds.Clear();
        quizReady = false;
        activeMilestoneId = null;
        EnsureActiveMilestone();
        ApplyActiveMissions(reset: true);
        OnMilestonesChanged?.Invoke();
    }

    void EnsureActiveMilestone()
    {
        if (database == null || database.milestones == null || database.milestones.Count == 0)
            return;

        if (!string.IsNullOrEmpty(activeMilestoneId) && database.GetById(activeMilestoneId) != null)
            return;

        // First incomplete milestone, else first entry
        foreach (var m in database.milestones)
        {
            if (m == null || string.IsNullOrEmpty(m.milestoneId)) continue;
            if (!completedMilestoneIds.Contains(m.milestoneId))
            {
                activeMilestoneId = m.milestoneId;
                return;
            }
        }

        activeMilestoneId = database.milestones[0] != null ? database.milestones[0].milestoneId : null;
    }

    void ApplyActiveMissions(bool reset)
    {
        if (!driveMissionList) return;

        var progress = MissionProgressManager.Instance;
        if (progress == null) return;

        var milestone = GetActiveMilestone();
        if (milestone == null)
        {
            progress.SetActiveMissions(Array.Empty<MissionDefinition>(), reset);
            return;
        }

        progress.SetActiveMissions(milestone.GetMissions(), reset);
    }

    void EvaluateActiveMilestone()
    {
        if (string.IsNullOrEmpty(activeMilestoneId)) return;
        if (completedMilestoneIds.Contains(activeMilestoneId)) return;

        bool wasReady = quizReady;
        quizReady = AreActiveMissionsComplete();

        if (quizReady && !wasReady)
        {
            OnQuizReady?.Invoke();
            OnMilestonesChanged?.Invoke();
        }
        else if (quizReady != wasReady)
        {
            OnMilestonesChanged?.Invoke();
        }
    }

    void GrantUnlocks(MilestoneDefinition milestone)
    {
        if (milestone?.unlocks == null) return;
        foreach (var unlock in milestone.unlocks)
        {
            if (unlock == null || string.IsNullOrEmpty(unlock.unlockId)) continue;
            if (unlockedFeatureIds.Add(unlock.unlockId))
                OnFeatureUnlocked?.Invoke(unlock);
        }
    }
}
