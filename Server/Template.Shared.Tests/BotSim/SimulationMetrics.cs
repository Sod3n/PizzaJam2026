using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Tests;

public class SimulationMetrics
{
    public int BotCount;
    public int SessionEndTick = -1;
    public int TotalTicks;

    public readonly List<Snapshot> Snapshots = new();
    public readonly List<BuildEvent> Buildings = new();

    // Change detection for buildings
    private readonly Dictionary<string, int> _prevCounts = new();
    private bool _prevFinalStructure;

    public class Snapshot
    {
        public int Tick;
        // Current values (what's in storage right now)
        public int Grass, Carrot, Apple, Mushroom;
        public int Milk, CarrotMilkshake, VitaminMix, PurplePotion;
        public int Coins;
        public int Houses, LoveHouses, SellPoints, FoodFarms, Helpers, Pets;
        public int Cows, HousedCows, FollowingCows, WildCows;
        public int LandPlots, TotalLandRemaining;
        public bool FinalStructureExists;
        // Cumulative totals (all gains up to this tick)
        public int CumFood, CumMilk, CumCoins;

        public int TotalFood => Grass + Carrot + Apple + Mushroom;
        public int TotalMilk => Milk + CarrotMilkshake + VitaminMix + PurplePotion;
        public int TotalBuildings => Houses + LoveHouses + SellPoints + FoodFarms;
    }

    public class BuildEvent
    {
        public int Tick;
        public string Type;
        public int Count;
    }

    /// <summary>
    /// Record a snapshot by reading from MetricsComponent (entity counts, cumulative totals)
    /// and GlobalResourcesComponent (per-type resource values).
    /// </summary>
    public void RecordSnapshot(Game game, int tick)
    {
        var s = new Snapshot { Tick = tick };

        // Per-type resource values from GlobalResourcesComponent (cheap — single entity read)
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            var r = game.State.GetComponent<GlobalResourcesComponent>(e);
            s.Grass = r.Grass; s.Carrot = r.Carrot; s.Apple = r.Apple; s.Mushroom = r.Mushroom;
            s.Milk = r.Milk; s.CarrotMilkshake = r.CarrotMilkshake; s.VitaminMix = r.VitaminMix; s.PurplePotion = r.PurplePotion;
            s.Coins = r.Coins;
            break;
        }

        // Entity counts, cumulative totals from MetricsComponent (already computed by MetricsSystem)
        foreach (var e in game.State.Filter<MetricsComponent>())
        {
            var m = game.State.GetComponent<MetricsComponent>(e);
            s.Houses = m.Houses;
            s.LoveHouses = m.LoveHouses;
            s.SellPoints = m.SellPoints;
            s.FoodFarms = m.FoodFarms;
            s.Helpers = m.Helpers;
            s.Pets = m.Pets;
            s.Cows = m.Cows;
            s.HousedCows = m.HousedCows;
            s.WildCows = m.WildCows;
            s.FollowingCows = m.Cows - m.HousedCows - m.WildCows;
            s.LandPlots = m.LandPlots;
            s.TotalLandRemaining = m.TotalLandCost;
            s.FinalStructureExists = m.FinalStructureBuilt != 0;
            s.CumFood = m.CumFood;
            s.CumMilk = m.CumMilk;
            s.CumCoins = m.CumCoins;
            break;
        }

        Snapshots.Add(s);

