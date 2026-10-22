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

    public static bool ExtraStaffingUnlocked => Researched("fourth_worker");
    public static bool ExpertStaffingUnlocked => Researched("sixth_worker");
    public static bool Mk2StationsUnlocked => Researched("mk2_stations");
}
