// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

public static class LandType
{
    public const int House = 0;
    public const int LoveHouse = 1;
    public const int SellPoint = 2;
    public const int FinalStructure = 3;
    public const int CarrotFarm = 4;
    public const int AppleOrchard = 5;
    public const int MushroomCave = 6;
    public const int HelperAssistant = 7;
    public const int UpgradeGatherer = 8;
    public const int UpgradeBuilder = 9;
    public const int UpgradeSeller = 10;
    public const int UpgradeAssistant = 11; // Late-game: x5 click speed (x10 with HelperAssistant)
    public const int Decoration = 12;       // Stub building — costs coins but doesn't house cows

    /// <summary>Get the FoodType this farm land type produces, or -1 if not a farm.</summary>
    public static int GetFoodType(int landType)
    {
        return landType switch
        {
            CarrotFarm => FoodType.Carrot,
            AppleOrchard => FoodType.Apple,
            MushroomCave => FoodType.Mushroom,
            _ => -1
        };
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("861f6742-9fbc-055e-b43d-b0f04d1b057f")]
public struct LandComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
    public int Type;
    public int Arm;    // 0-4: which star arm
    public int Ring;   // 0 = innermost, higher = further out
    public int Locked; // 1 = hidden/non-interactable, 0 = unlocked
}
