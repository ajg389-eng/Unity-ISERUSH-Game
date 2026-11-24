#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Tutorial + four numbered milestones with tasks.
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
        public string deliveredItemName;
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
                "Created Tutorial + 4 milestones with tasks at:\n" + DatabasePath,
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
            mission.completionEventId = string.IsNullOrEmpty(spec.deliveredItemName)
                ? spec.eventId : null;
            mission.requiredCount = Mathf.Max(1, spec.count);
            mission.producedItem = FindItemDefinition(spec.productItemName);
            mission.deliveredItem = FindItemDefinition(spec.deliveredItemName);
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
        string missionIdOverride = null, string deliveredItemName = null)
    {
        return new MissionSpec
        {
            id = id,
            title = title,
            description = description,
            eventId = eventId,
            count = count,
            productItemName = productItemName,
            deliveredItemName = deliveredItemName,
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

    static string ServedItemEvent(string itemName)
    {
        return TutorialVoiceEventId.MenuItemServed(FindItemDefinition(itemName));
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
                name = "Process Flow",
                description = "Connect stations and observe how work moves from ingredients to finished items.",
                topics = "Process, flow, bottleneck",
                missions = new[]
                {
                    M("observe_queue", "Serve 54 Fries",
                        "Serve at least 54 Fries this shift, 90% of the 60-item daily demand.", null, 54,
                        missionIdOverride: "milestone_01_serve_fries", deliveredItemName: "Fries"),
                    M("serve_orders", "Serve 54 Hamburgers",
                        "Serve at least 54 Hamburgers this shift, 90% of the 60-item daily demand.", null, 54,
                        missionIdOverride: "milestone_01_serve_hamburgers", deliveredItemName: "Hamburger"),
                    M("finish_shift", "Serve 54 Shakes",
                        "Serve at least 54 Shakes this shift, 90% of the 60-item daily demand.", null, 54,
                        missionIdOverride: "milestone_01_serve_shakes", deliveredItemName: "Shake")
                },
                questions = new[]
                {
                    Q("What does a bottleneck limit?",
                        "The finished output of the whole process",
                        "The price of each recipe",
                        "The number of stations in the restaurant",
                        "The restaurant's opening hours",
                        'A'),
                    Q("What can a growing queue tell you?",
                        "The next step may not keep up with incoming work",
                        "The whole process has extra capacity",
                        "The item is ready for pickup",
                        "Customer demand has stopped",
                        'A'),
                    Q("What is a process?",
                        "Connected steps that turn inputs into finished items",
                        "A single station working by itself",
                        "A list of menu prices",
                        "The number of customers in line",
                        'A')
                }
            },
            new MilestoneContent
            {
                id = "milestone_02",
                name = "Demand and Capacity",
                description = "Compare how quickly customers order with how quickly the process can produce items.",
                topics = "Demand, capacity, throughput",
                missions = new[]
                {
                    M("balance_output", "Serve 108 Cheeseburgers",
                        "Serve at least 108 Cheeseburgers this shift, 90% of the 120-item daily demand.", null, 108,
                        missionIdOverride: "milestone_02_serve_cheeseburgers",
                        deliveredItemName: "Cheeseburger"),
                    M("finish_shift", "Serve 108 Cheese Fries",
                        "Serve at least 108 Cheese Fries this shift, 90% of the 120-item daily demand.", null, 108,
                        missionIdOverride: "milestone_02_serve_cheese_fries",
                        deliveredItemName: "Cheese Fries"),
                    M("produce_whipped_cream_shake", "Serve 108 Whipped Cream Shakes",
                        "Serve at least 108 Whipped Cream Shakes this shift, 90% of the 120-item daily demand.", null, 108,
                        missionIdOverride: "milestone_02_serve_whipped_cream_shakes",
                        deliveredItemName: "Whipped Cream Shake")
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
                name = "Supply Planning",
                description = "Estimate ingredient use and set supply rates to support production.",
                topics = "Stock, supply, demand",
                missions = new[]
                {
                    M("finish_shift", "Serve 162 Bacon Cheeseburgers",
                        "Serve at least 162 Bacon Cheeseburgers this shift, 90% of the 180-item daily demand.", null, 162,
                        missionIdOverride: "milestone_03_serve_bacon_cheeseburgers",
                        deliveredItemName: "Bacon Cheeseburger"),
                    M("produce_classic_burger", "Serve 162 Classic Burgers",
                        "Serve at least 162 Classic Burgers this shift, 90% of the 180-item daily demand.", null, 162,
                        missionIdOverride: "milestone_03_serve_classic_burgers",
                        deliveredItemName: "Classic Burger")
                },
                questions = new[]
                {
                    Q("What should guide an ingredient's supply rate?",
                        "How quickly the process is expected to use it",
                        "The highest rate the supplier allows, regardless of need",
                        "The number of stations placed in the kitchen",
                        "The menu's selling price",
                        'A'),
                    Q("What can happen if ingredients arrive slower than production uses them?",
                        "A process step may run out and become starved",
                        "Finished output must increase",
                        "Customer demand is automatically reduced",
                        "The queue disappears",
                        'A'),
                    Q("Why avoid carrying much more stock than the forecast calls for?",
                        "It ties up money in ingredients that may go unused",
                        "It always lowers station capacity",
                        "It makes demand unpredictable",
                        "It prevents workers from moving items",
                        'A')
                }
            },
            new MilestoneContent
            {
                id = "milestone_04",
                name = "Process Optimization",
                description = "Improve one product line and check how the change affects output across the kitchen.",
                topics = "Shared resources, throughput trade-offs, customer service",
                missions = new[]
                {
                    M("serve_super_burgers", "Serve 216 Super Burgers",
                        "Serve at least 216 Super Burgers this shift, 90% of the 240-item daily demand.", null, 216,
                        missionIdOverride: "milestone_04_serve_super_burgers",
                        deliveredItemName: "Super Burger"),
                    M("serve_cheese_bacon_fries", "Serve 216 Cheese Bacon Fries",
                        "Serve at least 216 Cheese Bacon Fries this shift, 90% of the 240-item daily demand.", null, 216,
                        missionIdOverride: "milestone_06_serve_cheese_bacon_fries",
                        deliveredItemName: "Cheese Bacon Fries"),
                    M("serve_whipped_cream_sprinkle_shakes", "Serve 216 Whipped Cream Sprinkle Shakes",
                        "Serve at least 216 Whipped Cream Sprinkle Shakes this shift, 90% of the 240-item daily demand.",
                        null, 216,
                        missionIdOverride: "milestone_06_serve_whipped_cream_sprinkle_shakes",
                        deliveredItemName: "Whipped Cream Sprinkle Shake")
                },
                questions = new[]
                {
                    Q("What should you compare after changing a process step?",
                        "Finished output from the whole process",
                        "Only the fastest station rate",
                        "The color of the station card",
                        "The number of recipes in the menu",
                        'A'),
                    Q("Why change one likely cause at a time?",
                        "It helps show which change affected process output",
                        "It guarantees every station reaches maximum capacity",
                        "It makes customer demand stop changing",
                        "It removes the need to measure throughput",
                        'A'),
                    Q("If one station's output rises but delivered throughput stays flat, what should you check?",
                        "Another step may now limit the whole process",
                        "Only the upgraded station's target rate",
                        "Whether the menu has enough categories",
                        "Whether the station looks different",
                        'A')
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
