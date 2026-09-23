/// <summary>
/// Gameplay gates that open when the player reaches a numbered milestone
/// (that stage is active, completed, or already passed).
/// Tutorial is stage 0; Milestone 1 is the first numbered stage.
/// </summary>
public static class MilestoneFeatures
{
    public const int RestaurantBasics = 1;
    public const int CapacityAndOptimization = 2;

    public static int HighestReachedNumberedStage()
    {
        var progress = MilestoneProgressManager.Instance;
        return progress != null ? progress.GetHighestReachedNumberedStage() : 0;
    }

    public static bool HasReached(int numberedStage)
    {
        return numberedStage <= 0 || HighestReachedNumberedStage() >= numberedStage;
    }

    public static bool ExtraEquipmentUnlocked => HasReached(CapacityAndOptimization);
    public static bool ExtraStaffingUnlocked => HasReached(CapacityAndOptimization);
    public static bool RushHourUnlocked => HasReached(CapacityAndOptimization);
    public static bool BottleneckInsightsUnlocked => HasReached(CapacityAndOptimization);
    public static bool ExtraFlowStaffingUnlocked => HasReached(CapacityAndOptimization);
}
