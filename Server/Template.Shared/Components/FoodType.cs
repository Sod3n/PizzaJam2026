namespace Template.Shared.Components;

public static class FoodType
{
    public const int Grass = 0;
    public const int Carrot = 1;
    public const int Apple = 2;
    public const int Mushroom = 3;

    /// <summary>
    /// Get the milk product type produced by this food type.
    /// Grass → Milk, Carrot → VitaminShake, Apple → AppleYogurt, Mushroom → PurplePotion
    /// </summary>
    public static int ToMilkProduct(int foodType) => foodType; // Same mapping: 0→0, 1→1, 2→2, 3→3

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
    public const int VitaminShake = 1;
    public const int AppleYogurt = 2;
    public const int PurplePotion = 3;

    /// <summary>Coin value when selling a milk product.</summary>
    public static int CoinValue(int milkProduct) => milkProduct switch
    {
        PurplePotion => 100,
        AppleYogurt => 10,
        VitaminShake => 3,
        _ => 1
    };
}
