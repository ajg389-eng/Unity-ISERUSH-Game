using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Progression frame: Tutorial + the numbered milestones in the active database.
/// Flow: complete all milestone missions → next milestone and its research projects.
/// </summary>
public class MilestoneProgressManager : MonoBehaviour
{
    public static MilestoneProgressManager Instance { get; private set; }

    public enum MilestoneState
    {
        Locked = 0,
        Active = 1,
        Completed = 2
    }

    public MilestoneDatabase database;

    [Tooltip("If true, auto-bind MissionProgressManager to the active milestone's mission list.")]
    public bool driveMissionList = true;

    readonly HashSet<string> completedMilestoneIds = new HashSet<string>();

    string activeMilestoneId;
    GameTimeManager boundTimeManager;
    bool activeShiftObjectives;
    bool allowEndOfShiftCompletion;

    public event Action OnMilestonesChanged;
    public event Action<MilestoneDefinition> OnMilestoneCompleted;

    public string ActiveMilestoneId => activeMilestoneId;
    public bool ActiveShiftObjectives => activeShiftObjectives;
    public int CompletedMilestoneCount => completedMilestoneIds.Count;
    public int CompletedNumberedMilestoneCount
    {
        get
        {
            if (database?.milestones == null) return 0;
            int count = 0;
            foreach (MilestoneDefinition milestone in database.milestones)
                if (milestone != null && !milestone.isTutorial
                    && !string.IsNullOrEmpty(milestone.milestoneId)
                    && completedMilestoneIds.Contains(milestone.milestoneId))
                    count++;
            return count;
        }
    }
    public IReadOnlyCollection<string> GetCompletedMilestoneIds() => completedMilestoneIds;

    /// <summary>
    /// Highest numbered milestone the player has reached. Tutorial does not count.
    /// A milestone counts as reached when it is active or already completed.
    /// </summary>
    public int GetHighestReachedNumberedStage()
    {
        if (database?.milestones == null) return 0;
        int highest = 0;
        int numbered = 0;
        foreach (var milestone in database.milestones)
        {
            if (milestone == null || milestone.isTutorial) continue;
            numbered++;
            bool reached = completedMilestoneIds.Contains(milestone.milestoneId)
                || milestone.milestoneId == activeMilestoneId;
            if (reached)
                highest = numbered;
        }
        return highest;
    }

    public int GetNumberedMilestoneCount()
    {
        if (database?.milestones == null) return 0;
        int count = 0;
        foreach (MilestoneDefinition milestone in database.milestones)
            if (milestone != null && !milestone.isTutorial && !string.IsNullOrEmpty(milestone.milestoneId))
                count++;
        return count;
    }

