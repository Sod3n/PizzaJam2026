using System;
using System.Globalization;
using System.IO;
using System.Text;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

/// <summary>
/// Appends CSV rows from MetricsComponent + GlobalResourcesComponent.
/// Usable from both bot simulation and Godot client.
/// </summary>
public class MetricsExporter
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    private const string Header =
        "Tick,Seconds,Minutes," +
        "Grass,Carrot,Apple,Mushroom,TotalFood," +
        "Milk,VitaminShake,AppleYogurt,PurplePotion,TotalMilk," +
        "Coins,CumFood,CumMilk,CumCoins," +
        "Houses,LoveHouses,SellPoints,FoodFarms,Helpers,Pets," +
        "Cows,HousedCows,WildCows," +
        "LandPlots,TotalLandCost," +
        "PeakCoins,PeakFood,PeakMilk,PeakCows,PeakHouses," +
        "FinalStructure";

    private readonly string _filePath;
    private bool _headerWritten;
    private int _lastWrittenTick = -1;

    public string FilePath => _filePath;

    public MetricsExporter(string directory, string prefix = "metrics")
    {
        Directory.CreateDirectory(directory);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CI);
        _filePath = Path.Combine(directory, $"{prefix}_{timestamp}.csv");
    }

    /// <summary>
    /// Read current game state and append a CSV row.
    /// Skips if the tick hasn't changed since last write.
    /// </summary>
    public void WriteSnapshot(EntityWorld state)
    {
        // Find MetricsComponent
        MetricsComponent m = default;
        bool found = false;
        foreach (var e in state.Filter<MetricsComponent>())
        {
            m = state.GetComponent<MetricsComponent>(e);
            found = true;
            break;
        }
        if (!found) return;
        if (m.ElapsedTicks == _lastWrittenTick) return;
        _lastWrittenTick = m.ElapsedTicks;

        // Find GlobalResourcesComponent
        int grass = 0, carrot = 0, apple = 0, mushroom = 0;
        int milk = 0, vitaminShake = 0, appleYogurt = 0, purplePotion = 0;
        int coins = 0;
        foreach (var e in state.Filter<GlobalResourcesComponent>())
        {
            var r = state.GetComponent<GlobalResourcesComponent>(e);
            grass = r.Grass; carrot = r.Carrot; apple = r.Apple; mushroom = r.Mushroom;
            milk = r.Milk; vitaminShake = r.VitaminShake; appleYogurt = r.AppleYogurt; purplePotion = r.PurplePotion;
            coins = r.Coins;
            break;
        }

        int totalFood = grass + carrot + apple + mushroom;
        int totalMilk = milk + vitaminShake + appleYogurt + purplePotion;

        if (!_headerWritten)
        {
            File.WriteAllText(_filePath, Header + Environment.NewLine);
            _headerWritten = true;
        }

        var line = string.Format(CI,
            "{0},{1:F1},{2:F2}," +
            "{3},{4},{5},{6},{7}," +
            "{8},{9},{10},{11},{12}," +
            "{13},{14},{15},{16}," +
            "{17},{18},{19},{20},{21},{22}," +
            "{23},{24},{25}," +
            "{26},{27}," +
            "{28},{29},{30},{31},{32}," +
            "{33}",
            m.ElapsedTicks, m.ElapsedTicks / 60f, m.ElapsedTicks / 3600f,
            grass, carrot, apple, mushroom, totalFood,
            milk, vitaminShake, appleYogurt, purplePotion, totalMilk,
            coins, m.CumFood, m.CumMilk, m.CumCoins,
            m.Houses, m.LoveHouses, m.SellPoints, m.FoodFarms, m.Helpers, m.Pets,
            m.Cows, m.HousedCows, m.WildCows,
            m.LandPlots, m.TotalLandCost,
            m.PeakCoins, m.PeakFood, m.PeakMilk, m.PeakCows, m.PeakHouses,
            m.FinalStructureBuilt);

        File.AppendAllText(_filePath, line + Environment.NewLine);
    }

    /// <summary>
    /// Write a final summary row and return the file path.
    /// </summary>
    public string Finish(EntityWorld state)
    {
        WriteSnapshot(state);
        return _filePath;
    }
}
