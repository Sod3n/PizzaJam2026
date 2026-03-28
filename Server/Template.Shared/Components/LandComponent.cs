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
