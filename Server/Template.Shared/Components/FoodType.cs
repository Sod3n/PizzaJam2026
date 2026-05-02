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

    /// <summary>Uniform random food preference — every food type is equally likely (25% each).</summary>
    public static int RandomPreferred(ref Deterministic.GameFramework.Types.DeterministicRandom random)
        => random.NextInt(0, 4);
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
