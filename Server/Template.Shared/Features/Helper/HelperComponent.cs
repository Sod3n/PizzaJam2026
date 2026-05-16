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
    public const int Milker = 4;
}

public static class HelperState
{
    public const int Idle = 0;
    public const int SeekingTarget = 1;
    public const int MovingToTarget = 2;
    public const int Working = 3;
    public const int Returning = 4;
    public const int Depositing = 5;
    public const int WaitingForPickup = 6;
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
    public int BagCarrotMilkshake;
    public int BagVitaminMix;
    public int BagPurplePotion;
    public int BagCoins;
    public int BagCapacity;

    /// <summary>
    /// The food type the milker helper wants from the player before it will go milk.
    /// -1 means no food requested. Set when milker identifies a house target.
    /// </summary>
    public int WantedFoodType;

    // Set true by HelperSystem when the helper is carried or its owner is hidden;
    // per-type/warehouse systems early-exit on this. Reset at the start of each tick.
    public bool SuppressTickUpdate;

    // Breeding lineage — which cows were bred to produce this helper
    public Entity ParentA;
    public Entity ParentB;

    public int PetCount;

    // View-state flags driven by the role systems each tick. The view layer subscribes
    // to these so it never needs to re-derive role/idle conditions on the client.
    //   IsAsking  — helper is idle and the player has the resource it wants (show WantIcon)
    //   IsSleeping — helper has nothing useful to do (show zZz)
    // Exactly one of these is true at a time when the helper is idle; both are false when
    // the helper is actively working.
    public bool IsAsking;
    public bool IsSleeping;
    public bool IsReadyForPickup;
    public bool IsFollowingPlayer;

    public int GetBagTotal() => BagGrass + BagCarrot + BagApple + BagMushroom
                              + BagMilk + BagCarrotMilkshake + BagVitaminMix + BagPurplePotion
                              + BagCoins;

    public bool IsBagFull() => GetBagTotal() >= BagCapacity;

    public int GetFoodTotal() => BagGrass + BagCarrot + BagApple + BagMushroom;
    public int GetMilkTotal() => BagMilk;
    public bool HasAnyResources() => GetBagTotal() > 0;

    /// <summary>Get the amount of a specific food type in the bag.</summary>
    public int GetBagFood(int foodType)
    {
        return foodType switch
        {
            FoodType.Grass => BagGrass,
            FoodType.Carrot => BagCarrot,
            FoodType.Apple => BagApple,
            FoodType.Mushroom => BagMushroom,
            _ => 0
        };
    }

    /// <summary>Consume 1 unit of a specific food type from the bag. Returns false if none available.</summary>
    public bool ConsumeBagFood(int foodType)
    {
        switch (foodType)
        {
            case FoodType.Grass: if (BagGrass <= 0) return false; BagGrass--; return true;
            case FoodType.Carrot: if (BagCarrot <= 0) return false; BagCarrot--; return true;
            case FoodType.Apple: if (BagApple <= 0) return false; BagApple--; return true;
            case FoodType.Mushroom: if (BagMushroom <= 0) return false; BagMushroom--; return true;
            default: return false;
        }
    }

    /// <summary>Get the amount of a specific milk product in the bag.</summary>
    public int GetBagMilkProduct(int milkProduct)
    {
        return milkProduct == MilkProduct.Milk ? BagMilk : 0;
    }

    /// <summary>Consume 1 unit of a specific milk product from the bag. Returns false if none available.</summary>
    public bool ConsumeBagMilkProduct(int milkProduct)
    {
        if (milkProduct != MilkProduct.Milk || BagMilk <= 0) return false;
        BagMilk--;
        return true;
    }

    /// <summary>Add milk product to the bag.</summary>
    public void AddBagMilkProduct(int milkProduct, int amount)
    {
        BagMilk += amount;
    }

    /// <summary>Add food of a specific type to the bag.</summary>
    public void AddBagFood(int foodType, int amount)
    {
        switch (foodType)
        {
            case FoodType.Grass: BagGrass += amount; break;
            case FoodType.Carrot: BagCarrot += amount; break;
            case FoodType.Apple: BagApple += amount; break;
            case FoodType.Mushroom: BagMushroom += amount; break;
        }
    }
}
