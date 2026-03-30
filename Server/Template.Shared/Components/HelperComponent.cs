using System.Runtime.InteropServices;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

public static class HelperType
{
    public const int Assistant = 0;
    public const int Gatherer = 1;
    public const int Seller = 2;
    public const int Builder = 3;
}

public static class HelperState
{
    public const int Idle = 0;
    public const int SeekingTarget = 1;
    public const int MovingToTarget = 2;
    public const int Working = 3;
    public const int Returning = 4;
    public const int Depositing = 5;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("d1e2f3a4-b5c6-4d7e-8f9a-0b1c2d3e4f5a")]
public struct HelperComponent : IComponent
{
    public int Type;
    public Entity OwnerPlayer;
    public int State;
    public Entity TargetEntity;
    public int WorkTimer;
    public int WorkDuration;

    // Resource bag
    public int BagGrass;
    public int BagCarrot;
    public int BagApple;
    public int BagMushroom;
    public int BagMilk;
    public int BagVitaminShake;
    public int BagAppleYogurt;
    public int BagPurplePotion;
    public int BagCoins;
    public int BagCapacity;

    public int GetBagTotal() => BagGrass + BagCarrot + BagApple + BagMushroom
                              + BagMilk + BagVitaminShake + BagAppleYogurt + BagPurplePotion
                              + BagCoins;

    public bool IsBagFull() => GetBagTotal() >= BagCapacity;
}
