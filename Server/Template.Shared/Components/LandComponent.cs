// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

public enum LandType
{
    House = 0,
    LoveHouse = 1,
    SellPoint = 2,
    FinalStructure = 3,
    CarrotFarm = 4,
    AppleOrchard = 5,
    MushroomCave = 6,
    HelperAssistant = 7,
    Decoration = 12,
    Warehouse = 13,
    PlayerHouse = 14,
    Library = 15,
}

public static class LandTypes
{
    /// <summary>Get the FoodType this farm land type produces, or -1 if not a farm.</summary>
    public static int GetFoodType(LandType landType) => landType switch
    {
        LandType.CarrotFarm => FoodType.Carrot,
        LandType.AppleOrchard => FoodType.Apple,
        LandType.MushroomCave => FoodType.Mushroom,
        _ => -1
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("861f6742-9fbc-055e-b43d-b0f04d1b057f")]
public struct LandComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
    public LandType Type;
    public int Arm;    // 0-4: which star arm
    public int Ring;   // 0 = innermost, higher = further out
    public int Locked; // 1 = hidden/non-interactable, 0 = unlocked
}
