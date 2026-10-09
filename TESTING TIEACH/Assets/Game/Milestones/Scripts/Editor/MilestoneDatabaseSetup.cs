#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Tutorial + four numbered milestones with authored quizzes and tasks.
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
            "Created Tutorial + 4 milestones with tasks and quizzes at:\n" + DatabasePath,
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
            quiz.title = "Milestone " + displayName + " Quiz";
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
                name = "1",
                description = "Build a small menu and learn to shape a kitchen workflow.",
                topics = "Burgers, fries, recipes, and station layout",
                unlockId = "milestone_01_complete",
                unlockName = "Lettuce, tomato, and cheese fries",
                missions = new[]
                {
                    M("observe_queue", "Set up the cheeseburger recipe",
                        "Choose the cheeseburger recipe at an Assembly Station to add cheese to the burger workflow.",
                        "recipe_configured_cheeseburger"),
                    M("relink_flow", "Connect the production flow",
                        "Link a station's output to the next workstation in the recipe workflow.",
                        TutorialVoiceEventId.OutputLinked),
                    M("serve_orders", "Serve 25 customers",
                        "Serve 25 customers with your burger and fries menu.",
                        TutorialVoiceEventId.OrderServed, 25),
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
                name = "2",
                description = "Expand the burger and fries recipes with fresh ingredients.",
                topics = "Classic burgers and cheese fries",
                unlockId = "milestone_02_complete",
                unlockName = "Bacon, shakes, and MK2 stations",
                missions = new[]
                {
                    M("hire_capacity", "Configure a Classic Burger",
                        "Choose the Classic Burger recipe at an Assembly Station.",
                        "recipe_configured_classic_burger"),
                    M("assign_labor", "Add cheese fries to the menu",
                        "Configure the Cheese Fries recipe at an Assembly Station.",
                        "recipe_configured_cheese_fries"),
                    M("balance_output", "Serve 5 Classic Burgers",
                        "Serve five Classic Burgers.",
                        "menu_item_served_classic_burger", 5),
                    M("finish_shift", "Serve 5 Cheese Fries",
                        "Serve five orders that include Cheese Fries.",
                        "menu_item_served_cheese_fries", 5)
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
                name = "3",
                description = "Add bacon and shakes, then invest in faster MK2 workstations.",
                topics = "Bacon recipes, shakes, MK2 stations",
                unlockId = "milestone_03_complete",
                unlockName = "Advanced burger, fries, and shake recipes",
                missions = new[]
                {
                    M("buy_inputs", "Configure a bacon burger",
                        "Choose the Bacon Cheeseburger recipe at an Assembly Station.",
                        "recipe_configured_bacon_cheeseburger"),
                    M("expand_assets", "Add shakes to the menu",
                        "Configure the basic Shake recipe at a Shake Station.",
                        "recipe_configured_shake"),
                    M("produce_sales", "Purchase an MK2 workstation",
                        "Purchase any MK2 workstation upgrade.",
                        TutorialVoiceEventId.Mk2StationPurchased),
                    M("finish_shift", "Serve 5 shakes",
                        "Serve five customers who ordered a Shake.",
                        "menu_item_served_shake", 5)
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
                name = "4",
                description = "Complete the menu with the most involved fries and shake production chains.",
                topics = "Premium fries, finished shakes, and production balance",
                unlockId = "milestone_04_complete",
                unlockName = "Full menu complete",
                missions = new[]
                {
                    M("restock", "Configure a Bacon Cheeseburger",
                        "Choose the Bacon Cheeseburger recipe.",
                        "recipe_configured_bacon_cheeseburger"),
                    M("meet_demand", "Configure cheese bacon fries",
                        "Choose the Cheese Bacon Fries recipe at an Assembly Station.",
                        "recipe_configured_cheese_bacon_fries"),
                    M("multi_day", "Configure the premium shake",
                        "Choose the Whipped Cream Sprinkle Shake recipe.",
                        "recipe_configured_whipped_cream_sprinkle_shake"),
                    M("configure_recipe", "Serve 5 Bacon Cheeseburgers",
                        "Serve five Bacon Cheeseburgers.",
                        "menu_item_served_bacon_cheeseburger", 5)
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
