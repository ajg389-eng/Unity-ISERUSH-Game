#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Tutorial + six main milestones with authored quizzes and tasks.
/// Menu: Game / Setup Milestone Database
/// </summary>
public static class MilestoneDatabaseSetup
{
    const string RootFolder = "Assets/Game/Milestones";
    const string ResourcesFolder = RootFolder + "/Resources";
    const string DefinitionsFolder = RootFolder + "/Data";
    const string MissionsFolder = DefinitionsFolder + "/Missions";
    const string DatabasePath = ResourcesFolder + "/MilestoneDatabase.asset";

    struct MissionSpec
    {
        public string id;
        public string title;
        public string description;
        public string eventId;
        public int count;
    }

    struct MilestoneContent
    {
        public string id;
        public string name;
        public string description;
        public string topics;
        public string unlockId;
        public string unlockName;
        public QuizQuestion[] questions;
        public MissionSpec[] missions;
    }

    [MenuItem("Game/Setup Milestone Database")]
    public static void Setup()
    {
        EnsureFolders();

        var tutorialMissions = AssetDatabase.LoadAssetAtPath<MissionDatabase>("Assets/Game/Missions/Resources/MissionDatabase.asset");
        if (tutorialMissions == null)
            tutorialMissions = Resources.Load<MissionDatabase>("MissionDatabase");

        var content = BuildMilestoneContent();
        var milestones = new List<MilestoneDefinition>(content.Length + 1);

        milestones.Add(CreateOrLoadMilestone(
            DefinitionsFolder + "/Milestone_Tutorial.asset",
            "tutorial",
            "Restaurant Basics",
            "Learn the controls and complete your first service loop.",
            topics: "Onboarding",
            isTutorial: true,
            missionDatabase: tutorialMissions,
            inlineMissions: null,
            unlockId: "tutorial_complete",
            unlockName: "Tutorial Complete",
            questions: null));

        foreach (var m in content)
        {
            milestones.Add(CreateOrLoadMilestone(
                DefinitionsFolder + $"/Milestone_{m.id}.asset",
                m.id,
                m.name,
                m.description,
                topics: m.topics,
                isTutorial: false,
                missionDatabase: null,
                inlineMissions: m.missions,
                unlockId: m.unlockId,
                unlockName: m.unlockName,
                questions: m.questions));
        }

        var database = AssetDatabase.LoadAssetAtPath<MilestoneDatabase>(DatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<MilestoneDatabase>();
            AssetDatabase.CreateAsset(database, DatabasePath);
        }

        database.milestones = milestones;
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = database;
        EditorUtility.DisplayDialog(
            "Milestone Database",
            "Created Tutorial + 6 milestones with tasks and quizzes at:\n" + DatabasePath,
            "OK");
    }

    [MenuItem("Game/Setup Milestone Database", true)]
    static bool SetupValidate() => !EditorApplication.isPlaying;

    static MilestoneDefinition CreateOrLoadMilestone(
        string path,
        string id,
        string displayName,
        string description,
        string topics,
        bool isTutorial,
        MissionDatabase missionDatabase,
        MissionSpec[] inlineMissions,
        string unlockId,
        string unlockName,
        QuizQuestion[] questions)
    {
        var asset = AssetDatabase.LoadAssetAtPath<MilestoneDefinition>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<MilestoneDefinition>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.milestoneId = id;
        asset.displayName = displayName;
        asset.description = string.IsNullOrEmpty(topics)
            ? description
            : description + "\n\nTopics: " + topics;
        asset.isTutorial = isTutorial;
        asset.missionDatabase = missionDatabase;
        asset.missions = BuildMissionList(id, inlineMissions);

        asset.unlocks = new List<MilestoneUnlock>
        {
            new MilestoneUnlock
            {
                kind = MilestoneUnlockKind.Feature,
                unlockId = unlockId,
                displayName = unlockName
            }
        };

        if (!isTutorial)
        {
            string quizPath = DefinitionsFolder + $"/Quiz_{id}.asset";
            var quiz = AssetDatabase.LoadAssetAtPath<QuizDefinition>(quizPath);
            if (quiz == null)
            {
                quiz = ScriptableObject.CreateInstance<QuizDefinition>();
                AssetDatabase.CreateAsset(quiz, quizPath);
            }

            quiz.quizId = id + "_quiz";
            quiz.title = displayName + " Quiz";
            quiz.introText = "Answer every question correctly to unlock the next milestone.";
            quiz.questions = questions != null
                ? new List<QuizQuestion>(questions)
                : new List<QuizQuestion>();

            EditorUtility.SetDirty(quiz);
            asset.quiz = quiz;
        }
        else
        {
            asset.quiz = null;
        }

        EditorUtility.SetDirty(asset);
        return asset;
    }