    public string GetMilestoneDisplayLabel(MilestoneDefinition milestone)
    {
        if (milestone == null) return "Milestone";
        if (milestone.isTutorial) return "Tutorial";
        int number = 0;
        if (database?.milestones != null)
        {
            foreach (MilestoneDefinition entry in database.milestones)
            {
                if (entry == null || entry.isTutorial) continue;
                number++;
                if (entry.milestoneId == milestone.milestoneId)
                {
                    string displayName = milestone.displayName?.Trim();
                    if (string.IsNullOrWhiteSpace(displayName)
                        || (int.TryParse(displayName, out int displayNumber) && displayNumber == number))
                        return "Milestone " + number;
                    return "Milestone " + number + ": " + displayName;
                }
            }
        }
        return string.IsNullOrWhiteSpace(milestone.displayName) ? milestone.milestoneId : milestone.displayName;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<MilestoneProgressManager>() != null) return;
        var go = new GameObject("MilestoneSystem");
        go.AddComponent<MilestoneProgressManager>();
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
        BindShiftEvents();
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
        if (boundTimeManager != null)
            boundTimeManager.OnShiftEnding -= HandleShiftEnding;
    }

    void BindShiftEvents()
    {
        GameTimeManager timeManager = GameTimeManager.Instance;
        if (timeManager == boundTimeManager) return;
        if (boundTimeManager != null)
            boundTimeManager.OnShiftEnding -= HandleShiftEnding;
        boundTimeManager = timeManager;
        if (boundTimeManager != null)
            boundTimeManager.OnShiftEnding += HandleShiftEnding;
    }

    /// <summary>Begins a fresh, shift-scoped objective attempt for the active numbered milestone.</summary>
    public void BeginActiveShiftObjectives()
    {
        BindShiftEvents();
        MilestoneDefinition milestone = GetActiveMilestone();
        if (milestone == null || milestone.isTutorial) return;

        activeShiftObjectives = true;
        MissionProgressManager.Instance?.ResetProgress();
        OnMilestonesChanged?.Invoke();
    }

    /// <summary>Restores whether a save was captured during the active milestone shift.</summary>
    public void RestoreActiveShiftObjectives(bool active)
    {
        MilestoneDefinition milestone = GetActiveMilestone();
        activeShiftObjectives = active && milestone != null && !milestone.isTutorial;
    }

    void HandleShiftEnding()
    {
        if (!activeShiftObjectives) return;
        activeShiftObjectives = false;

        MilestoneDefinition milestone = GetActiveMilestone();
        StoreStatisticsManager statistics = StoreStatisticsManager.Instance;
        bool efficiencyMet = milestone != null
            && ShiftPlanningUI.GetShiftSystemEfficiencyPercent() + 0.001f
                >= Mathf.Max(0, milestone.minimumSystemEfficiencyPercent);
        bool serviceMet = milestone != null && statistics != null
            && statistics.CustomersServedPercent + 0.001f
                >= Mathf.Max(0, milestone.minimumCustomersServedPercent);
        bool itemTargetsMet = milestone != null && AreActiveMissionsComplete();

        if (itemTargetsMet && efficiencyMet && serviceMet)
        {
            allowEndOfShiftCompletion = true;
            try
            {
                EvaluateActiveMilestone();
            }
            finally
            {
                allowEndOfShiftCompletion = false;
            }
        }

        OnMilestonesChanged?.Invoke();
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
            return MilestoneState.Active;

        return MilestoneState.Locked;
    }

    public MilestoneState GetState(string milestoneId)
    {
        return GetState(database != null ? database.GetById(milestoneId) : null);
    }

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

    /// <summary>Advance after the active milestone's missions are complete.</summary>
    bool CompleteActiveMilestone()
    {
        var current = GetActiveMilestone();
        if (current == null) return false;
        if (completedMilestoneIds.Contains(current.milestoneId)) return false;

        completedMilestoneIds.Add(current.milestoneId);

        OnMilestoneCompleted?.Invoke(current);

        int index = database != null ? database.IndexOf(current.milestoneId) : -1;
        MilestoneDefinition next = null;
        if (database != null && index >= 0 && index + 1 < database.milestones.Count)
            next = database.milestones[index + 1];

        if (next != null && !string.IsNullOrEmpty(next.milestoneId))
        {
            activeMilestoneId = next.milestoneId;
            ApplyActiveMissions(reset: true);
            MilestoneUnlockPopupUI.ShowAfterTransition(next);
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

    /// <summary>Debug: complete every milestone before this numbered stage and make it active.</summary>
    public bool DebugJumpToNumberedMilestone(int number, out string label)
    {
        label = null;
        activeShiftObjectives = false;
        if (database == null || database.milestones == null || database.milestones.Count == 0)
            return false;

        var numbered = new List<MilestoneDefinition>();
        foreach (var milestone in database.milestones)
        {
            if (milestone == null || milestone.isTutorial) continue;
            if (string.IsNullOrEmpty(milestone.milestoneId)) continue;
            numbered.Add(milestone);
        }

        if (number < 1 || number > numbered.Count)
            return false;

        MilestoneDefinition target = numbered[number - 1];
        label = GetMilestoneDisplayLabel(target);

        completedMilestoneIds.Clear();

        foreach (var milestone in database.milestones)
        {
            if (milestone == null || string.IsNullOrEmpty(milestone.milestoneId)) continue;
            if (milestone.milestoneId == target.milestoneId)
                break;
            completedMilestoneIds.Add(milestone.milestoneId);
        }

        activeMilestoneId = target.milestoneId;
        ApplyActiveMissions(reset: true);
        OnMilestonesChanged?.Invoke();
        return true;
    }

    public string GetActiveMilestoneDebugLabel() =>
        GetActiveMilestone() != null ? GetMilestoneDisplayLabel(GetActiveMilestone()) : "None";

    public void ResetAllProgress()
    {
        activeShiftObjectives = false;
        completedMilestoneIds.Clear();
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

        // First incomplete milestone. A null active id is valid after the final milestone.
        foreach (var m in database.milestones)
        {
            if (m == null || string.IsNullOrEmpty(m.milestoneId)) continue;
            if (!completedMilestoneIds.Contains(m.milestoneId))
            {
                activeMilestoneId = m.milestoneId;
                return;
            }
        }

        activeMilestoneId = null;
    }

    public void RestoreCheckpoint(string savedActiveMilestoneId, IReadOnlyList<string> savedCompletedMilestoneIds)
    {
        activeShiftObjectives = false;
        completedMilestoneIds.Clear();

        if (database?.milestones != null && savedCompletedMilestoneIds != null)
        {
            foreach (string id in savedCompletedMilestoneIds)
            {
                MilestoneDefinition milestone = database.GetById(id);
                if (milestone == null || string.IsNullOrEmpty(id)) continue;
                completedMilestoneIds.Add(id);
            }
        }

        MilestoneDefinition active = database != null ? database.GetById(savedActiveMilestoneId) : null;
        activeMilestoneId = active != null && !completedMilestoneIds.Contains(active.milestoneId)
            ? active.milestoneId : null;
        if (activeMilestoneId == null && !AreAllMilestonesComplete())
            EnsureActiveMilestone();

        ApplyActiveMissions(reset: true);
        EvaluateActiveMilestone();
        OnMilestonesChanged?.Invoke();
    }

    bool AreAllMilestonesComplete()
    {
        if (database?.milestones == null || database.milestones.Count == 0) return false;
        foreach (MilestoneDefinition milestone in database.milestones)
        {
            if (milestone == null || string.IsNullOrEmpty(milestone.milestoneId)) continue;
            if (!completedMilestoneIds.Contains(milestone.milestoneId)) return false;
        }
        return true;
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
        if (TitleScreenController.IsTitleScreenActive || OnboardingTutorial.BlocksProgression) return;

        if (string.IsNullOrEmpty(activeMilestoneId)) return;
        if (completedMilestoneIds.Contains(activeMilestoneId)) return;
        MilestoneDefinition active = GetActiveMilestone();
        if (active != null && !active.isTutorial && !allowEndOfShiftCompletion) return;
        if (AreActiveMissionsComplete())
            CompleteActiveMilestone();
    }

    /// <summary>Called when the first-run kitchen tour is finished or skipped.</summary>
    public void NotifyOnboardingFinished()
    {
        EvaluateActiveMilestone();
        OnMilestonesChanged?.Invoke();
    }

    /// <summary>Evaluate restored progress only after the player enters gameplay.</summary>
    public void NotifyGameplayStarted()
    {
        EvaluateActiveMilestone();
    }

}
