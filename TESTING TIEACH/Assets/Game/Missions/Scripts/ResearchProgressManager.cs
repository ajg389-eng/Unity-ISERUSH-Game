using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ResearchProgressManager : MonoBehaviour
{
    [Serializable]
    public sealed class Definition
    {
        public string id, title, description, parentId;
        public int category, milestone, cost;
        public float durationMinutes;
        public Vector2 position;

        public Definition(string id, string title, string description, int category, int milestone,
            int cost, float durationMinutes, Vector2 position, string parentId = null)
        {
            this.id = id; this.title = title; this.description = description;
            this.category = category; this.milestone = milestone; this.cost = cost;
            this.durationMinutes = durationMinutes; this.position = position; this.parentId = parentId;
        }
    }

    public static ResearchProgressManager Instance { get; private set; }
    public static readonly IReadOnlyList<Definition> Definitions = new List<Definition>
    {
        new Definition("production_basics", "Restaurant Basics", "Unlock burgers and fries", 0, 1, 100, 30, new Vector2(0, 390)),
        new Definition("station_capacity_1", "Station Capacity I", "Place up to 2 of each station", 0, 2, 250, 60, new Vector2(270, 145), "production_basics"),
        new Definition("station_capacity_2", "Station Capacity II", "Place up to 3 of each station", 0, 3, 500, 90, new Vector2(270, -5), "station_capacity_1"),
        new Definition("station_capacity_3", "Station Capacity III", "Place up to 4 of each station", 0, 4, 800, 120, new Vector2(270, -155), "station_capacity_2"),
        new Definition("recipe_upgrades_1", "Recipe Upgrades I", "<b>Unlocks</b><br>• Bacon Burger<br>• Basic Shake", 0, 2, 400, 75, new Vector2(-270, 145), "production_basics"),
        new Definition("recipe_upgrades_2", "Recipe Upgrades II", "<b>Unlocks</b><br>• Cheese Bacon Burger<br>• Cheese Fries<br>• Whipped Cream Shake", 0, 3, 750, 120, new Vector2(-270, -5), "recipe_upgrades_1"),
        new Definition("recipe_upgrades_3", "Recipe Upgrades III", "<b>Unlocks</b><br>• Premium Burgers<br>• Bacon Fries<br>• Sprinkle Shake", 0, 4, 1000, 150, new Vector2(-270, -155), "recipe_upgrades_2"),
        new Definition("mk2_stations", "MK2 Stations", "Improve station speed", 0, 3, 500, 90, new Vector2(0, -305), "recipe_upgrades_1"),
        new Definition("industrial_kitchen", "Industrial Kitchen", "Place up to 5 of each station", 0, 4, 1200, 240, new Vector2(0, -755), "station_capacity_3"),

        new Definition("core_workforce", "Core Workforce", "Hire and assign workers", 1, 1, 100, 30, new Vector2(0, 390)),
        new Definition("fourth_worker", "Fourth Worker", "Hire up to 4 workers", 1, 2, 200, 60, new Vector2(-270, 145), "core_workforce"),
        new Definition("fifth_worker", "Fifth Worker", "Hire up to 5 workers", 1, 3, 400, 75, new Vector2(-270, -5), "fourth_worker"),
        new Definition("sixth_worker", "Sixth Worker", "Hire up to 6 workers", 1, 4, 700, 120, new Vector2(-270, -155), "fifth_worker"),
        new Definition("multi_worker_flows", "Multi-Worker Flows", "Assign up to 2 workers per flow", 1, 2, 300, 60, new Vector2(270, 145), "core_workforce"),
        new Definition("multi_worker_flows_2", "Multi-Worker Flows II", "Assign up to 3 workers per flow", 1, 3, 500, 90, new Vector2(270, -5), "multi_worker_flows"),
        new Definition("multi_worker_flows_3", "Multi-Worker Flows III", "Assign up to 4 workers per flow", 1, 4, 800, 120, new Vector2(270, -155), "multi_worker_flows_2"),
        new Definition("carry_training", "Carry Training", "Carry up to 2 items", 1, 2, 350, 75, new Vector2(-135, -305), "core_workforce"),
        new Definition("carry_training_2", "Carry Training II", "Carry up to 3 items", 1, 3, 550, 100, new Vector2(-135, -455), "carry_training"),
        new Definition("carry_training_3", "Carry Training III", "Carry up to 4 items", 1, 4, 850, 140, new Vector2(-135, -605), "carry_training_2"),
        new Definition("manual_operations", "Manual Operations", "Order and track supplies", 2, 1, 100, 30, new Vector2(0, 390)),
        new Definition("express_ordering", "Express Ordering", "Speed up ingredient orders", 2, 2, 250, 45, new Vector2(-270, 145), "manual_operations"),
        new Definition("automatic_ordering", "Automatic Ordering", "Restock below target", 2, 4, 700, 120, new Vector2(-270, -115), "express_ordering"),
        new Definition("smart_operations", "Smart Operations", "Coordinate stock and targets", 2, 4, 1200, 240, new Vector2(0, -380), "automatic_ordering")
    };

    readonly HashSet<string> completed = new HashSet<string>();
    string activeId;
    float remainingMinutes;
    float lastAbsoluteMinute;
    bool activeResearchUsesTutorialTimer;
    public event Action Changed;

    public string ActiveId => activeId;
    public float RemainingMinutes => Mathf.Max(0f, remainingMinutes);
    public float RemainingSeconds => Mathf.Max(0f, remainingMinutes * 60f);
    public bool IsTutorialTimedResearch => activeResearchUsesTutorialTimer;
    public bool HasActiveResearch => !string.IsNullOrEmpty(activeId);
    public int CompletedCount => completed.Count;

    void OnEnable()
    {
        if (MilestoneProgressManager.Instance != null)
            MilestoneProgressManager.Instance.OnMilestonesChanged += OnMilestonesChanged;
    }

    void OnDisable()
    {
        if (MilestoneProgressManager.Instance != null)
            MilestoneProgressManager.Instance.OnMilestonesChanged -= OnMilestonesChanged;
    }

    void OnMilestonesChanged() => Changed?.Invoke();

    public static ResearchProgressManager Ensure()
    {
        if (Instance != null) return Instance;
        ResearchProgressManager existing = FindFirstObjectByType<ResearchProgressManager>();
        return existing != null ? existing
            : new GameObject("ResearchProgressManager").AddComponent<ResearchProgressManager>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ResearchProgressManager>() != null) return;
        new GameObject("ResearchProgressManager").AddComponent<ResearchProgressManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        lastAbsoluteMinute = AbsoluteMinute;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        float now = AbsoluteMinute;
        if (!HasActiveResearch) return;

        if (activeResearchUsesTutorialTimer)
            remainingMinutes -= Time.unscaledDeltaTime / 60f;
        else
        {
            float elapsed = Mathf.Max(0f, now - lastAbsoluteMinute);
            remainingMinutes -= elapsed;
        }
        lastAbsoluteMinute = now;
        if (remainingMinutes > 0f) return;
        completed.Add(activeId);
        string finished = Get(activeId)?.title ?? "Research";
        activeId = null;
        remainingMinutes = 0f;
        activeResearchUsesTutorialTimer = false;
        NotificationCenter.Post(finished + " completed.", GameNotificationKind.Message, "research-complete-" + finished);
        Changed?.Invoke();
    }

    public bool IsCompleted(string id) => !string.IsNullOrEmpty(id) && completed.Contains(id);
    public bool IsActive(string id) => HasActiveResearch && activeId == id;
    public bool HasPrerequisite(Definition definition) => definition != null
        && (string.IsNullOrEmpty(definition.parentId) || IsCompleted(definition.parentId));
    public bool IsMilestoneAvailable(Definition definition) => definition != null
        && (MilestoneFeatures.HasReached(definition.milestone)
            || (OnboardingTutorial.IsResearchTutorialActive
                && OnboardingTutorial.IsTutorialResearchRoot(definition.id)));

    public bool TryStart(string id, out string reason)
    {
        Definition definition = Get(id);
        if (definition == null) { reason = "Research project not found."; return false; }
        if (IsCompleted(id)) { reason = "Already researched."; return false; }
        if (OnboardingTutorial.IsResearchTutorialActive
            && !OnboardingTutorial.IsTutorialResearchRoot(id))
        {
            reason = "Gus wants the first card in each branch researched first.";
            return false;
        }
        if (HasActiveResearch) { reason = "Finish the current research first."; return false; }
        if (!IsMilestoneAvailable(definition)) { reason = "Requires Milestone " + definition.milestone + "."; return false; }
        if (!HasPrerequisite(definition)) { reason = "Research the previous project first."; return false; }
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (money == null || !money.TrySpend(definition.cost)) { reason = "Not enough money."; return false; }
        activeId = id;
        activeResearchUsesTutorialTimer = OnboardingTutorial.IsResearchTutorialActive
            && OnboardingTutorial.IsTutorialResearchRoot(id);
        remainingMinutes = activeResearchUsesTutorialTimer ? 1f / 60f : definition.durationMinutes;
        lastAbsoluteMinute = AbsoluteMinute;
        Sfx.Play(SfxId.SpendMoney);
        Changed?.Invoke();
        reason = definition.title + " started.";
        return true;
    }

    public List<string> CaptureCompleted() => new List<string>(completed);

    public void DebugResetAll()
    {
        completed.Clear();
        activeId = null;
        remainingMinutes = 0f;
        activeResearchUsesTutorialTimer = false;
        lastAbsoluteMinute = AbsoluteMinute;
        Changed?.Invoke();
    }

    public void DebugCompleteAll()
    {
        completed.Clear();
        foreach (Definition definition in Definitions)
            if (definition != null && !string.IsNullOrEmpty(definition.id)) completed.Add(definition.id);
        activeId = null;
        remainingMinutes = 0f;
        activeResearchUsesTutorialTimer = false;
        lastAbsoluteMinute = AbsoluteMinute;
        Changed?.Invoke();
    }

    public void Restore(IReadOnlyList<string> savedCompleted, string savedActiveId, float savedRemaining)
    {
        completed.Clear();
        if (savedCompleted != null)
            foreach (string id in savedCompleted) if (Get(id) != null) completed.Add(id);
        activeId = Get(savedActiveId) != null && !completed.Contains(savedActiveId) ? savedActiveId : null;
        remainingMinutes = activeId != null ? Mathf.Max(0.01f, savedRemaining) : 0f;
        activeResearchUsesTutorialTimer = false;
        lastAbsoluteMinute = AbsoluteMinute;
        Changed?.Invoke();
    }

    public static Definition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Definition definition in Definitions) if (definition.id == id) return definition;
        return null;
    }

    float AbsoluteMinute
    {
        get
        {
            GameTimeManager time = GameTimeManager.Instance;
            return time != null ? (time.CurrentDay - 1) * 1440f + time.CurrentMinutes : 0f;
        }
    }
}