        // Detect building events
        DetectBuildEvents(s, tick);
    }

    private void DetectBuildEvents(Snapshot s, int tick)
    {
        CheckBuild("House", s.Houses, tick);
        CheckBuild("LoveHouse", s.LoveHouses, tick);
        CheckBuild("SellPoint", s.SellPoints, tick);
        CheckBuild("FoodFarm", s.FoodFarms, tick);
        CheckBuild("Helper", s.Helpers, tick);
        CheckBuild("Pet/Upgrade", s.Pets, tick);

        if (s.FinalStructureExists && !_prevFinalStructure)
            Buildings.Add(new BuildEvent { Tick = tick, Type = "FINAL STRUCTURE", Count = 1 });
        _prevFinalStructure = s.FinalStructureExists;
    }

    private void CheckBuild(string type, int current, int tick)
    {
        _prevCounts.TryGetValue(type, out int prev);
        if (current > prev)
            Buildings.Add(new BuildEvent { Tick = tick, Type = type, Count = current - prev });
        _prevCounts[type] = current;
    }

    public string GenerateReport(List<BotBrain> bots)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("╔═══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║         GAME SIMULATION METRICS REPORT                   ║");
        sb.AppendLine("╚═══════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        float endTick = SessionEndTick > 0 ? SessionEndTick : TotalTicks;
        float seconds = endTick / 60f;
        float minutes = seconds / 60f;

        sb.AppendLine("── Session Summary ──");
        sb.AppendLine($"  Bot Count:       {BotCount}");
        sb.AppendLine($"  Duration:        {endTick:F0} ticks  ({minutes:F1} min / {seconds:F0} sec at 60fps)");
        sb.AppendLine($"  Completed:       {(SessionEndTick > 0 ? "YES — Final Structure built" : "NO — timed out")}");
        sb.AppendLine();

        // Bot stats
        sb.AppendLine("── Bot Stats ──");
        for (int i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];
            sb.AppendLine($"  Bot {i}: MilkClicks={bot.TotalMilkClicks}  {bot.ActionStats()}");
        }
        sb.AppendLine();

        // Final state
        var final_ = Snapshots.LastOrDefault();
        if (final_ != null)
        {
            sb.AppendLine("── Final Game State ──");
            sb.AppendLine($"  Food:      Grass={final_.Grass} Carrot={final_.Carrot} Apple={final_.Apple} Mushroom={final_.Mushroom} (total={final_.TotalFood})");
            sb.AppendLine($"  Products:  Milk={final_.Milk} CarrotShake={final_.CarrotMilkshake} VitaMix={final_.VitaminMix} PurplePot={final_.PurplePotion} (total={final_.TotalMilk})");
            sb.AppendLine($"  Coins:     {final_.Coins}");
            sb.AppendLine($"  Buildings: Houses={final_.Houses} LoveHouses={final_.LoveHouses} SellPoints={final_.SellPoints} FoodFarms={final_.FoodFarms}");
            sb.AppendLine($"  Helpers:   {final_.Helpers}");
            sb.AppendLine($"  Cows:      Total={final_.Cows} Housed={final_.HousedCows} Following={final_.FollowingCows} Wild={final_.WildCows}");
            sb.AppendLine($"  Land:      {final_.LandPlots} plots remaining (cost: {final_.TotalLandRemaining} coins)");
            sb.AppendLine();
        }

        // Building timeline
        sb.AppendLine("── Building Timeline ──");
        sb.AppendLine($"  {"Time",-12} {"Type",-18} {"Count",-6}");
        sb.AppendLine($"  {"----",-12} {"----",-18} {"-----",-6}");
        foreach (var evt in Buildings)
        {
            float sec = evt.Tick / 60f;
            float min = sec / 60f;
            string time = min >= 1 ? $"{min:F1}m" : $"{sec:F0}s";
            sb.AppendLine($"  {time,-12} {evt.Type,-18} x{evt.Count}");
        }
        sb.AppendLine();

        // Resource timeline
        sb.AppendLine("── Resource Timeline ──");
        sb.AppendLine($"  {"Time",-10} {"Coins",-8} {"Food",-8} {"Milk",-8} {"Houses",-8} {"Cows",-8} {"Land",-8} {"LandCost",-10}");
        sb.AppendLine($"  {"----",-10} {"-----",-8} {"----",-8} {"----",-8} {"------",-8} {"----",-8} {"----",-8} {"--------",-10}");

        // Sample at reasonable intervals
        int sampleIntervalTicks = Math.Max(60, (int)(endTick / 30)); // ~30 samples
        Snapshot prev = null;
        foreach (var snap in Snapshots)
        {
            bool isLast = snap == Snapshots.Last();
            bool isSample = prev == null || snap.Tick - prev.Tick >= sampleIntervalTicks;
            if (!isSample && !isLast) continue;

            float min = snap.Tick / 3600f;
            string time = min >= 1 ? $"{min:F1}m" : $"{snap.Tick / 60f:F0}s";
            sb.AppendLine($"  {time,-10} {snap.Coins,-8} {snap.TotalFood,-8} {snap.TotalMilk,-8} {snap.Houses,-8} {snap.Cows,-8} {snap.LandPlots,-8} {snap.TotalLandRemaining,-10}");
            prev = snap;
        }
        sb.AppendLine();

        // Economy analysis
        if (Snapshots.Count >= 2)
        {
            sb.AppendLine("── Economy Analysis ──");

            // Find peak values
            int peakCoins = Snapshots.Max(s => s.Coins);
            int peakFood = Snapshots.Max(s => s.TotalFood);
            int peakMilk = Snapshots.Max(s => s.TotalMilk);
            int peakCows = Snapshots.Max(s => s.Cows);
            int peakHouses = Snapshots.Max(s => s.Houses);

            sb.AppendLine($"  Peak Coins:    {peakCoins}");
            sb.AppendLine($"  Peak Food:     {peakFood}");
            sb.AppendLine($"  Peak Milk:     {peakMilk}");
            sb.AppendLine($"  Peak Cows:     {peakCows}");
            sb.AppendLine($"  Peak Houses:   {peakHouses}");
            sb.AppendLine();

            // Bottleneck detection: find when resources hit 0
            int foodZeroTicks = Snapshots.Count(s => s.TotalFood == 0);
            int milkZeroTicks = Snapshots.Count(s => s.TotalMilk == 0);
            int coinZeroTicks = Snapshots.Count(s => s.Coins == 0);
            int total = Snapshots.Count;

            sb.AppendLine("  Bottleneck (% of time at 0):");
            sb.AppendLine($"    Food:   {100.0 * foodZeroTicks / total:F0}%");
            sb.AppendLine($"    Milk:   {100.0 * milkZeroTicks / total:F0}%");
            sb.AppendLine($"    Coins:  {100.0 * coinZeroTicks / total:F0}%");
            sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    public string ExportCsv()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Tick,Seconds,Minutes,Grass,Carrot,Apple,Mushroom,TotalFood,Milk,CarrotMilkshake,VitaminMix,PurplePotion,TotalMilk,Coins,CumFood,CumMilk,CumCoins,Houses,LoveHouses,SellPoints,FoodFarms,TotalBuildings,Helpers,Cows,HousedCows,FollowingCows,WildCows,LandPlots,TotalLandRemaining,FinalStructure");
        foreach (var s in Snapshots)
        {
            sb.AppendLine(string.Format(ci,
                "{0},{1:F1},{2:F2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29}",
                s.Tick, s.Tick / 60f, s.Tick / 3600f,
                s.Grass, s.Carrot, s.Apple, s.Mushroom, s.TotalFood,
                s.Milk, s.CarrotMilkshake, s.VitaminMix, s.PurplePotion, s.TotalMilk,
                s.Coins,
                s.CumFood, s.CumMilk, s.CumCoins,
                s.Houses, s.LoveHouses, s.SellPoints, s.FoodFarms, s.TotalBuildings, s.Helpers,
                s.Cows, s.HousedCows, s.FollowingCows, s.WildCows,
                s.LandPlots, s.TotalLandRemaining,
                s.FinalStructureExists ? 1 : 0));
        }
        return sb.ToString();
    }

    public string ExportBuildingsCsv()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Tick,Seconds,Minutes,Type,Count");
        foreach (var b in Buildings)
        {
            sb.AppendLine(string.Format(ci, "{0},{1:F1},{2:F2},{3},{4}",
                b.Tick, b.Tick / 60f, b.Tick / 3600f, b.Type, b.Count));
        }
        return sb.ToString();
    }
}
