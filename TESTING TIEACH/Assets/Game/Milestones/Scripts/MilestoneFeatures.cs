/// <summary>
/// Milestones are the source of gameplay unlocks. Research definitions remain
/// available for future use, but do not gate gameplay while research is disabled.
/// </summary>
using System.Collections.Generic;

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

    static readonly Dictionary<string, int> ResearchUnlockMilestones = new Dictionary<string, int>
    {
        // The first burger is part of Restaurant Basics, which is taught in the tutorial.
        { "production_basics", 0 },
        { "recipe_upgrades_1", 1 },
        { "recipe_upgrades_2", 2 },
        { "recipe_upgrades_3", 3 },
        { "recipe_upgrades_4", 4 },
        { "recipe_upgrades_5", 5 },
        { "mk2_stations", 3 },
        { "core_workforce", 0 },
        { "manual_operations", 0 },
        { "express_ordering", 2 },
        { "automatic_ordering", 5 },
        { "smart_operations", 6 }
    };

    public static bool IsResearchComplete(string id)
    {
        return !string.IsNullOrEmpty(id)
            && ResearchUnlockMilestones.TryGetValue(id, out int milestone)
            && HasReached(milestone);
    }

    public static bool Mk2StationsUnlocked => HasReached(Mk2Stations);

    public static IReadOnlyList<string> GetUnlocksForMilestone(MilestoneDefinition milestone)
    {
        if (milestone == null) return System.Array.Empty<string>();
        switch (milestone.milestoneId)
        {
            case "tutorial":
                return new[] { "Hamburger recipe" };
            case "milestone_01":
                return new[] { "Fries production and menu item", "Shake production and menu item", "Shake Station MK1" };
            case "milestone_02":
                return new[]
                {
                    "Cheeseburger", "Cheese Fries", "Whipped Cream Shake",
                    "Express ingredient ordering"
                };
            case "milestone_03":
                return new[]
                {
                    "Bacon Cheeseburger", "Classic Burger", "MK2 Stations"
                };
            case "milestone_04":
                return new[]
                {
                    "Cheese Bacon Fries", "Whipped Cream Sprinkle Shake"
                };
            case "milestone_05":
                return new[] { "Super Burger", "Automatic supply rates up to 40 items/min" };
            case "milestone_06":
                return new[] { "Integrated Supply Planning: supply rates up to 80 items/min" };
            default:
                return System.Array.Empty<string>();
        }
    }
}
