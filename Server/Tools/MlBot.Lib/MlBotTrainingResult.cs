using System.Collections.Generic;

namespace Template.Shared.Tests;

public sealed class MlBotTrainingResult
{
    public int Generation { get; set; }
    public int Episode { get; set; }
    public int Seed { get; set; }
    public float Fitness { get; set; }
    public bool OpenedFinalStructure { get; set; }
    public bool OpenedAllBuildings { get; set; }
    public int Ticks { get; set; }
    public int BuiltCount { get; set; }
    public int RemainingLandCount { get; set; }
    public int RemainingLandCost { get; set; }
    public int Coins { get; set; }
    public int Milk { get; set; }
    public int Food { get; set; }
    public int Cows { get; set; }
    public int Helpers { get; set; }
    public int Pets { get; set; }
    public Dictionary<string, float[]> Weights { get; set; } = new();
}