    static List<MissionDefinition> BuildMissionList(string milestoneId, MissionSpec[] specs)
    {
        var list = new List<MissionDefinition>();
        if (specs == null || specs.Length == 0)
            return list;

        foreach (var spec in specs)
        {
            if (string.IsNullOrEmpty(spec.id)) continue;
            string path = MissionsFolder + $"/{milestoneId}_{spec.id}.asset";
            var mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
            if (mission == null)
            {
                mission = ScriptableObject.CreateInstance<MissionDefinition>();
                AssetDatabase.CreateAsset(mission, path);
            }

            mission.missionId = milestoneId + "_" + spec.id;
            mission.title = spec.title;
            mission.description = spec.description;
            mission.completionEventId = spec.eventId;
            mission.requiredCount = Mathf.Max(1, spec.count);
            mission.showBeforeComplete = true;
            mission.showAfterComplete = true;
            EditorUtility.SetDirty(mission);
            list.Add(mission);
        }

        return list;
    }

    static MissionSpec M(string id, string title, string description, string eventId, int count = 1)
    {
        return new MissionSpec
        {
            id = id,
            title = title,
            description = description,
            eventId = eventId,
            count = count
        };
    }

    static QuizQuestion Q(string prompt, string a, string b, string c, string d, char answer)
    {
        int index;
        switch (answer)
        {
            case 'A': index = 0; break;
            case 'B': index = 1; break;
            case 'C': index = 2; break;
            case 'D': index = 3; break;
            default: index = 0; break;
        }

        return new QuizQuestion
        {
            prompt = prompt,
            choices = new List<string> { a, b, c, d },
            correctChoiceIndex = index
        };
    }

