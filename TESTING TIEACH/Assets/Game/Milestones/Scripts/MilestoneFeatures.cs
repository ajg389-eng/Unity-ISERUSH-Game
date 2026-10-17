/// <summary>
/// Milestones reveal research projects. Completed research grants gameplay unlocks.
/// Tutorial is stage 0; Milestone 1 is the first numbered stage.
/// </summary>
public static class MilestoneFeatures
{
    public const int RestaurantBasics = 1;
    public const int CapacityAndOptimization = 2;
    public const int Mk2Stations = 3;
    public const int AdvancedMenuRecipes = 4;
    public const int QualityAndReliability = 5;
    public const int SystemsIntegration = 6;

    public static int HighestReachedNumberedStage()
    {
        var progress = MilestoneProgressManager.Instance;
        return progress != null ? progress.GetHighestReachedNumberedStage() : 0;
    }

    public static bool HasReached(int numberedStage)
    {
        return numberedStage <= 0 || HighestReachedNumberedStage() >= numberedStage;
    }

    static bool Researched(string id) => ResearchProgressManager.Instance != null
        && ResearchProgressManager.Instance.IsCompleted(id);

    public static bool IsResearchComplete(string id) => Researched(id);

    public static bool IsResearchProjectComplete(int numberedStage) =>
        numberedStage <= 0 || Researched(ResearchIdForStage(numberedStage));

    public static bool ResearchUnlockedForStage(int numberedStage)
    {
        if (numberedStage <= 0) return true;
        ResearchProgressManager research = ResearchProgressManager.Instance;
        if (research == null) return MilestoneProgressManager.Instance == null;
        return IsResearchProjectComplete(numberedStage);
    }

    public static string ResearchIdForStage(int numberedStage) => numberedStage switch
    {
        <= RestaurantBasics => "production_basics",
        CapacityAndOptimization => "station_capacity_1",
        Mk2Stations => "mk2_stations",
        AdvancedMenuRecipes => "recipe_upgrades_4",
        QualityAndReliability => "industrial_kitchen",
        SystemsIntegration => "expanded_capacity",
        _ => "expanded_capacity",
    };

    public static string ResearchTitleForStage(int numberedStage) =>
        ResearchProgressManager.Get(ResearchIdForStage(numberedStage))?.title ?? "Research";

    public static bool ExtraEquipmentUnlocked => Researched("additional_stations") || Researched("station_capacity_1");
    public static bool ExtraStaffingUnlocked => Researched("fourth_worker");
    public static bool ExpertStaffingUnlocked => Researched("sixth_worker");
    public static bool ExtraFlowStaffingUnlocked => Researched("multi_worker_flows");
    public static int MaxWorkersPerFlow
    {
        get
        {
            if (Researched("multi_worker_flows_3")) return 4;
            if (Researched("multi_worker_flows_2")) return 3;
            return ExtraFlowStaffingUnlocked ? 2 : 1;
        }
    }
    public static bool Mk2StationsUnlocked => Researched("mk2_stations");
}
