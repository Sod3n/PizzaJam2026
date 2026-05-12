using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
public struct GlobalResourcesComponent : IComponent
{
    public int Grass;
    public int Carrot;
    public int Apple;
    public int Mushroom;
    public int Milk;
    public int CarrotMilkshake;
    public int VitaminMix;
    public int PurplePotion;
    public int Coins;
    public int TotalBreedCount; // Global breed counter — used for helper unlock thresholds
    public int SpawnedSpecials; // Bitmask tracking which special land plots have been placed
    public int HelpersEnabled; // 1 = breeding can produce helpers, 0 = always produce cows
    public int LastFarmGX;     // Grid X of last dynamically spawned farm (for angular separation)
    public int LastFarmGY;     // Grid Y of last dynamically spawned farm (for angular separation)

    // Day cycle (advances when player sleeps in Player House)
    public int DayCounter;     // Days elapsed since match start (0 on day 1)
    // Per-day food spawn cap counters — reset to 0 on day advance
    public int FoodSpawnedTodayGrass;
    public int FoodSpawnedTodayCarrot;
    public int FoodSpawnedTodayApple;
    public int FoodSpawnedTodayMushroom;

    // Helper unlock thresholds live in GameData.Balance.Helper.HelperUnlockBreeds — single source of truth.
    public static int GuaranteedMegaBreed => GameData.Balance.Helper.GuaranteedMegaBreed;
    public int HelpersSpawned; // How many helpers have been spawned (index into Balance.Helper.HelperUnlockBreeds)
    public int NextLoveBreedCount; // Breed count at which the next love event triggers (0 = not yet set)
    public int LoveEventTimer; // Countdown ticks until the pending love event fires (0 = no pending event)
    public Entity LoveEventCowTarget; // Player entity to pass to TriggerLoveEvent when timer fires
    public int LoveEventBreedCount; // Breed count to pass to TriggerLoveEvent when timer fires

    /// <summary>Get the per-day spawn count for a food type.</summary>
    public int GetFoodSpawnedToday(int foodType) => foodType switch
    {
        FoodType.Grass => FoodSpawnedTodayGrass,
        FoodType.Carrot => FoodSpawnedTodayCarrot,
        FoodType.Apple => FoodSpawnedTodayApple,
        FoodType.Mushroom => FoodSpawnedTodayMushroom,
        _ => 0
    };

    /// <summary>Increment the per-day spawn count for a food type.</summary>
    public void IncrementFoodSpawnedToday(int foodType)
    {
        switch (foodType)
        {
            case FoodType.Grass: FoodSpawnedTodayGrass++; break;
            case FoodType.Carrot: FoodSpawnedTodayCarrot++; break;
            case FoodType.Apple: FoodSpawnedTodayApple++; break;
            case FoodType.Mushroom: FoodSpawnedTodayMushroom++; break;
        }
    }

    /// <summary>Get the amount of a specific food type.</summary>
    public int GetFood(int foodType)
    {
        return foodType switch
        {
            FoodType.Grass => Grass,
            FoodType.Carrot => Carrot,
            FoodType.Apple => Apple,
            FoodType.Mushroom => Mushroom,
            _ => 0
        };
    }

    /// <summary>Consume 1 unit of a specific food type. Returns false if none available.</summary>
    public bool ConsumeFood(int foodType)
    {
        switch (foodType)
        {
            case FoodType.Grass: if (Grass <= 0) return false; Grass--; return true;
            case FoodType.Carrot: if (Carrot <= 0) return false; Carrot--; return true;
            case FoodType.Apple: if (Apple <= 0) return false; Apple--; return true;
            case FoodType.Mushroom: if (Mushroom <= 0) return false; Mushroom--; return true;
            default: return false;
        }
    }

    /// <summary>Add food resource of the given type.</summary>
    public void AddFood(int foodType, int amount)
    {
        switch (foodType)
        {
            case FoodType.Grass: Grass += amount; break;
            case FoodType.Carrot: Carrot += amount; break;
            case FoodType.Apple: Apple += amount; break;
            case FoodType.Mushroom: Mushroom += amount; break;
        }
    }

    /// <summary>Add milk product of the given type.</summary>
    public void AddMilkProduct(int milkProduct, int amount)
    {
        Milk += amount;
    }

    /// <summary>Get the amount of a specific milk product.</summary>
    public int GetMilkProduct(int milkProduct)
    {
        return milkProduct == MilkProduct.Milk ? Milk : 0;
    }

    /// <summary>Consume 1 unit of a specific milk product. Returns false if none available.</summary>
    public bool ConsumeMilkProduct(int milkProduct)
    {
        if (milkProduct != MilkProduct.Milk || Milk <= 0) return false;
        Milk--;
        return true;
    }

    /// <summary>Check if any food is available.</summary>
    public bool HasAnyFood() => Grass > 0 || Carrot > 0 || Apple > 0 || Mushroom > 0;

    /// <summary>Check if any milk product is available.</summary>
    public bool HasAnyMilkProduct() => Milk > 0;

    /// <summary>Consume 1 of any milk product. Returns true if consumed.</summary>
    public bool ConsumeAnyMilkProduct()
    {
        if (Milk > 0) { Milk--; return true; }
        return false;
    }

    /// <summary>Consume 1 of the most valuable milk product. Returns coin value (0 if none).</summary>
    public int ConsumeAndPriceMilkProduct()
    {
        if (Milk > 0) { Milk--; return MilkProduct.CoinValue(MilkProduct.Milk); }
        return 0;
    }

    /// <summary>
    /// Find food for a cow, preferring its preferred food but allowing lower-tier fallback.
    /// Non-preferred food is less reliable during milking.
    /// </summary>
    public int FindBestFoodForCow(int preferredFood)
    {
        if (GetFood(preferredFood) > 0) return preferredFood;
        if (Grass > 0) return FoodType.Grass;
        if (preferredFood >= FoodType.Carrot && Carrot > 0) return FoodType.Carrot;
        if (preferredFood >= FoodType.Apple && Apple > 0) return FoodType.Apple;
        if (preferredFood >= FoodType.Mushroom && Mushroom > 0) return FoodType.Mushroom;
        return -1;
    }
}
