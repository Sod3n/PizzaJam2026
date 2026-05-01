namespace Template.Shared.Components;

public static class FoodType
{
    public const int Grass = 0;
    public const int Carrot = 1;
    public const int Apple = 2;
    public const int Mushroom = 3;

    /// <summary>All food types produce the same general milk product.</summary>
    public static int ToMilkProduct(int foodType) => MilkProduct.Milk;

    /// <summary>Food tiers no longer require prior milk products.</summary>
    public static int PrerequisiteProduct(int foodType) => -1;

    /// <summary>Food preference is a requirement for satisfying the cow, not a milk tier.</summary>
    public static int MaxTier(int preferredFood) => preferredFood;

    /// <summary>
    /// Weighted random food preference. Rarer foods = rarer cows.
    /// Grass ~50%, Carrot ~28%, Apple ~15%, Mushroom ~7%
    /// </summary>
    public static int RandomPreferred(ref Deterministic.GameFramework.Types.DeterministicRandom random)
    {
        int roll = random.NextInt(100);
        if (roll < 50) return Grass;       // 50%
        if (roll < 78) return Carrot;      // 28%
        if (roll < 93) return Apple;       // 15%
        return Mushroom;                   // 7%
    }
}

public static class MilkProduct
{
    public const int Milk = 0;
    public const int CarrotMilkshake = 1;
    public const int VitaminMix = 2;
    public const int PurplePotion = 3;

    /// <summary>Coin value when selling a milk product.</summary>
    public static int CoinValue(int milkProduct) => 1;
}
