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
}

public static class MilkProduct
{
    public const int Milk = 0;
    public const int VitaminShake = 1;
    public const int AppleYogurt = 2;
    public const int PurplePotion = 3;
}
