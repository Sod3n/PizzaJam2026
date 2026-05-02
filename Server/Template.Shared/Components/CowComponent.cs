// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("fcf83639-f988-e35a-8fcc-1f0ebc71fb9e")]
public struct CowComponent : IComponent
{
    public int Exhaust;
    public int MaxExhaust;
    public bool IsMilking;
    public Entity HouseId;
    public Entity PreviousHouseId; // Saved when entering love house, restored after breeding
    public Vector2 SpawnPosition;
    public Entity FollowingPlayer;
    public Entity FollowTarget; // Entity this cow actually follows (player or previous cow in chain)
    public int PreferredFood; // FoodType constant — primary preferred food
    public int SecondaryPreferredFood; // -1 if none, else a second FoodType this cow also likes
    public int SelectedFood;  // FoodType constant — cow's chosen food, travels with cow between houses
    /// <summary>Bitmask of FoodType bits the player has fully tested (fed >= MaxExhaust units of that food).</summary>
    public int DiscoveredFoodMask;
    public int FedGrassCount;
    public int FedCarrotCount;
    public int FedAppleCount;
    public int FedMushroomCount;
    public bool IsDepressed;  // Depressed after failed breed — hides in house, can't interact until timer expires
    public int DepressionTicksRemaining; // Countdown timer for depression recovery (1800 ticks = 30s at 60 TPS)
    public Entity LoveTarget; // Entity of the cow this cow is in love with (guaranteed upgrade when bred together)
    public bool LoveConfessed; // True after the player has interacted with this love cow and seen the popup
    public Entity ParentA; // First parent entity (Entity.Null for wild/starter cows)
    public Entity ParentB; // Second parent entity (Entity.Null for wild/starter cows)
    public int PetCount;
    public int MilkClickCounter; // Clicks accumulated toward the next milk (4 per milk)

    /// <summary>True if <paramref name="foodType"/> matches this cow's primary or secondary preference.</summary>
    public bool IsFoodPreferred(int foodType) =>
        foodType == PreferredFood || (SecondaryPreferredFood >= 0 && foodType == SecondaryPreferredFood);

    /// <summary>True once the player has fed this cow MaxExhaust units of <paramref name="foodType"/>.</summary>
    public bool IsFoodDiscovered(int foodType) =>
        (DiscoveredFoodMask & (1 << foodType)) != 0;

    public int GetFedCount(int foodType) => foodType switch
    {
        FoodType.Grass => FedGrassCount,
        FoodType.Carrot => FedCarrotCount,
        FoodType.Apple => FedAppleCount,
        FoodType.Mushroom => FedMushroomCount,
        _ => 0,
    };

    /// <summary>Increment per-food fed counter; flips the discovered bit when total ≥ MaxExhaust.</summary>
    public void RecordFed(int foodType)
    {
        switch (foodType)
        {
            case FoodType.Grass: FedGrassCount++; break;
            case FoodType.Carrot: FedCarrotCount++; break;
            case FoodType.Apple: FedAppleCount++; break;
            case FoodType.Mushroom: FedMushroomCount++; break;
            default: return;
        }
        if (GetFedCount(foodType) >= MaxExhaust)
            DiscoveredFoodMask |= (1 << foodType);
    }
}
