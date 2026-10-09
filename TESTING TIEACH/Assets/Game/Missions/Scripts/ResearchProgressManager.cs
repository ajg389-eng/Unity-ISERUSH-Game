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
        new Definition("production_basics", "Restaurant Basics", "Core MK1 production", 0, 1, 100, 30, new Vector2(0, 390)),
        new Definition("additional_stations", "Additional Stations", "Buy extra station copies", 0, 2, 250, 60, new Vector2(-270, 145), "production_basics"),
        new Definition("mk2_stations", "MK2 Stations", "Larger buffers and batches", 0, 3, 500, 90, new Vector2(270, 145), "production_basics"),
        new Definition("expanded_capacity", "Expanded Capacity", "Higher station ownership limits", 0, 4, 650, 120, new Vector2(-270, -115), "additional_stations"),
        new Definition("advanced_processing", "Advanced Processing", "Higher-volume production layouts", 0, 4, 750, 150, new Vector2(270, -115), "mk2_stations"),
        new Definition("industrial_kitchen", "Industrial Kitchen", "Maximum production capacity", 0, 4, 1200, 240, new Vector2(0, -380), "expanded_capacity"),

        new Definition("core_workforce", "Core Workforce", "Hire and assign workers", 1, 1, 100, 30, new Vector2(0, 390)),
        new Definition("fourth_worker", "Fourth Worker", "Increase the hiring limit", 1, 2, 200, 60, new Vector2(-270, 145), "core_workforce"),
        new Definition("multi_worker_flows", "Multi-Worker Flows", "Assign two workers to one flow", 1, 2, 300, 60, new Vector2(270, 145), "core_workforce"),
        new Definition("carry_training", "Carry Training", "Unlock higher carry upgrades", 1, 3, 500, 90, new Vector2(-270, -115), "fourth_worker"),
        new Definition("specialist_training", "Specialist Training", "Improve processing efficiency", 1, 4, 650, 120, new Vector2(270, -115), "multi_worker_flows"),
        new Definition("expert_workforce", "Expert Workforce", "Maximum worker capacity", 1, 4, 1000, 180, new Vector2(0, -380), "carry_training"),

        new Definition("manual_operations", "Manual Operations", "Order and monitor supplies", 2, 1, 100, 30, new Vector2(0, 390)),
        new Definition("queue_analytics", "Queue Analytics", "See queues and utilization", 2, 2, 300, 60, new Vector2(-270, 145), "manual_operations"),
        new Definition("production_targets", "Production Targets", "Set desired product inventory", 2, 3, 500, 90, new Vector2(270, 145), "manual_operations"),
        new Definition("automatic_ordering", "Automatic Ordering", "Reorder ingredients below a target", 2, 4, 700, 120, new Vector2(-270, -115), "queue_analytics"),
        new Definition("bottleneck_insights", "Bottleneck Insights", "Identify constrained stations", 2, 4, 800, 150, new Vector2(270, -115), "production_targets"),
        new Definition("smart_operations", "Smart Operations", "Coordinate targets and supplies", 2, 4, 1200, 240, new Vector2(0, -380), "automatic_ordering")
    };

    readonly HashSet<string> completed = new HashSet<string>();
    string activeId;
    float remainingMinutes;
    float lastAbsoluteMinute;
    public event Action Changed;

    public string ActiveId => activeId;
    public float RemainingMinutes => Mathf.Max(0f, remainingMinutes);
    public bool HasActiveResearch => !string.IsNullOrEmpty(activeId);

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
        float elapsed = Mathf.Max(0f, now - lastAbsoluteMinute);
        lastAbsoluteMinute = now;
        if (!HasActiveResearch || elapsed <= 0f) return;
        remainingMinutes -= elapsed;
        if (remainingMinutes > 0f) return;
        completed.Add(activeId);
        string finished = Get(activeId)?.title ?? "Research";
        activeId = null;
        remainingMinutes = 0f;
        NotificationCenter.Post(finished + " completed.", GameNotificationKind.Message, "research-complete-" + finished);
        Changed?.Invoke();
    }

    public bool IsCompleted(string id) => !string.IsNullOrEmpty(id) && completed.Contains(id);
    public bool IsActive(string id) => HasActiveResearch && activeId == id;
    public bool HasPrerequisite(Definition definition) => definition != null
        && (string.IsNullOrEmpty(definition.parentId) || IsCompleted(definition.parentId));
    public bool IsMilestoneAvailable(Definition definition) => definition != null
        && MilestoneFeatures.HasReached(definition.milestone);

    public bool TryStart(string id, out string reason)
    {
        Definition definition = Get(id);
        if (definition == null) { reason = "Research project not found."; return false; }
        if (IsCompleted(id)) { reason = "Already researched."; return false; }
        if (HasActiveResearch) { reason = "Finish the current research first."; return false; }
        if (!IsMilestoneAvailable(definition)) { reason = "Requires Milestone " + definition.milestone + "."; return false; }
        if (!HasPrerequisite(definition)) { reason = "Research the previous project first."; return false; }
        MoneyManager money = FindFirstObjectByType<MoneyManager>();
        if (money == null || !money.TrySpend(definition.cost)) { reason = "Not enough money."; return false; }
        activeId = id;
        remainingMinutes = definition.durationMinutes;
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
