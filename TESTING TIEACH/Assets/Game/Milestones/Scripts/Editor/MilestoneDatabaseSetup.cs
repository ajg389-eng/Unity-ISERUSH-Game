#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Tutorial + six numbered milestones with tasks.
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
        public string productItemName;
        public bool countAnyUnlockedMenuProduct;
        public string missionIdOverride;
    }

    struct MilestoneContent
    {
        public string id;
        public string name;
        public string description;
        public string topics;
        public QuizQuestion[] questions;
        public MissionSpec[] missions;
    }

    [MenuItem("Game/Setup Milestone Database")]
    public static void Setup()
    {
        Setup(showDialog: true);
    }

    public static void SetupSilently()
    {
        Setup(showDialog: false);
    }

    static void Setup(bool showDialog)
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
        if (showDialog)
            EditorUtility.DisplayDialog(
                "Milestone Database",
                "Created Tutorial + 6 milestones with tasks at:\n" + DatabasePath,
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

        asset.unlocks = new List<MilestoneUnlock>();
        asset.quiz = null;

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

            mission.missionId = !string.IsNullOrEmpty(spec.missionIdOverride)
                ? spec.missionIdOverride
                : milestoneId + "_" + spec.id;
            if (string.IsNullOrEmpty(spec.missionIdOverride)
                && (!string.IsNullOrEmpty(spec.productItemName) || spec.countAnyUnlockedMenuProduct))
                mission.missionId += "_production";
            mission.title = spec.title;
            mission.description = spec.description;
            mission.completionEventId = spec.eventId;
            mission.requiredCount = Mathf.Max(1, spec.count);
            mission.producedItem = FindItemDefinition(spec.productItemName);
            mission.countAnyUnlockedMenuProduct = spec.countAnyUnlockedMenuProduct;
            mission.showBeforeComplete = true;
            mission.showAfterComplete = true;
            EditorUtility.SetDirty(mission);
            list.Add(mission);
        }

        return list;
    }

    static MissionSpec M(string id, string title, string description, string eventId, int count = 1,
        string productItemName = null, bool countAnyUnlockedMenuProduct = false,
        string missionIdOverride = null)
    {
        return new MissionSpec
        {
            id = id,
            title = title,
            description = description,
            eventId = eventId,
            count = count,
            productItemName = productItemName,
            countAnyUnlockedMenuProduct = countAnyUnlockedMenuProduct,
            missionIdOverride = missionIdOverride
        };
    }

    static ItemDefinition FindItemDefinition(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Game/Customers/Data" });
        foreach (string guid in guids)
        {
            ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null && (item.itemName == itemName || item.name == itemName))
                return item;
        }
        Debug.LogWarning("Milestone production item was not found: " + itemName);
        return null;
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
                name = "Process Analysis",
                description = "Map the process, improve station layout, and use queues to find bottlenecks.",
                topics = "Process mapping, facility layout, queues, and bottlenecks",
                missions = new[]
                {
                    M("observe_queue", "Configure a Fries recipe",
                        "Choose the Fries recipe at an Assembly Station to set up your first side item.",
                        "recipe_configured_fries",
                        missionIdOverride: "milestone_01_configure_fries"),
                    M("serve_orders", "Produce 15 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 15, countAnyUnlockedMenuProduct: true),
                    M("finish_shift", "Reposition a workstation",
                        "Move a workstation to improve the layout of your kitchen.",
                        TutorialVoiceEventId.StationRepositioned)
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
                name = "Capacity Planning",
                description = "Balance station and worker capacity as products and demand grow.",
                topics = "Capacity balancing, bottleneck shifts, and labor allocation",
                missions = new[]
                {
                    M("hire_capacity", "Configure a Cheeseburger",
                        "Choose the Cheeseburger recipe at an Assembly Station.",
                        "recipe_configured_cheeseburger",
                        missionIdOverride: "milestone_02_configure_cheeseburger"),
                    M("assign_labor", "Add cheese fries to the menu",
                        "Configure the Cheese Fries recipe at an Assembly Station.",
                        "recipe_configured_cheese_fries"),
                    M("balance_output", "Produce 25 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 25, countAnyUnlockedMenuProduct: true)
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
                name = "Engineering Economy",
                description = "Evaluate equipment and process changes by their impact on system output and profit.",
                topics = "Capital planning, production scheduling, and payback",
                missions = new[]
                {
                    M("buy_inputs", "Order production inputs",
                        "Place an ingredient order to supply your production line.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("expand_assets", "Configure a Classic Burger",
                        "Choose the Classic Burger recipe at an Assembly Station.",
                        "recipe_configured_classic_burger",
                        missionIdOverride: "milestone_03_configure_classic_burger"),
                    M("produce_sales", "Purchase an MK2 workstation",
                        "Purchase any MK2 workstation upgrade.",
                        TutorialVoiceEventId.Mk2StationPurchased),
                    M("finish_shift", "Produce 35 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 35, countAnyUnlockedMenuProduct: true)
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
                name = "Inventory Control",
                description = "Plan inventory and suppliers around demand, reliability, holding cost, and waste.",
                topics = "Forecasting, supplier reliability, and inventory control",
                missions = new[]
                {
                    M("restock", "Order production supplies",
                        "Place an ingredient order before the production line runs out.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("meet_demand", "Configure cheese bacon fries",
                        "Choose the Cheese Bacon Fries recipe at an Assembly Station.",
                        "recipe_configured_cheese_bacon_fries"),
                    M("multi_day", "Configure a Whipped Cream Sprinkle Shake",
                        "Choose the Whipped Cream Sprinkle Shake recipe at a Shake Station.",
                        "recipe_configured_whipped_cream_sprinkle_shake",
                        missionIdOverride: "milestone_04_configure_whipped_cream_sprinkle_shake"),
                    M("configure_recipe", "Produce 45 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 45, countAnyUnlockedMenuProduct: true)
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
                description = "Balance service speed with quality, and use maintenance to prevent equipment failures.",
                topics = "Quality Engineering, Reliability Engineering",
                missions = new[]
                {
                    M("finish_shift", "Produce 60 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 60, countAnyUnlockedMenuProduct: true),
                    M("keep_stocked", "Prevent stockouts",
                        "Replenish ingredients before they run out so production can continue throughout the shift.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("steady_service", "Maintain consistent service",
                        "Serve 25 customers while balancing order accuracy and speed.",
                        TutorialVoiceEventId.OrderServed, 25)
                }
            },
            new MilestoneContent
            {
                id = "milestone_06",
                name = "Systems Integration",
                description = "Treat people, processes, technology, and information as one system; optimize overall service.",
                topics = "Systems Engineering, Information Engineering, Engineering Management",
                missions = new[]
                {
                    M("connect_process", "Connect people to the process",
                        "Assign the worker to a station that supports the process bottleneck.",
                        TutorialVoiceEventId.WorkerAssigned),
                    M("link_output", "Link information flow",
                        "Set a station Output so work is routed through the connected kitchen.",
                        TutorialVoiceEventId.OutputLinked),
                    M("multi_day", "Sustain the integrated operation",
                        "Complete 2 full shifts and adjust people, process, and inventory as conditions change.",
                        TutorialVoiceEventId.DayEnded, 2),
                    M("staff_system", "Staff the operation",
                        "Hire a worker and consider how people fit into the whole production system.",
                        TutorialVoiceEventId.WorkerHired),
                    M("supply_ops", "Supply operations",
                        "Order ingredients and make sure upstream supply can support downstream service.",
                        TutorialVoiceEventId.IngredientsOrdered),
                    M("system_output", "Produce 75 unlocked menu items",
                        "Produce any combination of finished menu recipes you have unlocked so far.",
                        "", 75, countAnyUnlockedMenuProduct: true)
                }
            },
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
