using System.Runtime.InteropServices;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b8d4e6f1-2a3c-4d5e-9f7a-8b0c1d2e3f4a")]
public struct HelperPlayerComponent : IComponent
{
    public int Type;
    public int State;

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

    public int WantedFoodType;

    public int GetBagTotal() => BagGrass + BagCarrot + BagApple + BagMushroom
                              + BagMilk + BagCarrotMilkshake + BagVitaminMix + BagPurplePotion
                              + BagCoins;

    public bool IsBagFull() => GetBagTotal() >= BagCapacity;

    public int GetFoodTotal() => BagGrass + BagCarrot + BagApple + BagMushroom;
    public int GetMilkTotal() => BagMilk;
    public bool HasAnyResources() => GetBagTotal() > 0;

    public int GetBagFood(int foodType) => foodType switch
    {
        FoodType.Grass => BagGrass,
        FoodType.Carrot => BagCarrot,
        FoodType.Apple => BagApple,
        FoodType.Mushroom => BagMushroom,
        _ => 0
    };

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

    public int GetBagMilkProduct(int milkProduct)
        => milkProduct == MilkProduct.Milk ? BagMilk : 0;

    public bool ConsumeBagMilkProduct(int milkProduct)
    {
        if (milkProduct != MilkProduct.Milk || BagMilk <= 0) return false;
        BagMilk--;
        return true;
    }

    public void AddBagMilkProduct(int milkProduct, int amount)
    {
        BagMilk += amount;
    }

    public static int CapacityFor(int helperType) => helperType switch
    {
        HelperType.Gatherer => 75,
        HelperType.Seller => 500,
        HelperType.Builder => 500,
        HelperType.Milker => 125,
        _ => 50
    };

    public void ClearBag()
    {
        BagGrass = 0; BagCarrot = 0; BagApple = 0; BagMushroom = 0;
        BagMilk = 0; BagCarrotMilkshake = 0; BagVitaminMix = 0; BagPurplePotion = 0;
        BagCoins = 0;
    }
}
