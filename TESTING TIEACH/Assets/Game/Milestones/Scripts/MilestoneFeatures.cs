/// <summary>
/// Gameplay gates that open when the player reaches a numbered milestone
/// (that stage is active, completed, or already passed).
/// Tutorial is stage 0; Milestone 1 is the first numbered stage.
/// </summary>
public static class MilestoneFeatures
{
    public const int RestaurantBasics = 1;
    public const int CapacityAndOptimization = 2;
    public const int Mk2Stations = 3;
    public const int AdvancedMenuRecipes = 4;

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

    public static bool ExtraEquipmentUnlocked => HasReached(CapacityAndOptimization)
        && Researched("additional_stations");
    public static bool ExtraStaffingUnlocked => HasReached(CapacityAndOptimization)
        && Researched("fourth_worker");
    public static bool RushHourUnlocked => HasReached(CapacityAndOptimization);
    public static bool BottleneckInsightsUnlocked => HasReached(CapacityAndOptimization)
        && Researched("bottleneck_insights");
    public static bool ExtraFlowStaffingUnlocked => HasReached(CapacityAndOptimization)
        && Researched("multi_worker_flows");
    public static bool Mk2StationsUnlocked => HasReached(Mk2Stations)
        && Researched("mk2_stations");
}
