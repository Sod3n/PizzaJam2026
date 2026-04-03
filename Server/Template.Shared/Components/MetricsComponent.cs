using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b7e3a1d9-4f2c-48e6-9a1b-5d3c7f8e2a0b")]
public struct MetricsComponent : IComponent
{
    // Timing
    public int ElapsedTicks;

    // Cumulative production (all gains ever)
    public int CumFood;
    public int CumMilk;
    public int CumCoins;

    // Peak values
    public int PeakCoins;
    public int PeakFood;
    public int PeakMilk;
    public int PeakCows;
    public int PeakHouses;

    // Bottleneck tracking (sampled ticks at zero)
    public int FoodZeroTicks;
    public int MilkZeroTicks;
    public int CoinZeroTicks;
    public int SampleCount;

    // Entity counts (updated every 60 ticks)
    public int Houses;
    public int LoveHouses;
    public int SellPoints;
    public int FoodFarms;
    public int Helpers;
    public int Pets;
    public int Cows;
    public int HousedCows;
    public int WildCows;
    public int LandPlots;
    public int TotalLandCost;
    public int FinalStructureBuilt;

    // Internal: previous resource values for cumulative delta tracking
    public int PrevFood;
    public int PrevMilk;
    public int PrevCoins;
}
