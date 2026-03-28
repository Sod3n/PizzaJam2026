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
    public int VitaminShake;
    public int AppleYogurt;
    public int PurplePotion;
    public int Coins;

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
        switch (milkProduct)
        {
            case MilkProduct.Milk: Milk += amount; break;
            case MilkProduct.VitaminShake: VitaminShake += amount; break;
            case MilkProduct.AppleYogurt: AppleYogurt += amount; break;
            case MilkProduct.PurplePotion: PurplePotion += amount; break;
        }
    }

    /// <summary>Check if any food is available.</summary>
    public bool HasAnyFood() => Grass > 0 || Carrot > 0 || Apple > 0 || Mushroom > 0;

    /// <summary>Check if any milk product is available.</summary>
    public bool HasAnyMilkProduct() => Milk > 0 || VitaminShake > 0 || AppleYogurt > 0 || PurplePotion > 0;

    /// <summary>Consume 1 of any milk product. Returns true if consumed.</summary>
    public bool ConsumeAnyMilkProduct()
    {
        if (Milk > 0) { Milk--; return true; }
        if (VitaminShake > 0) { VitaminShake--; return true; }
        if (AppleYogurt > 0) { AppleYogurt--; return true; }
        if (PurplePotion > 0) { PurplePotion--; return true; }
        return false;
    }

    /// <summary>
    /// Find the best food to use for a cow, preferring the cow's preferred food.
    /// Returns the food type to consume, or -1 if no food available.
    /// </summary>
    public int FindBestFoodForCow(int preferredFood)
    {
        if (GetFood(preferredFood) > 0) return preferredFood;
        if (Grass > 0) return FoodType.Grass;
        if (Carrot > 0) return FoodType.Carrot;
        if (Apple > 0) return FoodType.Apple;
        if (Mushroom > 0) return FoodType.Mushroom;
        return -1;
    }
}
