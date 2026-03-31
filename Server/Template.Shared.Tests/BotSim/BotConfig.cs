namespace Template.Shared.Tests;

public static class BotConfig
{
    // Movement / timing
    public const float PlayerSpeed = 12f;
    public const int TickRate = 60;
    public const int MinApproachTicks = 3;
    public const int IdleCooldownTicks = 10;

    // Builder quick-check
    public const float BuilderProximity = 10f;
    public const int MinCoinsForBuilder = 5;

    // Scoring multipliers
    public const float FoodValueMultiplier = 3f;
    public const float MilkValueMultiplier = 5f;
    public const float SellValueMultiplier = 2f;
    public const float BuildValueMultiplier = 3f;
    public const float BreedValueMultiplier = 5f;  // scales avg coin yield → breed score
    public const float HelperBreedBonus = 0.1f;   // per-helper bonus to breed value
    public const float TameValue = 10f;

    // Work tick estimates
    public const int MilkSetupTicks = 120;
    public const int BreedWorkTicks = 130;
    public const int BreedFetchWorkTicks = 120;
    public const int TameWorkTicks = 15;

    // Thresholds
    public const int MinCoinsForBuild = 10;
}