    static MilestoneContent[] BuildMilestoneContent()
    {
        return new[]
        {
            new MilestoneContent
            {
                id = "milestone_01",
                name = "Flow & Work Design",
                description = "Understand queues, layout, and how work moves through the restaurant.",
                topics = "Queueing Theory, Work Design & Measurement, Facilities Planning",
                unlockId = "milestone_01_complete",
                unlockName = "Flow & Work Design Complete",
                missions = new[]
                {
                    M("observe_queue", "Spot a bottleneck",
                        "Let at least 3 customers line up so you can see work pile up at the register.",
                        TutorialVoiceEventId.QueueGrowing),
                    M("serve_orders", "Measure throughput",
                        "Serve 8 customers to see how fast the system can move orders.",
                        TutorialVoiceEventId.OrderServed, 8),
                    M("relink_flow", "Improve the layout",
                        "Set or change a station Output link to redesign how work flows.",
                        TutorialVoiceEventId.OutputLinked),
                    M("finish_shift", "Study a full day",
                        "Run until closing (10 PM) to observe a complete service day.",
                        TutorialVoiceEventId.DayEnded)
                },
                questions = new[]
                {
                    Q("Customers are waiting for burgers while the assembly station is frequently idle. What is the most likely problem?",
                        "Too much assembly capacity",
                        "A bottleneck earlier in the process",
                        "Too much finished inventory",
                        "Customer demand is too low",
                        'B'),
                    Q("A worker repeatedly walks across the entire kitchen between the freezer and stove. What would an industrial engineer most likely investigate?",
                        "Increasing menu prices",
                        "Moving the stations closer together",
                        "Increasing customer demand",
                        "Buying more inventory",
                        'B'),
                    Q("The restaurant serves 20 customers per hour, and customers spend an average of 0.25 hours in the system. Using Little's Law, approximately how many customers are in the system on average?",
                        "4",
                        "5",
                        "10",
                        "80",
                        'B'),
                    Q("Which metric is most useful for identifying where work is accumulating?",
                        "Restaurant revenue",
                        "Queue length by station",
                        "Ingredient purchase price",
                        "Menu size",
                        'B')
                }
            },
            new MilestoneContent
            {
                id = "milestone_02",
                name = "Capacity & Optimization",
                description = "Find bottlenecks, allocate capacity, and balance labor.",
                topics = "Operations Research, Capacity Planning, Human Factors",
                unlockId = "milestone_02_complete",
                unlockName = "Capacity & Optimization Complete",
                missions = new[]
                {
                    M("hire_capacity", "Add labor capacity",
                        "Hire a worker to increase the restaurant's productive capacity.",
                        TutorialVoiceEventId.WorkerHired),
                    M("assign_labor", "Allocate the worker",
                        "Assign a worker to a station so capacity is placed where work happens.",
                        TutorialVoiceEventId.WorkerAssigned),
                    M("balance_output", "Balance the line",
                        "Serve 15 orders after staffing to test whether the bottleneck moved.",
                        TutorialVoiceEventId.OrderServed, 15),
                    M("finish_shift", "Evaluate the day",
                        "Finish a full shift and review how capacity held up until close.",
                        TutorialVoiceEventId.DayEnded)
                },
                questions = new[]
                {
                    Q("The grill is 98% utilized while assembly is 55% utilized, and orders are piling up before the grill. Where should additional capacity most likely be added?",
                        "Assembly",
                        "Grill",
                        "Pantry",
                        "Customer seating",
                        'B'),
                    Q("After adding another grill, assembly becomes heavily utilized and orders begin waiting there. What happened?",
                        "Demand disappeared",
                        "The bottleneck shifted",
                        "The new grill reduced capacity",
                        "Inventory became unnecessary",
                        'B'),
                    Q("You have enough money for either another grill or another employee. What should you analyze first?",
                        "Which option looks better",
                        "Which resource is currently limiting system performance",
                        "Which option costs more",
                        "Which option was unlocked most recently",
                        'B'),
                    Q("One worker is overloaded while another worker spends significant time idle. What is the best first response?",
                        "Increase customer demand",
                        "Reallocate work between employees",
                        "Raise prices",
                        "Increase inventory",
                        'B')
                }
            },
            new MilestoneContent
            {
                id = "milestone_03",
                name = "Production & Economics",
                description = "Connect production decisions to cost, profit, and scheduling.",
                topics = "Production Planning & Control, Engineering Economics",
                unlockId = "milestone_03_complete",
                unlockName = "Production & Economics Complete",
                missions = new[]
                {
                    M("buy_inputs", "Invest in inputs",
                        "Order an ingredient pack — a production cost that enables throughput.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("expand_assets", "Invest in capacity",
                        "Buy a station from the shop or expand the floor — spend to grow throughput.",
                        TutorialVoiceEventId.CapacityInvested),
                    M("produce_sales", "Generate sales",
                        "Serve 12 customers and watch revenue against your costs.",
                        TutorialVoiceEventId.OrderServed, 12),
                    M("finish_shift", "Close the books",
                        "Finish the shift and check end-of-day profit vs. spending.",
                        TutorialVoiceEventId.DayEnded)
                },
                questions = new[]
                {
                    Q("A $1,000 equipment upgrade is expected to increase profit by $250 per day. What is its simple payback period?",
                        "2 days",
                        "4 days",
                        "5 days",
                        "10 days",
                        'B'),
                    Q("Why can batch cooking improve production efficiency?",
                        "It eliminates all inventory",
                        "Multiple items can be processed together using available capacity",
                        "It guarantees perfect quality",
                        "It eliminates customer variability",
                        'B'),
                    Q("Which option is better evidence that an expansion was successful?",
                        "Revenue increased",
                        "Profit increased after accounting for the expansion's additional costs",
                        "More equipment was purchased",
                        "More employees were hired",
                        'B'),
                    Q("Several large orders are waiting while restaurant capacity is limited. Deciding which jobs should be processed and when is primarily what type of problem?",
                        "Production scheduling",
                        "Facility decoration",
                        "Market research",
                        "Reliability testing",
                        'A')
                }
            },
            new MilestoneContent
            {
                id = "milestone_04",
                name = "Supply Chain & Forecasting",
                description = "Manage suppliers, inventory, and predicted demand.",
                topics = "Supply Chain Management, Inventory Control, Demand Forecasting",
                unlockId = "milestone_04_complete",
                unlockName = "Supply Chain & Forecasting Complete",
                missions = new[]
                {
                    M("restock", "Replenish inventory",
                        "Order ingredient packs twice to practice replenishment decisions.",
                        TutorialVoiceEventId.IngredientsOrdered, 2),
                    M("meet_demand", "Serve forecasted demand",
                        "Serve 12 customers while keeping the kitchen stocked.",
                        TutorialVoiceEventId.OrderServed, 12),
                    M("multi_day", "Compare two days",
                        "Complete 2 full shifts so you can compare demand across days.",
                        TutorialVoiceEventId.DayEnded, 2)
                },
                questions = new[]
                {
                    Q("Supplier A is cheaper but frequently delivers late. Supplier B costs more but is highly reliable. What type of decision is this?",
                        "Cost versus reliability trade-off",
                        "Queue discipline decision",
                        "Facility layout problem",
                        "Worker ergonomics problem",
                        'A'),
                    Q("What is the main purpose of safety stock?",
                        "Guarantee maximum profit",
                        "Protect against uncertainty in demand or replenishment",
                        "Eliminate holding costs",
                        "Increase cooking speed",
                        'B'),
                    Q("Ordering extremely large quantities of ingredients lowers purchase cost per unit but increases which cost?",
                        "Waiting cost",
                        "Inventory holding cost",
                        "Employee training cost",
                        "Equipment maintenance cost",
                        'B'),
                    Q("Historical data shows that burger demand consistently increases between noon and 1 PM. How should this information be used?",
                        "Ignore it because future demand is always random",
                        "Forecast higher lunch demand and plan inventory/capacity accordingly",
                        "Reduce inventory before noon",
                        "Close a cooking station during lunch",
                        'B')
                }
            },
            new MilestoneContent
            {
                id = "milestone_05",
                name = "Quality & Reliability",
                description = "Balance speed with quality and keep equipment reliable.",
                topics = "Quality Engineering, Reliability Engineering",
                unlockId = "milestone_05_complete",
                unlockName = "Quality & Reliability Complete",
                missions = new[]
                {
                    M("steady_service", "Maintain steady service",
                        "Serve 20 customers — prioritize correct, complete orders over raw speed.",
                        TutorialVoiceEventId.OrderServed, 20),
                    M("keep_stocked", "Prevent stockouts",
                        "Order ingredients to keep production reliable through the rush.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("finish_shift", "Survive a full day",
                        "Finish a shift with the kitchen still able to serve — reliability over time.",
                        TutorialVoiceEventId.DayEnded)
                },
                questions = new[]
                {
                    Q("Increasing service speed causes the order-error rate to rise significantly. What does this demonstrate?",
                        "A quality-throughput trade-off",
                        "A supplier lead-time problem",
                        "A forecasting error",
                        "A facility expansion problem",
                        'A'),
                    Q("What is the purpose of preventive maintenance?",
                        "Increase demand",
                        "Reduce the likelihood or impact of equipment failures",
                        "Increase inventory",
                        "Reduce employee wages",
                        'B'),
                    Q("A grill frequently fails during rush periods. Which information would be most useful when evaluating the problem?",
                        "Failure frequency and repair time",
                        "Customer menu preferences only",
                        "Restaurant decoration cost",
                        "Pantry location only",
                        'A'),
                    Q("Which restaurant is performing better overall?",
                        "Restaurant A serves 120 orders with 20% errors",
                        "Restaurant B serves 110 orders with 1% errors and higher overall profit",
                        "Restaurant A because throughput is always the only objective",
                        "They are automatically equal",
                        'B')
                }
            },
            new MilestoneContent
            {
                id = "milestone_06",
                name = "Systems Integration",
                description = "See the restaurant as one interconnected system of people, process, and technology.",
                topics = "Systems Engineering, Information Engineering, Engineering Management",
                unlockId = "milestone_06_complete",
                unlockName = "Systems Integration Complete",
                missions = new[]
                {
                    M("staff_system", "Staff the system",
                        "Hire a worker — people are part of the production system.",
                        TutorialVoiceEventId.WorkerHired),
                    M("connect_process", "Connect the process",
                        "Assign a worker and set a station Output so people, stations, and flow align.",
                        TutorialVoiceEventId.WorkerAssigned),
                    M("link_output", "Link information flow",
                        "Set a station Output link — the routing decision connects the whole kitchen.",
                        TutorialVoiceEventId.OutputLinked),
                    M("supply_ops", "Supply operations",
                        "Order ingredients so upstream supply supports downstream service.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("system_output", "Deliver system output",
                        "Serve 25 customers as proof the whole system works together.",
                        TutorialVoiceEventId.OrderServed, 25),
                    M("multi_day", "Run the enterprise",
                        "Complete 2 full days managing people, process, inventory, and service together.",
                        TutorialVoiceEventId.DayEnded, 2)
                },
                questions = new[]
                {
                    Q("The kitchen increases cooking output, but customers are not served any faster because assembly cannot keep up. What does this demonstrate?",
                        "Improving one component does not necessarily improve the entire system",
                        "More cooking capacity always reduces throughput",
                        "Customer demand no longer matters",
                        "Inventory should always be eliminated",
                        'A'),
                    Q("Which dashboard would be most useful for making system-level decisions?",
                        "Profit only",
                        "Customer count only",
                        "Throughput, wait time, utilization, inventory, quality, and profit together",
                        "Employee names only",
                        'C'),
                    Q("A catering contract is highly profitable but would use most kitchen capacity during the lunch rush. What should the player consider?",
                        "Catering revenue only",
                        "Effects on regular customers, capacity, costs, and total profit",
                        "Whether the catering order contains burgers",
                        "Only the number of employees",
                        'B'),
                    Q("Which statement best describes Industrial & Systems Engineering after completing the game?",
                        "It is primarily about manufacturing machines",
                        "It is primarily about managing employees",
                        "It involves designing and improving interconnected systems of people, processes, resources, information, and technology",
                        "It is primarily about reducing customer queues",
                        'C')
                }
            }
        };
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game"))
            AssetDatabase.CreateFolder("Assets", "Game");
        if (!AssetDatabase.IsValidFolder(RootFolder))
            AssetDatabase.CreateFolder("Assets/Game", "Milestones");
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder(RootFolder, "Resources");
        if (!AssetDatabase.IsValidFolder(DefinitionsFolder))
            AssetDatabase.CreateFolder(RootFolder, "Data");
        if (!AssetDatabase.IsValidFolder(MissionsFolder))
            AssetDatabase.CreateFolder(DefinitionsFolder, "Missions");
    }
}
#endif
